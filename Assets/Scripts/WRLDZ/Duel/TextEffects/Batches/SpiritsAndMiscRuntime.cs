using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpiritsAndMisc" runtime: the Spirit return (proc_spirit.lua), Yata-Garasu's skip-Draw,
    /// Hino-Kagu-Tsuchi's next-Draw-Phase hand discard, Yamata Dragon's draw-until-5 and Kiseitai's
    /// monster-becomes-Equip-Spell. Atoms: <see cref="SpiritsAndMiscAtoms"/>.
    /// <para>Spirit return (ygopro <c>Spirit.AddProcedure(c, EVENT_SUMMON_SUCCESS, EVENT_FLIP)</c>): a flag is
    /// registered when the Spirit is Normal Summoned (Tribute included) or flipped face-up by any means (Flip
    /// Summon, attacked while face-down, a card effect), reset at the End Phase, when it leaves the field or is
    /// turned face-down; a mandatory trigger in the Monster Zone returns it to its owner's hand during the End
    /// Phase of that turn — whichever player's turn it is. Here: <see cref="CardInstance.NormalOrFlipSummonedTurn"/>
    /// or <see cref="CardInstance.FlippedFaceUpThisTurn"/>, face-up in a Monster Zone.</para>
    /// </summary>
    public static class SpiritsAndMiscRuntime
    {
        // ───────────────────────────── Spirit return ─────────────────────────────

        /// <summary>DuelEngine.BeginTurn: "flipped face-up this turn" counts from the start of this turn.</summary>
        public static void MarkTurnStartFaceState(DuelEngine engine)
        {
            if (engine == null) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null)
                        m.FaceUpStampAtTurnStart = m.FaceUpStamp;
            }
        }

        /// <summary>
        /// The shared Spirit sentence ("This card returns to its owner's hand during the End Phase of the turn it
        /// is Normal Summoned or flipped face-up": PhaseTriggerTemplates' End Phase ReturnToHand with
        /// <see cref="EffectClause.RequiresSummonedOrFlippedThisTurn"/>). It fires in every End Phase.
        /// </summary>
        public static bool IsSpiritReturnClause(EffectClause c, EffectTiming timing) =>
            c != null && timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ReturnToHand &&
            c.RequiresSummonedOrFlippedThisTurn && !c.ResolvesFromGy;

        /// <summary>Face-up in a Monster Zone and Normal Summoned or flipped face-up this turn.</summary>
        public static bool SpiritReturnDue(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !card.FaceUp) return false;
            // Spiritual Energy Settle Machine (ContinuousTrapsAndSpiritSupport batch): EFFECT_SPIRIT_DONOT_RETURN.
            if (ContinuousTrapsAndSpiritSupportRuntime.SpiritReturnPrevented(engine)) return false;
            var controller = engine.ControllerOf(card);
            if (controller == null || !controller.TryFindMonster(card, out _)) return false;
            return card.NormalOrFlipSummonedTurn == engine.TurnNumber || card.FlippedFaceUpThisTurn;
        }

        // ───────────────────────────── Yata-Garasu ─────────────────────────────

        /// <summary>
        /// Yata-Garasu (c3078576): its EFFECT_SKIP_DP is reset at the End Phase of the opponent's turn
        /// (RESET_PHASE+PHASE_END+RESET_OPPO_TURN). Inflicted during the opponent's own turn, their Draw Phase has
        /// already passed and the skip ends with that turn: nothing is skipped. Fenrir's battle-destroy skip
        /// (reset at the opponent's Draw Phase) is not this timing and is unaffected.
        /// </summary>
        public static bool BattleDamageSkipLapses(DuelEngine engine, EffectClause clause, DuelistState opp)
        {
            if (engine == null || clause == null || opp == null) return false;
            if (clause.Timing != EffectTiming.ThisCardInflictsBattleDamage) return false;
            if (engine.TurnPlayer != opp) return false;
            engine.Log($"{opp.Name}'s Draw Phase this turn has passed — the skip ends with this turn.");
            return true;
        }

        // ───────────────────────────── Yamata Dragon ─────────────────────────────

        /// <summary>Draw until <paramref name="drawer"/> holds <see cref="EffectClause.Amount"/> cards (c76862289).</summary>
        public static void DrawUntilHandSize(DuelEngine engine, DuelistState drawer, EffectClause clause,
            CardInstance source)
        {
            if (engine == null || drawer?.Hand == null || clause == null) return;
            var want = clause.Amount > 0 ? clause.Amount : 5;
            var n = want - drawer.Hand.Count;
            if (n <= 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: {drawer.Name} already holds {drawer.Hand.Count} cards — no draw.");
                return;
            }

            engine.Log($"{source?.Name ?? "Effect"}: {drawer.Name} draws until {want} cards in hand ({n}).");
            engine.Draw(drawer, n, silent: false);
        }

        // ───────────────────────────── Hino-Kagu-Tsuchi ─────────────────────────────

        /// <summary>c75745607 hdreg: the opponent discards their hand at the start of the next Draw Phase.</summary>
        public static void ScheduleHandDiscardAtNextDrawPhase(DuelEngine engine, DuelistState victim,
            CardInstance source)
        {
            if (engine == null || victim == null) return;
            victim.DiscardHandAtNextDrawPhase = true;
            engine.Log($"{source?.Name ?? "Effect"}: {victim.Name} discards their whole hand during the next Draw Phase, before they draw.");
        }

        /// <summary>
        /// Start of every Draw Phase, before the draw (EVENT_PREDRAW): each player with a pending
        /// Hino-Kagu-Tsuchi discard sends every card in their hand to the GY. A Draw Phase a card effect skips
        /// (Yata-Garasu, Fenrir, Reckless Greed) has no EVENT_PREDRAW, and RESET_PHASE+PHASE_DRAW ends the
        /// pending discard with no effect.
        /// </summary>
        public static void OnDrawPhaseStart(DuelEngine engine, bool skippedByEffect)
        {
            if (engine == null) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null || !side.DiscardHandAtNextDrawPhase) continue;
                side.DiscardHandAtNextDrawPhase = false;
                if (skippedByEffect)
                {
                    engine.Log($"The Draw Phase is skipped — {side.Name}'s pending hand discard ends.");
                    continue;
                }

                var hand = side.Hand?.ToList();
                if (hand == null || hand.Count == 0)
                {
                    engine.Log($"{side.Name} has no cards in hand to discard.");
                    continue;
                }

                // Discarded by an effect of the card the other player controls (Lua REASON_DISCARD|REASON_EFFECT, rp = the
                // Hino controller): Electric Snake / Minar trigger (DiscardTriggers batch).
                var n = 0;
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    foreach (var card in hand)
                    {
                        var gone = MonsterEffects.DiscardFromHandByInstance(side, card);
                        if (gone == null) continue;
                        n++;
                        DiscardTriggersRuntime.NoteDiscard(engine, side, gone, engine.OpponentOf(side));
                    }
                }
                finally { DiscardTriggersRuntime.EndScope(engine); }
                engine.Log($"{side.Name} discards all {n} card(s) in their hand before the draw (Hino-Kagu-Tsuchi).");
            }
        }

        // ───────────────────────────── Kiseitai ─────────────────────────────

        /// <summary>
        /// DuelEngine Damage Step hook, right after a face-down attack target was flipped and before damage
        /// calculation (ygopro c4266839: EVENT_BATTLE_CONFIRM, attack target in face-down Defense Position when
        /// the battle started). The target's compiled <see cref="EffectActionKind.BecomeEquipOnFaceDownAttacker"/>
        /// clause moves it from its Monster Zone to its controller's Spell &amp; Trap Zone as an Equip Spell
        /// equipped to the attacker; with no free Spell &amp; Trap Zone, or a face-down attacker, it is destroyed
        /// instead. Either way the target left its Monster Zone and no damage calculation is conducted: returns
        /// true. False when the clause does not apply (the battle goes on).
        /// </summary>
        public static bool TryBecomeEquipOnFaceDownAttacker(DuelEngine engine, DuelistState attackingPlayer,
            CardInstance attacker, CardInstance target)
        {
            if (engine == null || attackingPlayer == null || attacker?.Def == null || target?.Def == null)
                return false;
            var owner = engine.ControllerOf(target);
            if (owner == null || owner == attackingPlayer || !owner.TryFindMonster(target, out var mi))
                return false;
            if (!attackingPlayer.TryFindMonster(attacker, out _)) return false;
            if (!target.FaceUp || target.IsNegated) return false;
            var prog = CompiledEffectCache.GetOrCompile(target);
            if (prog == null || !prog.FullyCompiled) return false;
            var clause = prog.ClausesFor(EffectTiming.StartOfDamageStep)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.BecomeEquipOnFaceDownAttacker);
            if (clause == null) return false;

            var zi = engine.FirstEmptySpellTrap(owner);
            if (zi < 0 || !attacker.FaceUp)
            {
                engine.Log(zi < 0
                    ? $"{target.Name}: no free Spell & Trap Zone — it is destroyed instead (no damage calculation)."
                    : $"{target.Name}: {attacker.Name} is face-down — {target.Name} is destroyed instead (no damage calculation).");
                engine.DestroyMonsterPublic(owner, target, target);
                return true;
            }

            // It leaves its Monster Zone but stays on the field: its own Equip Cards lose their monster (to the GY);
            // Call of the Haunted-style links stay face-up, unlinked.
            engine.UnlinkEquipsForZoneMove(owner, target);
            owner.MonsterZones[mi].Occupant = null;
            owner.SpellTrapZones[zi].Occupant = target;
            target.FaceUp = true;
            target.SetThisTurn = false;
            target.ClearAttackFlags();
            target.EquippedTo = attacker;
            target.EquipTurnCounter = 0;
            target.Counters = 0;
            if (!attacker.Equips.Contains(target))
                attacker.Equips.Add(target);
            engine.Log($"{target.Name} becomes an Equip Spell Card equipped to {attacker.Name} — no damage calculation is conducted.");
            // EquipModel batch: the attacker is now equipped with an Equip Card (ygopro Duel.Equip, EVENT_EQUIP).
            EquipModelRuntime.NoteEquipped(engine, target, attacker);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// AI hint (SimpleAi, DuelStressAgent): a monster whose effect only applies when it is attacked face-down
        /// (Kiseitai) is Set face-down Defense like a Flip monster, never Normal Summoned face-up in Attack Position.
        /// </summary>
        public static bool AiSetsFaceDown(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsMonster) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.BecomeEquipOnFaceDownAttacker);
        }

        /// <summary>
        /// A monster card that "becomes an Equip Spell Card" (Kiseitai) is a Spell Card while it is equipped
        /// from a Spell &amp; Trap Zone; its printed type is still Monster.
        /// </summary>
        public static bool IsMonsterTreatedAsEquipSpell(CardInstance card) =>
            card?.Def != null && card.Def.IsMonster && card.EquippedTo != null;

        /// <summary>
        /// Kiseitai's Standby Phase effect (its controller's opponent's Standby Phase, OpponentTurnOnly): gain LP
        /// equal to half the current ATK of the monster it is equipped to. Nothing while it is not an Equip Card, while
        /// its monster is face-down, or while Equip Card / Spell effects on the field are negated (The Emperor's
        /// Holiday, Imperial Order, Spell Canceller) — it is an Equip Spell Card there, which the Def-typed negation
        /// paths do not see.
        /// </summary>
        public static void GainLpHalfEquippedMonsterAtk(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            var host = source.EquippedTo;
            if (host == null || !source.FaceUp || !who.TryFindSpellTrap(source, out _)) return;
            var hostController = engine.ControllerOf(host);
            if (hostController == null || !hostController.TryFindMonster(host, out _)) return;
            // A face-down monster has no Equip Cards (the board refresh destroys them: TextEffectRuntime
            // SweepClassicStateChecks); never read a face-down host's ATK in between.
            if (!host.FaceUp) return;
            if (source.IsNegated || ContinuousProtections.NegateAllEquipCardEffectsOnField(engine))
            {
                engine.Log($"{source.Name}: its Equip Card effect is negated.");
                return;
            }

            var by = ContinuousNegationRuntime.SpellEffectsOnFieldNegatedBy(engine, source);
            if (by != null)
            {
                engine.Log($"{source.Name}: its effect is negated by {by.Name}.");
                return;
            }

            var gain = BattleMechanics.AttackValue(host) / 2;
            if (gain <= 0)
            {
                engine.Log($"{source.Name}: {host.Name} has 0 ATK — no Life Points gained.");
                return;
            }

            TextEffectRuntime.ApplyLifePointGain(engine, who, gain, source.Name);
        }
    }
}
