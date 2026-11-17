# Ethersio

Ethersio is a Windows tray application that shows and manages the network configuration of a PC (DHCP or static IP, diagnostics, network tools, IP profiles). It is built for BTS SIO SISR students (French IT vocational degree). The interface and the documentation are in French.

Application Windows de zone de notification pour étudiants BTS SIO SISR : vérifier et gérer sa configuration réseau.

## Fonctionnalités

- **Icône d'état** : prise RJ45 sur tuile colorée (vert = OK, orange = IP fixe, rouge = problème, gris = déconnecté).
- **Infos** : IP, passerelle, DNS, bail DHCP, IPv6, Wi-Fi, débit (60 s), IP publique, toutes les cartes, proxy.
- **Diagnostic** : chaîne Carte, Passerelle, DNS, Internet ; test de débit ; journal des événements.
- **Outils** : ping, tracert, nslookup ; test de port TCP sur un hôte et un port donnés ; calculateur IP et sous-réseaux ; table ARP.
- **Actions** : ouvrir `ncpa.cpl`, repasser en DHCP, renouveler l'IP, vider le cache DNS, copier les infos, appliquer des profils IP (Maison, Lycée, TP...).

Toute modification de la configuration réseau est demandée explicitement par l'utilisateur et passe par l'invite de droits administrateur de Windows.

## Installation

Télécharger `Ethersio-Setup.exe` sur <https://ethersio.barthaux.fr> et le lancer. Il installe Ethersio dans `C:\Program Files\Ethersio` pour tous les comptes du PC, le lance au démarrage de Windows et ajoute une entrée dans Paramètres > Applications (désinstallation). Windows 10 et 11, aucune dépendance.

Tant que l'exécutable n'est pas signé, Windows peut afficher un avertissement SmartScreen : « Informations complémentaires » puis « Exécuter quand même ».

## Mises à jour automatiques

1. Le mainteneur publie une version : `publier.sh` compile, signe `"ethersio:<version>:" + exe` (RSA 3072, SHA-256) avec une clé privée qui ne quitte jamais sa machine, puis envoie `Ethersio-X.Y.Z.exe` et `version.json` sur le serveur.
2. Une tâche planifiée SYSTEM (`Ethersio\Mise a jour`, toutes les 4 h et au démarrage) lance `Ethersio.exe --update` : lecture de `https://ethersio.barthaux.fr/version.json`, téléchargement, **vérification de la signature avec la clé publique embarquée** (`src/UpdateKey.cs`), remplacement de l'exe.
3. Les instances ouvertes détectent le nouvel exe et redémarrent seules.

Un serveur compromis ne peut pas pousser de mise à jour : sans la clé privée, aucune archive n'est acceptée. Journal : `C:\ProgramData\Ethersio\update.log`.

## Compiler

Le SDK .NET suffit (Windows, macOS ou Linux) ; la cible est .NET Framework 4.8.

```bash
dotnet build Ethersio.csproj -c Release -o dist/build
dotnet build setup/Setup.csproj -c Release -o dist/setup -p:Version=$(cat VERSION.txt) -p:EthersioExe=$PWD/dist/build/Ethersio.exe
```

`.github/workflows/build.yml` fait la même chose à chaque push. `publier.sh` (version, build, signature, envoi) est décrit en tête du script ; la destination se règle dans `.publier.env` (voir `publier.env.example`). Le dossier `serveur/` contient la configuration nginx (Docker) et la page de téléchargement.

| Chemin | Rôle |
|---|---|
| `src/` | Application (`Ethersio.cs`, `TrayApp.cs`, `Flyout.cs`, `UiKit.cs`, `Net.cs`, `Profiles.cs`, `Updater.cs`, `UpdateKey.cs`) |
| `setup/` | Installeur `Ethersio-Setup.exe` (embarque l'application, demande les droits administrateur) |
| `serveur/` | nginx (Docker) et page de téléchargement |
| `VERSION.txt` | Numéro de version, source unique |

## Politique de signature de code

Les exécutables publiés seront signés avec un certificat fourni gratuitement aux projets open source par la SignPath Foundation (demande en cours). Cette politique s'applique dès l'obtention du certificat.

- **Binaires signés** : uniquement `Ethersio.exe` et `Ethersio-Setup.exe`, compilés par le workflow GitHub Actions de ce dépôt à partir de ce code source, sans composant propriétaire.
- **Rôles** : projet à un seul mainteneur, [Yann Barthaux-Vallee](https://github.com/Greenou), qui est auteur, relecteur et approbateur. Chaque demande de signature est approuvée manuellement. L'authentification à deux facteurs est obligatoire pour GitHub et SignPath.
- **Mention** (ajoutée dès l'approbation) : « Free code signing provided by SignPath.io, certificate by SignPath Foundation ».

## Confidentialité

Ethersio ne collecte aucune donnée personnelle et ne les transmet à aucun serveur de l'auteur. Profils IP et journal restent sur le PC (`%APPDATA%\Ethersio`). L'application contacte uniquement :

| Service | Pourquoi | Quand |
|---|---|---|
| `ethersio.barthaux.fr` | mises à jour | toutes les 30 min et via la tâche planifiée |
| `1.1.1.1` (ping), `www.google.com` (résolution DNS), `www.msftconnecttest.com` | test de connexion Internet | toutes les 8 s tant que l'application tourne |
| `api.ipify.org` | affichage de l'IP publique | au clic sur la ligne « IP publique » |
| `speed.cloudflare.com` | test de débit | uniquement à la demande |

Ces services reçoivent l'adresse IP publique du PC, comme tout site visité.

## Développement assisté par IA

Ethersio est développé avec l'assistance de Claude (Anthropic), sous la responsabilité de l'auteur, qui le maintient.

## Licence

[MIT](LICENSE.txt)
