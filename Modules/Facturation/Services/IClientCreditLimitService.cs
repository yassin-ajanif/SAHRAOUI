namespace GestionCommerciale.Modules.Facturation.Services;

public interface IClientCreditLimitService
{
    /// <summary>
    /// Returns an error message if the client's outstanding balance already meets or exceeds MaxCredit.
    /// Null when no limit is set or the limit is not reached.
    /// </summary>
    Task<string?> GetBlockMessageIfLimitExceededAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an error message if applying <paramref name="proposedFactureTtc"/> would push the
    /// client's solde above MaxCredit. When editing, pass the previous facture TTC to replace.
    /// </summary>
    Task<string?> GetBlockMessageIfFactureWouldExceedAsync(
        int clientId,
        decimal proposedFactureTtc,
        decimal existingFactureTtc = 0m,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advisory warning when a BL document TTC would exceed remaining client credit.
    /// Null when no limit is set or the document fits within the plafond.
    /// </summary>
    Task<string?> GetBlDocumentCreditWarningAsync(
        int clientId,
        decimal documentTtc,
        CancellationToken cancellationToken = default);
}
