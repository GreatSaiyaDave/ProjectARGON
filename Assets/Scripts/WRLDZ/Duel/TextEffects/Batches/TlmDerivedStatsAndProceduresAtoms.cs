using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmDerivedStatsAndProcedures" (TLM): self ATK/DEF taken from counts or from the summon, a battle ATK
    /// growth and hand Special Summon procedures. Whole-sentence shapes, never a card-name list. Runtime:
    /// <see cref="TlmDerivedStatsAndProceduresRuntime"/> (Ultimate Insect LV7's aura is the shared LvChainAtoms sentence).
    /// <list type="bullet">
    /// <item>Millennium Scorpion (c82482194): FIELD TRIGGER_F EVENT_BATTLE_DESTROYED, a monster this card destroyed by battle
    /// is in the GY → UPDATE_ATTACK +N on itself, RESETS_STANDARD_DISABLE (BattleOutcomeTriggers' lasting
    /// <see cref="EffectActionKind.ChangeThisCardAtkWhileFaceUp"/>; a banished one does not count).</item>
    /// <item>Batteryman AA (c63142001): two SINGLE UPDATE_ATTACK / UPDATE_DEFENSE of N x the face-up copies you control, each
    /// live only while every face-up copy is in that battle position.</item>
    /// <item>Lost Guardian (c45871897): SET_BASE_DEFENSE = your face-up banished monsters of the Type x N (printed DEF "?").</item>
    /// <item>King of the Skull Servants (c36021814): SET_BASE_ATTACK = the cards with either name in your GY x N; SINGLE
    /// TRIGGER_O EVENT_BATTLE_DESTROYED in the GY, cost banish 1 other card with either name from the GY, Special Summon
    /// this card.</item>
    /// <item>Master Monk (c49814180): Nomi (EnableReviveLimit + EFFECT_SPSUMMON_CONDITION), EFFECT_SPSUMMON_PROC from the
    /// hand Tributing 1 named monster you control (CheckReleaseGroup: face-down too); EFFECT_EXTRA_ATTACK 1.</item>
    /// <item>Megarock Dragon (c71544954): Semi-Nomi (EnableReviveLimit + AddMustBeSpecialSummoned), EFFECT_SPSUMMON_PROC banishing 1 or more monsters of the Type from the GY
    /// (SpElimFilter); SET_BASE_ATTACK / _DEFENSE = the number banished x N.</item>
    /// </list>
    /// </summary>
    public static class TlmDerivedStatsAndProceduresAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis or quote.</summary>
        const string Start = @"(?:^|(?<=[.!?)""]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        const string Gy = @"(?:GY|Graveyard)";
        const string Q = @"[""“”]";
        const string Name = Q + @"([^""“”]+)" + Q;
        const string Apos = @"['’]";

        const string Race =
            @"(Aqua|Beast-Warrior|Winged Beast|Beast|Creator God|Cyberse|Dinosaur|Divine-Beast|Dragon|Fairy|Fiend|Fish|" +
            @"Illusion|Insect|Machine|Plant|Psychic|Pyro|Reptile|Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Wyrm|Zombie)";

        /// <summary>"This card cannot be Normal Summoned or Set." (the structural lock PsctGrammar.BlocksNormalSummonOrSet).</summary>
        const string NoNormalSummon = @"This card cannot be Normal Summoned or Set\.\s*";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Millennium Scorpion: each battle destruction it causes (the destroyed monster in the GY) → +N ATK while it
            // stays face-up on the field (lost for good once negated).
            new AtomRegistration.Atom(
                Start + @"Each time this card destroys 1 monster on your opponent" + Apos + @"s side of the field by battle " +
                @"and sends it to the " + Gy + @", this card gains (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 0),
                    MakesChainLink = true
                }),

            // Batteryman AA: one sentence per stat (an atom matches once per text) — ATK while all face-up copies are in
            // Attack Position, DEF while all are in Defense Position.
            new AtomRegistration.Atom(
                Start + @"If all " + Name + @"\(s\) you control are in Attack Position, this card gains (\d+) ATK for each" + End,
                m => AllInPosition(m, attack: true)),
            new AtomRegistration.Atom(
                Start + @"If all " + Name + @"\(s\) you control are in Defense Position, this card gains (\d+) DEF for each" +
                End,
                m => AllInPosition(m, attack: false)),

            // Lost Guardian: original DEF = your banished monsters of the Type x N (ATK untouched).
            new AtomRegistration.Atom(
                Start + @"The original DEF of this card equals the number of your banished " + Race + @"(?:-Type)? monsters " +
                @"(?:×|x) (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = 0,
                    DefAmount = Parse(m, 2, 0),
                    RaceFilter = m.Groups[1].Value,
                    CountSource = TlmDerivedStatsAndProceduresRuntime.YourFaceUpBanishedRace,
                    SelfStatIsSet = true,
                    MakesChainLink = false
                }),

            // King of the Skull Servants: original ATK = the cards with either name in your GY x N.
            new AtomRegistration.Atom(
                Start + @"The original ATK of this card is the combined number of " + Name + @" and " + Name + @" in your " +
                Gy + @" x (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    AltNamedCard = m.Groups[2].Value,
                    Amount = Parse(m, 3, 0),
                    DefAmount = 0,
                    CountSource = TlmDerivedStatsAndProceduresRuntime.NamedOrAltInYourGy,
                    SelfStatIsSet = true,
                    MakesChainLink = false
                }),

            // King of the Skull Servants: the optional battle-destroyed revival; the banish is its cost.
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed by battle and sent to the " + Gy + @": You can banish 1 other " + Name +
                @" or 1 " + Name + @" from your " + Gy + @"; Special Summon this card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonThisFromGyByBanishingNamedFromGy,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    AltNamedCard = m.Groups[2].Value,
                    RequiresThisDestroyedByBattle = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Master Monk: Nomi, Special Summoned from the hand only by Tributing 1 named monster you control (any position).
            new AtomRegistration.Atom(
                Start + NoNormalSummon + @"This card cannot be Special Summoned except by Tributing 1 " + Name + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonFromHandProcedure,
                    Amount = 0, // the engine picks the named Tribute (DuelUI reads HandProcedureOf(...).Amount)
                    TributeNamedCard = m.Groups[1].Value,
                    TributeFaceUpOnly = false,
                    ProcedureIsOnlySpecialSummon = true, // Nomi: never revived, even once properly summoned
                    FromHand = true,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),

            // Master Monk: EFFECT_EXTRA_ATTACK 1 (lost while negated).
            new AtomRegistration.Atom(
                Start + @"This card can attack twice during the same turn" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ExtraAttacks,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    MakesChainLink = false
                }),

            // Megarock Dragon: Nomi, banish 1 or more monsters of the Type from your GY; its original ATK and DEF become the
            // number banished x N.
            AtomRegistration.Atom.Sequence(
                Start + NoNormalSummon + @"This card cannot be Special Summoned except by (?:removing from play|banishing) " +
                @"an? " + Race + @"(?:-Type)? monster\(s\) (?:in|from) your " + Gy + @"\.\s*The original ATK and DEF of this " +
                @"card become the number of \1(?:-Type)? monsters you (?:removed from play|banished) when you Special " +
                @"Summoned this card x (\d+)" + End,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.SpecialSummonFromHandProcedure,
                        Amount = 0, // no Tribute (DuelUI reads HandProcedureOf(...).Amount)
                        BanishFromGyCount = 1,
                        BanishFromGyUpTo = true,
                        RaceFilter = m.Groups[1].Value,
                        FromHand = true,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.OriginalAtkDefBecomeProcedureBanishCountTimes,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 2, 0),
                        DefAmount = Parse(m, 2, 0),
                        RaceFilter = m.Groups[1].Value,
                        MakesChainLink = false
                    }
                }),
        };

        static EffectClause AllInPosition(Match m, bool attack)
        {
            var n = Parse(m, 2, 0);
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.GainSelfAtkPerCount,
                Side = EffectSide.Controller,
                NamedCard = m.Groups[1].Value,
                Amount = attack ? n : 0,
                DefAmount = attack ? 0 : n,
                CountSource = attack
                    ? TlmDerivedStatsAndProceduresRuntime.FaceUpNamedYouControlAllInAttack
                    : TlmDerivedStatsAndProceduresRuntime.FaceUpNamedYouControlAllInDefense,
                MakesChainLink = false
            };
        }

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
