using System.Globalization;
using System.Security;
using System.Text;
using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Materiel;

namespace Dimensionnement.Core.Rapport;

/// <summary>Vue de face de la plaque de montage au format SVG, en mm : zones CEM, appareils et champs indicatifs.</summary>
public static class SchemaArmoire
{
    public static string CouleurZone(ZoneCem z) => z switch
    {
        ZoneCem.Propre => "#d3f0dc",
        ZoneCem.Sale => "#f6dcd6",
        _ => "#dbe7f6",
    };

    /// <summary>Couleur du contour et du libellé de chaque zone.</summary>
    public static string TraitZone(ZoneCem z) => z switch
    {
        ZoneCem.Propre => "#2f9e44",
        ZoneCem.Sale => "#c92a2a",
        _ => "#1c7ed6",
    };

    public static string NomZone(ZoneCem z) => z switch
    {
        ZoneCem.Propre => "PROPRE",
        ZoneCem.Sale => "SALE",
        _ => "INTERMÉD.",
    };

    /// <summary>Maille de la carte de champ, en mm.</summary>
    public const double PasChamp = 10;

    public static string Svg(ResultatArmoire a, bool champs = true)
    {
        const double marge = 40;
        var largeur = a.LargeurTotale + 2 * marge;
        var hauteur = a.Hauteur + 2 * marge + (champs && a.Sources.Count > 0 ? 340 : 190);
        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(largeur)}\" height=\"{F(hauteur)}\" viewBox=\"0 0 {F(largeur)} {F(hauteur)}\" " +
                 "font-family=\"Segoe UI, Arial, sans-serif\">");
        s.Append("<defs><pattern id=\"hachures\" width=\"14\" height=\"14\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">" +
                 "<line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"14\" stroke=\"#c92a2a\" stroke-width=\"2.5\" stroke-opacity=\"0.28\"/></pattern>" +
                 // Goulottes à fentes : fond gris, dents perpendiculaires à la goulotte.
                 "<pattern id=\"fentesH\" width=\"12\" height=\"12\" patternUnits=\"userSpaceOnUse\"><rect width=\"12\" height=\"12\" fill=\"#d3d8dd\"/>" +
                 "<line x1=\"6\" y1=\"0\" x2=\"6\" y2=\"12\" stroke=\"#a6afb8\" stroke-width=\"2\"/></pattern>" +
                 "<pattern id=\"fentesV\" width=\"12\" height=\"12\" patternUnits=\"userSpaceOnUse\"><rect width=\"12\" height=\"12\" fill=\"#d3d8dd\"/>" +
                 "<line x1=\"0\" y1=\"6\" x2=\"12\" y2=\"6\" stroke=\"#a6afb8\" stroke-width=\"2\"/></pattern></defs>");
        s.Append($"<rect width=\"{F(largeur)}\" height=\"{F(hauteur)}\" fill=\"#fff\"/>");
        s.Append($"<g transform=\"translate({F(marge)},{F(marge)})\">");

        for (var c = 0; c < a.Cellules; c++)
        {
            s.Append($"<rect x=\"{F(c * a.Largeur)}\" y=\"0\" width=\"{F(a.Largeur)}\" height=\"{F(a.Hauteur)}\" fill=\"#d5d9dd\" stroke=\"#37414b\" stroke-width=\"4\"/>");
            if (a.PlaqueLargeur > 0)
                s.Append($"<rect x=\"{F(c * a.Largeur + a.PlaqueX)}\" y=\"{F(a.PlaqueY)}\" width=\"{F(a.PlaqueLargeur)}\" height=\"{F(a.PlaqueHauteur)}\" fill=\"#f7f7f5\" stroke=\"#9aa5b1\" stroke-width=\"2\"/>");
        }
        foreach (var b in a.Bandes)
        {
            var trait = TraitZone(b.Zone);
            s.Append($"<rect x=\"{F(b.X + 8)}\" y=\"{F(b.Y)}\" width=\"{F(b.Largeur - 16)}\" height=\"{F(b.Hauteur)}\" fill=\"{CouleurZone(b.Zone)}\" " +
                     $"stroke=\"{trait}\" stroke-width=\"3\" stroke-dasharray=\"14 6\" rx=\"6\"/>");
            if (b.Zone == ZoneCem.Sale)
                s.Append($"<rect x=\"{F(b.X + 8)}\" y=\"{F(b.Y)}\" width=\"{F(b.Largeur - 16)}\" height=\"{F(b.Hauteur)}\" fill=\"url(#hachures)\" rx=\"6\"/>");
            var rail = b.Y + b.Hauteur / 2;
            var retrait = b.Manuel ? 0 : a.Laterale;
            s.Append($"<line x1=\"{F(b.X + retrait)}\" y1=\"{F(rail)}\" x2=\"{F(b.X + b.Largeur - retrait)}\" y2=\"{F(rail)}\" stroke=\"#9aa5b1\" stroke-width=\"6\"/>");
            if (!b.Rail || b.Manuel) continue;
            // Nom de la zone à la verticale dans la marge gauche de la bande, hors des appareils.
            var cx = b.X + 8 + (a.Laterale - 8) / 2;
            s.Append($"<text x=\"{F(cx)}\" y=\"{F(rail)}\" transform=\"rotate(-90 {F(cx)} {F(rail)})\" font-size=\"{F(Math.Min(16, b.Hauteur / 7))}\" " +
                     $"font-weight=\"bold\" fill=\"{trait}\" text-anchor=\"middle\" dominant-baseline=\"middle\" letter-spacing=\"1\">{(b.Bornier ? "BORNIERS" : NomZone(b.Zone))}</text>");
        }
        foreach (var g in a.Cablage.Goulottes)
            s.Append($"<rect x=\"{F(g.X)}\" y=\"{F(g.Y)}\" width=\"{F(g.Largeur)}\" height=\"{F(g.Hauteur)}\" fill=\"url(#fentes{(g.Verticale ? "V" : "H")})\" " +
                     $"stroke=\"{(g.Manuelle ? "#f5a623" : "#7d8790")}\" stroke-width=\"2\"><title>Goulotte {g.Modele.Libelle}" +
                     (g.Manuelle ? " (ajoutée à la main)" : $", {g.Fils} fils, remplissage {F(g.Remplissage)} %") + "</title></rect>");
        var etiquettes = new StringBuilder();
        if (champs && a.Sources.Count > 0)
            CarteChamp(s, etiquettes, a);
        foreach (var e in a.Appareils)
        {
            var bord = e.Manuel ? "#f5a623" : "#37414b";
            s.Append($"<rect x=\"{F(e.X)}\" y=\"{F(e.Y)}\" width=\"{F(e.Largeur)}\" height=\"{F(e.Hauteur)}\" rx=\"3\" fill=\"#ffffff\" stroke=\"{bord}\" stroke-width=\"{(e.Manuel ? 4 : 2)}\"/>");
            var taille = Math.Clamp(Math.Min(e.Largeur / 3.2, e.Hauteur / 3), 7, 22);
            s.Append($"<text x=\"{F(e.CentreX)}\" y=\"{F(e.CentreY)}\" font-size=\"{F(taille)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" " +
                     $"font-weight=\"bold\" fill=\"#1d232a\">{SecurityElement.Escape(e.Repere)}</text>");
        }
        // IHM encastrées dans la porte : en pointillés, vues à travers la porte fermée.
        foreach (var e in a.Porte)
        {
            s.Append($"<rect x=\"{F(e.X)}\" y=\"{F(e.Y)}\" width=\"{F(e.Largeur)}\" height=\"{F(e.Hauteur)}\" rx=\"6\" fill=\"#1f6feb22\" " +
                     $"stroke=\"#1f6feb\" stroke-width=\"3\" stroke-dasharray=\"10 6\"><title>{SecurityElement.Escape(e.Repere)} – " +
                     $"{SecurityElement.Escape(e.Designation)}, en porte</title></rect>");
            s.Append($"<text x=\"{F(e.CentreX)}\" y=\"{F(e.CentreY)}\" font-size=\"{F(Math.Clamp(e.Largeur / 8, 10, 24))}\" text-anchor=\"middle\" " +
                     $"dominant-baseline=\"middle\" font-weight=\"bold\" fill=\"#1f6feb\">{SecurityElement.Escape(e.Repere)} (porte)</text>");
        }

        s.Append(etiquettes);
        var y = a.Hauteur + 36;
        const double x = 0;
        s.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"22\" font-weight=\"bold\" dominant-baseline=\"middle\" fill=\"#1d232a\">" +
                 $"{SecurityElement.Escape(a.Marque)} {SecurityElement.Escape(a.Reference)}" +
                 (a.Reference.Contains('×') ? "" : $" – {F(a.Largeur)} × {F(a.Hauteur)} × {F(a.Profondeur)} mm") +
                 $"{(a.Cellules > 1 ? $" × {a.Cellules} cellules" : "")}</text>");
        y += 34;
        foreach (var zone in new[] { ZoneCem.Propre, ZoneCem.Intermediaire, ZoneCem.Sale })
        {
            s.Append($"<rect x=\"{F(x)}\" y=\"{F(y - 14)}\" width=\"28\" height=\"28\" fill=\"{CouleurZone(zone)}\" stroke=\"{TraitZone(zone)}\" stroke-width=\"2\"/>");
            s.Append($"<text x=\"{F(x + 36)}\" y=\"{F(y)}\" font-size=\"20\" dominant-baseline=\"middle\" fill=\"#1d232a\">Zone {LibelleZone(zone)}</text>");
            y += 32;
        }
        s.Append($"<rect x=\"{F(x)}\" y=\"{F(y - 14)}\" width=\"28\" height=\"28\" fill=\"url(#fentesH)\" stroke=\"#7d8790\" stroke-width=\"2\"/>");
        s.Append($"<text x=\"{F(x + 36)}\" y=\"{F(y)}\" font-size=\"20\" dominant-baseline=\"middle\" fill=\"#1d232a\">Goulottes : " +
                 $"{SecurityElement.Escape(a.Cablage.Horizontale.Libelle)} horizontales, {SecurityElement.Escape(a.Cablage.Verticale.Libelle)} verticales, " +
                 $"{a.Cablage.NombreFils} fils estimés</text>");
        y += 32;
        if (champs && a.Sources.Count > 0)
        {
            y += 12;
            s.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"20\" font-weight=\"bold\" dominant-baseline=\"middle\" fill=\"#1d232a\">Champ magnétique (indicatif)</text>");
            y += 32;
            foreach (var (_, couleur, libelle) in ChampMagnetique.Isolignes)
            {
                s.Append($"<line x1=\"{F(x)}\" y1=\"{F(y)}\" x2=\"{F(x + 28)}\" y2=\"{F(y)}\" stroke=\"{couleur}\" stroke-width=\"4\"/>");
                s.Append($"<text x=\"{F(x + 36)}\" y=\"{F(y)}\" font-size=\"20\" dominant-baseline=\"middle\" fill=\"#1d232a\">{libelle}</text>");
                y += 30;
            }
        }
        s.Append("</g></svg>");
        return s.ToString();
    }

    /// <summary>Isolignes du champ magnétique, tracées par-dessus les zones sans les masquer.</summary>
    static void CarteChamp(StringBuilder s, StringBuilder textes, ResultatArmoire a)
    {
        var grille = ChampMagnetique.Grille(a.Sources, a.LargeurTotale, a.Hauteur, PasChamp);
        var etiquettes = new List<(double X, double Y)>();
        foreach (var (niveau, couleur, _) in ChampMagnetique.Isolignes)
        {
            var segments = ChampMagnetique.Isoligne(grille, PasChamp, niveau);
            var d = new StringBuilder();
            foreach (var (x1, y1, x2, y2) in segments)
                d.Append($"M{F(x1)} {F(y1)}L{F(x2)} {F(y2)}");
            if (d.Length > 0)
                s.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"{couleur}\" stroke-width=\"4\" stroke-linecap=\"round\"/>");
            if (ChampMagnetique.PointEtiquette(segments, a.LargeurTotale, a.Hauteur, etiquettes, a.Appareils) is { } p)
                textes.Append($"<text x=\"{F(p.X + 6)}\" y=\"{F(p.Y)}\" font-size=\"17\" font-weight=\"bold\" fill=\"{couleur}\" dominant-baseline=\"middle\" " +
                         $"stroke=\"#ffffff\" stroke-width=\"4\" paint-order=\"stroke\">{F(niveau)} A/m</text>");
        }
    }

    static string LibelleZone(ZoneCem z) => z switch
    {
        ZoneCem.Propre => "propre",
        ZoneCem.Sale => "sale",
        _ => "intermédiaire",
    };

    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
