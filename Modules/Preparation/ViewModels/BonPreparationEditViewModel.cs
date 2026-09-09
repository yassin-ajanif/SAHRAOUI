using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Stock.Services;
using GestionCommerciale.Modules.Preparation.Models;
using GestionCommerciale.Modules.Preparation.Services;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Preparation.ViewModels;

public partial class BonPreparationEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IAppSettingsService _settings;
    private readonly IBonPreparationWorkflowService _factureWorkflow;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly IStockMovementService _stock;
    private readonly ClientSoldeDisplay _clientSolde;
    private readonly IClientCreditLimitService _creditLimit;

    public BonPreparationEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IAppSettingsService settings,
        IBonPreparationWorkflowService factureWorkflow,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ICurrentUserSession session,
        ILocaleService locale,
        IUiPreferencesService uiPreferences,
        IPdfService pdf,
        IPdfPrintService pdfPrint,
        IStockMovementService stock,
        IClientAccountStatementService clientLedger,
        IClientCreditLimitService creditLimit)
    {
        _dbFactory = dbFactory;
        _numbers = numbers;
        _settings = settings;
        _factureWorkflow = factureWorkflow;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _session = session;
        _locale = locale;
        _uiPreferences = uiPreferences;
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _stock = stock;
        _clientSolde = new ClientSoldeDisplay(clientLedger, locale);
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _creditLimit = creditLimit;
        _locale.CultureApplied += (_, _) =>
        {
            RefreshBonPreparationUi();
            UpdateBonPreparationTotalLines();
            if (ClientId > 0)
                _ = ClientSolde.RefreshAsync(ClientId, Devise);
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("bon_preparation", LineGridColumns);
        Title = _locale.T("Bp_Title");
        RefreshBonPreparationUi();
        ClientLookup.BindSelection(() => ClientId, c => SelectedClient = c);
    }

    public ClientCategoryFilter ClientLookup { get; } = new();
    public ClientSoldeDisplay ClientSolde => _clientSolde;
    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Clients => ClientLookup.Clients;
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<BonPreparationLineRow> Lignes { get; } = [];
    public ObservableCollection<BonPreparationPaiementRowViewModel> Paiements { get; } = [];

    [ObservableProperty] private int? _bonPreparationId;
    [ObservableProperty] private int _clientId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedClient;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private DateTimeOffset _dateEcheance = new(DateTime.Today.AddDays(30));
    [ObservableProperty] private bool _estPayee;
    [ObservableProperty] private decimal _remiseGlobale;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private decimal _totalHt;
    [ObservableProperty] private decimal _totalTva;
    [ObservableProperty] private decimal _totalTtc;
    [ObservableProperty] private decimal _montantPaye;
    [ObservableProperty] private bool _canEditDraft;

    [ObservableProperty] private decimal _paiementMontant;
    [ObservableProperty] private DateTimeOffset _paiementDate = new(DateTime.Today);
    [ObservableProperty] private ModePaiement _paiementMode = ModePaiement.Especes;
    [ObservableProperty] private string _paiementReference = string.Empty;
    [ObservableProperty] private BonPreparationLineRow? _selectedLine;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _menuDeleteBonPreparation = string.Empty;
    [ObservableProperty] private string _lblFactPayee = string.Empty;
    [ObservableProperty] private string _lblPaid = string.Empty;
    [ObservableProperty] private string _lblUnpaid = string.Empty;
    [ObservableProperty] private string _lblClient = string.Empty;
    [ObservableProperty] private string _wmClientSearch = string.Empty;
    [ObservableProperty] private string _lblDateFacture = string.Empty;
    [ObservableProperty] private string _lblDateEcheance = string.Empty;
    [ObservableProperty] private string _btnRemoveLine = string.Empty;
    [ObservableProperty] private string _lblCatalogHintFacture = string.Empty;
    [ObservableProperty] private string _lblTotals = string.Empty;
    [ObservableProperty] private string _devise = string.Empty;
    [ObservableProperty] private string _totalHtLabel = string.Empty;
    [ObservableProperty] private string _totalTvaLabel = string.Empty;
    [ObservableProperty] private string _totalTtcLabel = string.Empty;
    [ObservableProperty] private string _montantPayeLine = string.Empty;
    [ObservableProperty] private string _lblPaymentsRecorded = string.Empty;
    [ObservableProperty] private string _lblMontant = string.Empty;
    [ObservableProperty] private string _lblPaymentDate = string.Empty;
    [ObservableProperty] private string _lblMode = string.Empty;
    [ObservableProperty] private string _lblReference = string.Empty;
    [ObservableProperty] private string _wmRefShort = string.Empty;
    [ObservableProperty] private string _lblNewPayment = string.Empty;
    [ObservableProperty] private string _btnAddPayment = string.Empty;
    [ObservableProperty] private string _btnDelete = string.Empty;
    [ObservableProperty] private string _btnCancel = string.Empty;
    [ObservableProperty] private string _payEditTooltip = string.Empty;
    [ObservableProperty] private string _lblDocLineColumnsHint = string.Empty;
    [ObservableProperty] private string _lblDocColRef = string.Empty;
    [ObservableProperty] private string _lblDocColDesignation = string.Empty;
    [ObservableProperty] private string _lblDocColQte = string.Empty;
    [ObservableProperty] private string _lblDocColCond = string.Empty;
    [ObservableProperty] private string _wmDocLineUnite = string.Empty;
    [ObservableProperty] private string _lblDocColPuHt = string.Empty;
    [ObservableProperty] private string _lblDocColRemise = string.Empty;
    [ObservableProperty] private string _lblDocColTva = string.Empty;
    [ObservableProperty] private string _lblDocColMontantHt = string.Empty;
    [ObservableProperty] private string _lblDocColMontantTtc = string.Empty;

    public DocumentLineGridColumnState LineGridColumns { get; } = new();
    public bool ShowTotalTva => LineGridColumns.ShowTva && LineGridColumns.ShowMontantTtc;
    public bool ShowTotalTtc => LineGridColumns.ShowMontantTtc && LineGridColumns.ShowTva;
    public bool HighlightHtTotal => !ShowTotalTtc;

    public AutoCompleteFilterPredicate<object?> ProduitAutocompleteFilter => ProductAutoComplete.ItemFilter;
    public AutoCompleteFilterPredicate<object?> PartyAutocompleteFilter => PartyAutoComplete.ItemFilter;

    private bool _suppressAddLinePick;

    private void OnLineGridColumnsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentLineGridColumnState.ShowTva) or nameof(DocumentLineGridColumnState.ShowMontantTtc))
        {
            OnPropertyChanged(nameof(ShowTotalTva));
            OnPropertyChanged(nameof(ShowTotalTtc));
            OnPropertyChanged(nameof(HighlightHtTotal));
            RefreshTotals();
        }
        _uiPreferences.SaveDocumentLineColumns("bon_preparation", LineGridColumns);
    }

    private void RefreshBonPreparationUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        MenuDeleteBonPreparation = _locale.T("Bp_MenuDelete");
        LblClient = _locale.T("Lbl_Client");
        WmClientSearch = _locale.T("Wm_SearchClient");
        LblDateFacture = _locale.T("Lbl_DateFacture");
        LblDateEcheance = _locale.T("Lbl_DateEcheance");
        BtnRemoveLine = _locale.T("Btn_RemoveLine");
        LblCatalogHintFacture = _locale.T("Lbl_CatalogHintFacture");
        LblTotals = _locale.T("Lbl_Totals");
        LblPaymentsRecorded = _locale.T("Lbl_PaymentsRecorded");
        LblMontant = _locale.T("Lbl_Montant");
        LblPaymentDate = _locale.T("Lbl_PaymentDate");
        LblMode = _locale.T("Lbl_Mode");
        LblReference = _locale.T("Lbl_Reference");
        WmRefShort = _locale.T("Lbl_RefShort");
        LblNewPayment = _locale.T("Lbl_NewPayment");
        BtnAddPayment = _locale.T("Btn_AddPayment");
        BtnDelete = _locale.T("Btn_Delete");
        BtnCancel = _locale.T("Btn_Cancel");
        PayEditTooltip = _locale.T("Pay_EditTooltip");
        LblFactPayee = _locale.T("Bp_LblPayee");
        LblPaid = _locale.T("Bp_Paid");
        LblUnpaid = _locale.T("Bp_Unpaid");
        LblDocLineColumnsHint = _locale.T("DocLine_ColumnsHint");
        LblDocColRef = _locale.T("DocLine_ColRef");
        LblDocColDesignation = _locale.T("DocLine_ColDesignation");
        LblDocColQte = _locale.T("DocLine_ColQte");
        LblDocColCond = _locale.T("DocLine_ColCond");
        WmDocLineUnite = _locale.T("DocLine_WmUnite");
        LblDocColPuHt = _locale.T("DocLine_ColPuHt");
        LblDocColRemise = _locale.T("DocLine_ColRemise");
        LblDocColTva = _locale.T("DocLine_ColTva");
        LblDocColMontantHt = _locale.T("DocLine_ColMontantHt");
        LblDocColMontantTtc = _locale.T("DocLine_ColMontantTtc");
    }

    private void UpdateBonPreparationTotalLines()
    {
        TotalHtLabel = _locale.Tf("Doc_FmtHt", TotalHt, Devise).TrimEnd();
        TotalTvaLabel = _locale.Tf("Doc_FmtTva", TotalTva, Devise).TrimEnd();
        TotalTtcLabel = _locale.Tf("Doc_FmtTtc", TotalTtc, Devise).TrimEnd();
        MontantPayeLine = _locale.Tf("Doc_FmtPaye", MontantPaye);
    }

    partial void OnDeviseChanged(string value)
    {
        UpdateBonPreparationTotalLines();
        if (ClientId > 0)
            _ = ClientSolde.RefreshAsync(ClientId, value);
    }

    public Array ModesPaiement => Enum.GetValues(typeof(ModePaiement));

    private bool CanExecuteAddPaiement() => BonPreparationId.HasValue;

    partial void OnMontantPayeChanged(decimal value) => UpdateBonPreparationTotalLines();

    partial void OnBonPreparationIdChanged(int? value)
    {
        AddPaiementCommand.NotifyCanExecuteChanged();
        RemoveBonPreparationCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveBonPreparation() => BonPreparationId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveBonPreparation))]
    private async Task RemoveBonPreparationAsync(CancellationToken cancellationToken)
    {
        if (BonPreparationId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("Bp_Title"), _locale.Tf("Bp_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.BonsPreparation.Include(f => f.Lignes).Include(f => f.Paiements).FirstAsync(f => f.Id == id, cancellationToken);
            await _stock.ResyncBonPreparationStockAsync(
                db, entity.Id, entity.Numero, Enumerable.Empty<(int ProduitId, decimal Quantite)>(), _session.UserId, cancellationToken);
            db.BonsPreparation.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);

            await _dialog.ShowInfoAsync(_locale.T("Bp_Title"), _locale.T("Bp_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Bp_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReloadPaiementsList(IEnumerable<PaiementBonPreparation> paiements)
    {
        Paiements.Clear();
        foreach (var p in paiements.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
            Paiements.Add(new BonPreparationPaiementRowViewModel(this, p));
    }

    public async Task CommitPaiementRowAsync(BonPreparationPaiementRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (BonPreparationId == null || row.Montant <= 0)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), _locale.T("Pay_ErrAmount"), cancellationToken);
            return;
        }

        try
        {
            IsBusy = true;
            await _factureWorkflow.UpdatePaiementAsync(
                BonPreparationId.Value,
                row.Id,
                row.Montant,
                row.Date.DateTime,
                row.Mode,
                row.Reference,
                cancellationToken);
            await LoadAsync(BonPreparationId, cancellationToken);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeletePaiementRowAsync(BonPreparationPaiementRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (BonPreparationId == null) return;
        if (!await _dialog.ConfirmAsync(_locale.T("Pay_Title"), _locale.T("Pay_ConfirmDelete"), cancellationToken))
            return;

        try
        {
            IsBusy = true;
            await _factureWorkflow.DeletePaiementAsync(BonPreparationId.Value, row.Id, cancellationToken);
            await LoadAsync(BonPreparationId, cancellationToken);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HookLines()
    {
        foreach (var row in Lignes)
            row.PropertyChanged += LineChanged;
    }

    private void LineChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshTotals();
        if (e.PropertyName == nameof(BonPreparationLineRow.ProduitId) && sender is BonPreparationLineRow changed && changed.ProduitId != 0)
            ConsolidateDuplicateProductLines();
    }

    partial void OnAddLineCatalogPickChanged(object? value)
    {
        if (_suppressAddLinePick) return;
        if (value is not GestionCommerciale.Modules.Stock.Models.Produit p) return;
        _suppressAddLinePick = true;
        var existing = Lignes.FirstOrDefault(l => l.ProduitId == p.Id && p.Id != 0);
        if (existing != null)
        {
            existing.Quantite += 1;
            SelectedLine = existing;
        }
        else
        {
            var row = new BonPreparationLineRow();
            row.ApplyCatalogProduct(p);
            row.Quantite = 1;
            row.PropertyChanged += LineChanged;
            Lignes.Add(row);
            SelectedLine = row;
        }
        DocumentLineSearchHelper.ClearAfterCatalogPick(() =>
        {
            _suppressAddLinePick = true;
            AddLineCatalogPick = null;
            AddLineSearchText = string.Empty;
            _suppressAddLinePick = false;
        });
        RefreshTotals();
    }

    private void ConsolidateDuplicateProductLines()
    {
        foreach (var g in Lignes.Where(l => l.ProduitId != 0).GroupBy(l => l.ProduitId).ToList())
        {
            if (g.Count() < 2) continue;
            var ordered = g.OrderBy(l => Lignes.IndexOf(l)).ToList();
            var keep = ordered[0];
            var extraQty = ordered.Skip(1).Sum(l => l.Quantite);
            foreach (var line in ordered.Skip(1))
            {
                if (ReferenceEquals(SelectedLine, line))
                    SelectedLine = keep;
                line.PropertyChanged -= LineChanged;
                Lignes.Remove(line);
            }
            keep.Quantite += extraQty;
        }
    }

    private void RefreshTotals()
    {
        var includeTvaInTotals = ShowTotalTtc;
        var lines = Lignes.Select(l => new BonPreparationLigne
        {
            Quantite = l.Quantite,
            PrixUnitaireHT = l.PrixUnitaireHt,
            Remise = l.Remise,
            TauxTVA = includeTvaInTotals ? l.TauxTva : 0
        });
        var (ht, tva, ttc) = DocumentTotalsHelper.BonPreparationTotals(lines, RemiseGlobale);
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        UpdateBonPreparationTotalLines();
        RefreshSuggestedPaiementMontant();
    }

    private void RefreshSuggestedPaiementMontant()
    {
        if (!BonPreparationId.HasValue) return;
        var fullTtc = ComputeFullPaymentTtc();
        PaiementMontant = Math.Round(Math.Max(0, fullTtc - MontantPaye), 2);
    }

    private decimal ComputeFullPaymentTtc() =>
        DocumentTotalsHelper.BonPreparationTtc(
            Lignes.Select(l => new BonPreparationLigne
            {
                Quantite = l.Quantite,
                PrixUnitaireHT = l.PrixUnitaireHt,
                Remise = l.Remise,
                TauxTVA = l.TauxTva
            }),
            RemiseGlobale);

    private async Task<bool> ValidatePaymentsAgainstTtcAsync(decimal ttc, decimal totalPayments, CancellationToken cancellationToken)
    {
        if (!DocumentTotalsHelper.PaymentsExceedTtc(ttc, totalPayments))
            return true;

        await _dialog.ShowErrorAsync(
            _locale.T("Pay_Title"),
            _locale.Tf("Pay_ErrPaymentsExceedTtc", totalPayments, ttc),
            cancellationToken);
        return false;
    }

    partial void OnRemiseGlobaleChanged(decimal value) => RefreshTotals();

    partial void OnSelectedClientChanged(GestionCommerciale.Modules.Tiers.Models.Tiers? value)
    {
        RefreshWhatsAppContact();
        var id = value?.Id ?? 0;
        if (ClientId == id)
        {
            _ = ClientSolde.RefreshAsync(id, Devise);
            return;
        }

        ClientId = id;
    }

    partial void OnClientIdChanged(int value)
    {
        if (SelectedClient?.Id != value)
        {
            ClientLookup.EnsureCategoryFor(value);
            SelectedClient = Clients.FirstOrDefault(c => c.Id == value);
        }

        _ = ClientSolde.RefreshAsync(value, Devise);
        RefreshWhatsAppContact();
    }

    private void RefreshWhatsAppContact()
    {
        var prefill = string.IsNullOrWhiteSpace(Numero) || Numero.Contains('(', StringComparison.Ordinal)
            ? null
            : Numero;
        WhatsApp.SetPhone(SelectedClient?.Telephone, prefill);
    }

    public async Task LoadAsync(int? id, CancellationToken cancellationToken = default)
    {
        BonPreparationId = id;
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await LoadLookupsAsync(db, cancellationToken);

        if (id == null)
        {
            Numero = _locale.T("Bp_NewNumPlaceholder");
            ClientId = Clients.FirstOrDefault()?.Id ?? 0;
            Date = new DateTimeOffset(DateTime.Today);
            DateEcheance = Date.AddDays(30);
            EstPayee = false;
            CanEditDraft = true;
            Title = _locale.T("Bp_NewTitle");
            MontantPaye = 0;
            Paiements.Clear();
            RefreshTotals();
            return;
        }

        var f = await db.BonsPreparation.Include(x => x.Lignes).Include(x => x.Paiements).FirstAsync(x => x.Id == id, cancellationToken);
        Numero = f.Numero;
        ClientId = f.ClientId;
        Date = new DateTimeOffset(f.Date);
        DateEcheance = new DateTimeOffset(f.DateEcheance);
        EstPayee = f.EstPayee;
        RemiseGlobale = f.RemiseGlobale;
        Note = f.Note;
        foreach (var l in f.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            var row = new BonPreparationLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = l.Conditionnement,
                Quantite = l.Quantite,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            };
            Lignes.Add(row);
        }

        HookLines();
        MontantPaye = f.Paiements.Sum(p => p.Montant);
        ReloadPaiementsList(f.Paiements);
        DocumentTotalsHelper.SyncBonPreparationTotalTtc(f);
        if (db.Entry(f).Property(x => x.TotalTtc).IsModified)
            await db.SaveChangesAsync(cancellationToken);
        CanEditDraft = true;
        Title = _locale.Tf("Bp_TitleNum", Numero);
        RefreshTotals();
    }

    private async Task LoadLookupsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var clients = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        ClientLookup.ReplaceAll(clients);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);
    }

    public void Load(int? id) => _ = LoadAsync(id, CancellationToken.None);

    [RelayCommand]
    private void AddLine()
    {
        var p = Produits.FirstOrDefault();
        var row = new BonPreparationLineRow
        {
            ProduitId = p?.Id ?? 0,
            Reference = p?.Reference ?? string.Empty,
            Designation = p?.Designation ?? string.Empty,
            Conditionnement = p?.Unite ?? string.Empty,
            Quantite = 1,
            PrixUnitaireHt = p?.PrixVenteHT ?? 0,
            Remise = 0,
            TauxTva = p?.TauxTVA ?? 20
        };
        row.PropertyChanged += LineChanged;
        Lignes.Add(row);
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveLine(BonPreparationLineRow? row)
    {
        if (row == null) return;
        row.PropertyChanged -= LineChanged;
        Lignes.Remove(row);
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveSelectedLine()
    {
        if (SelectedLine == null) return;
        RemoveLine(SelectedLine);
        SelectedLine = null;
    }

    [RelayCommand]
    private async Task SaveDraftAsync(CancellationToken cancellationToken)
    {
        if (ClientId == 0 || !Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("Bp_Title"), _locale.T("Bp_ErrClientLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(ComputeFullPaymentTtc()))
        {
            await _dialog.ShowErrorAsync(_locale.T("Bp_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        var proposedTtc = ComputeFullPaymentTtc();
        decimal existingTtc = 0m;
        if (BonPreparationId != null)
        {
            await using var checkDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
            existingTtc = await checkDb.BonsPreparation.AsNoTracking()
                .Where(b => b.Id == BonPreparationId)
                .Select(b => b.TotalTtc)
                .FirstOrDefaultAsync(cancellationToken);
            var paid = await checkDb.PaiementsBonPreparation.AsNoTracking()
                .Where(p => p.BonPreparationId == BonPreparationId)
                .SumAsync(p => p.Montant, cancellationToken);
            if (!await ValidatePaymentsAgainstTtcAsync(proposedTtc, paid, cancellationToken))
                return;
        }

        var creditBlock = await _creditLimit.GetBlockMessageIfFactureWouldExceedAsync(
            ClientId, proposedTtc, existingTtc, cancellationToken);
        if (creditBlock is not null)
        {
            await _dialog.ShowErrorAsync(_locale.T("Bp_Title"), creditBlock, cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            BonPreparation entity;
            if (BonPreparationId == null)
            {
                var num = await _numbers.NextBonPreparationAsync(cancellationToken);
                entity = new BonPreparation
                {
                    Numero = num,
                    ClientId = ClientId,
                    Date = Date.DateTime,
                    DateEcheance = DateEcheance.DateTime,
                    EstPayee = EstPayee,
                    RemiseGlobale = RemiseGlobale,
                    Note = Note,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonPreparationLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        Quantite = l.Quantite,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva
                    });
                }

                DocumentTotalsHelper.SyncBonPreparationTotalTtc(entity);
                db.BonsPreparation.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                BonPreparationId = entity.Id;
            }
            else
            {
                entity = await db.BonsPreparation.Include(f => f.Lignes).FirstAsync(f => f.Id == BonPreparationId, cancellationToken);

                entity.ClientId = ClientId;
                entity.Date = Date.DateTime;
                entity.DateEcheance = DateEcheance.DateTime;
                entity.EstPayee = EstPayee;
                entity.RemiseGlobale = RemiseGlobale;
                entity.Note = Note;
                db.BonPreparationLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonPreparationLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        Quantite = l.Quantite,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva
                    });
                }

                DocumentTotalsHelper.SyncBonPreparationTotalTtc(entity);
                await db.SaveChangesAsync(cancellationToken);
            }

            await ResyncStockAsync(db, entity, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("Bp_Title"), _locale.T("Bp_Saved"), cancellationToken);
            await LoadAsync(BonPreparationId, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAddPaiement))]
    private async Task AddPaiementAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;

        if (!BonPreparationId.HasValue)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), _locale.T("Pay_ErrSaveFirst"), cancellationToken);
            return;
        }

        if (PaiementMontant <= 0)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), _locale.T("Pay_ErrAmount"), cancellationToken);
            return;
        }

        var fullTtc = ComputeFullPaymentTtc();
        if (!await ValidatePaymentsAgainstTtcAsync(fullTtc, MontantPaye + PaiementMontant, cancellationToken))
            return;

        try
        {
            IsBusy = true;
            await _factureWorkflow.AddPaiementAsync(BonPreparationId.Value, new PaiementBonPreparation
            {
                Montant = PaiementMontant,
                Date = PaiementDate.DateTime,
                Mode = PaiementMode,
                Reference = PaiementReference,
                CreatedByUserId = _session.UserId
            }, cancellationToken);
            PaiementMontant = 0;
            PaiementReference = string.Empty;
            PaiementDate = new DateTimeOffset(DateTime.Today);
            await LoadAsync(BonPreparationId, cancellationToken);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<BonPreparationListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (BonPreparationId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBonPreparationPdfBytesAsync(cancellationToken);
            if (bytes == null) return;
            var ok = await _dialog.SavePickedFileBytesAsync(_locale.T("Export_PdfPicker"), $"{Numero}.pdf", new[] { "*.pdf" }, bytes, cancellationToken);
            if (ok)
                await _dialog.ShowInfoAsync(_locale.T("Export_Pdf"), _locale.T("Export_Done"), cancellationToken);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Export_Pdf"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrintAsync(CancellationToken cancellationToken)
    {
        if (BonPreparationId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBonPreparationPdfBytesAsync(cancellationToken);
            if (bytes == null) return;
            await _pdfPrint.PrintPdfAsync(bytes, Numero, cancellationToken);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Btn_Print"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<byte[]?> BuildBonPreparationPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (BonPreparationId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var f = await db.BonsPreparation.Include(x => x.Lignes).Include(x => x.Paiements).FirstAsync(x => x.Id == id, cancellationToken);
        var client = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == f.ClientId, cancellationToken);
        return await _pdf.BuildBonPreparationPdfAsync(f, DocumentPartyPdfInfo.FromTiers(client), cancellationToken);
    }

    private Task ResyncStockAsync(AppDbContext db, BonPreparation entity, CancellationToken cancellationToken)
    {
        var lines = entity.Lignes
            .Where(l => l.ProduitId > 0)
            .Select(l => (l.ProduitId, l.Quantite));
        return _stock.ResyncBonPreparationStockAsync(
            db, entity.Id, entity.Numero, lines, _session.UserId, cancellationToken);
    }
}
