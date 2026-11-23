using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Persistance;
using Dimensionnement.Core.Rapport;

namespace Dimensionnement.Core.Tests;

public class CotesArmoireTests
{
    static (Projet Projet, ResultatArmoire Armoire) Machine()
    {
        var projet = Version3Tests.ProjetMachine();
        return (projet, CalculProjet.Calculer(projet).Armoire!);
    }

    [Fact]
    public void Les_cotes_donnent_l_enveloppe_la_plaque_et_les_rails_en_chaine()
    {
        var (_, a) = Machine();
        var cotes = CotesArmoire.Toutes(a);

        Assert.Equal(a.LargeurTotale, cotes.Single(c => c.Cle == "enveloppe.L").Valeur);
        Assert.Equal(a.Hauteur, cotes.Single(c => c.Cle == "enveloppe.H").Valeur);
        Assert.Equal(a.PlaqueHauteur, cotes.Single(c => c.Cle == "plaque.H").Valeur);
        // La chaîne des rails va du haut au bas de la plaque : la somme de ses cotes est la hauteur de la plaque.
        var rails = cotes.Where(c => c.Cle.StartsWith("rail.")).ToList();
        Assert.True(rails.Count >= 2);
        Assert.Equal(a.PlaqueHauteur, rails.Sum(c => c.Valeur), 6);
        Assert.All(rails, c => Assert.False(c.Horizontale));
        Assert.Equal(cotes.Count, cotes.Select(c => c.Cle).Distinct().Count());       // clés uniques : elles portent les réglages
    }

    [Fact]
    public void Une_cote_deplacee_ou_masquee_le_reste_apres_enregistrement()
    {
        var (projet, a) = Machine();
        var origine = CotesArmoire.Toutes(a).Single(c => c.Cle == "plaque.H");
        projet.Armoire.Cotes.Afficher = true;
        projet.Armoire.Cotes.Decalages["plaque.H"] = -35;
        projet.Armoire.Cotes.Masquees.Add("enveloppe.L");

        var relu = FichierProjet.Deserialiser(FichierProjet.Serialiser(projet));
        var cotes = CotesArmoire.Calculer(CalculProjet.Calculer(relu).Armoire!, relu.Armoire.Cotes);

        Assert.True(relu.Armoire.Cotes.Afficher);
        Assert.Equal(origine.Position - 35, cotes.Single(c => c.Cle == "plaque.H").Position);
        Assert.DoesNotContain(cotes, c => c.Cle == "enveloppe.L");
        Assert.Equal(origine.Valeur, cotes.Single(c => c.Cle == "plaque.H").Valeur);  // déplacer une cote ne change pas sa valeur
    }

    [Fact]
    public void Le_dossier_de_cablage_montre_l_implantation_cotee_et_les_zones()
    {
        var projet = Version3Tests.ProjetMachine();
        var r = CalculProjet.Calculer(projet);
        var html = RapportCablageHtml.Generer(r);

        Assert.Contains("implantation cotée", html);
        Assert.Contains(SchemaArmoire.CouleurCote, html);                              // cotes tracées sur le plan
        Assert.Contains("Zone propre", html);
        Assert.Contains("Zone sale", html);
        Assert.Contains("rails DIN", html);
        Assert.Contains("Goulottes", html);
        foreach (var e in r.Armoire!.Appareils) Assert.Contains(System.Net.WebUtility.HtmlEncode(e.Repere), html);
        // Sans cotes demandées, le plan du rapport principal reste inchangé.
        Assert.DoesNotContain(SchemaArmoire.CouleurCote, SchemaArmoire.Svg(r.Armoire));
    }
}
