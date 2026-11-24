#!/usr/bin/env bash
# Builds the website's sound effects from Kenney's "Impact Sounds" pack (CC0, https://kenney.nl/assets/impact-sounds).
#
# Usage: download the pack, then
#   tools/make-sounds.sh path/to/folder-containing/impactWood_*.ogg
# Needs ffmpeg. Output goes to src/RlChess.Web/wwwroot/sounds/ (the source pack isn't kept in the repo).
set -euo pipefail
SRC="${1:?Give the folder containing impactWood_light_*.ogg and impactWood_heavy_*.ogg}"
OUT="$(dirname "$0")/../src/RlChess.Web/wwwroot/sounds"
mkdir -p "$OUT"

# Common finishing: trim leading silence, mono 44.1 kHz, gentle fade-out, peak-normalise.
finish="silenceremove=start_periods=1:start_threshold=-50dB,aresample=44100,afade=t=out:st=0.18:d=0.08,alimiter=limit=0.7:level=disabled"
render() { ffmpeg -loglevel error -y "${@:2}" -ac 1 -c:a pcm_s16le "$OUT/$1.wav"; }

L() { echo "$SRC/impactWood_light_00$1.ogg"; }
H() { echo "$SRC/impactWood_heavy_00$1.ogg"; }

# Move: a single light wooden clack.
render move -i "$(L 2)" -af "$finish,atrim=0:0.26"

# Capture: a heavier thud with a clack on top (a few ms apart, like a piece knocked aside).
render capture -i "$(H 3)" -i "$(L 0)" -filter_complex \
  "[1]adelay=12,volume=0.8[b];[0][b]amix=inputs=2:normalize=0,$finish,atrim=0:0.3"

# Check: two quick light knocks, "tok-tok", distinct but soft.
render check -i "$(L 0)" -i "$(L 4)" -filter_complex \
  "[0]volume=0.9[a];[1]adelay=95,volume=0.75[b];[a][b]amix=inputs=2:normalize=0,silenceremove=start_periods=1:start_threshold=-50dB,aresample=44100,afade=t=out:st=0.3:d=0.08,alimiter=limit=0.7:level=disabled,atrim=0:0.38"

# Game over: pieces settling, a thud then two lighter knocks.
render gameover -i "$(H 2)" -i "$(L 3)" -i "$(L 1)" -filter_complex \
  "[1]adelay=150,volume=0.6[b];[2]adelay=290,volume=0.4[c];[0][b][c]amix=inputs=3:normalize=0,silenceremove=start_periods=1:start_threshold=-50dB,aresample=44100,afade=t=out:st=0.5:d=0.12,alimiter=limit=0.7:level=disabled,atrim=0:0.62"

echo "Sounds written to $OUT"
