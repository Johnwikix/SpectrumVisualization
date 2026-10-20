using System;
using System.Threading;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original clockwork meshes, cached on layout changes; only native transforms run per frame.</summary>
internal sealed class FoliaPendolo
{
    private readonly record struct Part(Vector2 Origin, Vector3 Orbit, bool Orbiting, float Base, Vector4 Coefficients);
    private long _atlasIdentity;
    private Part[] _parts = [];
    internal float[] Mesh { get; private set; } = [];
    internal int MeshVersion { get; private set; }
    private float[] _heights = [];
    private float _width, _height, _scale, _tick, _velocity, _phase, _bass = .15f;
    private float _secondElapsed, _secondAngle, _secondVelocity;
    private int _secondStep, _index = -1;
    private double _position = double.NaN;
    private int _activeIndex = -1;
    private const float AngleStep = 100 * MathF.PI / 180 / 8;
    internal FoliaMotionFrame Motion;
    internal int GraphicsCount { get; private set; }

    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight, CancellationToken token = default)
    {
        if (atlas.Identity == _atlasIdentity && width == _width && height == _height && scale == _scale)
            return;
        using var document = JsonDocument.Parse(FoliaSourcePlanner.Compile(
            FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight), "compilePendolo", token));
        var root = document.RootElement;
        var parts = root.GetProperty("parts");
        _parts = new Part[parts.GetArrayLength()];
        int pi = 0;
        foreach (var part in parts.EnumerateArray())
        {
            var origin = part.GetProperty("origin");
            var orbit = part.GetProperty("orbit");
            var coefficients = part.GetProperty("coefficients");
            bool orbiting = orbit.ValueKind != JsonValueKind.Null;
            _parts[pi++] = new(new(origin[0].GetSingle(), origin[1].GetSingle()),
                orbiting ? new(orbit[0].GetSingle(), orbit[1].GetSingle(), orbit[2].GetSingle()) : default,
                orbiting, part.GetProperty("base").GetSingle(),
                new(coefficients[0].GetSingle(), coefficients[1].GetSingle(), coefficients[2].GetSingle(), coefficients[3].GetSingle()));
        }
        var data = root.GetProperty("data");
        if (_parts.Length > 32)
            throw new InvalidOperationException("Pendolo source exceeds its 32-part transform table.");
        Mesh = new float[data.GetArrayLength()];
        for (int i = 0; i < Mesh.Length; i++)
            Mesh[i] = data[i].GetSingle();
        MeshVersion++;
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = scale;
        _heights = new float[atlas.Lines.Length];
        float maxWidth = AvailableWidth(width) / 1.25f;
        float font = MathF.Round(28 * scale), lineHeight = MathF.Round(font * 1.2f);
        for (int i = 0; i < _heights.Length; i++)
        {
            var line = atlas.Lines[i];
            _heights[i] = (Rows(line.Glyphs, font, maxWidth) * lineHeight
                + (line.Translation.Length > 0 ? Rows(line.Translation, 18, maxWidth) * 23 + 6 : 0)) * 1.25f;
        }
        _index = -1;
    }

    internal void Update(double elapsed, double position, int index, float bass, bool playing, int preceding, int lineCount)
    {
        float dt = (float)Math.Clamp(elapsed, 0, .1);
        _activeIndex = index;
        float anchor = index >= 0 ? index : preceding < 0 ? -1 : preceding == lineCount - 1 ? lineCount : preceding + .5f;
        if (double.IsNaN(_position) || Math.Abs(position - _position) > .5)
        {
            _tick = anchor;
            _velocity = 0;
        }
        _index = index;
        _position = position;
        // Match the source Framer spring without publishing intermediate values through the UI thread.
        int steps = Math.Max(1, (int)MathF.Ceiling(dt * 240));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _velocity += ((anchor - _tick) * 360 - _velocity * 20) / .8f * step;
            _tick += _velocity * step;
        }
        _bass += (Math.Clamp(bass, 0, 1) - _bass) * .12f;
        if (!playing)
            return;
        _phase += dt * (2.8f + _bass * 3.5f);
        _secondElapsed += dt;
        int completed = (int)_secondElapsed;
        _secondElapsed -= completed;
        _secondStep += completed;
        _secondVelocity += (_secondStep * (MathF.Tau / 15) - _secondAngle) * 92 * dt;
        _secondVelocity *= MathF.Exp(-13 * dt);
        _secondAngle += _secondVelocity * dt;
    }

    internal void Write(ref FoliaPainter p, FoliaTextAtlas atlas, int index, double time, Vector3 primary, Vector3 secondary)
    {
        int firstGraphic = p.Count;
        float baseRadius = Math.Min(p.Width, p.Height) * .42f, ringRadius = baseRadius / .42f * .48f;
        float cx = p.Width * .5f - ringRadius;
        float cy = p.Height * .5f, gradient = baseRadius * 1.65f;
        p.Shape(15, cx - gradient, cy - gradient, gradient * 2, gradient * 2, new(14 / 255f, 17 / 255f, 24 / 255f), 1);
        if (Motion.Active)
        {
            float radius = baseRadius * (1 + Motion.Onset * .04f);
            p.Shape(15, cx - radius, cy - radius, radius * 2, radius * 2, secondary, Math.Min(.35f, Motion.Onset * .18f + Motion.Low * .22f));
        }
        GraphicsCount = p.Count - firstGraphic;
        if (atlas.Lines.Length == 0)
            return;
        Span<float> angles = stackalloc float[17];
        int first = Math.Max(0, index - 8), last = Math.Min(atlas.Lines.Length - 1, index + 8);
        angles[index - first] = 0;
        for (int i = index + 1; i <= last; i++)
            angles[i - first] = angles[i - first - 1] + Math.Max(AngleStep, (_heights[i - 1] + _heights[i] + 24) / (2 * ringRadius));
        for (int i = index - 1; i >= first; i--)
            angles[i - first] = angles[i - first + 1] - Math.Max(AngleStep, (_heights[i + 1] + _heights[i] + 24) / (2 * ringRadius));
        float wheel = -(_tick - index) * AngleStep;
        for (int i = first; i <= last; i++)
        {
            float angle = angles[i - first];
            if (Math.Abs(angle) >= MathF.PI * .5f)
                continue;
            float distance = Math.Abs(i - index), scale = i == _activeIndex ? 1.25f : Math.Max(.7f, 1 - distance * .08f);
            float alpha = Math.Max(.12f, MathF.Pow(MathF.Cos(angle * .75f), 2.5f) * (1 - distance * .18f));
            alpha *= Math.Clamp((110 - Math.Abs((angle + wheel) * 180 / MathF.PI)) / 28, 0, 1);
            var line = atlas.Lines[i];
            float rotation = (angle + wheel) * .35f;
            Vector2 origin = new(cx + ringRadius * MathF.Cos(angle + wheel), cy + ringRadius * MathF.Sin(angle + wheel));
            float font = MathF.Round((i == _activeIndex ? 28 : 22) * _scale);
            float maxWidth = AvailableWidth(p.Width) / scale;
            float rowHeight = MathF.Round(font * 1.2f), height = Rows(line.Glyphs, font, maxWidth) * rowHeight;
            float subtitleHeight = line.Translation.Length > 0 ? Rows(line.Translation, 18, maxWidth) * 23 + 6 : 0;
            DrawRows(ref p, line.Glyphs, origin, -(height + subtitleHeight) * scale * .5f, font, rowHeight, maxWidth, scale, rotation, primary, alpha * (i == _activeIndex ? 1 : .75f), i == _activeIndex, time);
            if (subtitleHeight > 0)
                DrawRows(ref p, line.Translation, origin, (height - subtitleHeight) * scale * .5f + 6 * scale, 18, 23, maxWidth, scale, rotation, secondary, alpha * (i == index ? 1 : .6f), false, time);
        }
    }

    internal void WriteTransforms(Span<Vector4> transforms)
    {
        float cy = _height * .5f;
        float cx = _width * .5f - Math.Min(_width, _height) * .48f;
        // The live drumhead response moves the mechanism without changing its gear timing.
        cx += MathF.Sin(_phase * 19) * Motion.Onset * 2.5f;
        cy += MathF.Cos(_phase * 23) * Motion.Low * 3;
        float escapement = _tick * AngleStep;
        var motion = new Vector4(escapement, _phase, MathF.Sin(_phase) * (.15f + _bass * .7f), _secondAngle);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref readonly var part = ref _parts[i];
            Vector2 origin = part.Origin;
            if (part.Orbiting)
            {
                float angle = part.Orbit.Y + escapement * part.Orbit.Z;
                origin = new(MathF.Cos(angle) * part.Orbit.X, cy + MathF.Sin(angle) * part.Orbit.X);
            }
            // The source mesh is compiled around x=0. Translate fixed and orbiting parts
            // together so its lyric ring meets the middle of the current DIP viewport.
            origin.X += cx;
            float rotation = part.Base + Vector4.Dot(motion, part.Coefficients);
            transforms[i] = new(origin, MathF.Cos(rotation), MathF.Sin(rotation));
        }
        transforms[32] = new(_width, _height, 0, 0);
    }

    private static float AvailableWidth(float width)
        => Math.Max(64, width * .5f - Math.Clamp(width * .04f, 16, 48));
    private static int Rows(ReadOnlySpan<FoliaPreparedGlyph> glyphs, float font, float width)
    {
        int rows = 1;
        float x = 0;
        foreach (ref readonly var glyph in glyphs)
        {
            float advance = (glyph.Glyph.Advance + .01f) * font;
            if (x > 0 && x + advance > width)
            {
                rows++;
                x = 0;
            }
            x += advance;
        }
        return rows;
    }
    private static void DrawRows(ref FoliaPainter p, ReadOnlySpan<FoliaPreparedGlyph> glyphs, Vector2 origin, float y, float font, float rowHeight, float width, float scale, float rotation, Vector3 color, float alpha, bool sweep, double time)
    {
        float x = 0;
        var transform = Matrix3x2.CreateRotation(rotation);
        foreach (ref readonly var glyph in glyphs)
        {
            float advance = (glyph.Glyph.Advance + .01f) * font;
            if (x > 0 && x + advance > width)
            {
                x = 0;
                y += rowHeight * scale;
            }
            Vector2 position = origin + Vector2.Transform(new Vector2(x * scale, y), transform);
            p.Glyph(glyph.Glyph, position.X, position.Y, font * scale, color, alpha * (sweep ? .3f : 1), rotation);
            if (sweep)
            {
                float progress = Math.Clamp((float)((time - glyph.Start) / Math.Max(.001, glyph.End - glyph.Start)), 0, 1);
                p.Glyph(glyph.Glyph, position.X, position.Y, font * scale, color, alpha, rotation, reveal: progress);
            }
            x += advance;
        }
    }
}
