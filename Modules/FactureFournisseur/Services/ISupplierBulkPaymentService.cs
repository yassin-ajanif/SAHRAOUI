namespace GestionCommerciale.Modules.FactureFournisseur.Services;

public interface ISupplierBulkPaymentService
{
    Task<IReadOnlyList<SupplierBulkPayableDocument>> GetOpenDocumentsAsync(int fournisseurId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a FIFO allocation preview. Throws if amount &lt;= 0 or exceeds total remaining.
    /// </summary>
    Task<SupplierBulkPaymentPreview> PreviewAsync(int fournisseurId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies payments and EstPayee updates in a single database transaction.
    /// </summary>
    Task ApplyAsync(SupplierBulkPaymentRequest request, CancellationToken cancellationToken = default);
}
