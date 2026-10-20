using ClosedXML.Excel;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;

namespace Dimensionnement.Core.Rapport;

/// <summary>Classeur Excel du projet : nomenclature, départs, notes de calcul, bilan 24 V et contrôles.</summary>
public static class ExportExcel
{
    public static void Enregistrer(ResultatProjet r, string chemin)
    {
        using var classeur = new XLWorkbook();

        var nomenclature = Feuille(classeur, "Nomenclature", "Repères", "Famille", "Désignation", "Calibre", "Quantité", "Fabricant", "Référence");
        var ligne = 2;
        foreach (var e in r.Nomenclature)
        {
            Remplir(nomenclature, ligne++, e.Repere, e.Famille, e.Designation, e.Calibre, e.Quantite, e.Fabricant, e.Reference);
        }

        var departs = Feuille(classeur, "Départs", "N°", "Rubrique", "Désignation", "Type", "Ib (A)", "Protection (A)",
            "Section (mm²)", "ΔU départ (%)", "Statut");
        ligne = 2;
        foreach (var d in r.Departs)
            Remplir(departs, ligne++, $"D{d.Depart.Numero:00}", d.Depart.Rubrique, d.Depart.Designation, d.Depart.TypeLibelle,
                Math.Round(d.CourantEmploi, 2), d.CalibreProtection, d.Section, Math.Round(d.ChuteTension, 2), d.Note.StatutGlobal.ToString());

        var notes = Feuille(classeur, "Notes de calcul", "Identifiant", "Libellé", "Valeur calculée", "Valeur retenue", "Unité",
            "Formule", "Entrées", "Message", "Référence", "Statut", "Forcé par", "Motif du forçage");
        ligne = 2;
        foreach (var l in r.Notes.SelectMany(n => n.Lignes))
            Remplir(notes, ligne++, l.Identifiant, l.Libelle, l.ValeurCalculee.HasValue ? Math.Round(l.ValeurCalculee.Value, 3) : "",
                l.ValeurRetenue.HasValue ? Math.Round(l.ValeurRetenue.Value, 3) : "", l.Unite, l.Formule, l.Entrees, l.Message,
                l.Reference, l.Statut.ToString(), l.Forcage?.Auteur ?? "", l.Forcage?.Motif ?? "");

        var bilan = Feuille(classeur, "Bilan 24 V", "Consommateur", "Courant (A)");
        ligne = 2;
        foreach (var c in r.Consommateurs24V)
            Remplir(bilan, ligne++, c.Designation, Math.Round(c.Courant, 3));
        Remplir(bilan, ligne, "Total", Math.Round(r.Consommation24V, 3));
        bilan.Row(ligne).Style.Font.Bold = true;

        FeuilleDevis(classeur, r);

        foreach (var feuille in classeur.Worksheets)
            feuille.Columns().AdjustToContents(1, 300, 8, 70);
        classeur.SaveAs(chemin);
    }

    /// <summary>Classeur à envoyer au fournisseur : la seule liste de matériel, avec l'en-tête du projet.</summary>
    public static void EnregistrerDevis(ResultatProjet r, string chemin)
    {
        using var classeur = new XLWorkbook();
        var feuille = FeuilleDevis(classeur, r);
        feuille.Columns().AdjustToContents(1, 300, 8, 70);
        classeur.SaveAs(chemin);
    }

    static IXLWorksheet FeuilleDevis(XLWorkbook classeur, ResultatProjet r)
    {
        var p = r.Projet;
        var feuille = classeur.Worksheets.Add("Demande de devis");
        feuille.Cell(1, 1).Value = "Demande de prix – matériel électrique";
        feuille.Cell(1, 1).Style.Font.Bold = true;
        feuille.Cell(1, 1).Style.Font.FontSize = 14;
        var entete = new (string, string)[]
        {
            ("Projet", p.Nom), ("Affaire", p.Affaire), ("Client", p.Client), ("Indice", p.Indice),
            ("Date", DateTime.Today.ToString("dd/MM/yyyy")), ("Fabricant souhaité", p.References.Fabricant.ToString()),
        };
        var ligne = 2;
        foreach (var (libelle, valeur) in entete)
        {
            feuille.Cell(ligne, 1).Value = libelle;
            feuille.Cell(ligne, 1).Style.Font.Bold = true;
            feuille.Cell(ligne++, 2).Value = valeur;
        }
        feuille.Cell(ligne++, 1).Value = "Références à confirmer par le fournisseur ; équivalences acceptées si signalées.";

        string[] colonnes = { "N°", "Catégorie", "Désignation", "Caractéristiques", "Fabricant", "Référence", "Quantité", "Unité",
            "Repères", "Remarque", "Prix unitaire HT", "Délai" };
        var debut = ++ligne;
        for (var i = 0; i < colonnes.Length; i++)
            feuille.Cell(debut, i + 1).Value = colonnes[i];
        var titres = feuille.Range(debut, 1, debut, colonnes.Length);
        titres.Style.Font.Bold = true;
        titres.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5A623");
        feuille.SheetView.FreezeRows(debut);

        var numero = 1;
        foreach (var d in ListeDevis.Generer(r))
            Remplir(feuille, ++ligne, numero++, d.Categorie, d.Designation, d.Caracteristiques, d.Fabricant, d.Reference,
                d.Quantite, d.Unite, d.Reperes, d.Remarque);
        feuille.Range(debut, 1, ligne, colonnes.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        feuille.Range(debut, 1, ligne, colonnes.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        return feuille;
    }

    static IXLWorksheet Feuille(XLWorkbook classeur, string nom, params string[] entetes)
    {
        var feuille = classeur.Worksheets.Add(nom);
        for (var i = 0; i < entetes.Length; i++)
            feuille.Cell(1, i + 1).Value = entetes[i];
        var entete = feuille.Row(1);
        entete.Style.Font.Bold = true;
        entete.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5A623");
        feuille.SheetView.FreezeRows(1);
        return feuille;
    }

    static void Remplir(IXLWorksheet feuille, int ligne, params object[] valeurs)
    {
        for (var i = 0; i < valeurs.Length; i++)
            feuille.Cell(ligne, i + 1).Value = XLCellValue.FromObject(valeurs[i]);
    }
}
