using System.Globalization;

namespace Dimensionnement.Core.Calculs;

/// <summary>Mise en forme des nombres à la française pour les notes de calcul et le rapport.</summary>
public static class Format
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string N(double valeur, int decimales = 2) =>
        Math.Round(valeur, decimales).ToString("0.##########", Fr);

    public static string A(double courant) => $"{N(courant, 1)} A";
    public static string Mm2(double section) => $"{N(section)} mm²";
    public static string Pct(double pourcentage) => $"{N(pourcentage)} %";
}
