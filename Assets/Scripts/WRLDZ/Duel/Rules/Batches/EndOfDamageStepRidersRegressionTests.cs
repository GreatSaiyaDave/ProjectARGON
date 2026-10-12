using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// EndOfDamageStepRiders batch: Getsu Fuhma, Ryu Kokki, Prickle Fairy, Insect Princess, Enraged Battle Ox, Drillago,
    /// Two Thousand Needles, Legendary Jujitsu Master. Compile shapes through the real compiler, battles through the engine
    /// entry points (TryAttack, response windows, board refresh), and look-alike sentences that must stay uncompiled.
    /// Turn 3 is the Player's, turn 4 the AI's. Runtime: <see cref="EndOfDamageStepRidersRuntime"/>.
    /// </summary>
    public static class EndOfDamageStepRidersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GetsuFuhma = 21887179;          // Warrior Lv4 1700/1200
        const int RyuKokki = 57281778;            // Zombie Lv6 2400/2000
        const int PrickleFairy = 91559748;        // Plant Lv4 300/2000
        const int InsectPrincess = 37957847;      // Insect Lv6 1900/1200
        const int EnragedBattleOx = 76909279;     // Beast-Warrior Lv4 1700/1000
        const int Drillago = 99050989;            // Machine Lv4 1600/1100
        const int TwoThousandNeedles = 83228073;  // Beast Lv5 2000/1800
        const int JujitsuMaster = 25773409;       // Rock Lv3 1300/1800

        const int SummonedSkull = 70781052;       // Fiend Normal 2500/1200
        const int ThirteenthGrave = 32864;        // Zombie Normal 1200/900
        const int AxeRaider = 48305365;           // Warrior Normal 1700/1150
        const int GeminiElf = 69140098;           // Spellcaster Normal 1900/900
        const int CelticGuardian = 91152256;      // Warrior Normal 1400/1200
        const int BasicInsect = 89091579;         // Insect Normal 500/700
        const int NeedleWorm = 81843628;          // Insect FLIP 750/600: mill the opponent's top 5
        const int SkullRedBird = 10202894;        // Winged Beast Normal 1550/1200
        const int TrapHole = 4206964;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("EndOfDamageStepRiders: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            GetsuFuhmaDuels(db, pDeck, aDeck, Check);
            RyuKokkiDuels(db, pDeck, aDeck, Check);
            PrickleFairyDuels(db, pDeck, aDeck, Check);
            InsectPrincessDuels(db, pDeck, aDeck, Check);
            EnragedBattleOxDuels(db, pDeck, aDeck, Check);
            DrillagoDuels(db, pDeck, aDeck, Check);
            TwoThousandNeedlesDuels(db, pDeck, aDeck, Check);
            JujitsuMasterDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards and hands.</summary>
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
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
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

        /// <summary>The AI's Battle Phase (turn 4): <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void Settle(DuelEngine engine)
        {
            engine.NotifyPublic();
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);
        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} race={c.RaceFilter} " +
                    $"battled={c.BattledMonsterRaceFilter} chain={c.MakesChainLink}")) + "]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Is(int id, CardEffectStatusKind k) => CardEffectStatus.Classify(db.Get(id)) == k;

            bool One(CompiledCardProgram p, EffectTiming t, EffectActionKind a) =>
                p != null && p.FullyCompiled && p.ClauseList.Count == 1 && p.ClauseList[0].Timing == t &&
                p.ClauseList[0].Action == a;

            var getsu = P(GetsuFuhma);
            Check("Getsu Fuhma: end-of-Damage-Step destroy of a battled Fiend or Zombie",
                One(getsu, EffectTiming.EndOfDamageStep, EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep) &&
                getsu.ClauseList[0].BattledMonsterRaceFilter == "Fiend|Zombie" &&
                getsu.ClauseList[0].Zone == EffectZoneFilter.OpponentBattlingMonster && getsu.ClauseList[0].MakesChainLink &&
                Is(GetsuFuhma, CardEffectStatusKind.Implemented), Describe(getsu));

            var ryu = P(RyuKokki);
            Check("Ryu Kokki: end-of-Damage-Step destroy of a battled Warrior or Spellcaster",
                One(ryu, EffectTiming.EndOfDamageStep, EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep) &&
                ryu.ClauseList[0].BattledMonsterRaceFilter == "Warrior|Spellcaster" &&
                Is(RyuKokki, CardEffectStatusKind.Implemented), Describe(ryu));

            var prickle = P(PrickleFairy);
            Check("Prickle Fairy: Insect attack shield + end-of-Damage-Step change to Defense",
                prickle != null && prickle.FullyCompiled && prickle.ClauseList.Count == 2 &&
                prickle.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                               c.Action == EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters &&
                                               c.RaceFilter == "Insect") &&
                prickle.ClauseList.Exists(c => c.Timing == EffectTiming.EndOfDamageStep &&
                                               c.Action == EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep) &&
                Is(PrickleFairy, CardEffectStatusKind.Implemented), Describe(prickle));

            var princess = P(InsectPrincess);
            Check("Insect Princess: opponent's Insects in Attack Position + 500 ATK for destroying an Insect",
                princess != null && princess.FullyCompiled && princess.ClauseList.Count == 2 &&
                princess.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                                c.Action == EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack &&
                                                c.RaceFilter == "Insect" && c.Side == EffectSide.Opponent) &&
                princess.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardDestroysByBattle &&
                                                c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp &&
                                                c.Amount == 500 && c.BattledMonsterRaceFilter == "Insect") &&
                Is(InsectPrincess, CardEffectStatusKind.Implemented), Describe(princess));

            var ox = P(EnragedBattleOx);
            Check("Enraged Battle Ox: piercing for your Beast / Beast-Warrior / Winged Beast monsters",
                One(ox, EffectTiming.ContinuousWhileFaceUp, EffectActionKind.GrantPiercingToYourRaceMonsters) &&
                ox.ClauseList[0].RaceFilter == "Beast|Beast-Warrior|Winged Beast" &&
                Is(EnragedBattleOx, CardEffectStatusKind.Implemented), Describe(ox));

            var drill = P(Drillago);
            Check("Drillago: direct attack while the opponent controls only face-up 1600+ ATK monsters",
                One(drill, EffectTiming.ContinuousWhileFaceUp,
                    EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast) &&
                drill.ClauseList[0].Amount == 1600 && Is(Drillago, CardEffectStatusKind.Implemented), Describe(drill));

            var needles = P(TwoThousandNeedles);
            Check("Two Thousand Needles: Thousand Needles' end-of-Damage-Step attacker destruction",
                One(needles, EffectTiming.EndOfDamageStep, EffectActionKind.DestroyAttackerAtEndOfDamageStep) &&
                needles.ClauseList[0].Zone == EffectZoneFilter.AttackingMonster && needles.ClauseList[0].MakesChainLink &&
                Is(TwoThousandNeedles, CardEffectStatusKind.Implemented), Describe(needles));

            var juji = P(JujitsuMaster);
            Check("Legendary Jujitsu Master: end-of-Damage-Step attacker to the top of the Deck",
                One(juji, EffectTiming.EndOfDamageStep, EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep) &&
                juji.ClauseList[0].Zone == EffectZoneFilter.AttackingMonster &&
                Is(JujitsuMaster, CardEffectStatusKind.Implemented), Describe(juji));
        }

        static void NearMisses(Chk Check)
        {
            var near = new (string Desc, EffectActionKind Action)[]
            {
                ("If this card battles a Fiend or Zombie-Type monster, destroy that monster at the end of the Battle Phase.",
                    EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep),
                ("If this card battles a Fiend-Type monster, destroy that monster and this card at the end of the Damage Step.",
                    EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep),
                ("At the end of the Damage Step, if this card battled a Warrior-Type monster: Destroy that monster, then " +
                 "inflict 500 damage to your opponent.",
                    EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep),
                ("Your opponent cannot attack Insect monsters, except this card.",
                    EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters),
                ("If this card battled a monster, at the end of the Damage Step: Change that monster to Defense Position, " +
                 "and if you do, it loses 500 ATK.",
                    EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep),
                ("All Insect monsters your opponent controls are changed to Defense Position.",
                    EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack),
                ("If this card destroys an Insect monster by battle: This card gains 500 ATK until the end of this turn.",
                    EffectActionKind.ChangeThisCardAtkWhileFaceUp),
                ("If a Beast-Type monster you control attacks a Defense Position monster, inflict double piercing battle " +
                 "damage to your opponent.",
                    EffectActionKind.GrantPiercingToYourRaceMonsters),
                ("If the only cards your opponent controls are face-up monsters with 1600 or more ATK, this card can attack " +
                 "your opponent directly, but the battle damage it inflicts is halved.",
                    EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast),
                ("When this Defense Position card is attacked and the ATK of the attacking monster is lower than the DEF of " +
                 "this card, destroy the attacking monster at the end of the Battle Phase.",
                    EffectActionKind.DestroyAttackerAtEndOfDamageStep),
                ("If this Defense Position card battled a monster, at the end of the Damage Step: Shuffle that monster into " +
                 "the Deck.",
                    EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90917700 + i, name = "Synthetic EndOfDamageStepRiders near miss " + i, type = "Effect Monster",
                    race = "Warrior", desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => c.Action == n.Action));
            }).Select((n, i) => i + ":" + n.Action).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(",", leaked));
        }

        // ───────────────────────────── Getsu Fuhma ─────────────────────────────

        static void GetsuFuhmaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var getsu = PlaceMonster(engine, p, GetsuFuhma, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, getsu, skull);
                Check("Getsu Fuhma: destroyed by battle with a Fiend, it still destroys that Fiend from the GY",
                    reached && ok && InGy(p, getsu) && InGy(o, skull),
                    $"ok={ok} getsuGy={InGy(p, getsu)} skullGy={InGy(o, skull)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var getsu = PlaceMonster(engine, p, GetsuFuhma, 0, BattlePosition.Defense, true);
                var grave = PlaceMonster(engine, o, ThirteenthGrave, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, grave, getsu);
                Check("Getsu Fuhma: attacked by a Zombie (no battle destruction), that Zombie is destroyed",
                    reached && ok && OnField(p, getsu) && InGy(o, grave),
                    $"ok={ok} getsu={OnField(p, getsu)} graveGy={InGy(o, grave)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var getsu = PlaceMonster(engine, p, GetsuFuhma, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, getsu, elf);
                Check("Getsu Fuhma: a Spellcaster it battled is not destroyed",
                    reached && ok && InGy(p, getsu) && OnField(o, elf),
                    $"ok={ok} getsuGy={InGy(p, getsu)} elf={OnField(o, elf)}");
            }
        }

        // ───────────────────────────── Ryu Kokki ─────────────────────────────

        static void RyuKokkiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, RyuKokki, 0, BattlePosition.Defense, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, elf, ryu);
                Check("Ryu Kokki: the Spellcaster that attacked it is destroyed at the end of the Damage Step",
                    reached && ok && OnField(p, ryu) && InGy(o, elf),
                    $"ok={ok} ryu={OnField(p, ryu)} elfGy={InGy(o, elf)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, RyuKokki, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, skull, ryu);
                Check("Ryu Kokki: a Fiend that destroys it by battle stays on the field",
                    reached && ok && InGy(p, ryu) && OnField(o, skull),
                    $"ok={ok} ryuGy={InGy(p, ryu)} skull={OnField(o, skull)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ryu = PlaceMonster(engine, p, RyuKokki, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Defense, true);
                elf.LingeringDefModifier = 2000; // DEF 2900: not destroyed by battle
                var ok = AttackAndDrain(engine, p, ryu, elf);
                Check("Ryu Kokki: attacking, the Spellcaster it battled is destroyed",
                    reached && ok && OnField(p, ryu) && InGy(o, elf),
                    $"ok={ok} ryu={OnField(p, ryu)} elfGy={InGy(o, elf)} def={elf.CurrentDef}");
            }
        }

        // ───────────────────────────── Prickle Fairy ─────────────────────────────

        static void PrickleFairyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var prickle = PlaceMonster(engine, p, PrickleFairy, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, prickle);
                Check("Prickle Fairy: the attacker is changed to face-up Defense Position at the end of the Damage Step",
                    reached && ok && OnField(p, prickle) && OnField(o, axe) && axe.FaceUp &&
                    axe.Position == BattlePosition.Defense,
                    $"ok={ok} prickle={OnField(p, prickle)} axe={OnField(o, axe)} pos={axe.Position}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var prickle = PlaceMonster(engine, p, PrickleFairy, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, skull, prickle);
                Check("Prickle Fairy: destroyed by battle, it still changes the attacker to Defense Position",
                    reached && ok && InGy(p, prickle) && OnField(o, skull) && skull.Position == BattlePosition.Defense,
                    $"ok={ok} prickleGy={InGy(p, prickle)} skull={OnField(o, skull)} pos={skull.Position}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, PrickleFairy, 0, BattlePosition.Defense, true);
                var insect = PlaceMonster(engine, p, BasicInsect, 1, BattlePosition.Attack, true);
                var setInsect = PlaceMonster(engine, p, BasicInsect, 2, BattlePosition.Defense, false);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var reachedBattle = ReachOpponentBattle(engine);
                var blocked = !engine.TryAttack(o, axe, insect);
                var direct = engine.CanAttackDirectly(o, axe);
                var setOk = engine.TryAttack(o, axe, setInsect);
                DrainCombat(engine);
                Check("Prickle Fairy: the opponent cannot attack a face-up Insect (no direct attack either); a Set one can be",
                    reached && reachedBattle && blocked && !direct && setOk && OnField(p, insect),
                    $"battle={reachedBattle} blocked={blocked} direct={direct} setOk={setOk}");
            }
        }

        // ───────────────────────────── Insect Princess ─────────────────────────────

        static void InsectPrincessDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var insect = PlaceMonster(engine, o, BasicInsect, 0, BattlePosition.Defense, true);
                var worm = PlaceMonster(engine, o, NeedleWorm, 1, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 2, BattlePosition.Defense, true);
                var deck0 = p.Deck.Count;
                Settle(engine);
                var before = insect.Position == BattlePosition.Defense && !worm.FaceUp;
                PlaceMonster(engine, p, InsectPrincess, 0, BattlePosition.Attack, true);
                Settle(engine);
                Check("Insect Princess: the opponent's Insects are changed to face-up Attack Position (a Set FLIP Insect " +
                      "is flipped and its FLIP effect activates); other monsters are not",
                    reached && before && insect.FaceUp && insect.Position == BattlePosition.Attack && worm.FaceUp &&
                    worm.Position == BattlePosition.Attack && celtic.Position == BattlePosition.Defense &&
                    p.Deck.Count == deck0 - 5,
                    $"insect={insect.Position} worm={worm.FaceUp}/{worm.Position} celtic={celtic.Position} " +
                    $"deck={deck0}->{p.Deck.Count}");

                var main = ReachOpponentMain(engine);
                Check("Insect Princess: the opponent cannot change an Insect's battle position (others can)",
                    main && !engine.CanChangePosition(o, insect) && engine.CanChangePosition(o, celtic),
                    $"main={main} insect={engine.CanChangePosition(o, insect)} celtic={engine.CanChangePosition(o, celtic)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var princess = PlaceMonster(engine, p, InsectPrincess, 0, BattlePosition.Attack, true);
                var insect = PlaceMonster(engine, o, BasicInsect, 0, BattlePosition.Defense, true);
                Settle(engine);
                var ok = AttackAndDrain(engine, p, princess, insect);
                Check("Insect Princess: destroying an Insect by battle gives it 500 ATK",
                    reached && ok && InGy(o, insect) && OnField(p, princess) && princess.CurrentAtk == 2400,
                    $"ok={ok} insectGy={InGy(o, insect)} atk={princess.CurrentAtk}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var princess = PlaceMonster(engine, p, InsectPrincess, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, princess, celtic);
                Check("Insect Princess: destroying a Warrior by battle gives no ATK",
                    reached && ok && InGy(o, celtic) && princess.CurrentAtk == 1900,
                    $"ok={ok} celticGy={InGy(o, celtic)} atk={princess.CurrentAtk}");
            }
        }

        // ───────────────────────────── Enraged Battle Ox ─────────────────────────────

        static void EnragedBattleOxDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, name, pierce) in new[]
                     {
                         (EnragedBattleOx, "Enraged Battle Ox itself (Beast-Warrior)", 1000),
                         (SkullRedBird, "a Winged Beast", 850),
                         (AxeRaider, "a Warrior (not one of its Types)", 0),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                if (id != EnragedBattleOx) PlaceMonster(engine, p, EnragedBattleOx, 1, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, p, id, 0, BattlePosition.Attack, true);
                var insect = PlaceMonster(engine, o, BasicInsect, 0, BattlePosition.Defense, true);
                var lp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, attacker, insect);
                Check($"Enraged Battle Ox: {name} attacking a Defense Position monster inflicts {pierce} piercing damage",
                    reached && ok && InGy(o, insect) && o.LifePoints == lp - pierce,
                    $"ok={ok} lp={lp}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── Drillago ─────────────────────────────

        static void DrillagoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var drill = PlaceMonster(engine, p, Drillago, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var can = engine.CanAttackDirectly(p, drill);
                var ok = AttackAndDrain(engine, p, drill, null);
                Check("Drillago: the opponent controls only a face-up 1900 ATK monster — it attacks directly",
                    reached && can && ok && o.LifePoints == lp - 1600 && OnField(o, elf),
                    $"can={can} ok={ok} lp={lp}->{o.LifePoints}");
            }

            foreach (var (label, setup) in new (string, Action<DuelEngine>)[]
                     {
                         ("a Set Spell/Trap", e => PlaceSetTrap(e, e.Opponent, TrapHole, 0)),
                         ("a 1400 ATK monster", e => PlaceMonster(e, e.Opponent, CelticGuardian, 1, BattlePosition.Attack, true)),
                         ("a face-down monster", e => PlaceMonster(e, e.Opponent, GeminiElf, 1, BattlePosition.Defense, false)),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var drill = PlaceMonster(engine, p, Drillago, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                setup(engine);
                Settle(engine);
                engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var inBattle = engine.Phase == DuelPhase.Battle && engine.CanAttack(p, drill);
                var grant = engine.GrantsDirectAttack(drill);
                var can = engine.CanAttackDirectly(p, drill);
                var lp = o.LifePoints;
                var ok = engine.TryAttack(p, drill, null);
                DrainCombat(engine);
                Check($"Drillago: no direct attack while the opponent also controls {label}",
                    reached && inBattle && !grant && !can && !ok && o.LifePoints == lp,
                    $"inBattle={inBattle} grant={grant} can={can} ok={ok} lp={lp}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── Two Thousand Needles ─────────────────────────────

        static void TwoThousandNeedlesDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var needles = PlaceMonster(engine, p, TwoThousandNeedles, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = OpponentAttacks(engine, axe, needles);
                Check("Two Thousand Needles: a 1700 ATK attacker is destroyed at the end of the Damage Step",
                    reached && ok && InGy(o, axe) && OnField(p, needles) && o.LifePoints == oLp - 100,
                    $"ok={ok} axeGy={InGy(o, axe)} needles={OnField(p, needles)} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var needles = PlaceMonster(engine, p, TwoThousandNeedles, 0, BattlePosition.Defense, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, elf, needles);
                Check("Two Thousand Needles: a 1900 ATK attacker destroys it and is not destroyed",
                    reached && ok && InGy(p, needles) && OnField(o, elf),
                    $"ok={ok} needlesGy={InGy(p, needles)} elf={OnField(o, elf)}");
            }
        }

        // ───────────────────────────── Legendary Jujitsu Master ─────────────────────────────

        static void JujitsuMasterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var juji = PlaceMonster(engine, p, JujitsuMaster, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, juji);
                Check("Legendary Jujitsu Master: the attacker is placed on top of its owner's Deck",
                    reached && ok && OnField(p, juji) && !OnField(o, axe) && !InGy(o, axe) && o.Deck.Count > 0 &&
                    o.Deck[0] == AxeRaider,
                    $"ok={ok} juji={OnField(p, juji)} axe={OnField(o, axe)} top={(o.Deck.Count > 0 ? o.Deck[0] : 0)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var juji = PlaceMonster(engine, p, JujitsuMaster, 0, BattlePosition.Defense, false);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, skull, juji);
                Check("Legendary Jujitsu Master (Set): destroyed by battle, it still puts the attacker on top of the Deck",
                    reached && ok && InGy(p, juji) && !OnField(o, skull) && o.Deck.Count > 0 && o.Deck[0] == SummonedSkull,
                    $"ok={ok} jujiGy={InGy(p, juji)} skull={OnField(o, skull)} top={(o.Deck.Count > 0 ? o.Deck[0] : 0)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var juji = PlaceMonster(engine, p, JujitsuMaster, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, juji);
                Check("Legendary Jujitsu Master: attacked in Attack Position, the attacker stays",
                    reached && ok && InGy(p, juji) && OnField(o, axe),
                    $"ok={ok} jujiGy={InGy(p, juji)} axe={OnField(o, axe)}");
            }
        }
    }
}
