using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GestionCommerciale.Modules.Tiers.Models;

namespace GestionCommerciale.Shared.Helpers;

public partial class ClientCategoryFilter : ObservableObject
{
    private readonly List<Tiers> _all = [];

    public ObservableCollection<CategorieTiers> Categories { get; } = [CategorieTiers.Officiel, CategorieTiers.Comptoir];

    public ObservableCollection<Tiers> Clients { get; } = [];

    [ObservableProperty] private CategorieTiers _categorie = CategorieTiers.Officiel;

    public void BindSelection(Func<int> getClientId, Action<Tiers?> setSelectedClient)
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Categorie)) return;
            var id = getClientId();
            setSelectedClient(Clients.FirstOrDefault(c => c.Id == id) ?? Clients.FirstOrDefault());
        };
    }

    public void ReplaceAll(IEnumerable<Tiers> clients)
    {
        _all.Clear();
        foreach (var c in clients)
        {
            c.ResetNomEtSolde();
            _all.Add(c);
        }
        Refresh();
    }

    public void EnsureCategoryFor(int clientId)
    {
        var match = _all.FirstOrDefault(c => c.Id == clientId);
        if (match != null && match.Categorie != Categorie)
            Categorie = match.Categorie;
    }

    partial void OnCategorieChanged(CategorieTiers value) => Refresh();

    private void Refresh()
    {
        Clients.Clear();
        foreach (var c in _all.Where(c => c.Categorie == Categorie).OrderBy(c => c.Nom))
            Clients.Add(c);
    }
}
