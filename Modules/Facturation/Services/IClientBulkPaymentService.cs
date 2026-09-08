namespace GestionCommerciale.Modules.Facturation.Services;

public interface IClientBulkPaymentService
{
    Task<IReadOnlyList<BulkPayableDocument>> GetOpenDocumentsAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a FIFO allocation preview. Throws if amount &lt;= 0 or exceeds total remaining.
    /// </summary>
    Task<BulkPaymentPreview> PreviewAsync(int clientId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies payments and EstPayee updates in a single database transaction.
    /// </summary>
    Task ApplyAsync(ClientBulkPaymentRequest request, CancellationToken cancellationToken = default);
}
