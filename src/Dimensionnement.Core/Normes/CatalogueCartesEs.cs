using System.Reflection;
using System.Text.Json;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Une carte d'entrées / sorties d'une famille d'automates, ou un élément de sa station : tête de station déportée,
/// châssis et alimentation de châssis. Cotes en mm, consommation sur le 24 V en A, pertes en W.
/// </summary>
public record CarteEs
{
    public string Famille { get; init; } = "";
    /// <summary>« DI », « DQ », « AI », « AQ », « Interface », « Chassis » ou « AlimentationChassis ».</summary>
    public string Type { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Designation { get; init; } = "";
    /// <summary>Voies d'une carte, cartes admises par une tête de station, emplacements libres d'un châssis.</summary>
    public int Voies { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public double Consommation { get; init; }
    public double Pertes { get; init; }
    /// <summary>Carte haute densité, retenue seulement avec l'option des cartes compactes.</summary>
    public bool Compacte { get; init; }
    /// <summary>Module enfiché dans un châssis, sans encombrement propre.</summary>
    public bool Loge { get; init; }
    /// <summary>Pièce commandée avec chaque carte (connecteur frontal, BaseUnit).</summary>
    public string Accessoire { get; init; } = "";
    public string AccessoireDesignation { get; init; } = "";
    /// <summary>Pièce de la première carte d'une station, à la place de l'accessoire (BaseUnit claire ET200SP).</summary>
    public string AccessoireDebut { get; init; } = "";
    public string AccessoireDebutDesignation { get; init; } = "";
    public bool Verifie { get; init; }

    /// <summary>Carte particulière (HF, relais, 2 A, RTD, sécurité) : au catalogue, jamais retenue automatiquement.</summary>
    public bool Specifique { get; init; }
}

/// <summary>
/// Catalogue des cartes d'E/S Siemens (S7-1200, S7-1500, ET200SP) et Schneider (TM3, X80), embarqué au format JSON
/// et remplaçable par un fichier « catalogue-cartes-es.json » de l'utilisateur. Indicatif tant que non vérifié.
/// </summary>
public static class CatalogueCartesEs
{
    public const string NomFichier = "catalogue-cartes-es.json";

    /// <summary>Types de voies d'E/S, dans l'ordre de la note de calcul.</summary>
    public static readonly string[] TypesVoies = { "DI", "DQ", "AI", "AQ" };

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<CarteEs>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<CarteEs> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-cartes-es.json")
                        ?? throw new InvalidOperationException("Catalogue des cartes d'E/S introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<CarteEs> Lire(string json) =>
        JsonSerializer.Deserialize<List<CarteEs>>(json, Options) ?? new List<CarteEs>();

    public static IEnumerable<CarteEs> De(string famille, string type, IReadOnlyList<CarteEs> catalogue) =>
        catalogue.Where(c => c.Type == type && c.Famille.Equals(famille, StringComparison.OrdinalIgnoreCase));

    public static string Libelle(string type) => type switch
    {
        "DI" => "Entrées TOR",
        "DQ" => "Sorties TOR",
        "AI" => "Entrées analogiques",
        "AQ" => "Sorties analogiques",
        _ => type,
    };
}
