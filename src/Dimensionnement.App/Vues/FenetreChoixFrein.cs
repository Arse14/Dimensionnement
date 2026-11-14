using System.Windows;
using System.Windows.Controls;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.Vues;

/// <summary>
/// Demande quelle source placer dans l'armoire pour la bobine d'un frein sans redresseur dans le moteur ; les choix
/// proposés dépendent de la tension d'alimentation et de la tension de bobine saisies, le recommandé en premier.
/// </summary>
public class FenetreChoixFrein : Window
{
    public SourceFrein? Choix { get; private set; }

    public FenetreChoixFrein(string question, IReadOnlyList<(SourceFrein Source, string Libelle)> options)
    {
        Title = "Alimentation de la bobine du frein";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Fond");
        SetResourceReference(ForegroundProperty, "Texte");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var pile = new StackPanel { Margin = new Thickness(20) };
        var titre = new TextBlock { Text = "ALIMENTATION DU FREIN À AJOUTER" };
        titre.SetResourceReference(StyleProperty, "TitrePanneau");
        pile.Children.Add(titre);
        pile.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        for (var i = 0; i < options.Count; i++)
        {
            var (source, libelle) = options[i];
            var contenu = new StackPanel();
            contenu.Children.Add(new TextBlock { Text = libelle, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (i == 0)
            {
                var conseil = new TextBlock { Text = "Recommandé d'après les tensions saisies", Margin = new Thickness(0, 2, 0, 0) };
                conseil.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                contenu.Children.Add(conseil);
            }
            var bouton = new Button
            {
                Content = contenu, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left, IsDefault = i == 0,
            };
            bouton.SetResourceReference(BackgroundProperty, "Panneau");
            bouton.Click += (_, _) => { Choix = source; DialogResult = true; };
            pile.Children.Add(bouton);
        }
        var annuler = new Button { Content = "Garder la solution recommandée", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        pile.Children.Add(annuler);
        Content = pile;
    }
}
