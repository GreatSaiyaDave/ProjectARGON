using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SpellTrapSealsAndImmunity": Talisman of Spell Sealing, Talisman of Trap Sealing, Sonic Jammer, Dust Barrier
    /// and Armor Break — compile shapes, look-alike sentences that stay uncompiled, and mini-duels through the real entry
    /// points (TryActivateSpellTrap, TryActivateChainResponse, MonsterEffects.OnFlipSummoned, RefreshBoard, turn changes,
    /// SpellTrapEffects.AiAutoRespond, SimpleAi).
    /// </summary>
    public static class SpellTrapSealsAndImmunityRegressionTests
    {
        const int TalismanOfSpellSealing = 71983925;
        const int TalismanOfTrapSealing = 19312169;
        const int SonicJammer = 84550200;
        const int DustBarrier = 31476755;
        const int ArmorBreak = 79649195;

        const int SealmasterMeisei = 2468169;
        const int PotOfGreed = 55144522;
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int LegendarySword = 61854111; // Equip Spell (Warrior), no Graveyard trigger
        const int MagicJammer = 77414722;
        const int JarOfGreed = 83968380;
        const int MirrorForce = 44095762;
        const int GravityBind = 85742772;
        const int Yami = 59197169;
        const int Sogen = 86318356;
        const int Celtic = 91152256;      // Normal Warrior Lv4 1400/1200
        const int MysticalElf = 15025844; // Normal Spellcaster Lv4 800/2000
        const int BlueEyes = 89631139;    // Normal Dragon Lv8 3000/2500
        const int Sangan = 26202165;      // Effect Fiend Lv3 1000/600
        const int ZombieTiger = 47693640; // Union
        const int DecayedCommander = 10209545;

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("SpellTrapSealsAndImmunity: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            TalismanDuels(db, pDeck, aDeck, Check);
            SonicJammerDuels(db, pDeck, aDeck, Check);
            DustBarrierDuels(db, pDeck, aDeck, Check);
            ArmorBreakDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} side={c.Side} amt={c.Amount} cond={c.ActivationCondition}:{c.ConditionName} " +
                    $"name={c.RequiresFaceUpName} mz={c.NamedFaceUpInYourMonsterZone} equipOnly={c.NegateRespondsToEquipSpellOnly}"));

        static CompiledCardProgram CompileText(string text, string type, string race) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990178, name = "Near-miss Card", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = race, desc = text, atk = 350, def = 650, level = 2
            });

        static bool HasAction(CompiledCardProgram p, Func<EffectClause, bool> pred) =>
            p != null && p.ClauseList.Exists(c => c != null && pred(c));

        static bool Offers(DuelEngine e, DuelistState who, CardInstance card) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who && e.PendingResponse.LegalCards.Contains(card);

        static string State(DuelEngine e) =>
            (e.PendingResponse == null
                ? "no window"
                : $"timing={e.PendingResponse.Timing} responder={e.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", e.PendingResponse.LegalCards.Select(c => c?.Name))}]") +
            $" chain={e.Chain.Count} pick={e.IsAwaitingEffectTarget} turn={e.TurnNumber}:{e.TurnPlayer?.Name} {e.Phase}";

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }
        }

        static DuelEngine PlayerFirstMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            Empty(e);
            return e;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            Empty(e);
            return e;
        }

        /// <summary>End turns until <paramref name="who"/>'s Main Phase of a later turn (both players pass every window).</summary>
        static bool NextMainOf(DuelEngine e, DuelistState who)
        {
            var start = e.TurnNumber;
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnNumber > start && e.TurnPlayer == who && e.InMainPhase && !e.IsAwaitingResponse) return true;
                if (e.TurnNumber > start && e.TurnPlayer == who && e.InMainPhase) continue;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            PassAll(e);
            return e.TurnNumber > start && e.TurnPlayer == who && e.InMainPhase;
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceFaceUpField(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static bool InSpellTrap(DuelistState who, CardInstance c) => who.SpellTrapZones.Any(z => z.Occupant == c);

        static bool InMonsterZone(DuelistState who, CardInstance c) => who.MonsterZones.Any(z => z.Occupant == c);

        static void LeaveMonsterZone(DuelistState who, CardInstance c)
        {
            foreach (var z in who.MonsterZones)
                if (z.Occupant == c)
                    z.Occupant = null;
        }

        static bool RunAiTurn(DuelEngine e, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            var steps = 0;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                while (steps < maxSteps)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                    steps++;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            foreach (var (id, trap) in new[] { (TalismanOfSpellSealing, false), (TalismanOfTrapSealing, true) })
            {
                var p = Prog(db, id);
                var lockAction = trap
                    ? EffectActionKind.ContinuousCannotActivateTraps
                    : EffectActionKind.ContinuousCannotActivateSpells;
                var negAction = trap
                    ? EffectActionKind.ContinuousNegateTrapEffectsOnField
                    : EffectActionKind.ContinuousNegateSpellEffectsOnField;
                var ok = p != null && p.FullyCompiled && p.ClauseList.Count == 3 && Implemented(db, id) &&
                         HasAction(p, c => c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == lockAction &&
                                           c.ActivationCondition ==
                                           SimpleTriggersAndSwarmRuntime.AllNamedFaceUpInYourMonsterZones &&
                                           c.ConditionName == "Sealmaster Meisei") &&
                         HasAction(p, c => c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == negAction &&
                                           !c.ExceptThisCard) &&
                         HasAction(p, c => c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp &&
                                           c.RequiresFaceUpName == "Sealmaster Meisei" && c.NamedFaceUpInYourMonsterZone);
                Check($"compile: Talisman of {(trap ? "Trap" : "Spell")} Sealing = lock + negation + Meisei-bound " +
                      "self-destruction, activation needs a face-up Meisei in your Monster Zone", ok, Describe(p));
            }

            {
                var p = Prog(db, SonicJammer);
                Check("compile: Sonic Jammer = FLIP, the opponent's Spell Card lock through the next turn",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && Implemented(db, SonicJammer) &&
                    p.ClauseList[0].Timing == EffectTiming.Flip &&
                    p.ClauseList[0].Action == EffectActionKind.OpponentCannotActivateSpellCardsUntilEndOfNextTurn,
                    Describe(p));
            }

            {
                var p = Prog(db, DustBarrier);
                Check("compile: Dust Barrier = Normal Monster shield + destroyed on your 2nd Standby Phase",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 2 && Implemented(db, DustBarrier) &&
                    HasAction(p, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                      c.Action == EffectActionKind.NormalMonstersUnaffectedByOpponentSpells) &&
                    HasAction(p, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                      c.Action == EffectActionKind.DestroyThisOnYourNthStandbyAfterActivation &&
                                      c.Amount == 2),
                    Describe(p));
            }

            {
                var p = Prog(db, ArmorBreak);
                var c = p != null && p.FullyCompiled && p.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("compile: Armor Break = Counter Trap negating an Equip Spell Card's activation and destroying it",
                    c != null && Implemented(db, ArmorBreak) && c.Timing == EffectTiming.ChainLinkActivated &&
                    c.Action == EffectActionKind.NegateActivation && c.NegateRespondsToSpell && !c.NegateRespondsToTrap &&
                    c.NegateCardActivationOnly && c.NegateRespondsToEquipSpellOnly && c.DestroyNegatedCard &&
                    !c.RequiresDiscardCost && c.PayLpAmount == 0,
                    Describe(p));
            }
        }

        // ───────────────────────── near misses ─────────────────────────

        static void NearMisses(CheckFn Check)
        {
            var mixed = CompileText(
                "You can only activate this card while there is a face-up \"Sealmaster Meisei\" on your side of the field. " +
                "While this card is face-up on the field, Spell Cards cannot be activated and the effects of face-up Trap " +
                "Cards on the field are negated. When \"Sealmaster Meisei\" is removed from the field, destroy this card.",
                "Spell Card", "Continuous");
            Check("near miss: a Talisman text that locks Spells but negates Traps is not compiled",
                mixed == null || !HasAction(mixed, c => c.NamedFaceUpInYourMonsterZone), Describe(mixed));

            var noFlip = CompileText(
                "Your opponent cannot activate any Spell Cards until the end of the End Phase of the next turn.",
                "Spell Card", "Normal");
            var trapJammer = CompileText(
                "FLIP: Your opponent cannot activate any Trap Cards until the end of the End Phase of the next turn.",
                "Flip Effect Monster", "Machine");
            Check("near miss: Sonic Jammer's lock needs the FLIP sentence and Spell Cards",
                !HasAction(noFlip, c => c.Action == EffectActionKind.OpponentCannotActivateSpellCardsUntilEndOfNextTurn) &&
                !HasAction(trapJammer, c => c.Action == EffectActionKind.OpponentCannotActivateSpellCardsUntilEndOfNextTurn),
                Describe(noFlip) + " // " + Describe(trapJammer));

            var effectBarrier = CompileText(
                "Face-up Effect Monsters on the field are unaffected by your opponent's Spell Cards. This card is destroyed " +
                "during your second Standby Phase after activation.", "Spell Card", "Continuous");
            var thirdStandby = CompileText(
                "Face-up Normal Monsters on the field are unaffected by your opponent's Spell Cards. This card is destroyed " +
                "during your third Standby Phase after activation.", "Spell Card", "Continuous");
            Check("near miss: Dust Barrier's shield is Normal Monsters only and its countdown the 2nd Standby Phase",
                !HasAction(effectBarrier, c => c.Action == EffectActionKind.NormalMonstersUnaffectedByOpponentSpells) &&
                !HasAction(thirdStandby, c => c.Action == EffectActionKind.NormalMonstersUnaffectedByOpponentSpells ||
                                              c.Action == EffectActionKind.DestroyThisOnYourNthStandbyAfterActivation),
                Describe(effectBarrier) + " // " + Describe(thirdStandby));

            var fieldBreak = CompileText("Negate the activation of a Field Spell Card and destroy it.", "Trap Card", "Counter");
            Check("near miss: another card kind than Equip Spell does not get the Equip-only answer",
                !HasAction(fieldBreak, c => c.NegateRespondsToEquipSpellOnly), Describe(fieldBreak));
        }

        // ───────────────────────── Talismans ─────────────────────────

        static void TalismanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Activation condition: a face-up Meisei in YOUR Monster Zone.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var talisman = PlaceSetTrap(e, p, TalismanOfSpellSealing, 0);
                var none = !e.CanActivateSpellTrap(p, talisman, fromHand: false);
                PlaceMonster(e, o, SealmasterMeisei, 0, BattlePosition.Attack, true);
                var oppsOnly = !e.CanActivateSpellTrap(p, talisman, fromHand: false);
                var mine = PlaceMonster(e, p, SealmasterMeisei, 1, BattlePosition.Defense, false);
                var setOnly = !e.CanActivateSpellTrap(p, talisman, fromHand: false);
                mine.FaceUp = true;
                mine.Position = BattlePosition.Attack;
                var ok = e.CanActivateSpellTrap(p, talisman, fromHand: false);
                Check("Talisman of Spell Sealing: activatable only with a face-up Meisei in your own Monster Zone",
                    none && oppsOnly && setOnly && ok,
                    $"none={none} oppsOnly={oppsOnly} setOnly={setOnly} mine={ok}");
            }

            {
                // Talisman of Spell Sealing resolves: both players' Spells are locked (hand and Set), face-up Spells (Field
                // Zone included) are negated, Traps stay free; Meisei leaving destroys it.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var meisei = PlaceMonster(e, p, SealmasterMeisei, 0, BattlePosition.Attack, true);
                var talisman = PlaceSetTrap(e, p, TalismanOfSpellSealing, 0);
                var myPot = PutInHand(e, p, PotOfGreed);
                var oppPot = PutInHand(e, o, PotOfGreed);
                var oppMst = PlaceSetTrap(e, o, Mst, 0);
                var jar = PlaceSetTrap(e, p, JarOfGreed, 1);
                var yami = PlaceFaceUpField(e, o, Yami);
                var act = e.TryActivateSpellTrap(p, talisman, fromHand: false);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                var up = talisman.FaceUp && InSpellTrap(p, talisman);
                var myLocked = !e.CanActivateSpellTrap(p, myPot, fromHand: true) &&
                               ContinuousNegationRuntime.ActivationLocked(e, p, myPot, out _);
                var oppLocked = ContinuousNegationRuntime.ActivationLocked(e, o, oppPot, out _) &&
                                ContinuousNegationRuntime.ActivationLocked(e, o, oppMst, out _);
                var trapFree = !ContinuousNegationRuntime.ActivationLocked(e, p, jar, out _);
                var fieldNegated = yami.IsNegated;
                Check("Talisman of Spell Sealing: Spells cannot be activated (both players, hand and Set), face-up Field " +
                      "Spells are negated, Traps stay free",
                    act && up && myLocked && oppLocked && trapFree && fieldNegated && !talisman.IsNegated,
                    $"act={act} up={up} myLocked={myLocked} oppLocked={oppLocked} trapFree={trapFree} " +
                    $"yamiNegated={fieldNegated} {State(e)}");

                LeaveMonsterZone(p, meisei);
                p.Graveyard.Add(meisei);
                FieldSpellEffects.RefreshBoard(e);
                var gone = !InSpellTrap(p, talisman) && p.Graveyard.Contains(talisman);
                FieldSpellEffects.RefreshBoard(e);
                var freed = !ContinuousNegationRuntime.ActivationLocked(e, p, myPot, out _) && !yami.IsNegated;
                Check("Talisman of Spell Sealing: destroyed once Meisei leaves; the lock and negation end",
                    gone && freed, $"gone={gone} freed={freed} yamiNegated={yami.IsNegated}");
            }

            {
                // Talisman of Trap Sealing from the hand: Traps locked (both players), face-up Continuous Traps negated,
                // Spells stay free; destroyed when Meisei is no longer face-up in ITS CONTROLLER's Monster Zone (a Meisei
                // the opponent controls does not keep it).
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var meisei = PlaceMonster(e, p, SealmasterMeisei, 0, BattlePosition.Attack, true);
                var talisman = PutInHand(e, p, TalismanOfTrapSealing);
                var mirror = PlaceSetTrap(e, o, MirrorForce, 0);
                var jar = PlaceSetTrap(e, p, JarOfGreed, 0);
                var bind = PlaceFaceUpSpellTrap(e, o, GravityBind, 1);
                var myPot = PutInHand(e, p, PotOfGreed);
                var act = e.TryActivateSpellTrap(p, talisman, fromHand: true);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                var up = talisman.FaceUp && InSpellTrap(p, talisman);
                var trapsLocked = ContinuousNegationRuntime.ActivationLocked(e, o, mirror, out _) &&
                                  ContinuousNegationRuntime.ActivationLocked(e, p, jar, out _) &&
                                  !e.CanActivateSpellTrap(p, jar, fromHand: false);
                var spellFree = e.CanActivateSpellTrap(p, myPot, fromHand: true);
                Check("Talisman of Trap Sealing: Traps cannot be activated (both players), face-up Traps are negated, " +
                      "Spells stay free",
                    act && up && trapsLocked && spellFree && bind.IsNegated,
                    $"act={act} up={up} trapsLocked={trapsLocked} spellFree={spellFree} bindNegated={bind.IsNegated} " +
                    State(e));

                // Control of Meisei switches to the opponent: still face-up on the field, but not in your Monster Zone.
                LeaveMonsterZone(p, meisei);
                o.MonsterZones[2].Occupant = meisei;
                FieldSpellEffects.RefreshBoard(e);
                var gone = !InSpellTrap(p, talisman) && p.Graveyard.Contains(talisman);
                Check("Talisman of Trap Sealing: destroyed when Meisei is face-up only on the opponent's side",
                    gone, $"gone={gone} st={InSpellTrap(p, talisman)}");
            }

            {
                // Talisman of Spell Sealing chained to a Pot of Greed: the Spell resolving from the S/T Zone is negated.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, SealmasterMeisei, 0, BattlePosition.Attack, true);
                var talisman = PlaceSetTrap(e, p, TalismanOfSpellSealing, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                var hand = o.Hand.Count;
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = Offers(e, p, talisman);
                var chained = offered && e.TryActivateChainResponse(p, talisman);
                PassAll(e);
                Check("Talisman of Spell Sealing chained to Pot of Greed: Pot resolves negated (no draw)",
                    act && offered && chained && talisman.FaceUp && InSpellTrap(p, talisman) &&
                    o.Graveyard.Contains(pot) && o.Hand.Count == hand - 1,
                    $"act={act} offered={offered} chained={chained} hand={o.Hand.Count} was={hand} {State(e)}");
            }
        }

        // ───────────────────────── Sonic Jammer ─────────────────────────

        static void SonicJammerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Flipped during your turn: the opponent cannot activate Spell Cards this turn and their next turn.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jammer = PlaceMonster(e, p, SonicJammer, 0, BattlePosition.Attack, true);
                var oppPot = PutInHand(e, o, PotOfGreed);
                var oppMst = PlaceSetTrap(e, o, Mst, 0);
                var oppJar = PlaceSetTrap(e, o, JarOfGreed, 1);
                var oppFaceUp = PlaceFaceUpSpellTrap(e, o, GravityBind, 2);
                var oppField = PlaceFaceUpField(e, o, Sogen);
                var myPot = PutInHand(e, p, PotOfGreed);
                var flipTurn = e.TurnNumber;
                MonsterEffects.OnFlipSummoned(e, p, jammer);
                PassAll(e);
                var lockedNow = ContinuousNegationRuntime.ActivationLocked(e, o, oppPot, out _) &&
                                ContinuousNegationRuntime.ActivationLocked(e, o, oppMst, out _);
                var trapFree = !ContinuousNegationRuntime.ActivationLocked(e, o, oppJar, out _) && oppFaceUp != null;
                var faceUpFree = !SpellTrapSealsAndImmunityRuntime.ActivationLocked(e, o, oppField, out _);
                var mineFree = e.CanActivateSpellTrap(p, myPot, fromHand: true);
                var oppTurn = NextMainOf(e, o);
                var lockedNext = oppTurn && e.TurnNumber == flipTurn + 1 &&
                                 !e.CanActivateSpellTrap(o, oppPot, fromHand: true) &&
                                 !e.CanActivateSpellTrap(o, oppMst, fromHand: false);
                var myTurn = NextMainOf(e, p);
                var freeAfter = myTurn && !ContinuousNegationRuntime.ActivationLocked(e, o, oppPot, out _) &&
                                !ContinuousNegationRuntime.ActivationLocked(e, o, oppMst, out _);
                Check("Sonic Jammer (flipped in your turn): the opponent's Spell Cards (hand and Set) are locked through " +
                      "their next turn, then free; their Traps, face-up Spells and your Spells are not locked",
                    lockedNow && trapFree && faceUpFree && mineFree && lockedNext && freeAfter,
                    $"now={lockedNow} trapFree={trapFree} faceUpFree={faceUpFree} mineFree={mineFree} next={lockedNext} " +
                    $"after={freeAfter} {State(e)}");
            }

            {
                // Flipped during the opponent's turn: locked that turn and through your next turn; free on their next.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jammer = PlaceMonster(e, p, SonicJammer, 0, BattlePosition.Defense, true);
                var oppPot = PutInHand(e, o, PotOfGreed);
                var oppMst = PlaceSetTrap(e, o, Mst, 0);
                MonsterEffects.OnFlipSummoned(e, p, jammer);
                PassAll(e);
                var lockedNow = reached && !e.CanActivateSpellTrap(o, oppPot, fromHand: true);
                var myTurn = NextMainOf(e, p);
                var lockedMine = myTurn && ContinuousNegationRuntime.ActivationLocked(e, o, oppMst, out _);
                var oppTurn = NextMainOf(e, o);
                var freeAfter = oppTurn && e.CanActivateSpellTrap(o, oppPot, fromHand: true);
                Check("Sonic Jammer (flipped in the opponent's turn): locked that turn and your next turn, free on theirs",
                    lockedNow && lockedMine && freeAfter,
                    $"reached={reached} now={lockedNow} mine={lockedMine} after={freeAfter} {State(e)}");
            }

            // The AI does not activate a locked Spell (its Field Spell), and does without the lock.
            foreach (var locked in new[] { true, false })
            {
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jammer = PlaceMonster(e, p, SonicJammer, 0, BattlePosition.Attack, true);
                if (locked)
                {
                    MonsterEffects.OnFlipSummoned(e, p, jammer);
                    PassAll(e);
                }

                var reached = NextMainOf(e, o);
                o.Hand.Clear();
                var field = PutInHand(e, o, Sogen);
                var done = reached && RunAiTurn(e, 80);
                var played = o.FieldSpellZone.Occupant == field;
                Check($"Sonic Jammer: the AI {(locked ? "does not activate" : "activates")} its Field Spell " +
                      $"{(locked ? "under" : "without")} the lock",
                    reached && (locked ? !played && o.Hand.Contains(field) : played),
                    $"reached={reached} done={done} played={played} {State(e)}");
            }

            {
                // A Field Spell placed straight from the hand (the AR disk path) honours the lock too.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jammer = PlaceMonster(e, o, SonicJammer, 0, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, o, jammer);
                PassAll(e);
                var yami = PutInHand(e, p, Yami);
                var placed = e.TryPlaceFieldSpell(p, yami);
                Check("Sonic Jammer: placing a Field Spell from the hand is refused while locked",
                    !placed && p.Hand.Contains(yami) && p.FieldSpellZone.Occupant == null, State(e));
            }
        }

        // ───────────────────────── Dust Barrier ─────────────────────────

        static void DustBarrierDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Dark Hole: face-up Normal Monsters on BOTH sides survive; Effect and face-down monsters do not.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, DustBarrier, 0);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var sangan = PlaceMonster(e, p, Sangan, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 2, BattlePosition.Defense, false);
                var blueEyes = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, o, DarkHole);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, hole, fromHand: true);
                PassAll(e);
                Check("Dust Barrier vs the opponent's Dark Hole: face-up Normal Monsters on both sides survive, the Effect " +
                      "Monster and the face-down Normal Monster are destroyed",
                    act && InMonsterZone(p, celtic) && InMonsterZone(o, blueEyes) && !InMonsterZone(p, sangan) &&
                    !InMonsterZone(p, elf) && o.Graveyard.Contains(hole),
                    $"act={act} celtic={InMonsterZone(p, celtic)} blueEyes={InMonsterZone(o, blueEyes)} " +
                    $"sangan={InMonsterZone(p, sangan)} elf={InMonsterZone(p, elf)} {State(e)}");
            }

            {
                // Its controller's own Dark Hole is not shielded.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, DustBarrier, 0);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var blueEyes = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("Dust Barrier: its controller's own Spell (Dark Hole) still destroys Normal Monsters",
                    act && !InMonsterZone(p, celtic) && !InMonsterZone(o, blueEyes),
                    $"act={act} celtic={InMonsterZone(p, celtic)} blueEyes={InMonsterZone(o, blueEyes)} {State(e)}");
            }

            {
                // The opponent's Field Spell does not boost Normal Monsters (either side); its controller's own does.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, DustBarrier, 0);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var oppCeltic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceFaceUpField(e, o, Sogen);
                FieldSpellEffects.RefreshBoard(e);
                var shielded = celtic.CurrentAtk == 1400 && oppCeltic.CurrentAtk == 1400;
                o.FieldSpellZone.Occupant = null;
                PlaceFaceUpField(e, p, Sogen);
                FieldSpellEffects.RefreshBoard(e);
                var ownBoosts = celtic.CurrentAtk == 1600 && oppCeltic.CurrentAtk == 1600;
                Check("Dust Barrier: the opponent's Sogen does not boost face-up Normal Monsters; its controller's own does",
                    shielded && ownBoosts,
                    $"shielded={shielded} ownBoosts={ownBoosts} atk={celtic.CurrentAtk}/{oppCeltic.CurrentAtk}");
            }

            {
                // Activated in your turn: it survives your next Standby Phase and the opponent's, and is destroyed in your
                // 2nd Standby Phase after activation.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var barrier = PutInHand(e, p, DustBarrier);
                var act = e.TryActivateSpellTrap(p, barrier, fromHand: true);
                PassAll(e);
                var start = e.TurnNumber;
                var up = barrier.FaceUp && InSpellTrap(p, barrier);
                var t1 = NextMainOf(e, o) && InSpellTrap(p, barrier);
                var t2 = NextMainOf(e, p) && e.TurnNumber == start + 2 && InSpellTrap(p, barrier);
                var t3 = NextMainOf(e, o) && InSpellTrap(p, barrier);
                var t4 = NextMainOf(e, p) && e.TurnNumber == start + 4 && !InSpellTrap(p, barrier) &&
                         p.Graveyard.Contains(barrier);
                Check("Dust Barrier: destroyed during your 2nd Standby Phase after activation (not the 1st, not the " +
                      "opponent's)",
                    act && up && t1 && t2 && t3 && t4,
                    $"act={act} up={up} t1={t1} t2={t2} t3={t3} t4={t4} {State(e)}");
            }
        }

        // ───────────────────────── Armor Break ─────────────────────────

        static void ArmorBreakDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Legendary Sword from the hand: Armor Break negates and destroys it.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var armor = PlaceSetTrap(e, p, ArmorBreak, 0);
                var host = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var sword = PutInHand(e, o, LegendarySword);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, sword, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(host)) e.TrySelectEffectTarget(host);
                var offered = Offers(e, p, armor);
                var answered = offered && e.TryActivateChainResponse(p, armor);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Armor Break: negates the opponent's Equip Spell activation and destroys it",
                    act && offered && answered && o.Graveyard.Contains(sword) && sword.EquippedTo == null &&
                    host.CurrentAtk == 1400 && p.Graveyard.Contains(armor) && e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} swordGy={o.Graveyard.Contains(sword)} " +
                    $"eq={sword.EquippedTo?.Name} armorGy={p.Graveyard.Contains(armor)} atk={host.CurrentAtk} {State(e)}");
            }

            {
                // Not offered for a non-Equip Spell (Magic Jammer is).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var armor = PlaceSetTrap(e, p, ArmorBreak, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var ok = act && Offers(e, p, jammer) && !Offers(e, p, armor);
                Check("Armor Break: not offered for Pot of Greed", ok, State(e));
                PassAll(e);
            }

            {
                // The AI answers the player's Set Equip Spell activation with its Armor Break (the engine runs
                // SpellTrapEffects.AiAutoRespond when the AI's response window opens), and keeps it for a Pot of Greed.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var armor = PlaceSetTrap(e, o, ArmorBreak, 0);
                var host = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var pot = PutInHand(e, p, PotOfGreed);
                var potAct = e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.PendingResponse != null && e.PendingResponse.Responder == o) SpellTrapEffects.AiAutoRespond(e);
                PassAll(e);
                var kept = InSpellTrap(o, armor) && !armor.FaceUp && p.Graveyard.Contains(pot);
                var sword = PlaceSetTrap(e, p, LegendarySword, 1);
                var act = e.TryActivateSpellTrap(p, sword, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(host)) e.TrySelectEffectTarget(host);
                if (e.PendingResponse != null && e.PendingResponse.Responder == o) SpellTrapEffects.AiAutoRespond(e);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Armor Break (AI): kept for Pot of Greed; answers the player's Set Equip Spell activation, which is " +
                      "destroyed",
                    potAct && kept && act && o.Graveyard.Contains(armor) && p.Graveyard.Contains(sword) &&
                    host.CurrentAtk == 1400,
                    $"potAct={potAct} kept={kept} act={act} armorGy={o.Graveyard.Contains(armor)} " +
                    $"swordGy={p.Graveyard.Contains(sword)} atk={host.CurrentAtk} {State(e)}");
            }

            {
                // A Union monster's equip is a monster effect, not an Equip Spell Card's activation.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var armor = PlaceSetTrap(e, o, ArmorBreak, 0);
                var tiger = PlaceMonster(e, p, ZombieTiger, 0, BattlePosition.Attack, true);
                var host = PlaceMonster(e, p, DecayedCommander, 1, BattlePosition.Attack, true);
                var used = e.TryActivateSpellTrap(p, tiger, fromHand: false);
                var everOffered = false;
                for (var i = 0; i < 8; i++)
                {
                    if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(host)) e.TrySelectEffectTarget(host);
                    if (!e.IsAwaitingResponse) continue;
                    everOffered |= Offers(e, o, armor);
                    if (e.PendingResponse.Responder == o) SpellTrapEffects.AiAutoRespond(e);
                    if (e.IsAwaitingResponse) e.PassResponse();
                }

                Check("Armor Break: a Union monster's equip effect is not answered",
                    used && !everOffered && InSpellTrap(o, armor) && !armor.FaceUp && tiger.EquippedTo == host,
                    $"used={used} offered={everOffered} armorSet={InSpellTrap(o, armor)} eq={tiger.EquippedTo?.Name} " +
                    State(e));
            }
        }
    }
}
