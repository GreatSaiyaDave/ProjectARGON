using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleDamageEvents" (IOC / AST): battle-damage triggers and battle-damage doublers. Each pattern is
    /// anchored to the start and the end of the whole printed text, so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="BattleDamageEventsRuntime"/>.
    /// </summary>
    public static class BattleDamageEventsAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";

        static int Parse(Match m, int group, int fallback) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n)
                ? n
                : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Begone, Knave! (c20374520): Continuous Trap; FIELD+TRIGGER_F EVENT_BATTLE_DAMAGE in the S/T Zone, any
            // monster (either player's) that inflicted battle damage to either player; Duel.SetTargetCard(eg) without
            // EFFECT_FLAG_CARD_TARGET (not a target); SendtoHand if it is still on the field.
            new AtomRegistration.Atom(
                Begin + @"If a monster inflicts battle damage to a player: Return that monster to the hand" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.ReturnToHand,
                    Side = EffectSide.Either,
                    AnyMonsterBattleDamageToAnyPlayer = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Des Counterblow (c39131963): Continuous Trap; FIELD+TRIGGER_F EVENT_BATTLE_DAMAGE with
            // Duel.GetAttackTarget()==nil (a direct attack, either player's monster); not a target; Duel.Destroy.
            new AtomRegistration.Atom(
                Begin + @"Destroy any monster that inflicts Battle Damage to a player's Life Points by attacking " +
                @"directly" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Either,
                    AnyMonsterBattleDamageToAnyPlayer = true,
                    RequiresDirectAttack = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Robbin' Zombie (c83258273): Continuous Trap; FIELD+TRIGGER_F EVENT_BATTLE_DAMAGE, ep ~= tp and the damaging
            // monster is yours; DiscardDeck(1-tp, 1). The Robbin' Goblin shape (AnyControllerMonsterBattleDamage).
            new AtomRegistration.Atom(
                Begin + @"If your monster inflicts battle damage to your opponent: Send the top card of their Deck " +
                @"to the (?:GY|Graveyard)" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.SendFromTopOfDeckToGy,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    AnyControllerMonsterBattleDamage = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Sasuke Samurai #3 (c77379481): SINGLE+TRIGGER_F EVENT_BATTLE_DAMAGE, ep ~= tp; the opponent draws 7 minus
            // their hand size at resolution (nothing at 7 or more). The Yamata Dragon arm with the opponent drawing.
            new AtomRegistration.Atom(
                Begin + @"When this card inflicts Battle Damage to your opponent's Life Points, your opponent draws " +
                @"cards until (?:his/her|their) hand has (\d+) cards" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.DrawUntilHandSize,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 7),
                    MakesChainLink = true
                }),

            // Aswan Apparition (c88236094) / Nubian Guard (c51616747): SINGLE+TRIGGER_O EVENT_BATTLE_DAMAGE, ep ~= tp,
            // EFFECT_FLAG_CARD_TARGET on 1 Trap / Continuous Spell in your GY; SendtoDeck(SEQ_DECKTOP) if it is still
            // there. A human gets a yes/no pick (BattleDamageEventsRuntime.TryOfferOptionalTarget).
            new AtomRegistration.Atom(
                Begin + @"If this card inflicts Battle Damage to your opponent's Life Points, you can return 1 " +
                @"(Trap|Continuous Spell) Card from your (?:Graveyard|GY) to the top of your Deck" + Finish,
                m =>
                {
                    var continuousSpell = m.Groups[1].Value.StartsWith("Continuous", System.StringComparison.OrdinalIgnoreCase);
                    return new EffectClause
                    {
                        Timing = EffectTiming.ThisCardInflictsBattleDamage,
                        Action = EffectActionKind.PlaceTargetOnTopOfDeck,
                        Side = EffectSide.Controller,
                        Zone = continuousSpell ? EffectZoneFilter.ControllerGySpells : EffectZoneFilter.ControllerGyTraps,
                        TargetCardKind = continuousSpell ? BattleDamageEventsRuntime.ContinuousSpellKind : "Trap",
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = 1,
                        IsOptional = true,
                        MakesChainLink = true
                    };
                }),

            // Piranha Army (c50823978): SINGLE EFFECT_CHANGE_BATTLE_DAMAGE, condition Duel.GetAttackTarget()==nil,
            // aux.ChangeBattleDamage(1, DOUBLE_DAMAGE): the damage its direct attack inflicts to its controller's opponent.
            new AtomRegistration.Atom(
                Begin + @"The Battle Damage this card inflicts by attacking your opponent's Life Points directly is " +
                @"doubled" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DoubleBattleDamageToOpponent,
                    Side = EffectSide.Opponent,
                    RequiresDirectAttack = true,
                    MakesChainLink = false
                }),

            // Stone Statue of the Aztecs (c31812496): SINGLE EFFECT_CHANGE_BATTLE_DAMAGE, condition
            // Duel.GetAttackTarget()==e:GetHandler(), aux.ChangeBattleDamage(1, DOUBLE_DAMAGE): the battle damage its
            // controller's opponent (the attacking player) takes from that battle is doubled.
            new AtomRegistration.Atom(
                Begin + @"Double any Battle Damage your opponent takes when they attack this monster" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DoubleBattleDamageToOpponent,
                    Side = EffectSide.Opponent,
                    RequiresThisIsAttackTarget = true,
                    MakesChainLink = false
                }),
        };
    }
}
