using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

/// <summary>Façon d'obtenir le rendement d'un moteur quand la fiche du fournisseur ne le donne pas.</summary>
public enum MethodeRendement { Plaque, CoupleVitesse }

/// <summary>Rendement retrouvé, puissances en W, détail du calcul et alerte éventuelle (vide si cohérent).</summary>
public record ResultatRendement(double Rendement, double PuissanceUtileW, double PuissanceAbsorbeeW, string Detail, string Alerte);

/// <summary>
/// Rendement d'un moteur : η = puissance utile / puissance absorbée. La puissance absorbée vient de la plaque
/// (√3 × U × I × cos φ en triphasé, U × I × cos φ en monophasé) ; la puissance utile est celle de la plaque, ou
/// C × 2π × n / 60 à partir du couple et de la vitesse nominaux.
/// </summary>
public static class CalculRendement
{
    /// <summary>Rendements vraisemblables d'un moteur asynchrone : en dehors, une donnée de plaque est probablement fausse.</summary>
    public const double Minimum = 0.5;
    public const double Maximum = 0.98;

    public static double Tension(TypeAlimentation a) => a == TypeAlimentation.Mono230 ? 230 : 400;

    public static double PuissanceAbsorbee(TypeAlimentation a, double courant, double cosPhi) => a switch
    {
        TypeAlimentation.Mono230 or TypeAlimentation.Bi400 => Tension(a) * courant * cosPhi,
        _ => Math.Sqrt(3) * Tension(a) * courant * cosPhi,
    };

    static string FormuleAbsorbee(TypeAlimentation a, double courant, double cosPhi) =>
        (a is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? "" : "√3 × ") +
        $"{N(Tension(a), 0)} V × {N(courant)} A × {N(cosPhi)} = {N(PuissanceAbsorbee(a, courant, cosPhi), 0)} W";

    /// <summary>Puissance utile tirée du couple (N·m) et de la vitesse (tr/min), en W.</summary>
    public static double PuissanceUtile(double couple, double vitesse) => couple * 2 * Math.PI * vitesse / 60;

    public static ResultatRendement? Calculer(DepartMoteur m, MethodeRendement methode)
    {
        if (m.CourantPlaque <= 0 || m.CosPhi <= 0) return null;
        var absorbee = PuissanceAbsorbee(m.Alimentation, m.CourantPlaque, m.CosPhi);
        double utile;
        string detail;
        if (methode == MethodeRendement.CoupleVitesse)
        {
            if (m.CoupleNominal <= 0 || m.VitesseNominale <= 0) return null;
            utile = PuissanceUtile(m.CoupleNominal, m.VitesseNominale);
            detail = $"Pu = C × 2π × n / 60 = {N(m.CoupleNominal)} N·m × 2π × {N(m.VitesseNominale, 0)} tr/min / 60 = {N(utile, 0)} W";
            if (m.PuissanceKw > 0)
                detail += $" (plaque : {N(m.PuissanceKw * 1000, 0)} W, écart {N(100 * Math.Abs(utile / (m.PuissanceKw * 1000) - 1), 1)} %)";
        }
        else
        {
            if (m.PuissanceKw <= 0) return null;
            utile = m.PuissanceKw * 1000;
            detail = $"Pu = puissance de plaque = {N(utile, 0)} W";
        }
        var eta = utile / absorbee;
        detail += $" ; Pabs = {FormuleAbsorbee(m.Alimentation, m.CourantPlaque, m.CosPhi)} ; η = {N(utile, 0)} / {N(absorbee, 0)} = {N(eta * 100, 1)} %";
        var alerte = eta is < Minimum or > Maximum
            ? $"Rendement de {N(eta * 100, 1)} % invraisemblable : vérifier le courant (à {N(Tension(m.Alimentation), 0)} V) et le cos φ de la plaque."
            : "";
        return new ResultatRendement(eta, utile, absorbee, detail, alerte);
    }
}
