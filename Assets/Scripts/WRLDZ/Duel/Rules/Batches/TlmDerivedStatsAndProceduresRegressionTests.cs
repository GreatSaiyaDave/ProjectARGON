using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TlmDerivedStatsAndProcedures batch: Ultimate Insect LV7, Millennium Scorpion, Batteryman AA, Lost Guardian, King of
    /// the Skull Servants, Master Monk, Megarock Dragon (<see cref="TlmDerivedStatsAndProceduresAtoms"/>,
    /// <see cref="TlmDerivedStatsAndProceduresRuntime"/>, the LvChainAtoms aura sentence). Compile shapes through the real
    /// compiler, mini-duels through the engine entry points (the Standby LV progression, TryAttack in the AI's Battle Phase,
    /// TrySpecialSummonProcedure + TrySelectEffectTarget / CancelEffectTargeting, board refreshes) and look-alike sentences
    /// that must stay off this batch's clauses.
    /// </summary>
    public static class TlmDerivedStatsAndProceduresRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int UltimateInsectLv5 = 34830502;
        const int UltimateInsectLv7 = 19877898; // Insect Lv7 2600/1200
        const int MillenniumScorpion = 82482194; // Insect Lv5 2000/1800
        const int BatterymanAA = 63142001;      // Thunder Lv3 0/0
        const int LostGuardian = 45871897;      // Rock Lv4 100/?
        const int KingSkull = 36021814;         // Zombie Lv1 ?/0
        const int SkullServant = 32274490;      // Zombie Lv1 300/200
        const int MasterMonk = 49814180;        // Rock Lv5 1900/1000
        const int MonkFighter = 3810071;        // Rock Lv3 1300/1000
        const int MegarockDragon = 71544954;    // Rock Lv7 ?/?

        const int Celtic = 91152256;            // Warrior 1400/1200
        const int GiantSoldier = 13039848;      // Rock 1300/2000
        const int BlueEyes = 89631139;          // Dragon 3000/2500
        const int Banisher = 61528025;          // Banisher of the Light (Fairy 100/2000)

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TlmDerivedStatsAndProcedures: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            InsectLv7Duels(db, pDeck, aDeck, Check);
            ScorpionDuels(db, pDeck, aDeck, Check);
            BatterymanDuels(db, pDeck, aDeck, Check);
            LostGuardianDuels(db, pDeck, aDeck, Check);
            KingSkullDuels(db, pDeck, aDeck, Check);
            MasterMonkDuels(db, pDeck, aDeck, Check);
            MegarockDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Compile(CardDatabase db, int id) =>
            db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments)}]";

        static CompiledCardProgram CompileText(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990211, name = "Near-miss TLM monster", type = "Effect Monster", frameType = "effect",
                race = "Rock", attribute = "EARTH", level = 4, atk = 1000, def = 1000, desc = text
            });

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
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        static CardInstance ToGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance ToBanished(DuelEngine engine, DuelistState who, int id, bool faceUp)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = faceUp;
            who.Banished.Add(c);
            return c;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 10 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            foreach (var id in ids) who.Deck.Add(id);
        }

        static string Stats(CardInstance c) => c == null ? "null" : $"{c.CurrentAtk}/{c.CurrentDef}";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var lv7 = Compile(db, UltimateInsectLv7);
            Check("Ultimate Insect LV7: the LV5-gated aura, -700 ATK and -700 DEF on every opponent's monster",
                lv7 != null && lv7.FullyCompiled && lv7.ClauseList.Count == 1 &&
                lv7.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                lv7.ClauseList[0].Action == EffectActionKind.ContinuousGainAtkDef &&
                lv7.ClauseList[0].Side == EffectSide.Opponent && lv7.ClauseList[0].Amount == -700 &&
                lv7.ClauseList[0].DefAmount == -700 && lv7.ClauseList[0].RequiresThisSpecialSummonedByNamed &&
                lv7.ClauseList[0].SpecialSummonedByNamedCard == "Ultimate Insect LV5" &&
                CardEffectStatus.Classify(db.Get(UltimateInsectLv7)) == CardEffectStatusKind.Implemented,
                Describe(lv7));

            var lv5 = Compile(db, UltimateInsectLv5);
            var lv5Aura = lv5?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.ContinuousGainAtkDef);
            Check("Ultimate Insect LV5 keeps its ATK-only aura (no DEF loss)",
                lv5 != null && lv5.FullyCompiled && lv5Aura != null && lv5Aura.Amount == -500 && lv5Aura.DefAmount == 0,
                Describe(lv5));

            var sc = Compile(db, MillenniumScorpion);
            Check("Millennium Scorpion: a battle-destruction trigger, +500 ATK on itself",
                sc != null && sc.FullyCompiled && sc.ClauseList.Count == 1 &&
                sc.ClauseList[0].Timing == EffectTiming.ThisCardDestroysByBattle &&
                sc.ClauseList[0].Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && sc.ClauseList[0].Amount == 500,
                Describe(sc));

            var aa = Compile(db, BatterymanAA);
            Check("Batteryman AA: +1000 ATK per copy while all are in Attack, +1000 DEF per copy while all are in Defense",
                aa != null && aa.FullyCompiled && aa.ClauseList.Count == 2 &&
                aa.ClauseList.Exists(c => c.Action == EffectActionKind.GainSelfAtkPerCount && c.Amount == 1000 &&
                                          c.DefAmount == 0 && c.NamedCard == "Batteryman AA" &&
                                          c.CountSource == TlmDerivedStatsAndProceduresRuntime.FaceUpNamedYouControlAllInAttack) &&
                aa.ClauseList.Exists(c => c.Action == EffectActionKind.GainSelfAtkPerCount && c.Amount == 0 &&
                                          c.DefAmount == 1000 &&
                                          c.CountSource == TlmDerivedStatsAndProceduresRuntime.FaceUpNamedYouControlAllInDefense),
                Describe(aa));

            var lg = Compile(db, LostGuardian);
            Check("Lost Guardian: original DEF = face-up banished Rock monsters x 700 (ATK untouched)",
                lg != null && lg.FullyCompiled && lg.ClauseList.Count == 1 &&
                lg.ClauseList[0].Action == EffectActionKind.GainSelfAtkPerCount && lg.ClauseList[0].SelfStatIsSet &&
                lg.ClauseList[0].Amount == 0 && lg.ClauseList[0].DefAmount == 700 && lg.ClauseList[0].RaceFilter == "Rock" &&
                lg.ClauseList[0].CountSource == TlmDerivedStatsAndProceduresRuntime.YourFaceUpBanishedRace,
                Describe(lg));

            var king = Compile(db, KingSkull);
            var kingAtk = king?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.GainSelfAtkPerCount);
            var kingRevive = king?.ClauseList.FirstOrDefault(c =>
                c.Action == EffectActionKind.SpecialSummonThisFromGyByBanishingNamedFromGy);
            Check("King of the Skull Servants: original ATK from the GY count + the optional battle-destroyed revival",
                king != null && king.FullyCompiled && king.ClauseList.Count == 2 &&
                kingAtk != null && kingAtk.SelfStatIsSet && kingAtk.Amount == 1000 &&
                kingAtk.CountSource == TlmDerivedStatsAndProceduresRuntime.NamedOrAltInYourGy &&
                kingAtk.NamedCard == "King of the Skull Servants" && kingAtk.AltNamedCard == "Skull Servant" &&
                kingRevive != null && kingRevive.Timing == EffectTiming.SentFromFieldToGy &&
                kingRevive.RequiresThisDestroyedByBattle && kingRevive.IsOptional &&
                kingRevive.NamedCard == "King of the Skull Servants" && kingRevive.AltNamedCard == "Skull Servant",
                Describe(king));

            var monk = Compile(db, MasterMonk);
            Check("Master Monk: the named-Tribute hand procedure (any position) + attack twice",
                monk != null && monk.FullyCompiled && monk.ClauseList.Count == 2 &&
                monk.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                                            c.TributeNamedCard == "Monk Fighter" && !c.TributeFaceUpOnly &&
                                            string.IsNullOrEmpty(c.RequiresFaceUpName) && c.Amount == 0) &&
                monk.ClauseList.Exists(c => c.Action == EffectActionKind.ExtraAttacks && c.Amount == 1),
                Describe(monk));

            var mega = Compile(db, MegarockDragon);
            Check("Megarock Dragon: banish 1+ Rock monsters from the GY (procedure) + original ATK/DEF = that number x 700",
                mega != null && mega.FullyCompiled && mega.ClauseList.Count == 2 &&
                mega.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                                            c.BanishFromGyCount == 1 && c.BanishFromGyUpTo && c.RaceFilter == "Rock" &&
                                            c.Amount == 0) &&
                mega.ClauseList.Exists(c => c.Action == EffectActionKind.OriginalAtkDefBecomeProcedureBanishCountTimes &&
                                            c.Amount == 700 && c.DefAmount == 700),
                Describe(mega));
        }

        static bool FromThisBatch(EffectClause c) =>
            c != null &&
            (c.CountSource == TlmDerivedStatsAndProceduresRuntime.FaceUpNamedYouControlAllInAttack ||
             c.CountSource == TlmDerivedStatsAndProceduresRuntime.FaceUpNamedYouControlAllInDefense ||
             c.CountSource == TlmDerivedStatsAndProceduresRuntime.YourFaceUpBanishedRace ||
             c.CountSource == TlmDerivedStatsAndProceduresRuntime.NamedOrAltInYourGy ||
             c.Action == EffectActionKind.SpecialSummonThisFromGyByBanishingNamedFromGy ||
             c.Action == EffectActionKind.OriginalAtkDefBecomeProcedureBanishCountTimes ||
             (c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
              (c.BanishFromGyUpTo || (!string.IsNullOrEmpty(c.TributeNamedCard) && !c.TributeFaceUpOnly))) ||
             (c.RequiresThisSpecialSummonedByNamed && c.DefAmount != 0) ||
             (c.Timing == EffectTiming.ThisCardDestroysByBattle && c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp &&
              c.Amount > 0) ||
             (c.Action == EffectActionKind.ExtraAttacks &&
              (c.SourceSnippet ?? "").IndexOf("same turn", StringComparison.OrdinalIgnoreCase) >= 0));

        static void NearMisses(Chk Check)
        {
            var misses = new[]
            {
                // The LV aura with a DEF-only loss.
                "If this card was Special Summoned by the effect of \"Ultimate Insect LV5\", while it remains on the field " +
                "all your opponent's monsters lose 700 DEF.",
                // A battle growth with no "sends it to the Graveyard".
                "Each time this card destroys 1 monster on your opponent's side of the field by battle, this card gains " +
                "500 ATK.",
                // Attack Position paired with DEF.
                "If all \"Batteryman AA\"(s) you control are in Attack Position, this card gains 1000 DEF for each.",
                // Banished cards with no Type.
                "The original DEF of this card equals the number of your banished monsters × 700.",
                // The names counted on the field, not in the GY.
                "The original ATK of this card is the combined number of \"King of the Skull Servants\" and \"Skull " +
                "Servant\" you control x 1000.",
                // A mandatory revival.
                "When this card is destroyed by battle and sent to the GY: Banish 1 other \"King of the Skull Servants\" or " +
                "1 \"Skull Servant\" from your GY; Special Summon this card.",
                // The procedure without the Nomi sentence.
                "This card cannot be Special Summoned except by Tributing 1 \"Monk Fighter\".",
                // A conditional second attack.
                "While you control \"Monk Fighter\", this card can attack twice during the same turn.",
                // Megarock with an ATK-only set.
                "This card cannot be Normal Summoned or Set. This card cannot be Special Summoned except by removing from " +
                "play a Rock-Type monster(s) in your Graveyard. The original ATK of this card becomes the number of " +
                "Rock-Type monsters you removed from play when you Special Summoned this card x 700.",
                // Megarock with a different Type counted.
                "This card cannot be Normal Summoned or Set. This card cannot be Special Summoned except by removing from " +
                "play a Rock-Type monster(s) in your Graveyard. The original ATK and DEF of this card become the number of " +
                "Dragon-Type monsters you removed from play when you Special Summoned this card x 700.",
            };
            var leaked = misses.Where(t =>
            {
                var p = CompileText(t);
                return p != null && p.ClauseList.Exists(FromThisBatch);
            }).ToList();
            Check("Near misses: look-alike sentences never compile to this batch's clauses", leaked.Count == 0,
                string.Join(" || ", leaked));
        }

        // ───────────────────────────── Ultimate Insect LV7 ─────────────────────────────

        static void InsectLv7Duels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A settled LV5 sends itself to the GY during the player's Standby Phase and Special Summons LV7 from the hand;
            // that LV7's aura takes 700 ATK and DEF from each face-up monster the opponent controls, until it is turned
            // face-down (the summon record belongs to that field presence).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(o, GiantSoldier, GiantSoldier, GiantSoldier, GiantSoldier);
                var lv5 = PlaceMonster(e, p, UltimateInsectLv5, 0, BattlePosition.Attack, true);
                PutInHand(e, p, UltimateInsectLv7);
                e.TryEndTurnSafe(p);          // turn 3 → 4 (opponent)
                PassAll(e);
                e.TryEndTurnSafe(o);          // turn 4 → 5 (player Standby: LV5's trigger)
                PassAll(e);
                var lv7 = p.MonstersOnField().FirstOrDefault(m => m?.Def?.id == UltimateInsectLv7);
                var zone = e.FirstEmpty(o.MonsterZones);
                var celtic = zone >= 0 ? PlaceMonster(e, o, Celtic, zone, BattlePosition.Attack, true) : null;
                FieldSpellEffects.RefreshBoard(e);
                var auraOn = celtic != null && celtic.CurrentAtk == 700 && celtic.CurrentDef == 500;
                Check("Ultimate Insect LV7: Special Summoned by LV5's Standby effect, the opponent's monster loses 700 ATK/DEF",
                    reached && lv7 != null && p.Graveyard.Contains(lv5) && auraOn,
                    $"lv7={lv7 != null} lv5Gy={p.Graveyard.Contains(lv5)} celtic={Stats(celtic)}");

                if (lv7 != null)
                {
                    lv7.FaceUp = false;
                    FieldSpellEffects.RefreshBoard(e);
                    lv7.FaceUp = true;
                    FieldSpellEffects.RefreshBoard(e);
                }

                Check("Ultimate Insect LV7: turned face-down and back face-up, its aura is gone (Lua flag RESET_TURN_SET)",
                    lv7 != null && celtic != null && celtic.CurrentAtk == 1400 && celtic.CurrentDef == 1200,
                    $"celtic={Stats(celtic)}");
            }

            // A LV7 that reached the field any other way has no aura.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, UltimateInsectLv7, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                Check("Ultimate Insect LV7 not Summoned by LV5: no aura",
                    reached && celtic.CurrentAtk == 1400 && celtic.CurrentDef == 1200, $"celtic={Stats(celtic)}");
            }
        }

        // ───────────────────────────── Millennium Scorpion ─────────────────────────────

        static void ScorpionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var scorpion = PlaceMonster(e, o, MillenniumScorpion, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var giant = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var ok1 = battle && e.TryAttack(o, scorpion, celtic);
                DrainCombat(e);
                PassAll(e);
                var after1 = scorpion.CurrentAtk;
                FieldSpellEffects.RefreshBoard(e);
                var lasting = scorpion.CurrentAtk;
                Check("Millennium Scorpion: destroys Celtic Guardian by battle → 2500 ATK, kept after a board refresh",
                    reached && ok1 && p.Graveyard.Contains(celtic) && after1 == 2500 && lasting == 2500,
                    $"ok={ok1} celticGy={p.Graveyard.Contains(celtic)} atk={after1}/{lasting}");
                // Not a second attack this turn: the growth is per battle destruction; a later one stacks.
                scorpion.AttacksDeclaredThisTurn = 0;
                var ok2 = e.TryAttack(o, scorpion, giant);
                DrainCombat(e);
                PassAll(e);
                Check("Millennium Scorpion: a second battle destruction → 3000 ATK",
                    ok2 && p.Graveyard.Contains(giant) && scorpion.CurrentAtk == 3000,
                    $"ok={ok2} atk={scorpion.CurrentAtk}");
            }

            // The destroyed monster is banished instead (Banisher of the Light): no growth.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var scorpion = PlaceMonster(e, o, MillenniumScorpion, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Banisher, 1, BattlePosition.Defense, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var ok = battle && e.TryAttack(o, scorpion, celtic);
                DrainCombat(e);
                PassAll(e);
                Check("Millennium Scorpion: the destroyed monster banished (Banisher of the Light) → no ATK gain",
                    reached && ok && p.Banished.Contains(celtic) && scorpion.CurrentAtk == 2000,
                    $"ok={ok} banished={p.Banished.Contains(celtic)} gy={p.Graveyard.Contains(celtic)} atk={scorpion.CurrentAtk}");
            }
        }

        // ───────────────────────────── Batteryman AA ─────────────────────────────

        static void BatterymanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var a1 = PlaceMonster(e, p, BatterymanAA, 0, BattlePosition.Attack, true);
            var a2 = PlaceMonster(e, p, BatterymanAA, 1, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var bothAttack = $"{Stats(a1)} {Stats(a2)}";
            var okAttack = a1.CurrentAtk == 2000 && a2.CurrentAtk == 2000 && a1.CurrentDef == 0 && a2.CurrentDef == 0;

            var a3 = PlaceMonster(e, p, BatterymanAA, 2, BattlePosition.Defense, false);
            FieldSpellEffects.RefreshBoard(e);
            var okFaceDown = a1.CurrentAtk == 2000 && a2.CurrentAtk == 2000;
            var withSet = $"{Stats(a1)} {Stats(a2)}";

            a2.Position = BattlePosition.Defense;
            FieldSpellEffects.RefreshBoard(e);
            var okMixed = a1.CurrentAtk == 0 && a1.CurrentDef == 0 && a2.CurrentAtk == 0 && a2.CurrentDef == 0;
            var mixed = $"{Stats(a1)} {Stats(a2)}";

            a1.Position = BattlePosition.Defense;
            FieldSpellEffects.RefreshBoard(e);
            var okDefense = a1.CurrentAtk == 0 && a1.CurrentDef == 2000 && a2.CurrentDef == 2000 && a3.CurrentDef == 0;
            var bothDefense = $"{Stats(a1)} {Stats(a2)}";

            Check("Batteryman AA: 2 in Attack → 2000 ATK each; a face-down copy ignored; mixed → nothing; 2 in Defense → 2000 DEF",
                reached && okAttack && okFaceDown && okMixed && okDefense,
                $"attack={bothAttack} withSet={withSet} mixed={mixed} defense={bothDefense}");
        }

        // ───────────────────────────── Lost Guardian ─────────────────────────────

        static void LostGuardianDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Two face-up banished Rock monsters (a face-down one and a Warrior do not count) → DEF 1400; the AI's Celtic
            // Guardian (1400) attacks it in Defense Position and does not destroy it.
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var guardian = PlaceMonster(e, p, LostGuardian, 0, BattlePosition.Defense, true);
            ToBanished(e, p, GiantSoldier, true);
            ToBanished(e, p, MonkFighter, true);
            ToBanished(e, p, GiantSoldier, false);
            ToBanished(e, p, Celtic, true);
            ToBanished(e, o, GiantSoldier, true); // the opponent's banishment does not count
            FieldSpellEffects.RefreshBoard(e);
            var stats = Stats(guardian);
            var okStats = guardian.CurrentAtk == 100 && guardian.CurrentDef == 1400 &&
                          BattleMechanics.DefenseValue(guardian) == 1400;
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var battle = ReachOpponentBattle(e);
            o.Hand.Clear();
            var ok = battle && e.TryAttack(o, celtic, guardian);
            DrainCombat(e);
            PassAll(e);
            Check("Lost Guardian: original DEF = 2 face-up banished Rock monsters x 700, ATK 100; survives a 1400 attack",
                reached && okStats && ok && p.TryFindMonster(guardian, out _),
                $"stats={stats} ok={ok} onField={p.TryFindMonster(guardian, out _)}");
        }

        // ───────────────────────────── King of the Skull Servants ─────────────────────────────

        static void KingSkullDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var king = PlaceMonster(e, p, KingSkull, 0, BattlePosition.Attack, true);
                ToGy(e, p, SkullServant);
                ToGy(e, p, SkullServant);
                ToGy(e, p, KingSkull);
                ToGy(e, e.Opponent, SkullServant); // the opponent's GY does not count
                FieldSpellEffects.RefreshBoard(e);
                Check("King of the Skull Servants: 2 Skull Servant + 1 King in your GY → original ATK 3000",
                    reached && king.CurrentAtk == 3000 && BattleMechanics.AttackValue(king) == 3000, Stats(king));
            }

            // Destroyed by the AI's attack: the player banishes 1 Skull Servant and Special Summons it again.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var king = PlaceMonster(e, p, KingSkull, 0, BattlePosition.Attack, true);
                var s1 = ToGy(e, p, SkullServant);
                var s2 = ToGy(e, p, SkullServant);
                var bewd = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var ok = battle && e.TryAttack(o, bewd, king);
                DrainCombat(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(s1) &&
                            e.PendingActivation.LegalTargets.Contains(s2) && !e.PendingActivation.LegalTargets.Contains(king);
                var picked = asked && e.TrySelectEffectTarget(s1);
                // Lua POS_FACEUP: the player then chooses the position (and zone) for this card — face-up Defense here.
                var placeAsked = picked && e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1 &&
                                 e.PendingActivation.LegalTargets.Contains(king) &&
                                 SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation) &&
                                 p.Banished.Contains(s1);
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var placed = placeAsked && e.TrySelectEffectTarget(king);
                DrainCombat(e);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("King of the Skull Servants: destroyed by battle, banish 1 Skull Servant → Special Summoned in the " +
                      "chosen face-up Defense Position (ATK 1000)",
                    reached && ok && asked && picked && placeAsked && placed && p.TryFindMonster(king, out _) &&
                    king.FaceUp && king.Position == BattlePosition.Defense &&
                    p.Banished.Contains(s1) && p.Graveyard.Contains(s2) && king.CurrentAtk == 1000,
                    $"ok={ok} asked={asked} picked={picked} placeAsked={placeAsked} placed={placed} " +
                    $"field={p.TryFindMonster(king, out _)} pos={king.Position} s1Ban={p.Banished.Contains(s1)} " +
                    $"atk={king.CurrentAtk}");
            }

            // Cancel = the effect is not used.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var king = PlaceMonster(e, p, KingSkull, 0, BattlePosition.Attack, true);
                var s1 = ToGy(e, p, SkullServant);
                var bewd = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var ok = battle && e.TryAttack(o, bewd, king);
                DrainCombat(e);
                var asked = e.IsAwaitingEffectTarget;
                if (asked) e.CancelEffectTargeting();
                DrainCombat(e);
                Check("King of the Skull Servants: CANCEL → it stays in the GY, nothing banished",
                    reached && ok && asked && p.Graveyard.Contains(king) && p.Graveyard.Contains(s1) && p.Banished.Count == 0 &&
                    !e.IsAwaitingEffectTarget,
                    $"ok={ok} asked={asked} kingGy={p.Graveyard.Contains(king)} banished={p.Banished.Count}");
            }

            // No other King / Skull Servant in the GY: no prompt, it stays in the GY.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var king = PlaceMonster(e, p, KingSkull, 0, BattlePosition.Attack, true);
                ToGy(e, p, Celtic);
                var bewd = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var ok = battle && e.TryAttack(o, bewd, king);
                DrainCombat(e);
                Check("King of the Skull Servants: alone in the GY (no cost) → no prompt, stays in the GY",
                    reached && ok && !e.IsAwaitingEffectTarget && p.Graveyard.Contains(king),
                    $"ok={ok} awaiting={e.IsAwaitingEffectTarget}");
            }

            // The AI's copy, destroyed by the player's attack, uses the effect at once.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var king = PlaceMonster(e, o, KingSkull, 0, BattlePosition.Attack, true);
                var s1 = ToGy(e, o, SkullServant);
                ToGy(e, o, KingSkull);
                var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var ok = e.TryAttack(p, bewd, king);
                DrainCombat(e);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                var oneBanished = o.Banished.Count == 1 && (o.Banished[0] == s1 || o.Banished[0].Def.id == KingSkull);
                Check("King of the Skull Servants (AI): banishes 1 of its GY cards and comes back (ATK 1000, Attack)",
                    reached && ok && o.TryFindMonster(king, out _) && oneBanished && king.CurrentAtk == 1000 &&
                    king.FaceUp && king.Position == BattlePosition.Attack,
                    $"ok={ok} field={o.TryFindMonster(king, out _)} banished={o.Banished.Count} atk={king.CurrentAtk} " +
                    $"pos={king.Position}");
            }

            // The AI's copy whose only cost card leaves it at 0 ATK comes back in face-up Defense Position.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var king = PlaceMonster(e, o, KingSkull, 0, BattlePosition.Attack, true);
                var s1 = ToGy(e, o, SkullServant);
                var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var ok = e.TryAttack(p, bewd, king);
                DrainCombat(e);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("King of the Skull Servants (AI): 0 ATK on arrival → face-up Defense Position",
                    reached && ok && o.TryFindMonster(king, out _) && o.Banished.Contains(s1) && king.FaceUp &&
                    king.Position == BattlePosition.Defense && king.CurrentAtk == 0,
                    $"ok={ok} field={o.TryFindMonster(king, out _)} pos={king.Position} atk={king.CurrentAtk}");
            }
        }

        // ───────────────────────────── Master Monk ─────────────────────────────

        static void MasterMonkDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var fighter = PlaceMonster(e, p, MonkFighter, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var monk = PutInHand(e, p, MasterMonk);
                var noNormal = !e.CanNormalSummonOrSet(p, monk);
                var can = e.CanSpecialSummonProcedure(p, monk);
                var ok = e.TrySpecialSummonProcedure(p, monk);
                PassAll(e);
                Check("Master Monk: no Normal Summon; Special Summoned by Tributing a face-down Monk Fighter",
                    reached && noNormal && can && ok && p.TryFindMonster(monk, out _) && monk.FaceUp &&
                    p.Graveyard.Contains(fighter) && p.TryFindMonster(celtic, out _) && monk.ProcedureCompleted &&
                    e.ExtraAttacksAllowed(monk) == 1,
                    $"noNormal={noNormal} can={can} ok={ok} field={p.TryFindMonster(monk, out _)} " +
                    $"fighterGy={p.Graveyard.Contains(fighter)} extra={e.ExtraAttacksAllowed(monk)}");
            }

            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, MonkFighter, 0, BattlePosition.Attack, true);
                var monk = PutInHand(e, p, MasterMonk);
                var gyMonk = ToGy(e, p, MasterMonk);
                var revivable = SpecialSummonLegality.CanBeSpecialSummoned(e, p, gyMonk, null, SummonOrigin.Graveyard);
                gyMonk.ProcedureCompleted = true;
                var revivableAfter = SpecialSummonLegality.CanBeSpecialSummoned(e, p, gyMonk, null, SummonOrigin.Graveyard);
                // Lua EFFECT_SPSUMMON_CONDITION (no value) is checked whether or not the copy was properly summoned: a
                // Nomi never comes back (Monster Reborn), and SpecialSummonToField refuses it too.
                p.Graveyard.Remove(gyMonk);
                var placed = e.SpecialSummonToField(p, gyMonk, BattlePosition.Attack, true);
                if (!placed && !p.Graveyard.Contains(gyMonk)) p.Graveyard.Add(gyMonk);
                // Megarock Dragon (AddMustBeSpecialSummoned) stays a Semi-Nomi: a properly summoned copy may come back.
                var gyRock = ToGy(e, p, MegarockDragon);
                var rockBefore = SpecialSummonLegality.CanBeSpecialSummoned(e, p, gyRock, null, SummonOrigin.Graveyard);
                gyRock.ProcedureCompleted = true;
                var rockAfter = SpecialSummonLegality.CanBeSpecialSummoned(e, p, gyRock, null, SummonOrigin.Graveyard);
                Check("Master Monk: no own Monk Fighter → no procedure; a Nomi → never revived, even once properly summoned",
                    reached && !e.CanSpecialSummonProcedure(p, monk) && !e.TrySpecialSummonProcedure(p, monk) &&
                    p.Hand.Contains(monk) && !revivable && !revivableAfter && !placed && p.Graveyard.Contains(gyMonk) &&
                    !rockBefore && rockAfter,
                    $"revivable={revivable}/{revivableAfter} placed={placed} megarock={rockBefore}/{rockAfter}");
            }

            // The AI's Master Monk attacks directly twice in one Battle Phase, not a third time.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                var monk = PlaceMonster(e, o, MasterMonk, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var lp = e.Player.LifePoints;
                var a1 = battle && e.TryAttack(o, monk, null);
                DrainCombat(e);
                var a2 = e.TryAttack(o, monk, null);
                DrainCombat(e);
                var a3 = e.TryAttack(o, monk, null);
                DrainCombat(e);
                Check("Master Monk: attacks twice during the same Battle Phase",
                    reached && a1 && a2 && !a3 && e.Player.LifePoints == lp - 3800,
                    $"a1={a1} a2={a2} a3={a3} lp={lp}->{e.Player.LifePoints}");
            }
        }

        // ───────────────────────────── Megarock Dragon ─────────────────────────────

        static void MegarockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player picks 2 of 3 Rock monsters, then CANCEL finishes: original ATK/DEF 1400.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var g1 = ToGy(e, p, GiantSoldier);
                var g2 = ToGy(e, p, MonkFighter);
                var g3 = ToGy(e, p, GiantSoldier);
                var warrior = ToGy(e, p, Celtic);
                var mega = PutInHand(e, p, MegarockDragon);
                var noNormal = !e.CanNormalSummonOrSet(p, mega);
                var started = e.TrySpecialSummonProcedure(p, mega);
                var offered = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 3 &&
                              !e.PendingActivation.LegalTargets.Contains(warrior);
                var t1 = offered && e.TrySelectEffectTarget(g1);
                var t2 = t1 && e.TrySelectEffectTarget(g2);
                var stillOpen = e.IsAwaitingEffectTarget;
                if (stillOpen) e.CancelEffectTargeting();
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Megarock Dragon: 2 Rock monsters banished (tap, tap, CANCEL to finish) → original ATK/DEF 1400/1400",
                    reached && noNormal && started && offered && t1 && t2 && stillOpen && p.TryFindMonster(mega, out _) &&
                    p.Banished.Contains(g1) && p.Banished.Contains(g2) && p.Graveyard.Contains(g3) &&
                    p.Graveyard.Contains(warrior) && mega.CurrentAtk == 1400 && mega.CurrentDef == 1400 &&
                    BattleMechanics.AttackValue(mega) == 1400 && mega.ProcedureCompleted,
                    $"started={started} offered={offered} taps={t1}/{t2} open={stillOpen} field={p.TryFindMonster(mega, out _)} " +
                    $"stats={Stats(mega)}");

                // Turned face-down: the set original ATK/DEF are gone for good ("?" = 0).
                mega.FaceUp = false;
                FieldSpellEffects.RefreshBoard(e);
                mega.FaceUp = true;
                FieldSpellEffects.RefreshBoard(e);
                Check("Megarock Dragon: once turned face-down its ATK/DEF are 0 for good",
                    mega.CurrentAtk == 0 && mega.CurrentDef == 0 && BattleMechanics.AttackValue(mega) == 0 &&
                    TlmDerivedStatsAndProceduresRuntime.GrantOf(e, mega) == null,
                    Stats(mega));
            }

            // CANCEL before the first tap: no Summon, nothing banished.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, GiantSoldier);
                ToGy(e, p, GiantSoldier);
                var mega = PutInHand(e, p, MegarockDragon);
                var started = e.TrySpecialSummonProcedure(p, mega);
                var open = e.IsAwaitingEffectTarget;
                if (open) e.CancelEffectTargeting();
                Check("Megarock Dragon: CANCEL before the first tap → not Summoned, nothing banished",
                    reached && started && open && p.Hand.Contains(mega) && p.Banished.Count == 0 &&
                    p.Graveyard.Count == 2 && !e.IsAwaitingEffectTarget,
                    $"open={open} hand={p.Hand.Contains(mega)} banished={p.Banished.Count}");
            }

            // A lone Rock monster is banished without a prompt: 700/700. No Rock monster: no procedure.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = ToGy(e, p, Celtic);
                var mega = PutInHand(e, p, MegarockDragon);
                var noRock = !e.CanSpecialSummonProcedure(p, mega);
                var g1 = ToGy(e, p, GiantSoldier);
                var ok = e.TrySpecialSummonProcedure(p, mega);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Megarock Dragon: no Rock in the GY → no procedure; a lone Rock → banished at once, 700/700",
                    reached && noRock && ok && !e.IsAwaitingEffectTarget && p.TryFindMonster(mega, out _) &&
                    p.Banished.Contains(g1) && p.Graveyard.Contains(celtic) && mega.CurrentAtk == 700 &&
                    mega.CurrentDef == 700,
                    $"noRock={noRock} ok={ok} stats={Stats(mega)}");
            }

            // The AI banishes every Rock monster in its GY: 3 → 2100/2100.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                var main = ReachOpponentMain(e);
                o.Hand.Clear();
                o.Graveyard.Clear();
                ToGy(e, o, GiantSoldier);
                ToGy(e, o, GiantSoldier);
                ToGy(e, o, MonkFighter);
                var mega = PutInHand(e, o, MegarockDragon);
                var ok = main && e.TrySpecialSummonProcedure(o, mega);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Megarock Dragon (AI): banishes its 3 Rock monsters → 2100/2100",
                    reached && ok && o.TryFindMonster(mega, out _) && o.Banished.Count(b => b.Def.race == "Rock") == 3 &&
                    mega.CurrentAtk == 2100 && mega.CurrentDef == 2100,
                    $"main={main} ok={ok} stats={Stats(mega)} banished={o.Banished.Count}");
            }
        }
    }
}
