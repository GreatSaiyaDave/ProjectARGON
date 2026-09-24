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
        // PSV tranche 1 (+ SRL/MRD leftovers)
        const int Ameba = 95174353;
        const int Griggle = 95744531;
        const int BackupSoldier = 36280194;
        const int BombardmentBeetle = 57409948;
        const int BubonicVermin = 6104968;
        const int BusterBlader = 78193831;
        const int Ceasefire = 36468556;
        const int DarkZebra = 59784896;
        const int DarknessApproaches = 80168720;
        const int FinalDestiny = 18591904;
        const int DrillBug = 88733579;
        const int ElegantEgotist = 90219263;
        const int EnchantedJavelin = 96355986;
        const int FairyMeteorCrush = 97687912;
        const int Gearfried = 423705;
        const int GiftOfTheMysticalElf = 98299011;
        const int HirosShadowScout = 81863068;
        const int InfiniteDismissal = 54109233;
        const int InvitationToADarkSleep = 52675689;
        const int LimiterRemoval = 23171610;
        const int MadSwordBeast = 79870141;
        const int MinorGoblinOfficial = 1918087;
        const int MonsterRecovery = 93108433;
        const int NoblemanOfExtermination = 17449108;
        const int PrematureBurial = 70828912;
        const int RainOfMercy = 66719324;
        const int ShadowOfEyes = 58621589;
        const int SolemnWishes = 35346968;
        const int SolomonsLawbook = 23471572;
        const int FiendMegacyber = 66362965;
        const int TimeSeal = 35316708;
        const int ParasiteParacide = 27911549;
        const int HarpieLadySisters = 12206212;
        const int ManEaterBug = 54652250;
        const int KarbonalaWarrior = 54541900;
        const int LegendarySword = 61854111;
        const int Sangan = 26202165;
        const int MonsterReborn = 83764719;
        const int CallOfTheHaunted = 97077563;
        const int SpellbindingCircle = 18807108;

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
                // AR field aura reads FieldAtkDelta/FieldDefDelta: the Field Spell's share only.
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var fish = PlaceMonster(e, me, GreatWhite, 0);
                var machine = PlaceMonster(e, opp, Mechanicalchaser, 0);
                PlaceMonster(e, me, CommandKnight, 1);
                var warrior = PlaceMonster(e, me, Celtic, 2);
                PlaceField(e, me, Umi);
                e.NotifyPublic();
                Check("Field delta: Umi boon on Fish is +200/+200",
                    fish.FieldAtkDelta == 200 && fish.FieldDefDelta == 200,
                    $"{fish.FieldAtkDelta}/{fish.FieldDefDelta}");
                Check("Field delta: Umi bane on the opponent's Machine is −200/−200",
                    machine.FieldAtkDelta == -200 && machine.FieldDefDelta == -200,
                    $"{machine.FieldAtkDelta}/{machine.FieldDefDelta}");
                Check("Field delta: Command Knight's Warrior aura is not the field's",
                    warrior.AtkModifier > 0 && warrior.FieldAtkDelta == 0,
                    $"mod={warrior.AtkModifier} field={warrior.FieldAtkDelta}");
                me.FieldSpellZone.Occupant = null;
                e.NotifyPublic();
                Check("Field delta: clears when the Field Spell leaves",
                    fish.FieldAtkDelta == 0 && machine.FieldAtkDelta == 0,
                    $"{fish.FieldAtkDelta}/{machine.FieldAtkDelta}");
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

            // ═════════════════════ SRL tranche 1 (compiler v53) ═════════════════════
            {
                var ids = new[] { 19384334, 45778932, 56594520, 81777047, 81380218, 70046172, 16430187, 596051,
                    93013676, 96890582, 23401839, 57617178, 42703248, 23289281, 21340051, 82003859, 16762927,
                    22046459, 18807108, 38552107, 45986603, 22567609, 95178994, 17375316, 42829885 };
                var bad = ids.Where(id =>
                {
                    var d = db.Get(id);
                    var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                    return pr == null || !pr.FullyCompiled;
                }).Select(id => db.Get(id)?.name ?? id.ToString()).ToList();
                Check($"Compile: SRL tranche 1 ({ids.Length} effect cards) FullyCompiled", bad.Count == 0,
                    string.Join(", ", bad));
                var rituals = new[] { 4849037, 30243636, 91782219 }
                    .Where(id => CardEffectStatus.Classify(db.Get(id)) != CardEffectStatusKind.Structural)
                    .Select(id => db.Get(id)?.name).ToList();
                Check("Non-effect Ritual Monsters are structural (summoned by their Ritual Spell)",
                    rituals.Count == 0, string.Join(", ", rituals));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var earth = PlaceMonster(e, me, Celtic, 0); // EARTH 1400/1200
                var water = PlaceMonster(e, opp, GreatWhite, 0);
                PlaceField(e, me, 56594520); // Gaia Power
                e.NotifyPublic();
                Check("Gaia Power: EARTH +500 ATK / −400 DEF, others unchanged",
                    earth.CurrentAtk == 1900 && earth.CurrentDef == 800 && water.CurrentAtk == 1600,
                    $"{earth.CurrentAtk}/{earth.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var def = PlaceMonster(e, me, Celtic, 0); def.Position = BattlePosition.Defense;
                var atk = PlaceMonster(e, opp, Celtic, 0);
                PlaceField(e, me, 81380218); // Chorus of Sanctuary
                e.NotifyPublic();
                Check("Chorus of Sanctuary: Defense Position monsters +500 DEF only",
                    def.CurrentDef == 1700 && atk.CurrentDef == 1200);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var mine = PlaceMonster(e, me, Celtic, 0);
                var rush = Hand(e, me, 70046172); // Rush Recklessly
                var ok = Activate(e, me, rush, true);
                var boosted = mine.CurrentAtk;
                e.TryEndTurnSafe(me);
                Check("Rush Recklessly: +700 ATK until the end of this turn",
                    ok && boosted == 2100 && mine.CurrentAtk == 1400, $"{boosted}→{mine.CurrentAtk}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var theirs = PlaceMonster(e, opp, Celtic, 0);
                var fang = PlaceSet(e, me, 596051, 0); // Snake Fang
                var ok = Activate(e, me, fang, false);
                Check("Snake Fang: a monster loses 500 DEF this turn", ok && theirs.CurrentDef == 700,
                    theirs.CurrentDef.ToString());
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var maha = PlaceMonster(e, me, 93013676, 0); // Maha Vailo 1550
                var horn = PlaceSpellTrap(e, me, 38552107, 0); // Horn of Light
                horn.EquippedTo = maha; maha.Equips.Add(horn);
                e.NotifyPublic();
                Check("Maha Vailo: +500 ATK per Equip Card (Horn of Light also +800 DEF)",
                    maha.CurrentAtk == 2050 && maha.CurrentDef == 2200, $"{maha.CurrentAtk}/{maha.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var flash = PlaceMonster(e, me, 96890582, 0); // Flash Assailant 2000/2000
                Hand(e, me, Celtic); Hand(e, me, PotOfGreed);
                e.NotifyPublic();
                Check("Flash Assailant: −400/−400 per card in hand",
                    flash.CurrentAtk == 1200 && flash.CurrentDef == 1200, $"{flash.CurrentAtk}/{flash.CurrentDef}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                me.Deck.Clear(); me.Deck.Add(Celtic); me.Deck.Add(91782219); me.Deck.Add(76806714);
                var senju = Hand(e, me, 23401839);
                var ok = e.TryNormalSummon(me, senju, false);
                Resolve(e);
                Check("Senju: Normal Summon adds a Ritual Monster from the Deck",
                    ok && me.Hand.Exists(c => c.CardId == 91782219) && !me.Deck.Contains(91782219));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                me.Deck.Clear(); me.Deck.Add(Celtic); me.Deck.Add(91782219); me.Deck.Add(76806714);
                var bird = Hand(e, me, 57617178);
                var ok = e.TryNormalSummon(me, bird, false);
                Resolve(e);
                Check("Sonic Bird: Normal Summon adds a Ritual Spell from the Deck",
                    ok && me.Hand.Exists(c => c.CardId == 76806714) && !me.Deck.Contains(76806714));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); me.Hand.Clear(); opp.Hand.Clear();
                var mySet = PlaceSet(e, me, TrapHole, 1);
                var theirSet = PlaceSet(e, opp, Mst, 0);
                var field = PlaceField(e, opp, 56594520);
                var tr = Hand(e, me, 42703248); // Giant Trunade
                var ok = Activate(e, me, tr, true);
                Check("Giant Trunade: every Spell/Trap returns to its owner's hand",
                    ok && me.Hand.Contains(mySet) && opp.Hand.Contains(theirSet) && opp.Hand.Contains(field) &&
                    me.Graveyard.Contains(tr));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var karate = PlaceMonster(e, me, 23289281, 0); // Karate Man 1000
                var prog = CompiledEffectCache.GetOrCompile(karate.Def);
                var ok = TextEffectRuntime.CanActivate(e, me, karate, false, prog, out _) &&
                         TextEffectRuntime.TryResolveActivation(e, me, karate, false, prog, true);
                Resolve(e);
                var doubled = karate.CurrentAtk;
                e.TryEndTurnSafe(me);
                Check("Karate Man: doubles its original ATK, then is destroyed in the End Phase",
                    ok && doubled == 2000 && me.Graveyard.Contains(karate), $"atk={doubled}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var boar = Hand(e, me, 21340051);
                e.TryNormalSummon(me, boar, false);
                Resolve(e);
                Check("Boar Soldier: destroyed when Normal Summoned", me.Graveyard.Contains(boar));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var boar = PlaceMonster(e, me, 21340051, 0);
                e.NotifyPublic();
                var alone = boar.CurrentAtk;
                PlaceMonster(e, opp, Celtic, 0);
                e.NotifyPublic();
                Check("Boar Soldier: −1000 ATK while the opponent controls a monster",
                    alone == 2000 && boar.CurrentAtk == 1000);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                PlaceSpellTrap(e, opp, 82003859, 0); // Toll
                var atk = PlaceMonster(e, me, Celtic, 0);
                var lp = me.LifePoints;
                Attack(e, me, atk, null);
                Check("Toll: attacking costs 500 LP", me.LifePoints == lp - 500, $"LP {me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                PlaceSpellTrap(e, opp, 16762927, 0); // Gravekeeper's Servant (opponent's card)
                var atk = PlaceMonster(e, me, Celtic, 0);
                var deck = me.Deck.Count; var gy = me.Graveyard.Count;
                Attack(e, me, atk, null);
                Check("Gravekeeper's Servant: attacker sends the top card of their Deck to the GY",
                    me.Deck.Count == deck - 1 && me.Graveyard.Count == gy + 1);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var host = PlaceMonster(e, me, Celtic, 0); // 1400
                var mega = PlaceSpellTrap(e, me, 22046459, 0);
                mega.EquippedTo = host; host.Equips.Add(mega);
                me.LifePoints = 3000; opp.LifePoints = 8000; e.NotifyPublic();
                var low = host.CurrentAtk;
                me.LifePoints = 8000; opp.LifePoints = 3000; e.NotifyPublic();
                Check("Megamorph: double original ATK when behind on LP, half when ahead",
                    low == 2800 && host.CurrentAtk == 700, $"{low}/{host.CurrentAtk}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var target = PlaceMonster(e, opp, Mechanicalchaser, 0);
                var circle = PlaceSet(e, me, 18807108, 0); // Spellbinding Circle
                var ok = Activate(e, me, circle, false);
                var bound = OnField(me, circle) && TextEffectRuntime.AttackForbiddenByEffect(e, target) &&
                            TextEffectRuntime.PositionChangeForbiddenByEffect(target);
                e.DestroyMonsterPublic(opp, target);
                Check("Spellbinding Circle: target cannot attack or change position; leaves with it",
                    ok && bound && me.Graveyard.Contains(circle), $"ok={ok} bound={bound}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var snatch = PlaceSpellTrap(e, me, 45986603, 0); // Snatch Steal
                var lp = opp.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(e, opp, EffectTiming.StandbyPhase);
                var afterOppStandby = opp.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(e, me, EffectTiming.StandbyPhase);
                Check("Snatch Steal: the opponent gains 1000 LP in each of THEIR Standby Phases",
                    afterOppStandby == lp + 1000 && opp.LifePoints == lp + 1000,
                    $"{lp}→{afterOppStandby}→{opp.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); opp.Hand.Clear();
                var theirs = Hand(e, opp, PotOfGreed); Hand(e, opp, SkullServant);
                var conf = Hand(e, me, 17375316); // Confiscation
                var lp = me.LifePoints;
                var ok = Activate(e, me, conf, true);
                Check("Confiscation: pay 1000, discard 1 card from the opponent's hand",
                    ok && me.LifePoints == lp - 1000 && opp.Hand.Count == 1 && opp.Graveyard.Contains(theirs),
                    $"ok={ok} lp={me.LifePoints} oppHand={opp.Hand.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); opp.Hand.Clear();
                var theirs = Hand(e, opp, PotOfGreed);
                var deck = opp.Deck.Count;
                var sentry = Hand(e, me, 42829885); // The Forceful Sentry
                var ok = Activate(e, me, sentry, true);
                Check("The Forceful Sentry: a card from the opponent's hand is shuffled into their Deck",
                    ok && opp.Hand.Count == 0 && opp.Deck.Count == deck + 1 && opp.Deck.Contains(PotOfGreed));
            }

            // ═══════════════ PSV tranche 1 (+ SRL/MRD leftovers), compiler v54 ═══════════════
            {
                var ids = new[]
                {
                    Ameba, Griggle, BackupSoldier, BombardmentBeetle, BubonicVermin, BusterBlader, Ceasefire,
                    DarkZebra, DarknessApproaches, FinalDestiny, DrillBug, ElegantEgotist, EnchantedJavelin,
                    FairyMeteorCrush, Gearfried, GiftOfTheMysticalElf, HirosShadowScout, InfiniteDismissal,
                    InvitationToADarkSleep, LimiterRemoval, MadSwordBeast, MinorGoblinOfficial, MonsterRecovery,
                    NoblemanOfExtermination, PrematureBurial, RainOfMercy, ShadowOfEyes, SolemnWishes,
                    SolomonsLawbook, FiendMegacyber, TimeSeal
                };
                var bad = ids.Where(id =>
                {
                    var d = db.Get(id);
                    var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                    return pr == null || !pr.FullyCompiled || pr.ClauseList.Count == 0;
                }).Select(id => db.Get(id)?.name ?? id.ToString()).ToList();
                Check($"Compile: all {ids.Length} PSV-tranche cards FullyCompiled from official text",
                    bad.Count == 0, string.Join(", ", bad));
            }

            // ─────────────────────── Ameba / Griggle ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var ameba = PlaceMonster(e, me, Ameba, 0);
                var myLp = me.LifePoints; var oppLp = opp.LifePoints;
                e.TryTakeControl(opp, ameba);
                var afterSteal = opp.LifePoints;
                e.TryTakeControl(me, ameba); // control returns: once while face-up
                Check("Ameba: the player who takes control takes 2000; the return does not trigger again",
                    afterSteal == oppLp - 2000 && opp.LifePoints == afterSteal && me.LifePoints == myLp,
                    $"opp {oppLp}→{afterSteal}→{opp.LifePoints} me={me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var ameba = PlaceMonster(e, me, Ameba, 0);
                ameba.FaceUp = false; ameba.Position = BattlePosition.Defense;
                var oppLp = opp.LifePoints;
                e.TryTakeControl(opp, ameba);
                Check("Ameba: a face-down Ameba changing control does nothing", opp.LifePoints == oppLp);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var griggle = PlaceMonster(e, me, Griggle, 0);
                var myLp = me.LifePoints; var oppLp = opp.LifePoints;
                e.TryTakeControl(opp, griggle);
                var afterSteal = me.LifePoints;
                e.TryTakeControl(me, griggle);
                Check("Griggle: the player who lost control gains 3000, once while face-up",
                    afterSteal == myLp + 3000 && me.LifePoints == afterSteal && opp.LifePoints == oppLp,
                    $"me {myLp}→{afterSteal}→{me.LifePoints} opp={opp.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var ameba = PlaceMonster(e, me, Ameba, 0);
                e.TryTakeControl(opp, ameba);
                e.TryTakeControl(me, ameba);
                e.DestroyMonsterPublic(me, ameba);
                me.Graveyard.Remove(ameba);
                e.SpecialSummonToField(me, ameba, BattlePosition.Attack, true);
                var oppLp = opp.LifePoints;
                e.TryTakeControl(opp, ameba);
                Check("Ameba: a new trip to the field resets the once-while-face-up limit",
                    opp.LifePoints == oppLp - 2000, $"opp {oppLp}→{opp.LifePoints}");
            }

            // ─────────────────────── Backup Soldier ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); me.Graveyard.Clear();
                var bug = Gy(e, me, ManEaterBug);           // Effect Monster: not legal
                var bewd = Gy(e, me, 89631139);             // 3000 ATK: not legal
                var celtic = Gy(e, me, Celtic);
                var karbonala = Gy(e, me, KarbonalaWarrior); // effectless Fusion, 1500
                var skull = Gy(e, me, SkullServant);
                var extra = me.ExtraDeck.Count;
                var trap = PlaceSet(e, me, BackupSoldier, 0);
                var ok = Activate(e, me, trap, false);
                Check("Backup Soldier: up to 3 non-Effect monsters (≤1500 ATK) return; a Fusion goes to the Extra Deck",
                    ok && me.Hand.Contains(celtic) && me.Hand.Contains(skull) &&
                    me.ExtraDeck.Count == extra + 1 && !me.Graveyard.Contains(karbonala) &&
                    me.Graveyard.Contains(bug) && me.Graveyard.Contains(bewd),
                    $"ok={ok} hand={me.Hand.Count} extra={me.ExtraDeck.Count - extra}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Graveyard.Clear();
                for (var i = 0; i < 4; i++) Gy(e, me, Celtic);
                var trap = PlaceSet(e, me, BackupSoldier, 0);
                var legal = TextEffectRuntime.CanActivate(e, me, trap, false,
                    CompiledEffectCache.GetOrCompile(trap.Def), out _);
                Check("Backup Soldier: needs 5 or more monsters in your GY", !legal);
            }

            // ─────────────────── Bombardment Beetle / Bubonic Vermin ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var mine = PlaceMonster(e, me, Celtic, 1);
                var bug = PlaceMonster(e, opp, ManEaterBug, 0);
                bug.FaceUp = false; bug.Position = BattlePosition.Defense;
                var beetle = PlaceMonster(e, me, BombardmentBeetle, 0);
                beetle.FaceUp = false; beetle.Position = BattlePosition.Defense;
                var flipped = e.TryFlipSummon(me, beetle);
                Resolve(e);
                Check("Bombardment Beetle: a face-down Effect Monster is destroyed (its FLIP does not activate)",
                    flipped && opp.Graveyard.Contains(bug) && OnField(me, mine), $"flipped={flipped}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var vanilla = PlaceMonster(e, opp, Celtic, 0);
                vanilla.FaceUp = false; vanilla.Position = BattlePosition.Defense;
                var beetle = PlaceMonster(e, me, BombardmentBeetle, 0);
                beetle.FaceUp = false; beetle.Position = BattlePosition.Defense;
                e.TryFlipSummon(me, beetle);
                Resolve(e);
                Check("Bombardment Beetle: a non-Effect monster returns face-down",
                    OnField(opp, vanilla) && !vanilla.FaceUp && vanilla.Position == BattlePosition.Defense);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                me.Deck.Insert(3, BubonicVermin);
                var vermin = PlaceMonster(e, me, BubonicVermin, 0);
                vermin.FaceUp = false; vermin.Position = BattlePosition.Defense;
                var deck = me.Deck.Count;
                e.TryFlipSummon(me, vermin);
                Resolve(e);
                var copy = me.MonstersOnField().FirstOrDefault(m => m != vermin && m.CardId == BubonicVermin);
                Check("Bubonic Vermin: FLIP Special Summons 1 copy from the Deck in face-down Defense",
                    copy != null && !copy.FaceUp && copy.Position == BattlePosition.Defense &&
                    me.Deck.Count == deck - 1, $"copy={copy != null} deck={deck}→{me.Deck.Count}");
            }

            // ─────────────────────── Buster Blader ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp); opp.Graveyard.Clear();
                var blader = PlaceMonster(e, me, BusterBlader, 0);
                PlaceMonster(e, opp, KoumoriDragon, 0);
                var hidden = PlaceMonster(e, opp, KoumoriDragon, 1);
                hidden.FaceUp = false; hidden.Position = BattlePosition.Defense;
                Gy(e, opp, 89631139);
                Gy(e, me, 89631139); // my own GY does not count
                e.NotifyPublic();
                Check("Buster Blader: +500 per face-up Dragon the opponent controls and per Dragon in their GY",
                    blader.CurrentAtk == 2600 + 1000, $"atk={blader.CurrentAtk}");
            }

            // ─────────────────────── Ceasefire ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var bug = PlaceMonster(e, me, ManEaterBug, 0);
                bug.FaceUp = false; bug.Position = BattlePosition.Defense;
                var celtic = PlaceMonster(e, opp, Celtic, 0);
                celtic.FaceUp = false; celtic.Position = BattlePosition.Defense;
                PlaceMonster(e, opp, Sangan, 1);
                var lp = opp.LifePoints;
                var cease = PlaceSet(e, me, Ceasefire, 0);
                var ok = Activate(e, me, cease, false);
                Check("Ceasefire: face-down Defense monsters flip face-up (no FLIP); 500 per Effect Monster",
                    ok && bug.FaceUp && bug.Position == BattlePosition.Defense && celtic.FaceUp &&
                    OnField(opp, celtic) && opp.MonsterCount == 2 && opp.LifePoints == lp - 1000,
                    $"ok={ok} lp={lp}→{opp.LifePoints} oppMonsters={opp.MonsterCount}");
            }

            // ─────────────────────── Dark Zebra ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                var zebra = PlaceMonster(e, me, DarkZebra, 0);
                TextEffectRuntime.FirePhaseTriggers(e, me, EffectTiming.StandbyPhase);
                var alone = zebra.Position == BattlePosition.Defense && zebra.ChangedPositionThisTurn;
                zebra.Position = BattlePosition.Attack;
                PlaceMonster(e, me, Celtic, 1);
                TextEffectRuntime.FirePhaseTriggers(e, me, EffectTiming.StandbyPhase);
                Check("Dark Zebra: the only monster you control → Defense (locked this turn); not with company",
                    alone && zebra.Position == BattlePosition.Attack);
            }

            // ─────────────────── Darkness Approaches / Final Destiny ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var target = PlaceMonster(e, opp, KoumoriDragon, 0);
                var da = Hand(e, me, DarknessApproaches);
                var a = Hand(e, me, Celtic); var b = Hand(e, me, SkullServant); var keep = Hand(e, me, PotOfGreed);
                var prog = CompiledEffectCache.GetOrCompile(da.Def);
                var ok = TextEffectRuntime.CanActivate(e, me, da, true, prog, out var why) &&
                         TextEffectRuntime.TryResolveActivation(e, me, da, true, prog, false);
                var askedTwo = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingMultiDiscard &&
                               e.PendingActivation.MultiDiscardRemaining == 2 &&
                               !e.PendingActivation.LegalTargets.Contains(da);
                e.TrySelectEffectTarget(a);
                var stillHand = me.Hand.Contains(a); // discards happen once all are chosen
                e.TrySelectEffectTarget(b);
                var askedTarget = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(target);
                if (askedTarget) e.TrySelectEffectTarget(target);
                Check("Darkness Approaches: you choose 2 discards, then a face-up monster turns face-down",
                    ok && askedTwo && stillHand && askedTarget && me.Graveyard.Contains(a) &&
                    me.Graveyard.Contains(b) && me.Hand.Contains(keep) && !target.FaceUp &&
                    target.Position == BattlePosition.Defense,
                    $"ok={ok} why={why} askedTwo={askedTwo} askedTarget={askedTarget}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, opp, KoumoriDragon, 0);
                var da = Hand(e, me, DarknessApproaches);
                var a = Hand(e, me, Celtic); Hand(e, me, SkullServant);
                var prog = CompiledEffectCache.GetOrCompile(da.Def);
                TextEffectRuntime.TryResolveActivation(e, me, da, true, prog, false);
                e.TrySelectEffectTarget(a);
                e.CancelEffectTargeting();
                Check("Darkness Approaches: Cancel during the discard choice discards nothing",
                    !e.IsAwaitingEffectTarget && me.Hand.Count == 3 && me.Hand.Contains(da) &&
                    me.Hand.Contains(a) && !me.Graveyard.Contains(a), $"hand={me.Hand.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, opp, KoumoriDragon, 0);
                var da = Hand(e, me, DarknessApproaches);
                Hand(e, me, Celtic);
                var legal = TextEffectRuntime.CanActivate(e, me, da, true,
                    CompiledEffectCache.GetOrCompile(da.Def), out _);
                Check("Darkness Approaches: 1 other card in hand is not enough", !legal);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var mine = PlaceMonster(e, me, Celtic, 0);
                var theirs = PlaceMonster(e, opp, KoumoriDragon, 0);
                var set = PlaceSet(e, opp, TrapHole, 0);
                var fd = Hand(e, me, FinalDestiny);
                for (var i = 0; i < 5; i++) Hand(e, me, SkullServant);
                var ok = Activate(e, me, fd, true);
                Check("Final Destiny: discard 5, destroy all cards on the field",
                    ok && me.Hand.Count == 0 && !OnField(me, mine) && !OnField(opp, theirs) &&
                    !OnField(opp, set) && me.Graveyard.Contains(fd),
                    $"ok={ok} hand={me.Hand.Count}");
            }

            // ─────────────────────── Drill Bug ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                me.Deck.Insert(me.Deck.Count / 2, ParasiteParacide);
                var drill = PlaceMonster(e, me, DrillBug, 0);
                Attack(e, me, drill, null);
                Check("Drill Bug: battle damage → \"Parasite Paracide\" is placed on top of the Deck",
                    me.Deck.Count > 0 && me.Deck[0] == ParasiteParacide, $"top={me.Deck.FirstOrDefault()}");
            }

            // ─────────────────────── Elegant Egotist ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, me, HarpieLady, 0);
                me.Deck.Insert(4, HarpieLadySisters);
                var deck = me.Deck.Count;
                var ego = Hand(e, me, ElegantEgotist);
                var ok = Activate(e, me, ego, true);
                var sisters = me.MonstersOnField().FirstOrDefault(m => m.CardId == HarpieLadySisters);
                Check("Elegant Egotist: with \"Harpie Lady\" face-up, Special Summon Harpie Lady Sisters from the Deck",
                    ok && sisters != null && sisters.FaceUp && me.Deck.Count == deck - 1,
                    $"ok={ok} deck={deck}→{me.Deck.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, me, Celtic, 0);
                me.Deck.Insert(0, HarpieLadySisters);
                var ego = Hand(e, me, ElegantEgotist);
                var legal = TextEffectRuntime.CanActivate(e, me, ego, true,
                    CompiledEffectCache.GetOrCompile(ego.Def), out _);
                Check("Elegant Egotist: no face-up \"Harpie Lady\" → cannot activate", !legal);
            }

            // ─────────────────────── Enchanted Javelin ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, opp);
                ClearField(me); ClearField(opp);
                var wall = PlaceMonster(e, me, MysticalElf, 0);
                wall.Position = BattlePosition.Defense;
                var jav = PlaceSet(e, me, EnchantedJavelin, 0);
                var attacker = PlaceMonster(e, opp, Mechanicalchaser, 0);
                attacker.ClearAttackFlags(); attacker.SummonedThisTurn = false;
                var lp = me.LifePoints;
                e.TryAttack(opp, attacker, wall);
                var ok = e.IsAwaitingResponse && e.TryActivateSpellTrap(me, jav, fromHand: false);
                Check("Enchanted Javelin: gain LP equal to the attacking monster's ATK",
                    ok && me.LifePoints == lp + 1850, $"ok={ok} lp={lp}→{me.LifePoints}");
            }

            // ─────────────────── Fairy Meteor Crush / Mad Sword Beast ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var host = PlaceMonster(e, me, Celtic, 0); // 1400
                var crush = PlaceSpellTrap(e, me, FairyMeteorCrush, 0);
                crush.EquippedTo = host; host.Equips.Add(crush);
                var wall = PlaceMonster(e, opp, SkullServant, 0); // DEF 200
                wall.Position = BattlePosition.Defense;
                var lp = opp.LifePoints;
                Attack(e, me, host, wall);
                Check("Fairy Meteor Crush: the equipped monster inflicts piercing damage",
                    opp.LifePoints == lp - 1200, $"lp={lp}→{opp.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                BattleFor(e, me);
                ClearField(me); ClearField(opp);
                var beast = PlaceMonster(e, me, MadSwordBeast, 0); // 1400
                var wall = PlaceMonster(e, opp, SkullServant, 0);
                wall.Position = BattlePosition.Defense;
                var lp = opp.LifePoints;
                Attack(e, me, beast, wall);
                var pierced = opp.LifePoints == lp - 1200;
                var plain = PlaceMonster(e, me, Celtic, 1);
                var wall2 = PlaceMonster(e, opp, SkullServant, 1);
                wall2.Position = BattlePosition.Defense;
                lp = opp.LifePoints;
                Attack(e, me, plain, wall2);
                Check("Mad Sword Beast: piercing damage (a plain attacker does none)",
                    pierced && opp.LifePoints == lp, $"lp now {opp.LifePoints}");
            }

            // ─────────────────────── Gearfried the Iron Knight ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var gear = PlaceMonster(e, me, Gearfried, 0);
                var sword = Hand(e, me, LegendarySword);
                var ok = Activate(e, me, sword, true);
                Check("Gearfried: an Equip Card equipped to it is destroyed (ATK unchanged)",
                    ok && me.Graveyard.Contains(sword) && gear.Equips.Count == 0 && gear.CurrentAtk == 1800,
                    $"ok={ok} atk={gear.CurrentAtk} equips={gear.Equips.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); me.Graveyard.Clear();
                var gear = Gy(e, me, Gearfried);
                var burial = Hand(e, me, PrematureBurial);
                var ok = Activate(e, me, burial, true);
                Check("Gearfried + Premature Burial: Burial is destroyed, so Gearfried is destroyed too",
                    ok && me.Graveyard.Contains(burial) && me.Graveyard.Contains(gear) && me.MonsterCount == 0,
                    $"ok={ok} monsters={me.MonsterCount}");
            }

            // ─────────────────── LP gain: Gift of the Mystical Elf / Rain of Mercy ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                PlaceMonster(e, me, Celtic, 0); PlaceMonster(e, opp, Celtic, 0); PlaceMonster(e, opp, Celtic, 1);
                var lp = me.LifePoints;
                var gift = PlaceSet(e, me, GiftOfTheMysticalElf, 0);
                var ok = Activate(e, me, gift, false);
                Check("Gift of the Mystical Elf: +300 LP per monster on the field",
                    ok && me.LifePoints == lp + 900, $"ok={ok} lp={lp}→{me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var a = me.LifePoints; var b = opp.LifePoints;
                var rain = Hand(e, me, RainOfMercy);
                var ok = Activate(e, me, rain, true);
                Check("Rain of Mercy: both players gain 1000 LP",
                    ok && me.LifePoints == a + 1000 && opp.LifePoints == b + 1000);
            }

            // ─────────────────────── Hiro's Shadow Scout ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); opp.Hand.Clear();
                opp.Deck.Insert(0, MonsterReborn); opp.Deck.Insert(0, Celtic); opp.Deck.Insert(0, PotOfGreed);
                var scout = PlaceMonster(e, me, HirosShadowScout, 0);
                scout.FaceUp = false; scout.Position = BattlePosition.Defense;
                e.TryFlipSummon(me, scout);
                Resolve(e);
                Check("Hiro's Shadow Scout: the opponent draws 3 and discards the Spells among them",
                    opp.Hand.Count == 1 && opp.Hand[0].CardId == Celtic &&
                    opp.Graveyard.Count(g => g.CardId == PotOfGreed || g.CardId == MonsterReborn) == 2,
                    $"hand={opp.Hand.Count}");
            }

            // ─────────────────────── Infinite Dismissal ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceSpellTrap(e, me, InfiniteDismissal, 0);
                var ns = Hand(e, me, SkullServant);
                e.TryNormalSummon(me, ns, false);
                Resolve(e);
                var ss = e.CreateCardInstance(SkullServant);
                e.SpecialSummonToField(me, ss, BattlePosition.Attack, true);
                var big = PlaceMonster(e, me, Celtic, 3);
                big.NormalOrFlipSummonedTurn = e.TurnNumber; // Level 4: out of range
                e.TryEndTurnSafe(me);
                Resolve(e);
                Check("Infinite Dismissal: a Normal Summoned Level ≤3 is destroyed in the End Phase; SS and Lv4 stay",
                    me.Graveyard.Contains(ns) && OnField(me, ss) && OnField(me, big),
                    $"nsGy={me.Graveyard.Contains(ns)} ss={OnField(me, ss)} big={OnField(me, big)}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, opp);
                ClearField(me); ClearField(opp); opp.Hand.Clear();
                PlaceSpellTrap(e, me, InfiniteDismissal, 0);
                var theirs = Hand(e, opp, SkullServant);
                e.TryNormalSummon(opp, theirs, false);
                Resolve(e);
                var set = Hand(e, opp, SkullServant);
                set.FaceUp = false;
                e.TryEndTurnSafe(opp);
                Resolve(e);
                Check("Infinite Dismissal: works in the opponent's End Phase too",
                    opp.Graveyard.Contains(theirs));
            }

            // ─────────────────────── Invitation to a Dark Sleep ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var target = PlaceMonster(e, opp, Celtic, 0);
                var inv = PlaceMonster(e, me, InvitationToADarkSleep, 0); // Level 5: Flip Summon it
                inv.FaceUp = false; inv.Position = BattlePosition.Defense;
                var flipped = e.TryFlipSummon(me, inv);
                Resolve(e);
                var locked = flipped && TextEffectRuntime.AttackForbiddenByEffect(e, target);
                e.DestroyMonsterPublic(me, inv);
                e.NotifyPublic();
                Check("Invitation to a Dark Sleep: the target cannot attack while Invitation is face-up",
                    locked && !TextEffectRuntime.AttackForbiddenByEffect(e, target) && target.AttackLockedBy == null,
                    $"locked={locked}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var target = PlaceMonster(e, opp, Celtic, 0);
                var inv = e.CreateCardInstance(InvitationToADarkSleep);
                e.SpecialSummonToField(me, inv, BattlePosition.Attack, true);
                Resolve(e);
                Check("Invitation to a Dark Sleep: a Special Summon does not trigger it",
                    !TextEffectRuntime.AttackForbiddenByEffect(e, target));
            }

            // ─────────────────────── Limiter Removal ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var mech = PlaceMonster(e, me, Mechanicalchaser, 0);
                var celtic = PlaceMonster(e, me, Celtic, 1);
                var lr = Hand(e, me, LimiterRemoval);
                var ok = Activate(e, me, lr, true);
                var doubled = mech.CurrentAtk;
                e.TryEndTurnSafe(me);
                Resolve(e);
                Check("Limiter Removal: your Machines' ATK doubles; they are destroyed in the End Phase",
                    ok && doubled == 3700 && me.Graveyard.Contains(mech) && OnField(me, celtic),
                    $"ok={ok} atk={doubled}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, me, Celtic, 0);
                PlaceMonster(e, opp, Mechanicalchaser, 0);
                var lr = Hand(e, me, LimiterRemoval);
                Check("Limiter Removal: needs a face-up Machine you control",
                    !TextEffectRuntime.CanActivate(e, me, lr, true, CompiledEffectCache.GetOrCompile(lr.Def), out _));
            }

            // ─────────────────────── Minor Goblin Official ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var mgo = PlaceSet(e, me, MinorGoblinOfficial, 0);
                opp.LifePoints = 3500;
                var early = TextEffectRuntime.CanActivate(e, me, mgo, false,
                    CompiledEffectCache.GetOrCompile(mgo.Def), out _);
                opp.LifePoints = 3000;
                var ok = Activate(e, me, mgo, false);
                TextEffectRuntime.FirePhaseTriggers(e, me, EffectTiming.StandbyPhase);
                var afterMine = opp.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(e, opp, EffectTiming.StandbyPhase);
                Check("Minor Goblin Official: only at ≤3000 LP; 500 damage in each of the opponent's Standby Phases",
                    !early && ok && OnField(me, mgo) && afterMine == 3000 && opp.LifePoints == 2500,
                    $"early={early} ok={ok} {afterMine}→{opp.LifePoints}");
            }

            // ─────────────────────── Monster Recovery ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var mon = PlaceMonster(e, me, Celtic, 0);
                var mr = Hand(e, me, MonsterRecovery);
                Hand(e, me, PotOfGreed); Hand(e, me, SkullServant);
                var deck = me.Deck.Count;
                var ok = Activate(e, me, mr, true);
                Check("Monster Recovery: the monster + your hand go into the Deck; draw as many as came from the hand",
                    ok && !OnField(me, mon) && me.Hand.Count == 2 && me.Deck.Count == deck + 1 &&
                    me.Graveyard.Contains(mr), $"ok={ok} hand={me.Hand.Count} deck={deck}→{me.Deck.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var borrowed = PlaceMonster(e, opp, Celtic, 0);
                e.TryTakeControl(me, borrowed);
                var mr = Hand(e, me, MonsterRecovery);
                Hand(e, me, PotOfGreed);
                Check("Monster Recovery: a monster you control but do not own is not a legal target",
                    !TextEffectRuntime.CanActivate(e, me, mr, true, CompiledEffectCache.GetOrCompile(mr.Def), out _));
            }

            // ─────────────────────── Nobleman of Extermination ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var set = PlaceSet(e, opp, TrapHole, 0);
                opp.Deck.Insert(2, TrapHole); opp.Deck.Insert(5, TrapHole);
                me.Deck.Insert(1, TrapHole);
                var oppCopies = opp.Deck.Count(id => id == TrapHole);
                var myCopies = me.Deck.Count(id => id == TrapHole);
                var oppBan = opp.Banished.Count(b => b.CardId == TrapHole);
                var myBan = me.Banished.Count(b => b.CardId == TrapHole);
                var nob = Hand(e, me, NoblemanOfExtermination);
                var ok = Activate(e, me, nob, true);
                Check("Nobleman of Extermination: a Set Trap is destroyed and banished; every copy leaves both Decks",
                    ok && opp.Banished.Contains(set) && !opp.Graveyard.Contains(set) &&
                    !opp.Deck.Contains(TrapHole) && !me.Deck.Contains(TrapHole) &&
                    opp.Banished.Count(b => b.CardId == TrapHole) == oppBan + oppCopies + 1 &&
                    me.Banished.Count(b => b.CardId == TrapHole) == myBan + myCopies,
                    $"ok={ok} oppBan={opp.Banished.Count} myBan={me.Banished.Count}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var set = PlaceSet(e, opp, PotOfGreed, 0);
                PlaceSpellTrap(e, opp, SolemnWishes, 1); // face-up: not a target
                opp.Deck.Insert(0, PotOfGreed);
                var nob = Hand(e, me, NoblemanOfExtermination);
                var ok = Activate(e, me, nob, true);
                Check("Nobleman of Extermination: a Set Spell is banished; Decks are untouched",
                    ok && opp.Banished.Contains(set) && opp.Deck.Contains(PotOfGreed), $"ok={ok}");
            }

            // ─────────────────────── Premature Burial ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); me.Graveyard.Clear();
                var bewd = Gy(e, me, 89631139);
                var burial = Hand(e, me, PrematureBurial);
                var lp = me.LifePoints;
                var ok = Activate(e, me, burial, true);
                var revived = OnField(me, bewd) && bewd.FaceUp && bewd.Position == BattlePosition.Attack &&
                              burial.EquippedTo == bewd && me.LifePoints == lp - 800;
                var mst = PlaceSpellTrap(e, opp, Mst, 0);
                var mstProg = CompiledEffectCache.GetOrCompile(mst.Def);
                TextEffectRuntime.TryResolveActivation(e, opp, mst, false, mstProg, true);
                Resolve(e);
                Check("Premature Burial: pay 800, revive in Attack Position; destroying Burial destroys the monster",
                    ok && revived && me.Graveyard.Contains(burial) && me.Graveyard.Contains(bewd),
                    $"ok={ok} revived={revived} lp={me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); me.Graveyard.Clear();
                var bewd = Gy(e, me, 89631139);
                var burial = Hand(e, me, PrematureBurial);
                Activate(e, me, burial, true);
                e.ReturnCardToHand(burial);
                e.NotifyPublic();
                Check("Premature Burial: returned to the hand (not destroyed) → the monster stays",
                    OnField(me, bewd) && me.Hand.Contains(burial) && bewd.Equips.Count == 0 &&
                    burial.EquippedTo == null);
            }

            // ─────────────────────── Shadow of Eyes ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, opp);
                ClearField(me); ClearField(opp); opp.Hand.Clear();
                var mine = PlaceMonster(e, me, Celtic, 0);
                var shadow = PlaceSet(e, me, ShadowOfEyes, 0);
                var bug = Hand(e, opp, ManEaterBug);
                e.TryNormalSummon(opp, bug, asSet: true);
                var window = e.IsAwaitingResponse && e.PendingResponse.Timing == ResponseTiming.MonsterSummoned;
                var ok = window && e.TryActivateSpellTrap(me, shadow, fromHand: false);
                Resolve(e);
                Check("Shadow of Eyes: a Set monster flips to face-up Attack; its FLIP does not activate",
                    ok && bug.FaceUp && bug.Position == BattlePosition.Attack && OnField(me, mine),
                    $"window={window} ok={ok} faceUp={bug.FaceUp}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, opp);
                ClearField(me); ClearField(opp); opp.Hand.Clear();
                var shadow = PlaceSet(e, me, ShadowOfEyes, 0);
                var celtic = Hand(e, opp, Celtic);
                e.TryNormalSummon(opp, celtic, asSet: false);
                var legal = e.IsAwaitingResponse &&
                            e.PendingResponse.LegalCards != null && e.PendingResponse.LegalCards.Contains(shadow);
                Check("Shadow of Eyes: a face-up Normal Summon is not a Set", !legal);
            }

            // ─────────────────────── Solemn Wishes ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                ClearField(me); ClearField(opp);
                PlaceSpellTrap(e, me, SolemnWishes, 0);
                var lp = me.LifePoints; var oppLp = opp.LifePoints;
                e.Draw(me, 2);
                var afterTwo = me.LifePoints;
                e.Draw(opp, 1);
                Check("Solemn Wishes: +500 once per draw (not per card); the opponent's draws do not count",
                    afterTwo == lp + 500 && me.LifePoints == afterTwo && opp.LifePoints == oppLp,
                    $"{lp}→{afterTwo}→{me.LifePoints}");
            }

            // ─────────────────── Solomon's Lawbook / Time Seal ───────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                PlaceSpellTrap(e, opp, MinorGoblinOfficial, 0); // hits me in my Standby Phase
                me.LifePoints = 3000;
                var law = PlaceSet(e, me, SolomonsLawbook, 1);
                var ok = Activate(e, me, law, false);
                e.TryEndTurnSafe(me); Resolve(e);
                e.TryEndTurnSafe(opp); Resolve(e);
                var skipped = me.LifePoints;
                e.TryEndTurnSafe(me); Resolve(e);
                e.TryEndTurnSafe(opp); Resolve(e);
                Check("Solomon's Lawbook: your next Standby Phase is skipped (only the next one)",
                    ok && e.TurnPlayer == me && skipped == 3000 && me.LifePoints == 2500,
                    $"ok={ok} {skipped}→{me.LifePoints}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var seal = PlaceSet(e, me, TimeSeal, 0);
                var ok = Activate(e, me, seal, false);
                var hand = opp.Hand.Count;
                e.TryEndTurnSafe(me); Resolve(e);
                Check("Time Seal: the opponent skips their next Draw Phase",
                    ok && e.TurnPlayer == opp && opp.Hand.Count == hand, $"ok={ok} hand={hand}→{opp.Hand.Count}");
            }

            // ─────────────────────── The Fiend Megacyber ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, me, Celtic, 0);
                PlaceMonster(e, opp, Celtic, 0); PlaceMonster(e, opp, Celtic, 1);
                var fiend = Hand(e, me, FiendMegacyber);
                var prog = CompiledEffectCache.GetOrCompile(fiend.Def);
                var early = TextEffectRuntime.CanActivate(e, me, fiend, true, prog, out _);
                PlaceMonster(e, opp, Celtic, 2);
                var ok = Activate(e, me, fiend, true);
                Check("The Fiend Megacyber: Special Summon from hand when the opponent controls 2+ more monsters",
                    !early && ok && OnField(me, fiend) && !me.NormalSummonUsed, $"early={early} ok={ok}");
            }

            // ─────────────────────── Review follow-ups (PSV tranche 1) ───────────────────────
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Graveyard.Clear();
                var gear = Gy(e, me, Gearfried);
                var coth = PlaceSet(e, me, CallOfTheHaunted, 0);
                var ok = Activate(e, me, coth, false);
                Check("Gearfried + Call of the Haunted: not an Equip Card — both stay on the field",
                    ok && OnField(me, gear) && OnField(me, coth), $"ok={ok}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var gear = PlaceMonster(e, opp, Gearfried, 0);
                var circle = PlaceSet(e, me, SpellbindingCircle, 0);
                var ok = Activate(e, me, circle, false);
                Check("Gearfried + Spellbinding Circle: the Circle stays and still stops the attack",
                    ok && OnField(me, circle) && TextEffectRuntime.AttackForbiddenByEffect(e, gear), $"ok={ok}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Graveyard.Clear();
                var revived = Gy(e, me, Celtic);
                var coth = PlaceSet(e, me, CallOfTheHaunted, 0);
                Activate(e, me, coth, false);
                e.ReturnCardToHand(revived);
                e.NotifyPublic();
                Check("Call of the Haunted: its monster bounced (not destroyed) → the Trap stays, unlinked",
                    OnField(me, coth) && coth.EquippedTo == null && me.Hand.Contains(revived));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var target = PlaceMonster(e, opp, KoumoriDragon, 0);
                var da = Hand(e, me, DarknessApproaches);
                var a = Hand(e, me, Celtic); var b = Hand(e, me, SkullServant);
                TextEffectRuntime.TryResolveActivation(e, me, da, true, CompiledEffectCache.GetOrCompile(da.Def), false);
                e.TrySelectEffectTarget(a);
                e.TrySelectEffectTarget(b);
                e.CancelEffectTargeting();
                Check("Darkness Approaches: Cancel after the discards are paid cannot undo the activation",
                    me.Graveyard.Contains(da) && !me.Hand.Contains(da) && me.Graveyard.Contains(a) &&
                    me.Graveyard.Contains(b) && target.FaceUp);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var fd = Hand(e, me, FinalDestiny);
                for (var i = 0; i < 5; i++) Hand(e, me, SkullServant);
                var fdLegal = TextEffectRuntime.CanActivate(e, me, fd, true,
                    CompiledEffectCache.GetOrCompile(fd.Def), out var fdWhy);
                Check("Final Destiny: needs another card on the field", !fdLegal, $"why={fdWhy}");
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var dismissal = PlaceSpellTrap(e, me, InfiniteDismissal, 0);
                e.SendCardToGrave(me, dismissal);
                var ns = Hand(e, me, SkullServant);
                e.TryNormalSummon(me, ns, false);
                Resolve(e);
                e.TryEndTurnSafe(me);
                Resolve(e);
                Check("Infinite Dismissal: does nothing from the GY", OnField(me, ns));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var target = PlaceMonster(e, opp, Celtic, 0);
                target.FaceUp = false; target.Position = BattlePosition.Defense;
                var inv = PlaceMonster(e, me, InvitationToADarkSleep, 1);
                inv.FaceUp = false; inv.Position = BattlePosition.Defense;
                e.TryFlipSummon(me, inv);
                Resolve(e);
                target.FaceUp = true; target.Position = BattlePosition.Attack;
                e.NotifyPublic();
                Check("Invitation to a Dark Sleep: a Set target stays locked after it flips face-up",
                    TextEffectRuntime.AttackForbiddenByEffect(e, target));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                var mech = PlaceMonster(e, me, Mechanicalchaser, 0);
                Activate(e, me, Hand(e, me, LimiterRemoval), true);
                mech.FaceUp = false; mech.Position = BattlePosition.Defense; // Book of Moon
                e.NotifyPublic();
                e.TryEndTurnSafe(me);
                Resolve(e);
                Check("Limiter Removal: a Machine turned face-down is not destroyed and loses the boost",
                    OnField(me, mech) && mech.UntilEndOfTurnAtk == 0);
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear();
                PlaceMonster(e, me, HarpieLady, 0);
                for (var z = 1; z < me.MonsterZones.Length; z++) PlaceMonster(e, me, Celtic, z);
                me.Deck.Insert(0, HarpieLadySisters);
                var ego = Hand(e, me, ElegantEgotist);
                Check("Elegant Egotist: needs a free Monster Zone",
                    !TextEffectRuntime.CanActivate(e, me, ego, true, CompiledEffectCache.GetOrCompile(ego.Def), out _));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp);
                var fiend = PlaceMonster(e, me, FiendMegacyber, 0);
                for (var z = 0; z < 3; z++) PlaceMonster(e, opp, Celtic, z);
                Check("The Fiend Megacyber: its hand-only summon cannot be used from the field",
                    !e.CanActivateSpellTrap(me, fiend, false));
            }
            {
                var e = Fresh(db); var me = e.Player; var opp = e.Opponent;
                MainFor(e, me);
                ClearField(me); ClearField(opp); me.Hand.Clear(); me.Graveyard.Clear();
                Gy(e, me, 89631139);
                var burial = Hand(e, me, PrematureBurial);
                var lp = me.LifePoints;
                TextEffectRuntime.TryResolveActivation(e, me, burial, true,
                    CompiledEffectCache.GetOrCompile(burial.Def), false);
                var asked = e.IsAwaitingEffectTarget;
                e.CancelEffectTargeting();
                Check("Premature Burial: cancelling the target choice refunds the 800 LP",
                    asked && me.LifePoints == lp && me.Hand.Contains(burial), $"asked={asked} lp={me.LifePoints}");
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
