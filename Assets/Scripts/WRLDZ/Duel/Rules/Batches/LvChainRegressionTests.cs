using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// LvChain batch (v179): Horus the Black Flame Dragon LV4/LV6, Ultimate Insect LV1, Dark Mimic LV3 and
    /// The Graveyard in the Fourth Dimension (<see cref="LvChainAtoms"/>, <see cref="LvChainRuntime"/>).
    /// Compile shapes, near misses, and mini-duels for the "unaffected by Spell effects" marker, the
    /// "send it to the GY, Special Summon the next LV" progressions (with the not-turn-summoned rider), the
    /// Dark Mimic conditional draw and the "LV" Graveyard-to-Deck shuffle.
    /// </summary>
    public static class LvChainRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int HorusLv4 = 75830094;
        const int HorusLv6 = 11224103;
        const int HorusLv8 = 48229808;
        const int UltimateInsectLv1 = 49441499;
        const int UltimateInsectLv3 = 34088136;
        const int DarkMimicLv1 = 74713516;
        const int DarkMimicLv3 = 1102515;
        const int GraveyardFourth = 88089103;

        const int Celtic = 91152256;      // Normal Lv4 EARTH 1400/1200 (not an "LV" monster)
        const int GeminiElf = 69140098;   // Normal Lv4 EARTH 1900/900
        const int DarkHole = 53129443;    // Normal Spell

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("LvChain: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            ImmunityDuels(db, pDeck, aDeck, Check);
            HorusProgressionDuels(db, pDeck, aDeck, Check);
            InsectProgressionDuels(db, pDeck, aDeck, Check);
            DarkMimicDuels(db, pDeck, aDeck, Check);
            GraveyardFourthDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments)}]";

        static CompiledCardProgram Compile(int id, CardDatabase db) =>
            db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;

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
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            Wipe(engine);
            return engine;
        }

        static void Wipe(DuelEngine engine)
        {
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }
        }

        static CardInstance ToGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 10 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool OnField(DuelistState who, int id) =>
            who.MonstersOnField().Any(m => m?.Def != null && m.Def.id == id);

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            foreach (var id in ids) who.Deck.Add(id);
        }

        static string Names(IEnumerable<CardInstance> list) =>
            list == null ? "(no prompt)" : string.Join(",", list.Select(c => c?.Name));

        static CompiledCardProgram CompileText(string text, bool monster) =>
            CardTextEffectCompiler.Compile(monster
                ? new CardDef
                {
                    id = 990183, name = "Near-miss LV monster", type = "Effect Monster", frameType = "effect",
                    race = "Dragon", attribute = "FIRE", level = 4, atk = 1600, def = 1000, desc = text
                }
                : new CardDef
                {
                    id = 990184, name = "Near-miss LV Spell", type = "Spell Card", frameType = "spell",
                    race = "Normal", desc = text
                });

        static bool FromThisBatch(EffectClause c) =>
            c != null &&
            ((c.Action == EffectActionKind.UnaffectedByCardEffects &&
              string.Equals(c.UnaffectedByFilter, "Spell", StringComparison.OrdinalIgnoreCase)) ||
             (c.Timing == EffectTiming.StandbyPhase && c.ActivationCondition == LvChainRuntime.NotTurnSummoned) ||
             c.Action == EffectActionKind.DrawAmountWhenSpecialSummonedByNamed ||
             (c.Action == EffectActionKind.ReturnChosenToDeckShuffle &&
              string.Equals(c.TargetSeriesName, "LV", StringComparison.OrdinalIgnoreCase)));

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id, out bool implemented)
            {
                var def = db.Get(id);
                implemented = def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                return def != null ? CardTextEffectCompiler.Compile(def) : null;
            }

            {
                var p = P(HorusLv4, out var impl);
                var body = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.ContinuousWhileFaceUp);
                var prog = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.EndPhase);
                Check("Horus LV4: control lock + End Phase LV6 progression after a battle destruction",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    body != null && body.Action == EffectActionKind.ControlOfThisCannotSwitch && body.Side == EffectSide.Both &&
                    prog != null && prog.Action == EffectActionKind.SpecialSummonNamed &&
                    prog.NamedCard == "Horus the Black Flame Dragon LV6" && prog.RequiresSendThisToGy &&
                    prog.RequiresDestroyedByBattleThisTurn && prog.FromHand && prog.FromDeck && prog.IsOptional,
                    Describe(p));
            }
            {
                var p = P(HorusLv6, out var impl);
                var imm = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.ContinuousWhileFaceUp);
                var prog = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.EndPhase);
                Check("Horus LV6: unaffected by Spell effects + End Phase LV8 progression",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    imm != null && imm.Action == EffectActionKind.UnaffectedByCardEffects && !imm.MakesChainLink &&
                    string.Equals(imm.UnaffectedByFilter, "Spell", StringComparison.OrdinalIgnoreCase) &&
                    prog != null && prog.Action == EffectActionKind.SpecialSummonNamed &&
                    prog.NamedCard == "Horus the Black Flame Dragon LV8" && prog.RequiresDestroyedByBattleThisTurn,
                    Describe(p));
            }
            {
                var p = P(UltimateInsectLv1, out var impl);
                var imm = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.ContinuousWhileFaceUp);
                var prog = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.StandbyPhase);
                Check("Ultimate Insect LV1: unaffected by Spell Cards + Standby LV3 progression with the " +
                      "not-turn-summoned rider",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    imm != null && imm.Action == EffectActionKind.UnaffectedByCardEffects &&
                    string.Equals(imm.UnaffectedByFilter, "Spell", StringComparison.OrdinalIgnoreCase) &&
                    prog != null && prog.Action == EffectActionKind.SpecialSummonNamed &&
                    prog.NamedCard == "Ultimate Insect LV3" &&
                    prog.ActivationCondition == LvChainRuntime.NotTurnSummoned &&
                    prog.CheckedAt == ConditionCheckedAt.Activation && prog.RequiresSendThisToGy &&
                    prog.OnlyDuringOwnStandby && prog.FromHand && prog.FromDeck && prog.IsOptional,
                    Describe(p));
            }
            {
                var p = P(DarkMimicLv3, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Dark Mimic LV3: battle-GY draw 1, or 2 when \"Dark Mimic LV1\" Special Summoned it",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.SentFromFieldToGy &&
                    c.Action == EffectActionKind.DrawAmountWhenSpecialSummonedByNamed && c.Amount == 1 &&
                    c.AmountIfSpecialSummonedByNamed == 2 && c.SpecialSummonedByNamedCard == "Dark Mimic LV1" &&
                    c.RequiresThisDestroyedByBattle && !c.IsOptional,
                    Describe(p));
            }
            {
                var p = P(GraveyardFourth, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("The Graveyard in the Fourth Dimension: exactly 2 \"LV\" monsters in the GY are targeted into the Deck",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.ReturnChosenToDeckShuffle &&
                    c.Zone == EffectZoneFilter.ControllerGyCards && c.TargetSeriesName == "LV" &&
                    c.TargetCount == 2 && !c.TargetUpTo && c.RequiresTargetChoice && c.IsPsctTarget,
                    Describe(p));
            }
            Check("The \"LV\" series covers Ultimate Insect LV1 / Dark Mimic LV3 but not Celtic",
                TextEffectRuntime.CardMatchesSeries(db.Get(UltimateInsectLv1), "LV") &&
                TextEffectRuntime.CardMatchesSeries(db.Get(DarkMimicLv3), "LV") &&
                !TextEffectRuntime.CardMatchesSeries(db.Get(Celtic), "LV"), "");
        }

        static void NearMisses(Chk Check)
        {
            var misses = new (string text, bool monster)[]
            {
                // Trap immunity, not Spell.
                ("While this card is on the field, it is unaffected by the effects of Trap Cards.", true),
                // The progression sentence without the not-turn-summoned rider (the shared generic path).
                ("During your Standby Phase, by sending this face-up card to the Graveyard, Special Summon 1 " +
                 "\"Ultimate Insect LV3\" from your hand or Deck.", true),
                // A plain battle-GY draw with no "summoned by" rider.
                ("If this card is destroyed by battle and sent to the GY: Draw 1 card.", true),
                ("If this card is destroyed by battle and sent to the GY: Draw 2 cards.", true),
                // The Graveyard shuffle with no quoted series name.
                ("Add 2 monsters from your Graveyard to your Deck and shuffle it.", false),
                // The Graveyard to the hand, not the Deck.
                ("Add 2 \"LV\" monsters from your Graveyard to your hand.", false),
                // A quoted series, but to the hand / with a different action.
                ("Add 2 \"LV\" monsters from your Deck to your hand.", false),
            };
            var leaked = misses.Where(n =>
            {
                var p = CompileText(n.text, n.monster);
                return p != null && p.ClauseList.Exists(FromThisBatch);
            }).Select(n => n.text).ToList();
            Check("Near misses: look-alike sentences never compile to this batch's clauses", leaked.Count == 0,
                string.Join(" || ", leaked));
        }

        // ───────────────────────────── Spell immunity ─────────────────────────────

        static void ImmunityDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Horus LV6 ("This card is unaffected by Spell effects.") and Ultimate Insect LV1 ("While this card is on
            // the field, it is unaffected by the effects of Spell Cards.") both survive the player's Dark Hole while a
            // plain monster is destroyed.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var horus = PlaceMonster(e, p, HorusLv6, 0, BattlePosition.Attack, true);
                var insect = PlaceMonster(e, p, UltimateInsectLv1, 1, BattlePosition.Attack, true);
                var ally = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("unaffected by Spell effects: Horus LV6 and Ultimate Insect LV1 survive Dark Hole, Celtic does not",
                    reached && act &&
                    p.TryFindMonster(horus, out _) && horus.FaceUp && OnField(p, HorusLv6) &&
                    p.TryFindMonster(insect, out _) && insect.FaceUp && OnField(p, UltimateInsectLv1) &&
                    p.Graveyard.Contains(ally) && !p.Graveyard.Contains(horus) && !p.Graveyard.Contains(insect),
                    $"act={act} horus={p.TryFindMonster(horus, out _)} insect={p.TryFindMonster(insect, out _)} " +
                    $"celticGy={p.Graveyard.Contains(ally)}");
            }
        }

        // ───────────────────────────── Horus progression ─────────────────────────────

        static void HorusProgressionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // After destroying a monster by battle this turn, the player's End Phase sends Horus LV4 to the GY and
            // Special Summons Horus LV6 from the hand.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var lv4 = PlaceMonster(e, p, HorusLv4, 0, BattlePosition.Attack, true);
                lv4.DestroyedByBattleThisTurn = true;
                var lv6 = PutInHand(e, p, HorusLv6);
                var ended = e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Horus LV4: the End Phase sends it to the GY and Special Summons Horus LV6",
                    reached && ended && p.Graveyard.Contains(lv4) && !p.TryFindMonster(lv4, out _) &&
                    OnField(p, HorusLv6) && !p.Hand.Contains(lv6),
                    $"ended={ended} lv4Gy={p.Graveyard.Contains(lv4)} lv6Field={OnField(p, HorusLv6)} " +
                    $"hand=[{Names(p.Hand)}]");
            }
        }

        // ───────────────────────────── Ultimate Insect progression ─────────────────────────────

        static void InsectProgressionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A settled Ultimate Insect LV1 promotes itself to LV3 during the player's next Standby Phase.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var lv1 = PlaceMonster(e, p, UltimateInsectLv1, 0, BattlePosition.Attack, true);
                var lv3 = PutInHand(e, p, UltimateInsectLv3);
                e.TryEndTurnSafe(p);       // turn 3 → 4 (opponent)
                PassAll(e);
                e.TryEndTurnSafe(e.Opponent); // turn 4 → 5 (player Standby: the trigger fires)
                PassAll(e);
                Check("Ultimate Insect LV1: during your next Standby Phase it sends itself to the GY and Special Summons LV3",
                    reached && p.Graveyard.Contains(lv1) && !p.TryFindMonster(lv1, out _) &&
                    OnField(p, UltimateInsectLv3) && !p.Hand.Contains(lv3),
                    $"lv1Gy={p.Graveyard.Contains(lv1)} lv3Field={OnField(p, UltimateInsectLv3)} hand=[{Names(p.Hand)}]");
            }

            // The not-turn-summoned rider: a monster summoned (or flipped face-up) this turn is blocked; a settled one
            // is allowed.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var lv1 = PlaceMonster(e, p, UltimateInsectLv1, 0, BattlePosition.Attack, true);
                var c = Compile(UltimateInsectLv1, db)?.ClausesFor(EffectTiming.StandbyPhase)?.FirstOrDefault();
                var handled = LvChainRuntime.TryActivationCondition(e, p, lv1, c, out var okSettled, out _);
                lv1.SummonedThisTurn = true;
                LvChainRuntime.TryActivationCondition(e, p, lv1, c, out var okSummoned, out _);
                lv1.SummonedThisTurn = false;
                lv1.FaceUpStamp = lv1.FaceUpStampAtTurnStart + 1; // flipped face-up this turn
                LvChainRuntime.TryActivationCondition(e, p, lv1, c, out var okFlipped, out _);
                lv1.FaceUpStamp = lv1.FaceUpStampAtTurnStart;
                Check("Ultimate Insect LV1 rider: NotTurnSummoned allows a settled monster and blocks a summoned / flipped one",
                    reached && handled && c?.ActivationCondition == LvChainRuntime.NotTurnSummoned &&
                    okSettled && !okSummoned && !okFlipped,
                    $"handled={handled} settled={okSettled} summoned={okSummoned} flipped={okFlipped}");
            }
        }

        // ───────────────────────────── Dark Mimic conditional draw ─────────────────────────────

        static void DarkMimicDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The opponent attacks over Dark Mimic LV3: destroyed by battle → draw 1.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mimic = PlaceMonster(e, p, DarkMimicLv3, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, GeminiElf, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var hand = p.HandCount;
                var atk = battle && e.TryAttack(o, elf, mimic);
                DrainCombat(e);
                PassAll(e);
                Check("Dark Mimic LV3: destroyed by battle → draw 1",
                    reached && atk && p.Graveyard.Contains(mimic) && p.HandCount == hand + 1,
                    $"atk={atk} gy={p.Graveyard.Contains(mimic)} hand={hand}->{p.HandCount}");
            }

            // The same battle destruction, but this copy was Special Summoned by "Dark Mimic LV1": draw 2. The
            // "summoned by" record is written exactly as the SpecialSummonNamed arm writes it.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mimic = PlaceMonster(e, p, DarkMimicLv3, 0, BattlePosition.Attack, true);
                var dm1 = e.CreateCardInstance(DarkMimicLv1);
                LvChainRuntime.NoteSpecialSummoned(p, new EffectClause { NamedCard = "Dark Mimic LV3" }, dm1, null);
                var elf = PlaceMonster(e, o, GeminiElf, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var hand = p.HandCount;
                var atk = battle && e.TryAttack(o, elf, mimic);
                DrainCombat(e);
                PassAll(e);
                Check("Dark Mimic LV3: Special Summoned by \"Dark Mimic LV1\" → the battle destruction draws 2",
                    reached && atk && p.Graveyard.Contains(mimic) && p.HandCount == hand + 2,
                    $"atk={atk} gy={p.Graveyard.Contains(mimic)} hand={hand}->{p.HandCount}");
            }
        }

        // ───────────────────────────── The Graveyard in the Fourth Dimension ─────────────────────────────

        static void GraveyardFourthDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Two "LV" monsters in the GY are the legal targets; a non-"LV" monster is not offered.
            {
                var e = Fresh(db, pDeck, aDeck);
                if (e.IsAwaitingResponse) e.PassResponse();
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                p.Graveyard.Clear();
                var lv1 = ToGy(e, p, UltimateInsectLv1);
                var lv3 = ToGy(e, p, DarkMimicLv3);
                var celtic = ToGy(e, p, Celtic);
                var spell = PutInHand(e, p, GraveyardFourth);
                SetDeck(p);
                var act = e.TryActivateSpellTrap(p, spell, fromHand: true);
                var offered = e.IsAwaitingEffectTarget
                    ? e.PendingActivation.LegalTargets.ToList()
                    : new List<CardInstance>();
                var offeredOk = offered.Contains(lv1) && offered.Contains(lv3) && !offered.Contains(celtic);
                var pick1 = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(lv1);
                var pick2 = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(lv3);
                DrainCombat(e);
                PassAll(e);
                Check("The Graveyard in the Fourth Dimension: only the two \"LV\" monsters are offered and shuffled into the Deck",
                    act && offeredOk && pick1 && pick2 &&
                    !p.Graveyard.Contains(lv1) && !p.Graveyard.Contains(lv3) && p.Graveyard.Contains(celtic) &&
                    p.Deck.Contains(UltimateInsectLv1) && p.Deck.Contains(DarkMimicLv3) && p.Deck.Count == 2,
                    $"act={act} offered=[{Names(offered)}] pick={pick1}/{pick2} gy=[{Names(p.Graveyard)}] " +
                    $"deck={p.Deck.Count}");
            }
        }
    }
}
