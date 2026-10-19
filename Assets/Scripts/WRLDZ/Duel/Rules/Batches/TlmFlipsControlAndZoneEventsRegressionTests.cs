using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TlmFlipsControlAndZoneEvents batch (TLM): White Ninja (FLIP, destroy a targeted Defense Position monster), Dummy
    /// Golem (FLIP, the opponent selects a monster to switch with it), Reshef the Dark Being (once per turn, discard a Spell:
    /// take control until the End Phase), Criosphinx (a monster returned to the hand: its owner sends 1 hand card to the
    /// GY) and D.D. Survivor (banished face-up from your field: Special Summoned in the End Phase). Compile shapes,
    /// mini-duels through real engine entry points (MonsterEffects.OnFlipSummoned, TryActivateSpellTrap,
    /// TrySelectEffectTarget, DuelEngine.ReturnCardToHand, BanishCard, the End Phase) and fail-closed near misses.
    /// Runtime: <see cref="TlmFlipsControlAndZoneEventsRuntime"/>.
    /// </summary>
    public static class TlmFlipsControlAndZoneEventsRegressionTests
    {
        const int WhiteNinja = 1571945;
        const int DummyGolem = 13532663;
        const int Reshef = 62420419;
        const int Criosphinx = 18654201;
        const int DdSurvivor = 48092532;

        const int Celtic = 91152256;
        const int BlueEyes = 89631139;
        const int MysticalElf = 15025844;
        const int Kuriboh = 40640057;
        const int PotOfGreed = 55144522;
        const int ManEaterBug = 54652250;
        const int ScoutPlane = 3773196;
        const int CompulsoryEvacuation = 94192409;
        const int MirrorForce = 44095762;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            WhiteNinjaDuels(db, pDeck, aDeck, Check);
            DummyGolemDuels(db, pDeck, aDeck, Check);
            ReshefDuels(db, pDeck, aDeck, Check);
            CriosphinxDuels(db, pDeck, aDeck, Check);
            SurvivorDuels(db, pDeck, aDeck, Check);
        }

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : $"full={p.FullyCompiled} " + string.Join("; ",
                p.ClauseList.Select(c => $"{c.Timing}/{c.Action}/{c.Zone}"));

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        // ───────────────────────────── Compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            CompiledCardProgram Prog(int id) => db.Get(id) is { } d ? CardTextEffectCompiler.Compile(d) : null;
            bool Implemented(int id) => db.Get(id) is { } d &&
                                        CardEffectStatus.Classify(d) == CardEffectStatusKind.Implemented;

            var ninja = Prog(WhiteNinja);
            Check("White Ninja: one FLIP Destroy clause targeting a Defense Position monster on either field",
                ninja != null && ninja.FullyCompiled && ninja.ClauseList.Count == 1 &&
                ninja.ClauseList[0].Timing == EffectTiming.Flip &&
                ninja.ClauseList[0].Action == EffectActionKind.Destroy &&
                ninja.ClauseList[0].Zone == EffectZoneFilter.FieldDefensePositionMonsters &&
                ninja.ClauseList[0].RequiresTargetChoice && ninja.ClauseList[0].IsPsctTarget &&
                TargetingTruthRuntime.ClauseTargets(ninja.ClauseList[0]) && Implemented(WhiteNinja),
                Describe(ninja));

            var golem = Prog(DummyGolem);
            Check("Dummy Golem: one FLIP clause, no target, the opponent selects at resolution",
                golem != null && golem.FullyCompiled && golem.ClauseList.Count == 1 &&
                golem.ClauseList[0].Timing == EffectTiming.Flip &&
                golem.ClauseList[0].Action == EffectActionKind.OpponentSelectsOwnMonsterSwitchControlWithThis &&
                !golem.ClauseList[0].RequiresTargetChoice && Implemented(DummyGolem),
                Describe(golem));

            var reshef = Prog(Reshef);
            var rc = reshef?.ClauseList.FirstOrDefault();
            Check("Reshef the Dark Being: soft once-per-turn ignition, discard 1 Spell, target 1 opponent's monster, " +
                  "take control until the End Phase",
                reshef != null && reshef.FullyCompiled && reshef.ClauseList.Count == 1 &&
                rc.Timing == EffectTiming.Activate && rc.Action == EffectActionKind.TakeControlUntilEndPhase &&
                rc.Zone == EffectZoneFilter.OppMonsters && rc.RequiresTargetChoice && rc.IsPsctTarget &&
                rc.RequiresDiscardCost && rc.DiscardCostAttribute == FaceUpQuickEffectsRuntime.DiscardSpellCard &&
                rc.OncePerTurn && Implemented(Reshef),
                Describe(reshef));

            var crio = Prog(Criosphinx);
            Check("Criosphinx: one face-up watcher clause (the trigger is fired by the return event)",
                crio != null && crio.FullyCompiled && crio.ClauseList.Count == 1 &&
                crio.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                crio.ClauseList[0].Action == EffectActionKind.OwnersOfMonstersReturnedToHandSendOneHandCardToGy &&
                Implemented(Criosphinx),
                Describe(crio));

            var dds = Prog(DdSurvivor);
            var dc = dds?.ClauseList.FirstOrDefault();
            Check("D.D. Survivor: End Phase return from the banished pile, banished face-up from your field this turn, " +
                  "once per turn, face-up position of choice",
                dds != null && dds.FullyCompiled && dds.ClauseList.Count == 1 &&
                dc.Timing == EffectTiming.EndPhase && dc.Action == EffectActionKind.SpecialSummonThisFromBanished &&
                dc.RequiresBanishedThisTurn && dc.RequiresBanishedFaceUpFromOwnField && dc.OncePerTurn &&
                !dc.SummonInAttack && Implemented(DdSurvivor),
                Describe(dds));

            // Near misses: the look-alike sentences keep their own shapes.
            var bug = Prog(ManEaterBug);
            Check("Near miss: Man-Eater Bug still targets any monster (FieldAnyMonster), not only Defense Position ones",
                bug != null && bug.ClauseList.Exists(c => c.Action == EffectActionKind.Destroy &&
                                                          c.Zone == EffectZoneFilter.FieldAnyMonster),
                Describe(bug));
            var plane = Prog(ScoutPlane);
            Check("Near miss: D.D. Scout Plane's return does not ask for a face-up banish from the field",
                plane != null && plane.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonThisFromBanished &&
                                                              !c.RequiresBanishedFaceUpFromOwnField && c.SummonInAttack),
                Describe(plane));
            if (db.Get(WhiteNinja) is { } wn)
            {
                var variant = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 990001,
                    name = "Ninja variant",
                    type = wn.type,
                    race = wn.race,
                    attribute = wn.attribute,
                    level = wn.level,
                    atk = wn.atk,
                    def = wn.def,
                    desc = "FLIP: Target 1 Defense Position monster on the field; destroy that target, then draw 1 card."
                });
                Check("Near miss: a longer White Ninja-like text is not compiled whole by its atom",
                    variant == null || !variant.FullyCompiled ||
                    !variant.ClauseList.Exists(c => c.Zone == EffectZoneFilter.FieldDefensePositionMonsters),
                    Describe(variant));
            }
        }

        // ───────────────────────────── White Ninja ─────────────────────────────

        static void WhiteNinjaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            // The AI's White Ninja: it destroys the player's face-down Defense monster, never the Attack Position one.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var attacker = PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
                var setOne = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Defense, false);
                var ninja = PlaceMonster(engine, o, WhiteNinja, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, o, ninja);
                PassAll(engine);
                Check("White Ninja (AI): the face-down Defense Position monster is destroyed; the Attack one stays",
                    !p.TryFindMonster(setOne, out _) && p.Graveyard.Contains(setOne) && p.TryFindMonster(attacker, out _),
                    $"set={p.TryFindMonster(setOne, out _)} atk={p.TryFindMonster(attacker, out _)} gy={p.Graveyard.Count}");
            }

            // The player's White Ninja: only Defense Position monsters are offered (its own face-up Defense self too).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var oppAttack = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                var oppDefense = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, true);
                var ninja = PlaceMonster(engine, p, WhiteNinja, 2, BattlePosition.Defense, true);
                MonsterEffects.OnFlipSummoned(engine, p, ninja);
                var pend = engine.PendingActivation;
                var offered = pend?.LegalTargets?.ToList() ?? new System.Collections.Generic.List<CardInstance>();
                var offersRight = offered.Contains(oppDefense) && offered.Contains(ninja) && !offered.Contains(oppAttack);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(oppDefense);
                PassAll(engine);
                Check("White Ninja (player): Defense Position monsters on both fields (itself included) are offered; " +
                      "the chosen one is destroyed",
                    offersRight && !o.TryFindMonster(oppDefense, out _) && o.Graveyard.Contains(oppDefense) &&
                    o.TryFindMonster(oppAttack, out _) && p.TryFindMonster(ninja, out _),
                    $"offered={string.Join(",", offered.Select(c => c.Name))} def={o.TryFindMonster(oppDefense, out _)}");
            }

            // No Defense Position monster: the FLIP effect resolves with no effect.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var oppAttack = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                var ninja = PlaceMonster(engine, p, WhiteNinja, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(engine, p, ninja);
                PassAll(engine);
                Check("White Ninja: with no Defense Position monster nothing is destroyed and nothing waits",
                    !engine.IsAwaitingEffectTarget && o.TryFindMonster(oppAttack, out _) && p.TryFindMonster(ninja, out _),
                    $"pending={engine.IsAwaitingEffectTarget}");
            }

            // Resolution re-check: a target changed to Attack Position before it resolves is not destroyed.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Defense, true);
                var ninja = PlaceMonster(engine, p, WhiteNinja, 2, BattlePosition.Attack, true);
                var clause = CardTextEffectCompiler.Compile(db.Get(WhiteNinja)).ClauseList[0];
                target.Position = BattlePosition.Attack;
                var d = false;
                TextEffectRuntime.ApplyClause(engine, p, ninja, clause, target, ref d, ref d);
                Check("White Ninja: a target no longer in Defense Position is not destroyed (Lua IsDefensePos)",
                    o.TryFindMonster(target, out _), $"onField={o.TryFindMonster(target, out _)}");
            }
        }

        // ───────────────────────────── Dummy Golem ─────────────────────────────

        static void DummyGolemDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            // The player's Dummy Golem: the AI opponent gives away its weakest monster.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var strong = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                var weak = PlaceMonster(engine, o, Kuriboh, 1, BattlePosition.Defense, false);
                var golem = PlaceMonster(engine, p, DummyGolem, 2, BattlePosition.Defense, true);
                MonsterEffects.OnFlipSummoned(engine, p, golem);
                PassAll(engine);
                Check("Dummy Golem (player): the AI selects its weakest monster; it and Dummy Golem switch control",
                    p.TryFindMonster(weak, out _) && o.TryFindMonster(golem, out _) && o.TryFindMonster(strong, out _) &&
                    !engine.IsAwaitingEffectTarget,
                    $"weakMine={p.TryFindMonster(weak, out _)} golemTheirs={o.TryFindMonster(golem, out _)}");
            }

            // The AI's Dummy Golem: the player selects which of their monsters to give.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var mine1 = PlaceMonster(engine, p, BlueEyes, 0, BattlePosition.Attack, true);
                var mine2 = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var golem = PlaceMonster(engine, o, DummyGolem, 2, BattlePosition.Defense, true);
                MonsterEffects.OnFlipSummoned(engine, o, golem);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.Controller == p && pend.LegalTargets.Contains(mine1) &&
                            pend.LegalTargets.Contains(mine2) && !pend.LegalTargets.Contains(golem);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(mine2);
                PassAll(engine);
                Check("Dummy Golem (AI): the player chooses 1 of their monsters; that one and Dummy Golem switch",
                    asked && o.TryFindMonster(mine2, out _) && p.TryFindMonster(golem, out _) &&
                    p.TryFindMonster(mine1, out _),
                    $"asked={asked} mine2Theirs={o.TryFindMonster(mine2, out _)} golemMine={p.TryFindMonster(golem, out _)}");
            }

            // The opponent controls no monster: nothing switches.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var golem = PlaceMonster(engine, p, DummyGolem, 2, BattlePosition.Defense, true);
                MonsterEffects.OnFlipSummoned(engine, p, golem);
                PassAll(engine);
                Check("Dummy Golem: with no opponent's monster it stays with its controller",
                    p.TryFindMonster(golem, out _) && !engine.IsAwaitingEffectTarget, "");
            }
        }

        // ───────────────────────────── Reshef the Dark Being ─────────────────────────────

        static void ReshefDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            // The player: only a Spell can be discarded; then the opponent's face-down monster is taken until the End Phase.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var monsterInHand = PutInHand(engine, p, Celtic);
                var spell = PutInHand(engine, p, PotOfGreed);
                var reshef = PlaceMonster(engine, p, Reshef, 0, BattlePosition.Attack, true);
                var stolen = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, false);
                var myTurn = engine.TurnPlayer == p && engine.InMainPhase;
                var can = engine.CanActivateSpellTrap(p, reshef, fromHand: false);
                engine.TryActivateSpellTrap(p, reshef, fromHand: false);
                var costPick = engine.PendingActivation;
                var onlySpell = costPick != null && costPick.LegalTargets.Contains(spell) &&
                                !costPick.LegalTargets.Contains(monsterInHand) &&
                                (costPick.Prompt ?? "").IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0;
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(spell);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(stolen);
                PassAll(engine);
                Check("Reshef: discard 1 Spell (cost), then take the opponent's face-down monster until the End Phase",
                    myTurn && can && onlySpell && p.Graveyard.Contains(spell) && p.Hand.Contains(monsterInHand) &&
                    p.TryFindMonster(stolen, out _) && stolen.ControlRevertsAtEndPhase,
                    $"turn={myTurn} can={can} onlySpell={onlySpell} prompt={costPick?.Prompt} " +
                    $"spellGy={p.Graveyard.Contains(spell)} stolen={p.TryFindMonster(stolen, out _)}");
                PutInHand(engine, p, PotOfGreed);
                PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                Check("Reshef: once per turn (a second Spell and target do not allow a second activation)",
                    !engine.CanActivateSpellTrap(p, reshef, fromHand: false), $"used={reshef.EffectUsedThisTurn}");
            }

            // No Spell in hand: it cannot be activated.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                PutInHand(engine, p, Celtic);
                var reshef = PlaceMonster(engine, p, Reshef, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                Check("Reshef: with no Spell Card in hand it cannot be activated (a monster is not its cost)",
                    !engine.CanActivateSpellTrap(p, reshef, fromHand: false), "");
            }

            // The AI's Reshef on its own turn: it discards its Spell and takes the player's monster.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    o.Hand.Clear();
                    var spell = PutInHand(engine, o, PotOfGreed);
                    var reshef = PlaceMonster(engine, o, Reshef, 0, BattlePosition.Attack, true);
                    var mine = PlaceMonster(engine, p, BlueEyes, 1, BattlePosition.Attack, true);
                    engine.TryActivateSpellTrap(o, reshef, fromHand: false);
                    PassAll(engine);
                    Check("Reshef (AI): its Spell goes to the GY and it takes control of the player's monster",
                        o.Graveyard.Contains(spell) && o.TryFindMonster(mine, out _), $"mine={o.TryFindMonster(mine, out _)}");
                }
                else
                    Check("Reshef (AI): reached the opponent's Main Phase", false, "");
            }
        }

        // ───────────────────────────── Criosphinx ─────────────────────────────

        static void CriosphinxDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            // An opponent's monster returned to their hand: they send 1 card from their hand to the GY.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, Criosphinx, 0, BattlePosition.Defense, true);
                var theirs = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                var gy = o.Graveyard.Count;
                var pgy = p.Graveyard.Count;
                engine.ReturnCardToHand(theirs, by: p);
                PassAll(engine);
                Check("Criosphinx: the owner of the returned monster sends 1 card from their hand to the GY",
                    o.Graveyard.Count == gy + 1 && o.Hand.Count == 1 && p.Graveyard.Count == pgy,
                    $"gy={o.Graveyard.Count} was={gy} hand={o.Hand.Count}");
            }

            // One group return of 2 opponent's monsters and 1 of the player's: each owner sends exactly 1 card.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, o, Criosphinx, 0, BattlePosition.Defense, true);
                var t1 = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var t2 = PlaceMonster(engine, o, Kuriboh, 2, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Attack, true);
                o.Hand.Clear();
                p.Hand.Clear();
                var ogy = o.Graveyard.Count;
                var pgy = p.Graveyard.Count;
                TextEffectRuntime.BeginMonsterReturnToOwnerHandBatch();
                try
                {
                    engine.ReturnCardToHand(t1, by: p);
                    engine.ReturnCardToHand(t2, by: p);
                    engine.ReturnCardToHand(mine, by: p);
                }
                finally
                {
                    TextEffectRuntime.EndMonsterReturnToOwnerHandBatch(engine);
                }

                PassAll(engine);
                Check("Criosphinx: one return event — each owner sends 1 card (not one per monster)",
                    o.Graveyard.Count == ogy + 1 && o.Hand.Count == 1 && p.Graveyard.Count == pgy + 1 && p.Hand.Count == 0,
                    $"ogy={o.Graveyard.Count}/{ogy} ohand={o.Hand.Count} pgy={p.Graveyard.Count}/{pgy} phand={p.Hand.Count}");
            }

            // The player chooses which card to send.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, o, Criosphinx, 0, BattlePosition.Defense, true);
                var mine = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                p.Hand.Clear();
                var keep = PutInHand(engine, p, BlueEyes);
                var send = PutInHand(engine, p, PotOfGreed);
                engine.ReturnCardToHand(mine, by: o);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.Controller == p && pend.LegalTargets.Contains(send) &&
                            pend.LegalTargets.Contains(keep) && pend.LegalTargets.Contains(mine);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(send);
                PassAll(engine);
                Check("Criosphinx: the player picks the card to send from their hand",
                    asked && p.Graveyard.Contains(send) && p.Hand.Contains(keep) && p.Hand.Contains(mine),
                    $"asked={asked} sent={p.Graveyard.Contains(send)}");
            }

            // A bounce made by a resolving Chain Link (Compulsory Evacuation Device): Criosphinx's trigger waits for that
            // Chain, then resolves on a Chain of its own (the opponent holds a Set Trap, so it may respond).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, Criosphinx, 0, BattlePosition.Defense, true);
                var theirs = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                PlaceSetTrap(engine, o, MirrorForce, 0);
                var ced = PlaceSetTrap(engine, p, CompulsoryEvacuation, 0);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                var gy = o.Graveyard.Count;
                engine.TryActivateSpellTrap(p, ced, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(theirs);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Criosphinx: a bounce by a resolving Chain Link — the owner still sends 1 card once that Chain is over",
                    o.Hand.Contains(theirs) && o.Graveyard.Count == gy + 1 && o.Hand.Count == 1 &&
                    !engine.Chain.HasLinks && !engine.IsAwaitingEffectTarget,
                    $"returned={o.Hand.Contains(theirs)} gy={o.Graveyard.Count}/{gy} hand={o.Hand.Count} " +
                    $"links={engine.Chain.HasLinks}");
            }

            // Near misses: a face-down Criosphinx, or Criosphinx itself returned, does nothing.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, Criosphinx, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                var gy = o.Graveyard.Count;
                engine.ReturnCardToHand(theirs, by: p);
                PassAll(engine);
                Check("Criosphinx: face-down, its effect does not apply",
                    o.Graveyard.Count == gy && o.Hand.Count == 2, $"gy={o.Graveyard.Count} hand={o.Hand.Count}");
            }
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var crio = PlaceMonster(engine, p, Criosphinx, 0, BattlePosition.Defense, true);
                p.Hand.Clear();
                PutInHand(engine, p, Kuriboh);
                var gy = p.Graveyard.Count;
                engine.ReturnCardToHand(crio, by: engine.Opponent);
                PassAll(engine);
                Check("Criosphinx: returned itself, it is no longer on the field to trigger",
                    p.Graveyard.Count == gy && p.Hand.Count == 2, $"gy={p.Graveyard.Count} hand={p.Hand.Count}");
            }
        }

        // ───────────────────────────── D.D. Survivor ─────────────────────────────

        static void SurvivorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            // The AI's D.D. Survivor banished face-up from its field: back in the End Phase (face-up Attack).
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var o = engine.Opponent;
                    var dds = PlaceMonster(engine, o, DdSurvivor, 0, BattlePosition.Attack, true);
                    engine.BanishCard(o, dds, by: engine.Player);
                    var banished = o.Banished.Contains(dds);
                    engine.TryEndTurnSafe(o);
                    PassAll(engine);
                    Check("D.D. Survivor (AI): banished face-up from its field → Special Summoned in the End Phase",
                        banished && o.TryFindMonster(dds, out _) && dds.FaceUp && !o.Banished.Contains(dds),
                        $"banished={banished} onField={o.TryFindMonster(dds, out _)}");
                }
                else
                    Check("D.D. Survivor (AI): reached the opponent's Main Phase", false, "");
            }

            // Near misses: banished from a face-down position, or from the GY, it stays banished.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var o = engine.Opponent;
                    var setOne = PlaceMonster(engine, o, DdSurvivor, 0, BattlePosition.Defense, false);
                    var fromGy = engine.CreateCardInstance(DdSurvivor);
                    o.Graveyard.Add(fromGy);
                    engine.BanishCard(o, setOne, by: engine.Player);
                    engine.BanishCard(o, fromGy, by: engine.Player);
                    engine.TryEndTurnSafe(o);
                    PassAll(engine);
                    Check("D.D. Survivor: banished while face-down, or from the GY, it is not Special Summoned",
                        o.Banished.Contains(setOne) && o.Banished.Contains(fromGy) &&
                        !o.TryFindMonster(setOne, out _) && !o.TryFindMonster(fromGy, out _),
                        $"set={o.Banished.Contains(setOne)} gy={o.Banished.Contains(fromGy)}");
                }
                else
                    Check("D.D. Survivor near miss: reached the opponent's Main Phase", false, "");
            }

            // No free Monster Zone: it is sent to the GY instead.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var o = engine.Opponent;
                    var dds = PlaceMonster(engine, o, DdSurvivor, 0, BattlePosition.Attack, true);
                    engine.BanishCard(o, dds, by: engine.Player);
                    for (var z = 0; z < o.MonsterZones.Length; z++)
                        if (o.MonsterZones[z].Occupant == null)
                            PlaceMonster(engine, o, Celtic, z, BattlePosition.Attack, true);
                    engine.TryEndTurnSafe(o);
                    PassAll(engine);
                    Check("D.D. Survivor: with no free Monster Zone it is sent to the GY",
                        o.Graveyard.Contains(dds) && !o.Banished.Contains(dds), $"gy={o.Graveyard.Contains(dds)}");
                }
                else
                    Check("D.D. Survivor zone check: reached the opponent's Main Phase", false, "");
            }

            // The player's D.D. Survivor: the player taps it and picks face-up Defense.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var dds = PlaceMonster(engine, p, DdSurvivor, 0, BattlePosition.Attack, true);
                engine.BanishCard(p, dds, by: engine.Opponent);
                var myTurn = engine.TurnPlayer == p;
                engine.TryEndTurnSafe(p);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.Controller == p && pend.LegalTargets.Contains(dds);
                if (asked)
                {
                    engine.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    engine.TrySelectEffectTarget(dds);
                }

                PassAll(engine);
                Check("D.D. Survivor (player): asked in the End Phase, Special Summoned in face-up Defense Position",
                    myTurn && asked && p.TryFindMonster(dds, out _) && dds.FaceUp &&
                    dds.Position == BattlePosition.Defense,
                    $"turn={myTurn} asked={asked} onField={p.TryFindMonster(dds, out _)} pos={dds.Position}");
            }
        }
    }
}
