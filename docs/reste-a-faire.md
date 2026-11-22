# Ce qui reste à faire

Mis à jour le 2026-11-21, après la v0.10. Détail des décisions : `docs/superpowers/plans/2026-11-03-v0.1-decisions.md`.

## Fait
| Version | Contenu |
|---|---|
| v0.1 | Fenêtre, FR/EN, thèmes, Bilan, page Système (matériel, Windows 11, démarrage, signatures) |
| v0.2 | Corrections sûres : conseiller, activer/désactiver, points de restauration, sauvegardes .reg, Historique, modes novice/expert, assistant de premier lancement |
| v0.3 | Diagnostic : écrans bleus, plantages, périphériques, santé des disques, redémarrage en attente, pilotes anciens, réparations DNS/SFC/DISM |
| v0.4 | Nettoyage : temporaires, caches (Windows Update, navigateurs, graphiques), rapports d'erreurs, vidages, corbeille, anciennes mises à jour, gros fichiers |
| v0.5 | Applications : liste Win32 + Store, désinstallation silencieuse quand possible, applications préinstallées (paquets provisionnés compris), restes en quarantaine 7 jours |
| v0.6 | Mises à jour (Windows Update et pilotes sans fenêtre, logiciels via winget, installation groupée) + Optimisation (réglages réversibles, mode d'alimentation, veille prolongée, profils Jeu/Bureautique/Batterie) |
| v0.7 | Sécurité : antivirus et Defender (analyses, définitions, détections), pare-feu, SMBv1, cachettes (IFEO, Winlogon, AppInit, hosts), réglages UAC/AutoRun/SmartScreen/RDP/WDigest, virus raccourci des clés USB |

## Prochaines versions
| Version | Contenu prévu |
|---|---|
| v0.8 | Journal d'activité daté + rapport .txt, page Confidentialité (14 réglages, profils Recommandé/Strict), page « Nouveau PC » (préparation complète en un clic), vérification complète de l'application |
| v0.9 | Réparations réseau/son/Windows Update + assistants « Pas d'Internet » / « Pas de son », sauvegarde des pilotes, avant/après, profils de technicien, mode portable, entretien automatique hebdomadaire, activité réseau, extensions des navigateurs, seconde analyse (Safety Scanner, AdwCleaner), VirusTotal |
| v0.10 | Confidentialité d'Office, Edge, Chrome, Firefox (seulement s'ils sont installés), mises à jour des applications du Store, recherche globale |
| v0.11 | **Traces et données** + effacement sécurisé |
| v0.12 | **Réseau**, **Licences**, **Rapport pour forums** (anonymisé) |
| v1.0 | Signature du code, installateur MSI dans Program Files, tâche planifiée (UAC une seule fois), mise à jour automatique, page de téléchargement |

## Reporté des versions précédentes (petits points)
- Diagnostic : pilote en cause d'un écran bleu (analyse des minidumps), températures (LibreHardwareMonitor/PawnIO), assistants guidés (« Pas d'internet », « Pas de son »…), réinitialisation réseau, réparation de Windows Update, processus gourmands, codes de sortie de SFC à vérifier sur un vrai Windows.
- Nettoyage : doublons, carte de l'espace disque, gestion des points de restauration, Windows.old, cache des miniatures.
- Système : liste exacte des processeurs compatibles Windows 11 (aujourd'hui par familles), carte « disque système » qui prend le premier disque, cas Secure Boot « capable mais désactivé ».
- Interface : couleurs qui ne suivent pas un changement de thème à chaud sur les lignes déjà affichées, boutons « Corriger » non rafraîchis après un changement de mode tant que la page n'est pas réanalysée, Tab qui atteint le menu derrière l'assistant.
- Applications : programmes installés pour un seul compte (HKCU, à lancer sans élévation), restes dans le registre, nom affiché des paquets provisionnés non installés.
- Corrections : deux pages peuvent lancer des corrections en même temps ; conditions du conseiller mises en cache pour la session ; service désactivé mais pas arrêté avant redémarrage (non indiqué).

## À faire par toi
- Tester chaque version sur ton PC (de préférence dans une machine virtuelle pour v0.2 et suivantes) avec `docs/tests-manuels-v0.1.md`.
- Choisir le nom définitif (il se change en un seul endroit).
- Pour v1.0 : demander la signature gratuite SignPath Foundation (projet open source).
