# PCDoc

> Nom de code provisoire. / Provisional code name.

**FR** — Outil Windows gratuit et open source (GPL-3.0) pour comprendre, réparer, nettoyer, mettre à jour,
optimiser, sécuriser et rendre confidentiel votre PC. Une seule fenêtre, aucune publicité, aucune télémétrie.

**EN** — Free, open-source (GPL-3.0) Windows tool to understand, repair, clean, update, optimize,
secure and de-track your PC. One window, no ads, no telemetry.

## État / Status
Version 0.1 en cours de développement : informations système en lecture seule et Bilan.
Version 0.1 in development: read-only system information and health check.

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
