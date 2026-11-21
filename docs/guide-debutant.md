# Guide débutant : GitHub et versions de test

Ce guide explique, pas à pas, comment mettre PCDoc sur GitHub et récupérer une version à tester.
Vous n'avez rien à installer pour compiler : GitHub le fait pour vous.

## 1. Créer le compte
1. Ouvrez **github.com/signup** et suivez les étapes (adresse e-mail, mot de passe, nom d'utilisateur).
2. Activez la **double authentification** : photo de profil (en haut à droite) → **Settings** →
   **Password and authentication** → **Enable two-factor authentication**. Utilisez une application
   d'authentification sur votre téléphone. Notez les codes de secours dans un endroit sûr.

## 2. Créer le dépôt
1. En haut à droite : **+** → **New repository**.
2. Nom : `pcdoc` (provisoire). Visibilité : **Public**.
3. **Ne cochez rien** (pas de README, pas de licence, pas de .gitignore) : ces fichiers viennent du projet.
4. **Create repository**.

## 3. Sécuriser le dépôt
Dans le dépôt : **Settings**.
1. **Branches** → **Add branch ruleset** (ou *Add rule*) pour `main` : exiger une *pull request* avant fusion,
   exiger que les vérifications (CI) réussissent, interdire le *force push*.
2. **Code security** (ou *Security*) : activer **Private vulnerability reporting**, **Dependabot alerts** et
   **CodeQL analysis → Default setup**.
3. **Actions → General** : laisser les actions autorisées.

## 4. Donner accès à Claude
Sur claude.ai : **Paramètres** → **Connecteurs** → **GitHub** → autorisez le dépôt `pcdoc`.
Donnez ensuite à Claude le nom complet du dépôt (`votre-nom/pcdoc`) : il y enverra le code.

## 5. Récupérer une version de test
1. Dans le dépôt : onglet **Actions** → cliquez sur le dernier passage marqué d'une coche verte.
2. En bas, section **Artifacts** : téléchargez **PCDoc-portable-win-x64** (ou **win-arm64** pour un PC à
   processeur Snapdragon).
3. Décompressez le fichier zip dans un dossier, par exemple `Documents\PCDoc`.
4. Double-cliquez sur **PCDoc.exe**. Windows demande l'autorisation administrateur : acceptez.
5. Si Windows SmartScreen affiche « Windows a protégé votre ordinateur » : **Informations complémentaires** →
   **Exécuter quand même**. C'est normal tant que l'application n'est pas signée (prévu en v1.0).

## 6. Les versions publiées
Quand une version est prête, elle apparaît dans l'onglet **Releases** du dépôt, avec le fichier zip,
le fichier `SHA256SUMS.txt` (empreintes pour vérifier le téléchargement) et la liste des composants (SBOM).
