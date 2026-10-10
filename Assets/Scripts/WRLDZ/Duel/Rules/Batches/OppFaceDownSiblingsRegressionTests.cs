using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// v152's sibling templates: "target 1 monster your opponent controls" (and the pre-PSCT "1 monster on your opponent's
    /// side of the field") includes face-down monsters, but six templates still emitted the face-up-only OppFaceUpMonsters:
    /// IgnitionTemplates.ParseTarget (Guardian Ceal), MonsterTriggerTemplates RxFlipBounceOpp (Gravekeeper's Guard, Gale
    /// Lizard) / RxFlipSummonDestroyOpp (Medusa Worm) / RxFlipSummonBounceOpp (Guardian Statue), CardTextEffectCompiler
    /// RxFlipDestroyOppMonster (Old Vindictive Magician, Night Assailant) and AdvancedEffectTemplates RxCoin3Destroy (Barrel
    /// Dragon) / RxEquipTargetToThis (Relinquished, Thousand-Eyes Restrict). Their Lua targets any opponent monster
    /// (SelectTarget(aux.TRUE / nil / Card.IsAbleToHand / Card.IsAbleToChangeControler, 0, LOCATION_MZONE)); they now
    /// compile to OppMonsters. A printed "face-up", or a target picked by its ATK (Armed Dragon LV5: Lua dfilter IsFaceup),
    /// stays face-up only.
    /// Relinquished / Thousand-Eyes Restrict (OCG rulings): a face-down Defense Position monster can be targeted and
    /// equipped; it stays face-down in the Spell &amp; Trap Zone and this card's ATK/DEF become 0 (Ignis s.atkval:
    /// IsFacedown() → 0). FieldSpellEffects.ApplyEquipStats now gives 0/0 for a face-down absorbed monster.
    /// </summary>
    public static class OppFaceDownSiblingsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int MedusaWorm = 2694423;
        const int GuardianCeal = 10755153;
        const int NightAssailant = 16226786;
        const int GravekeepersGuard = 37101832;
        const int OldVindictiveMagician = 45141844;
        const int ThousandEyesRestrict = 63519819;
        const int Relinquished = 64631466;
        const int GuardianStatue = 75209824;
        const int GaleLizard = 77491079;
        const int BarrelDragon = 81480460;
        const int BarrelDragonAlt = 81480461;
        const int ArmedDragonLv5 = 46384672;
        const int AxeOfDespair = 40619825;
        const int BlueEyes = 89631139;            // Normal 3000/2500
        const int GiantSoldierOfStone = 13039848; // Normal 1300/2000
        const int GaiaTheFierceKnight = 6368038;  // Normal 2300/2100
        const int CelticGuardian = 91152256;      // Normal 1400/1200

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("OppFaceDownSiblings: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            FlipDuels(db, pDeck, aDeck, Check);
            IgnitionDuels(db, pDeck, aDeck, Check);
            AbsorbDuels(db, pDeck, aDeck, Check);
            FaceUpOnlyControls(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            void Shape(int id, string label, CardEffectStatusKind status, Func<EffectClause, bool> clause)
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check($"{label}: targets OppMonsters (face-down too), status {status}",
                    prog != null && st == status && (status != CardEffectStatusKind.Implemented || prog.FullyCompiled) &&
                    prog.ClauseList.Exists(c => c != null && c.RequiresTargetChoice && c.Zone == EffectZoneFilter.OppMonsters &&
                                                !c.RequiresTargetFaceUp && clause(c)) &&
                    !prog.ClauseList.Exists(c => c != null && c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                    $"status={st} {Describe(prog)}");
            }

            Shape(MedusaWorm, "Medusa Worm (Flip Summoned: destroy)", CardEffectStatusKind.Implemented,
                c => c.Timing == EffectTiming.ThisCardSummoned && c.RequiresThisFlipSummoned &&
                     c.Action == EffectActionKind.Destroy);
            Shape(GuardianStatue, "Guardian Statue (Flip Summoned: return to hand)", CardEffectStatusKind.Implemented,
                c => c.Timing == EffectTiming.ThisCardSummoned && c.RequiresThisFlipSummoned &&
                     c.Action == EffectActionKind.ReturnToHand);
            Shape(OldVindictiveMagician, "Old Vindictive Magician (FLIP: destroy)", CardEffectStatusKind.Implemented,
                c => c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.Destroy);
            Shape(NightAssailant, "Night Assailant (FLIP: destroy)", CardEffectStatusKind.Stub,
                c => c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.Destroy);
            Shape(GravekeepersGuard, "Gravekeeper's Guard (FLIP: return to hand)", CardEffectStatusKind.Implemented,
                c => c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.ReturnToHand);
            Shape(GaleLizard, "Gale Lizard (FLIP: return to hand)", CardEffectStatusKind.Implemented,
                c => c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.ReturnToHand);
            Shape(GuardianCeal, "Guardian Ceal (ignition destroy)", CardEffectStatusKind.Implemented,
                c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy);
            foreach (var id in new[] { BarrelDragon, BarrelDragonAlt })
                Shape(id, $"Barrel Dragon {id} (3 coins, destroy)", CardEffectStatusKind.Implemented,
                    c => c.Action == EffectActionKind.CoinTossNDestroyIfHeads && c.CoinCount == 3 && c.Amount == 2 &&
                         c.OncePerTurn);
            Shape(Relinquished, "Relinquished (equip that target to this card)", CardEffectStatusKind.Implemented,
                c => c.Action == EffectActionKind.EquipTargetToThis && c.OncePerTurn);
            Shape(ThousandEyesRestrict, "Thousand-Eyes Restrict (equip that target to this card)",
                CardEffectStatusKind.Structural,
                c => c.Action == EffectActionKind.EquipTargetToThis && c.OncePerTurn);

            // RxFlipBounceOpp's "Attack Position" variant (no card in the pool prints it): face-up Attack Position only.
            {
                var prog = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 990153901, name = "ARGON OppFaceDownSiblings synthetic", type = "Flip Effect Monster",
                    race = "Warrior", attribute = "EARTH", level = 4, atk = 1000, def = 1000,
                    desc = "FLIP: Target 1 Attack Position monster your opponent controls; return that target to the hand."
                });
                Check("Synthetic \"FLIP: Target 1 Attack Position monster your opponent controls\" → OppAttackPositionMonsters",
                    prog != null && prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                                                c.Action == EffectActionKind.ReturnToHand &&
                                                                c.Zone == EffectZoneFilter.OppAttackPositionMonsters),
                    Describe(prog));
            }
        }

        // ───────────────────────────── FLIP / Flip Summon triggers ─────────────────────────────

        static void FlipDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Each card, Flip Summoned: the target prompt offers the opponent's Set monster, which is hit, and only it.
            foreach (var (id, label, dest) in new[]
                     {
                         (OldVindictiveMagician, "Old Vindictive Magician", "O.gy"),
                         (MedusaWorm, "Medusa Worm", "O.gy"),
                         (GravekeepersGuard, "Gravekeeper's Guard", "O.hand"),
                         (GaleLizard, "Gale Lizard", "O.hand"),
                         (GuardianStatue, "Guardian Statue", "O.hand"),
                     })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var me = PlaceMonster(e, p, id, 0, BattlePosition.Defense, false);
                var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(p, me) && me.FaceUp;
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check($"{label}: the target prompt offers the opponent's face-down monster",
                    flipped && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(set) && legal.Contains(gaia),
                    $"flipped={flipped} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, set);
                Check($"{label}: the targeted face-down monster goes to {dest}, and only it",
                    ok && Where(e, set) == dest && Where(e, gaia) == "O.mz" && Where(e, me) == "P.mz",
                    $"ok={ok} set={Where(e, set)} gaia={Where(e, gaia)} me={Where(e, me)}");
            }

            // With only a Set monster on the opponent's field the trigger still has a target (before: none).
            foreach (var (id, label, dest) in new[]
                     {
                         (OldVindictiveMagician, "Old Vindictive Magician", "O.gy"),
                         (GaleLizard, "Gale Lizard", "O.hand"),
                     })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var me = PlaceMonster(e, p, id, 0, BattlePosition.Defense, false);
                var set = PlaceMonster(e, e.Opponent, CelticGuardian, 3, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, me) && me.FaceUp;
                // A lone legal target may resolve with no prompt.
                if (e.IsAwaitingEffectTarget || e.IsAwaitingResponse) Drive(e, set);
                Check($"{label}: with only a Set monster on the opponent's field, it is hit ({dest})",
                    flipped && Where(e, set) == dest && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"flipped={flipped} set={Where(e, set)}");
            }
        }

        // ───────────────────────────── ignition: Guardian Ceal, Barrel Dragon ─────────────────────────────

        static void IgnitionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Guardian Ceal with an Equip Spell on it destroys the opponent's Set monster. (Only the target is checked
            // here: the "send 1 Equip Spell equipped to this card" cost is a separate residual.)
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var ceal = PlaceMonster(e, p, GuardianCeal, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                var axe = PutInHand(e, p, AxeOfDespair);
                var equipped = e.TryActivateSpellTrap(p, axe, fromHand: true);
                if (e.IsAwaitingEffectTarget) equipped &= e.TrySelectEffectTarget(ceal);
                if (e.IsAwaitingResponse) e.PassResponse();
                equipped &= axe.EquippedTo == ceal;
                var activated = equipped && e.TryActivateSpellTrap(p, ceal, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Guardian Ceal: the target prompt offers the opponent's face-down monster",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(set) && legal.Contains(gaia),
                    $"equipped={equipped} activated={activated} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, set);
                Check("Guardian Ceal: destroys the targeted face-down monster, and only it",
                    ok && Where(e, set) == "O.gy" && Where(e, gaia) == "O.mz" && Where(e, ceal) == "P.mz",
                    $"ok={ok} set={Where(e, set)} gaia={Where(e, gaia)} ceal={Where(e, ceal)}");
            }

            // Barrel Dragon: 3 heads destroys the targeted Set monster.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var drag = PlaceMonster(e, p, BarrelDragon, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, GiantSoldierOfStone, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, o, GaiaTheFierceKnight, 1, BattlePosition.Attack, true);
                for (var i = 0; i < 3; i++) e.Rng.QueueCoin(true);
                var activated = e.TryActivateSpellTrap(p, drag, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Barrel Dragon: the target prompt offers the opponent's face-down monster",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(set) && legal.Contains(gaia),
                    $"activated={activated} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, set);
                Check("Barrel Dragon: 3 heads destroys the targeted face-down monster, and only it",
                    ok && Where(e, set) == "O.gy" && Where(e, gaia) == "O.mz",
                    $"ok={ok} set={Where(e, set)} gaia={Where(e, gaia)}");
            }

            // Barrel Dragon with only a Set monster on the opponent's field can be activated (before: no target).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var drag = PlaceMonster(e, p, BarrelDragon, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, e.Opponent, CelticGuardian, 2, BattlePosition.Defense, false);
                for (var i = 0; i < 3; i++) e.Rng.QueueCoin(true);
                var can = e.CanActivateSpellTrap(p, drag, fromHand: false);
                var activated = can && e.TryActivateSpellTrap(p, drag, fromHand: false);
                if (e.IsAwaitingEffectTarget || e.IsAwaitingResponse) Drive(e, set);
                Check("Barrel Dragon: with only a Set monster on the opponent's field, it activates and destroys it",
                    can && activated && Where(e, set) == "O.gy" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"can={can} activated={activated} set={Where(e, set)}");
            }
        }

        // ───────────────────────────── Relinquished / Thousand-Eyes Restrict ─────────────────────────────

        static void AbsorbDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Thousand-Eyes Restrict shares the template (compile shape above), but the engine treats it as an effectless
            // Extra Deck monster (Structural), so only Relinquished can be played here.
            {
                const string label = "Relinquished";
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var host = PlaceMonster(e, p, Relinquished, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Defense, false);
                var up = PlaceMonster(e, o, CelticGuardian, 1, BattlePosition.Attack, true);
                var activated = e.TryActivateSpellTrap(p, host, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check($"{label}: the target prompt offers the opponent's face-down monster",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(set) && legal.Contains(up),
                    $"activated={activated} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, set);
                FieldSpellEffects.RefreshBoard(e);
                // OCG ruling: equipped with a face-down Defense Position monster, its ATK and DEF become 0; the monster
                // stays face-down (Ignis s.atkval: IsFacedown() → 0).
                Check($"{label}: equips the face-down monster, which stays face-down; ATK/DEF become 0",
                    ok && set.EquippedTo == host && host.Equips.Contains(set) && !set.FaceUp &&
                    TextEffectRuntime.AbsorbedEquipOf(host) == set && !o.TryFindMonster(set, out _) &&
                    host.CurrentAtk == 0 && host.CurrentDef == 0 && Where(e, up) == "O.mz",
                    $"ok={ok} eq={set.EquippedTo?.Name} faceUp={set.FaceUp} atk={host.CurrentAtk} def={host.CurrentDef} " +
                    $"set={Where(e, set)} up={Where(e, up)}");
                Check($"{label}: max. 1 — cannot absorb again while the face-down monster is equipped",
                    !e.CanActivateSpellTrap(p, host, fromHand: false));
            }

            // Relinquished with a face-down absorbed monster: battle still destroys that monster instead (Ignis repval,
            // REASON_BATTLE), and Relinquished is then 0/0 with nothing absorbed.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var rel = PlaceMonster(e, p, Relinquished, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, o, GaiaTheFierceKnight, 0, BattlePosition.Defense, false);
                var bewd = PlaceMonster(e, o, BlueEyes, 1, BattlePosition.Attack, true);
                bewd.SummonedThisTurn = false;
                var absorbed = e.TryActivateSpellTrap(p, rel, fromHand: false) && Drive(e, set) && set.EquippedTo == rel;
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryEndTurnSafe(e.TurnPlayer);
                if (e.IsAwaitingResponse) e.PassResponse();
                if (e.TurnPlayer == o && e.Phase == DuelPhase.Main1) e.TryEnterBattlePhase(o);
                if (e.IsAwaitingResponse) e.PassResponse();
                DrainCombat(e);
                var hit = e.Phase == DuelPhase.Battle && e.CanAttack(o, bewd) && e.TryAttack(o, bewd, rel);
                DrainCombat(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Relinquished: a face-down absorbed monster is destroyed instead by battle",
                    absorbed && hit && p.TryFindMonster(rel, out _) && set.EquippedTo == null && !rel.Equips.Contains(set) &&
                    o.Graveyard.Contains(set) && TextEffectRuntime.AbsorbedEquipOf(rel) == null,
                    $"absorbed={absorbed} hit={hit} phase={e.Phase} turn={(e.TurnPlayer == o ? "O" : "P")} " +
                    $"relOn={p.TryFindMonster(rel, out _)} set={Where(e, set)} eq={set.EquippedTo?.Name}");
            }
        }

        // ───────────────────────────── face-up-only controls ─────────────────────────────

        static void FaceUpOnlyControls(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Lua filters with IsFaceup: these keep OppFaceUpMonsters.
            foreach (var (id, label) in new[]
                     {
                         (ArmedDragonLv5, "Armed Dragon LV5"),
                         (37620434, "Shadow Tamer"),
                         (37744402, "Wynn the Wind Charmer"),
                         (37970940, "Aussa the Earth Charmer"),
                         (759393, "Hiita the Fire Charmer"),
                         (74364659, "Eria the Water Charmer"),
                         (63018132, "Dragon Manipulator"),
                         (87910978, "Brain Control"),
                         (98045062, "Enemy Controller"),
                     })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"{label}: face-up target stays OppFaceUpMonsters (Lua IsFaceup)",
                    prog != null && prog.ClauseList.Exists(c => c != null && c.RequiresTargetChoice &&
                                                                c.Zone == EffectZoneFilter.OppFaceUpMonsters) &&
                    !prog.ClauseList.Exists(c => c != null && c.Zone == EffectZoneFilter.OppMonsters),
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(ArmedDragonLv5));
                Check("Armed Dragon LV5: the ATK-picked destroy keeps RequiresAtkLeqCost on OppFaceUpMonsters",
                    prog != null && prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Destroy &&
                                                                c.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                                                                c.RequiresAtkLeqCost && c.RequiresSendHandToGy),
                    Describe(prog));
            }

            // Armed Dragon LV5 cannot destroy a Set monster (Lua dfilter IsFaceup); a face-up one it can.
            foreach (var faceUp in new[] { false, true })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var lv5 = PlaceMonster(e, p, ArmedDragonLv5, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, CelticGuardian, 0, faceUp ? BattlePosition.Attack : BattlePosition.Defense,
                    faceUp);
                PutInHand(e, p, BlueEyes);
                var can = e.CanActivateSpellTrap(p, lv5, fromHand: false);
                Check(faceUp
                        ? "Armed Dragon LV5: a face-up 1400 monster is a legal target (control)"
                        : "Armed Dragon LV5: cannot be activated when the opponent's only monster is face-down",
                    can == faceUp, $"can={can}");
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

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/faceUp={c.RequiresTargetFaceUp}/tgt={c.RequiresTargetChoice}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
