using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TributeSummons batch (v141 curriculum MRD/SRL/PSV): whole-sentence atoms for Tribute-then-Summon Spells
    /// and their summon locks — Tribute Doll, Insect Imitation, Magical Labyrinth (which makes Wall Shadow's
    /// "Must first be Special Summoned with" lock a live summon path) and Soul Exchange. Every pattern is
    /// anchored to a sentence start and a sentence end, so a longer card that merely contains one of these
    /// phrases never compiles part of its text here. Runtime: <see cref="TributeSummonsRuntime"/>.
    /// </summary>
    public static class TributeSummonsAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Tribute Doll (c2903036): cost Tribute 1 monster (Duel.SelectReleaseGroupCost); Special Summon
            // 1 Level 7 monster that IsSummonableCard from the hand at resolution; EFFECT_CANNOT_ATTACK on it
            // (RESETS_STANDARD_PHASE_END). The pick is not a target.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 monster;\s*Special Summon 1 Level (\d+) monster from your hand that can be " +
                @"Normal Summoned/Set\.\s*It cannot attack this turn" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromHand,
                    Zone = EffectZoneFilter.ControllerHandMonsters,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    RequiresTributeCount = 1,
                    RequiresExactLevel = Parse(m, 1, 7),
                    RequiresCanBeNormalSummonedOrSet = true,
                    SummonCannotAttackThisTurn = true,
                    MakesChainLink = true
                }),
            // Insect Imitation (c96965364): cost Tribute 1 monster with a Level whose Level+1 Insect is in the
            // Deck (cfilter); the Tributed monster's Level is recorded at cost time (e:SetLabel); Special Summon
            // 1 Insect of exactly that Level + 1 from the Deck. Not a target.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 monster\.\s*Special Summon 1 (\w+)(?:-Type)? monster from your Deck whose " +
                @"Level is (\d+) higher than the Tributed monster's" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromDeck,
                    Zone = EffectZoneFilter.ControllerDeckMonsters,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value,
                    RequiresTargetChoice = true,
                    RequiresTributeCount = 1,
                    MatchTributedLevel = true,
                    TributedLevelOffset = Parse(m, 2, 1),
                    MakesChainLink = true
                }),
            // Magical Labyrinth (c64389297), sentence 1: aux.AddEquipProcedure to a face-up "Labyrinth Wall".
            // Only when the card's next sentence is the Tribute-the-equipped-monster ignition below, so this
            // never compiles half of another named Equip Card.
            new AtomRegistration.Atom(
                Start + @"Equip only to ""([^""]+)""\.(?=\s*You can Tribute the equipped monster;)",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    EquipHostName = m.Groups[1].Value,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Magical Labyrinth, sentence 2: ignition (SZONE) while you control the equipped monster; cost
            // Tribute it (this card goes with it); Special Summon the named monster from the Deck — its own
            // summoning procedure (SpecialSummon(...,true,true) + CompleteProcedure), so Wall Shadow's
            // "Must first be Special Summoned with" lock is satisfied.
            new AtomRegistration.Atom(
                Start + @"You can Tribute the equipped monster;\s*Special Summon ""([^""]+)"" from your Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    FromDeck = true,
                    RequiresTributeEquippedMonster = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Soul Exchange (c68005187): oath EFFECT_CANNOT_BP at activation (not in Main Phase 2); target 1
            // monster the opponent controls (face-up or face-down); EFFECT_EXTRA_RELEASE on it until the end
            // of the turn — a Tribute the controller may use as if they controlled it and must use first.
            new AtomRegistration.Atom(
                Start + @"Target 1 monster your opponent controls;\s*this turn, if you Tribute a monster, you must " +
                @"Tribute that target, as if you controlled it\.\s*You cannot conduct your Battle Phase the turn " +
                @"you activate this card" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TributeTargetAsIfControlledThisTurn,
                    Zone = EffectZoneFilter.OppMonsters,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    ForbidsBattlePhaseThisTurn = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
