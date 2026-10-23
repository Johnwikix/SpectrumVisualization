using System.Collections.Generic;
using System.Text;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Runs the original eight font-fit trials without creating eight interpreter layout graphs.</summary>
internal static class FoliaFumeFontSearch
{
    private readonly record struct Advance(double Width, bool NewLine);

    internal static void Install(Engine engine, CancellationToken token)
    {
        // Widths belong to this compilation only; no song or engine escapes into a global cache.
        var lines = new Dictionary<string, Advance[]>();
        engine.SetValue("nativeFumeFont", new ClrFunction(engine, "nativeFumeFont", (_, args) =>
        {
            token.ThrowIfCancellationRequested();
            string text = args[0].AsString();
            if (!lines.TryGetValue(text, out var advances))
            {
                var values = new List<Advance>();
                var widths = args[6].AsObject();
                foreach (var rune in text.EnumerateRunes())
                {
                    var value = widths.Get(rune.ToString());
                    values.Add(new(value.IsUndefined() || value.IsNull() ? .6 : value.AsNumber(), rune.Value == '\n'));
                }
                lines.Add(text, advances = values.ToArray());
            }
            double width = args[1].AsNumber();
            bool hero = args[2].AsBoolean();
            double scale = args[3].AsNumber(), density = args[4].AsNumber(), heroScale = args[5].AsNumber();
            double low = hero ? 18 : 10, high = hero ? 58 : 30, best = 0;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                double size = (low + high) / 2 * scale * density * (hero ? heroScale : 1);
                if (FitsSingleLine(advances, size, width))
                {
                    best = size;
                    low = (low + high) / 2;
                }
                else high = (low + high) / 2;
            }
            return best > 0 ? best : (hero ? 18 : 10) * scale * density * (hero ? heroScale : 1);
        }));
    }

    private static bool FitsSingleLine(Advance[] advances, double size, double width)
    {
        int start = 0, completed = 0;
        double advance = 0;
        for (int i = 0; i < advances.Length; i++)
        {
            if (advances[i].NewLine)
            {
                if (++completed > 1) return false;
                start = i + 1;
                advance = 0;
                continue;
            }
            double next = advances[i].Width * size;
            if (i > start && advance + next > width)
            {
                if (++completed > 1) return false;
                start = i;
                advance = 0;
            }
            advance += next;
        }
        if (start < advances.Length || completed == 0) completed++;
        return completed <= 1;
    }
}
