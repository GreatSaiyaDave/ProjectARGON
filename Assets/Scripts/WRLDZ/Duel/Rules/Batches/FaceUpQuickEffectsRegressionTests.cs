using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// FaceUpQuickEffects batch (v160): Quick Effects (Spell Speed 2) of face-up Continuous Traps — Type Zero Magic
    /// Crusher (discard 1 Spell → 500 damage), Skull Lair (banish N monsters from your GY → destroy a face-up
    /// Level-N monster, once per Chain), Soul Demolition (pay 500 → each player targets a monster in their opponent's
    /// GY; both banished) and The Dragon's Bead (chained to a Trap Card that targets a face-up Dragon: discard 1 →
    /// negate it and destroy it). Compile shapes, mini-duels through the real engine entry points (open state on
    /// either turn, the opponent's open-state / attack windows, chain responses, the player's cost picks and Cancel,
    /// resolution after the card left the field, negation, the AI) and fail-closed near misses.
    /// Runtime: <see cref="FaceUpQuickEffectsRuntime"/>.
    /// </summary>
    public static class FaceUpQuickEffectsRegressionTests
    {
        const int TypeZero = 21237481;
        const int SkullLair = 6733059;

        const int PotOfGreed = 55144522;
        const int MonsterReborn = 83764718;
        const int Celtic = 91152256;      // Level 4 1400
        const int Kuriboh = 40640057;     // Level 1 300
        const int BattleOx = 5053103;     // Level 4 1700
        const int SkullServant = 32274490; // Level 1 300
        const int SummonedSkull = 70781052; // Level 6 2500
        const int MagicJammer = 77414722;
        const int SevenTools = 3819470;
        const int Mst = 5318639;
        const int RoyalDecree = 51452091;
        const int Necrovalley = 47355498;
        const int Waboku = 12607053;
        const int SoulDemolition = 76297408;
        const int DragonsBead = 92408984;
        const int BlueEyes = 89631139;    // Level 8 Dragon 3000
        const int SpellbindingCircle = 18807108; // Continuous Trap: target 1 opponent's monster
        const int RaigekiBreak = 4178474; // Normal Trap: discard 1, target 1 card on the field; destroy it

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("FaceUpQuickEffects: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            TypeZeroDuels(db, pDeck, aDeck, Check);
            TypeZeroChainDuels(db, pDeck, aDeck, Check);
            TypeZeroAiDuels(db, pDeck, aDeck, Check);
            SkullLairDuels(db, pDeck, aDeck, Check);
            SkullLairWindowDuels(db, pDeck, aDeck, Check);
            SoulDemolitionDuels(db, pDeck, aDeck, Check);
            DragonsBeadDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Shape(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram Synthetic(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Near-miss face-up quick " + id, type = type, race = race, desc = text, frameType = "trap"
            });

        static CardInstance PlaceFaceUp(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance InGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static DuelEngine Board(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman)
        {
            var engine = Fresh(db, pDeck, aDeck);
            engine.HumanVsHuman = humanVsHuman;
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return engine;
        }

        static string State(DuelEngine engine) =>
            $"chain={engine.Chain.Count} pr={engine.PendingResponse?.Timing} pa={engine.PendingActivation?.Prompt} " +
            $"turn={engine.TurnPlayer?.Name} phase={engine.Phase}";

        // ───────────────────────────── compile ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var tz = CardTextEffectCompiler.Compile(db.Get(TypeZero));
            var c = tz?.ClauseList.Count == 1 ? tz.ClauseList[0] : null;
            Check("Type Zero Magic Crusher compiles to one face-up Quick Effect: discard 1 Spell → 500 to the opponent",
                tz != null && tz.FullyCompiled && c != null && c.Timing == EffectTiming.FaceUpQuickEffect &&
                c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500 && c.RequiresDiscardCost &&
                c.DiscardCostAttribute == FaceUpQuickEffectsRuntime.DiscardSpellCard && !c.OncePerChain &&
                !tz.HasTiming(EffectTiming.Activate) &&
                CardEffectStatus.Classify(db.Get(TypeZero)) == CardEffectStatusKind.Implemented,
                Shape(tz));

            var sl = CardTextEffectCompiler.Compile(db.Get(SkullLair));
            var s = sl?.ClauseList.Count == 1 ? sl.ClauseList[0] : null;
            Check("Skull Lair compiles to one face-up Quick Effect, once per Chain: banish N → destroy a face-up Level-N " +
                  "monster (no target)",
                sl != null && sl.FullyCompiled && s != null && s.Timing == EffectTiming.FaceUpQuickEffect &&
                s.Action == EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost && s.OncePerChain &&
                s.ChoiceDoesNotTarget && !s.RequiresTargetChoice && !sl.HasTiming(EffectTiming.Activate) &&
                EffectVocabulary.ResolutionOf(s) == EffectResolutionKind.Destroy &&
                CardEffectStatus.Classify(db.Get(SkullLair)) == CardEffectStatusKind.Implemented,
                Shape(sl));

            var sd = CardTextEffectCompiler.Compile(db.Get(SoulDemolition));
            var d = sd?.ClauseList.Count == 1 ? sd.ClauseList[0] : null;
            Check("Soul Demolition compiles to one face-up Quick Effect: a face-up Fiend, pay 500 → each player targets " +
                  "a monster in their opponent's GY; both banished",
                sd != null && sd.FullyCompiled && d != null && d.Timing == EffectTiming.FaceUpQuickEffect &&
                d.Action == EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish && d.PayLpAmount == 500 &&
                d.RaceFilter == "Fiend" && d.RequiresTargetChoice && string.IsNullOrEmpty(d.ActivationCondition) &&
                !sd.HasTiming(EffectTiming.Activate) &&
                CardEffectStatus.Classify(db.Get(SoulDemolition)) == CardEffectStatusKind.Implemented,
                Shape(sd));

            var db2 = CardTextEffectCompiler.Compile(db.Get(DragonsBead));
            var b = db2?.ClauseList.Count == 1 ? db2.ClauseList[0] : null;
            Check("The Dragon's Bead compiles to one face-up chain answer: discard 1 → negate a Trap that targets a " +
                  "face-up Dragon and destroy it",
                db2 != null && db2.FullyCompiled && b != null && b.Timing == EffectTiming.FaceUpQuickEffect &&
                b.Action == EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt && b.RaceFilter == "Dragon" &&
                b.RequiresDiscardCost && b.DiscardCostAttribute == "*" && b.ChainResponseOnly &&
                !db2.HasTiming(EffectTiming.Activate) &&
                CardEffectStatus.Classify(db.Get(DragonsBead)) == CardEffectStatusKind.Implemented &&
                FaceUpQuickEffectsRuntime.NegatesTrapEffects(db2) && !FaceUpQuickEffectsRuntime.NegatesTrapEffects(tz),
                Shape(db2));
        }

        static void NearMisses(Chk Check)
        {
            var misses = new (string type, string race, string text)[]
            {
                // The same sentences on a card that does not stay face-up (no Spell & Trap Zone to use them from).
                ("Trap Card", "Normal",
                    "Discard 1 Spell Card from your hand to inflict 500 points of damage to your opponent's Life Points."),
                ("Spell Card", "Normal",
                    "Remove from play any number of Monster Cards in your Graveyard to destroy 1 face-up monster on the " +
                    "field whose Level is equal to the number of the cards you removed from play."),
                // Longer sentences of the same family.
                ("Trap Card", "Continuous",
                    "Discard 1 Spell Card from your hand to inflict 500 points of damage to your opponent's Life Points, " +
                    "then draw 1 card."),
                ("Trap Card", "Continuous",
                    "Discard 1 Spell Card from your hand to inflict 500 points of damage to your opponent's Life Points. " +
                    "Then destroy this card."),
                ("Trap Card", "Continuous",
                    "Remove from play any number of Monster Cards in your Graveyard to destroy 1 face-up monster on the " +
                    "field whose Level is equal to the number of the cards you removed from play, and draw 1 card."),
                ("Trap Card", "Continuous",
                    "Remove from play any number of Monster Cards in your Graveyard to destroy 1 face-up monster your " +
                    "opponent controls whose Level is equal to the number of the cards you removed from play."),
                ("Trap Card", "Normal",
                    "You can only activate this card's effect when you have a Fiend-Type monster on your side of the " +
                    "field. Pay 500 Life Points to use this effect. Both players select 1 Monster Card from their " +
                    "opponent's Graveyard. Remove the selected cards from play."),
                ("Trap Card", "Continuous",
                    "Pay 500 Life Points to use this effect. Both players select 1 Monster Card from their opponent's " +
                    "Graveyard. Remove the selected cards from play."),
                ("Trap Card", "Continuous",
                    "You can only activate this card's effect when you have a Fiend-Type monster on your side of the " +
                    "field. Pay 500 Life Points to use this effect. Both players select 1 Monster Card from their " +
                    "opponent's Graveyard. Remove the selected cards from play. Draw 1 card."),
                ("Trap Card", "Normal",
                    "Discard 1 card from your hand. Negate the effect of a Trap Card that targets 1 face-up Dragon-Type " +
                    "monster and destroy the Trap Card."),
                ("Trap Card", "Continuous",
                    "Discard 1 card from your hand. Negate the effect of a Trap Card that targets 1 face-up Dragon-Type " +
                    "monster and destroy the Trap Card, then draw 1 card."),
                ("Trap Card", "Continuous",
                    "Negate the effect of a Trap Card that targets 1 face-up Dragon-Type monster and destroy the Trap Card."),
            };
            var i = 0;
            foreach (var (type, race, text) in misses)
            {
                var prog = Synthetic(990760 + i++, type, race, text);
                Check($"near miss ({type} {race}) \"{text}\" is not FullyCompiled",
                    prog != null && !prog.FullyCompiled, Shape(prog));
            }
        }

        // ───────────────────────────── Type Zero ─────────────────────────────

        static void TypeZeroDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Open state, own Main Phase, one Spell in hand (a lone cost resolves with no prompt): 500 to the AI, which
            // has nothing to chain; the card stays face-up and the chain is empty.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                var pot = PutInHand(engine, p, PotOfGreed);
                PutInHand(engine, p, Celtic);
                var oLp = o.LifePoints;
                var can = engine.CanActivateSpellTrap(p, tz, fromHand: false);
                var fired = can && engine.TryActivateSpellTrap(p, tz, fromHand: false);
                Check("Type Zero: open state → discard the only Spell, 500 to the opponent; the card stays face-up",
                    fired && o.LifePoints == oLp - 500 && p.Graveyard.Contains(pot) && !p.Hand.Contains(pot) &&
                    p.Hand.Count == 1 && tz.FaceUp && p.TryFindSpellTrap(tz, out _) && !engine.Chain.HasLinks &&
                    engine.PendingActivation == null && engine.PendingResponse == null,
                    $"can={can} fired={fired} oLp={o.LifePoints} (was {oLp}) {State(engine)}");

                // Again (no per-turn limit) with no Spell left: refused.
                var again = engine.CanActivateSpellTrap(p, tz, fromHand: false);
                Check("Type Zero: no Spell Card in the hand → cannot be activated (a monster is not a Spell)",
                    !again && !engine.TryActivateSpellTrap(p, tz, fromHand: false) && o.LifePoints == oLp - 500,
                    State(engine));
            }

            // Two Spells: the player picks the discard (only Spells offered), then the damage resolves.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 1);
                var pot = PutInHand(engine, p, PotOfGreed);
                var reborn = PutInHand(engine, p, MonsterReborn);
                var celtic = PutInHand(engine, p, Celtic);
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, tz, fromHand: false);
                var pa = engine.PendingActivation;
                var offered = pa != null && pa.Controller == p && pa.LegalTargets.Count == 2 &&
                              pa.LegalTargets.Contains(pot) && pa.LegalTargets.Contains(reborn) &&
                              !pa.LegalTargets.Contains(celtic);
                var notYet = o.LifePoints == oLp && p.Hand.Count == 3;
                var picked = offered && engine.TrySelectEffectTarget(reborn);
                Check("Type Zero: with 2 Spells the player picks the discard (Spells only); then 500",
                    fired && offered && notYet && picked && p.Graveyard.Contains(reborn) && p.Hand.Contains(pot) &&
                    o.LifePoints == oLp - 500 && !engine.Chain.HasLinks && engine.PendingActivation == null,
                    $"fired={fired} offered={offered} notYet={notYet} picked={picked} oLp={o.LifePoints} {State(engine)}");
            }

            // Cancel at the discard pick: the activation never happened (nothing discarded, no link).
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                PutInHand(engine, p, PotOfGreed);
                PutInHand(engine, p, MonsterReborn);
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, tz, fromHand: false);
                var open = engine.PendingActivation != null;
                var cancelled = engine.CancelEffectTargeting();
                Check("Type Zero: Cancel at the discard pick undoes the activation (no discard, no link, no damage)",
                    fired && open && cancelled && engine.PendingActivation == null && !engine.Chain.HasLinks &&
                    p.Hand.Count == 2 && p.Graveyard.Count == 0 && o.LifePoints == oLp && tz.FaceUp &&
                    engine.CanActivateSpellTrap(p, tz, fromHand: false),
                    $"fired={fired} open={open} cancelled={cancelled} {State(engine)}");
            }

            // Its own activation unresolved / a Set copy: the Set card is activated as a card (placement only).
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var set = PlaceSetTrap(engine, p, TypeZero, 2);
                PutInHand(engine, p, PotOfGreed);
                var oLp = o.LifePoints;
                var activated = engine.TryActivateSpellTrap(p, set, fromHand: false);
                DrainCombat(engine);
                Check("Type Zero: activating the Set card only places it face-up (no damage, no discard)",
                    activated && set.FaceUp && p.TryFindSpellTrap(set, out _) && o.LifePoints == oLp &&
                    p.Hand.Count == 1 && !engine.Chain.HasLinks,
                    $"activated={activated} faceUp={set.FaceUp} oLp={o.LifePoints} {State(engine)}");
                var then = engine.TryActivateSpellTrap(p, set, fromHand: false);
                Check("Type Zero: once face-up, its effect is usable the same turn",
                    then && o.LifePoints == oLp - 500 && p.Hand.Count == 0, State(engine));
            }

            // Its own activation still on the chain (the opponent chained Waboku to it): the effect cannot be used yet.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var set = PlaceSetTrap(engine, p, TypeZero, 2);
                PutInHand(engine, p, PotOfGreed);
                var waboku = PlaceSetTrap(engine, o, Waboku, 0);
                var ownWaboku = PlaceSetTrap(engine, p, Waboku, 3); // gives the player a window on Chain Link 2
                var activated = engine.TryActivateSpellTrap(p, set, fromHand: false);
                var chained = engine.PendingResponse?.Responder == o &&
                              engine.TryActivateSpellTrap(o, waboku, fromHand: false);
                var pr = engine.PendingResponse;
                var notYet = pr != null && pr.Responder == p && pr.LegalCards.Contains(ownWaboku) &&
                             !pr.LegalCards.Contains(set) && engine.Chain.Count == 2;
                var refused = !FaceUpQuickEffectsRuntime.CanActivateQuick(engine, p, set,
                    CompiledEffectCache.GetOrCompile(set.Def), out var why);
                DrainCombat(engine);
                Check("Type Zero: while its own activation is an unresolved Chain Link, its effect cannot be activated",
                    activated && chained && notYet && refused && set.FaceUp && p.Hand.Count == 1,
                    $"activated={activated} chained={chained} notYet={notYet} refused={refused} ({why}) {State(engine)}");
            }

            // Royal Decree (the opponent's, face-up): Trap effects are negated → the effect cannot be activated.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                PlaceFaceUp(engine, o, RoyalDecree, 0);
                PutInHand(engine, p, PotOfGreed);
                FieldSpellEffects.RefreshBoard(engine);
                Check("Type Zero: negated by Royal Decree → its effect cannot be activated",
                    !engine.CanActivateSpellTrap(p, tz, fromHand: false), State(engine));
            }

            // The opponent's turn: the player is offered it in the opponent's open-state window (start of their Main
            // Phase 1) and in the open state itself.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                PutInHand(engine, p, PotOfGreed);
                PutInHand(engine, p, MonsterReborn);
                engine.TryEndTurnSafe(p);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.OpponentOpenState && pr.Responder == p &&
                             pr.LegalCards.Contains(tz);
                var oLp = o.LifePoints;
                var fired = window && engine.TryActivateSpellTrap(p, tz, fromHand: false);
                var pick = engine.PendingActivation?.LegalTargets.FirstOrDefault();
                var picked = pick != null && engine.TrySelectEffectTarget(pick);
                DrainCombat(engine);
                Check("Type Zero: in the opponent's open-state window (their Main Phase 1) → 500; play goes on",
                    window && fired && picked && o.LifePoints == oLp - 500 && engine.TurnPlayer == o &&
                    engine.Phase == DuelPhase.Main1 && engine.PendingResponse == null && !engine.Chain.HasLinks,
                    $"window={window} fired={fired} picked={picked} oLp={o.LifePoints} (was {oLp}) {State(engine)}");

                var openState = engine.CanActivateSpellTrap(p, tz, fromHand: false) &&
                                engine.TryActivateSpellTrap(p, tz, fromHand: false);
                Check("Type Zero: on the opponent's turn in the open state too (Spell Speed 2)",
                    openState && o.LifePoints == oLp - 1000 && p.Hand.Count == 0 && !engine.Chain.HasLinks,
                    $"open={openState} oLp={o.LifePoints} {State(engine)}");
            }
        }

        static void TypeZeroChainDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human vs human: the opponent's chain window has the cost already paid and no damage yet. Seven Tools of the
            // Bandit and Magic Jammer cannot answer it (an effect, not a card activation); Waboku can chain to it.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                var pot = PutInHand(engine, p, PotOfGreed);
                var tools = PlaceSetTrap(engine, o, SevenTools, 2);
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 3);
                var waboku = PlaceSetTrap(engine, o, Waboku, 4);
                PutInHand(engine, o, Celtic);
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, tz, fromHand: false);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o;
                var link = engine.Chain.Last;
                var seated = engine.Chain.Count == 1 && link?.Card == tz &&
                             link.EffectKey == FaceUpQuickEffectsRuntime.QuickEffectLinkKey &&
                             link.Speed == SpellSpeed.Speed2;
                var costPaid = p.Graveyard.Contains(pot) && o.LifePoints == oLp;
                var noCounter = window && !pr.LegalCards.Contains(tools) && !pr.LegalCards.Contains(jammer);
                var wabokuOffered = window && pr.LegalCards.Contains(waboku);
                var chained = wabokuOffered && engine.TryActivateSpellTrap(o, waboku, fromHand: false);
                DrainCombat(engine);
                Check("Type Zero: a Spell Speed 2 effect link — the opponent's Counter Traps cannot answer it, Waboku " +
                      "chains; then 500",
                    fired && window && seated && costPaid && noCounter && wabokuOffered && chained &&
                    o.LifePoints == oLp - 500 && !engine.Chain.HasLinks && !tools.FaceUp && !jammer.FaceUp &&
                    o.Graveyard.Contains(waboku),
                    $"fired={fired} window={window} seated={seated} cost={costPaid} noCounter={noCounter} " +
                    $"waboku={wabokuOffered} chained={chained} oLp={o.LifePoints} (was {oLp}) {State(engine)}");
            }

            // As a chain response: the opponent activates Pot of Greed; the player chains Type Zero (link 2), which
            // resolves first; Pot of Greed still draws 2.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                PutInHand(engine, p, MonsterReborn);
                o.Hand.Clear();
                var pot = PutInHand(engine, o, PotOfGreed);
                PlaceSetTrap(engine, o, Waboku, 4); // keeps the opponent's window open on Chain Link 2
                var oLp = o.LifePoints;
                var cast = engine.TurnPlayer == o && engine.TryActivateSpellTrap(o, pot, fromHand: true);
                var pr = engine.PendingResponse;
                var offered = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == p &&
                              pr.LegalCards.Contains(tz);
                var chained = offered && engine.TryActivateSpellTrap(p, tz, fromHand: false);
                var twoLinks = engine.Chain.Count == 2 && engine.Chain.Last?.Card == tz &&
                               engine.PendingResponse?.Responder == o && o.LifePoints == oLp;
                DrainCombat(engine);
                Check("Type Zero: chained to the opponent's Pot of Greed (Chain Link 2) → 500, then Pot of Greed draws 2",
                    cast && offered && chained && twoLinks && o.LifePoints == oLp - 500 && o.Hand.Count == 2 &&
                    o.Graveyard.Contains(pot) && !engine.Chain.HasLinks,
                    $"cast={cast} offered={offered} chained={chained} two={twoLinks} oLp={o.LifePoints} " +
                    $"oHand={o.Hand.Count} {State(engine)}");
            }

            // The card leaves the field before its effect resolves (Mystical Space Typhoon chained to it): no damage
            // (Lua IsRelateToEffect); the discarded Spell stays discarded.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var tz = PlaceFaceUp(engine, p, TypeZero, 0);
                var pot = PutInHand(engine, p, PotOfGreed);
                var mst = PlaceSetTrap(engine, o, Mst, 2);
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, tz, fromHand: false);
                var offered = engine.PendingResponse?.LegalCards.Contains(mst) == true;
                var chained = offered && engine.TryActivateSpellTrap(o, mst, fromHand: false);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(tz))
                    engine.TrySelectEffectTarget(tz);
                DrainCombat(engine);
                Check("Type Zero: destroyed by a chained MST before it resolves → no damage; the cost stays paid",
                    fired && offered && chained && p.Graveyard.Contains(tz) && p.Graveyard.Contains(pot) &&
                    o.LifePoints == oLp && !engine.Chain.HasLinks,
                    $"fired={fired} offered={offered} chained={chained} tzGy={p.Graveyard.Contains(tz)} " +
                    $"oLp={o.LifePoints} (was {oLp}) {State(engine)}");
            }
        }

        static void TypeZeroAiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI answers the player's Pot of Greed with a lethal Type Zero.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUp(engine, o, TypeZero, 0);
                PutInHand(engine, o, MonsterReborn);
                p.LifePoints = 500;
                var pot = PutInHand(engine, p, PotOfGreed);
                var cast = engine.TryActivateSpellTrap(p, pot, fromHand: true);
                DrainCombat(engine);
                Check("Type Zero AI: chains its lethal Type Zero to the player's Pot of Greed",
                    cast && p.LifePoints == 0 && engine.GameOver && o.Hand.Count == 0,
                    $"cast={cast} pLp={p.LifePoints} over={engine.GameOver} {State(engine)}");
            }

            // Not lethal, Main Phase 1, a Spell it can use: the AI holds it.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                var tz = PlaceFaceUp(engine, o, TypeZero, 0);
                o.Hand.Clear();
                PutInHand(engine, o, PotOfGreed);
                var holds = engine.TurnPlayer == o && !FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, o, tz);
                p.LifePoints = 400;
                var lethal = FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, o, tz);
                Check("Type Zero AI: holds it in Main Phase 1 unless lethal",
                    holds && lethal, State(engine));

                // A whole AI turn with a lethal Type Zero: it fires it (the player has nothing to chain). It plays Pot of
                // Greed first (the draws are random), so it also holds a Monster Reborn it cannot use (both GYs empty).
                PutInHand(engine, o, MonsterReborn);
                SimpleAi.TakeTurn(engine);
                Check("Type Zero AI: a whole AI turn fires the lethal Type Zero",
                    p.LifePoints == 0 && engine.GameOver, $"pLp={p.LifePoints} {State(engine)}");
            }

            // A whole AI turn with a Set Type Zero, Pot of Greed and a Monster Reborn it cannot use (both GYs empty): it
            // activates the Set card, keeps Pot of Greed for itself, and discards the dead Monster Reborn for 500.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                var tz = PlaceSetTrap(engine, o, TypeZero, 0);
                o.Hand.Clear();
                var reborn = PutInHand(engine, o, MonsterReborn);
                var pLp = p.LifePoints;
                var turn = engine.TurnPlayer == o;
                SimpleAi.TakeTurn(engine);
                Check("Type Zero AI: activates its Set copy, then in Main Phase 2 discards a Spell it cannot use → 500",
                    turn && tz.FaceUp && o.TryFindSpellTrap(tz, out _) && o.Graveyard.Contains(reborn) &&
                    p.LifePoints == pLp - 500,
                    $"turn={turn} faceUp={tz.FaceUp} rebornGy={o.Graveyard.Contains(reborn)} pLp={p.LifePoints} " +
                    $"(was {pLp}) {State(engine)}");
            }
        }

        // ───────────────────────────── Skull Lair ─────────────────────────────

        static void SkullLairDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // One possible N (only Level 1 is within the 3 GY monsters): the player picks the 1 monster to banish;
            // Kuriboh (the only face-up Level 1) is destroyed; the Level 4 stays.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                var kuri = PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(engine, o, BattleOx, 1, BattlePosition.Attack, true);
                var g1 = InGy(engine, p, Celtic);
                var g2 = InGy(engine, p, SkullServant);
                var g3 = InGy(engine, p, SummonedSkull);
                var fired = engine.TryActivateSpellTrap(p, lair, fromHand: false);
                var pa = engine.PendingActivation;
                var offered = pa != null && pa.LegalTargets.Count == 3 && pa.TargetKind == EffectTargetKind.BanishFromYourGy;
                var picked = offered && engine.TrySelectEffectTarget(g2);
                DrainCombat(engine);
                Check("Skull Lair: N = 1 → the player banishes 1 GY monster; the face-up Level 1 is destroyed",
                    fired && offered && picked && p.Banished.Contains(g2) && p.Graveyard.Count == 2 &&
                    o.Graveyard.Contains(kuri) && o.TryFindMonster(ox, out _) && lair.FaceUp && !engine.Chain.HasLinks,
                    $"fired={fired} offered={offered} picked={picked} kuriGy={o.Graveyard.Contains(kuri)} " +
                    $"banished={p.Banished.Count} {State(engine)}");
            }

            // Two possible N: the player chooses N with a number token, then exactly N monsters are banished (all of
            // them, no further choice) and the face-up Level 4 is destroyed.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                var kuri = PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(engine, o, BattleOx, 1, BattlePosition.Attack, true);
                // A face-down Level 6 is not a choice (face-up only).
                PlaceMonster(engine, o, SummonedSkull, 2, BattlePosition.Defense, false);
                for (var i = 0; i < 4; i++) InGy(engine, p, Celtic);
                var fired = engine.TryActivateSpellTrap(p, lair, fromHand: false);
                var pa = engine.PendingActivation;
                var tokens = pa?.LegalTargets.ToList();
                var twoChoices = tokens != null && tokens.Count == 2 && tokens.All(t => t.CardId == 0) &&
                                 tokens.Any(t => t.Def.level == 1) && tokens.Any(t => t.Def.level == 4);
                var four = tokens?.FirstOrDefault(t => t.Def.level == 4);
                var picked = four != null && engine.TrySelectEffectTarget(four);
                DrainCombat(engine);
                Check("Skull Lair: N chosen with a number token (1 or 4) → 4 banished, the face-up Level 4 destroyed",
                    fired && twoChoices && picked && p.Banished.Count == 4 && p.Graveyard.Count == 0 &&
                    o.Graveyard.Contains(ox) && o.TryFindMonster(kuri, out _) && !engine.Chain.HasLinks,
                    $"fired={fired} tokens={tokens?.Count} picked={picked} banished={p.Banished.Count} " +
                    $"oxGy={o.Graveyard.Contains(ox)} {State(engine)}");
            }

            // Cancel at the banish pick: nothing is banished and there is no link.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                InGy(engine, p, Celtic);
                InGy(engine, p, Celtic);
                var fired = engine.TryActivateSpellTrap(p, lair, fromHand: false);
                var open = engine.PendingActivation != null;
                var cancelled = engine.CancelEffectTargeting();
                Check("Skull Lair: Cancel at the banish pick undoes the activation (nothing banished, no link)",
                    fired && open && cancelled && p.Banished.Count == 0 && p.Graveyard.Count == 2 &&
                    !engine.Chain.HasLinks && engine.PendingActivation == null && o.MonsterCount == 1,
                    $"fired={fired} open={open} cancelled={cancelled} {State(engine)}");
            }

            // No face-up monster whose Level the GY can match (face-down, or too high) → cannot be activated.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, o, BattleOx, 1, BattlePosition.Attack, true);
                InGy(engine, p, Celtic);
                InGy(engine, p, Celtic);
                Check("Skull Lair: only a face-down Level 1 and a Level 4 with 2 GY monsters → cannot be activated",
                    !engine.CanActivateSpellTrap(p, lair, fromHand: false), State(engine));
            }

            // Necrovalley: monsters in the GY cannot be banished → cannot be activated.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                InGy(engine, p, Celtic);
                var can = engine.CanActivateSpellTrap(p, lair, fromHand: false);
                var nv = engine.CreateCardInstance(Necrovalley);
                nv.FaceUp = true;
                o.FieldSpellZone.Occupant = nv;
                FieldSpellEffects.RefreshBoard(engine);
                Check("Skull Lair: legal, then Necrovalley stops the GY banish → cannot be activated",
                    can && !engine.CanActivateSpellTrap(p, lair, fromHand: false), State(engine));
            }

            // Two face-up monsters of that Level: the player selects one at resolution (the chain waits); Cancel does
            // not skip it.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                var kuri = PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, p, SkullServant, 0, BattlePosition.Attack, true);
                InGy(engine, p, Celtic);
                var fired = engine.TryActivateSpellTrap(p, lair, fromHand: false);
                var pa = engine.PendingActivation;
                var atResolution = pa != null && pa.HoldsChainResolution && pa.LegalTargets.Count == 2 &&
                                   pa.LegalTargets.Contains(kuri) && pa.LegalTargets.Contains(servant) &&
                                   p.Banished.Count == 1;
                engine.CancelEffectTargeting();
                var stillWaiting = engine.PendingActivation != null && engine.PendingActivation.HoldsChainResolution;
                var picked = stillWaiting && engine.TrySelectEffectTarget(kuri);
                DrainCombat(engine);
                Check("Skull Lair: two face-up Level 1 monsters → the player selects one at resolution (mandatory)",
                    fired && atResolution && stillWaiting && picked && o.Graveyard.Contains(kuri) &&
                    p.TryFindMonster(servant, out _) && !engine.Chain.HasLinks && engine.PendingActivation == null,
                    $"fired={fired} atRes={atResolution} waiting={stillWaiting} picked={picked} {State(engine)}");
            }

            // Once per Chain: the activator gets no second activation while its first link is on the chain.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                var lair2 = PlaceFaceUp(engine, p, SkullLair, 1);
                PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, SkullServant, 1, BattlePosition.Attack, true);
                InGy(engine, p, Celtic);
                InGy(engine, p, Celtic);
                var waboku = PlaceSetTrap(engine, o, Waboku, 0);
                var fired = engine.TryActivateSpellTrap(p, lair, fromHand: false);
                if (engine.PendingActivation != null)
                    engine.TrySelectEffectTarget(engine.PendingActivation.LegalTargets[0]);
                var chained = engine.PendingResponse?.Responder == o &&
                              engine.TryActivateSpellTrap(o, waboku, fromHand: false);
                var pr = engine.PendingResponse;
                var sameRefused = !FaceUpQuickEffectsRuntime.CanActivateQuick(engine, p, lair,
                    CompiledEffectCache.GetOrCompile(lair.Def), out var why);
                var otherCopy = pr != null && pr.Responder == p && pr.LegalCards.Contains(lair2) &&
                                !pr.LegalCards.Contains(lair);
                Check("Skull Lair: once per Chain — the same copy is refused on its own chain; another copy is offered",
                    fired && chained && sameRefused && otherCopy,
                    $"fired={fired} chained={chained} refused={sameRefused} ({why}) other={otherCopy} {State(engine)}");
                if (engine.PendingResponse != null) engine.PassResponse();
                DrainCombat(engine);
            }
        }

        static void SkullLairWindowDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI answers the player's attack: Skull Lair destroys the attacker (Level 4, 4 GY monsters); the attack
            // ends and no damage is dealt.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, o, SkullLair, 0);
                for (var i = 0; i < 4; i++) InGy(engine, o, Celtic);
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                engine.TryEndTurnSafe(o);
                DrainCombat(engine);
                var ox = PlaceMonster(engine, p, BattleOx, 0, BattlePosition.Attack, true);
                var bp = engine.TurnPlayer == p && engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var oLp = o.LifePoints;
                var attacked = bp && engine.TryAttack(p, ox, null);
                DrainCombat(engine);
                Check("Skull Lair AI: destroys the player's Level 4 attacker in the attack window (4 banished); no damage",
                    bp && attacked && p.Graveyard.Contains(ox) && o.LifePoints == oLp && o.Banished.Count == 4 &&
                    o.Graveyard.Count == 0 && lair.FaceUp && !engine.Chain.HasLinks && !engine.HasDeclaredAttack,
                    $"bp={bp} attacked={attacked} oxGy={p.Graveyard.Contains(ox)} oLp={o.LifePoints} (was {oLp}) " +
                    $"banished={o.Banished.Count} {State(engine)}");
            }

            // The AI's resolution picks the player's monster over its own of the same Level.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                var lair = PlaceFaceUp(engine, o, SkullLair, 0);
                var mine = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(engine, p, BattleOx, 0, BattlePosition.Attack, true);
                for (var i = 0; i < 4; i++) InGy(engine, o, Celtic);
                var wants = FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, o, lair);
                var fired = wants && engine.TryActivateSpellTrap(o, lair, fromHand: false);
                DrainCombat(engine);
                Check("Skull Lair AI: Main Phase 1 → destroys the player's 1700 Level 4, not its own Level 4",
                    wants && fired && p.Graveyard.Contains(theirs) && o.TryFindMonster(mine, out _) &&
                    o.Banished.Count == 4,
                    $"wants={wants} fired={fired} {State(engine)}");
            }

            // The player answers the opponent's Normal Summon (the summon window) with Skull Lair.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var lair = PlaceFaceUp(engine, p, SkullLair, 0);
                for (var i = 0; i < 4; i++) InGy(engine, p, Celtic);
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                o.Hand.Clear();
                var ox = PutInHand(engine, o, BattleOx);
                var summoned = engine.TurnPlayer == o && engine.TryNormalSummon(o, ox, asSet: false);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.MonsterSummoned && pr.Responder == p &&
                             pr.LegalCards.Contains(lair);
                var fired = window && engine.TryActivateSpellTrap(p, lair, fromHand: false);
                DrainCombat(engine);
                Check("Skull Lair: offered in the opponent's summon window → destroys the summoned Level 4",
                    summoned && window && fired && o.Graveyard.Contains(ox) && p.Banished.Count == 4 &&
                    engine.PendingResponse == null && !engine.Chain.HasLinks,
                    $"summoned={summoned} window={window} fired={fired} {State(engine)}");
            }
        }

        // ───────────────────────────── Soul Demolition ─────────────────────────────

        static void SoulDemolitionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's own turn: the player (turn player) targets in the AI's GY, the AI targets the player's best;
            // 500 LP paid; both banished.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var sd = PlaceFaceUp(engine, p, SoulDemolition, 0);
                PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var mineWeak = InGy(engine, p, Kuriboh);
                var mineBest = InGy(engine, p, BlueEyes);
                var theirs1 = InGy(engine, o, Celtic);
                var theirs2 = InGy(engine, o, BattleOx);
                var pLp = p.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, sd, fromHand: false);
                var pa = engine.PendingActivation;
                var pickOpen = pa != null && pa.Controller == p && pa.LegalTargets.Count == 2 &&
                               pa.LegalTargets.Contains(theirs1) && pa.LegalTargets.Contains(theirs2) &&
                               p.LifePoints == pLp;
                var picked = pickOpen && engine.TrySelectEffectTarget(theirs1);
                DrainCombat(engine);
                Check("Soul Demolition: the player targets in the opponent's GY, the AI takes the player's best; " +
                      "500 LP; both banished",
                    fired && pickOpen && picked && p.LifePoints == pLp - 500 && o.Banished.Contains(theirs1) &&
                    o.Graveyard.Contains(theirs2) && p.Banished.Contains(mineBest) && p.Graveyard.Contains(mineWeak) &&
                    sd.FaceUp && !engine.Chain.HasLinks && engine.PendingActivation == null,
                    $"fired={fired} pick={pickOpen} picked={picked} pLp={p.LifePoints} (was {pLp}) " +
                    $"oBan={o.Banished.Count} pBan={p.Banished.Count} {State(engine)}");
            }

            // Conditions: no face-up Fiend (a face-down one does not count), or an empty GY on either side.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var sd = PlaceFaceUp(engine, p, SoulDemolition, 0);
                var fiend = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Defense, false);
                InGy(engine, p, Kuriboh);
                InGy(engine, o, Celtic);
                var faceDown = !engine.CanActivateSpellTrap(p, sd, fromHand: false);
                fiend.FaceUp = true;
                fiend.Position = BattlePosition.Attack;
                var faceUp = engine.CanActivateSpellTrap(p, sd, fromHand: false);
                o.Graveyard.Clear();
                var emptyGy = !engine.CanActivateSpellTrap(p, sd, fromHand: false);
                Check("Soul Demolition: needs a face-up Fiend you control and a monster in each GY",
                    faceDown && faceUp && emptyGy, State(engine));
            }

            // Cancel at the activator's own pick: no LP paid, no link.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var sd = PlaceFaceUp(engine, p, SoulDemolition, 0);
                PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                InGy(engine, p, Kuriboh);
                InGy(engine, o, Celtic);
                InGy(engine, o, BattleOx);
                var pLp = p.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, sd, fromHand: false);
                var cancelled = engine.PendingActivation != null && engine.CancelEffectTargeting();
                Check("Soul Demolition: Cancel at the player's own pick undoes the activation (no LP, no link)",
                    fired && cancelled && p.LifePoints == pLp && !engine.Chain.HasLinks &&
                    engine.PendingActivation == null && o.Graveyard.Count == 2 && p.Graveyard.Count == 1,
                    $"fired={fired} cancelled={cancelled} {State(engine)}");
            }

            // The AI activates it on its turn: it targets first (the player's best), then the player must target in the
            // AI's GY (Cancel does not skip it); then the AI's 500 LP and both banished.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                engine.TryEndTurnSafe(p);
                DrainCombat(engine);
                var sd = PlaceFaceUp(engine, o, SoulDemolition, 0);
                PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var pBest = InGy(engine, p, BlueEyes);
                InGy(engine, p, Kuriboh);
                var o1 = InGy(engine, o, Celtic);
                var o2 = InGy(engine, o, BattleOx);
                var oLp = o.LifePoints;
                var wants = FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, o, sd);
                var fired = engine.TurnPlayer == o && engine.TryActivateSpellTrap(o, sd, fromHand: false);
                var pa = engine.PendingActivation;
                var playerPicks = pa != null && pa.Controller == p && pa.LegalTargets.Count == 2 &&
                                  pa.LegalTargets.Contains(o1) && pa.LegalTargets.Contains(o2) && o.LifePoints == oLp;
                engine.CancelEffectTargeting();
                var mandatory = engine.PendingActivation == pa && pa != null;
                var picked = mandatory && engine.TrySelectEffectTarget(o2);
                DrainCombat(engine);
                Check("Soul Demolition AI: it targets the player's best first, then the player targets in its GY " +
                      "(mandatory); 500 LP; both banished",
                    wants && fired && playerPicks && mandatory && picked && o.LifePoints == oLp - 500 &&
                    p.Banished.Contains(pBest) && o.Banished.Contains(o2) && o.Graveyard.Contains(o1) &&
                    !engine.Chain.HasLinks && engine.PendingActivation == null,
                    $"wants={wants} fired={fired} playerPicks={playerPicks} mandatory={mandatory} picked={picked} " +
                    $"oLp={o.LifePoints} (was {oLp}) {State(engine)}");
                var notTwice = !FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, o, sd);
                Check("Soul Demolition AI: once per turn", notTwice, State(engine));
            }

            // The player activates it on the AI's turn (open state): the AI (turn player) targets first.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                var sd = PlaceFaceUp(engine, p, SoulDemolition, 0);
                PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var pBest = InGy(engine, p, BlueEyes);
                InGy(engine, p, Kuriboh);
                var o1 = InGy(engine, o, Celtic);
                var o2 = InGy(engine, o, BattleOx);
                engine.TryEndTurnSafe(p);
                if (engine.PendingResponse?.Responder == p) engine.PassResponse();
                DrainCombat(engine);
                var pLp = p.LifePoints;
                var fired = engine.TurnPlayer == o && engine.TryActivateSpellTrap(p, sd, fromHand: false);
                var aiFirst = p.Graveyard.Contains(pBest) && engine.PendingActivation?.Controller == p;
                var picked = engine.PendingActivation != null && engine.TrySelectEffectTarget(o1);
                DrainCombat(engine);
                Check("Soul Demolition: on the opponent's turn the opponent (turn player) targets first, then the player",
                    fired && aiFirst && picked && p.LifePoints == pLp - 500 && p.Banished.Contains(pBest) &&
                    o.Banished.Contains(o1) && o.Graveyard.Contains(o2),
                    $"fired={fired} aiFirst={aiFirst} picked={picked} {State(engine)}");
            }
        }

        // ───────────────────────────── The Dragon's Bead ─────────────────────────────

        static void DragonsBeadDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human vs human: the opponent's Spellbinding Circle targets the player's face-up Blue-Eyes; the player chains
            // The Dragon's Bead (discard 1) → the Circle's effect is negated and the Circle destroyed.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var bead = PlaceFaceUp(engine, p, DragonsBead, 0);
                var be = PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
                var fodder = PutInHand(engine, p, Celtic);
                var circle = PlaceSetTrap(engine, o, SpellbindingCircle, 2);
                var cast = engine.TryActivateSpellTrap(o, circle, fromHand: false);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(be))
                    engine.TrySelectEffectTarget(be);
                var pr = engine.PendingResponse;
                var offered = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == p &&
                              pr.LegalCards.Contains(bead);
                var chained = offered && engine.TryActivateSpellTrap(p, bead, fromHand: false);
                DrainCombat(engine);
                Check("The Dragon's Bead: chained to a Trap targeting a face-up Dragon → negated and destroyed (discard 1)",
                    cast && offered && chained && o.Graveyard.Contains(circle) && !o.TryFindSpellTrap(circle, out _) &&
                    p.Graveyard.Contains(fodder) && p.TryFindMonster(be, out _) && bead.FaceUp &&
                    !engine.Chain.HasLinks,
                    $"cast={cast} offered={offered} chained={chained} circleGy={o.Graveyard.Contains(circle)} " +
                    $"fodderGy={p.Graveyard.Contains(fodder)} {State(engine)}");
            }

            // Not offered when the Trap targets a non-Dragon, nor in the open state, nor without a card to discard.
            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var bead = PlaceFaceUp(engine, p, DragonsBead, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PutInHand(engine, p, Kuriboh);
                var openState = !engine.CanActivateSpellTrap(p, bead, fromHand: false);
                var circle = PlaceSetTrap(engine, o, SpellbindingCircle, 2);
                var cast = engine.TryActivateSpellTrap(o, circle, fromHand: false);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(celtic))
                    engine.TrySelectEffectTarget(celtic);
                var pr = engine.PendingResponse;
                var notOffered = pr == null || !pr.LegalCards.Contains(bead);
                DrainCombat(engine);
                Check("The Dragon's Bead: not in the open state, and not offered against a Trap targeting a Warrior",
                    openState && cast && notOffered && o.TryFindSpellTrap(circle, out _) && circle.FaceUp,
                    $"open={openState} cast={cast} notOffered={notOffered} {State(engine)}");
            }

            {
                var engine = Board(db, pDeck, aDeck, true);
                var p = engine.Player;
                var o = engine.Opponent;
                var bead = PlaceFaceUp(engine, p, DragonsBead, 0);
                var be = PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
                var circle = PlaceSetTrap(engine, o, SpellbindingCircle, 2);
                engine.TryActivateSpellTrap(o, circle, fromHand: false);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(be))
                    engine.TrySelectEffectTarget(be);
                var pr = engine.PendingResponse;
                Check("The Dragon's Bead: no card in hand to discard → not offered",
                    pr == null || !pr.LegalCards.Contains(bead), State(engine));
                DrainCombat(engine);
            }

            // The AI protects its Blue-Eyes: the player's Raigeki Break targets it; the AI chains The Dragon's Bead.
            {
                var engine = Board(db, pDeck, aDeck, false);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUp(engine, o, DragonsBead, 0);
                var be = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                PutInHand(engine, o, Kuriboh);
                PutInHand(engine, o, Celtic);
                var rb = PlaceSetTrap(engine, p, RaigekiBreak, 2);
                PutInHand(engine, p, Kuriboh);
                var cast = engine.TryActivateSpellTrap(p, rb, fromHand: false);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(be))
                    engine.TrySelectEffectTarget(be);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Count > 0)
                    engine.TrySelectEffectTarget(engine.PendingActivation.LegalTargets[0]);
                if (engine.PendingActivation != null && engine.PendingActivation.LegalTargets.Contains(be))
                    engine.TrySelectEffectTarget(be);
                DrainCombat(engine);
                Check("The Dragon's Bead AI: chains it to the player's Raigeki Break on its Blue-Eyes → Blue-Eyes survives",
                    cast && o.TryFindMonster(be, out _) && p.Graveyard.Contains(rb) && o.Hand.Count == 1 &&
                    !engine.Chain.HasLinks,
                    $"cast={cast} beOnField={o.TryFindMonster(be, out _)} rbGy={p.Graveyard.Contains(rb)} " +
                    $"oHand={o.Hand.Count} {State(engine)}");
            }
        }
    }
}
