using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DiscardTriggers batch (v142 curriculum): Electric Snake (discarded by the opponent's card effect: draw 2),
    /// Minar (same trigger: 1000 damage), Penguin Knight (sent from the Deck by the opponent's card effect: the GY
    /// is shuffled into the Deck) and Delinquent Duo (pay 1000 LP; the opponent discards 1 random card, then 1 of
    /// their choice). Compile shapes, mini-duels through real engine entry points (Robbin' Goblin battle damage,
    /// Confiscation, Card Destruction, Drop Off, Needle Worm's Flip, Chain Destruction, the Duo itself, Magic
    /// Jammer, Imperial Order, Spell Economics, Necrovalley) and fail-closed near misses (your own Card
    /// Destruction, the hand size rule, a Gravekeeper's Servant attack cost, look-alike sentences).
    /// Runtime: <see cref="DiscardTriggersRuntime"/>.
    /// </summary>
    public static class DiscardTriggersRegressionTests
    {
        const int Snake = 11324436;
        const int Minar = 32539892;
        const int Penguin = 36039163;
        const int Duo = 44763025;

        const int Confiscation = 17375316;
        const int RobbinGoblin = 88279736;
        const int CardDestruction = 72892473;
        const int NeedleWorm = 81843628;
        const int ChainDestruction = 1248895;
        const int GravekeepersServant = 16762927;
        const int DropOff = 55773067;
        const int MagicJammer = 77414722;
        const int ImperialOrder = 61740673;
        const int SpellEconomics = 4259068;
        const int Necrovalley = 47355498;
        const int ElephantStatueOfDisaster = 12160911;
        const int FearFromTheDark = 34193084;
        const int DespairFromTheDark = 71200730;

        const int Celtic = 91152256; // Level 4 EARTH 1400/1200 Normal Monster
        const int Mst = 5318639;
        const int PotOfGreed = 55144522;
        const int FlameSwordsman = 45231177; // Fusion Monster: an Extra Deck card in the GY

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            SnakeDuels(db, pDeck, aDeck, Check);
            MinarDuels(db, pDeck, aDeck, Check);
            PenguinDuels(db, pDeck, aDeck, Check);
            DuoDuels(db, pDeck, aDeck, Check);
            DuoCostAndNegation(db, pDeck, aDeck, Check);
            AiDuoDuels(db, pDeck, aDeck, Check);
            CrossGroupGuards(db, pDeck, aDeck, Check);
            SelfSsDuels(db, pDeck, aDeck, Check);
        }

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} " +
                  $"timings={string.Join(",", prog.ClauseList.Select(c => c?.Timing.ToString()))} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var snake = CardTextEffectCompiler.Compile(db.Get(Snake));
            Check("DiscardTriggers: Electric Snake FullyCompiled discarded-by-opponent → the owner draws 2",
                snake != null && snake.FullyCompiled && snake.ClauseList.Count == 1 &&
                snake.ClauseList.Exists(c => c != null &&
                                             c.Timing == EffectTiming.DiscardedFromHandByOpponentEffect &&
                                             c.Action == EffectActionKind.Draw && c.Amount == 2 &&
                                             c.Side == EffectSide.Controller && !c.RequiresTargetChoice) &&
                CardEffectStatus.Classify(db.Get(Snake)) == CardEffectStatusKind.Implemented,
                Left(snake));

            var minar = CardTextEffectCompiler.Compile(db.Get(Minar));
            Check("DiscardTriggers: Minar FullyCompiled discarded-by-opponent → 1000 damage to the opponent",
                minar != null && minar.FullyCompiled && minar.ClauseList.Count == 1 &&
                minar.ClauseList.Exists(c => c != null &&
                                             c.Timing == EffectTiming.DiscardedFromHandByOpponentEffect &&
                                             c.Action == EffectActionKind.InflictDamageToOpponent &&
                                             c.Amount == 1000 && !c.RequiresTargetChoice) &&
                CardEffectStatus.Classify(db.Get(Minar)) == CardEffectStatusKind.Implemented,
                Left(minar));

            var penguin = CardTextEffectCompiler.Compile(db.Get(Penguin));
            Check("DiscardTriggers: Penguin Knight FullyCompiled sent-from-Deck-by-opponent → GY shuffled into the Deck",
                penguin != null && penguin.FullyCompiled && penguin.ClauseList.Count == 1 &&
                penguin.ClauseList.Exists(c => c != null &&
                                               c.Timing == EffectTiming.SentFromDeckToGyByOpponentEffect &&
                                               c.Action == EffectActionKind.ShuffleGraveyardIntoDeck &&
                                               c.Side == EffectSide.Controller && !c.RequiresTargetChoice) &&
                CardEffectStatus.Classify(db.Get(Penguin)) == CardEffectStatusKind.Implemented,
                Left(penguin));

            var duo = CardTextEffectCompiler.Compile(db.Get(Duo));
            Check("DiscardTriggers: Delinquent Duo FullyCompiled pay 1000 LP → random then chosen discard, no target",
                duo != null && duo.FullyCompiled && duo.ClauseList.Count == 1 &&
                duo.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                           c.Action == EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice &&
                                           c.PayLpAmount == 1000 && c.RequiresOpponentHandAtLeast == 1 &&
                                           !c.RequiresTargetChoice) &&
                CardEffectStatus.Classify(db.Get(Duo)) == CardEffectStatusKind.Implemented,
                Left(duo));

            Check("DiscardTriggers: the new actions map to shared resolutions",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.ShuffleGraveyardIntoDeck }) ==
                EffectResolutionKind.ReturnToDeck &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice }) ==
                EffectResolutionKind.Discard);
        }

        static CompiledCardProgram Synthetic(int id, string type, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = "DiscardTriggers near miss " + id,
                type = type,
                frameType = type.Contains("Spell") ? "spell" : "effect",
                race = type.Contains("Spell") ? "Normal" : "Thunder",
                attribute = type.Contains("Spell") ? null : "LIGHT",
                level = 3,
                atk = 800,
                def = 900,
                desc = desc
            });

        static bool HasTiming(CompiledCardProgram prog, EffectTiming t) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Timing == t);

        static bool HasAction(CompiledCardProgram prog, EffectActionKind a) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == a);

        // ── Near misses: the look-alike sentences stay out ──

        static void NearMisses(CardDatabase db, Chk Check)
        {
            // "sent from your hand … by an opponent's card effect" also covers a card destroyed in the hand — another trigger.
            var statue = CardTextEffectCompiler.Compile(db.Get(ElephantStatueOfDisaster));
            Check("DiscardTriggers near miss: Elephant Statue of Disaster (sent from the hand) is not the discard trigger",
                statue == null || !HasTiming(statue, EffectTiming.DiscardedFromHandByOpponentEffect), Left(statue));

            var fear = CardTextEffectCompiler.Compile(db.Get(FearFromTheDark));
            Check("DiscardTriggersSelfSs: Fear from the Dark compiles BOTH timings (hand discard + Deck send)",
                fear != null && fear.FullyCompiled &&
                HasTiming(fear, EffectTiming.DiscardedFromHandByOpponentEffect) &&
                HasTiming(fear, EffectTiming.SentFromDeckToGyByOpponentEffect), Left(fear));

            var despair = CardTextEffectCompiler.Compile(db.Get(DespairFromTheDark));
            Check("DiscardTriggersSelfSs: Despair from the Dark compiles BOTH timings (hand discard + Deck send)",
                despair != null && despair.FullyCompiled &&
                HasTiming(despair, EffectTiming.DiscardedFromHandByOpponentEffect) &&
                HasTiming(despair, EffectTiming.SentFromDeckToGyByOpponentEffect), Left(despair));

            var anyDiscard = Synthetic(90011001, "Effect Monster",
                "When this card is discarded from your hand to the Graveyard, draw 2 cards from your Deck.");
            Check("DiscardTriggers near miss: any discard (no opponent clause) is not the trigger",
                anyDiscard == null ||
                !(anyDiscard.FullyCompiled && HasTiming(anyDiscard, EffectTiming.DiscardedFromHandByOpponentEffect)),
                Left(anyDiscard));

            var fromField = Synthetic(90011002, "Effect Monster",
                "When this card is sent from the field to the Graveyard by an effect of a card controlled by your opponent, draw 2 cards from your Deck.");
            Check("DiscardTriggers near miss: sent from the field is not the discard trigger",
                fromField == null || !HasTiming(fromField, EffectTiming.DiscardedFromHandByOpponentEffect),
                Left(fromField));

            var deckAny = Synthetic(90011003, "Effect Monster",
                "When this card is sent from your Deck to the Graveyard, unite your Graveyard cards and your own Deck, shuffle them and form a new Deck.");
            Check("DiscardTriggers near miss: any Deck send (no opponent clause) is not the trigger",
                deckAny == null ||
                !(deckAny.FullyCompiled && HasTiming(deckAny, EffectTiming.SentFromDeckToGyByOpponentEffect)),
                Left(deckAny));

            var randomOnly = Synthetic(90011004, "Spell Card", "Pay 1000 LP; your opponent discards 1 random card.");
            Check("DiscardTriggers near miss: a random-only discard is not the Duo action",
                randomOnly == null || !HasAction(randomOnly, EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice),
                Left(randomOnly));

            var twoRandom = Synthetic(90011005, "Spell Card", "Pay 1000 LP; your opponent discards 2 random cards.");
            Check("DiscardTriggers near miss: 2 random discards are not the Duo action",
                twoRandom == null || !HasAction(twoRandom, EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice),
                Left(twoRandom));

            var longer = Synthetic(90011006, "Effect Monster",
                "When this card is discarded from your hand to the Graveyard by an effect of a card controlled by your opponent, draw 2 cards from your Deck, then discard 1 card.");
            Check("DiscardTriggers near miss: a longer resolution does not compile",
                longer == null || !longer.FullyCompiled, Left(longer));

            var prefixed = Synthetic(90011007, "Effect Monster",
                "This card cannot be Special Summoned. When this card is discarded from your hand to the Graveyard by an effect of a card controlled by your opponent, draw 2 cards from your Deck.");
            Check("DiscardTriggers near miss: an extra sentence keeps the card uncompiled (atoms are whole-card)",
                prefixed == null || !prefixed.FullyCompiled, Left(prefixed));

            var duoYours = Synthetic(90011008, "Spell Card",
                "Pay 1000 LP; you discard 1 random card, and if you have any other cards in your hand, discard 1 more card of your choice.");
            Check("DiscardTriggers near miss: a self-discard Duo wording is not the Duo action",
                duoYours == null || !HasAction(duoYours, EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice),
                Left(duoYours));
        }

        // ── Electric Snake ──

        static void SnakeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The Snake in hand has nothing to activate.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var snake = PutInHand(engine, p, Snake);
                Check("Electric Snake: nothing to activate from the hand",
                    !engine.CanActivateSpellTrap(p, snake, fromHand: true));
            }

            // Robbin' Goblin (the opponent's): battle damage discards the lone Snake → its owner draws 2.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var atk = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                atk.ClearAttackFlags();
                atk.SummonedThisTurn = false;
                var goblin = PlaceSetTrap(engine, o, RobbinGoblin, 0);
                goblin.FaceUp = true;
                p.Hand.Clear();
                var snake = PutInHand(engine, p, Snake);
                var deck = p.Deck.Count;
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var attacked = reached && engine.Phase == DuelPhase.Battle && engine.TryAttack(o, atk, null);
                DrainCombat(engine);
                Check("Electric Snake: discarded by the opponent's Robbin' Goblin → its owner draws 2",
                    attacked && p.Graveyard.Contains(snake) && p.Hand.Count == 2 && p.Deck.Count == deck - 2,
                    $"reached={reached} attacked={attacked} gy={p.Graveyard.Contains(snake)} hand={p.Hand.Count} deck={p.Deck.Count} was={deck}");
            }

            // The opponent's Card Destruction: the Snake's owner redraws 2 first, then draws 2 more.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var snake = PutInHand(engine, p, Snake);
                PutInHand(engine, p, Celtic);
                PutInHand(engine, o, Celtic);
                var cd = PutInHand(engine, o, CardDestruction);
                var deck = p.Deck.Count;
                var activated = reached && engine.TryActivateSpellTrap(o, cd, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Electric Snake: the opponent's Card Destruction triggers it (2 redrawn + 2)",
                    activated && p.Graveyard.Contains(snake) && p.Hand.Count == 4 && p.Deck.Count == deck - 4 &&
                    o.Hand.Count == 1,
                    $"reached={reached} activated={activated} gy={p.Graveyard.Contains(snake)} hand={p.Hand.Count} oppHand={o.Hand.Count}");
            }

            // Discarded by Chain Link 2: the Snake's trigger waits for the whole chain (CL1 Pot of Greed resolves
            // first, so the Snake's owner holds 2 redrawn + 2 drawn cards when the Snake activates).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var snake = PutInHand(engine, p, Snake);
                PutInHand(engine, p, Celtic);
                var pog = engine.CreateCardInstance(PotOfGreed);
                pog.FaceUp = true;
                p.SpellTrapZones[0].Occupant = pog;
                var cd = engine.CreateCardInstance(CardDestruction);
                cd.FaceUp = true;
                o.SpellTrapZones[0].Occupant = cd;
                var l1 = engine.Chain.AddLink(p, pog, SpellSpeed.Speed1, EffectClass.Ignition,
                    CardLocation.SpellTrapZone, false, false, "test");
                var l2 = engine.Chain.AddLink(o, cd, SpellSpeed.Speed2, EffectClass.Quick,
                    CardLocation.SpellTrapZone, false, false, "test");
                var handAtTrigger = -1;
                void OnLog(string msg)
                {
                    if (handAtTrigger < 0 && msg != null && msg.StartsWith("Electric Snake was discarded"))
                        handAtTrigger = p.Hand.Count;
                }

                engine.OnLog += OnLog;
                var deck = p.Deck.Count;
                engine.Chain.StartResolution();
                engine.ResolveChainStackForActivation();
                engine.OnLog -= OnLog;
                Check("Electric Snake: discarded at Chain Link 2, it activates after the chain (after CL1 Pot of Greed)",
                    l1 != null && l2 != null && p.Graveyard.Contains(snake) && handAtTrigger == 4 &&
                    p.Hand.Count == 6 && p.Deck.Count == deck - 6 && !engine.Chain.HasLinks,
                    $"links={l1 != null}/{l2 != null} handAtTrigger={handAtTrigger} hand={p.Hand.Count} drawn={deck - p.Deck.Count}");
            }

            // Your own Card Destruction: discarded by your own effect → no trigger.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var snake = PutInHand(engine, p, Snake);
                PutInHand(engine, p, Celtic);
                var cd = PutInHand(engine, p, CardDestruction);
                var activated = engine.TryActivateSpellTrap(p, cd, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Electric Snake near miss: your own Card Destruction does not trigger it",
                    activated && p.Graveyard.Contains(snake) && p.Hand.Count == 2,
                    $"activated={activated} gy={p.Graveyard.Contains(snake)} hand={p.Hand.Count}");
            }

            // Drop Off (the player's): the opponent's just-drawn Snake is discarded → the opponent draws 2.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var snake = PutInHand(engine, o, Snake);
                TextEffectRuntime.RememberNormalDrawCard(o, snake);
                var trap = PlaceSetTrap(engine, p, DropOff, 0);
                trap.FaceUp = true;
                var resolved = TextEffectRuntime.TryResolveOpponentNormalDrawEffect(engine, p, trap);
                Check("Electric Snake: discarded by Drop Off → its owner draws 2",
                    resolved && o.Graveyard.Contains(snake) && o.Hand.Count == 2,
                    $"resolved={resolved} gy={o.Graveyard.Contains(snake)} hand={o.Hand.Count}");
            }

            // The hand size rule is not a card effect: the AI's End Phase discard of its Snake draws nothing.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var o = engine.Opponent;
                o.Hand.Clear();
                var snake = PutInHand(engine, o, Snake);
                for (var i = 0; i < 6; i++) PutInHand(engine, o, Celtic);
                engine.EndTurn(o);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Electric Snake near miss: the hand size rule discard (no card effect) does not trigger it",
                    reached && o.Graveyard.Contains(snake) && o.Hand.Count == 6 && o.Hand.All(h => h.CardId == Celtic),
                    $"reached={reached} gy={o.Graveyard.Contains(snake)} hand={o.Hand.Count} turn={engine.TurnPlayer?.Name}");
            }
        }

        // ── Minar ──

        static void MinarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Confiscation discards Minar (the lone card: no prompt) → 1000 damage to the Confiscation player.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                o.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var minar = PutInHand(engine, o, Minar);
                var conf = PutInHand(engine, p, Confiscation);
                var opened = engine.TryActivateSpellTrap(p, conf, fromHand: true);
                if (engine.IsAwaitingEffectTarget)
                    engine.TrySelectEffectTarget(minar);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Minar: discarded by Confiscation → 1000 damage to the Confiscation player",
                    opened && o.Graveyard.Contains(minar) && p.LifePoints == 6000 && o.LifePoints == 8000,
                    $"opened={opened} gy={o.Graveyard.Contains(minar)} pLP={p.LifePoints} oLP={o.LifePoints} pending={engine.IsAwaitingEffectTarget}");
            }

            // Your own Card Destruction: no damage.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                o.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var minar = PutInHand(engine, p, Minar);
                var cd = PutInHand(engine, p, CardDestruction);
                var activated = engine.TryActivateSpellTrap(p, cd, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Minar near miss: your own Card Destruction inflicts no damage",
                    activated && p.Graveyard.Contains(minar) && o.LifePoints == 8000 && p.LifePoints == 8000,
                    $"activated={activated} gy={p.Graveyard.Contains(minar)} oLP={o.LifePoints}");
            }

            // The opponent's Card Destruction discards Minar and a Snake together: both trigger.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                o.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var minar = PutInHand(engine, p, Minar);
                var snake = PutInHand(engine, p, Snake);
                var cd = PutInHand(engine, o, CardDestruction);
                var activated = reached && engine.TryActivateSpellTrap(o, cd, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Minar + Electric Snake: one Card Destruction of the opponent triggers both (1000 damage, 2 + 2 cards)",
                    activated && p.Graveyard.Contains(minar) && p.Graveyard.Contains(snake) &&
                    o.LifePoints == 7000 && p.Hand.Count == 4,
                    $"reached={reached} activated={activated} oLP={o.LifePoints} hand={p.Hand.Count}");
            }
        }

        // ── Penguin Knight ──

        static void PenguinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The opponent's Needle Worm mills the Knight → the whole GY is shuffled into the Deck.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Graveyard.Clear();
                p.Graveyard.Add(engine.CreateCardInstance(Mst));
                p.Deck.Insert(0, Celtic);
                p.Deck.Insert(0, Penguin);
                var deck = p.Deck.Count;
                var worm = PlaceMonster(engine, o, NeedleWorm, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, o, worm);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Penguin Knight: milled by the opponent's Needle Worm → GY (MST + 5 milled) shuffled into the Deck",
                    p.Graveyard.Count == 0 && p.Deck.Count == deck + 1 && p.Deck.Contains(Penguin) &&
                    p.Deck.Contains(Mst),
                    $"gy={p.Graveyard.Count} deck={p.Deck.Count} was={deck}");
            }

            // An Extra Deck monster in the GY goes back to the Extra Deck.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Graveyard.Clear();
                p.Graveyard.Add(engine.CreateCardInstance(FlameSwordsman));
                p.Deck.Insert(0, Penguin);
                var deck = p.Deck.Count;
                var extra = p.ExtraDeck.Count;
                var worm = PlaceMonster(engine, o, NeedleWorm, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, o, worm);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Penguin Knight: a Fusion Monster in the GY returns to the Extra Deck, the rest to the Deck",
                    p.Graveyard.Count == 0 && p.ExtraDeck.Count == extra + 1 && p.Deck.Count == deck,
                    $"gy={p.Graveyard.Count} deck={p.Deck.Count} was={deck} extra={p.ExtraDeck.Count} was={extra}");
            }

            // Necrovalley: no card leaves the GY.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var valley = engine.CreateCardInstance(Necrovalley);
                valley.FaceUp = true;
                o.FieldSpellZone.Occupant = valley;
                p.Graveyard.Clear();
                p.Deck.Insert(0, Penguin);
                var deck = p.Deck.Count;
                var worm = PlaceMonster(engine, o, NeedleWorm, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, o, worm);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Penguin Knight: under Necrovalley no card leaves the GY",
                    p.Graveyard.Count == 5 && p.Deck.Count == deck - 5 &&
                    p.Graveyard.Exists(g => g.CardId == Penguin),
                    $"gy={p.Graveyard.Count} deck={p.Deck.Count} was={deck}");
            }

            // The opponent's Chain Destruction destroys the Deck copy → trigger; the field copy stays.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Graveyard.Clear();
                o.Hand.Clear();
                o.Deck.RemoveAll(id => id == Penguin);
                var trap = PlaceSetTrap(engine, p, ChainDestruction, 2);
                trap.SetThisTurn = false;
                var onField = engine.CreateCardInstance(Penguin);
                var inHand = PutInHand(engine, o, Penguin);
                o.Deck.Insert(0, Penguin);
                var deck = o.Deck.Count;
                var summoned = engine.SpecialSummonToField(o, onField, BattlePosition.Attack, true);
                var activated = summoned && engine.TryActivateSpellTrap(p, trap, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(onField);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                // Deck copy destroyed (−1) → its trigger returns it and the destroyed hand copy (+2).
                Check("Penguin Knight: destroyed in the Deck by the opponent's Chain Destruction → GY shuffled into the Deck",
                    activated && o.TryFindMonster(onField, out _) && !o.Hand.Contains(inHand) &&
                    o.Graveyard.Count == 0 && o.Deck.Count == deck + 1 &&
                    o.Deck.Count(id => id == Penguin) == 2,
                    $"summoned={summoned} activated={activated} gy={o.Graveyard.Count} deck={o.Deck.Count} was={deck}");
            }

            // Near miss: a Gravekeeper's Servant attack cost mills the Knight — a cost, no trigger.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var servant = PlaceSetTrap(engine, p, GravekeepersServant, 0);
                servant.FaceUp = true;
                var atk = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                atk.ClearAttackFlags();
                atk.SummonedThisTurn = false;
                o.Graveyard.Clear();
                o.Deck.Insert(0, Penguin);
                var deck = o.Deck.Count;
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var attacked = reached && engine.Phase == DuelPhase.Battle && engine.TryAttack(o, atk, null);
                DrainCombat(engine);
                Check("Penguin Knight near miss: a Gravekeeper's Servant attack cost does not trigger it",
                    attacked && o.Graveyard.Count == 1 && o.Graveyard[0].CardId == Penguin &&
                    o.Deck.Count == deck - 1,
                    $"reached={reached} attacked={attacked} gy={o.Graveyard.Count} deck={o.Deck.Count} was={deck}");
            }

            // The player's Needle Worm mills the AI's Knight: only the AI's GY goes back, the player's Deck is untouched.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Graveyard.Clear();
                p.Graveyard.Clear();
                p.Graveyard.Add(engine.CreateCardInstance(Mst));
                var deck = p.Deck.Count;
                o.Deck.Insert(0, Penguin);
                var oDeck = o.Deck.Count;
                var worm = PlaceMonster(engine, p, NeedleWorm, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, p, worm);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Penguin Knight: the player's Needle Worm mills the AI's Knight → the AI's own GY is shuffled back",
                    o.Graveyard.Count == 0 && o.Deck.Count == oDeck && p.Deck.Count == deck && p.Graveyard.Count == 1,
                    $"oGy={o.Graveyard.Count} oDeck={o.Deck.Count} was={oDeck} pDeck={p.Deck.Count} was={deck} pGy={p.Graveyard.Count}");
            }
        }

        // ── Delinquent Duo ──

        static void DuoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Opponent with no cards: cannot activate. Not enough LP: cannot activate.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                p.Hand.Clear();
                var duo = PutInHand(engine, p, Duo);
                Check("Delinquent Duo: the opponent's hand must hold a card to activate",
                    !engine.CanActivateSpellTrap(p, duo, fromHand: true));
                PutInHand(engine, o, Celtic);
                var can = engine.CanActivateSpellTrap(p, duo, fromHand: true);
                p.LifePoints = 900;
                Check("Delinquent Duo: legal with a card in the opponent's hand; not with 900 LP",
                    can && !engine.CanActivateSpellTrap(p, duo, fromHand: true), $"can={can}");
            }

            // The player's Duo vs the AI: a random discard, then the AI's own choice (it discards its Snake → draws 2).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var a = PutInHand(engine, o, Celtic);
                var snake = PutInHand(engine, o, Snake);
                var b = PutInHand(engine, o, Celtic);
                var duo = PutInHand(engine, p, Duo);
                engine.Rng.QueuePick(0); // the random discard: the first Celtic
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Delinquent Duo: pays 1000 LP; the AI discards 1 at random, then chooses its Snake (draws 2)",
                    activated && !engine.IsAwaitingEffectTarget && p.LifePoints == 7000 &&
                    o.Graveyard.Contains(a) && o.Graveyard.Contains(snake) && o.Hand.Contains(b) &&
                    o.Hand.Count == 3 && p.Graveyard.Contains(duo),
                    $"activated={activated} LP={p.LifePoints} hand={o.Hand.Count} aGy={o.Graveyard.Contains(a)} snakeGy={o.Graveyard.Contains(snake)} duoGy={p.Graveyard.Contains(duo)}");
            }

            // Only 1 card: the random discard only.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var lone = PutInHand(engine, o, Celtic);
                var duo = PutInHand(engine, p, Duo);
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Delinquent Duo: with 1 card the opponent discards only that card",
                    activated && o.Hand.Count == 0 && o.Graveyard.Contains(lone) && !engine.IsAwaitingEffectTarget,
                    $"activated={activated} hand={o.Hand.Count}");
            }

            // The AI's Duo vs the human: the Snake goes at random, the human then chooses from the 2 others, and
            // only after that choice does the Snake draw 2.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var snake = PutInHand(engine, p, Snake);
                var a = PutInHand(engine, p, Celtic);
                var b = PutInHand(engine, p, Mst);
                var duo = PutInHand(engine, o, Duo);
                engine.Rng.QueuePick(0); // the random discard: the Snake
                var activated = reached && engine.TryActivateSpellTrap(o, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                var pending = engine.PendingActivation;
                var prompt = pending != null && pending.AwaitingForcedDiscardChoice &&
                             pending.Controller == p && pending.Card == duo &&
                             pending.TargetKind == EffectTargetKind.DiscardFromHand &&
                             pending.LegalTargets.Count == 2 &&
                             pending.LegalTargets.Contains(a) && pending.LegalTargets.Contains(b) &&
                             p.Graveyard.Contains(snake) && p.Hand.Count == 2;
                Check("Delinquent Duo: the human victim chooses from the 2 cards left (the Snake's draw waits)",
                    activated && prompt && o.LifePoints == 7000,
                    $"reached={reached} activated={activated} pending={pending != null} legal={pending?.LegalTargets.Count} hand={p.Hand.Count} oLP={o.LifePoints}");
                var illegal = prompt && engine.TrySelectEffectTarget(snake);
                var picked = prompt && engine.TrySelectEffectTarget(b);
                Check("Delinquent Duo: the chosen card is discarded, then the Snake draws 2",
                    !illegal && picked && !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(b) &&
                    p.Hand.Contains(a) && p.Hand.Count == 3,
                    $"illegal={illegal} picked={picked} pending={engine.IsAwaitingEffectTarget} hand={p.Hand.Count} bGy={p.Graveyard.Contains(b)}");
            }

            // The human cannot refuse: Cancel still discards a card.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PutInHand(engine, p, Celtic);
                PutInHand(engine, p, Celtic);
                PutInHand(engine, p, Mst);
                var duo = PutInHand(engine, o, Duo);
                var activated = reached && engine.TryActivateSpellTrap(o, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                var waited = engine.IsAwaitingEffectTarget &&
                             engine.PendingActivation.AwaitingForcedDiscardChoice;
                engine.CancelEffectTargeting();
                Check("Delinquent Duo: Cancel on the human's choice still discards a card",
                    activated && waited && !engine.IsAwaitingEffectTarget && p.Hand.Count == 1 &&
                    p.Graveyard.Count == 2,
                    $"reached={reached} activated={activated} waited={waited} hand={p.Hand.Count} gy={p.Graveyard.Count}");
            }

            // The player's Duo hits the AI's Minar at random: 1000 damage to the Duo player.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                o.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var minar = PutInHand(engine, o, Minar);
                var c1 = PutInHand(engine, o, Celtic);
                var duo = PutInHand(engine, p, Duo);
                engine.Rng.QueuePick(0); // the random discard: Minar
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Delinquent Duo → Minar: 1000 LP cost and 1000 damage to the Duo player; the Celtic goes too",
                    activated && o.Graveyard.Contains(minar) && o.Graveyard.Contains(c1) && o.Hand.Count == 0 &&
                    p.LifePoints == 6000 && o.LifePoints == 8000,
                    $"activated={activated} pLP={p.LifePoints} oLP={o.LifePoints} hand={o.Hand.Count}");
            }
        }

        // ── Delinquent Duo: cost on activation, negation ──

        static void DuoCostAndNegation(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI's Set Magic Jammer negates the Duo: its 1000 LP were paid on activation; the Duo discards
            // nothing (Magic Jammer's own discard is its cost).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 4);
                PutInHand(engine, o, Celtic);
                PutInHand(engine, o, Celtic);
                var duo = PutInHand(engine, p, Duo);
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                DrainCombat(engine);
                Check("Delinquent Duo: negated by Magic Jammer — the 1000 LP cost stays paid, the AI keeps its other card",
                    activated && o.Graveyard.Contains(jammer) && p.Graveyard.Contains(duo) && p.LifePoints == 7000 &&
                    o.Hand.Count == 1 && !engine.IsAwaitingEffectTarget && !engine.Chain.HasLinks,
                    $"activated={activated} jammerGy={o.Graveyard.Contains(jammer)} duoGy={p.Graveyard.Contains(duo)} LP={p.LifePoints} oHand={o.Hand.Count}");
            }

            // Imperial Order negates the Duo's effect as it resolves: the LP are paid, nothing is discarded.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var order = engine.CreateCardInstance(ImperialOrder);
                order.FaceUp = true;
                o.SpellTrapZones[0].Occupant = order;
                PutInHand(engine, o, Celtic);
                PutInHand(engine, o, Snake);
                var duo = PutInHand(engine, p, Duo);
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Delinquent Duo: under Imperial Order the LP are paid and nothing is discarded",
                    activated && p.LifePoints == 7000 && o.Hand.Count == 2 && o.Graveyard.Count == 0,
                    $"activated={activated} LP={p.LifePoints} oHand={o.Hand.Count} oGy={o.Graveyard.Count}");
            }

            // Spell Economics: no LP is paid, the effect resolves.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var eco = engine.CreateCardInstance(SpellEconomics);
                eco.FaceUp = true;
                p.SpellTrapZones[1].Occupant = eco;
                PutInHand(engine, o, Celtic);
                var duo = PutInHand(engine, p, Duo);
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Delinquent Duo: with Spell Economics no LP is paid and the opponent discards",
                    activated && p.LifePoints == 8000 && o.Hand.Count == 0 && o.Graveyard.Count == 1,
                    $"activated={activated} LP={p.LifePoints} oHand={o.Hand.Count}");
            }
        }

        // ── Delinquent Duo: the AI casts it (SimpleAi) ──

        /// <summary>
        /// Steps SimpleAi's turn with no presentation delay. The player passes every chain window; while the
        /// player's forced Duo choice is open, <paramref name="onChoice"/> runs before each step.
        /// Returns false if the routine has not finished within <paramref name="maxSteps"/>.
        /// </summary>
        static bool RunAiTurn(DuelEngine e, int maxSteps, Action<PendingActivation> onChoice)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    var pa = e.PendingActivation;
                    if (pa != null && pa.AwaitingForcedDiscardChoice && pa.Controller == e.Player)
                        onChoice?.Invoke(pa);
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static void AiDuoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI casts Delinquent Duo in its Main Phase: it pays 1000, the player's Celtic goes at random, and the
            // AI's turn waits (no step moves it on) until the player has chosen the second card; then it ends the turn.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var a = PutInHand(engine, p, Celtic);
                var b = PutInHand(engine, p, Celtic);
                var c = PutInHand(engine, p, Mst);
                var duo = PutInHand(engine, o, Duo);
                var wants = DiscardTriggersRuntime.AiWantsToActivateFromHand(engine, o, duo);
                engine.Rng.QueuePick(0); // the random discard: the first Celtic
                var prompts = 0;
                var promptOk = false;
                var heldWhileOpen = true;
                var picked = false;
                var done = RunAiTurn(engine, 400, pa =>
                {
                    prompts++;
                    if (prompts == 1)
                        promptOk = pa.Card == duo && pa.TargetKind == EffectTargetKind.DiscardFromHand &&
                                   pa.LegalTargets.Count == 2 && pa.LegalTargets.Contains(b) &&
                                   pa.LegalTargets.Contains(c) && p.Graveyard.Contains(a) &&
                                   o.LifePoints == 7000;
                    heldWhileOpen &= engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1;
                    if (prompts < 4) return; // let the AI routine step while the choice is open
                    picked = engine.TrySelectEffectTarget(c);
                });
                Check("Delinquent Duo (AI): SimpleAi casts it; the player's forced choice holds the AI's turn until made",
                    reached && wants && promptOk && prompts >= 4 && heldWhileOpen && picked,
                    $"reached={reached} wants={wants} prompts={prompts} promptOk={promptOk} held={heldWhileOpen} picked={picked} oLP={o.LifePoints}");
                Check("Delinquent Duo (AI): the chosen card goes too, the Duo is in the AI's GY, the AI's turn ends",
                    done && p.Graveyard.Contains(a) && p.Graveyard.Contains(c) && !p.Graveyard.Contains(b) &&
                    o.Graveyard.Contains(duo) && o.LifePoints == 7000 && engine.TurnPlayer == p &&
                    !engine.IsAwaitingEffectTarget,
                    $"done={done} aGy={p.Graveyard.Contains(a)} cGy={p.Graveyard.Contains(c)} bGy={p.Graveyard.Contains(b)} duoGy={o.Graveyard.Contains(duo)} oLP={o.LifePoints} turn={engine.TurnPlayer?.Name}");
            }

            // The AI keeps the Duo when it takes just 1 card, or when paying would leave it under 2000 LP.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var duo = PutInHand(engine, o, Duo);
                PutInHand(engine, p, Celtic);
                o.LifePoints = 8000;
                var oneCard = DiscardTriggersRuntime.AiWantsToActivateFromHand(engine, o, duo);
                PutInHand(engine, p, Mst);
                o.LifePoints = 3000;
                var at3000 = DiscardTriggersRuntime.AiWantsToActivateFromHand(engine, o, duo);
                o.LifePoints = 2900;
                var at2900 = DiscardTriggersRuntime.AiWantsToActivateFromHand(engine, o, duo);
                var done = RunAiTurn(engine, 400, null);
                Check("Delinquent Duo (AI): not cast for 1 card or below 3000 LP (1000 cost + 2000 kept)",
                    reached && !oneCard && at3000 && !at2900 && done && o.Hand.Contains(duo) &&
                    p.Graveyard.Count == 0 && o.LifePoints == 2900,
                    $"reached={reached} oneCard={oneCard} at3000={at3000} at2900={at2900} done={done} duoInHand={o.Hand.Contains(duo)} pGy={p.Graveyard.Count} oLP={o.LifePoints}");
            }
        }

        // ── Cross-group guards (v142 batch) ──

        const int HinoKaguTsuchi = 75745607;

        /// <summary>The player's Battle Phase of a turn after the first.</summary>
        static bool ReachPlayerBattleTurn(DuelEngine engine)
        {
            for (var t = 0; t < 8 && !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != engine.Player) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1)
                engine.TryEnterBattlePhase(engine.Player);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Player;
        }

        /// <summary>
        /// Discards and mills added by OTHER groups of the v142 batch must tell <see cref="DiscardTriggersRuntime"/> who
        /// caused them, or Electric Snake / Minar / Penguin Knight silently miss them. Idle while the other group's card
        /// is not compiled in this tree; it runs once that group is integrated.
        /// </summary>
        static void CrossGroupGuards(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            HinoGuard(db, pDeck, aDeck, Check);
            BanisherGuard(db, pDeck, aDeck, Check);
        }

        const int BanisherOfTheLight = 61528025;

        /// <summary>
        /// Banisher of the Light (RedirectAndReplace): a card banished instead of being sent to the GY was never
        /// discarded / sent to the GY, so Penguin Knight and Electric Snake do not activate (both are EVENT_TO_GRAVE).
        /// </summary>
        static void BanisherGuard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var banisherProg = CardTextEffectCompiler.Compile(db.Get(BanisherOfTheLight));
            if (banisherProg == null || !banisherProg.FullyCompiled)
            {
                Check("DiscardTriggers cross-group guard: Banisher of the Light is not compiled here (guard idle)", true,
                    Left(banisherProg));
                return;
            }

            // The player's Needle Worm mills the AI's Knight under the player's Banisher: all 5 are banished.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                o.Graveyard.Clear();
                o.Graveyard.Add(engine.CreateCardInstance(Mst));
                o.Deck.Insert(0, Penguin);
                var oDeck = o.Deck.Count;
                var banished = o.Banished.Count;
                engine.NotifyPublic();
                var worm = PlaceMonster(engine, p, NeedleWorm, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, p, worm);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("DiscardTriggers × RedirectAndReplace: a Penguin Knight banished by Banisher of the Light instead " +
                      "of being milled does not activate",
                    o.Banished.Count == banished + 5 && o.Banished.Exists(b => b.CardId == Penguin) &&
                    o.Graveyard.Count == 1 && o.Deck.Count == oDeck - 5,
                    $"banished={banished}->{o.Banished.Count} gy={o.Graveyard.Count} deck={oDeck}->{o.Deck.Count}. " +
                    "SendTopOfDeckToGy must note the Knight (DiscardTriggersRuntime.NoteSentFromDeck) only after the " +
                    "Banisher check put it in the GY.");
            }

            // The player's Duo discards the AI's lone Snake under the player's Banisher: banished, no draw.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var snake = PutInHand(engine, o, Snake);
                var duo = PutInHand(engine, p, Duo);
                var deck = o.Deck.Count;
                engine.NotifyPublic();
                var activated = engine.TryActivateSpellTrap(p, duo, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("DiscardTriggers × RedirectAndReplace: an Electric Snake banished by Banisher of the Light " +
                      "instead of being discarded draws nothing",
                    activated && o.Banished.Contains(snake) && !o.Graveyard.Contains(snake) && o.Hand.Count == 0 &&
                    o.Deck.Count == deck,
                    $"activated={activated} banished={o.Banished.Contains(snake)} gy={o.Graveyard.Contains(snake)} " +
                    $"hand={o.Hand.Count} deck={deck}->{o.Deck.Count}");
            }
        }

        // ── Fear from the Dark / Despair from the Dark (DiscardTriggersSelfSs, v173) ──

        static void SelfSsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The opponent's Confiscation discards the lone Fear from the Dark → it Special Summons itself face-up
            // Attack from the GY (ygopro: SpecialSummon(pos 0, tp, tp, false, false, POS_FACEUP)).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var fear = PutInHand(engine, o, FearFromTheDark);
                var conf = PutInHand(engine, p, Confiscation);
                var activated = engine.TryActivateSpellTrap(p, conf, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(fear);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Fear from the Dark: discarded from the hand by the opponent's Confiscation → Special Summoned",
                    activated && !o.Hand.Contains(fear) && !o.Graveyard.Contains(fear) &&
                    o.MonstersOnField().Any(m => m != null && m.CardId == FearFromTheDark),
                    $"activated={activated} hand={o.Hand.Count} gy={o.Graveyard.Contains(fear)} " +
                    $"field={string.Join(",", o.MonstersOnField().Select(m => m?.CardId))}");
            }

            // The opponent's Needle Worm mills the Despair from the Dark from the Deck → Special Summoned.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Graveyard.Clear();
                o.Deck.Insert(0, DespairFromTheDark);
                var worm = PlaceMonster(engine, p, NeedleWorm, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, p, worm);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Despair from the Dark: milled from the Deck by the opponent's Needle Worm → Special Summoned",
                    o.MonstersOnField().Any(m => m != null && m.CardId == DespairFromTheDark),
                    $"field={string.Join(",", o.MonstersOnField().Select(m => m?.CardId))}");
            }

            // Necrovalley: the trigger still fires, but the card cannot leave the GY — nothing is Summoned.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var valley = engine.CreateCardInstance(Necrovalley);
                valley.FaceUp = true;
                o.FieldSpellZone.Occupant = valley;
                var fear = PutInHand(engine, o, FearFromTheDark);
                var conf = PutInHand(engine, p, Confiscation);
                var activated = engine.TryActivateSpellTrap(p, conf, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(fear);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Fear from the Dark: under Necrovalley it stays in the GY (no Special Summon)",
                    activated && o.Graveyard.Contains(fear) && !o.Hand.Contains(fear) &&
                    !o.MonstersOnField().Any(m => m != null && m.CardId == FearFromTheDark),
                    $"activated={activated} gy={o.Graveyard.Contains(fear)} hand={o.Hand.Count} " +
                    $"field={string.Join(",", o.MonstersOnField().Select(m => m?.CardId))}");
            }

            // A card destroyed in the hand (Elephant Statue of Disaster) is a different trigger — it does not SS.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var statue = PutInHand(engine, o, ElephantStatueOfDisaster);
                var conf = PutInHand(engine, p, Confiscation);
                var activated = engine.TryActivateSpellTrap(p, conf, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(statue);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Elephant Statue of Disaster (near miss): discarded from the hand it does not Special Summon",
                    activated && o.Graveyard.Contains(statue) &&
                    !o.MonstersOnField().Any(m => m != null && m.CardId == ElephantStatueOfDisaster),
                    $"activated={activated} gy={o.Graveyard.Contains(statue)} " +
                    $"field={string.Join(",", o.MonstersOnField().Select(m => m?.CardId))}");
            }
        }

        static void HinoGuard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Hino-Kagu-Tsuchi (SpiritsAndMisc): "they discard all cards in their hand during the next Draw Phase,
            // before they draw" (c75745607: SendtoGrave(REASON_DISCARD|REASON_EFFECT) by an effect Hino's controller
            // registered, so rp = the victim's opponent). The AI's Snake draws 2 and its Minar burns the player.
            var hinoProg = CardTextEffectCompiler.Compile(db.Get(HinoKaguTsuchi));
            if (hinoProg == null || !hinoProg.FullyCompiled)
            {
                Check("DiscardTriggers cross-group guard: Hino-Kagu-Tsuchi is not compiled here (guard idle)", true,
                    Left(hinoProg));
                return;
            }

            var engine = Fresh(db, pDeck, aDeck);
            var reached = ReachPlayerBattleTurn(engine);
            ClearBoard(engine);
            var p = engine.Player;
            var o = engine.Opponent;
            p.LifePoints = 8000;
            o.LifePoints = 8000;
            var hino = PlaceMonster(engine, p, HinoKaguTsuchi, 0, BattlePosition.Attack, true);
            hino.ClearAttackFlags();
            hino.SummonedThisTurn = false;
            o.Hand.Clear();
            var snake = PutInHand(engine, o, Snake);
            var minar = PutInHand(engine, o, Minar);
            var celtic = PutInHand(engine, o, Celtic);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var attacked = reached && engine.Phase == DuelPhase.Battle && engine.TryAttack(p, hino, null);
            DrainCombat(engine);
            var damaged = o.LifePoints == 8000 - hino.CurrentAtk;
            var deck = o.Deck.Count;
            for (var i = 0; i < 4 && engine.TurnPlayer == p && !engine.GameOver; i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(p);
            }

            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            Check("DiscardTriggers × SpiritsAndMisc: Hino-Kagu-Tsuchi's Draw Phase hand discard triggers the AI's " +
                  "Electric Snake (draws 2) and Minar (1000 to the player)",
                attacked && damaged && engine.TurnPlayer == o && o.Graveyard.Contains(snake) &&
                o.Graveyard.Contains(minar) && o.Graveyard.Contains(celtic) && o.Hand.Count == 3 &&
                o.Deck.Count == deck - 3 && p.LifePoints == 7000,
                $"reached={reached} attacked={attacked} damaged={damaged} turn={engine.TurnPlayer?.Name} " +
                $"gy={o.Graveyard.Contains(snake)}/{o.Graveyard.Contains(minar)}/{o.Graveyard.Contains(celtic)} " +
                $"hand={o.Hand.Count} drawn={deck - o.Deck.Count} pLP={p.LifePoints}. If the hand was discarded but " +
                "nothing triggered: wrap the Hino hand discard (SpiritsAndMiscRuntime.OnDrawPhaseStart) in " +
                "DiscardTriggersRuntime.BeginScope/EndScope and call DiscardTriggersRuntime.NoteDiscard(engine, side, " +
                "discarded, engine.OpponentOf(side)) for each card it discards.");
        }
    }
}
