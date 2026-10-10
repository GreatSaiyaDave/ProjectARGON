using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "BattleWindows": attack-declaration responses and Damage Step events — Fairy Box and Jam Defender
    /// (face-up Continuous Trap triggers on an opponent's attack declaration), Dark Spirit of the Silent (negate the
    /// attack; another monster battles instead), Dreamsprite (the attacker battles another monster), Destruction Punch
    /// (the end-of-Damage-Step window) and Michizure (a monster sent from the field to your GY). Runtime:
    /// <see cref="BattleWindowsRuntime"/>.
    /// </summary>
    public static class BattleWindowsRegressionTests
    {
        const int FairyBox = 21598948;
        const int JamDefender = 21558682;
        const int DarkSpirit = 93599951;
        const int Dreamsprite = 8687195;
        const int DestructionPunch = 5616412;
        const int Michizure = 37580756;
        const int RevivalJam = 31709826;      // Aqua Lv4 1500/500

        const int Celtic = 91152256;          // Warrior Lv4 1400/1200
        const int Bewd = 89631139;            // Dragon Lv8 3000/2500
        const int SummonedSkull = 70781052;   // Fiend Lv6 2500/1200
        const int SkullServant = 32274490;    // Zombie Lv1 300/200
        const int GiantSoldier = 13039848;    // Rock Lv3 1300/2000
        const int MysticalElf = 15025844;     // Spellcaster Lv4 800/2000
        const int Fissure = 66788016;         // Normal Spell: destroy the opponent's face-up monster with the lowest ATK
        const int ExiledForce = 74131780;     // Warrior Lv4 1000/1000: Tribute this card; target 1 monster on the field; destroy it
        const int ShareThePain = 56830749;    // Normal Spell: Tribute 1 monster; make your opponent Tribute 1 monster

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("BattleWindows: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            FairyBoxDuels(db, pDeck, aDeck, Check);
            JamDefenderDuels(db, pDeck, aDeck, Check);
            DestructionPunchDuels(db, pDeck, aDeck, Check);
            MichizureDuels(db, pDeck, aDeck, Check);
            DreamspriteDuels(db, pDeck, aDeck, Check);
            DarkSpiritDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}]";

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            CompiledCardProgram Prog(int id) => CardTextEffectCompiler.Compile(db.Get(id));
            bool Implemented(int id) => CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

            var box = Prog(FairyBox);
            Check("Fairy Box: declaration trigger (coin, ATK 0 until the end of the Battle Phase) + optional 500 LP upkeep",
                box != null && box.FullyCompiled && Implemented(FairyBox) && box.ClauseList.Count == 2 &&
                box.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                box.ClauseList[0].Action == EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase &&
                box.ClauseList[1].Timing == EffectTiming.StandbyPhase &&
                box.ClauseList[1].Action == EffectActionKind.PayLpOrDestroyThis &&
                box.ClauseList[1].PayLpAmount == 500 && box.ClauseList[1].UpkeepIsOptional, Describe(box));

            var jam = Prog(JamDefender);
            Check("Jam Defender: optional declaration trigger naming \"Revival Jam\"",
                jam != null && jam.FullyCompiled && Implemented(JamDefender) && jam.ClauseList.Count == 1 &&
                jam.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                jam.ClauseList[0].Action == EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl &&
                jam.ClauseList[0].NamedCard == "Revival Jam" && jam.ClauseList[0].IsOptional, Describe(jam));

            var spirit = Prog(DarkSpirit);
            Check("Dark Spirit of the Silent: AttackDeclared, targets another face-up monster of the opponent",
                spirit != null && spirit.FullyCompiled && Implemented(DarkSpirit) && spirit.ClauseList.Count == 1 &&
                spirit.ClauseList[0].Timing == EffectTiming.AttackDeclared &&
                spirit.ClauseList[0].Action == EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead &&
                spirit.ClauseList[0].Zone == EffectZoneFilter.OppFaceUpMonsters &&
                spirit.ClauseList[0].RequiresTargetChoice, Describe(spirit));

            var sprite = Prog(Dreamsprite);
            Check("Dreamsprite: mandatory battle-confirm switch while it is the attack target",
                sprite != null && sprite.FullyCompiled && Implemented(Dreamsprite) && sprite.ClauseList.Count == 1 &&
                sprite.ClauseList[0].Timing == EffectTiming.BeforeDamageCalculation &&
                sprite.ClauseList[0].Action == EffectActionKind.BattleAnotherOwnMonsterInstead &&
                sprite.ClauseList[0].RequiresThisIsAttackTarget && !sprite.ClauseList[0].IsOptional, Describe(sprite));

            var punch = Prog(DestructionPunch);
            Check("Destruction Punch: Activate at the end of the Damage Step, destroy the attacker (not targeted)",
                punch != null && punch.FullyCompiled && Implemented(DestructionPunch) && punch.ClauseList.Count == 1 &&
                punch.ClauseList[0].Timing == EffectTiming.Activate &&
                punch.ClauseList[0].Action == EffectActionKind.DestroyAttackerAtEndOfDamageStep &&
                punch.ClauseList[0].ActivationCondition ==
                BattleWindowsRuntime.AttackerAtkBelowYourDefenderDefAtEndOfDamageStep &&
                !punch.ClauseList[0].RequiresTargetChoice, Describe(punch));

            var michi = Prog(Michizure);
            Check("Michizure: Activate when a monster is sent from the field to your GY; target 1 monster on the field",
                michi != null && michi.FullyCompiled && Implemented(Michizure) && michi.ClauseList.Count == 1 &&
                michi.ClauseList[0].Timing == EffectTiming.Activate &&
                michi.ClauseList[0].Action == EffectActionKind.Destroy &&
                michi.ClauseList[0].Zone == EffectZoneFilter.FieldAnyMonster &&
                michi.ClauseList[0].RequiresTargetChoice &&
                michi.ClauseList[0].ActivationCondition == BattleWindowsRuntime.MonsterSentFromFieldToYourGy,
                Describe(michi));

            var actions = new[]
            {
                EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase,
                EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl,
                EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                EffectActionKind.BattleAnotherOwnMonsterInstead,
                EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead
            };
            Check("New actions map to a resolution kind (not UniqueException)",
                actions.All(a => EffectVocabulary.ResolutionOf(new EffectClause { Action = a }) != EffectResolutionKind.UniqueException &&
                                 EffectVocabulary.ResolutionOf(new EffectClause { Action = a }) != EffectResolutionKind.None));
        }

        static CompiledCardProgram Synthetic(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "BattleWindows probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : "effect",
                race = race, attribute = "LIGHT", level = 2, desc = text
            });

        static bool Has(CompiledCardProgram p, EffectActionKind a) =>
            p != null && p.ClauseList.Exists(c => c != null && c.Action == a);

        static void NearMisses(CheckFn Check)
        {
            const string box =
                "When your opponent's monster declares an attack: Toss a coin and call it. If you call it right, the " +
                "attacking monster's ATK becomes 0 until the end of the Battle Phase.";
            var i = 0;
            (string text, string type, string race, EffectActionKind action, string why)[] misses =
            {
                (box.Replace("until the end of the Battle Phase", "until the end of this turn"), "Trap Card",
                    "Continuous", EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase,
                    "Fairy Box with another duration"),
                (box.Replace("Toss a coin and call it. If you call it right, the", "The"), "Trap Card", "Continuous",
                    EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase, "Fairy Box without its coin"),
                ("When an opponent's monster declares an attack on a monster you control: You can target 1 monster " +
                 "you control; switch the attack target to that target.", "Trap Card", "Continuous",
                    EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl, "Jam Defender with no named card"),
                ("When an opponent's monster declares an attack on a monster you control: You can target 1 " +
                 "\"Revival Jam\" you control; switch the attack target to that target. Then draw 1 card.",
                    "Trap Card", "Continuous", EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl,
                    "Jam Defender with an extra sentence"),
                ("If the ATK of an attacking monster your opponent controls is lower than the DEF of the attacked " +
                 "Defense Position monster you control, destroy the attacking monster at the end of the Damage Step.",
                    "Trap Card", "Normal", EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                    "Destruction Punch without its rulings sentence"),
                ("If the ATK of an attacking monster your opponent controls is lower than the ATK of the attacked " +
                 "monster you control, destroy the attacking monster at the end of the Damage Step. (Damage " +
                 "calculation is applied normally.)", "Trap Card", "Normal",
                    EffectActionKind.DestroyAttackerAtEndOfDamageStep, "Destruction Punch comparing ATK"),
                ("When a monster is sent from the field to your opponent's GY, even during the Damage Step: Target 1 " +
                 "monster on the field; destroy it.", "Trap Card", "Normal", EffectActionKind.Destroy,
                    "Michizure for the opponent's GY"),
                ("When a monster is sent from the field to your GY: Target 1 monster on the field; destroy it.",
                    "Trap Card", "Normal", EffectActionKind.Destroy, "Michizure without \"even during the Damage Step\""),
                ("When attacked, select another 1 of your Monster Cards and designate it as the attack's target, then " +
                 "calculate damage.", "Effect Monster", "Plant", EffectActionKind.BattleAnotherOwnMonsterInstead,
                    "Dreamsprite without \"by your opponent's monster\""),
                ("Activate only when an opponent's monster declares an attack. Negate that attack, and select another " +
                 "face-up monster your opponent controls. That monster attacks instead.", "Trap Card", "Normal",
                    EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead,
                    "Dark Spirit of the Silent without its position change"),
            };
            foreach (var (text, type, race, action, why) in misses)
            {
                var prog = Synthetic(990700 + i++, type, race, text);
                Check($"Near miss: {why} is not compiled",
                    prog == null || !prog.FullyCompiled || !Has(prog, action), Describe(prog));
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        static bool Offers(DuelEngine engine, ResponseTiming timing, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == timing &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static bool OffersAnywhere(DuelEngine engine, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.LegalCards.Contains(card);

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>The opponent's Battle Phase with an empty board and empty hands.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(engine);
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The Player's Main Phase 1 (turn 3+) with an empty board and empty hands.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The Player's Battle Phase (turn 3+) with an empty board and empty hands.</summary>
        static DuelEngine PlayerBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = PlayerMain(db, pDeck, aDeck, out var main);
            reached = main && engine.TryEnterBattlePhase(engine.Player) && engine.Phase == DuelPhase.Battle;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine;
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            return m;
        }

        static CardInstance FaceUpTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        // ───────────────────────── Fairy Box ─────────────────────────

        static void FairyBoxDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The Player's face-up Fairy Box; the opponent's Summoned Skull attacks Celtic Guardian. Right call: the
                // Skull's ATK becomes 0 and Celtic Guardian destroys it.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                FaceUpTrap(e, p, FairyBox, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                var oppLp = opp.LifePoints;
                var pLp = p.LifePoints;
                var declared = e.TryAttack(opp, skull, celtic);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingCoinCall;
                e.Rng.QueueCoin(true);
                var called = asked && e.TrySelectCoinCall(true);
                DrainCombat(e);
                Check("Fairy Box: the opponent's attack declaration asks its controller to call a coin",
                    reached && declared && asked && called, $"declared={declared} asked={asked} {Window(e)}");
                Check("Fairy Box: right call — the attacker's ATK becomes 0; it is destroyed by battle",
                    opp.Graveyard.Contains(skull) && p.TryFindMonster(celtic, out _) &&
                    opp.LifePoints == oppLp - 1400 && p.LifePoints == pLp && !e.HasDeclaredAttack,
                    $"skullGy={opp.Graveyard.Contains(skull)} oppLp={oppLp}->{opp.LifePoints} {Window(e)}");
            }

            {
                // Wrong call: no effect; the Skull destroys Celtic Guardian.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                FaceUpTrap(e, p, FairyBox, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.TryAttack(opp, skull, celtic);
                e.Rng.QueueCoin(false);
                var called = e.IsAwaitingEffectTarget && e.TrySelectCoinCall(true);
                DrainCombat(e);
                Check("Fairy Box: wrong call — no effect, the attack goes on",
                    called && skull.CurrentAtk == 2500 && p.Graveyard.Contains(celtic) && p.LifePoints == pLp - 1100,
                    $"called={called} atk={skull.CurrentAtk} pLp={pLp}->{p.LifePoints}");
            }

            {
                // The ATK stays 0 until the end of the Battle Phase (the Skull survives attacking Giant Soldier's DEF),
                // a second attack declaration triggers it again, and Main Phase 2 restores the ATK.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var soldier = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
                FaceUpTrap(e, p, FairyBox, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                var servant = Attacker(e, opp, SkullServant, 1);
                e.NotifyPublic();
                var oppLp = opp.LifePoints;
                e.TryAttack(opp, skull, soldier);
                e.Rng.QueueCoin(true);
                if (e.IsAwaitingEffectTarget) e.TrySelectCoinCall(true);
                DrainCombat(e);
                var zeroAfterBattle = skull.CurrentAtk == 0 && opp.TryFindMonster(skull, out _);
                var lpAfter = opp.LifePoints;
                e.TryAttack(opp, servant, soldier);
                var askedAgain = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingCoinCall;
                e.Rng.QueueCoin(false);
                if (e.IsAwaitingEffectTarget) e.TrySelectCoinCall(true);
                DrainCombat(e);
                e.TryEnterMainPhase2(opp);
                Check("Fairy Box: ATK 0 lasts through the Battle Phase, every declaration triggers, Main Phase 2 restores it",
                    zeroAfterBattle && lpAfter == oppLp - 2000 && askedAgain && e.Phase == DuelPhase.Main2 &&
                    skull.CurrentAtk == 2500 && p.TryFindMonster(soldier, out _),
                    $"zero={zeroAfterBattle} oppLp={oppLp}->{lpAfter} again={askedAgain} phase={e.Phase} atk={skull.CurrentAtk}");
            }

            {
                // The AI's Fairy Box: the Player's Blue-Eyes attacks; the AI calls (Heads) and is right.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = Attacker(e, p, Bewd, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                FaceUpTrap(e, opp, FairyBox, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.Rng.QueueCoin(true);
                var declared = e.TryAttack(p, bewd, celtic);
                var noPrompt = !e.IsAwaitingEffectTarget;
                DrainCombat(e);
                Check("Fairy Box: the AI's copy calls at once; Blue-Eyes at 0 ATK is destroyed by Celtic Guardian",
                    reached && declared && noPrompt && p.Graveyard.Contains(bewd) && opp.TryFindMonster(celtic, out _) &&
                    p.LifePoints == pLp - 1400, $"reached={reached} declared={declared} pLp={pLp}->{p.LifePoints} {Window(e)}");
            }

            {
                // A Set (face-down) Fairy Box does nothing.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, FairyBox, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingCoinCall;
                PassAll(e);
                DrainCombat(e);
                Check("Fairy Box: a Set copy does not trigger", !asked && p.Graveyard.Contains(celtic),
                    $"asked={asked}");
            }
        }

        // ───────────────────────── Jam Defender ─────────────────────────

        static void JamDefenderDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Summoned Skull attacks Celtic Guardian; the Player switches it to Revival Jam (DEF 500).
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var jam = PlaceMonster(e, p, RevivalJam, 1, BattlePosition.Defense, true);
                FaceUpTrap(e, p, JamDefender, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var declared = e.TryAttack(opp, skull, celtic);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1 &&
                            e.PendingActivation.LegalTargets.Contains(jam);
                var picked = asked && e.TrySelectEffectTarget(jam);
                DrainCombat(e);
                Check("Jam Defender: offers the face-up Revival Jam when the opponent attacks another monster",
                    reached && declared && asked && picked, $"declared={declared} asked={asked} {Window(e)}");
                Check("Jam Defender: the attack now targets Revival Jam — Celtic Guardian untouched",
                    p.TryFindMonster(celtic, out _) && !p.TryFindMonster(jam, out _) && p.LifePoints == pLp &&
                    !e.HasDeclaredAttack, $"celtic={p.TryFindMonster(celtic, out _)} jam={p.TryFindMonster(jam, out _)} " +
                                           $"pLp={pLp}->{p.LifePoints}");
            }

            {
                // CANCEL declines (optional); the attack stays on Celtic Guardian.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var jam = PlaceMonster(e, p, RevivalJam, 1, BattlePosition.Defense, true);
                FaceUpTrap(e, p, JamDefender, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                var asked = e.IsAwaitingEffectTarget;
                var cancelled = asked && e.CancelEffectTargeting();
                DrainCombat(e);
                Check("Jam Defender: CANCEL declines — the original attack goes on",
                    asked && cancelled && p.Graveyard.Contains(celtic) && p.TryFindMonster(jam, out _),
                    $"asked={asked} cancelled={cancelled} celticGy={p.Graveyard.Contains(celtic)}");
            }

            {
                // Not offered: an attack on Revival Jam itself (no other Revival Jam), a direct attack, a face-down
                // Revival Jam.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var jam = PlaceMonster(e, p, RevivalJam, 1, BattlePosition.Attack, true);
                FaceUpTrap(e, p, JamDefender, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, jam);
                // BattleDestroyedGyRecovery (v162): the destroyed Revival Jam's own optional LP prompt is not Jam Defender.
                var askedOnJam = e.IsAwaitingEffectTarget && e.PendingActivation?.Card != jam;
                PassAll(e);
                DrainCombat(e);

                var e2 = OpponentBattle(db, pDeck, aDeck, out _);
                var celtic2 = PlaceMonster(e2, e2.Player, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e2, e2.Player, RevivalJam, 1, BattlePosition.Defense, false);
                FaceUpTrap(e2, e2.Player, JamDefender, 0);
                var skull2 = Attacker(e2, e2.Opponent, SummonedSkull, 0);
                e2.NotifyPublic();
                e2.TryAttack(e2.Opponent, skull2, celtic2);
                var askedFaceDown = e2.IsAwaitingEffectTarget;
                PassAll(e2);
                DrainCombat(e2);
                Check("Jam Defender: not offered for an attack on the only Revival Jam or with a face-down one",
                    !askedOnJam && !askedFaceDown && e2.Player.Graveyard.Contains(celtic2),
                    $"onJam={askedOnJam} faceDown={askedFaceDown}");
            }

            {
                // The AI's Jam Defender: the Player's Celtic Guardian attacks Skull Servant (it would not survive); the AI
                // switches the attack to its Revival Jam (ATK 1500), which destroys Celtic Guardian.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = Attacker(e, p, Celtic, 0);
                var servant = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
                var jam = PlaceMonster(e, opp, RevivalJam, 1, BattlePosition.Attack, true);
                FaceUpTrap(e, opp, JamDefender, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var declared = e.TryAttack(p, celtic, servant);
                DrainCombat(e);
                Check("Jam Defender: the AI switches an attack its monster would not survive to Revival Jam",
                    reached && declared && p.Graveyard.Contains(celtic) && opp.TryFindMonster(servant, out _) &&
                    opp.TryFindMonster(jam, out _) && p.LifePoints == pLp - 100,
                    $"declared={declared} celticGy={p.Graveyard.Contains(celtic)} pLp={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────── Destruction Punch ─────────────────────────

        static void DestructionPunchDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Celtic Guardian (1400) attacks the Player's Mystical Elf (DEF 2000): damage calculation
                // applies (600 to the opponent), then Destruction Punch destroys Celtic Guardian.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Defense, true);
                var punch = PlaceSetTrap(e, p, DestructionPunch, 0);
                var celtic = Attacker(e, opp, Celtic, 0);
                e.NotifyPublic();
                var oppLp = opp.LifePoints;
                var declared = e.TryAttack(opp, celtic, elf);
                var notAtDeclaration = !OffersAnywhere(e, punch) ||
                                       e.PendingResponse.Timing == ResponseTiming.EndOfDamageStep;
                for (var i = 0; i < 4 && e.IsAwaitingResponse &&
                                e.PendingResponse.Timing != ResponseTiming.EndOfDamageStep; i++)
                    e.PassResponse();
                var offered = Offers(e, ResponseTiming.EndOfDamageStep, p, punch);
                var damageFirst = opp.LifePoints == oppLp - 600;
                var act = offered && e.TryActivateSpellTrap(p, punch, fromHand: false);
                PassAll(e);
                DrainCombat(e);
                Check("Destruction Punch: offered at the end of the Damage Step, after damage calculation",
                    reached && declared && notAtDeclaration && offered && damageFirst && act,
                    $"declared={declared} offered={offered} damageFirst={damageFirst} {Window(e)}");
                Check("Destruction Punch: destroys the attacking monster",
                    opp.Graveyard.Contains(celtic) && p.TryFindMonster(elf, out _) && p.Graveyard.Contains(punch) &&
                    e.Phase == DuelPhase.Battle && !e.HasDeclaredAttack && e.Chain.Count == 0,
                    $"celticGy={opp.Graveyard.Contains(celtic)} punchGy={p.Graveyard.Contains(punch)} {Window(e)}");
            }

            {
                // Not offered: the attacked monster is in Attack Position; the attacker's ATK is not lower than the DEF.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var punch = PlaceSetTrap(e, p, DestructionPunch, 0);
                var celtic = Attacker(e, opp, Celtic, 0);
                e.NotifyPublic();
                e.TryAttack(opp, celtic, bewd);
                var offeredAtk = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    offeredAtk |= OffersAnywhere(e, punch);
                    e.PassResponse();
                }

                DrainCombat(e);

                var e2 = OpponentBattle(db, pDeck, aDeck, out _);
                var soldier = PlaceMonster(e2, e2.Player, GiantSoldier, 0, BattlePosition.Defense, true);
                var punch2 = PlaceSetTrap(e2, e2.Player, DestructionPunch, 0);
                var skull = Attacker(e2, e2.Opponent, SummonedSkull, 0);
                e2.NotifyPublic();
                e2.TryAttack(e2.Opponent, skull, soldier);
                var offeredHigh = false;
                for (var i = 0; i < 6 && e2.IsAwaitingResponse; i++)
                {
                    offeredHigh |= OffersAnywhere(e2, punch2);
                    e2.PassResponse();
                }

                DrainCombat(e2);
                Check("Destruction Punch: not offered for an Attack Position monster or a DEF not above the ATK",
                    !offeredAtk && !offeredHigh && opp.Graveyard.Contains(celtic) &&
                    e2.Player.Graveyard.Contains(soldier) && !e.IsAwaitingResponse && !e2.IsAwaitingResponse,
                    $"atk={offeredAtk} high={offeredHigh}");
                Check("Destruction Punch: not activatable outside its window",
                    !e.CanActivateSpellTrap(p, punch, fromHand: false));
            }

            {
                // The AI's Destruction Punch: the Player's Celtic Guardian attacks its Mystical Elf; the Player's own Set
                // Destruction Punch is not offered (its monster is the attacker).
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = Attacker(e, p, Celtic, 0);
                var ownPunch = PlaceSetTrap(e, p, DestructionPunch, 0);
                PlaceMonster(e, opp, MysticalElf, 0, BattlePosition.Defense, true);
                var punch = PlaceSetTrap(e, opp, DestructionPunch, 0);
                e.NotifyPublic();
                var elf = opp.MonsterZones[0].Occupant;
                var pLp = p.LifePoints;
                var declared = e.TryAttack(p, celtic, elf);
                var ownOffered = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    ownOffered |= OffersAnywhere(e, ownPunch);
                    e.PassResponse();
                }

                DrainCombat(e);
                Check("Destruction Punch: the AI destroys the Player's attacker; the attacker's own copy is not offered",
                    reached && declared && !ownOffered && p.Graveyard.Contains(celtic) && opp.Graveyard.Contains(punch) &&
                    p.LifePoints == pLp - 600, $"declared={declared} own={ownOffered} celticGy={p.Graveyard.Contains(celtic)} " +
                                               $"punchGy={opp.Graveyard.Contains(punch)} pLp={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────── Michizure ─────────────────────────

        static void MichizureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Battle: the opponent's Summoned Skull destroys Celtic Guardian; Michizure destroys the Skull.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                var servant = PlaceMonster(e, opp, SkullServant, 1, BattlePosition.Defense, false);
                e.NotifyPublic();
                var declared = e.TryAttack(opp, skull, celtic);
                for (var i = 0; i < 4 && e.IsAwaitingResponse && !OffersAnywhere(e, michi); i++)
                    e.PassResponse();
                var offered = Offers(e, ResponseTiming.EndOfDamageStep, p, michi) && p.Graveyard.Contains(celtic);
                var act = offered && e.TryActivateSpellTrap(p, michi, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets;
                var anyMonster = legal != null && legal.Contains(skull) && legal.Contains(servant);
                var picked = act && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(skull);
                PassAll(e);
                DrainCombat(e);
                Check("Michizure: offered at the end of the Damage Step when your monster was destroyed by battle",
                    reached && declared && offered && act && anyMonster && picked,
                    $"declared={declared} offered={offered} act={act} any={anyMonster} {Window(e)}");
                Check("Michizure: destroys the targeted monster",
                    opp.Graveyard.Contains(skull) && p.Graveyard.Contains(michi) && !e.IsAwaitingResponse &&
                    e.Chain.Count == 0 && !e.HasDeclaredAttack, $"skullGy={opp.Graveyard.Contains(skull)} {Window(e)}");
            }

            {
                // Tribute: the Player Tribute Summons Summoned Skull with Celtic Guardian; Michizure is offered once play is
                // idle and destroys the opponent's face-down monster.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var setOpp = PlaceMonster(e, opp, GiantSoldier, 0, BattlePosition.Defense, false);
                var skull = PutInHand(e, p, SummonedSkull);
                e.NotifyPublic();
                var summoned = e.TryNormalSummon(p, skull, asSet: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse && !OffersAnywhere(e, michi); i++)
                    e.PassResponse();
                var offered = Offers(e, ResponseTiming.MonsterSentFromFieldToYourGy, p, michi) ||
                              Offers(e, ResponseTiming.ChainResponse, p, michi);
                var act = offered && e.TryActivateSpellTrap(p, michi, fromHand: false);
                if (act && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(setOpp);
                PassAll(e);
                Check("Michizure: a Tributed monster opens its window; it destroys a face-down monster",
                    reached && summoned && p.Graveyard.Contains(celtic) && offered && act &&
                    opp.Graveyard.Contains(setOpp) && p.TryFindMonster(skull, out _) && p.Graveyard.Contains(michi),
                    $"summoned={summoned} offered={offered} act={act} setGy={opp.Graveyard.Contains(setOpp)} {Window(e)}");
            }

            {
                // Effect: the opponent's Fissure destroys Celtic Guardian; Michizure is offered after the chain (not while
                // Fissure is on the chain).
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                e.ClearPendingResponse();
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var setOpp = PlaceMonster(e, opp, GiantSoldier, 0, BattlePosition.Defense, false);
                var fissure = PutInHand(e, opp, Fissure);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(opp, fissure, fromHand: true);
                // While Fissure is on the chain nothing has been sent yet: Michizure cannot chain to it.
                var onChain = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                              OffersAnywhere(e, michi);
                for (var i = 0; i < 4 && e.IsAwaitingResponse && !OffersAnywhere(e, michi); i++)
                    e.PassResponse();
                var offered = Offers(e, ResponseTiming.MonsterSentFromFieldToYourGy, p, michi) &&
                              p.Graveyard.Contains(celtic);
                var answered = offered && e.TryActivateSpellTrap(p, michi, fromHand: false);
                if (answered && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(setOpp);
                PassAll(e);
                Check("Michizure: an opponent's destruction effect opens its window after the chain resolved",
                    reached && act && !onChain && offered && answered && opp.Graveyard.Contains(setOpp) &&
                    !e.IsAwaitingResponse, $"act={act} onChain={onChain} offered={offered} celticGy={p.Graveyard.Contains(celtic)} {Window(e)}");
            }

            {
                // Not offered: the opponent's own monster goes to the opponent's GY; nothing was sent (open state).
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var celtic = Attacker(e, opp, Celtic, 0);
                e.NotifyPublic();
                var idleBefore = e.CanActivateSpellTrap(p, michi, fromHand: false);
                e.TryAttack(opp, celtic, bewd);
                var offered = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    offered |= OffersAnywhere(e, michi);
                    e.PassResponse();
                }

                DrainCombat(e);
                Check("Michizure: not offered for the opponent's monster sent to their GY, nor in an open state",
                    !idleBefore && !offered && opp.Graveyard.Contains(celtic) && p.TryFindSpellTrap(michi, out _) &&
                    !e.CanActivateSpellTrap(p, michi, fromHand: false), $"idle={idleBefore} offered={offered}");
            }

            {
                // The AI's Michizure: the Player's Blue-Eyes destroys its Celtic Guardian; Michizure destroys Blue-Eyes.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = Attacker(e, p, Bewd, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, opp, Michizure, 0);
                e.NotifyPublic();
                var declared = e.TryAttack(p, bewd, celtic);
                PassAll(e);
                DrainCombat(e);
                Check("Michizure: the AI takes the Player's attacker with it",
                    reached && declared && opp.Graveyard.Contains(celtic) && p.Graveyard.Contains(bewd) &&
                    opp.Graveyard.Contains(michi) && !e.IsAwaitingResponse,
                    $"declared={declared} bewdGy={p.Graveyard.Contains(bewd)} michiGy={opp.Graveyard.Contains(michi)} {Window(e)}");
            }

            {
                // Cost send onto your own activation (review finding): Share the Pain Tributes Celtic Guardian as its
                // cost; the opponent has nothing to chain, yet the Player may chain Michizure as Chain Link 2
                // (Lua EVENT_TO_GRAVE). Michizure resolves first, then the opponent Tributes its last monster.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var soldier = PlaceMonster(e, opp, GiantSoldier, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, opp, MysticalElf, 1, BattlePosition.Attack, true);
                var share = PutInHand(e, p, ShareThePain);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, share, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                var offered = Offers(e, ResponseTiming.ChainResponse, p, michi) && e.Chain.Count == 1 &&
                              p.Graveyard.Contains(celtic);
                var trace = $"pr={e.PendingResponse?.Timing}/{e.PendingResponse?.Responder?.Name} chain={e.Chain.Count} " +
                            $"celticGy={p.Graveyard.Contains(celtic)}";
                var act = offered && e.TryActivateSpellTrap(p, michi, fromHand: false);
                if (act && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(soldier);
                PassAll(e);
                Check("Michizure: chains to your own activation whose Tribute cost sent your monster",
                    reached && used && offered && act && opp.Graveyard.Contains(soldier) &&
                    opp.Graveyard.Contains(elf) && p.Graveyard.Contains(michi) && p.Graveyard.Contains(share) &&
                    !e.IsAwaitingResponse,
                    $"used={used} offered={offered} act={act} {trace} soldierGy={opp.Graveyard.Contains(soldier)} " +
                    $"elfGy={opp.Graveyard.Contains(elf)} chain={e.Chain.Count} {Window(e)}");
            }

            {
                // A monster ignition with a cost (Exiled Force) resolves inline in this engine, with no Chain Link and no
                // response window for either duelist: Michizure cannot chain to it, and its cost send is not answered
                // late either (not after the effect resolved, not at a later idle moment).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var force = PlaceMonster(e, p, ExiledForce, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, opp, GiantSoldier, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, force, fromHand: false);
                var offered = OffersAnywhere(e, michi);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                offered |= OffersAnywhere(e, michi);
                PassAll(e);
                e.NotifyPublic();
                offered |= OffersAnywhere(e, michi);
                Check("Michizure: not offered late after an inline monster ignition whose cost sent your monster",
                    reached && used && !offered && p.Graveyard.Contains(force) && opp.Graveyard.Contains(celtic) &&
                    p.TryFindSpellTrap(michi, out _),
                    $"used={used} offered={offered} celticGy={opp.Graveyard.Contains(celtic)} chain={e.Chain.Count} {Window(e)}");
            }

            {
                // The same inline ignition's effect destroys the AI's Celtic Guardian: the AI's Michizure window opens as
                // soon as that effect has resolved (not at the next idle moment) and it takes Blue-Eyes with it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var force = PlaceMonster(e, p, ExiledForce, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var michi = PlaceSetTrap(e, opp, Michizure, 0);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, force, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                var michiUsed = opp.Graveyard.Contains(michi);
                var bewdGone = p.Graveyard.Contains(bewd);
                Check("Michizure: the AI answers an inline monster ignition's destruction as soon as it resolved",
                    reached && used && opp.Graveyard.Contains(celtic) && michiUsed && bewdGone && !e.IsAwaitingResponse,
                    $"used={used} celticGy={opp.Graveyard.Contains(celtic)} michiGy={michiUsed} bewdGy={bewdGone} {Window(e)}");
            }

            {
                // Nothing sent: a plain activation with Michizure Set resolves at once (no extra window for the activator).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var michi = PlaceSetTrap(e, p, Michizure, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var fissure = PutInHand(e, p, Fissure);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                var paused = e.IsAwaitingResponse;
                PassAll(e);
                Check("Michizure: no extra chain window on your own activation when nothing was sent",
                    reached && used && !paused && opp.Graveyard.Contains(celtic) && p.TryFindSpellTrap(michi, out _),
                    $"used={used} paused={paused} celticGy={opp.Graveyard.Contains(celtic)} {Window(e)}");
            }
        }

        // ───────────────────────── Dreamsprite ─────────────────────────

        static void DreamspriteDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Celtic Guardian attacks Dreamsprite; the lone other monster (a Set Giant Soldier) battles
                // instead: flipped face-up, DEF 2000, 600 to the opponent.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var sprite = PlaceMonster(e, p, Dreamsprite, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, false);
                var celtic = Attacker(e, opp, Celtic, 0);
                e.NotifyPublic();
                var oppLp = opp.LifePoints;
                var pLp = p.LifePoints;
                var declared = e.TryAttack(opp, celtic, sprite);
                DrainCombat(e);
                Check("Dreamsprite: the attacker battles the other (face-down) monster instead",
                    reached && declared && p.TryFindMonster(sprite, out _) && p.TryFindMonster(soldier, out _) &&
                    soldier.FaceUp && opp.TryFindMonster(celtic, out _) && opp.LifePoints == oppLp - 600 &&
                    p.LifePoints == pLp && !e.HasDeclaredAttack && e.Phase == DuelPhase.Battle,
                    $"declared={declared} sprite={p.TryFindMonster(sprite, out _)} soldierUp={soldier.FaceUp} " +
                    $"oppLp={oppLp}->{opp.LifePoints} pLp={pLp}->{p.LifePoints} {Window(e)}");
            }

            {
                // Two other monsters: the Player picks Blue-Eyes; Celtic Guardian is destroyed.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var sprite = PlaceMonster(e, p, Dreamsprite, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var servant = PlaceMonster(e, p, SkullServant, 2, BattlePosition.Attack, true);
                var celtic = Attacker(e, opp, Celtic, 0);
                e.NotifyPublic();
                var oppLp = opp.LifePoints;
                e.TryAttack(opp, celtic, sprite);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                if (!e.IsAwaitingEffectTarget && e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                var legal = e.PendingActivation?.LegalTargets;
                var asked = legal != null && legal.Count == 2 && legal.Contains(bewd) && legal.Contains(servant) &&
                            !legal.Contains(sprite);
                var picked = asked && e.TrySelectEffectTarget(bewd);
                DrainCombat(e);
                Check("Dreamsprite: the player picks among the other monsters; the attacker battles the pick",
                    asked && picked && opp.Graveyard.Contains(celtic) && p.TryFindMonster(sprite, out _) &&
                    opp.LifePoints == oppLp - 1600, $"asked={asked} picked={picked} oppLp={oppLp}->{opp.LifePoints}");
            }

            {
                // No other monster: an ordinary battle — Dreamsprite is destroyed.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var sprite = PlaceMonster(e, p, Dreamsprite, 0, BattlePosition.Attack, true);
                var celtic = Attacker(e, opp, Celtic, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.TryAttack(opp, celtic, sprite);
                DrainCombat(e);
                Check("Dreamsprite: with no other monster the battle is unchanged",
                    p.Graveyard.Contains(sprite) && p.LifePoints == pLp - 1100, $"pLp={pLp}->{p.LifePoints}");
            }

            {
                // The AI's Dreamsprite: the Player's Celtic Guardian attacks it; the AI picks Blue-Eyes.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = Attacker(e, p, Celtic, 0);
                var sprite = PlaceMonster(e, opp, Dreamsprite, 0, BattlePosition.Attack, true);
                PlaceMonster(e, opp, Bewd, 1, BattlePosition.Attack, true);
                PlaceMonster(e, opp, SkullServant, 2, BattlePosition.Attack, true);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var declared = e.TryAttack(p, celtic, sprite);
                DrainCombat(e);
                Check("Dreamsprite: the AI moves the battle to a monster that wins it",
                    reached && declared && p.Graveyard.Contains(celtic) && opp.TryFindMonster(sprite, out _) &&
                    p.LifePoints == pLp - 1600, $"declared={declared} pLp={pLp}->{p.LifePoints} {Window(e)}");
            }
        }

        // ───────────────────────── Dark Spirit of the Silent ─────────────────────────

        static void DarkSpiritDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Summoned Skull attacks Celtic Guardian; Dark Spirit negates it and the opponent's Skull
                // Servant (face-up Defense) is changed to Attack Position and battles Celtic Guardian instead.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var spirit = PlaceSetTrap(e, p, DarkSpirit, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                var servant = PlaceMonster(e, opp, SkullServant, 1, BattlePosition.Defense, true);
                PlaceMonster(e, opp, GiantSoldier, 2, BattlePosition.Defense, false); // face-down: not a legal pick
                e.NotifyPublic();
                var oppLp = opp.LifePoints;
                var pLp = p.LifePoints;
                var declared = e.TryAttack(opp, skull, celtic);
                var offered = Offers(e, ResponseTiming.AttackDeclared, p, spirit);
                var act = offered && e.TryActivateSpellTrap(p, spirit, fromHand: false);
                var onlyServant = !e.IsAwaitingEffectTarget ||
                                  (e.PendingActivation.LegalTargets.Count == 1 &&
                                   e.PendingActivation.LegalTargets.Contains(servant));
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(servant);
                PassAll(e);
                DrainCombat(e);
                Check("Dark Spirit of the Silent: offered on the opponent's attack; picks another face-up monster only",
                    reached && declared && offered && act && onlyServant,
                    $"declared={declared} offered={offered} act={act} only={onlyServant} {Window(e)}");
                Check("Dark Spirit of the Silent: the attack is negated; Skull Servant (now Attack) battles Celtic Guardian",
                    opp.Graveyard.Contains(servant) && p.TryFindMonster(celtic, out _) && opp.TryFindMonster(skull, out _) &&
                    opp.LifePoints == oppLp - 1100 && p.LifePoints == pLp && p.Graveyard.Contains(spirit) &&
                    !e.HasDeclaredAttack && e.Phase == DuelPhase.Battle,
                    $"servantGy={opp.Graveyard.Contains(servant)} oppLp={oppLp}->{opp.LifePoints} pLp={pLp}->{p.LifePoints} " +
                    $"{Window(e)}");
            }

            {
                // A direct attack: the other monster attacks directly instead (300 instead of 2500).
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var spirit = PlaceSetTrap(e, p, DarkSpirit, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                var servant = PlaceMonster(e, opp, SkullServant, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                e.TryAttack(opp, skull, null);
                var offered = Offers(e, ResponseTiming.AttackDeclared, p, spirit);
                var act = offered && e.TryActivateSpellTrap(p, spirit, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(servant);
                PassAll(e);
                DrainCombat(e);
                Check("Dark Spirit of the Silent: against a direct attack the other monster attacks directly instead",
                    offered && act && p.LifePoints == pLp - 300 && opp.TryFindMonster(servant, out _),
                    $"offered={offered} act={act} pLp={pLp}->{p.LifePoints} {Window(e)}");
            }

            {
                // Not offered without another face-up monster of the opponent (the only other one is Set).
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var spirit = PlaceSetTrap(e, p, DarkSpirit, 0);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                PlaceMonster(e, opp, SkullServant, 1, BattlePosition.Defense, false);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                var offered = OffersAnywhere(e, spirit);
                PassAll(e);
                DrainCombat(e);
                Check("Dark Spirit of the Silent: not offered without another face-up monster to battle",
                    !offered && p.Graveyard.Contains(celtic), $"offered={offered}");
            }

            {
                // The AI's Dark Spirit: the Player's Blue-Eyes attacks Celtic Guardian; the AI negates it and the Player's
                // Skull Servant battles Celtic Guardian instead.
                var e = PlayerBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = Attacker(e, p, Bewd, 0);
                var servant = PlaceMonster(e, p, SkullServant, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var spirit = PlaceSetTrap(e, opp, DarkSpirit, 0);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var declared = e.TryAttack(p, bewd, celtic);
                PassAll(e);
                DrainCombat(e);
                Check("Dark Spirit of the Silent: the AI negates a lethal attack and the weakest monster battles instead",
                    reached && declared && opp.Graveyard.Contains(spirit) && opp.TryFindMonster(celtic, out _) &&
                    p.Graveyard.Contains(servant) && p.TryFindMonster(bewd, out _) && p.LifePoints == pLp - 1100,
                    $"declared={declared} spiritGy={opp.Graveyard.Contains(spirit)} servantGy={p.Graveyard.Contains(servant)} " +
                    $"pLp={pLp}->{p.LifePoints} {Window(e)}");
            }
        }
    }
}
