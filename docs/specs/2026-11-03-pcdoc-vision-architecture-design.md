# PCDoc — Vision et architecture (spécification maître)

> **PCDoc** est un nom de code provisoire. Le nom définitif sera choisi plus tard ; il n'apparaît que dans les métadonnées et se change en un seul endroit.

Date : 2026-11-03 — Statut : validée

---

## 1. Vision

Un **outil Windows gratuit, open source et multifonction** qui centralise tout ce qu'il faut pour **comprendre, réparer, nettoyer, sécuriser et rendre confidentiel** un PC, sans avoir besoin de logiciels propriétaires ou payants.

- **Pour les débutants** : un bouton « Analyser mon PC », une liste de problèmes classés 🔴🟠🟢, un bouton « Corriger » ou « Tout corriger ».
- **Pour les experts** : chaque problème s'ouvre sur le détail technique (chemin, clé de registre, code d'erreur, journal).
- **Principe** : rapide, minimaliste, aucune option inutile, aucune publicité, aucune télémétrie.

### Ce qui a été dit (décisions validées)
- Diffusion **publique**.
- Windows **10 et 11**.
- Interface **français + anglais**, choix dans l'app.
- Technologie **moderne** : C# / WPF / .NET 10 (LTS) avec thème Fluent.
- Deux distributions : **installateur léger** + **version portable**.
- L'utilisateur est débutant : tout le code est écrit par Claude.
- Inclure les réglages de confidentialité façon DoNotSpy11 / O&O ShutUp10++.
- Inclure diagnostic, sécurité, scan de malwares/adwares, temps de démarrage.
- Code publié sur GitHub sous licence **GPL-3.0**.
- Réutiliser le code libre compatible, sinon réécrire, sinon télécharger l'outil officiel (voir §6).
- **Tout se met à jour depuis l'app**, y compris PCDoc lui-même.
- Rester **100 % légal** : aucun activateur, aucun crack, aucune redistribution de logiciel dont la licence l'interdit.

### Hypothèses (à corriger si besoin)
- Pas de compte, pas de serveur : tout fonctionne en local. Seuls appels réseau : winget, VirusTotal (optionnel, hash uniquement), téléchargements officiels demandés par l'utilisateur, vérification de mise à jour de l'app.

---

## 2. Ce que l'app remplace

| Besoin | Logiciel habituel | Module PCDoc |
|---|---|---|
| Infos matériel et système | Speccy, HWiNFO (partiel) | Système |
| Programmes au démarrage, tâches, services | Autoruns | Système |
| Nettoyage disque | CCleaner, BleachBit | Nettoyage |
| Carte de l'espace disque | WizTree, WinDirStat | Nettoyage |
| Doublons | dupeGuru | Nettoyage |
| Désinstallation groupée + restes | Revo Uninstaller, BCUninstaller | Applications |
| Suppression des bloatwares | scripts « debloat » | Applications |
| Mise à jour des logiciels | Patch My PC, SUMo | Mises à jour |
| Écrans bleus | BlueScreenView | Diagnostic |
| Santé des disques | CrystalDiskInfo | Diagnostic |
| Températures | HWMonitor | Diagnostic |
| Adwares / indésirables | AdwCleaner, Malwarebytes (partiel) | Sécurité + moteur AdwCleaner en arrière-plan |
| Windows Update, pilotes, Store | Driver Booster, outils fabricants | Centre de mises à jour |
| Installation groupée de logiciels | Ninite | Applications |
| Réglages de confidentialité | O&O ShutUp10++, DoNotSpy11 | Confidentialité Windows |
| Traces et données récupérables | PrivaZer, BleachBit | Traces et données |
| Rapport de dépannage | — | Rapport |

---

## 3. Architecture

### 3.1 Couches

```
┌─────────────────────────────────────────────┐
│  PCDoc.App (WPF, Fluent)                    │  Fenêtres, pages, menu latéral,
│  MVVM — aucune logique système ici          │  dialogues de confirmation
├─────────────────────────────────────────────┤
│  PCDoc.Core                                 │  Contrats (IScanner, IFixer),
│  Modèles (Finding, FixResult, UndoEntry)    │  orchestrateur du Bilan
├─────────────────────────────────────────────┤
│  PCDoc.Modules.*  (un projet par module)    │  Logique métier de chaque module
├─────────────────────────────────────────────┤
│  PCDoc.Infrastructure                       │  Accès Windows : WMI, registre,
│  (adaptateurs derrière des interfaces)      │  services, tâches, journaux,
│                                             │  points de restauration, winget,
│                                             │  journalisation, langues
└─────────────────────────────────────────────┘
        PCDoc.Definitions (fichiers JSON)        Listes mises à jour sans recompiler
```

- L'interface ne parle **qu'au Core**. Les modules ne parlent **qu'à l'Infrastructure via des interfaces**. On peut donc tester les modules avec de faux adaptateurs, sans Windows.
- Chaque module est **indépendant** : on peut le livrer, le désactiver ou le réécrire sans toucher aux autres.

### 3.2 Contrat commun des modules

Chaque module expose :
- **`ScanAsync()`** — lecture seule, jamais d'effet de bord. Retourne une liste de `Finding`.
- **`FixAsync(Finding)`** — applique la correction après confirmation. Retourne un `FixResult` et une ou plusieurs `UndoEntry`.

`Finding` contient :
- `Id`, `Module`, `Severity` (Critique 🔴 / Attention 🟠 / Info 🟢)
- `Title` et `Explanation` (textes traduits, langage simple)
- `TechnicalDetails` (pour les experts)
- `FixAvailable`, `FixRisk` (Sûr / Modéré / Avancé), `Reversible` (oui/non)
- `EstimatedGain` facultatif (Mo libérés, secondes gagnées au démarrage…)

### 3.3 Définitions pilotées par données

Les listes qui évoluent souvent sont dans des **fichiers JSON** versionnés dans `PCDoc.Definitions`, et non dans le code :
- emplacements de traces par logiciel (WhatsApp, Discord, Teams, Slack, navigateurs…),
- réglages de confidentialité Windows (clé, valeur, version de Windows concernée, risque),
- liste des bloatwares,
- règles de détection des adwares,
- emplacements de nettoyage.

L'app peut télécharger une version plus récente de ces définitions depuis le dépôt GitHub (signée, vérifiée par hash), sans mettre à jour l'exécutable.

---

## 4. Sécurité des actions (commun à tous les modules)

1. **Analyse toujours en lecture seule.** Rien n'est modifié tant que l'utilisateur n'a pas cliqué sur « Corriger ».
2. **Confirmation** listant précisément ce qui va changer, avec le niveau de risque.
3. **Point de restauration automatique** avant toute série de corrections (une fois par session de corrections ; si la création échoue, l'utilisateur est prévenu et doit confirmer de nouveau).
4. **Sauvegarde avant modification** : export `.reg` des clés touchées, état d'origine des services et des tâches planifiées.
5. **Journal d'annulation** : chaque correction réversible enregistre une `UndoEntry` ; page « Historique » avec bouton « Annuler ».
6. **Liste d'exclusion intégrée** : services, tâches et fichiers critiques de Windows qui ne peuvent jamais être désactivés ou supprimés.
7. **Journal** texte lisible de toutes les actions (`%LOCALAPPDATA%\PCDoc\logs`), utilisable pour le support.
8. Les suppressions définitives (traces de confidentialité) sont signalées comme **non réversibles** et demandent une confirmation renforcée.

---

## 5. Modules

### M0 — Socle
Fenêtre principale, menu latéral, thème Fluent clair/sombre, langues FR/EN, élévation administrateur, journalisation, points de restauration, sauvegarde du registre, historique et annulation, paramètres.

### Droits administrateur (partie du Socle M0)
- Le manifeste de l'app contient `requireAdministrator` : l'app démarre toujours avec les droits administrateur.
- **Version installée** : l'installateur crée une tâche planifiée « PCDoc » (niveau d'exécution le plus élevé, déclenchée à la demande). Le raccourci du bureau et du menu Démarrer lance l'app via cette tâche : la demande d'autorisation Windows (UAC) n'apparaît **qu'une fois, à l'installation**. Case dans les Paramètres pour désactiver ce mode (retour à l'UAC à chaque lancement). La tâche est supprimée à la désinstallation.
- Ne fonctionne que si l'utilisateur est administrateur du PC ; sinon, UAC classique avec saisie du mot de passe administrateur.
- **Version portable** : UAC à chaque lancement (rien n'est installé).
- **Interdit** : toute technique de contournement de l'UAC (méthodes utilisées par les malwares).

### M1 — Bilan (page d'accueil)
- Bouton **« Analyser mon PC »** : lance `ScanAsync()` de tous les modules en parallèle.
- Résultat : note globale + liste des problèmes triés par gravité, filtrables par module.
- Boutons **« Corriger »** par problème et **« Tout corriger (sûr uniquement) »**.

### M2 — Système
- Version et édition de Windows, build, date d'installation, état d'activation.
- Processeur, RAM (totale, utilisée, barrettes), carte(s) graphique(s), carte mère, BIOS, disques (taille, type SSD/HDD, espace libre), écrans, batterie.
- **Temps de démarrage** (journal Microsoft-Windows-Diagnostics-Performance, événements 100 à 110) : historique, tendance, programmes et pilotes qui ralentissent.
- **Programmes au démarrage**, **tâches planifiées**, **services** : liste, éditeur, signature numérique, activer/désactiver, signalement des éléments non signés.

### M3 — Diagnostic
- **Écrans bleus** : date, code d'erreur traduit, pilote probablement en cause (minidumps + événements BugCheck).
- **Plantages et erreurs** : moniteur de fiabilité et journaux, expliqués en clair.
- **Périphériques en erreur** : codes d'erreur du Gestionnaire de périphériques, cause et solution.
- **Lenteurs** : processus gourmands en CPU / RAM / disque, températures (LibreHardwareMonitor), disque presque plein, redémarrage en attente.
- **Santé des disques** (SMART via smartmontools) et de la batterie (rapport d'usure).
- **Pilotes** : liste, pilotes anciens ou problématiques (mise à jour via le Centre de mises à jour, M6).
- **Assistants guidés** : « Pas d'internet », « Pas de son », « Windows Update bloqué », « PC lent », « Imprimante ».
- **Réparations en un clic** : SFC, DISM, réinitialisation réseau, vidage du cache DNS, réparation de Windows Update.

### M4 — Nettoyage
- Fichiers temporaires, cache et anciennes mises à jour de Windows Update (DISM component cleanup), vidages après plantage, corbeille, caches des navigateurs.
- **Gros fichiers** et **carte de l'espace disque**.
- **Doublons** (comparaison par taille puis hash).
- **Points de restauration** : liste, suppression, création.
- Réglage de l'hibernation, mode d'alimentation.

### M5 — Applications
- Liste des applications installées (Win32 + Microsoft Store) avec cases à cocher, recherche, tri par taille / date / éditeur.
- **Désinstallation groupée en un clic**, en mode silencieux quand c'est possible (moteur basé sur Bulk Crap Uninstaller).
- **Installation groupée** : catalogue de logiciels courants (navigateurs, lecteurs, bureautique…) installés depuis leur source officielle via winget, en cochant puis en un clic.
- **Recherche des restes** après désinstallation (dossiers, clés de registre), suppression après confirmation.
- **Bloatwares** préinstallés : liste recommandée, suppression en un clic.

### M6 — Centre de mises à jour
Une seule page, quatre sources officielles, cases à cocher, « Tout sélectionner » et **« Installer la sélection » en un clic** :
- **Windows Update** (API officielle Windows Update Agent) : mises à jour de sécurité, cumulatives, facultatives.
- **Pilotes** : catalogue officiel Microsoft via Windows Update, plus outil officiel du fabricant de la carte graphique (NVIDIA, AMD, Intel) proposé via winget.
- **Applications du Microsoft Store** (winget, source `msstore`).
- **Autres logiciels** (winget, source officielle des éditeurs).
- Chaque élément affiche : version installée → nouvelle version, éditeur, taille, besoin de redémarrage.
- Option d'exclusion (« ne jamais mettre à jour ce logiciel »).
- Si winget est absent : installation proposée depuis le Microsoft Store officiel.

### M13 — Moteurs externes invisibles
Règle : **aucune autre fenêtre ne s'ouvre jamais**. Un outil externe n'est utilisé que s'il peut fonctionner **en ligne de commande, en arrière-plan, sans fenêtre**, et que ses résultats peuvent être lus puis affichés dans notre interface.
- L'app télécharge l'outil depuis le **site officiel**, vérifie sa **signature numérique**, le range dans `%LOCALAPPDATA%\PCDoc\tools`, le lance caché, lit son rapport et affiche les résultats dans nos pages (avec nos boutons « Corriger »).
- Vérification de la dernière version à chaque utilisation.
- Candidats, sous réserve de vérification de la licence et du mode ligne de commande :
  - **Malwarebytes AdwCleaner** (mode ligne de commande : analyse et nettoyage, rapport lu par PCDoc) — complète notre propre détecteur d'adwares (M7).
  - **Emsisoft Emergency Kit** (scanner en ligne de commande `a2cmd`).
  - **Microsoft Defender** (`MpCmdRun`, déjà dans Windows).
- Les outils qui n'ont **qu'une interface graphique** (ESET Online Scanner, HWiNFO, CrystalDiskInfo, Sysinternals en mode fenêtre…) **ne sont pas utilisés** : leur fonction est réécrite dans PCDoc.
- Seule fenêtre Windows inévitable : la demande d'autorisation administrateur (UAC) au lancement de PCDoc.

---

## 6. Code open source réutilisé, outils réécrits, outils externes

Règle de choix, dans cet ordre :
1. Si nous pouvons faire **aussi bien ou mieux** nous-mêmes, ou si le projet existant **n'est plus maintenu** (aucune version depuis plus de 2 ans), **on écrit notre propre fonction**.
2. Sinon, **réutiliser** le code libre quand sa licence est compatible avec la GPL-3.0 (en le corrigeant et l'améliorant si besoin), sinon **réécrire** la fonction.
3. En dernier recours, pour un outil propriétaire gratuit **utilisable sans fenêtre**, le piloter en arrière-plan (M13).

Avant chaque intégration : vérification de la licence, de la date de dernière version et de la qualité du code. Tous les composants sont listés dans « À propos → Composants », avec leur licence et leur version.

| Projet | Licence | Usage dans PCDoc |
|---|---|---|
| Bulk Crap Uninstaller | Apache-2.0 (C#) | Moteur de désinstallation groupée et de recherche des restes (M5) |
| UniGetUI | MIT (C#) | Inspiration / code de l'interface winget (M5, M6) |
| winget | MIT | Installation et mises à jour (M5, M6) |
| BleachBit (définitions CleanerML) | GPL-3.0 | Emplacements de nettoyage et de traces (M4, M9), convertis en JSON |
| privacy.sexy | AGPL-3.0 | Source de réglages de confidentialité (M8) — données uniquement, crédit et licence conservés |
| WinUtil (Chris Titus) | MIT | Réglages, liste de bloatwares (M5, M8) |
| Win11Debloat | MIT | Liste de bloatwares (M5) |
| LibreHardwareMonitor | MPL-2.0 (C#) | Températures et capteurs (M3) |
| smartmontools | GPL-2.0 | Santé des disques, exécutable séparé (M3) |
| Velopack | MIT | Installateur, version portable, mise à jour de PCDoc |

Réécrits par nous (outils propriétaires sans code disponible) : Autoruns-like (M2), BlueScreenView-like (M3), carte disque WizTree-like (M4), doublons (M4), détecteur d'adwares (M7), réglages façon O&O ShutUp10++ / DoNotSpy11 (M8).

### Tout se met à jour depuis l'app
- **PCDoc lui-même** : Velopack vérifie les nouvelles versions sur GitHub Releases et se met à jour en un clic.
- **Composants intégrés** (bibliothèques ci-dessus) : mis à jour avec chaque version de PCDoc.
- **Définitions JSON** (traces, réglages, bloatwares, règles d'adwares, catalogue d'outils) : mises à jour indépendantes, plus fréquentes.
- **Moteurs externes** (M13) : toujours la dernière version officielle.
- **Logiciels, pilotes, Windows** : Centre de mises à jour (M6).

---

## 7. Interface

- **Une seule fenêtre** : tout se voit et se fait depuis PCDoc ; aucun autre programme n'ouvre de fenêtre (consoles, outils, assistants Windows). Les commandes système (SFC, DISM, winget…) tournent cachées, leur progression s'affiche dans PCDoc.
- **Une interface commune moderne** (thème Fluent Windows 11, clair/sombre, coins arrondis, icônes cohérentes) pour toutes les fonctions, y compris les résultats des moteurs externes : l'utilisateur ne voit jamais un patchwork d'outils au look ancien.
- Menu latéral : Bilan, Système, Diagnostic, Nettoyage, Applications, Mises à jour, Sécurité, Confidentialité, Traces, Réseau, Licences, Historique, Paramètres.
- Chaque page suit le même schéma : en-tête avec bouton « Analyser », liste avec cases à cocher et pastilles de gravité, barre d'action en bas (« Corriger la sélection », « Tout corriger »).
- Le design détaillé (maquettes) sera validé avant l'implémentation de l'interface de la v0.1.

---

## 8. Feuille de route

Chaque étape fait l'objet d'un plan d'implémentation séparé et livre une version utilisable.

| Version | Contenu |
|---|---|
| **v0.1** | M0 Socle + M2 Système (lecture seule) + M1 Bilan minimal |
| v0.2 | M3 Diagnostic |
| v0.3 | M4 Nettoyage + M5 Applications |
| v0.4 | M6 Centre de mises à jour |
| v0.5 | M7 Sécurité + M13 Moteurs externes |
| v0.6 | M8 Confidentialité Windows |
| v0.7 | M9 Traces et données |
| v0.8 | M10 Réseau + M11 Licences + M12 Rapport |
| **v1.0** | Signature du code, installateur, mise à jour automatique, page de téléchargement |

Chaque module (à partir de M3) aura sa propre spécification détaillée avant son plan.

---

## 9. Build, tests et distribution

- **Build** : solution .NET 10, compilée par **GitHub Actions** sur un runner Windows à chaque modification ; artefacts téléchargeables.
- **Tests automatiques** : tests unitaires des modules avec de faux adaptateurs (xUnit) ; tests d'intégration en lecture seule sur le runner Windows.
- **Tests manuels** : sur le PC de l'utilisateur et dans une machine virtuelle Windows 10 et Windows 11 (les actions destructrices ne sont jamais testées ailleurs que dans une VM).
- **Distribution** : **Velopack** (MIT) pour produire l'installateur léger, la version portable et la mise à jour automatique de l'app.
- **Signature** : SignPath Foundation (gratuit pour l'open source) ou Azure Trusted Signing (payant, faible coût), pour limiter SmartScreen et les faux positifs antivirus.
- **Licences tierces** : winget (MIT), LibreHardwareMonitor (MPL-2.0), smartmontools (GPL-2.0, exécutable séparé), Velopack (MIT) — crédités dans « À propos ».

---

## 10. Hors périmètre

- Activateurs, cracks, contournement de licences.
- Redistribution de logiciels dont la licence l'interdit.
- Pilotes provenant de sites non officiels (« driver packs », sites tiers).
- Moteur antivirus maison à base de signatures (on s'appuie sur Defender).
- macOS, Linux, Windows 7/8.

---

## 11. Risques connus

| Risque | Parade |
|---|---|
| Faux positifs antivirus | Code open source, signature, pas d'obfuscation, soumission aux éditeurs |
| Correction qui casse quelque chose | Point de restauration, sauvegarde `.reg`, liste d'exclusion, annulation |
| Les logiciels changent l'emplacement de leurs données | Définitions JSON mises à jour sans recompiler |
| Windows réactive des réglages après mise à jour | Détection dans le Bilan, réapplication en un clic |
| Effacement imparfait sur SSD | Message honnête + recommandation BitLocker |
| Pas de Windows dans l'environnement de développement | Build et tests sur GitHub Actions (runner Windows) + tests manuels de l'utilisateur |
