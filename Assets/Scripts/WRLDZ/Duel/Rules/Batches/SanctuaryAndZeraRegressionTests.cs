using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SanctuaryAndZera batch: Archlord Zerato, Mazera DeVille, The Agent of Force - Mars, The Agent of Judgment - Saturn,
    /// Light of Judgment. Compile shapes through the real compiler, near-miss sentences, and mini-duels through the engine
    /// entry points (TrySpecialSummonProcedure, TryActivateSpellTrap, TrySelectEffectTarget, CancelEffectTargeting,
    /// FieldSpellEffects.RefreshBoard). The Field Spells are only placed face-up here. Runtime:
    /// <see cref="SanctuaryAndZeraRuntime"/>.
    /// </summary>
    public static class SanctuaryAndZeraRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Zerato = 18378582;          // Archlord Zerato, LIGHT Fairy 2800/2300
        const int Mazera = 6133894;           // Mazera DeVille, DARK Fiend 2800/2300
        const int Mars = 91123920;            // The Agent of Force - Mars, LIGHT 0/0
        const int Saturn = 91345518;          // The Agent of Judgment - Saturn, LIGHT 2400/0
        const int LightOfJudgment = 44595286; // Normal Trap
        const int WarriorOfZera = 66073051;
        const int Sanctuary = 56433456;       // The Sanctuary in the Sky
        const int Pandemonium = 94585852;

        const int MysticalElf = 15025844;     // LIGHT 800/2000
        const int Bewd = 89631139;            // LIGHT 3000/2500
        const int Celtic = 91152256;          // EARTH 1400/1200
        const int Kuriboh = 40640057;         // DARK 300/200
        const int DarkMagician = 46986414;    // DARK 2500/2100
        const int PotOfGreed = 55144522;
        const int DarkHole = 53129443;
        const int MonsterReborn = 83764719;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SanctuaryAndZera: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            ZeratoProcedureDuels(db, pDeck, aDeck, Check);
            ZeratoIgnitionDuels(db, pDeck, aDeck, Check);
            MazeraDuels(db, pDeck, aDeck, Check);
            MarsDuels(db, pDeck, aDeck, Check);
            SaturnDuels(db, pDeck, aDeck, Check);
            LightOfJudgmentDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static CardInstance PlaceField(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static CardInstance Gy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static void ClearMonsters(DuelistState who)
        {
            for (var i = 0; i < who.MonsterZones.Length; i++)
                who.MonsterZones[i].Occupant = null;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 12 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>A human pays the discard cost by tapping the card (the cost pick lists only the legal discards).</summary>
        static bool PayDiscard(DuelEngine e, CardInstance discard)
        {
            var legal = Legal(e);
            var only = legal.Count == 1 && legal[0] == discard;
            if (only) e.TrySelectEffectTarget(discard);
            return only;
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.TryFindMonster(c, out _)) return $"{tag}M{(c.FaceUp ? "u" : "d")}";
                if (who.TryFindSpellTrap(c, out _)) return $"{tag}S{(c.FaceUp ? "u" : "d")}";
                if (who.FieldSpellZone?.Occupant == c) return tag + "F";
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
                id = 990000178, name = "SanctuaryAndZera probe", type = type, race = race,
                attribute = type.Contains("Monster") ? "LIGHT" : null, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Where(c => c != null)
                    .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}"));

        static EffectClause ClauseOf(CardDatabase db, int id, EffectActionKind action) =>
            Prog(db, id)?.ClauseList.FirstOrDefault(c => c != null && c.Action == action);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            const string sanct = "The Sanctuary in the Sky";
            var z = Prog(db, Zerato);
            Check("Archlord Zerato: the named face-up Tribute procedure (Sanctuary on either field) and the ignition",
                z != null && z.FullyCompiled &&
                Has(z, c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure && c.Amount == 0 &&
                            c.TributeNamedCard == "Warrior of Zera" && c.TributeFaceUpOnly &&
                            c.RequiresFaceUpName == sanct && !c.RequiresControllerNamedCard) &&
                Has(z, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
                            c.Side == EffectSide.Opponent && c.Zone == EffectZoneFilter.FieldMonsters &&
                            !c.RequiresTargetChoice && c.RequiresDiscardCost && c.DiscardCostAttribute == "LIGHT" &&
                            c.RequiresFaceUpName == sanct && c.RequiresControllerNamedCard &&
                            c.NotAppliedUnlessNamedFaceUp == sanct && !c.OncePerTurn) &&
                CardEffectStatus.Classify(db.Get(Zerato)) == CardEffectStatusKind.Implemented, Describe(z));

            var m = Prog(db, Mazera);
            Check("Mazera DeVille: the procedure with Pandemonium and the mandatory Special Summon discard of 3",
                m != null && m.FullyCompiled &&
                Has(m, c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                            c.TributeNamedCard == "Warrior of Zera" && c.RequiresFaceUpName == "Pandemonium") &&
                Has(m, c => c.Timing == EffectTiming.ThisCardSummoned &&
                            c.Action == EffectActionKind.DiscardRandomFromOpponentHand && c.Amount == 3 &&
                            c.RequiresThisSpecialSummoned && !c.IsOptional && c.MakesChainLink &&
                            c.NotAppliedUnlessNamedFaceUp == "Pandemonium" && !c.RequiresControllerNamedCard) &&
                CardEffectStatus.Classify(db.Get(Mazera)) == CardEffectStatusKind.Implemented, Describe(m));

            var mars = Prog(db, Mars);
            Check("The Agent of Force - Mars: unaffected by Spells; ATK/DEF = your LP lead while you control the Sanctuary",
                mars != null && mars.FullyCompiled &&
                Has(mars, c => c.Action == EffectActionKind.UnaffectedByCardEffects && c.UnaffectedByFilter == "Spell" &&
                               string.IsNullOrEmpty(c.RequiresFaceUpName)) &&
                Has(mars, c => c.Action == EffectActionKind.GainSelfAtkPerCount && c.Amount == 1 && c.DefAmount == 1 &&
                               c.CountSource == SanctuaryAndZeraRuntime.LpLeadWhileNamedYouControl &&
                               c.NamedCard == sanct) &&
                CardEffectStatus.Classify(db.Get(Mars)) == CardEffectStatusKind.Implemented, Describe(mars));

            var sat = Prog(db, Saturn);
            Check("The Agent of Judgment - Saturn: Tribute this card; LP-lead damage; the Battle Phase oath",
                sat != null && sat.FullyCompiled &&
                Has(sat, c => c.Timing == EffectTiming.Activate &&
                              c.Action == EffectActionKind.InflictLpLeadAsDamageToOpponent && c.RequiresTributeThis &&
                              c.ActivationCondition == SanctuaryAndZeraRuntime.YourLpHigher &&
                              c.RequiresFaceUpName == sanct && c.RequiresControllerNamedCard &&
                              c.NotAppliedUnlessNamedFaceUp == sanct && c.ForbidsBattlePhaseThisTurn) &&
                CardEffectStatus.Classify(db.Get(Saturn)) == CardEffectStatusKind.Implemented, Describe(sat));

            var loj = Prog(db, LightOfJudgment);
            Check("Light of Judgment: Sanctuary on the field; discard 1 LIGHT monster; the choice at resolution, no target",
                loj != null && loj.FullyCompiled &&
                Has(loj, c => c.Timing == EffectTiming.Activate &&
                              c.Action == EffectActionKind.SendOppFieldCardToGyOrDiscardFromOppHand &&
                              c.RequiresDiscardCost && c.DiscardCostAttribute == "LIGHT" &&
                              c.RequiresFaceUpName == sanct && !c.RequiresControllerNamedCard &&
                              c.ActivationCondition == SanctuaryAndZeraRuntime.OppHandOrFieldCard &&
                              !c.RequiresTargetChoice && !c.IsPsctTarget) &&
                CardEffectStatus.Classify(db.Get(LightOfJudgment)) == CardEffectStatusKind.Implemented,
                Describe(loj));

            // Near misses: one changed phrase keeps the sentence away from these atoms.
            var marsAtkOnly = CompileText("Effect Monster", "Fairy",
                "This card is unaffected by any Spell Cards. While you control a face-up \"The Sanctuary in the Sky\" and " +
                "your Life Points are higher than your opponent's, this card gains ATK by the difference between your and " +
                "your opponent's Life Points.");
            Check("Near miss: an ATK-only LP-lead sentence is not compiled as Mars' ATK and DEF",
                !marsAtkOnly.FullyCompiled ||
                !Has(marsAtkOnly, c => c.CountSource == SanctuaryAndZeraRuntime.LpLeadWhileNamedYouControl),
                Describe(marsAtkOnly));

            var lojTargeted = CompileText("Trap Card", "Normal",
                "If \"The Sanctuary in the Sky\" is on the field: Discard 1 LIGHT monster to the GY; apply 1 of the " +
                "following effects.\n● Look at your opponent's hand and discard 1 card from their hand.\n● Target 1 card " +
                "your opponent controls; send it to the GY.");
            Check("Near miss: a targeting second bullet is not Light of Judgment's choice",
                !lojTargeted.FullyCompiled ||
                !Has(lojTargeted, c => c.Action == EffectActionKind.SendOppFieldCardToGyOrDiscardFromOppHand),
                Describe(lojTargeted));

            var saturnNoOath = CompileText("Effect Monster", "Fairy",
                "While your Life Points are higher than your opponent's, you can Tribute this card to inflict damage to " +
                "your opponent equal to the difference. If you do not control a face-up \"The Sanctuary in the Sky\", " +
                "this effect is not applied.");
            Check("Near miss: Saturn's text without the Battle Phase oath is not compiled as Saturn",
                !saturnNoOath.FullyCompiled ||
                !Has(saturnNoOath, c => c.Action == EffectActionKind.InflictLpLeadAsDamageToOpponent),
                Describe(saturnNoOath));

            var zeratoAnyMonster = CompileText("Effect Monster", "Fairy",
                "This card cannot be Normal Summoned or Set. This card cannot be Special Summoned except by Tributing 1 " +
                "face-up \"Warrior of Zera\" on your side of the field while \"The Sanctuary in the Sky\" is on the field. " +
                "If \"The Sanctuary in the Sky\" is on your side of the field, by discarding 1 LIGHT Monster Card from your " +
                "hand to the Graveyard, destroy all monsters on the field. If \"The Sanctuary in the Sky\" is not on your " +
                "side of the field, this effect is not applied.");
            Check("Near miss: \"destroy all monsters on the field\" is not Zerato's opponent-only sweep",
                !zeratoAnyMonster.FullyCompiled &&
                !Has(zeratoAnyMonster, c => c.Action == EffectActionKind.Destroy &&
                                            c.NotAppliedUnlessNamedFaceUp == "The Sanctuary in the Sky"),
                Describe(zeratoAnyMonster));
        }

        // ───────────────────────────── Archlord Zerato: procedure ─────────────────────────────

        static void ZeratoProcedureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The Sanctuary on the OPPONENT's field is enough ("on the field").
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceField(e, e.Opponent, Sanctuary);
                var zera = PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var z = PutInHand(e, p, Zerato);
                Check("Archlord Zerato: cannot be Normal Summoned or Set", !e.CanNormalSummonOrSet(p, z));
                var can = e.CanSpecialSummonProcedure(p, z);
                var ok = e.TrySpecialSummonProcedure(p, z);
                PassAll(e);
                Check("Archlord Zerato: Special Summoned by Tributing the face-up Warrior of Zera (Sanctuary on either field)",
                    can && ok && Where(e, z) == "PMu" && Where(e, zera) == "PGY" && Where(e, celtic) == "PMu" &&
                    z.ProcedureCompleted && !e.Chain.HasLinks,
                    $"can={can} ok={ok} z={Where(e, z)} zera={Where(e, zera)} celtic={Where(e, celtic)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var z = PutInHand(e, p, Zerato);
                var noSanct = !e.CanSpecialSummonProcedure(p, z) && !e.TrySpecialSummonProcedure(p, z) &&
                              p.Hand.Contains(z);
                PlaceField(e, p, Pandemonium);
                var wrongField = !e.CanSpecialSummonProcedure(p, z);
                Check("Archlord Zerato: no procedure without \"The Sanctuary in the Sky\" (another Field Spell does not do)",
                    noSanct && wrongField, $"noSanct={noSanct} wrongField={wrongField}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceField(e, p, Sanctuary);
                PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, WarriorOfZera, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var z = PutInHand(e, p, Zerato);
                Check("Archlord Zerato: a face-down Warrior of Zera, or the opponent's, cannot be the Tribute",
                    !e.CanSpecialSummonProcedure(p, z) && !e.TrySpecialSummonProcedure(p, z) && p.Hand.Contains(z));
            }

            {
                // Two face-up Warriors of Zera: the player chooses which one is Tributed.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceField(e, p, Sanctuary);
                var a = PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, WarriorOfZera, 1, BattlePosition.Defense, true);
                var z = PutInHand(e, p, Zerato);
                var started = e.TrySpecialSummonProcedure(p, z);
                var legal = Legal(e);
                Check("Archlord Zerato: with 2 face-up Warriors of Zera the player picks the Tribute",
                    started && legal.Count == 2 && legal.Contains(a) && legal.Contains(b) && p.Hand.Contains(z),
                    $"started={started} legal={Names(legal)}");
                e.TrySelectEffectTarget(b);
                PassAll(e);
                Check("Archlord Zerato: the chosen Warrior of Zera is Tributed",
                    Where(e, z) == "PMu" && Where(e, b) == "PGY" && Where(e, a) == "PMu" && z.ProcedureCompleted,
                    $"z={Where(e, z)} a={Where(e, a)} b={Where(e, b)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceField(e, p, Sanctuary);
                var a = PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, WarriorOfZera, 1, BattlePosition.Attack, true);
                var z = PutInHand(e, p, Zerato);
                e.TrySpecialSummonProcedure(p, z);
                e.CancelEffectTargeting();
                Check("Archlord Zerato: CANCEL at the Tribute pick summons nothing and Tributes nothing",
                    p.Hand.Contains(z) && Where(e, a) == "PMu" && Where(e, b) == "PMu" && !z.ProcedureCompleted &&
                    e.PendingActivation == null,
                    $"z={Where(e, z)} a={Where(e, a)} b={Where(e, b)} pend={e.PendingActivation != null}");
            }

            {
                // The revive limit: only a properly summoned copy may come back from the GY.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var z = Gy(e, p, Zerato);
                var blocked = !SpecialSummonLegality.CanBeSpecialSummoned(e, p, z, null, SummonOrigin.Graveyard);
                z.ProcedureCompleted = true;
                var allowed = SpecialSummonLegality.CanBeSpecialSummoned(e, p, z, null, SummonOrigin.Graveyard);
                Check("Archlord Zerato: a copy never properly summoned cannot be revived; a properly summoned one can",
                    blocked && allowed, $"blocked={blocked} allowed={allowed}");
            }
        }

        // ───────────────────────────── Archlord Zerato: ignition ─────────────────────────────

        static void ZeratoIgnitionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Sanctuary);
                var z = PlaceMonster(e, p, Zerato, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var elf = PutInHand(e, p, MysticalElf);
                var celticHand = PutInHand(e, p, Celtic);
                var up = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var down = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Defense, false);
                var can = e.CanActivateSpellTrap(p, z, fromHand: false);
                var started = e.TryActivateSpellTrap(p, z, fromHand: false);
                var costPick = PayDiscard(e, elf);
                PassAll(e);
                Check("Archlord Zerato: discard the LIGHT monster; every monster the opponent controls is destroyed",
                    can && started && costPick && Where(e, elf) == "PGY" && Where(e, celticHand) == "PHand" &&
                    Where(e, up) == "OGY" && Where(e, down) == "OGY" && Where(e, z) == "PMu" && Where(e, mine) == "PMu",
                    $"can={can} started={started} elf={Where(e, elf)} up={Where(e, up)} down={Where(e, down)} " +
                    $"z={Where(e, z)} mine={Where(e, mine)}");
                PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
                PutInHand(e, p, Bewd);
                Check("Archlord Zerato: no once-per-turn limit",
                    e.CanActivateSpellTrap(p, z, fromHand: false));
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, o, Sanctuary);
                var z = PlaceMonster(e, p, Zerato, 0, BattlePosition.Attack, true);
                PutInHand(e, p, MysticalElf);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var oppSanct = !e.CanActivateSpellTrap(p, z, fromHand: false);
                o.FieldSpellZone.Occupant = null;
                PlaceField(e, p, Sanctuary);
                p.Hand.Clear();
                PutInHand(e, p, Celtic);
                PutInHand(e, p, Kuriboh);
                var noLight = !e.CanActivateSpellTrap(p, z, fromHand: false);
                p.Hand.Clear();
                PutInHand(e, p, MysticalElf);
                ClearMonsters(o);
                var noTarget = !e.CanActivateSpellTrap(p, z, fromHand: false);
                Check("Archlord Zerato: needs the Sanctuary on YOUR side, a LIGHT monster to discard and an opponent's monster",
                    oppSanct && noLight && noTarget, $"oppSanct={oppSanct} noLight={noLight} noTarget={noTarget}");
            }

            {
                // "If the Sanctuary is not on your side of the field, this effect is not applied" (resolution re-check).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var z = PlaceMonster(e, p, Zerato, 0, BattlePosition.Attack, true);
                var target = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var clause = ClauseOf(db, Zerato, EffectActionKind.Destroy);
                var a = false;
                var b = false;
                PlaceField(e, o, Sanctuary);
                TextEffectRuntime.ApplyClause(e, p, z, clause, null, ref a, ref b);
                var survived = Where(e, target) == "OMu";
                PlaceField(e, p, Sanctuary);
                TextEffectRuntime.ApplyClause(e, p, z, clause, null, ref a, ref b);
                Check("Archlord Zerato: with no Sanctuary on your side at resolution nothing is destroyed; with it, the sweep",
                    survived && Where(e, target) == "OGY", $"survived={survived} target={Where(e, target)}");
            }
        }

        // ───────────────────────────── Mazera DeVille ─────────────────────────────

        static void MazeraDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Pandemonium on the opponent's field (Lua IsEnvironment with no player: either field).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, o, Pandemonium);
                var zera = PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var m = PutInHand(e, p, Mazera);
                for (var i = 0; i < 4; i++) PutInHand(e, o, i % 2 == 0 ? PotOfGreed : Celtic);
                var can = e.CanSpecialSummonProcedure(p, m);
                var ok = e.TrySpecialSummonProcedure(p, m);
                PassAll(e);
                Check("Mazera DeVille: Special Summoned by Tributing Warrior of Zera; the opponent discards 3 random cards",
                    can && ok && Where(e, m) == "PMu" && Where(e, zera) == "PGY" && m.ProcedureCompleted &&
                    o.Hand.Count == 1 && o.Graveyard.Count == 3,
                    $"can={can} ok={ok} m={Where(e, m)} oHand={o.Hand.Count} oGy={o.Graveyard.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Pandemonium);
                PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var m = PutInHand(e, p, Mazera);
                PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                e.TrySpecialSummonProcedure(p, m);
                PassAll(e);
                Check("Mazera DeVille: with fewer than 3 cards the opponent discards them all",
                    Where(e, m) == "PMu" && o.Hand.Count == 0 && o.Graveyard.Count == 2,
                    $"m={Where(e, m)} oHand={o.Hand.Count} oGy={o.Graveyard.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceField(e, p, Sanctuary);
                PlaceMonster(e, p, WarriorOfZera, 0, BattlePosition.Attack, true);
                var m = PutInHand(e, p, Mazera);
                Check("Mazera DeVille: no procedure without Pandemonium; never Normal Summoned",
                    !e.CanSpecialSummonProcedure(p, m) && !e.CanNormalSummonOrSet(p, m));
            }

            {
                // A properly summoned copy revived by Monster Reborn: the trigger activates, but with no Pandemonium on the
                // field it is not applied. A copy never properly summoned is not a legal Monster Reborn target.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                Gy(e, p, Mazera);
                var reborn = PutInHand(e, p, MonsterReborn);
                var blocked = !e.CanActivateSpellTrap(p, reborn, fromHand: true);
                p.Graveyard.Clear();
                var m = Gy(e, p, Mazera);
                m.ProcedureCompleted = true;
                PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                PutInHand(e, o, Kuriboh);
                var started = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                if (e.PendingActivation != null && Legal(e).Contains(m)) e.TrySelectEffectTarget(m);
                PassAll(e);
                Check("Mazera DeVille: revived without Pandemonium on the field — the discard is not applied",
                    blocked && started && Where(e, m) == "PMu" && o.Hand.Count == 3,
                    $"blocked={blocked} started={started} m={Where(e, m)} oHand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── The Agent of Force - Mars ─────────────────────────────

        static void MarsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mars = PlaceMonster(e, p, Mars, 0, BattlePosition.Attack, true);
                PlaceField(e, p, Sanctuary);
                p.LifePoints = 8000;
                o.LifePoints = 5500;
                FieldSpellEffects.RefreshBoard(e);
                var lead = mars.CurrentAtk == 2500 && mars.CurrentDef == 2500;
                o.LifePoints = 7000;
                FieldSpellEffects.RefreshBoard(e);
                var recomputed = mars.CurrentAtk == 1000 && mars.CurrentDef == 1000;
                o.LifePoints = 9000;
                FieldSpellEffects.RefreshBoard(e);
                var behind = mars.CurrentAtk == 0 && mars.CurrentDef == 0;
                o.LifePoints = 4000;
                p.FieldSpellZone.Occupant = null;
                PlaceField(e, o, Sanctuary);
                FieldSpellEffects.RefreshBoard(e);
                var oppSanct = mars.CurrentAtk == 0 && mars.CurrentDef == 0;
                Check("The Agent of Force - Mars: gains ATK/DEF equal to the LP lead while you control the Sanctuary, " +
                      "recomputed each refresh; 0 when behind or when only the opponent has it",
                    lead && recomputed && behind && oppSanct,
                    $"lead={lead} recomputed={recomputed} behind={behind} oppSanct={oppSanct} atk={mars.CurrentAtk}");
            }

            {
                // Unaffected by Spell Cards, its controller's too: Dark Hole does not destroy it.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mars = PlaceMonster(e, p, Mars, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var dm = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                var started = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("The Agent of Force - Mars: unaffected by its own controller's Dark Hole; the others are destroyed",
                    started && Where(e, mars) == "PMu" && Where(e, celtic) == "PGY" && Where(e, dm) == "OGY",
                    $"started={started} mars={Where(e, mars)} celtic={Where(e, celtic)} dm={Where(e, dm)}");
            }
        }

        // ───────────────────────────── The Agent of Judgment - Saturn ─────────────────────────────

        static void SaturnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Sanctuary);
                var sat = PlaceMonster(e, p, Saturn, 0, BattlePosition.Attack, true);
                p.LifePoints = 8000;
                o.LifePoints = 6000;
                var can = e.CanActivateSpellTrap(p, sat, fromHand: false);
                var started = e.TryActivateSpellTrap(p, sat, fromHand: false);
                var tributed = Where(e, sat) == "PGY";
                PassAll(e);
                Check("The Agent of Judgment - Saturn: Tribute it; the opponent takes damage equal to the LP lead (2000)",
                    can && started && tributed && o.LifePoints == 4000 && p.LifePoints == 8000,
                    $"can={can} started={started} tributed={tributed} oLp={o.LifePoints}");
                Check("The Agent of Judgment - Saturn: you cannot conduct your Battle Phase this turn",
                    TributeSummonsRuntime.BattlePhaseForbidden(e, p));
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Sanctuary);
                var sat = PlaceMonster(e, p, Saturn, 0, BattlePosition.Attack, true);
                p.LifePoints = 6000;
                o.LifePoints = 6000;
                var equal = !e.CanActivateSpellTrap(p, sat, fromHand: false);
                p.LifePoints = 8000;
                p.FieldSpellZone.Occupant = null;
                PlaceField(e, o, Sanctuary);
                var oppSanct = !e.CanActivateSpellTrap(p, sat, fromHand: false);
                Check("The Agent of Judgment - Saturn: not while the LP are equal, nor with only the opponent's Sanctuary",
                    equal && oppSanct && Where(e, sat) == "PMu", $"equal={equal} oppSanct={oppSanct}");
            }

            {
                // Resolution without the Sanctuary on your side: no damage; the oath still binds (it is part of the cost).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 8000;
                o.LifePoints = 6000;
                var sat = Gy(e, p, Saturn);
                var clause = ClauseOf(db, Saturn, EffectActionKind.InflictLpLeadAsDamageToOpponent);
                var a = false;
                var b = false;
                TextEffectRuntime.ApplyClause(e, p, sat, clause, null, ref a, ref b);
                Check("The Agent of Judgment - Saturn: not applied without the Sanctuary on your side; the oath binds",
                    o.LifePoints == 6000 && TributeSummonsRuntime.BattlePhaseForbidden(e, p),
                    $"oLp={o.LifePoints}");
            }
        }

        // ───────────────────────────── Light of Judgment ─────────────────────────────

        sealed class LojSetup
        {
            public DuelEngine E;
            public CardInstance Loj, Elf, OppMonster, OppSet, HandDm, HandPot;
        }

        static LojSetup LojBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            PlaceField(e, o, Sanctuary); // either field
            return new LojSetup
            {
                E = e,
                Loj = PlaceSetTrap(e, p, LightOfJudgment, 0),
                Elf = PutInHand(e, p, MysticalElf),
                OppMonster = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true),
                OppSet = PlaceSetTrap(e, o, PotOfGreed, 0),
                HandDm = PutInHand(e, o, DarkMagician),
                HandPot = PutInHand(e, o, PotOfGreed)
            };
        }

        static void LightOfJudgmentDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var s = LojBoard(db, pDeck, aDeck);
                var e = s.E;
                var can = e.CanActivateSpellTrap(e.Player, s.Loj, fromHand: false);
                var started = e.TryActivateSpellTrap(e.Player, s.Loj, fromHand: false);
                var costPick = PayDiscard(e, s.Elf);
                var paid = costPick && Where(e, s.Elf) == "PGY";
                PassAll(e);
                var legal = Legal(e);
                Check("Light of Judgment: discard the LIGHT monster; at resolution the opponent's cards and this card " +
                      "(the hand option) are offered, never the hand itself",
                    can && started && paid && legal.Contains(s.OppMonster) && legal.Contains(s.OppSet) &&
                    legal.Contains(s.Loj) && !legal.Contains(s.HandDm) && !legal.Contains(s.HandPot),
                    $"can={can} started={started} paid={paid} legal={Names(legal)}");
                e.TrySelectEffectTarget(s.OppSet);
                PassAll(e);
                Check("Light of Judgment: the chosen face-down card the opponent controls is sent to the GY",
                    Where(e, s.OppSet) == "OGY" && Where(e, s.OppMonster) == "OMu" && e.Opponent.Hand.Count == 2 &&
                    Where(e, s.Loj) == "PGY" && e.PendingActivation == null,
                    $"set={Where(e, s.OppSet)} mon={Where(e, s.OppMonster)} oHand={e.Opponent.Hand.Count} " +
                    $"loj={Where(e, s.Loj)}");
            }

            {
                var s = LojBoard(db, pDeck, aDeck);
                var e = s.E;
                e.TryActivateSpellTrap(e.Player, s.Loj, fromHand: false);
                PayDiscard(e, s.Elf);
                PassAll(e);
                e.TrySelectEffectTarget(s.Loj);
                var hand = Legal(e);
                Check("Light of Judgment: tapping this card looks at the opponent's hand and offers its cards",
                    hand.Count == 2 && hand.Contains(s.HandDm) && hand.Contains(s.HandPot), $"legal={Names(hand)}");
                e.TrySelectEffectTarget(s.HandDm);
                PassAll(e);
                Check("Light of Judgment: the chosen card is discarded from the opponent's hand; their field is untouched",
                    Where(e, s.HandDm) == "OGY" && Where(e, s.HandPot) == "OHand" && Where(e, s.OppMonster) == "OMu" &&
                    Where(e, s.OppSet) == "OSd",
                    $"dm={Where(e, s.HandDm)} pot={Where(e, s.HandPot)} mon={Where(e, s.OppMonster)} " +
                    $"set={Where(e, s.OppSet)}");
            }

            {
                var s = LojBoard(db, pDeck, aDeck);
                var e = s.E;
                e.TryActivateSpellTrap(e.Player, s.Loj, fromHand: false);
                PayDiscard(e, s.Elf);
                PassAll(e);
                e.CancelEffectTargeting();
                var hand = Legal(e);
                Check("Light of Judgment: CANCEL at the first pick chooses the hand option",
                    hand.Count == 2 && hand.Contains(s.HandDm) && hand.Contains(s.HandPot) &&
                    Where(e, s.OppMonster) == "OMu", $"legal={Names(hand)}");
            }

            {
                // Only the hand option is left (the opponent controls no card): it is applied with no option prompt.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Sanctuary);
                var loj = PlaceSetTrap(e, p, LightOfJudgment, 0);
                var bewd = PutInHand(e, p, Bewd);
                var dm = PutInHand(e, o, DarkMagician);
                var pot = PutInHand(e, o, PotOfGreed);
                var started = e.TryActivateSpellTrap(p, loj, fromHand: false);
                PayDiscard(e, bewd);
                PassAll(e);
                var hand = Legal(e);
                var offered = hand.Count == 2 && hand.Contains(dm) && hand.Contains(pot);
                e.TrySelectEffectTarget(pot);
                PassAll(e);
                Check("Light of Judgment: with no card on the opponent's field it goes straight to their hand",
                    started && offered && Where(e, pot) == "OGY" && Where(e, dm) == "OHand",
                    $"started={started} legal={Names(hand)} pot={Where(e, pot)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var loj = PlaceSetTrap(e, p, LightOfJudgment, 0);
                PutInHand(e, p, MysticalElf);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var noSanct = !e.CanActivateSpellTrap(p, loj, fromHand: false);
                PlaceField(e, p, Sanctuary);
                p.Hand.Clear();
                PutInHand(e, p, Kuriboh);
                var noLight = !e.CanActivateSpellTrap(p, loj, fromHand: false);
                p.Hand.Clear();
                PutInHand(e, p, MysticalElf);
                ClearMonsters(o);
                o.Hand.Clear();
                var nothing = !e.CanActivateSpellTrap(p, loj, fromHand: false);
                Check("Light of Judgment: needs the Sanctuary on the field, a LIGHT monster to discard and an opponent's card",
                    noSanct && noLight && nothing && Where(e, loj) == "PSd",
                    $"noSanct={noSanct} noLight={noLight} nothing={nothing}");
            }

            {
                // The AI's resolution: it sends the opponent's strongest monster to the GY.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var weak = PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);
                var strong = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                PutInHand(e, p, PotOfGreed);
                var clause = ClauseOf(db, LightOfJudgment, EffectActionKind.SendOppFieldCardToGyOrDiscardFromOppHand);
                SanctuaryAndZeraRuntime.ResolveSendOrDiscard(e, o, null, clause);
                Check("Light of Judgment (AI): sends the opponent's strongest monster to the GY, with no prompt",
                    Where(e, strong) == "PGY" && Where(e, weak) == "PMu" && p.Hand.Count == 1 &&
                    e.PendingActivation == null, $"strong={Where(e, strong)} weak={Where(e, weak)}");
            }
        }
    }
}
