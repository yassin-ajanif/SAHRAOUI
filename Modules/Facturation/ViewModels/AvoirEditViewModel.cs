using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Stock.Models;
using GestionCommerciale.Modules.Stock.Services;
using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Facturation.ViewModels;

public partial class AvoirLineRow : ObservableObject
{
    [ObservableProperty] private int _produitId;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _designation = string.Empty;
    [ObservableProperty] private string _conditionnement = string.Empty;
    [ObservableProperty] private decimal _quantite = 1;
    [ObservableProperty] private decimal _prixUnitaireHt;
    [ObservableProperty] private decimal _remise;
    [ObservableProperty] private decimal _tauxTva;

    public decimal MontantHt => DocumentTotalsHelper.LigneHT(Quantite, PrixUnitaireHt, Remise);
    public decimal MontantTtc => MontantHt * (1 + TauxTva / 100m);

    partial void OnQuantiteChanged(decimal value) => NotifyMontants();
    partial void OnPrixUnitaireHtChanged(decimal value) => NotifyMontants();
    partial void OnRemiseChanged(decimal value) => NotifyMontants();
    partial void OnTauxTvaChanged(decimal value) => NotifyMontants();

    public void ApplyCatalogProduct(Produit p)
    {
        ProduitId = p.Id;
        Reference = p.Reference;
        Designation = p.Designation;
        Conditionnement = p.Unite;
        PrixUnitaireHt = p.PrixVenteHT;
        TauxTva = p.TauxTVA;
        NotifyMontants();
    }

    private void NotifyMontants()
    {
        OnPropertyChanged(nameof(MontantHt));
        OnPropertyChanged(nameof(MontantTtc));
    }
}

public partial class AvoirEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IAvoirWorkflowService _workflow;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly IStockMovementService _stock;
    private readonly IAppSettingsService _settings;
    private readonly ClientSoldeDisplay _clientSolde;

    public AvoirEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IAvoirWorkflowService workflow,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ICurrentUserSession session,
        ILocaleService locale,
        IUiPreferencesService uiPreferences,
        IPdfService pdf,
        IPdfPrintService pdfPrint,
        IStockMovementService stock,
        IAppSettingsService settings,
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
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _stock = stock;
        _settings = settings;
        _clientSolde = new ClientSoldeDisplay(clientLedger, locale);
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _locale.CultureApplied += (_, _) =>
        {
            RefreshAvoirUi();
            UpdateTotalLines();
            if (ClientId > 0)
                _ = ClientSolde.RefreshAsync(ClientId, Devise);
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("avoir", LineGridColumns);
        Title = _locale.T("Avoir_Title");
        RefreshAvoirUi();
        ClientLookup.BindSelection(() => ClientId, c => SelectedClient = c);
        _ = LoadClientsAsync(CancellationToken.None);
    }

    public ClientCategoryFilter ClientLookup { get; } = new();
    public ClientSoldeDisplay ClientSolde => _clientSolde;
    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Clients => ClientLookup.Clients;
    public ObservableCollection<Produit> Produits { get; } = [];
    public ObservableCollection<AvoirLineRow> Lignes { get; } = [];

    [ObservableProperty] private int? _avoirId;
    partial void OnAvoirIdChanged(int? value) => RemoveAvoirCommand.NotifyCanExecuteChanged();
    [ObservableProperty] private int? _factureId;
    [ObservableProperty] private int _clientId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedClient;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private string _motif = string.Empty;
    [ObservableProperty] private bool _retourMarchandise;
    [ObservableProperty] private decimal _totalHt;
    [ObservableProperty] private decimal _totalTva;
    [ObservableProperty] private decimal _totalTtc;
    [ObservableProperty] private bool _canEditDraft;
    [ObservableProperty] private AvoirLineRow? _selectedLine;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;

    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _menuDeleteAvoir = string.Empty;
    [ObservableProperty] private string _lblClient = string.Empty;
    [ObservableProperty] private string _wmClientSearch = string.Empty;
    [ObservableProperty] private string _lblDateAvoir = string.Empty;
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
        _uiPreferences.SaveDocumentLineColumns("avoir", LineGridColumns);
    }

    private void RefreshAvoirUi()
    {
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        MenuDeleteAvoir = _locale.T("Avoir_MenuDelete");
        LblClient = _locale.T("Lbl_Client");
        WmClientSearch = _locale.T("Wm_SearchClient");
        LblDateAvoir = _locale.T("Lbl_DateAvoir");
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

    partial void OnDeviseChanged(string value)
    {
        RefreshTotals();
        if (ClientId > 0)
            _ = ClientSolde.RefreshAsync(ClientId, value);
    }

    private async Task LoadDeviseAsync(CancellationToken cancellationToken)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);
    }

    private void RefreshTotals()
    {
        var includeTva = ShowTotalTtc;
        var lines = Lignes.Select(l => new AvoirLigne
        {
            Quantite = l.Quantite,
            PrixUnitaireHT = l.PrixUnitaireHt,
            Remise = l.Remise,
            TauxTVA = includeTva ? l.TauxTva : 0
        });
        var (ht, tva, ttc) = DocumentTotalsHelper.AvoirTotals(lines);
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        UpdateTotalLines();
    }

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
            var row = new AvoirLineRow();
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
        if (e.PropertyName is nameof(AvoirLineRow.MontantHt) or nameof(AvoirLineRow.MontantTtc))
            RefreshTotals();
    }

    [RelayCommand]
    private void RemoveLine(AvoirLineRow? line)
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

    private async Task LoadClientsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var clients = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(ct);
        ClientLookup.ReplaceAll(clients);
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
        if (id == null)
            _ = LoadNewAsync(CancellationToken.None);
        else
            _ = LoadExistingAsync(id.Value, CancellationToken.None);
    }

    public void LoadNew(int factureId) => _ = LoadNewAsync(factureId, CancellationToken.None);

    private async Task LoadNewAsync(CancellationToken cancellationToken)
    {
        if (!_session.CanAccessAvoir)
        {
            await _dialog.ShowErrorAsync(_locale.T("Avoir_Title"), _locale.T("Avoir_ErrDenied"), cancellationToken);
            return;
        }

        foreach (var l in Lignes) l.PropertyChanged -= LineChanged;
        AvoirId = null;
        FactureId = null;
        ClientId = Clients.FirstOrDefault()?.Id ?? 0;
        Lignes.Clear();
        Numero = _locale.T("Avoir_DraftPlaceholder");
        Date = new DateTimeOffset(DateTime.Today);
        Motif = string.Empty;
        RetourMarchandise = false;
        CanEditDraft = true;
        await LoadDeviseAsync(cancellationToken);
        await LoadProduitsAsync(cancellationToken);
        RefreshTotals();
        Title = _locale.T("Avoir_NewTitle");
    }

    private async Task LoadNewAsync(int factureId, CancellationToken cancellationToken)
    {
        if (!_session.CanAccessAvoir)
        {
            await _dialog.ShowErrorAsync(_locale.T("Avoir_Title"), _locale.T("Avoir_ErrDenied"), cancellationToken);
            return;
        }

        foreach (var l in Lignes) l.PropertyChanged -= LineChanged;
        AvoirId = null;
        FactureId = factureId;
        Lignes.Clear();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var f = await db.Factures.Include(x => x.Lignes).FirstAsync(x => x.Id == factureId, cancellationToken);
        ClientId = f.ClientId;
        Numero = _locale.T("Avoir_DraftPlaceholder");
        foreach (var l in f.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            var row = new AvoirLineRow
            {
                ProduitId = l.ProduitId,
                Reference = prod?.Reference ?? string.Empty,
                Designation = l.Designation,
                Conditionnement = l.Conditionnement,
                Quantite = Math.Min(l.Quantite, 1),
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
        Title = _locale.T("Avoir_NewTitle");
    }

    public void LoadExisting(int avoirId) => _ = LoadExistingAsync(avoirId, CancellationToken.None);

    private async Task LoadExistingAsync(int avoirId, CancellationToken cancellationToken)
    {
        if (!_session.CanAccessAvoir)
        {
            await _dialog.ShowErrorAsync(_locale.T("Avoir_Title"), _locale.T("Avoir_ErrDenied"), cancellationToken);
            return;
        }

        foreach (var l in Lignes) l.PropertyChanged -= LineChanged;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var avoir = await db.Avoirs.Include(x => x.Lignes).FirstAsync(x => x.Id == avoirId, cancellationToken);
        AvoirId = avoir.Id;
        FactureId = avoir.FactureId;
        ClientId = avoir.ClientId;
        Numero = avoir.Numero;
        Date = new DateTimeOffset(avoir.Date);
        Motif = avoir.Motif;
        RetourMarchandise = avoir.RetourMarchandise;
        Lignes.Clear();
        foreach (var l in avoir.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            var row = new AvoirLineRow
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
        Title = _locale.Tf("Avoir_TitleNum", Numero);
    }

    [RelayCommand]
    private void AddLine()
    {
        var p = Produits.FirstOrDefault();
        var row = new AvoirLineRow();
        if (p != null)
            row.ApplyCatalogProduct(p);
        row.PropertyChanged += LineChanged;
        Lignes.Add(row);
        RefreshTotals();
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!_session.CanAccessAvoir) return;
        if (!Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("Avoir_Title"), _locale.T("Avoir_ErrLines"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(TotalTtc))
        {
            await _dialog.ShowErrorAsync(_locale.T("Avoir_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            Avoir entity;
            if (AvoirId == null)
            {
                var num = await _numbers.NextAvoirAsync(cancellationToken);
                entity = new Avoir
                {
                    Numero = num,
                    FactureId = FactureId,
                    ClientId = ClientId,
                    Date = Date.DateTime,
                    Motif = Motif,
                    RetourMarchandise = RetourMarchandise,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new AvoirLigne
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

                db.Avoirs.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                AvoirId = entity.Id;
                Numero = entity.Numero;
                Title = _locale.Tf("Avoir_TitleNum", Numero);
            }
            else
            {
                entity = await db.Avoirs.Include(a => a.Lignes).FirstAsync(a => a.Id == AvoirId, cancellationToken);
                entity.FactureId = FactureId;
                entity.ClientId = ClientId;
                entity.Date = Date.DateTime;
                entity.Motif = Motif;
                entity.RetourMarchandise = RetourMarchandise;
                db.AvoirLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new AvoirLigne
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

            await _stock.SyncAvoirStockAsync(
                db,
                entity.Id,
                entity.Numero,
                RetourMarchandise,
                Lignes.Select(l => (l.ProduitId, l.Quantite)),
                _session.UserId,
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            CanEditDraft = false;
            await _dialog.ShowInfoAsync(_locale.T("Avoir_Title"), _locale.T("Avoir_Saved"), cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (AvoirId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildAvoirPdfBytesAsync(cancellationToken);
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
        if (AvoirId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildAvoirPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildAvoirPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (AvoirId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var a = await db.Avoirs.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        var client = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == a.ClientId, cancellationToken);
        return await _pdf.BuildAvoirPdfAsync(a, DocumentPartyPdfInfo.FromTiers(client), cancellationToken);
    }

    [RelayCommand]
    private void Back()
    {
        var vm = _sp.GetRequiredService<AvoirListViewModel>();
        _workspace.Open(vm);
    }

    private bool CanRemoveAvoir() => AvoirId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveAvoir))]
    private async Task RemoveAvoirAsync(CancellationToken cancellationToken)
    {
        if (AvoirId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("Avoir_Title"), _locale.Tf("Avoir_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var tracked = await db.Avoirs.Include(a => a.Lignes).FirstAsync(a => a.Id == id, cancellationToken);
            await _stock.SyncAvoirStockAsync(db, id, tracked.Numero, false, [], null, cancellationToken);
            db.Avoirs.Remove(tracked);
            await db.SaveChangesAsync(cancellationToken);
            await _dialog.ShowInfoAsync(_locale.T("Avoir_Title"), _locale.T("Avoir_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Avoir_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
