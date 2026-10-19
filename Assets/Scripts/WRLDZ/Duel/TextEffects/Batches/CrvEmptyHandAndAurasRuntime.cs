using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvEmptyHandAndAuras" runtime (atoms: <see cref="CrvEmptyHandAndAurasAtoms"/>). Read live, no card-id
    /// branches:
    /// <list type="bullet">
    /// <item><see cref="NoCardsInHand"/>: an <see cref="EffectClause.WhileCondition"/> evaluated by
    /// AttackShieldsAndDamageZeroRuntime.WhileConditionMet (Cybernetic Cyclopean's ATK, Reborn Zombie's battle shield,
    /// Mechanical Hound's lock).</item>
    /// <item><see cref="AuraMultiplier"/>: Batteryman C's field aura, N x the face-up copies its controller controls while
    /// every one is in the clause's battle position (FieldSpellEffects.ApplyStatClause).</item>
    /// <item><see cref="ActivationLocked"/>: Mechanical Hound's Spell Card lock (ContinuousNegationRuntime.ActivationLocked,
    /// the hub every activation site asks).</item>
    /// <item><see cref="IgnoresBattleDestruction"/>: Gyroid's once-per-turn battle shield (DuelEngine damage calculation).</item>
    /// </list>
    /// </summary>
    public static class CrvEmptyHandAndAurasRuntime
    {
        /// <summary>WhileCondition: the source's controller has no cards in hand (Lua GetFieldGroupCount(LOCATION_HAND) == 0).</summary>
        public const string NoCardsInHand = "NoCardsInHand";

        /// <summary>
        /// ContinuousGainAtkDef <see cref="EffectClause.CountSource"/> (Batteryman C, ATK half): Amount per face-up monster
        /// named <see cref="EffectClause.ConditionName"/> the aura's controller controls, when every one of them is in Attack
        /// Position (else 0). Face-down copies neither count nor break the condition (Lua aux.FaceupFilter(Card.IsCode)).
        /// </summary>
        public const string FaceUpNamedYouControlAllInAttack = "CrvFaceUpNamedAllInAttack";

        /// <summary>The DEF half of <see cref="FaceUpNamedYouControlAllInAttack"/>: every one in Defense Position.</summary>
        public const string FaceUpNamedYouControlAllInDefense = "CrvFaceUpNamedAllInDefense";

        public static bool HandEmpty(DuelistState who) => who != null && (who.Hand == null || who.Hand.Count == 0);

        // ───────────────────────────── Batteryman C ─────────────────────────────

        /// <summary>
        /// FieldSpellEffects.ApplyStatClause: the per-count multiplier of a ContinuousGainAtkDef aura with this batch's count
        /// source (1 for any other aura).
        /// </summary>
        public static int AuraMultiplier(DuelistState controller, EffectClause clause)
        {
            if (clause == null) return 1;
            var attack = clause.CountSource == FaceUpNamedYouControlAllInAttack;
            if (!attack && clause.CountSource != FaceUpNamedYouControlAllInDefense) return 1;
            if (controller == null || string.IsNullOrEmpty(clause.ConditionName)) return 0;
            var named = controller.MonstersOnField()
                .Where(m => m != null && m.FaceUp && m.IsNamed(clause.ConditionName))
                .ToList();
            if (named.Count == 0) return 0;
            var want = attack ? BattlePosition.Attack : BattlePosition.Defense;
            return named.All(m => m.Position == want) ? named.Count : 0;
        }

        // ───────────────────────────── Mechanical Hound ─────────────────────────────

        /// <summary>
        /// True when <paramref name="who"/> may not activate the Spell Card <paramref name="card"/> (from the hand, or Set
        /// on the field): their opponent controls a face-up, un-negated monster with
        /// <see cref="EffectActionKind.ContinuousOpponentCannotActivateSpellCards"/> whose WhileCondition holds. The effects
        /// of face-up Spells are not card activations (Lua re:IsHasType(EFFECT_TYPE_ACTIVATE)) and stay legal.
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster || !card.Def.IsSpell) return false;
            if (!OwnSetOrHand(who, card)) return false;
            var opp = engine.OpponentOf(who);
            if (opp == null) return false;
            foreach (var m in opp.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp || m.IsNegated || engine.IsSummonPending(m)) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.ContinuousOpponentCannotActivateSpellCards) continue;
                    if (!AttackShieldsAndDamageZeroRuntime.WhileConditionMet(engine, m, c)) continue;
                    reason = $"{m.Name}: you cannot activate Spell Cards.";
                    return true;
                }
            }

            return false;
        }

        /// <summary>The card's activation: from the hand, or a Set card on the field (a face-up card's effect is not one).</summary>
        static bool OwnSetOrHand(DuelistState who, CardInstance card) =>
            (who.Hand != null && who.Hand.Contains(card)) ||
            ((who.TryFindSpellTrap(card, out _) ||
              (who.FieldSpellZone != null && ReferenceEquals(who.FieldSpellZone.Occupant, card))) && !card.FaceUp);

        // ───────────────────────────── Gyroid ─────────────────────────────

        sealed class CountUse
        {
            public int Turn;
            public int FieldEntry;
            public int FaceUp;
        }

        static readonly ConditionalWeakTable<CardInstance, CountUse> s_used = new();

        /// <summary>
        /// DuelEngine damage calculation, for a battler this battle would destroy: true (and the count is spent) when it is
        /// a face-up, un-negated monster in a Monster Zone with
        /// <see cref="EffectActionKind.CannotBeDestroyedByBattleOncePerTurn"/> whose count is unused this turn in this field
        /// presence and face-up stint (Lua EFFECT_INDESTRUCTABLE_COUNT, SetCountLimit(1)). Battle damage is not touched.
        /// </summary>
        public static bool IgnoresBattleDestruction(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !card.FaceUp || card.IsNegated) return false;
            var ctrl = engine.ControllerOf(card);
            if (ctrl == null || !ctrl.TryFindMonster(card, out _)) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.CannotBeDestroyedByBattleOncePerTurn);
            if (clause == null || !AttackShieldsAndDamageZeroRuntime.WhileConditionMet(engine, card, clause)) return false;
            if (s_used.TryGetValue(card, out var use) && use.Turn == engine.TurnNumber &&
                use.FieldEntry == card.FieldEntryStamp && use.FaceUp == card.FaceUpStamp)
                return false;
            s_used.Remove(card);
            s_used.Add(card, new CountUse
                { Turn = engine.TurnNumber, FieldEntry = card.FieldEntryStamp, FaceUp = card.FaceUpStamp });
            engine.Log($"{card.Name}: once per turn, it is not destroyed by battle.");
            return true;
        }
    }
}
