using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Calculs;

/// <summary>Codes des valeurs calculées qui peuvent être forcées par l'utilisateur.</summary>
public static class CodesCalcul
{
    public const string CourantEmploi = "IB";
    public const string ReglageThermique = "IR";
    public const string CalibreProtection = "IN";
    public const string CalibreContacteur = "KM";
    public const string Section = "S";
    public const string Simultaneite = "KS";
    public const string CalibreGeneral = "QG";
    public const string CalibreSectionneur = "QS";
    public const string SectionArrivee = "SA";
    public const string CalibreAlimentation24V = "G24";
}

/// <summary>Une ligne de note de calcul : la valeur, d'où elle vient et si elle est conforme.</summary>
public class LigneCalcul
{
    /// <summary>Identifiant unique dans le projet, par exemple « Q3-S ».</summary>
    public string Identifiant { get; init; } = "";

    /// <summary>Code forçable (voir <see cref="CodesCalcul"/>), vide pour une simple vérification.</summary>
    public string Code { get; init; } = "";
    public string Libelle { get; init; } = "";
    public string Formule { get; init; } = "";
    public string Entrees { get; init; } = "";
    public string Reference { get; init; } = "";

    /// <summary>Valeur issue du calcul, avant arrondi ou forçage ; null pour une vérification.</summary>
    public double? ValeurCalculee { get; init; }

    /// <summary>Valeur utilisée pour la suite des calculs (calibre normalisé ou valeur forcée).</summary>
    public double? ValeurRetenue { get; init; }
    public string Unite { get; init; } = "";
    public StatutCalcul Statut { get; init; } = StatutCalcul.Info;
    public string Message { get; init; } = "";
    public Forcage? Forcage { get; init; }

    /// <summary>Vrai si l'utilisateur peut imposer une autre valeur.</summary>
    public bool Forcable => Code.Length > 0;

    public string ValeurTexte => ValeurRetenue is { } v ? $"{Format.N(v)} {Unite}".Trim() : "";
}

/// <summary>Note de calcul d'un départ ou de l'arrivée.</summary>
public class NoteDeCalcul
{
    readonly string _prefixe;
    readonly IReadOnlyDictionary<string, Forcage> _forcages;

    public NoteDeCalcul(string prefixe, IReadOnlyDictionary<string, Forcage>? forcages = null)
    {
        _prefixe = prefixe;
        _forcages = forcages ?? new Dictionary<string, Forcage>();
    }

    public List<LigneCalcul> Lignes { get; } = new();

    public StatutCalcul StatutGlobal => Lignes.Count == 0
        ? StatutCalcul.Info
        : Lignes.Max(l => l.Statut);

    /// <summary>
    /// Ajoute une valeur forçable : si l'utilisateur l'a forcée, la valeur forcée est retenue et renvoyée.
    /// </summary>
    public double Valeur(string code, string libelle, double calculee, double retenue, string unite,
        string formule = "", string entrees = "", string reference = "")
    {
        _forcages.TryGetValue(code, out var forcage);
        Lignes.Add(new LigneCalcul
        {
            Identifiant = $"{_prefixe}-{code}",
            Code = code,
            Libelle = libelle,
            Formule = formule,
            Entrees = entrees,
            Reference = reference,
            ValeurCalculee = calculee,
            ValeurRetenue = forcage?.Valeur ?? retenue,
            Unite = unite,
            Statut = forcage is null ? StatutCalcul.Info : StatutCalcul.Forcee,
            Message = forcage is null ? "" : $"Valeur forcée ({forcage.Motif})",
            Forcage = forcage,
        });
        return forcage?.Valeur ?? retenue;
    }

    /// <summary>Ajoute une vérification conforme ou non conforme.</summary>
    public bool Verifier(string libelle, bool conforme, string message, string reference = "",
        StatutCalcul siEchec = StatutCalcul.NonConforme)
    {
        Lignes.Add(new LigneCalcul
        {
            Identifiant = $"{_prefixe}-V{Lignes.Count + 1}",
            Libelle = libelle,
            Reference = reference,
            Statut = conforme ? StatutCalcul.Conforme : siEchec,
            Message = message,
        });
        return conforme;
    }

    /// <summary>Ajoute une préconisation ou une information sans vérification.</summary>
    public void Info(string libelle, string message, string reference = "") =>
        Lignes.Add(new LigneCalcul
        {
            Identifiant = $"{_prefixe}-I{Lignes.Count + 1}",
            Libelle = libelle,
            Reference = reference,
            Message = message,
        });
}
