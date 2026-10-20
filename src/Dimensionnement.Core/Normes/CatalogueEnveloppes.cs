using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Une enveloppe du catalogue : coffret mural ou cellule d'armoire au sol, dimensions extérieures en mm,
/// plaque de montage (surface utile en mm) et références de l'enveloppe et de la plaque.
/// </summary>
public record ModeleEnveloppe
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    /// <summary>« Coffret » ou « Armoire ».</summary>
    public string Type { get; init; } = "";
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public string Reference { get; init; } = "";
    public double PlaqueLargeur { get; init; }
    public double PlaqueHauteur { get; init; }
    public string ReferencePlaque { get; init; } = "";

    /// <summary>Vrai une fois la ligne contrôlée sur le catalogue du fabricant.</summary>
    public bool Verifie { get; init; }

    public bool EstArmoire => Type == "Armoire";
}

/// <summary>
/// Catalogue des enveloppes Rittal, Legrand et Schneider avec leurs plaques de montage, embarqué au format JSON et
/// remplaçable par un fichier « catalogue-enveloppes.json » de l'utilisateur. Indicatif tant que non vérifié.
/// </summary>
public static class CatalogueEnveloppes
{
    public const string NomFichier = "catalogue-enveloppes.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleEnveloppe>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<ModeleEnveloppe> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-enveloppes.json")
                        ?? throw new InvalidOperationException("Catalogue des enveloppes introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleEnveloppe> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleEnveloppe>>(json, Options) ?? new List<ModeleEnveloppe>();

    public static IEnumerable<ModeleEnveloppe> DeLaMarque(MarqueEnveloppe marque, IReadOnlyList<ModeleEnveloppe> catalogue) =>
        catalogue.Where(e => e.Fabricant.Equals(marque.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Enveloppe de la marque aux dimensions exactes, ou une enveloppe hors catalogue avec une plaque estimée.</summary>
    public static ModeleEnveloppe Trouver(MarqueEnveloppe marque, double largeur, double hauteur, double profondeur, IReadOnlyList<ModeleEnveloppe> catalogue) =>
        DeLaMarque(marque, catalogue).FirstOrDefault(e => Math.Abs(e.Largeur - largeur) < 1 && Math.Abs(e.Hauteur - hauteur) < 1 && Math.Abs(e.Profondeur - profondeur) < 1)
        ?? HorsCatalogue(marque, largeur, hauteur, profondeur);

    static ModeleEnveloppe HorsCatalogue(MarqueEnveloppe marque, double l, double h, double p)
    {
        var armoire = h >= 1600;
        var marge = armoire ? 100 : Math.Max(l, h) <= 600 ? 40 : 60;
        return new ModeleEnveloppe
        {
            Fabricant = marque.ToString(), Gamme = "hors catalogue", Type = armoire ? "Armoire" : "Coffret",
            Largeur = l, Hauteur = h, Profondeur = p, Reference = $"{l:0} × {h:0} × {p:0} (hors catalogue)",
            PlaqueLargeur = Math.Max(100, l - marge), PlaqueHauteur = Math.Max(100, h - marge), ReferencePlaque = "à définir",
        };
    }
}
