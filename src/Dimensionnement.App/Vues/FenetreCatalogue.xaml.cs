using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Dimensionnement.App.ModelesVue;

namespace Dimensionnement.App.Vues;

/// <summary>
/// Navigateur des catalogues : tableau filtrable à gauche, fiche technique à droite.
/// Les colonnes du tableau dépendent du catalogue choisi et sont refaites à chaque changement de famille.
/// </summary>
public partial class FenetreCatalogue : Window
{
    const string Dynamique = "colonne du catalogue";
    readonly ModeleVueCatalogue _modele;

    public FenetreCatalogue(ModeleVuePrincipal principal, string idFamille)
    {
        InitializeComponent();
        _modele = new ModeleVueCatalogue(principal, idFamille);
        DataContext = _modele;
        ConstruireColonnes();
        _modele.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ModeleVueCatalogue.Famille) or "" or null) ConstruireColonnes();
        };
        Closing += Fermeture;
    }

    /// <summary>Affiche la fiche de cette référence ; faux si aucun catalogue ne la contient.</summary>
    public bool Selectionner(string reference) => _modele.SelectionnerReference(reference);

    void Fermeture(object? sender, CancelEventArgs e)
    {
        // Valide la saisie en cours (le champ actif ne perd pas le focus avant la fermeture).
        if (Keyboard.FocusedElement is TextBox t) t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Cancel = !_modele.ConfirmerAbandon();
    }

    /// <summary>Insère les colonnes propres au catalogue entre la marque et le statut.</summary>
    void ConstruireColonnes()
    {
        foreach (var ancienne in Tableau.Columns.Where(c => Dynamique.Equals(c.GetValue(TagProperty))).ToList())
            Tableau.Columns.Remove(ancienne);
        var position = Tableau.Columns.Count - 1;
        var colonnes = _modele.Colonnes;
        for (var i = 0; i < colonnes.Count; i++)
        {
            var c = colonnes[i];
            var colonne = new DataGridTextColumn
            {
                Header = c.Unite.Length > 0 ? $"{c.Libelle} ({c.Unite})" : c.Libelle,
                Binding = new Binding($"Colonnes[{i}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 70,
            };
            colonne.SetValue(TagProperty, Dynamique);
            Tableau.Columns.Insert(position + i, colonne);
        }
    }
}
