using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipFamily batch runtime: Equip Cards that change their monster's Type or Attribute, the equip limit of an
    /// Equip Card (<see cref="IsCorrectEquipTarget"/>, Lua CheckEquipTarget) and moving Equip Cards to another
    /// monster (Collected Power). Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Type.</b> <see cref="EffectClause.EquipChangesRaceTo"/> (Dragonic Attack): while the Equip Card is
    /// face-up and its effects apply, the equipped monster is that Type. <see cref="TypeDeclarationRuntime.CurrentRace"/>
    /// layers it with DNA Surgery by timestamp: an Equip Card's timestamp is the moment it was equipped.</item>
    /// <item><b>Attribute.</b> <see cref="EffectClause.EquipDeclaresAnotherAttribute"/> (Scroll of Bewitchment): a
    /// human declares the Attribute with the existing picker (Attribute declaration tokens) as the card is activated,
    /// before its monster is chosen; the monster pick then offers only monsters that do not already have it (the same
    /// pairs Lua's target-then-announce order allows). The AI declares when the card is equipped: an Attribute the
    /// monster does not have. <see cref="CardInstance.Attribute"/> reads it live.</item>
    /// <item><b>Equip limit.</b> <see cref="IsCorrectEquipTarget"/>: the filters of the Equip Card's own equip
    /// clause (Type / Attribute / name, read live), its controller limit (Ring of Magnetism: its controller's
    /// monster; Snatch Steal: a monster its controller does not control), Union hosts, and "only this monster" for a
    /// monster that became an Equip Card by its own or its host's effect (Kiseitai, the Relinquished family).</item>
    /// </list>
    /// </summary>
    public static class EquipFamilyRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Collected Power, Lua chk): a face-up Equip Card is in a
        /// Spell &amp; Trap Zone on either field.
        /// </summary>
        public const string FaceUpEquipCardOnField = "FaceUpEquipCardOnField";

        /// <summary>The classic Attributes, in the order the declaration picker offers them (Lua ATTRIBUTE_ALL).</summary>
        static readonly string[] Attributes = { "EARTH", "WATER", "FIRE", "WIND", "LIGHT", "DARK", "DIVINE" };

        sealed class Memo
        {
            /// <summary>Timestamp of the latest equip (shared with Type declarations).</summary>
            public long Stamp = long.MinValue;
            /// <summary>An Attribute declared while activating, not yet applied (the card is not equipped yet).</summary>
            public string Declared;
            /// <summary>The Attribute this Equip Card gives its monster.</summary>
            public string Attribute;
            public WeakReference<DuelEngine> Engine;
        }

        static readonly ConditionalWeakTable<CardInstance, Memo> s_memo = new();

        // ------------------------------------------------------------------ equip bookkeeping

        /// <summary>
        /// An Equip Card was equipped by its own activation (<c>TextEffectRuntime.EquipCardTo</c>): stamp it, and
        /// apply an Attribute declaration (the AI declares here: an Attribute the monster does not have).
        /// </summary>
        public static void NoteEquipped(DuelEngine engine, CardInstance equip, CardInstance host)
        {
            if (equip == null) return;
            // EquipModel batch: the "a monster was equipped" event (Armored Glass's window).
            EquipModelRuntime.NoteEquipped(engine, equip, host);
            var m = s_memo.GetOrCreateValue(equip);
            m.Stamp = TypeDeclarationRuntime.NextStamp();
            m.Engine = engine != null ? new WeakReference<DuelEngine>(engine) : null;
            if (!DeclaresAttribute(equip))
            {
                m.Attribute = null;
                m.Declared = null;
                return;
            }

            var declared = m.Declared;
            m.Declared = null;
            if (string.IsNullOrEmpty(declared))
                declared = AiAttributeFor(host);
            m.Attribute = declared;
            engine?.Log($"{equip.Name}: {host?.Name ?? "the equipped monster"} becomes {declared}.");
        }

        /// <summary>The Equip Card moved to another monster (Collected Power): a new timestamp, same declaration.</summary>
        static void NoteMoved(DuelEngine engine, CardInstance equip)
        {
            if (equip == null) return;
            var m = s_memo.GetOrCreateValue(equip);
            m.Stamp = TypeDeclarationRuntime.NextStamp();
            m.Engine = engine != null ? new WeakReference<DuelEngine>(engine) : m.Engine;
        }

        static string AiAttributeFor(CardInstance host)
        {
            var current = host?.Attribute;
            foreach (var a in new[] { "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND" })
                if (!string.Equals(a, current, StringComparison.OrdinalIgnoreCase))
                    return a;
            return "DARK";
        }

        static EffectClause EquipClauseOf(CardInstance equip)
        {
            if (equip?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(equip.Def);
            return prog?.ClauseList?.FirstOrDefault(c => c != null && c.Action == EffectActionKind.EquipThisToTarget);
        }

        static bool DeclaresAttribute(CardInstance equip) => EquipClauseOf(equip)?.EquipDeclaresAnotherAttribute == true;

        /// <summary>
        /// The Equip Card's effects apply to its monster: face-up, not negated (Imperial Order, Royal Decree on an
        /// Equip Trap) and Equip Card effects are not negated (The Emperor's Holiday).
        /// </summary>
        static bool EquipEffectsApply(CardInstance eq, Memo m)
        {
            if (eq == null || !eq.FaceUp || eq.IsNegated) return false;
            DuelEngine engine = null;
            if (m?.Engine != null && m.Engine.TryGetTarget(out var e)) engine = e;
            return engine == null || !ContinuousProtections.EquipCardEffectsAreNegated(engine, eq);
        }

        // ------------------------------------------------------------------ live Type / Attribute

        /// <summary>
        /// The Type an Equip Card on <paramref name="card"/> makes it (Dragonic Attack), or null; the latest equipped
        /// one wins. <paramref name="stamp"/> is that equip's timestamp, for layering with DNA Surgery.
        /// </summary>
        public static string EquippedRace(CardInstance card, out long stamp)
        {
            stamp = long.MinValue;
            if (card?.Equips == null || card.Equips.Count == 0) return null;
            string best = null;
            foreach (var eq in card.Equips)
            {
                if (eq == null || eq.EquippedTo != card) continue;
                if (!s_memo.TryGetValue(eq, out var m) || m.Stamp <= stamp) continue;
                var race = EquipClauseOf(eq)?.EquipChangesRaceTo;
                if (string.IsNullOrEmpty(race) || !EquipEffectsApply(eq, m)) continue;
                best = race;
                stamp = m.Stamp;
            }

            return best;
        }

        /// <summary>
        /// <see cref="CardInstance.Attribute"/>: the printed Attribute, unless <paramref name="card"/> is a face-up
        /// monster equipped with an Equip Card that changes its Attribute (Scroll of Bewitchment) whose effects apply;
        /// the latest equipped one wins.
        /// </summary>
        public static string CurrentAttribute(CardInstance card)
        {
            var def = card?.Def;
            if (def == null) return null;
            if (!card.FaceUp || !def.IsMonster) return def.attribute;
            // TokensDeclarationsAndPicks batch: DNA Transplant — every face-up monster on the field has its declared
            // Attribute; with an Attribute-changing Equip too, the change applied last wins.
            var byField = TokensDeclarationsAndPicksRuntime.AttributeOnField(card, out var fieldStamp);
            if (card.Equips.Count == 0) return byField ?? def.attribute;
            var best = byField;
            var bestStamp = byField != null ? fieldStamp : long.MinValue;
            foreach (var eq in card.Equips)
            {
                if (eq == null || eq.EquippedTo != card) continue;
                if (!s_memo.TryGetValue(eq, out var m) || string.IsNullOrEmpty(m.Attribute) || m.Stamp <= bestStamp)
                    continue;
                if (!DeclaresAttribute(eq) || !EquipEffectsApply(eq, m)) continue;
                best = m.Attribute;
                bestStamp = m.Stamp;
            }

            return best ?? def.attribute;
        }

        // ------------------------------------------------------------------ Attribute declaration (activation)

        /// <summary>The activation still needs its Attribute declaration (a human declares before the monster pick).</summary>
        public static bool NeedsAttributeDeclaration(IEnumerable<EffectClause> activate, CardInstance card)
        {
            if (card == null || activate == null) return false;
            if (!activate.Any(c => c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                                   c.EquipDeclaresAnotherAttribute))
                return false;
            return !s_memo.TryGetValue(card, out var m) || string.IsNullOrEmpty(m.Declared);
        }

        /// <summary>
        /// Offer the Attribute declaration through the ordinary picker (one token per Attribute that at least one
        /// legal monster does not have). Returns true when a human is now choosing; the AI declares on equip.
        /// </summary>
        public static bool OfferAttributeDeclaration(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause clause, bool fromHand, bool autoPick)
        {
            if (engine == null || who == null || card == null || clause == null) return false;
            if (autoPick || !who.IsPlayer) return false;
            var monsters = TextEffectRuntime.CollectTargets(engine, who, clause, card);
            var offered = Attributes.Where(a => monsters.Any(t =>
                    !string.Equals(t?.Attribute, a, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (offered.Count == 0) return false; // no monster: the target step refuses the activation

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                PromptOverride = "declare 1 Attribute."
            };
            pending.LegalTargets.AddRange(TypeDeclarationRuntime.AttributeTokens(offered));
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// The human picked the Attribute token: remember it, then continue the activation (the monster pick).
        /// False when <paramref name="pick"/> is not this batch's declaration.
        /// </summary>
        public static bool TryFinishDeclaration(DuelEngine engine, PendingActivation p, CardInstance pick)
        {
            if (engine == null || p?.Card == null || !TypeDeclarationRuntime.IsAttributeToken(pick)) return false;
            if (p.AwaitingDeclaration || !DeclaresAttribute(p.Card)) return false;
            var m = s_memo.GetOrCreateValue(p.Card);
            m.Declared = pick.Def.attribute;
            var who = p.Controller;
            var card = p.Card;
            var fromHand = p.FromHand;
            var paidToll = p.HandActionLpPaid;
            engine.ClearPendingActivation();
            engine.Log($"{card.Name}: declares {m.Declared}.");
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            // Chain Energy (RuleLocks batch, v159-lon-b audit): the hand toll paid as this activation began stays with
            // it — its legality re-check does not ask for it again, the monster pick that follows can refund it
            // (SpellTrapEffects.CancelPending), and it is refunded if the activation is not made.
            var markedPaid = paidToll > 0 && engine.HandActionCostPaid.Add(card);
            bool ok;
            try
            {
                ok = TextEffectRuntime.TryResolveActivation(engine, who, card, fromHand, prog, false);
            }
            finally
            {
                if (markedPaid) engine.HandActionCostPaid.Remove(card);
            }

            if (paidToll > 0 && who != null)
            {
                var next = engine.PendingActivation;
                if (next != null && next.Card == card)
                    next.HandActionLpPaid += paidToll;
                else if (!ok && who.Hand.Contains(card))
                {
                    who.LifePoints += paidToll;
                    ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, who, paidToll);
                    engine.Log($"Chain Energy: {card.Name} was not activated — {paidToll} LP refunded ({who.LifePoints} LP).");
                }
            }

            return ok;
        }

        /// <summary>A declaration made for an activation that was cancelled (or did not equip) is forgotten.</summary>
        public static void ForgetDeclarationIfNotEquipped(CardInstance card)
        {
            if (card == null || card.EquippedTo != null) return;
            if (s_memo.TryGetValue(card, out var m)) m.Declared = null;
        }

        /// <summary>
        /// CollectTargets for an Attribute-declaring equip once the Attribute is declared: only a monster that does
        /// not already have that Attribute (Lua AnnounceAnotherAttribute).
        /// </summary>
        public static void FilterByDeclaredAttribute(EffectClause c, CardInstance source, List<CardInstance> list)
        {
            if (c == null || !c.EquipDeclaresAnotherAttribute || list == null || source == null) return;
            if (!s_memo.TryGetValue(source, out var m) || string.IsNullOrEmpty(m.Declared)) return;
            list.RemoveAll(t => string.Equals(t?.Attribute, m.Declared, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ equip limit

        /// <summary>
        /// Lua CheckEquipTarget: <paramref name="monster"/> is a face-up monster on the field that
        /// <paramref name="equip"/> may be equipped to (its equip limit, not its activation's target rule). The
        /// current host is always correct. Unknown Equip Cards are not correct (fail closed).
        /// </summary>
        public static bool IsCorrectEquipTarget(DuelEngine engine, CardInstance equip, CardInstance monster)
        {
            if (engine == null || equip?.Def == null || monster?.Def == null) return false;
            var mc = engine.ControllerOf(monster);
            if (mc == null || !mc.TryFindMonster(monster, out _) || !monster.FaceUp) return false;
            if (equip.EquippedTo == monster) return true;

            // A monster that became an Equip Card by an effect (Kiseitai, the Relinquished family's absorbed card,
            // Dark Necrofear): its equip limit is its own host only (Lua eqlimit: e:GetOwner()==c).
            // BothSideAndMonsterEquip (v161): one equipped "as an Equip Spell Card with this effect" (Sword Hunter, Lua
            // EquipByEffectLimit) only stays on its own host, even when it is a Union.
            if (equip.IsEquipSpellWithGrantedEffectOnly) return false;
            if (equip.Def.IsMonster)
                return UnionLimitAllows(engine, equip, monster);

            var clause = EquipClauseOf(equip);
            if (clause == null) return false;
            return ClauseLimitAllows(engine, equip, clause, engine.ControllerOf(equip), mc, monster);
        }

        /// <summary>
        /// GuardiansAndEquipEvents batch — Guardian Elma / Chopman the Desperate Outlaw (Lua s.filter: IsType(TYPE_EQUIP) and
        /// CheckEquipTarget(c)): the Equip Spell Card <paramref name="equip"/> in <paramref name="equipController"/>'s GY could
        /// be equipped by that player to <paramref name="monster"/>, a face-up monster on the field. Its equip limit is read
        /// from its own compiled equip clause, with <paramref name="equipController"/> as its controller (a GY card's handler
        /// player); a series limit ("Equip only to a "Ninja" monster") is honoured here. A card that is not FullyCompiled, has
        /// no equip clause (Premature Burial: its limit exists only once activated) or an unknown limit shape fails closed.
        /// </summary>
        public static bool IsCorrectEquipTargetFromGy(DuelEngine engine, DuelistState equipController, CardInstance equip,
            CardInstance monster)
        {
            if (engine == null || equipController == null || equip?.Def == null || monster?.Def == null) return false;
            if (!equip.Def.IsEquipSpell || equip.Def.IsMonster) return false;
            var mc = engine.ControllerOf(monster);
            if (mc == null || !mc.TryFindMonster(monster, out _) || !monster.FaceUp) return false;
            var prog = CompiledEffectCache.GetOrCompile(equip.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clause = EquipClauseOf(equip);
            if (clause == null) return false;
            // "Equip only to a monster your opponent controls": an opponent-only equip zone never fits its own controller's
            // monster.
            if (equipController == mc &&
                clause.Zone is EffectZoneFilter.OppMonsters or EffectZoneFilter.OppFaceUpMonsters or
                    EffectZoneFilter.OppAnyMonsters or EffectZoneFilter.OppAttackPositionMonsters or
                    EffectZoneFilter.OppDefensePositionMonsters or EffectZoneFilter.OppFaceDownMonsters)
                return false;
            return ClauseLimitAllows(engine, equip, clause, equipController, mc, monster);
        }

        /// <summary>
        /// The equip limit of <paramref name="clause"/> (an Equip Card's EquipThisToTarget clause controlled by
        /// <paramref name="ec"/>) allows <paramref name="monster"/>, controlled by <paramref name="mc"/>. On an Equip Spell Card a
        /// <see cref="EffectClause.TargetSeriesName"/> limit ("Equip only to a "Ninja" monster", aux.AddEquipProcedure with
        /// IsSetCard; GuardiansAndEquipEvents batch) is a series check; on any other card (Battle-Scarred, a Continuous Trap
        /// whose series filter is its activation target, not an equip limit) it stays an unknown shape (fail closed).
        /// </summary>
        static bool ClauseLimitAllows(DuelEngine engine, CardInstance equip, EffectClause clause, DuelistState ec,
            DuelistState mc, CardInstance monster)
        {
            if (clause.EquipLimitSameController && ec != mc) return false;
            // Snatch Steal (s.eqlimit): a monster its controller does not control.
            if (clause.TakeControlOfTarget && ec == mc) return false;
            if (!string.IsNullOrEmpty(clause.RaceFilter) &&
                !ClassicEraTemplates.RaceMatches(monster.Race, clause.RaceFilter))
                return false;
            if (!string.IsNullOrEmpty(clause.ExceptRaceFilter) &&
                ClassicEraTemplates.RaceMatches(monster.Race, clause.ExceptRaceFilter))
                return false;
            if (!string.IsNullOrEmpty(clause.AttributeFilter) &&
                !string.Equals(monster.Attribute, clause.AttributeFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            if (clause.RequiresFusionMonsterTarget && (monster?.Def == null || !monster.Def.IsFusionMonster))
                return false;
            if (clause.RequiresRitualMonsterTarget && (monster?.Def == null || !monster.Def.IsRitualMonster))
                return false;
            if (clause.MonsterLevelMax > 0 && (monster == null || monster.Level > clause.MonsterLevelMax))
                return false;
            if (!string.IsNullOrEmpty(clause.NamedCard) && !monster.IsNamed(clause.NamedCard)) return false;
            // ContinuousLocksAndIgnitions batch (v163): Buster Rancher — ATK 1000 or less (except during damage calculation).
            if (ContinuousLocksAndIgnitionsRuntime.BreaksAtkEquipLimit(engine, clause, monster)) return false;
            // Cyber Shield: "Harpie Lady" or "Harpie Lady Sisters" (EquipHostName / AltNamedCard).
            if (!string.IsNullOrEmpty(clause.EquipHostName) && !monster.IsNamed(clause.EquipHostName) &&
                (string.IsNullOrEmpty(clause.AltNamedCard) || !monster.IsNamed(clause.AltNamedCard)))
                return false;
            if (!string.IsNullOrEmpty(clause.TargetSeriesName))
            {
                if (equip?.Def == null || !equip.Def.IsEquipSpell || equip.Def.IsMonster) return false;
                if (!TextEffectRuntime.CardMatchesSeries(monster.Def, clause.TargetSeriesName)) return false;
            }
            return EquipLimitShapeKnown(clause);
        }

        /// <summary>
        /// A monster acting as an Equip Card: a Union may move to another monster its Union text names; every other
        /// one (Kiseitai, an absorbed monster) only stays on its own host.
        /// </summary>
        static bool UnionLimitAllows(DuelEngine engine, CardInstance equip, CardInstance monster)
        {
            if (equip.AbsorbedBy != null) return false;
            // Union (AdvancedEffectTemplates RxUnionEither): EquipThisToTarget naming its host (Lua UnionLimit f).
            var union = EquipClauseOf(equip);
            // UnionRiders batch (v175): Lunatite names no host — any face-up monster its controller controls.
            if (union != null && union.UnionEquipsAnyFaceUpMonster)
            {
                // Heavy Mech Support Platform: the typed Union carries a Type filter instead of a host name, so the
                // "any face-up monster" shape is narrowed here. An empty filter stays Lunatite's any-monster rule.
                if (!string.IsNullOrEmpty(union.RaceFilter))
                    return ClassicEraTemplates.RaceMatches(monster.Race, union.RaceFilter);
                return UnionRidersRuntime.AnyHostAllows(engine, union, equip, monster);
            }
            if (union == null || string.IsNullOrEmpty(union.EquipHostName)) return false;
            return monster.IsNamed(union.EquipHostName);
        }

        /// <summary>
        /// The equip clause carries only limits <see cref="IsCorrectEquipTarget"/> reads. Any other target filter
        /// (an ATK cap, a Level) is not modelled as an equip limit yet: such an Equip Card is not a correct
        /// target for a new monster (fail closed: Collected Power destroys it).
        /// </summary>
        static bool EquipLimitShapeKnown(EffectClause c) =>
            !c.RequiresAttackPosition && !c.AmountIsLevel &&
            !c.AmountIsAtkMax && !c.AmountIsDefMax && !c.RequiresAtkLeqCost && string.IsNullOrEmpty(c.TargetCardKind);

        // ------------------------------------------------------------------ Collected Power

        /// <summary>The face-up Equip Cards on the field: equipped cards in both players' Spell &amp; Trap Zones.</summary>
        public static List<CardInstance> FaceUpEquipCards(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            if (engine == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                    if (IsEquipCardOnField(st))
                        list.Add(st);
            }

            return list;
        }

        /// <summary>
        /// A face-up Equip Card (Lua TYPE_EQUIP) equipped to a monster: an Equip Spell, or a monster card acting as
        /// one (a Union, Kiseitai). Continuous Traps that keep a monster linked (Call of the Haunted, Spellbinding
        /// Circle, Battle-Scarred) are not Equip Cards.
        /// </summary>
        public static bool IsEquipCardOnField(CardInstance card) =>
            card?.Def != null && card.FaceUp && card.EquippedTo != null &&
            (card.Def.IsEquipSpell || card.Def.IsMonster);

        /// <summary>
        /// Collected Power resolves: every face-up Equip Card on the field is equipped to <paramref name="target"/>
        /// when that is a correct target for it, otherwise it is destroyed; all are destroyed when the target is no
        /// longer a face-up monster on the field (Lua s.operation).
        /// </summary>
        public static void ResolveCollectAllEquips(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            var valid = target != null && target.FaceUp && engine.ControllerOf(target) != null &&
                        engine.ControllerOf(target).TryFindMonster(target, out _);
            if (valid && ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                valid = false;
            }

            var destroy = new List<CardInstance>();
            // Control from a moved Snatch Steal changes once every Equip Card has moved (Lua: EFFECT_SET_CONTROL
            // applies in the adjust after Duel.EquipComplete), so a later card's controller limit (Ring of
            // Magnetism) still sees the monster's controller from before the move.
            var controlMoves = new List<(CardInstance equip, CardInstance oldHost)>();
            foreach (var ec in FaceUpEquipCards(engine))
            {
                if (valid && IsCorrectEquipTarget(engine, ec, target))
                {
                    var old = ec.EquippedTo;
                    if (MoveEquip(engine, ec, target, applyControl: false))
                        controlMoves.Add((ec, old));
                }
                else
                    destroy.Add(ec);
            }

            foreach (var (ec, old) in controlMoves)
                ApplyControlMove(engine, ec, old, target);

            if (!valid)
                engine.Log($"{source?.Name ?? "Effect"}: the target is gone — every Equip Card is destroyed.");
            foreach (var ec in destroy)
            {
                if (engine.ControllerOf(ec) == null) continue;
                engine.Log($"{ec.Name} cannot be equipped to that monster — destroyed.");
                TextEffectRuntime.DestroyCard(engine, ec, source, false, who);
            }
        }

        /// <summary>
        /// Move <paramref name="equip"/> (still on the field, keeping its controller) to <paramref name="host"/>.
        /// A control Equip Card (Snatch Steal) gives its former monster back and takes control of the new one.
        /// </summary>
        /// <returns>True when the moved card is a control Equip Card (its control move is still to apply).</returns>
        public static bool MoveEquip(DuelEngine engine, CardInstance equip, CardInstance host, bool applyControl = true)
        {
            if (engine == null || equip == null || host == null) return false;
            var old = equip.EquippedTo;
            if (old == host) return false;
            if (old != null) old.Equips.Remove(equip);
            equip.EquippedTo = host;
            if (!host.Equips.Contains(equip))
                host.Equips.Add(equip);
            NoteMoved(engine, equip);
            engine.Log($"{equip.Name} is now equipped to {host.Name}.");
            // EquipModel batch: an Equip Card equipped to another monster is a new equip (ygopro Duel.Equip, EVENT_EQUIP).
            EquipModelRuntime.NoteEquipped(engine, equip, host);
            var controlEquip = EquipClauseOf(equip)?.TakeControlOfTarget == true;
            if (!controlEquip) return false;
            if (applyControl) ApplyControlMove(engine, equip, old, host);
            return true;
        }

        /// <summary>
        /// A control Equip Card (Snatch Steal) moved from <paramref name="old"/> to <paramref name="host"/>: control of
        /// the old monster returns, and its controller takes control of the new one while its effects apply.
        /// </summary>
        static void ApplyControlMove(DuelEngine engine, CardInstance equip, CardInstance old, CardInstance host)
        {
            var equipController = engine.ControllerOf(equip);
            if (equipController == null) return;
            if (old != null && old.TakenByEquipControl && !old.Equips.Any(e =>
                    e != null && EquipClauseOf(e)?.TakeControlOfTarget == true))
                engine.RevertEquipTakeControlPublic(equipController, equip, old);
            if (equip.EquippedTo != host) return;
            if (ContinuousProtections.EquipCardEffectsAreNegated(engine, equip)) return;
            if (engine.ControllerOf(host) == equipController) return;
            host.TakenByEquipControl = true;
            if (!engine.TryTakeControl(equipController, host))
                engine.Log($"{equip.Name}: take control failed — no zone.");
        }

        /// <summary>AI target choice: this batch's equips go on the AI's own monster (AutoPick, FieldAnyMonster).</summary>
        public static bool AiPrefersOwnMonster(EffectClause c) =>
            c != null && (c.Action == EffectActionKind.EquipAllEquipCardsToTarget ||
                          (c.Action == EffectActionKind.EquipThisToTarget &&
                           (!string.IsNullOrEmpty(c.EquipChangesRaceTo) || c.EquipDeclaresAnotherAttribute)));

        // ------------------------------------------------------------------ Dark Necrofear

        /// <summary>How a monster card reached its owner's GY this turn (Lua tgop flag), and its trigger's use.</summary>
        sealed class DestroyRecord
        {
            public int Turn;
            /// <summary>Destroyed in its owner's Monster Zone (its owner controlled it).</summary>
            public bool InOwnersZone;
            /// <summary>The player whose card destroyed it (battle: the other battler's controller), or null.</summary>
            public DuelistState ReasonPlayer;
            public DuelistState Owner;
            public int FiredTurn = -1;
        }

        static readonly ConditionalWeakTable<CardInstance, DestroyRecord> s_destroyed = new();

        /// <summary>A human's Dark Necrofear target pick waiting for the End Phase's other triggers.</summary>
        sealed class QueuedEquip
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public int Turn;
        }

        sealed class NecrofearState
        {
            public readonly List<QueuedEquip> Queue = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, NecrofearState> s_necro = new();
        static readonly ConditionalWeakTable<PendingActivation, QueuedEquip> s_necroPicks = new();

        /// <summary>DuelEngine.MarkSentFromFieldToGy: any new trip to the GY starts without the destruction record.</summary>
        public static void ForgetDestroyRecord(CardInstance card)
        {
            if (card != null) s_destroyed.Remove(card);
        }

        /// <summary>
        /// DuelEngine.DestroyMonster (sent to the GY): record who destroyed it. Battle: the other battler's
        /// controller; a card effect: the player holding the destroying card (refined by <see cref="NoteDestroyer"/>).
        /// </summary>
        public static void NoteDestroyedToGy(DuelEngine engine, DuelistState controller, DuelistState gyOwner,
            CardInstance card, CardInstance byEffect, bool destroyedByBattle, CardInstance battleDestroyer)
        {
            if (engine == null || card == null) return;
            s_destroyed.Remove(card);
            DuelistState reason = null;
            if (destroyedByBattle)
                reason = (battleDestroyer != null ? HolderOf(engine, battleDestroyer) : null) ??
                         engine.OpponentOf(controller);
            else if (byEffect != null && !ReferenceEquals(byEffect, card))
                reason = HolderOf(engine, byEffect);
            else if (ReferenceEquals(byEffect, card))
                reason = controller;
            s_destroyed.Add(card, new DestroyRecord
            {
                Turn = engine.TurnNumber,
                InOwnersZone = ReferenceEquals(controller, gyOwner),
                ReasonPlayer = reason,
                Owner = gyOwner
            });
        }

        /// <summary>
        /// SpecialSummonEffects batch (v161, Twin-Headed Behemoth): <paramref name="card"/> reached the GY this turn because
        /// it was destroyed in a Monster Zone (battle or card effect), by anyone (Lua REASON_DESTROY + previous location on
        /// the field). A later trip to the GY from the field clears the record (<see cref="ForgetDestroyRecord"/>).
        /// </summary>
        public static bool DestroyedOnFieldThisTurn(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_destroyed.TryGetValue(card, out var r) &&
            r.Turn == engine.TurnNumber && card.SentFromFieldTurnNumber == engine.TurnNumber;

        /// <summary>
        /// TokensDeclarationsAndPicks batch (Balloon Lizard): the player whose card destroyed <paramref name="card"/> this
        /// turn (ygopro reason player: the other battler's controller, or the destroying effect's player), or null.
        /// </summary>
        public static DuelistState DestroyReasonPlayer(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_destroyed.TryGetValue(card, out var r) && r.Turn == engine.TurnNumber
                ? r.ReasonPlayer
                : null;

        /// <summary>TextEffectRuntime.DestroyCard: the player whose card effect destroyed it (the resolving player).</summary>
        public static void NoteDestroyer(DuelEngine engine, CardInstance card, DuelistState destroyer)
        {
            if (engine == null || card == null || destroyer == null) return;
            if (!s_destroyed.TryGetValue(card, out var r) || r.Turn != engine.TurnNumber) return;
            r.ReasonPlayer = destroyer;
        }

        static DuelistState HolderOf(DuelEngine engine, CardInstance card)
        {
            var c = engine.ControllerOf(card);
            if (c != null) return c;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.Graveyard.Contains(card) || side.Hand.Contains(card) || side.Banished.Contains(card))
                    return side;
            }

            return null;
        }

        /// <summary>
        /// <see cref="EffectClause.RequiresDestroyedByOpponentThisTurn"/>: <paramref name="card"/> is in
        /// <paramref name="who"/>'s GY because it was destroyed in <paramref name="who"/>'s Monster Zone by a card of
        /// the opponent, and sent there this turn.
        /// </summary>
        public static bool DestroyedByOpponentThisTurn(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null || !who.Graveyard.Contains(card)) return false;
            if (!s_destroyed.TryGetValue(card, out var r)) return false;
            return r.Turn == engine.TurnNumber && card.SentFromFieldTurnNumber == engine.TurnNumber &&
                   r.InOwnersZone && ReferenceEquals(r.Owner, who) && r.ReasonPlayer != null &&
                   !ReferenceEquals(r.ReasonPlayer, who);
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor (either player's End Phase): Dark Necrofear's mandatory trigger in
        /// its owner's GY, once per turn. The AI (or a lone target) resolves at once; a human's pick is queued and
        /// asked once the End Phase's triggers have fired (<see cref="OfferQueuedPick"/>).
        /// </summary>
        public static void OnEndPhaseTrigger(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null || engine.GameOver) return;
            if (clause.RequiresDestroyedByOpponentThisTurn && !DestroyedByOpponentThisTurn(engine, who, source)) return;
            if (!s_destroyed.TryGetValue(source, out var r) || r.FiredTurn == engine.TurnNumber) return;
            r.FiredTurn = engine.TurnNumber;
            var targets = NecrofearTargets(engine, who, source, clause);
            engine.Log($"{source.Name}: its effect activates in the End Phase.");
            if (targets.Count == 0)
            {
                engine.Log($"{source.Name}: no face-up monster your opponent controls — no effect.");
                return;
            }

            if (!engine.IsHumanControlled(who) || targets.Count == 1)
            {
                ResolveEquipFromGy(engine, who, source, AiNecrofearPick(targets));
                return;
            }

            StateOf(engine).Queue.Add(new QueuedEquip { Who = who, Source = source, Clause = clause, Turn = engine.TurnNumber });
        }

        static NecrofearState StateOf(DuelEngine engine) => s_necro.GetValue(engine, _ => new NecrofearState());

        static List<CardInstance> NecrofearTargets(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause) =>
            TextEffectRuntime.CollectTargets(engine, who, clause, source)
                .Where(t => t != null && t.FaceUp && ReferenceEquals(engine.ControllerOf(t), engine.OpponentOf(who)))
                .Distinct()
                .ToList();

        static CardInstance AiNecrofearPick(List<CardInstance> targets) =>
            targets.OrderByDescending(t => t.CurrentAtk).ThenBy(t => t.Name).First();

        /// <summary>
        /// Ask the next queued human Dark Necrofear pick (the End Phase waits). False when nothing is asked.
        /// </summary>
        public static bool OfferQueuedPick(DuelEngine engine)
        {
            if (engine == null || !s_necro.TryGetValue(engine, out var st) || st.Queue.Count == 0) return false;
            if (engine.PendingActivation != null || engine.IsAwaitingResponse || engine.GameOver) return false;
            while (st.Queue.Count > 0)
            {
                var q = st.Queue[0];
                st.Queue.RemoveAt(0);
                if (q.Turn != engine.TurnNumber || engine.Phase != DuelPhase.End) continue;
                if (q.Who?.Graveyard == null || !q.Who.Graveyard.Contains(q.Source)) continue;
                var targets = NecrofearTargets(engine, q.Who, q.Source, q.Clause);
                if (targets.Count == 0)
                {
                    engine.Log($"{q.Source.Name}: no face-up monster your opponent controls — no effect.");
                    continue;
                }

                var pending = new PendingActivation
                {
                    Controller = q.Who,
                    Card = q.Source,
                    TargetKind = EffectTargetKind.OppFaceUpMonster,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = "target 1 face-up monster your opponent controls: equip this card to it and take " +
                                     "control of it."
                };
                pending.LegalTargets.AddRange(targets);
                s_necroPicks.Add(pending, q);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            return false;
        }

        /// <summary>TextEffectRuntime.TryResolveMonsterTextTarget hook: the human picked Dark Necrofear's target.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_necroPicks.TryGetValue(pending, out var q)) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            if (pick == null)
            {
                engine.Log("Choose one of the highlighted monsters (this effect is mandatory).");
                return true;
            }

            s_necroPicks.Remove(pending);
            engine.ClearPendingActivation();
            ResolveEquipFromGy(engine, q.Who, q.Source, pick);
            ok = true;
            GoOn(engine);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the trigger is mandatory — Cancel takes the AI's pick.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_necroPicks.TryGetValue(pending, out var q)) return false;
            s_necroPicks.Remove(pending);
            var targets = pending.LegalTargets.Where(t => t != null && engine.ControllerOf(t) != null).ToList();
            engine.ClearPendingActivation();
            if (targets.Count > 0)
                ResolveEquipFromGy(engine, q.Who, q.Source, AiNecrofearPick(targets));
            GoOn(engine);
            return true;
        }

        static void GoOn(DuelEngine engine)
        {
            if (engine.GameOver) return;
            if (OfferQueuedPick(engine))
            {
                engine.NotifyPublic();
                return;
            }

            engine.ResumeEndTurnAfterTrigger();
            engine.NotifyPublic();
        }

        /// <summary>
        /// Dark Necrofear resolves (Lua s.eqop): it moves from its owner's GY to a Spell &amp; Trap Zone as an Equip Card
        /// equipped to <paramref name="target"/>, and its controller takes control of that monster. Nothing happens
        /// when it left the GY, the target is no longer a face-up monster the opponent controls, or no Spell &amp; Trap
        /// Zone is free.
        /// </summary>
        public static void ResolveEquipFromGy(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null || source == null) return;
            if (!who.Graveyard.Contains(source))
            {
                engine.Log($"{source.Name} is no longer in the GY — no effect.");
                return;
            }

            if (target == null || !target.FaceUp || !ReferenceEquals(engine.ControllerOf(target), engine.OpponentOf(who)))
            {
                engine.Log($"{source.Name}: the target is no longer a face-up monster your opponent controls — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, source))
            {
                engine.Log("Necrovalley prevents the card from leaving the GY.");
                return;
            }

            var zi = engine.FirstEmptySpellTrap(who);
            if (zi < 0)
            {
                engine.Log($"{source.Name}: no free Spell & Trap Zone — it stays in the GY.");
                return;
            }

            who.Graveyard.Remove(source);
            ForgetDestroyRecord(source);
            who.SpellTrapZones[zi].Occupant = source;
            source.FaceUp = true;
            source.SetThisTurn = false;
            source.ClearAttackFlags();
            source.SentFromFieldTurnNumber = 0;
            source.EquippedTo = target;
            source.EquipTurnCounter = 0;
            source.Counters = 0;
            if (!target.Equips.Contains(source))
                target.Equips.Add(source);
            NoteEquipped(engine, source, target);
            engine.Log($"{source.Name} is equipped to {target.Name}.");
            if (engine.ControllerOf(target) == who) return;
            // EquipModel batch: equipped while Equip Card effects are negated (Armored Glass, The Emperor's Holiday) —
            // its control effect applies once that negation ends.
            if (ContinuousProtections.EquipCardEffectsAreNegated(engine, source))
            {
                engine.Log($"{source.Name}: its Equip Card effect is negated — no control change for now.");
                EquipModelRuntime.SuspendControl(engine, source, target);
                engine.NotifyPublic();
                return;
            }

            // MonsterNegationAndControlLocks batch: control of a locked monster cannot switch (it stays equipped).
            if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, target))
            {
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

        // ------------------------------------------------------------------ activation conditions

        /// <summary><see cref="FaceUpEquipCardOnField"/>; false when <paramref name="c"/> is not this batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, EffectClause c, out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (c?.ActivationCondition != FaceUpEquipCardOnField) return false;
            ok = FaceUpEquipCards(engine).Count > 0;
            reason = "Needs a face-up Equip Card on the field.";
            return true;
        }
    }
}
