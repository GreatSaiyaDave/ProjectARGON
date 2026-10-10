using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SpellTrapDestroyedResponse" (v142): Gust and Driving Snow — Normal Traps activated when 1 or
    /// more of your Spell / Trap Cards were destroyed and sent from the field to the GY by a card effect your
    /// opponent controls (the <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/> window) — and Weather
    /// Report (FLIP: destroy the opponent's face-up "Swords of Revealing Light"; if one is destroyed, conduct the
    /// Battle Phase twice).
    /// </summary>
    public static class SpellTrapDestroyedResponseRegressionTests
    {
        const int Gust = 73079365;
        const int DrivingSnow = 473469;
        const int WeatherReport = 72053645;

        const int Swords = 72302403;       // Normal Spell that stays face-up
        const int Mst = 5318639;           // Quick-Play: target 1 Spell/Trap on the field; destroy it
        const int PotOfGreed = 55144522;   // Normal Spell (a Set one cannot be chained)
        const int TrapHole = 4206964;      // Normal Trap that needs a Summon (cannot be chained to a Spell)
        const int MagicJammer = 77414722;  // Counter Trap: discard 1; negate a Spell's activation and destroy it
        const int Spiritualism = 15866454; // Return 1 Spell/Trap your opponent controls to the hand
        const int Jinzo = 77585513;        // Traps cannot be activated
        const int Celtic = 91152256;       // Warrior Lv4 1400/1200
        const int GiantSoldier = 13039848; // Rock Lv3 1300/2000
        const int Solemn = 41420027;       // Solemn Judgment: negate a Summon (Counter Trap)
        const int SevenTools = 3819470;    // Seven Tools of the Bandit: negate a Trap's activation and destroy it
        const int GeminiElf = 69140098;    // Normal Monster Lv4 1900/900
        const int Sogen = 86318356;        // Field Spell
        const int ManEaterBug = 54652250;  // FLIP: target 1 monster on the field; destroy it

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("SpellTrapDestroyedResponse: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            GustDuels(db, pDeck, aDeck, Check);
            DrivingSnowDuels(db, pDeck, aDeck, Check);
            WindowFailClosed(db, pDeck, aDeck, Check);
            WeatherReportDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static bool ResponseClause(EffectClause c, string kind) =>
            c != null && c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
            c.Zone == EffectZoneFilter.FieldSpellTraps && c.RequiresTargetChoice && c.IsPsctTarget &&
            c.ExceptThisCard && c.MakesChainLink && !c.StaysOnField &&
            c.ActivationCondition == SpellTrapDestroyedResponseRuntime.YourCardsDestroyedByOpponentEffect &&
            c.ConditionName == kind && c.CheckedAt == ConditionCheckedAt.Activation;

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            foreach (var (id, kind) in new[]
                     {
                         (Gust, SpellTrapDestroyedResponseRuntime.SpellCards),
                         (DrivingSnow, SpellTrapDestroyedResponseRuntime.TrapCards)
                     })
            {
                var def = db.Get(id);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check($"{def?.name}: FullyCompiled, one targeted Activate Destroy (not itself) keyed to your {kind} Cards destroyed",
                    prog != null && prog.FullyCompiled && prog.CanResolveAny && ResponseClause(c, kind),
                    c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
                Check($"{def?.name}: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            var wr = db.Get(WeatherReport);
            var wp = CardTextEffectCompiler.Compile(wr);
            var d = wp != null && wp.ClauseList.Count == 2 ? wp.ClauseList[0] : null;
            var bp = wp != null && wp.ClauseList.Count == 2 ? wp.ClauseList[1] : null;
            Check("Weather Report: FLIP destroy the opponent's face-up Swords, then (if destroyed) Battle Phase twice",
                wp != null && wp.FullyCompiled &&
                d != null && d.Timing == EffectTiming.Flip && d.Action == EffectActionKind.Destroy &&
                d.Zone == EffectZoneFilter.FaceUpNamedSpellTraps && d.Side == EffectSide.Opponent &&
                d.NamedCard == "Swords of Revealing Light" && !d.RequiresTargetChoice &&
                bp != null && bp.Timing == EffectTiming.Flip &&
                bp.Action == EffectActionKind.ConductBattlePhaseTwice && bp.RequiresPreviousClauseHit,
                wp == null ? "null" : string.Join(" | ", wp.ClauseList.Select(EffectVocabulary.Describe)));
            Check("Weather Report: classified Implemented",
                CardEffectStatus.Classify(wr) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(wr).ToString());
            Check("ConductBattlePhaseTwice maps to a shared resolution (ExtraAttack)",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.ConductBattlePhaseTwice }) ==
                EffectResolutionKind.ExtraAttack);
        }

        static CompiledCardProgram CompileText(string text, bool monster = false) =>
            CardTextEffectCompiler.Compile(monster
                ? new CardDef
                {
                    id = 990011, name = "Near-miss Flip", type = "Flip Effect Monster", frameType = "effect",
                    race = "Aqua", attribute = "WATER", level = 4, atk = 950, def = 1500, desc = text
                }
                : new CardDef
                {
                    id = 990012, name = "Near-miss Trap", type = "Trap Card", frameType = "trap", race = "Normal",
                    desc = text
                });

        static bool CompilesToWindowAnswer(string text)
        {
            var prog = CompileText(text);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null &&
                                               c.ActivationCondition ==
                                               SpellTrapDestroyedResponseRuntime.YourCardsDestroyedByOpponentEffect);
        }

        static bool CompilesToBattlePhaseTwice(string text)
        {
            var prog = CompileText(text, monster: true);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ConductBattlePhaseTwice);
        }

        const string GustText =
            "You can activate this card when 1 or more of your Spell Cards are destroyed and sent from the field to " +
            "the Graveyard by a card effect your opponent controls. Destroy 1 Spell or Trap Card on the field.";

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            Check("Near miss: Gust's own text compiles (control)", CompilesToWindowAnswer(GustText));
            Check("Near miss: an extra sentence after Gust's text is not compiled",
                !CompilesToWindowAnswer(GustText + " Then draw 1 card."));
            Check("Near miss: \"Monster Cards\" destroyed is not this template",
                !CompilesToWindowAnswer(GustText.Replace("Spell Cards are", "Monster Cards are")));
            Check("Near miss: destroyed by any card effect (not your opponent's) is not this template",
                !CompilesToWindowAnswer(GustText.Replace(" by a card effect your opponent controls",
                    " by a card effect")));
            Check("Near miss: destroyed by battle is not this template",
                !CompilesToWindowAnswer(GustText.Replace("by a card effect your opponent controls", "by battle")));
            Check("Near miss: destroying 2 Spell or Trap Cards is not this template",
                !CompilesToWindowAnswer(GustText.Replace("Destroy 1 Spell", "Destroy 2 Spell")));

            const string wr =
                "FLIP: Destroys all of your opponent's face-up \"Swords of Revealing Light\" on the field. If " +
                "\"Swords of Revealing Light\" is destroyed, you can perform your Battle Phase twice this turn " +
                "(or your next turn, if activated during your opponent's turn).";
            Check("Near miss: Weather Report's own text compiles (control)", CompilesToBattlePhaseTwice(wr));
            Check("Near miss: a different card named in the condition is not this template",
                !CompilesToBattlePhaseTwice(wr.Replace("If \"Swords of Revealing Light\"", "If \"Dark Hole\"")));
            Check("Near miss: destroying face-up copies on both fields is not this template",
                !CompilesToBattlePhaseTwice(wr.Replace("all of your opponent's face-up", "all face-up")));
            Check("Near miss: \"next turn\" only (no \"this turn\") is not this template",
                !CompilesToBattlePhaseTwice(wr.Replace("twice this turn (or your next turn, if activated during " +
                                                       "your opponent's turn)", "twice during your next turn")));
            Check("Near miss: an unconditional Battle Phase twice is not this template",
                !CompilesToBattlePhaseTwice("FLIP: You can perform your Battle Phase twice this turn."));
        }

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>Advance to <paramref name="who"/>'s Main Phase 1 on game turn ≥ <paramref name="minTurn"/>.</summary>
        static bool ReachMain(DuelEngine engine, DuelistState who, int minTurn)
        {
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn)
                    return true;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool WindowFor(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null &&
            engine.PendingResponse.Timing == ResponseTiming.SpellTrapDestroyedByOpponent &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static bool NoDestroyedWindow(DuelEngine engine) =>
            engine.PendingResponse == null ||
            engine.PendingResponse.Timing != ResponseTiming.SpellTrapDestroyedByOpponent;

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        /// <summary>The AI activates <paramref name="id"/> from its hand (it auto-picks its target); the chain resolves.</summary>
        static bool AiCasts(DuelEngine engine, int id, out CardInstance card)
        {
            card = PutInHand(engine, engine.Opponent, id);
            return engine.TryActivateSpellTrap(engine.Opponent, card, fromHand: true);
        }

        /// <summary>The player activates <paramref name="id"/> from the hand targeting <paramref name="target"/>.</summary>
        static bool PlayerCasts(DuelEngine engine, int id, CardInstance target, out CardInstance card)
        {
            card = PutInHand(engine, engine.Player, id);
            var ok = engine.TryActivateSpellTrap(engine.Player, card, fromHand: true);
            if (ok && engine.IsAwaitingEffectTarget && target != null)
                ok = engine.TrySelectEffectTarget(target);
            return ok;
        }

        // ───────────────────────── Gust ─────────────────────────

        static void GustDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The opponent's Mystical Space Typhoon destroys your Set Spell: Gust is offered right after, and
                // destroys the opponent's Set card (not itself).
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mySpell = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var gust = PlaceSetTrap(engine, p, Gust, 4);
                var theirs = PlaceSetTrap(engine, o, PotOfGreed, 0);
                Check("Gust: cannot be activated at open game state",
                    !engine.CanActivateSpellTrap(p, gust, fromHand: false));
                var cast = AiCasts(engine, Mst, out var mst);
                Check("Gust: offered after the opponent's MST destroys your Set Spell",
                    reached && cast && p.Graveyard.Contains(mySpell) && WindowFor(engine, p, gust) &&
                    engine.Chain.Count == 0,
                    $"reached={reached} cast={cast} spellGy={p.Graveyard.Contains(mySpell)} {Window(engine)}");
                var act = engine.TryActivateSpellTrap(p, gust, fromHand: false);
                var picking = engine.IsAwaitingEffectTarget;
                var legalSelf = picking && engine.PendingActivation.LegalTargets.Contains(gust);
                if (picking) engine.TrySelectEffectTarget(theirs);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Gust: targets 1 Spell/Trap other than itself and destroys it; Gust goes to the GY",
                    act && !legalSelf && o.Graveyard.Contains(theirs) && !o.TryFindSpellTrap(theirs, out _) &&
                    p.Graveyard.Contains(gust) && engine.Chain.Count == 0 && NoDestroyedWindow(engine),
                    $"act={act} picking={picking} self={legalSelf} theirsGy={o.Graveyard.Contains(theirs)} " +
                    $"gustGy={p.Graveyard.Contains(gust)} chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // The legacy Spell/Trap target mirror (SpellTrapEffects.CollectLegalTargets) never offers Gust itself.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var gust = PlaceSetTrap(engine, engine.Player, Gust, 4);
                var other = PlaceSetTrap(engine, engine.Opponent, PotOfGreed, 0);
                var legacy = SpellTrapEffects.CollectLegalTargets(engine, engine.Player,
                    EffectTargetKind.SpellTrapOnField, gust);
                var mstCard = PlaceSetTrap(engine, engine.Player, Mst, 3);
                var mstLegacy = SpellTrapEffects.CollectLegalTargets(engine, engine.Player,
                    EffectTargetKind.SpellTrapOnField, mstCard);
                Check("Gust: the legacy target mirror excludes Gust itself (MST keeps its own offer)",
                    !legacy.Contains(gust) && legacy.Contains(other) && mstLegacy.Contains(other),
                    $"legacy=[{string.Join(",", legacy.Select(c => c?.Name))}]");
            }

            {
                // Passing the window: the moment is gone.
                var engine = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceSetTrap(engine, p, PotOfGreed, 0);
                var gust = PlaceSetTrap(engine, p, Gust, 4);
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                AiCasts(engine, Mst, out _);
                var offered = WindowFor(engine, p, gust);
                engine.PassResponse();
                Check("Gust: after passing the window it can no longer be activated",
                    offered && NoDestroyedWindow(engine) && !engine.CanActivateSpellTrap(p, gust, fromHand: false) &&
                    p.TryFindSpellTrap(gust, out _) && !gust.FaceUp,
                    $"offered={offered} {Window(engine)}");
            }

            {
                // A Counter Trap's "negate the activation, and if you do, destroy it" destroys your Spell by the
                // opponent's effect: Gust answers it once that chain has resolved.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PutInHand(engine, o, Celtic); // Magic Jammer's discard
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 0);
                var hole = PlaceSetTrap(engine, o, TrapHole, 1);
                var gust = PlaceSetTrap(engine, p, Gust, 4);
                var cast = PlayerCasts(engine, PotOfGreed, null, out var pog);
                var negated = o.Graveyard.Contains(jammer) && p.Graveyard.Contains(pog);
                var offered = WindowFor(engine, p, gust);
                var act = offered && engine.TryActivateSpellTrap(p, gust, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(hole);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Gust: answers Magic Jammer destroying your Pot of Greed; destroys the Set Trap Hole",
                    reached && cast && negated && offered && act && o.Graveyard.Contains(hole) &&
                    p.Graveyard.Contains(gust) && NoDestroyedWindow(engine),
                    $"reached={reached} cast={cast} negated={negated} offered={offered} act={act} " +
                    $"holeGy={o.Graveyard.Contains(hole)} {Window(engine)}");
            }

            {
                // The AI answers with Gust when your MST destroys its Set Spell (it targets your card).
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var aiSpell = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var cast = PlayerCasts(engine, Mst, aiSpell, out _);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Gust (AI): answers your MST and destroys your Set card",
                    reached && cast && o.Graveyard.Contains(aiSpell) && o.Graveyard.Contains(aiGust) &&
                    p.Graveyard.Contains(mine) && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"reached={reached} cast={cast} aiSpellGy={o.Graveyard.Contains(aiSpell)} " +
                    $"aiGustGy={o.Graveyard.Contains(aiGust)} mineGy={p.Graveyard.Contains(mine)} {Window(engine)}");
            }

            {
                // Your only Spell/Trap is a Field Spell: the AI's Gust never destroys one of the AI's own Set cards
                // (its target auto-pick prefers your Spell & Trap Zones and would fall back to its own card).
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var aiSpell = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var aiHole = PlaceSetTrap(engine, o, TrapHole, 2);
                var sogen = engine.CreateCardInstance(Sogen);
                sogen.FaceUp = true;
                sogen.SetThisTurn = false;
                p.FieldSpellZone.Occupant = sogen;
                var cast = PlayerCasts(engine, Mst, aiSpell, out _);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                var gustUsed = o.Graveyard.Contains(aiGust);
                Check("Gust (AI): never destroys its own Set card when your only Spell/Trap is a Field Spell",
                    reached && cast && o.Graveyard.Contains(aiSpell) && o.TryFindSpellTrap(aiHole, out _) &&
                    (!gustUsed || p.Graveyard.Contains(sogen)) && engine.Chain.Count == 0 &&
                    !engine.IsAwaitingResponse,
                    $"reached={reached} cast={cast} aiSpellGy={o.Graveyard.Contains(aiSpell)} gustUsed={gustUsed} " +
                    $"holeOnField={o.TryFindSpellTrap(aiHole, out _)} sogenGy={p.Graveyard.Contains(sogen)} {Window(engine)}");
            }
        }

        // ───────────────────────── Driving Snow ─────────────────────────

        static void DrivingSnowDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The AI answers your MST on its Set Trap with Driving Snow, destroying your Set card.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var hole = PlaceSetTrap(engine, o, TrapHole, 0);
                var snow = PlaceSetTrap(engine, o, DrivingSnow, 1);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var cast = PlayerCasts(engine, Mst, hole, out _);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Driving Snow (AI): answers your MST destroying its Trap Hole; destroys your Set card",
                    reached && cast && o.Graveyard.Contains(hole) && o.Graveyard.Contains(snow) &&
                    p.Graveyard.Contains(mine) && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"reached={reached} cast={cast} holeGy={o.Graveyard.Contains(hole)} snowGy={o.Graveyard.Contains(snow)} " +
                    $"mineGy={p.Graveyard.Contains(mine)} {Window(engine)}");
            }

            {
                // Player side: the opponent's MST destroys your Set Trap Hole; Driving Snow (not Gust) is offered.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var hole = PlaceSetTrap(engine, p, TrapHole, 0);
                var gust = PlaceSetTrap(engine, p, Gust, 3);
                var snow = PlaceSetTrap(engine, p, DrivingSnow, 4);
                var theirs = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var cast = AiCasts(engine, Mst, out _);
                var holeGone = p.Graveyard.Contains(hole);
                var offered = WindowFor(engine, p, snow) && !engine.PendingResponse.LegalCards.Contains(gust);
                var act = offered && engine.TryActivateSpellTrap(p, snow, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(theirs);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Driving Snow: offered (Gust is not) when your Trap is destroyed; destroys the target",
                    reached && cast && holeGone && offered && act && o.Graveyard.Contains(theirs) &&
                    p.Graveyard.Contains(snow) && p.TryFindSpellTrap(gust, out _),
                    $"reached={reached} cast={cast} holeGone={holeGone} offered={offered} act={act} " +
                    $"theirsGy={o.Graveyard.Contains(theirs)} {Window(engine)}");
            }

            {
                // Seven Tools of the Bandit negates and destroys your Dust Tornado: Driving Snow answers it and
                // destroys the opponent's Set Trap Hole.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var tools = PlaceSetTrap(engine, o, 3819470, 0); // Seven Tools of the Bandit
                var hole = PlaceSetTrap(engine, o, TrapHole, 1);
                var tornado = PlaceSetTrap(engine, p, 60082869, 0); // Dust Tornado
                var snow = PlaceSetTrap(engine, p, DrivingSnow, 4);
                var oppLp = o.LifePoints;
                var act = engine.TryActivateSpellTrap(p, tornado, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(hole);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing == ResponseTiming.ChainResponse; i++)
                    engine.PassResponse();
                var negated = o.Graveyard.Contains(tools) && p.Graveyard.Contains(tornado) &&
                              o.LifePoints == oppLp - 1000 && o.TryFindSpellTrap(hole, out _);
                var offered = WindowFor(engine, p, snow);
                var snowAct = offered && engine.TryActivateSpellTrap(p, snow, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(hole);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Driving Snow: answers Seven Tools of the Bandit destroying your Dust Tornado",
                    reached && act && negated && offered && snowAct && o.Graveyard.Contains(hole) &&
                    p.Graveyard.Contains(snow) && NoDestroyedWindow(engine),
                    $"reached={reached} act={act} negated={negated} offered={offered} snowAct={snowAct} " +
                    $"holeGy={o.Graveyard.Contains(hole)} {Window(engine)}");
            }

            {
                // Jinzo: Trap Cards cannot be activated — Driving Snow is not offered.
                var engine = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceMonster(engine, o, Jinzo, 0, BattlePosition.Attack, true);
                var hole = PlaceSetTrap(engine, p, TrapHole, 0);
                PlaceSetTrap(engine, p, DrivingSnow, 4);
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                AiCasts(engine, Mst, out _);
                Check("Driving Snow: not offered while Jinzo stops Trap activation",
                    p.Graveyard.Contains(hole) && NoDestroyedWindow(engine), Window(engine));
            }
        }

        // ───────────────────────── fail closed ─────────────────────────

        static void WindowFailClosed(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Your own MST destroying your own Set Spell: not an opponent's card effect.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mySpell = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var gust = PlaceSetTrap(engine, p, Gust, 4);
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                PlayerCasts(engine, Mst, mySpell, out _);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing != ResponseTiming.SpellTrapDestroyedByOpponent; i++)
                    engine.PassResponse();
                Check("Gust: not offered when your own card effect destroyed your Spell",
                    p.Graveyard.Contains(mySpell) && NoDestroyedWindow(engine) &&
                    !engine.CanActivateSpellTrap(p, gust, fromHand: false), Window(engine));
            }

            {
                // A Trap destroyed is not a Spell destroyed: Gust alone opens no window.
                var engine = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var hole = PlaceSetTrap(engine, p, TrapHole, 0);
                PlaceSetTrap(engine, p, Gust, 4);
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                AiCasts(engine, Mst, out _);
                Check("Gust: not offered when only a Trap Card of yours was destroyed",
                    p.Graveyard.Contains(hole) && NoDestroyedWindow(engine), Window(engine));
            }

            {
                // Returned to the hand (Spiritualism) is not destroyed.
                var engine = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mySpell = PlaceSetTrap(engine, p, PotOfGreed, 0);
                PlaceSetTrap(engine, p, Gust, 4);
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                var cast = AiCasts(engine, Spiritualism, out _);
                var bounced = p.Hand.Contains(mySpell) || !p.TryFindSpellTrap(mySpell, out _);
                Check("Gust: not offered when your Spell is returned to the hand (not destroyed)",
                    cast && bounced && !p.Graveyard.Contains(mySpell) && NoDestroyedWindow(engine),
                    $"cast={cast} bounced={bounced} {Window(engine)}");
            }

            {
                // Nothing to target but Gust itself: no window.
                var engine = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mySpell = PlaceSetTrap(engine, p, PotOfGreed, 0);
                PlaceSetTrap(engine, p, Gust, 4);
                AiCasts(engine, Mst, out _);
                Check("Gust: not offered when the only Spell/Trap left to target is Gust itself",
                    p.Graveyard.Contains(mySpell) && NoDestroyedWindow(engine), Window(engine));
            }

            {
                // Your MST answers the opponent's attack declaration and destroys its Set Spell. The attack then goes
                // on to the Damage Step before a window could open: the opponent's Gust is never offered afterwards.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var attacker = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var aiSpell = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var mst = PlaceSetTrap(engine, p, Mst, 0);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 1);
                var lp0 = p.LifePoints;
                engine.TryEnterBattlePhase(o);
                // The Set MST is offered at the start of the opponent's Battle Phase too: pass that one.
                if (engine.IsAwaitingResponse &&
                    engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState)
                    engine.PassResponse();
                var atk = engine.TryAttack(o, attacker, null);
                var attackWindow = engine.PendingResponse != null &&
                                   engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                                   engine.PendingResponse.LegalCards.Contains(mst);
                var answered = attackWindow && engine.TryActivateSpellTrap(p, mst, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(aiSpell);
                DrainCombat(engine);
                Check("Gust: no late window once the attack has moved on to the Damage Step",
                    reached && atk && attackWindow && answered && o.Graveyard.Contains(aiSpell) &&
                    o.TryFindSpellTrap(aiGust, out _) && !aiGust.FaceUp && p.TryFindSpellTrap(mine, out _) &&
                    p.LifePoints == lp0 - 1400 && NoDestroyedWindow(engine),
                    $"reached={reached} atk={atk} window={attackWindow} answered={answered} " +
                    $"aiSpellGy={o.Graveyard.Contains(aiSpell)} gustSet={o.TryFindSpellTrap(aiGust, out _)} " +
                    $"lp {lp0}->{p.LifePoints} {Window(engine)}");
            }

            {
                // Missed timing: your MST chained to the opponent's Pot of Greed destroys its Continuous Spell, then
                // Pot of Greed resolves — the destruction is not the last thing to happen, so its Gust stays Set.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var messenger = PlaceFaceUpSpellTrap(engine, o, 44656491, 0); // Messenger of Peace
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var mst = PlaceSetTrap(engine, p, Mst, 0);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 1);
                var handBefore = o.HandCount;
                var cast = AiCasts(engine, PotOfGreed, out _);
                var chainWindow = engine.PendingResponse != null &&
                                  engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                                  engine.PendingResponse.LegalCards.Contains(mst);
                var chained = chainWindow && engine.TryActivateSpellTrap(p, mst, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(messenger);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing == ResponseTiming.ChainResponse; i++)
                    engine.PassResponse();
                Check("Gust: missed timing — a later Chain Link resolved after the destruction",
                    reached && cast && chained && o.Graveyard.Contains(messenger) && o.HandCount == handBefore + 2 &&
                    o.TryFindSpellTrap(aiGust, out _) && !aiGust.FaceUp && p.TryFindSpellTrap(mine, out _) &&
                    NoDestroyedWindow(engine),
                    $"reached={reached} cast={cast} window={chainWindow} chained={chained} " +
                    $"messengerGy={o.Graveyard.Contains(messenger)} hand={handBefore}->{o.HandCount} " +
                    $"gustSet={o.TryFindSpellTrap(aiGust, out _)} {Window(engine)}");
            }

            {
                // Missed timing through a Summon: your Normal Summon meets the opponent's Solemn Judgment, you chain
                // Seven Tools of the Bandit (negate Solemn, and destroy it). Solemn's link is negated, so the Summon
                // then succeeds — that is the last thing to happen, and the opponent's Driving Snow stays Set.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var solemn = PlaceSetTrap(engine, o, Solemn, 0);
                var snow = PlaceSetTrap(engine, o, DrivingSnow, 1);
                var tools = PlaceSetTrap(engine, p, SevenTools, 0);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 1);
                var elf = PutInHand(engine, p, GeminiElf);
                var ok = engine.TryNormalSummon(p, elf, asSet: false);
                var chainWindow = ok && engine.PendingResponse != null &&
                                  engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                                  engine.PendingResponse.Responder == p &&
                                  engine.PendingResponse.LegalCards.Contains(tools);
                var chained = chainWindow && engine.TryActivateSpellTrap(p, tools, fromHand: false);
                for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Driving Snow: missed timing — your Summon succeeds after Seven Tools destroyed the opponent's Solemn",
                    chained && p.TryFindMonster(elf, out _) && o.Graveyard.Contains(solemn) &&
                    o.TryFindSpellTrap(snow, out _) && !snow.FaceUp && p.TryFindSpellTrap(mine, out _) &&
                    NoDestroyedWindow(engine) && engine.Chain.Count == 0,
                    $"ok={ok} chainWindow={chainWindow} chained={chained} elf={p.TryFindMonster(elf, out _)} " +
                    $"solemnGy={o.Graveyard.Contains(solemn)} snowSet={o.TryFindSpellTrap(snow, out _)} " +
                    $"mine={p.TryFindSpellTrap(mine, out _)} {Window(engine)}");
            }

            {
                // The mirror: the opponent's Summon meets your Solemn Judgment, the opponent chains Seven Tools. The
                // Summon succeeds last, so your Driving Snow is not offered.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var solemn = PlaceSetTrap(engine, p, Solemn, 0);
                var snow = PlaceSetTrap(engine, p, DrivingSnow, 1);
                var tools = PlaceSetTrap(engine, o, SevenTools, 0);
                PlaceSetTrap(engine, o, PotOfGreed, 1);
                var elf = PutInHand(engine, o, GeminiElf);
                var ok = reached && engine.TryNormalSummon(o, elf, asSet: false);
                var negWindow = ok && engine.PendingResponse != null &&
                                engine.PendingResponse.Timing == ResponseTiming.SummonNegation &&
                                engine.PendingResponse.Responder == p;
                var act = negWindow && engine.TryActivateSpellTrap(p, solemn, fromHand: false);
                var toolsUsed = o.Graveyard.Contains(tools);
                for (var i = 0; i < 6 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing != ResponseTiming.SpellTrapDestroyedByOpponent; i++)
                    engine.PassResponse();
                Check("Driving Snow: not offered once the opponent's Summon succeeds after its Seven Tools destroyed your Solemn",
                    act && toolsUsed && p.Graveyard.Contains(solemn) && o.TryFindMonster(elf, out _) &&
                    NoDestroyedWindow(engine) && p.TryFindSpellTrap(snow, out _) && !snow.FaceUp,
                    $"reached={reached} ok={ok} negWindow={negWindow} act={act} toolsUsed={toolsUsed} " +
                    $"elf={o.TryFindMonster(elf, out _)} {Window(engine)}");
            }

            {
                // In the chain window of the opponent's MST the Set Gust is not offered (nothing is destroyed yet;
                // a Set Waboku is); once the MST has destroyed your face-up Swords, Gust is.
                var engine = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var faceUp = PlaceFaceUpSpellTrap(engine, p, Swords, 0);
                var waboku = PlaceSetTrap(engine, p, 12607053, 3);
                var gust = PlaceSetTrap(engine, p, Gust, 4);
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                var cast = AiCasts(engine, Mst, out _);
                var chainWindow = engine.PendingResponse != null &&
                                  engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                                  engine.PendingResponse.Responder == p;
                var chainOffersGust = chainWindow && engine.PendingResponse.LegalCards.Contains(gust);
                var chainOffersWaboku = chainWindow && engine.PendingResponse.LegalCards.Contains(waboku);
                if (chainWindow) engine.PassResponse();
                Check("Gust: never offered as a chain response; offered once the destruction happened",
                    cast && chainWindow && chainOffersWaboku && !chainOffersGust && p.Graveyard.Contains(faceUp) &&
                    WindowFor(engine, p, gust),
                    $"cast={cast} chainWindow={chainWindow} gust={chainOffersGust} waboku={chainOffersWaboku} " +
                    $"swordsGy={p.Graveyard.Contains(faceUp)} {Window(engine)}");
            }
        }

        // ───────────────────────── Weather Report ─────────────────────────

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker)
        {
            if (!engine.CanAttack(who, attacker)) return false;
            if (!engine.TryAttack(who, attacker, null)) return false;
            DrainCombat(engine);
            return true;
        }

        static void WeatherReportDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Your turn: Flip Summon Weather Report while the opponent has a face-up Swords → Swords destroyed,
                // two Battle Phases this turn; your monsters attack in both.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var swords = PlaceFaceUpSpellTrap(engine, o, Swords, 0);
                var wr = PlaceMonster(engine, p, WeatherReport, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, wr);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                var turn = engine.TurnNumber;
                Check("Weather Report: Flip destroys the opponent's face-up Swords and grants two Battle Phases this turn",
                    reached && flipped && wr.FaceUp && o.Graveyard.Contains(swords) &&
                    p.BattlePhaseTwiceTurns.Contains(turn),
                    $"reached={reached} flipped={flipped} swordsGy={o.Graveyard.Contains(swords)} " +
                    $"turns=[{string.Join(",", p.BattlePhaseTwiceTurns)}] turn={turn}");

                var lp0 = o.LifePoints;
                var bp1 = engine.TryEnterBattlePhase(p);
                var a1 = Attack(engine, p, celtic);
                var mp2 = engine.TryEnterMainPhase2(p);
                var canSecond = engine.CanConductBattlePhase;
                var noPosChange = !engine.CanChangePosition(p, celtic);
                var bp2 = engine.TryEnterBattlePhase(p);
                var a2 = Attack(engine, p, celtic);
                var lp2 = o.LifePoints;
                Check("Weather Report: a monster that attacked in the first Battle Phase attacks again in the second",
                    bp1 && a1 && mp2 && canSecond && noPosChange && bp2 && a2 && lp2 == lp0 - 2 * 1400,
                    $"bp1={bp1} a1={a1} mp2={mp2} canSecond={canSecond} noPos={noPosChange} bp2={bp2} a2={a2} " +
                    $"lp {lp0}->{lp2}");
                var mp2b = engine.TryEnterMainPhase2(p);
                Check("Weather Report: no third Battle Phase",
                    mp2b && !engine.CanConductBattlePhase && !engine.TryEnterBattlePhase(p) &&
                    engine.Phase == DuelPhase.Main2 && !engine.CanChangePosition(p, celtic),
                    $"mp2b={mp2b} phase={engine.Phase}");
                EndTurn(engine);
                ReachMain(engine, p, turn + 2);
                engine.TryEnterBattlePhase(p);
                engine.TryEnterMainPhase2(p);
                Check("Weather Report: the grant was for that turn only",
                    engine.TurnNumber == turn + 2 && engine.Phase == DuelPhase.Main2 && !engine.CanConductBattlePhase,
                    $"turn={engine.TurnNumber} phase={engine.Phase}");
            }

            {
                // No Swords to destroy (only your own face-up Swords): nothing destroyed, no second Battle Phase.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var mine = PlaceFaceUpSpellTrap(engine, p, Swords, 0);
                var wr = PlaceMonster(engine, p, WeatherReport, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, wr);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.TryEnterBattlePhase(p);
                engine.TryEnterMainPhase2(p);
                Check("Weather Report: your own Swords is not destroyed and no second Battle Phase is granted",
                    flipped && p.TryFindSpellTrap(mine, out _) && p.BattlePhaseTwiceTurns.Count == 0 &&
                    engine.Phase == DuelPhase.Main2 && !engine.CanConductBattlePhase,
                    $"flipped={flipped} mineOnField={p.TryFindSpellTrap(mine, out _)} phase={engine.Phase}");
            }

            {
                // Flipped by the opponent's attack during their turn: the two Battle Phases are on your next turn.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var swords = PlaceFaceUpSpellTrap(engine, o, Swords, 0);
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var attacker = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var wr = PlaceMonster(engine, p, WeatherReport, 0, BattlePosition.Defense, false);
                var soldier = PlaceMonster(engine, p, GiantSoldier, 1, BattlePosition.Attack, true);
                var oppTurn = engine.TurnNumber;
                engine.TryEnterBattlePhase(o);
                var atk = engine.TryAttack(o, attacker, wr);
                DrainCombat(engine);
                Check("Weather Report: flipped by an attack on the opponent's turn — Swords destroyed, grant next turn",
                    reached && atk && wr.FaceUp && o.Graveyard.Contains(swords) &&
                    p.BattlePhaseTwiceTurns.Contains(oppTurn + 1) && !p.BattlePhaseTwiceTurns.Contains(oppTurn),
                    $"reached={reached} atk={atk} wrUp={wr.FaceUp} swordsGy={o.Graveyard.Contains(swords)} " +
                    $"turns=[{string.Join(",", p.BattlePhaseTwiceTurns)}] oppTurn={oppTurn}");
                Check("Gust: a Spell destroyed in the Damage Step (Flip by battle) is never answered",
                    o.TryFindSpellTrap(aiGust, out _) && !aiGust.FaceUp && p.TryFindSpellTrap(mine, out _) &&
                    NoDestroyedWindow(engine),
                    $"aiGustSet={o.TryFindSpellTrap(aiGust, out _)} mineOnField={p.TryFindSpellTrap(mine, out _)} {Window(engine)}");
                p.SpellTrapZones[0].Occupant = null;
                o.SpellTrapZones[1].Occupant = null;
                EndTurn(engine);
                var myTurn = ReachMain(engine, p, oppTurn + 1);
                o.MonsterZones[0].Occupant = null; // clear the way for direct attacks
                var lp0 = o.LifePoints;
                var bp1 = engine.TryEnterBattlePhase(p);
                var a1 = Attack(engine, p, soldier);
                engine.TryEnterMainPhase2(p);
                var bp2 = engine.CanConductBattlePhase && engine.TryEnterBattlePhase(p);
                var a2 = Attack(engine, p, soldier);
                Check("Weather Report: on your next turn you conduct two Battle Phases",
                    myTurn && engine.TurnNumber == oppTurn + 1 && bp1 && a1 && bp2 && a2 &&
                    o.LifePoints == lp0 - 2 * 1300,
                    $"myTurn={myTurn} turn={engine.TurnNumber} bp1={bp1} a1={a1} bp2={bp2} a2={a2} lp {lp0}->{o.LifePoints}");
            }

            {
                // Runtime helper only (no Flip is played; no card can flip your own Weather Report during your own
                // Battle Phase in this pool): GrantBattlePhaseTwice resolved during your own Battle Phase applies to
                // your next turn, not this one (Lua bpcon).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var t = engine.TurnNumber;
                engine.TryEnterBattlePhase(p);
                SpellTrapDestroyedResponseRuntime.GrantBattlePhaseTwice(engine, p, null);
                engine.TryEnterMainPhase2(p);
                Check("GrantBattlePhaseTwice helper: called during your own Battle Phase → your next turn (Lua bpcon)",
                    p.BattlePhaseTwiceTurns.Contains(t + 2) && !p.BattlePhaseTwiceTurns.Contains(t) &&
                    engine.Phase == DuelPhase.Main2 && !engine.CanConductBattlePhase,
                    $"turns=[{string.Join(",", p.BattlePhaseTwiceTurns)}] t={t}");
            }

            {
                // The opponent's Gust answers your Weather Report destroying its Swords (a nested window during the
                // Flip Summon); the Flip Summon still completes and the grant stands.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var swords = PlaceFaceUpSpellTrap(engine, o, Swords, 0);
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var wr = PlaceMonster(engine, p, WeatherReport, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, wr);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Weather Report vs Gust: the opponent's Gust answers the destroyed Swords; play continues",
                    flipped && wr.FaceUp && o.Graveyard.Contains(swords) && o.Graveyard.Contains(aiGust) &&
                    p.Graveyard.Contains(mine) && p.BattlePhaseTwiceTurns.Contains(engine.TurnNumber) &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget &&
                    engine.Phase == DuelPhase.Main1,
                    $"flipped={flipped} swordsGy={o.Graveyard.Contains(swords)} gustGy={o.Graveyard.Contains(aiGust)} " +
                    $"mineGy={p.Graveyard.Contains(mine)} chain={engine.Chain.Count} {Window(engine)} phase={engine.Phase}");
            }

            {
                // The opponent Flip Summons Weather Report and destroys your Swords: your Gust window opens during the
                // Flip Summon; after you answer, the Summon completes (no stuck window or chain).
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var swords = PlaceFaceUpSpellTrap(engine, p, Swords, 0);
                var gust = PlaceSetTrap(engine, p, Gust, 1);
                var theirs = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var wr = PlaceMonster(engine, o, WeatherReport, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(o, wr);
                var offered = WindowFor(engine, p, gust);
                var act = offered && engine.TryActivateSpellTrap(p, gust, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(theirs);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Weather Report (opponent's) vs your Gust: answered mid-Flip Summon, then play continues",
                    reached && flipped && wr.FaceUp && p.Graveyard.Contains(swords) && offered && act &&
                    o.Graveyard.Contains(theirs) && p.Graveyard.Contains(gust) &&
                    o.BattlePhaseTwiceTurns.Contains(engine.TurnNumber) && engine.Chain.Count == 0 &&
                    !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget && engine.Phase == DuelPhase.Main1,
                    $"reached={reached} flipped={flipped} swordsGy={p.Graveyard.Contains(swords)} offered={offered} " +
                    $"act={act} theirsGy={o.Graveyard.Contains(theirs)} chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // SimpleAi given a grant (set directly; the next block reaches it through a real Flip Summon): the AI
                // uses its second Battle Phase.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                o.BattlePhaseTwiceTurns.Add(engine.TurnNumber);
                var lp0 = p.LifePoints;
                var finished = RunAiTurn(engine, 400);
                Check("SimpleAi given a Battle-Phase-twice grant: attacks in both Battle Phases",
                    reached && finished && p.LifePoints == lp0 - 2 * 1400,
                    $"reached={reached} finished={finished} lp {lp0}->{p.LifePoints} turn={engine.TurnNumber}");
            }

            {
                // End to end for the AI: it Flip Summons Weather Report while you control a face-up Swords, the FLIP
                // effect destroys Swords, and SimpleAi then runs both Battle Phases.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var swords = PlaceFaceUpSpellTrap(engine, p, Swords, 0);
                var wr = PlaceMonster(engine, o, WeatherReport, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var flipped = engine.TryFlipSummon(o, wr);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                var granted = o.BattlePhaseTwiceTurns.Contains(turn);
                var lp0 = p.LifePoints;
                var finished = RunAiTurn(engine, 400);
                var dealt = lp0 - p.LifePoints;
                Check("Weather Report (AI): Flip Summoned, destroys your Swords, then SimpleAi attacks in two Battle Phases",
                    reached && flipped && p.Graveyard.Contains(swords) && granted && finished &&
                    o.SecondBattlePhaseTurn == turn && dealt > 1400 + 950,
                    $"reached={reached} flipped={flipped} swordsGy={p.Graveyard.Contains(swords)} granted={granted} " +
                    $"finished={finished} second={o.SecondBattlePhaseTurn} turn={turn} dealt={dealt}");
            }

            {
                // The classic answer: the opponent's Swords of Revealing Light flips your face-down Weather Report.
                // Its FLIP effect activates once Swords has resolved (Swords is on the field by then): Swords is
                // destroyed, your next turn has two Battle Phases, and the opponent's Gust answers the destruction.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var wr = PlaceMonster(engine, p, WeatherReport, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var mine = PlaceSetTrap(engine, p, PotOfGreed, 0);
                var aiGust = PlaceSetTrap(engine, o, Gust, 1);
                var oppTurn = engine.TurnNumber;
                var cast = AiCasts(engine, Swords, out var swords);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Responder == p; i++)
                    engine.PassResponse();
                Check("Weather Report: flipped by the opponent's Swords — its FLIP destroys Swords after Swords resolved",
                    reached && cast && wr.FaceUp && wr.Position == BattlePosition.Defense &&
                    o.Graveyard.Contains(swords) && !o.TryFindSpellTrap(swords, out _) &&
                    p.BattlePhaseTwiceTurns.Contains(oppTurn + 1) && !p.BattlePhaseTwiceTurns.Contains(oppTurn),
                    $"reached={reached} cast={cast} wrUp={wr.FaceUp} swordsGy={o.Graveyard.Contains(swords)} " +
                    $"swordsOnField={o.TryFindSpellTrap(swords, out _)} turns=[{string.Join(",", p.BattlePhaseTwiceTurns)}] " +
                    $"oppTurn={oppTurn} {Window(engine)}");
                Check("Weather Report vs Swords: the opponent's Gust answers the destroyed Swords; play settles",
                    o.Graveyard.Contains(aiGust) && p.Graveyard.Contains(mine) && engine.Chain.Count == 0 &&
                    !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget && engine.Phase == DuelPhase.Main1,
                    $"gustGy={o.Graveyard.Contains(aiGust)} mineGy={p.Graveyard.Contains(mine)} " +
                    $"chain={engine.Chain.Count} {Window(engine)} phase={engine.Phase}");
                EndTurn(engine);
                var myTurn = ReachMain(engine, p, oppTurn + 1);
                var lp0 = o.LifePoints;
                var bp1 = engine.TryEnterBattlePhase(p);
                var a1 = Attack(engine, p, celtic);
                engine.TryEnterMainPhase2(p);
                var bp2 = engine.CanConductBattlePhase && engine.TryEnterBattlePhase(p);
                var a2 = Attack(engine, p, celtic);
                Check("Weather Report vs Swords: on your next turn you attack in two Battle Phases",
                    myTurn && bp1 && a1 && bp2 && a2 && o.LifePoints == lp0 - 2 * 1400,
                    $"myTurn={myTurn} bp1={bp1} a1={a1} bp2={bp2} a2={a2} lp {lp0}->{o.LifePoints}");
            }

            {
                // The mirror, with a human answer: your Swords flips the opponent's face-down Weather Report; its FLIP
                // destroys your Swords (after Swords resolved), the grant is for the opponent's next turn, and you are
                // offered Gust.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var wr = PlaceMonster(engine, o, WeatherReport, 0, BattlePosition.Defense, false);
                var gust = PlaceSetTrap(engine, p, Gust, 1);
                var theirs = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var myTurn = engine.TurnNumber;
                var cast = PlayerCasts(engine, Swords, null, out var swords);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing == ResponseTiming.ChainResponse; i++)
                    engine.PassResponse();
                var offered = WindowFor(engine, p, gust);
                Check("Weather Report (opponent's) flipped by your Swords: Swords destroyed, grant on their next turn, Gust offered",
                    reached && cast && wr.FaceUp && p.Graveyard.Contains(swords) &&
                    o.BattlePhaseTwiceTurns.Contains(myTurn + 1) && offered,
                    $"reached={reached} cast={cast} wrUp={wr.FaceUp} swordsGy={p.Graveyard.Contains(swords)} " +
                    $"turns=[{string.Join(",", o.BattlePhaseTwiceTurns)}] myTurn={myTurn} {Window(engine)}");
                var act = offered && engine.TryActivateSpellTrap(p, gust, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(theirs);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Weather Report vs your Swords: your Gust resolves and play settles",
                    act && o.Graveyard.Contains(theirs) && p.Graveyard.Contains(gust) && engine.Chain.Count == 0 &&
                    !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget && engine.Phase == DuelPhase.Main1,
                    $"act={act} theirsGy={o.Graveyard.Contains(theirs)} chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // Any FLIP monster flipped by Swords activates its FLIP effect (not only Weather Report): the
                // opponent's face-down Man-Eater Bug destroys your monster once your Swords has resolved.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var bug = PlaceMonster(engine, o, ManEaterBug, 0, BattlePosition.Defense, false);
                var soldier = PlaceMonster(engine, p, GiantSoldier, 0, BattlePosition.Attack, true);
                var cast = PlayerCasts(engine, Swords, null, out var swords);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing == ResponseTiming.ChainResponse; i++)
                    engine.PassResponse();
                Check("Swords of Revealing Light: a flipped Man-Eater Bug activates its FLIP effect; Swords stays",
                    reached && cast && bug.FaceUp && p.Graveyard.Contains(soldier) &&
                    p.TryFindSpellTrap(swords, out _) && swords.FaceUp && swords.ContinuousTurnsRemaining == 3 &&
                    !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget,
                    $"reached={reached} cast={cast} bugUp={bug.FaceUp} soldierGy={p.Graveyard.Contains(soldier)} " +
                    $"swordsOnField={p.TryFindSpellTrap(swords, out _)} turns={swords.ContinuousTurnsRemaining} {Window(engine)}");
            }

            {
                // A human FLIP target pick after the opponent's Swords: your Man-Eater Bug asks for its target once
                // Swords has resolved, and destroys the opponent's monster.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var bug = PlaceMonster(engine, p, ManEaterBug, 0, BattlePosition.Defense, false);
                var soldier = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var cast = AiCasts(engine, Swords, out var swords);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse &&
                                engine.PendingResponse.Timing == ResponseTiming.ChainResponse; i++)
                    engine.PassResponse();
                var picking = engine.IsAwaitingEffectTarget && engine.PendingActivation.Controller == p &&
                              engine.PendingActivation.LegalTargets.Contains(soldier);
                var swordsFirst = o.TryFindSpellTrap(swords, out _) && swords.FaceUp;
                if (picking) engine.TrySelectEffectTarget(soldier);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Swords of Revealing Light (opponent's): your flipped Man-Eater Bug picks its target after Swords resolved",
                    reached && cast && bug.FaceUp && picking && swordsFirst && o.Graveyard.Contains(soldier) &&
                    !engine.IsAwaitingEffectTarget && !engine.IsAwaitingResponse && engine.Chain.Count == 0,
                    $"reached={reached} cast={cast} bugUp={bug.FaceUp} picking={picking} swordsFirst={swordsFirst} " +
                    $"soldierGy={o.Graveyard.Contains(soldier)} {Window(engine)}");
            }
        }

        static bool RunAiTurn(DuelEngine e, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }
    }
}
