using System;
using System.Globalization;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaChatSender(bool Right, int Avatar);

internal static class FoliaCappellaSenders
{
    internal static FoliaChatSender[] Build(FoliaLine[] lines)
    {
        var senders = new FoliaChatSender[lines.Length];
        ReadOnlySpan<bool> sequence = [false, true, false, true, true];
        ReadOnlySpan<int> leftAvatars = [0, 3, 6, 1, 4];
        int cursor = 0, leftCursor = 0;
        FoliaChatSender? previous = null;
        for (int i = 0; i < lines.Length; i++)
        {
            int compact = 0;
            foreach (char c in lines[i].Text)
                if (!char.IsWhiteSpace(c))
                    compact++;
            string time = lines[i].Start.ToString(CultureInfo.InvariantCulture);
            bool force = (i + 1) % 5 == 0;
            bool carry = compact <= 12 && previous.HasValue && Unit("carry|" + time + "|" + i) <= .68;
            bool flip = !force && Unit("flip|" + time + "|" + i) < .18;
            bool side = sequence[cursor % sequence.Length] ^ flip;
            var sender = force ? new FoliaChatSender(true, 8) : carry ? previous!.Value
                : new FoliaChatSender(side, side ? 8 : leftAvatars[leftCursor % leftAvatars.Length]);
            senders[i] = sender;
            if (force)
            {
                cursor = 0;
                previous = null;
            }
            else
            {
                if (!carry)
                {
                    if (!sender.Right)
                        leftCursor++;
                    cursor++;
                }
                previous = sender;
            }
        }
        return senders;
    }
    private static double Unit(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value)
            hash = unchecked((hash ^ c) * 16777619);
        return hash / (double)uint.MaxValue;
    }
}
