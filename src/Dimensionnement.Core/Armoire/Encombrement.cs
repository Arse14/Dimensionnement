using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Armoire;

/// <summary>
/// Encombrement en mm et puissance dissipée en W d'un appareil à son courant assigné.
/// Une largeur nulle signale un appareil logé dans un autre (cartouches fusibles) : il dissipe sans occuper de place.
/// </summary>
public record Encombrement(double Largeur, double Hauteur, double Profondeur, double DissipationW)
{
    /// <summary>D'où viennent les cotes : fiche du modèle choisi, gamme relevée, ou moyenne de la famille.</summary>
    public string Origine { get; init; } = "estimation moyenne de la famille";

    /// <summary>Vrai si les cotes ont été relevées sur une fiche (modèle du catalogue vérifié ou gamme vérifiée).</summary>
    public bool CotesVerifiees { get; init; }
}

/// <summary>
/// Encombrements et pertes moyens des gammes courantes (modulaire, TeSys / SIRIUS / AF, variateurs en livre).
/// Valeurs indicatives d'avant-projet : les fiches du fabricant retenu font foi.
/// </summary>
public static class Encombrements
{
    public const string Source = "Valeurs moyennes des catalogues Schneider, Siemens, ABB et Legrand ; indicatives";

    /// <summary>Encombrement de l'appareil, ou null s'il n'est pas monté dans l'armoire.</summary>
    public static Encombrement? Estimer(ElementMateriel e)
    {
        var q = Math.Max(1, e.Quantite);
        var poles = e.Poles > 0 ? e.Poles : 3;
        var i = e.Courant;
        if (e.HorsArmoire) return null;
        if (e.Loge || e.EnPorte) return new(0, 0, 0, e.PertesW);
        if (e.LargeurMm > 0 && e.HauteurMm > 0)
            return new(e.LargeurMm, e.HauteurMm, e.ProfondeurMm, e.PertesW)
            {
                Origine = e.AVerifier ? "catalogue du modèle, valeurs indicatives" : "catalogue du modèle",
                CotesVerifiees = !e.AVerifier,
            };
        var moyen = Moyen(e, q, poles, i);
        // Cotes relevées de la gamme quand la référence est reconnue ; la dissipation de la fiche quand elle est relevée,
        // sinon l'estimation de la famille.
        var valeur = e.Famille == "Disjoncteur moteur" && e.PlageMax > 0 ? e.PlageMax : i;
        if (moyen is { Largeur: > 0 } && CatalogueEncombrements.Trouver(e.Famille, e.Reference, valeur) is { } c)
            return moyen with
            {
                Largeur = c.ParPole ? c.Largeur * poles : c.Largeur, Hauteur = c.Hauteur, Profondeur = c.Profondeur,
                DissipationW = c.Pertes > 0 ? c.Pertes * q : moyen.DissipationW,
                Origine = $"gamme {c.Fabricant} {c.Gamme} {c.Taille}".TrimEnd() + (c.Verifie ? ", relevée" : ", indicative"),
                CotesVerifiees = c.Verifie,
            };
        return moyen;
    }

    /// <summary>Cotes moyennes de la famille selon le calibre, quand la gamme n'est pas relevée.</summary>
    static Encombrement? Moyen(ElementMateriel e, int q, int poles, double i)
    {
        return e.Famille switch
        {
            "Disjoncteur" => Disjoncteur(i, poles),
            "Interrupteur-sectionneur" => InterrupteurSectionneur(i, poles),
            "Disjoncteur moteur" => DisjoncteurMoteur(e.PlageMax > 0 ? e.PlageMax : i),
            "Contacteur" => Contacteur(i),
            "Relais thermique" => new(45, 70, 95, 1 + 0.1 * i),
            "Variateur" => Variateur(e.PuissanceKw),
            "Démarreur progressif" => DemarreurProgressif(e.PuissanceKw, i),
            "Alimentation" => Alimentation(i),
            "Sectionneur porte-fusibles" => PorteFusibles(e.TailleFusible, poles),
            "Fusible" => new(0, 0, 0, q * (0.5 + 0.08 * i)),
            "Automate" => e.Designation.Contains("1500") || e.Designation.Contains("M580") || e.Designation.Contains("M262")
                ? new(70, 150, 150, 15)
                : new(110, 100, 80, 10),
            "Entrées/sorties" => new(45 * q, 100, 80, 3 * q),
            "Sécurité" => e.RepereOrigine == "KA-SEC" ? new(45, 100, 115, 5) : new(22.5, 100, 115, 2.5),
            "Parafoudre" => new(18 * poles, 90, 70, 0.5),
            "Répartiteur" => new(27 * poles, 100, 70, 0.5 * poles),
            "Relais de contrôle" => new(22.5, 90, 100, 2),
            "Câble" or "Moteur" or "Récepteur" or "Prise de courant" or "Réseau" => null,
            _ => new(45, 90, 80, 2),
        };
    }

    static Encombrement Disjoncteur(double i, int poles) => i switch
    {
        <= 63 => new(18 * poles, 90, 75, poles * (1.5 + 0.06 * i)),
        <= 125 => new(27 * poles, 90, 80, poles * (3 + 0.05 * i)),
        <= 250 => new(35 * poles, 161, 86, poles * (4 + 0.04 * i)),
        <= 630 => new(47 * poles, 255, 110, poles * (5 + 0.035 * i)),
        _ => new(70 * poles, 330, 160, poles * (10 + 0.03 * i)),
    };

    static Encombrement InterrupteurSectionneur(double i, int poles)
    {
        var pertes = poles * (0.2 + 0.0002 * i * i);
        return i switch
        {
            <= 40 => new(60, 75, 80, pertes),
            <= 125 => new(90, 90, 90, pertes),
            <= 250 => new(140, 160, 110, pertes),
            _ => new(185, 250, 140, pertes),
        };
    }

    static Encombrement DisjoncteurMoteur(double max) => max switch
    {
        <= 32 => new(45, 97, 97, 1.5 + 0.2 * max),
        <= 65 => new(55, 130, 120, 1.5 + 0.2 * max),
        _ => new(70, 150, 130, 1.5 + 0.2 * max),
    };

    static Encombrement Contacteur(double ie)
    {
        // Bobine 24 V DC et pertes par effet Joule des trois pôles à Ie.
        var pertes = 2.5 + 3 * (ie <= 95 ? 0.0009 : 0.0005) * ie * ie;
        return ie switch
        {
            <= 38 => new(45, 80, 95, pertes),
            <= 95 => new(55, 125, 120, pertes),
            <= 150 => new(120, 160, 140, pertes),
            _ => new(180, 200, 190, pertes),
        };
    }

    static Encombrement Variateur(double kw)
    {
        var pertes = 15 + 30 * kw;
        return kw switch
        {
            <= 1.5 => new(75, 230, 175, pertes),
            <= 4 => new(90, 270, 200, pertes),
            <= 7.5 => new(130, 320, 210, pertes),
            <= 15 => new(175, 400, 230, pertes),
            <= 30 => new(230, 550, 260, pertes),
            <= 55 => new(290, 680, 300, pertes),
            _ => new(350, 900, 380, pertes),
        };
    }

    static Encombrement DemarreurProgressif(double kw, double ie)
    {
        // Démarreur à by-pass intégré : les thyristors ne dissipent qu'au démarrage.
        var pertes = 5 + 0.3 * ie;
        return kw switch
        {
            <= 15 => new(55, 160, 150, pertes),
            <= 37 => new(135, 260, 175, pertes),
            <= 75 => new(160, 275, 200, pertes),
            _ => new(200, 350, 250, pertes),
        };
    }

    /// <summary>Rendement moyen des alimentations 24 V à découpage.</summary>
    public const double RendementAlimentation = 0.92;

    static Encombrement Alimentation(double i)
    {
        var pertes = 24 * i * (1 / RendementAlimentation - 1);
        return i switch
        {
            <= 3.8 => new(40, 125, 120, pertes),
            <= 5 => new(50, 125, 125, pertes),
            <= 10 => new(65, 125, 125, pertes),
            <= 20 => new(85, 130, 130, pertes),
            _ => new(160, 130, 135, pertes),
        };
    }

    static Encombrement PorteFusibles(string taille, int poles) => taille switch
    {
        "10×38" => new(18 * poles, 81, 66, 0.5 * poles),
        "14×51" => new(27 * poles, 94, 75, 0.5 * poles),
        "22×58" => new(36 * poles, 120, 80, 0.5 * poles),
        "NH00" => new(35 * poles, 200, 120, 0.8 * poles),
        _ => new(62 * poles, 260, 150, 1.2 * poles),
    };

    /// <summary>Largeur d'une borne selon la section raccordée, en mm.</summary>
    public static double LargeurBorne(double section) => section switch
    {
        <= 4 => 6.2,
        <= 10 => 8.2,
        <= 35 => 12.2,
        _ => 16,
    };

    /// <summary>Largeur d'une borne de commande (2,5 mm²), en mm.</summary>
    public const double LargeurBorneCommande = 5.2;
}
