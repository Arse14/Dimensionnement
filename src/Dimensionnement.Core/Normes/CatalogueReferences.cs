using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Normes;

/// <summary>
/// Une ligne du catalogue de références : l'appareil convient si sa valeur (courant, réglage ou puissance) est dans
/// [Min ; Max]. La référence peut contenir des champs remplacés par ceux de l'appareil : {P} pôles, {In} calibre,
/// {In2} / {In3} calibre sur 2 ou 3 chiffres, {Ir} réglage, {kW}, {Min} / {Max} plage, {Taille}, {Classe}.
/// </summary>
public record ReferenceCatalogue
{
    public string Fabricant { get; init; } = "";
    public string Famille { get; init; } = "";
    public double Min { get; init; }
    public double Max { get; init; }
    public string Reference { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Courbe { get; init; } = "";
    public string Taille { get; init; } = "";
    public string Classe { get; init; } = "";

    /// <summary>Vrai une fois la référence contrôlée sur le catalogue du fabricant.</summary>
    public bool Verifie { get; init; }
}

public class ResultatReferences
{
    public required NoteDeCalcul Note { get; init; }
    public int Completees { get; init; }
    public IReadOnlyList<string> SansReference { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Catalogue des références de matériel par fabricant, embarqué au format JSON et remplaçable par un fichier
/// « catalogue-references.json » de l'utilisateur. Les références sont indicatives tant qu'elles ne sont pas vérifiées.
/// </summary>
public static class CatalogueReferences
{
    public const string NomFichier = "catalogue-references.json";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static readonly Lazy<IReadOnlyList<ReferenceCatalogue>> Embarque = new(() => Lire(TexteEmbarque()));

    /// <summary>Ordre des fabricants de repli quand le fabricant retenu n'a pas l'appareil (ex. variateur chez Legrand).</summary>
    static readonly FabricantMateriel[] Repli = { FabricantMateriel.Schneider, FabricantMateriel.Siemens, FabricantMateriel.ABB };

    public static IReadOnlyList<ReferenceCatalogue> Charger(string? fichierPersonnalise = null) =>
        fichierPersonnalise is not null && File.Exists(fichierPersonnalise) ? Lire(File.ReadAllText(fichierPersonnalise)) : Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Normes.catalogue-references.json")
                        ?? throw new InvalidOperationException("Catalogue des références introuvable.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static IReadOnlyList<ReferenceCatalogue> Lire(string json) =>
        JsonSerializer.Deserialize<List<ReferenceCatalogue>>(json, Options) ?? new List<ReferenceCatalogue>();

    /// <summary>Complète la référence et le fabricant de chaque appareil de l'arbre qui n'en a pas.</summary>
    public static ResultatReferences Appliquer(ElementMateriel arbre, ConfigurationReferences config, IReadOnlyList<ReferenceCatalogue> catalogue)
    {
        var note = new NoteDeCalcul("REF");
        note.Info("Fabricant retenu", $"{config.Fabricant} pour la puissance et les protections" +
            (config.MarqueDisjoncteurs != MarqueFamille.Projet ? $", {config.MarqueDisjoncteurs} pour les disjoncteurs" : "") +
            (config.MarqueContacteurs != MarqueFamille.Projet ? $", {config.MarqueContacteurs} pour les contacteurs" : "") + ".");
        var completees = 0;
        var replis = new List<string>();
        var sans = new List<string>();
        var nonVerifiees = 0;

        foreach (var e in arbre.Parcourir())
        {
            if (config.Forcees.TryGetValue(e.Repere, out var forcee) && forcee.Length > 0)
            {
                e.Reference = forcee;
                if (e.Fabricant.Length == 0) e.Fabricant = config.FabricantDe(e.Famille).ToString();
                completees++;
                continue;
            }
            if (e.Reference.Length > 0 || !Commandable(e)) continue;
            if (e.Famille is "Sécurité" or "Entrées/sorties")
            {
                // Choisis par leur module : la gamme suffit pour la consultation, la référence exacte vient du configurateur.
                e.Reference = e.Famille == "Sécurité" ? e.Calibre : $"{e.Designation} {e.Calibre}";
                completees++;
                continue;
            }

            var voulu = config.FabricantDe(e.Famille);
            var trouve = Chercher(e, voulu, catalogue);
            var fabricant = voulu;
            if (trouve is null)
                foreach (var f in Repli.Where(f => f != voulu))
                    if (Chercher(e, f, catalogue) is { } r)
                    {
                        trouve = r;
                        fabricant = f;
                        replis.Add($"{e.Repere} ({f})");
                        break;
                    }
            if (trouve is null)
            {
                sans.Add(e.Repere);
                continue;
            }
            e.Reference = Remplir(trouve.Reference, e);
            e.Fabricant = trouve.Gamme.Length > 0 ? $"{fabricant} – {trouve.Gamme}" : fabricant.ToString();
            if (!trouve.Verifie) nonVerifiees++;
            completees++;
        }

        if (replis.Count > 0)
            note.Info("Autre fabricant", $"Absent de la gamme {config.Fabricant} : {string.Join(", ", replis)}.");
        note.Verifier("Références", sans.Count == 0,
            sans.Count == 0 ? $"{completees} appareil(s) référencé(s)." : $"Sans référence, à compléter : {string.Join(", ", sans)}.",
            siEchec: StatutCalcul.Alerte);
        if (nonVerifiees > 0)
            note.Info("Contrôle des références", $"{nonVerifiees} référence(s) issues du catalogue indicatif : à confirmer sur le configurateur du fabricant avant commande.");
        return new ResultatReferences { Note = note, Completees = completees, SansReference = sans };
    }

    /// <summary>Appareils qui se commandent (les câbles et récepteurs sont traités à part dans la liste de matériel).</summary>
    public static bool Commandable(ElementMateriel e) =>
        e.Famille is not ("Réseau" or "Câble" or "Moteur" or "Récepteur" or "Prise de courant");

    public static ReferenceCatalogue? Chercher(ElementMateriel e, FabricantMateriel fabricant, IReadOnlyList<ReferenceCatalogue> catalogue)
    {
        var valeur = e.Famille is "Variateur" or "Démarreur progressif" ? e.PuissanceKw : e.Courant;
        var classe = Classe(e.Designation);
        return catalogue
            .Where(r => r.Fabricant.Equals(fabricant.ToString(), StringComparison.OrdinalIgnoreCase) && r.Famille == e.Famille)
            .Where(r => valeur >= r.Min - 1e-9 && valeur <= r.Max + 1e-9)
            .Where(r => r.Courbe.Length == 0 || r.Courbe == e.Courbe)
            .Where(r => r.Taille.Length == 0 || r.Taille == e.TailleFusible)
            .Where(r => r.Classe.Length == 0 || r.Classe == classe)
            .OrderBy(r => r.Max).ThenByDescending(r => r.Min)
            .FirstOrDefault();
    }

    /// <summary>
    /// Adapte la référence d'un contacteur à la tension de sa bobine : suffixe BD / B7 / P7 chez Schneider,
    /// 1BB4x / 1AB0x / 1AP0x chez Siemens, bobine -13 (24-60 V AC/DC) ou -14 (100-250 V AC/DC) chez ABB.
    /// </summary>
    public static string Bobine(string reference, TensionCommande tension)
    {
        if (string.IsNullOrEmpty(reference) || tension == TensionCommande.Continu24) return reference;
        var ac24 = tension == TensionCommande.Alternatif24;
        if (reference.StartsWith("LC1D") && reference.EndsWith("BD"))
            return reference[..^2] + (ac24 ? "B7" : "P7");
        if (reference.StartsWith("3RT20") && reference.Contains("-1BB4"))
            return reference.Replace("-1BB4", ac24 ? "-1AB0" : "-1AP0");
        if (reference.StartsWith("AF") && reference.EndsWith("-13"))
            return ac24 ? reference : reference[..^3] + "-14";
        if (reference.Contains("bobine 24 V DC"))
            return reference.Replace("bobine 24 V DC", ac24 ? "bobine 24 V AC" : "bobine 230 V AC");
        return reference;
    }

    static string Classe(string designation) =>
        new[] { "gG", "aM", "gR" }.FirstOrDefault(c => designation.Contains($" {c} ")) ?? "";

    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Remplir(string modele, ElementMateriel e)
    {
        var calibre = (int)Math.Round(e.Courant);
        return modele
            .Replace("{P}", (e.Poles > 0 ? e.Poles : 3).ToString(CultureInfo.InvariantCulture))
            .Replace("{In2}", calibre.ToString("00", CultureInfo.InvariantCulture))
            .Replace("{In3}", calibre.ToString("000", CultureInfo.InvariantCulture))
            .Replace("{In}", Format.N(e.Courant, 1))
            .Replace("{Ir}", Format.N(e.Courant, 1))
            .Replace("{kW}", e.PuissanceKw.ToString("0.##", Fr))
            .Replace("{Min}", Format.N(e.PlageMin))
            .Replace("{Max}", Format.N(e.PlageMax))
            .Replace("{Taille}", e.TailleFusible)
            .Replace("{Classe}", Classe(e.Designation));
    }
}
