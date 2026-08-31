#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
version=$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$repo_root/global.json" | head -n 1)
os_name=$(uname -s)
machine=$(uname -m)

case "$os_name:$machine" in
  Darwin:arm64|Darwin:aarch64) platform="osx-arm64" ;;
  Darwin:x86_64) platform="osx-x64" ;;
  Linux:arm64|Linux:aarch64) platform="linux-arm64" ;;
  Linux:x86_64) platform="linux-x64" ;;
  *)
    printf '%s\n' "Unsupported platform: $os_name $machine" >&2
    exit 1
    ;;
esac

download_dir=$(mktemp -d "$repo_root/.dotnet-download.XXXXXX")
trap 'rm -rf "$download_dir"' EXIT HUP INT TERM
archive="$download_dir/dotnet-sdk.tar.gz"
url="https://dotnetcli.azureedge.net/dotnet/Sdk/$version/dotnet-sdk-$version-$platform.tar.gz"

printf 'Installing .NET SDK %s for %s into %s\n' "$version" "$platform" "$repo_root/.dotnet"
curl --fail --location --retry 3 --connect-timeout 20 "$url" --output "$archive"
mkdir -p "$repo_root/.dotnet"
tar -xzf "$archive" -C "$repo_root/.dotnet"
printf 'Installed: '
"$repo_root/Scripts/dotnet" --version
