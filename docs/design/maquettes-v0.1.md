# Maquettes v0.1 — version validée

- Maquettes interactives : `docs/design/maquettes-v0.1.html` (publiées : https://claude.ai/artifact/FXyMXtcnGAAxyf9NqYnxFJ, version 2).
- Validées par l'utilisateur le 2026-11-21 (« Oui continue »).

## Décisions fixées par les maquettes
1. Fenêtre unique ; barre de titre Windows standard ; menu latéral de 220 px (icônes seules sous 760 px de large).
2. Menu v0.1 : **Bilan, Système**, puis **Paramètres** en bas. Les autres modules apparaîtront avec leur version.
3. Bilan :
   - état initial : carte « Vérifions l'état de votre PC », bouton **« Analyser mon PC »**, texte « L'analyse lit les informations de Windows sans rien modifier » ;
   - en cours : barre de progression, une ligne par étape, bouton **« Annuler »** ;
   - résultats : anneau de note sur 100 + libellé (Bon état / À surveiller / À améliorer), pastilles « N critique / N attention / N info », filtre par module, lignes avec bande de couleur + pastille texte (Critique / Attention / Info), titre, explication, chevron ; détail dépliable « Détails techniques » en police à chasse fixe + bouton **« Copier »** ;
   - barre du bas : « Les corrections automatiques arrivent dans la prochaine version. Rien n'a été modifié sur votre PC. » + **« Copier le rapport »** ; bouton **« Analyser à nouveau »** en en-tête.
4. Système : sous-titre « Lecture seule », bouton **« Actualiser »**, 7 onglets dans cet ordre : Vue d'ensemble · Matériel · Disques · Démarrage · Programmes au démarrage · Tâches planifiées · Services.
   - Vue d'ensemble : cartes Windows, Processeur, Mémoire (jauge), Carte graphique, Disque C: (jauge), Batterie, **Compatibilité Windows 11** ;
   - Démarrage : dernier démarrage, médiane des 5 derniers, tendance ; graphique en barres des 30 derniers démarrages avec la ligne de médiane ; tableau « Ce qui a ralenti le dernier démarrage » ;
   - listes : recherche « Rechercher un nom ou un éditeur », filtre **« Non signés uniquement »**, compteur « N élément(s) · lecture seule », colonnes Nom · Éditeur · Emplacement/Déclencheur/Démarrage · Signature · État · Commande.
5. Pastilles de signature : **Signé** (vert), **Non signé** (orange), **Introuvable** (rouge), toujours avec le mot, jamais la couleur seule.
6. Paramètres : Langue (Français / English), Thème (Clair / Sombre / Système), **Compte analysé**, Journal des actions (chemin + « Copier le chemin »), **« Effacer les données de PCDoc »** ; section À propos : version, licence GPL-3.0, tableau des composants, « Lire la licence GPL-3.0 » affichée dans la fenêtre.
7. Aucun bouton n'ouvre l'Explorateur, l'Éditeur du Registre ou un navigateur : on copie.
