namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleOutcomeTriggers" (v162): monster triggers on a battle's outcome — this card destroys a monster by
    /// battle (Zombyra the Dark, Timeater, Mystical Knight of Jackal, Winged Sage Falcos), is sent to the GY by battle
    /// (Kryuel) or inflicts battle damage (Dark Scorpion Burglars, Mucus Yolk; Don Zaloog's "1 of these effects" lives in
    /// <see cref="PartialCompilesAtoms"/> with the other Dark Scorpions). Every atom is one whole sentence (Kryuel's and
    /// Mucus Yolk's two sentences together), anchored at a sentence start and end, so a longer or reworded sentence stays
    /// uncompiled. Runtime: <see cref="BattleOutcomeTriggersRuntime"/>.
    /// </summary>
    public static class BattleOutcomeTriggersAtoms
    {
        /// <summary>Sentence start: text start or after a sentence-ending mark.</summary>
        const string Start = @"(?<=^|[.!?]\s+)";
        /// <summary>Sentence end: a period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string End = @"(?:\.(?=\s|$)|$)";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Zombyra the Dark (c88472456) e2: SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING; EFFECT_UPDATE_ATTACK -200 on itself,
            // RESET_EVENT|RESETS_STANDARD_DISABLE.
            new AtomRegistration.Atom(
                Start + @"If this card destroys a monster by battle: This card loses (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = -Parse(m, 1, 0),
                    MakesChainLink = true
                }),
            // Timeater (c44913552): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING, range MZONE (still on the field); registers
            // EFFECT_SKIP_M1 for the opponent until their next Main Phase 1.
            new AtomRegistration.Atom(
                Start + @"If this card destroys an opponent" + Apos + @"s monster by battle: Your opponent skips their next " +
                @"Main Phase 1" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.OpponentSkipsNextMainPhase1,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Kryuel (c82642348): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, condition in the GY with REASON_BATTLE;
            // Duel.CallCoin(tp), then the controller selects 1 monster the opponent controls (no target) and destroys it.
            new AtomRegistration.Atom(
                Start + @"When this card is sent to the (?:GY|Graveyard) as a result of battle, toss a coin and call it\.\s*" +
                @"If you call it right, destroy 1 monster on your opponent" + Apos + @"s side of the field" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.CoinCallRightDestroyOneOppMonster,
                    Side = EffectSide.Opponent,
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),
            // Mystical Knight of Jackal (c98745000): SINGLE TRIGGER_O EVENT_BATTLE_DESTROYING; this card face-up and still
            // related to the battle, the battle target in the GY and the opponent's; SetTargetCard(bc), SendtoDeck SEQ_DECKTOP.
            new AtomRegistration.Atom(
                Start + @"When this card destroys 1 monster on your opponent" + Apos + @"s side of the field and sends it to " +
                @"your opponent" + Apos + @"s Graveyard as a result of battle, you can then return the card to the top of " +
                @"your opponent" + Apos + @"s Deck" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.PlaceTargetOnTopOfDeck,
                    Side = EffectSide.Opponent,
                    TargetIsMonsterDestroyedByThisBattle = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Winged Sage Falcos (c87523462): as Jackal, and bc:IsPreviousPosition(POS_FACEUP_ATTACK).
            new AtomRegistration.Atom(
                Start + @"When this card destroys an opponent" + Apos + @"s face-up Attack Position monster by battle and " +
                @"sends it to their Graveyard: You can place that monster on top of their Deck" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.PlaceTargetOnTopOfDeck,
                    Side = EffectSide.Opponent,
                    TargetIsMonsterDestroyedByThisBattle = true,
                    RequiresDestroyedWasFaceUpAttack = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Dark Scorpion Burglars (c40933924): SINGLE TRIGGER_F EVENT_BATTLE_DAMAGE (ep ~= tp); the opponent selects 1
            // Spell in their Deck and sends it to the GY.
            new AtomRegistration.Atom(
                Start + @"When this card inflicts Battle Damage to your opponent: Your opponent sends 1 Spell Card from their " +
                @"Deck to the (?:GY|Graveyard)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.OpponentSendsSpellFromDeckToGy,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    MakesChainLink = true
                }),
            // Mucus Yolk (c70307656) e2/e3: a flag on battle damage (reset at the end of your next Standby Phase), then a
            // FIELD TRIGGER_F at your Standby Phase, once per turn: +N ATK, RESETS_STANDARD_DISABLE.
            new AtomRegistration.Atom(
                Start + @"Each time this card inflicts Battle Damage to your opponent" + Apos + @"s Life Points, increase the " +
                @"ATK of this card by (\d+) points during your next Standby Phase\.\s*The ATK increase lasts as long as this " +
                @"card remains face-up on the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.GainThisCardAtkAtNextStandbyWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 0),
                    MakesChainLink = false
                }),
            // Raging Flame Sprite (c90810762) e2: SINGLE TRIGGER_F EVENT_BATTLE_DAMAGE on a DIRECT attack only
            // (the battle has no target); EFFECT_UPDATE_ATTACK +1000 on itself with RESETS_STANDARD_DISABLE, so it
            // is permanent while the card stays face-up. Its first sentence ("can attack your opponent directly")
            // is already compiled by the generic direct-attack pass.
            new AtomRegistration.Atom(
                Start + @"If this card inflicts battle damage to your opponent by a direct attack:\s*" +
                @"This card gains (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 1000),
                    RequiresDirectAttack = true,
                    MakesChainLink = true
                }),
            // v208 QC: Sasuke Samurai #4's atom (StartOfDamageStep CoinCallDestroyBattledMonsterOnCorrect) was removed — no Damage
            // Step site tossed its coin (TryStartOfDamageStepDestroy reads only Destroy clauses), and the coin-call branch it
            // pointed at read Activate clauses and fell back to destroying any face-up opponent's monster. The card is
            // uncompiled again, as in v206 (a Start of Damage Step coin call with a human call needs a battle pause).
            // RdsRedoBattle batch (v210 group): compiled again by RdsRedoBattleAtoms with a real Damage Step pause
            // (RdsRedoBattleRuntime.TryBeginBattleStartCoinCalls).
        };

        static int Parse(System.Text.RegularExpressions.Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group || !m.Groups[group].Success) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
