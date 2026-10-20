using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>Un pupitre IHM : diagonale en pouces, façade et découpe de porte en mm, consommation sur le 24 V DC en A.</summary>
public record ModeleIhm
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    public int Rang { get; init; } = 1;
    public string Reference { get; init; } = "";
    public string Designation { get; init; } = "";
    public double Diagonale { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public double DecoupeLargeur { get; init; }
    public double DecoupeHauteur { get; init; }
    public double Consommation { get; init; }
    public string Interface { get; init; } = "";
    public bool Verifie { get; init; }

    public string Libelle => $"{Designation} ({Reference}), {Diagonale:0.#}\"";
}

/// <summary>
/// Catalogue des pupitres IHM Siemens et Schneider, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-ihm.json » de l'utilisateur. Indicatif tant que non vérifié.
/// </summary>
public static class CatalogueIhm
{
    public const string NomFichier = "catalogue-ihm.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleIhm>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<ModeleIhm> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-ihm.json")
                        ?? throw new InvalidOperationException("Catalogue des IHM introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleIhm> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleIhm>>(json, Options) ?? new List<ModeleIhm>();

    /// <summary>Modèle imposé, sinon le plus petit de la marque et de la gamme qui atteint la diagonale, sinon le plus grand de la gamme.</summary>
    public static ModeleIhm? Choisir(PupitreIhm p, string marque, IReadOnlyList<ModeleIhm> catalogue)
    {
        if (p.ReferenceImposee.Trim() is { Length: > 0 } reference)
            return catalogue.FirstOrDefault(m => m.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase));
        var rang = p.Gamme == GammeIhm.Standard ? 1 : 2;
        var gamme = catalogue.Where(m => m.Fabricant.Equals(marque, StringComparison.OrdinalIgnoreCase) && m.Rang == rang).ToList();
        return gamme.Where(m => m.Diagonale >= p.Diagonale - 0.3).OrderBy(m => m.Diagonale).FirstOrDefault()
               ?? gamme.OrderByDescending(m => m.Diagonale).FirstOrDefault();
    }
}
