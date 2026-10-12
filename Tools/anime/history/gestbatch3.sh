#!/bin/bash
SP=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
for n in 9 19 32 4 6 7 17 13 10 20 33 35 36 37; do
  d=$SP/anime/gest3/s1e$(printf %02d $n); [ -f $d/windows.json ] && continue
  nice -n 10 $SP/venv/bin/python $SP/anime/gesturescan3.py "Season 1 Episode $n.mp4" $d 14 2>&1 | tail -1
  $SP/venv/bin/python $SP/anime/review.py $d > /dev/null 2>&1
done
echo BATCH3_DONE
