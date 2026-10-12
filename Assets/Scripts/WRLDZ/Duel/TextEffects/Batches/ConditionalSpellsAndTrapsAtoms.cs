using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ConditionalSpellsAndTraps": Invasion of Chaos Spells and Traps whose activation condition (a count, an
    /// LP gap, named face-up monsters) stands in front of an effect the engine already resolves. Every atom owns the
    /// card's whole text (anchored ^…$): the old "You can only activate this card if …" sentence never reaches
    /// PsctConditionGate on its own, so the condition cannot be dropped while the effect stays compiled.
    /// Runtime: <see cref="ConditionalSpellsAndTrapsRuntime"/>.
    /// <list type="bullet">
    /// <item>Blasting the Ruins (c21466326): 30+ cards in your GY; 3000 damage to the opponent.</item>
    /// <item>Chaos End (c61044390): 7+ of your cards banished and a monster on the field; destroy all monsters.</item>
    /// <item>Chaos Greed (c97439308): 4+ of your cards banished, none in your GY, you can draw 2; draw 2.</item>
    /// <item>Self-Destruct Button (c57585212): your LP at least 7000 lower; both players' LP become 0 (a DRAW).</item>
    /// <item>Ojama Delta Hurricane!! (c8251996): the three named Ojamas face-up on your field and the opponent controls a
    /// card; destroy every card the opponent controls.</item>
    /// <item>Gryphon's Feather Duster (c34370473): destroy every other Spell/Trap you control; gain 500 x destroyed.</item>
    /// <item>Destruction Ring (c21219755): target 1 face-up monster you control; destroy it, then 1000 damage to both.</item>
    /// <item>D.D. Borderline (c60912752): Continuous Spell; no Battle Phase for either player while its controller's GY
    /// holds no Spell.</item>
    /// </list>
    /// </summary>
    public static class ConditionalSpellsAndTrapsAtoms
    {
        const string Gy = @"(?:Graveyard|GY)";
        const string Apos = @"['’]";
        const string Q = @"[""“”]";
        const string S = @"^\s*";
        const string E = @"\.?\s*$";
        const string OnlyIf = @"You can only activate this card ";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Blasting the Ruins (c21466326): Normal Trap, free chain; condition GetFieldGroupCount(tp, GRAVE, 0) >= 30;
            // Duel.Damage(1-tp, 3000).
            new AtomRegistration.Atom(
                S + OnlyIf + @"if there are (\d+) or more cards in your " + Gy + @"\.\s*" +
                @"Inflict (\d+) (?:points of )?damage to your opponent(?:" + Apos + @"s Life Points)?" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 2, 3000),
                    ActivationCondition = ConditionalSpellsAndTrapsRuntime.YourGyAtLeast,
                    ConditionAmount = Parse(m, 1, 30),
                    MakesChainLink = true
                }),

            // Chaos End (c61044390): condition GetFieldGroupCount(tp, REMOVED, 0) >= 7; target chk a monster in either
            // Monster Zone; Duel.Destroy(every monster on the field) — one event.
            new AtomRegistration.Atom(
                S + OnlyIf + @"if (\d+) or more of your cards are currently removed from play\.\s*" +
                @"Destroy all Monster Cards on the field" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldMonsters,
                    ActivationCondition = ConditionalSpellsAndTrapsRuntime.YourBanishedAtLeastAndMonsterOnField,
                    ConditionAmount = Parse(m, 1, 7),
                    MakesChainLink = true
                }),

            // Chaos Greed (c97439308): condition REMOVED >= 4 and GRAVE == 0 (yours); target chk IsPlayerCanDraw(tp, 2);
            // Duel.Draw(tp, 2).
            new AtomRegistration.Atom(
                S + OnlyIf + @"if (\d+) or more of your cards are currently removed from play and there are no cards " +
                @"in your " + Gy + @"\.\s*Draw (\d+) cards from your Deck" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 2, 2),
                    ActivationCondition = ConditionalSpellsAndTrapsRuntime.YourBanishedAtLeastNoGyCanDraw,
                    ConditionAmount = Parse(m, 1, 4),
                    MakesChainLink = true
                }),

            // Self-Destruct Button (c57585212): condition GetLP(tp) <= GetLP(1-tp) - 7000 (Frozen Soul's LP gap);
            // Duel.SetLP(tp, 0) + Duel.SetLP(1-tp, 0): not damage; both at 0 is a DRAW.
            new AtomRegistration.Atom(
                S + OnlyIf + @"while your Life Points are lower than your opponent" + Apos + @"s Life Points and the " +
                @"difference is (\d+) points or more\.\s*Both players" + Apos + @" Life Points become 0" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BothPlayersLifePointsBecomeZero,
                    Side = EffectSide.Both,
                    ActivationCondition = DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent,
                    ConditionAmount = Parse(m, 1, 7000),
                    MakesChainLink = true
                }),

            // Ojama Delta Hurricane!! (c8251996): condition — each named card face-up on your field (LOCATION_ONFIELD);
            // target chk the opponent controls a card; Duel.Destroy(every card the opponent controls) — one event.
            new AtomRegistration.Atom(
                S + OnlyIf + @"while " + Q + @"([^""“”]+)" + Q + @", " + Q + @"([^""“”]+)" + Q + @",? and " + Q +
                @"([^""“”]+)" + Q + @" are face-up on your side of the field\.\s*" +
                @"Destroy all cards on your opponent" + Apos + @"s side of the field" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppAnyCardOnField,
                    ActivationCondition = ConditionalSpellsAndTrapsRuntime.NamedFaceUpAndOpponentControlsACard,
                    ConditionName = m.Groups[1].Value.Trim() + "|" + m.Groups[2].Value.Trim() + "|" +
                                    m.Groups[3].Value.Trim(),
                    MakesChainLink = true
                }),

            // Gryphon's Feather Duster (c34370473): target chk a Spell/Trap you control other than this card; destroy
            // every other Spell/Trap you control (one event), then Duel.Recover(tp, destroyed x 500) — the count Duel.Destroy
            // returns, not the number that was there.
            AtomRegistration.Atom.Sequence(
                S + @"Destroy all Spell and Trap Cards on your side of the field\.\s*" +
                @"Increase your Life Points by the number of destroyed cards (?:x|×) (\d+) points" + E,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Controller,
                        Zone = EffectZoneFilter.FieldSpellTraps,
                        ExceptThisCard = true,
                        ActivationCondition = ConditionalSpellsAndTrapsRuntime.YouControlAnotherSpellTrap,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.GainLifePoints,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1, 500),
                        CountSource = PerCountBurnsRuntime.PreviousClauseHits,
                        RequiresPreviousClauseHit = true,
                        MakesChainLink = true
                    }
                }),

            // Destruction Ring (c21219755): EFFECT_FLAG_CARD_TARGET, SelectTarget 1 face-up monster you control; if it is
            // still face-up and related and Duel.Destroy returns 1, 1000 damage to the opponent and to you at once.
            new AtomRegistration.Atom(
                S + @"Destroy 1 face-up monster on your side of the field to inflict (\d+) points of damage to both " +
                @"players" + Apos + @" Life Points" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyTargetThenDamageBothPlayers,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    Amount = Parse(m, 1, 1000),
                    MakesChainLink = true
                }),

            // D.D. Borderline (c60912752): Continuous Spell; EFFECT_CANNOT_BP, target range (1,1), while
            // no Spell is in the handler player's GY.
            new AtomRegistration.Atom(
                S + @"When there are no Spell Cards in your " + Gy + @", neither player can conduct their Battle Phase" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NoBattlePhaseWhileNoSpellInControllerGy,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
        };
    }
}
