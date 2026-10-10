using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Cards that were FullyCompiled from only part of their text (fail-closed audit, 2026-09-29): the generic PSCT
    /// sentence compiler took "destroy it" out of Assault on GHQ's "destroy it, then send 2 cards …", and a leftover
    /// "● …" bullet counted as boilerplate, so the Dark Scorpions kept only their first effect. Each atom here is the
    /// card's whole text; a bullet this file cannot compile exactly leaves the card uncompiled.
    /// <para>
    /// ChoiceBullets batch (v173): Dark Scorpion - Chick the Yellow (c61587183), Dark Scorpion - Meanae the Thorn
    /// (c74153887), and Cyber Raider (c39978267) — all three compile both bullets as one choice. Chick's bullet 2 is
    /// an immediate top-or-bottom look at the opponent's Deck; Meanae's bullets search the Deck/GY for "Dark Scorpion"
    /// cards; Cyber Raider's two bullets both target Equip Cards on the field (destroy, or equip to this card).
    /// </para>
    /// </summary>
    public static class PartialCompilesAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the text's end.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Assault on GHQ (c62633180): target any monster you control (face-down too); destroy it, and only if it
            // was destroyed, the opponent sends the top 2 cards of their Deck to the GY
            // (EffectClause.ThenMillOpponentDeck).
            new AtomRegistration.Atom(
                S + @"Target 1 monster you control;\s*destroy it, then send (\d+) cards? from the top of your " +
                @"opponent's Deck to the (?:GY|Graveyard)" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    ThenMillOpponentDeck = Parse(m, 1, 2),
                    MakesChainLink = true
                }),
            // Dark Scorpion - Cliff the Trap Remover (c6967870) / Gorg the Strong (c48768179): one optional trigger,
            // 1 of 2 effects (EffectChoiceRuntime). At most one bullet targets. Don Zaloog (c76922029, BattleOutcomeTriggers
            // batch v162) prints ": You can activate 1 of these effects;" and neither of its bullets targets.
            AtomRegistration.Atom.Sequence(
                S + @"When this card inflicts Battle Damage to your opponent(?:, you can|: You can) activate 1 of these " +
                @"effects[:;]" +
                @"\s*●\s*(?<a>[^●]+?)\s*●\s*(?<b>[^●]+?)" + E,
                m => ChoiceOf(EffectTiming.ThisCardInflictsBattleDamage, m.Groups["a"].Value, m.Groups["b"].Value)),
            // ChoiceBullets batch (v173) — Cyber Raider (c39978267): "If this card is Summoned: Activate 1 of these
            // effects" — both bullets target Equip Cards on the field (destroy, or equip to this card).
            AtomRegistration.Atom.Sequence(
                S + @"(?:When|If) this card is Summoned: Activate 1 of these effects[.;]" +
                @"\s*●\s*(?<a>[^●]+?)\s*●\s*(?<b>[^●]+?)" + E,
                m => ChoiceOf(EffectTiming.ThisCardSummoned, m.Groups["a"].Value, m.Groups["b"].Value)),
        };

        static readonly Regex RxDestroySpellTrapOnField = new(
            @"^Target 1 Spell/Trap Card on the field;\s*destroy that target\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxOppMonsterToTopOfDeck = new(
            @"^Target 1 monster your opponent controls;\s*return that target to the top of the Deck\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Don Zaloog: "Discard 1 random card from their hand." (Lua RandomSelect, REASON_DISCARD+REASON_EFFECT).</summary>
        static readonly Regex RxDiscardRandomTheirHand = new(
            @"^Discard 1 random card from their hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMillTheirDeck = new(
            @"^Send the top (?:card|(\d+) cards) of their Deck to the (?:GY|Graveyard)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Chick b1: "Target 1 card on the field; return that target to the hand."</summary>
        static readonly Regex RxOppMonsterToHand = new(
            @"^Target 1 card on the field;\s*return that target to the hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Chick b2: "Look at the top card of their Deck, then return it to the top or bottom of the Deck."</summary>
        static readonly Regex RxLookAtTopCardTopOrBottom = new(
            @"^Look at the top card of their Deck,\s*then return it to the top or bottom of the Deck\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Meanae b1: "Add 1 "Dark Scorpion" card from your Deck to your hand."</summary>
        static readonly Regex RxAddDarkScorpionFromDeck = new(
            @"^Add 1 ""Dark Scorpion"" card from your Deck to your hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Meanae b2: "Target 1 "Dark Scorpion" card in your Graveyard; add that target to your hand."</summary>
        static readonly Regex RxAddDarkScorpionFromGy = new(
            @"^Target 1 ""Dark Scorpion"" card in your Graveyard;\s*add that target to your hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Cyber Raider b1: "Target 1 Equip Card on the field; destroy that target."</summary>
        static readonly Regex RxDestroyEquippedEquip = new(
            @"^Target 1 Equip Card on the field;\s*destroy that target\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Cyber Raider b2: "Target 1 Equip Card on the field; equip that target to this card."</summary>
        static readonly Regex RxEquipEquippedToThis = new(
            @"^Target 1 Equip Card on the field;\s*equip that target to this card\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The bullets of one "1 of these effects" choice, or null (the card stays uncompiled) unless every bullet
        /// compiles and at most two of them target (Cyber Raider: both target Equip Cards on the field; the picker
        /// offers the first bullet's targets, CANCEL offers the second bullet's narrower pool).
        /// </summary>
        static EffectClause[] ChoiceOf(EffectTiming timing, params string[] bullets)
        {
            if (bullets == null || bullets.Length != 2) return null;
            var clauses = new EffectClause[bullets.Length];
            var targeted = 0;
            for (var i = 0; i < bullets.Length; i++)
            {
                var c = Bullet((bullets[i] ?? string.Empty).Trim());
                if (c == null) return null;
                c.Timing = timing;
                c.EffectChoiceIndex = i + 1;
                c.IsOptional = true;
                c.MakesChainLink = true;
                if (c.RequiresTargetChoice) targeted++;
                clauses[i] = c;
            }

            // Two targeted bullets compile only when they share one target pool (Cyber Raider: both target Equip Cards
            // on the field; the picker offers b1's targets, CANCEL offers b2's narrower equip-legal subset). Two targeted
            // bullets with different pools (e.g. a Spell/Trap destroy vs a monster bounce) are refused: the picker offers
            // only the first targeted bullet's pool and has no second pool to swap to on CANCEL.
            if (targeted == 2 && clauses[0].Zone != clauses[1].Zone) return null;
            return targeted <= 2 ? clauses : null;
        }

        static EffectClause Bullet(string text)
        {
            // Cliff: any Spell/Trap on either field, Field Spells included.
            if (RxDestroySpellTrapOnField.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true
                };
            // Gorg: any monster the opponent controls, face-up or face-down (Lua: IsAbleToDeck in their MZONE).
            if (RxOppMonsterToTopOfDeck.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.PlaceTargetOnTopOfDeck,
                    Zone = EffectZoneFilter.OppMonsters,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true
                };
            if (RxDiscardRandomTheirHand.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = 1
                };
            var mill = RxMillTheirDeck.Match(text);
            if (mill.Success)
                return new EffectClause
                {
                    Action = EffectActionKind.SendFromTopOfDeckToGy,
                    Side = EffectSide.Opponent,
                    Amount = Parse(mill, 1, 1)
                };
            // Chick b1: return any card on the field to the hand.
            if (RxOppMonsterToHand.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.ReturnToHand,
                    Zone = EffectZoneFilter.AnyCardOnField,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true
                };
            // Chick b2: look at the top card of their Deck, then top-or-bottom.
            if (RxLookAtTopCardTopOrBottom.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.LookAtOpponentTopCardThenTopOrBottom,
                    Side = EffectSide.Opponent
                };
            // Meanae b1: add a "Dark Scorpion" card from the Deck to hand.
            if (RxAddDarkScorpionFromDeck.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.AddFromDeckToHand,
                    Zone = EffectZoneFilter.ControllerDeckCards,
                    Side = EffectSide.Controller,
                    TargetSeriesName = "Dark Scorpion"
                };
            // Meanae b2: add a "Dark Scorpion" card from the GY to hand.
            if (RxAddDarkScorpionFromGy.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.AddFromGyToHand,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    Side = EffectSide.Controller,
                    TargetSeriesName = "Dark Scorpion",
                    RequiresTargetChoice = true,
                    IsPsctTarget = true
                };
            // Cyber Raider b1: destroy an Equip Card equipped to a monster.
            if (RxDestroyEquippedEquip.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldEquipCards,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true
                };
            // Cyber Raider b2: equip an Equip Card equipped to a monster to this card.
            if (RxEquipEquippedToThis.IsMatch(text))
                return new EffectClause
                {
                    Action = EffectActionKind.EquipTargetToThis,
                    Zone = EffectZoneFilter.FieldEquipCards,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true
                };
            return null;
        }

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group || !m.Groups[group].Success) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
