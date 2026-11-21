# Tests manuels de la v0.1

À faire sur votre PC, puis dans une machine virtuelle Windows 10 et une machine virtuelle Windows 11.
Cochez chaque case. Notez tout ce qui ne va pas (capture d'écran + le fichier du journal).

## Lancement et fenêtre
- [ ] L'autorisation administrateur (UAC) apparaît au lancement, et rien d'autre.
- [ ] La fenêtre s'affiche en moins de 2 secondes (le journal indique « Window shown in … ms »).
- [ ] **Aucune autre fenêtre** ne s'ouvre pendant toute l'utilisation (pas de console, pas d'Explorateur, pas de navigateur).
- [ ] La fenêtre se rouvre à la même place et à la même taille.

## Langue et thème
- [ ] Paramètres → English : tous les textes passent en anglais **sans redémarrer**, y compris les résultats du Bilan.
- [ ] Retour au français : idem.
- [ ] Thème Clair, Sombre, Système : l'affichage change aussitôt ; les pastilles restent lisibles.
- [ ] Windows en **contraste élevé** (Alt gauche + Maj gauche + Impr. écran) : tout reste lisible.
- [ ] Paramètres Windows → Accessibilité → Taille du texte à 225 % : aucun texte coupé.

## Clavier et lecteur d'écran
- [ ] Toute l'application s'utilise **au clavier seul** (Tab, Maj+Tab, flèches, Entrée, Espace) ; le focus est visible.
- [ ] Le **Narrateur** (Ctrl + Windows + Entrée) lit chaque bouton, entrée du menu, pastille et ligne avec un nom compréhensible.

## Page Système
- [ ] Chaque carte est remplie, ou affiche « Non disponible : … » (jamais vide, jamais de plantage).
- [ ] Carte Windows 11 cohérente avec l'application **Contrôle d'intégrité du PC** de Microsoft.
- [ ] Onglet Démarrage : le temps affiché est cohérent avec l'Observateur d'événements
      (Journaux des applications et des services → Microsoft → Windows → Diagnostics-Performance → Opérationnel, événement 100).
- [ ] Programmes au démarrage : un programme non signé connu apparaît comme « Non signé » ;
      les éléments Microsoft (OneDrive, SecurityHealth) sont « Signé ».
- [ ] Services : `notepad`/services Windows marqués « Signé » ; la liste défile sans ralentir (plusieurs centaines de lignes).
- [ ] Recherche et filtre « Non signés uniquement » fonctionnent ; « Copier » met le détail dans le presse-papiers.

## Bilan
- [ ] « Analyser mon PC » : l'analyse se termine en moins de 30 secondes.
- [ ] « Annuler » pendant l'analyse revient à l'écran de départ.
- [ ] La note et les compteurs correspondent à la liste ; chaque ligne se déplie sur les détails techniques.
- [ ] « Copier le rapport » produit un texte lisible.

## Compte analysé
- [ ] Depuis un **compte standard**, en tapant le mot de passe d'un administrateur à l'UAC :
      Paramètres → Compte analysé affiche le **compte standard** (avec la mention « autorisé avec un autre compte »),
      et ses propres programmes au démarrage apparaissent.

## Journal et données
- [ ] Le journal existe dans `%LOCALAPPDATA%\PCDoc\logs` (chemin affiché dans Paramètres, bouton « Copier le chemin »).
- [ ] « Effacer les données de PCDoc » demande une confirmation dans la page, puis supprime paramètres et anciens journaux.

## Version ARM64
- [ ] Si un PC à processeur Snapdragon est disponible : mêmes vérifications avec la version `win-arm64`.

## v0.2 — corrections (à faire dans une machine virtuelle d'abord)
- [ ] Premier lancement : l'assistant s'affiche (langue, mode, protection du système) ; « Activer » active la protection.
- [ ] Bilan : « Corriger » et « Tout corriger (sûr uniquement) » ouvrent une confirmation dans la page, avec la liste exacte.
- [ ] Après confirmation : un point de restauration apparaît dans Windows (rstrui) ou PCDoc indique qu'un point récent est réutilisé.
- [ ] Le service ou programme est réellement désactivé (Gestionnaire des tâches → Démarrage, services.msc).
- [ ] Un fichier .reg est créé dans `C:\ProgramData\PCDoc\backup` ; ce dossier n'est pas lisible par un compte standard.
- [ ] Historique : « Annuler » remet l'élément dans son état d'origine.
- [ ] Mode novice : aucune action 🔴 proposée ; mode expert (avec confirmation) : 🔴 proposées, case « Je comprends le risque » obligatoire ; 🔒 jamais.
- [ ] Coupure pendant une correction (éteindre la VM au mauvais moment) : au redémarrage, l'Historique signale la modification interrompue.
