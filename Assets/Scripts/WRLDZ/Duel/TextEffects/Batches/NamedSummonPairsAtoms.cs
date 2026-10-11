using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "NamedSummonPairs". Whole-sentence atoms; runtime in <see cref="NamedSummonPairsRuntime"/>.
    /// The "Must be Special Summoned with "X" and cannot be Special Summoned by other ways" sentence itself stays a summon
    /// restriction (PsctGrammar); the engine lets only X's named Special Summon through
    /// (<see cref="NamedSummonPairsRuntime.NamedSummonerIsSummoning"/>).
    /// <list type="bullet">
    /// <item>A Deal with Dark Ruler (c6850209): EVENT_FREE_CHAIN Quick-Play; checkop records a Level 8+ monster sent from
    /// the Monster Zone to the GY by its previous controller (reset each turn); Special Summon 1 "Berserk Dragon" from the
    /// hand or Deck (nocheck).</item>
    /// <item>Berserk Dragon (c85605684 e3): FIELD TRIGGER_F at its controller's End Phase, EFFECT_UPDATE_ATTACK -500,
    /// RESETS_STANDARD_DISABLE.</item>
    /// <item>Contract with Exodia (c33244944): condition = the 5 pieces in your GY; Special Summon 1 "Exodia Necross" from
    /// your hand (nocheck, nolimit).</item>
    /// <item>Exodia Necross (c12600382): EFFECT_INDESTRUCTABLE_BATTLE, EFFECT_INDESTRUCTABLE_EFFECT vs Spell/Trap effects,
    /// TRIGGER_F at its controller's Standby Phase +500 ATK (RESETS_STANDARD_DISABLE), EFFECT_SELF_DESTROY while a piece is
    /// missing from its controller's GY.</item>
    /// <item>Mirage Knight (c49217579): EFFECT_UPDATE_ATTACK during PHASE_DAMAGE_CAL = the battle target's GetBaseAttack;
    /// FIELD TRIGGER_F at any End Phase, banish itself if GetBattledGroupCount() &gt; 0.</item>
    /// <item>Dedication through Light and Darkness (c69542930): cost Tribute 1 "Dark Magician"; Special Summon 1 "Dark
    /// Magician of Chaos" from the hand, Deck or GY.</item>
    /// </list>
    /// </summary>
    public static class NamedSummonPairsAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";
        const string Q = @"""([^""]+)""";
        const string Gy = @"(?:GY|Graveyard)";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // A Deal with Dark Ruler: the turn record (condition, checked at activation) and the named Special Summon.
            new AtomRegistration.Atom(
                Start + @"If a Level (\d+) or higher monster under your control was sent to the " + Gy +
                @" this turn: Special Summon 1 " + Q + @" from your hand or Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value,
                    FromHand = true,
                    FromDeck = true,
                    ActivationCondition = NamedSummonPairsRuntime.YourLevelOrHigherMonsterSentToGyThisTurn,
                    ConditionAmount = Parse(m, 1, 8),
                    MakesChainLink = true
                }),

            // Contract with Exodia: all 5 named cards in your GY; Special Summon the named monster from your hand.
            new AtomRegistration.Atom(
                Start + @"If you have all 5 of " + Q + @", " + Q + @", " + Q + @", " + Q + @",? and " + Q +
                @" in your " + Gy + @": Special Summon 1 " + Q + @" from your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[6].Value,
                    FromHand = true,
                    ActivationCondition = NamedSummonPairsRuntime.AllNamedInYourGy,
                    ConditionName = string.Join("|", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value,
                        m.Groups[4].Value, m.Groups[5].Value),
                    MakesChainLink = true
                }),

            // Berserk Dragon: its controller's End Phase, a lasting -N ATK on itself (the Zombyra arm).
            new AtomRegistration.Atom(
                Start + @"During each of your End Phases, this card loses (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = -Parse(m, 1, 0),
                    MakesChainLink = true
                }),

            // Exodia Necross sentence 3: battle protection plus Spell/Trap effect destruction protection.
            AtomRegistration.Atom.Sequence(
                Start + @"Cannot be destroyed by battle or Spell/Trap effects?" + End,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.CannotBeDestroyedByBattle,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.CannotBeDestroyedBySpellTrapEffects,
                        MakesChainLink = false
                    }
                }),

            // Exodia Necross sentence 4: its controller's Standby Phase, a lasting +N ATK on itself (once per turn: one
            // Standby Phase per turn).
            new AtomRegistration.Atom(
                Start + @"Once per turn, during your Standby Phase: This card gains (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 0),
                    MakesChainLink = true
                }),

            // Exodia Necross sentence 5: EFFECT_SELF_DESTROY unless every named card is in your GY.
            new AtomRegistration.Atom(
                Start + @"Destroy this card unless all 5 of these cards are in your " + Gy + @"\s*[-–—:]\s*" + Q +
                @", " + Q + @", " + Q + @", " + Q + @",? and " + Q + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SelfDestroyUnlessAllNamedInYourGy,
                    NamedCard = string.Join("|", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value,
                        m.Groups[4].Value, m.Groups[5].Value),
                    MakesChainLink = false
                }),

            // Mirage Knight sentence 3: the battle target's original ATK, during damage calculation only.
            new AtomRegistration.Atom(
                Start + @"During damage calculation only, this card gains ATK equal to the original ATK of the " +
                @"opponent['’]s monster it is battling" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainBattleTargetOriginalAtkDuringDamageCalculation,
                    MakesChainLink = false
                }),

            // Mirage Knight sentence 4: either player's End Phase, banish itself if it battled this turn.
            new AtomRegistration.Atom(
                Start + @"During the End Phase of a turn this card attacked or was attacked: Banish this card" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.BanishThisAtEndPhaseIfBattledThisTurn,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // Dedication through Light and Darkness: cost Tribute 1 named monster; Special Summon 1 named monster from
            // the hand, Deck or GY.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 " + Q + @"; Special Summon 1 " + Q + @" from your hand, Deck, or " + Gy + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value,
                    FromHand = true,
                    FromDeck = true,
                    FromGrave = true,
                    RequiresTributeCount = 1,
                    TributeNamedCard = m.Groups[1].Value,
                    MakesChainLink = true
                }),
        };
    }
}
