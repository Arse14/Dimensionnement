using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Un appareil de sécurité du catalogue et ses capacités de câblage :
/// relais (capteurs bicanaux raccordables, contacts de sécurité), extension de contacts,
/// automate de sécurité (entrées et sorties sûres de base, nombre d'extensions) ou module d'E/S de sécurité.
/// </summary>
public record ModeleSecurite
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    /// <summary>« Relais », « Extension contacts », « Automate » ou « Module E/S ».</summary>
    public string Type { get; init; } = "";
    public string Reference { get; init; } = "";
    /// <summary>Types de fonctions traités par un relais (noms de <see cref="TypeFonctionSecurite"/>).</summary>
    public List<string> Fonctions { get; init; } = new();
    /// <summary>Capteurs bicanaux raccordables sur un relais.</summary>
    public int Capteurs { get; init; }
    /// <summary>Entrées de sécurité (voies) d'un automate ou d'un module.</summary>
    public int Entrees { get; init; }
    /// <summary>Contacts de sécurité d'un relais ou d'une extension, sorties sûres d'un automate ou d'un module.</summary>
    public int Sorties { get; init; }
    public int SortiesInfo { get; init; }
    public double Largeur { get; init; }
    /// <summary>Consommation sur le 24 V, en A.</summary>
    public double Consommation { get; init; }
    /// <summary>Nombre maximal de modules d'extension d'un automate.</summary>
    public int ExtensionsMax { get; init; }
    public bool Verifie { get; init; }

    /// <summary>Un scrutateur laser se raccorde comme une barrière : deux sorties OSSD.</summary>
    public bool Traite(TypeFonctionSecurite type) => Fonctions.Contains(type.ToString()) ||
        type == TypeFonctionSecurite.ScrutateurLaser && Fonctions.Contains(nameof(TypeFonctionSecurite.BarriereImmaterielle));
}

/// <summary>
/// Catalogue des relais, extensions et automates de sécurité Pilz, Schmersal, Siemens et Schneider, embarqué au
/// format JSON et remplaçable par un fichier « catalogue-securite.json » de l'utilisateur. Indicatif tant que non vérifié.
/// </summary>
public static class CatalogueSecurite
{
    public const string NomFichier = "catalogue-securite.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleSecurite>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<ModeleSecurite> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-securite.json")
                        ?? throw new InvalidOperationException("Catalogue de sécurité introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleSecurite> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleSecurite>>(json, Options) ?? new List<ModeleSecurite>();

    public static IEnumerable<ModeleSecurite> De(FabricantSecurite fabricant, string type, IReadOnlyList<ModeleSecurite> catalogue) =>
        catalogue.Where(m => m.Type == type && m.Fabricant.Equals(fabricant.ToString(), StringComparison.OrdinalIgnoreCase));
}
