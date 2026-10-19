using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "RdsRedoSpellTrap": the RDS / FET Spells and Traps the v208 QC failed closed, redone from their printed
    /// text and ygopro scripts (<see cref="RdsRedoSpellTrapAtoms"/>, <see cref="RdsRedoSpellTrapRuntime"/>). Each card: its
    /// compiled shape, mini-duels through the engine entry points (TryActivateSpellTrap, PassResponse, TryNormalSummon,
    /// TryAttack, response windows) that assert the outcome, and near misses.
    /// </summary>
    public static class RdsRedoSpellTrapRegressionTests
    {
        const int ChainBurst = 48276469;
        const int SpellAbsorption = 51481927;
        const int ForcedCeasefire = 97806240;
        const int CurseOfDarkness = 84970821;
        const int JarOfGreed = 83968380;      // Normal Trap: draw 1
        const int SevenTools = 3819470;       // Counter Trap: pay 1000 LP; negate a Trap's activation and destroy it
        const int TrapHole = 4206964;         // Normal Trap: a 1000+ ATK monster Normal / Flip Summoned is destroyed
        const int PotOfGreed = 55144522;
        const int MagicJammer = 77414722;     // Counter Trap: discard 1; negate a Spell Card's activation
        const int Celtic = 91152256;          // Normal Lv4 1400/1200
        const int Bewd = 89631139;            // Normal 3000/2500
        const int NecklaceOfCommand = 48576971;
        const int Flint = 75560629;
        const int MysticalElf = 15025844;     // Normal 800/2000
        const int TriangleEcstasySpark = 12181376;
        const int HarpieLadySisters = 12206212; // 1950/2100
        const int CallOfTheHaunted = 97077563;  // Continuous Trap
        const int AxeOfDespair = 40619825;      // Equip Spell: +1000 ATK

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            void Check(string name, bool ok, string detail = null) => check("RdsRedoSpellTrap: " + name, ok, detail ?? "");

            Shapes(db, Check);
            ChainBurstDuels(db, pDeck, aDeck, Check);
            SpellAbsorptionDuels(db, pDeck, aDeck, Check);
            ForcedCeasefireDuels(db, pDeck, aDeck, Check);
            NecklaceDuels(db, pDeck, aDeck, Check);
            FlintDuels(db, pDeck, aDeck, Check);
            TriangleEcstasySparkDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Comp(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static string Shape(CardDatabase db, int id)
        {
            var p = Comp(db, id);
            if (p == null) return "null";
            var cls = string.Join("; ", p.ClauseList.Select(c => $"{c.Timing}:{c.Action}/{c.Zone}/{c.Side}"));
            return $"{CardEffectStatus.Classify(db.Get(id))} full={p.FullyCompiled} [{cls}] " +
                   $"unparsed=[{string.Join("|", p.UnparsedFragments ?? Array.Empty<string>())}]";
        }

        static bool Implemented(CardDatabase db, int id) =>
            db.Get(id) != null && CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CardDef Variant(CardDatabase db, int id, string desc)
        {
            var d = db.Get(id);
            return new CardDef { id = 990210000 + id % 10000, name = d.name + " (variant)", type = d.type, race = d.race, desc = desc };
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The Player's turn 3, Main Phase 1 (a Battle Phase is legal), boards, hands and GYs empty.</summary>
        static DuelEngine PlayerMain3(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
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
            engine.ClearPendingResponse();
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return engine;
        }

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>Activate an Equip Spell from the hand and pick <paramref name="host"/> (a lone target needs no pick).</summary>
        static bool EquipFromHand(DuelEngine e, DuelistState who, CardInstance equip, CardInstance host)
        {
            var act = e.TryActivateSpellTrap(who, equip, fromHand: true);
            if (act && e.IsAwaitingEffectTarget)
            {
                if (!e.PendingActivation.LegalTargets.Contains(host)) return false;
                e.TrySelectEffectTarget(host);
            }

            PassAll(e);
            return act && ReferenceEquals(equip.EquippedTo, host);
        }

        static bool AwaitingPickOf(DuelEngine e, CardInstance source) =>
            e.IsAwaitingEffectTarget && e.PendingActivation != null && ReferenceEquals(e.PendingActivation.Card, source);

        static string Board(DuelEngine e) =>
            $"P[mz={string.Join(",", e.Player.MonstersOnField().Select(m => $"{m.Name}:{m.CurrentAtk}"))} st={string.Join(",", e.Player.SpellTrapsOnField().Select(c => c.Name))} gy={string.Join(",", e.Player.Graveyard.Select(c => c.Name))} hand={e.Player.Hand.Count}] " +
            $"O[mz={string.Join(",", e.Opponent.MonstersOnField().Select(m => $"{m.Name}:{m.CurrentAtk}"))} st={string.Join(",", e.Opponent.SpellTrapsOnField().Select(c => c.Name))} gy={string.Join(",", e.Opponent.Graveyard.Select(c => c.Name))} hand={e.Opponent.Hand.Count}] " +
            $"pending={(e.PendingActivation != null ? e.PendingActivation.Card?.Name + "/" + e.PendingActivation.LegalTargets.Count : "none")}";

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static bool ChainWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, CheckFn Check)
        {
            {
                var p = Comp(db, ChainBurst);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Chain Burst: one continuous 1000-damage Trap-activation listener (no Chain Link), Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && c != null &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.InflictDamageToTrapActivatorAfterResolve && c.Amount == 1000 &&
                    !c.MakesChainLink && Implemented(db, ChainBurst), Shape(db, ChainBurst));
            }

            {
                var p = Comp(db, SpellAbsorption);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Spell Absorption: one continuous 500-LP Spell-activation listener (no Chain Link), Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && c != null &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.GainLpWhenSpellCardActivationResolves && c.Amount == 500 &&
                    !c.MakesChainLink && Implemented(db, SpellAbsorption), Shape(db, SpellAbsorption));
            }

            {
                var p = Comp(db, ForcedCeasefire);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Forced Ceasefire: discard-1 cost + a both-player Trap Card lock for the turn, Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && c != null &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.TrapCardActivationsLockedThisTurn &&
                    c.Side == EffectSide.Both && c.RequiresDiscardCost && c.DiscardCostCount == 1 &&
                    c.DiscardCostAttribute == "*" && Implemented(db, ForcedCeasefire), Shape(db, ForcedCeasefire));
            }

            // Near misses: a rider or a reworded sentence is not absorbed by the whole-card atoms.
            foreach (var (id, desc) in new[]
                     {
                         (ChainBurst, "Each time a player activates a Trap Card, they take 1000 damage immediately after it resolves. You can only control 1 \"Chain Burst\"."),
                         (ChainBurst, "Each time a player activates a Spell Card, they take 1000 damage immediately after it resolves."),
                         (SpellAbsorption, "Each time a Spell Card is activated, gain 500 LP immediately after it resolves. Destroy this card during your 3rd End Phase."),
                         (ForcedCeasefire, "Discard 1 card from your hand. No Spell Cards can be activated until the end of this turn."),
                         (ForcedCeasefire, "No Trap Cards can be activated until the end of this turn.")
                     })
            {
                var p = CardTextEffectCompiler.Compile(Variant(db, id, desc));
                Check($"near miss: \"{desc}\" is not FullyCompiled to this batch's actions",
                    p == null || !p.FullyCompiled || !p.ClauseList.Any(c => c != null &&
                        (c.Action == EffectActionKind.InflictDamageToTrapActivatorAfterResolve ||
                         c.Action == EffectActionKind.GainLpWhenSpellCardActivationResolves ||
                         c.Action == EffectActionKind.TrapCardActivationsLockedThisTurn)),
                    p == null ? "null" : $"full={p.FullyCompiled} [{string.Join("; ", p.ClauseList.Select(c => c.Action))}]");
            }

            {
                var p = Comp(db, NecklaceOfCommand);
                Check("Necklace of Command: equip (either field, the AI equips its own) + its battle-destroyed GY trigger marker, Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    p.ClauseList[0].Action == EffectActionKind.EquipThisToTarget &&
                    p.ClauseList[0].Zone == EffectZoneFilter.FieldAnyMonster && p.ClauseList[0].AiEquipsOwnMonster &&
                    p.ClauseList[1].Action == EffectActionKind.DrawOrDiscardRandomFromOpponentHand &&
                    p.ClauseList[1].Amount == 1 && Implemented(db, NecklaceOfCommand), Shape(db, NecklaceOfCommand));
            }

            {
                var p = Comp(db, Flint);
                Check("Flint: equip −300 ATK (either field) + position and attack locks + its re-equip GY trigger marker, Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 4 &&
                    p.ClauseList[0].Action == EffectActionKind.EquipThisToTarget &&
                    p.ClauseList[0].Zone == EffectZoneFilter.FieldAnyMonster && p.ClauseList[0].EquipAtkBonus == -300 &&
                    !p.ClauseList[0].AiEquipsOwnMonster &&
                    p.ClauseList.Any(c => c.Action == EffectActionKind.EquippedCannotChangePosition) &&
                    p.ClauseList.Any(c => c.Action == EffectActionKind.EquippedCannotAttack) &&
                    p.ClauseList.Any(c => c.Action == EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed) &&
                    Implemented(db, Flint), Shape(db, Flint));
            }

            foreach (var (id, desc) in new[]
                     {
                         (NecklaceOfCommand, "When a monster you control that's equipped with this card is destroyed by battle, and this card is sent to the Graveyard, you can either:\n● Draw 1 card. OR\n● Discard 1 random card from your opponent's hand. You can only use this effect once per turn."),
                         (NecklaceOfCommand, "When a monster you control that's equipped with this card is destroyed by battle, and this card is sent to the Graveyard, you can either:\n● Draw 2 cards. OR\n● Discard 1 random card from your opponent's hand."),
                         (Flint, "The equipped monster loses 300 ATK and cannot change its battle position or declare an attack."),
                         (Flint, "The equipped monster loses 300 ATK and cannot change its battle position or declare an attack. If this card is destroyed because the equipped monster is destroyed: Target 1 monster on the field; equip this card to that monster. You can only control 1 \"Flint\".")
                     })
            {
                var p = CardTextEffectCompiler.Compile(Variant(db, id, desc));
                Check($"near miss: \"{desc.Replace("\n", " ")}\" is not FullyCompiled to this batch's equip triggers",
                    p == null || !p.FullyCompiled || !p.ClauseList.Any(c => c != null &&
                        (c.Action == EffectActionKind.DrawOrDiscardRandomFromOpponentHand ||
                         c.Action == EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed)),
                    p == null ? "null" : $"full={p.FullyCompiled} [{string.Join("; ", p.ClauseList.Select(c => c.Action))}]");
            }

            {
                var p = Comp(db, TriangleEcstasySpark);
                Check("Triangle Ecstasy Spark: ATK-becomes-2700 for the named monsters (needs one face-up) + the opponent's Trap lock + the opponent's Trap negation, Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 3 &&
                    p.ClauseList[0].Action == EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn &&
                    p.ClauseList[0].Amount == 2700 && p.ClauseList[0].ConditionName == "Harpie Lady Sisters" &&
                    p.ClauseList[0].ActivationCondition == ChaosBanishSummonsRuntime.NamedFaceUpOnEitherField &&
                    p.ClauseList[1].Action == EffectActionKind.TrapCardActivationsLockedThisTurn &&
                    p.ClauseList[1].Side == EffectSide.Opponent &&
                    p.ClauseList[2].Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn &&
                    p.ClauseList[2].NegatedCardFamily == TurnNegationTrapsRuntime.OpponentTrap &&
                    Implemented(db, TriangleEcstasySpark), Shape(db, TriangleEcstasySpark));
                var v = CardTextEffectCompiler.Compile(Variant(db, TriangleEcstasySpark,
                    "Until the end of this turn, the ATK of all \"Harpie Lady Sisters\" currently on the field becomes 2700, your opponent cannot activate any Trap Cards."));
                Check("near miss: Triangle Ecstasy Spark without its negation sentence is not FullyCompiled to the ATK action",
                    v == null || !v.FullyCompiled ||
                    !v.ClauseList.Any(c => c?.Action == EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn),
                    v == null ? "null" : $"full={v.FullyCompiled} [{string.Join("; ", v.ClauseList.Select(c => c.Action))}]");
            }

            Check("new actions are mapped (EffectVocabulary.ResolutionOf) and have a runtime",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.InflictDamageToTrapActivatorAfterResolve }) == EffectResolutionKind.Damage &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.GainLpWhenSpellCardActivationResolves }) == EffectResolutionKind.GainLp &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.TrapCardActivationsLockedThisTurn }) == EffectResolutionKind.Protection &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.InflictDamageToTrapActivatorAfterResolve) &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.GainLpWhenSpellCardActivationResolves) &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.TrapCardActivationsLockedThisTurn) &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.DrawOrDiscardRandomFromOpponentHand }) == EffectResolutionKind.Draw &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed }) == EffectResolutionKind.Equip &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.DrawOrDiscardRandomFromOpponentHand) &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed) &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn }) == EffectResolutionKind.ModifyStats &&
                !EffectVocabulary.HasNoRuntime(EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn) &&
                TurnNegationTrapsRuntime.IsKnownFamily(TurnNegationTrapsRuntime.OpponentTrap));
        }

        // ───────────────────────────── Chain Burst ─────────────────────────────

        static void ChainBurstDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Its controller's own Jar of Greed: that player draws, then takes 1000 once it resolved.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                FaceUpSpellTrap(e, p, ChainBurst, 0);
                var jar = PlaceSetTrap(e, p, JarOfGreed, 1);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, jar, fromHand: false);
                PassAll(e);
                Check("Chain Burst: its controller's own Jar of Greed resolves → that player draws 1 and takes 1000",
                    act && p.Graveyard.Contains(jar) && p.Hand.Count == 1 && p.LifePoints == lp - 1000,
                    $"act={act} hand={p.Hand.Count} lp={p.LifePoints} was={lp} {Window(e)}");
            }

            {
                // The opponent's Jar of Greed on its turn: the opponent takes 1000, the Chain Burst controller nothing.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, ChainBurst, 0);
                var jar = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, jar, fromHand: false);
                PassAll(e);
                Check("Chain Burst: the opponent's Jar of Greed → 1000 damage to the opponent only",
                    act && o.LifePoints == lp - 1000 && p.LifePoints == pLp && o.Hand.Count == 1,
                    $"reached={reached} act={act} oppLp={o.LifePoints} was={lp} pLp={p.LifePoints} oppHand={o.Hand.Count}");
            }

            {
                // A Trap answering a summon (response window, not a free chain): the AI's Trap Hole on the Player's
                // Normal Summon → the AI takes 1000.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, ChainBurst, 0);
                var hole = PlaceSetTrap(e, o, TrapHole, 0);
                var celtic = PutInHand(e, p, Celtic);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var ns = e.TryNormalSummon(p, celtic, asSet: false);
                PassAll(e);
                Check("Chain Burst: the AI's Trap Hole answering a Normal Summon → 1000 damage to the AI",
                    ns && o.Graveyard.Contains(hole) && p.Graveyard.Contains(celtic) && o.LifePoints == lp - 1000 &&
                    p.LifePoints == pLp,
                    $"ns={ns} holeGy={o.Graveyard.Contains(hole)} celticGy={p.Graveyard.Contains(celtic)} oppLp={o.LifePoints} was={lp} pLp={p.LifePoints}");
            }

            {
                // A negated Trap activation never resolves; the Counter Trap that negated it is a Trap Card activation too:
                // the opponent's Jar of Greed, the Player's Seven Tools (1000 LP cost) → the Player takes 1000 more, the
                // opponent nothing.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, ChainBurst, 0);
                var tools = PlaceSetTrap(e, p, SevenTools, 1);
                var jar = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, jar, fromHand: false);
                var offered = ChainWindowOffers(e, p, tools);
                var negated = offered && e.TryActivateSpellTrap(p, tools, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(e.PendingActivation.LegalTargets.FirstOrDefault());
                PassAll(e);
                Check("Chain Burst: a negated Jar of Greed deals no damage; the Seven Tools that negated it deals 1000 to its activator",
                    act && negated && o.Graveyard.Contains(jar) && o.Hand.Count == 0 && o.LifePoints == lp &&
                    p.LifePoints == pLp - 2000,
                    $"act={act} offered={offered} negated={negated} oppHand={o.Hand.Count} oppLp={o.LifePoints} was={lp} pLp={p.LifePoints} was={pLp} {Window(e)}");
            }

            {
                // aux.chainreg: Chain Burst activated in answer to the Jar (Chain Link 2) did not apply when the Jar was
                // activated — no damage for the Jar; nor does Chain Burst's own activation count.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var burst = PlaceSetTrap(e, p, ChainBurst, 0);
                var jar = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, jar, fromHand: false);
                var offered = ChainWindowOffers(e, p, burst);
                var chained = offered && e.TryActivateSpellTrap(p, burst, fromHand: false);
                PassAll(e);
                Check("Chain Burst (near miss): chained to the Jar it was not applying when the Jar was activated; its own activation does not count",
                    act && chained && burst.FaceUp && p.TryFindSpellTrap(burst, out _) && o.Hand.Count == 1 &&
                    o.LifePoints == lp && p.LifePoints == pLp,
                    $"act={act} offered={offered} chained={chained} oppLp={o.LifePoints} was={lp} pLp={p.LifePoints} {Window(e)}");
            }

            {
                // A Spell Card is not a Trap Card: Pot of Greed → no damage.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                FaceUpSpellTrap(e, p, ChainBurst, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                Check("Chain Burst (near miss): a Spell Card's activation (Pot of Greed) deals no damage",
                    act && p.Hand.Count == 2 && p.LifePoints == lp, $"act={act} hand={p.Hand.Count} lp={p.LifePoints}");
            }

            {
                // Negated (its effects do not apply) → no damage; Curse of Darkness (Spells) does not react to a Trap.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var burst = FaceUpSpellTrap(e, p, ChainBurst, 0);
                FaceUpSpellTrap(e, p, CurseOfDarkness, 2);
                burst.IsNegated = true;
                var jar = PlaceSetTrap(e, p, JarOfGreed, 1);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, jar, fromHand: false);
                PassAll(e);
                Check("Chain Burst (near miss): while negated it deals no damage; Curse of Darkness ignores a Trap",
                    act && p.Hand.Count == 1 && p.LifePoints == lp, $"act={act} hand={p.Hand.Count} lp={p.LifePoints}");
            }
        }

        // ───────────────────────────── Spell Absorption ─────────────────────────────

        static void SpellAbsorptionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Its controller's Pot of Greed: +500 once it resolved.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                FaceUpSpellTrap(e, p, SpellAbsorption, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                Check("Spell Absorption: its controller's Pot of Greed resolves → that player gains 500",
                    act && p.Hand.Count == 2 && p.LifePoints == lp + 500, $"act={act} hand={p.Hand.Count} lp={p.LifePoints} was={lp}");
            }

            {
                // The opponent's Pot of Greed: the Spell Absorption controller gains 500, the opponent nothing.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, SpellAbsorption, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                PassAll(e);
                Check("Spell Absorption: the opponent's Pot of Greed → its controller gains 500, the opponent nothing",
                    act && o.Hand.Count == 2 && p.LifePoints == pLp + 500 && o.LifePoints == lp,
                    $"reached={reached} act={act} pLp={p.LifePoints} was={pLp} oppLp={o.LifePoints}");
            }

            {
                // A negated Spell activation never resolves: the opponent's Pot negated by the Player's Magic Jammer.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, SpellAbsorption, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = ChainWindowOffers(e, p, jammer);
                var negated = offered && e.TryActivateSpellTrap(p, jammer, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(e.PendingActivation.LegalTargets.FirstOrDefault());
                PassAll(e);
                Check("Spell Absorption (near miss): a negated Spell activation gives no LP; the Counter Trap is not a Spell",
                    act && negated && o.Graveyard.Contains(pot) && o.Hand.Count == 0 && p.LifePoints == pLp,
                    $"act={act} offered={offered} negated={negated} oppHand={o.Hand.Count} pLp={p.LifePoints} was={pLp} {Window(e)}");
            }

            {
                // Its own activation does not count; a Trap Card (Jar of Greed) does not either.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var absorb = PutInHand(e, p, SpellAbsorption);
                var jar = PlaceSetTrap(e, p, JarOfGreed, 1);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, absorb, fromHand: true);
                PassAll(e);
                var lpAfterOwn = p.LifePoints;
                var jarAct = e.TryActivateSpellTrap(p, jar, fromHand: false);
                PassAll(e);
                Check("Spell Absorption (near miss): its own activation and a Trap Card's give no LP",
                    act && absorb.FaceUp && p.TryFindSpellTrap(absorb, out _) && lpAfterOwn == lp && jarAct &&
                    p.Hand.Count == 1 && p.LifePoints == lp,
                    $"act={act} faceUp={absorb.FaceUp} afterOwn={lpAfterOwn} jar={jarAct} hand={p.Hand.Count} lp={p.LifePoints} was={lp}");
            }
        }

        // ───────────────────────────── Forced Ceasefire ─────────────────────────────

        static void ForcedCeasefireDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Discard 1 (the cost), then no Trap Card can be activated by either player this turn; Spells still can;
                // the lock ends with the turn.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var fc = PlaceSetTrap(e, p, ForcedCeasefire, 0);
                var jar = PlaceSetTrap(e, p, JarOfGreed, 1);
                var oppJar = PlaceSetTrap(e, o, JarOfGreed, 0);
                var fodder = PutInHand(e, p, Celtic);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var canBefore = e.CanActivateSpellTrap(p, jar, fromHand: false);
                var act = e.TryActivateSpellTrap(p, fc, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(fodder))
                    e.TrySelectEffectTarget(fodder);
                PassAll(e);
                var discarded = p.Graveyard.Contains(fodder) && p.Graveyard.Contains(fc) && p.Hand.Contains(pot);
                var ownLocked = !e.CanActivateSpellTrap(p, jar, fromHand: false);
                var oppLocked = ContinuousNegationRuntime.ActivationLocked(e, o, oppJar, out _);
                var spellOk = e.CanActivateSpellTrap(p, pot, fromHand: true);
                Check("Forced Ceasefire: discard 1 as the cost; then neither player can activate a Trap Card; Spells stay legal",
                    canBefore && act && discarded && ownLocked && oppLocked && spellOk,
                    $"before={canBefore} act={act} discarded={discarded} ownLocked={ownLocked} oppLocked={oppLocked} spell={spellOk} hand=[{string.Join(",", p.Hand.Select(c => c.Name))}] {Window(e)}");

                var ended = ReachOpponentMain(e);
                var oppFree = !ContinuousNegationRuntime.ActivationLocked(e, o, oppJar, out _);
                Check("Forced Ceasefire: the lock ends with the turn (the opponent's next turn is free)",
                    ended && oppFree, $"reached={ended} free={oppFree} turn={e.TurnNumber}");
            }

            {
                // The cost needs another card in the hand.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var fc = PlaceSetTrap(e, p, ForcedCeasefire, 0);
                e.NotifyPublic();
                Check("Forced Ceasefire (near miss): with an empty hand it cannot be activated (discard cost)",
                    !e.CanActivateSpellTrap(p, fc, fromHand: false), $"hand={p.Hand.Count}");
            }

            {
                // The AI cannot answer a Normal Summon with its Trap Hole once Forced Ceasefire resolved.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var fc = PlaceSetTrap(e, p, ForcedCeasefire, 0);
                var hole = PlaceSetTrap(e, o, TrapHole, 0);
                var fodder = PutInHand(e, p, Bewd);
                var celtic = PutInHand(e, p, Celtic);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, fc, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(fodder))
                    e.TrySelectEffectTarget(fodder);
                PassAll(e);
                var ns = act && p.Hand.Contains(celtic) && e.TryNormalSummon(p, celtic, asSet: false);
                PassAll(e);
                var survived = p.MonstersOnField().Contains(celtic);
                Check("Forced Ceasefire: the AI's Set Trap Hole cannot answer the Player's Normal Summon this turn",
                    act && ns && survived && o.TryFindSpellTrap(hole, out _) && !hole.FaceUp,
                    $"act={act} ns={ns} survived={survived} holeSet={o.TryFindSpellTrap(hole, out _)} {Window(e)}");
            }
        }

        // ───────────────────────────── Necklace of Command ─────────────────────────────

        static void NecklaceDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            foreach (var draw in new[] { true, false })
            {
                // The Player equips its Celtic, which attacks BEWD and is destroyed by battle: the Player chooses — tap the
                // Necklace to draw 1, CANCEL to discard 1 random card from the opponent's hand.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var necklace = PutInHand(e, p, NecklaceOfCommand);
                PutInHand(e, o, Celtic);
                e.NotifyPublic();
                var offersBoth = false;
                var act = e.TryActivateSpellTrap(p, necklace, fromHand: true);
                if (act && e.IsAwaitingEffectTarget)
                {
                    offersBoth = e.PendingActivation.LegalTargets.Contains(celtic) && e.PendingActivation.LegalTargets.Contains(bewd);
                    e.TrySelectEffectTarget(celtic);
                }

                PassAll(e);
                var equipped = ReferenceEquals(necklace.EquippedTo, celtic);
                var hand = p.Hand.Count;
                var attacked = reached && equipped && AttackAndDrain(e, p, celtic, bewd);
                PassAll(e);
                var asked = AwaitingPickOf(e, necklace) && e.PendingActivation.LegalTargets.Contains(necklace);
                if (asked)
                {
                    if (draw) e.TrySelectEffectTarget(necklace);
                    else e.CancelEffectTargeting();
                }

                PassAll(e);
                var outcome = draw
                    ? p.Hand.Count == hand + 1 && o.Hand.Count == 1
                    : p.Hand.Count == hand && o.Hand.Count == 0 && o.Graveyard.Any(c => c.CardId == Celtic);
                Check($"Necklace of Command: its monster destroyed by battle → the Player {(draw ? "taps it and draws 1" : "CANCELs and discards 1 random card from the opponent's hand")}",
                    act && offersBoth && equipped && attacked && p.Graveyard.Contains(celtic) && p.Graveyard.Contains(necklace) &&
                    asked && outcome && e.PendingActivation == null,
                    $"act={act} both={offersBoth} equipped={equipped} attacked={attacked} asked={asked} hand={p.Hand.Count} was={hand} oppHand={o.Hand.Count} {Board(e)}");
            }

            {
                // Only one option possible (the opponent's hand is empty): it draws with no prompt.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var necklace = PutInHand(e, p, NecklaceOfCommand);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, necklace, celtic);
                var hand = p.Hand.Count;
                var attacked = reached && equipped && AttackAndDrain(e, p, celtic, bewd);
                PassAll(e);
                Check("Necklace of Command: with the opponent's hand empty it draws 1 at once (no choice to make)",
                    equipped && attacked && p.Graveyard.Contains(necklace) && p.Hand.Count == hand + 1 && e.PendingActivation == null,
                    $"equipped={equipped} attacked={attacked} hand={p.Hand.Count} was={hand} {Board(e)}");
            }

            {
                // Near miss: destroyed by a card effect (not by battle) → no trigger.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var necklace = PutInHand(e, p, NecklaceOfCommand);
                PutInHand(e, o, Celtic);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, necklace, celtic);
                var hand = p.Hand.Count;
                e.DestroyMonsterPublic(p, celtic);
                e.NotifyPublic();
                PassAll(e);
                Check("Necklace of Command (near miss): its monster destroyed by an effect, not by battle — nothing happens",
                    equipped && p.Graveyard.Contains(necklace) && p.Hand.Count == hand && o.Hand.Count == 1 &&
                    e.PendingActivation == null, $"equipped={equipped} hand={p.Hand.Count} oppHand={o.Hand.Count} {Board(e)}");
            }

            {
                // Near miss: equipped to the opponent's monster ("a monster you control") → no trigger when it is destroyed.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var necklace = PutInHand(e, p, NecklaceOfCommand);
                PutInHand(e, o, Celtic);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, necklace, celtic);
                var hand = p.Hand.Count;
                var attacked = reached && equipped && AttackAndDrain(e, p, bewd, celtic);
                PassAll(e);
                Check("Necklace of Command (near miss): equipped to the opponent's monster destroyed by battle — nothing happens",
                    equipped && attacked && o.Graveyard.Contains(celtic) && p.Graveyard.Contains(necklace) &&
                    p.Hand.Count == hand && o.Hand.Count == 1 && e.PendingActivation == null,
                    $"equipped={equipped} attacked={attacked} hand={p.Hand.Count} oppHand={o.Hand.Count} {Board(e)}");
            }

            {
                // The AI's Necklace on its own monster, destroyed by the Player's attack: the AI draws 1.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var necklace = FaceUpSpellTrap(e, o, NecklaceOfCommand, 0);
                necklace.EquippedTo = celtic;
                celtic.Equips.Add(necklace);
                PutInHand(e, p, Celtic);
                e.NotifyPublic();
                var hand = o.Hand.Count;
                var attacked = reached && AttackAndDrain(e, p, bewd, celtic);
                PassAll(e);
                Check("Necklace of Command (AI): its monster destroyed by battle → the AI draws 1",
                    attacked && o.Graveyard.Contains(necklace) && o.Hand.Count == hand + 1 && p.Hand.Count == 1 &&
                    e.PendingActivation == null, $"attacked={attacked} oppHand={o.Hand.Count} was={hand} {Board(e)}");
            }
        }

        // ───────────────────────────── Flint ─────────────────────────────

        static void FlintDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The AI activates Flint on the Player's monster and Necklace of Command on its own.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var flint = PutInHand(e, o, Flint);
                var necklace = PutInHand(e, o, NecklaceOfCommand);
                e.NotifyPublic();
                var f = reached && e.TryActivateSpellTrap(o, flint, fromHand: true);
                PassAll(e);
                var n = reached && e.TryActivateSpellTrap(o, necklace, fromHand: true);
                PassAll(e);
                Check("Flint / Necklace of Command (AI): Flint goes on the Player's monster, the Necklace on the AI's own",
                    f && n && ReferenceEquals(flint.EquippedTo, bewd) && ReferenceEquals(necklace.EquippedTo, celtic) &&
                    bewd.CurrentAtk == 2700, $"reached={reached} f={f} n={n} flint→{flint.EquippedTo?.Name} necklace→{necklace.EquippedTo?.Name} {Board(e)}");
            }

            {
                // Equipped to the opponent's BEWD: 2700 ATK, it cannot change its battle position or declare an attack.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var flint = PutInHand(e, p, Flint);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, flint, bewd);
                var atk = bewd.CurrentAtk;
                var noAttack = TextEffectRuntime.AttackForbiddenByEffect(e, bewd);
                var noPosition = TextEffectRuntime.PositionChangeForbiddenByEffect(bewd);
                var ended = ReachOpponentMain(e);
                var canPos = e.CanChangePosition(o, bewd);
                Check("Flint: the equipped monster loses 300 ATK, cannot declare an attack nor change its battle position",
                    equipped && atk == 2700 && noAttack && noPosition && ended && !canPos,
                    $"equipped={equipped} atk={atk} noAttack={noAttack} noPos={noPosition} reached={ended} canPos={canPos} {Board(e)}");
            }

            {
                // Its monster destroyed: the Player targets another face-up monster and Flint is equipped to it from the GY.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var hidden = PlaceMonster(e, o, Celtic, 2, BattlePosition.Defense, false);
                var flint = PutInHand(e, p, Flint);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, flint, bewd);
                e.DestroyMonsterPublic(o, bewd);
                e.NotifyPublic();
                PassAll(e);
                var asked = AwaitingPickOf(e, flint);
                var offers = asked && e.PendingActivation.LegalTargets.Contains(celtic) &&
                             e.PendingActivation.LegalTargets.Contains(elf) && !e.PendingActivation.LegalTargets.Contains(hidden);
                if (asked) e.TrySelectEffectTarget(celtic);
                PassAll(e);
                var reEquipped = ReferenceEquals(flint.EquippedTo, celtic) && p.TryFindSpellTrap(flint, out _) &&
                                 !p.Graveyard.Contains(flint) && celtic.CurrentAtk == 1100 &&
                                 TextEffectRuntime.AttackForbiddenByEffect(e, celtic);
                Check("Flint: its monster destroyed → the Player targets a face-up monster (either field, not a face-down one); Flint equips it from the GY",
                    equipped && o.Graveyard.Contains(bewd) && asked && offers && reEquipped,
                    $"equipped={equipped} asked={asked} offers={offers} reEquipped={reEquipped} {Board(e)}");
            }

            {
                // The AI's Flint on the Player's monster: destroyed → the AI equips it to the Player's strongest face-up monster.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                var flint = FaceUpSpellTrap(e, o, Flint, 0);
                flint.EquippedTo = celtic;
                celtic.Equips.Add(flint);
                e.NotifyPublic();
                e.DestroyMonsterPublic(p, celtic);
                e.NotifyPublic();
                PassAll(e);
                Check("Flint (AI): its monster destroyed → the AI re-equips it to the Player's strongest face-up monster",
                    p.Graveyard.Contains(celtic) && ReferenceEquals(flint.EquippedTo, bewd) && o.TryFindSpellTrap(flint, out _) &&
                    bewd.CurrentAtk == 2700 && e.PendingActivation == null, $"host={flint.EquippedTo?.Name} {Board(e)}");
            }

            {
                // Near miss: its monster leaves the field without being destroyed (banished) → Flint goes to the GY, no trigger.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var flint = PutInHand(e, p, Flint);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, flint, bewd);
                e.BanishCard(o, bewd);
                e.NotifyPublic();
                PassAll(e);
                Check("Flint (near miss): its monster banished (not destroyed) — Flint stays in the GY, no re-equip",
                    equipped && p.Graveyard.Contains(flint) && flint.EquippedTo == null && e.PendingActivation == null,
                    $"equipped={equipped} {Board(e)}");
            }

            {
                // No face-up monster left on the field → the trigger is not activated.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
                var flint = PutInHand(e, p, Flint);
                e.NotifyPublic();
                var equipped = EquipFromHand(e, p, flint, bewd);
                e.DestroyMonsterPublic(o, bewd);
                e.NotifyPublic();
                PassAll(e);
                Check("Flint (near miss): no face-up monster on the field — it stays in the GY with no prompt",
                    equipped && p.Graveyard.Contains(flint) && e.PendingActivation == null, $"equipped={equipped} {Board(e)}");
            }
        }

        // ───────────────────────────── Triangle Ecstasy Spark ─────────────────────────────

        static void TriangleEcstasySparkDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Both fields' face-up Harpie Lady Sisters become 2700 (one with an Axe of Despair too); the opponent cannot
                // activate Trap Cards, the Player still can; the opponent's face-up Trap is negated, the Player's is not; all
                // of it ends with the turn.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, HarpieLadySisters, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(e, o, HarpieLadySisters, 0, BattlePosition.Attack, true);
                var axe = FaceUpSpellTrap(e, p, AxeOfDespair, 3);
                axe.EquippedTo = mine;
                mine.Equips.Add(axe);
                var myJar = PlaceSetTrap(e, p, JarOfGreed, 0);
                var myHaunted = FaceUpSpellTrap(e, p, CallOfTheHaunted, 1);
                var oppJar = PlaceSetTrap(e, o, JarOfGreed, 0);
                var oppHaunted = FaceUpSpellTrap(e, o, CallOfTheHaunted, 1);
                var tes = PutInHand(e, p, TriangleEcstasySpark);
                e.NotifyPublic();
                var before = mine.CurrentAtk;
                var act = reached && e.TryActivateSpellTrap(p, tes, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                var atk = mine.CurrentAtk == 2700 && theirs.CurrentAtk == 2700;
                var oppLocked = ContinuousNegationRuntime.ActivationLocked(e, o, oppJar, out _);
                var mineFree = e.CanActivateSpellTrap(p, myJar, fromHand: false);
                var negation = oppHaunted.IsNegated && !myHaunted.IsNegated;
                Check("Triangle Ecstasy Spark: both Harpie Lady Sisters become 2700; the opponent's Trap Cards are locked and their face-up Trap negated, the Player's are not",
                    act && before == 2950 && p.Graveyard.Contains(tes) && atk && oppLocked && mineFree && negation,
                    $"act={act} before={before} mine={mine.CurrentAtk} theirs={theirs.CurrentAtk} oppLocked={oppLocked} mineFree={mineFree} oppNeg={oppHaunted.IsNegated} myNeg={myHaunted.IsNegated} {Board(e)}");

                var later = PlaceMonster(e, p, HarpieLadySisters, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Triangle Ecstasy Spark: a Harpie Lady Sisters that arrives after it resolved keeps its ATK (\"currently on the field\")",
                    later.CurrentAtk == 1950, $"later={later.CurrentAtk}");

                var next = ReachOpponentMain(e);
                e.NotifyPublic();
                var reset = theirs.CurrentAtk == 1950 && !ContinuousNegationRuntime.ActivationLocked(e, o, oppJar, out _) &&
                            !oppHaunted.IsNegated;
                Check("Triangle Ecstasy Spark: everything ends with the turn (ATK back, the opponent's Traps free and no longer negated)",
                    next && reset, $"reached={next} theirs={theirs.CurrentAtk} neg={oppHaunted.IsNegated} {Board(e)}");
            }

            {
                // Needs a face-up Harpie Lady Sisters on the field: none, or only a face-down one → cannot be activated.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var tes = PutInHand(e, p, TriangleEcstasySpark);
                e.NotifyPublic();
                var none = !e.CanActivateSpellTrap(p, tes, fromHand: true);
                PlaceMonster(e, p, HarpieLadySisters, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var faceDown = !e.CanActivateSpellTrap(p, tes, fromHand: true);
                Check("Triangle Ecstasy Spark (near miss): no face-up Harpie Lady Sisters (none, or face-down only) — it cannot be activated",
                    none && faceDown, $"none={none} faceDown={faceDown}");
            }

            {
                // The AI cannot answer the Player's Normal Summon with its Set Trap Hole after the Player's Spark resolved.
                var e = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, HarpieLadySisters, 0, BattlePosition.Attack, true);
                var hole = PlaceSetTrap(e, o, TrapHole, 0);
                var tes = PutInHand(e, p, TriangleEcstasySpark);
                var celtic = PutInHand(e, p, Celtic);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(p, tes, fromHand: true);
                PassAll(e);
                var ns = act && e.TryNormalSummon(p, celtic, asSet: false);
                PassAll(e);
                Check("Triangle Ecstasy Spark: the AI's Set Trap Hole cannot answer the Player's Normal Summon this turn",
                    act && ns && p.MonstersOnField().Contains(celtic) && o.TryFindSpellTrap(hole, out _) && !hole.FaceUp,
                    $"act={act} ns={ns} {Board(e)}");
            }
        }
    }
}
