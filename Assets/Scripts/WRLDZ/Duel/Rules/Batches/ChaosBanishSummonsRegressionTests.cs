using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ChaosBanishSummons batch: Black Luster Soldier - Envoy of the Beginning, Chaos Sorcerer, Silpheed, Strike Ninja,
    /// Chaosrider Gustaph, Soul Absorption, Primal Seed. Compile shapes through the real compiler, near-miss sentences, and
    /// mini-duels through the engine entry points (TrySpecialSummonProcedure, TryActivateSpellTrap, TrySelectEffectTarget,
    /// TryAttack, response windows, TryEndTurnSafe). The cards are chained together: the procedures', Strike Ninja's and
    /// Gustaph's banishes feed Soul Absorption; Primal Seed needs Black Luster Soldier. Turn 1 is the Player's, turn 2 the AI's.
    /// Runtime: <see cref="ChaosBanishSummonsRuntime"/>, <see cref="BanishEvents"/>.
    /// </summary>
    public static class ChaosBanishSummonsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Bls = 72989439;            // Black Luster Soldier - Envoy of the Beginning, LIGHT 3000/2500
        const int ChaosSorcerer = 9596126;   // DARK 2300/2000
        const int Silpheed = 73001017;       // WIND 1700/700
        const int StrikeNinja = 41006930;    // DARK 1700/1200
        const int Gustaph = 47829960;        // Chaosrider Gustaph, WIND 1400/1500
        const int SoulAbsorption = 68073522; // Continuous Spell
        const int PrimalSeed = 23701465;     // Normal Spell
        const int Ced = 82301904;            // Chaos Emperor Dragon - Envoy of the End (skipped: stays a Stub)
        const int Fenrir = 218704;           // old-wording banish procedure (WATER)
        const int DarkNecrofear = 31829185;

        const int Bewd = 89631139;           // LIGHT 3000/2500
        const int MysticalElf = 15025844;    // LIGHT 800/2000
        const int Kuriboh = 40640057;        // DARK 300/200
        const int DarkMagician = 46986414;   // DARK 2500/2100
        const int Celtic = 91152256;         // EARTH 1400/1200
        const int HarpieLady = 76812113;     // WIND 1300/1400
        const int PotOfGreed = 55144522;
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int TrapHole = 4206964;
        const int BookOfMoon = 14087893;     // Quick-Play: target 1 face-up monster, face-down Defense Position

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("ChaosBanishSummons: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            PairProcedureDuels(db, pDeck, aDeck, Check);
            ChaosSorcererDuels(db, pDeck, aDeck, Check);
            BlsDuels(db, pDeck, aDeck, Check);
            SilpheedDuels(db, pDeck, aDeck, Check);
            StrikeNinjaDuels(db, pDeck, aDeck, Check);
            GustaphDuels(db, pDeck, aDeck, Check);
            GustaphTurnedFaceDown(db, pDeck, aDeck, Check);
            SoulAbsorptionDuels(db, pDeck, aDeck, Check);
            PrimalSeedDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            Clean(e);
            return e;
        }

        static void Clean(DuelEngine e)
        {
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        /// <summary>The Player's turn 3, Main Phase 1, board cleared (a turn with a Battle Phase).</summary>
        static DuelEngine PlayerTurn3(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Clean(e);
            return e;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pass every response window (no pick is answered).</summary>
        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 12 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static void EndTurn(DuelEngine e)
        {
            PassAll(e);
            e.RecoverStuckCombat();
            e.TryEndTurnSafe(e.TurnPlayer);
            PassAll(e);
        }

        static bool AttackAndDrain(DuelEngine e, DuelistState who, CardInstance attacker, CardInstance target)
        {
            PassAll(e);
            if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(who);
            PassAll(e);
            if (!e.CanAttack(who, attacker)) return false;
            var ok = e.TryAttack(who, attacker, target);
            DrainCombat(e);
            return ok;
        }

        static CardInstance Gy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance Banish(DuelEngine e, DuelistState who, int id, bool faceUp)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = faceUp;
            who.Banished.Add(c);
            return c;
        }

        /// <summary>A face-up Soul Absorption for <paramref name="who"/>, activated from the hand (turn player only).</summary>
        static CardInstance ActivateSoulAbsorption(DuelEngine e, DuelistState who)
        {
            var sa = PutInHand(e, who, SoulAbsorption);
            e.TryActivateSpellTrap(who, sa, fromHand: true);
            PassAll(e);
            return sa;
        }

        /// <summary>A face-up Soul Absorption placed straight in <paramref name="who"/>'s Spell &amp; Trap Zone.</summary>
        static CardInstance PlaceSoulAbsorption(DuelEngine e, DuelistState who, int zone = 4)
        {
            var sa = e.CreateCardInstance(SoulAbsorption);
            sa.FaceUp = true;
            sa.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = sa;
            return sa;
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.TryFindMonster(c, out _)) return $"{tag}M{(c.FaceUp ? "u" : "d")}";
                if (who.TryFindSpellTrap(c, out _)) return $"{tag}S{(c.FaceUp ? "u" : "d")}";
                if (who.Hand.Contains(c)) return tag + "Hand";
                if (who.Graveyard.Contains(c)) return tag + "GY";
                if (who.Banished.Contains(c)) return tag + "Ban";
            }

            return "gone";
        }

        static string Names(IEnumerable<CardInstance> cards) => string.Join(",", cards.Select(c => c?.Name ?? "null"));

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990000177, name = "ChaosBanishSummons probe", type = type, race = race,
                attribute = type.Contains("Monster") ? "DARK" : null, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static bool IsPairProcedure(EffectClause c) =>
            c.Action == EffectActionKind.SpecialSummonFromHandProcedure && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
            c.BanishFromGyCount == 2 && c.AttributeFilter == "LIGHT" && c.SecondAttributeFilter == "DARK" &&
            c.Amount == 0 && !c.MakesChainLink;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var sorc = Prog(db, ChaosSorcerer);
            Check("Chaos Sorcerer: the 1 LIGHT + 1 DARK procedure and the face-up banish ignition with its oath",
                sorc != null && sorc.FullyCompiled && Implemented(db, ChaosSorcerer) && sorc.ClauseList.Count == 2 &&
                Has(sorc, IsPairProcedure) &&
                Has(sorc, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.BanishTargetedFieldMonster &&
                               c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetChoice && c.IsPsctTarget &&
                               c.RequiresTargetFaceUp && c.TargetMayBeThisCard && c.OncePerTurn &&
                               c.ThisCannotAttackTurnActivated &&
                               c.ActivationCondition == ChaosBanishSummonsRuntime.ThisDeclaredNoAttackThisTurn),
                Describe(sorc));
            var bls = Prog(db, Bls);
            Check("BLS - Envoy of the Beginning: the procedure, the any-monster ignition and the second attack in a row",
                bls != null && bls.FullyCompiled && Implemented(db, Bls) && bls.ClauseList.Count == 3 &&
                Has(bls, IsPairProcedure) &&
                Has(bls, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.BanishTargetedFieldMonster &&
                              !c.RequiresTargetFaceUp && c.TargetMayBeThisCard && c.OncePerTurn &&
                              c.ThisCannotAttackTurnActivated) &&
                Has(bls, c => c.Timing == EffectTiming.ThisCardDestroysByBattle &&
                              c.Action == EffectActionKind.MakeSecondAttackInARow && c.OncePerTurn && !c.IsPsctTarget),
                Describe(bls));
            var sil = Prog(db, Silpheed);
            Check("Silpheed: the WIND banish procedure (no Chain Link) and the forced battle-destroyed random discard",
                sil != null && sil.FullyCompiled && Implemented(db, Silpheed) && sil.ClauseList.Count == 2 &&
                Has(sil, c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure && c.BanishFromGyCount == 1 &&
                              c.AttributeFilter == "WIND" && !c.MakesChainLink && c.Amount == 0) &&
                Has(sil, c => c.Timing == EffectTiming.SentFromFieldToGy &&
                              c.Action == EffectActionKind.DiscardRandomFromOpponentHand && c.Amount == 1 &&
                              c.RequiresThisDestroyedByBattle && c.Side == EffectSide.Opponent) &&
                !Has(sil, c => c.Action == EffectActionKind.SpecialSummonThisFromHand),
                Describe(sil));
            var fen = Prog(db, Fenrir);
            Check("The old-wording banish procedure family (Fenrir) is the hand procedure, not a hand Activate",
                fen != null && fen.FullyCompiled && Implemented(db, Fenrir) &&
                Has(fen, c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure && c.BanishFromGyCount == 2 &&
                              c.AttributeFilter == "WATER") &&
                !Has(fen, c => c.Action == EffectActionKind.SpecialSummonThisFromHand), Describe(fen));
            var necro = Prog(db, DarkNecrofear);
            Check("Dark Necrofear keeps its Type procedure (hand Activate, banish 3 Fiends)",
                necro != null && necro.FullyCompiled && Has(necro, c =>
                    c.Action == EffectActionKind.SpecialSummonThisFromHand && c.RaceFilter == "Fiend" &&
                    c.BanishFromGyCount == 3), Describe(necro));
            var ninja = Prog(db, StrikeNinja);
            Check("Strike Ninja: a monster Quick Effect — banish 2 DARK from the GY (cost), banish itself until the End Phase",
                ninja != null && ninja.FullyCompiled && Implemented(db, StrikeNinja) && ninja.ClauseList.Count == 1 &&
                Has(ninja, c => c.Timing == EffectTiming.MonsterQuickEffect &&
                                c.Action == EffectActionKind.BanishThisUntilEndPhase && c.BanishFromGyCount == 2 &&
                                c.AttributeFilter == "DARK" && c.OncePerTurn && c.IsQuickEffect), Describe(ninja));
            var gus = Prog(db, Gustaph);
            Check("Chaosrider Gustaph: ignition, up to 2 GY Spells banished at resolution, 300 ATK each",
                gus != null && gus.FullyCompiled && Implemented(db, Gustaph) && gus.ClauseList.Count == 1 &&
                Has(gus, c => c.Timing == EffectTiming.Activate &&
                              c.Action == EffectActionKind.BanishUpToSpellsFromYourGyGainAtkEach && c.Amount == 300 &&
                              c.PickUpTo == 2 && c.OncePerTurn && !c.RequiresTargetChoice && c.BanishFromGyCount == 0 &&
                              c.ActivationCondition == ChaosBanishSummonsRuntime.YourGySpellBanishable),
                Describe(gus));
            var soul = Prog(db, SoulAbsorption);
            Check("Soul Absorption: 500 LP per card banished (continuous trigger)",
                soul != null && soul.FullyCompiled && Implemented(db, SoulAbsorption) && soul.ClauseList.Count == 1 &&
                Has(soul, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                               c.Action == EffectActionKind.GainLpPerCardBanished && c.Amount == 500),
                Describe(soul));
            var seed = Prog(db, PrimalSeed);
            Check("Primal Seed: named condition on either field, exactly 2 of your banished cards to the hand",
                seed != null && seed.FullyCompiled && Implemented(db, PrimalSeed) && seed.ClauseList.Count == 1 &&
                Has(seed, c => c.Action == EffectActionKind.AddBanishedTargetsToHand &&
                               c.Zone == EffectZoneFilter.ControllerBanishedCards && c.RequiresTargetChoice &&
                               c.IsPsctTarget && c.TargetCount == 2 && !c.TargetUpTo &&
                               c.ActivationCondition == ChaosBanishSummonsRuntime.NamedFaceUpOnEitherField &&
                               c.ConditionName.Contains("Black Luster Soldier - Envoy of the Beginning")),
                Describe(seed));
            Check("Chaos Emperor Dragon - Envoy of the End stays not Implemented (skipped)",
                !Implemented(db, Ced) && !(Prog(db, Ced)?.FullyCompiled ?? false));

            // Near misses: the atoms own whole sentences.
            var sameAttr = CompileText("Effect Monster", "Warrior",
                "Cannot be Normal Summoned/Set. Must first be Special Summoned (from your hand) by banishing 1 LIGHT and 1 " +
                "LIGHT monster from your GY.");
            Check("near miss: '1 LIGHT and 1 LIGHT' is not the pair procedure",
                !Has(sameAttr, c => !string.IsNullOrEmpty(c.SecondAttributeFilter)), Describe(sameAttr));
            var noOath = CompileText("Effect Monster", "Spellcaster",
                "Once per turn: You can target 1 face-up monster on the field; banish that target.");
            Check("near miss: the Chaos ignition without its oath is not this batch's banish",
                !Has(noOath, c => c.Action == EffectActionKind.BanishTargetedFieldMonster || c.ThisCannotAttackTurnActivated),
                Describe(noOath));
            var loneTrigger = CompileText("Effect Monster", "Warrior",
                "If this attacking card destroys an opponent's monster by battle: It can make a second attack in a row.");
            Check("near miss: the second-attack bullet alone (no shared '1 of these effects') does not compile",
                !Has(loneTrigger, c => c.Action == EffectActionKind.MakeSecondAttackInARow), Describe(loneTrigger));
            var soulPlus = CompileText("Spell Card", "Continuous",
                "If a card(s) is banished: Gain 500 LP for each. Draw 1 card.");
            Check("near miss: Soul Absorption's sentence with a rider is not the banish trigger",
                !Has(soulPlus, c => c.Action == EffectActionKind.GainLpPerCardBanished), Describe(soulPlus));
            var seedNoCond = CompileText("Spell Card", "Normal",
                "Target 2 of your banished cards; add both those targets to your hand.");
            Check("near miss: Primal Seed's targets with no named condition are not this batch's clause",
                !Has(seedNoCond, c => c.Action == EffectActionKind.AddBanishedTargetsToHand), Describe(seedNoCond));
            var gusTraps = CompileText("Effect Monster", "Warrior",
                "Once per turn, you can remove from play up to 2 Trap Cards from your Graveyard. This card gains 300 ATK " +
                "for each card removed, until the end of your opponent's next turn.");
            Check("near miss: Trap Cards instead of Spell Cards is not Gustaph's clause",
                !Has(gusTraps, c => c.Action == EffectActionKind.BanishUpToSpellsFromYourGyGainAtkEach),
                Describe(gusTraps));
            var ninjaNoOpt = CompileText("Effect Monster", "Warrior",
                "(Quick Effect): You can banish 2 DARK monsters from your GY; banish this face-up card until the End Phase.");
            Check("near miss: Strike Ninja's effect without its once-per-turn sentence does not compile to it",
                !Has(ninjaNoOpt, c => c.Action == EffectActionKind.BanishThisUntilEndPhase), Describe(ninjaNoOpt));
        }

        // ───────────────────────────── the 1 LIGHT + 1 DARK procedure ─────────────────────────────

        static void PairProcedureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sa = ActivateSoulAbsorption(e, p);
                var elf = Gy(e, p, MysticalElf);
                var bewd = Gy(e, p, Bewd);
                var kuri = Gy(e, p, Kuriboh);
                var celtic = Gy(e, p, Celtic);
                var sorc = PutInHand(e, p, ChaosSorcerer);
                var lp = p.LifePoints;
                var events = BanishEvents.EventCount(e);
                var can = e.CanSpecialSummonProcedure(p, sorc);
                var started = e.TrySpecialSummonProcedure(p, sorc);
                var first = Legal(e);
                Check("Chaos Sorcerer: the procedure offers the LIGHT and DARK monsters of your GY (not the EARTH one)",
                    can && started && first.Count == 3 && first.Contains(elf) && first.Contains(bewd) &&
                    first.Contains(kuri) && !first.Contains(celtic) && Where(e, sa) == "PSu",
                    $"can={can} started={started} legal={Names(first)} sa={Where(e, sa)}");
                var picked = e.TrySelectEffectTarget(elf);
                var second = Legal(e);
                Check("Chaos Sorcerer: after a LIGHT monster only a DARK monster is left to pick",
                    picked && second.Count == 1 && second[0] == kuri, $"picked={picked} legal={Names(second)}");
                e.TrySelectEffectTarget(kuri);
                PassAll(e);
                Check("Chaos Sorcerer: Special Summoned by banishing exactly 1 LIGHT and 1 DARK; no Chain Link",
                    Where(e, sorc) == "PMu" && Where(e, elf) == "PBan" && Where(e, kuri) == "PBan" &&
                    Where(e, bewd) == "PGY" && Where(e, celtic) == "PGY" && sorc.ProcedureCompleted &&
                    !e.Chain.HasLinks,
                    $"sorc={Where(e, sorc)} elf={Where(e, elf)} kuri={Where(e, kuri)} bewd={Where(e, bewd)}");
                Check("Soul Absorption: the procedure's 2 banishes are one event — +1000 LP",
                    p.LifePoints == lp + 1000 && BanishEvents.EventCount(e) == events + 1,
                    $"lp {lp}->{p.LifePoints} events {events}->{BanishEvents.EventCount(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                Gy(e, p, MysticalElf);
                Gy(e, p, Bewd);
                var bls = PutInHand(e, p, Bls);
                Check("BLS: 2 LIGHT and no DARK monster cannot pay the procedure",
                    !e.CanSpecialSummonProcedure(p, bls) && !e.TrySpecialSummonProcedure(p, bls) && p.Hand.Contains(bls));
                var dm = Gy(e, p, DarkMagician);
                Check("BLS: with a DARK monster too, a player with one DARK picks the LIGHT first",
                    e.CanSpecialSummonProcedure(p, bls) && e.TrySpecialSummonProcedure(p, bls) &&
                    Legal(e).Count == 3, Names(Legal(e)));
                e.CancelEffectTargeting();
                Check("BLS: Cancel at the procedure pick — nothing is banished, the card stays in the hand",
                    p.Hand.Contains(bls) && p.Banished.Count == 0 && Where(e, dm) == "PGY" && e.PendingActivation == null);
            }

            {
                // The AI (Opponent) pays the procedure itself: its weakest LIGHT and weakest DARK.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var o = e.Opponent;
                var elf = Gy(e, o, MysticalElf);
                var bewd = Gy(e, o, Bewd);
                var kuri = Gy(e, o, Kuriboh);
                var dm = Gy(e, o, DarkMagician);
                var bls = PutInHand(e, o, Bls);
                var ok = e.TrySpecialSummonProcedure(o, bls);
                PassAll(e);
                Check("BLS (AI): Special Summoned by banishing its weakest LIGHT and weakest DARK monster",
                    reached && ok && Where(e, bls) == "OMu" && Where(e, elf) == "OBan" && Where(e, kuri) == "OBan" &&
                    Where(e, bewd) == "OGY" && Where(e, dm) == "OGY",
                    $"reached={reached} ok={ok} bls={Where(e, bls)} elf={Where(e, elf)} kuri={Where(e, kuri)}");
            }
        }

        // ───────────────────────────── Chaos Sorcerer ─────────────────────────────

        static void ChaosSorcererDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var sorc = PlaceMonster(e, p, ChaosSorcerer, 0, BattlePosition.Attack, true);
                var up = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var down = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Defense, false);
                var can = e.CanActivateSpellTrap(p, sorc, fromHand: false);
                var started = e.TryActivateSpellTrap(p, sorc, fromHand: false);
                var legal = Legal(e);
                Check("Chaos Sorcerer: targets a face-up monster on either field (itself too), never a face-down one",
                    can && started && legal.Contains(up) && legal.Contains(sorc) && !legal.Contains(down),
                    $"can={can} started={started} legal={Names(legal)}");
                e.TrySelectEffectTarget(up);
                PassAll(e);
                Check("Chaos Sorcerer: the target is banished; the oath stops this card attacking this turn",
                    Where(e, up) == "OBan" && Where(e, down) == "OMd" &&
                    sorc.CannotAttackDuringTurnNumber == e.TurnNumber,
                    $"up={Where(e, up)} oath={sorc.CannotAttackDuringTurnNumber} turn={e.TurnNumber}");
                Check("Chaos Sorcerer: once per turn",
                    !e.CanActivateSpellTrap(p, sorc, fromHand: false));
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sorc = PlaceMonster(e, p, ChaosSorcerer, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                sorc.AttackedThisTurn = true;
                sorc.AttacksDeclaredThisTurn = 1;
                Check("Chaos Sorcerer: a card that declared an attack this turn cannot activate it (GetAttackAnnouncedCount)",
                    !e.CanActivateSpellTrap(p, sorc, fromHand: false));
            }

            {
                // The AI's turn: it banishes the Player's strongest monster, then its Sorcerer cannot attack. The Player's
                // Soul Absorption gains LP from the opponent's banish too.
                var e = Fresh(db, pDeck, aDeck);
                var sa = PlaceSoulAbsorption(e, e.Player);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                e.Player.SpellTrapZones[4].Occupant = sa;
                var o = e.Opponent;
                var p = e.Player;
                var sorc = PlaceMonster(e, o, ChaosSorcerer, 0, BattlePosition.Attack, true);
                var weak = PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);
                var strong = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var ok = e.TryActivateSpellTrap(o, sorc, fromHand: false);
                PassAll(e);
                Check("Chaos Sorcerer (AI): banishes the opponent's strongest face-up monster",
                    reached && ok && Where(e, strong) == "PBan" && Where(e, weak) == "PMu",
                    $"reached={reached} ok={ok} strong={Where(e, strong)} weak={Where(e, weak)} q={BanishEvents.QueuedTriggers(e)} " +
                    $"links={e.Chain.HasLinks} pend={e.PendingActivation != null} resp={e.IsAwaitingResponse} sa={Where(e, sa)}");
                Check("Soul Absorption: a banish by the opponent's effect gains its controller 500 LP",
                    p.LifePoints == lp + 500, $"lp {lp}->{p.LifePoints} queued={BanishEvents.QueuedTriggers(e)} sa={Where(e, sa)}");
                e.TryEnterBattlePhase(o);
                PassAll(e);
                Check("Chaos Sorcerer (AI): cannot attack the turn it activated its effect",
                    e.Phase == DuelPhase.Battle && !e.CanAttack(o, sorc), $"phase={e.Phase}");
            }
        }

        // ───────────────────────────── Black Luster Soldier - Envoy of the Beginning ─────────────────────────────

        static void BlsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bls = PlaceMonster(e, p, Bls, 0, BattlePosition.Attack, true);
                var down = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Defense, false);
                var up = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var started = e.TryActivateSpellTrap(p, bls, fromHand: false);
                var legal = Legal(e);
                Check("BLS: the ignition targets any monster on the field — face-down ones and itself too",
                    started && legal.Contains(down) && legal.Contains(up) && legal.Contains(bls), Names(legal));
                e.TrySelectEffectTarget(down);
                PassAll(e);
                Check("BLS: the face-down target is banished; BLS cannot attack this turn; once per turn",
                    Where(e, down) == "OBan" && bls.CannotAttackDuringTurnNumber == e.TurnNumber && bls.EffectUsedThisTurn &&
                    !e.CanActivateSpellTrap(p, bls, fromHand: false), $"down={Where(e, down)}");
            }

            {
                var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bls = PlaceMonster(e, p, Bls, 0, BattlePosition.Attack, true);
                var a = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Attack, true);
                var first = AttackAndDrain(e, p, bls, a);
                Check("BLS: attacking and destroying a monster grants a second attack in a row",
                    reached && first && Where(e, a) == "OGY" && ChaosBanishSummonsRuntime.HasChainAttack(e, bls) &&
                    e.CanAttack(p, bls), $"reached={reached} first={first} a={Where(e, a)} used={bls.EffectUsedThisTurn} can={e.CanAttack(p, bls)} max={e.MaxAttacksThisTurn(bls)} n={bls.AttacksDeclaredThisTurn} phase={e.Phase} step={e.BattleStep}");
                var second = AttackAndDrain(e, p, bls, b);
                Check("BLS: the second attack in a row destroys the next monster; there is no third attack",
                    second && Where(e, b) == "OGY" && !e.CanAttack(p, bls) && bls.AttacksDeclaredThisTurn == 2,
                    $"second={second} b={Where(e, b)} attacks={bls.AttacksDeclaredThisTurn}");
            }

            {
                var e = PlayerTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bls = PlaceMonster(e, p, Bls, 0, BattlePosition.Attack, true);
                var other = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var a = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
                var first = AttackAndDrain(e, p, bls, a);
                var granted = ChaosBanishSummonsRuntime.HasChainAttack(e, bls);
                var otherAttack = AttackAndDrain(e, p, other, b);
                Check("near miss: another monster attacks first — the second attack 'in a row' is gone",
                    reached && first && granted && otherAttack && !e.CanAttack(p, bls) &&
                    !ChaosBanishSummonsRuntime.HasChainAttack(e, bls),
                    $"first={first} granted={granted} other={otherAttack}");
            }

            {
                // BLS defends and destroys the attacker: "this attacking card" — no second attack.
                var e = Fresh(db, pDeck, aDeck);
                var reachedMain = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var bls = PlaceMonster(e, p, Bls, 0, BattlePosition.Attack, true);
                var atk = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(e, o, atk, bls);
                Check("near miss: BLS destroying its attacker as the attack target gets no second attack",
                    reachedMain && ok && Where(e, atk) == "OGY" && !ChaosBanishSummonsRuntime.HasChainAttack(e, bls) &&
                    !bls.EffectUsedThisTurn, $"ok={ok} atk={Where(e, atk)}");
            }
        }

        // ───────────────────────────── Silpheed ─────────────────────────────

        static void SilpheedDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var harpie = Gy(e, p, HarpieLady);
                var celtic = Gy(e, p, Celtic);
                var sil = PutInHand(e, p, Silpheed);
                var ok = e.TrySpecialSummonProcedure(p, sil);
                PassAll(e);
                Check("Silpheed: Special Summoned by banishing its lone WIND monster; no Chain Link to respond to",
                    ok && Where(e, sil) == "PMu" && Where(e, harpie) == "PBan" && Where(e, celtic) == "PGY" &&
                    !e.Chain.HasLinks && sil.ProcedureCompleted, $"ok={ok} sil={Where(e, sil)}");
                var sil2 = PutInHand(e, p, Silpheed);
                Check("Silpheed: no WIND monster left — no procedure; it is not a hand Activate either",
                    !e.CanSpecialSummonProcedure(p, sil2) && !e.CanActivateSpellTrap(p, sil2, fromHand: true));
            }

            {
                // Destroyed by battle on the AI's turn: the attacker's controller discards 1 random card.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var sil = PlaceMonster(e, p, Silpheed, 0, BattlePosition.Attack, true);
                var atk = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var h1 = PutInHand(e, o, PotOfGreed);
                var h2 = PutInHand(e, o, Celtic);
                var ok = AttackAndDrain(e, o, atk, sil);
                PassAll(e);
                Check("Silpheed: destroyed by battle and sent to the GY — the opponent discards 1 random card",
                    reached && ok && Where(e, sil) == "PGY" && o.Hand.Count == 1 &&
                    (Where(e, h1) == "OGY") != (Where(e, h2) == "OGY"),
                    $"reached={reached} ok={ok} sil={Where(e, sil)} hand={Names(o.Hand)} gy={Names(o.Graveyard)}");
            }

            {
                // Cross-group audit (v177, with SpellEventsAndHandSendTriggers): Silpheed's random discard sends the
                // opponent's Elephant Statue of Disaster from the hand by the Player's card effect — it burns the Player for 2000.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var sil = PlaceMonster(e, p, Silpheed, 0, BattlePosition.Attack, true);
                var atk = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var statue = PutInHand(e, o, 12160911); // Elephant Statue of Disaster
                var lp = p.LifePoints;
                var ok = AttackAndDrain(e, o, atk, sil);
                PassAll(e);
                Check("Silpheed + Elephant Statue of Disaster: the discarded Statue inflicts 2000 to Silpheed's controller",
                    reached && ok && Where(e, sil) == "PGY" && Where(e, statue) == "OGY" && p.LifePoints == lp - 800 - 2000,
                    $"reached={reached} ok={ok} statue={Where(e, statue)} lp {lp}->{p.LifePoints}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Silpheed, 0, BattlePosition.Attack, true);
                PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                var hole = PutInHand(e, p, DarkHole);
                e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("near miss: Silpheed destroyed by a card effect — no discard",
                    p.Graveyard.Any(c => c.CardId == Silpheed) && o.Hand.Count == 2, $"hand={Names(o.Hand)}");
            }
        }

        // ───────────────────────────── Strike Ninja ─────────────────────────────

        static void StrikeNinjaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sa = ActivateSoulAbsorption(e, p);
                var ninja = PlaceMonster(e, p, StrikeNinja, 0, BattlePosition.Attack, true);
                var k1 = Gy(e, p, Kuriboh);
                var k2 = Gy(e, p, Kuriboh);
                var dm = Gy(e, p, DarkMagician);
                var elf = Gy(e, p, MysticalElf);
                var lp = p.LifePoints;
                var can = e.CanActivateSpellTrap(p, ninja, fromHand: false);
                var started = e.TryActivateSpellTrap(p, ninja, fromHand: false);
                var legal = Legal(e);
                Check("Strike Ninja: its Quick Effect asks for 2 DARK monsters of your GY as the cost",
                    can && started && legal.Count == 3 && legal.Contains(k1) && legal.Contains(dm) && !legal.Contains(elf) &&
                    Where(e, sa) == "PSu", $"can={can} started={started} legal={Names(legal)}");
                e.TrySelectEffectTarget(k1);
                e.TrySelectEffectTarget(k2);
                PassAll(e);
                Check("Strike Ninja: the 2 DARK monsters are banished (cost), then Strike Ninja is banished until the End Phase",
                    Where(e, k1) == "PBan" && Where(e, k2) == "PBan" && Where(e, dm) == "PGY" &&
                    Where(e, ninja) == "PBan" && !e.Chain.HasLinks,
                    $"k1={Where(e, k1)} k2={Where(e, k2)} ninja={Where(e, ninja)}");
                Check("Soul Absorption: the cost (2 cards, one event) and the effect (1 card) — +1500 LP",
                    p.LifePoints == lp + 1500, $"lp {lp}->{p.LifePoints}");
                EndTurn(e);
                Check("Strike Ninja: returns to the field face-up in the End Phase",
                    e.Player.TryFindMonster(ninja, out _) && ninja.FaceUp, $"ninja={Where(e, ninja)} turn={e.TurnNumber}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var ninja = PlaceMonster(e, p, StrikeNinja, 0, BattlePosition.Attack, true);
                Gy(e, p, Kuriboh);
                Gy(e, p, MysticalElf);
                Check("near miss: only 1 DARK monster in the GY — Strike Ninja cannot activate",
                    !e.CanActivateSpellTrap(p, ninja, fromHand: false));
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var ninja = PlaceMonster(e, p, StrikeNinja, 0, BattlePosition.Attack, true);
                var k1 = Gy(e, p, Kuriboh);
                var k2 = Gy(e, p, Kuriboh);
                Gy(e, p, DarkMagician);
                e.TryActivateSpellTrap(p, ninja, fromHand: false);
                e.CancelEffectTargeting();
                Check("Strike Ninja: Cancel at the cost pick — nothing is banished, the effect stays unused",
                    Where(e, ninja) == "PMu" && Where(e, k1) == "PGY" && Where(e, k2) == "PGY" && !e.Chain.HasLinks &&
                    e.CanActivateSpellTrap(p, ninja, fromHand: false), $"ninja={Where(e, ninja)}");
            }

            {
                // The AI saves its Strike Ninja from the Player's Dark Hole (a Chain response), then it returns.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ninja = PlaceMonster(e, o, StrikeNinja, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                Gy(e, o, Kuriboh);
                Gy(e, o, DarkMagician);
                var hole = PutInHand(e, p, DarkHole);
                e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("Strike Ninja (AI): chained to Dark Hole, it is banished and escapes the destruction",
                    Where(e, ninja) == "OBan" && Where(e, mine) == "PGY" && o.Banished.Count == 3,
                    $"ninja={Where(e, ninja)} mine={Where(e, mine)} oBan={Names(o.Banished)}");
                EndTurn(e);
                Check("Strike Ninja (AI): back on the field in the End Phase",
                    o.TryFindMonster(ninja, out _) && ninja.FaceUp, $"ninja={Where(e, ninja)}");
            }
        }

        // ───────────────────────────── Chaosrider Gustaph ─────────────────────────────

        static void GustaphDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sa = ActivateSoulAbsorption(e, p);
                var gus = PlaceMonster(e, p, Gustaph, 0, BattlePosition.Attack, true);
                var s1 = Gy(e, p, PotOfGreed);
                var s2 = Gy(e, p, DarkHole);
                var s3 = Gy(e, p, Mst);
                var trap = Gy(e, p, TrapHole);
                var lp = p.LifePoints;
                var atk0 = gus.CurrentAtk;
                var started = e.TryActivateSpellTrap(p, gus, fromHand: false);
                var legal = Legal(e);
                Check("Gustaph: at resolution choose Spells in your GY (no Trap)",
                    started && legal.Count == 4 - 1 && legal.Contains(s1) && !legal.Contains(trap) && Where(e, sa) == "PSu",
                    $"started={started} legal={Names(legal)}");
                e.TrySelectEffectTarget(s1);
                var legal2 = Legal(e);
                e.TrySelectEffectTarget(s2);
                PassAll(e);
                Check("Gustaph: 2 Spells banished — +600 ATK; the third Spell and the Trap stay",
                    legal2.Count == 2 && Where(e, s1) == "PBan" && Where(e, s2) == "PBan" && Where(e, s3) == "PGY" &&
                    Where(e, trap) == "PGY" && gus.CurrentAtk == atk0 + 600, $"atk {atk0}->{gus.CurrentAtk}");
                Check("Soul Absorption: Gustaph's 2 banishes are one event — +1000 LP", p.LifePoints == lp + 1000,
                    $"lp {lp}->{p.LifePoints}");
                Check("Gustaph: once per turn", !e.CanActivateSpellTrap(p, gus, fromHand: false));
                EndTurn(e);
                var duringOpp = gus.CurrentAtk;
                EndTurn(e);
                Check("Gustaph: the ATK lasts through the opponent's next turn and ends at its End Phase",
                    duringOpp == atk0 + 600 && gus.CurrentAtk == atk0 && e.TurnNumber == 3,
                    $"opp turn={duringOpp} after={gus.CurrentAtk} turn={e.TurnNumber}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var gus = PlaceMonster(e, p, Gustaph, 0, BattlePosition.Attack, true);
                var s1 = Gy(e, p, PotOfGreed);
                var s2 = Gy(e, p, DarkHole);
                var atk0 = gus.CurrentAtk;
                e.TryActivateSpellTrap(p, gus, fromHand: false);
                e.TrySelectEffectTarget(s1);
                e.CancelEffectTargeting(); // decline the second Spell
                PassAll(e);
                Check("Gustaph: up to 2 — declining the second banishes 1 Spell for +300 ATK",
                    Where(e, s1) == "PBan" && Where(e, s2) == "PGY" && gus.CurrentAtk == atk0 + 300 &&
                    e.PendingActivation == null, $"s1={Where(e, s1)} s2={Where(e, s2)} atk={gus.CurrentAtk}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var gus = PlaceMonster(e, p, Gustaph, 0, BattlePosition.Attack, true);
                Gy(e, p, TrapHole);
                Check("near miss: no Spell in the GY — Gustaph cannot activate", !e.CanActivateSpellTrap(p, gus, false));
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var o = e.Opponent;
                var gus = PlaceMonster(e, o, Gustaph, 0, BattlePosition.Attack, true);
                Gy(e, o, PotOfGreed);
                Gy(e, o, DarkHole);
                Gy(e, o, Mst);
                var atk0 = gus.CurrentAtk;
                var ok = e.TryActivateSpellTrap(o, gus, fromHand: false);
                PassAll(e);
                Check("Gustaph (AI): banishes 2 Spells at once for +600 ATK",
                    reached && ok && o.Banished.Count == 2 && gus.CurrentAtk == atk0 + 600 && e.PendingActivation == null,
                    $"reached={reached} ok={ok} ban={Names(o.Banished)} atk={gus.CurrentAtk}");
            }
        }

        // ───────────────────────────── Soul Absorption ─────────────────────────────

        static void SoulAbsorptionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var sa = ActivateSoulAbsorption(e, p);
                var before = p.LifePoints;
                var activated = Where(e, sa) == "PSu";
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Attack, true);
                // A mass banish by one clause is one event (both monsters at once).
                var events = BanishEvents.EventCount(e);
                e.BanishCopiesFromHandAndDeck(o, Celtic); // no copy in hand/Deck here: no event
                var noEvent = BanishEvents.EventCount(e) == events;
                using (BanishEvents.Group(e))
                    foreach (var m in o.MonstersOnField().ToList())
                        e.BanishCard(o, m, by: p);
                e.NotifyPublic(); // the state change a real action ends with
                PassAll(e);
                Check("Soul Absorption: 2 cards banished in one event — 1000 LP, once",
                    activated && noEvent && p.LifePoints == before + 1000 && BanishEvents.EventCount(e) == events + 1,
                    $"sa={Where(e, sa)} lp {before}->{p.LifePoints}");
                var hole = PutInHand(e, p, DarkHole);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var lp2 = p.LifePoints;
                e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("near miss: cards destroyed and sent to the GY are not banished — no LP", p.LifePoints == lp2,
                    $"lp {lp2}->{p.LifePoints}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sa = PutInHand(e, p, SoulAbsorption);
                var lp = p.LifePoints;
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                foreach (var m in e.Opponent.MonstersOnField().ToList()) e.BanishCard(e.Opponent, m, by: p);
                e.NotifyPublic();
                PassAll(e);
                Check("near miss: Soul Absorption in the hand gains nothing", p.LifePoints == lp && p.Hand.Contains(sa));
            }
        }

        /// <summary>
        /// Review finding (v177): the ATK gain is RESETS_STANDARD_DISABLE_PHASE_END, which includes RESET_TURN_SET. Book of
        /// Moon turns the boosted Gustaph face-down; flipped face-up again before the opponent's next End Phase it is back
        /// at its own ATK.
        /// </summary>
        static void GustaphTurnedFaceDown(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var gus = PlaceMonster(e, p, Gustaph, 0, BattlePosition.Attack, true);
            Gy(e, p, PotOfGreed);
            Gy(e, p, DarkHole);
            var atk0 = gus.CurrentAtk;
            e.TryActivateSpellTrap(p, gus, fromHand: false);
            foreach (var t in Legal(e).Take(1)) e.TrySelectEffectTarget(t);
            foreach (var t in Legal(e).Take(1)) e.TrySelectEffectTarget(t);
            PassAll(e);
            var boosted = gus.CurrentAtk;
            var moon = PutInHand(e, p, BookOfMoon);
            var moonStarted = e.TryActivateSpellTrap(p, moon, fromHand: true);
            if (Legal(e).Contains(gus)) e.TrySelectEffectTarget(gus);
            PassAll(e);
            var setFaceDown = !gus.FaceUp && gus.Position == BattlePosition.Defense;
            var lingering = gus.LingeringAtkModifier;
            // Flipped face-up again the same turn (any flip: the boost must not come back).
            gus.FaceUp = true;
            gus.Position = BattlePosition.Attack;
            e.NotifyPublic();
            Check("Gustaph: turned face-down by Book of Moon loses its ATK gain (RESET_TURN_SET) — face-up again it is back at its own ATK",
                boosted == atk0 + 600 && moonStarted && setFaceDown && lingering == 0 && gus.CurrentAtk == atk0,
                $"atk0={atk0} boosted={boosted} moon={moonStarted} faceDown={setFaceDown} lingering={lingering} after flip={gus.CurrentAtk}");
            EndTurn(e);
            EndTurn(e);
            Check("Gustaph: no stale boost is removed again at the opponent's End Phase", gus.CurrentAtk == atk0,
                $"atk={gus.CurrentAtk} turn={e.TurnNumber}");
        }

        // ───────────────────────────── Primal Seed ─────────────────────────────

        static void PrimalSeedDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Bls, 0, BattlePosition.Attack, true);
                var b1 = Banish(e, p, DarkMagician, true);
                var b2 = Banish(e, p, PotOfGreed, true);
                var b3 = Banish(e, p, Celtic, false);
                var theirs = Banish(e, e.Opponent, Kuriboh, true);
                var seed = PutInHand(e, p, PrimalSeed);
                var can = e.CanActivateSpellTrap(p, seed, fromHand: true);
                var started = e.TryActivateSpellTrap(p, seed, fromHand: true);
                var legal = Legal(e);
                Check("Primal Seed: with BLS face-up, targets exactly 2 of YOUR banished cards (any card, face-down too)",
                    can && started && legal.Count == 3 && legal.Contains(b1) && legal.Contains(b2) && legal.Contains(b3) &&
                    !legal.Contains(theirs), $"can={can} started={started} legal={Names(legal)}");
                for (var i = 0; i < 6 && e.IsAwaitingEffectTarget; i++)
                {
                    var next = new[] { b1, b2 }.FirstOrDefault(c => Legal(e).Contains(c));
                    if (next == null || !e.TrySelectEffectTarget(next)) e.CancelEffectTargeting();
                }

                PassAll(e);
                Check("Primal Seed: both targets are added to your hand; the third card stays banished",
                    Where(e, b1) == "PHand" && Where(e, b2) == "PHand" && Where(e, b3) == "PBan" &&
                    Where(e, theirs) == "OBan" && Where(e, seed) == "PGY",
                    $"b1={Where(e, b1)} b2={Where(e, b2)} b3={Where(e, b3)} seed={Where(e, seed)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                Banish(e, p, DarkMagician, true);
                Banish(e, p, PotOfGreed, true);
                var seed = PutInHand(e, p, PrimalSeed);
                var none = !e.CanActivateSpellTrap(p, seed, fromHand: true);
                var down = PlaceMonster(e, e.Opponent, Bls, 0, BattlePosition.Defense, false);
                var faceDown = !e.CanActivateSpellTrap(p, seed, fromHand: true);
                down.FaceUp = true;
                down.Position = BattlePosition.Attack;
                var oppField = e.CanActivateSpellTrap(p, seed, fromHand: true);
                p.Banished.RemoveAt(0);
                var oneTarget = !e.CanActivateSpellTrap(p, seed, fromHand: true);
                Check("Primal Seed: no BLS → no; a face-down BLS → no; a face-up BLS on the opponent's field → yes; " +
                      "1 banished card → no",
                    none && faceDown && oppField && oneTarget,
                    $"none={none} faceDown={faceDown} oppField={oppField} oneTarget={oneTarget}");
            }

            {
                // Chained: BLS is Special Summoned by its procedure (Soul Absorption +1000), then Primal Seed takes the
                // 2 banished cost monsters back.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var sa = ActivateSoulAbsorption(e, p);
                var elf = Gy(e, p, MysticalElf);
                var dm = Gy(e, p, DarkMagician);
                var bls = PutInHand(e, p, Bls);
                var seed = PutInHand(e, p, PrimalSeed);
                var lp = p.LifePoints;
                e.TrySpecialSummonProcedure(p, bls);
                PassAll(e);
                var summoned = Where(e, bls) == "PMu" && p.LifePoints == lp + 1000;
                var dbg = $"bls={Where(e, bls)} lp={lp}->{p.LifePoints}";
                e.TryActivateSpellTrap(p, seed, fromHand: true);
                for (var i = 0; i < 6 && e.IsAwaitingEffectTarget; i++)
                {
                    var next = Legal(e).FirstOrDefault();
                    if (next == null || !e.TrySelectEffectTarget(next)) e.CancelEffectTargeting();
                }

                PassAll(e);
                Check("Chained: BLS summoned by banishing LIGHT + DARK (+1000 LP), Primal Seed returns both to the hand",
                    summoned && Where(e, sa) == "PSu" && Where(e, elf) == "PHand" && Where(e, dm) == "PHand",
                    $"summoned={summoned} elf={Where(e, elf)} dm={Where(e, dm)} {dbg}");
            }
        }
    }
}
