using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Dimensionnement.App.ModelesVue;

namespace Dimensionnement.App.Vues;

public partial class FenetrePrincipale : Window
{
    readonly ModeleVuePrincipal _modele = new();

    public FenetrePrincipale()
    {
        InitializeComponent();
        DataContext = _modele;
    }

    /// <summary>Chaque valeur saisie relance le calcul de tout le projet.</summary>
    void ModificationSaisie(object? sender, DataTransferEventArgs e) =>
        // Le recalcul est différé pour laisser la liaison terminer sa mise à jour.
        Dispatcher.BeginInvoke(new Action(_modele.Recalculer));

    /// <summary>Le menu « Ajouter » s'ouvre sous le bouton au clic gauche.</summary>
    void AfficherAjouts(object sender, RoutedEventArgs e)
    {
        var bouton = (Button)sender;
        bouton.ContextMenu.DataContext = _modele;
        bouton.ContextMenu.PlacementTarget = bouton;
        bouton.ContextMenu.Placement = PlacementMode.Bottom;
        bouton.ContextMenu.IsOpen = true;
    }

    void AfficherRecents(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        if (_modele.Recents.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Aucun projet récent", IsEnabled = false });
        foreach (var chemin in _modele.Recents)
        {
            var item = new MenuItem { Header = chemin };
            item.Click += (_, _) => _modele.OuvrirChemin(chemin);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void Fermeture(object? sender, CancelEventArgs e) => e.Cancel = !_modele.ConfirmerAbandon();
}
