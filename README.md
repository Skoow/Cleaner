# PCDoc

> Nom de code provisoire. / Provisional code name.

**FR** — Outil Windows gratuit et open source (GPL-3.0) pour comprendre, réparer, nettoyer, mettre à jour,
optimiser, sécuriser et rendre confidentiel votre PC. Une seule fenêtre, aucune publicité, aucune télémétrie.

**EN** — Free, open-source (GPL-3.0) Windows tool to understand, repair, clean, update, optimize,
secure and de-track your PC. One window, no ads, no telemetry.

## État / Status
Version 0.1 (prérelease) : informations système en lecture seule et Bilan. Aucune modification du PC.
Version 0.1 (prerelease): read-only system information and health check. Nothing on the PC is changed.

## Fonctions de la v0.1 / v0.1 features
- **Bilan** : « Analyser mon PC », note sur 100, problèmes classés Critique / Attention / Info, détails techniques.
- **Système** (lecture seule) : Windows, matériel, disques, compatibilité Windows 11, temps de démarrage,
  programmes au démarrage, tâches planifiées, services et pilotes avec vérification des signatures numériques.
- **Paramètres** : français / anglais, thème clair / sombre / système, journal, effacement des données.

Guide pas à pas (GitHub, téléchargement) : [docs/guide-debutant.md](docs/guide-debutant.md).
Tests manuels : [docs/tests-manuels-v0.1.md](docs/tests-manuels-v0.1.md).

## Télécharger une version de test / Download a test build
*Actions* → dernier run vert / latest green run → *Artifacts* → `PCDoc-portable-win-x64` (ou / or `win-arm64`).
Windows SmartScreen : « Informations complémentaires » → « Exécuter quand même » (l'app n'est pas encore signée /
the app is not signed yet).

## Compiler / Build
```bash
dotnet build PCDoc.slnx
```

```bash
dotnet test PCDoc.slnx
```

Spécification : `docs/specs/2026-11-21-pcdoc-specification-v2.md`.
Voir aussi / See also: [SECURITY.md](SECURITY.md), [PRIVACY.md](PRIVACY.md), [LICENSE](LICENSE).
