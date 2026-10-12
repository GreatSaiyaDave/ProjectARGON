using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "RedirectAndReplace" (v142): Fairy's Hand Mirror and Shift (a targeting Spell / Spell/Trap, or
    /// Shift's attack, now targets another appropriate monster), Kotodama (face-up monsters sharing a name are
    /// destroyed) and Banisher of the Light (any card sent to the GY is banished instead). Tailor of the Fickle was
    /// deferred here and is now the EquipModel batch's; its shape check is below.
    /// </summary>
    public static class RedirectAndReplaceRegressionTests
    {
        const int FairysHandMirror = 17653779;
        const int Shift = 59560625;
        const int Kotodama = 19406822;
        const int TailorOfTheFickle = 43641473;
        const int BanisherOfTheLight = 61528025;
        const int FiendsHandMirror = 58607704;

        const int OfferingsToTheDoomed = 19230407; // Quick-Play: target 1 face-up monster on the field; destroy it
        const int DarkHole = 53129443;             // untargeted: destroy all monsters
        const int Mst = 5318639;                   // targets a Spell/Trap Card
        const int OjamaTrio = 29843091;            // Trap: 3 "Ojama Tokens" to the opponent's field at once
        const int Jinzo = 77585513;                // Trap Cards cannot be activated
        const int SpellbindingCircle = 18807108;   // Continuous Trap: activate by targeting 1 opponent's monster
        const int NeedleWorm = 81843628;           // FLIP: send the top 5 cards of the opponent's Deck to the GY
        const int Sangan = 26202165;               // sent from the field to the GY: search a monster
        const int TributeToTheDoomed = 79759861;   // discard 1 card (cost), then target 1 monster; destroy it
        const int GuardianAngelJoan = 68007326;    // destroys a monster by battle and sends it to the GY: gain LP
        const int HystericFairy = 21297224;        // Tribute 2 monsters (cost, picked one by one): gain 1000 LP
        const int Megamorph = 22046459;            // Equip Spell, no leave-field trigger
        const int MacroCosmos = 30241314;
        const int DimensionalFissure = 81674782;
        const int SlateWarrior = 78636495;         // "If this card is destroyed by battle": no GY needed
        const int NeedleBurrower = 98162242;       // "destroys a monster and sends it to the Graveyard": burn
        const int AxeOfDespair = 40619825;         // Equip Spell
        const int TrapHole = 4206964;              // targets the Summoned monster
        const int TorrentialTribute = 53582587;    // "When a monster(s) is Summoned: Destroy all monsters"

        const int Celtic = 91152256;        // Warrior Lv4 1400/1200
        const int Bewd = 89631139;          // Dragon Lv8 3000/2500
        const int SummonedSkull = 70781052; // Fiend Lv6 2500/1200
        const int SkullServant = 32274490;  // Zombie Lv1 300/200
        const int GiantSoldier = 13039848;  // Rock Lv3 1300/2000
        const int MysticalElf = 15025844;   // Spellcaster Lv4 800/2000
        const int HarpieLady = 76812113;    // Winged Beast Lv4 1300/1400
        const int HarpieLady1 = 91932350;   // always treated as "Harpie Lady"

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("RedirectAndReplace: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            FairysHandMirrorDuels(db, pDeck, aDeck, Check);
            ShiftDuels(db, pDeck, aDeck, Check);
            KotodamaDuels(db, pDeck, aDeck, Check);
            BanisherDuels(db, pDeck, aDeck, Check);
            ReviewFindingDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static EffectClause OnlyClause(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
        }

        static bool RedirectClause(EffectClause c, string condition, string answers, EffectSide side) =>
            c != null && c.Timing == EffectTiming.Activate &&
            c.Action == EffectActionKind.RedirectToAnotherAppropriateTarget &&
            c.Zone == EffectZoneFilter.AnotherAppropriateTarget && c.Side == side &&
            c.RequiresTargetChoice && c.IsPsctTarget && !c.ChoiceDoesNotTarget &&
            c.ActivationCondition == condition && c.ConditionName == answers &&
            c.CheckedAt == ConditionCheckedAt.Activation && c.MakesChainLink && !c.StaysOnField &&
            c.TargetCount == 1 && !c.RequiresSecondTarget;

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            foreach (var (id, condition, answers, side) in new[]
                     {
                         (FairysHandMirror, RedirectAndReplaceRuntime.OpponentSpellTargetsOneMonster,
                             RedirectAndReplaceRuntime.AnswersSpell, EffectSide.Both),
                         (Shift, RedirectAndReplaceRuntime.OpponentTargetsOneMonsterYouControl,
                             RedirectAndReplaceRuntime.AnswersSpellOrTrap, EffectSide.Controller)
                     })
            {
                var def = db.Get(id);
                var c = OnlyClause(db, id, out var prog);
                Check($"{def?.name}: FullyCompiled, one targeted Activate RedirectToAnotherAppropriateTarget ({side})",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && RedirectClause(c, condition, answers, side),
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check($"{def?.name}: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(Kotodama);
                var c = OnlyClause(db, Kotodama, out var prog);
                Check("Kotodama: FullyCompiled, one ContinuousWhileFaceUp DestroyNewFaceUpMonstersSharingAName",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.DestroyNewFaceUpMonstersSharingAName && !c.RequiresTargetChoice &&
                    !c.MakesChainLink,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check("Kotodama: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(BanisherOfTheLight);
                var c = OnlyClause(db, BanisherOfTheLight, out var prog);
                Check("Banisher of the Light: FullyCompiled, one ContinuousWhileFaceUp BanishCardsSentToGyInstead",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.BanishCardsSentToGyInstead && c.Side == EffectSide.Both &&
                    !c.RequiresTargetChoice && !c.MakesChainLink,
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check("Banisher of the Light: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            Check("RedirectToAnotherAppropriateTarget / DestroyNewFaceUpMonstersSharingAName / BanishCardsSentToGyInstead " +
                  "map to shared resolutions",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.RedirectToAnotherAppropriateTarget }) == EffectResolutionKind.Protection &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.DestroyNewFaceUpMonstersSharingAName }) == EffectResolutionKind.Destroy &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.BanishCardsSentToGyInstead }) == EffectResolutionKind.Banish);

            // Tailor of the Fickle, deferred here, is the EquipModel batch's (two targets: the Equip Card, then the
            // monster it moves to). Its duels are in EquipModelRegressionTests.
            {
                var def = db.Get(TailorOfTheFickle);
                var prog = CardTextEffectCompiler.Compile(def);
                Check($"{def?.name}: compiled by the EquipModel batch (one MoveEquipCardToAnotherCorrectTarget clause)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.MoveEquipCardToAnotherCorrectTarget &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }
        }

        static CompiledCardProgram CompileText(string text, string type = "Trap Card", string race = "Normal") =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990142, name = "Near-miss Card", type = type, frameType = type.StartsWith("Trap") ? "trap" : "effect",
                race = race, desc = text
            });

        static bool CompilesTo(string text, EffectActionKind action, string type = "Trap Card", string race = "Normal")
        {
            var prog = CompileText(text, type, race);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        const string MirrorText =
            "When your opponent activates a Spell Card that targets exactly 1 monster (and no other cards) on the " +
            "field: Target another card that would be an appropriate target; that Spell now targets the new target.";

        const string KotodamaText =
            "If there are face-up monsters with the same name on the field, destroy those monsters. (If a monster is " +
            "Summoned or flipped face-up that has the same name as a monster that is already face-up on the field, " +
            "the new face-up monster is destroyed. If they are Summoned or flipped face-up at the same time, they are " +
            "all destroyed.)";

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            Check("Near miss: Fairy's Hand Mirror's text compiles (control)",
                CompilesTo(MirrorText, EffectActionKind.RedirectToAnotherAppropriateTarget));
            Check("Near miss: an extra sentence after Fairy's Hand Mirror's text is not compiled",
                !CompilesTo(MirrorText + " Then draw 1 card.", EffectActionKind.RedirectToAnotherAppropriateTarget));
            // SpellEventsAndHandSendTriggers batch: Fiend's Hand Mirror is now Implemented with its own Spell/Trap zone. Guard
            // rewritten to its compiled shape: it never picks a monster (Fairy's AnotherAppropriateTarget).
            var fiend = CardTextEffectCompiler.Compile(db.Get(FiendsHandMirror));
            Check("Near miss: Fiend's Hand Mirror (a Spell targeting a Spell/Trap) is not this template — it redirects to a Spell/Trap",
                fiend != null && fiend.FullyCompiled &&
                !fiend.ClauseList.Exists(c => c != null && c.Zone == EffectZoneFilter.AnotherAppropriateTarget) &&
                fiend.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.RedirectToAnotherAppropriateTarget &&
                                             c.Zone == EffectZoneFilter.AnotherAppropriateSpellTrapTarget));
            Check("Near miss: a Trap-answering Mirror is not this template",
                !CompilesTo(MirrorText.Replace("activates a Spell Card", "activates a Trap Card")
                        .Replace("that Spell now", "that Trap now"),
                    EffectActionKind.RedirectToAnotherAppropriateTarget));
            Check("Near miss: Shift without its attack half is not this template",
                !CompilesTo(
                    "When your opponent targets exactly 1 monster you control (and no other cards) with a Spell/Trap " +
                    "effect: Target another monster you control that would be an appropriate target; that Spell/Trap " +
                    "now targets the new target.", EffectActionKind.RedirectToAnotherAppropriateTarget));
            Check("Near miss: Kotodama's text compiles (control)",
                CompilesTo(KotodamaText, EffectActionKind.DestroyNewFaceUpMonstersSharingAName, "Effect Monster", "Fairy"));
            Check("Near miss: \"on your side of the field\" same-name destruction is not this template",
                !CompilesTo(KotodamaText.Replace("with the same name on the field, destroy",
                        "with the same name on your side of the field, destroy"),
                    EffectActionKind.DestroyNewFaceUpMonstersSharingAName, "Effect Monster", "Fairy"));
            Check("Near miss: Kotodama without its rulings sentence is not this template",
                !CompilesTo("If there are face-up monsters with the same name on the field, destroy those monsters.",
                    EffectActionKind.DestroyNewFaceUpMonstersSharingAName, "Effect Monster", "Fairy"));

            const string BanisherText = "Any card sent to the Graveyard is banished instead.";
            Check("Near miss: Banisher of the Light's text compiles (control)",
                CompilesTo(BanisherText, EffectActionKind.BanishCardsSentToGyInstead, "Effect Monster", "Fairy"));
            Check("Near miss: \"Any monster sent to the Graveyard\" (Dimensional Fissure's half) is not this template",
                !CompilesTo("Any monster sent to the Graveyard is banished instead.",
                    EffectActionKind.BanishCardsSentToGyInstead, "Effect Monster", "Fairy"));
            Check("Near miss: an extra sentence after Banisher of the Light's text is not compiled",
                !CompilesTo(BanisherText + " Then draw 1 card.", EffectActionKind.BanishCardsSentToGyInstead,
                    "Effect Monster", "Fairy"));
            foreach (var id in new[] { MacroCosmos, DimensionalFissure })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"Near miss: {db.Get(id)?.name} is not Banisher's template",
                    prog == null || !prog.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.BanishCardsSentToGyInstead));
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool ChainWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static bool AttackWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>The Player activates <paramref name="trap"/> in the open window and picks <paramref name="pick"/>.</summary>
        static bool AnswerWith(DuelEngine engine, DuelistState p, CardInstance trap, CardInstance pick)
        {
            var ok = engine.TryActivateSpellTrap(p, trap, fromHand: false);
            if (ok && engine.IsAwaitingEffectTarget)
                ok = engine.TrySelectEffectTarget(pick);
            PassAll(engine);
            return ok;
        }

        /// <summary>A fresh duel in the opponent's Main Phase with an empty board and empty hands.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>A fresh duel in the Player's Main Phase 1 (turn 3+) with an empty board and empty hands.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        // ───────────────────────── Fairy's Hand Mirror ─────────────────────────

        static void FairysHandMirrorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var mirror = PlaceSetTrap(engine, engine.Player, FairysHandMirror, 0);
                PlaceMonster(engine, engine.Player, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                Check("Fairy's Hand Mirror: cannot be activated at open game state",
                    reached && !engine.CanActivateSpellTrap(engine.Player, mirror, fromHand: false));
            }

            {
                // The opponent's Offerings to the Doomed targets the Player's Blue-Eyes; the Mirror switches it to
                // the Player's Celtic Guardian, which is destroyed instead.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 0);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var link = engine.Chain.Last;
                var targetedBewd = link != null && ReferenceEquals(link.Card, offerings) &&
                                   ReferenceEquals(link.Target, bewd);
                var offered = ChainWindowOffers(engine, p, mirror);
                Check("Fairy's Hand Mirror: offered when the opponent's Spell targets exactly 1 monster",
                    reached && act && targetedBewd && offered, $"act={act} targetBewd={targetedBewd} {Window(engine)}");
                var legal = offered && engine.TryActivateSpellTrap(p, mirror, fromHand: false) &&
                            (!engine.IsAwaitingEffectTarget ||
                             (engine.PendingActivation.LegalTargets.Count == 1 &&
                              engine.PendingActivation.LegalTargets.Contains(celtic)));
                Check("Fairy's Hand Mirror: the only appropriate new target is the other monster (not the current one)",
                    legal, engine.PendingActivation == null
                        ? "no pick"
                        : $"legal=[{string.Join(",", engine.PendingActivation.LegalTargets.Select(c => c.Name))}]");
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                PassAll(engine);
                Check("Fairy's Hand Mirror: the Spell resolves on the new target — Celtic destroyed, Blue-Eyes stays",
                    p.Graveyard.Contains(celtic) && p.TryFindMonster(bewd, out _) && p.Graveyard.Contains(mirror) &&
                    opp.Graveyard.Contains(offerings) && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"celticGy={p.Graveyard.Contains(celtic)} bewdField={p.TryFindMonster(bewd, out _)} " +
                    $"mirrorGy={p.Graveyard.Contains(mirror)} chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // Control: without the Mirror the Spell destroys its original target.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                PassAll(engine);
                Check("Offerings to the Doomed (control): destroys the monster it targeted",
                    p.Graveyard.Contains(bewd) && p.TryFindMonster(celtic, out _));
            }

            {
                // "face-up monster": a face-down monster is not an appropriate target, so with no other face-up
                // monster the Mirror is not offered.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Defense, false);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 0);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var offered = ChainWindowOffers(engine, p, mirror);
                PassAll(engine);
                Check("Fairy's Hand Mirror: not offered when the only other monster is not an appropriate (face-up) target",
                    act && !offered && p.Graveyard.Contains(bewd), Window(engine));
            }

            {
                // A Spell targeting a Spell/Trap (MST) does not open the Mirror.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 0);
                var mst = PutInHand(engine, opp, Mst);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, mst, fromHand: true);
                var offered = ChainWindowOffers(engine, p, mirror);
                PassAll(engine);
                Check("Fairy's Hand Mirror: not offered against a Spell that targets a Spell/Trap Card",
                    act && !offered, Window(engine));
            }

            {
                // An untargeted Spell (Dark Hole) does not open the Mirror.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 0);
                var dark = PutInHand(engine, opp, DarkHole);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, dark, fromHand: true);
                var offered = ChainWindowOffers(engine, p, mirror);
                PassAll(engine);
                Check("Fairy's Hand Mirror: not offered against a Spell that targets nothing (Dark Hole)",
                    act && !offered && p.Graveyard.Contains(bewd), Window(engine));
            }

            {
                // Jinzo: Trap Cards cannot be activated, so the Mirror is not offered.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                // Jinzo's lock applies to both players; the opponent controls no monster, so its Spell targets ours.
                PlaceMonster(engine, p, Jinzo, 2, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 0);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var offered = ChainWindowOffers(engine, p, mirror);
                PassAll(engine);
                Check("Fairy's Hand Mirror: Jinzo (Traps cannot be activated) keeps it out of the chain window",
                    act && !offered && p.Graveyard.Contains(bewd), Window(engine));
            }

            {
                // AI: the Player's Offerings targets the AI's Summoned Skull; the AI's Mirror sends it to the Player's
                // Celtic Guardian.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(engine, opp, FairysHandMirror, 0);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                PassAll(engine);
                Check("Fairy's Hand Mirror (AI): redirects the Player's Offerings onto the Player's own monster",
                    reached && act && p.Graveyard.Contains(celtic) && opp.TryFindMonster(skull, out _) &&
                    opp.Graveyard.Contains(mirror) && engine.Chain.Count == 0,
                    $"act={act} celticGy={p.Graveyard.Contains(celtic)} skullField={opp.TryFindMonster(skull, out _)} " +
                    $"mirrorGy={opp.Graveyard.Contains(mirror)} {Window(engine)}");
            }
        }

        // ───────────────────────── Shift ─────────────────────────

        static void ShiftDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Spell/Trap half: the opponent's Offerings targets the Player's Blue-Eyes; Shift moves it to the
                // Player's Skull Servant.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, p, SkullServant, 1, BattlePosition.Attack, true);
                var shift = PlaceSetTrap(engine, p, Shift, 0);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var offered = ChainWindowOffers(engine, p, shift);
                var answered = offered && AnswerWith(engine, p, shift, servant);
                Check("Shift: redirects the opponent's targeting Spell to another monster you control",
                    reached && answered && p.Graveyard.Contains(servant) && p.TryFindMonster(bewd, out _) &&
                    p.Graveyard.Contains(shift) && engine.Chain.Count == 0,
                    $"offered={offered} servantGy={p.Graveyard.Contains(servant)} bewd={p.TryFindMonster(bewd, out _)} " +
                    $"{Window(engine)}");
            }

            {
                // A Trap Card: the opponent's Spellbinding Circle targets the Player's Blue-Eyes. Shift (Spell/Trap)
                // is offered, Fairy's Hand Mirror (Spell only) is not; Shift binds the Skull Servant instead.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, p, SkullServant, 1, BattlePosition.Attack, true);
                var shift = PlaceSetTrap(engine, p, Shift, 0);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 1);
                var circle = PlaceSetTrap(engine, opp, SpellbindingCircle, 0);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, circle, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(bewd);
                var targetedBewd = ReferenceEquals(engine.Chain.Last?.Target, bewd);
                var shiftOffered = ChainWindowOffers(engine, p, shift);
                var mirrorOffered = ChainWindowOffers(engine, p, mirror);
                var answered = shiftOffered && AnswerWith(engine, p, shift, servant);
                Check("Shift: offered against a Trap Card targeting your monster (Fairy's Hand Mirror is not)",
                    act && targetedBewd && shiftOffered && !mirrorOffered,
                    $"act={act} targetBewd={targetedBewd} {Window(engine)}");
                Check("Shift: the Trap now binds the new target",
                    answered && ReferenceEquals(circle.EquippedTo, servant) && circle.FaceUp &&
                    opp.TryFindSpellTrap(circle, out _) && p.Graveyard.Contains(shift),
                    $"answered={answered} bound={circle.EquippedTo?.Name ?? "none"}");
            }

            {
                // The Spell targets the opponent's own monster: Shift ("a monster you control") is not offered, the
                // Mirror (either field) is. EngineResiduals (v163): Offerings to the Doomed targets a face-up monster and
                // the AI no longer destroys its own monster while yours is face-up, so yours are Set here and the AI's
                // second face-up monster is the Mirror's appropriate new target.
                var engine = OpponentMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, SkullServant, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, SkullServant, 1, BattlePosition.Defense, false);
                var shift = PlaceSetTrap(engine, p, Shift, 0);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 1);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var target = engine.Chain.Last?.Target;
                Check("Shift: not offered when the opponent's Spell targets the opponent's own monster (Mirror is)",
                    target != null && opp.TryFindMonster(target, out _) && !ChainWindowOffers(engine, p, shift) &&
                    ChainWindowOffers(engine, p, mirror), $"target={target?.Name} {Window(engine)}");
                PassAll(engine);
            }

            {
                // Attack half: the opponent's Summoned Skull attacks the Player's Celtic Guardian; Shift moves the
                // attack to the Player's Blue-Eyes, which destroys the Skull.
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                {
                    Check("Shift: attack setup", false, "no opponent Battle Phase");
                    return;
                }

                ClearBoard(engine);
                engine.ClearPendingResponse();
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, p, Bewd, 1, BattlePosition.Attack, true);
                var shift = PlaceSetTrap(engine, p, Shift, 0);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                skull.ClearAttackFlags();
                engine.NotifyPublic();
                if (engine.IsAwaitingResponse) engine.PassResponse();
                DrainCombat(engine);
                var oppLp = opp.LifePoints;
                var declared = engine.TryAttack(opp, skull, celtic);
                var offered = AttackWindowOffers(engine, p, shift);
                var legalOk = offered && engine.TryActivateSpellTrap(p, shift, fromHand: false) &&
                              (!engine.IsAwaitingEffectTarget ||
                               (engine.PendingActivation.LegalTargets.Count == 1 &&
                                engine.PendingActivation.LegalTargets.Contains(bewd)));
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(bewd);
                DrainCombat(engine);
                Check("Shift: offered when the opponent attacks a monster you control; new target = another monster",
                    declared && offered && legalOk, $"declared={declared} offered={offered} {Window(engine)}");
                Check("Shift: the attack now targets Blue-Eyes — Summoned Skull destroyed, Celtic Guardian untouched",
                    opp.Graveyard.Contains(skull) && p.TryFindMonster(celtic, out _) && p.TryFindMonster(bewd, out _) &&
                    p.Graveyard.Contains(shift) && opp.LifePoints == oppLp - 500 && !engine.HasDeclaredAttack,
                    $"skullGy={opp.Graveyard.Contains(skull)} celtic={p.TryFindMonster(celtic, out _)} " +
                    $"oppLp={oppLp}->{opp.LifePoints} {Window(engine)}");
            }

            {
                // Attack half, no other monster: not offered. A direct attack: not offered.
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                {
                    Check("Shift: lone-target setup", false, "no opponent Battle Phase");
                    return;
                }

                ClearBoard(engine);
                engine.ClearPendingResponse();
                var p = engine.Player;
                var opp = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var shift = PlaceSetTrap(engine, p, Shift, 0);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                skull.ClearAttackFlags();
                engine.NotifyPublic();
                DrainCombat(engine);
                var declared = engine.TryAttack(opp, skull, celtic);
                var offeredLone = AttackWindowOffers(engine, p, shift);
                DrainCombat(engine);
                Check("Shift: not offered when you control no other monster the attacker could attack",
                    declared && !offeredLone && p.Graveyard.Contains(celtic), Window(engine));
            }

            {
                // AI: the Player's Summoned Skull attacks the AI's Celtic Guardian; the AI's Shift moves the attack
                // to its Blue-Eyes, which survives and destroys the Skull.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 1, BattlePosition.Attack, true);
                var shift = PlaceSetTrap(engine, opp, Shift, 0);
                engine.NotifyPublic();
                var bp = engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var pLp = p.LifePoints;
                var declared = engine.TryAttack(p, skull, celtic);
                DrainCombat(engine);
                Check("Shift (AI): moves an attack it would lose onto a monster that wins the battle",
                    reached && bp && declared && p.Graveyard.Contains(skull) && opp.TryFindMonster(celtic, out _) &&
                    opp.TryFindMonster(bewd, out _) && opp.Graveyard.Contains(shift) && p.LifePoints == pLp - 500,
                    $"bp={bp} declared={declared} skullGy={p.Graveyard.Contains(skull)} " +
                    $"celtic={opp.TryFindMonster(celtic, out _)} shiftGy={opp.Graveyard.Contains(shift)} pLp={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────── Kotodama ─────────────────────────

        static void KotodamaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // A face-up Celtic Guardian is already on the field; the Player Normal Summons another: only the new
                // one is destroyed.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Kotodama, 0, BattlePosition.Defense, true);
                var old = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var fresh = PutInHand(engine, p, Celtic);
                var summoned = engine.TryNormalSummon(p, fresh, asSet: false);
                PassAll(engine);
                Check("Kotodama: a monster Summoned with the name of one already face-up is destroyed (only the new one)",
                    reached && summoned && p.Graveyard.Contains(fresh) && opp.TryFindMonster(old, out _),
                    $"summoned={summoned} freshGy={p.Graveyard.Contains(fresh)} old={opp.TryFindMonster(old, out _)}");
            }

            {
                // Kotodama Normal Summoned onto a field with two face-up Celtic Guardians: both are destroyed.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var a = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var bystander = PlaceMonster(engine, opp, SummonedSkull, 1, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var kotodama = PutInHand(engine, p, Kotodama);
                var summoned = engine.TryNormalSummon(p, kotodama, asSet: false);
                PassAll(engine);
                Check("Kotodama: when it is Summoned, every face-up monster sharing a name is destroyed",
                    reached && summoned && p.Graveyard.Contains(a) && opp.Graveyard.Contains(b) &&
                    opp.TryFindMonster(bystander, out _) && p.TryFindMonster(kotodama, out _),
                    $"summoned={summoned} aGy={p.Graveyard.Contains(a)} bGy={opp.Graveyard.Contains(b)}");
            }

            {
                // Control: without Kotodama, two Celtic Guardians coexist.
                var engine = PlayerMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var a = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, engine.Opponent, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                Check("Kotodama (control): same-name monsters stay without it",
                    p.TryFindMonster(a, out _) && engine.Opponent.TryFindMonster(b, out _));
            }

            {
                // Flipped face-up: a Set Celtic Guardian Flip Summoned while another is face-up is destroyed.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, opp, Kotodama, 1, BattlePosition.Defense, true);
                var old = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Defense, false);
                engine.NotifyPublic();
                var faceDownSpared = p.TryFindMonster(set, out _);
                var flipped = engine.TryFlipSummon(p, set);
                PassAll(engine);
                Check("Kotodama: a face-down monster is not affected; flipped face-up it is destroyed",
                    reached && faceDownSpared && flipped && p.Graveyard.Contains(set) && opp.TryFindMonster(old, out _),
                    $"spared={faceDownSpared} flipped={flipped} setGy={p.Graveyard.Contains(set)}");
            }

            {
                // Summoned at the same time: Ojama Trio's three Ojama Tokens are all destroyed (300 damage each).
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Kotodama, 0, BattlePosition.Defense, true);
                var trio = PlaceSetTrap(engine, p, OjamaTrio, 0);
                engine.NotifyPublic();
                var oppLp = opp.LifePoints;
                var act = engine.TryActivateSpellTrap(p, trio, fromHand: false);
                PassAll(engine);
                var tokensLeft = opp.MonstersOnField().Count(m => m != null && m.IsToken);
                Check("Kotodama: monsters Summoned at the same time with the same name are all destroyed",
                    reached && act && tokensLeft == 0 && opp.LifePoints == oppLp - 900,
                    $"act={act} tokensLeft={tokensLeft} oppLp={oppLp}->{opp.LifePoints}");
            }

            {
                // "Same name" is the rules name: Harpie Lady 1 is always treated as "Harpie Lady".
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Kotodama, 0, BattlePosition.Defense, true);
                var lady = PlaceMonster(engine, opp, HarpieLady, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var one = PutInHand(engine, p, HarpieLady1);
                var summoned = engine.TryNormalSummon(p, one, asSet: false);
                PassAll(engine);
                Check("Kotodama: a monster whose name is treated as the face-up one's is destroyed (Harpie Lady 1)",
                    reached && summoned && p.Graveyard.Contains(one) && opp.TryFindMonster(lady, out _),
                    $"summoned={summoned} oneGy={p.Graveyard.Contains(one)} lady={opp.TryFindMonster(lady, out _)}");
            }

            {
                // Flipped face-up by an attack: not during the Damage Step before damage calculation; after the
                // battle the flipped (surviving) Celtic Guardian is destroyed.
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                {
                    Check("Kotodama: attack-flip setup", false, "no opponent Battle Phase");
                    return;
                }

                ClearBoard(engine);
                engine.ClearPendingResponse();
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Kotodama, 0, BattlePosition.Defense, true);
                var old = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var set = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Defense, false);
                var elf = PlaceMonster(engine, opp, MysticalElf, 0, BattlePosition.Attack, true);
                elf.ClearAttackFlags();
                engine.NotifyPublic();
                DrainCombat(engine);
                var oppLp = opp.LifePoints;
                var declared = engine.TryAttack(opp, elf, set);
                DrainCombat(engine);
                // The battle happens first (800 ATK into 1200 DEF: the attacker's controller takes 400).
                Check("Kotodama: a Set monster flipped by an attack that survives the battle is destroyed afterwards",
                    declared && p.Graveyard.Contains(set) && p.TryFindMonster(old, out _) &&
                    opp.TryFindMonster(elf, out _) && !engine.HasDeclaredAttack && opp.LifePoints == oppLp - 400,
                    $"declared={declared} setGy={p.Graveyard.Contains(set)} old={p.TryFindMonster(old, out _)} " +
                    $"elf={opp.TryFindMonster(elf, out _)} oppLp={oppLp}->{opp.LifePoints}");
            }

            {
                // A face-down Kotodama applies nothing; flipped face-up it destroys the duplicates on the field.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var kotodama = PlaceMonster(engine, p, Kotodama, 0, BattlePosition.Defense, false);
                var a = PlaceMonster(engine, p, GiantSoldier, 1, BattlePosition.Defense, true);
                var b = PlaceMonster(engine, opp, GiantSoldier, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var spared = p.TryFindMonster(a, out _) && opp.TryFindMonster(b, out _);
                var flipped = engine.TryFlipSummon(p, kotodama);
                PassAll(engine);
                Check("Kotodama: face-down it does nothing; Flip Summoned it destroys both same-name monsters",
                    reached && spared && flipped && p.Graveyard.Contains(a) && opp.Graveyard.Contains(b),
                    $"spared={spared} flipped={flipped} aGy={p.Graveyard.Contains(a)} bGy={opp.Graveyard.Contains(b)}");
            }
        }
        // ───────────────────────── Banisher of the Light ─────────────────────────

        static void BanisherDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // A monster destroyed by an effect is banished, its "sent from the field to the GY" trigger (Sangan)
                // does not activate, and the Spell is banished too once it has resolved.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var sangan = PlaceMonster(engine, opp, Sangan, 0, BattlePosition.Attack, true);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var oppHand = opp.Hand.Count;
                var act = engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(sangan);
                PassAll(engine);
                Check("Banisher of the Light: a destroyed monster is banished; its \"sent to the GY\" trigger does not activate",
                    reached && act && opp.Banished.Contains(sangan) && !opp.Graveyard.Contains(sangan) &&
                    opp.Hand.Count == oppHand && p.TryFindMonster(banisher, out _),
                    $"act={act} sanganBanished={opp.Banished.Contains(sangan)} sanganGy={opp.Graveyard.Contains(sangan)} " +
                    $"oppHand={oppHand}->{opp.Hand.Count}");
                Check("Banisher of the Light: the resolved Spell is banished instead of going to the GY",
                    p.Banished.Contains(offerings) && !p.Graveyard.Contains(offerings),
                    $"banished={p.Banished.Contains(offerings)} gy={p.Graveyard.Contains(offerings)}");
            }

            {
                // Control: without Banisher, Sangan goes to the GY and its trigger adds a monster to the hand.
                var engine = PlayerMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var sangan = PlaceMonster(engine, opp, Sangan, 0, BattlePosition.Attack, true);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var oppHand = opp.Hand.Count;
                engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(sangan);
                PassAll(engine);
                Check("Banisher of the Light (control): without it Sangan goes to the GY and searches",
                    opp.Graveyard.Contains(sangan) && opp.Hand.Count == oppHand + 1 && p.Graveyard.Contains(offerings),
                    $"sanganGy={opp.Graveyard.Contains(sangan)} oppHand={oppHand}->{opp.Hand.Count}");
            }

            {
                // Face-down, its effect does not apply.
                var engine = PlayerMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                PassAll(engine);
                Check("Banisher of the Light: face-down it does not apply (the destroyed monster goes to the GY)",
                    act && opp.Graveyard.Contains(celtic) && !opp.Banished.Contains(celtic),
                    $"act={act} celticGy={opp.Graveyard.Contains(celtic)}");
            }

            {
                // Dark Hole destroys Banisher with the other monsters at the same time: all are banished, Banisher
                // included. Dark Hole leaves the field after it resolved, when Banisher is gone: it goes to the GY.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var mine = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var a = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, opp, SummonedSkull, 1, BattlePosition.Attack, true);
                var hole = PutInHand(engine, p, DarkHole);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(engine);
                var all = p.Banished.Contains(banisher) && p.Banished.Contains(mine) && opp.Banished.Contains(a) &&
                          opp.Banished.Contains(b);
                Check("Banisher of the Light: destroyed with other monsters at the same time, all are banished (itself too)",
                    reached && act && all && p.Graveyard.Count(c => c != hole) == 0 && opp.Graveyard.Count == 0,
                    $"act={act} banisher={p.Banished.Contains(banisher)} mine={p.Banished.Contains(mine)} " +
                    $"a={opp.Banished.Contains(a)} b={opp.Banished.Contains(b)} pGy={p.Graveyard.Count} oGy={opp.Graveyard.Count}");
                Check("Banisher of the Light: a Spell that resolves after Banisher left the field goes to the GY",
                    p.Graveyard.Contains(hole) && !p.Banished.Contains(hole),
                    $"holeGy={p.Graveyard.Contains(hole)}");
            }

            {
                // Banisher destroyed by an effect banishes itself; its Equip Card is sent by rule afterwards, when
                // Banisher is gone, and goes to the GY (as does the Spell, which leaves once it has resolved).
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Attack, true);
                var morph = engine.CreateCardInstance(Megamorph);
                morph.FaceUp = true;
                p.SpellTrapZones[0].Occupant = morph;
                morph.EquippedTo = banisher;
                banisher.Equips.Add(morph);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(banisher);
                PassAll(engine);
                Check("Banisher of the Light: destroyed, it banishes itself; its Equip Card, sent afterwards, goes to the GY",
                    reached && act && p.Banished.Contains(banisher) && p.Graveyard.Contains(morph) &&
                    !p.Banished.Contains(morph) && p.Graveyard.Contains(offerings),
                    $"act={act} banisher={p.Banished.Contains(banisher)} morphGy={p.Graveyard.Contains(morph)} " +
                    $"offeringsGy={p.Graveyard.Contains(offerings)}");
            }

            {
                // Tribute Summon: Banisher Tributed together with another monster — both are banished.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var bewd = PutInHand(engine, p, Bewd);
                engine.NotifyPublic();
                engine.ToggleTribute(p, banisher);
                engine.ToggleTribute(p, celtic);
                var summoned = engine.TryNormalSummon(p, bewd, asSet: false);
                PassAll(engine);
                Check("Banisher of the Light: Tributed with another monster, both are banished",
                    reached && summoned && p.TryFindMonster(bewd, out _) && p.Banished.Contains(banisher) &&
                    p.Banished.Contains(celtic) && p.Graveyard.Count == 0,
                    $"summoned={summoned} banisher={p.Banished.Contains(banisher)} celtic={p.Banished.Contains(celtic)} " +
                    $"gy={p.Graveyard.Count}");
            }

            {
                // A cost the player pays pick by pick: Hysteric Fairy Tributes Banisher first, then Celtic Guardian —
                // they are Tributed at the same time, so both are banished.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var fairy = PlaceMonster(engine, p, HystericFairy, 2, BattlePosition.Attack, true);
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var lp = p.LifePoints;
                var started = engine.TryActivateSpellTrap(p, fairy, fromHand: false) && engine.IsAwaitingEffectTarget;
                var paid = started && engine.TrySelectEffectTarget(banisher) && engine.TrySelectEffectTarget(celtic);
                PassAll(engine);
                Check("Banisher of the Light: Tributed as one pick of a 2-Tribute cost, the other Tribute is banished too",
                    reached && paid && p.Banished.Contains(banisher) && p.Banished.Contains(celtic) &&
                    p.Graveyard.Count == 0 && p.LifePoints == lp + 1000,
                    $"started={started} paid={paid} banisher={p.Banished.Contains(banisher)} " +
                    $"celtic={p.Banished.Contains(celtic)} gy={p.Graveyard.Count} lp={lp}->{p.LifePoints}");
            }

            {
                // A discard cost and a Deck send: Tribute to the Doomed's discarded card is banished; Needle Worm's
                // 5 cards from the Deck are banished.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var tribute = PutInHand(engine, p, TributeToTheDoomed);
                var fodder = PutInHand(engine, p, Celtic);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, tribute, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingEffectTarget; i++)
                {
                    var legal = engine.PendingActivation.LegalTargets;
                    if (legal.Contains(fodder)) engine.TrySelectEffectTarget(fodder);
                    else if (legal.Contains(skull)) engine.TrySelectEffectTarget(skull);
                    else break;
                }

                PassAll(engine);
                Check("Banisher of the Light: a discarded card (cost) and the destroyed monster are banished",
                    reached && act && p.Banished.Contains(fodder) && opp.Banished.Contains(skull) &&
                    p.Banished.Contains(tribute) && p.Graveyard.Count == 0 && opp.Graveyard.Count == 0,
                    $"act={act} fodder={p.Banished.Contains(fodder)} skull={opp.Banished.Contains(skull)} " +
                    $"spell={p.Banished.Contains(tribute)} pGy={p.Graveyard.Count} oGy={opp.Graveyard.Count}");

                var worm = PlaceMonster(engine, p, NeedleWorm, 1, BattlePosition.Defense, false);
                engine.NotifyPublic();
                var deck = opp.Deck.Count;
                var banished = opp.Banished.Count;
                var flipped = engine.TryFlipSummon(p, worm);
                PassAll(engine);
                Check("Banisher of the Light: cards sent from the Deck to the GY (Needle Worm) are banished",
                    flipped && opp.Deck.Count == deck - 5 && opp.Banished.Count == banished + 5 &&
                    opp.Graveyard.Count == 0,
                    $"flipped={flipped} deck={deck}->{opp.Deck.Count} banished={banished}->{opp.Banished.Count} " +
                    $"oGy={opp.Graveyard.Count}");
            }

            {
                // Battle: the monster Banisher's controller destroys by battle is banished, so Guardian Angel Joan's
                // "destroys a monster by battle and sends it to the GY" does not trigger (no LP gain).
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var joan = PlaceMonster(engine, p, GuardianAngelJoan, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var bp = engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var lp = p.LifePoints;
                var oppLp = opp.LifePoints;
                var declared = engine.TryAttack(p, joan, celtic);
                DrainCombat(engine);
                Check("Banisher of the Light: destroyed by battle, banished; \"and sends it to the GY\" triggers do not activate",
                    reached && bp && declared && opp.Banished.Contains(celtic) && !opp.Graveyard.Contains(celtic) &&
                    p.LifePoints == lp && opp.LifePoints == oppLp - 1400,
                    $"bp={bp} declared={declared} celticBanished={opp.Banished.Contains(celtic)} lp={lp}->{p.LifePoints} " +
                    $"oppLp={oppLp}->{opp.LifePoints}");
            }

            {
                // Control: without Banisher, Joan's trigger gains the destroyed monster's original ATK.
                var engine = PlayerMain(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var opp = engine.Opponent;
                var joan = PlaceMonster(engine, p, GuardianAngelJoan, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var lp = p.LifePoints;
                engine.TryAttack(p, joan, celtic);
                DrainCombat(engine);
                Check("Banisher of the Light (control): without it Joan's trigger gains 1400 LP",
                    opp.Graveyard.Contains(celtic) && p.LifePoints == lp + 1400,
                    $"celticGy={opp.Graveyard.Contains(celtic)} lp={lp}->{p.LifePoints}");
            }

            {
                // Banisher destroyed by battle has no effect while that destruction is applied: it goes to the GY,
                // and so does the monster destroyed in the same battle.
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                {
                    Check("Banisher of the Light: battle setup", false, "no opponent Battle Phase");
                    return;
                }

                ClearBoard(engine);
                engine.ClearPendingResponse();
                var p = engine.Player;
                var opp = engine.Opponent;
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Attack, true);
                banisher.UntilEndOfTurnAtk = 1300; // 1400 ATK: both monsters are destroyed
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                celtic.ClearAttackFlags();
                engine.NotifyPublic();
                DrainCombat(engine);
                var declared = engine.TryAttack(opp, celtic, banisher);
                DrainCombat(engine);
                Check("Banisher of the Light: destroyed by battle it goes to the GY, with the monster it destroyed",
                    declared && p.Graveyard.Contains(banisher) && opp.Graveyard.Contains(celtic) &&
                    !p.Banished.Contains(banisher) && !opp.Banished.Contains(celtic),
                    $"declared={declared} banisherGy={p.Graveyard.Contains(banisher)} celticGy={opp.Graveyard.Contains(celtic)}");
            }
        }

        // ───────────────────────── review findings (fixer pass) ─────────────────────────

        static CardInstance EquipTo(DuelEngine engine, DuelistState side, int equipId, int zone, CardInstance host)
        {
            var eq = engine.CreateCardInstance(equipId);
            eq.FaceUp = true;
            side.SpellTrapZones[zone].Occupant = eq;
            eq.EquippedTo = host;
            host.Equips.Add(eq);
            return eq;
        }

        static void ReviewFindingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // Slate Warrior "If this card is destroyed by battle" (Lua EVENT_BATTLE_DESTROYED, any location): its
            // trigger still applies when Banisher of the Light banishes it instead of sending it to the GY.
            foreach (var withBanisher in new[] { false, true })
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                if (withBanisher) PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, p, SummonedSkull, 1, BattlePosition.Attack, true);
                var slate = PlaceMonster(engine, opp, SlateWarrior, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var bp = engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var declared = engine.TryAttack(p, skull, slate);
                DrainCombat(engine);
                var where = withBanisher ? opp.Banished.Contains(slate) : opp.Graveyard.Contains(slate);
                Check($"Banisher of the Light{(withBanisher ? "" : " (control)")}: Slate Warrior destroyed by battle " +
                      $"{(withBanisher ? "and banished " : "")}still makes Summoned Skull lose 500 ATK/DEF",
                    reached && bp && declared && where && skull.CurrentAtk == 2000 && skull.CurrentDef == 700,
                    $"bp={bp} declared={declared} slateBanished={opp.Banished.Contains(slate)} " +
                    $"slateGy={opp.Graveyard.Contains(slate)} skull={skull.CurrentAtk}/{skull.CurrentDef}");
            }

            // A Token destroyed by battle while Banisher is face-up is banished instead of sent to the GY: Needle
            // Burrower's "destroys a monster and sends it to the Graveyard" burn does not apply. Without Banisher it does
            // not apply either: a Token never reaches the GY (c98162242.lua damcon: bc:IsLocation(LOCATION_GRAVE);
            // EngineHygiene v160 review).
            foreach (var withBanisher in new[] { false, true })
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                if (withBanisher) PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var burrower = PlaceMonster(engine, p, NeedleBurrower, 1, BattlePosition.Attack, true);
                var tok = engine.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                tok.Position = BattlePosition.Defense;
                opp.MonsterZones[0].Occupant = tok;
                engine.NotifyPublic();
                var bp = engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var oppLp = opp.LifePoints;
                var declared = engine.TryAttack(p, burrower, tok);
                DrainCombat(engine);
                var expected = oppLp;
                Check($"Banisher of the Light{(withBanisher ? "" : " (control)")}: Needle Burrower destroys a Token by " +
                      $"battle — {(withBanisher ? "banished instead" : "it leaves play, not sent to the GY")}, no burn",
                    reached && bp && declared && !opp.TryFindMonster(tok, out _) && opp.LifePoints == expected,
                    $"declared={declared} oppLp={oppLp}->{opp.LifePoints} expected={expected}");
            }

            {
                // Control: a real Level 1 monster destroyed by battle reaches the GY → Needle Burrower burns 500.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var burrower = PlaceMonster(engine, p, NeedleBurrower, 1, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, opp, SkullServant, 0, BattlePosition.Defense, true);
                engine.NotifyPublic();
                var bp = engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                var oppLp = opp.LifePoints;
                var declared = engine.TryAttack(p, burrower, servant);
                DrainCombat(engine);
                Check("Needle Burrower (control): destroys Skull Servant (Level 1) by battle and sends it to the GY → 500",
                    reached && bp && declared && opp.Graveyard.Contains(servant) && opp.LifePoints == oppLp - 500,
                    $"declared={declared} oppLp={oppLp}->{opp.LifePoints}");
            }

            {
                // Dark Hole destroys every monster at the same time, Banisher included. The Equip Card of a destroyed
                // monster is sent to the GY by rule afterwards, when Banisher is gone: it goes to the GY even though
                // its monster was destroyed before Banisher in the engine's order.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var axe = EquipTo(engine, opp, AxeOfDespair, 0, skull);
                var hole = PutInHand(engine, p, DarkHole);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(engine);
                Check("Banisher of the Light + Dark Hole: the opponent's Equip Card, sent by rule afterwards, goes to the GY",
                    reached && act && p.Banished.Contains(banisher) && opp.Banished.Contains(skull) &&
                    opp.Graveyard.Contains(axe) && !opp.Banished.Contains(axe) && !opp.TryFindSpellTrap(axe, out _),
                    $"act={act} banisher={p.Banished.Contains(banisher)} skull={opp.Banished.Contains(skull)} " +
                    $"axeGy={opp.Graveyard.Contains(axe)} axeBanished={opp.Banished.Contains(axe)}");
            }

            {
                // Control: Banisher stays on the field while an equipped monster is destroyed — the Equip Card sent
                // by rule afterwards is banished.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, BanisherOfTheLight, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var axe = EquipTo(engine, opp, AxeOfDespair, 0, skull);
                var offerings = PutInHand(engine, p, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, offerings, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(skull);
                PassAll(engine);
                Check("Banisher of the Light (still on the field): the Equip Card of a destroyed monster is banished",
                    reached && act && opp.Banished.Contains(skull) && opp.Banished.Contains(axe) &&
                    !opp.Graveyard.Contains(axe),
                    $"act={act} skull={opp.Banished.Contains(skull)} axeBanished={opp.Banished.Contains(axe)} " +
                    $"axeGy={opp.Graveyard.Contains(axe)}");
            }

            {
                // Tribute Summon: an equipped monster Tributed before Banisher (engine order), both Tributed at the
                // same time — the Equip Card goes to the GY by rule afterwards.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 1, BattlePosition.Defense, true);
                var morph = EquipTo(engine, p, Megamorph, 0, celtic);
                var bewd = PutInHand(engine, p, Bewd);
                engine.NotifyPublic();
                engine.ToggleTribute(p, celtic);
                engine.ToggleTribute(p, banisher);
                var summoned = engine.TryNormalSummon(p, bewd, asSet: false);
                PassAll(engine);
                Check("Banisher of the Light: an equipped Tribute sent with Banisher — its Equip Card goes to the GY",
                    reached && summoned && p.Banished.Contains(celtic) && p.Banished.Contains(banisher) &&
                    p.Graveyard.Contains(morph) && !p.Banished.Contains(morph),
                    $"summoned={summoned} celtic={p.Banished.Contains(celtic)} banisher={p.Banished.Contains(banisher)} " +
                    $"morphGy={p.Graveyard.Contains(morph)} morphBanished={p.Banished.Contains(morph)}");
            }

            {
                // A 2-Tribute cost paid pick by pick: the equipped monster first, then Banisher — the Equip Card is
                // sent by rule after both Tributes, when Banisher is gone: GY.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var fairy = PlaceMonster(engine, p, HystericFairy, 2, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 1, BattlePosition.Defense, true);
                var morph = EquipTo(engine, p, Megamorph, 0, celtic);
                engine.NotifyPublic();
                var lp = p.LifePoints;
                var started = engine.TryActivateSpellTrap(p, fairy, fromHand: false) && engine.IsAwaitingEffectTarget;
                var paid = started && engine.TrySelectEffectTarget(celtic) && engine.TrySelectEffectTarget(banisher);
                PassAll(engine);
                Check("Banisher of the Light: a pick-by-pick cost Tributes an equipped monster, then Banisher — the " +
                      "Equip Card goes to the GY",
                    reached && paid && p.Banished.Contains(celtic) && p.Banished.Contains(banisher) &&
                    p.Graveyard.Contains(morph) && !p.Banished.Contains(morph) && p.LifePoints == lp + 1000,
                    $"started={started} paid={paid} celtic={p.Banished.Contains(celtic)} " +
                    $"banisher={p.Banished.Contains(banisher)} morphGy={p.Graveyard.Contains(morph)} " +
                    $"morphBanished={p.Banished.Contains(morph)} lp={lp}->{p.LifePoints}");
            }

            {
                // Same cost, Banisher is not one of the Tributes: it is still on the field when the Equip Card is
                // sent — banished.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var fairy = PlaceMonster(engine, p, HystericFairy, 2, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, p, MysticalElf, 3, BattlePosition.Attack, true);
                var banisher = PlaceMonster(engine, p, BanisherOfTheLight, 1, BattlePosition.Defense, true);
                var morph = EquipTo(engine, p, Megamorph, 0, celtic);
                engine.NotifyPublic();
                var started = engine.TryActivateSpellTrap(p, fairy, fromHand: false) && engine.IsAwaitingEffectTarget;
                var paid = started && engine.TrySelectEffectTarget(celtic) && engine.TrySelectEffectTarget(elf);
                PassAll(engine);
                Check("Banisher of the Light (not Tributed): the equipped Tribute's Equip Card is banished",
                    reached && paid && p.TryFindMonster(banisher, out _) && p.Banished.Contains(celtic) &&
                    p.Banished.Contains(elf) && p.Banished.Contains(morph) && !p.Graveyard.Contains(morph),
                    $"started={started} paid={paid} morphBanished={p.Banished.Contains(morph)} " +
                    $"morphGy={p.Graveyard.Contains(morph)} morphField={p.TryFindSpellTrap(morph, out _)}");
            }

            // Kotodama destroys a Summoned duplicate before anyone gets priority: Trap Hole ("Target that monster",
            // Lua tc:IsOnField()) cannot be activated on it — neither by the Player nor by the AI.
            {
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Kotodama, 0, BattlePosition.Defense, true);
                var old = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var hole = PlaceSetTrap(engine, p, TrapHole, 0);
                var torrential = PlaceSetTrap(engine, p, TorrentialTribute, 1);
                engine.NotifyPublic();
                var fresh = PutInHand(engine, opp, Celtic);
                var summoned = engine.TryNormalSummon(opp, fresh, asSet: false);
                var gone = !opp.TryFindMonster(fresh, out _) && opp.Graveyard.Contains(fresh);
                var holeOffered = engine.PendingResponse != null &&
                                  engine.PendingResponse.LegalCards.Contains(hole);
                var torrentialOffered = engine.PendingResponse != null &&
                                        engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned &&
                                        engine.PendingResponse.LegalCards.Contains(torrential);
                var w = Window(engine);
                PassAll(engine);
                Check("Kotodama: a Summoned duplicate it destroyed cannot be targeted by Trap Hole",
                    reached && summoned && gone && !holeOffered && p.TryFindSpellTrap(hole, out _) &&
                    p.TryFindMonster(old, out _),
                    $"summoned={summoned} gone={gone} holeOffered={holeOffered} {w}");
                Check("Kotodama: an untargeted Summon answer (Torrential Tribute) is still offered",
                    torrentialOffered, w);
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, opp, Kotodama, 0, BattlePosition.Defense, true);
                PlaceMonster(engine, opp, Celtic, 1, BattlePosition.Attack, true);
                var hole = PlaceSetTrap(engine, opp, TrapHole, 0);
                engine.NotifyPublic();
                var fresh = PutInHand(engine, p, Celtic);
                var summoned = engine.TryNormalSummon(p, fresh, asSet: false);
                PassAll(engine);
                Check("Kotodama: the AI does not spend Trap Hole on a Summoned duplicate Kotodama already destroyed",
                    reached && summoned && p.Graveyard.Contains(fresh) && opp.TryFindSpellTrap(hole, out _),
                    $"summoned={summoned} freshGy={p.Graveyard.Contains(fresh)} holeField={opp.TryFindSpellTrap(hole, out _)} " +
                    $"holeGy={opp.Graveyard.Contains(hole)}");
            }

            {
                // Control: without Kotodama, Trap Hole is offered on the Summoned monster.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var hole = PlaceSetTrap(engine, p, TrapHole, 0);
                engine.NotifyPublic();
                var fresh = PutInHand(engine, opp, Celtic);
                var summoned = engine.TryNormalSummon(opp, fresh, asSet: false);
                var offered = engine.PendingResponse != null && engine.PendingResponse.LegalCards.Contains(hole);
                PassAll(engine);
                Check("Trap Hole (control): offered on a Summoned monster still on the field",
                    reached && summoned && offered, $"summoned={summoned} offered={offered}");
            }

            {
                // Fairy's Hand Mirror's new target can be on either field: the prompt uses a monster target kind, so
                // the picker tags each candidate YOURS / OPP.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, GiantSoldier, 1, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(engine, p, FairysHandMirror, 0);
                var offerings = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, offerings, fromHand: true);
                var first = engine.Chain.Last?.Target;
                var answered = first != null && ChainWindowOffers(engine, p, mirror) &&
                               engine.TryActivateSpellTrap(p, mirror, fromHand: false);
                var pending = engine.PendingActivation;
                var legal = pending?.LegalTargets.ToList() ?? new System.Collections.Generic.List<CardInstance>();
                var both = legal.Exists(m => p.TryFindMonster(m, out _)) && legal.Exists(m => opp.TryFindMonster(m, out _));
                var kindOk = answered && engine.IsAwaitingEffectTarget && pending != null &&
                             pending.TargetKind == EffectTargetKind.FaceUpMonsterOnField && both &&
                             !legal.Contains(first);
                var kind = pending?.TargetKind.ToString() ?? "none";
                var pick = legal.FirstOrDefault(m => opp.TryFindMonster(m, out _));
                if (engine.IsAwaitingEffectTarget && pick != null) engine.TrySelectEffectTarget(pick);
                PassAll(engine);
                Check("Fairy's Hand Mirror: the new-target prompt is a monster target kind over both fields",
                    reached && act && kindOk,
                    $"act={act} first={first?.Name} answered={answered} kind={kind} " +
                    $"legal=[{string.Join(",", legal.Select(m => m.Name))}]");
                Check("Fairy's Hand Mirror: redirected onto a monster of the Spell's controller, the Spell destroys it",
                    pick != null && opp.Graveyard.Contains(pick) && first != null && !opp.Graveyard.Contains(first) &&
                    !p.Graveyard.Contains(first),
                    $"pick={pick?.Name} pickGy={pick != null && opp.Graveyard.Contains(pick)} first={first?.Name}");
            }
        }
    }
}
