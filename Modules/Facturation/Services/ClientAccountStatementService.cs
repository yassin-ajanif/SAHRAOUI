using GestionCommerciale.Modules.Facturation.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCommerciale.Modules.Facturation.Services;

public sealed class ClientAccountStatementService : IClientAccountStatementService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILocaleService _locale;

    public ClientAccountStatementService(IDbContextFactory<AppDbContext> dbFactory, ILocaleService locale)
    {
        _dbFactory = dbFactory;
        _locale = locale;
    }

    public async Task<ClientAccountStatementResult> GetStatementAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var factures = await db.Factures.AsNoTracking()
            .Where(f => f.ClientId == clientId)
            .Select(f => new
            {
                f.Id,
                f.Numero,
                f.Date,
                f.TotalTtc,
                Paiements = f.Paiements!.Select(p => new
                {
                    p.Id,
                    p.Date,
                    p.Montant,
                    p.Mode,
                    p.Reference
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var avoirs = await db.Avoirs.AsNoTracking()
            .Where(a => a.ClientId == clientId)
            .Select(a => new
            {
                a.Id,
                a.Numero,
                a.Date,
                a.Motif,
                Lignes = a.Lignes!.Select(l => new
                {
                    l.Quantite,
                    l.PrixUnitaireHT,
                    l.Remise,
                    l.TauxTVA
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var entries = new List<(DateTime Date, ClientAccountEntryKind Kind, long TieBreakId, ClientAccountEntryKind NavigationKind, int NavigationId, string Designation, string Observation, decimal Debit, decimal Credit)>();

        foreach (var f in factures)
        {
            var ttc = f.TotalTtc;
            if (ttc <= 0) continue;

            entries.Add((
                f.Date.Date,
                ClientAccountEntryKind.Facture,
                f.Id,
                ClientAccountEntryKind.Facture,
                f.Id,
                _locale.Tf("ClientLedger_FactureFmt", f.Numero),
                string.Empty,
                ttc,
                0));
        }

        var bonsPreparation = await db.BonsPreparation.AsNoTracking()
            .Where(b => b.ClientId == clientId)
            .Select(b => new
            {
                b.Id,
                b.Numero,
                b.Date,
                b.TotalTtc,
                Paiements = b.Paiements!.Select(p => new
                {
                    p.Id,
                    p.Date,
                    p.Montant,
                    p.Mode,
                    p.Reference
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var b in bonsPreparation)
        {
            var ttc = b.TotalTtc;
            if (ttc <= 0) continue;

            entries.Add((
                b.Date.Date,
                ClientAccountEntryKind.BonPreparation,
                b.Id,
                ClientAccountEntryKind.BonPreparation,
                b.Id,
                _locale.Tf("ClientLedger_BonPreparationFmt", b.Numero),
                string.Empty,
                ttc,
                0));
        }

        foreach (var a in avoirs)
        {
            var lignes = a.Lignes.Select(l => new AvoirLigne
            {
                Quantite = l.Quantite,
                PrixUnitaireHT = l.PrixUnitaireHT,
                Remise = l.Remise,
                TauxTVA = l.TauxTVA
            }).ToList();
            var (_, _, ttc) = DocumentTotalsHelper.AvoirTotals(lignes);
            if (ttc <= 0) continue;

            var observation = string.IsNullOrWhiteSpace(a.Motif) ? string.Empty : a.Motif.Trim();
            entries.Add((
                a.Date.Date,
                ClientAccountEntryKind.Avoir,
                a.Id,
                ClientAccountEntryKind.Avoir,
                a.Id,
                _locale.Tf("ClientLedger_AvoirFmt", a.Numero),
                observation,
                0,
                ttc));
        }

        foreach (var f in factures)
        {
            foreach (var p in f.Paiements)
            {
                if (p.Montant <= 0 || p.Mode == ModePaiement.Credit) continue;
                var observation = string.IsNullOrWhiteSpace(p.Reference) ? string.Empty : p.Reference.Trim();
                entries.Add((
                    p.Date.Date,
                    ClientAccountEntryKind.Paiement,
                    p.Id,
                    ClientAccountEntryKind.Facture,
                    f.Id,
                    PaymentDesignation(p.Mode),
                    observation,
                    0,
                    p.Montant));
            }
        }

        foreach (var b in bonsPreparation)
        {
            foreach (var p in b.Paiements)
            {
                if (p.Montant <= 0 || p.Mode == ModePaiement.Credit) continue;
                var observation = string.IsNullOrWhiteSpace(p.Reference) ? string.Empty : p.Reference.Trim();
                entries.Add((
                    p.Date.Date,
                    ClientAccountEntryKind.Paiement,
                    p.Id,
                    ClientAccountEntryKind.BonPreparation,
                    b.Id,
                    PaymentDesignation(p.Mode),
                    observation,
                    0,
                    p.Montant));
            }
        }

        var ordered = entries
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.TieBreakId)
            .ToList();

        decimal balance = 0;
        var rows = new List<ClientAccountStatementRow>(ordered.Count);
        foreach (var e in ordered)
        {
            balance += e.Debit - e.Credit;
            rows.Add(new ClientAccountStatementRow
            {
                Date = e.Date,
                Kind = e.Kind,
                TieBreakId = e.TieBreakId,
                NavigationKind = e.NavigationKind,
                NavigationId = e.NavigationId,
                Designation = e.Designation,
                Observation = e.Observation,
                Debit = e.Debit,
                Credit = e.Credit,
                Balance = balance
            });
        }

        return new ClientAccountStatementResult
        {
            Rows = rows,
            SoldeActuel = balance
        };
    }

    private string PaymentDesignation(ModePaiement mode) =>
        _locale.T(mode switch
        {
            ModePaiement.Virement => "ClientLedger_PayVirement",
            ModePaiement.Cheque => "ClientLedger_PayCheque",
            ModePaiement.Especes => "ClientLedger_PayEspeces",
            ModePaiement.TPE => "ClientLedger_PayTpe",
            ModePaiement.Effet => "ClientLedger_PayEffet",
            ModePaiement.Credit => "ClientLedger_PayCredit",
            _ => "ClientLedger_PayReceived"
        });
}
