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
        Assert.Equal(new[] { "Q1", "Q2", "Q3", "Q4" }, qg.Enfants.Where(e => e.Famille != "Répartiteur").Select(e => e.Repere));
        Assert.Contains(qg.Enfants, e => e.Repere == "XR");
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
                     "5. Notes de calcul", "6. Arrivée générale", "7. Nomenclature", "8. Historique" })
            Assert.Contains(titre, html);
        Assert.Contains("NF EN 61800-3", html);
    }

    [Fact]
    public void Fabricant_ITP_INTERPIPE_par_defaut_et_sur_le_rapport()
    {
        var projet = ProjetExemple();
        Assert.Equal("ITP INTERPIPE", projet.Fabricant);
        Assert.Contains("ITP INTERPIPE", RapportHtml.Generer(CalculProjet.Calculer(projet)));
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

public class ExportsTests
{
    [Fact]
    public void Export_Excel_contient_nomenclature_departs_notes_bilan_et_devis()
    {
        var projet = new Projet { Nom = "Essai" };
        projet.Ajouter(new DepartMoteur { Designation = "Pompe", PuissanceKw = 4, Demarrage = ModeDemarrage.Variateur });
        projet.Ajouter(new DepartAlimentation24V());
        projet.Consommateurs24V.Add(new Consommateur24V { Designation = "Capteurs", CourantMa = 500 });
        var chemin = Path.Combine(Path.GetTempPath(), $"essai-{Guid.NewGuid()}.xlsx");
        try
        {
            ExportExcel.Enregistrer(CalculProjet.Calculer(projet), chemin);

            using var classeur = new ClosedXML.Excel.XLWorkbook(chemin);
            Assert.Equal(new[] { "Nomenclature", "Départs", "Notes de calcul", "Bilan 24 V", "Demande de devis" },
                classeur.Worksheets.Select(f => f.Name));
            Assert.Equal("D01", classeur.Worksheet("Départs").Cell(2, 1).GetString());
        }
        finally
        {
            File.Delete(chemin);
        }
    }
}

public class RendementTests
{
    // Motoréducteur NORD SK9012.1AZD-80LP/4 : 0,75 kW, 1,79 A sous 400 V, cos φ 0,72, 5,1 N·m à 1415 tr/min, rendement non donné.
    static DepartMoteur Nord() => new() { PuissanceKw = 0.75, CourantPlaque = 1.79, CosPhi = 0.72, CoupleNominal = 5.1, VitesseNominale = 1415 };

    [Fact]
    public void Rendement_retrouve_par_la_plaque()
    {
        var r = CalculRendement.Calculer(Nord(), MethodeRendement.Plaque)!;
        Assert.Equal(893, r.PuissanceAbsorbeeW, 0);
        Assert.Equal(0.840, r.Rendement, 3);
        Assert.Empty(r.Alerte);
    }

    [Fact]
    public void Rendement_retrouve_par_le_couple_et_la_vitesse()
    {
        var r = CalculRendement.Calculer(Nord(), MethodeRendement.CoupleVitesse)!;
        Assert.Equal(756, r.PuissanceUtileW, 0);
        Assert.Equal(0.846, r.Rendement, 3);
    }

    [Fact]
    public void Rendement_applique_puis_trace_dans_la_note()
    {
        var m = Nord();
        Assert.True(m.AppliquerRendementCalcule());
        Assert.Equal(0.84, m.Rendement, 3);
        Assert.Equal("calculé (plaque)", m.OrigineRendement);
        var projet = new Projet();
        projet.Ajouter(m);
        var d = CalculProjet.Calculer(projet).Departs[0];
        Assert.Contains(d.Note.Lignes, l => l.Libelle == "Rendement" && l.Message.Contains("893"));
        m.Rendement = 0.8;
        Assert.Equal("", m.OrigineRendement);
    }

    [Fact]
    public void Plaque_incoherente_signalee()
    {
        var m = Nord();
        m.CourantPlaque = 1.0;
        Assert.NotEmpty(CalculRendement.Calculer(m, MethodeRendement.Plaque)!.Alerte);
        Assert.Null(CalculRendement.Calculer(new DepartMoteur { PuissanceKw = 1 }, MethodeRendement.Plaque));
    }
}

public class FreinTests
{
    static (ResultatDepart D, ResultatProjet R) Calculer(DepartMoteur m)
    {
        var projet = new Projet();
        projet.Ajouter(m);
        var r = CalculProjet.Calculer(projet);
        return (r.Departs[0], r);
    }

    static DepartMoteur MoteurFrein() => new()
    {
        PuissanceKw = 0.75, CourantPlaque = 1.79, CosPhi = 0.72, Frein = true, AlimentationFrein = AlimentationFrein.Separee230,
        PuissanceFreinW = 30, TensionFreinV = 400, TensionBobineFreinV = 180,
    };

    [Fact]
    public void Frein_180_V_sous_400_V_demi_onde_dans_le_moteur()
    {
        var (d, r) = Calculer(MoteurFrein());
        Assert.Contains(d.Note.Lignes, l => l.Libelle == "Tension de bobine du frein" && l.Statut == StatutCalcul.Conforme && l.Message.Contains("demi-onde"));
        var q = r.Arbre.Parcourir().Single(e => e.Repere == "Q1.F");
        Assert.Contains("2P", q.Calibre);
        Assert.Equal(1, q.Courant); // 2 × 30 / 400 = 0,15 A : 1 A minimum
        Assert.DoesNotContain(r.Arbre.Parcourir(), e => e.Famille == "Redresseur");
    }

    [Fact]
    public void Sans_redresseur_dans_le_moteur_l_armoire_en_recoit_un()
    {
        var m = MoteurFrein();
        m.RedresseurDansMoteur = false;
        var (_, r) = Calculer(m);
        Assert.Contains(r.Arbre.Parcourir(), e => e.Repere == "V1.F" && e.Designation.Contains("demi-onde"));

        m.TensionBobineFreinV = 24;
        Assert.Contains(Calculer(m).R.Arbre.Parcourir(), e => e.Repere == "G1.F" && e.Famille == "Alimentation");

        m.TensionBobineFreinV = 96;
        var (d, r3) = Calculer(m);
        Assert.Contains(r3.Arbre.Parcourir(), e => e.Repere == "T1.F" && e.Famille == "Transformateur");
        Assert.Contains(r3.Arbre.Parcourir(), e => e.Repere == "V1.F" && e.Designation.Contains("pont"));
        Assert.Contains(d.Note.Lignes, l => l.Libelle == "Alimentation du frein" && l.Statut == StatutCalcul.Alerte);
    }

    [Fact]
    public void Frein_pilote_par_le_variateur()
    {
        var m = MoteurFrein();
        m.Demarrage = ModeDemarrage.Variateur;
        m.CommandeFrein = CommandeFrein.Variateur;
        var (d, _) = Calculer(m);
        Assert.Contains(d.Note.Lignes, l => l.Libelle == "Frein" && l.Message.Contains("logique de frein du variateur"));

        m.Demarrage = ModeDemarrage.Direct;
        Assert.Contains(Calculer(m).D.Note.Lignes, l => l.Libelle == "Commande du frein" && l.Statut == StatutCalcul.Alerte);
    }
}
