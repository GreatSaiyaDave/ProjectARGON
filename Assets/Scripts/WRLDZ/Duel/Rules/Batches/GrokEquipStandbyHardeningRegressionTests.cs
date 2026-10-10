using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Hardening of the unreviewed 2026-09-30..10-02 compiles (EquipRiders, StandbyPhaseEffects, the ATK-or-DEF equip,
    /// the Token LP sentence), checked against the official Lua.
    /// <list type="bullet">
    /// <item>Cyclon Laser (c5494820): equip only to a face-up "Gradius" on either field. The either-field zone ignored
    /// <see cref="EffectClause.EquipHostName"/>, so any face-up monster was legal; fixed in CollectTargets.</item>
    /// <item>Mask of the Accursed (c56948373): the Standby burn had no runtime arm; it now damages the equipped
    /// monster's current controller during the Mask controller's Standby Phase only.</item>
    /// <item>Ekibyo Drakmord (c69954399): its End Phase countdown had no runtime here (a Stub); the EngineHygiene batch
    /// (v160) added it (EngineHygieneRuntime.OnEndPhase), so the guard now asserts the Implemented shape.</item>
    /// <item>7 Completed (c86198326): a human chooses ATK or DEF; the AI takes ATK; only a Machine can be equipped.</item>
    /// <item>Darklord Marie (c57579381): 200 LP only from the GY, only in its owner's Standby Phase.</item>
    /// <item>Token Thanksgiving (c57182235): every Token on the field is destroyed, 800 LP per Token.</item>
    /// <item>Blind Destruction (c32015116): parked until v162; since the StandbyBurnsAndDice batch it compiles to one
    /// Standby die-roll Level wipe clause (no Dark Hole fragment).</item>
    /// </list>
    /// </summary>
    public static class GrokEquipStandbyHardeningRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int CyclonLaser = 5494820;
        const int MaskOfTheAccursed = 56948373;
        const int EkibyoDrakmord = 69954399;
        const int SevenCompleted = 86198326;
        const int DarklordMarie = 57579381;
        const int TokenThanksgiving = 57182235;
        const int BlindDestruction = 32015116;
        const int Gradius = 10992251;          // Normal Machine 1200/800
        const int Mechanicalchaser = 7359741;  // Normal Machine 1850/800
        const int Celtic = 91152256;           // Normal Warrior 1400/1200

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("GrokEquipStandby: " + name, ok, detail ?? "");

            Shapes(db, Check);
            CyclonLaserOnlyGradius(db, pDeck, aDeck, Check);
            CyclonLaserPierces(db, pDeck, aDeck, Check);
            MaskOfTheAccursedBurnsEquippedController(db, pDeck, aDeck, Check);
            SevenCompletedHumanChooses(db, pDeck, aDeck, Check);
            SevenCompletedAiTakesAtk(db, pDeck, aDeck, Check);
            DarklordMarieOnlyFromGy(db, pDeck, aDeck, Check);
            TokenThanksgivingGainsPerToken(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            {
                var (prog, st) = Compile(db, CyclonLaser);
                var eq = prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == EffectActionKind.EquipThisToTarget);
                Check("Cyclon Laser: Implemented, equip to \"Gradius\" on either field, +300, piercing",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && eq != null &&
                    eq.Zone == EffectZoneFilter.FieldAnyMonster && eq.EquipHostName == "Gradius" &&
                    eq.EquipAtkBonus == 300 && eq.EquipDefBonus == 0 &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.EquippedGainsPiercing),
                    $"status={st} {Describe(prog)}");
            }

            {
                var (prog, st) = Compile(db, MaskOfTheAccursed);
                Check("Mask of the Accursed: Implemented, equip either field, cannot attack, Standby 500 to the host's controller",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                                                c.Zone == EffectZoneFilter.FieldAnyMonster) &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.EquippedCannotAttack) &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.StandbyPhase &&
                                                c.Action == EffectActionKind.InflictDamageToLinkedCardController &&
                                                c.Amount == 500 && !c.OpponentTurnOnly && !c.EachStandbyPhase),
                    $"status={st} {Describe(prog)}");
            }

            {
                var (prog, st) = Compile(db, EkibyoDrakmord);
                // EngineHygiene batch (v160): the countdown has its runtime (EngineHygieneRuntime.OnEndPhase).
                Check("Ekibyo Drakmord: Implemented — curse equip, cannot attack, destroy at its controller's 2nd End Phase",
                    st == CardEffectStatusKind.Implemented && prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                        c.Zone == EffectZoneFilter.FieldAnyMonster) &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.EquippedCannotAttack) &&
                    prog.ClauseList.Exists(c => c != null && c.Amount == 2 &&
                        c.Action == EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis),
                    $"status={st} {Describe(prog)}");
            }

            {
                var (prog, st) = Compile(db, SevenCompleted);
                var eq = prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == EffectActionKind.EquipThisToTarget);
                Check("7 Completed: Implemented, Machine only, 700 to the ATK or DEF chosen at activation",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && eq != null &&
                    eq.ChooseAtkOrDef && eq.RaceFilter == "Machine" && eq.EquipAtkBonus == 700 &&
                    eq.EquipDefBonus == 700 && prog.ClauseList.Count == 1,
                    $"status={st} {Describe(prog)}");
            }

            {
                var (prog, st) = Compile(db, DarklordMarie);
                var c0 = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Darklord Marie: Implemented, one Standby gain of 200 from the GY",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && c0 != null &&
                    c0.Timing == EffectTiming.StandbyPhase && c0.Action == EffectActionKind.GainLifePoints &&
                    c0.Amount == 200 && c0.ResolvesFromGy && !c0.OpponentTurnOnly && !c0.EachStandbyPhase,
                    $"status={st} {Describe(prog)}");
            }

            {
                var (prog, st) = Compile(db, TokenThanksgiving);
                var c0 = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Token Thanksgiving: Implemented, destroy every Token, gain 800 per Token",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && c0 != null &&
                    c0.Action == EffectActionKind.DestroyTokensInflictPer && c0.Amount == 800 &&
                    c0.TokenDestroyGainsLp,
                    $"status={st} {Describe(prog)}");
            }

            {
                // StandbyBurnsAndDice batch (v162): the die-roll Level wipe has its runtime now — one Standby clause, and
                // no Dark Hole "destroy all monsters on the field" fragment.
                var (prog, st) = Compile(db, BlindDestruction);
                Check("Blind Destruction: one Standby die-roll Level wipe clause (no Dark Hole fragment), Implemented",
                    st == CardEffectStatusKind.Implemented && prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.RollDieDestroyFaceUpMonstersWithRolledLevel &&
                    prog.ClauseList[0].Timing == EffectTiming.StandbyPhase,
                    $"status={st} {Describe(prog)}");
            }
        }

        // ───────────────────────────── mini-duels ─────────────────────────────

        /// <summary>Lua: aux.AddEquipProcedure(c,nil,IsCode Gradius): face-up "Gradius" on either field only.</summary>
        static void CyclonLaserOnlyGradius(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Gradius, 1, BattlePosition.Defense, false);
                var laser = PutInHand(e, p, CyclonLaser);
                var can = e.CanActivateSpellTrap(p, laser, fromHand: true);
                var act = !can && e.TryActivateSpellTrap(p, laser, fromHand: true);
                Settle(e);
                Check("Cyclon Laser: no face-up \"Gradius\" (only other monsters, a face-down Gradius): cannot activate",
                    !can && !act && p.Hand.Contains(laser) && laser.EquippedTo == null,
                    $"can={can} act={act} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var chaser = PlaceMonster(e, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
                var gradius = PlaceMonster(e, o, Gradius, 1, BattlePosition.Attack, true);
                var laser = PutInHand(e, p, CyclonLaser);
                var act = e.TryActivateSpellTrap(p, laser, fromHand: true);
                var legal = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var ok = Drive(e, gradius);
                e.NotifyPublic();
                Check("Cyclon Laser: only the opponent's face-up \"Gradius\" is legal; it gains 300 ATK",
                    act && laser.EquippedTo == gradius && gradius.Equips.Contains(laser) &&
                    !legal.Contains(celtic) && !legal.Contains(chaser) &&
                    gradius.CurrentAtk == 1500 && gradius.CurrentDef == 800 && celtic.Equips.Count == 0,
                    $"act={act} drive={ok} legal=[{Names(legal)}] host={laser.EquippedTo?.Name} atk={gradius.CurrentAtk} {State(e)}");
            }
        }

        /// <summary>Lua EFFECT_PIERCE: the equipped Gradius (1500) attacks a 1200 DEF monster: 300 damage.</summary>
        static void CyclonLaserPierces(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearAll(e);
            var p = e.Player;
            var o = e.Opponent;
            var gradius = PlaceMonster(e, o, Gradius, 0, BattlePosition.Attack, true);
            var wall = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, true);
            var laser = PutInHand(e, o, CyclonLaser);
            var act = e.TryActivateSpellTrap(o, laser, fromHand: true);
            Drive(e, gradius);
            e.NotifyPublic();
            var lp = p.LifePoints;
            var bp = e.TryEnterBattlePhase(o);
            Settle(e);
            var attacked = e.TryAttack(o, gradius, wall);
            DrainCombat(e);
            Check("Cyclon Laser: the equipped Gradius pierces a Defense Position monster for the difference",
                reached && act && laser.EquippedTo == gradius && bp && attacked && p.Graveyard.Contains(wall) &&
                p.LifePoints == lp - 300,
                $"reached={reached} act={act} host={laser.EquippedTo?.Name} bp={bp} attacked={attacked} " +
                $"lp {lp}->{p.LifePoints} wallGy={p.Graveyard.Contains(wall)}");
        }

        /// <summary>
        /// Lua: equip either field, EFFECT_CANNOT_ATTACK, TRIGGER_F at the Standby Phase while IsTurnPlayer(tp):
        /// Damage(GetEquipTarget():GetControler(), 500). The player's Mask on the opponent's monster: nothing in the
        /// opponent's Standby Phase, the monster cannot attack, 500 to the opponent in the player's next Standby Phase.
        /// </summary>
        static void MaskOfTheAccursedBurnsEquippedController(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var foe = PlaceMonster(e, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
            var mask = PutInHand(e, p, MaskOfTheAccursed);
            var act = e.TryActivateSpellTrap(p, mask, fromHand: true);
            Drive(e, foe);
            e.NotifyPublic();
            var equipped = mask.EquippedTo == foe;
            var pLp = p.LifePoints;
            var oLp = o.LifePoints;
            var reached = ReachOpponentBattle(e);
            var oppStandbyQuiet = p.LifePoints == pLp && o.LifePoints == oLp;
            var canAttack = reached && e.CanAttack(o, foe);
            var attacked = reached && e.TryAttack(o, foe, null);
            DrainCombat(e);
            var reachedMine = ReachPlayerMain(e);
            Check("Mask of the Accursed: equipped monster cannot attack; 500 to its controller only in the Mask controller's Standby Phase",
                act && equipped && reached && oppStandbyQuiet && !canAttack && !attacked && reachedMine &&
                o.LifePoints == oLp - 500 && p.LifePoints == pLp && mask.EquippedTo == foe,
                $"act={act} equipped={equipped} reached={reached} oppStandbyQuiet={oppStandbyQuiet} " +
                $"canAttack={canAttack} attacked={attacked} reachedMine={reachedMine} " +
                $"oLp {oLp}->{o.LifePoints} pLp {pLp}->{p.LifePoints} turn={e.TurnNumber}");
        }

        /// <summary>
        /// Lua s.operation: Duel.SelectOption(tp, ATK, DEF) for the activating player. A human gets the ATK/DEF
        /// prompt; DEF gives +700 DEF only. A non-Machine cannot be equipped.
        /// </summary>
        static void SevenCompletedHumanChooses(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var seven = PutInHand(e, p, SevenCompleted);
                var can = e.CanActivateSpellTrap(p, seven, fromHand: true);
                Check("7 Completed: no face-up Machine: cannot activate",
                    !can && p.Hand.Contains(seven), $"can={can} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var chaser = PlaceMonster(e, p, Mechanicalchaser, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var seven = PutInHand(e, p, SevenCompleted);
                var act = e.TryActivateSpellTrap(p, seven, fromHand: true);
                var pend = e.PendingActivation;
                var prompt = pend?.Prompt ?? "";
                var options = pend?.LegalTargets.Select(t => t?.Def?.name).ToList() ?? new List<string>();
                var def = pend?.LegalTargets.FirstOrDefault(t => t?.Def?.name == "DEF");
                var picked = def != null && e.TrySelectEffectTarget(def);
                Drive(e, chaser);
                e.NotifyPublic();
                Check("7 Completed: a human is asked ATK or DEF; DEF gives +700 DEF only",
                    act && prompt.IndexOf("ATK or DEF", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    options.Count == 2 && options.Contains("ATK") && options.Contains("DEF") && picked &&
                    seven.EquippedTo == chaser && chaser.CurrentAtk == 1850 && chaser.CurrentDef == 1500,
                    $"act={act} prompt='{prompt}' options=[{string.Join(",", options)}] picked={picked} " +
                    $"host={seven.EquippedTo?.Name} atk={chaser.CurrentAtk} def={chaser.CurrentDef} {State(e)}");
            }
        }

        /// <summary>The AI's 7 Completed takes ATK with no prompt.</summary>
        static void SevenCompletedAiTakesAtk(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearAll(e);
            var o = e.Opponent;
            var chaser = PlaceMonster(e, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
            var seven = PutInHand(e, o, SevenCompleted);
            var act = e.TryActivateSpellTrap(o, seven, fromHand: true);
            var prompted = e.PendingActivation != null && e.PendingActivation.Controller == o;
            Drive(e, chaser);
            e.NotifyPublic();
            Check("7 Completed: the AI is not prompted and takes +700 ATK",
                reached && act && !prompted && seven.EquippedTo == chaser && chaser.CurrentAtk == 2550 &&
                chaser.CurrentDef == 800,
                $"reached={reached} act={act} prompted={prompted} host={seven.EquippedTo?.Name} " +
                $"atk={chaser.CurrentAtk} def={chaser.CurrentDef}");
        }

        /// <summary>
        /// Lua: TRIGGER_F, range GY, condition tp == turn player, Recover(tp, 200). The GY copy gains 200 in its owner's
        /// Standby Phase, nothing in the opponent's; a face-up Marie on the field gains nothing.
        /// </summary>
        static void DarklordMarieOnlyFromGy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Graveyard.Add(e.CreateCardInstance(DarklordMarie));
            PlaceMonster(e, p, DarklordMarie, 0, BattlePosition.Defense, true);
            e.NotifyPublic();
            var pLp = p.LifePoints;
            var oLp = o.LifePoints;
            var reached = ReachOpponentMain(e);
            var oppTurn = p.LifePoints;
            var reachedMine = ReachPlayerMain(e);
            Check("Darklord Marie: +200 only from the GY in its owner's Standby Phase (none on the field, none in the opponent's)",
                reached && oppTurn == pLp && reachedMine && p.LifePoints == pLp + 200 && o.LifePoints == oLp,
                $"reached={reached} pLp {pLp}->{oppTurn}->{p.LifePoints} oLp {oLp}->{o.LifePoints} turn={e.TurnNumber}");
        }

        /// <summary>Lua: needs a Token on the field; destroys every Token; Recover(tp, destroyed * 800).</summary>
        static void TokenThanksgivingGainsPerToken(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var card = PutInHand(e, p, TokenThanksgiving);
                var can = e.CanActivateSpellTrap(p, card, fromHand: true);
                Check("Token Thanksgiving: no Token on the field: cannot activate", !can && p.Hand.Contains(card),
                    $"can={can} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var t1 = Token(e, p, 1);
                var t2 = Token(e, p, 2);
                var t3 = Token(e, o, 0);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var card = PutInHand(e, p, TokenThanksgiving);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                Settle(e);
                var left = new[] { t1, t2, t3 }.Count(t => e.ControllerOf(t) != null);
                Check("Token Thanksgiving: 3 Tokens (both fields) destroyed, the activator gains 2400, other monsters stay",
                    act && left == 0 && p.LifePoints == pLp + 2400 && o.LifePoints == oLp &&
                    p.TryFindMonster(celtic, out _),
                    $"act={act} tokensLeft={left} pLp {pLp}->{p.LifePoints} oLp {oLp}->{o.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static (CompiledCardProgram prog, CardEffectStatusKind st) Compile(CardDatabase db, int id)
        {
            var def = db.Get(id);
            if (def == null) return (null, CardEffectStatusKind.Unimplemented);
            return (CardTextEffectCompiler.Compile(def), CardEffectStatus.Classify(def));
        }

        static CardInstance Token(DuelEngine e, DuelistState who, int zone)
        {
            var t = e.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
            t.FaceUp = true;
            t.Position = BattlePosition.Defense;
            who.MonsterZones[zone].Occupant = t;
            return t;
        }

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearAll(e);
            return e;
        }

        static void ClearAll(DuelEngine e)
        {
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        /// <summary>End turns until the player's next Main Phase (its Draw and Standby Phases ran).</summary>
        static bool ReachPlayerMain(DuelEngine e)
        {
            var start = e.TurnNumber;
            for (var i = 0; i < 10 && !(e.TurnPlayer == e.Player && e.InMainPhase && e.TurnNumber > start); i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.InMainPhase && e.TurnNumber > start) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            Settle(e);
            return e.TurnPlayer == e.Player && e.InMainPhase && e.TurnNumber > start;
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>Every target prompt picks <paramref name="target"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, CardInstance target)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var ok = e.TrySelectEffectTarget(target);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
        }

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", cards.Select(c => c?.Name ?? "-"));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation != null} response={e.IsAwaitingResponse} chain={e.Chain.HasLinks}";

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "prog=null"
                : $"full={prog.FullyCompiled} [{string.Join("; ", prog.ClauseList.Where(c => c != null).Select(c => $"{c.Timing}/{c.Action}/{c.Zone}"))}]";
    }
}
