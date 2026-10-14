using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SOD completion batch: the two simple weapons (Fusion Weapon / Ritual Weapon, "Equip only to a Level 6 or
    /// lower Fusion/Ritual Monster" plus the +1500 ATK/DEF gain), Rafflesia Seduction (FLIP take control of one
    /// opponent face-up monster until the end of the turn) and Hallowed Life Barrier (discard 1 card; any damage
    /// this turn becomes 0). Compile shapes through the real compiler, equip limits through
    /// <see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/>, and mini-duels through the engine entry points
    /// (TryFlipSummon, TryActivateSpellTrap, TrySelectEffectTarget, EndTurn). Runtime:
    /// EquipFamilyRuntime (equip limit) and TextEffectRuntime (TakeControlUntilEndPhase, NoDamageThisTurn).
    /// </summary>
    public static class SodCompletionRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int FusionWeapon = 27967615;      // Equip Spell: +1500 ATK/DEF to a Fusion Monster Level 6 or lower
        const int RitualWeapon = 54351224;      // Equip Spell: +1500 ATK/DEF to a Ritual Monster Level 6 or lower
        const int Rafflesia = 31440542;         // Plant Lv2 300/900, FLIP take control until the end of the turn
        const int Hallowed = 88789641;          // Normal Trap: discard 1; no damage this turn
        const int MindWipe = 52817046;          // Quick-Play: opponent adds hand to Deck, shuffles, redraws same count
        const int InfernoFireBlast = 52684508;  // Normal Spell: damage = target "Red-Eyes B. Dragon" original ATK + attack lock
        const int RedEyes = 74677422;           // Red-Eyes B. Dragon, 2400 ATK
        const int Ninjitsu = 89628781;          // Continuous Trap: target 1 face-up "Ninja" you control; cannot be destroyed by battle
        const int NinjaSasuke = 4041838;        // Ninja Grandmaster Sasuke, LIGHT Warrior Lv4 1800/1000 (matches "Ninja")

        const int MetalDragon = 9293977;        // Fusion Lv6 1850/1700 — legal Fusion Weapon target
        const int SkullKnight = 2504891;        // Fusion Lv7 2650/2250 — too high
        const int HungryBurger = 30243636;      // Ritual Lv6 2000/1850 — legal Ritual Weapon target
        const int SkullGuardian = 3627449;      // Ritual Lv7 2050/2500 — too high
        const int Celtic = 91152256;            // Normal Lv4 1400/1200 — neither Fusion nor Ritual
        const int GiantSoldier = 13039848;      // Normal Lv3 1300/2000 — a face-down body / attacker
        const int Kuriboh = 40640057;           // discard fodder
        const int PotOfGreed = 55144522;        // a Spell as discard fodder ("discard 1 card" is any card)

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("SOD completion: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            WeaponEquipLimits(db, pDeck, aDeck, Check);
            RafflesiaDuel(db, pDeck, aDeck, Check);
            HallowedDuel(db, pDeck, aDeck, Check);
            MindWipeDuel(db, pDeck, aDeck, Check);
            InfernoFireBlastDuel(db, pDeck, aDeck, Check);
            NinjitsuDuel(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            foreach (var id in new[] { FusionWeapon, RitualWeapon })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Activate)
                    .Find(c => c != null && c.Action == EffectActionKind.EquipThisToTarget);
                Check($"{def?.name ?? id.ToString()}: Implemented, +1500/+1500 equip, Level ≤ 6, {Type(id)} only",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.EquipAtkBonus == 1500 && clause.EquipDefBonus == 1500 &&
                    clause.MonsterLevelMax == 6 &&
                    clause.RequiresFusionMonsterTarget == (id == FusionWeapon) &&
                    clause.RequiresRitualMonsterTarget == (id == RitualWeapon),
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"atk={clause.EquipAtkBonus} def={clause.EquipDefBonus} lvMax={clause.MonsterLevelMax} " +
                          $"fusion={clause.RequiresFusionMonsterTarget} ritual={clause.RequiresRitualMonsterTarget}");
            }

            {
                var def = db.Get(Rafflesia);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Flip)
                    .Find(c => c != null && c.Action == EffectActionKind.TakeControlUntilEndPhase);
                Check("Rafflesia Seduction: Implemented, Flip take control of one opponent face-up monster",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                    string.IsNullOrEmpty(clause.RaceFilter) && clause.RequiresTargetChoice,
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"action={clause.Action} zone={clause.Zone} race={clause.RaceFilter ?? "-"}");
            }

            {
                var def = db.Get(Hallowed);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Activate)
                    .Find(c => c != null && c.RequiresDiscardCost);
                Check("Hallowed Life Barrier: Implemented, discard 1 card then no damage this turn",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.Action == EffectActionKind.NoDamageThisTurn &&
                    clause.DiscardCostAttribute == "*" && clause.DiscardCostCount == 1,
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"action={clause.Action} attr={clause.DiscardCostAttribute} n={clause.DiscardCostCount}");
            }
        }

        static string Type(int id) => id == FusionWeapon ? "Fusion Monster" : "Ritual Monster";

        // ───────────────────────────── equip limits ─────────────────────────────

        static void WeaponEquipLimits(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            e.Player.Hand.Clear();
            e.Opponent.Hand.Clear();
            var fusionWeapon = FaceUpSpellTrap(e, e.Player, FusionWeapon);
            var ritualWeapon = FaceUpSpellTrap(e, e.Player, RitualWeapon);
            var fusion6 = PlaceMonster(e, e.Player, MetalDragon, 0, BattlePosition.Attack, true);
            var fusion7 = PlaceMonster(e, e.Player, SkullKnight, 1, BattlePosition.Attack, true);
            var ritual6 = PlaceMonster(e, e.Player, HungryBurger, 2, BattlePosition.Attack, true);
            var ritual7 = PlaceMonster(e, e.Player, SkullGuardian, 3, BattlePosition.Attack, true);
            var plain = PlaceMonster(e, e.Player, Celtic, 4, BattlePosition.Attack, true);

            bool Allows(CardInstance eq, CardInstance m) =>
                EquipFamilyRuntime.IsCorrectEquipTarget(e, eq, m);

            Check("Fusion Weapon: a Level 6 or lower Fusion Monster is a correct target",
                Allows(fusionWeapon, fusion6),
                $"fusion6={Allows(fusionWeapon, fusion6)}");
            Check("Fusion Weapon: a Level 7 Fusion Monster, a Ritual Monster and a Normal Monster are not",
                !Allows(fusionWeapon, fusion7) && !Allows(fusionWeapon, ritual6) && !Allows(fusionWeapon, plain),
                $"fusion7={Allows(fusionWeapon, fusion7)} ritual6={Allows(fusionWeapon, ritual6)} " +
                $"plain={Allows(fusionWeapon, plain)}");

            Check("Ritual Weapon: a Level 6 or lower Ritual Monster is a correct target",
                Allows(ritualWeapon, ritual6),
                $"ritual6={Allows(ritualWeapon, ritual6)}");
            Check("Ritual Weapon: a Level 7 Ritual Monster, a Fusion Monster and a Normal Monster are not",
                !Allows(ritualWeapon, ritual7) && !Allows(ritualWeapon, fusion6) && !Allows(ritualWeapon, plain),
                $"ritual7={Allows(ritualWeapon, ritual7)} fusion6={Allows(ritualWeapon, fusion6)} " +
                $"plain={Allows(ritualWeapon, plain)}");
        }

        // ───────────────────────────── Rafflesia Seduction ─────────────────────────────

        static void RafflesiaDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            var p = e.Player;
            var opp = e.Opponent;
            p.Hand.Clear();
            opp.Hand.Clear();
            var rafflesia = PlaceMonster(e, p, Rafflesia, 0, BattlePosition.Defense, false);
            var prey = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
            var hidden = PlaceMonster(e, opp, GiantSoldier, 1, BattlePosition.Defense, false);

            var flipped = e.TryFlipSummon(p, rafflesia);
            System.Collections.Generic.List<CardInstance> offered = null;
            var picked = false;
            if (e.IsAwaitingEffectTarget)
            {
                offered = e.PendingActivation.LegalTargets.ToList();
                picked = e.TrySelectEffectTarget(prey);
            }

            DrainCombat(e);
            Check("Rafflesia Seduction: the FLIP offers only the opponent's face-up monster",
                flipped && offered != null && offered.Count == 1 && offered[0] == prey,
                offered == null ? "no prompt" : $"offered={string.Join(",", offered.Select(t => t.Name))}");

            Check("Rafflesia Seduction: the FLIP takes control until the end of the turn",
                picked && p.TryFindMonster(prey, out _) && !opp.TryFindMonster(prey, out _) &&
                prey.ControlRevertsAtEndPhase && prey.ControlBoundTo == null &&
                opp.TryFindMonster(hidden, out _),
                $"picked={picked} onP={p.TryFindMonster(prey, out _)} reverts={prey.ControlRevertsAtEndPhase}");

            e.EndTurn(p);
            Check("Rafflesia Seduction: control reverts in the End Phase and the monster is not destroyed",
                opp.TryFindMonster(prey, out _) && !p.TryFindMonster(prey, out _) &&
                !prey.ControlRevertsAtEndPhase && !opp.Graveyard.Contains(prey),
                $"onOpp={opp.TryFindMonster(prey, out _)} reverts={prey.ControlRevertsAtEndPhase} " +
                $"inGy={opp.Graveyard.Contains(prey)}");
        }

        // ───────────────────────────── Hallowed Life Barrier ─────────────────────────────

        static void HallowedDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            var p = e.Player;
            var opp = e.Opponent;
            p.Hand.Clear();
            opp.Hand.Clear();
            var fodder = PutInHand(e, p, Kuriboh);
            var chosen = PutInHand(e, p, PotOfGreed);
            var barrier = PlaceSetTrap(e, p, Hallowed, 0);

            var act = e.TryActivateSpellTrap(p, barrier, fromHand: false);
            var pend = e.PendingActivation;
            var offered = pend != null && pend.LegalTargets.Contains(fodder) && pend.LegalTargets.Contains(chosen) &&
                          !pend.LegalTargets.Contains(barrier);
            Check("Hallowed Life Barrier: activating asks which card to discard (any card, not itself)",
                act && e.IsAwaitingEffectTarget && pend.AwaitingIgnitionCost &&
                pend.TargetKind == EffectTargetKind.DiscardMonsterInHand && offered,
                $"act={act} offered={offered} kind={pend?.TargetKind} prompt=\"{pend?.Prompt}\"");

            var picked = e.TrySelectEffectTarget(chosen);
            Settle(e);
            var lp = p.LifePoints;
            e.ApplyEffectDamage(p, 1000);
            Check("Hallowed Life Barrier: the chosen card is discarded and effect damage this turn is 0",
                picked && p.Graveyard.Contains(chosen) && !p.Hand.Contains(chosen) &&
                p.NoDamageThroughTurnNumber == e.TurnNumber && p.LifePoints == lp &&
                p.Graveyard.Contains(barrier),
                $"picked={picked} gy={p.Graveyard.Contains(chosen)} flag={p.NoDamageThroughTurnNumber}/turn={e.TurnNumber} " +
                $"lp={lp}->{p.LifePoints}");

            // The next turn the protection is gone.
            e.EndTurn(p);
            var lp2 = p.LifePoints;
            e.ApplyEffectDamage(p, 1000);
            Check("Hallowed Life Barrier: the protection is over on the next turn",
                p.LifePoints == lp2 - 1000,
                $"flag={p.NoDamageThroughTurnNumber} turn={e.TurnNumber} lp={lp2}->{p.LifePoints}");
        }

        // ───────────────────────────── Mind Wipe ─────────────────────────────

        static void MindWipeDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var def = db.Get(MindWipe);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Activate)
                    .Find(c => c != null && c.Action == EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber);
                Check("Mind Wipe: Implemented, quick-play opponent-hand shuffle+redraw with the ≤ 3 condition",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.Side == EffectSide.Opponent &&
                    clause.ActivationCondition == RecoveryAndPhaseCostsRuntime.OpponentHasAtMostThreeInHand,
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"action={clause.Action} side={clause.Side} cond={clause.ActivationCondition}");
            }

            // The opponent has 4 cards in hand: the activation condition fails.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var wipe = PutInHand(e, p, MindWipe);
                for (var i = 0; i < 4; i++) PutInHand(e, opp, Kuriboh);
                var legal = e.CanActivateSpellTrap(p, wipe, fromHand: true);
                var act = e.TryActivateSpellTrap(p, wipe, fromHand: true);
                Check("Mind Wipe: cannot be activated while the opponent holds 4 cards",
                    !legal && !act && p.Hand.Contains(wipe) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"legal={legal} act={act} oppHand={opp.Hand.Count}");
            }

            // The opponent has 3 cards: the hand is shuffled into the Deck and 3 fresh cards are drawn.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var wipe = PutInHand(e, p, MindWipe);
                var a = PutInHand(e, opp, Kuriboh);
                var b = PutInHand(e, opp, PotOfGreed);
                var c = PutInHand(e, opp, Celtic);
                opp.Deck.Clear();
                for (var i = 0; i < 5; i++) opp.Deck.Add(GiantSoldier);

                var act = e.TryActivateSpellTrap(p, wipe, fromHand: true);
                Settle(e);
                Check("Mind Wipe: the opponent's 3-card hand is shuffled into the Deck and 3 cards are drawn",
                    act && opp.Hand.Count == 3 && !opp.Hand.Contains(a) && !opp.Hand.Contains(b) &&
                    !opp.Hand.Contains(c) && p.Graveyard.Contains(wipe) && !opp.Graveyard.Contains(a),
                    $"act={act} hand={opp.Hand.Count} keptOld={opp.Hand.Contains(a) || opp.Hand.Contains(b) || opp.Hand.Contains(c)} " +
                    $"wipeGy={p.Graveyard.Contains(wipe)}");
            }
        }

        // ───────────────────────────── Inferno Fire Blast ─────────────────────────────

        static void InfernoFireBlastDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var def = db.Get(InfernoFireBlast);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Activate)
                    .Find(c => c != null && c.Action == EffectActionKind.InflictDamageToOpponent);
                Check("Inferno Fire Blast: Implemented, damage = target original ATK and an attack lock",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.Zone == EffectZoneFilter.ControllerMonsters &&
                    clause.NamedCard == "Red-Eyes Black Dragon" && clause.AmountFromTargetOriginalAtk &&
                    clause.TargetCannotAttackThisTurn && clause.Side == EffectSide.Opponent,
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"zone={clause.Zone} name={clause.NamedCard} orig={clause.AmountFromTargetOriginalAtk} " +
                          $"lock={clause.TargetCannotAttackThisTurn} side={clause.Side}");
            }

            // No "Red-Eyes B. Dragon" on the field: there is no legal target, so no activation.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var blast = PutInHand(e, p, InfernoFireBlast);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var legal = e.CanActivateSpellTrap(p, blast, fromHand: true);
                Check("Inferno Fire Blast: only a face-up \"Red-Eyes B. Dragon\" you control is a legal target",
                    !legal, $"legal={legal}");
            }

            // With Red-Eyes: 2400 damage (its original ATK) and the Dragon cannot attack this turn.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var blast = PutInHand(e, p, InfernoFireBlast);
                var red = PlaceMonster(e, p, RedEyes, 0, BattlePosition.Attack, true);
                var lp = opp.LifePoints;
                var act = e.TryActivateSpellTrap(p, blast, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(red);
                Settle(e);
                Check("Inferno Fire Blast: 2400 to the opponent and the Dragon cannot attack this turn",
                    act && opp.LifePoints == lp - 2400 && red.CannotAttackDuringTurnNumber == e.TurnNumber &&
                    p.Graveyard.Contains(blast),
                    $"act={act} lp={lp}->{opp.LifePoints} lock={red.CannotAttackDuringTurnNumber}/turn={e.TurnNumber}");
            }
        }

        // ───────────────────────────── Ninjitsu Art of Decoy ─────────────────────────────

        static void NinjitsuDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var def = db.Get(Ninjitsu);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var act = prog?.ClausesFor(EffectTiming.Activate)
                    .Find(c => c != null && c.Action == EffectActionKind.ContinuousCannotBeDestroyedByBattle);
                var cont = prog?.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                    .Find(c => c != null && c.Action == EffectActionKind.ContinuousCannotBeDestroyedByBattle);
                Check("Ninjitsu Art of Decoy: Implemented, binds a face-up \"Ninja\" you control for battle immunity",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    act != null && act.Zone == EffectZoneFilter.ControllerMonsters &&
                    act.TargetSeriesName == "Ninja" && act.RequiresTargetFaceUp && cont != null,
                    act == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"zone={act.Zone} series={act.TargetSeriesName} faceup={act.RequiresTargetFaceUp} cont={cont != null}");
            }

            // Only a "Ninja" (a monster whose name/archetype matches) is a legal target.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var trap = PlaceSetTrap(e, p, Ninjitsu, 2);
                trap.SetThisTurn = false;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true); // not a "Ninja"
                var legal = e.CanActivateSpellTrap(p, trap, fromHand: false);
                Check("Ninjitsu Art of Decoy: a non-\"Ninja\" monster is not a legal target",
                    !legal, $"legal={legal}");
            }

            // The bound Ninja cannot be destroyed by battle.
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var trap = PlaceSetTrap(e, p, Ninjitsu, 2);
                trap.SetThisTurn = false;
                var ninja = PlaceMonster(e, p, NinjaSasuke, 0, BattlePosition.Defense, true); // 1000 DEF
                var attacker = PlaceMonster(e, opp, RedEyes, 2, BattlePosition.Attack, true); // 2400 ATK
                attacker.SummonedThisTurn = false;

                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                // The engine auto-picks when there is exactly one legal target; otherwise the pick is explicit.
                var picked = e.IsAwaitingEffectTarget ? e.TrySelectEffectTarget(ninja) : true;
                Settle(e);
                Check("Ninjitsu Art of Decoy: the Trap binds the selected Ninja and grants battle immunity",
                    act && picked && ninja.Equips.Contains(trap) && ReferenceEquals(trap.EquippedTo, ninja) &&
                    OfficialEffectRegistry.CannotBeDestroyedByBattle(ninja),
                    $"act={act} picked={picked} bound={ninja.Equips.Contains(trap)} " +
                    $"flag={OfficialEffectRegistry.CannotBeDestroyedByBattle(ninja)}");

                // The opponent's stronger monster attacks the Defense-Position Ninja: it survives.
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryEndTurnSafe(e.TurnPlayer);
                if (e.IsAwaitingResponse) e.PassResponse();
                if (e.TurnPlayer == opp && e.Phase == DuelPhase.Main1) e.TryEnterBattlePhase(opp);
                if (e.IsAwaitingResponse) e.PassResponse();
                var lp = p.LifePoints;
                var declared = e.TryAttack(opp, attacker, ninja);
                if (e.IsAwaitingResponse && e.PendingResponse?.Timing == ResponseTiming.AttackDeclared) e.PassResponse();
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Ninjitsu Art of Decoy: the bound Ninja is not destroyed by battle",
                    declared && p.TryFindMonster(ninja, out _) && !p.Graveyard.Contains(ninja) &&
                    !ninja.CannotBeDestroyedByBattle,
                    $"declared={declared} alive={p.TryFindMonster(ninja, out _)} " +
                    $"inGy={p.Graveyard.Contains(ninja)} lp={lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id)
        {
            var c = PlaceSetTrap(e, who, id, 0);
            c.FaceUp = true;
            return c;
        }

        /// <summary>Answer any response window and drain the combat queue.</summary>
        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            DrainCombat(e);
        }
    }
}
