using System.Globalization;

namespace Dimensionnement.Core.Armoire;

/// <summary>Une rubrique d'explication de la vue d'armoire.</summary>
public record Explication(string Titre, string Texte);

/// <summary>
/// Comment l'implantation, les zones, les goulottes et le champ magnétique sont obtenus, avec les valeurs réellement
/// utilisées par le calcul : affiché dans la vue d'armoire pour que chaque tracé se comprenne sans question.
/// </summary>
public static class ExplicationsArmoire
{
    static string N(double v) => v.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));

    public static IReadOnlyList<Explication> Rubriques { get; } = new List<Explication>
    {
        new("Encombrement de chaque appareil",
            "Largeur × hauteur × profondeur et pertes viennent du modèle retenu au catalogue (variateurs, alimentations, automates, cartes d'E/S, " +
            "sécurité, IHM). À défaut, une valeur moyenne de la gamme est prise selon la famille et le calibre (disjoncteur modulaire 18 mm " +
            "par pôle jusqu'à 63 A, contacteur 45 mm jusqu'à 38 A…). Les valeurs marquées « indicatives » sont à vérifier sur la fiche du " +
            "fabricant ; une saisie dans le navigateur de catalogues les remplace. Les appareils hors armoire (moteurs, capteurs de sécurité, " +
            "périphériques machine, stations déportées en coffret) ne sont pas posés sur la plaque."),
        new("Zones CEM",
            "Chaque appareil reçoit une zone selon sa famille. Zone propre (vert, en haut) : automate, cartes d'E/S, sécurité. " +
            "Zone intermédiaire (bleu) : protections, alimentations, transformateurs, relais. Zone sale (rouge hachuré, en bas, près des " +
            "presse-étoupes des câbles moteurs) : variateurs, démarreurs progressifs, contacteurs, relais thermiques et bornier de puissance. " +
            $"Un espace d'au moins {N(CalculArmoire.SeparationCem)} mm sépare la zone propre de la zone sale quand elles se suivent. " +
            "Sur le dessin, chaque rangée prend la couleur de l'appareil le plus émissif qu'elle porte."),
        new("Rangées et rails DIN",
            "Les appareils sont posés dans l'ordre de l'arbre (un départ après l'autre), de gauche à droite, sur des rails horizontaux. " +
            $"Une rangée est pleine quand la largeur utile (plaque moins {N(CalculArmoire.MargeLaterale)} mm de goulotte de chaque côté) " +
            "moins la réserve demandée est atteinte. " +
            $"On laisse {N(CalculArmoire.MargeHaute)} mm libres en haut de plaque, {N(CalculArmoire.MargeBasse)} mm en bas pour l'arrivée des câbles, " +
            $"et {N(CalculArmoire.EntreRangees)} mm entre deux rangées pour la goulotte horizontale. Deux départs différents sont séparés de 5 mm."),
        new("Bloc des variateurs",
            "Variateurs et démarreurs progressifs sont fixés directement sur la plaque, côte à côte sur le côté droit de la zone sale, " +
            $"avec {N(CalculArmoire.DegagementVariateur)} mm de dégagement au-dessus et au-dessous pour leur ventilation et 10 mm entre eux. " +
            $"Une goulotte verticale de {N(CalculArmoire.GoulotteVariateurs)} mm les sépare des rails. Si le bloc laisserait moins de 40 % " +
            "de la largeur aux rails, les variateurs passent en rangées pleines en bas de la zone sale."),
        new("Borniers",
            "Bornier de puissance X1 (zone sale) : par départ, une borne par conducteur actif (neutre compris, 6 en étoile-triangle) plus le PE, " +
            "largeur selon la section " +
            "(6,2 mm jusqu'à 4 mm², 8,2 mm jusqu'à 10 mm², 12,2 mm jusqu'à 35 mm², 16 mm au-delà). Bornier de commande X2 (zone propre) : " +
            "une borne de 5,2 mm par E/S TOR, deux par voie analogique, 9 par barrière immatérielle, 8 par scrutateur, 2 par autre capteur " +
            "de sécurité, plus 10 bornes de distribution 24 V. Les borniers ont leur propre zone en bas de la plaque, au plus près des presse-étoupes : commande à gauche, puissance à droite, séparées de 50 mm, avec 20 % de place en réserve. Un frein à alimentation séparée ajoute deux bornes de puissance, un frein 24 V deux bornes de commande. Un bornier plus long qu'une rangée est coupé en tronçons (X1.1, X1.2…)."),
        new("Choix de l'enveloppe",
            "Toutes les enveloppes de la marque choisie sont essayées de la plus petite surface à la plus grande (coffrets, puis armoires de " +
            "1 à 8 cellules) ; la première où tout tient avec la réserve est retenue. La profondeur est la plus petite du catalogue qui dépasse " +
            "l'appareil le plus profond + 100 mm (plaque, porte, câblage). La liste « Enveloppe » permet d'imposer une enveloppe précise " +
            "du catalogue (marque, cotes, référence et plaque repris) ou des cotes libres. Avec une taille imposée trop juste, la place libre " +
            "demandée est gardée en priorité et l'implantation est allégée par paliers : dégagement bas des variateurs pris sur la marge des " +
            "presse-étoupes, goulottes de 25 mm, goulottes verticales seules, puis plus de goulotte. Ensuite seulement la place libre est réduite " +
            "par pas de 5 %. Ce qui n'entre toujours pas attend dans la zone « À placer » à droite : on peut le glisser dans l'armoire, " +
            "et si tout tient sans chevauchement, l'implantation manuelle est retenue."),
        new("Fils et goulottes",
            "Les fils sont estimés par appareil d'après la section de son départ (puissance) et ses voies câblées (commande 0,75 mm²). " +
            "Les fils d'une rangée passent dans la goulotte horizontale qui la longe, puis descendent par les goulottes verticales jusqu'aux " +
            $"borniers. Chaque goulotte est la plus petite du catalogue dont le remplissage reste ≤ {N(CalculCablage.RemplissageMax)} % " +
            "(diamètres des fils H07V-K) ; le gabarit trouvé est réservé et l'implantation recalculée."),
        new("Champ magnétique",
            "Sources : variateurs et alimentations à découpage (k = 3, harmoniques et commutation), démarreurs progressifs (k = 2), " +
            "contacteurs (k = 1). Chaque source est un faisceau de conducteurs d'écart e = √3 × 25 mm en triphasé (25 mm en monophasé) : " +
            "H = k × I × e / (2π × d²), avec I le courant de l'appareil ; les contributions sont additionnées sans tenir compte des phases, " +
            "ce qui majore le champ réel. Courbes tracées : 10 A/m (orange), 30 A/m (framboise, immunité exigée en industrie, " +
            "NF EN 61000-6-2) et 100 A/m (violet). Chaque appareil de la zone propre est contrôlé en 9 points (coins, milieux, centre) : " +
            "au-delà de 30 A/m, une alerte nomme la source principale."),
        new("Déplacements à la main",
            "Un appareil glissé à la souris garde sa position (pas de 5 mm, bord orange) même après un nouveau calcul ; la position est liée " +
            "au repère calculé, elle suit donc un appareil renommé. Les contrôles signalent un appareil hors rail, un chevauchement, ou un " +
            "mélange de zones propre et sale sur une même rangée. « Réinitialiser l'implantation » efface les positions, rails et goulottes " +
            "ajoutés. Clic droit sur un appareil : le renommer."),
        new("Coffrets déportés",
            "Clic droit sur un appareil : « Déporter vers » l'envoie dans un autre coffret, « Déporter tout son départ » emmène aussi sa " +
            "protection, son contacteur et son variateur. « Nouveau coffret » en crée un (jusqu'à 8), aux réglages de l'armoire principale. " +
            "Quand du matériel attend dans la zone « À placer », le bouton du bandeau rouge le déporte d'un coup avec le reste de ses départs. " +
            "Chaque coffret est dimensionné sur son seul matériel (plus petite enveloppe, ou enveloppe imposée avec la liste « Enveloppe »), " +
            "avec son propre bilan thermique, et figure au devis et au rapport. La liste « Afficher » passe d'une enveloppe à l'autre."),
    };
}
