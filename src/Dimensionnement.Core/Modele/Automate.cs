namespace Dimensionnement.Core.Modele;

public enum MarqueAutomate { Siemens, Schneider }

/// <summary>Besoins d'automatisme saisis par l'utilisateur, d'où découle le choix de la CPU.</summary>
public class ConfigurationAutomate
{
    /// <summary>Faux tant que le projet ne comporte pas d'automate.</summary>
    public bool Actif { get; set; }
    public MarqueAutomate Marque { get; set; } = MarqueAutomate.Siemens;

    public int EntreesTor { get; set; }
    public int SortiesTor { get; set; }
    public int EntreesAnalogiques { get; set; }
    public int SortiesAnalogiques { get; set; }

    /// <summary>Esclaves et îlots d'E/S déportées sur le réseau (PROFINET, Modbus TCP, EtherNet/IP).</summary>
    public int PeripheriquesReseau { get; set; }

    /// <summary>Variateurs pilotés par le bus de terrain.</summary>
    public int VariateursBus { get; set; }

    /// <summary>Pupitres IHM raccordés.</summary>
    public int Ihm { get; set; } = 1;

    /// <summary>Clients OPC UA ou postes de supervision.</summary>
    public int ClientsOpcUa { get; set; }

    /// <summary>Échanges avec d'autres automates.</summary>
    public int EchangesAutomates { get; set; }

    /// <summary>Sécurité intégrée à la CPU (CPU F chez Siemens, Safety chez Schneider).</summary>
    public bool SecuriteIntegree { get; set; }

    /// <summary>Réserve demandée sur les ressources de communication et d'E/S, en %.</summary>
    public double ReservePourcent { get; set; } = 20;
}
