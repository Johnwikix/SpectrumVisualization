using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaDiorama
{
    private Vector3 _cameraVelocity, _aimVelocity;
    private Quaternion _alignment, _orientation;
    private bool _orientationInitialized;
    private float _reading;

    internal void ResetMotion()
    {
        _camera = new(0, .6f, 9);
        _aim = _cameraVelocity = _aimVelocity = default;
        _reading = 0;
        _orientationInitialized = false;
    }
    private static int SeedHash(string seed)
    {
        int hash = 0;
        foreach (char value in seed)
            hash = (hash * 31 + value) % 100000;
        return hash == 0 ? 1 : hash;
    }
    private Vector3 Drift(double time)
    {
        double p = _seedPhase;
        return new((float)((Math.Sin(time * .11 + p) * .6 + Math.Sin(time * .047 + p * 1.7) * .4) * .4 * .62),
            (float)((Math.Sin(time * .09 + p * .7) * .2 + (Math.Sin(time * .13 + p * 1.3) * .6 + Math.Sin(time * .061 + p * 2.1) * .4) * .4) * .62),
            (float)(Math.Sin(time * .05 + p * 1.1) * .5 * .62));
    }
    private static Vector3 ShotOffset(string kind, float progress, float wordProgress, float side, float move)
    {
        const float hero = 5.2f, lift = .5f;
        float e = FoliaPainter.Ease(progress), right = 0, up = lift, back = hero;
        switch (kind)
        {
            case "pushIn":
                back = hero * (1.5f - .8f * e);
                up = lift * (1.2f - .5f * e) + .15f;
                break;
            case "pullBack":
                back = hero * (.85f + 1.15f * e);
                up = lift + 1.7f * e;
                break;
            case "orbit":
                float orbit = side * .55f * (2 * e - 1);
                right = hero * MathF.Sin(orbit);
                back = hero * MathF.Cos(orbit);
                up = lift + .4f;
                break;
            case "track":
                right = side * hero * .34f * MathF.Sin(MathF.PI * Math.Clamp(wordProgress, 0, 1));
                back = hero * 1.06f;
                up = lift + .12f;
                break;
            case "crane":
                float from = side > 0 ? -1.5f : 2.4f, to = side > 0 ? 2.4f : -1.4f;
                up = from + (to - from) * e;
                back = hero * 1.05f;
                break;
            case "swell":
                right = hero * MathF.Sin(side * .4f * e) * 1.1f;
                back = hero * (1 + .35f * e);
                up = lift + 1.2f * e + .3f;
                break;
            case "spiral":
                right = hero * MathF.Sin(side * .7f * (2 * e - 1)) * .85f;
                back = hero * (1.05f - .1f * e);
                up = lift - .8f + 2.4f * e;
                break;
            case "pendulum":
                right = side * hero * .45f * (2 * e - 1);
                up = lift + .9f - .9f * MathF.Sin(MathF.PI * e);
                back = hero * 1.02f;
                break;
            case "flyby":
                right = side * hero * (.7f - 1.4f * e);
                back = hero * (.78f + .12f * MathF.Sin(MathF.PI * e));
                up = lift + .05f;
                break;
            case "arc":
                float arc = side * .5f * MathF.Sin(MathF.PI * e);
                right = hero * MathF.Sin(arc) * 1.3f;
                back = hero * (1.05f + .12f * (1 - MathF.Cos(arc)));
                up = lift + .5f * MathF.Sin(MathF.PI * e);
                break;
            case "float":
                right = side * hero * .16f * MathF.Sin(e * MathF.Tau);
                up = lift + .6f + .5f * MathF.Sin(e * MathF.Tau + 1);
                back = hero * (1.05f + .06f * MathF.Sin(e * MathF.PI));
                break;
            case "glide":
                right = side * hero * .22f * e;
                up = lift + 1.4f * e;
                back = hero * (1.15f - .25f * e);
                break;
            default:
                up = lift + .3f * MathF.Sin(progress * MathF.PI);
                break;
        }
        return new(right * move, lift + (up - lift) * move, hero + (back - hero) * move);
    }
    private void UpdateCamera(Line line, FoliaLine lyrics, Vector3 target, Vector3 aim, double time, float aspect,
        out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        float dt = Math.Clamp(FrameDelta, 0, .1f);
        bool snapped = Vector3.DistanceSquared(_camera, target) > 576;
        if (snapped)
        {
            _camera = target;
            _cameraVelocity = default;
        }
        else
            _camera = Damp(_camera, target, ref _cameraVelocity, dt);
        if (Vector3.DistanceSquared(_aim, aim) > 576)
        {
            _aim = aim;
            _aimVelocity = default;
        }
        else
            _aim = Damp(_aim, aim, ref _aimVelocity, dt);
        var view = Vector3.Normalize(_aim - _camera);
        var look = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, view, Vector3.UnitY));
        var basis = new Matrix4x4(line.Right.X, line.Right.Y, line.Right.Z, 0,
            line.Up.X, line.Up.Y, line.Up.Z, 0, -line.Forward.X, -line.Forward.Y, -line.Forward.Z, 0, 0, 0, 0, 1);
        var aligned = Quaternion.CreateFromRotationMatrix(basis) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, line.Yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, line.Roll);
        if (snapped || !_orientationInitialized)
            _alignment = aligned;
        else
            _alignment = Quaternion.Slerp(_alignment, aligned, 1 - MathF.Exp(-2.6f * dt));
        float ceiling = line.Kind switch { "hold" => .9f, "pushIn" or "track" => .85f, "swell" => .8f,
            "float" => .78f, "glide" => .72f, "crane" or "pullBack" => .7f, "arc" => .62f,
            "pendulum" => .6f, "orbit" => .55f, _ => .5f };
        float read = Math.Min(FoliaPainter.Ease((float)(time - lyrics.Start + .6) / .95f), 1 - FoliaPainter.Ease((float)(time - lyrics.End))) * ceiling;
        _reading += (read - _reading) * (1 - MathF.Exp(-1.2f * dt));
        var desired = Quaternion.Slerp(look, _alignment, _reading);
        var direction = Vector3.Transform(-Vector3.UnitZ, desired);
        float offAxis = MathF.Acos(Math.Clamp(Vector3.Dot(direction, view), -1, 1));
        float vertical = 55 * MathF.PI / 360, horizontal = MathF.Atan(MathF.Tan(vertical) * aspect);
        float limit = Math.Min(vertical, horizontal) * .7f;
        if (offAxis > limit)
            desired = Quaternion.Slerp(desired, look, 1 - limit / offAxis);
        _orientation = snapped || !_orientationInitialized ? desired : Quaternion.Slerp(_orientation, desired, 1 - MathF.Exp(-4.2f * dt));
        _orientationInitialized = true;
        right = Vector3.Transform(Vector3.UnitX, _orientation);
        up = Vector3.Transform(Vector3.UnitY, _orientation);
        forward = Vector3.Transform(-Vector3.UnitZ, _orientation);
    }
    private static Vector3 Damp(Vector3 current, Vector3 target, ref Vector3 velocity, float dt)
    {
        const float omega = 2 / .42f;
        float x = omega * dt, decay = 1 / (1 + x + .48f * x * x + .235f * x * x * x);
        var change = current - target;
        var temporary = (velocity + change * omega) * dt;
        velocity = (velocity - temporary * omega) * decay;
        return target + (change + temporary) * decay;
    }
}
