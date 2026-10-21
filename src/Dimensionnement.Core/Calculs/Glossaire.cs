namespace Dimensionnement.Core.Calculs;

/// <summary>Un symbole d'une formule et ce qu'il représente, avec son unité.</summary>
public record TermeFormule(string Symbole, string Signification);

/// <summary>
/// Une notion du glossaire : ce qu'elle veut dire, la formule employée par le logiciel, chaque terme de la formule,
/// un exemple chiffré et la norme d'où elle vient. <see cref="MotsCles"/> relie les lignes de la note de calcul à l'entrée.
/// </summary>
public record EntreeGlossaire(string Id, string Categorie, string Terme, string Definition, string Formule,
    IReadOnlyList<TermeFormule> Termes, string Exemple, string Reference, IReadOnlyList<string> MotsCles);

/// <summary>Glossaire des coefficients, facteurs de correction et formules utilisés dans les notes de calcul.</summary>
public static class Glossaire
{
    public const string Courants = "Courants et protections";
    public const string Cables = "Câbles";
    public const string Puissances = "Puissances et bilan";
    public const string Moteurs = "Moteurs et freins";
    public const string Commande = "Circuits de commande";
    public const string Armoire = "Armoire";

    static TermeFormule T(string s, string d) => new(s, d);

    public static IReadOnlyList<EntreeGlossaire> Entrees { get; } = new List<EntreeGlossaire>
    {
        new("ib", Courants, "Ib – courant d'emploi",
            "Courant qui circule réellement dans le circuit quand la charge fonctionne. C'est le point de départ de tout le dimensionnement.",
            "Triphasé : Ib = P / (√3 × U × cos φ × η)      Monophasé : Ib = P / (U × cos φ × η)",
            new[] { T("P", "puissance utile de la charge (sur l'arbre pour un moteur), en W"), T("U", "tension entre phases en triphasé (400 V), phase-neutre en monophasé (230 V)"),
                T("√3", "≈ 1,732, propre au triphasé"), T("cos φ", "facteur de puissance (voir cette entrée)"), T("η", "rendement (voir cette entrée) ; 1 pour une résistance") },
            "Moteur 7,5 kW, cos φ 0,82, η 0,89 : Ib = 7 500 / (1,732 × 400 × 0,82 × 0,89) ≈ 14,8 A. Quand le courant plaque est saisi, c'est lui qui est pris.",
            "NF C 15-100, 523", new[] { "Ib", "courant d'emploi", "Courant d'emploi", "courant absorbé" }),

        new("in", Courants, "In – courant assigné de la protection",
            "Calibre du disjoncteur ou du fusible (ou réglage Ir du disjoncteur moteur). Il doit laisser passer le courant d'emploi et protéger le câble.",
            "Ib ≤ In ≤ Iz",
            new[] { T("Ib", "courant d'emploi du circuit"), T("In", "calibre normalisé immédiatement supérieur à Ib (6, 10, 16, 20, 25, 32 A…)"), T("Iz", "courant admissible du câble après corrections") },
            "Ib = 14,8 A → disjoncteur 16 A ; le câble doit alors admettre au moins 16 A.",
            "NF C 15-100, 433", new[] { "In ", "In)", "In,", "calibre", "Calibre", "Disjoncteur", "réglage" }),

        new("iz", Cables, "Iz – courant admissible du câble",
            "Courant que le câble peut transporter en permanence sans dépasser la température de son isolant. La valeur de base vient des tableaux de la norme, puis elle est corrigée selon l'environnement.",
            "Iz = Iz0 × K1 × K2      condition : Iz ≥ In",
            new[] { T("Iz0", "valeur du tableau pour la section, le mode de pose, l'isolant (PVC 70 °C ou PR 90 °C) et le nombre de conducteurs chargés, à 30 °C"),
                T("K1", "facteur de température ambiante"), T("K2", "facteur de groupement des circuits") },
            "2,5 mm² PVC, pose C, triphasé : Iz0 = 24 A ; à 40 °C (K1 = 0,87) avec 3 circuits jointifs (K2 = 0,70) : Iz = 24 × 0,87 × 0,70 ≈ 14,6 A.",
            "NF C 15-100, 523 et annexe B (tableaux B.52)", new[] { "Iz", "Courant admissible", "courant admissible" }),

        new("k1", Cables, "K1 – facteur de correction de température",
            "Un câble chaud évacue moins bien sa chaleur : au-dessus de 30 °C son courant admissible baisse, en dessous il augmente. Le PR supporte mieux la chaleur que le PVC.",
            "K1 = valeur du tableau B.52.14 pour la température ambiante et l'isolant",
            new[] { T("30 °C", "K1 = 1 (référence des tableaux)"), T("PVC", "0,94 à 35 °C, 0,87 à 40 °C, 0,79 à 45 °C, 0,71 à 50 °C"), T("PR", "0,96 à 35 °C, 0,91 à 40 °C, 0,87 à 45 °C, 0,82 à 50 °C") },
            "Armoire en atelier à 40 °C, câble PVC : Iz est multiplié par 0,87.",
            "NF C 15-100, tableau B.52.14", new[] { "K1" }),

        new("k2", Cables, "K2 – facteur de groupement",
            "Des circuits posés côte à côte se chauffent mutuellement : plus il y en a, plus le courant admissible de chacun baisse.",
            "K2 = valeur du tableau B.52.17 selon le nombre de circuits jointifs et le mode de pose",
            new[] { T("1 circuit", "K2 = 1"), T("2 circuits", "≈ 0,80 à 0,88"), T("3 circuits", "≈ 0,70 à 0,82"), T("6 circuits", "≈ 0,57 à 0,73"), T("Circuits groupés", "nombre saisi dans les caractéristiques du câble du départ") },
            "4 câbles moteurs dans le même chemin de câbles : K2 ≈ 0,65 à 0,77 selon la pose.",
            "NF C 15-100, tableau B.52.17", new[] { "K2" }),

        new("pose", Cables, "Méthode de pose",
            "Façon dont le câble est installé. Elle change la manière dont il refroidit, donc la colonne du tableau utilisée pour Iz0.",
            "",
            new[] { T("A1", "conducteurs dans un conduit encastré dans une paroi isolante (le moins favorable)"), T("B1", "conducteurs sous conduit ou goulotte sur une paroi"),
                T("C", "câble fixé directement sur une paroi, en apparent"), T("E", "câble multiconducteur à l'air libre, sur chemin de câbles perforé (le plus favorable)") },
            "Le même 2,5 mm² PVC triphasé admet 18 A en A1, 21 A en B1, 24 A en C et 25 A en E.",
            "NF C 15-100, tableau 52C", new[] { "pose" }),

        new("conducteurs", Cables, "Conducteurs chargés",
            "Nombre de conducteurs parcourus par le courant de la charge. Ils chauffent le câble ; le PE et un neutre non chargé ne comptent pas.",
            "",
            new[] { T("2", "monophasé 230 V ou biphasé 400 V"), T("3", "triphasé (le neutre équilibré n'est pas compté)") },
            "Un moteur triphasé a 3 conducteurs chargés : on lit la colonne « 3 conducteurs » du tableau.",
            "NF C 15-100, 523.6", new[] { "conducteurs chargés" }),

        new("du", Cables, "ΔU – chute de tension",
            "Baisse de tension le long du câble, due à sa résistance et à son inductance. Trop de chute et le moteur démarre mal, les appareils se dérèglent.",
            "ΔU = b × (ρ × L / S × cos φ + λ × L × sin φ) × Ib      puis ΔU % = 100 × ΔU / U0",
            new[] { T("b", "1 en triphasé, 2 en monophasé ou biphasé (aller et retour)"), T("ρ", "résistivité du cuivre en service : 0,0225 Ω·mm²/m"),
                T("L", "longueur du câble, en m"), T("S", "section du conducteur, en mm²"), T("λ", "réactance linéique : 0,08 mΩ/m"),
                T("cos φ, sin φ", "facteur de puissance de la charge et sin φ = √(1 − cos² φ)"), T("Ib", "courant d'emploi, en A"),
                T("U0", "230 V (tension simple) ; 400 V pour un circuit biphasé entre phases") },
            "Moteur 14,8 A, 50 m de 2,5 mm², cos φ 0,82 : ΔU = 1 × (0,0225 × 50 / 2,5 × 0,82 + 0,00008 × 50 × 0,57) × 14,8 ≈ 5,5 V, soit 2,4 %.",
            "NF C 15-100, 525", new[] { "ΔU", "Chute de tension", "chute de tension" }),

        new("du-limite", Cables, "Limite de chute de tension",
            "Chute totale admise entre l'origine de l'installation et la charge : arrivée plus départ, cumulées.",
            "ΔU arrivée + ΔU départ ≤ limite",
            new[] { T("5 %", "installation alimentée par le réseau public basse tension"), T("8 %", "installation alimentée par son propre poste de transformation") },
            "Arrivée 1,2 % + départ 2,4 % = 3,6 % ≤ 5 % : conforme.",
            "NF C 15-100, 525 et tableau 52W", new[] { "limite", "cumulée" }),

        new("section", Cables, "Choix de la section",
            "La section retenue est la plus petite qui respecte à la fois l'échauffement et la chute de tension.",
            "S = max(plus petite S telle que Iz ≥ In ; plus petite S telle que ΔU totale ≤ limite)",
            new[] { T("S", "section normalisée : 1,5 – 2,5 – 4 – 6 – 10 – 16 – 25 – 35 – 50 – 70 – 95 – 120 – 150 – 185 – 240 mm²") },
            "Iz demande 2,5 mm², la chute de tension demande 4 mm² (câble long) : 4 mm² est retenu.",
            "NF C 15-100, 523 et 525", new[] { "Section des conducteurs", "section" }),

        new("pe", Cables, "Section du conducteur de protection (PE)",
            "Le PE évacue le courant de défaut vers la terre ; sa section dépend de celle des phases.",
            "S ≤ 16 mm² : PE = S      16 < S ≤ 35 mm² : PE = 16 mm²      S > 35 mm² : PE = S / 2",
            new[] { T("S", "section des conducteurs de phase") },
            "Phases en 25 mm² : PE en 16 mm².",
            "NF C 15-100, tableau 54.2", new[] { "conducteur de protection", "PE" }),

        new("k3", Courants, "k3 – coefficient des fusibles gG",
            "Un fusible gG met plus de temps à fondre qu'un disjoncteur à déclencher : le câble doit être un peu plus gros que le calibre du fusible.",
            "Iz ≥ k3 × In",
            new[] { T("k3", "1,31 jusqu'à 10 A, 1,21 de 13 à 25 A, 1,10 au-delà"), T("In", "calibre du fusible") },
            "Fusible gG 20 A : le câble doit admettre 1,21 × 20 = 24,2 A.",
            "NF C 15-100, 533.3", new[] { "k3" }),

        new("extension", Cables, "Coefficient d'extension du câble d'arrivée",
            "Coefficient de sécurité choisi par le bureau d'études pour pouvoir ajouter des départs plus tard sans changer le câble d'arrivée.",
            "Ib futur = Ib × k ; le câble est choisi pour le calibre qui protégerait Ib futur ; marge = Iz − Ib",
            new[] { T("k", "coefficient saisi dans le réseau d'arrivée (1 = aucune extension ; 1,2 à 1,5 courant)"), T("Ib", "courant d'emploi actuel de l'armoire"),
                T("Iz", "courant admissible corrigé du câble retenu"), T("marge", "ampères disponibles pour des ajouts, en plus du besoin actuel") },
            "Ib = 40 A, k = 1,3 : câble choisi comme pour 52 A (protection 63 A) ; le disjoncteur général reste à 40 A.",
            "Choix du bureau d'études", new[] { "extension", "Marge en intensité" }),

        new("courbe", Courants, "Courbes de déclenchement B, C, D",
            "Seuil du déclencheur magnétique d'un disjoncteur, choisi selon l'appel de courant à la mise sous tension.",
            "",
            new[] { T("B", "3 à 5 × In : grandes longueurs, charges sans appel"), T("C", "5 à 10 × In : usage général, alimentations à découpage"),
                T("D", "10 à 20 × In : transformateurs, moteurs à fort appel") },
            "Transformateur de commande : courbe D, car il appelle 10 à 25 fois son courant à la mise sous tension.",
            "NF EN 60898-1 et 60947-2", new[] { "courbe" }),

        new("icc", Courants, "Icc et pouvoir de coupure",
            "Icc est le courant de court-circuit présumé à l'endroit de l'armoire. Chaque disjoncteur doit pouvoir couper au moins ce courant.",
            "Pouvoir de coupure (Icu ou Icn) ≥ Icc",
            new[] { T("Icc", "donné par le distributeur ou calculé depuis le transformateur, en kA"), T("Icu / Icn", "pouvoir de coupure assigné indiqué sur le disjoncteur, en kA") },
            "Icc = 10 kA : disjoncteurs d'au moins 10 kA.",
            "NF EN 60947-2", new[] { "Icc", "Pouvoir de coupure" }),

        new("cosphi", Puissances, "cos φ – facteur de puissance",
            "Part de la puissance appelée qui fait du travail utile. Un moteur appelle aussi de la puissance réactive pour magnétiser son circuit, ce qui augmente le courant.",
            "cos φ = P / S      Q = P × tan φ",
            new[] { T("P", "puissance active, en kW"), T("S", "puissance apparente, en kVA"), T("Q", "puissance réactive, en kvar") },
            "Moteur à cos φ 0,82 : pour 7,5 kW utiles il appelle environ 10 kVA.",
            "NF C 15-100", new[] { "cos φ", "cosφ" }),

        new("rendement", Moteurs, "η – rendement",
            "Rapport entre la puissance fournie sur l'arbre et la puissance absorbée au réseau ; le reste part en chaleur.",
            "η = P utile / P absorbée      méthode plaque : P absorbée = √3 × U × I × cos φ      méthode couple et vitesse : P utile = C × 2π × n / 60",
            new[] { T("P utile", "puissance sur l'arbre (plaque), ou C × ω"), T("C", "couple nominal, en N·m"), T("n", "vitesse nominale, en tr/min"),
                T("U, I, cos φ", "tension, courant nominal (IN sous la tension du réseau) et facteur de puissance de la plaque") },
            "5,1 N·m à 1 430 tr/min = 764 W utiles ; √3 × 400 × 1,79 × 0,72 = 893 W absorbés : η ≈ 0,85.",
            "NF EN 60034-2-1", new[] { "Rendement", "rendement", "η" }),

        new("iain", Moteurs, "Id/In – rapport du courant de démarrage",
            "Courant appelé par un moteur au démarrage direct, rapporté à son courant nominal. Il sert à vérifier que le disjoncteur moteur ne déclenche pas au démarrage.",
            "Id = (Id/In) × In ; le seuil magnétique du disjoncteur doit être au-dessus de Id (crête comprise)",
            new[] { T("Id/In", "valeur de la plaque ou du catalogue (souvent noté IA/IN), 5 à 8 en général") },
            "IA/IN = 5,4 et In = 1,79 A : Id ≈ 9,7 A au démarrage.",
            "NF EN 60947-4-1", new[] { "Id/In", "démarrage" }),

        new("frein", Moteurs, "Protection et alimentation du frein",
            "Un frein à alimentation séparée a son propre disjoncteur et son contacteur ; sa bobine en courant continu est alimentée par un redresseur, une alimentation à découpage ou un transformateur suivi d'un pont.",
            "Calibre ≥ max(1 A ; 2 × P / U)      demi-onde : U DC = 0,45 × U AC      pont : U DC = 0,9 × U AC",
            new[] { T("P", "puissance de la bobine, en W"), T("U", "tension d'alimentation du frein"), T("2", "marge prudente pour l'appel de la bobine (règle à valider)") },
            "Bobine 180 V DC sous 400 V AC : demi-onde, 0,45 × 400 = 180 V.",
            "Documentation des fabricants de freins", new[] { "frein", "Frein" }),

        new("ku", Puissances, "ku – coefficient d'utilisation",
            "Part de sa puissance nominale qu'une charge utilise vraiment en fonctionnement normal (un moteur tourne rarement à pleine charge).",
            "P comptée = P × ku",
            new[] { T("ku", "1 par défaut ; 0,75 à 0,8 pour des moteurs peu chargés") },
            "Moteur 7,5 kW à ku = 0,8 : 6 kW comptés dans le bilan.",
            "Guide UTE C 15-105", new[] { "ku" }),

        new("ks", Puissances, "ks – coefficient de simultanéité",
            "Tous les départs ne fonctionnent pas à pleine charge en même temps : le bilan de l'armoire est réduit selon leur nombre.",
            "S armoire = ks × √(Σ(P × ku)² + Σ(Q × ku)²)",
            new[] { T("ks", "1 pour 1 départ, 0,9 pour 2 ou 3, 0,8 pour 4 ou 5, 0,7 de 6 à 9, 0,6 à partir de 10 (forçable)") },
            "8 départs : ks = 0,7.",
            "Guide UTE C 15-105, B.4", new[] { "ks", "simultanéité" }),

        new("s", Puissances, "S – puissance apparente de l'armoire",
            "Puissance que le réseau doit fournir, actif et réactif ensemble ; elle donne le courant d'emploi de l'arrivée.",
            "S = ks × √(P² + Q²)      Ib = S / (√3 × U) en triphasé, S / U en monophasé",
            new[] { T("P", "somme des puissances actives × ku, en kW"), T("Q", "somme des puissances réactives × ku, en kvar"), T("U", "400 V ou 230 V") },
            "P = 30 kW, Q = 20 kvar, ks = 0,8 : S = 28,8 kVA, Ib = 41,6 A.",
            "Guide UTE C 15-105", new[] { "S =", "Puissances utilisées", "puissance apparente" }),

        new("alim24", Commande, "Alimentation 24 V DC et réserve",
            "L'alimentation est choisie sur la consommation de la commande majorée d'une réserve ; son courant primaire donne sa protection.",
            "I sortie ≥ I consommée × (1 + réserve)      I primaire = 24 × I assigné / (η × U × cos φ)",
            new[] { T("réserve", "25 % par défaut"), T("η", "rendement de l'alimentation (catalogue)"), T("U", "230 V ou 400 V selon le primaire"), T("cos φ", "≈ 0,9 pour une alimentation à découpage") },
            "Commande 6,2 A + 25 % = 7,75 A : alimentation 10 A.",
            "Documentation fabricant", new[] { "alimentation 24 V", "Courant assigné de l'alimentation", "réserve" }),

        new("transfo", Commande, "Transformateur de commande",
            "Puissance assignée choisie sur la commande et les départs qu'il alimente ; avec un redresseur filtré, le courant crête des condensateurs majore le besoin.",
            "S = (U × I commande × k + S départs) × (1 + réserve)      I primaire = S / (U × η)",
            new[] { T("k", "1,8 avec un redresseur filtré, 1 sinon"), T("S départs", "puissance des départs 230 V alimentés, en VA"), T("η", "rendement du transformateur") },
            "24 V × 4 A × 1,8 = 173 VA + 25 % = 216 VA : transformateur 250 VA.",
            "NF EN 61558-2-2", new[] { "transformateur", "Puissance assignée du transformateur" }),

        new("thermique", Armoire, "Bilan thermique de l'armoire",
            "Les appareils dissipent de la chaleur ; l'enveloppe en évacue une partie par ses parois. Le reste est sorti par un ventilateur ou un climatiseur.",
            "Ae = 1,4 × L × (H + P) + 1,8 × P × H (armoire adossée)      P évacuée = k × Ae × ΔT      Débit V = 3,1 × (P dissipée − P évacuée) / ΔT",
            new[] { T("Ae", "surface effective d'échange de l'enveloppe, en m² (selon la pose)"), T("L, H, P", "largeur, hauteur, profondeur en m"),
                T("k", "coefficient de transmission de la tôle : 5,5 W/(m²·K) pour l'acier peint"), T("ΔT", "écart admis entre l'intérieur et l'ambiance, en K"),
                T("3,1", "constante de l'air (m³·K/(h·W)) au niveau de la mer"), T("V", "débit du ventilateur, majoré de 30 % pour les filtres") },
            "500 W dissipés, Ae = 2,36 m², ΔT = 5 K : 65 W évacués, V = 3,1 × 435 / 5 ≈ 270 m³/h.",
            "IEC TR 60890", new[] { "Ae", "thermique", "Débit", "Puissance dissipée" }),

        new("reserve", Armoire, "Place libre (réserve) de l'armoire",
            "Part de la longueur des rails laissée vide pour les ajouts futurs et le confort de câblage.",
            "Longueur d'appareils par rail ≤ longueur utile × (1 − réserve)",
            new[] { T("réserve", "25 % par défaut, réglable ; les borniers ont en plus 20 % de place réservée") },
            "Rail utile 700 mm et réserve 25 % : 525 mm d'appareils au maximum.",
            "NF EN 61439-1 (bonne pratique)", new[] { "réserve", "Place libre" }),

        new("champ", Armoire, "Champ magnétique et zones CEM",
            "Les conducteurs de puissance rayonnent un champ qui peut perturber l'électronique ; on l'estime pour placer les appareils sensibles assez loin.",
            "H = I équivalent × e / (2π × d²)      I équivalent = I × facteur d'émission",
            new[] { T("H", "champ en A/m"), T("I", "courant de l'appareil"), T("facteur", "majoration pour les harmoniques et la commutation (variateurs, alimentations à découpage)"),
                T("e", "écart entre conducteurs aller et retour, en m"), T("d", "distance à l'appareil, en m"), T("30 A/m", "immunité d'un appareil industriel") },
            "Variateur à 20 cm d'un automate : champ vérifié inférieur à 30 A/m.",
            "NF EN 61000-6-2 et 61000-4-8", new[] { "champ", "Champ", "CEM", "Séparation" }),
    };

    /// <summary>Entrées utiles pour comprendre une ligne de la note de calcul, d'après son libellé, sa formule et ses données.</summary>
    public static IReadOnlyList<EntreeGlossaire> Pour(LigneCalcul ligne)
    {
        var texte = $"{ligne.Libelle} | {ligne.Formule} | {ligne.Entrees} | {ligne.Message}";
        return Entrees.Where(e => e.MotsCles.Any(m => texte.Contains(m, StringComparison.Ordinal))).ToList();
    }
}
