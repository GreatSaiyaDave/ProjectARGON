using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonTriggersAndTributes" (v163, PGD). Atoms: <see cref="SummonTriggersAndTributesAtoms"/>.
    /// <list type="bullet">
    /// <item>Byser Shock: every face-down card on the field returns to its owner's hand, one return event.</item>
    /// <item>King Tiger Wanghu: a face-up monster host answering any Normal / Special Summon (not a Flip Summon) of a face-up
    /// monster with N or less ATK, fired with the summon-success notify (TextEffectRuntime.TryTriggerFaceUpFieldSummon,
    /// after the summon response window, like Harpies' Hunting Ground).</item>
    /// <item>Moisture Creature: an extra Tribute Summon procedure of exactly N Tributes (DuelEngine.TryNormalSummonToZone),
    /// stamped on the card; its trigger fires only for a Summon made that way.</item>
    /// <item>"You can only control 1 face-up X" (Gravekeeper's Chief): a shared rule on every face-up summon path and a
    /// sweep on each board refresh for duplicates that arrive anyway.</item>
    /// <item>"Your GY is unaffected by Necrovalley" (Gravekeeper's Chief): Necrovalley's GY locks ask about the GY owner.</item>
    /// <item>Jowls of Dark Demise: the taken monster can attack directly this turn.</item>
    /// <item>Metamorphosis: a Level-matched Extra Deck Special Summon for a Tribute.</item>
    /// </list>
    /// </summary>
    public static class SummonTriggersAndTributesRuntime
    {
        sealed class Stamp
        {
            public int Turn;
            public int Entry;
            public int Value;
        }

        // ───────────────────────────── Byser Shock ─────────────────────────────

        /// <summary>
        /// The <see cref="EffectActionKind.ReturnAllSetCardsOnFieldToHand"/> arm: every face-down card on both fields (Monster
        /// Zones, Spell &amp; Trap Zones, Field Zone) returns to its owner's hand at once (Lua Duel.SendtoHand(group)): one
        /// return event (TextEffectRuntime.NotifyMonstersReturnedToOwnerHand once for Spirit's Invitation / Spring of Rebirth).
        /// </summary>
        internal static void ReturnAllSetCardsToHand(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null) return;
            var set = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                set.AddRange(side.MonstersOnField().Where(c => c?.Def != null && !c.FaceUp));
                set.AddRange(side.SpellTrapsOnField().Where(c => c?.Def != null && !c.FaceUp));
            }

            if (set.Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: there is no Set card on the field.");
                return;
            }

            TextEffectRuntime.BeginMonsterReturnToOwnerHandBatch();
            try
            {
                foreach (var c in set.Distinct().ToList())
                {
                    if (engine.GameOver) break;
                    if (c.FaceUp || engine.ControllerOf(c) == null) continue; // flipped or gone meanwhile
                    if (ContinuousProtections.IsUnaffectedBy(engine, c, source))
                    {
                        engine.Log($"{c.Name} is unaffected by {source?.Name ?? "this effect"}.");
                        continue;
                    }

                    engine.ReturnCardToHand(c);
                }
            }
            finally
            {
                TextEffectRuntime.EndMonsterReturnToOwnerHandBatch(engine);
            }
        }

        // ───────────────────────────── King Tiger Wanghu ─────────────────────────────

        static readonly ConditionalWeakTable<CardInstance, Stamp> s_flipSummoned = new();
        static readonly ConditionalWeakTable<CardInstance, Stamp> s_summonAnswered = new();

        /// <summary>DuelEngine.CompleteFlipSummon: this field presence was Flip Summoned (King Tiger ignores it).</summary>
        public static void NoteFlipSummon(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null) return;
            s_flipSummoned.Remove(monster);
            s_flipSummoned.Add(monster, new Stamp { Turn = engine.TurnNumber, Entry = monster.FieldEntryStamp });
        }

        static bool WasFlipSummoned(DuelEngine engine, CardInstance m) =>
            s_flipSummoned.TryGetValue(m, out var s) && s.Turn == engine.TurnNumber && s.Entry == m.FieldEntryStamp;

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClauseList.FirstOrDefault(c => c != null && c.Action == action &&
                                                       c.Timing == EffectTiming.ContinuousWhileFaceUp);
        }

        /// <summary>
        /// TextEffectRuntime.TryTriggerFaceUpFieldSummon (the summon-success notify, after the summon response window):
        /// <paramref name="summoned"/> was Normal (incl. Tribute) or Special Summoned face-up (a Flip Summon or a Set is not
        /// answered). Each face-up, non-negated King Tiger Wanghu on either field other than the summoned monster destroys it
        /// while it is still face-up with <see cref="EffectClause.ConditionAmount"/> or less ATK (Lua condition and
        /// operation filter). Answered once per field presence.
        /// </summary>
        public static void OnSummonSettled(DuelEngine engine, DuelistState summoner, CardInstance summoned)
        {
            if (engine == null || summoned?.Def == null || engine.GameOver) return;
            if (!summoned.FaceUp || !summoned.Def.IsMonster || WasFlipSummoned(engine, summoned)) return;
            var ctrl = engine.ControllerOf(summoned);
            if (ctrl == null || !ctrl.TryFindMonster(summoned, out _) || engine.IsSummonPending(summoned)) return;
            if (s_summonAnswered.TryGetValue(summoned, out var done) && done.Entry == summoned.FieldEntryStamp &&
                done.Turn == engine.TurnNumber)
                return;
            s_summonAnswered.Remove(summoned);
            s_summonAnswered.Add(summoned, new Stamp { Turn = engine.TurnNumber, Entry = summoned.FieldEntryStamp });

            var hosts = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null && !ReferenceEquals(m, summoned) && m.FaceUp && !m.IsNegated &&
                        !engine.IsSummonPending(m) &&
                        ClauseOf(m, EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned) != null)
                        hosts.Add(m);
            }

            foreach (var host in hosts)
            {
                if (engine.GameOver) return;
                var clause = ClauseOf(host, EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned);
                var hostCtrl = engine.ControllerOf(host);
                // "This card must be face-up on the field to activate and to resolve this effect."
                if (clause == null || hostCtrl == null || !hostCtrl.TryFindMonster(host, out _) || !host.FaceUp ||
                    host.IsNegated)
                    continue;
                var cap = clause.ConditionAmount > 0 ? clause.ConditionAmount : 1400;
                var now = engine.ControllerOf(summoned);
                if (now == null || !now.TryFindMonster(summoned, out _) || !summoned.FaceUp ||
                    summoned.CurrentAtk > cap)
                    continue;
                engine.Log($"{host.Name}: {summoned.Name} was Summoned with {cap} or less ATK — it is destroyed.");
                FlipMassEffectsRuntime.Simultaneously(engine,
                    () => TextEffectRuntime.DestroyCard(engine, summoned, host, false, hostCtrl));
            }
        }

        // ───────────────────────────── Moisture Creature ─────────────────────────────

        static readonly ConditionalWeakTable<CardInstance, Stamp> s_procedureSummon = new();

        static EffectClause TributeProcedureClause(CardInstance card)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ThisCardSummoned)
                .FirstOrDefault(c => c != null && c.RequiresThisTributeSummonedByTributingExactly > 0);
        }

        /// <summary>
        /// DuelEngine.TryNormalSummonToZone, before the Tributes are taken: the Tributes this Normal Summon uses. A card
        /// with an extra N-Tribute procedure (<see cref="EffectClause.RequiresThisTributeSummonedByTributingExactly"/>,
        /// Moisture Creature) uses it when its summoner can pay N: a human who tapped N monsters that can be Tributed, the
        /// AI when the opponent controls a Spell/Trap Card for its trigger to destroy. A Set never uses it (ygopro
        /// AddNormalSummonProcedure: Summon only).
        /// </summary>
        public static int TributesToUse(DuelEngine engine, DuelistState who, CardInstance card, bool asSet, int need)
        {
            if (asSet || need <= 0 || engine == null || who == null) return need;
            var c = TributeProcedureClause(card);
            if (c == null) return need;
            var n = c.RequiresThisTributeSummonedByTributingExactly;
            if (n <= need || TextEffectRuntime.TributesAvailableFor(who, card) < n) return need;
            if (engine.IsHumanControlled(who))
            {
                var tapped = engine.PendingTributes
                    .Where(t => t != null && TcgRules.CanBeTributedForSummon(who, t))
                    .Distinct()
                    .Count();
                return tapped >= n ? n : need;
            }

            var opp = engine.OpponentOf(who);
            return opp != null && opp.SpellTrapsOnField().Any(st => st != null) ? n : need;
        }

        /// <summary>
        /// DuelEngine.TryNormalSummonToZone, once the monster is placed: stamp a Tribute Summon made with the extra
        /// procedure (<paramref name="tributesUsed"/> = that procedure's N, above the Level's own Tribute count).
        /// </summary>
        public static void NoteNormalSummon(DuelEngine engine, CardInstance card, bool asSet, int tributesUsed)
        {
            if (card == null) return;
            s_procedureSummon.Remove(card);
            if (asSet || tributesUsed <= 0) return;
            var c = TributeProcedureClause(card);
            if (c == null || tributesUsed != c.RequiresThisTributeSummonedByTributingExactly ||
                tributesUsed <= TcgRules.TributesRequired(card.Level))
                return;
            s_procedureSummon.Add(card, new Stamp { Entry = card.FieldEntryStamp, Value = tributesUsed });
            engine?.Log($"{card.Name} is Tribute Summoned by Tributing {tributesUsed} monsters.");
        }

        /// <summary>TextEffectRuntime.TryResolveThisCardSummoned: this presence was Tribute Summoned with the N-Tribute procedure.</summary>
        public static bool SummonedByTributeProcedure(CardInstance card, int n) =>
            card != null && s_procedureSummon.TryGetValue(card, out var s) && s.Entry == card.FieldEntryStamp &&
            s.Value == n && card.WasTributeSummoned;

        // ───────────────────────────── "You can only control 1 face-up X" ─────────────────────────────

        /// <summary>The X of this card's "You can only control 1 face-up X" rule, or null.</summary>
        public static string UniqueName(CardInstance card)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog?.ClauseList
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.YouCanOnlyControlOneFaceUpNamed &&
                                     !string.IsNullOrEmpty(c.NamedCard))
                ?.NamedCard;
        }

        static IEnumerable<CardInstance> FaceUpOnField(DuelEngine engine, DuelistState side)
        {
            if (side == null) yield break;
            foreach (var m in side.MonstersOnField())
                if (m?.Def != null && m.FaceUp && (engine == null || !engine.IsSummonPending(m)))
                    yield return m;
            foreach (var st in side.SpellTrapsOnField())
                if (st?.Def != null && st.FaceUp)
                    yield return st;
        }

        /// <summary>
        /// ygopro check_unique_onfield: <paramref name="card"/> cannot be put face-up under <paramref name="who"/>'s
        /// control (Normal / Flip / Special Summon) while <paramref name="who"/> controls another face-up card named its
        /// unique X, or a face-up card whose unique X is <paramref name="card"/>'s name. A Set is not affected.
        /// </summary>
        public static bool UniqueBlocksFaceUp(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = null;
            if (who == null || card?.Def == null) return false;
            var own = UniqueName(card);
            foreach (var f in FaceUpOnField(engine, who))
            {
                if (ReferenceEquals(f, card)) continue;
                var theirs = UniqueName(f);
                if ((own != null && f.IsNamed(own)) || (theirs != null && card.IsNamed(theirs)))
                {
                    reason = $"You can only control 1 face-up \"{own ?? theirs}\".";
                    return true;
                }
            }

            return false;
        }

        sealed class Seen
        {
            public DuelistState Controller;
            public int Entry;
            public int FaceDownStamp;
            public long Order;
        }

        static readonly ConditionalWeakTable<CardInstance, Seen> s_seen = new();
        static long s_order;
        static bool s_sweeping;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: when a player controls 2+ face-up cards named the X of a face-up card's "only 1
        /// face-up X" rule (one flipped face-up by an effect or by battle, or taken by a control change), the one that
        /// came face-up under that player's control first stays and the others are destroyed (ygopro unique_destroy, a
        /// rule: no protection applies). Not while a choice is open or during the Damage Step before damage.
        /// </summary>
        public static void SweepUniqueOnField(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver || s_sweeping) return;
            // Arrival order: a card is "new" when first seen face-up under this controller in this field presence.
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var c in FaceUpOnField(engine, side))
            {
                if (s_seen.TryGetValue(c, out var s) && ReferenceEquals(s.Controller, side) && s.Entry == c.FieldEntryStamp &&
                    s.FaceDownStamp == c.FaceDownStamp)
                    continue;
                s_seen.Remove(c);
                s_seen.Add(c, new Seen
                {
                    Controller = side, Entry = c.FieldEntryStamp, FaceDownStamp = c.FaceDownStamp, Order = ++s_order
                });
            }

            if (engine.PendingActivation != null) return;
            if (engine.BattleStep == BattleStep.DamageStep &&
                (engine.DamageSubStep == DamageSubStep.Start ||
                 engine.DamageSubStep == DamageSubStep.BeforeDamageCalculation ||
                 engine.DamageSubStep == DamageSubStep.DamageCalculation))
                return;

            s_sweeping = true;
            try
            {
                foreach (var side in new[] { engine.Player, engine.Opponent })
                {
                    var faceUp = FaceUpOnField(engine, side).ToList();
                    var names = faceUp.Select(UniqueName).Where(n => n != null)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    foreach (var name in names)
                    {
                        var group = FaceUpOnField(engine, side).Where(c => c.IsNamed(name)).ToList();
                        if (group.Count <= 1) continue;
                        var keep = group.OrderBy(c => s_seen.TryGetValue(c, out var s) ? s.Order : long.MaxValue).First();
                        foreach (var c in group)
                        {
                            if (ReferenceEquals(c, keep) || engine.GameOver) continue;
                            engine.Log($"You can only control 1 face-up \"{name}\": {side.Name}'s {c.Name} is destroyed.");
                            if (side.TryFindMonster(c, out _))
                                engine.DestroyMonsterPublic(side, c);
                            else
                                TextEffectRuntime.DestroyCard(engine, c, null, false, null);
                        }
                    }
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }

        // ───────────────────────────── "Your GY is unaffected by Necrovalley" ─────────────────────────────

        /// <summary>
        /// ygopro EFFECT_NECRO_VALLEY_IM: <paramref name="gyOwner"/> controls a face-up, non-negated monster whose effect
        /// makes their GY unaffected by <paramref name="lockCard"/> (Gravekeeper's Chief: "Necrovalley"). Necrovalley's
        /// banish, target and leave-the-GY locks then do not apply to that GY (Lua e2a/e2b/e3*: condition per GY owner).
        /// </summary>
        public static bool GyUnaffectedBy(DuelEngine engine, DuelistState gyOwner, CardInstance lockCard)
        {
            if (gyOwner == null || lockCard == null) return false;
            foreach (var m in gyOwner.MonstersOnField())
            {
                if (m == null || !m.FaceUp || m.IsNegated || (engine != null && engine.IsSummonPending(m))) continue;
                var c = ClauseOf(m, EffectActionKind.YourGyUnaffectedByNamedCard);
                if (c != null && lockCard.IsNamed(c.NamedCard)) return true;
            }

            return false;
        }

        /// <summary>The player whose GY holds <paramref name="gyCard"/>, or null.</summary>
        public static DuelistState GyOwnerOf(DuelEngine engine, CardInstance gyCard)
        {
            if (engine == null || gyCard == null) return null;
            if (engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(gyCard)) return engine.Player;
            if (engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(gyCard)) return engine.Opponent;
            return null;
        }

        // ───────────────────────────── Jowls of Dark Demise ─────────────────────────────

        /// <summary>
        /// The TakeControlUntilEndPhase arm, once control was taken with <see cref="EffectClause.TakenMonsterCanAttackDirectlyThisTurn"/>:
        /// the monster can attack directly this turn (granted by this effect, so its own negation does not remove it; a
        /// face-down monster keeps it if it is flipped face-up; turning it face-down or leaving the field ends it).
        /// </summary>
        internal static void GrantDirectAttackAfterControl(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance taken)
        {
            if (engine == null || who == null || taken == null || engine.ControllerOf(taken) != who) return;
            PgdSpellTrapBasicsRuntime.GrantDirectAttack(engine, who, source, taken, requireFaceUp: false);
        }

        // ───────────────────────────── Metamorphosis ─────────────────────────────

        /// <summary>
        /// One instance per distinct Fusion Monster of <paramref name="level"/> in <paramref name="who"/>'s Extra Deck that
        /// can be Special Summoned by an effect (summoning conditions apply: SpecialSummonLegality, Extra Deck origin).
        /// </summary>
        static List<CardInstance> FusionsOfLevel(DuelEngine engine, DuelistState who, CardInstance source, int level)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.ExtraDeck == null || level <= 0) return list;
            foreach (var id in who.ExtraDeck.Distinct().ToList())
            {
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster || def.level != level ||
                    def.type == null || def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var inst = engine.CreateCardInstance(id);
                if (inst == null ||
                    !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.ExtraDeck))
                    continue;
                list.Add(inst);
            }

            return list;
        }

        /// <summary>
        /// TributeSummonsRuntime.FilterTributeCostCandidates (Metamorphosis' cost): a Fusion Monster of
        /// <paramref name="level"/> in the Extra Deck can be Special Summoned.
        /// </summary>
        public static bool ExtraHasLevelMatch(DuelEngine engine, DuelistState who, int level) =>
            FusionsOfLevel(engine, who, null, level).Count > 0;

        /// <summary>
        /// The SpecialSummonFusionFromExtra arm with <see cref="EffectClause.MatchTributedLevel"/>: Special Summon 1 Fusion
        /// Monster from the Extra Deck whose Level is the Tributed monster's Level recorded at cost time
        /// (<paramref name="costNumeric"/>; Lua e:GetLabel()), face-up (not a Fusion Summon). A human chooses among 2+.
        /// </summary>
        internal static void SpecialSummonLevelMatchedFusionFromExtra(DuelEngine engine, DuelistState who,
            CardInstance source, int costNumeric)
        {
            if (engine == null || who == null) return;
            var name = source?.Name ?? "Effect";
            if (costNumeric <= 0)
            {
                engine.Log($"{name}: no Tributed Level was recorded — nothing is Special Summoned.");
                return;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                () => FusionsOfLevel(engine, who, source, costNumeric),
                EffectTargetKind.MonsterInYourDeckToSummon,
                $"Special Summon 1 Level {costNumeric} Fusion Monster from your Extra Deck.",
                list => list.OrderByDescending(f => f.Def?.atk ?? 0).First(),
                pick => SummonFusion(engine, who, source, pick, costNumeric));
        }

        static void SummonFusion(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick, int level)
        {
            var name = source?.Name ?? "Effect";
            if (pick == null)
            {
                engine.Log($"{name}: no Level {level} Fusion Monster in the Extra Deck can be Special Summoned.");
                return;
            }

            if (!who.ExtraDeck.Remove(pick.CardId))
            {
                engine.Log($"{name}: {pick.Name} is no longer in the Extra Deck.");
                return;
            }

            if (!engine.SpecialSummonToField(who, pick, BattlePosition.Attack, true, SummonKind.SpecialSummon))
            {
                who.ExtraDeck.Add(pick.CardId);
                engine.Log($"{name}: {pick.Name} could not be Special Summoned.");
                return;
            }

            engine.Log($"{name}: {who.Name} Special Summons {pick.Name} (Level {level}) from the Extra Deck.");
            engine.FlushQueuedSummonResponses();
        }
    }
}
