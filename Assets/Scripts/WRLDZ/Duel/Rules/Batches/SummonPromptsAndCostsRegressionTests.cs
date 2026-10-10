using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SummonPromptsAndCosts" (v163): Vampire Baby's optional end-of-Battle-Phase Special Summon (a human's
    /// pick holds Main Phase 2 / the turn end) and Tyrant Dragon's GY summon cost (Tribute 1 Dragon; a human picks it as
    /// the Summon resolves). Compile shapes, near misses, mini-duels through the engine entry points, and AI duels with
    /// both cards in the Decks (no soft-lock).
    /// </summary>
    public static class SummonPromptsAndCostsRegressionTests
    {
        const int VampireBaby = 56387350;   // Zombie DARK 700/1000
        const int Tyrant = 94568601;        // Dragon FIRE 2900/2500
        const int OjamaGreen = 12482652;    // Beast 0/1000
        const int PetitDragon = 75356564;   // Dragon 600/700
        const int Celtic = 91152256;        // Warrior 1400/1200
        const int Luster = 11091375;        // Dragon 1900/1600
        const int Bewd = 89631139;          // Dragon 3000/2500
        const int BabyDragon = 88819587;    // Dragon 1200/700
        const int WingedDragon = 87796900;  // Dragon 1400/1200
        const int MysticalElf = 15025844;
        const int SummonedSkull = 70781052;
        const int MonsterReborn = 83764719;
        const int CallOfTheHaunted = 97077563;
        const int SpellbindingCircle = 18807108;
        const int TorrentialTribute = 53582587;
        const int GracefulCharity = 79571449;

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("SummonPromptsAndCosts: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            VampireBabyDuels(db, pDeck, aDeck, Check);
            TyrantDragonDuels(db, pDeck, aDeck, Check);
            AiDuels(db, Check);
        }

        static string Left(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [" + string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} opt={c.IsOptional} opt1={c.OncePerTurn} race={c.TributeRaceFilter} n={c.Amount}")) +
                  "] left=[" + string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>()) + "]";

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var baby = CardTextEffectCompiler.Compile(db.Get(VampireBaby));
            Check("Vampire Baby: one optional, once-per-turn end-of-Battle-Phase Special Summon clause; Implemented",
                baby != null && baby.FullyCompiled && baby.ClauseList.Count == 1 &&
                baby.ClauseList[0].Timing == EffectTiming.EndOfBattlePhase &&
                baby.ClauseList[0].Action == EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn &&
                baby.ClauseList[0].IsOptional && baby.ClauseList[0].OncePerTurn &&
                CardEffectStatus.Classify(db.Get(VampireBaby)) == CardEffectStatusKind.Implemented,
                Left(baby) + $" status={CardEffectStatus.Classify(db.Get(VampireBaby))}");

            var tyrant = CardTextEffectCompiler.Compile(db.Get(Tyrant));
            Check("Tyrant Dragon: Trap target negation + second attack + GY summon cost (Tribute 1 Dragon); Implemented",
                tyrant != null && tyrant.FullyCompiled && tyrant.ClauseList.Count == 3 &&
                tyrant.ClauseList.Exists(c => c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                                              c.NegateRespondsToTrap && c.AffectsThisCardOnly) &&
                tyrant.ClauseList.Exists(c => c.Action == EffectActionKind.ExtraAttacks && c.Amount == 1) &&
                tyrant.ClauseList.Exists(c => c.Action == EffectActionKind.GySpecialSummonNeedsTributeCost &&
                                              c.TributeRaceFilter == "Dragon" && c.Amount == 1 &&
                                              c.RequiresTributeCount == 0) &&
                CardEffectStatus.Classify(db.Get(Tyrant)) == CardEffectStatusKind.Implemented,
                Left(tyrant) + $" status={CardEffectStatus.Classify(db.Get(Tyrant))}");
            Check("Vocabulary: both new actions resolve as a Special Summon",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn }) ==
                EffectResolutionKind.SpecialSummon &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.GySpecialSummonNeedsTributeCost }) ==
                EffectResolutionKind.SpecialSummon);
        }

        static CardDef Synthetic(CardDef from, string desc) => new()
        {
            id = 90538163, name = "Synthetic " + from.name, type = from.type, frameType = from.frameType, race = from.race,
            attribute = from.attribute, level = from.level, atk = from.atk, def = from.def, desc = desc
        };

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var baby = db.Get(VampireBaby);
            foreach (var text in new[]
                     {
                         // "to your opponent's side", a mandatory Summon, and a "this Battle Phase" window are other effects.
                         "At the end of the Battle Phase, if this card destroyed a monster by battle and sent it to the Graveyard " +
                         "this turn: You can Special Summon that monster to your opponent's side of the field.",
                         "At the end of the Battle Phase, if this card destroyed a monster by battle and sent it to the Graveyard " +
                         "this turn: Special Summon that monster to your side of the field.",
                         "At the end of the Battle Phase, if this card destroyed a monster by battle and sent it to the Graveyard " +
                         "this turn: You can Special Summon that monster to your side of the field, and it gains 500 ATK."
                     })
            {
                var p = CardTextEffectCompiler.Compile(Synthetic(baby, text));
                Check("Near miss stays uncompiled: " + text.Substring(text.IndexOf(':') + 2),
                    p == null || !p.FullyCompiled ||
                    !p.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn),
                    Left(p));
            }

            var tyrant = db.Get(Tyrant);
            foreach (var sentence in new[]
                     {
                         "This card cannot be Special Summoned from the GY, unless you Tribute 2 Dragon monsters.",
                         "This card cannot be Special Summoned from the GY, unless you Tribute 1 Dragon monster and pay 1000 LP.",
                         "This card cannot be Special Summoned from the GY, unless you Tribute 1 Dragon monster your opponent " +
                         "controls."
                     })
            {
                var text = tyrant.desc.Replace(
                    "This card cannot be Special Summoned from the GY, unless you Tribute 1 Dragon monster.", sentence);
                var p = CardTextEffectCompiler.Compile(Synthetic(tyrant, text));
                Check("Near miss stays uncompiled: " + sentence,
                    text != tyrant.desc && (p == null || !p.FullyCompiled) &&
                    (p == null || !p.ClauseList.Exists(c => c.Action == EffectActionKind.GySpecialSummonNeedsTributeCost)),
                    Left(p));
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool ReachMain(DuelEngine engine, DuelistState who)
        {
            for (var t = 0; t < 8 && !(engine.TurnPlayer == who && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1;
        }

        static DuelEngine Duel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool player, bool battle, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            var who = player ? engine.Player : engine.Opponent;
            reached = ReachMain(engine, who);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            if (reached && battle)
            {
                engine.TryEnterBattlePhase(who);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                DrainCombat(engine);
                reached = engine.Phase == DuelPhase.Battle && engine.TurnPlayer == who;
            }

            return engine;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            return ok;
        }

        static void Pass(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static int ZoneOf(DuelistState who, CardInstance c) => who.TryFindMonster(c, out var zi) ? zi : -1;

        static CardInstance InGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            GyEntryStamps.Entered(engine, c, GyEntryReason.Destroyed);
            return c;
        }

        static string Pending(DuelEngine engine)
        {
            var p = engine.PendingActivation;
            return p == null
                ? $"none resp={engine.PendingResponse?.Timing}"
                : $"{p.Card?.Name} kind={p.TargetKind} by={p.Controller?.Name} hold={p.HoldsChainResolution} " +
                  $"targets=[{string.Join(",", p.LegalTargets.Select(t => t.Name))}]";
        }

        // ───────────────────────── Vampire Baby ─────────────────────────

        static void VampireBabyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Its controller's turn: Main Phase 2 opens the offer; the monster comes to the controller's field in the
                // position and zone picked.
                var e = Duel(db, pDeck, aDeck, player: true, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baby = PlaceMonster(e, p, VampireBaby, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var a = reached && Attack(e, p, baby, ojama);
                var killed = o.Graveyard.Contains(ojama);
                e.TryEnterMainPhase2(p);
                var pend = e.PendingActivation;
                var offered = pend != null && pend.Card == baby && pend.Controller == p && pend.LegalTargets.Count == 1 &&
                              pend.LegalTargets.Contains(ojama) && SpellTrapEffects.PendingTargetIsSpecialSummon(pend) &&
                              e.Phase == DuelPhase.Main2;
                var detail = Pending(e);
                e.SetPendingSpecialSummonPlacement(3, BattlePosition.Defense);
                var sel = e.TrySelectEffectTarget(ojama);
                Pass(e);
                Check("Vampire Baby: in Main Phase 2 its controller Special Summons the monster it destroyed (face-up DEF, zone 3)",
                    a && killed && offered && sel && ZoneOf(p, ojama) == 3 && ojama.FaceUp &&
                    ojama.Position == BattlePosition.Defense && !o.Graveyard.Contains(ojama) &&
                    e.ControllerOf(ojama) == p && !e.IsAwaitingEffectTarget,
                    $"reached={reached} a={a} killed={killed} offered={offered} [{detail}] sel={sel} zone={ZoneOf(p, ojama)} " +
                    $"pos={ojama.Position}");
                Check("Vampire Baby: once used, it is not offered again (once per turn, flag reset at the end of the Battle Phase)",
                    !e.IsAwaitingEffectTarget && e.Phase == DuelPhase.Main2);
            }

            {
                // CANCEL before a pick: the effect is not used; nothing is summoned and play goes on in Main Phase 2.
                var e = Duel(db, pDeck, aDeck, player: true, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baby = PlaceMonster(e, p, VampireBaby, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var a = reached && Attack(e, p, baby, ojama);
                e.TryEnterMainPhase2(p);
                var offered = e.IsAwaitingEffectTarget && e.PendingActivation.Card == baby;
                e.CancelEffectTargeting();
                Check("Vampire Baby: CANCEL declines — the monster stays in its owner's GY",
                    a && offered && o.Graveyard.Contains(ojama) && ZoneOf(p, ojama) < 0 && !e.IsAwaitingEffectTarget &&
                    e.Phase == DuelPhase.Main2 && e.TurnPlayer == p,
                    $"a={a} offered={offered} gy={o.Graveyard.Contains(ojama)} {Pending(e)}");
            }

            {
                // The opponent's turn: both attackers it destroyed are offered; the human picks both, one per position.
                var e = Duel(db, pDeck, aDeck, player: false, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baby = PlaceMonster(e, p, VampireBaby, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var petit = PlaceMonster(e, o, PetitDragon, 1, BattlePosition.Attack, true);
                var a1 = reached && Attack(e, o, ojama, baby);
                var a2 = reached && Attack(e, o, petit, baby);
                var killed = o.Graveyard.Contains(ojama) && o.Graveyard.Contains(petit);
                e.TryEnterMainPhase2(o);
                var pend = e.PendingActivation;
                var offered = pend != null && pend.Controller == p && pend.Card == baby && pend.LegalTargets.Count == 2 &&
                              pend.TargetPicksRemaining == 2;
                var detail = Pending(e);
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Attack);
                var s1 = e.TrySelectEffectTarget(petit);
                var stillOpen = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1;
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var s2 = e.TrySelectEffectTarget(ojama);
                Pass(e);
                Check("Vampire Baby: on the opponent's turn it Special Summons both attackers it destroyed (ATK + DEF)",
                    a1 && a2 && killed && offered && s1 && stillOpen && s2 && ZoneOf(p, petit) >= 0 && ZoneOf(p, ojama) >= 0 &&
                    petit.Position == BattlePosition.Attack && ojama.Position == BattlePosition.Defense && ojama.FaceUp &&
                    !e.IsAwaitingEffectTarget && e.TurnPlayer == o && e.Phase == DuelPhase.Main2,
                    $"reached={reached} a1={a1} a2={a2} killed={killed} offered={offered} [{detail}] s1={s1} open={stillOpen} " +
                    $"s2={s2} petit={ZoneOf(p, petit)}/{petit.Position} ojama={ZoneOf(p, ojama)}/{ojama.Position}");
            }

            {
                // Ending the turn straight from the Battle Phase: the turn stops before its End Phase; it ends after the pick.
                var e = Duel(db, pDeck, aDeck, player: true, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baby = PlaceMonster(e, p, VampireBaby, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var a = reached && Attack(e, p, baby, ojama);
                var turn = e.TurnNumber;
                e.EndTurn(p);
                var held = e.IsAwaitingEffectTarget && e.TurnNumber == turn && e.TurnPlayer == p && e.Phase == DuelPhase.Main2;
                var detail = Pending(e);
                var sel = e.TrySelectEffectTarget(ojama);
                Pass(e);
                Check("Vampire Baby: End Turn from the Battle Phase waits for the offer, then the turn ends",
                    a && held && sel && ZoneOf(p, ojama) >= 0 && ojama.Position == BattlePosition.Attack &&
                    e.TurnNumber == turn + 1 && e.TurnPlayer == o,
                    $"a={a} held={held} [{detail}] sel={sel} zone={ZoneOf(p, ojama)} turn={e.TurnNumber} (was {turn}) " +
                    $"player={e.TurnPlayer?.Name}");
            }

            {
                // The AI's Vampire Baby resolves at the end of the Battle Phase with no prompt.
                var e = Duel(db, pDeck, aDeck, player: false, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baby = PlaceMonster(e, o, VampireBaby, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, p, OjamaGreen, 0, BattlePosition.Attack, true);
                var a = reached && Attack(e, o, baby, ojama);
                e.TryEnterMainPhase2(o);
                Pass(e);
                Check("Vampire Baby (AI): the monster it destroyed is Special Summoned to the AI's field, no prompt",
                    a && ZoneOf(o, ojama) >= 0 && !p.Graveyard.Contains(ojama) && e.ControllerOf(ojama) == o &&
                    !e.IsAwaitingEffectTarget,
                    $"reached={reached} a={a} zone={ZoneOf(o, ojama)} {Pending(e)}");
            }

            {
                // Fail closed: a monster destroyed by another monster is not offered (the condition is this card's own kill).
                var e = Duel(db, pDeck, aDeck, player: true, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, VampireBaby, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var a = reached && Attack(e, p, celtic, ojama);
                e.TryEnterMainPhase2(p);
                Check("Vampire Baby: a monster another card destroyed by battle is not offered",
                    a && o.Graveyard.Contains(ojama) && !e.IsAwaitingEffectTarget && e.Phase == DuelPhase.Main2,
                    $"a={a} {Pending(e)}");
            }

            {
                // Vampire Baby Special Summons a Tyrant Dragon it destroyed by battle this turn (stamped as such): its
                // Tribute is picked before that Summon.
                var e = Duel(db, pDeck, aDeck, player: true, battle: true, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baby = PlaceMonster(e, p, VampireBaby, 0, BattlePosition.Attack, true);
                var luster = PlaceMonster(e, p, Luster, 1, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 2, BattlePosition.Attack, true);
                var ojama = PlaceMonster(e, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var a = reached && Attack(e, p, baby, ojama);
                var tyrant = InGy(e, o, Tyrant);
                GyEntryStamps.Entered(e, tyrant, GyEntryReason.Destroyed | GyEntryReason.Battle);
                tyrant.WasDestroyedByBattle = true;
                tyrant.BattleDestroyer = baby;
                tyrant.SentFromFieldTurnNumber = e.TurnNumber;
                e.TryEnterMainPhase2(p);
                var offered = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(tyrant) &&
                              e.PendingActivation.LegalTargets.Contains(ojama);
                var s1 = e.TrySelectEffectTarget(tyrant);
                e.CancelEffectTargeting(); // summon the one selected
                var pend = e.PendingActivation;
                var tributeAsked = pend != null && pend.TargetKind == EffectTargetKind.TributeMonsterYouControl &&
                                   pend.LegalTargets.Count == 2 && pend.LegalTargets.Contains(luster) &&
                                   pend.LegalTargets.Contains(bewd) && o.Graveyard.Contains(tyrant);
                var detail = Pending(e);
                var s2 = e.TrySelectEffectTarget(bewd);
                Pass(e);
                Check("Vampire Baby + Tyrant Dragon: the Tribute (human pick) is paid, then Tyrant Dragon is Special Summoned",
                    a && offered && s1 && tributeAsked && s2 && ZoneOf(p, tyrant) >= 0 && p.Graveyard.Contains(bewd) &&
                    ZoneOf(p, luster) >= 0 && o.Graveyard.Contains(ojama) && !e.IsAwaitingEffectTarget,
                    $"a={a} offered={offered} s1={s1} asked={tributeAsked} [{detail}] s2={s2} tyrant={ZoneOf(p, tyrant)} " +
                    $"bewdGy={p.Graveyard.Contains(bewd)}");
            }
        }

        // ───────────────────────── Tyrant Dragon ─────────────────────────

        static bool ActivateReborn(DuelEngine e, DuelistState who, CardInstance target, out bool targetOffered)
        {
            var reborn = PutInHand(e, who, MonsterReborn);
            var ok = e.TryActivateSpellTrap(who, reborn, true);
            targetOffered = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(target);
            if (ok && targetOffered) ok = e.TrySelectEffectTarget(target);
            Pass(e);
            return ok;
        }

        static void TyrantDragonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // No Dragon to Tribute: it cannot be Special Summoned from the GY — not even a legal Monster Reborn target.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var tyrant = InGy(e, p, Tyrant);
                var elf = InGy(e, p, MysticalElf);
                var canGy = SpecialSummonLegality.CanBeSpecialSummoned(e, p, tyrant, null, SummonOrigin.Graveyard);
                var canHand = SpecialSummonLegality.CanBeSpecialSummoned(e, p, tyrant, null, SummonOrigin.Hand);
                var legacy = SpellTrapEffects.CollectLegalTargets(e, p, EffectTargetKind.MonsterInEitherGy, null, true);
                var reborn = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, reborn, true);
                var offered = e.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                Check("Tyrant Dragon: with no Dragon to Tribute it is not a legal GY Special Summon (Monster Reborn skips it)",
                    reached && !canGy && canHand && !legacy.Contains(tyrant) && legacy.Contains(elf) &&
                    !offered.Contains(tyrant) && (offered.Contains(elf) || !act),
                    $"reached={reached} gy={canGy} hand={canHand} legacy=[{string.Join(",", legacy.Select(c => c.Name))}] " +
                    $"act={act} offered=[{string.Join(",", offered.Select(c => c.Name))}]");
                e.NextSpecialSummonFromGraveyard = true;
                p.Graveyard.Remove(tyrant);
                var direct = e.SpecialSummonToField(p, tyrant, BattlePosition.Attack, true);
                Check("Tyrant Dragon: a GY Special Summon without a Dragon to Tribute is refused by the engine",
                    !direct && ZoneOf(p, tyrant) < 0, $"direct={direct}");
            }

            {
                // One Dragon: it is Tributed automatically as Monster Reborn resolves.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                var luster = PlaceMonster(e, p, Luster, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var tyrant = InGy(e, p, Tyrant);
                var ok = ActivateReborn(e, p, tyrant, out var targetOffered);
                Check("Tyrant Dragon: Monster Reborn with one Dragon — that Dragon is Tributed, then Tyrant Dragon is Summoned",
                    reached && ok && targetOffered && ZoneOf(p, tyrant) >= 0 && p.Graveyard.Contains(luster) &&
                    ZoneOf(p, celtic) >= 0 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} ok={ok} offered={targetOffered} tyrant={ZoneOf(p, tyrant)} " +
                    $"lusterGy={p.Graveyard.Contains(luster)} {Pending(e)}");
            }

            {
                // Two Dragons (human): the Chain waits on the Tribute pick as Monster Reborn resolves; the pick is honoured.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                var luster = PlaceMonster(e, p, Luster, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var tyrant = InGy(e, p, Tyrant);
                var ok = ActivateReborn(e, p, tyrant, out var targetOffered);
                var pend = e.PendingActivation;
                var asked = pend != null && pend.TargetKind == EffectTargetKind.TributeMonsterYouControl &&
                            pend.Controller == p && pend.HoldsChainResolution && pend.LegalTargets.Count == 2 &&
                            p.Graveyard.Contains(tyrant) && e.IsResolvingChain;
                var detail = Pending(e);
                var sel = e.TrySelectEffectTarget(bewd);
                Pass(e);
                Check("Tyrant Dragon: two Dragons — the human picks the Tribute (BEWD) while the Chain waits; then it is Summoned",
                    reached && ok && targetOffered && asked && sel && ZoneOf(p, tyrant) >= 0 && p.Graveyard.Contains(bewd) &&
                    ZoneOf(p, luster) >= 0 && !e.IsAwaitingEffectTarget && !e.IsResolvingChain && !e.Chain.HasLinks,
                    $"reached={reached} ok={ok} offered={targetOffered} asked={asked} [{detail}] sel={sel} " +
                    $"tyrant={ZoneOf(p, tyrant)} bewdGy={p.Graveyard.Contains(bewd)} chain={e.Chain.Count}");
            }

            {
                // CANCEL at the Tribute pick: the Summon is not undone — the lowest-ATK Dragon is Tributed.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                var luster = PlaceMonster(e, p, Luster, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var tyrant = InGy(e, p, Tyrant);
                var ok = ActivateReborn(e, p, tyrant, out _);
                var asked = e.IsAwaitingEffectTarget &&
                            e.PendingActivation.TargetKind == EffectTargetKind.TributeMonsterYouControl;
                e.CancelEffectTargeting();
                Pass(e);
                Check("Tyrant Dragon: CANCEL at the Tribute pick Tributes automatically (lowest ATK) and Summons it",
                    reached && ok && asked && ZoneOf(p, tyrant) >= 0 && p.Graveyard.Contains(luster) && ZoneOf(p, bewd) >= 0 &&
                    !e.IsAwaitingEffectTarget,
                    $"ok={ok} asked={asked} tyrant={ZoneOf(p, tyrant)} {Pending(e)}");
            }

            {
                // Call of the Haunted (a Continuous Trap link): the same pick, then the Trap is linked to it.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                var luster = PlaceMonster(e, p, Luster, 0, BattlePosition.Attack, true);
                var winged = PlaceMonster(e, p, WingedDragon, 1, BattlePosition.Attack, true);
                var tyrant = InGy(e, p, Tyrant);
                var call = PlaceSetTrap(e, p, CallOfTheHaunted, 0);
                var act = e.TryActivateSpellTrap(p, call, false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(tyrant))
                    e.TrySelectEffectTarget(tyrant);
                Pass(e);
                var asked = e.IsAwaitingEffectTarget &&
                            e.PendingActivation.TargetKind == EffectTargetKind.TributeMonsterYouControl;
                var detail = Pending(e);
                if (asked) e.TrySelectEffectTarget(luster);
                Pass(e);
                Check("Tyrant Dragon: Call of the Haunted — Tribute picked (Luster Dragon), Tyrant Dragon Summoned",
                    reached && act && asked && ZoneOf(p, tyrant) >= 0 && p.Graveyard.Contains(luster) &&
                    ZoneOf(p, winged) >= 0 && !e.IsAwaitingEffectTarget,
                    $"act={act} asked={asked} [{detail}] tyrant={ZoneOf(p, tyrant)} lusterGy={p.Graveyard.Contains(luster)}");
            }

            {
                // The AI Tributes its lowest-ATK Dragon with no prompt.
                var e = Duel(db, pDeck, aDeck, player: false, battle: false, out var reached);
                var o = e.Opponent;
                var luster = PlaceMonster(e, o, Luster, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, o, Bewd, 1, BattlePosition.Attack, true);
                var tyrant = InGy(e, o, Tyrant);
                var ok = ActivateReborn(e, o, tyrant, out var targetOffered); // the AI picks its target itself
                Pass(e);
                Check("Tyrant Dragon (AI): Monster Reborn Tributes the AI's lowest-ATK Dragon, no prompt",
                    reached && ok && ZoneOf(o, tyrant) >= 0 && o.Graveyard.Contains(luster) &&
                    ZoneOf(o, bewd) >= 0 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} ok={ok} offered={targetOffered} tyrant={ZoneOf(o, tyrant)} {Pending(e)}");
            }

            {
                // The cost governs the GY only: from the hand it is Special Summoned with no Tribute.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                var luster = PlaceMonster(e, p, Luster, 0, BattlePosition.Attack, true);
                var tyrant = PutInHand(e, p, Tyrant);
                p.Hand.Remove(tyrant);
                e.MarkSpecialSummonFromHand(tyrant);
                var ok = e.SpecialSummonToField(p, tyrant, BattlePosition.Attack, true);
                Check("Tyrant Dragon: a Special Summon from the hand needs no Tribute",
                    reached && ok && ZoneOf(p, tyrant) >= 0 && ZoneOf(p, luster) >= 0, $"ok={ok}");
            }

            {
                // Its Trap sentence applies now that the card is FullyCompiled: Spellbinding Circle is negated and destroyed.
                var e = Duel(db, pDeck, aDeck, player: true, battle: false, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tyrant = PlaceMonster(e, o, Tyrant, 0, BattlePosition.Attack, true);
                var circle = PlaceSetTrap(e, p, SpellbindingCircle, 0);
                var ok = e.TryActivateSpellTrap(p, circle, false);
                if (ok && e.IsAwaitingEffectTarget) ok = e.TrySelectEffectTarget(tyrant);
                Pass(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Tyrant Dragon: a Trap that targets it (Spellbinding Circle) is negated and destroyed",
                    reached && ok && p.Graveyard.Contains(circle) && circle.EquippedTo == null && !tyrant.Equips.Contains(circle),
                    $"ok={ok} circleGy={p.Graveyard.Contains(circle)} boundTo={circle.EquippedTo?.Name}");
            }
        }

        // ───────────────────────── AI duels ─────────────────────────

        static DeckFile Deck(string name) => new()
        {
            name = name,
            format = "TCG",
            main = new[]
            {
                new DeckCardEntry { id = VampireBaby, qty = 3 }, new DeckCardEntry { id = Tyrant, qty = 3 },
                new DeckCardEntry { id = MonsterReborn, qty = 3 }, new DeckCardEntry { id = CallOfTheHaunted, qty = 3 },
                new DeckCardEntry { id = GracefulCharity, qty = 3 },
                new DeckCardEntry { id = Luster, qty = 3 }, new DeckCardEntry { id = Bewd, qty = 3 },
                new DeckCardEntry { id = PetitDragon, qty = 3 }, new DeckCardEntry { id = OjamaGreen, qty = 3 },
                new DeckCardEntry { id = Celtic, qty = 3 }, new DeckCardEntry { id = BabyDragon, qty = 3 },
                new DeckCardEntry { id = WingedDragon, qty = 3 }, new DeckCardEntry { id = SummonedSkull, qty = 2 },
                new DeckCardEntry { id = TorrentialTribute, qty = 1 }, new DeckCardEntry { id = SpellbindingCircle, qty = 1 }
            },
            extra = Array.Empty<DeckCardEntry>(),
            side = Array.Empty<DeckCardEntry>()
        };

        /// <summary>
        /// Duels between the stress agents (the Player side answers its picks automatically, the AI side decides at once)
        /// with both cards in the Decks: every duel ends or reaches the turn cap with play moving on.
        /// </summary>
        static void AiDuels(CardDatabase db, Chk Check)
        {
            const int duels = 250;
            var stuck = 0;
            var errors = new List<string>();
            var finished = 0;
            int babyUses = 0, babyOffers = 0, tyrantTributes = 0, tyrantPicks = 0;
            for (var d = 0; d < duels; d++)
            {
                var e = new DuelEngine();
                e.Rng.Reseed(16300 + d);
                e.OnLog += line =>
                {
                    if (line == null) return;
                    if (line.Contains("Vampire Baby: its effect activates")) babyUses++;
                    if (line.Contains("can Special Summon a monster it destroyed by battle this turn")) babyOffers++;
                    if (line.Contains("to Special Summon it from the GY.")) tyrantTributes++;
                    if (line.Contains("(its summon cost).")) tyrantPicks++;
                };
                try
                {
                    e.StartDuel(db, Deck("SummonPromptsA"), Deck("SummonPromptsB"), cinematicOpening: false);
                    // Each player starts with a Tyrant Dragon in the GY, so the revival Spells / Traps meet its cost.
                    InGy(e, e.Player, Tyrant);
                    InGy(e, e.Opponent, Tyrant);
                    var last = "";
                    var same = 0;
                    for (var step = 0; step < 240 && !e.GameOver && e.TurnNumber <= 40; step++)
                    {
                        DuelStressAgent.DrainWindows(e);
                        if (e.GameOver) break;
                        DuelStressAgent.PlayTurn(e, e.TurnPlayer);
                        DuelStressAgent.DrainWindows(e);
                        var status = $"{e.TurnNumber}:{e.Phase}:{e.Player.LifePoints}:{e.Opponent.LifePoints}:" +
                                     $"{e.IsAwaitingResponse}:{e.IsAwaitingEffectTarget}";
                        same = status == last ? same + 1 : 0;
                        last = status;
                        if (same < 6) continue;
                        stuck++;
                        if (errors.Count < 3) errors.Add($"duel {d} stuck at {status} {Pending(e)}");
                        break;
                    }

                    finished++;
                }
                catch (Exception ex)
                {
                    if (errors.Count < 3) errors.Add($"duel {d}: {ex.GetType().Name} {ex.Message}");
                    stuck++;
                }
            }

            Check($"AI duels: {duels} duels with Vampire Baby / Tyrant Dragon / Monster Reborn / Call of the Haunted — no soft-lock",
                stuck == 0 && finished == duels, $"stuck={stuck} finished={finished} {string.Join(" | ", errors)}");
            Check("AI duels: both cards' effects were used (Vampire Baby Summons, Tyrant Dragon Tributes)",
                babyUses > 0 && babyOffers > 0 && tyrantTributes > 0 && tyrantPicks > 0,
                $"babyUses={babyUses} babyOffers={babyOffers} tyrantTributes={tyrantTributes} tyrantPicks={tyrantPicks}");
        }
    }
}
