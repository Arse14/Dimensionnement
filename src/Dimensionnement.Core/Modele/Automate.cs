using System.Collections.ObjectModel;

namespace Dimensionnement.Core.Modele;

public enum MarqueAutomate { Siemens, Schneider }

/// <summary>Besoins d'automatisme saisis par l'utilisateur, d'où découle le choix de la CPU.</summary>
public class ConfigurationAutomate
{
    /// <summary>Faux tant que le projet ne comporte pas d'automate.</summary>
    public bool Actif { get; set; }
    public MarqueAutomate Marque { get; set; } = MarqueAutomate.Siemens;

    /// <summary>Référence de la CPU choisie à la main ; vide pour un choix automatique selon la charge.</summary>
    public string CpuImposee { get => _cpuImposee; set => _cpuImposee = value ?? ""; }
    string _cpuImposee = "";

    /// <summary>
    /// Cartes compactes : périphérie ET200SP chez Siemens (CPU ET200SP, ou station IM 155-6 à côté d'une autre CPU),
    /// cartes haute densité à connecteurs chez Schneider.
    /// </summary>
    public bool CartesCompactes { get; set; }

    public int EntreesTor { get; set; }
    public int SortiesTor { get; set; }
    public int EntreesAnalogiques { get; set; }
    public int SortiesAnalogiques { get; set; }

    /// <summary>Esclaves et îlots d'E/S déportées sur le réseau (PROFINET, Modbus TCP, EtherNet/IP).</summary>
    public int PeripheriquesReseau { get; set; }

    /// <summary>Variateurs pilotés par le bus de terrain.</summary>
    public int VariateursBus { get; set; }

    /// <summary>Pupitres IHM raccordés sans modèle choisi (ceux de <see cref="Pupitres"/> s'y ajoutent).</summary>
    public int Ihm { get; set; } = 1;

    /// <summary>Pupitres IHM choisis au catalogue : leurs connexions, leur consommation 24 V et leur découpe de porte sont comptées.</summary>
    public ObservableCollection<PupitreIhm> Pupitres { get; set; } = new();

    /// <summary>Clients OPC UA ou postes de supervision.</summary>
    public int ClientsOpcUa { get; set; }

    /// <summary>Échanges avec d'autres automates.</summary>
    public int EchangesAutomates { get; set; }

    /// <summary>Sécurité intégrée à la CPU (CPU F chez Siemens, Safety chez Schneider).</summary>
    public bool SecuriteIntegree { get; set; }

    /// <summary>Stations d'E/S déportées (ET200SP chez Siemens, îlot TM3 chez Schneider) pilotées par la CPU.</summary>
    public ObservableCollection<StationDeportee> Stations { get; set; } = new();

    /// <summary>Réserve demandée sur les ressources de communication et d'E/S, en %.</summary>
    public double ReservePourcent { get; set; } = 20;
}

/// <summary>
/// Station d'E/S déportée sur le réseau (PROFINET, EtherNet/IP) : une tête de station et ses cartes, dans l'armoire
/// ou dans un coffret au plus près de la machine.
/// </summary>
public class StationDeportee
{
    public string Nom { get; set; } = "Station";
    public int EntreesTor { get; set; }
    public int SortiesTor { get; set; }
    public int EntreesAnalogiques { get; set; }
    public int SortiesAnalogiques { get; set; }

    /// <summary>Faux pour une station en coffret sur la machine : elle n'occupe pas la plaque de l'armoire.</summary>
    public bool DansArmoire { get; set; } = true;
}

/// <summary>Gamme d'IHM : standard (Basic Panel, Harmony ST6) ou confort (Unified Comfort, Harmony GTU / GTO).</summary>
public enum GammeIhm { Standard, Confort }

/// <summary>Pupitre opérateur raccordé à la CPU, monté en porte d'armoire ou sur pupitre machine.</summary>
public class PupitreIhm
{
    public string Nom { get; set; } = "IHM";

    /// <summary>Diagonale souhaitée, en pouces : la plus petite du catalogue qui l'atteint est retenue.</summary>
    public double Diagonale { get; set; } = 7;
    public GammeIhm Gamme { get; set; } = GammeIhm.Standard;

    /// <summary>Référence choisie à la main ; vide pour un choix d'après la diagonale et la gamme.</summary>
    public string ReferenceImposee { get => _reference; set => _reference = value ?? ""; }
    string _reference = "";
    public int Quantite { get; set; } = 1;

    /// <summary>Faux pour un pupitre sur la machine : alimenté par l'armoire mais absent de la porte.</summary>
    public bool EnPorte { get; set; } = true;
}
