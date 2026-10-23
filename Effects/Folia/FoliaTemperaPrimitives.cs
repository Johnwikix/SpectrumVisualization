using System;
using System.Collections.Generic;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold primitive expansion without interpreter slice/spread graphs for each triangle.</summary>
internal sealed class FoliaTemperaPrimitives(Engine engine, CancellationToken token)
{
    private readonly record struct Transform(double X, double Y, double PivotX, double PivotY,
        double ScaleX, double ScaleY, double Cos, double Sin);

    // One compiler owns this scratch. JsValue output copies numbers and never retains these buffers.
    private readonly List<Transform> _transforms = new(4);
    private double[] _points = [];

    internal static void Install(Engine engine, CancellationToken token)
    {
        var expansion = new FoliaTemperaPrimitives(engine, token);
        // Explicit JsValue callbacks avoid reflection-based CLR binding under NativeAOT.
        engine.SetValue("nativeTemperaPrimitives", new ClrFunction(engine, "nativeTemperaPrimitives", (_, args) => expansion.Append(args)));
        FoliaTemperaTriangulation.Install(engine, token);
    }

    private JsValue Append(JsValue[] args)
    {
        token.ThrowIfCancellationRequested();
        var output = args[0].AsArray();
        var points = args[2].AsArray();
        int length = checked((int)points.Length);
        if (_points.Length < length) Array.Resize(ref _points, length);
        _transforms.Clear();
        for (var node = args[1]; !node.IsNull() && !node.IsUndefined();)
        {
            var value = node.AsObject();
            double rotation = value.Get("rotation").AsNumber();
            _transforms.Add(new(value.Get("x").AsNumber(), value.Get("y").AsNumber(),
                value.Get("px").AsNumber(), value.Get("py").AsNumber(), value.Get("sx").AsNumber(),
                value.Get("sy").AsNumber(), Math.Cos(rotation), Math.Sin(rotation)));
            node = value.Get("parent");
        }
        for (int i = 0; i < length; i += 2)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            double x = points.Get((uint)i).AsNumber(), y = points.Get((uint)(i + 1)).AsNumber();
            foreach (var transform in _transforms)
            {
                // Preserve the original child-to-parent order and double arithmetic.
                double px = (x - transform.PivotX) * transform.ScaleX;
                double py = (y - transform.PivotY) * transform.ScaleY;
                x = px * transform.Cos - py * transform.Sin + transform.X;
                y = px * transform.Sin + py * transform.Cos + transform.Y;
            }
            _points[i] = x;
            _points[i + 1] = y;
        }

        bool stroke = args[3].IsNull();
        var indices = stroke ? null : args[3].AsArray();
        int vertices = length / 2;
        int count = stroke ? Math.Max(0, vertices - 1 + (args[7].AsBoolean() ? 1 : 0))
            : checked((int)indices!.Length) / 3;
        for (int i = 0; i < count; i++)
        {
            if ((i & 63) == 0) token.ThrowIfCancellationRequested();
            var primitive = new JsValue[10];
            primitive[0] = stroke ? 1 : 2;
            primitive[1] = args[4];
            primitive[2] = args[5];
            primitive[3] = stroke ? args[6] : 0;
            for (int v = 0; v < (stroke ? 2 : 3); v++)
            {
                int index = stroke ? (i + v) % vertices : checked((int)indices!.Get((uint)(i * 3 + v)).AsNumber());
                primitive[4 + v * 2] = _points[index * 2];
                primitive[5 + v * 2] = _points[index * 2 + 1];
            }
            if (stroke) primitive[8] = primitive[9] = 0;
            output.Push(new JsArray(engine, primitive));
        }
        return JsValue.Undefined;
    }
}
