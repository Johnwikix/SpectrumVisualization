using ComputeSharp;

internal static class ProbeImages
{
    internal static void Save(string path, ReadOnlySpan<float4> pixels, int width, int height)
    {
        using var output = new BinaryWriter(File.Create(path));
        int stride = (width * 3 + 3) & ~3;
        output.Write((ushort)0x4D42);
        output.Write(54 + stride * height);
        output.Write(0);
        output.Write(54);
        output.Write(40);
        output.Write(width);
        output.Write(height);
        output.Write((ushort)1);
        output.Write((ushort)24);
        output.Write(0);
        output.Write(stride * height);
        output.Write(0); output.Write(0); output.Write(0); output.Write(0);
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y * width + x];
                output.Write(Encode(c.Z)); output.Write(Encode(c.Y)); output.Write(Encode(c.X));
            }
            for (int i = width * 3; i < stride; i++) output.Write((byte)0);
        }
    }

    private static byte Encode(float c)
    {
        c = Math.Clamp(c, 0, 1);
        return (byte)Math.Clamp((int)MathF.Round(255 * (c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - .055f)), 0, 255);
    }
}
