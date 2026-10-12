using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "AttackObligations": the must-attack obligation (ygopro EFFECT_MUST_ATTACK) — End Battle / End Turn are
    /// refused while an obligated monster can still attack, Main Phase 1 cannot skip the Battle Phase, and the AI meets the
    /// duty. Berserk Gorilla, Amazoness Archers, Diffusion Wave-Motion, Staunch Defender: compile shapes, near misses,
    /// mini-duels through the engine entry points, AI turns, and 250 stress duels with the cards in both Decks.
    /// </summary>
    public static class AttackObligationsRegressionTests
    {
        const int Gorilla = 39168895;       // Beast EARTH 2000/1000
        const int Archers = 67987611;
        const int Diffusion = 87880531;
        const int Staunch = 92854392;
        const int Paladin = 47480070;       // Amazoness Paladin, Warrior 1700/300
        const int AmazonessFighter = 55821894;
        const int DarkMagician = 46986414;  // Spellcaster Lv7 2500/2100
        const int DarkMagicianGirl = 38033121; // Spellcaster Lv6
        const int Sangan = 26202165;        // Fiend 1000/600
        const int ManEaterBug = 54652250;   // FLIP, 450/600
        const int Celtic = 91152256;        // Warrior 1400/1200
        const int SummonedSkull = 70781052; // Fiend 2500/1200
        const int MysticalElf = 15025844;   // Spellcaster 800/2000
        const int Swords = 72302403;

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("AttackObligations: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            GorillaDuels(db, pDeck, aDeck, Check);
            ArchersDuels(db, pDeck, aDeck, Check);
            DiffusionDuels(db, pDeck, aDeck, Check);
            StaunchDuels(db, pDeck, aDeck, Check);
            AiTurns(db, pDeck, aDeck, Check);
            AiDuels(db, Check);
        }

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [" + string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} zone={c.Zone} n={c.Amount} race={c.RaceFilter} lp={c.PayLpAmount} " +
                    $"cond={c.ActivationCondition}/{c.ConditionName} tgt={c.IsPsctTarget}")) + "] left=[" +
                  string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>()) + "]";

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var gorilla = CardTextEffectCompiler.Compile(db.Get(Gorilla));
            Check("Berserk Gorilla: two continuous rules (face-up Defense self-destruction, must attack if able); Implemented",
                gorilla != null && gorilla.FullyCompiled && gorilla.ClauseList.Count == 2 &&
                Has(gorilla, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.SelfDestroyWhileFaceUpDefense && !c.MakesChainLink) &&
                Has(gorilla, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.MustAttackIfAble && !c.MakesChainLink) &&
                CardEffectStatus.Classify(db.Get(Gorilla)) == CardEffectStatusKind.Implemented,
                Describe(gorilla));

            var archers = CardTextEffectCompiler.Compile(db.Get(Archers));
            Check("Amazoness Archers: attack-declaration Trap, face-up \"Amazoness\" condition, -500 ATK, no target; Implemented",
                archers != null && archers.FullyCompiled && archers.ClauseList.Count == 1 &&
                Has(archers, c => c.Timing == EffectTiming.AttackDeclared &&
                                  c.Action == EffectActionKind.OpponentMonstersToFaceUpAttackLoseAtkMustAttack &&
                                  c.Amount == 500 && c.SuppressFlipEffects && !c.RequiresTargetChoice &&
                                  c.ActivationCondition == AttackObligationsRuntime.YouControlFaceUpSeriesMonster &&
                                  c.ConditionName == "Amazoness") &&
                CardEffectStatus.Classify(db.Get(Archers)) == CardEffectStatusKind.Implemented,
                Describe(archers));

            var diffusion = CardTextEffectCompiler.Compile(db.Get(Diffusion));
            Check("Diffusion Wave-Motion: pay 1000 LP, target 1 face-up Level 7+ Spellcaster you control; Implemented",
                diffusion != null && diffusion.FullyCompiled && diffusion.ClauseList.Count == 1 &&
                Has(diffusion, c => c.Timing == EffectTiming.Activate &&
                                    c.Action == EffectActionKind.TargetMustAttackAllOthersCannotAttack &&
                                    c.Zone == EffectZoneFilter.ControllerAnyMonsters && c.IsPsctTarget &&
                                    c.RequiresTargetFaceUp && c.PayLpAmount == 1000 && c.Amount == 7 &&
                                    c.RaceFilter == "Spellcaster" &&
                                    c.ActivationCondition == AttackObligationsRuntime.OpponentControlsMonsterBeforeBattlePhase) &&
                CardEffectStatus.Classify(db.Get(Diffusion)) == CardEffectStatusKind.Implemented,
                Describe(diffusion));

            var staunch = CardTextEffectCompiler.Compile(db.Get(Staunch));
            Check("Staunch Defender: attack-declaration Trap targeting 1 face-up monster you control; Implemented",
                staunch != null && staunch.FullyCompiled && staunch.ClauseList.Count == 1 &&
                Has(staunch, c => c.Timing == EffectTiming.AttackDeclared &&
                                  c.Action == EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt &&
                                  c.Zone == EffectZoneFilter.ControllerAnyMonsters && c.IsPsctTarget &&
                                  c.RequiresTargetFaceUp) &&
                CardEffectStatus.Classify(db.Get(Staunch)) == CardEffectStatusKind.Implemented,
                Describe(staunch));
        }

        // ───────────────────────── near misses ─────────────────────────

        static void NearMisses(Chk Check)
        {
            CompiledCardProgram Syn(int id, string type, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = "Near miss " + id, type = type,
                    frameType = type.Contains("Monster") ? "effect" : type.StartsWith("Trap") ? "trap" : "spell",
                    race = type.Contains("Monster") ? "Beast" : "Normal", attribute = type.Contains("Monster") ? "EARTH" : null,
                    level = type.Contains("Monster") ? 4 : 0, desc = desc
                });

            bool Clean(CompiledCardProgram p, EffectActionKind action) =>
                p == null || !p.FullyCompiled || !Has(p, c => c.Action == action);

            var mustOnly = Syn(91780001, "Effect Monster", "This card must attack if able.");
            Check("Near miss: \"must attack if able\" alone (no self-destruction sentence) is not compiled by the Gorilla atom",
                Clean(mustOnly, EffectActionKind.MustAttackIfAble), Describe(mustOnly));
            var defOnly = Syn(91780002, "Effect Monster", "If this card is in face-up Defense Position, destroy this card.");
            Check("Near miss: the self-destruction sentence alone is not compiled by the Gorilla atom",
                Clean(defOnly, EffectActionKind.SelfDestroyWhileFaceUpDefense), Describe(defOnly));
            var archersDef = Syn(91780003, "Trap Card",
                "When an opponent's monster declares an attack, while you control an \"Amazoness\" monster: Change all " +
                "monsters your opponent currently controls to face-up Defense Position (Flip Effects are not activated), " +
                "they lose 500 ATK, also they must attack this turn, if able.");
            Check("Near miss: an Archers-like Trap that changes to Defense Position is not compiled",
                Clean(archersDef, EffectActionKind.OpponentMonstersToFaceUpAttackLoseAtkMustAttack), Describe(archersDef));
            var diffusionCan = Syn(91780004, "Spell Card",
                "If your opponent controls a monster: Pay 1000 LP, then target 1 Level 7 or higher Spellcaster monster " +
                "you control; this turn, it can attack all monsters your opponent controls once each, also other monsters " +
                "you control cannot attack. The effects of monsters destroyed by these attacks cannot activate and are negated.");
            Check("Near miss: \"can attack all\" (no obligation) is not the Diffusion atom",
                Clean(diffusionCan, EffectActionKind.TargetMustAttackAllOthersCannotAttack), Describe(diffusionCan));
            var staunchShort = Syn(91780005, "Trap Card",
                "You can only activate this card when your opponent declares an attack. Select 1 face-up monster on your " +
                "side of the field. During this turn, your opponent can only designate the selected monster as an attack " +
                "target.");
            Check("Near miss: a Staunch-like Trap without the must-attack part is not compiled",
                Clean(staunchShort, EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt), Describe(staunchShort));
        }

        // ───────────────────────── helpers ─────────────────────────

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            e.Player.Hand.Clear();
            e.Opponent.Hand.Clear();
        }

        /// <summary>The Player's Main Phase 1 (turn 3+) with an empty board and empty hands.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            Empty(e);
            return e;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e) && e.Phase == DuelPhase.Main1;
            Empty(e);
            return e;
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(e);
            Empty(e);
            return e;
        }

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone, BattlePosition pos = BattlePosition.Attack)
        {
            var m = PlaceMonster(e, who, id, zone, pos, true);
            m.ClearAttackFlags();
            return m;
        }

        static CardInstance FaceUpSpell(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static string State(DuelEngine e) =>
            $"turn={e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase} step={e.BattleStep} resp={e.IsAwaitingResponse} " +
            $"tgt={e.IsAwaitingEffectTarget} atk={e.HasDeclaredAttack} pLp={e.Player.LifePoints} oLp={e.Opponent.LifePoints}";

        // ───────────────────────── Berserk Gorilla ─────────────────────────

        static void GorillaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Main Phase 1: the turn cannot end and Main Phase 2 cannot skip the Battle Phase; in the Battle Phase,
                // End Battle / End Turn are refused until it attacks; then the Battle Phase may end.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var g = Up(e, p, Gorilla, 0);
                var celtic = Up(e, o, Celtic, 0);
                e.NotifyPublic();
                var noEnd = !e.TryEndTurnSafe(p) && e.TurnPlayer == p && e.Phase == DuelPhase.Main1;
                var noSkip = !e.TryEnterMainPhase2(p) && e.Phase == DuelPhase.Main1;
                var cmd = DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.EndTurn));
                Check("Berserk Gorilla: Main Phase 1 cannot end the turn or skip the Battle Phase (End Turn command refused)",
                    reached && noEnd && noSkip && !cmd.Ok && (cmd.Message ?? "").Contains("must attack"),
                    $"noEnd={noEnd} noSkip={noSkip} cmd={cmd.Ok}/{cmd.Message} {State(e)}");

                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var held = !e.TryEnterMainPhase2(p) && !e.TryEndTurnSafe(p) && e.Phase == DuelPhase.Battle;
                var m2Cmd = DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.EnterMainPhase2));
                Check("Berserk Gorilla: the Battle Phase cannot end before it attacks (Main Phase 2 / End Turn refused)",
                    bp && held && !m2Cmd.Ok && (m2Cmd.Message ?? "").Contains("must attack"),
                    $"bp={bp} held={held} m2={m2Cmd.Ok}/{m2Cmd.Message} {State(e)}");

                var attacked = e.TryAttack(p, g, celtic);
                DrainCombat(e);
                var left = e.TryEnterMainPhase2(p) && e.Phase == DuelPhase.Main2;
                Check("Berserk Gorilla: once it attacked, the Battle Phase ends normally",
                    attacked && o.Graveyard.Contains(celtic) && left, $"attacked={attacked} left={left} {State(e)}");
            }

            {
                // No legal attack (Swords of Revealing Light): no duty, the turn ends — no soft-lock.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                Up(e, p, Gorilla, 0);
                Up(e, o, Celtic, 0);
                FaceUpSpell(e, o, Swords, 0);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var m2 = e.TryEnterMainPhase2(p);
                var end = e.TryEndTurnSafe(p);
                Check("Berserk Gorilla: when it cannot attack (Swords of Revealing Light), the Battle Phase and turn end",
                    reached && bp && m2 && end && e.TurnPlayer == o, $"bp={bp} m2={m2} end={end} {State(e)}");
            }

            {
                // No monster to attack: it must attack directly before the Battle Phase ends.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var g = Up(e, p, Gorilla, 0);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var held = !e.TryEnterMainPhase2(p);
                var lp = o.LifePoints;
                var attacked = e.TryAttack(p, g, null);
                DrainCombat(e);
                var m2 = e.TryEnterMainPhase2(p);
                Check("Berserk Gorilla: with an empty opposing field the direct attack is the duty",
                    reached && bp && held && attacked && o.LifePoints == lp - 2000 && m2,
                    $"held={held} attacked={attacked} lp={lp}->{o.LifePoints} m2={m2} {State(e)}");
            }

            {
                // Set face-down: its effects do not apply — the turn ends from Main Phase 1.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Gorilla, 0, BattlePosition.Defense, false);
                Up(e, o, Celtic, 0);
                e.NotifyPublic();
                var end = e.TryEndTurnSafe(p);
                Check("Berserk Gorilla: Set face-down, no duty (the turn ends from Main Phase 1)",
                    reached && end && e.TurnPlayer == o, $"end={end} {State(e)}");
            }

            {
                // Changed to face-up Defense Position: destroyed by its own effect; a face-down Defense one stays.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var g = Up(e, p, Gorilla, 0);
                var set = PlaceMonster(e, p, Gorilla, 1, BattlePosition.Defense, false);
                e.NotifyPublic();
                var changed = e.TryChangePosition(p, g);
                FieldSpellEffects.RefreshBoard(e);
                Check("Berserk Gorilla: in face-up Defense Position it is destroyed; a Set copy is not",
                    reached && changed && p.Graveyard.Contains(g) && p.TryFindMonster(set, out _),
                    $"changed={changed} gy={p.Graveyard.Contains(g)} set={p.TryFindMonster(set, out _)} pos={g.Position}");
            }
        }

        // ───────────────────────── Amazoness Archers ─────────────────────────

        static void ArchersDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var paladin = Up(e, p, Paladin, 0);
                var archers = PlaceSetTrap(e, p, Archers, 0);
                var celtic = Up(e, o, Celtic, 0);
                var skull = PlaceMonster(e, o, SummonedSkull, 1, BattlePosition.Defense, true);
                skull.ClearAttackFlags();
                var bug = PlaceMonster(e, o, ManEaterBug, 2, BattlePosition.Defense, false);
                bug.ClearAttackFlags();
                e.NotifyPublic();
                var declared = e.TryAttack(o, celtic, paladin);
                var offered = e.PendingResponse != null && e.PendingResponse.Responder == p &&
                              e.PendingResponse.LegalCards.Contains(archers);
                var activated = offered && e.TryActivateSpellTrap(p, archers, fromHand: false);
                PassAll(e);
                DrainCombat(e);
                Check("Amazoness Archers: every opposing monster is face-up Attack Position (no FLIP), each loses 500 ATK",
                    reached && declared && activated && skull.FaceUp && skull.Position == BattlePosition.Attack &&
                    bug.FaceUp && bug.Position == BattlePosition.Attack && skull.CurrentAtk == 2000 &&
                    p.TryFindMonster(paladin, out _) && e.PendingActivation == null && o.Graveyard.Contains(celtic),
                    $"offered={offered} activated={activated} skull={skull.Position}/{skull.CurrentAtk} " +
                    $"bug={bug.FaceUp}/{bug.Position} paladin={p.TryFindMonster(paladin, out _)} {State(e)}");

                var held = !e.TryEnterMainPhase2(o) && !e.TryEndTurnSafe(o) && e.Phase == DuelPhase.Battle;
                Check("Amazoness Archers: the opponent cannot end the Battle Phase while those monsters can attack",
                    held, State(e));

                var skullAttack = e.TryAttack(o, skull, paladin);
                DrainCombat(e);
                var stillHeld = !e.TryEnterMainPhase2(o);
                var bugAttack = e.TryAttack(o, bug, null);
                DrainCombat(e);
                var left = e.TryEnterMainPhase2(o);
                Check("Amazoness Archers: once each obligated monster attacked, the Battle Phase ends; the ATK loss stays",
                    skullAttack && stillHeld && bugAttack && left && skull.CurrentAtk == 2000,
                    $"skull={skullAttack} stillHeld={stillHeld} bug={bugAttack} left={left} atk={skull.CurrentAtk} {State(e)}");
            }

            {
                // Without a face-up "Amazoness" monster the Trap is not offered.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var paladin = PlaceMonster(e, p, Paladin, 0, BattlePosition.Defense, false);
                var archers = PlaceSetTrap(e, p, Archers, 0);
                var celtic = Up(e, o, Celtic, 0);
                e.NotifyPublic();
                var declared = e.TryAttack(o, celtic, paladin);
                var offered = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(archers);
                PassAll(e);
                DrainCombat(e);
                Check("Amazoness Archers: not offered without a face-up \"Amazoness\" monster",
                    reached && declared && !offered, $"reached={reached} declared={declared} offered={offered} {State(e)}");
            }
        }

        // ───────────────────────── Diffusion Wave-Motion ─────────────────────────

        static void DiffusionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var dm = Up(e, p, DarkMagician, 0);
                var celtic = Up(e, p, Celtic, 1);
                var sangan = Up(e, o, Sangan, 0);
                var elf = Up(e, o, MysticalElf, 1, BattlePosition.Defense);
                var card = PutInHand(e, p, Diffusion);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var can = e.CanActivateSpellTrap(p, card, true);
                var act = can && e.TryActivateSpellTrap(p, card, true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(dm);
                PassAll(e);
                Check("Diffusion Wave-Motion: activates (1000 LP) on the Level 7 Spellcaster",
                    reached && act && p.LifePoints == lp - 1000 && p.Graveyard.Contains(card),
                    $"can={can} act={act} lp={lp}->{p.LifePoints} {State(e)}");

                var noEnd = !e.TryEndTurnSafe(p) && !e.TryEnterMainPhase2(p) && e.Phase == DuelPhase.Main1;
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                Check("Diffusion Wave-Motion: Main Phase 1 must go to the Battle Phase; the other monsters cannot attack",
                    noEnd && bp && !e.CanAttack(p, celtic) && e.CanAttack(p, dm),
                    $"noEnd={noEnd} bp={bp} celtic={e.CanAttack(p, celtic)} {State(e)}");

                var oHand = o.Hand.Count;
                var first = e.TryAttack(p, dm, sangan);
                DrainCombat(e);
                PassAll(e);
                Check("Diffusion Wave-Motion: a monster it destroys by battle is negated (Sangan does not search)",
                    first && o.Graveyard.Contains(sangan) && sangan.EffectsNegatedAfterBattle && o.Hand.Count == oHand &&
                    e.PendingActivation == null,
                    $"first={first} gy={o.Graveyard.Contains(sangan)} neg={sangan.EffectsNegatedAfterBattle} " +
                    $"hand={oHand}->{o.Hand.Count} {State(e)}");

                var held = !e.TryEnterMainPhase2(p) && e.Phase == DuelPhase.Battle;
                var second = e.TryAttack(p, dm, elf);
                DrainCombat(e);
                var noDirect = !e.CanAttackDirectly(p, dm);
                var left = e.TryEnterMainPhase2(p);
                Check("Diffusion Wave-Motion: it must attack each opposing monster once; then no direct attack, the phase ends",
                    held && second && o.Graveyard.Contains(elf) && dm.AttacksDeclaredThisTurn == 2 && noDirect && left,
                    $"held={held} second={second} attacks={dm.AttacksDeclaredThisTurn} noDirect={noDirect} left={left} {State(e)}");
            }

            {
                // A Level 6 Spellcaster is not a legal target; an empty opposing field forbids the activation.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                Up(e, p, DarkMagicianGirl, 0);
                Up(e, o, Celtic, 0);
                var card = PutInHand(e, p, Diffusion);
                e.NotifyPublic();
                var lv6 = e.CanActivateSpellTrap(p, card, true);
                var e2 = PlayerMain(db, pDeck, aDeck, out var reached2);
                Up(e2, e2.Player, DarkMagician, 0);
                var card2 = PutInHand(e2, e2.Player, Diffusion);
                e2.NotifyPublic();
                var noOpp = e2.CanActivateSpellTrap(e2.Player, card2, true);
                Check("Diffusion Wave-Motion: not with only a Level 6 Spellcaster, nor while the opponent controls no monster",
                    reached && reached2 && !lv6 && !noOpp, $"lv6={lv6} noOpp={noOpp}");
            }
        }

        // ───────────────────────── Staunch Defender ─────────────────────────

        static void StaunchDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pCeltic = Up(e, p, Celtic, 0);
                var elf = Up(e, p, MysticalElf, 1, BattlePosition.Defense);
                var bug = PlaceMonster(e, p, ManEaterBug, 2, BattlePosition.Defense, false);
                var staunch = PlaceSetTrap(e, p, Staunch, 0);
                var oCeltic = Up(e, o, Celtic, 0);
                var skull = Up(e, o, SummonedSkull, 1);
                e.NotifyPublic();
                var declared = e.TryAttack(o, oCeltic, pCeltic);
                var offered = e.PendingResponse != null && e.PendingResponse.Responder == p &&
                              e.PendingResponse.LegalCards.Contains(staunch);
                var activated = offered && e.TryActivateSpellTrap(p, staunch, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(elf);
                PassAll(e);
                DrainCombat(e);
                Check("Staunch Defender: activates on the attack declaration; the declared attack goes on",
                    reached && declared && activated && p.Graveyard.Contains(pCeltic) && o.Graveyard.Contains(oCeltic),
                    $"offered={offered} activated={activated} {State(e)}");

                var held = !e.TryEnterMainPhase2(o) && !e.TryEndTurnSafe(o);
                var other = e.TryAttack(o, skull, bug);
                var direct = e.TryAttack(o, skull, null);
                Check("Staunch Defender: the opponent must attack, and only the selected monster",
                    held && !other && !direct && bug.FaceUp == false && e.Phase == DuelPhase.Battle,
                    $"held={held} other={other} direct={direct} {State(e)}");

                var onElf = e.TryAttack(o, skull, elf);
                DrainCombat(e);
                var left = e.TryEnterMainPhase2(o);
                Check("Staunch Defender: after attacking the selected monster the Battle Phase ends",
                    onElf && p.Graveyard.Contains(elf) && left, $"onElf={onElf} left={left} {State(e)}");
            }

            {
                // The selected monster is destroyed: the others cannot attack anything — no duty, no soft-lock.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pCeltic = Up(e, p, Celtic, 0);
                var elf = Up(e, p, MysticalElf, 1, BattlePosition.Defense);
                var staunch = PlaceSetTrap(e, p, Staunch, 0);
                var skull = Up(e, o, SummonedSkull, 0);
                var oCeltic = Up(e, o, Celtic, 1);
                e.NotifyPublic();
                e.TryAttack(o, skull, elf);
                var activated = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(staunch) &&
                                e.TryActivateSpellTrap(p, staunch, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(elf);
                PassAll(e);
                DrainCombat(e);
                var refused = !e.TryAttack(o, oCeltic, pCeltic);
                var left = e.TryEnterMainPhase2(o);
                Check("Staunch Defender: once the selected monster is gone no attack is legal and the Battle Phase ends",
                    reached && activated && p.Graveyard.Contains(elf) && refused && left,
                    $"activated={activated} elfGy={p.Graveyard.Contains(elf)} refused={refused} left={left} {State(e)}");
            }

            {
                // Not outside the opponent's attack declaration.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Up(e, p, Celtic, 0);
                Up(e, e.Opponent, Celtic, 0);
                var staunch = PlaceSetTrap(e, p, Staunch, 0);
                e.NotifyPublic();
                Check("Staunch Defender: cannot be activated in your own Main Phase",
                    reached && !e.CanActivateSpellTrap(p, staunch, false), State(e));
            }
        }

        // ───────────────────────── AI turns ─────────────────────────

        /// <summary>
        /// Steps the AI's turn. <paramref name="answer"/> may answer a Player response window (true = handled); otherwise the
        /// Player passes.
        /// </summary>
        static bool RunAiTurn(DuelEngine e, int maxSteps, Func<DuelEngine, bool> answer = null)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player &&
                        (answer == null || !answer(e)))
                        e.PassResponse();
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

        static void AiTurns(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's Gorilla attacks a stronger monster because it must; the AI's turn then ends.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var g = Up(e, o, Gorilla, 0);
                var skull = Up(e, p, SummonedSkull, 0);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var done = RunAiTurn(e, 600);
                Check("AI: its Berserk Gorilla attacks the stronger Summoned Skull (it must); the turn ends",
                    reached && done && o.Graveyard.Contains(g) && o.LifePoints == lp - 500 && p.TryFindMonster(skull, out _) &&
                    e.TurnPlayer == p,
                    $"done={done} gy={o.Graveyard.Contains(g)} lp={lp}->{o.LifePoints} {State(e)}");
            }

            {
                // The Player answers the AI's first attack with Staunch Defender: the AI's other monster must attack the
                // selected Mystical Elf (and bounces off it); the AI's turn still ends.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var pCeltic = Up(e, p, Celtic, 0);
                var elf = Up(e, p, MysticalElf, 1, BattlePosition.Defense);
                var staunch = PlaceSetTrap(e, p, Staunch, 0);
                var skull = Up(e, o, SummonedSkull, 0);
                var oCeltic = Up(e, o, Celtic, 1);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var used = false;
                var done = RunAiTurn(e, 800, eng =>
                {
                    if (used || eng.PendingResponse.Timing != ResponseTiming.AttackDeclared ||
                        !eng.PendingResponse.LegalCards.Contains(staunch))
                        return false;
                    used = eng.TryActivateSpellTrap(p, staunch, fromHand: false);
                    if (eng.IsAwaitingEffectTarget) eng.TrySelectEffectTarget(elf);
                    return used;
                });
                Check("AI: under Staunch Defender its remaining monster attacks the selected monster; the turn ends",
                    reached && used && done && p.TryFindMonster(elf, out _) && o.LifePoints == lp - 600 &&
                    e.TurnPlayer == p,
                    $"used={used} done={done} elf={p.TryFindMonster(elf, out _)} lp={lp}->{o.LifePoints} " +
                    $"skull={o.TryFindMonster(skull, out _)} celtic={o.TryFindMonster(oCeltic, out _)} {State(e)}");
            }

            {
                // The AI defends with Amazoness Archers against the Player's attack.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var paladin = Up(e, o, Paladin, 0);
                var archers = PlaceSetTrap(e, o, Archers, 0);
                var skull = Up(e, p, SummonedSkull, 0);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Defense, true);
                celtic.ClearAttackFlags();
                e.NotifyPublic();
                e.TryEnterBattlePhase(p);
                PassAll(e);
                e.TryAttack(p, skull, paladin);
                PassAll(e);
                var used = o.Graveyard.Contains(archers);
                DrainCombat(e);
                var held = !e.TryEnterMainPhase2(p);
                Check("AI: answers an attack with Amazoness Archers; the Player's monsters must then attack",
                    reached && used && celtic.Position == BattlePosition.Attack && skull.CurrentAtk == 2000 && held,
                    $"used={used} celtic={celtic.Position} atk={skull.CurrentAtk} held={held} {State(e)}");
            }
        }

        // ───────────────────────── stress duels ─────────────────────────

        static DeckFile Deck(string name) => new()
        {
            name = name,
            format = "TCG",
            main = new[]
            {
                new DeckCardEntry { id = Gorilla, qty = 3 }, new DeckCardEntry { id = Archers, qty = 3 },
                new DeckCardEntry { id = Staunch, qty = 3 }, new DeckCardEntry { id = Diffusion, qty = 2 },
                new DeckCardEntry { id = Paladin, qty = 3 }, new DeckCardEntry { id = AmazonessFighter, qty = 3 },
                new DeckCardEntry { id = Celtic, qty = 3 }, new DeckCardEntry { id = SummonedSkull, qty = 2 },
                new DeckCardEntry { id = MysticalElf, qty = 3 }, new DeckCardEntry { id = DarkMagician, qty = 2 },
                new DeckCardEntry { id = ManEaterBug, qty = 3 }, new DeckCardEntry { id = Sangan, qty = 3 },
                new DeckCardEntry { id = Swords, qty = 1 }
            },
            extra = Array.Empty<DeckCardEntry>(),
            side = Array.Empty<DeckCardEntry>()
        };

        /// <summary>
        /// Duels between the stress agents with the four cards in both Decks (the AI side answers attacks with Amazoness
        /// Archers / Staunch Defender): every duel ends or reaches the turn cap with play moving on.
        /// </summary>
        static void AiDuels(CardDatabase db, Chk Check)
        {
            const int duels = 250;
            var stuck = 0;
            var finished = 0;
            var errors = new List<string>();
            int gorillaAttacks = 0, archersUsed = 0, staunchUsed = 0, refusals = 0;
            for (var d = 0; d < duels; d++)
            {
                var e = new DuelEngine();
                e.Rng.Reseed(17800 + d);
                e.OnLog += line =>
                {
                    if (line == null) return;
                    if (line.StartsWith("Berserk Gorilla attacks")) gorillaAttacks++;
                    if (line.Contains("must attack this turn, if able.")) archersUsed++;
                    if (line.Contains("can only attack") && line.Contains("must attack it")) staunchUsed++;
                    if (line.Contains("must attack if able —")) refusals++;
                };
                try
                {
                    e.StartDuel(db, Deck("ObligationsA"), Deck("ObligationsB"), cinematicOpening: false);
                    var last = "";
                    var same = 0;
                    for (var step = 0; step < 240 && !e.GameOver && e.TurnNumber <= 40; step++)
                    {
                        DuelStressAgent.DrainWindows(e);
                        if (e.GameOver) break;
                        DuelStressAgent.PlayTurn(e, e.TurnPlayer);
                        DuelStressAgent.DrainWindows(e);
                        var status = $"{e.TurnNumber}:{e.Phase}:{e.Player.LifePoints}:{e.Opponent.LifePoints}:" +
                                     $"{e.IsAwaitingResponse}:{e.IsAwaitingEffectTarget}";
                        same = status == last ? same + 1 : 0;
                        last = status;
                        if (same < 6) continue;
                        stuck++;
                        if (errors.Count < 3) errors.Add($"duel {d} stuck at {status}");
                        break;
                    }

                    finished++;
                }
                catch (Exception ex)
                {
                    if (errors.Count < 3) errors.Add($"duel {d}: {ex.GetType().Name} {ex.Message}");
                    stuck++;
                }
            }

            Check($"AI duels: {duels} duels with Berserk Gorilla / Amazoness Archers / Staunch Defender — no soft-lock",
                stuck == 0 && finished == duels,
                $"stuck={stuck} finished={finished} {string.Join(" | ", errors)}");
            Check("AI duels: the obligations were exercised (Gorilla attacks, Archers and Staunch Defender resolved)",
                gorillaAttacks > 0 && archersUsed > 0 && staunchUsed > 0,
                $"gorillaAttacks={gorillaAttacks} archers={archersUsed} staunch={staunchUsed} refusals={refusals}");
        }
    }
}
