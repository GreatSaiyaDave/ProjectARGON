using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpiritsAndMisc": the LOD Spirit battle-damage riders (Yata-Garasu, Hino-Kagu-Tsuchi, Yamata
    /// Dragon) and Kiseitai. The Spirit sentences themselves ("This card cannot be Special Summoned." and
    /// "This card returns to its owner's hand during the End Phase of the turn it is Normal Summoned or flipped
    /// face-up.") compile through the shared structural lock and PhaseTriggerTemplates' Spirit return; each atom
    /// here is one whole sentence (anchored at a sentence start and end), or the whole card text for Kiseitai,
    /// so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="SpiritsAndMiscRuntime"/>.
    /// Major Riot is not here: the engine has no activation window for "when your monster(s) are returned from
    /// the field to your hand by your opponent's effect" (see the batch notes).
    /// </summary>
    public static class SpiritsAndMiscAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string SentenceStart = @"(?<=^|\.\s)";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string SentenceEnd = @"\.?(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Yata-Garasu (3078576). ygopro: SINGLE+TRIGGER_F on EVENT_BATTLE_DAMAGE, ep == 1-tp; registers
            // EFFECT_SKIP_DP on the opponent until the End Phase of their turn (see
            // SpiritsAndMiscRuntime.BattleDamageSkipLapses). Not Fenrir (battle destroy).
            new AtomRegistration.Atom(
                SentenceStart +
                @"When this card inflicts Battle Damage to your opponent, they skip their next Draw Phase" +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.SkipOpponentNextDrawPhase,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Hino-Kagu-Tsuchi (75745607). ygopro: TRIGGER_F on EVENT_BATTLE_DAMAGE registers an EVENT_PREDRAW
            // discard of the opponent's whole hand, reset at the end of the next Draw Phase.
            new AtomRegistration.Atom(
                SentenceStart +
                @"If this card inflicts Battle Damage to your opponent, they discard all cards in their hand " +
                @"during the next Draw Phase, before they draw" +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.DiscardOpponentHandAtNextDrawPhase,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Yamata Dragon (76862289). ygopro: TRIGGER_F on EVENT_BATTLE_DAMAGE; the controller draws 5 minus
            // their hand size at resolution (nothing at 5 or more).
            new AtomRegistration.Atom(
                SentenceStart +
                @"When this card inflicts Battle Damage to your opponent, draw cards until you have (\d+) cards " +
                @"in your hand" +
                SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.DrawUntilHandSize,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 5),
                    MakesChainLink = true
                }),
            // Kiseitai (4266839), whole text. ygopro: SINGLE+TRIGGER_F on EVENT_BATTLE_CONFIRM while it is the
            // attack target in face-down Defense Position → Equip to the attacker (destroy it instead with no free
            // Spell & Trap Zone or a face-down attacker); the Equip then gains a FIELD+TRIGGER_F Standby effect,
            // range Spell & Trap Zone, on the opponent's turn: recover half the equipped monster's ATK.
            AtomRegistration.Atom.Sequence(
                @"^\s*When your opponent's monster attacks this card in face-down Defense Position, this card becomes " +
                @"an Equip Spell Card equipped to the attacking monster \(No damage calculation is conducted\)\. " +
                @"During each of your opponent's Standby Phases, increase your Life Points by half of the ATK of " +
                @"the monster equipped with this card\.?\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.StartOfDamageStep,
                        Action = EffectActionKind.BecomeEquipOnFaceDownAttacker,
                        RequiresThisIsAttackTarget = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.GainLpHalfEquippedMonsterAtk,
                        Side = EffectSide.Controller,
                        OpponentTurnOnly = true,
                        MakesChainLink = true
                    }
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n)
                ? n
                : fallback;
    }
}
