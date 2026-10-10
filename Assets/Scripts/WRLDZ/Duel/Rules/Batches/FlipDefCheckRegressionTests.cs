using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// FlipDefCheck batch: Acid Trap Hole. It used to compile through the generic PSCT "destroy it" fallback to
    /// "destroy any monster" (no face-down filter, no flip, no DEF check, no return face-down). Compile shape,
    /// mini-duels through the real engine entry points (TryActivateSpellTrap, TrySelectEffectTarget, chain
    /// responses) and fail-closed near misses of the fallback. Runtime: <see cref="FlipDefCheckRuntime"/>.
    /// </summary>
    public static class FlipDefCheckRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int AcidTrapHole = 41356845;
        const int MysticalSpaceTyphoon = 5318639;
        const int GiantSoldierOfStone = 13039848; // Normal 1300/2000 (the DEF boundary)
        const int GaiaTheFierceKnight = 6368038;  // Normal 2300/2100
        const int PoisonMummy = 43716289;         // FLIP: Inflict 500 damage to your opponent. 1000/1800

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("FlipDefCheck: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            TargetAndDefCheckDuels(db, pDeck, aDeck, Check);
            FlipEffectDuels(db, pDeck, aDeck, Check);
            AiDuels(db, pDeck, aDeck, Check);
            NearMisses(Check);
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var def = db.Get(AcidTrapHole);
            var prog = CardTextEffectCompiler.Compile(def);
            Check("Acid Trap Hole FullyCompiled: one Activate clause, face-down target on either field, DEF 2000",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                            c.Action == EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown &&
                                            c.Zone == EffectZoneFilter.FieldFaceDownMonsters &&
                                            c.ConditionAmount == 2000 && c.Amount <= 1 && c.TargetCount <= 1 &&
                                            c.RequiresTargetChoice && c.IsPsctTarget),
                Describe(prog));
            Check("Acid Trap Hole no longer compiles to a plain Destroy",
                prog != null && !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Destroy),
                Describe(prog));
            Check("Acid Trap Hole classifies Implemented",
                CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented);
            Check("FlipFaceUpDestroyIfDefLeqElseSetFaceDown maps to the Destroy resolution kind",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown }) ==
                EffectResolutionKind.Destroy);
            // TargetedClassics Soft: FieldFaceDownMonsters maps to FaceDownMonsterOnField (not AnyMonsterOnField,
            // which would list face-up monsters). CollectLegalTargets mirrors the text path's face-down filter, and
            // DuelUI's field-glance picker uses the same kind for Nobleman / Acid Trap Hole.
            Check("Acid Trap Hole maps to FaceDownMonsterOnField (face-down-only legacy picker)",
                SpellTrapEffects.GetTargetKind(def) == EffectTargetKind.FaceDownMonsterOnField,
                SpellTrapEffects.GetTargetKind(def).ToString());
        }

        // ───────────────────────────── targets and the DEF check ─────────────────────────────

        static void TargetAndDefCheckDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Only face-down monsters are legal, on either side of the field.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var oSet = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var oAtk = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var oDef = PlaceMonster(e, o, GiantSoldierOfStone, 2, BattlePosition.Defense, true);
                var pSet = PlaceMonster(e, p, GaiaTheFierceKnight, 0, BattlePosition.Defense, false);
                var ath = PlaceSetTrap(e, p, AcidTrapHole, 0);
                var activated = e.TryActivateSpellTrap(p, ath, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Acid Trap Hole: targets face-down monsters on either field, never a face-up one",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 &&
                    legal.Contains(oSet) && legal.Contains(pSet) && !legal.Contains(oAtk) && !legal.Contains(oDef),
                    $"activated={activated} legal={string.Join(",", legal.Select(t => t.Name + (t.FaceUp ? "(up)" : "(set)")))}");

                // DEF exactly 2000: flipped face-up, then destroyed.
                var ok = Drive(e, oSet);
                Check("Acid Trap Hole: DEF 2000 (the boundary) is flipped face-up, then destroyed",
                    ok && Where(e, oSet) == "O.gy" && Where(e, ath) == "P.gy" &&
                    Where(e, oAtk) == "O.mz" && Where(e, oDef) == "O.mz" && Where(e, pSet) == "P.mz" &&
                    !pSet.FaceUp,
                    $"ok={ok} target={Where(e, oSet)} ath={Where(e, ath)} gaia={Where(e, oAtk)} pSet={Where(e, pSet)} links={e.Chain.Count}");
            }

            // DEF above 2000: flipped face-up, then returned face-down; it stays on the field.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Defense, false);
                var ath = PlaceSetTrap(e, p, AcidTrapHole, 0);
                var lines = new List<string>();
                e.OnLog += lines.Add;
                var activated = e.TryActivateSpellTrap(p, ath, fromHand: false);
                var ok = activated && Drive(e, gaia);
                Check("Acid Trap Hole: DEF 2100 is flipped face-up, then returned face-down (not destroyed)",
                    ok && Where(e, gaia) == "O.mz" && !gaia.FaceUp && gaia.Position == BattlePosition.Defense &&
                    Where(e, ath) == "P.gy" &&
                    lines.Any(l => l.Contains("face-up Defense Position")) &&
                    lines.Any(l => l.Contains("returned face-down")),
                    $"ok={ok} gaia={Where(e, gaia)} faceUp={gaia.FaceUp} pos={gaia.Position} ath={Where(e, ath)}");
            }

            // Your own face-down monster is a legal target too.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var mine = PlaceMonster(e, p, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var ath = PlaceSetTrap(e, p, AcidTrapHole, 0);
                var activated = e.TryActivateSpellTrap(p, ath, fromHand: false);
                var ok = activated && Drive(e, mine);
                Check("Acid Trap Hole: your own face-down DEF 2000 monster is flipped and destroyed",
                    ok && Where(e, mine) == "P.gy", $"ok={ok} mine={Where(e, mine)}");
            }

            // No face-down monster anywhere: it cannot be activated (the old program could hit any monster).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, e.Opponent, GiantSoldierOfStone, 0, BattlePosition.Defense, true);
                PlaceMonster(e, e.Opponent, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, GiantSoldierOfStone, 0, BattlePosition.Attack, true);
                var ath = PlaceSetTrap(e, p, AcidTrapHole, 0);
                Check("Acid Trap Hole: cannot be activated with only face-up monsters on the field",
                    !e.CanActivateSpellTrap(p, ath, fromHand: false) && Where(e, ath) == "P.stz" && !ath.FaceUp);
            }
        }

        // ───────────────────────────── FLIP effects ─────────────────────────────

        static void FlipEffectDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The FLIP effect of the flipped monster activates after the DEF check, even though it was destroyed.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var mummy = PlaceMonster(e, e.Opponent, PoisonMummy, 0, BattlePosition.Defense, false);
                var ath = PlaceSetTrap(e, p, AcidTrapHole, 0);
                var lp = p.LifePoints;
                var lines = new List<string>();
                e.OnLog += lines.Add;
                var activated = e.TryActivateSpellTrap(p, ath, fromHand: false);
                var ok = activated && Drive(e, mummy);
                var destroyedAt = lines.FindIndex(l => l.Contains("destroyed") && l.Contains(mummy.Name));
                var flipAt = lines.FindIndex(l => l.Contains(mummy.Name + " Flip effect"));
                Check("Acid Trap Hole: a destroyed FLIP monster still activates its FLIP effect, after the DEF check",
                    ok && Where(e, mummy) == "O.gy" && p.LifePoints == lp - 500 &&
                    destroyedAt >= 0 && flipAt > destroyedAt,
                    $"ok={ok} mummy={Where(e, mummy)} lp={lp}->{p.LifePoints} destroyedAt={destroyedAt} flipAt={flipAt}");
            }

            // Returned face-down: the FLIP effect still activates.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var mummy = PlaceMonster(e, e.Opponent, PoisonMummy, 0, BattlePosition.Defense, false);
                mummy.OriginalDefOverride = 2100; // above the threshold
                var ath = PlaceSetTrap(e, p, AcidTrapHole, 0);
                var lp = p.LifePoints;
                var activated = e.TryActivateSpellTrap(p, ath, fromHand: false);
                var ok = activated && Drive(e, mummy);
                Check("Acid Trap Hole: a FLIP monster returned face-down still activates its FLIP effect",
                    ok && Where(e, mummy) == "O.mz" && !mummy.FaceUp && p.LifePoints == lp - 500,
                    $"ok={ok} mummy={Where(e, mummy)} faceUp={mummy.FaceUp} lp={lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── AI ─────────────────────────────

        static void AiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI chains its Set Acid Trap Hole to Mystical Space Typhoon and aims it at the player's
            // face-down monster it destroys, not at its own.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var ath = PlaceSetTrap(e, o, AcidTrapHole, 0);
                var mst = PutInHand(e, p, MysticalSpaceTyphoon);
                var played = e.TryActivateSpellTrap(p, mst, fromHand: true);
                var ok = played && Drive(e, ath);
                Check("AI: chains Acid Trap Hole to MST and destroys the player's face-down monster, not its own",
                    ok && Where(e, mine) == "P.gy" && Where(e, theirs) == "O.mz" && !theirs.FaceUp &&
                    Where(e, ath) == "O.gy",
                    $"ok={ok} mine={Where(e, mine)} theirs={Where(e, theirs)} ath={Where(e, ath)} mst={Where(e, mst)}");
            }

            // With only its own face-down monsters, the AI picks the one that survives the check.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var o = e.Opponent;
                var low = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var high = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Defense, false);
                var clause = CardTextEffectCompiler.Compile(db.Get(AcidTrapHole)).ClauseList[0];
                var pick = FlipDefCheckRuntime.AutoPick(e, o, clause, new List<CardInstance> { low, high });
                Check("AI: with only its own face-down monsters, picks one whose DEF is above 2000",
                    pick == high, pick?.Name ?? "null");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            CompiledCardProgram Trap(int id, string desc) => CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "ARGON FlipDefCheck near miss", type = "Trap Card", race = "Normal", desc = desc
            });

            bool Claims(CompiledCardProgram prog, EffectActionKind action) =>
                prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);

            {
                var prog = Trap(990142901,
                    "Target 1 face-down Defense Position monster on the field; flip it face-up, then destroy it if its " +
                    "DEF is 2000 or less, or return it face-down if its DEF is more than 1500.");
                Check("Near miss: two different DEF numbers is not the Acid Trap Hole atom and stays unparsed",
                    !prog.FullyCompiled && !Claims(prog, EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown) &&
                    !Claims(prog, EffectActionKind.Destroy), Describe(prog));
            }
            {
                var prog = Trap(990142902,
                    "Target 1 face-down Defense Position monster your opponent controls; flip it face-up, then destroy " +
                    "it if its DEF is 2000 or less, or return it face-down if its DEF is more than 2000.");
                Check("Near miss: an opponent-only target is not the Acid Trap Hole atom and stays unparsed",
                    !prog.FullyCompiled && !Claims(prog, EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown) &&
                    !Claims(prog, EffectActionKind.Destroy), Describe(prog));
            }
            {
                var prog = Trap(990142903, "Target 1 monster on the field; destroy it if its ATK is 1500 or less.");
                Check("Near miss: the generic PSCT destroy does not compile a conditional destroy",
                    !prog.FullyCompiled && !Claims(prog, EffectActionKind.Destroy), Describe(prog));
            }
            {
                var prog = Trap(990142904, "Target 1 face-down monster on the field; flip it face-up, then destroy it.");
                Check("Near miss: the generic PSCT destroy does not compile a destroy after another step",
                    !prog.FullyCompiled && !Claims(prog, EffectActionKind.Destroy), Describe(prog));
            }
            {
                var prog = Trap(990142905, "Target 1 monster on the field; destroy that target.");
                Check("Control: a plain targeted destroy still compiles through the generic PSCT destroy",
                    prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Destroy &&
                                                c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetChoice),
                    Describe(prog));
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

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

        /// <summary>Every target prompt picks <paramref name="target"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, CardInstance target)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var ok = e.TrySelectEffectTarget(target);
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

            return picked && !e.IsAwaitingEffectTarget && !e.IsAwaitingResponse && !e.Chain.HasLinks;
        }

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/{c.Amount}/{c.ConditionAmount}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
