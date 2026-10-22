# Nouveautés de Dimensionnement électrique
# Une section par version, la plus récente en haut : « ## vX.Y – JJ/MM/AAAA », puis une ligne « - » par changement.
# Cette page est affichée dans l'application (bouton « Nouveautés ») et sert de texte à la release GitHub.

## v1.2.2 – 22/10/2026
- IHM : les Comfort Panels Siemens (KP400 à TP2200, ex. TP700 Comfort 6AV2124-0GC01-0AX0) et les variantes SIPLUS (Comfort, Basic, Unified) sont au catalogue, pour réutiliser l'existant ; on les choisit dans « Modèle », le choix automatique reste sur les gammes actuelles. Comfort marqués « fin de commercialisation ».
- Catalogue des IHM personnalisé : les nouveaux pupitres livrés s'ajoutent à ton fichier sans toucher à tes modifications.
- Cotes, découpes et consommations des Comfort Panels 7 à 12" et des Basic Panels relevées sur SiePortal (découpe du KTP400 Basic : 124 × 100 mm, du KTP700 Basic : 198 × 142 mm).
- Catalogues : bouton « Copier » à côté de la référence sur la fiche, clic droit « Copier la référence » et Ctrl+C sur le tableau.
- Départ moteur : choix « Variateur imposé » pour prendre une référence précise du catalogue, contrôlée face à la puissance et au courant du moteur.
- Variateurs : choix automatique du meilleur modèle du catalogue (gamme la plus adaptée, plus petit calibre, filtre CEM intégré, encombrement, pertes) ; si la marque du projet n'a rien qui convient, le meilleur modèle des autres marques est retenu et signalé.
- Moteurs : étoile-triangle deux sens, deux vitesses deux sens (Dahlander ou deux enroulements), monophasé avec condensateur en armoire (permanent, ou permanent + démarrage avec son relais), capacité estimée en orange tant que la plaque n'est pas saisie.
- Bornier : six bornes pour un moteur deux vitesses (comme l'étoile-triangle).
- Tous les catalogues modifiés reçoivent les nouveaux modèles livrés par une mise à jour (variateurs, disjoncteurs, enveloppes, sécurité, cartes d'E/S, répartiteurs), sans toucher aux modifications.

## v1.2.1 – 22/10/2026
- Page « Nouveautés » : l'historique des versions, ouverte d'elle-même au premier démarrage après une mise à jour.
- Sélectivité : des seuils magnétiques qui se touchent ne sont plus une alerte (sélectivité partielle, cas normal entre modulaires) ; page réécrite en clair, niveaux en vert quand il n'y a rien à faire.
- Rapport : le chapitre « Points à traiter » suit la liste des alertes de l'application ; les alertes ignorées passent dans « Points acceptés par le bureau d'étude » avec leur motif, leur auteur et leur date.
- Le bouton « Nouveautés » du bandeau de mise à jour montre ce qu'apporte la version disponible, sans quitter l'application.

## v1.2 – 22/10/2026
- Note de calcul : le pouvoir de coupure est comparé à l'Icc avec la référence retenue (vert, ou orange s'il ne tient pas).
- Note de calcul : section du PE, conducteur PEN et marge en intensité deviennent de vrais contrôles ; légende des pastilles en haut de la note.
- Fusibles : pouvoir de coupure des cartouches industrielles contrôlé à 50 kA.
- Catalogue « Appareillage (références) » : largeur, hauteur, profondeur et pertes saisissables sur chaque fiche ; avec les trois cotes, l'implantation prend celles de la fiche.

## v1.1 – 22/10/2026
- Mise à jour automatique : vérification au démarrage, bandeau « Nouvelle version disponible », téléchargement contrôlé, installation et redémarrage depuis l'application.
- Alertes : le compteur du bandeau ouvre la liste de tout ce qui reste à traiter (non conforme, à vérifier, à renseigner), avec « Voir », « Ignorer » (motif obligatoire) et « Rétablir ».
- Sélectivité sous le disjoncteur général : général et départ en orange, encart sur la carte Arrivée et bouton « Remonter le général à X A ».
- Courant d'emploi forcé : le disjoncteur général, le sectionneur et le câble d'arrivée suivent la valeur forcée.
- Version de l'application affichée en bas à gauche.

## v1.0 – 21/10/2026
- Première version installable : installateur Windows (menu Démarrer, raccourci Bureau, ouverture des fichiers .dimelec, désinstallation) et version portable.
- Départs moteurs (direct, inverseur, étoile-triangle, deux vitesses, variateur, démarreur progressif, moteur-frein, servomoteur), résistifs, prises, coffrets déportés, sources de commande 24 V et transformateurs.
- Note de calcul en cartes par point de contrôle, formules en écriture mathématique, valeurs forçables avec motif, glossaire des calculs.
- Coefficient de sécurité électrique sur chaque départ et sur le câble d'arrivée (section du câble seulement, protections au vrai courant d'emploi).
- Armoire : implantation automatique ou à la main, zones CEM, rails DIN et goulottes, enveloppes Rittal / Legrand / Schneider, coffrets déportés.
- Bilan thermique, ventilation Schneider ClimaSys automatique (230 V AC ou 24 V DC), chauffage anticondensation et climatisation.
- Sélectivité, sécurité machine NF EN ISO 13849-1 avec export SISTEMA, automate Siemens / Schneider avec cartes d'E/S et IHM.
- Catalogues modifiables (disjoncteurs, variateurs, alimentations, automates, sécurité, enveloppes…) avec fiche, cotes et validation des références.
- Organigramme du matériel, rapport PDF, demande de devis Excel alignée sur la configuration.
