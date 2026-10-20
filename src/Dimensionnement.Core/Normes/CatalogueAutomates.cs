using System.Reflection;
using System.Text.Json;

namespace Dimensionnement.Core.Normes;

/// <summary>Capacités d'une CPU utilisées pour le choix ; null = valeur non renseignée.</summary>
public record CpuAutomate
{
    public string Marque { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Cpu { get; init; } = "";
    public string Reference { get; init; } = "";
    public int? PeripheriquesReseauMax { get; init; }
    public int? ConnexionsMax { get; init; }
    public bool? OpcUa { get; init; }
    public int? MemoireTravailKo { get; init; }
    public int? EntreesSortiesMax { get; init; }
    public bool? VersionSecurite { get; init; }

    /// <summary>Famille des cartes d'E/S raccordées ; vide = déduite de la gamme.</summary>
    public string FamilleCartes { get; init; } = "";
    /// <summary>Cartes d'E/S admises à côté de la CPU (ou dans son châssis) ; null = non renseigné.</summary>
    public int? CartesMax { get; init; }
    public int EntreesTorIntegrees { get; init; }
    public int SortiesTorIntegrees { get; init; }
    public int EntreesAnaIntegrees { get; init; }
    public int SortiesAnaIntegrees { get; init; }
    /// <summary>Cotes en mm ; une largeur nulle signale une CPU logée dans un châssis.</summary>
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    /// <summary>Consommation sur le 24 V en A et pertes en W.</summary>
    public double Consommation { get; init; }
    public double Pertes { get; init; }
    /// <summary>CPU au format de la périphérie compacte (ET200SP).</summary>
    public bool Compacte { get; init; }
    /// <summary>Zone d'adresses d'E/S, en octets en entrée comme en sortie ; null = non renseigné.</summary>
    public int? ZoneEsOctets { get; init; }

    public string Libelle => $"{Marque} {Gamme} – {Cpu} ({Reference})";

    /// <summary>Famille des cartes d'E/S, déduite de la gamme quand le catalogue ne la donne pas.</summary>
    public string Cartes => FamilleCartes.Length > 0 ? FamilleCartes
        : Gamme.Contains("M580") ? "Modicon X80"
        : Gamme.StartsWith("Modicon") ? "Modicon TM3"
        : Gamme;

    public int Integrees(string type) => type switch
    {
        "DI" => EntreesTorIntegrees,
        "DQ" => SortiesTorIntegrees,
        "AI" => EntreesAnaIntegrees,
        "AQ" => SortiesAnaIntegrees,
        _ => 0,
    };

    /// <summary>Vrai une fois les valeurs contrôlées contre la fiche technique du fabricant.</summary>
    public bool Verifie { get; init; }
    public string Source { get; init; } = "";
}

/// <summary>
/// Catalogue des CPU, embarqué au format JSON. Un fichier « catalogue-automates.json » placé à côté du projet
/// ou dans le dossier de l'application le remplace, pour compléter et valider les valeurs.
/// </summary>
public static class CatalogueAutomates
{
    public const string NomFichier = "catalogue-automates.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static IReadOnlyList<CpuAutomate> Charger(string? fichierPersonnalise = null)
    {
        if (fichierPersonnalise is not null && File.Exists(fichierPersonnalise))
            return Lire(File.ReadAllText(fichierPersonnalise));
        return Lire(TexteEmbarque());
    }

    /// <summary>Contenu du catalogue livré avec l'application, à copier pour le compléter.</summary>
    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-automates.json")
                        ?? throw new InvalidOperationException("Catalogue des automates introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<CpuAutomate> Lire(string json) =>
        JsonSerializer.Deserialize<List<CpuAutomate>>(json, Options) ?? new List<CpuAutomate>();
}
