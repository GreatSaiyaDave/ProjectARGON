using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// One record per new sentence: the regex and the clause it builds.
    /// Collect and ExpectedActions both walk this list, so the action on the
    /// clause is the action the coverage check requires.
    /// Existing templates stay in their own files. New batches add atoms here.
    /// </summary>
    public static class AtomRegistration
    {
        public readonly struct Atom
        {
            public readonly Regex Pattern;
            public readonly Func<Match, EffectClause> Build;

            /// <summary>
            /// Set instead of <see cref="Build"/> by <see cref="Sequence"/>: one sentence that resolves as
            /// several clauses, in this order (Thunder Crash: destroy, then burn per destroyed).
            /// </summary>
            public readonly Func<Match, EffectClause[]> BuildAll;

            public Atom(string pattern, Func<Match, EffectClause> build)
                : this(pattern, build, null)
            {
            }

            Atom(string pattern, Func<Match, EffectClause> build, Func<Match, EffectClause[]> buildAll)
            {
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                Build = build;
                BuildAll = buildAll;
            }

            /// <summary>
            /// One whole sentence compiled to several clauses of the same timing, applied in array order
            /// (the ClassicEra one-sentence-two-clauses shape, e.g. Dragon Piper). Every clause gets the
            /// sentence as its SourceSnippet.
            /// </summary>
            public static Atom Sequence(string pattern, Func<Match, EffectClause[]> buildAll) =>
                new Atom(pattern, null, buildAll);

            /// <summary>The clauses this atom builds for a match (null entries skipped by callers).</summary>
            public EffectClause[] BuildClauses(Match m)
            {
                if (BuildAll != null) return BuildAll(m);
                return Build != null ? new[] { Build(m) } : null;
            }

            public bool CanBuild => Build != null || BuildAll != null;
        }

        /// <summary>Whole-sentence atoms. A partial match of a longer card must not be added here.</summary>
        public static readonly Atom[] Atoms =
        {
            // Insect Soldiers: only while this card is attacking that Attribute.
            new Atom(
                @"If this card attacks a (\w+) monster, it gains (\d+) ATK during the Damage Step only\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DamageStepAttributeAtk,
                    AttributeFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 1000),
                    DamageStepOnlyWhenAttacking = true,
                    MakesChainLink = false
                }),
            // Penumbral Soldier Lady: either side of the battle.
            new Atom(
                @"If this card battles a (\w+) monster, this card gains (\d+) ATK during the Damage Step only\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DamageStepAttributeAtk,
                    AttributeFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 500),
                    DamageStepOnlyWhenAttacking = false,
                    MakesChainLink = false
                }),
            // Luminous Soldier: same window, older wording.
            new Atom(
                @"If this card battles a (\w+) monster, increase the ATK of this card by (\d+) points during the Damage Step only\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DamageStepAttributeAtk,
                    AttributeFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 500),
                    DamageStepOnlyWhenAttacking = false,
                    MakesChainLink = false
                }),
            // Mirage Dragon / Pitch-Black Warwolf.
            new Atom(
                @"Your opponent cannot activate Trap Cards during the Battle Phase\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentCannotActivateTrapsDuringBattle,
                    MakesChainLink = false
                }),
            // Invasion of Flames and the same Normal Summon line.
            new Atom(
                @"When this card is Normal Summoned successfully, no Trap Cards can be activated\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SealTrapsAnsweringThisSummon,
                    RequiresThisNormalSummoned = true,
                    MakesChainLink = false
                }),
            // Earthquake. Face-down monsters are left alone.
            new Atom(
                @"Change all face-up monsters on the field to Defense Position\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    ForceDefensePosition = true,
                    MakesChainLink = true
                }),
            // Desert Sunlight. Your monsters, including face-down, become face-up Defense.
            new Atom(
                @"All monsters on your side of the field are changed to face-up Defense Position\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Controller,
                    ForceDefensePosition = true,
                    AlsoFlipFaceDown = true,
                    MakesChainLink = true
                }),
            // Really Eternal Rest.
            new Atom(
                @"Destroy all monsters equipped with an Equip Card\(s\)\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyAllEquippedMonsters,
                    MakesChainLink = true
                }),
            // Lightning Vortex. Discard is any card; destroy is face-up opponent monsters only.
            new Atom(
                @"Discard 1 card; destroy all face-up monsters your opponent controls\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    MakesChainLink = true
                }),

            // ── v101: tribute costs, two-for-one tributes, mass bounce / position, DEF tweaks, summon draws ──

            // Hysteric Fairy. Legacy ignition wording; the Tribute is the cost.
            new Atom(
                @"Tribute 2 monsters on your side of the field to increase your Life Points by (\d+) points\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainLifePoints,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 1000),
                    RequiresTributeCount = 2,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Anti-Aircraft Flower. Typed Tribute (Insect) as the cost.
            new Atom(
                @"By Tributing 1 (\w+)-Type monster on your side of the field, inflict (\d+) points of damage to your opponent's Life Points\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Parse(m, 2, 800),
                    RequiresTributeCount = 1,
                    TributeRaceFilter = m.Groups[1].Value,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Man-Thro' Tro'. Normal Monster Tribute; Tokens are printed out.
            new Atom(
                @"By Tributing 1 Normal Monster \(except a Token\) on your side of the field, inflict (\d+) points of damage to your opponent's Life Points\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Parse(m, 1, 800),
                    RequiresTributeCount = 1,
                    TributeNormalMonsterOnly = true,
                    TributeExceptTokens = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Throwstone Unit. Typed Tribute, then a face-up target measured against this card's ATK.
            new Atom(
                @"Tribute 1 (\w+)-Type monster on your side of the field to destroy 1 face-up monster on the field whose DEF is equal to or less than the ATK of this card\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    RequiresTributeCount = 1,
                    TributeRaceFilter = m.Groups[1].Value,
                    RequiresTargetDefLeqSourceAtk = true,
                    // TargetingTruth batch (v177): Lua c76075810 desop selects at resolution (SelectMatchingCard +
                    // HintSelection) — the destroyed monster is not targeted.
                    ChoiceDoesNotTarget = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Double Coston / Flame Ruler. Consulted only by the Tribute Summon procedure.
            new Atom(
                @"This card can be treated as 2 Tributes for the Tribute Summon of an? (\w+) monster\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CountsAsTwoTributesForAttribute,
                    AttributeFilter = m.Groups[1].Value,
                    MakesChainLink = false
                }),
            // Unshaven Angler / Whirlwind Prodigy / The Trojan Horse. Same rule, newer wording.
            new Atom(
                @"When Tribute Summoning an? (\w+) monster, you can treat this 1 monster as 2 Tributes\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CountsAsTwoTributesForAttribute,
                    AttributeFilter = m.Groups[1].Value,
                    MakesChainLink = false
                }),
            // Giant Trunade. Both players' Spell & Trap Zones, face-up or Set.
            new Atom(
                @"Return all Spell and Trap Cards on the field to the hand\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnToHand,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // Time Seal.
            new Atom(
                @"Skip the Draw Phase of your opponent's next turn\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SkipOpponentNextDrawPhase,
                    MakesChainLink = true
                }),
            // The Reliable Guardian. Castle Walls shape, apostrophe wording.
            new Atom(
                @"Increase 1 face-up monster's DEF by (\d+) points until the end of this turn\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainAtkDefUntilEndOfTurn,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    Amount = 0,
                    DefAmount = Parse(m, 1, 700),
                    MakesChainLink = true
                }),
            // Snake Fang. DEF only; ATK untouched.
            new Atom(
                @"Decrease 1 selected monster's DEF by (\d+) points during the turn this card is activated\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    Amount = 0,
                    DefAmount = Parse(m, 1, 500),
                    MakesChainLink = true
                }),
            // Zero Gravity. Toggle every face-up monster; face-down stay as they are.
            new Atom(
                @"Change the battle positions of all face-up monsters on the field\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // Sacred Crane.
            new Atom(
                @"If this card is Special Summoned: Draw (\d+) cards?\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 1),
                    RequiresThisSpecialSummoned = true,
                    MakesChainLink = true
                }),
            // Molten Zombie. Only a Special Summon out of the Graveyard.
            new Atom(
                @"When this card is Special Summoned from the Graveyard: Draw (\d+) cards?\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 1),
                    RequiresThisSpecialSummoned = true,
                    RequiresThisSpecialSummonedFromGy = true,
                    MakesChainLink = true
                }),
            // Vengeful Bog Spirit. Either player's monsters, the turn they are Summoned (Flip included).
            new Atom(
                @"Monsters cannot attack the turn they are Summoned\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Side = EffectSide.Both,
                    RequiresSummonedOrFlippedThisTurn = true,
                    MakesChainLink = false
                }),
            // Big-Tusked Mammoth. Opponent's monsters only; carried by a monster, not a Spell/Trap.
            new Atom(
                @"Monsters your opponent controls cannot attack the turn they are Summoned\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Side = EffectSide.Opponent,
                    RequiresSummonedOrFlippedThisTurn = true,
                    MakesChainLink = false
                }),

            // ── v102: Tribute Summon riders, hand shuffles, DEF/ATK tweaks, deck peeks, draw / LP riders, damage locks ──

            // Zolga. Fires from the Tribute Summon procedure, face-up or Set.
            new Atom(
                @"When a monster is Tribute Summoned by Tributing this card: The owner of this card gains (\d+) Life Points\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainLpWhenTributedForSummon,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2000),
                    MakesChainLink = true
                }),
            // Precious Cards from Beyond.
            new Atom(
                @"When you Tribute Summon a monster using 2 or more Tributes: Draw (\d+) cards?\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DrawWhenTributeSummonedWithTwoPlus,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2),
                    MakesChainLink = true
                }),
            // Ultra Evolution Pill. Typed Tribute, then a typed hand pick.
            new Atom(
                @"Tribute 1 (\w+) monster; Special Summon 1 (\w+) monster from your hand\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromHand,
                    Zone = EffectZoneFilter.ControllerHandMonsters,
                    RequiresTargetChoice = true,
                    RaceFilter = m.Groups[2].Value,
                    RequiresTributeCount = 1,
                    TributeRaceFilter = m.Groups[1].Value,
                    MakesChainLink = true
                }),
            // Exhausting Spell.
            new Atom(
                @"Remove all Spell Counters on both sides of the field\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RemoveAllSpellCounters,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // Disturbance Strategy. Same number back.
            new Atom(
                @"Your opponent shuffles their entire hand into the Deck, then draws the same number of cards\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentShufflesHandThenDraws,
                    Side = EffectSide.Opponent,
                    Amount = 0,
                    MakesChainLink = true
                }),
            // Heavy Slump. Activation needs 8+ in the opponent's hand; they draw a fixed 2.
            new Atom(
                @"If your opponent has (\d+) or more cards in their hand: They shuffle their entire hand into the Deck, then draw (\d+) cards?\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentShufflesHandThenDraws,
                    Side = EffectSide.Opponent,
                    RequiresOpponentHandAtLeast = Parse(m, 1, 8),
                    Amount = Parse(m, 2, 2),
                    MakesChainLink = true
                }),
            // Micro Ray. DEF to 0, ATK untouched.
            new Atom(
                @"Target 1 face-up monster on the field; that target's DEF becomes 0 until the end of this turn\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    Amount = 0,
                    SetDefToZero = true,
                    MakesChainLink = true
                }),
            // Energy Drain. ×opponent's hand at resolution.
            new Atom(
                @"Target 1 face-up monster you control; it gains (\d+) ATK/DEF for each card your opponent currently has in their hand, until the end of this turn\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainAtkDefUntilEndOfTurn,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    Amount = Parse(m, 1, 200),
                    DefAmount = Parse(m, 1, 200),
                    ScaleAmountByOpponentHandCards = true,
                    MakesChainLink = true
                }),
            // Fruits of Kozaky's Studies / Spellbook Organization. Same peek, two wordings.
            new Atom(
                @"Look at the top (\d+) cards of your Deck(?:,? then| and) return (?:those cards|them) to the top of the Deck in any order\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReorderTopOfOwnDeck,
                    Amount = Parse(m, 1, 3),
                    MakesChainLink = true
                }),
            // Gravekeeper's Servant. Checked by the attack-declaration cost path.
            new Atom(
                @"Your opponent must send 1 card from the top of their Deck to the Graveyard to declare an attack\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.MillToDeclareAttack,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    MakesChainLink = false
                }),
            // Banner of Courage. Your monsters, your Battle Phase only.
            new Atom(
                @"All monsters you control gain (\d+) ATK during your Battle Phase only\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 200),
                    RequiresBattlePhase = true,
                    ControllerTurnOnly = true,
                    MakesChainLink = false
                }),
            // Solomon's Lawbook.
            new Atom(
                @"Skip your next Standby Phase\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SkipControllerNextStandbyPhase,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // Fire Princess.
            new Atom(
                @"Each time you gain Life Points, inflict (\d+) damage to your opponent\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageWhenControllerGainsLp,
                    Amount = Parse(m, 1, 500),
                    MakesChainLink = true
                }),
            // Solemn Wishes.
            new Atom(
                @"Increase your Life Points by (\d+) points each time you draw a card \(or cards\)\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainLpWhenControllerDraws,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 500),
                    MakesChainLink = true
                }),
            // Dark Jeroid. Any Summon; the loss stays while the target is face-up.
            new Atom(
                @"If this card is Summoned: Target 1 face-up monster on the field; it loses (\d+) ATK\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.ApplyLingeringAtkDef,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    Amount = -Parse(m, 1, 800),
                    MakesChainLink = true
                }),
            // Mobius the Frost Monarch. Up to 2 Spell/Traps.
            new Atom(
                @"When this card is Tribute Summoned: You can target up to (\d+) Spell/Trap Cards on the field; destroy those targets\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresThisTributeSummoned = true,
                    Amount = Parse(m, 1, 2),
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Spirit Barrier. Soft v137: ContinuousWhileFaceUp PreventControllerBattleDamage
            // RequiresControllerHasMonster Side=Controller (printed "you"; battle only).
            new Atom(
                @"While you control a monster, you take no battle damage\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.PreventControllerBattleDamage,
                    RequiresControllerHasMonster = true,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Amazoness Fighter. Monster-borne; only its own battles.
            new Atom(
                @"You take no battle damage from attacks involving this card\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.PreventControllerBattleDamage,
                    OnlyBattlesInvolvingThis = true,
                    MakesChainLink = false
                }),

            // —— v140: cards compiled on the unmerged Sept-10 cloud branches (GitHub PRs #28, #31, #32,
            // #33), re-implemented on this line. ——

            // Windstorm of Etaqua (PR #32): Zero Gravity for the opponent's side only, no target.
            new Atom(
                @"Change the battle positions of all face-up monsters your opponent controls\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Level Limit - Area B (PR #28): Dragon Capture Jar's continuous force-to-Defense, filtered by
            // "Level N or higher" (Gravity Bind's AmountIsLevel reading) instead of a Type.
            new Atom(
                @"Change all face-up Level (\d+) or higher monsters to Defense Position\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ChangeBattlePosition,
                    ForceDefensePosition = true,
                    Amount = Parse(m, 1, 4),
                    AmountIsLevel = true,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Spiritualism (PR #33): bounce 1 opponent Spell/Trap; activation and effect cannot be negated.
            new Atom(
                @"Return 1 Spell/?Trap(?: Card)? your opponent controls to the hand\.\s*" +
                @"This card's activation and effect cannot be negated\.?",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnToHand,
                    Zone = EffectZoneFilter.OppSpellTraps,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    ActivationNegatable = false,
                    EffectNegatable = false,
                    MakesChainLink = true
                }),
            // Graceful Dice / Skull Dice (PR #31): one die for all the side's monsters, until end of turn.
            new Atom(
                @"Roll a six-sided die\.\s*All monsters (you|your opponent) currently controls? (gain|lose) " +
                @"ATK(?:/DEF| and DEF) equal to the result x (\d+),? until the end of this turn\.?",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ModifyAllAtkDefByDieUntilEndOfTurn,
                    Side = m.Groups[1].Value.Equals("you", StringComparison.OrdinalIgnoreCase)
                        ? EffectSide.Controller
                        : EffectSide.Opponent,
                    Amount = (m.Groups[2].Value.Equals("lose", StringComparison.OrdinalIgnoreCase) ? -1 : 1) *
                             Parse(m, 3, 100),
                    MakesChainLink = true
                }),
        };

        /// <summary>
        /// One entry per card batch (Tools/CardCompile, .claude/workflows/compile-card-batch.js): each batch
        /// keeps its atoms in its own file, TextEffects/Batches/&lt;Batch&gt;Atoms.cs, and adds exactly one line
        /// here, so parallel batches never edit the same lines. Order is the matching order: the base
        /// <see cref="Atoms"/> first, then each batch in this list.
        /// </summary>
        static readonly Atom[][] BatchAtomSets =
        {
            // Batch atom lists — one line per batch, append below.
            PerCountBurnsAtoms.Atoms,
            SummonNegationAtoms.Atoms,
            ContinuousNegationAtoms.Atoms,
            TurnNegationTrapsAtoms.Atoms,
            TakeControlAtoms.Atoms,
            TributeSummonsAtoms.Atoms,
            EmptyHandStandbyDrawAtoms.Atoms,
            DiscardTriggersAtoms.Atoms,
            DiscardTriggersAtoms.SelfSsFromGy,
            BattlePhaseEndAtoms.Atoms,
            SpellTrapDestroyedResponseAtoms.Atoms,
            TypeDeclarationAtoms.Atoms,
            LocksAndZonesAtoms.Atoms,
            HandDisruptionAtoms.Atoms,
            BanishedReturnAtoms.Atoms,
            FlipMassEffectsAtoms.Atoms,
            RedirectAndReplaceAtoms.Atoms,
            StandbyGrowthAndMaintenanceAtoms.Atoms,
            SpiritsAndMiscAtoms.Atoms,
            PartialCompilesAtoms.Atoms,
            DrawThenDiscardAtoms.Atoms,
            SpiritReturnPsctAtoms.Atoms,
            FlipDefCheckAtoms.Atoms,
            ToonsAtoms.Atoms,
            TargetedClassicsAtoms.Atoms,
            PositionLocksAtoms.Atoms,
            EquipRidersAtoms.Atoms,
            BattleDestroyedRecoveryAtoms.Atoms,
            SelfStatMonstersAtoms.Atoms,
            StandbyPhaseEffectsAtoms.Atoms,
            PoisonMeteorCestusAtoms.Atoms,
            RockSentenceAtoms.Atoms,
            BattleWindowsAtoms.Atoms,
            EquipFamilyAtoms.Atoms,
            FlipCostsAndFusionAtoms.Atoms,
            RuleLocksAtoms.Atoms,
            DestinyBoardAtoms.Atoms,
            NegationsAndGySummonsAtoms.Atoms,
            EquipModelAtoms.Atoms,
            FaceUpQuickEffectsAtoms.Atoms,
            SimultaneousTriggersAtoms.Atoms,
            EngineHygieneAtoms.Atoms,
            SimpleSpellTrapsAtoms.Atoms,
            SpiritMonstersAtoms.Atoms,
            BothSideAndMonsterEquipAtoms.Atoms,
            CoinDiceInterruptsAtoms.Atoms,
            SpecialSummonEffectsAtoms.Atoms,
            ContinuousTrapsAndSpiritSupportAtoms.Atoms,
            BattleBoostsAtoms.Atoms,
            EquipAndSpellProtectionAtoms.Atoms,
            DeckReturnsAndMassEffectsAtoms.Atoms,
            BattleRulesAtoms.Atoms,
            NegationMonstersAtoms.Atoms,
            EffectDamageEventsAtoms.Atoms,
            PgdSpellTrapBasicsAtoms.Atoms,
            BattleOutcomeTriggersAtoms.Atoms,
            FlipDownAndPeekAtoms.Atoms,
            StandbyBurnsAndDiceAtoms.Atoms,
            BattleDestroyedGyRecoveryAtoms.Atoms,
            ContinuousLocksAndIgnitionsAtoms.Atoms,
            PhaseTimersAndDeckTopAtoms.Atoms,
            CallsDiceAndSetTriggersAtoms.Atoms,
            TargetNegationsAndHandQuickEffectsAtoms.Atoms,
            SummonTriggersAndTributesAtoms.Atoms,
            LastTurnAtoms.Atoms,
            SummonPromptsAndCostsAtoms.Atoms,
            ForcedActivationAndSubstitutionAtoms.Atoms,
            PerCopyDeckAtoms.Atoms,
            MfcSimpleSpellsAtoms.Atoms,
            MfcLabCardsAtoms.Atoms,
            MfcLabCards2Atoms.Atoms,
            MfcLabCards3Atoms.Atoms,
            MfcSimpleSpellsBAtoms.Atoms,
            SimpleTargetUtilityAtoms.Atoms,
            DamageStepRulesAtoms.Atoms,
            EquipLocksAndLeaveFieldAtoms.Atoms,
            DcrEquipSpellsAtoms.Atoms,
            SpellCounterSpendersAtoms.Atoms,
            ChainLinkAnswersAtoms.Atoms,
            FlipMassAndOpponentPicksAtoms.Atoms,
            PhaseTriggersAndIgnitionCostsAtoms.Atoms,
            UnionRidersAtoms.Atoms,
            SummonLocksAndStatRulesAtoms.Atoms,
            SimpleTriggersAndSwarmAtoms.Atoms,
            DcrSpellTrapUtilityAtoms.Atoms,
            BattleDestroyedTriggersAtoms.Atoms,
            GuardiansAndEquipEventsAtoms.Atoms,
            DerivedAtkMonstersAtoms.Atoms,
            NamedSummonPairsAtoms.Atoms,
            ArchfiendRidersAtoms.Atoms,
            SpellTrapAnswersAndActivationLocksAtoms.Atoms,
            MonsterNegationAndControlLocksAtoms.Atoms,
            ConditionalSpellsAndTrapsAtoms.Atoms,
            EndOfDamageStepRidersAtoms.Atoms,
            BattleDamageEventsAtoms.Atoms,
            SpellEventsAndHandSendTriggersAtoms.Atoms,
            RecoveryAndPhaseCostsAtoms.Atoms,
            TokensDeclarationsAndPicksAtoms.Atoms,
            ChaosBanishSummonsAtoms.Atoms,
            TargetingTruthAtoms.Atoms,
            BattleAndPositionRecordsAtoms.Atoms,
        };

        /// <summary>Every whole-sentence atom: <see cref="Atoms"/>, then each batch in <see cref="BatchAtomSets"/>.</summary>
        public static IEnumerable<Atom> AllAtoms()
        {
            foreach (var atom in Atoms)
                yield return atom;
            foreach (var set in BatchAtomSets)
            {
                if (set == null) continue;
                foreach (var atom in set)
                    yield return atom;
            }
        }

        public static void Collect(string text, List<EffectClause> into, List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;
            foreach (var atom in AllAtoms())
            {
                var m = atom.Pattern.Match(text);
                if (!m.Success || !atom.CanBuild) continue;
                if (SpanCovered(spans, m.Index, m.Length)) continue;
                var built = atom.BuildClauses(m);
                if (built == null) continue;
                var added = false;
                foreach (var clause in built)
                {
                    if (clause == null) continue;
                    clause.SourceSnippet = m.Value.Trim();
                    into.Add(clause);
                    added = true;
                }

                if (added)
                    spans?.Add((m.Index, m.Length));
            }
        }

        public static void ExpectedActions(string text, List<EffectActionKind> need)
        {
            if (string.IsNullOrEmpty(text) || need == null) return;
            foreach (var atom in AllAtoms())
            {
                var m = atom.Pattern.Match(text);
                if (!m.Success || !atom.CanBuild) continue;
                var built = atom.BuildClauses(m);
                if (built == null) continue;
                foreach (var clause in built)
                {
                    if (clause == null || need.Contains(clause.Action)) continue;
                    need.Add(clause.Action);
                }
            }
        }

        public static bool Matches(string text, out Atom atom)
        {
            atom = default;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var candidate in AllAtoms())
            {
                if (!candidate.Pattern.IsMatch(text)) continue;
                atom = candidate;
                return true;
            }

            return false;
        }

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }

        public static bool SpanCovered(List<(int start, int length)> spans, int start, int length)
        {
            if (spans == null || length <= 0) return false;
            var end = start + length;
            var covered = 0;
            foreach (var (s, n) in spans)
            {
                var a = start > s ? start : s;
                var b = end < s + n ? end : s + n;
                if (b > a) covered += b - a;
            }

            return covered * 2 >= length;
        }
    }
}
