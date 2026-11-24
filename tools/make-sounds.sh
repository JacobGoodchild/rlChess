#!/usr/bin/env bash
# Generates the website's sound effects from simple synthesised tones (needs ffmpeg).
# These sounds are made from scratch here, so they're covered by the repo's MIT licence.
set -euo pipefail
O="$(dirname "$0")/../src/RlChess.Web/wwwroot/sounds"
mkdir -p "$O"
gen() { ffmpeg -loglevel error -y -f lavfi -i "aevalsrc='$2':s=44100:d=$3" -af "$4" -ac 1 -c:a pcm_s16le "$O/$1.wav"; }

# A wooden "tock": low body + mid knock + short noise click.
gen move "0.55*sin(2*PI*190*t)*exp(-t*38)+0.35*sin(2*PI*610*t)*exp(-t*70)+0.25*sin(2*PI*1240*t)*exp(-t*140)+0.35*(random(0)*2-1)*exp(-t*260)" 0.16 \
  "lowpass=f=3200,afade=t=out:st=0.12:d=0.04,volume=0.9"
# Two quick knocks.
gen capture "0.6*sin(2*PI*160*t)*exp(-t*30)+0.45*sin(2*PI*540*t)*exp(-t*55)+0.4*(random(0)*2-1)*exp(-t*200)+ between(t,0.045,1)*(0.45*sin(2*PI*230*(t-0.045))*exp(-(t-0.045)*35)+0.4*(random(1)*2-1)*exp(-(t-0.045)*220)+0.3*sin(2*PI*720*(t-0.045))*exp(-(t-0.045)*60))" 0.24 \
  "lowpass=f=3600,afade=t=out:st=0.19:d=0.05,volume=0.85"
# A knock with a soft bell on top.
gen check "0.5*sin(2*PI*190*t)*exp(-t*38)+0.3*(random(0)*2-1)*exp(-t*260)+0.22*sin(2*PI*1318*t)*exp(-t*9)+0.12*sin(2*PI*1975*t)*exp(-t*12)" 0.45 \
  "lowpass=f=4200,afade=t=out:st=0.35:d=0.1,volume=0.85"
# Three rising soft notes.
gen gameover "0.22*sin(2*PI*523*t)*exp(-t*4)+0.18*between(t,0.12,9)*sin(2*PI*659*(t-0.12))*exp(-(t-0.12)*4)+0.16*between(t,0.24,9)*sin(2*PI*784*(t-0.24))*exp(-(t-0.24)*3)" 0.9 \
  "afade=t=out:st=0.7:d=0.2"
echo "Sounds written to $O"
