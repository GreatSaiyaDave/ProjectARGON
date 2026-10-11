using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// PhaseTriggersAndIgnitionCosts batch: Amazoness Blowpiper, Helping Robo for Combat, Mass Driver, Senri Eye, Magical
    /// Scientist, Roulette Barrel, Physical Double. Compile shapes through the real compiler, near misses that must stay
    /// uncompiled, and mini-duels through the engine entry points (turn changes, TryActivateSpellTrap, TryAttack,
    /// TrySelectEffectTarget / CancelEffectTargeting). Runtime: <see cref="PhaseTriggersAndIgnitionCostsRuntime"/>.
    /// </summary>
    public static class PhaseTriggersAndIgnitionCostsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Blowpiper = 73574678;
        const int HelpingRobo = 47025270;
        const int MassDriver = 34906152;
        const int SenriEye = 60391791;
        const int MagicalScientist = 34206604;
        const int RouletteBarrel = 46303688;
        const int PhysicalDouble = 63442604;

        const int Celtic = 91152256;          // Normal Warrior EARTH Lv4 1400/1200
        const int GiantSoldier = 13039848;    // Normal Rock EARTH Lv3 1300/2000
        const int Kuriboh = 40640057;         // Lv1 300/200
        const int PotOfGreed = 55144522;
        const int FlameSwordsman = 45231177;  // Fusion Lv5 1800/1600
        const int ThousandDragon = 41462083;  // Fusion Lv7 2400/2000

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("PhaseTriggersAndIgnitionCosts: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);
            BlowpiperDuels(db, pDeck, aDeck, Check);
            HelpingRoboDuels(db, pDeck, aDeck, Check);
            MassDriverDuels(db, pDeck, aDeck, Check);
            SenriEyeDuels(db, pDeck, aDeck, Check);
            MagicalScientistDuels(db, pDeck, aDeck, Check);
            RouletteBarrelDuels(db, pDeck, aDeck, Check);
            PhysicalDoubleDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", p.UnparsedFragments ?? Array.Empty<string>())}";

        static EffectClause Only(CompiledCardProgram p) => p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CompiledCardProgram Synthetic(CardDatabase db, int likeId, string desc)
        {
            var like = db.Get(likeId);
            return CardTextEffectCompiler.Compile(new CardDef
            {
                id = 0,
                name = "PhaseTriggersAndIgnitionCosts near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            });
        }

        /// <summary>The player's Main Phase 1 on turn 3+ (a Battle Phase is allowed); boards, hands and GYs empty.</summary>
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
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        static CardInstance Settled(DuelEngine engine, DuelistState who, int id, int zone, BattlePosition pos,
            bool faceUp = true)
        {
            var c = PlaceMonster(engine, who, id, zone, pos, faceUp);
            c.FaceUpStampAtTurnStart = c.FaceUpStamp;
            return c;
        }

        static CardInstance FaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static void EndTurn(DuelEngine engine)
        {
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static List<string> Capture(DuelEngine engine)
        {
            var lines = new List<string>();
            engine.OnLog += s => lines.Add(s);
            return lines;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var bp = Prog(db, Blowpiper);
            var bc = Only(bp);
            Check("Amazoness Blowpiper: one mandatory own-Standby trigger, opp face-up target, 500 ATK",
                Implemented(db, Blowpiper) && bp.FullyCompiled && bc != null &&
                bc.Timing == EffectTiming.StandbyPhase &&
                bc.Action == EffectActionKind.TargetOppFaceUpLosesAtkAtYourStandby &&
                bc.Zone == EffectZoneFilter.OppFaceUpMonsters && bc.IsPsctTarget && bc.Amount == 500 &&
                !bc.IsOptional && !bc.OpponentTurnOnly &&
                EffectVocabulary.ResolutionOf(bc) == EffectResolutionKind.ModifyStats,
                Describe(bp));

            var hr = Prog(db, HelpingRobo);
            var hc = Only(hr);
            Check("Helping Robo for Combat: one mandatory battle-destroying trigger, draw 1 then 1 card to the Deck bottom",
                Implemented(db, HelpingRobo) && hr.FullyCompiled && hc != null &&
                hc.Timing == EffectTiming.ThisCardDestroysByBattle &&
                hc.Action == EffectActionKind.DrawThenPlaceHandCardOnDeckBottom && hc.Amount == 1 && !hc.IsOptional &&
                EffectVocabulary.ResolutionOf(hc) == EffectResolutionKind.Draw,
                Describe(hr));

            var md = Prog(db, MassDriver);
            var mc = Only(md);
            Check("Mass Driver: one face-up field ignition, Tribute 1 (cost), 400 damage, no activation clause",
                Implemented(db, MassDriver) && md.FullyCompiled && mc != null && !md.HasTiming(EffectTiming.Activate) &&
                mc.Timing == EffectTiming.FaceUpFieldIgnition && mc.Action == EffectActionKind.InflictDamageToOpponent &&
                mc.RequiresTributeCount == 1 && mc.Amount == 400 && !mc.RequiresSendThisToGy && !mc.OncePerTurn &&
                string.IsNullOrEmpty(mc.CountSource) && PhaseTriggersAndIgnitionCostsRuntime.IsTributeBurnIgnition(md),
                Describe(md));

            var se = Prog(db, SenriEye);
            var sc = Only(se);
            Check("Senri Eye: one optional own-Standby trigger, 100 LP, no activation clause",
                Implemented(db, SenriEye) && se.FullyCompiled && sc != null && !se.HasTiming(EffectTiming.Activate) &&
                sc.Timing == EffectTiming.StandbyPhase &&
                sc.Action == EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby &&
                sc.Amount == 100 && sc.IsOptional && !sc.OpponentTurnOnly && sc.PayLpAmount == 0,
                Describe(se));

            var ms = Prog(db, MagicalScientist);
            var msc = Only(ms);
            Check("Magical Scientist: one ignition, 1000 LP cost, Level 6 or lower Fusion, unlimited",
                Implemented(db, MagicalScientist) && ms.FullyCompiled && msc != null &&
                msc.Timing == EffectTiming.Activate &&
                msc.Action == EffectActionKind.SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase &&
                msc.PayLpAmount == 1000 && msc.Amount == 6 && !msc.OncePerTurn && !msc.RequiresTargetChoice &&
                msc.ActivationCondition == PhaseTriggersAndIgnitionCostsRuntime.CanSummonLevelCappedFusionFromExtra &&
                EffectVocabulary.ResolutionOf(msc) == EffectResolutionKind.SpecialSummon,
                Describe(ms));

            var rb = Prog(db, RouletteBarrel);
            var rc = Only(rb);
            Check("Roulette Barrel: one once-per-turn ignition, no target",
                Implemented(db, RouletteBarrel) && rb.FullyCompiled && rc != null &&
                rc.Timing == EffectTiming.Activate &&
                rc.Action == EffectActionKind.RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel &&
                rc.OncePerTurn && !rc.RequiresTargetChoice &&
                EffectVocabulary.ResolutionOf(rc) == EffectResolutionKind.Destroy,
                Describe(rb));

            var pd = Prog(db, PhysicalDouble);
            var pc = Only(pd);
            Check("Physical Double: opponent's turn only, targets 1 face-up opp monster, \"Mirage Token\"",
                Implemented(db, PhysicalDouble) && pd.FullyCompiled && pc != null &&
                pc.Timing == EffectTiming.Activate &&
                pc.Action == EffectActionKind.SpecialSummonTokenCopyingTargetDestroyAtEndPhase &&
                pc.Zone == EffectZoneFilter.OppFaceUpMonsters && pc.RequiresTargetChoice && pc.RequiresTargetFaceUp &&
                pc.OpponentTurnOnly && pc.TokenName == "Mirage Token" &&
                EffectVocabulary.ResolutionOf(pc) == EffectResolutionKind.Token,
                Describe(pd));
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var misses = new (int like, string label, string desc)[]
            {
                (Blowpiper, "Blowpiper on each Standby Phase (either player's)",
                    "Select 1 face-up monster your opponent controls during each Standby Phase. It loses 500 ATK until " +
                    "the end of this turn."),
                (HelpingRobo, "Helping Robo drawing 2",
                    "If this card destroys an opponent's monster by battle: Draw 2 cards, then return 1 card from your " +
                    "hand to the bottom of the Deck."),
                (MassDriver, "Mass Driver burning both players",
                    "Tribute 1 monster on your side of the field to inflict 400 points of damage to your opponent's Life " +
                    "Points and 400 points of damage to your Life Points."),
                (SenriEye, "Senri Eye in each Standby Phase (either player's)",
                    "Once per turn, during each Standby Phase, you can pay 100 Life Points to look at 1 card on the top " +
                    "of your opponent's Deck and return it to its original position. Your opponent cannot look at the card."),
                (MagicalScientist, "Magical Scientist in face-up Attack Position only",
                    "Pay 1000 Life Points to Special Summon 1 level 6 or lower Fusion Monster from your Extra Deck in " +
                    "face-up Attack Position. That Fusion Monster cannot attack your opponent's Life Points directly, and " +
                    "is returned to your Extra Deck at the end of the turn."),
                (RouletteBarrel, "Roulette Barrel rolling once",
                    "Once per turn: You can roll a six-sided die, and destroy 1 monster on the field whose Level is equal " +
                    "to the result."),
                (PhysicalDouble, "Physical Double during your turn",
                    "During your turn: Target 1 face-up monster your opponent controls; Special Summon 1 \"Mirage Token\" " +
                    "with the same Level, Type, Attribute, ATK, and DEF as that target. Destroy this Token during the End " +
                    "Phase."),
            };
            foreach (var (like, label, desc) in misses)
            {
                var p = Synthetic(db, like, desc);
                var batchAction = p != null && p.ClauseList.Exists(c => c != null &&
                    (c.Action == EffectActionKind.TargetOppFaceUpLosesAtkAtYourStandby ||
                     c.Action == EffectActionKind.DrawThenPlaceHandCardOnDeckBottom ||
                     c.Action == EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby ||
                     c.Action == EffectActionKind.SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase ||
                     c.Action == EffectActionKind.RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel ||
                     c.Action == EffectActionKind.SpecialSummonTokenCopyingTargetDestroyAtEndPhase));
                Check($"near miss stays fail-closed: {label}",
                    p != null && !p.FullyCompiled && !batchAction, Describe(p));
            }
        }

        // ───────────────────────────── Amazoness Blowpiper ─────────────────────────────

        static void BlowpiperDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Not in the opponent's Standby Phase; in its controller's, the human picks 1 of 2 face-up monsters while
                // the Standby Phase waits; -500 ATK until the end of the turn.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                Settled(engine, p, Blowpiper, 0, BattlePosition.Attack);
                var celtic = Settled(engine, o, Celtic, 0, BattlePosition.Attack);
                var giant = Settled(engine, o, GiantSoldier, 1, BattlePosition.Defense);
                Settled(engine, o, Kuriboh, 2, BattlePosition.Defense, faceUp: false);
                EndTurn(engine); // the AI's turn
                var quiet = engine.TurnPlayer == o && engine.PendingActivation == null && celtic.CurrentAtk == 1400;
                EndTurn(engine); // the player's Standby Phase
                var standby = engine.Phase == DuelPhase.Standby && engine.TurnPlayer == p;
                var legal = Legal(engine);
                var picked = legal.Count == 2 && legal.Contains(celtic) && legal.Contains(giant) &&
                             engine.TrySelectEffectTarget(giant);
                Settle(engine);
                var lowered = giant.CurrentAtk == 800 && celtic.CurrentAtk == 1400;
                var main = engine.Phase == DuelPhase.Main1 && engine.PendingActivation == null;
                EndTurn(engine);
                Check("Amazoness Blowpiper: own Standby only; the human targets 1 of 2 face-up monsters, -500 ATK this turn",
                    reached && quiet && standby && picked && lowered && main && giant.CurrentAtk == 1300,
                    $"quiet={quiet} standby={standby} legal={string.Join(",", legal.Select(t => t.Name))} picked={picked} " +
                    $"atk={giant.CurrentAtk}/{celtic.CurrentAtk} main={main}");
            }

            {
                // The AI's Blowpiper: no prompt, the strongest face-up monster of the player loses 500 ATK.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                Settled(engine, o, Blowpiper, 0, BattlePosition.Attack);
                var celtic = Settled(engine, p, Celtic, 0, BattlePosition.Attack);
                var giant = Settled(engine, p, GiantSoldier, 1, BattlePosition.Defense);
                EndTurn(engine); // the AI's Standby Phase
                Check("Amazoness Blowpiper (AI): no prompt; the strongest face-up monster loses 500 ATK",
                    reached && engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null && celtic.CurrentAtk == 900 && giant.CurrentAtk == 1300,
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} atk={celtic.CurrentAtk}/{giant.CurrentAtk}");
            }

            {
                // No face-up monster the opponent controls (a Set one only): nothing happens, the turn goes on.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                Settled(engine, p, Blowpiper, 0, BattlePosition.Attack);
                var set = Settled(engine, o, Celtic, 0, BattlePosition.Defense, faceUp: false);
                EndTurn(engine);
                EndTurn(engine);
                Check("Amazoness Blowpiper: no face-up target → no prompt, Main Phase 1",
                    reached && engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null && !set.FaceUp && set.UntilEndOfTurnAtk == 0,
                    $"phase={engine.Phase} pend={engine.PendingActivation != null}");
            }

            {
                // v175 audit (EquipLocksAndLeaveField): the player's Metalsilver Armor on Giant Soldier — the AI's Blowpiper
                // can target only the equipped monster, not the stronger Celtic.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                Settled(engine, o, Blowpiper, 0, BattlePosition.Attack);
                var celtic = Settled(engine, p, Celtic, 0, BattlePosition.Attack);
                var giant = Settled(engine, p, GiantSoldier, 1, BattlePosition.Attack);
                var armor = PutInHand(engine, p, 33114323); // Metalsilver Armor
                var act = engine.TryActivateSpellTrap(p, armor, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(giant);
                Settle(engine);
                var equipped = ReferenceEquals(armor.EquippedTo, giant);
                EndTurn(engine); // the AI's Standby Phase
                Check("Amazoness Blowpiper (AI) vs Metalsilver Armor: only the equipped monster can be targeted",
                    reached && act && equipped && engine.TurnPlayer == o && celtic.CurrentAtk == 1400 &&
                    giant.CurrentAtk == 800,
                    $"act={act} equipped={equipped} turn={engine.TurnPlayer?.Name} atk={celtic.CurrentAtk}/{giant.CurrentAtk}");
            }

            {
                // v175 audit (ChainLinkAnswers): Chaos Command Magician negates a monster effect that targets only it.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                Settled(engine, o, Blowpiper, 0, BattlePosition.Attack);
                var ccm = Settled(engine, engine.Player, 72630549, 0, BattlePosition.Attack); // Chaos Command Magician
                EndTurn(engine); // the AI's Standby Phase
                Check("Amazoness Blowpiper (AI) vs Chaos Command Magician: the effect that targets only it is negated",
                    reached && engine.TurnPlayer == o && engine.PendingActivation == null && ccm.CurrentAtk == 2400,
                    $"turn={engine.TurnPlayer?.Name} atk={ccm.CurrentAtk}");
            }
        }

        // ───────────────────────────── Helping Robo for Combat ─────────────────────────────

        static void HelpingRoboDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var robo = Settled(engine, p, HelpingRobo, 0, BattlePosition.Attack);
                var prey = Settled(engine, o, Kuriboh, 0, BattlePosition.Attack);
                var pot = PutInHand(engine, p, PotOfGreed);
                PutInHand(engine, p, Celtic);
                var topId = p.Deck.Count > 0 ? p.Deck[0] : 0;
                var deckBefore = p.Deck.Count;
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var attacked = engine.TryAttack(p, robo, prey);
                for (var i = 0; i < 12 && !engine.IsAwaitingEffectTarget; i++)
                {
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    else if (engine.HasDeclaredAttack) engine.ResolveDeclaredAttack();
                    else break;
                }

                var drew = p.Hand.Count == 3 && p.Hand.Exists(h => h.CardId == topId) && p.Deck.Count == deckBefore - 1;
                var legal = Legal(engine);
                var picked = legal.Count == 3 && legal.Contains(pot) && engine.TrySelectEffectTarget(pot);
                DrainCombat(engine);
                Check("Helping Robo for Combat: destroys a monster by battle → draw 1, the human puts 1 hand card on the " +
                      "Deck bottom",
                    reached && attacked && o.Graveyard.Contains(prey) && drew && picked && p.Hand.Count == 2 &&
                    !p.Hand.Contains(pot) && p.Deck.Count == deckBefore && p.Deck[p.Deck.Count - 1] == PotOfGreed,
                    $"att={attacked} drew={drew} legal={legal.Count} picked={picked} hand={p.Hand.Count} " +
                    $"deck={p.Deck.Count}/{deckBefore}");
            }

            {
                // The AI's Robo destroys the player's monster: no prompt, 1 card goes to its Deck bottom.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var p = engine.Player;
                var o = engine.Opponent;
                var reached = r0 && ReachOpponentBattle(engine);
                ClearBoard(engine);
                o.Hand.Clear();
                var robo = Settled(engine, o, HelpingRobo, 0, BattlePosition.Attack);
                var prey = Settled(engine, p, Kuriboh, 0, BattlePosition.Attack);
                PutInHand(engine, o, PotOfGreed);
                var deckBefore = o.Deck.Count;
                var attacked = engine.TryAttack(o, robo, prey);
                DrainCombat(engine);
                Check("Helping Robo for Combat (AI): draw 1, then 1 card to the Deck bottom, no prompt",
                    reached && attacked && p.Graveyard.Contains(prey) && o.Hand.Count == 1 &&
                    o.Deck.Count == deckBefore && engine.PendingActivation == null,
                    $"reached={reached} att={attacked} hand={o.Hand.Count} deck={o.Deck.Count}/{deckBefore}");
            }
        }

        // ───────────────────────────── Mass Driver ─────────────────────────────

        static void MassDriverDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 2 monsters: the human picks the Tribute; 400 damage; Mass Driver stays; it can be used again.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = Settled(engine, p, Celtic, 0, BattlePosition.Attack);
                var giant = Settled(engine, p, GiantSoldier, 1, BattlePosition.Defense, faceUp: false);
                var driver = FaceUpSpell(engine, p, MassDriver, 0);
                var can = engine.CanActivateSpellTrap(p, driver, fromHand: false);
                var activated = engine.TryActivateSpellTrap(p, driver, fromHand: false);
                var legal = Legal(engine);
                var kind = engine.PendingActivation?.TargetKind;
                var picked = legal.Count == 2 && legal.Contains(giant) && engine.TrySelectEffectTarget(giant);
                Settle(engine);
                var first = o.LifePoints == 7600 && p.Graveyard.Contains(giant) && OnField(p, celtic) &&
                            p.TryFindSpellTrap(driver, out _) && driver.FaceUp && !engine.Chain.HasLinks;
                var again = engine.CanActivateSpellTrap(p, driver, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, driver, fromHand: false);
                Settle(engine);
                Check("Mass Driver: the human Tributes 1 of 2 monsters (face-down allowed); 400 damage; it stays and " +
                      "can be used again (the lone monster: no prompt)",
                    reached && can && activated && kind == EffectTargetKind.TributeMonsterYouControl && picked && first &&
                    again && o.LifePoints == 7200 && p.Graveyard.Contains(celtic) && p.MonsterCount == 0 &&
                    !engine.CanActivateSpellTrap(p, driver, fromHand: false),
                    $"can={can} act={activated} legal={legal.Count} kind={kind} picked={picked} first={first} " +
                    $"again={again} lp={o.LifePoints}");
            }

            {
                // Cancel at the Tribute pick: the activation never happened.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                Settled(engine, p, Celtic, 0, BattlePosition.Attack);
                Settled(engine, p, GiantSoldier, 1, BattlePosition.Attack);
                var driver = FaceUpSpell(engine, p, MassDriver, 0);
                var activated = engine.TryActivateSpellTrap(p, driver, fromHand: false) && engine.IsAwaitingEffectTarget;
                var cancelled = engine.CancelEffectTargeting();
                Settle(engine);
                Check("Mass Driver: Cancel at the Tribute pick → nothing Tributed, no damage, no Chain Link",
                    reached && activated && cancelled && p.MonsterCount == 2 && o.LifePoints == 8000 &&
                    !engine.Chain.HasLinks && engine.PendingActivation == null &&
                    engine.CanActivateSpellTrap(p, driver, fromHand: false),
                    $"act={activated} cancel={cancelled} mons={p.MonsterCount} lp={o.LifePoints} chain={engine.Chain.HasLinks}");
            }

            {
                // No monster: refused. From the hand: the activation only places it (no Tribute, no damage).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var driver = FaceUpSpell(engine, p, MassDriver, 1);
                var noMonster = !engine.CanActivateSpellTrap(p, driver, fromHand: false);
                Settled(engine, p, Celtic, 0, BattlePosition.Attack);
                var fromHand = PutInHand(engine, p, MassDriver);
                var placed = engine.TryActivateSpellTrap(p, fromHand, fromHand: true);
                Settle(engine);
                Check("Mass Driver: no monster → refused; activated from the hand it is only placed",
                    reached && noMonster && placed && fromHand.FaceUp && p.TryFindSpellTrap(fromHand, out _) &&
                    p.MonsterCount == 1 && o.LifePoints == 8000,
                    $"noMon={noMonster} placed={placed} mons={p.MonsterCount} lp={o.LifePoints}");
            }
        }

        // ───────────────────────────── Senri Eye ─────────────────────────────

        static void SenriEyeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Pay: 100 LP, the top card is looked at and stays; not in the opponent's Standby Phase.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var eye = FaceUpSpell(engine, p, SenriEye, 0);
                var log = Capture(engine);
                EndTurn(engine); // the AI's turn
                var quiet = engine.TurnPlayer == o && p.LifePoints == 8000 && engine.PendingActivation == null;
                EndTurn(engine); // the player's Standby Phase
                var standby = engine.Phase == DuelPhase.Standby && engine.TurnPlayer == p;
                var legal = Legal(engine);
                var topName = o.Deck.Count > 0 ? db.Get(o.Deck[0])?.name : null;
                var deckBefore = o.Deck.Count;
                var top = o.Deck.Count > 0 ? o.Deck[0] : 0;
                var paid = legal.Count == 1 && legal[0] == eye && engine.TrySelectEffectTarget(eye);
                Settle(engine);
                var looked = log.Exists(l => l.Contains("looks at") && topName != null && l.Contains(topName));
                Check("Senri Eye: own Standby only; tap it → pay 100 LP, look at the opponent's top card, it stays",
                    reached && quiet && standby && paid && looked && p.LifePoints == 7900 && o.Deck.Count == deckBefore &&
                    o.Deck[0] == top && engine.Phase == DuelPhase.Main1 && eye.FaceUp,
                    $"quiet={quiet} standby={standby} legal={legal.Count} paid={paid} looked={looked} lp={p.LifePoints}");
            }

            {
                // CANCEL: not used, nothing paid.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                FaceUpSpell(engine, p, SenriEye, 0);
                EndTurn(engine);
                EndTurn(engine);
                var asked = engine.IsAwaitingEffectTarget;
                var cancelled = engine.CancelEffectTargeting();
                Settle(engine);
                Check("Senri Eye: CANCEL → nothing is paid, Main Phase 1",
                    reached && asked && cancelled && p.LifePoints == 8000 && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null,
                    $"asked={asked} cancel={cancelled} lp={p.LifePoints} phase={engine.Phase}");
            }

            {
                // The AI pays at once while it keeps 4000 LP.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                FaceUpSpell(engine, o, SenriEye, 0);
                EndTurn(engine);
                Check("Senri Eye (AI): pays 100 LP with no prompt; Main Phase 1",
                    reached && engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 && o.LifePoints == 7900 &&
                    engine.PendingActivation == null,
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} lp={o.LifePoints}");
            }
        }

        // ───────────────────────────── Magical Scientist ─────────────────────────────

        static void MagicalScientistDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Only the Level 6 or lower Fusion Monster is offered; the human places it in Defense; it returns to the
                // Extra Deck in the End Phase.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var sci = Settled(engine, p, MagicalScientist, 0, BattlePosition.Attack);
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                p.ExtraDeck.Add(ThousandDragon);
                var can = engine.CanActivateSpellTrap(p, sci, fromHand: false);
                var activated = engine.TryActivateSpellTrap(p, sci, fromHand: false);
                var legal = Legal(engine);
                var lpPaid = p.LifePoints == 7000;
                engine.SetPendingSpecialSummonPlacement(3, BattlePosition.Defense);
                var picked = legal.Count == 1 && legal[0].CardId == FlameSwordsman && engine.TrySelectEffectTarget(legal[0]);
                Settle(engine);
                var fusion = p.MonstersOnField().FirstOrDefault(m => m.CardId == FlameSwordsman);
                var summoned = fusion != null && fusion.FaceUp && fusion.Position == BattlePosition.Defense &&
                               !p.ExtraDeck.Contains(FlameSwordsman) && p.ExtraDeck.Contains(ThousandDragon);
                var noDirect = fusion != null && SelfStatMonstersRuntime.ForbidsDirectAttack(engine, fusion);
                EndTurn(engine);
                Check("Magical Scientist: 1000 LP; only the Level 5 Fusion is offered; placed in Defense; it returns to " +
                      "the Extra Deck in the End Phase",
                    reached && can && activated && lpPaid && picked && summoned && noDirect &&
                    p.ExtraDeck.Contains(FlameSwordsman) && !p.MonstersOnField().Any(m => m.CardId == FlameSwordsman),
                    $"can={can} act={activated} legal={string.Join(",", legal.Select(l => l.Name))} lp={lpPaid} " +
                    $"picked={picked} summoned={summoned} noDirect={noDirect}");
            }

            {
                // Attack Position (no placement chosen): it cannot attack directly, but can attack a monster.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var sci = Settled(engine, p, MagicalScientist, 0, BattlePosition.Attack);
                var celtic = Settled(engine, p, Celtic, 1, BattlePosition.Attack);
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                var activated = engine.TryActivateSpellTrap(p, sci, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(Legal(engine).FirstOrDefault());
                Settle(engine);
                var fusion = p.MonstersOnField().FirstOrDefault(m => m.CardId == FlameSwordsman);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var fusionDirect = fusion != null && engine.CanAttackDirectly(p, fusion);
                var celticDirect = engine.CanAttackDirectly(p, celtic);
                var lpBefore = o.LifePoints;
                var tried = fusion != null && engine.TryAttack(p, fusion, null);
                DrainCombat(engine);
                Check("Magical Scientist: its Fusion Monster (Attack Position) cannot attack directly; another monster can",
                    reached && activated && fusion != null && fusion.Position == BattlePosition.Attack && !fusionDirect &&
                    celticDirect && !tried && o.LifePoints == lpBefore,
                    $"act={activated} fusion={fusion?.Position} fDirect={fusionDirect} cDirect={celticDirect} tried={tried}");
            }

            {
                // Only a Level 7 Fusion in the Extra Deck, or too few LP: the effect cannot be activated.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var sci = Settled(engine, p, MagicalScientist, 0, BattlePosition.Attack);
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(ThousandDragon);
                var noLevel = !engine.CanActivateSpellTrap(p, sci, fromHand: false);
                p.ExtraDeck.Add(FlameSwordsman);
                p.LifePoints = 900;
                var noLp = !engine.CanActivateSpellTrap(p, sci, fromHand: false);
                p.LifePoints = 8000;
                var ok = engine.CanActivateSpellTrap(p, sci, fromHand: false);
                Check("Magical Scientist: refused with only a Level 7 Fusion, or under 1000 LP",
                    reached && noLevel && noLp && ok, $"noLevel={noLevel} noLp={noLp} ok={ok}");
            }
        }

        // ───────────────────────────── Roulette Barrel ─────────────────────────────

        static void RouletteBarrelDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 4 and 3: the human chooses 4, then picks between 2 face-up Level 4 monsters; once per turn.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var barrel = Settled(engine, p, RouletteBarrel, 0, BattlePosition.Attack);
                var celtic = Settled(engine, o, Celtic, 0, BattlePosition.Attack);
                var giant = Settled(engine, o, GiantSoldier, 1, BattlePosition.Attack);
                var setLv4 = Settled(engine, o, Celtic, 2, BattlePosition.Defense, faceUp: false);
                engine.Rng.QueueDie(4);
                engine.Rng.QueueDie(3);
                var activated = engine.TryActivateSpellTrap(p, barrel, fromHand: false);
                var options = Legal(engine);
                var four = options.FirstOrDefault(t => t?.Def?.level == 4);
                var chose = options.Count == 2 && options.Exists(t => t?.Def?.level == 3) && four != null &&
                            engine.TrySelectEffectTarget(four);
                var victims = Legal(engine);
                var picked = victims.Count == 2 && victims.Contains(celtic) && victims.Contains(barrel) &&
                             !victims.Contains(setLv4) && engine.TrySelectEffectTarget(celtic);
                Settle(engine);
                Check("Roulette Barrel: rolls 4 and 3, the human chooses 4, then destroys 1 of the face-up Level 4 " +
                      "monsters; once per turn",
                    reached && activated && chose && picked && o.Graveyard.Contains(celtic) && OnField(o, giant) &&
                    OnField(p, barrel) && OnField(o, setLv4) && !engine.CanActivateSpellTrap(p, barrel, fromHand: false),
                    $"act={activated} options={string.Join(",", options.Select(t => t?.Name))} chose={chose} " +
                    $"victims={string.Join(",", victims.Select(t => t?.Name))} picked={picked}");
            }

            {
                // Equal results: no choice; a lone face-up Level 3 monster is destroyed with no prompt.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var barrel = Settled(engine, p, RouletteBarrel, 0, BattlePosition.Attack);
                var giant = Settled(engine, o, GiantSoldier, 0, BattlePosition.Defense);
                engine.Rng.QueueDie(3);
                engine.Rng.QueueDie(3);
                var activated = engine.TryActivateSpellTrap(p, barrel, fromHand: false);
                Settle(engine);
                Check("Roulette Barrel: 3 and 3 → no choice, the lone face-up Level 3 monster is destroyed",
                    reached && activated && o.Graveyard.Contains(giant) && engine.PendingActivation == null,
                    $"act={activated} gy={o.Graveyard.Contains(giant)} pend={engine.PendingActivation != null}");
            }

            {
                // The AI's choice: the result that destroys the player's monster.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var p = engine.Player;
                var o = engine.Opponent;
                var reached = r0 && ReachOpponentMain(engine);
                ClearBoard(engine);
                var barrel = Settled(engine, o, RouletteBarrel, 0, BattlePosition.Attack);
                var kuriboh = Settled(engine, p, Kuriboh, 0, BattlePosition.Attack);
                engine.Rng.QueueDie(4);
                engine.Rng.QueueDie(1);
                var activated = engine.TryActivateSpellTrap(o, barrel, fromHand: false);
                Settle(engine);
                Check("Roulette Barrel (AI): chooses 1 over its own Level 4 and destroys the player's Level 1 monster",
                    reached && activated && p.Graveyard.Contains(kuriboh) && OnField(o, barrel) &&
                    engine.PendingActivation == null,
                    $"reached={reached} act={activated} gy={p.Graveyard.Contains(kuriboh)}");
            }
        }

        // ───────────────────────────── Physical Double ─────────────────────────────

        static void PhysicalDoubleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var p = engine.Player;
                var o = engine.Opponent;
                var trap = PlaceSetTrap(engine, p, PhysicalDouble, 0);
                Settled(engine, o, Celtic, 0, BattlePosition.Attack);
                var ownTurn = !engine.CanActivateSpellTrap(p, trap, fromHand: false);
                var reached = r0 && ReachOpponentMain(engine);
                ClearBoard(engine);
                trap = PlaceSetTrap(engine, p, PhysicalDouble, 0);
                var celtic = Settled(engine, o, Celtic, 0, BattlePosition.Attack);
                celtic.UntilEndOfTurnAtk += 300; // its current ATK is copied: 1700
                Settled(engine, o, GiantSoldier, 1, BattlePosition.Defense, faceUp: false);
                var can = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                var activated = engine.TryActivateSpellTrap(p, trap, fromHand: false);
                var targeted = false;
                var positions = new List<CardInstance>();
                var chose = false;
                for (var i = 0; i < 10; i++)
                {
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var legal = Legal(engine);
                        if (legal.Contains(celtic))
                        {
                            targeted = legal.Count == 1 && engine.TrySelectEffectTarget(celtic);
                            continue;
                        }

                        positions = legal;
                        var def = positions.FirstOrDefault(t => t?.Def?.level == 1 && !t.Def.IsMonster);
                        chose = positions.Count == 2 && def != null && engine.TrySelectEffectTarget(def);
                        if (!chose) break;
                        continue;
                    }

                    if (engine.IsAwaitingResponse)
                    {
                        engine.PassResponse();
                        continue;
                    }

                    break;
                }

                Settle(engine);
                var token = p.MonstersOnField().FirstOrDefault(m => m.IsToken);
                var copied = token != null && token.Name == "Mirage Token" && token.Level == 4 && token.Race == "Warrior" &&
                             token.Attribute == "EARTH" && token.CurrentAtk == 1700 && token.CurrentDef == 1200 &&
                             token.FaceUp && token.Position == BattlePosition.Defense;
                EndTurn(engine); // the opponent's End Phase
                Check("Physical Double: not in your turn; in the opponent's, a \"Mirage Token\" copying the target's " +
                      "current Level / Type / Attribute / ATK / DEF, in the chosen position; destroyed in the End Phase",
                    ownTurn && reached && can && activated && chose && copied && !p.MonstersOnField().Any(m => m.IsToken),
                    $"own={ownTurn} reached={reached} can={can} act={activated} targeted={targeted} positions={positions.Count} " +
                    $"chose={chose} " +
                    $"token={token?.Name} {token?.Level} {token?.Race} {token?.Attribute} {token?.CurrentAtk}/{token?.CurrentDef} " +
                    $"{token?.Position}");
            }

            {
                // No face-up monster the opponent controls, or no free Monster Zone: it cannot be activated.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var p = engine.Player;
                var o = engine.Opponent;
                var reached = r0 && ReachOpponentMain(engine);
                ClearBoard(engine);
                var trap = PlaceSetTrap(engine, p, PhysicalDouble, 0);
                Settled(engine, o, Celtic, 0, BattlePosition.Defense, faceUp: false);
                var noFaceUp = !engine.CanActivateSpellTrap(p, trap, fromHand: false);
                Settled(engine, o, GiantSoldier, 1, BattlePosition.Attack);
                for (var z = 0; z < p.MonsterZones.Length; z++)
                    Settled(engine, p, Kuriboh, z, BattlePosition.Defense);
                var noZone = !engine.CanActivateSpellTrap(p, trap, fromHand: false);
                Check("Physical Double: refused with no face-up opponent monster, or no free Monster Zone",
                    reached && noFaceUp && noZone, $"reached={reached} noFaceUp={noFaceUp} noZone={noZone}");
            }
        }
    }
}
