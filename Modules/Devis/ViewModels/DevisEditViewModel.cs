using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCommerciale.Modules.Auth.Services;
using GestionCommerciale.Modules.Stock;
using GestionCommerciale.Modules.Devis.Models;
using GestionCommerciale.Modules.Facturation.ViewModels;
using GestionCommerciale.Modules.Livraison.ViewModels;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services;
using GestionCommerciale.Shared.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommerciale.Modules.Devis.ViewModels;

public partial class DevisEditViewModel : BaseViewModel
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberService _numbers;
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly WorkspaceNavigator _workspace;
    private readonly IServiceProvider _sp;
    private readonly ICurrentUserSession _session;
    private readonly ILocaleService _locale;
    private readonly IUiPreferencesService _uiPreferences;
    private readonly IPdfService _pdf;
    private readonly IPdfPrintService _pdfPrint;
    private readonly ClientSoldeDisplay _clientSolde;

    public DevisEditViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberService numbers,
        IAppSettingsService settings,
        IDialogService dialog,
        WorkspaceNavigator workspaceNavigator,
        IServiceProvider sp,
        ICurrentUserSession session,
        ILocaleService locale,
        IUiPreferencesService uiPreferences,
        IPdfService pdf,
        IPdfPrintService pdfPrint,
        GestionCommerciale.Modules.Facturation.Services.IClientAccountStatementService clientLedger)
    {
        _dbFactory = dbFactory;
        _numbers = numbers;
        _settings = settings;
        _dialog = dialog;
        _workspace = workspaceNavigator;
        _sp = sp;
        _session = session;
        _locale = locale;
        _uiPreferences = uiPreferences;
        _pdf = pdf;
        _pdfPrint = pdfPrint;
        _clientSolde = new ClientSoldeDisplay(clientLedger, locale);
        WhatsApp = new WhatsAppOpenHelper(dialog, locale);
        _locale.CultureApplied += (_, _) =>
        {
            RefreshDevisUi();
            RefreshTotals();
            if (ClientId > 0)
                _ = ClientSolde.RefreshAsync(ClientId, Devise);
        };
        LineGridColumns.PropertyChanged += OnLineGridColumnsPropertyChanged;
        _uiPreferences.LoadDocumentLineColumns("devis", LineGridColumns);
        Title = _locale.T("Devis_Title");
        Lignes.CollectionChanged += LignesOnCollectionChanged;
        RefreshDevisUi();
        ClientLookup.BindSelection(() => ClientId, c => SelectedClient = c);
    }

    [ObservableProperty] private string _btnPdf = string.Empty;
    [ObservableProperty] private string _btnPrint = string.Empty;
    [ObservableProperty] private string _btnBack = string.Empty;
    [ObservableProperty] private string _btnSave = string.Empty;
    [ObservableProperty] private string _btnToBl = string.Empty;
    [ObservableProperty] private string _btnToFacture = string.Empty;
    [ObservableProperty] private string _menuDeleteDevis = string.Empty;
    [ObservableProperty] private string _lblExpired = string.Empty;
    [ObservableProperty] private string _lblClient = string.Empty;
    [ObservableProperty] private string _wmClientSearch = string.Empty;
    [ObservableProperty] private string _lblDateDevis = string.Empty;
    [ObservableProperty] private string _lblValableJusqu = string.Empty;
    [ObservableProperty] private string _wmNote = string.Empty;
    [ObservableProperty] private string _lblAddProduct = string.Empty;
    [ObservableProperty] private string _wmAddProduct = string.Empty;
    [ObservableProperty] private string _btnRemoveSelectedLine = string.Empty;
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

    public DocumentLineGridColumnState LineGridColumns { get; } = new();

    public AutoCompleteFilterPredicate<object?> ProduitAutocompleteFilter => ProductAutoComplete.ItemFilter;
    public AutoCompleteFilterPredicate<object?> PartyAutocompleteFilter => PartyAutoComplete.ItemFilter;

    private void RefreshDevisUi()
    {
        BtnPdf = _locale.T("Btn_Pdf");
        BtnPrint = _locale.T("Btn_Print");
        WhatsApp.RefreshLabels();
        BtnBack = _locale.T("Btn_Back");
        BtnSave = _locale.T("Btn_Save");
        BtnToBl = _locale.T("Btn_ToBL");
        BtnToFacture = _locale.T("Btn_ToFacture");
        MenuDeleteDevis = _locale.T("Devis_MenuDelete");
        LblExpired = _locale.T("Lbl_Expired");
        LblClient = _locale.T("Lbl_Client");
        WmClientSearch = _locale.T("Wm_SearchClient");
        LblDateDevis = _locale.T("Lbl_DateDevis");
        LblValableJusqu = _locale.T("Lbl_ValableJusqu");
        WmNote = _locale.T("Lbl_Note");
        LblAddProduct = _locale.T("Devis_LblAddProduct");
        WmAddProduct = _locale.T("Devis_WmSearchProduct");
        BtnRemoveSelectedLine = _locale.T("Btn_RemoveSelectedLine");
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
    }

    public ClientCategoryFilter ClientLookup { get; } = new();
    public ClientSoldeDisplay ClientSolde => _clientSolde;
    public WhatsAppOpenHelper WhatsApp { get; }
    public ObservableCollection<GestionCommerciale.Modules.Tiers.Models.Tiers> Clients => ClientLookup.Clients;
    public ObservableCollection<GestionCommerciale.Modules.Stock.Models.Produit> Produits { get; } = [];
    public ObservableCollection<DevisLineRow> Lignes { get; } = [];

    [ObservableProperty] private int? _devisId;
    [ObservableProperty] private int _clientId;
    [ObservableProperty] private GestionCommerciale.Modules.Tiers.Models.Tiers? _selectedClient;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private DateTimeOffset _date = new(DateTime.Today);
    [ObservableProperty] private DateTimeOffset _dateValidite = new(DateTime.Today.AddDays(30));
    [ObservableProperty] private decimal _remiseGlobale;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private decimal _totalHt;
    [ObservableProperty] private decimal _totalTva;
    [ObservableProperty] private decimal _totalTtc;
    [ObservableProperty] private string _totalHtLabel = "HT 0,00";
    [ObservableProperty] private string _totalTvaLabel = "TVA 0,00";
    [ObservableProperty] private string _totalTtcLabel = "TTC 0,00";
    [ObservableProperty] private string _devise = string.Empty;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _isExpire;
    [ObservableProperty] private DevisLineRow? _selectedLine;
    [ObservableProperty] private string _addLineSearchText = string.Empty;
    [ObservableProperty] private object? _addLineCatalogPick;

    private bool _suppressAddLinePick;

    public bool CanEditLines => !IsReadOnly;
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
        _uiPreferences.SaveDocumentLineColumns("devis", LineGridColumns);
    }

    private void LignesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (DevisLineRow row in e.NewItems)
                row.PropertyChanged += LineOnPropertyChanged;
        RefreshTotals();
    }

    private void LineOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevisLineRow.ProduitId) && sender is DevisLineRow row && row.ProduitId != 0)
        {
            HydrateLineProductFields(row);
            ConsolidateDuplicateProductLines();
        }
        RefreshTotals();
    }

    private void HydrateLineProductFields(DevisLineRow row)
    {
        if (row.ProduitId == 0) return;
        var prod = Produits.FirstOrDefault(p => p.Id == row.ProduitId);
        if (prod == null) return;

        if (string.IsNullOrWhiteSpace(row.Reference))
            row.Reference = prod.Reference;
        if (string.IsNullOrWhiteSpace(row.Designation))
            row.Designation = prod.Designation;
        if (string.IsNullOrWhiteSpace(row.Conditionnement))
            row.Conditionnement = prod.Unite;
    }

    /// <summary>Merges lines that share the same catalog product: quantities add, first line in order is kept.</summary>
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
                line.PropertyChanged -= LineOnPropertyChanged;
                Lignes.Remove(line);
            }

            keep.Quantite += extraQty;
        }
    }

    private void RefreshTotals()
    {
        var includeTvaInTotals = ShowTotalTtc;
        var lines = Lignes.Select(l => new DevisLigne
        {
            Quantite = l.Quantite,
            PrixUnitaireHT = l.PrixUnitaireHt,
            Remise = l.Remise,
            TauxTVA = includeTvaInTotals ? l.TauxTva : 0
        });
        var (ht, tva, ttc) = DocumentTotalsHelper.DevisTotals(lines, RemiseGlobale);
        TotalHt = ht;
        TotalTva = tva;
        TotalTtc = ttc;
        TotalHtLabel = FormatTotalLabel("Doc_FmtHt", ht);
        TotalTvaLabel = FormatTotalLabel("Doc_FmtTva", tva);
        TotalTtcLabel = FormatTotalLabel("Doc_FmtTtc", ttc);
    }

    private string FormatTotalLabel(string key, decimal amount) =>
        _locale.Tf(key, amount, Devise).TrimEnd();

    partial void OnDeviseChanged(string value)
    {
        RefreshTotals();
        if (ClientId > 0)
            _ = ClientSolde.RefreshAsync(ClientId, value);
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

    partial void OnDevisIdChanged(int? value) => RemoveDevisCommand.NotifyCanExecuteChanged();

    private bool CanRemoveDevis() => DevisId != null;

    [RelayCommand(CanExecute = nameof(CanRemoveDevis))]
    private async Task RemoveDevisAsync(CancellationToken cancellationToken)
    {
        if (DevisId is not { } id) return;

        if (!await _dialog.ConfirmAsync(_locale.T("Devis_Title"), _locale.Tf("Devis_ConfirmDelete", Numero), cancellationToken))
            return;

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var blockedMsg = await DevisDeleteReferencedMessage.BuildIfBlockedAsync(db, id, _locale, cancellationToken);
            if (blockedMsg != null)
            {
                await _dialog.ShowErrorAsync(_locale.T("Devis_Title"), blockedMsg, cancellationToken);
                return;
            }

            var entity = await db.Devis.Include(d => d.Lignes).FirstAsync(d => d.Id == id, cancellationToken);
            db.Devis.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            await _dialog.ShowInfoAsync(_locale.T("Devis_Title"), _locale.T("Devis_Deleted"), cancellationToken);
            Back();
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(_locale.T("Devis_Title"), ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnAddLineCatalogPickChanged(object? value)
    {
        if (_suppressAddLinePick || IsReadOnly) return;
        if (value is not GestionCommerciale.Modules.Stock.Models.Produit p) return;
        _suppressAddLinePick = true;
        const decimal addQty = 1;
        var existing = Lignes.FirstOrDefault(l => l.ProduitId == p.Id && p.Id != 0);
        if (existing != null)
        {
            existing.Quantite += addQty;
            SelectedLine = existing;
        }
        else
        {
            var row = new DevisLineRow
            {
                ProduitId = p.Id,
                Reference = p.Reference,
                Designation = p.Designation,
                Conditionnement = p.Unite,
                Quantite = addQty,
                PrixUnitaireHt = p.PrixVenteHT,
                Remise = 0,
                TauxTva = p.TauxTVA
            };
            row.PropertyChanged += LineOnPropertyChanged;
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
        DevisId = id;
        var cfg = await _settings.GetAsync(cancellationToken);
        Devise = CurrencyHelper.FromSettings(cfg);

        Lignes.Clear();
        SelectedLine = null;
        ResetAddProductSearch();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var clients = await db.Tiers.AsNoTracking()
            .Where(t => t.Actif && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .OrderBy(t => t.Nom).ToListAsync(cancellationToken);
        ClientLookup.ReplaceAll(clients);

        var produits = await db.Produits.AsNoTracking().Where(p => p.Actif)
            .SelectForListWithoutImageData().ToListAsync(cancellationToken);
        Produits.Clear();
        foreach (var p in produits) Produits.Add(p);

        if (id == null)
        {
            Date = new DateTimeOffset(DateTime.Today);
            DateValidite = new DateTimeOffset(DateTime.Today.AddDays(cfg.DevisValiditeJoursDefaut));
            Numero = "(brouillon)";
            ClientId = Clients.FirstOrDefault()?.Id ?? 0;
            IsReadOnly = false;
            IsExpire = false;
            Title = _locale.T("Devis_NewTitle");
            RefreshTotals();
            return;
        }

        var d = await db.Devis.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        Numero = d.Numero;
        ClientId = d.ClientId;
        Date = new DateTimeOffset(d.Date);
        DateValidite = new DateTimeOffset(d.DateValidite);
        RemiseGlobale = d.RemiseGlobale;
        Note = d.Note;
        foreach (var l in d.Lignes)
        {
            var prod = Produits.FirstOrDefault(p => p.Id == l.ProduitId);
            var row = new DevisLineRow
            {
                ProduitId = l.ProduitId,
                Reference = string.IsNullOrWhiteSpace(prod?.Reference) ? string.Empty : prod.Reference,
                Designation = string.IsNullOrWhiteSpace(l.Designation) ? (prod?.Designation ?? string.Empty) : l.Designation,
                Conditionnement = string.IsNullOrWhiteSpace(l.Conditionnement) ? (prod?.Unite ?? string.Empty) : l.Conditionnement,
                Quantite = l.Quantite,
                PrixUnitaireHt = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTva = l.TauxTVA
            };
            HydrateLineProductFields(row);
            row.PropertyChanged += LineOnPropertyChanged;
            Lignes.Add(row);
        }

        IsExpire = DateValidite.DateTime.Date < DateTime.Today;
        IsReadOnly = false;
        Title = _locale.Tf("Devis_TitleNum", Numero);
        RefreshTotals();
        ResetAddProductSearch();
    }

    public void Load(int? id) => _ = LoadAsync(id, CancellationToken.None);

    private void ResetAddProductSearch()
    {
        _suppressAddLinePick = true;
        AddLineCatalogPick = null;
        AddLineSearchText = string.Empty;
        _suppressAddLinePick = false;
    }

    [RelayCommand]
    private void RemoveLine(DevisLineRow? row)
    {
        if (IsReadOnly || row == null) return;
        row.PropertyChanged -= LineOnPropertyChanged;
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
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (IsReadOnly)
        {
            await _dialog.ShowErrorAsync(_locale.T("Devis_Title"), _locale.T("Devis_ErrNoEdit"), cancellationToken);
            return;
        }

        if (ClientId == 0 || !Lignes.Any())
        {
            await _dialog.ShowErrorAsync(_locale.T("Devis_Title"), _locale.T("Devis_ErrClientLine"), cancellationToken);
            return;
        }

        if (DocumentTotalsHelper.IsEffectivelyZeroTotal(TotalTtc))
        {
            await _dialog.ShowErrorAsync(_locale.T("Devis_Title"), _locale.T("Doc_ErrZeroTtc"), cancellationToken);
            return;
        }

        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            GestionCommerciale.Modules.Devis.Models.Devis entity;
            if (DevisId == null)
            {
                var num = await _numbers.NextDevisAsync(cancellationToken);
                entity = new GestionCommerciale.Modules.Devis.Models.Devis
                {
                    Numero = num,
                    ClientId = ClientId,
                    Date = Date.DateTime,
                    DateValidite = DateValidite.DateTime,
                    RemiseGlobale = RemiseGlobale,
                    Note = Note,
                    CreatedByUserId = _session.UserId
                };
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new DevisLigne
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

                db.Devis.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                DevisId = entity.Id;
            }
            else
            {
                entity = await db.Devis.Include(d => d.Lignes).FirstAsync(d => d.Id == DevisId, cancellationToken);
                entity.ClientId = ClientId;
                entity.Date = Date.DateTime;
                entity.DateValidite = DateValidite.DateTime;
                entity.RemiseGlobale = RemiseGlobale;
                entity.Note = Note;
                db.DevisLignes.RemoveRange(entity.Lignes);
                foreach (var l in Lignes)
                {
                    entity.Lignes.Add(new DevisLigne
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

                await db.SaveChangesAsync(cancellationToken);
            }
            Numero = entity.Numero;
            await _dialog.ShowInfoAsync(_locale.T("Devis_Title"), _locale.T("Devis_Saved"), cancellationToken);
            await LoadAsync(DevisId, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToBL()
    {
        if (DevisId == null) return;
        var vm = _sp.GetRequiredService<BLEditViewModel>();
        vm.LoadFromDevis(DevisId.Value);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void ToFacture()
    {
        if (DevisId == null) return;
        var vm = _sp.GetRequiredService<FactureEditViewModel>();
        vm.LoadFromDevis(DevisId.Value);
        _workspace.Open(vm);
    }

    [RelayCommand]
    private void Back()
    {
        var list = _sp.GetRequiredService<DevisListViewModel>();
        _workspace.Open(list);
        list.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task ExportPdfAsync(CancellationToken cancellationToken)
    {
        if (DevisId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildDevisPdfBytesAsync(cancellationToken);
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
        if (DevisId is not { }) return;
        try
        {
            IsBusy = true;
            var bytes = await BuildDevisPdfBytesAsync(cancellationToken);
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

    private async Task<byte[]?> BuildDevisPdfBytesAsync(CancellationToken cancellationToken)
    {
        if (DevisId is not { } id) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var d = await db.Devis.Include(x => x.Lignes).FirstAsync(x => x.Id == id, cancellationToken);
        var client = await db.Tiers.AsNoTracking().FirstAsync(t => t.Id == d.ClientId, cancellationToken);
        return await _pdf.BuildDevisPdfAsync(d, DocumentPartyPdfInfo.FromTiers(client), cancellationToken);
    }
}
