using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// "Target 1 monster your opponent controls" includes face-down monsters. CardTextEffectCompiler.ParseActivationTarget
    /// mapped it to OppFaceUpMonsters, so Swarm of Scarabs, Thousand Knives and Golem Sentry could not target a Set
    /// monster (their Lua: SelectTarget(aux.TRUE / Card.IsAbleToHand, 0, LOCATION_MZONE)). It now maps to OppMonsters;
    /// a printed "face-up" keeps OppFaceUpMonsters, and "target 1 face-up monster on the field" keeps its face-up filter
    /// (RequiresTargetFaceUp; Dark Core). The legacy Spell/Trap mirror (SpellTrapEffects.MapCompiledTargetKind /
    /// CollectLegalTargets) maps OppMonsters to the face-down-inclusive OppMonster list, which also lets Spellbinding
    /// Circle bind a Set monster (Lua aux.AddPersistentProcedure(c, 1, nil)).
    /// </summary>
    public static class OppMonsterTargetsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int SwarmOfScarabs = 15383415;
        const int GolemSentry = 52323207;
        const int ThousandKnives = 63391643;
        const int DarkCore = 70231910;
        const int SpellbindingCircle = 18807108;
        const int BrainControl = 87910978;
        const int DarkMagician = 46986414;
        const int GiantSoldierOfStone = 13039848; // Normal 1300/2000
        const int GaiaTheFierceKnight = 6368038;  // Normal 2300/2100
        const int CelticGuardian = 91152256;      // Normal 1400/1200

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("OppMonsterTargets: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            ThousandKnivesDuels(db, pDeck, aDeck, Check);
            FlipSummonDuels(db, pDeck, aDeck, Check);
            SpellbindingCircleDuel(db, pDeck, aDeck, Check);
            FaceUpOnlyControls(db, pDeck, aDeck, Check);
            Synthetics(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var def = db.Get(SwarmOfScarabs);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Swarm of Scarabs: Flip Summon destroy targets OppMonsters (face-down too)",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ThisCardSummoned &&
                                                c.RequiresThisFlipSummoned && c.Action == EffectActionKind.Destroy &&
                                                c.Zone == EffectZoneFilter.OppMonsters && c.RequiresTargetChoice &&
                                                c.IsPsctTarget && !c.RequiresTargetFaceUp) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var def = db.Get(GolemSentry);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Golem Sentry: Flip Summon return-to-hand targets OppMonsters (face-down too)",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ThisCardSummoned &&
                                                c.RequiresThisFlipSummoned && c.Action == EffectActionKind.ReturnToHand &&
                                                c.Zone == EffectZoneFilter.OppMonsters && c.RequiresTargetChoice &&
                                                !c.RequiresTargetFaceUp) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var def = db.Get(ThousandKnives);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Thousand Knives: one Activate Destroy on OppMonsters, needs a face-up \"Dark Magician\"",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                                c.Action == EffectActionKind.Destroy &&
                                                c.Zone == EffectZoneFilter.OppMonsters && c.RequiresTargetChoice &&
                                                c.IsPsctTarget && c.RequiresControllerNamedCard &&
                                                c.RequiresFaceUpName == "Dark Magician" && !c.RequiresTargetFaceUp) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Describe(prog));
                // The legacy Spell/Trap mirror seats Thousand Knives: it must use the face-down-inclusive list.
                Check("Thousand Knives: legacy target kind is OppMonster (not the face-up-only OppFaceUpMonster)",
                    SpellTrapEffects.GetTargetKind(def) == EffectTargetKind.OppMonster,
                    SpellTrapEffects.GetTargetKind(def).ToString());
            }
            {
                var def = db.Get(DarkCore);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Dark Core: \"target 1 face-up monster on the field\" keeps its face-up filter",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Banish &&
                                                c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetFaceUp &&
                                                c.RequiresDiscardCost && c.RequiresTargetChoice) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var def = db.Get(SpellbindingCircle);
                Check("Spellbinding Circle: legacy target kind is OppMonster (face-down too)",
                    SpellTrapEffects.GetTargetKind(def) == EffectTargetKind.OppMonster,
                    SpellTrapEffects.GetTargetKind(def).ToString());
            }
            {
                var def = db.Get(BrainControl);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Brain Control: \"face-up monster your opponent controls\" stays OppFaceUpMonsters",
                    prog != null && prog.ClauseList.Exists(c => c != null && c.RequiresTargetChoice &&
                                                                c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                    Describe(prog));
            }
        }

        // ───────────────────────────── Thousand Knives ─────────────────────────────

        static void ThousandKnivesDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Both the Set monster and the face-up one are legal; the Set one is destroyed.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, DarkMagician, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var tk = PutInHand(e, p, ThousandKnives);
                var legacy = SpellTrapEffects.CollectLegalTargets(e, p, EffectTargetKind.OppMonster, tk);
                Check("Thousand Knives: the legacy OppMonster list holds the Set monster and the face-up one",
                    legacy.Count == 2 && legacy.Contains(set) && legacy.Contains(gaia),
                    Names(legacy));
                var activated = e.TryActivateSpellTrap(p, tk, fromHand: true);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Thousand Knives: target prompt offers the opponent's face-down monster",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(set) &&
                    legal.Contains(gaia),
                    $"activated={activated} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, set);
                Check("Thousand Knives: destroys the targeted face-down monster, and only it",
                    ok && Where(e, set) == "O.gy" && Where(e, gaia) == "O.mz" && Where(e, tk) == "P.gy",
                    $"ok={ok} set={Where(e, set)} gaia={Where(e, gaia)} tk={Where(e, tk)} links={e.Chain.Count}");
            }

            // A lone face-down monster: Thousand Knives can be activated (before, it had no legal target).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, DarkMagician, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, CelticGuardian, 2, BattlePosition.Defense, false);
                var tk = PutInHand(e, p, ThousandKnives);
                var can = e.CanActivateSpellTrap(p, tk, fromHand: true);
                var activated = can && e.TryActivateSpellTrap(p, tk, fromHand: true);
                // A lone legal target may resolve with no prompt.
                if (e.IsAwaitingEffectTarget || e.IsAwaitingResponse) Drive(e, set);
                Check("Thousand Knives: with only a Set monster on the opponent's field, it activates and destroys it",
                    can && activated && Where(e, set) == "O.gy" && Where(e, tk) == "P.gy" &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"can={can} activated={activated} set={Where(e, set)} tk={Where(e, tk)}");
            }

            // Still needs a face-up "Dark Magician": a Set one does not count (Lua cfilter IsFaceup).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, DarkMagician, 0, BattlePosition.Defense, false);
                PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var tk = PutInHand(e, p, ThousandKnives);
                Check("Thousand Knives: a face-down \"Dark Magician\" does not enable it",
                    !e.CanActivateSpellTrap(p, tk, fromHand: true));
            }
        }

        // ───────────────────────────── Flip Summon triggers ─────────────────────────────

        static void FlipSummonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Swarm of Scarabs: Flip Summoned, it destroys the opponent's Set monster.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bug = PlaceMonster(e, p, SwarmOfScarabs, 0, BattlePosition.Defense, false);
                var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(p, bug) && bug.FaceUp;
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Swarm of Scarabs: the Flip Summon target prompt offers the opponent's face-down monster",
                    flipped && e.IsAwaitingEffectTarget && legal.Contains(set) && legal.Contains(gaia) &&
                    !legal.Contains(bug),
                    $"flipped={flipped} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, set);
                Check("Swarm of Scarabs: destroys the targeted face-down monster, and only it",
                    ok && Where(e, set) == "O.gy" && Where(e, gaia) == "O.mz" && Where(e, bug) == "P.mz",
                    $"ok={ok} set={Where(e, set)} gaia={Where(e, gaia)} bug={Where(e, bug)}");
            }

            // Swarm of Scarabs with a lone Set monster: before, the trigger had no legal target at all.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bug = PlaceMonster(e, p, SwarmOfScarabs, 0, BattlePosition.Defense, false);
                var set = PlaceMonster(e, o, CelticGuardian, 3, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, bug) && bug.FaceUp;
                if (e.IsAwaitingEffectTarget || e.IsAwaitingResponse) Drive(e, set);
                Check("Swarm of Scarabs: with only a Set monster on the opponent's field, destroys it",
                    flipped && Where(e, set) == "O.gy" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"flipped={flipped} set={Where(e, set)}");
            }

            // Golem Sentry: Flip Summoned, it returns the opponent's Set monster to the hand.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var golem = PlaceMonster(e, p, GolemSentry, 0, BattlePosition.Defense, false);
                var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(p, golem) && golem.FaceUp;
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                var ok = flipped && legal.Contains(set) && Drive(e, set);
                Check("Golem Sentry: returns the targeted face-down monster to its owner's hand",
                    ok && Where(e, set) == "O.hand" && Where(e, gaia) == "O.mz" && Where(e, golem) == "P.mz",
                    $"flipped={flipped} legal={Names(legal)} set={Where(e, set)} gaia={Where(e, gaia)}");
            }
        }

        // ───────────────────────────── Spellbinding Circle (legacy mirror) ─────────────────────────────

        static void SpellbindingCircleDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
            var circle = PlaceSetTrap(e, p, SpellbindingCircle, 0);
            var can = e.CanActivateSpellTrap(p, circle, fromHand: false);
            var activated = can && e.TryActivateSpellTrap(p, circle, fromHand: false);
            if (e.IsAwaitingEffectTarget || e.IsAwaitingResponse) Drive(e, set);
            var bound = TextEffectRuntime.AttackForbiddenByEffect(e, set) &&
                        TextEffectRuntime.PositionChangeForbiddenByEffect(set);
            Check("Spellbinding Circle: binds the opponent's lone face-down monster",
                can && activated && bound && Where(e, circle) == "P.stz" && circle.FaceUp && Where(e, set) == "O.mz",
                $"can={can} activated={activated} bound={bound} circle={Where(e, circle)} set={Where(e, set)}");
        }

        // ───────────────────────────── face-up-only controls ─────────────────────────────

        static void FaceUpOnlyControls(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Dark Core: "face-up monster on the field": the runtime target list drops Set monsters on both sides.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var oSet = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var oUp = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var pSet = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Defense, false);
                var pUp = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                var core = PutInHand(e, p, DarkCore);
                var clause = CardTextEffectCompiler.Compile(db.Get(DarkCore)).ClauseList
                    .First(c => c != null && c.Action == EffectActionKind.Banish);
                var list = TextEffectRuntime.CollectTargets(e, p, clause, core);
                Check("Dark Core: only face-up monsters on either field are legal targets",
                    list.Count == 2 && list.Contains(oUp) && list.Contains(pUp) && !list.Contains(oSet) &&
                    !list.Contains(pSet),
                    Names(list));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, e.Opponent, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var core = PutInHand(e, p, DarkCore);
                PutInHand(e, p, CelticGuardian); // the discard
                Check("Dark Core: cannot be activated when the only monster is face-down",
                    !e.CanActivateSpellTrap(p, core, fromHand: true));
            }

            // Brain Control: "face-up monster your opponent controls" still skips Set monsters.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, e.Opponent, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var bc = PutInHand(e, p, BrainControl);
                Check("Brain Control: cannot be activated when the opponent's only monster is face-down",
                    !e.CanActivateSpellTrap(p, bc, fromHand: true));
            }
        }

        // ───────────────────────────── synthetic phrasings ─────────────────────────────

        static void Synthetics(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            CompiledCardProgram Spell(int id, string desc) => CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "ARGON OppMonsterTargets synthetic", type = "Spell Card", race = "Normal", desc = desc
            });

            EffectClause Destroy(CompiledCardProgram prog) =>
                prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == EffectActionKind.Destroy);

            {
                var prog = Spell(990152901, "Target 1 monster your opponent controls; destroy that target.");
                var c = Destroy(prog);
                Check("Synthetic \"target 1 monster your opponent controls\" → OppMonsters",
                    prog.FullyCompiled && c != null && c.Zone == EffectZoneFilter.OppMonsters && c.RequiresTargetChoice &&
                    !c.RequiresTargetFaceUp, Describe(prog));
            }
            {
                var prog = Spell(990152902, "Target 1 face-up monster your opponent controls; destroy that target.");
                var c = Destroy(prog);
                Check("Synthetic \"target 1 face-up monster your opponent controls\" → OppFaceUpMonsters",
                    prog.FullyCompiled && c != null && c.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                    c.RequiresTargetChoice, Describe(prog));
            }
            {
                var prog = Spell(990152903, "Target 1 monster on the field; destroy that target.");
                var c = Destroy(prog);
                Check("Synthetic \"target 1 monster on the field\" → FieldAnyMonster, face-down too",
                    prog.FullyCompiled && c != null && c.Zone == EffectZoneFilter.FieldAnyMonster &&
                    c.RequiresTargetChoice && !c.RequiresTargetFaceUp, Describe(prog));
            }
            {
                var prog = Spell(990152904, "Target 1 face-up monster on the field; destroy that target.");
                var c = Destroy(prog);
                Check("Synthetic \"target 1 face-up monster on the field\" → FieldAnyMonster + RequiresTargetFaceUp",
                    prog.FullyCompiled && c != null && c.Zone == EffectZoneFilter.FieldAnyMonster &&
                    c.RequiresTargetChoice && c.RequiresTargetFaceUp, Describe(prog));

                // The face-up filter holds in the runtime list, and the legacy mirror leaves it to that list.
                var e = FreshCleared(db, pDeck, aDeck);
                var set = PlaceMonster(e, e.Opponent, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var up = PlaceMonster(e, e.Opponent, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var list = c != null ? TextEffectRuntime.CollectTargets(e, e.Player, c, null) : new List<CardInstance>();
                Check("Synthetic face-up field destroy: a Set monster is not a legal target",
                    list.Contains(up) && !list.Contains(set), Names(list));
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

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/faceUp={c.RequiresTargetFaceUp}/tgt={c.RequiresTargetChoice}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
