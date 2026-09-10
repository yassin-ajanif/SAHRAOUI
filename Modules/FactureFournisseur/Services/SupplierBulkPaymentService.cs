using FactureFournisseurEntity = GestionCommerciale.Modules.FactureFournisseur.Models.FactureFournisseur;
using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.FactureFournisseur.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace GestionCommerciale.Modules.FactureFournisseur.Services;

public sealed class SupplierBulkPaymentService : ISupplierBulkPaymentService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SupplierBulkPaymentService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<IReadOnlyList<SupplierBulkPayableDocument>> GetOpenDocumentsAsync(int fournisseurId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await LoadOpenDocumentsAsync(db, fournisseurId, track: false, cancellationToken);
    }

    public async Task<SupplierBulkPaymentPreview> PreviewAsync(int fournisseurId, decimal amount, CancellationToken cancellationToken = default)
    {
        var docs = await GetOpenDocumentsAsync(fournisseurId, cancellationToken);
        return Allocate(docs, amount);
    }

    public async Task ApplyAsync(SupplierBulkPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FournisseurId <= 0)
            throw new InvalidOperationException("Fournisseur invalide.");
        if (request.Amount <= 0)
            throw new InvalidOperationException("Le montant doit être supérieur à 0.");
        if (request.Mode == ModePaiement.Credit)
            throw new InvalidOperationException("Le mode Crédit n'est pas autorisé pour un règlement groupé.");

        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var reference = (request.Reference ?? string.Empty).Trim();
        var date = request.Date.Date;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var open = await LoadOpenDocumentsAsync(db, request.FournisseurId, track: true, cancellationToken);
            var preview = Allocate(open, amount);

            foreach (var line in preview.Lines)
            {
                var facture = await db.FacturesFournisseurs
                    .Include(f => f.Paiements)
                    .Include(f => f.Lignes)
                    .FirstAsync(f => f.Id == line.DocumentId && f.FournisseurId == request.FournisseurId, cancellationToken);

                DocumentTotalsHelper.SyncFactureFournisseurTotalTtc(facture);
                var paidBefore = facture.Paiements.Sum(p => p.Montant);
                var totalAfter = paidBefore + line.Amount;
                DocumentTotalsHelper.EnsurePaymentsNotOverTtc(facture.TotalTtc, totalAfter);

                db.PaiementsFournisseurs.Add(new PaiementFournisseur
                {
                    FactureFournisseurId = facture.Id,
                    Montant = line.Amount,
                    Date = date,
                    Mode = request.Mode,
                    Reference = reference
                });

                if (IsFullyPaid(facture.TotalTtc, totalAfter))
                    facture.EstPayee = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<IReadOnlyList<SupplierBulkPayableDocument>> LoadOpenDocumentsAsync(
        AppDbContext db,
        int fournisseurId,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<FactureFournisseurEntity> facturesQ = db.FacturesFournisseurs
            .Include(f => f.Paiements)
            .Include(f => f.Lignes)
            .Where(f => f.FournisseurId == fournisseurId);

        if (!track)
            facturesQ = facturesQ.AsNoTracking();

        var factures = await facturesQ.ToListAsync(cancellationToken);
        var result = new List<SupplierBulkPayableDocument>();

        foreach (var f in factures)
        {
            DocumentTotalsHelper.SyncFactureFournisseurTotalTtc(f);
            var paid = f.Paiements.Sum(p => p.Montant);
            var remaining = Math.Round(f.TotalTtc - paid, 2, MidpointRounding.AwayFromZero);
            if (remaining <= DocumentTotalsHelper.PaiementTtcTolerance)
                continue;

            result.Add(new SupplierBulkPayableDocument(
                SupplierBulkPayableDocumentKind.FactureFournisseur,
                f.Id,
                f.Numero,
                f.Date.Date,
                f.TotalTtc,
                paid,
                remaining));
        }

        return result
            .OrderBy(d => d.Date)
            .ThenBy(d => d.DocumentId)
            .ToList();
    }

    internal static SupplierBulkPaymentPreview Allocate(IReadOnlyList<SupplierBulkPayableDocument> openDocuments, decimal amount)
    {
        var requested = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (requested <= 0)
            throw new InvalidOperationException("Le montant doit être supérieur à 0.");

        var ordered = openDocuments
            .Where(d => d.Remaining > DocumentTotalsHelper.PaiementTtcTolerance)
            .OrderBy(d => d.Date)
            .ThenBy(d => d.DocumentId)
            .ToList();

        var totalRemaining = Math.Round(ordered.Sum(d => d.Remaining), 2, MidpointRounding.AwayFromZero);
        if (requested > totalRemaining + DocumentTotalsHelper.PaiementTtcTolerance)
        {
            throw new InvalidOperationException(
                $"Le montant ({requested:N2}) dépasse le reste à régler ({totalRemaining:N2}).");
        }

        var lines = new List<SupplierBulkPaymentAllocationLine>();
        var left = requested;

        foreach (var doc in ordered)
        {
            if (left <= DocumentTotalsHelper.PaiementTtcTolerance)
                break;

            var apply = Math.Round(Math.Min(doc.Remaining, left), 2, MidpointRounding.AwayFromZero);
            if (apply <= 0)
                continue;

            var remainingAfter = Math.Round(doc.Remaining - apply, 2, MidpointRounding.AwayFromZero);
            if (remainingAfter < 0)
                remainingAfter = 0;

            lines.Add(new SupplierBulkPaymentAllocationLine(
                doc.Kind,
                doc.DocumentId,
                doc.Numero,
                doc.Date,
                apply,
                remainingAfter,
                IsFullyPaid(doc.TotalTtc, doc.AlreadyPaid + apply)));

            left = Math.Round(left - apply, 2, MidpointRounding.AwayFromZero);
        }

        if (lines.Count == 0)
            throw new InvalidOperationException("Aucun document à régler pour ce fournisseur.");

        return new SupplierBulkPaymentPreview(requested, totalRemaining, lines);
    }

    private static bool IsFullyPaid(decimal ttc, decimal totalPaid) =>
        totalPaid + DocumentTotalsHelper.PaiementTtcTolerance >= ttc;
}
