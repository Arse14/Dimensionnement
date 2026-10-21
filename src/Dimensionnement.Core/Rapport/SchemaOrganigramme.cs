using System.Globalization;
using System.Security;
using System.Text;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Organigramme du matériel au format SVG : l'arrivée en tête, un jeu de barres, puis une colonne par départ
/// groupée par rubrique, de l'amont vers l'aval. Les appareils d'un domaine de tension (automate, sécurité, IHM en 24 V DC)
/// pendent sous l'alimentation qui les nourrit.
/// </summary>
public static class SchemaOrganigramme
{
    const double Boite = 200;
    const double Hauteur = 44;
    const double Ecart = 20;
    const double Parallele = 14;
    const double Marge = 30;

    public static string CouleurStatut(StatutCalcul s) => s switch
    {
        StatutCalcul.Conforme => "#3fb950",
        StatutCalcul.Forcee or StatutCalcul.Alerte => "#f0a020",
        StatutCalcul.NonConforme => "#f85149",
        _ => "#8b949e",
    };

    static double Largeur(CarteMateriel c) => Boite + (c.Chaine.Any(m => m.EnParallele) ? Parallele + Boite : 0);
    static double Largeur(IReadOnlyList<MaillonMateriel> chaine) => Boite + (chaine.Any(m => m.EnParallele) ? Parallele + Boite : 0);

    /// <summary>
    /// Colonne d'un départ. Quand une alimentation de la chaîne nourrit un domaine de tension, ses appareils pendent sous
    /// elle : un jeu de barres secondaire violet se divise vers la suite de la chaîne puis vers chaque appareil du domaine.
    /// </summary>
    sealed record Colonne(CarteMateriel Carte, GroupeMateriel? Domaine, int IndexSource, List<IReadOnlyList<MaillonMateriel>> SousColonnes)
    {
        public double LargeurSous => SousColonnes.Sum(c => SchemaOrganigramme.Largeur(c)) + ColonneEcart * Math.Max(0, SousColonnes.Count - 1);
        public double Largeur => Domaine is null ? SchemaOrganigramme.Largeur(Carte) : Math.Max(SchemaOrganigramme.Largeur(Avant), LargeurSous);
        public IReadOnlyList<MaillonMateriel> Avant => Domaine is null ? Carte.Chaine : Carte.Chaine.Take(IndexSource + 1).ToList();
        public double HauteurTotale => Domaine is null
            ? Hauteur + Rangs(Carte.Chaine) * (Hauteur + Ecart)
            : Hauteur + Rangs(Avant) * (Hauteur + Ecart) + Ecart + SousColonnes.Select(Rangs).DefaultIfEmpty(0).Max() * (Hauteur + Ecart);
    }

    const double ColonneEcart = 24;

    public static string Svg(ResultatProjet r)
    {
        var groupes = SyntheseMateriel.Grouper(r);
        var arrivee = groupes[0].Cartes[0];
        var rubriques = groupes.Skip(1).Where(g => !g.Domaine).ToList();
        var domaines = groupes.Skip(1).Where(g => g.Domaine).ToList();

        // Un domaine dont l'alimentation est dessinée dans une rubrique pend sous elle ; les autres (alimentation à ajouter)
        // restent un cadre à part à droite.
        var accroches = new Dictionary<ElementMateriel, GroupeMateriel>();
        foreach (var d in domaines)
            if (d.Source is { } source && !d.Cartes.Any(c => c.Chaine.Any(m => m.Element == source))
                && rubriques.Any(g => g.Cartes.Any(c => c.Chaine.Any(m => m.Element == source))))
                accroches.TryAdd(source, d);
        var ordre = rubriques.Concat(domaines.Where(d => !accroches.ContainsValue(d))).ToList();

        Colonne Construire(CarteMateriel c)
        {
            for (var i = 0; i < c.Chaine.Count; i++)
                if (accroches.TryGetValue(c.Chaine[i].Element, out var d))
                {
                    var sous = new List<IReadOnlyList<MaillonMateriel>>();
                    var reste = c.Chaine.Skip(i + 1).ToList();
                    if (reste.Count > 0) sous.Add(reste);
                    sous.AddRange(d.Cartes.Select(k => k.Chaine));
                    return new Colonne(c, d, i, sous);
                }
            return new Colonne(c, null, -1, new());
        }

        // Position horizontale de chaque groupe, puis de chaque colonne.
        var x = Marge;
        var placements = new List<(GroupeMateriel Groupe, double X, double Largeur, List<(Colonne Colonne, double X)> Colonnes)>();
        foreach (var g in ordre)
        {
            if (g.Domaine) x += 2 * ColonneEcart;
            var debut = x;
            var colonnes = new List<(Colonne, double)>();
            foreach (var c in g.Cartes)
            {
                var col = Construire(c);
                colonnes.Add((col, x));
                x += col.Largeur + ColonneEcart;
            }
            placements.Add((g, debut, x - ColonneEcart - debut, colonnes));
            x += ColonneEcart;
        }
        var largeurTotale = Math.Max(x - ColonneEcart + Marge, Boite + 2 * Marge + 260);

        var lignesArrivee = arrivee.Chaine.Count;
        var yBarre = Marge + 40 + lignesArrivee * (Hauteur + Ecart) + 10;
        var yGroupe = yBarre + 30;
        var yCarte = yGroupe + 34;
        var hauteurColonnes = placements.SelectMany(p => p.Colonnes).Select(c => c.Colonne.HauteurTotale).DefaultIfEmpty(Hauteur).Max();
        var hauteurTotale = yCarte + hauteurColonnes + 10 + Marge;

        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(largeurTotale)}\" height=\"{F(hauteurTotale)}\" " +
                 $"viewBox=\"0 0 {F(largeurTotale)} {F(hauteurTotale)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
        s.Append($"<rect width=\"{F(largeurTotale)}\" height=\"{F(hauteurTotale)}\" fill=\"#ffffff\"/>");
        var positions = new Dictionary<ElementMateriel, (double X, double Y)>();

        // Arrivée : centrée au-dessus du jeu de barres de puissance.
        var finPuissance = placements.Where(p => !p.Groupe.Domaine).Select(p => p.X + p.Largeur).DefaultIfEmpty(Marge + Boite).Max();
        var xArrivee = Math.Max(Marge, (Marge + finPuissance) / 2 - Boite / 2);
        Texte(s, xArrivee + Boite / 2, Marge + 14, "ARRIVÉE", 13, "#1f6feb", "middle", true);
        var y = Marge + 30;
        foreach (var m in arrivee.Chaine)
        {
            Appareil(s, m.Element, xArrivee, y, positions);
            Trait(s, xArrivee + Boite / 2, y + Hauteur, xArrivee + Boite / 2, y + Hauteur + Ecart);
            y += Hauteur + Ecart;
        }

        // Jeu de barres puis une descente par départ.
        var colonnesPuissance = placements.Where(p => !p.Groupe.Domaine).SelectMany(p => p.Colonnes).ToList();
        if (colonnesPuissance.Count > 0)
        {
            var gauche = Math.Min(colonnesPuissance[0].X + Boite / 2, xArrivee + Boite / 2);
            var droite = Math.Max(colonnesPuissance[^1].X + Boite / 2, xArrivee + Boite / 2);
            Trait(s, xArrivee + Boite / 2, y - Ecart, xArrivee + Boite / 2, yBarre);
            s.Append($"<line x1=\"{F(gauche)}\" y1=\"{F(yBarre)}\" x2=\"{F(droite)}\" y2=\"{F(yBarre)}\" stroke=\"#37414b\" stroke-width=\"5\" stroke-linecap=\"round\"/>");
            foreach (var (_, cx) in colonnesPuissance) Trait(s, cx + Boite / 2, yBarre, cx + Boite / 2, yCarte);
        }

        foreach (var (groupe, gx, gl, colonnes) in placements)
        {
            var domaine = groupe.Domaine;
            s.Append($"<rect x=\"{F(gx - 8)}\" y=\"{F(yGroupe)}\" width=\"{F(gl + 16)}\" height=\"{F(hauteurTotale - yGroupe - Marge + 10)}\" rx=\"8\" " +
                     $"fill=\"{(domaine ? "#f3f0fa" : "#f2f5f8")}\" stroke=\"#d5dbe1\"/>");
            Texte(s, gx, yGroupe + 18, groupe.Titre, 12, domaine ? Violet : "#1f6feb", "start", true);
            foreach (var (col, cx) in colonnes) DessinerColonne(s, col, cx, yCarte, positions);
        }

        // Domaines restés à part (alimentation à ajouter) : un jeu de barres secondaire en tête du groupe.
        foreach (var (_, _, _, colonnes) in placements.Where(p => p.Groupe.Domaine))
        {
            var yBus = yCarte - 12;
            var gauche = colonnes[0].X + Boite / 2;
            var droite = colonnes[^1].X + Boite / 2;
            s.Append($"<line x1=\"{F(gauche)}\" y1=\"{F(yBus)}\" x2=\"{F(droite)}\" y2=\"{F(yBus)}\" stroke=\"{Violet}\" stroke-width=\"4\" stroke-linecap=\"round\"/>");
            foreach (var (_, cx) in colonnes)
                s.Append($"<line x1=\"{F(cx + Boite / 2)}\" y1=\"{F(yBus)}\" x2=\"{F(cx + Boite / 2)}\" y2=\"{F(yCarte)}\" stroke=\"{Violet}\" stroke-width=\"2\"/>");
        }

        s.Append("</svg>");
        return s.ToString();
    }

    const string Violet = "#6f42c1";

    static void DessinerColonne(StringBuilder s, Colonne col, double x, double y, Dictionary<ElementMateriel, (double X, double Y)> positions)
    {
        var bas = Carte(s, col.Carte, col.Avant, x, y, positions);
        if (col.Domaine is null) return;
        // Jeu de barres secondaire sous l'alimentation, puis une descente par sous-colonne.
        var xs = new List<double>();
        var xc = x;
        foreach (var sous in col.SousColonnes)
        {
            xs.Add(xc);
            xc += Largeur(sous) + ColonneEcart;
        }
        var yBus = bas + Ecart;
        s.Append($"<line x1=\"{F(x + Boite / 2)}\" y1=\"{F(bas)}\" x2=\"{F(x + Boite / 2)}\" y2=\"{F(yBus)}\" stroke=\"{Violet}\" stroke-width=\"2.5\"/>");
        s.Append($"<circle cx=\"{F(x + Boite / 2)}\" cy=\"{F(bas)}\" r=\"3.5\" fill=\"{Violet}\"/>");
        s.Append($"<line x1=\"{F(xs[0] + Boite / 2)}\" y1=\"{F(yBus)}\" x2=\"{F(xs[^1] + Boite / 2)}\" y2=\"{F(yBus)}\" stroke=\"{Violet}\" stroke-width=\"4\" stroke-linecap=\"round\"/>");
        Texte(s, x + Boite / 2 + 8, yBus - 6, col.Domaine.Titre, 11, Violet, "start", true);
        for (var i = 0; i < col.SousColonnes.Count; i++)
            Chaine(s, col.SousColonnes[i], xs[i], yBus, positions, Violet);
    }

    /// <summary>Nombre de rangs d'appareils (les appareils en parallèle partagent le rang de leur homologue).</summary>
    static int Rangs(IReadOnlyList<MaillonMateriel> chaine) => chaine.Count(m => !m.EnParallele);

    static double Carte(StringBuilder s, CarteMateriel carte, IReadOnlyList<MaillonMateriel> chaine, double x, double y,
        Dictionary<ElementMateriel, (double X, double Y)> positions)
    {
        // En-tête : repère et désignation du départ, type, liseré de statut.
        var titre = carte.Titre;
        s.Append($"<g><title>{E(titre)}{(carte.SousTitre.Length > 0 ? " – " + E(carte.SousTitre) : "")}</title>");
        s.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(Boite)}\" height=\"{F(Hauteur)}\" rx=\"5\" fill=\"#24303c\"/>");
        s.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"6\" height=\"{F(Hauteur)}\" rx=\"2\" fill=\"{CouleurStatut(carte.Statut)}\"/>");
        Texte(s, x + 14, y + 18, Couper(titre, 26), 12.5, "#ffffff", "start", true);
        Texte(s, x + 14, y + 35, Couper(carte.SousTitre, 31), 11, "#b7c1cb", "start", false);
        s.Append("</g>");
        return Chaine(s, chaine, x, y + Hauteur, positions, null);
    }

    /// <summary>Appareils l'un sous l'autre à partir de <paramref name="y"/> ; renvoie le bas du dernier.</summary>
    static double Chaine(StringBuilder s, IReadOnlyList<MaillonMateriel> chaine, double x, double y,
        Dictionary<ElementMateriel, (double X, double Y)> positions, string? couleurPremier)
    {
        var yl = y;
        double? yPrecedent = null;
        foreach (var m in chaine)
        {
            if (m.EnParallele && yPrecedent is { } yp)
            {
                var xp = x + Boite + Parallele;
                s.Append($"<line x1=\"{F(x + Boite)}\" y1=\"{F(yp + Hauteur / 2)}\" x2=\"{F(xp)}\" y2=\"{F(yp + Hauteur / 2)}\" stroke=\"#6e7681\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\"/>");
                Appareil(s, m.Element, xp, yp, positions);
                continue;
            }
            if (yPrecedent is null && couleurPremier is not null)
                s.Append($"<line x1=\"{F(x + Boite / 2)}\" y1=\"{F(yl)}\" x2=\"{F(x + Boite / 2)}\" y2=\"{F(yl + Ecart)}\" stroke=\"{couleurPremier}\" stroke-width=\"2\"/>");
            else
                Trait(s, x + Boite / 2, yl, x + Boite / 2, yl + Ecart);
            yl += Ecart;
            Appareil(s, m.Element, x, yl, positions);
            yPrecedent = yl;
            yl += Hauteur;
        }
        return yl;
    }

    static void Appareil(StringBuilder s, ElementMateriel e, double x, double y, Dictionary<ElementMateriel, (double X, double Y)> positions)
    {
        positions.TryAdd(e, (x, y));
        // La référence portée par le groupe permet à l'application d'ouvrir la fiche catalogue au clic.
        s.Append($"<g{(e.Reference.Length > 0 ? $" data-reference=\"{E(e.Reference)}\"" : "")}><title>{E(e.Repere)} – {E(e.Designation)}{(e.Calibre.Length > 0 ? " – " + E(e.Calibre) : "")}" +
                 $"{(e.Reference.Length > 0 ? "\n" + E(e.Fabricant) + " " + E(e.Reference) : "")}</title>");
        s.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(Boite)}\" height=\"{F(Hauteur)}\" rx=\"5\" fill=\"#ffffff\" stroke=\"#9aa5b1\"/>");
        s.Append($"<circle cx=\"{F(x + 12)}\" cy=\"{F(y + 14)}\" r=\"4.5\" fill=\"{CouleurStatut(e.Statut)}\"/>");
        Texte(s, x + 22, y + 18, Couper(e.Repere, 10), 12.5, "#1d232a", "start", true);
        Texte(s, x + Boite - 8, y + 18, Couper(e.Calibre, 19), 11.5, "#1f6feb", "end", true);
        Texte(s, x + 10, y + 35, Couper(e.Designation, 32), 11, "#57606a", "start", false);
        s.Append("</g>");
    }

    static void Trait(StringBuilder s, double x1, double y1, double x2, double y2) =>
        s.Append($"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"#37414b\" stroke-width=\"2\"/>");

    static void Texte(StringBuilder s, double x, double y, string texte, double taille, string couleur, string ancre, bool gras) =>
        s.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"{F(taille)}\" fill=\"{couleur}\" text-anchor=\"{ancre}\"" +
                 $"{(gras ? " font-weight=\"600\"" : "")}>{E(texte)}</text>");

    static string Couper(string texte, int max) => texte.Length <= max ? texte : texte[..(max - 1)].TrimEnd() + "…";

    static string E(string texte) => SecurityElement.Escape(texte) ?? "";

    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
