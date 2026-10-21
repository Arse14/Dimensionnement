using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

/// <summary>Niveau de sélectivité entre le général et un départ.</summary>
public enum NiveauSelectivite { Totale, Partielle, AVerifier, Absente }

/// <summary>Sélectivité entre la protection de tête et celle d'un départ.</summary>
public record LigneSelectivite(string Depart, string Amont, string Aval, double RapportSurcharge, double SeuilAmont,
    double SeuilAval, double Limite, NiveauSelectivite Niveau, string Commentaire);

public class ResultatSelectivite
{
    public required NoteDeCalcul Note { get; init; }
    public IReadOnlyList<LigneSelectivite> Lignes { get; init; } = Array.Empty<LigneSelectivite>();

    /// <summary>
    /// Vrai quand un départ approche de la limite : sélectivité absente, à vérifier ou seulement partielle,
    /// ou rapport des calibres sous <see cref="CalculSelectivite.RapportVigilance"/>. Sinon la section reste masquée.
    /// </summary>
    public bool AttentionRequise => Lignes.Any(l => l.Niveau != NiveauSelectivite.Totale || l.RapportSurcharge < CalculSelectivite.RapportVigilance);
}

/// <summary>
/// Sélectivité ampèremétrique entre le disjoncteur général et les protections des départs : rapport des seuils de
/// surcharge (≥ 1,6) et des seuils magnétiques, comparés au courant de court-circuit du jeu de barres.
/// Les tables de sélectivité du fabricant permettent souvent mieux (sélectivité énergétique) : le résultat est un minimum.
/// </summary>
public static class CalculSelectivite
{
    const string Ref = "NF C 15-100, 535 ; NF EN 60947-2, annexe A ; tables de sélectivité des fabricants";

    /// <summary>Rapport minimal des seuils de surcharge amont / aval.</summary>
    public const double RapportSurchargeMini = 1.6;

    /// <summary>Rapport des calibres sous lequel la sélectivité est signalée, avant d'atteindre le minimum.</summary>
    public const double RapportVigilance = 2;

    public static ResultatSelectivite Calculer(ResultatProjet r)
    {
        var note = new NoteDeCalcul("SEL");
        var icc = r.Projet.Reseau.IccKiloAmperes * 1000;
        var qg = r.CalibreGeneral;
        // Disjoncteur de tête : courbe C jusqu'à 63 A (magnétique 5 à 10 In), au-delà boîtier moulé (≈ 10 In ± 20 %).
        var imAmont = qg <= 63 ? 5 * qg : 8 * qg;
        note.Info("Disjoncteur général", $"QG {A(qg)}, seuil magnétique minimal ≈ {A(imAmont)} ; Icc au jeu de barres ≈ {N(icc / 1000)} kA.", Ref);

        var lignes = new List<LigneSelectivite>();
        var alimentesPar = SourcesCommande.AlimentesPar(r.Projet);
        foreach (var d in r.Departs)
        {
            var protection = d.Materiel;
            var nom = $"{d.Depart.Code}";
            // Départ derrière un transformateur 400/230 V : l'amont est la protection du primaire, ramenée au secondaire par
            // le rapport des tensions ; l'Icc est limité par le transformateur (≈ 20 × In secondaire, ucc ≈ 5 %, indicatif).
            var calibreAmont = qg;
            var seuilAmont = imAmont;
            var iccLocal = icc;
            var libelleAmont = $"QG {A(qg)}";
            if (alimentesPar.TryGetValue(d.Depart.Id, out var source) && r.Departs.FirstOrDefault(x => x.Depart.Id == source) is { } t)
            {
                var rapportTensions = (t.Depart.Alimentation == TypeAlimentation.Mono230 ? 230 : 400) / 230.0;
                calibreAmont = t.CalibreProtection * rapportTensions;
                seuilAmont = SeuilAmontPrimaire(t.Materiel) * rapportTensions;
                var va = t.Materiel.Parcourir().FirstOrDefault(e => e.Famille == "Alimentation" && e.PuissanceKw > 0)?.PuissanceKw * 1000 ?? 0;
                if (va > 0) iccLocal = 20 * va / 230;
                libelleAmont = $"{t.Materiel.Repere} {A(t.CalibreProtection)} (primaire de T{t.Depart.Numero}, ramené à {A(calibreAmont)} en 230 V)";
            }
            var rapport = d.CalibreProtection > 0 ? calibreAmont / d.CalibreProtection : double.MaxValue;
            var (imAval, type) = SeuilAval(protection);
            NiveauSelectivite niveau;
            string commentaire;
            double limite;
            if (rapport < RapportSurchargeMini)
            {
                niveau = NiveauSelectivite.Absente;
                limite = 0;
                commentaire = $"Rapport des calibres {N(rapport)} < {N(RapportSurchargeMini)} : l'amont peut déclencher sur une surcharge du départ.";
            }
            else if (imAval >= seuilAmont)
            {
                niveau = NiveauSelectivite.AVerifier;
                limite = 0;
                commentaire = $"Seuils magnétiques qui se recouvrent ({type}) : sélectivité à confirmer dans les tables du fabricant.";
            }
            else if (iccLocal <= seuilAmont)
            {
                niveau = NiveauSelectivite.Totale;
                limite = iccLocal;
                commentaire = "Totale : l'Icc reste sous le seuil magnétique de l'amont.";
            }
            else
            {
                niveau = NiveauSelectivite.Partielle;
                limite = seuilAmont;
                commentaire = $"Partielle jusqu'à {A(seuilAmont)} ; au-delà, voir les tables de sélectivité (souvent totale entre gammes adaptées).";
            }
            lignes.Add(new LigneSelectivite(nom, libelleAmont, $"{protection.Repere} {protection.Calibre}", rapport, seuilAmont, imAval,
                limite, niveau, commentaire));

            if (niveau is NiveauSelectivite.Absente or NiveauSelectivite.AVerifier)
                note.Verifier($"Sélectivité avec {nom}", false, commentaire, Ref, StatutCalcul.Alerte);
            else
                note.Verifier($"Sélectivité avec {nom}", true,
                    $"Rapport {N(rapport)} ; magnétique aval ≈ {A(imAval)} < {A(seuilAmont)} ({type}). {commentaire}", Ref);
        }
        return new ResultatSelectivite { Note = note, Lignes = lignes };
    }

    /// <summary>Seuil magnétique minimal de la protection du primaire d'un transformateur (courbe D : 10 × In ; fusibles : 5 × In).</summary>
    static double SeuilAmontPrimaire(ElementMateriel protection) =>
        protection.Famille == "Sectionneur porte-fusibles" ? 5 * protection.Courant : 10 * protection.Courant;

    /// <summary>Seuil magnétique maximal (ou courant de fusion rapide) de la protection d'un départ, en A.</summary>
    static (double Seuil, string Type) SeuilAval(ElementMateriel protection) => protection.Famille switch
    {
        "Disjoncteur moteur" => (Calibres13(protection), "disjoncteur moteur, 13 × maxi de plage"),
        "Sectionneur porte-fusibles" => (15 * protection.Courant, "fusibles, fusion en moins de 10 ms vers 15 × In (indicatif)"),
        _ => protection.Courbe switch
        {
            "B" => (5 * protection.Courant, "courbe B, 3 à 5 In"),
            "D" => (20 * protection.Courant, "courbe D, 10 à 20 In"),
            _ => (10 * protection.Courant, "courbe C, 5 à 10 In"),
        },
    };

    static double Calibres13(ElementMateriel e) => Normes.Calibres.SeuilMagnetiqueDisjoncteurMoteur * (e.PlageMax > 0 ? e.PlageMax : e.Courant);

    public static string Libelle(NiveauSelectivite n) => n switch
    {
        NiveauSelectivite.Totale => "Totale",
        NiveauSelectivite.Partielle => "Partielle",
        NiveauSelectivite.AVerifier => "À vérifier",
        _ => "Absente",
    };
}
