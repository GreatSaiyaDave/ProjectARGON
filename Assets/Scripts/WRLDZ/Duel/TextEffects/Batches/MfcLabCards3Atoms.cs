using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "MfcLabCards3" (v168): remaining unimplemented cards from the lab/character duelist decks.
    /// <list type="bullet">
    /// <item>Harpie Lady 2: name treated as "Harpie Lady"; negate effects of Flip monsters this card destroys by battle.</item>
    /// <item>Dark Magician Girl: gains 300 ATK for each "Dark Magician" or "Magician of Black Chaos" in either GY.</item>
    /// <item>Blast Sphere: face-down Defense attacked; equip this card to attacker; next opponent Standby Phase destroy and inflict ATK as damage.</item>
    /// <item>Vampire Lord: battle damage → declare type → opponent mills 1 of that type from Deck; GY revival once per turn after being destroyed by opponent's effect.</item>
    /// </list>
    /// Runtime: <see cref="MfcLabCards3Runtime"/>.
    /// </summary>
    public static class MfcLabCards3Atoms
    {
        const string SentenceStart = @"(?<=^|\.\s)";
        const string SentenceEnd = @"(?:\.|$)(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Harpie Lady 2 (c27927359): name treated as "Harpie Lady"; negate Flip monster effects destroyed by battle with this card.
            AtomRegistration.Atom.Sequence(
                SentenceStart + @"This card's name is always treated as ""([^""]+)""\.?\s*" +
                @"Negate the effects of Flip monsters this card destroys by battle" + SentenceEnd,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.AlwaysTreatedAsName,
                        TreatedAsName = m.Groups[1].Value.Trim(),
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle,
                        Side = EffectSide.Controller,
                        AffectsThisCardOnly = true,
                        TargetCardKind = "Flip",
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // — 26439287 Proto-Cyber Dragon: This card's name becomes "Cyber Dragon" while on the field.
            new AtomRegistration.Atom(
                SentenceStart + @"This card's name becomes ""([^""]+)"" while on the field" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AlwaysTreatedAsName,
                    TreatedAsName = m.Groups[1].Value.Trim(),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Dark Magician Girl (c38033121): "Gains 300 ATK for every "Dark Magician" or "Magician of Black Chaos"
            // in the GY." — ATK only (no DEF), either GY (ApplyNamedGyGrowth counts both players' GYs).
            new AtomRegistration.Atom(
                SentenceStart + @"Gains (\d+) ATK for every ""([^""]+)"" or ""([^""]+)"" in the GY" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainAtkDefPerNamedInBothGyWhileFaceUp,
                    Amount = int.Parse(m.Groups[1].Value),
                    NamedCard = m.Groups[2].Value.Trim(),
                    AltNamedCard = m.Groups[3].Value.Trim(),
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Blast Sphere (c26302522): face-down Defense attacked; equip to attacker; next opp Standby Phase destroy + inflict ATK damage.
            AtomRegistration.Atom.Sequence(
                @"^\s*If this face-down Defense Position card was attacked by an opponent's monster, before damage calculation:\s*" +
                @"Equip this card to the attacking monster\.\s*" +
                @"Then, during your opponent's next Standby Phase:\s*" +
                @"Destroy the monster this card is equipped to, and if you do, inflict damage to your opponent equal to the ATK the destroyed monster had on the field" + SentenceEnd,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.DamageCalculation,
                        Action = EffectActionKind.EquipThisToAttackingMonster,
                        Zone = EffectZoneFilter.AttackingMonster,
                        RequiresThisIsAttackTarget = true,
                        RequiresTargetChoice = false,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.BlastSphereEquipThenStandbyDestroyInflict,
                        Side = EffectSide.Opponent,
                        StaysOnField = true,
                        MakesChainLink = true
                    }
                }),

            // Vampire Lord (c53839837): battle damage → declare type → opponent mills 1 of that type from Deck.
            new AtomRegistration.Atom(
                SentenceStart + @"If this card inflicts battle damage to your opponent:\s*" +
                @"Declare 1 type of card \(Monster, Spell, or Trap\);\s*" +
                @"your opponent sends 1 card of that type from their Deck to the GY" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    MakesChainLink = true
                }),

            // Vampire Lord (c53839837) continued: once per turn, during your next Standby Phase after being destroyed by opponent's effect and sent to GY: SS from GY.
            new AtomRegistration.Atom(
                SentenceStart + @"Once per turn, during your next Standby Phase after this card in your possession was destroyed and sent to your GY by an opponent's card effect:\s*" +
                @"Special Summon this card from the GY" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GyRevivalAfterStandbyWhenDestroyedByOpponent,
                    Side = EffectSide.Controller,
                    RequiresThisDestroyedByOpponent = true,
                    // OncePerTurnSoft group (v211): a GY trigger (Lua SetRange(LOCATION_GRAVE)); without this flag
                    // FirePhaseTriggersFor skipped the clause for a card in the GY, so the revival never happened.
                    ResolvesFromGy = true,
                    MakesChainLink = true
                }),
        };
    }
}
