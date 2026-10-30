using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CrvCountersAndPhaseTriggers batch: B.E.S. Crystal Core, B.E.S. Tetran, Dark Catapulter, Ebon Magician Curran, Cyber
    /// Archfiend, Rapid-Fire Magician. Compile shapes through the real compiler, mini-duels through the engine entry points
    /// (TryNormalSummon, TryActivateSpellTrap, the target / cost picks and Cancel, TryAttack, turn ends through the Draw,
    /// Standby and End Phases), and look-alike sentences that must stay uncompiled. Turn 3 is the Player's, turn 4 the AI's.
    /// Runtime: <see cref="CrvCountersAndPhaseTriggersRuntime"/>.
    /// </summary>
    public static class CrvCountersAndPhaseTriggersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int CrystalCore = 22790789;     // Machine Lv5 2100/1000
        const int Tetran = 44954628;          // Machine Lv6 1800/2300
        const int DarkCatapulter = 33875961;  // Machine Lv4 1000/1500
        const int EbonCurran = 46128076;      // Spellcaster Lv2 1200/0
        const int JustDesserts = 24068492;
        const int CyberArchfiend = 59907935;  // Fiend Lv4 1000/2000
        const int RapidFire = 6337436;        // Spellcaster Lv4 1600/1200

        const int CelticGuardian = 91152256;  // Warrior Normal 1400/1200
        const int MysticalElf = 15025844;     // Spellcaster Normal 800/2000
        const int BasicInsect = 89091579;     // Insect Normal 500/700
        const int PoisonFangs = 76539047;     // Continuous Spell
        const int PotOfGreed = 55144522;      // Normal Spell

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Chk Check = (name, ok, detail) => check("CrvCountersAndPhaseTriggers: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            CrystalCoreDuels(db, pDeck, aDeck, Check);
            TetranDuels(db, pDeck, aDeck, Check);
            CatapulterDuels(db, pDeck, aDeck, Check);
            AiSpellTrapTargets(db, pDeck, aDeck, Check);
            CurranDuels(db, pDeck, aDeck, Check);
            CyberArchfiendDuels(db, pDeck, aDeck, Check);
            RapidFireDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards and hands.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
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

        static void Settle(DuelEngine engine)
        {
            engine.NotifyPublic();
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The Player passes turns until their next Main Phase 1 (nobody acts on the turn in between).</summary>
        static bool NextPlayerMain1(DuelEngine engine)
        {
            var start = engine.TurnNumber;
            for (var t = 0; t < 6 && !(engine.TurnPlayer == engine.Player && engine.TurnNumber > start &&
                                       engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
                if (engine.IsAwaitingResponse) engine.PassResponse();
            }

            return engine.TurnPlayer == engine.Player && engine.TurnNumber > start && engine.Phase == DuelPhase.Main1;
        }

        static CardInstance TributeSummon(DuelEngine engine, int id, out bool ok)
        {
            var p = engine.Player;
            var tribute = PlaceMonster(engine, p, CelticGuardian, 4, BattlePosition.Attack, true);
            var card = PutInHand(engine, p, id);
            engine.PendingTributes.Add(tribute);
            ok = engine.TryNormalSummon(p, card, asSet: false);
            Settle(engine);
            return card;
        }

        static bool OnField(DuelistState who, CardInstance c) =>
            who.TryFindMonster(c, out _) || who.TryFindSpellTrap(c, out _);

        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static CardInstance FaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static CardInstance InGraveyard(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        /// <summary>Puts <paramref name="n"/> counters on a face-up monster through the batch's own placement.</summary>
        static void PlaceCounters(DuelEngine engine, DuelistState who, CardInstance card, int n) =>
            TokensDeclarationsAndPicksRuntime.PlaceCounter(engine, who, card,
                new EffectClause { Action = EffectActionKind.PlaceCounterOnThisCard, Amount = n });

        static bool Banished(DuelistState who, CardInstance c) => who.Banished != null && who.Banished.Contains(c);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} chain={c.MakesChainLink}")) + "] " +
                  $"unparsed=[{string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Is(int id, CardEffectStatusKind k) => db.Get(id) != null && CardEffectStatus.Classify(db.Get(id)) == k;
            EffectClause Find(CompiledCardProgram p, EffectActionKind a) =>
                p?.ClauseList.Find(c => c != null && c.Action == a);

            foreach (var (id, name) in new[] { (CrystalCore, "B.E.S. Crystal Core"), (Tetran, "B.E.S. Tetran") })
            {
                var p = P(id);
                var counter = Find(p, EffectActionKind.PlaceCounterOnThisCard);
                var remove = Find(p, EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep);
                Check($"{name}: Normal Summon counters (3), battle indestructible, end-of-Damage-Step removal or destroy",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 4 &&
                    counter != null && counter.Timing == EffectTiming.ThisCardSummoned && counter.Amount == 3 &&
                    counter.RequiresThisNormalSummoned && !counter.IsOptional && counter.MakesChainLink &&
                    Find(p, EffectActionKind.CannotBeDestroyedByBattle) != null &&
                    remove != null && remove.Timing == EffectTiming.EndOfDamageStep && remove.Amount == 1 &&
                    !remove.IsOptional && Is(id, CardEffectStatusKind.Implemented), Describe(p));
            }

            var cc = P(CrystalCore);
            var ccPos = Find(cc, EffectActionKind.ChangeFaceUpAttackTargetToFaceUpDefense);
            Check("B.E.S. Crystal Core: once per turn, target 1 face-up Attack Position monster the opponent controls",
                ccPos != null && ccPos.Timing == EffectTiming.Activate &&
                ccPos.Zone == EffectZoneFilter.OppAttackPositionMonsters && ccPos.RequiresTargetChoice &&
                ccPos.IsPsctTarget && ccPos.RequiresTargetFaceUp && ccPos.OncePerTurn && ccPos.IsOptional,
                Describe(cc));

            var te = P(Tetran);
            var teDestroy = Find(te, EffectActionKind.Destroy);
            Check("B.E.S. Tetran: once per turn, remove 1 of its own counters (not a Spell Counter); target 1 Spell/Trap",
                teDestroy != null && teDestroy.Timing == EffectTiming.Activate &&
                teDestroy.Zone == EffectZoneFilter.FieldSpellTraps && teDestroy.RequiresTargetChoice &&
                teDestroy.IsPsctTarget && teDestroy.RequiresRemoveSpellCounters == 1 &&
                teDestroy.CounterCostIsNotSpellCounter && teDestroy.OncePerTurn,
                Describe(te));

            var dc = P(DarkCatapulter);
            var dcCounter = Find(dc, EffectActionKind.PlaceCounterOnThisCard);
            var dcIgn = Find(dc, EffectActionKind.BanishGyCardsPerCounterDestroyThatManySpellTraps);
            Check("Dark Catapulter: Standby counter in Defense Position; the counter-scaled ignition",
                dc != null && dc.FullyCompiled && dc.ClauseList.Count == 2 &&
                dcCounter != null && dcCounter.Timing == EffectTiming.StandbyPhase && dcCounter.Amount == 1 &&
                dcCounter.RequiresThisDefensePosition && !dcCounter.IsOptional &&
                dcIgn != null && dcIgn.Timing == EffectTiming.Activate && !dcIgn.OncePerTurn && dcIgn.IsOptional &&
                dcIgn.ActivationCondition == CrvCountersAndPhaseTriggersRuntime.CatapulterReady &&
                Is(DarkCatapulter, CardEffectStatusKind.Implemented), Describe(dc));

            var ec = P(EbonCurran);
            var ecBurn = Find(ec, EffectActionKind.InflictDamageToOpponent);
            Check("Ebon Magician Curran: your Standby Phase, 300 x the monsters your opponent controls",
                ec != null && ec.FullyCompiled && ec.ClauseList.Count == 1 && ecBurn != null &&
                ecBurn.Timing == EffectTiming.StandbyPhase && ecBurn.Amount == 300 &&
                ecBurn.ScaleAmountByOpponentMonsterCount && !ecBurn.OpponentTurnOnly &&
                Is(EbonCurran, CardEffectStatusKind.Implemented), Describe(ec));
            var jd = P(JustDesserts);
            Check("Just Desserts keeps its Activate burn (Princess Curran now implemented via v311 simplified atoms)",
                Find(jd, EffectActionKind.InflictDamageToOpponent)?.Timing == EffectTiming.Activate &&
                Is(JustDesserts, CardEffectStatusKind.Implemented),
                Describe(jd));

            var ca = P(CyberArchfiend);
            var caDraw = Find(ca, EffectActionKind.DrawOneAtDrawPhaseStartIfHandEmpty);
            var caDestroy = Find(ca, EffectActionKind.DestroyThisAtYourEndPhaseIfHandNotEmpty);
            Check("Cyber Archfiend: the pre-draw extra draw marker and the End Phase self-destruction",
                ca != null && ca.FullyCompiled && ca.ClauseList.Count == 2 && caDraw != null &&
                caDraw.Timing == EffectTiming.ContinuousWhileFaceUp && caDraw.IsOptional && caDestroy != null &&
                caDestroy.Timing == EffectTiming.EndPhase && !caDestroy.OpponentTurnOnly && caDestroy.MakesChainLink &&
                Is(CyberArchfiend, CardEffectStatusKind.Implemented), Describe(ca));

            var rf = P(RapidFire);
            var rfBurn = Find(rf, EffectActionKind.InflictDamageToOpponentWhenYourNormalSpellResolves);
            Check("Rapid-Fire Magician: 400 damage each time its controller's Normal Spell Card resolves",
                rf != null && rf.FullyCompiled && rf.ClauseList.Count == 1 && rfBurn != null && rfBurn.Amount == 400 &&
                rfBurn.Timing == EffectTiming.ContinuousWhileFaceUp && !rfBurn.MakesChainLink &&
                Is(RapidFire, CardEffectStatusKind.Implemented), Describe(rf));
        }

        static void NearMisses(Chk Check)
        {
            var near = new (string Desc, string Race, Func<EffectClause, bool> Leak)[]
            {
                ("When this card is Normal Summoned, put 3 counters on it and draw 1 card.", "Machine",
                    c => c.Action == EffectActionKind.PlaceCounterOnThisCard),
                ("If this card attacks, remove 1 counter from this card at the end of the Damage Step. If you cannot, " +
                 "destroy it.", "Machine",
                    c => c.Action == EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep),
                ("Once per turn, during your Main Phase, you can change 1 face-up Defense Position monster your opponent " +
                 "controls to face-up Attack Position.", "Machine",
                    c => c.Action == EffectActionKind.ChangeFaceUpAttackTargetToFaceUpDefense),
                ("Once per turn, you can remove 1 counter on this card to destroy 1 monster on the field.", "Machine",
                    c => c.CounterCostIsNotSpellCounter),
                ("During your Standby Phase, if this card is in Attack Position, put 1 counter on this card.", "Machine",
                    c => c.Action == EffectActionKind.PlaceCounterOnThisCard),
                ("You can remove from play a number of card(s) in your Graveyard equal to the number of counters on this " +
                 "card to destroy that many monsters on the field. Then remove all counter(s) from this card.", "Machine",
                    c => c.Action == EffectActionKind.BanishGyCardsPerCounterDestroyThatManySpellTraps),
                ("During your Standby Phase, inflict 300 damage to your opponent for each Spell Card they control.",
                    "Spellcaster", c => c.Timing == EffectTiming.StandbyPhase),
                ("During your opponent's Standby Phase, inflict 300 damage to your opponent for each monster they control.",
                    "Spellcaster", c => c.Timing == EffectTiming.StandbyPhase),
                ("At the beginning of your Draw Phase, if you have no cards in your hand, draw 2 more cards in addition " +
                 "to your normal draw.", "Fiend", c => c.Action == EffectActionKind.DrawOneAtDrawPhaseStartIfHandEmpty),
                ("During your End Phase, if you have 2 or more cards in your hand, destroy this card.", "Fiend",
                    c => c.Action == EffectActionKind.DestroyThisAtYourEndPhaseIfHandNotEmpty),
                ("Inflict 400 damage to your opponent each time you activate a Quick-Play Spell Card.", "Spellcaster",
                    c => c.Action == EffectActionKind.InflictDamageToOpponentWhenYourNormalSpellResolves),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90917900 + i, name = "Synthetic CrvCounters near miss " + i, type = "Effect Monster",
                    race = n.Race, desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => c != null && n.Leak(c)));
            }).Select(n => n.Desc.Substring(0, Math.Min(40, n.Desc.Length))).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(" | ", leaked));
        }

        // ───────────────────────────── B.E.S. Crystal Core ─────────────────────────────

        static void CrystalCoreDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cc = TributeSummon(engine, CrystalCore, out var ns);
                var placed = cc.Counters;
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, true);
                var setOne = PlaceMonster(engine, o, BasicInsect, 2, BattlePosition.Defense, false);
                var can = engine.CanActivateSpellTrap(p, cc, fromHand: false);
                var act = engine.TryActivateSpellTrap(p, cc, fromHand: false);
                // The lone face-up Attack Position monster is the only legal target (a prompt may still ask for it).
                var lone = !engine.IsAwaitingEffectTarget ||
                           (engine.PendingActivation.LegalTargets.Count == 1 &&
                            engine.PendingActivation.LegalTargets.Contains(celtic) && engine.TrySelectEffectTarget(celtic));
                Settle(engine);
                var again = engine.CanActivateSpellTrap(p, cc, fromHand: false);
                Check("B.E.S. Crystal Core: Tribute Summoned → 3 counters; its ignition turns the face-up Attack Position " +
                      "monster to face-up Defense (once per turn)",
                    reached && ns && placed == 3 && can && act && lone && celtic.FaceUp &&
                    celtic.Position == BattlePosition.Defense && elf.Position == BattlePosition.Defense &&
                    !setOne.FaceUp && !again,
                    $"reached={reached} ns={ns} placed={placed} can={can} act={act} lone={lone} celtic={celtic.Position} " +
                    $"again={again}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cc = TributeSummon(engine, CrystalCore, out var ns);
                PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, true);
                var can = engine.CanActivateSpellTrap(p, cc, fromHand: false);
                Check("B.E.S. Crystal Core: no face-up Attack Position monster on the opponent's side — no activation",
                    reached && ns && !can, $"can={can}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cc = TributeSummon(engine, CrystalCore, out var ns);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var ok = Attack(engine, p, cc, elf);
                var afterFirst = cc.Counters;
                Check("B.E.S. Crystal Core: after it attacks, 1 counter is removed at the end of the Damage Step",
                    reached && ns && ok && OnField(p, cc) && afterFirst == 2,
                    $"ok={ok} onField={OnField(p, cc)} counters={afterFirst}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cc = TributeSummon(engine, CrystalCore, out var ns);
                cc.Counters = 0; // its counters of this presence, all removed by earlier battles
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var ok = Attack(engine, p, cc, elf);
                Check("B.E.S. Crystal Core: with no counter left it is destroyed at the end of the Damage Step",
                    reached && ns && ok && !OnField(p, cc) && InGy(p, cc),
                    $"ok={ok} onField={OnField(p, cc)} inGy={InGy(p, cc)}");
            }
        }

        // ───────────────────────────── B.E.S. Tetran ─────────────────────────────

        static void TetranDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var te = TributeSummon(engine, Tetran, out var ns);
                var placed = te.Counters;
                var holder = SpellCounterSpendersRuntime.IsSpellCounterHolder(te);
                var fangs = FaceUpSpell(engine, o, PoisonFangs, 0);
                var act = engine.TryActivateSpellTrap(p, te, fromHand: false);
                var lone = !engine.IsAwaitingEffectTarget ||
                           (engine.PendingActivation.LegalTargets.Count == 1 &&
                            engine.PendingActivation.LegalTargets.Contains(fangs) && engine.TrySelectEffectTarget(fangs));
                Settle(engine);
                var again = engine.CanActivateSpellTrap(p, te, fromHand: false);
                Check("B.E.S. Tetran: remove 1 counter (cost), destroy the targeted Spell/Trap; once per turn; its " +
                      "counters are not Spell Counters",
                    reached && ns && placed == 3 && !holder && act && lone && InGy(o, fangs) && te.Counters == 2 && !again,
                    $"reached={reached} ns={ns} placed={placed} holder={holder} act={act} lone={lone} " +
                    $"fangsInGy={InGy(o, fangs)} " +
                    $"counters={te.Counters} again={again}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var te = TributeSummon(engine, Tetran, out var ns);
                var theirs = FaceUpSpell(engine, o, PoisonFangs, 0);
                var mine = PlaceSetTrap(engine, p, PoisonFangs, 0);
                var act = engine.TryActivateSpellTrap(p, te, fromHand: false);
                var picking = engine.IsAwaitingEffectTarget;
                var paid = te.Counters;
                var cancelled = picking && engine.CancelEffectTargeting();
                Settle(engine);
                var refunded = te.Counters;
                var can = engine.CanActivateSpellTrap(p, te, fromHand: false);
                Check("B.E.S. Tetran: Cancel at the target pick undoes the activation — the counter comes back",
                    reached && ns && act && picking && paid == 2 && cancelled && refunded == 3 && can &&
                    OnField(o, theirs) && OnField(p, mine) && !engine.Chain.HasLinks,
                    $"act={act} picking={picking} paid={paid} cancelled={cancelled} refunded={refunded} can={can}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var te = TributeSummon(engine, Tetran, out var ns);
                te.Counters = 0;
                FaceUpSpell(engine, o, PoisonFangs, 0);
                var can = engine.CanActivateSpellTrap(p, te, fromHand: false);
                Check("B.E.S. Tetran: no counter left — the cost cannot be paid",
                    reached && ns && !can, $"can={can}");
            }
        }

        // ───────────────────────────── Dark Catapulter ─────────────────────────────

        static void CatapulterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var def = PlaceMonster(engine, p, DarkCatapulter, 0, BattlePosition.Defense, true);
                var atk = PlaceMonster(engine, p, DarkCatapulter, 1, BattlePosition.Attack, true);
                var next = NextPlayerMain1(engine);
                Check("Dark Catapulter: a counter in your Standby Phase only while in Defense Position",
                    reached && next && def.Counters == 1 && atk.Counters == 0,
                    $"next={next} def={def.Counters} atk={atk.Counters}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cat = PlaceMonster(engine, p, DarkCatapulter, 0, BattlePosition.Defense, true);
                PlaceCounters(engine, p, cat, 1);
                var gy1 = InGraveyard(engine, p, CelticGuardian);
                var gy2 = InGraveyard(engine, p, PotOfGreed);
                var s1 = FaceUpSpell(engine, o, PoisonFangs, 0);
                var s2 = PlaceSetTrap(engine, o, PoisonFangs, 1);
                var act = engine.TryActivateSpellTrap(p, cat, fromHand: false);
                var costPick = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(gy1) &&
                               engine.PendingActivation.LegalTargets.Contains(gy2);
                var paid = costPick && engine.TrySelectEffectTarget(gy1);
                var targetPick = engine.IsAwaitingEffectTarget &&
                                 engine.PendingActivation.LegalTargets.Contains(s1) &&
                                 engine.PendingActivation.LegalTargets.Contains(s2);
                var picked = targetPick && engine.TrySelectEffectTarget(s1);
                Settle(engine);
                Check("Dark Catapulter: 1 counter → banish 1 chosen GY card, destroy 1 targeted Spell/Trap, counters gone",
                    reached && act && costPick && paid && targetPick && picked && Banished(p, gy1) && InGy(p, gy2) &&
                    InGy(o, s1) && OnField(o, s2) && cat.Counters == 0 && !engine.Chain.HasLinks,
                    $"act={act} costPick={costPick} paid={paid} targetPick={targetPick} picked={picked} " +
                    $"gy1Banished={Banished(p, gy1)} s1InGy={InGy(o, s1)} counters={cat.Counters}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cat = PlaceMonster(engine, p, DarkCatapulter, 0, BattlePosition.Defense, true);
                PlaceCounters(engine, p, cat, 1);
                var gy1 = InGraveyard(engine, p, CelticGuardian);
                InGraveyard(engine, p, PotOfGreed);
                var s1 = FaceUpSpell(engine, o, PoisonFangs, 0);
                var act = engine.TryActivateSpellTrap(p, cat, fromHand: false);
                var cancelled = engine.IsAwaitingEffectTarget && engine.CancelEffectTargeting();
                Settle(engine);
                Check("Dark Catapulter: Cancel at the cost pick — nothing banished or destroyed, the counter stays",
                    reached && act && cancelled && InGy(p, gy1) && OnField(o, s1) && cat.Counters == 1 &&
                    !engine.Chain.HasLinks && engine.CanActivateSpellTrap(p, cat, fromHand: false),
                    $"act={act} cancelled={cancelled} counters={cat.Counters} links={engine.Chain.HasLinks}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cat = PlaceMonster(engine, p, DarkCatapulter, 0, BattlePosition.Defense, true);
                PlaceCounters(engine, p, cat, 2);
                InGraveyard(engine, p, CelticGuardian);
                InGraveyard(engine, p, PotOfGreed);
                FaceUpSpell(engine, o, PoisonFangs, 0);
                var oneSpellTrap = !engine.CanActivateSpellTrap(p, cat, fromHand: false);
                FaceUpSpell(engine, o, PoisonFangs, 1);
                p.Graveyard.RemoveAt(0);
                var oneGyCard = !engine.CanActivateSpellTrap(p, cat, fromHand: false);
                cat.Counters = 0;
                InGraveyard(engine, p, CelticGuardian);
                var noCounter = !engine.CanActivateSpellTrap(p, cat, fromHand: false);
                Check("Dark Catapulter: N counters need N GY cards and N Spell/Trap Cards; no counter, no activation",
                    reached && oneSpellTrap && oneGyCard && noCounter,
                    $"oneSpellTrap={oneSpellTrap} oneGyCard={oneGyCard} noCounter={noCounter}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var mine1 = FaceUpSpell(engine, p, PoisonFangs, 0);
                var mine2 = PlaceSetTrap(engine, p, PoisonFangs, 1);
                var main = ReachOpponentMain(engine);
                ClearBoard(engine);
                p.SpellTrapZones[0].Occupant = mine1;
                p.SpellTrapZones[1].Occupant = mine2;
                var cat = PlaceMonster(engine, o, DarkCatapulter, 0, BattlePosition.Defense, true);
                PlaceCounters(engine, o, cat, 2);
                var theirs = FaceUpSpell(engine, o, PoisonFangs, 0);
                var monster = InGraveyard(engine, o, CelticGuardian);
                var g1 = InGraveyard(engine, o, PotOfGreed);
                var g2 = InGraveyard(engine, o, PoisonFangs);
                var act = engine.TryActivateSpellTrap(o, cat, fromHand: false);
                Settle(engine);
                Check("Dark Catapulter (AI): 2 counters → banishes 2 GY cards and destroys your 2 Spell/Trap Cards",
                    reached && main && act && Banished(o, g1) && Banished(o, g2) && InGy(o, monster) &&
                    InGy(p, mine1) && InGy(p, mine2) && OnField(o, theirs) && cat.Counters == 0,
                    $"main={main} act={act} g1={Banished(o, g1)} g2={Banished(o, g2)} mine1={InGy(p, mine1)} " +
                    $"mine2={InGy(p, mine2)} theirs={OnField(o, theirs)} counters={cat.Counters}");
            }
        }

        // ───────────────────────────── AI: Tetran / Catapulter ─────────────────────────────

        /// <summary>
        /// The rules let both cards target your own Spell/Traps; the AI (SimpleAi monster ignitions) uses them only on its
        /// opponent's (review finding: with only its own backrow on the field it destroyed it).
        /// </summary>
        static void AiSpellTrapTargets(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var main = ReachOpponentMain(engine);
            ClearBoard(engine);
            var cat = PlaceMonster(engine, o, DarkCatapulter, 0, BattlePosition.Defense, true);
            PlaceCounters(engine, o, cat, 1);
            var tet = PlaceMonster(engine, o, Tetran, 1, BattlePosition.Attack, true);
            PlaceCounters(engine, o, tet, 3);
            var own1 = FaceUpSpell(engine, o, PoisonFangs, 0);
            var own2 = FaceUpSpell(engine, o, PoisonFangs, 1);
            InGraveyard(engine, o, CelticGuardian);
            var legal = engine.CanActivateSpellTrap(o, cat, fromHand: false) &&
                        engine.CanActivateSpellTrap(o, tet, fromHand: false);
            var skipsOwnOnly = CrvCountersAndPhaseTriggersRuntime.AiSkipsIgnition(engine, o, cat) &&
                               CrvCountersAndPhaseTriggersRuntime.AiSkipsIgnition(engine, o, tet);
            var yours = PlaceSetTrap(engine, p, PoisonFangs, 0);
            var usesCat = !CrvCountersAndPhaseTriggersRuntime.AiSkipsIgnition(engine, o, cat);
            var usesTet = !CrvCountersAndPhaseTriggersRuntime.AiSkipsIgnition(engine, o, tet);
            PlaceCounters(engine, o, cat, 1);
            var catNeedsTwo = CrvCountersAndPhaseTriggersRuntime.AiSkipsIgnition(engine, o, cat);
            var act = engine.TryActivateSpellTrap(o, tet, fromHand: false);
            var pending = engine.IsAwaitingEffectTarget;
            Settle(engine);
            Check("AI: Tetran / Dark Catapulter are legal on its own Spell/Traps but the AI skips them; with 1 of yours it " +
                  "uses Tetran on it (Catapulter with 2 counters still waits)",
                reached && main && legal && skipsOwnOnly && usesCat && usesTet && catNeedsTwo && act && !pending &&
                InGy(p, yours) && OnField(o, own1) && OnField(o, own2),
                $"main={main} legal={legal} skipsOwnOnly={skipsOwnOnly} usesCat={usesCat} usesTet={usesTet} " +
                $"catNeedsTwo={catNeedsTwo} act={act} pending={pending} yours={InGy(p, yours)} own1={OnField(o, own1)} " +
                $"own2={OnField(o, own2)}");
        }

        // ───────────────────────────── Ebon Magician Curran ─────────────────────────────

        static void CurranDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            PlaceMonster(engine, p, EbonCurran, 0, BattlePosition.Attack, true);
            var faceDown = PlaceMonster(engine, p, EbonCurran, 1, BattlePosition.Defense, false);
            PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
            PlaceMonster(engine, o, BasicInsect, 1, BattlePosition.Defense, false);
            var pLp = p.LifePoints;
            var oLp = o.LifePoints;
            var next = NextPlayerMain1(engine);
            Check("Ebon Magician Curran: your Standby Phase, 300 x 2 monsters (a face-down one counts); a face-down copy " +
                  "and the opponent's turn deal nothing",
                reached && next && o.LifePoints == oLp - 600 && p.LifePoints == pLp && !faceDown.FaceUp,
                $"next={next} oLp={o.LifePoints} was {oLp} pLp={p.LifePoints} was {pLp}");
        }

        // ───────────────────────────── Cyber Archfiend ─────────────────────────────

        static void CyberArchfiendDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var ca = PlaceMonster(engine, p, CyberArchfiend, 0, BattlePosition.Defense, true);
                engine.TryEndTurnSafe(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var survivedEmpty = OnField(p, ca);
                engine.Opponent.Hand.Clear();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.Opponent);
                var asked = engine.TurnPlayer == p && engine.Phase == DuelPhase.Draw && engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation.LegalTargets.Contains(ca);
                var handBefore = p.Hand.Count;
                var yes = asked && engine.TrySelectEffectTarget(ca);
                Settle(engine);
                var hand = p.Hand.Count;
                var main = engine.Phase == DuelPhase.Main1;
                engine.TryEndTurnSafe(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Cyber Archfiend: empty hand at your Draw Phase → draw 1 more (yes), then the normal draw; your " +
                      "End Phase with cards in hand destroys it (not with an empty hand)",
                    reached && survivedEmpty && asked && handBefore == 0 && yes && hand == 2 && main && InGy(p, ca),
                    $"survivedEmpty={survivedEmpty} asked={asked} handBefore={handBefore} yes={yes} hand={hand} " +
                    $"main={main} phase={engine.Phase} inGy={InGy(p, ca)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var ca = PlaceMonster(engine, p, CyberArchfiend, 0, BattlePosition.Defense, true);
                engine.TryEndTurnSafe(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.Opponent.Hand.Clear();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.Opponent);
                var asked = engine.IsAwaitingEffectTarget && engine.Phase == DuelPhase.Draw;
                var no = asked && engine.CancelEffectTargeting();
                Settle(engine);
                Check("Cyber Archfiend: Cancel declines — only the normal draw",
                    reached && asked && no && p.Hand.Count == 1 && engine.Phase == DuelPhase.Main1 && OnField(p, ca),
                    $"asked={asked} no={no} hand={p.Hand.Count} phase={engine.Phase}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, CyberArchfiend, 0, BattlePosition.Defense, true);
                engine.TryEndTurnSafe(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.Opponent.Hand.Clear();
                PutInHand(engine, p, CelticGuardian);
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.Opponent);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Cyber Archfiend: a card in hand as the Draw Phase begins — no extra draw",
                    reached && !engine.IsAwaitingEffectTarget && p.Hand.Count == 2 && engine.Phase == DuelPhase.Main1,
                    $"pending={engine.IsAwaitingEffectTarget} hand={p.Hand.Count} phase={engine.Phase}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                PlaceMonster(engine, o, CyberArchfiend, 0, BattlePosition.Defense, true);
                var main = ReachOpponentMain(engine);
                Check("Cyber Archfiend (AI): empty hand at its Draw Phase → 2 cards in hand",
                    reached && main && o.Hand.Count == 2, $"main={main} hand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── Rapid-Fire Magician ─────────────────────────────

        static void RapidFireDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            PlaceMonster(engine, p, RapidFire, 0, BattlePosition.Attack, true);
            PlaceMonster(engine, p, RapidFire, 1, BattlePosition.Defense, false);
            PlaceMonster(engine, o, RapidFire, 0, BattlePosition.Attack, true);
            var oLp = o.LifePoints;
            var pLp = p.LifePoints;
            var pot = PutInHand(engine, p, PotOfGreed);
            var cast = engine.TryActivateSpellTrap(p, pot, fromHand: true);
            Settle(engine);
            var afterPot = o.LifePoints;
            var drew = p.Hand.Count;
            var fangs = PutInHand(engine, p, PoisonFangs);
            var cont = engine.TryActivateSpellTrap(p, fangs, fromHand: true);
            Settle(engine);
            Check("Rapid-Fire Magician: your Normal Spell resolves → 400 (the face-up copy only); a Continuous Spell and " +
                  "the opponent's copy deal nothing",
                reached && cast && drew == 2 && afterPot == oLp - 400 && cont && o.LifePoints == afterPot &&
                p.LifePoints == pLp,
                $"cast={cast} drew={drew} afterPot={afterPot} was {oLp} cont={cont} oLp={o.LifePoints} pLp={p.LifePoints}");
        }
    }
}
