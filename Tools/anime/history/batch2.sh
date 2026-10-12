S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/harvest.py "Awakening of Evil, Part 1" 60 1150 1 $S/anime/ep/joey_odion1 130 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Awakening of Evil, Part 3" 60 1150 1 $S/anime/ep/joey_odion3 130 2>&1 | tail -1
./venv/bin/python anime/hairscan.py "Mind Game： Mai Vs. Marik, Part 1" 60 1200 238,228,165 55 anime/hs/mai
./venv/bin/python anime/hairscan.py "Mind Game： Mai Vs. Marik, Part 2" 60 1200 238,228,165 55 anime/hs/mai2
echo BATCH2_DONE
