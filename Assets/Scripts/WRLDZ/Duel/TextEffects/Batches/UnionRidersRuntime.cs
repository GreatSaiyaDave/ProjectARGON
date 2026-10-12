using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// UnionRiders batch (v175): the Union state and the Union riders (<see cref="UnionRidersAtoms"/>).
    /// <para>
    /// <b>Union state</b> (ygopro aux.IsUnionState / EFFECT_UNION_STATUS): a Union monster equipped by its own Union effect.
    /// A Union equipped some other way — taken by Union Rider (<see cref="CardInstance.AbsorbedBy"/>), or a monster equipped
    /// with only a granted effect — is not in its Union state: it cannot unequip itself, its Union text (stat bonus, riders,
    /// destroy-instead) does not apply.
    /// </para>
    /// <list type="bullet">
    /// <item>Union-state ignitions (<see cref="EffectTiming.UnionStateIgnition"/>: Kiryu, Second Goblin): activated by tapping the
    /// equipped Union in the Spell &amp; Trap Zone (<see cref="TryCanActivate"/>, <see cref="TryActivate"/>). When its own unequip
    /// is usable too, a human chooses in the card picker: the equipped monster stands for the rider, the Union itself for the
    /// unequip; CANCEL activates neither. Like the v167 Union procedure, the effect resolves as it is activated.</item>
    /// <item>Battle-damage rider (<see cref="EffectTiming.EquippedMonsterInflictsBattleDamage"/>: Burning / Freezing Beast):
    /// mandatory, targets as it activates (a human picks; CANCEL makes the automatic pick), re-checked at resolution.</item>
    /// <item>Lunatite: <see cref="ShieldsFromOpponentSpell"/> (read by <see cref="ContinuousProtections.IsUnaffectedBy"/>);
    /// its no-name procedure through <see cref="FilterTargets"/> and <see cref="AnyHostAllows"/>.</item>
    /// <item>Union Rider: <see cref="TakeControlAndEquip"/>; activation condition <see cref="NoUnionEquippedByThisEffect"/>.</item>
    /// </list>
    /// </summary>
    public static class UnionRidersRuntime
    {
        /// <summary>ActivationCondition: Union Rider's eqcon — no Union equipped by its effect yet, and a free S/T Zone.</summary>
        public const string NoUnionEquippedByThisEffect = "UnionRiders.NoUnionEquippedByThisEffect";

        // ───────────────────────────── Union state ─────────────────────────────

        /// <summary>A Union Monster Card (printed type), wherever it is.</summary>
        public static bool IsUnionMonster(CardInstance c) =>
            c?.Def != null && c.Def.IsMonster &&
            (c.Def.type ?? "").IndexOf("Union", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Equipped by its own Union effect (or Formation Union-style "as an Equip Spell Card" Union equips): equipped, not
        /// absorbed by another card's effect (Union Rider), not a granted-effect-only equip.
        /// </summary>
        public static bool InUnionState(CardInstance c) =>
            IsUnionMonster(c) && c.EquippedTo != null && c.AbsorbedBy == null && !c.IsEquipSpellWithGrantedEffectOnly;

        /// <summary>
        /// The Union's effects are negated: the card itself (Imperial Order / Spell Canceller treat it as the Equip Spell it
        /// is), Armored Glass, or every Equip Card's effects (The Emperor's Holiday).
        /// </summary>
        static bool UnionEffectsNegated(DuelEngine engine, CardInstance union) =>
            union == null || union.IsNegated || ContinuousProtections.EquipCardEffectsAreNegated(engine, union);

        /// <summary>A Union Monster Card equipped but not in its Union state: its Union text does not apply.</summary>
        public static bool UnionTextOff(CardInstance equip) =>
            IsUnionMonster(equip) && equip.EquippedTo != null && !InUnionState(equip);

        /// <summary>
        /// DuelEngine.TryDestroyEquipInsteadOfBattle: the printed Union rule "If the equipped monster would be destroyed by
        /// battle, destroy this card instead" (aux.AddUnionProcedure's EFFECT_DESTROY_SUBSTITUTE, aux.IsUnionState). Every
        /// Union whose procedure compiled (it has the unequip clause) and whose text prints the rule.
        /// </summary>
        public static bool SubstitutesForBattleDestruction(DuelEngine engine, CardInstance equip)
        {
            if (!InUnionState(equip) || UnionEffectsNegated(engine, equip)) return false;
            var prog = CompiledEffectCache.GetOrCompile(equip.Def);
            if (prog == null || !prog.ClauseList.Exists(c =>
                    c != null && c.Action == EffectActionKind.UnequipThisSpecialSummon))
                return false;
            var desc = equip.Def.desc ?? "";
            return desc.IndexOf("destroy this card instead", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ───────────────────────────── Union-state ignitions (Kiryu, Second Goblin) ─────────────────────────────

        sealed class RiderUse
        {
            public int Turn;
            public int Stamp;
        }

        static readonly ConditionalWeakTable<CardInstance, RiderUse> s_riderUsed = new();

        /// <summary>Set while the shell's own unequip path runs from here, so the hooks below stand aside.</summary>
        [ThreadStatic] static bool s_bypass;

        static EffectClause RiderClause(CompiledCardProgram prog) =>
            prog == null || !prog.FullyCompiled
                ? null
                : prog.ClausesFor(EffectTiming.UnionStateIgnition).FirstOrDefault(c => c != null);

        static bool IsUnionStateCard(DuelistState who, CardInstance card, bool fromHand, CompiledCardProgram prog) =>
            !fromHand && who != null && RiderClause(prog) != null && InUnionState(card) && card.FaceUp &&
            who.TryFindSpellTrap(card, out _);

        static bool RiderUsedThisTurn(DuelEngine engine, CardInstance card) =>
            s_riderUsed.TryGetValue(card, out var u) && u.Turn == engine.TurnNumber && u.Stamp == card.FieldEntryStamp;

        static void MarkRiderUsed(DuelEngine engine, CardInstance card)
        {
            s_riderUsed.Remove(card);
            s_riderUsed.Add(card, new RiderUse { Turn = engine.TurnNumber, Stamp = card.FieldEntryStamp });
        }

        /// <summary>The equipped monster, still a face-up monster on the field.</summary>
        static CardInstance LiveHost(DuelEngine engine, CardInstance union)
        {
            var host = union?.EquippedTo;
            var ctrl = host != null ? engine.ControllerOf(host) : null;
            return ctrl != null && ctrl.TryFindMonster(host, out _) && host.FaceUp ? host : null;
        }

        /// <summary>The rider can be activated now (Spell Speed 1 ignition: open state of your own Main Phase).</summary>
        static bool RiderUsable(DuelEngine engine, DuelistState who, CardInstance card, EffectClause rider,
            out string reason)
        {
            reason = null;
            if (engine.GameOver)
            {
                reason = "The Duel is over.";
                return false;
            }

            if (engine.PendingResponse != null || engine.PendingActivation != null || engine.HasDeclaredAttack)
            {
                reason = "Respond or pass the open window first.";
                return false;
            }

            // Spell Speed 1: Chain Link 1 only (its own link seated by TryActivateSpellTrap is allowed).
            if (engine.IsResolvingChain || engine.Chain.IsResolving ||
                (engine.Chain.HasLinks &&
                 !(engine.Chain.Count == 1 && engine.Chain.Last.Card == card && !engine.Chain.Last.Resolved)))
            {
                reason = "A Spell Speed 1 effect can only be Chain Link 1.";
                return false;
            }

            if (engine.TurnPlayer != who || !engine.InMainPhase)
            {
                reason = "Only during your Main Phase.";
                return false;
            }

            if (UnionEffectsNegated(engine, card))
            {
                reason = "This card's effects are negated.";
                return false;
            }

            if (LiveHost(engine, card) == null)
            {
                reason = "No face-up equipped monster.";
                return false;
            }

            if (rider.OncePerTurn && RiderUsedThisTurn(engine, card))
            {
                reason = "Already used this effect this turn.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// TextEffectRuntime.CanActivate hook. Handled (true) with <paramref name="ok"/> = true when <paramref name="card"/> is
        /// a Union in its Union state in <paramref name="who"/>'s Spell &amp; Trap Zone and its Union-state ignition can be
        /// activated now; handled with <paramref name="ok"/> = false for an equipped Union that is not in its Union state
        /// (taken by Union Rider: none of its effects can be activated). Otherwise not handled and the ordinary checks go on
        /// (the shell's unequip).
        /// </summary>
        public static bool TryCanActivate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            CompiledCardProgram prog, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (s_bypass || engine == null || fromHand) return false;
            if (UnionTextOff(card))
            {
                reason = "This Union Monster is not equipped by its own effect: its effects cannot be activated.";
                return true;
            }

            if (!IsUnionStateCard(who, card, fromHand, prog)) return false;
            if (!RiderUsable(engine, who, card, RiderClause(prog), out reason)) return false;
            reason = "OK";
            ok = true;
            return true;
        }

        /// <summary>The shell's own unequip could be activated now (the ordinary checks, without this batch's hooks).</summary>
        static bool UnequipUsable(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog)
        {
            if (!prog.ClausesFor(EffectTiming.Activate).Exists(c =>
                    c != null && c.Action == EffectActionKind.UnequipThisSpecialSummon))
                return false;
            s_bypass = true;
            try
            {
                return TextEffectRuntime.CanActivate(engine, who, card, false, prog, out _);
            }
            finally
            {
                s_bypass = false;
            }
        }

        static bool RunUnequip(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog,
            bool autoPick)
        {
            s_bypass = true;
            try
            {
                return TextEffectRuntime.TryResolveActivation(engine, who, card, false, prog, autoPick);
            }
            finally
            {
                s_bypass = false;
            }
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveActivation hook. Returns false (not handled) unless <paramref name="card"/> is a Union in
        /// its Union state whose ignition can be activated now. Alone, the ignition resolves; with the unequip also usable,
        /// a human chooses (the equipped monster = the ignition, this card = unequip; CANCEL undoes the activation) and the
        /// AI takes the ignition.
        /// </summary>
        public static bool TryActivate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            CompiledCardProgram prog, bool autoPick, out bool ok)
        {
            ok = false;
            if (s_bypass || engine == null || engine.PendingResponse != null ||
                !IsUnionStateCard(who, card, fromHand, prog))
                return false;
            var rider = RiderClause(prog);
            if (!RiderUsable(engine, who, card, rider, out _)) return false;
            if (!UnequipUsable(engine, who, card, prog))
            {
                ok = ResolveRider(engine, who, card, rider);
                return true;
            }

            if (autoPick || !engine.IsHumanControlled(who))
            {
                ok = ResolveRider(engine, who, card, rider);
                return true;
            }

            var host = card.EquippedTo;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () =>
                {
                    var list = new List<CardInstance>();
                    if (InUnionState(card) && card.EquippedTo == host && LiveHost(engine, card) != null)
                    {
                        list.Add(host);
                        list.Add(card);
                    }

                    return list;
                },
                Kind = EffectTargetKind.AnyCardOnField,
                Prompt = $"{card.Name}: tap {host?.Name} to {Describe(rider)}, or tap {card.Name} to unequip it and " +
                         "Special Summon it. CANCEL activates neither.",
                AiChoose = list => list.FirstOrDefault(),
                Finish = pick =>
                {
                    if (pick == null)
                        engine.DropUnresolvedLink(card);
                    else if (pick == card)
                        RunUnequip(engine, who, card, prog, false);
                    else
                        ResolveRider(engine, who, card, rider);
                    engine.ClearIdleChain();
                    engine.NotifyPublic();
                },
                Cancel = () =>
                {
                    engine.DropUnresolvedLink(card);
                    engine.Log($"{card.Name}: no effect is activated.");
                    engine.NotifyPublic();
                },
                DuringActivation = true
            });
            ok = true;
            return true;
        }

        static string Describe(EffectClause rider) =>
            rider?.Action switch
            {
                EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly =>
                    "Tribute this card so that monster can attack directly this turn",
                EffectActionKind.ChangeEquippedMonsterBattlePosition => "change its battle position",
                _ => "apply this card's effect"
            };

        static bool ResolveRider(DuelEngine engine, DuelistState who, CardInstance card, EffectClause rider)
        {
            var host = LiveHost(engine, card);
            if (host == null) return false;
            switch (rider.Action)
            {
                case EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly:
                {
                    engine.Log($"Activate: {card.Name} (effect).");
                    // Cost (Lua s.atkcost): the equipped monster is remembered (SetTargetCard), then this card is Tributed.
                    var holder = engine.ControllerOf(card) ?? who;
                    engine.SendCardToGrave(holder, card);
                    engine.Log($"Cost: Tribute {card.Name}.");
                    // Lua s.atkop: tc:IsFaceup() and tc:IsRelateToEffect(e).
                    var hostCtrl = engine.ControllerOf(host);
                    if (hostCtrl == null || !hostCtrl.TryFindMonster(host, out _) || !host.FaceUp)
                    {
                        engine.Log($"{card.Name}: {host.Name} is no longer a face-up monster — no effect.");
                        break;
                    }

                    if (ContinuousProtections.IsUnaffectedBy(engine, host, card))
                    {
                        engine.Log($"{host.Name} is unaffected by {card.Name}.");
                        break;
                    }

                    host.DirectAttackThisTurn = true;
                    engine.Log($"{host.Name} can attack directly this turn.");
                    break;
                }
                case EffectActionKind.ChangeEquippedMonsterBattlePosition:
                {
                    engine.Log($"Activate: {card.Name} (effect).");
                    MarkRiderUsed(engine, card);
                    if (ContinuousProtections.IsUnaffectedBy(engine, host, card))
                    {
                        engine.Log($"{host.Name} is unaffected by {card.Name}.");
                        break;
                    }

                    var to = host.Position == BattlePosition.Attack ? BattlePosition.Defense : BattlePosition.Attack;
                    engine.ApplyEffectPositionChange(host, to, true);
                    engine.Log($"{card.Name}: {host.Name} is changed to face-up {(to == BattlePosition.Attack ? "Attack" : "Defense")} Position.");
                    break;
                }
                default:
                    return false;
            }

            FieldSpellEffects.RefreshBoard(engine);
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── Battle-damage rider (Burning Beast, Freezing Beast) ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifyInflictedBattleDamage: <paramref name="dealer"/> (controlled by <paramref name="who"/>) just
        /// inflicted battle damage to <paramref name="damaged"/>. Each Union in its Union state equipped to it, not negated,
        /// whose controller's opponent took the damage, activates its mandatory targeted trigger.
        /// </summary>
        public static void FireEquippedMonsterInflictedBattleDamage(DuelEngine engine, DuelistState who,
            DuelistState damaged, CardInstance dealer)
        {
            if (engine == null || damaged == null || dealer?.Equips == null || dealer.Equips.Count == 0) return;
            foreach (var eq in dealer.Equips.ToList())
            {
                if (!InUnionState(eq) || eq.EquippedTo != dealer || !eq.FaceUp || UnionEffectsNegated(engine, eq)) continue;
                var eqCtrl = engine.ControllerOf(eq);
                if (eqCtrl == null || !eqCtrl.TryFindSpellTrap(eq, out _) || engine.OpponentOf(eqCtrl) != damaged)
                    continue;
                var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.EquippedMonsterInflictsBattleDamage))
                    if (c != null && c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice)
                        ActivateDestroyRider(engine, eqCtrl, eq, c);
            }
        }

        /// <summary>The cards the rider may target: face-up Spells/Traps on the field, or face-down S/T Zone cards.</summary>
        static List<CardInstance> RiderTargets(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var list = new List<CardInstance>();
            var opp = engine.OpponentOf(who);
            foreach (var side in new[] { who, opp })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                    if (st != null && RiderTargetOk(c, st)) list.Add(st);
                // Burning Beast: LOCATION_ONFIELD — a face-up Field Spell counts (a Field Spell is never Set here).
                var fs = side.FieldSpellZone?.Occupant;
                if (fs != null && c.RequiresTargetFaceUp && RiderTargetOk(c, fs)) list.Add(fs);
            }

            return list;
        }

        static bool RiderTargetOk(EffectClause c, CardInstance t) =>
            t?.Def != null && (c.RequiresTargetFaceUp ? t.FaceUp : !t.FaceUp);

        static void ActivateDestroyRider(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var what = c.RequiresTargetFaceUp ? "face-up Spell/Trap Card" : "face-down Spell/Trap Card";
            var now = RiderTargets(engine, who, source, c);
            engine.Log($"{source.Name}: the equipped monster inflicted battle damage — destroy 1 {what} on the field.");
            if (now.Count == 0)
            {
                engine.Log($"{source.Name}: no {what} on the field — no effect.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => RiderTargets(engine, who, source, c),
                Kind = TextEffectRuntime.TargetKindOf(c),
                Prompt = $"{source.Name}: target 1 {what} on the field to destroy.",
                AiChoose = list => AiRiderPick(engine, who, list),
                Finish = pick => ResolveDestroyRider(engine, who, source, c, pick)
            });
        }

        /// <summary>The AI destroys an opponent's card first, then a card of its own that is not this Union.</summary>
        static CardInstance AiRiderPick(DuelEngine engine, DuelistState who, List<CardInstance> list)
        {
            if (list == null || list.Count == 0) return null;
            var opp = engine.OpponentOf(who);
            return list.FirstOrDefault(t => engine.ControllerOf(t) == opp) ?? list[0];
        }

        static void ResolveDestroyRider(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance pick)
        {
            if (pick == null) return;
            var ctrl = engine.ControllerOf(pick);
            var onField = ctrl != null && (ctrl.TryFindSpellTrap(pick, out _) || ctrl.FieldSpellZone?.Occupant == pick);
            // Lua s.desop: tc:IsFaceup() (Burning) / tc:IsFacedown() (Freezing) and tc:IsRelateToEffect(e).
            if (!onField || !RiderTargetOk(c, pick))
            {
                engine.Log($"{source.Name}: {pick.Name} is no longer a legal target — no effect.");
                engine.NotifyPublic();
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, pick, source))
            {
                engine.Log("Target is protected at resolution.");
                engine.NotifyPublic();
                return;
            }

            engine.Log($"{source.Name}: destroys {(pick.FaceUp ? pick.Name : "a Set card")}.");
            TextEffectRuntime.DestroyCard(engine, pick, source, false, who);
            TextEffectRuntime.NotifyTargetingEffectsResolved(engine, new[] { pick }, TargetingTruthRuntime.ClauseTargets(c));
            engine.NotifyPublic();
        }

        // ───────────────────────────── Lunatite ─────────────────────────────

        /// <summary>
        /// ContinuousProtections.IsUnaffectedBy: <paramref name="victim"/> is equipped with a Union in its Union state whose
        /// program has <see cref="EffectActionKind.EquippedUnaffectedByOpponentSpellEffects"/>, and <paramref name="source"/>
        /// is a Spell Card controlled by that Union's controller's opponent.
        /// </summary>
        public static bool ShieldsFromOpponentSpell(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (engine == null || victim?.Equips == null || victim.Equips.Count == 0 || source?.Def == null) return false;
            if (!source.Def.IsSpell || source.Def.IsTrap || source.Def.IsMonster) return false;
            foreach (var eq in victim.Equips)
            {
                if (!InUnionState(eq) || eq.EquippedTo != victim || UnionEffectsNegated(engine, eq)) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null || !prog.FullyCompiled || !prog.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.EquippedUnaffectedByOpponentSpellEffects))
                    continue;
                var eqCtrl = engine.ControllerOf(eq);
                var srcCtrl = HolderOf(engine, source);
                if (eqCtrl != null && srcCtrl != null && srcCtrl == engine.OpponentOf(eqCtrl))
                    return true;
            }

            return false;
        }

        /// <summary>The Spell's controller: where it is on the field, else the player whose GY / hand holds it.</summary>
        static DuelistState HolderOf(DuelEngine engine, CardInstance card)
        {
            var ctrl = engine.ControllerOf(card);
            if (ctrl != null) return ctrl;
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side != null && (side.Graveyard.Contains(card) || side.Hand.Contains(card)))
                    return side;
            return null;
        }

        /// <summary>
        /// EquipFamilyRuntime.IsCorrectEquipTarget: a Union with no named host (Lunatite) may be equipped to any face-up
        /// monster its controller controls (Lua aux.AddUnionProcedure(c,nil): the UnionLimit filter is nil).
        /// </summary>
        public static bool AnyHostAllows(DuelEngine engine, EffectClause union, CardInstance equip, CardInstance monster) =>
            union != null && union.UnionEquipsAnyFaceUpMonster && monster != null && monster != equip && monster.FaceUp &&
            engine != null && engine.ControllerOf(monster) != null && engine.ControllerOf(monster) == engine.ControllerOf(equip);

        // ───────────────────────────── Union Rider ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CollectTargets tail: Union Rider takes only a face-up Union Monster; Lunatite's no-name equip never
        /// targets itself; Combination Attack takes a face-up monster that declared an attack this turn and has a Union in
        /// its Union state that <paramref name="who"/> can Special Summon (Lua s.tgfilter).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance except,
            List<CardInstance> list)
        {
            if (c == null || list == null) return;
            if (c.Action == EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis)
                list.RemoveAll(t => t == null || !t.FaceUp || !IsUnionMonster(t));
            if (c.Action == EffectActionKind.EquipThisToTarget && c.UnionEquipsAnyFaceUpMonster)
                list.RemoveAll(t => t == null || ReferenceEquals(t, except) || !t.FaceUp);
            if (c.Action == EffectActionKind.SpecialSummonTargetsUnionsThenItAttacksAgain)
                list.RemoveAll(t => !CombinationAttackTarget(engine, who, t));
        }

        // ───────────────────────────── Combination Attack ─────────────────────────────

        /// <summary>The Unions in their Union state equipped to <paramref name="host"/> that <paramref name="who"/> can Special Summon.</summary>
        static List<CardInstance> SummonableUnions(DuelEngine engine, DuelistState who, CardInstance host)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || host?.Equips == null) return list;
            foreach (var u in host.Equips)
            {
                if (!InUnionState(u) || u.EquippedTo != host) continue;
                var ctrl = engine.ControllerOf(u);
                if (ctrl == null || !ctrl.TryFindSpellTrap(u, out _)) continue;
                if (RuleLocksRuntime.SpecialSummonLocked(engine, who, u)) continue;
                // Rivalry of Warlords (SummonLocksAndStatRules): a face-up summon of another Type is not possible.
                if (SummonLocksAndStatRulesRuntime.BlocksFaceUpSummon(engine, ctrl, u, out _)) continue;
                list.Add(u);
            }

            return list;
        }

        /// <summary>
        /// Lua s.tgfilter: a face-up monster on the field that declared an attack this turn, with at least one Union in its
        /// Union state to Special Summon, and enough free Monster Zones on each Union's controller's side.
        /// </summary>
        static bool CombinationAttackTarget(DuelEngine engine, DuelistState who, CardInstance t)
        {
            if (engine == null || t == null || !t.FaceUp || t.AttacksDeclaredThisTurn <= 0) return false;
            var unions = SummonableUnions(engine, who, t);
            if (unions.Count == 0) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                var need = unions.Count(u => engine.ControllerOf(u) == side);
                if (need > 0 && side.MonsterZones.Count(z => z.Occupant == null) < need) return false;
            }

            return true;
        }

        static CardInstance PositionToken(string name, int value) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new WRLDZ.Data.CardDef
            {
                id = 0,
                name = name,
                type = CallsDiceAndSetTriggersRuntime.OptionTokenKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = value,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        /// <summary>
        /// Combination Attack resolves (Lua s.activate): the target must still be face-up (IsRelateToEffect, IsFaceup). Its
        /// Unions in their Union state are Special Summoned to their controllers' fields in face-up Attack or Defense
        /// Position — <paramref name="who"/> chooses each position (the AI: the higher of ATK / DEF); if at least one was
        /// Special Summoned, the target gains extra attacks equal to the attacks it declared this turn.
        /// </summary>
        internal static void ResolveCombinationAttack(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null || target == null) return;
            var name = source?.Name ?? "Combination Attack";
            var tCtrl = engine.ControllerOf(target);
            if (tCtrl == null || !tCtrl.TryFindMonster(target, out _) || !target.FaceUp)
            {
                engine.Log($"{name}: its target is no longer a face-up monster on the field — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var unions = SummonableUnions(engine, who, target);
            if (unions.Count == 0)
            {
                engine.Log($"{name}: {target.Name} has no Union Monster to Special Summon — no effect.");
                return;
            }

            var positions = new List<BattlePosition>();
            AskPositions(engine, who, source, target, unions, positions, 0);
        }

        static void AskPositions(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target,
            List<CardInstance> unions, List<BattlePosition> positions, int i)
        {
            if (i >= unions.Count)
            {
                FinishCombinationAttack(engine, who, source, target, unions, positions);
                return;
            }

            var u = unions[i];
            var aiPos = (u.Def?.atk ?? 0) >= (u.Def?.def ?? 0) ? BattlePosition.Attack : BattlePosition.Defense;
            var options = new List<CardInstance>
            {
                PositionToken("Face-up Attack Position", 0),
                PositionToken("Face-up Defense Position", 1)
            };
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => options,
                Kind = EffectTargetKind.None,
                Prompt = $"{source?.Name}: Special Summon {u.Name} — choose its position.",
                AiChoose = list => list.ElementAtOrDefault(aiPos == BattlePosition.Defense ? 1 : 0),
                Finish = chosen =>
                {
                    positions.Add(chosen?.Def?.level == 1 ? BattlePosition.Defense : BattlePosition.Attack);
                    AskPositions(engine, who, source, target, unions, positions, i + 1);
                }
            });
        }

        static void FinishCombinationAttack(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target, List<CardInstance> unions, List<BattlePosition> positions)
        {
            var name = source?.Name ?? "Combination Attack";
            var summoned = 0;
            for (var i = 0; i < unions.Count; i++)
            {
                var u = unions[i];
                var ctrl = engine.ControllerOf(u);
                // Still the same Union in its Union state on the same monster (the position prompt may have waited).
                if (ctrl == null || !InUnionState(u) || u.EquippedTo != target || !ctrl.TryFindSpellTrap(u, out var si))
                    continue;
                if (engine.FirstEmpty(ctrl.MonsterZones) < 0) continue;
                target.Equips.Remove(u);
                u.EquippedTo = null;
                ctrl.SpellTrapZones[si].Occupant = null;
                var pos = i < positions.Count ? positions[i] : BattlePosition.Attack;
                if (engine.SpecialSummonToField(ctrl, u, pos, true))
                {
                    summoned++;
                    engine.Log($"{name}: {u.Name} is Special Summoned in face-up {(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                }
                else
                {
                    // Not summoned: it stays an Equip Card on the same monster.
                    ctrl.SpellTrapZones[si].Occupant = u;
                    u.EquippedTo = target;
                    if (!target.Equips.Contains(u)) target.Equips.Add(u);
                }
            }

            FieldSpellEffects.RefreshBoard(engine);
            var tCtrl = engine.ControllerOf(target);
            if (summoned == 0 || tCtrl == null || !tCtrl.TryFindMonster(target, out _) || !target.FaceUp)
            {
                engine.NotifyPublic();
                return;
            }

            // EFFECT_EXTRA_ATTACK, value = tc:GetAttackAnnouncedCount(), RESETS_STANDARD_PHASE_END.
            var n = Math.Max(1, target.AttacksDeclaredThisTurn);
            BattleBoostsRuntime.GrantExtraAttack(engine, tCtrl, target, new EffectClause { Amount = n });
            engine.NotifyPublic();
        }

        /// <summary>TextEffectRuntime.ActivationConditionsMet chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c?.ActivationCondition != NoUnionEquippedByThisEffect) return false;
            if (TextEffectRuntime.AbsorbedEquipOf(card) != null)
            {
                reason = "A Union Monster is already equipped to this card by its effect.";
                return true;
            }

            if (engine == null || who == null || engine.FirstEmptySpellTrap(who) < 0)
            {
                reason = "No free Spell & Trap Zone.";
                return true;
            }

            ok = true;
            return true;
        }

        /// <summary>
        /// Union Rider resolves (Lua s.eqop: tc:IsFaceup() and tc:IsRelateToEffect(e); EquipByEffectAndLimitRegister): the
        /// target leaves its controller's Monster Zone (its own Equip Cards are sent to the GY) and becomes an Equip Card in
        /// <paramref name="who"/>'s Spell &amp; Trap Zone, equipped to <paramref name="source"/>. It keeps its owner (it goes to
        /// its owner's GY when it leaves the field) and is not in its Union state.
        /// </summary>
        internal static void TakeControlAndEquip(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null || source == null || target == null) return;
            var holder = engine.ControllerOf(target);
            var mi = -1;
            if (holder == null || holder == who || !holder.TryFindMonster(target, out mi) || !target.FaceUp ||
                !IsUnionMonster(target))
            {
                engine.Log($"{source.Name}: its target is no longer a face-up Union Monster your opponent controls — no effect.");
                return;
            }

            if (!who.TryFindMonster(source, out _) || !source.FaceUp)
            {
                engine.Log($"{source.Name} is no longer a face-up monster on the field — no effect.");
                return;
            }

            if (TextEffectRuntime.AbsorbedEquipOf(source) != null)
            {
                engine.Log($"{source.Name}: already equipped with a Union by its effect — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var zi = engine.FirstEmptySpellTrap(who);
            if (zi < 0)
            {
                engine.Log($"{source.Name}: no free Spell & Trap Zone — {target.Name} stays where it is.");
                return;
            }

            engine.UnlinkEquipsForZoneMove(holder, target);
            holder.MonsterZones[mi].Occupant = null;
            who.SpellTrapZones[zi].Occupant = target;
            target.FaceUp = true;
            // Owner bookkeeping (DuelEngine.RecordControlMove): remember the owner while someone else controls it.
            if (target.ControlOwner == null)
                target.ControlOwner = holder;
            else if (target.ControlOwner == who)
                target.ControlOwner = null;
            target.ControlRevertsAtEndPhase = false;
            target.TakenByEquipControl = false;
            target.EquippedTo = source;
            target.AbsorbedBy = source;
            target.GrantedEquipHost = null;
            target.EquipTurnCounter = 0;
            target.Counters = 0;
            if (!source.Equips.Contains(target))
                source.Equips.Add(target);
            engine.Log($"{source.Name} takes control of {target.Name} and equips it to itself.");
            EquipFamilyRuntime.NoteEquipped(engine, target, source);
            FieldSpellEffects.RefreshBoard(engine);
        }
    }
}
