using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TlmLeftoverSpellTraps batch (TLM / SOI): Grave Lure (a face-up Deck copy sent to the GY when drawn), Pikeru's Second
    /// Sight (the opponent's draws are shown), Level Conversion Lab (hand monster, die: GY or a new Level this turn),
    /// Shifting Shadows (pay 300 LP: face-down monsters rearranged) and Mesmeric Control (no manual position changes for
    /// the opponent's next turn). Compile shapes, mini-duels through TryActivateSpellTrap / Draw / TryNormalSummon /
    /// CanChangePosition / CanFlipSummon and turn ends, and fail-closed near misses.
    /// Runtime: <see cref="TlmLeftoverSpellTrapsRuntime"/>.
    /// </summary>
    public static class TlmLeftoverSpellTrapsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GraveLure = 57270476;
        const int LevelConversionLab = 84397023;
        const int PikerusSecondSight = 58015506;
        const int ShiftingShadows = 59237154;
        const int MesmericControl = 48642904;

        const int Celtic = 91152256;          // Warrior Lv4 1400/1200
        const int Gaia = 6368038;             // Warrior Lv7 2300/2100
        const int MysticalElf = 15025844;     // Spellcaster Lv4 800/2000
        const int GiantSoldier = 13039848;    // Rock Lv3 1300/2000
        const int SilverFang = 90357090;      // Beast Lv3 1200/800
        const int Koumori = 67724379;         // Dragon Lv4 1500/1200
        const int PotOfGreed = 55144522;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TlmLeftoverSpellTraps: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);

            GraveLureMarksTheTopCopy(db, pDeck, aDeck, Check);
            GraveLureDrawOutsideAChain(db, pDeck, aDeck, Check);
            GraveLureDrawDuringAChainLink(db, pDeck, aDeck, Check);
            GraveLureOtherCopiesAndLegality(db, pDeck, aDeck, Check);

            SecondSightShowsTwoOpponentTurns(db, pDeck, aDeck, Check);

            LevelLabPickAndSummon(db, pDeck, aDeck, Check);
            LevelLabOneSendsToGy(db, pDeck, aDeck, Check);
            LevelLabLegalityAndAi(db, pDeck, aDeck, Check);
            LevelLabAndCostDownTimestamps(db, pDeck, aDeck, Check);

            ShadowsRearrange(db, pDeck, aDeck, Check);
            ShadowsLimits(db, pDeck, aDeck, Check);

            MesmericLocksTheNextTurn(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) =>
            db.Get(id) is { } d ? CardTextEffectCompiler.Compile(d) : null;

        static bool Implemented(CardDatabase db, int id) =>
            db.Get(id) is { } d && CardEffectStatus.Classify(d) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join("; ", p.ClauseList.Where(c => c != null)
                    .Select(c => $"{c.Timing}/{c.Action}/{c.Side}/cond={c.ActivationCondition}/lp={c.PayLpAmount}")) +
                  $" unparsed={string.Join("|", p.UnparsedFragments ?? Array.Empty<string>())}";

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "TlmLeftoverSpellTraps near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                race = like.race,
                attribute = like.attribute
            };

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

        static void SeedDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            who.Deck.AddRange(ids);
        }

        static List<string> Capture(DuelEngine e)
        {
            var lines = new List<string>();
            e.OnLog += s => lines.Add(s);
            return lines;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Every prompt picks the first legal card matching <paramref name="want"/>; every window passes.</summary>
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

        static void PassWindows(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}" +
            $"[{string.Join(",", Legal(e).Select(c => c?.Name))}] response={e.IsAwaitingResponse} " +
            $"links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnNumber}/{e.TurnPlayer?.Name}";

        /// <summary>The Deck index of the copy Grave Lure marked, or -1.</summary>
        static int MarkedIndex(DuelistState who)
        {
            for (var i = 0; i < who.Deck.Count; i++)
                if (who.Deck.InfoAt(i)?.SentToGyWhenDrawnBy != null)
                    return i;
            return -1;
        }

        static void MoveToTop(DuelistState who, int index)
        {
            if (index <= 0) return;
            var id = who.Deck[index];
            var info = who.Deck.TakeAt(index);
            who.Deck.Insert(0, id, info);
        }

        /// <summary>End turns until <paramref name="turn"/> is reached and its turn player is in a Main Phase.</summary>
        static bool ReachTurnMain(DuelEngine e, int turn)
        {
            for (var i = 0; i < 40 && !e.GameOver; i++)
            {
                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                e.RecoverStuckCombat();
                if (e.TurnNumber == turn && e.InMainPhase) break;
                if (e.TurnNumber > turn) break;
                if (e.TurnNumber < turn)
                {
                    e.TryEndTurnSafe(e.TurnPlayer);
                    continue;
                }

                // The right turn, not yet in a Main Phase (Draw / Standby windows).
                if (!e.TryEndTurnSafe(e.TurnPlayer)) break;
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnNumber == turn && e.InMainPhase;
        }

        // ───────────────────────────── Compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var p = Prog(db, GraveLure);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Grave Lure: one free-chain Activate clause, the opponent's Deck must not be empty",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && Implemented(db, GraveLure) &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.TurnOpponentDeckTopFaceUpSendToGyWhenDrawn &&
                    c.ActivationCondition == DcrSpellTrapUtilityRuntime.OpponentDeckNotEmpty &&
                    !TargetingTruthRuntime.ClauseTargets(c), Describe(p));
            }
            {
                var p = Prog(db, PikerusSecondSight);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Pikeru's Second Sight: one Activate clause registering the draw reveal",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && Implemented(db, PikerusSecondSight) &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.OpponentShowsDrawnCardsUntilTheirSecondEndPhase &&
                    string.IsNullOrEmpty(c.ActivationCondition) && !TargetingTruthRuntime.ClauseTargets(c), Describe(p));
            }
            {
                var p = Prog(db, LevelConversionLab);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Level Conversion Lab: one Activate clause, needs a hand monster, picks at resolution (no target)",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && Implemented(db, LevelConversionLab) &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.ShowHandMonsterRollDieLevelBecomesResultOrSendToGy &&
                    c.ActivationCondition == RecoveryAndPhaseCostsRuntime.HandHasLevelOrHigherMonster &&
                    c.MinLevelOrHigher == 1 && !c.RequiresTargetChoice && !TargetingTruthRuntime.ClauseTargets(c),
                    Describe(p));
            }
            {
                var p = Prog(db, ShiftingShadows);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Shifting Shadows: one face-up field ignition, 300 LP, once per turn",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && Implemented(db, ShiftingShadows) &&
                    c.Timing == EffectTiming.FaceUpFieldIgnition &&
                    c.Action == EffectActionKind.ShuffleYourFaceDownMainZoneMonsters && c.PayLpAmount == 300 &&
                    c.OncePerTurn && TlmLeftoverSpellTrapsRuntime.IsShadowsIgnition(p), Describe(p));
            }
            {
                var p = Prog(db, MesmericControl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Mesmeric Control: one Activate clause registering the opponent's position lock",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && Implemented(db, MesmericControl) &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.OpponentCannotChangeBattlePositionsDuringTheirNextTurn, Describe(p));
            }
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var batch = new[]
            {
                EffectActionKind.TurnOpponentDeckTopFaceUpSendToGyWhenDrawn,
                EffectActionKind.OpponentShowsDrawnCardsUntilTheirSecondEndPhase,
                EffectActionKind.ShowHandMonsterRollDieLevelBecomesResultOrSendToGy,
                EffectActionKind.ShuffleYourFaceDownMainZoneMonsters,
                EffectActionKind.OpponentCannotChangeBattlePositionsDuringTheirNextTurn
            };
            var cases = new (int like, string text)[]
            {
                // The draw rider is missing: the turned card would never go to the GY.
                (GraveLure, "Turn the top card of your opponent's Deck face-up, then your opponent shuffles their Deck."),
                // Your own Deck.
                (GraveLure, "Turn the top card of your Deck face-up, then shuffle your Deck. When you draw the face-up " +
                            "card, immediately send it to the Graveyard."),
                // A different duration.
                (PikerusSecondSight, "Until your opponent's next End Phase, your opponent shows you all cards that they " +
                                     "draw before adding them to their hand."),
                // Another stat changes on 2-6.
                (LevelConversionLab, "Select 1 monster in your hand and show it to your opponent, then roll a six-sided " +
                                     "die. If the result is 1, send the selected monster to the Graveyard. If the result " +
                                     "is 2-6, the ATK of the selected monster becomes equal to the result until the end " +
                                     "of this turn."),
                // Face-up monsters, or no LP cost.
                (ShiftingShadows, "Once per turn, by paying 300 Life Points, rearrange the face-up Attack Position " +
                                  "monsters in your Main Monster Zones."),
                (ShiftingShadows, "Once per turn, rearrange the face-down Defense Position monsters in your Main Monster " +
                                  "Zones, then return them in face-down Defense Position."),
                // Your own monsters, or without the card-effect exception.
                (MesmericControl, "During your opponent's next turn after you activate this card, you cannot change the " +
                                  "battle positions of monsters, except with a card effect."),
            };
            foreach (var (like, text) in cases)
            {
                var prog = CardTextEffectCompiler.Compile(Synthetic(db.Get(like), text));
                Check($"near miss gets none of this batch's clauses: {text}",
                    prog == null || !prog.ClauseList.Exists(c => c != null && batch.Contains(c.Action)), Describe(prog));
            }
        }

        // ───────────────────────────── Grave Lure ─────────────────────────────

        static void GraveLureMarksTheTopCopy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            SeedDeck(o, Gaia, Celtic, MysticalElf, GiantSoldier, SilverFang, Koumori, PotOfGreed, Celtic);
            var before = o.Deck.OrderBy(x => x).ToList();
            var log = Capture(e);
            var lure = PlaceSetTrap(e, p, GraveLure, 0);
            var act = e.TryActivateSpellTrap(p, lure, fromHand: false);
            Drive(e, _ => false);
            var marked = MarkedIndex(o);
            var info = marked >= 0 ? o.Deck.InfoAt(marked) : null;
            var faceUps = Enumerable.Range(0, o.Deck.Count).Count(i => o.Deck.InfoAt(i)?.FaceUp == true);
            Check("Grave Lure: the opponent's top card (shown) is face-up in their shuffled Deck, marked by its activator",
                act && marked >= 0 && o.Deck[marked] == Gaia && info.FaceUp && ReferenceEquals(info.SentToGyWhenDrawnBy, p) &&
                ReferenceEquals(info.SentToGyWhenDrawnSource, lure) && faceUps == 1 &&
                o.Deck.OrderBy(x => x).SequenceEqual(before) && p.Graveyard.Contains(lure) &&
                log.Exists(l => l.Contains("Gaia") && l.Contains("face-up")) && !e.Chain.HasLinks,
                $"act={act} marked={marked} faceUps={faceUps} {State(e)}");
        }

        static void GraveLureDrawOutsideAChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            SeedDeck(o, Gaia, Celtic, MysticalElf, GiantSoldier, SilverFang, Koumori, PotOfGreed, Celtic);
            var lure = PlaceSetTrap(e, p, GraveLure, 0);
            var act = e.TryActivateSpellTrap(p, lure, fromHand: false);
            Drive(e, _ => false);
            MoveToTop(o, MarkedIndex(o));
            var hand = o.Hand.Count;
            e.Draw(o, 1);
            var gaia = o.Graveyard.FirstOrDefault(c => c.CardId == Gaia);
            Check("Grave Lure: the opponent draws the face-up copy outside a Chain — it is sent to their GY at once",
                act && gaia != null && o.Hand.Count == hand && !o.Hand.Exists(c => c.CardId == Gaia) &&
                gaia.GyEntryReason == GyEntryReason.Effect && MarkedIndex(o) < 0,
                $"act={act} hand {hand}->{o.Hand.Count} gy={string.Join(",", o.Graveyard.Select(c => c.Name))}");

            // The next draw is an ordinary card: it stays in the hand.
            e.Draw(o, 1);
            Check("Grave Lure: the following ordinary draw stays in the hand",
                o.Hand.Count == hand + 1 && o.Graveyard.Count == 1, $"hand={o.Hand.Count} gy={o.Graveyard.Count}");
        }

        static void GraveLureDrawDuringAChainLink(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI's Grave Lure marks the player's Deck; the player's Pot of Greed draws the copy while its Chain Link
            // resolves: the copy goes to the GY once that link is over (Lua EVENT_CHAIN_SOLVED).
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            SeedDeck(p, Gaia, Celtic, MysticalElf, GiantSoldier, SilverFang, Koumori, Celtic, Celtic);
            var lure = PlaceSetTrap(e, o, GraveLure, 0);
            var act = e.TryActivateSpellTrap(o, lure, fromHand: false);
            Drive(e, _ => false);
            MoveToTop(p, MarkedIndex(p));
            var log = Capture(e);
            var pot = PutInHand(e, p, PotOfGreed);
            var cast = e.TryActivateSpellTrap(p, pot, fromHand: true);
            Drive(e, _ => false);
            var gaiaInGy = p.Graveyard.Exists(c => c.CardId == Gaia);
            var deferred = log.FindIndex(l => l.Contains("after this Chain Link"));
            var sent = log.FindIndex(l => l.Contains("drew the face-up") && l.Contains("Gaia"));
            Check("Grave Lure: drawn while a Chain Link resolves — sent to the GY after that link",
                act && cast && gaiaInGy && !p.Hand.Exists(c => c.CardId == Gaia) && p.Hand.Count == 1 &&
                deferred >= 0 && sent > deferred && !e.Chain.HasLinks,
                $"act={act} cast={cast} hand={string.Join(",", p.Hand.Select(c => c.Name))} " +
                $"gy={string.Join(",", p.Graveyard.Select(c => c.Name))} deferred={deferred} sent={sent} {State(e)}");
        }

        static void GraveLureOtherCopiesAndLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Two copies of the same card: only the marked copy is sent; the other one drawn first stays in the hand.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SeedDeck(o, Celtic, Celtic, Gaia, MysticalElf);
                var lure = PlaceSetTrap(e, p, GraveLure, 0);
                e.TryActivateSpellTrap(p, lure, fromHand: false);
                Drive(e, _ => false);
                var marked = MarkedIndex(o);
                // Put the ordinary Celtic on top and the marked one second.
                var plain = Enumerable.Range(0, o.Deck.Count).First(i => i != marked && o.Deck[i] == Celtic);
                var markedInfo = o.Deck.InfoAt(marked);
                var ids = new List<(int id, DeckCopyInfo info)>();
                while (o.Deck.Count > 0)
                {
                    var id = o.Deck[0];
                    ids.Add((id, o.Deck.TakeAt(0)));
                }

                var plainEntry = ids[plain];
                var markedEntry = ids[marked];
                o.Deck.Add(plainEntry.id, plainEntry.info);
                o.Deck.Add(markedEntry.id, markedEntry.info);
                for (var i = 0; i < ids.Count; i++)
                    if (i != plain && i != marked)
                        o.Deck.Add(ids[i].id, ids[i].info);
                e.Draw(o, 1);
                var keptFirst = o.Hand.Count(c => c.CardId == Celtic) == 1 && o.Graveyard.Count == 0;
                e.Draw(o, 1);
                Check("Grave Lure: per copy — an unmarked copy of the same card stays, the marked one goes to the GY",
                    marked >= 0 && ReferenceEquals(markedInfo, markedEntry.info) && keptFirst &&
                    o.Hand.Count(c => c.CardId == Celtic) == 1 && o.Graveyard.Count(c => c.CardId == Celtic) == 1,
                    $"marked={marked} keptFirst={keptFirst} hand={string.Join(",", o.Hand.Select(c => c.Name))} " +
                    $"gy={string.Join(",", o.Graveyard.Select(c => c.Name))}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                e.Opponent.Deck.Clear();
                var lure = PlaceSetTrap(e, p, GraveLure, 0);
                Check("Grave Lure: cannot be activated while the opponent's Deck is empty",
                    !e.CanActivateSpellTrap(p, lure, fromHand: false), State(e));
            }
        }

        // ───────────────────────────── Pikeru's Second Sight ─────────────────────────────

        static void SecondSightShowsTwoOpponentTurns(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var startTurn = e.TurnNumber;
            var mine = e.TurnPlayer == p;
            var sight = PlaceSetTrap(e, p, PikerusSecondSight, 0);
            var act = e.TryActivateSpellTrap(p, sight, fromHand: false);
            Drive(e, _ => false);
            var log = Capture(e);
            bool Shown() => log.Exists(l => l.Contains("looks at") && l.Contains(o.Name));

            e.Draw(o, 1);
            var shownNow = Shown();
            log.Clear();
            e.Draw(p, 1);
            var ownNotShown = !Shown();

            // The opponent's turns after this one: T+1 and T+3 are covered (their 1st and 2nd End Phases), T+5 is not.
            var seen = new Dictionary<int, bool>();
            foreach (var turn in new[] { startTurn + 1, startTurn + 3, startTurn + 5 })
            {
                log.Clear();
                var reached = ReachTurnMain(e, turn);
                seen[turn] = reached && e.TurnPlayer == o && Shown();
            }

            Check("Pikeru's Second Sight: the opponent's draws are shown to the activator through their 2nd End Phase",
                act && mine && shownNow && ownNotShown && seen[startTurn + 1] && seen[startTurn + 3] &&
                !seen[startTurn + 5] && p.Graveyard.Contains(sight),
                $"act={act} mine={mine} now={shownNow} own={ownNotShown} " +
                string.Join(" ", seen.Select(kv => $"t{kv.Key}={kv.Value}")) + $" {State(e)}");
        }

        // ───────────────────────────── Level Conversion Lab ─────────────────────────────

        static void LevelLabPickAndSummon(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var gaia = PutInHand(e, p, Gaia);
            var elf = PutInHand(e, p, MysticalElf);
            var tribute = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, faceUp: true);
            var lab = PlaceSetTrap(e, p, LevelConversionLab, 0);
            e.Rng.QueueDie(5);
            var act = e.TryActivateSpellTrap(p, lab, fromHand: false);
            PassWindows(e);
            var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2 && Legal(e).Contains(gaia) &&
                        Legal(e).Contains(elf);
            var picked = Drive(e, c => c == gaia);
            var levelInHand = gaia.Level;
            FieldSpellEffects.RefreshBoard(e);
            var afterRefresh = gaia.Level;
            Check("Level Conversion Lab: the player picks among hand monsters; a 5 makes Gaia Level 5",
                act && asked && picked && levelInHand == 5 && afterRefresh == 5 && elf.Level == 4 &&
                p.Hand.Contains(gaia) && p.Graveyard.Contains(lab) && !e.Chain.HasLinks,
                $"act={act} asked={asked} picked={picked} lv={levelInHand}/{afterRefresh} {State(e)}");

            // Level 5: one Tribute is enough, and the new Level stays on the field this turn.
            e.PendingTributes.Add(tribute);
            var summoned = e.TryNormalSummon(p, gaia, asSet: false);
            Drive(e, _ => false);
            FieldSpellEffects.RefreshBoard(e);
            var onField = p.TryFindMonster(gaia, out _);
            var levelOnField = gaia.Level;
            Check("Level Conversion Lab: the Level-5 Gaia is Tribute Summoned with 1 Tribute and stays Level 5",
                summoned && onField && levelOnField == 5 && p.Graveyard.Exists(c => c.CardId == Celtic),
                $"summoned={summoned} onField={onField} lv={levelOnField} {State(e)}");

            var turn = e.TurnNumber;
            var ended = ReachTurnMain(e, turn + 1);
            FieldSpellEffects.RefreshBoard(e);
            Check("Level Conversion Lab: the Level change ends with the turn",
                ended && gaia.Level == 7, $"ended={ended} lv={gaia.Level} {State(e)}");
        }

        static void LevelLabOneSendsToGy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var gaia = PutInHand(e, p, Gaia);
                var lab = PlaceSetTrap(e, p, LevelConversionLab, 0);
                e.Rng.QueueDie(1);
                var act = e.TryActivateSpellTrap(p, lab, fromHand: false);
                Drive(e, _ => false);
                Check("Level Conversion Lab: a lone monster is selected without a prompt; a 1 sends it to the GY",
                    act && !p.Hand.Contains(gaia) && p.Graveyard.Contains(gaia) &&
                    gaia.GyEntryReason == GyEntryReason.Effect && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"act={act} gy={string.Join(",", p.Graveyard.Select(c => c.Name))} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var silver = PutInHand(e, p, SilverFang);
                var lab = PlaceSetTrap(e, p, LevelConversionLab, 0);
                e.Rng.QueueDie(6);
                var act = e.TryActivateSpellTrap(p, lab, fromHand: false);
                Drive(e, _ => false);
                Check("Level Conversion Lab: the Level can go up (Level 3 → 6)",
                    act && p.Hand.Contains(silver) && silver.Level == 6, $"act={act} lv={silver.Level} {State(e)}");
            }
        }

        static void LevelLabLegalityAndAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PutInHand(e, p, PotOfGreed);
                var lab = PlaceSetTrap(e, p, LevelConversionLab, 0);
                Check("Level Conversion Lab: cannot be activated without a monster in your hand",
                    !e.CanActivateSpellTrap(p, lab, fromHand: false), State(e));
            }
            {
                // The AI's copy resolves without a prompt for the human: it takes its highest Level.
                var e = FreshCleared(db, pDeck, aDeck);
                var o = e.Opponent;
                var gaia = PutInHand(e, o, Gaia);
                var celtic = PutInHand(e, o, Celtic);
                var lab = PlaceSetTrap(e, o, LevelConversionLab, 0);
                e.Rng.QueueDie(4);
                var act = e.TryActivateSpellTrap(o, lab, fromHand: false);
                Drive(e, _ => false);
                Check("Level Conversion Lab (AI): no prompt for the human; its highest-Level monster becomes Level 4",
                    act && gaia.Level == 4 && celtic.Level == 4 && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"act={act} gaia={gaia.Level} {State(e)}");
            }
        }

        // ygopro card::get_level: CHANGE_LEVEL (the Lab) resets only the single UPDATE_LEVEL effects older than it, so a
        // Cost Down that resolves later still applies on top of the set Level; one that resolved earlier is overridden.
        static void LevelLabAndCostDownTimestamps(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var gaia = PutInHand(e, p, Gaia);
                var lab = PlaceSetTrap(e, p, LevelConversionLab, 0);
                e.Rng.QueueDie(5);
                var act = e.TryActivateSpellTrap(p, lab, fromHand: false);
                PassWindows(e);
                Drive(e, c => c == gaia);
                FieldSpellEffects.RefreshBoard(e);
                var afterLab = gaia.Level;
                SimpleTargetUtilityRuntime.ApplyHandLevelStamp(e, p, new EffectClause { Amount = 2 });
                FieldSpellEffects.RefreshBoard(e);
                var afterCostDown = gaia.Level;
                var summoned = e.TryNormalSummon(p, gaia, asSet: false);
                Drive(e, _ => false);
                FieldSpellEffects.RefreshBoard(e);
                Check("Level Conversion Lab (5) then Cost Down (-2): Gaia is Level 3, Normal Summoned without a Tribute",
                    act && afterLab == 5 && afterCostDown == 3 && summoned && p.TryFindMonster(gaia, out _) &&
                    gaia.Level == 3,
                    $"act={act} afterLab={afterLab} afterCostDown={afterCostDown} summoned={summoned} lv={gaia.Level} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var gaia = PutInHand(e, p, Gaia);
                SimpleTargetUtilityRuntime.ApplyHandLevelStamp(e, p, new EffectClause { Amount = 2 });
                FieldSpellEffects.RefreshBoard(e);
                var afterCostDown = gaia.Level;
                var lab = PlaceSetTrap(e, p, LevelConversionLab, 0);
                e.Rng.QueueDie(6);
                var act = e.TryActivateSpellTrap(p, lab, fromHand: false);
                PassWindows(e);
                Drive(e, c => c == gaia);
                FieldSpellEffects.RefreshBoard(e);
                var afterLab = gaia.Level;
                SimpleTargetUtilityRuntime.ApplyHandLevelStamp(e, p, new EffectClause { Amount = 2 });
                FieldSpellEffects.RefreshBoard(e);
                Check("Cost Down then Level Conversion Lab (6): the Lab overrides it (6); a 2nd Cost Down after it gives 4",
                    afterCostDown == 5 && act && afterLab == 6 && gaia.Level == 4,
                    $"afterCostDown={afterCostDown} act={act} afterLab={afterLab} lv={gaia.Level} {State(e)}");
            }
        }

        // ───────────────────────────── Shifting Shadows ─────────────────────────────

        static void ShadowsRearrange(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, faceUp: false);
            var b = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, faceUp: false);
            var c = PlaceMonster(e, p, GiantSoldier, 2, BattlePosition.Defense, faceUp: false);
            var up = PlaceMonster(e, p, Koumori, 3, BattlePosition.Attack, faceUp: true);
            var ss = PutInHand(e, p, ShiftingShadows);
            var lp = p.LifePoints;
            var placed = e.TryActivateSpellTrap(p, ss, fromHand: true);
            Drive(e, _ => false);
            var onField = ss.FaceUp && p.TryFindSpellTrap(ss, out _);
            var activationFree = p.LifePoints == lp;
            var fired = e.TryActivateSpellTrap(p, ss, fromHand: false);
            Drive(e, _ => false);
            var firstThree = Enumerable.Range(0, 3).Select(i => p.MonsterZones[i].Occupant).ToList();
            var sameSet = firstThree.Contains(a) && firstThree.Contains(b) && firstThree.Contains(c) &&
                          firstThree.All(m => !m.FaceUp && m.Position == BattlePosition.Defense);
            Check("Shifting Shadows: activation does nothing; its effect pays 300 LP and rearranges the face-down monsters",
                placed && onField && activationFree && fired && p.LifePoints == lp - 300 && sameSet &&
                p.MonsterZones[3].Occupant == up && up.FaceUp && p.TryFindSpellTrap(ss, out _) && !e.Chain.HasLinks &&
                !e.IsAwaitingEffectTarget,
                $"placed={placed} onField={onField} free={activationFree} fired={fired} lp {lp}->{p.LifePoints} " +
                $"zones={string.Join(",", p.MonsterZones.Select(z => z.Occupant?.Name ?? "-"))} {State(e)}");

            Check("Shifting Shadows: once per turn",
                !e.CanActivateSpellTrap(p, ss, fromHand: false), State(e));
        }

        static void ShadowsLimits(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, faceUp: false);
                PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, faceUp: true);
                var ss = PlaceSetTrap(e, p, ShiftingShadows, 0);
                ss.FaceUp = true;
                Check("Shifting Shadows: needs 2 or more face-down monsters", !e.CanActivateSpellTrap(p, ss, fromHand: false),
                    State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, faceUp: false);
                PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, faceUp: false);
                var ss = PlaceSetTrap(e, p, ShiftingShadows, 0);
                ss.FaceUp = true;
                p.LifePoints = 200;
                Check("Shifting Shadows: fewer than 300 LP — cannot use", !e.CanActivateSpellTrap(p, ss, fromHand: false),
                    State(e));
            }
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, faceUp: false);
                PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, faceUp: false);
                var ss = PlaceSetTrap(e, p, ShiftingShadows, 0);
                ss.FaceUp = true;
                Check("Shifting Shadows: not during the opponent's turn (an ignition)",
                    reached && !e.CanActivateSpellTrap(p, ss, fromHand: false), State(e));
            }
        }

        // ───────────────────────────── Mesmeric Control ─────────────────────────────

        static void MesmericLocksTheNextTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var start = e.TurnNumber;
            var mine = e.TurnPlayer == p;
            var mc = PutInHand(e, p, MesmericControl);
            var act = e.TryActivateSpellTrap(p, mc, fromHand: true);
            Drive(e, _ => false);

            var reached = ReachTurnMain(e, start + 1) && e.TurnPlayer == o;
            ClearBoard(e);
            var atk = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, faceUp: true);
            var set = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, faceUp: false);
            var lockedChange = !e.CanChangePosition(o, atk);
            var lockedFlip = !e.CanFlipSummon(o, set);
            var tried = e.TryChangePosition(o, atk);
            Check("Mesmeric Control: during the opponent's next turn they cannot change positions or Flip Summon",
                act && mine && p.Graveyard.Contains(mc) && reached && lockedChange && lockedFlip && !tried &&
                atk.Position == BattlePosition.Attack,
                $"act={act} mine={mine} reached={reached} change={lockedChange} flip={lockedFlip} tried={tried} {State(e)}");

            // The player's own monsters are not covered during their next turn.
            var mineReached = ReachTurnMain(e, start + 2) && e.TurnPlayer == p;
            ClearBoard(e);
            var own = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, faceUp: true);
            Check("Mesmeric Control: the activator's own monsters can change positions",
                mineReached && e.CanChangePosition(p, own), State(e));

            // The opponent's turn after that is free again.
            var later = ReachTurnMain(e, start + 3) && e.TurnPlayer == o;
            ClearBoard(e);
            var atk2 = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, faceUp: true);
            var set2 = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, faceUp: false);
            Check("Mesmeric Control: the lock ends after the opponent's next turn",
                later && e.CanChangePosition(o, atk2) && e.CanFlipSummon(o, set2), State(e));
        }
    }
}
