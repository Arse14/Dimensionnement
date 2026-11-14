using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Dimensionnement.Core.Calculs;

/// <summary>Un morceau de formule mise en forme.</summary>
public abstract record NoeudMath;

/// <summary>
/// Texte simple : <see cref="Math"/> faux pour la prose (police normale) ; vrai pour une formule, avec la grandeur en italique
/// et son indice en bas (I<sub>b</sub>).
/// </summary>
/// <see cref="Grand"/> : parenthèse ou barre agrandie autour d'une fraction.
public sealed record MorceauMath(string Texte, bool Math = false, bool Italique = false, string Indice = "", bool Grand = false) : NoeudMath;

/// <summary>Fraction superposée : numérateur au-dessus de la barre, dénominateur en dessous.</summary>
public sealed record FractionMath(IReadOnlyList<NoeudMath> Numerateur, IReadOnlyList<NoeudMath> Denominateur) : NoeudMath;

/// <summary>Racine carrée, la barre couvrant tout son contenu.</summary>
public sealed record RacineMath(IReadOnlyList<NoeudMath> Contenu) : NoeudMath;

/// <summary>
/// Écriture mathématique des formules des notes de calcul, du glossaire et des explications : les textes restent écrits en ligne
/// (« I = P / (√3 × U × cos φ × η) ») et sont mis en forme à l'affichage, comme sur papier : divisions en fractions superposées,
/// racine avec sa barre, indices en bas (Ib → I<sub>b</sub>), grandeurs en italique, unités et fonctions droites, « × » entre
/// lettres remplacé par un point. Seuls les passages qui sont réellement des formules (une grandeur et un opérateur) sont mis
/// en forme : la prose autour, les références (« 230 V AC / 24 V DC ») et les repères (Q1, KM2) restent tels quels.
/// Un indice long s'écrit explicitement : « I_{n,plaque} ».
/// </summary>
public static class EcritureMath
{
    enum Sorte { Espace, Nombre, Unite, Mot, Op, Ouvre, Ferme, Barre, Postfixe, Autre }

    sealed record Jeton(Sorte Sorte, string Texte, string Indice = "");

    // Grandeurs indicées reconnues sans écriture explicite (« Ib », « K1 »…). Les repères d'appareils (Q1, KM1) n'y sont pas.
    static readonly HashSet<string> Symboles = new(StringComparer.Ordinal)
    {
        "Ib", "In", "Iz", "Iz0", "Ir", "Ie", "Ia", "Id", "Im", "Ith", "Icc", "Icu", "Icn", "Ics", "Ik", "Isd", "Ip",
        "Ueff", "Udc", "Un", "U0", "Pn", "Pabs", "Pu", "Ph", "Qk", "Ae", "Ti", "Ta", "Tmax", "K1", "K2", "K3", "ku", "ks", "ρ1",
    };

    // Symboles qui sont aussi des mots français (« Ta disposition ») : mis en forme seulement au milieu d'une formule.
    static readonly HashSet<string> Ambigus = new(StringComparer.Ordinal) { "Ta", "Un", "Pu" };

    static readonly HashSet<string> Fonctions = new(StringComparer.Ordinal)
        { "cos", "sin", "tan", "arccos", "arcsin", "arctan", "max", "min", "ln", "log", "exp", "f" };

    const string Grecques = "αβγδεζηθλμνξρστφχψω";

    static readonly Regex Nombre = new(@"\G\d+(?:[   ]\d{3}(?!\d))*(?:,\d+)?", RegexOptions.Compiled);

    static readonly Regex Unite = new(
        @"\G([   ]?)(°C|%|N·m|m³/h|W/m²|W/K|A/m|tr/min|mm²|m²|mm|cm|km|kVA|kvar|kW|kA|kV|mA|mV|mΩ|VA|var|Hz|ms|min|°|Ω|K|W|A|V|m|s|h|g)(?![\p{L}\d])",
        RegexOptions.Compiled);

    static readonly Regex Mot = new(@"\G\p{L}[\p{L}\d]*", RegexOptions.Compiled);

    /// <summary>Le texte sans écriture explicite d'indice, pour l'export Excel et les recherches (« I_{n,plaque} » → « In plaque »).</summary>
    public static string Texte(string texte) =>
        Regex.Replace(Regex.Replace(texte, @"_\{([^}]*)\}", m => m.Groups[1].Value.Replace(",", " ")), @"_([\p{L}\d]+)", "$1");

    /// <summary>Découpe et met en forme un texte. <paramref name="formule"/> : le texte entier est une formule (symbole du glossaire).</summary>
    public static IReadOnlyList<NoeudMath> Analyser(string texte, bool formule = false)
    {
        var resultat = new List<NoeudMath>();
        if (string.IsNullOrEmpty(texte)) return resultat;
        var jetons = Decouper(texte);
        var i = 0;
        while (i < jetons.Count)
        {
            if (!Calculable(jetons[i]))
            {
                resultat.Add(new MorceauMath(jetons[i].Texte));
                i++;
                continue;
            }
            var debut = i;
            while (i < jetons.Count && Calculable(jetons[i])) i++;
            var passage = jetons.GetRange(debut, i - debut);
            var avant = passage.TakeWhile(j => j.Sorte == Sorte.Espace).ToList();
            if (avant.Count == passage.Count)
            {
                resultat.AddRange(avant.Select(j => new MorceauMath(j.Texte)));
                continue;
            }
            var apres = passage.AsEnumerable().Reverse().TakeWhile(j => j.Sorte == Sorte.Espace).Reverse().ToList();
            var coeur = passage.GetRange(avant.Count, passage.Count - avant.Count - apres.Count);
            resultat.AddRange(avant.Select(j => new MorceauMath(j.Texte)));
            if (formule || EstFormule(coeur)) resultat.AddRange(Composer(Structurer(coeur)));
            else resultat.AddRange(coeur.SelectMany(Isole));
            resultat.AddRange(apres.Select(j => new MorceauMath(j.Texte)));
        }
        return Fusionner(resultat);
    }

    static List<Jeton> Decouper(string s)
    {
        var jetons = new List<Jeton>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is ' ' or ' ' or ' ' or ' ' or '\t' or '\n')
            {
                var d = i;
                while (i < s.Length && s[i] is ' ' or ' ' or ' ' or ' ' or '\t' or '\n') i++;
                jetons.Add(new Jeton(Sorte.Espace, s[d..i]));
            }
            else if (char.IsDigit(c))
            {
                var m = Nombre.Match(s, i);
                jetons.Add(new Jeton(Sorte.Nombre, m.Value));
                i += m.Length;
                var u = Unite.Match(s, i);
                if (u.Success)
                {
                    if (u.Groups[1].Length > 0) jetons.Add(new Jeton(Sorte.Espace, u.Groups[1].Value));
                    jetons.Add(new Jeton(Sorte.Unite, u.Groups[2].Value));
                    i += u.Length;
                }
            }
            else if (char.IsLetter(c))
            {
                var m = Mot.Match(s, i);
                i += m.Length;
                var indice = "";
                if (i + 1 < s.Length && s[i] == '_')
                {
                    if (s[i + 1] == '{' && s.IndexOf('}', i) is var fin and > 0)
                    {
                        indice = s[(i + 2)..fin];
                        i = fin + 1;
                    }
                    else if (Regex.Match(s[(i + 1)..], @"^[\p{L}\d]+") is { Success: true } v)
                    {
                        indice = v.Value;
                        i += 1 + v.Length;
                    }
                }
                jetons.Add(new Jeton(Sorte.Mot, m.Value, indice));
            }
            else if (string.CompareOrdinal(s, i, "(s)", 0, 3) == 0 && i > 0 && char.IsLetter(s[i - 1]))
            {
                // Pluriel facultatif : « circuit(s) ».
                jetons.Add(new Jeton(Sorte.Autre, "(s)"));
                i += 3;
            }
            else
            {
                var precedent = i > 0 ? s[i - 1] : ' ';
                var suivant = i + 1 < s.Length ? s[i + 1] : ' ';
                var sorte = c switch
                {
                    '=' or '≥' or '≤' or '<' or '>' or '≈' or '≠' or '×' or '·' or '+' or '−' or '±' or '√' => Sorte.Op,
                    '-' when precedent == ' ' && suivant == ' ' => Sorte.Op,
                    '/' when (precedent is ' ' or ')') && (suivant is ' ' or '(' or '√') => Sorte.Op,
                    '(' or '[' => Sorte.Ouvre,
                    ')' or ']' => Sorte.Ferme,
                    '|' => Sorte.Barre,
                    '²' or '³' or '′' => Sorte.Postfixe,
                    '\'' when jetons.Count > 0 && jetons[^1].Sorte == Sorte.Mot && !char.IsLetter(suivant) => Sorte.Postfixe,
                    _ => Sorte.Autre,
                };
                jetons.Add(new Jeton(sorte, c == '-' && sorte == Sorte.Op ? "−" : c.ToString()));
                i++;
            }
        }
        return jetons;
    }

    static bool EstFonction(Jeton j) => j.Sorte == Sorte.Mot && j.Indice.Length == 0 && Fonctions.Contains(j.Texte);

    static bool EstSymbole(Jeton j) => j.Sorte == Sorte.Mot && (j.Indice.Length > 0 || Symboles.Contains(Corps(j.Texte)));

    static bool EstVariable(Jeton j)
    {
        if (j.Sorte != Sorte.Mot || j.Indice.Length > 0) return false;
        var t = Corps(j.Texte);
        return t.Length == 1 && (char.IsAsciiLetter(t[0]) && t != "a" && t != "y" || Grecques.Contains(t[0]));
    }

    static bool EstOperateurGrec(Jeton j) => j.Sorte == Sorte.Mot && j.Texte is "Δ" or "Σ" or "π";

    // « ΔU », « ΣP » : le préfixe opérateur et la grandeur.
    static string Prefixe(string mot) => mot.Length > 1 && mot[0] is 'Δ' or 'Σ' ? mot[..1] : "";
    static string Corps(string mot) => mot[Prefixe(mot).Length..];

    static bool Calculable(Jeton j) => j.Sorte switch
    {
        Sorte.Autre => false,
        Sorte.Mot => EstFonction(j) || EstSymbole(j) || EstVariable(j) || EstOperateurGrec(j),
        _ => true,
    };

    // Une formule relie des grandeurs (=, ≥…), divise ou prend une racine : « (L × H × P) » seul reste une notation de cotes.
    static bool EstFormule(List<Jeton> passage) =>
        passage.Any(j => j.Sorte == Sorte.Op && j.Texte is "=" or "≥" or "≤" or "<" or ">" or "≈" or "≠" or "/" or "√")
        && passage.Any(j => EstSymbole(j) || EstVariable(j));

    // Un jeton hors formule : seules les grandeurs indicées sans ambiguïté et les lettres grecques sont mises en forme.
    static IEnumerable<NoeudMath> Isole(Jeton j) =>
        EstSymbole(j) && (j.Indice.Length > 0 || !Ambigus.Contains(j.Texte)) || EstVariable(j) && (Grecques.Contains(j.Texte[0]) || Prefixe(j.Texte).Length > 0)
            ? MotMath(j)
            : new[] { new MorceauMath(j.Texte) };

    // ---- Structure : groupes entre parenthèses ou barres, racines, nombres avec unité, puis fractions ----

    sealed record Groupe(Jeton Ouvre, List<object> Contenu, Jeton Ferme);
    sealed record Racine(List<object> Contenu);
    sealed record Fraction(List<object> Haut, List<object> Bas);
    sealed record Bloc(List<object> Parties);

    static List<object> Structurer(List<Jeton> jetons)
    {
        var pile = new Stack<(Jeton? Ouvre, List<object> Contenu)>();
        pile.Push((null, new List<object>()));
        foreach (var j in jetons)
        {
            if (j.Sorte == Sorte.Ouvre || j.Sorte == Sorte.Barre && !(pile.Peek().Ouvre is { Sorte: Sorte.Barre }))
                pile.Push((j, new List<object>()));
            else if ((j.Sorte == Sorte.Ferme && pile.Peek().Ouvre is { Sorte: Sorte.Ouvre })
                     || (j.Sorte == Sorte.Barre && pile.Peek().Ouvre is { Sorte: Sorte.Barre }))
            {
                var (ouvre, contenu) = pile.Pop();
                pile.Peek().Contenu.Add(new Groupe(ouvre!, Assembler(contenu), j));
            }
            else pile.Peek().Contenu.Add(j);
        }
        // Parenthèses non refermées : leur contenu reste à plat.
        while (pile.Count > 1)
        {
            var (ouvre, contenu) = pile.Pop();
            pile.Peek().Contenu.Add(ouvre!);
            pile.Peek().Contenu.AddRange(contenu);
        }
        return Assembler(pile.Pop().Contenu);
    }

    static bool EstOp(object o, string texte) => o is Jeton { Sorte: Sorte.Op } j && j.Texte == texte;
    static bool EstEspace(object o) => o is Jeton { Sorte: Sorte.Espace };

    static bool EstOperande(object o) => o switch
    {
        Jeton j => j.Sorte is Sorte.Nombre or Sorte.Mot,
        Groupe or Racine or Fraction or Bloc => true,
        _ => false,
    };

    static List<object> Assembler(List<object> elements)
    {
        // Racines : √ suivi d'un nombre, d'une grandeur ou d'un groupe.
        var l = new List<object>();
        for (var i = 0; i < elements.Count; i++)
        {
            if (EstOp(elements[i], "√") && i + 1 < elements.Count && EstOperande(elements[i + 1]))
            {
                var suite = elements[++i];
                l.Add(new Racine(suite is Groupe { Ouvre.Sorte: Sorte.Ouvre } g ? g.Contenu : new List<object> { suite }));
            }
            else l.Add(elements[i]);
        }

        // Blocs : préfixe Δ / Σ et sa grandeur, nombre et son unité, exposants (², ³, ′).
        var b = new List<object>();
        for (var i = 0; i < l.Count; i++)
        {
            var parties = new List<object> { l[i] };
            if (l[i] is Jeton { Sorte: Sorte.Mot } m && m.Texte is "Δ" or "Σ" && i + 1 < l.Count && EstOperande(l[i + 1]))
                parties.Add(l[++i]);
            if (l[i] is Jeton { Sorte: Sorte.Nombre } && i + 2 < l.Count && EstEspace(l[i + 1]) && l[i + 2] is Jeton { Sorte: Sorte.Unite })
            {
                parties.Add(l[++i]);
                parties.Add(l[++i]);
            }
            else if (l[i] is Jeton { Sorte: Sorte.Nombre } && i + 1 < l.Count && l[i + 1] is Jeton { Sorte: Sorte.Unite })
                parties.Add(l[++i]);
            while (i + 1 < l.Count && l[i + 1] is Jeton { Sorte: Sorte.Postfixe }) parties.Add(l[++i]);
            b.Add(parties.Count == 1 ? parties[0] : new Bloc(parties));
        }

        // Fractions : l'opérande juste avant « / » au-dessus, celle juste après en dessous ; les parenthèses qui les entourent tombent.
        for (var i = 0; i < b.Count; i++)
        {
            if (!EstOp(b[i], "/")) continue;
            var g = i - 1;
            while (g >= 0 && EstEspace(b[g])) g--;
            var d = i + 1;
            while (d < b.Count && EstEspace(b[d])) d++;
            if (g < 0 || d >= b.Count || !EstOperande(b[g]) || !EstOperande(b[d])) continue;
            // Un groupe précédé d'une fonction (« max(…) ») est son argument : il garde ses parenthèses.
            var fonction = g > 0 && b[g - 1] is Jeton f && EstFonction(f);
            var fraction = new Fraction(fonction ? new List<object> { b[g - 1], b[g] } : Deballer(b[g]), Deballer(b[d]));
            var debut = fonction ? g - 1 : g;
            b.RemoveRange(debut, d - debut + 1);
            b.Insert(debut, fraction);
            i = debut;
        }
        return b;
    }

    static List<object> Deballer(object o) => o is Groupe { Ouvre.Sorte: Sorte.Ouvre } g ? g.Contenu : new List<object> { o };

    // ---- Mise en forme ----

    static List<NoeudMath> Composer(List<object> elements)
    {
        var noeuds = new List<NoeudMath>();
        for (var i = 0; i < elements.Count; i++)
        {
            switch (elements[i])
            {
                case Jeton { Sorte: Sorte.Op, Texte: "×" }:
                    noeuds.Add(new MorceauMath(Numerique(Voisin(elements, i, -1)) && Numerique(Voisin(elements, i, 1)) ? "×" : "·", true));
                    break;
                case Jeton { Sorte: Sorte.Mot } j:
                    noeuds.AddRange(MotMath(j));
                    break;
                case Jeton { Sorte: Sorte.Espace } e:
                    noeuds.Add(new MorceauMath(e.Texte.Contains('\n') ? e.Texte : " ", true));
                    break;
                case Jeton j:
                    noeuds.Add(new MorceauMath(j.Texte, true));
                    break;
                case Groupe g:
                    var grand = g.Contenu.Any(x => x is Fraction || x is Bloc b && b.Parties.Any(y => y is Fraction));
                    noeuds.Add(new MorceauMath(g.Ouvre.Texte, true, Grand: grand));
                    noeuds.AddRange(Composer(g.Contenu));
                    noeuds.Add(new MorceauMath(g.Ferme.Texte, true, Grand: grand));
                    break;
                case Racine r:
                    noeuds.Add(new RacineMath(Fusionner(Composer(r.Contenu))));
                    break;
                case Fraction f:
                    noeuds.Add(new FractionMath(Fusionner(Composer(Rogner(f.Haut))), Fusionner(Composer(Rogner(f.Bas)))));
                    break;
                case Bloc b:
                    noeuds.AddRange(Composer(b.Parties));
                    break;
            }
        }
        return noeuds;
    }

    static IEnumerable<NoeudMath> MotMath(Jeton j)
    {
        if (EstFonction(j) || EstOperateurGrec(j) || !(EstSymbole(j) || EstVariable(j)))
            return new[] { new MorceauMath(j.Texte, true) };
        var prefixe = Prefixe(j.Texte);
        var corps = Corps(j.Texte);
        var liste = new List<NoeudMath>();
        if (prefixe.Length > 0) liste.Add(new MorceauMath(prefixe, true));
        if (j.Indice.Length > 0) liste.Add(new MorceauMath(corps, true, true, j.Indice));
        else if (corps.Length == 1) liste.Add(new MorceauMath(corps, true, true));
        else liste.Add(new MorceauMath(corps[..1], true, true, corps[1..]));
        return liste;
    }

    static object? Voisin(List<object> elements, int i, int sens)
    {
        for (var k = i + sens; k >= 0 && k < elements.Count; k += sens)
            if (!EstEspace(elements[k])) return elements[k];
        return null;
    }

    static bool Numerique(object? o) => o switch
    {
        Jeton j => j.Sorte == Sorte.Nombre,
        Bloc b => b.Parties[0] is Jeton { Sorte: Sorte.Nombre },
        Groupe g => g.Contenu.All(x => EstEspace(x) || Numerique(x) || x is Jeton { Sorte: Sorte.Op }),
        _ => false,
    };

    static List<object> Rogner(List<object> l)
    {
        var debut = 0;
        var fin = l.Count;
        while (debut < fin && EstEspace(l[debut])) debut++;
        while (fin > debut && EstEspace(l[fin - 1])) fin--;
        return l.GetRange(debut, fin - debut);
    }

    static List<NoeudMath> Fusionner(List<NoeudMath> noeuds)
    {
        var resultat = new List<NoeudMath>();
        foreach (var n in noeuds)
        {
            if (n is MorceauMath m && resultat.Count > 0 && resultat[^1] is MorceauMath p
                && p.Math == m.Math && p.Italique == m.Italique && p.Indice.Length == 0 && m.Indice.Length == 0 && !p.Grand && !m.Grand)
                resultat[^1] = p with { Texte = p.Texte + m.Texte };
            else resultat.Add(n);
        }
        return resultat;
    }

    // ---- Rendu HTML (rapport) ----

    /// <summary>Feuille de style des formules, à inclure dans la page.</summary>
    public const string StyleHtml =
        ".m { font-family: 'Cambria Math', Cambria, 'STIX Two Math', 'Times New Roman', serif; font-size: 1.12em; white-space: nowrap; }" +
        " .m i { font-style: italic; } .m sub { font-size: .7em; font-style: normal; vertical-align: -.3em; line-height: 0; }" +
        " .fr { display: inline-flex; flex-direction: column; align-items: center; vertical-align: middle; margin: 1px 2px; line-height: 1.15; }" +
        " .fr > span:first-child { padding: 0 3px 1px; border-bottom: 1px solid currentColor; } .fr > span:last-child { padding: 1px 3px 0; }" +
        " .rc { display: inline-flex; align-items: flex-end; } .rc > span:first-child { margin-right: -1px; }" +
        " .rc > span:last-child { border-top: 1px solid currentColor; padding: 1px 1px 0; line-height: 1.1; }" +
        " .gp { font-size: 1.7em; font-weight: 300; vertical-align: -.12em; line-height: 0; }";

    /// <summary>Le texte en HTML, formules mises en forme (à afficher avec <see cref="StyleHtml"/>).</summary>
    public static string Html(string texte, bool formule = false)
    {
        var sb = new StringBuilder();
        var noeuds = Analyser(texte, formule);
        var i = 0;
        while (i < noeuds.Count)
        {
            if (noeuds[i] is MorceauMath { Math: false } p)
            {
                // Les grands blancs qui séparent deux formules (« Triphasé : …      Monophasé : … ») ne doivent pas se réduire à un espace.
                sb.Append(Regex.Replace(WebUtility.HtmlEncode(p.Texte), " {3,}", "&emsp;&emsp;"));
                i++;
                continue;
            }
            sb.Append("<span class=\"m\">");
            while (i < noeuds.Count && noeuds[i] is not MorceauMath { Math: false }) Html(sb, noeuds[i++]);
            sb.Append("</span>");
        }
        return sb.ToString();
    }

    static void Html(StringBuilder sb, NoeudMath n)
    {
        switch (n)
        {
            case MorceauMath m:
                var t = WebUtility.HtmlEncode(m.Texte);
                if (m.Italique) t = $"<i>{t}</i>";
                if (m.Indice.Length > 0) t += $"<sub>{WebUtility.HtmlEncode(m.Indice)}</sub>";
                if (m.Grand) t = $"<span class=\"gp\">{t}</span>";
                sb.Append(t);
                break;
            case FractionMath f:
                sb.Append("<span class=\"fr\"><span>");
                foreach (var x in f.Numerateur) Html(sb, x);
                sb.Append("</span><span>");
                foreach (var x in f.Denominateur) Html(sb, x);
                sb.Append("</span></span>");
                break;
            case RacineMath r:
                sb.Append("<span class=\"rc\"><span>√</span><span>");
                foreach (var x in r.Contenu) Html(sb, x);
                sb.Append("</span></span>");
                break;
        }
    }
}
