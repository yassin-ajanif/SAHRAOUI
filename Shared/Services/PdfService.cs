using GestionCommerciale.Modules.AvoirFournisseur.Models;
using GestionCommerciale.Modules.CommandeFournisseur.Models;
using GestionCommerciale.Modules.CommandeClient.Models;
using GestionCommerciale.Modules.Devis.Models;
using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.Facturation.Services;
using GestionCommerciale.Modules.FactureFournisseur.Models;
using GestionCommerciale.Modules.Livraison;
using GestionCommerciale.Modules.Livraison.Models;
using GestionCommerciale.Modules.Preparation.Models;
using GestionCommerciale.Modules.Reception.Models;
using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Models.Pdf;
using GestionCommerciale.Shared.Services.Pdf;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GestionCommerciale.Shared.Services;

public sealed class PdfService : IPdfService
{
    private readonly IAppSettingsService _settings;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IUiPreferencesService _uiPreferences;

    public PdfService(
        IAppSettingsService settings,
        IDbContextFactory<AppDbContext> dbFactory,
        IUiPreferencesService uiPreferences)
    {
        _settings = settings;
        _dbFactory = dbFactory;
        _uiPreferences = uiPreferences;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly CultureInfo PdfCulture = CultureInfo.GetCultureInfo("fr-FR");

    private static string FmtQty(decimal value) => value.ToString("#,##0.##", PdfCulture);

    private static string FmtUnitPrice(decimal value) => value.ToString("N2", PdfCulture);

    private static string FmtTvaPct(decimal value) => value.ToString("#,##0.##", PdfCulture);

    private static string FmtMoney(decimal value) => value.ToString("N2", PdfCulture);

    public async Task<byte[]> BuildDevisPdfAsync(Devis devis, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(devis.Lignes.Select(l => l.ProduitId), cancellationToken);
        var totals = DocumentTotalsHelper.DevisTotals(devis.Lignes, devis.RemiseGlobale);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("devis");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in devis.Lignes)
        {
            var ht = DocumentTotalsHelper.LigneHT(l.Quantite, l.PrixUnitaireHT, l.Remise);
            var ttc = ht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.Quantite),
                l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(ht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", devis.Numero),
            new("Date", devis.Date.ToString("dd/MM/yyyy")),
            new("Valable jusqu'au", devis.DateValidite.ToString("dd/MM/yyyy"))
        };
        if (devis.RemiseGlobale > 0)
            docLines.Add(new("Remise globale", $"{devis.RemiseGlobale:N2} %"));

        var model = BaseModel(cfg, "DEVIS", docLines, PartyLines(party, "Client"), cols, rows, totals, devis.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildBonLivraisonPdfAsync(BonLivraison bl, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(bl.Lignes.Select(l => l.ProduitId), cancellationToken);
        var blVis = _uiPreferences.GetDocumentLineColumnVisibility("bon_livraison");
        var totals = DocumentTotalsHelper.BonLivraisonTotals(bl.Lignes);
        var lineData = new List<StandardPdfLine>();
        foreach (var l in bl.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.QuantiteLivree, l.PrixUnitaireHT, l.Remise);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.QuantiteLivree),
                UniteCell(meta, l.ProduitId),
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(blVis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", bl.Numero),
            new("Date", bl.Date.ToString("dd/MM/yyyy"))
        };

        var bccRef = await ResolveBonCommandeReferenceForBlPdfAsync(bl, cancellationToken);
        if (!string.IsNullOrWhiteSpace(bccRef))
            docLines.Add(new("BC", bccRef));

        var model = BaseModel(cfg, "BON DE LIVRAISON", docLines, PartyLines(party, "Client"), cols, rows, totals, bl.Note, blVis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildBonReceptionPdfAsync(BonReception br, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(br.Lignes.Select(l => l.ProduitId), cancellationToken);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("bon_reception");
        var totals = DocumentTotalsHelper.BonReceptionTotals(br.Lignes);
        var lineData = new List<StandardPdfLine>();
        foreach (var l in br.Lignes)
        {
            var lht = l.QuantiteRecue * l.PrixUnitaireHT;
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.QuantiteRecue),
                UniteCell(meta, l.ProduitId),
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(0),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: false, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", br.Numero),
            new("Date", br.Date.ToString("dd/MM/yyyy"))
        };

        var model = BaseModel(cfg, "BON DE RÉCEPTION", docLines, PartyLines(party, "Fournisseur"), cols, rows, totals, br.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildBonCommandePdfAsync(BonCommande bc, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(bc.Lignes.Select(l => l.ProduitId), cancellationToken);
        decimal ht = 0, tva = 0;
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("bon_commande");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in bc.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.QuantiteCommandee, l.PrixUnitaireHT, l.Remise);
            ht += lht;
            tva += lht * (l.TauxTVA / 100m);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.QuantiteCommandee),
                l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", bc.Numero),
            new("Date", bc.Date.ToString("dd/MM/yyyy"))
        };

        var model = BaseModel(cfg, "BON DE COMMANDE", docLines, PartyLines(party, "Fournisseur"), cols, rows, (ht, tva, ht + tva), bc.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildBonCommandeClientPdfAsync(BonCommandeClient bc, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(bc.Lignes.Select(l => l.ProduitId), cancellationToken);
        decimal ht = 0, tva = 0;
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("bon_commande_client");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in bc.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.QuantiteCommandee, l.PrixUnitaireHT, l.Remise);
            ht += lht;
            tva += lht * (l.TauxTVA / 100m);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.QuantiteCommandee),
                l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", bc.Numero),
            new("Date", bc.Date.ToString("dd/MM/yyyy"))
        };

        var model = BaseModel(cfg, "BON DE COMMANDE", docLines, PartyLines(party, "Client"), cols, rows, (ht, tva, ht + tva), bc.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildFacturePdfAsync(Facture facture, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(facture.Lignes.Select(l => l.ProduitId), cancellationToken);
        var totals = DocumentTotalsHelper.FactureTotals(facture.Lignes, facture.RemiseGlobale);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("facture");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in facture.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.Quantite, l.PrixUnitaireHT, l.Remise);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.Quantite),
                l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", facture.Numero),
            new("Date", facture.Date.ToString("dd/MM/yyyy")),
            new("Échéance", facture.DateEcheance.ToString("dd/MM/yyyy"))
        };

        var blNums = await GetLinkedBlNumerosAsync(facture.Id, cancellationToken);
        if (blNums.Count > 0)
            docLines.Add(new("BL", string.Join(", ", blNums)));

        var bccRef = await ResolveBonCommandeReferenceForPdfAsync(facture, cancellationToken);
        if (!string.IsNullOrWhiteSpace(bccRef))
            docLines.Add(new("BC", bccRef));

        // var pay = SummarizePaiements(facture.Paiements);
        // if (!string.IsNullOrWhiteSpace(pay))
        //     docLines.Add(new("Payé par", pay!));
        if (facture.RemiseGlobale > 0)
            docLines.Add(new("Remise globale", $"{facture.RemiseGlobale:N2} %"));

        var model = BaseModel(cfg, "FACTURE", docLines, PartyLines(party, "Client"), cols, rows, totals, facture.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildBonPreparationPdfAsync(BonPreparation doc, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(doc.Lignes.Select(l => l.ProduitId), cancellationToken);
        var totals = DocumentTotalsHelper.BonPreparationTotals(doc.Lignes, doc.RemiseGlobale);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("bon_preparation");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in doc.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.Quantite, l.PrixUnitaireHT, l.Remise);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.Quantite),
                l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", doc.Numero),
            new("Date", doc.Date.ToString("dd/MM/yyyy")),
            new("Échéance", doc.DateEcheance.ToString("dd/MM/yyyy"))
        };

        if (doc.RemiseGlobale > 0)
            docLines.Add(new("Remise globale", $"{doc.RemiseGlobale:N2} %"));

        var model = BaseModel(cfg, "BON DE PRÉPARATION", docLines, PartyLines(party, "Client"), cols, rows, totals, doc.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildFactureFournisseurPdfAsync(FactureFournisseur factureFournisseur, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(factureFournisseur.Lignes.Select(l => l.ProduitId), cancellationToken);
        var totals = DocumentTotalsHelper.FactureFournisseurTotals(factureFournisseur.Lignes, factureFournisseur.RemiseGlobale);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("facture_fournisseur");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in factureFournisseur.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.Quantite, l.PrixUnitaireHT, l.Remise);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.Quantite),
                l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", factureFournisseur.Numero),
            new("Date", factureFournisseur.Date.ToString("dd/MM/yyyy")),
            new("Échéance", factureFournisseur.DateEcheance.ToString("dd/MM/yyyy"))
        };

        var brNums = await GetLinkedBrNumerosAsync(factureFournisseur.Id, cancellationToken);
        if (brNums.Count > 0)
            docLines.Add(new("BR", string.Join(", ", brNums)));

        // var pay = SummarizePaiementsFournisseur(factureFournisseur.Paiements);
        // if (!string.IsNullOrWhiteSpace(pay))
        //     docLines.Add(new("Payé par", pay!));
        if (factureFournisseur.RemiseGlobale > 0)
            docLines.Add(new("Remise globale", $"{factureFournisseur.RemiseGlobale:N2} %"));

        var model = BaseModel(cfg, "FACTURE FOURNISSEUR", docLines, PartyLines(party, "Fournisseur"), cols, rows, totals, factureFournisseur.Note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    private async Task<List<string>> GetLinkedBrNumerosAsync(int factureFournisseurId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.BonsReception.AsNoTracking()
            .Where(b => b.FactureFournisseurId == factureFournisseurId)
            .OrderBy(b => b.Date).ThenBy(b => b.Numero)
            .Select(b => b.Numero)
            .ToListAsync(cancellationToken);
    }

    private static string? SummarizePaiementsFournisseur(IEnumerable<PaiementFournisseur> paiements)
    {
        var list = paiements.OrderBy(p => p.Date).ToList();
        if (list.Count == 0) return null;
        return string.Join(", ", list.Select(p => $"{p.Montant:N2} ({p.Date:dd/MM/yyyy})"));
    }

    private async Task<List<string>> GetLinkedBlNumerosAsync(int factureId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.BonsLivraison.AsNoTracking()
            .Where(b => b.FactureId == factureId)
            .OrderBy(b => b.Date).ThenBy(b => b.Numero)
            .Select(b => b.Numero)
            .ToListAsync(cancellationToken);
    }

    private async Task<string?> ResolveBonCommandeReferenceForPdfAsync(Facture facture, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(facture.BonCommandeReference))
            return facture.BonCommandeReference.Trim();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var linkedNums = await db.BonsCommandeClient.AsNoTracking()
            .Where(b => b.FactureId == facture.Id)
            .OrderBy(b => b.Date).ThenBy(b => b.Numero)
            .Select(b => b.Numero)
            .ToListAsync(cancellationToken);

        return linkedNums.Count == 0 ? null : string.Join(", ", linkedNums);
    }

    private async Task<string?> ResolveBonCommandeReferenceForBlPdfAsync(BonLivraison bl, CancellationToken cancellationToken)
    {
        var fromNote = BonCommandeReferenceStorage.ResolveForPdf(bl.Note);
        if (!string.IsNullOrWhiteSpace(fromNote))
            return fromNote;

        if (bl.BonCommandeClientId is not int bccId)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.BonsCommandeClient.AsNoTracking()
            .Where(b => b.Id == bccId)
            .Select(b => b.Numero)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<byte[]> BuildAvoirPdfAsync(Avoir avoir, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(avoir.Lignes.Select(l => l.ProduitId), cancellationToken);
        var totals = DocumentTotalsHelper.AvoirTotals(avoir.Lignes);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("avoir");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in avoir.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.Quantite, l.PrixUnitaireHT, l.Remise);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.Quantite),
                string.IsNullOrWhiteSpace(l.Conditionnement) ? UniteCell(meta, l.ProduitId) : l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var note = $"{avoir.Motif}\nRetour marchandise : {(avoir.RetourMarchandise ? "Oui" : "Non")}";
        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", avoir.Numero),
            new("Date", avoir.Date.ToString("dd/MM/yyyy"))
        };

        var model = BaseModel(cfg, "AVOIR", docLines, PartyLines(party, "Client"), cols, rows, totals, note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildAvoirFournisseurPdfAsync(AvoirFournisseur doc, DocumentPartyPdfInfo party, CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var meta = await LoadProductMetaAsync(doc.Lignes.Select(l => l.ProduitId), cancellationToken);
        var totals = DocumentTotalsHelper.AvoirFournisseurTotals(doc.Lignes);
        var vis = _uiPreferences.GetDocumentLineColumnVisibility("avoirFournisseur");
        var lineData = new List<StandardPdfLine>();
        foreach (var l in doc.Lignes)
        {
            var lht = DocumentTotalsHelper.LigneHT(l.Quantite, l.PrixUnitaireHT, l.Remise);
            var ttc = lht * (1 + l.TauxTVA / 100m);
            lineData.Add(new StandardPdfLine(
                RefCell(meta, l.ProduitId),
                l.Designation,
                FmtQty(l.Quantite),
                string.IsNullOrWhiteSpace(l.Conditionnement) ? UniteCell(meta, l.ProduitId) : l.Conditionnement,
                FmtUnitPrice(l.PrixUnitaireHT),
                FmtTvaPct(l.TauxTVA),
                FmtMoney(l.Remise),
                FmtMoney(lht),
                FmtMoney(ttc)));
        }

        var (cols, rows) = BuildStandardPdfTable(vis, supportsLineRemise: true, "Qté", lineData);

        var note = $"{doc.Motif}\nRetour marchandise : {(doc.RetourMarchandise ? "Oui" : "Non")}";
        var docLines = new List<PdfKeyValueLine>
        {
            new("N°", doc.Numero),
            new("Date", doc.Date.ToString("dd/MM/yyyy"))
        };

        var model = BaseModel(cfg, "AVOIR FOURNISSEUR", docLines, PartyLines(party, "Fournisseur"), cols, rows, totals, note, vis.ShowMontantTtc);
        return CommercialDocumentPdfRenderer.Render(model, TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildClientAccountStatementPdfAsync(
        Tiers client,
        ClientAccountStatementResult statement,
        DocumentPartyPdfInfo party,
        CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var devise = string.IsNullOrWhiteSpace(cfg.Devise) ? "MAD" : cfg.Devise.Trim();
        return ClientAccountStatementPdfRenderer.Render(
            cfg.SocieteNom,
            devise,
            client,
            party,
            statement,
            TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    public async Task<byte[]> BuildSupplierAccountStatementPdfAsync(
        Tiers fournisseur,
        ClientAccountStatementResult statement,
        DocumentPartyPdfInfo party,
        CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetAsync(cancellationToken);
        var devise = string.IsNullOrWhiteSpace(cfg.Devise) ? "MAD" : cfg.Devise.Trim();
        return SupplierAccountStatementPdfRenderer.Render(
            cfg.SocieteNom,
            devise,
            fournisseur,
            party,
            statement,
            TryLoadLogoBytes(cfg.SocieteLogoPath));
    }

    private static CommercialDocumentPdfModel BaseModel(
        AppSettingsRow cfg,
        string kind,
        IReadOnlyList<PdfKeyValueLine> docLines,
        IReadOnlyList<PdfKeyValueLine> partyLines,
        IReadOnlyList<PdfTableColumn> columns,
        List<IReadOnlyList<string>> rows,
        (decimal ht, decimal tva, decimal ttc) totals,
        string? note,
        bool showTaxAndTtcInTotalsBox = true)
    {
        var qtyCol = FindQtyColumnIndex(columns);
        var refCol = FindRefColumnIndex(columns);
        decimal sumQty = 0;
        var refCount = 0;
        var qtyParse = CultureInfo.GetCultureInfo("fr-FR");
        foreach (var r in rows)
        {
            if (qtyCol >= 0 && qtyCol < r.Count && decimal.TryParse(r[qtyCol], NumberStyles.Any, qtyParse, out var q))
                sumQty += q;
            if (refCol >= 0 && refCol < r.Count && !string.IsNullOrWhiteSpace(r[refCol]) && r[refCol] != "—")
                refCount++;
        }

        if (refCount == 0)
            refCount = rows.Count;

        int leadingSpan;
        string[] summaryValues;
        if (qtyCol > 0 && qtyCol < columns.Count)
        {
            leadingSpan = qtyCol;
            summaryValues = new string[columns.Count - qtyCol];
            for (var i = 0; i < summaryValues.Length; i++)
                summaryValues[i] = i == 0 ? FmtQty(sumQty) : "";
        }
        else
        {
            leadingSpan = columns.Count;
            summaryValues = [];
        }

        var currencyWord = cfg.Devise.ToUpperInvariant() switch
        {
            "MAD" => "dirhams",
            "EUR" => "euros",
            "USD" => "dollars",
            _ => cfg.Devise
        };
        var amountForWords = showTaxAndTtcInTotalsBox ? totals.ttc : totals.ht;
        var amountWords = cfg.UiLanguage.Equals("ar", StringComparison.OrdinalIgnoreCase)
            ? MoneyFrenchWords.FormatArabicFallback(amountForWords, cfg.Devise)
            : MoneyFrenchWords.Format(amountForWords, currencyWord);

        return new CommercialDocumentPdfModel
        {
            CompanyName = cfg.SocieteNom,
            DocumentKindLabel = kind,
            DocumentInfoLines = docLines,
            PartyInfoLines = partyLines,
            Columns = columns,
            Rows = rows,
            SummaryRow = rows.Count > 0
                ? new PdfTableSummaryRow
                {
                    LeadingSpan = leadingSpan,
                    Label = $"Total : {refCount} référence(s)",
                    Values = summaryValues
                }
                : null,
            TotalHt = totals.ht,
            TotalTva = totals.tva,
            TotalTtc = totals.ttc,
            Devise = cfg.Devise,
            AmountInWords = amountWords,
            Note = note,
            FooterLines = BuildFooterLines(cfg),
            ShowTaxAndTtcInTotalsBox = showTaxAndTtcInTotalsBox
        };
    }

    private static int FindQtyColumnIndex(IReadOnlyList<PdfTableColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Header.Contains("livr", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var h = columns[i].Header.ToLowerInvariant();
            if (h.Contains("qté") || h.Contains("qte"))
                return i;
        }

        return -1;
    }

    private static int FindRefColumnIndex(IReadOnlyList<PdfTableColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var h = columns[i].Header.Trim();
            if (h.StartsWith("Réf", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private (List<PdfTableColumn> Columns, List<IReadOnlyList<string>> Rows) BuildStandardPdfTable(
        DocumentLineColumnVisibility visibility,
        bool supportsLineRemise,
        string qtyHeader,
        IReadOnlyList<StandardPdfLine> lines)
    {
        var v = supportsLineRemise ? visibility : visibility with { ShowRemise = false };
        var columns = BuildStandardColumnList(v, qtyHeader);
        if (columns.Count == 0)
            return BuildStandardPdfTable(DocumentLineColumnVisibility.AllVisible, supportsLineRemise, qtyHeader, lines);

        var rows = new List<IReadOnlyList<string>>(lines.Count);
        foreach (var line in lines)
            rows.Add(BuildStandardDataRow(v, line));

        return (columns, rows);
    }

    private static List<PdfTableColumn> BuildStandardColumnList(DocumentLineColumnVisibility v, string qtyHeader)
    {
        var columns = new List<PdfTableColumn>();
        if (v.ShowReference)
            columns.Add(new PdfTableColumn("Référence", 0.7f, PdfTextAlignment.Start));
        if (v.ShowDesignation)
            columns.Add(new PdfTableColumn("Désignation", 2.5f, PdfTextAlignment.Start));
        if (v.ShowQuantite)
            columns.Add(new PdfTableColumn(qtyHeader, 0.35f, PdfTextAlignment.Center));
        if (v.ShowConditionnement)
            columns.Add(new PdfTableColumn("Ute", 0.25f, PdfTextAlignment.Center));
        if (v.ShowPuHt)
            columns.Add(new PdfTableColumn("PU HT", 0.55f, PdfTextAlignment.Center));
        // TVA column hidden on PDF output (requested).
        // if (v.ShowTva)
        //     columns.Add(new PdfTableColumn("Tva", 0.25f, PdfTextAlignment.Center));
        if (v.ShowRemise)
            columns.Add(new PdfTableColumn("Rem. %", 0.35f, PdfTextAlignment.Center));
        if (v.ShowMontantHt)
            columns.Add(new PdfTableColumn("Mnt HT", 0.55f, PdfTextAlignment.Center));
        if (v.ShowMontantTtc)
            columns.Add(new PdfTableColumn("Mnt TTC", 0.55f, PdfTextAlignment.Center));
        return columns;
    }

    private static List<string> BuildStandardDataRow(DocumentLineColumnVisibility v, StandardPdfLine line)
    {
        var cells = new List<string>();
        if (v.ShowReference)
            cells.Add(line.Ref);
        if (v.ShowDesignation)
            cells.Add(line.Designation);
        if (v.ShowQuantite)
            cells.Add(line.Quantite);
        if (v.ShowConditionnement)
            cells.Add(line.Unite);
        if (v.ShowPuHt)
            cells.Add(line.PuHt);
        // TVA column hidden on PDF output (requested).
        // if (v.ShowTva)
        //     cells.Add(line.Tva);
        if (v.ShowRemise)
            cells.Add(line.Remise);
        if (v.ShowMontantHt)
            cells.Add(line.MntHt);
        if (v.ShowMontantTtc)
            cells.Add(line.MntTtc);
        return cells;
    }

    private readonly record struct StandardPdfLine(
        string Ref,
        string Designation,
        string Quantite,
        string Unite,
        string PuHt,
        string Tva,
        string Remise,
        string MntHt,
        string MntTtc);

    private const string EmptyPartyFieldPlaceholder = "—";

    private static List<PdfKeyValueLine> PartyLines(DocumentPartyPdfInfo p, string roleLabel)
    {
        var list = new List<PdfKeyValueLine> { new(roleLabel, p.Nom, EmphasizeValue: true) };
        if (!string.IsNullOrWhiteSpace(p.Ice))
            list.Add(new("ICE", p.Ice));
        if (!string.IsNullOrWhiteSpace(p.Adresse))
            list.Add(new("Adresse", p.Adresse));
        list.Add(new("Téléphone", string.IsNullOrWhiteSpace(p.Telephone) ? EmptyPartyFieldPlaceholder : p.Telephone));
        list.Add(new("Email", string.IsNullOrWhiteSpace(p.Email) ? EmptyPartyFieldPlaceholder : p.Email));
        return list;
    }

    private sealed record ProductPdfMeta(string Ref, string Unite);

    private static string RefCell(Dictionary<int, ProductPdfMeta> meta, int produitId) =>
        produitId > 0 && meta.TryGetValue(produitId, out var m) && !string.IsNullOrWhiteSpace(m.Ref) ? m.Ref : "—";

    private static string UniteCell(Dictionary<int, ProductPdfMeta> meta, int produitId) =>
        produitId > 0 && meta.TryGetValue(produitId, out var m) ? m.Unite : string.Empty;

    private static string ConditionnementCell(string? conditionnement, Dictionary<int, ProductPdfMeta> meta, int produitId) =>
        string.IsNullOrWhiteSpace(conditionnement) ? UniteCell(meta, produitId) : conditionnement.Trim();

    private static string? SummarizePaiements(IReadOnlyList<Paiement>? paiements)
    {
        if (paiements == null || paiements.Count == 0) return null;
        var total = paiements.Sum(p => p.Montant);
        var modes = string.Join(", ", paiements.Select(p => ModeFr(p.Mode)).Distinct());
        return $"{total:N2} — {modes}";
    }

    private static string ModeFr(ModePaiement m) => m switch
    {
        ModePaiement.Credit => "Crédit",
        ModePaiement.Cheque => "Chèque",
        ModePaiement.Especes => "Espèces",
        ModePaiement.TPE => "TPE",
        ModePaiement.Virement => "Virement",
        ModePaiement.Effet => "Effet",
        _ => m.ToString()
    };

    private async Task<Dictionary<int, ProductPdfMeta>> LoadProductMetaAsync(IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, ProductPdfMeta>();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Produits.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new ProductPdfMeta(p.Reference ?? "", p.Unite ?? ""), cancellationToken);
    }

    private static IReadOnlyList<string> BuildFooterLines(AppSettingsRow cfg)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.SocieteAdresse))
            lines.Add(cfg.SocieteAdresse.Trim());
        if (!string.IsNullOrWhiteSpace(cfg.SocieteICE))
            lines.Add($"ICE : {cfg.SocieteICE.Trim()}");

        if (!string.IsNullOrWhiteSpace(cfg.SocieteMentionsLegales))
        {
            foreach (var part in cfg.SocieteMentionsLegales.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                lines.Add(part);
        }

        return lines;
    }

    private static byte[]? TryLoadLogoBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }
}
