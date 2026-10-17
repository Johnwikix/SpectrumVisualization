using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Native lyric stage entry. Specialized layouts share timing / glyph coverage, never a XAML text overlay.</summary>
internal static partial class FoliaLyricLayout
{
    internal static int Write(Span<FoliaInstance> output, FoliaTextAtlas atlas, FoliaSong song,
        double time, float animationTime, int mode, float width, float height, float fontScale, FoliaLumiere lumiere, FoliaCadenza cadenza, FoliaTempera tempera, FoliaSonnet sonnet, FoliaDiorama diorama, FoliaFume fume, FoliaTilt tilt, FoliaPendolo pendolo, FoliaGlowWords glowWords, FoliaPartita partita)
    {
        var painter = new FoliaPainter(output, width, height);
        var palette = song.Palette;
        Vector3 primary = palette[1], secondary = palette[2], accent = palette[3];
        int index = FindLine(atlas.Lines, time);
        if (atlas.Lines.Length == 0)
        {
            float font = Math.Min(width * .055f, 58) * fontScale;
            font = Math.Min(font, width * .8f / Math.Max(atlas.TitleWidth, 1));
            painter.Text(atlas.Title, (width - atlas.TitleWidth * font) * .5f, height * .43f, font, primary, .9f);
            painter.Text(atlas.Artist, (width - atlas.ArtistWidth * 20) * .5f, height * .43f + font * 1.6f, 20, secondary, .75f);
            return painter.Count;
        }
        index = Math.Clamp(index, 0, atlas.Lines.Length - 1);
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
                Cappella(ref painter, atlas, index, time, fontScale, palette[0], primary, secondary, accent);
                break;
            case 5:
                tilt.Write(ref painter, atlas.Lines[index], index, time, fontScale, primary, accent);
                break;
            case 6:
                Claddagh(ref painter, atlas.Lines, index, time, animationTime, fontScale, primary, accent);
                break;
            case 7:
                Monet(ref painter, atlas, index, time, fontScale, primary, secondary, accent);
                break;
            case 8:
                diorama.Write(ref painter, atlas, index, time, fontScale, primary, secondary, accent);
                break;
            case 9:
                pendolo.Write(ref painter, atlas, index, time, primary, secondary);
                break;
            case 10:
                sonnet.Write(ref painter, time, primary, accent);
                break;
            case 11:
                tempera.Write(ref painter, atlas, index, time, fontScale);
                break;
            case 12:
                lumiere.Write(ref painter, atlas, time, fontScale);
                break;
            default:
                Classic(ref painter, atlas.Lines[index], time, animationTime, fontScale, primary, accent, glowWords);
                break;
        }
        if (mode is not (3 or 4 or 7 or 9))
            Subtitle(ref painter, atlas.Lines[index], primary, secondary);
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

    private static float Presence(FoliaLine line, double time, float lead = .3f, float tail = .5f)
        => FoliaPainter.Ease((float)(time - line.Start + lead) / lead) * (1 - FoliaPainter.Ease((float)(time - line.End) / tail));

    private static float Progress(in FoliaPreparedGlyph glyph, double time) => Math.Clamp((float)((time - glyph.Start) / Math.Max(.001, glyph.End - glyph.Start)), 0, 1);

    private static void Subtitle(ref FoliaPainter p, FoliaPreparedLine line, Vector3 primary, Vector3 secondary)
    {
        if (line.Translation.Length == 0)
            return;
        float font = Math.Clamp(p.Width * .014f, 16, 24);
        float scale = Math.Min(1, p.Width * .85f / Math.Max(line.TranslationWidth * font, 1));
        font *= scale;
        p.Text(line.Translation, (p.Width - line.TranslationWidth * font) * .5f, p.Height * .9f, font, secondary, .85f);
    }

}
