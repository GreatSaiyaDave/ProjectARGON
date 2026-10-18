namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LocksRedirectsAndActivity batch: a player-wide discard-cost lock (Goblin of Greed), the leave-field redirect funnel
    /// (Dark Magician of Chaos) and the battle GY-to-Deck redirect built on it (Grave Protector), and the per-player attack
    /// activity record with its oath (Burst Stream of Destruction). Every atom is the whole card text, so a card with any
    /// other sentence is not compiled here. Runtime: <see cref="LocksRedirectsAndActivityRuntime"/>.
    /// </summary>
    public static class LocksRedirectsAndActivityAtoms
    {
        const string Gy = @"(?:GY|Graveyard)";

        /// <summary>The start of a sentence of the card text (the text start, or after a full stop).</summary>
        const string SentenceStart = @"(?<=^\s*|\.\s*)";

        /// <summary>The end of a sentence of the card text (whitespace or the text end follows).</summary>
        const string SentenceEnd = @"(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Goblin of Greed (c425934): EFFECT_CANNOT_DISCARD_HAND, EFFECT_FLAG_PLAYER_TARGET, target range (1,1), in the
            // Monster Zone — neither player can activate a card or effect that discards from the hand as its cost
            // (the discard-cost lock with Side = Both).
            new AtomRegistration.Atom(
                @"^\s*As long as this card remains face-up on your side of the field, neither player can activate an " +
                @"effect by discarding from (?:his/her|their) hand\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotDiscardAsCost,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.None
                }),
            // Grave Protector (c11448373): EFFECT_TO_GRAVE_REDIRECT, value LOCATION_DECKSHF, IGNORE_IMMUNE, both Monster
            // Zones, target IsReason(REASON_BATTLE).
            new AtomRegistration.Atom(
                @"^\s*Any monster destroyed by battle is shuffled into the Deck instead of going to the " + Gy + @"\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.BattleDestroyedMonstersShuffleIntoDeckInstead,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.None
                }),
            // Burst Stream of Destruction (c17655904): condition a face-up "Blue-Eyes White Dragon" you control (s.condition,
            // LOCATION_ONFIELD); cost GetCustomActivityCount(id,tp,ACTIVITY_ATTACK)==0 (counterfilter: not that name) and the
            // oath EFFECT_CANNOT_ATTACK_ANNOUNCE on every monster of that name (both Monster Zones, reset at the End Phase);
            // activate: destroy every monster the opponent controls (no target).
            new AtomRegistration.Atom(
                @"^\s*If you control ""(?<n>[^""]+)"": Destroy all monsters your opponent controls\.\s*" +
                @"""\k<n>"" you control cannot attack the turn you activate this card\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldMonsters,
                    RequiresFaceUpName = m.Groups["n"].Value,
                    RequiresControllerNamedCard = true,
                    ActivationCondition = LocksRedirectsAndActivityRuntime.NoNamedAttackThisTurn,
                    NamedCannotAttackTurnActivated = m.Groups["n"].Value
                }),
            // Dark Magician of Chaos (c40737112). One atom per sentence (each anchored to whole sentences of the text), so the
            // PSCT "You can" / "target" flags read from each clause's own sentence (the battle banish is forced and does not
            // target). e1 FIELD TRIGGER_O on EVENT_PHASE+PHASE_END in the Monster Zone, CARD_TARGET, SetCountLimit(1,id),
            // cost = the aux.sumreg flag of a Normal / Special Summon this turn (spent); target 1 Spell in your GY, add it to
            // the hand.
            new AtomRegistration.Atom(
                SentenceStart + @"During the End Phase, if this card was Normal or Special Summoned this turn: You can " +
                @"target 1 Spell in your " + Gy + @"; add it to your hand\.\s*" +
                @"You can only use this effect of ""(?<n>[^""]+)"" once per turn\." + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.AddGySpellToHandAtEndPhaseIfSummonedThisTurn,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.None,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    IsOptional = true,
                    MakesChainLink = true,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerCardName,
                    NamedCard = m.Groups["n"].Value
                }),
            // e2 SINGLE TRIGGER_F on EVENT_BATTLED: the battled monster is battle destroyed (STATUS_BATTLE_DESTROYED,
            // STATUS_OPPO_BATTLE) — banish it (no target).
            new AtomRegistration.Atom(
                SentenceStart + @"If this card destroys an opponent's monster by battle, after damage calculation: Banish " +
                @"that opponent's monster\." + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.AfterDamageCalculation,
                    Action = EffectActionKind.Banish,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OpponentBattlingMonster,
                    MakesChainLink = true,
                    RequiresBattledMonsterDestroyed = true
                }),
            // e3 EFFECT_LEAVE_FIELD_REDIRECT to LOCATION_REMOVED while face-up (CANNOT_DISABLE).
            new AtomRegistration.Atom(
                SentenceStart + @"If this face-up card would leave the field, banish it instead\.?" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.BanishThisInsteadOfLeavingField,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.None
                }),
        };
    }
}
