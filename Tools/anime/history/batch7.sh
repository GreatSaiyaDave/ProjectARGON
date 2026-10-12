S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/harvest.py "Keith's Machinations, Part 2" 60 1150 1 $S/anime/ep/joey_keith2 130 2>&1 | tail -1
echo BATCH7_DONE
