using System.Text;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Rapport;

/// <summary>
/// Export des fonctions de sécurité au format CSV (séparateur point-virgule), pour préparer leur saisie
/// dans SISTEMA : une ligne par fonction avec ses sous-systèmes capteurs, logique et actionneurs.
/// </summary>
public static class ExportSistema
{
    public static string Generer(ResultatProjet r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Fonction;Type;PLr;Catégorie conseillée;Architecture;Capteurs;Logique;Actionneurs");
        if (r.Securite is null) return sb.ToString();

        var logique = r.Securite.Appareils.Select(a => $"{a.Repere} {a.Calibre}").ToList();
        var i = 0;
        foreach (var f in r.Securite.Fonctions)
        {
            var appareil = CalculSecurite.AvecAutomate(r.Projet.Securite) ? logique.FirstOrDefault() : logique.ElementAtOrDefault(i);
            i++;
            var actionneurs = f.Fonction.Departs
                .Select(n => r.Departs.FirstOrDefault(d => d.Depart.Numero == n))
                .Where(d => d is not null)
                .Select(d => Actionneurs(d!))
                .ToList();
            sb.AppendLine(string.Join(";", Champ(f.Fonction.Nom), f.Fonction.Type, f.Fonction.PLr, f.Categorie, Champ(f.Architecture),
                f.Fonction.NombreCapteurs, Champ(appareil ?? ""), Champ(string.Join(" / ", actionneurs))));
        }
        return sb.ToString();
    }

    static string Actionneurs(ResultatDepart d)
    {
        if (d.Depart is DepartMoteur { Demarrage: ModeDemarrage.Variateur })
            return $"D{d.Depart.Numero:00} STO variateur U{d.Depart.Numero}";
        var contacteurs = d.Materiel.Parcourir().Where(e => e.Famille == "Contacteur").Select(e => e.Repere);
        return $"D{d.Depart.Numero:00} {string.Join(" + ", contacteurs)}";
    }

    static string Champ(string texte) =>
        texte.Contains(';') || texte.Contains('"') ? $"\"{texte.Replace("\"", "\"\"")}\"" : texte;
}
