using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Spell/Trap activations on the compiled cost / target flow (SpellTrapEffects.UsesCompiledActivationPending:
    /// Fissure, Change of Heart, Brain Control, every Equip Spell, discard / Tribute costs, second targets …) are a real
    /// Chain Link (TextEffects.CompiledActivationChainRuntime): the costs are paid and the targets chosen at activation,
    /// the opponent may respond, and the effect applies when the link resolves. Before the fix they resolved at once,
    /// so a Set Magic Jammer was never offered against them (found 2026-09-29 with a headless probe). The 18 checks that
    /// expect a chain window, a Jammer negation or a moved target failed before the fix; the other 10 guard what must not
    /// change: the prompts, Cancel and its refunds, the AI's auto-picks, Brain Control's single 800 LP payment, and
    /// Monster Reborn / Dark Hole.
    /// </summary>
    public static class CompiledActivationChainRegressionTests
    {
        const int Fissure = 66788016;           // Destroy the face-up monster your opponent controls with the lowest ATK
        const int ChangeOfHeart = 4031928;      // Target 1 monster your opponent controls; take control until the End Phase
        const int AxeOfDespair = 40619825;      // Equip Spell: +1000 ATK
        const int SnatchSteal = 45986603;       // Equip Spell: take control of the equipped monster
        const int BrainControl = 87910978;      // Pay 800 LP, then target 1 face-up monster your opponent controls …
        const int MonsterReborn = 83764719;     // legacy chain path (control)
        const int DarkHole = 53129443;          // untargeted chain path (control)
        const int TributeToTheDoomed = 79759861; // Discard 1 card, then target 1 monster; destroy it
        const int TwoPronged = 83887306;        // Normal Trap: target 2 monsters you control and 1 your opponent controls
        const int MagicJammer = 77414722;       // Counter Trap: When a Spell Card is activated: Discard 1 card; negate …
        const int SevenTools = 3819470;         // Counter Trap: When a Trap Card is activated: Pay 1000 LP; negate …
        const int BookOfMoon = 14087893;        // Quick-Play: Target 1 face-up monster; change it to face-down Defense
        const int Celtic = 91152256;            // Lv4 1400 Warrior
        const int GiantSoldier = 13039848;      // Lv3 1300/2000
        const int Bewd = 89631139;              // Lv8 3000
        const int Kuriboh = 40640057;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("Compiled activation chain: " + name, ok, detail ?? "");

            CompileShapes(db, pDeck, aDeck, Check);
            ReportedCardsGiveTheOpponentAWindow(db, pDeck, aDeck, Check);
            LegacyPathsStillOpenAWindow(db, pDeck, aDeck, Check);
            AiAnswersAndAiActivations(db, pDeck, aDeck, Check);
            CancelAndRefund(db, pDeck, aDeck, Check);
            CostsAndSeveralTargets(db, pDeck, aDeck, Check);
            TargetsThatMoveBeforeResolution(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A fresh duel with empty boards, hands and GYs; the player is in their Main Phase 1.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman)
        {
            var e = Fresh(db, pDeck, aDeck);
            e.HumanVsHuman = humanVsHuman;
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        /// <summary>A Set Magic Jammer plus 1 card in hand to discard for it.</summary>
        static CardInstance SetJammer(DuelEngine e, DuelistState who)
        {
            var jam = PlaceSetTrap(e, who, MagicJammer, 4);
            PutInHand(e, who, Kuriboh);
            return jam;
        }

        static void Pick(DuelEngine e, CardInstance target)
        {
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(target);
        }

        /// <summary>The opponent's chain window on <paramref name="card"/>'s activation, which is the only link.</summary>
        static bool WindowFor(DuelEngine e, DuelistState responder, CardInstance card)
        {
            var pr = e.PendingResponse;
            return pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == responder &&
                   e.Chain.Count == 1 && e.Chain.Last != null && e.Chain.Last.Card == card && !e.IsAwaitingEffectTarget;
        }

        static string State(DuelEngine e) =>
            $"pr={e.PendingResponse?.Timing.ToString() ?? "-"}/{e.PendingResponse?.Responder?.Name ?? "-"} " +
            $"chain={e.Chain.Count} last={e.Chain.Last?.Card?.Name ?? "-"} pick={e.IsAwaitingEffectTarget} " +
            $"LP={e.Player.LifePoints}/{e.Opponent.LifePoints}";

        static bool OnField(DuelistState who, CardInstance card) => who.TryFindSpellTrap(card, out _) && card.FaceUp;

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShapes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);

            bool Has(int id, Func<EffectClause, bool> shape)
            {
                var prog = CompiledEffectCache.GetOrCompile(e.CreateCardInstance(id)?.Def);
                return prog != null && prog.FullyCompiled && prog.ClausesFor(EffectTiming.Activate).Exists(
                    c => c != null && shape(c));
            }

            Check("shapes: Fissure lowest-ATK pick, Change of Heart / Brain Control take control, Axe / Snatch Steal " +
                  "equip, Tribute to the Doomed discard-then-target, Two-Pronged Attack second target",
                Has(Fissure, c => c.Action == EffectActionKind.Destroy && c.PickLowestAtk) &&
                Has(ChangeOfHeart, c => c.Action == EffectActionKind.TakeControlUntilEndPhase && c.RequiresTargetChoice) &&
                Has(BrainControl, c => c.Action == EffectActionKind.TakeControlUntilEndPhase && c.PayLpAmount == 800) &&
                Has(AxeOfDespair, c => c.Action == EffectActionKind.EquipThisToTarget) &&
                Has(SnatchSteal, c => c.Action == EffectActionKind.EquipThisToTarget && c.TakeControlOfTarget) &&
                Has(TributeToTheDoomed, c => c.Action == EffectActionKind.Destroy && c.RequiresDiscardCost) &&
                Has(TwoPronged, c => c.Action == EffectActionKind.Destroy && c.RequiresSecondTarget), "");
        }

        /// <summary>
        /// The probe from the bug report: human vs human, the opponent has a Set Magic Jammer and 1 card in hand, the
        /// player activates from the hand in Main Phase 1 and picks the target.
        /// </summary>
        static void ReportedCardsGiveTheOpponentAWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var id in new[] { Fissure, ChangeOfHeart, AxeOfDespair, SnatchSteal, BrainControl })
            foreach (var jammed in new[] { false, true })
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var own = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var foe = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var jam = SetJammer(e, o);
                var card = PutInHand(e, p, id);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                var target = id == AxeOfDespair ? own : foe;
                Pick(e, target);
                var name = card.Name;

                var window = WindowFor(e, o, card) && e.PendingResponse.LegalCards.Contains(jam);
                var seated = OnField(p, card);
                var targetOnLink = id == Fissure || e.Chain.Last?.Target == target;
                var notYet = o.TryFindMonster(foe, out _) && own.CurrentAtk == 1400 && card.EquippedTo == null;
                var lpAtWindow = p.LifePoints;
                var detail = $"act={act} window={window} seated={seated} linkTarget={e.Chain.Last?.Target?.Name} " +
                             $"notYet={notYet} {State(e)}";
                if (!jammed)
                    Check($"{name} (window): the opponent may respond before it resolves; the card is on the field " +
                          "as the Chain Link and nothing has happened yet",
                        act && window && seated && targetOnLink && notYet &&
                        lpAtWindow == (id == BrainControl ? 7200 : 8000),
                        detail);

                if (jammed)
                {
                    var answered = e.TryActivateChainResponse(o, jam, fromHand: false, autoPickTarget: false);
                    for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                    DrainCombat(e);
                    Check($"{name} (Jammer): Magic Jammer negates it — no effect, the card in the GY, costs stay paid",
                        act && answered && p.Graveyard.Contains(card) && o.Graveyard.Contains(jam) &&
                        o.Hand.Count == 0 && o.TryFindMonster(foe, out _) && own.CurrentAtk == 1400 &&
                        p.LifePoints == (id == BrainControl ? 7200 : 8000) && !e.Chain.HasLinks &&
                        !e.IsAwaitingResponse,
                        $"answered={answered} cardGy={p.Graveyard.Contains(card)} jamGy={o.Graveyard.Contains(jam)} " +
                        $"oHand={o.Hand.Count} foeOpp={o.TryFindMonster(foe, out _)} atk={own.CurrentAtk} {State(e)}");
                    continue;
                }

                e.PassResponse();
                DrainCombat(e);
                bool applied;
                switch (id)
                {
                    case Fissure:
                        applied = o.Graveyard.Contains(foe) && p.Graveyard.Contains(card);
                        break;
                    case AxeOfDespair:
                        applied = card.EquippedTo == own && own.CurrentAtk == 2400 && OnField(p, card);
                        break;
                    case SnatchSteal:
                        applied = p.TryFindMonster(foe, out _) && card.EquippedTo == foe && OnField(p, card);
                        break;
                    default:
                        applied = p.TryFindMonster(foe, out _) && p.Graveyard.Contains(card);
                        break;
                }

                Check($"{name}: resolves after both pass" + (id == BrainControl ? " (800 LP paid once)" : ""),
                    applied && p.LifePoints == (id == BrainControl ? 7200 : 8000) && !e.Chain.HasLinks &&
                    !e.IsAwaitingResponse && o.Hand.Count == 1 && !jam.FaceUp,
                    $"applied={applied} equippedTo={card.EquippedTo?.Name} atk={own.CurrentAtk} {State(e)}");
            }
        }

        static void LegacyPathsStillOpenAWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                SetJammer(e, o);
                var ghost = e.CreateCardInstance(Celtic);
                p.Graveyard.Add(ghost);
                var mr = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, mr, fromHand: true);
                Pick(e, ghost);
                var window = WindowFor(e, o, mr);
                e.PassResponse();
                DrainCombat(e);
                Check("Monster Reborn (control): window, then the target is Special Summoned",
                    act && window && p.TryFindMonster(ghost, out _) && !e.Chain.HasLinks,
                    $"act={act} window={window} summoned={p.TryFindMonster(ghost, out _)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                SetJammer(e, o);
                var foe = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, dh, fromHand: true);
                var window = WindowFor(e, o, dh);
                e.PassResponse();
                DrainCombat(e);
                Check("Dark Hole (control): window, then every monster is destroyed",
                    act && window && o.Graveyard.Contains(foe) && !e.Chain.HasLinks,
                    $"act={act} window={window} foeGy={o.Graveyard.Contains(foe)} {State(e)}");
            }
        }

        static void AiAnswersAndAiActivations(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // The AI's Magic Jammer against the player's Brain Control (never used before the fix).
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: false);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var jam = SetJammer(e, o);
                var bc = PutInHand(e, p, BrainControl);
                var act = e.TryActivateSpellTrap(p, bc, fromHand: true);
                Pick(e, foe);
                DrainCombat(e);
                Check("Brain Control (Jammer): the AI's Magic Jammer negates it; the 800 LP stay paid",
                    act && o.Graveyard.Contains(jam) && o.Hand.Count == 0 && p.Graveyard.Contains(bc) &&
                    o.TryFindMonster(foe, out _) && p.LifePoints == 7200 && !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    $"act={act} jamGy={o.Graveyard.Contains(jam)} oHand={o.Hand.Count} bcGy={p.Graveyard.Contains(bc)} " +
                    $"foeOpp={o.TryFindMonster(foe, out _)} {State(e)}");
            }

            // The AI activates Change of Heart (it picks its target at once); the player may answer before it resolves.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mine = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var jam = SetJammer(e, p);
                var coh = PutInHand(e, o, ChangeOfHeart);
                var act = e.TryActivateSpellTrap(o, coh, fromHand: true);
                var window = WindowFor(e, p, coh) && e.PendingResponse.LegalCards.Contains(jam);
                var notYet = p.TryFindMonster(mine, out _);
                var linkTarget = e.Chain.Last?.Target;
                e.PassResponse();
                DrainCombat(e);
                Check("Change of Heart (window, AI): the AI's auto-picked target is on the link, the player may respond, " +
                      "then the AI takes control",
                    reached && act && window && notYet && linkTarget == mine && o.TryFindMonster(mine, out _) &&
                    o.Graveyard.Contains(coh) && !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    $"reached={reached} act={act} window={window} notYet={notYet} linkTarget={linkTarget?.Name} " +
                    $"taken={o.TryFindMonster(mine, out _)} {State(e)}");
            }
        }

        static void CancelAndRefund(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Bewd, 1, BattlePosition.Attack, true);
                var jam = SetJammer(e, o);
                var bc = PutInHand(e, p, BrainControl);
                var act = e.TryActivateSpellTrap(p, bc, fromHand: true);
                var prompted = e.IsAwaitingEffectTarget;
                var lpAtPick = p.LifePoints;
                var cancelled = prompted && e.CancelEffectTargeting();
                Check("Brain Control: cancelling the target pick refunds the 800 LP and opens no window",
                    act && prompted && lpAtPick == 7200 && cancelled && p.LifePoints == 8000 && p.Hand.Contains(bc) &&
                    !e.Chain.HasLinks && !e.IsAwaitingResponse && !e.IsAwaitingEffectTarget && !jam.FaceUp,
                    $"act={act} prompted={prompted} lpAtPick={lpAtPick} cancelled={cancelled} " +
                    $"inHand={p.Hand.Contains(bc)} {State(e)}");

                var again = e.TryActivateSpellTrap(p, bc, fromHand: true);
                Pick(e, foe);
                var window = WindowFor(e, o, bc);
                e.PassResponse();
                DrainCombat(e);
                Check("Brain Control: activated again after the cancel — window, 800 LP once, control taken",
                    again && window && p.LifePoints == 7200 && p.TryFindMonster(foe, out _) && !e.Chain.HasLinks,
                    $"again={again} window={window} taken={p.TryFindMonster(foe, out _)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Attack, true);
                SetJammer(e, o);
                var axe = PutInHand(e, p, AxeOfDespair);
                var act = e.TryActivateSpellTrap(p, axe, fromHand: true);
                var prompted = e.IsAwaitingEffectTarget;
                var cancelled = prompted && e.CancelEffectTargeting();
                Check("Axe of Despair: cancelling the equip target returns it to the hand, no link left",
                    act && prompted && cancelled && p.Hand.Contains(axe) && !p.TryFindSpellTrap(axe, out _) &&
                    !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    $"act={act} prompted={prompted} cancelled={cancelled} inHand={p.Hand.Contains(axe)} {State(e)}");
            }
        }

        static void CostsAndSeveralTargets(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Discard cost, then the target: both chosen before the opponent may respond.
            foreach (var jammed in new[] { false, true })
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var jam = SetJammer(e, o);
                var fodder = PutInHand(e, p, Kuriboh);
                var ttd = PutInHand(e, p, TributeToTheDoomed);
                var act = e.TryActivateSpellTrap(p, ttd, fromHand: true);
                var costPrompt = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingDiscardCost;
                Pick(e, fodder);
                Pick(e, foe);
                var window = WindowFor(e, o, ttd);
                var paid = p.Graveyard.Contains(fodder) && !p.Hand.Contains(fodder);
                var notYet = o.TryFindMonster(foe, out _);
                if (jammed)
                    e.TryActivateChainResponse(o, jam, fromHand: false, autoPickTarget: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                Check(jammed
                        ? "Tribute to the Doomed (Jammer): negated after the discard cost — the target survives"
                        : "Tribute to the Doomed (window): discard, target, then the opponent may respond; destroys on resolution",
                    act && costPrompt && window && paid && notYet && p.Graveyard.Contains(ttd) &&
                    p.Graveyard.Contains(fodder) && !e.Chain.HasLinks &&
                    (jammed ? o.TryFindMonster(foe, out _) && o.Graveyard.Contains(jam) : o.Graveyard.Contains(foe)),
                    $"act={act} costPrompt={costPrompt} window={window} paid={paid} notYet={notYet} " +
                    $"foeGy={o.Graveyard.Contains(foe)} {State(e)}");
            }

            // Two-Pronged Attack: a Set Trap with 2 + 1 targets; the opponent's Seven Tools is offered.
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, Kuriboh, 1, BattlePosition.Attack, true);
                var foe = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var tools = PlaceSetTrap(e, o, SevenTools, 4);
                var tpa = PlaceSetTrap(e, p, TwoPronged, 0);
                var act = e.TryActivateSpellTrap(p, tpa, fromHand: false);
                Pick(e, a);
                Pick(e, b);
                Pick(e, foe);
                var window = WindowFor(e, o, tpa) && e.PendingResponse.LegalCards.Contains(tools);
                var notYet = p.TryFindMonster(a, out _) && p.TryFindMonster(b, out _) && o.TryFindMonster(foe, out _);
                var linkTargets = e.Chain.Last?.Targets.Count ?? 0;
                e.PassResponse();
                DrainCombat(e);
                Check("Two-Pronged Attack (window): 3 targets on the link, Seven Tools offered; all 3 destroyed on resolution",
                    act && window && notYet && linkTargets == 3 && p.Graveyard.Contains(a) && p.Graveyard.Contains(b) &&
                    o.Graveyard.Contains(foe) && p.Graveyard.Contains(tpa) && !e.Chain.HasLinks,
                    $"act={act} window={window} notYet={notYet} linkTargets={linkTargets} {State(e)}");
            }
        }

        /// <summary>The opponent answers with Book of Moon on the chosen monster (it turns face-down).</summary>
        static void TargetsThatMoveBeforeResolution(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var bom = PlaceSetTrap(e, o, BookOfMoon, 4);
                var ss = PutInHand(e, p, SnatchSteal);
                var act = e.TryActivateSpellTrap(p, ss, fromHand: true);
                Pick(e, foe);
                var window = WindowFor(e, o, ss) && e.PendingResponse.LegalCards.Contains(bom);
                var answered = window && e.TryActivateChainResponse(o, bom, fromHand: false, autoPickTarget: false);
                Pick(e, foe);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                Check("Snatch Steal (moved): its target turned face-down in the chain — no control taken, Snatch Steal " +
                      "goes to the GY",
                    act && window && answered && o.TryFindMonster(foe, out _) && !foe.FaceUp && ss.EquippedTo == null &&
                    p.Graveyard.Contains(ss) && !p.TryFindSpellTrap(ss, out _) && !e.Chain.HasLinks,
                    $"act={act} window={window} answered={answered} foeOpp={o.TryFindMonster(foe, out _)} " +
                    $"foeUp={foe.FaceUp} ssGy={p.Graveyard.Contains(ss)} {State(e)}");
            }

            {
                // Fissure does not target: at resolution it destroys the face-up monster with the lowest ATK then. The
                // AI activates it (auto-picking the lowest), the player answers with Book of Moon on that monster.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var low = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Attack, true);
                var next = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var bom = PlaceSetTrap(e, p, BookOfMoon, 4);
                var fis = PutInHand(e, o, Fissure);
                var act = e.TryActivateSpellTrap(o, fis, fromHand: true);
                var window = WindowFor(e, p, fis) && e.PendingResponse.LegalCards.Contains(bom);
                var answered = window && e.TryActivateChainResponse(p, bom, fromHand: false, autoPickTarget: false);
                Pick(e, low);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                Check("Fissure (moved): its pick turned face-down — it destroys the lowest-ATK face-up monster at resolution",
                    reached && act && window && answered && p.TryFindMonster(low, out _) && !low.FaceUp &&
                    p.Graveyard.Contains(next) && o.Graveyard.Contains(fis) && !e.Chain.HasLinks,
                    $"act={act} window={window} answered={answered} lowOnField={p.TryFindMonster(low, out _)} " +
                    $"lowUp={low.FaceUp} nextGy={p.Graveyard.Contains(next)} {State(e)}");
            }
        }
    }
}
