using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Prepared reading layout with render-owned spring state. No Win2D drawing or frame allocations.</summary>
internal sealed class FoliaMonet
{
    private readonly record struct Glyph(FoliaPreparedGlyph Source, float X, float Y);
    private sealed record Line(FoliaPreparedLine Source, Glyph[] Text, Glyph[] Translation, float Height, float Top);
    private Line[] _lines = [];
    private FoliaLyricSpring[] _motion = [];
    private Glyph[] _title = [];
    private float _left, _top, _width, _bottom, _font, _translationFont, _titleFont, _artistFont;
    private float _infoLeft, _infoTop, _infoWidth, _titleTop, _artistTop, _coverSize;
    private bool _wide, _hasCover;
    private int _index = -1;
    private double _time = double.NaN;
    internal float FrameDelta { get; set; }

    internal void Prepare(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        float width = request.Width, height = request.Height;
        float margin = Math.Clamp(width * .045f, 24, 72);
        _wide = width >= 1040 && height >= 520;
        _hasCover = request.Song.CoverWidth > 0;
        _infoLeft = margin;
        _infoWidth = (_wide ? width * .5f : width) - margin * 2;
        _font = Math.Clamp(width * (_wide ? .027f : .038f), 24, _wide ? 52 : 38) * request.Scale;
        _font = Math.Min(_font, Math.Max(20, height * .085f));
        _translationFont = Math.Clamp(_font * .48f, 14, 23);
        _titleFont = Math.Clamp(width * .026f, 23, 34);
        _artistFont = Math.Clamp(_titleFont * .56f, 14, 19);
        _coverSize = _hasCover && !_wide ? Math.Clamp(height * .105f, 56, 88) : 0;
        float headerLeft = _wide || !_hasCover ? _infoLeft : _infoLeft + _coverSize + 20;
        float headerWidth = _wide ? _infoWidth : width - margin - headerLeft;
        _title = Arrange(atlas.MonetTitle, _titleFont, headerWidth, out float titleHeight, maximumRows: 2);
        _artistFont = Math.Min(_artistFont, headerWidth / Math.Max(1, atlas.ArtistWidth));
        if (_hasCover && _wide)
            _coverSize = Math.Min(_infoWidth, Math.Max(0, height - margin * 2 - titleHeight - 40 - _artistFont * 1.4f));
        float infoHeight = _wide ? _coverSize + (_hasCover ? 28 : 0) + titleHeight + 12 + _artistFont * 1.4f
            : Math.Max(_coverSize, titleHeight + 8 + _artistFont * 1.4f);
        _infoTop = _wide ? Math.Max(margin, (height - infoHeight) * .5f) : margin;
        _titleTop = _infoTop + (_wide && _hasCover ? _coverSize + 28 : 0);
        _artistTop = _titleTop + titleHeight + (_wide ? 12 : 8);
        _left = _wide ? width * .5f + margin : margin;
        _width = Math.Max(80, width - margin - _left);
        _top = _wide ? margin : _infoTop + infoHeight + Math.Clamp(height * .045f, 24, 40);
        _bottom = Math.Max(_top + _font * 1.8f, height - margin);
        _lines = new Line[atlas.Lines.Length];
        _motion = new FoliaLyricSpring[_lines.Length];
        float naturalTop = 0;
        for (int i = 0; i < _lines.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var line = atlas.Lines[i];
            var text = Arrange(line.Glyphs, _font, _width - 8, out float textHeight);
            var translation = Arrange(line.Translation, _translationFont, _width - 8, out float translationHeight);
            float heightOfLine = textHeight + (translation.Length == 0 ? 0 : 12 + translationHeight);
            for (int j = 0; j < translation.Length; j++) translation[j] = translation[j] with { Y = translation[j].Y + textHeight + 12 };
            _lines[i] = new(line, text, translation, heightOfLine, naturalTop);
            naturalTop += heightOfLine + Math.Clamp(_font * .72f, 22, 40);
        }
        _infoLeft = headerLeft;
        _index = -1;
        _time = double.NaN;
    }

    private static Glyph[] Arrange(ReadOnlySpan<FoliaPreparedGlyph> glyphs, float font, float width,
        out float height, int maximumRows = int.MaxValue)
    {
        if (glyphs.IsEmpty) { height = 0; return []; }
        var result = new List<Glyph>(glyphs.Length);
        float x = 0, y = 0;
        int row = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            // Prefer real word boundaries, but split oversized tokens (including an entire LRC line) at graphemes.
            if (i == 0 || glyphs[i].Word != glyphs[i - 1].Word)
            {
                int end = i;
                while (end + 1 < glyphs.Length && glyphs[end + 1].Word == glyphs[i].Word) end++;
                float wordWidth = (glyphs[end].Offset + glyphs[end].Glyph.Advance - glyphs[i].Offset) * font;
                if (wordWidth <= width && x > 0 && x + wordWidth > width) { x = 0; y += font * 1.42f; row++; }
            }
            float advance = glyphs[i].Glyph.Advance * font;
            if (x > 0 && x + advance > width) { x = 0; y += font * 1.42f; row++; }
            if (row >= maximumRows) break;
            result.Add(new(glyphs[i], x, y));
            x += advance;
        }
        height = y + font * 1.42f;
        return result.ToArray();
    }

    internal void Write(ref FoliaPainter painter, FoliaTextAtlas atlas, FoliaSong song, int index, double time)
    {
        Vector3 color = song.Palette[1];
        if (_hasCover)
        {
            float coverLeft = _wide ? _infoLeft + (_infoWidth - _coverSize) * .5f : _infoLeft - _coverSize - 20;
            painter.Shape(10, coverLeft, _infoTop, _coverSize, _coverSize, Vector3.One, 1, radius: _wide ? 16 : 10, uv: new(0, 0, 1, 1));
        }
        foreach (var glyph in _title)
            painter.Glyph(glyph.Source.Glyph, _infoLeft + glyph.X, _titleTop + glyph.Y, _titleFont, color, .96f);
        painter.Text(atlas.Artist, _infoLeft, _artistTop, _artistFont, color, .62f);
        if (_lines.Length == 0) return;
        index = Math.Clamp(index, 0, _lines.Length - 1);
        bool jump = double.IsNaN(_time) || Math.Abs(time - _time) > .6 + FrameDelta * 2;
        bool changed = index != _index;
        double offset = _top + (_bottom - _top) * .43 - _lines[index].Top - _lines[index].Height * .5;
        double interval = index + 1 < _lines.Length ? _lines[index + 1].Source.Source.Start - _lines[index].Source.Source.Start : 4;
        int first = Math.Max(0, index - 3);
        for (int i = 0; i < _lines.Length; i++)
        {
            double target = _lines[i].Top + offset;
            if (jump) _motion[i].Jump(target);
            else if (changed) _motion[i].Retarget(target, FoliaLyricSpring.Stagger(i, first, interval));
            _motion[i].Advance(Math.Clamp(FrameDelta, 0, .1));
        }
        _index = index;
        _time = time;
        int start = painter.Count;
        for (int i = 0; i < _lines.Length; i++)
        {
            var line = _lines[i];
            float y = (float)_motion[i].Value;
            if (y + line.Height < _top - _font || y > _bottom + _font) continue;
            int distance = Math.Abs(i - index);
            bool active = i == index;
            float opacity = active ? 1 : Math.Max(.16f, (i < index ? .38f : .46f) - Math.Max(0, distance - 1) * .12f);
            float blur = active ? 0 : Math.Min(1.1f, Math.Max(0, distance - 1) * .45f);
            foreach (var item in line.Text)
            {
                var glyph = item.Source;
                float x = _left + item.X, top = y + item.Y;
                float progress = line.Source.Source.WordSynced
                    ? Math.Clamp((float)((time - glyph.Start) / Math.Max(.001, glyph.End - glyph.Start)), 0, 1)
                    : time >= line.Source.Source.Start ? 1 : 0;
                float rise = 0, growth = 1, glow = 0;
                if (active && line.Source.Source.WordSynced)
                {
                    var word = line.Source.Source.Words[glyph.Word];
                    float enter = FoliaPainter.Ease((float)(time - glyph.Start) / .18f);
                    float leave = 1 - FoliaPainter.Ease((float)(time - word.End) / .36f);
                    float emphasis = enter * leave;
                    rise = -Math.Min(4, _font * .065f) * emphasis;
                    if (word.End - word.Start >= .7) { growth += .045f * emphasis; glow = .16f * emphasis; }
                }
                float size = _font * growth;
                x -= glyph.Glyph.Advance * (size - _font) * .5f;
                top += rise - (size - _font) * .5f;
                float baseAlpha = active ? .54f : opacity;
                painter.Glyph(glyph.Glyph, x, top, size, color, baseAlpha, blur: blur);
                if (active && progress > 0)
                    painter.Glyph(glyph.Glyph, x, top, size, color, .98f, reveal: progress, glow: glow, revealSoftness: .06f);
            }
            foreach (var glyph in line.Translation)
                painter.Glyph(glyph.Source.Glyph, _left + glyph.X, y + glyph.Y, _translationFont, color, active ? .70f : opacity * .72f, blur: blur);
        }
        painter.RailMaskFrom(start, _top, _bottom);
    }
}

/// <summary>Exact critically damped motion, following the player's velocity-preserving FlowWave model.</summary>
internal struct FoliaLyricSpring
{
    internal double Value { get; private set; }
    internal double Velocity { get; private set; }
    private double _target, _delay;
    internal void Jump(double value) { Value = _target = value; Velocity = _delay = 0; }
    internal void Retarget(double value, double delay) { _target = value; _delay = delay; }
    internal void Advance(double seconds)
    {
        double wait = Math.Min(_delay, seconds);
        _delay -= wait;
        seconds -= wait;
        if (seconds <= 0) return;
        const double omega = 7 / .65;
        double displacement = Value - _target, coefficient = Velocity + omega * displacement;
        double decay = Math.Exp(-omega * seconds);
        Value = _target + (displacement + coefficient * seconds) * decay;
        Velocity = (Velocity - omega * coefficient * seconds) * decay;
    }
    internal static double Stagger(int index, int first, double interval)
    {
        double strength = Math.Clamp((interval - .25) / .25, 0, 1);
        strength = strength * strength * (3 - 2 * strength);
        double budget = Math.Min(.4, Math.Min(.65, Math.Max(0, interval) * .85) * .75);
        return budget <= 0 ? 0 : budget * (1 - Math.Exp(-Math.Max(0, index - first) * .09 / budget)) * strength;
    }
}
