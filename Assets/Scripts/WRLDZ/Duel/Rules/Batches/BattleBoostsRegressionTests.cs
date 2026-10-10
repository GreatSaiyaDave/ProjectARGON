using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// BattleBoosts batch (v161, LOD): Spear Dragon (old piercing wording; Defense Position at the end of the Damage Step
    /// after attacking), Dragon's Rage (your Dragons inflict piercing damage), Gray Wing (discard → attack twice this turn),
    /// The Hunter with 7 Weapons (declare a Type on Normal Summon; +1000 ATK at damage calculation against it), Injection
    /// Fairy Lily (pay 2000 LP at damage calculation → +3000 ATK) and Spirit Ryu (Battle Step Quick Effect: discard a Dragon
    /// → +1000 ATK/DEF until the end of the Battle Phase). Compile shapes, mini-duels through the real engine entry points
    /// (TryActivateSpellTrap, TryNormalSummon, TryAttack and the response windows) and fail-closed near misses.
    /// Runtime: <see cref="BattleBoostsRuntime"/>.
    /// </summary>
    public static class BattleBoostsRegressionTests
    {
        const int SpearDragon = 31553716;
        const int DragonsRage = 54178050;
        const int GrayWing = 29618570;
        const int Hunter = 1525329;
        const int Lily = 79575620;
        const int SpiritRyu = 67957315;

        const int Celtic = 91152256;          // Warrior EARTH 1400/1200
        const int OjamaGreen = 12482652;      // Beast Normal 0/1000
        const int LusterDragon = 11091375;    // Dragon Normal 1900/1600
        const int Mechanicalchaser = 7359741; // Machine Normal 1850/800

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            SpearDragonDuels(db, pDeck, aDeck, Check);
            DragonsRageDuels(db, pDeck, aDeck, Check);
            GrayWingDuels(db, pDeck, aDeck, Check);
            HunterDuels(db, pDeck, aDeck, Check);
            LilyDuels(db, pDeck, aDeck, Check);
            SpiritRyuDuels(db, pDeck, aDeck, Check);
        }

        static string Shape(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} {string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe))} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static string Status(CardDatabase db, int id) => CardEffectStatus.Classify(db.Get(id)).ToString();

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var spear = CardTextEffectCompiler.Compile(db.Get(SpearDragon));
            Check("BattleBoosts: Spear Dragon = piercing + Defense at the end of the Damage Step (attacker only, no lock)",
                spear != null && spear.FullyCompiled && spear.ClauseList.Count == 2 &&
                spear.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                spear.ClauseList[0].Action == EffectActionKind.ContinuousPiercing &&
                spear.ClauseList[1].Timing == EffectTiming.EndOfDamageStep &&
                spear.ClauseList[1].Action == EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep &&
                spear.ClauseList[1].DamageStepOnlyWhenAttacking && !spear.ClauseList[1].MakesChainLink &&
                Status(db, SpearDragon) == "Implemented",
                Shape(spear));

            var rage = CardTextEffectCompiler.Compile(db.Get(DragonsRage));
            Check("BattleBoosts: Dragon's Rage = continuous piercing grant to your Dragon monsters",
                rage != null && rage.FullyCompiled && rage.ClauseList.Count == 1 &&
                rage.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                rage.ClauseList[0].Action == EffectActionKind.GrantPiercingToYourRaceMonsters &&
                rage.ClauseList[0].RaceFilter == "Dragon" && Status(db, DragonsRage) == "Implemented",
                Shape(rage));

            var gray = CardTextEffectCompiler.Compile(db.Get(GrayWing));
            Check("BattleBoosts: Gray Wing = Main Phase 1 ignition, discard 1 card (cost), 1 extra attack this turn",
                gray != null && gray.FullyCompiled && gray.ClauseList.Count == 1 &&
                gray.ClauseList[0].Timing == EffectTiming.Activate &&
                gray.ClauseList[0].Action == EffectActionKind.GrantThisExtraAttackThisTurn &&
                gray.ClauseList[0].Amount == 1 && gray.ClauseList[0].RequiresDiscardCost &&
                gray.ClauseList[0].DiscardCostAttribute == "*" && gray.ClauseList[0].OnlyDuringMainPhase1 &&
                gray.ClauseList[0].ActivationCondition == BattleBoostsRuntime.CanEnterBattlePhaseWithoutExtraAttack &&
                !gray.ClauseList.Exists(c => c.Action == EffectActionKind.ExtraAttacks) &&
                Status(db, GrayWing) == "Implemented",
                Shape(gray));

            var hunter = CardTextEffectCompiler.Compile(db.Get(Hunter));
            Check("BattleBoosts: The Hunter with 7 Weapons = Normal Summon Type declaration + damage-calculation ATK vs it",
                hunter != null && hunter.FullyCompiled && hunter.ClauseList.Count == 2 &&
                hunter.ClauseList[0].Timing == EffectTiming.ThisCardSummoned &&
                hunter.ClauseList[0].Action == EffectActionKind.DeclareTypeForThisCardBattles &&
                hunter.ClauseList[0].Zone == EffectZoneFilter.DeclaredMonsterTypes &&
                hunter.ClauseList[0].RequiresThisNormalSummoned && hunter.ClauseList[0].ChoiceDoesNotTarget &&
                !hunter.ClauseList[0].IsOptional &&
                hunter.ClauseList[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                hunter.ClauseList[1].Action == EffectActionKind.GainAtkThisDamageCalculationVsDeclaredType &&
                hunter.ClauseList[1].Amount == 1000 &&
                !hunter.ClauseList.Exists(c => c.Action == EffectActionKind.DeclareMonsterType) &&
                Status(db, Hunter) == "Implemented",
                Shape(hunter));

            var lily = CardTextEffectCompiler.Compile(db.Get(Lily));
            Check("BattleBoosts: Injection Fairy Lily = monster Quick Effect, pay 2000 LP, +3000 ATK this damage calculation",
                lily != null && lily.FullyCompiled && lily.ClauseList.Count == 1 &&
                lily.ClauseList[0].Timing == EffectTiming.MonsterQuickEffect &&
                lily.ClauseList[0].Action == EffectActionKind.GainAtkThisDamageCalculation &&
                lily.ClauseList[0].PayLpAmount == 2000 && lily.ClauseList[0].Amount == 3000 &&
                lily.ClauseList[0].IsOptional && Status(db, Lily) == "Implemented",
                Shape(lily));

            var ryu = CardTextEffectCompiler.Compile(db.Get(SpiritRyu));
            Check("BattleBoosts: Spirit Ryu = monster Quick Effect, discard 1 Dragon, +1000 ATK/DEF until the end of the BP",
                ryu != null && ryu.FullyCompiled && ryu.ClauseList.Count == 1 &&
                ryu.ClauseList[0].Timing == EffectTiming.MonsterQuickEffect &&
                ryu.ClauseList[0].Action == EffectActionKind.GainAtkDefUntilEndOfBattlePhase &&
                ryu.ClauseList[0].RequiresDiscardCost && ryu.ClauseList[0].DiscardCostRace == "Dragon" &&
                ryu.ClauseList[0].Amount == 1000 && ryu.ClauseList[0].DefAmount == 1000 &&
                Status(db, SpiritRyu) == "Implemented",
                Shape(ryu));
        }

        // ── Near misses ──

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "BattleBoosts near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var batchActions = new[]
            {
                EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep, EffectActionKind.GrantPiercingToYourRaceMonsters,
                EffectActionKind.GrantThisExtraAttackThisTurn, EffectActionKind.DeclareTypeForThisCardBattles,
                EffectActionKind.GainAtkThisDamageCalculationVsDeclaredType, EffectActionKind.GainAtkThisDamageCalculation,
                EffectActionKind.GainAtkDefUntilEndOfBattlePhase
            };
            var cases = new (int like, string text)[]
            {
                // A longer Spear Dragon text (an extra rider) stays uncompiled.
                (SpearDragon, "If this card attacks, it is changed to Defense Position at the end of the Damage Step, and " +
                              "its battle position cannot be changed until the end of your next turn."),
                // Dragon's Rage limited to "face-up" in another way, or for your opponent's monsters.
                (DragonsRage, "Dragon-Type monsters your opponent controls inflict piercing Battle Damage when they attack a " +
                              "Defense Position monster."),
                // Gray Wing in Main Phase 2, or discarding 2 cards.
                (GrayWing, "Discard 1 card from your hand during your Main Phase 2. This monster can attack twice during " +
                           "the Battle Phase of this turn."),
                (GrayWing, "Discard 2 cards from your hand during your Main Phase 1. This monster can attack twice during " +
                           "the Battle Phase of this turn."),
                // The Hunter: Flip Summoned, or a declaration without the battle sentence.
                (Hunter, "When this card is Flip Summoned, declare 1 Type of monster. When battling that Type of monster, " +
                         "increase the ATK of this card by 1000 points during Damage Calculation."),
                (Hunter, "When this card is Normal Summoned, declare 1 Type of monster."),
                // Lily without "once per battle", or on your turn only.
                (Lily, "If this card attacks or is attacked, during damage calculation (in either player's turn): You can " +
                       "pay 2000 LP; this card gains 3000 ATK during that damage calculation only."),
                (Lily, "If this card attacks, during damage calculation: You can pay 2000 LP once per battle; this card " +
                       "gains 3000 ATK during that damage calculation only."),
                // Spirit Ryu during either player's Battle Step, or discarding any card.
                (SpiritRyu, "During either player's Battle Step, if this card battles (Quick Effect): You can discard 1 " +
                            "Dragon monster to the GY; this card gains 1000 ATK/DEF until the end of the Battle Phase."),
                (SpiritRyu, "During your Battle Step, if this card battles (Quick Effect): You can discard 1 card to the GY; " +
                            "this card gains 1000 ATK/DEF until the end of the Battle Phase."),
            };
            foreach (var (like, text) in cases)
            {
                var prog = CardTextEffectCompiler.Compile(Synthetic(db.Get(like), text));
                var full = prog != null && prog.FullyCompiled &&
                           prog.ClauseList.Exists(c => c != null && batchActions.Contains(c.Action));
                Check($"BattleBoosts near miss stays uncompiled: {text}", !full, Shape(prog));
            }

            // The modern piercing wording keeps its own template; Goblin Attack Force keeps its end-of-Battle-Phase lock.
            var goblin = CardTextEffectCompiler.Compile(db.Get(78658564));
            Check("BattleBoosts near miss: Goblin Attack Force is not the end-of-Damage-Step change",
                goblin != null && !goblin.ClauseList.Exists(c =>
                    c != null && c.Action == EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep),
                Shape(goblin));
        }

        // ── Helpers ──

        static bool ReachPlayerMain1(DuelEngine engine)
        {
            for (var t = 0; t < 10 && !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2 &&
                                         engine.Phase == DuelPhase.Main1); t++)
            {
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2 && engine.Phase == DuelPhase.Main1)
                    break;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            return engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1;
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            reached = ReachPlayerMain1(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            reached = ReachOpponentBattle(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static bool EnterBattle(DuelEngine engine, DuelistState who)
        {
            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            engine.TryEnterBattlePhase(who);
            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            return engine.Phase == DuelPhase.Battle;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        // ── Spear Dragon ──

        static void SpearDragonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var spear = PlaceMonster(engine, o, SpearDragon, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, p, OjamaGreen, 0, BattlePosition.Defense, true);
                var lp = p.LifePoints;
                var attacked = reached && Attack(engine, o, spear, ojama);
                Check("Spear Dragon: attacking a Defense Position monster inflicts piercing damage, then it changes to " +
                      "Defense Position at the end of the Damage Step (still in the Battle Phase)",
                    attacked && InGy(p, ojama) && p.LifePoints == lp - 900 && spear.FaceUp &&
                    spear.Position == BattlePosition.Defense && engine.Phase == DuelPhase.Battle,
                    $"reached={reached} attacked={attacked} lp={lp}->{p.LifePoints} pos={spear.Position} phase={engine.Phase}");
                var spear2 = PlaceMonster(engine, o, SpearDragon, 1, BattlePosition.Attack, true);
                var lp2 = p.LifePoints;
                var direct = Attack(engine, o, spear2, null);
                Check("Spear Dragon: after a direct attack it is changed to Defense Position too",
                    direct && p.LifePoints == lp2 - 1900 && spear2.Position == BattlePosition.Defense,
                    $"direct={direct} lp={lp2}->{p.LifePoints} pos={spear2.Position}");
                engine.TryEnterMainPhase2(o);
                Check("Spear Dragon: no position lock — its controller may change it back to Attack Position next turn",
                    !TextEffects.BattlePhaseEndRuntime.ManualPositionChangeLocked(engine, spear),
                    $"pos={spear.Position}");
            }

            {
                // Attacked: it stays in Attack Position.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var spear = PlaceMonster(engine, p, SpearDragon, 0, BattlePosition.Attack, true);
                var chaser = PlaceMonster(engine, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
                var attacked = reached && Attack(engine, o, chaser, spear);
                Check("Spear Dragon: when it is attacked (not attacking) it stays in Attack Position",
                    attacked && InGy(o, chaser) && spear.Position == BattlePosition.Attack,
                    $"reached={reached} attacked={attacked} pos={spear.Position}");
            }
        }

        // ── Dragon's Rage ──

        static void DragonsRageDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            var reachedMain = ReachOpponentMain(engine);
            ClearBoard(engine);
            var p = engine.Player;
            var o = engine.Opponent;
            p.Hand.Clear();
            var rage = PlaceSetTrap(engine, o, DragonsRage, 0);
            var activated = reachedMain && engine.TryActivateSpellTrap(o, rage, fromHand: false);
            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            var stays = rage.FaceUp && o.TryFindSpellTrap(rage, out _);
            var luster = PlaceMonster(engine, o, LusterDragon, 0, BattlePosition.Attack, true);
            var chaser = PlaceMonster(engine, o, Mechanicalchaser, 1, BattlePosition.Attack, true);
            var ojama1 = PlaceMonster(engine, p, OjamaGreen, 0, BattlePosition.Defense, true);
            var battle = EnterBattle(engine, o);
            var lp = p.LifePoints;
            var hit = battle && Attack(engine, o, luster, ojama1);
            Check("Dragon's Rage: activated, it stays face-up; your Dragon attacking a Defense Position monster inflicts " +
                  "piercing damage",
                reachedMain && activated && stays && hit && InGy(p, ojama1) && p.LifePoints == lp - 900,
                $"main={reachedMain} act={activated} stays={stays} battle={battle} hit={hit} lp={lp}->{p.LifePoints}");

            var ojama2 = PlaceMonster(engine, p, OjamaGreen, 1, BattlePosition.Defense, true);
            var lp2 = p.LifePoints;
            var hit2 = battle && Attack(engine, o, chaser, ojama2);
            Check("Dragon's Rage: a non-Dragon attacking a Defense Position monster inflicts no piercing damage",
                hit2 && InGy(p, ojama2) && p.LifePoints == lp2,
                $"hit={hit2} lp={lp2}->{p.LifePoints}");

            Check("Dragon's Rage: an opponent's Dragon gets no piercing from it",
                !BattleBoostsRuntime.FieldGrantsPiercing(engine,
                    PlaceMonster(engine, p, LusterDragon, 2, BattlePosition.Attack, true)));
        }

        // ── Gray Wing ──

        static void GrayWingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var gray = PlaceMonster(engine, p, GrayWing, 0, BattlePosition.Attack, true);
                var fodder = PutInHand(engine, p, Celtic);
                var can = reached && engine.CanActivateSpellTrap(p, gray, fromHand: false);
                var fired = can && engine.TryActivateSpellTrap(p, gray, fromHand: false);
                // The player confirms the discard (the ignition cost pick).
                var costPick = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(fodder) &&
                               engine.TrySelectEffectTarget(fodder);
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                engine.RecoverStuckCombat();
                var discarded = InGy(p, fodder) && !p.Hand.Contains(fodder);
                var extra = engine.ExtraAttacksAllowed(gray);
                PutInHand(engine, p, Celtic);
                var again = engine.CanActivateSpellTrap(p, gray, fromHand: false);
                Check("Gray Wing: in Main Phase 1, discarding 1 card (cost) gives it 1 extra attack this turn",
                    can && fired && costPick && discarded && extra == 1 && engine.MaxAttacksThisTurn(gray) == 2,
                    $"reached={reached} can={can} fired={fired} pick={costPick} discarded={discarded} extra={extra} " +
                    $"pending={engine.PendingActivation?.Prompt}");
                Check("Gray Wing: not again while it already has the extra attack", !again);

                var battle = EnterBattle(engine, p);
                var lp = o.LifePoints;
                var first = battle && Attack(engine, p, gray, null);
                var second = battle && Attack(engine, p, gray, null);
                var third = engine.CanAttack(p, gray);
                Check("Gray Wing: it attacks twice this Battle Phase (two direct attacks), not three times",
                    first && second && !third && o.LifePoints == lp - 2600,
                    $"battle={battle} first={first} second={second} third={third} lp={lp}->{o.LifePoints}");

                // The grant ends with the turn.
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                engine.TryEndTurnSafe(p);
                Check("Gray Wing: the extra attack ends with the turn", engine.ExtraAttacksAllowed(gray) == 0,
                    $"extra={engine.ExtraAttacksAllowed(gray)}");
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var gray = PlaceMonster(engine, p, GrayWing, 0, BattlePosition.Attack, true);
                var emptyHand = reached && !engine.CanActivateSpellTrap(p, gray, fromHand: false);
                PutInHand(engine, p, Celtic);
                engine.TryEnterMainPhase2(p);
                var main2 = !engine.CanActivateSpellTrap(p, gray, fromHand: false);
                Check("Gray Wing: no activation without a card to discard, nor in Main Phase 2",
                    reached && emptyHand && main2, $"reached={reached} empty={emptyHand} main2={main2}");
            }
        }

        // ── The Hunter with 7 Weapons ──

        static CardInstance TypeToken(DuelEngine engine, string type) =>
            engine.PendingActivation?.LegalTargets.FirstOrDefault(t =>
                TypeDeclarationRuntime.IsTypeToken(t) && t.Def.race == type);

        static void HunterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hunter = PutInHand(engine, p, Hunter);
                var summoned = reached && engine.TryNormalSummon(p, hunter, asSet: false);
                var token = TypeToken(engine, "Dragon");
                var picked = token != null && engine.TrySelectEffectTarget(token);
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                Check("The Hunter with 7 Weapons: its Normal Summon asks for a Type; Dragon is declared on this copy",
                    summoned && picked && BattleBoostsRuntime.DeclaredTypeOf(hunter) == "Dragon",
                    $"reached={reached} summoned={summoned} token={token != null} picked={picked} " +
                    $"type={BattleBoostsRuntime.DeclaredTypeOf(hunter)}");

                var luster = PlaceMonster(engine, o, LusterDragon, 0, BattlePosition.Attack, true);
                var battle = EnterBattle(engine, p);
                var lp = o.LifePoints;
                var hit = battle && Attack(engine, p, hunter, luster);
                Check("The Hunter with 7 Weapons: attacking a monster of the declared Type it has 2000 ATK at damage " +
                      "calculation only",
                    hit && InGy(o, luster) && o.LifePoints == lp - 100 && hunter.CurrentAtk == 1000 &&
                    p.TryFindMonster(hunter, out _),
                    $"battle={battle} hit={hit} lp={lp}->{o.LifePoints} atk={hunter.CurrentAtk}");
            }

            {
                // Attacked by a monster of the declared Type (opponent's turn), and a near miss against another Type.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hunter = PlaceMonster(engine, p, Hunter, 0, BattlePosition.Attack, true);
                // Declared through the clause arm (the summon itself is covered above).
                var prog = CardTextEffectCompiler.Compile(db.Get(Hunter));
                BattleBoostsRuntime.DeclareType(engine, p, hunter,
                    TypeDeclarationRuntime.MonsterTypeTokens(engine).First(t => t.Def.race == "Dragon"));
                var luster = PlaceMonster(engine, o, LusterDragon, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var hit = reached && Attack(engine, o, luster, hunter);
                Check("The Hunter with 7 Weapons: attacked by a monster of the declared Type it gains 1000 ATK too",
                    prog != null && hit && InGy(o, luster) && o.LifePoints == lp - 100 && p.TryFindMonster(hunter, out _),
                    $"reached={reached} hit={hit} lp={lp}->{o.LifePoints}");

                var chaser = PlaceMonster(engine, o, Mechanicalchaser, 1, BattlePosition.Attack, true);
                var plp = p.LifePoints;
                var hit2 = Attack(engine, o, chaser, hunter);
                Check("The Hunter with 7 Weapons: against another Type it has no gain (destroyed by 1850)",
                    hit2 && InGy(p, hunter) && p.LifePoints == plp - 850,
                    $"hit={hit2} lp={plp}->{p.LifePoints}");
            }

            {
                // The AI declares a Type its opponent's face-up monsters have; a Special Summon declares nothing.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, LusterDragon, 0, BattlePosition.Attack, true);
                var hunter = PutInHand(engine, o, Hunter);
                var summoned = reached && engine.TryNormalSummon(o, hunter, asSet: false);
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                Check("The Hunter with 7 Weapons: the AI declares the Type of the opponent's strongest face-up monsters",
                    summoned && !engine.IsAwaitingEffectTarget && BattleBoostsRuntime.DeclaredTypeOf(hunter) == "Dragon",
                    $"reached={reached} summoned={summoned} type={BattleBoostsRuntime.DeclaredTypeOf(hunter)}");
            }
        }

        // ── Injection Fairy Lily ──

        static void LilyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacking (the attacking player's damage calculation window).
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lily = PlaceMonster(engine, p, Lily, 0, BattlePosition.Attack, true);
                var luster = PlaceMonster(engine, o, LusterDragon, 0, BattlePosition.Attack, true);
                var notOpen = reached && !engine.CanActivateSpellTrap(p, lily, fromHand: false);
                var battle = EnterBattle(engine, p);
                var declared = battle && engine.TryAttack(p, lily, luster);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.DamageCalculation && pr.Responder == p &&
                             pr.LegalCards.Contains(lily);
                var plp = p.LifePoints;
                var olp = o.LifePoints;
                var fired = window && engine.TryActivateSpellTrap(p, lily, fromHand: false);
                DrainCombat(engine);
                Check("Injection Fairy Lily: attacking, its controller pays 2000 LP at damage calculation and it battles " +
                      "with 3400 ATK (that damage calculation only)",
                    notOpen && window && fired && p.LifePoints == plp - 2000 && InGy(o, luster) &&
                    o.LifePoints == olp - 1500 && lily.CurrentAtk == 400 && p.TryFindMonster(lily, out _),
                    $"reached={reached} notOpen={notOpen} declared={declared} window={window} fired={fired} " +
                    $"plp={plp}->{p.LifePoints} olp={olp}->{o.LifePoints} atk={lily.CurrentAtk}");
            }

            {
                // Attacked (the defender's window), and not with less than 2000 LP.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lily = PlaceMonster(engine, p, Lily, 0, BattlePosition.Attack, true);
                var luster = PlaceMonster(engine, o, LusterDragon, 0, BattlePosition.Attack, true);
                var declared = reached && engine.TryAttack(o, luster, lily);
                for (var k = 0; k < 3 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing != ResponseTiming.DamageCalculation; k++)
                    engine.PassResponse();
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.DamageCalculation && pr.Responder == p &&
                             pr.LegalCards.Contains(lily);
                var olp = o.LifePoints;
                var plp = p.LifePoints;
                var fired = window && engine.TryActivateSpellTrap(p, lily, fromHand: false);
                DrainCombat(engine);
                Check("Injection Fairy Lily: attacked on the opponent's turn, it gains 3000 ATK at damage calculation",
                    declared && window && fired && InGy(o, luster) && o.LifePoints == olp - 1500 &&
                    p.LifePoints == plp - 2000 && lily.CurrentAtk == 400,
                    $"reached={reached} declared={declared} window={window} fired={fired} olp={olp}->{o.LifePoints} " +
                    $"plp={p.LifePoints}");

                p.LifePoints = 1500;
                var luster2 = PlaceMonster(engine, o, LusterDragon, 1, BattlePosition.Attack, true);
                var declared2 = engine.TryAttack(o, luster2, lily);
                var offered = false;
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++)
                {
                    if (engine.PendingResponse.LegalCards.Contains(lily)) offered = true;
                    engine.PassResponse();
                }

                DrainCombat(engine);
                Check("Injection Fairy Lily: not offered with less than 2000 LP (the cost cannot be paid)",
                    declared2 && !offered && InGy(p, lily), $"declared={declared2} offered={offered}");
            }

            {
                // The AI uses it when the gain wins the battle.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lily = PlaceMonster(engine, o, Lily, 0, BattlePosition.Attack, true);
                var luster = PlaceMonster(engine, p, LusterDragon, 0, BattlePosition.Attack, true);
                var battle = EnterBattle(engine, p);
                var plp = p.LifePoints;
                var olp = o.LifePoints;
                var hit = battle && Attack(engine, p, luster, lily);
                Check("Injection Fairy Lily: the AI pays 2000 LP when attacked by a monster it then destroys",
                    hit && o.LifePoints == olp - 2000 && InGy(p, luster) && p.LifePoints == plp - 1500 &&
                    o.TryFindMonster(lily, out _),
                    $"battle={battle} hit={hit} olp={o.LifePoints} plp={plp}->{p.LifePoints}");
            }

            // Lily vs Lily (fixer, v161 review): the attacking player answers the defender's damage-calculation activation —
            // chained to it (Spell Speed 2), or once the turn player gets priority back after it resolved. 3400 vs 3400:
            // both are destroyed, no battle damage.
            foreach (var chain in new[] { true, false })
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var mine = PlaceMonster(engine, p, Lily, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(engine, o, Lily, 0, BattlePosition.Attack, true);
                var battle = EnterBattle(engine, p);
                var plp = p.LifePoints;
                var olp = o.LifePoints;
                var declared = battle && engine.TryAttack(p, mine, theirs);
                var pr = engine.PendingResponse;
                var ownWindow = pr != null && pr.Timing == ResponseTiming.DamageCalculation && pr.Responder == p &&
                                pr.LegalCards.Contains(mine);
                if (ownWindow) engine.PassResponse(); // the AI's Lily activates in the defender's window (400 vs 400)
                pr = engine.PendingResponse;
                var chainWindow = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == p;
                var offeredInChain = chainWindow && pr.LegalCards.Contains(mine);
                var answered = false;
                if (chain)
                    answered = offeredInChain && engine.TryActivateSpellTrap(p, mine, fromHand: false);
                else if (chainWindow)
                {
                    engine.PassResponse();
                    pr = engine.PendingResponse;
                    answered = pr != null && pr.Timing == ResponseTiming.DamageCalculation && pr.Responder == p &&
                               pr.LegalCards.Contains(mine) && engine.TryActivateSpellTrap(p, mine, fromHand: false);
                }

                DrainCombat(engine);
                Check($"Injection Fairy Lily: the attacking player answers the defender's Lily at damage calculation " +
                      $"({(chain ? "chained to it" : "after it resolved")}) — both 3400, both destroyed",
                    reached && declared && ownWindow && chainWindow && answered && InGy(p, mine) && InGy(o, theirs) &&
                    p.LifePoints == plp - 2000 && o.LifePoints == olp - 2000,
                    $"reached={reached} declared={declared} ownWindow={ownWindow} chainWindow={chainWindow} " +
                    $"offeredInChain={offeredInChain} answered={answered} mineGy={InGy(p, mine)} theirsGy={InGy(o, theirs)} " +
                    $"plp={plp}->{p.LifePoints} olp={olp}->{o.LifePoints}");
            }

            {
                // The AI counts the attacker's resolved gain (fixer, v161 review): the player's Lily (300) pays first and
                // battles with 3300; the AI's Lily (400) answers once that has resolved (3400 wins), never chained blind.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var mine = PlaceMonster(engine, p, Lily, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(engine, o, Lily, 0, BattlePosition.Attack, true);
                mine.UntilEndOfTurnAtk = -100;
                var battle = EnterBattle(engine, p);
                var plp = p.LifePoints;
                var olp = o.LifePoints;
                var declared = battle && engine.TryAttack(p, mine, theirs);
                var pr = engine.PendingResponse;
                var fired = pr != null && pr.Timing == ResponseTiming.DamageCalculation && pr.Responder == p &&
                            mine.CurrentAtk == 300 && engine.TryActivateSpellTrap(p, mine, fromHand: false);
                DrainCombat(engine);
                Check("Injection Fairy Lily: the AI defender counts the attacker's resolved +3000 (3300) and pays to win " +
                      "with 3400",
                    reached && declared && fired && InGy(p, mine) && o.TryFindMonster(theirs, out _) &&
                    p.LifePoints == plp - 2000 - 100 && o.LifePoints == olp - 2000,
                    $"reached={reached} declared={declared} fired={fired} mineGy={InGy(p, mine)} " +
                    $"plp={plp}->{p.LifePoints} olp={olp}->{o.LifePoints}");
            }
        }

        // ── Spirit Ryu ──

        static void SpiritRyuDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // One Dragon in hand: the cost is paid at once; the boost lasts until the end of the Battle Phase.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, SpiritRyu, 0, BattlePosition.Attack, true);
                var chaser = PlaceMonster(engine, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
                var dragon = PutInHand(engine, p, LusterDragon);
                var celtic = PutInHand(engine, p, Celtic);
                var notOpen = reached && !engine.CanActivateSpellTrap(p, ryu, fromHand: false);
                var battle = EnterBattle(engine, p);
                var declared = battle && engine.TryAttack(p, ryu, chaser);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.AttackDeclared && pr.Responder == p &&
                             pr.LegalCards.Count == 1 && pr.LegalCards.Contains(ryu) && engine.HasDeclaredAttack;
                var olp = o.LifePoints;
                var fired = window && engine.TryActivateSpellTrap(p, ryu, fromHand: false);
                var boosted = ryu.CurrentAtk == 2000 && ryu.CurrentDef == 2000;
                DrainCombat(engine);
                Check("Spirit Ryu: in its controller's Battle Step it discards a Dragon (cost) and battles with 2000 ATK",
                    notOpen && window && fired && boosted && InGy(p, dragon) && p.Hand.Contains(celtic) &&
                    InGy(o, chaser) && o.LifePoints == olp - 150 && ryu.CurrentAtk == 2000,
                    $"reached={reached} notOpen={notOpen} declared={declared} window={window} fired={fired} " +
                    $"boosted={boosted} olp={olp}->{o.LifePoints} atk={ryu.CurrentAtk}");
                engine.TryEnterMainPhase2(p);
                Check("Spirit Ryu: the gain ends with the Battle Phase",
                    ryu.CurrentAtk == 1000 && ryu.CurrentDef == 1000, $"{ryu.CurrentAtk}/{ryu.CurrentDef}");
            }

            {
                // Two Dragons: the player picks the discard; the window is offered again after it resolved.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, SpiritRyu, 0, BattlePosition.Attack, true);
                var chaser = PlaceMonster(engine, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
                var d1 = PutInHand(engine, p, LusterDragon);
                var d2 = PutInHand(engine, p, LusterDragon);
                var battle = EnterBattle(engine, p);
                var declared = battle && engine.TryAttack(p, ryu, chaser);
                var fired = engine.PendingResponse != null && engine.TryActivateSpellTrap(p, ryu, fromHand: false);
                var pick = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Count == 2;
                var cancelled = pick && engine.CancelEffectTargeting();
                var stillWindow = engine.PendingResponse != null && !engine.IsAwaitingEffectTarget &&
                                  p.Hand.Contains(d1) && p.Hand.Contains(d2);
                var fired2 = stillWindow && engine.TryActivateSpellTrap(p, ryu, fromHand: false);
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(d2);
                var again = engine.PendingResponse != null && engine.PendingResponse.Responder == p &&
                            engine.PendingResponse.LegalCards.Contains(ryu);
                engine.PassResponse();
                DrainCombat(engine);
                Check("Spirit Ryu: with 2 Dragons the player picks the discard (Cancel undoes it), and it is offered again",
                    declared && fired && pick && cancelled && stillWindow && fired2 && picked && InGy(p, d2) &&
                    p.Hand.Contains(d1) && again && InGy(o, chaser) && ryu.CurrentAtk == 2000,
                    $"declared={declared} fired={fired} pick={pick} cancelled={cancelled} still={stillWindow} " +
                    $"fired2={fired2} picked={picked} again={again} atk={ryu.CurrentAtk}");
            }

            {
                // No Dragon to discard: no window; the attack goes straight on.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, SpiritRyu, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                PutInHand(engine, p, Mechanicalchaser);
                var battle = EnterBattle(engine, p);
                var declared = battle && engine.TryAttack(p, ryu, celtic);
                var noWindow = engine.PendingResponse == null && !engine.HasDeclaredAttack;
                DrainCombat(engine);
                Check("Spirit Ryu: without a Dragon in hand no Battle Step window opens",
                    declared && noWindow && InGy(p, ryu), $"declared={declared} noWindow={noWindow}");
            }

            {
                // Attacked on the opponent's turn: not its controller's Battle Step.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, SpiritRyu, 0, BattlePosition.Attack, true);
                PutInHand(engine, p, LusterDragon);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var declared = reached && engine.TryAttack(o, celtic, ryu);
                var offered = false;
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++)
                {
                    if (engine.PendingResponse.LegalCards.Contains(ryu)) offered = true;
                    engine.PassResponse();
                }

                DrainCombat(engine);
                Check("Spirit Ryu: attacked on the opponent's turn it is not offered", declared && !offered && InGy(p, ryu),
                    $"declared={declared} offered={offered}");
            }

            {
                // The AI attacks with it and pays the cost when the gain wins the battle.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, o, SpiritRyu, 0, BattlePosition.Attack, true);
                var dragon = PutInHand(engine, o, LusterDragon);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var plp = p.LifePoints;
                var hit = reached && Attack(engine, o, ryu, celtic);
                Check("Spirit Ryu: the AI discards a Dragon when the gain wins the battle",
                    hit && InGy(o, dragon) && InGy(p, celtic) && p.LifePoints == plp - 600,
                    $"reached={reached} hit={hit} plp={plp}->{p.LifePoints}");
            }
        }
    }
}
