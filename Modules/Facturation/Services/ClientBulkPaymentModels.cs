using GestionCommerciale.Modules.Facturation.Models;

namespace GestionCommerciale.Modules.Facturation.Services;

public enum BulkPayableDocumentKind
{
    Facture = 0,
    BonPreparation = 1
}

public sealed record BulkPayableDocument(
    BulkPayableDocumentKind Kind,
    int DocumentId,
    string Numero,
    DateTime Date,
    decimal TotalTtc,
    decimal AlreadyPaid,
    decimal Remaining);

public sealed record BulkPaymentAllocationLine(
    BulkPayableDocumentKind Kind,
    int DocumentId,
    string Numero,
    DateTime DocumentDate,
    decimal Amount,
    decimal RemainingAfter,
    bool WillBeFullyPaid);

public sealed record BulkPaymentPreview(
    decimal RequestedAmount,
    decimal TotalRemaining,
    IReadOnlyList<BulkPaymentAllocationLine> Lines);

public sealed record ClientBulkPaymentRequest(
    int ClientId,
    decimal Amount,
    DateTime Date,
    ModePaiement Mode,
    string Reference);
