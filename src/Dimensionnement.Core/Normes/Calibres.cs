namespace Dimensionnement.Core.Normes;

/// <summary>
/// Calibres normalisés utilisés pour arrondir un résultat de calcul à la valeur commerciale supérieure.
/// Ces listes sont génériques ; les références fabricants viendront avec les catalogues.
/// </summary>
public static class Calibres
{
    /// <summary>Calibres de disjoncteurs (modulaires jusqu'à 125 A, boîtiers moulés au-delà), en A.</summary>
    public static readonly double[] Disjoncteurs =
        { 1, 2, 3, 4, 6, 10, 13, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 320, 400, 500, 630 };

    /// <summary>Calibres d'interrupteurs-sectionneurs, en A.</summary>
    public static readonly double[] InterrupteursSectionneurs =
        { 16, 20, 25, 32, 40, 63, 80, 100, 125, 160, 200, 250, 315, 400, 630 };

    /// <summary>Courants d'emploi AC-3 sous 400 V des contacteurs, en A.</summary>
    public static readonly double[] ContacteursAc3 =
        { 9, 12, 18, 25, 32, 38, 40, 50, 65, 80, 95, 115, 150, 185, 225, 265, 330, 400, 500 };

    /// <summary>Courants d'emploi AC-1 des contacteurs, en A.</summary>
    public static readonly double[] ContacteursAc1 =
        { 20, 25, 32, 40, 50, 60, 80, 95, 125, 140, 200, 250, 275, 350, 450, 600 };

    /// <summary>Sections normalisées des conducteurs cuivre, en mm².</summary>
    public static readonly double[] Sections =
        { 1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95, 120, 150, 185, 240 };

    /// <summary>Calibres des cartouches fusibles, en A.</summary>
    public static readonly double[] Fusibles =
        { 2, 4, 6, 8, 10, 12, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630 };

    /// <summary>Courants assignés des alimentations 24 V DC à découpage, en A.</summary>
    public static readonly double[] Alimentations24V = { 2.5, 3.8, 5, 10, 20, 40 };

    /// <summary>Taille de cartouche et de porte-fusible pour un calibre et une classe donnés.</summary>
    public static string TailleFusible(double calibre, Modele.ClasseFusible classe)
    {
        // Les cartouches aM et gR d'une taille montent moins haut en calibre que les gG.
        var gG = classe == Modele.ClasseFusible.gG;
        return calibre switch
        {
            _ when calibre <= (gG ? 32 : 25) => "10×38",
            <= 50 => "14×51",
            _ when calibre <= (gG ? 125 : 100) => "22×58",
            <= 160 => "NH00",
            <= 250 => "NH1",
            <= 400 => "NH2",
            _ => "NH3",
        };
    }

    /// <summary>
    /// Coefficient k3 des fusibles gG : le câble doit vérifier Iz ≥ k3 × In (NF C 15-100, 433.1).
    /// </summary>
    public static double CoefficientFusibleGg(double calibre) => calibre switch
    {
        <= 10 => 1.31,
        <= 25 => 1.21,
        _ => 1.10,
    };

    /// <summary>Plages de réglage thermique des disjoncteurs moteurs, en A (mini, maxi).</summary>
    public static readonly (double Min, double Max)[] PlagesDisjoncteurMoteur =
    {
        (0.1, 0.16), (0.16, 0.25), (0.25, 0.4), (0.4, 0.63), (0.63, 1), (1, 1.6), (1.6, 2.5),
        (2.5, 4), (4, 6.3), (6, 10), (9, 14), (13, 18), (17, 23), (20, 25), (24, 32),
        (30, 40), (37, 50), (48, 65), (62, 80),
    };

    /// <summary>Seuil magnétique des disjoncteurs moteurs, en multiple du maxi de la plage.</summary>
    public const double SeuilMagnetiqueDisjoncteurMoteur = 13;

    /// <summary>Plus petit calibre de la liste supérieur ou égal à la valeur ; null si aucun ne convient.</summary>
    public static double? Superieur(IEnumerable<double> liste, double valeur) =>
        liste.Where(c => c >= valeur - 1e-9).Cast<double?>().FirstOrDefault();

    /// <summary>Plage de disjoncteur moteur dont le réglage contient le courant, la plus basse possible.</summary>
    public static (double Min, double Max)? PlageMoteur(double courant)
    {
        foreach (var plage in PlagesDisjoncteurMoteur)
            if (courant >= plage.Min && courant <= plage.Max)
                return plage;
        return null;
    }
}
