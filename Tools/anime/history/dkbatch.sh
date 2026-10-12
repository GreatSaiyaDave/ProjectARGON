S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
while IFS='|' read -r sub slug; do
  [ -z "$sub" ] && continue
  ./venv/bin/python anime/bodyharvest.py "$sub" 60 1150 1 $S/anime/dk/$slug 70 260 2>&1 | tail -1
done <<'LIST'
Journey To The Duelist Kingdom|e03_journey
Into The Hornet's Nest|e04_weevil
First Duel|e06_joey_mai
Attack From The Deep|e07_mako
Everything's Relative|e08_mokuba
Trial By Red-Eyes|e12_rex
Evil Spirit Of The Ring|e13_bakura
Winning Through Intimidation|e15_panik
The Scars Of Defeat|e16_kaiba_joey
Arena Of Lost Souls, Part 2|e18_bonz
Double Trouble Duel, Part 1|e19_paradox
Face Off, Part 1|e22_kaiba_yugi
Shining Friendship|e25_tea_mai
Champion Vs. Creator, Part 1|e26_kaiba_pegasus
Keith's Machinations, Part 1|e31_keith
Best Of Friends, Best Of Duelists, Part 1|e33_yugi_joey
LIST
echo DK_DONE
