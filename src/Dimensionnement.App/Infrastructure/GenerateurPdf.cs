using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Dimensionnement.App.Infrastructure;

/// <summary>
/// Convertit le rapport HTML en PDF avec le moteur Edge WebView2 présent sous Windows 10 et 11,
/// dans une fenêtre hors écran.
/// </summary>
public static class GenerateurPdf
{
    // Marges et format A4, en pouces.
    const double Marge = 0.4;
    const double LargeurA4 = 8.27;
    const double HauteurA4 = 11.69;

    public static async Task Generer(string html, string chemin)
    {
        var fenetre = new Window
        {
            Width = 900, Height = 700, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
        };
        var vue = new WebView2();
        fenetre.Content = vue;
        var fichierHtml = Path.Combine(Path.GetTempPath(), $"rapport-{Guid.NewGuid()}.html");
        fenetre.Show();
        try
        {
            var donnees = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Dimensionnement", "WebView2");
            var environnement = await CoreWebView2Environment.CreateAsync(null, donnees);
            await vue.EnsureCoreWebView2Async(environnement);

            var charge = new TaskCompletionSource<bool>();
            vue.CoreWebView2.NavigationCompleted += (_, e) => charge.TrySetResult(e.IsSuccess);
            await File.WriteAllTextAsync(fichierHtml, html);
            vue.CoreWebView2.Navigate(new Uri(fichierHtml).AbsoluteUri);
            if (!await charge.Task)
                throw new InvalidOperationException("Le rapport n'a pas pu être chargé pour la conversion en PDF.");

            var reglages = environnement.CreatePrintSettings();
            reglages.Orientation = CoreWebView2PrintOrientation.Portrait;
            reglages.PageWidth = LargeurA4;
            reglages.PageHeight = HauteurA4;
            reglages.MarginTop = reglages.MarginBottom = reglages.MarginLeft = reglages.MarginRight = Marge;
            reglages.ShouldPrintBackgrounds = true;
            reglages.ShouldPrintHeaderAndFooter = false;
            if (!await vue.CoreWebView2.PrintToPdfAsync(chemin, reglages))
                throw new InvalidOperationException("La conversion du rapport en PDF a échoué.");
        }
        finally
        {
            vue.Dispose();
            fenetre.Close();
            File.Delete(fichierHtml);
        }
    }
}
