using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DamageStepRules batch: D.D. Crazy Beast, Thousand Needles, Continuous Destruction Punch, Ultimate Obedient Fiend,
    /// Meteorain, Kishido Spirit, Cat's Ear Tribe, Amazoness Swords Woman. Compile shapes through the real compiler, battles
    /// through the engine entry points (TryActivateSpellTrap, TryAttack, response windows), and look-alike sentences that
    /// must stay uncompiled. Turn 3 is the Player's, turn 4 the AI's. Runtime: <see cref="DamageStepRulesRuntime"/>.
    /// </summary>
    public static class DamageStepRulesRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int DdCrazyBeast = 48148828;        // Beast Lv3 1400/1400
        const int ThousandNeedles = 33977496;     // Beast Lv4 1000/1800
        const int ContinuousPunch = 68057622;     // Continuous Spell
        const int ObedientFiend = 32240937;       // Fiend Lv10 3500/3000
        const int Meteorain = 64274292;           // Normal Trap
        const int KishidoSpirit = 60519422;       // Continuous Spell
        const int CatsEarTribe = 95841282;        // Beast-Warrior Lv1 200/100
        const int SwordsWoman = 94004268;         // Warrior Lv4 1500/1600

        const int AxeRaider = 48305365;           // Normal 1700/1150
        const int BattleOx = 5053103;             // Normal 1700/1000
        const int CelticGuardian = 91152256;      // Normal 1400/1200
        const int GeminiElf = 69140098;           // Normal 1900/900
        const int GiantSoldier = 13039848;        // Normal 1300/2000
        const int MysticalElf = 15025844;         // Normal 800/2000
        const int SummonedSkull = 70781052;       // Normal 2500/1200
        const int Witch = 78010363;               // Effect 1100/1200: sent to the GY → search
        const int Kuriboh = 40640057;
        const int GuardianBaou = 73544866;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DamageStepRules: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            DdCrazyBeastDuels(db, pDeck, aDeck, Check);
            ThousandNeedlesDuels(db, pDeck, aDeck, Check);
            ContinuousPunchDuels(db, pDeck, aDeck, Check);
            ObedientFiendDuels(db, pDeck, aDeck, Check);
            MeteorainDuels(db, pDeck, aDeck, Check);
            KishidoDuels(db, pDeck, aDeck, Check);
            CatsEarDuels(db, pDeck, aDeck, Check);
            SwordsWomanDuels(db, pDeck, aDeck, Check);
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

        static bool Activate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand)
        {
            var ok = engine.TryActivateSpellTrap(who, card, fromHand);
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            return ok;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);
        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} kind={c.TargetCardKind} " +
                    $"self={c.AffectsThisCardOnly} cond={c.ActivationCondition}")) + "]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Is(int id, CardEffectStatusKind k) => CardEffectStatus.Classify(db.Get(id)) == k;

            bool One(CompiledCardProgram p, EffectTiming t, EffectActionKind a) =>
                p != null && p.FullyCompiled && p.ClauseList.Count == 1 && p.ClauseList[0].Timing == t &&
                p.ClauseList[0].Action == a;

            var ddcb = P(DdCrazyBeast);
            Check("D.D. Crazy Beast: banish what it destroys by battle (Lesser Fiend's clause)",
                One(ddcb, EffectTiming.ContinuousWhileFaceUp, EffectActionKind.BanishMonstersDestroyedByBattleWithThis) &&
                Is(DdCrazyBeast, CardEffectStatusKind.Implemented), Describe(ddcb));

            var needles = P(ThousandNeedles);
            Check("Thousand Needles: end-of-Damage-Step trigger destroying the attacker",
                One(needles, EffectTiming.EndOfDamageStep, EffectActionKind.DestroyAttackerAtEndOfDamageStep) &&
                needles.ClauseList[0].Zone == EffectZoneFilter.AttackingMonster && needles.ClauseList[0].MakesChainLink &&
                string.IsNullOrEmpty(needles.ClauseList[0].ActivationCondition) &&
                Is(ThousandNeedles, CardEffectStatusKind.Implemented), Describe(needles));

            var punch = P(ContinuousPunch);
            Check("Continuous Destruction Punch: continuous (no Chain Link) attacker destruction",
                One(punch, EffectTiming.ContinuousWhileFaceUp, EffectActionKind.DestroyAttackerAtEndOfDamageStep) &&
                punch.ClauseList[0].StaysOnField && !punch.ClauseList[0].MakesChainLink &&
                Is(ContinuousPunch, CardEffectStatusKind.Implemented), Describe(punch));

            var fiend = P(ObedientFiend);
            Check("Ultimate Obedient Fiend: attack condition + Effect Monsters it destroys are negated",
                fiend != null && fiend.FullyCompiled && fiend.ClauseList.Count == 2 &&
                fiend.ClauseList.Exists(c => c.Action == EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand) &&
                fiend.ClauseList.Exists(c => c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                                             c.AffectsThisCardOnly && c.TargetCardKind == "Effect" &&
                                             string.IsNullOrEmpty(c.CountRaceFilter)) &&
                Is(ObedientFiend, CardEffectStatusKind.Implemented), Describe(fiend));

            var meteor = P(Meteorain);
            Check("Meteorain: Activate (your turn) → your monsters pierce this turn",
                One(meteor, EffectTiming.Activate, EffectActionKind.YourMonstersGainPiercingThisTurn) &&
                meteor.ClauseList[0].ActivationCondition == DamageStepRulesRuntime.YourTurn &&
                Is(Meteorain, CardEffectStatusKind.Implemented), Describe(meteor));

            var kishido = P(KishidoSpirit);
            Check("Kishido Spirit: equal-ATK battle protection for your monsters",
                One(kishido, EffectTiming.ContinuousWhileFaceUp, EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster) &&
                kishido.ClauseList[0].Side == EffectSide.Controller &&
                Is(KishidoSpirit, CardEffectStatusKind.Implemented), Describe(kishido));

            var cat = P(CatsEarTribe);
            Check("Cat's Ear Tribe: the opponent's battler's original ATK becomes 200 in the Damage Step",
                One(cat, EffectTiming.ContinuousWhileFaceUp,
                    EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep) &&
                cat.ClauseList[0].Amount == 200 && Is(CatsEarTribe, CardEffectStatusKind.Implemented), Describe(cat));

            var asw = P(SwordsWoman);
            Check("Amazoness Swords Woman: reflects the battle damage of its battles",
                One(asw, EffectTiming.ContinuousWhileFaceUp,
                    EffectActionKind.ReflectControllerBattleDamageFromBattlesInvolvingThis) &&
                Is(SwordsWoman, CardEffectStatusKind.Implemented), Describe(asw));

            // Near misses: each sentence with a rider or a changed word must not compile to these templates.
            var near = new (string Desc, EffectActionKind Action)[]
            {
                ("A monster that this card destroys as a result of battle is removed from play, and you gain 500 Life Points.",
                    EffectActionKind.BanishMonstersDestroyedByBattleWithThis),
                ("When this Defense Position card is attacked and if the DEF of this card is higher than the ATK of the " +
                 "attacking monster, destroy the attacking monster and inflict 500 damage at the end of the Damage Step.",
                    EffectActionKind.DestroyAttackerAtEndOfDamageStep),
                ("This card can only attack if this card is the only card on your side of the field.",
                    EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand),
                ("The effects of Effect Monsters that this card destroys are negated until the End Phase.",
                    EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle),
                ("During this turn, when your Dragon-Type monsters attack with an ATK that is higher than the DEF of your " +
                 "opponent's Defense Position monster, inflict the difference as Battle Damage to your opponent's Life Points.",
                    EffectActionKind.YourMonstersGainPiercingThisTurn),
                ("Monsters on your side of the field cannot be destroyed in a battle if the monsters' ATK are equal or lower.",
                    EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster),
                ("The original ATK of your opponent's monster(s) that battles with this card during his/her turn becomes " +
                 "200 points during the Damage Step, and this card gains 500 ATK.",
                    EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep),
                ("Your opponent takes any battle damage you would have taken from battles involving this card instead, and " +
                 "you gain that much Life Points.",
                    EffectActionKind.ReflectControllerBattleDamageFromBattlesInvolvingThis),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90539100 + i, name = "Synthetic DamageStepRules near miss " + i, type = "Effect Monster",
                    race = "Warrior", desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => c.Action == n.Action));
            }).Select(n => n.Action.ToString()).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(",", leaked));

            var baou = P(GuardianBaou);
            Check("Near miss: Guardian Baou's battle negation never takes the Effect-Monster kind",
                baou == null || !baou.ClauseList.Exists(c => c.TargetCardKind == "Effect"), Describe(baou));
        }

        // ───────────────────────────── D.D. Crazy Beast ─────────────────────────────

        static void DdCrazyBeastDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var beast = PlaceMonster(engine, p, DdCrazyBeast, 0, BattlePosition.Attack, true);
            var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
            var hand0 = o.Hand.Count;
            var ok = AttackAndDrain(engine, p, beast, witch);
            Check("D.D. Crazy Beast: the monster it destroys by battle is banished (no Witch search)",
                reached && ok && o.Banished.Contains(witch) && !InGy(o, witch) && o.Hand.Count == hand0 &&
                OnField(p, beast),
                $"ok={ok} banished={o.Banished.Contains(witch)} gy={InGy(o, witch)} hand={hand0}->{o.Hand.Count}");
        }

        // ───────────────────────────── Thousand Needles ─────────────────────────────

        static void ThousandNeedlesDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var faceUp in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var needles = PlaceMonster(engine, p, ThousandNeedles, 0, BattlePosition.Defense, faceUp);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var ok = OpponentAttacks(engine, axe, needles);
                Check(faceUp
                        ? "Thousand Needles (face-up Defense): a 1700 ATK attacker is destroyed at the end of the Damage Step"
                        : "Thousand Needles (Set): flipped by the attack, the 1700 ATK attacker is still destroyed",
                    reached && ok && InGy(o, axe) && OnField(p, needles) && o.LifePoints == oLp - 100,
                    $"ok={ok} axeGy={InGy(o, axe)} needles={OnField(p, needles)} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var needles = PlaceMonster(engine, p, ThousandNeedles, 0, BattlePosition.Defense, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, elf, needles);
                Check("Thousand Needles: a 1900 ATK attacker destroys it and is not destroyed",
                    reached && ok && InGy(p, needles) && OnField(o, elf),
                    $"ok={ok} needlesGy={InGy(p, needles)} elf={OnField(o, elf)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var needles = PlaceMonster(engine, p, ThousandNeedles, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                axe.LingeringAtkModifier = 100; // 1800: not lower than its DEF
                var ok = OpponentAttacks(engine, axe, needles);
                Check("Thousand Needles: an attacker whose ATK equals its DEF is not destroyed",
                    reached && ok && OnField(o, axe) && OnField(p, needles),
                    $"ok={ok} axe={OnField(o, axe)} needles={OnField(p, needles)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var needles = PlaceMonster(engine, p, ThousandNeedles, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, needles);
                Check("Thousand Needles: attacked in Attack Position, nothing happens to the attacker",
                    reached && ok && OnField(o, axe) && InGy(p, needles),
                    $"ok={ok} axe={OnField(o, axe)} needlesGy={InGy(p, needles)}");
            }
        }

        // ───────────────────────────── Continuous Destruction Punch ─────────────────────────────

        static void ContinuousPunchDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withPunch in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var activated = true;
                CardInstance punch = null;
                if (withPunch)
                {
                    punch = PutInHand(engine, p, ContinuousPunch);
                    activated = Activate(engine, p, punch, true) && punch.FaceUp && p.TryFindSpellTrap(punch, out _);
                }

                var soldier = PlaceMonster(engine, p, GiantSoldier, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, soldier);
                Check(withPunch
                        ? "Continuous Destruction Punch: the attacker lower than your Defense monster's DEF is destroyed"
                        : "Continuous Destruction Punch (control, not on the field): the attacker survives",
                    reached && activated && ok && OnField(p, soldier) && (withPunch ? InGy(o, axe) : OnField(o, axe)),
                    $"activated={activated} ok={ok} axeGy={InGy(o, axe)} soldier={OnField(p, soldier)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var punch = PutInHand(engine, p, ContinuousPunch);
                var activated = Activate(engine, p, punch, true);
                var soldier = PlaceMonster(engine, p, GiantSoldier, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, soldier);
                Check("Continuous Destruction Punch: an attacked Attack Position monster gives no destruction",
                    reached && activated && ok && OnField(o, axe) && InGy(p, soldier),
                    $"activated={activated} ok={ok} axe={OnField(o, axe)} soldierGy={InGy(p, soldier)}");
            }
        }

        // ───────────────────────────── Ultimate Obedient Fiend ─────────────────────────────

        static void ObedientFiendDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fiend = PlaceMonster(engine, p, ObedientFiend, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, BattleOx, 0, BattlePosition.Attack, true);
                if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var alone = engine.CanAttack(p, fiend);
                var kuriboh = PutInHand(engine, p, Kuriboh);
                var withHand = engine.CanAttack(p, fiend);
                p.Hand.Remove(kuriboh);
                var set = PlaceSetTrap(engine, p, Meteorain, 0);
                var withSet = engine.CanAttack(p, fiend);
                p.SpellTrapZones[0].Occupant = null;
                var again = engine.CanAttack(p, fiend);
                Check("Ultimate Obedient Fiend: attacks only as its controller's only card with an empty hand",
                    reached && alone && !withHand && !withSet && again && set != null,
                    $"alone={alone} hand={withHand} set={withSet} again={again}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fiend = PlaceMonster(engine, p, ObedientFiend, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
                var hand0 = o.Hand.Count;
                var ok = AttackAndDrain(engine, p, fiend, witch);
                Check("Ultimate Obedient Fiend: the Effect Monster it destroys is negated (no Witch search)",
                    reached && ok && InGy(o, witch) && witch.EffectsNegatedAfterBattle && o.Hand.Count == hand0,
                    $"ok={ok} gy={InGy(o, witch)} negated={witch.EffectsNegatedAfterBattle} hand={hand0}->{o.Hand.Count}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fiend = PlaceMonster(engine, p, ObedientFiend, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(engine, o, BattleOx, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, fiend, ox);
                Check("Ultimate Obedient Fiend: a Normal Monster it destroys is not marked negated",
                    reached && ok && InGy(o, ox) && !ox.EffectsNegatedAfterBattle,
                    $"ok={ok} gy={InGy(o, ox)} negated={ox.EffectsNegatedAfterBattle}");
            }
        }

        // ───────────────────────────── Meteorain ─────────────────────────────

        static void MeteorainDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withMeteorain in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var activated = true;
                if (withMeteorain)
                {
                    var met = PlaceSetTrap(engine, p, Meteorain, 0);
                    activated = Activate(engine, p, met, false) && InGy(p, met);
                }

                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, skull, elf);
                var want = withMeteorain ? oLp - 500 : oLp;
                Check(withMeteorain
                        ? "Meteorain: 2500 ATK attacking a 2000 DEF monster inflicts 500 piercing damage"
                        : "Meteorain (control, not activated): no piercing damage",
                    reached && activated && ok && InGy(o, elf) && o.LifePoints == want,
                    $"activated={activated} ok={ok} elfGy={InGy(o, elf)} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var met = PlaceSetTrap(engine, p, Meteorain, 0);
                var mine = engine.CanActivateSpellTrap(p, met, false);
                var oppTurn = ReachOpponentMain(engine);
                var theirs = engine.CanActivateSpellTrap(p, met, false);
                var aiMet = PlaceSetTrap(engine, engine.Opponent, Meteorain, 0);
                var aiCan = engine.CanActivateSpellTrap(engine.Opponent, aiMet, false);
                Check("Meteorain: only during your turn (either player)",
                    reached && mine && oppTurn && !theirs && aiCan,
                    $"mine={mine} oppTurn={oppTurn} theirs={theirs} ai={aiCan}");
            }
        }

        // ───────────────────────────── Kishido Spirit ─────────────────────────────

        static void KishidoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withKishido in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var activated = true;
                if (withKishido)
                {
                    var k = PutInHand(engine, p, KishidoSpirit);
                    activated = Activate(engine, p, k, true) && k.FaceUp && p.TryFindSpellTrap(k, out _);
                }

                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(engine, o, BattleOx, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, axe, ox);
                Check(withKishido
                        ? "Kishido Spirit: equal ATK (1700) — your monster survives, the opponent's is destroyed"
                        : "Kishido Spirit (control, not on the field): equal ATK destroys both",
                    reached && activated && ok && InGy(o, ox) && (withKishido ? OnField(p, axe) : InGy(p, axe)),
                    $"activated={activated} ok={ok} axe={OnField(p, axe)} oxGy={InGy(o, ox)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var k = PutInHand(engine, p, KishidoSpirit);
                var activated = Activate(engine, p, k, true);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(engine, o, BattleOx, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, celtic, ox);
                Check("Kishido Spirit: a lower-ATK monster of yours is still destroyed",
                    reached && activated && ok && InGy(p, celtic) && OnField(o, ox),
                    $"activated={activated} ok={ok} celticGy={InGy(p, celtic)} ox={OnField(o, ox)}");
            }

            {
                // The opponent's monster attacks yours, same ATK: Kishido Spirit protects the defender too.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var k = PutInHand(engine, p, KishidoSpirit);
                var activated = Activate(engine, p, k, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(engine, o, BattleOx, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, ox, axe);
                Check("Kishido Spirit: attacked by an equal-ATK monster, your monster survives",
                    reached && activated && ok && OnField(p, axe) && InGy(o, ox),
                    $"activated={activated} ok={ok} axe={OnField(p, axe)} oxGy={InGy(o, ox)}");
            }
        }

        // ───────────────────────────── Cat's Ear Tribe ─────────────────────────────

        static void CatsEarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cat = PlaceMonster(engine, p, CatsEarTribe, 0, BattlePosition.Attack, true);
                cat.LingeringAtkModifier = 100; // 300
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = OpponentAttacks(engine, axe, cat);
                Check("Cat's Ear Tribe: the 1700 ATK attacker battles with 200 original ATK (300 Cat wins, 100 damage)",
                    reached && ok && InGy(o, axe) && OnField(p, cat) && o.LifePoints == oLp - 100 &&
                    p.LifePoints == pLp && axe.OriginalAtkOverride == int.MinValue,
                    $"ok={ok} axeGy={InGy(o, axe)} cat={OnField(p, cat)} oLp={oLp}->{o.LifePoints} " +
                    $"pLp={pLp}->{p.LifePoints} override={axe.OriginalAtkOverride}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cat = PlaceMonster(engine, p, CatsEarTribe, 0, BattlePosition.Attack, true);
                cat.LingeringAtkModifier = 100; // 300
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                axe.LingeringAtkModifier = 500; // 2200, 700 in the Damage Step
                var pLp = p.LifePoints;
                var ok = OpponentAttacks(engine, axe, cat);
                Check("Cat's Ear Tribe: the attacker's other modifiers stay on top (700 vs 300); its ATK is back after",
                    reached && ok && InGy(p, cat) && OnField(o, axe) && p.LifePoints == pLp - 400 &&
                    axe.CurrentAtk == 2200 && axe.OriginalAtkOverride == int.MinValue,
                    $"ok={ok} catGy={InGy(p, cat)} axe={OnField(o, axe)} pLp={pLp}->{p.LifePoints} atk={axe.CurrentAtk}");
            }

            {
                // Its controller's own turn: no change (Lua: the turn player is not this card's controller).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var cat = PlaceMonster(engine, p, CatsEarTribe, 0, BattlePosition.Attack, true);
                var kuriboh = PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var ok = AttackAndDrain(engine, p, cat, kuriboh);
                Check("Cat's Ear Tribe: attacking during its controller's turn changes nothing (200 vs 300)",
                    reached && ok && InGy(p, cat) && OnField(o, kuriboh) && p.LifePoints == pLp - 100 &&
                    kuriboh.OriginalAtkOverride == int.MinValue,
                    $"ok={ok} catGy={InGy(p, cat)} kuriboh={OnField(o, kuriboh)} pLp={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Amazoness Swords Woman ─────────────────────────────

        static void SwordsWomanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asw = PlaceMonster(engine, p, SwordsWoman, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, asw, elf);
                Check("Amazoness Swords Woman: attacking a 1900 ATK monster, the opponent takes the 400",
                    reached && ok && p.LifePoints == pLp && o.LifePoints == oLp - 400 && InGy(p, asw) && OnField(o, elf),
                    $"ok={ok} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints} aswGy={InGy(p, asw)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asw = PlaceMonster(engine, p, SwordsWoman, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Defense, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, asw, soldier);
                Check("Amazoness Swords Woman: attacking a 2000 DEF monster, the opponent takes the 500",
                    reached && ok && p.LifePoints == pLp && o.LifePoints == oLp - 500 && OnField(p, asw) &&
                    OnField(o, soldier),
                    $"ok={ok} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asw = PlaceMonster(engine, p, SwordsWoman, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = OpponentAttacks(engine, elf, asw);
                Check("Amazoness Swords Woman: attacked by a 1900 ATK monster, the attacking player takes the 400",
                    reached && ok && p.LifePoints == pLp && o.LifePoints == oLp - 400 && InGy(p, asw),
                    $"ok={ok} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints} aswGy={InGy(p, asw)}");
            }

            {
                // Control: the battle damage of another monster's battle is not reflected.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, SwordsWoman, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, celtic, elf);
                Check("Amazoness Swords Woman: another monster's battle damage is not reflected",
                    reached && ok && p.LifePoints == pLp - 500 && o.LifePoints == oLp,
                    $"ok={ok} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }
        }
    }
}
