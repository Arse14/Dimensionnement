using System.Reflection;
using System.Text.Json;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Un répartiteur de puissance : courant assigné en A, raccordements de sortie par pôle, plus grande section de sortie
/// et d'entrée en mm², cotes en mm.
/// </summary>
public record ModeleRepartiteur
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Reference { get; init; } = "";
    public int Poles { get; init; } = 4;
    public double Courant { get; init; }
    public int Sorties { get; init; }
    public double SectionMaxSortie { get; init; }
    public double SectionEntree { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public string Montage { get; init; } = "";
    public double? Pertes { get; init; }
    public string Fiabilite { get; init; } = "";
    public string Source { get; init; } = "";
    public string Note { get; init; } = "";
    public bool Verifie { get; init; }
}

/// <summary>
/// Catalogue des répartiteurs Siemens et Schneider, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-repartiteurs.json » de l'utilisateur.
/// </summary>
public static class CatalogueRepartiteurs
{
    public const string NomFichier = "catalogue-repartiteurs.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleRepartiteur>> Embarque = new(() => Lire(TexteEmbarque()));

    /// <summary>Catalogue utilisé par le calcul ; l'application le remplace par celui du dossier utilisateur.</summary>
    public static IReadOnlyList<ModeleRepartiteur> Actif { get; set; } = Embarque.Value;

    public static IReadOnlyList<ModeleRepartiteur> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise)
            ? CatalogueAutomates.Completer(Lire(File.ReadAllText(fichierPersonnalise)), Embarque.Value, m => m.Reference)
            : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-repartiteurs.json")
                        ?? throw new InvalidOperationException("Catalogue des répartiteurs introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleRepartiteur> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleRepartiteur>>(json, Options) ?? new List<ModeleRepartiteur>();

    /// <summary>
    /// Plus petit répartiteur de la marque qui porte le courant, a assez de sorties par pôle et accepte la plus grosse
    /// section de départ ; à défaut, celui qui porte le courant avec le plus de sorties (plusieurs blocs seront nécessaires).
    /// Un bloc multipolaire est préféré ; un bloc unipolaire se commande alors par pôle.
    /// </summary>
    public static ModeleRepartiteur? Choisir(string marque, int poles, double courant, int sorties, double section,
        IReadOnlyList<ModeleRepartiteur>? catalogue = null)
    {
        var gamme = (catalogue ?? Actif)
            .Where(m => m.Fabricant.Equals(marque, StringComparison.OrdinalIgnoreCase) && m.Courant >= courant - 1e-6)
            .Where(m => m.Poles == poles || (m.Poles == 4 && poles < 4) || m.Poles == 1)
            .ToList();
        return gamme.Where(m => m.Sorties >= sorties && (m.SectionMaxSortie <= 0 || m.SectionMaxSortie >= section - 1e-6))
                   .OrderBy(m => m.Poles == 1).ThenBy(m => m.Courant).ThenBy(m => m.Poles).ThenBy(m => m.Sorties).ThenBy(m => m.Largeur).FirstOrDefault()
               ?? gamme.OrderByDescending(m => m.Sorties).ThenBy(m => m.Courant).FirstOrDefault();
    }
}
