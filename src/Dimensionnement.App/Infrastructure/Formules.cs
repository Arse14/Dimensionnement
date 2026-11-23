using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Dimensionnement.Core.Calculs;

namespace Dimensionnement.App.Infrastructure;

/// <summary>
/// Affiche un texte de calcul avec l'écriture mathématique propre (fractions superposées, racines, indices, grandeurs en
/// italique) : <c>inf:Formules.Texte="{Binding Formule}"</c> remplace <c>Text</c> sur un TextBlock. Le découpage est celui
/// du rapport (<see cref="EcritureMath"/>), la prose autour des formules garde la police de l'application.
/// </summary>
public static class Formules
{
    static readonly FontFamily Police = new("Cambria Math, Cambria, Times New Roman");

    public static readonly DependencyProperty TexteProperty = DependencyProperty.RegisterAttached(
        "Texte", typeof(string), typeof(Formules), new PropertyMetadata(null, Changer));

    /// <summary>Vrai : tout le texte est une formule (symbole du glossaire), même sans signe « = ».</summary>
    public static readonly DependencyProperty EntiereProperty = DependencyProperty.RegisterAttached(
        "Entiere", typeof(bool), typeof(Formules), new PropertyMetadata(false, Changer));

    public static string? GetTexte(DependencyObject o) => (string?)o.GetValue(TexteProperty);
    public static void SetTexte(DependencyObject o, string? valeur) => o.SetValue(TexteProperty, valeur);
    public static bool GetEntiere(DependencyObject o) => (bool)o.GetValue(EntiereProperty);
    public static void SetEntiere(DependencyObject o, bool valeur) => o.SetValue(EntiereProperty, valeur);

    static void Changer(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) Remplir(tb, GetTexte(tb) ?? "", GetEntiere(tb));
    }

    /// <summary>Remplit le TextBlock avec le texte mis en forme.</summary>
    public static void Remplir(TextBlock tb, string texte, bool entiere = false)
    {
        tb.Inlines.Clear();
        foreach (var n in EcritureMath.Analyser(texte, entiere)) tb.Inlines.Add(Element(n, tb, tb.FontSize));
    }

    /// <summary>Un TextBlock déjà rempli, pour les fenêtres construites en code.</summary>
    public static TextBlock Bloc(string texte, bool entiere = false, double taille = 13)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = taille };
        Remplir(tb, texte, entiere);
        return tb;
    }

    static Inline Element(NoeudMath n, TextBlock tb, double taille) => n switch
    {
        MorceauMath { Math: false } m => new Run(m.Texte),
        MorceauMath m => Morceau(m, taille),
        FractionMath f => new InlineUIContainer(Fraction(f, tb, taille)) { BaselineAlignment = BaselineAlignment.Center },
        RacineMath r => new InlineUIContainer(Racine(r, tb, taille)) { BaselineAlignment = BaselineAlignment.Center },
        _ => new Run(),
    };

    static Inline Morceau(MorceauMath m, double taille)
    {
        var run = new Run(m.Texte)
        {
            FontFamily = Police,
            FontSize = taille * (m.Grand ? 1.6 : 1.08),
            FontStyle = m.Italique ? FontStyles.Italic : FontStyles.Normal,
        };
        if (m.Indice.Length == 0) return run;
        var span = new Span(run);
        span.Inlines.Add(new Run(m.Indice) { FontFamily = Police, FontSize = taille * 0.75, BaselineAlignment = BaselineAlignment.Subscript });
        return span;
    }

    // Numérateur ou dénominateur : un TextBlock sans retour à la ligne, de même couleur que le texte.
    static TextBlock Ligne(IReadOnlyList<NoeudMath> noeuds, TextBlock tb, double taille)
    {
        var bloc = new TextBlock { FontSize = taille, HorizontalAlignment = HorizontalAlignment.Center };
        bloc.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = tb });
        foreach (var n in noeuds) bloc.Inlines.Add(Element(n, tb, taille));
        return bloc;
    }

    static FrameworkElement Fraction(FractionMath f, TextBlock tb, double taille)
    {
        var trait = new Rectangle { Height = 1, Margin = new Thickness(0, 1, 0, 1), SnapsToDevicePixels = true };
        trait.SetBinding(Shape.FillProperty, new Binding(nameof(TextBlock.Foreground)) { Source = tb });
        var pile = new StackPanel { Margin = new Thickness(2, 1, 2, 1) };
        var haut = Ligne(f.Numerateur, tb, taille * 0.95);
        var bas = Ligne(f.Denominateur, tb, taille * 0.95);
        haut.Margin = bas.Margin = new Thickness(3, 0, 3, 0);
        pile.Children.Add(haut);
        pile.Children.Add(trait);
        pile.Children.Add(bas);
        return pile;
    }

    static FrameworkElement Racine(RacineMath r, TextBlock tb, double taille)
    {
        var signe = new TextBlock { Text = "√", FontFamily = Police, FontSize = taille * 1.15, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -1, 0) };
        signe.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = tb });
        var contenu = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(1, 1, 1, 0), SnapsToDevicePixels = true,
            Child = Ligne(r.Contenu, tb, taille) };
        contenu.SetBinding(Border.BorderBrushProperty, new Binding(nameof(TextBlock.Foreground)) { Source = tb });
        var pile = new StackPanel { Orientation = Orientation.Horizontal };
        pile.Children.Add(signe);
        pile.Children.Add(contenu);
        return pile;
    }
}
