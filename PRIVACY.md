# Confidentialité / Privacy

## Français
- **Aucune télémétrie.** PCDoc n'envoie aucune statistique, aucun rapport de plantage, aucune donnée sur vous ou votre PC.
- **Aucun compte, aucun serveur.** Tout se passe sur votre PC.
- **Version 0.1 : aucun appel réseau.** Les versions suivantes ne se connecteront que pour les actions que vous demandez
  (Windows Update, winget, Microsoft Store, téléchargements officiels, VirusTotal si vous l'activez)
  et pour vérifier les mises à jour de PCDoc (désactivable). Chaque connexion sera listée dans la page « Activité réseau ».
- **Données enregistrées sur votre PC**, dans `%LOCALAPPDATA%\PCDoc` :
  - `settings.json` : langue et thème ;
  - `logs\` : journal des actions, un fichier par jour, conservé 14 jours. Il peut contenir des chemins de fichiers
    avec votre nom d'utilisateur. Il ne quitte jamais votre PC sauf si vous le copiez vous-même.
- Vous pouvez tout effacer depuis **Paramètres → Effacer les données de PCDoc**.

## English
- **No telemetry.** PCDoc sends no statistics, no crash reports, no data about you or your PC.
- **No account, no server.** Everything happens on your PC.
- **Version 0.1 makes no network connection at all.** Later versions only connect for actions you request
  (Windows Update, winget, Microsoft Store, official downloads, VirusTotal if you enable it)
  and to check for PCDoc updates (can be turned off). Every connection will be listed on the "Network activity" page.
- **Data stored on your PC**, in `%LOCALAPPDATA%\PCDoc`:
  - `settings.json`: language and theme;
  - `logs\`: action log, one file per day, kept 14 days. It may contain file paths including your user name.
    It never leaves your PC unless you copy it yourself.
- You can erase everything from **Settings → Clear PCDoc data**.
