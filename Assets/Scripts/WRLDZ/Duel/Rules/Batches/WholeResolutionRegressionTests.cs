using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "WholeResolution" (fail-closed audit, 2026-09-29). The generic PSCT Activate path in
    /// CardTextEffectCompiler.CompilePsctSentence picked its action with unanchored regexes, so a sentence compiled
    /// to one phrase inside it: "Either player can pay 1000 Life Points to negate the Normal Summon of a monster, and
    /// destroy it." became FullyCompiled PayLp → Destroy. Its "it" / "that target" arms also invented a target zone
    /// (any monster, any card, either GY) when the red text named a target the parser did not understand.
    ///
    /// Now every simple fallback must be the whole resolution, an unnamed target returns null, an "Each time …:"
    /// condition is not a one-shot activation, an unparsed Spell Counter cost refuses the sentence, and ", even
    /// during the Damage Step" is not read as the damage-calculation window. At v151, 16 Implemented cards fail closed
    /// (the printed text says more than the program did) and 16 Stubs lose their only clause; the other cards in the
    /// corpus below had already failed closed or been implemented by their own atoms. Correctly compiled cards on
    /// the same path keep their shapes.
    /// </summary>
    public static class WholeResolutionRegressionTests
    {
        const int Celtic = 91152256;       // Celtic Guardian 1400/1200, Normal Monster
        const int Bewd = 89631139;         // Blue-Eyes White Dragon: LIGHT, and discard fodder
        const int CallOfTheHaunted = 97077563; // a Continuous Trap to leave face-up for Remove Trap

        const int RemoveTrap = 51482758;
        const int SolarRay = 44472639;
        const int CoffinSeller = 65830223;
        const int DarkCore = 70231910;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("WholeResolution: " + name, ok, detail ?? "");

            void Guard(string name, Action body)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    check("WholeResolution: " + name + " (threw)", false,
                        ex.GetType().Name + ": " + ex.Message + " @ " + ex.StackTrace);
                }
            }

            Guard("fail-closed corpus", () => FailClosedCorpus(db, Check));
            Guard("kept corpus", () => KeptCorpus(db, Check));
            Guard("new-card rule", () => NewCardRule(Check));
            Guard("duels", () => Duels(db, pDeck, aDeck, Check));
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Synthetic(int id, string type, string race, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "WholeResolution Probe " + id, type = type, race = race,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                desc = desc
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> pred) =>
            p != null && p.ClauseList.Any(c => c != null && pred(c));

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "<null>"
                : $"full={prog.FullyCompiled} clauses=[" +
                  string.Join("; ", prog.ClauseList.Where(c => c != null).Select(c =>
                      $"{c.Timing}/{c.Action}/{c.Zone}/{c.Side} amt={c.Amount} lp={c.PayLpAmount} " +
                      $"tgt={c.RequiresTargetChoice}")) +
                  $"] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        // ───────────────────────────── corpus ─────────────────────────────

        /// <summary>
        /// Each card compiled one phrase of a longer sentence (or a guessed target). The wrong clause must be gone.
        /// <c>gated</c>: nobody else implements the card, so it must also fail closed and stay out of decks. The
        /// others have their own whole-sentence atoms queued or landed (Acid Trap Hole FlipDefCheck, Graceful Charity
        /// DrawThenDiscard, Card of Safe Return, D.D. Scout Plane BanishedReturn), so only the wrong shape is checked.
        /// </summary>
        static void FailClosedCorpus(CardDatabase db, Action<string, bool, string> Check)
        {
            var rows = new (int id, string label, string why, Func<EffectClause, bool> wrong, bool gated)[]
            {
                (41356845, "Acid Trap Hole", "flip, then destroy only if DEF <= 2000, else back face-down",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldAnyMonster, false),
                // SpellCounterSpenders batch: Implemented whole (NegateActivation + the side-wide counter cost); a Destroy
                // clause is still the wrong shape; gate off.
                (53112492, "Anti-Spell", "Spell Counter cost + negate a Spell activation",
                    c => c.Action == EffectActionKind.Destroy, false),
                (79649195, "Armor Break", "negate an Equip Spell activation",
                    c => c.Action == EffectActionKind.Destroy, true),
                (57953380, "Card of Safe Return", "a draw trigger, not a draw on activation",
                    c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Draw, false),
                (79853073, "Cipher Soldier", "vs a Warrior-Type, during that damage calculation only",
                    c => c.Action == EffectActionKind.ApplyLingeringAtkDef, true),
                // EffectDamageEvents batch (v162): Implemented as one whole-text trigger clause — the wrong clause is a burn
                // on activation; gate off.
                (65830223, "Coffin Seller", "burn each time a monster goes to the opponent's GY",
                    c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.InflictDamageToOpponent,
                    false),
                // TargetNegationsAndHandQuickEffects batch (v163): Implemented as one whole-text Counter Trap clause (the
                // answered activation must target and destroy exactly 1 Spell/Trap); a Destroy clause, or a negation without
                // that condition, is the wrong shape; gate off.
                (2926176, "Curse of Royal", "negate a Spell/Trap that destroys a Spell/Trap",
                    c => c.Action == EffectActionKind.Destroy ||
                         (c.Action == EffectActionKind.NegateActivation &&
                          c.ActivationCondition != TargetNegationsAndHandQuickEffectsRuntime.LinkTargetsOneSpellTrapToDestroy),
                    false),
                (3773196, "D.D. Scout Plane", "\"it\" is this banished card, not a GY monster",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy &&
                         c.Zone == EffectZoneFilter.EitherGyMonsters, false),
                // EffectDamageEvents batch (v162): Implemented as one whole-text trigger clause; gate off.
                (85562745, "Dark Room of Nightmare", "burn each time the opponent takes effect damage",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent, false),
                // FlipCostsAndFusion batch: Implemented — the burn carries its cost (2 random hand cards banished).
                (27132350, "Fire Sorcerer", "banish 2 random hand cards to burn",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent && c.CostBanishRandomHandCards != 2, false),
                (79571449, "Graceful Charity", "draw 3, then discard 2",
                    c => c.Action == EffectActionKind.Draw, false),
                (60229110, "Granmarg the Rock Monarch", "targets a Set card, not any monster",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldAnyMonster, true),
                // StandbyBurnsAndDice batch (v162): Implemented — the burn counts face-up banished monsters, never flat.
                (33737664, "Graverobber's Retribution", "100 for each banished opponent's monster",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent &&
                         c.CountSource != PerCountBurnsRuntime.OppBanishedFaceUpMonsters, false),
                (23265594, "Heavy Mech Support Platform", "Union equip / unequip, not a GY summon",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy, false),
                // PhaseTriggersAndIgnitionCosts batch: Implemented as one face-up field ignition whose cost is the Tribute;
                // a burn on any other timing, or without the Tribute cost, is the wrong clause; gate off.
                (34906152, "Mass Driver", "Tribute 1 monster to burn 400",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent &&
                         (c.Timing != EffectTiming.FaceUpFieldIgnition || c.RequiresTributeCount != 1), false),
                // Since v159 the BattleWindows atom compiles Michizure whole; no generic (condition-less) Destroy may appear.
                (37580756, "Michizure", "trigger: a monster sent from the field to your GY",
                    c => c.Timing == EffectTiming.DamageCalculation ||
                         (c.Action == EffectActionKind.Destroy &&
                          c.ActivationCondition != BattleWindowsRuntime.MonsterSentFromFieldToYourGy), false),
                // SpellCounterSpenders batch: Implemented whole — a GY summon without both names or without the 2-counter
                // cost is the wrong shape; gate off.
                (68334074, "Miracle Restoring", "Spell Counter cost; \"Dark Magician\" or \"Buster Blader\" only",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy &&
                         (c.NamedCard != "Dark Magician" || c.AltNamedCard != "Buster Blader" ||
                          c.RemoveSpellCountersOnYourSide != 2), false),
                // TargetedClassics Soft: FullyCompiled Implemented — wrong-clause was any Destroy; gate off.
                (71044499, "Nobleman of Crossout", "face-down; destroy+banish; purge Decks if Flip",
                    c => c.Action == EffectActionKind.Destroy &&
                         (c.Zone != EffectZoneFilter.FieldFaceDownMonsters || !c.BanishIfDestroyed ||
                          !c.PurgeDecksIfFlipMonster), false),
                (76539047, "Poison Fangs", "burn each time a Beast-Type monster inflicts battle damage",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent, true),
                // SimpleSpellTraps batch (v160 LOD): Implemented with the two-Type filter; an unfiltered clause is wrong.
                (31785398, "Ready for Intercepting", "Warrior or Spellcaster only",
                    c => c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                         c.Zone == EffectZoneFilter.FieldAnyMonster && c.RaceFilter != "Warrior|Spellcaster", false),
                // TargetedClassics Soft: FullyCompiled Implemented — wrong-clause was FieldAnyMonster Destroy; gate off.
                (51482758, "Remove Trap", "a face-up Trap Card, not a monster",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldAnyMonster, false),
                // BattleDestroyedGyRecovery batch (v162): Implemented with their own whole-sentence clauses (the flag and
                // the Standby Summon of this card; the Summon of 1 of those monsters with its 800 ATK) — the generic
                // GY Special Summon is still the wrong clause; gate off.
                (31709826, "Revival Jam", "\"it\" is this card, next Standby Phase",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy, false),
                (93382620, "Rope of Life", "one of those monsters, and it gains 800 ATK",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy, false),
                // IgnitionCostsAndSimpleSpells batch: Implemented whole — 600 x the face-up LIGHT monsters you control at
                // resolution (CountSource); a flat burn is still the wrong clause; gate off.
                (44472639, "Solar Ray", "600 for each face-up LIGHT monster, not a flat 600",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent &&
                         c.CountSource != IgnitionCostsAndSimpleSpellsRuntime.YourFaceUpMonstersOfConditionAttribute, false),
                // FlipCostsAndFusion batch: Implemented as one whole-sentence clause (no Destroy fragment).
                (14644902, "Summoner of Illusions", "Tribute, Fusion from the Extra Deck, destroy it in the End Phase",
                    c => c.Action == EffectActionKind.Destroy ||
                         (c.Action == EffectActionKind.SpecialSummonFusionFromExtra), false),
                // FaceUpQuickEffects batch (v160): FullyCompiled Implemented as one face-up Quick Effect clause — the
                // wrong clause is a burn on any other timing (the activation itself); gate off.
                (21237481, "Type Zero Magic Crusher", "discard a Spell Card to burn 500",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent &&
                         c.Timing != EffectTiming.FaceUpQuickEffect, false),
                (1571945, "White Ninja", "a Defense Position monster, not any monster",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldAnyMonster, true),
                (64500000, "Z-Metal Tank", "Union equip / unequip, not a GY summon",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy, false),
            };

            foreach (var (id, label, why, wrong, gated) in rows)
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label}: no clause from a partial match ({why})",
                    def != null && prog != null && !Has(prog, wrong), Describe(prog));
                if (!gated || def == null) continue;
                var status = CardEffectStatus.Classify(def);
                Check($"{label}: fails closed and the deck gate keeps it out",
                    !prog.FullyCompiled && status != CardEffectStatusKind.Implemented &&
                    status != CardEffectStatusKind.Structural && !CardEffectStatus.MayIncludeInDeck(def),
                    status + " " + Describe(prog));
            }

            // Spirit's Invitation: the generic fallback's "return any card" clause stays refused; since the
            // ContinuousTrapsAndSpiritSupport batch (v161) its trigger sentence is a whole-sentence atom instead.
            const int spiritsInvitation = 92394653;
            var si = db.Get(spiritsInvitation);
            var siProg = si != null ? CardTextEffectCompiler.Compile(si) : null;
            Check("Spirit's Invitation: no \"return any card\" clause; the trigger is compiled whole",
                si != null && siProg != null && siProg.FullyCompiled &&
                !Has(siProg, c => c.Action == EffectActionKind.ReturnToHand) &&
                Has(siProg, c => c.Action == EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand),
                Describe(siProg));

            // Stubs whose only clause was the same fallback; they no longer carry it (out of decks either way).
            // Asura Priest ("Return it" = this card) is Implemented since the SpiritMonsters batch (attack all): the
            // partial "return any card on the field" clause must still be gone.
            var asura = db.Get(2134346);
            var asuraProg = asura != null ? CardTextEffectCompiler.Compile(asura) : null;
            Check("Asura Priest (\"Return it\" = this card): no \"return a card on the field\" clause",
                asura != null && asuraProg != null &&
                !Has(asuraProg, c => c.Action == EffectActionKind.ReturnToHand && c.Zone == EffectZoneFilter.AnyCardOnField) &&
                asuraProg.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ReturnToHand &&
                                                 c.RequiresSummonedOrFlippedThisTurn && !c.RequiresTargetChoice),
                Describe(asuraProg));

            var stubs = new (int id, string label, Func<EffectClause, bool> wrong)[]
            {
                (4149689, "Goblin Fan (destroy the Flip Summoned monster)", c => c.Action == EffectActionKind.Destroy),
                (21051146, "Blast Magician (Spell Counter cost)", c => c.Action == EffectActionKind.Destroy),
                (29267084, "Shadow Spell (cannot attack or change position)",
                    c => c.Action == EffectActionKind.ChangeBattlePosition),
                (49328340, "Spiral Spear Strike (draw 2, then discard 1)", c => c.Action == EffectActionKind.Draw),
            };
            // Dramatic Rescue left this list in the ChainLinkAnswers batch: it compiles whole (return the targeted
            // "Amazoness" monster, then Special Summon from the hand; ChainLinkAnswersRegressionTests drive it) and still
            // carries no generic Special Summon fragment.
            var rescue = db.Get(80193355);
            var rescueProg = rescue != null ? CardTextEffectCompiler.Compile(rescue) : null;
            Check("Dramatic Rescue: compiled whole, with no partial \"Special Summon from hand\" clause",
                rescueProg != null && rescueProg.FullyCompiled && rescueProg.ClauseList.Count == 1 &&
                rescueProg.ClauseList[0].Action == EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand &&
                !Has(rescueProg, c => c.Action == EffectActionKind.SpecialSummonFromHand),
                Describe(rescueProg));
            foreach (var (id, label, wrong) in stubs)
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label}: the partial clause is gone and the card stays out of decks",
                    def != null && prog != null && !Has(prog, wrong) && !CardEffectStatus.MayIncludeInDeck(def),
                    Describe(prog));
            }
        }

        /// <summary>Cards on the same path whose whole sentence was the matched phrase keep their programs.</summary>
        static void KeptCorpus(CardDatabase db, Action<string, bool, string> Check)
        {
            var rows = new (int id, string label, Func<EffectClause, bool> shape)[]
            {
                (51945556, "Zaborg the Thunder Monarch: Tribute Summon → destroy 1 monster on the field",
                    c => c.Timing == EffectTiming.ThisCardSummoned && c.Action == EffectActionKind.Destroy &&
                         c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetChoice),
                (4178474, "Raigeki Break: discard 1 → destroy 1 card on the field",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.AnyCardOnField &&
                         c.RequiresDiscardCost),
                (79759861, "Tribute to the Doomed: discard 1 → destroy 1 monster",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldAnyMonster &&
                         c.RequiresDiscardCost),
                (41872150, "Swarm of Locusts: Flip Summon → destroy an opponent's Spell/Trap",
                    c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldSpellTraps),
                (52323207, "Golem Sentry: Flip Summon → return an opponent's monster to the hand",
                    c => c.Action == EffectActionKind.ReturnToHand && c.RequiresThisFlipSummoned),
                (18036057, "Airknight Parshath: battle damage → draw 1",
                    c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage && c.Action == EffectActionKind.Draw &&
                         c.Amount == 1),
                (40320754, "Lord Poison: Special Summon a Plant from your GY",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy &&
                         c.Zone == EffectZoneFilter.ControllerGyMonsters && c.RaceFilter == "Plant"),
                (30450531, "Rite of Spirit: Special Summon a \"Gravekeeper's\" monster from your GY",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy && c.NamedCard == "Gravekeeper's"),
                (70231910, "Dark Core: discard 1 → banish a face-up monster",
                    c => c.Action == EffectActionKind.Banish && c.RequiresDiscardCost),
                (40659562, "Guardian Sphinx: return all the opponent's monsters",
                    c => c.Action == EffectActionKind.ReturnToHand && c.Zone == EffectZoneFilter.FieldMonsters &&
                         !c.RequiresTargetChoice),
                (13604200, "Sage's Stone: Special Summon \"Dark Magician\" from hand or Deck",
                    c => c.Action == EffectActionKind.SpecialSummonNamed && c.NamedCard == "Dark Magician" &&
                         c.FromHand && c.FromDeck),
                (74848038, "Monster Reincarnation: discard 1 → add a GY monster",
                    c => c.Action == EffectActionKind.AddFromGyToHand && c.RequiresDiscardCost),
                (13626450, "Malice Dispersion: destroy all face-up Continuous Traps",
                    c => c.Action == EffectActionKind.Destroy && c.FaceUpContinuousTrapsOnly),
                (2314238, "Dark Magic Attack: destroy all the opponent's Spells and Traps",
                    c => c.Action == EffectActionKind.DestroyAllOppSpellTraps),
                // TargetedClassics Soft: positive shapes (were fail-closed at v151).
                (71044499, "Nobleman of Crossout: face-down Destroy+Banish; purge Decks if Flip",
                    c => c.Action == EffectActionKind.Destroy &&
                         c.Zone == EffectZoneFilter.FieldFaceDownMonsters && c.BanishIfDestroyed &&
                         c.PurgeDecksIfFlipMonster),
                (51482758, "Remove Trap: Destroy face-up Trap on FieldSpellTraps",
                    c => c.Action == EffectActionKind.Destroy &&
                         c.Zone == EffectZoneFilter.FieldSpellTraps &&
                         c.RequiresTargetFaceUp && c.TargetCardKind == "Trap"),
            };
            foreach (var (id, label, shape) in rows)
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label}: still FullyCompiled and Implemented",
                    def != null && prog != null && prog.FullyCompiled && Has(prog, shape) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            // Old wording is the same effect: "…damage to your opponent's Life Points." still compiles.
            const int blastingTheRuins = 21466326;
            var br = CardTextEffectCompiler.Compile(db.Get(blastingTheRuins));
            Check("Blasting the Ruins: \"Inflict 3000 points of damage to your opponent's Life Points.\" still compiles",
                Has(br, c => c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 3000),
                Describe(br));
        }

        // ───────────────────────────── new cards ─────────────────────────────

        static void NewCardRule(Action<string, bool, string> Check)
        {
            // The finding that started the audit.
            var trap = Synthetic(990501, "Trap Card", "Continuous",
                "Either player can pay 1000 Life Points to negate the Normal Summon of a monster, and destroy it.");
            Check("\"Either player can pay 1000 Life Points to negate the Normal Summon of a monster, and destroy it.\" " +
                  "is not compiled (no PayLp → Destroy clause)",
                trap != null && !trap.FullyCompiled &&
                !Has(trap, c => c.Action == EffectActionKind.Destroy) && !Has(trap, c => c.PayLpAmount > 0),
                Describe(trap));

            // wrong == null: only "not FullyCompiled" is asserted. The span-based LegacyTextTemplates.RxInflictOpp may
            // still keep the flat "Inflict N …" part as a clause, but it leaves "for each …" unparsed, so the card
            // cannot reach a deck.
            var misses = new (string type, string race, string text, Func<EffectClause, bool> wrong)[]
            {
                ("Trap Card", "Normal",
                    "Target 1 monster on the field; destroy it, and if you do, inflict 500 damage to your opponent.",
                    c => c.Action == EffectActionKind.Destroy),
                ("Spell Card", "Normal", "Target 1 Set card on the field; destroy that target.",
                    c => c.Action == EffectActionKind.Destroy),
                ("Spell Card", "Normal", "Target 1 Defense Position monster on the field; destroy that target.",
                    c => c.Action == EffectActionKind.Destroy),
                ("Spell Card", "Normal",
                    "Target 1 face-down monster on the field; return that target to the hand, then draw 1 card.",
                    c => c.Action == EffectActionKind.ReturnToHand || c.Action == EffectActionKind.Draw),
                ("Spell Card", "Normal",
                    "Inflict 300 points of damage to your opponent's Life Points for each Spell Card in your Graveyard.",
                    null),
                // Type Zero Magic Crusher's own text compiles whole since v160 (FaceUpQuickEffects); its longer twin must not.
                ("Trap Card", "Continuous",
                    "Discard 1 Spell Card from your hand to inflict 500 points of damage to your opponent's Life Points, " +
                    "then draw 1 card.",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent),
                ("Spell Card", "Normal", "Draw 2 cards, then discard 1 Spell Card.",
                    c => c.Action == EffectActionKind.Draw),
                ("Spell Card", "Continuous", "When your opponent Special Summons a monster, you can draw 1 card.",
                    c => c.Action == EffectActionKind.Draw),
                ("Trap Card", "Continuous", "Each time a monster is destroyed: Inflict 200 damage to your opponent.",
                    c => c.Action == EffectActionKind.InflictDamageToOpponent),
                ("Spell Card", "Normal",
                    "Remove 2 Spell Counters from your side of the field, then target 1 monster on the field; destroy that target.",
                    c => c.Action == EffectActionKind.Destroy),
                ("Trap Card", "Continuous",
                    "Target 1 monster your opponent controls; it cannot attack or change its battle position.",
                    c => c.Action == EffectActionKind.ChangeBattlePosition),
                ("Trap Card", "Normal",
                    "Return 1 monster you control to the hand and Special Summon 1 monster from your hand.",
                    c => c.Action == EffectActionKind.SpecialSummonFromHand),
                // Michizure's own text compiles whole since v159 (BattleWindows); its opponent's-GY twin must not.
                ("Trap Card", "Normal",
                    "When a monster is sent from the field to your opponent's GY, even during the Damage Step: Target 1 monster on the field; destroy it.",
                    c => c.Timing == EffectTiming.DamageCalculation || c.Action == EffectActionKind.Destroy),
                ("Effect Monster", "Aqua",
                    "When this card is destroyed by battle and sent to the GY: You can pay 500 LP; Special Summon it during your next Standby Phase.",
                    c => c.Action == EffectActionKind.SpecialSummonFromGy),
                ("Effect Monster", "Warrior",
                    "If this card battles, during damage calculation: This card gains 1000 ATK and DEF during that damage calculation only.",
                    c => c.Action == EffectActionKind.ApplyLingeringAtkDef),
            };
            var i = 0;
            foreach (var (type, race, text, wrong) in misses)
            {
                var prog = Synthetic(990510 + i++, type, race, text);
                Check(wrong == null
                        ? $"near miss \"{text}\" is not FullyCompiled"
                        : $"near miss \"{text}\" compiles no partial clause",
                    prog != null && !prog.FullyCompiled && (wrong == null || !Has(prog, wrong)), Describe(prog));
            }

            // The rule keeps whole-sentence shapes.
            var destroy = Synthetic(990540, "Trap Card", "Normal", "Target 1 monster your opponent controls; destroy that target.");
            Check("\"Target 1 monster your opponent controls; destroy that target.\" → targeted Destroy",
                destroy != null && destroy.FullyCompiled &&
                Has(destroy, c => c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice &&
                                  c.Zone == EffectZoneFilter.OppMonsters),
                Describe(destroy));
            var draw = Synthetic(990541, "Spell Card", "Normal", "Draw 2 cards.");
            Check("\"Draw 2 cards.\" → Draw 2",
                draw != null && draw.FullyCompiled &&
                Has(draw, c => c.Action == EffectActionKind.Draw && c.Amount == 2 && c.Side == EffectSide.Controller),
                Describe(draw));
            var drawDeck = Synthetic(990542, "Spell Card", "Normal", "Draw 2 cards from your Deck.");
            Check("\"Draw 2 cards from your Deck.\" (old wording) → Draw 2",
                drawDeck != null && drawDeck.FullyCompiled &&
                Has(drawDeck, c => c.Action == EffectActionKind.Draw && c.Amount == 2),
                Describe(drawDeck));
            var burn = Synthetic(990543, "Spell Card", "Normal", "Inflict 800 points of damage to your opponent's Life Points.");
            Check("\"Inflict 800 points of damage to your opponent's Life Points.\" → flat 800 burn",
                burn != null && burn.FullyCompiled &&
                Has(burn, c => c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 800),
                Describe(burn));
            var revive = Synthetic(990544, "Spell Card", "Normal",
                "Target 1 monster in your GY; Special Summon it in face-up Defense Position.");
            Check("\"Target 1 monster in your GY; Special Summon it in face-up Defense Position.\" → GY summon in Defense",
                revive != null && revive.FullyCompiled &&
                Has(revive, c => c.Action == EffectActionKind.SpecialSummonFromGy &&
                                 c.Zone == EffectZoneFilter.ControllerGyMonsters && c.SummonInDefense),
                Describe(revive));
        }

        // ───────────────────────────── duels ─────────────────────────────

        static void Duels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                // Remove Trap used to destroy any monster. There is a face-up Continuous Trap and a monster here.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var monster = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var trap = PlaceSetTrap(engine, o, CallOfTheHaunted, 0);
                trap.FaceUp = true;
                var card = PutInHand(engine, p, RemoveTrap);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var tried = engine.TryActivateSpellTrap(p, card, fromHand: true);
                // TargetedClassics Soft: face-up Trap is a legal target — activates and destroys it; monster stays.
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(trap);
                Check("Remove Trap: activates, destroys the face-up Trap, monster stays",
                    can && tried && !p.Hand.Contains(card) && o.TryFindMonster(monster, out _) &&
                    !o.TryFindSpellTrap(trap, out _),
                    $"can={can} tried={tried} monsterOnField={o.TryFindMonster(monster, out _)} " +
                    $"trapOnField={o.TryFindSpellTrap(trap, out _)}");
            }

            foreach (var (id, label) in new[] { (SolarRay, "Solar Ray") })
            {
                // It used to burn a flat amount the moment it was activated. Since the IgnitionCostsAndSimpleSpells batch it
                // counts: 2 face-up LIGHT monsters you control burn 1200, not a flat 600.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true); // a face-up LIGHT monster
                PlaceMonster(engine, p, Bewd, 1, BattlePosition.Attack, true); // a second one
                var set = PlaceSetTrap(engine, p, id, 0);
                var lp = o.LifePoints;
                var can = engine.CanActivateSpellTrap(p, set, fromHand: false);
                var tried = engine.TryActivateSpellTrap(p, set, fromHand: false);
                DrainCombat(engine);
                Check($"{label}: a Set copy burns 600 for each face-up LIGHT monster you control (2 → 1200)",
                    can && tried && o.LifePoints == lp - 1200,
                    $"can={can} tried={tried} faceUp={set.FaceUp} oppLp={o.LifePoints} was {lp}");
            }

            {
                // EffectDamageEvents batch (v162): Coffin Seller is Implemented — a Set copy activates face-up (placement) and
                // burns nothing on activation; the burn waits for a monster to reach the opponent's GY.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var set = PlaceSetTrap(engine, p, CoffinSeller, 0);
                var lp = o.LifePoints;
                var can = engine.CanActivateSpellTrap(p, set, fromHand: false);
                var tried = engine.TryActivateSpellTrap(p, set, fromHand: false);
                DrainCombat(engine);
                Check("Coffin Seller: a Set copy activates face-up and burns nothing on activation",
                    can && tried && set.FaceUp && o.LifePoints == lp,
                    $"can={can} tried={tried} faceUp={set.FaceUp} oppLp={o.LifePoints} was {lp}");
            }

            {
                // Positive control on an edited arm: Dark Core still discards, targets and banishes.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var prey = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var fodder = PutInHand(engine, p, Bewd);
                var card = PutInHand(engine, p, DarkCore);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var act = can && engine.TryActivateSpellTrap(p, card, fromHand: true);
                var paid = act && engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(fodder);
                var picked = paid && (!engine.IsAwaitingEffectTarget || engine.TrySelectEffectTarget(prey));
                DrainCombat(engine);
                Check("Dark Core: discard 1, then the targeted face-up monster is banished",
                    can && act && paid && picked && o.Banished.Contains(prey) && p.Graveyard.Contains(fodder),
                    $"can={can} act={act} paid={paid} picked={picked} banished={o.Banished.Contains(prey)} " +
                    $"fodderGy={p.Graveyard.Contains(fodder)}");
            }
        }
    }
}
