# Confidentialité / Privacy

## Français
- **Aucune télémétrie.** PCDoc n'envoie aucune statistique, aucun rapport de plantage, aucune donnée sur vous ou votre PC.
- **Aucun compte, aucun serveur.** Tout se passe sur votre PC.
- **Connexions** : seulement pour les actions que vous demandez (Windows Update, winget, Microsoft Store,
  téléchargements officiels, VirusTotal si vous l'activez) et pour lire le numéro de la dernière version de PCDoc sur
  GitHub au démarrage ou quand vous cliquez sur « Vérifier » (désactivable dans Paramètres → Mises à jour de PCDoc ;
  rien sur vous ou votre PC n'est envoyé, GitHub voit seulement votre adresse IP comme pour tout site), puis pour
  télécharger la mise à jour si vous l'installez.
- **Données enregistrées sur votre PC**, dans `%LOCALAPPDATA%\PCDoc` :
  - `settings.json` : langue et thème ;
  - `logs\` : journal des actions, un fichier par jour, conservé 14 jours. Il peut contenir des chemins de fichiers
    avec votre nom d'utilisateur. Il ne quitte jamais votre PC sauf si vous le copiez vous-même.
- Vous pouvez tout effacer depuis **Paramètres → Effacer les données de PCDoc**.

## English
- **No telemetry.** PCDoc sends no statistics, no crash reports, no data about you or your PC.
- **No account, no server.** Everything happens on your PC.
- **Connections**: only for actions you request (Windows Update, winget, Microsoft Store, official downloads,
  VirusTotal if you enable it) and to read the latest PCDoc version number on GitHub at startup (can be turned off
  in Settings → PCDoc updates; nothing about you or your PC is sent, GitHub only sees your IP address like any
  website), then to download the update if you install it.
- **Data stored on your PC**, in `%LOCALAPPDATA%\PCDoc`:
  - `settings.json`: language and theme;
  - `logs\`: action log, one file per day, kept 14 days. It may contain file paths including your user name.
    It never leaves your PC unless you copy it yourself.
- You can erase everything from **Settings → Clear PCDoc data**.
