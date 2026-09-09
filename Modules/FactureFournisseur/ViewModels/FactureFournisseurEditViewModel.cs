using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.FactureFournisseur.Models;
using GestionCommerciale.Modules.FactureFournisseur.Services;
using GestionCommerciale.Modules.Reception.ViewModels;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.FactureFournisseur.ViewModels;

public sealed record LinkedBrRow(int Id, string Numero, DateTime Date);

public partial class FactureFournisseurEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IAppSettingsService _settings;
    private readonly IFactureFournisseurWorkflowService _factureFournisseurWorkflow;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly IFactureFournisseurBrLinkService _brLinkService;

    public FactureFournisseurEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IAppSettingsService settings,
        IFactureFournisseurWorkflowService factureFournisseurWorkflow,
        IFactureFournisseurBrLinkService brLinkService,
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
        _factureFournisseurWorkflow = factureFournisseurWorkflow;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _session = session;
        _locale = locale;
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _uiPreferences = uiPreferences;
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _brLinkService = brLinkService;
        _locale.CultureApplied += (_, _) =>
        {
            RefreshFactureFournisseurUi();
            UpdateFactureFournisseurTotalLines();
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("facture_fournisseur", LineGridColumns);
        Title = _locale.T("Faf_Title");
        RefreshFactureFournisseurUi();
    }

    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Fournisseurs { get; } = [];
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<FactureFournisseurLineRow> Lignes { get; } = [];
    public ObservableCollection<FactureFournisseurPaiementRowViewModel> Paiements { get; } = [];
    public ObservableCollection<LinkedBrRow> LinkedBrs { get; } = [];

    [ObservableProperty] private int? _factureFournisseurId;
    [ObservableProperty] private int _fournisseurId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedFournisseur;
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
    [ObservableProperty] private FactureFournisseurLineRow? _selectedLine;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _menuDeleteFactureFournisseur = string.Empty;
    [ObservableProperty] private string _lblFactPayee = string.Empty;
    [ObservableProperty] private string _lblPaid = string.Empty;
    [ObservableProperty] private string _lblUnpaid = string.Empty;
    [ObservableProperty] private string _lblFournisseur = string.Empty;
    [ObservableProperty] private string _wmFournisseurSearch = string.Empty;
    [ObservableProperty] private string _lblDateFacture = string.Empty;
    [ObservableProperty] private string _lblDateEcheance = string.Empty;
    [ObservableProperty] private string _btnRemoveLine = string.Empty;
    [ObservableProperty] private string _lblCatalogHintFactureFournisseur = string.Empty;
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
    [ObservableProperty] private string _lblLinkedBrs = string.Empty;
    [ObservableProperty] private string _btnAddBr = string.Empty;

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
        _uiPreferences.SaveDocumentLineColumns("facture_fournisseur", LineGridColumns);
    }

    private void RefreshFactureFournisseurUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        MenuDeleteFactureFournisseur = _locale.T("Faf_MenuDelete");
        LblFournisseur = _locale.T("Lbl_Supplier");
        WmFournisseurSearch = _locale.T("Wm_SearchSupplier");
        LblDateFacture = _locale.T("Lbl_DateFacture");
        LblDateEcheance = _locale.T("Lbl_DateEcheance");
        BtnRemoveLine = _locale.T("Btn_RemoveLine");
        LblCatalogHintFactureFournisseur = _locale.T("Faf_CatalogHint");
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
        LblFactPayee = _locale.T("Faf_LblPayee");
        LblPaid = _locale.T("Faf_Paid");
        LblUnpaid = _locale.T("Faf_Unpaid");
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
        LblLinkedBrs = _locale.T("Faf_LinkedBrs");
        BtnAddBr = _locale.T("Faf_AddBr");
    }

    private void UpdateFactureFournisseurTotalLines()
    {
        TotalHtLabel = _locale.Tf("Doc_FmtHt", TotalHt, Devise).TrimEnd();
        TotalTvaLabel = _locale.Tf("Doc_FmtTva", TotalTva, Devise).TrimEnd();
        TotalTtcLabel = _locale.Tf("Doc_FmtTtc", TotalTtc, Devise).TrimEnd();
        MontantPayeLine = _locale.Tf("Doc_FmtPaye", MontantPaye);
    }

    partial void OnDeviseChanged(string value) => UpdateFactureFournisseurTotalLines();

    public Array ModesPaiement => Enum.GetValues(typeof(ModePaiement));

    private bool CanExecuteAddPaiement() => FactureFournisseurId.HasValue;

    partial void OnMontantPayeChanged(decimal value) => UpdateFactureFournisseurTotalLines();

    partial void OnFactureFournisseurIdChanged(int? value)
    {
        AddPaiementCommand.NotifyCanExecuteChanged();
        RemoveFactureFournisseurCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveFactureFournisseur() => FactureFournisseurId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveFactureFournisseur))]
    private async Task RemoveFactureFournisseurAsync(CancellationToken cancellationToken)
    {
        if (FactureFournisseurId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("Faf_Title"), _locale.Tf("Faf_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.FacturesFournisseurs.Include(f => f.Lignes).Include(f => f.Paiements).FirstAsync(f => f.Id == id, cancellationToken);
            db.FacturesFournisseurs.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);

            await _dialog.ShowInfoAsync(_locale.T("Faf_Title"), _locale.T("Faf_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Faf_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReloadPaiementsList(IEnumerable<PaiementFournisseur> paiements)
    {
        Paiements.Clear();
        foreach (var p in paiements.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
            Paiements.Add(new FactureFournisseurPaiementRowViewModel(this, p));
    }

    public async Task CommitPaiementRowAsync(FactureFournisseurPaiementRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (FactureFournisseurId == null || row.Montant <= 0)
        {
            await _dialog.ShowErrorAsync(_locale.T("Pay_Title"), _locale.T("Pay_ErrAmount"), cancellationToken);
            return;
        }

        try
        {
            IsBusy = true;
            await _factureFournisseurWorkflow.UpdatePaiementAsync(
                FactureFournisseurId.Value,
                row.Id,
                row.Montant,
                row.Date.DateTime,
                row.Mode,
                row.Reference,
                cancellationToken);
            await LoadAsync(FactureFournisseurId, cancellationToken);
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

    public async Task DeletePaiementRowAsync(FactureFournisseurPaiementRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (FactureFournisseurId == null) return;
        if (!await _dialog.ConfirmAsync(_locale.T("Pay_Title"), _locale.T("Pay_ConfirmDelete"), cancellationToken))
            return;

        try
        {
            IsBusy = true;
            await _factureFournisseurWorkflow.DeletePaiementAsync(FactureFournisseurId.Value, row.Id, cancellationToken);
            await LoadAsync(FactureFournisseurId, cancellationToken);
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
        if (e.PropertyName == nameof(FactureFournisseurLineRow.ProduitId) && sender is FactureFournisseurLineRow changed && changed.ProduitId != 0)
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
            var row = new FactureFournisseurLineRow();
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
        var lines = Lignes.Select(l => new FactureFournisseurLigne
        {
            Quantite = l.Quantite,
            PrixUnitaireHT = l.PrixUnitaireHt,
            Remise = l.Remise,
            TauxTVA = includeTvaInTotals ? l.TauxTva : 0
        });
        var (ht, tva, ttc) = DocumentTotalsHelper.FactureFournisseurTotals(lines, RemiseGlobale);
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        UpdateFactureFournisseurTotalLines();
        RefreshSuggestedPaiementMontant();
    }

    private void RefreshSuggestedPaiementMontant()
    {
        if (!FactureFournisseurId.HasValue) return;
        var fullTtc = ComputeFullPaymentTtc();
        PaiementMontant = Math.Round(Math.Max(0, fullTtc - MontantPaye), 2);
    }

    private decimal ComputeFullPaymentTtc() =>
        DocumentTotalsHelper.FactureFournisseurTtc(
            Lignes.Select(l => new FactureFournisseurLigne
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

    partial void OnSelectedFournisseurChanged(GestionCommerciale.Modules.Tiers.Models.Tiers? value)
    {
        RefreshWhatsAppContact();
        var id = value?.Id ?? 0;
        if (FournisseurId == id) return;
        FournisseurId = id;
    }

    partial void OnFournisseurIdChanged(int value)
    {
        if (SelectedFournisseur?.Id != value)
            SelectedFournisseur = Fournisseurs.FirstOrDefault(c => c.Id == value);
        RefreshWhatsAppContact();
    }

    private void RefreshWhatsAppContact()
    {
        var prefill = string.IsNullOrWhiteSpace(Numero) || Numero.Contains('(', StringComparison.Ordinal)
            ? null
            : Numero;
        WhatsApp.SetPhone(SelectedFournisseur?.Telephone, prefill);
    }

    public async Task LoadAsync(int? id, CancellationToken cancellationToken = default)
    {
        FactureFournisseurId = id;
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        LinkedBrs.Clear();
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await LoadLookupsAsync(db, cancellationToken);

        if (id == null)
        {
            Numero = _locale.T("Faf_NewNumPlaceholder");
            FournisseurId = Fournisseurs.FirstOrDefault()?.Id ?? 0;
            Date = new DateTimeOffset(DateTime.Today);
            DateEcheance = Date.AddDays(30);
            EstPayee = false;
            CanEditDraft = true;
            Title = _locale.T("Faf_NewTitle");
            MontantPaye = 0;
            Paiements.Clear();
            RefreshTotals();
            return;
        }

        var f = await db.FacturesFournisseurs.Include(x => x.Lignes).Include(x => x.Paiements).FirstAsync(x => x.Id == id, cancellationToken);
        var linkedBrs = await db.BonsReception.AsNoTracking()
            .Where(b => b.FactureFournisseurId == id)
            .OrderBy(b => b.Date).ThenBy(b => b.Numero)
            .ToListAsync(cancellationToken);
        foreach (var br in linkedBrs)
            LinkedBrs.Add(new LinkedBrRow(br.Id, br.Numero, br.Date));
        Numero = f.Numero;
        FournisseurId = f.FournisseurId;
        Date = new DateTimeOffset(f.Date);
        DateEcheance = new DateTimeOffset(f.DateEcheance);
        EstPayee = f.EstPayee;
        RemiseGlobale = f.RemiseGlobale;
        Note = f.Note;
        foreach (var l in f.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            var row = new FactureFournisseurLineRow
            {
                BonReceptionId = l.BonReceptionId,
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
        DocumentTotalsHelper.SyncFactureFournisseurTotalTtc(f);
        if (db.Entry(f).Property(x => x.TotalTtc).IsModified)
            await db.SaveChangesAsync(cancellationToken);
        CanEditDraft = true;
        Title = _locale.Tf("Faf_TitleNum", Numero);
        RefreshTotals();
    }

    private async Task LoadLookupsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var fournisseurs = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Fournisseur || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        Fournisseurs.Clear();
        foreach (var c in fournisseurs) Fournisseurs.Add(c);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);
    }

    public void Load(int? id) => _ = LoadAsync(id, CancellationToken.None);

    public void LoadFromBR(int brId) => _ = LoadFromBrsAsync([brId], CancellationToken.None);

    [RelayCommand]
    private void RemoveBrGroup(LinkedBrRow br)
    {
        for (var i = Lignes.Count - 1; i >= 0; i--)
        {
            if (Lignes[i].BonReceptionId == br.Id)
            {
                Lignes[i].PropertyChanged -= LineChanged;
                Lignes.RemoveAt(i);
            }
        }
        LinkedBrs.Remove(br);
        RefreshTotals();
    }

    [RelayCommand]
    private async Task ShowBrPickerAsync(CancellationToken cancellationToken)
    {
        if (FournisseurId == 0) return;
        var excludeIds = LinkedBrs.Select(b => b.Id).Concat(Lignes.Where(l => l.BonReceptionId.HasValue).Select(l => l.BonReceptionId!.Value)).Distinct().ToList();
        var available = await _brLinkService.GetAvailableBrsForFournisseurAsync(FournisseurId, FactureFournisseurId, cancellationToken);
        var filtered = available.Where(b => !excludeIds.Contains(b.Id)).ToList();
        if (filtered.Count == 0)
        {
            await _dialog.ShowInfoAsync(_locale.T("Faf_Title"), _locale.T("Faf_NoAvailableBrs"), cancellationToken);
            return;
        }

        var pickerItems = filtered.Select(b =>
        {
            var (_, _, ttc) = DocumentTotalsHelper.BonReceptionTotals(b.Lignes ?? []);
            var montantLabel = _locale.Tf("Doc_FmtTtc", ttc, Devise).TrimEnd();
            return (b.Id, b.Numero, b.Date, montantLabel);
        }).ToList();
        var selectedIds = await _dialog.ShowBrPickerAsync(_locale.T("Faf_AddBr"), pickerItems, cancellationToken);
        if (selectedIds == null || selectedIds.Count == 0) return;

        foreach (var brId in selectedIds)
            await AddBrLinesAsync(brId, cancellationToken);
    }

    [RelayCommand]
    private async Task AddBrLinesAsync(int brId, CancellationToken cancellationToken)
    {
        var lines = await _brLinkService.LoadBrLinesAsync(brId, cancellationToken);
        foreach (var l in lines)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            l.Reference = prod?.Reference ?? string.Empty;
            l.PropertyChanged += LineChanged;
            Lignes.Add(l);
        }
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var b = await db.BonsReception.AsNoTracking().FirstAsync(b => b.Id == brId, cancellationToken);
        LinkedBrs.Add(new LinkedBrRow(b.Id, b.Numero, b.Date));
        RefreshTotals();
    }

    public async Task LoadFromBrsAsync(IReadOnlyList<int> brIds, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        LinkedBrs.Clear();
        Lignes.Clear();
        FactureFournisseurId = null;
        Date = new DateTimeOffset(DateTime.Today);
        DateEcheance = Date.AddDays(30);
        EstPayee = false;
        Numero = _locale.T("Faf_NewNumPlaceholder");
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await LoadLookupsAsync(db, cancellationToken);

        var firstBrId = brIds[0];
        var firstBr = await db.BonsReception.AsNoTracking().FirstAsync(b => b.Id == firstBrId, cancellationToken);
        FournisseurId = firstBr.FournisseurId;

        foreach (var brId in brIds)
        {
            var br = await db.BonsReception.AsNoTracking().FirstAsync(b => b.Id == brId, cancellationToken);
            LinkedBrs.Add(new LinkedBrRow(br.Id, br.Numero, br.Date));
            var lines = await _brLinkService.LoadBrLinesAsync(brId, cancellationToken);
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
        Title = brIds.Count > 1 ? _locale.T("Faf_FromMultiBr") : _locale.T("Faf_FromBr");
        RefreshTotals();
    }

    [RelayCommand]
    private void AddLine()
    {
        var p = Produits.FirstOrDefault();
        var row = new FactureFournisseurLineRow
        {
            ProduitId = p?.Id ?? 0,
            Reference = p?.Reference ?? string.Empty,
            Designation = p?.Designation ?? string.Empty,
            Conditionnement = p?.Unite ?? string.Empty,
            Quantite = 1,
            PrixUnitaireHt = p?.PrixAchatHT ?? 0,
            Remise = 0,
            TauxTva = p?.TauxTVA ?? 20
        };
        row.PropertyChanged += LineChanged;
        Lignes.Add(row);
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveLine(FactureFournisseurLineRow? row)
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
        if (FournisseurId == 0 || !Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("Faf_Title"), _locale.T("Faf_ErrFournisseurLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(ComputeFullPaymentTtc()))
        {
            await _dialog.ShowErrorAsync(_locale.T("Faf_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        if (FactureFournisseurId != null)
        {
            await using var checkDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var paid = await checkDb.PaiementsFournisseurs.AsNoTracking()
                .Where(p => p.FactureFournisseurId == FactureFournisseurId)
                .SumAsync(p => p.Montant, cancellationToken);
            if (!await ValidatePaymentsAgainstTtcAsync(ComputeFullPaymentTtc(), paid, cancellationToken))
                return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            Models.FactureFournisseur entity;
            if (FactureFournisseurId == null)
            {
                var num = await _numbers.NextFactureFournisseurAsync(cancellationToken);
                entity = new Models.FactureFournisseur
                {
                    Numero = num,
                    FournisseurId = FournisseurId,
                    Date = Date.DateTime,
                    DateEcheance = DateEcheance.DateTime,
                    EstPayee = EstPayee,
                    RemiseGlobale = RemiseGlobale,
                    Note = Note,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new FactureFournisseurLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        Quantite = l.Quantite,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva,
                        BonReceptionId = l.BonReceptionId
                    });
                }

                DocumentTotalsHelper.SyncFactureFournisseurTotalTtc(entity);
                db.FacturesFournisseurs.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                FactureFournisseurId = entity.Id;

                foreach (var br in LinkedBrs)
                {
                    var brEntity = await db.BonsReception.FindAsync(br.Id);
                    if (brEntity != null)
                        brEntity.FactureFournisseurId = entity.Id;
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                entity = await db.FacturesFournisseurs.Include(f => f.Lignes).FirstAsync(f => f.Id == FactureFournisseurId, cancellationToken);

                entity.FournisseurId = FournisseurId;
                entity.Date = Date.DateTime;
                entity.DateEcheance = DateEcheance.DateTime;
                entity.EstPayee = EstPayee;
                entity.RemiseGlobale = RemiseGlobale;
                entity.Note = Note;
                db.FactureFournisseurLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new FactureFournisseurLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        Quantite = l.Quantite,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva,
                        BonReceptionId = l.BonReceptionId
                    });
                }

                DocumentTotalsHelper.SyncFactureFournisseurTotalTtc(entity);
                await db.SaveChangesAsync(cancellationToken);
            }

            var linkedBrIds = LinkedBrs.Select(b => b.Id).ToHashSet();
            var existingBrs = await db.BonsReception.Where(b => b.FactureFournisseurId == FactureFournisseurId).ToListAsync(cancellationToken);
            foreach (var br in existingBrs)
            {
                if (!linkedBrIds.Contains(br.Id))
                    br.FactureFournisseurId = null;
            }

            foreach (var br in LinkedBrs)
            {
                var brEntity = await db.BonsReception.FindAsync(br.Id);
                if (brEntity != null)
                    brEntity.FactureFournisseurId = FactureFournisseurId;
            }

            await db.SaveChangesAsync(cancellationToken);

            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("Faf_Title"), _locale.T("Faf_Saved"), cancellationToken);
            await LoadAsync(FactureFournisseurId, cancellationToken);
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

        if (!FactureFournisseurId.HasValue)
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
            await _factureFournisseurWorkflow.AddPaiementAsync(FactureFournisseurId.Value, new PaiementFournisseur
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
            await LoadAsync(FactureFournisseurId, cancellationToken);
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
    private void OpenLinkedBr(LinkedBrRow? br)
    {
        if (br == null) return;
        var vm = _sp.GetRequiredService<BREditViewModel>();
        vm.Load(br.Id);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<FactureFournisseurListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (FactureFournisseurId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildFactureFournisseurPdfBytesAsync(cancellationToken);
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
        if (FactureFournisseurId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildFactureFournisseurPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildFactureFournisseurPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (FactureFournisseurId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var f = await db.FacturesFournisseurs.Include(x => x.Lignes).Include(x => x.Paiements).FirstAsync(x => x.Id == id, cancellationToken);
        var fournisseur = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == f.FournisseurId, cancellationToken);
        return await _pdf.BuildFactureFournisseurPdfAsync(f, DocumentPartyPdfInfo.FromTiers(fournisseur), cancellationToken);
    }
}
