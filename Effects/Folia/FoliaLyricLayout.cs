using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Native lyric stage entry. Specialized layouts share timing / glyph coverage, never a XAML text overlay.</summary>
internal static partial class FoliaLyricLayout
{
    internal static int Write(Span<FoliaInstance> output, FoliaTextAtlas atlas, FoliaSong song,
        double time, float animationTime, int mode, float width, float height, float fontScale, FoliaLumiere lumiere, FoliaCadenza cadenza, FoliaTempera tempera, FoliaSonnet sonnet, FoliaDiorama diorama, FoliaFume fume, FoliaTilt tilt, FoliaPendolo pendolo, FoliaGlowWords glowWords, FoliaPartita partita, FoliaMonet monet)
    {
        var painter = new FoliaPainter(output, width, height);
        var palette = song.Palette;
        Vector3 primary = palette[1], secondary = palette[2], accent = palette[3];
        int index = FindLine(atlas.Lines, time);
        if (atlas.Lines.Length == 0)
        {
            return WriteMetadata(output, atlas, song, width, height, fontScale);
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
                monet.Write(ref painter, atlas, song, index, time);
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

    internal static int WriteMetadata(Span<FoliaInstance> output, FoliaTextAtlas atlas, FoliaSong song, float width, float height, float scale)
    {
        var painter = new FoliaPainter(output, width, height);
        float font = Math.Min(Math.Min(width * .045f, 42) * scale, width * .84f / Math.Max(atlas.TitleWidth, 1));
        float artist = Math.Min(18, width * .8f / Math.Max(atlas.ArtistWidth, 1));
        float top = height * .44f;
        if (song.CoverWidth > 0)
        {
            float cover = Math.Min(160, Math.Min(width * .28f, height * .28f));
            painter.Shape(10, (width - cover) * .5f, top - cover - 28, cover, cover, Vector3.One, 1, radius: 12, uv: new(0, 0, 1, 1));
        }
        painter.Text(atlas.Title, (width - atlas.TitleWidth * font) * .5f, top, font, song.Palette[1], .95f);
        painter.Text(atlas.Artist, (width - atlas.ArtistWidth * artist) * .5f, top + font * 1.45f + 8, artist, song.Palette[1], .65f);
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
