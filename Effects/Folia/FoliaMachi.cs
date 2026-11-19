using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Static, material-batched convenience store and a world-space LED lyric panel.</summary>
internal sealed class FoliaMachi
{
    internal const int PanelWidth = 1024;
    internal const int PanelHeight = 320;
    internal const float Near = .1f;
    internal const float Far = 100;
    internal const float FieldOfView = 38 * MathF.PI / 180;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Vertex(Vector4 PositionMaterial, Vector4 Color, Vector4 NormalUvX, Vector4 UvY);

    private enum Material { Matte, Window, Panel, Neon, Road }
    internal static readonly FoliaMachi ConvenienceStore = new();
    internal Vertex[] Mesh { get; }

    private FoliaMachi() => Mesh = BuildStore();

    internal static Matrix4x4 Camera(double position, float aspect)
    {
        float time = (float)position;
        float azimuth = MathF.PI / 4 + .22f * MathF.Sin(time * .11f);
        float elevation = .46f + .043f * MathF.Sin(time * .075f + 1);
        float radius = 20f * Math.Max(1, .85f / aspect);
        var focus = new Vector3(-.9f, 2.35f, -.65f);
        var eye = focus + new Vector3(MathF.Sin(azimuth) * MathF.Cos(elevation),
            MathF.Sin(elevation), MathF.Cos(azimuth) * MathF.Cos(elevation)) * radius;
        return Matrix4x4.CreateLookAt(eye, focus, Vector3.UnitY) *
            Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Near, Far);
    }

    internal static int WritePanel(Span<FoliaInstance> output, FoliaTextAtlas atlas, FoliaSong song, double position, int anchor, float scale)
    {
        var painter = new FoliaPainter(output, PanelWidth, PanelHeight);
        var color = Vector3.Lerp(new(.51f, .93f, 1), song.Palette[3], .30f);
        if (anchor < 0)
        {
            Center(ref painter, atlas.Title, atlas.TitleWidth, 86, 94 * scale, color, .98f);
            Center(ref painter, atlas.Artist, atlas.ArtistWidth, 198, 47 * scale, color, .68f);
            return painter.Count;
        }
        var line = atlas.Lines[anchor];
        float font = Math.Min(133 * scale, (PanelWidth - 108) / Math.Max(line.Width, 1));
        float left = (PanelWidth - line.Width * font) * .5f;
        float y = 111 - font * .59f;
        Vector3 idle = color * .37f;
        foreach (ref readonly var glyph in line.Glyphs.AsSpan())
        {
            float x = left + glyph.Offset * font;
            painter.Glyph(glyph.Glyph, x, y, font, idle, 1);
            float reveal = FoliaStageText.Reveal(glyph, position);
            painter.Glyph(glyph.Glyph, x, y, font, glyph.ActiveColor ?? color, 1, reveal: reveal, revealSoftness: .022f);
        }
        int next = anchor + 1;
        if (next < atlas.Lines.Length)
        {
            var upcoming = atlas.Lines[next];
            const float marqueeFont = 41;
            // The marquee is clipped to its own strip by the material's UV mapping.
            float marqueeWidth = upcoming.Width * marqueeFont;
            float x = PanelWidth + 26 - (float)((position - line.Source.Start) * 135 % (PanelWidth + marqueeWidth + 64));
            painter.Text(upcoming.Glyphs, x, 247, marqueeFont, color, .88f);
        }
        else
            Center(ref painter, line.Translation, line.TranslationWidth, 245, 43, color, .86f);
        return painter.Count;
    }

    private static void Center(ref FoliaPainter painter, ReadOnlySpan<FoliaPreparedGlyph> glyphs, float width, float y, float limit, Vector3 color, float alpha)
    {
        float font = Math.Min(limit, (PanelWidth - 104) / Math.Max(width, 1));
        painter.Text(glyphs, (PanelWidth - width * font) * .5f, y, font, color, alpha);
    }

    private static Vertex[] BuildStore()
    {
        var mesh = new List<Vertex>(2400);
        var concrete = new Vector3(.28f, .33f, .41f);
        var roof = new Vector3(.48f, .54f, .65f);
        var wall = new Vector3(.54f, .52f, .46f);
        var warm = new Vector3(1, .77f, .43f);
        var teal = new Vector3(.14f, .70f, .74f);
        var coral = new Vector3(1, .27f, .35f);
        var asphalt = new Vector3(.14f, .17f, .23f);

        Box(mesh, new(-6, -.6f, -6), new(12, .6f, 12), asphalt, Material.Road);
        Box(mesh, new(-6, 0, -6), new(8.2f, .13f, 8.2f), concrete);
        Box(mesh, new(-6, 0, 5.45f), new(12, .1f, .55f), concrete);
        Box(mesh, new(5.45f, 0, -6), new(.55f, .1f, 11.45f), concrete);
        Box(mesh, new(-5.43f, .13f, -5.46f), new(6.31f, .1f, 5.93f), concrete);
        Box(mesh, new(-5.43f, .22f, -5.46f), new(.25f, 3.02f, 5.91f), wall);
        Box(mesh, new(-5.43f, .22f, -5.46f), new(6.31f, 3.02f, .28f), wall);
        Box(mesh, new(.65f, .22f, -5.18f), new(.23f, 3.02f, 2.62f), wall);
        Box(mesh, new(-5.55f, 3.22f, -5.58f), new(6.62f, .31f, 6.25f), roof);
        Box(mesh, new(-5.47f, 3.5f, -5.49f), new(6.45f, .19f, .15f), concrete);
        Box(mesh, new(-5.47f, 3.5f, .44f), new(6.45f, .18f, .15f), concrete);
        Box(mesh, new(-5.47f, 3.5f, -5.34f), new(.15f, .18f, 5.78f), concrete);
        Box(mesh, new(.83f, 3.5f, -5.34f), new(.15f, .18f, 5.78f), concrete);
        Box(mesh, new(-5.42f, .26f, .34f), new(6.24f, 2.31f, .07f), warm, Material.Window);
        Box(mesh, new(.82f, .25f, -2.57f), new(.07f, 2.31f, 2.97f), warm, Material.Window);
        Box(mesh, new(-5.48f, 2.56f, .40f), new(6.48f, .14f, .13f), teal, Material.Neon);
        Box(mesh, new(-5.48f, 2.76f, .40f), new(6.48f, .42f, .13f), new(.82f, .88f, .85f), Material.Neon);
        Box(mesh, new(-5.48f, 3.17f, .40f), new(6.48f, .09f, .13f), coral, Material.Neon);
        Box(mesh, new(.88f, 2.56f, -5.43f), new(.14f, .15f, 5.96f), teal, Material.Neon);
        Box(mesh, new(.88f, 2.76f, -5.43f), new(.14f, .42f, 5.96f), new(.82f, .88f, .85f), Material.Neon);
        Box(mesh, new(.88f, 3.17f, -5.43f), new(.14f, .09f, 5.96f), coral, Material.Neon);
        Box(mesh, new(-5.45f, .22f, .411f), new(6.35f, .17f, .075f), concrete);
        for (int i = 0; i <= 7; i++)
            Box(mesh, new(-5.39f + i * .89f, .27f, .414f), new(.055f, 2.29f, .058f), concrete);
        for (int i = 0; i <= 4; i++)
            Box(mesh, new(.872f, .27f, -2.56f + i * .73f), new(.059f, 2.29f, .055f), concrete);

        for (int i = 0; i < 6; i++)
        {
            Box(mesh, new(-5.3f + i * 1.88f, .006f, 3.78f), new(1.08f, .012f, .09f), roof);
            Box(mesh, new(3.71f, .006f, -5.28f + i * 1.88f), new(.09f, .012f, 1.04f), roof);
        }
        for (int i = 0; i < 7; i++)
            Box(mesh, new(2.35f + i * .33f, .014f, 2.06f), new(.17f, .012f, 1.38f), wall);
        for (int i = 0; i < 4; i++)
            Box(mesh, new(-4.65f + i * 1.29f, .134f, 1.51f), new(.76f, .036f, .13f), concrete);

        Box(mesh, new(-2.52f, 3.58f, -2.95f), new(.24f, .97f, .24f), concrete);
        Box(mesh, new(-1.44f, 3.58f, -1.88f), new(.24f, .97f, .24f), concrete);
        var normal = Vector3.Normalize(new Vector3(1, 0, 1));
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
        var screen = new Vector3(-1.82f, 5.12f, -2.20f);
        Panel(mesh, screen - normal * .06f, right, normal, 5.77f, 2.01f, concrete, Material.Matte);
        Panel(mesh, screen, right, normal, 5.46f, 1.80f, teal, Material.Panel);
        Panel(mesh, screen + new Vector3(0, 1.07f, 0), right, normal, 5.74f, .075f, coral, Material.Neon);

        Box(mesh, new(1.60f, .13f, -4.37f), new(.18f, 3.83f, .18f), concrete);
        Box(mesh, new(1.13f, 3.45f, -4.43f), new(1.17f, .58f, .24f), teal, Material.Neon);
        Box(mesh, new(1.13f, 4.01f, -4.43f), new(1.17f, .13f, .24f), warm, Material.Neon);
        Box(mesh, new(5.61f, .13f, -1.20f), new(.13f, 4.6f, .13f), concrete);
        Box(mesh, new(4.53f, 4.62f, -1.20f), new(1.21f, .11f, .13f), concrete);
        Box(mesh, new(4.48f, 4.54f, -1.26f), new(.35f, .08f, .25f), warm, Material.Neon);
        Box(mesh, new(-5.43f, .13f, 1.57f), new(.14f, 5.77f, .15f), concrete);
        Box(mesh, new(-5.95f, 5.63f, 1.55f), new(1.11f, .09f, .19f), concrete);
        for (int i = 0; i < 2; i++)
        {
            float z = -3.11f - i * .9f;
            Box(mesh, new(.90f, .13f, z), new(.51f, 1.58f, .57f), i == 0 ? coral * .58f : teal * .63f);
            Box(mesh, new(1.407f, .91f, z + .05f), new(.016f, .62f, .47f), warm, Material.Neon);
        }
        return mesh.ToArray();
    }

    private static void Box(List<Vertex> mesh, Vector3 start, Vector3 size, Vector3 color, Material material = Material.Matte)
    {
        Vector3 x = new(size.X, 0, 0), y = new(0, size.Y, 0), z = new(0, 0, size.Z);
        Face(mesh, start, x, z, -Vector3.UnitY, color, material);
        Face(mesh, start + y, x, z, Vector3.UnitY, color, material);
        Face(mesh, start, x, y, -Vector3.UnitZ, color, material);
        Face(mesh, start + z, x, y, Vector3.UnitZ, color, material);
        Face(mesh, start, z, y, -Vector3.UnitX, color, material);
        Face(mesh, start + x, z, y, Vector3.UnitX, color, material);
    }

    private static void Panel(List<Vertex> mesh, Vector3 center, Vector3 right, Vector3 normal,
        float width, float height, Vector3 color, Material material)
    {
        Vector3 x = right * width, y = Vector3.UnitY * height;
        Face(mesh, center - (x + y) * .5f, x, y, normal, color, material);
    }

    private static void Face(List<Vertex> mesh, Vector3 origin, Vector3 x, Vector3 y, Vector3 normal, Vector3 color, Material material)
    {
        Vertex Make(Vector3 pos, float u, float v) => new(new(pos, (float)material), new(color, 0), new(normal, u), new(v, 0, 0, 0));
        var a = Make(origin, 0, 1);
        var b = Make(origin + x, 1, 1);
        var c = Make(origin + y, 0, 0);
        var d = Make(origin + x + y, 1, 0);
        mesh.Add(a);
        mesh.Add(b);
        mesh.Add(c);
        mesh.Add(b);
        mesh.Add(d);
        mesh.Add(c);
    }
}
