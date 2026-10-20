using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dimensionnement.Core.Normes;

public enum TypeChamp { Texte, Nombre, Booleen, Liste }

/// <summary>Un champ d'un article de catalogue : sa clé JSON, son libellé, son unité et son rôle dans le navigateur.</summary>
public record ChampCatalogue(string Cle, string Libelle, string Unite = "", TypeChamp Type = TypeChamp.Texte)
{
    /// <summary>Affiché en colonne du tableau.</summary>
    public bool Colonne { get; init; }

    /// <summary>Chiffre clé en tête de la fiche.</summary>
    public bool Cle_ { get; init; }

    /// <summary>Champ de cote (encombrement) : affiché dans l'onglet Encombrement.</summary>
    public bool Cote { get; init; }
}

/// <summary>Une famille de catalogue : son fichier, son texte livré et la description de ses champs.</summary>
public record FamilleCatalogue(string Id, string Nom, string NomFichier, Func<string> TexteEmbarque, IReadOnlyList<ChampCatalogue> Champs)
{
    /// <summary>Lecture typée utilisée par les calculs : un catalogue qu'elle refuse n'est pas enregistré.</summary>
    public Func<string, object> LireType { get; init; } = t => t;

    /// <summary>Champ numérique principal, filtré par une valeur minimale.</summary>
    public string? ChampPrincipal { get; init; }

    /// <summary>Champ qui donne la sous-famille (gamme, type) proposée en filtre.</summary>
    public string ChampGamme { get; init; } = "gamme";

    public string Designation { get; init; } = "Article";

    public ChampCatalogue? Champ(string cle) => Champs.FirstOrDefault(c => c.Cle == cle);
}

/// <summary>
/// Accès générique aux catalogues pour le navigateur : chaque article reste un objet JSON, ce qui garde les champs
/// propres à chaque famille et ceux ajoutés par le navigateur (photos, documents, lien fabricant, historique),
/// ignorés par les calculs.
/// </summary>
public static class BibliothequeCatalogues
{
    public const string Photos = "photos";
    public const string Documents = "documents";
    public const string LienFabricant = "lienFabricant";
    public const string Notes = "notes";
    public const string ModifieLe = "modifieLe";
    public const string ModifiePar = "modifiePar";
    public const string Verifie = "verifie";

    static ChampCatalogue T(string cle, string libelle, bool colonne = false) => new(cle, libelle) { Colonne = colonne };
    static ChampCatalogue N(string cle, string libelle, string unite, bool colonne = false, bool cle_ = false, bool cote = false) =>
        new(cle, libelle, unite, TypeChamp.Nombre) { Colonne = colonne, Cle_ = cle_, Cote = cote };
    static ChampCatalogue B(string cle, string libelle) => new(cle, libelle, "", TypeChamp.Booleen);
    static ChampCatalogue L(string cle, string libelle) => new(cle, libelle, "", TypeChamp.Liste);
    static ChampCatalogue[] Cotes(bool cle = false) =>
        new[] { N("largeur", "Largeur", "mm", true, cle, true), N("hauteur", "Hauteur", "mm", false, false, true), N("profondeur", "Profondeur", "mm", false, false, true) };

    public static IReadOnlyList<FamilleCatalogue> Familles { get; } = new[]
    {
        new FamilleCatalogue("alimentations", "Alimentations et transformateurs", CatalogueAlimentations.NomFichier, CatalogueAlimentations.TexteEmbarque,
            new[] { T("fabricant", "Fabricant"), T("gamme", "Gamme", true), T("reference", "Référence"),
                    T("type", "Technologie (Decoupage / Transformateur)"), T("secondaire", "Sortie", true), L("entrees", "Primaires admis (230, 400-2P, 400-3P)"),
                    N("courant", "Courant de sortie", "A", true, true), N("va", "Puissance assignée", "VA", true, true),
                    N("rendement", "Rendement", "", false, true), N("rang", "Rang (1 standard, 2 haut de gamme)", "") }
                .Concat(Cotes(true)).ToArray())
            { LireType = t => CatalogueAlimentations.Lire(t), ChampPrincipal = "courant", Designation = "Alimentation" },
        new FamilleCatalogue("ihm", "Pupitres IHM", CatalogueIhm.NomFichier, CatalogueIhm.TexteEmbarque,
            new[] { T("fabricant", "Fabricant"), T("gamme", "Gamme", true), T("reference", "Référence"), T("designation", "Désignation", true),
                    N("diagonale", "Diagonale", "\"", true, true), N("consommation", "Consommation 24 V DC", "A", true, true), T("interface", "Interface", true),
                    N("decoupeLargeur", "Découpe largeur", "mm", false, false, true), N("decoupeHauteur", "Découpe hauteur", "mm", false, false, true),
                    N("rang", "Rang (1 standard, 2 confort)", "") }
                .Concat(Cotes()).ToArray())
            { LireType = t => CatalogueIhm.Lire(t), ChampPrincipal = "diagonale", Designation = "Pupitre" },
        new FamilleCatalogue("automates", "CPU automates", CatalogueAutomates.NomFichier, CatalogueAutomates.TexteEmbarque,
            new[] { T("marque", "Marque"), T("gamme", "Gamme", true), T("cpu", "CPU", true), T("reference", "Référence"),
                    N("peripheriquesReseauMax", "Périphériques réseau max.", "", true, true), N("connexionsMax", "Connexions max.", "", true, true),
                    N("memoireTravailKo", "Mémoire de travail", "Ko", false, true), N("entreesSortiesMax", "E/S max.", ""), B("opcUa", "Serveur OPC UA"),
                    T("versionSecurite", "Version de sécurité"), T("familleCartes", "Famille de cartes"), N("cartesMax", "Cartes max.", "", false, true),
                    N("entreesTorIntegrees", "Entrées TOR intégrées", ""), N("sortiesTorIntegrees", "Sorties TOR intégrées", ""),
                    N("entreesAnaIntegrees", "Entrées analogiques intégrées", ""), N("sortiesAnaIntegrees", "Sorties analogiques intégrées", ""),
                    N("zoneEsOctets", "Zone d'E/S", "octets"), N("consommation", "Consommation 24 V DC", "A"), N("pertes", "Pertes", "W"),
                    B("compacte", "Format compact (ET200SP)"), T("source", "Source des données") }
                .Concat(Cotes()).ToArray())
            { LireType = t => CatalogueAutomates.Lire(t), ChampPrincipal = "connexionsMax", Designation = "CPU" },
        new FamilleCatalogue("cartes", "Cartes d'E/S et stations", CatalogueCartesEs.NomFichier, CatalogueCartesEs.TexteEmbarque,
            new[] { T("famille", "Famille d'automates", true), T("type", "Type (DI, DQ, AI, AQ, Interface…)", true), T("reference", "Référence"),
                    T("designation", "Désignation", true), N("voies", "Voies", "", true, true), N("consommation", "Consommation 24 V DC", "A", false, true),
                    N("pertes", "Pertes", "W", false, true), B("compacte", "Haute densité"), B("loge", "Enfichée dans un châssis"),
                    T("accessoire", "Accessoire"), T("accessoireDesignation", "Désignation de l'accessoire") }
                .Concat(Cotes(true)).ToArray())
            { LireType = t => CatalogueCartesEs.Lire(t), ChampPrincipal = "voies", ChampGamme = "famille", Designation = "Carte" },
        new FamilleCatalogue("variateurs", "Variateurs", CatalogueVariateurs.NomFichier, CatalogueVariateurs.TexteEmbarque,
            new[] { T("fabricant", "Fabricant"), T("gamme", "Gamme", true), T("reference", "Référence"), T("taille", "Taille", true),
                    N("kw", "Puissance moteur", "kW", true, true), N("courant", "Courant de sortie", "A", true, true), N("pertes", "Pertes", "W", false, true),
                    N("rang", "Rang", "") }
                .Concat(Cotes(true)).ToArray())
            { LireType = t => CatalogueVariateurs.Lire(t), ChampPrincipal = "kw", Designation = "Variateur" },
        new FamilleCatalogue("securite", "Sécurité machine", CatalogueSecurite.NomFichier, CatalogueSecurite.TexteEmbarque,
            new[] { T("fabricant", "Fabricant"), T("gamme", "Gamme", true), T("type", "Type", true), T("reference", "Référence"),
                    L("fonctions", "Fonctions"), N("capteurs", "Capteurs", "", true, true), N("entrees", "Entrées", "", false, true),
                    N("sorties", "Sorties de sécurité", "", true, true), N("sortiesInfo", "Sorties d'information", ""),
                    N("consommation", "Consommation 24 V DC", "A", false, true), N("extensionsMax", "Extensions max.", ""),
                    N("largeur", "Largeur", "mm", true, false, true) })
            { LireType = t => CatalogueSecurite.Lire(t), ChampPrincipal = "sorties", Designation = "Module" },
        new FamilleCatalogue("enveloppes", "Enveloppes", CatalogueEnveloppes.NomFichier, CatalogueEnveloppes.TexteEmbarque,
            new[] { T("fabricant", "Fabricant"), T("gamme", "Gamme", true), T("type", "Type", true), T("reference", "Référence"),
                    T("referencePlaque", "Référence de la plaque"),
                    N("plaqueLargeur", "Plaque largeur", "mm", false, true, true), N("plaqueHauteur", "Plaque hauteur", "mm", false, true, true) }
                .Concat(Cotes(true)).ToArray())
            { LireType = t => CatalogueEnveloppes.Lire(t), ChampPrincipal = "largeur", Designation = "Enveloppe" },
        new FamilleCatalogue("references", "Appareillage (références)", CatalogueReferences.NomFichier, CatalogueReferences.TexteEmbarque,
            new[] { T("fabricant", "Fabricant"), T("famille", "Famille d'appareil", true), T("gamme", "Gamme", true), T("reference", "Référence"),
                    N("min", "Calibre min.", "A", true, true), N("max", "Calibre max.", "A", true, true), T("courbe", "Courbe"), T("taille", "Taille"),
                    T("classe", "Classe") })
            { LireType = t => CatalogueReferences.Lire(t), ChampPrincipal = "max", ChampGamme = "famille", Designation = "Appareil" },
    };

    static readonly JsonDocumentOptions LectureJson = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonSerializerOptions EcritureJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static List<JsonObject> Lire(string json) =>
        (JsonNode.Parse(json, documentOptions: LectureJson) as JsonArray ?? new JsonArray())
        .OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList();

    /// <summary>Charge le catalogue de l'utilisateur s'il existe, sinon celui livré.</summary>
    public static List<JsonObject> Charger(FamilleCatalogue famille, string? fichierUtilisateur) =>
        Lire(fichierUtilisateur is not null && File.Exists(fichierUtilisateur) ? File.ReadAllText(fichierUtilisateur) : famille.TexteEmbarque());

    /// <summary>Texte JSON du catalogue : un article par objet, avec un en-tête qui rappelle d'où il vient.</summary>
    public static string Ecrire(FamilleCatalogue famille, IEnumerable<JsonObject> articles)
    {
        var tableau = new JsonArray(articles.Select(a => (JsonNode)a.DeepClone()).ToArray());
        var texte = new StringBuilder();
        texte.AppendLine($"// Catalogue « {famille.Nom} » modifié depuis le navigateur de catalogue de Dimensionnement.");
        texte.AppendLine("// verifie = true : valeurs contrôlées sur la fiche du fabricant.");
        texte.Append(tableau.ToJsonString(EcritureJson));
        return texte.ToString();
    }

    public static void Enregistrer(FamilleCatalogue famille, string fichier, IEnumerable<JsonObject> articles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
        // Le texte est entièrement écrit avant de remplacer le fichier : un catalogue n'est jamais laissé à moitié écrit.
        var temporaire = fichier + ".tmp";
        var texte = Ecrire(famille, articles);
        // Le catalogue doit se relire tel que les calculs le liront, sinon il n'est pas enregistré.
        try
        {
            famille.LireType(texte);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Le catalogue ne serait plus lisible par les calculs ({ex.Message}) : vérifier les nombres entiers (voies, rang…).", ex);
        }
        File.WriteAllText(temporaire, texte);
        File.Move(temporaire, fichier, true);
    }

    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Valeur affichable d'un champ : nombre au format français avec son unité, liste séparée par des virgules.</summary>
    public static string Texte(JsonObject article, ChampCatalogue champ, bool avecUnite = true)
    {
        var noeud = article[champ.Cle];
        if (noeud is null) return "";
        var texte = noeud switch
        {
            JsonArray liste => string.Join(", ", liste.Select(x => x?.ToString() ?? "")),
            JsonValue v when v.TryGetValue(out bool b) => b ? "oui" : "non",
            JsonValue v when v.TryGetValue(out double d) => d.ToString("0.###", Fr),
            _ => noeud.ToString(),
        };
        return avecUnite && champ.Unite.Length > 0 && texte.Length > 0 ? $"{texte} {champ.Unite}" : texte;
    }

    public static double? Nombre(JsonObject article, string cle) =>
        article[cle] is JsonValue v && v.TryGetValue(out double d) ? d : null;

    public static string Chaine(JsonObject article, string cle) => article[cle]?.ToString() ?? "";

    public static bool EstVerifie(JsonObject article) => article[Verifie] is JsonValue v && v.TryGetValue(out bool b) && b;

    public static string Marque(JsonObject article) => Chaine(article, "fabricant") is { Length: > 0 } f ? f : Chaine(article, "marque");

    public static IReadOnlyList<string> ListeTexte(JsonObject article, string cle) =>
        article[cle] is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() : Array.Empty<string>();

    /// <summary>
    /// Enregistre une saisie dans un champ : nombre (virgule ou point), booléen, liste séparée par des virgules ou texte.
    /// Renvoie un message d'erreur, ou null si la saisie est acceptée.
    /// </summary>
    public static string? Saisir(JsonObject article, ChampCatalogue champ, string saisie)
    {
        saisie = saisie.Trim();
        switch (champ.Type)
        {
            case TypeChamp.Nombre:
                if (saisie.Length == 0) { article.Remove(champ.Cle); return null; }
                if (!double.TryParse(saisie.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return $"{champ.Libelle} : « {saisie} » n'est pas un nombre.";
                if (d < 0) return $"{champ.Libelle} : la valeur doit être positive.";
                article[champ.Cle] = d;
                return null;
            case TypeChamp.Booleen:
                article[champ.Cle] = saisie is "oui" or "true" or "1" or "vrai";
                return null;
            case TypeChamp.Liste:
                article[champ.Cle] = new JsonArray(saisie.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
                return null;
            default:
                article[champ.Cle] = saisie;
                return null;
        }
    }

    /// <summary>Points à compléter d'un article : référence, cotes et chiffres clés manquants, doublon de référence.</summary>
    public static IReadOnlyList<string> Controler(FamilleCatalogue famille, JsonObject article, IEnumerable<JsonObject> catalogue)
    {
        var points = new List<string>();
        var reference = Chaine(article, "reference");
        if (reference.Length == 0) points.Add("Référence à compléter");
        else if (catalogue.Count(a => !ReferenceEquals(a, article) && Chaine(a, "reference") == reference && Marque(a) == Marque(article)) > 0)
            points.Add($"Référence {reference} déjà présente dans le catalogue");
        foreach (var c in famille.Champs.Where(c => (c.Cote || c.Cle_) && c.Type == TypeChamp.Nombre))
        {
            // La puissance en VA ne concerne que les transformateurs.
            if (c.Cle == "va" && Chaine(article, "type") != "Transformateur") continue;
            if (Nombre(article, c.Cle) is null) points.Add($"{c.Libelle} non renseigné(e)");
        }
        if (Nombre(article, "rendement") is { } eta && (eta < 0.5 || eta > 0.99))
            points.Add("Rendement hors plage plausible (0,5 à 0,99)");
        if (!EstVerifie(article)) points.Add("Valeurs non vérifiées sur la fiche du fabricant");
        return points;
    }

    /// <summary>Note l'auteur et la date de la dernière modification d'un article.</summary>
    public static void Tracer(JsonObject article, string auteur)
    {
        article[ModifieLe] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        article[ModifiePar] = auteur;
    }

    /// <summary>Filtre du tableau : texte libre sur toutes les valeurs, marque, gamme, valeur minimale du champ principal, vérifiés seulement.</summary>
    public static IEnumerable<JsonObject> Filtrer(FamilleCatalogue famille, IEnumerable<JsonObject> articles, string recherche,
        string? marque, string? gamme, double? minimum, bool verifiesSeulement) =>
        articles.Where(a =>
            (string.IsNullOrWhiteSpace(recherche) || famille.Champs.Any(c => Texte(a, c, false).Contains(recherche.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (string.IsNullOrEmpty(marque) || Marque(a).Equals(marque, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(gamme) || Chaine(a, famille.ChampGamme) == gamme)
            && (minimum is null || famille.ChampPrincipal is null || (Nombre(a, famille.ChampPrincipal) ?? 0) >= minimum - 1e-9)
            && (!verifiesSeulement || EstVerifie(a)));
}
