using System.ComponentModel.DataAnnotations.Schema;
using GestionCommerciale.Shared.Models;

namespace GestionCommerciale.Modules.Tiers.Models;

public class Tiers : BaseEntity
{
    public TypeTiers Type { get; set; }
    public CategorieTiers Categorie { get; set; } = CategorieTiers.Officiel;
    public string Nom { get; set; } = string.Empty;
    public string ICE { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public string Ville { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ConditionsPaiement { get; set; } = string.Empty;
    /// <summary>Optional ceiling on outstanding client balance (solde). Null = no limit.</summary>
    public decimal? MaxCredit { get; set; }
    public bool Actif { get; set; } = true;

    /// <summary>UI-only picker label: name, optionally with solde.</summary>
    [NotMapped]
    public string NomEtSolde { get; set; } = string.Empty;

    public void ResetNomEtSolde() => NomEtSolde = Nom;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(NomEtSolde) ? Nom : NomEtSolde;
}
