using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "PerCopyDeck" (v163): the per-copy Deck pile (<see cref="DeckPile"/>: the List&lt;int&gt; surface, owner
    /// and face-up metadata that shuffles keep, the same RNG sequence), owners' piles for cards held by another player,
    /// and the cards that need them — Parasite Paracide, Pharaoh's Treasure and Graverobber. Runtime:
    /// <see cref="PerCopyDeckRuntime"/>.
    /// </summary>
    public static class PerCopyDeckRegressionTests
    {
        const int ParasiteParacide = 27911549; // Insect Lv2 500/300
        const int PharaohsTreasure = 63571750;
        const int Graverobber = 61705417;

        const int Celtic = 91152256;           // Warrior Lv4 1400/1200
        const int SummonedSkull = 70781052;    // Fiend Lv6 2500/1200
        const int DianKeto = 84257640;         // Normal Spell: gain 1000 LP
        const int Mst = 5318639;               // Quick-Play Spell
        const int BlackPendant = 65169794;     // Equip Spell

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("PerCopyDeck: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            DeckPileUnits(db, pDeck, aDeck, Check);
            ParacideDuels(db, pDeck, aDeck, Check);
            PharaohsTreasureDuels(db, pDeck, aDeck, Check);
            GraverobberDuels(db, pDeck, aDeck, Check);
            CopyIdentityPaths(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}]";

        static CompiledCardProgram CompileText(int id, string type, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "PerCopyDeck probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = type.StartsWith("Trap") || type.StartsWith("Spell") ? "Normal" : "Insect",
                attribute = type.StartsWith("Trap") || type.StartsWith("Spell") ? null : "EARTH",
                level = 2, atk = 500, def = 300, desc = text
            });

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>The Player's Main Phase 1 (turn 3+) with an empty board and empty hands.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            engine.Player.Graveyard.Clear();
            engine.Opponent.Graveyard.Clear();
            return engine;
        }

        /// <summary>The index of the copy carrying a "when drawn" trigger, or -1.</summary>
        static int TriggerCopy(DeckPile deck)
        {
            for (var i = 0; i < deck.Count; i++)
                if (deck.InfoAt(i)?.DrawTrigger == true)
                    return i;
            return -1;
        }

        static int SpecialCopies(DeckPile deck)
        {
            var n = 0;
            for (var i = 0; i < deck.Count; i++)
                if (deck.InfoAt(i) != null)
                    n++;
            return n;
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(ParasiteParacide));
                var flip = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Flip);
                var drawn = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.DrawnFaceUpFromDeck);
                Check("Parasite Paracide compiles to its FLIP and its draw trigger (1000 damage, Insect)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 &&
                    flip?.Action == EffectActionKind.PutThisFaceUpInOpponentDeck &&
                    drawn?.Action == EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace &&
                    drawn.Amount == 1000 && drawn.RaceFilter == "Insect" && drawn.SummonInDefense &&
                    CardEffectStatus.Classify(db.Get(ParasiteParacide)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(PharaohsTreasure));
                var act = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
                var drawn = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.DrawnFaceUpFromDeck);
                Check("Pharaoh's Treasure compiles to its no-target activation and its targeting draw trigger",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 &&
                    act?.Action == EffectActionKind.ShuffleThisFaceUpIntoYourDeck && !act.IsPsctTarget &&
                    !act.RequiresTargetChoice && act.Zone == EffectZoneFilter.None &&
                    drawn?.Action == EffectActionKind.SendDrawnThisToGyThenAddGyTargetToHand && drawn.IsPsctTarget &&
                    drawn.Zone == EffectZoneFilter.ControllerGyCards &&
                    CardEffectStatus.Classify(db.Get(PharaohsTreasure)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Graverobber));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Graverobber compiles to 1 Spell in the opponent's GY taken into your hand (2000 damage on use)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.AddOpponentGySpellToHandUsableThisTurn &&
                    c.Zone == EffectZoneFilter.OppGySpells && c.RequiresTargetChoice && c.IsPsctTarget &&
                    c.Amount == 2000 &&
                    CardEffectStatus.Classify(db.Get(Graverobber)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
        }

        static void NearMisses(CheckFn Check)
        {
            var misses = new (string Type, string Text, string Why)[]
            {
                ("Flip Effect Monster",
                    "FLIP: Put this card face-up in your opponent's Deck and shuffle it.",
                    "the FLIP without its draw trigger"),
                ("Flip Effect Monster",
                    "FLIP: Put this card face-up in your opponent's Deck and shuffle it. When your opponent draws this " +
                    "card, it is Special Summoned to their side of the field in face-up Attack Position, and inflicts " +
                    "1000 damage to them. Then, while this card is face-up on the field, all monsters your opponent " +
                    "controls become Insect-Type monsters.",
                    "Attack Position instead of Defense"),
                ("Flip Effect Monster",
                    "FLIP: Put this card face-up in your opponent's Deck and shuffle it. When your opponent draws this " +
                    "card, it is Special Summoned to their side of the field in face-up Defense Position, and inflicts " +
                    "1000 damage to them.",
                    "no Type change"),
                ("Trap Card", "Shuffle this card face-up into your Deck.", "the activation without its draw trigger"),
                ("Trap Card",
                    "Shuffle this card face-down into your Deck. If you draw this face-up card that was added to the " +
                    "Deck by this effect: Send it to the GY, then target 1 card in your GY, except this card; add that " +
                    "target to your hand.",
                    "face-down"),
                ("Trap Card",
                    "If you draw this face-up card that was added to the Deck by this effect: Send it to the GY, then " +
                    "target 1 card in your GY, except this card; add that target to your hand.",
                    "the draw trigger without the activation"),
                ("Trap Card",
                    "Select 1 Trap Card from your opponent's Graveyard. You can use it as your hand until the end of " +
                    "the turn. If you use it, you take 2000 points of damage.",
                    "a Trap instead of a Spell"),
                ("Trap Card",
                    "Select 1 Spell Card from your opponent's Graveyard. You can use it as your hand until the end of " +
                    "the turn.",
                    "no damage sentence")
            };
            foreach (var (type, text, why) in misses)
            {
                var prog = CompileText(990001, type, text);
                Check($"near miss stays not FullyCompiled ({why})",
                    prog == null || !prog.FullyCompiled ||
                    !prog.ClauseList.Exists(c =>
                        c.Action is EffectActionKind.PutThisFaceUpInOpponentDeck or
                            EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace or
                            EffectActionKind.ShuffleThisFaceUpIntoYourDeck or
                            EffectActionKind.SendDrawnThisToGyThenAddGyTargetToHand or
                            EffectActionKind.AddOpponentGySpellToHandUsableThisTurn),
                    Describe(prog));
            }
        }

        // ───────────────────────── the Deck pile ─────────────────────────

        static void DeckPileUnits(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The List<int> surface: same members, same results.
                var pile = new DeckPile();
                pile.Add(1);
                pile.Add(2);
                pile.Insert(0, 3);
                pile.AddRange(new[] { 4, 5, 4 });
                var a = pile.Count == 6 && pile[0] == 3 && pile.IndexOf(2) == 2 && pile.Contains(5) &&
                        pile.LastIndexOf(4) == 5 && pile.FindIndex(x => x == 5) == 4;
                var removed = pile.Remove(1) && !pile.Remove(99);
                var all = pile.RemoveAll(x => x == 4);
                var b = pile.SequenceEqual(new[] { 3, 2, 5 }) && all == 2;
                pile.RemoveRange(0, 1);
                pile.RemoveAt(pile.Count - 1);
                var c = pile.Count == 1 && pile.First() == 2 && pile.ToList().Count == 1 && pile.Any();
                pile[0] = 7;
                pile.Clear();
                DeckPile fromList = new List<int> { 8, 9 };
                var d = pile.Count == 0 && fromList.Count == 2 && fromList[0] == 8 && fromList.Take(1).Single() == 8 &&
                        !fromList.HasSpecialCopies;
                Check("DeckPile keeps the List<int> surface (Add, Insert, AddRange, Remove, RemoveAll, RemoveRange, " +
                      "indexer, LINQ, assignment from a List<int>)",
                    a && removed && b && c && d, $"a={a} removed={removed} b={b} c={c} d={d}");
            }

            {
                // A shuffle makes the same RNG calls as the list shuffle and moves the metadata with its id.
                var ids = Enumerable.Range(1000, 40).ToList();
                var pile = new DeckPile(ids);
                var info = new DeckCopyInfo { FaceUp = true, DrawTrigger = true };
                pile.RemoveAt(10);
                pile.Insert(10, 1010, info);
                var list = new List<int>(pile);
                var r1 = new DuelRng(424242);
                var r2 = new DuelRng(424242);
                r1.Shuffle(list);
                r2.Shuffle(pile);
                var same = list.SequenceEqual(pile) && r1.Draws == r2.Draws && r1.Next(1000) == r2.Next(1000);
                var at = pile.IndexOf(1010);
                var follows = at >= 0 && ReferenceEquals(pile.InfoAt(at), info) && SpecialCopies(pile) == 1;
                // A reorder (taken out and put straight back) keeps it; another card put back does not take it.
                pile.RemoveAt(at);
                pile.Insert(0, 1010);
                var reorder = ReferenceEquals(pile.InfoAt(0), info);
                pile.RemoveAt(0);
                pile.Insert(0, 1);
                pile.Insert(1, 1010);
                var notTaken = pile.InfoAt(0) == null && pile.InfoAt(1) == null && SpecialCopies(pile) == 0;
                Check("DeckPile: a shuffle replays the seeded list shuffle and the face-up copy moves with its id; a " +
                      "reorder keeps it",
                    same && follows && reorder && notTaken,
                    $"same={same} follows={follows} at={at} reorder={reorder} notTaken={notTaken}");
            }

            {
                // Deck-out and the Deck count are unchanged.
                var e = Fresh(db, pDeck, aDeck);
                var o = e.Opponent;
                var count = o.DeckCount == o.Deck.Count;
                o.Deck.Clear();
                e.Draw(o, 1);
                Check("DeckPile: drawing from an empty Deck still loses the duel", count && e.GameOver,
                    $"count={count} over={e.GameOver}");
            }

            {
                // A copy owned by the other player: drawn, it keeps its owner, and a discard sends it to its owner's GY;
                // milled (removed, then an instance created and sent to the GY), it goes to its owner's GY.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                o.Deck.Insert(0, Celtic, new DeckCopyInfo { Owner = p });
                e.Draw(o, 1);
                var drawn = o.Hand.LastOrDefault();
                var owned = drawn != null && drawn.CardId == Celtic && drawn.ControlOwner == p;
                MonsterEffects.DiscardFromHandByInstance(o, drawn);
                var discarded = drawn != null && p.Graveyard.Contains(drawn) && !o.Graveyard.Contains(drawn) &&
                                drawn.ControlOwner == null;

                o.Deck.Insert(0, Celtic, new DeckCopyInfo { Owner = p });
                var id = o.Deck[0];
                o.Deck.RemoveAt(0);
                var milled = e.CreateCardInstance(id);
                RedirectAndReplaceRuntime.PutInGraveyard(e, o, milled);
                var toOwner = p.Graveyard.Contains(milled) && !o.Graveyard.Contains(milled);

                var plain = e.CreateCardInstance(Celtic);
                var unclaimed = plain.ControlOwner == null;
                Check("A copy owned by the other player keeps its owner when it leaves the Deck: drawn and discarded, " +
                      "or milled, it goes to its owner's GY",
                    reached && owned && discarded && toOwner && unclaimed,
                    $"reached={reached} owned={owned} discarded={discarded} milled={toOwner} unclaimed={unclaimed}");
            }
        }

        // ───────────────────────── Parasite Paracide ─────────────────────────

        static void ParacideDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Flip Summoned: into the opponent's Deck face-up; drawn in their Draw Phase: Special Summoned to their field
                // in face-up Defense Position, 1000 damage to them, their monsters become Insect; destroyed: to its owner's GY.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var para = PlaceMonster(e, p, ParasiteParacide, 0, BattlePosition.Defense, false);
                var myCeltic = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                var oppCeltic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var pDeck0 = p.Deck.Count;
                var oDeck0 = o.Deck.Count;
                var flipped = e.TryFlipSummon(p, para);
                PassAll(e);
                var idx = TriggerCopy(o.Deck);
                var info = idx >= 0 ? o.Deck.InfoAt(idx) : null;
                var inDeck = idx >= 0 && o.Deck[idx] == ParasiteParacide && info.FaceUp && info.Owner == p &&
                             SpecialCopies(o.Deck) == 1;
                Check("Parasite Paracide: FLIP — it is put face-up in the opponent's Deck (still yours)",
                    reached && flipped && !p.TryFindMonster(para, out _) && !p.Graveyard.Contains(para) &&
                    o.Deck.Count == oDeck0 + 1 && p.Deck.Count == pDeck0 && inDeck,
                    $"reached={reached} flipped={flipped} onField={p.TryFindMonster(para, out _)} " +
                    $"oDeck={o.Deck.Count}/{oDeck0 + 1} pDeck={p.Deck.Count}/{pDeck0} idx={idx} inDeck={inDeck}");

                if (idx >= 0)
                {
                    o.Deck.RemoveAt(idx);
                    o.Deck.Insert(0, ParasiteParacide); // put back on top: the same copy
                }

                var topKept = ReferenceEquals(o.Deck.InfoAt(0), info) && info != null;
                var oLp = o.LifePoints;
                var pLp = p.LifePoints;
                e.TryEndTurnSafe(p);
                PassAll(e);
                var summoned = o.MonstersOnField().FirstOrDefault(m => m.CardId == ParasiteParacide);
                Check("Parasite Paracide: drawn by the opponent — Special Summoned to their field in face-up Defense " +
                      "Position, 1000 damage to them, their monsters become Insect",
                    topKept && e.TurnPlayer == o && summoned != null && summoned.FaceUp &&
                    summoned.Position == BattlePosition.Defense && o.LifePoints == oLp - 1000 &&
                    p.LifePoints == pLp && oppCeltic.Race == "Insect" && myCeltic.Race == "Warrior" &&
                    summoned.ControlOwner == p && !o.Hand.Any(h => h.CardId == ParasiteParacide),
                    $"topKept={topKept} turn={e.TurnPlayer?.Name} summoned={summoned != null} " +
                    $"pos={summoned?.Position} oLp={o.LifePoints}/{oLp - 1000} oppRace={oppCeltic.Race} " +
                    $"myRace={myCeltic.Race} owner={summoned?.ControlOwner?.Name}");

                if (summoned != null)
                {
                    summoned.IsNegated = true;
                    var negatedOff = oppCeltic.Race == "Warrior";
                    summoned.IsNegated = false;
                    e.DestroyMonsterPublic(o, summoned);
                    Check("Parasite Paracide: no Type change while negated; destroyed, it goes to its owner's GY and " +
                          "the Type change ends",
                        negatedOff && p.Graveyard.Contains(summoned) && !o.Graveyard.Contains(summoned) &&
                        oppCeltic.Race == "Warrior",
                        $"negatedOff={negatedOff} pGy={p.Graveyard.Contains(summoned)} " +
                        $"oGy={o.Graveyard.Contains(summoned)} race={oppCeltic.Race}");
                }
            }

            {
                // Drawn by an effect while the drawer's Monster Zones are full: it stays in their hand (still yours); a
                // discard sends it to your GY. A face-up copy without the trigger, and a plain copy, do nothing.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                for (var z = 0; z < o.MonsterZones.Length; z++)
                    PlaceMonster(e, o, Celtic, z, BattlePosition.Attack, true);
                o.Deck.Insert(0, ParasiteParacide, new DeckCopyInfo { Owner = p, FaceUp = true, DrawTrigger = true });
                var oLp = o.LifePoints;
                e.Draw(o, 1);
                e.NotifyPublic();
                PassAll(e);
                var inHand = o.Hand.FirstOrDefault(h => h.CardId == ParasiteParacide);
                var stays = inHand != null && inHand.ControlOwner == p && o.LifePoints == oLp &&
                            o.MonstersOnField().Count(m => m.CardId == ParasiteParacide) == 0;
                if (inHand != null) MonsterEffects.DiscardFromHandByInstance(o, inHand);
                var toOwner = inHand != null && p.Graveyard.Contains(inHand) && !o.Graveyard.Contains(inHand);

                ClearBoard(e);
                o.Deck.Insert(0, ParasiteParacide, new DeckCopyInfo { Owner = p, FaceUp = true });
                o.Deck.Insert(1, ParasiteParacide);
                e.Draw(o, 2);
                e.NotifyPublic();
                PassAll(e);
                var noTrigger = o.LifePoints == oLp && !o.MonstersOnField().Any() &&
                                o.Hand.Count(h => h.CardId == ParasiteParacide) == 2;
                Check("Parasite Paracide: drawn with no free Monster Zone it stays in the drawer's hand (still yours; " +
                      "discarded, it goes to your GY); a face-up copy without its trigger and a plain copy do nothing",
                    reached && stays && toOwner && noTrigger,
                    $"reached={reached} stays={stays} toOwner={toOwner} noTrigger={noTrigger} " +
                    $"hand={o.Hand.Count(h => h.CardId == ParasiteParacide)} lp={o.LifePoints}/{oLp}");
            }

            {
                // Flipped by an attack that destroys it: its FLIP does nothing (ygopro STATUS_BATTLE_DESTROYED); it goes to
                // your GY.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var o = e.Opponent;
                var para = PlaceMonster(e, p, ParasiteParacide, 0, BattlePosition.Defense, false);
                var attacker = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var oDeck0 = o.Deck.Count;
                e.TryEnterBattlePhase(o);
                PassAll(e);
                e.TryAttack(o, attacker, para);
                PassAll(e);
                DrainCombat(e);
                PassAll(e);
                Check("Parasite Paracide: flipped and destroyed by an attack, it is not put in the opponent's Deck",
                    reached && p.Graveyard.Contains(para) && o.Deck.Count == oDeck0 && TriggerCopy(o.Deck) < 0,
                    $"reached={reached} gy={p.Graveyard.Contains(para)} oDeck={o.Deck.Count}/{oDeck0}");
            }
        }

        // ───────────────────────── Pharaoh's Treasure ─────────────────────────

        static void PharaohsTreasureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Activated: shuffled face-up into your Deck, not sent to the GY; drawn: sent to the GY, and the lone card
                // in your GY is added to your hand.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var skull = e.CreateCardInstance(SummonedSkull);
                p.Graveyard.Add(skull);
                var ph = PlaceSetTrap(e, p, PharaohsTreasure, 0);
                var deck0 = p.Deck.Count;
                var used = e.TryActivateSpellTrap(p, ph, fromHand: false);
                PassAll(e);
                var idx = TriggerCopy(p.Deck);
                var info = idx >= 0 ? p.Deck.InfoAt(idx) : null;
                var inDeck = idx >= 0 && p.Deck[idx] == PharaohsTreasure && info.FaceUp &&
                             (info.Owner == null || info.Owner == p) && SpecialCopies(p.Deck) == 1;
                Check("Pharaoh's Treasure: shuffled face-up into your Deck instead of going to the GY",
                    reached && used && !p.Graveyard.Any(c => c.CardId == PharaohsTreasure) &&
                    !p.TryFindSpellTrap(ph, out _) && p.Deck.Count == deck0 + 1 && inDeck,
                    $"reached={reached} used={used} gy={p.Graveyard.Count(c => c.CardId == PharaohsTreasure)} " +
                    $"deck={p.Deck.Count}/{deck0 + 1} inDeck={inDeck}");

                if (idx >= 0)
                {
                    p.Deck.RemoveAt(idx);
                    p.Deck.Insert(0, PharaohsTreasure);
                }

                e.Draw(p, 1);
                e.NotifyPublic();
                PassAll(e);
                Check("Pharaoh's Treasure: drawn — it is sent to the GY and the card in your GY is added to your hand",
                    p.Hand.Contains(skull) && !p.Graveyard.Contains(skull) &&
                    p.Graveyard.Count(c => c.CardId == PharaohsTreasure) == 1 &&
                    !p.Hand.Any(c => c.CardId == PharaohsTreasure) && !e.IsAwaitingEffectTarget,
                    $"skullHand={p.Hand.Contains(skull)} phGy={p.Graveyard.Count(c => c.CardId == PharaohsTreasure)} " +
                    $"phHand={p.Hand.Count(c => c.CardId == PharaohsTreasure)}");
            }

            {
                // Two cards in the GY: the player chooses the target.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var skull = e.CreateCardInstance(SummonedSkull);
                var celtic = e.CreateCardInstance(Celtic);
                p.Graveyard.Add(skull);
                p.Graveyard.Add(celtic);
                p.Deck.Insert(0, PharaohsTreasure, new DeckCopyInfo { Owner = p, FaceUp = true, DrawTrigger = true });
                e.Draw(p, 1);
                e.NotifyPublic();
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 2 &&
                            e.PendingActivation.LegalTargets.Contains(skull) &&
                            e.PendingActivation.LegalTargets.Contains(celtic);
                var picked = asked && e.TrySelectEffectTarget(celtic);
                PassAll(e);
                Check("Pharaoh's Treasure: with 2 cards in the GY the player targets one (Celtic Guardian) — it is added " +
                      "to the hand, the other stays",
                    reached && asked && picked && p.Hand.Contains(celtic) && p.Graveyard.Contains(skull) &&
                    p.Graveyard.Any(c => c.CardId == PharaohsTreasure) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} asked={asked} picked={picked} hand={p.Hand.Contains(celtic)} " +
                    $"skullGy={p.Graveyard.Contains(skull)}");
            }

            {
                // Drawn in your own Draw Phase with 2 cards in the GY: the pick opens at the start of the turn; once it is
                // answered the turn goes on (no soft-lock: the Battle Phase can be entered).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var skull = e.CreateCardInstance(SummonedSkull);
                var celtic = e.CreateCardInstance(Celtic);
                p.Graveyard.Add(skull);
                p.Graveyard.Add(celtic);
                p.Deck.Insert(0, PharaohsTreasure, new DeckCopyInfo { Owner = p, FaceUp = true, DrawTrigger = true });
                e.TryEndTurnSafe(p);
                PassAll(e);
                o.Hand.Clear();
                e.TryEndTurnSafe(o);
                PassAll(e);
                var asked = e.TurnPlayer == p && e.IsAwaitingEffectTarget &&
                            e.PendingActivation.LegalTargets.Count == 2 &&
                            e.PendingActivation.LegalTargets.Contains(skull);
                var picked = asked && e.TrySelectEffectTarget(skull);
                PassAll(e);
                var main = e.TurnPlayer == p && e.InMainPhase && !e.IsAwaitingEffectTarget;
                var battle = main && e.TryEnterBattlePhase(p);
                Check("Pharaoh's Treasure: drawn in your Draw Phase — you target 1 of 2 GY cards, it is added to your " +
                      "hand, and the turn goes on",
                    reached && asked && picked && p.Hand.Contains(skull) && p.Graveyard.Contains(celtic) &&
                    p.Graveyard.Any(c => c.CardId == PharaohsTreasure) && main && battle,
                    $"reached={reached} asked={asked} picked={picked} hand={p.Hand.Contains(skull)} main={main} " +
                    $"battle={battle} phase={e.Phase} turn={e.TurnPlayer?.Name}");
            }

            {
                // An empty GY: the trigger cannot activate (it needs a target); a plain copy drawn does nothing.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.Deck.Insert(0, PharaohsTreasure, new DeckCopyInfo { Owner = p, FaceUp = true, DrawTrigger = true });
                e.Draw(p, 1);
                e.NotifyPublic();
                PassAll(e);
                var empty = p.Hand.Count(c => c.CardId == PharaohsTreasure) == 1 && p.Graveyard.Count == 0;
                var skull = e.CreateCardInstance(SummonedSkull);
                p.Graveyard.Add(skull);
                p.Deck.Insert(0, PharaohsTreasure);
                e.Draw(p, 1);
                e.NotifyPublic();
                PassAll(e);
                var plain = p.Hand.Count(c => c.CardId == PharaohsTreasure) == 2 && p.Graveyard.Contains(skull);
                Check("Pharaoh's Treasure: no card in the GY — no effect; a face-down copy drawn — no effect",
                    reached && empty && plain && !e.IsAwaitingEffectTarget,
                    $"reached={reached} empty={empty} plain={plain}");
            }
        }

        // ───────────────────────── Graverobber ─────────────────────────

        static DuelEngine Robbed(CardDatabase db, DeckFile pDeck, DeckFile aDeck, int spellId, out CardInstance spell,
            out bool ok, out string detail)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            spell = e.CreateCardInstance(spellId);
            var monster = e.CreateCardInstance(Celtic);
            o.Graveyard.Add(spell);
            o.Graveyard.Add(monster);
            var gr = PlaceSetTrap(e, p, Graverobber, 0);
            var can = e.CanActivateSpellTrap(p, gr, fromHand: false);
            var used = e.TryActivateSpellTrap(p, gr, fromHand: false);
            var spellsOnly = true;
            if (e.IsAwaitingEffectTarget)
            {
                spellsOnly = e.PendingActivation.LegalTargets.All(t => t.Def != null && t.Def.IsSpell);
                e.TrySelectEffectTarget(spell);
            }

            PassAll(e);
            ok = reached && can && used && spellsOnly && p.Hand.Contains(spell) && !o.Graveyard.Contains(spell) &&
                 spell.ControlOwner == o && o.Graveyard.Contains(monster) && p.Graveyard.Any(c => c.CardId == Graverobber);
            detail = $"reached={reached} can={can} used={used} spellsOnly={spellsOnly} hand={p.Hand.Contains(spell)} " +
                     $"owner={spell.ControlOwner?.Name}";
            return e;
        }

        static void GraverobberDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Taken and used: its effect applies, then you take 2000 damage; it goes to its owner's GY.
                var e = Robbed(db, pDeck, aDeck, DianKeto, out var dk, out var ok, out var detail);
                var p = e.Player;
                var o = e.Opponent;
                Check("Graverobber: the target Spell is added to your hand, still your opponent's card", ok, detail);
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, dk, fromHand: true);
                PassAll(e);
                Check("Graverobber: using the Spell applies it (+1000 LP), then you take 2000 damage; it goes to its " +
                      "owner's GY",
                    act && p.LifePoints == lp + 1000 - 2000 && o.Graveyard.Contains(dk) && !p.Graveyard.Contains(dk) &&
                    !p.Hand.Contains(dk),
                    $"act={act} lp={p.LifePoints}/{lp - 1000} oGy={o.Graveyard.Contains(dk)} pGy={p.Graveyard.Contains(dk)}");
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Graverobber: the damage is taken once", p.LifePoints == lp - 1000,
                    $"lp={p.LifePoints}/{lp - 1000}");
            }

            {
                // Not used: at the end of the turn it goes to its owner's GY; no damage.
                var e = Robbed(db, pDeck, aDeck, Mst, out var mst, out var ok, out var detail);
                var p = e.Player;
                var o = e.Opponent;
                var lp = p.LifePoints;
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Graverobber: not used — at the end of the turn it is sent to its owner's GY, no damage",
                    ok && o.Graveyard.Contains(mst) && !p.Hand.Contains(mst) && !p.Graveyard.Contains(mst) &&
                    p.LifePoints == lp && e.TurnPlayer == o,
                    $"{detail} oGy={o.Graveyard.Contains(mst)} hand={p.Hand.Contains(mst)} lp={p.LifePoints}/{lp}");
            }

            {
                // Discarded: to its owner's GY. Set, then left on the field at the end of the turn: to its owner's GY.
                var e = Robbed(db, pDeck, aDeck, DianKeto, out var dk, out var ok, out var detail);
                var p = e.Player;
                var o = e.Opponent;
                MonsterEffects.DiscardFromHandByInstance(p, dk);
                var discarded = o.Graveyard.Contains(dk) && !p.Graveyard.Contains(dk);

                var e2 = Robbed(db, pDeck, aDeck, BlackPendant, out var bp, out var ok2, out var detail2);
                var p2 = e2.Player;
                var o2 = e2.Opponent;
                var set = e2.TrySetSpellTrap(p2, bp);
                var lp = p2.LifePoints;
                e2.TryEndTurnSafe(p2);
                PassAll(e2);
                Check("Graverobber: discarded, the Spell goes to its owner's GY; Set and kept, it is sent to its owner's " +
                      "GY at the end of the turn (no damage: it was not used)",
                    ok && discarded && ok2 && set && o2.Graveyard.Contains(bp) && !p2.TryFindSpellTrap(bp, out _) &&
                    p2.LifePoints == lp,
                    $"{detail} discarded={discarded} {detail2} set={set} oGy={o2.Graveyard.Contains(bp)} lp={p2.LifePoints}/{lp}");
            }

            {
                // No Spell in the opponent's GY (only a monster): it cannot be activated.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                e.Opponent.Graveyard.Add(e.CreateCardInstance(Celtic));
                p.Graveyard.Add(e.CreateCardInstance(Mst));
                var gr = PlaceSetTrap(e, p, Graverobber, 0);
                Check("Graverobber: no Spell in the opponent's GY (a monster there, a Spell in yours) — it cannot be " +
                      "activated",
                    reached && !e.CanActivateSpellTrap(p, gr, fromHand: false), $"reached={reached}");
            }
        }
            // ───────────────────────── review fixes: every path keeps the copy's identity ─────────────────────────

        static DeckCopyInfo ForeignFaceUp(DuelistState owner) =>
            new DeckCopyInfo { Owner = owner, FaceUp = true, DrawTrigger = true };

        static void CopyIdentityPaths(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Review v163: paths that make the instance before the removal (a Deck virus pick, a Race cost from the
                // Deck) send a foreign copy to its owner's GY, and leave nothing behind for a later, unrelated instance.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                o.Deck.Insert(0, ParasiteParacide, ForeignFaceUp(p));
                var cand = e.CreateCardInstance(ParasiteParacide);
                TextEffectRuntime.DestroyVirusDeckPick(e, o, cand);
                var virus = p.Graveyard.Contains(cand) && !o.Graveyard.Contains(cand);
                var virusLeak = e.CreateCardInstance(ParasiteParacide).ControlOwner == null;

                o.Deck.Insert(Math.Min(2, o.Deck.Count), ParasiteParacide, ForeignFaceUp(p));
                var costPick = e.CreateCardInstance(ParasiteParacide);
                var paid = TextEffectRuntime.SendRaceFromDeckAsCost(e, o, costPick);
                var cost = paid && p.Graveyard.Contains(costPick) && !o.Graveyard.Contains(costPick);
                var costLeak = e.CreateCardInstance(ParasiteParacide).ControlOwner == null;
                var deckClean = SpecialCopies(o.Deck) == 0;
                Check("A foreign copy picked from a Deck list made before the removal (Deck virus, Race cost) goes to " +
                      "its owner's GY, and no later instance inherits its owner",
                    reached && virus && virusLeak && cost && costLeak && deckClean,
                    $"reached={reached} virus={virus} virusLeak={virusLeak} paid={paid} cost={cost} " +
                    $"costLeak={costLeak} deckClean={deckClean}");
            }

            {
                // A removal nobody claimed is never handed on: the next instance (another id) ends it, and a newer
                // removal of an ordinary copy from the other Deck wins over an older foreign one.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                o.Deck.Insert(0, ParasiteParacide, ForeignFaceUp(p));
                o.Deck.RemoveAt(0);
                e.CreateCardInstance(Celtic);
                var ended = e.CreateCardInstance(ParasiteParacide).ControlOwner == null;

                o.Deck.Insert(0, ParasiteParacide, ForeignFaceUp(p));
                o.Deck.RemoveAt(0);
                p.Deck.Insert(0, ParasiteParacide);
                e.Draw(p, 1);
                var drawn = p.Hand.LastOrDefault();
                var newest = drawn != null && drawn.CardId == ParasiteParacide && drawn.ControlOwner == null;
                Check("A Deck removal no instance claimed right away is never given to a later one",
                    reached && ended && newest, $"reached={reached} ended={ended} newest={newest}");
            }

            {
                // Review v163: looking at the top cards and putting them back keeps every copy's metadata (the AI's
                // order and the player's chosen order).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var src = e.CreateCardInstance(Celtic);
                var clause = new EffectClause { Action = EffectActionKind.ReorderTopOfOwnDeck, Amount = 3 };
                o.Deck.Insert(0, ParasiteParacide, ForeignFaceUp(p));
                o.Deck.Insert(0, PharaohsTreasure, new DeckCopyInfo { Owner = o, FaceUp = true, DrawTrigger = true });
                bool a = false, b = false;
                TextEffectRuntime.ApplyClause(e, o, src, clause, null, ref a, ref b);
                var aiKept = !e.IsAwaitingEffectTarget && o.Deck[0] == PharaohsTreasure &&
                             o.Deck.InfoAt(0)?.DrawTrigger == true && o.Deck[1] == ParasiteParacide &&
                             o.Deck.InfoAt(1)?.Owner == p && o.Deck.InfoAt(1)?.FaceUp == true &&
                             SpecialCopies(o.Deck) == 2;

                p.Deck.Insert(0, PharaohsTreasure, new DeckCopyInfo { Owner = p, FaceUp = true, DrawTrigger = true });
                p.Deck.Insert(0, Celtic);
                var third = p.Deck[2];
                TextEffectRuntime.ApplyClause(e, p, src, clause, null, ref a, ref b);
                var pend = e.PendingActivation;
                var asked = pend != null && pend.AwaitingDeckOrder && pend.LegalTargets.Count == 3;
                if (asked)
                {
                    // The player puts them back bottom-to-top reversed: 3rd, Treasure, Celtic.
                    for (var i = pend.LegalTargets.Count - 1; i >= 0; i--)
                        pend.ChosenTargets.Add(pend.LegalTargets[i]);
                    TextEffectRuntime.FinishOrderedPick(e, useChosen: true);
                }

                var playerKept = asked && !e.IsAwaitingEffectTarget && p.Deck[0] == third &&
                                 p.Deck[1] == PharaohsTreasure && p.Deck.InfoAt(1)?.DrawTrigger == true &&
                                 p.Deck[2] == Celtic && SpecialCopies(p.Deck) == 1;
                Check("Looking at the top cards and putting them back keeps each copy face-up / owned / with its draw " +
                      "trigger (AI order and the player's chosen order)",
                    reached && aiKept && playerKept,
                    $"reached={reached} aiKept={aiKept} asked={asked} playerKept={playerKept} " +
                    $"top=[{string.Join(",", p.Deck.GetRange(0, Math.Min(3, p.Deck.Count)))}] special={SpecialCopies(p.Deck)}");
            }
        }
    }
}
