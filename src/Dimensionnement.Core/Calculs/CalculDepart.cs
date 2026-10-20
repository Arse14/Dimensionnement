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
    static string FormuleCourant(TypeAlimentation a, string suffixe) =>
        a is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? $"I = P / (U{suffixe})" : $"I = P / (√3 × U{suffixe})";

    static NoteDeCalcul NouvelleNote(Depart d) => new($"D{d.Numero:00}", d.Forcages);

    static ResultatDepart Moteur(DepartMoteur m, Reseau reseau, double chuteAmont, FabricantMateriel fabricant, IReadOnlyList<ModeleVariateur> variateurs)
    {
        var note = NouvelleNote(m);
        var u = Tension(m.Alimentation);
        var inCalcule = m.PuissanceKw * 1000 / (Racine(m.Alimentation) * u * m.CosPhi * m.Rendement);
        var entrees = $"Pn = {N(m.PuissanceKw)} kW, U = {N(u)} V, cos φ = {N(m.CosPhi)}, η = {N(m.Rendement)}";
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi du moteur", inCalcule,
            m.CourantPlaque > 0 ? m.CourantPlaque : inCalcule, "A",
            FormuleCourant(m.Alimentation, " × cos φ × η") + (m.CourantPlaque > 0 ? " ; valeur de plaque retenue" : ""),
            entrees, "Plaque signalétique, NF EN 60034-1");
        if (m.CourantPlaque > 0)
        {
            var ecart = Math.Abs(m.CourantPlaque - inCalcule) / m.CourantPlaque;
            note.Verifier("Cohérence avec la plaque", ecart <= 0.10,
                $"In plaque = {A(m.CourantPlaque)}, In recalculé = {A(inCalcule)}, écart {Pct(ecart * 100)}",
                siEchec: StatutCalcul.Alerte);
        }

        var pAbsorbee = m.PuissanceKw / m.Rendement;
        var q = pAbsorbee * Math.Tan(Math.Acos(m.CosPhi));
        var n = m.Numero;
        double calibreProtection;
        ElementMateriel protection;
        ElementMateriel avalProtection;

        double courantCable;
        if (m.Demarrage == ModeDemarrage.Variateur)
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
            note.Info("Coordination variateur",
                "Valider la protection amont (disjoncteur ou fusibles gR/aR) dans le tableau de coordination du fabricant du variateur.");
            note.Info("Variateur", $"Courant de sortie assigné ≥ {A(ib)} en service normal ; vérifier la surcharge nécessaire au process.");
            note.Info("CEM", "Câble moteur blindé, blindage repris à 360° aux deux extrémités, filtre CEM côté réseau.",
                "Directive 2014/30/UE ; NF EN 61800-3");
            var marque = CatalogueVariateurs.Marque(m.MarqueVariateur, fabricant);
            var modele = CatalogueVariateurs.Choisir(marque, m.PuissanceKw, ib, variateurs);
            var variateur = Element($"U{n}", "Variateur", "Variateur de fréquence",
                modele is null ? $"{N(m.PuissanceKw)} kW, sortie ≥ {A(ib)}" : $"{N(modele.Kw)} kW, {A(modele.Courant)}, {modele.Taille}")
                .Avec(modele?.Courant ?? ib, puissanceKw: m.PuissanceKw);
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
                    ? $"Aucun variateur {marque} du catalogue ne couvre {N(m.PuissanceKw)} kW et {A(ib)} : choisir une autre marque ou compléter le catalogue."
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
            var reglage = note.Valeur(CodesCalcul.ReglageThermique, "Réglage du relais thermique", ib, ib, "A", "Ir = Ib",
                $"Ib = {A(ib)}", "NF EN 60947-4-1");
            courantCable = reglage;
            note.Info("Coordination", "Vérifier la coordination type 2 fusibles / contacteur / relais dans les tables du fabricant.",
                "NF EN 60947-4-1");
            avalProtection = progressif
                ? DemarreurProgressif(m, note, ib, avalProtection)
                : Contacteurs(m, note, ib, avalProtection);
            avalProtection = avalProtection.Ajouter(Element($"F{n}", "Relais thermique", "Relais de protection thermique",
                $"réglé à {A(reglage)}").Avec(reglage));
        }
        else
        {
            var plage = Calibres.PlageMoteur(ib);
            if (plage is { } p)
            {
                calibreProtection = note.Valeur(CodesCalcul.ReglageThermique, "Réglage thermique du disjoncteur moteur",
                    ib, ib, "A", "Ir = Ib", $"plage {N(p.Min)}–{N(p.Max)} A", "NF EN 60947-4-1");
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
                ? DemarreurProgressif(m, note, ib, avalProtection)
                : Contacteurs(m, note, ib, avalProtection);
        }
        Differentiel(m, note, m.Demarrage == ModeDemarrage.Variateur
            ? (m.Alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? "F" : "B")
            : "A");
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA (Icc à l'arrivée).", RefProtection);

        // En étoile-triangle, deux câbles alimentent les enroulements, chacun parcouru par I / √3.
        var et = m.Demarrage == ModeDemarrage.EtoileTriangle;
        var cable = CalculCable.Dimensionner(note, m.Cable, m.Alimentation,
            et ? courantCable / Math.Sqrt(3) : courantCable, et ? ib / Math.Sqrt(3) : ib,
            m.CosPhi, reseau, chuteAmont);
        var w = avalProtection.Ajouter(ElementCable(n, m, cable, et ? 2 : 1));
        w.Ajouter(Element($"M{n}", "Moteur", m.Designation is { Length: > 0 } d ? d : "Moteur",
            $"{N(m.PuissanceKw)} kW, In {A(ib)}").Avec(ib, m.Alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400 ? 2 : 3, m.PuissanceKw));

        return Resultat(m, note, ib, calibreProtection, cable, pAbsorbee, q, protection);
    }

    static ElementMateriel DemarreurProgressif(DepartMoteur m, NoteDeCalcul note, double ib, ElementMateriel amont)
    {
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
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure des cartouches ≥ {N(reseau.IccKiloAmperes)} kA (en général 100 kA ou plus).",
            "NF EN 60269");
        var sectionneur = Element($"Q{d.Numero}", "Sectionneur porte-fusibles", $"Sectionneur porte-fusibles {taille}", poles)
            .Avec(calibre, NombrePoles(d.Alimentation, reseau.Slt), tailleFusible: taille);
        var fusibles = sectionneur.Ajouter(new ElementMateriel
        {
            Repere = $"FU{d.Numero}", Famille = "Fusible", Designation = $"Cartouche fusible {classe} {taille}",
            Calibre = A(calibre), Quantite = cartouches,
        }.Avec(calibre, tailleFusible: taille));
        return (calibre, sectionneur, fusibles);
    }

    static ElementMateriel Contacteurs(DepartMoteur m, NoteDeCalcul note, double ib, ElementMateriel protection)
    {
        var n = m.Numero;
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
        var note = NouvelleNote(r);
        var u = Tension(r.Alimentation);
        var calcule = r.PuissanceKw * 1000 / (Racine(r.Alimentation) * u);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", calcule, calcule, "A",
            FormuleCourant(r.Alimentation, ""), $"P = {N(r.PuissanceKw)} kW, U = {N(u)} V");
        var (courantCable, calibre, protection, aval) = Protection(r, note, reseau, ib, CourbeDisjoncteur.B, "charge résistive");
        if (r.AvecContacteur)
        {
            var km = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteur de puissance (AC-1)", ib,
                Calibres.Superieur(Calibres.ContacteursAc1, ib) ?? ib, "A", "Ie (AC-1) ≥ Ib", $"Ib = {A(ib)}", RefContacteur);
            aval = aval.Ajouter(Element($"KM{r.Numero}", "Contacteur", "Contacteur de puissance", $"AC-1 {A(km)}").Avec(km, 3));
        }
        var cable = CalculCable.Dimensionner(note, r.Cable, r.Alimentation, courantCable, ib, 1, reseau, chuteAmont);
        aval.Ajouter(ElementCable(r.Numero, r, cable, 1))
            .Ajouter(Element($"R{r.Numero}", "Récepteur", r.Designation is { Length: > 0 } d ? d : "Résistance",
                $"{N(r.PuissanceKw)} kW").Avec(ib, puissanceKw: r.PuissanceKw));
        return Resultat(r, note, ib, calibre, cable, r.PuissanceKw, 0, protection);
    }

    static ResultatDepart Prise(DepartPrise p, Reseau reseau, double chuteAmont)
    {
        var note = NouvelleNote(p);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", p.CalibrePrise, p.CalibrePrise, "A",
            "Ib = calibre du circuit prises", $"{p.NombrePrises} prise(s) {A(p.CalibrePrise)}");
        note.Verifier("Protection différentielle", p.Differentiel == SensibiliteDifferentiel.Ma30,
            "Les prises de courant jusqu'à 32 A sont protégées par un différentiel 30 mA.", "NF C 15-100, 411.3.3");
        var (courantCable, calibre, protection, aval) = Protection(p, note, reseau, ib, CourbeDisjoncteur.C, "circuit prises");
        var cable = CalculCable.Dimensionner(note, p.Cable, p.Alimentation, courantCable, ib, 1, reseau, chuteAmont);
        var u = Tension(p.Alimentation);
        var puissance = Racine(p.Alimentation) * u * ib / 1000;
        aval.Ajouter(ElementCable(p.Numero, p, cable, 1))
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
        var note = NouvelleNote(g);
        var u = Tension(g.Alimentation);
        var calcule = g.PuissanceKw * 1000 / (Racine(g.Alimentation) * u * g.CosPhi);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", calcule, calcule, "A",
            FormuleCourant(g.Alimentation, " × cos φ"), $"P = {N(g.PuissanceKw)} kW, U = {N(u)} V, cos φ = {N(g.CosPhi)}");
        var (courantCable, calibre, protection, aval) = Protection(g, note, reseau, ib, g.Courbe, "choix utilisateur");
        var cable = CalculCable.Dimensionner(note, g.Cable, g.Alimentation, courantCable, ib, g.CosPhi, reseau, chuteAmont);
        aval.Ajouter(ElementCable(g.Numero, g, cable, 1))
            .Ajouter(Element($"E{g.Numero}", "Récepteur", g.Designation is { Length: > 0 } d ? d : "Charge",
                $"{N(g.PuissanceKw)} kW").Avec(ib, puissanceKw: g.PuissanceKw));
        var q = g.PuissanceKw * Math.Tan(Math.Acos(g.CosPhi));
        return Resultat(g, note, ib, calibre, cable, g.PuissanceKw, q, protection);
    }

    /// <summary>Facteur de puissance d'une alimentation à découpage avec correction (PFC).</summary>
    const double CosPhiAlimentation = 0.95;

    static ResultatDepart Alimentation24V(DepartAlimentation24V a, Reseau reseau, double chuteAmont, double consommation,
        IReadOnlyList<ConsommationCommande>? postes, FabricantMateriel fabricant, IReadOnlyList<ModeleAlimentation> catalogue)
    {
        var note = NouvelleNote(a);
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
            : CatalogueAlimentations.Choisir(marque, besoin, primaire, catalogue);
        var proposee = modele?.Courant ?? Calibres.Superieur(Calibres.Alimentations24V, besoin) ?? besoin;
        var assigne = note.Valeur(CodesCalcul.CalibreAlimentation24V, "Courant assigné de l'alimentation 24 V", besoin,
            proposee, "A", "I ≥ consommation 24 V × (1 + réserve)",
            $"consommation {A(consommation)}, réserve {N(a.ReservePourcent)} %", "Bilan 24 V DC du projet");
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

        var rendement = modele?.Rendement > 0 ? modele.Rendement : a.Rendement;
        var u = Tension(a.Alimentation);
        var puissance = 24 * assigne / rendement / 1000;
        var calcule = puissance * 1000 / (Racine(a.Alimentation) * u * CosPhiAlimentation);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant absorbé au primaire", calcule, calcule, "A",
            FormuleCourant(a.Alimentation, " × cos φ") + ", P = 24 V × I assigné / η",
            $"η = {N(rendement)}, cos φ = {N(CosPhiAlimentation)}");
        note.Info("Appel de courant", "Les alimentations à découpage ont un fort courant d'appel : disjoncteur courbe C au minimum, D si plusieurs alimentations.",
            "Documentation fabricant");
        var (courantCable, calibre, protection, aval) = Protection(a, note, reseau, ib, CourbeDisjoncteur.C, "appel des alimentations à découpage");
        var cable = CalculCable.Dimensionner(note, a.Cable, a.Alimentation, courantCable, ib, CosPhiAlimentation, reseau, chuteAmont);
        var alimentation = Element($"G{a.Numero}", "Alimentation", "Alimentation 24 V DC", $"{N(assigne)} A, {N(24 * assigne)} W")
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
        aval.Ajouter(ElementCable(a.Numero, a, cable, 1)).Ajouter(alimentation);
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
        var note = NouvelleNote(t);
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
        var cable = CalculCable.Dimensionner(note, t.Cable, t.Alimentation, courantCable, ib, cosPhi, reseau, chuteAmont);
        var transfo = Element($"T{t.Numero}", "Alimentation", DepartAlimentation24V.Libelle(sortie, true, t.Alimentation),
                $"{N(va, 0)} VA, {CatalogueAlimentations.LibellePrimaire(primaire)} / {secondaire}")
            .Avec(va / usec, 2, puissanceKw: va / 1000);
        transfo.PertesW = va * (1 / rendement - 1);
        if (modele is not null)
        {
            transfo.Fabricant = modele.Fabricant;
            transfo.Reference = modele.Reference;
            transfo.LargeurMm = modele.Largeur;
            transfo.HauteurMm = modele.Hauteur;
            transfo.ProfondeurMm = modele.Profondeur;
        }
        avalElement = avalElement.Ajouter(ElementCable(t.Numero, t, cable, 1)).Ajouter(transfo);
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
    static (double CourantCable, double Calibre, ElementMateriel Element, ElementMateriel Aval) Protection(Depart d,
        NoteDeCalcul note, Reseau reseau, double ib, CourbeDisjoncteur courbe, string raisonCourbe)
    {
        if (d.Protection == TypeProtection.Fusibles)
        {
            var (calibreFusible, sectionneur, cartouches) = Fusibles(d, note, reseau, ib, ClasseFusible.gG);
            var k3 = Calibres.CoefficientFusibleGg(calibreFusible);
            note.Info("Coefficient k3 des fusibles gG", $"Le câble doit vérifier Iz ≥ {N(k3)} × In = {A(k3 * calibreFusible)}.",
                "NF C 15-100, 433.1");
            Differentiel(d, note, "A");
            return (k3 * calibreFusible, calibreFusible, sectionneur, cartouches);
        }

        var calibre = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur", ib,
            Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A", "In ≥ Ib, calibre normalisé supérieur",
            $"Ib = {A(ib)}", RefProtection);
        note.Info("Courbe de déclenchement", $"Courbe {courbe} ({raisonCourbe}).", "NF EN 60898-1");
        note.Info("Pôles", Poles(d.Alimentation, reseau.Slt), "NF C 15-100, 431");
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA (Icc à l'arrivée).", RefProtection);
        Differentiel(d, note, "A");
        var designation = d.Differentiel == SensibiliteDifferentiel.Aucun ? "Disjoncteur" : "Disjoncteur différentiel";
        var element = Element($"Q{d.Numero}", "Disjoncteur", designation,
            $"{A(calibre)} courbe {courbe}, {Poles(d.Alimentation, reseau.Slt)}")
            .Avec(calibre, NombrePoles(d.Alimentation, reseau.Slt), courbe: courbe.ToString());
        return (calibre, calibre, element, element);
    }

    static void Differentiel(Depart d, NoteDeCalcul note, string type)
    {
        if (d.Differentiel == SensibiliteDifferentiel.Aucun) return;
        var sensibilite = d.Differentiel == SensibiliteDifferentiel.Ma30 ? "30 mA" : "300 mA";
        var raison = type switch
        {
            "B" => "variateur triphasé : courants de défaut continus lisses possibles",
            "F" => "variateur monophasé : courants de défaut à fréquences mixtes",
            _ => "type A au minimum pour les charges électroniques courantes",
        };
        note.Info("Protection différentielle", $"{sensibilite} type {type} ({raison}).", "NF C 15-100, 531.3");
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

    static ElementMateriel ElementCable(int numero, Depart d, ResultatCable cable, int quantite)
    {
        var conducteurs = d.Alimentation switch
        {
            TypeAlimentation.Mono230 or TypeAlimentation.Bi400 => 3,
            TypeAlimentation.Tri400 => 4,
            _ => 5,
        };
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
            Section = cable.Section, ChuteTension = cable.ChuteTension,
            PuissanceActive = p, PuissanceReactive = q, Materiel = protection,
        };
    }
}
