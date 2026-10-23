using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dimensionnement.Core.Persistance;

namespace Dimensionnement.App.Vues;

/// <summary>
/// Historique des versions : chaque version avec ce qu'elle apporte, la version installée marquée, et en tête la version
/// disponible sur GitHub quand une mise à jour attend.
/// </summary>
public class FenetreNouveautes : Window
{
    static readonly Brush Vert = Gel("#3FB950");

    public FenetreNouveautes(Version installee, VersionDisponible? disponible)
    {
        Title = "Nouveautés";
        Width = 760;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Fond");
        SetResourceReference(ForegroundProperty, "Texte");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var haut = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        var titre = new TextBlock { Text = "NOUVEAUTÉS – HISTORIQUE DES VERSIONS" };
        titre.SetResourceReference(StyleProperty, "TitrePanneau");
        haut.Children.Add(titre);
        var aide = new TextBlock
        {
            Text = $"Tu utilises la version v{installee.ToString(installee.Build > 0 ? 3 : 2)}. Les mises à jour sont vérifiées à chaque démarrage.",
            TextWrapping = TextWrapping.Wrap,
        };
        aide.SetResourceReference(TextBlock.ForegroundProperty, "TexteSecondaire");
        haut.Children.Add(aide);

        var liste = new StackPanel();
        if (disponible is not null)
        {
            var points = disponible.Notes.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("- ")).Select(l => l[2..]).ToList();
            if (points.Count == 0) points.Add("Le détail de cette version est sur sa page GitHub.");
            liste.Children.Add(Carte(disponible.Etiquette, null, "disponible", points, disponible.Etiquette));
        }
        foreach (var v in JournalVersions.Versions)
        {
            var etat = v.Version == MiseAJour.Normaliser(installee) ? "installée" : v.Version > MiseAJour.Normaliser(installee) ? "" : null;
            if (disponible is not null && v.Version == disponible.Version) continue;
            liste.Children.Add(Carte(v.Etiquette, v.Date, etat, v.Changements, null));
        }

        var page = new DockPanel();
        DockPanel.SetDock(haut, Dock.Top);
        page.Children.Add(haut);
        page.Children.Add(new ScrollViewer { Content = liste, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20, 0, 20, 16) });
        Content = page;
    }

    static Border Carte(string etiquette, DateTime? date, string? etat, IReadOnlyList<string> points, string? lienGithub)
    {
        var pile = new StackPanel();
        var entete = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (lienGithub is not null)
        {
            var lien = new Button { Content = "Page GitHub", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
            lien.Click += (_, _) => Process.Start(new ProcessStartInfo($"{MiseAJour.UrlReleases}/tag/{lienGithub}") { UseShellExecute = true });
            DockPanel.SetDock(lien, Dock.Right);
            entete.Children.Add(lien);
        }
        var nom = new TextBlock { Text = etiquette, FontWeight = FontWeights.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        entete.Children.Add(nom);
        if (date is { } d)
        {
            var quand = new TextBlock { Text = $"  ·  {d:dd/MM/yyyy}", VerticalAlignment = VerticalAlignment.Center };
            quand.SetResourceReference(TextBlock.ForegroundProperty, "TexteSecondaire");
            entete.Children.Add(quand);
        }
        if (!string.IsNullOrEmpty(etat))
        {
            var badge = new Border
            {
                Background = etat == "disponible" ? Vert : Gel("#F5A623"), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = etat.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Gel("#1B1F24") },
            };
            entete.Children.Add(badge);
        }
        pile.Children.Add(entete);
        foreach (var p in points)
        {
            var ligne = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var puce = new TextBlock { Text = "•", Margin = new Thickness(2, 0, 8, 0) };
            puce.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            DockPanel.SetDock(puce, Dock.Left);
            ligne.Children.Add(puce);
            ligne.Children.Add(new TextBlock { Text = p, TextWrapping = TextWrapping.Wrap });
            pile.Children.Add(ligne);
        }
        var carte = new Border
        {
            Child = pile, Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(etat == "disponible" ? 1 : 0, etat == "disponible" ? 1 : 0, etat == "disponible" ? 1 : 0, etat == "disponible" ? 1 : 0),
            BorderBrush = Vert,
        };
        carte.SetResourceReference(Border.BackgroundProperty, "PanneauClair");
        return carte;
    }

    static Brush Gel(string couleur)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(couleur));
        b.Freeze();
        return b;
    }
}
