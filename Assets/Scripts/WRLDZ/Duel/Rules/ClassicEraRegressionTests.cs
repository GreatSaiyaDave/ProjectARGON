using System.Linq;
using System.Text;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Curriculum batch LOB-close + MRD tranche 1 (compiler v51). Each card is driven
    /// through the real activation / Flip / battle / board-refresh path, not only its
    /// compile. Families: multi-Type Field Spells, exact Type matching, kind-checked
    /// Set-card destruction, explicit position changes, Fissure, multi-target
    /// (Gravedigger Ghoul / Soul Release / Two-Pronged Attack), Dragon Capture Jar +
    /// Dragon Piper, Exodia, Change of Heart (control + ownership), LP loss, self-scaling
    /// ATK, battle-damage triggers and The Unhappy Maiden.
    /// </summary>
    public static class ClassicEraRegressionTests
    {
        // LOB
        const int Umi = 22702055;
        const int Wasteland = 23424603;
        const int Mountain = 50913601;
        const int Yami = 59197169;
        const int Sogen = 86318356;
        const int Forest = 87430998;
        const int VioletCrystal = 15052462;
        const int FollowWind = 98252586;
        const int ArmedNinja = 9076207;
        const int ReaperOfTheCards = 33066139;
        const int StopDefense = 63102017;
        const int Fissure = 66788016;
        const int GravediggerGhoul = 82542267;
        const int TwoPronged = 83887306;
        const int DragonCaptureJar = 50045299;
        const int Exodia = 33396948;
        const int RightLeg = 8124921;
        const int LeftLeg = 44519536;
        const int RightArm = 70903634;
        const int LeftArm = 7902349;
        // MRD
        const int BlockAttack = 25880422;
        const int ChangeOfHeart = 4031928;
        const int SoulRelease = 5758500;
        const int TremendousFire = 46918794;
        const int ImmortalOfThunder = 84926738;
        const int LavaBattleguard = 20394040;
        const int SwampBattleguard = 40453765;
        const int ShadowGhoul = 30778711;
        const int MukaMuka = 46657337;
        const int LittleSwordsman = 25109950;
        const int UnhappyMaiden = 51275027;
        const int DragonPiper = 55763552;
        const int MaskedSorcerer = 10189126;
        const int BistroButcher = 71107816;
        const int WhiteMagicalHat = 15150365;
        const int RobbinGoblin = 88279736;
        // Vanilla / helpers
        const int Celtic = 91152256;          // Warrior 1400/1200
        const int GreatWhite = 13429800;      // Fish 1600/800
        const int Mechanicalchaser = 7359741; // Machine 1850/800
        const int HarpieLady = 76812113;      // Winged Beast 1300/1400
        const int SilverFang = 90357090;      // Beast 1200/800
        const int HitotsuMe = 76184692;       // Beast-Warrior 1200/1000
        const int SkullServant = 32274490;    // Zombie 300/200
        const int KoumoriDragon = 67724379;   // Dragon 1500/1200
        const int PetitAngel = 38142739;      // Fairy 600/900
        const int MysticalElf = 15025844;     // Spellcaster 800/2000
        const int CommandKnight = 10375182;
        const int PotOfGreed = 55144522;
        const int TrapHole = 4206964;
        const int Mst = 5318639;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; sb.AppendLine("PASS  " + name); }
                else { fail++; sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
            }

            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                sb.AppendLine("FAIL  CardDatabase load");
                sb.AppendLine("--- 0 passed, 1 failed ---");
                return sb.ToString();
            }

            // ─────────────────────────── Compile gate ───────────────────────────
            {
                var ids = new[]
                {
                    Umi, Wasteland, Mountain, Yami, Sogen, Forest, VioletCrystal, FollowWind, ArmedNinja,
                    ReaperOfTheCards, StopDefense, Fissure, GravediggerGhoul, TwoPronged, DragonCaptureJar,
                    Exodia, BlockAttack, ChangeOfHeart, SoulRelease, TremendousFire, ImmortalOfThunder,
                    LavaBattleguard, SwampBattleguard, ShadowGhoul, MukaMuka, LittleSwordsman, UnhappyMaiden,
                    DragonPiper, MaskedSorcerer, BistroButcher, WhiteMagicalHat, RobbinGoblin
                };
                var bad = ids.Where(id =>
                {
                    var d = db.Get(id);
                    var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                    return pr == null || !pr.FullyCompiled || pr.ClauseList.Count == 0;
                }).Select(id => db.Get(id)?.name ?? id.ToString()).ToList();
                Check($"Compile: all {ids.Length} batch cards FullyCompiled from official text",
                    bad.Count == 0, string.Join(", ", bad));
            }

            // Element monsters: conditional Attribute bullets are continuous effects keyed on
            // the field — never an unconditional ignition. Refuse (fail closed) until modeled.
            {
                var bad = new[] { 30314994, 97623219, 92755808, 23118924, 65260293, 66712593 }
                    .Where(id =>
                    {
                        var d = db.Get(id);
                        var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                        return pr == null || pr.FullyCompiled || pr.ClauseList.Count > 0;
                    }).Select(id => db.Get(id)?.name ?? id.ToString()).ToList();
                Check("Compile: Element monsters' conditional Attribute bullets refuse (no false ignition)",
                    bad.Count == 0, string.Join(", ", bad));
            }

            // Exact Type matching: every RaceFilter in a FullyCompiled (playable) program must
            // name printed monster Types, or the filter would silently match nothing. Stubs are
            // refused at activation, so they are out of scope here.
            {
                var odd = new System.Collections.Generic.List<string>();
                foreach (var d in db.GetAllCards())
                {
                    if (d == null) continue;
                    var pr = CardTextEffectCompiler.Compile(d);
                    if (!pr.FullyCompiled) continue;
                    foreach (var c in pr.ClauseList)
                    {
                        if (c == null || string.IsNullOrEmpty(c.RaceFilter)) continue;
                        foreach (var part in c.RaceFilter.Split(',', '|'))
                        {
                            var t = part.Trim();
                            if (t.EndsWith("-Type")) t = t.Substring(0, t.Length - 5);
                            if (!ClassicEraTemplates.MonsterTypes.Contains(t))
                                odd.Add($"{d.name}:{c.RaceFilter}");
                        }
                    }
                }
                Check("Compile: every RaceFilter in a FullyCompiled program is a printed monster Type",
                    odd.Count == 0, string.Join(", ", odd.Distinct().Take(12)));
            }

            // ─────────────────────── Field Spells / Type match ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var fish = PlaceMonster(e, me, GreatWhite, 0);
                var machine = PlaceMonster(e, opp, Mechanicalchaser, 0);
                var warrior = PlaceMonster(e, me, Celtic, 1);
                PlaceField(e, me, Umi);
                e.NotifyPublic();
                Check("Umi: Fish +200/+200 (1800/1000)", fish.CurrentAtk == 1800 && fish.CurrentDef == 1000,
                    $"{fish.CurrentAtk}/{fish.CurrentDef}");
                Check("Umi: opponent's Machine −200/−200 (1650/600)",
                    machine.CurrentAtk == 1650 && machine.CurrentDef == 600, $"{machine.CurrentAtk}/{machine.CurrentDef}");
                Check("Umi: Warrior unaffected", warrior.CurrentAtk == 1400);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var beast = PlaceMonster(e, me, SilverFang, 0);
                var winged = PlaceMonster(e, me, HarpieLady, 1);
                var bw = PlaceMonster(e, opp, HitotsuMe, 0);
                PlaceField(e, me, Forest);
                e.NotifyPublic();
                Check("Forest: Beast +200", beast.CurrentAtk == 1400, beast.CurrentAtk.ToString());
                Check("Forest: Beast-Warrior +200 (listed)", bw.CurrentAtk == 1400, bw.CurrentAtk.ToString());
                Check("Forest: Winged Beast is not a Beast (exact Type match)", winged.CurrentAtk == 1300,
                    winged.CurrentAtk.ToString());
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var elf = PlaceMonster(e, me, MysticalElf, 0);
                var angel = PlaceMonster(e, opp, PetitAngel, 0);
                PlaceField(e, opp, Yami);
                e.NotifyPublic();
                Check("Yami: Spellcaster +200 on either field", elf.CurrentAtk == 1000);
                Check("Yami: Fairy −200", angel.CurrentAtk == 400 && angel.CurrentDef == 700,
                    $"{angel.CurrentAtk}/{angel.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var knight = PlaceMonster(e, me, CommandKnight, 0);
                var bw = PlaceMonster(e, me, HitotsuMe, 1);
                var w = PlaceMonster(e, me, Celtic, 2);
                e.NotifyPublic();
                Check("Command Knight: Warrior aura does not boost a Beast-Warrior",
                    bw.CurrentAtk == 1200 && w.CurrentAtk > 1400,
                    $"bw={bw.CurrentAtk} celtic={w.CurrentAtk} knight={knight.CurrentAtk}");
            }

            // ─────────────────────────── Equips ───────────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var zombie = PlaceMonster(e, me, SkullServant, 0);
                PlaceMonster(e, me, Celtic, 1);
                var vc = Hand(e, me, VioletCrystal);
                var ok = Activate(e, me, vc, true);
                Check("Violet Crystal equips only a Zombie (+300/+300)",
                    ok && zombie.CurrentAtk == 600 && zombie.CurrentDef == 500,
                    $"ok={ok} {zombie.CurrentAtk}/{zombie.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var harpie = PlaceMonster(e, me, HarpieLady, 0);
                var fw = Hand(e, me, FollowWind);
                var ok = Activate(e, me, fw, true);
                Check("Follow Wind equips a Winged Beast (+300/+300)",
                    ok && harpie.CurrentAtk == 1600 && harpie.CurrentDef == 1700,
                    $"ok={ok} {harpie.CurrentAtk}/{harpie.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, me, SilverFang, 0);
                var fw = Hand(e, me, FollowWind);
                Check("Follow Wind cannot equip a Beast (no Winged Beast)",
                    !e.CanActivateSpellTrap(me, fw, fromHand: true));
            }

            // ─────────────────── Armed Ninja / Reaper of the Cards ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var setSpell = PlaceSet(e, opp, PotOfGreed, 0);
                var ninja = PlaceMonster(e, me, ArmedNinja, 0);
                MonsterEffects.OnFlipSummoned(e, me, ninja);
                Resolve(e);
                Check("Armed Ninja: Set Spell is revealed and destroyed",
                    !OnField(opp, setSpell) && opp.Graveyard.Contains(setSpell));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var setTrap = PlaceSet(e, opp, TrapHole, 0);
                var ninja = PlaceMonster(e, me, ArmedNinja, 0);
                MonsterEffects.OnFlipSummoned(e, me, ninja);
                Resolve(e);
                Check("Armed Ninja: a Set Trap is revealed and returned (not destroyed)",
                    OnField(opp, setTrap) && !setTrap.FaceUp);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var upTrap = PlaceSpellTrap(e, opp, DragonCaptureJar, 0);
                var ninja = PlaceMonster(e, me, ArmedNinja, 0);
                var prog = CompiledEffectCache.GetOrCompile(ninja.Def);
                var clause = prog.ClausesFor(EffectTiming.Flip).First();
                MonsterEffects.OnFlipSummoned(e, me, ninja);
                Resolve(e);
                Check("Armed Ninja: a face-up Trap is not a legal target",
                    clause.TargetCardKind == "Spell" && OnField(opp, upTrap));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var setTrap = PlaceSet(e, opp, TrapHole, 0);
                var reaper = PlaceMonster(e, me, ReaperOfTheCards, 0);
                MonsterEffects.OnFlipSummoned(e, me, reaper);
                Resolve(e);
                Check("Reaper of the Cards: Set Trap is destroyed", opp.Graveyard.Contains(setTrap));
            }

            // ─────────────────────── Stop Defense / Block Attack ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var def = PlaceMonster(e, opp, Celtic, 0);
                def.Position = BattlePosition.Defense;
                var card = Hand(e, me, StopDefense);
                var ok = Activate(e, me, card, true);
                Check("Stop Defense: Defense Position monster → face-up Attack",
                    ok && def.FaceUp && def.Position == BattlePosition.Attack && me.Graveyard.Contains(card));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var set = PlaceMonster(e, opp, Celtic, 0);
                set.Position = BattlePosition.Defense;
                set.FaceUp = false;
                var card = Hand(e, me, StopDefense);
                var ok = Activate(e, me, card, true);
                Check("Stop Defense: a Set monster is flipped to face-up Attack",
                    ok && set.FaceUp && set.Position == BattlePosition.Attack);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, opp, Celtic, 0);
                var card = Hand(e, me, StopDefense);
                Check("Stop Defense: no Defense Position target → cannot activate",
                    !e.CanActivateSpellTrap(me, card, fromHand: true));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var atk = PlaceMonster(e, opp, Celtic, 0);
                var card = Hand(e, me, BlockAttack);
                var ok = Activate(e, me, card, true);
                Check("Block Attack: face-up Attack → face-up Defense",
                    ok && atk.FaceUp && atk.Position == BattlePosition.Defense);
            }

            // ─────────────────────────── Fissure ───────────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var strong = PlaceMonster(e, opp, Mechanicalchaser, 0);
                var weak = PlaceMonster(e, opp, SkullServant, 1);
                var setWeaker = PlaceMonster(e, opp, PetitAngel, 2);
                setWeaker.FaceUp = false;
                setWeaker.Position = BattlePosition.Defense;
                var card = Hand(e, me, Fissure);
                var ok = Activate(e, me, card, true);
                Check("Fissure: destroys the lowest-ATK face-up monster only",
                    ok && !OnField(opp, weak) && OnField(opp, strong) && OnField(opp, setWeaker));
            }

            // ─────────────────── Gravedigger Ghoul / Soul Release ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                opp.Graveyard.Clear(); me.Graveyard.Clear();
                var a = Gy(e, opp, Celtic); var b = Gy(e, opp, KoumoriDragon); var c = Gy(e, opp, SkullServant);
                var mine = Gy(e, me, Celtic);
                var card = Hand(e, me, GravediggerGhoul);
                var ok = Activate(e, me, card, true); // auto-pick (AI path)
                Check("Gravedigger Ghoul (auto): banishes 2 opponent GY monsters, not yours",
                    ok && opp.Banished.Count == 2 && opp.Graveyard.Count == 1 && me.Graveyard.Contains(mine),
                    $"banished={opp.Banished.Count} oppGy={opp.Graveyard.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                opp.Graveyard.Clear();
                var a = Gy(e, opp, Celtic); Gy(e, opp, KoumoriDragon);
                var card = Hand(e, me, GravediggerGhoul);
                var ok = e.TryActivateSpellTrap(me, card, fromHand: true);
                var pending = e.PendingActivation;
                var multi = pending != null && pending.MultiTarget && pending.LegalTargets.Count == 2;
                var picked = e.TrySelectEffectTarget(a);
                var stillPicking = e.IsAwaitingEffectTarget;
                var finished = e.CancelEffectTargeting(); // "up to": Cancel after 1 pick finishes
                Check("Gravedigger Ghoul (player): pick 1 then Cancel banishes just that one",
                    ok && multi && picked && stillPicking && finished &&
                    opp.Banished.Contains(a) && opp.Banished.Count == 1 && me.Graveyard.Contains(card),
                    $"ok={ok} multi={multi} picked={picked} still={stillPicking} banished={opp.Banished.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                opp.Graveyard.Clear();
                var card = Hand(e, me, GravediggerGhoul);
                Check("Gravedigger Ghoul: empty opponent GY → cannot activate",
                    !e.CanActivateSpellTrap(me, card, fromHand: true));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                opp.Graveyard.Clear(); me.Graveyard.Clear();
                for (var i = 0; i < 4; i++) Gy(e, opp, Celtic);
                for (var i = 0; i < 3; i++) Gy(e, me, PotOfGreed);
                var card = Hand(e, me, SoulRelease);
                var ok = Activate(e, me, card, true);
                Check("Soul Release: banishes at most 5 cards, opponent's first",
                    ok && opp.Banished.Count == 4 && me.Banished.Count == 1,
                    $"opp={opp.Banished.Count} me={me.Banished.Count}");
            }

            // ─────────────────────── Two-Pronged Attack ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var m1 = PlaceMonster(e, me, SkullServant, 0);
                var m2 = PlaceMonster(e, me, PetitAngel, 1);
                var m3 = PlaceMonster(e, me, Mechanicalchaser, 2);
                var o1 = PlaceMonster(e, opp, KoumoriDragon, 0);
                var trap = PlaceSet(e, me, TwoPronged, 0);
                var ok = Activate(e, me, trap, false);
                Check("Two-Pronged Attack: destroys 2 of yours (weakest) + 1 of theirs",
                    ok && !OnField(me, m1) && !OnField(me, m2) && OnField(me, m3) && !OnField(opp, o1) &&
                    me.Graveyard.Contains(trap));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                PlaceMonster(e, me, SkullServant, 0);
                PlaceMonster(e, opp, KoumoriDragon, 0);
                var trap = PlaceSet(e, me, TwoPronged, 0);
                Check("Two-Pronged Attack: needs 2 monsters you control",
                    !e.CanActivateSpellTrap(me, trap, fromHand: false));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var m1 = PlaceMonster(e, me, SkullServant, 0);
                var m2 = PlaceMonster(e, me, PetitAngel, 1);
                var o1 = PlaceMonster(e, opp, KoumoriDragon, 0);
                var trap = PlaceSet(e, me, TwoPronged, 0);
                var ok = e.TryActivateSpellTrap(me, trap, fromHand: false);
                var kind1 = e.PendingActivation?.TargetKind;
                e.TrySelectEffectTarget(m1);
                e.TrySelectEffectTarget(m2);
                var kind2 = e.PendingActivation?.TargetKind;
                var oppLegal = e.PendingActivation?.LegalTargets.Contains(o1) ?? false;
                e.TrySelectEffectTarget(o1);
                Check("Two-Pronged Attack (player): picks yours then theirs, then resolves",
                    ok && kind1 == EffectTargetKind.YourMonster && kind2 == EffectTargetKind.OppMonster &&
                    oppLegal && !OnField(me, m1) && !OnField(me, m2) && !OnField(opp, o1) &&
                    !e.IsAwaitingEffectTarget,
                    $"ok={ok} k1={kind1} k2={kind2} oppLegal={oppLegal}");
            }

            // ─────────────────── Dragon Capture Jar / Dragon Piper ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var dragon = PlaceMonster(e, opp, KoumoriDragon, 0);
                var mine = PlaceMonster(e, me, KoumoriDragon, 0);
                var warrior = PlaceMonster(e, me, Celtic, 1);
                var jar = PlaceSet(e, me, DragonCaptureJar, 0);
                var ok = Activate(e, me, jar, false);
                Check("Dragon Capture Jar: every face-up Dragon → Defense",
                    ok && jar.FaceUp && dragon.Position == BattlePosition.Defense &&
                    mine.Position == BattlePosition.Defense && warrior.Position == BattlePosition.Attack);
                Check("Dragon Capture Jar: a locked Dragon cannot change position",
                    mine.PositionLockedByEffect && !e.CanChangePosition(me, mine));

                var piper = PlaceMonster(e, opp, DragonPiper, 1);
                MonsterEffects.OnFlipSummoned(e, opp, piper);
                Resolve(e);
                Check("Dragon Piper: destroys the face-up Jar, Dragons → Attack",
                    me.Graveyard.Contains(jar) && dragon.Position == BattlePosition.Attack &&
                    mine.Position == BattlePosition.Attack && !mine.PositionLockedByEffect);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var dragon = PlaceMonster(e, opp, KoumoriDragon, 0);
                dragon.Position = BattlePosition.Defense;
                var piper = PlaceMonster(e, me, DragonPiper, 1);
                MonsterEffects.OnFlipSummoned(e, me, piper);
                Resolve(e);
                Check("Dragon Piper: no Jar destroyed → Dragons stay in Defense",
                    dragon.Position == BattlePosition.Defense);
            }

            // ─────────────────────────── Exodia ───────────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                me.Hand.Clear();
                Hand(e, me, Exodia); Hand(e, me, RightLeg); Hand(e, me, LeftLeg); Hand(e, me, RightArm);
                e.NotifyPublic();
                var notYet = !e.GameOver;
                Hand(e, me, LeftArm);
                e.NotifyPublic();
                Check("Exodia: four pieces do nothing; the fifth wins the Duel",
                    notYet && e.GameOver && e.Winner == me, $"over={e.GameOver} winnerIsMe={e.Winner == me}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                me.Hand.Clear();
                Hand(e, me, RightLeg); Hand(e, me, LeftLeg); Hand(e, me, RightArm); Hand(e, me, LeftArm);
                PlaceMonster(e, me, Exodia, 0);
                e.NotifyPublic();
                Check("Exodia: the head must be in the hand, not on the field", !e.GameOver);
            }

            // ─────────────────────────── Change of Heart ───────────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var stolen = PlaceMonster(e, opp, KoumoriDragon, 0);
                var card = Hand(e, me, ChangeOfHeart);
                var ok = Activate(e, me, card, true);
                Check("Change of Heart: take control of the target",
                    ok && OnField(me, stolen) && !OnField(opp, stolen) && stolen.ControlOwner == opp);
                e.TryEndTurnSafe(me);
                Check("Change of Heart: control returns in the End Phase",
                    OnField(opp, stolen) && !OnField(me, stolen) && stolen.ControlOwner == null);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); opp.Graveyard.Clear(); me.Graveyard.Clear();
                var stolen = PlaceMonster(e, opp, KoumoriDragon, 0);
                var card = Hand(e, me, ChangeOfHeart);
                Activate(e, me, card, true);
                e.SendCardToGrave(me, stolen); // e.g. Tributed by the thief
                Check("Change of Heart: a stolen monster leaving the field goes to its owner's GY",
                    opp.Graveyard.Contains(stolen) && !me.Graveyard.Contains(stolen));
            }

            // ─────────────────────── Tremendous Fire / Immortal ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var lpMe = me.LifePoints; var lpOpp = opp.LifePoints;
                var card = Hand(e, me, TremendousFire);
                var ok = Activate(e, me, card, true);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Tremendous Fire: 1000 to the opponent, 500 to you",
                    ok && opp.LifePoints == lpOpp - 1000 && me.LifePoints == lpMe - 500,
                    $"opp {opp.LifePoints} me {me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var imm = PlaceMonster(e, me, ImmortalOfThunder, 0);
                var lp = me.LifePoints;
                e.DestroyMonsterPublic(me, imm);
                Resolve(e);
                Check("The Immortal of Thunder: sent from the field → lose 5000 LP (not damage)",
                    me.LifePoints == lp - 5000, $"LP {me.LifePoints}");
            }

            // ─────────────────────── Self-scaling ATK ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var lava = PlaceMonster(e, me, LavaBattleguard, 0);
                var swamp = PlaceMonster(e, me, SwampBattleguard, 1);
                PlaceMonster(e, opp, SwampBattleguard, 0); // opponent's does not count
                e.NotifyPublic();
                Check("Battleguards: each gains 500 per partner you control",
                    lava.CurrentAtk == 2050 && swamp.CurrentAtk == 2300, $"lava={lava.CurrentAtk} swamp={swamp.CurrentAtk}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Graveyard.Clear();
                var ghoul = PlaceMonster(e, me, ShadowGhoul, 0);
                Gy(e, me, Celtic); Gy(e, me, Celtic); Gy(e, me, PotOfGreed);
                e.NotifyPublic();
                Check("Shadow Ghoul: +100 per monster in your GY (spells do not count)",
                    ghoul.CurrentAtk == 1800, ghoul.CurrentAtk.ToString());
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var muka = PlaceMonster(e, me, MukaMuka, 0);
                Hand(e, me, Celtic); Hand(e, me, PotOfGreed); Hand(e, me, TrapHole);
                e.NotifyPublic();
                Check("Muka Muka: +300/+300 per card in hand",
                    muka.CurrentAtk == 1500 && muka.CurrentDef == 1200, $"{muka.CurrentAtk}/{muka.CurrentDef}");
            }

            // ─────────────────────── Little Swordsman of Aile ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var aile = PlaceMonster(e, me, LittleSwordsman, 0);
                var fodder = PlaceMonster(e, me, SkullServant, 1);
                var prog = CompiledEffectCache.GetOrCompile(aile.Def);
                var ok = TextEffectRuntime.CanActivate(e, me, aile, false, prog, out var why) &&
                         TextEffectRuntime.TryResolveActivation(e, me, aile, false, prog, true);
                Resolve(e);
                Check("Little Swordsman of Aile: Tribute another monster → +700 ATK this turn",
                    ok && !OnField(me, fodder) && OnField(me, aile) && aile.CurrentAtk == 1500,
                    $"ok={ok} why={why} atk={aile.CurrentAtk}");
            }

            // ─────────────────────── Battle-damage triggers ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var sorc = PlaceMonster(e, me, MaskedSorcerer, 0);
                var hand0 = me.Hand.Count;
                Attack(e, me, sorc, null);
                Check("Masked Sorcerer: battle damage → you draw 1", me.Hand.Count == hand0 + 1,
                    $"hand {hand0}→{me.Hand.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var bistro = PlaceMonster(e, me, BistroButcher, 0);
                var hand0 = opp.Hand.Count;
                var myHand0 = me.Hand.Count;
                Attack(e, me, bistro, null);
                Check("The Bistro Butcher: battle damage → your opponent draws 2",
                    opp.Hand.Count == hand0 + 2 && me.Hand.Count == myHand0,
                    $"opp hand {hand0}→{opp.Hand.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var hat = PlaceMonster(e, me, WhiteMagicalHat, 0);
                opp.Hand.Clear();
                var x = Hand(e, opp, Celtic); var y = Hand(e, opp, PotOfGreed);
                e.Rng.QueuePick(1);
                Attack(e, me, hat, null);
                Check("White Magical Hat: battle damage → opponent discards 1 at random",
                    opp.Hand.Count == 1 && opp.Hand.Contains(x) && opp.Graveyard.Contains(y));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var goblin = PlaceSpellTrap(e, me, RobbinGoblin, 0);
                var atk = PlaceMonster(e, me, Celtic, 0);
                opp.Hand.Clear();
                Hand(e, opp, Celtic);
                Attack(e, me, atk, null);
                Check("Robbin' Goblin: your monster's battle damage → opponent discards 1",
                    opp.Hand.Count == 0 && goblin.FaceUp);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var sorc = PlaceMonster(e, me, MaskedSorcerer, 0);
                PlaceMonster(e, opp, Mechanicalchaser, 0).Position = BattlePosition.Defense;
                var hand0 = me.Hand.Count;
                Attack(e, me, sorc, opp.MonstersOnField().First());
                Check("Masked Sorcerer: no battle damage to the opponent → no draw",
                    me.Hand.Count == hand0);
            }

            // ═════════════════════ MRD tranche 2 (compiler v52) ═════════════════════
            {
                var ids = new[] { 13215230, 93889755, 28725004, 50152549, 24668830, 83225447, 20436034,
                    21417692, 7019529, 94773007, 55875323, 52097679, 41142615 };
                var bad = ids.Where(id =>
                {
                    var d = db.Get(id);
                    var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                    return pr == null || !pr.FullyCompiled;
                }).Select(id => db.Get(id)?.name ?? id.ToString()).ToList();
                Check("Compile: MRD tranche 2 (13 cards) FullyCompiled", bad.Count == 0, string.Join(", ", bad));
            }

            // Position-change triggers
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var clown = PlaceMonster(e, me, 13215230, 0); // Dream Clown
                var victim = PlaceMonster(e, opp, Celtic, 0);
                var ok = e.TryChangePosition(me, clown);
                Resolve(e);
                Check("Dream Clown: Attack → Defense destroys an opponent's monster",
                    ok && !OnField(opp, victim) && opp.Graveyard.Contains(victim));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); opp.Hand.Clear();
                var clown = PlaceMonster(e, me, 93889755, 0); // Crass Clown
                clown.Position = BattlePosition.Defense;
                var victim = PlaceMonster(e, opp, Celtic, 0);
                var ok = e.TryChangePosition(me, clown);
                Resolve(e);
                Check("Crass Clown: Defense → Attack returns an opponent's monster to the hand",
                    ok && !OnField(opp, victim) && opp.Hand.Contains(victim));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var clown = PlaceMonster(e, me, 13215230, 0); // Dream Clown
                clown.Position = BattlePosition.Defense;
                var victim = PlaceMonster(e, opp, Celtic, 0);
                e.TryChangePosition(me, clown);
                Resolve(e);
                Check("Dream Clown: Defense → Attack does not trigger", OnField(opp, victim));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var wisdom = PlaceMonster(e, me, 28725004, 0); // Tainted Wisdom
                var deck0 = me.Deck.Count;
                var ok = e.TryChangePosition(me, wisdom);
                Check("Tainted Wisdom: Attack → Defense shuffles the Deck (same cards)",
                    ok && me.Deck.Count == deck0 && e.PendingActivation == null);
            }

            // Equips
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var target = PlaceMonster(e, opp, KoumoriDragon, 0);
                var potion = Hand(e, me, 50152549); // Paralyzing Potion
                var ok = Activate(e, me, potion, true);
                Check("Paralyzing Potion: equips an opponent's monster; it cannot attack",
                    ok && target.Equips.Contains(potion) &&
                    TextEffectRuntime.AttackForbiddenByEffect(e, target));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, opp, Mechanicalchaser, 0);
                var potion = Hand(e, me, 50152549);
                Check("Paralyzing Potion: cannot equip a Machine",
                    !e.CanActivateSpellTrap(me, potion, fromHand: true));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var target = PlaceMonster(e, opp, KoumoriDragon, 0);
                var germ = Hand(e, me, 24668830); // Germ Infection
                Activate(e, me, germ, true);
                TextEffectRuntime.FirePhaseTriggers(e, me, EffectTiming.StandbyPhase);
                var afterMine = target.CurrentAtk;
                TextEffectRuntime.FirePhaseTriggers(e, opp, EffectTiming.StandbyPhase);
                var afterTheirs = target.CurrentAtk;
                TextEffectRuntime.FirePhaseTriggers(e, opp, EffectTiming.StandbyPhase);
                Check("Germ Infection: −300 ATK at each of the equipped monster's Standby Phases",
                    afterMine == 1500 && afterTheirs == 1200 && target.CurrentAtk == 900,
                    $"{afterMine}/{afterTheirs}/{target.CurrentAtk}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var mine = PlaceMonster(e, me, Celtic, 0);
                var stim = Hand(e, me, 83225447); // Stim-Pack
                Activate(e, me, stim, true);
                var boosted = mine.CurrentAtk;
                TextEffectRuntime.FirePhaseTriggers(e, opp, EffectTiming.StandbyPhase);
                var oppStandby = mine.CurrentAtk;
                TextEffectRuntime.FirePhaseTriggers(e, me, EffectTiming.StandbyPhase);
                Check("Stim-Pack: +700, then −200 at each of YOUR Standby Phases",
                    boosted == 2100 && oppStandby == 2100 && mine.CurrentAtk == 1900,
                    $"{boosted}/{oppStandby}/{mine.CurrentAtk}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, opp);
                ClearField(me); ClearField(opp);
                var ringed = PlaceMonster(e, me, Celtic, 0);
                var other = PlaceMonster(e, me, SkullServant, 1);
                var ring = PlaceSpellTrap(e, me, 20436034, 0); // Ring of Magnetism
                ring.EquippedTo = ringed; ringed.Equips.Add(ring);
                var atk = PlaceMonster(e, opp, Mechanicalchaser, 0);
                e.NotifyPublic();
                var vOther = e.ValidateAttack(opp, atk, other);
                var vDirect = e.ValidateAttack(opp, atk, null);
                var vRing = e.ValidateAttack(opp, atk, ringed);
                Check("Ring of Magnetism: −500/−500 and only the equipped monster can be attacked",
                    ringed.CurrentAtk == 900 && !vOther.Legal && !vDirect.Legal && vRing.Legal,
                    $"atk={ringed.CurrentAtk} other={vOther.Legal} direct={vDirect.Legal} ring={vRing.Legal}");
            }

            // Attack rules
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var elf = PlaceMonster(e, me, 21417692, 0); // Dark Elf
                var lp = me.LifePoints; var oppLp = opp.LifePoints;
                Attack(e, me, elf, null);
                Check("Dark Elf: pays 1000 LP to attack", me.LifePoints == lp - 1000 && opp.LifePoints == oppLp - 2000,
                    $"me {me.LifePoints} opp {opp.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var elf = PlaceMonster(e, me, 21417692, 0);
                me.LifePoints = 900;
                Check("Dark Elf: cannot attack with less than 1000 LP", !e.CanAttack(me, elf));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var bug = PlaceMonster(e, me, 7019529, 0); // Insect Soldiers of the Sky 1000
                var harpie = PlaceMonster(e, opp, HarpieLady, 0); // WIND 1300
                var oppLp = opp.LifePoints;
                Attack(e, me, bug, harpie);
                Check("Insect Soldiers: +1000 ATK only while attacking a WIND monster",
                    !OnField(opp, harpie) && opp.LifePoints == oppLp - 700 && bug.CurrentAtk == 1000,
                    $"oppLP {opp.LifePoints} atk {bug.CurrentAtk}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var gumo = PlaceMonster(e, me, 94773007, 0); // Jirai Gumo
                var lp = me.LifePoints;
                e.Rng.QueueCoin(false);
                Attack(e, me, gumo, null);
                Check("Jirai Gumo: wrong call → lose half your LP", me.LifePoints == lp - (lp + 1) / 2,
                    $"LP {lp}→{me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var gumo = PlaceMonster(e, me, 94773007, 0);
                var lp = me.LifePoints;
                e.Rng.QueueCoin(true);
                Attack(e, me, gumo, null);
                Check("Jirai Gumo: right call → no LP loss", me.LifePoints == lp);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var atk = PlaceMonster(e, me, Mechanicalchaser, 0);
                var lizard = PlaceMonster(e, opp, 55875323, 0); // Electric Lizard
                var turn = e.TurnNumber;
                Attack(e, me, atk, lizard);
                Check("Electric Lizard: a non-Zombie attacker cannot attack next turn",
                    atk.CannotAttackThroughTurn == turn + 2);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var zombie = PlaceMonster(e, me, SkullServant, 0);
                var lizard = PlaceMonster(e, opp, 55875323, 0);
                lizard.Position = BattlePosition.Defense;
                Attack(e, me, zombie, lizard);
                Check("Electric Lizard: a Zombie attacker is not locked", zombie.CannotAttackThroughTurn < e.TurnNumber);
            }

            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var halved = PlaceMonster(e, me, Mechanicalchaser, 0); // 1850
                halved.LingeringAtkModifier = -925; // e.g. Adhesion Trap Hole
                var wall = PlaceMonster(e, opp, Celtic, 0); // 1400
                var myLp = me.LifePoints;
                Attack(e, me, halved, wall);
                Check("Battle uses lingering ATK changes (halved 925 loses to 1400)",
                    !OnField(me, halved) && OnField(opp, wall) && me.LifePoints == myLp - 475,
                    $"LP {myLp}→{me.LifePoints}");
            }

            // Spells
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var elf = PlaceMonster(e, me, MysticalElf, 0); // 800/2000
                var card = Hand(e, me, 52097679); // Shield & Sword
                var ok = Activate(e, me, card, true);
                var swapped = elf.CurrentAtk == 2000 && elf.CurrentDef == 800;
                e.TryEndTurnSafe(me);
                Check("Shield & Sword: original ATK/DEF switched until the end of the turn",
                    ok && swapped && elf.CurrentAtk == 800 && elf.CurrentDef == 2000,
                    $"swapped={swapped} after={elf.CurrentAtk}/{elf.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear(); me.Graveyard.Clear();
                Hand(e, me, Celtic); Hand(e, me, SkullServant); Hand(e, me, PetitAngel); Hand(e, me, KoumoriDragon);
                var spell = Hand(e, me, PotOfGreed);
                var coffin = Hand(e, me, 41142615); // The Cheerful Coffin
                var ok = Activate(e, me, coffin, true);
                var monstersLeft = me.Hand.Count(c => c.Def.IsMonster);
                Check("The Cheerful Coffin: discards up to 3 monsters (not Spells)",
                    ok && monstersLeft == 1 && me.Hand.Contains(spell) &&
                    me.Graveyard.Count(c => c.Def.IsMonster) == 3,
                    $"left={monstersLeft} gy={me.Graveyard.Count}");
            }

            // ─────── Legacy duplicate targeted clauses still share one target ───────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var breaker = PlaceMonster(e, me, 71413901, 0); // Breaker the Magical Warrior
                breaker.Counters = 1;
                var lone = PlaceSet(e, opp, PotOfGreed, 0);
                var prog = CompiledEffectCache.GetOrCompile(breaker.Def);
                var ok = TextEffectRuntime.CanActivate(e, me, breaker, false, prog, out var why) &&
                         TextEffectRuntime.TryResolveActivation(e, me, breaker, false, prog, true);
                Resolve(e);
                Check("Breaker: one Spell/Trap on the field is enough (no multi-target demand)",
                    ok && !OnField(opp, lone), $"ok={ok} why={why}");
            }

            // ─────────────────────── The Unhappy Maiden ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var atk = PlaceMonster(e, me, Celtic, 0);
                var maiden = PlaceMonster(e, opp, UnhappyMaiden, 0);
                Attack(e, me, atk, maiden);
                Check("The Unhappy Maiden: destroyed by battle → the Battle Phase ends",
                    opp.Graveyard.Contains(maiden) && e.Phase == DuelPhase.Main2, $"phase={e.Phase}");
            }

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }

        // ─────────────────────────── helpers ───────────────────────────

        static DuelEngine Fresh(CardDatabase db)
        {
            var engine = new DuelEngine();
            engine.StartDuel(db, CardDatabase.LoadDeck("lab_rules_player.json"),
                CardDatabase.LoadDeck("lab_rules_ai.json"), cinematicOpening: false);
            return engine;
        }

        /// <summary>Advance to <paramref name="who"/>'s Main Phase 1.</summary>
        static void MainFor(DuelEngine e, DuelistState who)
        {
            for (var i = 0; i < 6 && !(e.TurnPlayer == who && e.Phase == DuelPhase.Main1); i++)
            {
                Resolve(e);
                e.TryEndTurnSafe(e.TurnPlayer);
            }
        }

        /// <summary>Advance to <paramref name="who"/>'s Battle Phase (not the first turn).</summary>
        static void BattleFor(DuelEngine e, DuelistState who)
        {
            for (var i = 0; i < 8 && e.Phase != DuelPhase.Battle; i++)
            {
                MainFor(e, who);
                Resolve(e);
                if (!e.TryEnterBattlePhase(who))
                    e.TryEndTurnSafe(e.TurnPlayer);
            }
        }

        static void Attack(DuelEngine e, DuelistState who, CardInstance attacker, CardInstance target)
        {
            attacker.SummonedThisTurn = false;
            attacker.ClearAttackFlags();
            e.NotifyPublic();
            e.TryAttack(who, attacker, target);
            for (var i = 0; i < 10; i++)
            {
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                else if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else break;
            }
        }

        /// <summary>Activate through the text runtime with auto-picked targets (AI path).</summary>
        static bool Activate(DuelEngine e, DuelistState who, CardInstance card, bool fromHand)
        {
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!TextEffectRuntime.CanActivate(e, who, card, fromHand, prog, out _)) return false;
            var ok = TextEffectRuntime.TryResolveActivation(e, who, card, fromHand, prog, true);
            Resolve(e);
            e.NotifyPublic();
            return ok;
        }

        /// <summary>Finish any pending pick / response with the first legal choice.</summary>
        static void Resolve(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var t = e.PendingActivation?.LegalTargets?.FirstOrDefault();
                    if (t == null || !e.TrySelectEffectTarget(t)) e.CancelEffectTargeting();
                }
                else if (e.IsAwaitingResponse) e.PassResponse();
                else break;
            }
        }

        static void ClearField(DuelistState who)
        {
            for (var i = 0; i < who.MonsterZones.Length; i++) who.MonsterZones[i].Occupant = null;
            for (var i = 0; i < who.SpellTrapZones.Length; i++) who.SpellTrapZones[i].Occupant = null;
            if (who.FieldSpellZone != null) who.FieldSpellZone.Occupant = null;
        }

        static CardInstance PlaceMonster(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.Position = BattlePosition.Attack;
            c.SetThisTurn = false;
            c.SummonedThisTurn = false;
            who.MonsterZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceSet(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSpellTrap(engine, who, id, zone);
            c.FaceUp = false;
            return c;
        }

        static CardInstance PlaceField(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            if (who.FieldSpellZone != null) who.FieldSpellZone.Occupant = c;
            return c;
        }

        static CardInstance Hand(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Hand.Add(c);
            return c;
        }

        static CardInstance Gy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static bool OnField(DuelistState who, CardInstance card)
        {
            foreach (var z in who.MonsterZones) if (z.Occupant == card) return true;
            foreach (var z in who.SpellTrapZones) if (z.Occupant == card) return true;
            return who.FieldSpellZone?.Occupant == card;
        }
    }
}
