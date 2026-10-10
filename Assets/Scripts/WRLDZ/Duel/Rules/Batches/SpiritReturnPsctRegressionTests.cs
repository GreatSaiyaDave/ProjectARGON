using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "SpiritReturnPsct": the PSCT prints of the Spirit return ("During the End Phase of the turn this card is
    /// Normal Summoned or flipped face-up: Return it to the hand." / Tsukuyomi's "Once per turn, during the End Phase,
    /// if this card was Normal Summoned or flipped face-up this turn: Return it to the hand.") compile to the shared
    /// Spirit return, so these Spirits go back to the hand during the End Phase of the turn they were Normal Summoned
    /// or flipped face-up — and only then.
    /// </summary>
    public static class SpiritReturnPsctRegressionTests
    {
        const int AsuraPriest = 2134346;     // Lv4
        const int Otohime = 39751093;        // Lv3
        const int OtohimeAlt = 39751094;     // Lv3, alternate artwork
        const int DarkDustSpirit = 89111398; // Lv6: one Tribute
        const int Tsukuyomi = 34853266;      // Lv4
        const int Celtic = 91152256;         // filler monster

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("SpiritReturnPsct: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NormalSummonReturns(db, pDeck, aDeck, Check);
            FlipSummonReturnsAndOldSpiritStays(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 on turn 3 (a Normal Summon is legal), empty board and hands.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                      engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        static bool SpiritReturnShape(EffectClause c) =>
            c != null && c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ReturnToHand &&
            c.RequiresSummonedOrFlippedThisTurn && !c.RequiresTargetChoice;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}]";

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            foreach (var id in new[] { AsuraPriest, Otohime, OtohimeAlt, DarkDustSpirit, Tsukuyomi })
            {
                if (!db.TryGet(id, out var def)) continue;
                var prog = CardTextEffectCompiler.Compile(def);
                var spirit = prog?.ClauseList.FindAll(SpiritReturnShape).Count ?? 0;
                var otherReturn = prog != null && prog.ClauseList.Exists(c =>
                    c != null && c.Action == EffectActionKind.ReturnToHand && !SpiritReturnShape(c));
                Check($"{def.name} ({id}): one Spirit return clause, no targeted or summon-time return",
                    spirit == 1 && !otherReturn, Describe(prog));
            }

            // Near miss: the atom takes only the whole sentence, never the head of a longer one.
            var longer = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990031, name = "Near-miss Spirit", type = "Spirit Monster", frameType = "effect",
                race = "Fiend", attribute = "DARK", level = 4,
                desc = "During the End Phase of the turn this card is Normal Summoned or flipped face-up: " +
                       "Return it to the hand, then draw 1 card."
            });
            Check("a longer sentence is not taken by the Spirit return atom",
                longer == null || !longer.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ReturnToHand),
                Describe(longer));
        }

        /// <summary>
        /// Normal Summoned (Tribute Summon included) → back to the owner's hand during that End Phase. The Summon
        /// itself returns nothing: the opponent's face-up monster stays on the field.
        /// </summary>
        static void NormalSummonReturns(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var id in new[] { AsuraPriest, Otohime, OtohimeAlt, Tsukuyomi, DarkDustSpirit })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bystander = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                if (id == DarkDustSpirit)
                    engine.ToggleTribute(p, PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true));
                var spirit = PutInHand(engine, p, id);
                var ns = engine.TryNormalSummon(p, spirit, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                // Otohime's own "When this card is Normal Summoned or flipped face-up: You can target 1 face-up monster
                // your opponent controls; change that target's battle position." (SpiritMonsters batch) offers the
                // bystander; it is declined here (CANCEL), so nothing changes.
                var isOtohime = id == Otohime || id == OtohimeAlt;
                var offered = engine.IsAwaitingEffectTarget &&
                              engine.PendingActivation.LegalTargets.Contains(bystander);
                if (isOtohime && engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                if (engine.IsAwaitingEffectTarget) engine.ClearPendingActivation();
                if (isOtohime)
                    Check($"{spirit.Name} ({id}): its Summon offers the optional position change; declined, the target " +
                          "keeps its position",
                        offered && !engine.IsAwaitingEffectTarget && bystander.Position == BattlePosition.Attack,
                        $"offered={offered} pos={bystander.Position}");
                var onField = p.TryFindMonster(spirit, out _);
                var turn = engine.TurnNumber;
                EndTurn(engine);
                Check($"{spirit.Name} ({id}): Normal Summoned → returns to the hand during the End Phase",
                    reached && ns && onField && p.Hand.Contains(spirit) && !p.TryFindMonster(spirit, out _) &&
                    engine.TurnNumber == turn + 1,
                    $"reached={reached} ns={ns} onField={onField} inHand={p.Hand.Contains(spirit)} turn={turn}->{engine.TurnNumber}");
                if (id != DarkDustSpirit) // its own Summon effect destroys other face-up monsters
                    Check($"{spirit.Name} ({id}): its Normal Summon returns no other card to the hand",
                        o.TryFindMonster(bystander, out _) && !o.Hand.Contains(bystander),
                        $"bystanderOnField={o.TryFindMonster(bystander, out _)}");
            }
        }

        /// <summary>
        /// Flip Summoned → returns; a face-up copy that was neither Summoned nor flipped this turn stays on the field
        /// (Tsukuyomi's "if this card was Normal Summoned or flipped face-up this turn").
        /// </summary>
        static void FlipSummonReturnsAndOldSpiritStays(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var set = PlaceMonster(engine, p, Otohime, 0, BattlePosition.Defense, false);
            var old = PlaceMonster(engine, p, Tsukuyomi, 1, BattlePosition.Attack, true);
            var flipped = engine.TryFlipSummon(p, set);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.IsAwaitingEffectTarget) engine.ClearPendingActivation();
            EndTurn(engine);
            Check("Otohime: Flip Summoned → returns to the hand during the End Phase",
                reached && flipped && p.Hand.Contains(set) && !p.TryFindMonster(set, out _),
                $"reached={reached} flipped={flipped} inHand={p.Hand.Contains(set)}");
            Check("Tsukuyomi: face-up but neither Summoned nor flipped this turn → stays on the field",
                p.TryFindMonster(old, out _) && !p.Hand.Contains(old),
                $"onField={p.TryFindMonster(old, out _)} inHand={p.Hand.Contains(old)}");
        }
    }
}
