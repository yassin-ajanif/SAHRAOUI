using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.AvoirFournisseur.Models;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Stock.Models;
using GestionCommerciale.Modules.Stock.Services;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.AvoirFournisseur.ViewModels;

public partial class AvoirFournisseurEditViewModel : BaseViewModel
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
    private readonly IStockMovementService _stock;

    public AvoirFournisseurEditViewModel(
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
        IAppSettingsService settings,
        IStockMovementService stock)
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
        _stock = stock;
        _locale.CultureApplied += (_, _) =>
        {
            RefreshUi();
            UpdateTotalLines();
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("avoirFournisseur", LineGridColumns);
        Title = _locale.T("Avf_Title");
        RefreshUi();
        _ = LoadFournisseursAsync(CancellationToken.None);
    }

    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Fournisseurs { get; } = [];
    public ObservableCollection<Produit> Produits { get; } = [];
    public ObservableCollection<AvoirFournisseurLineRow> Lignes { get; } = [];

    [ObservableProperty] private int? _avoirFournisseurId;
    [ObservableProperty] private int _fournisseurId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedFournisseur;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private string _motif = string.Empty;
    [ObservableProperty] private bool _retourMarchandise;
    [ObservableProperty] private decimal _totalHt;
    [ObservableProperty] private decimal _totalTva;
    [ObservableProperty] private decimal _totalTtc;
    [ObservableProperty] private bool _canEditDraft = true;
    [ObservableProperty] private AvoirFournisseurLineRow? _selectedLine;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;

    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _lblFournisseur = string.Empty;
    [ObservableProperty] private string _wmFournisseurSearch = string.Empty;
    [ObservableProperty] private string _lblDate = string.Empty;
    [ObservableProperty] private string _btnRemoveLine = string.Empty;
    [ObservableProperty] private string _lblCatalogHint = string.Empty;
    [ObservableProperty] private string _lblTotals = string.Empty;
    [ObservableProperty] private string _devise = string.Empty;
    [ObservableProperty] private string _totalHtLabel = string.Empty;
    [ObservableProperty] private string _totalTvaLabel = string.Empty;
    [ObservableProperty] private string _totalTtcLabel = string.Empty;
    [ObservableProperty] private string _wmMotif = string.Empty;
    [ObservableProperty] private string _chkRetourStock = string.Empty;
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
        _uiPreferences.SaveDocumentLineColumns("avoirFournisseur", LineGridColumns);
    }

    private void RefreshUi()
    {
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        LblFournisseur = _locale.T("Avf_LblFournisseur");
        WmFournisseurSearch = _locale.T("Wm_SearchClient");
        LblDate = _locale.T("Avf_LblDate");
        BtnRemoveLine = _locale.T("Btn_RemoveLine");
        LblCatalogHint = _locale.T("Lbl_CatalogHintAvoir");
        LblTotals = _locale.T("Lbl_Totals");
        WmMotif = _locale.T("Lbl_Motif");
        ChkRetourStock = _locale.T("Lbl_ReturnStock");
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

    private void UpdateTotalLines()
    {
        TotalHtLabel = _locale.Tf("Doc_FmtHt", TotalHt, Devise).TrimEnd();
        TotalTvaLabel = _locale.Tf("Doc_FmtTva", TotalTva, Devise).TrimEnd();
        TotalTtcLabel = _locale.Tf("Doc_FmtTtc", TotalTtc, Devise).TrimEnd();
    }

    partial void OnDeviseChanged(string value) => RefreshTotals();

    private async Task LoadDeviseAsync(CancellationToken cancellationToken)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
    }

    private void RefreshTotals()
    {
        var includeTva = ShowTotalTtc;
        var lines = Lignes.Select(l => new AvoirFournisseurLigne
        {
            Quantite = l.Quantite,
            PrixUnitaireHT = l.PrixUnitaireHt,
            Remise = l.Remise,
            TauxTVA = includeTva ? l.TauxTva : 0
        });
        var (ht, tva, ttc) = DocumentTotalsHelper.AvoirFournisseurTotals(lines);
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        UpdateTotalLines();
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
        if (_suppressAddLinePick) return;
        if (value is not Produit p) return;

        var existing = Lignes.FirstOrDefault(l => l.ProduitId == p.Id && l.ProduitId != 0);
        if (existing is not null)
        {
            existing.Quantite++;
            SelectedLine = existing;
        }
        else
        {
            var row = new AvoirFournisseurLineRow();
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

    private void LineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AvoirFournisseurLineRow.MontantHt) or nameof(AvoirFournisseurLineRow.MontantTtc))
            RefreshTotals();
    }

    [RelayCommand]
    private void RemoveLine(AvoirFournisseurLineRow? line)
    {
        if (line is null) return;
        line.PropertyChanged -= LineChanged;
        Lignes.Remove(line);
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveSelectedLine()
    {
        if (SelectedLine is null) return;
        RemoveLine(SelectedLine);
    }

    private async Task LoadFournisseursAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var list = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Fournisseur || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(ct);
        Fournisseurs.Clear();
        foreach (var f in list) Fournisseurs.Add(f);
    }

    private async Task LoadProduitsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(ct);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);
    }

    public void Load(int? id)
    {
        foreach (var l in Lignes) l.PropertyChanged -= LineChanged;
        if (id == null)
            _ = LoadNewAsync(CancellationToken.None);
        else
            _ = LoadExistingAsync(id.Value, CancellationToken.None);
    }

    private async Task LoadNewAsync(CancellationToken cancellationToken)
    {
        AvoirFournisseurId = null;
        FournisseurId = Fournisseurs.FirstOrDefault()?.Id ?? 0;
        Lignes.Clear();
        Numero = _locale.T("Avf_DraftPlaceholder");
        Date = new DateTimeOffset(DateTime.Today);
        Motif = string.Empty;
        RetourMarchandise = false;
        CanEditDraft = true;
        await LoadDeviseAsync(cancellationToken);
        await LoadProduitsAsync(cancellationToken);
        RefreshTotals();
        Title = _locale.T("Avf_NewTitle");
    }

    private async Task LoadExistingAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var doc = await db.Set<Models.AvoirFournisseur>().Include(x => x.Lignes)
            .FirstAsync(x => x.Id == id, cancellationToken);
        AvoirFournisseurId = doc.Id;
        FournisseurId = doc.FournisseurId;
        Numero = doc.Numero;
        Date = new DateTimeOffset(doc.Date);
        Motif = doc.Motif;
        RetourMarchandise = doc.RetourMarchandise;
        Lignes.Clear();
        foreach (var l in doc.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            var row = new AvoirFournisseurLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? l.Designation,
                Designation = l.Designation,
                Conditionnement = l.Conditionnement,
                Quantite = l.Quantite,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            };
            row.PropertyChanged += LineChanged;
            Lignes.Add(row);
        }

        CanEditDraft = true;
        await LoadDeviseAsync(cancellationToken);
        await LoadProduitsAsync(cancellationToken);
        RefreshTotals();
        Title = _locale.Tf("Avf_TitleNum", Numero);
    }

    [RelayCommand]
    private void AddLine()
    {
        var p = Produits.FirstOrDefault();
        var row = new AvoirFournisseurLineRow();
        if (p != null)
            row.ApplyCatalogProduct(p);
        row.PropertyChanged += LineChanged;
        Lignes.Add(row);
        RefreshTotals();
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("Avf_Title"), _locale.T("Avf_ErrLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(TotalTtc))
        {
            await _dialog.ShowErrorAsync(_locale.T("Avf_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            Models.AvoirFournisseur entity;
            if (AvoirFournisseurId == null)
            {
                var num = await _numbers.NextAvoirFournisseurAsync(cancellationToken);
                entity = new Models.AvoirFournisseur
                {
                    Numero = num,
                    FournisseurId = FournisseurId,
                    Date = Date.DateTime,
                    Motif = Motif,
                    RetourMarchandise = RetourMarchandise,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new Models.AvoirFournisseurLigne
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

                db.AvoirsFournisseurs.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                AvoirFournisseurId = entity.Id;
                Numero = entity.Numero;
                Title = _locale.Tf("Avf_TitleNum", Numero);
            }
            else
            {
                entity = await db.AvoirsFournisseurs.Include(x => x.Lignes)
                    .FirstAsync(x => x.Id == AvoirFournisseurId, cancellationToken);
                entity.FournisseurId = FournisseurId;
                entity.Date = Date.DateTime;
                entity.Motif = Motif;
                entity.RetourMarchandise = RetourMarchandise;
                db.AvoirFournisseurLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new Models.AvoirFournisseurLigne
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
            }

            await _stock.SyncAvoirFournisseurStockAsync(
                db,
                entity.Id,
                entity.Numero,
                RetourMarchandise,
                Lignes.Select(l => (l.ProduitId, l.Quantite)),
                _session.UserId,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await _dialog.ShowInfoAsync(_locale.T("Avf_Title"), _locale.T("Avf_Saved"), cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (AvoirFournisseurId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildAvoirFournisseurPdfBytesAsync(cancellationToken);
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
        if (AvoirFournisseurId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildAvoirFournisseurPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildAvoirFournisseurPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (AvoirFournisseurId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var a = await db.AvoirsFournisseurs.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        var fournisseur = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == a.FournisseurId, cancellationToken);
        return await _pdf.BuildAvoirFournisseurPdfAsync(a, DocumentPartyPdfInfo.FromTiers(fournisseur), cancellationToken);
    }

    [RelayCommand]
    private void Back()
    {
        var vm = _sp.GetRequiredService<AvoirFournisseurListViewModel>();
        _workspace.Open(vm);
    }
}
