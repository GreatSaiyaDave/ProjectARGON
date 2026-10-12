using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TargetingTruth batch (v177): one answer to "does this clause target?" (<see cref="TargetingTruthRuntime"/>).
    /// Fissure vs the die-roll Archfiends (no roll), real targets still roll, Lord of D. vs Fissure, Different Dimension
    /// Dragon / Metalsilver Armor unchanged, the audited old-text "select" clauses (Lua Target vs Select), Gora Turtle of
    /// Illusion and Big Burn — compile shapes, near misses and mini-duels through the real engine entry points.
    /// </summary>
    public static class TargetingTruthRegressionTests
    {
        const int Fissure = 66788016;              // Normal Spell: lowest-ATK face-up opp monster (Lua Select)
        const int Terrorking = 35975813;           // die 2 or 5 negates
        const int Vilepawn = 73219648;             // die 3 negates
        const int Darkbishop = 35798491;           // protects every Archfiend; die 3
        const int DiffDimDragon = 50939127;        // cannot be destroyed by S/T effects that do not target it
        const int MetalsilverArmor = 33114323;
        const int ChaosCommandMagician = 72630549;
        const int GoraTurtle = 42868711;
        const int BigBurn = 95472621;
        const int OfferingsToTheDoomed = 19230407; // Quick-Play: target 1 face-up monster; destroy it (PSCT target)
        const int StopDefense = 63102017;          // old text "Select 1 Defense Position monster" (Lua SelectTarget)
        const int HammerShot = 26412047;           // highest-ATK Attack Position monster (Lua Select)
        const int TwoPronged = 83887306;           // old text "Select and destroy" (Lua SelectTarget)
        const int GravediggerGhoul = 82542267;     // old text "Select up to 2 Monster Cards from your opponent's GY"
        const int MonsterReborn = 83764718;
        const int SpellbindingCircle = 18807109;
        const int Mst = 5318639;
        const int LordOfD = 17985575;
        const int PetitDragon = 75356564;          // Dragon 600 ATK
        const int Celtic = 91152256;               // Warrior 1400
        const int MysticalElf = 15025844;          // Spellcaster 800/2000
        const int SkullServant = 32274490;
        const int PetitAngel = 38142739;
        const int SilentSwordsmanLv3 = 1995985;
        const int SoulAbsorption = 68073522;       // ChaosBanishSummons: 500 LP per card in one banish event
        const int ChaosSorcerer = 9596126;         // ChaosBanishSummons: ignition, target 1 face-up monster; banish it

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("TargetingTruth: " + name, ok, detail);

            CompileShapes(db, Check);
            AuditedSelectClauses(db, Check);
            NearMisses(db, Check);
            ArchfiendDuels(db, pDeck, aDeck, Check);
            ProtectionDuels(db, pDeck, aDeck, Check);
            GoraTurtleDuels(db, pDeck, aDeck, Check);
            BigBurnDuels(db, pDeck, aDeck, Check);
            CrossGroupDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [" + string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} rtc={c.RequiresTargetChoice} psct={c.IsPsctTarget} " +
                    $"cdnt={c.ChoiceDoesNotTarget}")) + "]";

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Empty(e);
            return e;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            Empty(e);
            return e;
        }

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone,
            BattlePosition pos = BattlePosition.Attack) => PlaceMonster(e, who, id, zone, pos, true);

        static CardInstance InGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
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

        static bool Offers(DuelEngine e, DuelistState who, CardInstance card) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who && e.PendingResponse.LegalCards.Contains(card);

        static string State(DuelEngine e) =>
            (e.PendingResponse == null
                ? "no window"
                : $"timing={e.PendingResponse.Timing} responder={e.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", e.PendingResponse.LegalCards.Select(c => c?.Name))}]") +
            $" chain={e.Chain.Count} pick={e.IsAwaitingEffectTarget} turn={e.TurnPlayer?.Name} {e.Phase}";

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        /// <summary>Records the die-roll lines of the Archfiend protection.</summary>
        static List<string> RollLog(DuelEngine e)
        {
            var rolls = new List<string>();
            e.OnLog += m =>
            {
                if (m != null && m.Contains("being targeted")) rolls.Add(m);
            };
            return rolls;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var fis = Prog(db, Fissure);
            var f0 = fis?.ClauseList.FirstOrDefault(c => c.RequiresTargetChoice);
            Check("Fissure: lowest-ATK choice, no target (Lua c66788016 Select)",
                fis != null && fis.FullyCompiled && Implemented(db, Fissure) && f0 != null && f0.PickLowestAtk &&
                !TargetingTruthRuntime.ClauseTargets(f0) && !TargetingTruthRuntime.PicksTarget(f0) &&
                TargetingTruthRuntime.PicksWithoutTargeting(f0), Describe(fis));

            foreach (var (id, name, mask) in new[] { (Terrorking, "Terrorking Archfiend", 18), (Vilepawn, "Vilepawn Archfiend", 4) })
            {
                var p = Prog(db, id);
                var die = p?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.DieRollNegateWhenTargeted);
                Check($"{name}: die-roll target protection unchanged",
                    p != null && p.FullyCompiled && Implemented(db, id) && die != null && die.DieNegateFacesMask == mask &&
                    die.Timing == EffectTiming.ContinuousWhileFaceUp, Describe(p));
            }

            var ddd = Prog(db, DiffDimDragon);
            Check("Different Dimension Dragon: shape unchanged",
                ddd != null && ddd.FullyCompiled && Implemented(db, DiffDimDragon) &&
                ddd.ClauseList.Exists(c => c.Action == EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis),
                Describe(ddd));
            var ms = Prog(db, MetalsilverArmor);
            Check("Metalsilver Armor: shape unchanged",
                ms != null && ms.FullyCompiled && Implemented(db, MetalsilverArmor) &&
                ms.ClauseList.Exists(c => c.Action == EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost),
                Describe(ms));
            var ccm = Prog(db, ChaosCommandMagician);
            Check("Chaos Command Magician: shape unchanged",
                ccm != null && ccm.FullyCompiled && Implemented(db, ChaosCommandMagician) &&
                ccm.ClauseList.Exists(c => c.Action == EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis),
                Describe(ccm));

            var gora = Prog(db, GoraTurtle);
            var g = gora?.ClauseList.Count == 1 ? gora.ClauseList[0] : null;
            Check("Gora Turtle of Illusion: Freed's marker for the opponent's Spell OR Trap effects that target this card",
                gora != null && gora.FullyCompiled && Implemented(db, GoraTurtle) && g != null &&
                g.Timing == EffectTiming.ContinuousWhileFaceUp &&
                g.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy && g.Side == EffectSide.Opponent &&
                g.NegateRespondsToSpell && g.NegateRespondsToTrap && g.AffectsThisCardOnly && g.DestroyNegatedCard &&
                !TargetingTruthRuntime.ActivatedClauseTargets(g), Describe(gora));

            var burn = Prog(db, BigBurn);
            var b = burn?.ClauseList.Count == 1 ? burn.ClauseList[0] : null;
            Check("Big Burn: a Chain answer to the opponent's GY-monster target; banish every monster in both GYs",
                burn != null && burn.FullyCompiled && Implemented(db, BigBurn) && b != null &&
                b.Timing == EffectTiming.ChainLinkActivated &&
                b.Action == EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster && b.ChainResponseOnly &&
                ChainLinkAnswersRuntime.IsBatchAnswer(b) &&
                EffectVocabulary.ResolutionOf(b) == EffectResolutionKind.Banish, Describe(burn));

            // The helper on built clauses: a GY pick that does not target (a Lua Select) is not a target; the same pick
            // without the flag is; a Deck pick never is.
            var gySelect = new EffectClause
            {
                Timing = EffectTiming.Activate, Action = EffectActionKind.Banish, Zone = EffectZoneFilter.OppGyMonsters,
                RequiresTargetChoice = true, ChoiceDoesNotTarget = true
            };
            var gyTarget = new EffectClause
            {
                Timing = EffectTiming.Activate, Action = EffectActionKind.Banish, Zone = EffectZoneFilter.OppGyMonsters,
                RequiresTargetChoice = true
            };
            var deckPick = new EffectClause
            {
                Timing = EffectTiming.Activate, Action = EffectActionKind.AddFromDeckToHand,
                Zone = EffectZoneFilter.DeckFieldSpells, RequiresTargetChoice = true
            };
            var implied = new EffectClause
            {
                Timing = EffectTiming.AttackDeclared, Action = EffectActionKind.Destroy, IsPsctTarget = true
            };
            Check("Helper: GY Select is no target; GY select-target is; a Deck pick never; an implied target targets",
                !TargetingTruthRuntime.ClauseTargets(gySelect) && TargetingTruthRuntime.ClauseTargets(gyTarget) &&
                TargetingTruthRuntime.PicksTarget(gyTarget) && !TargetingTruthRuntime.ClauseTargets(deckPick) &&
                TargetingTruthRuntime.ClauseTargets(implied) && !TargetingTruthRuntime.PicksTarget(implied) &&
                !TargetingTruthRuntime.ClauseTargets(null));
        }

        /// <summary>
        /// Every Implemented card whose pick is an old-text "select" (RequiresTargetChoice, no PSCT "target") was audited
        /// against its Lua (v177): SelectTarget → targets; Select / SelectMatchingCard, the lowest / highest picks and Deck
        /// or hand picks → no target.
        /// </summary>
        static void AuditedSelectClauses(CardDatabase db, Chk Check)
        {
            var luaTargets = new[]
            {
                596051, 2694423, 7089711, 13215230, 13409151, 15866454, 16430187, 17449108, 24623598, 28357177,
                33066139, 42647539, 44209392, 46461247, 48659020, 50122883, 52675689, 57409948, 63018132, 63102017,
                63120904, 70138455, 75209824, 75946257, 76297408, 80168720, 82542267, 83887306, 93889755
            };
            var luaSelects = new[]
            {
                66788016, 26412047, 97169186, 77754944, 76075810, 2460565, 2903036, 22431243, 23401839, 26202165,
                32807846, 41142615, 57617178, 71829750, 73431236, 73628505, 78010363, 85489096, 92377303, 95492061,
                96965364
            };
            var wrong = new List<string>();
            foreach (var id in luaTargets.Concat(luaSelects))
            {
                var p = Prog(db, id);
                var picks = p?.ClauseList.Where(c => c != null && c.RequiresTargetChoice && !c.IsPsctTarget).ToList();
                if (p == null || !p.FullyCompiled || picks == null || picks.Count == 0)
                {
                    wrong.Add($"{id}:shape");
                    continue;
                }

                var want = luaTargets.Contains(id);
                if (picks.Exists(c => TargetingTruthRuntime.ClauseTargets(c) != want)) wrong.Add($"{db.Get(id)?.name}:{want}");
            }

            Check("Audit: old-text select clauses follow their Lua (SelectTarget targets, Select does not)",
                wrong.Count == 0, string.Join(", ", wrong));
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            CardDef Synthetic(int id, string type, string desc) => new()
            {
                id = id, name = "Synthetic TargetingTruth " + id, type = type, frameType = "effect", race = "Aqua",
                attribute = "WATER", level = 4, atk = 1200, def = 1400, desc = desc
            };

            var goraText = db.Get(GoraTurtle)?.desc ?? "";
            var burnText = db.Get(BigBurn)?.desc ?? "";
            var misses = new[]
            {
                // Without "face-up … on the field", or only Spell Cards, or a longer sentence: not Gora's template.
                Synthetic(90177001, "Effect Monster",
                    "Negate the effect of Spell and Trap Cards controlled by your opponent that target this card."),
                Synthetic(90177002, "Effect Monster",
                    "Negate the effect of Spell Cards controlled by your opponent that target this face-up card on the field."),
                Synthetic(90177003, "Effect Monster", goraText + " You can Tribute this card; draw 1 card."),
                // Big Burn with a field target, or without its first sentence.
                Synthetic(90177004, "Trap Card",
                    "You can only activate this card when your opponent activates a card that targets a monster on the " +
                    "field. Remove from play all monsters in both players' Graveyards."),
                Synthetic(90177005, "Trap Card", "Remove from play all monsters in both players' Graveyards."),
            };
            var leaked = misses.Where(d =>
            {
                var p = CardTextEffectCompiler.Compile(d);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster ||
                    (c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy && c.Side == EffectSide.Opponent)));
            }).Select(d => d.desc).ToList();
            var silent = Prog(db, SilentSwordsmanLv3);
            Check("Near misses: Gora / Big Burn look-alikes stay uncompiled (and Silent Swordsman LV3 is not Gora's shape)",
                goraText.Length > 0 && burnText.Length > 0 && leaked.Count == 0 &&
                (silent == null || !silent.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy && c.Side == EffectSide.Opponent)),
                string.Join(" | ", leaked));
        }

        // ───────────────────────────── Archfiend die roll ─────────────────────────────

        static void ArchfiendDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, name, face) in new[] { (Terrorking, "Terrorking Archfiend", 2), (Vilepawn, "Vilepawn Archfiend", 3) })
            {
                // Fissure picks without targeting: no die is rolled, even with a negate face queued.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var rolls = RollLog(e);
                var fiend = Up(e, e.Opponent, id, 0);
                var fis = PutInHand(e, e.Player, Fissure);
                e.Rng.QueueDie(face);
                var act = reached && e.TryActivateSpellTrap(e.Player, fis, fromHand: true);
                Drive(e, c => c == fiend);
                Check($"Fissure vs {name}: no die roll; the Archfiend is destroyed",
                    act && rolls.Count == 0 && e.Opponent.Graveyard.Contains(fiend) && e.Player.Graveyard.Contains(fis),
                    $"act={act} rolls=[{string.Join(" / ", rolls)}] {State(e)}");
            }

            {
                // A PSCT target still rolls: Offerings to the Doomed on Terrorking, a 2 negates and destroys Offerings.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var rolls = RollLog(e);
                var tk = Up(e, e.Opponent, Terrorking, 0);
                var off = PutInHand(e, e.Player, OfferingsToTheDoomed);
                e.Rng.QueueDie(2);
                var act = reached && e.TryActivateSpellTrap(e.Player, off, fromHand: true);
                Drive(e, c => c == tk);
                Check("Offerings to the Doomed (PSCT target) vs Terrorking: rolls a 2 — negated, Terrorking stays",
                    act && rolls.Count == 1 && OnField(e.Opponent, tk) && e.Player.Graveyard.Contains(off),
                    $"act={act} rolls=[{string.Join(" / ", rolls)}] {State(e)}");
            }

            {
                // An old-text "select" whose Lua targets still rolls: Stop Defense on a Defense Terrorking, a 5 negates.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var rolls = RollLog(e);
                var tk = Up(e, e.Opponent, Terrorking, 0, BattlePosition.Defense);
                var sd = PutInHand(e, e.Player, StopDefense);
                e.Rng.QueueDie(5);
                var act = reached && e.TryActivateSpellTrap(e.Player, sd, fromHand: true);
                Drive(e, c => c == tk);
                Check("Stop Defense (old-text select, Lua SelectTarget) vs Terrorking: rolls a 5 — negated, stays in Defense",
                    act && rolls.Count == 1 && OnField(e.Opponent, tk) && tk.Position == BattlePosition.Defense,
                    $"act={act} rolls=[{string.Join(" / ", rolls)}] pos={tk.Position} {State(e)}");
            }

            {
                // A roll that does not negate lets the targeting effect apply (Vilepawn on a 1).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var rolls = RollLog(e);
                var vp = Up(e, e.Opponent, Vilepawn, 0);
                var off = PutInHand(e, e.Player, OfferingsToTheDoomed);
                e.Rng.QueueDie(1);
                var act = reached && e.TryActivateSpellTrap(e.Player, off, fromHand: true);
                Drive(e, c => c == vp);
                Check("Offerings to the Doomed vs Vilepawn: rolls a 1 — Vilepawn is destroyed",
                    act && rolls.Count == 1 && e.Opponent.Graveyard.Contains(vp),
                    $"act={act} rolls=[{string.Join(" / ", rolls)}] {State(e)}");
            }

            {
                // Darkbishop protects Archfiends on the field only: Gravedigger Ghoul targeting a Terrorking in the GY
                // rolls nothing and banishes it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var rolls = RollLog(e);
                Up(e, e.Opponent, Darkbishop, 0);
                var gyTk = InGy(e, e.Opponent, Terrorking);
                var gg = PutInHand(e, e.Player, GravediggerGhoul);
                e.Rng.QueueDie(3);
                var act = reached && e.TryActivateSpellTrap(e.Player, gg, fromHand: true);
                Drive(e, c => c == gyTk);
                Check("Gravedigger Ghoul on a Terrorking in the GY under Darkbishop: no roll, it is banished",
                    act && rolls.Count == 0 && e.Opponent.Banished.Contains(gyTk),
                    $"act={act} rolls=[{string.Join(" / ", rolls)}] {State(e)}");
            }
        }

        // ───────────────────────────── protections ─────────────────────────────

        static void ProtectionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Lord of D. stops targets, not Fissure's choice: Fissure destroys the 600-ATK Petit Dragon; Offerings to the
                // Doomed (a target) cannot pick it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var lord = Up(e, e.Opponent, LordOfD, 0);
                var petit = Up(e, e.Opponent, PetitDragon, 1);
                var off = e.CreateCardInstance(OfferingsToTheDoomed);
                var offClause = CompiledEffectCache.GetOrCompile(off.Def)?.ClauseList
                    .FirstOrDefault(c => c != null && c.RequiresTargetChoice);
                var offList = offClause != null
                    ? TextEffectRuntime.CollectTargets(e, e.Player, offClause, off)
                    : new List<CardInstance>();
                var fis = PutInHand(e, e.Player, Fissure);
                var act = reached && e.TryActivateSpellTrap(e.Player, fis, fromHand: true);
                Drive(e, c => c == petit);
                Check("Lord of D.: Fissure (no target) destroys the Dragon; a targeting list leaves it out",
                    act && e.Opponent.Graveyard.Contains(petit) && OnField(e.Opponent, lord) && offClause != null &&
                    offList.Contains(lord) && !offList.Contains(petit),
                    $"act={act} offList=[{string.Join(",", offList.Select(c => c.Name))}] petit={petit.Name}/{petit.CurrentAtk} " +
                    $"oppGy=[{string.Join(",", e.Opponent.Graveyard.Select(c => c.Name))}] {State(e)}");
            }

            {
                // Different Dimension Dragon: Hammer Shot (no target) cannot destroy it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var ddd = Up(e, e.Opponent, DiffDimDragon, 0);
                var hs = PutInHand(e, e.Player, HammerShot);
                var act = reached && e.TryActivateSpellTrap(e.Player, hs, fromHand: true);
                Drive(e, c => c == ddd);
                Check("Different Dimension Dragon: survives Hammer Shot (highest-ATK choice, no target)",
                    act && OnField(e.Opponent, ddd), $"act={act} {State(e)}");
            }

            {
                // Different Dimension Dragon: Two-Pronged Attack's old-text "select" targets (Lua SelectTarget) — destroyed.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var s1 = Up(e, e.Player, SkullServant, 0);
                var s2 = Up(e, e.Player, PetitAngel, 1);
                var ddd = Up(e, e.Opponent, DiffDimDragon, 0);
                var trap = PlaceSetTrap(e, e.Player, TwoPronged, 0);
                var act = reached && e.TryActivateSpellTrap(e.Player, trap, fromHand: false);
                Drive(e, c => c == s1 || c == s2 || c == ddd);
                Check("Different Dimension Dragon: destroyed by Two-Pronged Attack (old-text select that targets)",
                    act && e.Opponent.Graveyard.Contains(ddd) && !OnField(e.Player, s1) && !OnField(e.Player, s2),
                    $"act={act} {State(e)}");
            }

            {
                // Metalsilver Armor unchanged: the opponent's targeting lists hold only the equipped monster; Fissure's
                // non-targeting list still holds the other one.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = Up(e, p, Celtic, 0);
                var elf = Up(e, p, MysticalElf, 1);
                var msa = PutInHand(e, p, MetalsilverArmor);
                var act = reached && e.TryActivateSpellTrap(p, msa, fromHand: true);
                Drive(e, c => c == celtic);
                FieldSpellEffects.RefreshBoard(e);
                var equipped = ReferenceEquals(msa.EquippedTo, celtic);
                List<CardInstance> ListFor(int id)
                {
                    var src = e.CreateCardInstance(id);
                    var cl = CompiledEffectCache.GetOrCompile(src.Def)?.ClauseList
                        .FirstOrDefault(c => c != null && c.RequiresTargetChoice);
                    return cl != null ? TextEffectRuntime.CollectTargets(e, e.Opponent, cl, src) : new List<CardInstance>();
                }

                var offList = ListFor(OfferingsToTheDoomed);
                var fisList = ListFor(Fissure);
                Check("Metalsilver Armor: targets only the host; Fissure's choice is not filtered",
                    act && equipped && offList.Contains(celtic) && !offList.Contains(elf) && fisList.Contains(elf),
                    $"act={act} equipped={equipped} off=[{string.Join(",", offList.Select(c => c.Name))}] " +
                    $"fis=[{string.Join(",", fisList.Select(c => c.Name))}]");
            }
        }

        // ───────────────────────────── Gora Turtle of Illusion ─────────────────────────────

        static void GoraTurtleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Spell that targets it: negated (and the Spell is destroyed); Gora stays.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var gora = Up(e, e.Opponent, GoraTurtle, 0);
                var off = PutInHand(e, e.Player, OfferingsToTheDoomed);
                var act = reached && e.TryActivateSpellTrap(e.Player, off, fromHand: true);
                Drive(e, c => c == gora);
                Check("Gora Turtle: the opponent's Offerings to the Doomed on it is negated; Gora stays",
                    act && OnField(e.Opponent, gora) && e.Player.Graveyard.Contains(off), $"act={act} {State(e)}");
            }

            {
                // Its controller's own Spell that targets it applies (Lua rp==tp returns).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var gora = Up(e, e.Player, GoraTurtle, 0);
                var off = PutInHand(e, e.Player, OfferingsToTheDoomed);
                var act = reached && e.TryActivateSpellTrap(e.Player, off, fromHand: true);
                Drive(e, c => c == gora);
                Check("Gora Turtle: its controller's own Spell on it applies (destroyed)",
                    act && e.Player.Graveyard.Contains(gora), $"act={act} {State(e)}");
            }

            {
                // A Spell that does not target it applies: Fissure destroys the face-up Gora.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var gora = Up(e, e.Opponent, GoraTurtle, 0);
                var fis = PutInHand(e, e.Player, Fissure);
                var act = reached && e.TryActivateSpellTrap(e.Player, fis, fromHand: true);
                Drive(e, c => c == gora);
                Check("Gora Turtle: Fissure (no target) destroys it",
                    act && e.Opponent.Graveyard.Contains(gora), $"act={act} {State(e)}");
            }

            {
                // Set, it is bound by the opponent's Spellbinding Circle (only "this face-up card"); flipped face-up, the
                // Continuous Trap that keeps targeting it is negated and destroyed (Lua e1 EFFECT_DISABLE + e3 SELF_DESTROY).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var gora = PlaceMonster(e, e.Opponent, GoraTurtle, 0, BattlePosition.Defense, false);
                var circle = PlaceSetTrap(e, e.Player, SpellbindingCircle, 0);
                var act = reached && e.TryActivateSpellTrap(e.Player, circle, fromHand: false);
                Drive(e, c => c == gora);
                var bound = ReferenceEquals(circle.EquippedTo, gora) && e.Player.TryFindSpellTrap(circle, out _);
                gora.FaceUp = true;
                gora.Position = BattlePosition.Attack;
                FieldSpellEffects.RefreshBoard(e);
                Check("Gora Turtle: flipped face-up, the opponent's Spellbinding Circle that bound it is destroyed",
                    act && bound && e.Player.Graveyard.Contains(circle) && OnField(e.Opponent, gora),
                    $"act={act} bound={bound} circleGy={e.Player.Graveyard.Contains(circle)} {State(e)}");
            }
        }

        // ─────────────────── cross-group audit (v177) ───────────────────

        /// <summary>
        /// ChaosBanishSummons' new targeting ignition read through this batch's helper: Chaos Sorcerer targets (Lua
        /// EFFECT_FLAG_CARD_TARGET), so the opponent's Chaos Command Magician negates it when it is the only target.
        /// </summary>
        static void CrossGroupDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var sorc = Up(e, p, ChaosSorcerer, 0);
            var ccm = Up(e, o, ChaosCommandMagician, 0);
            e.NotifyPublic();
            var started = reached && e.TryActivateSpellTrap(p, sorc, fromHand: false);
            var offered = e.IsAwaitingEffectTarget && Legal(e).Contains(ccm);
            var picked = offered && e.TrySelectEffectTarget(ccm);
            Drive(e, _ => false);
            Check("Chaos Sorcerer vs Chaos Command Magician: the targeting ignition is negated; the Magician stays",
                started && offered && picked && OnField(o, ccm) && !o.Banished.Contains(ccm) && OnField(p, sorc),
                $"started={started} offered={offered} picked={picked} ccmOnField={OnField(o, ccm)} {State(e)}");
        }

        // ───────────────────────────── Big Burn ─────────────────────────────

        static void BigBurnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Monster Reborn targets a monster in a GY: Big Burn answers; every GY monster is banished
                // and Monster Reborn finds no target.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var burn = PlaceSetTrap(e, p, BigBurn, 0);
                var celtic = InGy(e, p, Celtic);
                var elf = InGy(e, o, MysticalElf);
                var mst = InGy(e, o, Mst);
                var reborn = PutInHand(e, o, MonsterReborn);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, reborn, fromHand: true);
                var offered = Offers(e, p, burn);
                var answered = offered && e.TryActivateChainResponse(p, burn);
                Drive(e, _ => false);
                Check("Big Burn: answers Monster Reborn; both GYs' monsters are banished, nothing is revived",
                    act && offered && answered && p.Banished.Contains(celtic) && o.Banished.Contains(elf) &&
                    o.Graveyard.Contains(mst) && !OnField(o, celtic) && !OnField(o, elf) && p.Graveyard.Contains(burn) &&
                    e.Chain.Count == 0, $"act={act} offered={offered} answered={answered} {State(e)}");
            }

            {
                // Cross-group audit (v177, with ChaosBanishSummons): Big Burn banishes both GYs' monsters at once (Lua
                // Duel.Remove(g)), one EVENT_REMOVE, so a face-up Soul Absorption triggers once for the whole group.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var burn = PlaceSetTrap(e, p, BigBurn, 0);
                var sa = e.CreateCardInstance(SoulAbsorption);
                sa.FaceUp = true;
                sa.SetThisTurn = false;
                p.SpellTrapZones[4].Occupant = sa;
                var celtic = InGy(e, p, Celtic);
                var elf = InGy(e, o, MysticalElf);
                var reborn = PutInHand(e, o, MonsterReborn);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, reborn, fromHand: true);
                var events = BanishEvents.EventCount(e);
                var lp = p.LifePoints;
                var answered = Offers(e, p, burn) && e.TryActivateChainResponse(p, burn);
                Drive(e, _ => false);
                Check("Big Burn + Soul Absorption: the 2 banished monsters are one event — +1000 LP once",
                    act && answered && p.Banished.Contains(celtic) && o.Banished.Contains(elf) &&
                    BanishEvents.EventCount(e) == events + 1 && p.LifePoints == lp + 1000,
                    $"act={act} answered={answered} events {events}->{BanishEvents.EventCount(e)} " +
                    $"lp {lp}->{p.LifePoints} {State(e)}");
            }

            {
                // An old-text "select" whose Lua targets (Gravedigger Ghoul) counts.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var burn = PlaceSetTrap(e, p, BigBurn, 0);
                InGy(e, p, Celtic);
                var gg = PutInHand(e, o, GravediggerGhoul);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, gg, fromHand: true);
                Check("Big Burn: offered against Gravedigger Ghoul (old-text select, Lua SelectTarget)",
                    act && Offers(e, p, burn), $"act={act} {State(e)}");
                Drive(e, _ => false);
            }

            {
                // A target on the field is not a GY target: not offered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                Up(e, p, Celtic, 0);
                InGy(e, p, MysticalElf);
                var burn = PlaceSetTrap(e, p, BigBurn, 0);
                var mst = PlaceSetTrap(e, p, Mst, 1);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                Check("Big Burn: not offered when the opponent targets a monster on the field",
                    act && Offers(e, p, mst) && !Offers(e, p, burn), $"act={act} {State(e)}");
                Drive(e, _ => false);
            }

            {
                // The AI answers the Player's Monster Reborn with its own Big Burn.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var burn = PlaceSetTrap(e, o, BigBurn, 0);
                var elf = InGy(e, o, MysticalElf);
                var celtic = InGy(e, p, Celtic);
                var reborn = PutInHand(e, p, MonsterReborn);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(p, reborn, fromHand: true);
                Drive(e, c => c == celtic);
                Check("Big Burn (AI): answers the Player's Monster Reborn; nothing is revived",
                    act && o.Graveyard.Contains(burn) && p.Banished.Contains(celtic) && o.Banished.Contains(elf) &&
                    !OnField(p, celtic) && e.Chain.Count == 0 && !e.IsAwaitingEffectTarget,
                    $"act={act} {State(e)}");
            }
        }
    }
}
