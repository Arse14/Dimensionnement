namespace Dimensionnement.Core.Armoire;

/// <summary>
/// Source de champ magnétique sur la plaque : centre en mm, courant en A, facteur d'émission sans dimension et écart
/// équivalent des conducteurs en mm. Le facteur majore le courant fondamental pour les harmoniques et les fronts de
/// commutation (MLI, thyristors, découpage).
/// </summary>
public record SourceChamp(string Repere, string Famille, double X, double Y, double Courant, double Facteur, double Ecart, double RayonProche)
{
    /// <summary>Courant équivalent rayonnant, en A.</summary>
    public double Equivalent => Courant * Facteur;

    /// <summary>
    /// Champ au point (x, y) en mm, en A/m ; plafonné au contact de l'appareil. Les conducteurs aller et retour
    /// (ou les trois phases) se compensent en partie : le champ décroît en 1/d², H = k·I·e / (2π·d²).
    /// </summary>
    public double H(double x, double y)
    {
        var d = Math.Max(Math.Sqrt((x - X) * (x - X) + (y - Y) * (y - Y)), RayonProche) / 1000;
        return Equivalent * Ecart / 1000 / (2 * Math.PI * d * d);
    }

    /// <summary>Distance en mm jusqu'à laquelle cette source seule dépasse le niveau donné en A/m.</summary>
    public double Rayon(double niveau) => Math.Sqrt(Equivalent * Ecart / 1000 / (2 * Math.PI * niveau)) * 1000;
}

/// <summary>
/// Champ magnétique indicatif dans l'armoire. Chaque source est vue comme un faisceau de conducteurs d'écart e,
/// H = k·I·e / (2π·d²), et les contributions sont sommées sans tenir compte des phases, ce qui majore le champ réel.
/// Les seuils sont les niveaux d'essai d'immunité de la NF EN 61000-4-8.
/// </summary>
public static class ChampMagnetique
{
    /// <summary>Niveaux d'essai NF EN 61000-4-8 (niveaux 2 à 5), en A/m.</summary>
    public static readonly double[] Niveaux = { 3, 10, 30, 100 };

    /// <summary>Immunité exigée en environnement industriel (NF EN 61000-6-2), en A/m.</summary>
    public const double ImmuniteIndustrielle = 30;

    public const string Reference = "NF EN 61000-4-8 (niveaux d'essai) ; NF EN 61000-6-2 (immunité industrielle 30 A/m) ; NF EN 61800-3";

    /// <summary>Entraxe des pôles d'un appareil modulaire, en mm.</summary>
    public const double EntraxePoles = 25;

    /// <summary>Facteur d'émission par famille ; null si l'appareil n'est pas une source notable.</summary>
    public static double? Facteur(string famille) => famille switch
    {
        // Courant de sortie + harmoniques d'entrée (THDi 30 à 80 % sans self) + composantes HF de la MLI.
        "Variateur" => 3.0,
        // Harmoniques des thyristors pendant la rampe, contacteur de court-circuitage ensuite.
        "Démarreur progressif" => 2.0,
        // Courant de ligne fondamental, coupure avec arc.
        "Contacteur" => 1.0,
        // Alimentation à découpage : courant d'entrée faible mais riche en HF.
        "Alimentation" => 3.0,
        _ => null,
    };

    public static List<SourceChamp> Sources(IEnumerable<AppareilImplante> appareils)
    {
        var sources = new List<SourceChamp>();
        foreach (var a in appareils)
        {
            if (Facteur(a.Famille) is not { } k) continue;
            var e = a.Element;
            var courant = a.Famille == "Alimentation"
                // Courant absorbé côté 230 V : puissance de sortie / (230 V × rendement 0,9 × facteur de puissance 0,6).
                ? (e?.PuissanceKw ?? 0.06) * 1000 / (230 * 0.9 * 0.6)
                : e?.Courant is > 0 ? e.Courant : 1.8 * (e?.PuissanceKw ?? 1);
            // Triphasé : écart équivalent √3 × entraxe ; monophasé (alimentation, 2 pôles) : l'entraxe.
            var ecart = a.Famille == "Alimentation" || e?.Poles == 2 ? EntraxePoles : Math.Sqrt(3) * EntraxePoles;
            sources.Add(new SourceChamp(a.Repere, a.Famille, a.CentreX, a.CentreY, courant, k, ecart,
                Math.Max(Math.Min(a.Largeur, a.Hauteur) / 2, 25)));
        }
        return sources;
    }

    public static double H(IEnumerable<SourceChamp> sources, double x, double y) => sources.Sum(s => s.H(x, y));

    /// <summary>Champ le plus fort reçu par un appareil : maximum sur ses coins, milieux de côtés et centre.</summary>
    public static double Exposition(IEnumerable<SourceChamp> sources, AppareilImplante a, out SourceChamp? principale)
    {
        var liste = sources.Where(s => s.Repere != a.Repere).ToList();
        var max = 0.0;
        principale = null;
        foreach (var fx in new[] { 0, 0.5, 1 })
            foreach (var fy in new[] { 0, 0.5, 1 })
            {
                var x = a.X + fx * a.Largeur;
                var y = a.Y + fy * a.Hauteur;
                var h = H(liste, x, y);
                if (h <= max) continue;
                max = h;
                principale = liste.MaxBy(s => s.H(x, y));
            }
        return max;
    }

    /// <summary>Courbes tracées sur la vue d'armoire : niveau en A/m, couleur, libellé de légende.</summary>
    public static readonly (double Niveau, string Couleur, string Libelle)[] Isolignes =
    {
        (10, "#e67700", "10 A/m"),
        (30, "#c2255c", "30 A/m, immunité industrielle"),
        (100, "#5f3dc4", "100 A/m"),
    };

    /// <summary>Champ aux nœuds d'une grille de pas donné couvrant la plaque ; [colonne, ligne].</summary>
    public static double[,] Grille(IReadOnlyList<SourceChamp> sources, double largeur, double hauteur, double pas)
    {
        var nx = (int)Math.Ceiling(largeur / pas) + 1;
        var ny = (int)Math.Ceiling(hauteur / pas) + 1;
        var grille = new double[nx, ny];
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
                grille[i, j] = H(sources, Math.Min(i * pas, largeur), Math.Min(j * pas, hauteur));
        return grille;
    }

    /// <summary>Segments de l'isoligne d'un niveau, par la méthode des carrés marchants avec interpolation linéaire.</summary>
    public static List<(double X1, double Y1, double X2, double Y2)> Isoligne(double[,] g, double pas, double niveau)
    {
        var segments = new List<(double, double, double, double)>();
        for (var i = 0; i < g.GetLength(0) - 1; i++)
            for (var j = 0; j < g.GetLength(1) - 1; j++)
            {
                // Coins dans l'ordre haut gauche, haut droit, bas droit, bas gauche.
                double a = g[i, j], b = g[i + 1, j], c = g[i + 1, j + 1], d = g[i, j + 1];
                var cas = (a >= niveau ? 8 : 0) | (b >= niveau ? 4 : 0) | (c >= niveau ? 2 : 0) | (d >= niveau ? 1 : 0);
                if (cas is 0 or 15) continue;
                double x = i * pas, y = j * pas;
                (double, double) Haut() => (x + pas * T(a, b), y);
                (double, double) Droite() => (x + pas, y + pas * T(b, c));
                (double, double) Bas() => (x + pas * T(d, c), y + pas);
                (double, double) Gauche() => (x, y + pas * T(a, d));
                double T(double v1, double v2) => Math.Abs(v2 - v1) < 1e-9 ? 0.5 : Math.Clamp((niveau - v1) / (v2 - v1), 0, 1);
                void Ajouter((double X, double Y) p, (double X, double Y) q) => segments.Add((p.X, p.Y, q.X, q.Y));
                switch (cas)
                {
                    case 1 or 14: Ajouter(Gauche(), Bas()); break;
                    case 2 or 13: Ajouter(Bas(), Droite()); break;
                    case 3 or 12: Ajouter(Gauche(), Droite()); break;
                    case 4 or 11: Ajouter(Haut(), Droite()); break;
                    case 6 or 9: Ajouter(Haut(), Bas()); break;
                    case 7 or 8: Ajouter(Gauche(), Haut()); break;
                    case 5: Ajouter(Gauche(), Haut()); Ajouter(Bas(), Droite()); break;
                    case 10: Ajouter(Haut(), Droite()); Ajouter(Gauche(), Bas()); break;
                }
            }
        return segments;
    }

    /// <summary>
    /// Point où écrire le niveau d'une isoligne : le plus à droite possible, dans la plaque et sans chevaucher les
    /// étiquettes déjà placées (boîtes de 80 × 20 mm à droite du point).
    /// </summary>
    public static (double X, double Y)? PointEtiquette(IReadOnlyList<(double X1, double Y1, double X2, double Y2)> segments,
        double largeur, double hauteur, List<(double X, double Y)> placees)
    {
        bool Libre((double X, double Y) p) => placees.All(q => Math.Abs(p.X - q.X) > 85 || Math.Abs(p.Y - q.Y) > 24);
        var point = segments.Select(g => (X: g.X1, Y: g.Y1))
            .Where(p => p.X > 20 && p.X < largeur - 90 && p.Y > 15 && p.Y < hauteur - 15 && Libre(p))
            .OrderByDescending(p => p.X).Cast<(double X, double Y)?>().FirstOrDefault();
        if (point is { } retenu) placees.Add(retenu);
        return point;
    }
}
