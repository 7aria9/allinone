#!/usr/bin/env bash
# Lance AllInOne --smoke-test dans chaque dossier (Linux : sous Xvfb ; macOS : directement).
# L'application doit afficher sa fenêtre et tous ses écrans, puis se fermer d'elle-même avec le code 0.
# Usage : smoke-test.sh <dossier contenant AllInOne> [dossier des captures d'écran]
folder="$1"
shots="${2:-}"
exe="$folder/AllInOne"
args=(--smoke-test)
[ -n "$shots" ] && mkdir -p "$shots" && args+=(--screenshots "$shots")

echo "== Démarrage de $exe"
if [ "$(uname)" = "Linux" ]; then
  xvfb-run -a -s "-screen 0 1400x900x24" "$exe" "${args[@]}" &
else
  "$exe" "${args[@]}" &
fi
pid=$!
# Délai maximal de 120 s (la commande « timeout » n'existe pas sur macOS).
( for _ in $(seq 120); do sleep 1; kill -0 $pid 2>/dev/null || exit 0; done
  echo "::error::$exe ne s'est pas fermé en 120 s"; kill $pid ) &
wait $pid
code=$?
if [ $code -ne 0 ]; then
  echo "::error::$exe s'est arrêté avec le code $code"
  for dir in "$HOME/.local/share/AllInOne/logs" "$HOME/Library/Application Support/AllInOne/logs"; do
    [ -d "$dir" ] && cat "$dir"/*
  done
  exit 1
fi

cli="$folder/allinone-cli"
if ! "$cli" --help | head -1; then
  echo "::error::$cli --help a échoué"
  exit 1
fi
