using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// BattleRiders batch: B.E.S. Big Core, Kangaroo Champ, Poison Fangs, Spiral Spear Strike, Catnipped Kitty, Armed
    /// Samurai - Ben Kei. Compile shapes through the real compiler, battles and activations through the engine entry points
    /// (TryNormalSummon, SpecialSummonToField, TryActivateSpellTrap, TryAttack, response windows, the discard pick, turn
    /// ends), and look-alike sentences that must stay uncompiled. Turn 3 is the Player's, turn 4 the AI's.
    /// Runtime: <see cref="BattleRidersRuntime"/>.
    /// </summary>
    public static class BattleRidersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BesBigCore = 14148099;          // Machine Lv6 2300/1100
        const int BesBigCoreMk2 = 75937826;
        const int BesBigCoreMk3 = 82821760;
        const int KangarooChamp = 95789089;       // Beast Lv4 800/700
        const int PoisonFangs = 76539047;         // Continuous Spell
        const int SpiralSpearStrike = 49328340;   // Continuous Spell
        const int CatnippedKitty = 96501677;      // Beast Lv2 0/500
        const int BenKei = 84430950;              // Warrior Lv4 500/800

        const int GaiaFierceKnight = 6368038;     // Warrior Normal 2300/2100
        const int GaiaDragonChampion = 66889139;  // Dragon Fusion 2600/2100
        const int SilverFang = 90357090;          // Beast Normal 1200/800
        const int CelticGuardian = 91152256;      // Warrior Normal 1400/1200
        const int MysticalElf = 15025844;         // Spellcaster Normal 800/2000
        const int SummonedSkull = 70781052;       // Fiend Normal 2500/1200
        const int AxeRaider = 48305365;           // Warrior Normal 1700/1150
        const int BasicInsect = 89091579;         // Insect Normal 500/700
        const int GoblinAttackForce = 78658564;   // Warrior 2300/0
        const int CallOfTheHaunted = 97077563;    // Continuous Trap: a link recorded in Equips, not an Equip Card
        const int LegendarySword = 61854111;      // Equip Spell: a Warrior gains 300 ATK/DEF

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Chk Check = (name, ok, detail) => check("BattleRiders: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            BigCoreDuels(db, pDeck, aDeck, Check);
            KangarooChampDuels(db, pDeck, aDeck, Check);
            PoisonFangsDuels(db, pDeck, aDeck, Check);
            SpiralSpearStrikeDuels(db, pDeck, aDeck, Check);
            CatnippedKittyDuels(db, pDeck, aDeck, Check);
            BenKeiDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards and hands.</summary>
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
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void Settle(DuelEngine engine)
        {
            engine.NotifyPublic();
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The AI's Battle Phase (turn 4): <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The Player passes turns until their next Main Phase 1 (nobody acts on the turn in between).</summary>
        static bool NextPlayerMain1(DuelEngine engine)
        {
            var start = engine.TurnNumber;
            for (var t = 0; t < 6 && !(engine.TurnPlayer == engine.Player && engine.TurnNumber > start &&
                                       engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
                if (engine.IsAwaitingResponse) engine.PassResponse();
            }

            return engine.TurnPlayer == engine.Player && engine.TurnNumber > start && engine.Phase == DuelPhase.Main1;
        }

        /// <summary>The activator's target pick, when one opened (a lone legal target may resolve with no prompt).</summary>
        static bool PickTarget(DuelEngine engine, CardInstance target)
        {
            if (engine.IsAwaitingEffectTarget)
            {
                var pend = engine.PendingActivation;
                if (pend == null || !pend.LegalTargets.Contains(target)) return false;
                if (!engine.TrySelectEffectTarget(target)) return false;
            }

            Settle(engine);
            return true;
        }

        static CardInstance FaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);
        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} race={c.RaceFilter} " +
                    $"named={c.NamedCard} cond={c.WhileCondition}:{c.ConditionName} chain={c.MakesChainLink}")) + "] " +
                  $"unparsed=[{string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Is(int id, CardEffectStatusKind k) => db.Get(id) != null && CardEffectStatus.Classify(db.Get(id)) == k;
            EffectClause Find(CompiledCardProgram p, EffectActionKind a) =>
                p?.ClauseList.Find(c => c != null && c.Action == a);

            var bc = P(BesBigCore);
            var bcCounter = Find(bc, EffectActionKind.PlaceCounterOnThisCard);
            var bcRemove = Find(bc, EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep);
            Check("B.E.S. Big Core: Normal Summon counters, battle indestructible, end-of-Damage-Step removal or destroy",
                bc != null && bc.FullyCompiled && bc.ClauseList.Count == 3 &&
                bcCounter != null && bcCounter.Timing == EffectTiming.ThisCardSummoned && bcCounter.Amount == 3 &&
                bcCounter.RequiresThisNormalSummoned && !bcCounter.IsOptional &&
                Find(bc, EffectActionKind.CannotBeDestroyedByBattle) != null &&
                bcRemove != null && bcRemove.Timing == EffectTiming.EndOfDamageStep && bcRemove.Amount == 1 &&
                bcRemove.MakesChainLink && !bcRemove.IsOptional &&
                Is(BesBigCore, CardEffectStatusKind.Implemented), Describe(bc));
            Check("B.E.S. Big Core MK-2 / MK-3: still not Implemented (their summon wordings have no atom)",
                !Is(BesBigCoreMk2, CardEffectStatusKind.Implemented) && !Is(BesBigCoreMk3, CardEffectStatusKind.Implemented) &&
                !CardEffectStatus.MayIncludeInDeck(db.Get(BesBigCoreMk2)) &&
                !CardEffectStatus.MayIncludeInDeck(db.Get(BesBigCoreMk3)),
                Describe(P(BesBigCoreMk2)) + " / " + Describe(P(BesBigCoreMk3)));

            var kc = P(KangarooChamp);
            Check("Kangaroo Champ: Prickle Fairy's end-of-Damage-Step change of the battled monster to Defense",
                kc != null && kc.FullyCompiled && kc.ClauseList.Count == 1 &&
                kc.ClauseList[0].Timing == EffectTiming.EndOfDamageStep &&
                kc.ClauseList[0].Action == EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep &&
                kc.ClauseList[0].Zone == EffectZoneFilter.OpponentBattlingMonster && kc.ClauseList[0].MakesChainLink &&
                Is(KangarooChamp, CardEffectStatusKind.Implemented), Describe(kc));

            var pf = P(PoisonFangs);
            var pfc = pf?.ClauseList.FirstOrDefault();
            Check("Poison Fangs: a Beast's battle damage to the opponent burns 500 (Robbin' Zombie shape + Type)",
                pf != null && pf.FullyCompiled && pf.ClauseList.Count == 1 &&
                pfc.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                pfc.Action == EffectActionKind.InflictDamageToOpponent && pfc.Amount == 500 &&
                pfc.AnyControllerMonsterBattleDamage && pfc.RaceFilter == "Beast" && pfc.Side == EffectSide.Opponent &&
                pfc.StaysOnField && !pfc.RequiresDealerBattledDefensePosition &&
                Is(PoisonFangs, CardEffectStatusKind.Implemented), Describe(pf));

            var ss = P(SpiralSpearStrike);
            var ssPierce = Find(ss, EffectActionKind.GrantPiercingToYourNamedMonsters);
            var ssDraw = Find(ss, EffectActionKind.DrawThenDiscard);
            Check("Spiral Spear Strike: named piercing grant + Gaia the Dragon Champion's draw 2, then discard 1",
                ss != null && ss.FullyCompiled && ss.ClauseList.Count == 2 &&
                ssPierce != null && ssPierce.Timing == EffectTiming.ContinuousWhileFaceUp &&
                ssPierce.NamedCard == "Gaia The Fierce Knight|Swift Gaia the Fierce Knight|Gaia the Dragon Champion" &&
                ssDraw != null && ssDraw.Timing == EffectTiming.ThisCardInflictsBattleDamage && ssDraw.Amount == 2 &&
                ssDraw.ThenDiscardCount == 1 && ssDraw.NamedCard == "Gaia the Dragon Champion" &&
                ssDraw.AnyControllerMonsterBattleDamage && ssDraw.RequiresDealerBattledDefensePosition &&
                ssDraw.Side == EffectSide.Controller && string.IsNullOrEmpty(ssDraw.ActivationCondition) &&
                !ss.ClauseList.Exists(c => c.Action == EffectActionKind.Draw) &&
                Is(SpiralSpearStrike, CardEffectStatusKind.Implemented), Describe(ss));

            var ck = P(CatnippedKitty);
            var ckShield = Find(ck, EffectActionKind.CannotBeAttackTarget);
            var ckDef = Find(ck, EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn);
            Check("Catnipped Kitty: shield while you control another face-up Beast + once-per-turn targeted DEF 0",
                ck != null && ck.FullyCompiled && ck.ClauseList.Count == 2 &&
                ckShield != null && ckShield.WhileCondition == AttackShieldsAndDamageZeroRuntime.YouControlAnotherFaceUpRace &&
                ckShield.ConditionName == "Beast" && !ckShield.AllowsDirectAttackWhileProtected &&
                ckDef != null && ckDef.Timing == EffectTiming.Activate && ckDef.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                ckDef.RequiresTargetChoice && ckDef.IsPsctTarget && ckDef.OncePerTurn &&
                ckDef.OptScope == OncePerTurnScope.PerInstance && ckDef.IsOptional &&
                Is(CatnippedKitty, CardEffectStatusKind.Implemented), Describe(ck));

            var bk = P(BenKei);
            Check("Armed Samurai - Ben Kei: 1 extra attack per Equip Card equipped to it",
                bk != null && bk.FullyCompiled && bk.ClauseList.Count == 1 &&
                bk.ClauseList[0].Action == EffectActionKind.ExtraAttacks && bk.ClauseList[0].Amount == 1 &&
                bk.ClauseList[0].ExtraAttacksPerEquippedCard &&
                Is(BenKei, CardEffectStatusKind.Implemented), Describe(bk));

            Check("New actions map to shared resolutions",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep }) ==
                EffectResolutionKind.Destroy &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn }) == EffectResolutionKind.ModifyStats &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.GrantPiercingToYourNamedMonsters }) == EffectResolutionKind.DirectAttack);
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            var near = new (string Desc, string Type, string Race, EffectActionKind Action)[]
            {
                ("At the end of the Damage Step, if this card battled: Remove 1 counter from this card.",
                    "Effect Monster", "Machine", EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep),
                ("At the end of the Damage Step, if this card battled: Remove 1 counter from this card. If you cannot, " +
                 "destroy it and inflict 500 damage to your opponent.",
                    "Effect Monster", "Machine", EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep),
                ("A monster that battles with this card is changed to Defense Position after damage calculation, and it " +
                 "loses 500 ATK.",
                    "Effect Monster", "Beast", EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep),
                ("Each time a Beast-Type monster inflicts Battle Damage to your opponent, inflict 500 damage to your " +
                 "opponent and gain 500 Life Points.",
                    "Spell Card", "Continuous", EffectActionKind.InflictDamageToOpponent),
                ("Each time a Beast-Type monster inflicts Battle Damage to you, inflict 500 damage to your opponent.",
                    "Spell Card", "Continuous", EffectActionKind.InflictDamageToOpponent),
                ("If \"Gaia The Fierce Knight\" or \"Swift Gaia the Fierce Knight\" you control attacks a Defense Position " +
                 "monster, inflict piercing battle damage to your opponent. If \"Curse of Dragon\" inflicts battle damage " +
                 "with this effect: Draw 2 cards, then discard 1 card.",
                    "Spell Card", "Continuous", EffectActionKind.GrantPiercingToYourNamedMonsters),
                ("This card cannot be attacked if there is another Beast-Type monster on your opponent's side of the field.",
                    "Effect Monster", "Beast", EffectActionKind.CannotBeAttackTarget),
                ("Once per turn, you can make the ATK of 1 monster on your opponent's side of the field 0 until the end of " +
                 "this turn.",
                    "Effect Monster", "Beast", EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn),
                ("For each Equip Card equipped to this card, it gains 1 additional attack during each Battle Phase, but it " +
                 "cannot attack directly.",
                    "Effect Monster", "Warrior", EffectActionKind.ExtraAttacks),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90917800 + i, name = "Synthetic BattleRiders near miss " + i, type = n.Type, race = n.Race,
                    desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => c.Action == n.Action));
            }).Select(n => n.Action.ToString()).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(",", leaked));
        }

        // ───────────────────────────── B.E.S. Big Core ─────────────────────────────

        static CardInstance TributeSummonBigCore(DuelEngine engine, out bool ok)
        {
            var p = engine.Player;
            var tribute = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
            var bc = PutInHand(engine, p, BesBigCore);
            engine.PendingTributes.Add(tribute);
            ok = engine.TryNormalSummon(p, bc, asSet: false);
            Settle(engine);
            return bc;
        }

        static void BigCoreDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bc = TributeSummonBigCore(engine, out var ns);
                var placed = bc.Counters;
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var ok = Attack(engine, p, bc, elf);
                Check("B.E.S. Big Core: Tribute Summoned → 3 counters; after it battles, 1 is removed (2 left)",
                    reached && ns && placed == 3 && ok && OnField(p, bc) && bc.Counters == 2 && InGy(o, elf),
                    $"reached={reached} ns={ns} placed={placed} ok={ok} onField={OnField(p, bc)} counters={bc.Counters}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bc = TributeSummonBigCore(engine, out var ns);
                var oppLp = o.LifePoints;
                var ok = Attack(engine, p, bc, null);
                Check("B.E.S. Big Core: a direct attack is a battle too (1 counter removed)",
                    reached && ns && ok && OnField(p, bc) && bc.Counters == 2 && o.LifePoints == oppLp - 2300,
                    $"ok={ok} counters={bc.Counters} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bc = TributeSummonBigCore(engine, out var ns);
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, skull, bc);
                Check("B.E.S. Big Core: attacked by a stronger monster — not destroyed by battle, 1 counter removed",
                    reached && ns && ok && OnField(p, bc) && bc.Counters == 2 && OnField(o, skull),
                    $"ok={ok} onField={OnField(p, bc)} counters={bc.Counters}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bc = TributeSummonBigCore(engine, out var ns);
                bc.Counters = 1; // the counters of this presence, as after two earlier battles
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var ok = Attack(engine, p, bc, celtic);
                Check("B.E.S. Big Core: its last counter is removed and it stays on the field",
                    reached && ns && ok && OnField(p, bc) && bc.Counters == 0 && InGy(o, celtic),
                    $"ok={ok} onField={OnField(p, bc)} counters={bc.Counters}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bc = engine.CreateCardInstance(BesBigCore);
                var ss = engine.SpecialSummonToField(p, bc, BattlePosition.Attack, true);
                Settle(engine);
                var counters = bc.Counters;
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var ok = Attack(engine, p, bc, celtic);
                Check("B.E.S. Big Core: Special Summoned → no counters; it cannot remove one, so it is destroyed",
                    reached && ss && counters == 0 && ok && InGy(p, bc) && InGy(o, celtic),
                    $"ss={ss} counters={counters} ok={ok} inGy={InGy(p, bc)} celticGy={InGy(o, celtic)}");
            }
        }

        // ───────────────────────────── Kangaroo Champ ─────────────────────────────

        static void KangarooChampDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kc = PlaceMonster(engine, p, KangarooChamp, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, kc);
                Check("Kangaroo Champ: destroyed by the attacker, it still changes that monster to face-up Defense",
                    reached && ok && InGy(p, kc) && OnField(o, axe) && axe.FaceUp &&
                    axe.Position == BattlePosition.Defense,
                    $"ok={ok} kcGy={InGy(p, kc)} axe={OnField(o, axe)} pos={axe.Position}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kc = PlaceMonster(engine, p, KangarooChamp, 0, BattlePosition.Attack, true);
                var fang = PlaceMonster(engine, o, SilverFang, 0, BattlePosition.Attack, true);
                var ok = Attack(engine, p, kc, fang);
                Check("Kangaroo Champ: the monster it attacked is changed to Defense Position after the battle",
                    reached && ok && InGy(p, kc) && OnField(o, fang) && fang.FaceUp &&
                    fang.Position == BattlePosition.Defense,
                    $"ok={ok} kcGy={InGy(p, kc)} fang={OnField(o, fang)} pos={fang.Position}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kc = PlaceMonster(engine, p, KangarooChamp, 0, BattlePosition.Attack, true);
                var insect = PlaceMonster(engine, o, BasicInsect, 0, BattlePosition.Attack, true);
                var ok = Attack(engine, p, kc, insect);
                Check("Kangaroo Champ: a battled monster destroyed by the battle is left alone; Kangaroo Champ stays",
                    reached && ok && OnField(p, kc) && kc.Position == BattlePosition.Attack && InGy(o, insect),
                    $"ok={ok} kc={OnField(p, kc)} pos={kc.Position} insectGy={InGy(o, insect)}");
            }
        }

        // ───────────────────────────── Poison Fangs ─────────────────────────────

        static void PoisonFangsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var pf = PutInHand(engine, p, PoisonFangs);
                var act = engine.TryActivateSpellTrap(p, pf, fromHand: true);
                Settle(engine);
                var placed = p.TryFindSpellTrap(pf, out _) && pf.FaceUp;
                var fang = PlaceMonster(engine, p, SilverFang, 0, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                var ok = Attack(engine, p, fang, null);
                Check("Poison Fangs: a Beast's direct attack → its battle damage + 500",
                    reached && act && placed && ok && o.LifePoints == oppLp - 1200 - 500,
                    $"act={act} placed={placed} ok={ok} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, PoisonFangs, 0);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                var ok = Attack(engine, p, celtic, null);
                Check("Poison Fangs: a Warrior's battle damage → no burn",
                    reached && ok && o.LifePoints == oppLp - 1400, $"ok={ok} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, PoisonFangs, 0);
                var fang = PlaceMonster(engine, p, SilverFang, 0, BattlePosition.Attack, true);
                var insect = PlaceMonster(engine, o, BasicInsect, 0, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                var ok = OpponentAttacks(engine, insect, fang);
                Check("Poison Fangs: the attacked Beast inflicts the battle damage → + 500",
                    reached && ok && OnField(p, fang) && o.LifePoints == oppLp - 700 - 500,
                    $"ok={ok} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, PoisonFangs, 0);
                var oppFang = PlaceMonster(engine, o, SilverFang, 0, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                var myLp = p.LifePoints;
                var ok = OpponentAttacks(engine, oppFang, null);
                Check("Poison Fangs: the opponent's Beast damaging you → no burn to anyone",
                    reached && ok && p.LifePoints == myLp - 1200 && o.LifePoints == oppLp,
                    $"ok={ok} myLp={p.LifePoints} was {myLp} oppLp={o.LifePoints} was {oppLp}");
            }
        }

        // ───────────────────────────── Spiral Spear Strike ─────────────────────────────

        static void SpiralSpearStrikeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ss = PutInHand(engine, p, SpiralSpearStrike);
                var act = engine.TryActivateSpellTrap(p, ss, fromHand: true);
                Settle(engine);
                var gaia = PlaceMonster(engine, p, GaiaFierceKnight, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var oppLp = o.LifePoints;
                var hand = p.HandCount;
                var ok = Attack(engine, p, gaia, elf);
                Check("Spiral Spear Strike: Gaia The Fierce Knight inflicts piercing damage (no draw: not the Champion)",
                    reached && act && ok && InGy(o, elf) && o.LifePoints == oppLp - 300 && p.HandCount == hand &&
                    !engine.IsAwaitingEffectTarget,
                    $"act={act} ok={ok} oppLp={o.LifePoints} was {oppLp} hand={p.HandCount} was {hand}");
            }

            {
                // The AI controls it: Gaia the Dragon Champion pierces, the AI draws 2 and discards 1.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, o, SpiralSpearStrike, 0);
                var champ = PlaceMonster(engine, o, GaiaDragonChampion, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Defense, true);
                var myLp = p.LifePoints;
                var reachedBattle = ReachOpponentBattle(engine);
                o.Hand.Clear();
                var deck = o.Deck.Count;
                var gy = o.Graveyard.Count;
                var ok = engine.TryAttack(o, champ, elf);
                DrainCombat(engine);
                Check("Spiral Spear Strike (AI): Gaia the Dragon Champion pierces, then its controller draws 2 and " +
                      "discards 1",
                    reached && reachedBattle && ok && p.LifePoints == myLp - 600 && InGy(p, elf) && o.HandCount == 1 &&
                    o.Deck.Count == deck - 2 && o.Graveyard.Count == gy + 1,
                    $"battle={reachedBattle} ok={ok} myLp={p.LifePoints} was {myLp} hand={o.HandCount} " +
                    $"deck={o.Deck.Count} was {deck} gy={o.Graveyard.Count} was {gy}");
            }

            {
                // The Player controls it: the discard is the Player's choice (the Graceful Charity pick).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, SpiralSpearStrike, 0);
                var champ = PlaceMonster(engine, p, GaiaDragonChampion, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var oppLp = o.LifePoints;
                var deck = p.Deck.Count;
                var ok = Attack(engine, p, champ, elf);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.AwaitingDrawThenDiscard && pend.Controller == p &&
                            pend.TargetPicksRemaining == 1 && pend.LegalTargets.Count == 2;
                var pick = p.Hand.FirstOrDefault();
                var picked = pick != null && engine.TrySelectEffectTarget(pick);
                Check("Spiral Spear Strike (player): pierce, draw 2, then the player chooses the discard",
                    reached && ok && o.LifePoints == oppLp - 600 && asked && picked && !engine.IsAwaitingEffectTarget &&
                    p.HandCount == 1 && p.Deck.Count == deck - 2 && InGy(p, pick),
                    $"ok={ok} oppLp={o.LifePoints} was {oppLp} asked={asked} picked={picked} hand={p.HandCount} " +
                    $"deck={p.Deck.Count} was {deck}");
                var ended = engine.TryEndTurnSafe(p);
                Check("Spiral Spear Strike (player): the turn goes on after the pick (no soft-lock)",
                    ended && engine.TurnPlayer == o, $"ended={ended} turnPlayer={engine.TurnPlayer?.Name}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, SpiralSpearStrike, 0);
                var champ = PlaceMonster(engine, p, GaiaDragonChampion, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var oppLp = o.LifePoints;
                var hand = p.HandCount;
                var ok = Attack(engine, p, champ, celtic);
                Check("Spiral Spear Strike: battle damage against an Attack Position monster → no draw",
                    reached && ok && o.LifePoints == oppLp - 1200 && p.HandCount == hand && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} oppLp={o.LifePoints} was {oppLp} hand={p.HandCount} was {hand}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, SpiralSpearStrike, 0);
                var skull = PlaceMonster(engine, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var oppLp = o.LifePoints;
                var ok = Attack(engine, p, skull, elf);
                Check("Spiral Spear Strike: a monster it does not name gets no piercing damage",
                    reached && ok && InGy(o, elf) && o.LifePoints == oppLp, $"ok={ok} oppLp={o.LifePoints} was {oppLp}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSpell(engine, p, SpiralSpearStrike, 0);
                var champ = PlaceMonster(engine, p, GaiaDragonChampion, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                p.Deck.Clear();
                var oppLp = o.LifePoints;
                var ok = Attack(engine, p, champ, elf);
                Check("Spiral Spear Strike: with an empty Deck the draw does nothing (no loss, no discard)",
                    reached && ok && o.LifePoints == oppLp - 600 && !engine.GameOver && p.HandCount == 0 &&
                    !engine.IsAwaitingEffectTarget,
                    $"ok={ok} oppLp={o.LifePoints} was {oppLp} over={engine.GameOver} hand={p.HandCount}");
            }
        }

        // ───────────────────────────── Catnipped Kitty ─────────────────────────────

        static void CatnippedKittyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kitty = PlaceMonster(engine, p, CatnippedKitty, 0, BattlePosition.Attack, true);
                var fang = PlaceMonster(engine, p, SilverFang, 1, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var reachedBattle = ReachOpponentBattle(engine);
                var blocked = !engine.TryAttack(o, axe, kitty);
                var direct = engine.CanAttackDirectly(o, axe);
                var fangOk = engine.TryAttack(o, axe, fang);
                DrainCombat(engine);
                Check("Catnipped Kitty: with another face-up Beast it cannot be attacked (no direct attack either); the " +
                      "Beast can be",
                    reached && reachedBattle && blocked && !direct && fangOk && OnField(p, kitty) && InGy(p, fang),
                    $"battle={reachedBattle} blocked={blocked} direct={direct} fangOk={fangOk}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kitty = PlaceMonster(engine, p, CatnippedKitty, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, SilverFang, 1, BattlePosition.Defense, false);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, kitty);
                Check("Catnipped Kitty: a face-down Beast does not shield it",
                    reached && ok && InGy(p, kitty), $"ok={ok} kittyGy={InGy(p, kitty)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kitty = PlaceMonster(engine, p, CatnippedKitty, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var can = engine.CanActivateSpellTrap(p, kitty, fromHand: false);
                var act = engine.TryActivateSpellTrap(p, kitty, fromHand: false) && PickTarget(engine, elf);
                var def = elf.CurrentDef;
                var again = engine.CanActivateSpellTrap(p, kitty, fromHand: false);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 1, BattlePosition.Attack, true);
                var hit = Attack(engine, p, celtic, elf);
                Check("Catnipped Kitty: once per turn, the targeted opponent's monster has 0 DEF until the end of the turn",
                    reached && can && act && def == 0 && !again && hit && InGy(o, elf),
                    $"can={can} act={act} def={def} again={again} hit={hit} elfGy={InGy(o, elf)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kitty = PlaceMonster(engine, p, CatnippedKitty, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var act = engine.TryActivateSpellTrap(p, kitty, fromHand: false) && PickTarget(engine, elf);
                var def = elf.CurrentDef;
                var next = NextPlayerMain1(engine);
                Check("Catnipped Kitty: the DEF returns at the end of the turn",
                    reached && act && def == 0 && next && OnField(o, elf) && elf.CurrentDef == 2000,
                    $"act={act} def={def} next={next} now={elf.CurrentDef}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kitty = PlaceMonster(engine, p, CatnippedKitty, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var act = engine.TryActivateSpellTrap(p, kitty, fromHand: false) && PickTarget(engine, elf);
                var def = elf.CurrentDef;
                engine.SendCardToGrave(o, elf);
                engine.NotifyPublic();
                Check("Catnipped Kitty: the DEF 0 ends when the monster leaves the field (RESETS_STANDARD)",
                    reached && act && def == 0 && InGy(o, elf) && !elf.DefZeroUntilEndOfTurn && elf.CurrentDef == 2000,
                    $"act={act} def={def} gy={InGy(o, elf)} mark={elf.DefZeroUntilEndOfTurn} now={elf.CurrentDef}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var kitty = PlaceMonster(engine, p, CatnippedKitty, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, o, GoblinAttackForce, 1, BattlePosition.Attack, true);
                var can = engine.CanActivateSpellTrap(p, kitty, fromHand: false);
                var act = engine.TryActivateSpellTrap(p, kitty, fromHand: false);
                Check("Catnipped Kitty: no face-up opponent's monster with DEF above 0 → cannot be activated",
                    reached && !can && !act, $"can={can} act={act}");
            }
        }

        // ───────────────────────────── Armed Samurai - Ben Kei ─────────────────────────────

        static int AttacksMade(DuelEngine engine, DuelistState who, CardInstance attacker, int tries)
        {
            var made = 0;
            for (var i = 0; i < tries; i++)
                if (Attack(engine, who, attacker, null)) made++;
            return made;
        }

        static void BenKeiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var bk = PlaceMonster(engine, p, BenKei, 0, BattlePosition.Attack, true);
                var extra = engine.ExtraAttacksAllowed(bk);
                var made = AttacksMade(engine, p, bk, 3);
                Check("Armed Samurai - Ben Kei: no Equip Card → 1 attack", reached && extra == 0 && made == 1,
                    $"extra={extra} made={made}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bk = PlaceMonster(engine, p, BenKei, 0, BattlePosition.Attack, true);
                var s1 = PutInHand(engine, p, LegendarySword);
                var a1 = engine.TryActivateSpellTrap(p, s1, fromHand: true) && PickTarget(engine, bk);
                var extraOne = engine.ExtraAttacksAllowed(bk);
                var made = AttacksMade(engine, p, bk, 3);
                Check("Armed Samurai - Ben Kei: 1 Equip Card → 2 attacks",
                    reached && a1 && ReferenceEquals(s1.EquippedTo, bk) && extraOne == 1 && made == 2,
                    $"a1={a1} host={s1.EquippedTo?.Name} extra={extraOne} made={made} oppLp={o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bk = PlaceMonster(engine, p, BenKei, 0, BattlePosition.Attack, true);
                var s1 = PutInHand(engine, p, LegendarySword);
                var a1 = engine.TryActivateSpellTrap(p, s1, fromHand: true) && PickTarget(engine, bk);
                var s2 = PutInHand(engine, p, LegendarySword);
                var a2 = engine.TryActivateSpellTrap(p, s2, fromHand: true) && PickTarget(engine, bk);
                var extraTwo = engine.ExtraAttacksAllowed(bk);
                var oppLp = o.LifePoints;
                var made = AttacksMade(engine, p, bk, 4);
                var dealt = oppLp - o.LifePoints;
                var next = NextPlayerMain1(engine);
                var madeAgain = next ? AttacksMade(engine, p, bk, 4) : -1;
                Check("Armed Samurai - Ben Kei: 2 Equip Cards → 3 attacks in each Battle Phase",
                    reached && a1 && a2 && extraTwo == 2 && made == 3 && dealt == 3 * 1100 && next && madeAgain == 3,
                    $"a1={a1} a2={a2} extra={extraTwo} made={made} dealt={dealt} next={next} again={madeAgain}");

                TextEffectRuntime.DestroyCard(engine, s2);
                Settle(engine);
                Check("Armed Samurai - Ben Kei: an Equip Card gone → 1 extra attack (counted live)",
                    !p.TryFindSpellTrap(s2, out _) && engine.ExtraAttacksAllowed(bk) == 1,
                    $"s2OnField={p.TryFindSpellTrap(s2, out _)} extra={engine.ExtraAttacksAllowed(bk)}");
            }

            {
                // v210 cross-group audit: Call of the Haunted's link (recorded in Equips, Lua SetCardTarget) is not an
                // Equip Card, so it gives no extra attack (Lua c:GetEquipCount()).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var bk = PlaceMonster(engine, p, BenKei, 0, BattlePosition.Attack, true);
                var coth = PlaceSetTrap(engine, p, CallOfTheHaunted, 0);
                coth.FaceUp = true;
                coth.EquippedTo = bk;
                bk.Equips.Add(coth);
                engine.NotifyPublic();
                var extra = engine.ExtraAttacksAllowed(bk);
                Check("Armed Samurai - Ben Kei: Call of the Haunted's link is not an Equip Card → no extra attack",
                    reached && extra == 0, $"extra={extra}");
            }
        }
    }
}
