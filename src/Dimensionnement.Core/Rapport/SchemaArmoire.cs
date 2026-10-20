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
        ZoneCem.Propre => "#dff3e4",
        ZoneCem.Sale => "#fbe1dc",
        _ => "#e9edf1",
    };

    public static string Svg(ResultatArmoire a, bool champs = true)
    {
        const double marge = 40;
        var largeur = a.LargeurTotale + 2 * marge;
        var hauteur = a.Hauteur + 2 * marge + 150;
        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(largeur)}\" height=\"{F(hauteur)}\" viewBox=\"0 0 {F(largeur)} {F(hauteur)}\" " +
                 "font-family=\"Segoe UI, Arial, sans-serif\">");
        s.Append("<defs><radialGradient id=\"champ\"><stop offset=\"0\" stop-color=\"#e53935\" stop-opacity=\"0.55\"/>" +
                 "<stop offset=\"0.6\" stop-color=\"#fb8c00\" stop-opacity=\"0.22\"/><stop offset=\"1\" stop-color=\"#fdd835\" stop-opacity=\"0\"/></radialGradient></defs>");
        s.Append($"<rect width=\"{F(largeur)}\" height=\"{F(hauteur)}\" fill=\"#fff\"/>");
        s.Append($"<g transform=\"translate({F(marge)},{F(marge)})\">");

        for (var c = 0; c < a.Cellules; c++)
            s.Append($"<rect x=\"{F(c * a.Largeur)}\" y=\"0\" width=\"{F(a.Largeur)}\" height=\"{F(a.Hauteur)}\" fill=\"#f7f7f5\" stroke=\"#37414b\" stroke-width=\"4\"/>");
        foreach (var b in a.Bandes)
        {
            s.Append($"<rect x=\"{F(b.X + 8)}\" y=\"{F(b.Y)}\" width=\"{F(b.Largeur - 16)}\" height=\"{F(b.Hauteur)}\" fill=\"{CouleurZone(b.Zone)}\"/>");
            var rail = b.Y + b.Hauteur / 2;
            s.Append($"<line x1=\"{F(b.X + CalculArmoire.MargeLaterale)}\" y1=\"{F(rail)}\" x2=\"{F(b.X + b.Largeur - CalculArmoire.MargeLaterale)}\" y2=\"{F(rail)}\" stroke=\"#9aa5b1\" stroke-width=\"6\"/>");
        }
        if (champs)
            foreach (var src in a.Sources)
                s.Append($"<circle cx=\"{F(src.X)}\" cy=\"{F(src.Y)}\" r=\"{F(src.Rayon)}\" fill=\"url(#champ)\" opacity=\"{F(Math.Clamp(src.Intensite, 0.2, 1))}\"/>");
        foreach (var e in a.Appareils)
        {
            var bord = e.Manuel ? "#f5a623" : "#37414b";
            s.Append($"<rect x=\"{F(e.X)}\" y=\"{F(e.Y)}\" width=\"{F(e.Largeur)}\" height=\"{F(e.Hauteur)}\" rx=\"3\" fill=\"#ffffff\" stroke=\"{bord}\" stroke-width=\"{(e.Manuel ? 4 : 2)}\"/>");
            var taille = Math.Clamp(Math.Min(e.Largeur / 3.2, e.Hauteur / 3), 7, 22);
            s.Append($"<text x=\"{F(e.CentreX)}\" y=\"{F(e.CentreY)}\" font-size=\"{F(taille)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" " +
                     $"font-weight=\"bold\" fill=\"#1d232a\">{SecurityElement.Escape(e.Repere)}</text>");
        }

        var y = a.Hauteur + 36;
        const double x = 0;
        s.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"22\" font-weight=\"bold\" dominant-baseline=\"middle\" fill=\"#1d232a\">" +
                 $"{F(a.Largeur)} × {F(a.Hauteur)} × {F(a.Profondeur)} mm{(a.Cellules > 1 ? $" × {a.Cellules} cellules" : "")}</text>");
        y += 34;
        foreach (var (zone, libelle) in new[] { (ZoneCem.Propre, "Zone propre"), (ZoneCem.Intermediaire, "Zone intermédiaire"), (ZoneCem.Sale, "Zone sale") })
        {
            s.Append($"<rect x=\"{F(x)}\" y=\"{F(y - 14)}\" width=\"28\" height=\"28\" fill=\"{CouleurZone(zone)}\" stroke=\"#9aa5b1\"/>");
            s.Append($"<text x=\"{F(x + 36)}\" y=\"{F(y)}\" font-size=\"22\" dominant-baseline=\"middle\" fill=\"#1d232a\">{libelle}</text>");
            y += 32;
        }
        s.Append("</g></svg>");
        return s.ToString();
    }

    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
