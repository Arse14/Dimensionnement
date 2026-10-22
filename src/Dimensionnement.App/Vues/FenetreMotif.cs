using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dimensionnement.Core.Calculs;

namespace Dimensionnement.App.Vues;

/// <summary>Motif obligatoire pour ignorer une alerte : il reste affiché dans la liste des alertes.</summary>
public class FenetreMotif : Window
{
    public string Motif { get; private set; } = "";

    public FenetreMotif(Alerte alerte)
    {
        Title = "Ignorer une alerte";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Fond");
        SetResourceReference(ForegroundProperty, "Texte");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var pile = new StackPanel { Margin = new Thickness(20) };
        pile.Children.Add(new TextBlock { Text = $"{alerte.Lieu} : {alerte.Point}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = alerte.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TexteSecondaire");
        pile.Children.Add(detail);
        pile.Children.Add(new TextBlock { Text = "Motif (gardé dans la liste des alertes)", Margin = new Thickness(0, 0, 0, 6) });
        var saisie = new TextBox { Margin = new Thickness(0, 0, 0, 14), Height = 56, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(6, 4, 6, 4), TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
        pile.Children.Add(saisie);
        var erreur = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, Visibility = Visibility.Collapsed, Margin = new Thickness(0, -8, 0, 10),
            Text = "Le motif est obligatoire." };
        pile.Children.Add(erreur);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Ignorer", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 4, 14, 4) };
        var annuler = new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(saisie.Text)) { erreur.Visibility = Visibility.Visible; return; }
            Motif = saisie.Text.Trim();
            DialogResult = true;
        };
        boutons.Children.Add(ok);
        boutons.Children.Add(annuler);
        pile.Children.Add(boutons);
        Content = pile;
        Loaded += (_, _) => Keyboard.Focus(saisie);
    }
}
