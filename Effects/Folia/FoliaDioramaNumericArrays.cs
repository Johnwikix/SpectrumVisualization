using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold Float32 rounding for the native positions-only surface consumer.</summary>
internal static class FoliaDioramaNumericArrays
{
    internal static void Install(Engine engine, CancellationToken token) => engine.SetValue("nativeDioramaPositions",
        new ClrFunction(engine, "nativeDioramaPositions", (_, args) =>
        {
            var values = args[0].AsArray();
            for (uint i = 0; i < values.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                double number = values.Get(i).AsNumber();
                double rounded = (float)number;
                if (number != rounded) values[i] = rounded;
            }
            return values;
        }));
}
