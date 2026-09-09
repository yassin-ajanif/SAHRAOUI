using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.CommandeFournisseur.Models;
using GestionCommerciale.Modules.Reception.ViewModels;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.CommandeFournisseur.ViewModels;

public partial class BCEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly IAppSettingsService _settings;

    public BCEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ICurrentUserSession session,
        ILocaleService locale,
        IUiPreferencesService uiPreferences,
        IPdfService pdf,
        IPdfPrintService pdfPrint,
        IAppSettingsService settings)
    {
        _dbFactory = dbFactory;
        _numbers = numbers;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _session = session;
        _locale = locale;
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _uiPreferences = uiPreferences;
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _settings = settings;
        _locale.CultureApplied += (_, _) => RefreshBcUi();
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("bon_commande", LineGridColumns);
        Lignes.CollectionChanged += LignesOnCollectionChanged;
        Title = _locale.T("BC_Title");
        RefreshBcUi();
    }

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _btnToBr = string.Empty;
    [ObservableProperty] private string _menuDeleteBc = string.Empty;
    [ObservableProperty] private string _lblSupplier = string.Empty;
    [ObservableProperty] private string _wmSupplierSearch = string.Empty;
    [ObservableProperty] private string _lblDateBc = string.Empty;
    [ObservableProperty] private string _btnAddLine = string.Empty;
    [ObservableProperty] private string _btnRemoveLine = string.Empty;
    [ObservableProperty] private string _lblAddProduct = string.Empty;
    [ObservableProperty] private string _wmAddProduct = string.Empty;
    [ObservableProperty] private string _lblTotals = string.Empty;
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

    public DocumentLineGridColumnState LineGridColumns { get; } = new(supportsLineRemise: true);

    public AutoCompleteFilterPredicate<object?> ProduitAutocompleteFilter => ProductAutoComplete.ItemFilter;
    public AutoCompleteFilterPredicate<object?> PartyAutocompleteFilter => PartyAutoComplete.ItemFilter;

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

    private void OnLineGridColumnsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        _uiPreferences.SaveDocumentLineColumns("bon_commande", LineGridColumns);

    private void LignesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (BCLineRow row in e.NewItems)
                row.PropertyChanged += LineOnPropertyChanged;
        if (e.OldItems != null)
            foreach (BCLineRow row in e.OldItems)
                row.PropertyChanged -= LineOnPropertyChanged;
        RefreshTotals();
    }

    private void LineOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BCLineRow.ProduitId) && sender is BCLineRow row && row.ProduitId != 0)
            ConsolidateDuplicateProductLines();
        RefreshTotals();
    }

    private void RefreshBcUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        BtnToBr = _locale.T("Btn_ToBR");
        MenuDeleteBc = _locale.T("BC_MenuDelete");
        LblSupplier = _locale.T("Lbl_Supplier");
        WmSupplierSearch = _locale.T("Wm_SearchSupplier");
        LblDateBc = _locale.T("Lbl_DateBC");
        BtnAddLine = _locale.T("Btn_AddLine");
        BtnRemoveLine = _locale.T("Btn_RemoveLine");
        LblAddProduct = _locale.T("Devis_LblAddProduct");
        WmAddProduct = _locale.T("Devis_WmSearchProduct");
        LblTotals = _locale.T("Lbl_Totals");
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
        UpdateTotalLabels(TotalHt, TotalTva, TotalTtc);
    }

    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Fournisseurs { get; } = [];
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<BCLineRow> Lignes { get; } = [];

    [ObservableProperty] private int? _bcId;
    [ObservableProperty] private int _fournisseurId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedFournisseur;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private BCLineRow? _selectedLine;

    public bool CanEdit => true;

    partial void OnBcIdChanged(int? value) => RemoveBcCommand.NotifyCanExecuteChanged();

    private bool CanRemoveBc() => BcId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveBc))]
    private async Task RemoveBcAsync(CancellationToken cancellationToken)
    {
        if (BcId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("BC_Title"), _locale.Tf("BC_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var blockedMsg = await BonCommandeDeleteReferencedMessage.BuildIfBlockedAsync(db, id, _locale, cancellationToken);
            if (blockedMsg != null)
            {
                await _dialog.ShowErrorAsync(_locale.T("BC_Title"), blockedMsg, cancellationToken);
                return;
            }

            var entity = await db.BonsCommande.Include(b => b.Lignes).FirstAsync(b => b.Id == id, cancellationToken);
            db.BonsCommande.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            await _dialog.ShowInfoAsync(_locale.T("BC_Title"), _locale.T("BC_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("BC_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
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
            existing.QuantiteCommandee += 1;
            SelectedLine = existing;
        }
        else
        {
            var row = new BCLineRow();
            row.ApplyCatalogProduct(p);
            row.QuantiteCommandee = 1;
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
            var extraQty = ordered.Skip(1).Sum(l => l.QuantiteCommandee);
            foreach (var line in ordered.Skip(1))
            {
                if (ReferenceEquals(SelectedLine, line))
                    SelectedLine = keep;
                Lignes.Remove(line);
            }
            keep.QuantiteCommandee += extraQty;
        }
    }

    private void RefreshTotals()
    {
        var ht = Lignes.Sum(l => l.MontantHt);
        var tva = LineGridColumns.ShowTva ? Lignes.Sum(l => l.MontantHt * (l.TauxTva / 100m)) : 0m;
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

    public async Task LoadAsync(int? id, CancellationToken cancellationToken = default)
    {
        BcId = id;
        Lignes.Clear();
        SelectedLine = null;
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

        if (id == null)
        {
            Numero = "(brouillon)";
            FournisseurId = Fournisseurs.FirstOrDefault()?.Id ?? 0;
            Title = _locale.T("BC_NewTitle");
            RefreshTotals();
            return;
        }

        var b = await db.BonsCommande.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        Numero = b.Numero;
        FournisseurId = b.FournisseurId;
        Date = new DateTimeOffset(b.Date);
        Note = b.Note;
        foreach (var l in b.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            Lignes.Add(new BCLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = l.Conditionnement,
                QuantiteCommandee = l.QuantiteCommandee,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            });
        }

        Title = _locale.Tf("BC_TitleNum", Numero);
        RefreshTotals();
    }

    public void Load(int? id) => _ = LoadAsync(id, CancellationToken.None);

    [RelayCommand]
    private void AddLine()
    {
        if (!CanEdit) return;
        var p = Produits.FirstOrDefault();
        Lignes.Add(new BCLineRow
        {
            ProduitId = p?.Id ?? 0,
            Reference = p?.Reference ?? string.Empty,
            Designation = p?.Designation ?? string.Empty,
            Conditionnement = p?.Unite ?? string.Empty,
            QuantiteCommandee = 1,
            PrixUnitaireHt = p?.PrixAchatHT ?? 0,
            TauxTva = p?.TauxTVA ?? 20
        });
    }

    [RelayCommand]
    private void RemoveLine(BCLineRow? row)
    {
        if (!CanEdit || row == null) return;
        Lignes.Remove(row);
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
        if (FournisseurId == 0 || !Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("BC_Title"), _locale.T("BC_ErrSupplierLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(TotalTtc))
        {
            await _dialog.ShowErrorAsync(_locale.T("BC_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            BonCommande entity;
            if (BcId == null)
            {
                var num = await _numbers.NextBCAsync(cancellationToken);
                entity = new BonCommande
                {
                    Numero = num,
                    FournisseurId = FournisseurId,
                    Date = Date.DateTime,
                    Note = Note,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonCommandeLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        QuantiteCommandee = l.QuantiteCommandee,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva
                    });
                }

                db.BonsCommande.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                BcId = entity.Id;
            }
            else
            {
                entity = await db.BonsCommande.Include(b => b.Lignes).FirstAsync(b => b.Id == BcId, cancellationToken);
                entity.FournisseurId = FournisseurId;
                entity.Date = Date.DateTime;
                entity.Note = Note;
                db.BonCommandeLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new BonCommandeLigne
                    {
                        ProduitId = l.ProduitId,
                        Designation = l.Designation,
                        Conditionnement = l.Conditionnement,
                        QuantiteCommandee = l.QuantiteCommandee,
                        PrixUnitaireHT = l.PrixUnitaireHt,
                        Remise = l.Remise,
                        TauxTVA = l.TauxTva
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("BC_Title"), _locale.T("BC_Saved"), cancellationToken);
            await LoadAsync(BcId, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToBrAsync(CancellationToken cancellationToken)
    {
        if (BcId is not { } id)
        {
            await _dialog.ShowErrorAsync(_locale.T("BC_Title"), _locale.T("BC_ToBrNeedSave"), cancellationToken);
            return;
        }

        var vm = _sp.GetRequiredService<BREditViewModel>();
        await vm.LoadNewFromBonCommandeAsync(id, cancellationToken);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<BCListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (BcId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBcPdfBytesAsync(cancellationToken);
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
        if (BcId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildBcPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildBcPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (BcId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var b = await db.BonsCommande.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        var fournisseur = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == b.FournisseurId, cancellationToken);
        return await _pdf.BuildBonCommandePdfAsync(b, DocumentPartyPdfInfo.FromTiers(fournisseur), cancellationToken);
    }
}
