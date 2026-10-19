using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvBattleStepRiders" (CRV / EEN): attack-time riders — the end-of-Battle-Phase Defense lock after attacking
    /// (Goblin Elite Attack Force, Indomitable Fighter Lei Lei), Damage Step ATK by battle role (Steamroid, Etoile Cyber),
    /// destruction before damage calculation (Drillroid) or at the start of the Damage Step (Nanobreaker), and battle-destroyed
    /// GY burns (Chthonian Soldier, Oxygeddon). Every atom is the card's whole text (<c>^…$</c>), so a longer card that only
    /// contains part of it stays uncompiled. Runtime: <see cref="CrvBattleStepRidersRuntime"/>.
    /// </summary>
    public static class CrvBattleStepRidersAtoms
    {
        const string Apos = @"['’]";
        const string Gy = @"(?:GY|Graveyard)";

        static int Int(Match m, string group) => int.TryParse(m.Groups[group].Value, out var n) ? n : 0;

        static EffectClause DefenseLockAfterAttacking() => new EffectClause
        {
            Timing = EffectTiming.EndOfBattlePhase,
            Action = EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition,
            MakesChainLink = false
        };

        static EffectClause DamageStepAtk(int amount) => new EffectClause
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.ThisAtkChangesDuringDamageStepByBattleRole,
            Amount = amount,
            MakesChainLink = false
        };

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Goblin Elite Attack Force (c85306040): the same Lua as Goblin Attack Force (EVENT_PHASE+PHASE_BATTLE continuous,
            // GetAttackedCount() > 0; to face-up Defense, then EFFECT_CANNOT_CHANGE_POSITION — manual changes only — reset at
            // the 3rd End Phase, i.e. through the controller's next turn). Not a chain link.
            new AtomRegistration.Atom(
                @"^\s*If this card attacks, it is changed to Defense Position at the end of the Battle Phase, and its battle " +
                @"position cannot be changed until the end of your next turn\.\s*$",
                _ => DefenseLockAfterAttacking()),
            // Indomitable Fighter Lei Lei (c84173492): the same Lua; "except with a card effect" is what
            // EFFECT_CANNOT_CHANGE_POSITION already means (BattlePhaseEndRuntime.ManualPositionChangeLocked).
            new AtomRegistration.Atom(
                @"^\s*If this card attacks, it is changed to Defense Position at the end of the Battle Phase\. This card" + Apos +
                @"s battle position cannot be changed until the end of your next turn, except with a card effect\.\s*$",
                _ => DefenseLockAfterAttacking()),
            // Steamroid (c44729197): EFFECT_UPDATE_ATTACK during PHASE_DAMAGE / PHASE_DAMAGE_CAL — +N while it is the attacker
            // and there is an attack target, -N while it is the attack target.
            AtomRegistration.Atom.Sequence(
                @"^\s*If this card attacks an opponent" + Apos + @"s monster, this card gains (?<g>\d+) ATK during the Damage " +
                @"Step only\. If this card is attacked by an opponent" + Apos + @"s monster, this card loses (?<l>\d+) ATK " +
                @"during the Damage Step only\.\s*$",
                m =>
                {
                    var gain = DamageStepAtk(Int(m, "g"));
                    gain.DamageStepOnlyWhenAttacking = true;
                    var loss = DamageStepAtk(-Int(m, "l"));
                    loss.RequiresThisIsAttackTarget = true;
                    return new[] { gain, loss };
                }),
            // Etoile Cyber (c11460577): EFFECT_UPDATE_ATTACK during PHASE_DAMAGE / PHASE_DAMAGE_CAL while it is the attacker
            // and there is no attack target.
            new AtomRegistration.Atom(
                @"^\s*If this card attacks your opponent directly, it gains (?<n>\d+) ATK during the Damage Step only\.\s*$",
                m =>
                {
                    var c = DamageStepAtk(Int(m, "n"));
                    c.RequiresDirectAttack = true;
                    return c;
                }),
            // Drillroid (c71218746): Disc Fighter's Lua without the DEF floor — SINGLE TRIGGER_F EVENT_BATTLE_CONFIRM (the
            // face-down target already flipped), the attack target not in Attack Position → Destroy(REASON_EFFECT); the target
            // left, so there is no damage calculation. Amount 0 = no DEF floor.
            new AtomRegistration.Atom(
                @"^\s*Before damage calculation, if this card attacks a Defense Position monster: Destroy that monster\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.BeforeDamageCalculation,
                    Action = EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation,
                    Side = EffectSide.Opponent,
                    Amount = 0,
                    MakesChainLink = true
                }),
            // Nanobreaker (c70948327): SINGLE TRIGGER_F EVENT_BATTLE_START (before a face-down target is flipped), attacker =
            // this card, the attack target face-up (any position) and IsLevelBelow(N) → Destroy(REASON_EFFECT); no damage
            // calculation. TextEffectRuntime.TryStartOfDamageStepDestroy (CrvBattleStepRidersRuntime.StartOfDamageStepTargetAllowed).
            new AtomRegistration.Atom(
                @"^\s*If this card attacks a face-up Level (?<n>\d+) or lower monster, destroy the monster immediately with " +
                @"this card" + Apos + @"s effect without applying damage calculation\.\s*$",
                m => Int(m, "n") <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.StartOfDamageStep,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Opponent,
                        RequiresTargetFaceUp = true,
                        MonsterLevelMax = Int(m, "n"),
                        MakesChainLink = true
                    }),
            // Chthonian Soldier (c50916353): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, in the GY, destroyed as the attack target
            // (ev==1); damage to the opponent = Duel.GetBattleDamage(tp), and it does not activate when that is 0.
            new AtomRegistration.Atom(
                @"^\s*When this card is destroyed by an opponent" + Apos + @"s attacking monster and sent to the " + Gy +
                @", the Battle Damage you took from this battle is also inflicted to your opponent\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.InflictBattleDamageYouTookThisBattleToOpponent,
                    Side = EffectSide.Opponent,
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),
            // Oxygeddon (c58071123): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, in the GY, its battle target IsRace(PYRO); each
            // player takes 800 at the same time (Damage(…, true) x2 + RDComplete: a double KO is a DRAW).
            new AtomRegistration.Atom(
                @"^\s*If this card is destroyed by battle with an? (?<t>[A-Za-z][A-Za-z-]*(?: [A-Za-z]+)?) monster and sent to the " + Gy +
                @": Each player takes (?<n>\d+) damage\.\s*$",
                m => !ClassicEraTemplates.MonsterTypes.Contains(m.Groups["t"].Value) || Int(m, "n") <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.InflictDamageToBothPlayers,
                        Amount = Int(m, "n"),
                        Side = EffectSide.Both,
                        RequiresThisDestroyedByBattle = true,
                        BattledMonsterRaceFilter = m.Groups["t"].Value,
                        MakesChainLink = true
                    }),
        };
    }
}
