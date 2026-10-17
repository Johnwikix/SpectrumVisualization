using System;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Closed-form port of Folia windowGeometry / windowTiming / lineClearance.
/// Source planning supplies immutable flow and seeded parameters; every frame reuses these buffers.</summary>
internal sealed partial class FoliaLumiereGeometry
{
    internal readonly record struct Pose(Vector2 Position, float Font, float Rotation, float Alpha, float Flying);
    private readonly record struct Slot(float X, float Y, float Scale, float Alpha, float Orient, float Rotation, float Wrap);
    private sealed record Flow(Vector2[] Points, Vector2[] Spots, float Along, float Across, float InkWidth, float InkHeight, int Lines);
    private readonly record struct Glyph(Vector2 C1, Vector2 C2, float Delay, float Duration, float Spin,
        float Scale, float VRotation, Vector4 Drift, Vector2 Scatter, float Phase, Vector4 Star, float Wobble);
    private sealed record Line(Flow[] Flows, Slot[] Placements, float Jitter, Vector2 Velocity, float Phase, Glyph[] Glyphs);
    private readonly record struct Move(float Orient, float Wrap, float Phase, bool Fly);
    private readonly record struct Transform(int Current, Vector2 Position, float Scale, float Rotation, float Alpha,
        float Orient, float Wrap, float RestOrient, float RestWrap, int Moves);
    private readonly record struct Protect(int Line, Vector2 Position, Vector2 Rotation, Vector2 Half, float Margin, float Weight);
    private readonly FoliaPreparedLine[] _lyrics;
    private readonly Line[] _lines;
    private readonly int[] _typography;
    private readonly Move[] _moves;
    private readonly Protect[] _protection = new Protect[4];
    private readonly Vector4 _region, _band;
    private readonly float _width, _height, _hero, _fontH, _decayStrength, _decayDelay, _columnCap;
    private int _protectCount;
    internal int Low { get; private set; }
    internal int High { get; private set; }
    private Transform _transform;
    private int _activeLine;
    private double _time, _slideStart;
    private readonly Vector2[] _trailPoints;

    internal FoliaLumiereGeometry(JsonElement root, FoliaPreparedLine[] lyrics, float width, float height)
    {
        _lyrics = lyrics;
        _width = width;
        _height = height;
        _hero = F(root, "heroPx");
        _fontH = _hero / height;
        var region = root.GetProperty("region");
        _region = new(F(region, "cx"), F(region, "cy"), F(region, "w"), F(region, "h"));
        var decay = root.GetProperty("decay");
        _decayStrength = F(decay, "strength");
        _decayDelay = F(decay, "delay");
        float left = Math.Max(30, width * .065f) / height + .04f;
        float top = Math.Max(30, height * .085f) / height + .035f;
        _band = new(left, width / height - left, top, Math.Max(top + .2f, .8f));
        _columnCap = (_band.W - _band.Z) * height;
        _typography = new int[lyrics.Length];
        var kinds = root.GetProperty("typography");
        for (int i = 0; i < _typography.Length; i++)
            _typography[i] = kinds[i].GetString() switch { "vertical" => 1, "crossed" => 2, _ => 0 };
        _lines = new Line[lyrics.Length];
        _moves = new Move[lyrics.Length + 1];
        int li = 0;
        foreach (var row in root.GetProperty("views").EnumerateArray())
        {
            var flows = new Flow[4];
            var f = row.GetProperty("flow");
            for (int i = 0; i < 4; i++)
            {
                var v = f[i / 2][i % 2];
                flows[i] = new(Points(v.GetProperty("points")), Points(v.GetProperty("spots")), F(v, "along"), F(v, "across"), F(v, "inkWidth"), F(v, "inkHeight"), v.GetProperty("lines").GetInt32());
            }
            var slots = new Slot[5];
            foreach (var entry in row.GetProperty("placements").EnumerateArray())
            {
                var v = entry[1];
                slots[entry[0].GetInt32() + 2] = new(F(v, "dx"), F(v, "dy"), F(v, "scale"), F(v, "alpha"), F(v, "orient"), F(v, "rotation"), F(v, "wrap"));
            }
            var values = row.GetProperty("glyphs");
            var glyphs = new Glyph[values.GetArrayLength()];
            for (int i = 0; i < glyphs.Length; i++)
            {
                var g = values[i];
                var flight = g.GetProperty("flight");
                var drift = g.GetProperty("drift");
                var star = g.GetProperty("starShape");
                glyphs[i] = new(Point(flight.GetProperty("c1")), Point(flight.GetProperty("c2")), F(flight, "delay"), F(flight, "duration"), F(flight, "spin"),
                    F(g, "scale"), F(g, "vRotation"), new(F(drift, "dx"), F(drift, "dy"), F(drift, "speed"), F(drift, "spin")), Point(g.GetProperty("scatter")), F(g, "phase"),
                    new(F(star, "dx"), F(star, "dy"), F(star, "rotation"), F(star, "size")), F(flight, "wobble"));
            }
            _lines[li++] = new(flows, slots, F(row, "jitter"), Point(row.GetProperty("velocity")), F(row, "motionPhase"), glyphs);
        }
        int maximumGlyphs = 0;
        foreach (var line in lyrics)
            maximumGlyphs = Math.Max(maximumGlyphs, line.Glyphs.Length);
        _trailPoints = new Vector2[maximumGlyphs * 21];
    }
    private static float F(JsonElement value, string key) => value.GetProperty(key).GetSingle();
    private static Vector2 Point(JsonElement value) => new(F(value, "x"), F(value, "y"));
    private static Vector2[] Points(JsonElement value)
    {
        var result = new Vector2[value.GetArrayLength()];
        for (int i = 0; i < result.Length; i++)
            result[i] = Point(value[i]);
        return result;
    }
    private int Kind(int current) => _typography[Math.Clamp(current, 0, _typography.Length - 1)];
    private float MaxWidth(int kind) => (_band.Y - _band.X) * _height * (kind == 2 ? .9f : 1);
    private static float Fit(float along, float budget, float scale) => Math.Min(1, budget / Math.Max(scale * along, .000001f));
    private static float ClampInto(float center, float extent, float low, float high)
        => extent >= high - low ? (low + high) * .5f : Math.Clamp(center, low + extent * .5f, high - extent * .5f);
    private (int Wrap, float Scale) Settle(int index, int kind, int orient, float scale)
    {
        var flows = _lines[index].Flows;
        float budget = orient == 1 ? _columnCap : MaxWidth(kind);
        int first = orient * 2;
        int wrap = flows[first + 1].Lines > 1 && scale * flows[first].Along * .8f > budget ? 1 : 0;
        return (wrap, scale * Fit(flows[first + wrap].Along, budget, scale));
    }
    private float Across(int index, int kind, int orient, float scale)
    {
        if (index < 0 || index >= _lines.Length)
            return _fontH * scale;
        var settled = Settle(index, kind, orient, scale);
        return _lines[index].Flows[orient * 2 + settled.Wrap].Across / _height * settled.Scale;
    }
    private Vector2 Shift(int current, int kind)
    {
        if (current < 0 || current >= _lines.Length)
            return default;
        int orient = kind == 1 ? 1 : 0;
        var settled = Settle(current, kind, orient, 1);
        var flow = _lines[current].Flows[orient * 2 + settled.Wrap];
        return new(ClampInto(_region.X, flow.InkWidth / _height * settled.Scale, _band.X, _band.Y) - _region.X,
            ClampInto(_region.Y, flow.InkHeight / _height * settled.Scale, _band.Z, _band.W) - _region.Y);
    }
    private float TiltedHeight(Line line, int orient, int wrap, float scale, float tilt, float budget)
    {
        var flow = line.Flows[orient * 2 + wrap];
        return (flow.InkHeight * MathF.Cos(tilt) + flow.InkWidth * MathF.Sin(tilt)) / _height * scale * Fit(flow.Along, budget, scale);
    }
    private Slot SlotOf(int index, int current, int kind)
    {
        var line = _lines[index];
        int relative = Math.Clamp(index - current, -2, 2);
        var shift = Shift(current, kind);
        Slot slot;
        int? forcedWrap = null;
        if (kind == 2 && relative != 0)
        {
            var basis = line.Placements[relative + 2];
            int orient = basis.Orient >= .5f ? 1 : 0;
            int sign = basis.Y < 0 ? -1 : 1;
            float currentHalf = _fontH * .5f;
            if (current >= 0 && current < _lines.Length)
            {
                var settled = Settle(current, kind, 0, 1);
                currentHalf = _lines[current].Flows[settled.Wrap].InkHeight / _height * settled.Scale * .5f;
            }
            float gap = _fontH * .5f;
            float centerY = _region.Y + shift.Y;
            float room = sign < 0 ? centerY - currentHalf - gap - _band.Z : _band.W - centerY - currentHalf - gap;
            float budget = orient == 1 ? _columnCap : MaxWidth(kind);
            float tilt = Math.Abs(basis.Rotation) + .03f;
            float scale = basis.Scale;
            int wrap = Settle(index, kind, orient, scale).Wrap;
            if (orient == 1 && room > 0)
            {
                if (wrap == 0 && line.Flows[3].Lines > 1 && TiltedHeight(line, orient, 0, scale, tilt, budget) > room)
                    wrap = 1;
                float tall = TiltedHeight(line, orient, wrap, scale, tilt, budget);
                if (tall > room)
                    scale = Math.Max(basis.Scale * .5f, scale * room / tall);
            }
            forcedWrap = wrap;
            float clearance = currentHalf + gap + TiltedHeight(line, orient, wrap, scale, tilt, budget) * .5f;
            float wider = orient == 1 ? Math.Max(0, Across(index, kind, 1, scale) - _fontH * scale) * .5f : 0;
            slot = basis with { X = basis.X + MathF.Sign(basis.X) * wider, Y = sign * Math.Max(Math.Abs(basis.Y), clearance), Scale = scale };
        }
        else
        {
            bool vertical = kind == 1;
            int orient = vertical ? 1 : 0;
            if (relative == 0)
                slot = new(0, 0, 1, 1, orient, 0, 0);
            else
            {
                bool previous = relative < 0, far = Math.Abs(relative) > 1;
                float nearScale = vertical ? .58f : .52f;
                float scale = far ? vertical ? .48f : .44f : nearScale;
                int sign = previous ? -1 : 1;
                float offset = Across(current, kind, orient, 1) * .5f + (vertical ? previous ? .95f : .88f : previous ? .93f : .735f) * _fontH;
                if (far)
                    offset += Across(current + sign, kind, orient, nearScale) + (vertical ? previous ? .78f : .7f : .56f) * _fontH;
                offset += Across(index, kind, orient, scale) * .5f;
                float alpha = far ? 0 : previous ? .9f : .95f;
                slot = vertical ? new(-sign * offset, sign * (far ? .07f : .03f), scale, alpha, 1, 0, 0)
                    : new(sign * _region.Z * (far ? .24f : .16f), sign * offset, scale, alpha, 0, 0, 0);
            }
        }
        return slot with { X = slot.X + shift.X, Y = slot.Y + shift.Y,
            Wrap = forcedWrap ?? Settle(index, kind, slot.Orient >= .5f ? 1 : 0, slot.Scale).Wrap };
    }
    private static float Clamp(float value) => Math.Clamp(value, 0, 1);
    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;
    private static float Sine(float value) => (1 - MathF.Cos(MathF.PI * Clamp(value))) * .5f;
    private static float Cubic(float value)
    {
        float t = Clamp(value);
        return t < .5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) * .5f;
    }
    private static int Stack(int kind) => kind == 1 ? 1 : 0;
    private static int Away(int index, int current, int kind) => (index == current ? 0 : index < current ? -1 : 1) * (kind == 1 ? -1 : 1);
    private static float AwayDrift(float drift, float away)
    {
        if (away == 0)
            return drift;
        float sign = MathF.Sign(away), along = drift * sign;
        return drift + Math.Abs(away) * (MathF.Sqrt(along * along + .000025f) - .005f - along) * sign;
    }
    private static Vector2 HeldDrift(Vector2 velocity, float age)
    {
        float speed = velocity.Length();
        if (speed < 1e-9f)
            return default;
        float phi = speed * age / .018f, along = .018f * MathF.Sin(phi), turn = .018f * (1 - MathF.Cos(phi));
        return new Vector2(along * velocity.X - turn * velocity.Y, along * velocity.Y + turn * velocity.X) / speed;
    }
    private static float Lag(int relative) => relative <= -2 ? 0 : relative == -1 ? .15f : relative == 0 ? .3f : relative == 1 ? .5f : .6f;
    private int Current(double time) => FoliaLyricLayout.FindLine(_lyrics, time + 1.2);
    private int FirstLive(double time, int current)
    {
        int c = current;
        while (c >= 0 && _lyrics[c].Source.Start - 1.2 + .6 + 1.5 + .45 > time)
            c--;
        return c + 1;
    }
}

