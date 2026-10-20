using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Rapport;

namespace Dimensionnement.App.Infrastructure;

/// <summary>Appareil déplacé par l'utilisateur, coordonnées en mm sur la plaque.</summary>
public record DeplacementAppareil(string Repere, double X, double Y);

/// <summary>
/// Vue de face de l'armoire, à l'échelle du millimètre dans une Viewbox : zones CEM, rails, appareils et champs.
/// En mode interactif, les appareils se déplacent à la souris (pas de 5 mm) et chaque dépôt est envoyé à
/// <see cref="DeplacerCommande"/>.
/// </summary>
public class VueArmoire : Border
{
    const double Marge = 40;
    const double Pas = 5;

    public static readonly DependencyProperty ArmoireProperty = DependencyProperty.Register(nameof(Armoire),
        typeof(ResultatArmoire), typeof(VueArmoire), new PropertyMetadata(null, Redessiner));
    public static readonly DependencyProperty AfficherChampsProperty = DependencyProperty.Register(nameof(AfficherChamps),
        typeof(bool), typeof(VueArmoire), new PropertyMetadata(true, Redessiner));
    public static readonly DependencyProperty AfficherZonesProperty = DependencyProperty.Register(nameof(AfficherZones),
        typeof(bool), typeof(VueArmoire), new PropertyMetadata(true, Redessiner));
    public static readonly DependencyProperty InteractifProperty = DependencyProperty.Register(nameof(Interactif),
        typeof(bool), typeof(VueArmoire), new PropertyMetadata(false, Redessiner));
    public static readonly DependencyProperty DeplacerCommandeProperty = DependencyProperty.Register(nameof(DeplacerCommande),
        typeof(ICommand), typeof(VueArmoire));

    public ResultatArmoire? Armoire { get => (ResultatArmoire?)GetValue(ArmoireProperty); set => SetValue(ArmoireProperty, value); }
    public bool AfficherChamps { get => (bool)GetValue(AfficherChampsProperty); set => SetValue(AfficherChampsProperty, value); }
    public bool AfficherZones { get => (bool)GetValue(AfficherZonesProperty); set => SetValue(AfficherZonesProperty, value); }
    public bool Interactif { get => (bool)GetValue(InteractifProperty); set => SetValue(InteractifProperty, value); }
    public ICommand? DeplacerCommande { get => (ICommand?)GetValue(DeplacerCommandeProperty); set => SetValue(DeplacerCommandeProperty, value); }

    readonly Canvas _plan = new();
    Border? _glisse;
    Point _depart;
    Point _origine;

    public VueArmoire()
    {
        Child = new Viewbox { Child = _plan, Stretch = Stretch.Uniform };
        _plan.MouseMove += Deplacer;
        _plan.MouseLeftButtonUp += Deposer;
    }

    static void Redessiner(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((VueArmoire)d).Dessiner();

    static Brush Pinceau(string couleur) => (Brush)new BrushConverter().ConvertFromString(couleur)!;

    void Dessiner()
    {
        _plan.Children.Clear();
        _glisse = null;
        if (Armoire is not { } a) return;
        _plan.Width = a.LargeurTotale + 2 * Marge;
        _plan.Height = a.Hauteur + 2 * Marge;

        for (var c = 0; c < a.Cellules; c++)
            Placer(new Rectangle { Width = a.Largeur, Height = a.Hauteur, Fill = Pinceau("#ECEDEA"), Stroke = Pinceau("#5B6670"), StrokeThickness = 6 },
                c * a.Largeur, 0);

        foreach (var b in a.Bandes)
        {
            if (AfficherZones)
            {
                var trait = Pinceau(SchemaArmoire.TraitZone(b.Zone));
                Placer(new Rectangle
                {
                    Width = b.Largeur - 16, Height = b.Hauteur, RadiusX = 6, RadiusY = 6,
                    Fill = Pinceau(SchemaArmoire.CouleurZone(b.Zone)), Stroke = trait, StrokeThickness = 3,
                    StrokeDashArray = new DoubleCollection { 4, 2 }, ToolTip = LibelleEnum.Libelle(b.Zone),
                }, b.X + 8, b.Y);
                if (b.Zone == ZoneCem.Sale)
                    Placer(new Rectangle { Width = b.Largeur - 16, Height = b.Hauteur, RadiusX = 6, RadiusY = 6, Fill = Hachures, IsHitTestVisible = false },
                        b.X + 8, b.Y);
                // Nom de la zone à la verticale dans la marge gauche de la bande, hors des appareils.
                Placer(new TextBlock
                {
                    Text = SchemaArmoire.NomZone(b.Zone), Width = b.Hauteur, TextAlignment = TextAlignment.Center,
                    FontSize = Math.Min(16, b.Hauteur / 7), FontWeight = FontWeights.Bold, Foreground = trait,
                    LayoutTransform = new RotateTransform(-90), IsHitTestVisible = false,
                }, b.X + 8 + (CalculArmoire.MargeLaterale - 8 - 21) / 2, b.Y);
            }
            Placer(new Rectangle { Width = b.Largeur - 2 * CalculArmoire.MargeLaterale, Height = 8, Fill = Pinceau("#A3ACB5"), IsHitTestVisible = false },
                b.X + CalculArmoire.MargeLaterale, b.Y + b.Hauteur / 2 - 4);
        }

        if (AfficherChamps && a.Sources.Count > 0)
            DessinerChamp(a);

        foreach (var x in a.Appareils)
        {
            var bloc = new Border
            {
                Width = x.Largeur, Height = x.Hauteur, CornerRadius = new CornerRadius(3),
                Background = Brushes.White,
                BorderBrush = x.Manuel ? Pinceau("#F5A623") : Pinceau("#37414B"),
                BorderThickness = new Thickness(x.Manuel ? 4 : 2),
                Tag = x,
                ToolTip = $"{x.Repere} – {x.Designation}\n{x.Largeur:0} × {x.Hauteur:0} × {x.Profondeur:0} mm, {x.DissipationW:0.#} W\n" +
                          $"{LibelleEnum.Libelle(x.Zone)}{(x.Manuel ? "\nPlacé à la main" : "")}" +
                          (a.Expositions.TryGetValue(x.Repere, out var h) ? $"\nChamp reçu ≈ {h:0.#} A/m (immunité industrielle {ChampMagnetique.ImmuniteIndustrielle:0} A/m)" : "") +
                          (a.Sources.FirstOrDefault(c => c.Repere == x.Repere) is { } src
                              ? $"\nSource : {src.Courant:0.#} A × k {src.Facteur:0.#} ; 30 A/m jusqu'à {src.Rayon(30):0} mm"
                              : ""),
                Child = new Viewbox
                {
                    Margin = new Thickness(2), StretchDirection = StretchDirection.DownOnly,
                    Child = new TextBlock { Text = x.Repere, FontWeight = FontWeights.Bold, FontSize = 22, Foreground = Pinceau("#1D232A") },
                },
            };
            if (Interactif)
            {
                bloc.Cursor = Cursors.SizeAll;
                bloc.MouseLeftButtonDown += Saisir;
            }
            Placer(bloc, x.X, x.Y);
        }
    }

    /// <summary>Hachures obliques de la zone sale : dégradé répété à bords francs.</summary>
    static readonly Brush Hachures = CreerHachures();

    static Brush CreerHachures()
    {
        var couleur = Color.FromArgb(70, 0xC9, 0x2A, 0x2A);
        var pinceau = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new Point(0, 0), EndPoint = new Point(10, 10),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.75),
                new GradientStop(couleur, 0.75), new GradientStop(couleur, 1),
            },
        };
        pinceau.Freeze();
        return pinceau;
    }

    /// <summary>Isolignes du champ magnétique (10, 30 et 100 A/m) et leur niveau écrit à côté.</summary>
    void DessinerChamp(ResultatArmoire a)
    {
        var grille = ChampMagnetique.Grille(a.Sources, a.LargeurTotale, a.Hauteur, SchemaArmoire.PasChamp);
        var etiquettes = new List<(double X, double Y)>();
        foreach (var (niveau, couleur, libelle) in ChampMagnetique.Isolignes)
        {
            var segments = ChampMagnetique.Isoligne(grille, SchemaArmoire.PasChamp, niveau);
            if (segments.Count == 0) continue;
            var geometrie = new StreamGeometry();
            using (var g = geometrie.Open())
                foreach (var (x1, y1, x2, y2) in segments)
                {
                    g.BeginFigure(new Point(x1, y1), false, false);
                    g.LineTo(new Point(x2, y2), true, false);
                }
            geometrie.Freeze();
            var trait = Pinceau(couleur);
            Placer(new System.Windows.Shapes.Path
            {
                Data = geometrie, Stroke = trait, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, ToolTip = libelle,
            }, 0, 0);
            if (ChampMagnetique.PointEtiquette(segments, a.LargeurTotale, a.Hauteur, etiquettes) is { } p)
                Placer(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 0, 3, 0), IsHitTestVisible = false,
                    Child = new TextBlock { Text = $"{niveau:0} A/m", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = trait },
                }, p.X + 6, p.Y - 11);
        }
    }

    void Placer(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x + Marge);
        Canvas.SetTop(element, y + Marge);
        _plan.Children.Add(element);
    }

    void Saisir(object sender, MouseButtonEventArgs e)
    {
        _glisse = (Border)sender;
        _depart = e.GetPosition(_plan);
        _origine = new Point(Canvas.GetLeft(_glisse), Canvas.GetTop(_glisse));
        Panel.SetZIndex(_glisse, 100);
        _glisse.Opacity = 0.85;
        _plan.CaptureMouse();
        e.Handled = true;
    }

    void Deplacer(object sender, MouseEventArgs e)
    {
        if (_glisse is null) return;
        var p = e.GetPosition(_plan);
        Canvas.SetLeft(_glisse, Arrondir(_origine.X + p.X - _depart.X));
        Canvas.SetTop(_glisse, Arrondir(_origine.Y + p.Y - _depart.Y));
    }

    void Deposer(object sender, MouseButtonEventArgs e)
    {
        if (_glisse is not { Tag: AppareilImplante appareil } bloc) return;
        _plan.ReleaseMouseCapture();
        _glisse = null;
        bloc.Opacity = 1;
        var x = Canvas.GetLeft(bloc) - Marge;
        var y = Canvas.GetTop(bloc) - Marge;
        if (Math.Abs(x - appareil.X) < 1 && Math.Abs(y - appareil.Y) < 1) return;
        var deplacement = new DeplacementAppareil(appareil.Repere, x, y);
        if (DeplacerCommande?.CanExecute(deplacement) == true) DeplacerCommande.Execute(deplacement);
    }

    static double Arrondir(double v) => Math.Round(v / Pas) * Pas;
}
