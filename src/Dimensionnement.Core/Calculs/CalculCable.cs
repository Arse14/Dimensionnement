using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public record ResultatCable(double Section, double CourantAdmissible, double ChuteTension, double SectionPe)
{
    /// <summary>Chute de tension en amont du départ, en % : la chute cumulée vaut ChuteAmont + ChuteTension.</summary>
    public double ChuteAmont { get; init; }
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

    public static ResultatCable Dimensionner(NoteDeCalcul note, CaracteristiquesCable cable, TypeAlimentation alimentation,
        double courantProtection, double courantEmploi, double cosPhi, Reseau reseau, double chuteAmont)
    {
        var conducteursCharges = alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 2 : 3;
        var kt = TablesCables.FacteurTemperature(cable.Isolant, reseau.TemperatureAmbiante);
        var kg = TablesCables.FacteurGroupement(cable.Pose, cable.CircuitsGroupes);
        var limite = TablesCables.ChuteTensionMax(reseau.Origine);

        double Iz(double s) => TablesCables.CourantAdmissible(cable.Pose, cable.Isolant, conducteursCharges, s) * kt * kg;
        double Du(double s) => ChuteTension(alimentation, cable.Longueur, s, courantEmploi, cosPhi);

        double? sectionIz = null, sectionDu = null;
        foreach (var s in Calibres.Sections)
        {
            sectionIz ??= Iz(s) >= courantProtection ? s : null;
            sectionDu ??= chuteAmont + Du(s) <= limite ? s : null;
        }
        var calculee = Math.Max(sectionIz ?? double.MaxValue, sectionDu ?? double.MaxValue);
        var trouvee = calculee < double.MaxValue;
        var retenueCalcul = trouvee ? calculee : Calibres.Sections[^1];

        var entrees = $"pose {cable.Pose}, {cable.Isolant}, {conducteursCharges} conducteurs chargés, " +
                      $"{N(reseau.TemperatureAmbiante)} °C (K1 = {N(kt)}), {cable.CircuitsGroupes} circuit(s) (K2 = {N(kg)}), " +
                      $"L = {N(cable.Longueur)} m";
        var section = note.Valeur(CodesCalcul.Section, "Section des conducteurs de phase",
            trouvee ? calculee : retenueCalcul, retenueCalcul, "mm²",
            "Plus petite section telle que Iz × K1 × K2 ≥ In et ΔU totale ≤ limite", entrees, RefIz);

        if (!trouvee)
            note.Verifier("Section disponible", false,
                "Aucune section jusqu'à 240 mm² ne convient : prévoir des câbles en parallèle.", RefIz);

        var iz = Iz(section);
        note.Verifier("Courant admissible", iz >= courantProtection,
            $"Iz corrigé = {A(iz)} pour {Mm2(section)}, protection = {A(courantProtection)}", RefIz);

        var du = Du(section);
        note.Verifier("Chute de tension", chuteAmont + du <= limite,
            $"ΔU départ = {Pct(du)}, cumulée avec l'amont = {Pct(chuteAmont + du)}, limite = {Pct(limite)}", RefDu);

        var pe = TablesCables.SectionPe(section);
        // Le PE est pris dans le même câble, à la section du tableau 54.2 : le contrôle compare au minimum de ce tableau.
        note.Verifier("Section du conducteur de protection", pe >= TablesCables.SectionPe(section),
            $"PE = {Mm2(pe)} ≥ minimum du tableau 54.2 pour {Mm2(section)} de phase (PE dans le même câble).", "NF C 15-100, tableau 54.2");
        if (reseau.Slt == SchemaLiaisonTerre.TNC && alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Tetra400)
            note.Verifier("Conducteur PEN", true, Pen(section)
                ? $"Section {Mm2(section)} ≥ 10 mm² : neutre et protection confondus (PEN), un conducteur de moins."
                : $"Section {Mm2(section)} < 10 mm² : le PEN est interdit, le circuit est en TN-S (neutre et PE séparés).",
                "NF C 15-100, 543.4");

        return new ResultatCable(section, iz, du, pe) { ChuteAmont = chuteAmont };
    }

    /// <summary>
    /// Chute de tension en % de la tension simple :
    /// ΔU = b × (ρ × L / S × cos φ + λ × L × sin φ) × Ib, avec b = 2 en monophasé et 1 en triphasé.
    /// </summary>
    public static double ChuteTension(TypeAlimentation alimentation, double longueur, double section, double courant, double cosPhi)
    {
        var b = alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 2 : 1;
        var sinPhi = Math.Sqrt(Math.Max(0, 1 - cosPhi * cosPhi));
        var du = b * (TablesCables.ResistiviteCuivre * longueur / section * cosPhi
                      + TablesCables.ReactanceLineique * longueur * sinPhi) * courant;
        // Biphasé : boucle entre deux phases, rapportée à la tension composée.
        return 100 * du / (alimentation == TypeAlimentation.Bi400 ? 400 : 230);
    }
}
