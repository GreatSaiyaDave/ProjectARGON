using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "BanishedReturn": D.D. Scout Plane is Special Summoned in face-up Attack Position during the End
    /// Phase of the turn it was banished face-up. It used to compile to a targeted either-GY revive (the condition
    /// before the colon was dropped), and the End Phase never looked at the banished pile, so it never came back.
    /// Turn 1 is always the Player's, turn 2 the AI's.
    /// </summary>
    public static class BanishedReturnRegressionTests
    {
        const int Plane = 3773196;   // Machine Lv2 800/1200
        const int Celtic = 91152256; // filler monster

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("BanishedReturn: " + name, ok, detail ?? "");

            CompileShape(db, Check);
            FieldCopyRevivesNothing(db, pDeck, aDeck, Check);
            ReturnsDuringThatEndPhase(db, pDeck, aDeck, Check);
            ReturnsToItsOwner(db, pDeck, aDeck, Check);
            OnlyAFaceUpBanishThisTurnCounts(db, pDeck, aDeck, Check);
            OncePerTurn(db, pDeck, aDeck, Check);
            NoFreeZoneSendsItToTheGy(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Start(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static void EndTurn(DuelEngine e)
        {
            e.TryEndTurnSafe(e.TurnPlayer);
            if (e.IsAwaitingResponse) e.PassResponse();
        }

        static bool OnFieldFaceUpAttack(DuelistState who, CardInstance c) =>
            who.TryFindMonster(c, out _) && c.FaceUp && c.Position == BattlePosition.Attack && c.WasSpecialSummoned;

        static CompiledCardProgram CompileMonsterText(int id, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Near-miss Scout Plane", type = "Effect Monster", frameType = "effect",
                race = "Machine", attribute = "DARK", level = 2, desc = text
            });

        static string Where(DuelEngine e, DuelistState who, CardInstance c) =>
            $"turn={e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase} field={who.TryFindMonster(c, out _)} " +
            $"faceUp={c.FaceUp} pos={c.Position} banished={who.Banished.Contains(c)} gy={who.Graveyard.Contains(c)} " +
            $"stamp={c.BanishedTurnNumber}";

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShape(CardDatabase db, Action<string, bool, string> Check)
        {
            var def = db.Get(Plane);
            var prog = CardTextEffectCompiler.Compile(def);
            var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            Check("Scout Plane: FullyCompiled, one mandatory once-per-turn End Phase return from the banished pile",
                prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.SpecialSummonThisFromBanished &&
                c.Side == EffectSide.Controller && c.RequiresBanishedThisTurn && c.OncePerTurn && c.SummonInAttack &&
                !c.RequiresTargetChoice && c.Zone == EffectZoneFilter.None && !c.IsOptional && c.MakesChainLink,
                c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
            Check("Scout Plane: classified Implemented",
                CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(def).ToString());

            // Without "Once per turn" the same return compiles, without the once-per-turn gate.
            var plain = CompileMonsterText(990011,
                "During the End Phase, if this card is currently banished, and was banished this turn: " +
                "Special Summon it in face-up Attack Position.");
            var pc = plain != null && plain.ClauseList.Count == 1 ? plain.ClauseList[0] : null;
            Check("\"During the End Phase, …\" compiles to the same return without OncePerTurn",
                plain != null && plain.FullyCompiled && pc != null &&
                pc.Action == EffectActionKind.SpecialSummonThisFromBanished && pc.RequiresBanishedThisTurn &&
                !pc.OncePerTurn,
                pc == null ? $"clauses={plain?.ClauseList.Count}" : EffectVocabulary.Describe(pc));

            // Near miss: the atom takes only the whole sentence, never the head of a longer one.
            var longer = CompileMonsterText(990012,
                "Once per turn, during the End Phase, if this card is currently banished, and was banished this " +
                "turn: Special Summon it in face-up Attack Position, then draw 1 card.");
            Check("a longer sentence is not taken by the Scout Plane atom",
                longer == null || !longer.ClauseList.Exists(x =>
                    x != null && x.Action == EffectActionKind.SpecialSummonThisFromBanished),
                longer == null ? "null" : string.Join("; ", longer.ClauseList.ConvertAll(EffectVocabulary.Describe)));
        }

        /// <summary>
        /// The reported bug, field half: a face-up Scout Plane on the field during the End Phase must not revive
        /// anything from either GY (the old program was a targeted either-GY revive).
        /// </summary>
        static void FieldCopyRevivesNothing(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var plane = PlaceMonster(e, p, Plane, 2, BattlePosition.Attack, true);
            p.Graveyard.Add(e.CreateCardInstance(Celtic));
            o.Graveyard.Add(e.CreateCardInstance(Celtic));
            EndTurn(e);
            Check("a face-up Scout Plane on the field revives nothing from either GY in the End Phase",
                e.TurnPlayer == o && p.Graveyard.Count == 1 && o.Graveyard.Count == 1 &&
                p.MonsterCount == 1 && o.MonsterCount == 0 && p.TryFindMonster(plane, out _) &&
                !e.IsAwaitingEffectTarget,
                $"pGy={p.Graveyard.Count} oGy={o.Graveyard.Count} pMon={p.MonsterCount} oMon={o.MonsterCount} " +
                Where(e, p, plane));
        }

        /// <summary>
        /// The reported bug, banished half: banished face-up from the field, the hand, or the GY this turn, it is
        /// Special Summoned in face-up Attack Position during that End Phase — the Player's own, and the AI's
        /// copy during the Player's End Phase ("during the End Phase" is either player's).
        /// </summary>
        static void ReturnsDuringThatEndPhase(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var from in new[] { "field", "hand", "GY" })
            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                CardInstance plane;
                if (from == "field")
                    plane = PlaceMonster(e, p, Plane, 0, BattlePosition.Defense, true);
                else if (from == "hand")
                    plane = PutInHand(e, p, Plane);
                else
                {
                    plane = e.CreateCardInstance(Plane);
                    p.Graveyard.Add(plane);
                }

                e.BanishCard(p, plane);
                var banishedOk = p.Banished.Contains(plane) && plane.BanishedTurnNumber == e.TurnNumber;
                EndTurn(e);
                Check($"the Player's Scout Plane banished from the {from} on turn 1 returns in that End Phase",
                    banishedOk && e.TurnNumber == 2 && OnFieldFaceUpAttack(p, plane) && !p.Banished.Contains(plane),
                    $"banishedOk={banishedOk} " + Where(e, p, plane));
            }

            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                var plane = PlaceMonster(e, o, Plane, 1, BattlePosition.Attack, true);
                e.BanishCard(o, plane);
                EndTurn(e);
                Check("the AI's Scout Plane banished during the Player's turn returns in the Player's End Phase",
                    e.TurnPlayer == o && OnFieldFaceUpAttack(o, plane) && !o.Banished.Contains(plane),
                    Where(e, o, plane));
            }
        }

        /// <summary>
        /// A banished card goes to its owner's pile, so a Scout Plane the Player controlled but the AI owns
        /// returns to the AI's field.
        /// </summary>
        static void ReturnsToItsOwner(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var plane = PlaceMonster(e, p, Plane, 0, BattlePosition.Attack, true);
            plane.ControlOwner = o; // taken from the AI (Change of Heart style)
            e.BanishCard(p, plane);
            var inOwnersPile = o.Banished.Contains(plane) && !p.Banished.Contains(plane);
            EndTurn(e);
            Check("a borrowed Scout Plane goes to its owner's banished pile and returns to its owner's field",
                inOwnersPile && OnFieldFaceUpAttack(o, plane) && !p.TryFindMonster(plane, out _),
                $"inOwnersPile={inOwnersPile} " + Where(e, o, plane));
        }

        /// <summary>
        /// "if this card is currently banished, and was banished this turn": banished face-down (Lightforce
        /// Sword), or banished during an earlier turn, it stays banished.
        /// </summary>
        static void OnlyAFaceUpBanishThisTurnCounts(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                var plane = PutInHand(e, o, Plane);
                e.BanishCard(o, plane);
                plane.FaceUp = false; // Lightforce Sword banishes face-down
                EndTurn(e);
                Check("a Scout Plane banished face-down stays banished",
                    o.Banished.Contains(plane) && !o.TryFindMonster(plane, out _), Where(e, o, plane));
            }

            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                EndTurn(e); // turn 2: the AI's
                var plane = e.CreateCardInstance(Plane);
                plane.FaceUp = true;
                plane.BanishedTurnNumber = e.TurnNumber - 1; // banished during the Player's turn 1
                o.Banished.Add(plane);
                EndTurn(e);
                Check("a Scout Plane banished during an earlier turn stays banished",
                    e.TurnNumber == 3 && o.Banished.Contains(plane) && !o.TryFindMonster(plane, out _),
                    Where(e, o, plane));
            }
        }

        /// <summary>
        /// "Once per turn": a copy that already returned this turn and is banished again stays banished; another
        /// copy still returns (the limit is per copy, ygopro RegisterFlagEffect on the card).
        /// </summary>
        static void OncePerTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var plane = PutInHand(e, p, Plane);
            e.BanishCard(p, plane);
            BanishedReturnRuntime.FireEndPhase(e, p);
            var first = OnFieldFaceUpAttack(p, plane);
            e.BanishCard(p, plane);
            var other = PutInHand(e, p, Plane);
            e.BanishCard(p, other);
            BanishedReturnRuntime.FireEndPhase(e, p);
            Check("a copy returns once per turn; banished again the same turn it stays banished, another copy returns",
                first && p.Banished.Contains(plane) && !p.TryFindMonster(plane, out _) &&
                OnFieldFaceUpAttack(p, other),
                $"first={first} " + Where(e, p, plane) + " | other: " + Where(e, p, other));
        }

        /// <summary>ygopro s.operation: with no free Monster Zone the Scout Plane is sent to the GY.</summary>
        static void NoFreeZoneSendsItToTheGy(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            for (var z = 0; z < p.MonsterZones.Length; z++)
                PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true);
            var plane = PutInHand(e, p, Plane);
            e.BanishCard(p, plane);
            BanishedReturnRuntime.FireEndPhase(e, p);
            Check("with no free Monster Zone the Scout Plane is sent to the GY",
                p.Graveyard.Contains(plane) && !p.Banished.Contains(plane) && !p.TryFindMonster(plane, out _) &&
                p.MonsterCount == p.MonsterZones.Length,
                Where(e, p, plane));
        }
    }
}
