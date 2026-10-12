S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/harvest.py "Dungeon Dice Monsters, Part 3" 60 1150 1 $S/anime/ep/ddm3 130 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Battle for the Bronze, Part 2" 60 1150 1 $S/anime/ep/bronze2 130 2>&1 | tail -1
echo BATCH3_DONE
