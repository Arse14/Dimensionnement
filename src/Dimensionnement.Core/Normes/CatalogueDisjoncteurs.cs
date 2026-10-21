using System.Reflection;
using System.Text.Json;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Un disjoncteur du catalogue, une ligne par référence : courbe, pôles, calibre, pouvoirs de coupure et cotes.
/// <see cref="Icn"/> : pouvoir de coupure assigné NF EN 60898-1 (kA) ; <see cref="Icu"/> : pouvoir de coupure ultime
/// NF EN 60947-2 sous 400 V (kA). Pertes : puissance dissipée totale à In (W), 0 quand la fiche ne la donne pas.
/// </summary>
public record ModeleDisjoncteur
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Designation { get; init; } = "";
    /// <summary>« Modulaire » ou « Boîtier moulé ».</summary>
    public string Type { get; init; } = "";
    /// <summary>B, C, D pour les modulaires ; TM pour un déclencheur magnétothermique de boîtier moulé.</summary>
    public string Courbe { get; init; } = "";
    public int Poles { get; init; }
    /// <summary>« 1P+N » quand le neutre est sectionné sans être protégé (Poles = 2).</summary>
    public string PolesTexte { get; init; } = "";
    public double Calibre { get; init; }
    public double? Icn { get; init; }
    public double Icu { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public double Pertes { get; init; }
    public string Fiabilite { get; init; } = "";
    public string Source { get; init; } = "";
    public string Note { get; init; } = "";
    /// <summary>Référence et caractéristiques relevées sur le site ou la documentation du fabricant.</summary>
    public bool Verifie { get; init; }

    public bool Modulaire => Type.StartsWith("Modulaire", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Catalogue des disjoncteurs Schneider, Siemens, ABB et Legrand, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-disjoncteurs.json » de l'utilisateur. Le choix automatique y prend la vraie référence, ses cotes et son
/// pouvoir de coupure ; à défaut, le catalogue des références par gamme sert de repli.
/// </summary>
public static class CatalogueDisjoncteurs
{
    public const string NomFichier = "catalogue-disjoncteurs.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleDisjoncteur>> Embarque = new(() => Lire(TexteEmbarque()));

    /// <summary>Catalogue utilisé par le calcul ; l'application le remplace par celui du dossier utilisateur.</summary>
    public static IReadOnlyList<ModeleDisjoncteur> Actif { get; set; } = Embarque.Value;

    public static IReadOnlyList<ModeleDisjoncteur> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-disjoncteurs.json")
                        ?? throw new InvalidOperationException("Catalogue des disjoncteurs introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleDisjoncteur> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleDisjoncteur>>(json, Options) ?? new List<ModeleDisjoncteur>();

    /// <summary>
    /// Disjoncteur de la marque au calibre, aux pôles et à la courbe demandés. Une gamme dont le pouvoir de coupure tient
    /// l'Icc passe avant les autres ; puis le modulaire avant le boîtier moulé, la référence vérifiée avant l'indicative,
    /// et le plus petit pouvoir de coupure suffisant (la gamme la plus courante). Un boîtier moulé n'a pas de courbe B/C/D :
    /// il convient pour toutes. <paramref name="polesTexte"/> (« 1P+N », « 3P+N ») fait préférer l'appareil à neutre
    /// sectionné ; à défaut, l'appareil au même nombre de pôles protégés convient.
    /// </summary>
    public static ModeleDisjoncteur? Choisir(string marque, string courbe, int poles, double calibre, double iccKiloAmperes = 0,
        IReadOnlyList<ModeleDisjoncteur>? catalogue = null, string polesTexte = "") =>
        (catalogue ?? Actif)
            .Where(m => m.Fabricant.Equals(marque, StringComparison.OrdinalIgnoreCase))
            .Where(m => Math.Abs(m.Calibre - calibre) < 1e-6 && m.Poles == poles)
            .Where(m => m.PolesTexte.Length == 0 || m.PolesTexte.Equals(polesTexte, StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.Modulaire || courbe.Length == 0 || m.Courbe.Equals(courbe, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => iccKiloAmperes > 0 && m.Icu < iccKiloAmperes)
            .ThenBy(m => !m.Modulaire)
            .ThenBy(m => !m.PolesTexte.Equals(polesTexte, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => !m.Verifie)
            .ThenBy(m => m.Icu)
            .FirstOrDefault();

    /// <summary>Pôles à neutre sectionné lus dans le libellé de l'appareil (« 1A, 1P+N, courbe C ») ; vide sinon.</summary>
    public static string PolesAvecNeutre(string libelle) =>
        libelle.Contains("1P+N", StringComparison.OrdinalIgnoreCase) ? "1P+N"
        : libelle.Contains("3P+N", StringComparison.OrdinalIgnoreCase) ? "3P+N" : "";

    /// <summary>Fiche d'une référence, pour l'afficher ou la reconnaître.</summary>
    public static ModeleDisjoncteur? Trouver(string reference, IReadOnlyList<ModeleDisjoncteur>? catalogue = null) =>
        (catalogue ?? Actif).FirstOrDefault(m => m.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase));
}
