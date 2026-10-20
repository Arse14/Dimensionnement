using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Courants admissibles et facteurs de correction des conducteurs cuivre, d'après la NF C 15-100 partie 5-52
/// (tableaux repris de l'IEC 60364-5-52, annexe B). Valeurs à vérifier contre votre exemplaire de la norme
/// avant usage en production.
/// </summary>
public static class TablesCables
{
    // Une ligne par section de Calibres.Sections, de 1,5 à 240 mm².
    static readonly Dictionary<(MethodePose, Isolant, int), double[]> Iz = new()
    {
        // Tableau B.52.4 / B.52.2 : PVC, 2 conducteurs chargés
        [(MethodePose.A1, Isolant.PVC, 2)] = new double[] { 14.5, 19.5, 26, 34, 46, 61, 80, 99, 119, 151, 182, 210, 240, 273, 321 },
        [(MethodePose.B1, Isolant.PVC, 2)] = new double[] { 17.5, 24, 32, 41, 57, 76, 101, 125, 151, 192, 232, 269, 300, 341, 400 },
        [(MethodePose.C, Isolant.PVC, 2)] = new double[] { 19.5, 27, 36, 46, 63, 85, 112, 138, 168, 213, 258, 299, 344, 392, 461 },
        // Tableau B.52.4 / B.52.2 : PVC, 3 conducteurs chargés
        [(MethodePose.A1, Isolant.PVC, 3)] = new double[] { 13.5, 18, 24, 31, 42, 56, 73, 89, 108, 136, 164, 188, 216, 245, 286 },
        [(MethodePose.B1, Isolant.PVC, 3)] = new double[] { 15.5, 21, 28, 36, 50, 68, 89, 110, 134, 171, 207, 239, 262, 296, 346 },
        [(MethodePose.C, Isolant.PVC, 3)] = new double[] { 17.5, 24, 32, 41, 57, 76, 96, 119, 144, 184, 223, 259, 299, 341, 403 },
        // Tableau B.52.5 / B.52.3 : PR, 2 conducteurs chargés
        [(MethodePose.A1, Isolant.PR, 2)] = new double[] { 19, 26, 35, 45, 61, 81, 106, 131, 158, 200, 241, 278, 318, 362, 424 },
        [(MethodePose.B1, Isolant.PR, 2)] = new double[] { 23, 31, 42, 54, 75, 100, 133, 164, 198, 253, 306, 354, 393, 449, 528 },
        [(MethodePose.C, Isolant.PR, 2)] = new double[] { 24, 33, 45, 58, 80, 107, 138, 171, 209, 269, 328, 382, 441, 506, 599 },
        // Tableau B.52.5 / B.52.3 : PR, 3 conducteurs chargés
        [(MethodePose.A1, Isolant.PR, 3)] = new double[] { 17, 23, 31, 40, 54, 73, 95, 117, 141, 179, 216, 249, 285, 324, 380 },
        [(MethodePose.B1, Isolant.PR, 3)] = new double[] { 20, 28, 37, 48, 66, 88, 117, 144, 175, 222, 269, 312, 342, 384, 450 },
        [(MethodePose.C, Isolant.PR, 3)] = new double[] { 22, 30, 40, 51, 70, 94, 119, 148, 180, 232, 282, 328, 379, 434, 514 },
        // Tableaux B.52.10 et B.52.12 : câbles multiconducteurs à l'air libre (méthode E)
        [(MethodePose.E, Isolant.PVC, 2)] = new double[] { 22, 30, 40, 51, 70, 94, 119, 148, 180, 232, 282, 328, 379, 434, 514 },
        [(MethodePose.E, Isolant.PVC, 3)] = new double[] { 18.5, 25, 34, 43, 60, 80, 101, 126, 153, 196, 238, 276, 319, 364, 430 },
        [(MethodePose.E, Isolant.PR, 2)] = new double[] { 26, 36, 49, 63, 86, 115, 149, 185, 225, 289, 352, 410, 473, 542, 641 },
        [(MethodePose.E, Isolant.PR, 3)] = new double[] { 23, 32, 42, 54, 75, 100, 127, 158, 192, 246, 298, 346, 399, 456, 538 },
    };

    /// <summary>Courant admissible de base (30 °C, sans groupement), en A.</summary>
    public static double CourantAdmissible(MethodePose pose, Isolant isolant, int conducteursCharges, double section)
    {
        var index = Array.IndexOf(Calibres.Sections, section);
        if (index < 0) throw new ArgumentException($"Section non normalisée : {section} mm²", nameof(section));
        return Iz[(pose, isolant, conducteursCharges >= 3 ? 3 : 2)][index];
    }

    // Tableau B.52.14 : température ambiante de l'air, par pas de 5 °C de 10 à 60 °C.
    static readonly double[] TemperaturePvc = { 1.22, 1.17, 1.12, 1.06, 1.00, 0.94, 0.87, 0.79, 0.71, 0.61, 0.50 };
    static readonly double[] TemperaturePr = { 1.15, 1.12, 1.08, 1.04, 1.00, 0.96, 0.91, 0.87, 0.82, 0.76, 0.71 };

    /// <summary>Facteur de correction de température ambiante, interpolé linéairement entre 10 et 60 °C.</summary>
    public static double FacteurTemperature(Isolant isolant, double temperature)
    {
        var table = isolant == Isolant.PVC ? TemperaturePvc : TemperaturePr;
        var t = Math.Clamp(temperature, 10, 60);
        var position = (t - 10) / 5;
        var bas = (int)Math.Floor(position);
        if (bas >= table.Length - 1) return table[^1];
        return table[bas] + (table[bas + 1] - table[bas]) * (position - bas);
    }

    // Tableau B.52.17, pour 1 à 9 circuits. Ligne 1 : jointifs en faisceau ; ligne 2 : simple couche sur paroi ;
    // ligne 4 : simple couche sur chemin de câbles perforé.
    static readonly double[] GroupementFaisceau = { 1.00, 0.80, 0.70, 0.65, 0.60, 0.57, 0.54, 0.52, 0.50 };
    static readonly double[] GroupementParoi = { 1.00, 0.85, 0.79, 0.75, 0.73, 0.72, 0.72, 0.71, 0.70 };
    static readonly double[] GroupementChemin = { 1.00, 0.88, 0.82, 0.77, 0.75, 0.73, 0.73, 0.72, 0.72 };

    /// <summary>Facteur de groupement selon le nombre de circuits jointifs.</summary>
    public static double FacteurGroupement(MethodePose pose, int circuits)
    {
        var table = pose switch
        {
            MethodePose.C => GroupementParoi,
            MethodePose.E => GroupementChemin,
            _ => GroupementFaisceau,
        };
        var n = Math.Max(1, circuits);
        if (n <= table.Length) return table[n - 1];
        if (pose != MethodePose.A1 && pose != MethodePose.B1) return table[^1];
        return n switch { <= 12 => 0.45, <= 16 => 0.41, _ => 0.38 };
    }

    /// <summary>Résistivité du cuivre retenue pour la chute de tension (1,25 × ρ à 20 °C, guide UTE C 15-105), en Ω·mm²/m.</summary>
    public const double ResistiviteCuivre = 0.0225;

    /// <summary>Réactance linéique des conducteurs, en Ω/m (0,08 mΩ/m).</summary>
    public const double ReactanceLineique = 0.00008;

    /// <summary>Section du conducteur de protection selon la NF C 15-100 (tableau 54.2), en mm².</summary>
    public static double SectionPe(double sectionPhase) => sectionPhase switch
    {
        <= 16 => sectionPhase,
        <= 35 => 16,
        _ => Calibres.Superieur(Calibres.Sections, sectionPhase / 2) ?? sectionPhase,
    };

    /// <summary>Chute de tension maximale admise, en %, pour un circuit autre que l'éclairage.</summary>
    public static double ChuteTensionMax(OrigineAlimentation origine) =>
        origine == OrigineAlimentation.ReseauPublicBT ? 5 : 8;
}
