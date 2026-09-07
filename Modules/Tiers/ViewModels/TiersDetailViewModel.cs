using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Modules.Reception.Services;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Tiers.ViewModels;

public sealed class ClientLedgerDisplayRow
{
    public string DateText { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public string Observation { get; init; } = string.Empty;
    public string DebitText { get; init; } = string.Empty;
    public string CreditText { get; init; } = string.Empty;
    public string BalanceText { get; init; } = string.Empty;
}

public partial class TiersDetailViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ILocaleService _locale;
    private readonly IClientAccountStatementService _clientLedgerService;
    private readonly ISupplierAccountStatementService _supplierLedgerService;
    private readonly IPdfService _pdf;
    private readonly IAppSettingsService _settings;

    private TiersListScope _returnScope = TiersListScope.Clients;
    private string _devise = "MAD";

    public TiersListScope ListScope => _returnScope;

    public TiersDetailViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ILocaleService locale,
        IClientAccountStatementService clientLedgerService,
        ISupplierAccountStatementService supplierLedgerService,
        IPdfService pdf,
        IAppSettingsService settings)
    {
        _dbFactory = dbFactory;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _locale = locale;
        _clientLedgerService = clientLedgerService;
        _supplierLedgerService = supplierLedgerService;
        _pdf = pdf;
        _settings = settings;
        Title = _locale.T("TiersDetail_Title");
        RebuildTypeOptions();
        _locale.CultureApplied += (_, _) =>
        {
            RefreshDetailUi();
            if (TiersId.HasValue)
                _ = LoadAsync(TiersId.Value, CancellationToken.None);
        };
        RefreshDetailUi();
    }

    [ObservableProperty] private string _btnBackList = string.Empty;
    [ObservableProperty] private string _wmNom = string.Empty;
    [ObservableProperty] private string _wmIce = string.Empty;
    [ObservableProperty] private string _wmAdresse = string.Empty;
    [ObservableProperty] private string _wmVille = string.Empty;
    [ObservableProperty] private string _wmTelephone = string.Empty;
    [ObservableProperty] private string _wmEmail = string.Empty;
    [ObservableProperty] private string _wmConditions = string.Empty;
    [ObservableProperty] private string _wmMaxCredit = string.Empty;
    [ObservableProperty] private string _lblMaxCredit = string.Empty;
    [ObservableProperty] private string _chkActif = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _lblCategorie = string.Empty;
    [ObservableProperty] private bool _showMaxCredit;

    [ObservableProperty] private string _lblLedgerTitle = string.Empty;
    [ObservableProperty] private string _lblSoldeActuel = string.Empty;
    [ObservableProperty] private string _soldeActuelText = string.Empty;
    [ObservableProperty] private string _btnPdfLedger = string.Empty;
    [ObservableProperty] private string _lblLedgerDate = string.Empty;
    [ObservableProperty] private string _lblLedgerDesignation = string.Empty;
    [ObservableProperty] private string _lblLedgerObservation = string.Empty;
    [ObservableProperty] private string _lblLedgerDebit = string.Empty;
    [ObservableProperty] private string _lblLedgerCredit = string.Empty;
    [ObservableProperty] private string _lblLedgerBalance = string.Empty;
    [ObservableProperty] private string _lblLedgerEmpty = string.Empty;
    [ObservableProperty] private string _lblLedgerSaveFirst = string.Empty;
    [ObservableProperty] private bool _showLedger;
    [ObservableProperty] private bool _showLedgerSaveFirst;
    [ObservableProperty] private bool _showLedgerEmpty;

    public ObservableCollection<ClientLedgerDisplayRow> LedgerRows { get; } = [];
    public ObservableCollection<TypeTiers> Types { get; } = [];
    public ObservableCollection<CategorieTiers> Categories { get; } = [CategorieTiers.Officiel, CategorieTiers.Comptoir];

    [ObservableProperty] private int? _tiersId;
    [ObservableProperty] private TypeTiers _type = TypeTiers.Client;
    [ObservableProperty] private CategorieTiers _categorie = CategorieTiers.Officiel;
    [ObservableProperty] private string _nom = string.Empty;
    [ObservableProperty] private string _ice = string.Empty;
    [ObservableProperty] private string _adresse = string.Empty;
    [ObservableProperty] private string _ville = string.Empty;
    [ObservableProperty] private string _telephone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _conditionsPaiement = string.Empty;
    [ObservableProperty] private string _maxCreditText = string.Empty;
    [ObservableProperty] private bool _actif = true;

    private void RefreshDetailUi()
    {
        BtnBackList = _locale.T("Btn_BackList");
        WmNom = _locale.T("Wm_Nom");
        WmIce = _locale.T("Wm_Ice");
        WmAdresse = _locale.T("Wm_Adresse");
        WmVille = _locale.T("Wm_Ville");
        WmTelephone = _locale.T("Wm_Telephone");
        WmEmail = _locale.T("Wm_Email");
        WmConditions = _locale.T("Wm_ConditionsPaiement");
        WmMaxCredit = _locale.T("Wm_MaxCredit");
        LblMaxCredit = _locale.T("Lbl_MaxCredit");
        ChkActif = _locale.T("Lbl_Actif");
        BtnSave = _locale.T("Btn_Save");
        LblCategorie = _locale.T("Lbl_CategorieTiers");
        UpdateShowMaxCredit();
        LblLedgerTitle = _returnScope == TiersListScope.Fournisseurs
            ? _locale.T("SupplierLedger_Title")
            : _locale.T("ClientLedger_Title");
        LblSoldeActuel = _locale.T("ClientLedger_SoldeActuel");
        BtnPdfLedger = _locale.T("Btn_Pdf");
        LblLedgerDate = _locale.T("ClientLedger_ColDate");
        LblLedgerDesignation = _locale.T("ClientLedger_ColDesignation");
        LblLedgerObservation = _locale.T("ClientLedger_ColObservation");
        LblLedgerDebit = _locale.T("ClientLedger_ColDebit");
        LblLedgerCredit = _locale.T("ClientLedger_ColCredit");
        LblLedgerBalance = _locale.T("ClientLedger_ColBalance");
        LblLedgerEmpty = _returnScope == TiersListScope.Fournisseurs
            ? _locale.T("SupplierLedger_Empty")
            : _locale.T("ClientLedger_Empty");
        LblLedgerSaveFirst = _returnScope == TiersListScope.Fournisseurs
            ? _locale.T("SupplierLedger_SaveFirst")
            : _locale.T("ClientLedger_SaveFirst");
    }

    private void RebuildTypeOptions()
    {
        Types.Clear();
        switch (_returnScope)
        {
            case TiersListScope.Clients:
                Types.Add(TypeTiers.Client);
                Types.Add(TypeTiers.LesDeux);
                break;
            case TiersListScope.Fournisseurs:
                Types.Add(TypeTiers.Fournisseur);
                Types.Add(TypeTiers.LesDeux);
                break;
        }
    }

    public void Load(int? tiersId) => Load(tiersId, TiersListScope.Clients);

    public void Load(int? tiersId, TiersListScope returnScope)
    {
        _returnScope = returnScope;
        RebuildTypeOptions();
        TiersId = tiersId;
        LedgerRows.Clear();
        SoldeActuelText = string.Empty;
        ShowLedger = returnScope == TiersListScope.Clients || returnScope == TiersListScope.Fournisseurs;
        ShowLedgerSaveFirst = tiersId == null && ShowLedger;
        ShowLedgerEmpty = false;
        RefreshDetailUi();

        if (tiersId == null)
        {
            Nom = string.Empty;
            Ice = string.Empty;
            Adresse = string.Empty;
            Ville = string.Empty;
            Telephone = string.Empty;
            Email = string.Empty;
            ConditionsPaiement = string.Empty;
            MaxCreditText = string.Empty;
            Type = returnScope == TiersListScope.Fournisseurs ? TypeTiers.Fournisseur : TypeTiers.Client;
            Categorie = CategorieTiers.Officiel;
            Actif = true;
            UpdateShowMaxCredit();
            Title = returnScope == TiersListScope.Fournisseurs
                ? _locale.T("TiersDetail_NewSupplier")
                : _locale.T("TiersDetail_NewClient");
            return;
        }

        _ = LoadAsync(tiersId.Value, CancellationToken.None);
    }

    private async Task LoadAsync(int id, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var cfg = await _settings.GetAsync(cancellationToken);
            _devise = string.IsNullOrWhiteSpace(cfg.Devise) ? "MAD" : cfg.Devise.Trim();

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var t = await db.Tiers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (t == null) return;

            Type = t.Type;
            if (!Types.Contains(Type))
                Types.Add(Type);

            Categorie = t.Categorie;
            Nom = t.Nom;
            Ice = t.ICE;
            Adresse = t.Adresse;
            Ville = t.Ville;
            Telephone = t.Telephone;
            Email = t.Email;
            ConditionsPaiement = t.ConditionsPaiement;
            MaxCreditText = t.MaxCredit is { } max ? max.ToString("0.##") : string.Empty;
            Actif = t.Actif;
            Title = _returnScope == TiersListScope.Fournisseurs
                ? _locale.Tf("Tiers_TitleSupplierFmt", t.Nom)
                : _locale.Tf("Tiers_TitleClientFmt", t.Nom);

            ShowLedgerSaveFirst = false;
            var isClient = t.Type is TypeTiers.Client or TypeTiers.LesDeux;
            var isSupplier = t.Type is TypeTiers.Fournisseur or TypeTiers.LesDeux;
            UpdateShowMaxCredit();
            ShowLedger = _returnScope switch
            {
                TiersListScope.Clients => isClient,
                TiersListScope.Fournisseurs => isSupplier,
                _ => false
            };

            if (ShowLedger)
                await LoadLedgerAsync(id, cancellationToken);
            else
            {
                LedgerRows.Clear();
                SoldeActuelText = string.Empty;
                ShowLedgerEmpty = false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadLedgerAsync(int tiersId, CancellationToken cancellationToken)
    {
        var statement = _returnScope == TiersListScope.Fournisseurs
            ? await _supplierLedgerService.GetStatementAsync(tiersId, cancellationToken)
            : await _clientLedgerService.GetStatementAsync(tiersId, cancellationToken);
        LedgerRows.Clear();
        foreach (var row in statement.Rows)
        {
            LedgerRows.Add(new ClientLedgerDisplayRow
            {
                DateText = row.Date.ToString("dd/MM/yyyy"),
                Designation = row.Designation,
                Observation = row.Observation,
                DebitText = row.Debit > 0 ? FormatAmount(row.Debit) : string.Empty,
                CreditText = row.Credit > 0 ? FormatAmount(row.Credit) : string.Empty,
                BalanceText = FormatAmount(row.Balance)
            });
        }

        SoldeActuelText = FormatAmount(statement.SoldeActuel);
        ShowLedgerEmpty = LedgerRows.Count == 0;
    }

    private string FormatAmount(decimal amount) => CurrencyHelper.Format(amount, _devise);

    [RelayCommand]
    private async Task ExportLedgerPdfAsync(CancellationToken cancellationToken)
    {
        if (TiersId is not { } id || !ShowLedger) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var tiers = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == id, cancellationToken);
            var statement = _returnScope == TiersListScope.Fournisseurs
                ? await _supplierLedgerService.GetStatementAsync(id, cancellationToken)
                : await _clientLedgerService.GetStatementAsync(id, cancellationToken);
            var bytes = _returnScope == TiersListScope.Fournisseurs
                ? await _pdf.BuildSupplierAccountStatementPdfAsync(
                    tiers, statement, DocumentPartyPdfInfo.FromTiers(tiers), cancellationToken)
                : await _pdf.BuildClientAccountStatementPdfAsync(
                    tiers, statement, DocumentPartyPdfInfo.FromTiers(tiers), cancellationToken);
            var fileName = $"Etat-{tiers.Nom}.pdf";
            var ok = await _dialog.SavePickedFileBytesAsync(
                _locale.T("Export_PdfPicker"), fileName, new[] { "*.pdf" }, bytes, cancellationToken);
            if (ok)
            {
                var title = _returnScope == TiersListScope.Fournisseurs
                    ? _locale.T("SupplierLedger_Title")
                    : _locale.T("ClientLedger_Title");
                await _dialog.ShowInfoAsync(title, _locale.T("Export_Done"), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var title = _returnScope == TiersListScope.Fournisseurs
                ? _locale.T("SupplierLedger_Title")
                : _locale.T("ClientLedger_Title");
            await _dialog.ShowErrorAsync(title, ex.Message, cancellationToken);
        }
    }

    partial void OnTypeChanged(TypeTiers value) => UpdateShowMaxCredit();

    private void UpdateShowMaxCredit() =>
        ShowMaxCredit = _returnScope == TiersListScope.Clients
            && Type is TypeTiers.Client or TypeTiers.LesDeux;

    private bool TryParseMaxCredit(out decimal? maxCredit, out string? errorKey)
    {
        maxCredit = null;
        errorKey = null;
        if (!ShowMaxCredit || string.IsNullOrWhiteSpace(MaxCreditText))
            return true;

        if (!decimal.TryParse(MaxCreditText.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) || parsed < 0)
        {
            errorKey = "Tiers_ErrMaxCredit";
            return false;
        }

        maxCredit = parsed;
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Nom))
        {
            await _dialog.ShowErrorAsync(_locale.T("Dlg_Validation"), _locale.T("Tiers_ErrName"), cancellationToken);
            return;
        }

        if (!TryParseMaxCredit(out var maxCredit, out var maxCreditError))
        {
            await _dialog.ShowErrorAsync(_locale.T("Dlg_Validation"), _locale.T(maxCreditError!), cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            if (TiersId == null)
            {
                var t = new Models.Tiers
                {
                    Type = Type,
                    Categorie = Categorie,
                    Nom = Nom.Trim(),
                    ICE = Ice.Trim(),
                    Adresse = Adresse.Trim(),
                    Ville = Ville.Trim(),
                    Telephone = Telephone.Trim(),
                    Email = Email.Trim(),
                    ConditionsPaiement = ConditionsPaiement.Trim(),
                    MaxCredit = maxCredit,
                    Actif = Actif
                };
                db.Tiers.Add(t);
                await db.SaveChangesAsync(cancellationToken);
                TiersId = t.Id;
            }
            else
            {
                var t = await db.Tiers.FirstAsync(x => x.Id == TiersId, cancellationToken);
                t.Type = Type;
                t.Categorie = Categorie;
                t.Nom = Nom.Trim();
                t.ICE = Ice.Trim();
                t.Adresse = Adresse.Trim();
                t.Ville = Ville.Trim();
                t.Telephone = Telephone.Trim();
                t.Email = Email.Trim();
                t.ConditionsPaiement = ConditionsPaiement.Trim();
                t.MaxCredit = ShowMaxCredit ? maxCredit : null;
                t.Actif = Actif;
                await db.SaveChangesAsync(cancellationToken);
            }

            await _dialog.ShowInfoAsync(_locale.T("Tiers_InfoTitle"), _locale.T("Tiers_Saved"), cancellationToken);
            if (TiersId.HasValue)
                await LoadAsync(TiersId.Value, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<TiersListViewModel>();
        list.Configure(_returnScope);
        _workspace.Open(list);
    }
}
