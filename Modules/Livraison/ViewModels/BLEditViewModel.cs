using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Modules.Facturation.ViewModels;
using GestionCommerciale.Modules.CommandeClient.ViewModels;
using GestionCommerciale.Modules.Livraison.Models;
using GestionCommerciale.Modules.Livraison.Services;
using GestionCommerciale.Modules.Stock.Services;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Livraison.ViewModels;

using BonCommandeReferenceStorage = GestionCommerciale.Modules.Livraison.BonCommandeReferenceStorage;

public partial class BLEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IBonLivraisonWorkflowService _workflow;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IStockMovementService _stock;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly IAppSettingsService _settings;
    private readonly IFactureBlLinkService _blLinkService;
    private readonly IFactureBccLinkService _bccLinkService;
    private readonly IClientCreditLimitService _creditLimit;
    private readonly IClientAccountStatementService _clientLedger;
    private int _clientSoldeLoadVersion;

    public BLEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IBonLivraisonWorkflowService workflow,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ICurrentUserSession session,
        ILocaleService locale,
        IUiPreferencesService uiPreferences,
        IStockMovementService stock,
        IPdfService pdf,
        IPdfPrintService pdfPrint,
        IAppSettingsService settings,
        IFactureBlLinkService blLinkService,
        IFactureBccLinkService bccLinkService,
        IClientCreditLimitService creditLimit,
        IClientAccountStatementService clientLedger)
    {
        _dbFactory = dbFactory;
        _numbers = numbers;
        _workflow = workflow;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _session = session;
        _locale = locale;
        _uiPreferences = uiPreferences;
        _stock = stock;
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _settings = settings;
        _blLinkService = blLinkService;
        _bccLinkService = bccLinkService;
        _creditLimit = creditLimit;
        _clientLedger = clientLedger;
        _locale.CultureApplied += (_, _) =>
        {
            RefreshBlUi();
            if (ClientId > 0)
                _ = RefreshClientSoldeAsync(ClientId);
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("bon_livraison", LineGridColumns);
        Title = _locale.T("BL_Title");
        Lignes.CollectionChanged += LignesOnCollectionChanged;
        RefreshBlUi();
        ClientLookup.BindSelection(() => ClientId, c => SelectedClient = c);
    }

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _btnToInvoice = string.Empty;
    [ObservableProperty] private string _menuDeleteBl = string.Empty;
    [ObservableProperty] private string _lblClient = string.Empty;
    [ObservableProperty] private string _wmClientSearch = string.Empty;
    [ObservableProperty] private string _lblDateBl = string.Empty;
    [ObservableProperty] private string _btnAddLine = string.Empty;
    [ObservableProperty] private string _btnApplyProduct = string.Empty;
    [ObservableProperty] private string _btnRemoveLine = string.Empty;
    [ObservableProperty] private string _lblAddProduct = string.Empty;
    [ObservableProperty] private string _wmAddProduct = string.Empty;
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
    [ObservableProperty] private string _lblTotals = string.Empty;
    [ObservableProperty] private string _invoicedLabel = string.Empty;
    [ObservableProperty] private string _bccLabel = string.Empty;
    [ObservableProperty] private string _lblLinkedBcc = string.Empty;
    [ObservableProperty] private string _btnAddBcc = string.Empty;
    [ObservableProperty] private string _wmBonCommandeReference = string.Empty;
    [ObservableProperty] private string _bonCommandeReference = string.Empty;
    public bool HasInvoicedLabel => !string.IsNullOrEmpty(InvoicedLabel);
    public bool HasBccLabel => !string.IsNullOrWhiteSpace(BonCommandeReference);

    partial void OnInvoicedLabelChanged(string value) => OnPropertyChanged(nameof(HasInvoicedLabel));
    partial void OnBonCommandeReferenceChanged(string value)
    {
        UpdateBccLabel();
        OnPropertyChanged(nameof(HasBccLabel));
    }

    [ObservableProperty] private decimal _totalHt;
    [ObservableProperty] private decimal _totalTva;
    [ObservableProperty] private decimal _totalTtc;
    [ObservableProperty] private string _totalHtLabel = "HT 0,00";
    [ObservableProperty] private string _totalTvaLabel = "TVA 0,00";
    [ObservableProperty] private string _totalTtcLabel = "TTC 0,00";
    [ObservableProperty] private string _devise = string.Empty;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;
    private bool _suppressAddLinePick;

    public DocumentLineGridColumnState LineGridColumns { get; } = new(supportsLineRemise: true);

    public AutoCompleteFilterPredicate<object?> ProduitAutocompleteFilter => ProductAutoComplete.ItemFilter;
    public AutoCompleteFilterPredicate<object?> PartyAutocompleteFilter => PartyAutoComplete.ItemFilter;

    public bool ShowTotalTva => LineGridColumns.ShowTva && LineGridColumns.ShowMontantTtc;
    public bool ShowTotalTtc => LineGridColumns.ShowMontantTtc && LineGridColumns.ShowTva;
    public bool HighlightHtTotal => !ShowTotalTtc;

    private void OnLineGridColumnsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentLineGridColumnState.ShowTva) or nameof(DocumentLineGridColumnState.ShowMontantTtc))
        {
            OnPropertyChanged(nameof(ShowTotalTva));
            OnPropertyChanged(nameof(ShowTotalTtc));
            OnPropertyChanged(nameof(HighlightHtTotal));
            RefreshTotals();
        }

        _uiPreferences.SaveDocumentLineColumns("bon_livraison", LineGridColumns);
    }

    private void LignesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (BLLineRow row in e.NewItems)
                row.PropertyChanged += LineOnPropertyChanged;
        if (e.OldItems != null)
            foreach (BLLineRow row in e.OldItems)
                row.PropertyChanged -= LineOnPropertyChanged;
        RefreshTotals();
    }

    private void LineOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshTotals();

    private void RefreshBlUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        BtnToInvoice = _locale.T("Btn_ToInvoice");
        MenuDeleteBl = _locale.T("BL_MenuDelete");
        LblClient = _locale.T("Lbl_Client");
        WmClientSearch = _locale.T("Wm_SearchClient");
        LblDateBl = _locale.T("Lbl_DateBL");
        BtnAddLine = _locale.T("Btn_AddLine");
        BtnApplyProduct = _locale.T("Btn_ApplyProduct");
        BtnRemoveLine = _locale.T("Btn_RemoveLine");
        LblAddProduct = _locale.T("Devis_LblAddProduct");
        WmAddProduct = _locale.T("Devis_WmSearchProduct");
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
        LblTotals = _locale.T("Lbl_Totals");
        LblLinkedBcc = _locale.T("Fact_LinkedBccs");
        BtnAddBcc = _locale.T("Fact_AddBcc");
        WmBonCommandeReference = _locale.T("Fact_WmBonCommandeReference");
        UpdateBccLabel();
        UpdateTotalLabels(TotalHt, TotalTva, TotalTtc);
    }

    private void UpdateBccLabel()
    {
        BccLabel = string.IsNullOrWhiteSpace(BonCommandeReference)
            ? string.Empty
            : _locale.Tf("BL_LinkedBcc", BonCommandeReference);
    }

    public ClientCategoryFilter ClientLookup { get; } = new();
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Clients => ClientLookup.Clients;
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<BLLineRow> Lignes { get; } = [];

    [ObservableProperty] private int? _blId;
    [ObservableProperty] private int? _devisId;
    [ObservableProperty] private int _clientId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedClient;
    [ObservableProperty] private string _clientSoldeText = string.Empty;
    public bool HasClientSolde => !string.IsNullOrWhiteSpace(ClientSoldeText);

    partial void OnClientSoldeTextChanged(string value) => OnPropertyChanged(nameof(HasClientSolde));

    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private BLLineRow? _selectedLine;

    public bool CanEdit => !IsReadOnly;

    partial void OnBlIdChanged(int? value) => RemoveBlCommand.NotifyCanExecuteChanged();

    private bool CanRemoveBl() => BlId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveBl))]
    private async Task RemoveBlAsync(CancellationToken cancellationToken)
    {
        if (BlId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("BL_DlgShort"), _locale.Tf("BL_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var blockedMsg = await BonLivraisonDeleteReferencedMessage.BuildIfBlockedAsync(db, id, _locale, cancellationToken);
            if (blockedMsg != null)
            {
                await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), blockedMsg, cancellationToken);
                return;
            }

            var entity = await db.BonsLivraison.Include(b => b.Lignes).FirstAsync(b => b.Id == id, cancellationToken);
            await _stock.ResyncBonLivraisonStockAsync(db, entity.Id, entity.Numero, Enumerable.Empty<(int ProduitId, decimal QuantiteLivree)>(), null, cancellationToken);
            db.BonsLivraison.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            await _dialog.ShowInfoAsync(_locale.T("BL_DlgShort"), _locale.T("BL_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedClientChanged(GestionCommerciale.Modules.Tiers.Models.Tiers? value)
    {
        var id = value?.Id ?? 0;
        if (ClientId == id)
        {
            _ = RefreshClientSoldeAsync(id);
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

        _ = RefreshClientSoldeAsync(value);
    }

    private async Task RefreshClientSoldeAsync(int clientId)
    {
        var version = ++_clientSoldeLoadVersion;
        if (clientId <= 0)
        {
            ClientSoldeText = string.Empty;
            return;
        }

        try
        {
            var statement = await _clientLedger.GetStatementAsync(clientId);
            if (version != _clientSoldeLoadVersion)
                return;

            var amount = CurrencyHelper.Format(statement.SoldeActuel, Devise);
            ClientSoldeText = _locale.Tf("BL_ClientSoldeFmt", amount);
        }
        catch
        {
            if (version == _clientSoldeLoadVersion)
                ClientSoldeText = string.Empty;
        }
    }

    partial void OnAddLineCatalogPickChanged(object? value)
    {
        if (_suppressAddLinePick || !CanEdit) return;
        if (value is not GestionCommerciale.Modules.Stock.Models.Produit p) return;
        _suppressAddLinePick = true;
        var existing = Lignes.FirstOrDefault(l => l.ProduitId == p.Id && p.Id != 0);
        if (existing != null)
        {
            existing.QuantiteLivree += 1;
            existing.QuantiteCommandee += 1;
            SelectedLine = existing;
        }
        else
        {
            var row = new BLLineRow();
            row.ApplyCatalogProduct(p);
            row.QuantiteCommandee = 1;
            row.QuantiteLivree = 1;
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

    public async Task LoadAsync(int? id, CancellationToken cancellationToken = default)
    {
        BlId = id;
        BonCommandeReference = string.Empty;
        BccLabel = string.Empty;
        DevisId = null;
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var clients = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        ClientLookup.ReplaceAll(clients);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);

        InvoicedLabel = string.Empty;
        _linkedFactureId = null;
        _linkedBccId = null;

        if (id == null)
        {
            Numero = "(brouillon)";
            ClientId = Clients.FirstOrDefault()?.Id ?? 0;
            IsReadOnly = false;
            Title = _locale.T("BL_NewTitle");
            RefreshTotals();
            return;
        }

        var factNum = await _blLinkService.GetInvoicingStatusAsync(id.Value, cancellationToken);
        if (factNum != null)
            InvoicedLabel = _locale.Tf("BL_FacturedOn", factNum);

        var b = await db.BonsLivraison.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        _linkedFactureId = b.FactureId;
        _linkedBccId = b.BonCommandeClientId;
        DevisId = b.DevisId;
        var (storedBccRef, userNote) = BonCommandeReferenceStorage.Parse(b.Note);
        BonCommandeReference = storedBccRef;
        if (string.IsNullOrWhiteSpace(BonCommandeReference) && b.BonCommandeClientId is int linkedBccId)
        {
            BonCommandeReference = await db.BonsCommandeClient.AsNoTracking()
                .Where(x => x.Id == linkedBccId)
                .Select(x => x.Numero)
                .FirstAsync(cancellationToken);
        }
        UpdateBccLabel();
        Numero = b.Numero;
        ClientId = b.ClientId;
        Date = new DateTimeOffset(b.Date);
        Note = userNote;
        foreach (var l in b.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new BLLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = prod?.Unite ?? string.Empty,
                QuantiteCommandee = l.QuantiteCommandee,
                QuantiteLivree = l.QuantiteLivree,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            });
        }

        IsReadOnly = false;
        Title = _locale.Tf("BL_TitleNum", Numero);
        RefreshTotals();
    }

    public void Load(int? id) => _ = LoadAsync(id, CancellationToken.None);

    public async Task LoadNewFromBonCommandeClientAsync(int bonCommandeClientId, CancellationToken cancellationToken = default)
    {
        BlId = null;
        BonCommandeReference = string.Empty;
        BccLabel = string.Empty;
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var bcc = await db.BonsCommandeClient.Include(x => x.Lignes).FirstAsync(x => x.Id == bonCommandeClientId, cancellationToken);

        var clients = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        ClientLookup.ReplaceAll(clients);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);

        ClientId = bcc.ClientId;
        DevisId = bcc.DevisId;
        Date = new DateTimeOffset(DateTime.Today);
        Note = string.Empty;
        Numero = "(brouillon)";
        foreach (var l in bcc.Lignes.OrderBy(x => x.Id))
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new BLLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                QuantiteCommandee = l.QuantiteCommandee,
                QuantiteLivree = l.QuantiteCommandee,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            });
        }

        IsReadOnly = false;
        AppendBonCommandeNumero(bcc.Numero);
        Title = _locale.Tf("BL_NewFromBcc", bcc.Numero);
        RefreshTotals();
    }

    public async Task LoadFromDevisAsync(int devisId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var clients = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        ClientLookup.ReplaceAll(clients);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);

        var d = await db.Devis.Include(x => x.Lignes).FirstAsync(x => x.Id == devisId, cancellationToken);
        DevisId = d.Id;
        ClientId = d.ClientId;
        Date = new DateTimeOffset(DateTime.Today);
        BlId = null;
        Numero = "(brouillon)";
        Lignes.Clear();
        foreach (var l in d.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new BLLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = prod?.Unite ?? string.Empty,
                QuantiteCommandee = l.Quantite,
                QuantiteLivree = l.Quantite,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            });
        }

        IsReadOnly = false;
        Title = _locale.T("BL_FromDevis");
        RefreshTotals();
    }

    public void LoadFromDevis(int devisId) => _ = LoadFromDevisAsync(devisId, CancellationToken.None);

    [RelayCommand]
    private void AddLine()
    {
        if (!CanEdit) return;
        var p = Produits.FirstOrDefault();
        Lignes.Add(new BLLineRow
        {
            ProduitId = p?.Id ?? 0,
            Reference = p?.Reference ?? string.Empty,
            Designation = p?.Designation ?? string.Empty,
            Conditionnement = p?.Unite ?? string.Empty,
            QuantiteCommandee = 1,
            QuantiteLivree = 1,
            PrixUnitaireHt = p?.PrixVenteHT ?? 0,
            TauxTva = p?.TauxTVA ?? 20
        });
    }

    [RelayCommand]
    private void RemoveLine(BLLineRow? row)
    {
        if (!CanEdit || row == null) return;
        Lignes.Remove(row);
    }

    [RelayCommand]
    private void ApplyProductToSelected() => ApplyProduct(SelectedLine);

    private void ApplyProduct(BLLineRow? row)
    {
        if (row == null) return;
        var p = Produits.FirstOrDefault(x => x.Id == row.ProduitId);
        if (p == null) return;
        row.Reference = p.Reference;
        row.Designation = p.Designation;
        row.Conditionnement = p.Unite;
        row.PrixUnitaireHt = p.PrixVenteHT;
        row.TauxTva = p.TauxTVA;
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        var includeTvaInTotals = ShowTotalTtc;
        var ht = Lignes.Sum(l => l.MontantHt);
        var tva = includeTvaInTotals
            ? Lignes.Sum(l => l.MontantHt * (l.TauxTva / 100m))
            : 0m;
        var ttc = ht + tva;
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        UpdateTotalLabels(ht, tva, ttc);
    }

    private void UpdateTotalLabels(decimal ht, decimal tva, decimal ttc)
    {
        TotalHtLabel = _locale.Tf("Doc_FmtHt", ht, Devise).TrimEnd();
        TotalTvaLabel = _locale.Tf("Doc_FmtTva", tva, Devise).TrimEnd();
        TotalTtcLabel = _locale.Tf("Doc_FmtTtc", ttc, Devise).TrimEnd();
    }

    partial void OnDeviseChanged(string value)
    {
        RefreshTotals();
        if (ClientId > 0)
            _ = RefreshClientSoldeAsync(ClientId);
    }

    [RelayCommand]
    private void RemoveSelectedLine()
    {
        if (SelectedLine == null) return;
        RemoveLine(SelectedLine);
        SelectedLine = null;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), _locale.T("BL_ErrNoEdit"), cancellationToken);
            return;
        }

        if (ClientId == 0 || !Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), _locale.T("BL_ErrClientLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(TotalTtc))
        {
            await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        var creditWarning = await _creditLimit.GetBlDocumentCreditWarningAsync(ClientId, TotalTtc, cancellationToken);
        if (creditWarning is not null
            && !await _dialog.ConfirmAsync(_locale.T("CreditLimit_WarnTitle"), creditWarning, cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            BonLivraison entity;
            if (BlId == null)
            {
                var num = await _numbers.NextBLAsync(cancellationToken);
                entity = new BonLivraison
                {
                    Numero = num,
                    ClientId = ClientId,
                    DevisId = DevisId,
                    Date = Date.DateTime,
                    Note = BonCommandeReferenceStorage.Format(BonCommandeReference, Note),
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonLivraisonLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        QuantiteCommandee = l.QuantiteLivree,
                        QuantiteLivree = l.QuantiteLivree,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva
                    });
                }

                db.BonsLivraison.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                BlId = entity.Id;
            }
            else
            {
                entity = await db.BonsLivraison.Include(b => b.Lignes).FirstAsync(b => b.Id == BlId, cancellationToken);
                entity.ClientId = ClientId;
                entity.DevisId = DevisId;
                entity.Date = Date.DateTime;
                entity.Note = BonCommandeReferenceStorage.Format(BonCommandeReference, Note);
                entity.BonCommandeClientId = null;
                db.BonLivraisonLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonLivraisonLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        QuantiteCommandee = l.QuantiteLivree,
                        QuantiteLivree = l.QuantiteLivree,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            try
            {
                await _workflow.ValiderAsync(entity.Id, _session.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), ex.Message, cancellationToken);
                await LoadAsync(BlId, cancellationToken);
                return;
            }

            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("BL_DlgShort"), _locale.T("BL_Saved"), cancellationToken);
            await LoadAsync(BlId, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShowBccPickerAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit || ClientId == 0) return;

        var existingNumeros = ParseBonCommandeNumeros(BonCommandeReference);
        var available = await _bccLinkService.GetAvailableBccsForClientAsync(ClientId, null, cancellationToken);
        var filtered = available.Where(b => !existingNumeros.Contains(b.Numero, StringComparer.OrdinalIgnoreCase)).ToList();
        if (filtered.Count == 0)
        {
            await _dialog.ShowInfoAsync(_locale.T("BL_DlgShort"), _locale.T("Fact_NoAvailableBccs"), cancellationToken);
            return;
        }

        var pickerItems = filtered.Select(b =>
        {
            var (_, _, ttc) = DocumentTotalsHelper.BonCommandeClientTotals(b.Lignes ?? []);
            var montantLabel = _locale.Tf("Doc_FmtTtc", ttc, Devise).TrimEnd();
            return (b.Id, b.Numero, b.Date, montantLabel);
        }).ToList();

        var selectedIds = await _dialog.ShowBlPickerAsync(_locale.T("Fact_AddBcc"), pickerItems, cancellationToken);
        if (selectedIds == null || selectedIds.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var selectedNumeros = await db.BonsCommandeClient.AsNoTracking()
            .Where(b => selectedIds.Contains(b.Id))
            .OrderBy(b => b.Date).ThenBy(b => b.Numero)
            .Select(b => b.Numero)
            .ToListAsync(cancellationToken);

        foreach (var numero in selectedNumeros)
            AppendBonCommandeNumero(numero);
    }

    private static HashSet<string> ParseBonCommandeNumeros(string reference) =>
        reference.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void AppendBonCommandeNumero(string numero)
    {
        if (string.IsNullOrWhiteSpace(numero)) return;
        var existing = ParseBonCommandeNumeros(BonCommandeReference);
        if (existing.Contains(numero)) return;
        BonCommandeReference = string.IsNullOrWhiteSpace(BonCommandeReference)
            ? numero
            : $"{BonCommandeReference}, {numero}";
        UpdateBccLabel();
    }

    private int? _linkedFactureId;
    private int? _linkedBccId;

    [RelayCommand]
    private void OpenLinkedFacture()
    {
        if (_linkedFactureId is not int factureId) return;
        var vm = _sp.GetRequiredService<FactureEditViewModel>();
        vm.Load(factureId);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private async Task OpenLinkedBccAsync(CancellationToken cancellationToken)
    {
        var bccId = _linkedBccId;
        if (bccId == null && !string.IsNullOrWhiteSpace(BonCommandeReference))
        {
            var numero = ParseBonCommandeNumeros(BonCommandeReference).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(numero))
            {
                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                bccId = await db.BonsCommandeClient.AsNoTracking()
                    .Where(b => b.Numero == numero)
                    .Select(b => (int?)b.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (bccId is not int id) return;
        var vm = _sp.GetRequiredService<BCVEditViewModel>();
        vm.Load(id);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private async Task ToFactureAsync(CancellationToken cancellationToken)
    {
        if (BlId == null) return;
        var factNum = await _blLinkService.GetInvoicingStatusAsync(BlId.Value, cancellationToken);
        if (factNum != null)
        {
            await _dialog.ShowErrorAsync(_locale.T("BL_DlgShort"), _locale.Tf("BL_ErrAlreadyInvoiced", factNum), cancellationToken);
            return;
        }

        var creditWarning = await _creditLimit.GetBlDocumentCreditWarningAsync(ClientId, TotalTtc, cancellationToken);
        if (creditWarning is not null
            && !await _dialog.ConfirmAsync(_locale.T("CreditLimit_WarnTitle"), creditWarning, cancellationToken))
            return;

        var vm = _sp.GetRequiredService<FactureEditViewModel>();
        vm.LoadFromBL(BlId.Value);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<BLListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (BlId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBlPdfBytesAsync(cancellationToken);
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
        if (BlId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBlPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildBlPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (BlId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var b = await db.BonsLivraison.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        var client = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == b.ClientId, cancellationToken);
        return await _pdf.BuildBonLivraisonPdfAsync(b, DocumentPartyPdfInfo.FromTiers(client), cancellationToken);
    }
}
