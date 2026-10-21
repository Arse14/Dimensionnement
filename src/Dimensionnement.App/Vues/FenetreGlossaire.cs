using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Dimensionnement.Core.Calculs;

namespace Dimensionnement.App.Vues;

/// <summary>
/// Glossaire des coefficients et formules : chaque notion avec sa formule et le sens de chaque terme. Ouvert depuis le bandeau,
/// ou depuis le « ? » d'une ligne de note de calcul, il montre alors d'abord les notions de cette ligne.
/// </summary>
public class FenetreGlossaire : Window
{
    readonly StackPanel _liste = new();
    readonly IReadOnlyList<EntreeGlossaire> _enTete;

    public FenetreGlossaire(LigneCalcul? ligne = null)
    {
        Title = "Glossaire des calculs";
        Width = 820;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Fond");
        SetResourceReference(ForegroundProperty, "Texte");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _enTete = ligne is null ? Array.Empty<EntreeGlossaire>() : Glossaire.Pour(ligne);

        var recherche = new TextBox { Margin = new Thickness(0, 0, 0, 10), ToolTip = "Rechercher un terme, un symbole ou un mot de la formule" };
        recherche.TextChanged += (_, _) => Afficher(recherche.Text.Trim());
        var titre = new TextBlock { Text = "GLOSSAIRE – COEFFICIENTS, FACTEURS ET FORMULES" };
        titre.SetResourceReference(StyleProperty, "TitrePanneau");
        var aide = new TextBlock
        {
            Text = ligne is null
                ? "Ce que contient chaque formule des notes de calcul, terme par terme, avec un exemple chiffré."
                : $"Notions utilisées dans « {ligne.Libelle} » en premier, puis tout le glossaire.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        aide.SetResourceReference(TextBlock.ForegroundProperty, "TexteSecondaire");
        var haut = new StackPanel { Margin = new Thickness(20, 16, 20, 0) };
        haut.Children.Add(titre);
        haut.Children.Add(aide);
        haut.Children.Add(recherche);

        var defilement = new ScrollViewer { Content = _liste, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20, 0, 20, 16) };
        var page = new DockPanel();
        DockPanel.SetDock(haut, Dock.Top);
        page.Children.Add(haut);
        page.Children.Add(defilement);
        Content = page;
        Afficher("");
    }

    void Afficher(string filtre)
    {
        _liste.Children.Clear();
        bool Garde(EntreeGlossaire e) => filtre.Length == 0
            || $"{e.Terme} {e.Definition} {e.Formule} {string.Join(" ", e.Termes.Select(t => t.Symbole + " " + t.Signification))}"
                .Contains(filtre, StringComparison.OrdinalIgnoreCase);
        if (_enTete.Count > 0 && filtre.Length == 0)
        {
            _liste.Children.Add(Rubrique("POUR CETTE LIGNE"));
            foreach (var e in _enTete) _liste.Children.Add(Carte(e, true));
        }
        foreach (var groupe in Glossaire.Entrees.Where(Garde).GroupBy(e => e.Categorie))
        {
            _liste.Children.Add(Rubrique(groupe.Key.ToUpperInvariant()));
            foreach (var e in groupe) _liste.Children.Add(Carte(e, false));
        }
    }

    static TextBlock Rubrique(string texte)
    {
        var t = new TextBlock { Text = texte, FontWeight = FontWeights.Bold, FontSize = 11.5, Margin = new Thickness(2, 12, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        return t;
    }

    static Border Carte(EntreeGlossaire e, bool enAvant)
    {
        var pile = new StackPanel();
        pile.Children.Add(new TextBlock { Text = e.Terme, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        pile.Children.Add(new TextBlock { Text = e.Definition, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (e.Formule.Length > 0)
        {
            var formule = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
            formule.SetResourceReference(Border.BackgroundProperty, "Champ");
            formule.Child = new TextBlock { Text = e.Formule, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Segoe UI") };
            pile.Children.Add(formule);
        }
        if (e.Termes.Count > 0)
        {
            var grille = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < e.Termes.Count; i++)
            {
                grille.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var symbole = new TextBlock { Text = e.Termes[i].Symbole, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas, Segoe UI"),
                    Margin = new Thickness(0, 2, 14, 2) };
                symbole.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                var sens = new TextBlock { Text = e.Termes[i].Signification, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(symbole, i);
                Grid.SetRow(sens, i);
                Grid.SetColumn(sens, 1);
                grille.Children.Add(symbole);
                grille.Children.Add(sens);
            }
            pile.Children.Add(grille);
        }
        if (e.Exemple.Length > 0)
        {
            var exemple = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            exemple.Inlines.Add(new Run("Exemple : ") { FontWeight = FontWeights.SemiBold });
            exemple.Inlines.Add(new Run(e.Exemple));
            pile.Children.Add(exemple);
        }
        var reference = new TextBlock { Text = e.Reference, FontSize = 11, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 6, 0, 0) };
        reference.SetResourceReference(TextBlock.ForegroundProperty, "TexteSecondaire");
        pile.Children.Add(reference);

        var carte = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = pile,
            BorderThickness = new Thickness(enAvant ? 2 : 0) };
        carte.SetResourceReference(Border.BackgroundProperty, "PanneauClair");
        carte.SetResourceReference(Border.BorderBrushProperty, "Accent");
        return carte;
    }
}
