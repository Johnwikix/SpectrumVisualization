using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Services;

namespace WinExSpectrumTest.Canvas
{
    /// <summary>
    /// Host for the visualizer effect pages. Owns the shared audio analyzer and
    /// SMTC service and swaps the active <see cref="IVisualizerEffect"/> on demand.
    /// </summary>
    public sealed partial class CanvasPanel : UserControl
    {
        private readonly SpectrumAnalyzer _analyzer;
        private readonly VisualizerServices _services;
        private IVisualizerEffect? _effect;
        // Effects own GPU resources (render targets, shader effects) that must not be
        // disposed from the UI thread while the render thread is drawing them. Swapped
        // effects are parked here and released by the render thread at the next frame.
        private IVisualizerEffect? _effectPendingDispose;
        private float _width;
        private float _height;
        private bool _disposed;

        public CanvasPanel()
        {
            InitializeComponent();
            SpectrumCanvasControl.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / AppSettings.RefreshRate);
            SpectrumCanvasControl.SizeChanged += OnCanvasSizeChanged;
            SpectrumCanvasControl.Loaded += OnCanvasLoaded;

            _analyzer = new SpectrumAnalyzer();
            _services = new VisualizerServices
            {
                Control = SpectrumCanvasControl,
                Analyzer = _analyzer,
                Media = App.MediaInfoService,
            };
            // The effect is created once the canvas is loaded: effects may need the
            // render device (CanvasAnimatedControl.Device), which only exists then.
        }

        private bool _effectLoaded;

        private void OnCanvasLoaded(object sender, RoutedEventArgs e)
        {
            if (_effectLoaded || _disposed) return;
            _effectLoaded = true;
            LoadEffect(AppSettings.VisualEffect);
        }

        public void ChangeRefreshRate()
        {
            SpectrumCanvasControl.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / AppSettings.RefreshRate);
        }

        /// <summary>Activates the effect page with the given registry id.</summary>
        public void LoadEffect(string id)
        {
            if (_disposed) return;
            IVisualizerEffect effect = EffectRegistry.Create(id);
            effect.Initialize(_services);
            effect.OnResize(_width, _height);
            IVisualizerEffect? old = _effect;
            _effect = effect;
            _effectPendingDispose = old;
            AppSettings.VisualEffect = id;
        }

        /// <summary>Cycles to the next (direction = 1) or previous (direction = -1) effect page.</summary>
        public void SwitchEffect(int direction)
        {
            (string Id, string DisplayName)[] catalog = EffectRegistry.GetCatalog();
            if (catalog.Length == 0) return;
            int index = 0;
            for (int i = 0; i < catalog.Length; i++)
            {
                if (catalog[i].Id == AppSettings.VisualEffect)
                {
                    index = i;
                    break;
                }
            }
            index = (index + direction + catalog.Length) % catalog.Length;
            LoadEffect(catalog[index].Id);
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs args)
        {
            _width = (float)args.NewSize.Width;
            _height = (float)args.NewSize.Height;
            _effect?.OnResize(_width, _height);
        }

        private void SpectrumCanvasControl_Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        {
            _effect?.Update(args.Timing.ElapsedTime.TotalSeconds);
        }

        private void SpectrumCanvasControl_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            try
            {
                // Release the previously swapped-out effect on the render thread, where
                // no Draw can still be using it.
                Interlocked.Exchange(ref _effectPendingDispose, null)?.Dispose();
                _effect?.Draw(args.DrawingSession, (float)sender.Size.Width, (float)sender.Size.Height);
            }
            catch (Exception)
            {
                // Keep the render loop alive on transient device errors.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _effect?.Dispose();
            _effectPendingDispose?.Dispose();
            _analyzer.Dispose();
        }
    }
}
