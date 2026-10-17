using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiereGeometry
{
    private Transform ComputeTransform(int index, double time)
    {
        var line = _lines[index];
        _slideStart = double.NegativeInfinity;
        int current = Current(time), first = FirstLive(time, current), basis = first - 1;
        int initialKind = Kind(basis);
        var initial = SlotOf(index, basis, initialKind);
        float dx = initial.X, dy = initial.Y, scale = initial.Scale, rotation = initial.Rotation;
        float alpha = initial.Alpha, orient = initial.Orient, wrap = initial.Wrap;
        float restOrient = orient, restWrap = wrap;
        float widthCap = MaxWidth(initialKind), jitterOn = initialKind == 2 ? 0 : 1;
        float awayX = Stack(initialKind) * Away(index, basis, initialKind);
        float awayY = (1 - Stack(initialKind)) * Away(index, basis, initialKind);
        float hero = index == basis ? 1 : 0;
        int moveCount = 0;
        for (int c = first; c <= Math.Min(_lines.Length - 1, current); c++)
        {
            int beforeKind = Kind(c - 1), afterKind = Kind(c);
            var before = SlotOf(index, c - 1, beforeKind);
            var after = SlotOf(index, c, afterKind);
            bool changed = beforeKind != afterKind;
            if (before == after && !changed)
                continue;
            double start = _lyrics[c].Source.Start - 1.2 + Lag(index - c);
            float phase = Clamp((float)(time - start) / 1.5f), k = Sine(phase);
            if (phase > 0)
                _slideStart = start;
            if (changed)
            {
                widthCap += (MaxWidth(afterKind) - MaxWidth(beforeKind)) * k;
                jitterOn += ((afterKind == 2 ? 0 : 1) - (beforeKind == 2 ? 0 : 1)) * k;
            }
            float awayBefore = Away(index, c - 1, beforeKind), awayAfter = Away(index, c, afterKind);
            awayX += (Stack(afterKind) * awayAfter - Stack(beforeKind) * awayBefore) * k;
            awayY += ((1 - Stack(afterKind)) * awayAfter - (1 - Stack(beforeKind)) * awayBefore) * k;
            hero += ((index == c ? 1 : 0) - (index == c - 1 ? 1 : 0)) * k;
            float fade = after.Alpha < before.Alpha ? Cubic(phase / .6f) : after.Alpha > before.Alpha ? Cubic((phase - .4f) / .6f) : k;
            dx += (after.X - before.X) * k;
            dy += (after.Y - before.Y) * k;
            scale += (after.Scale - before.Scale) * k;
            rotation += (after.Rotation - before.Rotation) * k;
            alpha += (after.Alpha - before.Alpha) * fade;
            orient += (after.Orient - before.Orient) * phase;
            wrap += (after.Wrap - before.Wrap) * k;
            if (phase >= 1)
            {
                restOrient = after.Orient;
                restWrap = after.Wrap;
                moveCount = 0;
            }
            else if (phase > 0)
                _moves[moveCount++] = new(after.Orient, after.Wrap, phase, afterKind == 2 || before.Orient != after.Orient);
        }
        float o = Clamp(orient), w = Clamp(wrap);
        var h0 = line.Flows[0]; var h1 = line.Flows[1];
        var v0 = line.Flows[2]; var v1 = line.Flows[3];
        float fitH = Lerp(Fit(h0.Along, widthCap, scale), Fit(h1.Along, widthCap, scale), w);
        float fitV = Lerp(Fit(v0.Along, _columnCap, scale), Fit(v1.Along, _columnCap, scale), w);
        float fitted = scale * Lerp(fitH, fitV, o);
        float jitter = jitterOn * line.Jitter * _region.Z * Clamp((1 - scale) / .5f);
        float baseX = _region.X + dx + jitter * (1 - orient), baseY = _region.Y + dy + jitter * .5f * orient;
        float alongH = Lerp(h0.InkWidth, h1.InkWidth, w) / _height * fitted;
        float alongV = Lerp(v0.InkHeight, v1.InkHeight, w) / _height * fitted;
        float x = baseX + (ClampInto(baseX, alongH, _band.X, _band.Y) - baseX) * (1 - o) * jitterOn;
        float y = baseY + (ClampInto(baseY, alongV, _band.Z, _band.W) - baseY) * o * jitterOn;
        float t = (float)time, age = (float)(time - _lyrics[index].Source.Start), m = line.Phase;
        float orbitX = MathF.Sin(t * .52f + m) * .03f, orbitY = MathF.Cos(t * .41f + m * 1.3f) * .021f;
        float held = Clamp(hero);
        var heldDrift = HeldDrift(line.Velocity, age);
        float driftX = Lerp(AwayDrift(line.Velocity.X * age + orbitX, awayX), heldDrift.X + orbitX, held);
        float driftY = Lerp(AwayDrift(line.Velocity.Y * age + orbitY, awayY), heldDrift.Y + orbitY, held);
        return new(current, new Vector2(x + driftX, y + driftY) * _height,
            fitted * (1 + .025f * MathF.Sin(t * .43f + m)), rotation + .03f * MathF.Sin(t * .23f + m * .7f), Clamp(alpha), o, w, restOrient, restWrap, moveCount);
    }

    internal void BeginFrame(double time)
    {
        _time = time;
        int current = Current(time), first = FirstLive(time, current);
        Low = Math.Max(0, Math.Min(current, first) - 3);
        High = Math.Min(_lines.Length - 1, Math.Max(current, 0) + 3);
        _protectCount = 0;
        float later = 0;
        for (int j = current; j >= Low && _protectCount < 4; j--)
        {
            float eased = Sine((float)(time - (_lyrics[j].Source.Start - 1.2)) / 1.5f);
            var transform = ComputeTransform(j, time);
            float o = Clamp(transform.Orient);
            float weight = (eased - later) * Clamp(transform.Alpha) * (1 - 4 * o * (1 - o));
            later = eased;
            if (weight > .0001f)
            {
                var flows = _lines[j].Flows;
                float w = transform.Wrap;
                float inkW = Lerp(Lerp(flows[0].InkWidth, flows[1].InkWidth, w), Lerp(flows[2].InkWidth, flows[3].InkWidth, w), transform.Orient);
                float inkH = Lerp(Lerp(flows[0].InkHeight, flows[1].InkHeight, w), Lerp(flows[2].InkHeight, flows[3].InkHeight, w), transform.Orient);
                _protection[_protectCount++] = new(j, transform.Position, new(MathF.Cos(transform.Rotation), MathF.Sin(transform.Rotation)),
                    new Vector2(inkW, inkH) * (.5f * transform.Scale), .5f * _hero * transform.Scale, weight);
            }
            if (eased >= 1)
                break;
        }
    }
    internal void BeginLine(int index)
    {
        _activeLine = index;
        _transform = ComputeTransform(index, _time);
    }
    internal Pose Resolve(int index)
    {
        var line = _lines[_activeLine];
        var meta = line.Glyphs[index];
        var tr = _transform;
        var point = line.Flows[(tr.RestOrient >= .5f ? 2 : 0) + (tr.RestWrap >= .5f ? 1 : 0)].Points[index];
        float orientation = tr.RestOrient, flying = 0;
        for (int i = 0; i < tr.Moves; i++)
        {
            var move = _moves[i];
            var target = line.Flows[(move.Orient >= .5f ? 2 : 0) + (move.Wrap >= .5f ? 1 : 0)].Points[index];
            if (move.Fly)
            {
                float s = Cubic((move.Phase - meta.Delay) / meta.Duration), u = 1 - s;
                point = point * (u * u * u) + (point + meta.C1) * (3 * u * u * s)
                    + (target + meta.C2) * (3 * u * s * s) + target * (s * s * s);
                orientation = Lerp(orientation, move.Orient, s);
                flying = Math.Max(flying, MathF.Sin(MathF.PI * s));
            }
            else
            {
                float k = Sine(move.Phase);
                point = Vector2.Lerp(point, target, k);
                orientation = Lerp(orientation, move.Orient, k);
            }
        }
        ref readonly var glyph = ref _lyrics[_activeLine].Glyphs[index];
        float t = (float)_time;
        float gather = FoliaPainter.Ease((float)(_time - (_lyrics[_activeLine].Source.Start - 1.2 - 1.6)) / 1.8f);
        float scatter = (1 - gather) * (1 - gather) * _decayStrength;
        point += meta.Scatter * (_hero * scatter);
        float age = (float)(_time - glyph.Start - _decayDelay);
        float amount = age > 0 ? _decayStrength * .08f * MathF.Pow(age, 1.5f) * meta.Drift.Z : 0;
        point += new Vector2(meta.Drift.X, meta.Drift.Y) * (_hero * amount);
        float breath = _hero * .022f * Math.Min(1, _decayStrength + .3f);
        point += new Vector2(MathF.Sin(t * .9f + meta.Phase), MathF.Cos(t * 1.13f + meta.Phase * 1.7f)) * breath;
        float rotation = meta.VRotation * orientation + meta.Spin * flying + meta.Drift.W * amount * .18f + tr.Rotation;
        float cos = MathF.Cos(tr.Rotation), sin = MathF.Sin(tr.Rotation);
        var position = tr.Position + new Vector2(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos) * tr.Scale;
        float font = _hero * tr.Scale * meta.Scale * (1 - .28f * flying);
        bool passed = _activeLine < tr.Current || (_activeLine == tr.Current && _time > _lyrics[_activeLine].Source.End);
        float dim = passed ? 1 - .25f * Clamp((float)(_time - _lyrics[_activeLine].Source.End) / 2.5f) : 1;
        float dissolve = 1 - Clamp((amount - 1) / 2);
        float alpha = tr.Alpha * dim * dissolve * (.35f + .65f * gather) * Shield(_activeLine, position, font * .5f, flying);
        return new(position, font, rotation, alpha, flying);
    }
    private float Shield(int line, Vector2 position, float radius, float flying)
    {
        float protect = 0;
        for (int i = 0; i < _protectCount; i++)
        {
            var box = _protection[i];
            if (box.Line == line || box.Weight <= 0)
                continue;
            var d = position - box.Position;
            float u = Math.Abs(d.X * box.Rotation.X + d.Y * box.Rotation.Y) - box.Half.X - radius;
            float v = Math.Abs(d.Y * box.Rotation.X - d.X * box.Rotation.Y) - box.Half.Y - radius;
            float outside = u > 0 && v > 0 ? MathF.Sqrt(u * u + v * v) : Math.Max(Math.Max(u, v), 0);
            protect += box.Weight * (1 - FoliaPainter.Ease(outside / box.Margin));
        }
        return 1 - .6f * Math.Min(1, protect) * (1 - flying);
    }
}
