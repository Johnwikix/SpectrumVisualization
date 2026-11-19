using System;
using System.Text.Json;
using System.Globalization;
using System.Numerics;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Lyric-PV timeline and film layers adapted from Folia's Hanabi program.</summary>
internal sealed partial class FoliaHanabi
{
    private readonly record struct Burst(string? Kind, int Count, float Radius, float Duration, float Trail, float Twinkle);
    private readonly record struct TextStyle(string Layout, string Block, string Enter, string Sing, string Exit,
        float Size, float Emphasis, float Tilt, float Stagger, bool Outline, bool HardShadow);
    private readonly record struct Region(float X, float Y, float Width, float Height);
    private readonly record struct CameraStyle(float Rise, float Push, float Roll, float Punch);
    private readonly record struct Profile(string Kind, string Family, string Mood, string Palette, string BurstAt,
        TextStyle Text, Region Region, CameraStyle Camera, bool Launch, Burst Burst, Burst SungBurst);

    private readonly record struct PlacedGlyph(FoliaPreparedGlyph Text, Vector2 Center, Vector2 Box, float Size, float Rotation,
        double Enter, double Arrival, double Exit, double Gone, Vector3 Ink, Vector3 Sung, Vector3 Flash,
        Vector3 Outline, Vector3 Shadow, Vector3 Block);
    private sealed record Placement(PlacedGlyph[] Glyphs, Vector4 Bounds);
    private readonly Placement?[] _layouts;
    private readonly int[] _profiles;
    private readonly Transition?[] _tracks;
    private readonly CameraTrack[] _cameras;
    private readonly int[] _paragraph;
    private readonly int[] _paragraphStarts;
    private readonly FoliaHanabiCollage[][] _collages;
    private readonly FoliaHanabiOrnament[][] _ornaments;
    private readonly FoliaHanabiFireworks[] _fireworks;
    private readonly FoliaHanabiFireworks.Palette[] _palettes;
    private readonly double _creditsAt;
    private readonly FoliaTextAtlas _atlas;
    private readonly FoliaGlyph[] _overlayTitle;
    private readonly FoliaGlyph[] _overlaySeal;
    private readonly record struct StageFrame(float Alpha, float Zoom, float Blur, float Y, float Rotation)
    {
        internal static StageFrame Interpolate(ReadOnlySpan<StageFrame> frames, double t)
        {
            float index = Math.Clamp((float)t, 0, 1) * (frames.Length - 1);
            int a = Math.Min((int)index, frames.Length - 2);
            float p = index - a;
            var first = frames[a];
            var second = frames[a + 1];
            return new(float.Lerp(first.Alpha, second.Alpha, p), float.Lerp(first.Zoom, second.Zoom, p),
                float.Lerp(first.Blur, second.Blur, p), float.Lerp(first.Y, second.Y, p), float.Lerp(first.Rotation, second.Rotation, p));
        }
    }

    private sealed record CameraTrack(double Start, double End, Vector4[] Poses)
    {
        internal Vector4 Sample(double time)
        {
            float index = Math.Clamp((float)((time - Start) / Math.Max(.001, End - Start)), 0, 1) * (Poses.Length - 1);
            int first = Math.Min((int)index, Poses.Length - 2);
            return Vector4.Lerp(Poses[first], Poses[first + 1], index - first);
        }
    }

    private sealed record Transition(double Start, double Boundary, double End, StageFrame[] Lead, StageFrame[] Leave, StageFrame[] Enter);
    private readonly FoliaShot[] _shots;
    private readonly ChorusSpan[] _choruses;
    private readonly record struct ChorusSpan(double Start, double End);

    internal FoliaHanabi(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        _atlas = atlas;
        _overlayTitle = OverlayGlyphs(atlas, ["H", "A", "N", "A", "B", "I"]);
        _overlaySeal = OverlayGlyphs(atlas, ["花", "火"]);
        using var program = FoliaSourcePlanner.CompileNativeStage(atlas, request, "hanabi", token);
        var root = program.RootElement;
        var shots = root.GetProperty("shots");
        var chorus = root.GetProperty("chorus");
        var lyricEnd = root.GetProperty("lyricEnd");
        _creditsAt = lyricEnd.ValueKind == JsonValueKind.Number ? lyricEnd.GetDouble() : double.PositiveInfinity;
        _choruses = new ChorusSpan[chorus.GetArrayLength()];
        _shots = new FoliaShot[shots.GetArrayLength()];
        _profiles = new int[_shots.Length];
        _tracks = new Transition?[_shots.Length];
        _cameras = new CameraTrack[_shots.Length];
        _paragraph = new int[_shots.Length];
        var paragraphStarts = new System.Collections.Generic.List<int>();
        int previousParagraph = -1;
        _collages = new FoliaHanabiCollage[_shots.Length][];
        _ornaments = new FoliaHanabiOrnament[_shots.Length][];
        _fireworks = new FoliaHanabiFireworks[_shots.Length];
        _palettes = new FoliaHanabiFireworks.Palette[_shots.Length];
        _layouts = new Placement?[_shots.Length];
        for (int i = 0; i < _choruses.Length; i++)
            _choruses[i] = new(chorus[i].GetProperty("startTime").GetDouble(), chorus[i].GetProperty("endTime").GetDouble());
        for (int i = 0; i < _shots.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var shot = shots[i];
            int paragraph = shot.GetProperty("paragraph").GetInt32();
            _paragraph[i] = paragraph;
            if (paragraph != previousParagraph)
            {
                paragraphStarts.Add(i);
                previousParagraph = paragraph;
            }
            var camera = shot.GetProperty("camera");
            var poses = new Vector4[camera.GetArrayLength()];
            for (int j = 0; j < poses.Length; j++)
            {
                var pose = camera[j];
                poses[j] = new(pose[0].GetSingle(), pose[1].GetSingle(), pose[2].GetSingle(), pose[3].GetSingle());
            }
            _cameras[i] = new(shot.GetProperty("from").GetDouble(), shot.GetProperty("to").GetDouble(), poses);
            var slices = shot.GetProperty("slices");
            int lineIndex = slices.GetArrayLength() == 0 ? -1 : slices[0].GetProperty("lineIndex").GetInt32();
            string id = shot.GetProperty("id").GetString()!;
            string name = shot.GetProperty("kind").GetString()!;
            _profiles[i] = Math.Max(0, Array.FindIndex(Profiles, profile => profile.Kind == name));
            _shots[i] = new(shot.GetProperty("startTime").GetDouble(), shot.GetProperty("endTime").GetDouble(), lineIndex, 0,
                FoliaShotTimeline.Seed(atlas.Seed + ":" + id));
            _collages[i] = FoliaHanabiCollage.Read(shot.GetProperty("collage"), atlas);
            _fireworks[i] = new(shot.GetProperty("fireworks"), shot.GetProperty("launches"));
            _palettes[i] = FoliaHanabiFireworks.Palette.Read(shot.GetProperty("palette"));
            _ornaments[i] = FoliaHanabiOrnament.Read(shot, _palettes[i]);
            if (lineIndex >= 0)
                _layouts[i] = ReadPlacement(Slice(atlas.Lines[lineIndex], slices[0]), shot.GetProperty("text"));
            var track = shot.GetProperty("track");
            if (track.ValueKind == JsonValueKind.Object)
                _tracks[i] = new(track.GetProperty("startTime").GetDouble(), track.GetProperty("endTime").GetDouble(),
                    track.GetProperty("enterEnd").GetDouble(), ReadFrames(track.GetProperty("lead")),
                    ReadFrames(track.GetProperty("leave")), ReadFrames(track.GetProperty("enter")));
        }
        paragraphStarts.Add(_shots.Length);
        _paragraphStarts = paragraphStarts.ToArray();
    }

    private static Placement ReadPlacement(FoliaPreparedLine text, JsonElement placement)
    {
        var bounds = placement.GetProperty("bounds");
        var glyphs = placement.GetProperty("glyphs");
        var available = text.Glyphs;
        var result = new System.Collections.Generic.List<PlacedGlyph>(available.Length);
        for (int i = 0; i < glyphs.GetArrayLength() && i < available.Length; i++)
        {
            var glyph = glyphs[i];
            if (glyph.ValueKind == JsonValueKind.Null)
                continue;
            double enter = glyph[8].GetDouble();
            result.Add(new(available[i], new(glyph[0].GetSingle(), glyph[1].GetSingle()), new(glyph[4].GetSingle(), glyph[5].GetSingle()),
                glyph[2].GetSingle(), glyph[3].GetSingle(), enter, enter + glyph[9].GetDouble(),
                glyph[10].GetDouble(), glyph[11].GetDouble(), FoliaHanabiFireworks.Rgb(glyph[12].GetInt32()),
                FoliaHanabiFireworks.Rgb(glyph[13].GetInt32()), FoliaHanabiFireworks.Rgb(glyph[14].GetInt32()),
                FoliaHanabiFireworks.Rgb(glyph[16].GetInt32()), FoliaHanabiFireworks.Rgb(glyph[17].GetInt32()),
                FoliaHanabiFireworks.Rgb(glyph[18].GetInt32())));
        }
        return new(result.ToArray(), new(bounds.GetProperty("x").GetSingle(), bounds.GetProperty("y").GetSingle(),
            bounds.GetProperty("width").GetSingle(), bounds.GetProperty("height").GetSingle()));
    }

    private static StageFrame[] ReadFrames(JsonElement track)
    {
        var frames = new StageFrame[track.GetArrayLength()];
        for (int i = 0; i < frames.Length; i++)
        {
            var f = track[i];
            frames[i] = new(f.GetProperty("alpha").GetSingle(), f.GetProperty("scale").GetSingle(),
                f.GetProperty("blur").GetSingle(), f.GetProperty("y").GetSingle(), f.GetProperty("rotation").GetSingle());
        }
        return frames;
    }

    private static FoliaPreparedLine Slice(FoliaPreparedLine line, JsonElement slice)
    {
        int[] offsets = StringInfo.ParseCombiningCharacters(line.Source.Text);
        int firstOffset = slice.GetProperty("startOffset").GetInt32();
        int endOffset = slice.GetProperty("endOffset").GetInt32();
        if (firstOffset == 0 && endOffset >= line.Source.Text.Length)
            return line;
        int first = Array.BinarySearch(offsets, firstOffset);
        int last = Array.BinarySearch(offsets, endOffset);
        first = Math.Min(Math.Max(0, first < 0 ? ~first - 1 : first), line.Glyphs.Length);
        last = Math.Clamp(last < 0 ? ~last : last, first, line.Glyphs.Length);
        var glyphs = line.Glyphs.AsSpan(first, last - first).ToArray();
        if (glyphs.Length == 0)
            return line with { Glyphs = glyphs, Width = 0 };
        float offset = glyphs[0].Offset;
        for (int i = 0; i < glyphs.Length; i++)
            glyphs[i] = glyphs[i] with { Index = i, Offset = glyphs[i].Offset - offset };
        return line with { Glyphs = glyphs, Width = glyphs[^1].Offset + glyphs[^1].Glyph.Advance };
    }
    internal float Bloom(double position)
    {
        float weight = 0;
        foreach (var chorus in _choruses)
        {
            if (position < chorus.Start - .5)
                break;
            if (position > chorus.End + .9)
                continue;
            float rise = FoliaPainter.Ease((float)((position - chorus.Start + .5) / .8));
            float fall = 1 - FoliaPainter.Ease((float)((position - chorus.End + .2) / 1.1));
            weight = Math.Max(weight, Math.Min(rise, fall));
        }
        return .36f + weight * 1.17f;
    }
    internal void Write(ref FoliaPainter painter, double position, in FoliaMotionFrame motion)
    {
        int index = FoliaShotTimeline.Find(_shots, position);
        if (index < 0)
            return;
        int paragraph = _paragraph[index];
        int first = _paragraphStarts[paragraph], last = _paragraphStarts[paragraph + 1] - 1;
        var frame = new StageFrame(1, 1, 0, 0, 0);
        if (_tracks[last] is { } outgoing && position >= outgoing.Start && position < outgoing.Boundary)
            frame = StageFrame.Interpolate(outgoing.Lead, (position - outgoing.Start) / (outgoing.Boundary - outgoing.Start));
        if (first > 0 && _tracks[first - 1] is { } incoming && position < incoming.End)
        {
            double blend = (position - incoming.Boundary) / (incoming.End - incoming.Boundary);
            var leaving = StageFrame.Interpolate(incoming.Leave, blend);
            for (int i = _paragraphStarts[paragraph - 1]; i < first; i++)
                WriteLayer(ref painter, i, position, leaving, default);
            frame = StageFrame.Interpolate(incoming.Enter, blend);
        }
        for (int i = first; i <= last; i++)
            WriteLayer(ref painter, i, position, frame, i == index ? motion : default);
        WriteOverlay(ref painter, position, _shots[first].Start, _palettes[index]);
    }

    private static FoliaGlyph[] OverlayGlyphs(FoliaTextAtlas atlas, ReadOnlySpan<string> text)
    {
        var glyphs = new FoliaGlyph[text.Length];
        for (int i = 0; i < text.Length; i++)
            atlas.CharacterGlyphs.TryGetValue(text[i], out glyphs[i]);
        return glyphs;
    }

    private void WriteOverlay(ref FoliaPainter painter, double time, double paragraphStart, in FoliaHanabiFireworks.Palette palette)
    {
        float unit = Math.Min(painter.Width, painter.Height);
        float padX = Math.Max(painter.Width * .07f, unit * .06f);
        float padY = Math.Max(painter.Height * .08f, unit * .06f);
        float seal = unit * .024f;
        painter.Shape(16, padX, padY, seal * 2, seal * 2, palette.Blocks[0], .85f);
        painter.Shape(2, padX - seal * .28f, padY - seal * .28f, seal * 2.56f, seal * 2.56f, palette.Blocks[0], .5f, stroke: 1);
        float bottom = painter.Height - padY, right = painter.Width - padX;
        float arm = unit * .035f;
        for (int i = 0; i < _overlayTitle.Length; i++)
        {
            float size = unit * .017f;
            painter.GlyphCentered(_overlayTitle[i], right, padY + size * (.6f + i * 1.5f), size, palette.Paper, .55f);
        }
        for (int i = 0; i < _overlaySeal.Length; i++)
            painter.GlyphCentered(_overlaySeal[i], padX + seal * (.67f + i * .66f), padY + seal, seal * .66f, palette.Ink, .9f);
        painter.Line(right - arm, bottom, right, bottom, palette.Accent, .6f, 1.4f);
        painter.Line(right, bottom, right, bottom - arm, palette.Accent, .6f, 1.4f);
        painter.Line(right, padY + unit * .165f, right, padY + unit * .245f, palette.Paper, .35f);
        for (int i = 0; i < Math.Min(3, palette.Blocks.Length); i++)
            painter.Shape(16, padX + i * unit * .018f, bottom, unit * .011f, unit * .011f, palette.Blocks[i], .85f);
        painter.Line(padX + unit * .05f, bottom, padX + unit * .16f, bottom, palette.Paper, .35f);

        float reel = Math.Max(20, unit * .055f), pitch = reel * 1.42f;
        float rail = Math.Max(reel * .9f, painter.Width * .022f) - reel * .5f;
        painter.Shape(3, rail, 0, reel, painter.Height, new(.17f, .12f, .22f), .85f);
        float drift = (float)((time - paragraphStart) * unit * .03) % pitch;
        for (int row = -1; row <= (int)(painter.Height / pitch) + 1; row++)
        {
            float y = row * pitch + drift;
            Vector3 tint = palette.Blocks[(row + palette.Blocks.Length) % palette.Blocks.Length];
            painter.Shape(3, rail + reel * .24f, y + 2, reel * .52f, pitch - 4, tint, .9f, radius: 1.8f);
            for (int side = 0; side < 2; side++)
            for (int sprocket = 0; sprocket < 3; sprocket++)
                painter.Shape(3, rail + reel * (.08f + .76f * side), y + (sprocket + .4f) * pitch / 3,
                    reel * .074f, reel * .13f, palette.Paper, .65f, radius: .65f);
        }
    }

    internal void WriteCredits(ref FoliaPainter painter, double position)
    {
        if (position < _creditsAt)
            return;
        float alpha = FoliaPainter.Ease((float)((position - _creditsAt) / .8));
        float font = Math.Min(painter.Height * .054f, painter.Width * .6f / Math.Max(1, _atlas.TitleWidth));
        painter.Text(_atlas.Title, (painter.Width - _atlas.TitleWidth * font) * .5f, painter.Height * .81f, font, new(.95f, .88f, 1), alpha);
        font = Math.Min(font * .44f, painter.Width * .6f / Math.Max(1, _atlas.ArtistWidth));
        painter.Text(_atlas.Artist, (painter.Width - _atlas.ArtistWidth * font) * .5f, painter.Height * .883f, font, new(.83f, .77f, .91f), alpha);
    }

    private void WriteLayer(ref FoliaPainter painter, int index, double position,
        in StageFrame frame, in FoliaMotionFrame motion)
    {
        CameraTrack camera = _cameras[index];
        if (frame.Alpha <= .002f || position < camera.Start || position > camera.End)
            return;
        ref readonly var shot = ref _shots[index];
        ref readonly var profile = ref Profiles[_profiles[index]];
        float alpha = frame.Alpha;
        float unit = Math.Min(painter.Width, painter.Height);
        var center = new Vector2(painter.Width * profile.Region.X, painter.Height * profile.Region.Y);
        ref readonly var colors = ref _palettes[index];
        Vector3 warm = colors.Accent;
        int first = painter.Count;

        _fireworks[index].Write(ref painter, position, alpha);
        Vector4 bounds = _layouts[index]?.Bounds ?? new(-unit * .18f, -unit * .12f, unit * .36f, unit * .24f);
        foreach (ref readonly var ornament in _ornaments[index].AsSpan())
            if (!ornament.Front)
                ornament.Write(ref painter, center, bounds, position, alpha);
        foreach (ref readonly var item in _collages[index].AsSpan())
            if (!item.Front)
                item.Write(ref painter, position, alpha);

        if (_layouts[index] is { } text)
            WriteText(ref painter, text, center, position, alpha, unit, shot.Seed, profile, colors);
        foreach (ref readonly var ornament in _ornaments[index].AsSpan())
            if (ornament.Front)
                ornament.Write(ref painter, center, bounds, position, alpha);
        foreach (ref readonly var item in _collages[index].AsSpan())
            if (item.Front)
                item.Write(ref painter, position, alpha);

        if (motion.Onset > .08f && profile.Burst.Kind is not null)
            Sparks(ref painter, new(center.X + motion.Sign * unit * .3f, center.Y), unit * .55f,
                (1 - motion.Onset) * .46f, shot.Seed + 501, warm, alpha * motion.Onset, profile.Burst);

        Vector4 pose = camera.Sample(position);
        Vector2 screenCenter = new(painter.Width * .5f, painter.Height * .5f);
        var offset = new Vector2(pose.X, pose.Y);
        float zoom = pose.Z * (1 + motion.Low * .055f + motion.Onset * profile.Camera.Punch * .035f);
        painter.TransformFrom(first, screenCenter, screenCenter + offset, zoom, pose.W);
        if (frame.Y != 0 || frame.Zoom != 1 || frame.Rotation != 0)
            painter.TransformFrom(first, screenCenter, screenCenter + new Vector2(0, frame.Y * painter.Height), frame.Zoom, frame.Rotation);
        if (frame.Blur > 0)
            painter.FadeFrom(first, 1, frame.Blur);
    }

    private static void WriteText(ref FoliaPainter painter, Placement text, Vector2 center, double position,
        float alpha, float unit, uint seed, in Profile profile, in FoliaHanabiFireworks.Palette colors)
    {
        if (profile.Text.Block == "band" && text.Glyphs.Length > 0)
        {
            float fade = FoliaPainter.Ease((float)((position - text.Glyphs[0].Enter) / .25)) *
                (1 - FoliaPainter.Ease((float)((position - text.Glyphs[^1].Exit) / .3)));
            var b = text.Bounds;
            float x = center.X + b.X, y = center.Y + b.Y;
            painter.Shape(3, x, y, b.Z, b.W, colors.Blocks[0], alpha * fade, radius: 1.5f);
            if (colors.Blocks.Length > 1)
                painter.Shape(1, x, y + b.W * .9f, b.Z, b.W * .1f, colors.Blocks[1], alpha * fade);
        }
        foreach (ref readonly var glyph in text.Glyphs.AsSpan())
        {
            if (position < glyph.Enter || position >= glyph.Gone)
                continue;
            float progress = FoliaPainter.Ease((float)((position - glyph.Enter) / Math.Max(.001, glyph.Arrival - glyph.Enter)));
            float exit = FoliaPainter.Ease((float)((position - glyph.Exit) / Math.Max(.001, glyph.Gone - glyph.Exit)));
            float show = progress * (1 - exit);
            if (show <= .002f)
                continue;
            Vector2 at = center + glyph.Center;
            float font = glyph.Size, rotation = glyph.Rotation;
            float settle = 1 - progress;
            switch (profile.Text.Enter)
            {
                case "drop": at.Y -= settle * font * .9f; break;
                case "rise": at.Y += settle * font * .75f; break;
                case "pop": font *= .5f + .5f * progress; break;
                case "elastic": at.X += settle * MathF.Cos(progress * 8) * font; break;
                case "spin":
                case "spiral": rotation -= settle * 1.7f; font *= .5f + .5f * progress; break;
                case "wave": at.Y += settle * MathF.Sin(progress * 6 + glyph.Text.Index * .8f) * font; break;
                case "stamp":
                case "zoom": font *= 1 + settle * 1.2f; break;
                case "burst": at -= glyph.Center * settle; break;
            }
            switch (profile.Text.Exit)
            {
                case "ascend": at.Y -= exit * font; break;
                case "fall": at.Y += exit * font * 1.35f; break;
                case "slide": at.X += exit * font * 1.8f; break;
                case "scatter": at += glyph.Center * exit * .33f; rotation += exit * .5f; break;
                case "shrink": font *= 1 - exit; break;
            }
            float age = (float)(position - glyph.Text.Start);
            float sung = age >= 0 ? Math.Clamp(1 - age * 4, 0, 1) : 0;
            if (profile.Text.Sing is "bounce" or "jump")
                at.Y -= MathF.Sin(sung * MathF.PI) * font * .14f;
            if (profile.Text.Sing == "shake")
                at.X += MathF.Sin(age * 58) * sung * font * .11f;
            if (profile.Text.Sing == "swell")
                font *= 1 + sung * .13f;
            Vector3 block = glyph.Block;
            Vector2 box = glyph.Box;
            if (profile.Text.Block is not ("none" or "band") && box.X > 0 && box.Y > 0)
            {
                float radius = profile.Text.Block is "circle" or "pill" ? box.Y * .5f : font * .07f;
                Vector2 pivot = at;
                if (profile.Text.Block == "marker")
                {
                    box.Y *= .3f;
                    pivot.Y += font * .23f;
                }
                var top = pivot - box * .5f;
                if (profile.Text.Block is "double" or "split")
                    painter.Shape(3, top.X + font * .06f, top.Y + font * .09f, box.X, box.Y,
                        colors.Glow, alpha * show, rotation, radius: radius);
                int kind = profile.Text.Block switch { "circle" => 16, "outline" => 7, _ => 3 };
                painter.Shape(kind, top.X, top.Y, box.X, box.Y, block, alpha * show * .91f, rotation, radius: radius);
            }
            Vector3 ink = glyph.Ink;
            if (profile.Text.Sing == "flash")
                ink = Vector3.Lerp(ink, glyph.Flash, sung);
            else if (profile.Text.Sing == "color" && age >= 0)
                ink = Vector3.Lerp(ink, glyph.Sung, FoliaPainter.Ease(age / .18f));
            if (profile.Text.HardShadow)
                painter.GlyphCentered(glyph.Text.Glyph, at.X + font * .07f, at.Y + font * .085f, font, glyph.Shadow, show * alpha, rotation);
            if (profile.Text.Outline)
                painter.GlyphCentered(glyph.Text.Glyph, at.X, at.Y, font, glyph.Outline, show * alpha,
                    rotation, outline: true, outlineWidth: Math.Max(1, font * .065f));
            painter.GlyphCentered(glyph.Text.Glyph, at.X, at.Y, font, ink, show * alpha, rotation,
                glow: profile.Text.Sing == "glow" ? sung : 0);
            if (profile.SungBurst.Kind is not null)
                Sparks(ref painter, at, unit, age, seed + (uint)glyph.Text.Index, block, alpha, profile.SungBurst);
        }
    }

    private static void Sparks(ref FoliaPainter painter, Vector2 center, float unit, float age, uint seed, Vector3 color, float alpha, in Burst burst)
    {
        if (age < 0 || age >= burst.Duration || alpha < .003f)
            return;
        float phase = age / burst.Duration;
        float fade = (1 - phase) * (1 - phase) * alpha;
        float radius = unit * burst.Radius * (1 - MathF.Exp(-age * 3.5f));
        bool droop = burst.Kind is "yanagi" or "kamuro" or "willow-ring" or "palm";
        float gravity = unit * (droop ? .053f : .025f) * age * age;
        for (uint i = 0; i < burst.Count; i++)
        {
            float angle = (i + FoliaShotTimeline.Random(seed, i + 89) * .55f) * MathF.Tau / burst.Count;
            var (sin, cos) = MathF.SinCos(angle);
            var direction = new Vector2(cos, sin);
            float spread = burst.Kind == "ring" ? radius : radius * (.47f + .53f * FoliaShotTimeline.Random(seed, i + 13));
            if (burst.Kind == "star")
                spread *= .69f + .31f * MathF.Cos(angle * 5);
            if (burst.Kind == "heart")
                direction = new(sin * sin * sin, -(13 * cos - 5 * MathF.Cos(angle * 2) - 2 * MathF.Cos(angle * 3) - MathF.Cos(angle * 4)) / 16);
            if (burst.Kind == "mine")
                direction = new(cos * .72f, -MathF.Abs(sin));
            Vector2 tip = center + direction * spread + new Vector2(0, gravity);
            float flicker = 1 - burst.Twinkle * .72f * (.5f + .5f * MathF.Sin(age * 34 + i * 12.6f));
            float thickness = Math.Max(.9f, unit * (burst.Kind == "botan" ? .004f : .0024f));
            if (burst.Trail <= .12f)
            {
                painter.Shape(16, tip.X - thickness, tip.Y - thickness, thickness * 2, thickness * 2, color * 1.42f, fade * flicker);
                continue;
            }
            Vector2 tail = tip - direction * (unit * burst.Trail * (.018f + .036f * (1 - phase))) - new Vector2(0, gravity * burst.Trail * .17f);
            painter.Line(tail.X, tail.Y, tip.X, tip.Y, color * 1.42f, fade * flicker, thickness);
        }
    }
}
