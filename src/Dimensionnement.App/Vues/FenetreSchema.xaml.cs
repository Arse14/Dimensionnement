using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Dimensionnement.App.ModelesVue;
using Dimensionnement.Core.Calculs;
using Microsoft.Web.WebView2.Core;

namespace Dimensionnement.App.Vues;

/// <summary>Schéma SVG (unifilaire, organigramme) affiché par WebView2 ; redessiné à chaque recalcul du projet.</summary>
public partial class FenetreSchema : Window
{
    readonly ModeleVuePrincipal _modele;
    readonly Func<ResultatProjet, string> _svg;
    bool _pret;

    public FenetreSchema(ModeleVuePrincipal modele, string titre, string aide, Func<ResultatProjet, string> svg, ICommand enregistrer)
    {
        InitializeComponent();
        _modele = modele;
        _svg = svg;
        DataContext = modele;
        Title = titre;
        Aide.Text = aide;
        BoutonEnregistrer.Command = enregistrer;
        Loaded += async (_, _) => await Initialiser();
        modele.PropertyChanged += Recalcul;
        Closed += (_, _) => modele.PropertyChanged -= Recalcul;
    }

    async Task Initialiser()
    {
        try
        {
            var donnees = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Dimensionnement", "WebView2");
            var environnement = await CoreWebView2Environment.CreateAsync(null, donnees);
            await Vue.EnsureCoreWebView2Async(environnement);
            // Clic sur un appareil de l'organigramme : sa fiche catalogue s'ouvre pour la contrôler et la valider.
            Vue.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (e.TryGetWebMessageAsString() is { Length: > 0 } reference) _modele.OuvrirFiche(reference);
            };
            _pret = true;
            Afficher();
        }
        catch (Exception ex)
        {
            Vue.Visibility = Visibility.Collapsed;
            Erreur.Visibility = Visibility.Visible;
            Erreur.Text = $"L'affichage intégré n'est pas disponible ({ex.Message}). Utilisez « Ouvrir dans le navigateur » ou « Enregistrer en SVG ».";
        }
    }

    void Recalcul(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ModeleVuePrincipal.Resultat)) Afficher();
    }

    /// <summary>Script de la vue intégrée : un clic sur un appareil qui porte une référence l'envoie à l'application.</summary>
    const string ScriptFiche =
        "<script>document.addEventListener('click',function(e){var g=e.target.closest&&e.target.closest('[data-reference]');" +
        "if(g&&window.chrome&&chrome.webview)chrome.webview.postMessage(g.getAttribute('data-reference'));});</script>";

    string Html() =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><style>body{margin:0;background:#fff}" +
        "[data-reference]{cursor:pointer}[data-reference]:hover rect{stroke:#1f6feb;stroke-width:2}</style></head><body>" +
        _svg(_modele.Resultat) + ScriptFiche + "</body></html>";

    void Afficher()
    {
        if (_pret) Vue.NavigateToString(Html());
    }

    void OuvrirNavigateur(object sender, RoutedEventArgs e)
    {
        try
        {
            var chemin = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid():N}.html");
            File.WriteAllText(chemin, Html());
            Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Impossible d'ouvrir le schéma :\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
