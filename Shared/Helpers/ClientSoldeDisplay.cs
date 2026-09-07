using CommunityToolkit.Mvvm.ComponentModel;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Shared.Services;

namespace GestionCommerciale.Shared.Helpers;

/// <summary>Loads and exposes a client's outstanding solde for document headers.</summary>
public sealed partial class ClientSoldeDisplay : ObservableObject
{
    private readonly IClientAccountStatementService _ledger;
    private readonly ILocaleService _locale;
    private int _loadVersion;

    public ClientSoldeDisplay(IClientAccountStatementService ledger, ILocaleService locale)
    {
        _ledger = ledger;
        _locale = locale;
    }

    [ObservableProperty] private string _text = string.Empty;

    public bool IsVisible => !string.IsNullOrWhiteSpace(Text);

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(IsVisible));

    public void Clear() => Text = string.Empty;

    public async Task RefreshAsync(int clientId, string? devise = null, CancellationToken cancellationToken = default)
    {
        var version = ++_loadVersion;
        if (clientId <= 0)
        {
            Text = string.Empty;
            return;
        }

        try
        {
            var statement = await _ledger.GetStatementAsync(clientId, cancellationToken);
            if (version != _loadVersion)
                return;

            var amount = CurrencyHelper.Format(statement.SoldeActuel, string.IsNullOrWhiteSpace(devise) ? "MAD" : devise);
            Text = _locale.Tf("Client_SoldeFmt", amount);
        }
        catch
        {
            if (version == _loadVersion)
                Text = string.Empty;
        }
    }
}
