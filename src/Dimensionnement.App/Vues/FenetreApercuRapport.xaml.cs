using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Dimensionnement.App.ModelesVue;
using Microsoft.Web.WebView2.Core;

namespace Dimensionnement.App.Vues;

/// <summary>Aperçu du rapport HTML avant sa génération en PDF ; redessiné à chaque recalcul du projet.</summary>
public partial class FenetreApercuRapport : Window
{
    readonly ModeleVuePrincipal _modele;
    bool _pret;

    public FenetreApercuRapport(ModeleVuePrincipal modele)
    {
        InitializeComponent();
        _modele = modele;
        DataContext = modele;
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
            Erreur.Text = $"L'aperçu intégré n'est pas disponible ({ex.Message}). Utilisez « Ouvrir dans le navigateur ».";
        }
    }

    void Recalcul(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ModeleVuePrincipal.Resultat)) Afficher();
    }

    void Afficher()
    {
        if (_pret) Vue.NavigateToString(_modele.RapportHtmlApercu);
    }

    void GenererPdf(object sender, RoutedEventArgs e) => _modele.GenererRapport();

    void OuvrirNavigateur(object sender, RoutedEventArgs e)
    {
        try
        {
            var chemin = Path.Combine(Path.GetTempPath(), $"rapport-{Guid.NewGuid():N}.html");
            File.WriteAllText(chemin, _modele.RapportHtmlApercu);
            Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show($"Impossible d'ouvrir le rapport :\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
