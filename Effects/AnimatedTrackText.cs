using AnimatedWin2dControls.Controls.AnimatedTextBlock;
using AnimatedWin2dControls.Controls.AnimatedTextBlock.Effects;
using AnimatedWin2dControls.Controls.AnimatedTextBlock.Enums;
using AnimatedWin2dControls.Controls.AnimatedTextBlock.Internals;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.UI;

namespace WinExSpectrumTest.Effects;

/// <summary>Hosts the reference AnimatedTextBlock effects on Aurora's render thread.</summary>
internal sealed class AnimatedTrackText : IDisposable
{
    private string _text = "";
    private string _oldText = "";
    private string _effectName = "none";
    private CanvasTextLayout? _oldLayout;
    private CanvasTextFormat? _format;
    private ITextEffect? _effect;
    private List<TextDiffResult>? _diffs;
    private double _elapsed;
    public CanvasTextLayout? Layout { get; private set; }
    public bool IsAnimating => _oldLayout != null;

    public static string NormalizeEffect(string? name) => name is
        "default" or "fade" or "wipe" or "blur" or "elastic" or "motion-blur" or "pivot" or "zoom"
        ? name : "none";

    public void SetText(CanvasDevice device, string? text, CanvasTextFormat format,
        float width, float height, string effectName, bool animate)
    {
        text ??= "";
        effectName = NormalizeEffect(effectName);
        bool changed = text != _text;
        FinishAnimation();
        if (effectName != _effectName)
        {
            _effectName = effectName;
            _effect = effectName switch
            {
                "default" => new TextDefaultEffect(),
                "fade" => new TextFadeEffect(),
                "wipe" => new TextWipeEffect(),
                "blur" => new TextBlurEffect(),
                "elastic" => new TextElasticEffect(),
                "motion-blur" => new TextMotionBlurEffect(),
                "pivot" => new TextPivotEffect(),
                "zoom" => new TextZoomEffect(),
                _ => null,
            };
        }
        _format = format;
        _oldText = _text;
        _oldLayout = Layout;
        Layout = new CanvasTextLayout(device, text, format, width, height);
        _text = text;
        if (animate && changed && _effect != null)
        {
            _oldLayout ??= new CanvasTextLayout(device, "", format, width, height);
            _diffs = GraphemeClusterDiff.Diff(
                TextRenderingHelper.GenerateGraphemeClusters(_oldText, _oldLayout),
                TextRenderingHelper.GenerateGraphemeClusters(_text, Layout));
            _elapsed = 0;
            if (_effect is TextWipeEffect wipe) wipe.Reset();
        }
        else FinishAnimation();
    }

    public bool Update(double seconds)
    {
        if (!IsAnimating || _effect == null || _diffs == null) return false;
        _elapsed += seconds;
        bool finished = true;
        if (_effect is TextWipeEffect wipe)
        {
            wipe.Advance(TimeSpan.FromSeconds(seconds));
            finished = wipe.IsFinished;
        }
        else
        {
            Span<int> offsets = stackalloc int[5];
            offsets.Clear();
            foreach (TextDiffResult diff in _diffs)
            {
                int index = offsets[(int)diff.Type]++;
                // Bound the stagger for long titles; a new track must settle promptly.
                double delay = Math.Min(0.3, index * _effect.DelayPerCluster.TotalSeconds);
                float progress = (float)Math.Clamp((_elapsed - delay) /
                    Math.Max(0.001, _effect.AnimationDuration.TotalSeconds), 0, 1);
                if (diff.OldGlyphCluster != null) diff.OldGlyphCluster.Progress = progress;
                if (diff.NewGlyphCluster != null) diff.NewGlyphCluster.Progress = progress;
                finished &= progress >= 1;
            }
        }
        if (finished) FinishAnimation();
        return true; // Includes the last frame so the shadow cache reaches its final state.
    }

    public void Draw(CanvasDrawingSession session, Vector2 position, Color color)
    {
        if (Layout == null) return;
        if (!IsAnimating || _effect == null || _diffs == null)
        {
            session.DrawTextLayout(Layout, position, color);
            return;
        }
        Matrix3x2 transform = session.Transform;
        try
        {
            session.Transform = Matrix3x2.CreateTranslation(position) * transform;
            _effect.DrawText(_oldText, _text, _diffs, _oldLayout!, Layout, _format!,
                color, null!, AnimatedTextBlockRedrawState.Animating, session);
        }
        finally { session.Transform = transform; }
    }

    private void FinishAnimation()
    {
        if (_diffs != null) TextRenderingHelper.DisposeShapedText(_diffs);
        _diffs = null;
        _oldLayout?.Dispose();
        _oldLayout = null;
    }

    public void Dispose()
    {
        FinishAnimation();
        Layout?.Dispose();
        Layout = null;
    }
}
