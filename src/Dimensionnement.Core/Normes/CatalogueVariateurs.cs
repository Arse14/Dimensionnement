using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Un calibre de variateur 400 V triphasé : puissance moteur et courant de sortie en service normal, taille (frame),
/// encombrement en mm et pertes à pleine charge en W. Rang 1 = gamme machine compacte, 2 = gamme usage général.
/// </summary>
public record ModeleVariateur
{
    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    public int Rang { get; init; } = 1;
    public string Taille { get; init; } = "";
    public double Kw { get; init; }
    public double Courant { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public double Pertes { get; init; }
    public string Reference { get; init; } = "";

    /// <summary>Servovariateur (servomoteur et codeur), à part des variateurs de fréquence.</summary>
    public bool Servo { get; init; }

    /// <summary>Variateur alimenté en monophasé 230 V (les modèles du catalogue sont triphasés 400 V par défaut).</summary>
    public bool Monophase { get; init; }

    /// <summary>Filtre CEM réseau (catégorie C2) intégré au variateur : pas de filtre à ajouter au devis.</summary>
    public bool FiltreCemIntegre { get; init; }

    /// <summary>Plaque ou étriers de reprise de blindage livrés avec le variateur : pas de colliers à ajouter au devis.</summary>
    public bool BlindageFourni { get; init; }

    /// <summary>Vrai une fois la ligne contrôlée sur la fiche du fabricant.</summary>
    /// <summary>Page d'où viennent les cotes.</summary>
    public string Source { get; init; } = "";
    public bool Verifie { get; init; }
}

/// <summary>
/// Catalogue des variateurs ABB, Siemens et Schneider, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-variateurs.json » de l'utilisateur. Encombrements et références indicatifs tant que non vérifiés.
/// </summary>
public static class CatalogueVariateurs
{
    public const string NomFichier = "catalogue-variateurs.json";

    /// <summary>Fabricants proposant des variateurs dans le catalogue.</summary>
    public static readonly FabricantMateriel[] Fabricants = { FabricantMateriel.ABB, FabricantMateriel.Siemens, FabricantMateriel.Schneider };

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleVariateur>> Embarque = new(() => Lire(TexteEmbarque()));

    /// <summary>Catalogue de l'utilisateur complété des variateurs livrés qu'il ne contient pas encore.</summary>
    public static IReadOnlyList<ModeleVariateur> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise)
            ? CatalogueAutomates.Completer(Lire(File.ReadAllText(fichierPersonnalise)), Embarque.Value, m => m.Reference)
            : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-variateurs.json")
                        ?? throw new InvalidOperationException("Catalogue des variateurs introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleVariateur> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleVariateur>>(json, Options) ?? new List<ModeleVariateur>();

    /// <summary>Marque retenue : celle du départ, sinon le fabricant du projet ; Legrand ne fait pas de variateurs, Schneider le remplace.</summary>
    public static FabricantMateriel Marque(MarqueVariateur choix, FabricantMateriel projet) => choix switch
    {
        MarqueVariateur.ABB => FabricantMateriel.ABB,
        MarqueVariateur.Siemens => FabricantMateriel.Siemens,
        MarqueVariateur.Schneider => FabricantMateriel.Schneider,
        _ => Fabricants.Contains(projet) ? projet : FabricantMateriel.Schneider,
    };

    /// <summary>
    /// Meilleur variateur de la marque pour le moteur : parmi ceux qui couvrent sa puissance et son courant, la gamme la plus
    /// adaptée d'abord (machine compacte, puis usage général, puis process), puis le plus petit calibre, puis celui qui évite
    /// du matériel en plus (filtre CEM intégré), puis le moins encombrant sur la plaque et enfin celui qui chauffe le moins.
    /// </summary>
    public static ModeleVariateur? Choisir(FabricantMateriel fabricant, double kw, double courant, IReadOnlyList<ModeleVariateur> catalogue,
        bool servo = false, bool monophase = false) =>
        Candidats(kw, courant, catalogue, servo, monophase)
            .Where(v => v.Fabricant.Equals(fabricant.ToString(), StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    /// <summary>Tous les variateurs du catalogue qui conviennent, du meilleur au moins bon, toutes marques confondues.</summary>
    public static IEnumerable<ModeleVariateur> Candidats(double kw, double courant, IReadOnlyList<ModeleVariateur> catalogue,
        bool servo = false, bool monophase = false) =>
        catalogue
            .Where(v => v.Servo == servo && v.Monophase == monophase && v.Kw >= kw - 1e-6 && v.Courant >= courant - 1e-6)
            .OrderBy(v => v.Rang)
            .ThenBy(v => v.Courant)
            .ThenBy(v => v.FiltreCemIntegre ? 0 : 1)
            .ThenBy(v => v.Largeur * v.Hauteur)
            .ThenBy(v => v.Pertes);

    /// <summary>Variateur imposé par sa référence, quelle que soit sa marque ; null s'il n'est pas au catalogue.</summary>
    public static ModeleVariateur? Trouver(string reference, IReadOnlyList<ModeleVariateur> catalogue) =>
        reference.Trim() is { Length: > 0 } r ? catalogue.FirstOrDefault(v => v.Reference.Equals(r, StringComparison.OrdinalIgnoreCase)) : null;
}
