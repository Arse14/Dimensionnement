using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Une gamme de barrière immatérielle (paire émetteur / récepteur) ou un scrutateur laser de sécurité,
/// monté sur la machine. Une ligne de barrière couvre une résolution et une plage de hauteurs protégées par pas.
/// </summary>
public record ModeleCapteurSecurite
{
    public const string Barriere = "Barrière immatérielle";
    public const string Scrutateur = "Scrutateur laser";

    public string Fabricant { get; init; } = "";
    public string Gamme { get; init; } = "";
    /// <summary>« Barrière immatérielle » ou « Scrutateur laser ».</summary>
    public string Type { get; init; } = "";
    public string Reference { get; init; } = "";
    /// <summary>Type selon la NF EN IEC 61496 : 2, 3 (scrutateurs) ou 4.</summary>
    public int TypeIec { get; init; }
    /// <summary>PL maximal atteignable (NF EN ISO 13849-1).</summary>
    public string Pl { get; init; } = "";
    /// <summary>Résolution en mm ; espacement des faisceaux d'une barrière multi-faisceaux.</summary>
    public double Resolution { get; init; }
    /// <summary>Nombre de faisceaux d'une barrière multi-faisceaux (0 pour un rideau).</summary>
    public int Faisceaux { get; init; }
    public double HauteurMin { get; init; }
    public double HauteurMax { get; init; }
    public double PasHauteur { get; init; }
    /// <summary>Portée maximale d'une barrière, ou rayon du champ de protection d'un scrutateur, en m.</summary>
    public double Portee { get; init; }
    /// <summary>Angle de balayage d'un scrutateur, en degrés.</summary>
    public double Angle { get; init; }
    /// <summary>Paires de sorties OSSD.</summary>
    public int Ossd { get; init; } = 1;
    /// <summary>Consommation 24 V DC de l'ensemble (émetteur et récepteur), hors charge des OSSD, en A.</summary>
    public double Consommation { get; init; }
    /// <summary>Connectique des câbles de raccordement, une entrée par câble (« M12 5 broches », « M12 8 broches »).</summary>
    public List<string> Connectique { get; init; } = new();
    /// <summary>Bornes utilisées dans l'armoire (alimentation, OSSD, EDM, réarmement, test…).</summary>
    public int Bornes { get; init; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public bool Verifie { get; init; }

    public bool EstBarriere => Type == Barriere;
    public bool MultiFaisceaux => Faisceaux > 0;
}

/// <summary>
/// Catalogue des barrières immatérielles Datasensing (ex-Datalogic), Schneider et Sick et des scrutateurs laser Datasensing,
/// embarqué au format JSON et remplaçable par un fichier « catalogue-capteurs-securite.json » de l'utilisateur.
/// </summary>
public static class CatalogueCapteursSecurite
{
    public const string NomFichier = "catalogue-capteurs-securite.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ModeleCapteurSecurite>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<ModeleCapteurSecurite> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise)
            ? CatalogueAutomates.Completer(Lire(File.ReadAllText(fichierPersonnalise)), Embarque.Value, m => m.Reference)
            : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-capteurs-securite.json")
                        ?? throw new InvalidOperationException("Catalogue des capteurs de sécurité introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ModeleCapteurSecurite> Lire(string json) =>
        JsonSerializer.Deserialize<List<ModeleCapteurSecurite>>(json, Options) ?? new List<ModeleCapteurSecurite>();

    /// <summary>Type minimal de barrière pour un PLr : type 4 dès PL d, type 2 jusqu'à PL c (NF EN IEC 61496-1).</summary>
    public static int TypeMinimal(NiveauPerformance plr) => plr >= NiveauPerformance.d ? 4 : 2;

    /// <summary>Hauteur protégée commandée : la première hauteur de la gamme au moins égale à la hauteur demandée.</summary>
    public static double HauteurRetenue(ModeleCapteurSecurite m, double voulue)
    {
        if (m.MultiFaisceaux || m.PasHauteur <= 0) return m.HauteurMin;
        var pas = Math.Ceiling(Math.Max(0, voulue - m.HauteurMin) / m.PasHauteur - 1e-9);
        return Math.Min(m.HauteurMax, m.HauteurMin + pas * m.PasHauteur);
    }

    /// <summary>
    /// Choisit la barrière ou le scrutateur d'une fonction : référence imposée, sinon la marque demandée (repli sur une autre
    /// marque si elle n'en propose pas), le type IEC, la résolution, la hauteur et la portée nécessaires, au plus juste.
    /// </summary>
    public static (ModeleCapteurSecurite Modele, bool Repli)? Choisir(FonctionSecurite f, IReadOnlyList<ModeleCapteurSecurite> catalogue)
    {
        if (f.ReferenceCapteur.Trim() is { Length: > 0 } imposee &&
            catalogue.FirstOrDefault(m => m.Reference.Length > 0 && m.Reference.Contains(imposee, StringComparison.OrdinalIgnoreCase)) is { } m0)
            return (m0, false);

        var candidats = Candidats(f, catalogue).ToList();
        var marque = candidats.Where(m => m.Fabricant.Equals(f.FabricantCapteur.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        var liste = marque.Count > 0 ? marque : candidats;
        var choix = f.Type == TypeFonctionSecurite.ScrutateurLaser
            ? liste.OrderBy(m => m.Portee).ThenBy(m => m.Consommation).FirstOrDefault()
            // Le type le plus bas admis, la résolution la plus grosse qui détecte encore, la portée la plus courte suffisante.
            : liste.OrderBy(m => m.TypeIec).ThenByDescending(m => m.Resolution).ThenBy(m => m.Portee).FirstOrDefault();
        return choix is null ? null : (choix, marque.Count == 0);
    }

    static IEnumerable<ModeleCapteurSecurite> Candidats(FonctionSecurite f, IReadOnlyList<ModeleCapteurSecurite> catalogue)
    {
        if (f.Type == TypeFonctionSecurite.ScrutateurLaser)
            return catalogue.Where(m => m.Type == ModeleCapteurSecurite.Scrutateur && m.Portee >= f.Portee);

        var typeMin = TypeMinimal(f.PLr);
        var barrieres = catalogue.Where(m => m.EstBarriere && m.TypeIec >= typeMin && m.Portee >= f.Portee);
        return f.Detection switch
        {
            DetectionBarriere.Doigt => barrieres.Where(m => !m.MultiFaisceaux && m.Resolution <= 14 && Couvre(m, f.HauteurProtegee)),
            DetectionBarriere.Main => barrieres.Where(m => !m.MultiFaisceaux && m.Resolution <= 30 && Couvre(m, f.HauteurProtegee)),
            // Protection d'accès du corps : multi-faisceaux dont la hauteur couvre au mieux la hauteur demandée.
            _ => barrieres.Where(m => m.MultiFaisceaux).OrderBy(m => Math.Abs(m.HauteurMin - f.HauteurProtegee)).Take(1)
                .SelectMany(p => barrieres.Where(m => m.MultiFaisceaux && m.Faisceaux == p.Faisceaux)),
        };
    }

    static bool Couvre(ModeleCapteurSecurite m, double hauteur) => hauteur <= m.HauteurMax + 1e-9;
}
