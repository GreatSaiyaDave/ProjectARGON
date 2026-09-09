from pathlib import Path
p=Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs')
t=p.read_text(encoding='utf-8')

old='''                CheckFilterSs(57839750, "Mother Grizzly", "WATER", 1500, true);
                CheckFilterSs(60806437, "UFO Turtle", "FIRE", 1500, true);
                CheckFilterSs(39191307, "Masked Dragon", "Dragon", 1500, true);
                CheckFilterSs(77044671, "Pyramid Turtle", "Zombie", 2000, false);
                CheckFilterSs(84834865, "Flying Kamakiri #1", "WIND", 1500, true);
                CheckFilterSs(93107608, "Howling Insect", "Insect", 1500, true);
                CheckFilterSs(95956346, "Shining Angel", "LIGHT", 1500, true);
'''
new='''                CheckFilterSs(83011278, "Mystic Tomato", "DARK", 1500, true);
                CheckFilterSs(97017120, "Giant Rat", "EARTH", 1500, true);
                CheckFilterSs(57839750, "Mother Grizzly", "WATER", 1500, true);
                CheckFilterSs(60806437, "UFO Turtle", "FIRE", 1500, true);
                CheckFilterSs(39191307, "Masked Dragon", "Dragon", 1500, true);
                CheckFilterSs(77044671, "Pyramid Turtle", "Zombie", 2000, false);
                CheckFilterSs(84834865, "Flying Kamakiri #1", "WIND", 1500, true);
                CheckFilterSs(93107608, "Howling Insect", "Insect", 1500, true);
                CheckFilterSs(95956346, "Shining Angel", "LIGHT", 1500, true);
'''
if old not in t:
    raise SystemExit('CheckFilterSs block not found')
t=t.replace(old, new, 1)
print('OK CheckFilterSs Tomato+Rat')

# Insert P1 block after Spellbinding Circle PARKED check inside Deck-Grok P0 section
anchor='''                var sbProg = CardTextEffectCompiler.Compile(db.Get(spellbinding));
                Check("Spellbinding Circle PARKED (no target-bound continuous cannot-attack)",
                    sbProg == null || !sbProg.FullyCompiled);
'''
if anchor not in t:
    raise SystemExit('P0 park anchor not found')

p1=r'''                var sbProg = CardTextEffectCompiler.Compile(db.Get(spellbinding));
                Check("Spellbinding Circle PARKED (no target-bound continuous cannot-attack)",
                    sbProg == null || !sbProg.FullyCompiled);

                // ── Deck-Grok P1 wander searchers (shared kinds / PSCT fragments) ──
                {
                    void CheckSearcher(int id, string label, System.Func<CompiledCardProgram, bool> ok)
                    {
                        var d = db.Get(id);
                        var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                        Check(label,
                            pr != null && pr.FullyCompiled && ok(pr),
                            pr == null
                                ? "null"
                                : $"full={pr.FullyCompiled} n={pr.ClauseList.Count} " +
                                  $"unparsed={string.Join("|", pr.UnparsedFragments ?? System.Array.Empty<string>())}");
                    }

                    CheckSearcher(32807846, "ROTA FullyCompiled Level≤4 Warrior Deck add",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq &&
                            c.Amount == 4 &&
                            !string.IsNullOrEmpty(c.RaceFilter) &&
                            c.RaceFilter.IndexOf("Warrior", System.StringComparison.OrdinalIgnoreCase) >= 0));

                    CheckSearcher(31786629, "Thunder Dragon FullyCompiled discard-self named Deck add×2",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresDiscardSelf && c.ActivatesFromHand &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck && c.Amount == 2 &&
                            string.Equals(c.NamedCard, "Thunder Dragon",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(24317029, "Gravekeeper's Spy FullyCompiled Flip series ATK≤1500 Deck SS",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck && c.AmountIsAtkMax && c.Amount == 1500 &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(45547649, "Birdface FullyCompiled battle-GY named Deck add (not free ignition)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck &&
                            string.Equals(c.NamedCard, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase)) &&
                        !pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            !c.RequiresThisDestroyedByBattle));

                    CheckSearcher(89997728, "Toon Table of Contents FullyCompiled named Toon Deck add",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck &&
                            string.Equals(c.NamedCard, "Toon",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(36262024, "Black Dragon's Chick FullyCompiled send-this named hand SS",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresSendThisToGy &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand &&
                            string.Equals(c.NamedCard, "Red-Eyes B. Dragon",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(14087893, "Book of Moon FullyCompiled SetTargetFaceDownDefense",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                            c.RequiresTargetChoice));

                    CheckSearcher(53582587, "Torrential Tribute FullyCompiled summon-window destroy all",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.Destroy &&
                            c.AnswersSpecialSummon && c.AnswersControllerSummon &&
                            c.Side == EffectSide.Both));

                    CheckSearcher(30450531, "Rite of Spirit FullyCompiled named GY-SS (Necrovalley rider absorbed)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(15259703, "Toon World FullyCompiled pay-1000 activate",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.PayLpAmount == 1000 && c.StaysOnField));

                    CheckSearcher(45986603, "Snatch Steal FullyCompiled take-control + opp Standby they-gain-LP",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.TakeControlOfTarget) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.GainLifePoints &&
                            c.Side == EffectSide.Opponent && c.OpponentTurnOnly &&
                            c.Amount == 1000));

                    void CheckParked(int id, string label)
                    {
                        var d = db.Get(id);
                        var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                        Check(label + " PARKED",
                            pr == null || !pr.FullyCompiled,
                            pr == null ? "null" :
                            $"full={pr.FullyCompiled} unparsed={string.Join("|", pr.UnparsedFragments ?? System.Array.Empty<string>())}");
                    }

                    CheckParked(26185991, "Pinch Hopper");
                    CheckParked(68191243, "Mustering of the Dark Scorpions");
                    CheckParked(2204140, "Book of Life");
                    CheckParked(47355498, "Necrovalley");
                    CheckParked(75782277, "Harpies' Hunting Ground");
                    CheckParked(3819470, "Seven Tools of the Bandit");
                    CheckParked(77414722, "Magic Jammer");
                    CheckParked(18807108, "Spellbinding Circle");
                    CheckParked(57728570, "Crush Card Virus");
                    CheckParked(71625222, "Time Wizard");
                    CheckParked(81210440, "Magical Hats");
                    CheckParked(40703222, "Multiply");
                }

'''
t=t.replace(anchor, p1, 1)
p.write_text(t, encoding='utf-8')
print('OK P1 tests inserted')
# verify Crush/Time/Hats/Multiply ids exist in db
import json
db=json.loads(Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/StreamingAssets/Cards/cards_db.json').read_text())
idx={int(c['id']):c['name'] for c in db['cards']}
for i in [57728570,71625222,81210440,40703222,3819470,77414722,18807108]:
    print(i, idx.get(i, 'MISSING'))
