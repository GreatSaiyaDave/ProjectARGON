using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DerivedAtkMonsters batch (v176): Great Maju Garzett, Maju Garzett, Guardian Tryce, Gren Maju Da Eiza, Chaos
    /// Necromancer. Compile shapes through the real compiler, mini-duels through the engine entry points (TryNormalSummon
    /// with PendingTributes, TryFlipSummon, TryActivateSpellTrap + TrySelectEffectTarget, ReachOpponentBattle + TryAttack),
    /// and fail-closed look-alikes. Runtime: <see cref="DerivedAtkMonstersRuntime"/>.
    /// </summary>
    public static class DerivedAtkMonstersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GreatMajuGarzett = 47942531; // Fiend Lv6 0/0
        const int MajuGarzett = 8794435;       // Fiend Lv7 ?/0
        const int GuardianTryce = 46037213;    // Thunder Lv5 1900/1700
        const int GrenMajuDaEiza = 36584821;   // Fiend Lv3 ?/?
        const int ChaosNecromancer = 1434352;  // Fiend Lv1 0/0

        const int TwinSwords = 21900719;       // Twin Swords of Flashing Light - Tryce (Equip Spell)
        const int Celtic = 91152256;           // Warrior 1400/1200
        const int GiantSoldier = 13039848;     // Rock 1300/2000
        const int Kuriboh = 40640057;          // Fiend 300/200
        const int BlueEyes = 89631139;         // Dragon 3000/2500
        const int MonsterReborn = 83764718;    // Normal Spell
        const int PotOfGreed = 55144522;       // Normal Spell

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DerivedAtkMonsters: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            GarzettDuels(db, pDeck, aDeck, Check);
            TryceDuels(db, pDeck, aDeck, Check);
            CountStatDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = e.PendingActivation?.LegalTargets?.FirstOrDefault(want);
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }
        }

        /// <summary>End turns until it is the Player's Main Phase of turn 3 or later.</summary>
        static bool ReachPlayerNextMain(DuelEngine e)
        {
            for (var t = 0; t < 12 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.InMainPhase); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.InMainPhase) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.InMainPhase;
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 90176001, name = "DerivedAtkMonsters probe", type = "Effect Monster", frameType = "effect",
                race = "Fiend", attribute = "DARK", level = 4, atk = 0, def = 0, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var great = Prog(db, GreatMajuGarzett);
            Check("Great Maju Garzett FullyCompiled: ATK becomes 2x the first Tribute's original ATK",
                great != null && great.FullyCompiled && Implemented(db, GreatMajuGarzett) && great.ClauseList.Count == 1 &&
                Has(great, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.ThisAtkBecomesTributedOriginalAtk && c.Amount == 2 &&
                                c.CountSource == DerivedAtkMonstersRuntime.FirstTribute && !c.MakesChainLink),
                Describe(great));

            var maju = Prog(db, MajuGarzett);
            Check("Maju Garzett FullyCompiled: ATK becomes the Tributes' combined original ATK",
                maju != null && maju.FullyCompiled && Implemented(db, MajuGarzett) && maju.ClauseList.Count == 1 &&
                Has(maju, c => c.Action == EffectActionKind.ThisAtkBecomesTributedOriginalAtk && c.Amount == 1 &&
                               c.CountSource == DerivedAtkMonstersRuntime.AllTributes),
                Describe(maju));

            var tryce = Prog(db, GuardianTryce);
            Check("Guardian Tryce FullyCompiled: mandatory destroyed-and-sent trigger revives its Tribute (gate structural)",
                tryce != null && tryce.FullyCompiled && Implemented(db, GuardianTryce) && tryce.ClauseList.Count == 1 &&
                Has(tryce, c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                c.Action == EffectActionKind.SpecialSummonTributeMaterialsFromGy &&
                                c.RequiresDestroyed && !c.IsOptional && c.MakesChainLink) &&
                string.Equals(PsctGrammar.SummonGateNamed(db.Get(GuardianTryce).desc),
                    "Twin Swords of Flashing Light - Tryce", StringComparison.OrdinalIgnoreCase),
                Describe(tryce));

            var gren = Prog(db, GrenMajuDaEiza);
            Check("Gren Maju Da Eiza FullyCompiled: ATK/DEF set to your banished cards x 400",
                gren != null && gren.FullyCompiled && Implemented(db, GrenMajuDaEiza) && gren.ClauseList.Count == 1 &&
                Has(gren, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                               c.Action == EffectActionKind.GainSelfAtkPerCount && c.SelfStatIsSet &&
                               c.Amount == 400 && c.DefAmount == 400 &&
                               c.CountSource == DerivedAtkMonstersRuntime.YourBanishedCards),
                Describe(gren));

            var necro = Prog(db, ChaosNecromancer);
            Check("Chaos Necromancer FullyCompiled: ATK set to Monster Cards in your GY x 300 (DEF untouched)",
                necro != null && necro.FullyCompiled && Implemented(db, ChaosNecromancer) && necro.ClauseList.Count == 1 &&
                Has(necro, c => c.Action == EffectActionKind.GainSelfAtkPerCount && c.SelfStatIsSet &&
                                c.Amount == 300 && c.DefAmount == 0 && c.CountSource == "MonstersInYourGy"),
                Describe(necro));

            // Fail-closed look-alikes: other counts / other Tribute wordings stay unparsed.
            var spells = CompileText("The ATK of this card is the number of Spell Cards in your Graveyard x 300.");
            var oppBanished = CompileText("This card's ATK/DEF are each equal to the number of banished cards x 400.");
            var tributeDef = CompileText("The DEF of this card becomes twice the original DEF of 1 monster that you " +
                                         "Tributed for the Tribute Summon of this card.");
            var tryceRider = CompileText("When this card is destroyed and sent to the Graveyard: Special Summon the monster " +
                                         "from the Graveyard that was used for its Tribute Summon, and if you do, draw 1 card.");
            Check("Look-alikes stay fail-closed (Spell count, any-banished count, Tribute DEF, Tryce + rider)",
                !Has(spells, c => c.SelfStatIsSet) && !(spells?.FullyCompiled ?? false) &&
                !Has(oppBanished, c => c.SelfStatIsSet) && !(oppBanished?.FullyCompiled ?? false) &&
                !Has(tributeDef, c => c.Action == EffectActionKind.ThisAtkBecomesTributedOriginalAtk) &&
                !(tributeDef?.FullyCompiled ?? false) &&
                !(tryceRider?.FullyCompiled ?? false),
                $"spells=[{Describe(spells)}] oppBanished=[{Describe(oppBanished)}] tributeDef=[{Describe(tributeDef)}] " +
                $"tryceRider=[{Describe(tryceRider)}]");
        }

        // ───────────────────────────── Great Maju Garzett / Maju Garzett ─────────────────────────────

        static void GarzettDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Tribute Summon: 2 x Celtic Guardian's 1400 = 2800, kept across refreshes; turning it face-down ends it.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var great = PutInHand(e, p, GreatMajuGarzett);
                e.PendingTributes.Add(celtic);
                var summoned = e.TryNormalSummon(p, great, false);
                Drive(e, _ => true);
                FieldSpellEffects.RefreshBoard(e);
                var atk = great.CurrentAtk;
                var battleAtk = BattleMechanics.AttackValue(great);
                FieldSpellEffects.RefreshBoard(e);
                var again = great.CurrentAtk;
                Check("Great Maju Garzett: Tribute Summoned with Celtic Guardian (1400), its ATK becomes 2800 and stays",
                    summoned && OnField(p, great) && p.Graveyard.Contains(celtic) && atk == 2800 && battleAtk == 2800 &&
                    again == 2800,
                    $"summoned={summoned} atk={atk} battle={battleAtk} again={again}");

                var down = e.ApplyEffectPositionChange(great, BattlePosition.Defense, false);
                FieldSpellEffects.RefreshBoard(e);
                great.FaceUp = true;
                great.Position = BattlePosition.Attack;
                FieldSpellEffects.RefreshBoard(e);
                Check("Great Maju Garzett: turned face-down and back up, its ATK is its own 0 again (RESET_TURN_SET)",
                    down && great.CurrentAtk == 0, $"down={down} atk={great.CurrentAtk}");
            }

            {
                // A Tribute-Summoned Great Maju that is negated loses the ATK for good (RESET_DISABLE).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var be = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                var great = PutInHand(e, p, GreatMajuGarzett);
                e.PendingTributes.Add(be);
                var summoned = e.TryNormalSummon(p, great, false);
                Drive(e, _ => true);
                FieldSpellEffects.RefreshBoard(e);
                var before = great.CurrentAtk;
                great.IsNegated = true;
                FieldSpellEffects.RefreshBoard(e);
                var negated = great.CurrentAtk;
                great.IsNegated = false;
                FieldSpellEffects.RefreshBoard(e);
                Check("Great Maju Garzett: 6000 from Blue-Eyes; negated → 0, and still 0 once the negation ends",
                    summoned && before == 6000 && negated == 0 && great.CurrentAtk == 0,
                    $"summoned={summoned} before={before} negated={negated} after={great.CurrentAtk}");
            }

            {
                // Special Summoned (Monster Reborn): no Tribute Summon, its ATK stays 0.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var great = e.CreateCardInstance(GreatMajuGarzett);
                p.Graveyard.Add(great);
                var reborn = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                Drive(e, c => c == great);
                FieldSpellEffects.RefreshBoard(e);
                Check("Great Maju Garzett: Special Summoned by Monster Reborn, its ATK is its original 0",
                    act && OnField(p, great) && great.CurrentAtk == 0 && BattleMechanics.AttackValue(great) == 0,
                    $"act={act} onField={OnField(p, great)} atk={great.CurrentAtk}");
            }

            {
                // Maju Garzett ("?" ATK): Celtic 1400 + Giant Soldier 1300 = 2700; the AI's Blue-Eyes (3000) attacks it:
                // Maju is destroyed and the player takes 300.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Attack, true);
                var maju = PutInHand(e, p, MajuGarzett);
                e.PendingTributes.Add(celtic);
                e.PendingTributes.Add(soldier);
                var summoned = e.TryNormalSummon(p, maju, false);
                Drive(e, _ => true);
                FieldSpellEffects.RefreshBoard(e);
                var atk = maju.CurrentAtk;
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var lp = p.LifePoints;
                var midAtk = maju.CurrentAtk;
                var attacked = reached && e.TryAttack(o, be, maju);
                DrainCombat(e);
                Check("Maju Garzett: Tributes 1400 + 1300 → ATK 2700 (printed ?), used in battle (3000 vs 2700: 300 damage)",
                    summoned && atk == 2700 && midAtk == 2700 && attacked && p.Graveyard.Contains(maju) &&
                    lp - p.LifePoints == 300 && OnField(o, be),
                    $"summoned={summoned} atk={atk} mid={midAtk} reached={reached} attacked={attacked} " +
                    $"lp {lp}->{p.LifePoints}");
            }

            {
                // Tribute Set, then Flip Summoned: not a Tribute Summon — the "?" ATK is 0.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Attack, true);
                var maju = PutInHand(e, p, MajuGarzett);
                e.PendingTributes.Add(celtic);
                e.PendingTributes.Add(soldier);
                var set = e.TryNormalSummon(p, maju, true);
                Drive(e, _ => true);
                var reached = ReachPlayerNextMain(e);
                var flipped = reached && e.TryFlipSummon(p, maju);
                Drive(e, _ => true);
                FieldSpellEffects.RefreshBoard(e);
                Check("Maju Garzett: Tribute Set then Flip Summoned → ATK stays 0 (the ATK is only set by a Tribute Summon)",
                    set && reached && flipped && maju.FaceUp && maju.CurrentAtk == 0 &&
                    BattleMechanics.AttackValue(maju) == 0,
                    $"set={set} reached={reached} flipped={flipped} atk={maju.CurrentAtk}");
            }
        }

        // ───────────────────────────── Guardian Tryce ─────────────────────────────

        static CardInstance PlaceTwinSwords(DuelEngine e, DuelistState who, CardInstance host)
        {
            var sw = e.CreateCardInstance(TwinSwords);
            sw.FaceUp = true;
            sw.SetThisTurn = false;
            sw.EquippedTo = host;
            host.Equips.Add(sw);
            who.SpellTrapZones[0].Occupant = sw;
            return sw;
        }

        static void TryceDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Tribute Summon Tryce with Celtic; the AI's Blue-Eyes destroys it by battle → Celtic returns.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, true);
                PlaceTwinSwords(e, p, soldier);
                var tryce = PutInHand(e, p, GuardianTryce);
                e.PendingTributes.Add(celtic);
                var summoned = e.TryNormalSummon(p, tryce, false);
                Drive(e, _ => true);
                var recorded = tryce.TributeMaterials != null && tryce.TributeMaterials.Any(t => t.Card == celtic);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var attacked = reached && e.TryAttack(o, be, tryce);
                DrainCombat(e);
                Drive(e, _ => true);
                Check("Guardian Tryce: destroyed by battle → the Celtic Guardian Tributed for it is Special Summoned",
                    summoned && recorded && attacked && p.Graveyard.Contains(tryce) && OnField(p, celtic) &&
                    celtic.FaceUp && !p.Graveyard.Contains(celtic),
                    $"summoned={summoned} recorded={recorded} reached={reached} attacked={attacked} " +
                    $"tryceGy={p.Graveyard.Contains(tryce)} celticOnField={OnField(p, celtic)}");
            }

            {
                // The Tribute left the GY (banished) before Tryce was destroyed: nothing is Special Summoned.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, true);
                PlaceTwinSwords(e, p, soldier);
                var tryce = PutInHand(e, p, GuardianTryce);
                e.PendingTributes.Add(celtic);
                var summoned = e.TryNormalSummon(p, tryce, false);
                Drive(e, _ => true);
                p.Graveyard.Remove(celtic);
                p.Banished.Add(celtic);
                FieldSpellEffects.RefreshBoard(e);
                p.Banished.Remove(celtic);
                p.Graveyard.Add(celtic); // back in the GY by another path: no longer "used for its Tribute Summon"
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var attacked = reached && e.TryAttack(o, be, tryce);
                DrainCombat(e);
                Drive(e, _ => true);
                Check("Guardian Tryce: its Tribute left the GY in between → nothing is Special Summoned",
                    summoned && attacked && p.Graveyard.Contains(tryce) && p.Graveyard.Contains(celtic) &&
                    !OnField(p, celtic),
                    $"summoned={summoned} reached={reached} attacked={attacked} celticGy={p.Graveyard.Contains(celtic)}");
            }

            {
                // Tryce Tributed for Great Maju Garzett (sent, not destroyed): no trigger; Great Maju's ATK = 2 x 1900.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, true);
                PlaceTwinSwords(e, p, soldier);
                var tryce = PutInHand(e, p, GuardianTryce);
                e.PendingTributes.Add(celtic);
                var summoned = e.TryNormalSummon(p, tryce, false);
                Drive(e, _ => true);
                p.NormalSummonUsed = false; // a second Normal Summon this turn (test only)
                var great = PutInHand(e, p, GreatMajuGarzett);
                e.PendingTributes.Add(tryce);
                var second = e.TryNormalSummon(p, great, false);
                Drive(e, _ => true);
                FieldSpellEffects.RefreshBoard(e);
                Check("Guardian Tryce: Tributed (not destroyed) → no revival; Great Maju Garzett's ATK becomes 3800",
                    summoned && second && p.Graveyard.Contains(tryce) && p.Graveyard.Contains(celtic) &&
                    !OnField(p, celtic) && great.CurrentAtk == 3800,
                    $"summoned={summoned} second={second} celticGy={p.Graveyard.Contains(celtic)} atk={great.CurrentAtk}");
            }
        }

        // ───────────────────────────── Chaos Necromancer / Gren Maju Da Eiza ─────────────────────────────

        static void CountStatDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's Chaos Necromancer: 5 Monster Cards (+1 Spell) in its GY → 1500; it destroys Celtic (1400).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                foreach (var id in new[] { Kuriboh, Kuriboh, Celtic, GiantSoldier, BlueEyes, PotOfGreed })
                    o.Graveyard.Add(e.CreateCardInstance(id));
                foreach (var id in new[] { Kuriboh, Kuriboh, Kuriboh })
                    p.Graveyard.Add(e.CreateCardInstance(id)); // the player's GY does not count
                var necro = PlaceMonster(e, o, ChaosNecromancer, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var atk = necro.CurrentAtk;
                var lp = p.LifePoints;
                var attacked = reached && e.TryAttack(o, necro, celtic);
                DrainCombat(e);
                Check("Chaos Necromancer: 5 Monster Cards in its own GY (Spell ignored) → 1500; destroys Celtic, 100 damage",
                    reached && atk == 1500 && attacked && p.Graveyard.Contains(celtic) && OnField(o, necro) &&
                    lp - p.LifePoints == 100,
                    $"reached={reached} atk={atk} attacked={attacked} lp {lp}->{p.LifePoints}");

                o.Graveyard.Add(e.CreateCardInstance(Kuriboh));
                FieldSpellEffects.RefreshBoard(e);
                var six = necro.CurrentAtk;
                necro.IsNegated = true;
                FieldSpellEffects.RefreshBoard(e);
                var negated = necro.CurrentAtk;
                Check("Chaos Necromancer: recomputed on refresh (6 → 1800); negated → its own 0",
                    six == 1800 && negated == 0, $"six={six} negated={negated}");
            }

            {
                // Gren Maju Da Eiza: 3 of your banished cards (one face-down) → 1200/1200; the opponent's do not count.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var gren = PutInHand(e, p, GrenMajuDaEiza);
                for (var i = 0; i < 3; i++)
                {
                    var b = e.CreateCardInstance(i == 2 ? PotOfGreed : Kuriboh);
                    b.FaceUp = i != 2;
                    p.Banished.Add(b);
                }
                for (var i = 0; i < 4; i++) o.Banished.Add(e.CreateCardInstance(Kuriboh));
                var summoned = e.TryNormalSummon(p, gren, false);
                Drive(e, _ => true);
                FieldSpellEffects.RefreshBoard(e);
                Check("Gren Maju Da Eiza: 3 of your banished cards → ATK/DEF 1200/1200 (printed ?/?); battle values match",
                    summoned && gren.CurrentAtk == 1200 && gren.CurrentDef == 1200 &&
                    BattleMechanics.AttackValue(gren) == 1200 && BattleMechanics.DefenseValue(gren) == 1200,
                    $"summoned={summoned} atk={gren.CurrentAtk} def={gren.CurrentDef}");

                foreach (var z in p.MonsterZones) if (z.Occupant == gren) z.Occupant = null;
                p.Hand.Add(gren);
                FieldSpellEffects.RefreshBoard(e);
                Check("Gren Maju Da Eiza: back in the hand, its ATK is the printed ? (0) again",
                    gren.CurrentAtk == 0 && gren.CurrentDef == 0, $"atk={gren.CurrentAtk} def={gren.CurrentDef}");
            }

            {
                // The AI's Blue-Eyes (3000) attacks the player's face-down Gren Maju with 8 banished cards (DEF 3200).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                for (var i = 0; i < 8; i++) p.Banished.Add(e.CreateCardInstance(Kuriboh));
                var gren = PlaceMonster(e, p, GrenMajuDaEiza, 0, BattlePosition.Defense, false);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var oLp = o.LifePoints;
                var attacked = reached && e.TryAttack(o, be, gren);
                DrainCombat(e);
                Check("Gren Maju Da Eiza: flipped by the attack, DEF 3200 holds against Blue-Eyes (3000): 200 to the attacker",
                    reached && attacked && OnField(p, gren) && gren.FaceUp && OnField(o, be) &&
                    oLp - o.LifePoints == 200,
                    $"reached={reached} attacked={attacked} grenOnField={OnField(p, gren)} def={gren.CurrentDef} " +
                    $"oLp {oLp}->{o.LifePoints}");
            }
        }
    }
}
