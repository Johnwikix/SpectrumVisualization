using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

internal static partial class FoliaSourcePlanner
{
    internal sealed record PendoloResult(float[] Mesh, string Parts);
    private static string? _pendoloInput;
    private static PendoloResult? _pendoloResult;

    private static void ClearPendoloResult()
    {
        _pendoloInput = null;
        _pendoloResult = null;
    }

    internal static string CreatePendoloInput(float width, float height, Vector3[]? palette)
    {
        // The original clockwork builder reads only viewport and these theme colors.
        // Lyric/title/seed changes must not invalidate identical mechanical geometry.
        using var input = new MemoryStream();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartObject();
            writer.WriteNumber("width", width);
            writer.WriteNumber("height", height);
            if (palette != null)
            {
                writer.WriteStartObject("theme");
                writer.WriteString("backgroundColor", Hex(palette[0]));
                writer.WriteString("primaryColor", Hex(palette[1]));
                writer.WriteString("accentColor", Hex(palette[3]));
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(input.GetBuffer(), 0, (int)input.Length);
    }

    internal static PendoloResult CompilePendolo(string input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (CacheGate)
            if (_pendoloInput == input && _pendoloResult != null) return _pendoloResult;
        float[]? mesh = null;
        var engine = new Engine(options => options.CancellationToken(token));
        engine.SetValue("nativePendoloMesh", new ClrFunction(engine, "nativePendoloMesh", (_, args) =>
        {
            var values = (JsTypedArray)args[0];
            mesh = new float[checked((int)values.Length)];
            for (int i = 0; i < mesh.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                mesh[i] = (float)values[i].AsNumber();
            }
            return JsValue.Undefined;
        }));
        engine.Execute(Source.Value);
        engine.SetValue("nativeInput", input);
        string parts = engine.Evaluate("FoliaNativeSource.compilePendolo(nativeInput)").AsString();
        token.ThrowIfCancellationRequested();
        var result = new PendoloResult(mesh ?? throw new InvalidOperationException("Missing Pendolo mesh."), parts);
        lock (CacheGate)
        {
            _pendoloInput = input;
            _pendoloResult = result;
        }
        return result;
    }
}
