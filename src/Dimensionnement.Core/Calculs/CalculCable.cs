using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public record ResultatCable(double Section, double CourantAdmissible, double ChuteTension, double SectionPe)
{
    /// <summary>Chute de tension en amont du départ, en % : la chute cumulée vaut ChuteAmont + ChuteTension.</summary>
    public double ChuteAmont { get; init; }

    /// <summary>Courant de court-circuit minimal en bout de câble (défaut phase-PE, méthode conventionnelle), en A ; 0 si non calculé.</summary>
    public double IccMin { get; init; }

    /// <summary>Longueur maximale protégée contre les contacts indirects pour la section retenue, en m ; 0 si non calculée.</summary>
    public double LongueurMax { get; init; }
}

/// <summary>
/// Protection d'un départ vue du défaut d'isolement : courant Ia qui la fait déclencher dans le temps exigé (seuil magnétique
/// haut), ou l'indication qu'un différentiel, des fusibles ou un variateur assurent la coupure.
/// </summary>
public record ProtectionDefaut(double Ia, string Libelle)
{
    /// <summary>Un différentiel protège le départ : la coupure ne dépend pas de la longueur.</summary>
    public bool Differentiel { get; init; }

    /// <summary>Fusibles : Ia se lit sur la courbe de fusion, le contrôle reste à faire sur la courbe du fabricant.</summary>
    public bool Fusibles { get; init; }

    /// <summary>Câble moteur en aval d'un variateur : le défaut est vu et coupé par le variateur.</summary>
    public bool Variateur { get; init; }

    /// <summary>Temps de coupure exigé, en s (0,4 s pour un circuit terminal jusqu'à 32 A, 5 s sinon).</summary>
    public double TempsMax { get; init; } = 5;
}

/// <summary>Choix de la section d'un câble cuivre : courant admissible puis chute de tension.</summary>
public static class CalculCable
{
    const string RefIz = "NF C 15-100, 523 et annexe B de la partie 5-52";
    const string RefDu = "NF C 15-100, 525 ; guide UTE C 15-105";

    /// <param name="courantProtection">Courant assigné ou de réglage de la protection, que Iz doit couvrir.</param>
    /// <param name="courantEmploi">Courant d'emploi qui circule dans le câble, pour la chute de tension.</param>
    /// <param name="chuteAmont">Chute de tension déjà consommée en amont, en %.</param>
    /// <summary>Vrai : en TN-C, la section permet un conducteur PEN (10 mm² cuivre au moins).</summary>
    public static bool Pen(double section) => section >= 10;

    /// <param name="defaut">Protection vue du défaut d'isolement ; null pour le câble d'arrivée, protégé en amont de l'armoire.</param>
    /// <param name="moteurTri230">Câble d'un moteur triphasé 230 V en sortie d'un variateur à entrée monophasée.</param>
    public static ResultatCable Dimensionner(NoteDeCalcul note, CaracteristiquesCable cable, TypeAlimentation alimentation,
        double courantProtection, double courantEmploi, double cosPhi, Reseau reseau, double chuteAmont,
        ProtectionDefaut? defaut = null, bool moteurTri230 = false)
    {
        var conducteursCharges = !moteurTri230 && alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 2 : 3;
        var kt = TablesCables.FacteurTemperature(cable.Isolant, reseau.TemperatureAmbiante);
        if (reseau.TemperatureAmbiante > TablesCables.TemperatureMaxTable(cable.Isolant))
            note.Verifier("Température ambiante", false,
                $"{N(reseau.TemperatureAmbiante)} °C dépasse le tableau B.52.14 pour un isolant {cable.Isolant} " +
                $"({N(TablesCables.TemperatureMaxTable(cable.Isolant))} °C au plus) : choisir un câble PR (polyéthylène réticulé) ou un câble spécial haute température.",
                RefIz);
        var kg = TablesCables.FacteurGroupement(cable.Pose, cable.CircuitsGroupes);
        var limite = TablesCables.ChuteTensionMax(reseau.Origine);
        // Départ de l'équipement de la machine : NF EN 60204-1 limite en plus la chute du départ seul à 5 %.
        var limiteMachine = defaut is null ? double.MaxValue : TablesCables.ChuteTensionMachine;

        double Iz(double s) => TablesCables.CourantAdmissible(cable.Pose, cable.Isolant, conducteursCharges, s) * kt * kg;
        double Du(double s) => ChuteTension(alimentation, cable.Longueur, s, courantEmploi, cosPhi, moteurTri230);
        // Contacts indirects en TN et IT : le courant de défaut minimal doit faire déclencher la protection dans le temps exigé.
        var controleDefaut = defaut is { Differentiel: false, Fusibles: false, Variateur: false, Ia: > 0 }
                             && reseau.Slt != SchemaLiaisonTerre.TT && cable.Longueur > 0;
        double Icc(double s) => IccMinimal(reseau.Slt, alimentation, cable.Longueur, s);

        double? sectionIz = null, sectionDu = null, sectionDefaut = controleDefaut ? null : Calibres.Sections[0];
        foreach (var s in Calibres.Sections)
        {
            sectionIz ??= Iz(s) >= courantProtection ? s : null;
            sectionDu ??= chuteAmont + Du(s) <= limite && Du(s) <= limiteMachine ? s : null;
            sectionDefaut ??= Icc(s) >= defaut!.Ia ? s : null;
        }
        var calculee = Math.Max(Math.Max(sectionIz ?? double.MaxValue, sectionDu ?? double.MaxValue), sectionDefaut ?? double.MaxValue);
        var trouvee = calculee < double.MaxValue;
        var retenueCalcul = trouvee ? calculee : Calibres.Sections[^1];

        var entrees = $"pose {cable.Pose}, {cable.Isolant}, {conducteursCharges} conducteurs chargés, " +
                      $"{N(reseau.TemperatureAmbiante)} °C (K1 = {N(kt)}), {cable.CircuitsGroupes} circuit(s) (K2 = {N(kg)}), " +
                      $"L = {N(cable.Longueur)} m";
        var section = note.Valeur(CodesCalcul.Section, "Section des conducteurs de phase",
            trouvee ? calculee : retenueCalcul, retenueCalcul, "mm²",
            "Plus petite section telle que Iz × K1 × K2 ≥ In, ΔU ≤ limite" + (controleDefaut ? " et Icc mini ≥ Ia (contacts indirects)" : ""),
            entrees, RefIz);

        if (!trouvee)
            note.Verifier("Section disponible", false,
                "Aucune section jusqu'à 240 mm² ne convient : prévoir des câbles en parallèle.", RefIz);

        var iz = Iz(section);
        note.Verifier("Courant admissible", iz >= courantProtection,
            $"Iz corrigé = {A(iz)} pour {Mm2(section)}, protection = {A(courantProtection)}", RefIz);

        var du = Du(section);
        note.Verifier("Chute de tension", chuteAmont + du <= limite && du <= limiteMachine,
            $"ΔU départ = {Pct(du)}, cumulée avec l'amont = {Pct(chuteAmont + du)}, limite = {Pct(limite)}" +
            (defaut is null ? "" : $" ; départ seul ≤ {Pct(TablesCables.ChuteTensionMachine)} (NF EN 60204-1, 12.5)"), RefDu);

        var pe = TablesCables.SectionPe(section);
        // Le PE est pris dans le même câble, à la section du tableau 54.2 : le contrôle compare au minimum de ce tableau.
        note.Verifier("Section du conducteur de protection", pe >= TablesCables.SectionPe(section),
            $"PE = {Mm2(pe)} ≥ minimum du tableau 54.2 pour {Mm2(section)} de phase (PE dans le même câble).", "NF C 15-100, tableau 54.2");

        double iccMin = 0, longueurMax = 0;
        if (defaut is not null && cable.Longueur > 0)
        {
            note.Groupe = "Contacts indirects";
            if (reseau.Slt == SchemaLiaisonTerre.TT)
            {
                // En TT la coupure est assurée par un différentiel (sur le départ ou en amont) : pas de longueur maximale.
            }
            else if (defaut.Variateur)
                note.Info("Contacts indirects", "Câble moteur en aval du variateur : un défaut à la terre est détecté et coupé par le variateur " +
                    "(protection de sortie) ; liaison équipotentielle du moteur et du blindage obligatoire. Vérifier cette fonction dans la notice du variateur.",
                    RefDefaut);
            else
            {
                iccMin = Icc(section);
                longueurMax = defaut.Ia > 0 ? cable.Longueur * iccMin / defaut.Ia : 0;
                var formule = $"I_{{cc,min}} = {FormuleIcc(reseau.Slt, alimentation)} = {A(iccMin)} pour {Mm2(section)} (PE {Mm2(pe)}), L = {N(cable.Longueur)} m";
                if (defaut.Differentiel)
                    note.Info("Contacts indirects", $"{formule} ; coupure assurée par le différentiel du départ, quelle que soit la longueur.", RefDefaut);
                else if (defaut.Fusibles)
                    note.Verifier("Contacts indirects", false,
                        $"{formule}. Vérifier sur la courbe de fusion du fabricant que la cartouche {defaut.Libelle} fond en moins de " +
                        $"{N(defaut.TempsMax)} s sous ce courant (en général au moins 5 à 8 × In pour 0,4 s).", RefDefaut, StatutCalcul.Alerte);
                else
                    note.Verifier("Contacts indirects", iccMin >= defaut.Ia,
                        $"{formule} ; {defaut.Libelle} : Ia = {A(defaut.Ia)} pour une coupure en moins de {N(defaut.TempsMax)} s. " +
                        (iccMin >= defaut.Ia
                            ? $"Longueur maximale protégée ≈ {N(longueurMax, 0)} m."
                            : $"Longueur maximale protégée ≈ {N(longueurMax, 0)} m < {N(cable.Longueur)} m : augmenter la section, " +
                              "ajouter un différentiel sur le départ ou choisir une courbe plus basse."), RefDefaut);
            }
        }
        if (reseau.Slt == SchemaLiaisonTerre.TNC && alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Tetra400)
            note.Verifier("Conducteur PEN", true, Pen(section)
                ? $"Section {Mm2(section)} ≥ 10 mm² : neutre et protection confondus (PEN), un conducteur de moins."
                : $"Section {Mm2(section)} < 10 mm² : le PEN est interdit, le circuit est en TN-S (neutre et PE séparés).",
                "NF C 15-100, 543.4");

        return new ResultatCable(section, iz, du, pe) { ChuteAmont = chuteAmont, IccMin = iccMin, LongueurMax = longueurMax };
    }

    const string RefDefaut = "NF C 15-100, 411.4 (TN) et 411.6 (IT) ; guide UTE C 15-105, méthode conventionnelle";

    /// <summary>
    /// Courant de défaut phase-PE minimal en bout de câble, méthode conventionnelle (80 % de U0 à l'origine du circuit,
    /// ρ = 1,25 × ρ0, m = S phase / S PE) : TN : 0,8 × U0 × S / (ρ × (1 + m) × L) ; IT, double défaut sur deux départs de même
    /// longueur : moitié de la boucle, × √3 sans neutre distribué.
    /// </summary>
    public static double IccMinimal(SchemaLiaisonTerre slt, TypeAlimentation alimentation, double longueur, double section)
    {
        if (longueur <= 0 || slt == SchemaLiaisonTerre.TT) return double.MaxValue;
        var m = section / TablesCables.SectionPe(section);
        var tn = 0.8 * 230 * section / (TablesCables.ResistiviteCuivre * (1 + m) * longueur);
        if (slt != SchemaLiaisonTerre.IT) return tn;
        var neutre = alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Tetra400;
        return tn / 2 * (neutre ? 1 : Math.Sqrt(3));
    }

    static string FormuleIcc(SchemaLiaisonTerre slt, TypeAlimentation alimentation) => slt == SchemaLiaisonTerre.IT
        ? alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Tetra400
            ? "0,8 × U_0 × S / (2 × ρ × (1 + m) × L)"
            : "0,8 × √3 × U_0 × S / (2 × ρ × (1 + m) × L)"
        : "0,8 × U_0 × S / (ρ × (1 + m) × L)";

    /// <summary>
    /// Chute de tension en % de la tension simple :
    /// ΔU = b × (ρ × L / S × cos φ + λ × L × sin φ) × Ib, avec b = 2 en monophasé et 1 en triphasé.
    /// </summary>
    public static double ChuteTension(TypeAlimentation alimentation, double longueur, double section, double courant, double cosPhi,
        bool moteurTri230 = false)
    {
        // Moteur triphasé 230 V derrière un variateur monophasé : ΔU = √3 × (R cos φ + X sin φ) × I, rapportée à 230 V entre phases.
        if (moteurTri230)
        {
            var sin = Math.Sqrt(Math.Max(0, 1 - cosPhi * cosPhi));
            return 100 * Math.Sqrt(3) * (TablesCables.ResistiviteCuivre * longueur / section * cosPhi
                                         + TablesCables.ReactanceLineique * longueur * sin) * courant / 230;
        }
        var b = alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 2 : 1;
        var sinPhi = Math.Sqrt(Math.Max(0, 1 - cosPhi * cosPhi));
        var du = b * (TablesCables.ResistiviteCuivre * longueur / section * cosPhi
                      + TablesCables.ReactanceLineique * longueur * sinPhi) * courant;
        // Biphasé : boucle entre deux phases, rapportée à la tension composée.
        return 100 * du / (alimentation == TypeAlimentation.Bi400 ? 400 : 230);
    }
}
