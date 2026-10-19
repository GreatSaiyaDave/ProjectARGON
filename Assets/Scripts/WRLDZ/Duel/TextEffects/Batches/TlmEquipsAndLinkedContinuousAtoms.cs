using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TlmEquipsAndLinkedContinuous batch — Equip Spells and linked Continuous Spells/Traps with battle riders (TLM). Each
    /// atom matches a card's WHOLE printed text (anchored at both ends), so a longer or different card never compiles part
    /// of it here. The quoted name lists ("A", "B", or "C") are one <see cref="EffectClause.AnyOfNamedCards"/> value
    /// (<see cref="TlmEquipsAndLinkedContinuousRuntime.NamedInList"/>; Lua IsCode with several codes).
    /// <list type="bullet">
    /// <item>Legendary Black Belt (c96458440): aux.AddEquipProcedure(c, nil, IsCode(list)) — a face-up listed monster on
    /// either field, and the same equip limit; FIELD TRIGGER_F EVENT_BATTLE_DESTROYING: damage equal to the destroyed
    /// monster's DEF in the GY.</item>
    /// <item>Nitro Unit (c23842445): aux.AddEquipProcedure(c, 1) — a face-up monster the opponent controls, no other equip
    /// limit; SINGLE TRIGGER_F EVENT_LEAVE_FIELD (REASON_LOST_TARGET): damage equal to its former monster's ATK in the GY
    /// when that monster was destroyed by battle.</item>
    /// <item>Lone Wolf (c82452993): you control exactly 1 monster and it is a face-up listed monster; it is the target
    /// (Duel.SetTargetCard, no pick); linked, it cannot be destroyed by battle and is unaffected by the opponent's monster
    /// effects. This card stays when the monster leaves.</item>
    /// <item>Impenetrable Formation (c96631852): target 1 face-up monster you control and link it; +N DEF while its
    /// controller controls 2+ monsters and none is in Attack Position; destroyed when that monster leaves the field.</item>
    /// <item>Kaminote Blow (c97570038): a face-up listed monster in your Monster Zone; this turn, at the end of each Damage
    /// Step, the monster that battled a listed monster you control is destroyed while it is still in a Monster Zone.</item>
    /// </list>
    /// </summary>
    public static class TlmEquipsAndLinkedContinuousAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";
        const string Apos = @"['’]";

        /// <summary>A quoted list of 2 or more card names: "A" or "B" / "A", "B", or "C".</summary>
        const string NameList = @"(""[^""]+""(?:,\s*""[^""]+"")*,?\s+or\s+""[^""]+"")";

        static readonly Regex RxQuoted = new(@"""([^""]+)""", RegexOptions.Compiled);

        /// <summary>The '|'-separated names of a <see cref="NameList"/> match (null when fewer than 2).</summary>
        static string Names(Match m, int group)
        {
            var names = new List<string>();
            foreach (Match q in RxQuoted.Matches(m.Groups[group].Value))
            {
                var n = q.Groups[1].Value.Trim();
                if (n.Length == 0 || n.Contains("|")) return null;
                names.Add(n);
            }

            return names.Count >= 2 ? string.Join("|", names) : null;
        }

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Legendary Black Belt.
            AtomRegistration.Atom.Sequence(
                Start + @"Equip only to " + NameList + @"\.\s*" +
                @"When the equipped monster destroys your opponent" + Apos + @"s monster by battle and sends it to the " +
                @"Graveyard, inflict damage to your opponent equal to the DEF of the destroyed monster" + End,
                m =>
                {
                    var names = Names(m, 1);
                    if (names == null) return null;
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.Activate,
                            Action = EffectActionKind.EquipThisToTarget,
                            Side = EffectSide.Both,
                            Zone = EffectZoneFilter.FieldAnyMonster,
                            RequiresTargetChoice = true,
                            IsPsctTarget = true,
                            RequiresTargetFaceUp = true,
                            TargetCount = 1,
                            AnyOfNamedCards = names,
                            AiEquipsOwnMonster = true,
                            StaysOnField = true,
                            MakesChainLink = true
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.EquippedMonsterDestroysByBattle,
                            Action = EffectActionKind.InflictDestroyedMonsterDefToOpponent,
                            Side = EffectSide.Opponent,
                            StaysOnField = true,
                            MakesChainLink = true
                        }
                    };
                }),

            // Nitro Unit.
            AtomRegistration.Atom.Sequence(
                Start + @"Equip only to a monster on your opponent" + Apos + @"s side of the field\.\s*" +
                @"When the equipped monster is destroyed by battle and sent to the Graveyard, inflict damage to your " +
                @"opponent equal to the ATK of the equipped monster" + End,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppFaceUpMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.InflictLostHostAtkToOpponentWhenHostDestroyedByBattle,
                        Side = EffectSide.Opponent,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Lone Wolf.
            AtomRegistration.Atom.Sequence(
                Start + @"Activate only if the 1 and only monster on your side of the field is " + NameList + @"\.\s*" +
                @"That monster cannot be destroyed as a result of battle, and is unaffected by any of your opponent" +
                Apos + @"s monster effects" + End,
                m =>
                {
                    var names = Names(m, 1);
                    if (names == null) return null;
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.Activate,
                            Action = EffectActionKind.LinkThisToTarget,
                            Side = EffectSide.Controller,
                            Zone = EffectZoneFilter.ControllerMonsters,
                            RequiresTargetChoice = true,
                            IsPsctTarget = true,
                            RequiresTargetFaceUp = true,
                            TargetCount = 1,
                            AnyOfNamedCards = names,
                            ActivationCondition = TlmEquipsAndLinkedContinuousRuntime.OnlyMonsterIsFaceUpNamed,
                            LinkStaysWhenHostLeaves = true,
                            StaysOnField = true,
                            MakesChainLink = true
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.ContinuousCannotBeDestroyedByBattle,
                            StaysOnField = true,
                            MakesChainLink = false
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.LinkedMonsterUnaffectedByOpponentMonsterEffects,
                            StaysOnField = true,
                            MakesChainLink = false
                        }
                    };
                }),

            // Impenetrable Formation.
            AtomRegistration.Atom.Sequence(
                Start + @"Select 1 monster on your side of the field to activate this card\.\s*" +
                @"While you have 2 or more monsters on your side of the field, and all monsters on your side of the field " +
                @"are in Defense Position, the selected monster gains (\d+) DEF\.\s*" +
                @"When the selected monster is removed from the field, destroy this card" + End,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.LinkThisToTarget,
                        Side = EffectSide.Controller,
                        Zone = EffectZoneFilter.ControllerMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        EquipDefBonus = int.Parse(m.Groups[1].Value),
                        EquipBonusRequiresTwoMonstersNoneInAttack = true,
                        DestroyThisWhenBoundHostLeaves = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    }
                }),

            // Kaminote Blow.
            AtomRegistration.Atom.Sequence(
                Start + @"Activate only if there is at least 1 face-up " + NameList + @" on your side of the field\.\s*" +
                @"Destroy the monster\(s\) that battle with these monsters on your side of the field this turn, at the end " +
                @"of each Damage Step" + End,
                m =>
                {
                    var names = Names(m, 1);
                    if (names == null) return null;
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.Activate,
                            Action = EffectActionKind.DestroyMonstersBattlingNamedAtEndOfDamageStepThisTurn,
                            Side = EffectSide.Opponent,
                            AnyOfNamedCards = names,
                            ActivationCondition = TlmEquipsAndLinkedContinuousRuntime.NamedFaceUpInYourMonsterZone,
                            MakesChainLink = true
                        }
                    };
                }),
        };
    }
}
