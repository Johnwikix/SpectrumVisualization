using System;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold camera sampling with the original 241 samples and five-tap focus filter.
/// Only JsValue arrays cross the interpreter boundary; rendering consumes the prepared frames.</summary>
internal static class FoliaSonnetCameraSamples
{
    private readonly record struct Point(double X, double Y);
    private readonly record struct Glyph(double X, double Y, double Start);
    private readonly record struct Frame(double X, double Y, double Scale, double Rotation);

    internal static void Install(Engine engine, CancellationToken token) =>
        engine.SetValue("nativeSonnetCamera", new ClrFunction(engine, "nativeSonnetCamera",
            (_, args) => Sample(engine, args, token)));

    private static JsValue Sample(Engine engine, JsValue[] args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string kind = args[0].AsString();
        double start = args[1].AsNumber(), end = args[2].AsNumber(), phase = args[3].AsNumber();
        var tracked = args[4].AsArray();
        var tracks = new Glyph[tracked.Length][];
        double done = double.NegativeInfinity;
        for (uint i = 0; i < tracked.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var source = tracked.Get(i).AsObject().Get("glyphs").AsArray();
            var glyphs = tracks[i] = new Glyph[source.Length];
            for (uint j = 0; j < source.Length; j++)
            {
                if ((j & 63) == 0) token.ThrowIfCancellationRequested();
                var glyph = source.Get(j).AsObject();
                glyphs[j] = new(glyph.Get("baseX").AsNumber(), glyph.Get("baseY").AsNumber(), glyph.Get("startTime").AsNumber());
            }
            done = Math.Max(done, glyphs[^1].Start);
        }
        var sampler = new FocusSampler(tracks);
        var frames = new JsValue[241];
        double duration = Math.Max(.001, end - start);
        for (int i = 0; i < frames.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            double progress = (double)i / 240, time = start + duration * progress;
            var frame = Motion(kind, progress);
            var focus = sampler.Smoothed(time, start, end);
            double weight = Ease(Clamp((time - done) / 1.2));
            double tau = time * Math.PI * 2;
            double x = (Math.Sin(tau * .13 + phase) * .65 + Math.Sin(tau * .31 + phase * 1.7) * .35) * .006;
            double y = (Math.Cos(tau * .11 + phase * 2.3) * .65 + Math.Sin(tau * .29 + phase * .9) * .35) * .006;
            double scale = Math.Sin(tau * .09 + phase * 1.3) * .002;
            double rotation = Math.Sin(tau * .07 + phase * 2.9) * .0015;
            frames[i] = new JsArray(engine, [(JsValue)focus.X, (JsValue)focus.Y,
                (JsValue)(frame.X + x * weight), (JsValue)(frame.Y + y * weight),
                (JsValue)(frame.Scale + scale * weight), (JsValue)(frame.Rotation + rotation * weight)]);
        }
        return new JsArray(engine, frames);
    }

    private sealed class FocusSampler(Glyph[][] tracks)
    {
        private readonly double[] _weights = new double[tracks.Length];

        private Point At(double time)
        {
            double maximum = double.NegativeInfinity;
            for (int i = 0; i < tracks.Length; i++)
            {
                double a = tracks[i][0].Start, b = tracks[i][^1].Start;
                double start = Math.Min(a, b), end = Math.Max(a, b);
                double distance = time < start ? start - time : time > end ? time - end : 0;
                double weight = -(distance * distance) / (2 * .35 * .35);
                _weights[i] = weight;
                maximum = Math.Max(maximum, weight);
            }
            double total = 0;
            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] = Math.Exp(_weights[i] - maximum);
                total += _weights[i];
            }
            double x = 0, y = 0;
            for (int i = 0; i < tracks.Length; i++)
            {
                var focus = Segment(tracks[i], time);
                double weight = _weights[i] / total;
                x += focus.X * weight;
                y += focus.Y * weight;
            }
            return new(x, y);
        }

        internal Point Smoothed(double time, double start, double end)
        {
            double low = Math.Min(start, end), high = Math.Max(start, end);
            if (low == high) return At(Math.Min(high, Math.Max(low, time)));
            ReadOnlySpan<double> offsets = [-1, -.5, 0, .5, 1];
            ReadOnlySpan<double> weights = [1, 4, 6, 4, 1];
            Span<Point> samples = stackalloc Point[5];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = At(Math.Min(high, Math.Max(low, time + offsets[i] * .12)));
            var center = samples[2];
            double x = 0, y = 0, total = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                var point = samples[i];
                double dx = point.X - center.X, dy = point.Y - center.Y;
                if (dx * dx + dy * dy > 96 * 96) continue;
                x += point.X * weights[i];
                y += point.Y * weights[i];
                total += weights[i];
            }
            return new(x / total, y / total);
        }
    }

    private static Point Segment(Glyph[] glyphs, double time)
    {
        var first = glyphs[0];
        var last = glyphs[^1];
        double centerX = (first.X + last.X) / 2, centerY = (first.Y + last.Y) / 2;
        double x = first.X, y = first.Y;
        if (time >= last.Start && time > first.Start)
        {
            x = last.X;
            y = last.Y;
        }
        else if (time > first.Start)
        {
            for (int i = 0; i < glyphs.Length - 1; i++)
            {
                var current = glyphs[i];
                var next = glyphs[i + 1];
                if (time < current.Start || time > next.Start) continue;
                double progress = (time - current.Start) / Math.Max(.001, next.Start - current.Start);
                x = current.X + (next.X - current.X) * progress;
                y = current.Y + (next.Y - current.Y) * progress;
                break;
            }
        }
        return new(centerX + (x - centerX) * .5, centerY + (y - centerY) * .5);
    }

    private static double Clamp(double value) => Math.Min(1, Math.Max(0, value));
    private static double Cubic(double p1, double p2, double time)
    {
        double inverse = 1 - time;
        return 3 * inverse * inverse * time * p1 + 3 * inverse * time * time * p2 + time * time * time;
    }
    private static double Ease(double value)
    {
        double target = Clamp(value);
        if (target is 0 or 1) return target;
        double low = 0, high = 1, parameter = target;
        for (int i = 0; i < 12; i++)
        {
            if (Cubic(.65, .35, parameter) < target) low = parameter;
            else high = parameter;
            parameter = (low + high) / 2;
        }
        return Cubic(0, 1, parameter);
    }
    private static double Expo(double value) => value == 1 ? 1 : 1 - Math.Pow(2, -10 * value);
    private static Frame Motion(string kind, double progress)
    {
        double linear = Clamp(progress);
        double eased;
        if (kind is "tracking-ribbon" or "fragment-collage" or "quiet-tableau" or "poster-blocks")
            eased = linear * .55 + Ease(linear) * .45;
        else if (linear < .18) eased = Expo(linear / .18) * .22;
        else if (linear < .78) eased = .22 + (linear - .18) / .6 * .56;
        else
        {
            double settle = (linear - .78) / .22;
            eased = .78 + (1 - (1 - settle) * (1 - settle)) * .22;
        }
        return kind switch
        {
            "editorial-column" => new(-.055 + eased * .095, .025 - eased * .04, .98 + eased * .07, -.006 + eased * .01),
            "type-impact" => new(-.035 + eased * .07, .018 - eased * .028, 1 + (1 - Expo(Math.Min(linear / .18, 1))) * .22 + eased * .08, -.01 + eased * .016),
            "fragment-collage" => new(-.045 + eased * .085, .028 - Math.Sin(eased * Math.PI) * .055, .97 + eased * .09, -.014 + eased * .028),
            "tracking-ribbon" => new(-.16 + eased * .28, .05 - eased * .085, .98 + eased * .07, .008 - eased * .014),
            "mask-reveal" => new(.035 - eased * .065, .1 - eased * .135, .96 + eased * .12, -.006 + eased * .009),
            "poster-blocks" => new(-.012 + eased * .024, .008 - eased * .016, .99 + eased * .025, -.0015 + eased * .003),
            "quiet-tableau" => new(-.022 + eased * .04, .014 - eased * .025, 1 + eased * .028, -.002 + eased * .003),
            _ => throw new InvalidOperationException("Unknown Sonnet camera kind: " + kind)
        };
    }
}
