using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SpiritsAndMisc batch: Yata-Garasu, Hino-Kagu-Tsuchi, Yamata Dragon (LOD Spirits) and Kiseitai. Compile
    /// shapes, mini-duels through the real engine entry points (TryNormalSummon with Tributes, TryFlipSummon,
    /// TryAttack, EndTurn and the next turn's Draw / Standby Phases) and fail-closed near misses. Major Riot was
    /// uncompiled here (no activation window for "returned to your hand by your opponent's effect"); it landed with the
    /// ForcedActivationAndSubstitution batch (v163) and its guard below asserts the compiled shape.
    /// Runtime: <see cref="SpiritsAndMiscRuntime"/>.
    /// </summary>
    public static class SpiritsAndMiscRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int YataGarasu = 3078576;       // Spirit Lv2 200/100
        const int HinoKaguTsuchi = 75745607;  // Spirit Lv8 2800/2900
        const int YamataDragon = 76862289;    // Spirit Lv7 2600/3100
        const int Kiseitai = 4266839;         // Lv2 300/800
        const int MajorRiot = 9074847;
        const int GreatLongNose = 2356994;    // Spirit: skip the next Battle Phase (SpiritMonsters batch)
        const int MirageOfNightmare = 41482598;

        const int Celtic = 91152256;          // 1400/1200
        const int OjamaGreen = 12482652;      // 0/1000
        const int PotOfGreed = 55144522;
        const int EmperorsHoliday = 68400115;
        const int ImperialOrder = 61740673;
        const int ArmedNinja = 9076207;       // FLIP: Target 1 Spell Card on the field; destroy that target.
        const int BookOfMoon = 14087893;      // Target 1 face-up monster; change it to face-down Defense Position.
        const int LegendarySword = 61854111;  // Equip only to a Warrior: +300 ATK/DEF.
        const int AxeOfDespair = 40619825;    // Equip: +1000 ATK.
        const int Relinquished = 64631466;    // 0/0: "equip that target to this card"; ATK/DEF become its.
        const int BlueEyes = 89631139;        // 3000/2500
        const int PrematureBurial = 70828912; // Pay 800: revive; when this card is destroyed, destroy that monster.

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SpiritsAndMisc: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            SpiritReturnDuels(db, pDeck, aDeck, Check);
            YataDuels(db, pDeck, aDeck, Check);
            HinoDuels(db, pDeck, aDeck, Check);
            YamataDuels(db, pDeck, aDeck, Check);
            KiseitaiDuels(db, pDeck, aDeck, Check);
            KiseitaiEquipRuleDuels(db, pDeck, aDeck, Check);
            KiseitaiAi(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
            HandDisruptionSkippedTurnDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        /// <summary>The player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards and hands empty.</summary>
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

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool Has(CompiledCardProgram prog, Func<EffectClause, bool> shape) =>
            prog != null && prog.ClauseList.Exists(c => c != null && shape(c));

        static bool SpiritReturnShape(EffectClause c) =>
            c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ReturnToHand &&
            c.RequiresSummonedOrFlippedThisTurn;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var yata = CardTextEffectCompiler.Compile(db.Get(YataGarasu));
            Check("Yata-Garasu FullyCompiled: Spirit return + battle damage → opponent skips next Draw Phase",
                yata != null && yata.FullyCompiled && yata.ClauseList.Count == 2 &&
                Has(yata, SpiritReturnShape) &&
                Has(yata, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                               c.Action == EffectActionKind.SkipOpponentNextDrawPhase) &&
                PsctGrammar.BlocksSpecialSummon(yata.SourceText),
                Describe(yata));

            var hino = CardTextEffectCompiler.Compile(db.Get(HinoKaguTsuchi));
            Check("Hino-Kagu-Tsuchi FullyCompiled: Spirit return + battle damage → hand discard at next Draw Phase",
                hino != null && hino.FullyCompiled && hino.ClauseList.Count == 2 &&
                Has(hino, SpiritReturnShape) &&
                Has(hino, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                               c.Action == EffectActionKind.DiscardOpponentHandAtNextDrawPhase) &&
                PsctGrammar.BlocksSpecialSummon(hino.SourceText),
                Describe(hino));

            var yamata = CardTextEffectCompiler.Compile(db.Get(YamataDragon));
            Check("Yamata Dragon FullyCompiled: Spirit return + battle damage → draw until 5",
                yamata != null && yamata.FullyCompiled && yamata.ClauseList.Count == 2 &&
                Has(yamata, SpiritReturnShape) &&
                Has(yamata, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                 c.Action == EffectActionKind.DrawUntilHandSize && c.Amount == 5 &&
                                 c.Side == EffectSide.Controller) &&
                PsctGrammar.BlocksSpecialSummon(yamata.SourceText),
                Describe(yamata));

            var kis = CardTextEffectCompiler.Compile(db.Get(Kiseitai));
            Check("Kiseitai FullyCompiled: attacked face-down → Equip on the attacker; opp Standby → half its ATK",
                kis != null && kis.FullyCompiled && kis.ClauseList.Count == 2 &&
                Has(kis, c => c.Timing == EffectTiming.StartOfDamageStep &&
                              c.Action == EffectActionKind.BecomeEquipOnFaceDownAttacker &&
                              c.RequiresThisIsAttackTarget) &&
                Has(kis, c => c.Timing == EffectTiming.StandbyPhase &&
                              c.Action == EffectActionKind.GainLpHalfEquippedMonsterAtk &&
                              c.OpponentTurnOnly && c.Side == EffectSide.Controller),
                Describe(kis));

            foreach (var id in new[] { YataGarasu, HinoKaguTsuchi, YamataDragon, Kiseitai })
                Check($"{db.Get(id)?.name} classifies Implemented",
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented);

            Check("New actions map to shared resolution kinds (Draw / Discard / Equip / GainLp)",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.DrawUntilHandSize }) ==
                EffectResolutionKind.Draw &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.DiscardOpponentHandAtNextDrawPhase }) == EffectResolutionKind.Discard &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.BecomeEquipOnFaceDownAttacker }) == EffectResolutionKind.Equip &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.GainLpHalfEquippedMonsterAtk }) == EffectResolutionKind.GainLp);
        }

        // ───────────────────────────── Spirit return ─────────────────────────────

        static void SpiritReturnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Normal Summoned → back to the hand at this turn's End Phase; cannot be Special Summoned.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var yata = PutInHand(engine, p, YataGarasu);
                var ss = engine.SpecialSummonToField(p, yata, BattlePosition.Attack, true);
                Check("Yata-Garasu cannot be Special Summoned", !ss && p.Hand.Contains(yata),
                    $"ss={ss}");
                var ns = engine.TryNormalSummon(p, yata, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var onField = p.TryFindMonster(yata, out _);
                var turn = engine.TurnNumber;
                EndTurn(engine);
                Check("Spirit: Normal Summoned this turn → returns to its owner's hand during the End Phase",
                    reached && ns && onField && p.Hand.Contains(yata) && !p.TryFindMonster(yata, out _) &&
                    engine.TurnNumber == turn + 1,
                    $"reached={reached} ns={ns} onField={onField} inHand={p.Hand.Contains(yata)} turn={turn}->{engine.TurnNumber}");
            }

            {
                // Tribute Summon is a Normal Summon (Hino-Kagu-Tsuchi, 2 Tributes).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var a = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var hino = PutInHand(engine, p, HinoKaguTsuchi);
                engine.ToggleTribute(p, a);
                engine.ToggleTribute(p, b);
                var ts = engine.TryNormalSummon(p, hino, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var onField = p.TryFindMonster(hino, out _);
                EndTurn(engine);
                Check("Spirit: Tribute Summoned this turn → returns to the hand during the End Phase",
                    reached && ts && onField && p.Hand.Contains(hino),
                    $"reached={reached} ts={ts} onField={onField} inHand={p.Hand.Contains(hino)}");
            }

            {
                // Flip Summoned → returns; a face-up Spirit that was neither Summoned nor flipped this turn stays.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var set = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Defense, false);
                var old = PlaceMonster(engine, p, YamataDragon, 1, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, set);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                EndTurn(engine);
                Check("Spirit: Flip Summoned this turn → returns to the hand during the End Phase",
                    reached && flipped && p.Hand.Contains(set) && !p.TryFindMonster(set, out _),
                    $"reached={reached} flipped={flipped} inHand={p.Hand.Contains(set)}");
                Check("Spirit: a face-up Spirit not Summoned or flipped this turn stays on the field",
                    p.TryFindMonster(old, out _) && !p.Hand.Contains(old));
            }

            {
                // Set on the player's turn, flipped face-up by the opponent's attack on their turn → returns during
                // the opponent's End Phase (proc_spirit.lua: EVENT_FLIP, End Phase trigger in either turn).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Defense, false);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                EndTurn(engine);
                var stayed = p.TryFindMonster(yata, out _) && !yata.FaceUp;
                var oppTurn = engine.TurnPlayer == o;
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, o, ojama, yata);
                var flippedUp = yata.FaceUp && p.TryFindMonster(yata, out _) && yata.FlippedFaceUpThisTurn;
                Check("Spirit: a Set Spirit attacked on the opponent's turn is flipped face-up (100 DEF vs 0 ATK)",
                    reached && stayed && oppTurn && attacked && flippedUp && o.LifePoints == lpO - 100,
                    $"reached={reached} stayed={stayed} oppTurn={oppTurn} attacked={attacked} flippedUp={flippedUp} lp={lpO}->{o.LifePoints}");
                Check("Yata-Garasu: battle damage inflicted during the opponent's turn skips no Draw Phase (Lua reset)",
                    !o.SkipNextDrawPhase, $"skip={o.SkipNextDrawPhase}");
                EndTurn(engine);
                Check("Spirit: flipped face-up by battle on the opponent's turn → returns during that End Phase",
                    p.Hand.Contains(yata) && !p.TryFindMonster(yata, out _) && engine.TurnPlayer == p,
                    $"inHand={p.Hand.Contains(yata)} onField={p.TryFindMonster(yata, out _)} turnPlayer={engine.TurnPlayer?.Name}");
            }
        }

        // ───────────────────────────── Yata-Garasu ─────────────────────────────

        static void YataDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Direct attack on the player's turn: 200 damage, the opponent's next Draw Phase is skipped.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PutInHand(engine, p, YataGarasu);
                var ns = engine.TryNormalSummon(p, yata, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, yata, null);
                Check("Yata-Garasu: direct attack inflicts 200 and the opponent will skip their next Draw Phase",
                    reached && ns && attacked && o.LifePoints == lpO - 200 && o.SkipNextDrawPhase,
                    $"reached={reached} ns={ns} attacked={attacked} lp={lpO}->{o.LifePoints} skip={o.SkipNextDrawPhase}");
                var handBefore = o.Hand.Count;
                var deckBefore = o.Deck.Count;
                EndTurn(engine);
                Check("Yata-Garasu: returns at the End Phase; the opponent's Draw Phase is skipped (no card drawn)",
                    p.Hand.Contains(yata) && engine.TurnPlayer == o && o.Hand.Count == handBefore &&
                    o.Deck.Count == deckBefore && !o.SkipNextDrawPhase,
                    $"yataInHand={p.Hand.Contains(yata)} hand={handBefore}->{o.Hand.Count} deck={deckBefore}->{o.Deck.Count} skip={o.SkipNextDrawPhase}");
            }

            {
                // A monster battle (Yata-Garasu attacks a weaker Attack Position monster) counts as battle damage too.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, yata, ojama);
                Check("Yata-Garasu: 200 battle damage from destroying an Attack Position monster → skip",
                    reached && attacked && o.LifePoints == lpO - 200 && o.SkipNextDrawPhase &&
                    o.Graveyard.Contains(ojama),
                    $"attacked={attacked} lp={lpO}->{o.LifePoints} skip={o.SkipNextDrawPhase}");
            }

            {
                // The opponent already skips their next Draw Phase by their own effect (Offerings to the Doomed's
                // count): Yata-Garasu's skip applies to that same Draw Phase and ends with it (Lua reset at the End
                // Phase of the opponent's turn), so their following Draw Phase is not skipped as well.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Attack, true);
                o.SkipNextDrawPhaseCount = 1;
                var attacked = AttackAndDrain(engine, p, yata, null);
                var both = o.SkipNextDrawPhase && o.SkipNextDrawPhaseCount == 1;
                var hand0 = o.Hand.Count;
                EndTurn(engine);
                var firstSkipped = engine.TurnPlayer == o && o.Hand.Count == hand0 &&
                                   o.SkipNextDrawPhaseCount == 0 && !o.SkipNextDrawPhase;
                EndTurn(engine);
                EndTurn(engine);
                Check("Yata-Garasu + the opponent's own skip: one Draw Phase skipped, the next one draws",
                    reached && attacked && both && firstSkipped && engine.TurnPlayer == o &&
                    o.Hand.Count == hand0 + 1,
                    $"attacked={attacked} both={both} firstSkipped={firstSkipped} turnPlayer={engine.TurnPlayer?.Name} hand={hand0}->{o.Hand.Count}");
            }

            {
                // No battle damage (attacks a face-up Defense Position monster with higher DEF) → no skip.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Attack, true);
                var wall = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Defense, true);
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, yata, wall);
                Check("Yata-Garasu: no battle damage to the opponent → no skip",
                    reached && attacked && o.LifePoints == lpO && !o.SkipNextDrawPhase,
                    $"attacked={attacked} lp={lpO}->{o.LifePoints} skip={o.SkipNextDrawPhase}");
            }
        }

        // ───────────────────────────── Hino-Kagu-Tsuchi ─────────────────────────────

        static void HinoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Direct attack: at the opponent's next Draw Phase their whole hand is discarded, then they draw.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hino = PlaceMonster(engine, p, HinoKaguTsuchi, 0, BattlePosition.Attack, true);
                var h1 = PutInHand(engine, o, Celtic);
                var h2 = PutInHand(engine, o, PotOfGreed);
                var h3 = PutInHand(engine, o, OjamaGreen);
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, hino, null);
                Check("Hino-Kagu-Tsuchi: 2800 battle damage schedules the discard; the hand is untouched until then",
                    reached && attacked && o.LifePoints == lpO - 2800 && o.DiscardHandAtNextDrawPhase &&
                    o.Hand.Count == 3,
                    $"attacked={attacked} lp={lpO}->{o.LifePoints} pending={o.DiscardHandAtNextDrawPhase} hand={o.Hand.Count}");
                var deckBefore = o.Deck.Count;
                EndTurn(engine);
                Check("Hino-Kagu-Tsuchi: the opponent discards all 3 cards before their draw, then draws 1",
                    engine.TurnPlayer == o && o.Hand.Count == 1 && !o.Hand.Contains(h1) &&
                    o.Graveyard.Contains(h1) && o.Graveyard.Contains(h2) && o.Graveyard.Contains(h3) &&
                    o.Deck.Count == deckBefore - 1 && !o.DiscardHandAtNextDrawPhase,
                    $"hand={o.Hand.Count} gy={o.Graveyard.Count} deck={deckBefore}->{o.Deck.Count} pending={o.DiscardHandAtNextDrawPhase}");
            }

            {
                // Yata-Garasu + Hino-Kagu-Tsuchi: the skipped Draw Phase has no "before they draw" — the discard
                // lapses (RESET_PHASE+PHASE_DRAW) and never applies to a later Draw Phase.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hino = PlaceMonster(engine, p, HinoKaguTsuchi, 0, BattlePosition.Attack, true);
                var yata = PutInHand(engine, p, YataGarasu);
                engine.TryNormalSummon(p, yata, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                PutInHand(engine, o, Celtic);
                PutInHand(engine, o, PotOfGreed);
                var a1 = AttackAndDrain(engine, p, hino, null);
                var a2 = AttackAndDrain(engine, p, yata, null);
                var both = o.SkipNextDrawPhase && o.DiscardHandAtNextDrawPhase;
                EndTurn(engine);
                Check("Hino-Kagu-Tsuchi + Yata-Garasu: skipped Draw Phase → no discard, no draw, pending cleared",
                    reached && a1 && a2 && both && engine.TurnPlayer == o && o.Hand.Count == 2 &&
                    !o.DiscardHandAtNextDrawPhase,
                    $"a1={a1} a2={a2} both={both} hand={o.Hand.Count} pending={o.DiscardHandAtNextDrawPhase}");
                EndTurn(engine);
                Check("Hino-Kagu-Tsuchi: the lapsed discard does not carry to the player's Draw Phase",
                    engine.TurnPlayer == p && o.Hand.Count == 2, $"hand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── Yamata Dragon ─────────────────────────────

        static void YamataDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var start in new[] { 1, 6 })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yamata = PlaceMonster(engine, p, YamataDragon, 0, BattlePosition.Attack, true);
                for (var i = 0; i < start; i++)
                    PutInHand(engine, p, Celtic);
                var deckBefore = p.Deck.Count;
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, yamata, null);
                var want = Math.Max(5, start);
                Check(start < 5
                        ? "Yamata Dragon: battle damage with 1 card in hand → draws 4 (hand of 5)"
                        : "Yamata Dragon: battle damage with 6 cards in hand → draws nothing",
                    reached && attacked && o.LifePoints == lpO - 2600 && p.Hand.Count == want &&
                    p.Deck.Count == deckBefore - (want - start),
                    $"attacked={attacked} lp={lpO}->{o.LifePoints} hand={start}->{p.Hand.Count} deck={deckBefore}->{p.Deck.Count}");
            }
        }

        // ───────────────────────────── Kiseitai ─────────────────────────────

        static void KiseitaiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacked face-down: becomes an Equip Spell on the attacker (no damage calculation); during each of
                // its controller's opponent's Standby Phases it gains half the equipped monster's ATK.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                var lpP = p.LifePoints;
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, celtic, kis);
                var equipped = !o.TryFindMonster(kis, out _) && o.TryFindSpellTrap(kis, out _) &&
                               kis.EquippedTo == celtic && celtic.Equips.Contains(kis) && kis.FaceUp;
                Check("Kiseitai: attacked face-down → leaves the Monster Zone, Equip Spell equipped to the attacker",
                    reached && attacked && equipped && p.TryFindMonster(celtic, out _),
                    $"attacked={attacked} mz={o.TryFindMonster(kis, out _)} st={o.TryFindSpellTrap(kis, out _)} eq={kis.EquippedTo?.Name}");
                Check("Kiseitai: no damage calculation (no battle damage, nothing destroyed)",
                    p.LifePoints == lpP && o.LifePoints == lpO && !o.Graveyard.Contains(kis) &&
                    !p.Graveyard.Contains(celtic),
                    $"lpP={lpP}->{p.LifePoints} lpO={lpO}->{o.LifePoints}");
                EndTurn(engine);
                Check("Kiseitai: no Life Points during its controller's own Standby Phase",
                    engine.TurnPlayer == o && o.LifePoints == lpO, $"lpO={lpO}->{o.LifePoints}");
                EndTurn(engine);
                Check("Kiseitai: during the opponent's Standby Phase its controller gains 700 (half of 1400)",
                    engine.TurnPlayer == p && o.LifePoints == lpO + 700, $"lpO={lpO}->{o.LifePoints}");

                // The equipped monster leaves the field → the Equip Card is sent to the GY.
                engine.DestroyMonsterPublic(p, celtic);
                Check("Kiseitai: its equipped monster destroyed → Kiseitai goes to its owner's GY",
                    o.Graveyard.Contains(kis) && !o.TryFindSpellTrap(kis, out _) && kis.EquippedTo == null,
                    $"gy={o.Graveyard.Contains(kis)} st={o.TryFindSpellTrap(kis, out _)}");
            }

            {
                // No free Spell & Trap Zone → destroyed instead; still no damage calculation.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                for (var i = 0; i < o.SpellTrapZones.Length; i++)
                    PlaceSetTrap(engine, o, PotOfGreed, i);
                var lpP = p.LifePoints;
                var attacked = AttackAndDrain(engine, p, celtic, kis);
                Check("Kiseitai: no free Spell & Trap Zone → destroyed instead, not equipped, no battle damage",
                    reached && attacked && o.Graveyard.Contains(kis) && kis.EquippedTo == null &&
                    !celtic.Equips.Contains(kis) && p.LifePoints == lpP && p.TryFindMonster(celtic, out _),
                    $"attacked={attacked} gy={o.Graveyard.Contains(kis)} eq={kis.EquippedTo?.Name} lpP={lpP}->{p.LifePoints}");
            }

            {
                // Face-up Defense Position: an ordinary battle (near miss).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, true);
                var attacked = AttackAndDrain(engine, p, celtic, kis);
                Check("Kiseitai: attacked in face-up Defense Position → an ordinary battle (destroyed, not equipped)",
                    reached && attacked && o.Graveyard.Contains(kis) && kis.EquippedTo == null &&
                    !o.TryFindSpellTrap(kis, out _),
                    $"attacked={attacked} gy={o.Graveyard.Contains(kis)} eq={kis.EquippedTo?.Name}");
            }

            foreach (var negator in new[] { EmperorsHoliday, ImperialOrder })
            {
                // An Equip Spell's effect under The Emperor's Holiday (Equip Card effects) or Imperial Order
                // (Spell effects): no Life Points.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                var attacked = AttackAndDrain(engine, p, celtic, kis);
                // On the player's side: its controller (not Kiseitai's) pays any upkeep.
                var neg = PlaceFaceUpSpellTrap(engine, p, negator, 0);
                FieldSpellEffects.RefreshBoard(engine);
                var lpO = o.LifePoints;
                EndTurn(engine); // opponent's turn: no gain anyway
                EndTurn(engine); // player's turn: the Standby Phase where Kiseitai would apply
                Check($"Kiseitai: {neg.Name} face-up → no Life Points during the opponent's Standby Phase",
                    reached && attacked && kis.EquippedTo == celtic && engine.TurnPlayer == p &&
                    p.TryFindSpellTrap(neg, out _) && neg.FaceUp && o.LifePoints == lpO,
                    $"attacked={attacked} eq={kis.EquippedTo?.Name} negOnField={p.TryFindSpellTrap(neg, out _)} lpO={lpO}->{o.LifePoints}");
            }

            {
                // It is a Spell Card while equipped: Armed Ninja's "Target 1 Spell Card on the field" can take it.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                var ninja = PlaceMonster(engine, p, ArmedNinja, 1, BattlePosition.Defense, false);
                var attacked = AttackAndDrain(engine, p, celtic, kis);
                engine.TryEnterMainPhase2(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var flipped = engine.TryFlipSummon(p, ninja);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var offered = !engine.IsAwaitingEffectTarget ||
                              engine.PendingActivation.LegalTargets.Contains(kis);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(kis);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Kiseitai: as an Equip Spell Card it is a legal \"Spell Card on the field\" (Armed Ninja destroys it)",
                    reached && attacked && flipped && offered && o.Graveyard.Contains(kis) &&
                    !o.TryFindSpellTrap(kis, out _) && !celtic.Equips.Contains(kis),
                    $"attacked={attacked} flipped={flipped} offered={offered} gy={o.Graveyard.Contains(kis)}");
            }
        }

        // ───────────────────── Kiseitai: equip rules it reaches (review fixes) ─────────────────────

        static bool ActivateOnTarget(DuelEngine engine, DuelistState who, CardInstance card, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryActivateSpellTrap(who, card, fromHand: true);
            if (engine.IsAwaitingEffectTarget)
                ok &= engine.PendingActivation.LegalTargets.Contains(target) && engine.TrySelectEffectTarget(target);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            FieldSpellEffects.RefreshBoard(engine);
            return ok;
        }

        static void KiseitaiEquipRuleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Book of Moon on Kiseitai's monster: an Equip Card cannot stay on a face-down monster, so Kiseitai is
                // destroyed and gains nothing at the next Standby Phase (review: it stayed and gained 700 each turn).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                var attacked = AttackAndDrain(engine, p, celtic, kis);
                engine.TryEnterMainPhase2(p);
                var moon = ActivateOnTarget(engine, p, PutInHand(engine, p, BookOfMoon), celtic);
                var faceDown = !celtic.FaceUp;
                var gone = o.Graveyard.Contains(kis) && !o.TryFindSpellTrap(kis, out _) && kis.EquippedTo == null &&
                           !celtic.Equips.Contains(kis);
                var lpO = o.LifePoints;
                EndTurn(engine);
                EndTurn(engine);
                Check("Kiseitai: its monster turned face-down (Book of Moon) → Kiseitai destroyed, no LP at the next Standby",
                    reached && attacked && moon && faceDown && gone && engine.TurnPlayer == p && o.LifePoints == lpO,
                    $"attacked={attacked} moon={moon} faceDown={faceDown} gone={gone} st={o.TryFindSpellTrap(kis, out _)} lpO={lpO}->{o.LifePoints}");
            }

            {
                // The same rule for an ordinary Equip Spell: Legendary Sword on Celtic, Book of Moon on Celtic.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var sword = PutInHand(engine, p, LegendarySword);
                var equipped = ActivateOnTarget(engine, p, sword, celtic) && sword.EquippedTo == celtic &&
                               celtic.CurrentAtk == 1700;
                var moon = ActivateOnTarget(engine, p, PutInHand(engine, p, BookOfMoon), celtic);
                Check("Equip rule: an Equip Spell on a monster turned face-down is destroyed (Legendary Sword → GY)",
                    reached && equipped && moon && !celtic.FaceUp && p.Graveyard.Contains(sword) &&
                    !p.TryFindSpellTrap(sword, out _) && sword.EquippedTo == null && celtic.Equips.Count == 0,
                    $"equipped={equipped} moon={moon} faceUp={celtic.FaceUp} gy={p.Graveyard.Contains(sword)} eq={celtic.Equips.Count}");
            }

            {
                // Premature Burial: turned face-down, its monster loses it (destroyed by rule), and "When this card is
                // destroyed, destroy the equipped monster" then destroys the face-down monster too.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var celtic = engine.CreateCardInstance(Celtic);
                celtic.FaceUp = true;
                p.Graveyard.Add(celtic);
                var burial = PutInHand(engine, p, PrematureBurial);
                var revived = ActivateOnTarget(engine, p, burial, celtic) && p.TryFindMonster(celtic, out _) &&
                              burial.EquippedTo == celtic;
                var moon = ActivateOnTarget(engine, p, PutInHand(engine, p, BookOfMoon), celtic);
                Check("Equip rule: Premature Burial's monster turned face-down → Burial destroyed, then its monster too",
                    reached && revived && moon && p.Graveyard.Contains(burial) && !p.TryFindSpellTrap(burial, out _) &&
                    !p.TryFindMonster(celtic, out _) && p.Graveyard.Contains(celtic),
                    $"revived={revived} moon={moon} burialGy={p.Graveyard.Contains(burial)} celticOn={p.TryFindMonster(celtic, out _)} celticGy={p.Graveyard.Contains(celtic)}");
            }

            {
                // Relinquished with nothing absorbed attacks a face-down Kiseitai: Kiseitai is an ordinary Equip Card
                // there (Ignis s.eqfilter). Relinquished stays 0 ATK and may still absorb (review: it became 300/800
                // and could not absorb).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var rel = PlaceMonster(engine, p, Relinquished, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, o, BlueEyes, 2, BattlePosition.Attack, true);
                var attacked = AttackAndDrain(engine, p, rel, kis);
                FieldSpellEffects.RefreshBoard(engine);
                var atk = rel.CurrentAtk;
                engine.TryEnterMainPhase2(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var canAbsorb = engine.CanActivateSpellTrap(p, rel, fromHand: false);
                var absorbed = canAbsorb && engine.TryActivateSpellTrap(p, rel, fromHand: false) &&
                               (!engine.IsAwaitingEffectTarget || engine.TrySelectEffectTarget(prey));
                if (engine.IsAwaitingResponse) engine.PassResponse();
                FieldSpellEffects.RefreshBoard(engine);
                Check("Kiseitai on an empty Relinquished: Relinquished stays 0 ATK and can still absorb (then 1400)",
                    reached && attacked && kis.EquippedTo == rel && atk == 0 && canAbsorb && absorbed &&
                    prey.EquippedTo == rel && rel.CurrentAtk == 1400 && kis.EquippedTo == rel,
                    $"attacked={attacked} eq={kis.EquippedTo?.Name} atk={atk} canAbsorb={canAbsorb} absorbed={absorbed} atkAfter={rel.CurrentAtk}");

                // Kiseitai is first in its equip list; Blue-Eyes attacks Relinquished: the absorbed Celtic is destroyed
                // instead, Kiseitai stays.
                EndTurn(engine);
                if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(o);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var hit = engine.TryAttack(o, bewd, rel);
                DrainCombat(engine);
                Check("Kiseitai equipped before the absorb: battle destroys the absorbed monster instead, Kiseitai stays",
                    absorbed && hit && p.TryFindMonster(rel, out _) && prey.EquippedTo == null &&
                    !rel.Equips.Contains(prey) && kis.EquippedTo == rel && o.TryFindSpellTrap(kis, out _),
                    $"hit={hit} relOn={p.TryFindMonster(rel, out _)} preyEq={prey.EquippedTo?.Name} kisEq={kis.EquippedTo?.Name} kisSt={o.TryFindSpellTrap(kis, out _)}");
            }

            {
                // Relinquished with Celtic absorbed attacks a face-down Kiseitai, then Blue-Eyes attacks Relinquished:
                // Celtic (the absorbed monster) is destroyed instead, never Kiseitai; Relinquished is then 0/0 again.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var rel = PlaceMonster(engine, p, Relinquished, 0, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Defense, false);
                var bewd = PlaceMonster(engine, o, BlueEyes, 2, BattlePosition.Attack, true);
                var absorbed = engine.TryActivateSpellTrap(p, rel, fromHand: false) && engine.TrySelectEffectTarget(prey);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                FieldSpellEffects.RefreshBoard(engine);
                var atk1 = rel.CurrentAtk;
                var attacked = AttackAndDrain(engine, p, rel, kis);
                FieldSpellEffects.RefreshBoard(engine);
                var atk2 = rel.CurrentAtk;
                EndTurn(engine);
                if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(o);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var hit = engine.TryAttack(o, bewd, rel);
                DrainCombat(engine);
                FieldSpellEffects.RefreshBoard(engine);
                Check("Kiseitai on an absorbing Relinquished: ATK stays the absorbed 1400; battle destroys the absorbed monster, not Kiseitai",
                    reached && absorbed && attacked && hit && atk1 == 1400 && atk2 == 1400 &&
                    p.TryFindMonster(rel, out _) && !rel.Equips.Contains(prey) && prey.EquippedTo == null &&
                    kis.EquippedTo == rel && o.TryFindSpellTrap(kis, out _) && rel.CurrentAtk == 0,
                    $"absorbed={absorbed} atk1={atk1} attacked={attacked} atk2={atk2} hit={hit} relOn={p.TryFindMonster(rel, out _)} " +
                    $"equips={string.Join(",", rel.Equips.Select(e => e.Name))} kisSt={o.TryFindSpellTrap(kis, out _)} atk3={rel.CurrentAtk}");
            }

            {
                // An Equip Spell on an empty Relinquished is an ordinary Equip Card too: Axe of Despair gives +1000
                // (before: Relinquished took the Axe's printed 0/0 as if it had absorbed it).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var rel = PlaceMonster(engine, p, Relinquished, 0, BattlePosition.Attack, true);
                var axe = PutInHand(engine, p, AxeOfDespair);
                var equipped = ActivateOnTarget(engine, p, axe, rel) && axe.EquippedTo == rel;
                Check("Relinquished with only an Equip Spell (Axe of Despair): 1000 ATK, nothing counted as absorbed",
                    reached && equipped && rel.CurrentAtk == 1000 && TextEffectRuntime.AbsorbedEquipOf(rel) == null,
                    $"equipped={equipped} atk={rel.CurrentAtk}");

                // Blue-Eyes attacks it: with nothing absorbed there is no substitute — Relinquished is destroyed, and
                // the Axe goes with it (not destroyed in its place).
                var o = engine.Opponent;
                var bewd = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                EndTurn(engine);
                if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(o);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var hit = engine.TryAttack(o, bewd, rel);
                DrainCombat(engine);
                Check("Relinquished with only an Equip Spell is destroyed by battle (the Equip Spell is no substitute)",
                    equipped && hit && !p.TryFindMonster(rel, out _) && p.Graveyard.Contains(rel),
                    $"hit={hit} relOn={p.TryFindMonster(rel, out _)} relGy={p.Graveyard.Contains(rel)} axeGy={p.Graveyard.Contains(axe)}");
            }

            {
                // Kiseitai leaves its Monster Zone: an Equip Card still recorded on it (only reachable if a board
                // refresh has not run yet) loses its monster and goes to the GY instead of staying linked.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var kis = PlaceMonster(engine, o, Kiseitai, 0, BattlePosition.Attack, true);
                var axe = PlaceFaceUpSpellTrap(engine, p, AxeOfDespair, 0);
                axe.EquippedTo = kis;
                kis.Equips.Add(axe);
                var moved = SpiritsAndMiscRuntime.TryBecomeEquipOnFaceDownAttacker(engine, p, celtic, kis);
                Check("Kiseitai becomes an Equip Card: the Equip Card on it goes to the GY, unlinked",
                    reached && moved && kis.EquippedTo == celtic && o.TryFindSpellTrap(kis, out _) &&
                    kis.Equips.Count == 0 && axe.EquippedTo == null && p.Graveyard.Contains(axe) &&
                    !p.TryFindSpellTrap(axe, out _),
                    $"moved={moved} kisEq={kis.EquippedTo?.Name} kisEquips={kis.Equips.Count} axeEq={axe.EquippedTo?.Name} axeGy={p.Graveyard.Contains(axe)}");

                // Its gain never reads a face-down monster, even before a board refresh destroys it.
                celtic.FaceUp = false;
                var lpO = o.LifePoints;
                SpiritsAndMiscRuntime.GainLpHalfEquippedMonsterAtk(engine, o, kis);
                Check("Kiseitai: no Life Points from a face-down monster",
                    moved && o.LifePoints == lpO, $"lpO={lpO}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── Kiseitai: AI ─────────────────────────────

        /// <summary>Step SimpleAi's turn coroutine synchronously; the player passes every window.</summary>
        static bool RunSimpleAiTurn(DuelEngine engine, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(engine, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (engine.PendingResponse != null && engine.PendingResponse.Responder == engine.Player)
                        engine.PassResponse();
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static void KiseitaiAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // SimpleAi Sets Kiseitai face-down Defense (its effect needs an attack on it face-down).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                EndTurn(engine);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                o.Hand.Clear();
                var kis = PutInHand(engine, o, Kiseitai);
                var ran = engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 && RunSimpleAiTurn(engine, 400);
                Check("Kiseitai: SimpleAi Sets it face-down Defense, not a face-up Attack Position summon",
                    reached && ran && o.TryFindMonster(kis, out _) && !kis.FaceUp &&
                    kis.Position == BattlePosition.Defense,
                    $"ran={ran} onField={o.TryFindMonster(kis, out _)} faceUp={kis.FaceUp} pos={kis.Position}");
                Check("Kiseitai: AI hint only for the compiled face-down-attacked clause (Celtic is not)",
                    SpiritsAndMiscRuntime.AiSetsFaceDown(kis) &&
                    !SpiritsAndMiscRuntime.AiSetsFaceDown(engine.CreateCardInstance(Celtic)));
            }

            {
                // The stress agent (AI-vs-AI duels) does the same.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                EndTurn(engine);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                o.Hand.Clear();
                var kis = PutInHand(engine, o, Kiseitai);
                var inMain = engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1;
                DuelStressAgent.PlayTurn(engine, o);
                Check("Kiseitai: DuelStressAgent Sets it face-down Defense",
                    reached && inMain && o.TryFindMonster(kis, out _) && !kis.FaceUp &&
                    kis.Position == BattlePosition.Defense,
                    $"inMain={inMain} onField={o.TryFindMonster(kis, out _)} faceUp={kis.FaceUp} pos={kis.Position}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Syn(int id, string type, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef { id = id, name = "Near miss " + id, type = type, desc = desc });

            var battlePhaseSkip = Syn(90500001, "Spirit Monster",
                "This card cannot be Special Summoned. This card returns to its owner's hand during the End Phase of " +
                "the turn it is Normal Summoned or flipped face-up. When this card inflicts Battle Damage to your " +
                "opponent, they skip their next Battle Phase.");
            Check("Near miss: \"skip their next Battle Phase\" is not the Yata-Garasu skip-Draw atom",
                battlePhaseSkip != null && !battlePhaseSkip.FullyCompiled &&
                !Has(battlePhaseSkip, c => c.Action == EffectActionKind.SkipOpponentNextDrawPhase) &&
                Has(battlePhaseSkip, SpiritReturnShape),
                Describe(battlePhaseSkip));

            var drawRider = Syn(90500002, "Effect Monster",
                "When this card inflicts Battle Damage to your opponent, draw cards until you have 5 cards in your " +
                "hand, then discard 1 card.");
            Check("Near miss: draw-until-5 with a discard rider does not compile to DrawUntilHandSize",
                drawRider != null && !drawRider.FullyCompiled &&
                !Has(drawRider, c => c.Action == EffectActionKind.DrawUntilHandSize),
                Describe(drawRider));

            var discardOne = Syn(90500003, "Effect Monster",
                "If this card inflicts Battle Damage to your opponent, they discard 1 card in their hand during the " +
                "next Draw Phase, before they draw.");
            Check("Near miss: \"discard 1 card … next Draw Phase\" is not the whole-hand discard",
                discardOne != null &&
                !Has(discardOne, c => c.Action == EffectActionKind.DiscardOpponentHandAtNextDrawPhase),
                Describe(discardOne));

            var faceUpKis = Syn(90500004, "Effect Monster",
                "When your opponent's monster attacks this card in face-up Defense Position, this card becomes an " +
                "Equip Spell Card equipped to the attacking monster (No damage calculation is conducted). During " +
                "each of your opponent's Standby Phases, increase your Life Points by half of the ATK of the " +
                "monster equipped with this card.");
            Check("Near miss: a face-up-Defense Kiseitai wording does not compile to the Kiseitai atom",
                faceUpKis != null && !faceUpKis.FullyCompiled &&
                !Has(faceUpKis, c => c.Action == EffectActionKind.BecomeEquipOnFaceDownAttacker ||
                                     c.Action == EffectActionKind.GainLpHalfEquippedMonsterAtk),
                Describe(faceUpKis));

            var mirage = CardTextEffectCompiler.Compile(db.Get(MirageOfNightmare));
            // PhaseTimersAndDeckTop batch (v163): Mirage compiles to its own draw-with-record and Standby discard actions.
            Check("Near miss: Mirage of Nightmare's draw-until-4 is not the Yamata Dragon atom",
                mirage != null && !Has(mirage, c => c.Action == EffectActionKind.DrawUntilHandSize) &&
                Has(mirage, c => c.Action == EffectActionKind.DrawUntilHandSizeRecordForNextStandbyDiscard) &&
                Has(mirage, c => c.Action == EffectActionKind.DiscardRandomRecordedDrawCountAtStandby),
                Describe(mirage));

            // Great Long Nose is compiled by the SpiritMonsters batch to its own skip-Battle-Phase action; Yata-Garasu's
            // skip-Draw atom stays separate in both directions.
            var nose = CardTextEffectCompiler.Compile(db.Get(GreatLongNose));
            var yataProg = CardTextEffectCompiler.Compile(db.Get(YataGarasu));
            Check("Great Long Nose: Spirit return + OpponentSkipsNextBattlePhase, never Yata-Garasu's skip-Draw",
                nose != null && nose.FullyCompiled && Has(nose, SpiritReturnShape) &&
                Has(nose, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                               c.Action == EffectActionKind.OpponentSkipsNextBattlePhase) &&
                !Has(nose, c => c.Action == EffectActionKind.SkipOpponentNextDrawPhase) &&
                CardEffectStatus.Classify(db.Get(GreatLongNose)) == CardEffectStatusKind.Implemented,
                Describe(nose));
            Check("Yata-Garasu does not compile the skip-Battle-Phase action",
                yataProg != null && !Has(yataProg, c => c.Action == EffectActionKind.OpponentSkipsNextBattlePhase),
                Describe(yataProg));

            // ForcedActivationAndSubstitution batch (v163): Major Riot now answers its own window (the "returned to your
            // hand by your opponent's effect" event); ForcedActivationAndSubstitutionRegressionTests drive it.
            var riot = CardTextEffectCompiler.Compile(db.Get(MajorRiot));
            Check("Major Riot compiles to its window-only Activate clause (ForcedActivationAndSubstitution, v163)",
                riot != null && riot.FullyCompiled && riot.ClauseList.Count == 1 &&
                riot.ClauseList[0].ActivationCondition == MajorRiotRuntime.YourMonstersReturnedToHandByOpponent &&
                riot.ClauseList[0].Action == EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown &&
                CardEffectStatus.Classify(db.Get(MajorRiot)) == CardEffectStatusKind.Implemented,
                Describe(riot));
        }

        // ──────────────── cross-group: HandDisruption (Gamble's skipped turn) ────────────────

        /// <summary>
        /// ygopro's skipped turn still runs reset_phase(PHASE_DRAW): it ends a pending Hino-Kagu-Tsuchi discard
        /// (EVENT_PREDRAW, RESET_PHASE+PHASE_DRAW) and every skip-Draw flag (Yata-Garasu / Fenrir / Time Seal) on
        /// that Draw Phase, as <see cref="HandDisruptionRuntime.ConsumeSkippedTurn"/> does.
        /// </summary>
        static void HandDisruptionSkippedTurnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // A pending Hino discard on the opponent; the opponent's next turn is skipped (Gamble). At the
                // player's own next Draw Phase nobody discards.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                PutInHand(engine, o, Celtic);
                PutInHand(engine, o, Celtic);
                o.DiscardHandAtNextDrawPhase = true;
                o.SkipNextTurn = true;
                var turn = engine.TurnNumber;
                EndTurn(engine); // the opponent's turn is skipped → the player's next turn
                Check("x HandDisruption: a skipped turn ends a pending Hino discard",
                    reached && engine.TurnPlayer == p && engine.TurnNumber == turn + 2 && o.HandCount == 2 &&
                    !o.DiscardHandAtNextDrawPhase,
                    $"turnP={engine.TurnPlayer == p} turn={turn}->{engine.TurnNumber} oppHand={o.HandCount} " +
                    $"pending={o.DiscardHandAtNextDrawPhase}");
            }

            {
                // Skip count 1 (Offerings) + a Yata/Fenrir skip flag on the same Draw Phase; the turn is skipped
                // instead: both end, and the opponent's following Draw Phase draws.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                o.SkipNextDrawPhaseCount = 1;
                o.SkipNextDrawPhase = true;
                o.SkipNextTurn = true;
                EndTurn(engine); // skipped → the player's turn
                var cleared = o.SkipNextDrawPhaseCount == 0 && !o.SkipNextDrawPhase;
                var deck = o.Deck.Count;
                EndTurn(engine); // the opponent's turn: it draws
                Check("x HandDisruption: a skipped turn ends both the skip count and the skip flag",
                    reached && cleared && engine.TurnPlayer == o && o.Deck.Count == deck - 1,
                    $"cleared={cleared} count={o.SkipNextDrawPhaseCount} flag={o.SkipNextDrawPhase} " +
                    $"deck={deck}->{o.Deck.Count} turnO={engine.TurnPlayer == o}");
            }
        }
    }
}
