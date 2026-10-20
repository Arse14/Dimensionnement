# Dimensionnement électrique

Logiciel Windows d'aide au dimensionnement du matériel électrique des coffrets et armoires industriels :
départs moteurs, protections, contacteurs, sections de câbles, arrivée générale, bilan de puissance et
note de calcul traçable.

> Aide au dimensionnement : les résultats doivent être vérifiés et validés par le bureau d'étude. Les tables
> normatives embarquées (`src/Dimensionnement.Core/Normes`) sont à contrôler contre les éditions en vigueur
> de la NF C 15-100 avant usage en production.

## Ce que fait la version 1

| Domaine | Contenu |
| --- | --- |
| Projet et réseau | Mono 230 V, tri 400 V, tétra 400 V ; SLT TT, TN-C, TN-S, IT ; Icc ; température ; câble d'arrivée |
| Départs moteurs | Saisie de la plaque, contrôle de cohérence, démarrage direct, inverseur, étoile-triangle, variateur |
| Disjoncteurs moteurs | Choix de la plage (0,1–0,16 A à 62–80 A), réglage thermique, contrôle du seuil magnétique |
| Contacteurs | AC-3 pour les moteurs (ligne, étoile, triangle), AC-1 pour les charges résistives |
| Disjoncteurs | Calibre normalisé, courbe, nombre de pôles selon le SLT, pouvoir de coupure, différentiel A / B / F |
| Câbles | Courant admissible (méthodes A1, B1, C, E ; PVC ou PR), facteurs de température et de groupement, chute de tension cumulée, section du PE |
| Arrivée et bilan | Coefficients ku et ks, P, Q, S, cos φ, interrupteur-sectionneur, disjoncteur général, câble d'arrivée, sélectivité indicative |
| Note de calcul | Chaque valeur identifiée (formule, entrées, référence normative) ; valeur forçable avec motif, auteur et date |
| Arbre du matériel | De l'arrivée jusqu'à chaque récepteur, avec voyant d'état et zone CEM de chaque appareil |
| Rapport | Rapport complet HTML prêt à imprimer en PDF : garde, données, départs, arbre, notes, bilan, CEM / BT, nomenclature, révisions |
| Projets | Fichier unique `.dimelec`, réouverture, « Enregistrer sous » pour dupliquer, liste des projets récents |

## Organisation

```
src/Dimensionnement.Core        moteur de calcul, sans interface (testable partout)
  Modele/                       projet, réseau, départs, forçages
  Normes/                       calibres normalisés, tables de câbles
  Calculs/                      départs, câbles, bilan et arrivée, notes de calcul
  Materiel/                     arbre du matériel et nomenclature
  Persistance/                  fichiers projet et projets récents
  Rapport/                      rapport HTML
src/Dimensionnement.App         interface WPF (Windows)
tests/Dimensionnement.Core.Tests  tests sur des cas de calcul connus
```

## Compiler et lancer

Prérequis : SDK .NET 8 sous Windows.

```
dotnet build Dimensionnement.sln
dotnet test tests/Dimensionnement.Core.Tests
dotnet run --project src/Dimensionnement.App
```

L'intégration continue publie à chaque modification un exécutable Windows autonome
(artefact `Dimensionnement-windows-x64` de l'action « Compilation et tests »).
