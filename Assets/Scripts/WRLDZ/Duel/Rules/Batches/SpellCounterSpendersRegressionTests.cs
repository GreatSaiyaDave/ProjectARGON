using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SpellCounterSpenders" (<see cref="SpellCounterSpendersAtoms"/>, <see cref="SpellCounterSpendersRuntime"/>):
    /// Magical Plant Mandragola, Mega Ton Magical Cannon, Miracle Restoring, Anti-Spell, Apprentice Magician, Armor Exe,
    /// Legendary Flame Lord and Pitch-Black Power Stone. Compile shapes, mini-duels through the real entry points, and the
    /// near-misses that must stay closed.
    /// </summary>
    public static class SpellCounterSpendersRegressionTests
    {
        const int Mandragola = 7802006;
        const int MegaTon = 32062913;
        const int MiracleRestoring = 68334074;
        const int AntiSpell = 53112492;
        const int Apprentice = 9156135;
        const int ArmorExe = 7180418;
        const int FlameLord = 60258960;
        const int PitchBlack = 34029630;

        const int Breaker = 71413901;      // holder, "(max. 1)"
        const int Rml = 70791313;          // holder, "(max. 3)", +1 when a Spell resolves
        const int Marionette = 8034697;    // holder, no max, +1 when a Spell resolves
        const int PotOfGreed = 55144522;
        const int DarkMagician = 46986414;
        const int BusterBlader = 78193831;
        const int DarkMagicianGirl = 38033121;
        const int GeminiElf = 69140098;    // a plain 1900 ATK body, not a holder
        const int OldVindictive = 45141844; // Level 2 Spellcaster
        const int MirrorForce = 44095762;
        const int DarkHole = 53129443;
        const int RoyalDecree = 51452091;
        const int Mst = 5318639;           // Mystical Space Typhoon

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("SpellCounterSpenders: " + n, ok, d ?? "");
            Shapes(db, Check);
            NearMisses(db, Check);
            MandragolaFlip(db, pDeck, aDeck, Check);
            MegaTonCannon(db, pDeck, aDeck, Check);
            MiracleRestoringDuel(db, pDeck, aDeck, Check);
            AntiSpellDuel(db, pDeck, aDeck, Check);
            ApprenticeMagicianDuel(db, pDeck, aDeck, Check);
            ArmorExeDuel(db, pDeck, aDeck, Check);
            FlameLordDuel(db, pDeck, aDeck, Check);
            PitchBlackDuel(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static CardInstance Mon(DuelEngine e, DuelistState who, int id, int zone, int counters = 0, bool faceUp = true)
        {
            var c = PlaceMonster(e, who, id, zone, faceUp ? BattlePosition.Attack : BattlePosition.Defense, faceUp);
            c.Counters = counters;
            return c;
        }

        static CardInstance FaceUpSt(DuelEngine e, DuelistState who, int id, int zone, int counters)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            c.Counters = counters;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static bool MyChainWindow(DuelEngine e, DuelistState who) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who;

        static bool OnField(DuelEngine e, CardInstance c) => c != null && e.ControllerOf(c) != null;

        static bool InGy(DuelEngine e, CardInstance c) =>
            c != null && (e.Player.Graveyard.Contains(c) || e.Opponent.Graveyard.Contains(c));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}" +
            $"[{string.Join(",", Legal(e).Select(c => c.Name))}] response={e.IsAwaitingResponse} " +
            $"links={e.Chain.Count} phase={e.Phase} turn={e.TurnPlayer?.Name}#{e.TurnNumber}";

        static string Ctr(params CardInstance[] cards) =>
            string.Join(" ", cards.Select(c => $"{c?.Name}={c?.Counters}"));

        static CompiledCardProgram Comp(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Shape(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [{string.Join("; ", p.ClauseList.Where(c => c != null).Select(EffectVocabulary.Describe))}]";

        static CompiledCardProgram ProbeText(int id, string type, string race, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "SpellCounterSpenders Probe " + id, type = type, race = race,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                desc = desc
            });

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Action<string, bool, string> Check)
        {
            var mand = Comp(db, Mandragola);
            Check("Mandragola: one mandatory FLIP clause, 1 counter on each face-up holder (no target)",
                mand != null && mand.FullyCompiled && Implemented(db, Mandragola) && mand.ClauseList.Count == 1 &&
                mand.ClauseList[0].Timing == EffectTiming.Flip &&
                mand.ClauseList[0].Action == EffectActionKind.PlaceSpellCounterOnEachFaceUpHolder &&
                mand.ClauseList[0].Amount == 1 && !mand.ClauseList[0].IsOptional &&
                !mand.ClauseList[0].RequiresTargetChoice, Shape(mand));

            var mega = Comp(db, MegaTon);
            var mc = mega?.ClauseList.FirstOrDefault();
            Check("Mega Ton Magical Cannon: cost 10 counters from your side, destroy every card the opponent controls",
                mega != null && mega.FullyCompiled && Implemented(db, MegaTon) && mega.ClauseList.Count == 1 &&
                mc.Timing == EffectTiming.Activate && mc.Action == EffectActionKind.Destroy &&
                mc.Zone == EffectZoneFilter.OppAnyCardOnField && !mc.RequiresTargetChoice &&
                mc.RemoveSpellCountersOnYourSide == 10 && mc.RequiresRemoveSpellCounters == 0 &&
                mc.ActivationCondition == SpellCounterSpendersRuntime.OpponentControlsACard, Shape(mega));

            var mir = Comp(db, MiracleRestoring);
            var mrc = mir?.ClauseList.FirstOrDefault();
            Check("Miracle Restoring: cost 2 counters from your side, target \"Dark Magician\" or \"Buster Blader\" in your GY",
                mir != null && mir.FullyCompiled && Implemented(db, MiracleRestoring) && mir.ClauseList.Count == 1 &&
                mrc.Action == EffectActionKind.SpecialSummonFromGy && mrc.Zone == EffectZoneFilter.ControllerGyMonsters &&
                mrc.NamedCard == "Dark Magician" && mrc.AltNamedCard == "Buster Blader" && mrc.RequiresTargetChoice &&
                mrc.RemoveSpellCountersOnYourSide == 2, Shape(mir));

            var anti = Comp(db, AntiSpell);
            var ac = anti?.ClauseList.FirstOrDefault();
            Check("Anti-Spell: a Counter Trap answering a Spell Card activation, cost 2 counters from your side",
                anti != null && anti.FullyCompiled && Implemented(db, AntiSpell) && anti.ClauseList.Count == 1 &&
                ac.Timing == EffectTiming.ChainLinkActivated && ac.Action == EffectActionKind.NegateActivation &&
                ac.NegateRespondsToSpell && !ac.NegateRespondsToTrap && ac.DestroyNegatedCard &&
                ac.NegateCardActivationOnly && !ac.NegateOpponentActivationOnly && ac.RemoveSpellCountersOnYourSide == 2,
                Shape(anti));

            var app = Comp(db, Apprentice);
            var a1 = app?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.ThisCardSummoned);
            var a2 = app?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.SentFromFieldToGy);
            Check("Apprentice Magician: mandatory Summon trigger targeting a holder; optional battle-destroyed face-down summon",
                app != null && app.FullyCompiled && Implemented(db, Apprentice) && app.ClauseList.Count == 2 &&
                a1 != null && a1.Action == EffectActionKind.PlaceSpellCounterOnTarget && !a1.IsOptional &&
                a1.Zone == EffectZoneFilter.FieldFaceUpSpellCounterHolders && a1.RequiresTargetChoice &&
                !a1.RequiresThisNormalSummoned && !a1.RequiresThisSpecialSummoned && !a1.RequiresThisFlipSummoned &&
                a2 != null && a2.Action == EffectActionKind.SpecialSummonLevelRaceFromDeckFaceDownDefense &&
                a2.IsOptional && a2.RequiresThisDestroyedByBattle && a2.Amount == 2 && a2.RaceFilter == "Spellcaster",
                Shape(app));

            var armor = Comp(db, ArmorExe);
            Check("Armor Exe: summon-turn attack lock (Normal / Flip / Special) and an each-Standby counter upkeep",
                armor != null && armor.FullyCompiled && Implemented(db, ArmorExe) && armor.ClauseList.Count == 2 &&
                armor.ClauseList.Exists(c => c.Action == EffectActionKind.CannotAttackTurnSpecialSummoned &&
                                             c.CannotAttackAlsoTurnNormalOrFlipSummoned) &&
                armor.ClauseList.Exists(c => c.Timing == EffectTiming.StandbyPhase &&
                                             c.Action == EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis &&
                                             c.EachStandbyPhase && !c.MakesChainLink), Shape(armor));

            var lfl = Comp(db, FlameLord);
            Check("Legendary Flame Lord: +1 per Spell activation (no max); 3 counters → destroy all other monsters",
                lfl != null && lfl.FullyCompiled && Implemented(db, FlameLord) && lfl.ClauseList.Count == 2 &&
                lfl.ClauseList.Exists(c => c.Action == EffectActionKind.PlaceSpellCounters &&
                                           c.PlaceCounterOnSpellActivate && c.CounterMax == 0 && c.Amount == 1) &&
                lfl.ClauseList.Exists(c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
                                           c.Side == EffectSide.Both && c.Zone == EffectZoneFilter.FieldMonsters &&
                                           c.ExceptThisCard && c.RequiresRemoveSpellCounters == 3) &&
                !lfl.ClauseList.Exists(c => c.Action == EffectActionKind.Destroy && !c.ExceptThisCard), Shape(lfl));

            var pbs = Comp(db, PitchBlack);
            Check("Pitch-Black Power Stone: 3 counters on activation, a once-per-turn move (your turn), self-destruction",
                pbs != null && pbs.FullyCompiled && Implemented(db, PitchBlack) && pbs.ClauseList.Count == 3 &&
                pbs.ClauseList.Exists(c => c.Timing == EffectTiming.Activate &&
                                           c.Action == EffectActionKind.PlaceSpellCounters && c.Amount == 3) &&
                pbs.ClauseList.Exists(c => c.Timing == EffectTiming.FaceUpQuickEffect &&
                                           c.Action == EffectActionKind.MoveSpellCounterFromThisToTarget &&
                                           c.ExceptThisCard && c.OncePerTurn && c.ControllerTurnOnly) &&
                pbs.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                           c.Action == EffectActionKind.DestroyThisWhenLastSpellCounterRemoved), Shape(pbs));

            Check("vocabulary: every new action maps to a shared resolution kind",
                new[]
                {
                    EffectActionKind.PlaceSpellCounterOnEachFaceUpHolder, EffectActionKind.PlaceSpellCounterOnTarget,
                    EffectActionKind.SpecialSummonLevelRaceFromDeckFaceDownDefense,
                    EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis,
                    EffectActionKind.MoveSpellCounterFromThisToTarget, EffectActionKind.DestroyThisWhenLastSpellCounterRemoved
                }.All(a => EffectVocabulary.ResolutionOf(new EffectClause { Action = a }) != EffectResolutionKind.UniqueException) &&
                EffectVocabulary.CostOf(new EffectClause { RemoveSpellCountersOnYourSide = 2 }) == EffectCostKind.RemoveCounters,
                "");
        }

        static void NearMisses(CardDatabase db, Action<string, bool, string> Check)
        {
            var trapAnswer = ProbeText(990001, "Trap Card", "Counter",
                "Remove 2 Spell Counters on your side of the field to negate the activation of a Trap Card and destroy it.");
            Check("near-miss: the Anti-Spell sentence against a Trap Card does not compile",
                trapAnswer != null && !trapAnswer.FullyCompiled, Shape(trapAnswer));

            var monsterFlip = ProbeText(990002, "Flip Effect Monster", "Plant",
                "FLIP: Place 1 Spell Counter on each face-up monster on the field.");
            Check("near-miss: a FLIP placing counters on every face-up monster (no holder filter) does not compile",
                monsterFlip != null && !monsterFlip.FullyCompiled, Shape(monsterFlip));

            var noExcept = ProbeText(990003, "Effect Monster", "Spellcaster",
                "Remove 3 Spell Counters from this card to destroy all monsters on the field.");
            Check("near-miss: a counter cost without \"except this card\" stays closed (no free Dark Hole)",
                noExcept != null && !noExcept.FullyCompiled, Shape(noExcept));

            var oneName = ProbeText(990004, "Trap Card", "Normal",
                "Remove 2 Spell Counters from your side of the field, then target 1 \"Dark Magician\" in your Graveyard; " +
                "Special Summon that target.");
            Check("near-miss: Miracle Restoring's sentence with one name does not compile through the two-name atom",
                oneName != null && !oneName.FullyCompiled, Shape(oneName));

            var dh = Comp(db, DarkHole);
            Check("control: Dark Hole still destroys all monsters on both fields",
                dh != null && dh.FullyCompiled && dh.ClauseList.Count == 1 &&
                dh.ClauseList[0].Action == EffectActionKind.Destroy && dh.ClauseList[0].Side == EffectSide.Both &&
                dh.ClauseList[0].Zone == EffectZoneFilter.FieldMonsters && !dh.ClauseList[0].ExceptThisCard, Shape(dh));
        }

        // ───────────────────────────── Mandragola ─────────────────────────────

        static void MandragolaFlip(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mand = Mon(e, p, Mandragola, 0, faceUp: false);
            var breaker = Mon(e, p, Breaker, 1);
            var fullRml = Mon(e, o, Rml, 0, 3);
            var setRml = Mon(e, o, Rml, 1, 0, faceUp: false);
            var elf = Mon(e, o, GeminiElf, 2);
            var mar = Mon(e, o, Marionette, 3, 2);
            var flipped = e.TryFlipSummon(p, mand);
            Settle(e);
            Check("Mandragola FLIP: +1 on each face-up holder (both fields); not on a capped one, a Set one or a non-holder",
                flipped && mand.FaceUp && breaker.Counters == 1 && fullRml.Counters == 3 && setRml.Counters == 0 &&
                elf.Counters == 0 && mar.Counters == 3 && e.PendingActivation == null,
                $"flipped={flipped} {Ctr(breaker, fullRml, setRml, elf, mar)} {State(e)}");
        }

        // ───────────────────────────── Mega Ton Magical Cannon ─────────────────────────────

        static void MegaTonCannon(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                // Exactly 10 counters between two cards: no choice, paid at once.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mar = Mon(e, p, Marionette, 0, 7);
                var rml = Mon(e, p, Rml, 1, 3);
                var oMon = Mon(e, o, GeminiElf, 0);
                var oSet = PlaceSetTrap(e, o, MirrorForce, 0);
                var cannon = PutInHand(e, p, MegaTon);
                var act = e.TryActivateSpellTrap(p, cannon, fromHand: true);
                var noPrompt = e.PendingActivation == null;
                Settle(e);
                // Both holders take a counter when the Cannon resolves ("Each time a Spell Card is activated").
                Check("Mega Ton Magical Cannon: 10 counters removed (no choice), every card the opponent controls destroyed",
                    act && noPrompt && InGy(e, oMon) && InGy(e, oSet) && OnField(e, mar) && OnField(e, rml) &&
                    mar.Counters == 1 && rml.Counters == 1 && InGy(e, cannon),
                    $"act={act} noPrompt={noPrompt} {Ctr(mar, rml)} oMon={InGy(e, oMon)} oSet={InGy(e, oSet)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                Mon(e, p, Marionette, 0, 6);
                Mon(e, p, Rml, 1, 3);
                Mon(e, e.Opponent, GeminiElf, 0);
                var cannon = PutInHand(e, p, MegaTon);
                Check("Mega Ton Magical Cannon: 9 counters cannot pay the cost",
                    !e.CanActivateSpellTrap(p, cannon, fromHand: true), State(e));

                var e2 = Setup(db, pDeck, aDeck);
                Mon(e2, e2.Player, Marionette, 0, 10);
                var cannon2 = PutInHand(e2, e2.Player, MegaTon);
                Check("Mega Ton Magical Cannon: the opponent controls no card — cannot be activated",
                    !e2.CanActivateSpellTrap(e2.Player, cannon2, fromHand: true), State(e2));
            }

            {
                // A real choice: the player taps the cards; Cancel puts the counters back and undoes the activation.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mar = Mon(e, p, Marionette, 0, 8);
                var rml = Mon(e, p, Rml, 1, 3);
                var oMon = Mon(e, o, GeminiElf, 0);
                var cannon = PutInHand(e, p, MegaTon);
                var act = e.TryActivateSpellTrap(p, cannon, fromHand: true);
                var legal = Legal(e);
                var asked = e.IsAwaitingEffectTarget && legal.Contains(mar) && legal.Contains(rml) && legal.Count == 2;
                var tapped = asked && e.TrySelectEffectTarget(rml);
                var afterTap = rml.Counters;
                e.CancelEffectTargeting();
                Settle(e);
                Check("Mega Ton Magical Cannon (human): Cancel at the counter pick refunds it and undoes the activation",
                    act && asked && tapped && afterTap == 2 && rml.Counters == 3 && mar.Counters == 8 &&
                    p.Hand.Contains(cannon) && OnField(e, oMon) && e.PendingActivation == null && !e.Chain.HasLinks,
                    $"act={act} asked={asked} tapped={tapped} afterTap={afterTap} {Ctr(mar, rml)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mar = Mon(e, p, Marionette, 0, 8);
                var rml = Mon(e, p, Rml, 1, 3);
                var oMon = Mon(e, o, GeminiElf, 0);
                var cannon = PutInHand(e, p, MegaTon);
                var act = e.TryActivateSpellTrap(p, cannon, fromHand: true);
                var taps = 0;
                // Tap the Library until it is empty; the rest can then only come from Marionette (paid at once).
                for (var i = 0; i < 5 && e.IsAwaitingEffectTarget && Legal(e).Contains(rml); i++)
                {
                    e.TrySelectEffectTarget(rml);
                    taps++;
                }

                Settle(e);
                Check("Mega Ton Magical Cannon (human): the player's taps decide where the counters come from",
                    act && taps == 3 && InGy(e, oMon) && rml.Counters == 1 && mar.Counters == 2 &&
                    e.PendingActivation == null,
                    $"act={act} taps={taps} {Ctr(mar, rml)} oMon={InGy(e, oMon)} {State(e)}");
            }

            {
                // The AI pays with no prompt.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mar = Mon(e, o, Marionette, 0, 8);
                var rml = Mon(e, o, Rml, 1, 3);
                var pMon = Mon(e, p, GeminiElf, 0);
                var cannon = PutInHand(e, o, MegaTon);
                var act = e.TryActivateSpellTrap(o, cannon, fromHand: true);
                Settle(e);
                Check("Mega Ton Magical Cannon (AI): pays 10 of its 11 counters with no prompt; the player's card is destroyed",
                    reached && act && InGy(e, pMon) && mar.Counters + rml.Counters == 3 && e.PendingActivation == null,
                    $"reached={reached} act={act} {Ctr(mar, rml)} {State(e)}");
            }
        }

        // ───────────────────────────── Miracle Restoring ─────────────────────────────

        static void MiracleRestoringDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var rml = Mon(e, p, Rml, 0, 2);
                var dmg = e.CreateCardInstance(DarkMagicianGirl);
                var dm = e.CreateCardInstance(DarkMagician);
                p.Graveyard.Add(dmg);
                p.Graveyard.Add(dm);
                var mr = PlaceSetTrap(e, p, MiracleRestoring, 0);
                var act = e.TryActivateSpellTrap(p, mr, fromHand: false);
                var legal = Legal(e);
                var onlyDm = !e.IsAwaitingEffectTarget || (legal.Count == 1 && legal[0] == dm);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(dm);
                Settle(e);
                Check("Miracle Restoring: 2 counters removed; \"Dark Magician\" (not Dark Magician Girl) Special Summoned",
                    act && onlyDm && OnField(e, dm) && dm.FaceUp && rml.Counters == 0 && p.Graveyard.Contains(dmg) &&
                    InGy(e, mr),
                    $"act={act} onlyDm={onlyDm} dm={OnField(e, dm)} {Ctr(rml)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var rml = Mon(e, p, Rml, 0, 2);
                var dm = e.CreateCardInstance(DarkMagician);
                var bb = e.CreateCardInstance(BusterBlader);
                p.Graveyard.Add(dm);
                p.Graveyard.Add(bb);
                var mr = PlaceSetTrap(e, p, MiracleRestoring, 0);
                var act = e.TryActivateSpellTrap(p, mr, fromHand: false);
                var legal = Legal(e);
                var asked = e.IsAwaitingEffectTarget && legal.Contains(dm) && legal.Contains(bb);
                var paid = rml.Counters == 0;
                e.CancelEffectTargeting();
                Settle(e);
                Check("Miracle Restoring: Cancel at the target pick puts the 2 counters back and the card stays Set",
                    act && asked && paid && rml.Counters == 2 && !mr.FaceUp && p.TryFindSpellTrap(mr, out _) &&
                    p.Graveyard.Contains(dm) && p.Graveyard.Contains(bb) && !e.Chain.HasLinks,
                    $"act={act} asked={asked} paid={paid} {Ctr(rml)} mrFaceUp={mr.FaceUp} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                Mon(e, p, Rml, 0, 1);
                p.Graveyard.Add(e.CreateCardInstance(DarkMagician));
                var mr = PlaceSetTrap(e, p, MiracleRestoring, 0);
                Check("Miracle Restoring: 1 counter cannot pay the cost",
                    !e.CanActivateSpellTrap(p, mr, fromHand: false), State(e));

                var e2 = Setup(db, pDeck, aDeck);
                Mon(e2, e2.Player, Rml, 0, 2);
                e2.Player.Graveyard.Add(e2.CreateCardInstance(DarkMagicianGirl));
                var mr2 = PlaceSetTrap(e2, e2.Player, MiracleRestoring, 0);
                Check("Miracle Restoring: only Dark Magician Girl in the GY — no legal target",
                    !e2.CanActivateSpellTrap(e2.Player, mr2, fromHand: false), State(e2));
            }

            // The opponent's open game state (the Lua hint timing is the opponent's End Phase): a human whose counters
            // are spread over 2 cards with more than 2 between them taps the cards, then picks the target.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var rml = Mon(e, p, Rml, 0, 2);
                var rml2 = Mon(e, p, Rml, 1, 2);
                var dm = e.CreateCardInstance(DarkMagician);
                p.Graveyard.Add(dm);
                var mr = PlaceSetTrap(e, p, MiracleRestoring, 0);
                var opened = e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
                var act = opened && e.TryActivateSpellTrap(p, mr, fromHand: false);
                var costAsked = e.IsAwaitingEffectTarget && Legal(e).Contains(rml) && Legal(e).Contains(rml2) &&
                                !Legal(e).Contains(dm);
                var tap1 = costAsked && e.TrySelectEffectTarget(rml);
                var stillCost = e.IsAwaitingEffectTarget && Legal(e).Contains(rml) && Legal(e).Contains(rml2);
                var tap2 = stillCost && e.TrySelectEffectTarget(rml);
                var targetAsked = e.IsAwaitingEffectTarget && Legal(e).Count == 1 && Legal(e)[0] == dm;
                var picked = targetAsked && e.TrySelectEffectTarget(dm);
                Settle(e);
                Check("Miracle Restoring: in the opponent's open game state a human taps 2 counters off 1 of 2 cards, " +
                      "then targets Dark Magician",
                    reached && act && costAsked && tap1 && stillCost && tap2 && targetAsked && picked && rml.Counters == 0 &&
                    rml2.Counters == 2 && OnField(e, dm) && dm.FaceUp && InGy(e, mr),
                    $"reached={reached} opened={opened} act={act} cost={costAsked} taps={tap1}/{stillCost}/{tap2} " +
                    $"target={targetAsked} picked={picked} dm={OnField(e, dm)} {Ctr(rml, rml2)} {State(e)}");
            }

            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var rml = Mon(e, p, Rml, 0, 2);
                var rml2 = Mon(e, p, Rml, 1, 1);
                var dm = e.CreateCardInstance(DarkMagician);
                p.Graveyard.Add(dm);
                var mr = PlaceSetTrap(e, p, MiracleRestoring, 0);
                var opened = e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
                var act = opened && e.TryActivateSpellTrap(p, mr, fromHand: false);
                var tap = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(rml2);
                // rml2's only counter is gone: the last one can only come off rml, so it goes at once (rml 2 → 1).
                var forcedRest = rml.Counters == 1 && rml2.Counters == 0 && e.IsAwaitingEffectTarget &&
                                 Legal(e).Count == 1 && Legal(e)[0] == dm;
                e.CancelEffectTargeting();
                var window = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.OpponentOpenState &&
                             e.PendingResponse.Responder == p;
                Check("Miracle Restoring: in the opponent's open game state, Cancel at the target after the counter taps " +
                      "puts every counter back, the card stays Set and the window is offered again",
                    reached && act && tap && forcedRest && rml.Counters == 2 && rml2.Counters == 1 && !mr.FaceUp &&
                    p.TryFindSpellTrap(mr, out _) && p.Graveyard.Contains(dm) && !e.Chain.HasLinks && window,
                    $"reached={reached} opened={opened} act={act} tap={tap} forced={forcedRest} {Ctr(rml, rml2)} " +
                    $"mrFaceUp={mr.FaceUp} window={window} {State(e)}");
            }

            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var rml = Mon(e, p, Rml, 0, 2);
                var rml2 = Mon(e, p, Rml, 1, 2);
                p.Graveyard.Add(e.CreateCardInstance(DarkMagician));
                var mr = PlaceSetTrap(e, p, MiracleRestoring, 0);
                var opened = e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
                var act = opened && e.TryActivateSpellTrap(p, mr, fromHand: false);
                var tap = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(rml2);
                e.CancelEffectTargeting();
                Check("Miracle Restoring: Cancel during the counter taps puts the removed counter back",
                    reached && act && tap && rml.Counters == 2 && rml2.Counters == 2 && !mr.FaceUp && !e.Chain.HasLinks,
                    $"reached={reached} opened={opened} act={act} tap={tap} {Ctr(rml, rml2)} mrFaceUp={mr.FaceUp} {State(e)}");
            }
        }

        // ───────────────────────────── Anti-Spell ─────────────────────────────

        static void AntiSpellDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anti = PlaceSetTrap(e, p, AntiSpell, 0);
                var rml = Mon(e, p, Rml, 0, 2);
                var pot = PutInHand(e, o, PotOfGreed);
                var hand0 = o.Hand.Count;
                e.TryActivateSpellTrap(o, pot, fromHand: true);
                var window = MyChainWindow(e, p);
                var answered = window && e.TryActivateChainResponse(p, anti);
                var noPrompt = e.PendingActivation == null;
                Settle(e);
                Check("Anti-Spell: 2 counters removed (no choice); the Spell is negated and destroyed",
                    reached && window && answered && noPrompt && rml.Counters == 0 && o.Graveyard.Contains(pot) &&
                    o.Hand.Count == hand0 - 1 && p.Graveyard.Contains(anti),
                    $"reached={reached} window={window} answered={answered} noPrompt={noPrompt} {Ctr(rml)} " +
                    $"hand {hand0}->{o.Hand.Count} {State(e)}");
            }

            {
                // A real choice: the player takes both counters from Pitch-Black Power Stone, which is then destroyed.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anti = PlaceSetTrap(e, p, AntiSpell, 0);
                var rml = Mon(e, p, Rml, 0, 2);
                var stone = FaceUpSt(e, p, PitchBlack, 1, 2);
                FieldSpellEffects.RefreshBoard(e);
                var pot = PutInHand(e, o, PotOfGreed);
                e.TryActivateSpellTrap(o, pot, fromHand: true);
                var answered = MyChainWindow(e, p) && e.TryActivateChainResponse(p, anti);
                var asked = e.IsAwaitingEffectTarget && Legal(e).Contains(stone) && Legal(e).Contains(rml);
                var t1 = asked && e.TrySelectEffectTarget(stone);
                var t2 = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(stone);
                Settle(e);
                Check("Anti-Spell (human): the player picks the counters; Pitch-Black Power Stone loses its last one and is destroyed",
                    reached && answered && asked && t1 && t2 && rml.Counters == 2 && InGy(e, stone) &&
                    o.Graveyard.Contains(pot) && p.Graveyard.Contains(anti),
                    $"answered={answered} asked={asked} t1={t1} t2={t2} {Ctr(rml, stone)} stoneGy={InGy(e, stone)} {State(e)}");
            }

            {
                var e = OpponentMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var anti = PlaceSetTrap(e, p, AntiSpell, 0);
                var rml = Mon(e, p, Rml, 0, 2);
                var stone = FaceUpSt(e, p, PitchBlack, 1, 2);
                var pot = PutInHand(e, o, PotOfGreed);
                e.TryActivateSpellTrap(o, pot, fromHand: true);
                var answered = MyChainWindow(e, p) && e.TryActivateChainResponse(p, anti);
                var tapped = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(stone);
                e.CancelEffectTargeting();
                var windowBack = MyChainWindow(e, p);
                Settle(e);
                Check("Anti-Spell (human): Cancel puts the counter back; Anti-Spell stays Set and the Spell resolves",
                    answered && tapped && windowBack && stone.Counters == 2 && !anti.FaceUp &&
                    p.TryFindSpellTrap(anti, out _) && rml.Counters == 3,
                    $"answered={answered} tapped={tapped} windowBack={windowBack} {Ctr(rml, stone)} {State(e)}");
            }

            {
                var e = OpponentMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var anti = PlaceSetTrap(e, p, AntiSpell, 0);
                Mon(e, p, Rml, 0, 1);
                var pot = PutInHand(e, o, PotOfGreed);
                e.TryActivateSpellTrap(o, pot, fromHand: true);
                // With no legal answer the player is not even offered a chain window.
                var window = MyChainWindow(e, p);
                var refused = !window || !e.TryActivateChainResponse(p, anti);
                Settle(e);
                Check("Anti-Spell: 1 counter on your side cannot pay the cost (the Spell resolves)",
                    refused && !anti.FaceUp && p.TryFindSpellTrap(anti, out _) && o.Graveyard.Contains(pot) &&
                    o.Hand.Count == 2,
                    $"window={window} refused={refused} hand={o.Hand.Count} {State(e)}");
            }
        }

        // ───────────────────────────── Apprentice Magician ─────────────────────────────

        static void ApprenticeMagicianDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var breaker = Mon(e, p, Breaker, 1);
                var fullRml = Mon(e, o, Rml, 0, 3);
                var elf = Mon(e, o, GeminiElf, 1);
                var app = PutInHand(e, p, Apprentice);
                var summoned = e.TryNormalSummon(p, app, asSet: false);
                var legal = Legal(e);
                var onlyBreaker = !e.IsAwaitingEffectTarget || (legal.Count == 1 && legal[0] == breaker);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(breaker);
                Settle(e);
                Check("Apprentice Magician: Normal Summoned — 1 counter on the only face-up holder that can take one",
                    summoned && onlyBreaker && breaker.Counters == 1 && fullRml.Counters == 3 && elf.Counters == 0,
                    $"summoned={summoned} onlyBreaker={onlyBreaker} {Ctr(breaker, fullRml, elf)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var breaker = Mon(e, p, Breaker, 1);
                var mar = Mon(e, o, Marionette, 0);
                var app = PutInHand(e, p, Apprentice);
                var summoned = e.TryNormalSummon(p, app, asSet: false);
                var legal = Legal(e);
                var asked = e.IsAwaitingEffectTarget && legal.Contains(breaker) && legal.Contains(mar) &&
                            !legal.Contains(app);
                var picked = asked && e.TrySelectEffectTarget(mar);
                Settle(e);
                Check("Apprentice Magician: the player targets a holder on either field (here the opponent's)",
                    summoned && asked && picked && mar.Counters == 1 && breaker.Counters == 0,
                    $"summoned={summoned} asked={asked} picked={picked} {Ctr(breaker, mar)} {State(e)}");
            }

            {
                // Destroyed by battle: the player Special Summons a Level 2 or lower Spellcaster face-down from the Deck.
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var app = Mon(e, p, Apprentice, 0);
                var elf = Mon(e, o, GeminiElf, 0);
                p.Deck.Clear();
                p.Deck.Add(GeminiElf);
                p.Deck.Add(OldVindictive);
                p.Deck.Add(DarkMagicianGirl);
                var reached = ReachOpponentBattle(e);
                var attacked = reached && e.TryAttack(o, elf, app);
                DrainCombat(e);
                Settle(e);
                var legal = Legal(e);
                var offered = e.IsAwaitingEffectTarget && legal.Count == 1 && legal[0].CardId == OldVindictive;
                var pick = offered ? legal[0] : null;
                var picked = offered && e.TrySelectEffectTarget(pick);
                Settle(e);
                var summoned = p.MonstersOnField().FirstOrDefault(m => m.CardId == OldVindictive);
                Check("Apprentice Magician: destroyed by battle — a Level 2 or lower Spellcaster from the Deck, face-down Defense",
                    attacked && InGy(e, app) && offered && picked && summoned != null && !summoned.FaceUp &&
                    summoned.Position == BattlePosition.Defense && !p.Deck.Contains(OldVindictive) &&
                    p.Deck.Contains(DarkMagicianGirl),
                    $"reached={reached} attacked={attacked} appGy={InGy(e, app)} offered={offered} picked={picked} " +
                    $"summoned={summoned?.Name} faceUp={summoned?.FaceUp} {State(e)}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var app = Mon(e, p, Apprentice, 0);
                var elf = Mon(e, o, GeminiElf, 0);
                p.Deck.Clear();
                p.Deck.Add(OldVindictive);
                p.Deck.Add(GeminiElf);
                var reached = ReachOpponentBattle(e);
                var attacked = reached && e.TryAttack(o, elf, app);
                DrainCombat(e);
                Settle(e);
                var offered = e.IsAwaitingEffectTarget;
                e.CancelEffectTargeting();
                Settle(e);
                Check("Apprentice Magician: the battle-destroyed summon is optional — Cancel declines it",
                    attacked && offered && p.Deck.Contains(OldVindictive) &&
                    !p.MonstersOnField().Any(m => m.CardId == OldVindictive) && e.PendingActivation == null,
                    $"attacked={attacked} offered={offered} {State(e)}");
            }
        }

        // ───────────────────────────── Armor Exe ─────────────────────────────

        static bool ReachPlayerMainAgain(DuelEngine e)
        {
            for (var t = 0; t < 12 && !(e.TurnPlayer == e.Player && e.InMainPhase && e.TurnNumber >= 3); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.InMainPhase && e.TurnNumber >= 3) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == e.Player && e.InMainPhase && e.TurnNumber >= 3;
        }

        static void ArmorExeDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMainAgain(e);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                var armor = PutInHand(e, p, ArmorExe);
                var old = Mon(e, p, GeminiElf, 1);
                var summoned = e.TryNormalSummon(p, armor, asSet: false);
                var battle = e.TryEnterBattlePhase(p);
                Settle(e);
                Check("Armor Exe: cannot attack the turn it is Normal Summoned (another monster can)",
                    reached && summoned && battle && !e.CanAttack(p, armor) && e.CanAttack(p, old),
                    $"reached={reached} summoned={summoned} battle={battle} armorCan={e.CanAttack(p, armor)} " +
                    $"elfCan={e.CanAttack(p, old)} {State(e)}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMainAgain(e);
                ClearBoard(e);
                var p = e.Player;
                var armor = Mon(e, p, ArmorExe, 0, faceUp: false);
                var flipped = e.TryFlipSummon(p, armor);
                Settle(e);
                var battle = e.TryEnterBattlePhase(p);
                Settle(e);
                Check("Armor Exe: cannot attack the turn it is Flip Summoned",
                    reached && flipped && battle && !e.CanAttack(p, armor),
                    $"reached={reached} flipped={flipped} battle={battle} {State(e)}");
            }

            {
                // The opponent's Standby Phase: the player removes the Library's counter to keep Armor Exe; at the
                // player's next Standby Phase nothing is left, so Armor Exe is destroyed.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var armor = Mon(e, p, ArmorExe, 0);
                var rml = Mon(e, p, Rml, 1, 1);
                e.TryEndTurnSafe(p);
                Settle(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation?.Card == armor && Legal(e).Contains(rml);
                var paid = asked && e.TrySelectEffectTarget(rml);
                Settle(e);
                var kept = OnField(e, armor) && rml.Counters == 0;
                var turnBefore = e.TurnNumber;
                for (var i = 0; i < 6 && e.TurnPlayer != p; i++)
                {
                    Settle(e);
                    e.RecoverStuckCombat();
                    if (e.TurnPlayer != p) e.TryEndTurnSafe(e.TurnPlayer);
                }

                Settle(e);
                Check("Armor Exe: each Standby Phase — a counter removed keeps it; with none left it is destroyed",
                    asked && paid && kept && e.TurnPlayer == p && e.TurnNumber > turnBefore && InGy(e, armor),
                    $"asked={asked} paid={paid} kept={kept} armorGy={InGy(e, armor)} {Ctr(rml)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var armor = Mon(e, p, ArmorExe, 0);
                var rml = Mon(e, p, Rml, 1, 1);
                e.TryEndTurnSafe(p);
                Settle(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation?.Card == armor;
                e.CancelEffectTargeting();
                Settle(e);
                Check("Armor Exe: declining the upkeep destroys it (the counter stays)",
                    asked && InGy(e, armor) && rml.Counters == 1,
                    $"asked={asked} armorGy={InGy(e, armor)} {Ctr(rml)} {State(e)}");
            }

            {
                // The AI's Armor Exe at the player's Standby Phase: it pays with no prompt.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var o = e.Opponent;
                var armor = Mon(e, o, ArmorExe, 0);
                var rml = Mon(e, o, Rml, 1, 1);
                e.TryEndTurnSafe(o);
                Settle(e);
                Check("Armor Exe (AI): its controller removes its counter at the opponent's Standby Phase",
                    reached && OnField(e, armor) && rml.Counters == 0 && e.TurnPlayer == e.Player,
                    $"reached={reached} armorOnField={OnField(e, armor)} {Ctr(rml)} {State(e)}");
            }
        }

        // ───────────────────────────── Legendary Flame Lord ─────────────────────────────

        static void FlameLordDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var lfl = Mon(e, p, FlameLord, 0, 4);
                var pot = PutInHand(e, p, PotOfGreed);
                e.TryActivateSpellTrap(p, pot, fromHand: true);
                Settle(e);
                Check("Legendary Flame Lord: a resolved Spell Card puts a counter on it (no max)",
                    lfl.Counters == 5, Ctr(lfl));
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var lfl = Mon(e, p, FlameLord, 0, 3);
                var mine = Mon(e, p, GeminiElf, 1);
                var setOpp = Mon(e, o, GeminiElf, 0, 0, faceUp: false);
                var theirs = Mon(e, o, Breaker, 1);
                var act = e.TryActivateSpellTrap(p, lfl, fromHand: false);
                Settle(e);
                Check("Legendary Flame Lord: remove 3 counters — every other monster on the field is destroyed, it stays",
                    act && OnField(e, lfl) && lfl.Counters == 0 && InGy(e, mine) && InGy(e, setOpp) && InGy(e, theirs),
                    $"act={act} {Ctr(lfl)} mine={InGy(e, mine)} setOpp={InGy(e, setOpp)} theirs={InGy(e, theirs)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var lone = Mon(e, e.Player, FlameLord, 0, 3);
                Check("Legendary Flame Lord: no other monster on the field — cannot be activated",
                    !e.CanActivateSpellTrap(e.Player, lone, fromHand: false), State(e));
                var e2 = Setup(db, pDeck, aDeck);
                var two = Mon(e2, e2.Player, FlameLord, 0, 2);
                Mon(e2, e2.Opponent, GeminiElf, 0);
                Check("Legendary Flame Lord: 2 counters cannot pay the cost",
                    !e2.CanActivateSpellTrap(e2.Player, two, fromHand: false), State(e2));
            }
        }

        // ───────────────────────────── Pitch-Black Power Stone ─────────────────────────────

        static void PitchBlackDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var breaker = Mon(e, p, Breaker, 0);
                var stone = PlaceSetTrap(e, p, PitchBlack, 0);
                var act = e.TryActivateSpellTrap(p, stone, fromHand: false);
                Settle(e);
                var placed = stone.FaceUp && stone.Counters == 3;
                var moved = e.TryActivateSpellTrap(p, stone, fromHand: false);
                var noPrompt = e.PendingActivation == null;
                Settle(e);
                var afterMove = stone.Counters == 2 && breaker.Counters == 1;
                var mar = Mon(e, p, Marionette, 1);
                var again = e.TryActivateSpellTrap(p, stone, fromHand: false);
                Settle(e);
                Check("Pitch-Black Power Stone: activated with 3 counters; moves 1 to the lone holder; once per turn",
                    act && placed && moved && noPrompt && afterMove && !again && stone.Counters == 2 && mar.Counters == 0,
                    $"act={act} placed={placed} moved={moved} noPrompt={noPrompt} afterMove={afterMove} again={again} " +
                    $"{Ctr(stone, breaker, mar)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var stone = FaceUpSt(e, p, PitchBlack, 0, 1);
                var breaker = Mon(e, p, Breaker, 0);
                var mar = Mon(e, o, Marionette, 0);
                FieldSpellEffects.RefreshBoard(e);
                var act = e.TryActivateSpellTrap(p, stone, fromHand: false);
                var legal = Legal(e);
                var asked = e.IsAwaitingEffectTarget && legal.Contains(breaker) && legal.Contains(mar) &&
                            !legal.Contains(stone);
                var picked = asked && e.TrySelectEffectTarget(mar);
                Settle(e);
                Check("Pitch-Black Power Stone: the player targets another holder; losing its last counter destroys it",
                    act && asked && picked && mar.Counters == 1 && breaker.Counters == 0 && InGy(e, stone),
                    $"act={act} asked={asked} picked={picked} {Ctr(stone, breaker, mar)} gy={InGy(e, stone)} {State(e)}");
            }

            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var stone = FaceUpSt(e, p, PitchBlack, 0, 3);
                Mon(e, p, Marionette, 0);
                Check("Pitch-Black Power Stone: not during the opponent's turn",
                    reached && !e.CanActivateSpellTrap(p, stone, fromHand: false), State(e));
            }

            // "Activate this card by placing 3 Spell Counters on it": placed as it is activated, not by its resolution.
            {
                var e = Setup(db, pDeck, aDeck);
                e.HumanVsHuman = true; // the opponent's chain window waits, so the open Chain Link can be inspected
                var p = e.Player;
                PlaceSetTrap(e, e.Opponent, Mst, 0); // something the opponent could chain
                var stone = PlaceSetTrap(e, p, PitchBlack, 0);
                var act = e.TryActivateSpellTrap(p, stone, fromHand: false);
                var atActivation = stone.Counters;
                var onChain = e.Chain.HasLinks && e.PendingResponse?.Responder == e.Opponent;
                Settle(e);
                Check("Pitch-Black Power Stone: the 3 counters are on it while its activation waits on the Chain, and its " +
                      "resolution adds none",
                    act && onChain && atActivation == 3 && stone.Counters == 3 && stone.FaceUp && OnField(e, stone) &&
                    !e.Chain.HasLinks,
                    $"act={act} atActivation={atActivation} onChain={onChain} {Ctr(stone)} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var decree = FaceUpSt(e, e.Opponent, RoyalDecree, 0, 0);
                FieldSpellEffects.RefreshBoard(e);
                var stone = PlaceSetTrap(e, p, PitchBlack, 0);
                var act = e.TryActivateSpellTrap(p, stone, fromHand: false);
                Settle(e);
                FieldSpellEffects.RefreshBoard(e);
                var under = stone.Counters == 3 && OnField(e, stone);
                e.Opponent.SpellTrapZones[0].Occupant = null;
                e.Opponent.Graveyard.Add(decree);
                FieldSpellEffects.RefreshBoard(e);
                Check("Pitch-Black Power Stone: under Royal Decree the activation still places 3 counters (not an effect)",
                    act && under && stone.Counters == 3 && OnField(e, stone),
                    $"act={act} under={under} {Ctr(stone)} {State(e)}");
            }
        }
    }
}
