using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct MachiCamera(Matrix4x4 ViewProjection, Matrix4x4 View,
    Matrix4x4 Projection, Matrix4x4 Inverse);

internal sealed partial class FoliaMachi
{
    private const uint SceneMagic = 0x3448434D;
    private const int MaximumIndices = 500_000 * 3;
    private const float SubtitleReserve = .05f;
    private const float UsableFrustum = 1 - 2 * .02f - SubtitleReserve;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly record struct PackedVertex(
        float X, float Y, float Z,
        short Nx, short Ny, short Nz, ushort U, ushort V,
        byte Material, byte Gain, byte R, byte G, byte B, byte Surface, ushort OverlayU, ushort OverlayV);

    private readonly Vector3 _fitCenter, _fitUp;
    private readonly float _fitWidth, _fitHeight;
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

    private FoliaMachi(BinaryReader reader, CancellationToken cancellationToken, WeatherLayout weather)
    {
        Weather = weather;
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

        Vector3 forward = -OrbitDirection(_azimuth, _elevation);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        _fitUp = Vector3.Cross(right, forward);
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        for (int i = 0; i < points; i++)
        {
            var point = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var projected = new Vector2(Vector3.Dot(point, right), Vector3.Dot(point, _fitUp));
            minimum = Vector2.Min(minimum, projected);
            maximum = Vector2.Max(maximum, projected);
        }
        Vector2 center = (maximum + minimum) * .5f;
        _fitCenter = right * center.X + _fitUp * center.Y;
        Vector2 half = (maximum - minimum) * .5f;
        _fitWidth = half.X;
        _fitHeight = half.Y;

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

        Mesh = ReadVertices(reader.BaseStream, vertexCount, cancellationToken);
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
        return new FoliaMachi(reader, cancellationToken, sceneId switch
        {
            "station" => StationWeather,
            "kowloon" => KowloonWeather,
            _ => KonbiniWeather
        });
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

    internal MachiCamera CreateCamera(float aspect, double ambientTime = 0, float motionWeight = 0)
    {
        float fittedHalfHeight = MathF.Max(_fitHeight, _fitWidth / aspect) / UsableFrustum;
        float overflow = MathF.Max(0, _fitHeight / UsableFrustum - fittedHalfHeight / _zoom);
        Vector3 target = _fitCenter + _fitUp * (overflow * .7f);
        float azimuth = _azimuth;
        float elevation = _elevation;
        float zoom = _zoom;
        if (motionWeight > 0)
        {
            float weight = motionWeight * motionWeight * (3 - 2 * motionWeight);
            const float Degrees = MathF.PI / 180;
            azimuth += Degrees * weight * (15 * Wave(ambientTime, 67, 0) + 5 * Wave(ambientTime, 29, 1.3));
            elevation = Math.Clamp(elevation + Degrees * weight * 4 * Wave(ambientTime, 47, .7), 20 * Degrees, 70 * Degrees);
            zoom = Math.Clamp(zoom * MathF.Exp(.08f * Wave(ambientTime, 41, 2.1) * weight), .75f, 3.6f);
            var panRight = new Vector3(MathF.Cos(_azimuth), 0, -MathF.Sin(_azimuth));
            var panBack = new Vector3(-MathF.Sin(_azimuth), 0, -MathF.Cos(_azimuth));
            target += weight * (.45f * Wave(ambientTime, 53, .4) * panRight + .3f * Wave(ambientTime, 71, 2.6) * panBack);
        }
        Vector3 eye = target + OrbitDirection(azimuth, elevation) * 60;
        float halfHeight = fittedHalfHeight / zoom;
        float halfWidth = halfHeight * aspect;
        float shift = fittedHalfHeight * SubtitleReserve;
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var viewProjection = view * Matrix4x4.CreateOrthographicOffCenter(
            -halfWidth, halfWidth, -halfHeight - shift, halfHeight - shift, Near, Far);
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
            throw new InvalidOperationException("Machi camera projection is singular.");

        // Move the subtitle framing into view-space Y for the SDKs. RELAX
        // reconstructs orthographic world positions along the centered frustum
        // forward axis and classifies the projection by its exact W.
        view.M42 += shift;
        var projection = Matrix4x4.CreateOrthographic(halfWidth * 2, halfHeight * 2, Near, Far);
        return new(viewProjection, view, projection, inverse);
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

    private static Vertex[] ReadVertices(Stream stream, int count, CancellationToken cancellationToken)
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
                            source.OverlayV / 16383.75f - 2, source.Surface));
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
