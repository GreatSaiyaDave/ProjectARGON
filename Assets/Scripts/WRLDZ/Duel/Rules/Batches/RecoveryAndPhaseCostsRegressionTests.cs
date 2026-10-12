using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// RecoveryAndPhaseCosts batch: Dimension Distortion, Dimension Fusion, Salvage, Recycle, Reload, Coach Goblin, Manticore
    /// of Darkness. Compile shapes through the real compiler, near misses that must stay uncompiled, and mini-duels through
    /// the engine entry points (TryActivateSpellTrap, TrySelectEffectTarget / CancelEffectTargeting, turn changes).
    /// Runtime: <see cref="RecoveryAndPhaseCostsRuntime"/>.
    /// </summary>
    public static class RecoveryAndPhaseCostsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int DimensionDistortion = 95194279;
        const int DimensionFusion = 23557835;
        const int Salvage = 96947648;
        const int Recycle = 96316857;
        const int Reload = 22589918;
        const int CoachGoblin = 42541548;
        const int Manticore = 77121851;

        const int Celtic = 91152256;          // Normal Warrior EARTH 1400/1200
        const int GiantSoldier = 13039848;    // Normal Rock EARTH 1300/2000
        const int Kuriboh = 40640057;         // Effect Fiend 300/200
        const int PotOfGreed = 55144522;
        const int ArmoredStarfish = 17535588; // Normal Aqua WATER 850/1400
        const int RootWater = 39004808;       // Normal Fish WATER 900/800
        const int SpikeSeadra = 85326399;     // Normal Sea Serpent WATER 1600/1300
        const int DarkGray = 9159938;         // Normal Beast EARTH 800/900
        const int BeaverWarrior = 32452818;   // Normal Beast-Warrior EARTH 1200/1500

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("RecoveryAndPhaseCosts: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);
            DimensionDistortionDuels(db, pDeck, aDeck, Check);
            DimensionFusionDuels(db, pDeck, aDeck, Check);
            SalvageDuels(db, pDeck, aDeck, Check);
            RecycleDuels(db, pDeck, aDeck, Check);
            ReloadDuels(db, pDeck, aDeck, Check);
            CoachGoblinDuels(db, pDeck, aDeck, Check);
            ManticoreDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static EffectClause Only(CompiledCardProgram p) => p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}]";

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CompiledCardProgram Synthetic(CardDatabase db, int likeId, string desc)
        {
            var like = db.Get(likeId);
            return CardTextEffectCompiler.Compile(new CardDef
            {
                id = 0,
                name = "RecoveryAndPhaseCosts near miss",
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

        /// <summary>The player's Main Phase 1, turn 1; boards, hands, GYs and banished piles empty.</summary>
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
                who.LifePoints = 8000;
            }

            return e;
        }

        /// <summary>The player's Main Phase 1 on turn 3+; boards, hands, GYs and banished piles empty.</summary>
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
                who.Banished.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        static CardInstance Banish(DuelEngine e, DuelistState who, int id, bool faceUp = true)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = faceUp;
            who.Banished.Add(c);
            return c;
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance FaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pass every response window until a target pick (or nothing) is open.</summary>
        static void ToPick(DuelEngine e)
        {
            for (var i = 0; i < 6 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++) e.PassResponse();
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 8; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    e.CancelEffectTargeting();
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

        static void EndTurn(DuelEngine engine)
        {
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            engine.RecoverStuckCombat();
            if (engine.StandbyPausedFor != null) engine.ContinueAfterStandby();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static string St(DuelEngine e)
        {
            var pend = e.PendingActivation;
            return $"phase={e.Phase} turn={(e.TurnPlayer == e.Player ? "P" : "O")} await={e.IsAwaitingEffectTarget} " +
                   $"resp={e.IsAwaitingResponse} pend={(pend == null ? "-" : $"{pend.TargetKind}/{pend.LegalTargets.Count}")}";
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var dd = Prog(db, DimensionDistortion);
            var ddc = Only(dd);
            Check("Dimension Distortion: empty-GY condition, 1 targeted face-up monster in your banished pile, Special Summon it",
                Implemented(db, DimensionDistortion) && dd.FullyCompiled && ddc != null &&
                ddc.Timing == EffectTiming.Activate && ddc.Action == EffectActionKind.SpecialSummonBanishedTarget &&
                ddc.Zone == EffectZoneFilter.ControllerBanishedFaceUpMonsters && ddc.RequiresTargetChoice &&
                ddc.IsPsctTarget && !ddc.ChoiceDoesNotTarget &&
                ddc.ActivationCondition == RecoveryAndPhaseCostsRuntime.NoCardsInYourGy,
                Describe(dd));

            var df = Prog(db, DimensionFusion);
            var dfc = Only(df);
            Check("Dimension Fusion: pay 2000 LP (cost), each player Special Summons their face-up banished monsters, no target",
                Implemented(db, DimensionFusion) && df.FullyCompiled && dfc != null &&
                dfc.Timing == EffectTiming.Activate &&
                dfc.Action == EffectActionKind.EachPlayerSpecialSummonsFaceUpBanishedMonstersAsPossible &&
                dfc.PayLpAmount == 2000 && !dfc.RequiresTargetChoice && !dfc.IsPsctTarget && dfc.ChoiceDoesNotTarget &&
                dfc.ActivationCondition == RecoveryAndPhaseCostsRuntime.EitherPlayerCanSummonFaceUpBanished,
                Describe(df));

            var sv = Prog(db, Salvage);
            var svc = Only(sv);
            Check("Salvage: exactly 2 targeted WATER monsters with 1500 or less ATK in your GY to the hand",
                Implemented(db, Salvage) && sv.FullyCompiled && svc != null && svc.Timing == EffectTiming.Activate &&
                svc.Action == EffectActionKind.AddFromGyToHand && svc.Zone == EffectZoneFilter.ControllerGyMonsters &&
                svc.RequiresTargetChoice && svc.IsPsctTarget && svc.TargetCount == 2 && !svc.TargetUpTo &&
                svc.AttributeFilter == "WATER" && svc.Amount == 1500 && svc.AmountIsAtkMax &&
                TargetedClassicsRuntime.ExactTargetCount(svc) == 2,
                Describe(sv));

            var rc = Prog(db, Recycle);
            var rcc = Only(rc);
            Check("Recycle: optional own-Standby trigger, 300 LP, targets a non-Monster in your GY, no activation clause",
                Implemented(db, Recycle) && rc.FullyCompiled && rcc != null && rcc.Timing == EffectTiming.StandbyPhase &&
                rcc.Action == EffectActionKind.PayLpReturnGyNonMonsterToDeckBottomAtYourStandby && rcc.Amount == 300 &&
                rcc.IsOptional && rcc.OncePerTurn && rcc.IsPsctTarget && !rcc.OpponentTurnOnly &&
                rc.ClausesFor(EffectTiming.Activate).Count == 0,
                Describe(rc));

            var rl = Prog(db, Reload);
            var rlc = Only(rl);
            Check("Reload: Activate — shuffle the hand into the Deck, draw that many; needs another card in hand",
                Implemented(db, Reload) && rl.FullyCompiled && rlc != null && rlc.Timing == EffectTiming.Activate &&
                rlc.Action == EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber && !rlc.RequiresTargetChoice &&
                rlc.ActivationCondition == RecoveryAndPhaseCostsRuntime.AnotherCardInYourHand,
                Describe(rl));

            var cg = Prog(db, CoachGoblin);
            var cgc = Only(cg);
            Check("Coach Goblin: optional once-per-turn own End Phase trigger (Normal Monster cost, draw 1)",
                Implemented(db, CoachGoblin) && cg.FullyCompiled && cgc != null && cgc.Timing == EffectTiming.EndPhase &&
                cgc.Action == EffectActionKind.ReturnHandNormalMonsterToDeckBottomDrawAtYourEndPhase &&
                cgc.IsOptional && cgc.OncePerTurn && !cgc.ResolvesFromGy && !cgc.OpponentTurnOnly && !cgc.IsPsctTarget,
                Describe(cg));

            var mt = Prog(db, Manticore);
            var mtc = Only(mt);
            Check("Manticore of Darkness: optional GY End Phase trigger, Beast / Beast-Warrior / Winged Beast send cost",
                Implemented(db, Manticore) && mt.FullyCompiled && mtc != null && mtc.Timing == EffectTiming.EndPhase &&
                mtc.Action == EffectActionKind.SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase &&
                mtc.ResolvesFromGy && mtc.IsOptional && mtc.OncePerTurn &&
                mtc.RaceFilter == "Beast|Beast-Warrior|Winged Beast",
                Describe(mt));
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var misses = new (int like, string text)[]
            {
                (DimensionDistortion,
                    "You can only activate this card if there are no cards in your Graveyard. Select 1 removed from play " +
                    "monster and Special Summon it on your opponent's side of the field."),
                (DimensionFusion,
                    "Pay 2000 Life Points. Both players Special Summon as many of their removed from play Spellcaster-Type " +
                    "monsters as possible."),
                (Salvage,
                    "Target 2 WATER monsters with 1500 or less ATK in your Graveyard; add those targets to your hand, then " +
                    "discard 1 card."),
                (Recycle,
                    "During your Standby Phase, by paying 300 Life Points, select 1 Monster Card in your Graveyard and " +
                    "return it to the bottom of your Deck."),
                (Reload,
                    "Add all cards in your hand to your Deck and shuffle it. Then draw the same number of cards that you " +
                    "added to your Deck, plus 1."),
                (CoachGoblin,
                    "While this card is face-up on your side of the field, you can return 1 Effect Monster Card in your " +
                    "hand to the bottom of your Deck during your End Phase to draw 1 card from your Deck. You can only use " +
                    "this effect once per turn."),
                (Manticore,
                    "During the End Phase of the turn this card was sent to the Graveyard: You can send to the Graveyard 1 " +
                    "Beast-Type monster from your Deck; Special Summon this card from your Graveyard."),
            };
            var bad = new List<string>();
            foreach (var (like, text) in misses)
            {
                var p = Synthetic(db, like, text);
                if (p.FullyCompiled) bad.Add($"{db.Get(like).name}: {Describe(p)}");
            }

            Check("near misses (other zone / Type / cost / rider) stay not FullyCompiled", bad.Count == 0,
                string.Join(" | ", bad));
        }

        // ───────────────────────────── Dimension Distortion ─────────────────────────────

        static void DimensionDistortionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Only the face-up banished monster is offered; the tap asks its position; it is Special Summoned in
                // face-up Defense Position.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var celtic = Banish(e, p, Celtic);
                var hidden = Banish(e, p, GiantSoldier, faceUp: false);
                var theirs = Banish(e, e.Opponent, BeaverWarrior);
                var dd = PutInHand(e, p, DimensionDistortion);
                var can = e.CanActivateSpellTrap(p, dd, fromHand: true);
                var act = e.TryActivateSpellTrap(p, dd, fromHand: true);
                ToPick(e);
                var legal = Legal(e);
                var asksPlacement = e.PendingActivation != null &&
                                    SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation);
                var offered = legal.Count == 1 && legal[0] == celtic;
                if (e.IsAwaitingEffectTarget)
                {
                    e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    e.TrySelectEffectTarget(celtic);
                }

                Settle(e);
                Check("Dimension Distortion: your face-up banished monster only; Special Summoned in the chosen face-up position",
                    can && act && offered && asksPlacement && OnField(p, celtic) && celtic.FaceUp &&
                    celtic.Position == BattlePosition.Defense && !p.Banished.Contains(celtic) && p.Banished.Contains(hidden) &&
                    e.Opponent.Banished.Contains(theirs) && p.Graveyard.Contains(dd),
                    $"can={can} act={act} legal={legal.Count} place={asksPlacement} onField={OnField(p, celtic)} " +
                    $"pos={celtic.Position} {St(e)}");
            }

            {
                // A card in your GY, or only a face-down banished monster: it cannot be activated.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                Banish(e, p, Celtic);
                AddToGy(e, p, PotOfGreed);
                var dd = PutInHand(e, p, DimensionDistortion);
                var gyBlocks = !e.CanActivateSpellTrap(p, dd, fromHand: true);
                p.Graveyard.Clear();
                p.Banished.Clear();
                Banish(e, p, Celtic, faceUp: false);
                Banish(e, e.Opponent, Celtic);
                var faceDownBlocks = !e.CanActivateSpellTrap(p, dd, fromHand: true);
                Check("Dimension Distortion: not with a card in your GY, nor with only face-down / opponent's banished monsters",
                    gyBlocks && faceDownBlocks, $"gy={gyBlocks} faceDown={faceDownBlocks}");
            }
        }

        // ───────────────────────────── Dimension Fusion ─────────────────────────────

        static void DimensionFusionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Both players summon their own face-up banished monsters; the player picks theirs and their positions.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = Banish(e, p, Celtic);
                var soldier = Banish(e, p, GiantSoldier);
                var hidden = Banish(e, p, DarkGray, faceUp: false);
                var gray = Banish(e, o, DarkGray);
                var df = PutInHand(e, p, DimensionFusion);
                var act = e.TryActivateSpellTrap(p, df, fromHand: true);
                ToPick(e);
                var lpAtPick = p.LifePoints;
                var legal = Legal(e);
                var offered = legal.Count == 2 && legal.Contains(celtic) && legal.Contains(soldier);
                var asksPlacement = e.PendingActivation != null &&
                                    SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation);
                if (e.IsAwaitingEffectTarget)
                {
                    e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    e.TrySelectEffectTarget(soldier);
                }

                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                Settle(e);
                Check("Dimension Fusion: 2000 LP; each player Special Summons their own face-up banished monsters",
                    act && offered && asksPlacement && lpAtPick == 6000 && p.LifePoints == 6000 &&
                    OnField(p, celtic) && OnField(p, soldier) && soldier.FaceUp && soldier.Position == BattlePosition.Defense &&
                    celtic.FaceUp && celtic.Position == BattlePosition.Attack && OnField(o, gray) && gray.FaceUp &&
                    p.Banished.Contains(hidden) && p.Graveyard.Contains(df),
                    $"act={act} legal={legal.Count} place={asksPlacement} lp={p.LifePoints} celtic={OnField(p, celtic)} " +
                    $"soldier={OnField(p, soldier)}/{soldier.Position} gray={OnField(o, gray)} {St(e)}");
            }

            {
                // One free zone: exactly 1 is Special Summoned; CANCEL takes the strongest; the other stays banished.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                for (var z = 0; z < 4; z++) PlaceMonster(e, p, Kuriboh, z, BattlePosition.Defense, true);
                var celtic = Banish(e, p, Celtic);
                var gray = Banish(e, p, DarkGray);
                var df = PutInHand(e, p, DimensionFusion);
                var act = e.TryActivateSpellTrap(p, df, fromHand: true);
                ToPick(e);
                var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2;
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                Settle(e);
                Check("Dimension Fusion: as many as the free zones allow — CANCEL Special Summons the strongest one",
                    act && asked && OnField(p, celtic) && !OnField(p, gray) && p.Banished.Contains(gray) &&
                    p.MonsterCount == 5,
                    $"act={act} asked={asked} celtic={OnField(p, celtic)} gray={OnField(p, gray)} count={p.MonsterCount} {St(e)}");
            }

            {
                // Legality: nothing face-up banished on either side, or too few LP.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var df = PutInHand(e, p, DimensionFusion);
                Banish(e, p, Celtic, faceUp: false);
                var noneBlocks = !e.CanActivateSpellTrap(p, df, fromHand: true);
                Banish(e, e.Opponent, DarkGray);
                var oppOnlyOk = e.CanActivateSpellTrap(p, df, fromHand: true);
                p.LifePoints = 1500;
                var lpBlocks = !e.CanActivateSpellTrap(p, df, fromHand: true);
                Check("Dimension Fusion: needs a face-up banished monster on either side and 2000 LP",
                    noneBlocks && oppOnlyOk && lpBlocks, $"none={noneBlocks} oppOnly={oppOnlyOk} lp={lpBlocks}");
            }
        }

        // ───────────────────────────── Salvage ─────────────────────────────

        static void SalvageDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var starfish = AddToGy(e, p, ArmoredStarfish);
                var root = AddToGy(e, p, RootWater);
                var seadra = AddToGy(e, p, SpikeSeadra);
                var celtic = AddToGy(e, p, Celtic);
                var theirs = AddToGy(e, e.Opponent, RootWater);
                var sv = PutInHand(e, p, Salvage);
                var act = e.TryActivateSpellTrap(p, sv, fromHand: true);
                ToPick(e);
                var legal = Legal(e);
                var offered = legal.Count == 2 && legal.Contains(starfish) && legal.Contains(root);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(starfish);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(root);
                Settle(e);
                Check("Salvage: only your WATER monsters with 1500 or less ATK; both targets are added to the hand",
                    act && offered && p.Hand.Contains(starfish) && p.Hand.Contains(root) && p.Graveyard.Contains(seadra) &&
                    p.Graveyard.Contains(celtic) && e.Opponent.Graveyard.Contains(theirs) && p.Graveyard.Contains(sv),
                    $"act={act} legal={string.Join(",", legal.Select(l => l.Name))} {St(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                AddToGy(e, p, ArmoredStarfish);
                AddToGy(e, p, SpikeSeadra);
                var sv = PutInHand(e, p, Salvage);
                Check("Salvage: 1 legal target is not enough (exactly 2 targets)",
                    !e.CanActivateSpellTrap(p, sv, fromHand: true), "");
            }
        }

        // ───────────────────────────── Recycle ─────────────────────────────

        static void RecycleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Your Standby: tap the Spell → pay 300 LP, it goes to the bottom of your Deck; the monster is not offered.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var recycle = FaceUpSpell(e, p, Recycle, 0);
                var pot = AddToGy(e, p, PotOfGreed);
                var celtic = AddToGy(e, p, Celtic);
                EndTurn(e); // the AI's turn
                var quiet = e.TurnPlayer == o && p.LifePoints == 8000 && p.Graveyard.Contains(pot);
                EndTurn(e); // the player's Standby Phase
                var standby = e.Phase == DuelPhase.Standby && e.TurnPlayer == p;
                var legal = Legal(e);
                var tapped = legal.Count == 1 && legal[0] == pot && e.TrySelectEffectTarget(pot);
                Settle(e);
                if (e.StandbyPausedFor != null) e.ContinueAfterStandby();
                Check("Recycle: own Standby only; tap a non-Monster → pay 300 LP, it goes to the bottom of the Deck",
                    reached && quiet && standby && tapped && p.LifePoints == 7700 && !p.Graveyard.Contains(pot) &&
                    p.Deck.Count > 0 && p.Deck[p.Deck.Count - 1] == PotOfGreed && p.Graveyard.Contains(celtic) &&
                    recycle.FaceUp,
                    $"reached={reached} quiet={quiet} standby={standby} legal={legal.Count} tapped={tapped} " +
                    $"lp={p.LifePoints} {St(e)}");
            }

            {
                // CANCEL: not used, nothing paid.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FaceUpSpell(e, p, Recycle, 0);
                var pot = AddToGy(e, p, PotOfGreed);
                EndTurn(e);
                EndTurn(e);
                var asked = e.IsAwaitingEffectTarget;
                var cancelled = asked && e.CancelEffectTargeting();
                Settle(e);
                Check("Recycle: CANCEL → nothing is paid, the card stays in the GY",
                    reached && asked && cancelled && p.LifePoints == 8000 && p.Graveyard.Contains(pot),
                    $"asked={asked} cancel={cancelled} lp={p.LifePoints} {St(e)}");
            }

            {
                // The AI uses it in its own Standby Phase with no prompt.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                FaceUpSpell(e, o, Recycle, 0);
                var pot = AddToGy(e, o, PotOfGreed);
                EndTurn(e);
                Check("Recycle (AI): pays 300 LP and returns the Spell with no prompt",
                    reached && e.TurnPlayer == o && o.LifePoints == 7700 && !o.Graveyard.Contains(pot) &&
                    o.Deck.Count > 0 && o.Deck[o.Deck.Count - 1] == PotOfGreed && e.PendingActivation == null,
                    $"lp={o.LifePoints} {St(e)}");
            }
        }

        // ───────────────────────────── Reload ─────────────────────────────

        static void ReloadDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var reload = PutInHand(e, p, Reload);
                var a = PutInHand(e, p, Celtic);
                var b = PutInHand(e, p, GiantSoldier);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, reload, fromHand: true);
                Settle(e);
                Check("Reload: the 2 other hand cards go into the Deck, then 2 cards are drawn",
                    act && p.Hand.Count == 2 && !p.Hand.Contains(a) && !p.Hand.Contains(b) && p.Deck.Count == deckBefore &&
                    p.Graveyard.Contains(reload),
                    $"act={act} hand={p.Hand.Count} deck={p.Deck.Count}/{deckBefore} {St(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var reload = PutInHand(e, p, Reload);
                Check("Reload: not with no other card in the hand",
                    !e.CanActivateSpellTrap(p, reload, fromHand: true), "");
            }
        }

        // ───────────────────────────── Coach Goblin ─────────────────────────────

        static void CoachGoblinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Your End Phase: only the Normal Monster is offered; it goes to the bottom of the Deck and you draw 1.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var coach = PlaceMonster(e, p, CoachGoblin, 0, BattlePosition.Attack, true);
                var soldier = PutInHand(e, p, GiantSoldier);
                var kuriboh = PutInHand(e, p, Kuriboh);
                var deckBefore = p.Deck.Count;
                e.TryEndTurnSafe(p);
                var legal = Legal(e);
                var asked = e.IsAwaitingEffectTarget && legal.Count == 1 && legal[0] == soldier;
                var tapped = asked && e.TrySelectEffectTarget(soldier);
                Settle(e);
                Check("Coach Goblin: your End Phase — the Normal Monster to the bottom of the Deck, draw 1",
                    reached && asked && tapped && !p.Hand.Contains(soldier) && p.Hand.Contains(kuriboh) &&
                    p.Hand.Count == 2 && p.Deck.Count == deckBefore && p.Deck.Count > 0 && OnField(p, coach) &&
                    e.TurnPlayer == o,
                    $"reached={reached} asked={asked} legal={legal.Count} tapped={tapped} hand={p.Hand.Count} " +
                    $"deck={p.Deck.Count}/{deckBefore} {St(e)}");
            }

            {
                // CANCEL: nothing happens; no Normal Monster in hand: no prompt.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, CoachGoblin, 0, BattlePosition.Attack, true);
                var soldier = PutInHand(e, p, GiantSoldier);
                e.TryEndTurnSafe(p);
                var asked = e.IsAwaitingEffectTarget;
                if (asked) e.CancelEffectTargeting();
                Settle(e);
                var kept = p.Hand.Contains(soldier) && p.Hand.Count == 1;

                var e2 = PlayerMain1(db, pDeck, aDeck, out _);
                PlaceMonster(e2, e2.Player, CoachGoblin, 0, BattlePosition.Attack, true);
                PutInHand(e2, e2.Player, Kuriboh);
                e2.TryEndTurnSafe(e2.Player);
                var noPrompt = !e2.IsAwaitingEffectTarget && e2.TurnPlayer == e2.Opponent;
                Check("Coach Goblin: CANCEL keeps the hand; no Normal Monster in hand → no prompt",
                    reached && asked && kept && noPrompt, $"asked={asked} kept={kept} noPrompt={noPrompt} {St(e2)}");
            }
        }

        // ───────────────────────────── Manticore of Darkness ─────────────────────────────

        static void ManticoreDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Sent to the GY this turn: in the End Phase, send a Beast from the hand, then summon it in face-up Defense.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mant = PlaceMonster(e, p, Manticore, 0, BattlePosition.Attack, true);
                e.SendCardToGrave(p, mant);
                var gray = PutInHand(e, p, DarkGray);
                var celtic = PutInHand(e, p, Celtic);
                var beaver = PlaceMonster(e, p, BeaverWarrior, 1, BattlePosition.Attack, true);
                e.TryEndTurnSafe(p);
                var legal = Legal(e);
                var offered = legal.Count == 2 && legal.Contains(gray) && legal.Contains(beaver) && !legal.Contains(celtic);
                var paid = offered && e.TrySelectEffectTarget(gray);
                var placeAsked = e.IsAwaitingEffectTarget && Legal(e).Contains(mant) &&
                                 SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation);
                if (placeAsked)
                {
                    e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    e.TrySelectEffectTarget(mant);
                }

                Settle(e);
                Check("Manticore of Darkness: the turn it was sent — Beast cost from the hand, Special Summoned face-up Defense",
                    reached && offered && paid && placeAsked && OnField(p, mant) && mant.FaceUp &&
                    mant.Position == BattlePosition.Defense && p.Graveyard.Contains(gray) && OnField(p, beaver) &&
                    p.Hand.Contains(celtic) && e.TurnPlayer == o,
                    $"reached={reached} legal={legal.Count} paid={paid} place={placeAsked} onField={OnField(p, mant)} " +
                    $"pos={mant.Position} {St(e)}");
            }

            {
                // Not sent this turn: no trigger. CANCEL: it stays in the GY and the cost is not sent.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var old = AddToGy(e, p, Manticore); // no GY entry this turn
                var gray = PutInHand(e, p, DarkGray);
                e.TryEndTurnSafe(p);
                var quiet = !e.IsAwaitingEffectTarget && p.Graveyard.Contains(old) && p.Hand.Contains(gray);
                Settle(e);

                var e2 = PlayerMain1(db, pDeck, aDeck, out _);
                var p2 = e2.Player;
                var mant = PlaceMonster(e2, p2, Manticore, 0, BattlePosition.Attack, true);
                e2.SendCardToGrave(p2, mant);
                var gray2 = PutInHand(e2, p2, DarkGray);
                e2.TryEndTurnSafe(p2);
                var asked = e2.IsAwaitingEffectTarget;
                if (asked) e2.CancelEffectTargeting();
                Settle(e2);
                Check("Manticore of Darkness: no trigger from an earlier turn; CANCEL keeps it in the GY and the cost in hand",
                    reached && quiet && asked && p2.Graveyard.Contains(mant) && p2.Hand.Contains(gray2),
                    $"quiet={quiet} asked={asked} {St(e2)}");
            }

            {
                // Either player's End Phase: the AI's copy sent during the player's turn comes back in the player's End Phase.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mant = PlaceMonster(e, o, Manticore, 0, BattlePosition.Attack, true);
                e.SendCardToGrave(o, mant);
                var gray = PutInHand(e, o, DarkGray);
                e.TryEndTurnSafe(p);
                Settle(e);
                Check("Manticore of Darkness (AI): uses it in the opponent's End Phase with a Beast from its hand",
                    reached && OnField(o, mant) && mant.FaceUp && o.Graveyard.Contains(gray) && e.TurnPlayer == o,
                    $"onField={OnField(o, mant)} gray={o.Graveyard.Contains(gray)} {St(e)}");
            }

            {
                // The two-Manticore loop: a 2nd Manticore sent as the cost during the End Phase was sent there this turn, so
                // it is offered its own effect once the 1st one resolved (ygopro raises EVENT_PHASE+PHASE_END again).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var m1 = PlaceMonster(e, p, Manticore, 0, BattlePosition.Attack, true);
                e.SendCardToGrave(p, m1);
                var m2 = PutInHand(e, p, Manticore);
                var gray = PutInHand(e, p, DarkGray);
                e.TryEndTurnSafe(p);
                var first = e.IsAwaitingEffectTarget && Legal(e).Contains(m2);
                if (first) e.TrySelectEffectTarget(m2);
                if (e.IsAwaitingEffectTarget && Legal(e).Contains(m1))
                {
                    e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                    e.TrySelectEffectTarget(m1);
                }

                var m2InGy = p.Graveyard.Contains(m2);
                var legal2 = e.IsAwaitingEffectTarget ? Legal(e) : new List<CardInstance>();
                var second = legal2.Contains(gray) && legal2.Contains(m1) && e.TurnPlayer == p;
                if (second) e.TrySelectEffectTarget(gray);
                if (e.IsAwaitingEffectTarget && Legal(e).Contains(m2))
                {
                    e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Attack);
                    e.TrySelectEffectTarget(m2);
                }

                Settle(e);
                Check("Manticore of Darkness: the copy sent as the cost in the End Phase is offered its own effect that End Phase",
                    reached && first && m2InGy && second && OnField(p, m1) && OnField(p, m2) && m2.FaceUp &&
                    m2.Position == BattlePosition.Attack && p.Graveyard.Contains(gray) && e.TurnPlayer == o,
                    $"first={first} m2InGy={m2InGy} second={second} m1Field={OnField(p, m1)} m2Field={OnField(p, m2)} " +
                    $"grayInGy={p.Graveyard.Contains(gray)} {St(e)}");
            }
        }
    }
}
