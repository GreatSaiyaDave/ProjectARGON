using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ConditionalSpellsAndTraps batch: Blasting the Ruins, Chaos End, Chaos Greed, Self-Destruct Button, Ojama Delta
    /// Hurricane!!, Gryphon's Feather Duster, Destruction Ring, D.D. Borderline. Compile shapes through the real compiler,
    /// near-miss sentences, and mini-duels through the engine entry points (CanActivateSpellTrap, TryActivateSpellTrap,
    /// TrySelectEffectTarget, CanConductBattlePhase / TryEnterBattlePhase). Turn 1 is the Player's, turn 2 the AI's.
    /// Runtime: <see cref="ConditionalSpellsAndTrapsRuntime"/>.
    /// </summary>
    public static class ConditionalSpellsAndTrapsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BlastingTheRuins = 21466326;   // Normal Trap
        const int ChaosEnd = 61044390;           // Normal Spell
        const int ChaosGreed = 97439308;         // Normal Spell
        const int SelfDestructButton = 57585212; // Normal Trap
        const int OjamaDelta = 8251996;          // Ojama Delta Hurricane!!, Normal Spell
        const int GryphonDuster = 34370473;      // Gryphon's Feather Duster, Normal Spell
        const int DestructionRing = 21219755;    // Normal Trap
        const int DdBorderline = 60912752;       // D.D. Borderline, Continuous Spell

        const int OjamaGreen = 12482652;
        const int OjamaYellow = 42941100;
        const int OjamaBlack = 79335209;
        const int Bewd = 89631139;               // LIGHT Dragon 3000/2500
        const int Celtic = 91152256;             // EARTH Warrior 1400/1200
        const int Kuriboh = 40640057;
        const int PotOfGreed = 55144522;         // Normal Spell
        const int MirrorForce = 44095762;        // Normal Trap (attack only: the AI cannot chain it here)
        const int Yami = 59197169;               // Field Spell

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("ConditionalSpellsAndTraps: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            BlastingTheRuinsDuels(db, pDeck, aDeck, Check);
            ChaosEndDuels(db, pDeck, aDeck, Check);
            ChaosGreedDuels(db, pDeck, aDeck, Check);
            SelfDestructButtonDuels(db, pDeck, aDeck, Check);
            OjamaDeltaDuels(db, pDeck, aDeck, Check);
            GryphonDusterDuels(db, pDeck, aDeck, Check);
            DestructionRingDuels(db, pDeck, aDeck, Check);
            DdBorderlineDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

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

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }
        }

        static void EndTurn(DuelEngine e)
        {
            Drive(e, _ => false);
            e.TryEndTurnSafe(e.TurnPlayer);
            Drive(e, _ => false);
        }

        static void Fill(DuelEngine e, List<CardInstance> pile, int id, int n)
        {
            pile.Clear();
            for (var i = 0; i < n; i++) pile.Add(e.CreateCardInstance(id));
        }

        static CardInstance PlaceFaceUpSpell(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.TryFindMonster(c, out var z)) return $"{tag}M{z}{(c.FaceUp ? "u" : "d")}{c.Position}";
                if (who.TryFindSpellTrap(c, out var s)) return $"{tag}S{s}";
                if (who.FieldSpellZone?.Occupant == c) return tag + "Field";
                if (who.Hand.Contains(c)) return tag + "Hand";
                if (who.Graveyard.Contains(c)) return tag + "GY";
                if (who.Banished.Contains(c)) return tag + "Banished";
            }
            return "gone";
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990000002, name = "ConditionalSpellsAndTraps probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : "spell", race = race, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var blast = Prog(db, BlastingTheRuins);
            Check("Blasting the Ruins: one burn clause gated on 30+ cards in your GY",
                blast.FullyCompiled && blast.ClauseList.Count == 1 &&
                Has(blast, c => c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 3000 &&
                                c.ActivationCondition == ConditionalSpellsAndTrapsRuntime.YourGyAtLeast &&
                                c.ConditionAmount == 30) && Implemented(db, BlastingTheRuins),
                Describe(blast));

            var end = Prog(db, ChaosEnd);
            Check("Chaos End: destroy all monsters (both sides), gated on 7+ banished and a monster",
                end.FullyCompiled && end.ClauseList.Count == 1 &&
                Has(end, c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldMonsters &&
                              c.Side == EffectSide.Both && !c.RequiresTargetChoice &&
                              c.ActivationCondition ==
                              ConditionalSpellsAndTrapsRuntime.YourBanishedAtLeastAndMonsterOnField &&
                              c.ConditionAmount == 7) && Implemented(db, ChaosEnd),
                Describe(end));

            var greed = Prog(db, ChaosGreed);
            Check("Chaos Greed: draw 2 gated on 4+ banished, an empty GY and 2 cards to draw",
                greed.FullyCompiled && greed.ClauseList.Count == 1 &&
                Has(greed, c => c.Action == EffectActionKind.Draw && c.Amount == 2 &&
                                c.ActivationCondition == ConditionalSpellsAndTrapsRuntime.YourBanishedAtLeastNoGyCanDraw &&
                                c.ConditionAmount == 4) && Implemented(db, ChaosGreed),
                Describe(greed));

            var sdb = Prog(db, SelfDestructButton);
            Check("Self-Destruct Button: both LP become 0, gated on a 7000+ LP gap (Frozen Soul's condition)",
                sdb.FullyCompiled && sdb.ClauseList.Count == 1 &&
                Has(sdb, c => c.Action == EffectActionKind.BothPlayersLifePointsBecomeZero &&
                              c.ActivationCondition == DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent &&
                              c.ConditionAmount == 7000) && Implemented(db, SelfDestructButton) &&
                EffectVocabulary.ResolutionOf(sdb.ClauseList[0]) != EffectResolutionKind.UniqueException,
                Describe(sdb));

            var ojama = Prog(db, OjamaDelta);
            Check("Ojama Delta Hurricane!!: destroy every card the opponent controls, gated on the three Ojamas",
                ojama.FullyCompiled && ojama.ClauseList.Count == 1 &&
                Has(ojama, c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.OppAnyCardOnField &&
                                c.Side == EffectSide.Opponent && !c.RequiresTargetChoice &&
                                c.ActivationCondition ==
                                ConditionalSpellsAndTrapsRuntime.NamedFaceUpAndOpponentControlsACard &&
                                c.ConditionName == "Ojama Green|Ojama Yellow|Ojama Black") &&
                Implemented(db, OjamaDelta),
                Describe(ojama));

            var gry = Prog(db, GryphonDuster);
            Check("Gryphon's Feather Duster: destroy your other Spells/Traps, then 500 x destroyed (not a flat gain)",
                gry.FullyCompiled && gry.ClauseList.Count == 2 &&
                gry.ClauseList[0].Action == EffectActionKind.Destroy &&
                gry.ClauseList[0].Zone == EffectZoneFilter.FieldSpellTraps &&
                gry.ClauseList[0].Side == EffectSide.Controller && gry.ClauseList[0].ExceptThisCard &&
                !gry.ClauseList[0].RequiresTargetChoice &&
                gry.ClauseList[0].ActivationCondition == ConditionalSpellsAndTrapsRuntime.YouControlAnotherSpellTrap &&
                gry.ClauseList[1].Action == EffectActionKind.GainLifePoints && gry.ClauseList[1].Amount == 500 &&
                gry.ClauseList[1].CountSource == PerCountBurnsRuntime.PreviousClauseHits &&
                gry.ClauseList[1].RequiresPreviousClauseHit && Implemented(db, GryphonDuster),
                Describe(gry));

            var ring = Prog(db, DestructionRing);
            Check("Destruction Ring: targets 1 face-up monster you control; destroy then 1000 to both",
                ring.FullyCompiled && ring.ClauseList.Count == 1 &&
                Has(ring, c => c.Action == EffectActionKind.DestroyTargetThenDamageBothPlayers && c.Amount == 1000 &&
                               c.Zone == EffectZoneFilter.ControllerAnyMonsters && c.RequiresTargetChoice &&
                               c.IsPsctTarget && c.RequiresTargetFaceUp && c.TargetCount == 1) &&
                Implemented(db, DestructionRing),
                Describe(ring));

            var dd = Prog(db, DdBorderline);
            Check("D.D. Borderline: a continuous Battle Phase lock for both players",
                dd.FullyCompiled && dd.ClauseList.Count == 1 &&
                Has(dd, c => c.Action == EffectActionKind.NoBattlePhaseWhileNoSpellInControllerGy &&
                             c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Side == EffectSide.Both) &&
                Implemented(db, DdBorderline),
                Describe(dd));

            // Near misses: look-alike wordings stay closed (no condition dropped, no other zone guessed).
            var nearMisses = new (string type, string race, string text)[]
            {
                ("Trap Card", "Normal",
                    "You can only activate this card if there are 30 or more cards in your opponent's Graveyard. " +
                    "Inflict 3000 points of damage to your opponent's Life Points."),
                ("Spell Card", "Normal",
                    "You can only activate this card if 7 or more of your cards are currently removed from play. " +
                    "Destroy all Spell and Trap Cards on the field."),
                ("Spell Card", "Normal",
                    "You can only activate this card if 4 or more of your cards are currently removed from play and " +
                    "there are no cards in your hand. Draw 2 cards from your Deck."),
                ("Trap Card", "Normal",
                    "You can only activate this card while your Life Points are lower than your opponent's Life Points " +
                    "and the difference is 7000 points or more. Your opponent's Life Points become 0."),
                ("Spell Card", "Normal",
                    "You can only activate this card while \"Ojama Green\", \"Ojama Yellow\" and \"Ojama Black\" are " +
                    "face-up on your side of the field. Destroy all monsters on your opponent's side of the field " +
                    "and inflict 300 damage to your opponent."),
                ("Spell Card", "Normal",
                    "Destroy all Spell and Trap Cards on your side of the field. Increase your opponent's Life Points " +
                    "by the number of destroyed cards x 500 points."),
                ("Trap Card", "Normal",
                    "Destroy 1 face-up monster on your opponent's side of the field to inflict 1000 points of damage " +
                    "to both players' Life Points."),
                ("Spell Card", "Continuous",
                    "When there are no Trap Cards in your Graveyard, neither player can conduct their Battle Phase."),
            };
            var open = new List<string>();
            foreach (var (type, race, text) in nearMisses)
            {
                var p = CompileText(type, race, text);
                if (p.FullyCompiled) open.Add(text.Substring(0, Math.Min(60, text.Length)) + " => " + Describe(p));
            }

            Check("near misses stay not FullyCompiled", open.Count == 0, string.Join(" | ", open));
        }

        // ───────────────────────────── Blasting the Ruins ─────────────────────────────

        static void BlastingTheRuinsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var btr = PlaceSetTrap(e, p, BlastingTheRuins, 0);
            Fill(e, p.Graveyard, Kuriboh, 29);
            Fill(e, o.Graveyard, Kuriboh, 40);
            var at29 = e.CanActivateSpellTrap(p, btr, fromHand: false);
            Fill(e, p.Graveyard, Kuriboh, 30);
            var at30 = e.CanActivateSpellTrap(p, btr, fromHand: false);
            var oLp = o.LifePoints;
            var pLp = p.LifePoints;
            var act = e.TryActivateSpellTrap(p, btr, fromHand: false);
            Drive(e, _ => false);
            Check("Blasting the Ruins: 30+ cards in YOUR GY (29 no — the opponent's 40 do not count); 3000 to the opponent",
                !at29 && at30 && act && o.LifePoints == oLp - 3000 && p.LifePoints == pLp && p.Graveyard.Contains(btr),
                $"29={at29} 30={at30} act={act} oLp={oLp}->{o.LifePoints} pLp={pLp}->{p.LifePoints}");
        }

        // ───────────────────────────── Chaos End ─────────────────────────────

        static void ChaosEndDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ce = PutInHand(e, p, ChaosEnd);
                Fill(e, p.Banished, Kuriboh, 7);
                var noMonster = e.CanActivateSpellTrap(p, ce, fromHand: true);
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var setCeltic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Defense, false);
                var kuri = PlaceMonster(e, o, Kuriboh, 2, BattlePosition.Attack, true);
                Fill(e, p.Banished, Kuriboh, 6);
                Fill(e, o.Banished, Kuriboh, 10);
                var at6 = e.CanActivateSpellTrap(p, ce, fromHand: true);
                Fill(e, p.Banished, Kuriboh, 7);
                var at7 = e.CanActivateSpellTrap(p, ce, fromHand: true);
                var act = e.TryActivateSpellTrap(p, ce, fromHand: true);
                Drive(e, _ => false);
                Check("Chaos End: 7+ of YOUR cards banished and a monster on the field; every monster (face-down too) " +
                      "on both fields is destroyed",
                    !noMonster && !at6 && at7 && act && p.Graveyard.Contains(bewd) && o.Graveyard.Contains(setCeltic) &&
                    o.Graveyard.Contains(kuri) && p.MonsterCount == 0 && o.MonsterCount == 0 &&
                    p.Graveyard.Contains(ce),
                    $"noMonster={noMonster} 6={at6} 7={at7} act={act} bewd={Where(e, bewd)} " +
                    $"celtic={Where(e, setCeltic)} kuri={Where(e, kuri)}");
            }
        }

        // ───────────────────────────── Chaos Greed ─────────────────────────────

        static void ChaosGreedDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var cg = PutInHand(e, p, ChaosGreed);
            Fill(e, p.Banished, Kuriboh, 3);
            var at3 = e.CanActivateSpellTrap(p, cg, fromHand: true);
            Fill(e, p.Banished, Kuriboh, 4);
            p.Graveyard.Add(e.CreateCardInstance(Kuriboh));
            var withGy = e.CanActivateSpellTrap(p, cg, fromHand: true);
            p.Graveyard.Clear();
            var deck = p.Deck.ToList();
            p.Deck.RemoveRange(1, p.Deck.Count - 1);
            var deck1 = e.CanActivateSpellTrap(p, cg, fromHand: true);
            p.Deck.Clear();
            p.Deck.AddRange(deck);
            var ok = e.CanActivateSpellTrap(p, cg, fromHand: true);
            var hand = p.Hand.Count;
            var deckCount = p.Deck.Count;
            var act = e.TryActivateSpellTrap(p, cg, fromHand: true);
            Drive(e, _ => false);
            Check("Chaos Greed: 4+ banished (3 no), no card in your GY (1 no), 2 cards to draw (1 no); draws 2",
                !at3 && !withGy && !deck1 && ok && act && p.Hand.Count == hand - 1 + 2 &&
                p.Deck.Count == deckCount - 2 && p.Graveyard.Contains(cg),
                $"3={at3} gy={withGy} deck1={deck1} ok={ok} act={act} hand={hand}->{p.Hand.Count} " +
                $"deck={deckCount}->{p.Deck.Count}");
        }

        // ───────────────────────────── Self-Destruct Button ─────────────────────────────

        static void SelfDestructButtonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var sdb = PlaceSetTrap(e, p, SelfDestructButton, 0);
            p.LifePoints = 1001;
            o.LifePoints = 8000;
            var gap6999 = e.CanActivateSpellTrap(p, sdb, fromHand: false);
            o.LifePoints = 1000;
            p.LifePoints = 8000;
            var ahead = e.CanActivateSpellTrap(p, sdb, fromHand: false);
            p.LifePoints = 1000;
            o.LifePoints = 8000;
            var gap7000 = e.CanActivateSpellTrap(p, sdb, fromHand: false);
            var act = e.TryActivateSpellTrap(p, sdb, fromHand: false);
            Drive(e, _ => false);
            Check("Self-Destruct Button: only while your LP is 7000+ lower (6999 no, ahead no); both LP become 0 — a DRAW",
                !gap6999 && !ahead && gap7000 && act && p.LifePoints == 0 && o.LifePoints == 0 && e.GameOver &&
                e.Winner == null && e.EndReason == DuelEndReason.Draw,
                $"6999={gap6999} ahead={ahead} 7000={gap7000} act={act} lp={p.LifePoints}/{o.LifePoints} " +
                $"over={e.GameOver} winner={e.Winner?.Name ?? "none"} reason={e.EndReason}");
        }

        // ───────────────────────────── Ojama Delta Hurricane!! ─────────────────────────────

        static void OjamaDeltaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var odh = PutInHand(e, p, OjamaDelta);
                var green = PlaceMonster(e, p, OjamaGreen, 0, BattlePosition.Defense, true);
                var yellow = PlaceMonster(e, p, OjamaYellow, 1, BattlePosition.Defense, true);
                var black = PlaceMonster(e, p, OjamaBlack, 2, BattlePosition.Defense, false);
                var ownTrap = PlaceSetTrap(e, p, MirrorForce, 1);
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var setMon = PlaceMonster(e, o, Celtic, 1, BattlePosition.Defense, false);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                var yami = e.CreateCardInstance(Yami);
                yami.FaceUp = true;
                o.FieldSpellZone.Occupant = yami;
                var blackFaceDown = e.CanActivateSpellTrap(p, odh, fromHand: true);
                black.FaceUp = true;
                var allUp = e.CanActivateSpellTrap(p, odh, fromHand: true);
                var act = e.TryActivateSpellTrap(p, odh, fromHand: true);
                Drive(e, _ => false);
                Check("Ojama Delta Hurricane!!: all three Ojamas face-up (one Set: no); every card the opponent " +
                      "controls is destroyed (monsters, Set cards, Field Spell); yours stay",
                    !blackFaceDown && allUp && act && o.Graveyard.Contains(bewd) && o.Graveyard.Contains(setMon) &&
                    o.Graveyard.Contains(mf) && o.Graveyard.Contains(yami) && o.Graveyard.Count(c => c == yami) == 1 &&
                    p.TryFindMonster(green, out _) && p.TryFindMonster(yellow, out _) && p.TryFindMonster(black, out _) &&
                    p.TryFindSpellTrap(ownTrap, out _) && p.Graveyard.Contains(odh),
                    $"down={blackFaceDown} up={allUp} act={act} bewd={Where(e, bewd)} set={Where(e, setMon)} " +
                    $"mf={Where(e, mf)} yami={Where(e, yami)} own={Where(e, ownTrap)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var odh = PutInHand(e, p, OjamaDelta);
                PlaceMonster(e, p, OjamaGreen, 0, BattlePosition.Defense, true);
                PlaceMonster(e, p, OjamaYellow, 1, BattlePosition.Defense, true);
                PlaceMonster(e, p, OjamaBlack, 2, BattlePosition.Defense, true);
                var empty = e.CanActivateSpellTrap(p, odh, fromHand: true);
                PlaceMonster(e, e.Opponent, Kuriboh, 0, BattlePosition.Attack, true);
                var one = e.CanActivateSpellTrap(p, odh, fromHand: true);
                Check("Ojama Delta Hurricane!!: needs a card on the opponent's field", !empty && one,
                    $"empty={empty} one={one}");
            }
        }

        // ───────────────────────────── Gryphon's Feather Duster ─────────────────────────────

        static void GryphonDusterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var gfd = PutInHand(e, p, GryphonDuster);
                var oppTrap = PlaceSetTrap(e, o, MirrorForce, 0);
                var alone = e.CanActivateSpellTrap(p, gfd, fromHand: true);
                var mf = PlaceSetTrap(e, p, MirrorForce, 1);
                var pot = PlaceSetTrap(e, p, PotOfGreed, 2);
                var yami = e.CreateCardInstance(Yami);
                yami.FaceUp = true;
                p.FieldSpellZone.Occupant = yami;
                var ok = e.CanActivateSpellTrap(p, gfd, fromHand: true);
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, gfd, fromHand: true);
                Drive(e, _ => false);
                Check("Gryphon's Feather Duster: needs another Spell/Trap of yours; destroys your 3 (Field Spell too), " +
                      "gains 3 x 500; the opponent's stay",
                    !alone && ok && act && p.Graveyard.Contains(mf) && p.Graveyard.Contains(pot) &&
                    p.Graveyard.Contains(yami) && p.Graveyard.Contains(gfd) && o.TryFindSpellTrap(oppTrap, out _) &&
                    p.LifePoints == lp + 1500 && o.LifePoints == oLp,
                    $"alone={alone} ok={ok} act={act} lp={lp}->{p.LifePoints} oLp={oLp}->{o.LifePoints} " +
                    $"mf={Where(e, mf)} pot={Where(e, pot)} yami={Where(e, yami)} opp={Where(e, oppTrap)}");
            }
        }

        // ───────────────────────────── Destruction Ring ─────────────────────────────

        static void DestructionRingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var setMon = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, false);
                var oppMon = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var ring = PlaceSetTrap(e, p, DestructionRing, 0);
                var onlySet = e.CanActivateSpellTrap(p, ring, fromHand: false);
                var bewd = PlaceMonster(e, p, Bewd, 2, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, ring, fromHand: false);
                var legal = Legal(e);
                Drive(e, c => c == bewd);
                Check("Destruction Ring: targets only a face-up monster you control (a Set one: cannot activate); " +
                      "destroys it, then 1000 damage to both players",
                    !onlySet && act && !legal.Contains(setMon) && !legal.Contains(oppMon) &&
                    p.Graveyard.Contains(bewd) && p.TryFindMonster(setMon, out _) && o.TryFindMonster(oppMon, out _) &&
                    p.LifePoints == lp - 1000 && o.LifePoints == oLp - 1000 && p.Graveyard.Contains(ring),
                    $"onlySet={onlySet} act={act} legal=[{string.Join(",", legal.Select(c => c.Name))}] " +
                    $"bewd={Where(e, bewd)} lp={lp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                // The target was turned face-down before the Chain resolves (Book of Moon): Lua tc:IsFaceup() fails —
                // nothing is destroyed and nobody takes damage. The resolution arm itself, on that target.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 2, BattlePosition.Defense, false);
                var ring = PlaceSetTrap(e, p, DestructionRing, 0);
                var clause = Prog(db, DestructionRing).ClauseList[0];
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var destroyCalls = 0;
                ConditionalSpellsAndTrapsRuntime.ApplyDestroyTargetThenDamageBoth(e, p, ring, clause, bewd,
                    _ => destroyCalls++);
                Check("Destruction Ring: a target turned face-down before resolution is not destroyed; no damage",
                    destroyCalls == 0 && p.TryFindMonster(bewd, out _) && p.LifePoints == lp && o.LifePoints == oLp,
                    $"destroyCalls={destroyCalls} bewd={Where(e, bewd)} lp={lp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                // Both players at 1000: the damage is simultaneous, so both reach 0 together — a DRAW.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 1000;
                o.LifePoints = 1000;
                var bewd = PlaceMonster(e, p, Bewd, 2, BattlePosition.Attack, true);
                var ring = PlaceSetTrap(e, p, DestructionRing, 0);
                var act = e.TryActivateSpellTrap(p, ring, fromHand: false);
                Drive(e, c => c == bewd);
                Check("Destruction Ring: 1000 to both at 1000 LP each ends the Duel in a DRAW",
                    act && p.LifePoints == 0 && o.LifePoints == 0 && e.GameOver && e.Winner == null,
                    $"act={act} lp={p.LifePoints}/{o.LifePoints} over={e.GameOver} winner={e.Winner?.Name ?? "none"}");
            }
        }

        // ───────────────────────────── D.D. Borderline ─────────────────────────────

        static void DdBorderlineDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ddb = PutInHand(e, p, DdBorderline);
                var act = e.TryActivateSpellTrap(p, ddb, fromHand: true);
                Drive(e, _ => false);
                var faceUp = p.TryFindSpellTrap(ddb, out _) && ddb.FaceUp;
                PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                // A Spell in the opponent's GY does not lift it: only its controller's GY counts.
                o.Graveyard.Add(e.CreateCardInstance(PotOfGreed));
                EndTurn(e);
                var oppTurn = e.TurnPlayer == o && e.TurnNumber == 2 && e.Phase == DuelPhase.Main1;
                var locked = !e.CanConductBattlePhase;
                var entered = e.TryEnterBattlePhase(o);
                p.Graveyard.Add(e.CreateCardInstance(PotOfGreed));
                var unlocked = e.CanConductBattlePhase;
                Check("D.D. Borderline: no Spell in its controller's GY — the opponent cannot enter the Battle Phase " +
                      "(their own GY's Spell does not count); a Spell in yours lifts it",
                    act && faceUp && oppTurn && locked && !entered && e.Phase != DuelPhase.Battle && unlocked,
                    $"act={act} faceUp={faceUp} turn2={oppTurn} phase={e.Phase} locked={locked} entered={entered} " +
                    $"unlocked={unlocked}");
            }

            {
                // The lock binds its own controller too, and ends when the card leaves the field.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ddb = PlaceFaceUpSpell(e, o, DdBorderline, 0);
                EndTurn(e);
                EndTurn(e);
                var turn3 = e.TurnPlayer == p && e.TurnNumber == 3;
                var locked = !e.CanConductBattlePhase && !e.TryEnterBattlePhase(p);
                o.SpellTrapZones[0].Occupant = null;
                var free = e.CanConductBattlePhase;
                Check("D.D. Borderline: the opponent's copy locks your Battle Phase; gone, the lock ends",
                    turn3 && locked && free, $"turn3={turn3} locked={locked} free={free} ddb={Where(e, ddb)}");
            }
        }
    }
}
