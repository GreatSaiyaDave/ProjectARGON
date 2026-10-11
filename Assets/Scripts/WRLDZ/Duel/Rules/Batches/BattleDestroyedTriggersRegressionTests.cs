using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// BattleDestroyedTriggers batch: Kelbek, Keldo, Agido, Kaiser Glider, Different Dimension Dragon, Shinato, King of a
    /// Higher Plane, Des Kangaroo. Compile shapes through the real compiler, battles and Spell activations through the engine
    /// entry points (TryAttack, TryActivateSpellTrap, TrySelectEffectTarget), and look-alike sentences that must stay
    /// uncompiled. Turn 3 is the Player's, turn 4 the AI's. Runtime: <see cref="BattleDestroyedTriggersRuntime"/>.
    /// </summary>
    public static class BattleDestroyedTriggersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Kelbek = 54878498;              // Fairy Lv4 1500/1800
        const int Keldo = 80441106;               // Fairy Lv4 1200/1600
        const int Agido = 16135253;               // Fairy Lv4 1500/1300
        const int KaiserGlider = 52824910;        // Dragon Lv6 2400/2200
        const int DdDragon = 50939127;            // Dragon Lv5 1200/1500
        const int Shinato = 86327225;             // Ritual Fairy Lv8 3300/3000
        const int DesKangaroo = 78613627;         // Beast Lv4 1500/1700

        const int AxeRaider = 48305365;           // Normal Warrior Lv4 1700/1150
        const int CelticGuardian = 91152256;      // Normal 1400/1200
        const int GeminiElf = 69140098;           // Normal 1900/900
        const int GiantSoldier = 13039848;        // Normal 1300/2000
        const int MysticalElf = 15025844;         // Normal 800/2000
        const int SummonedSkull = 70781052;       // Normal 2500/1200
        const int Joan = 68007326;                // Guardian Angel Joan, Fairy Lv7 2800/2000
        const int Fissure = 66788016;             // Normal Spell: destroy the opponent's lowest-ATK face-up monster
        const int Raigeki = 12580477;             // Normal Spell: destroy all the opponent's monsters
        const int TributeToTheDoomed = 79759861;  // Normal Spell: discard 1 card, then target 1 monster; destroy it
        const int MirrorForce = 44095762;         // Normal Trap: destroy all the attacker's Attack Position monsters

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("BattleDestroyedTriggers: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            KelbekDuels(db, pDeck, aDeck, Check);
            KeldoDuels(db, pDeck, aDeck, Check);
            AgidoDuels(db, pDeck, aDeck, Check);
            KaiserGliderDuels(db, pDeck, aDeck, Check);
            DdDragonDuels(db, pDeck, aDeck, Check);
            ShinatoDuels(db, pDeck, aDeck, Check);
            DesKangarooDuels(db, pDeck, aDeck, Check);
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
            engine.Player.Graveyard.Clear();
            engine.Opponent.Graveyard.Clear();
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

        /// <summary>The AI's Battle Phase (turn 4): <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static CardInstance InGyOf(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);
        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} n={c.TargetCount} race={c.RaceFilter} " +
                    $"atkTgt={c.RequiresThisIsAttackTarget} notDes={c.RequiresBattledMonsterNotDestroyed} " +
                    $"bat={c.RequiresThisDestroyedByBattle} des={c.RequiresDestroyed} def={c.RequiresDestroyedWasDefensePosition} " +
                    $"psct={c.IsPsctTarget} chain={c.MakesChainLink}")) + "]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Is(int id, CardEffectStatusKind k) => CardEffectStatus.Classify(db.Get(id)) == k;

            bool One(CompiledCardProgram p, EffectTiming t, EffectActionKind a) =>
                p != null && p.FullyCompiled && p.ClauseList.Count == 1 && p.ClauseList[0].Timing == t &&
                p.ClauseList[0].Action == a;

            var kelbek = P(Kelbek);
            Check("Kelbek: after damage calculation, the attacker that is not destroyed returns to the hand",
                One(kelbek, EffectTiming.AfterDamageCalculation, EffectActionKind.ReturnToHand) &&
                kelbek.ClauseList[0].Zone == EffectZoneFilter.AttackingMonster &&
                kelbek.ClauseList[0].RequiresThisIsAttackTarget && kelbek.ClauseList[0].RequiresBattledMonsterNotDestroyed &&
                !kelbek.ClauseList[0].IsPsctTarget && Is(Kelbek, CardEffectStatusKind.Implemented), Describe(kelbek));

            var keldo = P(Keldo);
            Check("Keldo: battle-destroyed in the GY → 2 targets in the opponent's GY shuffled into the Deck",
                One(keldo, EffectTiming.SentFromFieldToGy, EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck) &&
                keldo.ClauseList[0].TargetCount == 2 && keldo.ClauseList[0].RequiresThisDestroyedByBattle &&
                !keldo.ClauseList[0].IsOptional && Is(Keldo, CardEffectStatusKind.Implemented), Describe(keldo));

            var agido = P(Agido);
            Check("Agido: battle-destroyed in the GY → roll a die, then may revive a Fairy of that Level",
                One(agido, EffectTiming.SentFromFieldToGy, EffectActionKind.RollDieSpecialSummonFromGyWithLevelOfRoll) &&
                agido.ClauseList[0].RaceFilter == "Fairy" && agido.ClauseList[0].RequiresThisDestroyedByBattle &&
                Is(Agido, CardEffectStatusKind.Implemented), Describe(agido));

            var kaiser = P(KaiserGlider);
            Check("Kaiser Glider: equal-ATK battle protection + destroyed → target 1 monster on the field, bounce it",
                kaiser != null && kaiser.FullyCompiled && kaiser.ClauseList.Count == 2 &&
                kaiser.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                              c.Action == EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster) &&
                kaiser.ClauseList.Exists(c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                              c.Action == EffectActionKind.ReturnToHand &&
                                              c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresDestroyed &&
                                              !c.RequiresThisDestroyedByBattle && c.RequiresTargetChoice &&
                                              c.IsPsctTarget && !c.IsOptional) &&
                Is(KaiserGlider, CardEffectStatusKind.Implemented), Describe(kaiser));

            var ddd = P(DdDragon);
            Check("Different Dimension Dragon: untargeted Spell/Trap destruction + 1900-or-less battle protections",
                ddd != null && ddd.FullyCompiled && ddd.ClauseList.Count == 2 &&
                ddd.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                           c.Action == EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis) &&
                ddd.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                           c.Action == EffectActionKind.CannotBeDestroyedByBattleWithMonsterAtkAtMost &&
                                           c.Amount == 1900) &&
                Is(DdDragon, CardEffectStatusKind.Implemented), Describe(ddd));

            var shinato = P(Shinato);
            Check("Shinato: destroys a Defense Position monster by battle → burn its original ATK",
                One(shinato, EffectTiming.ThisCardDestroysByBattle, EffectActionKind.InflictDamageToOpponent) &&
                shinato.ClauseList[0].AmountFromTargetOriginalAtk &&
                shinato.ClauseList[0].RequiresDestroyedWasDefensePosition &&
                Is(Shinato, CardEffectStatusKind.Implemented), Describe(shinato));

            var kangaroo = P(DesKangaroo);
            Check("Des Kangaroo: end-of-Damage-Step destruction of a lower-ATK attacker (Thousand Needles' clause)",
                One(kangaroo, EffectTiming.EndOfDamageStep, EffectActionKind.DestroyAttackerAtEndOfDamageStep) &&
                kangaroo.ClauseList[0].Zone == EffectZoneFilter.AttackingMonster &&
                Is(DesKangaroo, CardEffectStatusKind.Implemented), Describe(kangaroo));

            // Near misses: each sentence with a rider or a changed word must not compile to these templates.
            var near = new (string Desc, Func<EffectClause, bool> Leak)[]
            {
                ("A monster that attacks this card returns to the Deck. Damage calculation is applied normally.",
                    c => c.RequiresBattledMonsterNotDestroyed),
                ("When this card is destroyed and sent to the Graveyard as a result of battle, select 2 cards from your " +
                 "opponent's Graveyard, add them to your opponent's Deck and the Deck is then shuffled, then draw 1 card.",
                    c => c.Action == EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck),
                ("When this card is destroyed and sent to the Graveyard as a result of battle, select 2 cards from your " +
                 "Graveyard, add them to your Deck and the Deck is then shuffled.",
                    c => c.Action == EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck),
                ("When this card is destroyed and sent to the Graveyard as a result of battle, roll a six-sided die. You " +
                 "can Special Summon 1 Fairy-Type monster from your Graveyard whose Level is equal to the number rolled.",
                    c => c.Action == EffectActionKind.RollDieSpecialSummonFromGyWithLevelOfRoll),
                ("Cannot be destroyed by battle with a monster that has the same DEF.",
                    c => c.Action == EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster),
                ("This card cannot be destroyed by Spell effects that do not target it.",
                    c => c.Action == EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis),
                ("This card cannot be destroyed by battle with a monster that has 1900 or more ATK.",
                    c => c.Action == EffectActionKind.CannotBeDestroyedByBattleWithMonsterAtkAtMost),
                ("When this card destroys a Defense Position monster and sends it to the Graveyard as a result of battle, " +
                 "inflict damage to your opponent's Life Points equal to the original DEF of the destroyed monster.",
                    c => c.RequiresDestroyedWasDefensePosition),
                ("If the ATK of a monster that attacks this monster in Defense Position is lower than the DEF of this card, " +
                 "destroy the attacking monster and inflict 500 damage to your opponent. (Damage calculation is applied " +
                 "normally.)",
                    c => c.Action == EffectActionKind.DestroyAttackerAtEndOfDamageStep),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90717600 + i, name = "Synthetic BattleDestroyedTriggers near miss " + i, type = "Effect Monster",
                    race = "Fairy", desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => n.Leak(c)));
            }).Select(n => n.Desc.Substring(0, Math.Min(40, n.Desc.Length))).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(" | ", leaked));
        }

        // ───────────────────────────── Kelbek ─────────────────────────────

        static void KelbekDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kelbek = PlaceMonster(engine, p, Kelbek, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var ok = OpponentAttacks(engine, axe, kelbek);
                Check("Kelbek: destroyed by a 1700 ATK attacker, the attacker still returns to the hand",
                    reached && ok && InGy(p, kelbek) && o.Hand.Contains(axe) && !OnField(o, axe) &&
                    p.LifePoints == pLp - 200,
                    $"ok={ok} kelbekGy={InGy(p, kelbek)} axeHand={o.Hand.Contains(axe)} pLp={pLp}->{p.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kelbek = PlaceMonster(engine, p, Kelbek, 0, BattlePosition.Defense, false);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = OpponentAttacks(engine, axe, kelbek);
                Check("Kelbek (Set): flipped by the attack, the attacker returns to the hand; damage is calculated normally",
                    reached && ok && OnField(p, kelbek) && kelbek.FaceUp && o.Hand.Contains(axe) &&
                    o.LifePoints == oLp - 100,
                    $"ok={ok} kelbek={OnField(p, kelbek)} faceUp={kelbek.FaceUp} axeHand={o.Hand.Contains(axe)} " +
                    $"oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kelbek = PlaceMonster(engine, p, Kelbek, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, celtic, kelbek);
                Check("Kelbek: an attacker destroyed by the battle goes to the GY, not the hand",
                    reached && ok && InGy(o, celtic) && !o.Hand.Contains(celtic) && OnField(p, kelbek),
                    $"ok={ok} celticGy={InGy(o, celtic)} celticHand={o.Hand.Contains(celtic)} kelbek={OnField(p, kelbek)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kelbek = PlaceMonster(engine, p, Kelbek, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var ok = AttackAndDrain(engine, p, kelbek, elf);
                Check("Kelbek: when it attacks, nothing returns to the hand",
                    reached && ok && OnField(p, kelbek) && OnField(o, elf) && !p.Hand.Contains(kelbek) &&
                    !o.Hand.Contains(elf),
                    $"ok={ok} kelbek={OnField(p, kelbek)} elf={OnField(o, elf)}");
            }
        }

        // ───────────────────────────── Keldo ─────────────────────────────

        static void KeldoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's Keldo: it targets 2 of the Player's 3 GY cards.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var keldo = PlaceMonster(engine, o, Keldo, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                InGyOf(engine, p, CelticGuardian);
                InGyOf(engine, p, GeminiElf);
                InGyOf(engine, p, SummonedSkull);
                var deck = p.Deck.Count;
                var ok = AttackAndDrain(engine, p, axe, keldo);
                Check("Keldo (AI): destroyed by battle, 2 cards of the Player's GY are shuffled into the Deck",
                    reached && ok && InGy(o, keldo) && p.Graveyard.Count == 1 && p.Deck.Count == deck + 2 &&
                    !p.Graveyard.Exists(c => c.CardId == SummonedSkull) && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} keldoGy={InGy(o, keldo)} pGy={p.Graveyard.Count} deck={deck}->{p.Deck.Count}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var keldo = PlaceMonster(engine, o, Keldo, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                var lone = InGyOf(engine, p, CelticGuardian);
                var deck = p.Deck.Count;
                var ok = AttackAndDrain(engine, p, axe, keldo);
                Check("Keldo: only 1 card in the opponent's GY — no target, nothing happens",
                    reached && ok && InGy(o, keldo) && InGy(p, lone) && p.Deck.Count == deck &&
                    !engine.IsAwaitingEffectTarget,
                    $"ok={ok} keldoGy={InGy(o, keldo)} lone={InGy(p, lone)} deck={deck}->{p.Deck.Count}");
            }

            {
                // The Player's Keldo: two picks through the target flow.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var keldo = PlaceMonster(engine, p, Keldo, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var a = InGyOf(engine, o, CelticGuardian);
                var b = InGyOf(engine, o, GeminiElf);
                var keep = InGyOf(engine, o, MysticalElf);
                var reachedBattle = ReachOpponentBattle(engine);
                o.Hand.Clear();
                var deck = o.Deck.Count; // after the AI's draw
                var ok = reachedBattle && engine.TryAttack(o, axe, keldo);
                DrainCombat(engine);
                var asked1 = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(a);
                var picked1 = asked1 && engine.TrySelectEffectTarget(a);
                var asked2 = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(b) &&
                             !engine.PendingActivation.LegalTargets.Contains(a);
                var picked2 = asked2 && engine.TrySelectEffectTarget(b);
                DrainCombat(engine);
                Check("Keldo (Player): two picks in the AI's GY, both shuffled into the AI's Deck",
                    reached && ok && InGy(p, keldo) && picked1 && picked2 && !InGy(o, a) && !InGy(o, b) &&
                    InGy(o, keep) && o.Deck.Count == deck + 2 && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} asked={asked1}/{asked2} picked={picked1}/{picked2} oGy={o.Graveyard.Count} " +
                    $"deck={deck}->{o.Deck.Count}");
            }

            {
                // Destroyed by a card effect: no trigger.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var keldo = PlaceMonster(engine, o, Keldo, 0, BattlePosition.Attack, true);
                InGyOf(engine, p, CelticGuardian);
                InGyOf(engine, p, GeminiElf);
                var fissure = PutInHand(engine, p, Fissure);
                engine.NotifyPublic();
                var deck = p.Deck.Count;
                var act = engine.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(keldo);
                PassAll(engine);
                Check("Keldo: destroyed by Fissure (not by battle) — no shuffle",
                    reached && act && InGy(o, keldo) && p.Graveyard.Count == 3 && p.Deck.Count == deck,
                    $"act={act} keldoGy={InGy(o, keldo)} pGy={p.Graveyard.Count} deck={deck}->{p.Deck.Count}");
            }
        }

        // ───────────────────────────── Agido ─────────────────────────────

        static void AgidoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (die, expectId, label) in new[]
                     {
                         (4, Kelbek, "a 4 revives the Level 4 Fairy"),
                         (6, Joan, "a 6 revives a Level 6 or higher Fairy"),
                         (3, 0, "a 3 finds no Level 3 Fairy — nothing is summoned"),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var agido = PlaceMonster(engine, o, Agido, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                var kelbek = InGyOf(engine, o, Kelbek);
                var joan = InGyOf(engine, o, Joan);
                var axe2 = InGyOf(engine, o, AxeRaider); // a Level 4 Warrior: never revived
                engine.Rng.QueueDie(die);
                var ok = AttackAndDrain(engine, p, axe, agido);
                var revived = expectId == Kelbek ? kelbek : expectId == Joan ? joan : null;
                var fieldCount = o.MonstersOnField().Count();
                Check("Agido (AI): " + label,
                    reached && ok && InGy(o, agido) && InGy(o, axe2) &&
                    (revived == null
                        ? fieldCount == 0 && InGy(o, kelbek) && InGy(o, joan)
                        : OnField(o, revived) && revived.FaceUp && fieldCount == 1) &&
                    !engine.IsAwaitingEffectTarget,
                    $"ok={ok} agidoGy={InGy(o, agido)} field={fieldCount} kelbek={OnField(o, kelbek)} " +
                    $"joan={OnField(o, joan)} axe2Gy={InGy(o, axe2)}");
            }

            foreach (var accept in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var agido = PlaceMonster(engine, p, Agido, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var kelbek = InGyOf(engine, p, Kelbek);
                engine.Rng.QueueDie(4);
                var ok = OpponentAttacks(engine, axe, agido);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(kelbek) &&
                            SpellTrapEffects.PendingTargetIsSpecialSummon(engine.PendingActivation);
                var answered = asked && (accept ? engine.TrySelectEffectTarget(kelbek) : engine.CancelEffectTargeting());
                DrainCombat(engine);
                Check(accept
                        ? "Agido (Player): a 4, the Player picks the Level 4 Fairy (a placement pick) — it is Special Summoned"
                        : "Agido (Player): a 4, the Player declines (Cancel) — the Fairy stays in the GY",
                    reached && ok && InGy(p, agido) && asked && answered &&
                    (accept ? OnField(p, kelbek) && kelbek.FaceUp : InGy(p, kelbek)) && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} asked={asked} answered={answered} kelbek={OnField(p, kelbek)} gy={InGy(p, kelbek)}");
            }
        }

        // ───────────────────────────── Kaiser Glider ─────────────────────────────

        static void KaiserGliderDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kaiser = PlaceMonster(engine, p, KaiserGlider, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                skull.LingeringAtkModifier = -100; // 2400, Kaiser Glider's ATK
                var ok = OpponentAttacks(engine, skull, kaiser);
                Check("Kaiser Glider: an equal-ATK battle destroys only the attacker",
                    reached && ok && OnField(p, kaiser) && InGy(o, skull),
                    $"ok={ok} kaiser={OnField(p, kaiser)} skullGy={InGy(o, skull)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kaiser = PlaceMonster(engine, o, KaiserGlider, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 1, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, skull, kaiser);
                Check("Kaiser Glider (AI): destroyed by battle, it returns the Player's monster to the hand",
                    reached && ok && InGy(o, kaiser) && p.Hand.Contains(skull) && !OnField(p, skull) &&
                    OnField(o, celtic),
                    $"ok={ok} kaiserGy={InGy(o, kaiser)} skullHand={p.Hand.Contains(skull)} celtic={OnField(o, celtic)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kaiser = PlaceMonster(engine, o, KaiserGlider, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                var fissure = PutInHand(engine, p, Fissure);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(kaiser);
                PassAll(engine);
                Check("Kaiser Glider (AI): destroyed by Fissure (a card effect), it returns the Player's monster",
                    reached && act && InGy(o, kaiser) && p.Hand.Contains(axe) && !OnField(p, axe),
                    $"act={act} kaiserGy={InGy(o, kaiser)} axeHand={p.Hand.Contains(axe)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kaiser = PlaceMonster(engine, p, KaiserGlider, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, skull, kaiser);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(skull);
                var picked = asked && engine.TrySelectEffectTarget(skull);
                DrainCombat(engine);
                Check("Kaiser Glider (Player): destroyed by a 2500 ATK attacker, the Player targets it — back to the hand",
                    reached && ok && InGy(p, kaiser) && picked && o.Hand.Contains(skull) && !OnField(o, skull),
                    $"ok={ok} kaiserGy={InGy(p, kaiser)} asked={asked} picked={picked} skullHand={o.Hand.Contains(skull)}");
            }
        }

        // ───────────────────────────── Different Dimension Dragon ─────────────────────────────

        static void DdDragonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, survives, label) in new[]
                     {
                         (GeminiElf, true, "a 1900 ATK attacker does not destroy it"),
                         (SummonedSkull, false, "a 2500 ATK attacker destroys it"),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ddd = PlaceMonster(engine, p, DdDragon, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, o, id, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var ok = OpponentAttacks(engine, attacker, ddd);
                var dmg = (id == GeminiElf ? 1900 : 2500) - 1200;
                Check("Different Dimension Dragon: " + label,
                    reached && ok && (survives ? OnField(p, ddd) : InGy(p, ddd)) && OnField(o, attacker) &&
                    p.LifePoints == pLp - dmg,
                    $"ok={ok} ddd={OnField(p, ddd)} gy={InGy(p, ddd)} pLp={pLp}->{p.LifePoints}");
            }

            foreach (var (spell, label) in new[]
                     {
                         (Raigeki, "Raigeki (no target) does not destroy it"),
                         (Fissure, "Fissure (no target) does not destroy it"),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ddd = PlaceMonster(engine, o, DdDragon, 0, BattlePosition.Attack, true);
                var card = PutInHand(engine, p, spell);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, card, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(ddd); // Fissure's lowest-ATK pick
                PassAll(engine);
                Check("Different Dimension Dragon: " + label,
                    reached && act && OnField(o, ddd) && InGy(p, card),
                    $"act={act} ddd={OnField(o, ddd)} spellGy={InGy(p, card)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ddd = PlaceMonster(engine, o, DdDragon, 0, BattlePosition.Attack, true);
                var tribute = PutInHand(engine, p, TributeToTheDoomed);
                var fodder = PutInHand(engine, p, CelticGuardian);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, tribute, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingEffectTarget; i++)
                {
                    var legal = engine.PendingActivation.LegalTargets;
                    if (legal.Contains(fodder)) engine.TrySelectEffectTarget(fodder);
                    else if (legal.Contains(ddd)) engine.TrySelectEffectTarget(ddd);
                    else break;
                }

                PassAll(engine);
                Check("Different Dimension Dragon: Tribute to the Doomed targets it — it is destroyed",
                    reached && act && InGy(o, ddd) && InGy(p, fodder),
                    $"act={act} dddGy={InGy(o, ddd)} fodderGy={InGy(p, fodder)}");
            }

            {
                // The AI's Different Dimension Dragon attacks; the Player answers with Mirror Force (no target): the
                // other Attack Position monster is destroyed, Different Dimension Dragon is not.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ddd = PlaceMonster(engine, o, DdDragon, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 1, BattlePosition.Attack, true);
                var mf = PlaceSetTrap(engine, p, MirrorForce, 0);
                var battle = ReachOpponentBattle(engine);
                o.Hand.Clear();
                var ok = battle && engine.TryAttack(o, ddd, null);
                var offered = engine.PendingResponse != null && engine.PendingResponse.Responder == p &&
                              engine.PendingResponse.LegalCards.Contains(mf);
                var chained = offered && engine.TryActivateSpellTrap(p, mf, fromHand: false);
                PassAll(engine);
                DrainCombat(engine);
                Check("Different Dimension Dragon: Mirror Force destroys the other Attack Position monster, not it",
                    reached && ok && chained && InGy(p, mf) && OnField(o, ddd) && InGy(o, celtic),
                    $"ok={ok} offered={offered} chained={chained} mfGy={InGy(p, mf)} ddd={OnField(o, ddd)} " +
                    $"celticGy={InGy(o, celtic)}");
            }

            {
                // Negated (face-down) it has no protection: Raigeki destroys a Set Different Dimension Dragon.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ddd = PlaceMonster(engine, o, DdDragon, 0, BattlePosition.Defense, false);
                var card = PutInHand(engine, p, Raigeki);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, card, fromHand: true);
                PassAll(engine);
                Check("Different Dimension Dragon: face-down, Raigeki destroys it",
                    reached && act && InGy(o, ddd), $"act={act} dddGy={InGy(o, ddd)}");
            }
        }

        // ───────────────────────────── Shinato ─────────────────────────────

        static void ShinatoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, faceUp, pos, boost, burn, label) in new[]
                     {
                         (GiantSoldier, true, BattlePosition.Defense, 0, 1300, "a face-up Defense Position monster: burn 1300"),
                         (MysticalElf, false, BattlePosition.Defense, 0, 800, "a Set monster: burn its 800 original ATK"),
                         (GiantSoldier, true, BattlePosition.Defense, 500, 1300,
                             "a boosted Defense Position monster: burn its original ATK (1300), not 1800"),
                         (AxeRaider, true, BattlePosition.Attack, 0, 0, "an Attack Position monster: battle damage only"),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var shinato = PlaceMonster(engine, p, Shinato, 0, BattlePosition.Attack, true);
                var victim = PlaceMonster(engine, o, id, 0, pos, faceUp);
                victim.LingeringAtkModifier = boost;
                var oLp = o.LifePoints;
                var battle = pos == BattlePosition.Attack ? 3300 - 1700 : 0;
                var ok = AttackAndDrain(engine, p, shinato, victim);
                Check("Shinato: " + label,
                    reached && ok && InGy(o, victim) && OnField(p, shinato) && o.LifePoints == oLp - battle - burn,
                    $"ok={ok} victimGy={InGy(o, victim)} oLp={oLp}->{o.LifePoints} want={oLp - battle - burn}");
            }
        }

        // ───────────────────────────── Des Kangaroo ─────────────────────────────

        static void DesKangarooDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var faceUp in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var roo = PlaceMonster(engine, p, DesKangaroo, 0, BattlePosition.Defense, faceUp);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = OpponentAttacks(engine, celtic, roo);
                Check(faceUp
                        ? "Des Kangaroo (face-up Defense): a 1400 ATK attacker is destroyed; damage is calculated normally"
                        : "Des Kangaroo (Set): flipped by the attack, the 1400 ATK attacker is still destroyed",
                    reached && ok && InGy(o, celtic) && OnField(p, roo) && o.LifePoints == oLp - 300,
                    $"ok={ok} celticGy={InGy(o, celtic)} roo={OnField(p, roo)} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var roo = PlaceMonster(engine, p, DesKangaroo, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, roo);
                Check("Des Kangaroo: an attacker whose ATK equals its DEF (1700) is not destroyed",
                    reached && ok && OnField(o, axe) && OnField(p, roo),
                    $"ok={ok} axe={OnField(o, axe)} roo={OnField(p, roo)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var roo = PlaceMonster(engine, p, DesKangaroo, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                celtic.LingeringAtkModifier = 200; // 1600: beats its 1500 ATK, lower than its 1700 DEF
                var ok = OpponentAttacks(engine, celtic, roo);
                Check("Des Kangaroo: attacked in Attack Position, the attacker is not destroyed",
                    reached && ok && OnField(o, celtic) && InGy(p, roo),
                    $"ok={ok} celtic={OnField(o, celtic)} rooGy={InGy(p, roo)}");
            }
        }
    }
}
