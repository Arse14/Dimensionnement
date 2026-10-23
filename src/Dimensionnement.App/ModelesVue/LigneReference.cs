using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>Un appareil de la page des références ; une référence saisie remplace celle du catalogue.</summary>
public class LigneReference
{
    readonly ConfigurationReferences _config;
    readonly string _cle;
    string _reference;

    public LigneReference(ElementMateriel e, ConfigurationReferences config)
    {
        _config = config;
        Repere = e.Repere;
        _cle = e.RepereOrigine;
        Designation = e.Designation;
        Calibre = e.Calibre;
        Fabricant = e.Fabricant;
        _reference = e.Reference;
    }

    public string Repere { get; }
    public string Designation { get; }
    public string Calibre { get; }
    public string Fabricant { get; }

    /// <summary>Vrai si la référence a été saisie à la main.</summary>
    public bool Forcee => _config.Forcees.ContainsKey(_cle);

    public string Reference
    {
        get => _reference;
        set
        {
            var saisie = value?.Trim() ?? "";
            if (saisie == _reference) return;
            // Vider la case rend la main au catalogue.
            if (saisie.Length == 0) _config.Forcees.Remove(_cle);
            else _config.Forcees[_cle] = saisie;
            _reference = saisie;
        }
    }
}
