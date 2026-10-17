using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiereGeometry
{
    internal Vector4 Star(int index) => _lines[_activeLine].Glyphs[index].Star;
    internal float StarStrength(in Pose pose) => _transform.Alpha * Shield(_activeLine, pose.Position, pose.Font * .5f, pose.Flying);

    internal void WriteSpot(ref FoliaPainter p, Vector3 color)
    {
        var glyphs = _lyrics[_activeLine].Glyphs;
        if (glyphs.Length == 0)
            return;
        float alpha = _transform.Alpha * FoliaPainter.Ease((float)(_time - glyphs[0].Start + .3) / .3f)
            * (1 - FoliaPainter.Ease((float)(_time - glyphs[^1].End) / .6f));
        if (alpha <= .003f)
            return;
        var flows = _lines[_activeLine].Flows;
        int orient = _transform.Orient >= .5f ? 2 : 0;
        Vector2 local = default;
        for (int sample = 0; sample < 8; sample++)
        {
            double time = _time - .3 * sample / 7;
            float position = 0;
            foreach (ref readonly var glyph in glyphs.AsSpan())
                position += Clamp((float)((time - glyph.Start) / Math.Max(.001, glyph.End - glyph.Start)));
            position = Math.Clamp(position - .5f, 0, glyphs.Length - 1);
            int lower = (int)position, upper = Math.Min(glyphs.Length - 1, lower + 1);
            var a = Vector2.Lerp(flows[orient].Spots[lower], flows[orient].Spots[upper], position - lower);
            var b = Vector2.Lerp(flows[orient + 1].Spots[lower], flows[orient + 1].Spots[upper], position - lower);
            local += Vector2.Lerp(a, b, _transform.Wrap) / 8;
        }
        float cos = MathF.Cos(_transform.Rotation), sin = MathF.Sin(_transform.Rotation);
        var point = _transform.Position + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos) * _transform.Scale;
        float size = _hero * _transform.Scale * 5;
        float width = size * (orient == 2 ? 1 : 1.6f), height = size * (orient == 2 ? 1.6f : 1);
        p.Shape(17, point.X - width * .5f, point.Y - height * .5f, width, height, color, alpha * .07f, rotation: _transform.Rotation);
    }

    internal void WriteTrails(ref FoliaPainter p, Vector3 color)
    {
        double time = _time, start = _slideStart;
        if (!double.IsFinite(start) || time > start + 1.5 + .45)
            return;
        double from = Math.Max(start, time - .45);
        if (time - from < .001)
            return;
        var line = _lines[_activeLine];
        int count = line.Glyphs.Length;
        bool flying = false;
        for (int sample = 0; sample <= 20; sample++)
        {
            _time = from + (time - from) * sample / 20;
            _transform = ComputeTransform(_activeLine, _time);
            for (int m = 0; m < _transform.Moves; m++)
                flying |= _moves[m].Fly;
            for (int glyph = 0; glyph < count; glyph++)
            {
                var pose = Resolve(glyph);
                float wobble = MathF.Sin(sample * .9f + line.Glyphs[glyph].Wobble + (float)_time * 6) * _hero * .02f;
                _trailPoints[glyph * 21 + sample] = pose.Position + new Vector2(wobble, -wobble * .6f);
            }
        }
        _time = time;
        _transform = ComputeTransform(_activeLine, time);
        if (!flying)
            return;
        float dim = 1 - .25f * Clamp((float)(time - _lyrics[_activeLine].Source.End) / 2.5f);
        for (int glyph = 0; glyph < count; glyph++)
        {
            var points = _trailPoints.AsSpan(glyph * 21, 21);
            float length = 0;
            for (int sample = 1; sample < points.Length; sample++)
                length += Vector2.Distance(points[sample - 1], points[sample]);
            float strength = Math.Min(1, length / (_hero * 3));
            if (strength <= .05f)
                continue;
            var pose = Resolve(glyph);
            float alpha = _transform.Alpha * dim * .55f * strength * Shield(_activeLine, points[^1], pose.Font * .5f, pose.Flying);
            for (int sample = 1; sample < points.Length; sample++)
                p.Line(points[sample - 1].X, points[sample - 1].Y, points[sample].X, points[sample].Y, color, alpha, 1.2f);
        }
    }
}
