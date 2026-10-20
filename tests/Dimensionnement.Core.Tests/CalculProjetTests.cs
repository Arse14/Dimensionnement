using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Persistance;
using Dimensionnement.Core.Rapport;

namespace Dimensionnement.Core.Tests;

public class CalculProjetTests
{
    static Projet ProjetExemple()
    {
        var projet = new Projet { Nom = "Convoyeur ligne 2", Affaire = "2026-042", Client = "Client test" };
        projet.Ajouter(new DepartMoteur { Designation = "Convoyeur", PuissanceKw = 7.5, CosPhi = 0.84, Rendement = 0.89 });
        projet.Ajouter(new DepartMoteur { Designation = "Pompe", PuissanceKw = 4, CosPhi = 0.82, Rendement = 0.87,
            Demarrage = ModeDemarrage.Variateur, Differentiel = SensibiliteDifferentiel.Ma300 });
        projet.Ajouter(new DepartResistif { Designation = "Réchauffeur", PuissanceKw = 9 });
        projet.Ajouter(new DepartPrise { Designation = "PC armoire" });
        return projet;
    }

    [Fact]
    public void Bilan_applique_ku_et_ks_puis_dimensionne_l_arrivee()
    {
        var r = CalculProjet.Calculer(ProjetExemple());

        Assert.Equal(0.8, r.CoefficientSimultaneite); // 4 départs
        // P = 7,5/0,89 + 4/0,87 + 9 + 0,5 × 3,68 = 23,87 kW avant ks
        Assert.Equal(0.8 * 23.87, r.PuissanceActive, 1);
        // Q = 5,44 + 3,21 kvar ; S = 0,8 × √(23,87² + 8,65²) = 20,3 kVA ; Ib = 29,3 A
        Assert.Equal(29.3, r.CourantEmploi, 1);
        Assert.Equal(32, r.CalibreGeneral);
        Assert.Equal(4, r.Departs.Count);
    }

    [Fact]
    public void Arbre_part_du_reseau_jusqu_aux_recepteurs()
    {
        var r = CalculProjet.Calculer(ProjetExemple());

        var qg = r.Arbre.Enfants.Single().Enfants.Single().Enfants.Single();
        Assert.Equal("QG", qg.Repere);
        Assert.Equal(new[] { "Q1", "Q2", "Q3", "Q4" }, qg.Enfants.Select(e => e.Repere));
        Assert.Contains(r.Arbre.Parcourir(), e => e.Repere == "M1");
        Assert.Contains(r.Nomenclature, e => e.Famille == "Contacteur" && e.Repere == "KM1");
    }

    [Fact]
    public void Simultaneite_forcee_est_retenue_et_tracee()
    {
        var projet = ProjetExemple();
        projet.ForcagesArrivee[CodesCalcul.Simultaneite] = new Forcage { Valeur = 1, Motif = "tout fonctionne ensemble" };

        var r = CalculProjet.Calculer(projet);

        Assert.Equal(1, r.CoefficientSimultaneite);
        Assert.Contains(r.NoteArrivee.Lignes, l => l.Identifiant == "ARR-KS" && l.Statut == StatutCalcul.Forcee);
    }

    [Fact]
    public void Projet_enregistre_puis_rouvert_donne_le_meme_resultat()
    {
        var projet = ProjetExemple();
        projet.Departs[0].Forcages[CodesCalcul.Section] = new Forcage { Valeur = 4, Motif = "réserve" };
        var chemin = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}{FichierProjet.Extension}");
        try
        {
            FichierProjet.Enregistrer(projet, chemin);
            var rouvert = FichierProjet.Ouvrir(chemin);

            Assert.IsType<DepartMoteur>(rouvert.Departs[0]);
            Assert.IsType<DepartPrise>(rouvert.Departs[3]);
            var avant = CalculProjet.Calculer(projet);
            var apres = CalculProjet.Calculer(rouvert);
            Assert.Equal(avant.CourantEmploi, apres.CourantEmploi);
            Assert.Equal(4, apres.Departs[0].Section);
        }
        finally
        {
            File.Delete(chemin);
        }
    }

    [Fact]
    public void Rapport_contient_toutes_les_parties()
    {
        var html = RapportHtml.Generer(CalculProjet.Calculer(ProjetExemple()));

        foreach (var titre in new[] { "1. Page de garde", "2. Données d'entrée", "3. Liste des départs", "4. Arbre du matériel",
                     "5. Notes de calcul", "6. Arrivée générale", "7. Directives CEM", "8. Nomenclature", "9. Historique" })
            Assert.Contains(titre, html);
        Assert.Contains("NF EN 61800-3", html);
    }

    [Fact]
    public void Projets_recents_gardent_le_plus_recent_en_tete_sans_doublon()
    {
        var dossier = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var recents = new ProjetsRecents(dossier);
        recents.Ajouter("a.dimelec");
        recents.Ajouter("b.dimelec");
        recents.Ajouter("a.dimelec");

        Assert.Equal(new[] { "a.dimelec", "b.dimelec" }, recents.Lire());
        Directory.Delete(dossier, true);
    }
}
