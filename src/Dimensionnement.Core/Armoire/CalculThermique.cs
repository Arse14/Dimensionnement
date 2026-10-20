using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Armoire;

/// <summary>Équipement de climatisation retenu.</summary>
public enum SolutionThermique { Aucune, Ventilation, Climatisation }

public record PosteDissipation(string Repere, string Designation, double PuissanceW);

public class ResultatThermique
{
    public required NoteDeCalcul Note { get; init; }
    public IReadOnlyList<PosteDissipation> Postes { get; init; } = Array.Empty<PosteDissipation>();

    /// <summary>Puissance dissipée retenue (appareils + câblage), en W.</summary>
    public double PuissanceDissipee { get; init; }

    /// <summary>Surface d'échange effective, en m², et coefficient de transmission, en W/(m²·K).</summary>
    public double SurfaceEffective { get; init; }
    public double Coefficient { get; init; }

    /// <summary>Température intérieure sans équipement, en °C.</summary>
    public double TemperatureInterieure { get; init; }
    public SolutionThermique Solution { get; init; }

    /// <summary>Débit d'air nécessaire et ventilateur à filtre retenu, en m³/h.</summary>
    public double DebitNecessaire { get; init; }
    public double Ventilateur { get; init; }

    /// <summary>Puissance frigorifique nécessaire et climatiseur retenu, en W.</summary>
    public double PuissanceFrigorifique { get; init; }
    public double Climatiseur { get; init; }

    /// <summary>Puissance de chauffage nécessaire et résistance retenue, en W (0 si inutile).</summary>
    public double Chauffage { get; init; }
    public double Resistance { get; init; }
}

/// <summary>
/// Bilan thermique de l'enveloppe par la méthode des surfaces effectives (IEC TR 60890) :
/// ΔT = P / (k × Ae), puis ventilation, climatisation ou chauffage pour tenir la plage de température intérieure.
/// </summary>
public static class CalculThermique
{
    public const string CodePuissance = "PD";
    const string Ref = "IEC TR 60890 ; NF EN 61439-1, 10.10 ; méthode des surfaces effectives";

    /// <summary>Part des pertes du câblage interne, ajoutée à celles des appareils.</summary>
    public const double PartCablage = 0.10;

    /// <summary>Débits libres des ventilateurs à filtre courants, en m³/h.</summary>
    public static readonly double[] Ventilateurs = { 20, 55, 105, 180, 230, 550, 700, 1000 };

    /// <summary>Puissances frigorifiques des climatiseurs d'armoire courants, en W.</summary>
    public static readonly double[] Climatiseurs = { 300, 500, 750, 1000, 1500, 2000, 2500, 3000, 4000 };

    /// <summary>Résistances de chauffage anticondensation courantes, en W.</summary>
    public static readonly double[] Resistances = { 10, 20, 30, 50, 75, 100, 150, 200, 250, 400 };

    /// <summary>Majoration du débit pour les pertes de charge des filtres d'entrée et de sortie.</summary>
    public const double MajorationFiltres = 1.3;

    public static ResultatThermique Calculer(ResultatProjet r, ResultatArmoire armoire, ConfigurationArmoire c)
    {
        var note = new NoteDeCalcul("TH", c.Forcages);
        var postes = Postes(r);
        var appareils = postes.Sum(p => p.PuissanceW);
        var calcule = appareils * (1 + PartCablage);
        var p = note.Valeur(CodePuissance, "Puissance dissipée dans l'armoire", calcule, calcule, "W",
            $"P = Σ pertes des appareils × (Ib / In)² + {N(PartCablage * 100, 0)} % de câblage",
            $"appareils {N(appareils, 0)} W", Encombrements.Source);

        var k = Coefficient(c.Materiau);
        var ae = SurfaceEffective(armoire.LargeurTotale / 1000, armoire.Hauteur / 1000, armoire.Profondeur / 1000, c.Installation);
        note.Info("Surface effective", $"Ae = {N(ae)} m² ({LibelleInstallation(c.Installation)}), k = {N(k)} W/(m²·K) ({LibelleMateriau(c.Materiau)}).", Ref);

        var echauffement = ae > 0 ? p / (k * ae) : 0;
        var ti = c.TemperatureAmbianteMax + echauffement;
        note.Info("Échauffement sans équipement", $"ΔT = P / (k × Ae) = {N(echauffement, 1)} K, soit {N(ti, 1)} °C à l'intérieur pour {N(c.TemperatureAmbianteMax, 0)} °C ambiants.", Ref);

        var solution = SolutionThermique.Aucune;
        double debit = 0, ventilateur = 0, froid = 0, climatiseur = 0;
        if (ti <= c.TemperatureInterieureMax)
        {
            note.Verifier("Température intérieure", true,
                $"{N(ti, 1)} °C ≤ {N(c.TemperatureInterieureMax, 0)} °C : convection naturelle suffisante.", Ref);
        }
        else
        {
            var ecart = c.TemperatureInterieureMax - c.TemperatureAmbianteMax;
            // Puissance que l'enveloppe évacue seule à la température intérieure maximale (négative si l'ambiance est plus chaude).
            var evacuee = k * ae * ecart;
            if (ecart >= 5)
            {
                debit = 3.1 * (p - evacuee) / ecart;
                var besoin = debit * MajorationFiltres;
                if (Calibres.Superieur(Ventilateurs, besoin) is { } v)
                {
                    solution = SolutionThermique.Ventilation;
                    ventilateur = v;
                    note.Verifier("Température intérieure", false,
                        $"{N(ti, 1)} °C > {N(c.TemperatureInterieureMax, 0)} °C : ventilation forcée nécessaire.", Ref, StatutCalcul.Alerte);
                    note.Info("Ventilateur à filtre",
                        $"V = 3,1 × (P − k × Ae × ΔT) / ΔT = {N(debit, 0)} m³/h ; avec {N((MajorationFiltres - 1) * 100, 0)} % de pertes des filtres : " +
                        $"ventilateur de {N(v, 0)} m³/h en partie basse, grille de sortie en partie haute.", Ref);
                }
            }
            if (solution == SolutionThermique.Aucune)
            {
                solution = SolutionThermique.Climatisation;
                froid = p - evacuee;
                climatiseur = Calibres.Superieur(Climatiseurs, froid) ?? Climatiseurs[^1];
                note.Verifier("Température intérieure", false,
                    $"{N(ti, 1)} °C > {N(c.TemperatureInterieureMax, 0)} °C et ventilation insuffisante : climatisation nécessaire.", Ref, StatutCalcul.Alerte);
                note.Info("Climatiseur", $"Qk = P − k × Ae × (Ti − Ta) = {N(froid, 0)} W : climatiseur de {N(climatiseur, 0)} W.", Ref);
                if (froid > Climatiseurs[^1])
                    note.Verifier("Climatiseur", false, "Besoin supérieur au plus gros climatiseur : répartir sur plusieurs cellules.", Ref, StatutCalcul.Alerte);
            }
            note.Info("Environnement", "En atmosphère poussiéreuse ou chargée en huile, préférer un climatiseur ou un échangeur air/air à la ventilation.", Ref);
        }

        double chauffage = 0, resistance = 0;
        if (c.TemperatureAmbianteMin < c.TemperatureInterieureMin)
        {
            // Les pertes des appareils ne sont pas comptées : l'armoire peut être hors tension.
            chauffage = k * ae * (c.TemperatureInterieureMin - c.TemperatureAmbianteMin);
            resistance = Calibres.Superieur(Resistances, chauffage) ?? Resistances[^1];
            note.Info("Chauffage anticondensation",
                $"Ph = k × Ae × (Ti min − Ta min) = {N(chauffage, 0)} W : résistance de {N(resistance, 0)} W avec thermostat et hygrostat.", Ref);
        }

        return new ResultatThermique
        {
            Note = note, Postes = postes, PuissanceDissipee = p, SurfaceEffective = ae, Coefficient = k,
            TemperatureInterieure = ti, Solution = solution, DebitNecessaire = debit, Ventilateur = ventilateur,
            PuissanceFrigorifique = froid, Climatiseur = climatiseur, Chauffage = chauffage, Resistance = resistance,
        };
    }

    /// <summary>Pertes de chaque appareil de l'armoire à son courant de service.</summary>
    public static List<PosteDissipation> Postes(ResultatProjet r)
    {
        var courants = r.Departs.ToDictionary(d => d.Depart.Id, d => d.CourantEmploi);
        var postes = new List<PosteDissipation>();
        foreach (var e in r.Arbre.Parcourir())
        {
            if (Encombrements.Estimer(e) is not { DissipationW: > 0 } enc) continue;
            var service = e.DepartId is { } id && courants.TryGetValue(id, out var ib) ? ib : r.CourantEmploi;
            var charge = e.Famille switch
            {
                "Disjoncteur" or "Interrupteur-sectionneur" or "Disjoncteur moteur" or "Contacteur" or "Relais thermique"
                    or "Fusible" or "Sectionneur porte-fusibles" when e.Courant > 0 => Math.Pow(Math.Min(1, service / e.Courant), 2),
                "Alimentation" when e.Courant > 0 => Math.Min(1, r.Consommation24V / e.Courant),
                _ => 1,
            };
            postes.Add(new PosteDissipation(e.Repere, e.Designation, enc.DissipationW * charge));
        }
        return postes;
    }

    public static double Coefficient(MateriauEnveloppe m) => m switch
    {
        MateriauEnveloppe.Inox => 4.5,
        MateriauEnveloppe.Aluminium => 12,
        MateriauEnveloppe.Polyester => 3.5,
        _ => 5.5,
    };

    /// <summary>Surface d'échange effective selon l'installation (IEC TR 60890), dimensions en m.</summary>
    public static double SurfaceEffective(double l, double h, double p, InstallationEnveloppe installation) => installation switch
    {
        InstallationEnveloppe.Autonome => 1.8 * h * (l + p) + 1.4 * l * p,
        InstallationEnveloppe.AdosseeAuMur => 1.4 * l * (h + p) + 1.8 * p * h,
        InstallationEnveloppe.ExtremiteRangee => 1.4 * p * (h + l) + 1.8 * l * h,
        InstallationEnveloppe.ExtremiteRangeeAdossee => 1.4 * h * (l + p) + 1.4 * l * p,
        InstallationEnveloppe.MilieuRangee => 1.8 * l * h + 1.4 * l * p + p * h,
        _ => 1.4 * l * (h + p) + p * h,
    };

    public static string LibelleInstallation(InstallationEnveloppe i) => i switch
    {
        InstallationEnveloppe.Autonome => "autonome, toutes faces libres",
        InstallationEnveloppe.AdosseeAuMur => "adossée au mur",
        InstallationEnveloppe.ExtremiteRangee => "en extrémité de rangée",
        InstallationEnveloppe.ExtremiteRangeeAdossee => "en extrémité de rangée, adossée",
        InstallationEnveloppe.MilieuRangee => "en milieu de rangée",
        _ => "en milieu de rangée, adossée",
    };

    public static string LibelleMateriau(MateriauEnveloppe m) => m switch
    {
        MateriauEnveloppe.Inox => "inox",
        MateriauEnveloppe.Aluminium => "aluminium",
        MateriauEnveloppe.Polyester => "polyester",
        _ => "acier peint",
    };
}
