using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// EquipLocksAndLeaveField batch: Demotion, Big Bang Shot, Autonomous Action Unit, Raregold Armor and Metalsilver
    /// Armor. Compile shapes through the real compiler, mini-duels through the engine entry points
    /// (TryActivateSpellTrap, TrySelectEffectTarget, TryAttack, ReturnCardToHand, SendCardToGrave) and near-miss texts
    /// that must stay not FullyCompiled.
    /// </summary>
    public static class EquipLocksAndLeaveFieldRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Demotion = 72575145;             // Equip Spell
        const int BigBangShot = 61127349;          // Equip Spell
        const int AutonomousActionUnit = 71453557; // Equip Spell
        const int RaregoldArmor = 7625614;         // Equip Spell
        const int MetalsilverArmor = 33114323;     // Equip Spell
        const int BookOfMoon = 14087893;           // Quick-Play Spell: target 1 face-up monster
        const int MonsterReborn = 83764718;        // Normal Spell: target 1 monster in either GY
        const int CelticGuardian = 91152256;       // Normal Lv4 1400/1200
        const int MysticalElf = 15025844;          // Normal Lv4 800/2000
        const int PsychicKappa = 7892180;          // Normal Lv2 400/1000

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("EquipLocksAndLeaveField: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            DemotionDuels(db, pDeck, aDeck, Check);
            BigBangShotDuels(db, pDeck, aDeck, Check);
            AutonomousActionUnitDuels(db, pDeck, aDeck, Check);
            RaregoldDuels(db, pDeck, aDeck, Check);
            MetalsilverDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

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

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
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
        }

        /// <summary>Activate an Equip Spell from the hand and pick <paramref name="target"/> (or let a lone target resolve).</summary>
        static bool Equip(DuelEngine e, DuelistState who, CardInstance spell, CardInstance target)
        {
            var act = e.TryActivateSpellTrap(who, spell, fromHand: true);
            Drive(e, c => ReferenceEquals(c, target));
            FieldSpellEffects.RefreshBoard(e);
            return act && ReferenceEquals(spell.EquippedTo, target);
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static bool IsAnyMonsterEquip(EffectClause c) =>
            c != null && c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.EquipThisToTarget &&
            c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetChoice && c.IsPsctTarget && c.StaysOnField;

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var dem = Prog(db, Demotion);
            Check("Demotion FullyCompiled: any-monster equip + ContinuousReduceLevel 2 on the equipped monster",
                dem != null && dem.FullyCompiled && Implemented(db, Demotion) && dem.ClauseList.Count == 2 &&
                IsAnyMonsterEquip(dem.ClauseList[0]) &&
                dem.ClauseList[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                dem.ClauseList[1].Action == EffectActionKind.ContinuousReduceLevel &&
                dem.ClauseList[1].Amount == 2 && dem.ClauseList[1].AppliesToEquippedHost,
                Describe(dem));

            var bbs = Prog(db, BigBangShot);
            Check("Big Bang Shot FullyCompiled: +400 any-monster equip that banishes its monster on leaving + piercing",
                bbs != null && bbs.FullyCompiled && Implemented(db, BigBangShot) && bbs.ClauseList.Count == 2 &&
                IsAnyMonsterEquip(bbs.ClauseList[0]) && bbs.ClauseList[0].EquipAtkBonus == 400 &&
                bbs.ClauseList[0].BanishHostWhenThisLeaves && !bbs.ClauseList[0].DestroyHostWhenThisLeaves &&
                bbs.ClauseList[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                bbs.ClauseList[1].Action == EffectActionKind.PiercingBattleDamage,
                Describe(bbs));

            var aau = Prog(db, AutonomousActionUnit);
            var ac = aau?.ClauseList.Count == 1 ? aau.ClauseList[0] : null;
            Check("Autonomous Action Unit FullyCompiled: pay 1500, revive from the opponent's GY in Attack, destroy on leave",
                aau != null && aau.FullyCompiled && Implemented(db, AutonomousActionUnit) && ac != null &&
                ac.Timing == EffectTiming.Activate && ac.Action == EffectActionKind.SpecialSummonFromGy &&
                ac.Zone == EffectZoneFilter.OppGyMonsters && ac.RequiresTargetChoice && ac.IsPsctTarget &&
                ac.PayLpAmount == 1500 && ac.SummonInAttack && !ac.SummonInDefense && !ac.SummonToTargetsOwner &&
                ac.DestroyHostWhenThisLeaves && !ac.DestroyHostOnlyIfThisDestroyed && ac.StaysOnField,
                Describe(aau));

            var rg = Prog(db, RaregoldArmor);
            Check("Raregold Armor FullyCompiled: any-monster equip + OpponentCannotAttackOtherThanEquippedHost",
                rg != null && rg.FullyCompiled && Implemented(db, RaregoldArmor) && rg.ClauseList.Count == 2 &&
                IsAnyMonsterEquip(rg.ClauseList[0]) &&
                rg.ClauseList[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                rg.ClauseList[1].Action == EffectActionKind.OpponentCannotAttackOtherThanEquippedHost &&
                EffectVocabulary.ResolutionOf(rg.ClauseList[1]) == EffectResolutionKind.Protection,
                Describe(rg));

            var ms = Prog(db, MetalsilverArmor);
            Check("Metalsilver Armor FullyCompiled: any-monster equip + OpponentCannotTargetMonstersExceptEquippedHost",
                ms != null && ms.FullyCompiled && Implemented(db, MetalsilverArmor) && ms.ClauseList.Count == 2 &&
                IsAnyMonsterEquip(ms.ClauseList[0]) &&
                ms.ClauseList[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                ms.ClauseList[1].Action == EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost &&
                EffectVocabulary.ResolutionOf(ms.ClauseList[1]) == EffectResolutionKind.Protection,
                Describe(ms));
        }

        static void NearMisses(Chk Check)
        {
            var texts = new (string label, string desc)[]
            {
                ("Demotion + for each", "Downgrade the monster equipped with this card by 2 Levels for each Spell Card in " +
                                        "your Graveyard."),
                ("Big Bang Shot + extra banish", "The equipped monster gains 400 ATK. If the equipped monster attacks a " +
                                                 "Defense Position monster, inflict piercing battle damage to your opponent. " +
                                                 "When this card leaves the field, banish the equipped monster and all " +
                                                 "cards in your hand."),
                ("Autonomous Action Unit + burn", "Pay 1500 Life Points, then target 1 monster in your opponent's " +
                                                  "Graveyard; Special Summon that target to your side of the field in " +
                                                  "face-up Attack Position, and equip it with this card. When this card " +
                                                  "leaves the field, destroy the equipped monster and inflict 1000 damage " +
                                                  "to your opponent."),
                ("Raregold Armor + no direct attacks", "As long as you control the monster equipped with this card, your " +
                                                       "opponent cannot attack other monsters other than the equipped " +
                                                       "monster, and cannot attack directly."),
                ("Metalsilver Armor + Trap lock", "As long as you control the monster equipped with this card, your " +
                                                  "opponent cannot use the effect of a Spell, Trap, or Effect Monster " +
                                                  "that targets 1 monster, except the equipped monster, and cannot " +
                                                  "activate Trap Cards."),
            };
            var id = 990_750_01;
            foreach (var (label, desc) in texts)
            {
                var p = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id++, name = "Near Miss " + label, type = "Spell Card", race = "Equip", desc = desc,
                    atk = -1, def = -1
                });
                Check("near miss stays not FullyCompiled: " + label, p == null || !p.FullyCompiled, Describe(p));
            }
        }

        // ───────────────────────── Demotion ─────────────────────────

        static void DemotionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var dem = PutInHand(e, p, Demotion);
                var ok = Equip(e, p, dem, elf);
                var down = elf.Level;
                e.ReturnCardToHand(dem);
                FieldSpellEffects.RefreshBoard(e);
                Check("Demotion: the equipped Level 4 monster is Level 2; back to 4 once Demotion leaves",
                    ok && down == 2 && elf.Level == 4,
                    $"ok={ok} down={down} after={elf.Level}");
            }
            {
                // Level never drops below 1; another monster is not changed.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var kappa = PlaceMonster(e, p, PsychicKappa, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
                var dem = PutInHand(e, p, Demotion);
                var ok = Equip(e, p, dem, kappa);
                Check("Demotion: a Level 2 monster becomes Level 1 (not 0); an unequipped monster keeps its Level",
                    ok && kappa.Level == 1 && elf.Level == 4,
                    $"ok={ok} kappa={kappa.Level} elf={elf.Level}");
            }
            {
                // aux.AddEquipProcedure: a face-up monster on either field.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = PlaceMonster(e, e.Opponent, CelticGuardian, 0, BattlePosition.Attack, true);
                var dem = PutInHand(e, p, Demotion);
                var ok = Equip(e, p, dem, celtic);
                Check("Demotion: equips to the opponent's face-up monster too and downgrades it",
                    ok && celtic.Level == 2, $"ok={ok} level={celtic.Level}");
            }
        }

        // ───────────────────────── Big Bang Shot ─────────────────────────

        static void BigBangShotDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Opponent's turn: +400 ATK, piercing, then bounced → the equipped monster is banished.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                opp.Hand.Clear();
                var celtic = PlaceMonster(e, opp, CelticGuardian, 0, BattlePosition.Attack, true);
                var kappa = PlaceMonster(e, p, PsychicKappa, 0, BattlePosition.Defense, true);
                var bbs = PutInHand(e, opp, BigBangShot);
                var ok = Equip(e, opp, bbs, celtic);
                var atk = celtic.CurrentAtk;
                var lpBefore = p.LifePoints;
                var bp = e.TryEnterBattlePhase(opp);
                if (e.IsAwaitingResponse) e.PassResponse();
                var attacked = e.TryAttack(opp, celtic, kappa);
                DrainCombat(e);
                var pierced = lpBefore - p.LifePoints;
                Check("Big Bang Shot: +400 ATK and piercing (1800 ATK into 1000 DEF deals 800)",
                    reached && ok && atk == 1800 && bp && attacked && pierced == 800 && !OnField(p, kappa),
                    $"reached={reached} ok={ok} atk={atk} bp={bp} attacked={attacked} pierced={pierced}");

                var back = e.ReturnCardToHand(bbs);
                Check("Big Bang Shot: returned to the hand → the equipped monster is banished",
                    back && opp.Banished.Contains(celtic) && !OnField(opp, celtic),
                    $"back={back} banished={opp.Banished.Contains(celtic)} onField={OnField(opp, celtic)}");
            }
            {
                // Sent to the GY (destroyed) → the equipped monster is banished.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var bbs = PutInHand(e, p, BigBangShot);
                var ok = Equip(e, p, bbs, elf);
                e.SendCardToGrave(p, bbs);
                Check("Big Bang Shot: sent to the GY → the equipped monster is banished",
                    ok && p.Banished.Contains(elf) && !OnField(p, elf) && p.Graveyard.Contains(bbs),
                    $"ok={ok} banished={p.Banished.Contains(elf)} bbsInGy={p.Graveyard.Contains(bbs)}");
            }
            {
                // The monster leaves first: Big Bang Shot goes to the GY with no equip target, nothing is banished.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var bbs = PutInHand(e, p, BigBangShot);
                var ok = Equip(e, p, bbs, elf);
                e.SendCardToGrave(p, elf);
                Check("Big Bang Shot: its monster sent to the GY first stays in the GY (no banish)",
                    ok && p.Graveyard.Contains(elf) && !p.Banished.Contains(elf) && p.Graveyard.Contains(bbs),
                    $"ok={ok} elfGy={p.Graveyard.Contains(elf)} banished={p.Banished.Contains(elf)}");
            }
        }

        // ───────────────────────── Autonomous Action Unit ─────────────────────────

        static void AutonomousActionUnitDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = e.CreateCardInstance(CelticGuardian);
                opp.Graveyard.Add(celtic);
                var lp = p.LifePoints;
                var aau = PutInHand(e, p, AutonomousActionUnit);
                var act = e.TryActivateSpellTrap(p, aau, fromHand: true);
                Drive(e, c => ReferenceEquals(c, celtic));
                var summoned = OnField(p, celtic) && celtic.FaceUp && celtic.Position == BattlePosition.Attack;
                var linked = ReferenceEquals(aau.EquippedTo, celtic);
                Check("Autonomous Action Unit: pays 1500 LP, Special Summons the opponent's GY monster to your field " +
                      "in face-up Attack Position and equips it",
                    act && p.LifePoints == lp - 1500 && summoned && linked && !opp.Graveyard.Contains(celtic),
                    $"act={act} lp={lp}->{p.LifePoints} summoned={summoned} linked={linked}");

                // Leaves the field by a bounce (not destroyed): the equipped monster is still destroyed.
                var back = e.ReturnCardToHand(aau);
                Check("Autonomous Action Unit: returned to the hand → the equipped monster is destroyed (to its owner's GY)",
                    back && !OnField(p, celtic) && opp.Graveyard.Contains(celtic) && p.Hand.Contains(aau),
                    $"back={back} onField={OnField(p, celtic)} oppGy={opp.Graveyard.Contains(celtic)}");
            }
            {
                // Only your own GY has a monster: no legal target, refused, no LP paid.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Graveyard.Add(e.CreateCardInstance(CelticGuardian));
                var lp = p.LifePoints;
                var aau = PutInHand(e, p, AutonomousActionUnit);
                var can = e.CanActivateSpellTrap(p, aau, fromHand: true);
                var act = e.TryActivateSpellTrap(p, aau, fromHand: true);
                Drive(e, _ => false);
                Check("Autonomous Action Unit: refused with no monster in the opponent's GY",
                    !can && !act && p.LifePoints == lp && p.Hand.Contains(aau),
                    $"can={can} act={act} lp={p.LifePoints}");
            }
        }

        // ───────────────────────── Raregold Armor ─────────────────────────

        static void RaregoldDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var kappa = PlaceMonster(e, p, PsychicKappa, 1, BattlePosition.Attack, true);
                var setMon = PlaceMonster(e, p, CelticGuardian, 2, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, opp, CelticGuardian, 0, BattlePosition.Attack, true);
                var rg = PutInHand(e, p, RaregoldArmor);
                var ok = Equip(e, p, rg, elf);
                var gated = ContinuousProtections.CannotBeAttackTarget(e, kappa) &&
                            ContinuousProtections.CannotBeAttackTarget(e, setMon) &&
                            !ContinuousProtections.CannotBeAttackTarget(e, elf);
                var battle = ReachOpponentBattle(e);
                var can = e.CanAttack(opp, celtic);
                var other = e.TryAttack(opp, celtic, kappa);
                var host = !other && e.TryAttack(opp, celtic, elf);
                DrainCombat(e);
                Check("Raregold Armor: the opponent can attack only the equipped monster (not another or a Set one)",
                    ok && gated && battle && can && !other && host,
                    $"ok={ok} gated={gated} battle={battle} can={can} other={other} host={host}");
            }
            {
                // Equipped to the opponent's monster: "as long as you control" fails, nothing is locked.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var kappa = PlaceMonster(e, p, PsychicKappa, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, CelticGuardian, 0, BattlePosition.Attack, true);
                var oppElf = PlaceMonster(e, opp, MysticalElf, 1, BattlePosition.Attack, true);
                var rg = PutInHand(e, p, RaregoldArmor);
                var ok = Equip(e, p, rg, celtic);
                Check("Raregold Armor: equipped to an opponent's monster → no attack-target lock on either side",
                    ok && !ContinuousProtections.CannotBeAttackTarget(e, kappa) &&
                    !ContinuousProtections.CannotBeAttackTarget(e, oppElf),
                    $"ok={ok}");
            }
        }

        // ───────────────────────── Metalsilver Armor ─────────────────────────

        static void MetalsilverDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var kappa = PlaceMonster(e, p, PsychicKappa, 1, BattlePosition.Attack, true);
                var oppKappa = PlaceMonster(e, opp, PsychicKappa, 0, BattlePosition.Attack, true);
                var gyMon = e.CreateCardInstance(CelticGuardian);
                opp.Graveyard.Add(gyMon);
                var ms = PutInHand(e, p, MetalsilverArmor);
                var ok = Equip(e, p, ms, elf);

                // Both target mirrors and the resolution guard, for the opponent's card effects.
                var book = e.CreateCardInstance(BookOfMoon);
                var bookClause = CardTextEffectCompiler.Compile(book.Def)?.ClauseList
                    .FirstOrDefault(c => c != null && c.RequiresTargetChoice);
                var text = bookClause != null
                    ? TextEffectRuntime.CollectTargets(e, opp, bookClause, book)
                    : new List<CardInstance>();
                var legacy = SpellTrapEffects.CollectLegalTargets(e, opp, EffectTargetKind.AnyMonsterOnField, book);
                var legacyGy = SpellTrapEffects.CollectLegalTargets(e, opp, EffectTargetKind.MonsterInEitherGy, book);
                var mine = SpellTrapEffects.CollectLegalTargets(e, p, EffectTargetKind.AnyMonsterOnField, book);
                Check("Metalsilver Armor: the opponent's targeting lists hold only the equipped monster (text + legacy, GY too)",
                    ok && bookClause != null && text.Contains(elf) && !text.Contains(kappa) && !text.Contains(oppKappa) &&
                    legacy.Contains(elf) && !legacy.Contains(kappa) && !legacy.Contains(oppKappa) &&
                    !legacyGy.Contains(gyMon),
                    $"ok={ok} text=[{string.Join(",", text.Select(t => t.Name))}] " +
                    $"legacy=[{string.Join(",", legacy.Select(t => t.Name))}] gy={legacyGy.Count}");
                Check("Metalsilver Armor: its controller's own effects still target any monster",
                    mine.Contains(kappa) && mine.Contains(oppKappa) && mine.Contains(elf),
                    $"mine=[{string.Join(",", mine.Select(t => t.Name))}]");
                var oppSource = PlaceMonster(e, opp, CelticGuardian, 1, BattlePosition.Attack, true);
                Check("Metalsilver Armor: a source card's player decides the shield (opponent's monster effect lists)",
                    EquipLocksAndLeaveFieldRuntime.ShieldedFromSource(e, kappa, oppSource) &&
                    !EquipLocksAndLeaveFieldRuntime.ShieldedFromSource(e, elf, oppSource) &&
                    !EquipLocksAndLeaveFieldRuntime.ShieldedFromSource(e, oppKappa, elf) &&
                    // Not a resolution re-check: a target chosen before the lock applied stays legal.
                    !ContinuousProtections.TargetedEffectBlocked(e, kappa, oppSource),
                    "");
                opp.MonsterZones[1].Occupant = null;

                // Opponent's turn, real activations: Monster Reborn has no legal target; Book of Moon only hits the host.
                var reached = ReachOpponentMain(e);
                opp.Hand.Clear();
                var reborn = PutInHand(e, opp, MonsterReborn);
                var canReborn = e.CanActivateSpellTrap(opp, reborn, fromHand: true);
                var bom = PutInHand(e, opp, BookOfMoon);
                var act = e.TryActivateSpellTrap(opp, bom, fromHand: true);
                Drive(e, c => ReferenceEquals(c, kappa));
                Check("Metalsilver Armor: the opponent's Monster Reborn cannot activate; Book of Moon flips only the equipped monster",
                    reached && !canReborn && act && !elf.FaceUp && kappa.FaceUp && oppKappa.FaceUp,
                    $"reached={reached} canReborn={canReborn} act={act} elfUp={elf.FaceUp} kappaUp={kappa.FaceUp}");
            }
            {
                // Equipped to the opponent's monster: the condition fails, the opponent targets freely.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var opp = e.Opponent;
                var kappa = PlaceMonster(e, p, PsychicKappa, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, CelticGuardian, 0, BattlePosition.Attack, true);
                var ms = PutInHand(e, p, MetalsilverArmor);
                var ok = Equip(e, p, ms, celtic);
                var legacy = SpellTrapEffects.CollectLegalTargets(e, opp, EffectTargetKind.AnyMonsterOnField,
                    e.CreateCardInstance(BookOfMoon));
                Check("Metalsilver Armor: equipped to an opponent's monster → no targeting lock",
                    ok && legacy.Contains(kappa) && legacy.Contains(celtic),
                    $"ok={ok} legacy=[{string.Join(",", legacy.Select(t => t.Name))}]");
            }
        }
    }
}
