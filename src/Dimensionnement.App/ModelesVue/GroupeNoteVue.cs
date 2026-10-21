using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>
/// Une carte de la note de calcul à l'écran : le groupe du moteur de calcul, plus l'état plié ou déplié,
/// mémorisé par clé pour survivre aux recalculs (la note est reconstruite à chaque modification).
/// </summary>
public class GroupeNoteVue
{
    readonly GroupeCalcul _groupe;
    readonly Dictionary<string, bool> _etats;

    public GroupeNoteVue(GroupeCalcul groupe, Dictionary<string, bool> etats)
    {
        _groupe = groupe;
        _etats = etats;
        Controles = groupe.Controles;
        Calculs = groupe.Calculs;
    }

    public string Nom => _groupe.Nom;
    public StatutCalcul Statut => _groupe.Statut;
    public string Resume => _groupe.Resume;
    public string ValeurCle => _groupe.ValeurCle;
    public string TitreCalculs => _groupe.TitreCalculs;
    public IReadOnlyList<LigneCalcul> Controles { get; }
    public IReadOnlyList<LigneCalcul> Calculs { get; }

    /// <summary>Carte sans point de contrôle : ses lignes grises s'affichent directement.</summary>
    public bool CalculsDirects => Controles.Count == 0 && Calculs.Count > 0;

    /// <summary>Carte avec points de contrôle : ses lignes grises sont pliées sous « Note de calcul ».</summary>
    public bool CalculsPliables => Controles.Count > 0 && Calculs.Count > 0;

    public bool Ouvert
    {
        get => _etats.TryGetValue(_groupe.Cle, out var o) ? o : _groupe.OuvertParDefaut;
        set => _etats[_groupe.Cle] = value;
    }

    public bool CalculsOuverts
    {
        get => _etats.TryGetValue(_groupe.Cle + "|calculs", out var o) ? o : _groupe.CalculsOuvertsParDefaut;
        set => _etats[_groupe.Cle + "|calculs"] = value;
    }
}
