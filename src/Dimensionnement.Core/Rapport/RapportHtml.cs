using System.Net;
using System.Text;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Rapport complet du projet en HTML autonome, prêt à imprimer ou à convertir en PDF. La première lecture tient en quelques
/// pages (matériel par départ, synthèse des calculs, points à traiter) ; la démonstration complète est en annexes, avec renvois.
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
            body { font-family: "Segoe UI", Arial, sans-serif; color: #1b1f23; margin: 24px; font-size: 12.5px; }
            h1 { font-size: 22px; margin: 0 0 4px; } h2 { font-size: 16px; border-bottom: 2px solid #f0a020; padding-bottom: 4px; margin-top: 30px; }
            h3 { font-size: 14px; margin: 20px 0 6px; }
            h4 { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: #6a737b; margin: 16px 0 6px; }
            table { border-collapse: collapse; width: 100%; margin: 6px 0 12px; font-variant-numeric: tabular-nums; }
            th { text-align: left; font-size: 10.5px; letter-spacing: .05em; text-transform: uppercase; color: #6a737b; font-weight: 600;
                 padding: 6px 8px; border-bottom: 1px solid #1b1f23; }
            td { padding: 5px 8px; border-bottom: 1px solid #e2e6e9; vertical-align: top; }
            td.rep { font-weight: 600; white-space: nowrap; }
            td.nb, th.nb { text-align: right; white-space: nowrap; }
            .s { display: block; color: #6a737b; font-size: 11px; }
            .ref { font-family: Consolas, "Courier New", monospace; font-size: 11px; color: #57606a; }
            tr.l-Conforme > td:first-child { box-shadow: inset 3px 0 0 #1a7f37; }
            tr.l-Alerte > td:first-child, tr.l-Forcee > td:first-child { box-shadow: inset 3px 0 0 #b26a00; }
            tr.l-NonConforme > td:first-child { box-shadow: inset 3px 0 0 #c62828; }
            td a { white-space: nowrap; }
            .Conforme { color: #1a7f37; } .Forcee, .Alerte { color: #b26a00; } .NonConforme { color: #c62828; font-weight: 600; }
            .etat { font-weight: 600; white-space: nowrap; }
            .garde td:first-child { width: 200px; font-weight: 600; }
            .bandeau { display: flex; flex-wrap: wrap; gap: 6px 28px; background: #f3f5f6; padding: 10px 12px; margin: 6px 0 12px; }
            .bandeau b { margin-right: 6px; }
            .renvoi { color: #6a737b; font-style: italic; }
            .notes { color: #6a737b; font-size: 11px; margin: -6px 0 14px; }
            tr.grp td { background: #f3f5f6; font-weight: 600; padding-top: 8px; border-bottom: 1px solid #c9d1d9; }
            tr.grp .s { display: inline; font-weight: 400; margin-left: 10px; }
            .schema svg { max-width: 100%; height: auto; border: 1px solid #c9d1d9; }
            .schema.armoire svg { max-height: 900px; width: auto; }
            .avert { background: #fff6e5; border-left: 4px solid #f0a020; padding: 8px 10px; }
            .glossaire p { margin: 2px 0 8px; } .glossaire .formule { background: #f3f5f6; padding: 6px 10px; display: inline-block; }
            {{EcritureMath.StyleHtml}}
            a { color: #0f5d5e; }
            @media print { h2, h3 { page-break-after: avoid; } .bloc { page-break-inside: avoid; } .annexes { page-break-before: always; } }
            </style></head><body>
            <h1>{{E(p.Nom)}}</h1>
            <div>Rapport de dimensionnement – indice {{E(p.Indice)}} – {{DateTime.Now:dd/MM/yyyy}}</div>
            """);

        // Repères des annexes : un numéro par départ (C1, C2…) et par entrée du glossaire (G1, G2…).
        var annexe = r.Departs.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => $"C{x.i + 1}");

        var reseau = p.Reseau;
        sb.Append($"""
            <h2>1. Projet et hypothèses</h2>
            <table class="garde">
            <tr><td>Fabricant</td><td>{E(p.Fabricant)}</td></tr>
            <tr><td>Affaire / client</td><td>{E(p.Affaire)} – {E(p.Client)}</td></tr>
            <tr><td>Auteur, indice</td><td>{string.Join(", ", new[] { E(p.Auteur), $"indice {E(p.Indice)}" }.Where(t => t.Length > 0))}</td></tr>
            <tr><td>Alimentation</td><td>{CalculProjet.Libelle(reseau.Alimentation)}, {N(reseau.FrequenceHz)} Hz, {reseau.Slt},
                {(reseau.Origine == OrigineAlimentation.ReseauPublicBT ? "réseau public BT" : "poste privé HTA/BT")}, Icc {N(reseau.IccKiloAmperes)} kA</td></tr>
            <tr><td>Température ambiante</td><td>{N(reseau.TemperatureAmbiante)} °C</td></tr>
            <tr><td>Câble d'arrivée</td><td>{N(reseau.CableArrivee.Longueur)} m, pose {reseau.CableArrivee.Pose}, {reseau.CableArrivee.Isolant}</td></tr>
            <tr class="l-{r.StatutGlobal}"><td>État du projet</td><td class="{r.StatutGlobal} etat">{Statut(r.StatutGlobal)}
                ({r.NombreAlertes} point(s) à traiter, voir chapitre 5)</td></tr>
            </table>
            <p class="avert">Ce rapport est une aide au dimensionnement. Les résultats doivent être vérifiés et validés par le bureau d'étude ;
            les tables normatives embarquées sont à contrôler contre les éditions en vigueur des normes.</p>
            """);

        sb.Append($"""
            <h2>2. Bilan de puissance et arrivée</h2>
            <table class="garde">
            <tr><td>Puissances</td><td>{N(r.PuissanceActive)} kW ; {N(r.PuissanceReactive)} kvar ; {N(r.PuissanceApparente)} kVA ; cos φ {N(r.CosPhiGlobal)}</td></tr>
            <tr><td>Coefficient de simultanéité</td><td>{N(r.CoefficientSimultaneite)}</td></tr>
            <tr><td>Courant d'emploi</td><td>{A(r.CourantEmploi)}</td></tr>
            <tr><td>Disjoncteur général</td><td>{A(r.CalibreGeneral)}</td></tr>
            <tr><td>Section d'arrivée</td><td>{Mm2(r.SectionArrivee)} (ΔU {Pct(r.ChuteTensionArrivee)})</td></tr>
            </table>
            <p class="renvoi">Calcul détaillé de l'arrivée : annexe C0.</p>
            """);

        Materiel(sb, r);
        Synthese(sb, r, annexe);
        PointsATraiter(sb, r, annexe);

        if (r.Armoire is { } arm)
        {
            sb.Append($"<h2>6. Armoire et implantation</h2><p>{E(arm.Type)} {E(arm.Marque)} {E(arm.Gamme)} {N(arm.Largeur)} × {N(arm.Hauteur)} × {N(arm.Profondeur)} mm" +
                      (arm.Reference.Length > 0 ? $" ({E(arm.Reference)})" : "") + (arm.Cellules > 1 ? $", {arm.Cellules} cellules" : "") +
                      (arm.Thermique is { } ta ? $", {N(ta.PuissanceDissipee, 0)} W dissipés" : "") +
                      ". Les zones rouge orangé figurent le rayonnement indicatif des sources de perturbations.</p>");
            sb.Append("<div class=\"schema armoire\">").Append(SchemaArmoire.Svg(arm)).Append("</div>");
            Etapes(sb, arm.Note);
            foreach (var c in arm.Coffrets)
            {
                sb.Append($"<h3>Coffret déporté : {E(c.Nom)}</h3><p>{E(c.Type)} {E(c.Marque)} {E(c.Gamme)} {N(c.Largeur)} × {N(c.Hauteur)} × {N(c.Profondeur)} mm" +
                          (c.Reference.Length > 0 ? $" ({E(c.Reference)})" : "") + $", {c.Appareils.Count} appareil(s)" +
                          (c.Thermique is { } tc ? $", {N(tc.PuissanceDissipee, 0)} W dissipés." : ".") + "</p>");
                sb.Append("<div class=\"schema armoire\">").Append(SchemaArmoire.Svg(c)).Append("</div>");
                Etapes(sb, c.Note);
                if (c.Thermique is { } t) Etapes(sb, t.Note);
            }
        }
        if (r.Thermique is { } th)
        {
            sb.Append("<h3>Bilan thermique</h3><table><tr><th>Repère</th><th>Appareil</th><th class=\"nb\">Pertes</th></tr>");
            foreach (var poste in th.Postes.Where(x => x.PuissanceW >= 0.05))
                sb.Append($"<tr><td class=\"rep\">{E(poste.Repere)}</td><td>{E(poste.Designation)}</td><td class=\"nb\">{N(poste.PuissanceW, 1)} W</td></tr>");
            sb.Append($"<tr><th colspan=\"2\">Total retenu (câblage compris)</th><th class=\"nb\">{N(th.PuissanceDissipee, 0)} W</th></tr></table>");
            Etapes(sb, th.Note);
        }

        sb.Append("<h2>7. Nomenclature</h2><table><tr><th>Repères</th><th>Désignation</th><th>Calibre</th><th class=\"nb\">Qté</th>" +
                  "<th>Fabricant</th><th>Référence</th></tr>");
        foreach (var e in r.Nomenclature)
            sb.Append($"<tr><td class=\"rep\">{E(e.Repere)}</td><td>{E(e.Designation)}<span class=\"s\">{E(e.Famille)}</span></td><td>{E(e.Calibre)}</td>" +
                      $"<td class=\"nb\">{e.Quantite}</td><td>{E(e.Fabricant)}</td><td class=\"ref\">{E(e.Reference)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>8. Historique des révisions</h2><table><tr><th>Indice</th><th>Date</th><th>Auteur</th><th>Description</th></tr>");
        foreach (var rev in p.Revisions)
            sb.Append($"<tr><td>{E(rev.Indice)}</td><td>{rev.Date:dd/MM/yyyy}</td><td>{E(rev.Auteur)}</td><td>{E(rev.Description)}</td></tr>");
        sb.Append("</table>");

        // ---------- Annexes ----------
        sb.Append("<div class=\"annexes\">");
        AnnexeAutomatisme(sb, r);
        sb.Append("<h2>Annexe B. Organigramme du matériel</h2><div class=\"schema\">").Append(SchemaOrganigramme.Svg(r)).Append("</div>");

        sb.Append("<h2 id=\"annexe-c\">Annexe C. Calcul détaillé</h2>");
        sb.Append("<div class=\"bloc\"><h3 id=\"C0\">C0. Arrivée générale</h3>");
        Etapes(sb, r.NoteArrivee);
        sb.Append("</div>");
        foreach (var d in r.Departs)
        {
            sb.Append($"<div class=\"bloc\"><h3 id=\"{annexe[d]}\">{annexe[d]}. {d.Depart.Code} – {E(d.Depart.Designation)} ({E(d.Depart.TypeLibelle)})</h3>");
            Etapes(sb, d.Note);
            sb.Append("</div>");
        }
        if (r.Selectivite is { Lignes.Count: > 0 } sel)
        {
            sb.Append("<h3>Sélectivité</h3><table><tr><th>Départ</th><th>Amont</th><th>Aval</th><th class=\"nb\">Rapport</th>" +
                      "<th class=\"nb\">Magn. amont mini</th><th class=\"nb\">Magn. aval maxi</th><th>Sélectivité</th></tr>");
            foreach (var l in sel.Lignes)
            {
                var ok = l.Niveau is NiveauSelectivite.Totale or NiveauSelectivite.Partielle;
                sb.Append($"<tr class=\"l-{(ok ? "Conforme" : "Alerte")}\"><td class=\"rep\">{E(l.Depart)}</td><td>{E(l.Amont)}</td><td>{E(l.Aval)}</td>" +
                          $"<td class=\"nb\">{N(l.RapportSurcharge)}</td><td class=\"nb\">{A(l.SeuilAmont)}</td><td class=\"nb\">{A(l.SeuilAval)}</td>" +
                          $"<td>{CalculSelectivite.Libelle(l.Niveau)}<span class=\"s\">{E(l.Commentaire)}</span></td></tr>");
            }
            sb.Append("</table>");
            Etapes(sb, sel.Note);
        }
        if (r.References is { } refs)
        {
            sb.Append("<h3>Références du matériel</h3>");
            Etapes(sb, refs.Note);
        }

        Consignes(sb, r, annexe);
        AnnexeGlossaire(sb);
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>Chapitre 3 : l'arrivée en bandeau, puis un tableau par rubrique, une ligne par départ de la protection au récepteur.</summary>
    static void Materiel(StringBuilder sb, ResultatProjet r)
    {
        sb.Append("<h2>3. Matériel par départ</h2><div class=\"bandeau\">");
        sb.Append($"<span><b>{E(r.Arbre.Repere)}</b>{E(r.Arbre.Designation)} – {E(r.Arbre.Calibre)}</span>");
        foreach (var e in r.Arbre.Parcourir().Where(e => e.RepereOrigine is "W0" or "QS0" or "QG" || e.Repere.EndsWith("-PF")))
            sb.Append($"<span><b>{E(e.Repere)}</b>{E(e.Designation)}{(e.Calibre.Length > 0 ? $", {E(e.Calibre)}" : "")}</span>");
        sb.Append("</div>");

        foreach (var rubrique in r.Departs.GroupBy(d => d.Depart.Rubrique is { Length: > 0 } x ? x : "Puissance"))
        {
            sb.Append($"<h4>{E(rubrique.Key)} ({rubrique.Count()} départ(s))</h4><table style=\"table-layout:fixed\">" +
                      "<colgroup><col style=\"width:13%\"><col style=\"width:22%\"><col style=\"width:25%\"><col style=\"width:16%\"><col style=\"width:16%\"><col style=\"width:8%\"></colgroup>" +
                      "<tr><th>Départ</th><th>Protection</th><th>Commande</th>" +
                      "<th>Câble</th><th>Récepteur</th><th class=\"nb\">Ib</th></tr>");
            foreach (var d in rubrique)
            {
                var chaine = SyntheseMateriel.Chaine(d.Materiel).Select(m => m.Element).ToList();
                var iCable = chaine.FindIndex(e => e.Famille == "Câble");
                var protection = chaine.Take(1);
                var commande = chaine.Skip(1).Take((iCable < 0 ? chaine.Count - 1 : iCable) - 1);
                var cable = iCable < 0 ? Enumerable.Empty<ElementMateriel>() : chaine.Skip(iCable).Take(1);
                var recepteur = iCable < 0 ? chaine.Skip(Math.Max(1, chaine.Count - 1)).Take(chaine.Count > 1 ? 1 : 0) : chaine.Skip(iCable + 1);
                if (iCable < 0 && chaine.Count > 1) commande = chaine.Skip(1).Take(chaine.Count - 2);
                sb.Append($"<tr class=\"l-{d.Note.StatutGlobal}\"><td class=\"rep\">{d.Depart.Code}<span class=\"s\">{E(d.Depart.Designation)}</span></td>" +
                          $"<td>{Cellule(protection)}</td><td>{Cellule(commande)}</td><td>{Cellule(cable)}</td><td>{Cellule(recepteur)}</td>" +
                          $"<td class=\"nb\">{A(d.CourantEmploi)}</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<p class=\"renvoi\">Automatisme et sécurité : annexe A. Vue d'ensemble de l'arborescence : annexe B.</p>");
    }

    static string Cellule(IEnumerable<ElementMateriel> elements)
    {
        var liste = elements.ToList();
        if (liste.Count == 0) return "–";
        return string.Join("", liste.Select(e =>
            $"<div><b>{E(e.Repere)}</b> {E(e.Designation)}" +
            (e.Calibre.Length > 0 ? $"<span class=\"s\">{E(e.Calibre)}</span>" : "") +
            (e.Reference.Length > 0 ? $"<span class=\"ref\">{E(e.Reference)}</span>" : "") + "</div>"));
    }

    /// <summary>Chapitre 4 : une ligne par départ avec les grandeurs clés et l'état, renvoi vers l'annexe C.</summary>
    static void Synthese(StringBuilder sb, ResultatProjet r, IReadOnlyDictionary<ResultatDepart, string> annexe)
    {
        sb.Append("<h2>4. Synthèse des calculs</h2><table><tr><th>Départ</th><th class=\"nb\">Ib</th><th class=\"nb\">Protection</th>" +
                  "<th class=\"nb\">Section</th><th class=\"nb\">Iz corrigé</th><th class=\"nb\">ΔU cumulée</th><th class=\"nb\">PE</th>" +
                  "<th>État</th><th>Détail</th></tr>");
        foreach (var d in r.Departs)
        {
            var c = d.Cable;
            var points = d.Note.Lignes.Count(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme);
            var etat = d.Note.StatutGlobal switch
            {
                StatutCalcul.NonConforme => "Non conforme",
                StatutCalcul.Alerte => $"{points} point(s) à vérifier",
                StatutCalcul.Forcee => "Valeur forcée",
                _ => "Conforme",
            };
            sb.Append($"<tr class=\"l-{d.Note.StatutGlobal}\"><td class=\"rep\">{d.Depart.Code}<span class=\"s\">{E(d.Depart.Designation)}</span></td>" +
                      $"<td class=\"nb\">{A(d.CourantEmploi)}</td><td class=\"nb\">{A(d.CalibreProtection)}</td><td class=\"nb\">{Mm2(d.Section)}</td>" +
                      $"<td class=\"nb\">{(c is null ? "–" : A(c.CourantAdmissible))}</td>" +
                      $"<td class=\"nb\">{(c is null ? Pct(d.ChuteTension) : Pct(c.ChuteAmont + c.ChuteTension))}</td>" +
                      $"<td class=\"nb\">{(c is null ? "–" : Mm2(c.SectionPe))}</td>" +
                      $"<td class=\"{d.Note.StatutGlobal} etat\">{etat}</td><td><a href=\"#{annexe[d]}\">Annexe {annexe[d]}</a></td></tr>");
        }
        sb.Append("</table>");
    }

    /// <summary>Chapitre 5 : toutes les alertes et non-conformités du projet, avec où les retrouver.</summary>
    static void PointsATraiter(StringBuilder sb, ResultatProjet r, IReadOnlyDictionary<ResultatDepart, string> annexe)
    {
        IEnumerable<(string Ou, NoteDeCalcul Note, string Renvoi, string Lien)> sources = r.Departs
            .Select(d => (d.Depart.Code, d.Note, $"Annexe {annexe[d]}", $"#{annexe[d]}"))
            .Append(("Arrivée", r.NoteArrivee, "Annexe C0", "#C0"));
        if (r.Armoire is { } a)
        {
            sources = sources.Append(("Armoire", a.Note, "Chapitre 6", ""));
            sources = sources.Concat(a.Coffrets.Select(c => (c.Nom, c.Note, "Chapitre 6", "")));
        }
        if (r.Thermique is { } t) sources = sources.Append(("Thermique", t.Note, "Chapitre 6", ""));
        if (r.Automate is { } api) sources = sources.Append(("Automate", api.Note, "Annexe A", "#annexe-a"));
        if (r.Securite is { } sec) sources = sources.Append(("Sécurité", sec.Note, "Annexe A", "#annexe-a"));
        if (r.Selectivite is { } sel) sources = sources.Append(("Sélectivité", sel.Note, "Annexe C", "#annexe-c"));
        if (r.References is { } refs) sources = sources.Append(("Références", refs.Note, "Annexe C", "#annexe-c"));

        var points = sources.SelectMany(s => s.Note.Lignes.Where(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme)
                .Select(l => (s.Ou, Ligne: l, s.Renvoi, s.Lien)))
            .OrderByDescending(x => x.Ligne.Statut).ToList();
        sb.Append("<h2>5. Points à traiter</h2>");
        if (points.Count == 0)
        {
            sb.Append("<p class=\"Conforme etat\">Aucun point à traiter : tous les contrôles sont conformes.</p>");
            return;
        }
        sb.Append("<table><tr><th>Où</th><th>Point</th><th>Détail</th><th>État</th><th>Renvoi</th></tr>");
        foreach (var (ou, l, renvoi, lien) in points)
            sb.Append($"<tr class=\"l-{l.Statut}\"><td class=\"rep\">{E(ou)}</td><td>{E(l.Libelle)}</td><td>{M(l.Message)}</td>" +
                      $"<td class=\"{l.Statut} etat\">{Statut(l.Statut)}</td>" +
                      $"<td>{(lien.Length > 0 ? $"<a href=\"{lien}\">{E(renvoi)}</a>" : E(renvoi))}</td></tr>");
        sb.Append("</table>");
    }

    /// <summary>Annexe A : automatisme et sécurité, une ligne par appareil avec ses accessoires et sa référence.</summary>
    static void AnnexeAutomatisme(StringBuilder sb, ResultatProjet r)
    {
        sb.Append("<h2 id=\"annexe-a\">Annexe A. Automatisme et sécurité</h2>");
        var domaines = SyntheseMateriel.Grouper(r).Where(g => g.Domaine).ToList();
        if (domaines.Count > 0)
        {
            sb.Append("<table><tr><th>Repère</th><th>Appareil</th><th>Calibre / voies</th><th>Référence</th></tr>");
            foreach (var domaine in domaines)
            {
                var source = domaine.Source!;
                sb.Append($"<tr><td colspan=\"4\" class=\"s\">{E(domaine.Titre)} : alimenté par {E(source.Repere)} {E(source.Designation)}</td></tr>");
                foreach (var e in domaine.Cartes.SelectMany(c => c.Chaine.Select(m => m.Element)).Where(e => e != source))
                {
                    var accessoires = e.Enfants.Where(x => x.Famille == "Accessoire automate").ToList();
                    sb.Append($"<tr class=\"l-{e.Statut}\"><td class=\"rep\">{E(e.Repere)}</td><td>{E(e.Designation)}" +
                              string.Join("", accessoires.Select(x => $"<span class=\"s\">+ {E(x.Designation)} <span class=\"ref\">{E(x.Reference)}</span></span>")) +
                              $"</td><td>{E(e.Calibre)}</td><td class=\"ref\">{E(e.Reference)}</td></tr>");
                }
            }
            sb.Append("</table>");
        }
        if (r.Securite is { Fonctions.Count: > 0 } sec)
        {
            sb.Append("<h3>Fonctions de sécurité</h3><table><tr><th>Fonction</th><th>PLr</th><th>Catégorie conseillée</th>" +
                      "<th>Architecture</th><th>Départs coupés</th></tr>");
            foreach (var f in sec.Fonctions)
                sb.Append($"<tr><td>{E(f.Fonction.Nom)}</td><td>{f.Fonction.PLr}</td><td>{f.Categorie}</td>" +
                          $"<td>{E(f.Architecture)}</td><td>{E(f.Fonction.DepartsTexte)}</td></tr>");
            sb.Append("</table>");
            Etapes(sb, sec.Note);
        }
        if (r.Automate is { Note.Lignes.Count: > 0 } api)
        {
            sb.Append("<h3>Automate</h3>");
            Etapes(sb, api.Note);
        }
        if (r.Consommateurs24V.Count > 0)
        {
            sb.Append("<h3>Bilan 24 V DC</h3><table><tr><th>Consommateur</th><th class=\"nb\">Courant</th></tr>");
            foreach (var c in r.Consommateurs24V)
                sb.Append($"<tr><td>{E(c.Designation)}</td><td class=\"nb\">{A(c.Courant)}</td></tr>");
            sb.Append($"<tr><th>Total</th><th class=\"nb\">{A(r.Consommation24V)}</th></tr></table>");
        }
    }

    /// <summary>
    /// Une note de calcul en étapes : le résultat en gras, le calcul en dessous avec ses renvois au glossaire, l'état en symbole.
    /// Les normes passent en notes de bas de tableau, numérotées une seule fois chacune.
    /// </summary>
    static void Etapes(StringBuilder sb, NoteDeCalcul note)
    {
        if (note.Lignes.Count == 0) return;
        var normes = new List<string>();
        sb.Append("<table><tr><th style=\"width:24%\">Étape</th><th class=\"nb\" style=\"width:13%\">Résultat</th><th>Calcul</th><th style=\"width:6%\">État</th></tr>");
        // Les étapes sont rangées par carte, comme dans l'application : un titre par objet contrôlé quand il y en a plusieurs.
        var groupes = note.Groupes;
        foreach (var g in groupes)
        {
        if (groupes.Count > 1)
            sb.Append($"<tr class=\"grp\"><td colspan=\"4\">{E(g.Nom)}<span class=\"s\">{E(g.Resume)}</span></td></tr>");
        foreach (var l in g.Lignes)
        {
            var valeur = l.ValeurRetenue is { } v ? $"<b>{N(v)} {E(l.Unite)}</b>" : "";
            if (l.ValeurCalculee is { } c && l.ValeurRetenue is { } rv && Math.Abs(c - rv) > 1e-9)
                valeur += $"<span class=\"s\">calcul : {N(c)}</span>";
            var calcul = string.Join("<br>", new[] { l.Formule, l.Entrees, l.Message }.Where(t => t.Length > 0).Select(t => M(t)));
            if (l.Forcage is { } f)
                calcul += $"<br><em>Forcé par {E(f.Auteur)} le {f.Date:dd/MM/yyyy} : {E(f.Motif)}</em>";
            var renvois = new List<string>();
            var glossaire = Glossaire.Pour(l).Select(g => $"<a href=\"#{IdGlossaire(g)}\">{IdGlossaire(g)}</a>").ToList();
            if (glossaire.Count > 0) renvois.Add("Glossaire " + string.Join(", ", glossaire));
            if (l.Reference.Length > 0)
            {
                var i = normes.IndexOf(l.Reference);
                if (i < 0) { normes.Add(l.Reference); i = normes.Count - 1; }
                renvois.Add($"norme ({i + 1})");
            }
            if (renvois.Count > 0) calcul += $"<span class=\"s\">{string.Join(" · ", renvois)}</span>";
            sb.Append($"<tr class=\"l-{l.Statut}\"><td class=\"rep\" style=\"white-space:normal\">{E(l.Libelle)}</td><td class=\"nb\">{valeur}</td>" +
                      $"<td>{calcul}</td><td class=\"{l.Statut} etat\">{Symbole(l.Statut)}</td></tr>");
        }
        }
        sb.Append("</table>");
        if (normes.Count > 0)
            sb.Append("<p class=\"notes\">" + string.Join(" · ", normes.Select((n, i) => $"({i + 1}) {E(n)}")) + "</p>");
    }

    /// <summary>Annexe D : les vérifications à faire au montage, écrites une fois pour tous les départs concernés.</summary>
    static void Consignes(StringBuilder sb, ResultatProjet r, IReadOnlyDictionary<ResultatDepart, string> annexe)
    {
        static bool EstConsigne(LigneCalcul l) => l.ValeurRetenue is null && l.Statut == StatutCalcul.Info
            && (l.Message.Contains("érifier") || l.Message.Contains("Valider") || l.Message.Contains("révoir") || l.Message.Contains("blindé")
                || l.Message.Contains("repris"));
        var consignes = r.Departs.SelectMany(d => d.Note.Lignes.Where(EstConsigne).Select(l => (d, l)))
            .GroupBy(x => x.l.Libelle).ToList();
        sb.Append("<h2>Annexe D. Consignes de montage et de vérification</h2>");
        if (consignes.Count == 0)
        {
            sb.Append("<p class=\"renvoi\">Aucune consigne particulière.</p>");
            return;
        }
        sb.Append("<table><tr><th style=\"width:24%\">Consigne</th><th>Détail</th><th style=\"width:18%\">Départs</th></tr>");
        foreach (var g in consignes)
        {
            var messages = g.Select(x => x.l.Message).Distinct().ToList();
            sb.Append($"<tr><td class=\"rep\" style=\"white-space:normal\">{E(g.Key)}</td><td>{string.Join("<br>", messages.Take(3).Select(t => M(t)))}" +
                      (messages.Count > 3 ? $"<span class=\"s\">… et {messages.Count - 3} variante(s), voir l'annexe C</span>" : "") +
                      $"</td><td>{string.Join(", ", g.Select(x => $"<a href=\"#{annexe[x.d]}\">{x.d.Depart.Code}</a>"))}</td></tr>");
        }
        sb.Append("</table>");
    }

    /// <summary>Annexe E : le glossaire des formules, cible des renvois « Glossaire Gn ».</summary>
    static void AnnexeGlossaire(StringBuilder sb)
    {
        sb.Append("<h2>Annexe E. Glossaire des formules</h2><div class=\"glossaire\">");
        foreach (var g in Glossaire.Entrees)
        {
            sb.Append($"<div class=\"bloc\"><h3 id=\"{IdGlossaire(g)}\">{IdGlossaire(g)}. {E(g.Terme)}</h3><p>{M(g.Definition)}</p>");
            if (g.Formule.Length > 0) sb.Append($"<p class=\"formule\">{M(g.Formule)}</p>");
            if (g.Termes.Count > 0)
                sb.Append("<p>" + string.Join("<br>", g.Termes.Select(t => $"<b>{M(t.Symbole, g.Formule.Length > 0)}</b> : {M(t.Signification)}")) + "</p>");
            if (g.Exemple.Length > 0) sb.Append($"<p class=\"s\">Exemple : {M(g.Exemple)}</p>");
            if (g.Reference.Length > 0) sb.Append($"<p class=\"s\">{E(g.Reference)}</p>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
    }

    static string IdGlossaire(EntreeGlossaire g) => $"G{Glossaire.Entrees.ToList().IndexOf(g) + 1}";

    static string Symbole(StatutCalcul s) => s switch
    {
        StatutCalcul.Conforme => "✓",
        StatutCalcul.Forcee => "forcé",
        StatutCalcul.Alerte => "!",
        StatutCalcul.NonConforme => "✗",
        _ => "",
    };

    static string Statut(StatutCalcul s) => s switch
    {
        StatutCalcul.Conforme => "Conforme",
        StatutCalcul.Forcee => "Forcée",
        StatutCalcul.Alerte => "Alerte",
        StatutCalcul.NonConforme => "Non conforme",
        _ => "–",
    };

    static string E(string texte) => WebUtility.HtmlEncode(texte);

    /// <summary>Texte dont les formules sont écrites comme sur papier (fractions, racines, indices).</summary>
    static string M(string texte, bool formule = false) => EcritureMath.Html(texte, formule);
}
