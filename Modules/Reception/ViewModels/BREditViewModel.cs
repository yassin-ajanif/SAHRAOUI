using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.CommandeFournisseur.Models;
using GestionCommerciale.Modules.FactureFournisseur.Services;
using GestionCommerciale.Modules.FactureFournisseur.ViewModels;
using GestionCommerciale.Modules.Reception.Models;
using GestionCommerciale.Modules.Reception.Services;
using GestionCommerciale.Modules.Stock.Services;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Reception.ViewModels;

public partial class BREditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IBonReceptionWorkflowService _workflow;
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
    private readonly IFactureFournisseurBrLinkService _brLinkService;
    private int? _sourceBonCommandeId;

    public BREditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IBonReceptionWorkflowService workflow,
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
        IFactureFournisseurBrLinkService brLinkService)
    {
        _dbFactory = dbFactory;
        _numbers = numbers;
        _workflow = workflow;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _session = session;
        _locale = locale;
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _uiPreferences = uiPreferences;
        _stock = stock;
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _settings = settings;
        _brLinkService = brLinkService;
        _locale.CultureApplied += (_, _) => RefreshBrUi();
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("bon_reception", LineGridColumns);
        Lignes.CollectionChanged += LignesOnCollectionChanged;
        Title = _locale.T("BR_Title");
        RefreshBrUi();
    }

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _btnToInvoice = string.Empty;
    [ObservableProperty] private string _menuDeleteBr = string.Empty;
    [ObservableProperty] private string _lblSupplier = string.Empty;
    [ObservableProperty] private string _wmSupplierSearch = string.Empty;
    [ObservableProperty] private string _lblDateBr = string.Empty;
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
    [ObservableProperty] private string _wmNote = string.Empty;
    [ObservableProperty] private string _invoicedLabel = string.Empty;

    public bool HasInvoicedLabel => !string.IsNullOrEmpty(InvoicedLabel);

    partial void OnInvoicedLabelChanged(string value) => OnPropertyChanged(nameof(HasInvoicedLabel));

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

    public DocumentLineGridColumnState LineGridColumns { get; } = new(supportsLineRemise: false);

    public AutoCompleteFilterPredicate<object?> ProduitAutocompleteFilter => ProductAutoComplete.ItemFilter;
    public AutoCompleteFilterPredicate<object?> PartyAutocompleteFilter => PartyAutoComplete.ItemFilter;

    public bool ShowTotalTva => LineGridColumns.ShowTva && LineGridColumns.ShowMontantTtc;
    public bool ShowTotalTtc => LineGridColumns.ShowMontantTtc && LineGridColumns.ShowTva;

    private void OnLineGridColumnsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentLineGridColumnState.ShowTva) or nameof(DocumentLineGridColumnState.ShowMontantTtc))
        {
            OnPropertyChanged(nameof(ShowTotalTva));
            OnPropertyChanged(nameof(ShowTotalTtc));
            RefreshTotals();
        }

        _uiPreferences.SaveDocumentLineColumns("bon_reception", LineGridColumns);
    }

    private void LignesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (BRLineRow row in e.NewItems)
                row.PropertyChanged += LineOnPropertyChanged;
        if (e.OldItems != null)
            foreach (BRLineRow row in e.OldItems)
                row.PropertyChanged -= LineOnPropertyChanged;
        RefreshTotals();
    }

    private void LineOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshTotals();

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

    partial void OnDeviseChanged(string value) => RefreshTotals();

    private void RefreshBrUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        BtnToInvoice = _locale.T("Btn_ToInvoice");
        MenuDeleteBr = _locale.T("BR_MenuDelete");
        LblSupplier = _locale.T("Lbl_Supplier");
        WmSupplierSearch = _locale.T("Wm_SearchSupplier");
        LblDateBr = _locale.T("Lbl_DateBR");
        BtnAddLine = _locale.T("Btn_AddLine");
        BtnApplyProduct = _locale.T("Btn_ApplyProduct");
        BtnRemoveLine = _locale.T("Btn_RemoveLine");
        LblAddProduct = _locale.T("Devis_LblAddProduct");
        WmAddProduct = _locale.T("Devis_WmSearchProduct");
        WmNote = _locale.T("Lbl_Note");
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
        UpdateTotalLabels(TotalHt, TotalTva, TotalTtc);
    }

    partial void OnIsReadOnlyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));

    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Fournisseurs { get; } = [];
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<BRLineRow> Lignes { get; } = [];

    [ObservableProperty] private int? _brId;
    [ObservableProperty] private int _fournisseurId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedFournisseur;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private BRLineRow? _selectedLine;

    public bool CanEdit => !IsReadOnly;

    partial void OnBrIdChanged(int? value) => RemoveBrCommand.NotifyCanExecuteChanged();

    private bool CanRemoveBr() => BrId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveBr))]
    private async Task RemoveBrAsync(CancellationToken cancellationToken)
    {
        if (BrId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("BR_DlgShort"), _locale.Tf("BR_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.BonsReception.Include(b => b.Lignes).FirstAsync(b => b.Id == id, cancellationToken);
            await _stock.SyncBonReceptionStockAsync(db, entity.Id, entity.Numero, [], null, cancellationToken);
            db.BonsReception.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            await _dialog.ShowInfoAsync(_locale.T("BR_DlgShort"), _locale.T("BR_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("BR_DlgShort"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            SelectedFournisseur = Fournisseurs.FirstOrDefault(f => f.Id == value);
        RefreshWhatsAppContact();
    }

    private void RefreshWhatsAppContact()
    {
        var prefill = string.IsNullOrWhiteSpace(Numero) || Numero.Contains('(', StringComparison.Ordinal)
            ? null
            : Numero;
        WhatsApp.SetPhone(SelectedFournisseur?.Telephone, prefill);
    }

    partial void OnAddLineCatalogPickChanged(object? value)
    {
        if (_suppressAddLinePick || !CanEdit) return;
        if (value is not GestionCommerciale.Modules.Stock.Models.Produit p) return;
        _suppressAddLinePick = true;
        var existing = Lignes.FirstOrDefault(l => l.ProduitId == p.Id && p.Id != 0);
        if (existing != null)
        {
            existing.QuantiteRecue += 1;
            SelectedLine = existing;
        }
        else
        {
            var row = new BRLineRow();
            row.ApplyCatalogProduct(p);
            row.QuantiteRecue = 1;
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
    }

    public async Task LoadAsync(int? id, CancellationToken cancellationToken = default)
    {
        _sourceBonCommandeId = null;
        BrId = id;
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var fournisseurs = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Fournisseur || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        Fournisseurs.Clear();
        foreach (var f in fournisseurs) Fournisseurs.Add(f);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);

        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
        InvoicedLabel = string.Empty;

        if (id == null)
        {
            Numero = "(brouillon)";
            FournisseurId = Fournisseurs.FirstOrDefault()?.Id ?? 0;
            IsReadOnly = false;
            Title = _locale.T("BR_NewTitle");
            RefreshTotals();
            return;
        }

        var factNum = await _brLinkService.GetInvoicingStatusAsync(id.Value, cancellationToken);
        if (factNum != null)
            InvoicedLabel = _locale.Tf("BR_FacturedOn", factNum);

        var b = await db.BonsReception.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        Numero = b.Numero;
        FournisseurId = b.FournisseurId;
        Date = new DateTimeOffset(b.Date);
        Note = b.Note;
        foreach (var l in b.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new BRLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = prod?.Unite ?? string.Empty,
                QuantiteRecue = l.QuantiteRecue,
                PrixUnitaireHt = l.PrixUnitaireHT,
                TauxTva = l.TauxTVA
            });
        }

        IsReadOnly = false;
        Title = _locale.Tf("BR_TitleNum", Numero);
        RefreshTotals();
    }

    public void Load(int? id) => _ = LoadAsync(id, CancellationToken.None);

    /// <summary>Prépare un nouveau BR à partir d'un bon de commande validé (lignes et fournisseur copiés).</summary>
    public async Task<bool> LoadNewFromBonCommandeAsync(int bonCommandeId, CancellationToken cancellationToken = default)
    {
        BrId = null;
        _sourceBonCommandeId = bonCommandeId;
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var bc = await db.BonsCommande.Include(x => x.Lignes).FirstAsync(x => x.Id == bonCommandeId, cancellationToken);

        var fournisseurs = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Fournisseur || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        Fournisseurs.Clear();
        foreach (var f in fournisseurs) Fournisseurs.Add(f);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);

        FournisseurId = bc.FournisseurId;
        Date = new DateTimeOffset(DateTime.Today);
        Note = string.Empty;
        Numero = "(brouillon)";
        foreach (var l in bc.Lignes.OrderBy(x => x.Id))
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new BRLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = prod?.Unite ?? string.Empty,
                QuantiteRecue = l.QuantiteCommandee,
                PrixUnitaireHt = l.PrixUnitaireHT,
                TauxTva = l.TauxTVA
            });
        }

        IsReadOnly = false;
        Title = _locale.Tf("BR_NewFromBc", bc.Numero);
        RefreshTotals();
        return true;
    }

    [RelayCommand]
    private void AddLine()
    {
        if (!CanEdit) return;
        var p = Produits.FirstOrDefault();
        var row = new BRLineRow();
        if (p != null)
            row.ApplyCatalogProduct(p);
        else
        {
            row.TauxTva = 20;
        }
        row.QuantiteRecue = 1;
        Lignes.Add(row);
    }

    [RelayCommand]
    private void RemoveLine(BRLineRow? row)
    {
        if (!CanEdit || row == null) return;
        Lignes.Remove(row);
    }

    [RelayCommand]
    private void ApplyProductToSelected() => ApplyProduct(SelectedLine);

    private void ApplyProduct(BRLineRow? row)
    {
        if (row == null) return;
        var p = Produits.FirstOrDefault(x => x.Id == row.ProduitId);
        if (p == null) return;
        row.ApplyCatalogProduct(p);
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
            await _dialog.ShowErrorAsync(_locale.T("BR_DlgShort"), _locale.T("BR_ErrNoEdit"), cancellationToken);
            return;
        }

        if (FournisseurId == 0 || !Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("BR_DlgShort"), _locale.T("BR_ErrSupplierLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(TotalTtc))
        {
            await _dialog.ShowErrorAsync(_locale.T("BR_DlgShort"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            BonReception entity;
            if (BrId == null)
            {
                var num = await _numbers.NextBRAsync(cancellationToken);
                entity = new BonReception
                {
                    Numero = num,
                    BonCommandeId = _sourceBonCommandeId,
                    FournisseurId = FournisseurId,
                    Date = Date.DateTime,
                    Note = Note,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonReceptionLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        QuantiteRecue = l.QuantiteRecue,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        TauxTVA = l.TauxTva
                    });
                }

                DocumentTotalsHelper.SyncBonReceptionTotalTtc(entity);
                db.BonsReception.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                BrId = entity.Id;
                _sourceBonCommandeId = null;
            }
            else
            {
                entity = await db.BonsReception.Include(b => b.Lignes).FirstAsync(b => b.Id == BrId, cancellationToken);
                entity.FournisseurId = FournisseurId;
                entity.Date = Date.DateTime;
                entity.Note = Note;
                db.BonReceptionLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonReceptionLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        QuantiteRecue = l.QuantiteRecue,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        TauxTVA = l.TauxTva
                    });
                }

                DocumentTotalsHelper.SyncBonReceptionTotalTtc(entity);
                await db.SaveChangesAsync(cancellationToken);
            }

            try
            {
                await _workflow.ValiderAsync(entity.Id, _session.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                await _dialog.ShowErrorAsync(_locale.T("BR_DlgShort"), ex.Message, cancellationToken);
                await LoadAsync(BrId, cancellationToken);
                return;
            }

            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("BR_DlgShort"), _locale.T("BR_Saved"), cancellationToken);
            await LoadAsync(BrId, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToFactureAsync(CancellationToken cancellationToken)
    {
        if (BrId == null) return;
        var factNum = await _brLinkService.GetInvoicingStatusAsync(BrId.Value, cancellationToken);
        if (factNum != null)
        {
            await _dialog.ShowErrorAsync(_locale.T("BR_DlgShort"), _locale.Tf("BR_ErrAlreadyInvoiced", factNum), cancellationToken);
            return;
        }

        var vm = _sp.GetRequiredService<FactureFournisseurEditViewModel>();
        vm.LoadFromBR(BrId.Value);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<BRListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (BrId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBrPdfBytesAsync(cancellationToken);
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
        if (BrId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBrPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildBrPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (BrId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var b = await db.BonsReception.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        var fournisseur = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == b.FournisseurId, cancellationToken);
        return await _pdf.BuildBonReceptionPdfAsync(b, DocumentPartyPdfInfo.FromTiers(fournisseur), cancellationToken);
    }
}
