using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public class ResultatDepart
{
    public required Depart Depart { get; init; }
    public required NoteDeCalcul Note { get; init; }

    /// <summary>Courant d'emploi, en A.</summary>
    public double CourantEmploi { get; init; }

    /// <summary>Courant assigné (ou de réglage) de la protection, en A.</summary>
    public double CalibreProtection { get; init; }
    public double Section { get; init; }
    public double ChuteTension { get; init; }

    /// <summary>Résultat complet du câble (Iz corrigé, PE, chute amont) pour la synthèse du rapport.</summary>
    public ResultatCable? Cable { get; init; }

    /// <summary>Puissance active absorbée, en kW, avant coefficient d'utilisation.</summary>
    public double PuissanceActive { get; init; }

    /// <summary>Puissance réactive absorbée, en kvar, avant coefficient d'utilisation.</summary>
    public double PuissanceReactive { get; init; }

    /// <summary>Premier appareil du départ (sa protection) avec ses appareils aval.</summary>
    public required ElementMateriel Materiel { get; init; }
}

/// <summary>Charge d'un transformateur 400/230 V : départs qu'il alimente, en kW et kvar.</summary>
public record ChargeAval(double P, double Q)
{
    public IReadOnlyList<string> Departs { get; init; } = Array.Empty<string>();
    public double S => Math.Sqrt(P * P + Q * Q);
}

/// <summary>Dimensionne un départ : courant d'emploi, protection, contacteur(s), câble.</summary>
public static class CalculDepart
{
    const string RefProtection = "NF C 15-100, 433 ; NF EN 60947-2";
    const string RefContacteur = "NF EN 60947-4-1, catégories d'emploi";
    const string RefFrein = "NF EN 60204-1, 7.2.9 et 9.3.4 ; documentation du moteur-frein";

    /// <param name="consommation24V">Consommation 24 V DC du projet, en A, pour les départs d'alimentation 24 V.</param>
    /// <param name="fabricant">Fabricant du projet (alimentations).</param>
    /// <param name="marqueVariateurs">Marque des variateurs laissés au choix du projet ; le fabricant du projet si absente.</param>
    /// <param name="variateurs">Catalogue des variateurs ; le catalogue embarqué par défaut.</param>
    public static ResultatDepart Calculer(Depart depart, Reseau reseau, double chuteAmont = 0, double consommation24V = 0,
        FabricantMateriel fabricant = FabricantMateriel.Schneider, IReadOnlyList<ModeleVariateur>? variateurs = null,
        IReadOnlyList<ConsommationCommande>? postes24V = null, IReadOnlyList<ModeleAlimentation>? alimentations = null,
        ChargeAval? chargeAval = null, FabricantMateriel? marqueVariateurs = null) => depart switch
    {
        DepartAlimentation24V { EstTransformateur: true } t => Transformateur(t, reseau, chuteAmont, consommation24V, postes24V, fabricant,
            alimentations ?? CatalogueAlimentations.Charger(), chargeAval),
        DepartAlimentation24V a => Alimentation24V(a, reseau, chuteAmont, consommation24V, postes24V, fabricant,
            alimentations ?? CatalogueAlimentations.Charger()),
        DepartMoteur m => Moteur(m, reseau, chuteAmont, marqueVariateurs ?? fabricant, variateurs ?? CatalogueVariateurs.Charger()),
        DepartResistif r => Resistif(r, reseau, chuteAmont),
        DepartPrise p => Prise(p, reseau, chuteAmont),
        DepartGenerique g => Generique(g, reseau, chuteAmont),
        _ => throw new NotSupportedException(depart.GetType().Name),
    };

    static double Tension(TypeAlimentation a) => a == TypeAlimentation.Mono230 ? 230 : 400;
    static double Racine(TypeAlimentation a) => a is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 1 : Math.Sqrt(3);
    /// <summary>La formule du courant, puis la même avec les valeurs et le résultat : « I = P / (√3 × U × cos φ) = 5 500 / (√3 × 400 × 0,85) = 9,34 A ».</summary>
    static string FormuleCourant(TypeAlimentation a, string suffixe, double puissanceW, double tension, params double[] facteurs)
    {
        var mono = a is TypeAlimentation.Mono230 or TypeAlimentation.Bi400;
        var valeurs = string.Join("", facteurs.Select(f => $" × {N(f)}"));
        var courant = puissanceW / ((mono ? 1 : Math.Sqrt(3)) * tension * facteurs.Aggregate(1.0, (x, f) => x * f));
        var denominateur = $"{(mono ? "" : "√3 × ")}{N(tension)}{valeurs}";
        return (mono ? suffixe.Length > 0 ? $"I = P / (U{suffixe})" : "I = P / U" : $"I = P / (√3 × U{suffixe})") +
               $" = {N(puissanceW, 0)} / {(denominateur.Contains('×') ? $"({denominateur})" : denominateur)} = {N(courant)} A";
    }

    static NoteDeCalcul NouvelleNote(Depart d, string groupe) => new($"{d.Code}", d.Forcages) { Groupe = groupe };

    static ResultatDepart Moteur(DepartMoteur m, Reseau reseau, double chuteAmont, FabricantMateriel fabricant, IReadOnlyList<ModeleVariateur> variateurs)
    {
        var note = NouvelleNote(m, "Moteur");
        var u = Tension(m.Alimentation);
        var inCalcule = m.PuissanceKw * 1000 / (Racine(m.Alimentation) * u * m.CosPhi * m.Rendement);
        var entrees = $"Pn = {N(m.PuissanceKw)} kW, U = {N(u)} V, cos φ = {N(m.CosPhi)}, η = {N(m.Rendement)}";
        if (m.OrigineRendement.Length > 0 && m.RendementCalcule is { } rc)
            note.Info("Rendement", $"η = {N(m.Rendement * 100, 1)} %, {m.OrigineRendement} : {rc.Detail}." +
                (rc.Alerte.Length > 0 ? " " + rc.Alerte : ""), "η = puissance utile / puissance absorbée");
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi du moteur", inCalcule,
            m.CourantPlaque > 0 ? m.CourantPlaque : inCalcule, "A",
            FormuleCourant(m.Alimentation, " × cos φ × η", m.PuissanceKw * 1000, u, m.CosPhi, m.Rendement) + (m.CourantPlaque > 0 ? " ; valeur de plaque retenue" : ""),
            entrees, "Plaque signalétique, NF EN 60034-1");
        if (m.CourantPlaque > 0)
        {
            var ecart = Math.Abs(m.CourantPlaque - inCalcule) / m.CourantPlaque;
            note.Verifier("Cohérence avec la plaque", ecart <= 0.10,
                $"I_{{n,plaque}} = {A(m.CourantPlaque)}, I_{{n,calc}} = {A(inCalcule)} ; écart ε = |I_{{n,plaque}} − I_{{n,calc}}| / I_{{n,plaque}} = {Pct(ecart * 100)}",
                siEchec: StatutCalcul.Alerte);
        }

        var pAbsorbee = m.PuissanceKw / m.Rendement;
        var q = pAbsorbee * Math.Tan(Math.Acos(m.CosPhi));
        var n = m.Numero;
        double calibreProtection;
        ElementMateriel protection;
        ElementMateriel avalProtection;

        double courantCable;
        var servo = m.Demarrage == ModeDemarrage.Servomoteur;
        note.Groupe = "Protection";
        if (m.SurVariateur)
        {
            if (m.Protection == TypeProtection.Fusibles)
            {
                (calibreProtection, protection, avalProtection) = Fusibles(m, note, reseau, ib, ClasseFusible.gR);
                courantCable = ib;
            }
            else
            {
                calibreProtection = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur amont du variateur",
                    ib, Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A", "In ≥ Ib, calibre normalisé supérieur",
                    $"Ib = {A(ib)}", RefProtection);
                protection = avalProtection = Element($"Q{n}", "Disjoncteur", "Disjoncteur amont variateur",
                    $"{A(calibreProtection)}, {Poles(m.Alimentation, reseau.Slt)}")
                    .Avec(calibreProtection, NombrePoles(m.Alimentation, reseau.Slt), courbe: "C");
                courantCable = calibreProtection;
            }
            note.Groupe = "Variateur";
            note.Info("Coordination variateur",
                "Valider la protection amont (disjoncteur ou fusibles gR/aR) dans le tableau de coordination du fabricant du variateur.");
            note.Info("Variateur", $"Courant de sortie assigné ≥ {A(ib)} en service normal ; vérifier la surcharge nécessaire au process.");
            note.Info("CEM", "Câble moteur blindé, blindage repris à 360° aux deux extrémités, filtre CEM côté réseau.",
                "Directive 2014/30/UE ; NF EN 61800-3");
            var marque = CatalogueVariateurs.Marque(m.MarqueVariateur, fabricant);
            var mono = m.Alimentation == TypeAlimentation.Mono230;
            var impose = CatalogueVariateurs.Trouver(m.ReferenceVariateur, variateurs);
            var modele = impose ?? CatalogueVariateurs.Choisir(marque, m.PuissanceKw, ib, variateurs, servo, mono);
            if (impose is not null)
            {
                marque = Enum.TryParse<FabricantMateriel>(impose.Fabricant, true, out var f) ? f : marque;
                var suffisant = impose.Kw >= m.PuissanceKw - 1e-6 && impose.Courant >= ib - 1e-6;
                note.Verifier("Variateur imposé", suffisant,
                    $"{impose.Reference} choisi à la main : {N(impose.Kw)} kW et {A(impose.Courant)} pour un moteur de {N(m.PuissanceKw)} kW à {A(ib)}" +
                    (suffisant ? "." : " : trop petit, choisir un calibre au-dessus ou repasser en automatique."),
                    "Catalogue du fabricant ; NF EN 61800-2");
            }
            else if (m.ReferenceVariateur.Trim().Length > 0)
                note.Info("Variateur imposé", $"{m.ReferenceVariateur} n'est pas au catalogue des variateurs : choix automatique d'après la puissance.");
            if (servo)
                note.Info("Servomoteur", "Servovariateur et servomoteur de la même gamme ; câble moteur et câble codeur blindés préconfectionnés, " +
                    "résistance de freinage selon le cycle (énergie renvoyée au freinage).", "Documentation du fabricant ; NF EN 61800-5-1");
            var variateur = Element($"U{n}", "Variateur", servo ? "Servovariateur" : "Variateur de fréquence",
                modele is null ? $"{N(m.PuissanceKw)} kW, sortie ≥ {A(ib)}" : $"{N(modele.Kw)} kW, {A(modele.Courant)}, {modele.Taille}")
                .Avec(modele?.Courant ?? ib, puissanceKw: m.PuissanceKw);
            // Marque choisie gardée même sans modèle au catalogue ; un servo ou un variateur monophasé n'a pas d'équivalent générique.
            variateur.Fabricant = marque.ToString();
            if (modele is null && (servo || mono)) variateur.ReferenceAuto = false;
            if (modele is not null)
            {
                variateur.Fabricant = $"{marque} – {modele.Gamme}";
                variateur.Reference = modele.Reference;
                variateur.LargeurMm = modele.Largeur;
                variateur.HauteurMm = modele.Hauteur;
                variateur.ProfondeurMm = modele.Profondeur;
                variateur.PertesW = modele.Pertes;
                variateur.FiltreCemIntegre = modele.FiltreCemIntegre;
                variateur.BlindageFourni = modele.BlindageFourni;
            }
            note.Verifier("Variateur retenu", modele is not null,
                modele is null
                    ? $"Aucun {(servo ? "servovariateur" : "variateur")} {marque}{(mono ? " monophasé 230 V" : "")} du catalogue ne couvre {N(m.PuissanceKw)} kW et {A(ib)} : choisir une autre marque ou compléter le catalogue."
                    : $"{marque} {modele.Gamme}, taille {modele.Taille} : {modele.Reference}, {N(modele.Kw)} kW, {A(modele.Courant)} ; " +
                      $"{N(modele.Largeur)} × {N(modele.Hauteur)} × {N(modele.Profondeur)} mm (L × H × P), pertes ≈ {N(modele.Pertes)} W" +
                      (modele.Verifie ? "." : " (indicatif, à vérifier sur la fiche du fabricant)."),
                "Catalogues ABB, Siemens, Schneider ; NF EN 61800-2", StatutCalcul.Alerte);
            avalProtection = avalProtection.Ajouter(variateur);
        }
        else if (m.Protection == TypeProtection.Fusibles)
        {
            // Fusibles : aM contre les courts-circuits, relais thermique contre les surcharges
            // (gR pour protéger les thyristors d'un démarreur progressif).
            var progressif = m.Demarrage == ModeDemarrage.DemarreurProgressif;
            (calibreProtection, protection, avalProtection) =
                Fusibles(m, note, reseau, ib, progressif ? ClasseFusible.gR : ClasseFusible.aM);
            note.Groupe = "Protection";
            var reglage = note.Valeur(CodesCalcul.ReglageThermique, "Réglage du relais thermique", ib, ib, "A", "Ir = Ib",
                $"Ib = {A(ib)}", "NF EN 60947-4-1");
            courantCable = reglage;
            note.Info("Coordination", "Vérifier la coordination type 2 fusibles / contacteur / relais dans les tables du fabricant.",
                "NF EN 60947-4-1");
            avalProtection = progressif
                ? DemarreurProgressif(m, note, ib, avalProtection)
                : Contacteurs(m, note, ib, avalProtection);
            // En deux vitesses, chaque vitesse a déjà son relais thermique.
            if (m.Demarrage != ModeDemarrage.DeuxVitesses)
                avalProtection = avalProtection.Ajouter(Element($"F{n}", "Relais thermique", "Relais de protection thermique",
                    $"réglé à {A(reglage)}").Avec(reglage));
        }
        else
        {
            var plage = Calibres.PlageMoteur(ib);
            var courantAppareillage = ib;
            if (plage is { } p)
            {
                calibreProtection = note.Valeur(CodesCalcul.ReglageThermique, "Réglage thermique du disjoncteur moteur",
                    ib, ib, "A", "Ir = Ib", $"plage {N(p.Min)}–{N(p.Max)} A", "NF EN 60947-4-1");
                // Réglage forcé hors de la plage calculée : le disjoncteur moteur est celui dont la plage contient le réglage,
                // et le contacteur suit ce réglage.
                if (calibreProtection < p.Min || calibreProtection > p.Max)
                {
                    if (Calibres.PlageMoteur(calibreProtection) is { } pf) p = pf;
                    else note.Verifier("Réglage thermique forcé", false,
                        $"Aucun disjoncteur moteur ne se règle à {A(calibreProtection)} ; plage conservée {N(p.Min)}–{N(p.Max)} A.",
                        "NF EN 60947-4-1", StatutCalcul.Alerte);
                }
                courantAppareillage = Math.Max(ib, Math.Min(calibreProtection, p.Max));
                var im = Calibres.SeuilMagnetiqueDisjoncteurMoteur * p.Max;
                // Un démarreur progressif limite l'appel de courant à environ 4 × In.
                var rapportId = m.Demarrage == ModeDemarrage.DemarreurProgressif ? Math.Min(m.RapportId, 4) : m.RapportId;
                var crete = 2 * rapportId * ib;
                note.Verifier("Tenue au démarrage", im >= crete,
                    $"Seuil magnétique ≈ {A(im)} ; pointe de démarrage estimée 2 × Id = {A(crete)}",
                    "NF EN 60947-4-1", StatutCalcul.Alerte);
                protection = Element($"Q{n}", "Disjoncteur moteur", "Disjoncteur moteur magnétothermique",
                    $"{N(p.Min)}–{N(p.Max)} A, réglé à {A(calibreProtection)}")
                    .Avec(calibreProtection, 3, plageMin: p.Min, plageMax: p.Max);
                avalProtection = protection;
            }
            else
            {
                calibreProtection = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur courbe D",
                    ib, Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A",
                    "In ≥ Ib ; au-delà des disjoncteurs moteurs, disjoncteur courbe D et relais thermique",
                    $"Ib = {A(ib)}", RefProtection);
                protection = Element($"Q{n}", "Disjoncteur", "Disjoncteur courbe D",
                    $"{A(calibreProtection)}, {Poles(m.Alimentation, reseau.Slt)}")
                    .Avec(calibreProtection, NombrePoles(m.Alimentation, reseau.Slt), courbe: "D");
                avalProtection = protection.Ajouter(Element($"F{n}", "Relais thermique", "Relais de protection thermique",
                    $"réglé à {A(ib)}").Avec(ib));
            }
            courantCable = calibreProtection;
            avalProtection = m.Demarrage == ModeDemarrage.DemarreurProgressif
                ? DemarreurProgressif(m, note, courantAppareillage, avalProtection)
                : Contacteurs(m, note, courantAppareillage, avalProtection);
        }
        note.Groupe = "Protection";
        Differentiel(m, note, reseau.Slt, m.SurVariateur
            ? (m.Alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? "F" : "B")
            : "A", articleSepare: true);
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA (Icc à l'arrivée).", RefProtection);

        // En étoile-triangle, deux câbles alimentent les enroulements, chacun parcouru par I / √3.
        // En deux vitesses, un câble par vitesse (deux enroulements, ou les deux couplages Dahlander).
        var et = m.Demarrage == ModeDemarrage.EtoileTriangle;
        note.Groupe = "Câble";
        courantCable = CourantCable(note, m, courantCable, ib);
        var cable = CalculCable.Dimensionner(note, m.Cable, m.Alimentation,
            et ? courantCable / Math.Sqrt(3) : courantCable, et ? ib / Math.Sqrt(3) : ib,
            m.CosPhi, reseau, chuteAmont);
        Frein(m, note, protection);
        Condensateur(m, note, avalProtection);
        var w = avalProtection.Ajouter(ElementCable(n, m, cable, reseau.Slt, et || m.Demarrage == ModeDemarrage.DeuxVitesses ? 2 : 1));
        w.Ajouter(Element($"M{n}", "Moteur", m.Designation is { Length: > 0 } d ? d : "Moteur",
            $"{N(m.PuissanceKw)} kW, In {A(ib)}").Avec(ib, m.Alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 2 : 3, m.PuissanceKw));

        return Resultat(m, note, ib, calibreProtection, cable, pAbsorbee, q, protection);
    }

    static ElementMateriel DemarreurProgressif(DepartMoteur m, NoteDeCalcul note, double ib, ElementMateriel amont)
    {
        note.Groupe = "Démarreur progressif";
        note.Info("Démarreur progressif", $"Courant assigné ≥ {A(ib)} pour le service de démarrage du process (nombre de démarrages par heure).",
            "NF EN 60947-4-2");
        note.Info("Contacteur de ligne", "Prévoir un contacteur de ligne si l'arrêt d'urgence ou la sécurité machine l'exige.",
            "NF EN 60204-1, 9.2");
        return amont.Ajouter(Element($"U{m.Numero}", "Démarreur progressif", "Démarreur progressif", $"Ie ≥ {A(ib)}")
            .Avec(ib, puissanceKw: m.PuissanceKw));
    }

    /// <summary>Sectionneur porte-fusibles et cartouches : renvoie le calibre, le sectionneur et les cartouches.</summary>
    static (double Calibre, ElementMateriel Sectionneur, ElementMateriel Cartouches) Fusibles(Depart d, NoteDeCalcul note,
        Reseau reseau, double ib, ClasseFusible classe)
    {
        note.Groupe = "Protection";
        var usage = classe switch
        {
            ClasseFusible.aM => "accompagnement moteur, surcharge assurée par le relais thermique",
            ClasseFusible.gR => "protection des semi-conducteurs ; calibre à confirmer avec le fabricant de l'appareil",
            _ => "usage général, surcharge et court-circuit",
        };
        var calibre = note.Valeur(CodesCalcul.CalibreProtection, $"Calibre des fusibles {classe}", ib,
            Calibres.Superieur(Calibres.Fusibles, ib) ?? ib, "A", "In ≥ Ib, calibre normalisé supérieur",
            $"Ib = {A(ib)} ; {usage}", "NF EN 60269 ; NF C 15-100, 433");
        var taille = Calibres.TailleFusible(calibre, classe);
        var poles = Poles(d.Alimentation, reseau.Slt);
        var cartouches = d.Alimentation switch
        {
            TypeAlimentation.Mono230 => 1,
            TypeAlimentation.Bi400 => 2,
            _ => 3,
        };
        note.Info("Porte-fusibles", $"Sectionneur porte-fusibles taille {taille}, {poles} ; {cartouches} cartouche(s) {classe} {A(calibre)}.",
            "NF EN 60947-3");
        note.Verifier("Pouvoir de coupure", reseau.IccKiloAmperes <= 50,
            $"Cartouches industrielles gG / aM : au moins 50 kA sous 400 V, pour Icc = {N(reseau.IccKiloAmperes)} kA" +
            (reseau.IccKiloAmperes <= 50 ? "." : " : vérifier le pouvoir de coupure des cartouches choisies."),
            "NF EN 60269-2", StatutCalcul.Alerte);
        var sectionneur = Element($"Q{d.Numero}", "Sectionneur porte-fusibles", $"Sectionneur porte-fusibles {taille}", poles)
            .Avec(calibre, NombrePoles(d.Alimentation, reseau.Slt), tailleFusible: taille);
        var fusibles = sectionneur.Ajouter(new ElementMateriel
        {
            Repere = $"FU{d.Numero}", Famille = "Fusible", Designation = $"Cartouche fusible {classe} {taille}",
            Calibre = A(calibre), Quantite = cartouches,
        }.Avec(calibre, tailleFusible: taille));
        return (calibre, sectionneur, fusibles);
    }

    /// <summary>
    /// Frein d'un moteur-frein : par la boîte à bornes (démarrage direct), par une alimentation séparée (tension et nature
    /// saisies) protégée par un disjoncteur dimensionné sur le courant du frein et commandée par contacteur, ou en 24 V DC
    /// par relais. Sur variateur, le frein peut être piloté par la logique de frein du variateur au lieu de l'automate.
    /// </summary>
    static void Frein(DepartMoteur m, NoteDeCalcul note, ElementMateriel protection)
    {
        if (!m.Frein) return;
        note.Groupe = "Frein";
        var n = m.Numero;
        var alimentation = m.AlimentationFrein;
        var direct = m.Demarrage is ModeDemarrage.Direct or ModeDemarrage.Inverseur;
        if (alimentation == AlimentationFrein.BoiteABornes && !direct)
        {
            note.Verifier("Frein", false, "Le frein ne peut pas être alimenté par la boîte à bornes avec ce démarrage (tension variable ou couplage) : " +
                "alimentation séparée retenue.", RefFrein, StatutCalcul.Alerte);
            alimentation = AlimentationFrein.Separee230;
        }

        var parVariateur = m.CommandeFrein == CommandeFrein.Variateur;
        if (parVariateur && !m.SurVariateur)
        {
            note.Verifier("Commande du frein", false, "Pas de variateur sur ce départ : le frein est commandé par l'automate.", RefFrein, StatutCalcul.Alerte);
            parVariateur = false;
        }
        var servo = m.Demarrage == ModeDemarrage.Servomoteur;
        var commande = parVariateur
            ? servo
                ? "piloté par le servovariateur (sortie frein intégrée, Lexium 32 ou SINAMICS V90) : il s'ouvre quand le couple est établi et retombe à l'arrêt"
                : "piloté par la logique de frein du variateur (Altivar : fonction « commande de frein » affectée au relais R2 ; SINAMICS G120 : " +
                  "commande du frein de maintien sur une sortie TOR) : ouverture quand le courant moteur est établi, retombée à vitesse nulle, sans automate"
            : "commandé par une sortie de l'automate, avec la marche du moteur";

        switch (alimentation)
        {
            case AlimentationFrein.BoiteABornes:
                note.Info("Frein", $"Frein {N(m.PuissanceFreinW)} W alimenté par le redresseur de la boîte à bornes : il retombe à l'ouverture du contacteur.",
                    RefFrein);
                break;
            case AlimentationFrein.Separee230:
            {
                var u = m.TensionFreinV > 0 ? m.TensionFreinV : 230;
                var nature = m.FreinEnContinu ? "DC" : "AC";
                var courant = m.PuissanceFreinW / u;
                // Bobine surexcitée à l'appel par les redresseurs rapides : calibre ≥ 2 × I, 1 A au minimum, courbe C.
                var calibre = Calibres.Superieur(Calibres.Disjoncteurs, Math.Max(1, 2 * courant)) ?? 2;
                const int poles = 2;
                var libellePoles = m.FreinEnContinu ? "2P" : u > 250 ? "2P (entre phases)" : "1P+N";
                calibre = note.Valeur(CodesCalcul.CalibreFrein, "Disjoncteur du frein", 2 * courant, calibre, "A",
                    "In ≥ 2 × P / U (appel de la bobine), 1 A minimum, courbe C",
                    $"P = {N(m.PuissanceFreinW)} W, U = {N(u, 0)} V {nature}, I = {A(courant)}", RefFrein);
                note.Info("Frein", $"Frein {N(m.PuissanceFreinW)} W alimenté en {N(u, 0)} V {nature} ({A(courant)}) par Q{n}.F et KM{n}.F, {commande}." +
                    (m.FreinEnContinu ? " Disjoncteur et contacteur prévus pour le courant continu (tension DC assignée ≥ " + N(u, 0) + " V, coupure DC-13)." :
                        " Coupure côté continu du redresseur pour un serrage rapide."), RefFrein);
                var qf = Element($"Q{n}.F", "Disjoncteur", "Disjoncteur du frein",
                    $"{A(calibre)}, {libellePoles}, courbe C" + (m.FreinEnContinu ? ", pour courant continu" : "")).Avec(calibre, poles, courbe: "C");
                var kmf = Element($"KM{n}.F", "Contacteur", "Contacteur de frein", m.FreinEnContinu ? "DC-13, pôles en série" : "AC-3 9 A").Avec(9, 3);
                // Le catalogue n'a que des appareils pour courant alternatif : en continu, les références restent à choisir.
                qf.ReferenceAuto = kmf.ReferenceAuto = !m.FreinEnContinu;
                var km = protection.Ajouter(qf).Ajouter(kmf);
                Redresseur(m, note, km, u, RefFrein);
                break;
            }
            default:
                if (parVariateur && servo)
                {
                    note.Info("Frein", $"Frein {N(m.PuissanceFreinW)} W en 24 V DC ({A(m.PuissanceFreinW / 24)} au bilan 24 V), {commande} : pas de relais.",
                        RefFrein);
                    break;
                }
                note.Info("Frein", $"Frein {N(m.PuissanceFreinW)} W en 24 V DC ({A(m.PuissanceFreinW / 24)} au bilan 24 V) commandé par le relais KA{n}.F, {commande}.",
                    RefFrein);
                protection.Ajouter(new ElementMateriel
                {
                    Repere = $"KA{n}.F", Famille = "Relais", Designation = "Relais de commande du frein 24 V DC",
                    Calibre = $"1 contact 6 A, ≥ {A(m.PuissanceFreinW / 24)}", LargeurMm = 6.2, HauteurMm = 90, ProfondeurMm = 70, PertesW = 0.5,
                });
                break;
        }
    }

    /// <summary>Redresseurs de frein : demi-onde (U DC ≈ 0,45 × U AC) ou pont (≈ 0,9 × U AC).</summary>
    public static (string Type, double Rapport)[] Redresseurs { get; } = { ("demi-onde", 0.45), ("pont", 0.9) };

    /// <summary>
    /// Bobine en courant continu alimentée en alternatif. Avec le redresseur du moteur, on contrôle seulement que la tension de bobine
    /// correspond à un demi-onde (0,45 × U) ou à un pont (0,9 × U), par exemple 180 V DC sous 400 V AC. Sans redresseur dans le moteur,
    /// l'armoire reçoit : une alimentation à découpage pour une bobine 24 ou 48 V DC ; un redresseur de frein quand le rapport tombe
    /// juste ; sinon un transformateur d'adaptation suivi d'un redresseur en pont (les alimentations à découpage du catalogue
    /// ne sortent pas de 100 à 200 V DC).
    /// </summary>
    /// <summary>Vrai quand la bobine du frein attend une source dans l'armoire : alimentation séparée alternative, sans redresseur dans le moteur.</summary>
    public static bool SourceFreinDansArmoire(DepartMoteur m) =>
        m.Frein && m.AlimentationFrein == AlimentationFrein.Separee230 && !m.FreinEnContinu && !m.RedresseurDansMoteur && m.TensionBobineFreinV > 0;

    /// <summary>Sources possibles pour la bobine du frein, la recommandée en premier, avec la raison de chacune.</summary>
    public static IReadOnlyList<(SourceFrein Source, string Libelle)> SourcesFreinPossibles(DepartMoteur m)
    {
        var u = m.TensionFreinV > 0 ? m.TensionFreinV : 230;
        var bobine = m.TensionBobineFreinV;
        var (type, rapport) = Redresseurs.MinBy(r => Math.Abs(bobine / u - r.Rapport));
        var liste = new List<(SourceFrein, string)>();
        if (Math.Abs(bobine - 24) <= 2.4 || Math.Abs(bobine - 48) <= 4.8)
            liste.Add((SourceFrein.Decoupage, $"Alimentation à découpage {(bobine < 36 ? 24 : 48)} V DC dédiée au frein"));
        if (Math.Abs(rapport * u / bobine - 1) <= 0.1)
            liste.Add((SourceFrein.Redresseur, $"Redresseur de frein {type} : {N(rapport)} × {N(u, 0)} V AC = {N(rapport * u, 0)} V DC"));
        liste.Add((SourceFrein.Transformateur, $"Transformateur {N(u, 0)} / {N(Math.Round(bobine / 0.9 / 5) * 5, 0)} V AC puis redresseur en pont"));
        return liste;
    }

    static void Redresseur(DepartMoteur m, NoteDeCalcul note, ElementMateriel amont, double u, string reference)
    {
        if (m.FreinEnContinu || m.TensionBobineFreinV <= 0) return;
        var bobine = m.TensionBobineFreinV;
        var courant = m.PuissanceFreinW / bobine;
        var (type, rapport) = Redresseurs.MinBy(r => Math.Abs(bobine / u - r.Rapport));
        var obtenue = rapport * u;
        var accord = Math.Abs(obtenue / bobine - 1) <= 0.1;
        var n = m.Numero;

        if (m.RedresseurDansMoteur)
        {
            note.Verifier("Tension de bobine du frein", accord,
                accord
                    ? $"Bobine {N(bobine, 0)} V DC sous {N(u, 0)} V AC : redresseur {type} dans la boîte à bornes ({N(rapport)} × {N(u, 0)} = {N(obtenue, 0)} V DC)."
                    : $"Bobine {N(bobine, 0)} V DC sous {N(u, 0)} V AC : aucun redresseur courant ne donne cette tension " +
                      $"(demi-onde {N(0.45 * u, 0)} V, pont {N(0.9 * u, 0)} V). Vérifier la tension d'alimentation du frein.",
                reference, StatutCalcul.Alerte);
            return;
        }

        var possibles = SourcesFreinPossibles(m);
        var choix = possibles.Any(p => p.Source == m.SourceFrein) ? m.SourceFrein : possibles[0].Source;
        if (choix != m.SourceFrein)
            note.Verifier("Source du frein", false, $"La source demandée ne convient pas à une bobine {N(bobine, 0)} V DC : " +
                $"{possibles[0].Libelle.ToLowerInvariant()} retenue à la place.", RefFrein, StatutCalcul.Alerte);
        ElementMateriel source;
        if (choix == SourceFrein.Decoupage)
        {
            var sortie = bobine < 36 ? 24 : 48;
            source = new ElementMateriel
            {
                Repere = $"G{n}.F", Famille = "Alimentation", Designation = $"Alimentation à découpage du frein {sortie} V DC",
                Calibre = $"{N(u, 0)} V AC → {sortie} V DC, ≥ {A(1.5 * m.PuissanceFreinW / sortie)}", Courant = 1.5 * m.PuissanceFreinW / sortie,
                // Le catalogue n'a que des alimentations 24 V : une 48 V reste à choisir.
                ReferenceAuto = sortie == 24,
            };
            note.Info("Alimentation du frein", $"Bobine {N(bobine, 0)} V DC : alimentation à découpage {sortie} V DC dédiée, calibrée à 1,5 × {A(courant)} " +
                "pour l'appel de la bobine.", reference);
        }
        else if (choix == SourceFrein.Redresseur)
        {
            source = new ElementMateriel
            {
                Repere = $"V{n}.F", Famille = "Redresseur", Designation = $"Redresseur de frein {type}",
                Calibre = $"{N(u, 0)} V AC → {N(bobine, 0)} V DC, ≥ {A(courant)}",
                LargeurMm = 22.5, HauteurMm = 90, ProfondeurMm = 100, PertesW = 1,
            };
            note.Info("Alimentation du frein", $"Bobine {N(bobine, 0)} V DC sous {N(u, 0)} V AC : redresseur {type} dans l'armoire " +
                $"({N(rapport)} × {N(u, 0)} = {N(obtenue, 0)} V DC).", reference);
        }
        else
        {
            // Secondaire qui donne la tension de bobine à travers un pont : U AC = U DC / 0,9.
            var secondaire = Math.Round(bobine / 0.9 / 5) * 5;
            var va = Calibres.Superieur(new double[] { 25, 40, 63, 100, 160, 250, 400 }, 1.5 * m.PuissanceFreinW) ?? 1.5 * m.PuissanceFreinW;
            amont = amont.Ajouter(new ElementMateriel
            {
                Repere = $"T{n}.F", Famille = "Transformateur", Designation = "Transformateur d'adaptation du frein",
                Calibre = $"{N(u, 0)} / {N(secondaire, 0)} V AC, {N(va, 0)} VA", Courant = va / secondaire,
                LargeurMm = va <= 63 ? 60 : va <= 160 ? 85 : 105, HauteurMm = va <= 63 ? 70 : va <= 160 ? 80 : 95,
                ProfondeurMm = va <= 63 ? 80 : va <= 160 ? 95 : 110, PertesW = 0.08 * va,
            });
            source = new ElementMateriel
            {
                Repere = $"V{n}.F", Famille = "Redresseur", Designation = "Redresseur de frein en pont",
                Calibre = $"{N(secondaire, 0)} V AC → {N(bobine, 0)} V DC, ≥ {A(courant)}",
                LargeurMm = 22.5, HauteurMm = 90, ProfondeurMm = 100, PertesW = 1,
            };
            if (accord || possibles[0].Source != SourceFrein.Transformateur)
                note.Info("Alimentation du frein", $"Bobine {N(bobine, 0)} V DC : transformateur {N(u, 0)} / {N(secondaire, 0)} V ({N(va, 0)} VA) " +
                    "puis redresseur en pont, choisi à la place de la solution directe.", reference);
            else
                note.Verifier("Alimentation du frein", false,
                    $"Bobine {N(bobine, 0)} V DC : ni demi-onde ({N(0.45 * u, 0)} V) ni pont ({N(0.9 * u, 0)} V) sous {N(u, 0)} V AC. " +
                    $"Transformateur {N(u, 0)} / {N(secondaire, 0)} V ({N(va, 0)} VA) puis redresseur en pont ; à confirmer avec le fabricant du frein.",
                    reference, StatutCalcul.Alerte);
        }
        amont.Ajouter(source);
    }

    static readonly double[] CapacitesNormalisees =
        { 2, 2.5, 3, 4, 5, 6, 8, 10, 12, 14, 16, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 100, 120, 150, 200 };

    /// <summary>
    /// Moteur monophasé avec condensateur en armoire : permanent d'environ 40 µF par kW sous 230 V (règle d'usage, à confirmer
    /// sur la plaque), et condensateur de démarrage d'environ 2,5 fois le permanent, coupé par un relais en fin de démarrage.
    /// </summary>
    static void Condensateur(DepartMoteur m, NoteDeCalcul note, ElementMateriel aval)
    {
        if (m.Alimentation != TypeAlimentation.Mono230 || m.SurVariateur) return;
        note.Groupe = "Condensateur";
        if (m.Condensateur == CondensateurMoteur.DansLeMoteur)
        {
            note.Info("Condensateur", "Moteur monophasé : condensateur dans la boîte à bornes du moteur, rien à prévoir en armoire.");
            return;
        }
        var n = m.Numero;
        var estime = Math.Max(2, 40 * m.PuissanceKw);
        var cp = m.CapaciteCondensateurUf > 0 ? m.CapaciteCondensateurUf : CapacitesNormalisees.FirstOrDefault(c => c >= estime - 1e-6, estime);
        note.Verifier("Condensateur permanent", m.CapaciteCondensateurUf > 0,
            m.CapaciteCondensateurUf > 0
                ? $"{N(cp)} µF, 450 V AC, valeur saisie (plaque du moteur)."
                : $"≈ 40 µF/kW × {N(m.PuissanceKw)} kW = {N(estime)} µF → {N(cp)} µF, 450 V AC : estimation, saisir la valeur de la plaque du moteur.",
            "Plaque du moteur ; NF EN 60252-1", StatutCalcul.Alerte);
        aval.Ajouter(Element($"C{n}.1", "Condensateur", "Condensateur permanent de moteur", $"{N(cp)} µF 450 V AC"));
        if (m.Condensateur != CondensateurMoteur.PermanentEtDemarrage) return;
        var cd = CapacitesNormalisees.FirstOrDefault(c => c >= 2.5 * cp - 1e-6, 2.5 * cp);
        note.Info("Condensateur de démarrage", $"≈ 2,5 × {N(cp)} µF → {N(cd)} µF, 330 V AC, coupé en fin de démarrage par un relais de démarrage (ou relais temporisé).",
            "Plaque du moteur ; NF EN 60252-2");
        aval.Ajouter(Element($"C{n}.2", "Condensateur", "Condensateur de démarrage", $"{N(cd)} µF 330 V AC"));
        aval.Ajouter(Element($"KA{n}.C", "Relais", "Relais de coupure du condensateur de démarrage", "relais de démarrage ou temporisé"));
    }

    static ElementMateriel Contacteurs(DepartMoteur m, NoteDeCalcul note, double ib, ElementMateriel protection)
    {
        note.Groupe = "Contacteurs";
        var n = m.Numero;
        if (m.Demarrage == ModeDemarrage.DeuxVitesses)
        {
            // Protection de court-circuit commune (réglée sur la grande vitesse), un relais thermique par vitesse.
            var pPv = m.PuissancePetiteVitesseKw > 0 ? m.PuissancePetiteVitesseKw : 0.6 * m.PuissanceKw;
            var ibPv = m.PuissanceKw > 0 ? ib * pPv / m.PuissanceKw : ib;
            var kmGv = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteur grande vitesse (AC-3)", ib,
                Calibres.Superieur(Calibres.ContacteursAc3, ib) ?? ib, "A", "Ie ≥ Ib grande vitesse", $"Ib = {A(ib)}", RefContacteur);
            var kmPv = Calibres.Superieur(Calibres.ContacteursAc3, ibPv) ?? ibPv;
            note.Info("Petite vitesse", $"{N(pPv)} kW, Ib ≈ {A(ibPv)} : contacteur AC-3 {A(kmPv)} et relais thermique réglé à {A(ibPv)}." +
                (m.PuissancePetiteVitesseKw > 0 ? "" : " Puissance estimée à 60 % de la grande vitesse : saisir la valeur de plaque."), RefContacteur);
            note.Info("Deux vitesses", m.TypeDeuxVitesses == TypeDeuxVitesses.Dahlander
                ? "Dahlander : contacteur petite vitesse, contacteur grande vitesse et contacteur étoile, verrouillés mécaniquement et électriquement."
                : "Deux enroulements séparés : un contacteur par vitesse, verrouillés mécaniquement et électriquement.");
            if (m.DeuxSens && m.TypeDeuxVitesses == TypeDeuxVitesses.Dahlander)
            {
                // Dahlander deux sens : inversion commune en tête, calibrée sur la grande vitesse.
                note.Info("Deux sens", "Contacteurs sens avant et sens arrière en tête, communs aux deux vitesses, verrouillés mécaniquement et électriquement.");
                protection.Ajouter(Element($"KM{n}.5", "Contacteur", "Contacteur sens arrière", $"AC-3 {A(kmGv)}").Avec(kmGv, 3));
                protection = protection.Ajouter(Element($"KM{n}.4", "Contacteur", "Contacteur sens avant", $"AC-3 {A(kmGv)}").Avec(kmGv, 3));
            }
            else if (m.DeuxSens)
                note.Info("Deux sens", "Deux enroulements : un contacteur sens avant et un sens arrière par vitesse, verrouillés mécaniquement et électriquement.");
            protection.Ajouter(Element($"KM{n}.1", "Contacteur", "Contacteur petite vitesse", $"AC-3 {A(kmPv)}").Avec(kmPv, 3))
                .Ajouter(Element($"F{n}.1", "Relais thermique", "Relais thermique petite vitesse", $"réglé à {A(ibPv)}").Avec(ibPv));
            if (m.DeuxSens && m.TypeDeuxVitesses == TypeDeuxVitesses.DeuxEnroulements)
            {
                protection.Ajouter(Element($"KM{n}.4", "Contacteur", "Contacteur petite vitesse sens arrière", $"AC-3 {A(kmPv)}").Avec(kmPv, 3));
                protection.Ajouter(Element($"KM{n}.5", "Contacteur", "Contacteur grande vitesse sens arrière", $"AC-3 {A(kmGv)}").Avec(kmGv, 3));
            }
            var gv = protection.Ajouter(Element($"KM{n}.2", "Contacteur", "Contacteur grande vitesse", $"AC-3 {A(kmGv)}").Avec(kmGv, 3));
            if (m.TypeDeuxVitesses == TypeDeuxVitesses.Dahlander)
                gv.Ajouter(Element($"KM{n}.3", "Contacteur", "Contacteur étoile (grande vitesse)", $"AC-3 {A(kmGv)}").Avec(kmGv, 3));
            return gv.Ajouter(Element($"F{n}.2", "Relais thermique", "Relais thermique grande vitesse", $"réglé à {A(ib)}").Avec(ib));
        }
        if (m.Demarrage == ModeDemarrage.EtoileTriangle)
        {
            var iLigne = ib / Math.Sqrt(3);
            var km = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteurs ligne et triangle (AC-3)", iLigne,
                Calibres.Superieur(Calibres.ContacteursAc3, iLigne) ?? iLigne, "A", "Ie ≥ Ib / √3",
                $"Ib = {A(ib)}", RefContacteur);
            var kmEtoile = Calibres.Superieur(Calibres.ContacteursAc3, ib / 3) ?? ib / 3;
            note.Info("Contacteur étoile (AC-3)", $"Ie ≥ Ib / 3 = {A(ib / 3)} → {A(kmEtoile)}", RefContacteur);
            var ligne = protection.Ajouter(Element($"KM{n}.1", "Contacteur", "Contacteur de ligne", $"AC-3 {A(km)}").Avec(km, 3));
            ligne.Ajouter(Element($"KM{n}.2", "Contacteur", "Contacteur étoile", $"AC-3 {A(kmEtoile)}").Avec(kmEtoile, 3));
            ligne.Ajouter(Element($"KM{n}.3", "Contacteur", "Contacteur triangle", $"AC-3 {A(km)}").Avec(km, 3));
            if (m.DeuxSens)
            {
                note.Info("Deux sens", "Contacteur de ligne sens arrière, de même calibre que la ligne, verrouillé mécaniquement et électriquement avec le sens avant.");
                protection.Ajouter(Element($"KM{n}.4", "Contacteur", "Contacteur de ligne sens arrière", $"AC-3 {A(km)}").Avec(km, 3));
            }
            return ligne;
        }

        var calibre = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteur de puissance (AC-3)", ib,
            Calibres.Superieur(Calibres.ContacteursAc3, ib) ?? ib, "A", "Ie (AC-3, 400 V) ≥ Ib", $"Ib = {A(ib)}", RefContacteur);
        if (m.Demarrage == ModeDemarrage.Inverseur)
        {
            var avant = protection.Ajouter(Element($"KM{n}.1", "Contacteur", "Contacteur sens avant", $"AC-3 {A(calibre)}").Avec(calibre, 3));
            protection.Ajouter(Element($"KM{n}.2", "Contacteur", "Contacteur sens arrière", $"AC-3 {A(calibre)}").Avec(calibre, 3));
            note.Info("Inverseur", "Verrouillage mécanique et électrique entre les deux contacteurs.");
            return avant;
        }
        return protection.Ajouter(Element($"KM{n}", "Contacteur", "Contacteur de puissance", $"AC-3 {A(calibre)}").Avec(calibre, 3));
    }

    static ResultatDepart Resistif(DepartResistif r, Reseau reseau, double chuteAmont)
    {
        var note = NouvelleNote(r, "Récepteur");
        var u = Tension(r.Alimentation);
        var calcule = r.PuissanceKw * 1000 / (Racine(r.Alimentation) * u);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", calcule, calcule, "A",
            FormuleCourant(r.Alimentation, "", r.PuissanceKw * 1000, u), $"P = {N(r.PuissanceKw)} kW, U = {N(u)} V");
        var (courantCable, calibre, protection, aval) = Protection(r, note, reseau, ib, CourbeDisjoncteur.B, "charge résistive");
        if (r.AvecContacteur)
        {
            note.Groupe = "Contacteur";
            var km = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteur de puissance (AC-1)", ib,
                Calibres.Superieur(Calibres.ContacteursAc1, ib) ?? ib, "A", "Ie (AC-1) ≥ Ib", $"Ib = {A(ib)}", RefContacteur);
            aval = aval.Ajouter(Element($"KM{r.Numero}", "Contacteur", "Contacteur de puissance", $"AC-1 {A(km)}").Avec(km, 3));
        }
        note.Groupe = "Câble";
        courantCable = CourantCable(note, r, courantCable, ib);
        var cable = CalculCable.Dimensionner(note, r.Cable, r.Alimentation, courantCable, ib, 1, reseau, chuteAmont);
        aval.Ajouter(ElementCable(r.Numero, r, cable, reseau.Slt, 1))
            .Ajouter(Element($"R{r.Numero}", "Récepteur", r.Designation is { Length: > 0 } d ? d : "Résistance",
                $"{N(r.PuissanceKw)} kW").Avec(ib, puissanceKw: r.PuissanceKw));
        return Resultat(r, note, ib, calibre, cable, r.PuissanceKw, 0, protection);
    }

    static ResultatDepart Prise(DepartPrise p, Reseau reseau, double chuteAmont)
    {
        var note = NouvelleNote(p, "Prises");
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", p.CalibrePrise, p.CalibrePrise, "A",
            "Ib = calibre du circuit prises", $"{p.NombrePrises} prise(s) {A(p.CalibrePrise)}");
        note.Verifier("Protection différentielle", p.Differentiel == SensibiliteDifferentiel.Ma30,
            "Les prises de courant jusqu'à 32 A sont protégées par un différentiel 30 mA.", "NF C 15-100, 411.3.3");
        var (courantCable, calibre, protection, aval) = Protection(p, note, reseau, ib, CourbeDisjoncteur.C, "circuit prises");
        note.Groupe = "Câble";
        courantCable = CourantCable(note, p, courantCable, ib);
        var cable = CalculCable.Dimensionner(note, p.Cable, p.Alimentation, courantCable, ib, 1, reseau, chuteAmont);
        var u = Tension(p.Alimentation);
        var puissance = Racine(p.Alimentation) * u * ib / 1000;
        aval.Ajouter(ElementCable(p.Numero, p, cable, reseau.Slt, 1))
            .Ajouter(new ElementMateriel
            {
                Repere = $"XS{p.Numero}", Famille = "Prise de courant",
                Designation = p.Designation is { Length: > 0 } d ? d : "Prise de courant",
                Calibre = A(p.CalibrePrise), Quantite = p.NombrePrises,
            }.Avec(p.CalibrePrise));
        return Resultat(p, note, ib, calibre, cable, puissance, 0, protection);
    }

    static ResultatDepart Generique(DepartGenerique g, Reseau reseau, double chuteAmont)
    {
        var coffret = g as DepartCoffret;
        var note = NouvelleNote(g, coffret is null ? "Récepteur" : "Coffret déporté");
        var u = Tension(g.Alimentation);
        var ks = coffret is null ? 1 : Math.Clamp(coffret.Simultaneite, 0.1, 1);
        var puissance = g.PuissanceKw * ks;
        if (coffret is not null)
            note.Info("Puissance du coffret", $"P installée {N(g.PuissanceKw)} kW × ks interne {N(ks)} = {N(puissance)} kW.",
                "Bilan de puissance du coffret");
        var calcule = puissance * 1000 / (Racine(g.Alimentation) * u * g.CosPhi);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", calcule, calcule, "A",
            FormuleCourant(g.Alimentation, " × cos φ", puissance * 1000, u, g.CosPhi), $"P = {N(puissance)} kW, U = {N(u)} V, cos φ = {N(g.CosPhi)}");
        var (courantCable, calibre, protection, aval) = Protection(g, note, reseau, ib, g.Courbe, "choix utilisateur");
        note.Groupe = "Câble";
        courantCable = CourantCable(note, g, courantCable, ib);
        var cable = CalculCable.Dimensionner(note, g.Cable, g.Alimentation, courantCable, ib, g.CosPhi, reseau, chuteAmont);
        note.Groupe = "Coffret déporté";
        if (coffret is not null)
            note.Info("Coffret déporté", "Le coffret porte son propre organe de sectionnement (NF EN 60204-1, 5.3) ; " +
                "sa sélectivité avec la protection de ce départ et la chute de tension jusqu'à ses récepteurs restent à vérifier.",
                "NF EN 60204-1, 5.3 et 7.2");
        aval.Ajouter(ElementCable(g.Numero, g, cable, reseau.Slt, 1))
            .Ajouter(Element($"{g.PrefixeRecepteur}{g.Numero}", "Récepteur",
                g.Designation is { Length: > 0 } d ? d : coffret is null ? "Charge" : "Coffret déporté",
                $"{N(g.PuissanceKw)} kW").Avec(ib, puissanceKw: puissance));
        var q = puissance * Math.Tan(Math.Acos(g.CosPhi));
        return Resultat(g, note, ib, calibre, cable, puissance, q, protection);
    }

    /// <summary>Facteur de puissance d'une alimentation à découpage avec correction (PFC).</summary>
    const double CosPhiAlimentation = 0.95;

    static ResultatDepart Alimentation24V(DepartAlimentation24V a, Reseau reseau, double chuteAmont, double consommation,
        IReadOnlyList<ConsommationCommande>? postes, FabricantMateriel fabricant, IReadOnlyList<ModeleAlimentation> catalogue)
    {
        var note = NouvelleNote(a, "Alimentation 24 V");
        if (postes is { Count: > 0 })
            foreach (var g in postes.GroupBy(p => p.Poste))
                note.Info($"Raccordés : {g.Key}", string.Join(" ; ", g.Select(p => $"{p.Designation} {A(p.Courant)}")) + $" ; total {A(g.Sum(p => p.Courant))}.",
                    "Bilan 24 V DC du projet");
        var besoin = consommation * (1 + a.ReservePourcent / 100);
        var primaire = CatalogueAlimentations.Primaire(a.Alimentation);
        var marque = CatalogueAlimentations.Marque(a.Marque, fabricant);
        var modele = a.Manuelle
            ? new ModeleAlimentation
            {
                Fabricant = a.FabricantManuel.Length > 0 ? a.FabricantManuel : "Saisie manuelle", Gamme = "saisie manuelle",
                Reference = a.ReferenceManuelle, Courant = a.CourantManuel, Rendement = a.Rendement,
                Largeur = a.LargeurManuelle, Hauteur = a.HauteurManuelle, Profondeur = a.ProfondeurManuelle,
            }
            : CatalogueAlimentations.Choisir(marque, besoin, primaire, catalogue, a.Reglable);
        var proposee = modele?.Courant ?? Calibres.Superieur(Calibres.Alimentations24V, besoin) ?? besoin;
        var assigne = note.Valeur(CodesCalcul.CalibreAlimentation24V, "Courant assigné de l'alimentation 24 V", besoin,
            proposee, "A", "I ≥ consommation 24 V × (1 + réserve)",
            $"consommation {A(consommation)}, réserve {N(a.ReservePourcent)} %", "Bilan 24 V DC du projet");
        // Courant forcé au-dessus du modèle proposé : le modèle suit la valeur forcée.
        if (!a.Manuelle && modele is not null && assigne > modele.Courant + 1e-6)
            modele = CatalogueAlimentations.Choisir(marque, assigne, primaire, catalogue, a.Reglable);
        if (a.Manuelle)
            note.Verifier("Alimentation saisie à la main", modele!.Courant >= besoin - 1e-6,
                $"{modele.Fabricant} {modele.Reference} : {A(modele.Courant)} pour {A(besoin)} nécessaires" +
                (modele.Courant >= besoin - 1e-6 ? $", marge {N(100 * (modele.Courant / Math.Max(besoin, 1e-6) - 1), 0)} %."
                    : " : courant insuffisant, choisir un modèle plus puissant ou répartir la charge sur une autre alimentation."),
                "Bilan 24 V DC du projet");
        else if (modele is null)
            note.Verifier("Alimentation au catalogue", false,
                $"Aucune alimentation {marque} de {A(besoin)} ou plus pour un primaire {CatalogueAlimentations.LibellePrimaire(primaire)} au catalogue : " +
                "ajouter une seconde alimentation (usage réparti), changer de marque ou saisir une alimentation à la main.",
                "Catalogue des alimentations 24 V", StatutCalcul.Alerte);
        else
            note.Verifier("Alimentation retenue", modele.Verifie,
                $"{modele.Fabricant} {modele.Gamme} {modele.Reference} : {A(modele.Courant)}, primaire {CatalogueAlimentations.LibellePrimaire(primaire)}, " +
                $"rendement {N(modele.Rendement * 100, 0)} %, {N(modele.Largeur, 0)} × {N(modele.Hauteur, 0)} × {N(modele.Profondeur, 0)} mm" +
                (modele.Verifie ? "." : " (valeurs indicatives, à vérifier)."), "Catalogue des alimentations 24 V", StatutCalcul.Alerte);

        if (a.Reglable && !a.Manuelle && modele is not null)
            note.Verifier("Sortie réglable", modele.Reglage.Length > 0,
                modele.Reglage.Length > 0
                    ? $"Sortie réglable {modele.Reglage} : remonter la tension compense la chute en ligne des longues liaisons 24 V."
                    : $"Pas d'alimentation {marque} réglable de ce courant et de ce primaire au catalogue : modèle à sortie fixe retenu.",
                "Catalogue des alimentations 24 V", StatutCalcul.Alerte);
        var rendement = modele?.Rendement > 0 ? modele.Rendement : a.Rendement;
        var u = Tension(a.Alimentation);
        var puissance = 24 * assigne / rendement / 1000;
        var calcule = puissance * 1000 / (Racine(a.Alimentation) * u * CosPhiAlimentation);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant absorbé au primaire", calcule, calcule, "A",
            FormuleCourant(a.Alimentation, " × cos φ", puissance * 1000, u, CosPhiAlimentation) + " ; P = 24 V × I_{assigné} / η",
            $"η = {N(rendement)}, cos φ = {N(CosPhiAlimentation)}");
        note.Info("Appel de courant", "Les alimentations à découpage ont un fort courant d'appel : disjoncteur courbe C au minimum, D si plusieurs alimentations.",
            "Documentation fabricant");
        var (courantCable, calibre, protection, aval) = Protection(a, note, reseau, ib, CourbeDisjoncteur.C, "appel des alimentations à découpage");
        note.Groupe = "Câble";
        courantCable = CourantCable(note, a, courantCable, ib);
        var cable = CalculCable.Dimensionner(note, a.Cable, a.Alimentation, courantCable, ib, CosPhiAlimentation, reseau, chuteAmont);
        var alimentation = Element($"G{a.Numero}", "Alimentation", DepartAlimentation24V.Libelle(TensionCommande.Continu24, false, a.Alimentation), $"{N(assigne)} A, {N(24 * assigne)} W")
            .Avec(assigne, puissanceKw: 24 * assigne / 1000);
        if (modele is not null)
        {
            alimentation.Fabricant = modele.Fabricant;
            alimentation.Reference = modele.Reference;
            alimentation.LargeurMm = modele.Largeur;
            alimentation.HauteurMm = modele.Hauteur;
            alimentation.ProfondeurMm = modele.Profondeur;
            alimentation.PertesW = 24 * assigne * (1 / rendement - 1);
        }
        aval.Ajouter(ElementCable(a.Numero, a, cable, reseau.Slt, 1)).Ajouter(alimentation);
        return Resultat(a, note, ib, calibre, cable, puissance, puissance * Math.Tan(Math.Acos(CosPhiAlimentation)), protection);
    }

    /// <summary>Facteur de dimensionnement d'un transformateur suivi d'un redresseur filtré (courant crête des condensateurs).</summary>
    public const double FacteurRedresseur = 1.8;

    /// <summary>
    /// Transformateur de commande 400/24 V ou 230/24 V (sortie alternative, ou continue par redresseur filtré)
    /// et transformateur 400/230 V qui alimente des départs monophasés et les bobines 230 V AC.
    /// </summary>
    static ResultatDepart Transformateur(DepartAlimentation24V t, Reseau reseau, double chuteAmont, double consommation,
        IReadOnlyList<ConsommationCommande>? postes, FabricantMateriel fabricant, IReadOnlyList<ModeleAlimentation> catalogue,
        ChargeAval? aval)
    {
        const string Ref = "NF EN 61558-2-2 ; NF EN 60204-1, 7.2.7 et 9.1.1";
        var note = NouvelleNote(t, "Transformateur");
        var sortie = t.Sortie;
        var usec = sortie == TensionCommande.Alternatif230 ? 230.0 : 24.0;
        if (postes is { Count: > 0 })
            foreach (var g in postes.GroupBy(p => p.Poste))
                note.Info($"Raccordés : {g.Key}", string.Join(" ; ", g.Select(p => $"{p.Designation} {A(p.Courant)}")) + $" ; total {A(g.Sum(p => p.Courant))}.",
                    "Bilan de la commande du projet");
        if (t.Source == SourceAlimentation.Decoupage && sortie != TensionCommande.Continu24)
            note.Verifier("Technologie", false, "Une alimentation à découpage ne fournit que du 24 V DC : une sortie alternative impose un transformateur.", Ref, StatutCalcul.Alerte);
        if (sortie == TensionCommande.Alternatif24 && postes is not null
            && postes.Any(p => p.Poste is ConsommationCommande.Automate or ConsommationCommande.Securite))
            note.Verifier("Automate et sécurité en 24 V AC", false,
                "L'automate et les relais de sécurité demandent du 24 V DC : les raccorder à une alimentation 24 V DC.",
                "Documentation fabricant", StatutCalcul.Alerte);

        var charge = aval ?? new ChargeAval(0, 0);
        if (charge.Departs.Count > 0)
            note.Info("Départs alimentés", string.Join(" ; ", charge.Departs) + $" : {N(charge.S * 1000, 0)} VA (P = {N(charge.P)} kW, Q = {N(charge.Q)} kvar).",
                "Bilan de puissance du projet");
        var facteur = sortie == TensionCommande.Continu24 ? FacteurRedresseur : 1;
        var sCommande = usec * consommation * facteur;
        var sBesoin = (sCommande + charge.S * 1000) * (1 + t.ReservePourcent / 100);
        var detail = $"commande {N(usec, 0)} V × {A(consommation)}" + (facteur > 1 ? $" × {N(facteur)} (redresseur filtré)" : "") +
                     (charge.S > 0 ? $" + départs {N(charge.S * 1000, 0)} VA" : "") + $", réserve {N(t.ReservePourcent)} %";
        note.Info("Puissance nécessaire", $"S = (U × I commande × k + S départs) × (1 + réserve) = {N(sBesoin, 0)} VA ({detail}).", Ref);

        var primaire = CatalogueAlimentations.Primaire(t.Alimentation);
        if (t.Alimentation is TypeAlimentation.Tri400 or TypeAlimentation.Tetra400)
            note.Verifier("Primaire", false, "Un transformateur de commande est monophasé : le raccorder entre deux phases (400 V 2P) ou en 230 V.", Ref, StatutCalcul.Alerte);
        var marque = CatalogueAlimentations.Marque(t.Marque, fabricant);
        var secondaire = CatalogueAlimentations.Secondaire(sortie, true);
        var modele = t.Manuelle
            ? new ModeleAlimentation
            {
                Fabricant = t.FabricantManuel.Length > 0 ? t.FabricantManuel : "Saisie manuelle", Gamme = "saisie manuelle",
                Reference = t.ReferenceManuelle, Va = t.CourantManuel * usec, Courant = t.CourantManuel, Rendement = t.Rendement,
                Type = ModeleAlimentation.Transformateur, Secondaire = secondaire,
                Largeur = t.LargeurManuelle, Hauteur = t.HauteurManuelle, Profondeur = t.ProfondeurManuelle,
            }
            : CatalogueAlimentations.ChoisirTransformateur(marque, secondaire, sBesoin, primaire, catalogue);
        var va = note.Valeur(CodesCalcul.CalibreAlimentation24V, "Puissance assignée du transformateur", sBesoin,
            modele?.Va ?? Calibres.Superieur(Calibres.Transformateurs, sBesoin) ?? sBesoin, "VA", "S assignée ≥ S nécessaire",
            detail, Ref);
        if (!t.Manuelle && modele is not null && va > modele.Va + 1e-6)
            modele = CatalogueAlimentations.ChoisirTransformateur(marque, secondaire, va, primaire, catalogue);
        if (t.Manuelle)
            note.Verifier("Transformateur saisi à la main", modele!.Va >= sBesoin - 1e-6,
                $"{modele.Fabricant} {modele.Reference} : {N(modele.Va, 0)} VA ({A(modele.Courant)} sous {N(usec, 0)} V) pour {N(sBesoin, 0)} VA nécessaires" +
                (modele.Va >= sBesoin - 1e-6 ? "." : " : puissance insuffisante."), Ref);
        else if (modele is null)
            note.Verifier("Transformateur au catalogue", false,
                $"Aucun transformateur {marque} {secondaire} de {N(sBesoin, 0)} VA ou plus pour un primaire {CatalogueAlimentations.LibellePrimaire(primaire)} : " +
                "changer de marque, répartir la charge ou saisir le transformateur à la main.", "Catalogue des alimentations", StatutCalcul.Alerte);
        else
            note.Verifier("Transformateur retenu", modele.Verifie && modele.Reference.Length > 0,
                $"{modele.Fabricant} {modele.Gamme} {(modele.Reference.Length > 0 ? modele.Reference : "(référence à compléter)")} : {N(modele.Va, 0)} VA, " +
                $"{CatalogueAlimentations.LibellePrimaire(primaire)} / {secondaire}, {N(modele.Largeur, 0)} × {N(modele.Hauteur, 0)} × {N(modele.Profondeur, 0)} mm" +
                (modele.Verifie ? "." : " (valeurs indicatives, à vérifier)."), "Catalogue des alimentations", StatutCalcul.Alerte);
        if (sortie == TensionCommande.Continu24)
            note.Info("Redresseur", "Pont redresseur et condensateur de filtrage en aval : secondaire à choisir pour obtenir 24 V DC filtré " +
                "(Udc ≈ 1,35 × Ueff à vide), ondulation ≤ 5 % pour l'électronique.", "NF EN 61131-2");
        if (sortie == TensionCommande.Alternatif24 || sortie == TensionCommande.Alternatif230)
            note.Info("Appel des bobines", "Les bobines AC appellent 6 à 10 fois leur puissance de maintien à la fermeture : " +
                "vérifier la puissance instantanée admissible du transformateur si plusieurs contacteurs ferment ensemble.", "Documentation fabricant");
        note.Info("Circuit de commande", "Un pôle du secondaire relié au conducteur de protection, l'autre protégé ; " +
            "transformateur obligatoire pour plus d'un démarreur ou d'un appareil de commande extérieur.", "NF EN 60204-1, 9.1.1 et 9.4.3");

        var rendement = modele?.Rendement > 0 ? modele.Rendement : t.Rendement;
        var u = Tension(t.Alimentation);
        var calcule = va / (u * rendement);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant absorbé au primaire", calcule, calcule, "A",
            "I = S assignée / (U × η)", $"S = {N(va, 0)} VA, U = {N(u, 0)} V, η = {N(rendement)}");
        note.Info("Appel de courant", "Un transformateur appelle 10 à 25 fois son courant nominal à la mise sous tension : courbe D.",
            "NF C 15-100, 533 ; documentation fabricant");
        var (courantCable, calibre, protection, avalElement) = Protection(t, note, reseau, ib, CourbeDisjoncteur.D, "appel du transformateur");
        var cosPhi = 0.9;
        note.Groupe = "Câble";
        courantCable = CourantCable(note, t, courantCable, ib);
        var cable = CalculCable.Dimensionner(note, t.Cable, t.Alimentation, courantCable, ib, cosPhi, reseau, chuteAmont);
        var transfo = Element($"T{t.Numero}", "Alimentation", DepartAlimentation24V.Libelle(sortie, true, t.Alimentation),
                $"{N(va, 0)} VA, {CatalogueAlimentations.LibellePrimaire(primaire)} / {secondaire}")
            .Avec(va / usec, 2, puissanceKw: va / 1000);
        transfo.PertesW = va * (1 / rendement - 1);
        // Un transformateur n'a pas d'équivalent dans les références d'alimentations 24 V : sans référence au catalogue, elle reste à compléter.
        transfo.ReferenceAuto = false;
        if (modele is not null)
        {
            transfo.Fabricant = modele.Fabricant;
            transfo.Reference = modele.Reference;
            transfo.LargeurMm = modele.Largeur;
            transfo.HauteurMm = modele.Hauteur;
            transfo.ProfondeurMm = modele.Profondeur;
        }
        avalElement = avalElement.Ajouter(ElementCable(t.Numero, t, cable, reseau.Slt, 1)).Ajouter(transfo);
        if (sortie == TensionCommande.Continu24)
            transfo.Ajouter(Element($"V{t.Numero}", "Redresseur", "Pont redresseur + condensateur de filtrage", $"24 V DC, {A(consommation * (1 + t.ReservePourcent / 100))}")
                .Avec(consommation * (1 + t.ReservePourcent / 100), 2));
        // Puissance vue du réseau : celle réellement appelée par la commande et les départs alimentés, augmentée des pertes.
        var p = (usec * consommation * 0.9 / 1000 + charge.P) / rendement;
        var q = usec * consommation * 0.44 / 1000 + charge.Q;
        return Resultat(t, note, ib, calibre, cable, p, q, protection);
    }

    /// <summary>
    /// Protection d'un départ non moteur : disjoncteur ou fusibles gG. Renvoie le courant que le câble doit
    /// supporter (In, ou k3 × In pour les fusibles gG), le calibre, le premier appareil et l'appareil aval.
    /// </summary>
    /// <summary>Coefficient de sécurité électrique du départ : le câble est choisi pour Ib × k, jamais pour moins
    /// que la préconisation (courant de la protection ou du réglage thermique).</summary>
    static double CourantCable(NoteDeCalcul note, Depart d, double preconise, double ib)
    {
        if (d.CoefficientSecurite < 1)
            note.Verifier("Coefficient de sécurité électrique", false,
                $"k = {N(d.CoefficientSecurite)} est sous la préconisation : k = 1 est retenu.", "Choix du bureau d'études", StatutCalcul.Alerte);
        var k = Math.Max(1, d.CoefficientSecurite);
        if (k <= 1) return preconise;
        var ibMajore = ib * k;
        var retenu = Math.Max(preconise, Calibres.Superieur(Calibres.Disjoncteurs, ibMajore) ?? ibMajore);
        note.Info("Coefficient de sécurité électrique", $"Ib × k = {A(ib)} × {N(k)} = {A(ibMajore)} : câble choisi pour {A(retenu)} " +
            $"(préconisation sans coefficient : {A(preconise)}). La protection du départ ne change pas.", "Choix du bureau d'études");
        return retenu;
    }

    static (double CourantCable, double Calibre, ElementMateriel Element, ElementMateriel Aval) Protection(Depart d,
        NoteDeCalcul note, Reseau reseau, double ib, CourbeDisjoncteur courbe, string raisonCourbe)
    {
        note.Groupe = "Protection";
        if (d.Protection == TypeProtection.Fusibles)
        {
            var (calibreFusible, sectionneur, cartouches) = Fusibles(d, note, reseau, ib, ClasseFusible.gG);
            var k3 = Calibres.CoefficientFusibleGg(calibreFusible);
            note.Info("Coefficient k3 des fusibles gG", $"Le câble doit vérifier Iz ≥ {N(k3)} × In = {A(k3 * calibreFusible)}.",
                "NF C 15-100, 433.1");
            Differentiel(d, note, reseau.Slt, "A", articleSepare: true);
            return (k3 * calibreFusible, calibreFusible, sectionneur, cartouches);
        }

        var calibre = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur", ib,
            Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A", "In ≥ Ib, calibre normalisé supérieur",
            $"Ib = {A(ib)}", RefProtection);
        note.Info("Courbe de déclenchement", $"Courbe {courbe} ({raisonCourbe}).", "NF EN 60898-1");
        note.Info("Pôles", Poles(d.Alimentation, reseau.Slt), "NF C 15-100, 431");
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA (Icc à l'arrivée).", RefProtection);
        var sensibilite = Differentiel(d, note, reseau.Slt, "A", articleSepare: false);
        var designation = sensibilite.Length == 0 ? "Disjoncteur" : "Disjoncteur différentiel";
        var element = Element($"Q{d.Numero}", "Disjoncteur", designation,
            $"{A(calibre)} courbe {courbe}, {Poles(d.Alimentation, reseau.Slt)}{sensibilite}")
            .Avec(calibre, NombrePoles(d.Alimentation, reseau.Slt), courbe: courbe.ToString());
        // Le catalogue n'a que des disjoncteurs simples : la référence du disjoncteur différentiel est à saisir.
        if (sensibilite.Length > 0) element.ReferenceAuto = false;
        return (calibre, calibre, element, element);
    }

    /// <summary>
    /// Note de la protection différentielle ; renvoie le complément de libellé (« , 30 mA type A ») ou une chaîne vide.
    /// <paramref name="articleSepare"/> : la protection n'intègre pas le différentiel (disjoncteur moteur, fusibles),
    /// un appareil différentiel séparé est à prévoir.
    /// </summary>
    static string Differentiel(Depart d, NoteDeCalcul note, SchemaLiaisonTerre slt, string type, bool articleSepare)
    {
        if (d.Differentiel == SensibiliteDifferentiel.Aucun) return "";
        note.Groupe = "Protection";
        var sensibilite = d.Differentiel == SensibiliteDifferentiel.Ma30 ? "30 mA" : "300 mA";
        var raison = type switch
        {
            "B" => "variateur triphasé : courants de défaut continus lisses possibles",
            "F" => "variateur monophasé : courants de défaut à fréquences mixtes",
            _ => "type A au minimum pour les charges électroniques courantes",
        };
        note.Info("Protection différentielle", $"{sensibilite} type {type} ({raison}).", "NF C 15-100, 531.3");
        if (slt == SchemaLiaisonTerre.TNC)
            note.Verifier("Différentiel en TN-C", false,
                "Un différentiel ne fonctionne pas sur un conducteur PEN : séparer neutre et PE en amont (TN-C-S) ou retirer le différentiel.",
                "NF C 15-100, 411.4.5", StatutCalcul.Alerte);
        if (articleSepare)
            note.Verifier("Appareil différentiel", false,
                $"La protection choisie n'intègre pas de différentiel : ajouter un interrupteur différentiel {sensibilite} type {type} " +
                "en amont, ou un relais différentiel avec tore. Il n'est pas compté dans le devis.", "NF C 15-100, 531.3", StatutCalcul.Alerte);
        else
            note.Info("Référence du disjoncteur différentiel", "À saisir : le catalogue ne propose que des disjoncteurs simples.");
        return $", {sensibilite} type {type}";
    }

    /// <summary>Nombre de pôles de l'appareil (1P+N compte pour 2 pôles d'encombrement).</summary>
    public static int NombrePoles(TypeAlimentation alimentation, SchemaLiaisonTerre slt) => alimentation switch
    {
        TypeAlimentation.Mono230 or TypeAlimentation.Bi400 => 2,
        TypeAlimentation.Tri400 => 3,
        _ => slt == SchemaLiaisonTerre.TNC ? 3 : 4,
    };

    /// <summary>Nombre de pôles coupés selon la distribution et le schéma de liaison à la terre.</summary>
    public static string Poles(TypeAlimentation alimentation, SchemaLiaisonTerre slt) => alimentation switch
    {
        TypeAlimentation.Mono230 => slt == SchemaLiaisonTerre.IT ? "2P (neutre protégé)" : "1P+N",
        TypeAlimentation.Bi400 => "2P",
        TypeAlimentation.Tri400 => "3P",
        _ => slt switch
        {
            SchemaLiaisonTerre.TNC => "3P (PEN jamais coupé)",
            SchemaLiaisonTerre.IT => "4P (neutre protégé)",
            _ => "3P+N",
        },
    };

    static ElementMateriel Element(string repere, string famille, string designation, string calibre) =>
        new() { Repere = repere, Famille = famille, Designation = designation, Calibre = calibre };

    /// <summary>Nombre de conducteurs du câble, PE compris : en TN-C au-delà de 10 mm², le PEN remplace neutre et PE.</summary>
    public static int Conducteurs(TypeAlimentation alimentation, SchemaLiaisonTerre slt, double section)
    {
        var pen = slt == SchemaLiaisonTerre.TNC && CalculCable.Pen(section) ? 1 : 0;
        return alimentation switch
        {
            TypeAlimentation.Mono230 => 3 - pen,
            TypeAlimentation.Bi400 => 3,
            TypeAlimentation.Tri400 => 4,
            _ => 5 - pen,
        };
    }

    static ElementMateriel ElementCable(int numero, Depart d, ResultatCable cable, SchemaLiaisonTerre slt, int quantite)
    {
        var conducteurs = Conducteurs(d.Alimentation, slt, cable.Section);
        return new ElementMateriel
        {
            Repere = $"W{numero}", Famille = "Câble", Designation = $"Câble cuivre {d.Cable.Isolant}",
            Calibre = $"{conducteurs}G{N(cable.Section)} mm², {N(d.Cable.Longueur)} m", Quantite = quantite,
        };
    }

    static ResultatDepart Resultat(Depart d, NoteDeCalcul note, double ib, double calibre, ResultatCable cable,
        double p, double q, ElementMateriel protection)
    {
        foreach (var element in protection.Parcourir())
        {
            element.Statut = note.StatutGlobal;
            element.DepartId = d.Id;
        }
        return new ResultatDepart
        {
            Depart = d, Note = note, CourantEmploi = ib, CalibreProtection = calibre,
            Section = cable.Section, ChuteTension = cable.ChuteTension, Cable = cable,
            PuissanceActive = p, PuissanceReactive = q, Materiel = protection,
        };
    }
}
