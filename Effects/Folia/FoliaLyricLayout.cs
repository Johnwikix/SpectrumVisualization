using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Native lyric stage entry. Specialized layouts share timing / glyph coverage, never a XAML text overlay.</summary>
internal static partial class FoliaLyricLayout
{
    internal static int Write(Span<FoliaInstance> output, FoliaTextAtlas atlas, FoliaSong song,
        double time, float animationTime, int mode, float width, float height, float fontScale, FoliaLumiere lumiere, FoliaCadenza cadenza, FoliaTempera tempera, FoliaSonnet sonnet, FoliaDiorama diorama, FoliaFume fume, FoliaTilt tilt, FoliaPendolo pendolo, FoliaGlowWords glowWords, FoliaPartita partita, FoliaMonet monet, bool showSong = true, int? lineAnchor = null)
    {
        var painter = new FoliaPainter(output, width, height);
        var palette = song.Palette;
        Vector3 primary = palette[1], secondary = palette[2], accent = palette[3];
        if (!showSong || atlas.Lines.Length == 0)
        {
            switch (mode)
            {
                case 7 when showSong: monet.Write(ref painter, atlas, song, 0, time); break;
                case 9: pendolo.Write(ref painter, atlas, -1, time, primary, secondary); break;
                case 10: sonnet.Write(ref painter, time, primary, secondary, accent, showLyrics: false); break;
                case 11: tempera.Write(ref painter, atlas, -1, time, fontScale, showLyrics: false); break;
                case 12: lumiere.Write(ref painter, atlas, time, fontScale, showLyrics: false); break;
            }
            return painter.Count;
        }
        int anchor = lineAnchor ?? FindLine(atlas.Lines, time);
        int active = mode is 3 or 4 or 7 or 9 ? -1 : atlas.Timeline.FindActive(anchor, time);
        int index = Math.Clamp(anchor, 0, atlas.Lines.Length - 1);
        bool timed = mode is 0 or 1 or 2 or 5;
        if (timed && active >= 0)
            index = active;
        var timing = atlas.Lines[index].Source.Timing;
        glowWords.SetTiming(timing);
        if (!timed || active >= 0)
        switch (mode)
        {
            case 1:
                cadenza.Write(ref painter, atlas.Lines[index], index, time, primary, accent);
                break;
            case 2:
                partita.Write(ref painter, atlas.Lines[index], index, time, animationTime, fontScale, primary, accent, glowWords);
                break;
            case 3:
                fume.Write(ref painter, atlas, index, time, primary, secondary, accent);
                break;
            case 4:
                Cappella(ref painter, atlas, index, time, fontScale, palette[0], primary, secondary, accent, glowWords.Chat!);
                break;
            case 5:
                tilt.Write(ref painter, atlas.Lines[index], index, time, fontScale, primary, accent);
                break;
            case 6:
                Claddagh(ref painter, atlas.Lines, index, time, animationTime, fontScale, primary, accent, glowWords.Energy);
                break;
            case 7:
                monet.Write(ref painter, atlas, song, index, time);
                break;
            case 8:
                diorama.Write(ref painter, atlas, index, time, fontScale, primary, secondary, accent, palette[0]);
                break;
            case 9:
                pendolo.Write(ref painter, atlas, index, time, primary, secondary);
                break;
            case 10:
                sonnet.Write(ref painter, time, primary, secondary, accent);
                break;
            case 11:
                tempera.Write(ref painter, atlas, index, time, fontScale);
                break;
            case 12:
                lumiere.Write(ref painter, atlas, time, fontScale);
                break;
            default:
                Classic(ref painter, glowWords.ClassicLine(index, atlas.Lines[index]), time, animationTime, fontScale, primary, accent, glowWords);
                break;
        }
        if (timed)
            glowWords.Handoff?.Compose(ref painter, output, active, time, glowWords.HandoffTime, timing);
        int firstSubtitle = painter.Count;
        if (mode is not (3 or 4 or 7 or 9))
            Subtitle(ref painter, atlas, active, primary, secondary);
        if (mode == 10)
            sonnet.SubtitleCount = painter.Count - firstSubtitle;
        return painter.Count;
    }

    internal static int FindLine(FoliaPreparedLine[] lines, double time)
    {
        int lo = 0, hi = lines.Length - 1, answer = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (lines[mid].Source.Start <= time)
            {
                answer = mid;
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }
        return answer;
    }

    private static float Progress(in FoliaPreparedGlyph glyph, double time) => Math.Clamp((float)((time - glyph.Start) / Math.Max(.001, glyph.End - glyph.Start)), 0, 1);

    internal static void Subtitle(ref FoliaPainter p, FoliaTextAtlas atlas, int active, Vector3 primary, Vector3 secondary)
    {
        if (active < 0) return;
        var line = atlas.Lines[active];
        var original = line.OriginalBounds;
        var translation = line.TranslationBounds;
        float font = Math.Clamp(p.Width * .014f, 16, 24);
        float originalFont = Math.Min(font, p.Width * .85f / Math.Max(1, original.Width));
        float translationFont = Math.Min(font * .82f, p.Width * .85f / Math.Max(1, translation.Width));
        float margin = Math.Min(24, p.Height * .04f);
        float gap = line.Glyphs.Length > 0 && line.Translation.Length > 0 ? Math.Min(5, p.Height * .015f) : 0;
        float textHeight = original.Height * originalFont + translation.Height * translationFont;
        float scale = Math.Min(1, (p.Height - 2 * margin - gap) / Math.Max(1, textHeight));
        originalFont *= scale;
        translationFont *= scale;
        float bottom = p.Height - margin;
        float originalBottom = bottom - translation.Height * translationFont - gap;
        p.Text(line.Glyphs, (p.Width - original.Width * originalFont) * .5f - original.Left * originalFont,
            originalBottom - original.Bottom * originalFont, originalFont, primary, .9f);
        p.Text(line.Translation, (p.Width - translation.Width * translationFont) * .5f - translation.Left * translationFont,
            bottom - translation.Bottom * translationFont, translationFont, secondary, .85f);
    }

}
