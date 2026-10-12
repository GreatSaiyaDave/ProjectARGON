#!/bin/bash
SP=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
for n in 1 24 25 4 5 6 27 28 7 8 9 10 11 17 18 19 20 21 32 33 35 36 37 38 39 2 3 12 13 14 15 16 22 23 26 29 30 31 34; do
  nice -n 10 $SP/venv/bin/python $SP/anime/gesturescan2.py "Season 1 Episode $n.mp4" $SP/anime/gest2/s1e$(printf %02d $n) 14 2>&1 | tail -1
done
echo BATCH_DONE
