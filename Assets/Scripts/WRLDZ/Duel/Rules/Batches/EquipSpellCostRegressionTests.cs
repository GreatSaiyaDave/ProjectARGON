using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Guardian Ceal: "You can send 1 Equip Spell Card you control that is equipped to this card to the Graveyard to target
    /// 1 monster your opponent controls; destroy that target." IgnitionTemplates.ParseCost had no arm for the cost, so the
    /// card was Implemented with a free, unlimited destroy (probe 2026-09-30: no Equip Spell on it, the effect still
    /// destroyed the target, every Main Phase). The cost now compiles to
    /// <see cref="EffectClause.RequiresSendEquipSpellOnThisToGy"/> (ygopro c10755153 s.costfilter: face-up,
    /// GetEquipTarget() == this card, in your Spell &amp; Trap Zone; no count limit, the cost is the only limit). The
    /// effect cannot be activated without such a card; a lone one is sent as is, and the player picks among several.
    /// No other card in the pool prints this cost (Woodland Sprite's "Send 1 Equip Card equipped to this card to the
    /// Graveyard." is a different sentence: any Equip Card of either controller, EffectClause.EquipCostAnyEquipCard,
    /// EquipAndSpellProtection batch v161).
    /// An Equip Spell sent as this cost has its optional "When … : You can" effect miss the timing (Yugipedia "If... You
    /// Can VS When... You Can", Cost vs Effect): Axe of Despair and Malevolent Nuzzler are not placed on top of the Deck.
    /// Before, the AI's Guardian Ceal Tributed itself to put its Axe back, and the player's Axe prompt was opened and then
    /// overwritten by the target prompt. Black Pendant's mandatory damage still applies.
    /// </summary>
    public static class EquipSpellCostRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GuardianCeal = 10755153;        // FIRE Pyro 1700/1400
        const int WoodlandSprite = 6979239;
        const int AxeOfDespair = 40619825;        // +1000 ATK; sent from the field to the GY: Tribute 1; top of Deck
        const int Salamandra = 32268901;          // FIRE only, +700 ATK
        const int BurningSpear = 18937875;        // FIRE, +400 ATK / -200 DEF
        const int GiantSoldierOfStone = 13039848; // Normal 1300/2000
        const int GaiaTheFierceKnight = 6368038;  // Normal 2300/2100
        const int CelticGuardian = 91152256;      // Normal 1400/1200
        const int MalevolentNuzzler = 99597615;   // +700 ATK; sent from the field to the GY: pay 500 LP; top of Deck
        const int BlackPendant = 65169794;        // +500 ATK; sent from the field to the GY: 500 damage (mandatory)

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("EquipSpellCost: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NoEquipCannotActivate(db, pDeck, aDeck, Check);
            AxeOfDespairPaysTheCost(db, pDeck, aDeck, Check);
            SeveralEquipsPlayerPicks(db, pDeck, aDeck, Check);
            CancelAtTheCostPick(db, pDeck, aDeck, Check);
            EquipOnAnotherMonsterDoesNotCount(db, pDeck, aDeck, Check);
            OpponentsEquipSpellDoesNotCount(db, pDeck, aDeck, Check);
            AiPaysTheCost(db, pDeck, aDeck, Check);
            AiAxeMissesTheTiming(db, pDeck, aDeck, Check);
            WhenEffectsOfTheCost(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var def = db.Get(GuardianCeal);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                var act = prog?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>();
                var c = act.Count == 1 ? act[0] : null;
                Check("Guardian Ceal: one ignition destroy whose cost is the Equip Spell equipped to it (no count limit)",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice && c.IsPsctTarget &&
                    c.RequiresSendEquipSpellOnThisToGy && !c.OncePerTurn &&
                    EffectVocabulary.CostOf(c) == EffectCostKind.SendToGy &&
                    !c.RequiresSendThisToGy && !c.RequiresSendOtherYouControl && !c.RequiresSendNamedToGy,
                    $"status={st} {Describe(prog)}");
            }

            {
                // EquipAndSpellProtection batch (v161): Woodland Sprite's "Send 1 Equip Card equipped to this card" is the
                // wider cost (any Equip Card, either controller's), marked by EquipCostAnyEquipCard; Guardian Ceal's stays
                // "an Equip Spell Card you control" (EquipAndSpellProtectionRegressionTests drives Woodland Sprite).
                var def = db.Get(WoodlandSprite);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                var ceal = db.Get(GuardianCeal);
                var cealProg = ceal != null ? CardTextEffectCompiler.Compile(ceal) : null;
                Check("Woodland Sprite (\"Send 1 Equip Card equipped to this card\") is the any-Equip-Card form of this cost; " +
                      "Guardian Ceal's is not",
                    def != null && st == CardEffectStatusKind.Implemented && prog != null &&
                    prog.ClauseList.Exists(x => x != null && x.RequiresSendEquipSpellOnThisToGy && x.EquipCostAnyEquipCard) &&
                    cealProg != null && !cealProg.ClauseList.Exists(x => x != null && x.EquipCostAnyEquipCard),
                    $"status={st} {Describe(prog)}");
            }
        }

        // ───────────────────────────── mini-duels ─────────────────────────────

        /// <summary>The reported bug: no Equip Spell on Guardian Ceal, yet its effect destroyed the target.</summary>
        static void NoEquipCannotActivate(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            var can = e.CanActivateSpellTrap(p, ceal, fromHand: false);
            var act = e.TryActivateSpellTrap(p, ceal, fromHand: false);
            Check("No Equip Spell on Guardian Ceal: its effect cannot be activated",
                !can && !act && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks && Where(e, foe) == "O.mz" &&
                Where(e, ceal) == "P.mz",
                $"can={can} act={act} {State(e)} foe={Where(e, foe)}");
        }

        /// <summary>Axe of Despair on Guardian Ceal: the Axe is the cost, the target is destroyed; then no Equip is left.</summary>
        static void AxeOfDespairPaysTheCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            var other = PlaceMonster(e, o, CelticGuardian, 1, BattlePosition.Attack, true);
            var equipped = Equip(e, p, ceal, AxeOfDespair, out var axe);
            var atkEquipped = ceal.CurrentAtk;
            var logs = new List<string>();
            e.OnLog += logs.Add;
            var deck = p.Deck.Count;
            var can = equipped && e.CanActivateSpellTrap(p, ceal, fromHand: false);
            var act = can && e.TryActivateSpellTrap(p, ceal, fromHand: false);
            // A lone Equip Spell is sent without a prompt; the next prompt is the target.
            var axePaid = Where(e, axe) == "P.gy" && axe.EquippedTo == null && !ceal.Equips.Contains(axe);
            var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
            var targetPrompt = e.IsAwaitingEffectTarget &&
                               e.PendingActivation?.TargetKind != EffectTargetKind.SendEquipSpellOnThisToGy &&
                               legal.Contains(foe) && legal.Contains(other) && !legal.Contains(axe);
            Check("Axe of Despair equipped: Guardian Ceal activates; the Axe goes to the GY as the cost, then the target prompt",
                equipped && atkEquipped == 2700 && can && act && axePaid && targetPrompt,
                $"equipped={equipped} atk={atkEquipped} can={can} act={act} axe={Where(e, axe)} legal={Names(legal)} {State(e)}");

            var ok = Drive(e, foe);
            Check("Axe of Despair equipped: the target is destroyed, and only it; Guardian Ceal stays, back to 1700 ATK",
                ok && Where(e, foe) == "O.gy" && Where(e, other) == "O.mz" && Where(e, ceal) == "P.mz" &&
                Where(e, axe) == "P.gy" && ceal.CurrentAtk == 1700,
                $"ok={ok} foe={Where(e, foe)} other={Where(e, other)} ceal={Where(e, ceal)} axe={Where(e, axe)} atk={ceal.CurrentAtk} {State(e)}");
            Check("Axe of Despair sent as the cost: its \"When … you can\" effect misses the timing (no Tribute prompt, not on the Deck)",
                deck == p.Deck.Count && !logs.Exists(l => l.Contains("Tribute a monster you control")) &&
                logs.Exists(l => l.Contains("Axe of Despair") && l.Contains("misses the timing")),
                $"deck {deck}->{p.Deck.Count} log={string.Join(" / ", logs)}");

            var again = e.CanActivateSpellTrap(p, ceal, fromHand: false);
            var againAct = e.TryActivateSpellTrap(p, ceal, fromHand: false);
            Check("Axe of Despair spent: Guardian Ceal cannot activate its effect again (the cost is its only limit)",
                !again && !againAct && Where(e, other) == "O.mz" && !e.IsAwaitingEffectTarget,
                $"again={again} act={againAct} other={Where(e, other)} {State(e)}");
        }

        /// <summary>Two Equip Spells on Guardian Ceal: the player picks which one is sent; the other stays equipped.</summary>
        static void SeveralEquipsPlayerPicks(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, true);
            var equipped = Equip(e, p, ceal, Salamandra, out var sala) & Equip(e, p, ceal, BurningSpear, out var spear);
            var act = equipped && e.TryActivateSpellTrap(p, ceal, fromHand: false);
            var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
            var costPrompt = e.IsAwaitingEffectTarget &&
                             e.PendingActivation?.TargetKind == EffectTargetKind.SendEquipSpellOnThisToGy &&
                             legal.Count == 2 && legal.Contains(sala) && legal.Contains(spear) &&
                             (e.PendingActivation?.Prompt ?? "").Contains("Equip Spell");
            var nothingPaidYet = Where(e, sala) == "P.stz" && Where(e, spear) == "P.stz";
            Check("Salamandra + Burning Spear equipped: the cost prompt offers both Equip Spells, and nothing is paid yet",
                act && costPrompt && nothingPaidYet,
                $"equipped={equipped} act={act} legal={Names(legal)} prompt=\"{e.PendingActivation?.Prompt}\" sala={Where(e, sala)} spear={Where(e, spear)} {State(e)}");

            var picked = costPrompt && e.TrySelectEffectTarget(spear);
            var legal2 = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
            Check("Picking Burning Spear sends it (only it) to the GY; the target prompt follows",
                picked && Where(e, spear) == "P.gy" && Where(e, sala) == "P.stz" && sala.EquippedTo == ceal &&
                e.IsAwaitingEffectTarget && legal2.Count == 1 && legal2.Contains(foe),
                $"picked={picked} spear={Where(e, spear)} sala={Where(e, sala)} salaOn={sala.EquippedTo?.Name} legal={Names(legal2)} {State(e)}");

            var ok = Drive(e, foe);
            Check("Several Equip Spells: the target is destroyed; Salamandra stays on Guardian Ceal (2400 ATK)",
                ok && Where(e, foe) == "O.gy" && Where(e, spear) == "P.gy" && Where(e, sala) == "P.stz" &&
                sala.EquippedTo == ceal && ceal.CurrentAtk == 2400,
                $"ok={ok} foe={Where(e, foe)} spear={Where(e, spear)} sala={Where(e, sala)} atk={ceal.CurrentAtk} {State(e)}");
        }

        /// <summary>
        /// Cancel at the cost pick: nothing is paid, nothing is destroyed, and the effect can still be used. (The empty
        /// Chain Link a cancelled monster ignition leaves behind is not checked here: SpellTrapEffects.CancelPending drops
        /// the unresolved link only for Spell/Trap activations, the same for Throwstone Unit or Freed the Brave Wanderer.)
        /// </summary>
        static void CancelAtTheCostPick(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Attack, true);
            var equipped = Equip(e, p, ceal, Salamandra, out var sala) & Equip(e, p, ceal, BurningSpear, out var spear);
            var act = equipped && e.TryActivateSpellTrap(p, ceal, fromHand: false);
            var prompt = e.IsAwaitingEffectTarget &&
                         e.PendingActivation?.TargetKind == EffectTargetKind.SendEquipSpellOnThisToGy;
            e.CancelEffectTargeting();
            Check("Cancel at the cost pick: both Equip Spells stay on Guardian Ceal and the target is not destroyed",
                act && prompt && !e.IsAwaitingEffectTarget && Where(e, sala) == "P.stz" &&
                Where(e, spear) == "P.stz" && sala.EquippedTo == ceal && spear.EquippedTo == ceal &&
                Where(e, foe) == "O.mz" && e.CanActivateSpellTrap(p, ceal, fromHand: false),
                $"act={act} prompt={prompt} sala={Where(e, sala)} spear={Where(e, spear)} foe={Where(e, foe)} {State(e)}");
        }

        /// <summary>An Equip Spell you control on another monster is not "equipped to this card".</summary>
        static void EquipOnAnotherMonsterDoesNotCount(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
            var mine = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            var equipped = Equip(e, p, mine, AxeOfDespair, out var axe);
            var can = e.CanActivateSpellTrap(p, ceal, fromHand: false);
            Check("Axe of Despair on another monster you control: Guardian Ceal cannot activate its effect",
                equipped && !can && Where(e, axe) == "P.stz" && Where(e, foe) == "O.mz",
                $"equipped={equipped} can={can} axe={Where(e, axe)} {State(e)}");
        }

        /// <summary>"you control": an opponent's Equip Spell equipped to Guardian Ceal cannot be sent.</summary>
        static void OpponentsEquipSpellDoesNotCount(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            // The opponent's Burning Spear, face-up in the opponent's Spell & Trap Zone, equipped to the player's Ceal.
            var spear = e.CreateCardInstance(BurningSpear);
            spear.FaceUp = true;
            spear.SetThisTurn = false;
            o.SpellTrapZones[0].Occupant = spear;
            spear.EquippedTo = ceal;
            ceal.Equips.Add(spear);
            var can = e.CanActivateSpellTrap(p, ceal, fromHand: false);
            Check("The opponent's Equip Spell equipped to Guardian Ceal does not pay the cost (\"you control\")",
                !can && Where(e, spear) == "O.stz" && Where(e, foe) == "O.mz",
                $"can={can} spear={Where(e, spear)} {State(e)}");
        }

        /// <summary>The AI's Guardian Ceal pays with its Equip Spell and destroys the player's monster, with no prompt.</summary>
        static void AiPaysTheCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            foreach (var who in new[] { p, o })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            var ceal = PlaceMonster(e, o, GuardianCeal, 0, BattlePosition.Attack, true);
            var mine = PlaceMonster(e, p, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            var noEquip = e.CanActivateSpellTrap(o, ceal, fromHand: false);
            var equipped = Equip(e, o, ceal, Salamandra, out var sala);
            var act = equipped && e.TryActivateSpellTrap(o, ceal, fromHand: false);
            var noPrompt = !e.IsAwaitingEffectTarget;
            Settle(e);
            Check("AI Guardian Ceal: no Equip Spell, no activation; with Salamandra it sends it and destroys the player's monster",
                reached && !noEquip && act && noPrompt && Where(e, sala) == "O.gy" && Where(e, mine) == "P.gy" &&
                Where(e, ceal) == "O.mz" && !e.Chain.HasLinks,
                $"reached={reached} noEquip={noEquip} equipped={equipped} act={act} noPrompt={noPrompt} sala={Where(e, sala)} mine={Where(e, mine)} {State(e)}");
        }

        /// <summary>
        /// The AI's Guardian Ceal pays with Axe of Despair: the Axe's optional effect misses the timing. Before, the AI
        /// Tributed Guardian Ceal itself (its only monster) to put the Axe on top of its Deck, mid-activation.
        /// </summary>
        static void AiAxeMissesTheTiming(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            foreach (var who in new[] { p, o })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            var ceal = PlaceMonster(e, o, GuardianCeal, 0, BattlePosition.Attack, true);
            var mine = PlaceMonster(e, p, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            var equipped = Equip(e, o, ceal, AxeOfDespair, out var axe);
            var deck = o.Deck.Count;
            var act = equipped && e.TryActivateSpellTrap(o, ceal, fromHand: false);
            Settle(e);
            Check("AI Guardian Ceal pays with Axe of Despair: Ceal stays, the Axe stays in the GY, the player's monster is destroyed",
                reached && act && Where(e, ceal) == "O.mz" && Where(e, axe) == "O.gy" && o.Deck.Count == deck &&
                Where(e, mine) == "P.gy" && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"reached={reached} equipped={equipped} act={act} ceal={Where(e, ceal)} axe={Where(e, axe)} deck {deck}->{o.Deck.Count} mine={Where(e, mine)} {State(e)}");
        }

        /// <summary>
        /// Malevolent Nuzzler as the cost: its optional "pay 500 LP; place this card on the top of your Deck" misses the
        /// timing (no LP paid). Black Pendant as the cost: its mandatory 500 damage still applies.
        /// </summary>
        static void WhenEffectsOfTheCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
                var foe = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, ceal, MalevolentNuzzler, out var nuzzler);
                var lp = p.LifePoints;
                var deck = p.Deck.Count;
                var act = equipped && e.TryActivateSpellTrap(p, ceal, fromHand: false);
                var ok = act && Drive(e, foe);
                Check("Malevolent Nuzzler sent as the cost: no LP paid, it stays in the GY; the target is destroyed",
                    ok && Where(e, nuzzler) == "P.gy" && p.LifePoints == lp && p.Deck.Count == deck &&
                    Where(e, foe) == "O.gy" && Where(e, ceal) == "P.mz",
                    $"equipped={equipped} act={act} ok={ok} nuzzler={Where(e, nuzzler)} lp {lp}->{p.LifePoints} deck {deck}->{p.Deck.Count} foe={Where(e, foe)} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
                var foe = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, ceal, BlackPendant, out var pendant);
                var oppLp = o.LifePoints;
                var act = equipped && e.TryActivateSpellTrap(p, ceal, fromHand: false);
                var ok = act && Drive(e, foe);
                Check("Black Pendant sent as the cost: its mandatory 500 damage still applies; the target is destroyed",
                    ok && Where(e, pendant) == "P.gy" && o.LifePoints == oppLp - 500 && Where(e, foe) == "O.gy",
                    $"equipped={equipped} act={act} ok={ok} pendant={Where(e, pendant)} oppLp {oppLp}->{o.LifePoints} foe={Where(e, foe)} {State(e)}");
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

        /// <summary>Activate an Equip Spell from <paramref name="who"/>'s hand onto <paramref name="host"/>.</summary>
        static bool Equip(DuelEngine e, DuelistState who, CardInstance host, int equipId, out CardInstance equip)
        {
            equip = PutInHand(e, who, equipId);
            var ok = e.TryActivateSpellTrap(who, equip, fromHand: true);
            if (e.IsAwaitingEffectTarget) ok &= e.TrySelectEffectTarget(host);
            Settle(e);
            return ok && equip.EquippedTo == host && host.Equips.Contains(equip);
        }

        /// <summary>Pass every open chain window.</summary>
        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>Every target prompt picks <paramref name="target"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, CardInstance target)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var ok = e.TrySelectEffectTarget(target);
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

            return picked && !e.IsAwaitingEffectTarget && !e.IsAwaitingResponse && !e.Chain.HasLinks;
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
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/tgt={c.RequiresTargetChoice}/equipCost={c.RequiresSendEquipSpellOnThisToGy}/opt={c.OncePerTurn}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
