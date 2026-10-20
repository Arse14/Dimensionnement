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

    string Html() =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><style>body{margin:0;background:#fff}</style></head><body>" +
        _svg(_modele.Resultat) + "</body></html>";

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
