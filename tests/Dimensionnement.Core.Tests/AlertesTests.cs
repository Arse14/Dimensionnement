using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Tests;

public class AlertesTests
{
    /// <summary>Un gros moteur seul : le général, calibré sur Ib, est trop proche de sa protection pour être sélectif.</summary>
    static Projet ProjetNonSelectif()
    {
        var projet = new Projet { Reseau = { CoefficientSimultaneite = 1 } };
        projet.Ajouter(new DepartMoteur { Designation = "Moteur vertical", PuissanceKw = 7.5, CourantPlaque = 15.2, Demarrage = ModeDemarrage.Variateur });
        return projet;
    }

    [Fact]
    public void Selectivite_sous_le_general_propose_de_le_remonter_et_la_correction_fait_disparaitre_l_alerte()
    {
        var projet = ProjetNonSelectif();
        var r = CalculProjet.Calculer(projet);
        var alerte = Assert.Single(r.Alertes, a => a.Action == ActionAlerte.RemonterGeneral);
        Assert.Equal(projet.Departs[0].Id, alerte.DepartId);
        Assert.True(alerte.Valeur >= r.Departs[0].CalibreProtection * CalculSelectivite.RapportSurchargeMini);

        // Encart sur la carte Arrivée, étiquette sur le départ concerné.
        var groupes = SyntheseMateriel.Grouper(r);
        Assert.NotNull(groupes[0].Cartes[0].Encart);
        Assert.Contains(groupes.SelectMany(g => g.Cartes), c => c.Etiquette == "Sélectivité avec QG");

        ListeAlertes.Appliquer(projet, r, alerte, "BE");
        var apres = CalculProjet.Calculer(projet);

        Assert.Equal(alerte.Valeur, apres.CalibreGeneral);
        Assert.DoesNotContain(apres.Alertes, a => a.Action == ActionAlerte.RemonterGeneral);
        var qs = apres.NoteArrivee.Lignes.First(l => l.Code == CodesCalcul.CalibreSectionneur).ValeurRetenue;
        Assert.True(qs >= apres.CalibreGeneral);
        Assert.Null(SyntheseMateriel.Grouper(apres)[0].Cartes[0].Encart);
    }

    [Fact]
    public void Alerte_ignoree_garde_son_motif_et_sort_du_compte()
    {
        var projet = ProjetNonSelectif();
        var alerte = CalculProjet.Calculer(projet).Alertes.First(a => a.Action == ActionAlerte.RemonterGeneral);
        projet.AlertesIgnorees[alerte.Cle] = "Sélectivité assurée par les tables du fabricant";

        var r = CalculProjet.Calculer(projet);
        var ignoree = Assert.Single(r.Alertes, a => a.Cle == alerte.Cle);
        Assert.True(ignoree.Ignoree);
        Assert.Equal("Sélectivité assurée par les tables du fabricant", ignoree.Motif);
        Assert.Same(ignoree, r.Alertes[^1]);
        Assert.DoesNotContain(r.Alertes.Actives(), a => a.Cle == alerte.Cle);
        Assert.Null(SyntheseMateriel.Grouper(r)[0].Cartes[0].Encart);
    }

    [Fact]
    public void Cartouche_a_completer_disparait_une_fois_renseigne()
    {
        var projet = ProjetNonSelectif();
        var avant = Assert.Single(CalculProjet.Calculer(projet).Alertes, a => a.Cle == "PROJET|cartouche");
        Assert.Equal(TypeAlerte.ARenseigner, avant.Type);

        projet.Nom = "Positionneur";
        projet.Affaire = "6004";
        projet.Client = "ITP";
        projet.Auteur = "BE";
        projet.Indice = "A";
        Assert.DoesNotContain(CalculProjet.Calculer(projet).Alertes, a => a.Cle == "PROJET|cartouche");
    }

    [Fact]
    public void Chaque_verification_non_conforme_ou_a_verifier_est_dans_la_liste()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());
        var attendues = r.Notes.SelectMany(n => n.Lignes).Count(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme);
        Assert.True(r.Alertes.Count(a => a.Type != TypeAlerte.ARenseigner) >= attendues);
    }
}

public class ControlesNoteTests
{
    [Fact]
    public void Pouvoir_de_coupure_compare_a_l_Icc_une_fois_la_reference_choisie()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Reseau.IccKiloAmperes = 10;
        var r = CalculProjet.Calculer(projet);
        var ligne = r.NoteArrivee.Lignes.Single(l => l.Libelle == "Pouvoir de coupure");
        Assert.Equal(StatutCalcul.Conforme, ligne.Statut);
        Assert.Contains("Icu", ligne.Message);

        projet.Reseau.IccKiloAmperes = 200;
        var fort = CalculProjet.Calculer(projet).NoteArrivee.Lignes.Single(l => l.Libelle == "Pouvoir de coupure");
        Assert.Equal(StatutCalcul.Alerte, fort.Statut);
    }

    [Fact]
    public void PE_et_marge_sont_des_controles()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Reseau.CoefficientExtension = 1.5;
        var lignes = CalculProjet.Calculer(projet).NoteArrivee.Lignes;
        Assert.Equal(StatutCalcul.Conforme, lignes.Single(l => l.Libelle == "Section du conducteur de protection").Statut);
        Assert.Equal(StatutCalcul.Conforme, lignes.Single(l => l.Libelle == "Marge en intensité").Statut);
    }
}
