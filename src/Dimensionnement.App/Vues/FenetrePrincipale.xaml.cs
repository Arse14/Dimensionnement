using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Dimensionnement.App.ModelesVue;
using Dimensionnement.Core.Materiel;

namespace Dimensionnement.App.Vues;

public partial class FenetrePrincipale : Window
{
    readonly ModeleVuePrincipal _modele = new();
    bool _reconstruction;

    public FenetrePrincipale()
    {
        InitializeComponent();
        DataContext = _modele;
    }

    /// <summary>Chaque valeur saisie relance le calcul de tout le projet.</summary>
    void ModificationSaisie(object? sender, DataTransferEventArgs e) =>
        // Le recalcul est différé pour laisser la liaison terminer sa mise à jour.
        Dispatcher.BeginInvoke(new Action(Recalculer));

    void Recalculer()
    {
        _reconstruction = true;
        _modele.Recalculer();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => _reconstruction = false));
    }

    void ArbreSelection(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // L'arbre est reconstruit à chaque recalcul : seule une sélection faite par l'utilisateur dans l'arbre
        // doit changer la page affichée, sinon la saisie en cours renverrait sur la page du projet.
        if (_reconstruction || (!ArbreMateriel.IsKeyboardFocusWithin && !ArbreMateriel.IsMouseOver)) return;
        if (e.NewValue is not ElementMateriel element) return;
        var depart = element.DepartId is { } id ? _modele.Projet.Departs.FirstOrDefault(d => d.Id == id) : null;
        _modele.Selection = depart is null ? _modele.Projet : depart;
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
