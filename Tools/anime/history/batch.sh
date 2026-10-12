S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
while IFS='|' read -r sub slug; do
  [ -z "$sub" ] && continue
  ./venv/bin/python anime/harvest.py "$sub" 60 1150 1 $S/anime/ep/$slug 130 2>&1 | tail -1
done <<'LIST'
Rare Hunter, Part 1|yyugi_rarehunter
Master of Magicians, Part 1|yyugi_arkana1
The Dark Spirit Revealed： Yugi Vs. Bakura, Part 1|yugi_bakura1
The Dark Spirit Revealed： Yugi Vs. Bakura, Part 2|yugi_bakura2
The Final Face-Off, Part 2|yugi_marik2
Double Duel, Part 2|kaiba_lumis2
A Duel With Destiny： Kaiba Vs. Ishizu, Part 1|kaiba_ishizu1
Clash in the Coliseum, Part 2|kaiba_yugi2
The ESP Duelist, Part 1|joey_espa1
Playing With a Parasite, Part 1|joey_weevil1
Legendary Fisherman, Part 1|joey_mako1
Awakening of Evil, Part 2|joey_odion2
The Darkness Returns, Part 2|joey_marik2
Battle for the Bronze, Part 1|joey_kaiba1
Mind Game： Mai Vs. Marik, Part 1|mai_marik1
Lights, Camera Duel|mai_magnum
Yugi Vs. Pegasus： Match Of The Millennium, Part 1|yugi_pegasus1
Champion Vs. Creator, Part 1|kaiba_pegasus1
Keith's Machinations, Part 1|joey_keith1
The Mystery Duelist, Part 1|yugi_keith_bc1
Arena Of Lost Souls, Part 1|joey_bonz1
Shadow of a Duel|bakura_bonz
Dungeon Dice Monsters, Part 1|yugi_duke1
Steppin' Out|steppin_out
The Past Is Present|past_present
Shining Friendship|tea_mai
Trial By Red-Eyes|joey_rex
Into The Hornet's Nest|yugi_weevil_dk
Attack From The Deep|yugi_mako_dk
The Tomb-Keepers' Secret|tombkeepers
Friends Til the End, Part 2|yugi_joey2
Everything's Relative|mokuba_dk
LIST
echo BATCH_DONE
