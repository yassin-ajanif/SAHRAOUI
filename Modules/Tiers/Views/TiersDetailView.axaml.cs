using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GestionCommerciale.Modules.Tiers.ViewModels;

namespace GestionCommerciale.Modules.Tiers.Views;

public partial class TiersDetailView : UserControl
{
    public TiersDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnLedgerRowTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button) return;
        if (sender is not Border { DataContext: ClientLedgerDisplayRow row }) return;
        if (DataContext is not TiersDetailViewModel vm || !row.IsNavigable) return;

        vm.OpenLedgerDocumentCommand.Execute(row);
        e.Handled = true;
    }
}
