#!/usr/bin/env bash
# Builds the world and a race replay, then renders the showcase video (intro flyover, start,
# LASTMA chase, finish and results) exactly as the reference renders were made. Run from anywhere.
#   scripts/render-race-video.sh                 # day, seed 16, out/renders/naija_kart.mp4
#   scripts/render-race-video.sh --theme night   # night version
#   SEED=7 FPS=30 scripts/render-race-video.sh   # other seed / frame rate
set -euo pipefail
cd "$(dirname "$0")/.."
THEME=day; while [ $# -gt 0 ]; do case "$1" in --theme) THEME="$2"; shift 2;; *) echo "unknown option $1"; exit 1;; esac; done
SEED="${SEED:-16}"; FPS="${FPS:-24}"; OUT_DIR="${OUT_DIR:-out/renders}"; mkdir -p "$OUT_DIR"
PV=tools/world-preview; WORLD=world.json; [ "$THEME" = night ] && WORLD=world_night.json

echo "1/4 world + replay"
dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --theme "$THEME" --out "$PV/public/$WORLD"
dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --seed "$SEED" --replay "$PV/public/replay.json"

echo "2/4 previewer"
( cd $PV && [ -d node_modules ] || npm install --no-audit --no-fund; node setup.js )

echo "3/4 frames: part A (flyover, start, chase) and part B (finish, results)"
A="$OUT_DIR/part_a.mp4"; B="$OUT_DIR/part_b.mp4"
( cd $PV && node render.js --fps="$FPS" --start=-3 --duration=55 --overview=5 --world="$WORLD" --out="../../$A" )
( cd $PV && node render.js --fps="$FPS" --start=184 --duration=14 --world="$WORLD" --out="../../$B" )

echo "4/4 encode"
FINAL="$OUT_DIR/naija_kart_${THEME}.mp4"
printf "file '%s'\nfile '%s'\n" "$(realpath "$A")" "$(realpath "$B")" > "$OUT_DIR/concat.txt"
ffmpeg -y -loglevel error -f concat -safe 0 -i "$OUT_DIR/concat.txt" -c:v libx264 -pix_fmt yuv420p -crf 22 -movflags +faststart "$FINAL"
rm -f "$OUT_DIR/concat.txt"
echo "wrote $FINAL ($(du -h "$FINAL" | cut -f1))"
