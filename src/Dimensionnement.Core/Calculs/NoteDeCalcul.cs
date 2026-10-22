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
    public const string CalibreFrein = "QF";
}

/// <summary>Une ligne de note de calcul : la valeur, d'où elle vient et si elle est conforme.</summary>
public class LigneCalcul
{
    /// <summary>Identifiant unique dans le projet, par exemple « Q3-S ».</summary>
    public string Identifiant { get; init; } = "";

    /// <summary>Code forçable (voir <see cref="CodesCalcul"/>), vide pour une simple vérification.</summary>
    public string Code { get; init; } = "";
    public string Libelle { get; init; } = "";

    /// <summary>Carte de la note qui regroupe la ligne avec les calculs qui la préparent (« Câble », « Protection »…).</summary>
    public string Groupe { get; init; } = "";
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

    /// <summary>Préfixe des identifiants de lignes (« ARR », « D01 », « TH »…).</summary>
    public string Prefixe => _prefixe;

    /// <summary>Nom de la carte qui reçoit les lignes ajoutées à partir de maintenant.</summary>
    public string Groupe { get; set; } = "";

    /// <summary>Carte des lignes sans point de contrôle ni groupe explicite.</summary>
    public const string GroupeInformations = "Informations";

    /// <summary>
    /// La note en cartes : une par groupe, dans l'ordre d'apparition, les lignes sans groupe à la fin sous « Informations ».
    /// Chaque carte sépare ses points de contrôle (vérifications et valeurs forcées) des calculs qui les préparent.
    /// </summary>
    public IReadOnlyList<GroupeCalcul> Groupes
    {
        get
        {
            var groupes = new List<GroupeCalcul>();
            foreach (var l in Lignes)
            {
                var nom = l.Groupe.Length > 0 ? l.Groupe : GroupeInformations;
                var g = groupes.FirstOrDefault(x => x.Nom == nom);
                if (g is null) groupes.Add(g = new GroupeCalcul(nom, $"{_prefixe}|{nom}"));
                g.Lignes.Add(l);
            }
            var infos = groupes.FirstOrDefault(g => g.Nom == GroupeInformations);
            if (infos is not null) { groupes.Remove(infos); groupes.Add(infos); }
            return groupes;
        }
    }

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
            Groupe = Groupe,
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
            Groupe = Groupe,
            Reference = reference,
            Statut = conforme ? StatutCalcul.Conforme : siEchec,
            Message = message,
        });
        return conforme;
    }

    /// <summary>
    /// Change une ligne d'information déjà écrite en vérification, une fois connu ce qui permet de conclure (ex. le pouvoir
    /// de coupure de la référence retenue) ; sans effet si la ligne n'existe pas.
    /// </summary>
    public void Requalifier(string libelle, bool conforme, string message, StatutCalcul siEchec = StatutCalcul.NonConforme)
    {
        var i = Lignes.FindIndex(l => l.Libelle == libelle && l.Statut == StatutCalcul.Info && !l.Forcable);
        if (i < 0) return;
        var l = Lignes[i];
        Lignes[i] = new LigneCalcul
        {
            Identifiant = l.Identifiant, Libelle = l.Libelle, Groupe = l.Groupe, Reference = l.Reference,
            Statut = conforme ? StatutCalcul.Conforme : siEchec, Message = message,
        };
    }

    /// <summary>Ajoute une préconisation ou une information sans vérification.</summary>
    public void Info(string libelle, string message, string reference = "") =>
        Lignes.Add(new LigneCalcul
        {
            Identifiant = $"{_prefixe}-I{Lignes.Count + 1}",
            Libelle = libelle,
            Groupe = Groupe,
            Reference = reference,
            Message = message,
        });
}

/// <summary>Une carte de la note de calcul : un objet contrôlé, ses vérifications et les calculs qui les préparent.</summary>
public class GroupeCalcul
{
    public GroupeCalcul(string nom, string cle) { Nom = nom; Cle = cle; }

    public string Nom { get; }

    /// <summary>Clé stable d'une note à l'autre (préfixe et nom), pour mémoriser l'état plié ou déplié.</summary>
    public string Cle { get; }

    public List<LigneCalcul> Lignes { get; } = new();

    /// <summary>Vérifications et valeurs forcées : toujours visibles.</summary>
    public IReadOnlyList<LigneCalcul> Controles => Lignes.Where(l => l.Statut != StatutCalcul.Info).ToList();

    /// <summary>Lignes grises : valeurs calculées et informations, pliées sous « Note de calcul ».</summary>
    public IReadOnlyList<LigneCalcul> Calculs => Lignes.Where(l => l.Statut == StatutCalcul.Info).ToList();

    public StatutCalcul Statut => Lignes.Count == 0 ? StatutCalcul.Info : Lignes.Max(l => l.Statut);

    public bool SansControle => Lignes.All(l => l.Statut == StatutCalcul.Info);

    /// <summary>Valeur mise en avant dans l'en-tête : la dernière valeur dimensionnante du groupe.</summary>
    public string ValeurCle => Lignes.LastOrDefault(l => l.Forcable && l.ValeurTexte.Length > 0)?.ValeurTexte ?? "";

    /// <summary>« 2 vérifications conformes · 5 calculs », « 1 à vérifier, 1 forcée · 3 calculs »…</summary>
    public string Resume
    {
        get
        {
            var controles = Controles;
            var calculs = Lignes.Count - controles.Count;
            var parties = new List<string>();
            if (controles.Count > 0)
            {
                var conformes = controles.Count(l => l.Statut == StatutCalcul.Conforme);
                var forcees = controles.Count(l => l.Statut == StatutCalcul.Forcee);
                var alertes = controles.Count(l => l.Statut == StatutCalcul.Alerte);
                var nonConformes = controles.Count(l => l.Statut == StatutCalcul.NonConforme);
                if (conformes == controles.Count)
                    parties.Add(conformes == 1 ? "1 vérification conforme" : $"{conformes} vérifications conformes");
                else
                {
                    if (nonConformes > 0) parties.Add(nonConformes == 1 ? "1 non conforme" : $"{nonConformes} non conformes");
                    if (alertes > 0) parties.Add($"{alertes} à vérifier");
                    if (forcees > 0) parties.Add(forcees == 1 ? "1 forcée" : $"{forcees} forcées");
                    if (conformes > 0) parties.Add(conformes == 1 ? "1 conforme" : $"{conformes} conformes");
                }
            }
            var texte = string.Join(", ", parties);
            if (calculs > 0)
                texte += (texte.Length > 0 ? " · " : "") + (calculs == 1 ? "1 calcul" : $"{calculs} calculs");
            return texte;
        }
    }

    /// <summary>Libellé du pli des calculs : « Note de calcul (5 lignes) ».</summary>
    public string TitreCalculs
    {
        get { var n = Lignes.Count(l => l.Statut == StatutCalcul.Info); return n == 1 ? "Note de calcul (1 ligne)" : $"Note de calcul ({n} lignes)"; }
    }

    /// <summary>Une carte s'ouvre d'elle-même quand elle porte un point de contrôle.</summary>
    public bool OuvertParDefaut => !SansControle;

    /// <summary>Les calculs se déplient d'eux-mêmes quand une vérification n'est pas bonne.</summary>
    public bool CalculsOuvertsParDefaut => Statut >= StatutCalcul.Alerte;
}
