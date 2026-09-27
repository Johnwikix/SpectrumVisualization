using WinExSpectrumTest.Effects;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var state = new RadialSpectrumState();
float[] left = Enumerable.Repeat(0.8f, 512).ToArray();
float[] right = Enumerable.Repeat(0.2f, 512).ToArray();
foreach (int rate in new[] { 8000, 12000, 44100, 48000, 96000 })
foreach (int count in new[] { 32, 128, 255, 512 })
{
    state.Configure(count, rate, 512);
    state.Update(left, right, 1, 0, 1, 1f / 60);
    int n = state.Levels.Length;
    float lowSeam = MathF.Abs(state.Levels[0] - state.Levels[^1]);
    float highSeam = MathF.Abs(state.Levels[n / 2 - 1] - state.Levels[n / 2]);
    Console.WriteLine($"rate={rate} bars={n} low seam={lowSeam:F6} high seam={highSeam:F6}");
    Require(lowSeam < 0.06f && highSeam < 0.08f, "Stereo seam forms a hard step");
    Require(state.Levels[n / 4] > state.Levels[n * 3 / 4] * 2, "Stereo separation was lost away from seams");
    for (int i = 0; i < n; i++)
        Require(state.Peaks[i] >= state.Levels[i], "Peak cap falls inside the bar");

    state.Update(left, left, 1, 0, 1, 1f / 60);
    for (int i = 0; i < n / 2; i++)
    {
        Require(MathF.Abs(state.Levels[i] - state.Levels[n - 1 - i]) < 0.00001f, "Mono bars are not mirror symmetric");
        Require(MathF.Abs(state.FrequencyPositions[i] - state.FrequencyPositions[n - 1 - i]) < 0.00001f, "Color/tilt mapping is asymmetric");
    }
    state.Update(new float[512], new float[512], 1, 0, 1, 0.1f);
    Require(state.Levels.All(v => v == 0), "Silence left stale bars");
}
Console.WriteLine("PASS: stereo seams, channel separation, mirrored mapping, peak caps, silence and sample-rate changes");
