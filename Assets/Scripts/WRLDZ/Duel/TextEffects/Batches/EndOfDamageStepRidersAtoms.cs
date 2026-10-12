using System.Linq;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "EndOfDamageStepRiders": battle riders printed in IOC / AST wordings — end-of-Damage-Step triggers on the
    /// monster this card battled, a Type-wide piercing grant, an attack shield, a continuous Attack Position rule and a
    /// conditional direct attack. Every atom is a whole sentence, anchored at both ends, so a longer or reworded card stays
    /// uncompiled. Runtime: <see cref="EndOfDamageStepRidersRuntime"/>.
    /// <list type="bullet">
    /// <item>Getsu Fuhma (c21887179), Ryu Kokki (c57281778): SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END — the battled monster of
    /// the printed Types, still related to the battle, is destroyed
    /// (<see cref="EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep"/>).</item>
    /// <item>Prickle Fairy (c91559748): the opponent cannot attack your face-up Insects
    /// (<see cref="EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters"/>); end of the Damage Step: the battled
    /// monster in Attack Position → face-up Defense (<see cref="EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep"/>).</item>
    /// <item>Insect Princess (c37957847): the opponent's Insects are in face-up Attack Position
    /// (<see cref="EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack"/>); destroying an Insect by battle gives
    /// it +500 ATK (Zombyra's <see cref="EffectActionKind.ChangeThisCardAtkWhileFaceUp"/> with
    /// <see cref="EffectClause.BattledMonsterRaceFilter"/>).</item>
    /// <item>Enraged Battle Ox (c76909279): Dragon's Rage's <see cref="EffectActionKind.GrantPiercingToYourRaceMonsters"/>
    /// from a monster, three Types.</item>
    /// <item>Drillago (c99050989): <see cref="EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast"/>.</item>
    /// <item>Two Thousand Needles (c83228073): Thousand Needles' clause
    /// (<see cref="EffectActionKind.DestroyAttackerAtEndOfDamageStep"/>), AST wording.</item>
    /// <item>Legendary Jujitsu Master (c25773409): <see cref="EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep"/>.</item>
    /// </list>
    /// </summary>
    public static class EndOfDamageStepRidersAtoms
    {
        const string S = @"(?<=^|\.\s)";
        const string E = @"(?:\.|$)(?=\s|$)";

        /// <summary>One printed Monster Type ("Fiend", "Beast-Warrior", "Winged Beast").</summary>
        const string OneType = @"[A-Z][a-z]+(?:[- ][A-Z][a-z]+)?";

        /// <summary>A printed Type list: "Fiend or Zombie", "Beast, Beast-Warrior, or Winged Beast".</summary>
        const string Types = @"(" + OneType + @"(?:(?:,? or |, )" + OneType + @")*)";

        /// <summary>"Fiend or Zombie" → "Fiend|Zombie" (ClassicEraTemplates.RaceMatches).</summary>
        static string TypeList(string printed) =>
            string.Join("|", Regex.Split(printed, @",? or |, ").Select(t => t.Trim()).Where(t => t.Length > 0));

        static EffectClause DestroyBattled(Match m) => new()
        {
            Timing = EffectTiming.EndOfDamageStep,
            Action = EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep,
            Side = EffectSide.Opponent,
            Zone = EffectZoneFilter.OpponentBattlingMonster,
            BattledMonsterRaceFilter = TypeList(m.Groups[1].Value),
            MakesChainLink = true
        };

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Getsu Fuhma (c21887179): SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END; aux.dsercon (this card related to the battle
            // or destroyed by it), GetBattleTarget():IsRace(FIEND+ZOMBIE) and IsRelateToBattle → Duel.Destroy it.
            new AtomRegistration.Atom(
                S + @"If this card battles an? " + Types + @"-Type monster, destroy that monster at the end of the " +
                @"Damage Step" + E,
                DestroyBattled),
            // Ryu Kokki (c57281778): the same trigger, RACE_WARRIOR+RACE_SPELLCASTER (either battler: attacker or target).
            new AtomRegistration.Atom(
                S + @"At the end of the Damage Step, if this card battled an? " + Types + @"-Type monster: Destroy that " +
                @"monster" + E,
                DestroyBattled),
            // Prickle Fairy (c91559748) e2: FIELD EFFECT_CANNOT_SELECT_BATTLE_TARGET, target range (0, MZONE) of the
            // opponent's attackers: c:IsFaceup() and c:IsRace(RACE_INSECT) on its controller's side.
            new AtomRegistration.Atom(
                S + @"Your opponent cannot attack (" + OneType + @") monsters" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Prickle Fairy (c91559748) e1: SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END; the battle target IsRelateToBattle;
            // operation: still related and IsAttackPos → Duel.ChangePosition(POS_FACEUP_DEFENSE).
            new AtomRegistration.Atom(
                S + @"If this card battled a monster, at the end of the Damage Step: Change that monster to Defense " +
                @"Position" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OpponentBattlingMonster,
                    MakesChainLink = true
                }),
            // Insect Princess (c37957847) e1: FIELD EFFECT_SET_POSITION POS_FACEUP_ATTACK, range MZONE, target range
            // (0, MZONE), target c:IsRace(RACE_INSECT) (no face-up filter).
            new AtomRegistration.Atom(
                S + @"All (" + OneType + @") monsters your opponent controls are changed to Attack Position" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack,
                    Side = EffectSide.Opponent,
                    RaceFilter = m.Groups[1].Value,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Insect Princess (c37957847) e2/e3: EVENT_BATTLED flags a battle with an Insect; SINGLE TRIGGER_F
            // EVENT_BATTLE_DESTROYING → EFFECT_UPDATE_ATTACK +500, RESET_EVENT|RESETS_STANDARD_DISABLE.
            new AtomRegistration.Atom(
                S + @"If this card destroys an? (" + OneType + @") monster by battle: This card gains (\d+) ATK" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[2].Value),
                    BattledMonsterRaceFilter = m.Groups[1].Value,
                    MakesChainLink = true
                }),
            // Enraged Battle Ox (c76909279): FIELD EFFECT_PIERCE, range MZONE, target range (MZONE, 0),
            // c:IsRace(BEASTWARRIOR+WINDBEAST+BEAST) — the live Type of its controller's attacker.
            new AtomRegistration.Atom(
                S + @"If an? " + Types + @"-Type monster you control attacks a Defense Position monster, inflict piercing " +
                @"battle damage to your opponent" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GrantPiercingToYourRaceMonsters,
                    RaceFilter = TypeList(m.Groups[1].Value),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Drillago (c99050989): SINGLE EFFECT_DIRECT_ATTACK; condition: the opponent's LOCATION_SZONE is empty and none
            // of their monsters is face-down or has ATK lower than 1600.
            new AtomRegistration.Atom(
                S + @"If the only cards your opponent controls are face-up monsters with (\d+) or more ATK, this card can " +
                @"attack your opponent directly" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast,
                    Amount = int.Parse(m.Groups[1].Value),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Two Thousand Needles (c83228073): Thousand Needles' trigger (SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END; this card
            // the attack target in Defense Position, the attacker's ATK lower than its DEF; destroy the attacker if it
            // still relates to the battle).
            new AtomRegistration.Atom(
                S + @"When this Defense Position card is attacked and the ATK of the attacking monster is lower than the " +
                @"DEF of this card, destroy the attacking monster at the end of the Damage Step" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    MakesChainLink = true
                }),
            // Legendary Jujitsu Master (c25773409): SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END; aux.dsercon, GetAttackTarget()
            // is this card and its battle position is Defense; the attacker, still related to the battle →
            // Duel.SendtoDeck(SEQ_DECKTOP) (its owner's Deck).
            new AtomRegistration.Atom(
                S + @"If this Defense Position card battled a monster, at the end of the Damage Step: Place that monster " +
                @"on (?:the )?top of the Deck" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    MakesChainLink = true
                }),
        };
    }
}
