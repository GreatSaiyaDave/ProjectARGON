using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CrvBattleStepRiders batch: Goblin Elite Attack Force, Indomitable Fighter Lei Lei, Steamroid, Etoile Cyber, Drillroid,
    /// Nanobreaker, Chthonian Soldier and Oxygeddon. Compile shapes, near misses, and mini-duels through TryAttack (both
    /// players' Battle Phases), Stop Defense (TryActivateSpellTrap + TrySelectEffectTarget) and the turn loop.
    /// Runtime: <see cref="CrvBattleStepRidersRuntime"/>.
    /// </summary>
    public static class CrvBattleStepRidersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GoblinElite = 85306040;      // Fiend Lv4 2200/1500
        const int LeiLei = 84173492;           // Beast-Warrior Lv4 2300/0
        const int Steamroid = 44729197;        // Machine Lv4 1800/1800
        const int EtoileCyber = 11460577;      // Warrior Lv4 1200/1600
        const int Drillroid = 71218746;        // Machine Lv4 1600/1600
        const int Nanobreaker = 70948327;      // Machine Lv4 1600/1800
        const int Chthonian = 50916353;        // Warrior Lv4 1200/1400
        const int Oxygeddon = 58071123;        // Dinosaur Lv4 1800/800
        const int BlackVeloci = 52319752;      // Dinosaur Lv4 1800/300 (Steamroid's sentence, 400)

        const int DiscFighter = 19612721;
        const int GoblinAttackForce = 78658564;
        const int AxeRaider = 48305365;        // Warrior Normal Lv4 1700/1150
        const int DarkBlade = 11321183;        // Warrior Normal Lv4 1800/1500
        const int Celtic = 91152256;           // Warrior Normal Lv4 1400/1200
        const int KingFog = 84686841;          // Fiend Normal Lv3 1000/900
        const int BasicInsect = 89091579;      // Insect Normal Lv2 500/700
        const int GiantSoldier = 13039848;     // Rock Normal Lv3 1300/2000
        const int MrVolcano = 31477025;        // Pyro Normal Lv5 2100/1300
        const int GeminiElf = 69140098;        // Spellcaster Normal Lv4 1900/900
        const int StopDefense = 63102017;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("CrvBattleStepRiders: " + name, ok, detail ?? "");
            Shapes(db, Check);
            NearMisses(db, Check);
            DefenseLockDuels(db, pDeck, aDeck, Check);
            SteamroidDuels(db, pDeck, aDeck, Check);
            EtoileCyberDuels(db, pDeck, aDeck, Check);
            DrillroidDuels(db, pDeck, aDeck, Check);
            NanobreakerDuels(db, pDeck, aDeck, Check);
            ChthonianDuels(db, pDeck, aDeck, Check);
            OxygeddonDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuelEngine, List<string>> s_logs = new();

        static DuelEngine Watch(DuelEngine e)
        {
            var lines = s_logs.GetValue(e, _ => new List<string>());
            e.OnLog += lines.Add;
            return e;
        }

        static string State(DuelEngine e)
        {
            var lines = s_logs.TryGetValue(e, out var l) ? l : new List<string>();
            return $"pLP={e.Player.LifePoints} oLP={e.Opponent.LifePoints} phase={e.Phase} resp={e.PendingResponse?.Timing} " +
                   $"pend={(e.PendingActivation != null)} log={string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - 8)))}";
        }

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "CrvBattleStepRiders near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

        static bool CompilesTo(CardDef def, Func<EffectClause, bool> shape)
        {
            var prog = CardTextEffectCompiler.Compile(def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && shape(c));
        }

        static bool ReachPlayerBattle(DuelEngine engine)
        {
            for (var t = 0; t < 8 && !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != engine.Player) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1)
                engine.TryEnterBattlePhase(engine.Player);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Player;
        }

        /// <summary>A fresh duel in the Player's Battle Phase: empty boards and hands.</summary>
        static DuelEngine PlayerBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
            ClearBoard(engine);
            reached = ReachPlayerBattle(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>A fresh duel in the opponent's Battle Phase: empty boards and hands.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
            ClearBoard(engine);
            reached = ReachOpponentBattle(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void ToMainPhase2(DuelEngine engine, DuelistState who)
        {
            engine.TryEnterMainPhase2(who);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool AdvanceToMain(DuelEngine engine, DuelistState who, int turn)
        {
            for (var i = 0; i < 12 && !(engine.TurnPlayer == who && engine.TurnNumber == turn &&
                                         engine.Phase == DuelPhase.Main1); i++)
            {
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            return engine.TurnPlayer == who && engine.TurnNumber == turn && engine.Phase == DuelPhase.Main1;
        }

        static bool InGy(DuelistState who, CardInstance card) => who.Graveyard.Contains(card);

        static bool OnField(DuelistState who, CardInstance card) => who.TryFindMonster(card, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static bool IsLock(EffectClause c) =>
            c.Timing == EffectTiming.EndOfBattlePhase &&
            c.Action == EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition && !c.MakesChainLink;

        static bool IsRoleAtk(EffectClause c) =>
            c.Timing == EffectTiming.ContinuousWhileFaceUp &&
            c.Action == EffectActionKind.ThisAtkChangesDuringDamageStepByBattleRole;

        static bool IsBattleConfirmDestroy(EffectClause c) =>
            c.Timing == EffectTiming.BeforeDamageCalculation &&
            c.Action == EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation;

        static bool IsStartDestroy(EffectClause c) =>
            c.Timing == EffectTiming.StartOfDamageStep && c.Action == EffectActionKind.Destroy;

        static bool IsMirror(EffectClause c) =>
            c.Timing == EffectTiming.SentFromFieldToGy &&
            c.Action == EffectActionKind.InflictBattleDamageYouTookThisBattleToOpponent && c.RequiresThisDestroyedByBattle;

        static bool IsBothBurn(EffectClause c) =>
            c.Timing == EffectTiming.SentFromFieldToGy && c.Action == EffectActionKind.InflictDamageToBothPlayers &&
            c.RequiresThisDestroyedByBattle;

        static void Shapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => CardTextEffectCompiler.Compile(db.Get(id));

            foreach (var (id, name) in new[] { (GoblinElite, "Goblin Elite Attack Force"), (LeiLei, "Indomitable Fighter Lei Lei") })
            {
                var p = P(id);
                Check($"{name} compiles whole to the end-of-Battle-Phase Defense lock (Goblin Attack Force's action)",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && IsLock(p.ClauseList[0]) &&
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented, Left(p));
            }

            {
                var p = P(Steamroid);
                var gain = p?.ClauseList.FirstOrDefault(c => c != null && IsRoleAtk(c) && c.DamageStepOnlyWhenAttacking);
                var loss = p?.ClauseList.FirstOrDefault(c => c != null && IsRoleAtk(c) && c.RequiresThisIsAttackTarget);
                Check("Steamroid compiles whole: +500 attacking a monster, -500 when attacked (Damage Step only)",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 2 && gain != null && gain.Amount == 500 &&
                    !gain.RequiresThisIsAttackTarget && !gain.RequiresDirectAttack && loss != null && loss.Amount == -500 &&
                    !loss.DamageStepOnlyWhenAttacking && !loss.RequiresDirectAttack, Left(p));
            }

            {
                // Black Veloci (c52319752): Steamroid's sentence with 400 (the same Lua shape) — compiled by the same atom.
                var p = P(BlackVeloci);
                Check("Black Veloci (Steamroid's sentence, 400) compiles whole: +400 attacking a monster, -400 when attacked",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    p.ClauseList.Exists(c => c != null && IsRoleAtk(c) && c.DamageStepOnlyWhenAttacking && c.Amount == 400) &&
                    p.ClauseList.Exists(c => c != null && IsRoleAtk(c) && c.RequiresThisIsAttackTarget && c.Amount == -400),
                    Left(p));
            }

            {
                var p = P(EtoileCyber);
                Check("Etoile Cyber compiles whole: +500 on a direct attack (Damage Step only)",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && IsRoleAtk(p.ClauseList[0]) &&
                    p.ClauseList[0].RequiresDirectAttack && p.ClauseList[0].Amount == 500 &&
                    !p.ClauseList[0].DamageStepOnlyWhenAttacking, Left(p));
            }

            {
                var p = P(Drillroid);
                var disc = P(DiscFighter);
                Check("Drillroid compiles whole to Disc Fighter's battle-confirm destroy with no DEF floor",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && IsBattleConfirmDestroy(p.ClauseList[0]) &&
                    p.ClauseList[0].Amount == 0 && disc != null && disc.FullyCompiled &&
                    disc.ClauseList.Exists(c => c != null && IsBattleConfirmDestroy(c) && c.Amount == 2000),
                    Left(p) + " disc=" + Left(disc));
            }

            {
                var p = P(Nanobreaker);
                Check("Nanobreaker compiles whole to a start-of-Damage-Step destroy of a face-up Level 3 or lower target",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && IsStartDestroy(p.ClauseList[0]) &&
                    p.ClauseList[0].RequiresTargetFaceUp && p.ClauseList[0].MonsterLevelMax == 3 &&
                    !p.ClauseList[0].RequiresTargetFaceUpDefense && !p.ClauseList[0].RequiresTargetFaceDownDefense, Left(p));
            }

            {
                var p = P(Chthonian);
                Check("Chthonian Soldier compiles whole to the battle-damage mirror (mandatory sent-to-GY trigger)",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && IsMirror(p.ClauseList[0]) &&
                    !p.ClauseList[0].IsOptional && p.ClauseList[0].MakesChainLink, Left(p));
            }

            {
                var p = P(Oxygeddon);
                Check("Oxygeddon compiles whole: battle with a Pyro → each player takes 800 (simultaneous)",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && IsBothBurn(p.ClauseList[0]) &&
                    p.ClauseList[0].Amount == 800 && p.ClauseList[0].BattledMonsterRaceFilter == "Pyro" &&
                    !p.ClauseList[0].IsOptional, Left(p));
            }

            foreach (var id in new[] { GoblinElite, LeiLei, Steamroid, EtoileCyber, Drillroid, Nanobreaker, Chthonian, Oxygeddon })
                Check($"{db.Get(id)?.name}: classified Implemented",
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(db.Get(id)).ToString());

            var goblin = P(GoblinAttackForce);
            Check("Goblin Attack Force still compiles to its Defense lock",
                goblin != null && goblin.FullyCompiled && goblin.ClauseList.Exists(c => c != null && IsLock(c)), Left(goblin));
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var elite = db.Get(GoblinElite);
            var lei = db.Get(LeiLei);
            var steam = db.Get(Steamroid);
            var etoile = db.Get(EtoileCyber);
            var drill = db.Get(Drillroid);
            var nano = db.Get(Nanobreaker);
            var chth = db.Get(Chthonian);
            var oxy = db.Get(Oxygeddon);
            var cases = new (CardDef like, string text, Func<EffectClause, bool> shape)[]
            {
                (elite, "If this card attacks, it is changed to Defense Position at the end of the Battle Phase, and its " +
                        "battle position cannot be changed until the end of your opponent's next turn.", IsLock),
                // Without "except with a card effect" the lock would also stop effects: not this action.
                (lei, "If this card attacks, it is changed to Defense Position at the end of the Battle Phase. This card's " +
                      "battle position cannot be changed until the end of your next turn.", IsLock),
                (lei, "If this card attacks, it is changed to Defense Position at the end of the Battle Phase. This card's " +
                      "battle position cannot be changed until the end of your next turn, except with a card effect. " +
                      "Draw 1 card.", IsLock),
                (steam, "If this card attacks an opponent's monster, this card gains 500 ATK during the Damage Step only.",
                    IsRoleAtk),
                (steam, "If this card attacks an opponent's monster, this card gains 500 ATK until the end of the turn. If " +
                        "this card is attacked by an opponent's monster, this card loses 500 ATK during the Damage Step only.",
                    IsRoleAtk),
                (etoile, "If this card attacks your opponent directly, it gains 500 ATK until the end of the turn.", IsRoleAtk),
                (etoile, "If this card attacks your opponent directly, it gains 500 ATK during the Damage Step only. Draw 1 card.",
                    IsRoleAtk),
                (drill, "Before damage calculation, if this card attacks a face-down Defense Position monster: Destroy that " +
                        "monster.", IsBattleConfirmDestroy),
                (drill, "Before damage calculation, if this card attacks a Defense Position monster: Destroy that monster, " +
                        "and if you do, inflict 500 damage to your opponent.", IsBattleConfirmDestroy),
                (nano, "If this card attacks a face-down Level 3 or lower monster, destroy the monster immediately with this " +
                       "card's effect without applying damage calculation.", IsStartDestroy),
                (nano, "If this card attacks a face-up Level 3 or lower monster, destroy the monster immediately with this " +
                       "card's effect without applying damage calculation. Draw 1 card.", IsStartDestroy),
                (chth, "When this card is destroyed by battle and sent to the Graveyard, the Battle Damage you took from this " +
                       "battle is also inflicted to your opponent.", IsMirror),
                (chth, "When this card is destroyed by an opponent's attacking monster and sent to the Graveyard, the Battle " +
                       "Damage you took from this battle is doubled.", IsMirror),
                // FIRE is an Attribute, not a Type.
                (oxy, "If this card is destroyed by battle with a FIRE monster and sent to the GY: Each player takes 800 damage.",
                    IsBothBurn),
                (oxy, "If this card is destroyed by battle with a Pyro monster and sent to the GY: Each player takes 800 damage. " +
                      "Draw 1 card.", IsBothBurn),
                (oxy, "If this card is destroyed by battle with a Pyro monster: Each player takes 800 damage.", IsBothBurn),
            };
            foreach (var (like, text, shape) in cases)
                Check($"near miss stays uncompiled: {text}", !CompilesTo(Synthetic(like, text), shape),
                    Left(CardTextEffectCompiler.Compile(Synthetic(like, text))));

            Check("Oxygeddon's sentence generalizes to a two-word Type (Winged Beast)",
                CompilesTo(Synthetic(oxy, "If this card is destroyed by battle with a Winged Beast monster and sent to the " +
                                          "Graveyard: Each player takes 500 damage."),
                    c => IsBothBurn(c) && c.Amount == 500 && c.BattledMonsterRaceFilter == "Winged Beast"),
                Left(CardTextEffectCompiler.Compile(Synthetic(oxy, "If this card is destroyed by battle with a Winged Beast " +
                                                                    "monster and sent to the Graveyard: Each player takes 500 damage."))));
        }

        // ───────────────────────────── Goblin Elite Attack Force / Lei Lei ─────────────────────────────

        static void DefenseLockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var elite = PlaceMonster(engine, o, GoblinElite, 0, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, p, BasicInsect, 0, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, o, elite, prey);
                var inBattle = elite.Position == BattlePosition.Attack;
                ToMainPhase2(engine, o);
                Check("Goblin Elite Attack Force: after attacking it is changed to Defense Position at the end of the Battle Phase",
                    attacked && inBattle && InGy(p, prey) && elite.FaceUp && elite.Position == BattlePosition.Defense &&
                    engine.Phase == DuelPhase.Main2, $"reached={reached} attacked={attacked} pos={elite.Position} {State(engine)}");

                var next = AdvanceToMain(engine, o, turn + 2);
                Check("Goblin Elite Attack Force: its battle position cannot be changed during its controller's next turn",
                    next && OnField(o, elite) && !engine.CanChangePosition(o, elite) &&
                    BattlePhaseEndRuntime.ManualPositionChangeLocked(engine, elite),
                    $"next={next} turn={engine.TurnNumber} can={engine.CanChangePosition(o, elite)}");

                var after = AdvanceToMain(engine, o, turn + 4);
                Check("Goblin Elite Attack Force: the lock ends after that next turn",
                    after && engine.CanChangePosition(o, elite),
                    $"after={after} turn={engine.TurnNumber} can={engine.CanChangePosition(o, elite)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                var elite = PlaceMonster(engine, o, GoblinElite, 0, BattlePosition.Attack, true);
                ToMainPhase2(engine, o);
                Check("Goblin Elite Attack Force: one that did not attack stays in Attack Position, unlocked",
                    reached && elite.Position == BattlePosition.Attack && engine.CanChangePosition(o, elite),
                    $"reached={reached} pos={elite.Position} can={engine.CanChangePosition(o, elite)}");
            }

            {
                // Lei Lei: the lock stops only manual changes — Stop Defense (a card effect) still changes it to Attack.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lei = PlaceMonster(engine, o, LeiLei, 0, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, o, lei, null);
                ToMainPhase2(engine, o);
                var toDefense = lei.Position == BattlePosition.Defense && lei.FaceUp;
                var mine = AdvanceToMain(engine, p, turn + 1);
                p.Hand.Clear();
                o.Hand.Clear();
                var sd = PutInHand(engine, p, StopDefense);
                var act = mine && engine.TryActivateSpellTrap(p, sd, fromHand: true);
                for (var i = 0; i < 8; i++)
                {
                    if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(lei);
                    else if (engine.IsAwaitingResponse) engine.PassResponse();
                    else break;
                }

                Check("Indomitable Fighter Lei Lei: Defense after attacking, and a card effect (Stop Defense) still changes it",
                    attacked && toDefense && act && lei.Position == BattlePosition.Attack && OnField(o, lei),
                    $"reached={reached} attacked={attacked} toDef={toDefense} mine={mine} act={act} pos={lei.Position} " +
                    State(engine));

                var next = AdvanceToMain(engine, o, turn + 2);
                Check("Indomitable Fighter Lei Lei: its controller still cannot change its position during their next turn",
                    next && !engine.CanChangePosition(o, lei) && BattlePhaseEndRuntime.ManualPositionChangeLocked(engine, lei),
                    $"next={next} turn={engine.TurnNumber} can={engine.CanChangePosition(o, lei)}");
                var after = AdvanceToMain(engine, o, turn + 4);
                Check("Indomitable Fighter Lei Lei: the lock ends after that next turn",
                    after && engine.CanChangePosition(o, lei),
                    $"after={after} turn={engine.TurnNumber} can={engine.CanChangePosition(o, lei)}");
            }
        }

        // ───────────────────────────── Steamroid ─────────────────────────────

        static void SteamroidDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 1800 + 500 vs Dark Blade 1800: Dark Blade alone is destroyed, 500 damage; the gain ends with the battle.
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var steam = PlaceMonster(engine, p, Steamroid, 0, BattlePosition.Attack, true);
                var blade = PlaceMonster(engine, o, DarkBlade, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var pLp = p.LifePoints;
                var ok = reached && Attack(engine, p, steam, blade);
                Check("Steamroid: attacking a monster it gains 500 ATK for the battle (2300 vs 1800)",
                    ok && InGy(o, blade) && OnField(p, steam) && o.LifePoints == oLp - 500 && p.LifePoints == pLp &&
                    steam.CurrentAtk == 1800, $"ok={ok} atk={steam.CurrentAtk} {State(engine)}");
            }

            {
                // Attacked by Axe Raider (1700): 1800 - 500 = 1300 — Steamroid is destroyed, 400 damage.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var steam = PlaceMonster(engine, p, Steamroid, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, o, axe, steam);
                Check("Steamroid: attacked by an opponent's monster it loses 500 ATK for the battle (1300 vs 1700)",
                    ok && InGy(p, steam) && OnField(o, axe) && p.LifePoints == pLp - 400 && o.LifePoints == oLp,
                    $"ok={ok} {State(engine)}");
            }

            {
                // A direct attack is neither role: 1800 damage.
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var steam = PlaceMonster(engine, p, Steamroid, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, steam, null);
                Check("Steamroid: a direct attack inflicts its plain 1800 ATK",
                    ok && o.LifePoints == oLp - 1800, $"ok={ok} {State(engine)}");
            }
        }

        // ───────────────────────────── Etoile Cyber ─────────────────────────────

        static void EtoileCyberDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var etoile = PlaceMonster(engine, p, EtoileCyber, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, etoile, null);
                Check("Etoile Cyber: a direct attack inflicts 1200 + 500 = 1700, and the gain ends with the battle",
                    ok && o.LifePoints == oLp - 1700 && etoile.CurrentAtk == 1200, $"ok={ok} atk={etoile.CurrentAtk} {State(engine)}");
            }

            {
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var etoile = PlaceMonster(engine, p, EtoileCyber, 0, BattlePosition.Attack, true);
                var fog = PlaceMonster(engine, o, KingFog, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, etoile, fog);
                Check("Etoile Cyber: attacking a monster it gains nothing (1200 vs 1000: 200 damage)",
                    ok && InGy(o, fog) && o.LifePoints == oLp - 200, $"ok={ok} {State(engine)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var etoile = PlaceMonster(engine, p, EtoileCyber, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var ok = reached && Attack(engine, o, axe, etoile);
                Check("Etoile Cyber: when attacked it gains nothing (1200 vs 1700: destroyed, 500 damage)",
                    ok && InGy(p, etoile) && p.LifePoints == pLp - 500, $"ok={ok} {State(engine)}");
            }
        }

        // ───────────────────────────── Drillroid ─────────────────────────────

        static void DrillroidDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var faceUp in new[] { true, false })
            {
                // Giant Soldier of Stone (DEF 2000 > 1600): destroyed by the effect, so no damage calculation (no 400 to us).
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var drill = PlaceMonster(engine, p, Drillroid, 0, BattlePosition.Attack, true);
                var stone = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Defense, faceUp);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, drill, stone);
                Check($"Drillroid: attacking a {(faceUp ? "face-up" : "face-down")} Defense Position monster (DEF 2000) " +
                      "destroys it before damage calculation",
                    ok && InGy(o, stone) && !stone.WasDestroyedByBattle && OnField(p, drill) && p.LifePoints == pLp &&
                    o.LifePoints == oLp, $"ok={ok} byBattle={stone.WasDestroyedByBattle} {State(engine)}");
            }

            {
                // An Attack Position target: no effect, Drillroid (1600) loses to Axe Raider (1700).
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var drill = PlaceMonster(engine, p, Drillroid, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var ok = reached && Attack(engine, p, drill, axe);
                Check("Drillroid: an Attack Position target is not destroyed by its effect (normal battle)",
                    ok && OnField(o, axe) && InGy(p, drill) && p.LifePoints == pLp - 100, $"ok={ok} {State(engine)}");
            }
        }

        // ───────────────────────────── Nanobreaker ─────────────────────────────

        static void NanobreakerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Face-up Level 3 King Fog (Attack, 1000): destroyed by the effect — no battle damage (600 otherwise).
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var nano = PlaceMonster(engine, p, Nanobreaker, 0, BattlePosition.Attack, true);
                var fog = PlaceMonster(engine, o, KingFog, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, nano, fog);
                Check("Nanobreaker: a face-up Level 3 attack target is destroyed by its effect with no damage calculation",
                    ok && InGy(o, fog) && !fog.WasDestroyedByBattle && o.LifePoints == oLp && OnField(p, nano),
                    $"ok={ok} byBattle={fog.WasDestroyedByBattle} {State(engine)}");
            }

            {
                // Face-up Defense Level 3 Giant Soldier of Stone (DEF 2000): destroyed by the effect, no 400 to us.
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var nano = PlaceMonster(engine, p, Nanobreaker, 0, BattlePosition.Attack, true);
                var stone = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Defense, true);
                var pLp = p.LifePoints;
                var ok = reached && Attack(engine, p, nano, stone);
                Check("Nanobreaker: a face-up Defense Position Level 3 target is destroyed too (any position)",
                    ok && InGy(o, stone) && p.LifePoints == pLp, $"ok={ok} {State(engine)}");
            }

            {
                // Face-down Level 3 Giant Soldier of Stone: not face-up at the start of the Damage Step — flipped, 400 to us.
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var nano = PlaceMonster(engine, p, Nanobreaker, 0, BattlePosition.Attack, true);
                var stone = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Defense, false);
                var pLp = p.LifePoints;
                var ok = reached && Attack(engine, p, nano, stone);
                Check("Nanobreaker: a face-down Level 3 target is not destroyed (it is flipped and battles)",
                    ok && OnField(o, stone) && stone.FaceUp && p.LifePoints == pLp - 400, $"ok={ok} {State(engine)}");
            }

            {
                // Face-up Level 4 Celtic Guardian (1400): ordinary battle, 200 damage.
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var nano = PlaceMonster(engine, p, Nanobreaker, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, nano, celtic);
                Check("Nanobreaker: a Level 4 target is not destroyed by its effect (ordinary battle)",
                    ok && InGy(o, celtic) && celtic.WasDestroyedByBattle && o.LifePoints == oLp - 200,
                    $"ok={ok} byBattle={celtic.WasDestroyedByBattle} {State(engine)}");
            }

            {
                // Its own monster attacked by a Level 3: it is not the attacker, nothing happens.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var nano = PlaceMonster(engine, p, Nanobreaker, 0, BattlePosition.Attack, true);
                var stone = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, o, stone, nano);
                Check("Nanobreaker: attacked by a face-up Level 3 monster, its effect does not apply",
                    ok && InGy(o, stone) && stone.WasDestroyedByBattle && o.LifePoints == oLp - 300,
                    $"ok={ok} {State(engine)}");
            }
        }

        // ───────────────────────────── Chthonian Soldier ─────────────────────────────

        static void ChthonianDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacked by Axe Raider in Attack Position: we take 500 battle damage, the opponent takes 500 too.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var chth = PlaceMonster(engine, p, Chthonian, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, o, axe, chth);
                Check("Chthonian Soldier: destroyed by an attacking monster, the 500 battle damage we took hits the opponent too",
                    ok && InGy(p, chth) && p.LifePoints == pLp - 500 && o.LifePoints == oLp - 500 &&
                    !engine.IsAwaitingEffectTarget, $"ok={ok} {State(engine)}");
            }

            {
                // In Defense Position: no battle damage, so it does not activate.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var chth = PlaceMonster(engine, p, Chthonian, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, o, axe, chth);
                Check("Chthonian Soldier: destroyed in Defense Position (0 battle damage) — no damage to the opponent",
                    ok && InGy(p, chth) && p.LifePoints == pLp && o.LifePoints == oLp, $"ok={ok} {State(engine)}");
            }

            {
                // It attacks and is destroyed: not "by an opponent's attacking monster".
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var chth = PlaceMonster(engine, p, Chthonian, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, chth, axe);
                Check("Chthonian Soldier: destroyed while attacking — its effect does not activate",
                    ok && InGy(p, chth) && p.LifePoints == pLp - 500 && o.LifePoints == oLp, $"ok={ok} {State(engine)}");
            }
        }

        // ───────────────────────────── Oxygeddon ─────────────────────────────

        static void OxygeddonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacked by Mr. Volcano (Pyro, 2100): 300 battle damage to us, then 800 to each player.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var oxy = PlaceMonster(engine, p, Oxygeddon, 0, BattlePosition.Attack, true);
                var volcano = PlaceMonster(engine, o, MrVolcano, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, o, volcano, oxy);
                Check("Oxygeddon: destroyed by battle with a Pyro monster — each player takes 800",
                    ok && InGy(p, oxy) && p.LifePoints == pLp - 300 - 800 && o.LifePoints == oLp - 800,
                    $"ok={ok} {State(engine)}");
            }

            {
                // It attacks Mr. Volcano and is destroyed: the battle target is a Pyro either way.
                var engine = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var oxy = PlaceMonster(engine, p, Oxygeddon, 0, BattlePosition.Attack, true);
                var volcano = PlaceMonster(engine, o, MrVolcano, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, p, oxy, volcano);
                Check("Oxygeddon: destroyed attacking a Pyro monster — each player takes 800",
                    ok && InGy(p, oxy) && p.LifePoints == pLp - 300 - 800 && o.LifePoints == oLp - 800,
                    $"ok={ok} {State(engine)}");
            }

            {
                // A non-Pyro (Gemini Elf, 1900): no burn.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var oxy = PlaceMonster(engine, p, Oxygeddon, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = reached && Attack(engine, o, elf, oxy);
                Check("Oxygeddon: destroyed by battle with a non-Pyro monster — no damage",
                    ok && InGy(p, oxy) && p.LifePoints == pLp - 100 && o.LifePoints == oLp, $"ok={ok} {State(engine)}");
            }

            {
                // Both players at 800: the two damages are one event — a double KO is a DRAW.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var oxy = PlaceMonster(engine, p, Oxygeddon, 0, BattlePosition.Defense, true);
                var volcano = PlaceMonster(engine, o, MrVolcano, 0, BattlePosition.Attack, true);
                p.LifePoints = 800;
                o.LifePoints = 800;
                var ok = reached && Attack(engine, o, volcano, oxy);
                Check("Oxygeddon: each player at 800 LP — both reach 0 at the same time and the Duel is a DRAW",
                    ok && InGy(p, oxy) && engine.GameOver && engine.IsDraw, $"ok={ok} over={engine.GameOver} " +
                                                                          $"draw={engine.IsDraw} {State(engine)}");
            }
        }
    }
}
