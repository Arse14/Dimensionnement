using System.Net;
using System.Text;
using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Dossier de câblage de l'armoire, pour l'atelier : une page d'implantation cotée avec les zones CEM, puis les rails, les
/// goulottes, les appareils à poser et la filerie. Document court, à imprimer et à garder près de l'armoire pendant le montage.
/// </summary>
public static class RapportCablageHtml
{
    static string E(string texte) => WebUtility.HtmlEncode(texte);

    public static string Generer(ResultatProjet r)
    {
        var p = r.Projet;
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8">
            <title>{{E(p.Nom)}} – dossier de câblage</title>
            <style>
            @page { size: A4; }
            body { font-family: "Segoe UI", Arial, sans-serif; color: #1b1f23; margin: 24px; font-size: 12px; }
            h1 { font-size: 21px; margin: 0 0 4px; } h2 { font-size: 15px; border-bottom: 2px solid #f0a020; padding-bottom: 4px; margin-top: 24px; }
            h3 { font-size: 13px; margin: 16px 0 6px; }
            table { border-collapse: collapse; width: 100%; margin: 6px 0 12px; font-variant-numeric: tabular-nums; }
            th { text-align: left; font-size: 10px; letter-spacing: .05em; text-transform: uppercase; color: #6a737b; font-weight: 600;
                 padding: 5px 7px; border-bottom: 1px solid #1b1f23; }
            td { padding: 4px 7px; border-bottom: 1px solid #e2e6e9; vertical-align: top; }
            td.rep { font-weight: 600; white-space: nowrap; } td.nb, th.nb { text-align: right; white-space: nowrap; }
            .s { color: #6a737b; font-size: 10.5px; } .ref { font-family: Consolas, "Courier New", monospace; font-size: 10.5px; }
            .garde td:first-child { width: 190px; font-weight: 600; }
            .plan { text-align: center; break-inside: avoid; page-break-inside: avoid; } .plan svg { max-width: 100%; max-height: 238mm; width: auto; height: auto; }
            .zone { display: inline-block; padding: 1px 7px; border: 1.5px solid; font-weight: 600; font-size: 10.5px; }
            .z-Propre { background: #d3f0dc; border-color: #2f9e44; } .z-Sale { background: #f6dcd6; border-color: #c92a2a; }
            .z-Intermediaire { background: #dbe7f6; border-color: #1c7ed6; }
            .regles li { margin-bottom: 3px; }
            @media print { h2, h3 { page-break-after: avoid; } tr { page-break-inside: avoid; } .saut { page-break-before: always; } }
            </style></head><body>
            <h1>Dossier de câblage de l'armoire</h1>
            <div>{{E(p.Nom)}}{{(p.Affaire.Length > 0 ? " – " + E(p.Affaire) : "")}} – indice {{E(p.Indice)}} – édité le {{DateTime.Now:dd/MM/yyyy}}</div>
            """);
        if (r.Armoire is not { } principale)
        {
            sb.Append("<p>Le projet n'a pas encore d'armoire calculée.</p></body></html>");
            return sb.ToString();
        }
        sb.Append("<table class=\"garde\">");
        sb.Append($"<tr><td>Client</td><td>{E(p.Client)}</td></tr><tr><td>Fabricant</td><td>{E(p.Fabricant)}</td></tr><tr><td>Auteur</td><td>{E(p.Auteur)}</td></tr>");
        sb.Append("</table>");

        var premiere = true;
        foreach (var a in principale.Enveloppes)
        {
            var haut = a.PlaqueLargeur > 0 ? a.PlaqueY : 0;
            var gauche = a.PlaqueLargeur > 0 ? a.PlaqueX : 0;
            var cotes = CotesArmoire.Calculer(a, p.Armoire.Cotes);

            // Page d'implantation cotée, zones CEM visibles, sans la carte des champs qui chargerait le plan.
            sb.Append($"<h2{(premiere ? "" : " class=\"saut\"")}>{E(a.Nom)} — implantation cotée</h2>");
            sb.Append($"<p>{E(a.Type)} {E(a.Marque)} {E(a.Gamme)} {N(a.Largeur)} × {N(a.Hauteur)} × {N(a.Profondeur)} mm" +
                      (a.Reference.Length > 0 ? $" ({E(a.Reference)})" : "") + (a.Cellules > 1 ? $", {a.Cellules} cellules" : "") +
                      (a.PlaqueLargeur > 0 ? $". Plaque de montage {E(a.ReferencePlaque)} : {N(a.PlaqueLargeur)} × {N(a.PlaqueHauteur)} mm." : ".") +
                      " Cotes en mm, vue de face. Les rails et les goulottes sont cotés sur leur axe, depuis le haut de la plaque.</p>");
            sb.Append("<div class=\"plan\">").Append(SchemaArmoire.Svg(a, champs: false, cotes: cotes)).Append("</div>");
            premiere = false;

            // Rails DIN : position de l'axe, longueur, appareils de gauche à droite.
            var rails = a.Bandes.Where(b => b.Rail).OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
            sb.Append($"<h2 class=\"saut\">{E(a.Nom)} — rails DIN</h2>");
            if (rails.Count == 0) sb.Append("<p>Aucun rail DIN.</p>");
            else
            {
                sb.Append("<table><tr><th>Rail</th><th>Zone</th><th class=\"nb\">Axe depuis le haut de la plaque</th><th class=\"nb\">Début depuis la gauche</th><th class=\"nb\">Longueur</th><th>Appareils, de gauche à droite</th></tr>");
                var n = 1;
                foreach (var b in rails)
                {
                    var axe = b.Y + b.Hauteur / 2;
                    var retrait = b.Manuel ? 0 : a.Laterale;
                    var sur = a.Appareils.Where(e => e.Y <= axe && e.Y + e.Hauteur >= axe && e.CentreX > b.X && e.CentreX < b.X + b.Largeur)
                        .OrderBy(e => e.X).Select(e => E(e.Repere));
                    sb.Append($"<tr><td class=\"rep\">R{n++}</td><td>{Zone(b.Zone, b.Bornier)}</td><td class=\"nb\">{N(axe - haut, 0)} mm</td>" +
                              $"<td class=\"nb\">{N(b.X + retrait - gauche, 0)} mm</td><td class=\"nb\">{N(b.Largeur - 2 * retrait, 0)} mm</td><td>{string.Join(" · ", sur)}</td></tr>");
                }
                sb.Append($"</table><p class=\"s\">Longueur de rail à approvisionner : {N(a.LongueurRail)} m.</p>");
            }

            // Goulottes.
            sb.Append("<h3>Goulottes</h3>");
            var goulottes = a.Cablage.Goulottes.OrderBy(g => g.Verticale).ThenBy(g => g.Y).ThenBy(g => g.X).ToList();
            if (goulottes.Count == 0) sb.Append("<p>Aucune goulotte : fils posés en direct.</p>");
            else
            {
                sb.Append("<table><tr><th>Goulotte</th><th>Sens</th><th>Modèle (l × h)</th><th class=\"nb\">Longueur</th><th class=\"nb\">Depuis la gauche de la plaque</th><th class=\"nb\">Depuis le haut de la plaque</th><th class=\"nb\">Remplissage estimé</th></tr>");
                var n = 1;
                foreach (var g in goulottes)
                    sb.Append($"<tr><td class=\"rep\">G{n++}</td><td>{(g.Verticale ? "Verticale" : "Horizontale")}</td><td>{E(g.Modele.Libelle)}</td><td class=\"nb\">{N(g.Longueur, 0)} mm</td>" +
                              $"<td class=\"nb\">{N(g.X - gauche, 0)} mm</td><td class=\"nb\">{N(g.Y - haut, 0)} mm</td><td class=\"nb\">{(g.Manuelle ? "ajoutée à la main" : N(g.Remplissage, 0) + " %")}</td></tr>");
                sb.Append($"</table><p class=\"s\">Longueur de goulotte à approvisionner : {N(a.LongueurGoulotte)} m. Positions données au coin haut gauche de la goulotte.</p>");
            }

            // Appareils : où les poser.
            sb.Append($"<h2 class=\"saut\">{E(a.Nom)} — appareils à poser</h2>");
            sb.Append("<table><tr><th>Repère</th><th>Désignation</th><th>Référence</th><th>Zone</th><th class=\"nb\">Depuis la gauche</th><th class=\"nb\">Depuis le haut</th><th class=\"nb\">L × H × P (mm)</th></tr>");
            foreach (var e in a.Appareils.OrderBy(e => Math.Round(e.Y / 20)).ThenBy(e => e.X))
                sb.Append($"<tr><td class=\"rep\">{E(e.Repere)}</td><td>{E(e.Designation)}</td><td class=\"ref\">{E(((e.Element?.Fabricant ?? "") + " " + (e.Element?.Reference ?? "")).Trim())}</td>" +
                          $"<td>{Zone(e.Zone, false)}</td><td class=\"nb\">{N(e.X - gauche, 0)}</td><td class=\"nb\">{N(e.Y - haut, 0)}</td><td class=\"nb\">{N(e.Largeur, 0)} × {N(e.Hauteur, 0)} × {N(e.Profondeur, 0)}</td></tr>");
            foreach (var e in a.Porte)
                sb.Append($"<tr><td class=\"rep\">{E(e.Repere)}</td><td>{E(e.Designation)}</td><td class=\"ref\">{E(((e.Element?.Fabricant ?? "") + " " + (e.Element?.Reference ?? "")).Trim())}</td>" +
                          $"<td>En porte</td><td class=\"nb\">{N(e.X, 0)}</td><td class=\"nb\">{N(e.Y, 0)}</td><td class=\"nb\">{N(e.Largeur, 0)} × {N(e.Hauteur, 0)} × {N(e.Profondeur, 0)}</td></tr>");
            sb.Append("</table><p class=\"s\">Positions du coin haut gauche de l'appareil, en mm depuis le coin haut gauche de la plaque de montage (depuis l'enveloppe pour le matériel en porte).</p>");

            // Filerie estimée.
            if (a.Cablage.Fils.Count > 0)
            {
                sb.Append("<h3>Filerie estimée</h3><table><tr><th>Usage</th><th class=\"nb\">Section</th><th class=\"nb\">Nombre de fils</th><th class=\"nb\">Longueur estimée</th></tr>");
                foreach (var f in a.Cablage.Fils.OrderByDescending(f => f.Section))
                    sb.Append($"<tr><td>{E(f.Usage)}</td><td class=\"nb\">{N(f.Section)} mm²</td><td class=\"nb\">{f.Nombre}</td><td class=\"nb\">{N(f.Nombre * a.Cablage.LongueurMoyenne, 0)} m</td></tr>");
                sb.Append($"</table><p class=\"s\">Estimation d'après les appareils implantés ({N(a.Cablage.LongueurMoyenne)} m par fil en moyenne) : à ajuster sur le schéma.</p>");
            }
        }

        sb.Append("""
            <h2>Règles de câblage CEM</h2>
            <ul class="regles">
            <li><span class="zone z-Propre">Zone propre</span> automate, entrées / sorties, sécurité, signaux et bus : aucun câble de puissance ni câble moteur n'y passe.</li>
            <li><span class="zone z-Sale">Zone sale</span> variateurs, contacteurs de puissance, filtres et départs moteurs : les câbles moteurs sortent au plus court vers le bas de l'armoire.</li>
            <li><span class="zone z-Intermediaire">Zone intermédiaire</span> protections et distribution : elle sépare les deux précédentes.</li>
            <li>Puissance et signaux dans des goulottes différentes ; s'ils doivent se croiser, croisement à angle droit.</li>
            <li>Câbles blindés (moteur de variateur, codeur, analogique, bus) : blindage repris sur 360° par collier sur la plaque ou la barre de masse, aux deux extrémités pour les câbles moteurs, sans queue de cochon.</li>
            <li>Plaque de montage non peinte ou peinture retirée sous les variateurs, les filtres et les colliers de blindage ; liaisons de masse courtes et larges (tresses).</li>
            <li>Conducteur aller et retour d'un même circuit dans la même goulotte ; pas de boucle de réserve enroulée.</li>
            </ul>
            <p class="s">Références : directive 2014/30/UE, NF EN 60204-1, NF EN 61800-3 et notices de montage des variateurs.</p>
            </body></html>
            """);
        return sb.ToString();
    }

    static string Zone(ZoneCem z, bool bornier) => bornier ? "Borniers" : z switch
    {
        ZoneCem.Propre => "<span class=\"zone z-Propre\">Propre</span>",
        ZoneCem.Sale => "<span class=\"zone z-Sale\">Sale</span>",
        ZoneCem.Intermediaire => "<span class=\"zone z-Intermediaire\">Intermédiaire</span>",
        _ => E(z.ToString()),
    };
}
