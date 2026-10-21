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
            Remplir(departs, ligne++, $"{d.Depart.Code}", d.Depart.Rubrique, d.Depart.Designation, d.Depart.TypeLibelle,
                Math.Round(d.CourantEmploi, 2), d.CalibreProtection, d.Section, Math.Round(d.ChuteTension, 2), d.Note.StatutGlobal.ToString());

        var notes = Feuille(classeur, "Notes de calcul", "Identifiant", "Libellé", "Valeur calculée", "Valeur retenue", "Unité",
            "Formule", "Entrées", "Message", "Référence", "Statut", "Forcé par", "Motif du forçage");
        ligne = 2;
        foreach (var l in r.Notes.SelectMany(n => n.Lignes))
            Remplir(notes, ligne++, l.Identifiant, l.Libelle, l.ValeurCalculee.HasValue ? Math.Round(l.ValeurCalculee.Value, 3) : "",
                l.ValeurRetenue.HasValue ? Math.Round(l.ValeurRetenue.Value, 3) : "", l.Unite, EcritureMath.Texte(l.Formule), EcritureMath.Texte(l.Entrees), EcritureMath.Texte(l.Message),
                l.Reference, l.Statut.ToString(), l.Forcage?.Auteur ?? "", l.Forcage?.Motif ?? "");

        var bilan = Feuille(classeur, "Bilan 24 V", "Consommateur", "Courant (A)");
        ligne = 2;
        foreach (var c in r.Consommateurs24V)
            Remplir(bilan, ligne++, c.Designation, Math.Round(c.Courant, 3));
        Remplir(bilan, ligne, "Total", Math.Round(r.Consommation24V, 3));
        bilan.Row(ligne).Style.Font.Bold = true;

        FeuilleDevis(classeur, r);

        // La demande de devis a ses propres largeurs de colonnes.
        foreach (var feuille in classeur.Worksheets.Where(w => w.Name != "Demande de devis"))
            feuille.Columns().AdjustToContents(1, 300, 8, 70);
        classeur.SaveAs(chemin);
    }

    /// <summary>Classeur à envoyer au fournisseur : la seule liste de matériel, avec l'en-tête du projet.</summary>
    public static void EnregistrerDevis(ResultatProjet r, string chemin)
    {
        using var classeur = new XLWorkbook();
        FeuilleDevis(classeur, r);
        classeur.SaveAs(chemin);
    }

    static readonly XLColor Marine = XLColor.FromHtml("#1F3A5F");
    static readonly XLColor Bandeau = XLColor.FromHtml("#DCE6F0");
    static readonly XLColor Zebre = XLColor.FromHtml("#F5F8FB");
    static readonly XLColor Trait = XLColor.FromHtml("#C5D0DC");
    static readonly XLColor Saisie = XLColor.FromHtml("#FFF6D5");
    static readonly XLColor Gris = XLColor.FromHtml("#5B6573");

    /// <summary>
    /// Demande de prix mise en page pour le fournisseur : bandeau titre, cartouche du projet, une section par catégorie
    /// avec numéros 1.1, 1.2…, colonnes de prix à compléter en jaune, sous-totaux et total HT calculés par Excel,
    /// impression A4 paysage sur une largeur de page avec l'en-tête répété.
    /// </summary>
    static IXLWorksheet FeuilleDevis(XLWorkbook classeur, ResultatProjet r)
    {
        var p = r.Projet;
        var f = classeur.Worksheets.Add("Demande de devis");
        f.Style.Font.FontName = "Calibri";
        f.Style.Font.FontSize = 10;
        f.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        f.ShowGridLines = false;

        string[] colonnes = { "N°", "Désignation", "Caractéristiques", "Fabricant", "Référence", "Qté", "Unité", "Repères", "Remarque",
            "Prix unitaire HT", "Total HT", "Délai" };
        double[] largeurs = { 6, 38, 44, 22, 22, 7, 6, 18, 30, 13, 13, 10 };
        var n = colonnes.Length;
        for (var i = 0; i < n; i++) f.Column(i + 1).Width = largeurs[i];
        const int cQte = 6, cPrix = 10, cTotal = 11, cDelai = 12;

        // Bandeau titre.
        var titre = f.Range(1, 1, 1, n).Merge();
        titre.Value = "DEMANDE DE PRIX – MATÉRIEL ÉLECTRIQUE";
        titre.Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Marine);
        titre.Style.Alignment.SetIndent(1);
        f.Row(1).Height = 30;
        var emetteur = f.Range(2, 1, 2, n).Merge();
        emetteur.Value = $"{p.Fabricant} · bureau d'études électrique";
        emetteur.Style.Font.SetItalic().Font.SetFontColor(Gris).Alignment.SetIndent(1);

        // Cartouche du projet sur deux colonnes.
        var gauche = new (string, string)[] { ("Projet", p.Nom), ("Affaire", p.Affaire), ("Client", p.Client) };
        var droite = new (string, string)[] { ("Indice", p.Indice), ("Date", DateTime.Today.ToString("dd/MM/yyyy")),
            ("Marque souhaitée", p.References.Fabricant.ToString()) };
        for (var i = 0; i < 3; i++)
        {
            Cartouche(f, 4 + i, 2, 3, gauche[i]);
            Cartouche(f, 4 + i, 4, 5, droite[i]);
        }
        var consigne = f.Range(8, 1, 8, n).Merge();
        consigne.Value = "Références à confirmer par le fournisseur ; équivalences acceptées si signalées. Merci de compléter les colonnes en jaune.";
        consigne.Style.Font.SetItalic().Font.SetFontColor(Gris).Alignment.SetIndent(1);

        // En-tête du tableau, figé et répété à l'impression.
        const int debut = 10;
        for (var i = 0; i < n; i++) f.Cell(debut, i + 1).Value = colonnes[i];
        var entete = f.Range(debut, 1, debut, n);
        entete.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Marine)
            .Alignment.SetWrapText().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        entete.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorderColor(XLColor.White);
        f.Row(debut).Height = 28;
        f.SheetView.FreezeRows(debut);

        var ligne = debut;
        var sousTotaux = new List<int>();
        foreach (var categorie in ListeDevis.Generer(r).GroupBy(d => d.Categorie))
        {
            var section = f.Range(++ligne, 1, ligne, n).Merge();
            section.Value = categorie.Key.ToUpperInvariant();
            section.Style.Font.SetBold().Font.SetFontColor(Marine).Fill.SetBackgroundColor(Bandeau).Alignment.SetIndent(1);
            f.Row(ligne).Height = 20;
            var numeroSection = categorie.Key.Split('.')[0];
            var premiere = ligne + 1;
            var rang = 0;
            foreach (var d in categorie)
            {
                rang++;
                Remplir(f, ++ligne, $"{numeroSection}.{rang}", d.Designation, d.Caracteristiques, d.Fabricant, d.Reference,
                    d.Quantite, d.Unite, d.Reperes, d.Remarque);
                var rangee = f.Range(ligne, 1, ligne, n);
                rangee.Style.Alignment.SetWrapText();
                if (rang % 2 == 0) rangee.Style.Fill.SetBackgroundColor(Zebre);
                f.Range(ligne, cQte, ligne, cQte + 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                f.Cell(ligne, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Font.SetFontColor(Gris);
                f.Cell(ligne, cQte).Style.NumberFormat.Format = "0.##";
                var remarque = f.Cell(ligne, 9);
                remarque.Style.Font.SetFontColor(d.Remarque.Contains("à compléter") ? XLColor.FromHtml("#C65A00") : Gris);
                // Colonnes du fournisseur.
                f.Cell(ligne, cPrix).Style.Fill.SetBackgroundColor(Saisie).NumberFormat.SetFormat("#,##0.00 €");
                f.Cell(ligne, cDelai).Style.Fill.SetBackgroundColor(Saisie).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                f.Cell(ligne, cTotal).FormulaA1 = $"IF(J{ligne}=\"\",\"\",F{ligne}*J{ligne})";
                f.Cell(ligne, cTotal).Style.NumberFormat.SetFormat("#,##0.00 €");
            }
            var bloc = f.Range(premiere, 1, ligne, n);
            bloc.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(Trait)
                .Border.SetInsideBorder(XLBorderStyleValues.Hair).Border.SetInsideBorderColor(Trait);
            // Sous-total de la catégorie.
            ligne++;
            var libelle = f.Range(ligne, 1, ligne, cTotal - 1).Merge();
            libelle.Value = $"Sous-total {categorie.Key}";
            libelle.Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            f.Cell(ligne, cTotal).FormulaA1 = $"SUM(K{premiere}:K{ligne - 1})";
            f.Cell(ligne, cTotal).Style.Font.SetBold().NumberFormat.SetFormat("#,##0.00 €");
            f.Range(ligne, cTotal, ligne, cTotal).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
            sousTotaux.Add(ligne);
            ligne++;
        }

        ligne++;
        var total = f.Range(ligne, 1, ligne, cTotal - 1).Merge();
        total.Value = "TOTAL HT";
        total.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        f.Cell(ligne, cTotal).FormulaA1 = string.Join("+", sousTotaux.Select(l => $"K{l}"));
        f.Cell(ligne, cTotal).Style.NumberFormat.SetFormat("#,##0.00 €");
        var bandeTotal = f.Range(ligne, 1, ligne, n);
        bandeTotal.Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Marine);
        f.Row(ligne).Height = 22;

        f.Range(debut, 1, ligne - 2, n).SetAutoFilter();
        var page = f.PageSetup;
        page.PaperSize = XLPaperSize.A4Paper;
        page.PageOrientation = XLPageOrientation.Landscape;
        page.FitToPages(1, 0);
        page.Margins.SetLeft(0.4).SetRight(0.4).SetTop(0.5).SetBottom(0.6);
        page.SetRowsToRepeatAtTop(debut, debut);
        page.PrintAreas.Add(1, 1, ligne, n);
        page.Footer.Left.AddText($"{p.Fabricant} – {p.Nom} – indice {p.Indice}");
        page.Footer.Right.AddText("Page &P / &N");
        return f;
    }

    /// <summary>Libellé en gras sur fond clair, valeur à côté, dans le cartouche du projet.</summary>
    static void Cartouche(IXLWorksheet f, int ligne, int colLibelle, int colValeur, (string Libelle, string Valeur) champ)
    {
        var libelle = f.Cell(ligne, colLibelle);
        libelle.Value = champ.Libelle;
        libelle.Style.Font.SetBold().Font.SetFontColor(Marine).Fill.SetBackgroundColor(Bandeau).Alignment.SetIndent(1);
        var valeur = f.Cell(ligne, colValeur);
        valeur.Value = champ.Valeur;
        valeur.Style.Alignment.SetIndent(1);
        f.Range(ligne, colLibelle, ligne, colValeur).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(Trait);
        f.Row(ligne).Height = 18;
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
