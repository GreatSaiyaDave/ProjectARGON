using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SimpleTargetUtility batch (v173-dcr): Altar for Tribute, Arsenal Summoner, Cost Down, Des Feral Imp,
    /// Fairy of the Spring. Compile shapes through the real compiler plus mini-duels through the engine
    /// entry points (TryActivateSpellTrap, TryFlipSummon, TrySelectEffectTarget, chain responses).
    /// Turn 1 is always the Player's, turn 2 the AI's. Runtime: <see cref="SimpleTargetUtilityRuntime"/>.
    /// </summary>
    public static class SimpleTargetUtilityRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int AltarForTribute = 21070956;   // Normal Trap
        const int ArsenalSummoner = 85489096;   // FLIP Spellcaster 900/2000?
        const int CostDown = 23265313;          // Normal Spell
        const int DesFeralImp = 81985784;       // FLIP Reptile
        const int FairyOfTheSpring = 20188127;  // Normal Spell

        const int Celtic = 91152256;            // Celtic Guardian — one of Arsenal's five excepted names
        const int Kuriboh = 40640057;
        const int PotOfGreed = 55144522;
        const int GiantSoldier = 13039848;      // Normal Lv3 1300/2000
        const int PrematureBurial = 70828912;   // Equip Spell: Pay 800 LP, then target …

        static readonly string[] ArsenalExcept =
        {
            "Celtic Guardian",
            "Winged Dragon, Guardian of the Fortress #1",
            "Winged Dragon, Guardian of the Fortress #2",
            "Guardian of the Labyrinth",
            "The Reliable Guardian"
        };

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SimpleTargetUtility: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            AltarDuels(db, pDeck, aDeck, Check);
            CostDownDuels(db, pDeck, aDeck, Check);
            ArsenalDuels(db, pDeck, aDeck, Check);
            DesFeralImpDuels(db, pDeck, aDeck, Check);
            FairyOfTheSpringDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A fresh duel with empty boards, hands and GYs; the player is in their Main Phase 1.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static bool Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            Drive(e, _ => false);
            DrainCombat(e);
        }

        /// <summary>Ends the current turn (passing open windows) and passes the next turn's opening windows.</summary>
        static void EndTurn(DuelEngine e)
        {
            Drive(e, _ => false);
            if (e.StandbyPausedFor != null) e.ContinueAfterStandby();
            e.TryEndTurnSafe(e.TurnPlayer);
            Drive(e, _ => false);
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "-";
            var at = new List<string>();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) at.Add(tag + ".hand");
                if (who.Graveyard.Contains(c)) at.Add(tag + ".gy");
                if (who.TryFindMonster(c, out _)) at.Add(tag + ".mz");
                if (who.TryFindSpellTrap(c, out _)) at.Add(tag + ".stz");
            }
            return string.Join("+", at);
        }

        /// <summary>Pending state after an activation: which window is open, and what it offers.</summary>
        static string St(DuelEngine e)
        {
            var pend = e.PendingActivation;
            return $"await={e.IsAwaitingEffectTarget} resp={e.IsAwaitingResponse} " +
                   $"pend={(pend == null ? "-" : $"{pend.AwaitingIgnitionCost}/{pend.TargetKind}/{pend.LegalTargets.Count}")} " +
                   $"prompt=\"{pend?.Prompt ?? "-"}\"";
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) =>
            CardTextEffectCompiler.Compile(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var altar = Prog(db, AltarForTribute);
            var ac = altar?.ClauseList.Count == 1 ? altar.ClauseList[0] : null;
            Check("Altar for Tribute FullyCompiled: one Activate clause, send-you-control cost, no target choice",
                altar != null && altar.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(AltarForTribute)) == CardEffectStatusKind.Implemented &&
                ac != null && ac.Timing == EffectTiming.Activate &&
                ac.Action == EffectActionKind.GainLpEqualToCostAmount &&
                ac.RequiresSendOtherYouControl && !ac.RequiresTargetChoice && ac.MakesChainLink &&
                EffectVocabulary.ResolutionOf(ac) == EffectResolutionKind.GainLp,
                Describe(altar));

            var costDown = Prog(db, CostDown);
            var cc = costDown?.ClauseList.Count == 1 ? costDown.ClauseList[0] : null;
            Check("Cost Down FullyCompiled: one Activate clause with a discard cost and Amount 2",
                costDown != null && costDown.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(CostDown)) == CardEffectStatusKind.Implemented &&
                cc != null && cc.Timing == EffectTiming.Activate &&
                cc.Action == EffectActionKind.ReduceHandLevelsUntilEndOfTurn &&
                cc.RequiresDiscardCost && cc.Amount == 2 && !cc.RequiresTargetChoice && cc.MakesChainLink &&
                cc.DiscardCostAttribute == "*" && cc.DiscardCostCount == 1 &&
                EffectVocabulary.ResolutionOf(cc) == EffectResolutionKind.ModifyStats,
                cc == null ? Describe(costDown) : $"attr={cc.DiscardCostAttribute ?? "null"} n={cc.DiscardCostCount}");

            var arsenal = Prog(db, ArsenalSummoner);
            var ar = arsenal?.ClauseList.Count == 1 ? arsenal.ClauseList[0] : null;
            var excepted = ar?.ExceptNamedCard?.Split('|') ?? new string[0];
            Check("Arsenal Summoner FullyCompiled: one Flip Guardian search, deck zone, five excepted names",
                arsenal != null && arsenal.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(ArsenalSummoner)) == CardEffectStatusKind.Implemented &&
                ar != null && ar.Timing == EffectTiming.Flip &&
                ar.Action == EffectActionKind.AddFromDeckToHand &&
                ar.Zone == EffectZoneFilter.ControllerDeckCards &&
                ar.TargetSeriesName == "Guardian" && ar.RequiresTargetChoice &&
                excepted.Length == 5 && ArsenalExcept.All(n => excepted.Contains(n)),
                ar == null ? Describe(arsenal) : $"zone={ar.Zone} except={ar.ExceptNamedCard}");

            var feral = Prog(db, DesFeralImp);
            var fc = feral?.ClauseList.Count == 1 ? feral.ClauseList[0] : null;
            Check("Des Feral Imp FullyCompiled: one Flip clause targeting the controller's GY, shuffle into Deck",
                feral != null && feral.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(DesFeralImp)) == CardEffectStatusKind.Implemented &&
                fc != null && fc.Timing == EffectTiming.Flip &&
                fc.Action == EffectActionKind.ReturnChosenToDeckShuffle &&
                fc.Zone == EffectZoneFilter.ControllerGyCards &&
                fc.RequiresTargetChoice && fc.IsPsctTarget && fc.TargetCount == 1 && fc.MakesChainLink,
                Describe(feral));

            var fairy = Prog(db, FairyOfTheSpring);
            var fy = fairy?.ClauseList.Count == 1 ? fairy.ClauseList[0] : null;
            Check("Fairy of the Spring FullyCompiled: one Activate clause, GY Equip zone, activation lock flag",
                fairy != null && fairy.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(FairyOfTheSpring)) == CardEffectStatusKind.Implemented &&
                fy != null && fy.Timing == EffectTiming.Activate &&
                fy.Action == EffectActionKind.AddFromGyToHand &&
                fy.Zone == EffectZoneFilter.ControllerGyEquipSpells &&
                fy.RequiresTargetChoice && fy.IsPsctTarget && fy.LocksAddedCardActivationThisTurn &&
                fy.MakesChainLink,
                Describe(fairy));
        }

        // ───────────────────────────── Altar for Tribute ─────────────────────────────

        static void AltarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The sent monster's PRINTED ATK (1300, no modifiers) is gained; the cost monster and the trap
            // both end in the GY.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var lp = p.LifePoints;
                var victim = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Attack, true);
                var altar = PlaceSetTrap(e, p, AltarForTribute, 1);
                var act = e.TryActivateSpellTrap(p, altar, fromHand: false);
                var prompted = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                               e.PendingActivation.TargetKind == EffectTargetKind.SendMonsterYouControlToGy;
                var offered = prompted && Legal(e).Contains(victim);
                Drive(e, c => c == victim);
                Settle(e);
                // The cost is a forced single choice (the only monster you control), so the engine may
                // auto-pay it without seating a pick — outcomes are the contract, not the prompt.
                Check("Altar for Tribute: the picked monster is sent (printed 1300 ATK gained), trap to the GY",
                    act && p.Graveyard.Contains(victim) &&
                    p.LifePoints == lp + 1300 && p.Graveyard.Contains(altar) && !e.Chain.HasLinks,
                    $"act={act} prompted={prompted} offered={offered} lp={lp}->{p.LifePoints} " +
                    $"victim={Where(e, victim)} altar={Where(e, altar)} chain={e.Chain.Count}");
            }

            // No monster to send: the trap cannot be activated at all.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var altar = PlaceSetTrap(e, p, AltarForTribute, 1);
                Check("Altar for Tribute: refused with an empty monster zones",
                    !e.CanActivateSpellTrap(p, altar, fromHand: false) &&
                    !e.TryActivateSpellTrap(p, altar, fromHand: false) &&
                    p.LifePoints == 8000 && Where(e, altar) == "P.stz" && !e.Chain.HasLinks,
                    $"lp={p.LifePoints} altar={Where(e, altar)} {e.PendingActivation?.TargetKind}");
            }
        }

        // ───────────────────────────── Cost Down ─────────────────────────────

        static void CostDownDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Discard pick, held monster reduced 7→5, a monster added to the hand afterwards is reduced too
            // (hand-wide stamp), and the reduction is gone on the player's next turn.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var gaia = PutInHand(e, p, 6368038); // Gaia the Fierce Knight Lv7
                var fodder = PutInHand(e, p, PotOfGreed);
                var first = PutInHand(e, p, Kuriboh);
                var cd = PutInHand(e, p, CostDown);

                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                var pend = e.PendingActivation;
                var offered = act && e.IsAwaitingEffectTarget && pend != null &&
                              pend.AwaitingIgnitionCost && Legal(e).Contains(fodder) &&
                              !Legal(e).Contains(cd);
                var offeredState = St(e);
                Drive(e, c => c == fodder);
                Settle(e);
                var reducedNow = gaia.Level == 5 && gaia.Def != null && gaia.Def.level == 7;

                var later = PutInHand(e, p, GiantSoldier);
                e.NotifyPublic();
                var reducedLater = later.Level == 1; // Lv3 − 2

                Check("Cost Down: player picks the discard, held Level 7 becomes 5, a later addition becomes 1",
                    act && offered && p.Graveyard.Contains(fodder) && p.Graveyard.Contains(cd) &&
                    p.Hand.Contains(gaia) && p.Hand.Contains(first) && reducedNow && reducedLater,
                    $"act={act} offered={offered} fodder={Where(e, fodder)} cd={Where(e, cd)} " +
                    $"gaia={gaia.Level}/{gaia.Def?.level} later={later.Level} hand={p.Hand.Count} {offeredState}");

                EndTurn(e); // AI's turn
                EndTurn(e); // back to the player
                Check("Cost Down: the stamp expires with the turn — Level 7 again",
                    e.TurnNumber >= 3 && gaia.Level == 7,
                    $"turn={e.TurnNumber} gaia={gaia.Level}");
            }

            // The reduced Level is live on the field too ("even after they are Summoned"): a Level 5 Normal
            // monster needs no tribute once it reads 3.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var five = db.GetAllCards().FirstOrDefault(d =>
                    d != null && d.type == "Normal Monster" && d.level == 5 && !d.IsExtraDeck && d.atk > 0);
                if (five == null)
                {
                    Check("Cost Down: a Level 5 Normal monster exists in the corpus (summon-after-reduce case)",
                        false, "corpus has no Level 5 Normal Monster");
                    return;
                }

                var held = PutInHand(e, p, five.id);
                PutInHand(e, p, PotOfGreed);
                var cd = PutInHand(e, p, CostDown);
                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                var offeredState = St(e);
                Drive(e, c => c != cd && c != held);
                Settle(e);
                var reduced = held.Level == 3;
                var summoned = reduced && e.TryNormalSummon(p, held, asSet: false);
                Settle(e);
                Check("Cost Down: the reduced Level (5→3) lets the monster Normal Summon without a tribute",
                    act && reduced && summoned && p.TryFindMonster(held, out _) && held.Level == 3,
                    $"act={act} reduced={reduced} summoned={summoned} level={held.Level} " +
                    $"field={Where(e, held)} {offeredState}");
            }

            // Two copies in one turn accumulate: a second Cost Down adds its own reduction, and the total
            // survives a board refresh instead of collapsing to the last copy's stamp.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var gaia = PutInHand(e, p, 6368038); // Gaia the Fierce Knight Lv7
                var f1 = PutInHand(e, p, PotOfGreed);
                var f2 = PutInHand(e, p, Kuriboh);
                var cd1 = PutInHand(e, p, CostDown);
                var cd2 = PutInHand(e, p, CostDown);

                var a1 = e.TryActivateSpellTrap(p, cd1, fromHand: true);
                var offered1 = a1 && e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                               e.PendingActivation.AwaitingIgnitionCost && Legal(e).Contains(f1) &&
                               !Legal(e).Contains(cd1);
                Drive(e, c => c == f1);
                Settle(e);
                var afterFirst = gaia.Level == 5; // 7 − 2

                var a2 = e.TryActivateSpellTrap(p, cd2, fromHand: true);
                var offered2 = a2 && e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                               e.PendingActivation.AwaitingIgnitionCost && Legal(e).Contains(f2) &&
                               !Legal(e).Contains(cd2);
                Drive(e, c => c == f2);
                Settle(e);
                var afterSecond = gaia.Level == 3; // 7 − 2 − 2

                // Force a board refresh (simulating any unrelated state change) and confirm the accumulated
                // stamp is still 4, not the last copy's 2.
                e.NotifyPublic();
                var afterRefresh = gaia.Level == 3;

                EndTurn(e); // AI's turn
                EndTurn(e); // back to the player
                var expired = gaia.Level == 7;
                Check("Cost Down: two copies stack to −4 and keep the stack across a board refresh, gone next turn",
                    offered1 && offered2 && afterFirst && afterSecond && afterRefresh && expired,
                    $"offered1={offered1} offered2={offered2} first={afterFirst} second={afterSecond} " +
                    $"refresh={afterRefresh} expired={expired} gaia={gaia.Level}/{gaia.Def?.level} turn={e.TurnNumber}");
            }
        }

        // ───────────────────────────── Arsenal Summoner ─────────────────────────────

        static void ArsenalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var seriesPick = db.GetAllCards().FirstOrDefault(d =>
                d != null && !d.IsExtraDeck && d.IsMonster && d.name != null &&
                d.name.IndexOf("Guardian", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Array.IndexOf(ArsenalExcept, d.name) < 0);
            if (seriesPick == null)
            {
                Check("Arsenal Summoner: a non-excepted Guardian-series card exists in the corpus",
                    false, "corpus has no non-excepted Guardian card");
                return;
            }

            {
                // The FLIP search offers only series cards minus the five excepted names, and adds the pick.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Add(seriesPick.id);
                p.Deck.Add(Celtic); // excepted — must never be offered
                var arsenal = PlaceMonster(e, p, ArsenalSummoner, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, arsenal);
                var offered = e.IsAwaitingEffectTarget && Legal(e).Any(c => c != null && c.CardId == seriesPick.id) &&
                              !Legal(e).Any(c => c != null && c.CardId == Celtic);
                var prompt = e.PendingActivation?.Prompt ?? "";
                Drive(e, c => c != null && c.CardId == seriesPick.id);
                Settle(e);
                var added = p.Hand.Any(c => c != null && c.CardId == seriesPick.id) &&
                            !p.Hand.Any(c => c != null && c.CardId == Celtic);
                Check("Arsenal Summoner: FLIP offers only the series minus the five excepted names, then adds it",
                    flipped && offered && added && arsenal.FaceUp &&
                    prompt.IndexOf("Deck", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"flipped={flipped} offered={offered} added={added} prompt=\"{prompt}\" " +
                    $"hand={string.Join(",", p.Hand.Select(c => c.Name))}");
            }

            {
                // No Guardian series card in the Deck: the FLIP effect resolves without adding anything.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Add(Celtic);
                var arsenal = PlaceMonster(e, p, ArsenalSummoner, 0, BattlePosition.Defense, false);
                var handBefore = p.Hand.Count;
                var flipped = e.TryFlipSummon(p, arsenal);
                var offered = Legal(e);
                Drive(e, _ => true);
                Settle(e);
                Check("Arsenal Summoner: only excepted names in the Deck — nothing can be picked or added",
                    flipped && offered.Count == 0 && p.Hand.Count == handBefore &&
                    p.Hand.All(c => c == null || c.CardId != Celtic),
                    $"flipped={flipped} offered={offered.Count} hand={p.Hand.Count}");
            }
        }

        // ───────────────────────────── Des Feral Imp ─────────────────────────────

        static void DesFeralImpDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The GY target is shuffled into the controller's Deck.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var inGy = AddToGy(e, p, GiantSoldier);
                var deckBefore = p.Deck.Count;
                var imp = PlaceMonster(e, p, DesFeralImp, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, imp);
                var offered = e.IsAwaitingEffectTarget && Legal(e).Contains(inGy);
                Drive(e, c => c == inGy);
                Settle(e);
                Check("Des Feral Imp: the GY target is shuffled into the controller's Deck",
                    flipped && offered && !p.Graveyard.Contains(inGy) &&
                    p.Deck.Count == deckBefore + 1 && p.Deck.Contains(inGy.CardId) && imp.FaceUp,
                    $"flipped={flipped} offered={offered} inGy={p.Graveyard.Contains(inGy)} " +
                    $"deck={deckBefore}->{p.Deck.Count} gy={Where(e, inGy)}");
            }

            // Empty GY: nothing to target, nothing moves.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var deckBefore = p.Deck.Count;
                var imp = PlaceMonster(e, p, DesFeralImp, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, imp);
                var offered = Legal(e);
                Drive(e, _ => true);
                Settle(e);
                Check("Des Feral Imp: an empty GY offers no target and shuffles nothing",
                    flipped && offered.Count == 0 && p.Deck.Count == deckBefore && p.Graveyard.Count == 0,
                    $"flipped={flipped} offered={offered.Count} deck={p.Deck.Count} gy={p.Graveyard.Count}");
            }
        }

        // ───────────────────────────── Fairy of the Spring ─────────────────────────────

        static void FairyOfTheSpringDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The Equip added from the GY cannot be activated this turn; a second copy in the hand still can.
            // On the player's next turn the lock is gone.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var pGyMonster = AddToGy(e, p, GiantSoldier);
                var equip = AddToGy(e, p, PrematureBurial);
                PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, true); // an equip target on the field
                var spare = PutInHand(e, p, PrematureBurial); // second copy: never locked
                var fairy = PutInHand(e, p, FairyOfTheSpring);

                var baselineSpare = e.CanActivateSpellTrap(p, spare, fromHand: true);
                var act = e.TryActivateSpellTrap(p, fairy, fromHand: true);
                var preState = St(e) + $" respCards={(e.PendingResponse?.LegalCards == null ? "-" : string.Join(",", e.PendingResponse.LegalCards.Select(c => c?.Name ?? "?")))}";
                // The seated target pick opens after the activation's response window (Wingbeat idiom).
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var offered = e.IsAwaitingEffectTarget && Legal(e).Contains(equip);
                var offeredState = St(e);
                Drive(e, c => c == equip);
                Settle(e);

                var addedToHand = p.Hand.Contains(equip);
                var blockedNow = e.CanActivateSpellTrap(p, equip, fromHand: true);
                var spareStillOk = e.CanActivateSpellTrap(p, spare, fromHand: true);
                var blockedLog = addedToHand && !blockedNow && spareStillOk;

                Check("Fairy of the Spring: the GY Equip is added, blocked this turn, while a spare copy is not",
                    act && offered && blockedLog && p.Graveyard.Contains(fairy) && !p.Graveyard.Contains(equip),
                    $"act={act} offered={offered} added={addedToHand} blocked={blockedNow} " +
                    $"spare={spareStillOk} fairy={Where(e, fairy)} equip={Where(e, equip)} pre=[{preState}] post=[{offeredState}]");

                EndTurn(e); // AI's turn
                EndTurn(e); // back to the player
                var unblockedNextTurn = e.TurnNumber >= 3 && e.CanActivateSpellTrap(p, equip, fromHand: true);
                Check("Fairy of the Spring: the lock expires with the turn — activatable on the player's next turn",
                    unblockedNextTurn, $"turn={e.TurnNumber} legal={unblockedNextTurn}");
                GC.KeepAlive(pGyMonster);
            }
        }
    }
}
