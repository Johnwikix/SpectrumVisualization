using System;
using System.Collections.Generic;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaStageRow(int First, int End, float Offset, float Width);

/// <summary>Wraps shaped glyphs once, preserving their original lyric timing and word identities.</summary>
internal sealed record FoliaStageText(FoliaPreparedLine Line, FoliaStageRow[] Rows, float Width)
{
    internal const float RowHeight = 1.24f;

    internal static FoliaStageText[] Prepare(FoliaTextAtlas atlas, float aspect, CancellationToken token)
    {
        var layouts = new FoliaStageText[atlas.Lines.Length];
        for (int index = 0; index < layouts.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var line = atlas.Lines[index];
            var glyphs = line.Glyphs.AsSpan();
            var rows = new List<FoliaStageRow>();
            float limit = Math.Max(line.Width / 4, Math.Clamp(aspect * 9, 6, 20));
            int first = 0;
            float width = 0;
            while (first < glyphs.Length)
            {
                int end = first + 1;
                int wordBreak = end;
                float offset = glyphs[first].Offset;
                for (; end < glyphs.Length; end++)
                {
                    if (glyphs[end].Word != glyphs[end - 1].Word)
                        wordBreak = end;
                    if (glyphs[end].Offset + glyphs[end].Glyph.Advance - offset > limit)
                        break;
                }
                if (end < glyphs.Length && wordBreak > first + 1)
                    end = wordBreak;
                float rowWidth = glyphs[end - 1].Offset + glyphs[end - 1].Glyph.Advance - offset;
                rows.Add(new(first, end, offset, rowWidth));
                width = Math.Max(width, rowWidth);
                first = end;
            }
            layouts[index] = new(line, rows.ToArray(), width);
        }
        return layouts;
    }

    internal float Fit(float width, float height, float scale) =>
        Math.Min(Math.Min(width / Math.Max(Width, .01f), height / Math.Max(Rows.Length * RowHeight, 1)),
            Math.Min(width, height * 1.6f) * .29f * scale);

    internal static float Reveal(in FoliaPreparedGlyph glyph, double position) =>
        Math.Clamp((float)((position - glyph.Start) / Math.Max(.001, glyph.End - glyph.Start)), 0, 1);
}
