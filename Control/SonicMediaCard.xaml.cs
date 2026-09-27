using Microsoft.UI.Xaml.Controls;
using WinExSpectrumTest.ViewModel;

namespace WinExSpectrumTest.Control;

public sealed partial class SonicMediaCard : UserControl
{
    public SonicMediaViewModel ViewModel { get; }
    public SonicMediaCard(SonicMediaViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
