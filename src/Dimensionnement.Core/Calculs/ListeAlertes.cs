using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

/// <summary>Gravité d'une alerte : non conforme (rouge), à vérifier (orange) ou information à compléter (orange).</summary>
public enum TypeAlerte { NonConforme, AVerifier, ARenseigner }

/// <summary>Page à ouvrir pour traiter l'alerte.</summary>
public enum CibleAlerte { Projet, Depart, Armoire, Thermique, Securite, Automate, Selectivite, References }

/// <summary>Correction proposée directement sur l'alerte.</summary>
public enum ActionAlerte { Aucune, RemonterGeneral }

/// <summary>
/// Un point à traiter, tiré des notes de calcul ou de ce qui reste à renseigner. <see cref="Cle"/> est stable d'un calcul
/// à l'autre : c'est elle qui retient qu'une alerte a été ignorée, avec son motif.
/// </summary>
public record Alerte(string Cle, TypeAlerte Type, string Lieu, string Point, string Detail, CibleAlerte Cible)
{
    public Guid? DepartId { get; init; }
    public Guid? CoffretId { get; init; }
    public ActionAlerte Action { get; init; }
    /// <summary>Valeur de l'action : calibre du général proposé.</summary>
    public double Valeur { get; init; }
    public string LibelleAction => Action == ActionAlerte.RemonterGeneral ? $"Remonter le général à {N(Valeur)} A" : "";
    public string? Motif { get; init; }
    public bool Ignoree => Motif is not null;
    public StatutCalcul Statut => Type == TypeAlerte.NonConforme ? StatutCalcul.NonConforme : StatutCalcul.Alerte;
}

/// <summary>
/// Toutes les alertes du projet en une liste : vérifications non conformes ou à vérifier de chaque note de calcul
/// (coffrets déportés compris), puis ce qui reste à renseigner (cartouche, cotes, références indicatives).
/// Recalculée à chaque modification : une alerte réglée disparaît d'elle-même.
/// </summary>
public static class ListeAlertes
{
    public static IReadOnlyList<Alerte> Generer(ResultatProjet r)
    {
        var liste = new List<Alerte>();
        foreach (var d in r.Departs)
            DepuisNote(liste, d.Note, d.Depart.Repere.Length > 0 ? d.Depart.Repere : $"{d.Depart.Code} {d.Depart.Designation}".TrimEnd(),
                CibleAlerte.Depart, d.Depart.Id, null);
        DepuisNote(liste, r.NoteArrivee, "Arrivée", CibleAlerte.Projet, null, null);
        if (r.Securite?.Note is { } sec) DepuisNote(liste, sec, "Sécurité machine", CibleAlerte.Securite, null, null);
        if (r.Automate?.Note is { } api) DepuisNote(liste, api, "Automate", CibleAlerte.Automate, null, null);
        if (r.References?.Note is { } refs) DepuisNote(liste, refs, "Références", CibleAlerte.References, null, null);
        if (r.Selectivite?.Note is { } sel) DepuisNote(liste, sel, "Sélectivité", CibleAlerte.Selectivite, null, null);
        if (r.Armoire is { } armoire)
            foreach (var a in armoire.Enveloppes)
            {
                var nom = a.Id is null ? "Armoire" : a.Nom;
                DepuisNote(liste, a.Note, nom, CibleAlerte.Armoire, null, a.Id);
                if (a.Thermique?.Note is { } th) DepuisNote(liste, th, a.Id is null ? "Thermique" : $"{a.Nom} – thermique", CibleAlerte.Thermique, null, a.Id);
            }
        ProposerGeneral(liste, r);
        ARenseigner(liste, r);

        // Alertes ignorées par l'utilisateur : gardées en bas de liste avec leur motif.
        var ignorees = r.Projet.AlertesIgnorees;
        return liste.Select(a => ignorees.TryGetValue(a.Cle, out var motif) ? a with { Motif = motif } : a)
            .OrderBy(a => a.Ignoree).ThenBy(a => a.Type).ToList();
    }

    /// <summary>Alertes encore à traiter (non ignorées).</summary>
    public static IEnumerable<Alerte> Actives(this IEnumerable<Alerte> alertes) => alertes.Where(a => !a.Ignoree);

    static void DepuisNote(List<Alerte> liste, NoteDeCalcul note, string lieu, CibleAlerte cible, Guid? depart, Guid? coffret)
    {
        foreach (var l in note.Lignes.Where(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme))
            liste.Add(new Alerte($"{note.Prefixe}|{coffret}|{l.Libelle}", l.Statut == StatutCalcul.NonConforme ? TypeAlerte.NonConforme : TypeAlerte.AVerifier,
                lieu, l.Libelle, EcritureMath.Texte(l.Message), cible) { DepartId = depart, CoffretId = coffret });
    }

    /// <summary>
    /// Sélectivité absente sous le disjoncteur général : propose le plus petit calibre normalisé du général qui donne un
    /// rapport d'au moins 1,6 avec tous les départs qu'il alimente directement.
    /// </summary>
    static void ProposerGeneral(List<Alerte> liste, ResultatProjet r)
    {
        var calibre = CalibreGeneralSelectif(r);
        if (calibre is not { } v) return;
        for (var i = 0; i < liste.Count; i++)
            if (liste[i].Cible == CibleAlerte.Selectivite && liste[i].Detail.StartsWith("Rapport des calibres"))
                liste[i] = liste[i] with
                {
                    Action = ActionAlerte.RemonterGeneral, Valeur = v,
                    DepartId = r.Departs.FirstOrDefault(d => $"Sélectivité avec {d.Depart.Code}" == liste[i].Point)?.Depart.Id,
                };
    }

    /// <summary>Calibre minimal du général pour une sélectivité ampèremétrique avec tous ses départs, ou null si déjà assurée.</summary>
    public static double? CalibreGeneralSelectif(ResultatProjet r)
    {
        if (r.Selectivite is not { } sel) return null;
        var departs = sel.Lignes.Where(l => l.Niveau == NiveauSelectivite.Absente && l.Amont.StartsWith("QG"))
            .Select(l => r.Departs.FirstOrDefault(d => $"{d.Depart.Code}" == l.Depart)?.CalibreProtection ?? 0).ToList();
        if (departs.Count == 0) return null;
        var mini = departs.Max() * CalculSelectivite.RapportSurchargeMini;
        return Calibres.Superieur(Calibres.Disjoncteurs, mini - 1e-9);
    }

    /// <summary>
    /// Applique la correction proposée : le général est forcé au calibre sélectif (motif automatique), et le sectionneur
    /// remonté avec lui s'il devient plus petit ; le câble d'arrivée suit au recalcul.
    /// </summary>
    public static void Appliquer(Projet projet, ResultatProjet r, Alerte alerte, string auteur)
    {
        if (alerte.Action != ActionAlerte.RemonterGeneral) return;
        var v = alerte.Valeur;
        var motif = $"{alerte.Point} : rapport des calibres ≥ {N(CalculSelectivite.RapportSurchargeMini)}";
        projet.ForcagesArrivee[CodesCalcul.CalibreGeneral] = new Forcage { Valeur = v, Motif = motif, Auteur = auteur };
        var qs = r.NoteArrivee.Lignes.FirstOrDefault(l => l.Code == CodesCalcul.CalibreSectionneur)?.ValeurRetenue ?? 0;
        if (qs < v && Calibres.Superieur(Calibres.InterrupteursSectionneurs, v - 1e-9) is { } s)
            projet.ForcagesArrivee[CodesCalcul.CalibreSectionneur] = new Forcage
                { Valeur = s, Motif = $"Suit le disjoncteur général remonté à {N(v)} A", Auteur = auteur };
    }

    static void ARenseigner(List<Alerte> liste, ResultatProjet r)
    {
        var p = r.Projet;
        var manquants = new (string Champ, string Valeur)[] { ("Nom du projet", p.Nom), ("N° d'affaire", p.Affaire), ("Client", p.Client), ("Auteur", p.Auteur), ("Indice", p.Indice) }
            .Where(c => string.IsNullOrWhiteSpace(c.Valeur)).Select(c => c.Champ).ToList();
        if (manquants.Count > 0)
            liste.Add(new Alerte("PROJET|cartouche", TypeAlerte.ARenseigner, "Projet", "Cartouche à compléter",
                $"{string.Join(", ", manquants)} : repris sur le rapport et la demande de devis.", CibleAlerte.Projet));

        var elements = r.Arbre.Parcourir().ToList();
        var cotes = elements.Where(e => e.CotesIncompletes).Select(e => e.Repere).Distinct().ToList();
        if (cotes.Count > 0)
            liste.Add(new Alerte("MAT|cotes", TypeAlerte.ARenseigner, "Matériel", $"Cotes à compléter ({cotes.Count})",
                $"{Liste(cotes)} : cotes par défaut dans l'implantation tant que largeur, hauteur et profondeur ne sont pas saisies.", CibleAlerte.Armoire));
        var indicatives = elements.Where(e => e.AVerifier && e.Reference.Length > 0).Select(e => e.Repere).Distinct().ToList();
        if (indicatives.Count > 0)
            liste.Add(new Alerte("MAT|references", TypeAlerte.ARenseigner, "Matériel", $"Références indicatives ({indicatives.Count})",
                $"{Liste(indicatives)} : à contrôler sur la fiche du fabricant, puis clic droit « Valider la référence ».", CibleAlerte.References));
        var sansReference = elements.Where(e => e.Reference.Length == 0 && e.Famille is "Disjoncteur" or "Contacteur" or "Disjoncteur moteur"
            or "Variateur" or "Alimentation" or "Chauffage" or "Climatisation" && !e.HorsArmoire).Select(e => e.Repere).Distinct().ToList();
        if (sansReference.Count > 0)
            liste.Add(new Alerte("MAT|sans-reference", TypeAlerte.ARenseigner, "Matériel", $"Références à choisir ({sansReference.Count})",
                $"{Liste(sansReference)} : aucune référence catalogue retenue ; le devis les décrit par leurs caractéristiques.", CibleAlerte.References));
    }

    static string Liste(IReadOnlyList<string> reperes) =>
        reperes.Count <= 8 ? string.Join(", ", reperes) : $"{string.Join(", ", reperes.Take(8))} et {reperes.Count - 8} autre(s)";
}
