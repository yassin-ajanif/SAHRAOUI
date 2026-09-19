using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GestionCommerciale.Modules.Auth.ViewModels;

namespace GestionCommerciale.Modules.Auth.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(RefreshDashboardIfAttached);
    }

    private void RefreshDashboardIfAttached()
    {
        if (VisualRoot is null) return;
        if (DataContext is HomeViewModel home)
            _ = home.RefreshAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
