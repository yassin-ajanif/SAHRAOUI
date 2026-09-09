using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Shared.Services;

namespace GestionCommerciale.Shared.Helpers;

/// <summary>
/// Shared WhatsApp open action for document / tiers screens.
/// Enabled only when a normalizable phone number is available.
/// </summary>
public sealed partial class WhatsAppOpenHelper : ObservableObject
{
    private readonly IDialogService _dialog;
    private readonly ILocaleService _locale;
    private string _phone = string.Empty;
    private string? _prefillMessage;

    public WhatsAppOpenHelper(IDialogService dialog, ILocaleService locale)
    {
        _dialog = dialog;
        _locale = locale;
        RefreshLabels();
        _locale.CultureApplied += (_, _) => RefreshLabels();
    }

    [ObservableProperty] private string _btnLabel = string.Empty;
    [ObservableProperty] private string _toolTip = string.Empty;
    [ObservableProperty] private bool _canOpen;

    public void RefreshLabels()
    {
        BtnLabel = _locale.T("Btn_WhatsApp");
        ToolTip = _locale.T("Tip_WhatsApp");
    }

    public void SetPhone(string? phone, string? prefillMessage = null)
    {
        _phone = phone?.Trim() ?? string.Empty;
        _prefillMessage = string.IsNullOrWhiteSpace(prefillMessage) ? null : prefillMessage.Trim();
        CanOpen = WhatsAppHelper.NormalizePhoneDigits(_phone) != null;
    }

    partial void OnCanOpenChanged(bool value) => OpenCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (WhatsAppHelper.TryOpenChat(_phone, out var errorKey, _prefillMessage))
            return;

        await _dialog.ShowErrorAsync(
            _locale.T("Btn_WhatsApp"),
            _locale.T(errorKey ?? "WhatsApp_ErrPhone"),
            cancellationToken);
    }
}
