using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Une alimentation à découpage ou un transformateur de commande : sortie, primaires admis, courant ou puissance assignés,
/// rendement et encombrement. Rang 1 = gamme standard, 2 = gamme haut de gamme.
/// </summary>
public record ModeleAlimentation
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    public int Rang { get; init; } = 1;
    public string Reference { get; init; } = "";
    /// <summary>Courant de sortie nominal, en A.</summary>
    public double Courant { get; init; }
    /// <summary>« Decoupage » ou « Transformateur ».</summary>
    public string Type { get; init; } = Decoupage;
    /// <summary>« 24 V DC », « 24 V AC » ou « 230 V AC ».</summary>
    public string Secondaire { get; init; } = "24 V DC";
    /// <summary>Primaires admis : « 230 », « 400-2P », « 400-3P ».</summary>
    public List<string> Entrees { get; init; } = new() { "230" };
    /// <summary>Puissance assignée d'un transformateur, en VA.</summary>
    public double Va { get; init; }

    public const string Decoupage = "Decoupage";
    public const string Transformateur = "Transformateur";
    public double Rendement { get; init; } = 0.9;
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public bool Verifie { get; init; }
}

/// <summary>
/// Catalogue des alimentations 24 V et des transformateurs de commande Siemens et Schneider, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-alimentations.json » de l'utilisateur. Indicatif tant que non vérifié.
/// </summary>
public static class CatalogueAlimentations
{
    public const string NomFichier = "catalogue-alimentations.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleAlimentation>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<ModeleAlimentation> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-alimentations.json")
                        ?? throw new InvalidOperationException("Catalogue des alimentations introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleAlimentation> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleAlimentation>>(json, Options) ?? new List<ModeleAlimentation>();

    /// <summary>Marque retenue : celle du départ, sinon Siemens pour un projet Siemens et Schneider pour les autres.</summary>
    public static string Marque(MarqueAlimentation choix, FabricantMateriel projet) => choix switch
    {
        MarqueAlimentation.Siemens => "Siemens",
        MarqueAlimentation.Schneider => "Schneider",
        _ => projet == FabricantMateriel.Siemens ? "Siemens" : "Schneider",
    };

    /// <summary>Clé du primaire d'après la distribution du départ.</summary>
    public static string Primaire(TypeAlimentation a) => a switch
    {
        TypeAlimentation.Mono230 => "230",
        TypeAlimentation.Bi400 => "400-2P",
        _ => "400-3P",
    };

    public static string LibellePrimaire(string primaire) => primaire switch
    {
        "230" => "230 V phase-neutre",
        "400-2P" => "400 V entre phases",
        _ => "400 V triphasé",
    };

    public static string Secondaire(TensionCommande t, bool transformateur) => t switch
    {
        TensionCommande.Continu24 => transformateur ? "24 V AC" : "24 V DC",
        TensionCommande.Alternatif24 => "24 V AC",
        _ => "230 V AC",
    };

    /// <summary>
    /// Plus petite alimentation à découpage de la marque qui couvre le courant, avec ce primaire ; gamme standard d'abord.
    /// </summary>
    public static ModeleAlimentation? Choisir(string fabricant, double courant, string primaire, IReadOnlyList<ModeleAlimentation> catalogue) =>
        catalogue
            .Where(m => m.Type == ModeleAlimentation.Decoupage && m.Secondaire == "24 V DC"
                        && m.Fabricant.Equals(fabricant, StringComparison.OrdinalIgnoreCase) && m.Courant >= courant - 1e-6
                        && m.Entrees.Contains(primaire))
            .OrderBy(m => m.Courant).ThenBy(m => m.Rang).ThenBy(m => m.Largeur)
            .FirstOrDefault();

    /// <summary>Plus petit transformateur de la marque de ce secondaire et de ce primaire dont la puissance couvre le besoin.</summary>
    public static ModeleAlimentation? ChoisirTransformateur(string fabricant, string secondaire, double va, string primaire,
        IReadOnlyList<ModeleAlimentation> catalogue) =>
        catalogue
            .Where(m => m.Type == ModeleAlimentation.Transformateur && m.Secondaire == secondaire
                        && m.Fabricant.Equals(fabricant, StringComparison.OrdinalIgnoreCase) && m.Va >= va - 1e-6
                        && m.Entrees.Contains(primaire))
            .OrderBy(m => m.Va).ThenBy(m => m.Rang).ThenBy(m => m.Largeur)
            .FirstOrDefault();
}
