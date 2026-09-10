using GestionCommerciale.Modules.Facturation.Models;

namespace GestionCommerciale.Modules.FactureFournisseur.Services;

public enum SupplierBulkPayableDocumentKind
{
    FactureFournisseur = 0
}

public sealed record SupplierBulkPayableDocument(
    SupplierBulkPayableDocumentKind Kind,
    int DocumentId,
    string Numero,
    DateTime Date,
    decimal TotalTtc,
    decimal AlreadyPaid,
    decimal Remaining);

public sealed record SupplierBulkPaymentAllocationLine(
    SupplierBulkPayableDocumentKind Kind,
    int DocumentId,
    string Numero,
    DateTime DocumentDate,
    decimal Amount,
    decimal RemainingAfter,
    bool WillBeFullyPaid);

public sealed record SupplierBulkPaymentPreview(
    decimal RequestedAmount,
    decimal TotalRemaining,
    IReadOnlyList<SupplierBulkPaymentAllocationLine> Lines);

public sealed record SupplierBulkPaymentRequest(
    int FournisseurId,
    decimal Amount,
    DateTime Date,
    ModePaiement Mode,
    string Reference);
