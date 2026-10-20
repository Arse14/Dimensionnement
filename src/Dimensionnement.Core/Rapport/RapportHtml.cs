using System.Net;
using System.Text;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Rapport complet du projet en HTML autonome, prêt à imprimer ou à convertir en PDF.
/// </summary>
public static class RapportHtml
{
    public static string Generer(ResultatProjet r)
    {
        var p = r.Projet;
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8">
            <title>{{E(p.Nom)}} – rapport de dimensionnement</title>
            <style>
            body { font-family: "Segoe UI", Arial, sans-serif; color: #1d232a; margin: 24px; font-size: 13px; }
            h1 { font-size: 22px; margin: 0 0 4px; } h2 { font-size: 16px; border-bottom: 2px solid #f5a623; padding-bottom: 4px; margin-top: 28px; }
            h3 { font-size: 14px; margin: 18px 0 6px; }
            table { border-collapse: collapse; width: 100%; margin: 6px 0 12px; }
            th, td { border: 1px solid #c9d1d9; padding: 4px 6px; text-align: left; vertical-align: top; }
            th { background: #eef1f4; }
            .garde td:first-child { width: 180px; font-weight: 600; }
            .Conforme { color: #1a7f37; } .Forcee { color: #b26b00; } .Alerte { color: #b26b00; } .NonConforme { color: #c62828; font-weight: 600; }
            .arbre ul { list-style: none; padding-left: 18px; border-left: 1px dashed #9aa5b1; margin: 2px 0; }
            .arbre > ul { border-left: none; padding-left: 0; }
            .rep { font-weight: 600; display: inline-block; min-width: 56px; }
            .schema svg { max-width: 100%; height: auto; border: 1px solid #c9d1d9; }
            .schema.armoire svg { max-height: 900px; width: auto; }
            .avert { background: #fff6e5; border-left: 4px solid #f5a623; padding: 8px 10px; }
            @media print { h2 { page-break-after: avoid; } .depart { page-break-inside: avoid; } }
            </style></head><body>
            <h1>{{E(p.Nom)}}</h1>
            <div>Rapport de dimensionnement – indice {{E(p.Indice)}} – {{DateTime.Now:dd/MM/yyyy}}</div>

            <h2>1. Page de garde</h2>
            <table class="garde">
            <tr><td>Affaire</td><td>{{E(p.Affaire)}}</td></tr>
            <tr><td>Client</td><td>{{E(p.Client)}}</td></tr>
            <tr><td>Auteur</td><td>{{E(p.Auteur)}}</td></tr>
            <tr><td>Indice</td><td>{{E(p.Indice)}}</td></tr>
            <tr><td>Statut global</td><td class="{{r.StatutGlobal}}">{{Statut(r.StatutGlobal)}} ({{r.NombreAlertes}} alerte(s) ou non-conformité(s))</td></tr>
            </table>
            <p class="avert">Ce rapport est une aide au dimensionnement. Les résultats doivent être vérifiés et validés par le bureau d'étude ;
            les tables normatives embarquées sont à contrôler contre les éditions en vigueur des normes.</p>
            """);

        var reseau = p.Reseau;
        sb.Append($"""
            <h2>2. Données d'entrée</h2>
            <table class="garde">
            <tr><td>Alimentation</td><td>{CalculProjet.Libelle(reseau.Alimentation)}, {N(reseau.FrequenceHz)} Hz</td></tr>
            <tr><td>Schéma de liaison à la terre</td><td>{reseau.Slt}</td></tr>
            <tr><td>Origine</td><td>{(reseau.Origine == OrigineAlimentation.ReseauPublicBT ? "Réseau public BT" : "Poste privé HTA/BT")}</td></tr>
            <tr><td>Icc présumé à l'arrivée</td><td>{N(reseau.IccKiloAmperes)} kA</td></tr>
            <tr><td>Température ambiante</td><td>{N(reseau.TemperatureAmbiante)} °C</td></tr>
            <tr><td>Câble d'arrivée</td><td>{N(reseau.CableArrivee.Longueur)} m, pose {reseau.CableArrivee.Pose}, {reseau.CableArrivee.Isolant}</td></tr>
            </table>
            """);

        sb.Append("<h2>3. Liste des départs</h2><table><tr><th>N°</th><th>Rubrique</th><th>Désignation</th><th>Type</th>" +
                  "<th>Ib</th><th>Protection</th><th>Section</th><th>ΔU départ</th><th>Statut</th></tr>");
        foreach (var d in r.Departs)
            sb.Append($"<tr><td>D{d.Depart.Numero:00}</td><td>{E(d.Depart.Rubrique)}</td><td>{E(d.Depart.Designation)}</td>" +
                      $"<td>{E(d.Depart.TypeLibelle)}</td><td>{A(d.CourantEmploi)}</td><td>{A(d.CalibreProtection)}</td>" +
                      $"<td>{Mm2(d.Section)}</td><td>{Pct(d.ChuteTension)}</td>" +
                      $"<td class=\"{d.Note.StatutGlobal}\">{Statut(d.Note.StatutGlobal)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>4. Arbre du matériel</h2><div class=\"arbre\"><ul>");
        Arbre(sb, r.Arbre);
        sb.Append("</ul></div>");

        sb.Append("<h2>5. Notes de calcul des départs</h2>");
        foreach (var d in r.Departs)
        {
            sb.Append($"<div class=\"depart\"><h3>D{d.Depart.Numero:00} – {E(d.Depart.Designation)} ({E(d.Depart.TypeLibelle)})</h3>");
            Note(sb, d.Note);
            sb.Append("</div>");
        }

        sb.Append($"""
            <h2>6. Arrivée générale et bilan de puissance</h2>
            <table class="garde">
            <tr><td>Puissance active</td><td>{N(r.PuissanceActive)} kW</td></tr>
            <tr><td>Puissance réactive</td><td>{N(r.PuissanceReactive)} kvar</td></tr>
            <tr><td>Puissance apparente</td><td>{N(r.PuissanceApparente)} kVA</td></tr>
            <tr><td>cos φ global</td><td>{N(r.CosPhiGlobal)}</td></tr>
            <tr><td>Coefficient de simultanéité</td><td>{N(r.CoefficientSimultaneite)}</td></tr>
            <tr><td>Courant d'emploi</td><td>{A(r.CourantEmploi)}</td></tr>
            <tr><td>Disjoncteur général</td><td>{A(r.CalibreGeneral)}</td></tr>
            <tr><td>Section d'arrivée</td><td>{Mm2(r.SectionArrivee)} (ΔU {Pct(r.ChuteTensionArrivee)})</td></tr>
            </table>
            """);
        Note(sb, r.NoteArrivee);

        if (r.Securite is { Fonctions.Count: > 0 } sec)
        {
            sb.Append("<h2>Sécurité machine</h2><table><tr><th>Fonction</th><th>PLr</th><th>Catégorie conseillée</th>" +
                      "<th>Architecture</th><th>Départs coupés</th></tr>");
            foreach (var f in sec.Fonctions)
                sb.Append($"<tr><td>{E(f.Fonction.Nom)}</td><td>{f.Fonction.PLr}</td><td>{f.Categorie}</td>" +
                          $"<td>{E(f.Architecture)}</td><td>{E(f.Fonction.DepartsTexte)}</td></tr>");
            sb.Append("</table>");
            Note(sb, sec.Note);
        }

        if (r.Automate is { Note.Lignes.Count: > 0 } api)
        {
            sb.Append("<h2>Automate</h2>");
            Note(sb, api.Note);
        }

        if (r.Consommateurs24V.Count > 0)
        {
            sb.Append("<h3>Bilan 24 V DC</h3><table><tr><th>Consommateur</th><th>Courant</th></tr>");
            foreach (var c in r.Consommateurs24V)
                sb.Append($"<tr><td>{E(c.Designation)}</td><td>{A(c.Courant)}</td></tr>");
            sb.Append($"<tr><th>Total</th><th>{A(r.Consommation24V)}</th></tr></table>");
        }

        sb.Append("<h2>Organigramme du matériel</h2><div class=\"schema\">");
        sb.Append(SchemaOrganigramme.Svg(r));
        sb.Append("</div>");

        if (r.Selectivite is { } sel)
        {
            sb.Append("<h2>Sélectivité</h2><table><tr><th>Départ</th><th>Amont</th><th>Aval</th><th>Rapport des calibres</th>" +
                      "<th>Magnétique amont mini</th><th>Magnétique aval maxi</th><th>Sélectivité</th><th>Commentaire</th></tr>");
            foreach (var l in sel.Lignes)
                sb.Append($"<tr><td>{E(l.Depart)}</td><td>{E(l.Amont)}</td><td>{E(l.Aval)}</td><td>{N(l.RapportSurcharge)}</td>" +
                          $"<td>{A(l.SeuilAmont)}</td><td>{A(l.SeuilAval)}</td>" +
                          $"<td class=\"{(l.Niveau is NiveauSelectivite.Totale or NiveauSelectivite.Partielle ? "Conforme" : "Alerte")}\">{CalculSelectivite.Libelle(l.Niveau)}</td>" +
                          $"<td>{E(l.Commentaire)}</td></tr>");
            sb.Append("</table>");
            Note(sb, sel.Note);
        }

        if (r.Armoire is { } arm)
        {
            sb.Append($"<h2>Armoire et implantation</h2><p>{E(arm.Type)} {N(arm.Largeur)} × {N(arm.Hauteur)} × {N(arm.Profondeur)} mm" +
                      (arm.Cellules > 1 ? $", {arm.Cellules} cellules" : "") + $", hauteur de plaque occupée {Pct(arm.Occupation)}. " +
                      "Les zones de couleur rouge orangé figurent le rayonnement indicatif des sources de perturbations.</p>");
            sb.Append("<div class=\"schema armoire\">");
            sb.Append(SchemaArmoire.Svg(arm));
            sb.Append("</div>");
            Note(sb, arm.Note);
        }

        if (r.Thermique is { } th)
        {
            sb.Append("<h2>Bilan thermique</h2><table><tr><th>Repère</th><th>Appareil</th><th>Pertes</th></tr>");
            foreach (var poste in th.Postes.Where(x => x.PuissanceW >= 0.05))
                sb.Append($"<tr><td>{E(poste.Repere)}</td><td>{E(poste.Designation)}</td><td>{N(poste.PuissanceW, 1)} W</td></tr>");
            sb.Append($"<tr><th colspan=\"2\">Total retenu (câblage compris)</th><th>{N(th.PuissanceDissipee, 0)} W</th></tr></table>");
            Note(sb, th.Note);
        }

        if (r.References is { } refs)
        {
            sb.Append("<h2>Références du matériel</h2>");
            Note(sb, refs.Note);
        }

        sb.Append("<h2>7. Directives CEM et basse tension</h2><table><tr><th>Vérifié</th><th>Point de contrôle</th><th>Référence</th></tr>");
        foreach (var c in r.Controles)
            sb.Append($"<tr><td class=\"{(c.Fait ? "Conforme" : "Alerte")}\">{(c.Fait ? "☑ Oui" : "☐ Non")}</td><td>{E(c.Libelle)}</td><td>{E(c.Reference)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>8. Nomenclature</h2><table><tr><th>Repères</th><th>Famille</th><th>Désignation</th><th>Calibre</th><th>Qté</th>" +
                  "<th>Fabricant</th><th>Référence</th></tr>");
        foreach (var e in r.Nomenclature)
            sb.Append($"<tr><td>{E(e.Repere)}</td><td>{E(e.Famille)}</td><td>{E(e.Designation)}</td><td>{E(e.Calibre)}</td><td>{e.Quantite}</td>" +
                      $"<td>{E(e.Fabricant)}</td><td>{E(e.Reference)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>9. Historique des révisions</h2><table><tr><th>Indice</th><th>Date</th><th>Auteur</th><th>Description</th></tr>");
        foreach (var rev in p.Revisions)
            sb.Append($"<tr><td>{E(rev.Indice)}</td><td>{rev.Date:dd/MM/yyyy}</td><td>{E(rev.Auteur)}</td><td>{E(rev.Description)}</td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    static void Note(StringBuilder sb, NoteDeCalcul note)
    {
        sb.Append("<table><tr><th>Id</th><th>Libellé</th><th>Valeur</th><th>Détail</th><th>Référence</th><th>Statut</th></tr>");
        foreach (var l in note.Lignes)
        {
            var valeur = l.ValeurRetenue is { } v ? $"{N(v)} {l.Unite}" : "";
            if (l.ValeurCalculee is { } c && l.ValeurRetenue is { } rv && Math.Abs(c - rv) > 1e-9)
                valeur += $" <small>(calcul : {N(c)})</small>";
            var detail = string.Join("<br>", new[] { l.Formule, l.Entrees, l.Message }
                .Where(t => t.Length > 0).Select(E));
            if (l.Forcage is { } f)
                detail += $"<br><em>Forcé par {E(f.Auteur)} le {f.Date:dd/MM/yyyy} : {E(f.Motif)}</em>";
            sb.Append($"<tr><td>{E(l.Identifiant)}</td><td>{E(l.Libelle)}</td><td>{valeur}</td><td>{detail}</td>" +
                      $"<td>{E(l.Reference)}</td><td class=\"{l.Statut}\">{Statut(l.Statut)}</td></tr>");
        }
        sb.Append("</table>");
    }

    static void Arbre(StringBuilder sb, ElementMateriel e)
    {
        sb.Append($"<li><span class=\"rep\">{E(e.Repere)}</span> {E(e.Designation)} – {E(e.Calibre)}");
        if (e.Enfants.Count > 0)
        {
            sb.Append("<ul>");
            foreach (var enfant in e.Enfants) Arbre(sb, enfant);
            sb.Append("</ul>");
        }
        sb.Append("</li>");
    }

    static string Statut(StatutCalcul s) => s switch
    {
        StatutCalcul.Conforme => "Conforme",
        StatutCalcul.Forcee => "Forcée",
        StatutCalcul.Alerte => "Alerte",
        StatutCalcul.NonConforme => "Non conforme",
        _ => "–",
    };

    static string E(string texte) => WebUtility.HtmlEncode(texte);
}
