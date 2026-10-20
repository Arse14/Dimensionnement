using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>Page du bilan 24 V DC et des consommateurs déclarés.</summary>
public record PageBilan24V(Projet Projet);

/// <summary>Page de la liste de contrôle CEM et basse tension.</summary>
public record PageControles(Projet Projet);
