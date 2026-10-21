using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Dimensionnement.App.Infrastructure;
using Dimensionnement.Core.Normes;
using Microsoft.Win32;

namespace Dimensionnement.App.ModelesVue;

/// <summary>Une ligne du tableau du catalogue : l'article et ses colonnes affichées.</summary>
public class LigneArticle : Observable
{
    readonly FamilleCatalogue _famille;

    public LigneArticle(JsonObject article, FamilleCatalogue famille)
    {
        Article = article;
        _famille = famille;
    }

    public JsonObject Article { get; }
    public IReadOnlyList<string> Colonnes => _famille.Champs.Where(c => c.Colonne).Select(c => BibliothequeCatalogues.Texte(Article, c, false)).ToList();
    public string Reference => BibliothequeCatalogues.Chaine(Article, "reference") is { Length: > 0 } r ? r : "(référence à compléter)";
    public string Marque => BibliothequeCatalogues.Marque(Article);
    public bool Verifie => BibliothequeCatalogues.EstVerifie(Article);
    public string Statut => Verifie ? "vérifié" : "indicatif";
    public string? Vignette => BibliothequeCatalogues.ListeTexte(Article, BibliothequeCatalogues.Photos).FirstOrDefault(File.Exists);

    /// <summary>Remet à jour la ligne du tableau après une saisie dans la fiche.</summary>
    public void Rafraichir() => SignalerTout();

    // Deux lignes du même article sont égales : le tableau garde sa sélection quand il est refait.
    public override bool Equals(object? obj) => obj is LigneArticle l && ReferenceEquals(l.Article, Article);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Article);
}

/// <summary>Un champ de la fiche : sa valeur affichée, modifiable en mode édition.</summary>
public class ChampFiche : Observable
{
    readonly ModeleVueCatalogue _parent;

    public ChampFiche(ModeleVueCatalogue parent, ChampCatalogue champ)
    {
        _parent = parent;
        Champ = champ;
    }

    public ChampCatalogue Champ { get; }
    public string Libelle => Champ.Libelle;
    public string Unite => Champ.Unite;
    public bool Manquant => Valeur.Length == 0;

    public string Valeur
    {
        get => _parent.Article is { } a ? BibliothequeCatalogues.Texte(a, Champ, false) : "";
        set
        {
            if (_parent.Article is not { } a) return;
            Erreur = BibliothequeCatalogues.Saisir(a, Champ, value ?? "");
            if (Erreur is null) _parent.ArticleModifie();
            Signaler();
            Signaler(nameof(Erreur));
            Signaler(nameof(Manquant));
        }
    }

    public string? Erreur { get; private set; }
}

public record ChiffreCle(string Libelle, string Valeur);

/// <summary>
/// Navigateur de catalogue : tableau filtrable de tous les articles d'une famille et fiche technique complète
/// (caractéristiques, encombrement, photos et documents, usage dans le projet, historique), modifiable à la main.
/// Les modifications vont dans la copie du catalogue de l'utilisateur, jamais dans le catalogue livré.
/// </summary>
public class ModeleVueCatalogue : Observable
{
    public const string Toutes = "Toutes";

    static readonly string Dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement");
    static readonly string DossierPhotos = Path.Combine(Dossier, "photos");

    readonly ModeleVuePrincipal _principal;
    FamilleCatalogue _famille;
    List<JsonObject> _articles = new();
    List<LigneArticle> _lignes = new();
    LigneArticle? _selection;
    string _recherche = "";
    string _marque = Toutes;
    string _gamme = Toutes;
    string _minimum = "";
    bool _verifiesSeulement;
    bool _edition;
    bool _modifie;
    string? _photo;

    public ModeleVueCatalogue(ModeleVuePrincipal principal, string idFamille, bool choix = false)
    {
        _principal = principal;
        ModeChoix = choix;
        Retenir = new Commande(() => Retenu?.Invoke(_selection?.Reference ?? ""), () => _selection is not null && choix);
        _famille = BibliothequeCatalogues.Familles.FirstOrDefault(f => f.Id == idFamille) ?? BibliothequeCatalogues.Familles[0];
        Charger();

        Enregistrer = new Commande(EnregistrerCatalogue, () => _modifie);
        Annuler = new Commande(() => { if (ConfirmerAbandon()) Charger(); }, () => _modifie);
        Nouveau = new Commande(NouvelArticle);
        Dupliquer = new Commande(DupliquerArticle, () => Article is not null);
        Supprimer = new Commande(SupprimerArticle, () => Article is not null);
        BasculerEdition = new Commande(() => Edition = !Edition, () => Article is not null);
        AjouterPhoto = new Commande(AjouterPhotoArticle, () => Article is not null);
        RetirerPhoto = new Commande(p => Modifier(a => RetirerDeListe(a, BibliothequeCatalogues.Photos, p as string)), p => p is string && Article is not null);
        ChoisirPhoto = new Commande(p => PhotoCourante = p as string, p => p is string);
        AjouterDocument = new Commande(AjouterDocumentArticle, () => Article is not null);
        RetirerDocument = new Commande(p => Modifier(a => RetirerDeListe(a, BibliothequeCatalogues.Documents, p as string)), p => p is string && Article is not null);
        OuvrirDocument = new Commande(p => Ouvrir(p as string), p => p is string { Length: > 0 });
        OuvrirLien = new Commande(() => Ouvrir(LienFabricant), () => LienFabricant.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        OuvrirFichier = new Commande(OuvrirFichierBrut);
        ChercherEnLigne = new Commande(() => Ouvrir(LienVerification), () => Article is not null);
        ValiderProduit = new Commande(ValiderArticle, () => Article is not null && !Verifie);
        RevenirAuLivre = new Commande(RevenirAuCatalogueLivre, () => File.Exists(CheminUtilisateur));
        Reinitialiser = new Commande(() =>
        {
            _recherche = ""; _marque = Toutes; _gamme = Toutes; _minimum = ""; _verifiesSeulement = false;
            SignalerTout();
        });
    }

    /// <summary>Vrai quand le catalogue sert à choisir un article pour le projet : le bouton « Retenir » renvoie sa référence.</summary>
    public bool ModeChoix { get; }
    public Commande Retenir { get; }
    public event Action<string>? Retenu;

    public Commande Enregistrer { get; }
    public Commande Annuler { get; }
    public Commande Nouveau { get; }
    public Commande Dupliquer { get; }
    public Commande Supprimer { get; }
    public Commande BasculerEdition { get; }
    public Commande AjouterPhoto { get; }
    public Commande RetirerPhoto { get; }
    public Commande ChoisirPhoto { get; }
    public Commande AjouterDocument { get; }
    public Commande RetirerDocument { get; }
    public Commande OuvrirDocument { get; }
    public Commande OuvrirLien { get; }
    public Commande OuvrirFichier { get; }
    public Commande ChercherEnLigne { get; }
    public Commande ValiderProduit { get; }

    /// <summary>
    /// Page à consulter pour contrôler l'article : la page du fabricant, sinon la source des cotes, sinon une recherche
    /// de sa fiche technique.
    /// </summary>
    public string LienVerification => Article is not { } a ? ""
        : new[] { BibliothequeCatalogues.Chaine(a, BibliothequeCatalogues.LienFabricant), BibliothequeCatalogues.Chaine(a, "source") }
              .FirstOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
          ?? "https://www.google.com/search?q=" + Uri.EscapeDataString($"{BibliothequeCatalogues.Marque(a)} {BibliothequeCatalogues.Chaine(a, "reference")} fiche technique".Trim());

    public string LibelleVerification => Article is { } a
        && new[] { BibliothequeCatalogues.Chaine(a, BibliothequeCatalogues.LienFabricant), BibliothequeCatalogues.Chaine(a, "source") }
            .Any(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        ? "Ouvrir la fiche en ligne" : "Chercher la fiche sur internet";

    /// <summary>Ouvre le catalogue sur l'article de cette référence, dans la famille qui la contient.</summary>
    public bool SelectionnerReference(string reference)
    {
        foreach (var famille in BibliothequeCatalogues.Familles.OrderBy(f => f.Id == _famille.Id ? 0 : 1))
        {
            if (famille.Id != _famille.Id)
            {
                if (!BibliothequeCatalogues.Charger(famille, Path.Combine(Dossier, famille.NomFichier))
                        .Any(a => BibliothequeCatalogues.Chaine(a, "reference").Equals(reference, StringComparison.OrdinalIgnoreCase))) continue;
                Famille = famille;
            }
            _recherche = ""; _marque = Toutes; _gamme = Toutes; _minimum = ""; _verifiesSeulement = false;
            SignalerFiltre();
            if (Lignes.FirstOrDefault(l => l.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase)) is { } ligne)
            {
                Selection = ligne;
                return true;
            }
        }
        return false;
    }

    /// <summary>Passe l'article en « vérifié » et enregistre aussitôt le catalogue, après confirmation.</summary>
    void ValiderArticle()
    {
        if (MessageBox.Show($"Les valeurs de {Reference} ont-elles été contrôlées sur la fiche du fabricant ?\n\n" +
                            "L'article passera de « indicatif » à « vérifié », pour ce projet et les suivants.",
                "Valider le produit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Verifie = true;
        EnregistrerCatalogue();
    }
    public Commande RevenirAuLivre { get; }
    public Commande Reinitialiser { get; }

    public IReadOnlyList<FamilleCatalogue> Familles => BibliothequeCatalogues.Familles;

    public FamilleCatalogue Famille
    {
        get => _famille;
        set
        {
            if (value is null || value == _famille || !ConfirmerAbandon()) { Signaler(); return; }
            _famille = value;
            _marque = Toutes;
            _gamme = Toutes;
            _minimum = "";
            Charger();
        }
    }

    string CheminUtilisateur => Path.Combine(Dossier, _famille.NomFichier);

    public string Origine => File.Exists(CheminUtilisateur)
        ? "Catalogue personnel (copie modifiée, conservée lors des mises à jour)"
        : "Catalogue livré avec l'application";

    /// <summary>Colonnes du tableau, dans l'ordre des champs de la famille.</summary>
    public IReadOnlyList<ChampCatalogue> Colonnes => _famille.Champs.Where(c => c.Colonne).ToList();

    // Filtres
    public string Recherche { get => _recherche; set { _recherche = value ?? ""; SignalerFiltre(); } }
    public string Marque { get => _marque; set { _marque = value ?? Toutes; SignalerFiltre(); } }
    public string Gamme { get => _gamme; set { _gamme = value ?? Toutes; SignalerFiltre(); } }
    public string Minimum { get => _minimum; set { _minimum = value ?? ""; SignalerFiltre(); } }
    public bool VerifiesSeulement { get => _verifiesSeulement; set { _verifiesSeulement = value; SignalerFiltre(); } }

    public IReadOnlyList<string> Marques => _articles.Select(BibliothequeCatalogues.Marque).Where(m => m.Length > 0)
        .Distinct().Order().Prepend(Toutes).ToList();

    public IReadOnlyList<string> Gammes => _articles
        .Where(a => _marque == Toutes || BibliothequeCatalogues.Marque(a) == _marque)
        .Select(a => BibliothequeCatalogues.Chaine(a, _famille.ChampGamme)).Where(g => g.Length > 0)
        .Distinct().Order().Prepend(Toutes).ToList();

    public string LibelleGamme => _famille.Champ(_famille.ChampGamme)?.Libelle ?? "Gamme";

    public string LibelleMinimum => _famille.ChampPrincipal is { } p && _famille.Champ(p) is { } c
        ? $"{c.Libelle} ≥{(c.Unite.Length > 0 ? $" ({c.Unite})" : "")}"
        : "";

    public IReadOnlyList<LigneArticle> Lignes => _lignes;

    List<LigneArticle> Filtrer()
    {
        {
            double? minimum = double.TryParse(_minimum.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : null;
            return BibliothequeCatalogues.Filtrer(_famille, _articles, _recherche,
                    _marque == Toutes ? null : _marque, _gamme == Toutes ? null : _gamme, minimum, _verifiesSeulement)
                .Select(a => new LigneArticle(a, _famille)).ToList();
        }
    }

    public string Compte => $"{Lignes.Count} article(s) sur {_articles.Count}";

    public LigneArticle? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            _photo = null;
            SignalerFiche();
        }
    }

    public JsonObject? Article => _selection?.Article;
    public bool AArticle => Article is not null;

    /// <summary>Mode édition : les champs de la fiche deviennent modifiables.</summary>
    public bool Edition { get => _edition; set { _edition = value; Signaler(); Signaler(nameof(Lecture)); } }
    public bool Lecture => !_edition;
    public bool Modifie => _modifie;

    // Fiche
    public string Titre => Article is { } a
        ? new[] { "designation", "cpu" }.Select(k => BibliothequeCatalogues.Chaine(a, k)).FirstOrDefault(t => t.Length > 0)
          ?? $"{_famille.Designation} {BibliothequeCatalogues.Chaine(a, "gamme")}".Trim()
        : "";
    public string Reference => _selection?.Reference ?? "";
    public string SousTitre => Article is { } a
        ? string.Join(" · ", new[] { BibliothequeCatalogues.Marque(a), BibliothequeCatalogues.Chaine(a, "gamme") }.Where(t => t.Length > 0))
        : "";

    public IReadOnlyList<ChiffreCle> ChiffresCles => Article is { } a
        ? _famille.Champs.Where(c => c.Cle_).Select(c => new ChiffreCle(c.Libelle.ToUpperInvariant(), BibliothequeCatalogues.Texte(a, c) is { Length: > 0 } t ? t : "à compléter"))
            .Take(4).ToList()
        : Array.Empty<ChiffreCle>();

    public IReadOnlyList<ChampFiche> Champs => Article is null
        ? Array.Empty<ChampFiche>()
        : _famille.Champs.Where(c => !c.Cote).Select(c => new ChampFiche(this, c)).ToList();

    public IReadOnlyList<ChampFiche> ChampsCotes => Article is null
        ? Array.Empty<ChampFiche>()
        : _famille.Champs.Where(c => c.Cote).Select(c => new ChampFiche(this, c)).ToList();

    public double LargeurCote => Article is { } a ? BibliothequeCatalogues.Nombre(a, "largeur") ?? 0 : 0;
    public double HauteurCote => Article is { } a ? BibliothequeCatalogues.Nombre(a, "hauteur") ?? 0 : 0;
    public double ProfondeurCote => Article is { } a ? BibliothequeCatalogues.Nombre(a, "profondeur") ?? 0 : 0;
    public bool ACotes => LargeurCote > 0 && HauteurCote > 0;
    public string ResumeCotes => ACotes
        ? $"{LargeurCote:0.#} × {HauteurCote:0.#}{(ProfondeurCote > 0 ? $" × {ProfondeurCote:0.#}" : "")} mm (L × H{(ProfondeurCote > 0 ? " × P" : "")})"
        : "Encombrement à compléter : clique sur « Modifier » et saisis les cotes manquantes (largeur, hauteur, profondeur).";

    public IReadOnlyList<string> Photos => Article is { } a ? BibliothequeCatalogues.ListeTexte(a, BibliothequeCatalogues.Photos) : Array.Empty<string>();
    public string? PhotoCourante
    {
        get => _photo ?? Photos.FirstOrDefault(File.Exists);
        set { _photo = value; Signaler(); }
    }
    public bool APhoto => PhotoCourante is not null;

    public IReadOnlyList<string> DocumentsArticle => Article is { } a ? BibliothequeCatalogues.ListeTexte(a, BibliothequeCatalogues.Documents) : Array.Empty<string>();

    public string LienFabricant
    {
        get => Article is { } a ? BibliothequeCatalogues.Chaine(a, BibliothequeCatalogues.LienFabricant) : "";
        set => Modifier(a => a[BibliothequeCatalogues.LienFabricant] = (value ?? "").Trim());
    }

    public string NotesArticle
    {
        get => Article is { } a ? BibliothequeCatalogues.Chaine(a, BibliothequeCatalogues.Notes) : "";
        set => Modifier(a => a[BibliothequeCatalogues.Notes] = value ?? "");
    }

    public bool Verifie
    {
        get => Article is { } a && BibliothequeCatalogues.EstVerifie(a);
        set => Modifier(a => a[BibliothequeCatalogues.Verifie] = value);
    }

    public IReadOnlyList<string> Points => Article is { } a ? BibliothequeCatalogues.Controler(_famille, a, _articles) : Array.Empty<string>();

    /// <summary>Repères du projet ouvert qui utilisent cette référence.</summary>
    public IReadOnlyList<string> DansLeProjet
    {
        get
        {
            var reference = Article is { } a ? BibliothequeCatalogues.Chaine(a, "reference") : "";
            if (reference.Length == 0) return Array.Empty<string>();
            return _principal.Resultat.Arbre.Parcourir()
                .Where(e => e.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase))
                .Select(e => $"{e.Repere} – {e.Designation}{(e.Calibre.Length > 0 ? $" ({e.Calibre})" : "")}")
                .ToList();
        }
    }

    public string Historique => Article is { } a && BibliothequeCatalogues.Chaine(a, BibliothequeCatalogues.ModifieLe) is { Length: > 0 } le
        ? $"Modifié le {le} par {BibliothequeCatalogues.Chaine(a, BibliothequeCatalogues.ModifiePar)}."
        : "Valeurs du catalogue livré, jamais modifiées.";

    /// <summary>Appelé quand un champ de la fiche change : l'article est tracé et le catalogue marqué modifié.</summary>
    public void ArticleModifie()
    {
        if (Article is { } a) BibliothequeCatalogues.Tracer(a, Environment.UserName);
        _selection?.Rafraichir();
        _modifie = true;
        Signaler(nameof(Modifie));
        Signaler(nameof(Points));
        Signaler(nameof(ChiffresCles));
        Signaler(nameof(Historique));
        Signaler(nameof(LargeurCote));
        Signaler(nameof(HauteurCote));
        Signaler(nameof(ProfondeurCote));
        Signaler(nameof(ACotes));
        Signaler(nameof(ResumeCotes));
        Signaler(nameof(Titre));
        Signaler(nameof(Reference));
        Signaler(nameof(SousTitre));
    }

    void Modifier(Action<JsonObject> action)
    {
        if (Article is not { } a) return;
        action(a);
        ArticleModifie();
        SignalerFiche();
    }

    void Charger()
    {
        _articles = BibliothequeCatalogues.Charger(_famille, CheminUtilisateur);
        _modifie = false;
        _edition = false;
        _lignes = Filtrer();
        _selection = _lignes.FirstOrDefault();
        SignalerTout();
    }

    void SignalerFiltre()
    {
        _lignes = Filtrer();
        // La fiche reste sur l'article affiché tant qu'il passe le filtre, sinon elle passe au premier.
        var garde = _lignes.FirstOrDefault(l => l.Equals(_selection));
        if (garde is null && _selection is not null || garde is not null && !ReferenceEquals(garde, _selection))
        {
            if (garde is null) _photo = null;
            _selection = garde ?? _lignes.FirstOrDefault();
            SignalerFiche();
        }
        Signaler(nameof(Lignes));
        Signaler(nameof(Compte));
        Signaler(nameof(Gammes));
        Signaler(nameof(Recherche));
        Signaler(nameof(Marque));
        Signaler(nameof(Gamme));
        Signaler(nameof(Minimum));
        Signaler(nameof(VerifiesSeulement));
    }

    void SignalerFiche()
    {
        foreach (var p in new[] { nameof(Selection), nameof(Article), nameof(AArticle), nameof(Titre), nameof(Reference), nameof(SousTitre),
                     nameof(ChiffresCles), nameof(Champs), nameof(ChampsCotes), nameof(LargeurCote), nameof(HauteurCote), nameof(ProfondeurCote),
                     nameof(ACotes), nameof(ResumeCotes), nameof(Photos), nameof(PhotoCourante), nameof(APhoto), nameof(DocumentsArticle),
                     nameof(LienFabricant), nameof(LienVerification), nameof(LibelleVerification), nameof(NotesArticle), nameof(Verifie), nameof(Points), nameof(DansLeProjet), nameof(Historique),
                     nameof(Modifie) })
            Signaler(p);
    }

    /// <summary>Après un ajout ou une suppression : le tableau est refait et la ligne de l'article sélectionnée.</summary>
    void Selectionner(JsonObject? article)
    {
        SignalerFiltre();
        _selection = Lignes.FirstOrDefault(l => ReferenceEquals(l.Article, article)) ?? Lignes.FirstOrDefault();
        _photo = null;
        SignalerFiche();
    }

    void NouvelArticle()
    {
        var article = new JsonObject();
        if (_marque != Toutes) article[_famille.Champs.Any(c => c.Cle == "marque") ? "marque" : "fabricant"] = _marque;
        if (_gamme != Toutes) article[_famille.ChampGamme] = _gamme;
        article[BibliothequeCatalogues.Verifie] = false;
        BibliothequeCatalogues.Tracer(article, Environment.UserName);
        _articles.Insert(0, article);
        _recherche = "";
        _minimum = "";
        _verifiesSeulement = false;
        _modifie = true;
        Edition = true;
        Selectionner(article);
    }

    void DupliquerArticle()
    {
        if (Article is not { } a) return;
        var copie = (JsonObject)a.DeepClone();
        copie["reference"] = "";
        copie[BibliothequeCatalogues.Verifie] = false;
        BibliothequeCatalogues.Tracer(copie, Environment.UserName);
        _articles.Insert(_articles.IndexOf(a) + 1, copie);
        _modifie = true;
        Edition = true;
        Selectionner(copie);
    }

    void SupprimerArticle()
    {
        if (Article is not { } a) return;
        if (MessageBox.Show($"Retirer « {Reference} » du catalogue ?", "Catalogue", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        _articles.Remove(a);
        _modifie = true;
        Selectionner(null);
    }

    void AjouterPhotoArticle()
    {
        var dialogue = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp", Multiselect = true };
        if (dialogue.ShowDialog() != true) return;
        try
        {
            Directory.CreateDirectory(DossierPhotos);
            var copies = dialogue.FileNames.Select(f =>
            {
                // Copie dans le dossier de l'application : la photo reste disponible si l'original est déplacé.
                var cible = Path.Combine(DossierPhotos, $"{Guid.NewGuid():N}{Path.GetExtension(f)}");
                File.Copy(f, cible);
                return cible;
            }).ToList();
            Modifier(a => Ajouter(a, BibliothequeCatalogues.Photos, copies));
            PhotoCourante = copies.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Impossible d'ajouter la photo :\n{ex.Message}", "Catalogue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void AjouterDocumentArticle()
    {
        var dialogue = new OpenFileDialog { Filter = "Documents (*.pdf;*.dxf;*.dwg;*.step;*.stp;*.zip)|*.pdf;*.dxf;*.dwg;*.step;*.stp;*.zip|Tous les fichiers|*.*", Multiselect = true };
        if (dialogue.ShowDialog() != true) return;
        Modifier(a => Ajouter(a, BibliothequeCatalogues.Documents, dialogue.FileNames));
    }

    static void Ajouter(JsonObject a, string cle, IEnumerable<string> valeurs)
    {
        var liste = BibliothequeCatalogues.ListeTexte(a, cle).Concat(valeurs).Distinct().Select(v => (JsonNode)JsonValue.Create(v)!).ToArray();
        a[cle] = new JsonArray(liste);
    }

    static void RetirerDeListe(JsonObject a, string cle, string? valeur)
    {
        var liste = BibliothequeCatalogues.ListeTexte(a, cle).Where(v => v != valeur).Select(v => (JsonNode)JsonValue.Create(v)!).ToArray();
        a[cle] = new JsonArray(liste);
    }

    static void Ouvrir(string? cible)
    {
        if (string.IsNullOrWhiteSpace(cible)) return;
        try
        {
            Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Impossible d'ouvrir « {cible} » :\n{ex.Message}", "Catalogue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void EnregistrerCatalogue()
    {
        try
        {
            BibliothequeCatalogues.Enregistrer(_famille, CheminUtilisateur, _articles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show($"Le catalogue n'a pas été enregistré :\n{ex.Message}", "Catalogue", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _modifie = false;
        Edition = false;
        _principal.RechargerCatalogues();
        SignalerTout();
    }

    void RevenirAuCatalogueLivre()
    {
        if (MessageBox.Show("Abandonner toutes vos modifications de ce catalogue et revenir au catalogue livré ?", "Catalogue",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(CheminUtilisateur);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Impossible de supprimer le catalogue personnel :\n{ex.Message}", "Catalogue", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Charger();
        _principal.RechargerCatalogues();
    }

    void OuvrirFichierBrut()
    {
        try
        {
            if (!File.Exists(CheminUtilisateur))
            {
                Directory.CreateDirectory(Dossier);
                File.WriteAllText(CheminUtilisateur, _famille.TexteEmbarque());
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{CheminUtilisateur}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Impossible d'ouvrir le fichier :\n{ex.Message}", "Catalogue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Demande confirmation avant de perdre des modifications non enregistrées.</summary>
    public bool ConfirmerAbandon()
    {
        if (!_modifie) return true;
        var reponse = MessageBox.Show("Le catalogue a été modifié. Enregistrer les modifications ?", "Catalogue",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (reponse == MessageBoxResult.Yes) EnregistrerCatalogue();
        if (reponse == MessageBoxResult.No) _modifie = false;
        return reponse != MessageBoxResult.Cancel && !_modifie;
    }
}
