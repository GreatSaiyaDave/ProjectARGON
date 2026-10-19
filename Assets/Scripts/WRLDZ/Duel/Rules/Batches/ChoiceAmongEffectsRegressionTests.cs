using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ChoiceAmongEffects batch: "When &lt;condition&gt;: Apply / Activate 1 of these effects. ● … ● …".
    /// Fuh-Rin-Ka-Zan (c1781310): the bullet is chosen as the card resolves, among the bullets that can apply then.
    /// Penalty Game! (c967928): the bullet is chosen as the card is activated; its two locks (no draw in the opponent's
    /// next Draw Phase; no Spell/Trap Card activations by the opponent this turn). Compile shapes through the real
    /// compiler, near-miss texts, and mini-duels through TryActivateSpellTrap / CanActivateSpellTrap, the target picker,
    /// turn ends and TryAttack's response window. Runtime: <see cref="ChoiceAmongEffectsRuntime"/>.
    /// </summary>
    public static class ChoiceAmongEffectsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int FuhRinKaZan = 1781310;
        const int PenaltyGame = 967928;

        const int HarpieLady = 76812113;       // WIND Normal 1300/1400
        const int SevenColoredFish = 23771716; // WATER Normal 1800/800
        const int HinotamaSoul = 96851799;     // FIRE Normal 600/500
        const int Celtic = 91152256;           // EARTH Normal 1400/1200
        const int Bewd = 89631139;             // LIGHT Normal 3000/2500
        const int PotOfGreed = 55144522;       // Normal Spell (cannot be chained)
        const int Mst = 5318639;               // Quick-Play Spell
        const int JarOfGreed = 83968380;       // Normal Trap
        const int MirrorForce = 44095762;      // Normal Trap: when an opponent's monster declares an attack

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (n, ok, d) => check(n, ok, d ?? "");
            Guard(Check, "shapes", () => CompileShapes(db, Check));
            Guard(Check, "near misses", () => NearMisses(Check));
            Guard(Check, "frkz condition", () => FuhRinKaZanCondition(db, pDeck, aDeck, Check));
            Guard(Check, "frkz picks", () => FuhRinKaZanEachBullet(db, pDeck, aDeck, Check));
            Guard(Check, "frkz availability", () => FuhRinKaZanAvailability(db, pDeck, aDeck, Check));
            Guard(Check, "frkz ai", () => FuhRinKaZanAi(db, pDeck, aDeck, Check));
            Guard(Check, "penalty condition", () => PenaltyGameCondition(db, pDeck, aDeck, Check));
            Guard(Check, "penalty draw lock", () => PenaltyGameDrawLock(db, pDeck, aDeck, Check));
            Guard(Check, "penalty ai", () => PenaltyGameAi(db, pDeck, aDeck, Check));
            Guard(Check, "penalty s/t lock", () => PenaltyGameSpellTrapLock(db, pDeck, aDeck, Check));
            Guard(Check, "penalty chained", () => PenaltyGameChained(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk check, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { check($"ChoiceAmongEffects {name}: no exception", false, ex.ToString()); }
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var frkzDef = db.Get(FuhRinKaZan);
            var frkz = frkzDef != null ? CardTextEffectCompiler.Compile(frkzDef) : null;
            var a = frkz?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>();
            var shape = frkz != null && frkz.FullyCompiled && a.Count == 5 &&
                        a[0].Action == EffectActionKind.ApplyOneOfTheseEffectsAtResolution && a[0].EffectChoiceIndex == 0 &&
                        a[0].ActivationCondition == ChoiceAmongEffectsRuntime.FaceUpAttributesOnField &&
                        a[0].ConditionName == "WIND,WATER,FIRE,EARTH" &&
                        a.Skip(1).Select((c, i) => c.EffectChoiceIndex == i + 1 && c.EffectChoiceAppliedAtResolution &&
                                                   !c.RequiresTargetChoice &&
                                                   c.ActivationCondition == ChoiceAmongEffectsRuntime.AnyBulletApplicable)
                            .All(x => x) &&
                        a[1].Action == EffectActionKind.Destroy && a[1].Zone == EffectZoneFilter.FieldMonsters &&
                        a[1].Side == EffectSide.Opponent &&
                        a[2].Action == EffectActionKind.Destroy && a[2].Zone == EffectZoneFilter.FieldSpellTraps &&
                        a[2].Side == EffectSide.Opponent &&
                        a[3].Action == EffectActionKind.DiscardRandomFromOpponentHand && a[3].Amount == 2 &&
                        a[4].Action == EffectActionKind.Draw && a[4].Amount == 2 && a[4].Side == EffectSide.Controller &&
                        // Not an activation-time choice: the Poison of the Old Man flow does not offer these bullets.
                        !PoisonMeteorCestusRuntime.HasBulletChoice(a);
            Check("ChoiceAmongEffects compile: Fuh-Rin-Ka-Zan = chooser (4 Attributes face-up) + 4 bullets applied at resolution",
                shape && CardEffectStatus.Classify(frkzDef) == CardEffectStatusKind.Implemented, Describe(frkz));

            var pgDef = db.Get(PenaltyGame);
            var pg = pgDef != null ? CardTextEffectCompiler.Compile(pgDef) : null;
            var b = pg?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>();
            var pgShape = pg != null && pg.FullyCompiled && b.Count == 2 &&
                          b[0].Action == EffectActionKind.OpponentCannotDrawDuringNextDrawPhase && b[0].EffectChoiceIndex == 1 &&
                          b[1].Action == EffectActionKind.OpponentCannotActivateSpellTrapCardsThisTurn &&
                          b[1].EffectChoiceIndex == 2 &&
                          b.All(c => !c.EffectChoiceAppliedAtResolution && !c.RequiresTargetChoice &&
                                     c.ActivationCondition == ChoiceAmongEffectsRuntime.OpponentHandExactly &&
                                     c.ConditionAmount == 4) &&
                          PoisonMeteorCestusRuntime.HasBulletChoice(b);
            Check("ChoiceAmongEffects compile: Penalty Game! = 2 bullets chosen on activation (opponent's hand exactly 4)",
                pgShape && CardEffectStatus.Classify(pgDef) == CardEffectStatusKind.Implemented, Describe(pg));
        }

        static void NearMisses(Chk Check)
        {
            const string cond = "When there are face-up WIND, WATER, FIRE and EARTH monster(s) on the field: ";
            var cases = new[]
            {
                // A targeted bullet: the "Apply" choice has no target step.
                cond + "Apply 1 of these effects.\n● Destroy all monsters your opponent controls.\n" +
                "● Target 1 card on the field; destroy that target.",
                // A condition this batch does not know.
                "When your opponent controls a monster: Apply 1 of these effects.\n● Destroy all monsters your opponent " +
                "controls.\n● Draw 2 cards.",
                // An "Apply" bullet whose availability the runtime cannot judge.
                "When your opponent has 4 cards in their hand: Apply 1 of these effects.\n● Your opponent cannot draw " +
                "during their next Draw Phase.\n● Draw 2 cards.",
                // One bullet is not a choice.
                "When your opponent has 4 cards in their hand: Activate 1 of these effects.\n● Your opponent cannot draw " +
                "during their next Draw Phase.",
                // An unknown bullet sentence.
                "When your opponent has 4 cards in their hand: Activate 1 of these effects.\n● Your opponent cannot draw " +
                "during their next Draw Phase.\n● Your opponent cannot Normal Summon this turn.",
            };
            foreach (var text in cases)
            {
                var prog = CardTextEffectCompiler.Compile(Def(text));
                // Fail closed: not FullyCompiled, no bullet clause, and "Destroy all monsters your opponent controls" after a
                // "●" did not become an always-applied Raigeki.
                var ok = prog != null && !prog.FullyCompiled &&
                         !prog.ClauseList.Exists(c => c != null && c.EffectChoiceIndex > 0) &&
                         !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Destroy &&
                                                      c.Zone == EffectZoneFilter.FieldMonsters) &&
                         !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ApplyOneOfTheseEffectsAtResolution);
                Check($"ChoiceAmongEffects near miss stays uncompiled: {text.Substring(0, Math.Min(70, text.Length))}…", ok,
                    Describe(prog));
            }

            // The standalone sentences keep their own cards (Raigeki, Harpie's Feather Duster).
            var raigeki = CardTextEffectCompiler.Compile(Def("Destroy all monsters your opponent controls.", "Spell Card"));
            var hfd = CardTextEffectCompiler.Compile(Def("Destroy all Spell and Trap Cards your opponent controls.", "Spell Card"));
            Check("ChoiceAmongEffects near miss: standalone Raigeki / Harpie's Feather Duster sentences still compile",
                raigeki != null && raigeki.FullyCompiled && raigeki.ClauseList.Count == 1 &&
                raigeki.ClauseList[0].Zone == EffectZoneFilter.FieldMonsters &&
                hfd != null && hfd.FullyCompiled && hfd.ClauseList.Count == 1 &&
                hfd.ClauseList[0].Zone == EffectZoneFilter.FieldSpellTraps,
                Describe(raigeki) + " || " + Describe(hfd));
        }

        // ───────────────────────────── Fuh-Rin-Ka-Zan ─────────────────────────────

        static void FuhRinKaZanCondition(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var frkz = PlaceSetTrap(e, p, FuhRinKaZan, 0);
            PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Attack, true);
            PlaceMonster(e, p, SevenColoredFish, 1, BattlePosition.Attack, true);
            PlaceMonster(e, o, HinotamaSoul, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Defense, false);
            PutInHand(e, o, PotOfGreed);
            e.NotifyPublic();
            var earthDown = e.CanActivateSpellTrap(p, frkz, fromHand: false);
            celtic.FaceUp = true;
            celtic.Position = BattlePosition.Attack;
            e.NotifyPublic();
            var all = e.CanActivateSpellTrap(p, frkz, fromHand: false);
            p.MonsterZones[1].Occupant = null;
            e.NotifyPublic();
            var noWater = e.CanActivateSpellTrap(p, frkz, fromHand: false);
            Check("Fuh-Rin-Ka-Zan: needs face-up WIND, WATER, FIRE and EARTH monsters on the field (either side)",
                !earthDown && all && !noWater, $"earthFaceDown={earthDown} all={all} noWater={noWater}");
        }

        static void FuhRinKaZanEachBullet(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            for (var idx = 1; idx <= 4; idx++)
            {
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var frkz = PlaceSetTrap(e, p, FuhRinKaZan, 0);
                PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, SevenColoredFish, 1, BattlePosition.Attack, true);
                PlaceMonster(e, o, HinotamaSoul, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var oSet = PlaceSetTrap(e, o, PotOfGreed, 0); // a Set Normal Spell: the AI cannot chain it
                for (var i = 0; i < 3; i++) PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var pDeckBefore = p.Deck.Count;

                var act = e.TryActivateSpellTrap(p, frkz, fromHand: false);
                var offered = new List<CardInstance>();
                string prompt = null;
                var want = idx;
                var picked = Drive(e, legal =>
                {
                    if (offered.Count == 0)
                    {
                        offered.AddRange(legal);
                        prompt = e.PendingActivation?.Prompt;
                    }

                    return legal.FirstOrDefault(t => t.CardId == 0 && t.Def?.level == want);
                });
                var offeredAll = offered.Count == 4 && offered.All(t => t.CardId == 0) &&
                                 offered.Select(t => t.Def.level).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3, 4 });
                var oMonsters = o.MonstersOnField().Count();
                var oSetStays = o.TryFindSpellTrap(oSet, out _);
                bool outcome = idx switch
                {
                    1 => oMonsters == 0 && oSetStays && o.Hand.Count == 3 && p.Hand.Count == 0,
                    2 => oMonsters == 2 && !oSetStays && o.Hand.Count == 3 && p.Hand.Count == 0,
                    3 => oMonsters == 2 && oSetStays && o.Hand.Count == 1 && p.Hand.Count == 0,
                    _ => oMonsters == 2 && oSetStays && o.Hand.Count == 3 && p.Hand.Count == 2 &&
                         p.Deck.Count == pDeckBefore - 2
                };
                var ownKept = p.MonstersOnField().Count() == 2;
                Check($"Fuh-Rin-Ka-Zan: the player applies bullet {idx} as it resolves — only that bullet happens",
                    act && picked && offeredAll && outcome && ownKept && p.Graveyard.Contains(frkz) &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks &&
                    (prompt ?? "").IndexOf("apply 1 of these effects", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"act={act} picked={picked} offered={Names(offered)} prompt={prompt} oMon={oMonsters} oSet={oSetStays} " +
                    $"oHand={o.Hand.Count} pHand={p.Hand.Count} frkzGy={p.Graveyard.Contains(frkz)} {State(e)}");
            }
        }

        static void FuhRinKaZanAvailability(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // No opponent Spell/Trap and 1 card in their hand: only "destroy monsters" and "draw 2" are offered.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var frkz = PlaceSetTrap(e, p, FuhRinKaZan, 0);
                PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, SevenColoredFish, 1, BattlePosition.Attack, true);
                PlaceMonster(e, o, HinotamaSoul, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, frkz, fromHand: false);
                var offered = new List<CardInstance>();
                var picked = Drive(e, legal =>
                {
                    if (offered.Count == 0) offered.AddRange(legal);
                    return legal.FirstOrDefault(t => t.Def?.level == 4);
                });
                Check("Fuh-Rin-Ka-Zan: a bullet that cannot apply is not offered (no opponent S/T, 1 card in their hand)",
                    act && picked && offered.Select(t => t.Def.level).OrderBy(x => x).SequenceEqual(new[] { 1, 4 }) &&
                    p.Hand.Count == 2 && o.MonstersOnField().Count() == 2 && o.Hand.Count == 1,
                    $"act={act} picked={picked} offered={Names(offered)} pHand={p.Hand.Count} {State(e)}");
            }

            {
                // Every Attribute on your own side, nothing on theirs: only "draw 2" can apply — no prompt.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var frkz = PlaceSetTrap(e, p, FuhRinKaZan, 0);
                PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, SevenColoredFish, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, HinotamaSoul, 2, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 3, BattlePosition.Attack, true);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, frkz, fromHand: false);
                var prompted = false;
                Drive(e, legal =>
                {
                    prompted = true;
                    return legal.FirstOrDefault();
                });
                Check("Fuh-Rin-Ka-Zan: a lone applicable bullet applies with no prompt (draw 2)",
                    act && !prompted && p.Hand.Count == 2 && p.MonstersOnField().Count() == 4 && p.Graveyard.Contains(frkz),
                    $"act={act} prompted={prompted} pHand={p.Hand.Count} {State(e)}");

                // No bullet can apply (fewer than 2 cards in your Deck): it cannot be activated (Lua s.target chk).
                var e2 = PlayerMain(db, pDeck, aDeck);
                var p2 = e2.Player;
                var frkz2 = PlaceSetTrap(e2, p2, FuhRinKaZan, 0);
                PlaceMonster(e2, p2, HarpieLady, 0, BattlePosition.Attack, true);
                PlaceMonster(e2, p2, SevenColoredFish, 1, BattlePosition.Attack, true);
                PlaceMonster(e2, p2, HinotamaSoul, 2, BattlePosition.Attack, true);
                PlaceMonster(e2, p2, Celtic, 3, BattlePosition.Attack, true);
                while (p2.Deck.Count > 1) p2.Deck.RemoveAt(p2.Deck.Count - 1);
                e2.NotifyPublic();
                Check("Fuh-Rin-Ka-Zan: no bullet can apply — it cannot be activated",
                    !e2.CanActivateSpellTrap(p2, frkz2, fromHand: false), State(e2));
            }
        }

        static void FuhRinKaZanAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            Empty(e);
            var p = e.Player;
            var o = e.Opponent;
            var frkz = PlaceSetTrap(e, o, FuhRinKaZan, 0);
            PlaceMonster(e, p, HarpieLady, 0, BattlePosition.Attack, true);
            PlaceMonster(e, p, SevenColoredFish, 1, BattlePosition.Attack, true);
            PlaceMonster(e, p, Bewd, 2, BattlePosition.Attack, true);
            PlaceMonster(e, o, HinotamaSoul, 0, BattlePosition.Attack, true);
            PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            var pSet = PlaceSetTrap(e, p, PotOfGreed, 0);
            PutInHand(e, p, PotOfGreed);
            PutInHand(e, p, PotOfGreed);
            e.NotifyPublic();
            var oHand = o.Hand.Count;
            var act = e.TryActivateSpellTrap(o, frkz, fromHand: false);
            var promptedPlayer = false;
            Drive(e, legal =>
            {
                promptedPlayer = true;
                return null;
            });
            Check("Fuh-Rin-Ka-Zan (AI): applies the bullet worth most — destroys the player's monsters only",
                reached && act && !promptedPlayer && !p.MonstersOnField().Any() && o.MonstersOnField().Count() == 2 &&
                p.TryFindSpellTrap(pSet, out _) && p.Hand.Count == 2 && o.Hand.Count == oHand && o.Graveyard.Contains(frkz),
                $"reached={reached} act={act} prompted={promptedPlayer} pMon={p.MonstersOnField().Count()} " +
                $"pHand={p.Hand.Count} oHand={o.Hand.Count} {State(e)}");
        }

        // ───────────────────────────── Penalty Game! ─────────────────────────────

        static void PenaltyGameCondition(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var pg = PlaceSetTrap(e, p, PenaltyGame, 0);
            var results = new List<bool>();
            for (var n = 3; n <= 5; n++)
            {
                o.Hand.Clear();
                for (var i = 0; i < n; i++) PutInHand(e, o, PotOfGreed);
                results.Add(e.CanActivateSpellTrap(p, pg, fromHand: false));
            }

            Check("Penalty Game!: only while the opponent has exactly 4 cards in their hand",
                results.SequenceEqual(new[] { false, true, false }), string.Join(",", results));
        }

        static void PenaltyGameDrawLock(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var pg = PlaceSetTrap(e, p, PenaltyGame, 0);
            for (var i = 0; i < 4; i++) PutInHand(e, o, PotOfGreed);
            var oJar = PlaceSetTrap(e, o, JarOfGreed, 0);
            e.NotifyPublic();
            var act = e.TryActivateSpellTrap(p, pg, fromHand: false);
            // Chosen as it is activated: the picker opens before the card resolves.
            var atActivation = e.PendingActivation != null && !p.Graveyard.Contains(pg) &&
                               e.PendingActivation.LegalTargets.Count == 2 &&
                               e.PendingActivation.LegalTargets.All(t => t.CardId == 0);
            var picked = Drive(e, legal => legal.FirstOrDefault(t =>
                t.Def != null && t.Def.name.StartsWith("Your opponent cannot draw", StringComparison.Ordinal)));
            var stFree = !ContinuousNegationRuntime.ActivationLocked(e, o, oJar, out _);
            var oHand = o.Hand.Count;
            var oDeck = o.Deck.Count;
            var pHand = p.Hand.Count;
            var oppTurn = NextMainOf(e, o);
            var noDraw = oppTurn && o.Hand.Count == oHand && o.Deck.Count == oDeck;
            var myTurn = NextMainOf(e, p);
            var iDraw = myTurn && p.Hand.Count == pHand + 1;
            var oppNext = NextMainOf(e, o);
            var drawsAgain = oppNext && o.Hand.Count == oHand + 1 && o.Deck.Count == oDeck - 1;
            Check("Penalty Game! (bullet 1): the opponent does not draw in their next Draw Phase, then draws again; " +
                  "the player's draw and the opponent's Traps are untouched",
                act && atActivation && picked && p.Graveyard.Contains(pg) && stFree && noDraw && iDraw && drawsAgain,
                $"act={act} atActivation={atActivation} picked={picked} stFree={stFree} noDraw={noDraw} iDraw={iDraw} " +
                $"drawsAgain={drawsAgain} oHand {oHand}->{o.Hand.Count} oDeck {oDeck}->{o.Deck.Count} {State(e)}");
        }

        static void PenaltyGameAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            Empty(e);
            var p = e.Player;
            var o = e.Opponent;
            var pg = PlaceSetTrap(e, o, PenaltyGame, 0);
            for (var i = 0; i < 4; i++) PutInHand(e, p, PotOfGreed);
            e.NotifyPublic();
            var act = e.TryActivateSpellTrap(o, pg, fromHand: false);
            var promptedPlayer = false;
            Drive(e, _ =>
            {
                promptedPlayer = true;
                return null;
            });
            var pHand = p.Hand.Count;
            var pDeckCount = p.Deck.Count;
            var myTurn = NextMainOf(e, p);
            Check("Penalty Game! (AI): activates with no player prompt; the player does not draw in their next Draw Phase",
                reached && act && !promptedPlayer && o.Graveyard.Contains(pg) && myTurn && p.Hand.Count == pHand &&
                p.Deck.Count == pDeckCount,
                $"reached={reached} act={act} prompted={promptedPlayer} myTurn={myTurn} pHand {pHand}->{p.Hand.Count} {State(e)}");
        }

        static void PenaltyGameSpellTrapLock(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withPenalty in new[] { false, true })
            {
                // Turn 3 (the first player may battle): the AI holds Mirror Force against the attack.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e) && NextMainOf(e, e.Player);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                var oMst = PlaceSetTrap(e, o, Mst, 1);
                var oJar = PlaceSetTrap(e, o, JarOfGreed, 2);
                for (var i = 0; i < 4; i++) PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = true;
                var picked = true;
                if (withPenalty)
                {
                    var pg = PlaceSetTrap(e, p, PenaltyGame, 0);
                    act = e.TryActivateSpellTrap(p, pg, fromHand: false);
                    picked = Drive(e, legal => legal.FirstOrDefault(t =>
                        t.Def != null && t.Def.name.StartsWith("Your opponent cannot activate", StringComparison.Ordinal)));
                }

                var lockedSet = ContinuousNegationRuntime.ActivationLocked(e, o, oMst, out _);
                var lockedHand = o.Hand.Count > 0 && ContinuousNegationRuntime.ActivationLocked(e, o, o.Hand[0], out _);
                // A face-up card's effect is not a card activation (Lua re:IsHasType(EFFECT_TYPE_ACTIVATE)).
                oJar.FaceUp = true;
                var faceUpFree = !ChoiceAmongEffectsRuntime.ActivationLocked(e, o, oJar, out _);
                oJar.FaceUp = false;
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var lp = o.LifePoints;
                var attacked = e.TryAttack(p, bewd, null);
                DrainCombat(e);
                PassAll(e);
                if (!withPenalty)
                {
                    Check("Penalty Game! control: without it the AI answers the attack with Mirror Force",
                        reached && bp && attacked && !p.TryFindMonster(bewd, out _) && !lockedSet && !lockedHand,
                        $"reached={reached} bp={bp} attacked={attacked} bewd={p.TryFindMonster(bewd, out _)} " +
                        $"lp {lp}->{o.LifePoints} {State(e)}");
                    continue;
                }

                var oppTurn = NextMainOf(e, o);
                var freeNext = oppTurn && !ContinuousNegationRuntime.ActivationLocked(e, o, oMst, out _) &&
                               e.CanActivateSpellTrap(o, oMst, fromHand: false);
                Check("Penalty Game! (bullet 2): the opponent cannot activate Spell/Trap Cards this turn (Mirror Force stays " +
                      "Set, the attack lands); face-up effects are not locked; free again on their turn",
                    reached && act && picked && lockedSet && lockedHand && faceUpFree && bp && attacked &&
                    p.TryFindMonster(bewd, out _) && o.LifePoints == lp - 3000 && !mf.FaceUp && freeNext,
                    $"reached={reached} act={act} picked={picked} lockedSet={lockedSet} lockedHand={lockedHand} " +
                    $"faceUpFree={faceUpFree} bp={bp} attacked={attacked} lp {lp}->{o.LifePoints} mfUp={mf.FaceUp} " +
                    $"freeNext={freeNext} {State(e)}");
            }
        }

        /// <summary>Chained to the AI's Pot of Greed: the player still picks the bullet as Penalty Game! is activated.</summary>
        static void PenaltyGameChained(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            Empty(e);
            var p = e.Player;
            var o = e.Opponent;
            var pg = PlaceSetTrap(e, p, PenaltyGame, 0);
            var pot = PutInHand(e, o, PotOfGreed);
            for (var i = 0; i < 3; i++) PutInHand(e, o, Celtic);
            var pot2 = PutInHand(e, o, PotOfGreed);
            e.NotifyPublic();
            var potAct = e.TryActivateSpellTrap(o, pot, fromHand: true);
            var window = e.IsAwaitingResponse && e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                         e.PendingResponse.Responder == p;
            var chained = window && e.TryActivateChainResponse(p, pg, fromHand: false, autoPickTarget: false);
            var offered = e.PendingActivation?.LegalTargets?.Count(t => t.CardId == 0) ?? 0;
            var picked = Drive(e, legal => legal.FirstOrDefault(t =>
                t.Def != null && t.Def.name.StartsWith("Your opponent cannot activate", StringComparison.Ordinal)));
            var locked = ContinuousNegationRuntime.ActivationLocked(e, o, pot2, out _);
            var oHand = o.Hand.Count;
            var myTurn = NextMainOf(e, p);
            var oppTurn = myTurn && NextMainOf(e, o);
            // Bullet 2 only: the opponent draws normally in their next Draw Phase.
            var drew = oppTurn && o.Hand.Count == oHand + 1;
            Check("Penalty Game! chained to a link: the player picks its bullet as it is activated — only that one applies",
                reached && potAct && window && chained && offered == 2 && picked && locked && drew &&
                p.Graveyard.Contains(pg) && o.Graveyard.Contains(pot),
                $"reached={reached} potAct={potAct} window={window} chained={chained} offered={offered} picked={picked} " +
                $"locked={locked} drew={drew} oHand {oHand}->{o.Hand.Count} {State(e)}");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CardDef Def(string text, string type = "Trap Card") => new()
        {
            id = 0,
            name = "Choice near miss",
            type = type,
            frameType = type.StartsWith("Spell", StringComparison.Ordinal) ? "spell" : "trap",
            race = "Normal",
            attribute = "",
            archetype = "",
            desc = text,
            atk = -1,
            def = -1
        };

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

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            Empty(e);
            return e;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
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

        /// <summary>Every picker answers <paramref name="choose"/> (null cancels); every chain window passes.</summary>
        static bool Drive(DuelEngine e, Func<List<CardInstance>, CardInstance> choose)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                    var pick = choose(legal);
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

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>()).Select(t => t == null ? "null" : $"{t.Name}#{t.Def?.level}"));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null).Select(c =>
                $"{c.Timing}/{c.Action}/{c.Zone}/{c.Side}/amt={c.Amount}/cond={c.ActivationCondition}:{c.ConditionName}:" +
                $"{c.ConditionAmount}/choice={c.EffectChoiceIndex}{(c.EffectChoiceAppliedAtResolution ? "@res" : "")}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
