using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// NormalMonsterSpells batch: Delta Attacker, Thousand Energy, Triangle Power, The Law of the Normal, Order to Smash,
    /// Sword of the Soul-Eater, Opti-Camouflage Armor. Compile shapes through the real compiler, near-miss sentences, and
    /// mini-duels through the engine entry points (CanActivateSpellTrap, TryActivateSpellTrap, TrySelectEffectTarget,
    /// TryEnterBattlePhase + TryAttack, TryEndTurnSafe). Runtime: <see cref="NormalMonsterSpellsRuntime"/>.
    /// </summary>
    public static class NormalMonsterSpellsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int DeltaAttacker = 39719977;     // Normal Spell
        const int ThousandEnergy = 5703682;     // Normal Spell
        const int TrianglePower = 32298781;     // Normal Spell
        const int LawOfTheNormal = 66926224;    // Normal Spell
        const int OrderToSmash = 39019325;      // Normal Trap
        const int SwordOfSoulEater = 5371656;   // Equip Spell
        const int OptiCamouflage = 44762290;    // Equip Spell

        const int MokeyMokey = 27288416;        // Normal, Level 1, 300/100
        const int SkullServant = 32274490;      // Normal, Level 1, 300/200
        const int PetitDragon = 75356564;       // Normal, Level 2, 600/700
        const int Kagemusha = 15401633;         // Kagemusha of the Blue Flame: Normal, Level 2, 800/400
        const int BabyDragon = 88819587;        // Normal, Level 3, 1200/700
        const int Celtic = 91152256;            // Normal, Level 4, 1400/1200
        const int Kuriboh = 40640057;           // Effect, Level 1, 300/200
        const int ManEaterBug = 54652250;       // Flip Effect, Level 2
        const int PotOfGreed = 55144522;        // Normal Spell (a Set copy cannot answer a Chain)
        const int MirrorForce = 44095762;       // Normal Trap (attack only)
        const int Yami = 59197169;              // Field Spell
        const int MaskOfRestrict = 29549364;    // Continuous Trap: neither player can Tribute

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("NormalMonsterSpells: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            DeltaAttackerDuels(db, pDeck, aDeck, Check);
            ThousandEnergyDuels(db, pDeck, aDeck, Check);
            TrianglePowerDuels(db, pDeck, aDeck, Check);
            LawOfTheNormalDuels(db, pDeck, aDeck, Check);
            OrderToSmashDuels(db, pDeck, aDeck, Check);
            SwordDuels(db, pDeck, aDeck, Check);
            OptiCamouflageDuels(db, pDeck, aDeck, Check);
            AiDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3 (a Battle Phase is possible), board, hands and piles cleared.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
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

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt (else Cancel); pass windows.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want, List<List<CardInstance>> offers = null)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    offers?.Add(Legal(e));
                    var pick = Legal(e).FirstOrDefault(want);
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

        static CardInstance FaceUpSpell(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static CardInstance PlaceToken(DuelEngine e, DuelistState who, int zone, int level)
        {
            var t = e.CreateToken("Sheep Token", "Beast", "EARTH", level, 0, 0);
            t.Position = BattlePosition.Defense;
            t.FaceUp = true;
            who.MonsterZones[zone].Occupant = t;
            return t;
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.TryFindMonster(c, out var z)) return $"{tag}M{z}{(c.FaceUp ? "u" : "d")}";
                if (who.TryFindSpellTrap(c, out var s)) return $"{tag}S{s}";
                if (who.FieldSpellZone?.Occupant == c) return tag + "Field";
                if (who.Hand.Contains(c)) return tag + "Hand";
                if (who.Graveyard.Contains(c)) return tag + "GY";
            }

            return "gone";
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990000011, name = "NormalMonsterSpells probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : "spell", race = race, desc = text
            });

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : $"full={p.FullyCompiled} " + string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static EffectClause Only(CompiledCardProgram p) =>
            p != null && p.FullyCompiled && p.ClauseList.Count == 1 ? p.ClauseList[0] : null;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var delta = Only(Prog(db, DeltaAttacker));
            Check("Delta Attacker: one direct-attack grant gated on 3 face-up Normal Monsters of one name",
                delta != null && delta.Action == EffectActionKind.NormalsSharingNameAttackDirectlyThisTurn &&
                delta.ActivationCondition == NormalMonsterSpellsRuntime.NormalsSharingNameFaceUp &&
                delta.ConditionAmount == 3 && !delta.RequiresTargetChoice && Implemented(db, DeltaAttacker) &&
                EffectVocabulary.ResolutionOf(delta) == EffectResolutionKind.DirectAttack,
                Describe(Prog(db, DeltaAttacker)));

            foreach (var (id, label, level, amount) in new[]
                     { (ThousandEnergy, "Thousand Energy", 2, 1000), (TrianglePower, "Triangle Power", 1, 2000) })
            {
                var c = Only(Prog(db, id));
                Check($"{label}: original ATK/DEF +{amount} for Level {level} Normal Monsters, destroyed in the End Phase",
                    c != null && c.Action == EffectActionKind.RaiseOriginalAtkDefOfLevelNormalsDestroyThemAtEndPhase &&
                    c.Amount == amount && c.MonsterLevelMin == level && c.MonsterLevelMax == level &&
                    c.ActivationCondition == NormalMonsterSpellsRuntime.FaceUpNonTokenNormalOfLevel &&
                    Implemented(db, id),
                    Describe(Prog(db, id)));
            }

            var law = Only(Prog(db, LawOfTheNormal));
            Check("The Law of the Normal: discard both hands, destroy all but Level 2 or lower Normal Monsters, gated on 5",
                law != null && law.Action == EffectActionKind.BothDiscardHandsDestroyAllButLowLevelNormals &&
                law.MonsterLevelMax == 2 && law.ConditionAmount == 5 &&
                law.ActivationCondition == NormalMonsterSpellsRuntime.LowLevelNormalsAndCardsToDiscardAndDestroy &&
                Implemented(db, LawOfTheNormal),
                Describe(Prog(db, LawOfTheNormal)));

            var smash = Only(Prog(db, OrderToSmash));
            Check("Order to Smash: targets 1 Level 2 or lower Normal Monster you control; up to 2 opponent Spells/Traps",
                smash != null && smash.Action == EffectActionKind.TributeTargetedNormalThenDestroyUpToOppSpellTraps &&
                smash.Zone == EffectZoneFilter.ControllerMonsters && smash.RequiresTargetChoice && smash.IsPsctTarget &&
                smash.TargetCount == 1 && smash.MonsterLevelMax == 2 && smash.PickUpTo == 2 &&
                smash.ActivationCondition == NormalMonsterSpellsRuntime.OpponentControlsSpellTrap &&
                Implemented(db, OrderToSmash),
                Describe(Prog(db, OrderToSmash)));

            var sword = Only(Prog(db, SwordOfSoulEater));
            Check("Sword of the Soul-Eater: equip to a Level 3 or lower Normal Monster you control; 1000 per Tribute",
                sword != null && sword.Action == EffectActionKind.EquipThisToTarget &&
                sword.Zone == EffectZoneFilter.ControllerMonsters && sword.MonsterLevelMax == 3 &&
                sword.RequiresNormalType && sword.EquipAtkPerTributedOnActivation == 1000 && sword.EquipAtkBonus == 0 &&
                Implemented(db, SwordOfSoulEater),
                Describe(Prog(db, SwordOfSoulEater)));

            var opti = Prog(db, OptiCamouflage);
            Check("Opti-Camouflage Armor: equip to a Level 1 monster on either field + the Equip's direct attack (no own " +
                  "CanAttackDirectly clause)",
                opti.FullyCompiled && opti.ClauseList.Count == 2 &&
                opti.ClauseList.Exists(c => c.Action == EffectActionKind.EquipThisToTarget &&
                                            c.Zone == EffectZoneFilter.FieldAnyMonster && c.MonsterLevelMin == 1 &&
                                            c.MonsterLevelMax == 1 && !c.RequiresNormalType) &&
                opti.ClauseList.Exists(c => c.Action == EffectActionKind.EquippedCanAttackDirectly) &&
                !opti.ClauseList.Exists(c => c.Action == EffectActionKind.CanAttackDirectly) &&
                Implemented(db, OptiCamouflage),
                Describe(opti));

            // Near misses: look-alike wordings stay closed (no Level, name count or zone guessed).
            var nearMisses = new (string type, string race, string text)[]
            {
                ("Spell Card", "Normal",
                    "You can only activate this card while there are 3 Normal Monsters of the same name (except Tokens) " +
                    "on your side of the field. The 2 Normal Monsters of the same name can attack your opponent's Life " +
                    "Points directly during the turn this card is activated."),
                ("Spell Card", "Normal",
                    "Increase the ATK and DEF of all face-up Level 2 Normal Monsters (except Tokens) on your side of the " +
                    "field by 1000 points until the End Phase. Destroy all Level 2 Normal Monsters on your side of the " +
                    "field during the End Phase."),
                ("Spell Card", "Normal",
                    "Increase the original ATK and DEF of all face-up Level 2 Normal Monsters (except Tokens) on your side " +
                    "of the field by 1000 points until the End Phase. Destroy all Level 3 Normal Monsters on your side of " +
                    "the field during the End Phase."),
                ("Spell Card", "Normal",
                    "You can only activate this card while there are 5 face-up Level 2 or lower Normal Monsters on your " +
                    "side of the field. Both players discard all cards in their hands, and destroy all cards on the field " +
                    "except Level 3 or lower Normal Monsters."),
                ("Trap Card", "Normal",
                    "Target 1 Level 2 or lower non-Token Normal Monster you control; Tribute that Normal Monster, and if " +
                    "you do, destroy up to 2 monsters your opponent controls."),
                ("Spell Card", "Equip",
                    "You can only equip this card to a Level 2 or lower monster. A monster equipped with this card can " +
                    "attack your opponent's Life Points directly."),
            };
            var open = new List<string>();
            foreach (var (type, race, text) in nearMisses)
            {
                var p = CompileText(type, race, text);
                if (p.FullyCompiled) open.Add(text.Substring(0, Math.Min(60, text.Length)) + " => " + Describe(p));
            }

            Check("near misses stay not FullyCompiled", open.Count == 0, string.Join(" | ", open));
        }

        // ───────────────────────────── Delta Attacker ─────────────────────────────

        static void DeltaAttackerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var m1 = PlaceMonster(e, p, MokeyMokey, 0, BattlePosition.Attack, true);
            var m2 = PlaceMonster(e, p, MokeyMokey, 1, BattlePosition.Attack, true);
            var m3 = PlaceMonster(e, p, MokeyMokey, 2, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, p, Celtic, 3, BattlePosition.Attack, true);
            var oppCeltic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var da = PutInHand(e, p, DeltaAttacker);
            var withSet = e.CanActivateSpellTrap(p, da, fromHand: true);
            m3.FaceUp = true;
            m3.Position = BattlePosition.Attack;
            var withThree = e.CanActivateSpellTrap(p, da, fromHand: true);
            var act = e.TryActivateSpellTrap(p, da, fromHand: true);
            Drive(e, _ => false);
            var granted = m1.DirectAttackThisTurn && m2.DirectAttackThisTurn && m3.DirectAttackThisTurn &&
                          !celtic.DirectAttackThisTurn;
            e.TryEnterBattlePhase(p);
            if (e.IsAwaitingResponse) e.PassResponse();
            var celticDirect = e.CanAttackDirectly(p, celtic);
            var lp = o.LifePoints;
            var hit = e.TryAttack(p, m1, null);
            DrainCombat(e);
            Check("Delta Attacker: 3 face-up Mokey Mokey (a face-down third does not count) attack directly; Celtic does not",
                reached && !withSet && withThree && act && granted && !celticDirect && hit && o.LifePoints == lp - 300 &&
                p.Graveyard.Contains(da) && Where(e, oppCeltic) == "OM0u",
                $"reached={reached} set={withSet} three={withThree} act={act} granted={granted} celtic={celticDirect} " +
                $"hit={hit} lp={lp}->{o.LifePoints}");

            // Tokens never count: 2 Mokey Mokey + 1 Token named alike would still be only 2 Normal Monsters.
            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            PlaceMonster(e2, e2.Player, MokeyMokey, 0, BattlePosition.Attack, true);
            PlaceMonster(e2, e2.Player, MokeyMokey, 1, BattlePosition.Attack, true);
            PlaceToken(e2, e2.Player, 2, 1);
            PlaceMonster(e2, e2.Player, Celtic, 3, BattlePosition.Attack, true);
            PlaceMonster(e2, e2.Player, Celtic, 4, BattlePosition.Attack, true);
            var da2 = PutInHand(e2, e2.Player, DeltaAttacker);
            Check("Delta Attacker: two pairs of names (2 + 2) are not 3 of one name",
                !e2.CanActivateSpellTrap(e2.Player, da2, fromHand: true));
        }

        // ───────────────────────────── Thousand Energy ─────────────────────────────

        static void ThousandEnergyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var petit = PlaceMonster(e, p, PetitDragon, 0, BattlePosition.Attack, true);
            var kage = PlaceMonster(e, p, Kagemusha, 1, BattlePosition.Defense, false);
            var bug = PlaceMonster(e, p, ManEaterBug, 2, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 3, BattlePosition.Attack, true);
            var te = PutInHand(e, p, ThousandEnergy);
            var act = e.TryActivateSpellTrap(p, te, fromHand: true);
            Drive(e, _ => false);
            var boosted = petit.CurrentAtk == 1600 && petit.CurrentDef == 1700;
            var untouched = kage.OriginalAtkOverride == int.MinValue && bug.CurrentAtk == 450 && celtic.CurrentAtk == 1400;
            // A Level 2 Normal Monster that is face-up only later is destroyed in the End Phase too (Lua dfilter).
            kage.FaceUp = true;
            kage.Position = BattlePosition.Attack;
            var kageAtk = kage.CurrentAtk;
            e.TryEndTurnSafe(p);
            Drive(e, _ => false);
            Check("Thousand Energy: Petit Dragon 1600/1700 (original +1000), the Set Kagemusha / Effect / Level 4 " +
                  "untouched; End Phase destroys both face-up Level 2 Normal Monsters only",
                reached && act && boosted && untouched && kageAtk == 800 && p.Graveyard.Contains(petit) &&
                p.Graveyard.Contains(kage) && p.TryFindMonster(bug, out _) && p.TryFindMonster(celtic, out _) &&
                petit.OriginalAtkOverride == int.MinValue && petit.OriginalDefOverride == int.MinValue,
                $"reached={reached} act={act} petit={petit.CurrentAtk}/{petit.CurrentDef} kageAtk={kageAtk} " +
                $"bug={bug.CurrentAtk} petit@{Where(e, petit)} kage@{Where(e, kage)} bug@{Where(e, bug)} " +
                $"celtic@{Where(e, celtic)} override={petit.OriginalAtkOverride}");

            // No face-up non-Token Level 2 Normal Monster: cannot be activated (only a Set one and a Level 2 Effect Monster).
            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            PlaceMonster(e2, e2.Player, Kagemusha, 0, BattlePosition.Defense, false);
            PlaceMonster(e2, e2.Player, ManEaterBug, 1, BattlePosition.Attack, true);
            PlaceToken(e2, e2.Player, 2, 2);
            var te2 = PutInHand(e2, e2.Player, ThousandEnergy);
            Check("Thousand Energy: a Set Level 2 Normal, a Level 2 Effect Monster and a Level 2 Token do not allow it",
                !e2.CanActivateSpellTrap(e2.Player, te2, fromHand: true));

            // The boost is the monster's original ATK while it stays: it leaves the field, its original comes back.
            var e3 = PlayerMain1(db, pDeck, aDeck, out _);
            var petit3 = PlaceMonster(e3, e3.Player, PetitDragon, 0, BattlePosition.Attack, true);
            var te3 = PutInHand(e3, e3.Player, ThousandEnergy);
            e3.TryActivateSpellTrap(e3.Player, te3, fromHand: true);
            Drive(e3, _ => false);
            var during = petit3.CurrentAtk;
            e3.ReturnCardToDeck(petit3);
            FieldSpellEffects.RefreshBoard(e3);
            Check("Thousand Energy: the original ATK/DEF change ends when the monster leaves the field",
                during == 1600 && petit3.OriginalAtkOverride == int.MinValue && petit3.OriginalDefOverride == int.MinValue,
                $"during={during} override={petit3.OriginalAtkOverride}/{petit3.OriginalDefOverride}");
        }

        // ───────────────────────────── Triangle Power ─────────────────────────────

        static void TrianglePowerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var mokey = PlaceMonster(e, p, MokeyMokey, 0, BattlePosition.Attack, true);
            var skull = PlaceMonster(e, p, SkullServant, 1, BattlePosition.Attack, true);
            var kuri = PlaceMonster(e, p, Kuriboh, 2, BattlePosition.Attack, true);
            var sheep = PlaceToken(e, p, 3, 1);
            var petit = PlaceMonster(e, p, PetitDragon, 4, BattlePosition.Attack, true);
            var tp = PutInHand(e, p, TrianglePower);
            var act = e.TryActivateSpellTrap(p, tp, fromHand: true);
            Drive(e, _ => false);
            var boosted = mokey.CurrentAtk == 2300 && mokey.CurrentDef == 2100 && skull.CurrentAtk == 2300 &&
                          skull.CurrentDef == 2200;
            var untouched = kuri.CurrentAtk == 300 && sheep.CurrentAtk == 0 && petit.CurrentAtk == 600;
            e.TryEndTurnSafe(p);
            Drive(e, _ => false);
            Check("Triangle Power: Level 1 Normal Monsters +2000 original (the Token and Kuriboh not); End Phase destroys " +
                  "the Level 1 Normal Monsters and the Level 1 Token, not Kuriboh or the Level 2 Petit Dragon",
                reached && act && boosted && untouched && p.Graveyard.Contains(mokey) && p.Graveyard.Contains(skull) &&
                !p.TryFindMonster(sheep, out _) && p.TryFindMonster(kuri, out _) && p.TryFindMonster(petit, out _),
                $"reached={reached} act={act} mokey={mokey.CurrentAtk}/{mokey.CurrentDef} skull={skull.CurrentAtk} " +
                $"kuri={kuri.CurrentAtk} sheep={sheep.CurrentAtk} mokey@{Where(e, mokey)} sheep@{Where(e, sheep)} " +
                $"kuri@{Where(e, kuri)} petit@{Where(e, petit)}");
        }

        // ───────────────────────────── The Law of the Normal ─────────────────────────────

        static void LawOfTheNormalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var mine = new List<CardInstance>
            {
                PlaceMonster(e, p, MokeyMokey, 0, BattlePosition.Attack, true),
                PlaceMonster(e, p, MokeyMokey, 1, BattlePosition.Attack, true),
                PlaceMonster(e, p, SkullServant, 2, BattlePosition.Attack, true),
                PlaceMonster(e, p, PetitDragon, 3, BattlePosition.Attack, true),
            };
            var oppMokey = PlaceMonster(e, o, MokeyMokey, 0, BattlePosition.Attack, true);
            var oppCeltic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            var oppSetSkull = PlaceMonster(e, o, SkullServant, 2, BattlePosition.Defense, false);
            var oppSet = PlaceSetTrap(e, o, PotOfGreed, 0);
            var yami = e.CreateCardInstance(Yami);
            yami.FaceUp = true;
            o.FieldSpellZone.Occupant = yami;
            var law = PutInHand(e, p, LawOfTheNormal);
            var pog = PutInHand(e, p, PotOfGreed);
            var oHand1 = PutInHand(e, o, Celtic);
            var oHand2 = PutInHand(e, o, Kuriboh);
            var withFour = e.CanActivateSpellTrap(p, law, fromHand: true);
            mine.Add(PlaceMonster(e, p, Kagemusha, 4, BattlePosition.Attack, true));
            var withFive = e.CanActivateSpellTrap(p, law, fromHand: true);
            o.Hand.Clear();
            var noOppHand = e.CanActivateSpellTrap(p, law, fromHand: true);
            o.Hand.Add(oHand1);
            o.Hand.Add(oHand2);
            p.Hand.Remove(pog);
            var noOwnHand = e.CanActivateSpellTrap(p, law, fromHand: true);
            p.Hand.Add(pog);
            var act = e.TryActivateSpellTrap(p, law, fromHand: true);
            Drive(e, _ => false);
            var kept = mine.All(m => p.TryFindMonster(m, out _)) && o.TryFindMonster(oppMokey, out _);
            Check("The Law of the Normal: needs 5 face-up Level 2 or lower Normal Monsters, another card in your hand and " +
                  "one in the opponent's; both hands discarded; everything but face-up Level 2 or lower Normal Monsters " +
                  "destroyed (a Set Level 1 Normal, a Level 4 Normal, a Set Spell and the Field Spell too)",
                reached && !withFour && withFive && !noOppHand && !noOwnHand && act && kept && p.Hand.Count == 0 &&
                o.Hand.Count == 0 && p.Graveyard.Contains(pog) && o.Graveyard.Contains(oHand1) &&
                o.Graveyard.Contains(oHand2) && o.Graveyard.Contains(oppCeltic) && o.Graveyard.Contains(oppSetSkull) &&
                o.Graveyard.Contains(oppSet) && o.Graveyard.Contains(yami) && p.Graveyard.Contains(law),
                $"reached={reached} four={withFour} five={withFive} noOpp={noOppHand} noOwn={noOwnHand} act={act} " +
                $"kept={kept} pHand={p.Hand.Count} oHand={o.Hand.Count} celtic@{Where(e, oppCeltic)} " +
                $"setSkull@{Where(e, oppSetSkull)} set@{Where(e, oppSet)} yami@{Where(e, yami)} law@{Where(e, law)}");
        }

        // ───────────────────────────── Order to Smash ─────────────────────────────

        static void OrderToSmashDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var petit = PlaceMonster(e, p, PetitDragon, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var bug = PlaceMonster(e, p, ManEaterBug, 2, BattlePosition.Attack, true);
                var setSkull = PlaceMonster(e, p, SkullServant, 3, BattlePosition.Defense, false);
                PlaceToken(e, p, 4, 1);
                var ots = PlaceSetTrap(e, p, OrderToSmash, 0);
                var noOppCard = e.CanActivateSpellTrap(p, ots, fromHand: false);
                var s1 = PlaceSetTrap(e, o, PotOfGreed, 0);
                var s2 = PlaceSetTrap(e, o, MirrorForce, 1);
                var s3 = PlaceSetTrap(e, o, PotOfGreed, 2);
                var can = e.CanActivateSpellTrap(p, ots, fromHand: false);
                var offers = new List<List<CardInstance>>();
                var act = e.TryActivateSpellTrap(p, ots, fromHand: false);
                Drive(e, c => c == petit || c == s1 || c == s2, offers);
                var firstOffer = offers.FirstOrDefault(l => l.Contains(s1)) ?? new List<CardInstance>();
                Check("Order to Smash: the lone legal target (face-up Level 2 non-Token Normal) is Tributed; 2 of the " +
                      "opponent's 3 Set cards are chosen and destroyed",
                    reached && !noOppCard && can && act && p.Graveyard.Contains(petit) && o.Graveyard.Contains(s1) &&
                    o.Graveyard.Contains(s2) && o.TryFindSpellTrap(s3, out _) && p.TryFindMonster(celtic, out _) &&
                    p.TryFindMonster(bug, out _) && p.TryFindMonster(setSkull, out _) && p.Graveyard.Contains(ots) &&
                    firstOffer.Count == 3 && !firstOffer.Contains(ots),
                    $"reached={reached} noOpp={noOppCard} can={can} act={act} petit@{Where(e, petit)} s1@{Where(e, s1)} " +
                    $"s2@{Where(e, s2)} s3@{Where(e, s3)} offers={offers.Count}:{firstOffer.Count}");
            }

            {
                // "Up to 2": Cancel at the second choice destroys only the first.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var petit = PlaceMonster(e, p, PetitDragon, 0, BattlePosition.Attack, true);
                var ots = PlaceSetTrap(e, p, OrderToSmash, 0);
                var s1 = PlaceSetTrap(e, o, PotOfGreed, 0);
                var s2 = PlaceSetTrap(e, o, PotOfGreed, 1);
                var act = e.TryActivateSpellTrap(p, ots, fromHand: false);
                Drive(e, c => c == petit || c == s1);
                Check("Order to Smash: Cancel at the optional second choice — only 1 card is destroyed",
                    act && p.Graveyard.Contains(petit) && o.Graveyard.Contains(s1) && o.TryFindSpellTrap(s2, out _),
                    $"act={act} petit@{Where(e, petit)} s1@{Where(e, s1)} s2@{Where(e, s2)}");
            }

            {
                // No legal target: a Level 3 Normal, an Effect Monster, a Token; and while Tributes are forbidden.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, BabyDragon, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, ManEaterBug, 1, BattlePosition.Attack, true);
                PlaceToken(e, p, 2, 1);
                var ots = PlaceSetTrap(e, p, OrderToSmash, 0);
                PlaceSetTrap(e, o, PotOfGreed, 0);
                var noTarget = e.CanActivateSpellTrap(p, ots, fromHand: false);
                PlaceMonster(e, p, PetitDragon, 3, BattlePosition.Attack, true);
                var withTarget = e.CanActivateSpellTrap(p, ots, fromHand: false);
                FaceUpSpell(e, o, MaskOfRestrict, 1);
                FieldSpellEffects.RefreshBoard(e);
                var underMask = e.CanActivateSpellTrap(p, ots, fromHand: false);
                Check("Order to Smash: no Level 3 / Effect / Token target; a Level 2 Normal is one; Mask of Restrict " +
                      "stops it",
                    !noTarget && withTarget && !underMask,
                    $"noTarget={noTarget} withTarget={withTarget} mask={underMask}");
            }
        }

        // ───────────────────────────── Sword of the Soul-Eater ─────────────────────────────

        static void SwordDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var petit = PlaceMonster(e, p, PetitDragon, 0, BattlePosition.Attack, true);
            var baby = PlaceMonster(e, p, BabyDragon, 1, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
            var setKage = PlaceMonster(e, p, Kagemusha, 3, BattlePosition.Defense, false);
            var bug = PlaceMonster(e, p, ManEaterBug, 4, BattlePosition.Attack, true);
            var oppMokey = PlaceMonster(e, o, MokeyMokey, 0, BattlePosition.Attack, true);
            var oppCeltic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            var sword = PutInHand(e, p, SwordOfSoulEater);
            var offers = new List<List<CardInstance>>();
            var act = e.TryActivateSpellTrap(p, sword, fromHand: true);
            Drive(e, c => c == baby, offers);
            var offer = offers.FirstOrDefault() ?? new List<CardInstance>();
            var offerOk = offer.Count == 2 && offer.Contains(petit) && offer.Contains(baby);
            Check("Sword of the Soul-Eater: offered only your face-up Level 3 or lower Normal Monsters; activating Tributes " +
                  "the other 3 Normal Monsters (the Set one too, not the Effect Monster); +3000 ATK",
                reached && act && offerOk && sword.EquippedTo == baby && baby.CurrentAtk == 4200 &&
                p.Graveyard.Contains(petit) && p.Graveyard.Contains(celtic) && p.Graveyard.Contains(setKage) &&
                p.TryFindMonster(bug, out _) && o.TryFindMonster(oppMokey, out _),
                $"reached={reached} act={act} offer=[{string.Join(",", offer.Select(c => c.Name))}] " +
                $"host={sword.EquippedTo?.Name} atk={baby.CurrentAtk} petit@{Where(e, petit)} celtic@{Where(e, celtic)} " +
                $"kage@{Where(e, setKage)} bug@{Where(e, bug)}");

            Check("Sword of the Soul-Eater: its equip limit is a Level 3 or lower Normal Monster on either field",
                EquipFamilyRuntime.IsCorrectEquipTarget(e, sword, oppMokey) &&
                !EquipFamilyRuntime.IsCorrectEquipTarget(e, sword, oppCeltic) &&
                !EquipFamilyRuntime.IsCorrectEquipTarget(e, sword, bug));

            // Nothing else to Tribute: it still equips, with no ATK gain.
            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            var lone = PlaceMonster(e2, e2.Player, PetitDragon, 0, BattlePosition.Attack, true);
            var bug2 = PlaceMonster(e2, e2.Player, ManEaterBug, 1, BattlePosition.Attack, true);
            var sword2 = PutInHand(e2, e2.Player, SwordOfSoulEater);
            var act2 = e2.TryActivateSpellTrap(e2.Player, sword2, fromHand: true);
            Drive(e2, c => c == lone);
            Check("Sword of the Soul-Eater: no other Normal Monster — equipped, ATK unchanged",
                act2 && sword2.EquippedTo == lone && lone.CurrentAtk == 600 && e2.Player.TryFindMonster(bug2, out _),
                $"act={act2} host={sword2.EquippedTo?.Name} atk={lone.CurrentAtk}");
        }

        // ───────────────────────────── Opti-Camouflage Armor ─────────────────────────────

        static void OptiCamouflageDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var kuri = PlaceMonster(e, p, Kuriboh, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var oppMokey = PlaceMonster(e, o, MokeyMokey, 0, BattlePosition.Attack, true);
            var oppCeltic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            var opti = PutInHand(e, p, OptiCamouflage);
            var offers = new List<List<CardInstance>>();
            var act = e.TryActivateSpellTrap(p, opti, fromHand: true);
            Drive(e, c => c == kuri, offers);
            var offer = offers.FirstOrDefault() ?? new List<CardInstance>();
            var offerOk = offer.Count == 2 && offer.Contains(kuri) && offer.Contains(oppMokey);
            e.TryEnterBattlePhase(p);
            if (e.IsAwaitingResponse) e.PassResponse();
            var celticDirect = e.CanAttackDirectly(p, celtic);
            var lp = o.LifePoints;
            var hit = e.TryAttack(p, kuri, null);
            DrainCombat(e);
            Check("Opti-Camouflage Armor: offered the Level 1 monsters of both fields; the equipped Kuriboh attacks " +
                  "directly while the opponent has monsters; Celtic cannot",
                reached && act && offerOk && opti.EquippedTo == kuri && !celticDirect && hit && o.LifePoints == lp - 300 &&
                EquipFamilyRuntime.IsCorrectEquipTarget(e, opti, oppMokey) &&
                !EquipFamilyRuntime.IsCorrectEquipTarget(e, opti, oppCeltic),
                $"reached={reached} act={act} offer=[{string.Join(",", offer.Select(c => c.Name))}] " +
                $"host={opti.EquippedTo?.Name} celtic={celticDirect} hit={hit} lp={lp}->{o.LifePoints}");

            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            PlaceMonster(e2, e2.Player, Celtic, 0, BattlePosition.Attack, true);
            PlaceMonster(e2, e2.Opponent, PetitDragon, 0, BattlePosition.Attack, true);
            var opti2 = PutInHand(e2, e2.Player, OptiCamouflage);
            Check("Opti-Camouflage Armor: no Level 1 monster on the field — it cannot be activated",
                !e2.CanActivateSpellTrap(e2.Player, opti2, fromHand: true));
        }

        // ───────────────────────────── the AI's own activations ─────────────────────────────

        /// <summary>The AI (turn 2) activates Order to Smash and Sword of the Soul-Eater: every choice is made at once.</summary>
        static void AiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var petit = PlaceMonster(e, o, PetitDragon, 0, BattlePosition.Attack, true);
            var baby = PlaceMonster(e, o, BabyDragon, 1, BattlePosition.Attack, true);
            var skull = PlaceMonster(e, o, SkullServant, 2, BattlePosition.Attack, true);
            var ots = PlaceSetTrap(e, o, OrderToSmash, 0);
            var s1 = PlaceSetTrap(e, p, PotOfGreed, 0);
            var s2 = PlaceSetTrap(e, p, PotOfGreed, 1);
            var s3 = PlaceSetTrap(e, p, PotOfGreed, 2);
            var act = e.TryActivateSpellTrap(o, ots, fromHand: false);
            Drive(e, _ => false);
            var tributed = o.Graveyard.Contains(petit) || o.Graveyard.Contains(skull);
            var destroyed = new[] { s1, s2, s3 }.Count(x => p.Graveyard.Contains(x));
            var sword = PutInHand(e, o, SwordOfSoulEater);
            var swordAct = e.TryActivateSpellTrap(o, sword, fromHand: true);
            Drive(e, _ => false);
            var host = sword.EquippedTo;
            Check("AI: Order to Smash Tributes its Level 2 or lower Normal and destroys 2 of 3 Spells/Traps; Sword of the " +
                  "Soul-Eater equips with no prompt and Tributes the other Normal Monster",
                reached && act && tributed && destroyed == 2 && swordAct && host != null &&
                o.MonsterCount == 1 && host.CurrentAtk == (host.Def?.atk ?? 0) + 1000 && !e.IsAwaitingEffectTarget,
                $"reached={reached} act={act} tributed={tributed} destroyed={destroyed} swordAct={swordAct} " +
                $"host={host?.Name} atk={host?.CurrentAtk} monsters={o.MonsterCount} baby@{Where(e, baby)}");
        }
    }
}
