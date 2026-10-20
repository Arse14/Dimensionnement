using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dimensionnement.Core.Normes;

/// <summary>Cotes d'une taille de boîtier d'appareillage, reconnue par un motif sur la référence.</summary>
public record CoteAppareil
{
    public string Fabricant { get; init; } = "";
    public string Famille { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Taille { get; init; } = "";
    /// <summary>Expression régulière appliquée à la référence de l'appareil.</summary>
    public string Motif { get; init; } = "";
    /// <summary>Courant assigné maximal couvert par la ligne, en A ; 0 = toute la taille.</summary>
    public double Max { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    /// <summary>Largeur donnée pour un pôle (appareils modulaires).</summary>
    public bool ParPole { get; init; }
    public bool Verifie { get; init; }
    /// <summary>« verifie », « une_source » ou « estime ».</summary>
    public string Fiabilite { get; init; } = "";
    public string Source { get; init; } = "";
    public string Note { get; init; } = "";
}

/// <summary>
/// Cotes relevées des gammes d'appareillage Siemens et Schneider (contacteurs, disjoncteurs, relais thermiques,
/// interrupteurs-sectionneurs, démarreurs, porte-fusibles), embarquées au format JSON et remplaçables par un fichier
/// « catalogue-encombrements.json » de l'utilisateur.
/// </summary>
public static class CatalogueEncombrements
{
    public const string NomFichier = "catalogue-encombrements.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<CoteAppareil>> Embarque = new(() => Lire(TexteEmbarque()));

    /// <summary>Catalogue utilisé par l'implantation ; l'application y met celui de l'utilisateur s'il existe.</summary>
    public static IReadOnlyList<CoteAppareil> Actif { get; set; } = Array.Empty<CoteAppareil>();

    static CatalogueEncombrements() => Actif = Embarque.Value;

    public static IReadOnlyList<CoteAppareil> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-encombrements.json")
                        ?? throw new InvalidOperationException("Catalogue des encombrements introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<CoteAppareil> Lire(string json) =>
        JsonSerializer.Deserialize<List<CoteAppareil>>(json, Options) ?? new List<CoteAppareil>();

    /// <summary>Première ligne de la famille dont le motif reconnaît la référence et dont le courant maximal couvre la valeur.</summary>
    public static CoteAppareil? Trouver(string famille, string reference, double valeur, IReadOnlyList<CoteAppareil>? catalogue = null)
    {
        if (reference.Length == 0) return null;
        foreach (var c in catalogue ?? Actif)
        {
            if (c.Famille != famille || (c.Max > 0 && valeur > c.Max + 1e-6)) continue;
            try
            {
                if (Regex.IsMatch(reference, c.Motif, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50))) return c;
            }
            catch (ArgumentException) { }
            catch (RegexMatchTimeoutException) { }
        }
        return null;
    }
}
