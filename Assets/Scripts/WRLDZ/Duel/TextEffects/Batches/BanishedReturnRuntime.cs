using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// "Once per turn, during the End Phase, if this card is currently banished, and was banished this turn:
    /// Special Summon it in face-up Attack Position." (D.D. Scout Plane, <see cref="BanishedReturnAtoms"/>).
    /// Every engine banish path stamps <see cref="CardInstance.BanishedTurnNumber"/>;
    /// TextEffectRuntime.FirePhaseTriggers calls <see cref="FireEndPhase"/> at the start of every End Phase, and
    /// TextEffectRuntime.FirePhaseTriggersFor skips the clause for copies on the field or in the GY.
    /// </summary>
    public static class BanishedReturnRuntime
    {
        /// <summary>Turn in which a copy last used its once-per-turn return (ygopro flag id+1).</summary>
        static readonly ConditionalWeakTable<CardInstance, StrongBox<int>> s_returnedTurn =
            new ConditionalWeakTable<CardInstance, StrongBox<int>>();

        /// <summary>
        /// End Phase of <paramref name="turnPlayer"/>'s turn: the turn player's banished pile, then the other
        /// player's. Each qualifying card is Special Summoned to its owner's field in face-up Attack Position.
        /// Mandatory (ygopro EFFECT_TYPE_TRIGGER_F), no target.
        /// </summary>
        public static void FireEndPhase(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null) return;
            foreach (var owner in new[] { turnPlayer, engine.OpponentOf(turnPlayer) })
            {
                if (owner?.Banished == null) continue;
                foreach (var card in owner.Banished.ToList())
                {
                    if (engine.GameOver) return;
                    if (card?.Def == null) continue;
                    var prog = CompiledEffectCache.GetOrCompile(card.Def);
                    if (prog == null) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.EndPhase))
                    {
                        if (c == null || c.Action != EffectActionKind.SpecialSummonThisFromBanished) continue;
                        if (!Qualifies(engine, owner, card, c)) continue;
                        Resolve(engine, owner, card, c);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Face-up in <paramref name="owner"/>'s banished pile (a face-down banish never counts: ygopro rmop),
        /// banished this turn when the clause asks for it, and its once-per-turn return not used yet this turn.
        /// </summary>
        public static bool Qualifies(DuelEngine engine, DuelistState owner, CardInstance card, EffectClause c)
        {
            if (engine == null || owner?.Banished == null || card == null || c == null) return false;
            if (!owner.Banished.Contains(card) || !card.FaceUp) return false;
            if (c.RequiresBanishedThisTurn && card.BanishedTurnNumber != engine.TurnNumber) return false;
            if (c.OncePerTurn && s_returnedTurn.TryGetValue(card, out var used) && used.Value == engine.TurnNumber)
                return false;
            return true;
        }

        /// <summary>
        /// ygopro s.operation: Special Summon it in face-up Attack Position to its owner's field; with no free
        /// Monster Zone the card is sent to the GY instead.
        /// </summary>
        static void Resolve(DuelEngine engine, DuelistState owner, CardInstance card, EffectClause c)
        {
            if (c.OncePerTurn)
                s_returnedTurn.GetOrCreateValue(card).Value = engine.TurnNumber;
            owner.Banished.Remove(card);
            if (engine.FirstEmpty(owner.MonsterZones) < 0)
            {
                engine.Log($"{card.Name}: no free Monster Zone — sent to the GY.");
                engine.SendCardToGrave(owner, card, sentBy: card);
                return;
            }

            DuelEngine.RecordOwnerOnFieldEntry(card, owner, owner);
            if (!engine.SpecialSummonToField(owner, card, BattlePosition.Attack, true))
            {
                owner.Banished.Add(card);
                engine.Log($"{card.Name} cannot be Special Summoned — it stays banished.");
                return;
            }

            engine.Log($"{card.Name} returns from banishment: Special Summoned in face-up Attack Position.");
        }
    }
}
