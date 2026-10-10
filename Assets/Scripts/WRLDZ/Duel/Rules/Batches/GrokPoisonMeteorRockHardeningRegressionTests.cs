using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Review of five cards an unsupervised pass compiled on 2026-10-02 with no tests
    /// (<see cref="PoisonMeteorCestusAtoms"/>, <see cref="RockSentenceAtoms"/>). Each is checked against its ygopro script:
    /// <list type="bullet">
    /// <item>Cestus of Dagla c28106077: Equip to a Fairy, +500 ATK; EVENT_BATTLE_DAMAGE with the equipped monster in eg and
    /// ep ~= tp: its controller gains the damage (TRIGGER_F).</item>
    /// <item>Meteor of Destruction c33767325: condition GetLP(1-tp) &gt; 3000 at activation; 1000 damage, no recheck.</item>
    /// <item>Poison of the Old Man c8842266: Duel.SelectOption at activation; only the chosen bullet resolves.</item>
    /// <item>Rock Bombardment c20781762: cost, send 1 Rock from the Deck (player selects); 500 damage.</item>
    /// <item>Stamping Destruction c81385346: condition aux.FaceupFilter(IsRace Dragon) you control; target any other
    /// Spell/Trap on the field (face-down too); if Duel.Destroy &gt; 0, 500 damage to the target's controller.</item>
    /// </list>
    /// </summary>
    public static class GrokPoisonMeteorRockHardeningRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Cestus = 28106077;
        const int Meteor = 33767325;
        const int Poison = 8842266;
        const int RockBombardment = 20781762;
        const int Stamping = 81385346;

        const int SpiritOfTheHarp = 80770678;   // Fairy 800/2000
        const int MysticalElf = 15025844;       // Spellcaster 800/2000
        const int CelticGuardian = 91152256;    // Warrior 1400/1200
        const int KoumoriDragon = 67724379;     // Dragon 1500/1200
        const int GiantSoldier = 13039848;      // Rock 1300/2000
        const int PotOfGreed = 55144522;
        const int MysticalSpaceTyphoon = 5318639;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("GrokPoisonMeteorRock: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            CestusEquipsOnlyAFairy(db, pDeck, aDeck, Check);
            CestusGainsTheBattleDamage(db, pDeck, aDeck, Check);
            MeteorNeedsMoreThan3000(db, pDeck, aDeck, Check);
            PoisonResolvesTheChosenBullet(db, pDeck, aDeck, Check);
            PoisonAiTakesTheDamageBullet(db, pDeck, aDeck, Check);
            PoisonInAnAttackWindow(db, pDeck, aDeck, Check);
            SeatedPromptsCancel(db, pDeck, aDeck, Check);
            AiChainsItsTargetedSetCard(db, pDeck, aDeck, Check);
            AutoPickSeatPaysTheDeckCost(db, pDeck, aDeck, Check);
            RockBombardmentPaysTheDeckCost(db, pDeck, aDeck, Check);
            RockBombardmentInAnAttackWindow(db, pDeck, aDeck, Check);
            StampingDestroysAndBurnsTheController(db, pDeck, aDeck, Check);
            StampingNeedsAFaceUpDragon(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            (CompiledCardProgram prog, CardEffectStatusKind st) Of(int id)
            {
                var def = db.Get(id);
                return (def != null ? CardTextEffectCompiler.Compile(def) : null,
                    def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented);
            }

            {
                var (p, st) = Of(Cestus);
                var a = p?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>();
                var t = p?.ClausesFor(EffectTiming.YourMonsterInflictsBattleDamage) ?? new List<EffectClause>();
                Check("Cestus of Dagla: Fairy equip +500 ATK, and an LP gain equal to the equipped monster's battle damage",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && p.ClauseList.Count == 2 &&
                    a.Count == 1 && a[0].Action == EffectActionKind.EquipThisToTarget && a[0].RaceFilter == "Fairy" &&
                    a[0].EquipAtkBonus == 500 && a[0].EquipDefBonus == 0 && !a[0].ChooseAtkOrDef &&
                    t.Count == 1 && t[0].Action == EffectActionKind.GainLifePoints && t[0].AmountIsEquippedBattleDamage &&
                    t[0].OnlyEquippedMonster && t[0].Side == EffectSide.Controller,
                    $"status={st} {Describe(p)}");
            }

            {
                var (p, st) = Of(Meteor);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Meteor of Destruction: 1000 damage, only while the opponent has more than 3000 LP (an activation condition)",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 1000 &&
                    c.ActivationCondition == PoisonMeteorCestusRuntime.OppLpGreaterThan && c.ConditionAmount == 3000 &&
                    !c.RequiresTargetChoice,
                    $"status={st} {Describe(p)}");
            }

            {
                var (p, st) = Of(Poison);
                var a = p?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>();
                Check("Poison of the Old Man: two bullets, gain 1200 LP (1) or inflict 800 damage (2)",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && a.Count == 2 &&
                    a[0].Action == EffectActionKind.GainLifePoints && a[0].Amount == 1200 && a[0].EffectChoiceIndex == 1 &&
                    a[1].Action == EffectActionKind.InflictDamageToOpponent && a[1].Amount == 800 &&
                    a[1].EffectChoiceIndex == 2,
                    $"status={st} {Describe(p)}");
            }

            {
                var (p, st) = Of(RockBombardment);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Rock Bombardment: cost send 1 Rock from the Deck, then 500 damage",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500 &&
                    c.RequiresSendRaceFromDeck && c.RaceFilter == "Rock",
                    $"status={st} {Describe(p)}");
            }

            {
                var (p, st) = Of(Stamping);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Stamping Destruction: needs a Dragon you control; target 1 Spell/Trap, destroy it, 500 to its controller",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldSpellTraps &&
                    c.RequiresTargetChoice && c.IsPsctTarget && c.DamageItsController == 500 &&
                    c.ActivationCondition == PoisonMeteorCestusRuntime.YouControlRace && c.ConditionName == "Dragon" &&
                    string.IsNullOrEmpty(c.RaceFilter),
                    $"status={st} {Describe(p)}");
            }
        }

        // ───────────────────────────── Cestus of Dagla ─────────────────────────────

        static void CestusEquipsOnlyAFairy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var harp = PlaceMonster(e, p, SpiritOfTheHarp, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                var cestus = PutInHand(e, p, Cestus);
                var act = e.TryActivateSpellTrap(p, cestus, fromHand: true);
                var legal = Legal(e);
                var offered = e.IsAwaitingEffectTarget ? legal.Contains(harp) && !legal.Contains(celtic) : true;
                Drive(e, c => c == harp);
                Check("Cestus of Dagla: equips the Fairy (a Warrior is not offered); it gains 500 ATK",
                    act && offered && cestus.EquippedTo == harp && harp.Equips.Contains(cestus) && harp.CurrentAtk == 1300 &&
                    celtic.CurrentAtk == 1400 && Where(e, cestus) == "P.stz",
                    $"act={act} legal={Names(legal)} eq={cestus.EquippedTo?.Name} harpAtk={harp.CurrentAtk} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var cestus = PutInHand(e, p, Cestus);
                var can = e.CanActivateSpellTrap(p, cestus, fromHand: true);
                Check("Cestus of Dagla: no Fairy, cannot be activated",
                    !can && p.Hand.Contains(cestus), $"can={can}");
            }
        }

        /// <summary>
        /// The opponent's monsters attack into the player's monsters, which inflict the battle damage. Only the equipped
        /// Fairy's damage is gained.
        /// </summary>
        static void CestusGainsTheBattleDamage(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var battle = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var harp = PlaceMonster(e, p, SpiritOfTheHarp, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
            var cestus = e.CreateCardInstance(Cestus);
            cestus.FaceUp = true;
            p.SpellTrapZones[0].Occupant = cestus;
            cestus.EquippedTo = harp;
            harp.Equips.Add(cestus);
            var elf1 = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
            var elf2 = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
            e.NotifyPublic();
            var harpAtk = harp.CurrentAtk;

            p.LifePoints = 4000;
            o.LifePoints = 4000;
            var a1 = e.TryAttack(o, elf1, celtic);
            DrainCombat(e);
            var pAfter1 = p.LifePoints;
            var oAfter1 = o.LifePoints;
            Check("Cestus of Dagla: a monster that is not the equipped one inflicts battle damage — no LP gained",
                battle && a1 && oAfter1 == 3400 && pAfter1 == 4000,
                $"battle={battle} a1={a1} p={pAfter1} o={oAfter1} {State(e)}");

            var a2 = e.TryAttack(o, elf2, harp);
            DrainCombat(e);
            Check("Cestus of Dagla: the equipped Fairy (1300) inflicts 500 battle damage — its controller gains 500 LP",
                battle && harpAtk == 1300 && a2 && o.LifePoints == oAfter1 - 500 && p.LifePoints == pAfter1 + 500,
                $"harpAtk={harpAtk} a2={a2} p={p.LifePoints} o={o.LifePoints} {State(e)}");
        }

        // ───────────────────────────── Meteor of Destruction ─────────────────────────────

        static void MeteorNeedsMoreThan3000(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var lp in new[] { 3001, 3000 })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                o.LifePoints = lp;
                var pLp = p.LifePoints;
                var meteor = PutInHand(e, p, Meteor);
                var can = e.CanActivateSpellTrap(p, meteor, fromHand: true);
                var act = e.TryActivateSpellTrap(p, meteor, fromHand: true);
                Drive(e, _ => false);
                if (lp > 3000)
                    Check("Meteor of Destruction: opponent at 3001 LP — 1000 damage",
                        can && act && o.LifePoints == lp - 1000 && p.LifePoints == pLp && Where(e, meteor) == "P.gy",
                        $"can={can} act={act} o={o.LifePoints} p={p.LifePoints} meteor={Where(e, meteor)} {State(e)}");
                else
                    Check("Meteor of Destruction: opponent at exactly 3000 LP — cannot be activated (\"higher than\")",
                        !can && !act && o.LifePoints == lp && p.Hand.Contains(meteor),
                        $"can={can} act={act} o={o.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Poison of the Old Man ─────────────────────────────

        static void PoisonResolvesTheChosenBullet(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var gain in new[] { true, false })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var poison = PutInHand(e, p, Poison);
                var act = e.TryActivateSpellTrap(p, poison, fromHand: true);
                var legal = Legal(e);
                var twoChoices = legal.Count == 2;
                var picked = Drive(e, c => c?.Def != null && c.CardId == 0 &&
                                           c.Def.name.StartsWith(gain ? "Gain" : "Inflict", StringComparison.Ordinal));
                var ok = gain
                    ? p.LifePoints == pLp + 1200 && o.LifePoints == oLp
                    : p.LifePoints == pLp && o.LifePoints == oLp - 800;
                Check($"Poison of the Old Man: the player picks {(gain ? "gain 1200 LP" : "inflict 800")} — only that bullet resolves",
                    act && twoChoices && picked && ok && Where(e, poison) == "P.gy",
                    $"act={act} legal={Names(legal)} picked={picked} p={p.LifePoints} o={o.LifePoints} poison={Where(e, poison)} {State(e)}");
            }
        }

        static void PoisonAiTakesTheDamageBullet(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var pLp = p.LifePoints;
            var oLp = o.LifePoints;
            var poison = PutInHand(e, o, Poison);
            var act = e.TryActivateSpellTrap(o, poison, fromHand: true);
            Drive(e, _ => false);
            Check("Poison of the Old Man: the AI takes the damage bullet only",
                main && act && p.LifePoints == pLp - 800 && o.LifePoints == oLp && Where(e, poison) == "O.gy",
                $"main={main} act={act} p={p.LifePoints} o={o.LifePoints} {State(e)}");
        }

        /// <summary>A Set Poison activated in answer to an attack (a response seated on the chain): one bullet, not both.</summary>
        static void PoisonInAnAttackWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var gain in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var poison = PlaceSetTrap(e, p, Poison, 0);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                p.LifePoints = 4000;
                o.LifePoints = 4000;
                var atk = e.TryAttack(o, elf, null);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, poison, fromHand: false);
                Drive(e, c => c?.Def != null && c.CardId == 0 &&
                              c.Def.name.StartsWith(gain ? "Gain" : "Inflict", StringComparison.Ordinal));
                DrainCombat(e);
                // The Elf's 800 direct damage still applies.
                var ok = gain
                    ? p.LifePoints == 4000 + 1200 - 800 && o.LifePoints == 4000
                    : p.LifePoints == 4000 - 800 && o.LifePoints == 4000 - 800;
                Check($"Poison of the Old Man in an attack window: picks {(gain ? "gain" : "damage")} — only that bullet resolves",
                    battle && atk && window && act && ok && Where(e, poison) == "P.gy",
                    $"battle={battle} atk={atk} window={window} act={act} p={p.LifePoints} o={o.LifePoints} poison={Where(e, poison)} {State(e)}");
            }
        }

        /// <summary>
        /// The seated bullet and Deck-cost prompts: the choices offered, and Cancel takes the activation back (the Set card is
        /// face-down again, nothing paid, the attack window is still open).
        /// </summary>
        static void SeatedPromptsCancel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var id in new[] { Poison, RockBombardment })
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                p.Graveyard.Clear();
                p.Deck.Clear();
                p.Deck.AddRange(new[] { CelticGuardian, GiantSoldier, KoumoriDragon });
                var card = PlaceSetTrap(e, p, id, 0);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                p.LifePoints = 4000;
                o.LifePoints = 4000;
                var atk = e.TryAttack(o, elf, null);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, card, fromHand: false);
                var legal = Legal(e);
                var prompt = id == Poison
                    ? legal.Count == 2 && legal.All(t => t.CardId == 0)
                    : legal.Count == 1 && legal[0].CardId == GiantSoldier &&
                      e.PendingActivation?.TargetKind == EffectTargetKind.SendRaceMonsterFromDeckToGy;
                var cancelled = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                var back = Where(e, card) == "P.stz" && !card.FaceUp && !e.Chain.HasLinks && p.Deck.Count == 3 &&
                           p.Graveyard.Count == 0;
                var stillOpen = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                DrainCombat(e);
                Check($"{db.Get(id)?.name} answering an attack: the {(id == Poison ? "bullet" : "Deck Rock")} prompt; Cancel takes it back",
                    battle && atk && act && prompt && cancelled && back && stillOpen && o.LifePoints == 4000 &&
                    p.LifePoints == 3200,
                    $"battle={battle} atk={atk} act={act} legal={Names(legal)} cancelled={cancelled} back={back} " +
                    $"open={stillOpen} card={Where(e, card)} deck={p.Deck.Count} p={p.LifePoints} o={o.LifePoints} {State(e)}");
            }
        }

        /// <summary>
        /// The AI chains its Set Poison of the Old Man when the player's Mystical Space Typhoon targets it (a response
        /// seated on the chain): the damage bullet only. (The AI does not chain Rock Bombardment there; its automatic
        /// Deck-cost pick is covered by <see cref="AutoPickSeatPaysTheDeckCost"/>.)
        /// </summary>
        static void AiChainsItsTargetedSetCard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var id in new[] { Poison })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { CelticGuardian, GiantSoldier, KoumoriDragon });
                var card = PlaceSetTrap(e, o, id, 0);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var mst = PutInHand(e, p, MysticalSpaceTyphoon);
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                Drive(e, c => c == card);
                var ok = id == Poison
                    ? p.LifePoints == pLp - 800 && o.LifePoints == oLp
                    : p.LifePoints == pLp - 500 && o.LifePoints == oLp &&
                      o.Graveyard.Any(g => g?.CardId == GiantSoldier) && !o.Deck.Contains(GiantSoldier) && o.Deck.Count == 2;
                Check($"{db.Get(id)?.name}: the AI chains it to Mystical Space Typhoon — {(id == Poison ? "the damage bullet only" : "the Rock leaves the Deck, 500 damage")}",
                    act && Where(e, card) == "O.gy" && Where(e, mst) == "P.gy" && ok,
                    $"act={act} card={Where(e, card)} mst={Where(e, mst)} p={p.LifePoints} o={o.LifePoints} deck={o.Deck.Count} {State(e)}");
            }
        }

        /// <summary>
        /// The automatic (AI) choices of a response seated on the chain, through the same resolver with autoPickTarget:
        /// Rock Bombardment sends a Rock from the Deck without a prompt; Poison of the Old Man takes the damage bullet.
        /// </summary>
        static void AutoPickSeatPaysTheDeckCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var id in new[] { RockBombardment, Poison })
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                p.Graveyard.Clear();
                p.Deck.Clear();
                p.Deck.AddRange(new[] { CelticGuardian, GiantSoldier, KoumoriDragon });
                var card = PlaceSetTrap(e, p, id, 0);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                p.LifePoints = 4000;
                o.LifePoints = 4000;
                var atk = e.TryAttack(o, elf, null);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && TextEffectRuntime.TryResolveActivation(e, p, card, false,
                    CompiledEffectCache.GetOrCompile(card.Def), autoPickTarget: true);
                var noPrompt = !e.IsAwaitingEffectTarget;
                DrainCombat(e);
                var ok = id == RockBombardment
                    ? o.LifePoints == 3500 && p.LifePoints == 3200 && p.Graveyard.Any(g => g?.CardId == GiantSoldier) &&
                      !p.Deck.Contains(GiantSoldier) && p.Deck.Count == 2
                    : o.LifePoints == 3200 && p.LifePoints == 3200;
                Check($"{db.Get(id)?.name} seated with automatic choices: {(id == Poison ? "the damage bullet only" : "a Rock leaves the Deck, 500 damage")}",
                    battle && atk && act && noPrompt && ok && Where(e, card) == "P.gy",
                    $"battle={battle} atk={atk} act={act} noPrompt={noPrompt} p={p.LifePoints} o={o.LifePoints} deck={p.Deck.Count} card={Where(e, card)} {State(e)}");
            }
        }

        // ───────────────────────────── Rock Bombardment ─────────────────────────────

        static void RockBombardmentPaysTheDeckCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { CelticGuardian, GiantSoldier, CelticGuardian });
                var oLp = o.LifePoints;
                var rock = PlaceSetTrap(e, p, RockBombardment, 0);
                var act = e.TryActivateSpellTrap(p, rock, fromHand: false);
                var legal = Legal(e);
                Drive(e, c => c?.Def != null && c.Def.race == "Rock");
                var sent = p.Graveyard.Any(g => g?.CardId == GiantSoldier);
                Check("Rock Bombardment: the Rock monster leaves the Deck for the GY, then 500 damage",
                    act && sent && p.Deck.Count == 2 && !p.Deck.Contains(GiantSoldier) && o.LifePoints == oLp - 500 &&
                    Where(e, rock) == "P.gy" && legal.All(c => c?.Def?.race == "Rock"),
                    $"act={act} legal={Names(legal)} sent={sent} deck={p.Deck.Count} o={o.LifePoints} rock={Where(e, rock)} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { CelticGuardian, KoumoriDragon });
                var rock = PlaceSetTrap(e, p, RockBombardment, 0);
                var can = e.CanActivateSpellTrap(p, rock, fromHand: false);
                Check("Rock Bombardment: no Rock monster in the Deck — cannot be activated",
                    !can && Where(e, rock) == "P.stz" && !rock.FaceUp, $"can={can}");
            }
        }

        /// <summary>Rock Bombardment answering an attack: the Deck cost is still paid (or it cannot be activated there).</summary>
        static void RockBombardmentInAnAttackWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var hasRock in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                p.Graveyard.Clear();
                p.Deck.Clear();
                p.Deck.AddRange(hasRock ? new[] { CelticGuardian, GiantSoldier } : new[] { CelticGuardian, KoumoriDragon });
                var rock = PlaceSetTrap(e, p, RockBombardment, 0);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                p.LifePoints = 4000;
                o.LifePoints = 4000;
                var atk = e.TryAttack(o, elf, null);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, rock, fromHand: false);
                Drive(e, c => c?.Def != null && c.Def.race == "Rock");
                DrainCombat(e);
                var sent = p.Graveyard.Any(g => g?.CardId == GiantSoldier);
                if (hasRock)
                    Check("Rock Bombardment in an attack window: the Rock is sent from the Deck and 500 damage is inflicted",
                        battle && atk && window && act && sent && !p.Deck.Contains(GiantSoldier) && o.LifePoints == 3500,
                        $"battle={battle} atk={atk} window={window} act={act} sent={sent} deck={p.Deck.Count} o={o.LifePoints} {State(e)}");
                else
                    Check("Rock Bombardment in an attack window, no Rock in the Deck: no free burn",
                        battle && atk && !act && o.LifePoints == 4000 && Where(e, rock) == "P.stz",
                        $"battle={battle} atk={atk} window={window} act={act} o={o.LifePoints} rock={Where(e, rock)} {State(e)}");
            }
        }

        // ───────────────────────────── Stamping Destruction ─────────────────────────────

        static void StampingDestroysAndBurnsTheController(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var own in new[] { false, true })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, KoumoriDragon, 0, BattlePosition.Attack, true);
                var oppSet = PlaceSetTrap(e, o, PotOfGreed, 0);
                var mySet = PlaceSetTrap(e, p, PotOfGreed, 1);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var stamping = PutInHand(e, p, Stamping);
                var act = e.TryActivateSpellTrap(p, stamping, fromHand: true);
                var legal = Legal(e);
                var victim = own ? mySet : oppSet;
                Drive(e, c => c == victim);
                var ok = own
                    ? Where(e, mySet) == "P.gy" && Where(e, oppSet) == "O.stz" && p.LifePoints == pLp - 500 && o.LifePoints == oLp
                    : Where(e, oppSet) == "O.gy" && Where(e, mySet) == "P.stz" && o.LifePoints == oLp - 500 && p.LifePoints == pLp;
                Check($"Stamping Destruction: destroys {(own ? "your own" : "the opponent's")} Set Spell; 500 damage to its controller",
                    act && legal.Contains(oppSet) && legal.Contains(mySet) && !legal.Contains(stamping) && ok &&
                    Where(e, stamping) == "P.gy",
                    $"act={act} legal={Names(legal)} opp={Where(e, oppSet)} mine={Where(e, mySet)} p={p.LifePoints} o={o.LifePoints} {State(e)}");
            }
        }

        static void StampingNeedsAFaceUpDragon(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var setup in new[] { "face-down Dragon", "no Dragon", "opponent's Dragon" })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                if (setup == "face-down Dragon")
                    PlaceMonster(e, p, KoumoriDragon, 0, BattlePosition.Defense, false);
                else if (setup == "no Dragon")
                    PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                else
                    PlaceMonster(e, o, KoumoriDragon, 0, BattlePosition.Attack, true);
                var oppSet = PlaceSetTrap(e, o, PotOfGreed, 0);
                var stamping = PutInHand(e, p, Stamping);
                var can = e.CanActivateSpellTrap(p, stamping, fromHand: true);
                Check($"Stamping Destruction: {setup} only — cannot be activated (the Dragon must be face-up, yours)",
                    !can && Where(e, oppSet) == "O.stz" && p.Hand.Contains(stamping), $"can={can}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Every prompt picks the first legal card matching <paramref name="want"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
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

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/amt={c.Amount}/race={c.RaceFilter}/cond={c.ActivationCondition}:{c.ConditionName}:{c.ConditionAmount}/choice={c.EffectChoiceIndex}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
