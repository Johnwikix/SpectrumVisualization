using System;
using System.Collections.Generic;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold numerical placement search. The original planner still owns typography,
/// candidate order and motion; only its nested collision loop runs outside the interpreter.</summary>
internal sealed class FoliaCadenzaSearch(Engine engine, CancellationToken token)
{
    private sealed class Rect(double left, double top, double right, double bottom)
    {
        internal readonly double Left = left, Top = top, Right = right, Bottom = bottom;
        internal int Stamp;
    }

    private readonly List<Rect> _rectangles = [];
    private readonly Dictionary<int, List<Rect>> _bands = [];
    private double _bandSize;
    private int _stamp;

    internal static void Install(Engine engine, CancellationToken token)
    {
        var search = new FoliaCadenzaSearch(engine, token);
        // A direct JsValue callback avoids reflection-based CLR object/delegate binding in AOT.
        engine.SetValue("nativeCadenzaSearch", new ClrFunction(engine, "nativeCadenzaSearch", (_, args) => search.Find(args)));
    }

    private JsValue Find(JsValue[] args)
    {
        token.ThrowIfCancellationRequested();
        if (args.Length == 1)
        {
            _rectangles.Clear();
            _bands.Clear();
            _stamp = 0;
            _bandSize = Math.Max(24, Math.Floor(args[0].AsNumber() * .9 + .5));
            return JsValue.Undefined;
        }

        double x = args[0].AsNumber(), y = args[1].AsNumber();
        double width = args[2].AsNumber(), height = args[3].AsNumber(), padding = args[4].AsNumber();
        double step = args[5].AsNumber(), maximum = args[6].AsNumber();
        bool hero = args[7].AsNumber() > 1;
        double baseAngle = args[8].AsNumber();
        double minX = args[9].AsNumber(), maxX = args[10].AsNumber();
        double minY = args[11].AsNumber(), maxY = args[12].AsNumber();
        double chosenX = x, chosenY = y, bestScore = double.PositiveInfinity;
        bool found = false;
        for (double radius = 0; radius <= maximum && !found; radius += step)
        {
            token.ThrowIfCancellationRequested();
            int samples = radius == 0 ? 1 : hero ? 8
                : Math.Max(12, (int)Math.Floor(Math.PI * 2 * radius / Math.Max(step * 1.1, 10) + .5));
            for (int sample = 0; sample < samples; sample++)
            {
                double angle = baseAngle + (double)sample / samples * Math.PI * 2;
                double dx = radius == 0 ? 0 : Math.Cos(angle) * radius;
                double dy = radius == 0 ? 0 : Math.Sin(angle) * (radius * (hero ? .8 : .92));
                double left = x + dx - padding, top = y + dy - height - padding;
                double right = left + width + padding * 2, bottom = top + height + padding * 2;
                if (!(left >= minX && right <= maxX && top >= minY && bottom <= maxY)) continue;
                bool intersects = Intersects(left, top, right, bottom, out double overlap);
                double score = overlap * 2.2 + double.Hypot(dx, dy);
                if (score < bestScore)
                {
                    chosenX = x + dx;
                    chosenY = y + dy;
                    bestScore = score;
                }
                if (!intersects)
                {
                    chosenX = x + dx;
                    chosenY = y + dy;
                    found = true;
                    break;
                }
            }
        }
        var rect = new Rect(chosenX - padding, chosenY - height - padding,
            chosenX + width + padding, chosenY + padding);
        _rectangles.Add(rect);
        for (int band = Band(rect.Top); band <= Band(rect.Bottom); band++)
        {
            if (!_bands.TryGetValue(band, out var bucket)) _bands.Add(band, bucket = []);
            bucket.Add(rect);
        }
        return new JsArray(engine, [(JsValue)chosenX, (JsValue)chosenY]);
    }

    private int Band(double value) => checked((int)Math.Floor(value / _bandSize));

    private bool Intersects(double left, double top, double right, double bottom, out double overlap)
    {
        overlap = 0;
        if (_rectangles.Count == 0) return false;
        int stamp = ++_stamp;
        bool intersects = false;
        // Preserve the original band/bucket traversal and summation order for fallback scores.
        for (int band = Band(top); band <= Band(bottom); band++)
        {
            if (!_bands.TryGetValue(band, out var bucket)) continue;
            foreach (var rect in bucket)
            {
                if (rect.Stamp == stamp) continue;
                rect.Stamp = stamp;
                double width = Math.Max(0, Math.Min(right, rect.Right) - Math.Max(left, rect.Left));
                double height = Math.Max(0, Math.Min(bottom, rect.Bottom) - Math.Max(top, rect.Top));
                if (width <= 0 || height <= 0) continue;
                intersects = true;
                overlap += width * height;
            }
        }
        return intersects;
    }
}
