S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/hairscan.py "Match Of The Millennium, Part 2" 60 1200 205,205,215 60 anime/hs/pegasus
./venv/bin/python anime/hairscan.py "Arena Of Lost Souls, Part 2" 60 1200 60,60,70 70 anime/hs/bonz
./venv/bin/python anime/hairscan.py "Dungeon Dice Monsters, Part 2" 60 1200 40,40,45 60 anime/hs/duke
./venv/bin/python anime/hairscan.py "Everything's Relative" 60 1200 40,40,45 60 anime/hs/mokuba
./venv/bin/python anime/hairscan.py "Trial By Red-Eyes" 60 1200 120,70,40 70 anime/hs/rex
./venv/bin/python anime/hairscan.py "The Final Face-Off, Part 2" 60 1200 225,220,185 60 anime/hs/ymarik
./venv/bin/python anime/hairscan.py "The Dark Spirit Revealed： Yugi Vs. Bakura, Part 2" 60 1200 230,230,235 50 anime/hs/ybakura
./venv/bin/python anime/hairscan.py "Steppin' Out" 60 1200 200,40,120 90 anime/hs/yugimuto
./venv/bin/python anime/hairscan.py "Dungeon Dice Monsters, Part 1" 150,100,60 60 anime/hs/tristan 2>/dev/null || ./venv/bin/python anime/hairscan.py "Dungeon Dice Monsters, Part 1" 60 1200 150,100,60 60 anime/hs/tristan
echo HAIR_DONE
