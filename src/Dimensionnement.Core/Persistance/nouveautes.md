# Nouveautés de Dimensionnement électrique
# Une section par version, la plus récente en haut : « ## vX.Y – JJ/MM/AAAA », puis une ligne « - » par changement.
# Cette page est affichée dans l'application (bouton « Nouveautés ») et sert de texte à la release GitHub.

## v1.3 – 14/11/2026
- Contacts indirects (NF C 15-100, 411.4 et 411.6) : en TN et IT, le courant de défaut minimal en bout de câble est calculé (méthode conventionnelle) et comparé au seuil magnétique de la protection ; la section monte si besoin, la longueur maximale protégée est donnée dans la note de calcul. Un différentiel sur le départ lève la limite ; avec des fusibles, contrôle à faire sur la courbe de fusion (orange).
- Schéma TT : un départ sans différentiel est signalé (NF C 15-100, 411.5), sauf si la nouvelle case « Différentiel en amont » de « Projet et réseau » est cochée ; R_A maximal rappelé pour 30 et 300 mA.
- Interrupteur différentiel séparé (derrière un disjoncteur moteur, des fusibles ou le disjoncteur d'un variateur) : il entre maintenant dans le matériel, l'implantation et le devis, référence à saisir.
- Variateurs : le disjoncteur amont est choisi sur le courant d'entrée du variateur (estimé sans self de ligne, en orange tant que la fiche ne le donne pas), et le bilan de l'armoire voit ce même courant. Le câble moteur reste choisi sur le courant du moteur (protégé par le variateur).
- Variateur à entrée monophasée : le moteur est un triphasé 230 V ; courant moteur, câble 4G et chute de tension calculés en triphasé 230 V.
- Disjoncteur général : contrôle du démarrage du plus gros moteur en direct (Id + autres départs face au seuil magnétique bas du général).
- Bilan de puissance : chauffage, climatiseur et ventilateurs 230 V de l'enveloppe sont comptés dans le courant d'emploi de l'armoire.
- Chute de tension : en plus de la limite NF C 15-100, chaque départ seul est limité à 5 % (NF EN 60204-1, 12.5).
- Câbles PR : facteur de température jusqu'à 80 °C ; une ambiance au-delà du tableau (60 °C en PVC) est signalée en rouge au lieu d'être ramenée en silence à 60 °C.
- Saisies : un cos φ ou un rendement hors de ]0 ; 1] est signalé en rouge et le calcul continue, au lieu d'afficher des courants infinis.

## v1.2.3 – 23/10/2026
- Fermeture de l'application : un catalogue ouvert et modifié est proposé à l'enregistrement d'abord, puis toutes les fenêtres (catalogues, aperçus) se ferment avec la fenêtre principale.
- Mise à jour : les catalogues ouverts sont proposés à l'enregistrement avant l'installation, plus pendant la fermeture.

## v1.2.2 – 22/10/2026
- IHM : les Comfort Panels Siemens (KP400 à TP2200, ex. TP700 Comfort 6AV2124-0GC01-0AX0) et les variantes SIPLUS (Comfort, Basic, Unified) sont au catalogue, pour réutiliser l'existant ; on les choisit dans « Modèle », le choix automatique reste sur les gammes actuelles. Comfort marqués « fin de commercialisation ».
- Catalogue des IHM personnalisé : les nouveaux pupitres livrés s'ajoutent à ton fichier sans toucher à tes modifications.
- Cotes, découpes et consommations des Comfort Panels 7 à 12" et des Basic Panels relevées sur SiePortal (découpe du KTP400 Basic : 124 × 100 mm, du KTP700 Basic : 198 × 142 mm).
- Catalogues : bouton « Copier » à côté de la référence sur la fiche, clic droit « Copier la référence » et Ctrl+C sur le tableau.
- Départ moteur : choix « Variateur imposé » pour prendre une référence précise du catalogue, contrôlée face à la puissance et au courant du moteur.
- Variateurs : choix automatique du meilleur modèle du catalogue (gamme la plus adaptée, plus petit calibre, filtre CEM intégré, encombrement, pertes) ; si la marque du projet n'a rien qui convient, le meilleur modèle des autres marques est retenu et signalé.
- Catalogue des variateurs : 99 modèles ajoutés. Schneider : ATV320 livre, ATV320 monophasé 230 V, ATV12, petits ATV630, ATV340, ATV930. Siemens : V20 triphasé et monophasé, G120 PM240-2 jusqu'à 250 kW (Control Unit CU240E-2 ajoutée au devis). Valeurs non relevées sur fiche en orange.
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
