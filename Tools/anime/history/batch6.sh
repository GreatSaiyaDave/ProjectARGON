S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/harvest.py "Journey To The Duelist Kingdom" 60 1150 1 $S/anime/ep/journey_dk 130 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Legendary Heroes, Part 2" 60 1150 1 $S/anime/ep/legheroes2 130 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Legendary Heroes, Part 1" 60 1150 1 $S/anime/ep/legheroes1 130 2>&1 | tail -1
echo BATCH6_DONE
