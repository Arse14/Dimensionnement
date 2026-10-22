using System.Windows;
using Dimensionnement.App.Infrastructure;
using Dimensionnement.App.Vues;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>
/// Liste des alertes sous le compteur du bandeau : ce qui est non conforme, à vérifier ou à renseigner, recalculée à chaque
/// modification. « Voir » ouvre la page concernée, l'action proposée corrige directement, « Ignorer » demande un motif.
/// </summary>
public partial class ModeleVuePrincipal
{
    bool _alertesOuvertes;

    public IReadOnlyList<Alerte> Alertes => Resultat.Alertes;

    public bool AucuneAlerte => Resultat.Alertes.Count == 0;

    public int NombreAlertesActives => Resultat.Alertes.Actives().Count();

    /// <summary>Couleur du compteur : rouge s'il reste un non conforme, orange s'il reste un point, vert sinon.</summary>
    public StatutCalcul StatutAlertes => Resultat.Alertes.Actives().Select(a => a.Statut).DefaultIfEmpty(StatutCalcul.Conforme).Max();

    public string TitreAlertes
    {
        get
        {
            var actives = Resultat.Alertes.Actives().ToList();
            if (actives.Count == 0) return "Aucun point à traiter";
            var parties = new List<string>();
            var nc = actives.Count(a => a.Type == TypeAlerte.NonConforme);
            var av = actives.Count(a => a.Type == TypeAlerte.AVerifier);
            var ar = actives.Count(a => a.Type == TypeAlerte.ARenseigner);
            if (nc > 0) parties.Add(nc == 1 ? "1 non conforme" : $"{nc} non conformes");
            if (av > 0) parties.Add($"{av} à vérifier");
            if (ar > 0) parties.Add($"{ar} à renseigner");
            return string.Join(", ", parties);
        }
    }

    public bool AlertesOuvertes
    {
        get => _alertesOuvertes;
        set { if (_alertesOuvertes == value) return; _alertesOuvertes = value; Signaler(); }
    }

    Commande? _basculerAlertes, _voirAlerte, _appliquerAlerte, _ignorerAlerte, _retablirAlerte;
    public Commande BasculerAlertes => _basculerAlertes ??= new Commande(() => AlertesOuvertes = !AlertesOuvertes);
    public Commande VoirAlerte => _voirAlerte ??= new Commande(p => Voir((Alerte)p!), p => p is Alerte);
    public Commande AppliquerAlerte => _appliquerAlerte ??= new Commande(p => Appliquer((Alerte)p!), p => p is Alerte { Action: not ActionAlerte.Aucune });
    public Commande IgnorerAlerte => _ignorerAlerte ??= new Commande(p => Ignorer((Alerte)p!), p => p is Alerte { Ignoree: false });
    public Commande RetablirAlerte => _retablirAlerte ??= new Commande(p => Modifier(() => _projet.AlertesIgnorees.Remove(((Alerte)p!).Cle)),
        p => p is Alerte { Ignoree: true });

    void Voir(Alerte a)
    {
        AlertesOuvertes = false;
        if (a.CoffretId != _affichee && a.Cible is CibleAlerte.Armoire or CibleAlerte.Thermique)
        {
            _affichee = a.CoffretId;
            SignalerTout();
        }
        Selection = a.Cible switch
        {
            CibleAlerte.Depart or CibleAlerte.Selectivite when a.DepartId is { } id && _projet.Departs.FirstOrDefault(d => d.Id == id) is { } d => d,
            CibleAlerte.Selectivite => new PageSelectivite(_projet),
            CibleAlerte.Armoire => new PageArmoire(_projet, ConfigCourante, NomCourant),
            CibleAlerte.Thermique => new PageThermique(_projet, ConfigCourante, NomCourant),
            CibleAlerte.Securite => _projet.Securite,
            CibleAlerte.Automate => _projet.Automate,
            CibleAlerte.References => new PageReferences(_projet),
            _ => _projet,
        };
    }

    void Appliquer(Alerte a)
    {
        AlertesOuvertes = false;
        Modifier(() => ListeAlertes.Appliquer(_projet, Resultat, a, Auteur));
    }

    void Ignorer(Alerte a)
    {
        var fenetre = new FenetreMotif(a) { Owner = Application.Current.MainWindow };
        if (fenetre.ShowDialog() != true) return;
        Modifier(() => _projet.AlertesIgnorees[a.Cle] = $"{fenetre.Motif} ({Auteur}, {DateTime.Now:dd/MM/yyyy})");
    }

    string Auteur => _projet.Auteur is { Length: > 0 } a ? a : Environment.UserName;
}
