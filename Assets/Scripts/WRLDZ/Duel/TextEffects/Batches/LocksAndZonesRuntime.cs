using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// One Dimensionhole resolution (LocksAndZones batch, v142): <see cref="Card"/> was banished from
    /// <see cref="ReturnTo"/>'s Monster Zone <see cref="ZoneIndex"/> until <see cref="WaitsFor"/>'s next Standby
    /// Phase (the one in turn <see cref="ReturnTurn"/>). While the card stays in <see cref="Pile"/>'s banishment
    /// that zone cannot be used.
    /// </summary>
    public sealed class TemporaryBanishment
    {
        public CardInstance Card;
        /// <summary>The card that banished it (Dimensionhole).</summary>
        public CardInstance Source;
        /// <summary>The duelist whose banished pile holds the card (its owner).</summary>
        public DuelistState Pile;
        /// <summary>
        /// The duelist that controlled it when it was banished: its Monster Zone <see cref="ZoneIndex"/> is the one
        /// locked, and the card returns there when this duelist also owns it (otherwise to <see cref="Pile"/>'s field).
        /// </summary>
        public DuelistState ReturnTo;
        /// <summary>The Monster Zone it left (made unusable while it stays banished).</summary>
        public int ZoneIndex;
        public BattlePosition Position;
        public bool FaceUp;
        /// <summary>"your next Standby Phase": the duelist who activated the effect.</summary>
        public DuelistState WaitsFor;
        /// <summary>The turn whose Standby Phase returns the card.</summary>
        public int ReturnTurn;
        /// <summary>False once that Standby Phase has passed without returning it (it was skipped).</summary>
        public bool ReturnPending = true;
    }

    /// <summary>
    /// Card batch "LocksAndZones" (v142) runtime: Cold Wave, Dimensionhole and Light of Intervention. Reads
    /// compiled clauses only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Cold Wave</b> (<see cref="EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn"/>).
    /// Activation condition <see cref="StartOfMainPhase1"/>: Main Phase 1 and nothing has happened in it yet
    /// (ygopro <c>not Duel.CheckPhaseActivity()</c>) — <see cref="OnMainPhase1Start"/> fingerprints the game
    /// state when Main Phase 1 begins, before the opponent's start-of-phase window; any Summon, Set, position
    /// change, activation (counted on the Chain) or card movement since then changes the fingerprint. The resolution
    /// (<see cref="RegisterSpellTrapLock"/>) locks both players through the next turn: no Spell/Trap Card or
    /// effect can be activated (<see cref="SpellTrapActivationLocked"/>, read by
    /// <c>ContinuousNegationRuntime.ActivationLocked</c>, which every activation gate checks, and by
    /// <c>DuelEngine.TryPlaceFieldSpell</c>), and no Spell/Trap can be Set (<see cref="SpellTrapSetLocked"/>,
    /// read by <c>DuelEngine.CanSetSpellTrap</c> and the effect Set path). The activated trigger effects of
    /// face-up Spell/Trap Cards (and of Equip Spells sent to the GY) are locked too: the trigger paths that apply
    /// them without an activation gate ask <see cref="SpellTrapTriggerLocked"/>. Continuous effects (maintenance
    /// costs, Solemn Wishes) keep applying.</item>
    /// <item><b>Dimensionhole</b> (<see cref="EffectActionKind.BanishTargetUntilYourNextStandbyPhase"/>).
    /// <see cref="BanishUntilNextStandby"/> banishes the target and records the zone it left
    /// (<see cref="FieldZone.UnusableFor"/>: every placement path skips it). <see cref="OnStandbyPhase"/>
    /// (DuelEngine.BeginTurn, after the Standby Phase triggers) returns it to that zone in the position it left,
    /// during the activator's next Standby Phase — not a Summon (a monster you controlled but do not own returns
    /// to its owner's field instead). If that Standby Phase is skipped the card
    /// stays banished (ygopro: the return resets after two Standby Phases). <see cref="Refresh"/> (every board
    /// refresh) frees the zone as soon as the card is no longer banished.</item>
    /// <item><b>Light of Intervention</b> (<see cref="EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown"/>,
    /// ContinuousWhileFaceUp). While a face-up, not negated copy is in a Spell &amp; Trap Zone
    /// (<see cref="LightOfInterventionSource"/>): DuelEngine.TryNormalSummonToZone makes a Set (or Tribute Set)
    /// a Normal (Tribute) Summon in face-up Defense Position; DuelEngine.SpecialSummonToField makes a face-down
    /// Special Summon face-up Defense; and nothing turns a monster face-down (<see cref="CannotTurnFaceDown"/>:
    /// Book of Moon family targets in both target mirrors and at resolution, "change this card to face-down"
    /// ignitions, Swords of Concealing Light, Magical Hats — which then works face-up, as its ygopro script
    /// does).</item>
    /// </list>
    /// </summary>
    public static class LocksAndZonesRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Cold Wave): the activator's Main Phase 1, before any
        /// action has been taken in it.
        /// </summary>
        public const string StartOfMainPhase1 = "StartOfMainPhase1";

        // ───────────────────────── Cold Wave: start of Main Phase 1 ─────────────────────────

        sealed class PhaseMark
        {
            public int Turn;
            public DuelistState TurnPlayer;
            public long Fingerprint;
        }

        static readonly ConditionalWeakTable<DuelEngine, PhaseMark> s_main1 = new();

        /// <summary>
        /// DuelEngine.EnterMain1, right after the Phase becomes Main Phase 1 and before any window opens:
        /// remember the game state at the start of this Main Phase 1.
        /// </summary>
        public static void OnMainPhase1Start(DuelEngine engine)
        {
            if (engine == null) return;
            var mark = s_main1.GetValue(engine, _ => new PhaseMark());
            mark.Turn = engine.TurnNumber;
            mark.TurnPlayer = engine.TurnPlayer;
            mark.Fingerprint = Fingerprint(engine);
        }

        /// <summary>
        /// True during <paramref name="who"/>'s own Main Phase 1 while nothing has happened in it yet: no
        /// Summon, Set, position change or activation by either player (ygopro CheckPhaseActivity).
        /// </summary>
        public static bool AtStartOfMainPhase1(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            if (engine.Phase != DuelPhase.Main1 || engine.TurnPlayer != who) return false;
            if (!s_main1.TryGetValue(engine, out var mark)) return false;
            if (mark.Turn != engine.TurnNumber || mark.TurnPlayer != who) return false;
            return mark.Fingerprint == Fingerprint(engine);
        }

        /// <summary>
        /// Everything a player action can change: each duelist's LP, hand, Deck, GY, banishment, Extra Deck and
        /// Normal Summon use, every zone's card with its face and position, the Chain, every activation ever
        /// placed on a Chain (<see cref="WRLDZ.Duel.Rules.ChainStack.ActivationCount"/>: an ignition that only
        /// changes ATK, like Karate Man's, still counts) and the game-event log. Piles are compared as sets: a
        /// card that goes back to the hand at the end (a cancelled activation) is not an action.
        /// </summary>
        static long Fingerprint(DuelEngine engine)
        {
            unchecked
            {
                var h = (long)1469598103934665603;

                void Mix(long v)
                {
                    h ^= v;
                    h *= 1099511628211;
                }

                void MixList(List<CardInstance> cards)
                {
                    Mix(cards?.Count ?? -1);
                    if (cards == null) return;
                    var ids = new List<int>(cards.Count);
                    foreach (var c in cards) ids.Add(c?.InstanceId ?? 0);
                    ids.Sort();
                    foreach (var id in ids) Mix(id);
                }

                void MixZone(FieldZone z)
                {
                    var c = z?.Occupant;
                    Mix(c?.InstanceId ?? 0);
                    if (c == null) return;
                    Mix(c.FaceUp ? 1 : 2);
                    Mix(c.Position == BattlePosition.Attack ? 3 : 4);
                }

                foreach (var who in new[] { engine.Player, engine.Opponent })
                {
                    if (who == null)
                    {
                        Mix(-7);
                        continue;
                    }

                    Mix(who.LifePoints);
                    Mix(who.DeckCount);
                    Mix(who.ExtraDeck?.Count ?? -1);
                    Mix(who.NormalSummonUsed ? 1 : 0);
                    MixList(who.Hand);
                    MixList(who.Graveyard);
                    MixList(who.Banished);
                    foreach (var z in who.MonsterZones) MixZone(z);
                    foreach (var z in who.SpellTrapZones) MixZone(z);
                    MixZone(who.FieldSpellZone);
                }

                Mix(engine.Chain?.Count ?? -1);
                Mix(engine.Chain?.ActivationCount ?? -1);
                Mix(engine.GameEvents?.Events?.Count ?? -1);
                return h;
            }
        }

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet hook for this batch's conditions. False when
        /// <paramref name="c"/> does not carry one of them.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || c.ActivationCondition != StartOfMainPhase1) return false;
            ok = AtStartOfMainPhase1(engine, who);
            reason = "Only at the start of your Main Phase 1, before anything else happens in it.";
            return true;
        }

        // ───────────────────────── Cold Wave: the lock ─────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn"/>: through the
        /// next turn neither player can activate Spell/Trap Cards or their effects, nor Set Spell/Trap Cards.
        /// </summary>
        public static void RegisterSpellTrapLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var through = engine.TurnNumber + 1;
            if (who.SpellTrapPlayLockedThroughTurn < through)
                who.SpellTrapPlayLockedThroughTurn = through;
            engine.Log(
                $"{source?.Name ?? "Card"}: until {who.Name}'s next turn, neither player can activate or Set " +
                "Spell or Trap Cards.");
        }

        /// <summary>True while a Cold Wave lock registered by either duelist lasts.</summary>
        public static bool SpellTrapLockActive(DuelEngine engine)
        {
            if (engine == null || engine.TurnNumber <= 0) return false;
            return LockedBy(engine.Player, engine.TurnNumber) || LockedBy(engine.Opponent, engine.TurnNumber);
        }

        static bool LockedBy(DuelistState who, int turn) =>
            who != null && who.SpellTrapPlayLockedThroughTurn > 0 && turn <= who.SpellTrapPlayLockedThroughTurn;

        /// <summary>
        /// Cold Wave: <paramref name="card"/> is a Spell/Trap Card and a lock lasts, so neither the card nor
        /// any of its effects can be activated (wherever it is).
        /// </summary>
        public static bool SpellTrapActivationLocked(DuelEngine engine, CardInstance card, out string reason)
        {
            reason = null;
            if (card?.Def == null || card.Def.IsMonster) return false;
            if (!card.Def.IsSpell && !card.Def.IsTrap) return false;
            // SpellTrapAnswersAndActivationLocks batch: Sasuke Samurai #2 — the same lock (re:IsSpellTrapEffect()) until the
            // End Phase of this turn, without Cold Wave's Set lock.
            if (SpellTrapAnswersAndActivationLocksRuntime.TurnSpellTrapLockActive(engine, out reason)) return true;
            if (!SpellTrapLockActive(engine)) return false;
            reason = "Spell and Trap Cards cannot be activated until the Cold Wave lock ends.";
            return true;
        }

        /// <summary>Cold Wave: no Spell/Trap Card can be Set while a lock lasts (by either player, by any means).</summary>
        public static bool SpellTrapSetLocked(DuelEngine engine) => SpellTrapLockActive(engine);

        /// <summary>
        /// Cold Wave, for the trigger paths that apply a Spell/Trap Card's effect without an activation gate
        /// (TextEffectRuntime.FirePhaseTriggersFor, NotifyDraw, the battle damage, return-to-hand and Tribute
        /// Summon notifications, TryResolveSentToGy, TryTriggerFaceUpFieldSummon, Pandemonium's search). The
        /// caller has decided that this effect is an <b>activated</b> one (ygopro TRIGGER_O / TRIGGER_F: it
        /// starts a Chain Link). While the lock lasts it cannot be activated, so it does not happen (ygopro
        /// EFFECT_CANNOT_ACTIVATE, value re:IsSpellTrapEffect). Effects that are not activated (ygopro
        /// EFFECT_TYPE_CONTINUOUS: maintenance costs, Solemn Wishes' LP gain, Stim-Pack's ATK loss, Swords of
        /// Concealing Light's self-destruction) still apply; their callers do not ask. Logs when it stops one.
        /// </summary>
        public static bool SpellTrapTriggerLocked(DuelEngine engine, CardInstance card)
        {
            if (!SpellTrapActivationLocked(engine, card, out var why)) return false;
            engine.Log($"{card.Name}: its effect cannot be activated ({why})");
            return true;
        }

        // ───────────────────────── Dimensionhole ─────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.BanishTargetUntilYourNextStandbyPhase"/>: banish
        /// <paramref name="target"/> (still a monster on the field) until <paramref name="who"/>'s next Standby
        /// Phase and make the zone it left unusable while it stays banished. A Token ceases to exist instead.
        /// </summary>
        public static void BanishUntilNextStandby(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            var controller = target != null ? engine.ControllerOf(target) : null;
            if (controller == null || !controller.TryFindMonster(target, out var zone))
            {
                engine.Log($"{source?.Name ?? "Card"}: the monster is no longer on the field — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var faceUp = target.FaceUp;
            var position = target.Position;
            var token = target.IsToken;
            engine.BanishCard(controller, target);
            var pile = PileHolding(engine, target);
            if (pile == null)
                return; // not banished (an effect stopped it)

            if (token)
            {
                // A Token that leaves the field ceases to exist: nothing returns, the zone stays free.
                pile.Banished.Remove(target);
                engine.Log($"{target.Name} (Token) ceases to exist.");
                engine.NotifyPublic();
                return;
            }

            var record = new TemporaryBanishment
            {
                Card = target,
                Source = source,
                Pile = pile,
                ReturnTo = controller,
                ZoneIndex = zone,
                Position = position,
                FaceUp = faceUp,
                WaitsFor = who,
                ReturnTurn = engine.TurnPlayer == who ? engine.TurnNumber + 2 : engine.TurnNumber + 1
            };
            who.TemporaryBanishments.Add(record);
            controller.MonsterZones[zone].UnusableFor = target;
            engine.Log(
                $"{target.Name} is banished until {who.Name}'s next Standby Phase; its Monster Zone cannot be used.");
            engine.NotifyPublic();
        }

        static DuelistState PileHolding(DuelEngine engine, CardInstance card)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
                if (who?.Banished != null && who.Banished.Contains(card))
                    return who;
            return null;
        }

        /// <summary>
        /// DuelEngine.BeginTurn, after <paramref name="turnPlayer"/>'s Standby Phase triggers: every monster
        /// banished until this player's next Standby Phase returns now. A return whose Standby Phase went by
        /// (skipped) lapses: the monster stays banished, and its zone stays unusable while it does.
        /// </summary>
        public static void OnStandbyPhase(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null) return;
            var returned = false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null || who.TemporaryBanishments.Count == 0) continue;
                foreach (var r in who.TemporaryBanishments.ToArray())
                {
                    if (r == null || !r.ReturnPending) continue;
                    if (r.WaitsFor == turnPlayer && engine.TurnNumber == r.ReturnTurn)
                    {
                        who.TemporaryBanishments.Remove(r);
                        ReturnToField(engine, r);
                        returned = true;
                    }
                    else if (engine.TurnNumber > r.ReturnTurn)
                        r.ReturnPending = false;
                }
            }

            if (returned) engine.NotifyPublic();
        }

        /// <summary>
        /// Duel.ReturnToField for one temporary banishment (also DcrSpellTrapUtility: Interdimensional Matter Transporter,
        /// Different Dimension Gate): the zone it left, else the first free one; no zone: the GY.
        /// </summary>
        internal static void ReturnToField(DuelEngine engine, TemporaryBanishment r)
        {
            var zones = r.ReturnTo?.MonsterZones;
            var inRange = zones != null && r.ZoneIndex >= 0 && r.ZoneIndex < zones.Length;
            if (inRange && zones[r.ZoneIndex].UnusableFor == r.Card)
                zones[r.ZoneIndex].UnusableFor = null;
            if (r.Pile?.Banished == null || !r.Pile.Banished.Contains(r.Card))
                return; // it left the banishment another way: nothing returns
            // ygopro Duel.ReturnToField puts the card back in the zone it left, on the side of the player who
            // controlled it. No effect gives that player control any more (Change of Heart ended when it left the
            // field), so control goes straight back to its owner (ocgcore card::refresh_control_status): a monster
            // you controlled but do not own returns to its owner's field, in the first free Monster Zone.
            var dest = r.Pile;
            var destZones = dest.MonsterZones;
            if (destZones == null) return;
            var idx = dest == r.ReturnTo
                ? (inRange && zones[r.ZoneIndex].IsEmpty && !ZoneCapsRuntime.CapReached(engine, zones) // ZoneCaps: caps
                    ? r.ZoneIndex
                    : engine.FirstEmpty(zones))
                : engine.FirstEmpty(destZones);
            r.Pile.Banished.Remove(r.Card);
            if (idx < 0)
            {
                // ygopro Duel.ReturnToField: with no zone to return to, the card goes to the GY.
                r.Card.FaceUp = true;
                r.Pile.Graveyard.Add(r.Card);
                GyEntryStamps.Entered(engine, r.Card, GyEntryReason.Rule); // BattleDestroyedGyRecovery (v162)
                engine.Log($"{r.Card.Name} cannot return to the field and is sent to the GY.");
                return;
            }

            engine.PlaceReturnedMonster(dest, r.Pile, r.Card, idx, r.Position, r.FaceUp);
        }

        /// <summary>
        /// Every board refresh: a Dimensionhole record whose monster is no longer banished (returned by another
        /// effect, shuffled into the Deck …) ends, and its zone can be used again. A zone marked unusable with no
        /// record behind it is freed too.
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine == null) return;
            HashSet<CardInstance> live = null;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null || who.TemporaryBanishments.Count == 0) continue;
                for (var i = who.TemporaryBanishments.Count - 1; i >= 0; i--)
                {
                    var r = who.TemporaryBanishments[i];
                    if (r?.Card != null && r.Pile?.Banished != null && r.Pile.Banished.Contains(r.Card))
                    {
                        (live ??= new HashSet<CardInstance>()).Add(r.Card);
                        continue;
                    }

                    who.TemporaryBanishments.RemoveAt(i);
                }
            }

            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who?.MonsterZones == null) continue;
                foreach (var z in who.MonsterZones)
                {
                    if (z?.UnusableFor == null) continue;
                    if (live == null || !live.Contains(z.UnusableFor))
                        z.UnusableFor = null;
                }
            }
        }

        // ───────────────────────── Light of Intervention ─────────────────────────

        /// <summary>
        /// The face-up card whose <see cref="EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown"/> applies right
        /// now (in a Spell &amp; Trap Zone, its activation resolved, not negated), or null.
        /// </summary>
        public static CardInstance LightOfInterventionSource(DuelEngine engine)
        {
            if (engine == null) return null;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    if (ContinuousNegationRuntime.NegatedBy(engine, st) != null) continue;
                    var prog = CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                        if (c != null && c.Action == EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown)
                            return st;
                }
            }

            return null;
        }

        /// <summary>
        /// Light of Intervention applies: no monster can be Set (a Set is made as a Normal Summon in face-up
        /// Defense Position, <see cref="SetBecomesFaceUpDefenseSummon"/>), a face-down Special Summon is made
        /// face-up (<see cref="FaceDownSpecialSummonBecomesFaceUp"/>), and no monster on the field can be
        /// changed to face-down (<see cref="CannotTurnFaceDown"/>).
        /// </summary>
        public static bool MonstersCannotBeSetOrTurnedFaceDown(DuelEngine engine) =>
            LightOfInterventionSource(engine) != null;

        /// <summary>DuelEngine.TryNormalSummonToZone: this Set is made as a Normal (Tribute) Summon in face-up Defense.</summary>
        public static bool SetBecomesFaceUpDefenseSummon(DuelEngine engine) =>
            MonstersCannotBeSetOrTurnedFaceDown(engine);

        /// <summary>DuelEngine.SpecialSummonToField: a face-down Special Summon is made in face-up Defense Position.</summary>
        public static bool FaceDownSpecialSummonBecomesFaceUp(DuelEngine engine) =>
            MonstersCannotBeSetOrTurnedFaceDown(engine);

        /// <summary>
        /// <paramref name="monster"/> (a monster on the field) cannot be changed to face-down Defense Position by
        /// an effect (ygopro IsCanTurnSet false): Book of Moon cannot target it, Swords of Concealing Light leaves
        /// it, and a "change this card to face-down" effect cannot be activated.
        /// </summary>
        public static bool CannotTurnFaceDown(DuelEngine engine, CardInstance monster) =>
            monster != null && MonstersCannotBeSetOrTurnedFaceDown(engine);

        /// <summary>
        /// <paramref name="card"/>'s activation changes its target to face-down Defense Position (Book of Moon's
        /// <see cref="EffectActionKind.SetTargetFaceDownDefense"/>): the legacy target mirror
        /// (SpellTrapEffects.CollectLegalTargets) filters its pick like TextEffectRuntime.CollectTargets does.
        /// </summary>
        public static bool TurnsTargetFaceDown(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                if (c != null && c.RequiresTargetChoice && c.Action == EffectActionKind.SetTargetFaceDownDefense)
                    return true;
            return false;
        }

        /// <summary>A Monster Zone of <paramref name="who"/> made unusable by this batch (UI / validator reads).</summary>
        public static bool MonsterZoneUnusable(DuelistState who, int index) =>
            who?.MonsterZones != null && index >= 0 && index < who.MonsterZones.Length &&
            who.MonsterZones[index]?.UnusableFor != null;
    }
}
