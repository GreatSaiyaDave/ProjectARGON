using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SummonNegation batch (v141 curriculum): Counter Traps that negate a Summon or an activation — Solemn
    /// Judgment (Summon OR Spell/Trap activation, pay half LP), Horn of Heaven (Summon, Tribute 1 monster) and
    /// Magic Drain (the opponent's Spell; they may discard a Spell to negate Magic Drain's effect). Compile
    /// shapes, mini-duels through the real engine entry points (Normal / Tribute / Flip Summon, Special Summon
    /// by procedure, Spell activation, response and chain windows, target picks) and fail-closed near misses.
    /// Runtime: <see cref="SummonNegationRuntime"/> and DuelEngine's summon-negation window.
    /// </summary>
    public static class SummonNegationRegressionTests
    {
        const int Solemn = 41420027;
        const int Horn = 98069388;
        const int MagicDrain = 59344077;

        const int SevenTools = 3819470;
        const int MagicJammer = 77414722;
        const int PotOfGreed = 55144522;
        const int Raigeki = 12580477;
        const int Messenger = 44656491;   // Continuous Spell
        const int TrapHole = 4206964;

        const int GeminiElf = 69140098;   // Level 4 1900
        const int Celtic = 91152256;      // Level 4 1400
        const int Kuriboh = 40640057;     // Level 1 300
        const int Skull = 70781052;       // Level 6 2500
        const int Curse = 50712728;       // Gravekeeper's Curse: when Summoned, 500 damage to the opponent
        const int Sangan = 26202165;      // field → GY: search
        const int ManEaterBug = 54652250; // FLIP: destroy 1 monster
        const int GateGuardian = 25833572;
        const int Sanga = 25955164;
        const int Kazejin = 62340868;
        const int Suijin = 98434877;
        const int Witch = 78010363;           // field → GY: search a DEF <= 1500 monster
        const int Megacyber = 66362965;       // The Fiend Megacyber: Special Summon from the hand (procedure)
        const int WitchsApprentice = 80741828; // face-up aura: DARK +500, LIGHT -400
        const int DianKeto = 84257640;        // gain 1000 LP
        const int Hinotama = 46130346;        // 500 damage
        const int Jinzo = 77585513;           // Traps cannot be activated (ContinuousNegation batch)

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            SolemnSummonDuels(db, pDeck, aDeck, Check);
            SolemnActivationDuels(db, pDeck, aDeck, Check);
            HornDuels(db, pDeck, aDeck, Check);
            SummonWindowScope(db, pDeck, aDeck, Check);
            MagicDrainDuels(db, pDeck, aDeck, Check);
            ChainLegality(db, pDeck, aDeck, Check);
            TributeTriggerWindows(db, pDeck, aDeck, Check);
            InherentProcedureWindows(db, pDeck, aDeck, Check);
            WindowDetails(db, pDeck, aDeck, Check);
            AiHalfLpValue(db, pDeck, aDeck, Check);
            JinzoIntegrationGuard(db, pDeck, aDeck, Check);
            KnownGaps(db, pDeck, aDeck, Check);
        }

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static string State(DuelEngine e) =>
            $"resp={e.PendingResponse?.Timing} pend={(e.PendingActivation != null)} chain={e.Chain.Count} " +
            $"awaiting={e.SummonAwaitingSuccess?.Name ?? "-"} P.LP={e.Player.LifePoints} O.LP={e.Opponent.LifePoints}";

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var solemn = CardTextEffectCompiler.Compile(db.Get(Solemn));
            Check("SummonNegation: Solemn Judgment FullyCompiled — Summon negation + Spell/Trap activation negation, pay half LP",
                solemn != null && solemn.FullyCompiled && solemn.ClauseList.Count == 2 &&
                solemn.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.MonsterWouldBeSummoned &&
                                              c.Action == EffectActionKind.NegateSummon && c.PayHalfLpCost &&
                                              c.DestroyNegatedCard && c.RequiresTributeCount == 0) &&
                solemn.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ChainLinkActivated &&
                                              c.Action == EffectActionKind.NegateActivation && c.PayHalfLpCost &&
                                              c.DestroyNegatedCard && c.NegateRespondsToSpell &&
                                              c.NegateRespondsToTrap && c.NegateCardActivationOnly &&
                                              !c.NegateOpponentActivationOnly && c.PayLpAmount == 0 &&
                                              !c.RequiresDiscardCost && !c.OpponentMayDiscardSpellToNegateThis) &&
                CardEffectStatus.Classify(db.Get(Solemn)) == CardEffectStatusKind.Implemented,
                Left(solemn));

            var horn = CardTextEffectCompiler.Compile(db.Get(Horn));
            Check("SummonNegation: Horn of Heaven FullyCompiled — Summon negation, Tribute 1 monster, destroy",
                horn != null && horn.FullyCompiled && horn.ClauseList.Count == 1 &&
                horn.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.MonsterWouldBeSummoned &&
                                            c.Action == EffectActionKind.NegateSummon &&
                                            c.RequiresTributeCount == 1 && c.DestroyNegatedCard &&
                                            !c.PayHalfLpCost) &&
                CardEffectStatus.Classify(db.Get(Horn)) == CardEffectStatusKind.Implemented,
                Left(horn));

            var drain = CardTextEffectCompiler.Compile(db.Get(MagicDrain));
            Check("SummonNegation: Magic Drain FullyCompiled — opponent's Spell activation, Spell-discard waiver, destroy",
                drain != null && drain.FullyCompiled && drain.ClauseList.Count == 1 &&
                drain.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ChainLinkActivated &&
                                             c.Action == EffectActionKind.NegateActivation &&
                                             c.NegateRespondsToSpell && !c.NegateRespondsToTrap &&
                                             c.NegateOpponentActivationOnly && c.NegateCardActivationOnly &&
                                             c.OpponentMayDiscardSpellToNegateThis && c.DestroyNegatedCard &&
                                             !c.RequiresDiscardCost && c.PayLpAmount == 0 && !c.PayHalfLpCost) &&
                CardEffectStatus.Classify(db.Get(MagicDrain)) == CardEffectStatusKind.Implemented,
                Left(drain));

            // The Magic Jammer / Seven Tools template is untouched by this batch.
            var jammer = CardTextEffectCompiler.Compile(db.Get(MagicJammer));
            Check("SummonNegation: Magic Jammer keeps its shape (no batch riders)",
                jammer != null && jammer.FullyCompiled &&
                jammer.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.NegateActivation &&
                                              c.RequiresDiscardCost && !c.PayHalfLpCost &&
                                              !c.NegateCardActivationOnly && !c.NegateOpponentActivationOnly &&
                                              !c.OpponentMayDiscardSpellToNegateThis),
                Left(jammer));
        }

        static CompiledCardProgram Text(int id, string name, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = name,
                type = "Trap Card",
                race = "Counter",
                frameType = "trap",
                desc = desc
            });

        static void NearMisses(CardDatabase db, Chk Check)
        {
            // Solemn Warning: a different trigger (only Special-Summoning activations) and a flat LP cost.
            var warning = Text(90041001, "Near-miss Solemn Warning",
                "When a monster(s) would be Summoned, OR when a Spell/Trap Card, or monster effect, is activated " +
                "that includes an effect that Special Summons a monster(s): Pay 2000 LP; negate the Summon or " +
                "activation, and if you do, destroy that card.");
            Check("SummonNegation near-miss: Solemn Warning's sentence does not compile as Solemn Judgment",
                warning == null || (!warning.FullyCompiled &&
                                    !warning.ClauseList.Exists(c => c != null &&
                                                                    (c.Action == EffectActionKind.NegateSummon ||
                                                                     c.PayHalfLpCost))),
                Left(warning));

            // Black Horn of Heaven: only the opponent's single Special Summon, no cost.
            var blackHorn = Text(90041002, "Near-miss Black Horn of Heaven",
                "When your opponent would Special Summon exactly 1 monster: Negate the Special Summon, and if you do, destroy it.");
            Check("SummonNegation near-miss: Black Horn of Heaven's sentence stays uncompiled",
                blackHorn == null || (!blackHorn.FullyCompiled &&
                                      !blackHorn.ClauseList.Exists(c => c != null &&
                                                                        c.Action == EffectActionKind.NegateSummon)),
                Left(blackHorn));

            // A Magic Drain-like text whose waiver is "discard 1 card" (not a Spell) must not borrow the Spell waiver.
            var anyCard = Text(90041003, "Near-miss discard any card waiver",
                "When your opponent activates a Spell Card: They can discard 1 card to negate this card's effect, " +
                "otherwise negate the activation of their Spell Card, and if you do, destroy it.");
            Check("SummonNegation near-miss: a discard-any-card waiver is not Magic Drain",
                anyCard == null || (!anyCard.FullyCompiled &&
                                    !anyCard.ClauseList.Exists(c => c != null && c.OpponentMayDiscardSpellToNegateThis)),
                Left(anyCard));

            // Horn of Heaven's words at the start of a longer sentence: the atom is whole-sentence only.
            var longer = Text(90041004, "Near-miss Horn with a longer destroy",
                "When a monster(s) would be Summoned: Tribute 1 monster; negate the Summon, and if you do, destroy " +
                "that monster(s) and all cards in its column.");
            Check("SummonNegation near-miss: Horn of Heaven's words inside a longer sentence do not compile",
                longer == null || (!longer.FullyCompiled &&
                                   !longer.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.NegateSummon)),
                Left(longer));
        }

        // ── Solemn Judgment: the Summon half ──

        static void SolemnSummonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // AI Solemn negates the player's Normal Summon of a 1900 ATK monster.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var elf = PutInHand(e, p, GeminiElf);
                var another = PutInHand(e, p, Celtic);
                var ok = e.TryNormalSummon(p, elf, asSet: false);
                Check("Solemn Judgment (AI): negates the Normal Summon, destroys the monster, pays half LP",
                    ok && !p.TryFindMonster(elf, out _) && p.Graveyard.Contains(elf) &&
                    o.Graveyard.Contains(solemn) && o.LifePoints == 4000 && p.LifePoints == 8000 &&
                    e.SummonAwaitingSuccess == null && !e.IsAwaitingResponse && !e.Chain.HasLinks,
                    $"ok={ok} {State(e)}");
                Check("Solemn Judgment (AI): the negated Normal Summon still used the Normal Summon",
                    p.NormalSummonUsed && !e.CanNormalSummonOrSet(p, another) && p.Hand.Contains(another),
                    $"used={p.NormalSummonUsed}");
            }

            // AI does not spend Solemn on a weak Summon: the Summon succeeds and its "when Summoned" effect fires.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var curse = PutInHand(e, p, Curse);
                var ok = e.TryNormalSummon(p, curse, asSet: false);
                Check("Solemn Judgment (AI): lets a weak Summon through — it succeeds and its Summon effect applies",
                    ok && p.TryFindMonster(curse, out _) && curse.FaceUp && !solemn.FaceUp &&
                    o.TryFindSpellTrap(solemn, out _) && o.LifePoints == 7500 && e.SummonAwaitingSuccess == null,
                    $"ok={ok} {State(e)}");
            }

            // Human Solemn against the opponent's Summon: the window opens before the Summon succeeds.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                o.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var curse = PutInHand(e, o, Curse);
                var ok = reached && e.TryNormalSummon(o, curse, asSet: false);
                var window = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.SummonNegation &&
                             e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(solemn) &&
                             e.SummonAwaitingSuccess == curse && o.TryFindMonster(curse, out _) &&
                             p.LifePoints == 8000;
                Check("Solemn Judgment (player): the summon-negation window opens before the Summon succeeds (no Summon effect yet)",
                    ok && window, $"reached={reached} ok={ok} {State(e)}");
                var act = window && e.TryActivateSpellTrap(p, solemn, fromHand: false);
                Check("Solemn Judgment (player): the Summon is negated — no \"when Summoned\" damage, monster destroyed",
                    act && !o.TryFindMonster(curse, out _) && o.Graveyard.Contains(curse) &&
                    p.Graveyard.Contains(solemn) && p.LifePoints == 4000 && o.NormalSummonUsed &&
                    !e.IsAwaitingResponse && e.SummonAwaitingSuccess == null && !e.Chain.HasLinks,
                    $"act={act} {State(e)}");
            }

            // Passing the window lets the Summon succeed.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var curse = PutInHand(e, o, Curse);
                var ok = reached && e.TryNormalSummon(o, curse, asSet: false) &&
                         e.PendingResponse?.Timing == ResponseTiming.SummonNegation;
                if (ok) e.PassResponse();
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Solemn Judgment (player passes): the Summon succeeds and its Summon effect applies",
                    ok && o.TryFindMonster(curse, out _) && p.LifePoints == 7500 && !solemn.FaceUp &&
                    p.TryFindSpellTrap(solemn, out _) && e.SummonAwaitingSuccess == null,
                    $"ok={ok} {State(e)}");
            }

            // A negated monster was never on the field: Sangan's field → GY search does not activate.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var sangan = PutInHand(e, o, Sangan);
                var deckBefore = o.Deck.Count;
                var ok = reached && e.TryNormalSummon(o, sangan, asSet: false) &&
                         e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                         e.TryActivateSpellTrap(p, solemn, fromHand: false);
                Check("Solemn Judgment: a negated Sangan was never on the field — no search",
                    ok && o.Graveyard.Contains(sangan) && o.HandCount == 0 && o.Deck.Count == deckBefore &&
                    !e.IsAwaitingEffectTarget,
                    $"ok={ok} hand={o.HandCount} deck={o.Deck.Count}/{deckBefore} {State(e)}");
            }

            // Flip Summon: negated before its FLIP effect.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
                var ok = reached && e.TryFlipSummon(o, bug);
                var window = ok && e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                             bug.FaceUp && p.TryFindMonster(celtic, out _);
                var act = window && e.TryActivateSpellTrap(p, solemn, fromHand: false);
                Check("Solemn Judgment: a negated Flip Summon is destroyed before its FLIP effect",
                    act && o.Graveyard.Contains(bug) && p.TryFindMonster(celtic, out _) &&
                    p.Graveyard.Contains(solemn) && !e.IsAwaitingResponse && !e.IsAwaitingEffectTarget,
                    $"ok={ok} window={window} act={act} {State(e)}");
            }

            // The summoner chains Seven Tools of the Bandit to the AI's Solemn: the Summon then succeeds.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var tools = PlaceSetTrap(e, p, SevenTools, 0);
                var elf = PutInHand(e, p, GeminiElf);
                var ok = e.TryNormalSummon(p, elf, asSet: false);
                var chainWindow = ok && e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                                  e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(tools) &&
                                  solemn.FaceUp && e.SummonAwaitingSuccess == elf;
                var act = chainWindow && e.TryActivateSpellTrap(p, tools, fromHand: false);
                if (e.IsAwaitingResponse && e.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
                    e.PassResponse();
                Check("Solemn Judgment: Seven Tools chained to it negates it — the Summon succeeds",
                    act && p.TryFindMonster(elf, out _) && elf.FaceUp && o.Graveyard.Contains(solemn) &&
                    p.Graveyard.Contains(tools) && o.LifePoints == 4000 && p.LifePoints == 7000 &&
                    e.SummonAwaitingSuccess == null && !e.Chain.HasLinks,
                    $"chainWindow={chainWindow} act={act} {State(e)}");
            }

            // Special Summon by procedure (Gate Guardian) has a summon-negation window.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                PlaceMonster(e, p, Sanga, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Kazejin, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, Suijin, 2, BattlePosition.Attack, true);
                var gate = PutInHand(e, p, GateGuardian);
                var ok = e.TrySpecialSummonProcedure(p, gate);
                Check("Solemn Judgment (AI): negates Gate Guardian's Special Summon by procedure",
                    ok && !p.TryFindMonster(gate, out _) && p.Graveyard.Contains(gate) &&
                    o.Graveyard.Contains(solemn) && o.LifePoints == 4000 && p.MonsterCount == 0,
                    $"ok={ok} {State(e)}");
            }
        }

        // ── Solemn Judgment: the Spell/Trap activation half ──

        static void SolemnActivationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // AI Solemn negates the player's Pot of Greed.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Solemn Judgment (AI): negates a Spell activation and destroys it, pays half LP",
                    act && p.Deck.Count == deckBefore && p.Graveyard.Contains(pot) &&
                    o.Graveyard.Contains(solemn) && o.LifePoints == 4000 && !e.Chain.HasLinks,
                    $"act={act} drew={deckBefore - p.Deck.Count} {State(e)}");
            }

            // The AI keeps Solemn when half its LP would cost too much.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.LifePoints = 1500;
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Solemn Judgment (AI): not paid at low LP — the Spell resolves",
                    act && deckBefore - p.Deck.Count == 2 && !solemn.FaceUp && o.TryFindSpellTrap(solemn, out _) &&
                    o.LifePoints == 1500,
                    $"act={act} drew={deckBefore - p.Deck.Count} {State(e)}");
            }

            // Human Solemn answers the opponent's Trap activation (the Trap half).
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                var deckBefore = o.Deck.Count;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var window = act && e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                             e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(solemn);
                var answered = window && e.TryActivateSpellTrap(p, solemn, fromHand: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Solemn Judgment (player): negates the opponent's Spell activation",
                    answered && o.Deck.Count == deckBefore && o.Graveyard.Contains(pot) &&
                    p.Graveyard.Contains(solemn) && p.LifePoints == 4000 && !e.Chain.HasLinks,
                    $"reached={reached} act={act} window={window} answered={answered} {State(e)}");
            }
        }

        // ── Horn of Heaven ──

        static void HornDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // AI: Tributes its weakest monster to negate a stronger Summon.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var horn = PlaceSetTrap(e, o, Horn, 0);
                var kuri = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var elf = PutInHand(e, p, GeminiElf);
                var ok = e.TryNormalSummon(p, elf, asSet: false);
                Check("Horn of Heaven (AI): Tributes 1 monster, negates the Summon and destroys the monster",
                    ok && p.Graveyard.Contains(elf) && !p.TryFindMonster(elf, out _) &&
                    o.Graveyard.Contains(kuri) && o.Graveyard.Contains(horn) && o.LifePoints == 8000 &&
                    p.NormalSummonUsed && e.SummonAwaitingSuccess == null,
                    $"ok={ok} {State(e)}");
            }

            // AI: no Tribute weaker than the Summoned monster → it keeps Horn.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var horn = PlaceSetTrap(e, o, Horn, 0);
                var skull = PlaceMonster(e, o, Skull, 0, BattlePosition.Attack, true);
                var elf = PutInHand(e, p, GeminiElf);
                var ok = e.TryNormalSummon(p, elf, asSet: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Horn of Heaven (AI): kept when its only Tribute is stronger — the Summon succeeds",
                    ok && p.TryFindMonster(elf, out _) && o.TryFindMonster(skull, out _) && !horn.FaceUp,
                    $"ok={ok} {State(e)}");
            }

            // Not legal without a monster to Tribute.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var horn = PlaceSetTrap(e, o, Horn, 0);
                var elf = PutInHand(e, p, GeminiElf);
                var ok = e.TryNormalSummon(p, elf, asSet: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Horn of Heaven: no monster to Tribute — no window, the Summon succeeds",
                    ok && p.TryFindMonster(elf, out _) && !horn.FaceUp && e.SummonAwaitingSuccess == null,
                    $"ok={ok} {State(e)}");
            }

            // Player with two possible Tributes picks one; the Summon is negated.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                var horn = PlaceSetTrap(e, p, Horn, 0);
                var kuri = PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Defense, false);
                var curse = PutInHand(e, o, Curse);
                var ok = reached && e.TryNormalSummon(o, curse, asSet: false) &&
                         e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                         e.PendingResponse.LegalCards.Contains(horn);
                var act = ok && e.TryActivateSpellTrap(p, horn, fromHand: false);
                var pick = act && e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingSummonNegationTribute &&
                           e.PendingActivation.TargetKind == EffectTargetKind.TributeMonsterYouControl &&
                           e.PendingActivation.LegalTargets.Contains(kuri) &&
                           e.PendingActivation.LegalTargets.Contains(celtic) &&
                           e.PendingResponse?.Timing == ResponseTiming.SummonNegation;
                var chosen = pick && e.TrySelectEffectTarget(celtic);
                Check("Horn of Heaven (player): picks the Tribute (face-down allowed), then negates the Summon",
                    chosen && p.Graveyard.Contains(celtic) && p.TryFindMonster(kuri, out _) &&
                    o.Graveyard.Contains(curse) && p.Graveyard.Contains(horn) && p.LifePoints == 8000 &&
                    !e.IsAwaitingResponse && !e.IsAwaitingEffectTarget && e.SummonAwaitingSuccess == null,
                    $"ok={ok} act={act} pick={pick} chosen={chosen} {State(e)}");
            }

            // Cancelling the Tribute pick keeps Horn Set and re-offers the window; passing lets the Summon succeed.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                var horn = PlaceSetTrap(e, p, Horn, 0);
                var kuri = PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var curse = PutInHand(e, o, Curse);
                var ok = reached && e.TryNormalSummon(o, curse, asSet: false) &&
                         e.TryActivateSpellTrap(p, horn, fromHand: false) && e.IsAwaitingEffectTarget;
                var cancelled = ok && e.CancelEffectTargeting();
                var reoffered = cancelled && !e.IsAwaitingEffectTarget && !horn.FaceUp &&
                                p.TryFindSpellTrap(horn, out _) && !e.Chain.HasLinks &&
                                e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                                e.PendingResponse.LegalCards.Contains(horn);
                if (reoffered) e.PassResponse();
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Horn of Heaven (player): Cancel re-offers the window; Pass lets the Summon succeed",
                    reoffered && o.TryFindMonster(curse, out _) && p.LifePoints == 7500 &&
                    p.TryFindMonster(kuri, out _) && p.TryFindMonster(celtic, out _) && !horn.FaceUp,
                    $"ok={ok} cancelled={cancelled} reoffered={reoffered} {State(e)}");
            }
        }

        // ── What has a summon-negation window and what does not ──

        static void SummonWindowScope(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A Set is not a Summon.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var elf = PutInHand(e, p, GeminiElf);
                var ok = e.TryNormalSummon(p, elf, asSet: true);
                Check("Summon negation: a Normal Set opens no summon-negation window",
                    ok && p.TryFindMonster(elf, out _) && !elf.FaceUp && !solemn.FaceUp &&
                    e.SummonAwaitingSuccess == null && o.LifePoints == 8000,
                    $"ok={ok} {State(e)}");
            }

            // A Special Summon performed by an effect cannot be negated by Horn of Heaven / Solemn.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var horn = PlaceSetTrap(e, o, Horn, 0);
                var kuri = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var skull = e.CreateCardInstance(Skull);
                var ok = e.SpecialSummonToField(p, skull, BattlePosition.Attack, true);
                if (e.IsAwaitingResponse && e.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
                    e.PassResponse();
                Check("Summon negation: a Special Summon by an effect has no negation window",
                    ok && p.TryFindMonster(skull, out _) && !horn.FaceUp && o.TryFindMonster(kuri, out _) &&
                    e.SummonAwaitingSuccess == null,
                    $"ok={ok} {State(e)}");
            }

            // Horn / Solemn cannot be activated in the open game state.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var horn = PlaceSetTrap(e, p, Horn, 0);
                var solemn = PlaceSetTrap(e, p, Solemn, 1);
                PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);
                Check("Summon negation: Horn of Heaven / Solemn Judgment are not activatable with no Summon or chain",
                    !e.CanActivateSpellTrap(p, horn, false) && !e.CanActivateSpellTrap(p, solemn, false));
            }
        }

        // ── Magic Drain ──

        static void MagicDrainDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // AI Magic Drain, player has no Spell in hand: the Spell is negated and destroyed.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var drain = PlaceSetTrap(e, o, MagicDrain, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                PutInHand(e, p, Celtic);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Magic Drain (AI): no Spell in the player's hand — their Spell is negated and destroyed",
                    act && p.Deck.Count == deckBefore && p.Graveyard.Contains(pot) && o.Graveyard.Contains(drain) &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks && o.LifePoints == 8000,
                    $"act={act} {State(e)}");
            }

            // AI Magic Drain, player holds a Spell: the chain pauses for the player's choice; discarding saves the Spell.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var drain = PlaceSetTrap(e, o, MagicDrain, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                var raigeki = PutInHand(e, p, Raigeki);
                var celtic = PutInHand(e, p, Celtic);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.IsAwaitingResponse && !e.IsAwaitingEffectTarget) e.PassResponse();
                var paused = act && e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingSpellDiscardWaiver &&
                             e.PendingActivation.Controller == p &&
                             e.PendingActivation.LegalTargets.Count == 1 &&
                             e.PendingActivation.LegalTargets.Contains(raigeki) &&
                             e.IsResolvingChain && p.Deck.Count == deckBefore;
                var chosen = paused && e.TrySelectEffectTarget(raigeki);
                Check("Magic Drain (AI): the player discards a Spell — Magic Drain's effect is negated, the Spell resolves",
                    chosen && p.Graveyard.Contains(raigeki) && deckBefore - p.Deck.Count == 2 &&
                    p.Hand.Contains(celtic) && o.Graveyard.Contains(drain) && !e.IsResolvingChain &&
                    !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"act={act} paused={paused} chosen={chosen} drew={deckBefore - p.Deck.Count} {State(e)}");
            }

            // Same, but the player declines: the Spell is negated and destroyed, the other Spell stays in hand.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var drain = PlaceSetTrap(e, o, MagicDrain, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                var raigeki = PutInHand(e, p, Raigeki);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.IsAwaitingResponse && !e.IsAwaitingEffectTarget) e.PassResponse();
                var paused = act && e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingSpellDiscardWaiver;
                var declined = paused && e.CancelEffectTargeting();
                Check("Magic Drain (AI): the player declines — their Spell is negated and destroyed",
                    declined && p.Deck.Count == deckBefore && p.Graveyard.Contains(pot) && p.Hand.Contains(raigeki) &&
                    o.Graveyard.Contains(drain) && !e.IsResolvingChain && !e.Chain.HasLinks &&
                    !e.IsAwaitingEffectTarget,
                    $"act={act} paused={paused} declined={declined} {State(e)}");
            }

            // Player's Magic Drain vs the AI's Spell: the AI discards a Spell when it has one.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                var drain = PlaceSetTrap(e, p, MagicDrain, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                var spare = PutInHand(e, o, Raigeki);
                var deckBefore = o.Deck.Count;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var window = act && e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                             e.PendingResponse.LegalCards.Contains(drain);
                var answered = window && e.TryActivateSpellTrap(p, drain, fromHand: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Magic Drain (player): the AI discards its Spell — the AI's Spell resolves",
                    answered && o.Graveyard.Contains(spare) && deckBefore - o.Deck.Count == 2 &&
                    p.Graveyard.Contains(drain) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"window={window} answered={answered} drew={deckBefore - o.Deck.Count} {State(e)}");
            }

            // Player's Magic Drain vs the AI's Spell, the AI has no Spell to discard: negated and destroyed.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                var drain = PlaceSetTrap(e, p, MagicDrain, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                var deckBefore = o.Deck.Count;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var answered = act && e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                               e.TryActivateSpellTrap(p, drain, fromHand: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Magic Drain (player): the AI has no Spell — its Spell is negated and destroyed",
                    answered && o.Deck.Count == deckBefore && o.Graveyard.Contains(pot) &&
                    p.Graveyard.Contains(drain) && !e.Chain.HasLinks,
                    $"answered={answered} {State(e)}");
            }
        }

        // ── Chain-response legality near misses ──

        static void ChainLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            var solemn = PlaceSetTrap(e, p, Solemn, 0);
            var drain = PlaceSetTrap(e, p, MagicDrain, 1);
            var horn = PlaceSetTrap(e, p, Horn, 2);
            PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);

            System.Collections.Generic.List<CardInstance> LegalAgainst(DuelistState controller, int cardId,
                bool fromHand, bool wasSet)
            {
                e.Chain.Clear();
                var c = e.CreateCardInstance(cardId);
                c.FaceUp = true;
                controller.SpellTrapZones[4].Occupant = c;
                e.Chain.BeginBuilding();
                var link = e.Chain.AddLink(controller, c, OfficialEffectRegistry.SpeedOf(c.Def),
                    OfficialEffectRegistry.ClassOfActivation(c.Def, false), CardLocation.SpellTrapZone,
                    fromHand, wasSet, "t");
                e.SetPendingResponse(new PendingResponse
                    { Timing = ResponseTiming.ChainResponse, Responder = p, ChainTarget = link });
                var legal = SpellTrapEffects.CollectLegalResponseCards(e, p, ResponseTiming.ChainResponse, null);
                e.ClearPendingResponse();
                e.Chain.Clear();
                controller.SpellTrapZones[4].Occupant = null;
                return legal;
            }

            var oppSpellCard = LegalAgainst(o, Messenger, fromHand: true, wasSet: false);
            Check("SummonNegation legality: an opponent's Spell Card activation — Solemn and Magic Drain answer, Horn does not",
                oppSpellCard.Contains(solemn) && oppSpellCard.Contains(drain) && !oppSpellCard.Contains(horn),
                string.Join(",", oppSpellCard.Select(c => c.Name)));

            var oppSpellEffect = LegalAgainst(o, Messenger, fromHand: false, wasSet: false);
            Check("SummonNegation legality: an effect of a face-up Spell is not a card activation (no Solemn / Magic Drain)",
                !oppSpellEffect.Contains(solemn) && !oppSpellEffect.Contains(drain),
                string.Join(",", oppSpellEffect.Select(c => c.Name)));

            var ownSpell = LegalAgainst(p, PotOfGreed, fromHand: true, wasSet: false);
            Check("SummonNegation legality: Magic Drain never answers its controller's own Spell (Solemn may)",
                !ownSpell.Contains(drain) && ownSpell.Contains(solemn),
                string.Join(",", ownSpell.Select(c => c.Name)));

            var oppTrap = LegalAgainst(o, TrapHole, fromHand: false, wasSet: true);
            Check("SummonNegation legality: an opponent's Trap activation — Solemn answers, Magic Drain does not",
                oppTrap.Contains(solemn) && !oppTrap.Contains(drain),
                string.Join(",", oppTrap.Select(c => c.Name)));
        }

        // ── Review fixes: a Tribute's field → GY trigger no longer skips the window ──

        static void TributeTriggerWindows(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Player Tribute Summons over Sangan; AI Solemn. Sangan's search waits for the Summon, then activates.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                p.Deck.Add(Kuriboh);
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var sangan = PlaceMonster(e, p, Sangan, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, p, Skull);
                var ok = e.TryNormalSummon(p, skull, asSet: false);
                var searchNow = e.IsAwaitingEffectTarget && e.PendingActivation?.Card == sangan;
                Check("Solemn Judgment (AI): negates a Tribute Summon over Sangan (the window opens despite Sangan's pick)",
                    ok && p.Graveyard.Contains(skull) && !p.TryFindMonster(skull, out _) &&
                    o.Graveyard.Contains(solemn) && o.LifePoints == 4000 && p.Graveyard.Contains(sangan) &&
                    e.SummonAwaitingSuccess == null && !e.Chain.HasLinks,
                    $"ok={ok} {State(e)}");
                Check("Solemn Judgment (AI): the Tributed Sangan's search activates after the negation",
                    searchNow && e.PendingActivation.Controller == p && !e.IsAwaitingResponse,
                    $"searchNow={searchNow} pend={e.PendingActivation?.Card?.Name}/{e.PendingActivation?.TargetKind}");
                var picked = searchNow && e.TrySelectEffectTarget(e.PendingActivation.LegalTargets.FirstOrDefault());
                Check("Solemn Judgment (AI): after Sangan's search the state is clean",
                    picked && p.HandCount == 1 && !e.IsAwaitingEffectTarget && !e.IsAwaitingResponse &&
                    !e.Chain.HasLinks,
                    $"picked={picked} hand={p.HandCount} {State(e)}");
            }

            // Player Tribute Summons over Witch of the Black Forest; AI Horn of Heaven (Tributes Kuriboh).
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                p.Deck.Add(Kuriboh);
                var horn = PlaceSetTrap(e, o, Horn, 0);
                var kuri = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(e, p, Witch, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, p, Skull);
                e.ToggleTribute(p, witch);
                var ok = e.TryNormalSummon(p, skull, asSet: false);
                var searchNow = e.IsAwaitingEffectTarget && e.PendingActivation?.Card == witch;
                Check("Horn of Heaven (AI): negates a Tribute Summon over Witch of the Black Forest; Witch's search follows",
                    ok && p.Graveyard.Contains(skull) && o.Graveyard.Contains(horn) && o.Graveyard.Contains(kuri) &&
                    p.Graveyard.Contains(witch) && searchNow && e.SummonAwaitingSuccess == null && !e.Chain.HasLinks,
                    $"ok={ok} searchNow={searchNow} {State(e)}");
            }

            // Control: the AI keeps Solemn (low LP). The Tribute Summon over Sangan succeeds; Sangan's search opens.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                p.Deck.Add(Kuriboh);
                o.LifePoints = 1500;
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var sangan = PlaceMonster(e, p, Sangan, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, p, Skull);
                var ok = e.TryNormalSummon(p, skull, asSet: false);
                var searchNow = e.IsAwaitingEffectTarget && e.PendingActivation?.Card == sangan;
                var picked = searchNow && e.TrySelectEffectTarget(e.PendingActivation.LegalTargets.FirstOrDefault());
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Summon negation: an unanswered Tribute Summon over Sangan succeeds, then Sangan searches",
                    ok && searchNow && picked && p.TryFindMonster(skull, out _) && skull.FaceUp &&
                    p.HandCount == 1 && !solemn.FaceUp && o.LifePoints == 1500 && e.SummonAwaitingSuccess == null &&
                    !e.IsAwaitingEffectTarget && !e.IsAwaitingResponse && !e.Chain.HasLinks,
                    $"ok={ok} searchNow={searchNow} picked={picked} hand={p.HandCount} {State(e)}");
            }

            // The AI Tribute Summons over its own Sangan; the player's Solemn window opens before Sangan's search.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                o.Deck.Add(Kuriboh);
                p.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var sangan = PlaceMonster(e, o, Sangan, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, o, Skull);
                var ok = reached && e.TryNormalSummon(o, skull, asSet: false);
                var window = ok && e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                             e.PendingResponse.LegalCards.Contains(solemn) && o.HandCount == 0;
                var act = window && e.TryActivateSpellTrap(p, solemn, fromHand: false);
                Check("Solemn Judgment (player): negates the AI's Tribute Summon over Sangan; Sangan searches afterwards",
                    act && o.Graveyard.Contains(skull) && o.Graveyard.Contains(sangan) && o.HandCount == 1 &&
                    p.LifePoints == 4000 && e.SummonAwaitingSuccess == null && !e.IsAwaitingEffectTarget &&
                    !e.Chain.HasLinks,
                    $"reached={reached} ok={ok} window={window} act={act} oHand={o.HandCount} {State(e)}");
            }
        }

        // ── Review fixes: The Fiend Megacyber's Special Summon from the hand is a procedure (negatable) ──

        static void InherentProcedureWindows(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Player activates Megacyber's procedure; AI Solemn negates it.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var mega = PutInHand(e, p, Megacyber);
                var can = e.CanActivateSpellTrap(p, mega, true);
                var act = can && e.TryActivateSpellTrap(p, mega, true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Solemn Judgment (AI): negates The Fiend Megacyber's Special Summon from the hand",
                    act && p.Graveyard.Contains(mega) && !p.TryFindMonster(mega, out _) &&
                    o.Graveyard.Contains(solemn) && o.LifePoints == 4000 && e.SummonAwaitingSuccess == null &&
                    !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"can={can} act={act} {State(e)}");
            }

            // The AI Special Summons Megacyber; the player's Solemn window opens, activating negates it.
            foreach (var answer in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var mega = PutInHand(e, o, Megacyber);
                var act = reached && e.TryActivateSpellTrap(o, mega, true);
                var window = act && e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                             e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(solemn) &&
                             e.SummonAwaitingSuccess == mega && !e.Chain.HasLinks;
                if (answer)
                {
                    var neg = window && e.TryActivateSpellTrap(p, solemn, fromHand: false);
                    Check("Solemn Judgment (player): the window opens for the AI's Megacyber and negates it",
                        neg && o.Graveyard.Contains(mega) && p.Graveyard.Contains(solemn) && p.LifePoints == 4000 &&
                        e.SummonAwaitingSuccess == null && !e.Chain.HasLinks,
                        $"reached={reached} act={act} window={window} neg={neg} {State(e)}");
                }
                else
                {
                    if (window) e.PassResponse();
                    for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                    Check("Solemn Judgment (player passes): the AI's Megacyber is Special Summoned",
                        window && o.TryFindMonster(mega, out _) && mega.FaceUp && !solemn.FaceUp &&
                        e.SummonAwaitingSuccess == null && !e.Chain.HasLinks,
                        $"reached={reached} act={act} window={window} {State(e)}");
                }
            }

            // No answer: Megacyber is Special Summoned and leaves no chain link behind.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var mega = PutInHand(e, p, Megacyber);
                var act = e.TryActivateSpellTrap(p, mega, true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("The Fiend Megacyber: with no negation card it is Special Summoned; no chain link is left",
                    act && p.TryFindMonster(mega, out _) && mega.FaceUp && !e.Chain.HasLinks &&
                    e.SummonAwaitingSuccess == null && !e.IsAwaitingResponse,
                    $"act={act} {State(e)}");
            }
        }

        // ── Review fixes: HvH Tribute pick, hint line, continuous effects of the monster being Summoned ──

        static void WindowDetails(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Two humans: the Opponent seat's Horn of Heaven asks for its Tribute.
            {
                var e = Fresh(db, pDeck, aDeck);
                e.HumanVsHuman = true;
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var horn = PlaceSetTrap(e, o, Horn, 0);
                var kuri = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var elf = PutInHand(e, p, GeminiElf);
                var ok = e.TryNormalSummon(p, elf, asSet: false);
                var window = ok && e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                             e.PendingResponse.Responder == o && e.PendingResponse.LegalCards.Contains(horn);
                var act = window && e.TryActivateSpellTrap(o, horn, fromHand: false);
                var asked = act && e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingSummonNegationTribute &&
                            e.PendingActivation.Controller == o && e.PendingActivation.LegalTargets.Contains(kuri) &&
                            e.PendingActivation.LegalTargets.Contains(celtic) && !horn.FaceUp;
                var chosen = asked && e.TrySelectEffectTarget(celtic);
                Check("Horn of Heaven (HvH): the second human picks the Tribute; the Summon is negated",
                    chosen && o.Graveyard.Contains(celtic) && o.TryFindMonster(kuri, out _) &&
                    p.Graveyard.Contains(elf) && o.Graveyard.Contains(horn) && e.SummonAwaitingSuccess == null,
                    $"window={window} act={act} asked={asked} chosen={chosen} {State(e)}");
            }

            // The hint line names the summon-negation window as a Summon window, not an attack.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                PlaceSetTrap(e, p, Solemn, 0);
                var curse = PutInHand(e, o, Curse);
                var ok = reached && e.TryNormalSummon(o, curse, asSet: false) &&
                         e.PendingResponse?.Timing == ResponseTiming.SummonNegation;
                // The opponent's Summon holds the presentation pacer; its read hold takes priority in the hint.
                DuelPresentationPacer.Clear();
                var hint = DuelPresentationPacer.IsHolding ? "(pacer holding)" : e.HintLine() ?? "";
                Check("Summon negation: the hint line calls the summon-negation window a Summon window, not an attack",
                    ok && hint.StartsWith("Summon"), $"ok={ok} hint={hint}");
            }

            // The monster being Summoned is not on the field yet: its face-up aura waits for the Summon to succeed.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                var skull = PlaceMonster(e, p, Skull, 0, BattlePosition.Attack, true); // DARK
                e.NotifyPublic();
                var before = skull.CurrentAtk;
                var apprentice = PutInHand(e, o, WitchsApprentice);
                var ok = reached && e.TryNormalSummon(o, apprentice, asSet: false) &&
                         e.PendingResponse?.Timing == ResponseTiming.SummonNegation;
                e.NotifyPublic();
                var during = skull.CurrentAtk;
                var pending = e.IsSummonPending(apprentice);
                if (ok) e.PassResponse();
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                e.NotifyPublic();
                var after = skull.CurrentAtk;
                Check("Summon negation: a monster in its summon-negation window applies no aura until its Summon succeeds",
                    ok && pending && before == 2500 && during == 2500 && after == 3000 &&
                    !e.IsSummonPending(apprentice) && o.TryFindMonster(apprentice, out _) && !solemn.FaceUp,
                    $"ok={ok} pending={pending} atk {before}/{during}/{after} {State(e)}");
            }
        }

        // ── Review fixes: the AI spends Solemn's half LP only on an activation that matters ──

        static void AiHalfLpValue(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, label) in new[] { (DianKeto, "Dian Keto (gain 1000 LP)"), (Hinotama, "Hinotama (500 damage)") })
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                p.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var spell = PutInHand(e, p, id);
                var act = e.TryActivateSpellTrap(p, spell, fromHand: true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var resolved = id == DianKeto ? p.LifePoints == 9000 : o.LifePoints == 7500;
                Check($"Solemn Judgment (AI): keeps Solemn against {label} — the Spell resolves",
                    act && resolved && !solemn.FaceUp && o.TryFindSpellTrap(solemn, out _) && !e.Chain.HasLinks,
                    $"act={act} {State(e)}");
            }

            // Still worth it: a Spell that destroys the AI's monsters.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var skull = PlaceMonster(e, o, Skull, 0, BattlePosition.Attack, true);
                var raigeki = PutInHand(e, p, Raigeki);
                var act = e.TryActivateSpellTrap(p, raigeki, fromHand: true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Solemn Judgment (AI): still negates Raigeki (it would destroy the AI's monsters)",
                    act && o.Graveyard.Contains(solemn) && o.TryFindMonster(skull, out _) && p.Graveyard.Contains(raigeki) &&
                    o.LifePoints == 4000,
                    $"act={act} {State(e)}");
            }
        }

        // ── Integration guard: Solemn Judgment vs Jinzo's own Summon ──

        /// <summary>
        /// Jinzo's Trap lock must not apply while Jinzo's Summon is still in its summon-negation window (it is not
        /// on the field yet): Solemn Judgment answers Jinzo's Summon. These checks run once Jinzo compiles (the
        /// ContinuousNegation batch). They fail if that batch's continuous-source scan does not skip
        /// <see cref="DuelEngine.IsSummonPending"/>.
        /// </summary>
        static void JinzoIntegrationGuard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var jinzoProg = CardTextEffectCompiler.Compile(db.Get(Jinzo));
            if (jinzoProg == null || !jinzoProg.FullyCompiled ||
                CardEffectStatus.Classify(db.Get(Jinzo)) != CardEffectStatusKind.Implemented)
            {
                Check("Solemn Judgment vs Jinzo: waits until Jinzo compiles (ContinuousNegation batch)", true,
                    Left(jinzoProg));
                return;
            }

            // The AI Tribute Summons Jinzo; the player's Solemn still gets the window.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Hand.Clear();
                p.LifePoints = 8000;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var jinzo = PutInHand(e, o, Jinzo);
                var ok = reached && e.TryNormalSummon(o, jinzo, asSet: false);
                var window = ok && e.PendingResponse?.Timing == ResponseTiming.SummonNegation &&
                             e.PendingResponse.LegalCards.Contains(solemn);
                var act = window && e.TryActivateSpellTrap(p, solemn, fromHand: false);
                Check("Solemn Judgment (player) vs Jinzo: Jinzo's lock does not apply before its Summon succeeds",
                    act && o.Graveyard.Contains(jinzo) && p.Graveyard.Contains(solemn) && p.LifePoints == 4000,
                    $"reached={reached} ok={ok} window={window} act={act} {State(e)}");
            }

            // The player Tribute Summons Jinzo; the AI's Solemn negates it.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var jinzo = PutInHand(e, p, Jinzo);
                e.ToggleTribute(p, celtic);
                var ok = e.TryNormalSummon(p, jinzo, asSet: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Solemn Judgment (AI) vs Jinzo: negates the Tribute Summon of Jinzo",
                    ok && p.Graveyard.Contains(jinzo) && o.Graveyard.Contains(solemn) && o.LifePoints == 4000,
                    $"ok={ok} {State(e)}");
            }
        }

        // ── Known engine gap (pre-existing; Seven Tools of the Bandit and Magic Jammer share it) ──

        static void KnownGaps(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Trap Hole-style answers resolve inline (no chain link), so Solemn's Spell/Trap half is not offered
            // against them, exactly like Seven Tools. When the engine seats them on the chain, this check flips:
            // update it to assert the negation.
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            var hole = PlaceSetTrap(e, o, TrapHole, 0);
            var solemn = PlaceSetTrap(e, p, Solemn, 0);
            var tools = PlaceSetTrap(e, p, SevenTools, 1);
            var elf = PutInHand(e, p, GeminiElf);
            var ok = e.TryNormalSummon(p, elf, asSet: false);
            var offered = e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                          e.PendingResponse.Responder == p;
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            Check("KNOWN GAP (pre-existing): an inline Trap Hole offers no chain window — Solemn and Seven Tools stay Set",
                ok && !offered && o.Graveyard.Contains(hole) && p.Graveyard.Contains(elf) && !solemn.FaceUp &&
                !tools.FaceUp,
                $"ok={ok} offered={offered} {State(e)}");
        }
    }
}
