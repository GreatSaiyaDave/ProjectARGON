using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch v163 "ForcedActivationAndSubstitution": Spirit Elimination (the banish-from-GY substitution read by every
    /// banish-from-GY cost and effect) and Major Riot (the "returned to your hand by your opponent's effect" window, then
    /// both players' face-down hand Summons). Bait Doll stays fail-closed (no forced activation of a Set Trap inside a
    /// resolving effect).
    /// </summary>
    public static class ForcedActivationAndSubstitutionRegressionTests
    {
        const int SpiritElimination = 69832741;
        const int MajorRiot = 9074847;
        const int BaitDoll = 7165085;

        const int CompulsoryEvacuationDevice = 94192409;
        const int SoulRelease = 5758500;
        const int GravediggerGhoul = 82542267;
        const int BookOfLife = 2204140;
        const int FiendComedian = 81172176;
        const int SkullLair = 6733059;
        const int SoulDemolition = 76297408;
        const int SpiritOfFlames = 13522325;
        const int Bazoo = 40133511;
        const int Disappear = 24623598;
        const int WitchDoctorOfChaos = 75946257;
        const int PotOfGreed = 55144522;

        const int Celtic = 91152256;        // EARTH Warrior Lv4 1400/1200
        const int Gaia = 6368038;           // EARTH Warrior Lv7 2300/2100
        const int MysticalElf = 15025844;   // LIGHT Spellcaster Lv4 800/2000
        const int GiantSoldier = 13039848;  // EARTH Rock Lv3 1300/2000
        const int SummonedSkull = 70781052; // DARK Fiend Lv6 2500/1200
        const int BattleOx = 5053103;       // EARTH Beast-Warrior Lv4 1700/1000
        const int Kuriboh = 40640057;       // DARK Fiend Lv1
        const int HinotamaSoul = 96851799;  // FIRE Pyro Lv2
        const int FirePrincess = 64752646;  // FIRE Pyro Lv4
        const int ArmoredZombie = 20277860; // Zombie Lv3

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("ForcedActivationAndSubstitution: " + name, ok, detail);

            Guard(Check, "compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "Spirit Elimination", () => SpiritEliminationDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Major Riot", () => MajorRiotDuels(db, pDeck, aDeck, Check));
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

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 12 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static CardInstance ToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static string Pending(DuelEngine e)
        {
            var p = e.PendingActivation;
            return p == null
                ? $"no pending, response={e.PendingResponse?.Timing}"
                : $"kind={p.TargetKind} left={p.TargetPicksRemaining} [{string.Join(",", p.LegalTargets.Select(t => t?.Name))}]";
        }

        static bool OnField(DuelistState who, CardInstance c) => c != null && who.TryFindMonster(c, out _);

        static bool FaceDownDefense(DuelistState who, CardInstance c) =>
            OnField(who, c) && !c.FaceUp && c.Position == BattlePosition.Defense;

        static CompiledCardProgram CompileText(string text, string type) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990163, name = "Near-miss Card", type = type, frameType = type.StartsWith("Trap") ? "trap" : "spell",
                race = "Normal", desc = text
            });

        // ───────────────────────── compile shape ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var def = db.Get(SpiritElimination);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Spirit Elimination: Implemented — one Activate clause registering the turn-long substitution",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.BanishFieldMonstersInsteadOfGyMonstersThisTurn &&
                    c.Side == EffectSide.Controller && !c.RequiresTargetChoice,
                    $"status={CardEffectStatus.Classify(def)} fc={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
                var near = CompileText(def.desc.Replace("until the end of the turn", "until the end of the next turn"),
                    "Spell Card");
                Check("Near miss: a Spirit Elimination text lasting another turn does not compile",
                    near == null || !near.FullyCompiled ||
                    !near.ClauseList.Exists(x => x.Action == EffectActionKind.BanishFieldMonstersInsteadOfGyMonstersThisTurn));
            }

            {
                var def = db.Get(MajorRiot);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Major Riot: Implemented — window-only Activate clause, Level 4 or lower hand Summons",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown &&
                    c.ActivationCondition == MajorRiotRuntime.YourMonstersReturnedToHandByOpponent &&
                    c.SummonLevelMax == 4 && MajorRiotRuntime.AnswersWindow(prog),
                    $"status={CardEffectStatus.Classify(def)} fc={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
                var near = CompileText(def.desc.Replace("by your opponent's effect", "by a card effect"), "Trap Card");
                Check("Near miss: a Major Riot text answering any effect does not compile to its window",
                    near == null || !near.FullyCompiled || !MajorRiotRuntime.AnswersWindow(near));
            }

            {
                var def = db.Get(BaitDoll);
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Bait Doll: deferred — not FullyCompiled, not Implemented (forced Set-Trap activation)",
                    (prog == null || !prog.FullyCompiled) &&
                    CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    $"status={CardEffectStatus.Classify(def)}");
            }
        }

        // ───────────────────────── Spirit Elimination ─────────────────────────

        static void SpiritEliminationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Its activation applies to its activator for the turn; Soul Release then targets the monster you control
                // instead of the monster in your GY; your GY Spell and the opponent's GY monster stay targets.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var se = PutInHand(e, p, SpiritElimination);
                var act = e.TryActivateSpellTrap(p, se, fromHand: true);
                Settle(e);
                Check("Spirit Elimination: resolves, goes to the GY and applies to its activator only",
                    act && p.Graveyard.Contains(se) && SpiritEliminationRuntime.Affects(e, p) &&
                    !SpiritEliminationRuntime.Affects(e, o),
                    $"act={act} gy={p.Graveyard.Contains(se)} {Pending(e)}");

                var gyMon = ToGy(e, p, Celtic);
                var gySpell = ToGy(e, p, PotOfGreed);
                var sub = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, false);
                var oppGy = ToGy(e, o, Kuriboh);
                var release = PutInHand(e, p, SoulRelease);
                var act2 = e.TryActivateSpellTrap(p, release, fromHand: true);
                Settle(e);
                var legal = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                Check("Soul Release under it: the monster you control replaces your GY monster as a target",
                    act2 && legal.Contains(sub) && !legal.Contains(gyMon) && legal.Contains(gySpell) &&
                    legal.Contains(oppGy),
                    $"act={act2} {Pending(e)}");
                foreach (var t in new[] { sub, gySpell, oppGy, se })
                {
                    if (!e.IsAwaitingEffectTarget) break;
                    e.TrySelectEffectTarget(t);
                    Settle(e);
                }

                Check("Soul Release under it: the substitute is banished from the field; the GY monster stays",
                    !e.IsAwaitingEffectTarget && p.Banished.Contains(sub) && !OnField(p, sub) &&
                    p.Graveyard.Contains(gyMon) && p.Banished.Contains(gySpell) && o.Banished.Contains(oppGy),
                    $"subBan={p.Banished.Contains(sub)} gyMon={p.Graveyard.Contains(gyMon)} " +
                    $"spell={p.Banished.Contains(gySpell)} opp={o.Banished.Contains(oppGy)} {Pending(e)}");

                e.TryEndTurnSafe(p);
                Settle(e);
                Check("Spirit Elimination: it ends with the turn",
                    !SpiritEliminationRuntime.Affects(e, p), $"turn={e.TurnNumber}");
            }

            {
                // Cost path (TextEffectRuntime.CollectBanishGy): Bazoo banishes monsters you control, never itself.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                SpiritEliminationRuntime.Apply(e, p, null);
                var bazoo = PlaceMonster(e, p, Bazoo, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var setMon = PlaceMonster(e, p, GiantSoldier, 2, BattlePosition.Defense, false);
                var g1 = ToGy(e, p, Kuriboh);
                var g2 = ToGy(e, p, SummonedSkull);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, bazoo, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var offered = legal.Contains(celtic) && legal.Contains(setMon) && !legal.Contains(bazoo) &&
                              !legal.Contains(g1) && !legal.Contains(g2);
                var picked = offered && e.TrySelectEffectTarget(celtic);
                for (var i = 0; i < 3 && e.IsAwaitingEffectTarget; i++) e.CancelEffectTargeting();
                Settle(e);
                Check("Bazoo under it: the cost offers the monsters you control except Bazoo, never the GY",
                    act && offered, $"act={act} {Pending(e)}");
                Check("Bazoo under it: the picked monster is banished from the field; the GY keeps its monsters",
                    picked && p.Banished.Contains(celtic) && !OnField(p, celtic) && OnField(p, bazoo) &&
                    p.Graveyard.Contains(g1) && p.Graveyard.Contains(g2) && bazoo.CurrentAtk > 1600,
                    $"picked={picked} celticBan={p.Banished.Contains(celtic)} atk={bazoo.CurrentAtk} {Pending(e)}");
            }

            {
                // Hand procedure (EngineHygieneRuntime.ProcedureBanishCandidates): Spirit of Flames banishes a face-up FIRE
                // monster you control, not a face-down one, not the FIRE monster in the GY.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                SpiritEliminationRuntime.Apply(e, p, null);
                var hinotama = PlaceMonster(e, p, HinotamaSoul, 0, BattlePosition.Attack, true);
                var hidden = PlaceMonster(e, p, FirePrincess, 1, BattlePosition.Defense, false);
                var gyFire = ToGy(e, p, HinotamaSoul);
                var spirit = PutInHand(e, p, SpiritOfFlames);
                e.NotifyPublic();
                var proc = ToonsRuntime.HandProcedureOf(spirit);
                var cands = EngineHygieneRuntime.ProcedureBanishCandidates(e, p, spirit, proc);
                var ok = e.TrySpecialSummonProcedure(p, spirit);
                Settle(e);
                Check("Spirit of Flames under it: only the face-up FIRE monster you control can be banished",
                    cands.Count == 1 && cands[0] == hinotama, $"cands=[{string.Join(",", cands.Select(c => c.Name))}]");
                Check("Spirit of Flames under it: Summoned by banishing it; the GY FIRE monster stays",
                    ok && OnField(p, spirit) && p.Banished.Contains(hinotama) && p.Graveyard.Contains(gyFire) &&
                    OnField(p, hidden),
                    $"ok={ok} spirit={OnField(p, spirit)} ban={p.Banished.Contains(hinotama)} {Pending(e)}");
            }

            {
                // Skull Lair's cost (FaceUpQuickEffectsRuntime.BanishableGyMonsters): your face-down monster stands in.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, p, null);
                var lair = PlaceSetTrap(e, p, SkullLair, 0);
                lair.FaceUp = true;
                var sub = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, false);
                var kuri = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var ox = PlaceMonster(e, o, BattleOx, 1, BattlePosition.Attack, true);
                var g1 = ToGy(e, p, Celtic);
                e.NotifyPublic();
                var fired = e.TryActivateSpellTrap(p, lair, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(sub))
                    e.TrySelectEffectTarget(sub);
                DrainCombat(e);
                Settle(e);
                Check("Skull Lair under it: banishes the monster you control (N = 1) and destroys the Level 1",
                    fired && p.Banished.Contains(sub) && p.Graveyard.Contains(g1) && o.Graveyard.Contains(kuri) &&
                    OnField(o, ox),
                    $"fired={fired} subBan={p.Banished.Contains(sub)} kuri={o.Graveyard.Contains(kuri)} {Pending(e)}");
            }

            {
                // Soul Demolition's targets (FaceUpQuickEffectsRuntime.GyTargets) when the opponent is affected: you
                // target the monster they control; the AI targets the monster in your GY (you are not affected).
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, o, null);
                var demolition = PlaceSetTrap(e, p, SoulDemolition, 0);
                demolition.FaceUp = true;
                PlaceMonster(e, p, SummonedSkull, 0, BattlePosition.Attack, true); // a face-up Fiend
                var myGy = ToGy(e, p, Celtic);
                var oppGy = ToGy(e, o, Kuriboh);
                var oppMon = PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var fired = e.TryActivateSpellTrap(p, demolition, fromHand: false);
                var offered = !e.IsAwaitingEffectTarget ||
                              (e.PendingActivation.LegalTargets.Contains(oppMon) &&
                               !e.PendingActivation.LegalTargets.Contains(oppGy));
                for (var i = 0; i < 3 && e.IsAwaitingEffectTarget; i++)
                {
                    var t = e.PendingActivation.LegalTargets.Contains(oppMon) ? oppMon
                        : e.PendingActivation.LegalTargets.FirstOrDefault();
                    if (t == null || !e.TrySelectEffectTarget(t)) break;
                }

                DrainCombat(e);
                Settle(e);
                Check("Soul Demolition against it: your target is the monster they control, banished from the field",
                    fired && offered && o.Banished.Contains(oppMon) && !OnField(o, oppMon) && o.Graveyard.Contains(oppGy) &&
                    p.Banished.Contains(myGy),
                    $"fired={fired} offered={offered} oppMon={o.Banished.Contains(oppMon)} myGy={p.Banished.Contains(myGy)} " +
                    $"{Pending(e)}");
            }

            {
                // Targets in the opponent's GY (TextEffectRuntime.CollectTargets) when the opponent is affected:
                // Gravedigger Ghoul targets the monster they control.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, o, null);
                var oppGy = ToGy(e, o, Kuriboh);
                var oppMon = PlaceMonster(e, o, Celtic, 0, BattlePosition.Defense, false);
                var ghoul = PutInHand(e, p, GravediggerGhoul);
                var act = e.TryActivateSpellTrap(p, ghoul, fromHand: true);
                Settle(e);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(oppMon))
                {
                    e.TrySelectEffectTarget(oppMon);
                    Settle(e);
                }

                Check("Gravedigger Ghoul against it: banishes the monster the opponent controls; their GY monster stays",
                    act && o.Banished.Contains(oppMon) && !OnField(o, oppMon) && o.Graveyard.Contains(oppGy),
                    $"act={act} ban={o.Banished.Contains(oppMon)} gy={o.Graveyard.Contains(oppGy)} {Pending(e)}");
            }

            {
                // Disappear (Normal Trap; Lua c24623598: SelectTarget Card.IsAbleToRemove in the opponent's GY only, no
                // aux.SpElimFilter) is not substituted: under the opponent's Spirit Elimination it still targets the
                // monster in their GY and never a monster they control.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, o, null);
                var oppGy = ToGy(e, o, Kuriboh);
                var oppMon = PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
                var disappear = PlaceSetTrap(e, p, Disappear, 0);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, disappear, fromHand: false);
                Settle(e);
                var legal = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var offered = legal.Contains(oppGy) && !legal.Contains(oppMon);
                if (e.IsAwaitingEffectTarget && legal.Contains(oppGy))
                {
                    e.TrySelectEffectTarget(oppGy);
                    Settle(e);
                }

                Check("Disappear against it: still targets the GY monster, never the monster the opponent controls",
                    act && offered && o.Banished.Contains(oppGy) && OnField(o, oppMon),
                    $"act={act} offered={offered} ban={o.Banished.Contains(oppGy)} field={OnField(o, oppMon)} " +
                    $"legal=[{string.Join(",", legal.Select(t => t?.Name))}] {Pending(e)}");
            }

            {
                // Witch Doctor of Chaos (Lua c75946257: SelectTarget s.filter in MZONE|GRAVE of both players, exclusion
                // nil) can target itself as the substitute for its controller's GY monsters.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, p, null);
                var ownGy = ToGy(e, p, Kuriboh);
                var oppGy = ToGy(e, o, SummonedSkull);
                var wd = PlaceMonster(e, p, WitchDoctorOfChaos, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, wd);
                Settle(e);
                var legal = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var offered = legal.Contains(wd) && legal.Contains(oppGy) && !legal.Contains(ownGy);
                if (e.IsAwaitingEffectTarget && legal.Contains(wd))
                {
                    e.TrySelectEffectTarget(wd);
                    Settle(e);
                }

                Check("Witch Doctor of Chaos under it: its FLIP effect can target itself in place of your GY monster",
                    flipped && offered && p.Banished.Contains(wd) && !OnField(p, wd) && p.Graveyard.Contains(ownGy) &&
                    o.Graveyard.Contains(oppGy),
                    $"flipped={flipped} offered={offered} ban={p.Banished.Contains(wd)} " +
                    $"legal=[{string.Join(",", legal.Select(t => t?.Name))}] {Pending(e)}");
            }

            {
                // Book of Life's second target (CollectTargetsForZone): the opponent's monster replaces their GY monster.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, o, null);
                var zombie = ToGy(e, p, ArmoredZombie);
                var oppGy = ToGy(e, o, Kuriboh);
                var oppMon = PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
                var book = PutInHand(e, p, BookOfLife);
                var act = e.TryActivateSpellTrap(p, book, fromHand: true);
                Settle(e);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(zombie))
                {
                    e.TrySelectEffectTarget(zombie);
                    Settle(e);
                }

                var second = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var offered = !e.IsAwaitingEffectTarget || (second.Contains(oppMon) && !second.Contains(oppGy));
                if (e.IsAwaitingEffectTarget && second.Contains(oppMon))
                {
                    e.TrySelectEffectTarget(oppMon);
                    Settle(e);
                }

                Check("Book of Life against it: the second target is the opponent's monster, banished from the field",
                    act && offered && OnField(p, zombie) && o.Banished.Contains(oppMon) && o.Graveyard.Contains(oppGy),
                    $"act={act} offered={offered} zombie={OnField(p, zombie)} ban={o.Banished.Contains(oppMon)} {Pending(e)}");
            }

            {
                // Fiend Comedian's right call (SimpleSpellTrapsRuntime) against it: the opponent's GY Spell and every
                // monster they control are banished; their GY monster stays.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SpiritEliminationRuntime.Apply(e, o, null);
                var oppGyMon = ToGy(e, o, Kuriboh);
                var oppGySpell = ToGy(e, o, PotOfGreed);
                var oppMon = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var comedian = PlaceSetTrap(e, p, FiendComedian, 0);
                e.Rng.QueueCoin(true);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, comedian, fromHand: false);
                Settle(e);
                if (e.PendingActivation != null) e.TrySelectCoinCall(true);
                Settle(e);
                Check("Fiend Comedian against it: banishes the GY Spell and the monster they control, not the GY monster",
                    act && o.Banished.Contains(oppGySpell) && o.Banished.Contains(oppMon) && !OnField(o, oppMon) &&
                    o.Graveyard.Contains(oppGyMon),
                    $"act={act} spell={o.Banished.Contains(oppGySpell)} mon={o.Banished.Contains(oppMon)} " +
                    $"gyMon={o.Graveyard.Contains(oppGyMon)} {Pending(e)}");
            }
        }

        // ───────────────────────── Major Riot ─────────────────────────

        static bool RiotWindowFor(DuelEngine e, DuelistState who) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.MonstersReturnedToHandByOpponent &&
            e.PendingResponse.Responder == who;

        static void MajorRiotDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI answers the player's Compulsory Evacuation Device: every monster returns; the player Summons
                // their 2 eligible monsters face-down (Gaia is Level 7); 1 went to the AI's hand, so the AI Summons its
                // best eligible monster (Giant Soldier, back from the first return) face-down.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var elf = PutInHand(e, p, MysticalElf);
                var ced = PlaceSetTrap(e, p, CompulsoryEvacuationDevice, 0);
                var soldier = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var oppOx = PlaceMonster(e, o, BattleOx, 1, BattlePosition.Attack, true);
                var riot = PlaceSetTrap(e, o, MajorRiot, 0);
                e.NotifyPublic();
                var openState = e.CanActivateSpellTrap(o, riot, fromHand: false);
                var act = e.TryActivateSpellTrap(p, ced, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(soldier))
                    e.TrySelectEffectTarget(soldier);
                Settle(e);
                Check("Major Riot: not activatable in an open game state",
                    !openState, $"open={openState}");
                Check("Major Riot (AI): answers the bounce — all monsters return, 2 face-down for the player, 1 for the AI",
                    act && o.Graveyard.Contains(riot) && FaceDownDefense(o, soldier) && o.Hand.Contains(oppOx) &&
                    p.Hand.Contains(gaia) && FaceDownDefense(p, celtic) && FaceDownDefense(p, elf) &&
                    o.MonstersOnField().Count() == 1 && !e.IsAwaitingEffectTarget,
                    $"act={act} riotGy={o.Graveyard.Contains(riot)} gaiaHand={p.Hand.Contains(gaia)} " +
                    $"celtic={FaceDownDefense(p, celtic)} elf={FaceDownDefense(p, elf)} {Pending(e)}");
            }

            {
                // The player answers the AI's Compulsory Evacuation Device (it returns Gaia) on the AI's turn: then
                // Celtic goes to the hand, and the player chooses 1 of 3 eligible monsters (not Level 7 Gaia); nothing
                // went to the AI's hand, so it Summons nothing.
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var riot = PlaceSetTrap(e, p, MajorRiot, 0);
                var elf = PutInHand(e, p, MysticalElf);
                var soldier = PutInHand(e, p, GiantSoldier);
                var ced = PlaceSetTrap(e, o, CompulsoryEvacuationDevice, 0);
                var ox = PutInHand(e, o, BattleOx);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(o, ced, fromHand: false);
                for (var i = 0; i < 6 && e.IsAwaitingResponse && !RiotWindowFor(e, p); i++)
                    e.PassResponse();
                var window = RiotWindowFor(e, p);
                var bounced = p.Hand.Contains(gaia);
                var answered = window && e.TryActivateSpellTrap(p, riot, fromHand: false);
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                    e.PassResponse();
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                            e.PendingActivation.LegalTargets.Contains(elf) &&
                            e.PendingActivation.LegalTargets.Contains(soldier) &&
                            !e.PendingActivation.LegalTargets.Contains(gaia);
                var picked = asked && e.TrySelectEffectTarget(elf);
                Settle(e);
                Check("Major Riot (player): its window opens after the opponent's effect returned your monster",
                    reached && act && bounced && window, $"reached={reached} act={act} bounced={bounced} window={window} " +
                                                         $"pr={e.PendingResponse?.Timing} log={string.Join(" | ", log.Skip(Math.Max(0, log.Count - 14)))}");
                Check("Major Riot (player): you choose which eligible hand monster to Summon (Level 7 is not offered)",
                    answered && asked, $"answered={answered} {Pending(e)}");
                Check("Major Riot (player): you Special Summon the chosen monster face-down; the AI Summons nothing",
                    picked && FaceDownDefense(p, elf) && o.Hand.Contains(ox) &&
                    p.Hand.Contains(celtic) && p.Hand.Contains(gaia) &&
                    p.Graveyard.Contains(riot) && p.MonstersOnField().Count() == 1 && !o.MonstersOnField().Any() &&
                    !e.IsAwaitingEffectTarget,
                    $"picked={picked} elf={FaceDownDefense(p, elf)} oxHand={o.Hand.Contains(ox)} {Pending(e)}");
            }

            {
                // Near misses: your own effect returning your monster, or a return with no reason player, opens nothing.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Gaia, 1, BattlePosition.Attack, true);
                var riot = PlaceSetTrap(e, p, MajorRiot, 0);
                var ced = PlaceSetTrap(e, p, CompulsoryEvacuationDevice, 1);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, ced, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(celtic))
                    e.TrySelectEffectTarget(celtic);
                var sawWindow = false;
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                {
                    sawWindow |= RiotWindowFor(e, p);
                    e.PassResponse();
                }

                Check("Major Riot: no window when your own effect returns your monster",
                    act && p.Hand.Contains(celtic) && !sawWindow && !p.Graveyard.Contains(riot),
                    $"act={act} hand={p.Hand.Contains(celtic)} window={sawWindow}");
            }

            {
                // A Token returned to the hand leaves the field (it never reaches the hand) and records nothing.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var token = e.CreateToken("Test Token", "Rock", "EARTH", 1, 0, 0);
                p.MonsterZones[0].Occupant = token;
                var ok = e.ReturnCardToHand(token, by: e.Opponent);
                Check("ReturnCardToHand: a Token leaves the field and never reaches the hand",
                    ok && !p.Hand.Contains(token) && !OnField(p, token) && !MajorRiotRuntime.HasPending(e),
                    $"ok={ok} hand={p.Hand.Contains(token)}");
            }
        }
    }
}
