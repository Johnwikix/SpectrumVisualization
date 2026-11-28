using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct MachiCamera(Matrix4x4 ViewProjection, Matrix4x4 View,
    Matrix4x4 Projection, Matrix4x4 RayBasis, float VerticalFieldOfView, float FocusDistance);

internal sealed partial class FoliaMachi
{
    private const uint SceneMagic = 0x3448434D;
    private const int MaximumIndices = 600_000 * 3;
    private const float HalfFrameHeightMm = 12;
    private static readonly float DefaultHalfFov = MathF.Atan(HalfFrameHeightMm / AppSettings.MachiDefaultFocalLengthMm);
    private const float SubtitleReserve = .05f;
    private const float FramingLimit = .96f;
    private const float MovingFramingBlend = .15f;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly record struct PackedVertex(
        float X, float Y, float Z,
        short Nx, short Ny, short Nz, ushort U, ushort V,
        byte Material, byte Gain, byte R, byte G, byte B, byte Surface, ushort OverlayU, ushort OverlayV);

    private readonly record struct FramingPoint(Vector3 World, Vector3 Authored);

    private readonly Vector3 _fitCenter;
    private readonly FramingPoint[] _framingPoints;
    private readonly float _azimuth, _elevation, _zoom;

    internal readonly record struct Actor(uint FirstIndex, uint IndexCount, uint Kind, Vector3 Pivot, Vector3 Axis);
    internal readonly record struct Light(Vector3 Position, Vector3 Direction, Vector3 Color, float Intensity, float Range, float CosInner, float CosOuter);
    internal Actor[] Actors { get; } = [];
    internal Light[] Lights { get; }
    internal int StaticIndexCount { get; }
    internal uint[] Indices { get; } = [];
    internal byte[] SignPixels { get; } = [];
    internal int SignWidth { get; }
    internal int SignHeight { get; }
    internal float SceneHalf { get; }
    internal WeatherLayout Weather { get; }
    internal SteamEmitter[] Steam { get; }

    private FoliaMachi(BinaryReader reader, CancellationToken cancellationToken, WeatherLayout weather, SteamEmitter[] steam,
        string sceneId)
    {
        Weather = weather;
        Steam = steam;
        if (reader.ReadUInt32() != SceneMagic)
            throw new InvalidDataException("Unsupported Machi scene format.");

        int vertexCount = ReadCount(reader, MaximumIndices);
        int indexCount = ReadCount(reader, MaximumIndices);
        if (indexCount % 3 != 0)
            throw new InvalidDataException("The Machi scene contains an incomplete triangle.");
        SignWidth = ReadCount(reader, 4096);
        SignHeight = ReadCount(reader, 4096);
        int points = ReadCount(reader, 128);
        _azimuth = reader.ReadSingle();
        _elevation = reader.ReadSingle();
        _zoom = reader.ReadSingle();
        SceneHalf = reader.ReadSingle();
        int actorCount = ReadCount(reader, 16, minimum: 0);
        StaticIndexCount = ReadCount(reader, indexCount);
        int lights = ReadCount(reader, 128, minimum: 0);

        Vector3 back = OrbitDirection(_azimuth, _elevation);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, back));
        Vector3 up = Vector3.Cross(back, right);
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        _framingPoints = new FramingPoint[points];
        for (int i = 0; i < points; i++)
        {
            var point = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var local = new Vector3(Vector3.Dot(point, right), Vector3.Dot(point, up), Vector3.Dot(point, back));
            _framingPoints[i] = new(point, local);
            minimum = Vector3.Min(minimum, local);
            maximum = Vector3.Max(maximum, local);
        }
        Vector3 center = (maximum + minimum) * .5f;
        _fitCenter = right * center.X + up * center.Y + back * center.Z;
        for (int i = 0; i < points; i++)
            _framingPoints[i] = _framingPoints[i] with { Authored = _framingPoints[i].Authored - center };

        Actors = new Actor[actorCount];
        uint nextIndex = (uint)StaticIndexCount;
        for (int i = 0; i < Actors.Length; i++)
        {
            uint kind = reader.ReadUInt32();
            uint first = reader.ReadUInt32();
            uint count = reader.ReadUInt32();
            if (kind is < 1 or > 6 || first != nextIndex || count == 0 || count % 3 != 0 || count > indexCount - (long)first)
                throw new InvalidDataException("Invalid Machi actor range.");
            var pivot = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var axis = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Actors[i] = new(first, count, kind, pivot, axis);
            nextIndex += count;
        }
        if (nextIndex != indexCount || StaticIndexCount % 3 != 0)
            throw new InvalidDataException("Machi scene and actor ranges do not cover the triangle buffer.");
        Lights = new Light[lights];
        for (int i = 0; i < Lights.Length; i++)
        {
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var direction = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var color = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Lights[i] = new Light(position, direction, color, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        Mesh = ReadVertices(reader.BaseStream, vertexCount, sceneId, cancellationToken);
        Indices = new uint[indexCount];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(Indices.AsSpan()));
        foreach (uint index in Indices)
            if (index >= vertexCount)
                throw new InvalidDataException("A Machi scene index refers outside the vertex buffer.");
        cancellationToken.ThrowIfCancellationRequested();
        SignPixels = new byte[checked(SignWidth * SignHeight * 4)];
        reader.BaseStream.ReadExactly(SignPixels);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static FoliaMachi Load(string sceneId, CancellationToken cancellationToken = default)
    {
        sceneId = sceneId is "station" or "kowloon" ? sceneId : "konbini";
        cancellationToken.ThrowIfCancellationRequested();
        using Stream stream = typeof(FoliaMachi).Assembly.GetManifestResourceStream($"Spectrum.Folia.Machi.{sceneId}.machi.br")
            ?? throw new InvalidDataException($"The Machi scene '{sceneId}' is missing.");
        using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(brotli);
        var (weather, steam) = sceneId switch
        {
            "station" => (StationWeather, StationSteam),
            "kowloon" => (KowloonWeather, KowloonSteam),
            _ => (KonbiniWeather, KonbiniSteam)
        };
        return new FoliaMachi(reader, cancellationToken, weather, steam, sceneId);
    }

    internal static string ResolveSceneId(string requested, FoliaSong song)
    {
        if (requested is "konbini" or "station" or "kowloon")
            return requested;
        return (Hash("machi-scene:", song.Title, song.Artist) % 3) switch
        {
            1 => "station",
            2 => "kowloon",
            _ => "konbini"
        };
    }

    // Folia's visualizer seed hash uses UTF-16 code units with wrapping arithmetic.
    private static uint Hash(string prefix, string title, string artist)
    {
        uint hash = 2166136261;
        foreach (char code in prefix)
            hash = unchecked((hash ^ code) * 16777619u);
        foreach (char code in title)
            hash = unchecked((hash ^ code) * 16777619u);
        hash = unchecked((hash ^ '|') * 16777619u);
        foreach (char code in artist)
            hash = unchecked((hash ^ code) * 16777619u);
        return hash;
    }

    internal MachiCamera CreateCamera(float aspect, double ambientTime = 0, float motionWeight = 0,
        int focalLengthMm = AppSettings.MachiDefaultFocalLengthMm)
    {
        Vector3 target = _fitCenter;
        float azimuth = _azimuth;
        float elevation = _elevation;
        float dolly = 1;
        float weight = motionWeight * motionWeight * (3 - 2 * motionWeight);
        if (motionWeight > 0)
        {
            const float Degrees = MathF.PI / 180;
            azimuth += Degrees * weight * (15 * Wave(ambientTime, 67, 0) + 5 * Wave(ambientTime, 29, 1.3));
            elevation = Math.Clamp(elevation + Degrees * weight * 4 * Wave(ambientTime, 47, .7), 20 * Degrees, 70 * Degrees);
            dolly = MathF.Exp(-.08f * Wave(ambientTime, 41, 2.1) * weight);
            var panRight = new Vector3(MathF.Cos(_azimuth), 0, -MathF.Sin(_azimuth));
            var panBack = new Vector3(-MathF.Sin(_azimuth), 0, -MathF.Cos(_azimuth));
            target += weight * (.45f * Wave(ambientTime, 53, .4) * panRight + .3f * Wave(ambientTime, 71, 2.6) * panBack);
        }

        // The 24 mm height of a 35 mm frame sets the vertical angle at any aspect.
        float tanHalfFov = HalfFrameHeightMm / focalLengthMm;
        float halfFov = MathF.Atan(tanHalfFov);
        // A wider lens adds room below the set while keeping the top edge aimed
        // into the modeled district, even at the shallowest point of the orbit.
        elevation += MathF.Max(0, halfFov - DefaultHalfFov);
        Vector3 back = OrbitDirection(azimuth, elevation);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, back));
        Vector3 up = Vector3.Cross(back, right);

        // Fit the set at the default lens. Move the camera partway toward the fit
        // for other lenses so they change both perspective and composition.
        float framingTanHalfFov = HalfFrameHeightMm / MathF.Sqrt(AppSettings.MachiDefaultFocalLengthMm * focalLengthMm);
        float verticalFit = 1 / (framingTanHalfFov * (FramingLimit - SubtitleReserve) * _zoom);
        float horizontalFit = verticalFit / aspect;
        float horizontalLimit = 1 / (framingTanHalfFov * FramingLimit * aspect);
        float minimumDistance = Near;
        float baselineDistance = Near;
        float blend = MovingFramingBlend * weight;
        Span<float> constraints = blend > 0 ? stackalloc float[3 * _framingPoints.Length + 2] : [];
        int constraintCount = 0;
        foreach (ref readonly FramingPoint point in _framingPoints.AsSpan())
        {
            Vector3 authored = point.Authored;
            float fitted = authored.Z + MathF.Max(MathF.Abs(authored.X) * horizontalFit, MathF.Abs(authored.Y) * verticalFit);
            baselineDistance = MathF.Max(baselineDistance, fitted);
            if (point.World.Y <= 0) continue;

            Vector3 offset = point.World - target;
            float x = Vector3.Dot(offset, right);
            float y = Vector3.Dot(offset, up) / framingTanHalfFov;
            float z = Vector3.Dot(offset, back);
            float horizontal = z + MathF.Abs(x) * horizontalLimit;
            float top = (z * FramingLimit + y) / (FramingLimit - SubtitleReserve);
            float bottom = (z * FramingLimit - y) / (FramingLimit + SubtitleReserve);
            minimumDistance = MathF.Max(minimumDistance, MathF.Max(horizontal, MathF.Max(top, bottom)));
            if (blend > 0)
            {
                constraints[constraintCount++] = horizontal;
                constraints[constraintCount++] = top;
                constraints[constraintCount++] = bottom;
            }
        }

        float authoredDistance = baselineDistance * dolly;
        float distance = MathF.Max(authoredDistance, minimumDistance);
        if (blend > 0)
        {
            constraints[constraintCount++] = authoredDistance;
            constraints[constraintCount++] = Near;
            // Smooth the winning constraint as the camera moves. Referencing the exact
            // maximum keeps each exponential in range and the result keeps all margins.
            // Even together, terms below this cutoff are smaller than float precision.
            float cutoff = distance - 24 * blend;
            float sum = 0;
            foreach (float constraint in constraints[..constraintCount])
                if (constraint >= cutoff)
                    sum += MathF.Exp((constraint - distance) / blend);
            distance += blend * MathF.Log(sum);
        }
        // Lower the optical axis so the set sits slightly above the subtitle area.
        target -= up * (distance * tanHalfFov * SubtitleReserve);
        Vector3 eye = target + back * distance;
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        float verticalFieldOfView = 2 * halfFov;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(verticalFieldOfView, aspect, Near, Far);
        var viewProjection = view * projection;
        // Screen-space axes for a world-space ray with unit forward depth. The W
        // column supplies device-depth coefficients and the near clipping distance.
        float invProjectionX = 1 / projection.M11, invProjectionY = 1 / projection.M22;
        var rayBasis = new Matrix4x4(
            view.M11 * invProjectionX, view.M21 * invProjectionX, view.M31 * invProjectionX, projection.M33,
            view.M12 * invProjectionY, view.M22 * invProjectionY, view.M32 * invProjectionY, projection.M43,
            -view.M13, -view.M23, -view.M33, Near,
            eye.X, eye.Y, eye.Z, 0);
        return new(viewProjection, view, projection, rayBasis, verticalFieldOfView, distance);
    }

    private static Vector3 OrbitDirection(float azimuth, float elevation) =>
        new(MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(azimuth) * MathF.Cos(elevation));

    private static float Wave(double time, double period, double phase) =>
        (float)Math.Sin(time * (2 * Math.PI / period) + phase);

    private static int ReadCount(BinaryReader reader, int limit, int minimum = 1)
    {
        int value = reader.ReadInt32();
        return value >= minimum && value <= limit ? value : throw new InvalidDataException("Invalid Machi scene size.");
    }

    private static Vertex[] ReadVertices(Stream stream, int count, string sceneId, CancellationToken cancellationToken)
    {
        const int BlockVertices = 2048;
        const int Stride = 32;
        var result = new Vertex[count];
        Span<float> linear = stackalloc float[256];
        for (int i = 0; i < linear.Length; i++)
            linear[i] = ToLinear((byte)i);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockVertices * Stride);
        try
        {
            for (int start = 0; start < result.Length; start += BlockVertices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = Math.Min(BlockVertices, result.Length - start);
                Span<byte> bytes = buffer.AsSpan(0, length * Stride);
                stream.ReadExactly(bytes);
                ReadOnlySpan<PackedVertex> vertices = MemoryMarshal.Cast<byte, PackedVertex>(bytes);
                for (int i = 0; i < vertices.Length; i++)
                {
                    ref readonly PackedVertex source = ref vertices[i];
                    bool fixture = source.Material == 11;
                    result[start + i] = new Vertex(
                        new(source.X, source.Y, source.Z, source.Material),
                        new(linear[source.R], linear[source.G], linear[source.B], source.Gain / 16f),
                        new(source.Nx / 32767f, source.Ny / 32767f, source.Nz / 32767f,
                            fixture ? (float)BitConverter.UInt16BitsToHalf(source.U) :
                                source.Material == 12 ? source.U : source.U / 65535f),
                        new(fixture ? (float)BitConverter.UInt16BitsToHalf(source.V) : source.V / 65535f,
                            fixture ? (float)BitConverter.UInt16BitsToHalf(source.OverlayU) : source.OverlayU / 16383.75f - 2,
                            source.Material is 5 or 11 ? FindNeonBand(sceneId, in source) : source.OverlayV / 16383.75f - 2,
                            source.Surface));
                }
            }
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static float ToLinear(byte source)
    {
        float srgb = source / 255f;
        return srgb <= .04045f ? srgb / 12.92f : MathF.Pow((srgb + .055f) / 1.055f, 2.4f);
    }
}
