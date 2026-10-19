namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SimpleTargetUtility batch (v173): Cost Down's hand Level stamps, re-applied after every
    /// <c>FieldSpellEffects.ClearModifiers</c> sweep (called from <c>FieldSpellEffects.RefreshBoard</c>).
    /// Altar for Tribute's LP gain lives inline in <c>TextEffectRuntime.ApplyClause</c>
    /// (<c>GainLpEqualToCostAmount</c> reads the recorded costNumeric); the activation lock of Fairy of the
    /// Spring is stamped by the shared <c>AddFromGyToHand</c> arm and checked in
    /// <c>TextEffectRuntime.CanActivate</c>.
    /// </summary>
    public static class SimpleTargetUtilityRuntime
    {
        /// <summary>
        /// Cost Down resolution: stamp the controller's hand (duelist-wide stamp for cards drawn later this
        /// turn, per-monster stamps for the cards already held), then apply immediately so the reduced Levels
        /// are live before the next state refresh. Stamps accumulate: a second Cost Down adds its amount to the
        /// reduction already live. <c>FieldSpellEffects.RefreshBoard</c> clears every <c>LevelModifier</c>
        /// first, so <see cref="ApplyLevelStamps"/> re-applies the accumulated stamps after each clear — the
        /// stamps themselves only die when <c>TurnNumber</c> moves past them. Re-application is idempotent
        /// (<see cref="ApplyOne"/> sets, never adds), so two copies in one turn settle at twice the reduction
        /// instead of double-applying once.
        /// </summary>
        public static void ApplyHandLevelStamp(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var amount = clause.Amount;
            if (amount == 0)
            {
                engine.Log("Cost Down: no Level reduction recorded.");
                return;
            }

            // Stamps accumulate: a second Cost Down this turn adds its amount to the reduction already live.
            who.LevelReducedHandUntilTurn = engine.TurnNumber;
            who.LevelReducedHandAmount += amount;
            if (who.Hand != null)
                foreach (var h in who.Hand)
                {
                    if (h?.Def == null || !h.Def.IsMonster) continue;
                    h.LevelReducedUntilTurn = engine.TurnNumber;
                    h.LevelReducedAmount += amount;
                }

            engine.Log(
                $"Levels of monsters in {who.Name}'s hand reduced by {amount} (total {who.LevelReducedHandAmount} this turn) for the rest of this turn.");
            // Apply immediately (stamp sweep only — no full board refresh from inside a resolving clause).
            ApplyLevelStamps(engine);
        }

        /// <summary>
        /// Called from <c>FieldSpellEffects.RefreshBoard</c> right after both ClearModifiers calls: re-stamp
        /// hand monsters drawn under the hand-wide stamp, then set every live stamp onto LevelModifier
        /// for hand and field monsters (field included: "even after they are Summoned").
        /// </summary>
        public static void ApplyLevelStamps(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            Sweep(engine, engine.Player);
            Sweep(engine, engine.Opponent);
            // TlmLeftoverSpellTraps batch: Level Conversion Lab's "Level becomes N" (a set Level, applied after the stamps).
            TlmLeftoverSpellTrapsRuntime.ApplyLevelChanges(engine);
        }

        static void Sweep(DuelEngine engine, DuelistState who)
        {
            // Cards drawn after the resolution inherit the hand-wide stamp.
            if (who.LevelReducedHandUntilTurn == engine.TurnNumber && who.Hand != null)
                foreach (var h in who.Hand)
                {
                    if (h?.Def == null || !h.Def.IsMonster) continue;
                    if (h.LevelReducedUntilTurn == engine.TurnNumber) continue;
                    h.LevelReducedUntilTurn = engine.TurnNumber;
                    h.LevelReducedAmount = who.LevelReducedHandAmount;
                }

            if (who.Hand != null)
                foreach (var h in who.Hand)
                    ApplyOne(engine, h);
            if (who.MonsterZones == null) return;
            foreach (var z in who.MonsterZones)
                ApplyOne(engine, z?.Occupant);
        }

        static void ApplyOne(DuelEngine engine, CardInstance c)
        {
            if (c?.Def == null || !c.Def.IsMonster) return;
            if (c.LevelReducedUntilTurn != engine.TurnNumber) return;
            if (c.LevelReducedAmount == 0) return;
            c.LevelModifier = -c.LevelReducedAmount;
        }
    }
}
