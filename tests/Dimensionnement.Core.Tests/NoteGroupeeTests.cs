using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Tests;

/// <summary>La note de calcul en cartes : un objet contrôlé par carte, ses vérifications visibles, ses calculs pliés dessous.</summary>
public class NoteGroupeeTests
{
    [Fact]
    public void Les_cartes_suivent_l_ordre_d_apparition_et_les_informations_sans_groupe_finissent()
    {
        var note = new NoteDeCalcul("T");
        note.Info("Sans groupe", "libre");
        note.Groupe = "Câble";
        note.Valeur("S", "Section", 4, 4, "mm²");
        note.Verifier("Courant admissible", true, "ok");
        note.Groupe = "Protection";
        note.Valeur("IN", "Calibre", 16, 16, "A");

        var groupes = note.Groupes;
        Assert.Equal(new[] { "Câble", "Protection", NoteDeCalcul.GroupeInformations }, groupes.Select(g => g.Nom));
        var cable = groupes[0];
        Assert.Single(cable.Controles);
        Assert.Single(cable.Calculs);
        Assert.Equal("4 mm²", cable.ValeurCle);
        Assert.Equal(StatutCalcul.Conforme, cable.Statut);
        Assert.Equal("1 vérification conforme · 1 calcul", cable.Resume);
        Assert.True(cable.OuvertParDefaut);
        Assert.False(cable.CalculsOuvertsParDefaut);
        Assert.True(groupes[1].SansControle);
        Assert.False(groupes[1].OuvertParDefaut);
        Assert.Equal("T|Câble", cable.Cle);
    }

    [Fact]
    public void Une_verification_en_defaut_colore_la_carte_et_deplie_ses_calculs()
    {
        var note = new NoteDeCalcul("T") { Groupe = "Câble" };
        note.Info("Entrée", "x");
        note.Verifier("Chute de tension", false, "trop");
        note.Verifier("Courant admissible", true, "ok");
        var g = Assert.Single(note.Groupes);
        Assert.Equal(StatutCalcul.NonConforme, g.Statut);
        Assert.True(g.CalculsOuvertsParDefaut);
        Assert.Equal("1 non conforme, 1 conforme · 1 calcul", g.Resume);
        Assert.Equal("Note de calcul (1 ligne)", g.TitreCalculs);
    }

    [Fact]
    public void Une_valeur_forcee_est_un_point_de_controle()
    {
        var forcages = new Dictionary<string, Forcage> { ["S"] = Version3Tests.Force(6) };
        var note = new NoteDeCalcul("T", forcages) { Groupe = "Câble" };
        note.Valeur("S", "Section", 4, 4, "mm²");
        var g = Assert.Single(note.Groupes);
        Assert.Single(g.Controles);
        Assert.Equal("1 forcée", g.Resume);
    }

    [Fact]
    public void Toutes_les_lignes_du_projet_machine_ont_une_carte()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Reseau.CoefficientExtension = 1.5;
        var r = CalculProjet.Calculer(projet);
        foreach (var note in r.Notes)
            foreach (var l in note.Lignes)
                Assert.True(l.Groupe.Length > 0, $"{l.Identifiant} {l.Libelle} sans carte");

        var arrivee = r.NoteArrivee.Groupes.Select(g => g.Nom).ToList();
        Assert.Equal("Protection générale", arrivee[0]);
        Assert.Contains("Câble d'arrivée", arrivee);
        Assert.Contains(r.NoteArrivee.Lignes, l => l.Libelle == "Marge en intensité" && l.Groupe == "Câble d'arrivée");

        var moteur = r.Departs.First(d => d.Depart is DepartMoteur).Note.Groupes.Select(g => g.Nom).ToList();
        Assert.Equal("Moteur", moteur[0]);
        Assert.Contains("Protection", moteur);
        Assert.Contains("Câble", moteur);
    }
}
