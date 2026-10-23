using System.Reflection;
using System.Text.Json;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Un ventilateur à filtre encastrable : débit en soufflage libre avec filtre en m³/h, découpe carrée et cotes
/// extérieures en mm, puissance absorbée en W.
/// </summary>
public record ModeleVentilateur
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Reference { get; init; } = "";
    public double Debit { get; init; }
    public double Decoupe { get; init; }
    public double Puissance { get; init; }

    /// <summary>Tension d'alimentation du ventilateur : « 230 V AC » ou « 24 V DC ».</summary>
    public string Tension { get; init; } = "230 V AC";

    /// <summary>Vrai pour un ventilateur alimenté par le 24 V DC de commande.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EnContinu24V => Tension.Replace(" ", "").StartsWith("24VDC", StringComparison.OrdinalIgnoreCase);
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public string Source { get; init; } = "";
    public string Note { get; init; } = "";
    public bool Verifie { get; init; }
}

/// <summary>
/// Catalogue des ventilateurs à filtre Schneider ClimaSys, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-ventilateurs.json » de l'utilisateur.
/// </summary>
public static class CatalogueVentilateurs
{
    public const string NomFichier = "catalogue-ventilateurs.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleVentilateur>> Embarque = new(() => Lire(TexteEmbarque()));

    /// <summary>Catalogue utilisé par le calcul ; l'application le remplace par celui du dossier utilisateur.</summary>
    public static IReadOnlyList<ModeleVentilateur> Actif { get; set; } = Embarque.Value;

    /// <summary>
    /// Le catalogue de l'utilisateur s'il existe, complété des références embarquées qu'il ne contient pas encore
    /// (nouvelles tensions ou tailles ajoutées depuis sa dernière modification). Une ligne encore indicative chez
    /// l'utilisateur cède la place à la ligne embarquée vérifiée sur la fiche du fabricant.
    /// </summary>
    public static IReadOnlyList<ModeleVentilateur> Charger(string? fichierPersonnalise = null)
    {
        if (fichierPersonnalise is null || !File.Exists(fichierPersonnalise)) return Embarque.Value;
        var embarques = Embarque.Value.ToDictionary(m => m.Reference, StringComparer.OrdinalIgnoreCase);
        var personnel = Lire(File.ReadAllText(fichierPersonnalise))
            .Select(m => !m.Verifie && embarques.TryGetValue(m.Reference, out var e) && e.Verifie ? e : m).ToList();
        var connues = personnel.Select(m => m.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return personnel.Concat(Embarque.Value.Where(m => !connues.Contains(m.Reference))).ToList();
    }

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-ventilateurs.json")
                        ?? throw new InvalidOperationException("Catalogue des ventilateurs introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleVentilateur> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleVentilateur>>(json, Options) ?? new List<ModeleVentilateur>();
}
