using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Dimensionnement.App.Infrastructure;

/// <summary>Raison d'une case en rouge ; une valeur hors des usages peut être forcée, un texte illisible non.</summary>
public record ErreurSaisie(string Message, bool Forcable, string Chemin = "", double Valeur = 0)
{
    public override string ToString() => Message;
}

/// <summary>
/// Conversion des saisies : accepte la virgule comme le point, les espaces de milliers et le signe %.
/// Les autres types (listes, cases à cocher) passent tels quels.
/// </summary>
public class ConvertisseurSaisie : IValueConverter
{
    public static readonly ConvertisseurSaisie Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        targetType == typeof(string) && value is IFormattable f and not Enum ? f.ToString(null, culture) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string texte) return value;
        var cible = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (cible == typeof(string)) return texte;
        if (cible != typeof(double) && cible != typeof(int)) return value;
        var propre = texte.Trim().Replace(' ', ' ').Replace(" ", "").Replace("%", "").Replace(',', '.');
        if (propre.Length == 0 && Nullable.GetUnderlyingType(targetType) is not null) return null;
        if (!double.TryParse(propre, NumberStyles.Float, CultureInfo.InvariantCulture, out var nombre))
            throw new FormatException($"« {texte} » n'est pas un nombre. Saisir par exemple 0,85 ou 0.85.");
        if (cible == typeof(int))
        {
            if (Math.Abs(nombre - Math.Round(nombre)) > 1e-9)
                throw new FormatException($"« {texte} » : un nombre entier est attendu.");
            return (int)Math.Round(nombre);
        }
        return nombre;
    }
}

/// <summary>
/// Plages usuelles des grandeurs saisies : une valeur en dehors met la case en rouge avec l'explication,
/// et peut être forcée depuis la barre d'état.
/// </summary>
public class RegleVraisemblance : ValidationRule
{
    readonly string _chemin;

    public RegleVraisemblance(string chemin)
    {
        _chemin = chemin;
        ValidationStep = ValidationStep.ConvertedProposedValue;
    }

    /// <summary>Valeurs forcées pendant la session, par propriété.</summary>
    public static readonly HashSet<(string Chemin, double Valeur)> Forcees = new();

    static readonly Dictionary<string, (double Min, double Max, string Raison)> Plages = new()
    {
        ["Rendement"] = (0.3, 1, "Le rendement est un rapport entre 0 et 1 (0,85 pour 85 %) ; un moteur courant est entre 0,70 et 0,97."),
        ["CosPhi"] = (0.3, 1, "Le cos φ est compris entre 0 et 1 ; un moteur asynchrone est entre 0,70 et 0,90 en charge."),
        ["TensionFreinV"] = (12, 690, "Tension d'alimentation du frein ou de son redresseur, en V (24, 230, 400…)."),
        ["TensionBobineFreinV"] = (0, 500, "Tension de bobine du frein en V DC (ex. 104, 180, 205), 0 si inconnue."),
        ["CoupleNominal"] = (0, 100000, "Le couple nominal du moteur est en N·m (valeur « Couple nominal » ou M de la fiche)."),
        ["VitesseNominale"] = (0, 12000, "La vitesse nominale est en tr/min (environ 1450 pour un 4 pôles en 50 Hz)."),
        ["RapportId"] = (1, 12, "Le rapport Id / In d'un moteur asynchrone est en général entre 5 et 9 (IE3 : jusqu'à 10)."),
        ["CoefficientUtilisation"] = (0, 1, "Le coefficient d'utilisation ku est compris entre 0 et 1."),
        ["Simultaneite"] = (0, 1, "Un coefficient de simultanéité est compris entre 0 et 1."),
        ["ReservePourcent"] = (0, 200, "La réserve est un pourcentage, en général entre 10 et 30 %."),
        ["PuissanceKw"] = (0, 2000, "La puissance est en kW ; au-delà de 2 MW, vérifier l'unité."),
        ["Longueur"] = (0, 2000, "La longueur est en mètres ; au-delà de 2 km, vérifier l'unité."),
        ["TemperatureAmbiante"] = (-40, 70, "La température ambiante est en °C, en général entre -25 et 55 °C."),
        ["IccKiloAmperes"] = (0, 150, "L'Icc est en kA ; une valeur au-delà de 150 kA est inhabituelle en BT."),
    };

    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (value is not double v) return ValidationResult.ValidResult;
        var propriete = _chemin.Split('.').Last();
        if (v < 0 && propriete != "TemperatureAmbiante")
            return Refus($"Une valeur négative n'a pas de sens ici ({propriete}).", v);
        if (!Plages.TryGetValue(propriete, out var p) || (v >= p.Min && v <= p.Max)) return ValidationResult.ValidResult;
        return Refus($"{p.Raison} Valeur saisie : {v.ToString(CultureInfo.CurrentCulture)}.", v);
    }

    ValidationResult Refus(string raison, double v) => Forcees.Contains((_chemin, v))
        ? ValidationResult.ValidResult
        : new ValidationResult(false, new ErreurSaisie(raison, true, _chemin, v));
}
