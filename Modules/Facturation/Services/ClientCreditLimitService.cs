using GestionCommerciale.Modules.Tiers.Models;
using GestionCommerciale.Shared.Database;
using GestionCommerciale.Shared.Helpers;
using GestionCommerciale.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCommerciale.Modules.Facturation.Services;

public sealed class ClientCreditLimitService : IClientCreditLimitService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IClientAccountStatementService _ledger;
    private readonly ILocaleService _locale;

    public ClientCreditLimitService(
        IDbContextFactory<AppDbContext> dbFactory,
        IClientAccountStatementService ledger,
        ILocaleService locale)
    {
        _dbFactory = dbFactory;
        _ledger = ledger;
        _locale = locale;
    }

    public async Task<string?> GetBlockMessageIfLimitExceededAsync(int clientId, CancellationToken cancellationToken = default)
    {
        var maxCredit = await GetMaxCreditAsync(clientId, cancellationToken);
        if (maxCredit is null)
            return null;

        var statement = await _ledger.GetStatementAsync(clientId, cancellationToken);
        if (statement.SoldeActuel < maxCredit.Value)
            return null;

        return FormatExceededMessage(statement.SoldeActuel, maxCredit.Value);
    }

    public async Task<string?> GetBlockMessageIfFactureWouldExceedAsync(
        int clientId,
        decimal proposedFactureTtc,
        decimal existingFactureTtc = 0m,
        CancellationToken cancellationToken = default)
    {
        var maxCredit = await GetMaxCreditAsync(clientId, cancellationToken);
        if (maxCredit is null)
            return null;

        var statement = await _ledger.GetStatementAsync(clientId, cancellationToken);
        var projected = statement.SoldeActuel - existingFactureTtc + proposedFactureTtc;
        if (projected <= maxCredit.Value)
            return null;

        return FormatWouldExceedMessage(projected, maxCredit.Value);
    }

    private async Task<decimal?> GetMaxCreditAsync(int clientId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tiers.AsNoTracking()
            .Where(t => t.Id == clientId && (t.Type == TypeTiers.Client || t.Type == TypeTiers.LesDeux))
            .Select(t => t.MaxCredit)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private string FormatExceededMessage(decimal solde, decimal maxCredit) =>
        _locale.Tf(
            "CreditLimit_Exceeded",
            CurrencyHelper.Format(solde),
            CurrencyHelper.Format(maxCredit));

    private string FormatWouldExceedMessage(decimal projected, decimal maxCredit) =>
        _locale.Tf(
            "CreditLimit_WouldExceed",
            CurrencyHelper.Format(projected),
            CurrencyHelper.Format(maxCredit));
}
