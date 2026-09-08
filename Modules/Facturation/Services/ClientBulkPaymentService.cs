using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Modules.Preparation.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace GestionCommerciale.Modules.Facturation.Services;

public sealed class ClientBulkPaymentService : IClientBulkPaymentService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClientBulkPaymentService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<IReadOnlyList<BulkPayableDocument>> GetOpenDocumentsAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await LoadOpenDocumentsAsync(db, clientId, track: false, cancellationToken);
    }

    public async Task<BulkPaymentPreview> PreviewAsync(int clientId, decimal amount, CancellationToken cancellationToken = default)
    {
        var docs = await GetOpenDocumentsAsync(clientId, cancellationToken);
        return Allocate(docs, amount);
    }

    public async Task ApplyAsync(ClientBulkPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ClientId <= 0)
            throw new InvalidOperationException("Client invalide.");
        if (request.Amount <= 0)
            throw new InvalidOperationException("Le montant doit être supérieur à 0.");
        if (request.Mode == ModePaiement.Credit)
            throw new InvalidOperationException("Le mode Crédit n'est pas autorisé pour un encaissement groupé.");

        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var reference = (request.Reference ?? string.Empty).Trim();
        var date = request.Date.Date;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var open = await LoadOpenDocumentsAsync(db, request.ClientId, track: true, cancellationToken);
            var preview = Allocate(open, amount);

            foreach (var line in preview.Lines)
            {
                if (line.Kind == BulkPayableDocumentKind.Facture)
                {
                    var facture = await db.Factures
                        .Include(f => f.Paiements)
                        .Include(f => f.Lignes)
                        .FirstAsync(f => f.Id == line.DocumentId && f.ClientId == request.ClientId, cancellationToken);

                    DocumentTotalsHelper.SyncFactureTotalTtc(facture);
                    var paidBefore = facture.Paiements.Sum(p => p.Montant);
                    var totalAfter = paidBefore + line.Amount;
                    DocumentTotalsHelper.EnsurePaymentsNotOverTtc(facture.TotalTtc, totalAfter);

                    db.Paiements.Add(new Paiement
                    {
                        FactureId = facture.Id,
                        Montant = line.Amount,
                        Date = date,
                        Mode = request.Mode,
                        Reference = reference
                    });

                    if (IsFullyPaid(facture.TotalTtc, totalAfter))
                        facture.EstPayee = true;
                }
                else
                {
                    var bp = await db.BonsPreparation
                        .Include(b => b.Paiements)
                        .Include(b => b.Lignes)
                        .FirstAsync(b => b.Id == line.DocumentId && b.ClientId == request.ClientId, cancellationToken);

                    DocumentTotalsHelper.SyncBonPreparationTotalTtc(bp);
                    var paidBefore = bp.Paiements.Sum(p => p.Montant);
                    var totalAfter = paidBefore + line.Amount;
                    DocumentTotalsHelper.EnsurePaymentsNotOverTtc(bp.TotalTtc, totalAfter);

                    db.PaiementsBonPreparation.Add(new PaiementBonPreparation
                    {
                        BonPreparationId = bp.Id,
                        Montant = line.Amount,
                        Date = date,
                        Mode = request.Mode,
                        Reference = reference
                    });

                    if (IsFullyPaid(bp.TotalTtc, totalAfter))
                        bp.EstPayee = true;
                }
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

    private static async Task<IReadOnlyList<BulkPayableDocument>> LoadOpenDocumentsAsync(
        AppDbContext db,
        int clientId,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Facture> facturesQ = db.Factures
            .Include(f => f.Paiements)
            .Include(f => f.Lignes)
            .Where(f => f.ClientId == clientId);
        IQueryable<BonPreparation> bpsQ = db.BonsPreparation
            .Include(b => b.Paiements)
            .Include(b => b.Lignes)
            .Where(b => b.ClientId == clientId);

        if (!track)
        {
            facturesQ = facturesQ.AsNoTracking();
            bpsQ = bpsQ.AsNoTracking();
        }

        var factures = await facturesQ.ToListAsync(cancellationToken);
        var bps = await bpsQ.ToListAsync(cancellationToken);

        var result = new List<BulkPayableDocument>();

        foreach (var f in factures)
        {
            DocumentTotalsHelper.SyncFactureTotalTtc(f);
            var paid = f.Paiements.Sum(p => p.Montant);
            var remaining = Math.Round(f.TotalTtc - paid, 2, MidpointRounding.AwayFromZero);
            if (remaining <= DocumentTotalsHelper.PaiementTtcTolerance)
                continue;

            result.Add(new BulkPayableDocument(
                BulkPayableDocumentKind.Facture,
                f.Id,
                f.Numero,
                f.Date.Date,
                f.TotalTtc,
                paid,
                remaining));
        }

        foreach (var b in bps)
        {
            DocumentTotalsHelper.SyncBonPreparationTotalTtc(b);
            var paid = b.Paiements.Sum(p => p.Montant);
            var remaining = Math.Round(b.TotalTtc - paid, 2, MidpointRounding.AwayFromZero);
            if (remaining <= DocumentTotalsHelper.PaiementTtcTolerance)
                continue;

            result.Add(new BulkPayableDocument(
                BulkPayableDocumentKind.BonPreparation,
                b.Id,
                b.Numero,
                b.Date.Date,
                b.TotalTtc,
                paid,
                remaining));
        }

        return result
            .OrderBy(d => d.Date)
            .ThenBy(d => d.DocumentId)
            .ToList();
    }

    internal static BulkPaymentPreview Allocate(IReadOnlyList<BulkPayableDocument> openDocuments, decimal amount)
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
                $"Le montant ({requested:N2}) dépasse le reste à encaisser ({totalRemaining:N2}).");
        }

        var lines = new List<BulkPaymentAllocationLine>();
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

            lines.Add(new BulkPaymentAllocationLine(
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
            throw new InvalidOperationException("Aucun document à encaisser pour ce client.");

        return new BulkPaymentPreview(requested, totalRemaining, lines);
    }

    private static bool IsFullyPaid(decimal ttc, decimal totalPaid) =>
        totalPaid + DocumentTotalsHelper.PaiementTtcTolerance >= ttc;
}
