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

/// <summary>Dimensionne un départ : courant d'emploi, protection, contacteur(s), câble.</summary>
public static class CalculDepart
{
    const string RefProtection = "NF C 15-100, 433 ; NF EN 60947-2";
    const string RefContacteur = "NF EN 60947-4-1, catégories d'emploi";

    public static ResultatDepart Calculer(Depart depart, Reseau reseau, double chuteAmont = 0) => depart switch
    {
        DepartMoteur m => Moteur(m, reseau, chuteAmont),
        DepartResistif r => Resistif(r, reseau, chuteAmont),
        DepartPrise p => Prise(p, reseau, chuteAmont),
        DepartGenerique g => Generique(g, reseau, chuteAmont),
        _ => throw new NotSupportedException(depart.GetType().Name),
    };

    static double Tension(TypeAlimentation a) => a == TypeAlimentation.Mono230 ? 230 : 400;
    static double Racine(TypeAlimentation a) => a == TypeAlimentation.Mono230 ? 1 : Math.Sqrt(3);
    static string FormuleCourant(TypeAlimentation a, string suffixe) =>
        a == TypeAlimentation.Mono230 ? $"I = P / (U{suffixe})" : $"I = P / (√3 × U{suffixe})";

    static NoteDeCalcul NouvelleNote(Depart d) => new($"D{d.Numero:00}", d.Forcages);

    static ResultatDepart Moteur(DepartMoteur m, Reseau reseau, double chuteAmont)
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

        if (m.Demarrage == ModeDemarrage.Variateur)
        {
            calibreProtection = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur amont du variateur",
                ib, Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A", "In ≥ Ib, calibre normalisé supérieur",
                $"Ib = {A(ib)}", RefProtection);
            note.Info("Coordination variateur",
                "Valider la protection amont (disjoncteur ou fusibles gR/aR) dans le tableau de coordination du fabricant du variateur.");
            note.Info("Variateur", $"Courant de sortie assigné ≥ {A(ib)} en service normal ; vérifier la surcharge nécessaire au process.");
            note.Info("CEM", "Câble moteur blindé, blindage repris à 360° aux deux extrémités, filtre CEM côté réseau.",
                "Directive 2014/30/UE ; NF EN 61800-3");
            protection = Element($"Q{n}", "Disjoncteur", "Disjoncteur amont variateur",
                $"{A(calibreProtection)}, {Poles(m.Alimentation, reseau.Slt)}");
            avalProtection = protection.Ajouter(Element($"U{n}", "Variateur", "Variateur de fréquence",
                $"{N(m.PuissanceKw)} kW, sortie ≥ {A(ib)}"));
        }
        else
        {
            var plage = Calibres.PlageMoteur(ib);
            if (plage is { } p)
            {
                calibreProtection = note.Valeur(CodesCalcul.ReglageThermique, "Réglage thermique du disjoncteur moteur",
                    ib, ib, "A", "Ir = Ib", $"plage {N(p.Min)}–{N(p.Max)} A", "NF EN 60947-4-1");
                var im = Calibres.SeuilMagnetiqueDisjoncteurMoteur * p.Max;
                var crete = 2 * m.RapportId * ib;
                note.Verifier("Tenue au démarrage", im >= crete,
                    $"Seuil magnétique ≈ {A(im)} ; pointe de démarrage estimée 2 × Id = {A(crete)}",
                    "NF EN 60947-4-1", StatutCalcul.Alerte);
                protection = Element($"Q{n}", "Disjoncteur moteur", "Disjoncteur moteur magnétothermique",
                    $"{N(p.Min)}–{N(p.Max)} A, réglé à {A(calibreProtection)}");
            }
            else
            {
                calibreProtection = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur courbe D",
                    ib, Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A",
                    "In ≥ Ib ; au-delà des disjoncteurs moteurs, disjoncteur courbe D et relais thermique",
                    $"Ib = {A(ib)}", RefProtection);
                protection = Element($"Q{n}", "Disjoncteur", "Disjoncteur courbe D",
                    $"{A(calibreProtection)}, {Poles(m.Alimentation, reseau.Slt)}");
                protection.Ajouter(Element($"F{n}", "Relais thermique", "Relais de protection thermique", $"réglé à {A(ib)}"));
            }
            avalProtection = Contacteurs(m, note, ib, protection);
        }
        Differentiel(m, note, m.Demarrage == ModeDemarrage.Variateur
            ? (m.Alimentation == TypeAlimentation.Mono230 ? "F" : "B")
            : "A");
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA (Icc à l'arrivée).", RefProtection);

        // En étoile-triangle, deux câbles alimentent les enroulements, chacun parcouru par I / √3.
        var et = m.Demarrage == ModeDemarrage.EtoileTriangle;
        var cable = CalculCable.Dimensionner(note, m.Cable, m.Alimentation,
            et ? calibreProtection / Math.Sqrt(3) : calibreProtection, et ? ib / Math.Sqrt(3) : ib,
            m.CosPhi, reseau, chuteAmont);
        var w = avalProtection.Ajouter(ElementCable(n, m, cable, et ? 2 : 1));
        w.Ajouter(Element($"M{n}", "Moteur", m.Designation is { Length: > 0 } d ? d : "Moteur",
            $"{N(m.PuissanceKw)} kW, In {A(ib)}"));

        return Resultat(m, note, ib, calibreProtection, cable, pAbsorbee, q, protection);
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
            var ligne = protection.Ajouter(Element($"KM{n}.1", "Contacteur", "Contacteur de ligne", $"AC-3 {A(km)}"));
            ligne.Ajouter(Element($"KM{n}.2", "Contacteur", "Contacteur étoile", $"AC-3 {A(kmEtoile)}"));
            ligne.Ajouter(Element($"KM{n}.3", "Contacteur", "Contacteur triangle", $"AC-3 {A(km)}"));
            return ligne;
        }

        var calibre = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteur de puissance (AC-3)", ib,
            Calibres.Superieur(Calibres.ContacteursAc3, ib) ?? ib, "A", "Ie (AC-3, 400 V) ≥ Ib", $"Ib = {A(ib)}", RefContacteur);
        if (m.Demarrage == ModeDemarrage.Inverseur)
        {
            var avant = protection.Ajouter(Element($"KM{n}.1", "Contacteur", "Contacteur sens avant", $"AC-3 {A(calibre)}"));
            protection.Ajouter(Element($"KM{n}.2", "Contacteur", "Contacteur sens arrière", $"AC-3 {A(calibre)}"));
            note.Info("Inverseur", "Verrouillage mécanique et électrique entre les deux contacteurs.");
            return avant;
        }
        return protection.Ajouter(Element($"KM{n}", "Contacteur", "Contacteur de puissance", $"AC-3 {A(calibre)}"));
    }

    static ResultatDepart Resistif(DepartResistif r, Reseau reseau, double chuteAmont)
    {
        var note = NouvelleNote(r);
        var u = Tension(r.Alimentation);
        var calcule = r.PuissanceKw * 1000 / (Racine(r.Alimentation) * u);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", calcule, calcule, "A",
            FormuleCourant(r.Alimentation, ""), $"P = {N(r.PuissanceKw)} kW, U = {N(u)} V");
        var (calibre, protection) = Disjoncteur(r, note, reseau, ib, CourbeDisjoncteur.B, "charge résistive");
        var aval = protection;
        if (r.AvecContacteur)
        {
            var km = note.Valeur(CodesCalcul.CalibreContacteur, "Contacteur de puissance (AC-1)", ib,
                Calibres.Superieur(Calibres.ContacteursAc1, ib) ?? ib, "A", "Ie (AC-1) ≥ Ib", $"Ib = {A(ib)}", RefContacteur);
            aval = protection.Ajouter(Element($"KM{r.Numero}", "Contacteur", "Contacteur de puissance", $"AC-1 {A(km)}"));
        }
        var cable = CalculCable.Dimensionner(note, r.Cable, r.Alimentation, calibre, ib, 1, reseau, chuteAmont);
        aval.Ajouter(ElementCable(r.Numero, r, cable, 1))
            .Ajouter(Element($"R{r.Numero}", "Récepteur", r.Designation is { Length: > 0 } d ? d : "Résistance",
                $"{N(r.PuissanceKw)} kW"));
        return Resultat(r, note, ib, calibre, cable, r.PuissanceKw, 0, protection);
    }

    static ResultatDepart Prise(DepartPrise p, Reseau reseau, double chuteAmont)
    {
        var note = NouvelleNote(p);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", p.CalibrePrise, p.CalibrePrise, "A",
            "Ib = calibre du circuit prises", $"{p.NombrePrises} prise(s) {A(p.CalibrePrise)}");
        note.Verifier("Protection différentielle", p.Differentiel == SensibiliteDifferentiel.Ma30,
            "Les prises de courant jusqu'à 32 A sont protégées par un différentiel 30 mA.", "NF C 15-100, 411.3.3");
        var (calibre, protection) = Disjoncteur(p, note, reseau, ib, CourbeDisjoncteur.C, "circuit prises");
        var cable = CalculCable.Dimensionner(note, p.Cable, p.Alimentation, calibre, ib, 1, reseau, chuteAmont);
        var u = Tension(p.Alimentation);
        var puissance = Racine(p.Alimentation) * u * ib / 1000;
        protection.Ajouter(ElementCable(p.Numero, p, cable, 1))
            .Ajouter(new ElementMateriel
            {
                Repere = $"XS{p.Numero}", Famille = "Prise de courant",
                Designation = p.Designation is { Length: > 0 } d ? d : "Prise de courant",
                Calibre = A(p.CalibrePrise), Quantite = p.NombrePrises,
            });
        return Resultat(p, note, ib, calibre, cable, puissance, 0, protection);
    }

    static ResultatDepart Generique(DepartGenerique g, Reseau reseau, double chuteAmont)
    {
        var note = NouvelleNote(g);
        var u = Tension(g.Alimentation);
        var calcule = g.PuissanceKw * 1000 / (Racine(g.Alimentation) * u * g.CosPhi);
        var ib = note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi", calcule, calcule, "A",
            FormuleCourant(g.Alimentation, " × cos φ"), $"P = {N(g.PuissanceKw)} kW, U = {N(u)} V, cos φ = {N(g.CosPhi)}");
        var (calibre, protection) = Disjoncteur(g, note, reseau, ib, g.Courbe, "choix utilisateur");
        var cable = CalculCable.Dimensionner(note, g.Cable, g.Alimentation, calibre, ib, g.CosPhi, reseau, chuteAmont);
        protection.Ajouter(ElementCable(g.Numero, g, cable, 1))
            .Ajouter(Element($"E{g.Numero}", "Récepteur", g.Designation is { Length: > 0 } d ? d : "Charge",
                $"{N(g.PuissanceKw)} kW"));
        var q = g.PuissanceKw * Math.Tan(Math.Acos(g.CosPhi));
        return Resultat(g, note, ib, calibre, cable, g.PuissanceKw, q, protection);
    }

    static (double Calibre, ElementMateriel Element) Disjoncteur(Depart d, NoteDeCalcul note, Reseau reseau, double ib,
        CourbeDisjoncteur courbe, string raisonCourbe)
    {
        var calibre = note.Valeur(CodesCalcul.CalibreProtection, "Calibre du disjoncteur", ib,
            Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A", "In ≥ Ib, calibre normalisé supérieur",
            $"Ib = {A(ib)}", RefProtection);
        note.Info("Courbe de déclenchement", $"Courbe {courbe} ({raisonCourbe}).", "NF EN 60898-1");
        note.Info("Pôles", Poles(d.Alimentation, reseau.Slt), "NF C 15-100, 431");
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA (Icc à l'arrivée).", RefProtection);
        Differentiel(d, note, "A");
        var designation = d.Differentiel == SensibiliteDifferentiel.Aucun ? "Disjoncteur" : "Disjoncteur différentiel";
        return (calibre, Element($"Q{d.Numero}", "Disjoncteur", designation,
            $"{A(calibre)} courbe {courbe}, {Poles(d.Alimentation, reseau.Slt)}"));
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

    /// <summary>Nombre de pôles coupés selon la distribution et le schéma de liaison à la terre.</summary>
    public static string Poles(TypeAlimentation alimentation, SchemaLiaisonTerre slt) => alimentation switch
    {
        TypeAlimentation.Mono230 => slt == SchemaLiaisonTerre.IT ? "2P (neutre protégé)" : "1P+N",
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
            TypeAlimentation.Mono230 => 3,
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
