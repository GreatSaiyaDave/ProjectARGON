using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// UnionRiders batch (v175): the Union riders on top of the v167 Union shell (Kiryu, Second Goblin, Burning Beast,
    /// Freezing Beast), Metallizing Parasite - Lunatite's no-name procedure and Spell immunity, Union Rider's
    /// take-and-equip, the printed destroy-instead rule for every Union in its Union state, and Zombie Tiger's discard
    /// read off the monster that destroyed, and Combination Attack. Formation Union stays fail-closed.
    /// </summary>
    public static class UnionRidersRegressionTests
    {
        const int Kiryu = 84814897;
        const int DarkBlade = 11321183;
        const int SecondGoblin = 19086954;
        const int GiantOrc = 73698349;
        const int BurningBeast = 59364406;
        const int FreezingBeast = 85359414;
        const int Lunatite = 7369217;
        const int UnionRider = 11743119;
        const int FormationUnion = 26931058;
        const int CombinationAttack = 8964854;
        const int ZombieTiger = 47693640;
        const int DecayedCommander = 10209545;
        const int Celtic = 91152256;
        const int Kuriboh = 40640057;
        const int Yami = 59197169;
        const int DarkHole = 53129443;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("UnionRiders: " + n, ok, d ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            KiryuTributeForDirectAttack(db, pDeck, aDeck, Check);
            KiryuChoiceAndCancel(db, pDeck, aDeck, Check);
            SecondGoblinChangesPosition(db, pDeck, aDeck, Check);
            BurningBeastDestroysFaceUp(db, pDeck, aDeck, Check);
            FreezingBeastDestroysFaceDown(db, pDeck, aDeck, Check);
            LunatiteEquipsAnyAndShields(db, pDeck, aDeck, Check);
            UnionRiderTakesAndEquips(db, pDeck, aDeck, Check);
            OneUnionLimitCountsUnionStateOnly(db, pDeck, aDeck, Check);
            EveryUnionSubstitutes(db, pDeck, aDeck, Check);
            ZombieTigerDiscardsOffTheDestroyer(db, pDeck, aDeck, Check);
            CombinationAttackSummonsAndAttacksAgain(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static string Frags(CompiledCardProgram p) =>
            p == null ? "null" : $"full={p.FullyCompiled} | {string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}";

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            var kiryu = CardTextEffectCompiler.Compile(db.Get(Kiryu));
            Check("Kiryu compiles: Dark Blade procedure +900 ATK/DEF and the Tribute ignition from the S/T Zone",
                kiryu != null && kiryu.FullyCompiled &&
                Has(kiryu, c => c.Action == EffectActionKind.EquipThisToTarget && c.EquipHostName == "Dark Blade" &&
                                c.EquipAtkBonus == 900 && c.EquipDefBonus == 900) &&
                Has(kiryu, c => c.Action == EffectActionKind.UnequipThisSpecialSummon) &&
                Has(kiryu, c => c.Timing == EffectTiming.UnionStateIgnition &&
                                c.Action == EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly && !c.OncePerTurn) &&
                CardEffectStatus.Classify(db.Get(Kiryu)) == CardEffectStatusKind.Implemented,
                Frags(kiryu));

            var goblin = CardTextEffectCompiler.Compile(db.Get(SecondGoblin));
            Check("Second Goblin compiles: Giant Orc procedure and a once-per-turn position ignition",
                goblin != null && goblin.FullyCompiled &&
                Has(goblin, c => c.Action == EffectActionKind.EquipThisToTarget && c.EquipHostName == "Giant Orc") &&
                Has(goblin, c => c.Timing == EffectTiming.UnionStateIgnition &&
                                 c.Action == EffectActionKind.ChangeEquippedMonsterBattlePosition && c.OncePerTurn) &&
                CardEffectStatus.Classify(db.Get(SecondGoblin)) == CardEffectStatusKind.Implemented,
                Frags(goblin));

            var burning = CardTextEffectCompiler.Compile(db.Get(BurningBeast));
            var freezing = CardTextEffectCompiler.Compile(db.Get(FreezingBeast));
            Check("Burning / Freezing Beast compile a mandatory targeted destroy on the equipped monster's battle damage",
                burning != null && burning.FullyCompiled && freezing != null && freezing.FullyCompiled &&
                Has(burning, c => c.Timing == EffectTiming.EquippedMonsterInflictsBattleDamage &&
                                  c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice && c.RequiresTargetFaceUp &&
                                  c.Zone == EffectZoneFilter.FieldSpellTraps && !c.IsOptional) &&
                Has(freezing, c => c.Timing == EffectTiming.EquippedMonsterInflictsBattleDamage &&
                                   c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice &&
                                   c.RequiresTargetFaceDown && c.Zone == EffectZoneFilter.FaceDownSpellTraps) &&
                CardEffectStatus.Classify(db.Get(BurningBeast)) == CardEffectStatusKind.Implemented &&
                CardEffectStatus.Classify(db.Get(FreezingBeast)) == CardEffectStatusKind.Implemented,
                Frags(burning) + " // " + Frags(freezing));

            var luna = CardTextEffectCompiler.Compile(db.Get(Lunatite));
            Check("Lunatite compiles: no-name Union procedure and the opponent's-Spell immunity",
                luna != null && luna.FullyCompiled &&
                Has(luna, c => c.Action == EffectActionKind.EquipThisToTarget && c.UnionEquipsAnyFaceUpMonster &&
                               string.IsNullOrEmpty(c.EquipHostName) && c.OncePerTurn) &&
                Has(luna, c => c.Action == EffectActionKind.UnequipThisSpecialSummon) &&
                Has(luna, c => c.Action == EffectActionKind.EquippedUnaffectedByOpponentSpellEffects) &&
                CardEffectStatus.Classify(db.Get(Lunatite)) == CardEffectStatusKind.Implemented,
                Frags(luna));

            var rider = CardTextEffectCompiler.Compile(db.Get(UnionRider));
            Check("Union Rider compiles: once per turn, target a face-up opponent's Union, take it and equip it",
                rider != null && rider.FullyCompiled &&
                Has(rider, c => c.Timing == EffectTiming.Activate &&
                                c.Action == EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis &&
                                c.Zone == EffectZoneFilter.OppFaceUpMonsters && c.RequiresTargetChoice && c.OncePerTurn &&
                                c.ActivationCondition == UnionRidersRuntime.NoUnionEquippedByThisEffect) &&
                CardEffectStatus.Classify(db.Get(UnionRider)) == CardEffectStatusKind.Implemented,
                Frags(rider));

            var combo = CardTextEffectCompiler.Compile(db.Get(CombinationAttack));
            Check("Combination Attack compiles: Battle Phase only, target a monster that attacked with a Union equipped",
                combo != null && combo.FullyCompiled &&
                Has(combo, c => c.Timing == EffectTiming.Activate &&
                                c.Action == EffectActionKind.SpecialSummonTargetsUnionsThenItAttacksAgain &&
                                c.RequiresTargetChoice && c.RequiresTargetFaceUp && c.RequiresBattlePhase &&
                                c.Zone == EffectZoneFilter.FieldAnyMonster) &&
                CardEffectStatus.Classify(db.Get(CombinationAttack)) == CardEffectStatusKind.Implemented,
                Frags(combo));

            foreach (var id in new[] { FormationUnion })
            {
                var def = db.Get(id);
                var prog = CardTextEffectCompiler.Compile(def);
                Check($"{def?.name} stays fail-closed (not FullyCompiled, not Implemented)",
                    def != null && (prog == null || !prog.FullyCompiled) &&
                    CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    Frags(prog));
            }
        }

        static CompiledCardProgram CompileUnion(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990175, name = "Near-miss Union", type = "Union Effect Monster", frameType = "effect",
                race = "Dragon", attribute = "DARK", level = 4, atk = 1000, def = 1000, desc = text
            });

        static void NearMisses(Action<string, bool, string> Check)
        {
            const string proc = "Once per turn, during your Main Phase, if you control this card on the field, you can " +
                                "equip it to your \"Dark Blade\" as an Equip Spell Card, OR unequip the Union equipment and " +
                                "Special Summon this card in face-up Attack Position. ";
            const string tail = " (1 monster can only be equipped with 1 Union Monster at a time. If the equipped monster " +
                                "is destroyed as a result of battle, destroy this card instead.)";
            var twice = CompileUnion(proc + "While equipped to a monster by this card's effect, increase the ATK/DEF of " +
                                     "the equipped monster by 900 points, and you can Tribute this card to allow the " +
                                     "equipped monster to attack twice this turn." + tail);
            Check("near miss: a Tribute rider granting a second attack is not read as the direct-attack ignition",
                twice != null && !twice.FullyCompiled &&
                !Has(twice, c => c.Action == EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly),
                Frags(twice));
            var monsters = CompileUnion(proc + "While equipped to a monster by this card's effect, each time the " +
                                        "equipped monster inflicts Battle Damage to your opponent, destroy 1 face-up " +
                                        "monster on the field." + tail);
            Check("near miss: a battle-damage rider destroying a monster is not the Spell/Trap destroy",
                monsters != null && !monsters.FullyCompiled &&
                !Has(monsters, c => c.Timing == EffectTiming.EquippedMonsterInflictsBattleDamage),
                Frags(monsters));
            var own = CompileUnion(proc + "While equipped to a monster by this card's effect, that monster will be " +
                                   "unaffected by the effects of Trap Cards controlled by your opponent." + tail);
            Check("near miss: an opponent's-Trap immunity is not compiled as the Spell immunity",
                own != null && !own.FullyCompiled &&
                !Has(own, c => c.Action == EffectActionKind.EquippedUnaffectedByOpponentSpellEffects),
                Frags(own));
            var two = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990176, name = "Near-miss Rider", type = "Effect Monster", frameType = "effect", race = "Machine",
                attribute = "WIND", level = 2, atk = 1000, def = 1000,
                desc = "You can take control of up to 2 Union Monsters that are currently monsters on your opponent's " +
                       "side of the field, and equip them to this card. The Union Monster that is equipped to this card " +
                       "cannot be changed back to a monster using its own effect."
            });
            Check("near miss: 'up to 2 Union Monsters' is not Union Rider's take-and-equip",
                two != null && !two.FullyCompiled &&
                !Has(two, c => c.Action == EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis),
                Frags(two));
        }

        // ───────────────────────── duels ─────────────────────────

        /// <summary>A cleared board on the Player's Main Phase 1 (turn 3 when a battle is needed: turn 1 has none).</summary>
        static DuelEngine Board(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool battleTurn = false)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var i = 0; battleTurn && i < 12; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.InMainPhase) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static bool InSt(DuelistState who, CardInstance c) => who.SpellTrapZones.Any(z => z.Occupant == c);
        static bool InMz(DuelistState who, CardInstance c) => who.MonsterZones.Any(z => z.Occupant == c);

        static bool Equip(DuelEngine e, CardInstance union, CardInstance host)
        {
            var used = e.TryActivateSpellTrap(e.Player, union, fromHand: false);
            Settle(e);
            if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(host))
                e.TrySelectEffectTarget(host);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            return used && union.EquippedTo == host && InSt(e.Player, union);
        }

        /// <summary>Declares an attack and runs the battle until it ends or a pick opens.</summary>
        static bool AttackUntilPick(DuelEngine e, CardInstance attacker, CardInstance target)
        {
            if (e.Phase == DuelPhase.Main1) e.TryEnterBattlePhase(e.Player);
            Settle(e);
            if (!e.TryAttack(e.Player, attacker, target)) return false;
            for (var i = 0; i < 16 && !e.IsAwaitingEffectTarget; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else break;
            }

            return true;
        }

        static void KiryuTributeForDirectAttack(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck, battleTurn: true);
            var p = e.Player;
            var kiryu = PlaceMonster(e, p, Kiryu, 0, BattlePosition.Attack, true);
            var blade = PlaceMonster(e, p, DarkBlade, 1, BattlePosition.Attack, true);
            PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
            var equipped = Equip(e, kiryu, blade);
            Check("Kiryu equips to Dark Blade (+900 ATK/DEF)",
                equipped && blade.CurrentAtk == 2700 && blade.CurrentDef == 2400,
                $"equipped={equipped} atk={blade.CurrentAtk} def={blade.CurrentDef} phase={e.Phase} turn={e.TurnNumber}");

            // The procedure is used this turn; the Tribute ignition is a different effect and is still legal.
            var legal = e.CanActivateSpellTrap(p, kiryu, fromHand: false);
            var used = e.TryActivateSpellTrap(p, kiryu, fromHand: false);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            Check("Kiryu: Tribute it from the S/T Zone — the equipped monster can attack directly this turn",
                legal && used && !e.IsAwaitingEffectTarget && p.Graveyard.Contains(kiryu) && !InSt(p, kiryu) &&
                blade.DirectAttackThisTurn && blade.CurrentAtk == 1800 && !e.Chain.HasLinks,
                $"legal={legal} used={used} gy={p.Graveyard.Contains(kiryu)} direct={blade.DirectAttackThisTurn} atk={blade.CurrentAtk}");

            var lp = e.Opponent.LifePoints;
            AttackUntilPick(e, blade, null);
            DrainCombat(e);
            Check("Dark Blade attacks directly although the opponent controls a monster",
                e.Opponent.LifePoints == lp - 1800, $"lp {lp}->{e.Opponent.LifePoints}");
        }

        static void KiryuChoiceAndCancel(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var kiryu = PlaceMonster(e, p, Kiryu, 0, BattlePosition.Attack, true);
            var blade = PlaceMonster(e, p, DarkBlade, 1, BattlePosition.Attack, true);
            Equip(e, kiryu, blade);
            kiryu.EffectUsedThisTurn = false; // a later turn: both its unequip and its ignition are usable

            var used = e.TryActivateSpellTrap(p, kiryu, fromHand: false);
            var offered = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(blade) && e.IsLegalEffectTarget(kiryu);
            e.CancelEffectTargeting();
            Settle(e);
            Check("Kiryu: with its unequip also usable you choose (Dark Blade = ignition, Kiryu = unequip); CANCEL activates neither",
                used && offered && !e.IsAwaitingEffectTarget && kiryu.EquippedTo == blade && InSt(p, kiryu) &&
                !kiryu.EffectUsedThisTurn && !e.Chain.HasLinks,
                $"used={used} offered={offered} eq={kiryu.EquippedTo?.Name} links={e.Chain.Count}");

            e.TryActivateSpellTrap(p, kiryu, fromHand: false);
            var chose = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(kiryu);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            Check("Kiryu: tapping Kiryu unequips it and Special Summons it in Attack Position",
                chose && kiryu.EquippedTo == null && InMz(p, kiryu) && kiryu.Position == BattlePosition.Attack &&
                kiryu.FaceUp && blade.CurrentAtk == 1800 && !blade.DirectAttackThisTurn,
                $"chose={chose} mz={InMz(p, kiryu)} atk={blade.CurrentAtk}");
        }

        static void SecondGoblinChangesPosition(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var goblin = PlaceMonster(e, p, SecondGoblin, 0, BattlePosition.Attack, true);
            var orc = PlaceMonster(e, p, GiantOrc, 1, BattlePosition.Attack, true);
            var equipped = Equip(e, goblin, orc);
            var used = e.TryActivateSpellTrap(p, goblin, fromHand: false);
            Settle(e);
            Check("Second Goblin: changes the equipped Giant Orc to face-up Defense Position",
                equipped && used && orc.Position == BattlePosition.Defense && orc.FaceUp && goblin.EquippedTo == orc &&
                InSt(p, goblin) && !e.Chain.HasLinks,
                $"equipped={equipped} used={used} pos={orc.Position}");
            var again = e.CanActivateSpellTrap(p, goblin, fromHand: false);
            Check("Second Goblin: its position change is once per turn (and its unequip was used by the equip)",
                !again && orc.Position == BattlePosition.Defense, $"again={again}");

            // A later turn would offer both: here only the unequip is reset, so the spent ignition is not offered.
            goblin.EffectUsedThisTurn = false;
            e.TryActivateSpellTrap(p, goblin, fromHand: false);
            Settle(e);
            Check("Second Goblin: with the ignition spent, activating it unequips it (no choice offered)",
                !e.IsAwaitingEffectTarget && goblin.EquippedTo == null && InMz(p, goblin) &&
                orc.Position == BattlePosition.Defense,
                $"awaiting={e.IsAwaitingEffectTarget} eq={goblin.EquippedTo?.Name}");
        }

        static void BurningBeastDestroysFaceUp(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck, battleTurn: true);
            var p = e.Player;
            var o = e.Opponent;
            var freezing = PlaceMonster(e, p, FreezingBeast, 0, BattlePosition.Attack, true);
            var burning = PlaceMonster(e, p, BurningBeast, 1, BattlePosition.Attack, true);
            var yami = e.CreateCardInstance(Yami);
            yami.FaceUp = true;
            o.FieldSpellZone.Occupant = yami;
            var setHole = PlaceSetTrap(e, o, DarkHole, 1);
            var equipped = Equip(e, burning, freezing);
            var lp = o.LifePoints;
            var attacked = AttackUntilPick(e, freezing, null);
            // Face-up Spells/Traps: Yami and Burning Beast itself (an Equip Spell Card now) — the player chooses.
            var offered = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(yami) && e.IsLegalEffectTarget(burning) &&
                          !e.IsLegalEffectTarget(setHole);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(yami);
            DrainCombat(e);
            Check("Burning Beast: the equipped monster's battle damage destroys the face-up Spell/Trap you target",
                equipped && attacked && offered && o.LifePoints == lp - 1500 && o.Graveyard.Contains(yami) &&
                o.FieldSpellZone.Occupant == null && o.SpellTrapZones[1].Occupant == setHole && InSt(p, burning),
                $"equipped={equipped} attacked={attacked} offered={offered} lp {lp}->{o.LifePoints} yamiGy={o.Graveyard.Contains(yami)}");
        }

        static void FreezingBeastDestroysFaceDown(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck, battleTurn: true);
            var p = e.Player;
            var o = e.Opponent;
            var burning = PlaceMonster(e, p, BurningBeast, 0, BattlePosition.Attack, true);
            var freezing = PlaceMonster(e, p, FreezingBeast, 1, BattlePosition.Attack, true);
            var yami = e.CreateCardInstance(Yami);
            yami.FaceUp = true;
            o.FieldSpellZone.Occupant = yami;
            var setHole = PlaceSetTrap(e, o, DarkHole, 1);
            var equipped = Equip(e, freezing, burning);
            var lp = o.LifePoints;
            AttackUntilPick(e, burning, null);
            var prompt = e.IsAwaitingEffectTarget;
            DrainCombat(e);
            Check("Freezing Beast: the lone face-down Spell/Trap is destroyed with no prompt; face-up cards stay",
                equipped && !prompt && o.LifePoints == lp - 1500 && o.Graveyard.Contains(setHole) &&
                o.FieldSpellZone.Occupant == yami,
                $"equipped={equipped} prompt={prompt} lp {lp}->{o.LifePoints} holeGy={o.Graveyard.Contains(setHole)}");

            // No battle damage to the opponent (it attacks a stronger Defense Position monster): nothing is destroyed.
            var e2 = Board(db, pDeck, aDeck, battleTurn: true);
            var b2 = PlaceMonster(e2, e2.Player, BurningBeast, 0, BattlePosition.Attack, true);
            var f2 = PlaceMonster(e2, e2.Player, FreezingBeast, 1, BattlePosition.Attack, true);
            var wall = PlaceMonster(e2, e2.Opponent, DarkBlade, 0, BattlePosition.Defense, true);
            var hole2 = PlaceSetTrap(e2, e2.Opponent, DarkHole, 1);
            Equip(e2, f2, b2);
            AttackUntilPick(e2, b2, wall);
            DrainCombat(e2);
            Check("Freezing Beast: no battle damage to the opponent, no destruction",
                e2.Opponent.SpellTrapZones[1].Occupant == hole2, $"hole={e2.Opponent.SpellTrapZones[1].Occupant?.Name}");
        }

        static void LunatiteEquipsAnyAndShields(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var luna = PlaceMonster(e, p, Lunatite, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var orc = PlaceMonster(e, p, GiantOrc, 2, BattlePosition.Attack, true);
            var used = e.TryActivateSpellTrap(p, luna, fromHand: false);
            var offered = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(celtic) && e.IsLegalEffectTarget(orc) &&
                          !e.IsLegalEffectTarget(luna);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
            Settle(e);
            Check("Lunatite: equips to any face-up monster you control (not itself)",
                used && offered && luna.EquippedTo == celtic && InSt(p, luna),
                $"used={used} offered={offered} eq={luna.EquippedTo?.Name}");

            var opp = e.Opponent;
            var oppMon = PlaceMonster(e, opp, Kuriboh, 0, BattlePosition.Attack, true);
            var reached = ReachOpponentMain(e);
            var hole = PutInHand(e, opp, DarkHole);
            var cast = e.TryActivateSpellTrap(opp, hole, fromHand: true);
            Settle(e);
            Check("Lunatite: the equipped monster is unaffected by the opponent's Dark Hole; others are destroyed",
                reached && cast && InMz(p, celtic) && p.Graveyard.Contains(orc) && opp.Graveyard.Contains(oppMon) &&
                luna.EquippedTo == celtic && InSt(p, luna),
                $"reached={reached} cast={cast} celtic={InMz(p, celtic)} orcGy={p.Graveyard.Contains(orc)} oppMonGy={opp.Graveyard.Contains(oppMon)}");

            // Its own controller's Spell still applies.
            var e2 = Board(db, pDeck, aDeck);
            var luna2 = PlaceMonster(e2, e2.Player, Lunatite, 0, BattlePosition.Attack, true);
            var celtic2 = PlaceMonster(e2, e2.Player, Celtic, 1, BattlePosition.Attack, true);
            Equip(e2, luna2, celtic2);
            var own = PutInHand(e2, e2.Player, DarkHole);
            e2.TryActivateSpellTrap(e2.Player, own, fromHand: true);
            Settle(e2);
            Check("Lunatite: your own Dark Hole still destroys the equipped monster",
                e2.Player.Graveyard.Contains(celtic2) && !InMz(e2.Player, celtic2),
                $"celtic={InMz(e2.Player, celtic2)}");
        }

        static void UnionRiderTakesAndEquips(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var rider = PlaceMonster(e, p, UnionRider, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            Check("Union Rider: no Union on the opponent's field — cannot activate",
                !e.CanActivateSpellTrap(p, rider, fromHand: false), "");
            var kiryu = PlaceMonster(e, o, Kiryu, 0, BattlePosition.Attack, true);
            var used = e.TryActivateSpellTrap(p, rider, fromHand: false);
            // The monster ignition's target sheet: only the face-up Union is offered.
            var lone = !e.IsAwaitingEffectTarget ||
                       (e.IsLegalEffectTarget(kiryu) && !e.IsLegalEffectTarget(celtic) && e.TrySelectEffectTarget(kiryu));
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            Check("Union Rider: takes the opponent's face-up Union and equips it to itself (Celtic Guardian is no target)",
                used && lone && InSt(p, kiryu) && !InMz(o, kiryu) && kiryu.EquippedTo == rider &&
                e.ControllerOf(kiryu) == p && InMz(o, celtic) && rider.CurrentAtk == 1000,
                $"used={used} lone={lone} st={InSt(p, kiryu)} eq={kiryu.EquippedTo?.Name} atk={rider.CurrentAtk}");
            Check("Union Rider: the taken Union cannot unequip itself or use its Union-state ignition; once per turn",
                !e.CanActivateSpellTrap(p, kiryu, fromHand: false) && !e.CanActivateSpellTrap(p, rider, fromHand: false),
                "");

            rider.EffectUsedThisTurn = false;
            var kiryu2 = PlaceMonster(e, o, Kiryu, 2, BattlePosition.Attack, true);
            Check("Union Rider: max. 1 — no second Union while one is equipped by its effect",
                !e.CanActivateSpellTrap(p, rider, fromHand: false) && InMz(o, kiryu2), "");

            e.DestroyMonsterByBattle(p, rider, attacker: null);
            Check("Union Rider: battle destroys it (no destroy-instead); the taken Union goes to its owner's GY",
                p.Graveyard.Contains(rider) && o.Graveyard.Contains(kiryu) && !p.Graveyard.Contains(kiryu) &&
                !InSt(p, kiryu),
                $"riderGy={p.Graveyard.Contains(rider)} kiryuOppGy={o.Graveyard.Contains(kiryu)}");
        }

        /// <summary>
        /// Review fix: the 1-Union limit (Lua aux.CheckUnionEquip / GetUnionCount) counts only Unions in their Union state
        /// (EFFECT_UNION_STATUS). A Union that Union Rider took (EquipByEffectAndLimitRegister) does not count, so Lunatite
        /// may still equip to Union Rider; a Union in its Union state still blocks a second one.
        /// </summary>
        static void OneUnionLimitCountsUnionStateOnly(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var rider = PlaceMonster(e, p, UnionRider, 0, BattlePosition.Attack, true);
            var kiryu = PlaceMonster(e, o, Kiryu, 0, BattlePosition.Attack, true);
            e.TryActivateSpellTrap(p, rider, fromHand: false);
            if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(kiryu)) e.TrySelectEffectTarget(kiryu);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            var taken = kiryu.EquippedTo == rider && InSt(p, kiryu);
            var luna = PlaceMonster(e, p, Lunatite, 1, BattlePosition.Attack, true);
            var used = e.TryActivateSpellTrap(p, luna, fromHand: false);
            var offered = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(rider);
            if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(rider)) e.TrySelectEffectTarget(rider);
            Settle(e);
            Check("Union limit: a Union Union Rider took does not count — Lunatite equips to Union Rider",
                taken && used && offered && luna.EquippedTo == rider && InSt(p, luna) && kiryu.EquippedTo == rider,
                $"taken={taken} used={used} offered={offered} eq={luna.EquippedTo?.Name}");

            var e2 = Board(db, pDeck, aDeck);
            var p2 = e2.Player;
            var luna1 = PlaceMonster(e2, p2, Lunatite, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e2, p2, Celtic, 1, BattlePosition.Attack, true);
            var first = Equip(e2, luna1, celtic);
            var luna2 = PlaceMonster(e2, p2, Lunatite, 2, BattlePosition.Attack, true);
            Check("Union limit: a Union in its Union state still blocks a second Union on the same host",
                first && !e2.CanActivateSpellTrap(p2, luna2, fromHand: false),
                $"first={first} eq={luna1.EquippedTo?.Name}");
        }

        static void EveryUnionSubstitutes(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var kiryu = PlaceMonster(e, p, Kiryu, 0, BattlePosition.Attack, true);
            var blade = PlaceMonster(e, p, DarkBlade, 1, BattlePosition.Attack, true);
            Equip(e, kiryu, blade);
            e.DestroyMonsterByBattle(p, blade, attacker: null);
            Check("Kiryu: the printed Union rule — destroyed instead of the equipped monster by battle",
                InMz(p, blade) && p.Graveyard.Contains(kiryu) && kiryu.EquippedTo == null,
                $"blade={InMz(p, blade)} kiryuGy={p.Graveyard.Contains(kiryu)}");
        }

        static void ZombieTigerDiscardsOffTheDestroyer(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck, battleTurn: true);
            var p = e.Player;
            var o = e.Opponent;
            var tiger = PlaceMonster(e, p, ZombieTiger, 0, BattlePosition.Attack, true);
            var commander = PlaceMonster(e, p, DecayedCommander, 1, BattlePosition.Attack, true);
            var kuriboh = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
            PutInHand(e, o, Celtic);
            PutInHand(e, o, Celtic);
            var equipped = Equip(e, tiger, commander);
            AttackUntilPick(e, commander, kuriboh);
            DrainCombat(e);
            Check("Zombie Tiger: its equipped monster destroys a monster by battle — the opponent discards 1 random card",
                equipped && o.Graveyard.Contains(kuriboh) && o.Hand.Count == 1,
                $"equipped={equipped} kuribohGy={o.Graveyard.Contains(kuriboh)} hand={o.Hand.Count}");
        }

        static void CombinationAttackSummonsAndAttacksAgain(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Board(db, pDeck, aDeck, battleTurn: true);
            var p = e.Player;
            var o = e.Opponent;
            var kiryu = PlaceMonster(e, p, Kiryu, 0, BattlePosition.Attack, true);
            var blade = PlaceMonster(e, p, DarkBlade, 1, BattlePosition.Attack, true);
            var kuriboh = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
            var combo = PutInHand(e, p, CombinationAttack);
            Equip(e, kiryu, blade);
            Check("Combination Attack: not in the Main Phase (and its monster has not attacked)",
                !e.CanActivateSpellTrap(p, combo, fromHand: true), $"phase={e.Phase}");

            e.TryEnterBattlePhase(p);
            Settle(e);
            var beforeAttack = e.CanActivateSpellTrap(p, combo, fromHand: true);
            AttackUntilPick(e, blade, kuriboh);
            DrainCombat(e);
            var legal = e.CanActivateSpellTrap(p, combo, fromHand: true);
            Check("Combination Attack: legal in the Battle Phase once the equipped monster has attacked",
                !beforeAttack && legal && o.Graveyard.Contains(kuriboh) && blade.AttacksDeclaredThisTurn == 1 &&
                !e.CanAttack(p, blade),
                $"before={beforeAttack} legal={legal} kuribohGy={o.Graveyard.Contains(kuriboh)} attacks={blade.AttacksDeclaredThisTurn}");

            var used = e.TryActivateSpellTrap(p, combo, fromHand: true);
            for (var i = 0; i < 8 && !e.IsAwaitingEffectTarget && e.IsAwaitingResponse; i++) e.PassResponse();
            if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(blade)) e.TrySelectEffectTarget(blade);
            for (var i = 0; i < 8 && !e.IsAwaitingEffectTarget && e.IsAwaitingResponse; i++) e.PassResponse();
            var token = e.IsAwaitingEffectTarget
                ? e.PendingActivation.LegalTargets.FirstOrDefault(t => t?.Def?.level == 1 && t.CardId == 0)
                : null;
            var asked = token != null && e.TrySelectEffectTarget(token);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            Check("Combination Attack: the Union is Special Summoned in the position you choose (Defense)",
                used && asked && InMz(p, kiryu) && kiryu.FaceUp && kiryu.Position == BattlePosition.Defense &&
                kiryu.EquippedTo == null && blade.CurrentAtk == 1800 && p.Graveyard.Contains(combo),
                $"used={used} asked={asked} kiryuMz={InMz(p, kiryu)} pos={kiryu.Position} atk={blade.CurrentAtk}");

            var lp = o.LifePoints;
            var again = e.CanAttack(p, blade);
            AttackUntilPick(e, blade, null);
            DrainCombat(e);
            Check("Combination Attack: the monster that was equipped attacks again this turn",
                again && o.LifePoints == lp - 1800, $"again={again} lp {lp}->{o.LifePoints}");
        }
    }
}
