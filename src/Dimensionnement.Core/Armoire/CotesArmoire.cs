using System.Globalization;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Armoire;

/// <summary>
/// Une cote de l'implantation, en mm dans le repère de l'enveloppe vue de face. Une cote horizontale mesure de <see cref="De"/> à
/// <see cref="A"/> le long de X et sa ligne est tracée à la hauteur <see cref="Position"/> ; une cote verticale mesure le long de Y
/// et sa ligne est tracée à l'abscisse <see cref="Position"/>. Les lignes d'attache partent de <see cref="Attache"/>.
/// </summary>
public record Cote(string Cle, string Libelle, bool Horizontale, double De, double A, double Attache, double Position)
{
    public double Valeur => Math.Abs(A - De);
    public string Texte => Valeur.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"));
}

/// <summary>
/// Cotes de fabrication de la plaque : enveloppe, plaque de montage, entraxes des rails, position des goulottes horizontales,
/// position et encombrement des appareils fixés directement sur la plaque (variateurs…). Chaque cote peut être décalée ou masquée
/// par l'utilisateur ; ses réglages sont gardés dans le projet, par clé.
/// </summary>
public static class CotesArmoire
{
    /// <summary>Écart entre deux lignes de cotes empilées, et entre l'enveloppe et la première, en mm.</summary>
    public const double Pas = 26;

    /// <summary>Place à réserver autour de l'enveloppe pour les cotes extérieures, en mm.</summary>
    public const double Marge = 3 * Pas + 16;

    /// <summary>Les cotes à afficher : toutes celles de l'enveloppe, sans les cotes masquées, avec les décalages de l'utilisateur.</summary>
    public static List<Cote> Calculer(ResultatArmoire a, ReglagesCotes reglages) =>
        Toutes(a).Where(c => !reglages.Masquees.Contains(c.Cle))
            .Select(c => reglages.Decalages.TryGetValue(c.Cle, out var d) ? c with { Position = c.Position + d } : c).ToList();

    /// <summary>Toutes les cotes de l'enveloppe, à leur place d'origine.</summary>
    public static List<Cote> Toutes(ResultatArmoire a)
    {
        var cotes = new List<Cote>();
        var p = a.Id is { } id ? id.ToString("N")[..8] + "|" : "";
        var plaque = a.PlaqueLargeur > 0;
        double haut = plaque ? a.PlaqueY : 0, bas = plaque ? a.PlaqueY + a.PlaqueHauteur : a.Hauteur;

        // Enveloppe : largeur au-dessus, hauteur à droite.
        cotes.Add(new($"{p}enveloppe.L", "Largeur de l'enveloppe", true, 0, a.LargeurTotale, 0, -(plaque ? 2 : 1) * Pas));
        cotes.Add(new($"{p}enveloppe.H", "Hauteur de l'enveloppe", false, 0, a.Hauteur, a.LargeurTotale, a.LargeurTotale + 2 * Pas));
        if (plaque)
            for (var c = 0; c < a.Cellules; c++)
            {
                var x = c * a.Largeur + a.PlaqueX;
                var s = a.Cellules > 1 ? $".{c + 1}" : "";
                cotes.Add(new($"{p}plaque.L{s}", "Largeur de la plaque de montage", true, x, x + a.PlaqueLargeur, a.PlaqueY, -Pas));
                if (c == 0) cotes.Add(new($"{p}plaque.H", "Hauteur de la plaque de montage", false, haut, bas, x, -2 * Pas));
            }

        // Rails : cotes en chaîne du haut de la plaque à l'axe de chaque rail, puis au bas de la plaque, à gauche.
        var axes = a.Bandes.Where(b => b.Rail).Select(b => Math.Round(b.Y + b.Hauteur / 2)).Distinct().OrderBy(y => y).ToList();
        Chaine(cotes, $"{p}rail", "Axe de rail", false, axes.Prepend(haut).Append(bas).ToList(), a.PlaqueX, -Pas);

        // Goulottes horizontales : cotes en chaîne sur leurs axes, à droite.
        var goulottes = a.Cablage.Goulottes.Where(g => !g.Verticale).Select(g => Math.Round(g.Y + g.Hauteur / 2)).Distinct().OrderBy(y => y).ToList();
        if (goulottes.Count > 0)
            Chaine(cotes, $"{p}goulotte", "Axe de goulotte", false, goulottes.Prepend(haut).Append(bas).ToList(), a.LargeurTotale, a.LargeurTotale + Pas);

        // Appareils fixés sur la plaque (hors rail) : perçage. Par rangée, chaîne horizontale depuis le bord gauche de la plaque,
        // puis hauteur de la rangée depuis le haut de la plaque.
        var horsRail = a.Appareils.Where(e => !SurRail(e, a)).OrderBy(e => e.Y).ThenBy(e => e.X).ToList();
        var rangees = new List<List<AppareilImplante>>();
        foreach (var e in horsRail)
            if (rangees.LastOrDefault() is { } r && e.Y < r.Max(x => x.Y + x.Hauteur)
                && CelluleDe(e, a) == CelluleDe(r[0], a)) r.Add(e);
            else rangees.Add(new() { e });
        foreach (var r in rangees)
        {
            var ordre = r.OrderBy(e => e.X).ToList();
            var gauche = CelluleDe(ordre[0], a) * a.Largeur + (plaque ? a.PlaqueX : 0);
            var y = ordre.Min(e => e.Y);
            var points = new List<double> { gauche };
            foreach (var e in ordre) { points.Add(e.X); points.Add(e.X + e.Largeur); }
            Chaine(cotes, $"{p}app.{ordre[0].Cle}.x", "Position et largeur des appareils sur la plaque", true, points, y, y - 14);
            // Hauteur de chaque appareil, tracée dans l'appareil le long de son bord droit ; la distance au haut de la plaque
            // n'est donnée qu'une fois par niveau.
            var niveaux = new HashSet<double>();
            foreach (var e in ordre)
            {
                var droite = e.X + e.Largeur;
                var niveau = new List<double> { e.Y, e.Y + e.Hauteur };
                if (niveaux.Add(Math.Round(e.Y))) niveau.Insert(0, haut);
                Chaine(cotes, $"{p}app.{e.Cle}.y", "Position et hauteur de l'appareil sur la plaque", false, niveau, droite, droite - 14);
            }
        }
        return cotes;
    }

    /// <summary>Cotes en chaîne entre des points successifs, toutes sur la même ligne. Les intervalles nuls sont ignorés.</summary>
    static void Chaine(List<Cote> cotes, string cle, string libelle, bool horizontale, List<double> points, double attache, double position)
    {
        for (var i = 1; i < points.Count; i++)
            if (points[i] - points[i - 1] >= 1)
                cotes.Add(new($"{cle}.{i}", libelle, horizontale, points[i - 1], points[i], attache, position));
    }

    static int CelluleDe(AppareilImplante e, ResultatArmoire a) => a.Largeur > 0 ? Math.Clamp((int)(e.CentreX / a.Largeur), 0, a.Cellules - 1) : 0;

    /// <summary>Appareil accroché à un rail DIN : son rectangle coupe l'axe d'un rail.</summary>
    static bool SurRail(AppareilImplante e, ResultatArmoire a) => a.Bandes.Any(b => b.Rail
        && e.Y <= b.Y + b.Hauteur / 2 && e.Y + e.Hauteur >= b.Y + b.Hauteur / 2 && e.X + e.Largeur > b.X && e.X < b.X + b.Largeur);
}
