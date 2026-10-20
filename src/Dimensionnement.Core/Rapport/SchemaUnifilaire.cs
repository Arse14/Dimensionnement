using System.Globalization;
using System.Security;
using System.Text;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Schéma unifilaire de puissance au format SVG : arrivée, sectionneur et disjoncteur général, jeu de barres, puis une
/// colonne par départ jusqu'au récepteur. Symboles simplifiés inspirés de l'IEC 60617.
/// </summary>
public static class SchemaUnifilaire
{
    const double Colonne = 175;
    const double Pas = 74;
    const double Marge = 30;
    const double XArrivee = 90;

    public static string Svg(ResultatProjet r)
    {
        var corps = new StringBuilder();
        var reseau = r.Projet.Reseau;

        // Arrivée : source, câble, sectionneur, disjoncteur général.
        var y = 60.0;
        Texte(corps, Marge, 28, $"Arrivée {CalculProjet.Libelle(reseau.Alimentation)}, {reseau.Slt}, Icc {Format.N(reseau.IccKiloAmperes)} kA", 12, true, "start");
        var tete = r.Arbre;
        while (tete.Enfants.Count > 0 && tete.Repere != "QG")
        {
            tete = tete.Enfants[0];
            y += Symbole(corps, tete, XArrivee, y);
        }

        // Jeu de barres.
        var departs = r.Departs;
        var largeur = Math.Max(2 * XArrivee + 260, Marge + departs.Count * Colonne + Marge);
        var yBarre = y + 10;
        Ligne(corps, XArrivee, y, XArrivee, yBarre);
        var xFin = Marge + Math.Max(1, departs.Count) * Colonne - Colonne / 2;
        corps.Append($"<line x1=\"{F(Math.Min(XArrivee, Marge + Colonne / 2) - 10)}\" y1=\"{F(yBarre)}\" x2=\"{F(Math.Max(xFin, XArrivee) + 10)}\" y2=\"{F(yBarre)}\" stroke=\"#000\" stroke-width=\"5\"/>");
        Texte(corps, Math.Max(xFin, XArrivee) + 16, yBarre + 4, "Jeu de barres", 11, false, "start");

        var bas = yBarre;
        for (var i = 0; i < departs.Count; i++)
        {
            var d = departs[i];
            var x = Marge + i * Colonne + Colonne / 2;
            var yd = yBarre;
            ElementMateriel? e = d.Materiel;
            Ligne(corps, x, yBarre, x, yBarre + 14);
            yd += 14;
            while (e is not null)
            {
                yd += Symbole(corps, e, x, yd);
                var suivant = Principal(e);
                foreach (var autre in e.Enfants.Where(c => c != suivant && Dessinable(c)))
                {
                    Texte(corps, x + 18, yd + 6, Court($"∥ {autre.Repere} {autre.Designation}", 30), 9, false, "start");
                    yd += 14;
                }
                e = suivant is not null && Dessinable(suivant) ? suivant : null;
            }
            Texte(corps, x, yd + 14, $"D{d.Depart.Numero:00}", 12, true, "middle");
            if (d.Depart.Designation.Length > 0)
                Texte(corps, x, yd + 28, Court(d.Depart.Designation, 26), 10, false, "middle");
            bas = Math.Max(bas, yd + 40);
        }

        var hauteur = bas + 40;
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(largeur)}\" height=\"{F(hauteur)}\" viewBox=\"0 0 {F(largeur)} {F(hauteur)}\" ");
        svg.Append("font-family=\"Segoe UI, Arial, sans-serif\" fill=\"none\" stroke-linecap=\"round\">");
        svg.Append($"<rect width=\"{F(largeur)}\" height=\"{F(hauteur)}\" fill=\"#fff\"/>");
        svg.Append(corps);
        Texte(svg, largeur - 12, hauteur - 14, $"{r.Projet.Nom} – indice {r.Projet.Indice} – schéma unifilaire indicatif", 10, false, "end");
        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Appareils représentés : la partie commande (automate, sécurité) n'est pas sur le schéma de puissance.</summary>
    static bool Dessinable(ElementMateriel e) => e.Famille is not ("Automate" or "Entrées/sorties" or "Sécurité");

    /// <summary>Enfant qui continue la ligne de puissance : celui qui mène au câble ou au récepteur.</summary>
    static ElementMateriel? Principal(ElementMateriel e) =>
        e.Enfants.FirstOrDefault(c => c.Parcourir().Any(x => x.Famille is "Câble" or "Moteur" or "Récepteur" or "Prise de courant"))
        ?? e.Enfants.FirstOrDefault(Dessinable);

    /// <summary>Dessine le symbole de l'appareil sous le point (x, y) ; renvoie la hauteur occupée.</summary>
    static double Symbole(StringBuilder s, ElementMateriel e, double x, double y)
    {
        switch (e.Famille)
        {
            case "Câble":
                Ligne(s, x, y, x, y + 50);
                s.Append($"<path d=\"M{F(x - 6)} {F(y + 22)} l12 -6 M{F(x - 6)} {F(y + 30)} l12 -6\" stroke=\"#000\"/>");
                Etiquette(s, e, x, y + 20);
                return 50;
            case "Moteur":
                Cercle(s, x, y + 24, 20);
                Texte(s, x, y + 24, "M", 13, true, "middle");
                Texte(s, x, y + 36, e.Poles == 2 ? "1~" : "3~", 9, false, "middle");
                Ligne(s, x, y, x, y + 4);
                Etiquette(s, e, x + 8, y + 18);
                return 48;
            case "Récepteur":
                Ligne(s, x, y, x, y + 8);
                s.Append($"<rect x=\"{F(x - 8)}\" y=\"{F(y + 8)}\" width=\"16\" height=\"34\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                Etiquette(s, e, x, y + 20);
                return 46;
            case "Prise de courant":
                Ligne(s, x, y, x, y + 14);
                s.Append($"<path d=\"M{F(x - 14)} {F(y + 30)} a14 14 0 0 1 28 0 M{F(x - 16)} {F(y + 30)} h32\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                Etiquette(s, e, x, y + 20);
                return 36;
            case "Fusible":
                Ligne(s, x, y, x, y + 46);
                s.Append($"<rect x=\"{F(x - 6)}\" y=\"{F(y + 8)}\" width=\"12\" height=\"30\" stroke=\"#000\" stroke-width=\"1.5\" fill=\"#fff\"/>");
                Ligne(s, x, y + 8, x, y + 38);
                Etiquette(s, e, x, y + 20);
                return 46;
            case "Relais thermique":
                Ligne(s, x, y, x, y + 8);
                s.Append($"<rect x=\"{F(x - 12)}\" y=\"{F(y + 8)}\" width=\"24\" height=\"28\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                s.Append($"<path d=\"M{F(x)} {F(y + 8)} v6 h-5 v8 h10 v8 h-5 v6\" stroke=\"#000\"/>");
                Ligne(s, x, y + 36, x, y + 46);
                Etiquette(s, e, x, y + 20);
                return 46;
            case "Variateur":
            case "Démarreur progressif":
            case "Alimentation":
                Ligne(s, x, y, x, y + 6);
                s.Append($"<rect x=\"{F(x - 20)}\" y=\"{F(y + 6)}\" width=\"40\" height=\"40\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                s.Append($"<line x1=\"{F(x - 20)}\" y1=\"{F(y + 46)}\" x2=\"{F(x + 20)}\" y2=\"{F(y + 6)}\" stroke=\"#000\"/>");
                var (haut, bas) = e.Famille switch { "Variateur" => ("~", "~"), "Alimentation" => ("~", "="), _ => ("~", "DP") };
                Texte(s, x - 10, y + 22, haut, 11, false, "middle");
                Texte(s, x + 10, y + 42, bas, 10, false, "middle");
                Ligne(s, x, y + 46, x, y + 56);
                Etiquette(s, e, x + 6, y + 22);
                return 56;
            default:
                return Contact(s, e, x, y);
        }
    }

    /// <summary>Contact de puissance : disjoncteur (croix), contacteur (demi-cercle), sectionneur (barre).</summary>
    static double Contact(StringBuilder s, ElementMateriel e, double x, double y)
    {
        Ligne(s, x, y, x, y + 16);
        s.Append($"<line x1=\"{F(x)}\" y1=\"{F(y + 48)}\" x2=\"{F(x - 15)}\" y2=\"{F(y + 18)}\" stroke=\"#000\" stroke-width=\"1.8\"/>");
        Ligne(s, x, y + 48, x, y + 58);
        switch (e.Famille)
        {
            case "Disjoncteur":
            case "Disjoncteur moteur":
                s.Append($"<path d=\"M{F(x - 4)} {F(y + 12)} l8 8 M{F(x + 4)} {F(y + 12)} l-8 8\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                if (e.Famille == "Disjoncteur moteur")
                    s.Append($"<rect x=\"{F(x - 34)}\" y=\"{F(y + 22)}\" width=\"14\" height=\"14\" stroke=\"#000\"/>" +
                             $"<path d=\"M{F(x - 27)} {F(y + 22)} v3 h-3 v4 h6 v4 h-3 v3\" stroke=\"#000\"/>");
                break;
            case "Contacteur":
                s.Append($"<path d=\"M{F(x - 5)} {F(y + 16)} a5 5 0 0 0 10 0\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                break;
            default:
                s.Append($"<line x1=\"{F(x - 6)}\" y1=\"{F(y + 16)}\" x2=\"{F(x + 6)}\" y2=\"{F(y + 16)}\" stroke=\"#000\" stroke-width=\"1.8\"/>");
                break;
        }
        Etiquette(s, e, x, y + 26);
        return 58;
    }

    static void Etiquette(StringBuilder s, ElementMateriel e, double x, double y)
    {
        Texte(s, x + 18, y, e.Repere, 11, true, "start");
        Texte(s, x + 18, y + 13, Court(e.Calibre, 24), 9, false, "start");
        if (e.Reference.Length > 0 && e.Famille != "Câble")
            Texte(s, x + 18, y + 25, Court(e.Reference, 24), 8, false, "start", "#555");
    }

    static void Ligne(StringBuilder s, double x1, double y1, double x2, double y2) =>
        s.Append($"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"#000\" stroke-width=\"1.5\"/>");

    static void Cercle(StringBuilder s, double x, double y, double r) =>
        s.Append($"<circle cx=\"{F(x)}\" cy=\"{F(y)}\" r=\"{F(r)}\" stroke=\"#000\" stroke-width=\"1.5\"/>");

    static void Texte(StringBuilder s, double x, double y, string texte, double taille, bool gras, string ancre, string couleur = "#000") =>
        s.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"{F(taille)}\" text-anchor=\"{ancre}\" dominant-baseline=\"middle\" " +
                 $"fill=\"{couleur}\"{(gras ? " font-weight=\"bold\"" : "")}>{SecurityElement.Escape(texte)}</text>");

    static string Court(string texte, int longueur) => texte.Length <= longueur ? texte : texte[..(longueur - 1)] + "…";

    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
