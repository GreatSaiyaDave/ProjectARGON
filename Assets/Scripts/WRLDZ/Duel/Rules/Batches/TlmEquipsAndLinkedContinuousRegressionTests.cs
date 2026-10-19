using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TlmEquipsAndLinkedContinuous batch: Legendary Black Belt, Nitro Unit, Lone Wolf, Impenetrable Formation and Kaminote
    /// Blow (TLM Equip Spells and linked Continuous Spells/Traps with battle riders, three of them naming "Chu-Ske the Mouse
    /// Fighter", "Monk Fighter" and "Master Monk"). Compile shapes, mini-duels through the real entry points
    /// (TryActivateSpellTrap, TryAttack, DestroyMonsterPublic) and near misses.
    /// </summary>
    public static class TlmEquipsAndLinkedContinuousRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BlackBelt = 96458440;
        const int NitroUnit = 23842445;
        const int LoneWolf = 82452993;
        const int Formation = 96631852;
        const int Kaminote = 97570038;

        const int MonkFighter = 3810071;      // Rock 1300/1000
        const int MasterMonk = 49814180;      // Rock 1900/1000

        const int Celtic = 91152256;          // Normal Warrior 1400/1200
        const int GiantSoldier = 13039848;    // Normal Rock 1300/2000
        const int MysticalElf = 15025844;     // Normal Spellcaster 800/2000
        const int GeminiElf = 69140098;       // Normal Spellcaster 1900/900
        const int BlueEyes = 89631139;        // Normal Dragon 3000/2500
        const int ManEaterBug = 54652250;     // FLIP: target 1 monster on the field; destroy it
        const int DarkHole = 53129443;        // Normal Spell: destroy all monsters on the field
        const int RushRecklessly = 70046172;  // Quick-Play: target 1 face-up monster; +700 ATK until the End Phase

        const string Trio = "Chu-Ske the Mouse Fighter|Monk Fighter|Master Monk";

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TlmEquipsAndLinkedContinuous: " + name, ok, detail ?? "");

            LegendaryBlackBelt(db, pDeck, aDeck, Check);
            NitroUnitDuels(db, pDeck, aDeck, Check);
            LoneWolfDuels(db, pDeck, aDeck, Check);
            ImpenetrableFormation(db, pDeck, aDeck, Check);
            KaminoteBlow(db, pDeck, aDeck, Check);
            NearMisses(Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CardEffectStatusKind Status(CardDatabase db, int id) => CardEffectStatus.Classify(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [" + string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} names={c.AnyOfNamedCards} cond={c.ActivationCondition}")) +
                  "] unparsed=[" + string.Join("|", p.UnparsedFragments ?? Array.Empty<string>()) + "]";

        static void Wipe(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Wipe(e);
            return e;
        }

        static bool EnterBattle(DuelEngine e, DuelistState who)
        {
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(who);
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            return e.Phase == DuelPhase.Battle;
        }

        /// <summary>Pick the first legal card at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e)
        {
            for (var i = 0; i < 30; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = e.PendingActivation?.LegalTargets?.FirstOrDefault();
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                if (e.HasDeclaredAttack)
                {
                    e.ResolveDeclaredAttack();
                    continue;
                }

                break;
            }
        }

        /// <summary>Attack <paramref name="target"/> with <paramref name="attacker"/> and play the battle out.</summary>
        static bool Attack(DuelEngine e, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (!EnterBattle(e, who)) return false;
            var ok = e.TryAttack(who, attacker, target);
            Drive(e);
            DrainCombat(e);
            Drive(e);
            return ok;
        }

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone,
            BattlePosition pos = BattlePosition.Attack) => PlaceMonster(e, who, id, zone, pos, true);

        static CardInstance Down(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        /// <summary>Activate <paramref name="card"/>; at a prompt pick <paramref name="pick"/> (or the first legal card).</summary>
        static bool Activate(DuelEngine e, DuelistState who, CardInstance card, bool fromHand, CardInstance pick,
            out List<CardInstance> offered)
        {
            offered = null;
            var ok = e.TryActivateSpellTrap(who, card, fromHand);
            if (ok && e.IsAwaitingEffectTarget)
            {
                offered = e.PendingActivation.LegalTargets?.ToList();
                var chosen = pick != null && offered != null && offered.Contains(pick) ? pick : offered?.FirstOrDefault();
                if (chosen == null || !e.TrySelectEffectTarget(chosen)) e.CancelEffectTargeting();
            }

            Drive(e);
            return ok;
        }

        // ───────────────────────────── Legendary Black Belt ─────────────────────────────

        static void LegendaryBlackBelt(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, BlackBelt);
            var equip = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
            var burn = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.EquippedMonsterDestroysByBattle);
            Check("Legendary Black Belt: equip to a face-up listed monster on either field + the destroyed monster's DEF burn",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && equip != null &&
                equip.Action == EffectActionKind.EquipThisToTarget && equip.Zone == EffectZoneFilter.FieldAnyMonster &&
                equip.RequiresTargetFaceUp && equip.AnyOfNamedCards == Trio && burn != null &&
                burn.Action == EffectActionKind.InflictDestroyedMonsterDefToOpponent &&
                Status(db, BlackBelt) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // Targets: the listed monsters on either field, face-up only; Celtic and a face-down Monk Fighter are not.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var master = Up(e, p, MasterMonk, 0);
                var celtic = Up(e, p, Celtic, 1);
                Down(e, p, MonkFighter, 2);
                var oppMonk = Up(e, o, MonkFighter, 0);
                var belt = PutInHand(e, p, BlackBelt);
                e.NotifyPublic();
                var activated = Activate(e, p, belt, true, master, out var offered) && reached;
                var targetsOk = offered != null && offered.Count == 2 && offered.Contains(master) && offered.Contains(oppMonk);
                Check("Legendary Black Belt: offers only the face-up listed monsters (its own and the opponent's)",
                    activated && targetsOk && belt.EquippedTo == master && belt.FaceUp,
                    $"reached={reached} activated={activated} offered=[{string.Join(", ", offered?.Select(t => t.Name) ?? Array.Empty<string>())}] " +
                    $"equipped={belt.EquippedTo?.Name}");
                Check("Legendary Black Belt: its equip limit is the listed names (IsCorrectEquipTarget)",
                    EquipFamilyRuntime.IsCorrectEquipTarget(e, belt, oppMonk) &&
                    !EquipFamilyRuntime.IsCorrectEquipTarget(e, belt, celtic),
                    $"oppMonk={EquipFamilyRuntime.IsCorrectEquipTarget(e, belt, oppMonk)} " +
                    $"celtic={EquipFamilyRuntime.IsCorrectEquipTarget(e, belt, celtic)}");
            }

            {
                // The equipped Master Monk (1900) destroys Mystical Elf (800/2000, Attack): 1100 battle + 2000 (its DEF).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var master = Up(e, p, MasterMonk, 0);
                var elf = Up(e, o, MysticalElf, 0);
                var belt = PutInHand(e, p, BlackBelt);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, belt, true, master, out _);
                var lp = o.LifePoints;
                var attacked = Attack(e, p, master, elf);
                Check("Legendary Black Belt: its monster destroys a monster by battle → the destroyed monster's DEF as damage",
                    activated && attacked && o.Graveyard.Contains(elf) && lp - o.LifePoints == 1100 + 2000 &&
                    belt.EquippedTo == master,
                    $"reached={reached} activated={activated} attacked={attacked} elfGy={o.Graveyard.Contains(elf)} " +
                    $"lp={lp}->{o.LifePoints}");
            }

            {
                // Near miss: an unequipped battle (Gemini Elf destroys Mystical Elf) burns nothing beyond battle damage.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var master = Up(e, p, MasterMonk, 0);
                var gemini = Up(e, p, GeminiElf, 1);
                var elf = Up(e, o, MysticalElf, 0);
                var belt = PutInHand(e, p, BlackBelt);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, belt, true, master, out _);
                var lp = o.LifePoints;
                var attacked = Attack(e, p, gemini, elf);
                Check("Legendary Black Belt: another monster's battle destruction does not activate it",
                    activated && attacked && o.Graveyard.Contains(elf) && lp - o.LifePoints == 1100,
                    $"reached={reached} activated={activated} attacked={attacked} lp={lp}->{o.LifePoints}");
            }

            {
                // Near miss: no face-up listed monster (a face-down Monk Fighter, a face-up Celtic) → cannot be activated.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Down(e, p, MonkFighter, 0);
                Up(e, e.Opponent, Celtic, 0);
                var belt = PutInHand(e, p, BlackBelt);
                e.NotifyPublic();
                Check("Legendary Black Belt: cannot be activated without a face-up listed monster",
                    reached && !e.CanActivateSpellTrap(p, belt, fromHand: true), $"reached={reached}");
            }
        }

        // ───────────────────────────── Nitro Unit ─────────────────────────────

        static void NitroUnitDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, NitroUnit);
            var equip = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
            Check("Nitro Unit: equip to a face-up monster the opponent controls + the lost-host ATK burn marker",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && equip != null &&
                equip.Action == EffectActionKind.EquipThisToTarget && equip.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                !equip.TakeControlOfTarget &&
                prog.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                            c.Action == EffectActionKind.InflictLostHostAtkToOpponentWhenHostDestroyedByBattle) &&
                Status(db, NitroUnit) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // Celtic (1400) gets +700 (Rush Recklessly) and is destroyed by battle by Blue-Eyes: 900 battle damage and
                // 1400 (its ATK in the GY, not the 2100 it had) from Nitro Unit.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var be = Up(e, p, BlueEyes, 0);
                var celtic = Up(e, o, Celtic, 0);
                var nitro = PutInHand(e, p, NitroUnit);
                e.NotifyPublic();
                var activated = Activate(e, p, nitro, true, celtic, out var offered) && reached;
                var onlyOpp = offered == null || offered.All(t => o.TryFindMonster(t, out _));
                var equipped = nitro.EquippedTo == celtic;
                var rush = PutInHand(e, p, RushRecklessly);
                var rushed = Activate(e, p, rush, true, celtic, out _);
                var boosted = celtic.CurrentAtk == 2100;
                var lp = o.LifePoints;
                var attacked = Attack(e, p, be, celtic);
                Check("Nitro Unit: equipped to the opponent's monster; destroyed by battle → its printed ATK as damage",
                    activated && onlyOpp && equipped && rushed && boosted && attacked && o.Graveyard.Contains(celtic) &&
                    p.Graveyard.Contains(nitro) && lp - o.LifePoints == 900 + 1400,
                    $"reached={reached} activated={activated} onlyOpp={onlyOpp} equipped={equipped} rushed={rushed} " +
                    $"boosted={boosted} attacked={attacked} nitroGy={p.Graveyard.Contains(nitro)} lp={lp}->{o.LifePoints}");
            }

            {
                // Near miss: its monster destroyed by a card effect (not by battle) → Nitro Unit leaves, no damage.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = Up(e, o, Celtic, 0);
                var nitro = PutInHand(e, p, NitroUnit);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, nitro, true, celtic, out _);
                var lp = o.LifePoints;
                var plp = p.LifePoints;
                var hole = PutInHand(e, p, DarkHole);
                var holed = Activate(e, p, hole, true, null, out _);
                Check("Nitro Unit: its monster destroyed by an effect → it is sent to the GY, no damage",
                    activated && holed && o.Graveyard.Contains(celtic) && p.Graveyard.Contains(nitro) &&
                    lp == o.LifePoints && plp == p.LifePoints,
                    $"reached={reached} activated={activated} holed={holed} nitroGy={p.Graveyard.Contains(nitro)} " +
                    $"lp={lp}->{o.LifePoints}");
            }

            {
                // Near miss: only your own face-up monster → no target, cannot be activated.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Up(e, p, Celtic, 0);
                Down(e, e.Opponent, Celtic, 0);
                var nitro = PutInHand(e, p, NitroUnit);
                e.NotifyPublic();
                Check("Nitro Unit: cannot be activated without a face-up monster the opponent controls",
                    reached && !e.CanActivateSpellTrap(p, nitro, fromHand: true), $"reached={reached}");
            }
        }

        // ───────────────────────────── Lone Wolf ─────────────────────────────

        static void LoneWolfDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, LoneWolf);
            var link = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
            Check("Lone Wolf: links to the lone face-up listed monster; battle immunity + opponent monster-effect immunity",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 3 && link != null &&
                link.Action == EffectActionKind.LinkThisToTarget && link.Zone == EffectZoneFilter.ControllerMonsters &&
                link.AnyOfNamedCards == Trio && link.LinkStaysWhenHostLeaves && !link.DestroyThisWhenBoundHostLeaves &&
                link.ActivationCondition == TlmEquipsAndLinkedContinuousRuntime.OnlyMonsterIsFaceUpNamed &&
                prog.ClauseList.Exists(c => c.Action == EffectActionKind.ContinuousCannotBeDestroyedByBattle) &&
                prog.ClauseList.Exists(c => c.Action == EffectActionKind.LinkedMonsterUnaffectedByOpponentMonsterEffects) &&
                Status(db, LoneWolf) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // Linked Master Monk (1900) attacks Blue-Eyes (3000): it is not destroyed by battle.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var master = Up(e, p, MasterMonk, 0);
                var be = Up(e, o, BlueEyes, 0);
                var wolf = PlaceSetTrap(e, p, LoneWolf, 0);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, wolf, false, master, out _);
                var linked = wolf.FaceUp && wolf.EquippedTo == master;
                var plp = p.LifePoints;
                var attacked = Attack(e, p, master, be);
                var survived = p.TryFindMonster(master, out _) && plp - p.LifePoints == 1100;
                Check("Lone Wolf: linked to the lone listed monster; it is not destroyed by battle",
                    activated && linked && attacked && survived,
                    $"reached={reached} activated={activated} linked={linked} attacked={attacked} survived={survived} " +
                    $"plp={plp}->{p.LifePoints}");
            }

            {
                // Dark Hole (your Spell) still destroys its monster, and Lone Wolf stays on the field, unlinked.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var master = Up(e, p, MasterMonk, 0);
                var wolf = PlaceSetTrap(e, p, LoneWolf, 0);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, wolf, false, master, out _);
                var hole = PutInHand(e, p, DarkHole);
                var holed = Activate(e, p, hole, true, null, out _);
                Check("Lone Wolf: a Spell still destroys its monster; Lone Wolf stays face-up, unlinked",
                    activated && holed && p.Graveyard.Contains(master) && p.TryFindSpellTrap(wolf, out _) && wolf.FaceUp &&
                    wolf.EquippedTo == null,
                    $"reached={reached} activated={activated} holed={holed} masterGy={p.Graveyard.Contains(master)} " +
                    $"wolfOnField={p.TryFindSpellTrap(wolf, out _)}");
            }

            // The opponent's FLIP monster effect (Man-Eater Bug flipped by the attack) cannot destroy the linked monster;
            // without Lone Wolf the same battle destroys it.
            foreach (var withWolf in new[] { true, false })
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var master = Up(e, p, MasterMonk, 0);
                var bug = Down(e, o, ManEaterBug, 0);
                var activated = true;
                if (withWolf)
                {
                    var wolf = PlaceSetTrap(e, p, LoneWolf, 0);
                    e.NotifyPublic();
                    activated = reached && Activate(e, p, wolf, false, master, out _) && wolf.EquippedTo == master;
                }

                var attacked = Attack(e, p, master, bug);
                var masterStays = p.TryFindMonster(master, out _);
                Check(withWolf
                        ? "Lone Wolf: the opponent's Man-Eater Bug FLIP effect does not destroy its monster"
                        : "Lone Wolf control: without it, the opponent's Man-Eater Bug FLIP effect destroys Master Monk",
                    reached && activated && attacked && o.Graveyard.Contains(bug) && masterStays == withWolf,
                    $"reached={reached} activated={activated} attacked={attacked} bugGy={o.Graveyard.Contains(bug)} " +
                    $"masterStays={masterStays}");
            }

            {
                // Only the opponent's MONSTER effects: a Spell of the opponent is not shielded (ContinuousProtections).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var master = Up(e, p, MasterMonk, 0);
                var wolf = PlaceSetTrap(e, p, LoneWolf, 0);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, wolf, false, master, out _);
                var oppMonster = Up(e, o, Celtic, 0);
                var oppSpell = PutInHand(e, o, DarkHole);
                var ownMonster = PutInHand(e, p, Celtic);
                Check("Lone Wolf: unaffected by the opponent's monster effects only (not their Spells, not your monsters)",
                    activated && ContinuousProtections.IsUnaffectedBy(e, master, oppMonster) &&
                    !ContinuousProtections.IsUnaffectedBy(e, master, oppSpell) &&
                    !ContinuousProtections.IsUnaffectedBy(e, master, ownMonster),
                    $"reached={reached} activated={activated}");
            }

            {
                // Near misses: two monsters; a lone non-listed monster; a lone face-down listed monster.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Up(e, p, MasterMonk, 0);
                Up(e, p, Celtic, 1);
                var wolf = PlaceSetTrap(e, p, LoneWolf, 0);
                e.NotifyPublic();
                var two = e.CanActivateSpellTrap(p, wolf, fromHand: false);
                p.MonsterZones[0].Occupant = null;
                e.NotifyPublic();
                var nonListed = e.CanActivateSpellTrap(p, wolf, fromHand: false);
                p.MonsterZones[1].Occupant = null;
                Down(e, p, MonkFighter, 0);
                e.NotifyPublic();
                var faceDown = e.CanActivateSpellTrap(p, wolf, fromHand: false);
                Check("Lone Wolf: cannot be activated with 2 monsters, a lone non-listed monster or a face-down one",
                    reached && !two && !nonListed && !faceDown,
                    $"reached={reached} two={two} nonListed={nonListed} faceDown={faceDown}");
            }
        }

        // ───────────────────────────── Impenetrable Formation ─────────────────────────────

        static void ImpenetrableFormation(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, Formation);
            var link = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
            Check("Impenetrable Formation: link to a face-up monster you control; +700 DEF on the 2+/all-Defense condition",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 && link != null &&
                link.Action == EffectActionKind.LinkThisToTarget && link.Zone == EffectZoneFilter.ControllerMonsters &&
                link.RequiresTargetFaceUp && link.EquipDefBonus == 700 && link.EquipBonusRequiresTwoMonstersNoneInAttack &&
                link.DestroyThisWhenBoundHostLeaves && Status(db, Formation) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var giant = Up(e, p, GiantSoldier, 0, BattlePosition.Defense);
                Down(e, p, Celtic, 1);
                Up(e, o, MysticalElf, 0);
                var form = PutInHand(e, p, Formation);
                e.NotifyPublic();
                var activated = Activate(e, p, form, true, giant, out var offered) && reached;
                var onlyGiant = offered == null || (offered.Count == 1 && offered[0] == giant);
                var linked = form.EquippedTo == giant;
                e.NotifyPublic();
                var both = giant.CurrentDef;
                Up(e, p, GeminiElf, 2);
                e.NotifyPublic();
                var withAttack = giant.CurrentDef;
                p.MonsterZones[2].Occupant = null;
                p.MonsterZones[1].Occupant = null;
                e.NotifyPublic();
                var alone = giant.CurrentDef;
                Check("Impenetrable Formation: +700 DEF with 2+ monsters all in Defense (a Set one counts), not otherwise",
                    activated && onlyGiant && linked && both == 2700 && withAttack == 2000 && alone == 2000,
                    $"reached={reached} activated={activated} onlyGiant={onlyGiant} linked={linked} both={both} " +
                    $"withAttack={withAttack} alone={alone}");

                // Turned face-down and face-up again: the effect on the monster ended (RESETS_STANDARD), the link stays.
                Down(e, p, Celtic, 1);
                giant.FaceUp = false;
                giant.FaceUp = true;
                e.NotifyPublic();
                var afterReset = giant.CurrentDef;
                Check("Impenetrable Formation: its DEF gain ends once the monster was turned face-down",
                    linked && afterReset == 2000 && form.EquippedTo == giant, $"afterReset={afterReset}");

                e.DestroyMonsterPublic(p, giant);
                e.NotifyPublic();
                Check("Impenetrable Formation: when the selected monster leaves the field, this card is destroyed",
                    linked && p.Graveyard.Contains(giant) && p.Graveyard.Contains(form) && !p.TryFindSpellTrap(form, out _),
                    $"formGy={p.Graveyard.Contains(form)}");
            }

            {
                // Near miss: only a face-down monster you control and the opponent's face-up monster → cannot be activated.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Down(e, p, GiantSoldier, 0);
                Up(e, e.Opponent, Celtic, 0);
                var form = PutInHand(e, p, Formation);
                e.NotifyPublic();
                Check("Impenetrable Formation: cannot be activated without a face-up monster you control",
                    reached && !e.CanActivateSpellTrap(p, form, fromHand: true), $"reached={reached}");
            }
        }

        // ───────────────────────────── Kaminote Blow ─────────────────────────────

        static void KaminoteBlow(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var prog = Prog(db, Kaminote);
            var act = prog?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.Activate);
            Check("Kaminote Blow: a listed face-up monster condition; destroy what battles them at the end of the Damage Step",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 && act != null &&
                act.Action == EffectActionKind.DestroyMonstersBattlingNamedAtEndOfDamageStepThisTurn &&
                act.AnyOfNamedCards == Trio &&
                act.ActivationCondition == TlmEquipsAndLinkedContinuousRuntime.NamedFaceUpInYourMonsterZone &&
                !act.RequiresTargetChoice && Status(db, Kaminote) == CardEffectStatusKind.Implemented,
                Describe(prog));

            {
                // Monk Fighter (1300) attacks Giant Soldier (DEF 2000): no battle destruction; destroyed at the end of the
                // Damage Step. Then Celtic (not listed) attacks Mystical Elf (DEF 2000): it survives.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var monk = Up(e, p, MonkFighter, 0);
                var celtic = Up(e, p, Celtic, 1);
                var giant = Up(e, o, GiantSoldier, 0, BattlePosition.Defense);
                var elf = Up(e, o, MysticalElf, 1, BattlePosition.Defense);
                var blow = PutInHand(e, p, Kaminote);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, blow, true, null, out _);
                var attacked = Attack(e, p, monk, giant);
                var giantGone = o.Graveyard.Contains(giant);
                var attacked2 = Attack(e, p, celtic, elf);
                var elfStays = o.TryFindMonster(elf, out _);
                Check("Kaminote Blow: the monster a listed monster battled is destroyed at the end of the Damage Step",
                    activated && p.Graveyard.Contains(blow) && attacked && giantGone && p.TryFindMonster(monk, out _),
                    $"reached={reached} activated={activated} attacked={attacked} giantGone={giantGone}");
                Check("Kaminote Blow: a battle without a listed monster destroys nothing",
                    attacked2 && elfStays, $"attacked2={attacked2} elfStays={elfStays}");
            }

            {
                // Monk Fighter (1300) attacks Celtic (1400): Monk Fighter is destroyed by battle; Celtic, still in its Monster
                // Zone, is destroyed at the end of the Damage Step (Lua s.cfilter reads the GY card's controller).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var monk = Up(e, p, MonkFighter, 0);
                var celtic = Up(e, o, Celtic, 0);
                var blow = PutInHand(e, p, Kaminote);
                e.NotifyPublic();
                var activated = reached && Activate(e, p, blow, true, null, out _);
                var attacked = Attack(e, p, monk, celtic);
                Check("Kaminote Blow: applies even when the listed monster was destroyed by that battle",
                    activated && attacked && p.Graveyard.Contains(monk) && o.Graveyard.Contains(celtic),
                    $"reached={reached} activated={activated} attacked={attacked} monkGy={p.Graveyard.Contains(monk)} " +
                    $"celticGy={o.Graveyard.Contains(celtic)}");
            }

            {
                // Near miss: a face-down Monk Fighter and the opponent's face-up Master Monk → cannot be activated.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Down(e, p, MonkFighter, 0);
                Up(e, e.Opponent, MasterMonk, 0);
                var blow = PutInHand(e, p, Kaminote);
                e.NotifyPublic();
                Check("Kaminote Blow: cannot be activated without a face-up listed monster you control",
                    reached && !e.CanActivateSpellTrap(p, blow, fromHand: true), $"reached={reached}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            CompiledCardProgram Syn(int id, string type, string race, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = "Near miss " + id, type = type,
                    frameType = type.StartsWith("Trap") ? "trap" : "spell", race = race, desc = desc
                });

            bool NotOurs(CompiledCardProgram p) =>
                p == null || !p.FullyCompiled ||
                !p.ClauseList.Exists(c => !string.IsNullOrEmpty(c.AnyOfNamedCards) ||
                                          c.Action == EffectActionKind.InflictDestroyedMonsterDefToOpponent ||
                                          c.Action == EffectActionKind.InflictLostHostAtkToOpponentWhenHostDestroyedByBattle ||
                                          c.Action == EffectActionKind.LinkedMonsterUnaffectedByOpponentMonsterEffects ||
                                          c.Action == EffectActionKind.DestroyMonstersBattlingNamedAtEndOfDamageStepThisTurn);

            var atkBurn = Syn(91790001, "Spell Card", "Equip",
                "Equip only to \"Chu-Ske the Mouse Fighter\", \"Monk Fighter\", or \"Master Monk\". When the equipped monster " +
                "destroys your opponent's monster by battle and sends it to the Graveyard, inflict damage to your opponent " +
                "equal to the ATK of the destroyed monster.");
            var oneName = Syn(91790002, "Trap Card", "Continuous",
                "Activate only if the 1 and only monster on your side of the field is \"Monk Fighter\". That monster cannot " +
                "be destroyed as a result of battle, and is unaffected by any of your opponent's monster effects.");
            var longer = Syn(91790003, "Spell Card", "Normal",
                "Activate only if there is at least 1 face-up \"Chu-Ske the Mouse Fighter\", \"Monk Fighter\", or \"Master " +
                "Monk\" on your side of the field. Destroy the monster(s) that battle with these monsters on your side of the " +
                "field this turn, at the end of each Damage Step. Draw 1 card.");
            var ownSide = Syn(91790004, "Spell Card", "Equip",
                "Equip only to a monster on your side of the field. When the equipped monster is destroyed by battle and " +
                "sent to the Graveyard, inflict damage to your opponent equal to the ATK of the equipped monster.");
            Check("Near misses: the ATK variant, a single name, a longer text and an own-side Nitro are not compiled here",
                NotOurs(atkBurn) && NotOurs(oneName) && NotOurs(longer) && NotOurs(ownSide),
                $"atk={Describe(atkBurn)} one={Describe(oneName)} longer={Describe(longer)} own={Describe(ownSide)}");
        }
    }
}
