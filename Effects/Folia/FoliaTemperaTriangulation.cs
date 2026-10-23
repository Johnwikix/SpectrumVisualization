using System;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>The embedded Mapbox Earcut's first ear-clipping pass (ISC, External/Folia/Earcut-LICENSE.txt).
/// Holes, large polygons and any shape requiring filtering/repair retain the complete original algorithm.</summary>
internal sealed class FoliaTemperaTriangulation(Engine engine, CancellationToken token)
{
    private struct Vertex
    {
        internal double X, Y;
        internal int Previous, Next;
    }

    // The original selects its hashed path beyond 80 vertices. Scratch is compiler-local.
    private readonly Vertex[] _vertices = new Vertex[80];
    private readonly int[] _indices = new int[3 * 78];

    internal static void Install(Engine engine, CancellationToken token)
    {
        var clipping = new FoliaTemperaTriangulation(engine, token);
        engine.SetValue("nativeTemperaTriangulate", new ClrFunction(engine, "nativeTemperaTriangulate", (_, args) => clipping.Clip(args)));
    }

    private JsValue Clip(JsValue[] args)
    {
        token.ThrowIfCancellationRequested();
        var points = args[0].AsArray();
        if (args[1].AsArray().Length != 0 || points.Length < 6 || points.Length > 160 || (points.Length & 1) != 0)
            return JsValue.Null;
        int count = checked((int)points.Length) / 2;
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double x = points.Get((uint)(i * 2)).AsNumber(), y = points.Get((uint)(i * 2 + 1)).AsNumber();
            if (!double.IsFinite(x) || !double.IsFinite(y)) return JsValue.Null;
            _vertices[i] = new() { X = x, Y = y };
        }
        for (int i = 0, j = count - 1; i < count; j = i++)
            sum += (_vertices[j].X - _vertices[i].X) * (_vertices[i].Y + _vertices[j].Y);
        bool forward = sum > 0;
        for (int i = 0; i < count; i++)
        {
            _vertices[i].Previous = (i + (forward ? count - 1 : 1)) % count;
            _vertices[i].Next = (i + (forward ? 1 : count - 1)) % count;
        }
        int ear = forward ? count - 1 : 0, stop = ear, written = 0;
        if (Same(ear, _vertices[ear].Next)) return JsValue.Null;
        while (_vertices[ear].Previous != _vertices[ear].Next)
        {
            token.ThrowIfCancellationRequested();
            int previous = _vertices[ear].Previous, next = _vertices[ear].Next;
            if (Area(previous, ear, next) < 0 && IsEar(ear))
            {
                _indices[written++] = previous;
                _indices[written++] = ear;
                _indices[written++] = next;
                _vertices[next].Previous = previous;
                _vertices[previous].Next = next;
                ear = stop = next;
                continue;
            }
            ear = next;
            // Delegate filtering, intersection repair and splitting to the original Earcut.
            if (ear == stop) return JsValue.Null;
        }
        var result = new JsValue[written];
        for (int i = 0; i < written; i++) result[i] = _indices[i];
        return new JsArray(engine, result);
    }

    private bool Same(int a, int b) => _vertices[a].X == _vertices[b].X && _vertices[a].Y == _vertices[b].Y;
    private double Area(int p, int q, int r) =>
        (_vertices[q].Y - _vertices[p].Y) * (_vertices[r].X - _vertices[q].X)
        - (_vertices[q].X - _vertices[p].X) * (_vertices[r].Y - _vertices[q].Y);

    private bool IsEar(int ear)
    {
        int a = _vertices[ear].Previous, c = _vertices[ear].Next;
        double ax = _vertices[a].X, bx = _vertices[ear].X, cx = _vertices[c].X;
        double ay = _vertices[a].Y, by = _vertices[ear].Y, cy = _vertices[c].Y;
        double x0 = Math.Min(ax, Math.Min(bx, cx)), y0 = Math.Min(ay, Math.Min(by, cy));
        double x1 = Math.Max(ax, Math.Max(bx, cx)), y1 = Math.Max(ay, Math.Max(by, cy));
        for (int p = _vertices[c].Next; p != a; p = _vertices[p].Next)
        {
            double px = _vertices[p].X, py = _vertices[p].Y;
            if (px >= x0 && px <= x1 && py >= y0 && py <= y1 && !(ax == px && ay == py)
                && (cx - px) * (ay - py) >= (ax - px) * (cy - py)
                && (ax - px) * (by - py) >= (bx - px) * (ay - py)
                && (bx - px) * (cy - py) >= (cx - px) * (by - py)
                && Area(_vertices[p].Previous, p, _vertices[p].Next) >= 0)
                return false;
        }
        return true;
    }
}
