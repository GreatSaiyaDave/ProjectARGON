#!/bin/bash
# Ranks the DK episodes by hair / glove colour windows (tools/rank.py); 4 at a time. Writes only into /tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/138746c4-81e1-4dbe-b842-1cdaa6db0c59/scratchpad/dkb/meas/rank.
cd /tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/138746c4-81e1-4dbe-b842-1cdaa6db0c59/scratchpad/dkb/meas
B="blond=40:58,0.35:0.85,0.70:1"; T="teal=150:185,0.18:0.6,0.55:0.95"; C="crimson=330:358,0.5:1,0.45:0.95"; M="maroon=325:355,0.45:0.9,0.25:0.55"
BR="brown=12:35,0.4:0.8,0.3:0.7"; N="navy=230:285,0.2:0.65,0.12:0.45"; L="lav=250:290,0.15:0.6,0.5:0.95"; S="silver=200:290,0.03:0.18,0.78:0.98"; BL="blue=205:240,0.3:0.75,0.3:0.75"
run() { PYTHONDONTWRITEBYTECODE=1 /tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad/venv/bin/python tools/rank.py "$@" >> rank/log.txt 2>&1; }
run 5 0 1300 rank $T $C $M &
run 6 0 1300 rank $B $BR $M $C &
run 7 0 1300 rank $N $M $C &
run 8 0 1300 rank $N $M $BR &
wait
run 12 0 1300 rank $L $BR $M $B &
run 16 0 1300 rank $S $BR $M &
run 17 0 1300 rank $BL $M &
run 18 0 1300 rank $BL $M &
wait
run 22 0 1300 rank $BR $N $M &
run 25 0 1300 rank $BR $B $M &
run 26 0 1300 rank $S $BR &
run 31 0 1300 rank $B $M &
wait
run 32 0 1300 rank $B $M &
run 36 0 1300 rank $S &
run 15 0 1300 rank $M $N &
run 13 0 1300 rank $S $M &
wait
echo ALLDONE >> rank/log.txt
