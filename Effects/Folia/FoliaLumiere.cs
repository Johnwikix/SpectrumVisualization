using WinExSpectrumTest.Model;
using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original Lumiere light rigs, seeded line-art recipes and closed-form native motion.
/// Cold compilation retains immutable numeric plans; playback never invokes the script engine.</summary>
internal sealed partial class FoliaLumiere
{
    private sealed class Path(Vector4 style, Vector2 dash, Vector2[] points)
    {
        internal readonly Vector4 Style = style;
        internal readonly Vector2 Dash = dash;
        internal readonly Vector2[] Points = points;
        internal readonly float[] Lengths = LengthsOf(points);
        private static float[] LengthsOf(Vector2[] points)
        {
            var values = new float[points.Length];
            for (int i = 1; i < values.Length; i++)
                values[i] = values[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            return values;
        }
    }
    private sealed record Profile(string Kind, string Family, string Mood, string Typography,
        float[] Layout, Vector4[][] Beams, Vector4 FogA, Vector4 FogB, Vector4 Glare, float GlareStreak,
        Vector4 Caustic, Vector4 Floor, float FloorStrength, Vector4 Wave, Vector4 WaveArea, float Shield,
        float[] Motes, Path[] Paths, float[][] Nodes);
    internal int GraphicsCount { get; private set; }
    internal int TextCount { get; private set; }
    internal int DarkCount { get; private set; }
    internal bool HasScene => _shots.Length > 0;
    private Vector3 _background;
    private Shot[] _shots = [];
    private Shot _shot = null!;
    private Profile _profile = null!;
    private readonly Vector4[] _resolved = new Vector4[36];
    private float _local;
    private Vector3 _light, _lit, _unlit;

    internal void Update(Span<Vector4> data, FoliaSong song, double time, float width, float height, float bass)
    {
        _background = song.Palette[0];
        if (_shots.Length == 0)
        {
            data.Clear();
            GraphicsCount = TextCount = DarkCount = 0;
            return;
        }
        int shotIndex = 0;
        for (int i = 1; i < _shots.Length; i++)
            if (_shots[i].Start <= time)
                shotIndex = i;
        _shot = _shots[shotIndex];
        _profile = _shot.Profile;
        _local = (float)(time - _shot.Start);
        _camera = ResolveCamera(time);
        bass *= AppSettings.FoliaLumiereAudio;
        float aspect = _width / _height;
        float openingTime = (float)(time - _span.X) - _opening * .45f;
        _sceneIntensity = (_hasOpening ? FoliaPainter.Ease((openingTime + .4f) / 1.6f) : 1) * (AppSettings.FoliaLumiereLight * (1 + BurstBoost(time)));
        Vector3 champagne = new(1, .86f, .62f);
        _light = Vector3.Lerp(Vector3.Lerp(champagne, song.Palette[3], .18f), Glow(song.Palette[3], champagne), AppSettings.FoliaLumiereThemeMix);
        _light /= Math.Max(.001f, Math.Max(_light.X, Math.Max(_light.Y, _light.Z)));
        Vector3 warm = Vector3.Lerp(_light, Vector3.One, .2f);
        _lit = Vector3.Lerp(warm, Vector3.Lerp(Glow(song.Palette[1], warm), Vector3.One, .2f), AppSettings.FoliaLumiereThemeMix);
        _unlit = Vector3.Lerp(Vector3.Lerp(_light, new(.62f, .68f, .8f), .55f),
            Vector3.Lerp(Glow(song.Palette[2], _lit), new(.62f, .68f, .8f), .35f), AppSettings.FoliaLumiereThemeMix);
        data.Clear();
        // Original handoff keeps the six strongest beams across both rigs.
        Span<ResolvedLight> candidates = stackalloc ResolvedLight[12];
        float handoff = shotIndex == 0 ? 1 : FoliaPainter.Ease(_local / .9f);
        int count = Resolve(_profile, time, _local, aspect, bass, handoff, candidates);
        Profile previous = shotIndex > 0 ? _shots[shotIndex - 1].Profile : _profile;
        if (handoff < 1)
            count += Resolve(previous, time, (float)(time - _shots[shotIndex - 1].Start), aspect, bass, 1 - handoff, candidates[count..]);
        for (int i = 1; i < count; i++)
        {
            var candidate = candidates[i];
            int j = i;
            while (j > 0 && candidates[j - 1].C.X < candidate.C.X)
            {
                candidates[j] = candidates[j - 1];
                j--;
            }
            candidates[j] = candidate;
        }
        count = Math.Min(count, 6);
        for (int i = 0; i < count; i++)
        {
            var b = candidates[i];
            data[i] = b.A;
            data[6 + i] = b.B;
            data[12 + i] = b.C;
            data[18 + i] = b.D;
            data[24 + i] = b.E;
            data[30 + i] = b.F;
        }
        data[..36].CopyTo(_resolved);
        data[36] = new(width, height, (float)time, count);
        data[37] = Vector4.Lerp(previous.FogA, _profile.FogA, handoff);
        data[37].X *= AppSettings.FoliaLumiereFog;
        data[38] = Vector4.Lerp(previous.FogB, _profile.FogB, handoff);
        data[38].Z *= AppSettings.FoliaLumiereFog;
        data[39] = new(_light, 0);
        float glarePhase = Math.Clamp(_local / 1.6f, 0, 1);
        float glareBlend = shotIndex == 0 ? 1 : glarePhase < .5f ? 4 * glarePhase * glarePhase * glarePhase : 1 - MathF.Pow(-2 * glarePhase + 2, 3) * .5f;
        Vector4 glare = Vector4.Lerp(previous.Glare, _profile.Glare, glareBlend);
        data[40] = new(glare.X * aspect, glare.Y, glare.Z, glare.W * _sceneIntensity
            + (_hasOpening && openingTime >= 0 ? 1.8f * MathF.Exp(-openingTime / .35f) : 0));
        data[41] = new(previous.GlareStreak + (_profile.GlareStreak - previous.GlareStreak) * glareBlend, 0, 0, 0);
        data[42] = new(_light, 0);
        data[43] = Vector4.Zero;
        if (AppSettings.FoliaLumiereTextOnly)
            for (int i = 12; i < 18; i++) data[i].X = 0;
        Profile pattern = handoff < .5f ? previous : _profile;
        float patternFade = handoff < .5f ? 1 - 2 * handoff : 2 * handoff - 1;
        data[44] = pattern.Caustic;
        data[44].Z *= patternFade;
        data[45] = new(pattern.Floor.X * aspect, pattern.Floor.Y, pattern.Floor.Z * aspect, pattern.Floor.W);
        data[46] = new(pattern.FloorStrength * patternFade, 0, 0, 0);
        data[47] = pattern.Wave;
        data[47].W *= patternFade;
        data[48] = new(pattern.WaveArea.X * aspect, pattern.WaveArea.Y, pattern.WaveArea.Z, pattern.WaveArea.W);
        data[49] = new(_lead.Layout[0] * aspect, _lead.Layout[1], _lead.Layout[2] * aspect * .5f, _lead.Layout[3] * .5f);
        data[50] = new(pattern.Shield, 0, 0, 0);
        data[51] = _camera;
    }

    private static float Oscillate(Vector4 oscillation, float time)
        => oscillation.X * MathF.Sin((time / Math.Max(.001f, oscillation.Y) + oscillation.Z) * MathF.Tau);

    internal void Write(ref FoliaPainter painter, FoliaTextAtlas atlas, double time, float fontScale)
    {
        int firstInstance = painter.Count;
        float h = painter.Height, w = painter.Width;
        Vector3 background = _background;
        float brightness = Vector3.Dot(background, new(.2126f, .7152f, .0722f));
        float dark = brightness > .18f ? Math.Max(.94f, AppSettings.FoliaLumiereDarkField) : AppSettings.FoliaLumiereDarkField;
        painter.Shape(1, 0, 0, w, h, background * .06f, dark);
        DarkCount = painter.Count - firstInstance;
        if (!AppSettings.FoliaLumiereTextOnly && AppSettings.FoliaLumiereEcho > 0)
        {
            int firstEcho = painter.Count;
            WriteEchoes(ref painter, atlas, time);
            painter.FadeFrom(firstEcho, AppSettings.FoliaLumiereEcho);
        }
        if (!AppSettings.FoliaLumiereTextOnly) WriteStars(ref painter, time);
        foreach (var shot in _shots)
        {
            float begin = ReferenceEquals(shot, _shots[0]) && _hasOpening ? _span.X + _opening * .45f - .6f : (float)shot.Start - .6f;
            float fade = FoliaPainter.Ease((float)(time - begin) / 1.2f)
                * (ReferenceEquals(shot, _shots[^1]) ? 1 : 1 - FoliaPainter.Ease((float)(time - shot.End) / .8f));
            if (fade <= .003f)
                continue;
            float progress = (float)(time - begin) / 3.6f;
            foreach (var path in shot.Profile.Paths)
            {
                if (!AppSettings.FoliaLumiereLineArt || AppSettings.FoliaLumiereTextOnly || painter.Remaining < 2300 || path.Points.Length < 2)
                    break;
                float draw = Math.Clamp((progress - path.Style.Z) / Math.Max(.001f, path.Style.W), 0, 1);
                float amount = (1 - MathF.Pow(1 - draw, 3)) * path.Lengths[^1];
                var midpoint = path.Points[path.Points.Length / 2];
                float alpha = Math.Min(1, path.Style.Y * fade * shot.Profile.Layout[8] * (.55f + .9f * Illumination(midpoint.X, midpoint.Y)));
                float period = path.Dash.X + path.Dash.Y;
                for (int i = 1; i < path.Points.Length && path.Lengths[i - 1] < amount; i++)
                {
                    if (painter.Remaining < 2300)
                        break;
                    float start = path.Lengths[i - 1], end = Math.Min(amount, path.Lengths[i]);
                    if (period <= 0)
                        Trace(ref painter, path, i, start, end, h, alpha);
                    else
                        for (int dash = (int)MathF.Floor(start / period); dash * period < end; dash++)
                        {
                            float from = Math.Max(start, dash * period), to = Math.Min(end, dash * period + path.Dash.X);
                            if (to > from && painter.Remaining >= 2300)
                                Trace(ref painter, path, i, from, to, h, alpha);
                        }
                }
            }
            foreach (float[] node in shot.Profile.Nodes)
            {
                if (!AppSettings.FoliaLumiereLineArt || AppSettings.FoliaLumiereTextOnly || painter.Remaining < 1800 || progress < node[3])
                    continue;
                float appear = Math.Clamp((progress - node[3]) / .08f, 0, 1);
                float lit = Illumination(node[0], node[1]);
                float twinkle = .55f + .45f * MathF.Sin((float)time * 2.3f + node[4]);
                float alpha = appear * fade * (.25f + .75f * lit) * twinkle;
                float size = node[2] * h * (1 + (1 - appear) * 1.5f) * (.8f + .2f * twinkle);
                painter.Shape(18, node[0] * h - size * .5f, node[1] * h - size * .5f, size, size, _light, alpha);
            }
        }
        WriteMotes(ref painter, _motes, time, false);
        GraphicsCount = painter.Count - firstInstance;
        int textFirst = painter.Count;
        WriteWindow(ref painter, atlas, time);
        TextCount = painter.Count - textFirst;
        if (AppSettings.FoliaLumiereFrontBokeh) WriteMotes(ref painter, _front, time, true);
        painter.TransformFrom(firstInstance + DarkCount, new(w * .5f, h * .5f), new(_camera.X * w, _camera.Y * h), _camera.Z, _camera.W);
        // The frame is a screen-space overlay, outside the scene camera.
        if (!AppSettings.FoliaLumiereFrame) return;
        for (int corner = 0; corner < 4; corner++)
        {
            float padX = Math.Max(30, w * .065f), padY = Math.Max(30, h * .085f);
            float x = corner % 2 == 0 ? padX : w - padX;
            float y = corner < 2 ? padY : h - padY;
            float arm = Math.Min(w, h) * .045f;
            float dx = corner % 2 == 0 ? arm : -arm, dy = corner < 2 ? arm : -arm;
            painter.Line(x, y, x + dx, y, _light, .5f);
            painter.Line(x, y, x, y + dy, _light, .5f);
        }
        float pad = Math.Max(30, w * .065f), unit = Math.Min(w, h), mark = unit * .012f;
        float stroke = Math.Max(1.2f, unit / 600);
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? pad : w - pad, y = h * .5f;
            painter.Shape(2, x - mark * .6f, y - mark * .6f, mark * 1.2f, mark * 1.2f, _light, .4f, stroke: stroke);
            painter.Line(x - mark, y, x + mark, y, _light, .4f, stroke);
            painter.Line(x, y - mark, x, y + mark, _light, .4f, stroke);
            int sign = side == 0 ? -1 : 1;
            for (int k = 0; k < 4; k++)
            {
                x = w * .5f + sign * (unit * .1f + k * unit * .018f);
                y = Math.Max(30, h * .085f);
                painter.Line(x, y, x + sign * unit * .009f, y, _light, .275f, stroke);
            }
        }
    }
    private void Trace(ref FoliaPainter painter, Path path, int segment, float from, float to, float height, float alpha)
    {
        float length = Math.Max(.000001f, path.Lengths[segment] - path.Lengths[segment - 1]);
        var a = Vector2.Lerp(path.Points[segment - 1], path.Points[segment], (from - path.Lengths[segment - 1]) / length) * height;
        var b = Vector2.Lerp(path.Points[segment - 1], path.Points[segment], (to - path.Lengths[segment - 1]) / length) * height;
        float width = path.Style.X * height;
        painter.Line(a.X, a.Y, b.X, b.Y, _light, alpha * .12f, width * 3.2f);
        painter.Line(a.X, a.Y, b.X, b.Y, _light, alpha, width);
    }
}
