namespace GestionCommerciale.Modules.Facturation.Services;

public enum ClientAccountEntryKind
{
    Facture = 0,
    BonPreparation = 1,
    Avoir = 2,
    Paiement = 3
}

public sealed class ClientAccountStatementRow
{
    public DateTime Date { get; init; }
    public ClientAccountEntryKind Kind { get; init; }
    public long TieBreakId { get; init; }
    public ClientAccountEntryKind NavigationKind { get; init; }
    public int NavigationId { get; init; }
    public bool IsNavigable => NavigationId > 0;
    public string Designation { get; init; } = string.Empty;
    public string Observation { get; init; } = string.Empty;
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public decimal Balance { get; init; }
}

public sealed class ClientAccountStatementResult
{
    public required IReadOnlyList<ClientAccountStatementRow> Rows { get; init; }
    public decimal SoldeActuel { get; init; }
}
