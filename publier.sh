#!/usr/bin/env bash
# Publie une nouvelle version d'Ethersio sur le Raspberry Pi (ethersio.barthaux.fr), depuis macOS ou Linux.
# Prerequis : SDK .NET (~/.dotnet), openssl, acces SSH au Pi.
#
#   ./publier.sh --init                      une seule fois : genere la cle de signature
#   ./publier.sh                             publie la version de VERSION.txt
#   ./publier.sh --bump patch|minor|major    incremente VERSION.txt puis publie
#   ./publier.sh --version 2.2.0             force un numero de version
#   ./publier.sh --server utilisateur@hote   serveur de destination (sinon .publier.env)
#   ./publier.sh --no-upload                 compile et signe sans rien envoyer
#
# La cle privee reste dans ~/.ethersio/signing-key.pem : ne jamais la copier sur le Pi.
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

KEY="$HOME/.ethersio/signing-key.pem"
KEY_SOURCE=src/UpdateKey.cs
# destination : .publier.env (non versionne, voir publier.env.example) ou variables d'environnement
[ -f .publier.env ] && . ./.publier.env
SERVER="${ETHERSIO_SERVER:-}"
REMOTE_DIR="${ETHERSIO_REMOTE_DIR:-}"
SSH_OPTS=(-o BatchMode=yes)
[ -n "${ETHERSIO_SSH_KEY:-}" ] && SSH_OPTS+=(-i "$ETHERSIO_SSH_KEY")

init=0; bump=""; version=""; upload=1
while [ $# -gt 0 ]; do
  case "$1" in
    --init) init=1 ;;
    --bump) bump="${2:?patch|minor|major}"; shift ;;
    --version) version="${2:?X.Y.Z}"; shift ;;
    --server) SERVER="${2:?utilisateur@hote}"; shift ;;
    --no-upload) upload=0 ;;
    *) echo "Option inconnue : $1" >&2; exit 2 ;;
  esac
  shift
done

# cle publique au format XML .NET (RSAKeyValue), lue par RSACryptoServiceProvider.FromXmlString
public_xml() {
  local modulus
  modulus=$(openssl rsa -in "$KEY" -noout -modulus | sed 's/^Modulus=//' | xxd -r -p | base64 | tr -d '\n')
  printf '<RSAKeyValue><Modulus>%s</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>' "$modulus"
}

if [ "$init" = 1 ]; then
  [ -e "$KEY" ] && { echo "Une cle existe deja ($KEY). La remplacer invaliderait les mises a jour de toutes les installations." >&2; exit 1; }
  mkdir -p "$(dirname "$KEY")"
  openssl genrsa -out "$KEY" 3072 2>/dev/null
  chmod 600 "$KEY"
  cat > "$KEY_SOURCE" <<EOF
// Cle PUBLIQUE de signature des mises a jour. Generee par "publier.sh --init" (la cle privee reste sur le PC de l'auteur).
// Tant qu'elle est vide, toute mise a jour est refusee.
namespace Ethersio { static class UpdateKey { public const string PublicKeyXml = "$(public_xml)"; } }
EOF
  echo "Cle privee : $KEY"
  echo "SAUVEGARDE-LA (gestionnaire de mots de passe, cle USB) : sans elle, plus aucune mise a jour possible."
  echo "Cle publique ecrite dans $KEY_SOURCE. Publie ensuite avec ./publier.sh"
  exit 0
fi

[ -f "$KEY" ] || { echo "Cle de signature absente. Lance d'abord : ./publier.sh --init" >&2; exit 1; }
grep -qF "$(public_xml)" "$KEY_SOURCE" || { echo "$KEY_SOURCE ne contient pas la cle publique de ta cle privee." >&2; exit 1; }

# ---- numero de version
cur=$(tr -d '[:space:]' < VERSION.txt)
IFS=. read -r major minor patch <<< "$cur"
if [ -n "$version" ]; then
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version attendue : X.Y.Z" >&2; exit 2; }
  ver="$version"
else
  case "$bump" in
    major) ver="$((major + 1)).0.0" ;;
    minor) ver="$major.$((minor + 1)).0" ;;
    patch) ver="$major.$minor.$((patch + 1))" ;;
    "") ver="$cur" ;;
    *) echo "--bump : patch, minor ou major" >&2; exit 2 ;;
  esac
fi
printf '%s\n' "$ver" > VERSION.txt
echo "Version $ver"

# ---- compilation (.NET Framework 4.8, tourne sur Windows 10/11 sans rien installer) : l'app puis l'installeur qui l'embarque
rm -rf dist
dotnet build Ethersio.csproj -c Release -o dist/build -v quiet --nologo
exe=dist/build/Ethersio.exe

# ---- signature : "ethersio:<version>:" + exe (voir Updater.Verify)
site=dist/site
mkdir -p "$site"
{ printf 'ethersio:%s:' "$ver"; cat "$exe"; } > dist/payload.bin
openssl dgst -sha256 -sign "$KEY" -out dist/payload.sig dist/payload.bin
openssl rsa -in "$KEY" -pubout -out dist/public.pem 2>/dev/null
openssl dgst -sha256 -verify dist/public.pem -signature dist/payload.sig dist/payload.bin > /dev/null
sig=$(base64 < dist/payload.sig | tr -d '\n')

# ---- fichiers a publier
exe_name="Ethersio-$ver.exe"
cp "$exe" "$site/$exe_name"
dotnet build setup/Setup.csproj -c Release -o dist/setup -v quiet --nologo -p:Version="$ver" -p:EthersioExe="$PWD/$exe"
cp dist/setup/Ethersio-Setup.exe "$site/"

printf '{"version":"%s","file":"%s","sig":"%s"}' "$ver" "$exe_name" "$sig" > "$site/version.json"
echo "Signature verifiee. Fichiers prets dans $site"

# ---- envoi : version.json en dernier pour que les PC ne voient la version qu'une fois l'exe en ligne
if [ "$upload" = 1 ]; then
  [ -n "$SERVER" ] && [ -n "$REMOTE_DIR" ] || { echo "Destination inconnue : renseigne .publier.env (voir publier.env.example) ou utilise --no-upload." >&2; exit 1; }
  scp "${SSH_OPTS[@]}" "$site/$exe_name" "$site/Ethersio-Setup.exe" "$SERVER:$REMOTE_DIR/"
  scp "${SSH_OPTS[@]}" "$site/version.json" "$SERVER:$REMOTE_DIR/"
  ssh "${SSH_OPTS[@]}" "$SERVER" "chmod 644 $REMOTE_DIR/*"
  echo "Ethersio $ver publie. Les PC se mettent a jour d'ici 4 h au plus."
fi
