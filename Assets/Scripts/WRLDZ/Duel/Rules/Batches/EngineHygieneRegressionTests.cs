using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "EngineHygiene" (v160): the engine-aware Special Summon legality check (+ summon-cost hook), the central
    /// discard event and the Tribute record, the banish-N-ATTRIBUTE hand procedure (Gigantes, Spirit of Flames, Inferno),
    /// Ekibyo Drakmord's End Phase countdown, Forced Requisition, Super Rejuvenation, and the v159 audit follow-ups
    /// (Michizure answering Maryokutai's Tribute cost, Kycoo's human pick before the end-of-Damage-Step window, Royal
    /// Command with Lady Assailant of Flames' FLIP cost). Runtime: <see cref="EngineHygieneRuntime"/>,
    /// <see cref="SpecialSummonLegality"/>, <see cref="DiscardEvents"/>, <see cref="ReleaseEvents"/>.
    /// </summary>
    public static class EngineHygieneRegressionTests
    {
        const int SpiritOfFlames = 13522325;
        const int Inferno = 74823665;
        const int Gigantes = 47606319;
        const int DarkNecrofear = 31829185;
        const int ChaosSorcerer = 9596126;
        const int Ekibyo = 69954399;
        const int ForcedRequisition = 74923978;
        const int SuperRejuvenation = 27770341;
        const int SpearCretin = 58551308;
        const int Jowgen = 41855169;
        const int Michizure = 37580756;
        const int Maryokutai = 71466592;
        const int Kycoo = 88240808;
        const int RoyalCommand = 33950246;
        const int LadyAssailant = 90147755;

        const int FlameManipulator = 34460851; // FIRE Spellcaster Lv3 900/1000
        const int HinotamaSoul = 96851799;     // FIRE Pyro Lv2 600/500
        const int Celtic = 91152256;           // EARTH Warrior Lv4 1400/1200
        const int GiantSoldier = 13039848;     // EARTH Rock Lv3 1300/2000
        const int SkullServant = 32274490;     // DARK Zombie Lv1 300/200
        const int Koumori = 67724379;          // DARK Dragon Lv4 1500/1200
        const int BabyDragon = 88819587;       // WIND Dragon Lv3 1200/700
        const int SummonedSkull = 70781052;    // DARK Fiend Lv6 2500/1200
        const int MysticalElf = 15025844;      // LIGHT Spellcaster Lv4 800/2000
        const int PotOfGreed = 55144522;
        const int LightningVortex = 69162969;
        const int CardDestruction = 72892473;
        const int Minar = 32539892;            // discarded by an opponent's card effect: 1000 damage

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("EngineHygiene: " + name, ok, detail);

            Guard(Check, "compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "near misses", () => NearMisses(db, Check));
            Guard(Check, "summon legality", () => SummonLegality(db, pDeck, aDeck, Check));
            Guard(Check, "banish procedure", () => BanishProcedureDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Spirit of Flames ATK", () => SpiritOfFlamesAtk(db, pDeck, aDeck, Check));
            Guard(Check, "Inferno", () => InfernoDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Ekibyo Drakmord", () => EkibyoDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Forced Requisition", () => ForcedRequisitionDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Super Rejuvenation", () => SuperRejuvenationDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Royal Command x Lady Assailant", () => RoyalCommandFlipCost(db, pDeck, aDeck, Check));
            Guard(Check, "Kycoo x end of Damage Step", () => KycooEndOfDamageStep(db, pDeck, aDeck, Check));
            Guard(Check, "Michizure x Maryokutai", () => MichizureAnswersMaryokutai(db, pDeck, aDeck, Check));
            Guard(Check, "Kycoo defending", () => KycooDefending(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk Check, string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check($"{label}: no exception", false, ex.GetType().Name + ": " + ex.Message + " @ " +
                                                         (ex.StackTrace ?? "").Split('\n').FirstOrDefault());
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} [{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}]";

        static CompiledCardProgram CompileText(string text, string type, string race, string frame) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990160, name = "Near-miss Card", type = type, frameType = frame, race = race, desc = text,
                atk = 900, def = 900, level = 3, attribute = "FIRE"
            });

        static string Window(DuelEngine e) =>
            e.PendingResponse == null
                ? (e.PendingActivation == null ? "idle" : $"pick[{e.PendingActivation.Card?.Name}] legal=" +
                                                          string.Join(",", e.PendingActivation.LegalTargets.Select(c => c?.Name)))
                : $"window={e.PendingResponse.Timing} responder={e.PendingResponse.Responder?.Name} legal=" +
                  string.Join(",", e.PendingResponse.LegalCards.Select(c => c?.Name));

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static void Empty(DuelEngine engine)
        {
            ClearBoard(engine);
            engine.ClearPendingResponse();
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

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

            Empty(engine);
            return engine;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            Empty(engine);
            return engine;
        }

        static CardInstance InGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        /// <summary>End the current turn and reach the next turn's Main Phase 1.</summary>
        static bool NextTurn(DuelEngine e)
        {
            var turn = e.TurnNumber;
            for (var i = 0; i < 6 && e.TurnNumber == turn; i++)
            {
                PassAll(e);
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            for (var i = 0; i < 6 && e.Phase != DuelPhase.Main1; i++)
            {
                PassAll(e);
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                if (e.Phase != DuelPhase.Main1 && e.CanContinueAfterStandby(e.TurnPlayer)) e.ContinueAfterStandby();
            }

            PassAll(e);
            return e.TurnNumber == turn + 1;
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, attr) in new[] { (SpiritOfFlames, "FIRE"), (Inferno, "FIRE"), (Gigantes, "EARTH") })
            {
                var prog = Prog(db, id);
                Check($"{db.Get(id)?.name}: Implemented; hand procedure banishing 1 {attr} monster from the GY (no Tribute)",
                    prog != null && prog.FullyCompiled && Implemented(db, id) &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                        c.Action == EffectActionKind.SpecialSummonFromHandProcedure && c.BanishFromGyCount == 1 &&
                        c.AttributeFilter == attr && c.Amount == 0 && !c.MakesChainLink) &&
                    !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SpecialSummonThisFromHand),
                    Describe(prog));
            }

            var inferno = Prog(db, Inferno);
            Check("Inferno: 1500 to the opponent when it destroys an opponent's monster by battle and sends it to the GY",
                inferno != null && inferno.ClauseList.Exists(c => c != null &&
                    c.Timing == EffectTiming.ThisCardDestroysByBattle &&
                    c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 1500 &&
                    (c.SourceSnippet ?? "").Contains("sends it to the GY")), Describe(inferno));

            var ek = Prog(db, Ekibyo);
            Check("Ekibyo Drakmord: Implemented — any face-up monster, cannot attack, countdown 2",
                ek != null && ek.FullyCompiled && Implemented(db, Ekibyo) && ek.ClauseList.Count == 3 &&
                ek.ClauseList[0].Action == EffectActionKind.EquipThisToTarget &&
                ek.ClauseList[0].Zone == EffectZoneFilter.FieldAnyMonster &&
                ek.ClauseList[1].Action == EffectActionKind.EquippedCannotAttack &&
                ek.ClauseList[2].Action == EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis &&
                ek.ClauseList[2].Amount == 2, Describe(ek));

            var fr = Prog(db, ForcedRequisition);
            Check("Forced Requisition: Implemented — window-only activation (you discarded), no other clause",
                fr != null && fr.FullyCompiled && Implemented(db, ForcedRequisition) && fr.ClauseList.Count == 1 &&
                fr.ClauseList[0].Timing == EffectTiming.Activate &&
                fr.ClauseList[0].Action == EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard &&
                fr.ClauseList[0].ActivationCondition == DiscardEvents.YouDiscardedFromHand &&
                fr.ClauseList[0].StaysOnField && DiscardEvents.AnswersDiscardWindow(fr), Describe(fr));

            var sr = Prog(db, SuperRejuvenation);
            Check("Super Rejuvenation: Implemented — one End Phase draw registration",
                sr != null && sr.FullyCompiled && Implemented(db, SuperRejuvenation) && sr.ClauseList.Count == 1 &&
                sr.ClauseList[0].Timing == EffectTiming.Activate &&
                sr.ClauseList[0].Action == EffectActionKind.DrawAtEndPhasePerDragonsDiscardedOrTributedThisTurn,
                Describe(sr));

            Check("Vocabulary: Forced Requisition → Discard, Super Rejuvenation → Draw",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard }) == EffectResolutionKind.Discard &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.DrawAtEndPhasePerDragonsDiscardedOrTributedThisTurn }) ==
                EffectResolutionKind.Draw);
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var necro = Prog(db, DarkNecrofear);
            Check("Dark Necrofear keeps its Type procedure (hand Activate, banish 3 Fiends)",
                necro != null && necro.FullyCompiled && necro.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.SpecialSummonThisFromHand && c.RaceFilter == "Fiend" &&
                    c.BanishFromGyCount == 3), Describe(necro));

            var chaos = Prog(db, ChaosSorcerer);
            // ChaosBanishSummons batch: "1 LIGHT and 1 DARK" is its own pair procedure (SecondAttributeFilter), never the
            // single-Attribute "banishing 2 LIGHT monsters" shape.
            Check("Chaos Sorcerer (1 LIGHT and 1 DARK) is the pair procedure, not the single-Attribute one",
                chaos != null && chaos.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.SpecialSummonFromHandProcedure && c.BanishFromGyCount == 2 &&
                    c.AttributeFilter == "LIGHT" && c.SecondAttributeFilter == "DARK") &&
                !chaos.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                    string.IsNullOrEmpty(c.SecondAttributeFilter)), Describe(chaos));

            var sofText = db.Get(SpiritOfFlames)?.desc ?? "";
            Check("Near miss: 'from your Deck' banish is not the hand procedure",
                !(CompileText(sofText.Replace("from your Graveyard", "from your Deck"), "Effect Monster", "Pyro", "effect")
                    ?.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SpecialSummonFromHandProcedure) ?? false));
            Check("Near miss: 'during each Battle Phase' is not Spirit of Flames' ATK",
                !(CompileText(sofText.Replace("during your Battle Phase only", "during each Battle Phase"),
                    "Effect Monster", "Pyro", "effect")?.FullyCompiled ?? false));

            var infText = db.Get(Inferno)?.desc ?? "";
            Check("Near miss: Inferno burning its controller is not compiled",
                !(CompileText(infText.Replace("Inflict 1500 damage to your opponent", "Inflict 1500 damage to you"),
                    "Effect Monster", "Pyro", "effect")?.FullyCompiled ?? false));

            var ekText = db.Get(Ekibyo)?.desc ?? "";
            Check("Near miss: Ekibyo without 'returned to the owner's hand' is not compiled",
                !(CompileText(ekText.Replace(" At that time, this card is returned to the owner's hand.", ""),
                    "Spell Card", "Equip", "spell")?.FullyCompiled ?? false));

            var frText = db.Get(ForcedRequisition)?.desc ?? "";
            Check("Near miss: Forced Requisition without its 'After that' sentence is not compiled",
                !(CompileText(frText.Substring(0, frText.IndexOf(" After that", StringComparison.Ordinal)),
                    "Trap Card", "Continuous", "trap")?.FullyCompiled ?? false));
            Check("Near miss: 'discard half as many' is not Forced Requisition",
                !(CompileText(frText.Replace("the same number of cards", "half as many cards"), "Trap Card",
                    "Continuous", "trap")?.FullyCompiled ?? false));

            var srText = db.Get(SuperRejuvenation)?.desc ?? "";
            Check("Near miss: Super Rejuvenation counting only discards is not compiled",
                !(CompileText(srText.Replace("discarded or Tributed", "discarded"), "Spell Card", "Quick-Play", "spell")
                    ?.FullyCompiled ?? false));
            Check("Near miss: Super Rejuvenation counting Spellcasters is not compiled",
                !(CompileText(srText.Replace("Dragon monsters", "Spellcaster monsters"), "Spell Card", "Quick-Play",
                    "spell")?.FullyCompiled ?? false));
        }

        // ───────────────────────── Special Summon legality ─────────────────────────

        static void SummonLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celticGy = InGy(e, p, Celtic);
                var sofGy = InGy(e, p, SpiritOfFlames);
                var sofHand = PutInHand(e, p, SpiritOfFlames);
                var celticHand = PutInHand(e, p, Celtic);
                bool Can(CardInstance c, SummonOrigin from) =>
                    SpecialSummonLegality.CanBeSpecialSummoned(e, p, c, null, from);
                var before = Can(celticGy, SummonOrigin.Graveyard) && Can(celticHand, SummonOrigin.Hand) &&
                             !Can(sofGy, SummonOrigin.Graveyard) && !Can(sofHand, SummonOrigin.Hand) &&
                             !Can(sofHand, SummonOrigin.Deck);
                sofGy.ProcedureCompleted = true;
                var revived = Can(sofGy, SummonOrigin.Graveyard);
                Check("Legality: a Semi-Nomi (Spirit of Flames) is never Special Summoned from the hand/Deck by an effect, " +
                      "and from the GY only once properly summoned",
                    reached && before && revived, $"before={before} revived={revived}");

                var jowgen = PlaceMonster(e, p, Jowgen, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Legality: Jowgen the Spiritualist face-up — nothing can be Special Summoned",
                    !Can(celticGy, SummonOrigin.Graveyard) && !Can(celticHand, SummonOrigin.Hand) &&
                    !Can(sofGy, SummonOrigin.Graveyard), $"jowgen={jowgen.FaceUp}");
            }

            {
                // Spear Cretin (v159 audit): a never-properly-summoned Spirit of Flames in the GY is not offered.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var cretin = PlaceMonster(e, p, SpearCretin, 0, BattlePosition.Defense, false);
                InGy(e, p, GiantSoldier);
                InGy(e, p, SpiritOfFlames);
                var flipped = e.TryFlipSummon(p, cretin);
                DrainCombat(e);
                PassAll(e);
                e.DestroyMonsterPublic(p, cretin, null);
                var offered = e.PendingActivation?.LegalTargets?.Select(c => c.CardId).ToList() ?? new List<int>();
                Check("Spear Cretin: only Special Summonable GY monsters are offered (not an unsummoned Spirit of Flames)",
                    reached && flipped && offered.Contains(GiantSoldier) && !offered.Contains(SpiritOfFlames),
                    $"flipped={flipped} offered=[{string.Join(",", offered)}] {Window(e)}");
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
            }

            {
                // Summon-cost hook (Tyrant Dragon forward hook): a cost governs one copy from the GY only.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var subject = InGy(e, p, Celtic);
                var payable = false;
                var paid = 0;
                SpecialSummonLegality.RegisterSummonCost(new SpecialSummonLegality.SummonCost
                {
                    Name = "EngineHygieneTest#" + Guid.NewGuid().ToString("N"),
                    AppliesTo = (c, from) => ReferenceEquals(c, subject) && from == SummonOrigin.Graveyard,
                    CanPay = (_, _, _, _) => payable,
                    Pay = (_, _, _, _) =>
                    {
                        paid++;
                        return true;
                    }
                });
                var refusedCheck = !SpecialSummonLegality.CanBeSpecialSummoned(e, p, subject, null, SummonOrigin.Graveyard);
                p.Graveyard.Remove(subject);
                e.NextSpecialSummonFromGraveyard = true;
                var refused = !e.SpecialSummonToField(p, subject, BattlePosition.Attack, true);
                payable = true;
                var allowedCheck = SpecialSummonLegality.CanBeSpecialSummoned(e, p, subject, null, SummonOrigin.Graveyard);
                e.NextSpecialSummonFromGraveyard = true;
                var summoned = e.SpecialSummonToField(p, subject, BattlePosition.Attack, true);
                Check("Summon cost hook: unpayable refuses the check and the Summon; payable pays once as it is Summoned",
                    reached && refusedCheck && refused && allowedCheck && summoned && paid == 1 &&
                    p.TryFindMonster(subject, out _),
                    $"refusedCheck={refusedCheck} refused={refused} allowed={allowedCheck} summoned={summoned} paid={paid}");
            }
        }

        // ───────────────────────── banish procedure ─────────────────────────

        static void BanishProcedureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // A human with 2 FIRE monsters picks the one to banish.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var sof = PutInHand(e, p, SpiritOfFlames);
                var manip = InGy(e, p, FlameManipulator);
                var hino = InGy(e, p, HinotamaSoul);
                var celtic = InGy(e, p, Celtic);
                e.NotifyPublic();
                var can = e.CanSpecialSummonProcedure(p, sof);
                var started = can && e.TrySpecialSummonProcedure(p, sof);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                var pickOffered = e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(manip) &&
                                  legal.Contains(hino) && !legal.Contains(celtic);
                var picked = pickOffered && e.TrySelectEffectTarget(hino);
                PassAll(e);
                Check("Spirit of Flames: the hand Special Summon banishes the FIRE monster the player picks",
                    reached && can && started && pickOffered && picked && p.TryFindMonster(sof, out _) && sof.FaceUp &&
                    sof.ProcedureCompleted && p.Banished.Contains(hino) && p.Graveyard.Contains(manip) &&
                    p.Graveyard.Contains(celtic) && !p.Hand.Contains(sof),
                    $"can={can} started={started} pick={pickOffered} picked={picked} {Window(e)}");
            }

            {
                // Cancel at the pick: nothing is banished, the card stays in the hand.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var sof = PutInHand(e, p, SpiritOfFlames);
                InGy(e, p, FlameManipulator);
                InGy(e, p, HinotamaSoul);
                var started = e.TrySpecialSummonProcedure(p, sof);
                var cancelled = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                Check("Spirit of Flames: Cancel at the banish pick — nothing banished, still in the hand",
                    reached && started && cancelled && p.Hand.Contains(sof) && p.Banished.Count == 0 &&
                    p.Graveyard.Count == 2 && !e.IsAwaitingEffectTarget, $"started={started} cancelled={cancelled}");
            }

            {
                // No FIRE monster in the GY: not offered; a lone FIRE monster is banished without a pick (Inferno).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var inferno = PutInHand(e, p, Inferno);
                InGy(e, p, Celtic);
                var noFire = !e.CanSpecialSummonProcedure(p, inferno);
                var hino = InGy(e, p, HinotamaSoul);
                var summoned = e.TrySpecialSummonProcedure(p, inferno);
                PassAll(e);
                Check("Inferno: no FIRE monster in the GY → no procedure; a lone FIRE monster is banished at once",
                    reached && noFire && summoned && !e.IsAwaitingEffectTarget && p.TryFindMonster(inferno, out _) &&
                    p.Banished.Contains(hino), $"noFire={noFire} summoned={summoned} {Window(e)}");

                // Not Normal Summonable; the Normal Summon stays unused.
                var gig = PutInHand(e, p, Gigantes);
                Check("Gigantes: cannot be Normal Summoned (summon restriction kept)",
                    !e.TryNormalSummon(p, gig, asSet: false) && p.Hand.Contains(gig));
            }

            {
                // The AI uses the procedure on its own (Gigantes, banishing an EARTH monster).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var opp = e.Opponent;
                var gig = PutInHand(e, opp, Gigantes);
                var soldier = InGy(e, opp, GiantSoldier);
                InGy(e, opp, Celtic);
                var wants = ToonsRuntime.AiWantsHandProcedure(e, opp, gig);
                var summoned = e.TrySpecialSummonProcedure(opp, gig);
                PassAll(e);
                Check("Gigantes (AI): Special Summoned by banishing one EARTH monster (no pick left open)",
                    reached && wants && summoned && opp.TryFindMonster(gig, out _) && opp.Banished.Count == 1 &&
                    opp.Graveyard.Count == 1 && !e.IsAwaitingEffectTarget,
                    $"wants={wants} summoned={summoned} soldierBanished={opp.Banished.Contains(soldier)} {Window(e)}");
            }

            {
                // Semi-Nomi: a Spirit of Flames discarded to the GY is not revived; once properly summoned it may be.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var sof = InGy(e, p, SpiritOfFlames);
                e.NextSpecialSummonFromGraveyard = true;
                p.Graveyard.Remove(sof);
                var refused = !e.SpecialSummonToField(p, sof, BattlePosition.Attack, true);
                Check("Spirit of Flames: never properly summoned — the engine refuses another Special Summon",
                    reached && refused && !p.TryFindMonster(sof, out _));
            }
        }

        static void SpiritOfFlamesAtk(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var sof = PlaceMonster(e, p, SpiritOfFlames, 0, BattlePosition.Attack, true);
            sof.ProcedureCompleted = true;
            e.NotifyPublic();
            var main = sof.CurrentAtk;
            var bp = e.TryEnterBattlePhase(p);
            e.NotifyPublic();
            var battle = sof.CurrentAtk;
            Check("Spirit of Flames: 1700 in Main Phase 1, 2000 during your Battle Phase",
                reached && bp && main == 1700 && battle == 2000, $"main={main} battle={battle} bp={bp}");
            NextTurn(e);
            e.TryEnterBattlePhase(e.Opponent);
            e.NotifyPublic();
            Check("Spirit of Flames: 1700 during the opponent's Battle Phase",
                e.TurnPlayer == e.Opponent && sof.CurrentAtk == 1700, $"turn={e.TurnPlayer?.Name} atk={sof.CurrentAtk} phase={e.Phase}");
        }

        // ───────────────────────── Inferno ─────────────────────────

        static void InfernoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacking: destroys Skull Servant → 800 battle damage + 1500.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var inf = PlaceMonster(e, p, Inferno, 0, BattlePosition.Attack, true);
                inf.ProcedureCompleted = true;
                var servant = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var atk = e.TryAttack(p, inf, servant);
                DrainCombat(e);
                PassAll(e);
                Check("Inferno (attacker): 800 battle damage + 1500 when it destroys and sends the monster to the GY",
                    reached && bp && atk && opp.Graveyard.Contains(servant) && opp.LifePoints == lp - 800 - 1500,
                    $"lp {lp}->{opp.LifePoints} servantGy={opp.Graveyard.Contains(servant)}");
            }

            {
                // Defending: the opponent's Skull Servant attacks Inferno in Attack Position → 1500 to the attacker's controller.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Empty(e);
                var p = e.Player;
                var opp = e.Opponent;
                var inf = PlaceMonster(e, p, Inferno, 0, BattlePosition.Attack, true);
                inf.ProcedureCompleted = true;
                var servant = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                var atk = e.TryAttack(opp, servant, inf);
                DrainCombat(e);
                PassAll(e);
                Check("Inferno (defender): destroys the attacker by battle → 800 battle damage + 1500 to the opponent",
                    reached && atk && opp.Graveyard.Contains(servant) && p.TryFindMonster(inf, out _) &&
                    opp.LifePoints == lp - 800 - 1500, $"lp {lp}->{opp.LifePoints} atk={atk}");
            }

            {
                // A Token destroyed by battle never reaches the GY: no 1500 (Lua damcon: bc:IsLocation(LOCATION_GRAVE)).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var inf = PlaceMonster(e, p, Inferno, 0, BattlePosition.Attack, true);
                inf.ProcedureCompleted = true;
                var tok = e.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                tok.FaceUp = true;
                tok.Position = BattlePosition.Defense;
                tok.SetThisTurn = false;
                tok.SummonedThisTurn = false;
                opp.MonsterZones[0].Occupant = tok;
                e.NotifyPublic();
                var lp = opp.LifePoints;
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                var atk = e.TryAttack(p, inf, tok);
                DrainCombat(e);
                PassAll(e);
                Check("Inferno: destroying a Token by battle inflicts no damage (it is not sent to the GY)",
                    reached && bp && atk && !opp.TryFindMonster(tok, out _) && opp.LifePoints == lp,
                    $"lp {lp}->{opp.LifePoints} atk={atk} tokGone={!opp.TryFindMonster(tok, out _)}");
            }
        }

        // ───────────────────────── Ekibyo Drakmord ─────────────────────────

        static void EkibyoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Equipped to the opponent's monster during your turn: destroyed at the end of their 2nd turn after it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var ek = PutInHand(e, p, Ekibyo);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, ek, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
                PassAll(e);
                var equipped = ek.EquippedTo == skull && ek.FaceUp;
                NextTurn(e); // opponent's turn 1
                var cannotAttack = e.TurnPlayer == opp && !e.CanAttack(opp, skull);
                var afterOwnEnd = EngineHygieneRuntime.CountdownOf(ek);
                NextTurn(e); // opponent's End Phase 1 passed → player's turn
                var afterFirst = EngineHygieneRuntime.CountdownOf(ek);
                var stillThere = opp.TryFindMonster(skull, out _);
                NextTurn(e); // player's End Phase → opponent's turn 2
                NextTurn(e); // opponent's End Phase 2 → destroyed
                Check("Ekibyo Drakmord: the equipped monster cannot attack; counts only its controller's End Phases",
                    reached && act && equipped && cannotAttack && afterOwnEnd == 0 && afterFirst == 1 && stillThere,
                    $"act={act} equipped={equipped} cannotAttack={cannotAttack} counts={afterOwnEnd}/{afterFirst}");
                Check("Ekibyo Drakmord: at the end of the controller's 2nd turn the monster is destroyed and Ekibyo returns " +
                      "to its owner's hand",
                    !opp.TryFindMonster(skull, out _) && opp.Graveyard.Contains(skull) && p.Hand.Contains(ek) &&
                    !p.Graveyard.Contains(ek), $"skullGy={opp.Graveyard.Contains(skull)} ekHand={p.Hand.Contains(ek)} " +
                                               $"ekGy={p.Graveyard.Contains(ek)} turn={e.TurnNumber}");
            }

            {
                // Equipped to your own monster during your turn: this turn's End Phase counts (1), your next one destroys.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var ek = PutInHand(e, p, Ekibyo);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, ek, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                PassAll(e);
                NextTurn(e);
                var first = EngineHygieneRuntime.CountdownOf(ek);
                NextTurn(e);
                var alive = p.TryFindMonster(celtic, out _);
                NextTurn(e);
                Check("Ekibyo Drakmord on your own monster: the activation turn's End Phase counts; the next one destroys",
                    reached && act && first == 1 && alive && p.Graveyard.Contains(celtic) && p.Hand.Contains(ek),
                    $"act={act} first={first} alive={alive} celticGy={p.Graveyard.Contains(celtic)} ekHand={p.Hand.Contains(ek)}");
            }

            {
                // v160 integration audit (EngineHygiene x EquipModel): moved to another monster (Tailor of the Fickle /
                // Collected Power, EquipFamilyRuntime.MoveEquip = Duel.Equip) it keeps the count from its activation
                // (Lua eqpop1 resets it only on activation): the new monster is destroyed at its controller's 2nd End Phase.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 1, BattlePosition.Attack, true);
                var ek = PutInHand(e, p, Ekibyo);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, ek, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
                PassAll(e);
                NextTurn(e); // opponent's turn 1
                NextTurn(e); // opponent's End Phase 1 (count 1) → player's turn
                var wasOnSkull = ek.EquippedTo == skull;
                EquipFamilyRuntime.MoveEquip(e, ek, celtic);
                var moved = wasOnSkull && ek.EquippedTo == celtic;
                var kept = EngineHygieneRuntime.CountdownOf(ek);
                NextTurn(e); // player's End Phase → opponent's turn 2
                NextTurn(e); // opponent's End Phase 2 (count 2) → Celtic destroyed
                Check("Ekibyo Drakmord moved to another monster keeps its count: destroyed at the controller's 2nd End Phase",
                    reached && act && moved && kept == 1 && opp.Graveyard.Contains(celtic) &&
                    opp.TryFindMonster(skull, out _) && p.Hand.Contains(ek),
                    $"act={act} moved={moved} kept={kept} celticGy={opp.Graveyard.Contains(celtic)} " +
                    $"skull={opp.TryFindMonster(skull, out _)} ekHand={p.Hand.Contains(ek)} turn={e.TurnNumber}");
            }
        }

        // ───────────────────────── Forced Requisition ─────────────────────────

        static void ForcedRequisitionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var fr = PlaceSetTrap(e, p, ForcedRequisition, 0);
                var openState = !e.TryActivateSpellTrap(p, fr, fromHand: false) && !fr.FaceUp;
                var target = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, p, LightningVortex);
                var koumori = PutInHand(e, p, Koumori);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, vortex, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(koumori))
                    e.TrySelectEffectTarget(koumori);
                for (var i = 0; i < 8 && e.IsAwaitingResponse &&
                                e.PendingResponse.Timing != ResponseTiming.YouDiscardedFromHand; i++)
                    e.PassResponse();
                var offered = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.YouDiscardedFromHand &&
                              e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(fr);
                var frAct = offered && e.TryActivateSpellTrap(p, fr, fromHand: false);
                PassAll(e);
                Check("Forced Requisition: not activatable without a discard; offered after you discard (Lightning Vortex)",
                    reached && openState && act && p.Graveyard.Contains(koumori) && opp.Graveyard.Contains(target) &&
                    offered && frAct && fr.FaceUp && p.TryFindSpellTrap(fr, out _),
                    $"open={openState} act={act} offered={offered} frAct={frAct} {Window(e)}");

                // Card Destruction: the player discards 2 → after it resolves the opponent discards 2 more.
                foreach (var _ in Enumerable.Range(0, 2)) PutInHand(e, p, Celtic);
                var cd = PutInHand(e, p, CardDestruction);
                opp.Hand.Clear();
                foreach (var _ in Enumerable.Range(0, 3)) PutInHand(e, opp, MysticalElf);
                e.NotifyPublic();
                var cdAct = e.TryActivateSpellTrap(p, cd, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                PassAll(e);
                Check("Forced Requisition: each later discard (Card Destruction, 2 cards) — the opponent discards 2 more",
                    cdAct && fr.FaceUp && p.Hand.Count == 2 && opp.Hand.Count == 1,
                    $"cdAct={cdAct} pHand={p.Hand.Count} oppHand={opp.Hand.Count} {Window(e)}");
            }

            {
                // The AI activates its own Set copy in the window after it discarded (Lightning Vortex's cost).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var fr = PlaceSetTrap(e, opp, ForcedRequisition, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, opp, LightningVortex);
                PutInHand(e, opp, MysticalElf);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(opp, vortex, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                PassAll(e);
                Check("Forced Requisition (AI): activated in its own discard window, no pick left open",
                    reached && act && fr.FaceUp && opp.TryFindSpellTrap(fr, out _) && !e.IsAwaitingEffectTarget &&
                    !e.IsAwaitingResponse, $"act={act} frFaceUp={fr.FaceUp} {Window(e)}");
            }

            {
                // The opponent's face-up copy: a human victim chooses the card; with fewer cards, all of them go.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                FaceUpSpellTrap(e, opp, ForcedRequisition, 0);
                var target = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, opp, LightningVortex);
                PutInHand(e, opp, MysticalElf);
                var keep = PutInHand(e, p, GiantSoldier);
                var toss = PutInHand(e, p, SkullServant);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(opp, vortex, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                var pick = e.PendingActivation;
                var asked = pick != null && pick.Controller == p && pick.AwaitingDrawThenDiscard &&
                            pick.LegalTargets.Contains(keep) && pick.LegalTargets.Contains(toss);
                var picked = asked && e.TrySelectEffectTarget(toss);
                PassAll(e);
                Check("Forced Requisition (opponent's): you discard 1 card of your choice after they discard 1",
                    reached && act && p.Graveyard.Contains(target) && asked && picked && p.Graveyard.Contains(toss) &&
                    p.Hand.Contains(keep) && p.Hand.Count == 1, $"act={act} asked={asked} picked={picked} {Window(e)}");
            }

            {
                // Forced Requisition's discard is an effect of the opponent's card: Minar (lone card, discarded at once)
                // inflicts 1000 to Forced Requisition's controller (Lua DiscardHand REASON_EFFECT, reason player tp).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                FaceUpSpellTrap(e, opp, ForcedRequisition, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, opp, LightningVortex);
                PutInHand(e, opp, MysticalElf);
                p.Hand.Clear();
                var minar = PutInHand(e, p, Minar);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                var act = e.TryActivateSpellTrap(opp, vortex, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                PassAll(e);
                Check("Forced Requisition (opponent's): your lone Minar is discarded by their effect → 1000 to them",
                    reached && act && p.Graveyard.Contains(minar) && opp.LifePoints == lp - 1000,
                    $"act={act} minarGy={p.Graveyard.Contains(minar)} lp {lp}->{opp.LifePoints} {Window(e)}");
            }

            {
                // Human pick path: the victim chooses Minar out of two cards → Minar's trigger.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                FaceUpSpellTrap(e, opp, ForcedRequisition, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, opp, LightningVortex);
                PutInHand(e, opp, MysticalElf);
                p.Hand.Clear();
                var keep = PutInHand(e, p, GiantSoldier);
                var minar = PutInHand(e, p, Minar);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                var act = e.TryActivateSpellTrap(opp, vortex, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                var pick = e.PendingActivation;
                var picked = pick != null && pick.AwaitingDrawThenDiscard && e.TrySelectEffectTarget(minar);
                PassAll(e);
                Check("Forced Requisition (opponent's): you pick Minar to discard → 1000 to them",
                    reached && act && picked && p.Graveyard.Contains(minar) && p.Hand.Contains(keep) &&
                    opp.LifePoints == lp - 1000, $"act={act} picked={picked} lp {lp}->{opp.LifePoints} {Window(e)}");
            }

            {
                // Graceful Charity-style self-discard (your own effect) never counts as the opponent's: covered by
                // DiscardTriggers; here the AI victim of the player's Forced Requisition discards its lone Minar → 1000 to you.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                FaceUpSpellTrap(e, p, ForcedRequisition, 0);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, p, LightningVortex);
                var koumori = PutInHand(e, p, Koumori);
                opp.Hand.Clear();
                var minar = PutInHand(e, opp, Minar);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, vortex, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(koumori))
                    e.TrySelectEffectTarget(koumori);
                PassAll(e);
                e.NotifyPublic();
                PassAll(e);
                Check("Forced Requisition (yours): the AI's lone Minar is discarded by your effect → 1000 to you",
                    reached && act && opp.Graveyard.Contains(minar) && p.LifePoints == lp - 1000,
                    $"act={act} minarGy={opp.Graveyard.Contains(minar)} lp {lp}->{p.LifePoints} {Window(e)}");
            }
        }

        // ───────────────────────── Super Rejuvenation ─────────────────────────

        static void SuperRejuvenationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Tribute a Dragon for a Tribute Summon, discard a Dragon as a cost, activate, draw 2 in the End Phase.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, p, Koumori, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, p, SummonedSkull);
                var summoned = e.TryNormalSummon(p, skull, asSet: false);
                PassAll(e);
                var afterTribute = TextEffects.SuperRejuvenation.CountFor(e, p);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, p, LightningVortex);
                var baby = PutInHand(e, p, BabyDragon);
                PutInHand(e, p, Celtic); // not a Dragon: stays
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, vortex, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(baby))
                    e.TrySelectEffectTarget(baby);
                PassAll(e);
                var afterDiscard = TextEffects.SuperRejuvenation.CountFor(e, p);
                var sr = PutInHand(e, p, SuperRejuvenation);
                var srAct = e.TryActivateSpellTrap(p, sr, fromHand: true);
                PassAll(e);
                var hand = p.Hand.Count;
                var deck = p.Deck.Count;
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Super Rejuvenation: a Tributed Dragon + a discarded Dragon this turn → draws 2 in the End Phase",
                    reached && summoned && afterTribute == 1 && act && afterDiscard == 2 && srAct &&
                    p.Hand.Count == hand + 2 && p.Deck.Count == deck - 2,
                    $"summoned={summoned} counts={afterTribute}/{afterDiscard} srAct={srAct} hand {hand}->{p.Hand.Count} " +
                    $"deck {deck}->{p.Deck.Count}");
            }

            {
                // Nothing counted (a non-Dragon discard, the opponent's Dragon): no draw. The count resets each turn.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var oppDragon = PlaceMonster(e, opp, Koumori, 0, BattlePosition.Attack, true);
                ReleaseEvents.Record(e, oppDragon, opp); // the opponent Tributes its own Dragon
                opp.MonsterZones[0].Occupant = null;
                opp.Graveyard.Add(oppDragon);
                var celtic = PutInHand(e, p, Celtic);
                MonsterEffects.DiscardFromHandByInstance(p, celtic, DiscardCause.Cost);
                var sr = PutInHand(e, p, SuperRejuvenation);
                var srAct = e.TryActivateSpellTrap(p, sr, fromHand: true);
                PassAll(e);
                var hand = p.Hand.Count;
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Super Rejuvenation: a non-Dragon discard and the opponent's Dragon do not count — no draw",
                    reached && srAct && TextEffects.SuperRejuvenation.CountFor(e, opp) == 0 && p.Hand.Count == hand,
                    $"srAct={srAct} hand {hand}->{p.Hand.Count}");
            }
        }

        // ───────────────────────── audit follow-ups ─────────────────────────

        static void RoyalCommandFlipCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            FaceUpSpellTrap(e, o, RoyalCommand, 0);
            var lady = PlaceMonster(e, p, LadyAssailant, 0, BattlePosition.Attack, true);
            var deck = p.Deck.Count;
            var lp = o.LifePoints;
            MonsterEffects.OnFlipSummoned(e, p, lady);
            PassAll(e);
            Check("Royal Command x Lady Assailant of Flames: the FLIP cost is paid (top 3 banished), the effect is negated",
                reached && p.Deck.Count == deck - 3 && p.Banished.Count == 3 && o.LifePoints == lp,
                $"deck {deck}->{p.Deck.Count} banished={p.Banished.Count} lp {lp}->{o.LifePoints}");
        }

        static void KycooEndOfDamageStep(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Kycoo destroys Skull Servant: its battle-damage pick opens; the opponent's Michizure must still
            // get the end-of-Damage-Step window (it destroys Kycoo) — after the pick, never skipped.
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var kycoo = PlaceMonster(e, p, Kycoo, 0, BattlePosition.Attack, true);
            var servant = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
            var michi = PlaceSetTrap(e, opp, Michizure, 0);
            var gyCeltic = InGy(e, opp, Celtic);
            var gySoldier = InGy(e, opp, GiantSoldier);
            e.NotifyPublic();
            var bp = e.TryEnterBattlePhase(p);
            PassAll(e);
            var atk = e.TryAttack(p, kycoo, servant);
            for (var i = 0; i < 6 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++) e.PassResponse();
            var pickOpen = e.IsAwaitingEffectTarget && e.PendingActivation.Card == kycoo &&
                           e.PendingActivation.LegalTargets.Contains(gyCeltic);
            var picked = pickOpen && e.TrySelectEffectTarget(gyCeltic);
            if (e.IsAwaitingEffectTarget && e.PendingActivation?.Card == kycoo) e.CancelEffectTargeting();
            for (var i = 0; i < 8; i++)
            {
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                if (e.IsAwaitingResponse) e.PassResponse();
                else break;
            }

            DrainCombat(e);
            Check("Kycoo (human pick) then the end-of-Damage-Step window: the banish resolves and Michizure destroys Kycoo",
                reached && bp && atk && pickOpen && picked && opp.Banished.Contains(gyCeltic) &&
                opp.Graveyard.Contains(michi) && p.Graveyard.Contains(kycoo) && !e.HasDeclaredAttack,
                $"pick={pickOpen} picked={picked} celticBanished={opp.Banished.Contains(gyCeltic)} " +
                $"michiGy={opp.Graveyard.Contains(michi)} kycooGy={p.Graveyard.Contains(kycoo)} {Window(e)}");
        }

        static void KycooDefending(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The opponent's Skull Servant attacks the player's Kycoo: the human picks 1 of 2 GY monsters and stops; the
            // battle then completes and the opponent's Michizure gets its end-of-Damage-Step window.
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentBattle(e);
            Empty(e);
            var p = e.Player;
            var opp = e.Opponent;
            var kycoo = PlaceMonster(e, p, Kycoo, 0, BattlePosition.Attack, true);
            var servant = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
            var michi = PlaceSetTrap(e, opp, Michizure, 0);
            var gyCeltic = InGy(e, opp, Celtic);
            var gySoldier = InGy(e, opp, GiantSoldier);
            e.NotifyPublic();
            var atk = e.TryAttack(opp, servant, kycoo);
            for (var i = 0; i < 6 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++) e.PassResponse();
            var pickOpen = e.IsAwaitingEffectTarget && e.PendingActivation.Card == kycoo;
            var picked = pickOpen && e.TrySelectEffectTarget(gyCeltic);
            var stillPicking = e.IsAwaitingEffectTarget && e.BattleStep == BattleStep.DamageStep;
            if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
            for (var i = 0; i < 8; i++)
            {
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                if (e.IsAwaitingResponse) e.PassResponse();
                else break;
            }

            DrainCombat(e);
            Check("Kycoo defending (human up-to pick, stopped at 1): the battle waits, then Michizure's end-of-Damage-Step window",
                reached && atk && pickOpen && picked && stillPicking && opp.Banished.Contains(gyCeltic) &&
                opp.Graveyard.Contains(gySoldier) && opp.Graveyard.Contains(servant) && opp.Graveyard.Contains(michi) &&
                p.Graveyard.Contains(kycoo), $"pick={pickOpen} picked={picked} waiting={stillPicking} {Window(e)}");
        }

        static void MichizureAnswersMaryokutai(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The opponent's Pot of Greed; the player Tributes Maryokutai to negate it and chains Michizure to that cost.
            var e = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var mary = PlaceMonster(e, p, Maryokutai, 0, BattlePosition.Attack, true);
            var michi = PlaceSetTrap(e, p, Michizure, 0);
            var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
            var pot = PutInHand(e, opp, PotOfGreed);
            e.NotifyPublic();
            var deck = opp.Deck.Count;
            var act = e.TryActivateSpellTrap(opp, pot, fromHand: true);
            var maryOffered = e.PendingResponse != null && e.PendingResponse.Responder == p &&
                              e.PendingResponse.LegalCards.Contains(mary);
            var answered = maryOffered && e.TryActivateChainResponse(p, mary);
            var michiOffered = false;
            for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
            {
                if (e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(michi))
                {
                    michiOffered = true;
                    break;
                }

                e.PassResponse();
            }

            var michiAct = michiOffered && e.TryActivateSpellTrap(p, michi, fromHand: false);
            if (michiAct && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
            PassAll(e);
            Check("Michizure answers Maryokutai's own Tribute cost (chained to its activation)",
                reached && act && answered && michiOffered && michiAct && opp.Graveyard.Contains(skull) &&
                p.Graveyard.Contains(mary) && opp.Graveyard.Contains(pot) && opp.Deck.Count == deck,
                $"act={act} answered={answered} michiOffered={michiOffered} michiAct={michiAct} " +
                $"skullGy={opp.Graveyard.Contains(skull)} {Window(e)}");

            {
                // Declined in the chain window: the cost send cannot be answered once the chain has resolved.
                var e2 = OpponentMain(db, pDeck, aDeck, out var reached2);
                var p2 = e2.Player;
                var opp2 = e2.Opponent;
                var mary2 = PlaceMonster(e2, p2, Maryokutai, 0, BattlePosition.Attack, true);
                var michi2 = PlaceSetTrap(e2, p2, Michizure, 0);
                PlaceMonster(e2, opp2, SummonedSkull, 0, BattlePosition.Attack, true);
                var pot2 = PutInHand(e2, opp2, PotOfGreed);
                e2.NotifyPublic();
                var act2 = e2.TryActivateSpellTrap(opp2, pot2, fromHand: true);
                var answered2 = e2.IsAwaitingResponse && e2.TryActivateChainResponse(p2, mary2);
                var offeredInChain = false;
                var offeredAfter = false;
                for (var i = 0; i < 10 && e2.IsAwaitingResponse; i++)
                {
                    if (e2.PendingResponse.LegalCards.Contains(michi2))
                    {
                        if (e2.PendingResponse.Timing == ResponseTiming.ChainResponse) offeredInChain = true;
                        else offeredAfter = true;
                    }

                    e2.PassResponse();
                }

                Check("Michizure: declined while Maryokutai's link is on the chain — not offered once the chain resolved",
                    reached2 && act2 && answered2 && offeredInChain && !offeredAfter && !michi2.FaceUp &&
                    p2.Graveyard.Contains(mary2), $"inChain={offeredInChain} after={offeredAfter} {Window(e2)}");
            }

        }
    }
}
