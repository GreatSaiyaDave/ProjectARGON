using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "TlmSpellTrapAnswersAndAttackLocks" (v211, TLM): Royal Surrender, Spell-Stopping Statute, Mid Shield Gardna,
    /// Ancient Gear Soldier, Ancient Gear Golem and Ancient Gear Beast — compile shapes, look-alike sentences that stay
    /// uncompiled, and mini-duels through the real entry points (TryActivateSpellTrap, TryActivateChainResponse, TryAttack,
    /// DrainCombat, SpellTrapEffects.AiAutoRespond).
    /// </summary>
    public static class TlmSpellTrapAnswersAndAttackLocksRegressionTests
    {
        const int RoyalSurrender = 56058888;
        const int SpellStoppingStatute = 10069180;
        const int MidShieldGardna = 75487237;
        const int AncientGearSoldier = 56094445; // Machine Lv4 1300/1300
        const int AncientGearGolem = 83104731;   // Machine Lv8 3000/3000
        const int AncientGearBeast = 10509340;   // Machine Lv6 2000/2000

        const int GravityBind = 85742772;        // Continuous Trap
        const int TypeZero = 21237481;           // Continuous Trap with a face-up Quick Effect (Type Zero Magic Crusher)
        const int Lily = 79575620;               // Injection Fairy Lily: a face-up damage-calculation Quick Effect
        const int Bewd = 89631139;               // Normal Dragon Lv8 3000/2500
        const int MessengerOfPeace = 44656491;   // Continuous Spell
        const int JarOfGreed = 83968380;         // Normal Trap
        const int SevenTools = 3819470;          // Counter Trap vs a Trap activation
        const int PotOfGreed = 55144522;
        const int MagicJammer = 77414722;
        const int BookOfTaiyou = 38699854;       // Target 1 face-down monster on the field
        const int StopDefense = 63102017;        // Target 1 Defense Position monster your opponent controls
        const int MirrorForce = 44095762;
        const int Celtic = 91152256;             // Normal Warrior Lv4 1400/1200
        const int Sangan = 26202165;             // Effect Fiend Lv3 1000/600

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("TlmSpellTrapAnswersAndAttackLocks: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            ContinuousAnswerDuels(db, pDeck, aDeck, Check);
            MidShieldDuels(db, pDeck, aDeck, Check);
            AncientGearLockDuels(db, pDeck, aDeck, Check);
            AncientGearBeastDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} side={c.Side} spell={c.NegateRespondsToSpell} trap={c.NegateRespondsToTrap} " +
                    $"cont={c.NegateRespondsToContinuousOnly} opp={c.NegateOpponentActivationOnly} " +
                    $"card={c.NegateCardActivationOnly} destroy={c.DestroyNegatedCard} flip={c.FlipSelfFaceUpDefense} " +
                    $"send={c.SendNegatedCardToGyThenFlipSelf} opt={c.OncePerTurn} self={c.AffectsThisCardOnly} " +
                    $"notDestroyed={c.RequiresNotDestroyedByBattle} kind={c.TargetCardKind}"));

        static CompiledCardProgram CompileText(string text, string type, string race) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990211, name = "Near-miss Card", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = race, desc = text, atk = 1300, def = 1300, level = 4
            });

        static bool HasAction(CompiledCardProgram p, Func<EffectClause, bool> pred) =>
            p != null && p.ClauseList.Exists(c => c != null && pred(c));

        static bool Offers(DuelEngine e, DuelistState who, CardInstance card) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who && e.PendingResponse.LegalCards.Contains(card);

        static bool InSpellTrap(DuelistState who, CardInstance card) => who.TryFindSpellTrap(card, out _);

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

        /// <summary>Turn 3: the player's Battle Phase on an empty board (the player may attack).</summary>
        static DuelEngine PlayerBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e) && NextMainOf(e, e.Player);
            Empty(e);
            reached &= e.TryEnterBattlePhase(e.Player);
            if (e.IsAwaitingResponse) e.PassResponse();
            return e;
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            foreach (var (id, trap) in new[] { (RoyalSurrender, true), (SpellStoppingStatute, false) })
            {
                var p = Prog(db, id);
                var c = p?.ClauseList.FirstOrDefault();
                Check($"compile: {db.Get(id)?.name} = Counter Trap negating the opponent's Continuous {(trap ? "Trap" : "Spell")} " +
                      "Card activation and destroying it",
                    p != null && p.FullyCompiled && Implemented(db, id) && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.ChainLinkActivated && c.Action == EffectActionKind.NegateActivation &&
                    c.ChainResponseOnly && c.NegateCardActivationOnly && c.NegateOpponentActivationOnly &&
                    c.NegateRespondsToContinuousOnly && c.DestroyNegatedCard && c.NegateRespondsToTrap == trap &&
                    c.NegateRespondsToSpell == !trap && !c.FlipSelfFaceUpDefense && !c.SendNegatedCardToGyThenFlipSelf,
                    Describe(p));
            }

            {
                var p = Prog(db, MidShieldGardna);
                var set = p?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.SetThisFaceDownDefense);
                var neg = p?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.ChainLinkActivated);
                Check("compile: Mid Shield Gardna = once-per-turn turn-set + negate a Spell targeting it face-down, send, flip",
                    p != null && p.FullyCompiled && Implemented(db, MidShieldGardna) && p.ClauseList.Count == 2 &&
                    set != null && set.OncePerTurn && neg != null && neg.Action == EffectActionKind.NegateActivation &&
                    neg.ChainResponseOnly && neg.NegateCardActivationOnly && neg.NegateRespondsToSpell &&
                    !neg.NegateRespondsToTrap && neg.FlipSelfFaceUpDefense && neg.SendNegatedCardToGyThenFlipSelf &&
                    !neg.DestroyNegatedCard && !neg.NegateOpponentActivationOnly,
                    Describe(p));
            }

            foreach (var id in new[] { AncientGearSoldier, AncientGearGolem, AncientGearBeast })
            {
                var p = Prog(db, id);
                Check($"compile: {db.Get(id)?.name} carries the attack lock on Spell/Trap Cards",
                    p != null && p.FullyCompiled && Implemented(db, id) &&
                    HasAction(p, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                      c.Action == EffectActionKind
                                          .ContinuousOpponentCannotActivateSpellTrapCardsWhileThisAttacks &&
                                      c.Side == EffectSide.Opponent),
                    Describe(p));
            }

            {
                var golem = Prog(db, AncientGearGolem);
                var beast = Prog(db, AncientGearBeast);
                Check("compile: Ancient Gear Golem = lock + piercing; Golem and Beast keep their printed Nomi rule",
                    golem != null && golem.ClauseList.Count == 2 &&
                    HasAction(golem, c => c.Action == EffectActionKind.PiercingBattleDamage) &&
                    PsctGrammar.BlocksSpecialSummon(db.Get(AncientGearGolem)?.desc) &&
                    PsctGrammar.BlocksSpecialSummon(db.Get(AncientGearBeast)?.desc) &&
                    !PsctGrammar.BlocksSpecialSummon(db.Get(AncientGearSoldier)?.desc),
                    Describe(golem));
                Check("compile: Ancient Gear Beast = lock + battle negation of any monster it destroys while it survives",
                    beast != null && beast.ClauseList.Count == 2 &&
                    HasAction(beast, c => c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                                          c.AffectsThisCardOnly && c.RequiresNotDestroyedByBattle &&
                                          string.IsNullOrEmpty(c.TargetCardKind) &&
                                          string.IsNullOrEmpty(c.CountRaceFilter)),
                    Describe(beast));
            }
        }

        // ───────────────────────── near misses ─────────────────────────

        static void NearMisses(CheckFn Check)
        {
            var field = CompileText("Activate only when your opponent activates a Field Spell Card. Negate the activation and " +
                                    "the effect of the card and destroy it.", "Trap Card", "Counter");
            Check("near-miss: a Field Spell version of the Royal Surrender text stays uncompiled",
                !field.FullyCompiled && !HasAction(field, c => c.NegateRespondsToContinuousOnly), Describe(field));

            var noDestroy = CompileText("Activate only when your opponent activates a Continuous Trap Card. Negate the " +
                                        "activation and the effect of the card.", "Trap Card", "Counter");
            Check("near-miss: Royal Surrender text without \"and destroy it\" stays uncompiled",
                !noDestroy.FullyCompiled && !HasAction(noDestroy, c => c.NegateRespondsToContinuousOnly),
                Describe(noDestroy));

            var trapGardna = CompileText("Negate the activation of any Trap Card that targets this 1 face-down monster. At " +
                                         "that time, flip this card into face-up Defense Position.", "Effect Monster",
                "Warrior");
            Check("near-miss: a Trap Card version of Mid Shield Gardna's answer stays uncompiled",
                !trapGardna.FullyCompiled && !HasAction(trapGardna, c => c.SendNegatedCardToGyThenFlipSelf),
                Describe(trapGardna));

            var trapsOnly = CompileText("If this card attacks, your opponent cannot activate any Trap Cards until the end of " +
                                        "the Damage Step.", "Effect Monster", "Machine");
            Check("near-miss: a Trap-only attack lock is not the Ancient Gear Spell/Trap lock",
                !HasAction(trapsOnly,
                    c => c.Action == EffectActionKind.ContinuousOpponentCannotActivateSpellTrapCardsWhileThisAttacks),
                Describe(trapsOnly));

            var battlePhase = CompileText("If this card attacks, your opponent cannot activate any Spell/Trap Cards until " +
                                          "the end of the Battle Phase.", "Effect Monster", "Machine");
            Check("near-miss: \"until the end of the Battle Phase\" is not the Damage Step lock",
                !HasAction(battlePhase,
                    c => c.Action == EffectActionKind.ContinuousOpponentCannotActivateSpellTrapCardsWhileThisAttacks),
                Describe(battlePhase));
        }

        // ───────────────────────── Royal Surrender / Spell-Stopping Statute ─────────────────────────

        static void ContinuousAnswerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Set Gravity Bind: Royal Surrender negates its activation and destroys it.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var royal = PlaceSetTrap(e, p, RoyalSurrender, 0);
                var gb = PlaceSetTrap(e, o, GravityBind, 0);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, gb, fromHand: false);
                var offered = Offers(e, p, royal);
                var answered = offered && e.TryActivateChainResponse(p, royal);
                PassAll(e);
                Check("Royal Surrender: negates the opponent's Continuous Trap activation and destroys it",
                    act && offered && answered && o.Graveyard.Contains(gb) && p.Graveyard.Contains(royal) &&
                    e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} gbGy={o.Graveyard.Contains(gb)} " +
                    $"royalGy={p.Graveyard.Contains(royal)} {State(e)}");
            }

            {
                // Not offered for a Normal Trap (Seven Tools is).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var royal = PlaceSetTrap(e, p, RoyalSurrender, 0);
                var tools = PlaceSetTrap(e, p, SevenTools, 1);
                var jar = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, jar, fromHand: false);
                var ok = act && Offers(e, p, tools) && !Offers(e, p, royal);
                Check("Royal Surrender: not offered for a Normal Trap (Jar of Greed)", ok, State(e));
                PassAll(e);
            }

            {
                // The AI answers the player's Continuous Trap with its Royal Surrender, but never its own.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var royal = PlaceSetTrap(e, o, RoyalSurrender, 0);
                var gb = PlaceSetTrap(e, p, GravityBind, 0);
                var act = e.TryActivateSpellTrap(p, gb, fromHand: false);
                if (e.PendingResponse != null && e.PendingResponse.Responder == o) SpellTrapEffects.AiAutoRespond(e);
                PassAll(e);
                Check("Royal Surrender (AI): answers the player's Gravity Bind activation, which is destroyed",
                    act && o.Graveyard.Contains(royal) && p.Graveyard.Contains(gb),
                    $"act={act} royalGy={o.Graveyard.Contains(royal)} gbGy={p.Graveyard.Contains(gb)} {State(e)}");
            }

            {
                // Its controller's own Continuous Trap is not answered (Lua rp~=tp).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                var royal = PlaceSetTrap(e, o, RoyalSurrender, 0);
                var gb = PlaceSetTrap(e, o, GravityBind, 1);
                // The player's Seven Tools keeps the Chain open (a window with a legal answer) while the link is checked.
                PlaceSetTrap(e, e.Player, SevenTools, 0);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, gb, fromHand: false);
                var link = e.Chain.Links.LastOrDefault();
                var clause = Prog(db, RoyalSurrender).ClauseList[0];
                var own = link != null &&
                          TextEffectRuntime.CanAnswerChainLinkWithClause(e, o, royal, clause, link, out var reason);
                PassAll(e);
                Check("Royal Surrender: does not answer its controller's own Continuous Trap",
                    act && link != null && !own && InSpellTrap(o, royal) && !royal.FaceUp && InSpellTrap(o, gb) &&
                    gb.FaceUp,
                    $"act={act} link={link?.Card?.Name} own={own} royalSet={InSpellTrap(o, royal)} " +
                    $"gbUp={InSpellTrap(o, gb) && gb.FaceUp} {State(e)}");
            }

            {
                // The opponent's Continuous Spell from the hand: Spell-Stopping Statute negates and destroys it.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var statute = PlaceSetTrap(e, p, SpellStoppingStatute, 0);
                var mop = PutInHand(e, o, MessengerOfPeace);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, mop, fromHand: true);
                var offered = Offers(e, p, statute);
                var answered = offered && e.TryActivateChainResponse(p, statute);
                PassAll(e);
                Check("Spell-Stopping Statute: negates the opponent's Continuous Spell activation and destroys it",
                    act && offered && answered && o.Graveyard.Contains(mop) && p.Graveyard.Contains(statute) &&
                    e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} mopGy={o.Graveyard.Contains(mop)} " +
                    $"statuteGy={p.Graveyard.Contains(statute)} {State(e)}");
            }

            {
                // Not offered for a Normal Spell (Magic Jammer is), nor for a Continuous Trap.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var statute = PlaceSetTrap(e, p, SpellStoppingStatute, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var ok = act && Offers(e, p, jammer) && !Offers(e, p, statute);
                PassAll(e);
                var gb = PlaceSetTrap(e, o, GravityBind, 1);
                var tools = PlaceSetTrap(e, p, SevenTools, 2);
                var act2 = e.TryActivateSpellTrap(o, gb, fromHand: false);
                var ok2 = act2 && Offers(e, p, tools) && !Offers(e, p, statute);
                Check("Spell-Stopping Statute: not offered for Pot of Greed or a Continuous Trap", ok && ok2,
                    $"ok={ok} ok2={ok2} {State(e)}");
                PassAll(e);
            }
        }

        // ───────────────────────── Mid Shield Gardna ─────────────────────────

        static void MidShieldDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Book of Taiyou targets the face-down Mid Shield Gardna: negated, sent to the GY, and the
                // Gardna turns face-up Defense Position (not face-up Attack).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var gardna = PlaceMonster(e, p, MidShieldGardna, 0, BattlePosition.Defense, false);
                var book = PutInHand(e, o, BookOfTaiyou);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, book, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(gardna)) e.TrySelectEffectTarget(gardna);
                var offered = Offers(e, p, gardna);
                var answered = offered && e.TryActivateChainResponse(p, gardna);
                PassAll(e);
                Check("Mid Shield Gardna: negates a Spell targeting it face-down, the Spell goes to the GY, it flips face-up " +
                      "Defense",
                    act && offered && answered && o.Graveyard.Contains(book) && p.TryFindMonster(gardna, out _) &&
                    gardna.FaceUp && gardna.Position == BattlePosition.Defense && e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} bookGy={o.Graveyard.Contains(book)} " +
                    $"up={gardna.FaceUp} pos={gardna.Position} {State(e)}");
            }

            {
                // The AI's face-down Mid Shield Gardna answers the player's Book of Taiyou.
                var e = PlayerFirstMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var gardna = PlaceMonster(e, o, MidShieldGardna, 0, BattlePosition.Defense, false);
                var book = PutInHand(e, p, BookOfTaiyou);
                var act = e.TryActivateSpellTrap(p, book, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(gardna)) e.TrySelectEffectTarget(gardna);
                if (e.PendingResponse != null && e.PendingResponse.Responder == o) SpellTrapEffects.AiAutoRespond(e);
                PassAll(e);
                Check("Mid Shield Gardna (AI): answers the player's Book of Taiyou and ends face-up Defense",
                    act && p.Graveyard.Contains(book) && o.TryFindMonster(gardna, out _) && gardna.FaceUp &&
                    gardna.Position == BattlePosition.Defense,
                    $"act={act} bookGy={p.Graveyard.Contains(book)} up={gardna.FaceUp} pos={gardna.Position} {State(e)}");
            }

            {
                // A face-up Mid Shield Gardna does not answer (Stop Defense changes it to Attack Position).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var gardna = PlaceMonster(e, p, MidShieldGardna, 0, BattlePosition.Defense, true);
                var stop = PutInHand(e, o, StopDefense);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, stop, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(gardna)) e.TrySelectEffectTarget(gardna);
                var offered = Offers(e, p, gardna);
                PassAll(e);
                Check("Mid Shield Gardna: a face-up copy does not answer a Spell that targets it",
                    act && !offered && gardna.FaceUp && gardna.Position == BattlePosition.Attack,
                    $"act={act} offered={offered} pos={gardna.Position} {State(e)}");
            }
        }

        // ───────────────────────── Ancient Gear attack lock ─────────────────────────

        static void AncientGearLockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            foreach (var gearId in new[] { 0, AncientGearGolem })
            {
                // Turn 3: the player's 3000-ATK direct attacker (Blue-Eyes as the control) against the AI's Set Mirror Force.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var attacker = PlaceMonster(e, p, gearId == 0 ? Bewd : gearId, 0, BattlePosition.Attack, true);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var attacked = reached && e.TryAttack(p, attacker, null);
                DrainCombat(e);
                PassAll(e);
                var survived = p.TryFindMonster(attacker, out _);
                var cleared = !ContinuousNegationRuntime.ActivationLocked(e, o, mf, out _);
                if (gearId == 0)
                {
                    Check("Ancient Gear control: without it the AI answers the attack with Mirror Force",
                        attacked && !survived && o.Graveyard.Contains(mf) && o.LifePoints == lp,
                        $"reached={reached} attacked={attacked} survived={survived} lp {lp}->{o.LifePoints} {State(e)}");
                    continue;
                }

                Check($"{db.Get(gearId)?.name} (AI): the AI cannot answer its attack with a Set Mirror Force; the lock " +
                      "ends with the Damage Step",
                    attacked && survived && InSpellTrap(o, mf) && !mf.FaceUp && o.LifePoints == lp - 3000 && cleared,
                    $"reached={reached} attacked={attacked} survived={survived} mfSet={InSpellTrap(o, mf) && !mf.FaceUp} " +
                    $"lp {lp}->{o.LifePoints} cleared={cleared} {State(e)}");
            }

            foreach (var negated in new[] { false, true })
            {
                // The opponent's Ancient Gear Soldier attacks the player's Injection Fairy Lily. At the declaration the
                // player's Set Mirror Force and the Spell in hand are locked, while the face-up Type Zero Magic Crusher's
                // effect (not a card activation) stays usable; during damage calculation (Lily's window) Mirror Force is
                // still locked; after the Damage Step the lock is gone. A negated Soldier locks nothing.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var gear = PlaceMonster(e, o, AncientGearSoldier, 0, BattlePosition.Attack, true);
                if (negated) gear.IsNegated = true;
                var mf = PlaceSetTrap(e, p, MirrorForce, 0);
                var tz = PlaceSetTrap(e, p, TypeZero, 1);
                tz.FaceUp = true;
                var pot = PutInHand(e, p, PotOfGreed);
                var lily = PlaceMonster(e, p, Lily, 0, BattlePosition.Attack, true);
                var oSet = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var attacked = reached && e.TryAttack(o, gear, lily);
                var declared = e.HasDeclaredAttack && e.PendingResponse?.Timing == ResponseTiming.AttackDeclared;
                var lockedSet = ContinuousNegationRuntime.ActivationLocked(e, p, mf, out var why);
                var lockedHand = ContinuousNegationRuntime.ActivationLocked(e, p, pot, out _);
                var faceUpFree = !ContinuousNegationRuntime.ActivationLocked(e, p, tz, out _);
                var mfOffered = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(mf);
                var tzOffered = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(tz);
                // The lock binds only the attacker's opponent.
                var oppFree = !ContinuousNegationRuntime.ActivationLocked(e, o, oSet, out _);
                if (negated)
                {
                    Check("Ancient Gear Soldier: a negated attacker locks nothing (Mirror Force is offered)",
                        attacked && declared && !lockedSet && !lockedHand && mfOffered,
                        $"reached={reached} attacked={attacked} declared={declared} set={lockedSet} hand={lockedHand} " +
                        $"offered={mfOffered} {State(e)}");
                    PassAll(e);
                    DrainCombat(e);
                    continue;
                }

                var tzUsed = tzOffered && e.TryActivateSpellTrap(p, tz, fromHand: false);
                // Pass step by step (not PassAll, which would also pass Lily's damage calculation window).
                var dcLocked = false;
                var dcWindow = false;
                for (var i = 0; i < 12; i++)
                {
                    if (e.PendingResponse?.Timing == ResponseTiming.DamageCalculation)
                    {
                        dcWindow = true;
                        dcLocked = ContinuousNegationRuntime.ActivationLocked(e, p, mf, out _);
                        break;
                    }

                    if (e.IsAwaitingResponse) e.PassResponse();
                    else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                    else e.RecoverStuckCombat();
                }

                DrainCombat(e);
                PassAll(e);
                var cleared = !ContinuousNegationRuntime.ActivationLocked(e, p, mf, out _);
                Check("Ancient Gear Soldier: while it attacks the opponent cannot activate a Set Spell/Trap or a hand Spell " +
                      "(through damage calculation); a face-up Continuous Trap's effect stays usable; the lock ends after " +
                      "the Damage Step",
                    attacked && declared && lockedSet && lockedHand && faceUpFree && !mfOffered && tzOffered && tzUsed &&
                    o.LifePoints == oLp - 500 && oppFree && dcWindow && dcLocked && p.LifePoints == lp - 900 &&
                    InSpellTrap(p, mf) && !mf.FaceUp && cleared,
                    $"reached={reached} attacked={attacked} declared={declared} set={lockedSet} hand={lockedHand} " +
                    $"faceUpFree={faceUpFree} mfOffered={mfOffered} tz={tzOffered}/{tzUsed} oLp {oLp}->{o.LifePoints} " +
                    $"oppFree={oppFree} dc={dcWindow}/{dcLocked} lp {lp}->{p.LifePoints} cleared={cleared} why={why} " +
                    State(e));
            }
        }

        // ───────────────────────── Ancient Gear Beast: battle negation ─────────────────────────

        static void AncientGearBeastDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Ancient Gear Beast destroys the AI's Sangan by battle: Sangan's effects are negated in the GY (no search).
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var beast = PlaceMonster(e, p, AncientGearBeast, 0, BattlePosition.Attack, true);
                var sangan = PlaceMonster(e, o, Sangan, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var hand = o.Hand.Count;
                var attacked = reached && e.TryAttack(p, beast, sangan);
                DrainCombat(e);
                PassAll(e);
                Check("Ancient Gear Beast: a monster it destroys by battle has its effects negated in the GY (Sangan does " +
                      "not search)",
                    attacked && o.Graveyard.Contains(sangan) && sangan.EffectsNegatedAfterBattle &&
                    NegationMonstersRuntime.NegatedInGy(e, sangan) && o.Hand.Count == hand &&
                    p.TryFindMonster(beast, out _),
                    $"reached={reached} attacked={attacked} gy={o.Graveyard.Contains(sangan)} " +
                    $"negated={sangan.EffectsNegatedAfterBattle} hand {hand}->{o.Hand.Count} {State(e)}");
            }

            {
                // Both Ancient Gear Beasts are destroyed by the same battle: neither negates the other (Lua
                // c:IsStatus(STATUS_BATTLE_DESTROYED)).
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, AncientGearBeast, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(e, o, AncientGearBeast, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var attacked = reached && e.TryAttack(p, mine, theirs);
                DrainCombat(e);
                PassAll(e);
                Check("Ancient Gear Beast: no negation when it is destroyed by the same battle",
                    attacked && p.Graveyard.Contains(mine) && o.Graveyard.Contains(theirs) &&
                    !mine.EffectsNegatedAfterBattle && !theirs.EffectsNegatedAfterBattle,
                    $"reached={reached} attacked={attacked} mineGy={p.Graveyard.Contains(mine)} " +
                    $"theirsGy={o.Graveyard.Contains(theirs)} mineNeg={mine.EffectsNegatedAfterBattle} " +
                    $"theirsNeg={theirs.EffectsNegatedAfterBattle} {State(e)}");
            }
        }
    }
}
