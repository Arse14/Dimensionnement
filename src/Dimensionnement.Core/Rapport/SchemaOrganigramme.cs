using System.Globalization;
using System.Security;
using System.Text;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Organigramme du matériel au format SVG : l'arrivée en tête, un jeu de barres, puis une colonne par départ
/// groupée par rubrique, de l'amont vers l'aval. La commande (automate, sécurité) est à droite, hors puissance.
/// </summary>
public static class SchemaOrganigramme
{
    const double Boite = 200;
    const double Hauteur = 44;
    const double Ecart = 20;
    const double Colonne = 24;
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

    public static string Svg(ResultatProjet r)
    {
        var groupes = SyntheseMateriel.Grouper(r);
        var arrivee = groupes[0].Cartes[0];
        var puissance = groupes.Skip(1).Where(g => g.Titre != SyntheseMateriel.TitreCommande).ToList();
        var commande = groupes.Skip(1).FirstOrDefault(g => g.Titre == SyntheseMateriel.TitreCommande);

        // Position horizontale de chaque groupe, puis de chaque colonne.
        var x = Marge;
        var placements = new List<(GroupeMateriel Groupe, double X, double Largeur, List<(CarteMateriel Carte, double X)> Colonnes)>();
        foreach (var g in puissance.Concat(commande is null ? Array.Empty<GroupeMateriel>() : new[] { commande }))
        {
            if (g == commande) x += 2 * Colonne;
            var debut = x;
            var colonnes = new List<(CarteMateriel, double)>();
            foreach (var c in g.Cartes)
            {
                colonnes.Add((c, x));
                x += Largeur(c) + Colonne;
            }
            placements.Add((g, debut, x - Colonne - debut, colonnes));
            x += Colonne;
        }
        var largeurTotale = Math.Max(x - Colonne + Marge, Boite + 2 * Marge + 260);

        var lignesArrivee = arrivee.Chaine.Count;
        var yBarre = Marge + 40 + lignesArrivee * (Hauteur + Ecart) + 10;
        var yGroupe = yBarre + 30;
        var yCarte = yGroupe + 34;
        var lignes = placements.SelectMany(p => p.Colonnes).Select(c => Lignes(c.Carte)).DefaultIfEmpty(0).Max();
        var hauteurTotale = yCarte + Hauteur + 10 + lignes * (Hauteur + Ecart) + Marge;

        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(largeurTotale)}\" height=\"{F(hauteurTotale)}\" " +
                 $"viewBox=\"0 0 {F(largeurTotale)} {F(hauteurTotale)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
        s.Append($"<rect width=\"{F(largeurTotale)}\" height=\"{F(hauteurTotale)}\" fill=\"#ffffff\"/>");

        // Arrivée : centrée au-dessus du jeu de barres de puissance.
        var finPuissance = placements.Where(p => p.Groupe != commande).Select(p => p.X + p.Largeur).DefaultIfEmpty(Marge + Boite).Max();
        var xArrivee = Math.Max(Marge, (Marge + finPuissance) / 2 - Boite / 2);
        Texte(s, xArrivee + Boite / 2, Marge + 14, "ARRIVÉE", 13, "#1f6feb", "middle", true);
        var y = Marge + 30;
        foreach (var m in arrivee.Chaine)
        {
            Appareil(s, m.Element, xArrivee, y);
            Trait(s, xArrivee + Boite / 2, y + Hauteur, xArrivee + Boite / 2, y + Hauteur + Ecart);
            y += Hauteur + Ecart;
        }

        // Jeu de barres puis une descente par départ.
        var colonnesPuissance = placements.Where(p => p.Groupe != commande).SelectMany(p => p.Colonnes).ToList();
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
            var estCommande = groupe == commande;
            s.Append($"<rect x=\"{F(gx - 8)}\" y=\"{F(yGroupe)}\" width=\"{F(gl + 16)}\" height=\"{F(hauteurTotale - yGroupe - Marge + 10)}\" rx=\"8\" " +
                     $"fill=\"{(estCommande ? "#f3f0fa" : "#f2f5f8")}\" stroke=\"#d5dbe1\"/>");
            Texte(s, gx, yGroupe + 18, groupe.Titre, 12, estCommande ? "#6f42c1" : "#1f6feb", "start", true);
            foreach (var (carte, cx) in colonnes) Carte(s, carte, cx, yCarte);
        }

        s.Append("</svg>");
        return s.ToString();
    }

    /// <summary>Nombre de lignes d'appareils sous l'en-tête d'une carte (les appareils en parallèle partagent la ligne de leur homologue).</summary>
    static int Lignes(CarteMateriel c) => c.Chaine.Count(m => !m.EnParallele);

    static void Carte(StringBuilder s, CarteMateriel carte, double x, double y)
    {
        // En-tête : repère et désignation du départ, type, liseré de statut.
        var titre = carte.Titre;
        s.Append($"<g><title>{E(titre)}{(carte.SousTitre.Length > 0 ? " – " + E(carte.SousTitre) : "")}</title>");
        s.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(Boite)}\" height=\"{F(Hauteur)}\" rx=\"5\" fill=\"#24303c\"/>");
        s.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"6\" height=\"{F(Hauteur)}\" rx=\"2\" fill=\"{CouleurStatut(carte.Statut)}\"/>");
        Texte(s, x + 14, y + 18, Couper(titre, 26), 12.5, "#ffffff", "start", true);
        Texte(s, x + 14, y + 35, Couper(carte.SousTitre, 31), 11, "#b7c1cb", "start", false);
        s.Append("</g>");

        var yl = y + Hauteur;
        double? yPrecedent = null;
        foreach (var m in carte.Chaine)
        {
            if (m.EnParallele && yPrecedent is { } yp)
            {
                var xp = x + Boite + Parallele;
                s.Append($"<line x1=\"{F(x + Boite)}\" y1=\"{F(yp + Hauteur / 2)}\" x2=\"{F(xp)}\" y2=\"{F(yp + Hauteur / 2)}\" stroke=\"#6e7681\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\"/>");
                Appareil(s, m.Element, xp, yp);
                continue;
            }
            Trait(s, x + Boite / 2, yl, x + Boite / 2, yl + Ecart);
            yl += Ecart;
            Appareil(s, m.Element, x, yl);
            yPrecedent = yl;
            yl += Hauteur;
        }
    }

    static void Appareil(StringBuilder s, ElementMateriel e, double x, double y)
    {
        s.Append($"<g><title>{E(e.Repere)} – {E(e.Designation)}{(e.Calibre.Length > 0 ? " – " + E(e.Calibre) : "")}" +
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
