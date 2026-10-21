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

    public static IReadOnlyList<ModeleVentilateur> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

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
