using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Activation LP costs are paid when the Spell/Trap is activated, before any response, and a negated activation
    /// keeps them paid (TextEffects.ActivationLpCostRuntime). Before the fix, Toon World and Premature Burial
    /// negated by Magic Jammer, Confiscation negated by Magic Jammer and Archfiend's Roar negated by Seven Tools of
    /// the Bandit kept their LP, and Archfiend's Roar answering an attack never paid its 500 LP. The checks marked
    /// "(activation)" or "negated" failed before the fix; the others guard the parts that must not change: one
    /// payment in total, the LP legality check, Spell Economics, the refund of a cancelled target pick, and the
    /// last-LP confirm.
    /// </summary>
    public static class ActivationLpCostRegressionTests
    {
        const int ToonWorld = 15259703;       // Continuous Spell: Activate this card by paying 1000 LP.
        const int PrematureBurial = 70828912; // Equip Spell: Activate this card by paying 800 LP, then target …
        const int Confiscation = 17375316;    // Normal Spell: Pay 1000 Life Points. Look at your opponent's hand …
        const int BrainControl = 87910978;    // Normal Spell: Pay 800 LP, then target … (paid by StartIgnition)
        const int ArchfiendsRoar = 56246017;  // Normal Trap: Pay 500 Life Points, then target 1 "Archfiend" …
        const int DelinquentDuo = 44763025;   // Normal Spell: Pay 1000 LP; your opponent discards … (DiscardTriggers)
        const int MagicJammer = 77414722;     // Counter Trap: When a Spell Card is activated: Discard 1 card; negate …
        const int SevenTools = 3819470;       // Counter Trap: When a Trap Card is activated: Pay 1000 LP; negate …
        const int SpellEconomics = 4259068;   // Continuous Spell: You do not pay LP to activate Spell Cards.
        const int CyberStein = 69015963;      // Monster: You can pay 5000 LP; … (not a Spell/Trap activation)
        const int Celtic = 91152256;          // Lv4 1400
        const int Kuriboh = 40640057;
        const int ArchfiendSoldier = 49881766;
        const int GiantSoldier = 13039848;    // Normal Lv3 1300/2000
        const int ThousandEyesIdol = 27125110; // 0 ATK: its attack deals no damage

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("Activation LP cost: " + name, ok, detail ?? "");

            CompileShapes(db, pDeck, aDeck, Check);
            ToonWorldVsMagicJammer(db, pDeck, aDeck, Check);
            PrematureBurialVsMagicJammer(db, pDeck, aDeck, Check);
            PaidBeforeTheResponseWindow(db, pDeck, aDeck, Check);
            CancelledTargetPickRefunds(db, pDeck, aDeck, Check);
            SpellEconomicsSkipsTheCost(db, pDeck, aDeck, Check);
            LastLifePointsEndTheDuelAtActivation(db, pDeck, aDeck, Check);
            OtherSpellsAndTraps(db, pDeck, aDeck, Check);
            AiActivationPaysBeforeTheHumanResponds(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A fresh duel with empty boards, hands and GYs; the player is in their Main Phase 1.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman = false)
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

        /// <summary>The opponent's Set Magic Jammer plus 1 card in their hand to discard for it.</summary>
        static CardInstance SetJammer(DuelEngine e, DuelistState who)
        {
            var jam = PlaceSetTrap(e, who, MagicJammer, 4);
            PutInHand(e, who, Kuriboh);
            return jam;
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static void Pick(DuelEngine e, CardInstance target)
        {
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(target);
        }

        static string Lp(DuelistState who) => $"{who.Name}={who.LifePoints}";

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShapes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            foreach (var (id, lp) in new[]
                     {
                         (ToonWorld, 1000), (PrematureBurial, 800), (Confiscation, 1000), (BrainControl, 800),
                         (ArchfiendsRoar, 500), (DelinquentDuo, 1000)
                     })
            {
                var card = e.CreateCardInstance(id);
                var prog = CompiledEffectCache.GetOrCompile(card?.Def);
                var clause = ActivationLpCostRuntime.LpCostClause(card, prog);
                Check($"{card?.Name}: its Activate clause's {lp} LP is an activation cost",
                    prog != null && prog.FullyCompiled && clause != null && clause.Timing == EffectTiming.Activate &&
                    clause.PayLpAmount == lp,
                    $"full={prog?.FullyCompiled} clause={clause?.Action} lp={clause?.PayLpAmount}");
            }

            // Seven Tools pays on its own chain clause (DuelEngine.TryActivateChainResponse); a monster's ignition
            // pays through StartIgnition.
            foreach (var id in new[] { SevenTools, CyberStein })
            {
                var card = e.CreateCardInstance(id);
                var prog = CompiledEffectCache.GetOrCompile(card?.Def);
                Check($"{card?.Name}: not an Activate-clause Spell/Trap LP cost",
                    prog != null && prog.FullyCompiled && ActivationLpCostRuntime.LpCostClause(card, prog) == null,
                    $"full={prog?.FullyCompiled}");
            }
        }

        static void ToonWorldVsMagicJammer(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PutInHand(e, o, Celtic);
                var toon = PutInHand(e, p, ToonWorld);
                var act = e.CanActivateSpellTrap(p, toon, fromHand: true) &&
                          e.TryActivateSpellTrap(p, toon, fromHand: true);
                DrainCombat(e);
                Check("Toon World resolves: 1000 LP paid once, Toon World face-up on the field",
                    act && p.LifePoints == 7000 && toon.FaceUp && p.TryFindSpellTrap(toon, out _) &&
                    !e.Chain.HasLinks,
                    $"act={act} {Lp(p)} onField={p.TryFindSpellTrap(toon, out _)} chain={e.Chain.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jam = SetJammer(e, o);
                var toon = PutInHand(e, p, ToonWorld);
                var act = e.CanActivateSpellTrap(p, toon, fromHand: true) &&
                          e.TryActivateSpellTrap(p, toon, fromHand: true);
                DrainCombat(e);
                Check("Toon World negated by the AI's Magic Jammer: the 1000 LP stay paid",
                    act && o.Graveyard.Contains(jam) && o.Hand.Count == 0 && p.Graveyard.Contains(toon) &&
                    !p.TryFindSpellTrap(toon, out _) && p.LifePoints == 7000 && !e.Chain.HasLinks,
                    $"act={act} jamGy={o.Graveyard.Contains(jam)} oHand={o.Hand.Count} " +
                    $"toonGy={p.Graveyard.Contains(toon)} {Lp(p)} chain={e.Chain.Count}");
            }
        }

        static void PrematureBurialVsMagicJammer(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PutInHand(e, o, Celtic);
                var ghost = AddToGy(e, p, Celtic);
                var pb = PutInHand(e, p, PrematureBurial);
                var act = e.CanActivateSpellTrap(p, pb, fromHand: true) &&
                          e.TryActivateSpellTrap(p, pb, fromHand: true);
                Pick(e, ghost);
                DrainCombat(e);
                Check("Premature Burial resolves: 800 LP paid once, the target Special Summoned in Attack Position " +
                      "and equipped",
                    act && p.LifePoints == 7200 && p.TryFindMonster(ghost, out _) &&
                    ghost.Position == BattlePosition.Attack && pb.EquippedTo == ghost &&
                    p.TryFindSpellTrap(pb, out _) && !e.Chain.HasLinks,
                    $"act={act} {Lp(p)} summoned={p.TryFindMonster(ghost, out _)} pos={ghost.Position} " +
                    $"equipped={pb.EquippedTo == ghost} chain={e.Chain.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jam = SetJammer(e, o);
                var ghost = AddToGy(e, p, Celtic);
                var pb = PutInHand(e, p, PrematureBurial);
                var act = e.CanActivateSpellTrap(p, pb, fromHand: true) &&
                          e.TryActivateSpellTrap(p, pb, fromHand: true);
                Pick(e, ghost);
                DrainCombat(e);
                Check("Premature Burial negated by the AI's Magic Jammer: the 800 LP stay paid, nothing is summoned",
                    act && o.Graveyard.Contains(jam) && o.Hand.Count == 0 && p.Graveyard.Contains(pb) &&
                    p.Graveyard.Contains(ghost) && !p.TryFindMonster(ghost, out _) && p.LifePoints == 7200 &&
                    !e.Chain.HasLinks,
                    $"act={act} jamGy={o.Graveyard.Contains(jam)} oHand={o.Hand.Count} pbGy={p.Graveyard.Contains(pb)} " +
                    $"ghostGy={p.Graveyard.Contains(ghost)} {Lp(p)} chain={e.Chain.Count}");
            }
        }

        static void PaidBeforeTheResponseWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Human vs human: the opponent's chain window opens with the cost already paid; passing resolves the
            // card without a second payment.
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                SetJammer(e, o);
                var toon = PutInHand(e, p, ToonWorld);
                var act = e.TryActivateSpellTrap(p, toon, fromHand: true);
                var pr = e.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o;
                var lpAtWindow = p.LifePoints;
                e.PassResponse();
                DrainCombat(e);
                Check("Toon World (activation): 1000 LP are paid before the opponent's chain window opens; " +
                      "passing does not charge them again",
                    act && window && lpAtWindow == 7000 && p.LifePoints == 7000 && p.TryFindSpellTrap(toon, out _) &&
                    !e.Chain.HasLinks,
                    $"act={act} window={window} lpAtWindow={lpAtWindow} {Lp(p)} chain={e.Chain.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                SetJammer(e, o);
                var ghost = AddToGy(e, p, Celtic);
                var pb = PutInHand(e, p, PrematureBurial);
                var act = e.TryActivateSpellTrap(p, pb, fromHand: true);
                var lpAtPick = p.LifePoints;
                var prompted = e.IsAwaitingEffectTarget;
                Pick(e, ghost);
                var pr = e.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o;
                var lpAtWindow = p.LifePoints;
                e.PassResponse();
                DrainCombat(e);
                Check("Premature Burial (activation): 800 LP are paid before the target pick and the opponent's " +
                      "window; passing does not charge them again",
                    act && lpAtPick == 7200 && window && lpAtWindow == 7200 && p.LifePoints == 7200 &&
                    p.TryFindMonster(ghost, out _) && pb.EquippedTo == ghost && !e.Chain.HasLinks,
                    $"act={act} prompted={prompted} lpAtPick={lpAtPick} window={window} lpAtWindow={lpAtWindow} " +
                    $"{Lp(p)} summoned={p.TryFindMonster(ghost, out _)} chain={e.Chain.Count}");
            }
        }

        static void CancelledTargetPickRefunds(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            AddToGy(e, p, Celtic);
            var pb = PutInHand(e, p, PrematureBurial);
            var act = e.TryActivateSpellTrap(p, pb, fromHand: true);
            var prompted = e.IsAwaitingEffectTarget;
            var lpAtPick = p.LifePoints;
            var cancelled = prompted && e.CancelEffectTargeting();
            Check("Premature Burial: cancelling the target pick undoes the activation and refunds the 800 LP",
                act && prompted && lpAtPick == 7200 && cancelled && p.LifePoints == 8000 && p.Hand.Contains(pb) &&
                !p.TryFindSpellTrap(pb, out _) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"act={act} prompted={prompted} lpAtPick={lpAtPick} cancelled={cancelled} {Lp(p)} " +
                $"inHand={p.Hand.Contains(pb)} chain={e.Chain.Count}");

            // Activated again after the cancel: paid once more, resolves once.
            var again = e.TryActivateSpellTrap(p, pb, fromHand: true);
            if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count > 0)
                e.TrySelectEffectTarget(e.PendingActivation.LegalTargets[0]);
            DrainCombat(e);
            Check("Premature Burial: activated again after the cancel, 800 LP paid once",
                again && p.LifePoints == 7200 && pb.EquippedTo != null && !e.Chain.HasLinks,
                $"again={again} {Lp(p)} equipped={pb.EquippedTo != null} chain={e.Chain.Count}");
        }

        static void SpellEconomicsSkipsTheCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var jammed in new[] { false, true })
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceSetTrap(e, p, SpellEconomics, 0).FaceUp = true;
                if (jammed) SetJammer(e, o);
                var toon = PutInHand(e, p, ToonWorld);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, toon, fromHand: true);
                DrainCombat(e);
                var placed = p.TryFindSpellTrap(toon, out _);
                Check($"Spell Economics: Toon World costs no LP, at activation or resolution ({(jammed ? "negated" : "resolved")})",
                    act && p.LifePoints == 8000 && (jammed ? p.Graveyard.Contains(toon) : placed),
                    $"act={act} {Lp(p)} placed={placed} gy={p.Graveyard.Contains(toon)}");
            }
        }

        static void LastLifePointsEndTheDuelAtActivation(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 999;
                var toon = PutInHand(e, p, ToonWorld);
                Check("Toon World: refused with 999 LP (LP >= cost)",
                    !e.CanActivateSpellTrap(p, toon, fromHand: true) && !e.TryActivateSpellTrap(p, toon, fromHand: true) &&
                    p.LifePoints == 999 && p.Hand.Contains(toon) && !e.Chain.HasLinks,
                    $"{Lp(p)} inHand={p.Hand.Contains(toon)} chain={e.Chain.Count}");
            }

            // Paying the last 1000 LP (after the are-you-sure) loses the duel at once: the opponent's Magic Jammer
            // never gets the chance to negate it.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jam = SetJammer(e, o);
                p.LifePoints = 1000;
                var toon = PutInHand(e, p, ToonWorld);
                var asked = e.TryActivateSpellTrap(p, toon, fromHand: true) && e.PendingActivation != null &&
                            e.PendingActivation.AwaitingLpZeroConfirm && p.LifePoints == 1000;
                var confirmed = asked && e.TryConfirmLpZeroPay();
                Check("Toon World (activation): confirming the last 1000 LP pays them at once and loses the duel " +
                      "before the opponent can respond",
                    asked && confirmed && p.LifePoints == 0 && e.GameOver && e.Winner == o &&
                    e.EndReason == DuelEndReason.LifePoints && !jam.FaceUp && o.Hand.Count == 1,
                    $"asked={asked} confirmed={confirmed} {Lp(p)} over={e.GameOver} " +
                    $"winner={e.Winner?.Name ?? "none"} jamUp={jam.FaceUp} oHand={o.Hand.Count}");
            }
        }

        static void OtherSpellsAndTraps(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Confiscation ("Pay 1000 Life Points. Look at your opponent's hand …"), untargeted.
            foreach (var jammed in new[] { false, true })
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var jam = jammed ? SetJammer(e, o) : null;
                var victim = PutInHand(e, o, Celtic);
                var conf = PutInHand(e, p, Confiscation);
                var act = e.TryActivateSpellTrap(p, conf, fromHand: true);
                DrainCombat(e);
                Check(jammed
                        ? "Confiscation negated by the AI's Magic Jammer: the 1000 LP stay paid"
                        : "Confiscation resolves: 1000 LP paid once, 1 card discarded",
                    act && p.LifePoints == 7000 && p.Graveyard.Contains(conf) && !e.Chain.HasLinks &&
                    (jammed
                        ? o.Graveyard.Contains(jam) && o.Hand.Contains(victim)
                        : o.Graveyard.Contains(victim) && o.Hand.Count == 0),
                    $"act={act} {Lp(p)} jamGy={jam != null && o.Graveyard.Contains(jam)} " +
                    $"victimGy={o.Graveyard.Contains(victim)} oHand={o.Hand.Count} chain={e.Chain.Count}");
            }

            // Brain Control already paid on activation (StartIgnition); it still pays exactly once.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var bc = PutInHand(e, p, BrainControl);
                var act = e.TryActivateSpellTrap(p, bc, fromHand: true);
                Pick(e, foe);
                DrainCombat(e);
                Check("Brain Control: 800 LP paid once, control taken",
                    act && p.LifePoints == 7200 && p.TryFindMonster(foe, out _) && !e.Chain.HasLinks,
                    $"act={act} {Lp(p)} taken={p.TryFindMonster(foe, out _)} chain={e.Chain.Count}");
            }

            // Archfiend's Roar, a Trap activated from the field, answered by the opponent's Seven Tools of the Bandit.
            foreach (var tools in new[] { false, true })
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var soldier = AddToGy(e, p, ArchfiendSoldier);
                var seven = tools ? PlaceSetTrap(e, o, SevenTools, 4) : null;
                var roar = PlaceSetTrap(e, p, ArchfiendsRoar, 0);
                var act = e.CanActivateSpellTrap(p, roar, fromHand: false) &&
                          e.TryActivateSpellTrap(p, roar, fromHand: false);
                var lpAtPick = p.LifePoints;
                Pick(e, soldier);
                DrainCombat(e);
                Check(tools
                        ? "Archfiend's Roar negated by the AI's Seven Tools of the Bandit: the 500 LP stay paid"
                        : "Archfiend's Roar resolves: 500 LP paid once (before the target pick), Archfiend Soldier summoned",
                    act && lpAtPick == 7500 && p.LifePoints == 7500 && p.Graveyard.Contains(roar) && !e.Chain.HasLinks &&
                    (tools
                        ? o.Graveyard.Contains(seven) && o.LifePoints == 7000 && p.Graveyard.Contains(soldier)
                        : p.TryFindMonster(soldier, out _)),
                    $"act={act} lpAtPick={lpAtPick} {Lp(p)} {Lp(o)} roarGy={p.Graveyard.Contains(roar)} " +
                    $"toolsGy={seven != null && o.Graveyard.Contains(seven)} summoned={p.TryFindMonster(soldier, out _)} " +
                    $"chain={e.Chain.Count}");
            }

            // Archfiend's Roar answering an attack declaration (a seated response link): it used to resolve without
            // ever paying its cost.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                p.Graveyard.Clear();
                var soldier = AddToGy(e, p, ArchfiendSoldier);
                var roar = PlaceSetTrap(e, p, ArchfiendsRoar, 0);
                var attacker = PlaceMonster(e, o, ThousandEyesIdol, 2, BattlePosition.Attack, true);
                attacker.ClearAttackFlags();
                e.NotifyPublic();
                if (e.IsAwaitingResponse) e.PassResponse();
                DrainCombat(e);
                var declared = e.TryAttack(o, attacker, null);
                var window = e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                             e.PendingResponse.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, roar, fromHand: false);
                var lpAtPick = p.LifePoints;
                Pick(e, soldier);
                DrainCombat(e);
                Check("Archfiend's Roar (activation) answering an attack: 500 LP paid once, Archfiend Soldier summoned",
                    reached && declared && act && lpAtPick == 7500 && p.LifePoints == 7500 &&
                    p.TryFindMonster(soldier, out _) && p.Graveyard.Contains(roar) && !e.Chain.HasLinks,
                    $"reached={reached} declared={declared} window={window} act={act} lpAtPick={lpAtPick} {Lp(p)} " +
                    $"summoned={p.TryFindMonster(soldier, out _)} roarGy={p.Graveyard.Contains(roar)} chain={e.Chain.Count}");
            }
        }

        static void AiActivationPaysBeforeTheHumanResponds(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var id in new[] { ToonWorld, PrematureBurial })
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                p.Graveyard.Clear();
                o.Graveyard.Clear();
                o.LifePoints = 8000;
                AddToGy(e, o, Celtic);
                var jam = SetJammer(e, p);
                var card = PutInHand(e, o, id);
                var cost = id == ToonWorld ? 1000 : 800;
                var act = e.TryActivateSpellTrap(o, card, fromHand: true);
                var pr = e.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == p;
                var lpAtWindow = o.LifePoints;
                var negated = window && e.TryActivateSpellTrap(p, jam, fromHand: false);
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                Check($"{card.Name} (activation) activated by the AI: {cost} LP are paid before the player's window; " +
                      "negated by the player's Magic Jammer they stay paid",
                    reached && act && window && lpAtWindow == 8000 - cost && negated &&
                    o.LifePoints == 8000 - cost && o.Graveyard.Contains(card) && p.Graveyard.Contains(jam) &&
                    !e.Chain.HasLinks,
                    $"reached={reached} act={act} window={window} lpAtWindow={lpAtWindow} negated={negated} {Lp(o)} " +
                    $"cardGy={o.Graveyard.Contains(card)} chain={e.Chain.Count}");
            }
        }
    }
}
