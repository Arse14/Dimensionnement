using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Dimensionnement.App.Infrastructure;
using Dimensionnement.App.ModelesVue;

namespace Dimensionnement.App.Vues;

public partial class FenetrePrincipale : Window
{
    readonly ModeleVuePrincipal _modele = new();

    public FenetrePrincipale()
    {
        InitializeComponent();
        DataContext = _modele;
        // Nouvelle version publiée sur GitHub ? Vérifié à chaque démarrage, sans bloquer l'ouverture.
        Loaded += async (_, _) =>
        {
            _modele.AfficherNouveautesSiNouvelle();
            await _modele.VerifierMiseAJour();
        };
        // Projet ouvert par double-clic sur un fichier .dimelec (association créée par l'installateur).
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Length > 1 && System.IO.File.Exists(arguments[1]))
            Loaded += (_, _) => _modele.OuvrirChemin(arguments[1], confirmer: false);
    }

    /// <summary>Chaque valeur saisie relance le calcul de tout le projet.</summary>
    void ModificationSaisie(object? sender, DataTransferEventArgs e) =>
        // Le recalcul est différé pour laisser la liaison terminer sa mise à jour.
        Dispatcher.BeginInvoke(new Action(_modele.Recalculer));

    BindingExpression? _saisieEnErreur;

    /// <summary>Une case passe en rouge : la raison s'affiche en bas à droite, avec « Forcer » quand la valeur est seulement inhabituelle.</summary>
    void ErreurDeSaisie(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Removed)
        {
            if (ReferenceEquals(e.Error.BindingInError, _saisieEnErreur)) MasquerErreur();
            return;
        }
        _saisieEnErreur = e.Error.BindingInError as BindingExpression;
        var forcable = e.Error.ErrorContent is ErreurSaisie { Forcable: true };
        TexteErreur.Text = e.Error.ErrorContent switch
        {
            ErreurSaisie erreur => erreur.Message,
            string texte => texte,
            _ => e.Error.Exception?.Message ?? "Valeur refusée.",
        };
        BoutonForcer.Visibility = forcable ? Visibility.Visible : Visibility.Collapsed;
        BandeauErreur.Visibility = Visibility.Visible;
        MentionAide.Visibility = Visibility.Collapsed;
    }

    void MasquerErreur()
    {
        _saisieEnErreur = null;
        BandeauErreur.Visibility = Visibility.Collapsed;
        MentionAide.Visibility = Visibility.Visible;
    }

    void ForcerSaisie(object sender, RoutedEventArgs e)
    {
        if (_saisieEnErreur is not { } liaison) return;
        foreach (var erreur in Validation.GetErrors(liaison.Target).Select(v => v.ErrorContent).OfType<ErreurSaisie>())
            RegleVraisemblance.Forcees.Add((erreur.Chemin, erreur.Valeur));
        liaison.UpdateSource();
    }

    /// <summary>Le menu « Ajouter » s'ouvre sous le bouton au clic gauche.</summary>
    void AfficherAjouts(object sender, RoutedEventArgs e)
    {
        var bouton = (Button)sender;
        bouton.ContextMenu.DataContext = _modele;
        bouton.ContextMenu.PlacementTarget = bouton;
        bouton.ContextMenu.Placement = PlacementMode.Bottom;
        bouton.ContextMenu.IsOpen = true;
    }

    void DeplierNote(object sender, RoutedEventArgs e) => (DataContext as ModeleVuePrincipal)?.DeplierNote(true);
    void ReplierNote(object sender, RoutedEventArgs e) => (DataContext as ModeleVuePrincipal)?.DeplierNote(false);

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

    /// <summary>
    /// Fermeture de l'application : les catalogues ouverts et non enregistrés sont proposés à l'enregistrement d'abord, puis
    /// toutes les fenêtres secondaires sont fermées avec la principale, pour qu'aucune ne reste affichée seule.
    /// </summary>
    void Fermeture(object? sender, CancelEventArgs e)
    {
        var secondaires = OwnedWindows.OfType<Window>().ToList();
        if (secondaires.OfType<FenetreCatalogue>().Any(c => !c.ConfirmerAbandon()) || !_modele.ConfirmerAbandon())
        {
            e.Cancel = true;
            return;
        }
        foreach (var f in secondaires)
        {
            if (f is FenetreCatalogue c) c.FermetureSansQuestion = true;
            f.Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
