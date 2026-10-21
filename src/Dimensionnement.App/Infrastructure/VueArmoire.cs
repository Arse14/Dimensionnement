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

/// <summary>Enveloppe vers laquelle déporter un appareil : Id vide pour l'armoire principale, Guid.Empty pour un nouveau coffret.</summary>
public record DestinationDeport(Guid? Id, string Libelle);

/// <summary>Demande de déport d'un appareil, ou de tout son départ, vers une enveloppe.</summary>
public record DeportAppareil(string Cle, Guid? DepartId, Guid? Cible, bool ToutLeDepart);

/// <summary>
/// Action sur un rail ou une goulotte ajouté à la main : « Deplacer » (X, Y), « Longueur » (X = variation en mm),
/// « Modele » (X = largeur, Y = hauteur de la goulotte) ou « Supprimer ».
/// </summary>
public record ActionArmoire(Guid Id, string Action, double X = 0, double Y = 0);

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
    public static readonly DependencyProperty ActionCommandeProperty = DependencyProperty.Register(nameof(ActionCommande),
        typeof(ICommand), typeof(VueArmoire));
    public static readonly DependencyProperty DeporterCommandeProperty = DependencyProperty.Register(nameof(DeporterCommande),
        typeof(ICommand), typeof(VueArmoire));
    public static readonly DependencyProperty DestinationsProperty = DependencyProperty.Register(nameof(Destinations),
        typeof(IReadOnlyList<DestinationDeport>), typeof(VueArmoire));
    public ICommand? DeporterCommande { get => (ICommand?)GetValue(DeporterCommandeProperty); set => SetValue(DeporterCommandeProperty, value); }
    public IReadOnlyList<DestinationDeport>? Destinations
    {
        get => (IReadOnlyList<DestinationDeport>?)GetValue(DestinationsProperty);
        set => SetValue(DestinationsProperty, value);
    }
    public static readonly DependencyProperty RenommerCommandeProperty = DependencyProperty.Register(nameof(RenommerCommande),
        typeof(ICommand), typeof(VueArmoire), new PropertyMetadata(null, Redessiner));

    /// <summary>Reçoit l'appareil à renommer (clic droit sur un appareil de la plaque).</summary>
    public ICommand? RenommerCommande { get => (ICommand?)GetValue(RenommerCommandeProperty); set => SetValue(RenommerCommandeProperty, value); }

    public ResultatArmoire? Armoire { get => (ResultatArmoire?)GetValue(ArmoireProperty); set => SetValue(ArmoireProperty, value); }
    public bool AfficherChamps { get => (bool)GetValue(AfficherChampsProperty); set => SetValue(AfficherChampsProperty, value); }
    public bool AfficherZones { get => (bool)GetValue(AfficherZonesProperty); set => SetValue(AfficherZonesProperty, value); }
    public bool Interactif { get => (bool)GetValue(InteractifProperty); set => SetValue(InteractifProperty, value); }
    public ICommand? DeplacerCommande { get => (ICommand?)GetValue(DeplacerCommandeProperty); set => SetValue(DeplacerCommandeProperty, value); }

    /// <summary>Reçoit les <see cref="ActionArmoire"/> sur les rails et goulottes ajoutés à la main.</summary>
    public ICommand? ActionCommande { get => (ICommand?)GetValue(ActionCommandeProperty); set => SetValue(ActionCommandeProperty, value); }

    /// <summary>Épaisseur de la poignée d'un rail ajouté, en mm.</summary>
    const double PoigneeRail = 16;

    readonly Canvas _plan = new();
    FrameworkElement? _glisse;
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
        _plan.Width = a.LargeurTotale + a.LargeurAttente + 2 * Marge;
        _plan.Height = a.Hauteur + 2 * Marge;
        if (a.LargeurAttente > 0)
        {
            // Appareils qui n'entrent pas : à glisser dans l'armoire ou à déporter dans un autre coffret.
            var x0 = a.LargeurTotale + CalculArmoire.EcartAttente / 2;
            Placer(new Rectangle { Width = a.LargeurAttente - CalculArmoire.EcartAttente / 2, Height = a.Hauteur, RadiusX = 8, RadiusY = 8,
                Stroke = Pinceau("#F85149"), StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 6, 3 }, Fill = Pinceau("#22F85149"),
                IsHitTestVisible = false }, x0, 0);
            Placer(new TextBlock { Text = "À PLACER", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Pinceau("#F85149"), IsHitTestVisible = false },
                x0 + 10, 8);
        }

        for (var c = 0; c < a.Cellules; c++)
        {
            Placer(new Rectangle { Width = a.Largeur, Height = a.Hauteur, Fill = Pinceau("#C9CED3"), Stroke = Pinceau("#5B6670"), StrokeThickness = 6,
                ToolTip = $"{a.Marque} {a.Gamme} : {a.Reference}" }, c * a.Largeur, 0);
            if (a.PlaqueLargeur > 0)
                Placer(new Rectangle { Width = a.PlaqueLargeur, Height = a.PlaqueHauteur, Fill = Pinceau("#ECEDEA"), Stroke = Pinceau("#9AA5B1"), StrokeThickness = 2,
                    ToolTip = $"Plaque de montage {a.ReferencePlaque} : {a.PlaqueLargeur:0} × {a.PlaqueHauteur:0} mm utiles" }, c * a.Largeur + a.PlaqueX, a.PlaqueY);
        }

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
                if (b.Rail)
                    Placer(new TextBlock
                {
                    Text = b.Bornier ? "BORNIERS" : SchemaArmoire.NomZone(b.Zone), Width = b.Hauteur, TextAlignment = TextAlignment.Center,
                    FontSize = Math.Min(16, b.Hauteur / 7), FontWeight = FontWeights.Bold, Foreground = trait,
                    LayoutTransform = new RotateTransform(-90), IsHitTestVisible = false,
                }, b.X + 8 + (a.Laterale - 8 - 21) / 2, b.Y);
            }
            if (b.Manuel)
                continue;
            Placer(new Rectangle { Width = b.Largeur - 2 * a.Laterale, Height = 8, Fill = Pinceau("#A3ACB5"), IsHitTestVisible = false },
                b.X + a.Laterale, b.Y + b.Hauteur / 2 - 4);
        }

        foreach (var g in a.Cablage.Goulottes)
        {
            var goulotte = new Border
            {
                Width = g.Largeur, Height = g.Hauteur, Background = g.Verticale ? FentesV : FentesH,
                BorderBrush = Pinceau(g.Manuelle ? "#F5A623" : "#7D8790"), BorderThickness = new Thickness(g.Manuelle ? 3 : 2), Tag = g,
                ToolTip = $"Goulotte {g.Modele.Libelle}, {g.Longueur:0} mm" +
                          (g.Manuelle ? "\nAjoutée à la main : glisser pour déplacer, clic droit pour la modifier"
                                      : $"\n{g.Fils} fils estimés, remplissage {g.Remplissage:0} % (≤ {CalculCablage.RemplissageMax:0} %)"),
                IsHitTestVisible = g.Manuelle,
            };
            if (Interactif && g.Id is { } id) Manipulable(goulotte, id, true);
            Placer(goulotte, g.X, g.Y);
        }

        // Rails ajoutés à la main : poignée qui se déplace à la souris, par-dessus leur bande.
        foreach (var b in a.Bandes.Where(b => b.Manuel))
        {
            var rail = new Border
            {
                Width = b.Largeur, Height = PoigneeRail, Background = Pinceau("#A3ACB5"), BorderBrush = Pinceau("#F5A623"),
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2), Tag = b,
                ToolTip = $"Rail DIN ajouté, {b.Largeur:0} mm, {LibelleEnum.Libelle(b.Zone)}\nGlisser pour déplacer, clic droit pour le modifier",
            };
            if (Interactif) Manipulable(rail, b.Id!.Value, false);
            Placer(rail, b.X, b.Y + b.Hauteur / 2 - PoigneeRail / 2);
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
                var menu = new ContextMenu();
                if (x.Element is { } element && RenommerCommande is { } renommer)
                {
                    var item = new MenuItem { Header = $"Renommer {x.Repere}…" };
                    item.Click += (_, _) => renommer.Execute(element);
                    menu.Items.Add(item);
                }
                if (DeporterCommande is { } deporter && Destinations is { Count: > 0 } destinations)
                {
                    var cle = CalculArmoire.CleAffectation(x);
                    var depart = x.Element?.DepartId;
                    MenuItem Sous(string titre, bool tout)
                    {
                        var parent = new MenuItem { Header = titre };
                        foreach (var d in destinations.Where(d => d.Id != a.Id))
                        {
                            var item = new MenuItem { Header = d.Libelle };
                            var demande = new DeportAppareil(cle, depart, d.Id, tout);
                            item.Click += (_, _) => { if (deporter.CanExecute(demande)) deporter.Execute(demande); };
                            parent.Items.Add(item);
                        }
                        return parent;
                    }
                    if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                    menu.Items.Add(Sous($"Déporter {x.Repere} vers", false));
                    if (depart is not null) menu.Items.Add(Sous("Déporter tout son départ vers", true));
                }
                if (menu.Items.Count > 0) bloc.ContextMenu = menu;
            }
            Placer(bloc, x.X, x.Y);
        }

        // IHM encastrées dans la porte : en pointillés par-dessus la plaque, vues à travers la porte fermée.
        foreach (var x in a.Porte)
        {
            var cadre = new Border
            {
                Width = x.Largeur, Height = x.Hauteur, CornerRadius = new CornerRadius(6),
                Background = Pinceau("#331F6FEB"), Tag = "Porte",
                ToolTip = $"{x.Repere} – {x.Designation}\nEn porte : façade {x.Largeur:0} × {x.Hauteur:0} mm, {x.Profondeur:0} mm à l'intérieur, {x.DissipationW:0.#} W",
                Child = new Grid
                {
                    Children =
                    {
                        new System.Windows.Shapes.Rectangle
                        {
                            Stroke = Pinceau("#58A6FF"), StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 5, 3 }, RadiusX = 6, RadiusY = 6,
                        },
                        new Viewbox
                        {
                            Margin = new Thickness(6), StretchDirection = StretchDirection.DownOnly,
                            Child = new TextBlock { Text = $"{x.Repere} (porte)", FontWeight = FontWeights.Bold, FontSize = 22, Foreground = Pinceau("#58A6FF") },
                        },
                    },
                },
            };
            Panel.SetZIndex(cadre, 40);
            Placer(cadre, x.X, x.Y);
        }
    }

    /// <summary>Rend un rail ou une goulotte ajouté déplaçable, avec un menu pour sa longueur, son modèle et sa suppression.</summary>
    void Manipulable(FrameworkElement element, Guid id, bool goulotte)
    {
        element.Cursor = Cursors.SizeAll;
        element.MouseLeftButtonDown += Saisir;
        var menu = new ContextMenu();
        void Item(ItemsControl parent, string titre, ActionArmoire action)
        {
            var item = new MenuItem { Header = titre };
            item.Click += (_, _) => Executer(action);
            parent.Items.Add(item);
        }
        Item(menu, "Allonger de 100 mm", new ActionArmoire(id, "Longueur", 100));
        Item(menu, "Raccourcir de 100 mm", new ActionArmoire(id, "Longueur", -100));
        if (goulotte)
        {
            var modeles = new MenuItem { Header = "Modèle de goulotte" };
            foreach (var m in CalculCablage.Modeles) Item(modeles, m.Libelle, new ActionArmoire(id, "Modele", m.Largeur, m.Hauteur));
            menu.Items.Add(modeles);
        }
        menu.Items.Add(new Separator());
        Item(menu, "Supprimer", new ActionArmoire(id, "Supprimer"));
        element.ContextMenu = menu;
    }

    void Executer(ActionArmoire action)
    {
        if (ActionCommande?.CanExecute(action) == true) ActionCommande.Execute(action);
    }

    static readonly Brush FentesH = CreerFentes(false);
    static readonly Brush FentesV = CreerFentes(true);

    /// <summary>Fond de goulotte à fentes : gris clair, dents perpendiculaires à la goulotte.</summary>
    static Brush CreerFentes(bool verticale)
    {
        var dent = Color.FromRgb(0xA6, 0xAF, 0xB8);
        var fond = Color.FromRgb(0xD3, 0xD8, 0xDD);
        var pinceau = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new Point(0, 0), EndPoint = verticale ? new Point(0, 12) : new Point(12, 0),
            GradientStops =
            {
                new GradientStop(fond, 0), new GradientStop(fond, 0.42), new GradientStop(dent, 0.42),
                new GradientStop(dent, 0.58), new GradientStop(fond, 0.58), new GradientStop(fond, 1),
            },
        };
        pinceau.Freeze();
        return pinceau;
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
            if (ChampMagnetique.PointEtiquette(segments, a.LargeurTotale, a.Hauteur, etiquettes, a.Appareils) is { } p)
                Placer(new Border
                {
                    // Au-dessus des appareils, dessinés après le champ.
                    Tag = "Etiquette",
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 0, 3, 0), IsHitTestVisible = false,
                    Child = new TextBlock { Text = $"{niveau:0} A/m", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = trait },
                }, p.X + 6, p.Y - 11);
        }
    }

    void Placer(UIElement element, double x, double y)
    {
        if (element is Border { Tag: "Etiquette" }) Panel.SetZIndex(element, 50);
        Canvas.SetLeft(element, x + Marge);
        Canvas.SetTop(element, y + Marge);
        _plan.Children.Add(element);
    }

    void Saisir(object sender, MouseButtonEventArgs e)
    {
        _glisse = (FrameworkElement)sender;
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
        if (_glisse is not { } bloc) return;
        _plan.ReleaseMouseCapture();
        _glisse = null;
        bloc.Opacity = 1;
        var x = Canvas.GetLeft(bloc) - Marge;
        var y = Canvas.GetTop(bloc) - Marge;
        switch (bloc.Tag)
        {
            case GoulottePlacee { Id: { } goulotte } g when Math.Abs(x - g.X) >= 1 || Math.Abs(y - g.Y) >= 1:
                Executer(new ActionArmoire(goulotte, "Deplacer", x, y));
                return;
            case BandeZone { Id: { } rail }:
                // La poignée est centrée sur l'axe du rail.
                Executer(new ActionArmoire(rail, "Deplacer", x, y + PoigneeRail / 2));
                return;
        }
        if (bloc.Tag is not AppareilImplante appareil) return;
        if (Math.Abs(x - appareil.X) < 1 && Math.Abs(y - appareil.Y) < 1) return;
        var deplacement = new DeplacementAppareil(appareil.Cle, x, y);
        if (DeplacerCommande?.CanExecute(deplacement) == true) DeplacerCommande.Execute(deplacement);
    }

    static double Arrondir(double v) => Math.Round(v / Pas) * Pas;
}
