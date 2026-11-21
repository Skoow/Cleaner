# PCDoc — Analyse globale : manques, sécurité, confidentialité, optimisation

Date : 2026-11-21 — Base : spécification maître du 03/11, plan v0.1, et ajouts discutés depuis (outils, catalogue des réglages Windows, modes novice/expert, optimisation, désinstallation propre, bloatwares, rapport forum, antivirus en couches).

Légende des priorités :
- **P0** : à corriger dans la conception **avant d'écrire le code** concerné (faille ou impasse).
- **P1** : important, à prévoir dans la version du module.
- **P2** : amélioration, quand on a le temps.

---

## 1. Failles de conception à corriger (P0)

Ce sont les points les plus importants de l'analyse. PCDoc tourne en administrateur : s'il est mal conçu, un virus peut s'en servir pour obtenir les droits administrateur sans UAC. On deviendrait l'outil des pirates.

| # | Problème | Pourquoi c'est grave | Correction proposée |
|---|---|---|---|
| 1 | **Tâche planifiée élevée** (« UAC une seule fois ») qui lance un exécutable installé dans `%LOCALAPPDATA%` (emplacement par défaut de Velopack) | N'importe quel programme lancé par l'utilisateur, virus compris, peut remplacer `PCDoc.exe` dans ce dossier. Au lancement suivant, la tâche l'exécute en administrateur sans UAC. C'est exactement un contournement de l'UAC, ce que la spécification interdit | Installer dans **Program Files** (Velopack sait le faire avec son installateur MSI en mode *PerMachine*). La tâche n'est proposée **que** si l'exécutable est dans un dossier protégé, et la signature de PCDoc est vérifiée avant chaque lancement. Version portable : jamais de tâche |
| 2 | **Outils externes rangés dans `%LOCALAPPDATA%\PCDoc\tools`** puis lancés en administrateur | Même problème : dossier modifiable sans droits admin, donc un virus peut remplacer l'outil entre le téléchargement et le lancement | Les ranger dans `%ProgramData%\PCDoc\tools` avec des droits réservés aux administrateurs. Vérifier la signature **et le nom exact de l'éditeur** (« Malwarebytes Inc. », pas seulement « signé ») **juste avant chaque lancement**. Toujours un chemin complet |
| 3 | **Désinstalleurs lus dans `HKCU`** (logiciels installés « pour moi seulement ») et lancés par PCDoc en administrateur | Cette partie du registre est modifiable sans droits admin. Un virus peut y écrire une fausse commande de désinstallation, et PCDoc la lancerait en administrateur | Lancer les désinstalleurs issus de `HKCU` **sans** les droits administrateur (jeton de l'utilisateur), et refuser toute commande qui ne pointe pas vers un fichier existant et signé ou connu |
| 4 | **Mauvais profil utilisateur** : quand un utilisateur standard tape le mot de passe d'un autre compte administrateur à l'UAC, PCDoc tourne sous **ce compte-là** | `HKCU`, `%LOCALAPPDATA%`, les navigateurs, les traces et les programmes au démarrage lus ou nettoyés seraient ceux de l'administrateur, pas de la personne assise devant le PC. Les résultats seraient faux et les nettoyages viseraient le mauvais compte | Notion de **profil cible** dans l'Infrastructure : on détecte l'utilisateur de la session ouverte et on lit sa ruche (`HKEY_USERS\<SID>`) et ses dossiers. À intégrer dès la v0.1 (Task 5 et Task 10) |
| 5 | **Définitions JSON « signées, vérifiées par hash »** | Une empreinte téléchargée au même endroit que le fichier ne protège de rien : quiconque modifie l'un modifie l'autre. Or ces définitions décident de ce qu'on supprime | **Vraie signature numérique** (Ed25519, avec minisign ou l'équivalent .NET). La clé publique est intégrée à PCDoc et la clé privée reste dans un secret GitHub. Numéro de version croissant pour empêcher qu'on renvoie une ancienne version |
| 6 | **LibreHardwareMonitor et le pilote WinRing0** (températures, M3) | Defender bloque WinRing0 comme pilote vulnérable (`VulnerableDriver:WinNT/Winring0`). Utiliser une ancienne version ferait signaler PCDoc comme dangereux | N'utiliser que les versions de LibreHardwareMonitor basées sur **PawnIO**. Prévenir les joueurs : certains anti-triche réagiraient à PawnIO, donc installation du pilote **à la demande** seulement |
| 7 | **Import d'une liste de corrections venant d'un forum** (idée façon FRST) | Les arnaqueurs se font passer pour des aideurs : « colle ce texte dans PCDoc » | Reporté après la v1.0. Si on le fait : mode expert, chaque ligne affichée en clair, aucune commande libre, seulement des actions de notre catalogue |

---

## 2. Sécurité de PCDoc lui-même

### 2.1 Chaîne de fabrication (P1, dès la v0.1)
- **GitHub** : double authentification, branche `main` protégée, revue obligatoire avant fusion.
- **GitHub Actions** :
  - actions épinglées par leur empreinte (SHA), pas par `@v4` ;
  - `permissions: contents: read` par défaut ;
  - aucun secret accessible aux pull requests externes.
- **Dépendances** :
  - fichiers de verrouillage NuGet (`packages.lock.json`) ;
  - Dependabot pour les mises à jour ;
  - alerte sur les paquets vulnérables (`dotnet list package --vulnerable` dans la CI).
- **Analyse du code** : CodeQL (gratuit pour l'open source), analyseurs .NET au niveau « recommended », OpenSSF Scorecard.
- **Nomenclature des composants (SBOM)** au format CycloneDX, jointe à chaque version.
- **Builds reproductibles** autant que possible (`Deterministic`, `ContinuousIntegrationBuild`).
- **Signature du code** en v1.0 (SignPath Foundation ou Azure Trusted Signing : conditions et pays éligibles à vérifier). D'ici là, les sommes SHA-256 sont publiées avec chaque version.

### 2.2 Exécution (P1)
- Lancer tout processus avec un **chemin complet** (`C:\Windows\System32\…`), jamais via le `PATH`.
- Bloquer le détournement de DLL : `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)` au démarrage.
- Traiter comme **non fiables** tout ce que PCDoc lit sans l'avoir écrit lui-même en administrateur : `settings.json`, journaux, registre `HKCU`, rapports d'outils externes, fichiers `.lnk`. Lecture défensive, aucune commande exécutée depuis ces sources.
- **Quarantaine** neutralisée : fichiers renommés, droits retirés, contenu chiffré (pour qu'un antivirus ne les signale pas), dans un dossier réservé aux administrateurs.
- **Clé VirusTotal** chiffrée avec DPAPI (liée au compte Windows), jamais dans le journal.
- **Tests à données piégées** (fuzzing) des analyseurs maison : raccourcis `.lnk`, XML des événements, lignes de commande, rapports AdwCleaner.
- **SECURITY.md** : comment signaler une faille en privé (signalement privé de GitHub).

### 2.3 Faux positifs des antivirus (P1)
- Pas de compression ni d'obfuscation de l'exécutable. Le mode « single-file » auto-extractible déclenche parfois les heuristiques : on teste les deux formats sur VirusTotal à chaque version.
- Soumettre chaque version à Microsoft (portail de soumission Defender) avant de l'annoncer.

---

## 3. Confidentialité de PCDoc

| Point | Proposition | Priorité |
|---|---|---|
| Aucune télémétrie | Le promettre dans un **PRIVACY.md** clair (FR/EN) et dans l'app | P1 (v0.1) |
| Transparence réseau | Page **« Activité réseau »** : chaque connexion faite par PCDoc (vers GitHub, winget, VirusTotal, éditeurs), avec date et raison | P1 (v0.4) |
| Vérification des mises à jour | Elle révèle l'adresse IP à GitHub : le dire, et proposer de la désactiver | P1 |
| VirusTotal | Désactivé par défaut. Empreinte seulement. Envoi d'un fichier uniquement après confirmation, avec l'avertissement « le fichier devient consultable par les clients de VirusTotal ». Clé personnelle de l'utilisateur. L'API gratuite est limitée à 4 requêtes par minute et 500 par jour, et réservée aux usages non commerciaux (PCDoc est gratuit, donc c'est conforme) | P1 (v0.5) |
| Rapport pour les forums | Anonymisation automatique (nom d'utilisateur, nom du PC, IP, numéros de série, Wi-Fi), **jamais** de clé de produit, aperçu avant partage | P1 (v0.8) |
| Données locales de PCDoc | Les journaux contiennent des chemins avec le nom d'utilisateur : conservation 14 jours, bouton **« Effacer les données de PCDoc »**, tout est supprimé à la désinstallation | P1 |
| Rapports de plantage | Jamais envoyés automatiquement. L'utilisateur peut copier le rapport pour l'ajouter à un ticket GitHub | P1 |

---

## 4. Fonctions qui manquent encore

Les ajouts des derniers échanges sont supposés intégrés. Voici ce qui manque en plus.

| Fonction | Pourquoi | Priorité / version |
|---|---|---|
| **Compatibilité Windows 11** (TPM 2.0, Secure Boot, processeur pris en charge, UEFI/GPT), façon PC Health Check / WhyNotWin11 | Windows 10 n'est plus pris en charge depuis le 14/10/2025 : c'est la première question des utilisateurs de Windows 10 | P1 — **v0.1** (ajouter au Bilan, coût faible) |
| **Infos sur les mises à jour de sécurité étendues (ESU)** de Windows 10 | Les utilisateurs qui ne peuvent pas passer à 11 doivent savoir comment rester protégés | P1 — v0.1 (texte du finding) |
| **Assistant de premier lancement** : langue, mode novice/expert, vérification que la protection du système (points de restauration) est activée, clé VirusTotal facultative | La protection du système est **désactivée** sur beaucoup de PC : sans elle, notre promesse « point de restauration avant chaque correction » échoue | P1 — v0.2 |
| **Limite de Windows sur les points de restauration** (un par 24 h par défaut) | Le deuxième point de la journée échoue sans le dire : il faut le détecter et le gérer | P0 — v0.2 |
| **Journal d'annulation transactionnel** : l'`UndoEntry` est écrite **avant** la modification | Coupure de courant au milieu d'une correction | P0 — v0.2 |
| **Maintenance planifiée** facultative (analyse hebdomadaire, nettoyage, vérification des mises à jour) avec une notification Windows | L'utilisateur novice oublie de relancer l'outil. La notification n'est pas une fenêtre, donc la règle est respectée | P2 — v0.4 |
| **Préparer une réinstallation / changer de PC** : export des logiciels installés (`winget export`), des pilotes (`pnputil /export-driver`), des profils Wi-Fi | Très demandé, simple à faire | P2 — v0.8 |
| **Aide en cas de PC qui ne démarre plus** : fiche « que faire » (environnement de récupération, restauration système, démarrage en mode sans échec) accessible depuis l'app et le README | PCDoc ne peut pas aider si Windows ne démarre plus | P2 |
| **Réinitialisation des navigateurs** (page d'accueil, moteur de recherche, extensions) | Complète la détection façon ZHPCleaner | P1 — v0.5 |
| **Comptes Windows** : comptes sans mot de passe, compte Invité, comptes administrateurs en trop | Sécurité de base | P1 — v0.5 |
| **Historique de la note de santé** | Voir l'effet des corrections dans le temps | P2 |
| **Recherche globale** dans toutes les pages | Avec 13 modules, on ne sait plus où se trouve une option | P2 — v0.6 |
| **Version ARM64** (PC Snapdragon) | Ces PC se répandent. .NET et WPF les prennent en charge | P1 — **v0.1** (une ligne de plus dans la CI) |
| **Mode portable pour les dépanneurs** : paramètres à côté de l'exécutable, utilisable depuis une clé USB | Les techniciens qui dépannent des PC sont une cible naturelle | P2 — v1.0 |
| **Traductions par la communauté** (Weblate, gratuit pour l'open source) | D'autres langues sans travail pour toi | P2 — après v1.0 |

---

## 5. Accessibilité (P1, dès la v0.1)

Totalement absente de la spécification. Pour un outil destiné aux novices, elle est indispensable.
- Tout se fait **au clavier** (Tab, Entrée, Échap), avec un focus visible.
- **Lecteurs d'écran** (Narrateur, NVDA) : noms d'automatisation (`AutomationProperties.Name`) sur chaque bouton, pastille et ligne.
- **Contraste élevé** de Windows respecté, et texte agrandi de Windows (jusqu'à 225 %) sans texte coupé.
- **La gravité ne repose jamais sur la couleur seule** : pastille + mot (« Critique ») + icône. C'est déjà le cas dans les maquettes.

---

## 6. Optimisation de PCDoc lui-même

| Point | Cible / solution | Priorité |
|---|---|---|
| Démarrage de l'app | Fenêtre affichée en moins de 2 s, analyses lancées seulement à la demande. ReadyToRun activé | P1 — v0.1 |
| Mémoire | Moins de 200 Mo pendant une analyse | P1 |
| Listes longues (services, tâches, fichiers, doublons) | **Virtualisation** des listes WPF (`VirtualizingStackPanel`), indispensable au-delà de quelques centaines de lignes | P1 — **v0.1** (page Système) |
| Analyses lourdes (doublons, carte du disque, antivirus) | Priorité processeur et disque basse pour ne pas figer le PC, annulables, reportées si le portable est sur batterie | P1 — v0.3 |
| Signatures | Cache par fichier et date de modification (déjà prévu), conservé entre deux analyses | P2 |
| Taille | La version portable autonome pèse environ 150 Mo : WPF ne permet pas d'enlever le code inutilisé. L'installateur (v1.0) peut être léger en installant le runtime .NET à part (Velopack sait le faire) | P2 — v1.0 |

---

## 7. Qualité et tests

| Point | Proposition | Priorité |
|---|---|---|
| Matrice de compatibilité | Windows 10 22H2, Windows 11 23H2 / 24H2 / 25H2, éditions Famille et Pro, Windows en français et en anglais (les noms des groupes et de certains services sont traduits), x64 et ARM64, utilisateur standard qui saisit un mot de passe admin | P1 |
| Tester les corrections sans risque | Le runner Windows de GitHub est une machine jetable : on peut y tester les corrections pour de vrai. Mais c'est Windows Server, pas Windows 10/11 : les tests dans tes VM restent nécessaires | P1 — v0.2 |
| Bac à sable Windows | Windows Sandbox (Pro) pour tes tests manuels rapides | P2 |
| Annulation qui vieillit | Que faire si on annule une correction après une mise à jour de Windows qui a changé l'élément ? Vérifier l'état actuel avant d'annuler et prévenir en cas de différence | P1 — v0.2 |
| Bloatwares et réglages qui reviennent | Re-détection dans le Bilan (déjà prévue) | P1 |

---

## 8. Licences et aspects juridiques

| Composant | Point d'attention |
|---|---|
| **privacy.sexy** (AGPL-3.0) | Compatible avec la GPL-3.0 (article 13). Les données reprises gardent leur licence AGPL : crédit et licence à conserver dans le dossier des définitions |
| **AdwCleaner** | Gratuit, mais régi par la licence Malwarebytes : vérifier que le pilotage automatisé est autorisé. On télécharge depuis le site officiel, on ne redistribue jamais |
| **Emsisoft Emergency Kit** | Gratuit pour un usage **privé** seulement. Dernière version trouvée : janvier 2025, donc vérifier qu'il est encore maintenu (règle des 2 ans) |
| **VirusTotal** | API gratuite interdite dans un produit commercial : PCDoc doit rester gratuit (c'est prévu) |
| **smartmontools** (GPL-2.0-or-later) | Compatible. Exécutable séparé |
| **Nom définitif** | Vérifier les marques déposées (INPI, EUIPO, USPTO) et le nom de domaine avant de choisir. « Sentinelle » et « Vigil » sont très utilisés en sécurité |
| **Bloatwares des fabricants** | Les supprimer est légal. Ne jamais supprimer de pilotes ni de logiciels de gestion du BIOS ou des mises à jour du fabricant en mode novice |

---

## 9. Architecture : un choix à confirmer

La spécification lance **toute l'app en administrateur**. C'est simple, mais ça entraîne le problème du mauvais profil (§1, n° 4), un risque plus large si l'app a une faille, et l'impossibilité de glisser-déposer un fichier depuis l'Explorateur.

L'autre possibilité est une interface lancée normalement plus un petit processus administrateur invisible qui fait les actions. C'est plus propre, mais aussi plus complexe pour un projet dont tu assureras le suivi.

**Recommandation :** garder l'app entière en administrateur, avec la notion de **profil cible** (§1, n° 4) dès la v0.1. On réévaluera si le glisser-déposer devient nécessaire (par exemple pour analyser un fichier avec VirusTotal : un bouton « Choisir un fichier » suffit).

---

## 10. Ce que ça change au plan v0.1

Changements à intégrer au plan avant de commencer à coder :

1. **Profil cible** : `IUserProfileResolver` dans `Core/Abstractions`, implémenté dans l'Infrastructure (session active → SID → `HKEY_USERS\<SID>`, dossiers du profil). Utilisé par `StartupCollector` pour les clés `HKCU` et les dossiers Démarrage. Test : « un PC où PCDoc tourne sous un autre compte admin lit bien le profil de l'utilisateur connecté ».
2. **Règle de compatibilité Windows 11** dans le Bilan (TPM 2.0, Secure Boot, processeur pris en charge), avec le texte ESU pour Windows 10.
3. **Build ARM64** dans la CI.
4. **CI renforcée** : actions épinglées par SHA, permissions minimales, Dependabot, CodeQL, vérification des paquets vulnérables, fichiers de verrouillage NuGet.
5. **SECURITY.md** et **PRIVACY.md**.
6. **Accessibilité** : noms d'automatisation, clavier, contraste élevé (Tasks 13 à 16).
7. **Listes virtualisées** dans la page Système.
8. **ReadyToRun** à la publication.

Changements à la spécification (version 2) : les points P0 du §1, plus tous les ajouts des derniers échanges.

---

Sources des points vérifiés :
- [Alerte Defender VulnerableDriver:WinNT/Winring0 (Microsoft)](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/microsoft-defender-antivirus-alert-vulnerabledriver-winnt-winring0) · [Neowin : WinRing0 signalé](https://www.neowin.net/news/windows-1110-is-flagging-winring0-on-your-pc-monitoring-fan-control-apps-heres-why/) · [Framework : passage à PawnIO](https://community.frame.work/t/potential-threat-from-software-that-uses-winring0-drivers-fanctrl-openrgb-libre-hardware-monitor-etc/66135)
- [VirusTotal : API publique et Premium](https://docs.virustotal.com/reference/public-vs-premium-api)
- [Velopack : installateur et emplacements](https://docs.velopack.io/packaging/installer)
