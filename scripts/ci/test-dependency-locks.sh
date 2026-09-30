#!/usr/bin/env bash
set -euo pipefail
# ARCH-01-AC-001 / ARCH-01-TC-01: an unreviewed manifest change must fail
# before resolving a replacement dependency, even when sources are offline.
scratch="$(mktemp -d /tmp/strataai-lock-check.XXXXXXXX)"
case "$scratch" in /tmp/strataai-lock-check.*) ;; *) exit 1 ;; esac
trap 'rm -rf -- "$scratch"' EXIT
cp global.json Directory.Build.props Directory.Build.targets Directory.Packages.props StrataAI2.slnx "$scratch/"
while IFS= read -r path; do
  mkdir -p "$scratch/$(dirname "$path")"
  cp "$path" "$scratch/$path"
done < <(find src tests -type f \( -name '*.csproj' -o -name 'packages.lock.json' \))
mkdir "$scratch/empty-feed"
sed -i 's/Include="Npgsql" Version="[^"]*"/Include="Npgsql" Version="0.0.0-lock-test"/' "$scratch/Directory.Packages.props"
if dotnet restore "$scratch/StrataAI2.slnx" --locked-mode --source "$scratch/empty-feed" >"$scratch/restore.log" 2>&1; then
  echo 'Locked restore unexpectedly accepted a changed dependency manifest.' >&2
  exit 1
fi
if ! grep -q 'NU1004' "$scratch/restore.log"; then
  echo 'Restore did not reject dependency drift with the expected lock mismatch.' >&2
  cat "$scratch/restore.log" >&2
  exit 1
fi
echo 'Locked restore rejects dependency drift without resolving new packages.'
