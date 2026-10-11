using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DcrEquipSpells batch: Shooting Star Bow - Ceal, Twin Swords of Flashing Light - Tryce, Wicked-Breaking Flamberge -
    /// Baou, Rod of the Mind's Eye, Rod of Silence - Kay'est, Butterfly Dagger - Elma (<see cref="DcrEquipSpellsAtoms"/>,
    /// <see cref="DcrEquipSpellsRuntime"/>). Compile shapes, near misses, and mini-duels through TryActivateSpellTrap, the
    /// send cost's pick / Cancel, TryAttack, MST / Dark Hole, Book of Moon / Rush Recklessly / Snatch Steal and the AI.
    /// </summary>
    public static class DcrEquipSpellsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Bow = 95638658;
        const int Tryce = 21900719;
        const int Flamberge = 68427465;
        const int MindsEye = 94793422;
        const int Silence = 95515060;
        const int Dagger = 69243953;

        const int Celtic = 91152256;          // Warrior 1400/1200
        const int Gaia = 6368038;             // Warrior 2300/2100
        const int AxeRaider = 48305365;       // Warrior 1700/1150
        const int MysticalElf = 15025844;     // Spellcaster 800/2000
        const int Witch = 78010363;           // 1100/1200, "If this card is sent from the field to the GY: Add 1 monster …"
        const int Mst = 5318639;
        const int DarkHole = 53129443;
        const int BookOfMoon = 14087893;      // Quick-Play: target 1 face-up monster; face-down Defense
        const int RushRecklessly = 70046172;  // Quick-Play: target 1 face-up monster; +700 ATK
        const int SnatchSteal = 45986603;     // Equip Spell: take control of the equipped monster

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DcrEquipSpells: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            BowDuels(db, pDeck, aDeck, Check);
            TryceDuels(db, pDeck, aDeck, Check);
            FlambergeDuels(db, pDeck, aDeck, Check);
            MindsEyeDuels(db, pDeck, aDeck, Check);
            SilenceDuels(db, pDeck, aDeck, Check);
            DaggerDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            bool Impl(int id, out CompiledCardProgram prog, out EffectClause equip, out EffectClause rider, out string why)
            {
                var def = db.Get(id);
                prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                why = $"status={st} {Describe(prog)}";
                equip = prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault();
                rider = prog?.ClauseList.FirstOrDefault(c => c != null && c.Timing != EffectTiming.Activate);
                return prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                       prog.ClauseList.Count == 2 && equip != null && rider != null &&
                       equip.Action == EffectActionKind.EquipThisToTarget && equip.Zone == EffectZoneFilter.FieldAnyMonster &&
                       equip.RequiresTargetChoice && equip.IsPsctTarget && equip.RequiresTargetFaceUp && equip.StaysOnField &&
                       equip.AiEquipsOwnMonster;
            }

            {
                var ok = Impl(Bow, out _, out var eq, out var r, out var why);
                Check("Shooting Star Bow - Ceal: any face-up monster, -1000 ATK, Equip direct-attack grant",
                    ok && eq.EquipAtkBonus == -1000 && !eq.RequiresDiscardCost &&
                    r.Timing == EffectTiming.ContinuousWhileFaceUp && r.Action == EffectActionKind.EquippedCanAttackDirectly &&
                    !r.MakesChainLink, why);
            }
            {
                var ok = Impl(Tryce, out _, out var eq, out var r, out var why);
                Check("Twin Swords of Flashing Light - Tryce: send 1 other hand card (not a discard), -500 ATK, 1 extra attack",
                    ok && eq.EquipAtkBonus == -500 && eq.RequiresDiscardCost && eq.DiscardCostAttribute == "*" &&
                    eq.DiscardCostIsSend && EffectVocabulary.CostOf(eq) == EffectCostKind.SendToGy &&
                    r.Action == EffectActionKind.EquippedGainsExtraAttacks && r.Amount == 1 && !r.MakesChainLink, why);
            }
            {
                var ok = Impl(Flamberge, out _, out var eq, out var r, out var why);
                Check("Wicked-Breaking Flamberge - Baou: send cost, +500 ATK, equip-host battle negation of Effect Monsters",
                    ok && eq.EquipAtkBonus == 500 && eq.RequiresDiscardCost && eq.DiscardCostIsSend &&
                    r.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle && r.AppliesToEquippedHost &&
                    r.TargetCardKind == "Effect" && !r.AffectsThisCardOnly, why);
            }
            {
                var ok = Impl(MindsEye, out _, out var eq, out var r, out var why);
                Check("Rod of the Mind's Eye: any face-up monster, battle damage to the opponent becomes 1000",
                    ok && eq.EquipAtkBonus == 0 && eq.EquipDefBonus == 0 &&
                    r.Action == EffectActionKind.EquippedBattleDamageToOpponentBecomes && r.Amount == 1000, why);
            }
            {
                var ok = Impl(Silence, out _, out var eq, out var r, out var why);
                Check("Rod of Silence - Kay'est: +500 DEF, equip-host Spell target negation + destroy (not this card)",
                    ok && eq.EquipDefBonus == 500 && eq.EquipAtkBonus == 0 &&
                    r.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy && r.AppliesToEquippedHost &&
                    r.NegateRespondsToSpell && !r.NegateRespondsToTrap && r.DestroyNegatedCard && r.ExceptThisCard &&
                    !r.AffectsThisCardOnly, why);
            }
            {
                var ok = Impl(Dagger, out _, out var eq, out var r, out var why);
                Check("Butterfly Dagger - Elma: +300 ATK, optional return to the hand when destroyed while equipped",
                    ok && eq.EquipAtkBonus == 300 && r.Timing == EffectTiming.SentFromFieldToGy &&
                    r.Action == EffectActionKind.ReturnThisFromGyToHand && r.IsOptional &&
                    r.CheckedAt == ConditionCheckedAt.Activation && r.RequiresDestroyedByEffectWhileEquipped, why);
            }

            Check("ResolutionOf maps the new actions",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.EquippedCanAttackDirectly }) == EffectResolutionKind.DirectAttack &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.EquippedGainsExtraAttacks }) == EffectResolutionKind.ExtraAttack &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.EquippedBattleDamageToOpponentBecomes }) == EffectResolutionKind.Damage &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.ReturnThisFromGyToHand }) == EffectResolutionKind.ReturnToHand);
            Check("CostOf: a plain discard cost is still a discard",
                EffectVocabulary.CostOf(new EffectClause { RequiresDiscardCost = true, DiscardCostAttribute = "*" }) == EffectCostKind.Discard);
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static CompiledCardProgram CompileText(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990175, name = "Near-miss Equip", type = "Spell Card", frameType = "spell", race = "Equip", desc = text
            });

        static bool FromThisBatch(EffectClause c) =>
            c != null && (c.Action == EffectActionKind.EquippedCanAttackDirectly ||
                          c.Action == EffectActionKind.EquippedGainsExtraAttacks ||
                          c.Action == EffectActionKind.EquippedBattleDamageToOpponentBecomes ||
                          c.Action == EffectActionKind.ReturnThisFromGyToHand ||
                          c.DiscardCostIsSend || c.AiEquipsOwnMonster ||
                          ((c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy ||
                            c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle) && c.AppliesToEquippedHost));

        static void NearMisses(Chk Check)
        {
            var texts = new[]
            {
                // A longer card: the whole-text anchor refuses it.
                "The equipped monster loses 1000 ATK, but it can attack your opponent directly. " +
                "It cannot attack while you control another monster.",
                // A discard, not a send.
                "Discard 1 card; equip this card to a monster. It loses 500 ATK, but it can make a second attack during each " +
                "Battle Phase.",
                // Trap effects, not Spell effects.
                "The equipped monster gains 500 DEF. Negate other Trap effects that target the equipped monster, and if you " +
                "do, destroy that Spell Card.",
                // Sent, not destroyed.
                "The equipped monster gains 300 ATK. When this card is sent to the Graveyard while equipped: You can return " +
                "this card to the hand.",
                // Doubled, not a fixed amount.
                "If a monster equipped with this card inflicts Battle Damage to your opponent, the damage is doubled.",
                // "this card", not the equipped monster.
                "Send 1 card from your hand to the GY, then target 1 monster on the field; equip this card to that target. " +
                "It gains 500 ATK. Negate the effects of opponent's monsters destroyed by battle with this card.",
            };
            var leaked = new List<string>();
            foreach (var t in texts)
            {
                var prog = CompileText(t);
                if (prog != null && (prog.FullyCompiled || prog.ClauseList.Exists(FromThisBatch)))
                    leaked.Add($"[{t.Substring(0, Math.Min(40, t.Length))}…] {Describe(prog)}");
            }

            Check("Near misses: look-alike Equip texts stay uncompiled", leaked.Count == 0, string.Join(" || ", leaked));
        }

        // ───────────────────────────── Shooting Star Bow - Ceal ─────────────────────────────

        static void BowDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, Bow, out var bow);
                var atk = celtic.CurrentAtk;
                var battle = EnterBattle(e, p);
                var axeDirect = e.CanAttackDirectly(p, axe);
                var celticDirect = e.CanAttackDirectly(p, celtic);
                var lp0 = o.LifePoints;
                var attacked = e.TryAttack(p, celtic, null);
                DrainCombat(e);
                Check("Shooting Star Bow: -1000 ATK; the equipped monster attacks directly while the opponent has a monster",
                    reached && equipped && atk == 400 && battle && !axeDirect && celticDirect && attacked &&
                    o.LifePoints == lp0 - 400 && Where(e, gaia) == "O.mz",
                    $"reached={reached} equipped={equipped} atk={atk} battle={battle} axe={axeDirect} celtic={celticDirect} " +
                    $"attacked={attacked} lp={lp0}->{o.LifePoints} {State(e)}");

                // The grant is the Equip's effect: kept while the monster is negated, gone while Bow is negated.
                var granted = e.GrantsDirectAttack(celtic);
                celtic.IsNegated = true;
                var monsterNegated = e.GrantsDirectAttack(celtic);
                celtic.IsNegated = false;
                bow.IsNegated = true;
                var bowNegated = e.GrantsDirectAttack(celtic);
                bow.IsNegated = false;
                Check("Shooting Star Bow: the grant survives the monster's negation, not Bow's",
                    granted && monsterNegated && !bowNegated,
                    $"granted={granted} monsterNegated={monsterNegated} bowNegated={bowNegated}");
            }

            // The AI equips its own monster.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var bow = PutInHand(e, o, Bow);
                var act = reached && e.TryActivateSpellTrap(o, bow, fromHand: true);
                Settle(e);
                Check("Shooting Star Bow (AI): the AI equips its own monster, not the player's",
                    act && bow.EquippedTo == mine && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} host={bow.EquippedTo?.Name} {State(e)}");
            }
        }

        // ───────────────────────────── Twin Swords of Flashing Light - Tryce ─────────────────────────────

        static void TryceDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's send cost: picked from the hand (not Tryce itself), then the target; two attacks.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lines = new List<string>();
                e.OnLog += lines.Add;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Defense, true);
                var tryce = PutInHand(e, p, Tryce);
                var elf = PutInHand(e, p, MysticalElf);
                var act = e.TryActivateSpellTrap(p, tryce, fromHand: true);
                var costPrompt = e.PendingActivation != null && e.PendingActivation.AwaitingDiscardCost &&
                                 e.PendingActivation.LegalTargets.Contains(elf) &&
                                 !e.PendingActivation.LegalTargets.Contains(tryce) &&
                                 (e.PendingActivation.Prompt ?? "").Contains("send 1 card");
                var paid = costPrompt && e.TrySelectEffectTarget(elf);
                var targetPrompt = e.PendingActivation != null && !e.PendingActivation.AwaitingDiscardCost &&
                                   e.PendingActivation.LegalTargets.Contains(celtic) &&
                                   e.PendingActivation.LegalTargets.Contains(gaia);
                var picked = targetPrompt && e.TrySelectEffectTarget(celtic);
                Settle(e);
                var sentLog = lines.Any(l => l.Contains("Cost: send Mystical Elf")) &&
                              !lines.Any(l => l.Contains("Cost: discard Mystical Elf"));
                Check("Tryce: the player sends 1 other hand card as the cost, then picks the monster; -500 ATK, 2 attacks",
                    reached && act && costPrompt && paid && targetPrompt && picked && Where(e, elf) == "P.gy" &&
                    Where(e, tryce) == "P.stz" && tryce.EquippedTo == celtic && celtic.CurrentAtk == 900 &&
                    e.MaxAttacksThisTurn(celtic) == 2 && sentLog,
                    $"reached={reached} act={act} cost={costPrompt} paid={paid} target={targetPrompt} picked={picked} " +
                    $"elf={Where(e, elf)} tryce={Where(e, tryce)} atk={celtic.CurrentAtk} max={e.MaxAttacksThisTurn(celtic)} " +
                    $"sentLog={sentLog} {State(e)}");

                // Two attacks in this Battle Phase, not three (Gaia in Defense: 900 < 2100, so attack directly after it goes).
                e.Opponent.MonsterZones[0].Occupant = null;
                var lp0 = o.LifePoints;
                var battle = EnterBattle(e, p);
                var first = e.TryAttack(p, celtic, null);
                DrainCombat(e);
                var second = e.TryAttack(p, celtic, null);
                DrainCombat(e);
                var third = e.TryAttack(p, celtic, null);
                DrainCombat(e);
                Check("Tryce: the equipped monster attacks twice during the Battle Phase, not a third time",
                    battle && first && second && !third && o.LifePoints == lp0 - 1800,
                    $"battle={battle} first={first} second={second} third={third} lp={lp0}->{o.LifePoints}");
            }

            // No other card in the hand: Tryce cannot be activated.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var tryce = PutInHand(e, p, Tryce);
                var act = e.TryActivateSpellTrap(p, tryce, fromHand: true);
                Settle(e);
                Check("Tryce: with no other card in the hand it cannot be activated",
                    reached && !act && Where(e, tryce) == "P.hand" && !e.IsAwaitingEffectTarget,
                    $"act={act} tryce={Where(e, tryce)} {State(e)}");
            }

            // Cancel at the target: the activation never happened, the sent card comes back.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
                var tryce = PutInHand(e, p, Tryce);
                var elf = PutInHand(e, p, MysticalElf);
                var act = e.TryActivateSpellTrap(p, tryce, fromHand: true);
                var paid = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(elf);
                var sent = Where(e, elf) == "P.gy";
                var cancelled = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                Settle(e);
                Check("Tryce: Cancel at the target refunds the sent card and returns Tryce to the hand",
                    reached && act && paid && sent && cancelled && Where(e, elf) == "P.hand" && Where(e, tryce) == "P.hand" &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"act={act} paid={paid} sent={sent} cancelled={cancelled} elf={Where(e, elf)} tryce={Where(e, tryce)} {State(e)}");
            }

            // The AI pays the send cost and equips its own monster.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var tryce = PutInHand(e, o, Tryce);
                var fodder = PutInHand(e, o, MysticalElf);
                var act = reached && e.TryActivateSpellTrap(o, tryce, fromHand: true);
                Settle(e);
                Check("Tryce (AI): the AI sends its other hand card and equips its own monster",
                    act && tryce.EquippedTo == mine && Where(e, fodder) == "O.gy" && e.MaxAttacksThisTurn(mine) == 2 &&
                    !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} host={tryce.EquippedTo?.Name} fodder={Where(e, fodder)} {State(e)}");
            }
        }

        // ───────────────────────────── Wicked-Breaking Flamberge - Baou ─────────────────────────────

        static void FlambergeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withFlamberge in new[] { true, false })
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var attacker = PlaceMonster(e, p, withFlamberge ? Celtic : AxeRaider, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(e, o, Witch, 0, BattlePosition.Attack, true);
                var equipped = true;
                CardInstance flamberge = null;
                if (withFlamberge)
                {
                    flamberge = PutInHand(e, p, Flamberge);
                    var elf = PutInHand(e, p, MysticalElf);
                    equipped = e.TryActivateSpellTrap(p, flamberge, fromHand: true);
                    if (e.IsAwaitingEffectTarget) equipped &= e.TrySelectEffectTarget(elf);
                    if (e.IsAwaitingEffectTarget) equipped &= e.TrySelectEffectTarget(attacker);
                    Settle(e);
                    equipped &= flamberge.EquippedTo == attacker && attacker.CurrentAtk == 1900 && Where(e, elf) == "P.gy";
                }

                var hand0 = o.Hand.Count;
                var deck0 = o.Deck.Count;
                var battle = EnterBattle(e, p);
                var attacked = e.TryAttack(p, attacker, witch);
                DrainCombat(e);
                // The AI's Witch search (when not negated) resolves on its own; a human pick never opens for the AI.
                Settle(e);
                var searched = o.Hand.Count > hand0 || o.Deck.Count < deck0;
                if (withFlamberge)
                    Check("Flamberge: +500 ATK; Witch of the Black Forest destroyed by battle with the equipped monster has its " +
                          "'sent to the GY' search negated",
                        reached && equipped && battle && attacked && Where(e, witch) == "O.gy" && witch.EffectsNegatedAfterBattle &&
                        !searched,
                        $"reached={reached} equipped={equipped} battle={battle} attacked={attacked} witch={Where(e, witch)} " +
                        $"flag={witch.EffectsNegatedAfterBattle} hand={hand0}->{o.Hand.Count} deck={deck0}->{o.Deck.Count}");
                else
                    Check("Flamberge (control): without it, Witch's search is not negated",
                        reached && battle && attacked && Where(e, witch) == "O.gy" && !witch.EffectsNegatedAfterBattle,
                        $"attacked={attacked} witch={Where(e, witch)} flag={witch.EffectsNegatedAfterBattle}");
            }
        }

        // ───────────────────────────── Rod of the Mind's Eye ─────────────────────────────

        static void MindsEyeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, gaia, MindsEye, out _);
                var lp0 = o.LifePoints;
                var battle = EnterBattle(e, p);
                var attacked = e.TryAttack(p, gaia, null);
                DrainCombat(e);
                Check("Rod of the Mind's Eye: a 2300 direct attack inflicts 1000",
                    reached && equipped && battle && attacked && o.LifePoints == lp0 - 1000,
                    $"equipped={equipped} battle={battle} attacked={attacked} lp={lp0}->{o.LifePoints}");
            }

            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, MindsEye, out _);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var battle = EnterBattle(e, p);
                var attacked = e.TryAttack(p, celtic, gaia);
                DrainCombat(e);
                Check("Rod of the Mind's Eye: damage to the Rod's own controller is unchanged (900)",
                    reached && equipped && battle && attacked && p.LifePoints == pLp - 900 && o.LifePoints == oLp,
                    $"equipped={equipped} attacked={attacked} p={pLp}->{p.LifePoints} o={oLp}->{o.LifePoints}");
            }

            {
                // A defending monster with the Rod inflicts the damage: 2100 DEF vs 1400 ATK → 700 becomes 1000.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var wall = PlaceMonster(e, p, Gaia, 0, BattlePosition.Defense, true);
                var equipped = Equip(e, p, wall, MindsEye, out _);
                var foe = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var battle = ReachOpponentBattle(e);
                var oLp = o.LifePoints;
                var attacked = battle && e.TryAttack(o, foe, wall);
                DrainCombat(e);
                Check("Rod of the Mind's Eye: the equipped defender's 700 battle damage to the attacker becomes 1000",
                    reached && equipped && attacked && o.LifePoints == oLp - 1000,
                    $"equipped={equipped} battle={battle} attacked={attacked} o={oLp}->{o.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Rod of Silence - Kay'est ─────────────────────────────

        static void SilenceDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, Silence, out var rod);
                var def = celtic.CurrentDef;
                var book = PutInHand(e, p, BookOfMoon);
                var act = e.TryActivateSpellTrap(p, book, fromHand: true);
                if (e.IsAwaitingEffectTarget) act &= e.TrySelectEffectTarget(celtic);
                Settle(e);
                Check("Rod of Silence: +500 DEF; Book of Moon on the equipped monster is negated and destroyed",
                    reached && equipped && def == 1700 && act && celtic.FaceUp && celtic.Position == BattlePosition.Attack &&
                    rod.EquippedTo == celtic && Where(e, rod) == "P.stz" && Where(e, book) == "P.gy",
                    $"equipped={equipped} def={def} act={act} faceUp={celtic.FaceUp} pos={celtic.Position} rod={Where(e, rod)} " +
                    $"book={Where(e, book)} {State(e)}");

                // A Spell that targets another monster is not negated.
                var atk0 = axe.CurrentAtk;
                var rush = PutInHand(e, p, RushRecklessly);
                var act2 = e.TryActivateSpellTrap(p, rush, fromHand: true);
                if (e.IsAwaitingEffectTarget) act2 &= e.TrySelectEffectTarget(axe);
                Settle(e);
                Check("Rod of Silence: a Spell that targets another monster resolves",
                    act2 && axe.CurrentAtk == atk0 + 700, $"act2={act2} atk={atk0}->{axe.CurrentAtk}");
            }

            // The opponent's Snatch Steal on the equipped monster: negated, destroyed; control stays.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, Silence, out var rod);
                var opp = ReachOpponentMainKeepingBoard(e);
                o.Hand.Clear();
                var snatch = PutInHand(e, o, SnatchSteal);
                var act = opp && e.TryActivateSpellTrap(o, snatch, fromHand: true);
                Settle(e);
                Check("Rod of Silence: the opponent's Snatch Steal on the equipped monster is negated and destroyed",
                    reached && equipped && act && Where(e, celtic) == "P.mz" && Where(e, snatch) == "O.gy" &&
                    Where(e, rod) == "P.stz" && !e.IsAwaitingEffectTarget,
                    $"equipped={equipped} opp={opp} act={act} celtic={Where(e, celtic)} snatch={Where(e, snatch)} " +
                    $"rod={Where(e, rod)} {State(e)}");
            }
        }

        // ───────────────────────────── Butterfly Dagger - Elma ─────────────────────────────

        static void DaggerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var answer in new[] { true, false })
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, Dagger, out var dagger);
                var atk = celtic.CurrentAtk;
                var mst = PutInHand(e, p, Mst);
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Card == mst) e.TrySelectEffectTarget(dagger);
                Settle(e);
                var prompt = e.PendingActivation != null && e.PendingActivation.LegalTargets.Contains(dagger) &&
                             Where(e, dagger) == "P.gy";
                var done = prompt && (answer ? e.TrySelectEffectTarget(dagger) : e.CancelEffectTargeting());
                Settle(e);
                Check(answer
                        ? "Butterfly Dagger: +300 ATK; destroyed by MST while equipped, the player returns it to the hand"
                        : "Butterfly Dagger: the player declines — it stays in the GY",
                    reached && equipped && atk == 1700 && act && prompt && done &&
                    Where(e, dagger) == (answer ? "P.hand" : "P.gy") && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"equipped={equipped} atk={atk} act={act} prompt={prompt} done={done} dagger={Where(e, dagger)} {State(e)}");
            }

            // Its monster destroyed (Dark Hole): sent by rule — no prompt, it stays in the GY.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, Dagger, out var dagger);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                Check("Butterfly Dagger: sent to the GY because its monster left — no return",
                    reached && equipped && act && Where(e, dagger) == "P.gy" && !e.IsAwaitingEffectTarget,
                    $"act={act} dagger={Where(e, dagger)} {State(e)}");
            }

            // The AI's Dagger: equipped to its own monster, destroyed by the player's MST, back to the AI's hand.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var dagger = PutInHand(e, o, Dagger);
                var act = reached && e.TryActivateSpellTrap(o, dagger, fromHand: true);
                Settle(e);
                var aiEquipped = dagger.EquippedTo == mine;
                for (var i = 0; i < 6 && !(e.TurnPlayer == p && e.InMainPhase); i++)
                {
                    Settle(e);
                    e.TryEndTurnSafe(e.TurnPlayer);
                }

                p.Hand.Clear();
                var mst = PutInHand(e, p, Mst);
                var act2 = e.TurnPlayer == p && e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Card == mst) e.TrySelectEffectTarget(dagger);
                Settle(e);
                Check("Butterfly Dagger (AI): the AI equips its own monster; destroyed by MST, it returns to the AI's hand",
                    act && aiEquipped && act2 && Where(e, dagger) == "O.hand" && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} host={dagger.EquippedTo?.Name} act2={act2} dagger={Where(e, dagger)} {State(e)}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The player's Main Phase 1 of turn 3 (a Battle Phase is legal), board and hands cleared.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Clear(e);
            return e;
        }

        /// <summary>The opponent's Main Phase, board and hands cleared.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            Clear(e);
            return e;
        }

        /// <summary>From the player's turn to the opponent's Main Phase, keeping the board.</summary>
        static bool ReachOpponentMainKeepingBoard(DuelEngine e)
        {
            for (var t = 0; t < 6 && !(e.TurnPlayer == e.Opponent && e.InMainPhase); t++)
            {
                Settle(e);
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            Settle(e);
            return e.TurnPlayer == e.Opponent && e.InMainPhase;
        }

        static void Clear(DuelEngine e)
        {
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static bool EnterBattle(DuelEngine e, DuelistState who)
        {
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(who);
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            DrainCombat(e);
            return e.Phase == DuelPhase.Battle;
        }

        /// <summary>Activate an Equip Spell from <paramref name="who"/>'s hand onto <paramref name="host"/>.</summary>
        static bool Equip(DuelEngine e, DuelistState who, CardInstance host, int equipId, out CardInstance equip)
        {
            equip = PutInHand(e, who, equipId);
            var ok = e.TryActivateSpellTrap(who, equip, fromHand: true);
            if (e.IsAwaitingEffectTarget) ok &= e.TrySelectEffectTarget(host);
            Settle(e);
            return ok && equip.EquippedTo == host && host.Equips.Contains(equip);
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[" +
            $"{string.Join(",", (e.PendingActivation?.LegalTargets ?? new List<CardInstance>()).Select(t => t?.Name))}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/tgt={c.RequiresTargetChoice}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
