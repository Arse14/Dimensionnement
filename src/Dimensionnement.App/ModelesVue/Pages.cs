using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>Page du bilan 24 V DC et des consommateurs déclarés.</summary>
public record PageBilan24V(Projet Projet);

/// <summary>Page de la liste de contrôle CEM et basse tension.</summary>

/// <summary>Page de l'armoire : enveloppe, réserve, aperçu de l'implantation.</summary>
public record PageArmoire(Projet Projet);

/// <summary>Page du bilan thermique de l'armoire.</summary>
public record PageThermique(Projet Projet);

/// <summary>Page de la sélectivité entre le général et les départs.</summary>
public record PageSelectivite(Projet Projet);

/// <summary>Page des références fabricants du matériel.</summary>
public record PageReferences(Projet Projet);

/// <summary>Page du bilan de puissance expliqué pas à pas.</summary>
public record PageBilanPuissance(Projet Projet);

/// <summary>Choix proposé dans une liste : la valeur enregistrée et son libellé.</summary>
public record ChoixTexte(string Valeur, string Libelle);

/// <summary>
/// Page de la ventilation d'une enveloppe (armoire principale ou coffret déporté), ouverte depuis sa carte : commande par thermostat
/// ou en continu, consigne, paroi des ventilateurs et modèle imposé.
/// </summary>
public record PageVentilation(Projet Projet, ConfigurationArmoire Config, string Nom)
{
    public IReadOnlyList<ChoixTexte> Ventilateurs { get; } = new[] { new ChoixTexte("", "Automatique : le plus petit qui suffit") }
        .Concat(Core.Normes.CatalogueVentilateurs.Actif.OrderBy(m => m.EnContinu24V).ThenBy(m => m.Debit)
            .Select(m => new ChoixTexte(m.Reference, $"{m.Reference} – {m.Debit:0} m³/h, {m.Tension}, {m.Largeur:0} × {m.Hauteur:0} mm")))
        .ToList();
}
