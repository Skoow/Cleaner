# PCDoc

> Nom de code provisoire. / Provisional code name.

**FR** — Outil Windows gratuit et open source (GPL-3.0) pour comprendre, réparer, nettoyer, mettre à jour,
optimiser, sécuriser et rendre confidentiel votre PC. Une seule fenêtre, aucune publicité, aucune télémétrie.

**EN** — Free, open-source (GPL-3.0) Windows tool to understand, repair, clean, update, optimize,
secure and de-track your PC. One window, no ads, no telemetry.

## État / Status
Version 1.0 : Bilan, Nettoyer (fichiers, traces, applications, effacement sécurisé), Protéger (antivirus et analyses
d'autres éditeurs, confidentialité), Mettre à jour et accélérer (Windows, pilotes, logiciels, Store, réglages),
Dépanner (diagnostic, réparations, réseau, licences), Historique, Nouveau PC (expert), installateur MSI et mises à jour.

Version 1.0: health check, cleanup, protection (antivirus and second-opinion scans, privacy), updates and tuning,
troubleshooting, history, new-PC setup (expert), MSI installer and verified updates.

Guide pas à pas (GitHub, téléchargement) : [docs/guide-debutant.md](docs/guide-debutant.md).
Tests manuels : [docs/tests-manuels-v0.1.md](docs/tests-manuels-v0.1.md).

## Télécharger / Download
Page **Releases** du dépôt / repository *Releases* page :
- `PCDoc-x.y.z-x64.msi` (ou / or `arm64`) : installateur pour tous les comptes / per-machine installer ;
- `PCDoc-x.y.z-portable-win-x64.zip` : version portable (clé USB) / portable build ;
- `SHA256SUMS.txt` : sommes de contrôle / checksums.

Page de téléchargement / download page : `site/index.html` (GitHub Pages).
Tant que la version n'est pas signée, Windows SmartScreen affiche « Informations complémentaires » → « Exécuter quand
même » / until builds are signed, SmartScreen asks to confirm.

## Publier une version / Make a release
```bash
git tag v1.0.0
```

```bash
git push origin v1.0.0
```

La signature (Azure Trusted Signing) est faite si les secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_CLIENT_SECRET` et les variables `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE` sont configurés.

## Compiler / Build
```bash
dotnet build PCDoc.slnx
```

```bash
dotnet test PCDoc.slnx
```

Spécification : `docs/specs/2026-11-21-pcdoc-specification-v2.md`.
Voir aussi / See also: [SECURITY.md](SECURITY.md), [PRIVACY.md](PRIVACY.md), [LICENSE](LICENSE).
