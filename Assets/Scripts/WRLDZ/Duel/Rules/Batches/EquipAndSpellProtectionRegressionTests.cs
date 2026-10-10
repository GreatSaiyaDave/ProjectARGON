using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// EquipAndSpellProtection batch (v161): Fusion Sword Murasame Blade, Heart of Clear Water, Smoke Grenade of the Thief,
    /// Blast with Chain, Woodland Sprite, Magic Reflector (<see cref="EquipAndSpellProtectionAtoms"/>,
    /// <see cref="EquipAndSpellProtectionRuntime"/>). Compile shapes, near misses that stay uncompiled, and mini-duels
    /// through TryActivateSpellTrap, MST / Heavy Storm / Dark Hole / Fissure / Tribute to the Doomed, battle and the
    /// Standby upkeep.
    /// </summary>
    public static class EquipAndSpellProtectionRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Murasame = 37684215;
        const int HeartOfClearWater = 64801562;
        const int SmokeGrenade = 63789924;
        const int BlastWithChain = 98239899;
        const int WoodlandSprite = 6979239;        // Plant 900/400
        const int MagicReflector = 61844784;

        const int Mst = 5318639;
        const int HeavyStorm = 19613556;
        const int DarkHole = 53129443;
        const int Fissure = 66788016;
        const int TributeToTheDoomed = 79759861;
        const int Disarmament = 20727787;
        const int AxeOfDespair = 40619825;          // +1000 ATK
        const int BlackPendant = 65169794;          // +500 ATK; sent from the field to the GY: 500 damage
        const int MessengerOfPeace = 44656491;
        const int CelticGuardian = 91152256;        // Warrior 1400
        const int GaiaTheFierceKnight = 6368038;    // Warrior 2300
        const int GiantSoldierOfStone = 13039848;   // Rock 1300
        const int MysticalElf = 15025844;           // Spellcaster 800/2000
        const int SakuretsuArmor = 56120475;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("EquipAndSpellProtection: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            MurasameDuels(db, pDeck, aDeck, Check);
            HeartDuels(db, pDeck, aDeck, Check);
            SmokeGrenadeDuels(db, pDeck, aDeck, Check);
            BlastWithChainDuels(db, pDeck, aDeck, Check);
            WoodlandSpriteDuels(db, pDeck, aDeck, Check);
            MagicReflectorDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            bool Impl(int id, out CompiledCardProgram prog, out string why)
            {
                var def = db.Get(id);
                prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                why = $"status={st} {Describe(prog)}";
                return prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented;
            }

            {
                var ok = Impl(Murasame, out var prog, out var why);
                var eq = prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault();
                var imm = prog?.ClausesFor(EffectTiming.ContinuousWhileFaceUp).FirstOrDefault();
                Check("Fusion Sword Murasame Blade: Warrior equip +800 ATK and its own immunity to card-effect destruction",
                    ok && prog.ClauseList.Count == 2 && eq != null && eq.Action == EffectActionKind.EquipThisToTarget &&
                    eq.RaceFilter == "Warrior" && eq.EquipAtkBonus == 800 && imm != null &&
                    imm.Action == EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped && !imm.MakesChainLink, why);
            }
            {
                var ok = Impl(HeartOfClearWater, out var prog, out var why);
                var eq = prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault();
                var cont = prog?.ClausesFor(EffectTiming.ContinuousWhileFaceUp) ?? new List<EffectClause>();
                Check("Heart of Clear Water: any face-up monster; self-destruction at 1300 ATK; battle / targeting protection",
                    ok && prog.ClauseList.Count == 3 && eq != null && eq.Action == EffectActionKind.EquipThisToTarget &&
                    eq.Zone == EffectZoneFilter.FieldAnyMonster && eq.EquipAtkBonus == 0 &&
                    cont.Exists(c => c.Action == EffectActionKind.DestroyThisWhileEquippedMonsterAtkAtLeast && c.Amount == 1300) &&
                    cont.Exists(c => c.Action == EffectActionKind.EquippedMonsterNotDestroyedByBattleOrTargetingEffects), why);
            }
            {
                var ok = Impl(SmokeGrenade, out var prog, out var why);
                var eq = prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault();
                var trg = prog?.ClausesFor(EffectTiming.SentFromFieldToGy).FirstOrDefault();
                Check("Smoke Grenade of the Thief: any face-up monster; mandatory non-targeting discard from the opponent's hand " +
                      "when destroyed by a card effect while equipped",
                    ok && prog.ClauseList.Count == 2 && eq != null && eq.Zone == EffectZoneFilter.FieldAnyMonster && trg != null &&
                    trg.Action == EffectActionKind.DiscardChosenFromHand && trg.Zone == EffectZoneFilter.OppHandCards &&
                    trg.ChoiceDoesNotTarget && !trg.IsOptional && trg.RequiresDestroyedByEffectWhileEquipped, why);
            }
            {
                var ok = Impl(BlastWithChain, out var prog, out var why);
                var eq = prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault();
                var trg = prog?.ClausesFor(EffectTiming.SentFromFieldToGy).FirstOrDefault();
                Check("Blast with Chain: Trap equip on your face-up monster (+500, own-controller limit); targeted destroy when " +
                      "destroyed by a card effect while equipped",
                    ok && prog.ClauseList.Count == 2 && eq != null && eq.Action == EffectActionKind.EquipThisToTarget &&
                    eq.Zone == EffectZoneFilter.ControllerMonsters && eq.RequiresTargetFaceUp && eq.EquipAtkBonus == 500 &&
                    eq.EquipLimitSameController && eq.StaysOnField && trg != null && trg.Action == EffectActionKind.Destroy &&
                    trg.Zone == EffectZoneFilter.AnyCardOnField && trg.IsPsctTarget && !trg.IsOptional &&
                    trg.RequiresDestroyedByEffectWhileEquipped, why);
            }
            {
                var ok = Impl(WoodlandSprite, out var prog, out var why);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Woodland Sprite: ignition 500 damage, cost any Equip Card equipped to it, no once per turn",
                    ok && c != null && c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.InflictDamageToOpponent &&
                    c.Amount == 500 && c.RequiresSendEquipSpellOnThisToGy && c.EquipCostAnyEquipCard && !c.OncePerTurn &&
                    !c.RequiresTargetChoice && EffectVocabulary.CostOf(c) == EffectCostKind.SendToGy, why);
            }
            {
                var ok = Impl(MagicReflector, out var prog, out var why);
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Magic Reflector: target another face-up Spell you control; place its destroy-replacement counter",
                    ok && c != null && c.Action == EffectActionKind.PlaceDestroyReplacementCounterOnTarget &&
                    c.Zone == EffectZoneFilter.ControllerFaceUpSpellsExceptThis && c.RequiresTargetChoice && c.IsPsctTarget,
                    why);
            }
            Check("ResolutionOf maps the new actions",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped }) == EffectResolutionKind.Protection &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.EquippedMonsterNotDestroyedByBattleOrTargetingEffects }) == EffectResolutionKind.Protection &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.DestroyThisWhileEquippedMonsterAtkAtLeast }) == EffectResolutionKind.Destroy &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.PlaceDestroyReplacementCounterOnTarget }) == EffectResolutionKind.PlaceCounters);
        }

        static CompiledCardProgram CompileText(string text, string type, string race) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990161, name = "Near-miss Card", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = race, desc = text
            });

        static void NearMisses(Chk Check)
        {
            var p = CompileText("Equip only to a Warrior-Type monster. It gains 800 ATK. While equipped to a monster, this " +
                                "card cannot be destroyed by battle.", "Spell Card", "Equip");
            Check("near miss: \"cannot be destroyed by battle\" is not Murasame's immunity",
                p != null && !p.FullyCompiled &&
                !p.ClauseList.Exists(c => c?.Action == EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped),
                Describe(p));
            p = CompileText("If the equipped monster's ATK is 1300 or higher, this card is destroyed. The monster equipped " +
                            "with this card is not destroyed as a result of battle or by the effect of a card.", "Spell Card",
                "Equip");
            Check("near miss: Heart of Clear Water without \"that targets the monster\" stays uncompiled",
                p != null && !p.FullyCompiled, Describe(p));
            p = CompileText("When this card is destroyed by a card effect while equipped to a monster: Look at your " +
                            "opponent's hand.", "Spell Card", "Equip");
            Check("near miss: Smoke Grenade without its discard stays uncompiled", p != null && !p.FullyCompiled, Describe(p));
            p = CompileText("Target 1 face-up monster you control; equip this card to that target. It gains 500 ATK. If this " +
                            "card is destroyed by a card effect while equipped: Target 1 card on the field; destroy that " +
                            "target, and if you do, inflict 500 damage to your opponent.", "Trap Card", "Normal");
            Check("near miss: Blast with Chain with an extra rider stays uncompiled", p != null && !p.FullyCompiled, Describe(p));
            p = CompileText("Send 1 Equip Spell Card equipped to this card to the Graveyard. Inflict 500 points of damage to " +
                            "your opponent's Life Points.", "Effect Monster", "Plant");
            Check("near miss: an \"Equip Spell Card\" cost is not Woodland Sprite's any-Equip-Card cost",
                p == null || !p.ClauseList.Exists(c => c != null && c.EquipCostAnyEquipCard), Describe(p));
            p = CompileText("Target 1 face-up Spell Card you control; place 1 counter on it.", "Spell Card", "Normal");
            Check("near miss: Magic Reflector without its replacement stays uncompiled", p != null && !p.FullyCompiled,
                Describe(p));
        }

        // ───────────────────────────── Fusion Sword Murasame Blade ─────────────────────────────

        static void MurasameDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
            var equipped = Equip(e, p, celtic, Murasame, out var blade);
            Check("Murasame: equipped to a Warrior, 1400 → 2200 ATK", equipped && celtic.CurrentAtk == 2200,
                $"equipped={equipped} atk={celtic.CurrentAtk} {State(e)}");

            var mst = PutInHand(e, p, Mst);
            var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
            var ok = act && Drive(e, blade);
            Check("Murasame: MST targets it and resolves, but it is not destroyed (still equipped, 2200 ATK)",
                ok && Where(e, blade) == "P.stz" && blade.EquippedTo == celtic && celtic.CurrentAtk == 2200 &&
                Where(e, mst) == "P.gy", $"act={act} ok={ok} blade={Where(e, blade)} mst={Where(e, mst)} {State(e)}");

            var storm = PutInHand(e, p, HeavyStorm);
            var oSet = PlaceSetTrap(e, o, Mst, 0);
            act = e.TryActivateSpellTrap(p, storm, fromHand: true);
            Settle(e);
            Check("Murasame: Heavy Storm destroys the other Spells/Traps, not Murasame",
                act && Where(e, blade) == "P.stz" && blade.EquippedTo == celtic && Where(e, oSet) == "O.gy",
                $"act={act} blade={Where(e, blade)} oSet={Where(e, oSet)} {State(e)}");

            var hole = PutInHand(e, p, DarkHole);
            act = e.TryActivateSpellTrap(p, hole, fromHand: true);
            Settle(e);
            Check("Murasame: its monster destroyed (Dark Hole) — it goes to the GY by rule",
                act && Where(e, celtic) == "P.gy" && Where(e, blade) == "P.gy" && Where(e, gaia) == "O.gy",
                $"act={act} celtic={Where(e, celtic)} blade={Where(e, blade)} {State(e)}");
        }

        // ───────────────────────────── Heart of Clear Water ─────────────────────────────

        static void HeartDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Battle: Gaia (2300) attacks the Heart-equipped Mystical Elf (800, Attack Position).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, elf, HeartOfClearWater, out var heart);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var lp = p.LifePoints;
                var attacked = reached && e.TryAttack(o, gaia, elf);
                DrainCombat(e);
                Check("Heart: the equipped monster is not destroyed by battle; the battle damage applies",
                    equipped && attacked && Where(e, elf) == "P.mz" && Where(e, heart) == "P.stz" &&
                    p.LifePoints == lp - 1500,
                    $"equipped={equipped} reached={reached} attacked={attacked} elf={Where(e, elf)} lp={lp}->{p.LifePoints} {State(e)}");
            }

            // Effects: Heart equipped to the opponent's monster; a targeting destroy fails, a non-targeting one works.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                var heart = PutInHand(e, p, HeartOfClearWater);
                var act = e.TryActivateSpellTrap(p, heart, fromHand: true);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                var ok = act && legal.Contains(elf) && legal.Contains(mine) && Drive(e, elf);
                Check("Heart: may be equipped to a face-up monster on either field (the opponent's Elf)",
                    ok && heart.EquippedTo == elf && Where(e, heart) == "P.stz",
                    $"act={act} legal={Names(legal)} ok={ok} host={heart.EquippedTo?.Name} {State(e)}");

                var doomed = PutInHand(e, p, TributeToTheDoomed);
                var fodder = PutInHand(e, p, GiantSoldierOfStone);
                act = e.TryActivateSpellTrap(p, doomed, fromHand: true);
                var picks = 0;
                for (var i = 0; i < 20; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var lt = e.PendingActivation.LegalTargets;
                        var pick = lt.Contains(fodder) ? fodder : lt.Contains(elf) ? elf : lt.FirstOrDefault();
                        if (!e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                        picks++;
                        continue;
                    }

                    if (e.IsAwaitingResponse)
                    {
                        e.PassResponse();
                        continue;
                    }

                    break;
                }

                Check("Heart: Tribute to the Doomed targets the equipped Elf and resolves — it is not destroyed",
                    act && picks > 0 && Where(e, doomed) == "P.gy" && Where(e, fodder) == "P.gy" && Where(e, elf) == "O.mz" &&
                    Where(e, heart) == "P.stz", $"act={act} picks={picks} elf={Where(e, elf)} doomed={Where(e, doomed)} {State(e)}");

                var fissure = PutInHand(e, p, Fissure);
                act = e.TryActivateSpellTrap(p, fissure, fromHand: true) && Drive(e, elf);
                Check("Heart: Fissure does not target — the equipped Elf is destroyed; Heart goes to the GY",
                    act && Where(e, elf) == "O.gy" && Where(e, heart) == "P.gy",
                    $"act={act} elf={Where(e, elf)} heart={Where(e, heart)} {State(e)}");
            }

            // An implied target (review v161-lod-b): Sakuretsu Armor "Target the attacking monster; destroy that target"
            // picks no card, yet the attacker is its target (Lua CHAININFO_TARGET_CARDS) — Heart keeps it on the field.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var wall = PlaceMonster(e, p, CelticGuardian, 2, BattlePosition.Defense, true);
                var sak = PlaceSetTrap(e, p, SakuretsuArmor, 2);
                var elf = PlaceMonster(e, o, MysticalElf, 2, BattlePosition.Attack, true);
                elf.ClearAttackFlags();
                elf.SummonedThisTurn = false;
                var heart = e.CreateCardInstance(HeartOfClearWater);
                heart.FaceUp = true;
                heart.SetThisTurn = false;
                o.SpellTrapZones[0].Occupant = heart;
                heart.EquippedTo = elf;
                elf.Equips.Add(heart);
                EquipFamilyRuntime.NoteEquipped(e, heart, elf);
                if (e.Phase == DuelPhase.Main1) e.TryEnterBattlePhase(o);
                DrainCombat(e);
                var attacked = reached && e.TryAttack(o, elf, wall);
                var act = e.CanActivateSpellTrap(p, sak, fromHand: false) && e.TryActivateSpellTrap(p, sak, fromHand: false);
                DrainCombat(e);
                Check("Heart: Sakuretsu Armor targets the attacking monster — the Heart-equipped attacker is not destroyed",
                    attacked && act && Where(e, elf) == "O.mz" && Where(e, heart) == "O.stz" && Where(e, sak) == "P.gy",
                    $"reached={reached} attacked={attacked} act={act} elf={Where(e, elf)} heart={Where(e, heart)} sak={Where(e, sak)} {State(e)}");
            }

            // Self-destruction: equipped to a 1300 ATK monster, or the monster reaches 1300 ATK.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var stone = PlaceMonster(e, p, GiantSoldierOfStone, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
                var heart1 = PutInHand(e, p, HeartOfClearWater);
                var act = e.TryActivateSpellTrap(p, heart1, fromHand: true);
                var ok = act && Drive(e, stone);
                Check("Heart: equipped to a 1300 ATK monster — it is destroyed at once",
                    ok && Where(e, heart1) == "P.gy" && Where(e, stone) == "P.mz",
                    $"act={act} ok={ok} heart={Where(e, heart1)} {State(e)}");

                var on = Equip(e, p, elf, HeartOfClearWater, out var heart2);
                var stays = on && Where(e, heart2) == "P.stz";
                var axeOn = Equip(e, p, elf, AxeOfDespair, out _);
                Check("Heart: stays on an 800 ATK monster; Axe of Despair (+1000) brings it to 1800 — Heart is destroyed",
                    stays && axeOn && elf.CurrentAtk == 1800 && Where(e, heart2) == "P.gy",
                    $"stays={stays} axe={axeOn} atk={elf.CurrentAtk} heart={Where(e, heart2)} {State(e)}");
            }
        }

        // ───────────────────────────── Smoke Grenade of the Thief ─────────────────────────────

        static void SmokeGrenadeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, SmokeGrenade, out var smoke);
                var oGaia = PutInHand(e, o, GaiaTheFierceKnight);
                var oMst = PutInHand(e, o, Mst);
                var mst = PutInHand(e, p, Mst);
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(smoke);
                Settle(e);
                var pa = e.PendingActivation;
                var prompt = pa != null && pa.TargetKind == EffectTargetKind.CardInOppHand &&
                             pa.LegalTargets.Contains(oGaia) && pa.LegalTargets.Contains(oMst);
                var picked = prompt && e.TrySelectEffectTarget(oGaia);
                Settle(e);
                Check("Smoke Grenade: destroyed by MST while equipped — the player looks at the opponent's hand and " +
                      "discards the chosen card",
                    equipped && act && prompt && picked && Where(e, smoke) == "P.gy" && Where(e, oGaia) == "O.gy" &&
                    Where(e, oMst) == "O.hand" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"equipped={equipped} act={act} prompt={prompt} picked={picked} smoke={Where(e, smoke)} gaia={Where(e, oGaia)} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var equipped = Equip(e, p, celtic, SmokeGrenade, out var smoke);
                var oGaia = PutInHand(e, o, GaiaTheFierceKnight);
                var oMst = PutInHand(e, o, Mst);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                Check("Smoke Grenade: its monster destroyed (Dark Hole) — sent to the GY by rule, no discard",
                    equipped && act && Where(e, smoke) == "P.gy" && Where(e, oGaia) == "O.hand" && Where(e, oMst) == "O.hand" &&
                    !e.IsAwaitingEffectTarget, $"act={act} smoke={Where(e, smoke)} {State(e)}");
            }

            // The AI's Smoke Grenade: equipped on its turn, destroyed by the player's MST on the next turn.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentMain(e);
                var smoke = PutInHand(e, o, SmokeGrenade);
                var act = reached && e.TryActivateSpellTrap(o, smoke, fromHand: true);
                Settle(e);
                var aiEquipped = smoke.EquippedTo == foe;
                for (var i = 0; i < 6 && !(e.TurnPlayer == p && e.InMainPhase); i++)
                {
                    Settle(e);
                    e.TryEndTurnSafe(e.TurnPlayer);
                }

                p.Hand.Clear();
                var pGaia = PutInHand(e, p, GaiaTheFierceKnight);
                var pElf = PutInHand(e, p, MysticalElf);
                var mst = PutInHand(e, p, Mst);
                var act2 = e.TurnPlayer == p && e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(smoke);
                Settle(e);
                Check("Smoke Grenade (AI): the AI equips its own monster; destroyed by the player's MST, it discards the " +
                      "player's strongest monster without a prompt",
                    act && aiEquipped && act2 && Where(e, smoke) == "O.gy" && Where(e, pGaia) == "P.gy" &&
                    Where(e, pElf) == "P.hand" && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} host={smoke.EquippedTo?.Name} act2={act2} smoke={Where(e, smoke)} gaia={Where(e, pGaia)} {State(e)}");
            }
        }

        // ───────────────────────────── Blast with Chain ─────────────────────────────

        static void BlastWithChainDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, true);
                var blast = PlaceSetTrap(e, p, BlastWithChain, 0);
                var act = e.TryActivateSpellTrap(p, blast, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                Settle(e);
                Check("Blast with Chain: equips itself to the player's face-up monster (+500 ATK) and stays as an Equip Card",
                    act && Where(e, blast) == "P.stz" && blast.FaceUp && blast.EquippedTo == celtic &&
                    celtic.CurrentAtk == 1900 && EquipModelRuntime.IsEquipCard(e, blast),
                    $"act={act} blast={Where(e, blast)} host={blast.EquippedTo?.Name} atk={celtic.CurrentAtk} {State(e)}");

                var mst = PutInHand(e, p, Mst);
                act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(blast);
                Settle(e);
                var pa = e.PendingActivation;
                var prompt = pa != null && pa.LegalTargets.Contains(gaia) && pa.LegalTargets.Contains(elf) &&
                             pa.LegalTargets.Contains(celtic);
                var picked = prompt && e.TrySelectEffectTarget(gaia);
                Settle(e);
                Check("Blast with Chain: destroyed by MST while equipped — the player targets 1 card on the field; it is destroyed",
                    act && prompt && picked && Where(e, blast) == "P.gy" && Where(e, gaia) == "O.gy" &&
                    Where(e, elf) == "O.mz" && celtic.CurrentAtk == 1400 && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"act={act} prompt={prompt} picked={picked} blast={Where(e, blast)} gaia={Where(e, gaia)} {State(e)}");
            }

            // Disarmament ("Destroy all Equip Cards on the field") sees the Equip Trap; destroyed by it, Blast triggers.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var blast = PlaceSetTrap(e, p, BlastWithChain, 0);
                var act = e.TryActivateSpellTrap(p, blast, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                Settle(e);
                var disarm = PlaceSetTrap(e, p, Disarmament, 1);
                var act2 = e.TryActivateSpellTrap(p, disarm, fromHand: false) && Drive(e, gaia);
                Check("Blast with Chain: Disarmament destroys it as an Equip Card; its trigger destroys the target",
                    act && act2 && Where(e, blast) == "P.gy" && Where(e, gaia) == "O.gy" && Where(e, celtic) == "P.mz",
                    $"act={act} act2={act2} blast={Where(e, blast)} gaia={Where(e, gaia)} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var oSet = PlaceSetTrap(e, o, Mst, 0);
                var blast = PlaceSetTrap(e, p, BlastWithChain, 0);
                var act = e.TryActivateSpellTrap(p, blast, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                Settle(e);
                var hole = PutInHand(e, p, DarkHole);
                var act2 = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                Check("Blast with Chain: its monster destroyed (Dark Hole) — sent to the GY by rule, no destroy trigger",
                    act && blast.EquippedTo == null && act2 && Where(e, blast) == "P.gy" && Where(e, oSet) == "O.stz" &&
                    !e.IsAwaitingEffectTarget, $"act={act} act2={act2} blast={Where(e, blast)} oSet={Where(e, oSet)} {State(e)}");
            }

            // The AI's Blast with Chain: its target leaves while the player's response window is open (the player holds a
            // Set MST); at resolution it equips nothing and is sent to the GY.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentMain(e);
                var blast = PlaceSetTrap(e, o, BlastWithChain, 0);
                PlaceSetTrap(e, p, Mst, 0);
                var act = reached && e.TryActivateSpellTrap(o, blast, fromHand: false);
                var window = e.IsAwaitingResponse && e.Chain.HasLinks;
                if (window) e.SendCardToGrave(o, foe);
                Settle(e);
                Check("Blast with Chain: its target is gone when it resolves — it equips nothing and is sent to the GY",
                    act && window && Where(e, blast) == "O.gy" && blast.EquippedTo == null && !e.Chain.HasLinks,
                    $"reached={reached} act={act} window={window} blast={Where(e, blast)} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, false);
                var blast = PlaceSetTrap(e, p, BlastWithChain, 0);
                var can = e.CanActivateSpellTrap(p, blast, fromHand: false);
                Check("Blast with Chain: no face-up monster you control — it cannot be activated", !can, State(e));
            }
        }

        // ───────────────────────────── Woodland Sprite ─────────────────────────────

        static void WoodlandSpriteDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var sprite = PlaceMonster(e, p, WoodlandSprite, 0, BattlePosition.Attack, true);
                var can = e.CanActivateSpellTrap(p, sprite, fromHand: false);
                Check("Woodland Sprite: no Equip Card on it — its effect cannot be activated", !can, State(e));

                var axeOn = Equip(e, p, sprite, AxeOfDespair, out var axe);
                var pendantOn = Equip(e, p, sprite, BlackPendant, out var pendant);
                var lp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, sprite, fromHand: false);
                var pa = e.PendingActivation;
                var costPick = pa != null && pa.TargetKind == EffectTargetKind.SendEquipSpellOnThisToGy &&
                               pa.LegalTargets.Contains(axe) && pa.LegalTargets.Contains(pendant) &&
                               (pa.Prompt ?? "").Contains("Equip Card");
                var paid = costPick && e.TrySelectEffectTarget(axe);
                Settle(e);
                Check("Woodland Sprite: two Equip Cards — the player picks the Axe as the cost; 500 damage",
                    axeOn && pendantOn && act && costPick && paid && Where(e, axe) == "P.gy" && Where(e, pendant) == "P.stz" &&
                    o.LifePoints == lp - 500, $"act={act} costPick={costPick} paid={paid} lp={lp}->{o.LifePoints} {State(e)}");

                lp = o.LifePoints;
                var again = e.TryActivateSpellTrap(p, sprite, fromHand: false);
                Settle(e);
                Check("Woodland Sprite: no once per turn — a lone Black Pendant is sent without a prompt; 500 + its own 500",
                    again && Where(e, pendant) == "P.gy" && o.LifePoints == lp - 1000 && !e.IsAwaitingEffectTarget,
                    $"again={again} lp={lp}->{o.LifePoints} {State(e)}");
            }

            // An Equip Card the opponent controls counts: the player's Heart of Clear Water on the AI's Woodland Sprite.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var sprite = PlaceMonster(e, o, WoodlandSprite, 0, BattlePosition.Attack, true);
                var heart = PutInHand(e, p, HeartOfClearWater);
                var act = e.TryActivateSpellTrap(p, heart, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(sprite);
                Settle(e);
                var on = heart.EquippedTo == sprite;
                var reached = ReachOpponentMain(e);
                var lp = p.LifePoints;
                var can = reached && e.CanActivateSpellTrap(o, sprite, fromHand: false);
                var fired = can && e.TryActivateSpellTrap(o, sprite, fromHand: false);
                Settle(e);
                Check("Woodland Sprite: the player's Heart of Clear Water equipped to the AI's Sprite pays its cost; the " +
                      "player takes 500",
                    act && on && can && fired && Where(e, heart) == "P.gy" && p.LifePoints == lp - 500,
                    $"act={act} on={on} reached={reached} can={can} fired={fired} heart={Where(e, heart)} lp={lp}->{p.LifePoints} {State(e)}");
            }
        }

        // ───────────────────────────── Magic Reflector ─────────────────────────────

        static void MagicReflectorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var reflector = PutInHand(e, p, MagicReflector);
                Check("Magic Reflector: no other face-up Spell you control — it cannot be activated",
                    !e.CanActivateSpellTrap(p, reflector, fromHand: true), State(e));
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var oAxeHost = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Attack, true);
                var axeOn = Equip(e, p, celtic, AxeOfDespair, out var axe);
                var reflector = PutInHand(e, p, MagicReflector);
                var act = e.TryActivateSpellTrap(p, reflector, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(axe);
                Settle(e);
                var counters = EquipAndSpellProtectionRuntime.ReflectorCounters(e, axe);
                Check("Magic Reflector: 1 counter on the face-up Axe of Despair; Magic Reflector goes to the GY",
                    axeOn && act && counters == 1 && Where(e, reflector) == "P.gy",
                    $"axe={axeOn} act={act} counters={counters} reflector={Where(e, reflector)} {State(e)}");

                var mst1 = PutInHand(e, p, Mst);
                act = e.TryActivateSpellTrap(p, mst1, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(axe);
                Settle(e);
                Check("Magic Reflector: MST would destroy the Axe — the counter is removed instead",
                    act && Where(e, axe) == "P.stz" && axe.EquippedTo == celtic && celtic.CurrentAtk == 2400 &&
                    EquipAndSpellProtectionRuntime.ReflectorCounters(e, axe) == 0,
                    $"act={act} axe={Where(e, axe)} atk={celtic.CurrentAtk} {State(e)}");

                var mst2 = PutInHand(e, p, Mst);
                act = e.TryActivateSpellTrap(p, mst2, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(axe);
                Settle(e);
                Check("Magic Reflector: with no counter left, the next MST destroys the Axe",
                    act && Where(e, axe) != "P.stz" && celtic.CurrentAtk == 1400,
                    $"act={act} axe={Where(e, axe)} {State(e)} host={oAxeHost.Name}");
            }

            // Rule: the Equip Spell's monster leaves the field — the counter does not keep it.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, CelticGuardian, 0, BattlePosition.Attack, true);
                Equip(e, p, celtic, AxeOfDespair, out var axe);
                var reflector = PutInHand(e, p, MagicReflector);
                e.TryActivateSpellTrap(p, reflector, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(axe);
                Settle(e);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                Check("Magic Reflector: the Axe's monster destroyed — the Axe goes to the GY by rule despite the counter",
                    act && Where(e, celtic) == "P.gy" && Where(e, axe) != "P.stz", $"axe={Where(e, axe)} {State(e)}");
            }

            // A "pay or destroy this card" upkeep: Messenger of Peace with too few LP keeps itself with the counter.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var messenger = PutInHand(e, p, MessengerOfPeace);
                var actM = e.TryActivateSpellTrap(p, messenger, fromHand: true);
                Settle(e);
                var reflector = PutInHand(e, p, MagicReflector);
                var actR = e.TryActivateSpellTrap(p, reflector, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(messenger);
                Settle(e);
                var counters = EquipAndSpellProtectionRuntime.ReflectorCounters(e, messenger);
                p.LifePoints = 50;
                for (var i = 0; i < 8 && !(e.TurnPlayer == p && e.InMainPhase && e.TurnNumber >= 3); i++)
                {
                    Settle(e);
                    if (e.TurnPlayer == p && e.TurnNumber >= 3) e.ContinueAfterStandby();
                    else e.TryEndTurnSafe(e.TurnPlayer);
                }

                Check("Magic Reflector: Messenger of Peace's upkeep cannot be paid (50 LP) — the counter is removed instead",
                    actM && actR && counters == 1 && e.TurnPlayer == p && e.TurnNumber >= 3 &&
                    Where(e, messenger) == "P.stz" && EquipAndSpellProtectionRuntime.ReflectorCounters(e, messenger) == 0,
                    $"actM={actM} actR={actR} counters={counters} turn={e.TurnNumber} messenger={Where(e, messenger)} {State(e)}");
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

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

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
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/tgt={c.RequiresTargetChoice}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
