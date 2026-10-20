using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.ModelesVue;

/// <summary>Page du bilan 24 V DC et des consommateurs déclarés.</summary>
public record PageBilan24V(Projet Projet);

/// <summary>Page de la liste de contrôle CEM et basse tension.</summary>
public record PageControles(Projet Projet);

/// <summary>Page de l'armoire : enveloppe, réserve, aperçu de l'implantation.</summary>
public record PageArmoire(Projet Projet);

/// <summary>Page du bilan thermique de l'armoire.</summary>
public record PageThermique(Projet Projet);

/// <summary>Page de la sélectivité entre le général et les départs.</summary>
public record PageSelectivite(Projet Projet);

/// <summary>Page des références fabricants du matériel.</summary>
public record PageReferences(Projet Projet);
