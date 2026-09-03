using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>Standby / End Phase trigger templates from official text.</summary>
    public static class PhaseTriggerTemplates
    {
        static readonly Regex RxStandbyDmgSelf = new(
            @"during your standby phase:\s*take (\d+) damage",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Falling Down: During each of your opponent's Standby Phases: You take 800 damage.</summary>
        static readonly Regex RxOppStandbyYouTakeDmg = new(
            @"During each of your opponent's Standby Phases:\s*You take (\d+) damage\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxStandbyDmgOpp = new(
            @"(?:Once per turn,\s*)?during your standby phase:\s*inflict (\d+) damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxStandbyGainLp = new(
            @"during your standby phase:\s*(?:you gain|increase your life points by) (\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Pre-PSCT: "increase your Life Points by 800 points during each of your Standby Phases."
        /// Cure Mermaid / Spirit of the Breeze / Dancing Fairy.
        /// </summary>
        static readonly Regex RxStandbyGainLpEach = new(
            @"(?:As long as this card remains (?:in )?face-up(?: Attack Position| Defense Position)?" +
            @"(?: on your side of the field)?,?\s*)?" +
            @"increase your Life Points by (\d+) points during each of your Standby Phases\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAsLongAsAtk = new(
            @"As long as this card remains in face-up Attack Position",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAsLongAsDef = new(
            @"As long as this card remains in face-up Defense Position",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPikeru = new(
            @"During your Standby Phase, increase your Life Points by (\d+) points " +
            @"for each monster on your side of the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxLvSendSs = new(
            @"(?:once per turn,\s*)?during (?:your standby phase|the end phase)[^.]{0,80}?:\s*you can send this (?:face-up )?card to the (?:GY|Graveyard);\s*" +
            @"special summon 1 ""([^""]+)"" from your hand or deck",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSpiritBounce = new(
            @"during the end phase of the turn (?:this card (?:is|was) |that this card (?:is|was) )?" +
            @"(?:normal summoned or flipped face-up|this card is normal summoned or flipped face-up)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxReturnHand = new(
            @"return (?:it|this card) to (?:the owner's|its owner's|the) hand",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Ectoplasmer: Once per turn, during each player's End Phase: The turn player
        /// must Tribute 1 face-up monster, and if they do, inflict damage equal to half
        /// the original ATK of the Tributed monster.
        /// </summary>
        static readonly Regex RxEndPhaseTurnPlayerTributeHalfAtk = new(
            @"Once per turn, during each player's End Phase:\s*" +
            @"The turn player must Tribute 1 face-up monster, and if they do, " +
            @"inflict damage to their opponent equal to half the original ATK of the Tributed monster\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEndPhaseTurnPlayerChangePos = new(
            @"Once per turn, during each player's End Phase:\s*" +
            @"Change the battle positions of all face-up monsters the turn player controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxActivateDestroyFieldSpells = new(
            @"When this card is activated:\s*" +
            @"If there are any Field Spell Cards on the field, destroy them\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxStandbyTurnPlayerTakesDmg = new(
            @"During each player's Standby Phase:\s*The turn player takes (\d+) damage\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Fox Fire family. Official text: End Phase, if this card was destroyed by battle
        /// and sent to the GY this turn (and was face-up at the start of the Damage Step):
        /// Special Summon this card from the GY.
        /// Ignis/OcgCore c88753985: EVENT_BATTLE_DESTROYED registers PHASE_END TRIGGER_F.
        /// </summary>
        static readonly Regex RxEndPhaseBattleGySelfSs = new(
            @"During the End Phase, if this card was destroyed by battle and sent to the (?:GY|Graveyard) this turn" +
            @"(?: and was face-up at the start of the Damage Step)?:\s*" +
            @"Special Summon this card from the (?:GY|Graveyard)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def,
            System.Collections.Generic.List<EffectClause> into,
            System.Collections.Generic.List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
            }

            Add(RxStandbyDmgSelf.Match(text), new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = Parse(RxStandbyDmgSelf.Match(text), 1, 1000),
                MakesChainLink = true
            });
            var oppDmg = RxOppStandbyYouTakeDmg.Match(text);
            Add(oppDmg, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = oppDmg.Success ? Parse(oppDmg, 1, 800) : 800,
                OpponentTurnOnly = true,
                MakesChainLink = true
            });
            Add(RxStandbyDmgOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.InflictDamageToOpponent,
                Amount = Parse(RxStandbyDmgOpp.Match(text), 1, 600),
                MakesChainLink = true
            });
            var lp = RxStandbyGainLp.Match(text);
            Add(lp, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.GainLifePoints,
                Amount = lp.Success ? Parse(lp, 1, 200) : 200,
                ResolvesFromGy = Regex.IsMatch(text, @"must be in the graveyard", RegexOptions.IgnoreCase),
                MakesChainLink = true
            });

            var each = RxStandbyGainLpEach.Match(text);
            if (each.Success && !lp.Success)
            {
                Add(each, new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GainLifePoints,
                    Amount = Parse(each, 1, 800),
                    RequiresThisAttackPosition = RxAsLongAsAtk.IsMatch(text),
                    RequiresThisDefensePosition = RxAsLongAsDef.IsMatch(text),
                    MakesChainLink = true
                });
            }

            var pik = RxPikeru.Match(text);
            Add(pik, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.GainLifePoints,
                Amount = pik.Success ? Parse(pik, 1, 400) : 400,
                ScaleAmountByControllerMonsters = true,
                MakesChainLink = true
            });

            var lv = RxLvSendSs.Match(text);
            if (lv.Success)
            {
                var end = Regex.IsMatch(lv.Value, @"end phase", RegexOptions.IgnoreCase);
                Add(lv, new EffectClause
                {
                    Timing = end ? EffectTiming.EndPhase : EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SpecialSummonNamed,
                    NamedCard = lv.Groups[1].Value,
                    RequiresSendThisToGy = true,
                    FromHand = true,
                    FromDeck = true,
                    IsOptional = true,
                    OncePerTurn = Regex.IsMatch(lv.Value, @"once per turn", RegexOptions.IgnoreCase),
                    RequiresDestroyedByBattleThisTurn = end &&
                        Regex.IsMatch(lv.Value, @"destroyed a monster by battle", RegexOptions.IgnoreCase),
                    MakesChainLink = true
                });
            }

            if (RxSpiritBounce.IsMatch(text) && RxReturnHand.IsMatch(text))
            {
                var m = RxSpiritBounce.Match(text);
                Add(m, new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ReturnToHand,
                    RequiresSummonedOrFlippedThisTurn = true,
                    MakesChainLink = true
                });
            }

            var ecto = RxEndPhaseTurnPlayerTributeHalfAtk.Match(text);
            Add(ecto, ecto.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.InflictDamageHalfTributedAtk,
                    RequiresTributeCount = 1,
                    TributeFaceUpOnly = true,
                    TurnPlayerTributes = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            var laby = RxEndPhaseTurnPlayerChangePos.Match(text);
            Add(laby, laby.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ChangeBattlePosition,
                    TurnPlayerIsSubject = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            var burnAct = RxActivateDestroyFieldSpells.Match(text);
            Add(burnAct, burnAct.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellsOnField,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            var burnSt = RxStandbyTurnPlayerTakesDmg.Match(text);
            Add(burnSt, burnSt.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.TakeEffectDamage,
                    Amount = Parse(burnSt, 1, 500),
                    TurnPlayerIsSubject = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            Add(RxEndPhaseBattleGySelfSs.Match(text), new EffectClause
            {
                Timing = EffectTiming.EndPhase,
                Action = EffectActionKind.SpecialSummonFromGy,
                ResolvesFromGy = true,
                RequiresThisDestroyedByBattle = true,
                MakesChainLink = true
            });
        }

        public static void ExpectedActions(CardDef def, System.Collections.Generic.List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxStandbyGainLp.IsMatch(text) || RxStandbyGainLpEach.IsMatch(text) ||
                RxPikeru.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
            if (RxStandbyDmgSelf.IsMatch(text) || RxOppStandbyYouTakeDmg.IsMatch(text))
                need.Add(EffectActionKind.TakeEffectDamage);
            if (RxEndPhaseTurnPlayerTributeHalfAtk.IsMatch(text))
                need.Add(EffectActionKind.InflictDamageHalfTributedAtk);
            if (RxEndPhaseTurnPlayerChangePos.IsMatch(text))
                need.Add(EffectActionKind.ChangeBattlePosition);
            if (RxActivateDestroyFieldSpells.IsMatch(text))
                need.Add(EffectActionKind.Destroy);
            if (RxStandbyTurnPlayerTakesDmg.IsMatch(text))
                need.Add(EffectActionKind.TakeEffectDamage);
            if (RxEndPhaseBattleGySelfSs.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
        }

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
        }
    }
}
