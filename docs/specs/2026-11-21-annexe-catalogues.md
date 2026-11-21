# PCDoc — Annexe à la spécification v2 : catalogues de départ

Date : 2026-11-21. Ces listes sont le **point de départ** des fichiers JSON de `PCDoc.Definitions`. Chaque entrée sera vérifiée sur Windows 10 et 11 avant d'être livrée. Niveaux : 🟢 Sûr · 🟠 Selon l'usage · 🔴 Déconseillé (expert) · 🔒 Verrouillé.

---

## A. Outils de référence et décision d'intégration

| Outil | Ce qu'on reprend | Décision |
|---|---|---|
| CCleaner (registre) | Restes de logiciels désinstallés uniquement | Réécrit (M4) |
| Autoruns, Gestionnaire des tâches | Démarrage, tâches, services, impact | Réécrit (M2) |
| BleachBit, Nettoyage de disque | Emplacements de nettoyage | Code libre (CleanerML → JSON) + commandes Windows cachées (M4) |
| DriverStore Explorer | Anciens pilotes | Réécrit ou code libre si licence compatible (à vérifier) (M4) |
| ShellExView, ShellMenuView | Menu clic droit | Réécrit (M4) |
| WizTree, WinDirStat, dupeGuru | Carte du disque, doublons | Réécrit (M4) |
| Revo Uninstaller, BCUninstaller | Désinstallation propre, restes | Code libre BCU (M5) |
| Scripts debloat, Win11Debloat, WinUtil | Bloatwares, réglages | Code libre, données (M5, M8) |
| Patch My PC, SUMo, UniGetUI | Mises à jour | winget + API officielles (M6) |
| UsbFix | Protection USB | Réécrit (M15) — propriétaire, graphique |
| ZHPCleaner | Pirates de navigateur | Réécrit (M7) — propriétaire, graphique |
| Malwarebytes AdwCleaner | Adwares | Moteur caché (`/eula /scan`, `/eula /clean /noreboot`), licence à relire (M13) |
| Emsisoft Emergency Kit | Second avis | Moteur caché (`a2cmd`), usage privé, maintenance à vérifier (M13) |
| Microsoft Defender, MRT | Antivirus | Moteurs cachés (`MpCmdRun`, `mrt.exe /q`) (M7, M13) |
| VirusTotal | Avis de plus de 70 antivirus | API, empreinte, clé personnelle (M7) |
| HardeningKitty | Audit de sécurité | Réécrit (M7) |
| Process Explorer, TCPView | Processus, connexions | Réécrit (M7, M10) |
| Windows Firewall Control | Pare-feu | Réécrit, version simplifiée (M10) |
| O&O ShutUp10++, DoNotSpy11, W10Privacy, privacy.sexy | Confidentialité | Réécrit + données privacy.sexy / WinUtil (M8) |
| PrivaZer, Eraser | Traces, effacement sécurisé | Réécrit + `cipher /w` (M9) |
| FRST, ZHPDiag | Rapport pour forums | Réécrit (M12) |
| PC Health Check, WhyNotWin11 | Compatibilité Windows 11 | Réécrit (M2) |
| Windows File Recovery | Récupération de fichiers | Moteur caché (`winfr`) (M13) |
| NirSoft (BlueScreenView, WifiInfoView…) | Divers | Moteur caché `/scomma` seulement si on ne fait pas aussi bien |
| Black Viper (services) | Conseils de services | Non utilisé : plus maintenu. Catalogue maison |
| KVRT, TDSSKiller (Kaspersky) | — | Exclu (interdit aux États-Unis) |
| Nettoyeurs de RAM, « boosters » | — | Exclu |

---

## B. Réglages Windows : surveillance et collecte (M8)

| Réglage | Niveau | Remarque |
|---|---|---|
| Données de diagnostic au minimum | 🟢 | « 0 » seulement en Entreprise/Éducation ; minimum réel 1 sur Famille/Pro |
| Service DiagTrack | 🟢 | |
| Tâches : Microsoft Compatibility Appraiser, ProgramDataUpdater, Consolidator, UsbCeip, DiskDiagnosticDataCollector, Feedback\Siuf | 🟢 | |
| Fréquence des commentaires : jamais | 🟢 | |
| Expériences personnalisées | 🟢 | |
| Identifiant publicitaire | 🟢 | |
| Historique des activités | 🟢 | |
| Personnalisation de l'écriture et de la frappe | 🟢 | |
| Reconnaissance vocale en ligne | 🟢 | la dictée hors ligne reste |
| Collecteur d'inventaire, Enregistreur d'actions | 🟢 | |
| Suivi des applications lancées | 🟢 | supprime « Les plus utilisées » |
| Recall | 🟢 | PC Copilot+ |
| Copilot, Click to Do, Cortana | 🟢 | |
| Recherche Bing dans le menu Démarrer | 🟢 | |
| Rapport d'erreurs Windows (envoi) | 🟠 | les vidages locaux restent |
| Localisation | 🟠 | météo, heure automatique, Localiser mon appareil |
| Localiser mon appareil | 🟠 | utile sur portable |
| Synchronisation des paramètres | 🟠 | |
| Autorisations des applications | 🟠 | choix de l'utilisateur |
| Optimisation de la distribution : réseau local seulement | 🟢 | |

**Publicités et suggestions (toutes 🟢)** : suggestions du menu Démarrer et des Paramètres ; Spotlight et astuces de l'écran de verrouillage ; « Bienvenue » / « Terminer la configuration » ; **installation silencieuse d'applications sponsorisées** ; publicités de l'Explorateur ; widgets / Actualités et centres d'intérêt ; conseils dans les notifications.

**Office** : stratégies `Policies\Microsoft\office\16.0\common\privacy`, tâches `OfficeTelemetryAgentLogOn` / `OfficeTelemetryAgentFallBack`, expériences connectées (🟠 : certaines fonctions en ligne disparaissent).

**Logiciels tiers** : Edge (télémétrie, *Startup boost*, arrière-plan), Chrome et Firefox (stratégies officielles), NVIDIA, Visual Studio / VS Code, PowerShell (`POWERSHELL_TELEMETRY_OPTOUT`), outils .NET (`DOTNET_CLI_TELEMETRY_OPTOUT`).

---

## C. Charge système (M14 et conseiller M2)

| Réglage | Niveau | Remarque |
|---|---|---|
| Programmes au démarrage | 🟢 / 🟠 | conseiller |
| Applications en arrière-plan | 🟢 | |
| Enregistrement en arrière-plan des jeux (Game DVR) | 🟢 | si non utilisé |
| Edge *Startup boost* et arrière-plan | 🟢 | |
| Teams personnel, OneDrive au démarrage | 🟠 | si non utilisés |
| Indexation (WSearch) | 🔴 | réduire les dossiers plutôt que couper |
| SysMain | 🟠 | par défaut, sauf disque dur bloqué à 100 % |
| Effets visuels | 🟠 | PC peu puissants |
| Démarrage rapide | 🟠 | double démarrage ou bugs de mise à jour |
| Hibernation | 🟠 | PC fixe |
| Stockage réservé | 🔴 | peut gêner les mises à jour |
| Performances optimales | 🟠 | PC fixe |
| Assistant Stockage | 🟢 | à activer |

---

## D. Services (conseiller M2)

| Service | Niveau | Condition |
|---|---|---|
| Télécopie | 🟢 | |
| Windows Insider (`wisvc`) | 🟢 | hors programme Insider |
| Démonstration en magasin (`RetailDemo`) | 🟢 | |
| Cartes téléchargées (`MapsBroker`) | 🟢 | |
| Registre à distance | 🟢 | garder désactivé |
| Spouleur d'impression | 🟠 | aucune imprimante |
| Xbox (`XblAuthManager`, `XblGameSave`, `XboxNetApiSvc`, `XboxGipSvc`) | 🟠 | aucune application Xbox / Game Pass |
| Prise en charge Bluetooth | 🟠 | pas de Bluetooth |
| Biométrie (`WbioSrvc`) | 🟠 | Windows Hello non utilisé |
| Carte à puce (`SCardSvr`) | 🟠 | aucun lecteur |
| Géolocalisation (`lfsvc`) | 🟠 | localisation désactivée |
| Contrôle parental (`WpcMonSvc`) | 🟠 | aucun compte enfant |
| Bureau à distance | 🟠 | jamais utilisé |
| Lien avec Windows | 🟠 | aucun téléphone lié |
| Clavier tactile et écriture (`TabletInputService`) | 🔴 | casse la saisie dans certaines applications sous Windows 11 |
| Service de stratégie de diagnostic | 🔴 | |
| Windows Update, Defender, Pare-feu, RPC, DCOM, Audio, `WlanSvc`, Cryptographie, BITS | 🔒 | |

---

## E. Fonctionnalités facultatives (M14)

| Fonctionnalité | Niveau |
|---|---|
| SMB 1.0 | 🟢 (retrait conseillé, sécurité) |
| PowerShell 2.0 | 🟢 (retrait conseillé, sécurité) |
| Imprimante XPS, Télécopie et numérisation, Dossiers de travail, Windows Media Player hérité | 🟢 |
| Mode Internet Explorer | 🟠 |
| Hyper-V, WSL, Bac à sable | 🟠 (si non utilisés) |

---

## F. Bloatwares (M5)

**Microsoft, recommandés en novice** : Candy Crush et jeux sponsorisés, Clipchamp, Actualités, Météo, Solitaire, Teams personnel, nouvel Outlook, LinkedIn, Spotify préinstallé, Hub de commentaires, Obtenir de l'aide, Astuces, Cartes, Portail de réalité mixte, Visionneuse 3D, Cortana, application Copilot.

**Fabricants** : utilitaires HP, Dell, Lenovo, Asus, Acer (hors pilotes et outils BIOS/mises à jour du fabricant), versions d'essai de McAfee et Norton, offres préinstallées (Booking, Dropbox…).

**🔒 Jamais** : Microsoft Store, Programme d'installation d'application (winget), VCLibs, UI.Xaml, Edge WebView2, Xbox Identity Provider si l'utilisateur joue. **À garder en novice** : Calculatrice, Bloc-notes, Terminal, Outil Capture d'écran, Photos.

---

## G. Emplacements de persistance analysés (M7)

- `Run`, `RunOnce` (HKLM, HKCU, WOW6432Node), dossiers Démarrage, `StartupApproved`
- Tâches planifiées (dont tâches cachées et sans auteur), tâches BITS
- Services et pilotes (chemins, signatures, `ServiceDll`)
- Abonnements d'événements WMI (`__EventFilter`, `__EventConsumer`, `__FilterToConsumerBinding`)
- `Winlogon` : `Shell`, `Userinit`, `Notify`
- `Image File Execution Options` : `Debugger`, `GlobalFlag` / `SilentProcessExit`
- `AppInit_DLLs`, `AppCertDlls`
- Fournisseurs LSA (`Security Packages`, `Authentication Packages`, `Notification Packages`)
- Moniteurs et processeurs d'impression
- Détournements COM (`InprocServer32` dans HKCU qui masque HKLM)
- Extensions de l'Explorateur et du menu contextuel non signées
- **Exclusions de Defender** (chemins, processus, extensions)
- **Certificats racine** ajoutés au magasin
- Proxy (WinINet, WinHTTP), DNS, fichier `hosts`
- Stratégies et extensions forcées des navigateurs, raccourcis de navigateur modifiés
- Profils PowerShell, compléments Office, modèles Office au démarrage
- Portes dérobées d'accessibilité (`sethc.exe`, `utilman.exe`, `osk.exe`, `Magnify.exe` remplacés ou détournés via IFEO)
- Exécutables et scripts lancés depuis `%TEMP%`, `%APPDATA%`, `%PUBLIC%`, `%ProgramData%`
- Processus en cours non signés ou dont le fichier n'existe plus

---

## H. À ne jamais proposer en novice, et 🔒 même en expert

🔒 : supprimer ou modifier des fichiers système ; désactiver RPC, DCOM, Cryptographie ; désactiver Windows Update ou Defender de façon permanente ; contourner l'UAC ; retirer le Microsoft Store, winget ou les bibliothèques système.

🔴 (expert seulement, avec avertissement) : couper l'indexation, le fichier d'échange ou Prefetch ; désactiver l'intégrité de la mémoire ; désactiver le clavier tactile ; supprimer le stockage réservé ; désactiver le pare-feu sur un réseau privé.

Exclus de partout : nettoyeurs de RAM, réglages TCP « miracle », désactivation d'IPv6, « déblocage des cœurs », vidage manuel de WinSxS.
