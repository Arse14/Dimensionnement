using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dimensionnement.App.ModelesVue;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.App.Vues;

/// <summary>
/// Navigateur des catalogues : tableau filtrable à gauche, fiche technique à droite.
/// Les colonnes du tableau dépendent du catalogue choisi et sont refaites à chaque changement de famille.
/// </summary>
public partial class FenetreCatalogue : Window
{
    const string Dynamique = "colonne du catalogue";
    readonly ModeleVueCatalogue _modele;

    /// <summary>Référence retenue par le bouton « Retenir » quand la fenêtre sert à choisir un article.</summary>
    public string? ReferenceRetenue { get; private set; }

    public FenetreCatalogue(ModeleVuePrincipal principal, string idFamille, bool choix = false)
    {
        InitializeComponent();
        _modele = new ModeleVueCatalogue(principal, idFamille, choix);
        _modele.Retenu += r => { ReferenceRetenue = r; DialogResult = true; };
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

    /// <summary>Vrai quand l'application se ferme : les modifications ont déjà été proposées à l'enregistrement.</summary>
    public bool FermetureSansQuestion { get; set; }

    /// <summary>Propose d'enregistrer les modifications du catalogue ; faux si l'utilisateur annule.</summary>
    public bool ConfirmerAbandon()
    {
        // Valide la saisie en cours (le champ actif ne perd pas le focus avant la fermeture).
        if (Keyboard.FocusedElement is TextBox t && IsAncestorOf(t)) t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        return _modele.ConfirmerAbandon();
    }

    void Fermeture(object? sender, CancelEventArgs e)
    {
        if (FermetureSansQuestion) return;
        e.Cancel = !ConfirmerAbandon();
    }

    /// <summary>Copie la référence de l'article sélectionné, sans la mention « à compléter » d'un article sans référence.</summary>
    void CopierReference(object sender, RoutedEventArgs e)
    {
        if (_modele.Selection is not { } ligne || BibliothequeCatalogues.Chaine(ligne.Article, "reference") is not { Length: > 0 } reference) return;
        // Le presse-papiers peut être tenu un instant par une autre application.
        for (var essai = 0; ; essai++)
        {
            try { Clipboard.SetText(reference); break; }
            catch (System.Runtime.InteropServices.COMException) when (essai < 4) { Thread.Sleep(50); }
            catch (System.Runtime.InteropServices.COMException) { return; }
        }
        IconeCopier.Text = "\uE73E";
        TexteCopier.Text = "Copié";
        var minuterie = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        minuterie.Tick += (_, _) => { minuterie.Stop(); IconeCopier.Text = "\uE8C8"; TexteCopier.Text = "Copier"; };
        minuterie.Start();
    }

    /// <summary>Ctrl+C sur le tableau copie la référence seule, pas toute la ligne.</summary>
    void CopierAuClavier(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control) return;
        CopierReference(sender, e);
        e.Handled = true;
    }

    /// <summary>Le clic droit sélectionne la ligne visée avant d'ouvrir le menu.</summary>
    void SelectionnerAuClicDroit(object sender, MouseButtonEventArgs e)
    {
        for (var o = e.OriginalSource as DependencyObject; o is not null; o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is DataGridRow ligne) { ligne.IsSelected = true; return; }
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
