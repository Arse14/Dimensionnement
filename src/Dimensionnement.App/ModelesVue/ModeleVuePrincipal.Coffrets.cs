using Dimensionnement.App.Infrastructure;
using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>Une enveloppe proposée dans la vue d'armoire : l'armoire principale (Id vide) ou un coffret déporté.</summary>
public sealed record ChoixArmoire(Guid? Id, string Libelle);

/// <summary>Coffrets déportés : enveloppe affichée dans la vue d'armoire et déport d'appareils de l'une à l'autre.</summary>
public partial class ModeleVuePrincipal
{
    /// <summary>Plus de coffrets déportés n'a pas de sens pour une même machine.</summary>
    public const int CoffretsMax = 8;

    Guid? _affichee;

    /// <summary>Réglages de l'enveloppe affichée dans la vue d'armoire.</summary>
    ConfigurationArmoire ConfigCourante =>
        _affichee is { } id && _projet.Armoire.Coffrets.FirstOrDefault(c => c.Id == id) is { } c ? c : _projet.Armoire;

    string NomCourant => (ConfigCourante as CoffretDeporte)?.Nom ?? "armoire principale";

    public ResultatArmoire? ArmoireAffichee =>
        Resultat.Armoire?.Enveloppes.FirstOrDefault(e => e.Id == _affichee) ?? Resultat.Armoire;

    public IReadOnlyList<ChoixArmoire> ChoixArmoires =>
        _projet.Armoire.Coffrets.Select(c => new ChoixArmoire(c.Id, c.Nom)).Prepend(new ChoixArmoire(null, "Armoire principale")).ToList();

    public ChoixArmoire? ArmoireChoisie
    {
        get => ChoixArmoires.FirstOrDefault(c => c.Id == _affichee) ?? ChoixArmoires[0];
        set
        {
            if (value is null || value.Id == _affichee) return;
            _affichee = value.Id;
            // Les pages de réglages suivent l'enveloppe affichée.
            Selection = Selection switch
            {
                PageArmoire => new PageArmoire(_projet, ConfigCourante, NomCourant),
                PageThermique => new PageThermique(_projet, ConfigCourante, NomCourant),
                var page => page,
            };
            SignalerTout();
        }
    }

    public bool CoffretAffiche => ConfigCourante is CoffretDeporte;

    /// <summary>Nom du coffret affiché, modifiable.</summary>
    public string NomCoffret
    {
        get => (ConfigCourante as CoffretDeporte)?.Nom ?? "";
        set
        {
            if (ConfigCourante is not CoffretDeporte c || string.IsNullOrWhiteSpace(value) || value.Trim() == c.Nom) return;
            c.Nom = value.Trim();
            Recalculer();
        }
    }

    /// <summary>Destinations proposées au clic droit sur un appareil.</summary>
    public IReadOnlyList<DestinationDeport> DestinationsDeport =>
        ChoixArmoires.Select(c => new DestinationDeport(c.Id, c.Libelle))
            .Concat(_projet.Armoire.Coffrets.Count < CoffretsMax ? new[] { new DestinationDeport(Guid.Empty, "Nouveau coffret") } : Array.Empty<DestinationDeport>())
            .ToList();

    Commande? _deporter, _deporterCeQuiNEntrePas, _supprimerCoffret;

    /// <summary>Déporte un appareil, ou tout son départ, vers une autre enveloppe (Guid.Empty : un nouveau coffret).</summary>
    public Commande Deporter => _deporter ??= new Commande(p => Modifier(() =>
    {
        var d = (DeportAppareil)p!;
        var cible = d.Cible == Guid.Empty ? NouveauCoffret().Id : d.Cible;
        var cles = d.ToutLeDepart && d.DepartId is { } depart
            ? Resultat.Armoire!.Enveloppes.SelectMany(e => e.Appareils).Where(a => a.Element?.DepartId == depart).Select(CalculArmoire.CleAffectation)
            : new[] { d.Cle };
        foreach (var cle in cles.Distinct().ToList()) Affecter(cle, cible);
    }), p => p is DeportAppareil);

    /// <summary>Envoie dans un nouveau coffret tout ce qui attend dans la zone « À placer », avec le reste de leurs départs.</summary>
    public Commande DeporterCeQuiNEntrePas => _deporterCeQuiNEntrePas ??= new Commande(() => Modifier(() =>
    {
        if (ArmoireAffichee is not { } a) return;
        var dehors = a.Appareils.Where(x => x.X >= a.LargeurTotale).ToList();
        var departs = dehors.Select(x => x.Element?.DepartId).OfType<Guid>().ToHashSet();
        var cles = dehors.Select(CalculArmoire.CleAffectation)
            .Concat(a.Appareils.Where(x => x.Element?.DepartId is { } id && departs.Contains(id)).Select(CalculArmoire.CleAffectation));
        var coffret = NouveauCoffret();
        foreach (var cle in cles.Distinct().ToList()) Affecter(cle, coffret.Id);
    }), () => ArmoireAffichee is { LargeurAttente: > 0 } && _projet.Armoire.Coffrets.Count < CoffretsMax);

    /// <summary>Supprime le coffret affiché ; son matériel revient dans l'armoire principale.</summary>
    public Commande SupprimerCoffret => _supprimerCoffret ??= new Commande(() => Modifier(() =>
    {
        if (ConfigCourante is not CoffretDeporte c) return;
        foreach (var cle in _projet.Armoire.Affectations.Where(x => x.Value == c.Id).Select(x => x.Key).ToList())
            Affecter(cle, null);
        _projet.Armoire.Coffrets.Remove(c);
        _affichee = null;
    }), () => CoffretAffiche);

    void Affecter(string cle, Guid? coffret)
    {
        var armoire = _projet.Armoire;
        // La position manuelle était celle de l'ancienne enveloppe.
        armoire.Positions.Remove(cle);
        foreach (var c in armoire.Coffrets) c.Positions.Remove(cle);
        if (coffret is { } id && armoire.Coffrets.Any(c => c.Id == id)) armoire.Affectations[cle] = id;
        else armoire.Affectations.Remove(cle);
    }

    /// <summary>Nouveau coffret aux réglages de l'armoire principale (marque, thermique, place libre), taille automatique.</summary>
    CoffretDeporte NouveauCoffret()
    {
        var a = _projet.Armoire;
        var numero = 2;
        while (a.Coffrets.Any(c => c.Nom == $"Coffret {numero}")) numero++;
        var coffret = new CoffretDeporte
        {
            Nom = $"Coffret {numero}", Marque = a.Marque, ReservePourcent = a.ReservePourcent, Materiau = a.Materiau,
            Installation = a.Installation, TemperatureAmbianteMax = a.TemperatureAmbianteMax, TemperatureAmbianteMin = a.TemperatureAmbianteMin,
            TemperatureInterieureMax = a.TemperatureInterieureMax, TemperatureInterieureMin = a.TemperatureInterieureMin,
            DegagementVariateurVentile = a.DegagementVariateurVentile, CommandeVentilation = a.CommandeVentilation,
            ConsigneThermostat = a.ConsigneThermostat, PositionVentilation = a.PositionVentilation, TensionVentilation = a.TensionVentilation,
        };
        a.Coffrets.Add(coffret);
        _affichee = coffret.Id;
        return coffret;
    }
}
