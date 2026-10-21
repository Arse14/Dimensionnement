using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

/// <summary>Une ligne du tableau du bilan : un départ et sa contribution.</summary>
public record LigneBilan(string Depart, string Type, double P, double Q, double Ku, string Remarque)
{
    public double PUtilisee => P * Ku;
    public double QUtilisee => Q * Ku;
    public double S => Math.Sqrt(P * P + Q * Q);
}

/// <summary>Une étape du bilan de puissance : ce qu'on fait, pourquoi, la formule et le résultat.</summary>
public record EtapeBilan(int Numero, string Titre, string Pourquoi, string Formule, string Resultat);

/// <summary>Bilan de puissance expliqué pas à pas, de l'inventaire des départs au câble d'arrivée et aux sources de commande.</summary>
public static class ExplicationBilan
{
    public static IReadOnlyList<LigneBilan> Lignes(ResultatProjet r)
    {
        var alimentes = SourcesCommande.AlimentesPar(r.Projet);
        return r.Departs.Select(d => new LigneBilan($"{d.Depart.Code} {d.Depart.Designation}".Trim(), d.Depart.TypeLibelle,
                d.PuissanceActive, d.PuissanceReactive, alimentes.ContainsKey(d.Depart.Id) ? 0 : d.Depart.CoefficientUtilisation,
                alimentes.ContainsKey(d.Depart.Id) ? "derrière un transformateur 400/230 V : compté au primaire de celui-ci" : ""))
            .ToList();
    }

    public static IReadOnlyList<EtapeBilan> Etapes(ResultatProjet r)
    {
        var lignes = Lignes(r);
        var comptees = lignes.Count(l => l.Ku > 0);
        var p = lignes.Sum(l => l.PUtilisee);
        var q = lignes.Sum(l => l.QUtilisee);
        var reseau = r.Projet.Reseau;
        var mono = reseau.Alimentation == TypeAlimentation.Mono230;
        var etapes = new List<EtapeBilan>();
        void Etape(string titre, string pourquoi, string formule, string resultat) =>
            etapes.Add(new EtapeBilan(etapes.Count + 1, titre, pourquoi, formule, resultat));

        Etape("Inventaire des départs",
            "Chaque récepteur absorbe une puissance active P (travail utile et pertes) et une puissance réactive Q (magnétisation des moteurs, " +
            "transformateurs). Q se déduit de P et du cos φ du récepteur.",
            "Q = P × tan(arccos φ)",
            $"{lignes.Count} départ(s), Σ P = {N(lignes.Sum(l => l.P))} kW, Σ Q = {N(lignes.Sum(l => l.Q))} kvar (détail dans le tableau).");
        Etape("Coefficient d'utilisation ku",
            "Un récepteur ne fonctionne pas toujours à pleine charge : un moteur est dimensionné avec une marge, une prise ne débite pas son calibre. " +
            "ku ramène chaque départ à sa charge réelle. Les départs derrière un transformateur 400/230 V ne sont pas comptés deux fois : " +
            "c'est le primaire du transformateur qui les représente.",
            "P_{utilisée} = P × ku ; Q_{utilisée} = Q × ku",
            $"Σ (P × ku) = {N(p)} kW ; Σ (Q × ku) = {N(q)} kvar.");
        Etape("Coefficient de simultanéité ks",
            "Tous les départs ne fonctionnent pas en même temps. Le guide UTE C 15-105 donne ks selon le nombre de circuits d'une armoire " +
            "de distribution : 1 pour un circuit, 0,9 jusqu'à 3, 0,8 jusqu'à 5, 0,7 jusqu'à 9, 0,6 au-delà. Il peut être forcé dans la note de l'arrivée.",
            "ks = f(nombre de départs)",
            $"{comptees} départ(s) comptés : ks = {N(r.CoefficientSimultaneite)}.");
        Etape("Puissance apparente et cos φ global",
            "Le courant dépend de la puissance apparente S, qui combine P et Q. Le cos φ global indique la part utile ; " +
            "un cos φ faible augmente le courant pour la même puissance utile.",
            "S = ks × √(ΣP² + ΣQ²) ; cos φ = ks × ΣP / S",
            $"S = {N(r.PuissanceApparente)} kVA ; P = {N(r.PuissanceActive)} kW ; Q = {N(r.PuissanceReactive)} kvar ; cos φ = {N(r.CosPhiGlobal)}.");
        Etape("Courant d'emploi de l'armoire",
            mono ? "En monophasé, le courant est la puissance apparente divisée par la tension simple."
                 : "En triphasé équilibré, le courant de ligne est la puissance apparente divisée par √3 fois la tension composée.",
            mono ? "Ib = S / 230 V" : "Ib = S / (√3 × 400 V)",
            $"Ib = {A(r.CourantEmploi)}.");
        Etape("Appareils de tête",
            "Le disjoncteur général doit laisser passer le courant d'emploi en permanence : on retient le calibre normalisé immédiatement supérieur. " +
            "L'interrupteur-sectionneur doit être au moins aussi gros que le disjoncteur qu'il précède (NF EN 60204-1, 5.3).",
            "In ≥ Ib ; I_{th,sectionneur} ≥ In",
            $"Disjoncteur général {A(r.CalibreGeneral)}.");
        Etape("Câble d'arrivée",
            "La section doit supporter le calibre du disjoncteur dans ses conditions de pose (Iz ≥ In, NF C 15-100, 523) " +
            "et limiter la chute de tension jusqu'à l'armoire, qui s'ajoute à celle de chaque départ (NF C 15-100, 525).",
            "Iz (pose, température, groupement) ≥ In ; ΔU % limitée",
            $"Section {N(r.SectionArrivee)} mm², chute de tension {N(r.ChuteTensionArrivee)} %.");
        var commande = Enum.GetValues<TensionCommande>()
            .Select(t => (t, i: r.Consommation(t))).Where(x => x.i > 0)
            .Select(x => $"{SourcesCommande.Libelle(x.t)} : {A(x.i)}").ToList();
        Etape("Sources de commande",
            "Automates, IHM, relais de sécurité, bobines de contacteurs et capteurs sont regroupés par tension (24 V DC, 24 V AC, 230 V AC). " +
            "Chaque source est dimensionnée sur les consommateurs qui lui sont raccordés, avec sa réserve ; elle apparaît ensuite " +
            "comme un départ du bilan, par sa puissance absorbée au primaire.",
            "I_{source} ≥ (1 + r) × Σ I_{consommateurs}, r : réserve ; S_{transformateur} ≥ U × I × k",
            commande.Count == 0 ? "Aucun consommateur de commande." : string.Join(" ; ", commande) +
                (r.SourcesManquantes.Count > 0 ? $". Manque : {string.Join(", ", r.SourcesManquantes.Select(b => SourcesCommande.Libelle(b.Tension)))}." : "."));
        return etapes;
    }
}
