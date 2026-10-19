using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CrvDestroyedTriggersAndFloats batch: Poison Draw Frog, T.A.D.P.O.L.E., UFOroid, Hydrogeddon, Wroughtweiler, Winged
    /// Kuriboh (both prints) and Giant Kozaky. Compile shapes, near misses, and mini-duels through the engine entry points
    /// (TryAttack, TryActivateSpellTrap with Dark Hole, TryNormalSummon, TryTakeControl, DestroyMonsterPublic, the human
    /// card picks). Runtime: <see cref="CrvDestroyedTriggersAndFloatsRuntime"/>.
    /// </summary>
    public static class CrvDestroyedTriggersAndFloatsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int PoisonDrawFrog = 56840658;    // Aqua Lv2 100/100
        const int Tadpole = 10456559;           // Aqua Lv1 0/0
        const int Uforoid = 7602840;            // Machine Lv6 1200/1200
        const int Hydrogeddon = 22587018;       // Dinosaur Lv4 1600/1000
        const int Wroughtweiler = 6480253;      // Machine Lv3 800/1200
        const int WingedKuribohGy = 57116033;   // "… sent to the GY"
        const int WingedKuribohGraveyard = 57116034; // "… sent to the Graveyard"
        const int GiantKozaky = 58185394;       // Fiend Lv4 2500/2400
        const int Kozaky = 99171160;            // Fiend Normal Lv1 400/400

        const int Avian = 21844577;             // Elemental HERO Avian, Normal Lv3 1000/1000
        const int Burstinatrix = 58932616;      // Elemental HERO Burstinatrix, Normal Lv3 1200/800
        const int FlameWingman = 35809262;      // Elemental HERO Flame Wingman, Fusion 2100/1200
        const int Polymerization = 24094653;
        const int Mechanicalchaser = 7359741;   // Machine Normal 1850/800
        const int CannonSoldier = 11384280;     // Machine Lv4 1400/1300
        const int Celtic = 91152256;            // Warrior Normal Lv4 1400/1200
        const int AxeRaider = 48305365;         // Warrior Normal Lv4 1700/1150
        const int DarkBlade = 11321183;         // Warrior Normal Lv4 1800/1500
        const int BasicInsect = 89091579;       // Insect Normal Lv2 500/700
        const int DarkHole = 53129443;
        const int Zeradias = 12171659;          // Zeradias, Herald of Heaven, Fairy Lv4 2100/800 (outside this group)
        const int Sanctuary = 56433456;         // The Sanctuary in the Sky, Field Spell

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("CrvDestroyedTriggersAndFloats: " + name, ok, detail ?? "");
            Shapes(db, Check);
            NearMisses(Check);
            PoisonDrawFrogDuels(db, pDeck, aDeck, Check);
            TadpoleDuels(db, pDeck, aDeck, Check);
            UforoidDuels(db, pDeck, aDeck, Check);
            HydrogeddonDuels(db, pDeck, aDeck, Check);
            WroughtweilerDuels(db, pDeck, aDeck, Check);
            WingedKuribohDuels(db, pDeck, aDeck, Check);
            GiantKozakyDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuelEngine, System.Collections.Generic.List<string>>
            s_logs = new();

        static DuelEngine Watch(DuelEngine e)
        {
            var lines = s_logs.GetValue(e, _ => new System.Collections.Generic.List<string>());
            e.OnLog += lines.Add;
            return e;
        }

        static string State(DuelEngine e)
        {
            var lines = s_logs.TryGetValue(e, out var l) ? l : new System.Collections.Generic.List<string>();
            return $"pLP={e.Player.LifePoints} oLP={e.Opponent.LifePoints} pHand={e.Player.Hand.Count} " +
                   $"oHand={e.Opponent.Hand.Count} chain={e.Chain.Count} resp={e.PendingResponse?.Timing} " +
                   $"pend={(e.PendingActivation != null)} " +
                   $"log={string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - 8)))}";
        }

        /// <summary>The Player's Main Phase 1 of turn 3+, empty boards and hands.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The AI's Main Phase (turn 2+), empty boards and hands.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            engine.ClearPendingResponse();
            return engine;
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
            engine.NotifyPublic();
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The AI's Battle Phase: <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static CardInstance FaceUp(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);

        static CardInstance FaceDown(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        static CardInstance InGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.Graveyard.Add(c);
            return c;
        }

        /// <summary>Replace <paramref name="who"/>'s Deck with <paramref name="ids"/> (top first) and 6 Celtic Guardians.</summary>
        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            foreach (var id in ids) who.Deck.Add(id);
            for (var i = 0; i < 6; i++) who.Deck.Add(Celtic);
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        static CardInstance PlaceField(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static int HandCount(DuelistState who, int id) => who.Hand.Count(c => c != null && c.CardId == id);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram Compile(int id, string name, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = name,
                type = "Effect Monster",
                frameType = "effect",
                race = "Aqua",
                attribute = "WATER",
                level = 2,
                atk = 1000,
                def = 1000,
                desc = desc
            });

        // ───────────────────────────── shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            EffectClause One(int id, out CompiledCardProgram prog)
            {
                prog = CardTextEffectCompiler.Compile(db.Get(id));
                return prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            }

            var frog = One(PoisonDrawFrog, out var frogProg);
            Check("Poison Draw Frog compiles to an optional face-up sent-to-GY draw 1 (not the attacked face-down battle)",
                frog != null && frog.Timing == EffectTiming.SentFromFieldToGy && frog.Action == EffectActionKind.Draw &&
                frog.Amount == 1 && frog.IsOptional && frog.CheckedAt == ConditionCheckedAt.Activation &&
                frog.RequiresLeftFieldFaceUp && frog.RequiresNotAttackedFaceDownAndDestroyedByBattle &&
                !frog.RequiresDestroyed && !frog.RequiresThisDestroyedByBattle,
                Describe(frogProg));

            var tad = One(Tadpole, out var tadProg);
            Check("T.A.D.P.O.L.E. compiles to an optional battle-destroyed any-number search of its own name",
                tad != null && tad.Timing == EffectTiming.SentFromFieldToGy &&
                tad.Action == EffectActionKind.AddAnyNumberOfNamedFromDeckToHand && tad.NamedCard == "T.A.D.P.O.L.E." &&
                tad.RequiresThisDestroyedByBattle && tad.RequiresSentToPreviousControllersGy && tad.IsOptional,
                Describe(tadProg));

            var ufo = One(Uforoid, out var ufoProg);
            Check("UFOroid compiles to the battle-destroyed Machine ≤1500 ATK Deck float (face-up Attack)",
                ufo != null && ufo.Timing == EffectTiming.SentFromFieldToGy &&
                ufo.Action == EffectActionKind.SpecialSummonNamed && ufo.RaceFilter == "Machine" && ufo.Amount == 1500 &&
                ufo.AmountIsAtkMax && ufo.FromDeck && ufo.RequiresThisDestroyedByBattle && ufo.IsOptional &&
                !ufo.SummonInDefense,
                Describe(ufoProg));

            var hyd = One(Hydrogeddon, out var hydProg);
            Check("Hydrogeddon compiles to an optional battle-destroying Special Summon of \"Hydrogeddon\" from the Deck",
                hyd != null && hyd.Timing == EffectTiming.ThisCardDestroysByBattle &&
                hyd.Action == EffectActionKind.SpecialSummonNamedFromDeckInChosenFaceUpPosition &&
                hyd.NamedCard == "Hydrogeddon" && hyd.FromDeck &&
                hyd.IsOptional,
                Describe(hydProg));

            var wr = One(Wroughtweiler, out var wrProg);
            Check("Wroughtweiler compiles to a mandatory battle-destroyed 2-target GY add (E-HERO series + Polymerization)",
                wr != null && wr.Timing == EffectTiming.SentFromFieldToGy &&
                wr.Action == EffectActionKind.AddTargetedSeriesCardAndNamedCardFromYourGyToHand &&
                wr.NamedCard == "Elemental HERO" && wr.NamedCardIsSeries && wr.AltNamedCard == "Polymerization" &&
                wr.RequiresThisDestroyedByBattle && !wr.IsOptional,
                Describe(wrProg));

            foreach (var id in new[] { WingedKuribohGy, WingedKuribohGraveyard })
            {
                var wk = One(id, out var wkProg);
                Check($"Winged Kuriboh ({id}) compiles to a destroyed-and-sent rest-of-turn battle damage shield",
                    wk != null && wk.Timing == EffectTiming.SentFromFieldToGy &&
                    wk.Action == EffectActionKind.ControllerTakesNoBattleDamageForRestOfTurn && wk.RequiresDestroyed &&
                    !wk.IsOptional && wk.MakesChainLink,
                    Describe(wkProg));
            }

            var gk = CardTextEffectCompiler.Compile(db.Get(GiantKozaky));
            Check("Giant Kozaky compiles to the Kozaky self-destruction and the face-up-destroyed original-ATK burn",
                gk != null && gk.FullyCompiled && gk.ClauseList.Count == 2 &&
                gk.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                          c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp &&
                                          c.RequiresFaceUpName == "Kozaky") &&
                gk.ClauseList.Exists(c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                          c.Action == EffectActionKind.PreviousControllerTakesThisOriginalAtkAsDamage &&
                                          c.RequiresDestroyed && c.RequiresLeftFieldFaceUp && !c.IsOptional &&
                                          RedirectAndReplaceRuntime.IsDestroyedTriggerWithoutGy(c)),
                Describe(gk));
        }

        static void NearMisses(Chk Check)
        {
            var misses = new (string what, string desc)[]
            {
                ("Poison Draw Frog + a discard rider",
                    "When this face-up card on the field is sent to the Graveyard (unless it was attacked while face-down " +
                    "and destroyed by battle), you can draw 1 card, then discard 1 card."),
                ("T.A.D.P.O.L.E. + a rider",
                    "When this card you control is destroyed by battle and sent to your Graveyard, you can add any " +
                    "\"T.A.D.P.O.L.E.\"(s) from your Deck to your hand, then shuffle your Deck and discard 1 card."),
                ("Hydrogeddon: any destroyed monster, Defense rider",
                    "When this card destroys an opponent's monster by battle and sends it to the GY: You can Special Summon " +
                    "1 \"Hydrogeddon\" from your Deck in Defense Position, and if you do, it cannot attack."),
                ("Wroughtweiler + a rider",
                    "When this card is destroyed by battle and sent to the Graveyard: Target 1 \"Elemental HERO\" card and 1 " +
                    "\"Polymerization\" in your Graveyard; add both those targets to your hand, then discard 1 card."),
                ("Giant Kozaky: destroy plus burn",
                    "If there is no face-up \"Kozaky\" on the field, destroy this card and inflict 500 damage to your " +
                    "opponent."),
                ("Giant Kozaky: burn to the opponent",
                    "If this face-up card is destroyed, inflict damage equal to the original ATK of this card to your " +
                    "opponent."),
                ("Winged Kuriboh: only this battle",
                    "If this card on the field is destroyed and sent to the GY: For the rest of this turn, you take no " +
                    "battle damage from attacks involving Fairy monsters."),
            };
            for (var i = 0; i < misses.Length; i++)
            {
                var (what, desc) = misses[i];
                var prog = Compile(990212300 + i, "Near miss " + what, desc);
                Check("near miss stays not FullyCompiled — " + what,
                    prog == null || !prog.FullyCompiled || prog.ClauseList.Count == 0, Describe(prog));
            }
        }

        // ───────────────────────────── Poison Draw Frog ─────────────────────────────

        static void PoisonDrawFrogDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's face-up Frog destroyed by the player's attack: the AI draws 1.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var axe = FaceUp(e, p, AxeRaider, 0);
                var frog = FaceUp(e, o, PoisonDrawFrog, 0);
                var deck = o.Deck.Count;
                var att = AttackAndDrain(e, p, axe, frog);
                Check("Poison Draw Frog: destroyed face-up by battle — its controller draws 1",
                    reached && att && o.Graveyard.Contains(frog) && o.Hand.Count == 1 && o.Deck.Count == deck - 1,
                    $"att={att} {State(e)}");
            }
            {
                // Attacked while face-down and destroyed by battle: no draw (the Damage Step flip does not count).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var axe = FaceUp(e, p, AxeRaider, 0);
                var frog = FaceDown(e, o, PoisonDrawFrog, 0);
                var att = AttackAndDrain(e, p, axe, frog);
                Check("Poison Draw Frog: attacked while face-down and destroyed by battle — no draw",
                    reached && att && o.Graveyard.Contains(frog) && o.Hand.Count == 0, $"att={att} {State(e)}");
            }
            {
                // Dark Hole: the face-up copy draws, the face-down copy does not (one draw in all).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var up = FaceUp(e, o, PoisonDrawFrog, 0);
                var down = FaceDown(e, o, PoisonDrawFrog, 1);
                var act = e.TryActivateSpellTrap(p, PutInHand(e, p, DarkHole), fromHand: true);
                Settle(e);
                Check("Poison Draw Frog: Dark Hole — the face-up copy draws 1, the face-down copy nothing",
                    reached && act && o.Graveyard.Contains(up) && o.Graveyard.Contains(down) && o.Hand.Count == 1,
                    $"act={act} {State(e)}");
            }
            {
                // Flipped face-up by an attack it survived, then destroyed while face-up by a later attack: it draws.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tad = FaceUp(e, p, Tadpole, 0); // 0 ATK: does not destroy the Frog's 100 DEF
                var axe = FaceUp(e, p, AxeRaider, 1);
                var frog = FaceDown(e, o, PoisonDrawFrog, 0);
                var first = AttackAndDrain(e, p, tad, frog);
                var survived = OnField(o, frog) && frog.FaceUp;
                var second = AttackAndDrain(e, p, axe, frog);
                Check("Poison Draw Frog: flipped by an earlier attack, destroyed face-up by the next — it draws 1",
                    reached && first && survived && second && o.Graveyard.Contains(frog) && o.Hand.Count == 1,
                    $"first={first} survived={survived} second={second} {State(e)}");
            }
        }

        // ───────────────────────────── T.A.D.P.O.L.E. ─────────────────────────────

        static void TadpoleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's copy destroyed by battle: the AI adds every copy from its Deck.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(o, Celtic, Tadpole, Celtic, Tadpole);
                var axe = FaceUp(e, p, AxeRaider, 0);
                var tad = FaceUp(e, o, Tadpole, 0);
                var att = AttackAndDrain(e, p, axe, tad);
                Check("T.A.D.P.O.L.E.: destroyed by battle — the AI adds both copies from its Deck",
                    reached && att && o.Graveyard.Contains(tad) && HandCount(o, Tadpole) == 2 &&
                    !o.Deck.Contains(Tadpole) && !e.IsAwaitingEffectTarget,
                    $"att={att} {State(e)}");
            }
            {
                // A human picks one copy at a time: two taps add two.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Tadpole, Celtic, Tadpole);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var tad = FaceUp(e, p, Tadpole, 0);
                var att = OpponentAttacks(e, axe, tad);
                var asked1 = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1;
                var ok1 = asked1 && e.TrySelectEffectTarget(e.PendingActivation.LegalTargets[0]);
                var asked2 = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1;
                var ok2 = asked2 && e.TrySelectEffectTarget(e.PendingActivation.LegalTargets[0]);
                Check("T.A.D.P.O.L.E.: a human taps one copy at a time — both copies are added",
                    reached && att && asked1 && ok1 && asked2 && ok2 && HandCount(p, Tadpole) == 2 &&
                    !p.Deck.Contains(Tadpole) && !e.IsAwaitingEffectTarget,
                    $"att={att} asked1={asked1} asked2={asked2} {State(e)}");
            }
            {
                // CANCEL at the first pick: "you can" — none is added.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Tadpole, Celtic);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var tad = FaceUp(e, p, Tadpole, 0);
                var att = OpponentAttacks(e, axe, tad);
                var asked = e.IsAwaitingEffectTarget;
                var cancelled = asked && e.CancelEffectTargeting();
                Check("T.A.D.P.O.L.E.: CANCEL at the first pick adds no copy",
                    reached && att && asked && cancelled && HandCount(p, Tadpole) == 0 && p.Deck.Contains(Tadpole) &&
                    !e.IsAwaitingEffectTarget,
                    $"att={att} asked={asked} {State(e)}");
            }
            {
                // A copy the AI took control of goes to the player's GY: not "your Graveyard" for its controller.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Tadpole);
                SetDeck(o, Tadpole);
                var tad = FaceUp(e, p, Tadpole, 0);
                var axe = FaceUp(e, p, AxeRaider, 1);
                var taken = e.TryTakeControl(o, tad);
                var att = AttackAndDrain(e, p, axe, tad);
                Check("T.A.D.P.O.L.E.: a stolen copy destroyed by battle goes to its owner's GY — no search",
                    reached && taken && att && p.Graveyard.Contains(tad) && HandCount(p, Tadpole) == 0 &&
                    HandCount(o, Tadpole) == 0 && !e.IsAwaitingEffectTarget,
                    $"taken={taken} att={att} {State(e)}");
            }
        }

        // ───────────────────────────── UFOroid ─────────────────────────────

        static void UforoidDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Destroyed by battle: the first Machine with 1500 or less ATK in the Deck, face-up Attack.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(o, Mechanicalchaser, Celtic, CannonSoldier);
                var blade = FaceUp(e, p, DarkBlade, 0);
                var ufo = FaceUp(e, o, Uforoid, 0);
                var att = AttackAndDrain(e, p, blade, ufo);
                var summoned = o.MonstersOnField().FirstOrDefault();
                Check("UFOroid: destroyed by battle — Special Summons a Machine with 1500 or less ATK (Cannon Soldier) " +
                      "in face-up Attack Position",
                    reached && att && o.Graveyard.Contains(ufo) && summoned != null && summoned.CardId == CannonSoldier &&
                    summoned.FaceUp && summoned.Position == BattlePosition.Attack && o.MonsterCount == 1,
                    $"att={att} summoned={summoned?.Name} {State(e)}");
            }
            {
                // Destroyed by Dark Hole (not by battle): nothing.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(o, CannonSoldier);
                var ufo = FaceUp(e, o, Uforoid, 0);
                var act = e.TryActivateSpellTrap(p, PutInHand(e, p, DarkHole), fromHand: true);
                Settle(e);
                Check("UFOroid: destroyed by a card effect — no Special Summon",
                    reached && act && o.Graveyard.Contains(ufo) && o.MonsterCount == 0 && o.Deck.Contains(CannonSoldier),
                    $"act={act} {State(e)}");
            }
        }

        // ───────────────────────────── Hydrogeddon ─────────────────────────────

        static void HydrogeddonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // A human taps the copy and chooses face-up Defense Position.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Celtic, Hydrogeddon);
                var hyd = FaceUp(e, p, Hydrogeddon, 0);
                var insect = FaceUp(e, o, BasicInsect, 0);
                var att = AttackAndDrain(e, p, hyd, insect);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1 &&
                            SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation);
                CardInstance pick = asked ? e.PendingActivation.LegalTargets[0] : null;
                if (asked) e.SetPendingSpecialSummonPlacement(2, BattlePosition.Defense);
                var picked = asked && e.TrySelectEffectTarget(pick);
                Settle(e);
                var copy = p.MonsterZones[2].Occupant;
                Check("Hydrogeddon: destroys an opponent's monster by battle — a human Special Summons \"Hydrogeddon\" " +
                      "from the Deck in the face-up Defense Position it chose",
                    reached && att && o.Graveyard.Contains(insect) && asked && picked && copy != null &&
                    copy.CardId == Hydrogeddon && copy.FaceUp && copy.Position == BattlePosition.Defense &&
                    !p.Deck.Contains(Hydrogeddon),
                    $"att={att} asked={asked} picked={picked} {State(e)}");
            }
            {
                // The AI's Hydrogeddon: Special Summoned in Attack Position, no prompt.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(o, Hydrogeddon);
                var insect = FaceUp(e, p, BasicInsect, 0);
                var hyd = FaceUp(e, o, Hydrogeddon, 0);
                var att = OpponentAttacks(e, hyd, insect);
                var copies = o.MonstersOnField().Count(m => m.CardId == Hydrogeddon && m.FaceUp &&
                                                            m.Position == BattlePosition.Attack);
                Check("Hydrogeddon: the AI's copy destroys a monster by battle — Special Summons another in Attack Position",
                    reached && att && p.Graveyard.Contains(insect) && copies == 2 && !o.Deck.Contains(Hydrogeddon) &&
                    !e.IsAwaitingEffectTarget,
                    $"att={att} copies={copies} {State(e)}");
            }
            {
                // It is the one destroyed: nothing.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Hydrogeddon);
                var hyd = FaceUp(e, p, Hydrogeddon, 0);
                var blade = FaceUp(e, o, DarkBlade, 0);
                var att = AttackAndDrain(e, p, hyd, blade);
                Check("Hydrogeddon: destroyed by battle itself — no Special Summon",
                    reached && att && p.Graveyard.Contains(hyd) && p.MonsterCount == 0 && p.Deck.Contains(Hydrogeddon),
                    $"att={att} {State(e)}");
            }
        }

        // ───────────────────────────── Wroughtweiler ─────────────────────────────

        static void WroughtweilerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI targets its strongest E-HERO and Polymerization.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var avian = InGy(e, o, Avian);
                var burst = InGy(e, o, Burstinatrix);
                var poly = InGy(e, o, Polymerization);
                var axe = FaceUp(e, p, AxeRaider, 0);
                var wr = FaceUp(e, o, Wroughtweiler, 0);
                var att = AttackAndDrain(e, p, axe, wr);
                Check("Wroughtweiler: destroyed by battle — adds 1 E-HERO (Burstinatrix) and Polymerization from the GY",
                    reached && att && o.Graveyard.Contains(wr) && o.Hand.Contains(burst) && o.Hand.Contains(poly) &&
                    o.Graveyard.Contains(avian) && o.Hand.Count == 2,
                    $"att={att} {State(e)}");
            }
            {
                // No Polymerization in the GY: nothing is targeted, the E-HERO stays.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var avian = InGy(e, o, Avian);
                var axe = FaceUp(e, p, AxeRaider, 0);
                var wr = FaceUp(e, o, Wroughtweiler, 0);
                var att = AttackAndDrain(e, p, axe, wr);
                Check("Wroughtweiler: no Polymerization in the GY — nothing happens",
                    reached && att && o.Graveyard.Contains(wr) && o.Graveyard.Contains(avian) && o.Hand.Count == 0,
                    $"att={att} {State(e)}");
            }
            {
                // A human chooses the E-HERO (2 candidates); the lone Polymerization follows.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var avian = InGy(e, p, Avian);
                var burst = InGy(e, p, Burstinatrix);
                var poly = InGy(e, p, Polymerization);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var wr = FaceUp(e, p, Wroughtweiler, 0);
                var att = OpponentAttacks(e, axe, wr);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(avian) &&
                            e.PendingActivation.LegalTargets.Contains(burst) &&
                            !e.PendingActivation.LegalTargets.Contains(poly);
                var picked = asked && e.TrySelectEffectTarget(avian);
                Check("Wroughtweiler: a human targets Avian of 2 E-HEROes — Avian and Polymerization are added",
                    reached && att && asked && picked && p.Hand.Contains(avian) && p.Hand.Contains(poly) &&
                    p.Graveyard.Contains(burst) && !e.IsAwaitingEffectTarget,
                    $"att={att} asked={asked} picked={picked} {State(e)}");
            }
            {
                // An E-HERO Fusion Monster target returns to the Extra Deck.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var wing = InGy(e, o, FlameWingman);
                var poly = InGy(e, o, Polymerization);
                var extra = o.ExtraDeck.Count(id => id == FlameWingman);
                var axe = FaceUp(e, p, AxeRaider, 0);
                var wr = FaceUp(e, o, Wroughtweiler, 0);
                var att = AttackAndDrain(e, p, axe, wr);
                Check("Wroughtweiler: an Elemental HERO Fusion Monster target returns to the Extra Deck, Polymerization " +
                      "to the hand",
                    reached && att && !o.Graveyard.Contains(wing) && o.ExtraDeck.Count(id => id == FlameWingman) == extra + 1 &&
                    o.Hand.Contains(poly) && o.Hand.Count == 1,
                    $"att={att} {State(e)}");
            }
            {
                // Lua thtg (EFFECT_FLAG_CARD_TARGET) targets as the trigger activates; thop adds only the targets still in the
                // GY. Two Wroughtweilers destroyed by one battle are one Chain: the AI's (turn player, CL1) targets
                // Burstinatrix + Polymerization before the human's (CL2) activates. At the human's prompt the AI's targeted
                // Polymerization is banished and another Polymerization reaches the AI's GY: CL1 still adds Burstinatrix and
                // does not take the newcomer.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var oBurst = InGy(e, o, Burstinatrix);
                var oPoly = InGy(e, o, Polymerization);
                var avian = InGy(e, p, Avian);
                var pBurst = InGy(e, p, Burstinatrix);
                var pPoly = InGy(e, p, Polymerization);
                var oWr = FaceUp(e, o, Wroughtweiler, 0);
                var pWr = FaceUp(e, p, Wroughtweiler, 0);
                var att = OpponentAttacks(e, oWr, pWr);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(avian) &&
                            e.PendingActivation.LegalTargets.Contains(pBurst);
                CardInstance late = null;
                if (asked)
                {
                    o.Graveyard.Remove(oPoly);
                    o.Banished.Add(oPoly);
                    late = InGy(e, o, Polymerization);
                }

                var picked = asked && e.TrySelectEffectTarget(avian);
                Settle(e);
                Check("Wroughtweiler: targets are chosen as it activates — CL1's banished Polymerization is not replaced, " +
                      "its E-HERO is still added",
                    reached && att && asked && picked && o.Graveyard.Contains(oWr) && p.Graveyard.Contains(pWr) &&
                    o.Hand.Contains(oBurst) && o.Hand.Count == 1 && late != null && o.Graveyard.Contains(late) &&
                    o.Banished.Contains(oPoly) && p.Hand.Contains(avian) && p.Hand.Contains(pPoly) &&
                    p.Graveyard.Contains(pBurst),
                    $"att={att} asked={asked} picked={picked} {State(e)}");
            }
        }

        // ───────────────────────────── Winged Kuriboh ─────────────────────────────

        static void WingedKuribohDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var id in new[] { WingedKuribohGy, WingedKuribohGraveyard })
            {
                // Destroyed by battle: that battle's damage applies; the rest of the turn deals none.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var wk = FaceUp(e, p, id, 0);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var blade = FaceUp(e, o, DarkBlade, 1);
                var lp = p.LifePoints;
                var att = OpponentAttacks(e, axe, wk);
                var afterFirst = p.LifePoints;
                var direct = e.TryAttack(o, blade, null);
                DrainCombat(e);
                var shielded = CrvDestroyedTriggersAndFloatsRuntime.AvoidsBattleDamage(e, p);
                e.TryEndTurnSafe(o);
                Settle(e);
                var expired = !CrvDestroyedTriggersAndFloatsRuntime.AvoidsBattleDamage(e, p);
                Check($"Winged Kuriboh ({id}): destroyed by battle — 1400 from that battle, then no battle damage from a " +
                      "direct attack; the shield ends with the turn",
                    reached && att && p.Graveyard.Contains(wk) && afterFirst == lp - 1400 && direct &&
                    p.LifePoints == afterFirst && shielded && expired,
                    $"att={att} direct={direct} shielded={shielded} expired={expired} {State(e)}");
            }
            {
                // Sent to the GY without being destroyed: no shield.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var wk = FaceUp(e, p, WingedKuribohGy, 0);
                var blade = FaceUp(e, o, DarkBlade, 0);
                e.SendCardToGrave(p, wk);
                Settle(e);
                var lp = p.LifePoints;
                var att = OpponentAttacks(e, blade, null);
                Check("Winged Kuriboh: sent to the GY (not destroyed) — no shield, the direct attack deals 1800",
                    reached && att && p.Graveyard.Contains(wk) && p.LifePoints == lp - 1800,
                    $"att={att} {State(e)}");
            }
        }

        // ───────────────────────────── Giant Kozaky ─────────────────────────────

        static void GiantKozakyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Normal Summoned with no face-up "Kozaky": destroyed, its controller takes 2500.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var gk = PutInHand(e, p, GiantKozaky);
                var lp = p.LifePoints;
                var olp = e.Opponent.LifePoints;
                var ns = e.TryNormalSummon(p, gk, false);
                Settle(e);
                Check("Giant Kozaky: Normal Summoned without a face-up Kozaky — destroyed, its controller takes 2500",
                    reached && ns && p.Graveyard.Contains(gk) && p.LifePoints == lp - 2500 && e.Opponent.LifePoints == olp,
                    $"ns={ns} {State(e)}");
            }
            {
                // With a face-up Kozaky it stays; once Kozaky is destroyed, it is destroyed too (2500 to its controller).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var koz = FaceUp(e, p, Kozaky, 0);
                var gk = PutInHand(e, p, GiantKozaky);
                var lp = p.LifePoints;
                var ns = e.TryNormalSummon(p, gk, false);
                Settle(e);
                var stays = OnField(p, gk) && p.LifePoints == lp;
                e.DestroyMonsterPublic(p, koz);
                Settle(e);
                Check("Giant Kozaky: stays with a face-up Kozaky; destroyed when Kozaky leaves — 2500 to its controller",
                    reached && ns && stays && p.Graveyard.Contains(gk) && p.LifePoints == lp - 2500,
                    $"ns={ns} stays={stays} {State(e)}");
            }
            {
                // Face-down: not destroyed by its own effect, and its destruction while face-down burns nobody.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var gk = FaceDown(e, p, GiantKozaky, 0);
                var lp = p.LifePoints;
                var olp = e.Opponent.LifePoints;
                e.NotifyPublic();
                var stays = OnField(p, gk);
                e.DestroyMonsterPublic(p, gk);
                Settle(e);
                Check("Giant Kozaky: face-down — no self-destruction, destroyed face-down: no damage",
                    reached && stays && p.Graveyard.Contains(gk) && p.LifePoints == lp && e.Opponent.LifePoints == olp,
                    $"stays={stays} {State(e)}");
            }
            {
                // Controlled by the AI when destroyed: the AI (its current controller) takes 2500; it goes to its owner's GY.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, Kozaky, 0);
                var gk = FaceUp(e, p, GiantKozaky, 1);
                var taken = e.TryTakeControl(o, gk);
                var plp = p.LifePoints;
                var olp = o.LifePoints;
                e.DestroyMonsterPublic(o, gk);
                Settle(e);
                Check("Giant Kozaky: destroyed while the AI controls it — the AI takes 2500, the card goes to the player's GY",
                    reached && taken && p.Graveyard.Contains(gk) && o.LifePoints == olp - 2500 && p.LifePoints == plp,
                    $"taken={taken} {State(e)}");
            }
            {
                // The monster self-destruction sweep also runs Zeradias, Herald of Heaven's compiled "If "The Sanctuary in
                // the Sky" is not on the field, destroy this card." (Lua EFFECT_SELF_DESTROY, descon: no face-up Sanctuary on
                // either field). Before this batch nothing read that monster clause. Destroyed alone; kept while the AI's
                // Sanctuary is face-up; destroyed once that Sanctuary leaves.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var alone = FaceUp(e, p, Zeradias, 0);
                e.NotifyPublic();
                Settle(e);
                var destroyedAlone = p.Graveyard.Contains(alone) && !OnField(p, alone);
                var sanct = PlaceField(e, o, Sanctuary);
                var kept = FaceUp(e, p, Zeradias, 1);
                e.NotifyPublic();
                Settle(e);
                var stays = OnField(p, kept);
                o.FieldSpellZone.Occupant = null;
                o.Graveyard.Add(sanct);
                e.NotifyPublic();
                Settle(e);
                Check("Zeradias, Herald of Heaven: destroyed without a face-up Sanctuary, kept with the AI's Sanctuary, " +
                      "destroyed once it leaves",
                    reached && destroyedAlone && stays && p.Graveyard.Contains(kept) && !OnField(p, kept),
                    $"alone={destroyedAlone} stays={stays} {State(e)}");
            }
        }
    }
}
