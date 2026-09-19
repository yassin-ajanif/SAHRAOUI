using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Reporting.ViewModels;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Auth.ViewModels;

public class HomeViewModel : BaseViewModel
{
    private readonly ILocaleService _locale;
    private readonly ILicenseService _license;
    private readonly IAppSettingsService _appSettings;

    public HomeViewModel(ILocaleService locale, ICurrentUserSession session, IServiceProvider sp,
        ILicenseService license, IAppSettingsService appSettings)
    {
        _locale = locale;
        _license = license;
        _appSettings = appSettings;
        Title = _locale.T("Nav_Home");
        _locale.CultureApplied += (_, _) =>
        {
            OnPropertyChanged(nameof(Welcome));
            OnPropertyChanged(nameof(TrialMessage));
            OnPropertyChanged(nameof(ShowTrialMessage));
            Title = _locale.T("Nav_Home");
        };

        if (session.CanAccessReporting)
            Dashboard = sp.GetRequiredService<ReportingViewModel>();

        _ = RefreshTrialMessageAsync();
    }

    /// <summary>Reporting dashboard; null when the user role cannot access reporting.</summary>
    public ReportingViewModel? Dashboard { get; }

    public bool ShowDashboard => Dashboard is not null;

    public bool ShowWelcomeOnly => Dashboard is null;

    public string Welcome => _locale.T("Home_Welcome");

    public string? TrialMessage { get; private set; }

    public bool ShowTrialMessage => TrialMessage is not null;

    private async Task RefreshTrialMessageAsync()
    {
        var settings = await _appSettings.GetAsync(default);

        if (_license.IsLicensed(settings))
        {
            TrialMessage = null;
            return;
        }

        if (settings.TrialStartedAt is null)
        {
            TrialMessage = null;
            return;
        }

        var remaining = (settings.TrialStartedAt.Value.AddDays(3) - DateTime.UtcNow).Days;

        if (remaining <= 0)
        {
            TrialMessage = _locale.T("Home_TrialExpired");
        }
        else if (remaining == 1)
        {
            TrialMessage = _locale.T("Home_TrialLastDay");
        }
        else
        {
            TrialMessage = _locale.Tf("Home_TrialDaysRemaining", remaining);
        }

        OnPropertyChanged(nameof(TrialMessage));
        OnPropertyChanged(nameof(ShowTrialMessage));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshTrialMessageAsync();
        if (Dashboard is not null)
            await Dashboard.RefreshAsync(cancellationToken);
    }
}
