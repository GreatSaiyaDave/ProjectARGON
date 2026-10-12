#!/bin/bash
SP=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
for n in $(seq 1 40); do
  nice -n 10 $SP/venv/bin/python $SP/anime/gesturescan.py "Season 1 Episode $n.mp4" $SP/anime/gest/s1e$(printf %02d $n) 12 2>&1 | tail -1
done
echo BATCH_DONE
