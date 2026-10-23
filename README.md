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
| Rapport | Rapport complet HTML prêt à imprimer en PDF : garde, données, départs, arbre, notes, bilan, nomenclature, révisions |
| Projets | Fichier unique `.dimelec`, réouverture, « Enregistrer sous » pour dupliquer, liste des projets récents |

## Ce qu'ajoute la version 2

| Domaine | Contenu |
| --- | --- |
| Protection par fusibles | Sectionneur porte-fusibles et cartouches aM ou gR (avec relais thermique) pour les moteurs, gG pour les autres départs, taille 10×38 à NH |
| Démarreur progressif | Mode de démarrage supplémentaire, avec contacteur de by-pass et point de contrôle CEM |
| Alimentation 24 V | Bilan des consommateurs de commande (automate, sécurité, saisie libre), réserve de 25 %, calibre normalisé de l'alimentation et de sa protection |
| Sécurité machine | Fonctions de sécurité avec PLr (NF EN ISO 13849-1), catégorie, redondance des contacteurs, STO des variateurs, relais Pilz / Schmersal / Siemens / Schneider ou automate de sécurité, export SISTEMA (CSV) |
| Automate | Siemens et Schneider uniquement : cartes E/S avec réserve et choix de la CPU selon les périphériques et connexions réseau. Le catalogue est modifiable (`%AppData%\Dimensionnement\catalogue-automates.json`) ; ses valeurs sont indicatives et à vérifier sur les fiches constructeur |
| Exports | Rapport PDF direct (WebView2, repli en HTML si indisponible), nomenclature et notes de calcul en Excel |

## Ce qu'ajoute la version 3

| Domaine | Contenu |
| --- | --- |
| Armoire | Plus petite enveloppe normalisée (coffret mural ou armoire au sol, cellules juxtaposées) selon l'encombrement du matériel et la réserve ; profondeur selon l'appareil le plus profond ; dimensions forçables |
| Implantation CEM | Zone propre en haut (automate, E/S, sécurité), intermédiaire (protections, alimentations), sale en bas près des entrées de câbles moteurs, séparation ≥ 200 mm ; borniers estimés |
| Vue d'armoire | Fenêtre interactive : déplacement des appareils à la souris, contrôle de zone et de chevauchement, carte indicative des champs des variateurs, démarreurs, contacteurs et alimentations |
| Bilan thermique | Pertes des appareils au courant de service, surface effective IEC TR 60890, ventilateur à filtre, climatiseur ou résistance anticondensation |
| Sélectivité | Rapport des calibres et des seuils magnétiques entre le général et chaque départ, comparés à l'Icc |
| Références | Schneider, ABB, Siemens ou Legrand pour la puissance (catalogue indicatif modifiable, repli sur un autre fabricant si la gamme manque), saisie manuelle possible |
| Demande de devis | Liste exhaustive en Excel pour le fournisseur : enveloppe, thermique, appareillage regroupé par référence, accessoires, borniers, rails, goulottes, filerie, câbles en mètres, presse-étoupes || Automate | Cartes d'E/S standard ajoutées au fil des voies (S7-1200, S7-1500, ET200SP, TM3, X80), option compacte ET200SP, CPU imposable, stations déportées ET200SP / TM3 comptées dans la charge de communication |
| Câblage | Estimation des fils par section et des goulottes ; rails DIN et goulottes ajoutés à la main dans la vue d'armoire, zones propre / intermédiaire / sale recalculées |
| Sources de commande | Alimentations à découpage Siemens / Schneider, transformateurs 400/24 V et 230/24 V (AC, ou DC par redresseur) et 400/230 V ; chaque source est dimensionnée sur ce qui lui est raccordé (automate, IHM, sécurité, bobines, capteurs) ; saisie manuelle vérifiée ; ajout automatique de la source qui manque, selon la préférence du projet |
| Bobines | Tension des bobines de contacteurs (24 V DC, 24 V AC, 230 V AC) : consommation sur la bonne source et références adaptées |
| IHM | Pupitres Siemens (Basic, Unified Comfort) et Schneider (Harmony ST6, GTO, GTU) choisis selon la diagonale, comptés en connexions et au bilan 24 V |
| Bilan de puissance | Onglet dédié qui explique chaque étape, du récepteur au câble d'arrivée |
| Aperçus | Demande de devis et rapport visibles avant l'export Excel ou PDF ; marques imposables pour les disjoncteurs et les contacteurs, caractéristiques complètes au devis même sans référence |
| Catalogues | Navigateur commun aux 8 catalogues (automates, cartes d'E/S, IHM, alimentations et transformateurs, variateurs, sécurité, enveloppes, références) : tableau filtrable par marque, gamme et valeur minimale, fiche technique avec chiffres clés, encombrement à l'échelle, photos, fiches PDF et plans, usage dans le projet et historique ; valeurs manquantes signalées et complétées à la main, article marqué « vérifié » une fois contrôlé ; les modifications vont dans `%AppData%\Dimensionnement` et le catalogue livré reste récupérable |
| Nouveaux matériels | Servomoteurs (SINAMICS V90, Lexium 32, MicroFlex), moteurs deux vitesses (Dahlander ou deux enroulements), moteurs-freins (frein 230 V séparé ou 24 V DC), coffret déporté avec sa simultanéité, éclairage machine, parafoudre type 2 et relais de contrôle de phases en tête, îlots d'électrovannes, colonnes lumineuses et éclairages 24 V |
| Sécurité | Barrières immatérielles et scrutateurs (Datasensing, Schneider, Sick) hors plaque, avec leurs protections et bornes dans l'armoire |
| Cotes | Encombrements relevés des gammes Siemens et Schneider (contacteurs, disjoncteurs, disjoncteurs moteurs, relais thermiques, interrupteurs-sectionneurs, démarreurs, porte-fusibles), avec source et fiabilité, dans le catalogue « Appareillage (cotes) » ; variateurs et alimentations Siemens corrigés d'après les fiches |
| Ergonomie | Chaque appareil se renomme (clic droit dans l'arbre ou sur la plaque) ; une case en rouge explique pourquoi en bas à droite et une valeur inhabituelle peut être forcée ; la vue d'armoire explique comment zones, rangées, borniers, goulottes et champ magnétique sont obtenus |

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
