using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DestroyWatchersAndSpellShields batch: Firebird, Maji-Gire Panda, Mokey Mokey Smackdown, Pole Position and Blast
    /// Magician (<see cref="DestroyWatchersAndSpellShieldsAtoms"/>, <see cref="DestroyWatchersAndSpellShieldsRuntime"/>).
    /// Compile shapes, near misses, and mini-duels through TryActivateSpellTrap (Raigeki, Dark Hole, Mystical Space Typhoon,
    /// Pot of Greed, the cards themselves), TryAttack and the AI's activation.
    /// </summary>
    public static class DestroyWatchersAndSpellShieldsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Firebird = 87473172;
        const int Panda = 60102563;
        const int Smackdown = 1965724;
        const int PolePosition = 73578229;
        const int BlastMagician = 21051146;

        const int MokeyMokey = 27288416;     // Normal Fairy 300/100
        const int PetitAngel = 38142739;     // Normal Fairy 600/900
        const int Kurama = 85705804;         // Normal Winged Beast 800/800
        const int SkullRedBird = 10202894;   // Normal Winged Beast 1550/1200
        const int DarkGray = 9159938;        // Normal Beast 800/900
        const int MysticalSheep2 = 83464209; // Normal Beast 800/1000
        const int BeaverWarrior = 32452818;  // Normal Beast-Warrior 1200/1500
        const int Celtic = 91152256;         // Normal Warrior 1400/1200
        const int Wingweaver = 31447217;     // Normal Fairy 2750/2400
        const int RoyalLibrary = 70791313;   // Effect Spellcaster 0/2000
        const int Jinzo = 77585513;          // Traps on the field are negated
        const int SkillDrain = 82732705;     // Continuous Trap: face-up monsters' effects negated
        const int Raigeki = 12580477;
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int PotOfGreed = 55144522;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DestroyWatchersAndSpellShields: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            WatcherDuels(db, pDeck, aDeck, Check);
            SmackdownDuels(db, pDeck, aDeck, Check);
            PolePositionDuels(db, pDeck, aDeck, Check);
            BlastMagicianDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments)}]";

        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        static CardInstance FaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static CardInstance Up(DuelEngine engine, DuelistState who, int id, int zone) =>
            PlaceMonster(engine, who, id, zone, BattlePosition.Attack, true);

        static void Settle(DuelEngine engine)
        {
            DrainCombat(engine);
            FieldSpellEffects.RefreshBoard(engine);
            engine.NotifyPublic();
            DrainCombat(engine);
        }

        /// <summary>The player activates <paramref name="card"/> and picks <paramref name="target"/> if asked.</summary>
        static bool Activate(DuelEngine engine, CardInstance card, bool fromHand, CardInstance target,
            out List<CardInstance> offered)
        {
            offered = null;
            if (!engine.TryActivateSpellTrap(engine.Player, card, fromHand)) return false;
            if (engine.IsAwaitingEffectTarget)
            {
                offered = engine.PendingActivation.LegalTargets.ToList();
                if (target == null || !engine.TrySelectEffectTarget(target)) return false;
            }

            Settle(engine);
            return true;
        }

        static bool Attack(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            var p = engine.Player;
            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(p);
            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) return false;
            var ok = engine.TryAttack(p, attacker, target);
            Settle(engine);
            return ok;
        }

        static bool OnField(DuelEngine engine, CardInstance c) => engine.ControllerOf(c) != null;

        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static CompiledCardProgram CompileText(string text, bool monster) =>
            CardTextEffectCompiler.Compile(monster
                ? new CardDef
                {
                    id = 990211, name = "Near-miss Watcher", type = "Effect Monster", frameType = "effect", race = "Beast",
                    attribute = "EARTH", level = 4, atk = 1000, def = 800, desc = text
                }
                : new CardDef
                {
                    id = 990212, name = "Near-miss Shield", type = "Trap Card", frameType = "trap", race = "Continuous",
                    desc = text
                });

        static bool FromThisBatch(EffectClause c) =>
            c != null && (c.SpellCountersPerTargetAtk > 0 ||
                          c.Action == EffectActionKind.GainSelfAtkEachTimeRaceMonsterDestroyed ||
                          c.Action == EffectActionKind.NamedMonstersAtkBecomeUntilEndOfTurnWhenRaceMonsterDestroyed ||
                          c.Action == EffectActionKind.HighestAtkMonstersUnaffectedBySpells ||
                          c.Action == EffectActionKind.DestroyHighestAtkMonstersWhenThisLeavesField);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id, out bool implemented)
            {
                var def = db.Get(id);
                implemented = def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                return def != null ? CardTextEffectCompiler.Compile(def) : null;
            }

            {
                var p = P(Firebird, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Firebird: a Winged Beast you control destroyed → this card gains 500 ATK",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.GainSelfAtkEachTimeRaceMonsterDestroyed && c.Side == EffectSide.Controller &&
                    c.RaceFilter == "Winged Beast" && c.Amount == 500, Describe(p));
            }
            {
                var p = P(Panda, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Maji-Gire Panda: a Beast on either field destroyed → this card gains 500 ATK",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Action == EffectActionKind.GainSelfAtkEachTimeRaceMonsterDestroyed && c.Side == EffectSide.Both &&
                    c.RaceFilter == "Beast" && c.Amount == 500, Describe(p));
            }
            {
                var p = P(Smackdown, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Mokey Mokey Smackdown: a Fairy you control destroyed while you control Mokey Mokey → its ATK becomes 3000",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Action == EffectActionKind.NamedMonstersAtkBecomeUntilEndOfTurnWhenRaceMonsterDestroyed &&
                    c.NamedCard == "Mokey Mokey" && c.RaceFilter == "Fairy" && c.Amount == 3000 && !c.MakesChainLink,
                    Describe(p));
            }
            {
                var p = P(PolePosition, out var impl);
                Check("Pole Position: highest-ATK Spell shield + leave-field destruction",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    p.ClauseList.Exists(x => x.Action == EffectActionKind.HighestAtkMonstersUnaffectedBySpells) &&
                    p.ClauseList.Exists(x => x.Action == EffectActionKind.DestroyHighestAtkMonstersWhenThisLeavesField) &&
                    p.ClauseList.TrueForAll(x => x.Timing == EffectTiming.ContinuousWhileFaceUp), Describe(p));
            }
            {
                var p = P(BlastMagician, out var impl);
                var counter = p?.ClauseList.FirstOrDefault(x => x.Action == EffectActionKind.PlaceSpellCounters);
                var ign = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.Activate);
                Check("Blast Magician: Spell Counter per resolved Spell (no max) + target face-up monster, remove ceil(ATK/700)",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 && counter != null &&
                    counter.PlaceCounterOnSpellActivate && counter.CounterMax == 0 && ign != null &&
                    ign.Action == EffectActionKind.Destroy && ign.Zone == EffectZoneFilter.FieldAnyMonster &&
                    ign.RequiresTargetChoice && ign.IsPsctTarget && ign.RequiresTargetFaceUp &&
                    ign.SpellCountersPerTargetAtk == 700 && ign.RequiresRemoveSpellCounters == 0 &&
                    EffectVocabulary.CostOf(ign) == EffectCostKind.RemoveCounters, Describe(p));
            }
        }

        static void NearMisses(Chk Check)
        {
            var misses = new (string text, bool monster)[]
            {
                // Destroyed by battle only; loses ATK; an extra sentence.
                ("Each time a Winged Beast-Type monster(s) on your side of the field is destroyed by battle, this card gains " +
                 "500 ATK.", true),
                ("Each time a Beast-Type monster(s) on the field is destroyed, this card loses 500 ATK.", true),
                ("Each time a Beast-Type monster(s) on the field is destroyed, this card gains 500 ATK. This card cannot " +
                 "attack.", true),
                // Two different names; a different duration.
                ("While a face-up \"Mokey Mokey\" is on your side of the field, if a Fairy-Type monster on your side of the " +
                 "field is destroyed, the ATK of each \"Mokey Mokey King\" on your side of the field becomes 3000 until the " +
                 "end of this turn.", false),
                ("While a face-up \"Mokey Mokey\" is on your side of the field, if a Fairy-Type monster on your side of the " +
                 "field is destroyed, the ATK of each \"Mokey Mokey\" on your side of the field becomes 3000.", false),
                // Trap Cards; another card's name; a different destruction.
                ("The face-up monster(s) with the highest ATK on the field is unaffected by the effects of Trap Cards. When " +
                 "\"Pole Position\" is removed from the field, destroy the face-up monster(s) with the highest ATK on the " +
                 "field.", false),
                ("The face-up monster(s) with the highest ATK on the field is unaffected by the effects of Spell Cards. When " +
                 "\"Royal Decree\" is removed from the field, destroy the face-up monster(s) with the highest ATK on the " +
                 "field.", false),
                ("The face-up monster(s) with the highest ATK on the field is unaffected by the effects of Spell Cards.", false),
                // Only the opponent's monsters; banish instead.
                ("You can remove any number of Spell Counters from this card, then target 1 face-up monster your opponent " +
                 "controls with ATK less than or equal to the number of counters you removed x 700; destroy that target.", true),
                ("You can remove any number of Spell Counters from this card, then target 1 face-up monster on the field with " +
                 "ATK less than or equal to the number of counters you removed x 700; banish that target.", true),
            };
            foreach (var (text, monster) in misses)
            {
                var p = CompileText(text, monster);
                Check("near miss stays unparsed: " + text.Substring(0, Math.Min(70, text.Length)),
                    p != null && !p.FullyCompiled && !p.ClauseList.Exists(FromThisBatch), Describe(p));
            }
        }

        // ───────────────────────────── Firebird / Maji-Gire Panda ─────────────────────────────

        static void WatcherDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Firebird: the player's Winged Beast is destroyed by battle → +500, which lasts after more refreshes.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bird = PlaceMonster(e, p, Firebird, 0, BattlePosition.Defense, true);
                var kurama = Up(e, p, Kurama, 1);
                Up(e, o, Celtic, 0);
                Settle(e);
                var attacked = Attack(e, kurama, o.MonsterZones[0].Occupant);
                Settle(e);
                Check("Firebird: a Winged Beast you control destroyed by battle — it gains 500 ATK (lasting)",
                    reached && attacked && InGy(p, kurama) && OnField(e, bird) && bird.CurrentAtk == 1500,
                    $"reached={reached} att={attacked} kurama={InGy(p, kurama)} atk={bird.CurrentAtk}");
            }

            // Firebird destroyed together with the Winged Beasts (Raigeki on its side): nothing; the opponent's
            // Winged Beast destroyed is not "your side" for the player's Firebird.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bird = Up(e, p, Firebird, 0);
                Up(e, o, SkullRedBird, 0);
                Up(e, o, Firebird, 1);
                var theirs = o.MonsterZones[1].Occupant;
                var rg = PutInHand(e, p, Raigeki);
                var act = Activate(e, rg, true, null, out _);
                Check("Firebird: the opponent's Firebird destroyed with their Winged Beast gains nothing; the player's " +
                      "Firebird does not watch the opponent's side",
                    reached && act && InGy(o, theirs) && OnField(e, bird) && bird.CurrentAtk == 1000,
                    $"reached={reached} act={act} theirsGy={InGy(o, theirs)} atk={bird.CurrentAtk}");
            }

            // Maji-Gire Panda: two Beasts of the opponent destroyed by one Raigeki → one event, +500 once; a Beast-Warrior
            // is not a Beast.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var panda = Up(e, p, Panda, 0);
                Up(e, o, DarkGray, 0);
                Up(e, o, MysticalSheep2, 1);
                var rg = PutInHand(e, p, Raigeki);
                var act = Activate(e, rg, true, null, out _);
                Check("Maji-Gire Panda: two Beasts on the field destroyed together — it gains 500 ATK once",
                    reached && act && o.MonstersOnField().Count() == 0 && panda.CurrentAtk == 1500,
                    $"reached={reached} act={act} atk={panda.CurrentAtk}");

                Up(e, o, BeaverWarrior, 0);
                var rg2 = PutInHand(e, p, Raigeki);
                var act2 = Activate(e, rg2, true, null, out _);
                Check("Maji-Gire Panda: a Beast-Warrior destroyed — no gain",
                    act2 && o.MonstersOnField().Count() == 0 && panda.CurrentAtk == 1500,
                    $"act2={act2} atk={panda.CurrentAtk}");
            }

            // Maji-Gire Panda under Skill Drain: its effect is negated — no gain.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, SkillDrain, 0);
                var panda = Up(e, p, Panda, 0);
                Up(e, o, DarkGray, 0);
                Settle(e);
                var rg = PutInHand(e, p, Raigeki);
                var act = Activate(e, rg, true, null, out _);
                Check("Maji-Gire Panda: negated by Skill Drain — a Beast destroyed gives no ATK",
                    reached && act && panda.IsNegated && o.MonstersOnField().Count() == 0 && panda.CurrentAtk == 1000,
                    $"reached={reached} act={act} neg={panda.IsNegated} atk={panda.CurrentAtk}");
            }
        }

        // ───────────────────────────── Mokey Mokey Smackdown ─────────────────────────────

        static void SmackdownDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A Fairy the player controls is destroyed by battle while a Mokey Mokey is face-up: each Mokey Mokey becomes
            // 3000 ATK (one summoned later this turn too); next turn it is 300 again.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, Smackdown, 0);
                var mokey = PlaceMonster(e, p, MokeyMokey, 0, BattlePosition.Defense, true);
                var angel = Up(e, p, PetitAngel, 1);
                Up(e, o, Celtic, 0);
                Settle(e);
                var before = mokey.CurrentAtk;
                var attacked = Attack(e, angel, o.MonsterZones[0].Occupant);
                var during = mokey.CurrentAtk;
                var second = PlaceMonster(e, p, MokeyMokey, 2, BattlePosition.Defense, true);
                Settle(e);
                var secondAtk = second.CurrentAtk;
                o.MonsterZones[0].Occupant = null;
                e.TryEndTurnSafe(p);
                Settle(e);
                Check("Mokey Mokey Smackdown: a Fairy you control destroyed — each Mokey Mokey has 3000 ATK until the end " +
                      "of the turn",
                    reached && attacked && InGy(p, angel) && before == 300 && during == 3000 && secondAtk == 3000 &&
                    e.TurnPlayer == o && mokey.CurrentAtk == 300,
                    $"reached={reached} att={attacked} before={before} during={during} second={secondAtk} " +
                    $"after={mokey.CurrentAtk} turn={e.TurnPlayer?.Name}");
            }

            // No face-up Mokey Mokey when the Fairy is destroyed: no flag (a Mokey Mokey placed afterwards stays 300); a
            // non-Fairy destroyed while Mokey Mokey is out changes nothing.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, Smackdown, 0);
                var angel = Up(e, p, PetitAngel, 1);
                Up(e, o, Celtic, 0);
                Settle(e);
                var attacked = Attack(e, angel, o.MonsterZones[0].Occupant);
                var mokey = PlaceMonster(e, p, MokeyMokey, 0, BattlePosition.Defense, true);
                Settle(e);
                Check("Mokey Mokey Smackdown: no Mokey Mokey on the field when the Fairy is destroyed — no change",
                    reached && attacked && InGy(p, angel) && mokey.CurrentAtk == 300,
                    $"reached={reached} att={attacked} atk={mokey.CurrentAtk}");
            }
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, Smackdown, 0);
                var mokey = PlaceMonster(e, p, MokeyMokey, 0, BattlePosition.Defense, true);
                var kurama = Up(e, p, Kurama, 1);
                Up(e, o, Celtic, 0);
                Settle(e);
                var attacked = Attack(e, kurama, o.MonsterZones[0].Occupant);
                Check("Mokey Mokey Smackdown: a non-Fairy destroyed — Mokey Mokey stays 300",
                    reached && attacked && InGy(p, kurama) && mokey.CurrentAtk == 300,
                    $"reached={reached} att={attacked} atk={mokey.CurrentAtk}");
            }
        }

        // ───────────────────────────── Pole Position ─────────────────────────────

        static void PolePositionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Activated from the Set: the highest-ATK monster survives Dark Hole; the rest are destroyed.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pole = PlaceSetTrap(e, p, PolePosition, 0);
                var celtic = Up(e, p, Celtic, 0);
                var kurama = Up(e, p, Kurama, 1);
                var bird = Up(e, o, SkullRedBird, 0);
                var activated = Activate(e, pole, false, null, out _);
                var dh = PutInHand(e, p, DarkHole);
                var act = Activate(e, dh, true, null, out _);
                Check("Pole Position: the face-up monster with the highest ATK is unaffected by Dark Hole",
                    reached && activated && pole.FaceUp && act && OnField(e, bird) && InGy(p, celtic) && InGy(p, kurama),
                    $"reached={reached} activated={activated} up={pole.FaceUp} act={act} bird={OnField(e, bird)} " +
                    $"celtic={InGy(p, celtic)} kurama={InGy(p, kurama)}");
            }

            // Tied for the highest ATK: both are shielded; leaving the field (MST) destroys both, nothing else.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pole = FaceUpSpellTrap(e, p, PolePosition, 0);
                var bird1 = Up(e, p, SkullRedBird, 0);
                var celtic = Up(e, p, Celtic, 1);
                var bird2 = Up(e, o, SkullRedBird, 0);
                Settle(e);
                var shielded = TextEffects.MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(e, bird1, e.CreateCardInstance(DarkHole)) &&
                               TextEffects.MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(e, bird2, e.CreateCardInstance(DarkHole)) &&
                               !TextEffects.MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(e, celtic, e.CreateCardInstance(DarkHole));
                var mst = PutInHand(e, p, Mst);
                var act = Activate(e, mst, true, pole, out _);
                Check("Pole Position: tied highest ATK both shielded; when it leaves the field both are destroyed",
                    reached && shielded && act && InGy(p, pole) && InGy(p, bird1) && InGy(o, bird2) && OnField(e, celtic),
                    $"reached={reached} shielded={shielded} act={act} pole={InGy(p, pole)} b1={InGy(p, bird1)} " +
                    $"b2={InGy(o, bird2)} celtic={OnField(e, celtic)}");
            }

            // A Set Pole Position destroyed: it never applied — nothing is destroyed.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pole = PlaceSetTrap(e, p, PolePosition, 0);
                var bird = Up(e, o, SkullRedBird, 0);
                Settle(e);
                var mst = PutInHand(e, p, Mst);
                var act = Activate(e, mst, true, pole, out _);
                Check("Pole Position: destroyed while Set — nothing is destroyed",
                    reached && act && InGy(p, pole) && OnField(e, bird),
                    $"reached={reached} act={act} pole={InGy(p, pole)} bird={OnField(e, bird)}");
            }

            // Negated by Jinzo: no shield (Dark Hole destroys the highest too) and no leave destruction.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pole = FaceUpSpellTrap(e, p, PolePosition, 0);
                var jinzo = Up(e, o, Jinzo, 0);
                var celtic = Up(e, p, Celtic, 0);
                Settle(e);
                var negated = pole.IsNegated;
                var mst = PutInHand(e, p, Mst);
                var act = Activate(e, mst, true, pole, out _);
                var jinzoAfterLeave = OnField(e, jinzo);
                var celticAfterLeave = OnField(e, celtic);
                var dh = PutInHand(e, p, DarkHole);
                var act2 = Activate(e, dh, true, null, out _);
                Check("Pole Position: negated by Jinzo — leaving the field destroys nothing",
                    reached && negated && act && InGy(p, pole) && jinzoAfterLeave && celticAfterLeave,
                    $"reached={reached} neg={negated} act={act} pole={InGy(p, pole)} jinzo={jinzoAfterLeave} " +
                    $"celtic={celticAfterLeave}");
                Check("Pole Position: gone — Dark Hole destroys every monster",
                    act2 && InGy(o, jinzo) && InGy(p, celtic), $"act2={act2} jinzo={InGy(o, jinzo)}");
            }
        }

        // ───────────────────────────── Blast Magician ─────────────────────────────

        static void BlastMagicianDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // 3 counters: 1400 ATK (2 counters) and its own 1400 are legal, 2750 (4) and a face-down monster are not; the
            // pick removes 2 counters and destroys the target.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var blast = Up(e, p, BlastMagician, 0);
                blast.Counters = 3;
                var celtic = Up(e, o, Celtic, 0);
                var weaver = Up(e, o, Wingweaver, 1);
                var setOne = PlaceMonster(e, o, DarkGray, 2, BattlePosition.Defense, false);
                Settle(e);
                var act = Activate(e, blast, false, celtic, out var offered);
                Check("Blast Magician: targets need ceil(ATK/700) counters; 2 removed for a 1400 ATK target, which is destroyed",
                    reached && act && offered != null && offered.Contains(celtic) && offered.Contains(blast) &&
                    !offered.Contains(weaver) && !offered.Contains(setOne) && InGy(o, celtic) && blast.Counters == 1 &&
                    OnField(e, weaver),
                    $"reached={reached} act={act} offered=[{string.Join(",", offered?.Select(x => x.Name) ?? new string[0])}] " +
                    $"celtic={InGy(o, celtic)} counters={blast.Counters}");
            }

            // A 0 ATK target still costs 1 counter; with no counters it cannot be activated.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var blast = PlaceMonster(e, p, BlastMagician, 0, BattlePosition.Defense, true);
                blast.Counters = 1;
                var lib = PlaceMonster(e, o, RoyalLibrary, 0, BattlePosition.Defense, true);
                Settle(e);
                var act = Activate(e, blast, false, lib, out _);
                var again = e.TryActivateSpellTrap(p, blast, fromHand: false);
                Check("Blast Magician: a 0 ATK target costs 1 counter; with 0 counters it cannot be activated",
                    reached && act && InGy(o, lib) && blast.Counters == 0 && !again && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} lib={InGy(o, lib)} counters={blast.Counters} again={again}");
            }

            // A Spell resolves: +1 counter, no printed maximum (5 → 6).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var blast = Up(e, p, BlastMagician, 0);
                blast.Counters = 5;
                Settle(e);
                var pot = PutInHand(e, p, PotOfGreed);
                var act = Activate(e, pot, true, null, out _);
                Check("Blast Magician: a Spell Card resolves — +1 Spell Counter, no maximum",
                    reached && act && blast.Counters == 6, $"reached={reached} act={act} counters={blast.Counters}");
            }

            // The AI's Blast Magician (2 counters) destroys the player's strongest monster it can pay for.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                e.Player.Hand.Clear();
                e.Opponent.Hand.Clear();
                var p = e.Player;
                var o = e.Opponent;
                var blast = Up(e, o, BlastMagician, 0);
                blast.Counters = 2;
                var bird = Up(e, p, SkullRedBird, 2);
                Up(e, o, Kurama, 1);
                Settle(e);
                // Only the AI's own 800 ATK monster (and itself) are payable: the AI does not use it.
                var skipsOwnOnly = DestroyWatchersAndSpellShieldsRuntime.AiSkipsIgnition(e, o, blast);
                var celtic = Up(e, p, Celtic, 0);
                var kurama = Up(e, p, Kurama, 1);
                Settle(e);
                var usesNow = !DestroyWatchersAndSpellShieldsRuntime.AiSkipsIgnition(e, o, blast);
                var act = e.TryActivateSpellTrap(o, blast, fromHand: false);
                Settle(e);
                Check("Blast Magician (AI): skipped with no payable opponent's monster; with 2 counters it destroys the " +
                      "player's 1400 ATK monster (not the 1550 one)",
                    reached && skipsOwnOnly && usesNow && act && InGy(p, celtic) && OnField(e, kurama) && OnField(e, bird) && blast.Counters == 0 &&
                    !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} celtic={InGy(p, celtic)} bird={OnField(e, bird)} counters={blast.Counters}");
            }
        }
    }
}
