using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Modules.Livraison.ViewModels;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Facturation.ViewModels;

public sealed record LinkedBlRow(int Id, string Numero, DateTime Date);

public partial class FactureEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IAppSettingsService _settings;
    private readonly IFactureWorkflowService _factureWorkflow;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly IFactureBlLinkService _blLinkService;
    private readonly IFactureBccLinkService _bccLinkService;
    private readonly IClientCreditLimitService _creditLimit;
    private readonly ClientSoldeDisplay _clientSolde;

    public FactureEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IAppSettingsService settings,
        IFactureWorkflowService factureWorkflow,
        IFactureBlLinkService blLinkService,
        IFactureBccLinkService bccLinkService,
        IClientCreditLimitService creditLimit,
        IClientAccountStatementService clientLedger,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ICurrentUserSession session,
        ILocaleService locale,
        IUiPreferencesService uiPreferences,
        IPdfService pdf,
        IPdfPrintService pdfPrint)
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
        _blLinkService = blLinkService;
        _bccLinkService = bccLinkService;
        _creditLimit = creditLimit;
        _clientSolde = new ClientSoldeDisplay(clientLedger, locale);
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _locale.CultureApplied += (_, _) =>
        {
            RefreshFactureUi();
            UpdateFactureTotalLines();
            if (ClientId > 0)
                _ = ClientSolde.RefreshAsync(ClientId, Devise);
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("facture", LineGridColumns);
        Title = _locale.T("Fact_Title");
        RefreshFactureUi();
        ClientLookup.BindSelection(() => ClientId, c => SelectedClient = c);
    }

    public ClientCategoryFilter ClientLookup { get; } = new();
    public ClientSoldeDisplay ClientSolde => _clientSolde;
    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Clients => ClientLookup.Clients;
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<FactureLineRow> Lignes { get; } = [];
    public ObservableCollection<FacturePaiementRowViewModel> Paiements { get; } = [];
    public ObservableCollection<LinkedBlRow> LinkedBls { get; } = [];

    [ObservableProperty] private int? _factureId;
    [ObservableProperty] private int? _devisId;
    [ObservableProperty] private int _clientId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedClient;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private DateTimeOffset _dateEcheance = new(DateTime.Today.AddDays(30));
    [ObservableProperty] private bool _estPayee;
    [ObservableProperty] private decimal _remiseGlobale;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _bonCommandeReference = string.Empty;
    [ObservableProperty] private decimal _totalHt;
    [ObservableProperty] private decimal _totalTva;
    [ObservableProperty] private decimal _totalTtc;
    [ObservableProperty] private decimal _montantPaye;
    [ObservableProperty] private bool _canEditDraft;

    [ObservableProperty] private decimal _paiementMontant;
    [ObservableProperty] private DateTimeOffset _paiementDate = new(DateTime.Today);
    [ObservableProperty] private ModePaiement _paiementMode = ModePaiement.Especes;
    [ObservableProperty] private string _paiementReference = string.Empty;
    [ObservableProperty] private FactureLineRow? _selectedLine;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _menuDeleteFacture = string.Empty;
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
    [ObservableProperty] private string _lblLinkedBls = string.Empty;
    [ObservableProperty] private string _btnAddBl = string.Empty;
    [ObservableProperty] private string _lblLinkedBccs = string.Empty;
    [ObservableProperty] private string _btnAddBcc = string.Empty;
    [ObservableProperty] private string _wmBonCommandeReference = string.Empty;

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
        _uiPreferences.SaveDocumentLineColumns("facture", LineGridColumns);
    }

    private void RefreshFactureUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        MenuDeleteFacture = _locale.T("Fact_MenuDelete");
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
        LblFactPayee = _locale.T("Fact_LblPayee");
        LblPaid = _locale.T("Fact_Paid");
        LblUnpaid = _locale.T("Fact_Unpaid");
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
        LblLinkedBls = _locale.T("Fact_LinkedBls");
        BtnAddBl = _locale.T("Fact_AddBl");
        LblLinkedBccs = _locale.T("Fact_LinkedBccs");
        BtnAddBcc = _locale.T("Fact_AddBcc");
        WmBonCommandeReference = _locale.T("Fact_WmBonCommandeReference");
    }

    private void UpdateFactureTotalLines()
    {
        TotalHtLabel = _locale.Tf("Doc_FmtHt", TotalHt, Devise).TrimEnd();
        TotalTvaLabel = _locale.Tf("Doc_FmtTva", TotalTva, Devise).TrimEnd();
        TotalTtcLabel = _locale.Tf("Doc_FmtTtc", TotalTtc, Devise).TrimEnd();
        MontantPayeLine = _locale.Tf("Doc_FmtPaye", MontantPaye);
    }

    partial void OnDeviseChanged(string value)
    {
        UpdateFactureTotalLines();
        if (ClientId > 0)
            _ = ClientSolde.RefreshAsync(ClientId, value);
    }

    public Array ModesPaiement => Enum.GetValues(typeof(ModePaiement));

    private bool CanExecuteAddPaiement() => FactureId.HasValue;

    partial void OnMontantPayeChanged(decimal value) => UpdateFactureTotalLines();

    partial void OnFactureIdChanged(int? value)
    {
        AddPaiementCommand.NotifyCanExecuteChanged();
        RemoveFactureCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveFacture() => FactureId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveFacture))]
    private async Task RemoveFactureAsync(CancellationToken cancellationToken)
    {
        if (FactureId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("Fact_Title"), _locale.Tf("Fact_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            if (await db.Avoirs.AsNoTracking().AnyAsync(a => a.FactureId == id, cancellationToken))
            {
                await _dialog.ShowErrorAsync(_locale.T("Fact_Title"), _locale.T("Fact_ErrDeleteReferenced"), cancellationToken);
                return;
            }

            var entity = await db.Factures.Include(f => f.Lignes).Include(f => f.Paiements).FirstAsync(f => f.Id == id, cancellationToken);
            db.Factures.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);

            await _dialog.ShowInfoAsync(_locale.T("Fact_Title"), _locale.T("Fact_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Fact_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReloadPaiementsList(IEnumerable<Paiement> paiements)
    {
        Paiements.Clear();
        foreach (var p in paiements.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
            Paiements.Add(new FacturePaiementRowViewModel(this, p));
    }

    public async Task CommitPaiementRowAsync(FacturePaiementRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (FactureId == null || row.Montant <= 0)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), _locale.T("Pay_ErrAmount"), cancellationToken);
            return;
        }

        try
        {
            IsBusy = true;
            await _factureWorkflow.UpdatePaiementAsync(
                FactureId.Value,
                row.Id,
                row.Montant,
                row.Date.DateTime,
                row.Mode,
                row.Reference,
                cancellationToken);
            await LoadAsync(FactureId, cancellationToken);
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

    public async Task DeletePaiementRowAsync(FacturePaiementRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (FactureId == null) return;
        if (!await _dialog.ConfirmAsync(_locale.T("Pay_Title"), _locale.T("Pay_ConfirmDelete"), cancellationToken))
            return;

        try
        {
            IsBusy = true;
            await _factureWorkflow.DeletePaiementAsync(FactureId.Value, row.Id, cancellationToken);
            await LoadAsync(FactureId, cancellationToken);
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
        if (e.PropertyName == nameof(FactureLineRow.ProduitId) && sender is FactureLineRow changed && changed.ProduitId != 0)
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
            var row = new FactureLineRow();
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
        var lines = Lignes.Select(l => new FactureLigne
        {
            Quantite = l.Quantite,
            PrixUnitaireHT = l.PrixUnitaireHt,
            Remise = l.Remise,
            TauxTVA = includeTvaInTotals ? l.TauxTva : 0
        });
        var (ht, tva, ttc) = DocumentTotalsHelper.FactureTotals(lines, RemiseGlobale);
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        UpdateFactureTotalLines();
        RefreshSuggestedPaiementMontant();
    }

    private void RefreshSuggestedPaiementMontant()
    {
        if (!FactureId.HasValue) return;
        var fullTtc = ComputeFullPaymentTtc();
        PaiementMontant = Math.Round(Math.Max(0, fullTtc - MontantPaye), 2);
    }

    private decimal ComputeFullPaymentTtc() =>
        DocumentTotalsHelper.FactureTtc(
            Lignes.Select(l => new FactureLigne
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
        FactureId = id;
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        DevisId = null;
        LinkedBls.Clear();
        BonCommandeReference = string.Empty;
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await LoadLookupsAsync(db, cancellationToken);

        if (id == null)
        {
            Numero = _locale.T("Fact_NewNumPlaceholder");
            ClientId = Clients.FirstOrDefault()?.Id ?? 0;
            Date = new DateTimeOffset(DateTime.Today);
            DateEcheance = Date.AddDays(30);
            EstPayee = false;
            CanEditDraft = true;
            Title = _locale.T("Fact_NewTitle");
            MontantPaye = 0;
            Paiements.Clear();
            RefreshTotals();
            return;
        }

        var f = await db.Factures.Include(x => x.Lignes).Include(x => x.Paiements).FirstAsync(x => x.Id == id, cancellationToken);
        var linkedBls = await db.BonsLivraison.AsNoTracking()
            .Where(b => b.FactureId == id)
            .OrderBy(b => b.Date).ThenBy(b => b.Numero)
            .ToListAsync(cancellationToken);
        foreach (var bl in linkedBls)
            LinkedBls.Add(new LinkedBlRow(bl.Id, bl.Numero, bl.Date));
        BonCommandeReference = f.BonCommandeReference;
        if (string.IsNullOrWhiteSpace(BonCommandeReference))
        {
            var linkedBccNums = await db.BonsCommandeClient.AsNoTracking()
                .Where(b => b.FactureId == id)
                .OrderBy(b => b.Date).ThenBy(b => b.Numero)
                .Select(b => b.Numero)
                .ToListAsync(cancellationToken);
            if (linkedBccNums.Count > 0)
                BonCommandeReference = string.Join(", ", linkedBccNums);
        }
        DevisId = f.DevisId;
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
            var row = new FactureLineRow
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
        DocumentTotalsHelper.SyncFactureTotalTtc(f);
        if (db.Entry(f).Property(x => x.TotalTtc).IsModified)
            await db.SaveChangesAsync(cancellationToken);
        CanEditDraft = true;
        Title = _locale.Tf("Fact_TitleNum", Numero);
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

    public void LoadFromBL(int blId) => _ = LoadFromBlsAsync([blId], CancellationToken.None);

    [RelayCommand]
    private async Task ShowBccPickerAsync(CancellationToken cancellationToken)
    {
        if (ClientId == 0) return;
        var existingNumeros = ParseBonCommandeNumeros(BonCommandeReference);
        var available = await _bccLinkService.GetAvailableBccsForClientAsync(ClientId, FactureId, cancellationToken);
        var filtered = available.Where(b => !existingNumeros.Contains(b.Numero, StringComparer.OrdinalIgnoreCase)).ToList();
        if (filtered.Count == 0)
        {
            await _dialog.ShowInfoAsync(_locale.T("Fact_Title"), _locale.T("Fact_NoAvailableBccs"), cancellationToken);
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
    }

    [RelayCommand]
    private void RemoveBlGroup(LinkedBlRow bl)
    {
        for (var i = Lignes.Count - 1; i >= 0; i--)
        {
            if (Lignes[i].BonLivraisonId == bl.Id)
            {
                Lignes[i].PropertyChanged -= LineChanged;
                Lignes.RemoveAt(i);
            }
        }
        LinkedBls.Remove(bl);
        RefreshTotals();
    }

    [RelayCommand]
    private async Task ShowBlPickerAsync(CancellationToken cancellationToken)
    {
        if (ClientId == 0) return;
        var excludeIds = LinkedBls.Select(b => b.Id).Concat(Lignes.Where(l => l.BonLivraisonId.HasValue).Select(l => l.BonLivraisonId!.Value)).Distinct().ToList();
        var available = await _blLinkService.GetAvailableBlsForClientAsync(ClientId, FactureId, cancellationToken);
        var filtered = available.Where(b => !excludeIds.Contains(b.Id)).ToList();
        if (filtered.Count == 0)
        {
            await _dialog.ShowInfoAsync(_locale.T("Fact_Title"), _locale.T("Fact_NoAvailableBls"), cancellationToken);
            return;
        }

        var pickerItems = filtered.Select(b =>
        {
            var (_, _, ttc) = DocumentTotalsHelper.BonLivraisonTotals(b.Lignes ?? []);
            var montantLabel = _locale.Tf("Doc_FmtTtc", ttc, Devise).TrimEnd();
            return (b.Id, b.Numero, b.Date, montantLabel);
        }).ToList();
        var selectedIds = await _dialog.ShowBlPickerAsync(_locale.T("Fact_AddBl"), pickerItems, cancellationToken);
        if (selectedIds == null || selectedIds.Count == 0) return;

        foreach (var blId in selectedIds)
            await AddBlLinesAsync(blId, cancellationToken);
    }

    [RelayCommand]
    private async Task AddBlLinesAsync(int blId, CancellationToken cancellationToken)
    {
        var lines = await _blLinkService.LoadBlLinesAsync(blId, cancellationToken);
        foreach (var l in lines)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            l.Reference = prod?.Reference ?? string.Empty;
            l.PropertyChanged += LineChanged;
            Lignes.Add(l);
        }
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var b = await db.BonsLivraison.AsNoTracking().FirstAsync(b => b.Id == blId, cancellationToken);
        LinkedBls.Add(new LinkedBlRow(b.Id, b.Numero, b.Date));
        RefreshTotals();
    }

    public async Task LoadFromBlsAsync(IReadOnlyList<int> blIds, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        LinkedBls.Clear();
        BonCommandeReference = string.Empty;
        Lignes.Clear();
        DevisId = null;
        FactureId = null;
        Date = new DateTimeOffset(DateTime.Today);
        DateEcheance = Date.AddDays(30);
        EstPayee = false;
        Numero = _locale.T("Fact_NewNumPlaceholder");
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await LoadLookupsAsync(db, cancellationToken);

        var firstBlId = blIds[0];
        var firstBl = await db.BonsLivraison.AsNoTracking().FirstAsync(b => b.Id == firstBlId, cancellationToken);
        ClientId = firstBl.ClientId;

        foreach (var blId in blIds)
        {
            var bl = await db.BonsLivraison.AsNoTracking().FirstAsync(b => b.Id == blId, cancellationToken);
            LinkedBls.Add(new LinkedBlRow(bl.Id, bl.Numero, bl.Date));
            var lines = await _blLinkService.LoadBlLinesAsync(blId, cancellationToken);
            foreach (var l in lines)
            {
                var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
                l.Reference = prod?.Reference ?? string.Empty;
                l.PropertyChanged += LineChanged;
                Lignes.Add(l);
            }
        }

        HookLines();
        CanEditDraft = true;
        MontantPaye = 0;
        Paiements.Clear();
        Title = blIds.Count > 1 ? _locale.T("Fact_FromMultiBl") : _locale.T("Fact_FromBl");
        RefreshTotals();
    }

    public async Task LoadFromDevisAsync(int devisId, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var d = await db.Devis.Include(x => x.Lignes).FirstAsync(x => x.Id == devisId, cancellationToken);
        DevisId = d.Id;
        LinkedBls.Clear();
        BonCommandeReference = string.Empty;
        FactureId = null;
        ClientId = d.ClientId;
        Date = new DateTimeOffset(DateTime.Today);
        DateEcheance = Date.AddDays(30);
        EstPayee = false;
        Numero = _locale.T("Fact_NewNumPlaceholder");
        RemiseGlobale = d.RemiseGlobale;
        await LoadLookupsAsync(db, cancellationToken);
        Lignes.Clear();
        foreach (var l in d.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new FactureLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = l.Conditionnement,
                Quantite = l.Quantite,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            });
        }

        HookLines();
        CanEditDraft = true;
        MontantPaye = 0;
        Paiements.Clear();
        Title = _locale.T("Fact_FromDevis");
        RefreshTotals();
    }

    public void LoadFromDevis(int devisId) => _ = LoadFromDevisAsync(devisId, CancellationToken.None);

    [RelayCommand]
    private void AddLine()
    {
        var p = Produits.FirstOrDefault();
        var row = new FactureLineRow
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
    private void RemoveLine(FactureLineRow? row)
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
            await _dialog.ShowErrorAsync(_locale.T("Fact_Title"), _locale.T("Fact_ErrClientLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(ComputeFullPaymentTtc()))
        {
            await _dialog.ShowErrorAsync(_locale.T("Fact_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        var proposedTtc = ComputeFullPaymentTtc();
        decimal existingTtc = 0m;
        if (FactureId != null)
        {
            await using var checkDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
            existingTtc = await checkDb.Factures.AsNoTracking()
                .Where(f => f.Id == FactureId)
                .Select(f => f.TotalTtc)
                .FirstOrDefaultAsync(cancellationToken);
            var paid = await checkDb.Paiements.AsNoTracking()
                .Where(p => p.FactureId == FactureId)
                .SumAsync(p => p.Montant, cancellationToken);
            if (!await ValidatePaymentsAgainstTtcAsync(proposedTtc, paid, cancellationToken))
                return;
        }

        var creditBlock = await _creditLimit.GetBlockMessageIfFactureWouldExceedAsync(
            ClientId, proposedTtc, existingTtc, cancellationToken);
        if (creditBlock is not null)
        {
            await _dialog.ShowErrorAsync(_locale.T("Fact_Title"), creditBlock, cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            Facture entity;
            if (FactureId == null)
            {
                var num = await _numbers.NextFactureAsync(cancellationToken);
                entity = new Facture
                {
                    Numero = num,
                    ClientId = ClientId,
                    DevisId = DevisId,
                    Date = Date.DateTime,
                    DateEcheance = DateEcheance.DateTime,
                    EstPayee = EstPayee,
                    RemiseGlobale = RemiseGlobale,
                    Note = Note,
                    BonCommandeReference = BonCommandeReference.Trim(),
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new FactureLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        Quantite = l.Quantite,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva,
                        BonLivraisonId = l.BonLivraisonId
                    });
                }

                DocumentTotalsHelper.SyncFactureTotalTtc(entity);
                db.Factures.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                FactureId = entity.Id;

                foreach (var bl in LinkedBls)
                {
                    var blEntity = await db.BonsLivraison.FindAsync(bl.Id);
                    if (blEntity != null)
                        blEntity.FactureId = entity.Id;
                }

                await _bccLinkService.AssignBccsToFactureAsync(db, entity.Id, [], cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                entity = await db.Factures.Include(f => f.Lignes).FirstAsync(f => f.Id == FactureId, cancellationToken);

                entity.ClientId = ClientId;
                entity.DevisId = DevisId;
                entity.Date = Date.DateTime;
                entity.DateEcheance = DateEcheance.DateTime;
                entity.EstPayee = EstPayee;
                entity.RemiseGlobale = RemiseGlobale;
                entity.Note = Note;
                entity.BonCommandeReference = BonCommandeReference.Trim();
                db.FactureLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new FactureLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        Quantite = l.Quantite,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva,
                        BonLivraisonId = l.BonLivraisonId
                    });
                }

                DocumentTotalsHelper.SyncFactureTotalTtc(entity);
                await db.SaveChangesAsync(cancellationToken);
            }

            var linkedBlIds = LinkedBls.Select(b => b.Id).ToHashSet();
            var existingBls = await db.BonsLivraison.Where(b => b.FactureId == FactureId).ToListAsync(cancellationToken);
            foreach (var bl in existingBls)
            {
                if (!linkedBlIds.Contains(bl.Id))
                    bl.FactureId = null;
            }

            foreach (var bl in LinkedBls)
            {
                var blEntity = await db.BonsLivraison.FindAsync(bl.Id);
                if (blEntity != null)
                    blEntity.FactureId = FactureId;
            }

            await _bccLinkService.AssignBccsToFactureAsync(db, FactureId!.Value, [], cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("Fact_Title"), _locale.T("Fact_Saved"), cancellationToken);
            await LoadAsync(FactureId, cancellationToken);
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

        if (!FactureId.HasValue)
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
            await _factureWorkflow.AddPaiementAsync(FactureId.Value, new Paiement
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
            await LoadAsync(FactureId, cancellationToken);
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
    private void OpenLinkedBl(LinkedBlRow? bl)
    {
        if (bl == null) return;
        var vm = _sp.GetRequiredService<BLEditViewModel>();
        vm.Load(bl.Id);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<FactureListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (FactureId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildFacturePdfBytesAsync(cancellationToken);
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
        if (FactureId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildFacturePdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildFacturePdfBytesAsync(CancellationToken cancellationToken)
    {
        if (FactureId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var f = await db.Factures.Include(x => x.Lignes).Include(x => x.Paiements).FirstAsync(x => x.Id == id, cancellationToken);
        f.BonCommandeReference = BonCommandeReference.Trim();
        var client = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == f.ClientId, cancellationToken);
        return await _pdf.BuildFacturePdfAsync(f, DocumentPartyPdfInfo.FromTiers(client), cancellationToken);
    }
}
