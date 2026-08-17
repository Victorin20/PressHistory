# PressHistory

PressHistory est un petit gestionnaire d’historique du presse-papiers pour Windows 10 et 11. Il enregistre localement les textes copiés et permet de les retrouver, rechercher et recopier rapidement.

## Fonctionnalités

- capture automatique du texte Unicode depuis toutes les applications Windows ;
- déduplication : un texte recopié remonte en tête au lieu d’être ajouté deux fois ;
- recherche instantanée, insensible à la casse et aux accents ;
- recopie par bouton, double-clic ou touche `Entrée` ;
- suppression individuelle avec `Suppr`, et effacement complet avec confirmation ;
- pause immédiate de la capture ;
- démarrage optionnel avec Windows, sans droits administrateur ;
- icône dans la zone de notification : fermer la fenêtre ne coupe pas la capture ;
- raccourci global `Ctrl + Alt + H` pour afficher la fenêtre ;
- instance unique : relancer l’application affiche la fenêtre déjà ouverte ;
- sauvegarde locale atomique avec fichier de secours.

## Utilisation rapide

Prérequis pour la version légère : [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0) sur Windows 10/11.

Depuis la racine du projet :

```powershell
dotnet run --project .\src\PressHistory\PressHistory.csproj
```

Copiez ensuite n’importe quel texte. Il apparaît automatiquement dans la liste. La croix masque l’interface dans la zone de notification ; utilisez le menu de l’icône pour quitter réellement l’application.

## Créer l’exécutable

La commande suivante produit une version légère dans `artifacts\PressHistory-win-x64` :

```powershell
.\scripts\Publish.ps1
```

Pour une version autonome qui n’exige pas l’installation de .NET sur le PC cible :

```powershell
.\scripts\Publish.ps1 -SelfContained
```

La version autonome est nettement plus volumineuse, car elle embarque le runtime .NET Desktop.

## Confidentialité et stockage

Les données ne quittent jamais l’ordinateur et sont enregistrées dans le dossier :

```text
%LOCALAPPDATA%\PressHistory\
```

`history.json` contient l’état courant, `history.bak.json` sa copie de récupération et `settings.json` les préférences non sensibles. Une copie `history.corrupt-*.json` peut être créée temporairement si un fichier illisible doit être récupéré ; elle est supprimée après la prochaine sauvegarde réussie. Le bouton **Effacer** purge toutes les copies de l’historique, y compris les fichiers de récupération.

PressHistory respecte les marqueurs Windows `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard` et l’ancien marqueur `Clipboard Viewer Ignore`. Les applications qui les utilisent, notamment certains gestionnaires de mots de passe, peuvent ainsi empêcher une capture.

Tous les logiciels ne marquent pas leurs contenus sensibles. Utilisez le bouton **Capture active** pour suspendre l’enregistrement avant de manipuler un secret, puis **Effacer** si nécessaire. Les données locales ne sont pas chiffrées.

Cette version conserve uniquement le texte, avec une limite de 250 éléments, 2 Mo par élément et 32 Mo pour l’ensemble de l’historique. Elle n’enregistre pas les images ni les fichiers copiés.

## Développement et tests

```powershell
dotnet restore .\PressHistory.sln -p:NuGetAudit=false
dotnet build .\PressHistory.sln -c Release --no-restore
dotnet run --project .\tests\PressHistory.Tests\PressHistory.Tests.csproj -c Release --no-restore
```

Le projet ne dépend d’aucun paquet NuGet externe. Les tests couvrent la déduplication, la limite de rétention, Unicode, la sauvegarde/récupération, les paramètres corrompus, les marqueurs privés et la commande de démarrage Windows.
