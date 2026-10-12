using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// BattleDamageEvents batch (IOC / AST): Begone, Knave!, Robbin' Zombie, Sasuke Samurai #3, Aswan Apparition, Nubian
    /// Guard, Des Counterblow, Piranha Army and Stone Statue of the Aztecs. Compile shapes, mini-duels through the real
    /// engine entry points (TryActivateSpellTrap, TryAttack, TrySelectEffectTarget / CancelEffectTargeting) and
    /// fail-closed near misses. Runtime: <see cref="BattleDamageEventsRuntime"/>.
    /// </summary>
    public static class BattleDamageEventsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BegoneKnave = 20374520;
        const int RobbinZombie = 83258273;
        const int Sasuke3 = 77379481;         // 1000/1000
        const int Aswan = 88236094;           // 500/500
        const int NubianGuard = 51616747;     // 500/500
        const int DesCounterblow = 39131963;
        const int PiranhaArmy = 50823978;     // 800/200
        const int StoneStatue = 31812496;     // 300/2000

        const int Celtic = 91152256;          // 1400/1200
        const int Timeater = 44913552;        // destroys by battle: its opponent skips their next Main Phase 1
        const int OjamaGreen = 12482652;      // 0/1000
        const int TrapHole = 4206964;         // Normal Trap
        const int PotOfGreed = 55144522;      // Normal Spell
        const int MessengerOfPeace = 44656491; // Continuous Spell
        const int GravityBind = 85742772;     // Continuous Trap

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("BattleDamageEvents: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            DoublerDuels(db, pDeck, aDeck, Check);
            SasukeDuels(db, pDeck, aDeck, Check);
            GyToDeckTopDuels(db, pDeck, aDeck, Check);
            RobbinZombieDuels(db, pDeck, aDeck, Check);
            BegoneAndCounterblowDuels(db, pDeck, aDeck, Check);
            NearMisses(Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static bool Has(CompiledCardProgram prog, Func<EffectClause, bool> shape) =>
            prog != null && prog.ClauseList.Exists(c => c != null && shape(c));

        /// <summary>The <paramref name="turnOf"/> player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards and
        /// hands empty.</summary>
        static DuelEngine Main1Of(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool playerTurn, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            bool At() => engine.TurnPlayer == (playerTurn ? engine.Player : engine.Opponent) &&
                         engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            for (var t = 0; t < 10 && !At(); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = At();
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
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

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool Activate(DuelEngine engine, DuelistState who, int id, int zone, out CardInstance card)
        {
            card = PlaceSetTrap(engine, who, id, zone);
            var ok = engine.TryActivateSpellTrap(who, card, false);
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            return ok && card.FaceUp && who.TryFindSpellTrap(card, out _);
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Prog(int id) => CardTextEffectCompiler.Compile(db.Get(id));

            bool One(int id, Func<EffectClause, bool> shape, out CompiledCardProgram p)
            {
                p = Prog(id);
                return p != null && p.FullyCompiled && p.ClauseList.Count == 1 && shape(p.ClauseList[0]) &&
                       CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;
            }

            Check("Begone, Knave!: any monster's battle damage → return that monster to the hand (no target)",
                One(BegoneKnave, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                      c.Action == EffectActionKind.ReturnToHand && c.AnyMonsterBattleDamageToAnyPlayer &&
                                      !c.RequiresDirectAttack && !c.RequiresTargetChoice &&
                                      !c.AnyControllerMonsterBattleDamage, out var begone), Describe(begone));
            Check("Des Counterblow: a direct attack's battle damage → destroy that monster (no target)",
                One(DesCounterblow, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                         c.Action == EffectActionKind.Destroy && c.AnyMonsterBattleDamageToAnyPlayer &&
                                         c.RequiresDirectAttack && !c.RequiresTargetChoice, out var des), Describe(des));
            Check("Robbin' Zombie: your monster's battle damage → mill the top card of the opponent's Deck",
                One(RobbinZombie, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                       c.Action == EffectActionKind.SendFromTopOfDeckToGy &&
                                       c.Side == EffectSide.Opponent && c.Amount == 1 &&
                                       c.AnyControllerMonsterBattleDamage && !c.AnyMonsterBattleDamageToAnyPlayer,
                    out var zombie), Describe(zombie));
            Check("Sasuke Samurai #3: battle damage → the opponent draws until 7 in hand",
                One(Sasuke3, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                  c.Action == EffectActionKind.DrawUntilHandSize && c.Side == EffectSide.Opponent &&
                                  c.Amount == 7 && !c.IsOptional, out var sasuke), Describe(sasuke));
            Check("Aswan Apparition: optional targeted Trap in your GY → top of the Deck",
                One(Aswan, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                c.Action == EffectActionKind.PlaceTargetOnTopOfDeck &&
                                c.Zone == EffectZoneFilter.ControllerGyTraps && c.TargetCardKind == "Trap" &&
                                c.IsOptional && c.RequiresTargetChoice && c.IsPsctTarget, out var aswan), Describe(aswan));
            Check("Nubian Guard: optional targeted Continuous Spell in your GY → top of the Deck",
                One(NubianGuard, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                      c.Action == EffectActionKind.PlaceTargetOnTopOfDeck &&
                                      c.Zone == EffectZoneFilter.ControllerGySpells &&
                                      c.TargetCardKind == BattleDamageEventsRuntime.ContinuousSpellKind &&
                                      c.IsOptional && c.RequiresTargetChoice, out var nubian), Describe(nubian));
            Check("Piranha Army: continuous doubling of its direct attack's damage",
                One(PiranhaArmy, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                      c.Action == EffectActionKind.DoubleBattleDamageToOpponent &&
                                      c.RequiresDirectAttack && !c.RequiresThisIsAttackTarget, out var piranha),
                Describe(piranha));
            Check("Stone Statue of the Aztecs: continuous doubling while it is the attack target",
                One(StoneStatue, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                      c.Action == EffectActionKind.DoubleBattleDamageToOpponent &&
                                      c.RequiresThisIsAttackTarget && !c.RequiresDirectAttack, out var statue),
                Describe(statue));
        }

        // ───────────────────────────── Piranha Army / Stone Statue ─────────────────────────────

        static void DoublerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var piranha = PlaceMonster(engine, p, PiranhaArmy, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, p, piranha, null);
                Check("Piranha Army: its direct attack inflicts double (800 → 1600)",
                    reached && hit && o.LifePoints == lpO - 1600 && p.LifePoints == lpP,
                    $"hit={hit} lpO={lpO}->{o.LifePoints}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var piranha = PlaceMonster(engine, p, PiranhaArmy, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, piranha, ojama);
                Check("Piranha Army: attacking a monster is not doubled (800 vs 0 ATK → 800)",
                    reached && hit && o.LifePoints == lpO - 800, $"hit={hit} lpO={lpO}->{o.LifePoints}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var piranha = PlaceMonster(engine, p, PiranhaArmy, 0, BattlePosition.Attack, true);
                piranha.IsNegated = true;
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, piranha, null);
                Check("Piranha Army: negated, its direct attack inflicts 800",
                    reached && hit && o.LifePoints == lpO - 800, $"hit={hit} lpO={lpO}->{o.LifePoints}");
            }
            {
                // Attacked face-down: flipped face-up before damage calculation; the attacker's controller takes
                // (2000 − 1400) × 2.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var statue = PlaceMonster(engine, o, StoneStatue, 0, BattlePosition.Defense, false);
                var lpP = p.LifePoints;
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, statue);
                Check("Stone Statue of the Aztecs: attacked face-down (DEF 2000 vs 1400) → the attacker takes 1200",
                    reached && hit && p.LifePoints == lpP - 1200 && o.LifePoints == lpO && OnField(o, statue),
                    $"hit={hit} lpP={lpP}->{p.LifePoints} lpO={lpO}->{o.LifePoints}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ojama = PlaceMonster(engine, p, OjamaGreen, 0, BattlePosition.Attack, true);
                var statue = PlaceMonster(engine, o, StoneStatue, 0, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, p, ojama, statue);
                Check("Stone Statue of the Aztecs: in Attack Position vs a 0 ATK attacker → 300 doubled to 600",
                    reached && hit && p.LifePoints == lpP - 600, $"hit={hit} lpP={lpP}->{p.LifePoints}");
            }
            {
                // The damage its controller takes is not doubled.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var statue = PlaceMonster(engine, o, StoneStatue, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, statue);
                Check("Stone Statue of the Aztecs: destroyed by a stronger attacker → its controller takes 1100, not doubled",
                    reached && hit && o.LifePoints == lpO - 1100 && p.LifePoints == lpP && o.Graveyard.Contains(statue),
                    $"hit={hit} lpO={lpO}->{o.LifePoints} lpP={lpP}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Sasuke Samurai #3 ─────────────────────────────

        static void SasukeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var start in new[] { 2, 8 })
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var sasuke = PlaceMonster(engine, p, Sasuke3, 0, BattlePosition.Attack, true);
                for (var i = 0; i < start; i++) PutInHand(engine, o, Celtic);
                var deckO = o.Deck.Count;
                var lpO = o.LifePoints;
                var handP = p.Hand.Count;
                var hit = AttackAndDrain(engine, p, sasuke, null);
                var want = Math.Max(7, start);
                Check(start < 7
                        ? "Sasuke Samurai #3: battle damage with 2 cards in the opponent's hand → they draw 5 (hand of 7)"
                        : "Sasuke Samurai #3: battle damage with 8 cards in the opponent's hand → no draw",
                    reached && hit && o.LifePoints == lpO - 1000 && o.Hand.Count == want &&
                    o.Deck.Count == deckO - (want - start) && p.Hand.Count == handP,
                    $"hit={hit} lpO={lpO}->{o.LifePoints} hand={start}->{o.Hand.Count} deck={deckO}->{o.Deck.Count}");
            }
            {
                // No battle damage (1000 ATK attacking a 1000 DEF monster) → no draw.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var sasuke = PlaceMonster(engine, p, Sasuke3, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Defense, true);
                var hand = o.Hand.Count;
                var hit = AttackAndDrain(engine, p, sasuke, ojama);
                Check("Sasuke Samurai #3: no battle damage (1000 ATK vs 1000 DEF) → no draw",
                    reached && hit && o.Hand.Count == hand, $"hit={hit} hand={hand}->{o.Hand.Count}");
            }
        }

        // ───────────────────────────── Aswan Apparition / Nubian Guard ─────────────────────────────

        static void GyToDeckTopDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var yes in new[] { true, false })
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var aswan = PlaceMonster(engine, p, Aswan, 0, BattlePosition.Attack, true);
                var trap = engine.CreateCardInstance(TrapHole);
                var spell = engine.CreateCardInstance(PotOfGreed);
                p.Graveyard.Add(spell);
                p.Graveyard.Add(trap);
                var deck0 = p.Deck.Count;
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, aswan, null);
                var pend = engine.PendingActivation;
                var offered = pend != null && pend.LegalTargets.Count == 1 && ReferenceEquals(pend.LegalTargets[0], trap);
                if (offered)
                {
                    if (yes) engine.TrySelectEffectTarget(trap);
                    else engine.CancelEffectTargeting();
                }

                DrainCombat(engine);
                var ok = yes
                    ? !p.Graveyard.Contains(trap) && p.Deck.Count == deck0 + 1 && p.Deck[0] == TrapHole
                    : p.Graveyard.Contains(trap) && p.Deck.Count == deck0;
                Check(yes
                        ? "Aswan Apparition: battle damage → a lone Trap in the GY is still asked; Yes puts it on top of the Deck"
                        : "Aswan Apparition: Cancel → not activated, the Trap stays in the GY",
                    reached && hit && o.LifePoints == lpO - 500 && offered && ok && p.Graveyard.Contains(spell) &&
                    engine.PendingActivation == null,
                    $"hit={hit} offered={offered} legal=[{string.Join(",", pend?.LegalTargets.Select(t => t.Name) ?? Array.Empty<string>())}] " +
                    $"deck {deck0}->{p.Deck.Count} top={p.Deck.FirstOrDefault()} pending={engine.PendingActivation != null}");
            }
            {
                // No Trap in the GY: nothing to activate, no prompt.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var aswan = PlaceMonster(engine, p, Aswan, 0, BattlePosition.Attack, true);
                p.Graveyard.Add(engine.CreateCardInstance(PotOfGreed));
                var deck0 = p.Deck.Count;
                var hit = AttackAndDrain(engine, p, aswan, null);
                Check("Aswan Apparition: no Trap in the GY → no prompt, nothing returned",
                    reached && hit && engine.PendingActivation == null && p.Deck.Count == deck0 && p.Graveyard.Count == 1,
                    $"hit={hit} pending={engine.PendingActivation != null} deck {deck0}->{p.Deck.Count}");
            }
            {
                // AI controller: the opponent's Aswan attacks directly and returns its Trap at once.
                var engine = Main1Of(db, pDeck, aDeck, false, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var aswan = PlaceMonster(engine, o, Aswan, 0, BattlePosition.Attack, true);
                var trap = engine.CreateCardInstance(TrapHole);
                o.Graveyard.Add(trap);
                var deck0 = o.Deck.Count;
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, o, aswan, null);
                Check("Aswan Apparition (AI): battle damage → its Trap goes to the top of its Deck, no prompt",
                    reached && hit && p.LifePoints == lpP - 500 && !o.Graveyard.Contains(trap) &&
                    o.Deck.Count == deck0 + 1 && o.Deck[0] == TrapHole && engine.PendingActivation == null,
                    $"hit={hit} lpP={lpP}->{p.LifePoints} deck {deck0}->{o.Deck.Count} top={o.Deck.FirstOrDefault()}");
            }
            {
                // Nubian Guard: only a Continuous Spell (not a Normal Spell, not a Continuous Trap).
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var nubian = PlaceMonster(engine, p, NubianGuard, 0, BattlePosition.Attack, true);
                var messenger = engine.CreateCardInstance(MessengerOfPeace);
                p.Graveyard.Add(engine.CreateCardInstance(PotOfGreed));
                p.Graveyard.Add(engine.CreateCardInstance(GravityBind));
                p.Graveyard.Add(messenger);
                var deck0 = p.Deck.Count;
                var hit = AttackAndDrain(engine, p, nubian, null);
                var pend = engine.PendingActivation;
                var offered = pend != null && pend.LegalTargets.Count == 1 && ReferenceEquals(pend.LegalTargets[0], messenger);
                if (offered) engine.TrySelectEffectTarget(messenger);
                DrainCombat(engine);
                Check("Nubian Guard: only the Continuous Spell is offered; Yes puts it on top of the Deck",
                    reached && hit && offered && !p.Graveyard.Contains(messenger) && p.Graveyard.Count == 2 &&
                    p.Deck.Count == deck0 + 1 && p.Deck[0] == MessengerOfPeace && engine.PendingActivation == null,
                    $"hit={hit} legal=[{string.Join(",", pend?.LegalTargets.Select(t => t.Name) ?? Array.Empty<string>())}] " +
                    $"deck {deck0}->{p.Deck.Count} top={p.Deck.FirstOrDefault()}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var nubian = PlaceMonster(engine, p, NubianGuard, 0, BattlePosition.Attack, true);
                p.Graveyard.Add(engine.CreateCardInstance(GravityBind));
                p.Graveyard.Add(engine.CreateCardInstance(PotOfGreed));
                var deck0 = p.Deck.Count;
                var hit = AttackAndDrain(engine, p, nubian, null);
                Check("Nubian Guard: a Continuous Trap and a Normal Spell in the GY → no prompt, nothing returned",
                    reached && hit && engine.PendingActivation == null && p.Deck.Count == deck0 && p.Graveyard.Count == 2,
                    $"hit={hit} pending={engine.PendingActivation != null} deck {deck0}->{p.Deck.Count}");
            }
        }

        // ───────────────────────────── Robbin' Zombie ─────────────────────────────

        static void RobbinZombieDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var placed = Activate(engine, p, RobbinZombie, 0, out _);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var deckO = o.Deck.Count;
                var gyO = o.Graveyard.Count;
                var deckP = p.Deck.Count;
                var hit = AttackAndDrain(engine, p, celtic, null);
                Check("Robbin' Zombie: activated; your monster's battle damage → the top card of their Deck goes to the GY",
                    reached && placed && hit && o.Deck.Count == deckO - 1 && o.Graveyard.Count == gyO + 1 &&
                    p.Deck.Count == deckP,
                    $"placed={placed} hit={hit} deckO={deckO}->{o.Deck.Count} gyO={gyO}->{o.Graveyard.Count}");
            }
            {
                // The opponent's monster's battle damage to you: nothing.
                var engine = Main1Of(db, pDeck, aDeck, false, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, RobbinZombie, 0);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var deckO = o.Deck.Count;
                var deckP = p.Deck.Count;
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, o, celtic, null);
                Check("Robbin' Zombie: the opponent's monster damaging you mills nobody",
                    reached && hit && p.LifePoints == lpP - 1400 && o.Deck.Count == deckO && p.Deck.Count == deckP,
                    $"hit={hit} lpP={lpP}->{p.LifePoints} deckO={deckO}->{o.Deck.Count} deckP={deckP}->{p.Deck.Count}");
            }
        }

        // ───────────────────────────── Begone, Knave! / Des Counterblow ─────────────────────────────

        static void BegoneAndCounterblowDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var placed = Activate(engine, p, BegoneKnave, 0, out _);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, null);
                Check("Begone, Knave!: activated; your own monster's direct attack damage → it returns to your hand",
                    reached && placed && hit && o.LifePoints == lpO - 1400 && !OnField(p, celtic) && p.Hand.Contains(celtic),
                    $"placed={placed} hit={hit} lpO={lpO}->{o.LifePoints} inHand={p.Hand.Contains(celtic)}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, BegoneKnave, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, ojama);
                Check("Begone, Knave! (opponent's): the attacker that destroyed and damaged returns to its owner's hand",
                    reached && hit && o.LifePoints == lpO - 1400 && p.Hand.Contains(celtic) && !OnField(p, celtic) &&
                    o.Graveyard.Contains(ojama),
                    $"hit={hit} lpO={lpO}->{o.LifePoints} inHand={p.Hand.Contains(celtic)} ojamaGy={o.Graveyard.Contains(ojama)}");
            }
            {
                // The defender inflicts the damage (attacker weaker): the defender goes back.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, BegoneKnave, 0);
                var ojama = PlaceMonster(engine, p, OjamaGreen, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, p, ojama, celtic);
                Check("Begone, Knave!: the attack target that inflicted the damage returns to its owner's hand",
                    reached && hit && p.LifePoints == lpP - 1400 && o.Hand.Contains(celtic) && !OnField(o, celtic) &&
                    p.Graveyard.Contains(ojama),
                    $"hit={hit} lpP={lpP}->{p.LifePoints} celticInHand={o.Hand.Contains(celtic)} ojamaGy={p.Graveyard.Contains(ojama)}");
            }
            {
                // No battle damage: nothing returns.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, BegoneKnave, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Defense, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, ojama);
                Check("Begone, Knave!: destroying a Defense Position monster without damage → the attacker stays",
                    reached && hit && o.LifePoints == lpO && OnField(p, celtic) && o.Graveyard.Contains(ojama),
                    $"hit={hit} lpO={lpO}->{o.LifePoints} onField={OnField(p, celtic)}");
            }
            {
                // Negated (Royal Decree-style): nothing returns.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var begone = PlaceFaceUpSpellTrap(engine, o, BegoneKnave, 0);
                begone.IsNegated = true;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var hit = AttackAndDrain(engine, p, celtic, null);
                Check("Begone, Knave!: negated → the attacker stays on the field",
                    reached && hit && OnField(p, celtic), $"hit={hit} onField={OnField(p, celtic)}");
            }
            {
                // Either player's monster: the AI's monster returns to the AI's hand.
                var engine = Main1Of(db, pDeck, aDeck, false, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, BegoneKnave, 0);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var hit = AttackAndDrain(engine, o, celtic, null);
                Check("Begone, Knave!: the opponent's direct attack damage → their monster returns to their hand",
                    reached && hit && p.LifePoints == lpP - 1400 && o.Hand.Contains(celtic) && !OnField(o, celtic),
                    $"hit={hit} lpP={lpP}->{p.LifePoints} inHand={o.Hand.Contains(celtic)}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, DesCounterblow, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, null);
                Check("Des Counterblow: a direct attack's battle damage → the attacker is destroyed (damage still dealt)",
                    reached && hit && o.LifePoints == lpO - 1400 && !OnField(p, celtic) && p.Graveyard.Contains(celtic),
                    $"hit={hit} lpO={lpO}->{o.LifePoints} gy={p.Graveyard.Contains(celtic)}");
            }
            {
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, DesCounterblow, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, celtic, ojama);
                Check("Des Counterblow: battle damage from attacking a monster → the attacker stays",
                    reached && hit && o.LifePoints == lpO - 1400 && OnField(p, celtic),
                    $"hit={hit} lpO={lpO}->{o.LifePoints} onField={OnField(p, celtic)}");
            }
            {
                // Both (SEGOC): the turn player's Des Counterblow is Chain Link 1, the opponent's Begone, Knave! Chain Link 2
                // and resolves first — the attacker returns to the hand, so Des Counterblow finds it gone.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, DesCounterblow, 0);
                PlaceFaceUpSpellTrap(engine, o, BegoneKnave, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var hit = AttackAndDrain(engine, p, celtic, null);
                Check("Des Counterblow + Begone, Knave!: the opponent's Begone resolves first — Des Counterblow finds the monster gone",
                    reached && hit && p.Hand.Contains(celtic) && !p.Graveyard.Contains(celtic),
                    $"hit={hit} gy={p.Graveyard.Contains(celtic)} hand={p.Hand.Contains(celtic)}");
            }
            {
                // Begone, Knave! resolves after battle destruction, on the chain with the destroyer's "destroys by battle"
                // trigger (aux.bdocon): Timeater returns to the hand AND its opponent still skips their next Main Phase 1.
                var engine = Main1Of(db, pDeck, aDeck, true, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, BegoneKnave, 0);
                var timeater = PlaceMonster(engine, p, Timeater, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var hit = AttackAndDrain(engine, p, timeater, celtic);
                var skip = BattleOutcomeTriggersRuntime.MainPhase1SkipPending(engine, o);
                Check("Begone, Knave! + Timeater: Timeater returns to the hand and its destroys-by-battle trigger still applies",
                    reached && hit && o.LifePoints < lpO && o.Graveyard.Contains(celtic) && p.Hand.Contains(timeater) &&
                    !OnField(p, timeater) && skip,
                    $"hit={hit} lpO={lpO}->{o.LifePoints} celticGy={o.Graveyard.Contains(celtic)} " +
                    $"inHand={p.Hand.Contains(timeater)} skipPending={skip}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            CompiledCardProgram Syn(int id, string type, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef { id = id, name = "Near miss " + id, type = type, desc = desc });

            var begoneRider = Syn(90517701, "Trap Card",
                "If a monster inflicts battle damage to a player: Return that monster to the hand, then draw 1 card.");
            Check("Near miss: Begone, Knave! with a draw rider does not compile to the any-monster return",
                begoneRider != null && !begoneRider.FullyCompiled &&
                !Has(begoneRider, c => c.AnyMonsterBattleDamageToAnyPlayer), Describe(begoneRider));

            var counterAny = Syn(90517702, "Trap Card",
                "Destroy any monster that inflicts Battle Damage to a player's Life Points.");
            Check("Near miss: Des Counterblow without \"by attacking directly\" is not the direct-attack destroy",
                counterAny != null && !Has(counterAny, c => c.AnyMonsterBattleDamageToAnyPlayer), Describe(counterAny));

            var plainSpell = Syn(90517703, "Effect Monster",
                "If this card inflicts Battle Damage to your opponent's Life Points, you can return 1 Spell Card from " +
                "your Graveyard to the top of your Deck.");
            Check("Near miss: \"1 Spell Card\" is not the Trap / Continuous Spell atom",
                plainSpell != null && !Has(plainSpell, c => c.Action == EffectActionKind.PlaceTargetOnTopOfDeck &&
                                                          c.Timing == EffectTiming.ThisCardInflictsBattleDamage),
                Describe(plainSpell));

            var anyDouble = Syn(90517704, "Effect Monster",
                "The Battle Damage this card inflicts to your opponent's Life Points is doubled.");
            Check("Near miss: doubling without \"by attacking … directly\" is not Piranha Army's doubler",
                anyDouble != null && !Has(anyDouble, c => c.Action == EffectActionKind.DoubleBattleDamageToOpponent),
                Describe(anyDouble));

            var sasukeRider = Syn(90517705, "Effect Monster",
                "When this card inflicts Battle Damage to your opponent's Life Points, your opponent draws cards until " +
                "his/her hand has 7 cards, then discards 1 card.");
            Check("Near miss: Sasuke Samurai #3 with a discard rider does not compile",
                sasukeRider != null && !sasukeRider.FullyCompiled, Describe(sasukeRider));

            var zombieYours = Syn(90517706, "Trap Card",
                "If your monster inflicts battle damage to your opponent: Send the top card of your Deck to the GY.");
            Check("Near miss: Robbin' Zombie milling your own Deck does not compile to the opponent's mill",
                zombieYours != null && !Has(zombieYours, c => c.Action == EffectActionKind.SendFromTopOfDeckToGy &&
                                                            c.Side == EffectSide.Opponent && c.AnyControllerMonsterBattleDamage),
                Describe(zombieYours));
        }
    }
}
