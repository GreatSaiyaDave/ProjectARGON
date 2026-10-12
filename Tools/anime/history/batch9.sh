S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/harvest.py "Winning Through Intimidation" 60 1150 1 $S/anime/ep/panik_e15 110 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Double Trouble Duel, Part 1" 60 1150 1 $S/anime/ep/paradox_e19 110 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Double Trouble Duel, Part 2" 60 1150 1 $S/anime/ep/paradox_e20 110 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Double Trouble Duel, Part 3" 60 1150 1 $S/anime/ep/paradox_e21 110 2>&1 | tail -1
echo BATCH9_DONE
