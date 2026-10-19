using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "RevivalEquipsAndGearfried": Fulfillment of the Contract, Re-Fusion, Release Restraint, Gearfried the
    /// Swordmaster and Brain Jacker (<see cref="RevivalEquipsAndGearfriedAtoms"/>, <see cref="RevivalEquipsAndGearfriedRuntime"/>).
    /// </summary>
    public static class RevivalEquipsAndGearfriedRegressionTests
    {
        const int Fulfillment = 48206762;
        const int ReFusion = 74694807;
        const int ReleaseRestraint = 75417459;
        const int Swordmaster = 57046845;
        const int BrainJacker = 40267580;

        const int IronKnight = 423705;          // Gearfried the Iron Knight
        const int PrematureBurial = 70828912;
        const int Dedication = 69542930;        // Dedication through Light and Darkness
        const int CallOfTheHaunted = 97077563;
        const int MonsterReborn = 83764718;
        const int Mst = 5318639;
        const int LegendarySword = 61854111;    // Warrior equip
        const int BerserkDragon = 85605684;     // hard Nomi ("… and cannot be Special Summoned by other ways")
        const int MaskedHeroVapor = 10920352;   // Extra Deck "Must be Special Summoned with "Mask Change"."

        const int BlackLusterSoldier = 5405694; // Ritual Monster
        const int GaiaDragonChampion = 66889139; // Fusion Monster
        const int Celtic = 91152256;            // EARTH Warrior Lv4 1400/1200
        const int SummonedSkull = 70781052;     // DARK Fiend Lv6 2500/1200
        const int MysticalElf = 15025844;       // LIGHT Spellcaster Lv4 800/2000

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("RevivalEquipsAndGearfried: " + name, ok, detail);

            Guard(Check, "compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "Fulfillment of the Contract", () => FulfillmentDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Re-Fusion", () => ReFusionDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Release Restraint", () => ReleaseRestraintDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Gearfried the Swordmaster", () => SwordmasterDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Brain Jacker", () => BrainJackerDuels(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk Check, string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check($"{label}: no exception", false, ex.GetType().Name + ": " + ex.Message + " @ " +
                                                         (ex.StackTrace ?? "").Split('\n').FirstOrDefault());
            }
        }

        // ───────────────────────── helpers ─────────────────────────

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
            Wipe(engine);
            return engine;
        }

        static void Wipe(DuelEngine engine)
        {
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
                who.LifePoints = 8000;
            }
        }

        static CardInstance ToGy(DuelEngine engine, DuelistState who, int id, bool properlySummoned = false)
        {
            var c = engine.CreateCardInstance(id);
            c.ProcedureCompleted = properlySummoned;
            who.Graveyard.Add(c);
            return c;
        }

        static void Settle(DuelEngine engine)
        {
            DrainCombat(engine);
            FieldSpellEffects.RefreshBoard(engine);
        }

        /// <summary>Activate <paramref name="card"/> (from the hand, or Set when <paramref name="fromHand"/> is false) and
        /// pick <paramref name="target"/> if asked.</summary>
        static bool ActivateOn(DuelEngine engine, CardInstance card, CardInstance target, out List<CardInstance> offered,
            bool fromHand = true)
        {
            offered = null;
            var p = engine.Player;
            if (!engine.TryActivateSpellTrap(p, card, fromHand)) return false;
            if (engine.IsAwaitingEffectTarget)
            {
                offered = engine.PendingActivation.LegalTargets.ToList();
                if (!engine.TrySelectEffectTarget(target)) return false;
            }

            Settle(engine);
            return true;
        }

        static string Names(IEnumerable<CardInstance> list) =>
            list == null ? "(no prompt)" : string.Join(",", list.Select(c => c?.Name));

        static CompiledCardProgram CompileText(string text, string type, string race) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990210, name = "Near-miss Card", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = race, desc = text, atk = 1000, def = 1000, level = 4, attribute = "DARK"
            });

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id, out bool implemented)
            {
                var def = db.Get(id);
                implemented = def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                return def != null ? CardTextEffectCompiler.Compile(def) : null;
            }

            foreach (var (id, ritual) in new[] { (Fulfillment, true), (ReFusion, false) })
            {
                var prog = P(id, out var impl);
                var c = prog?.ClauseList?.FirstOrDefault();
                Check($"{(ritual ? "Fulfillment of the Contract" : "Re-Fusion")}: pay 800 LP, target 1 " +
                      $"{(ritual ? "Ritual" : "Fusion")} Monster in your GY, Special Summon it face-up (either position) and " +
                      "equip it; banished only when this card is destroyed",
                    prog != null && prog.FullyCompiled && impl && prog.ClauseList.Count == 1 && c != null &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.SpecialSummonFromGy &&
                    c.Zone == EffectZoneFilter.ControllerGyMonsters && c.RequiresTargetChoice && c.PayLpAmount == 800 &&
                    c.RequiresRitualMonsterTarget == ritual && c.RequiresFusionMonsterTarget == !ritual &&
                    c.BanishHostWhenThisLeaves && c.BanishHostOnlyIfThisDestroyed && !c.DestroyHostWhenThisLeaves &&
                    !c.SummonInAttack && !c.SummonInDefense && c.StaysOnField &&
                    !prog.ClauseList.Exists(x => x != null && x.Action == EffectActionKind.EquipThisToTarget),
                    $"full={prog?.FullyCompiled} impl={impl} n={prog?.ClauseList?.Count} action={c?.Action} zone={c?.Zone}");
            }

            // Premature Burial keeps its own shape (destroy, Attack Position).
            {
                var prog = P(PrematureBurial, out var impl);
                var c = prog?.ClauseList?.FirstOrDefault(x => x?.Action == EffectActionKind.SpecialSummonFromGy);
                Check("Premature Burial unchanged: destroy the equipped monster, Summon in Attack Position",
                    prog != null && prog.FullyCompiled && impl && c != null && c.DestroyHostWhenThisLeaves &&
                    c.DestroyHostOnlyIfThisDestroyed && c.SummonInAttack && !c.BanishHostWhenThisLeaves &&
                    !c.RequiresRitualMonsterTarget && !c.RequiresFusionMonsterTarget,
                    $"full={prog?.FullyCompiled} impl={impl}");
            }

            // Near misses: another monster kind, or "destroy" instead of "banish", never take the revival atom.
            {
                var synchro = CompileText("Activate this card by paying 800 LP, then target 1 Synchro Monster in your GY; " +
                                          "Special Summon it and equip it with this card. When this card is destroyed, " +
                                          "banish the equipped monster.", "Spell Card", "Equip");
                var destroy = CompileText("Activate this card by paying 800 LP, then target 1 Ritual Monster in your GY; " +
                                          "Special Summon it and equip it with this card. When this card is destroyed, " +
                                          "destroy the equipped monster.", "Spell Card", "Equip");
                Check("Near miss: a Synchro revival or a 'destroy the equipped monster' rider never takes the revival atom",
                    synchro != null && !synchro.FullyCompiled && destroy != null &&
                    !destroy.ClauseList.Exists(x => x != null && (x.BanishHostWhenThisLeaves || x.RequiresRitualMonsterTarget)),
                    $"synchro={synchro?.FullyCompiled} destroy={destroy?.FullyCompiled}");
            }

            {
                var prog = P(ReleaseRestraint, out var impl);
                var c = prog?.ClauseList?.FirstOrDefault();
                var ded = P(Dedication, out var dedImpl)?.ClauseList?.FirstOrDefault();
                Check("Release Restraint: Tribute 1 \"Gearfried the Iron Knight\"; Special Summon 1 \"Gearfried the " +
                      "Swordmaster\" from the hand or Deck (not the GY); Dedication keeps its GY",
                    prog != null && prog.FullyCompiled && impl && prog.ClauseList.Count == 1 && c != null &&
                    c.Action == EffectActionKind.SpecialSummonNamed && c.NamedCard == "Gearfried the Swordmaster" &&
                    c.FromHand && c.FromDeck && !c.FromGrave && c.RequiresTributeCount == 1 &&
                    c.TributeNamedCard == "Gearfried the Iron Knight" &&
                    dedImpl && ded != null && ded.FromGrave && ded.FromHand && ded.FromDeck,
                    $"full={prog?.FullyCompiled} impl={impl} named={c?.NamedCard} grave={c?.FromGrave} ded={ded?.FromGrave}");
            }

            {
                var prog = P(Swordmaster, out var impl);
                var c = prog?.ClauseList?.FirstOrDefault();
                var swordDef = db.Get(Swordmaster);
                Check("Gearfried the Swordmaster: the summon sentences are structural (proper summon = Release Restraint), the " +
                      "equip trigger is its one clause",
                    prog != null && prog.FullyCompiled && impl && prog.ClauseList.Count == 1 && c != null &&
                    c.Action == EffectActionKind.DestroyOppMonsterEachTimeThisIsEquipped &&
                    TributeSummonsRuntime.ProcedureCardName(swordDef) == "Release Restraint" &&
                    TributeSummonsRuntime.ProcedureCardName(db.Get(BerserkDragon)) == null &&
                    TributeSummonsRuntime.ProcedureCardName(db.Get(MaskedHeroVapor)) == null &&
                    PsctGrammar.BlocksNormalSummonOrSet(swordDef.desc),
                    $"full={prog?.FullyCompiled} impl={impl} n={prog?.ClauseList?.Count} " +
                    $"proc={TributeSummonsRuntime.ProcedureCardName(swordDef)} " +
                    $"berserk={TributeSummonsRuntime.ProcedureCardName(db.Get(BerserkDragon))} " +
                    $"vapor={TributeSummonsRuntime.ProcedureCardName(db.Get(MaskedHeroVapor))}");
                var near = CompileText("Each time an Equip Card(s) is equipped to this card: Target 1 monster on the field; " +
                                       "destroy that target.", "Effect Monster", "Warrior");
                Check("Near miss: 'target 1 monster on the field' does not take the Swordmaster trigger",
                    near != null && !near.ClauseList.Exists(x =>
                        x != null && x.Action == EffectActionKind.DestroyOppMonsterEachTimeThisIsEquipped),
                    $"full={near?.FullyCompiled}");
            }

            {
                var prog = P(BrainJacker, out var impl);
                var flip = prog?.ClauseList?.FirstOrDefault(x => x?.Timing == EffectTiming.Flip);
                var standby = prog?.ClauseList?.FirstOrDefault(x => x?.Timing == EffectTiming.StandbyPhase);
                Check("Brain Jacker: FLIP equip to a face-up opponent's monster + take control; the opponent's Standby gain " +
                      "applies only while it is an Equip Card",
                    prog != null && prog.FullyCompiled && impl && prog.ClauseList.Count == 2 && flip != null &&
                    flip.Action == EffectActionKind.EquipThisFromFieldToTargetTakeControl &&
                    flip.Zone == EffectZoneFilter.OppFaceUpMonsters && flip.RequiresTargetChoice && flip.TakeControlOfTarget &&
                    standby != null && standby.Action == EffectActionKind.GainLifePoints && standby.Amount == 500 &&
                    standby.Side == EffectSide.Opponent && standby.OpponentTurnOnly && standby.RequiresThisIsEquipCard,
                    $"full={prog?.FullyCompiled} impl={impl} n={prog?.ClauseList?.Count}");
                var lone = CompileText("Your opponent gains 500 Life Points during each of their Standby Phases.",
                    "Effect Monster", "Fiend");
                Check("Near miss: the Standby gain sentence alone does not compile to an equip-only gain",
                    lone != null && !lone.ClauseList.Exists(x => x != null && x.RequiresThisIsEquipCard),
                    $"full={lone?.FullyCompiled}");
            }
        }

        // ───────────────────────── Fulfillment of the Contract ─────────────────────────

        static void FulfillmentDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Only a properly summoned Ritual Monster is offered; it is Special Summoned and equipped; MST banishes it.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var b1 = ToGy(e, p, BlackLusterSoldier, properlySummoned: true);
                var b2 = ToGy(e, p, BlackLusterSoldier, properlySummoned: true);
                var raw = ToGy(e, p, BlackLusterSoldier);
                var celtic = ToGy(e, p, Celtic);
                var ful = PutInHand(e, p, Fulfillment);
                var act = ActivateOn(e, ful, b2, out var offered);
                Check("Fulfillment: offers the properly summoned Ritual Monsters only; pays 800 LP, Special Summons and equips",
                    reached && act && offered != null && offered.Count == 2 && offered.Contains(b1) && offered.Contains(b2) &&
                    !offered.Contains(raw) && !offered.Contains(celtic) && p.TryFindMonster(b2, out _) && b2.FaceUp &&
                    ful.EquippedTo == b2 && b2.Equips.Contains(ful) && p.TryFindSpellTrap(ful, out _) &&
                    p.LifePoints == 7200 && p.Graveyard.Contains(b1),
                    $"act={act} offered={Names(offered)} onField={p.TryFindMonster(b2, out _)} eq={ful.EquippedTo?.Name} " +
                    $"lp={p.LifePoints}");

                var mst = PutInHand(e, p, Mst);
                var destroyed = ActivateOn(e, mst, ful, out _);
                Check("Fulfillment: destroyed (MST) — the equipped monster is banished",
                    destroyed && p.Graveyard.Contains(ful) && p.Banished.Contains(b2) && !p.TryFindMonster(b2, out _),
                    $"destroyed={destroyed} fulGy={p.Graveyard.Contains(ful)} banished={p.Banished.Contains(b2)} " +
                    $"gy={p.Graveyard.Contains(b2)}");
            }

            // Returned to the hand (not destroyed): the monster stays.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var bls = ToGy(e, p, BlackLusterSoldier, properlySummoned: true);
                var ful = PutInHand(e, p, Fulfillment);
                var act = ActivateOn(e, ful, bls, out _);
                var bounced = act && e.ReturnCardToHand(ful);
                Settle(e);
                Check("Fulfillment: returned to the hand — the monster stays on the field",
                    reached && act && bounced && p.Hand.Contains(ful) && p.TryFindMonster(bls, out _) &&
                    !p.Banished.Contains(bls),
                    $"act={act} bounced={bounced} onField={p.TryFindMonster(bls, out _)}");
            }

            // No properly summoned Ritual Monster (only a Ritual never Ritual Summoned, and a Normal Monster): no activation.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, BlackLusterSoldier);
                ToGy(e, p, Celtic);
                ToGy(e, e.Opponent, BlackLusterSoldier, properlySummoned: true);
                var ful = PutInHand(e, p, Fulfillment);
                var can = e.CanActivateSpellTrap(p, ful, fromHand: true);
                Check("Fulfillment: no properly summoned Ritual Monster in your GY (the opponent's does not count) — " +
                      "cannot be activated",
                    reached && !can && p.LifePoints == 8000, $"can={can}");
            }
        }

        // ───────────────────────── Re-Fusion ─────────────────────────

        static void ReFusionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var gaia = ToGy(e, p, GaiaDragonChampion, properlySummoned: true);
                var bls = ToGy(e, p, BlackLusterSoldier, properlySummoned: true);
                var rf = PutInHand(e, p, ReFusion);
                var act = ActivateOn(e, rf, gaia, out var offered);
                Check("Re-Fusion: a properly summoned Fusion Monster (not the Ritual) is Special Summoned and equipped",
                    reached && act && (offered == null || (offered.Count == 1 && offered[0] == gaia)) &&
                    p.TryFindMonster(gaia, out _) && rf.EquippedTo == gaia && p.Graveyard.Contains(bls) &&
                    p.LifePoints == 7200,
                    $"act={act} offered={Names(offered)} onField={p.TryFindMonster(gaia, out _)} lp={p.LifePoints}");

                var mst = PutInHand(e, p, Mst);
                var destroyed = ActivateOn(e, mst, rf, out _);
                Check("Re-Fusion: destroyed — the Fusion Monster is banished",
                    destroyed && p.Graveyard.Contains(rf) && p.Banished.Contains(gaia) && !p.TryFindMonster(gaia, out _),
                    $"destroyed={destroyed} banished={p.Banished.Contains(gaia)}");
            }

            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, GaiaDragonChampion);
                var rf = PutInHand(e, p, ReFusion);
                var can = e.CanActivateSpellTrap(p, rf, fromHand: true);
                Check("Re-Fusion: a Fusion Monster never Fusion Summoned is not a legal target — cannot be activated",
                    reached && !can, $"can={can}");
            }
        }

        // ───────────────────────── Release Restraint + the Swordmaster's summon rules ─────────────────────────

        static bool ActivateReleaseRestraint(DuelEngine e, CardInstance rr, CardInstance knight)
        {
            if (!e.TryActivateSpellTrap(e.Player, rr, fromHand: true)) return false;
            for (var i = 0; i < 3 && e.IsAwaitingEffectTarget; i++)
                e.TrySelectEffectTarget(knight);
            Settle(e);
            return true;
        }

        static void ReleaseRestraintDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // From the hand: the Iron Knight is Tributed, the Swordmaster is Special Summoned and properly summoned.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var knight = PlaceMonster(e, p, IronKnight, 0, BattlePosition.Attack, true);
                var sword = PutInHand(e, p, Swordmaster);
                var normal = e.TryNormalSummon(p, sword, asSet: false);
                var rr = PutInHand(e, p, ReleaseRestraint);
                var act = ActivateReleaseRestraint(e, rr, knight);
                Check("Release Restraint: Tributes the Iron Knight and Special Summons the Swordmaster from the hand " +
                      "(it cannot be Normal Summoned)",
                    reached && !normal && act && p.Graveyard.Contains(knight) && p.TryFindMonster(sword, out _) &&
                    sword.FaceUp && sword.ProcedureCompleted && p.Graveyard.Contains(rr),
                    $"normal={normal} act={act} knightGy={p.Graveyard.Contains(knight)} " +
                    $"onField={p.TryFindMonster(sword, out _)} proc={sword.ProcedureCompleted}");
            }

            // From the Deck.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var knight = PlaceMonster(e, p, IronKnight, 0, BattlePosition.Attack, true);
                p.Deck.Insert(0, Swordmaster);
                var before = p.Deck.Count;
                var rr = PutInHand(e, p, ReleaseRestraint);
                var act = ActivateReleaseRestraint(e, rr, knight);
                var sword = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == Swordmaster);
                Check("Release Restraint: Special Summons the Swordmaster from the Deck",
                    reached && act && p.Graveyard.Contains(knight) && sword != null && sword.ProcedureCompleted &&
                    p.Deck.Count == before - 1,
                    $"act={act} sword={sword?.Name} deck {before} -> {p.Deck.Count}");
            }

            // No Iron Knight: no activation (the cost is a named Tribute).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PutInHand(e, p, Swordmaster);
                var rr = PutInHand(e, p, ReleaseRestraint);
                var can = e.CanActivateSpellTrap(p, rr, fromHand: true);
                Check("Release Restraint: no \"Gearfried the Iron Knight\" to Tribute — cannot be activated",
                    reached && !can, $"can={can}");
            }

            // Revival limit: Monster Reborn cannot revive a Swordmaster that was never properly summoned; it can once it was.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, Swordmaster);
                var reborn = PutInHand(e, p, MonsterReborn);
                var can = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                p.Graveyard.Clear();
                var proper = ToGy(e, p, Swordmaster, properlySummoned: true);
                var act = ActivateOn(e, reborn, proper, out _);
                Check("Gearfried the Swordmaster: not revivable before it was properly summoned; revivable after",
                    reached && !can && act && p.TryFindMonster(proper, out _),
                    $"canRaw={can} act={act} onField={p.TryFindMonster(proper, out _)}");
            }
        }

        // ───────────────────────── Gearfried the Swordmaster ─────────────────────────

        static void SwordmasterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // An Equip Spell is equipped to it: target 1 monster the opponent controls (face-down offered too) and destroy it.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sword = PlaceMonster(e, p, Swordmaster, 0, BattlePosition.Attack, true);
                sword.ProcedureCompleted = true;
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, false);
                var ls = PutInHand(e, p, LegendarySword);
                var act = ActivateOn(e, ls, sword, out _);
                List<CardInstance> offered = null;
                if (e.IsAwaitingEffectTarget)
                {
                    offered = e.PendingActivation.LegalTargets.ToList();
                    e.TrySelectEffectTarget(elf);
                }

                Settle(e);
                Check("Swordmaster: equipped with Legendary Sword — its trigger offers both opponent's monsters (face-down " +
                      "too) and destroys the chosen one",
                    reached && act && ls.EquippedTo == sword && offered != null && offered.Contains(skull) &&
                    offered.Contains(elf) && o.Graveyard.Contains(elf) && o.TryFindMonster(skull, out _),
                    $"act={act} eq={ls.EquippedTo?.Name} offered={Names(offered)} elfGy={o.Graveyard.Contains(elf)} " +
                    $"skull={o.TryFindMonster(skull, out _)}");
            }

            // Premature Burial revives a properly summoned Swordmaster and equips it (Duel.Equip): the trigger destroys the
            // opponent's only monster (no prompt).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sword = ToGy(e, p, Swordmaster, properlySummoned: true);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var pb = PutInHand(e, p, PrematureBurial);
                var act = ActivateOn(e, pb, sword, out _);
                Settle(e);
                Check("Swordmaster: revived and equipped by Premature Burial — destroys the opponent's monster",
                    reached && act && p.TryFindMonster(sword, out _) && pb.EquippedTo == sword && o.Graveyard.Contains(skull),
                    $"act={act} onField={p.TryFindMonster(sword, out _)} skullGy={o.Graveyard.Contains(skull)}");
            }

            // Call of the Haunted is no Equip Card: no trigger.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sword = ToGy(e, p, Swordmaster, properlySummoned: true);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var coth = PlaceSetTrap(e, p, CallOfTheHaunted, 0);
                var act = ActivateOn(e, coth, sword, out _, fromHand: false);
                Settle(e);
                Check("Swordmaster: Special Summoned by Call of the Haunted (not an equip) — no trigger",
                    reached && act && p.TryFindMonster(sword, out _) && o.TryFindMonster(skull, out _),
                    $"act={act} onField={p.TryFindMonster(sword, out _)} skull={o.TryFindMonster(skull, out _)}");
            }
        }

        // ───────────────────────── Brain Jacker ─────────────────────────

        static void BrainJackerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Flip Summoned: it equips itself to the opponent's face-up monster and takes control; destroyed, control returns.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jack = PlaceMonster(e, p, BrainJacker, 0, BattlePosition.Defense, false);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(p, jack);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
                Settle(e);
                var atk = skull.CurrentAtk;
                Check("Brain Jacker: FLIP — equipped to the opponent's monster as an Equip Card, takes control of it",
                    reached && flipped && p.TryFindSpellTrap(jack, out _) && !p.TryFindMonster(jack, out _) &&
                    jack.EquippedTo == skull && skull.Equips.Contains(jack) && e.ControllerOf(skull) == p &&
                    skull.TakenByEquipControl && atk == 2500,
                    $"flipped={flipped} st={p.TryFindSpellTrap(jack, out _)} eq={jack.EquippedTo?.Name} " +
                    $"ctl={e.ControllerOf(skull)?.Name} atk={atk}");

                var mst = PutInHand(e, p, Mst);
                var destroyed = ActivateOn(e, mst, jack, out _);
                Check("Brain Jacker: destroyed — control of the monster returns",
                    destroyed && p.Graveyard.Contains(jack) && e.ControllerOf(skull) == o,
                    $"destroyed={destroyed} gy={p.Graveyard.Contains(jack)} ctl={e.ControllerOf(skull)?.Name}");
            }

            // The opponent's Standby Phase: they gain 500 while it is an Equip Card.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jack = PlaceMonster(e, p, BrainJacker, 0, BattlePosition.Defense, false);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.TryFlipSummon(p, jack);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
                Settle(e);
                var equipped = jack.EquippedTo == skull;
                var lp = o.LifePoints;
                var mine = p.LifePoints;
                e.TryEndTurnSafe(p);
                Settle(e);
                Check("Brain Jacker: during the opponent's Standby Phase they gain 500 LP (you do not)",
                    reached && equipped && e.TurnPlayer == o && o.LifePoints == lp + 500 && p.LifePoints <= mine,
                    $"equipped={equipped} turn={e.TurnPlayer?.Name} opp {lp} -> {o.LifePoints} me {mine} -> {p.LifePoints}");
            }

            // No face-up monster to steal: it stays a monster, and gives no LP during the opponent's Standby Phase.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jack = PlaceMonster(e, p, BrainJacker, 0, BattlePosition.Defense, false);
                PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, jack);
                Settle(e);
                var stays = p.TryFindMonster(jack, out _) && jack.EquippedTo == null && !e.IsAwaitingEffectTarget;
                var lp = o.LifePoints;
                e.TryEndTurnSafe(p);
                Settle(e);
                Check("Brain Jacker: no face-up target — it stays in the Monster Zone and the opponent gains nothing",
                    reached && flipped && stays && e.TurnPlayer == o && o.LifePoints == lp,
                    $"flipped={flipped} stays={stays} opp {lp} -> {o.LifePoints}");
            }

            // Attacked face-down and destroyed by that battle: it is not equipped (STATUS_BATTLE_DESTROYED).
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Wipe(e);
                var p = e.Player;
                var o = e.Opponent;
                var jack = PlaceMonster(e, p, BrainJacker, 0, BattlePosition.Defense, false);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var attacked = e.TryAttack(o, skull, jack);
                DrainCombat(e);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(skull);
                Settle(e);
                Check("Brain Jacker: destroyed by the battle that flipped it — no equip, no control change",
                    reached && attacked && p.Graveyard.Contains(jack) && e.ControllerOf(skull) == o && skull.Equips.Count == 0,
                    $"attacked={attacked} gy={p.Graveyard.Contains(jack)} ctl={e.ControllerOf(skull)?.Name}");
            }
        }
    }
}
