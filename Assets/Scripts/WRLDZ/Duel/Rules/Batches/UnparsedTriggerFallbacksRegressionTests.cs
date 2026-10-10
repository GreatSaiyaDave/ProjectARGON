using System;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "UnparsedTriggerFallbacks": a Spell/Trap sentence with no colon ("Each time X, do Y.") lands whole in
    /// PsctGrammar.Sentence.Resolution, and every generic fallback in CardTextEffectCompiler.CompilePsctSentence (burn,
    /// return-to-hand, destroy, draw, Special Summon …) kept only the action and dropped the trigger. Dark Room of
    /// Nightmare burned 300 on activation, Poison Fangs and Spirit's Invitation lost their "each time" subject, and
    /// Card of Safe Return drew on activation, yet all four classified Implemented. Blue text that opens with
    /// "When / If / Each time / Every time" is now refused before the fallbacks run, so those cards fail closed and the
    /// deck gate keeps them out. Lead-trigger sentences owned by a whole-text atom (Mysterious Puppeteer) and flat
    /// fallbacks behind a colon (Airknight Parshath, Golem Sentry, Sage's Stone) compile exactly as before.
    /// </summary>
    public static class UnparsedTriggerFallbacksRegressionTests
    {
        const int DarkRoomOfNightmare = 85562745; // Continuous Spell: each time opp takes effect damage, burn 300
        const int PoisonFangs = 76539047;         // Continuous Spell: each time a Beast inflicts battle damage, burn 500
        const int SpiritsInvitation = 92394653;   // Continuous Trap: each time your Spirit returns to hand, bounce 1
        const int CardOfSafeReturn = 57953380;    // Continuous Spell: draw 1 when a monster is SS'd from your GY
        const int Seven = 67048711;               // "7": draw 3 when there are 3 face-up "7"
        const int TheFirstSarcophagus = 31076103; // SS "Spirit of the Pharaoh" when all 3 Sarcophagus are on the field
        const int PitchBlackPowerStone = 34029630; // destroy it when the last Spell Counter is removed
        const int GoblinFan = 4149689;            // destroy a Level 2 or lower monster when it is Flip Summoned
        const int OminousFortunetelling = 56995655; // burn 700 only if you call it right

        const int MysteriousPuppeteer = 54098121; // lead-trigger sentence owned by a whole-text atom
        const int AirknightParshath = 18036057;   // "If this card inflicts battle damage to your opponent: Draw 1 card."
        const int GolemSentry = 52323207;         // "When this card is Flip Summoned: Target …; return that target to the hand."
        const int SagesStone = 13604200;          // "If you control a face-up "Dark Magician Girl": Special Summon 1 …"
        const int Ookazi = 19523799;              // "Inflict 800 damage to your opponent."
        const int CelticGuardian = 91152256;

        static readonly Regex RxLead = new(@"^\s*(?:When|If|Each time|Every time)\b", RegexOptions.IgnoreCase);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("UnparsedTriggerFallbacks: " + name, ok, detail ?? "");

            CorpusShapes(db, Check);
            Controls(db, Check);
            NewCardRule(Check);
            ActivationRefused(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static bool Has(CompiledCardProgram prog, EffectActionKind action) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);

        /// <summary>A clause whose source sentence still opens with the trigger the fallback dropped.</summary>
        static bool HasLeadTriggerClause(CompiledCardProgram prog) =>
            prog != null && prog.ClauseList.Exists(c => c != null && RxLead.IsMatch(c.SourceSnippet ?? ""));

        static string Shape(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram CompileText(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Near-miss unsplit trigger " + id, type = type, race = race, desc = text
            });

        static EffectClause Find(CompiledCardProgram prog, EffectActionKind action) =>
            prog?.ClauseList.Find(c => c != null && c.Action == action);

        // ───────────────────────────── checks ─────────────────────────────

        static void CorpusShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            // The four cards that classified Implemented with the trigger dropped. Each loses the fallback action.
            foreach (var (id, label, dropped) in new[]
                     {
                         (PoisonFangs, "Poison Fangs", EffectActionKind.InflictDamageToOpponent),
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label}: not FullyCompiled and no bare {dropped} clause",
                    def != null && prog != null && !prog.FullyCompiled && !Has(prog, dropped) &&
                    !HasLeadTriggerClause(prog),
                    Shape(prog));
                var status = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check($"{label}: fails closed (not Implemented) and the deck gate keeps it out",
                    def != null && status != CardEffectStatusKind.Implemented &&
                    status != CardEffectStatusKind.Structural && !CardEffectStatus.MayIncludeInDeck(def),
                    status.ToString());
            }

            // EffectDamageEvents batch (v162): Dark Room of Nightmare's whole sentence is the "each time your opponent takes
            // damage from a card effect" trigger (a whole-text atom), never the bare activation burn the fallback produced.
            {
                var def = db.Get(DarkRoomOfNightmare);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Dark Room of Nightmare: compiled whole as its trigger, no bare InflictDamageToOpponent clause",
                    prog != null && prog.FullyCompiled && !Has(prog, EffectActionKind.InflictDamageToOpponent) &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.InflictDamageEachTimeOpponentTakesEffectDamage &&
                    prog.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp,
                    Shape(prog));
            }

            // NegationsAndGySummons batch (v159): Card of Safe Return's whole sentence is now a draw trigger (a
            // whole-sentence atom), never the bare activation Draw the fallback produced.
            {
                var def = db.Get(CardOfSafeReturn);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Card of Safe Return: no bare Draw clause — the trigger is compiled whole (NegationsAndGySummons)",
                    prog != null && prog.FullyCompiled && !Has(prog, EffectActionKind.Draw) &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy &&
                    prog.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp,
                    Shape(prog));
            }

            // ContinuousTrapsAndSpiritSupport batch (v161): Spirit's Invitation's "Each time" sentence is now a whole-sentence
            // atom (the trigger is kept, never a bare activation ReturnToHand), next to its Standby 500 LP maintenance.
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(SpiritsInvitation));
                var pay = Find(prog, EffectActionKind.PayLpOrDestroyThis);
                var trigger = Find(prog, EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand);
                Check("Spirit's Invitation: the trigger compiled whole (ContinuousTrapsAndSpiritSupport) + the 500 LP upkeep",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && pay != null &&
                    pay.Timing == EffectTiming.StandbyPhase && pay.PayLpAmount == 500 && trigger != null &&
                    trigger.Timing == EffectTiming.ContinuousWhileFaceUp && !Has(prog, EffectActionKind.ReturnToHand),
                    Shape(prog));
            }

            // ContinuousTrapsAndSpiritSupport batch (v161): Ominous Fortunetelling compiles whole (a called random card), never
            // a bare burn from its "If you call it right" sentence.
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(OminousFortunetelling));
                Check("Ominous Fortunetelling: one Standby call-and-burn clause, no bare burn (ContinuousTrapsAndSpiritSupport)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.CallTypeOfRandomOpponentHandCardBurn &&
                    prog.ClauseList[0].Timing == EffectTiming.StandbyPhase &&
                    !Has(prog, EffectActionKind.InflictDamageToOpponent),
                    Shape(prog));
            }

            // Stubs that went through the same fallbacks: the trigger sentence no longer compiles to a bare action.
            foreach (var (id, label) in new[]
                     {
                         (Seven, "\"7\""), (TheFirstSarcophagus, "The First Sarcophagus"),
                         (PitchBlackPowerStone, "Pitch-Black Power Stone"), (GoblinFan, "Goblin Fan"),
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label}: no clause compiled from its unsplit \"When / If / Each time\" sentence",
                    def != null && prog != null && !prog.FullyCompiled && !HasLeadTriggerClause(prog) &&
                    !CardEffectStatus.MayIncludeInDeck(def),
                    Shape(prog));
            }
        }

        /// <summary>Cards the guard must not touch.</summary>
        static void Controls(CardDatabase db, Action<string, bool, string> Check)
        {
            {
                // A lead-trigger sentence that a whole-text atom owns before the PSCT split.
                var def = db.Get(MysteriousPuppeteer);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var c = Find(prog, EffectActionKind.GainLpWhenEitherPlayerNormalOrFlipSummons);
                Check("Mysterious Puppeteer: still FullyCompiled by its whole-text atom",
                    prog != null && prog.FullyCompiled && c != null &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Amount == 500 &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Shape(prog));
            }
            {
                var def = db.Get(AirknightParshath);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var d = Find(prog, EffectActionKind.Draw);
                Check("Airknight Parshath: colon-conditioned Draw 1 still compiles",
                    prog != null && prog.FullyCompiled && d != null &&
                    d.Timing == EffectTiming.ThisCardInflictsBattleDamage && d.Amount == 1 &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Shape(prog));
            }
            {
                var def = db.Get(GolemSentry);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var r = Find(prog, EffectActionKind.ReturnToHand);
                Check("Golem Sentry: colon-conditioned ReturnToHand still compiles",
                    prog != null && prog.FullyCompiled && r != null &&
                    r.Timing == EffectTiming.ThisCardSummoned && r.RequiresTargetChoice &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Shape(prog));
            }
            {
                var def = db.Get(SagesStone);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var s = Find(prog, EffectActionKind.SpecialSummonNamed);
                Check("Sage's Stone: colon-conditioned SpecialSummonNamed still compiles",
                    prog != null && prog.FullyCompiled && s != null && s.NamedCard == "Dark Magician" &&
                    s.RequiresFaceUpName == "Dark Magician Girl" &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Shape(prog));
            }
            {
                var def = db.Get(Ookazi);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var b = Find(prog, EffectActionKind.InflictDamageToOpponent);
                Check("Ookazi: flat burn still compiles",
                    prog != null && prog.FullyCompiled && b != null && b.Timing == EffectTiming.Activate &&
                    b.Amount == 800,
                    Shape(prog));
            }
        }

        /// <summary>
        /// The rule for new cards: no generic Resolution fallback compiles a sentence whose blue text still opens
        /// with its trigger or condition.
        /// </summary>
        static void NewCardRule(Action<string, bool, string> Check)
        {
            var nearMisses = new[]
            {
                ("Spell Card", "Continuous", "Each time a monster is destroyed, inflict 300 damage to your opponent.",
                    EffectActionKind.InflictDamageToOpponent),
                ("Trap Card", "Continuous", "Each time your opponent Normal Summons a monster, return it to its owner's hand.",
                    EffectActionKind.ReturnToHand),
                ("Trap Card", "Continuous", "When a monster is Flip Summoned, destroy it.",
                    EffectActionKind.Destroy),
                ("Spell Card", "Continuous", "Every time a Spell Card is activated, draw 1 card.",
                    EffectActionKind.Draw),
                ("Spell Card", "Continuous",
                    "When you control 3 face-up \"Sarcophagus\" cards, Special Summon 1 \"Spirit of the Pharaoh\" from your hand or Deck.",
                    EffectActionKind.SpecialSummonNamed),
                ("Spell Card", "Continuous", "If a Spell Card is activated, inflict 300 damage to your opponent.",
                    EffectActionKind.InflictDamageToOpponent),
                // The whole-text burn takes the tail here and leaves "If you control no monsters," unparsed: still
                // fail-closed, just not through this guard.
                ("Spell Card", "Normal", "If you control no monsters, inflict 1000 damage to your opponent.",
                    EffectActionKind.InflictDamageToOpponent),
                ("Spell Card", "Normal", "Target 1 monster on the field; if it is a Dragon monster, destroy it.",
                    EffectActionKind.Destroy),
            };
            var i = 0;
            foreach (var (type, race, text, action) in nearMisses)
            {
                var prog = CompileText(990601 + i++, type, race, text);
                Check($"near miss ({race} {type}) \"{text}\" is not FullyCompiled and no {action} clause keeps its trigger",
                    prog != null && !prog.FullyCompiled && !HasLeadTriggerClause(prog),
                    Shape(prog));
            }

            var controls = new[]
            {
                ("Spell Card", "Normal", "Inflict 1000 damage to your opponent.", EffectActionKind.InflictDamageToOpponent),
                ("Spell Card", "Normal", "Target 1 monster on the field; destroy it.", EffectActionKind.Destroy),
                ("Trap Card", "Normal", "Target 1 monster on the field; return that target to the hand.",
                    EffectActionKind.ReturnToHand),
            };
            foreach (var (type, race, text, action) in controls)
            {
                var prog = CompileText(990601 + i++, type, race, text);
                Check($"control ({race} {type}) \"{text}\" still compiles to {action}",
                    prog != null && prog.FullyCompiled && Has(prog, action),
                    Shape(prog));
            }
        }

        /// <summary>In a duel the three cards can no longer be activated, and trying does nothing.</summary>
        static void ActivationRefused(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var (id, label) in new[] { (PoisonFangs, "Poison Fangs") })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var oppLp = o.LifePoints;
                var myLp = p.LifePoints;
                var card = PutInHand(engine, p, id);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var tried = engine.TryActivateSpellTrap(p, card, fromHand: true);
                DrainCombat(engine);
                Check($"{label}: activation from the hand is refused and inflicts no damage",
                    !can && !tried && p.Hand.Contains(card) && !p.TryFindSpellTrap(card, out _) &&
                    o.LifePoints == oppLp && p.LifePoints == myLp,
                    $"can={can} tried={tried} hand={p.Hand.Count} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                // EffectDamageEvents batch (v162): Dark Room of Nightmare is Implemented — its activation only places it; the
                // burn waits for the opponent to take effect damage.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var oppLp = o.LifePoints;
                var myLp = p.LifePoints;
                var card = PutInHand(engine, p, DarkRoomOfNightmare);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var tried = engine.TryActivateSpellTrap(p, card, fromHand: true);
                DrainCombat(engine);
                Check("Dark Room of Nightmare: activation from the hand places it face-up and inflicts no damage",
                    can && tried && p.TryFindSpellTrap(card, out _) && card.FaceUp && o.LifePoints == oppLp &&
                    p.LifePoints == myLp,
                    $"can={can} tried={tried} faceUp={card.FaceUp} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var theirs = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, faceUp: true);
                var set = PlaceSetTrap(engine, p, SpiritsInvitation, 0);
                var can = engine.CanActivateSpellTrap(p, set, fromHand: false);
                var tried = engine.TryActivateSpellTrap(p, set, fromHand: false);
                DrainCombat(engine);
                // ContinuousTrapsAndSpiritSupport batch (v161): the card is Implemented — its activation only places it; the
                // bounce waits for one of your face-up Spirits to return to your hand.
                Check("Spirit's Invitation: a Set copy activates face-up and returns nothing on activation",
                    can && tried && set.FaceUp && o.MonsterZones[0].Occupant == theirs,
                    $"can={can} tried={tried} faceUp={set.FaceUp} oppZone0={o.MonsterZones[0].Occupant?.Name ?? "empty"}");
            }

            {
                // Positive control: the same helpers do activate a flat burn Spell.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var oppLp = o.LifePoints;
                var card = PutInHand(engine, p, Ookazi);
                var ok = engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                         engine.TryActivateSpellTrap(p, card, fromHand: true);
                DrainCombat(engine);
                Check("Ookazi: activation from the hand still inflicts 800",
                    ok && o.LifePoints == oppLp - 800,
                    $"ok={ok} oppLp={o.LifePoints} was {oppLp}");
            }
        }
    }
}
