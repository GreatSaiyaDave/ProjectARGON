using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleRiders" (RDS / FET): an end-of-Damage-Step counter removal, an end-of-Damage-Step position change
    /// on the monster this card battled, battle-damage triggers on the monster that inflicted the damage, a named piercing
    /// grant, a conditional attack shield with a DEF 0 ignition, and extra attacks per Equip Card. Every atom is a whole
    /// sentence (or the whole text), anchored at both ends, so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="BattleRidersRuntime"/>.
    /// <list type="bullet">
    /// <item>B.E.S. Big Core (c14148099): aux.EnableBESRemove (SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END, IsRelateToBattle;
    /// IsCanRemoveCounter → RemoveCounter, else Destroy) → <see cref="EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep"/>.
    /// Its counter sentence is SummonTriggersAndTributesAtoms' (Normal Summon only) and "Cannot be destroyed by battle" the
    /// shared template.</item>
    /// <item>Kangaroo Champ (c95789089): Prickle Fairy's end-of-Damage-Step change of the battled monster to face-up Defense
    /// Position (<see cref="EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep"/>), its FET wording.</item>
    /// <item>Poison Fangs (c76539047): FIELD TRIGGER_F EVENT_BATTLE_DAMAGE, ep ~= tp and eg:GetFirst():IsRace(BEAST) →
    /// Robbin' Zombie's <see cref="EffectClause.AnyControllerMonsterBattleDamage"/> shape with the Type in
    /// <see cref="EffectClause.RaceFilter"/>, burning 500.</item>
    /// <item>Spiral Spear Strike (c49328340): FIELD EFFECT_PIERCE for the three printed names
    /// (<see cref="EffectActionKind.GrantPiercingToYourNamedMonsters"/>) and FIELD TRIGGER_F EVENT_BATTLE_DAMAGE — your
    /// "Gaia the Dragon Champion" that battled a Defense Position monster → Graceful Charity's
    /// <see cref="EffectActionKind.DrawThenDiscard"/> (draw 2; only if both were drawn, discard 1).</item>
    /// <item>Catnipped Kitty (c96501677): Solar Flare Dragon's shield (another face-up monster of the Type you control) and
    /// an ignition, once per turn, targeting 1 face-up monster the opponent controls with DEF above 0
    /// (<see cref="EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn"/>).</item>
    /// <item>Armed Samurai - Ben Kei (c84430950): <see cref="EffectActionKind.ExtraAttacks"/> with
    /// <see cref="EffectClause.ExtraAttacksPerEquippedCard"/> (EFFECT_EXTRA_ATTACK = c:GetEquipCount()).</item>
    /// </list>
    /// </summary>
    public static class BattleRidersAtoms
    {
        /// <summary>A sentence start: the text start or after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>A sentence end: "." (or the text end) followed by a space or the text end.</summary>
        const string E = @"(?:\.|$)(?=\s|$)";

        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";

        /// <summary>The monster Types, as printed (two-word Types before their first word).</summary>
        const string Race =
            @"(Aqua|Beast-Warrior|Winged Beast|Beast|Dinosaur|Dragon|Fairy|Fiend|Fish|Insect|Machine|Plant|Psychic|Pyro|" +
            @"Reptile|Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Wyrm|Zombie)";

        /// <summary>A printed list of quoted names: "A", "B", or "C" / "A" or "B".</summary>
        const string QuotedNames = @"(""[^""]+""(?:(?:,? or |, )""[^""]+"")*)";

        static List<string> Names(string printed) =>
            Regex.Matches(printed ?? "", @"""([^""]+)""").Cast<Match>().Select(x => x.Groups[1].Value.Trim())
                .Where(n => n.Length > 0).ToList();

        static int Parse(Match m, int group) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : 0;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // B.E.S. Big Core (c14148099): aux.EnableBESRemove — SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END, condition
            // c:IsRelateToBattle(); operation: still related → IsCanRemoveCounter(tp,0x1f,1) ? RemoveCounter : Destroy.
            // The two printed sentences are one effect.
            new AtomRegistration.Atom(
                S + @"At the end of the Damage Step, if this card battled: Remove (\d+) counters? from this card\. If you " +
                @"cannot, destroy it" + E,
                m => Parse(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.EndOfDamageStep,
                        Action = EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1),
                        MakesChainLink = true
                    }),

            // Kangaroo Champ (c95789089): SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END; aux.dsercon and the battle target
            // IsRelateToBattle; operation: still related and IsAttackPos → ChangePosition(POS_FACEUP_DEFENSE). Prickle
            // Fairy's e1 (attacker or attack target).
            new AtomRegistration.Atom(
                S + @"A monster that battles with this card is changed to Defense Position after damage calculation" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OpponentBattlingMonster,
                    MakesChainLink = true
                }),

            // Poison Fangs (c76539047): Continuous Spell (activation EVENT_FREE_CHAIN does nothing); FIELD TRIGGER_F
            // EVENT_BATTLE_DAMAGE in the S/T Zone, ep ~= tp and eg:GetFirst():IsRace(RACE_BEAST); Damage(1-tp, 500).
            new AtomRegistration.Atom(
                Begin + @"Each time an? " + Race + @"(?:-Type)? monster inflicts Battle Damage to your opponent, inflict " +
                @"(\d+) damage to your opponent" + Finish,
                m => Parse(m, 2) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.ThisCardInflictsBattleDamage,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 2),
                        RaceFilter = m.Groups[1].Value,
                        AnyControllerMonsterBattleDamage = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    }),

            // Spiral Spear Strike (c49328340), its whole text. e2: FIELD EFFECT_PIERCE, range SZONE, target range
            // (MZONE, 0), c:IsCode(6368038, 16589042, 66889139). e3: FIELD TRIGGER_F EVENT_BATTLE_DAMAGE, condition
            // tc = eg:GetFirst() is yours, its battle target is in Defense Position and tc:IsCode(66889139); operation:
            // still related and a non-empty Deck → Draw(tp, 2) == 2 → ShuffleHand, BreakEffect, DiscardHand(1).
            // "with this effect" ties the second sentence to the first, so the atom is the whole text.
            AtomRegistration.Atom.Sequence(
                Begin + @"If " + QuotedNames + @" you control attacks a Defense Position monster, inflict piercing " +
                @"battle damage to your opponent\.\s*If ""([^""]+)"" inflicts battle damage with this effect: Draw " +
                @"(\d+) cards, then discard (\d+) cards?" + Finish,
                m =>
                {
                    var names = Names(m.Groups[1].Value);
                    var drawer = m.Groups[2].Value.Trim();
                    var draw = Parse(m, 3);
                    var discard = Parse(m, 4);
                    // "with this effect": the drawing monster must be one of the monsters the piercing applies to.
                    if (names.Count < 2 || draw <= 0 || discard <= 0 ||
                        !names.Exists(n => string.Equals(n, drawer, System.StringComparison.OrdinalIgnoreCase)))
                        return null;
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.GrantPiercingToYourNamedMonsters,
                            NamedCard = string.Join("|", names),
                            StaysOnField = true,
                            MakesChainLink = false
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ThisCardInflictsBattleDamage,
                            Action = EffectActionKind.DrawThenDiscard,
                            Side = EffectSide.Controller,
                            Amount = draw,
                            ThenDiscardCount = discard,
                            NamedCard = drawer,
                            AnyControllerMonsterBattleDamage = true,
                            RequiresDealerBattledDefensePosition = true,
                            StaysOnField = true,
                            MakesChainLink = true
                        }
                    };
                }),

            // Catnipped Kitty (c96501677) e1: SINGLE EFFECT_CANNOT_BE_BATTLE_TARGET, range MZONE, condition a face-up Beast
            // you control other than this card, aux.imval1 — Solar Flare Dragon's shield (no direct attack past it).
            new AtomRegistration.Atom(
                S + @"This card cannot be attacked if there is another " + Race + @"(?:-Type)? monster on your side of the " +
                @"field" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeAttackTarget,
                    WhileCondition = AttackShieldsAndDamageZeroRuntime.YouControlAnotherFaceUpRace,
                    ConditionName = m.Groups[1].Value,
                    AllowsDirectAttackWhileProtected = false,
                    MakesChainLink = false
                }),

            // Catnipped Kitty (c96501677) e2: IGNITION, CARD_TARGET, SetCountLimit(1); target Card.HasNonZeroDefense (a
            // face-up monster with DEF above 0) the opponent controls; operation: still face-up and related →
            // EFFECT_SET_DEFENSE_FINAL 0, RESET_EVENT+RESETS_STANDARD+RESET_PHASE+PHASE_END.
            new AtomRegistration.Atom(
                S + @"Once per turn, you can make the DEF of 1 monster on your opponent's side of the field 0 until the " +
                @"end of this turn" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerInstance,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Armed Samurai - Ben Kei (c84430950): SINGLE EFFECT_EXTRA_ATTACK, range MZONE, value c:GetEquipCount().
            new AtomRegistration.Atom(
                Begin + @"For each Equip Card equipped to this card, it gains (\d+) additional attacks? during each " +
                @"Battle Phase" + Finish,
                m => Parse(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ExtraAttacks,
                        Amount = Parse(m, 1),
                        ExtraAttacksPerEquippedCard = true,
                        StaysOnField = true,
                        MakesChainLink = false
                    }),
        };
    }
}
