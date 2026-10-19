using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SimpleTriggersAndSpells" (v210 FET), checked against the ygopro scripts:
    /// <list type="bullet">
    /// <item>Abare Ushioni c89718302: IGNITION, SetCountLimit(1), Duel.CallCoin(tp) at resolution; 1000 to the opponent on a
    /// right call, 1000 to you on a wrong one.</item>
    /// <item>Blade Rabbit c58268433: TRIGGER_F on EVENT_CHANGE_POS (Attack → face-up Defense), CARD_TARGET 1 monster the
    /// opponent controls (face-down too); destroy it.</item>
    /// <item>Granmarg the Rock Monarch c60229110: TRIGGER_F on EVENT_SUMMON_SUCCESS if Tribute Summoned, CARD_TARGET 1
    /// face-down card on either field; destroy it if still face-down.</item>
    /// <item>Good Goblin Housekeeping c9744376: draw (copies in your GY)+1, then 1 hand card to the Deck bottom (mandatory).</item>
    /// <item>The Big March of Animals c1689516: each face-up Beast you control gains 200 x your face-up Beasts this turn.</item>
    /// <item>Elemental Burst c61411502: cost Tribute one WIND, WATER, FIRE and EARTH monster; destroy all cards the opponent
    /// controls.</item>
    /// </list>
    /// </summary>
    public static class SimpleTriggersAndSpellsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Ushioni = 89718302;
        const int BladeRabbit = 58268433;
        const int Granmarg = 60229110;
        const int GoblinHousekeeping = 9744376;
        const int BigMarch = 1689516;
        const int ElementalBurst = 61411502;

        const int Celtic = 91152256;          // Warrior EARTH 1400
        const int HarpieLady = 76812113;      // Winged Beast WIND 1300
        const int FiendKraken = 77456781;     // Aqua WATER 1200
        const int FlameManipulator = 34460851; // Spellcaster FIRE 900
        const int HinotamaSoul = 96851799;    // Pyro FIRE 600
        const int MysticalElf = 15025844;     // Spellcaster LIGHT 800
        const int SilverFang = 90357090;      // Beast EARTH 1200
        const int Gazelle = 5818798;          // Beast EARTH 1500
        const int BigKoala = 42129512;        // Beast EARTH 2700
        const int BattleOx = 5053103;         // Beast-Warrior EARTH 1700
        const int TrapHole = 4206964;
        const int MirrorForce = 44095762;
        const int Mountain = 50913601;
        const int PotOfGreed = 55144522;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SimpleTriggersAndSpells: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);

            UshioniCalls(db, pDeck, aDeck, Check);
            UshioniAi(db, pDeck, aDeck, Check);

            RabbitDestroysOpponentsMonster(db, pDeck, aDeck, Check);
            RabbitAi(db, pDeck, aDeck, Check);

            GranmargDestroysASetCard(db, pDeck, aDeck, Check);
            GranmargAi(db, pDeck, aDeck, Check);

            HousekeepingDrawsAndReturns(db, pDeck, aDeck, Check);
            HousekeepingAi(db, pDeck, aDeck, Check);

            BigMarchBoostsFaceUpBeasts(db, pDeck, aDeck, Check);

            ElementalBurstTributesOneEach(db, pDeck, aDeck, Check);
            ElementalBurstLegality(db, pDeck, aDeck, Check);
            ElementalBurstAi(db, pDeck, aDeck, Check);
            ElementalBurstInAWindow(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            (CompiledCardProgram prog, CardEffectStatusKind st, EffectClause c) Of(int id)
            {
                var def = db.Get(id);
                var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
                return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented,
                    p?.ClauseList.Count == 1 ? p.ClauseList[0] : null);
            }

            bool Ok(CompiledCardProgram p, CardEffectStatusKind st, EffectClause c) =>
                p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null;

            {
                var (p, st, c) = Of(Ushioni);
                Check("Abare Ushioni: one once-per-turn coin-call ignition, 1000 either way",
                    Ok(p, st, c) && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.CallCoinRightBurnOpponentWrongBurnSelf && c.Amount == 1000 &&
                    c.OncePerTurn && c.MakesChainLink && !c.RequiresTargetChoice, $"status={st} {Describe(p)}");
            }
            {
                var (p, st, c) = Of(BladeRabbit);
                Check("Blade Rabbit: Dream Clown's Attack → face-up Defense trigger, targets an opponent's monster",
                    Ok(p, st, c) && c.Timing == EffectTiming.PositionChanged && c.Action == EffectActionKind.Destroy &&
                    c.Zone == EffectZoneFilter.OppMonsters && c.PositionFromAttack && c.PositionToDefense &&
                    c.RequireDestinationFaceUp && TargetingTruthRuntime.ClauseTargets(c), $"status={st} {Describe(p)}");
            }
            {
                var (p, st, c) = Of(Granmarg);
                Check("Granmarg: mandatory Tribute Summon trigger targeting a Set card on the field",
                    Ok(p, st, c) && c.Timing == EffectTiming.ThisCardSummoned && c.Action == EffectActionKind.Destroy &&
                    c.Zone == EffectZoneFilter.FieldSetCards && c.RequiresThisTributeSummoned && !c.IsOptional &&
                    TargetingTruthRuntime.ClauseTargets(c), $"status={st} {Describe(p)}");
            }
            {
                var (p, st, c) = Of(GoblinHousekeeping);
                Check("Good Goblin Housekeeping: draw named-in-GY + 1, then a hand card to the Deck bottom",
                    Ok(p, st, c) && c.Action == EffectActionKind.DrawNamedInGyPlusAmountThenHandCardToDeckBottom &&
                    c.Amount == 1 && c.NamedCard == "Good Goblin Housekeeping" &&
                    c.ActivationCondition == DrawThenDiscardRuntime.CanDrawAmount, $"status={st} {Describe(p)}");
            }
            {
                var (p, st, c) = Of(BigMarch);
                Check("The Big March of Animals: face-up Beasts gain 200 per face-up Beast, needs a face-up Beast",
                    Ok(p, st, c) && c.Action == EffectActionKind.FaceUpRaceYouControlGainAtkPerFaceUpRaceUntilEndOfTurn &&
                    c.Amount == 200 && c.RaceFilter == "Beast" && c.ActivationCondition == "YouControlFaceUpRace",
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st, c) = Of(ElementalBurst);
                Check("Elemental Burst: Tribute 4 (one of each Attribute), destroy every card the opponent controls",
                    Ok(p, st, c) && c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.OppAnyCardOnField &&
                    !c.RequiresTargetChoice && c.RequiresTributeCount == 4 &&
                    c.TributeOneEachAttribute == "WIND|WATER|FIRE|EARTH" &&
                    c.ActivationCondition == SpellCounterSpendersRuntime.OpponentControlsACard, $"status={st} {Describe(p)}");
            }
        }

        static CompiledCardProgram Probe(string desc, string type, string race = "Normal") =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990210000 + Math.Abs(desc.GetHashCode() % 9999), name = "SimpleTriggersAndSpells Probe", type = type,
                race = race, attribute = type.Contains("Monster") ? "EARTH" : null, level = 4,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect", desc = desc
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> pred) =>
            p != null && p.ClauseList.Any(c => c != null && pred(c));

        /// <summary>Look-alike sentences do not take these atoms (anchored whole-text shapes).</summary>
        static void NearMisses(Chk Check)
        {
            const string monster = "Effect Monster";
            var rows = new (string desc, string type, Func<EffectClause, bool> wrong, string label)[]
            {
                ("Once per turn, you can toss a coin and call it. If you call it right, inflict 1000 damage to your " +
                 "opponent. If you call it wrong, you take 500 damage.", monster,
                    c => c.Action == EffectActionKind.CallCoinRightBurnOpponentWrongBurnSelf, "different amounts"),
                ("Once per turn, you can toss a coin and call it. If you call it right, inflict 1000 damage to your " +
                 "opponent. If you call it wrong, you take 1000 damage. Then draw 1 card.", monster,
                    c => c.Action == EffectActionKind.CallCoinRightBurnOpponentWrongBurnSelf, "a coin call plus a rider"),
                ("When this card is changed from Attack Position to face-up Defense Position, destroy 1 monster on " +
                 "your opponent's side of the field, then draw 1 card.", monster,
                    c => c.Timing == EffectTiming.PositionChanged && c.Action == EffectActionKind.Destroy,
                    "a position trigger plus a draw"),
                ("If this card is Tribute Summoned: Target 1 Set card your opponent controls; destroy that target.",
                    monster, c => c.Zone == EffectZoneFilter.FieldSetCards, "only the opponent's Set cards"),
                ("If this card is Tribute Summoned: Target 1 Set card on the field; return that target to the hand.",
                    monster, c => c.Zone == EffectZoneFilter.FieldSetCards, "a return, not a destroy"),
                ("Draw cards equal to the number of \"Good Goblin Housekeeping\" cards in your Graveyard +1.",
                    "Trap Card", c => c.Action == EffectActionKind.DrawNamedInGyPlusAmountThenHandCardToDeckBottom,
                    "the draw without the return"),
                ("Until the end of this turn, all face-up Beast-Type monsters on your side of the field gain 200 ATK for " +
                 "each Warrior-Type monster on your side of the field.", "Spell Card",
                    c => c.Action == EffectActionKind.FaceUpRaceYouControlGainAtkPerFaceUpRaceUntilEndOfTurn,
                    "counts another Type"),
                ("Until the end of this turn, all face-up Beast-Type monsters on your side of the field gain 200 ATK for " +
                 "each Beast-Type monster on the field.", "Spell Card",
                    c => c.Action == EffectActionKind.FaceUpRaceYouControlGainAtkPerFaceUpRaceUntilEndOfTurn,
                    "counts both fields"),
                ("Tribute 1 each of WIND, WATER, FIRE, and WIND monsters to activate this card. Destroy all cards on " +
                 "your opponent's side of the field.", "Trap Card",
                    c => !string.IsNullOrEmpty(c.TributeOneEachAttribute), "a repeated Attribute"),
                ("Tribute 1 each of WIND, WATER, FIRE, and EARTH monsters to activate this card. Destroy all monsters on " +
                 "your opponent's side of the field.", "Trap Card",
                    c => !string.IsNullOrEmpty(c.TributeOneEachAttribute), "monsters only"),
            };
            foreach (var (desc, type, wrong, label) in rows)
            {
                var p = Probe(desc, type);
                Check($"near-miss ({label}) does not take the atom", p != null && !Has(p, wrong), Describe(p));
            }
        }

        // ───────────────────────────── Abare Ushioni ─────────────────────────────

        static void UshioniCalls(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var right in new[] { true, false })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ush = PlaceMonster(e, p, Ushioni, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                e.Rng.QueueCoin(true); // the toss is Heads
                var act = e.TryActivateSpellTrap(p, ush, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall &&
                            e.PendingActivation.Controller == p;
                var lpWhileAsked = p.LifePoints == pLp && o.LifePoints == oLp;
                var called = asked && e.TrySelectCoinCall(right);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var ok = right
                    ? o.LifePoints == oLp - 1000 && p.LifePoints == pLp
                    : p.LifePoints == pLp - 1000 && o.LifePoints == oLp;
                Check($"Abare Ushioni: the player calls at resolution — {(right ? "right: 1000 to the opponent" : "wrong: 1000 to you")}",
                    act && asked && lpWhileAsked && called && ok && !e.Chain.HasLinks && e.PendingActivation == null,
                    $"act={act} asked={asked} called={called} pLp={p.LifePoints} oLp={o.LifePoints} {State(e)}");
                Check($"Abare Ushioni: once per turn ({(right ? "after a right call" : "after a wrong call")})",
                    !e.CanActivateSpellTrap(p, ush, fromHand: false), State(e));
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var ush = PlaceMonster(e, e.Player, Ushioni, 0, BattlePosition.Attack, true);
                var oLp = e.Opponent.LifePoints;
                e.Rng.QueueCoin(true);
                var act = e.TryActivateSpellTrap(e.Player, ush, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall;
                e.CancelEffectTargeting();
                Check("Abare Ushioni: CANCEL at the call — the call is mandatory (Heads), the effect resolves",
                    act && asked && e.Opponent.LifePoints == oLp - 1000 && e.PendingActivation == null && !e.Chain.HasLinks,
                    $"act={act} asked={asked} oLp={e.Opponent.LifePoints} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var ush = PlaceMonster(e, e.Player, Ushioni, 0, BattlePosition.Defense, false);
                Check("Abare Ushioni: face-down — no ignition", !e.CanActivateSpellTrap(e.Player, ush, fromHand: false),
                    State(e));
            }
        }

        static void UshioniAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            var ush = PlaceMonster(e, o, Ushioni, 0, BattlePosition.Attack, true);
            int pLp = p.LifePoints, oLp = o.LifePoints;
            e.Rng.QueueCoin(false); // Tails: the AI's Heads call is wrong
            var act = e.TryActivateSpellTrap(o, ush, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            Check("Abare Ushioni (AI): calls by itself — wrong call, the AI takes 1000, no prompt left",
                main && act && o.LifePoints == oLp - 1000 && p.LifePoints == pLp && !e.IsAwaitingEffectTarget &&
                !e.Chain.HasLinks,
                $"main={main} act={act} pLp={p.LifePoints} oLp={o.LifePoints} {State(e)}");
        }

        // ───────────────────────────── Blade Rabbit ─────────────────────────────

        static void RabbitDestroysOpponentsMonster(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var rabbit = PlaceMonster(e, p, BladeRabbit, 0, BattlePosition.Attack, true);
                var up = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, FiendKraken, 1, BattlePosition.Defense, false);
                var mine = PlaceMonster(e, p, HarpieLady, 1, BattlePosition.Attack, true);
                var changed = e.TryChangePosition(p, rabbit);
                var legal = Legal(e);
                var offered = legal.Contains(up) && legal.Contains(set) && !legal.Contains(mine) && !legal.Contains(rabbit);
                var picked = offered && e.TrySelectEffectTarget(set);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Blade Rabbit: Attack → face-up Defense — the player targets the opponent's Set monster; destroyed",
                    changed && offered && picked && Where(e, set) == "O.gy" && Where(e, up) == "O.mz" &&
                    rabbit.Position == BattlePosition.Defense && rabbit.FaceUp,
                    $"changed={changed} legal=[{Names(legal)}] set={Where(e, set)} up={Where(e, up)} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var rabbit = PlaceMonster(e, e.Player, BladeRabbit, 0, BattlePosition.Defense, true);
                var victim = PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var changed = e.TryChangePosition(e.Player, rabbit);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Blade Rabbit: Defense → Attack does not trigger",
                    changed && Where(e, victim) == "O.mz" && !e.IsAwaitingEffectTarget, State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var rabbit = PlaceMonster(e, e.Player, BladeRabbit, 0, BattlePosition.Attack, true);
                var victim = PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var changed = e.TryChangePosition(e.Player, rabbit);
                if (e.IsAwaitingEffectTarget && Legal(e).Count == 1) e.TrySelectEffectTarget(victim);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Blade Rabbit: a lone opponent's monster is destroyed",
                    changed && Where(e, victim) == "O.gy" && !e.IsAwaitingEffectTarget, State(e));
            }
        }

        static void RabbitAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var rabbit = PlaceMonster(e, e.Opponent, BladeRabbit, 0, BattlePosition.Attack, true);
            var a = PlaceMonster(e, e.Player, Celtic, 0, BattlePosition.Attack, true);
            var b = PlaceMonster(e, e.Player, HarpieLady, 1, BattlePosition.Attack, true);
            var changed = e.TryChangePosition(e.Opponent, rabbit);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var gone = (Where(e, a) == "P.gy" ? 1 : 0) + (Where(e, b) == "P.gy" ? 1 : 0);
            Check("Blade Rabbit (AI): picks and destroys 1 of the player's monsters, no prompt left",
                main && changed && gone == 1 && !e.IsAwaitingEffectTarget,
                $"main={main} changed={changed} a={Where(e, a)} b={Where(e, b)} {State(e)}");
        }

        // ───────────────────────────── Granmarg the Rock Monarch ─────────────────────────────

        static void GranmargDestroysASetCard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var fodder = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var oppUp = PlaceMonster(e, o, HarpieLady, 0, BattlePosition.Attack, true);
                var oppSetMon = PlaceMonster(e, o, FiendKraken, 1, BattlePosition.Defense, false);
                var oppSet = PlaceSetTrap(e, o, MirrorForce, 0);
                var mySet = PlaceSetTrap(e, p, TrapHole, 0);
                var field = e.CreateCardInstance(Mountain);
                field.FaceUp = false;
                o.FieldSpellZone.Occupant = field;
                var gran = PutInHand(e, p, Granmarg);
                var summoned = e.TryNormalSummon(p, gran, asSet: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var legal = Legal(e);
                var offered = legal.Contains(oppSet) && legal.Contains(oppSetMon) && legal.Contains(mySet) &&
                              legal.Contains(field) && !legal.Contains(oppUp) && !legal.Contains(gran);
                var picked = offered && e.TrySelectEffectTarget(oppSet);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Granmarg: Tribute Summoned — targets a Set card on either field (monster, Spell/Trap, Field Zone); " +
                      "the opponent's Set Mirror Force is destroyed",
                    summoned && Where(e, fodder) == "P.gy" && offered && picked && Where(e, oppSet) == "O.gy" &&
                    Where(e, oppSetMon) == "O.mz" && Where(e, mySet) == "P.stz" && !e.IsAwaitingEffectTarget,
                    $"summoned={summoned} legal=[{Names(legal)}] oppSet={Where(e, oppSet)} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var oppSet = PlaceSetTrap(e, e.Opponent, MirrorForce, 0);
                var gran = PutInHand(e, p, Granmarg);
                var set = e.TryNormalSummon(p, gran, asSet: true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Granmarg: a Tribute Set is not a Tribute Summon — no trigger",
                    set && Where(e, oppSet) == "O.stz" && !e.IsAwaitingEffectTarget, State(e));
            }
            {
                // Resolution re-check (Lua tc:IsFacedown()): a target turned face-up is not destroyed.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var gran = PlaceMonster(e, p, Granmarg, 0, BattlePosition.Attack, true);
                var target = PlaceSetTrap(e, e.Opponent, MirrorForce, 0);
                var clause = CardTextEffectCompiler.Compile(db.Get(Granmarg))?.ClauseList.FirstOrDefault();
                target.FaceUp = true;
                bool a = false, b = false;
                if (clause != null) TextEffectRuntime.ApplyClause(e, p, gran, clause, target, ref a, ref b);
                Check("Granmarg: a target that is face-up at resolution is not destroyed",
                    clause != null && Where(e, target) == "O.stz", Where(e, target));
            }
        }

        static void GranmargAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            o.Hand.Clear();
            PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var aiSet = PlaceSetTrap(e, o, TrapHole, 0);
            var playerSet = PlaceSetTrap(e, p, PotOfGreed, 0);
            var gran = PutInHand(e, o, Granmarg);
            var summoned = e.TryNormalSummon(o, gran, asSet: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            Check("Granmarg (AI): targets the player's Set card, not its own",
                main && summoned && Where(e, playerSet) == "P.gy" && Where(e, aiSet) == "O.stz" &&
                !e.IsAwaitingEffectTarget,
                $"main={main} summoned={summoned} playerSet={Where(e, playerSet)} aiSet={Where(e, aiSet)} {State(e)}");
        }

        // ───────────────────────────── Good Goblin Housekeeping ─────────────────────────────

        static void HousekeepingDrawsAndReturns(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, HarpieLady, FiendKraken, SilverFang, Gazelle });
                p.Graveyard.Add(e.CreateCardInstance(GoblinHousekeeping));
                p.Graveyard.Add(e.CreateCardInstance(GoblinHousekeeping));
                var kept = PutInHand(e, p, MysticalElf);
                var ggh = PlaceSetTrap(e, p, GoblinHousekeeping, 0);
                var act = e.TryActivateSpellTrap(p, ggh, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 4 && Legal(e).Contains(kept);
                var picked = asked && e.TrySelectEffectTarget(kept);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Good Goblin Housekeeping: 2 copies in the GY — draws 3, then the chosen hand card goes to the Deck bottom",
                    act && asked && picked && p.Hand.Count == 3 && !p.Hand.Contains(kept) && p.Deck.Count == 3 &&
                    p.Deck[p.Deck.Count - 1] == MysticalElf && p.Deck[0] == SilverFang &&
                    Where(e, ggh) == "P.gy" && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"act={act} asked={asked} hand={p.Hand.Count} deck=[{string.Join(",", p.Deck.ToArray())}] {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, HarpieLady });
                PutInHand(e, p, MysticalElf);
                var ggh = PlaceSetTrap(e, p, GoblinHousekeeping, 0);
                var act = e.TryActivateSpellTrap(p, ggh, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2;
                e.CancelEffectTargeting();
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Good Goblin Housekeeping: none in the GY draws 1; CANCEL at the mandatory pick still returns a card",
                    act && asked && p.Hand.Count == 1 && p.Deck.Count == 2 && p.Deck[0] == HarpieLady &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"act={act} asked={asked} hand={p.Hand.Count} deck=[{string.Join(",", p.Deck.ToArray())}] {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                e.Player.Deck.Clear();
                var ggh = PlaceSetTrap(e, e.Player, GoblinHousekeeping, 0);
                Check("Good Goblin Housekeeping: an empty Deck — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, ggh, fromHand: false), State(e));
            }
        }

        static void HousekeepingAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var o = e.Opponent;
            o.Hand.Clear();
            o.Graveyard.Clear();
            o.Deck.Clear();
            o.Deck.AddRange(new[] { Celtic, HarpieLady, FiendKraken });
            o.Graveyard.Add(e.CreateCardInstance(GoblinHousekeeping));
            PutInHand(e, o, MysticalElf);
            var ggh = PlaceSetTrap(e, o, GoblinHousekeeping, 0);
            var act = e.TryActivateSpellTrap(o, ggh, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            Check("Good Goblin Housekeeping (AI): draws 2, returns 1 card by itself, no prompt left",
                main && act && o.Hand.Count == 2 && o.Deck.Count == 2 && o.Deck[0] == FiendKraken &&
                !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                $"main={main} act={act} hand={o.Hand.Count} deck=[{string.Join(",", o.Deck.ToArray())}] {State(e)}");
        }

        // ───────────────────────────── The Big March of Animals ─────────────────────────────

        static void BigMarchBoostsFaceUpBeasts(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var fang = PlaceMonster(e, p, SilverFang, 0, BattlePosition.Attack, true);
                var gazelle = PlaceMonster(e, p, Gazelle, 1, BattlePosition.Defense, true);
                var koala = PlaceMonster(e, p, BigKoala, 2, BattlePosition.Defense, false);
                var harpie = PlaceMonster(e, p, HarpieLady, 3, BattlePosition.Attack, true);
                var ox = PlaceMonster(e, p, BattleOx, 4, BattlePosition.Attack, true);
                var oppBeast = PlaceMonster(e, e.Opponent, SilverFang, 0, BattlePosition.Attack, true);
                var march = PutInHand(e, p, BigMarch);
                var act = e.TryActivateSpellTrap(p, march, fromHand: true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var ok = fang.CurrentAtk == 1600 && gazelle.CurrentAtk == 1900 && koala.UntilEndOfTurnAtk == 0 &&
                         harpie.CurrentAtk == 1300 && ox.CurrentAtk == 1700 && oppBeast.CurrentAtk == 1200;
                Check("The Big March of Animals: 2 face-up Beasts → each gains 400; Set Beast, Winged Beast, Beast-Warrior " +
                      "and the opponent's Beast unchanged",
                    act && ok && Where(e, march) == "P.gy" && !e.Chain.HasLinks,
                    $"act={act} fang={fang.CurrentAtk} gazelle={gazelle.CurrentAtk} koala+={koala.UntilEndOfTurnAtk} " +
                    $"harpie={harpie.CurrentAtk} ox={ox.CurrentAtk} opp={oppBeast.CurrentAtk} {State(e)}");
                var later = PlaceMonster(e, p, Gazelle, 2, BattlePosition.Attack, true);
                Check("The Big March of Animals: the value is fixed at resolution (a later Beast gains nothing)",
                    later.CurrentAtk == 1500 && fang.CurrentAtk == 1600, $"later={later.CurrentAtk} fang={fang.CurrentAtk}");
                var next = ReachOpponentMain(e);
                Check("The Big March of Animals: the gain ends with the turn",
                    next && fang.UntilEndOfTurnAtk == 0 && gazelle.UntilEndOfTurnAtk == 0,
                    $"next={next} fang+={fang.UntilEndOfTurnAtk} gazelle+={gazelle.UntilEndOfTurnAtk}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, BigKoala, 0, BattlePosition.Defense, false);
                PlaceMonster(e, p, HarpieLady, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, BattleOx, 2, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, SilverFang, 0, BattlePosition.Attack, true);
                var march = PutInHand(e, p, BigMarch);
                Check("The Big March of Animals: no face-up Beast you control (Set Beast, Winged Beast, Beast-Warrior, " +
                      "the opponent's Beast) — cannot activate",
                    !e.CanActivateSpellTrap(p, march, fromHand: true), State(e));
            }
        }

        // ───────────────────────────── Elemental Burst ─────────────────────────────

        static DuelEngine BurstBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out CardInstance burst,
            out CardInstance[] mine, out CardInstance[] theirs)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            mine = new[]
            {
                PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Attack, true),
                PlaceMonster(e, p, FiendKraken, 1, BattlePosition.Defense, false),
                PlaceMonster(e, p, FlameManipulator, 2, BattlePosition.Attack, true),
                PlaceMonster(e, p, HinotamaSoul, 3, BattlePosition.Attack, true),
                PlaceMonster(e, p, Celtic, 4, BattlePosition.Attack, true),
            };
            var field = e.CreateCardInstance(Mountain);
            field.FaceUp = true;
            o.FieldSpellZone.Occupant = field;
            theirs = new[]
            {
                PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true),
                PlaceMonster(e, o, BigKoala, 1, BattlePosition.Defense, false),
                PlaceSetTrap(e, o, PotOfGreed, 0),
                field,
            };
            burst = PlaceSetTrap(e, p, ElementalBurst, 0);
            return e;
        }

        static void ElementalBurstTributesOneEach(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = BurstBoard(db, pDeck, aDeck, out var burst, out var mine, out var theirs);
                var (harpie, kraken, flame, hinotama, celtic) = (mine[0], mine[1], mine[2], mine[3], mine[4]);
                var act = e.TryActivateSpellTrap(e.Player, burst, fromHand: false);
                var first = Legal(e);
                var firstOk = first.Count == 5;
                var p1 = firstOk && e.TrySelectEffectTarget(hinotama);
                var second = Legal(e);
                var secondOk = second.Count == 3 && !second.Contains(flame) && second.Contains(kraken);
                var p2 = secondOk && e.TrySelectEffectTarget(kraken);
                var p3 = p2 && e.TrySelectEffectTarget(harpie);
                var p4 = p3 && e.TrySelectEffectTarget(celtic);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var tributed = Where(e, hinotama) == "P.gy" && Where(e, kraken) == "P.gy" && Where(e, harpie) == "P.gy" &&
                               Where(e, celtic) == "P.gy" && Where(e, flame) == "P.mz";
                var cleared = theirs.All(t => Where(e, t) == "O.gy");
                Check("Elemental Burst: the player Tributes one WIND, WATER (Set), FIRE and EARTH monster — a second FIRE " +
                      "is no longer offered; every card the opponent controls is destroyed",
                    act && firstOk && p1 && secondOk && p4 && tributed && cleared && Where(e, burst) == "P.gy" &&
                    !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"act={act} first=[{Names(first)}] second=[{Names(second)}] p4={p4} " +
                    $"theirs=[{string.Join(",", theirs.Select(t => Where(e, t)))}] flame={Where(e, flame)} {State(e)}");
            }
            {
                var e = BurstBoard(db, pDeck, aDeck, out var burst, out var mine, out var theirs);
                var act = e.TryActivateSpellTrap(e.Player, burst, fromHand: false);
                e.CancelEffectTargeting();
                Check("Elemental Burst: CANCEL before any Tribute — not activated, nothing Tributed, the card stays Set",
                    act && mine.All(m => Where(e, m) == "P.mz") && Where(e, burst) == "P.stz" && !burst.FaceUp &&
                    theirs.All(t => Where(e, t) == "O.mz" || Where(e, t) == "O.stz" ||
                                    ReferenceEquals(e.Opponent.FieldSpellZone.Occupant, t)) &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks, State(e));
            }
            {
                var e = BurstBoard(db, pDeck, aDeck, out var burst, out var mine, out var theirs);
                var act = e.TryActivateSpellTrap(e.Player, burst, fromHand: false);
                var p1 = e.TrySelectEffectTarget(mine[2]); // Flame Manipulator (FIRE)
                e.CancelEffectTargeting();
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var gy = mine.Count(m => Where(e, m) == "P.gy");
                Check("Elemental Burst: CANCEL after a Tribute — the rest is paid (one of each), the card resolves",
                    act && p1 && gy == 4 && Where(e, mine[3]) == "P.mz" && theirs.All(t => Where(e, t) == "O.gy") &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"act={act} p1={p1} gy={gy} hinotama={Where(e, mine[3])} {State(e)}");
            }
        }

        static void ElementalBurstLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = BurstBoard(db, pDeck, aDeck, out var burst, out var mine, out _);
                e.Player.MonsterZones[1].Occupant = null; // no WATER
                PlaceMonster(e, e.Player, MysticalElf, 1, BattlePosition.Attack, true);
                Check("Elemental Burst: no WATER monster (a LIGHT one instead) — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, burst, fromHand: false), State(e));
            }
            {
                var e = BurstBoard(db, pDeck, aDeck, out var burst, out _, out _);
                var o = e.Opponent;
                for (var i = 0; i < o.MonsterZones.Length; i++) o.MonsterZones[i].Occupant = null;
                for (var i = 0; i < o.SpellTrapZones.Length; i++) o.SpellTrapZones[i].Occupant = null;
                o.FieldSpellZone.Occupant = null;
                Check("Elemental Burst: the opponent controls no card — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, burst, fromHand: false), State(e));
            }
        }

        static void ElementalBurstAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            var flame = PlaceMonster(e, o, FlameManipulator, 0, BattlePosition.Attack, true);
            var hinotama = PlaceMonster(e, o, HinotamaSoul, 1, BattlePosition.Attack, true);
            var harpie = PlaceMonster(e, o, HarpieLady, 2, BattlePosition.Attack, true);
            var kraken = PlaceMonster(e, o, FiendKraken, 3, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 4, BattlePosition.Attack, true);
            var victim = PlaceMonster(e, p, Gazelle, 0, BattlePosition.Attack, true);
            var victimSet = PlaceSetTrap(e, p, PotOfGreed, 0);
            var burst = PlaceSetTrap(e, o, ElementalBurst, 0);
            var act = e.TryActivateSpellTrap(o, burst, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var paid = new[] { hinotama, harpie, kraken, celtic }.All(m => Where(e, m) == "O.gy") &&
                       Where(e, flame) == "O.mz";
            Check("Elemental Burst (AI): Tributes one of each Attribute (the weaker FIRE), destroys the player's cards",
                main && act && paid && Where(e, victim) == "P.gy" && Where(e, victimSet) == "P.gy" &&
                !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                $"main={main} act={act} flame={Where(e, flame)} hinotama={Where(e, hinotama)} harpie={Where(e, harpie)} " +
                $"kraken={Where(e, kraken)} celtic={Where(e, celtic)} victim={Where(e, victim)} {State(e)}");
        }

        /// <summary>
        /// Seated in the opponent's attack window: the four Tributes are paid as it is activated (picked one by one, Tributed
        /// together), never skipped; CANCEL during the picks Tributes nothing and leaves the card Set. A Chain Link answer is
        /// refused (the engine pays no pick cost while chaining; a residual).
        /// </summary>
        static void ElementalBurstInAWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var cancel in new[] { false, true })
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mine = new[]
                {
                    PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Defense, true),
                    PlaceMonster(e, p, FiendKraken, 1, BattlePosition.Defense, true),
                    PlaceMonster(e, p, HinotamaSoul, 2, BattlePosition.Defense, true),
                    PlaceMonster(e, p, Celtic, 3, BattlePosition.Defense, true),
                    PlaceMonster(e, p, FlameManipulator, 4, BattlePosition.Defense, true),
                };
                var attacker = PlaceMonster(e, o, BigKoala, 0, BattlePosition.Attack, true);
                var burst = PlaceSetTrap(e, p, ElementalBurst, 0);
                e.NotifyPublic();
                var atk = e.TryAttack(o, attacker, mine[3]);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, burst, fromHand: false);
                var first = Legal(e);
                var p1 = act && e.TrySelectEffectTarget(mine[2]); // Hinotama Soul (FIRE)
                var second = Legal(e);
                var noSecondFire = p1 && !second.Contains(mine[4]) && second.Count == 3;
                var tributedMid = mine.Count(m => Where(e, m) == "P.gy");
                if (cancel)
                {
                    e.CancelEffectTargeting();
                    var undone = Where(e, burst) == "P.stz" && !burst.FaceUp && mine.All(m => Where(e, m) == "P.mz") &&
                                 !e.IsAwaitingEffectTarget;
                    Check("Elemental Burst: seated in the attack window — CANCEL during the Tribute picks Tributes nothing; " +
                          "the card stays Set",
                        battle && atk && window && act && first.Count == 5 && noSecondFire && tributedMid == 0 && undone,
                        $"battle={battle} atk={atk} window={window} act={act} first=[{Names(first)}] " +
                        $"second=[{Names(second)}] burst={Where(e, burst)} {State(e)}");
                    DrainCombat(e);
                    continue;
                }

                var p2 = noSecondFire && e.TrySelectEffectTarget(mine[0]);
                var p3 = p2 && e.TrySelectEffectTarget(mine[1]);
                var p4 = p3 && e.TrySelectEffectTarget(mine[3]);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var paid = new[] { mine[0], mine[1], mine[2], mine[3] }.All(m => Where(e, m) == "P.gy") &&
                           Where(e, mine[4]) == "P.mz";
                DrainCombat(e);
                Check("Elemental Burst: seated in the opponent's attack window — one of each Attribute is Tributed (together, " +
                      "after the last pick), then the attacker is destroyed",
                    battle && atk && window && act && first.Count == 5 && noSecondFire && tributedMid == 0 && p4 && paid &&
                    Where(e, attacker) == "O.gy" && Where(e, burst) == "P.gy" && !e.IsAwaitingEffectTarget,
                    $"battle={battle} atk={atk} window={window} act={act} first=[{Names(first)}] second=[{Names(second)}] " +
                    $"p4={p4} mine=[{string.Join(",", mine.Select(m => Where(e, m)))}] attacker={Where(e, attacker)} " +
                    $"burst={Where(e, burst)} {State(e)}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                e.Opponent.Hand.Clear();
                PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Defense, true);
                PlaceMonster(e, p, FiendKraken, 1, BattlePosition.Defense, true);
                var lone = PlaceMonster(e, p, Celtic, 3, BattlePosition.Defense, true);
                var attacker = PlaceMonster(e, e.Opponent, BigKoala, 0, BattlePosition.Attack, true);
                var burst = PlaceSetTrap(e, p, ElementalBurst, 0);
                e.NotifyPublic();
                var atk = e.TryAttack(e.Opponent, attacker, lone);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                // With nothing else to answer the attack, no window opens at all.
                var offered = window && e.CanActivateSpellTrap(p, burst, fromHand: false);
                Check("Elemental Burst: no FIRE monster — not offered in the attack window",
                    battle && atk && !offered && Where(e, burst) == "P.stz" && !burst.FaceUp, $"window={window} {State(e)}");
                DrainCombat(e);
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                var main = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Defense, true);
                PlaceMonster(e, p, FiendKraken, 1, BattlePosition.Defense, true);
                PlaceMonster(e, p, HinotamaSoul, 2, BattlePosition.Defense, true);
                PlaceMonster(e, p, Celtic, 3, BattlePosition.Defense, true);
                PlaceMonster(e, o, BigKoala, 0, BattlePosition.Attack, true);
                var burst = PlaceSetTrap(e, p, ElementalBurst, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                var act = e.TryActivateSpellTrap(o, pot, fromHand: true);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p &&
                             e.PendingResponse.Timing == ResponseTiming.ChainResponse;
                // With nothing else to chain, no chain window opens for the player at all.
                var offered = window && e.CanActivateSpellTrap(p, burst, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Elemental Burst: not chained to a Chain Link (its Tribute pick is not paid while chaining)",
                    main && act && !offered && Where(e, burst) == "P.stz" && !burst.FaceUp &&
                    p.MonstersOnField().Count() == 4, $"main={main} act={act} window={window} {State(e)}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/amt={c.Amount}/race={c.RaceFilter}/cond={c.ActivationCondition}" +
                             $"/trib={c.RequiresTributeCount}:{c.TributeOneEachAttribute}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
