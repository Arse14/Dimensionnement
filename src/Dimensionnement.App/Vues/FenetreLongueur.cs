using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dimensionnement.App.Vues;

/// <summary>Saisie de la longueur exacte d'un rail DIN ou d'une goulotte, en mm.</summary>
public class FenetreLongueur : Window
{
    public double? Longueur { get; private set; }

    public FenetreLongueur(string objet, double actuelle)
    {
        Title = $"Longueur {objet}";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Fond");
        SetResourceReference(ForegroundProperty, "Texte");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var pile = new StackPanel { Margin = new Thickness(20) };
        pile.Children.Add(new TextBlock { Text = $"Longueur {objet} (mm)", Margin = new Thickness(0, 0, 0, 6) });
        var saisie = new TextBox { Text = actuelle.ToString("0", CultureInfo.CurrentCulture), Margin = new Thickness(0, 0, 0, 14) };
        pile.Children.Add(saisie);
        var erreur = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, Visibility = Visibility.Collapsed, Margin = new Thickness(0, -8, 0, 10) };
        pile.Children.Add(erreur);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Appliquer", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 4, 14, 4) };
        var annuler = new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };
        ok.Click += (_, _) =>
        {
            // Point ou virgule décimale acceptés.
            var texte = saisie.Text.Trim().Replace(',', '.');
            if (double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 20 && v <= 10000)
            {
                Longueur = v;
                DialogResult = true;
            }
            else
            {
                erreur.Text = "Saisir une longueur entre 20 et 10 000 mm.";
                erreur.Visibility = Visibility.Visible;
            }
        };
        boutons.Children.Add(ok);
        boutons.Children.Add(annuler);
        pile.Children.Add(boutons);
        Content = pile;
        Loaded += (_, _) => { saisie.Focus(); saisie.SelectAll(); Keyboard.Focus(saisie); };
    }
}
