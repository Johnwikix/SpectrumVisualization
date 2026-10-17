using System;
namespace WinExSpectrumTest.Effects.Folia;

internal static class FoliaMotion
{
    internal static float Enter(float value) => Bezier(.22f, 1, .36f, 1, value);
    internal static float InOut(float value) => Bezier(.62f, 0, .32f, 1, value);
    internal static float Bezier(float x1, float y1, float x2, float y2, float value)
    {
        float target = Math.Clamp(value, 0, 1);
        if (target is 0 or 1)
            return target;
        float low = 0, high = 1, t = target;
        for (int i = 0; i < 12; i++)
        {
            float x = Coordinate(x1, x2, t);
            if (x < target)
                low = t;
            else
                high = t;
            t = (low + high) * .5f;
        }
        return Coordinate(y1, y2, t);
    }
    private static float Coordinate(float a, float b, float t)
    {
        float inverse = 1 - t;
        return 3 * inverse * inverse * t * a + 3 * inverse * t * t * b + t * t * t;
    }
}
