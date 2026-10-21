using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Tests;

/// <summary>Cohérence des choix automatiques avec les réglages du projet (revue du 21/10/2026).</summary>
public class ChoixAutomatiquesTests
{
    [Fact]
    public void Reglage_thermique_force_change_la_plage_et_le_contacteur()
    {
        var m = new DepartMoteur
        {
            Numero = 1, PuissanceKw = 7.5, CosPhi = 0.84, Rendement = 0.89, RapportId = 7.5,
            Cable = new CaracteristiquesCable { Longueur = 10, Pose = MethodePose.C },
        };
        m.Forcages[CodesCalcul.ReglageThermique] = Version3Tests.Force(20);
        var r = CalculDepart.Calculer(m, new Reseau());

        var q = r.Materiel;
        Assert.True(q.PlageMin <= 20 && q.PlageMax >= 20, q.Calibre);
        Assert.True(q.Enfants.Single().Courant >= 20);
    }

    [Fact]
    public void Moteur_monophase_ne_recoit_pas_de_variateur_triphase()
    {
        var m = new DepartMoteur { Numero = 1, PuissanceKw = 1.5, Alimentation = TypeAlimentation.Mono230, Demarrage = ModeDemarrage.Variateur };
        var r = CalculDepart.Calculer(m, new Reseau());
        var v = Assert.Single(r.Materiel.Parcourir(), e => e.Famille == "Variateur");
        Assert.DoesNotContain("N4", v.Reference);
    }

    [Fact]
    public void Cable_TNC_compte_le_PEN_a_partir_de_10_mm2()
    {
        var reseau = new Reseau { Slt = SchemaLiaisonTerre.TNC };
        var gros = CalculDepart.Calculer(new DepartResistif { Numero = 1, PuissanceKw = 40, Alimentation = TypeAlimentation.Tetra400 }, reseau);
        var petit = CalculDepart.Calculer(new DepartResistif { Numero = 2, PuissanceKw = 3, Alimentation = TypeAlimentation.Tetra400 }, reseau);
        Assert.True(gros.Section >= 10);
        Assert.StartsWith("4G", gros.Materiel.Parcourir().Single(e => e.Famille == "Câble").Calibre);
        Assert.StartsWith("5G", petit.Materiel.Parcourir().Single(e => e.Famille == "Câble").Calibre);
    }

    [Fact]
    public void Differentiel_en_TNC_signale_et_sans_reference_de_disjoncteur_simple()
    {
        var d = new DepartResistif { Numero = 1, PuissanceKw = 3, Differentiel = SensibiliteDifferentiel.Ma30 };
        var r = CalculDepart.Calculer(d, new Reseau { Slt = SchemaLiaisonTerre.TNC });
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Différentiel en TN-C" && l.Statut == StatutCalcul.Alerte);
        Assert.False(r.Materiel.ReferenceAuto);
        Assert.Contains("30 mA", r.Materiel.Calibre);
    }

    [Fact]
    public void Icc_eleve_alerte_sur_le_pouvoir_de_coupure()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Reseau.IccKiloAmperes = 50;
        var r = CalculProjet.Calculer(projet);
        Assert.Contains(r.References.Note.Lignes, l => l.Libelle == "Pouvoir de coupure" && l.Statut == StatutCalcul.Alerte);
    }

    [Fact]
    public void Renommage_s_applique_aux_ventilateurs()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Armoire.Forcages[Armoire.CalculThermique.CodePuissance] = Version3Tests.Force(300);
        projet.Renommages["projet|Q-EV"] = new Renommage { Repere = "Q99" };
        var r = CalculProjet.Calculer(projet);
        Assert.Contains(r.Arbre.Parcourir(), e => e.RepereOrigine == "Q-EV" && e.Repere == "Q99");
    }

    [Fact]
    public void Reference_de_contacteur_saisie_n_est_pas_reecrite()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.TensionBobines = TensionCommande.Alternatif230;
        var km = CalculProjet.Calculer(projet).Arbre.Parcourir().First(e => e.Famille == "Contacteur");
        projet.References.Forcees[km.Repere] = "REF-SAISIE-BD";
        var r = CalculProjet.Calculer(projet);
        Assert.Equal("REF-SAISIE-BD", r.Arbre.Parcourir().First(e => e.Repere == km.Repere).Reference);
    }

    [Fact]
    public void IHM_imposee_hors_catalogue_reste_dans_le_materiel()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Automate.Pupitres.Add(new PupitreIhm { Nom = "Pupitre", ReferenceImposee = "XYZ-INCONNUE" });
        var r = CalculProjet.Calculer(projet);
        var ihm = Assert.Single(r.Arbre.Parcourir(), e => e.Reference == "XYZ-INCONNUE");
        Assert.True(ihm.AVerifier);
    }
}
