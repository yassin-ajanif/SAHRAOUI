using Avalonia.Controls;
using GestionCommerciale.Modules.Tiers.Models;

namespace GestionCommerciale.Shared.Helpers;

public static class PartyAutoComplete
{
    public static AutoCompleteFilterPredicate<object?> ItemFilter { get; } = static (search, item) =>
    {
        if (item is not Tiers t) return false;
        if (string.IsNullOrWhiteSpace(search)) return false;

        var q = search.Trim();
        var sep = q.IndexOf("  ", StringComparison.Ordinal);
        if (sep > 0)
            q = q[..sep].Trim();
        if (q.Length == 0) return false;

        static bool Match(string? s, string qq) =>
            !string.IsNullOrEmpty(s) && s.Contains(qq, StringComparison.OrdinalIgnoreCase);

        return Match(t.Nom, q) || Match(t.ICE, q) || Match(t.Ville, q) || Match(t.Telephone, q) || Match(t.Email, q);
    };
}
