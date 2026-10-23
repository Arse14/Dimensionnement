namespace Dimensionnement.Core.Modele;

/// <summary>Distribution du départ ou de l'arrivée.</summary>
public enum TypeAlimentation
{
    /// <summary>Monophasé 230 V, phase + neutre.</summary>
    Mono230,
    /// <summary>Biphasé 400 V entre deux phases (2P) : transformateur ou alimentation de commande.</summary>
    Bi400,
    /// <summary>Triphasé 400 V sans neutre (3P).</summary>
    Tri400,
    /// <summary>Triphasé 400 V avec neutre (3P+N).</summary>
    Tetra400,
}

/// <summary>Schéma de liaison à la terre.</summary>
public enum SchemaLiaisonTerre { TT, TNC, TNS, IT }

/// <summary>Origine de l'alimentation, qui fixe les limites de chute de tension (NF C 15-100, 525).</summary>
public enum OrigineAlimentation
{
    /// <summary>Réseau public BT : 3 % éclairage, 5 % autres usages.</summary>
    ReseauPublicBT,
    /// <summary>Poste de livraison privé HTA/BT : 6 % éclairage, 8 % autres usages.</summary>
    PostePrive,
}

public enum ModeDemarrage { Direct, Inverseur, EtoileTriangle, Variateur, DemarreurProgressif, Servomoteur, DeuxVitesses }

/// <summary>Moteur deux vitesses : couplage Dahlander (un enroulement) ou deux enroulements séparés.</summary>
public enum TypeDeuxVitesses { Dahlander, DeuxEnroulements }

/// <summary>Condensateur d'un moteur monophasé : dans la boîte à bornes du moteur, ou en armoire (permanent, avec ou sans condensateur de démarrage).</summary>
public enum CondensateurMoteur { DansLeMoteur, Permanent, PermanentEtDemarrage }

/// <summary>Alimentation du frein d'un moteur-frein.</summary>
public enum AlimentationFrein
{
    /// <summary>Redresseur dans la boîte à bornes, alimenté par le moteur (démarrage direct seulement).</summary>
    BoiteABornes,
    /// <summary>Alimentation séparée, tension et nature à préciser (230 V AC par défaut), protégée et commandée par un contacteur.</summary>
    Separee230,
    /// <summary>Frein 24 V DC, commandé par un relais depuis l'alimentation de commande.</summary>
    Continu24,
}

/// <summary>Source de la bobine du frein placée dans l'armoire quand le moteur n'a pas de redresseur ; Automatique = à demander.</summary>
public enum SourceFrein { Automatique, Redresseur, Decoupage, Transformateur }

/// <summary>Qui commande le frein : l'automate (contacteur ou relais) ou la logique de frein du variateur.</summary>
public enum CommandeFrein { Automate, Variateur }

/// <summary>Appareil de protection du départ contre les surintensités.</summary>
public enum TypeProtection { Disjoncteur, Fusibles }

/// <summary>Classes de fusibles (NF EN 60269).</summary>
public enum ClasseFusible
{
    /// <summary>Usage général.</summary>
    gG,
    /// <summary>Accompagnement moteur : court-circuit seulement, surcharge par relais thermique.</summary>
    aM,
    /// <summary>Protection des semi-conducteurs (variateurs, démarreurs progressifs).</summary>
    gR,
}

/// <summary>Méthodes de référence de pose (NF C 15-100 / IEC 60364-5-52).</summary>
public enum MethodePose
{
    /// <summary>A1 : conducteurs isolés en conduit dans une paroi isolante.</summary>
    A1,
    /// <summary>B1 : conducteurs isolés en conduit ou goulotte sur paroi.</summary>
    B1,
    /// <summary>C : câble multiconducteur fixé sur paroi.</summary>
    C,
    /// <summary>E : câble multiconducteur sur chemin de câbles perforé ou à l'air libre.</summary>
    E,
}

public enum Isolant
{
    /// <summary>PVC, 70 °C.</summary>
    PVC,
    /// <summary>PR / EPR (XLPE), 90 °C.</summary>
    PR,
}

public enum CourbeDisjoncteur { B, C, D }

public enum SensibiliteDifferentiel { Aucun, Ma30, Ma300 }

public enum StatutCalcul
{
    /// <summary>Valeur informative, sans vérification associée.</summary>
    Info,
    Conforme,
    /// <summary>Valeur forcée par l'utilisateur.</summary>
    Forcee,
    /// <summary>Point à surveiller sans être une non-conformité.</summary>
    Alerte,
    NonConforme,
}

/// <summary>Périphérique de machine alimenté en 24 V DC par l'armoire et monté sur la machine.</summary>
public enum TypePeripherique
{
    IlotElectrovannes,
    ColonneLumineuse,
    Eclairage24V,
}
