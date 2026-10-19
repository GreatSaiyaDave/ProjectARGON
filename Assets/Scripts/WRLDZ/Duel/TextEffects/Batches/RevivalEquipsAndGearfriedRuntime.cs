using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RevivalEquipsAndGearfried" runtime (<see cref="RevivalEquipsAndGearfriedAtoms"/>). Reads compiled clauses
    /// only — no card-id branches.
    /// <list type="bullet">
    /// <item>Fulfillment of the Contract / Re-Fusion: their GY target is a Ritual / Fusion Monster that
    /// <see cref="SpecialSummonLegality.CanBeSpecialSummoned"/> allows from the GY (only a properly summoned copy) —
    /// <see cref="FilterTargets"/>; the Extra Deck monster enters the GY list (<see cref="RevivesExtraDeckMonster"/>) and the
    /// activation takes the compiled flow (<see cref="UsesCompiledActivationPending"/>). The Summon, the equip link and the
    /// "when destroyed, banish" are the shared SpecialSummonFromGy arm and DuelEngine.UnlinkCardFromHostOnLeave
    /// (<see cref="EffectClause.BanishHostOnlyIfThisDestroyed"/>).</item>
    /// <item>Gearfried the Swordmaster: <see cref="NoteEquipped"/> (the shared EVENT_EQUIP,
    /// GuardiansAndEquipEventsRuntime.NoteEquipped) queues its mandatory trigger on the SEGOC queue; it targets 1 monster
    /// the opponent controls (face-up or face-down) and destroys it if it is still the opponent's.</item>
    /// <item>Brain Jacker: <see cref="EquipThisFromFieldToTarget"/> (its FLIP) and <see cref="SkipsPhaseClause"/> (its Standby
    /// gain only from the Spell &amp; Trap Zone as an Equip Card).</item>
    /// </list>
    /// </summary>
    public static class RevivalEquipsAndGearfriedRuntime
    {
        // ───────────────────────────── Fulfillment of the Contract / Re-Fusion ─────────────────────────────

        /// <summary>"Target 1 Ritual / Fusion Monster in your GY; Special Summon it".</summary>
        static bool IsTypedRevival(EffectClause c) =>
            c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
            c.Zone == EffectZoneFilter.ControllerGyMonsters &&
            (c.RequiresRitualMonsterTarget || c.RequiresFusionMonsterTarget);

        /// <summary>TextEffectRuntime.CollectTargets: the GY list keeps Extra Deck monsters for a Fusion Monster revival.</summary>
        public static bool RevivesExtraDeckMonster(EffectClause c) => IsTypedRevival(c) && c.RequiresFusionMonsterTarget;

        /// <summary>SpellTrapEffects: the activation asks its target through the compiled flow (this filter).</summary>
        public static bool UsesCompiledActivationPending(EffectClause c) => IsTypedRevival(c);

        /// <summary>
        /// End of TextEffectRuntime.CollectTargets: a Ritual / Fusion Monster in <paramref name="who"/>'s GY that can be
        /// Special Summoned from there by <paramref name="source"/> (Lua IsCanBeSpecialSummoned(e,0,tp,false,false): a
        /// Ritual or Fusion Monster only once properly summoned, the Nomi / Necrovalley / summon locks).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance source,
            List<CardInstance> list)
        {
            if (engine == null || who == null || list == null || list.Count == 0 || !IsTypedRevival(c)) return;
            list.RemoveAll(t => t?.Def == null || who.Graveyard == null || !who.Graveyard.Contains(t) ||
                                (c.RequiresRitualMonsterTarget && !t.Def.IsRitualMonster) ||
                                (c.RequiresFusionMonsterTarget && !t.Def.IsFusionMonster) ||
                                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, t, source, SummonOrigin.Graveyard,
                                    checkHandToll: false));
        }

        // ───────────────────────────── Gearfried the Swordmaster ─────────────────────────────

        static EffectClause LiveMarker(DuelEngine engine, CardInstance host)
        {
            if (engine == null || host?.Def == null || !host.FaceUp || host.IsNegated) return null;
            var side = engine.ControllerOf(host);
            if (side == null || !side.TryFindMonster(host, out _)) return null;
            var prog = CompiledEffectCache.GetOrCompile(host);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.DestroyOppMonsterEachTimeThisIsEquipped);
        }

        /// <summary>
        /// The shared EVENT_EQUIP (GuardiansAndEquipEventsRuntime.NoteEquipped, called by EquipModelRuntime.NoteEquipped and
        /// the Premature Burial-style Summon link): <paramref name="equip"/>, an Equip Card (an Equip Spell, a monster Card or
        /// an Equip Trap — not a Call of the Haunted link), was just equipped to <paramref name="host"/>. A face-up Gearfried
        /// the Swordmaster whose effects are not negated queues its mandatory trigger (one per equip).
        /// </summary>
        public static void NoteEquipped(DuelEngine engine, CardInstance equip, CardInstance host)
        {
            if (engine == null || engine.GameOver || equip?.Def == null || host == null || equip.EquippedTo != host) return;
            if (!TextEffectRuntime.IsRealEquipCard(equip)) return;
            if (LiveMarker(engine, host) == null) return;
            var side = engine.ControllerOf(host);
            if (side == null) return;
            engine.Log($"{host.Name}: {equip.Name} was equipped to it — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, side, host, optional: false,
                () => ResolveSwordmaster(engine, host, side));
        }

        /// <summary>The pick list: monsters the opponent of <paramref name="who"/> controls that this effect can target.</summary>
        static List<CardInstance> SwordmasterTargets(DuelEngine engine, DuelistState who, CardInstance host)
        {
            var pick = new EffectClause
            {
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppMonsters,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                TargetCount = 1,
                MakesChainLink = true
            };
            return TextEffectRuntime.CollectTargets(engine, who, pick, host) ?? new List<CardInstance>();
        }

        static void ResolveSwordmaster(DuelEngine engine, CardInstance host, DuelistState queuedFor)
        {
            if (engine == null || engine.GameOver || host == null) return;
            // The trigger's player is the monster's controller once the equip has resolved (a Brain Jacker that took it).
            var who = engine.ControllerOf(host) ?? queuedFor;
            if (who == null) return;
            if (engine.ControllerOf(host) != null && host.FaceUp && host.IsNegated)
            {
                engine.Log($"{host.Name}: its effect is negated.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, host, new CardPickRequest
            {
                Candidates = () => SwordmasterTargets(engine, who, host),
                Kind = EffectTargetKind.OppMonster,
                Prompt = $"{host.Name}: tap 1 monster your opponent controls to destroy it.",
                AiChoose = list => list.OrderByDescending(t => t.FaceUp ? 1 : 0).ThenByDescending(t => t.CurrentAtk)
                    .FirstOrDefault(),
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{host.Name}: your opponent controls no monster it can target — no effect.");
                        return;
                    }

                    // Lua s.desop: tc:IsRelateToEffect(e) and tc:IsControler(1-tp).
                    var opp = engine.OpponentOf(who);
                    if (opp == null || !ReferenceEquals(engine.ControllerOf(chosen), opp) || !opp.TryFindMonster(chosen, out _))
                    {
                        engine.Log($"{host.Name}: {chosen.Name} is no longer a monster your opponent controls — no effect.");
                        return;
                    }

                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosen, host))
                    {
                        engine.Log("Target is protected at resolution.");
                        return;
                    }

                    engine.Log($"{host.Name} destroys {chosen.Name}.");
                    TextEffectRuntime.DestroyCard(engine, chosen, host);
                    engine.NotifyPublic();
                }
            });
        }

        // ───────────────────────────── Brain Jacker ─────────────────────────────

        /// <summary>
        /// ApplyClause arm (<see cref="EffectActionKind.EquipThisFromFieldToTargetTakeControl"/>, Lua s.eqop): this monster
        /// (<paramref name="source"/>, still face-up in <paramref name="who"/>'s Monster Zone and not destroyed by the battle
        /// that flipped it) moves to a free Spell &amp; Trap Zone as an Equip Card equipped to <paramref name="target"/> (still
        /// a face-up monster the opponent controls whose control can change: aux.CheckStealEquip), then <paramref name="who"/>
        /// takes control of it while it stays equipped (DuelEngine.RevertEquipTakeControl returns it when the equip leaves).
        /// </summary>
        public static void EquipThisFromFieldToTarget(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null || source == null) return;
            if (!who.TryFindMonster(source, out var mi) || !source.FaceUp)
            {
                engine.Log($"{source.Name} is no longer face-up in its Monster Zone — it is not equipped.");
                return;
            }

            // Lua not c:IsStatus(STATUS_BATTLE_DESTROYED).
            if (ReferenceEquals(engine.FlipSourceBattleDestroyPending, source) || engine.IsBattleDestroyPending(source) ||
                engine.IsDeferredBattleDestroy(source))
            {
                engine.Log($"{source.Name} was destroyed by battle — it is not equipped.");
                return;
            }

            var opp = engine.OpponentOf(who);
            if (target == null || opp == null || !target.FaceUp || !ReferenceEquals(engine.ControllerOf(target), opp) ||
                !opp.TryFindMonster(target, out _))
            {
                engine.Log($"{source.Name}: the target is no longer a face-up monster your opponent controls — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            // aux.CheckStealEquip: IsControlerCanBeChanged — otherwise nothing is equipped.
            if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, target))
            {
                engine.Log($"{source.Name}: control of {target.Name} cannot change — it is not equipped.");
                return;
            }

            var zi = engine.FirstEmptySpellTrap(who);
            if (zi < 0)
            {
                engine.Log($"{source.Name}: no free Spell & Trap Zone — it is not equipped.");
                return;
            }

            // It leaves its Monster Zone but stays on the field (Kiseitai's move): its own Equip Cards lose their monster.
            engine.UnlinkEquipsForZoneMove(who, source);
            who.MonsterZones[mi].Occupant = null;
            who.SpellTrapZones[zi].Occupant = source;
            source.FaceUp = true;
            source.SetThisTurn = false;
            source.ClearAttackFlags();
            source.EquippedTo = target;
            source.EquipTurnCounter = 0;
            source.Counters = 0;
            if (!target.Equips.Contains(source))
                target.Equips.Add(source);
            engine.Log($"{source.Name} is equipped to {target.Name} as an Equip Card.");
            // Duel.Equip (EVENT_EQUIP): the shared equip bookkeeping and event.
            EquipFamilyRuntime.NoteEquipped(engine, source, target);
            // EquipModel batch: equipped while Equip Card effects are negated — control follows once that negation ends.
            if (ContinuousProtections.EquipCardEffectsAreNegated(engine, source))
            {
                engine.Log($"{source.Name}: its Equip Card effect is negated — no control change for now.");
                EquipModelRuntime.SuspendControl(engine, source, target);
                engine.NotifyPublic();
                return;
            }

            target.TakenByEquipControl = true;
            if (!engine.TryTakeControl(who, target))
            {
                target.TakenByEquipControl = false;
                engine.Log($"{source.Name}: take control failed — no Monster Zone.");
            }

            engine.NotifyPublic();
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor: a <see cref="EffectClause.RequiresThisIsEquipCard"/> clause applies only
        /// while <paramref name="card"/> is a face-up Equip Card in <paramref name="who"/>'s Spell &amp; Trap Zone (Brain
        /// Jacker as a monster in its Monster Zone, or in the GY, gives nothing). Equipped, it is an Equip Spell Card
        /// (ocgcore Duel.Equip: EFFECT_CHANGE_TYPE TYPE_EQUIP+TYPE_SPELL; SpiritsAndMiscRuntime.IsMonsterTreatedAsEquipSpell),
        /// so Equip Card / Spell negation on the field stops it, as Kiseitai's gain.
        /// </summary>
        public static bool SkipsPhaseClause(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (c == null || !c.RequiresThisIsEquipCard) return false;
            if (engine == null || who == null || card == null || !card.FaceUp || card.EquippedTo == null ||
                !who.TryFindSpellTrap(card, out _))
                return true;
            if (ContinuousProtections.NegateAllEquipCardEffectsOnField(engine) ||
                ContinuousNegationRuntime.SpellEffectsOnFieldNegatedBy(engine, card) != null)
            {
                engine.Log($"{card.Name}: its Equip Card effect is negated.");
                return true;
            }

            return false;
        }
    }
}
