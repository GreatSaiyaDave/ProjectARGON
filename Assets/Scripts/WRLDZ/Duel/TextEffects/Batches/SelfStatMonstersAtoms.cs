using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SelfStatMonsters (v153). Shared sentence shapes, not a card-name list.
    /// The "Must first be Special Summoned by banishing N ATTRIBUTE" procedure is not an atom: Gigantes'
    /// test treats that nomi line as a summon restriction, and an Activate clause there fails the suite.
    /// "If this card destroys a monster by battle: This card loses N ATK" is not compiled. The attacker
    /// path already fires; the defender-destroyed-the-attacker path is a DuelEngine block, not the
    /// existing one-line direct-attack gate, so that sentence stays unparsed (not FullyCompiled).
    /// "If this card/monster is Normal Summoned, destroy this card" stays on
    /// <see cref="ClassicEraTemplates"/> (this card or this monster).
    /// Runtime: <see cref="SelfStatMonstersRuntime"/> for the direct-attack lock and the per-monster count.
    /// </summary>
    public static class SelfStatMonstersAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";

        /// <summary>The seven printed Attributes. A clause filter for one of these words is an ATTRIBUTE filter, not
        /// a monster Type — "WATER" in a RaceFilter matches no card.</summary>
        const string Attr = @"(DARK|LIGHT|WATER|FIRE|EARTH|WIND|DIVINE)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Nightmare Penguin (c81306586) e1: SINGLE RANGE EFFECT_UPDATE_ATTACK on every face-up WATER monster the
            // controller controls (aux.Stringid 0 is the FLIP effect). "face-up" is explicit here, and the word is
            // an ATTRIBUTE, so it goes in AttributeFilter — a RaceFilter of "WATER" matches nothing. The compiler's
            // RxAllYouControlGainAtk only accepts a single word ("All Machines you control gain…"), so this
            // "face-up <ATTRIBUTE>" phrasing is not covered there.
            new AtomRegistration.Atom(
                Start + @"All face-up " + Attr + @" monsters you control gain (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    AttributeFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 0),
                    DefAmount = 0,
                    MakesChainLink = false
                }),
            // This card only, opponent's Battle Phase (The Rock Spirit). Banner of Courage is every monster you
            // control during your Battle Phase; AffectsThisCardOnly keeps those apart.
            new AtomRegistration.Atom(
                Start + @"Increase the ATK of this (?:monster|card) by (\d+) points during your opponent's Battle Phase" +
                End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 300),
                    DefAmount = 0,
                    AffectsThisCardOnly = true,
                    RequiresBattlePhase = true,
                    OpponentTurnOnly = true,
                    MakesChainLink = false
                }),

            // Spirit of Flames' "This card gains N ATK during your Battle Phase only." lives in EngineHygieneAtoms (v160),
            // with the banish procedure it needs.

            // Opponent's monsters, their Battle Phase (Soul of Purity and Light). OpponentIsSubject so it is not
            // both fields.
            new AtomRegistration.Atom(
                Start + @"All monsters your opponent controls lose (\d+) ATK during their Battle Phase only" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Opponent,
                    OpponentIsSubject = true,
                    Amount = -Parse(m, 1, 300),
                    DefAmount = 0,
                    RequiresBattlePhase = true,
                    OpponentTurnOnly = true,
                    MakesChainLink = false
                }),

            // Per monster the opponent controls, face-down included. Not Boar Soldier's flat "1 or more".
            new AtomRegistration.Atom(
                Start + @"If your opponent controls any monsters?, decrease the ATK of this card by (\d+) points? " +
                @"for each monster on your opponent'?s side of the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = -Parse(m, 1, 200),
                    DefAmount = 0,
                    CountSource = SelfStatMonstersRuntime.OppMonsterCount,
                    MakesChainLink = false
                }),

            // Direct-attack lock. The ATK-loss sentence of the same cards is intentionally not an atom.
            new AtomRegistration.Atom(
                Start + @"This card cannot attack your opponent directly" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotAttackDirectly,
                    MakesChainLink = false
                }),
            // — 89621922 Neo-Spacian Flare Scarab: Gains 400 ATK for each Spell/Trap your opponent controls.
            new AtomRegistration.Atom(
                Start + @"Gains (\d+) ATK for each Spell/Trap your opponent controls" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    DefAmount = 0,
                    CountSource = TextEffects.PerCountBurnsRuntime.OppSpellTrapCards,
                    MakesChainLink = false
                }),
            // — 89222931 Machine King Prototype: This card gains 100 ATK and DEF for each Machine-Type monster on the field, except this card.
            new AtomRegistration.Atom(
                Start + @"This card gains (\d+) ATK and DEF for each ([A-Za-z-]+)(?:-Type)? monster on the field, except this card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    DefAmount = int.Parse(m.Groups[1].Value),
                    RaceFilter = m.Groups[2].Value,
                    CountSource = AttackShieldsAndDamageZeroRuntime.FaceUpRaceOnFieldExceptThis,
                    MakesChainLink = false
                }),
            // — 27408609 Golden Homunculus: This card gains 300 ATK and DEF for each of your banished cards.
            new AtomRegistration.Atom(
                Start + @"This card gains (\d+) ATK and DEF for each of your banished cards" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    DefAmount = int.Parse(m.Groups[1].Value),
                    CountSource = DerivedAtkMonstersRuntime.YourBanishedCards,
                    MakesChainLink = false
                }),
            // — 23927545 Jurrac Protops: This card gains 100 ATK for each monster your opponent controls.
            new AtomRegistration.Atom(
                Start + @"This card gains (\d+) ATK for each monster your opponent controls" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    DefAmount = 0,
                    CountSource = SelfStatMonstersRuntime.OppMonsterCount,
                    MakesChainLink = false
                }),
            // — 6285791: This card can attack your opponent directly.
            new AtomRegistration.Atom(
                Start + @"This card can attack your opponent directly" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CanAttackDirectly,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // — 18724123 Infernity Archer: If you have no cards in your hand, this card can attack your opponent directly.
            new AtomRegistration.Atom(
                Start + @"If you have no cards in your hand, this card can attack your opponent directly" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CanAttackDirectly,
                    Side = EffectSide.Controller,
                    WhileCondition = CrvEmptyHandAndAurasRuntime.NoCardsInHand,
                    MakesChainLink = false
                }),
            // — 52222372 Genex Turbine: All face-up "Genex" monsters you control gain 400 ATK.
            new AtomRegistration.Atom(
                Start + @"All face-up ""([^""]+)"" monsters you control gain (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    Amount = int.Parse(m.Groups[2].Value),
                    DefAmount = 0,
                    MakesChainLink = false
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
