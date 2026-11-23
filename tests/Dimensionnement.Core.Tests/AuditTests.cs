using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Tests;

/// <summary>Corrections de l'audit des calculs (contacts indirects, TT, variateurs, général, saisies).</summary>
public class AuditTests
{
    static readonly Reseau Tns = new() { Slt = SchemaLiaisonTerre.TNS };

    static DepartMoteur Moteur4kW(double longueur) => new()
    {
        Numero = 1, PuissanceKw = 4, CosPhi = 0.82, Rendement = 0.87, Cable = new CaracteristiquesCable { Longueur = longueur },
    };

    [Fact]
    public void En_TN_la_section_monte_pour_que_le_defaut_fasse_declencher_le_disjoncteur_moteur()
    {
        var r = CalculDepart.Calculer(Moteur4kW(100), Tns);

        // Plage 6–10 A : Ia = 1,2 × 13 × 10 = 156 A. Icc mini = 0,8 × 230 × S / (0,0225 × 2 × 100) : 61 A en 1,5 mm²,
        // 102 A en 2,5 mm², 164 A en 4 mm². La chute de tension seule laissait 1,5 mm².
        Assert.Equal(4, r.Section);
        Assert.True(r.Cable!.IccMin >= 156);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Contacts indirects" && l.Statut == StatutCalcul.Conforme);
    }

    [Fact]
    public void En_TN_une_section_forcee_trop_faible_est_non_conforme_aux_contacts_indirects()
    {
        var m = Moteur4kW(100);
        m.Forcages[CodesCalcul.Section] = new Forcage { Valeur = 2.5, Motif = "câble existant" };

        var r = CalculDepart.Calculer(m, Tns);

        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Contacts indirects" && l.Statut == StatutCalcul.NonConforme
                                            && l.Message.Contains("Longueur maximale protégée"));
    }

    [Fact]
    public void En_TN_un_differentiel_supprime_la_longueur_maximale()
    {
        var m = Moteur4kW(100);
        m.Differentiel = SensibiliteDifferentiel.Ma300;

        var r = CalculDepart.Calculer(m, Tns);

        Assert.Equal(1.5, r.Section);
        Assert.DoesNotContain(r.Note.Lignes, l => l.Libelle == "Contacts indirects" && l.Statut != StatutCalcul.Info);
        // Différentiel séparé (le disjoncteur moteur n'en intègre pas) : il entre dans l'arbre, donc l'implantation et le devis.
        Assert.Contains(r.Materiel.Parcourir(), e => e.Famille == "Interrupteur différentiel");
    }

    [Fact]
    public void En_TT_un_depart_sans_differentiel_est_signale_sauf_differentiel_en_amont()
    {
        var tt = new Reseau { Slt = SchemaLiaisonTerre.TT };
        var sans = CalculDepart.Calculer(Moteur4kW(10), tt);
        Assert.Contains(sans.Note.Lignes, l => l.Libelle == "Contacts indirects (TT)" && l.Statut == StatutCalcul.Alerte);

        tt.DifferentielAmont = true;
        var amont = CalculDepart.Calculer(Moteur4kW(10), tt);
        Assert.Contains(amont.Note.Lignes, l => l.Libelle == "Contacts indirects (TT)" && l.Statut == StatutCalcul.Info);
    }

    [Fact]
    public void Variateur_monophase_alimente_un_moteur_triphase_230_V()
    {
        var m = new DepartMoteur { Numero = 2, PuissanceKw = 2.2, CosPhi = 0.8, Rendement = 0.85,
            Alimentation = TypeAlimentation.Mono230, Demarrage = ModeDemarrage.Variateur };

        var r = CalculDepart.Calculer(m, Tns);

        // Moteur : 2 200 / (√3 × 230 × 0,8 × 0,85) = 8,12 A ; entrée du variateur ≈ 2,59 kW / 0,97 / (230 × 0,5) ≈ 23,2 A.
        Assert.Equal(8.12, r.CourantEmploi, 2);
        Assert.Equal(25, r.CalibreProtection);
        Assert.StartsWith("4G", r.Materiel.Parcourir().Single(e => e.Famille == "Câble").Calibre);
    }

    [Fact]
    public void Cos_phi_nul_est_une_saisie_invalide_et_le_calcul_continue()
    {
        var m = Moteur4kW(10);
        m.CosPhi = 0;

        var r = CalculDepart.Calculer(m, Tns);

        Assert.True(double.IsFinite(r.CourantEmploi));
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Saisie de cos φ" && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Ambiance_au_dela_du_tableau_PVC_est_non_conforme()
    {
        var r = CalculDepart.Calculer(new DepartResistif { Numero = 1, PuissanceKw = 3 }, new Reseau { Slt = SchemaLiaisonTerre.TNS, TemperatureAmbiante = 65 });

        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Température ambiante" && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Chute_du_depart_seul_limitee_a_5_pourcent_pour_la_machine()
    {
        // Poste privé : 8 % au total, mais NF EN 60204-1 limite le départ de la machine à 5 %.
        var reseau = new Reseau { Slt = SchemaLiaisonTerre.TNS, Origine = OrigineAlimentation.PostePrive };
        var r = CalculDepart.Calculer(new DepartResistif { Numero = 1, PuissanceKw = 9, Cable = new CaracteristiquesCable { Longueur = 150 } }, reseau);

        Assert.True(r.ChuteTension <= 5);
    }

    [Fact]
    public void General_trop_juste_pour_le_demarrage_du_moteur_est_signale()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, CosPhi = 0.82, Rendement = 0.87 });

        var r = CalculProjet.Calculer(projet);

        // Général 10 A courbe C (seuil bas 50 A) pour un démarrage direct à 7 × 8,1 A ≈ 57 A.
        Assert.Contains(r.NoteArrivee.Lignes, l => l.Libelle == "Démarrage sous le général" && l.Statut == StatutCalcul.Alerte);
    }
}
