using System;
using System.Collections.Generic;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaMachi
{
    private const double DoorCycle = 4.35;

    internal void WriteActorTransforms(Span<Matrix4x4> output, Plan plan, double position, double ambientTime)
    {
        float door = DoorOpenness(plan, position);
        int fan = 0;
        int flag = 0;
        for (int i = 0; i < Actors.Length; i++)
        {
            ref readonly Actor actor = ref Actors[i];
            output[i] = actor.Kind switch
            {
                1 => Matrix4x4.CreateTranslation(-.86f * door, 0, 0),
                2 => Matrix4x4.CreateTranslation(.86f * door, 0, 0),
                3 => RotateAt(actor.Pivot, actor.Axis, (float)(-ambientTime * (plan.FanSpeed + fan++ * .37) * (Math.PI * 2) % (Math.PI * 2))),
                4 => RotateAt(actor.Pivot, Vector3.UnitY, .12f * Wave(ambientTime, 1.7, flag++ * 1.9)),
                5 => TrafficPose(plan, ambientTime),
                _ => Matrix4x4.Identity,
            };
        }
    }

    private static Matrix4x4 RotateAt(Vector3 pivot, Vector3 axis, float angle) =>
        Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(axis, angle) * Matrix4x4.CreateTranslation(pivot);

    private static Matrix4x4 TrafficPose(Plan plan, double ambientTime)
    {
        const double Window = 15;
        const double Speed = 6.5;
        // Enter and leave beyond the extended streets, even during a wide camera sweep.
        const float Reach = 34;
        const double StartSlack = Window - 2 * Reach / Speed;
        bool station = plan.SceneId == "station";
        int latest = (int)Math.Floor(ambientTime / Window);
        for (int window = latest; window >= latest - 1; window--)
        {
            if (Hash01(plan.TrafficSeed, window, 0x11) > .62) continue;
            double start = window * Window + Hash01(plan.TrafficSeed, window, 0x22) * StartSlack;
            float distance = (float)((ambientTime - start) * Speed);
            if (distance < 0 || distance > Reach * 2) continue;
            int lane = (int)(Hash01(plan.TrafficSeed, window, 0x33) * (station ? 2 : 4));
            float along = distance - Reach;
            Vector3 position = lane switch
            {
                0 => new(along, 30, station ? 3.36f : 3.12f),
                1 => new(-along, 30, station ? 4.99f : 4.38f),
                2 => new(3.12f, 30, -along),
                _ => new(4.38f, 30, along)
            };
            float yaw = lane switch { 1 => MathF.PI, 2 => MathF.PI / 2, 3 => -MathF.PI / 2, _ => 0 };
            return Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position);
        }
        return Matrix4x4.Identity;
    }

    private static double[] BuildDoorEvents(FoliaLine[] lines, float interval, float phase)
    {
        var events = new List<double>();
        double lastEnd = 0;
        bool previous = false;
        bool chorus = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text) || line.Text is "♪" or "♫" or "···") continue;
            double gap = line.Start - lastEnd;
            if (gap >= interval * .6f)
            {
                for (double time = lastEnd + interval * (.35 + phase * .4); time < line.Start - DoorCycle; time += interval)
                    events.Add(time);
            }
            if (previous && (gap >= 1.55 || chorus != line.IsChorus))
                events.Add(Math.Max(0, line.Start - .7));
            lastEnd = Math.Max(BoardEnd(line), lastEnd);
            chorus = line.IsChorus;
            previous = true;
        }
        events.Sort();
        int count = 0;
        for (int i = 0; i < events.Count; i++)
            if (i == 0 || events[i] - events[i - 1] >= DoorCycle)
                count++;
        if (count == 0) return [];
        var result = new double[count];
        int at = 0;
        for (int i = 0; i < events.Count; i++)
            if (i == 0 || events[i] - events[i - 1] >= DoorCycle)
                result[at++] = events[i];
        return result;
    }

    private static float DoorOpenness(Plan plan, double time)
    {
        if (plan.SceneId != "konbini") return 0;
        double local;
        if (plan.DoorEvents.Length == 0)
        {
            double phase = time / plan.DoorInterval + plan.DoorPhase;
            local = (phase - Math.Floor(phase)) * plan.DoorInterval;
        }
        else
        {
            int index = Array.BinarySearch(plan.DoorEvents, time);
            if (index < 0) index = ~index - 1;
            if (index < 0) return 0;
            local = time - plan.DoorEvents[index];
        }
        if (local < 0 || local >= DoorCycle) return 0;
        if (local < .75)
        {
            float t = (float)(1 - local / .75);
            return 1 - t * t * t;
        }
        if (local < 3.35) return 1;
        float close = (float)(local - 3.35);
        float inverse = 1 - close;
        return close < .5f ? 1 - 4 * close * close * close : 4 * inverse * inverse * inverse;
    }

    private static float NextRandom(ref uint state)
    {
        uint t = state = unchecked(state + 0x6d2b79f5);
        t = unchecked((t ^ (t >> 15)) * (1 | t));
        t = unchecked(t + (t ^ (t >> 7)) * (61 | t)) ^ t;
        return (float)((t ^ (t >> 14)) / 4294967296d);
    }

    private static double Hash01(uint seed, int index, uint salt) =>
        unchecked(((seed + (uint)((index + 1) * 97)) ^ salt) * 2654435761u) / 4294967296d;
}
