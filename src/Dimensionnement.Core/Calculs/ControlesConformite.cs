using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Calculs;

/// <summary>Point de la liste de contrôle CEM et basse tension, coché par l'utilisateur.</summary>
public class PointControle
{
    readonly Dictionary<string, bool> _etats;

    public PointControle(Dictionary<string, bool> etats, string id, string libelle, string reference)
    {
        _etats = etats;
        Id = id;
        Libelle = libelle;
        Reference = reference;
    }

    public string Id { get; }
    public string Libelle { get; }
    public string Reference { get; }

    /// <summary>État coché, conservé dans le projet.</summary>
    public bool Fait
    {
        get => _etats.TryGetValue(Id, out var fait) && fait;
        set => _etats[Id] = value;
    }
}

/// <summary>Liste de contrôle des directives CEM 2014/30/UE et basse tension 2014/35/UE, générale puis par départ.</summary>
public static class ControlesConformite
{
    public static IReadOnlyList<PointControle> Generer(Projet projet)
    {
        var e = projet.Controles;
        var points = new List<PointControle>
        {
            new(e, "CEM-GOULOTTES", "Goulottes puissance et commande séparées, croisements à 90°", "Directive 2014/30/UE ; NF EN 60204-1, 13"),
            new(e, "CEM-PLAQUE", "Plaque de montage métallique non peinte reliée à la terre, liaisons équipotentielles courtes", "Directive 2014/30/UE"),
            new(e, "CEM-ZONES", "Implantation par zones : puissance « sale » séparée de l'automatisme « propre »", "Directive 2014/30/UE"),
            new(e, "BT-IP2X", "Protection contre les contacts directs, IP2X minimum à l'intérieur de l'armoire", "Directive 2014/35/UE ; NF EN IEC 61439-1"),
            new(e, "BT-ECHAUFFEMENT", "Vérification d'échauffement de l'ensemble", "NF EN IEC 61439-1, 10.10"),
            new(e, "BT-PE", "Continuité du circuit de protection et section des conducteurs PE", "NF EN IEC 61439-1, 10.5 ; NF C 15-100, 543"),
            new(e, "BT-MARQUAGE", "Plaque signalétique de l'ensemble, déclaration UE de conformité et marquage CE", "Directive 2014/35/UE ; NF EN IEC 61439-2"),
            new(e, "BT-ESSAIS", "Vérifications individuelles de série (isolement, câblage, fonctionnement)", "NF EN IEC 61439-1, 11"),
        };
        foreach (var d in projet.Departs)
        {
            var id = $"D{d.Numero:00}";
            if (d is DepartMoteur { Demarrage: ModeDemarrage.Variateur })
            {
                points.Add(new(e, $"{id}-BLINDAGE", $"{id} : câble moteur blindé, blindage repris à 360° aux deux extrémités", "NF EN 61800-3"));
                points.Add(new(e, $"{id}-FILTRE", $"{id} : filtre CEM côté réseau au plus près du variateur", "NF EN 61800-3"));
            }
            if (d is DepartMoteur { Demarrage: ModeDemarrage.DemarreurProgressif })
                points.Add(new(e, $"{id}-PROGRESSIF", $"{id} : catégorie d'émission du démarreur progressif compatible avec le site", "NF EN 60947-4-2"));
            if (d.Differentiel != SensibiliteDifferentiel.Aucun)
                points.Add(new(e, $"{id}-DDR", $"{id} : test du dispositif différentiel", "NF C 15-100, 61"));
        }
        return points;
    }
}
