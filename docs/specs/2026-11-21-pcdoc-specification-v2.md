# PCDoc — Spécification maître, version 2

> **PCDoc** est un nom de code provisoire. Le nom définitif n'apparaît que dans les métadonnées et se change en un seul endroit (`Directory.Build.props`).

Date : 2026-11-21 — Statut : **validée** — Remplace : `2026-11-03-pcdoc-vision-architecture-design.md` (v1)
Annexe : `2026-11-21-annexe-catalogues.md` (outils de référence, catalogue des réglages Windows, bloatwares, emplacements de persistance des malwares).
Analyse à l'origine des changements : `docs/analyse/2026-11-21-analyse-globale.md`.

### Changements depuis la v1
- Nouveaux modules : **M14 Optimisation**, **M15 Protection USB**.
- **Modes novice / expert** et quatre niveaux de risque, dont 🔒 Verrouillé.
- Conseiller de désactivation (démarrage, tâches, services), désinstallation propre avec quarantaine, bloatwares Microsoft **et** fabricants, rapport pour forums, antivirus en couches (Defender, nos règles, moteurs externes, VirusTotal).
- Corrections de sécurité de la conception : installation dans Program Files, outils externes dans un dossier protégé, désinstalleurs `HKCU` lancés sans élévation, **profil cible**, définitions **signées** (Ed25519), LibreHardwareMonitor avec PawnIO uniquement.
- Accessibilité, ARM64, compatibilité Windows 11, confidentialité de PCDoc lui-même (PRIVACY.md, page Activité réseau), chaîne de fabrication sécurisée.

---

## 1. Vision

Un **outil Windows gratuit, open source et tout-en-un** pour **comprendre, réparer, nettoyer, mettre à jour, optimiser, sécuriser et rendre confidentiel** un PC, sans logiciel propriétaire ou payant.

- **Pour les débutants (mode novice, par défaut)** : un bouton « Analyser mon PC », une liste de problèmes classés 🔴🟠🟢, un bouton « Tout corriger » qui ne peut rien casser.
- **Pour les experts (mode expert)** : tout est visible, y compris ce qu'on ne devrait normalement pas toucher, avec le détail technique (chemin, clé de registre, code d'erreur, journal).
- **Principes** : rapide, minimaliste, aucune option inutile, aucune publicité, **aucune télémétrie**.

### Décisions validées
- Diffusion **publique** sur GitHub, licence **GPL-3.0**.
- Windows **10 et 11**, **x64 et ARM64**.
- Interface **français + anglais**, changement à chaud.
- C# / WPF / .NET 10 (LTS), thème Fluent.
- Distribution : **version portable** dès la v0.1, **installateur** (Velopack, MSI *PerMachine*) en v1.0.
- **Tout se met à jour depuis l'app** : Windows, pilotes, applications du Store, logiciels, PCDoc et ses définitions.
- **100 % légal** : aucun activateur, aucun crack, aucune redistribution interdite.
- L'utilisateur est débutant : tout le code est écrit par Claude, avec des explications pour compiler, tester et publier.

### Règle de l'interface
**Une seule fenêtre.** Rien d'autre ne s'ouvre (console, Explorateur, navigateur, outil externe). Exceptions, limitées à :
1. la demande d'autorisation administrateur (UAC) de Windows ;
2. la fenêtre d'un **désinstalleur officiel sans mode silencieux** (M5). PCDoc prévient avant (« Le programme de désinstallation de X va s'ouvrir ») ;
3. l'écran de Windows pendant une **analyse Defender hors ligne**, qui se fait au redémarrage, hors de Windows.

Les notifications Windows (maintenance planifiée) ne sont pas des fenêtres et sont permises.

### Règle de choix des composants
1. Si on peut faire **aussi bien ou mieux**, ou si l'outil existant **n'est plus maintenu depuis plus de 2 ans** : on écrit notre propre fonction.
2. Sinon on **réutilise du code libre compatible GPL-3.0** (licence, date de dernière version et qualité vérifiées d'abord), en le corrigeant si besoin.
3. En dernier recours, on pilote un **outil officiel gratuit en ligne de commande, caché** (M13).

---

## 2. Ce que l'app remplace

| Besoin | Logiciels habituels | Module |
|---|---|---|
| Infos matériel et système, compatibilité Windows 11 | Speccy, HWiNFO (partiel), PC Health Check, WhyNotWin11 | M2 |
| Démarrage, tâches, services + conseils | Autoruns, Gestionnaire des tâches | M2 |
| Écrans bleus, plantages, périphériques, SMART, températures | BlueScreenView, CrystalDiskInfo, HWMonitor | M3 |
| Nettoyage disque, anciennes mises à jour, anciens pilotes | CCleaner, BleachBit, DriverStore Explorer | M4 |
| Restes de logiciels dans le registre (ciblé) | CCleaner (registre) | M4 |
| Carte de l'espace disque, doublons | WizTree, WinDirStat, dupeGuru | M4 |
| Menu clic droit encombré | ShellExView, ShellMenuView | M4 |
| Désinstallation groupée et propre, bloatwares, installation groupée | Revo Uninstaller, BCUninstaller, scripts « debloat », Ninite | M5 |
| Windows Update, pilotes, Store, logiciels | Patch My PC, SUMo, UniGetUI | M6 |
| Antivirus de second avis, adwares, pirates de navigateur, persistance | Malwarebytes, AdwCleaner, ZHPCleaner, Emsisoft EEK | M7, M13 |
| Audit de sécurité de Windows | HardeningKitty | M7 |
| Réglages de confidentialité Windows, Office et logiciels tiers | O&O ShutUp10++, DoNotSpy11, W10Privacy, privacy.sexy | M8 |
| Traces et effacement sécurisé | PrivaZer, BleachBit, Eraser | M9 |
| Réseau, ports, pare-feu, DNS | TCPView, Windows Firewall Control | M10 |
| Licences de ce PC | ProduKey (partiel) | M11 |
| Rapport pour les forums d'entraide | FRST, ZHPDiag | M12 |
| Mode d'alimentation, mémoire virtuelle, démarrage rapide, profils | « optimiseurs » divers | M14 |
| Protection des clés USB | UsbFix | M15 |

---

## 3. Architecture

### 3.1 Couches

```
PCDoc.App (WPF, Fluent)            vues XAML uniquement
PCDoc.Presentation (net10.0)       ViewModels MVVM, testables partout
PCDoc.Core (net10.0)               contrats, modèles, orchestrateur, langues, paramètres,
                                   interfaces d'accès à Windows (Abstractions)
PCDoc.Modules.* (net10.0)          logique de chaque module, un projet par module
PCDoc.Infrastructure (-windows)    implémentations : WMI, registre, journaux, services, tâches,
                                   signatures, points de restauration, winget, profil cible
PCDoc.Definitions                  fichiers JSON signés, mis à jour sans recompiler
```

- L'interface ne parle qu'aux ViewModels ; les modules ne parlent à Windows **que via les interfaces** de `Core/Abstractions`. On teste donc les modules avec de faux adaptateurs, sans Windows.
- Chaque module est **indépendant** : livrable, désactivable, réécrivable seul.
- **Un seul processus**, lancé en administrateur (manifeste `requireAdministrator`).

### 3.2 Profil cible
PCDoc peut tourner sous un autre compte que la personne devant le PC (un utilisateur standard tape le mot de passe d'un administrateur). Tout ce qui concerne « l'utilisateur » (`HKCU`, `%APPDATA%`, `%LOCALAPPDATA%`, navigateurs, dossier Démarrage, traces) passe donc par `IUserProfileResolver` : utilisateur de la session interactive → SID → `HKEY_USERS\<SID>` et dossiers de son profil. La page Paramètres affiche le compte analysé.

### 3.3 Contrat commun des modules
- **`ScanAsync()`** : lecture seule, jamais d'effet de bord, retourne des `Finding`.
- **`FixAsync(Finding)`** : applique la correction après confirmation, retourne un `FixResult` et des `UndoEntry`.

`Finding` contient : `Id`, `Module`, `Severity` (Critique 🔴 / Attention 🟠 / Info 🟢), `Title` et `Explanation` (clés traduites + arguments, langage simple), `TechnicalDetails`, `FixAvailable`, `Risk` (voir §5), `Reversible`, `EstimatedGain` facultatif.

### 3.4 Emplacements sur le disque

| Quoi | Où | Droits |
|---|---|---|
| Version installée (v1.0) | `C:\Program Files\PCDoc` (MSI *PerMachine*) | administrateurs |
| Version portable | là où l'utilisateur la décompresse | — |
| Outils externes (M13) | `%ProgramData%\PCDoc\tools` | administrateurs uniquement |
| Quarantaine | `%ProgramData%\PCDoc\quarantine` | administrateurs uniquement |
| Sauvegardes `.reg`, journal d'annulation | `%ProgramData%\PCDoc\backup` | administrateurs uniquement |
| Paramètres, journaux | `%LOCALAPPDATA%\PCDoc` du compte qui lance PCDoc | considérés **non fiables** à la lecture |

### 3.5 Définitions pilotées par données
Les listes qui évoluent souvent sont des **fichiers JSON** dans `PCDoc.Definitions` : emplacements de nettoyage et de traces, catalogue des réglages de confidentialité, catalogue du conseiller (services, tâches, démarrage), bloatwares, règles d'adwares et de pirates de navigateur, emplacements de persistance, catalogue d'outils externes, processeurs compatibles Windows 11.

- Chaque entrée indique : versions et éditions de Windows concernées, **niveau de risque** (§5), condition éventuelle (§5.3), textes FR/EN, action et action inverse.
- Les mises à jour des définitions sont **signées en Ed25519**. La clé publique est intégrée à PCDoc ; la clé privée n'existe que dans un secret GitHub. Un numéro de version croissant empêche le retour à une ancienne version. Un fichier mal signé est refusé et PCDoc garde le précédent.
- Des schémas JSON valident chaque fichier dans la CI.

---

## 4. Sécurité des actions (commun à tous les modules)

1. **Analyse toujours en lecture seule.**
2. **Confirmation** listant précisément ce qui va changer, avec le niveau de risque.
3. **Point de restauration automatique** avant chaque série de corrections. PCDoc vérifie que la protection du système est activée (et propose de l'activer). Il gère aussi la limite de Windows d'un point par 24 h : si un point récent existe déjà, il le réutilise et le dit. Si la création échoue, l'utilisateur est prévenu et doit confirmer de nouveau.
4. **Sauvegarde avant modification** : export `.reg` des clés touchées, état d'origine des services, des tâches et des réglages.
5. **Journal d'annulation transactionnel** : l'`UndoEntry` est écrite **avant** la modification, puis marquée « appliquée ». Une coupure de courant ne laisse jamais une modification sans moyen de l'annuler. Page **Historique** avec « Annuler ». Avant d'annuler, PCDoc vérifie que l'état actuel est bien celui qu'il a laissé ; sinon il prévient.
6. **Quarantaine** plutôt que suppression pour les fichiers (malwares, restes de logiciels) : 7 jours par défaut (30 pour les malwares), fichiers neutralisés (renommés, contenu chiffré, droits retirés), restauration en un clic.
7. **Niveau 🔒 Verrouillé** (§5) : intouchable dans tous les modes.
8. **Journal** texte lisible de toutes les actions, conservé 14 jours.
9. **Suppressions définitives** (traces, effacement sécurisé) signalées comme **non réversibles**, avec confirmation renforcée.
10. Après une mise à jour de Windows, le Bilan **détecte les réglages réactivés** et propose de les réappliquer en un clic.

---

## 5. Niveaux de risque et modes novice / expert

### 5.1 Quatre niveaux

| Niveau | Sens | Exemples |
|---|---|---|
| 🟢 **Sûr** | Ne casse rien | publicités du menu Démarrer, télémétrie DiagTrack, Game DVR non utilisé |
| 🟠 **Selon l'usage** | Sûr **si une condition est vraie** | Spouleur d'impression s'il n'y a aucune imprimante, services Xbox sans application Xbox |
| 🔴 **Déconseillé** | Peut gêner ou affaiblir la sécurité ; possible en expert | indexation, fichier d'échange, intégrité de la mémoire, clavier tactile |
| 🔒 **Verrouillé** | Empêcherait Windows de démarrer ou ressemble à une technique de malware ; **jamais**, même en expert | supprimer des fichiers système, désactiver RPC/DCOM, désactiver Windows Update ou Defender de façon permanente, contourner l'UAC, retirer le Microsoft Store ou winget |

### 5.2 Modes

| | Novice (par défaut) | Expert |
|---|---|---|
| Affiché | 🟢, et 🟠 seulement si la condition est vérifiée | 🟢 🟠 🔴 (🔒 affiché grisé, avec la raison) |
| « Tout corriger » | 🟢 uniquement | 🟢 uniquement |
| 🔴 | masqué | un par un, avec l'explication de ce qui peut casser et une confirmation renforcée |
| Détails techniques | repliés | dépliés |
| Réglages fins (taille de la mémoire virtuelle, délai du menu de démarrage…) | seulement « remettre la valeur recommandée » | valeurs personnalisées |
| Sécurité (§4) | complète | complète, sans exception |

Le mode se choisit au premier lancement et dans les Paramètres ; passer en expert demande une confirmation.

### 5.3 Conditions détectées automatiquement
Exemples : aucune imprimante installée, aucune application Xbox / Game Pass, pas de puce Bluetooth, Windows Hello non configuré, aucun lecteur de carte à puce, localisation désactivée, aucun compte enfant, bureau à distance jamais utilisé, PC fixe ou portable, édition de Windows. Une condition **non vérifiable** fait passer l'élément en 🔴 (masqué en novice).

### 5.4 Édition de Windows
Certaines stratégies sont ignorées sur Windows Famille, et la télémétrie « 0 » n'existe qu'en Entreprise/Éducation. PCDoc affiche alors « Non applicable sur votre édition » au lieu de faire croire que le réglage est appliqué.

---

## 6. Modules

### M0 — Socle
- Fenêtre unique, menu latéral, thème Fluent clair/sombre/système, FR/EN à chaud.
- Élévation administrateur ; **profil cible** (§3.2) ; journalisation.
- Points de restauration, sauvegarde du registre, quarantaine, historique et annulation (§4).
- **Assistant de premier lancement** : langue, mode novice/expert, vérification de la protection du système, clé VirusTotal facultative, maintenance planifiée facultative.
- **Paramètres** : langue, thème, mode, compte analysé, maintenance planifiée, vérification des mises à jour (désactivable), **« Effacer les données de PCDoc »**.
- **Activité réseau** : liste de chaque connexion faite par PCDoc (destination, date, raison).
- **Maintenance planifiée** facultative (analyse hebdomadaire, vérification des mises à jour, nettoyage 🟢) : tâche planifiée qui lance PCDoc sans fenêtre et affiche une notification Windows si quelque chose demande l'attention. Elle n'applique **que** du 🟢.
- **Droits administrateur** :
  - manifeste `requireAdministrator` ;
  - **version installée (v1.0)** dans Program Files : tâche planifiée « PCDoc » au niveau le plus élevé, lancée par les raccourcis, pour que l'UAC n'apparaisse qu'à l'installation. Elle n'est créée que si l'exécutable est dans un dossier protégé, et la signature de PCDoc est vérifiée avant chaque lancement. Désactivable dans les Paramètres, supprimée à la désinstallation ;
  - **version portable** : UAC à chaque lancement, jamais de tâche ;
  - **interdit** : toute technique de contournement de l'UAC.
- **Accessibilité** (§9).

### M1 — Bilan (page d'accueil)
- **« Analyser mon PC »** : `ScanAsync()` de tous les modules en parallèle, délai maximum par module, un module en panne n'empêche pas le résultat.
- **Note sur 100** (100 − 20 par critique − 5 par attention, bornée 0–100), libellés : Bon état (≥ 80), À surveiller (50–79), À améliorer (< 50) ; historique de la note.
- Liste triée par gravité, filtrable par module ; « Corriger » par problème ; **« Tout corriger (sûr uniquement) »**.
- Inclut la **compatibilité Windows 11** et la fin de support de Windows 10 (14/10/2025), avec les informations sur les mises à jour de sécurité étendues (ESU).

### M2 — Système
- Windows : édition, version, build, date d'installation, activation.
- Matériel : processeur, RAM (totale, utilisée, barrettes), GPU, carte mère, BIOS, disques (SSD/HDD/NVMe, espace libre), écrans, batterie (usure).
- **Compatibilité Windows 11** : TPM 2.0, Secure Boot, UEFI/GPT, processeur dans la liste Microsoft, RAM ≥ 4 Go, stockage ≥ 64 Go.
- **Temps de démarrage** (journal Diagnostics-Performance, événements 100 à 110) : historique, médiane, tendance, éléments qui ralentissent.
- **Programmes au démarrage, tâches planifiées, services, pilotes** : liste, éditeur, signature numérique (Authenticode **et catalogue**), éléments orphelins.
- **Conseiller de désactivation** : chaque élément reçoit un niveau (§5) depuis le catalogue JSON ; un élément inconnu est jugé sur sa signature, son éditeur et son impact mesuré sur le démarrage. Activer/désactiver avec sauvegarde (à partir de la v0.2).

### M3 — Diagnostic
- **Écrans bleus** : date, code traduit, pilote probablement en cause (minidumps + événements BugCheck).
- **Plantages et erreurs** : moniteur de fiabilité et journaux, expliqués en clair.
- **Périphériques en erreur** : codes du Gestionnaire de périphériques, cause et solution.
- **Lenteurs** : processus gourmands, disque presque plein, redémarrage en attente, températures via **LibreHardwareMonitor basé sur PawnIO uniquement** (jamais WinRing0, que Defender bloque). Le pilote ne s'installe qu'à la demande, avec un avertissement pour les joueurs (certains anti-triche).
- **Santé des disques** (SMART via smartmontools) et de la batterie (`powercfg /batteryreport` caché).
- **Pilotes** : liste, pilotes anciens ou problématiques.
- **Assistants guidés** : « Pas d'internet », « Pas de son », « Windows Update bloqué », « PC lent », « Imprimante ».
- **Réparations en un clic** : SFC, DISM, réinitialisation réseau, cache DNS, réparation de Windows Update.
- **Fiche « Mon PC ne démarre plus »** : que faire (environnement de récupération, restauration du système, mode sans échec), dans l'app et dans le README.

### M4 — Nettoyage
- Fichiers temporaires, anciennes mises à jour (nettoyage DISM des composants), `Windows.old`, `$WINDOWS.~BT`, `SoftwareDistribution\Download`, cache de l'Optimisation de la distribution, rapports d'erreurs, vidages après plantage, caches de shaders (DirectX, NVIDIA, AMD), cache des miniatures, corbeille, caches des navigateurs, caches des outils de développement s'ils sont présents.
- **Anciens pilotes** du magasin de pilotes (on garde toujours la version en service et la précédente).
- **Restes de logiciels dans le registre, de façon ciblée** : entrées de désinstallation vers un dossier absent, `App Paths` et associations de fichiers vers un exécutable absent, `SharedDLLs` orphelins, `MUICache`, éléments de démarrage orphelins. Toujours avec sauvegarde `.reg`. **Pas** de « nettoyeur de registre » général (§13).
- **Menu clic droit** : entrées ajoutées par les logiciels, désactivation réversible.
- Gros fichiers, **carte de l'espace disque**, **doublons** (taille puis empreinte).
- **Points de restauration** : liste, suppression, création.
- Activation de l'**Assistant Stockage** de Windows.

### M5 — Applications
- Liste des applications (Win32 + Store) : cases à cocher, recherche, tri par taille, date et éditeur.
- **Désinstallation propre** (moteur basé sur Bulk Crap Uninstaller) :
  1. point de restauration ;
  2. désinstalleur officiel en **mode silencieux** quand il existe (MSI, NSIS, Inno Setup, InstallShield…) ; sinon sa fenêtre s'ouvre, après avertissement (exception n° 2 du §1) ;
  3. **les désinstalleurs déclarés dans le registre de l'utilisateur (`HKCU`) sont lancés sans les droits administrateur**, et toute commande qui ne pointe pas vers un fichier existant est refusée ;
  4. **recherche des restes** : dossiers (Program Files, AppData, ProgramData), clés de registre de l'éditeur, services, tâches, démarrage, raccourcis, règles du pare-feu ;
  5. restes classés « sûr » / « probable » ; en novice, seuls les « sûrs » sont cochés ;
  6. **quarantaine** 7 jours.
- **Désinstallation groupée** en un clic.
- **Bloatwares** :
  - applications Microsoft préinstallées, **y compris les paquets provisionnés**, pour qu'elles ne reviennent pas sur les nouveaux comptes ;
  - logiciels des fabricants (HP, Dell, Lenovo, Asus, Acer…) et versions d'essai d'antivirus ;
  - en novice, liste recommandée cochée d'avance ; en expert, tout ce qui peut être retiré ;
  - 🔒 jamais : Microsoft Store, Programme d'installation d'application (winget), bibliothèques système, WebView2, pilotes et outils BIOS/mises à jour du fabricant ;
  - réinstallation possible depuis l'Historique.
- **Installation groupée** depuis les sources officielles via winget (catalogue de logiciels courants).

### M6 — Centre de mises à jour
Une page, cases à cocher, « Tout sélectionner », **« Installer la sélection »** :
- **Windows Update** (API officielle Windows Update Agent) : sécurité, cumulatives, facultatives ; mise en pause et report des mises à jour majeures (jamais de désactivation permanente).
- **Pilotes** : catalogue Microsoft via Windows Update + outil officiel du fabricant de la carte graphique via winget.
- **Applications du Microsoft Store** : API officielle du Store (`AppInstallManager`), winget (source `msstore`) en secours.
- **Autres logiciels** : winget, source officielle des éditeurs.
- **PCDoc et ses définitions**.
- Pour chaque élément : version installée → nouvelle, éditeur, taille, redémarrage nécessaire ; exclusion possible. Si winget manque, installation proposée depuis le Store.

### M7 — Sécurité
- **Antivirus en couches** :
  1. **Microsoft Defender** (`MpCmdRun`) : analyses rapide, complète, ciblée, hors ligne ; mise à jour des signatures ; état de la protection ;
  2. **nos règles** : adwares, logiciels indésirables, **pirates de navigateur** (proxy imposé, raccourcis de navigateur modifiés, moteur de recherche détourné, stratégies et extensions forcées, `hosts`, DNS) ;
  3. **second avis** caché (M13) : AdwCleaner, Emsisoft Emergency Kit, outil de suppression des logiciels malveillants de Microsoft ;
  4. **VirusTotal** (facultatif).
- **Emplacements de persistance analysés** (liste complète en annexe) : démarrage, tâches, services, pilotes, abonnements WMI, Winlogon, IFEO, AppInit_DLLs, fournisseurs LSA, moniteurs d'impression, détournements COM, tâches BITS, **exclusions ajoutées à Defender**, **certificats racine ajoutés**, profil PowerShell, compléments Office, portes dérobées d'accessibilité, exécutables lancés depuis `%TEMP%`/AppData, processus non signés.
- **Note de suspicion** par élément : non signé, emplacement inhabituel, récent, caché, détections VirusTotal → 🔴 ; signé par un éditeur connu → 🟢.
- **VirusTotal** : désactivé par défaut ; **clé personnelle** de l'utilisateur (compte gratuit, chiffrée par DPAPI) ; **empreinte seulement** par défaut ; envoi d'un fichier seulement après confirmation explicite (le fichier devient consultable par les clients de VirusTotal) ; respect des quotas de l'API gratuite (4 requêtes/minute, 500/jour) en priorisant les éléments suspects ; usage non commercial uniquement.
- **Processus en cours** : signature, éditeur, emplacement, vérification VirusTotal.
- **Extensions des navigateurs** (Chrome, Edge, Firefox, Brave) : liste, extensions dangereuses ou abandonnées ; **réinitialisation des navigateurs**.
- **Audit de sécurité de Windows** : SMBv1, PowerShell 2.0, bureau à distance exposé, niveau de l'UAC, Secure Boot, TPM, intégrité de la mémoire, pare-feu, compte Invité, comptes sans mot de passe, administrateurs en trop, protection LSA, Smart App Control, BitLocker.
- **Protection contre les rançongiciels** : Accès contrôlé aux dossiers de Defender.
- **Limite honnête** : aucune détection n'est garantie à 100 % ; pas de moteur antivirus maison.

### M8 — Confidentialité
Catalogue détaillé en annexe. Familles :
- **Télémétrie Windows** : données de diagnostic, DiagTrack, tâches du programme d'amélioration, commentaires, expériences personnalisées, identifiant publicitaire, historique des activités, saisie, reconnaissance vocale en ligne, collecteur d'inventaire, suivi des applications lancées.
- **IA et recherche** : Recall, Copilot, Click to Do, Cortana, recherche Bing dans le menu Démarrer.
- **Publicités et suggestions** : menu Démarrer, Spotlight, « Bienvenue » après mise à jour, **installation silencieuse d'applications sponsorisées**, publicités de l'Explorateur, widgets.
- **Autorisations des applications** : caméra, micro, contacts, agenda, localisation (l'utilisateur choisit).
- **Optimisation de la distribution** limitée au réseau local.
- **Office** : stratégies de confidentialité, tâches `OfficeTelemetryAgent*`, expériences connectées.
- **Logiciels tiers** : Edge, Chrome, Firefox (stratégies officielles), NVIDIA, Visual Studio / VS Code, PowerShell, outils .NET.

### M9 — Traces et données
- Traces par logiciel (navigateurs, WhatsApp, Discord, Teams, Slack…), historiques de Windows (documents récents, listes de raccourcis, presse-papiers).
- **Effacement sécurisé** de fichiers et de l'espace libre (`cipher /w` caché). Message honnête sur les SSD (effacement imparfait), recommandation de BitLocker.

### M10 — Réseau
- Connexions et ports ouverts (avec le programme responsable).
- Règles du pare-feu, vue simplifiée.
- Profils Wi-Fi enregistrés sur ce PC (dont leurs mots de passe, affichés à la demande).
- Choix d'un DNS (Cloudflare, Quad9, NextDNS…), réinitialisation réseau, test de débit (composant libre à vérifier).

### M11 — Licences
- État d'activation de Windows, clé **de ce PC** (OEM du BIOS ou installée), licences Office. Jamais d'activateur, jamais de clé tierce.

### M12 — Rapport
- **Rapport pour les forums d'entraide**, façon FRST / ZHPDiag : système, processus, démarrage, services et tâches hors Microsoft, extensions, proxy, DNS, `hosts`, écrans bleus, erreurs récentes, logiciels, état de Defender, fichiers non signés, résultats de M7.
- **Anonymisation automatique** : nom d'utilisateur, nom du PC, IP, numéros de série, noms des Wi-Fi ; **jamais** de clé de produit. Aperçu avant partage.
- Partage par **copie** ou fichier `.txt` dans Documents ; aucune ouverture de navigateur.
- **Préparer une réinstallation** : export des logiciels installés (`winget export`), des pilotes (`pnputil /export-driver`) et des profils Wi-Fi.
- *(Après la v1.0, et seulement en expert)* import d'une liste de corrections venant d'un aideur : uniquement des actions de notre catalogue, affichées une par une, aucune commande libre.

### M13 — Moteurs externes invisibles
- Un outil externe n'est utilisé que s'il fonctionne **en ligne de commande, sans fenêtre**, et que ses résultats peuvent être lus puis affichés dans nos pages.
- Téléchargé depuis le **site officiel**, rangé dans `%ProgramData%\PCDoc\tools` (administrateurs uniquement). **Signature et nom exact de l'éditeur vérifiés juste avant chaque lancement** ; chemin complet ; dernière version vérifiée à chaque utilisation. Jamais redistribué par nous.
- Candidats (licence et ligne de commande à vérifier avant intégration) : **AdwCleaner** (`/eula /scan`, `/eula /clean /noreboot`) ; **Emsisoft Emergency Kit** (`a2cmd`, gratuit pour usage privé, maintenance à vérifier) ; **Defender** (`MpCmdRun`) ; **outil de suppression des logiciels malveillants** (`mrt.exe /q`) ; **Windows File Recovery** (`winfr`) pour récupérer des fichiers supprimés ; outils NirSoft avec export `/scomma`, seulement si on ne fait pas aussi bien nous-mêmes.
- Les outils qui n'ont **qu'une interface graphique** (UsbFix, ZHPCleaner, ESET Online Scanner, HWiNFO, CrystalDiskInfo…) ne sont pas utilisés : leur fonction est réécrite.

### M14 — Optimisation (nouveau)

| Réglage | Novice | Expert |
|---|---|---|
| Mode d'alimentation (Économie, Équilibré, Performances élevées, Performances optimales sur PC fixe ; secteur et batterie séparés) | « Silencieux / Équilibré / Puissant » | tous les plans et réglages |
| Mémoire virtuelle | détection d'une mauvaise configuration, retour à « gérée par Windows » | taille personnalisée, déplacement |
| Démarrage rapide | explication + activer/désactiver | idem |
| Hibernation | désactiver sur PC fixe | taille du fichier |
| Programmes au démarrage, applications en arrière-plan | conseiller (M2) | tout |
| Effets visuels | « Plus rapide » sur PC peu puissant | effet par effet |
| Jeu : mode Jeu, enregistrement en arrière-plan, planification GPU, choix du GPU par application | profil « Jeu » | réglage par réglage |
| Disques : TRIM des SSD, défragmentation planifiée des HDD | automatique | détail |
| Indexation | — | dossiers indexés |
| Stockage réservé | — | 🔴 |
| Menu de démarrage (délai) | — | réglable |
| Fonctionnalités facultatives (SMB 1.0, PowerShell 2.0, XPS, Fax, Hyper-V, WSL, Bac à sable…) | retrait 🟢 | tout |

- **Profils en un clic** : « Jeu », « Bureautique », « Autonomie batterie », réversibles en un clic.
- **Mesure avant / après** : temps de démarrage, mémoire utilisée au repos, nombre de processus en arrière-plan.
- Les faux gains (nettoyeurs de RAM, réglages TCP « miracle », désactivation d'IPv6, « déblocage des cœurs »…) sont exclus (§13).

### M15 — Protection USB (nouveau)
- À l'insertion d'une clé ou d'un disque externe (quand PCDoc est ouvert ou via la maintenance planifiée) : analyse Defender ciblée.
- Détection des **faux raccourcis** `.lnk` qui lancent un programme, des **fichiers rendus cachés** par un virus (restauration des attributs), des `autorun.inf` suspects, des exécutables et scripts à la racine.
- Vérification que l'exécution automatique est désactivée pour les supports amovibles.
- Fonction réécrite : UsbFix est propriétaire et graphique uniquement.

---

## 7. Code open source réutilisé

| Projet | Licence | Usage |
|---|---|---|
| Bulk Crap Uninstaller | Apache-2.0 | Désinstallation et restes (M5) |
| UniGetUI | MIT | Inspiration / code winget (M5, M6) |
| winget | MIT | Installation et mises à jour (M5, M6) |
| BleachBit (CleanerML) | GPL-3.0 | Emplacements de nettoyage et de traces (M4, M9), convertis en JSON |
| privacy.sexy | AGPL-3.0 | Réglages de confidentialité (M8), données seulement ; compatible GPL-3.0 (art. 13), crédit et licence conservés |
| WinUtil | MIT | Réglages, bloatwares, catalogue du conseiller (M2, M5, M8) |
| Win11Debloat | MIT | Bloatwares (M5) |
| LibreHardwareMonitor (versions PawnIO) | MPL-2.0 | Températures (M3) |
| smartmontools | GPL-2.0-or-later | SMART, exécutable séparé (M3) |
| WhyNotWin11 | LGPL-3.0 | Inspiration des contrôles de compatibilité Windows 11 (M2) |
| Velopack | MIT | Installateur, mises à jour de PCDoc |
| CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, System.Management | MIT | Socle |
| Serilog | Apache-2.0 | Journal |
| TaskScheduler (dahall) | MIT | Lecture des tâches planifiées |

Réécrits par nous : façon Autoruns, BlueScreenView, WizTree, dupeGuru, ShellExView, ZHPCleaner, UsbFix, O&O ShutUp10++ / DoNotSpy11, TCPView, conseiller de désactivation.
Tous les composants sont listés dans « À propos → Composants » avec leur licence et leur version.

---

## 8. Confidentialité de PCDoc lui-même
- **Aucune télémétrie**, aucun compte, aucun serveur à nous. Promesse écrite dans **PRIVACY.md** (FR/EN) et dans l'app.
- Seuls appels réseau, tous listés dans la page **Activité réseau** : vérification des mises à jour (GitHub, désactivable, révèle l'adresse IP à GitHub), définitions (GitHub), winget et Store, Windows Update, téléchargements officiels demandés, VirusTotal (si activé).
- Journaux : 14 jours, effaçables ; tout est supprimé à la désinstallation.
- Aucun rapport de plantage envoyé automatiquement.

---

## 9. Interface
- **Une seule fenêtre** (règle du §1). Thème Fluent Windows 11, clair/sombre/système, coins arrondis, icônes cohérentes, y compris pour les résultats des moteurs externes.
- Menu latéral : Bilan, Système, Diagnostic, Nettoyage, Applications, Mises à jour, Optimisation, Sécurité, Protection USB, Confidentialité, Traces, Réseau, Licences, Rapport, Historique, Paramètres. Chaque entrée n'apparaît qu'avec la version qui livre son module.
- Chaque page suit le même schéma : en-tête avec « Analyser », liste avec cases à cocher et pastilles de gravité, barre d'action en bas.
- **Recherche globale** dans toutes les pages (v0.6).
- **Accessibilité** : tout au clavier avec focus visible ; noms d'automatisation pour les lecteurs d'écran (Narrateur, NVDA) ; contraste élevé de Windows respecté ; texte agrandi jusqu'à 225 % sans texte coupé ; la gravité n'est jamais indiquée par la couleur seule (pastille + mot + icône).
- **Performance de l'interface** : fenêtre affichée en moins de 2 s ; listes longues **virtualisées** ; analyses lourdes en priorité basse, annulables, reportées sur batterie.
- Maquettes validées avant chaque interface (`docs/design/`).

---

## 10. Sécurité de PCDoc lui-même
- **Exécution** : chemins complets pour tout processus ; recherche de DLL limitée à System32 ; tout ce qui n'a pas été écrit par PCDoc en administrateur est lu comme **non fiable** (`settings.json`, journaux, `HKCU`, rapports d'outils, raccourcis) ; aucune commande exécutée depuis ces sources ; tests à données piégées (fuzzing) des analyseurs maison.
- **Chaîne de fabrication** : double authentification, branche `main` protégée, actions GitHub épinglées par SHA, permissions minimales, Dependabot, CodeQL, OpenSSF Scorecard, vérification des paquets vulnérables, fichiers de verrouillage NuGet, builds déterministes, **SBOM CycloneDX** et sommes SHA-256 jointes à chaque version.
- **Signature du code** en v1.0 (SignPath Foundation ou Azure Trusted Signing, conditions à vérifier).
- **Faux positifs** : pas de compression ni d'obfuscation ; chaque version testée sur VirusTotal et soumise à Microsoft avant annonce.
- **SECURITY.md** : signalement privé des failles via GitHub.

---

## 11. Feuille de route

Chaque version a son plan d'implémentation et livre une version utilisable. À partir de M3, chaque module a sa propre spécification détaillée avant son plan.

| Version | Contenu |
|---|---|
| **v0.1** | M0 Socle (lecture seule : fenêtre, langues, thème, journal, profil cible, paramètres) + M2 Système (lecture seule, avec compatibilité Windows 11) + M1 Bilan minimal |
| v0.2 | Sécurité des actions (§4) + niveaux et **modes novice/expert** (§5) + assistant de premier lancement + conseiller et activer/désactiver (M2) + Historique |
| v0.3 | M3 Diagnostic |
| v0.4 | M4 Nettoyage (catégories, corbeille, anciennes mises à jour, gros fichiers) |
| v0.5 | M5 Applications (désinstallation propre avec quarantaine, bloatwares, installation groupée) |
| v0.6 | M6 Centre de mises à jour + M14 Optimisation + Activité réseau + maintenance planifiée |
| v0.7 | M7 Sécurité + M13 Moteurs externes + M15 Protection USB |
| v0.8 | M8 Confidentialité + recherche globale |
| v0.9 | M9 Traces et données |
| v0.10 | M10 Réseau + M11 Licences + M12 Rapport |
| **v1.0** | Signature du code, installateur MSI *PerMachine*, tâche planifiée élevée, mise à jour automatique, page de téléchargement, mode portable pour dépanneurs |
| après v1.0 | Import de liste de corrections (expert), traductions communautaires (Weblate) |

---

## 12. Build, tests et distribution
- **Build** : solution .NET 10 compilée par **GitHub Actions** sur runner Windows à chaque modification, pour **x64 et ARM64** ; artefacts téléchargeables ; ReadyToRun.
- **Tests automatiques** : tests unitaires (xUnit) avec faux adaptateurs, exécutables sous Linux et Windows ; tests d'intégration en lecture seule sur runner Windows ; à partir de la v0.2, tests des corrections sur le runner (machine jetable, mais Windows Server).
- **Tests manuels** : sur le PC de l'utilisateur, dans des VM Windows 10 et 11 et dans le Bac à sable Windows. Les actions destructrices ne sont jamais testées ailleurs que dans une VM, le Bac à sable ou le runner.
- **Matrice de compatibilité** : Windows 10 22H2, Windows 11 23H2 / 24H2 / 25H2 ; Famille et Pro ; Windows en français et en anglais ; x64 et ARM64 ; utilisateur standard qui saisit un mot de passe administrateur.
- **Distribution** : version portable (zip + SHA-256) dès la v0.1 en prérelease ; installateur **Velopack MSI PerMachine** (Program Files) en v1.0, qui peut installer le runtime .NET à part pour rester léger.

---

## 13. Hors périmètre
- Activateurs, cracks, contournement de licences ou de l'UAC.
- Redistribution de logiciels dont la licence l'interdit.
- Pilotes provenant de sites non officiels.
- Moteur antivirus maison à base de signatures (on s'appuie sur Defender).
- **« Nettoyeur de registre » général** : non pris en charge par Microsoft, sans gain réel ; seul le nettoyage ciblé des restes (M4) est fait.
- **Faux optimiseurs** : nettoyeurs de RAM, défragmentation des SSD, réglages TCP « miracle », désactivation d'IPv6, « déblocage des cœurs » dans msconfig, vidage manuel de WinSxS.
- **Suppression forcée d'Edge ou de OneDrive** (casse les mises à jour) : on les désactive proprement.
- **Outils Kaspersky** (KVRT, TDSSKiller) : interdits aux États-Unis depuis 2024.
- Service résident ou protection en temps réel (rôle de Defender).
- macOS, Linux, Windows 7/8.

---

## 14. Risques connus

| Risque | Parade |
|---|---|
| PCDoc détourné pour obtenir les droits admin | Program Files, outils dans un dossier protégé, vérification de signature avant lancement, `HKCU` lancé sans élévation (§3.4, M0, M5, M13) |
| Mauvais compte analysé ou nettoyé | Profil cible (§3.2) |
| Définitions falsifiées | Signature Ed25519, numéro de version croissant (§3.5) |
| Faux positifs antivirus (dont pilote WinRing0) | Code ouvert, signature, pas d'obfuscation, PawnIO uniquement, soumission à Microsoft |
| Correction qui casse quelque chose | Niveaux de risque, mode novice, point de restauration, sauvegarde, journal transactionnel, quarantaine, annulation |
| Protection du système désactivée / limite d'un point par 24 h | Vérification et activation proposée, réutilisation du point récent (§4) |
| Windows réactive des réglages ou réinstalle des bloatwares | Détection dans le Bilan, réapplication en un clic, retrait des paquets provisionnés |
| Les logiciels changent leurs emplacements | Définitions JSON mises à jour sans recompiler |
| Arnaques via le rapport ou l'import de corrections | Anonymisation, aperçu, import reporté et limité au catalogue |
| Quotas ou conditions de VirusTotal | Clé personnelle, priorisation, usage non commercial |
| Outil externe abandonné | Règle des 2 ans, revue à chaque version |
| Effacement imparfait sur SSD | Message honnête + BitLocker |
| Pas de Windows dans l'environnement de développement | GitHub Actions (runner Windows) + tests manuels de l'utilisateur |
