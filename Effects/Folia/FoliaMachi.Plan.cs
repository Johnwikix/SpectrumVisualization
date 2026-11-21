using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaMachi
{
    private const int BoardHeight = 320;
    private const int TickerHeight = 80;
    private const int MetadataHeight = 40;
    private const float BoardMargin = .05f;
    private const float LineGap = 1.22f;
    private readonly record struct Row(int Start, int End, float Offset, float Width);

    internal sealed class Plan
    {
        private readonly FoliaTextAtlas _atlas;
        private readonly Row[][] _rows;
        private readonly int[] _next;
        private readonly Vector3 _lit, _boardInk, _boardAccent, _boardBackground, _waitingInk, _tickerColor;
        private readonly Vector3 _sungInk, _sungAccent, _pendingInk, _pendingCueInk, _sweepInk;
        private readonly float _boardHeight;
        private readonly double _duration;
        internal string SceneId { get; }
        internal uint TrafficSeed { get; }
        internal float DoorInterval { get; }
        internal float DoorPhase { get; }
        internal float FanSpeed { get; }
        internal double[] DoorEvents { get; }

        internal bool UpdateVacancy(int board, float delta, bool reset, ref float chorus, ref float signChorus)
        {
            if (SceneId != "station") return false;
            float target = board >= 0 && _atlas.Lines[board].Source.IsChorus ? 1 : 0;
            if (reset)
            {
                chorus = signChorus = target;
            }
            else
            {
                chorus += (target - chorus) * (1 - MathF.Exp(-delta * 2.5f));
                signChorus += (chorus - signChorus) * (1 - MathF.Exp(-delta * 3));
            }
            return signChorus > .5f;
        }

        internal Plan(FoliaTextAtlas atlas, FoliaSong song, string sceneId, CancellationToken token)
        {
            _duration = song.Duration > 0 ? song.Duration : song.Lines.Length == 0 ? 0 : BoardEnd(song.Lines[^1]);
            _atlas = atlas;
            SceneId = sceneId;
            TrafficSeed = Hash("car:", song.Title, song.Artist);
            uint actorSeed = Hash("machi:", song.Title, song.Artist);
            // The upstream seed draws these three values after its eleven prop/weather draws.
            for (int i = 0; i < 11; i++) NextRandom(ref actorSeed);
            DoorPhase = NextRandom(ref actorSeed);
            DoorInterval = 22 + 18 * NextRandom(ref actorSeed);
            FanSpeed = 1.6f + 1.6f * NextRandom(ref actorSeed);
            DoorEvents = sceneId == "konbini" ? BuildDoorEvents(song.Lines, DoorInterval, DoorPhase) : [];
            _boardHeight = PanelWidth * (sceneId switch
            {
                "station" => 2.02f / 4.79f,
                "kowloon" => 1.8f / 4.2f,
                _ => 2.18f / 5.46f
            });
            var swatches = song.CoverColors ?? FoliaCoverPalette.Extract(song.CoverRgba, song.CoverWidth, song.CoverHeight, token);
            Vector3 ink, accent, waiting, background;
            if (swatches.Length > 0)
            {
                Vector3[] cover = FoliaCoverPalette.CreateTheme(swatches);
                ink = cover[3] / Math.Max(cover[3].X, Math.Max(cover[3].Y, cover[3].Z));
                accent = Vector3.Lerp(ink, cover[1], .55f);
                waiting = Vector3.Lerp(cover[2], ink, .35f) * .25f;
                background = Vector3.Lerp(ink, swatches[0], .45f) * .13f;
            }
            else
            {
                ink = new(1, .58f, .27f);
                accent = new(1, .81f, .53f);
                waiting = new(.14f, .17f, .23f);
                background = new(.072f, .055f, .047f);
            }
            _lit = song.IsLightTheme ? Vector3.Lerp(new(.16f, .06f, .03f), song.Palette[3], .30f)
                : Vector3.Lerp(new(1, .37f, .09f), song.Palette[3], .20f);
            _tickerColor = accent;
            var lumaWeights = new Vector3(.2126f, .7152f, .0722f);
            Vector3 hue = LinearInk(ink);
            Vector3 accentHue = LinearInk(accent);
            _sungInk = new Vector3(.007f) + hue * .090f;
            _sungAccent = new Vector3(.007f) + accentHue * .090f;
            // Untimed lettering still supplies more light, with headroom for pale covers.
            float steadyGain = 1 - .75f * FoliaPainter.Ease((Vector3.Dot(hue, lumaWeights) - .45f) / .50f);
            _boardInk = EncodeInk(hue * steadyGain);
            _boardAccent = EncodeInk(accentHue * steadyGain);
            _waitingInk = waiting;

            // Limit the diffuse LED field before DXR and bloom while preserving its cover hue.
            Vector3 boardSurface = LinearInk(background);
            float luminance = Vector3.Dot(boardSurface, lumaWeights);
            const float Knee = .003f;
            if (luminance > Knee)
            {
                float excess = luminance - Knee;
                boardSurface *= (Knee + excess / (1 + excess / .004f)) / luminance;
                _pendingInk = boardSurface * .19f;
                _pendingCueInk = boardSurface * .33f;
            }
            else
            {
                _pendingInk = new Vector3(.008f) + hue * .006f;
                _pendingCueInk = _pendingInk + hue * .004f;
            }
            _boardBackground = EncodeInk(boardSurface);

            // Lift the complementary stroke through the LED diffuser without bleaching its hue.
            Vector3 opposite = Vector3.One - hue;
            float span = Math.Max(opposite.X, Math.Max(opposite.Y, opposite.Z));
            Vector3 oppositeHue = Vector3.Lerp(new(.04f, .64f, 1), opposite / Math.Max(span, .001f), Math.Min(1, span * 2));
            _sweepInk = new Vector3(.005f) + oppositeHue * .048f;
            _rows = new Row[atlas.Lines.Length][];
            _next = new int[_rows.Length];
            int next = -1;
            for (int i = _rows.Length - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                FoliaPreparedLine line = atlas.Lines[i];
                _next[i] = next;
                if (IsLyric(line.Source))
                    next = i;
                _rows[i] = LayoutRows(line, _boardHeight, token);
            }
        }

        internal int LyricTarget(int activeLine) =>
            activeLine >= 0 && IsLyric(_atlas.Lines[activeLine].Source) ? activeLine : -1;

        internal int WritePanel(Span<FoliaInstance> output, int target, double position, double ambientTime, float scale, bool showContent = true)
        {
            var painter = new FoliaPainter(output, PanelWidth, PanelHeight);
            painter.Shape(3, 0, 0, PanelWidth, BoardHeight, _boardBackground, 1);
            if (!showContent) return painter.Count;
            int firstGlyph = painter.Count;
            double switched = target >= 0 ? (position - _atlas.Lines[target].Source.Start) / .3 : 1;
            if (target < 0)
                WriteCard(ref painter, scale);
            else
                WriteLine(ref painter, output, target, position, scale);

            int endBoard = painter.Count;
            float boardScale = BoardHeight / _boardHeight;
            float glitch = switched is >= 0 and < 1 ? MathF.Pow(MathF.Sin((float)switched * MathF.PI), .7f) : 0;
            for (int i = firstGlyph; i < endBoard; i++)
            {
                if (glitch > 0)
                {
                    float band = MathF.Floor(output[i].Rect.Y * 22 / _boardHeight);
                    double noise = Math.Sin((band + Math.Floor(ambientTime * 24) * 7) * 127.1) * 43758.5453;
                    float tear = (float)(noise - Math.Floor(noise));
                    if (tear > .62f) output[i].Rect.X += (tear - .5f) * .08f * PanelWidth * glitch;
                    output[i].Color.W *= 1 - .3f * glitch;
                }
                output[i].Rect.Y *= boardScale;
                output[i].Rect.W *= boardScale;
            }

            WriteTicker(ref painter, target, ambientTime);
            int endTicker = painter.Count;
            MarkBand(output, endBoard, endTicker, 1);
            bool neon = SceneId == "kowloon";
            Vector3 ink = SceneId == "konbini" ? new(.012f, .017f, .022f) : _lit;
            if (SceneId == "station")
                WriteMotelTitle(ref painter, output);
            else
                WriteMetadata(ref painter, _atlas.Title, _atlas.TitleWidth, BoardHeight + TickerHeight, ink, false);
            int endTitle = painter.Count;
            MarkBand(output, endTicker, endTitle, 2);
            WriteMetadata(ref painter, _atlas.Artist, _atlas.ArtistWidth, BoardHeight + TickerHeight + MetadataHeight,
                SceneId == "station" ? new(.012f, .017f, .022f) : new(.06f, .025f, .018f), neon);
            int endArtist = painter.Count;
            MarkBand(output, endTitle, endArtist, 3);
            if (neon)
                WriteHologram(ref painter, output, target);
            else if (SceneId == "station")
                WriteStationPylon(ref painter, target, position);
            MarkBand(output, endArtist, painter.Count, 4);
            return painter.Count;
        }

        private static void MarkBand(Span<FoliaInstance> output, int first, int end, int band)
        {
            foreach (ref FoliaInstance instance in output[first..end])
                instance.GlyphData.W = band;
        }

        private void WriteCard(ref FoliaPainter painter, float scale)
        {
            ReadOnlySpan<FoliaPreparedGlyph> title = _atlas.HasMachiCard ? _atlas.MachiCardTitle : _atlas.Title;
            ReadOnlySpan<FoliaPreparedGlyph> artist = _atlas.HasMachiCard ? _atlas.MachiCardArtist : _atlas.Artist;
            float titleWidth = _atlas.HasMachiCard ? _atlas.MachiCardTitleWidth : _atlas.TitleWidth;
            float artistWidth = _atlas.HasMachiCard ? _atlas.MachiCardArtistWidth : _atlas.ArtistWidth;
            bool subtitle = artist.Length > 0;
            float titleFont = Fit(titleWidth, _boardHeight * (subtitle ? .4f : .5f) * scale);
            float artistFont = subtitle ? Fit(artistWidth, titleFont * .52f) : 0;
            float titleHeight = titleFont * 1.15f;
            float artistHeight = artistFont * 1.4f;
            float top = (_boardHeight - titleHeight - artistHeight) * .5f;
            PaintCentered(ref painter, title, titleWidth, top + titleHeight * .5f, titleFont, _boardInk);
            PaintCentered(ref painter, artist, artistWidth, top + titleHeight + artistHeight * .5f, artistFont, _waitingInk);
        }

        private void WriteLine(ref FoliaPainter painter, Span<FoliaInstance> output, int target, double position, float scale)
        {
            var line = _atlas.Lines[target];
            var timing = line.Source.Timing;
            var rows = _rows[target].AsSpan();
            float widest = 0;
            foreach (ref readonly Row row in rows)
                widest = Math.Max(widest, row.Width);
            float fill = rows.Length == 1 ? .56f : .86f / (Math.Max(1, rows.Length) * LineGap);
            float font = Fit(widest, _boardHeight * fill * scale);
            float rowHeight = font * LineGap;
            float y = (_boardHeight - rowHeight * rows.Length) * .5f + rowHeight * .5f;
            foreach (ref readonly Row row in rows)
            {
                float x = (PanelWidth - row.Width * font) * .5f;
                foreach (ref readonly FoliaPreparedGlyph glyph in line.Glyphs.AsSpan(row.Start, row.End - row.Start))
                {
                    float at = x + (glyph.Offset - row.Offset) * font;
                    float top = y - (glyph.Glyph.BearingY + glyph.Glyph.Height * .5f - glyph.Glyph.CanvasCenterY) * font;
                    bool accent = glyph.ActiveColor.HasValue;
                    if (!line.Source.WordSynced)
                    {
                        painter.Glyph(glyph.Glyph, at, top, font, accent ? _boardAccent : _boardInk, 1);
                        continue;
                    }
                    // All graphemes belonging to one authored syllable share its clock.
                    var word = line.Source.Words[glyph.Word];
                    float cue = Math.Clamp((float)((position - timing.WordEntrance(word.Start)) / timing.Lookahead), 0, 1);
                    Vector3 sung = accent ? _sungAccent : _sungInk;
                    Vector3 pending = Vector3.Lerp(_pendingInk, _pendingCueInk, cue);
                    float progress = position < word.Start ? -1 :
                        Math.Clamp((float)((position - word.Start) / Math.Max(.001, word.End - word.Start)), 0, 1);
                    int first = painter.Count;
                    painter.Glyph(glyph.Glyph, at, top, font, sung, 1);
                    if (painter.Count == first) continue;
                    // MachiPanelPS shades all three LED states under one MSDF coverage.
                    ref FoliaInstance instance = ref output[first];
                    instance.GlyphData.Y = 1;
                    instance.GlyphData.Z = FoliaTextAtlas.GlyphPaddingEm / glyph.Glyph.Width;
                    instance.Effect = new(progress, pending.X, pending.Y, pending.Z);
                    instance.Shape = new(0, _sweepInk.X, _sweepInk.Y, _sweepInk.Z);
                }
                y += rowHeight;
            }
        }

        private static Vector3 LinearInk(Vector3 color) => new(LinearInk(color.X), LinearInk(color.Y), LinearInk(color.Z));

        private static float LinearInk(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);

        private static Vector3 EncodeInk(Vector3 color) => new(EncodeInk(color.X), EncodeInk(color.Y), EncodeInk(color.Z));

        private static float EncodeInk(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;

        private void WriteTicker(ref FoliaPainter painter, int target, double ambientTime)
        {
            ReadOnlySpan<FoliaPreparedGlyph> first = _atlas.Title;
            ReadOnlySpan<FoliaPreparedGlyph> second = _atlas.Artist;
            float firstWidth = _atlas.TitleWidth;
            float secondWidth = _atlas.ArtistWidth;
            if (target >= 0)
            {
                var line = _atlas.Lines[target];
                if (SceneId == "kowloon" && line.Translation.Length > 0)
                {
                    first = line.Translation;
                    firstWidth = line.TranslationWidth;
                    second = [];
                    secondWidth = 0;
                }
                else if (_next[target] >= 0 && SceneId != "kowloon")
                {
                    int next = _next[target];
                    first = _atlas.Lines[next].Glyphs;
                    firstWidth = _atlas.Lines[next].Width;
                    second = line.Translation;
                    secondWidth = line.TranslationWidth;
                }
                else if (line.Translation.Length > 0)
                {
                    first = line.Translation;
                    firstWidth = line.TranslationWidth;
                    second = [];
                    secondWidth = 0;
                }
            }
            if (first.Length + second.Length == 0) return;

            const float Font = 42;
            const float Gap = 74;
            float secondOffset = firstWidth * Font + Gap;
            float pitch = Math.Max(256, secondOffset + secondWidth * Font + Gap);
            float origin = -(float)(Math.Max(0, ambientTime) * 70 % pitch);
            float center = BoardHeight + TickerHeight * .5f;
            for (float x = origin; x < PanelWidth; x += pitch)
            {
                PaintText(ref painter, first, x, center, Font, _tickerColor);
                PaintText(ref painter, second, x + secondOffset, center, Font, _tickerColor);
            }
        }

        private void WriteMotelTitle(ref FoliaPainter painter, Span<FoliaInstance> output)
        {
            // Band 2 uses 38 rows to represent the 2.1 x .62 m sign inside its horizontal margins.
            const float Stretch = PanelWidth / (38 * (2.1f / .62f) * .92f);
            float font = Math.Min(38 * .62f, PanelWidth * .86f / (.92f * Stretch * Math.Max(1, _atlas.TitleWidth)));
            int start = painter.Count;
            PaintCentered(ref painter, _atlas.Title, _atlas.TitleWidth, 421.5f, font, new(.29f, .67f, 1));
            StretchBand(output[start..painter.Count], Stretch);
        }

        private void WriteHologram(ref FoliaPainter painter, Span<FoliaInstance> output, int target)
        {
            const float Stretch = PanelWidth / (78 * (3f / 1.1f));
            int next = target < 0 ? -1 : _next[target];
            var line = next >= 0 ? _atlas.Lines[next] : null;
            ReadOnlySpan<FoliaPreparedGlyph> text = line is null ? [] : line.Glyphs;
            float width = line?.Width ?? 0;
            if (text.IsEmpty && target >= 0)
            {
                text = _atlas.Lines[target].Translation;
                width = _atlas.Lines[target].TranslationWidth;
            }
            bool card = text.IsEmpty;
            if (card)
            {
                text = _atlas.Title;
                width = _atlas.TitleWidth;
            }
            bool subtitle = card && _atlas.Artist.Length > 0;
            float font = Math.Min(78 * .30f, PanelWidth * .88f / (Stretch * Math.Max(1, width)));
            int start = painter.Count;
            PaintCentered(ref painter, text, width, subtitle ? 515 : 526, font, Vector3.One);
            if (subtitle)
            {
                float artistFont = Math.Min(font * .7f, PanelWidth * .88f / (Stretch * Math.Max(1, _atlas.ArtistWidth)));
                PaintCentered(ref painter, _atlas.Artist, _atlas.ArtistWidth, 539, artistFont, new(.70f));
            }
            StretchBand(output[start..painter.Count], Stretch);
        }

        private static void StretchBand(Span<FoliaInstance> instances, float stretch)
        {
            const float Center = PanelWidth * .5f;
            foreach (ref FoliaInstance instance in instances)
            {
                instance.Rect.X = Center + (instance.Rect.X - Center) * stretch;
                instance.Rect.Z *= stretch;
            }
        }

        private static void WriteMetadata(ref FoliaPainter painter, ReadOnlySpan<FoliaPreparedGlyph> glyphs, float width,
            float y, Vector3 ink, bool vertical)
        {
            if (glyphs.IsEmpty) return;
            const float FontLimit = 22;
            float center = y + MetadataHeight * .5f;
            if (vertical)
            {
                float advance = Math.Min(100, PanelWidth / Math.Max(1, glyphs.Length));
                float x = (PanelWidth - advance * (glyphs.Length - 1)) * .5f;
                foreach (ref readonly FoliaPreparedGlyph glyph in glyphs)
                {
                    painter.GlyphCentered(glyph.Glyph, x, center, FontLimit, ink, 1, rotation: -MathF.PI / 2);
                    x += advance;
                }
                return;
            }
            float font = Fit(width, FontLimit);
            PaintCentered(ref painter, glyphs, width, center, font, ink);
        }

        private static void PaintCentered(ref FoliaPainter painter, ReadOnlySpan<FoliaPreparedGlyph> glyphs, float width,
            float center, float font, Vector3 color) =>
            PaintText(ref painter, glyphs, (PanelWidth - width * font) * .5f, center, font, color);

        private static void PaintText(ref FoliaPainter painter, ReadOnlySpan<FoliaPreparedGlyph> glyphs, float x,
            float center, float font, Vector3 color)
        {
            foreach (ref readonly FoliaPreparedGlyph glyph in glyphs)
            {
                float at = x + glyph.Offset * font;
                if (at < -160 || at > PanelWidth + 160) continue;
                float top = center - (glyph.Glyph.BearingY + glyph.Glyph.Height * .5f - glyph.Glyph.CanvasCenterY) * font;
                painter.Glyph(glyph.Glyph, at, top, font, color, 1);
            }
        }

        private static float Fit(float width, float limit) => Math.Min(limit, PanelWidth * (1 - 2 * BoardMargin) / Math.Max(1, width));

        private static Row[] LayoutRows(FoliaPreparedLine line, float boardHeight, CancellationToken token)
        {
            var glyphs = line.Glyphs.AsSpan();
            if (glyphs.IsEmpty) return [];
            Span<Row> best = stackalloc Row[3];
            Span<Row> candidate = stackalloc Row[3];
            float score = 0;
            int chosen = 1;
            for (int count = 1; count <= 3 && count <= glyphs.Length; count++)
            {
                int first = 0;
                for (int row = 0; row < count; row++)
                {
                    int end = row == count - 1 ? glyphs.Length :
                        FindBreak(glyphs, first, glyphs.Length - (count - row - 1), line.Width * (row + 1) / count, token);
                    float width = glyphs[end - 1].Offset + glyphs[end - 1].Glyph.Advance - glyphs[first].Offset;
                    candidate[row] = new(first, end, glyphs[first].Offset, width);
                    first = end;
                }
                float widest = 0;
                foreach (ref readonly Row row in candidate[..count])
                    widest = Math.Max(widest, row.Width);
                float fill = count == 1 ? .56f : .86f / (count * LineGap);
                float nextScore = Fit(widest, boardHeight * fill) * (count is 1 ? 1 : count is 2 ? .94f : .84f);
                if (nextScore <= score) continue;
                candidate[..count].CopyTo(best);
                score = nextScore;
                chosen = count;
            }
            return best[..chosen].ToArray();
        }

        private static int FindBreak(ReadOnlySpan<FoliaPreparedGlyph> glyphs, int start, int maximum, float target, CancellationToken token)
        {
            int selected = start + 1;
            float best = float.PositiveInfinity;
            for (int i = selected; i <= maximum; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                float penalty = glyphs[i - 1].Word == glyphs[i].Word ? .8f : 0;
                float error = MathF.Abs(target - glyphs[i].Offset) + penalty;
                if (error >= best) continue;
                best = error;
                selected = i;
            }
            return selected;
        }

        private static bool IsLyric(FoliaLine line) => !string.IsNullOrWhiteSpace(line.Text) && line.Text is not ("♪" or "♫" or "···");

        private void WriteStationPylon(ref FoliaPainter painter, int target, double position)
        {
            Span<int> digits = stackalloc int[5];
            WritePylonClock(digits, position);
            WritePylonRow(ref painter, digits, 0);
            WritePylonClock(digits, _duration);
            WritePylonRow(ref painter, digits, 1);
            int index = Math.Min(999, target + 1);
            digits[0] = index >= 100 ? index / 100 : target < 0 ? -1 : -2;
            digits[1] = index >= 10 ? index / 10 % 10 : target < 0 ? -1 : -2;
            digits[2] = target < 0 ? -1 : index % 10;
            WritePylonRow(ref painter, digits[..3], 2);
        }

        private static void WritePylonClock(Span<int> digits, double value)
        {
            int seconds = (int)Math.Clamp(value, 0, 5999);
            digits[0] = seconds / 600;
            digits[1] = seconds / 60 % 10;
            digits[2] = -3;
            digits[3] = seconds % 60 / 10;
            digits[4] = seconds % 10;
        }

        private static void WritePylonRow(ref FoliaPainter painter, scoped ReadOnlySpan<int> digits, int row)
        {
            const float DigitWidth = PanelWidth * .135f;
            const float Gap = DigitWidth * .32f;
            const float Half = DigitWidth * .5f;
            const float DigitHeight = 8.07f;
            const float ThicknessX = DigitWidth * .16f;
            const float ThicknessY = .72f;
            Vector3 on = new(.98f, .11f, .035f);
            Vector3 off = new(.027f, .029f, .037f);
            float width = 0;
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && digits[i] != -3 && digits[i - 1] != -3) width += Gap;
                width += digits[i] == -3 ? Half : DigitWidth;
            }
            float x = PanelWidth * .9f - width;
            float y = 481 + 78 * (.37f + row * .2f + .07f);
            for (int i = 0; i < digits.Length; i++)
            {
                int digit = digits[i];
                if (i > 0 && digit != -3 && digits[i - 1] != -3) x += Gap;
                if (digit == -3)
                {
                    painter.Shape(3, x + Half * .4f, y + DigitHeight * .28f, ThicknessX * .45f, ThicknessY, on, 1);
                    painter.Shape(3, x + Half * .4f, y + DigitHeight * .68f, ThicknessX * .45f, ThicknessY, on, 1);
                    x += Half;
                    continue;
                }
                int mask = digit switch { 0 => 0x3f, 1 => 0x06, 2 => 0x5b, 3 => 0x4f, 4 => 0x66, 5 => 0x6d,
                    6 => 0x7d, 7 => 0x07, 8 => 0x7f, 9 => 0x6f, -1 => 0x40, _ => 0 };
                float right = x + DigitWidth - ThicknessX;
                float inside = DigitWidth - 1.4f * ThicknessX;
                float middle = y + DigitHeight * .5f;
                float sideHeight = DigitHeight * .5f - ThicknessY * .85f;
                painter.Shape(3, x + ThicknessX * .7f, y, inside, ThicknessY, (mask & 0x01) != 0 ? on : off, 1);
                painter.Shape(3, right, y + ThicknessY * .65f, ThicknessX, sideHeight, (mask & 0x02) != 0 ? on : off, 1);
                painter.Shape(3, right, middle + ThicknessY * .65f, ThicknessX, sideHeight, (mask & 0x04) != 0 ? on : off, 1);
                painter.Shape(3, x + ThicknessX * .7f, y + DigitHeight, inside, ThicknessY, (mask & 0x08) != 0 ? on : off, 1);
                painter.Shape(3, x, middle + ThicknessY * .65f, ThicknessX, sideHeight, (mask & 0x10) != 0 ? on : off, 1);
                painter.Shape(3, x, y + ThicknessY * .65f, ThicknessX, sideHeight, (mask & 0x20) != 0 ? on : off, 1);
                painter.Shape(3, x + ThicknessX * .7f, middle, inside, ThicknessY, (mask & 0x40) != 0 ? on : off, 1);
                x += DigitWidth;
            }
        }
    }

    internal static Plan Prepare(FoliaTextAtlas atlas, FoliaSong song, string sceneId, CancellationToken token = default) =>
        new(atlas, song, sceneId, token);

    private static double BoardEnd(FoliaLine line) => line.Words.Length == 0 ?
        line.Timing.RenderEnd : Math.Max(line.Start + .067, line.Words[^1].End);
}
