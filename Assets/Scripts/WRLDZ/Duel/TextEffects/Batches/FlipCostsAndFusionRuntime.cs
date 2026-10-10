using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// FlipCostsAndFusion batch runtime.
    /// <list type="bullet">
    /// <item>FLIP costs (Fire Sorcerer, Lady Assailant of Flames): <see cref="TryPayFlipCost"/> runs when the FLIP effect
    /// activates (TextEffectRuntime.TryResolveFlip — every Flip Summon, battle flip and queued flip goes through it).
    /// A cost that cannot be paid means the FLIP effect is not activated at all.</item>
    /// <item>Fusion Material bookkeeping (Supply, De-Fusion): <see cref="RecordFusionSummon"/> (SpellTrapEffects.TryResolveFusion,
    /// the only Fusion Summon path) stamps every material that reached a GY with the Fusion Monster
    /// (<see cref="CardInstance.SentAsFusionMaterialFor"/>, ygopro REASON_FUSION + GetReasonCard) and gives that monster
    /// its material list (<see cref="CardInstance.FusionMaterialsUsed"/>, ygopro GetMaterial). A stamp lives only while
    /// the card stays in the GY: a new trip to the GY clears it, and <see cref="SweepFusionMaterialStamps"/> clears it on
    /// every board refresh once the card is no longer in a GY.</item>
    /// <item>Summoner of Illusions and De-Fusion's choices go through the existing target picker
    /// (PendingActivation + TargetKind; SpellTrapEffects routes the taps here through <see cref="TryResolvePick"/>,
    /// <see cref="TryCancelPick"/> and <see cref="TryPickAsksPlacement"/>). The AI decides at once.</item>
    /// </list>
    /// </summary>
    public static class FlipCostsAndFusionRuntime
    {
        // ───────────────────────────── FLIP costs ─────────────────────────────

        /// <summary>
        /// The FLIP effect of <paramref name="monster"/> activates: pay its cost (Lua SetCost). Returns false when a
        /// clause has a cost that cannot be paid — the FLIP effect is then not activated (nothing resolves).
        /// </summary>
        public static bool TryPayFlipCost(DuelEngine engine, DuelistState who, CardInstance monster,
            IReadOnlyList<EffectClause> flip)
        {
            if (engine == null || who == null || flip == null) return true;
            var hand = 0;
            var deck = 0;
            foreach (var c in flip)
            {
                if (c == null) continue;
                hand = Math.Max(hand, c.CostBanishRandomHandCards);
                deck = Math.Max(deck, c.CostBanishTopDeckCards);
            }

            if (hand <= 0 && deck <= 0) return true;
            if (!CanPayFlipCost(who, hand, deck))
            {
                engine.Log($"{monster?.Name}: its FLIP effect cannot be activated — the cost cannot be paid.");
                return false;
            }

            if (hand > 0)
            {
                // Lua g:RandomSelect(tp, 2): chosen at random, then banished face-up as a cost.
                var pool = who.Hand.Where(h => h != null).ToList();
                var picked = new List<CardInstance>();
                for (var i = 0; i < hand && pool.Count > 0; i++)
                {
                    var pick = pool[engine.Rng.Next(pool.Count)];
                    pool.Remove(pick);
                    picked.Add(pick);
                }

                foreach (var p in picked)
                    engine.BanishCard(who, p);
                engine.Log($"{monster?.Name}: {who.Name} banishes {string.Join(", ", picked.Select(p => p.Name))} " +
                           "from their hand at random (cost).");
            }

            if (deck > 0)
            {
                var names = new List<string>();
                for (var i = 0; i < deck && who.Deck.Count > 0; i++)
                {
                    var id = who.Deck[0];
                    who.Deck.RemoveAt(0);
                    var inst = engine.CreateCardInstance(id);
                    if (inst == null) continue;
                    inst.FaceUp = true;
                    inst.BanishedTurnNumber = engine.TurnNumber;
                    // PerCopyDeck batch (v163): a copy another player owns is banished to its owner's pile.
                    PerCopyDeckRuntime.OwnersPile(who, inst).Banished.Add(inst);
                    names.Add(inst.Name);
                }

                engine.Log($"{monster?.Name}: {who.Name} banishes the top {names.Count} cards of their Deck " +
                           $"({string.Join(", ", names)}) (cost).");
            }

            return true;
        }

        /// <summary>Lua cost check: enough cards in the hand / Deck to banish.</summary>
        public static bool CanPayFlipCost(DuelistState who, int handCards, int deckCards) =>
            who != null &&
            (handCards <= 0 || who.Hand.Count(h => h != null) >= handCards) &&
            (deckCards <= 0 || who.Deck.Count >= deckCards);

        // ───────────────────────────── Fusion Material stamps ─────────────────────────────

        static readonly ConditionalWeakTable<DuelEngine, List<CardInstance>> s_stamped = new();

        /// <summary>
        /// A Fusion Summon by SpellTrapEffects.TryResolveFusion: <paramref name="fusion"/> remembers its materials, and
        /// each material now in a GY is stamped as sent there as Fusion Material for it.
        /// </summary>
        public static void RecordFusionSummon(DuelEngine engine, CardInstance fusion, List<CardInstance> materials)
        {
            if (engine == null || fusion == null || materials == null) return;
            fusion.FusionMaterialsUsed = new List<CardInstance>(materials.Where(m => m != null));
            var tracked = s_stamped.GetOrCreateValue(engine);
            foreach (var m in fusion.FusionMaterialsUsed)
            {
                if (!InAnyGy(engine, m)) continue; // banished instead (Banisher of the Light): not in the GY
                m.SentAsFusionMaterialFor = fusion;
                if (!tracked.Contains(m)) tracked.Add(m);
            }
        }

        /// <summary>Every board refresh: a stamped card that is no longer in a GY loses its stamp for good.</summary>
        public static void SweepFusionMaterialStamps(DuelEngine engine)
        {
            if (engine == null || !s_stamped.TryGetValue(engine, out var tracked) || tracked.Count == 0) return;
            for (var i = tracked.Count - 1; i >= 0; i--)
            {
                var c = tracked[i];
                if (c != null && c.SentAsFusionMaterialFor != null && InAnyGy(engine, c)) continue;
                if (c != null) c.SentAsFusionMaterialFor = null;
                tracked.RemoveAt(i);
            }
        }

        /// <summary>Supply's filter: <paramref name="card"/> is in <paramref name="who"/>'s GY, sent there as Fusion Material.</summary>
        public static bool IsFusionMaterialInGy(DuelistState who, CardInstance card) =>
            who != null && card?.Def != null && card.Def.IsMonster && card.SentAsFusionMaterialFor != null &&
            who.Graveyard.Contains(card);

        static bool InAnyGy(DuelEngine engine, CardInstance c) =>
            c != null && ((engine.Player != null && engine.Player.Graveyard.Contains(c)) ||
                          (engine.Opponent != null && engine.Opponent.Graveyard.Contains(c)));

        /// <summary>
        /// Exact-count targets (Tornado Bird, Supply) still relate at resolution: on the field, or for a GY zone still in
        /// a GY (and, for Supply, still stamped as Fusion Material).
        /// </summary>
        public static bool AllExactTargetsStillThere(DuelEngine engine, EffectClause clause,
            IEnumerable<CardInstance> picks)
        {
            if (engine == null || picks == null) return false;
            if (clause != null && clause.Zone == EffectZoneFilter.ControllerGyMonsters)
                return picks.All(t => t != null && InAnyGy(engine, t) &&
                                      (!clause.RequiresSentAsFusionMaterial || t.SentAsFusionMaterialFor != null));
            return TargetedClassicsRuntime.AllStillOnField(engine, picks);
        }

        // ───────────────────────────── De-Fusion targets ─────────────────────────────

        static bool IsFusionMonster(CardInstance c) =>
            c?.Def != null && c.Def.IsMonster && c.Def.type != null &&
            c.Def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>CollectTargets (FieldFaceUpFusionMonsters): every face-up Fusion Monster on either field.</summary>
        public static void CollectFaceUpFusions(DuelEngine engine, DuelistState who, DuelistState opp,
            List<CardInstance> into)
        {
            if (engine == null || into == null) return;
            foreach (var side in new[] { who, opp })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || !m.FaceUp || !IsFusionMonster(m)) continue;
                    if (engine.IsDragonTargetProtected(m)) continue;
                    if (!into.Contains(m)) into.Add(m);
                }
            }
        }

        /// <summary>AutoPick (FieldFaceUpFusionMonsters): the opponent's strongest Fusion first, else your own.</summary>
        public static CardInstance AutoPickFusion(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var opp = engine?.OpponentOf(who);
            return targets.Where(t => opp != null && opp.TryFindMonster(t, out _))
                       .OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
                   ?? targets.OrderByDescending(t => t.CurrentAtk).First();
        }

        // ───────────────────────────── picks (Summoner of Illusions, De-Fusion) ─────────────────────────────

        enum Step
        {
            SummonerTribute,
            SummonerFusion,
            DeFusionMaterials
        }

        sealed class Plan
        {
            public Step Step;
            public DuelistState Who;
            public CardInstance Source;
            public readonly List<CardInstance> Candidates = new();
            /// <summary>Summoner: the Fusion Monsters on offer (instances not yet in any zone).</summary>
            public readonly List<CardInstance> Fusions = new();
            /// <summary>De-Fusion: the materials chosen so far with their position and zone.</summary>
            public readonly List<(CardInstance card, BattlePosition pos, int zone)> Chosen = new();
            public bool HoldsChain;
        }

        static readonly ConditionalWeakTable<PendingActivation, Plan> s_picks = new();

        /// <summary>This batch owns the open pick.</summary>
        public static bool IsBatchPick(PendingActivation pending) =>
            pending != null && s_picks.TryGetValue(pending, out _);

        /// <summary>
        /// SpellTrapEffects.PendingTargetIsSpecialSummon: the Fusion Monster and De-Fusion's materials are placed (the UI
        /// asks face-up Attack or Defense and a zone); the Tribute is not. False for any other pending activation.
        /// </summary>
        public static bool TryPickAsksPlacement(PendingActivation pending, out bool asksPlacement)
        {
            asksPlacement = false;
            if (pending == null || !s_picks.TryGetValue(pending, out var plan)) return false;
            asksPlacement = plan.Step != Step.SummonerTribute;
            return true;
        }

        // ── Summoner of Illusions ──

        /// <summary>
        /// Summoner of Illusions (Flip): Tribute 1 other monster you control (an effect Tribute; your choice), and if you
        /// do, Special Summon 1 Fusion Monster of your choice from your Extra Deck in face-up Attack or Defense Position
        /// (not a Fusion Summon); it is destroyed during this turn's End Phase. Nothing happens unless both a Tribute and
        /// a Fusion Monster that can be Special Summoned exist (Lua rfilter).
        /// </summary>
        public static void ResolveSummonerOfIllusions(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var tributes = TributeCandidates(engine, who, source);
            var fusions = FusionCandidates(engine, who);
            if (tributes.Count == 0 || fusions.Count == 0)
            {
                engine.Log($"{source?.Name}: " + (tributes.Count == 0
                    ? "no other monster can be Tributed — no effect."
                    : "no Fusion Monster in the Extra Deck can be Special Summoned — no effect."));
                return;
            }

            var plan = new Plan { Step = Step.SummonerTribute, Who = who, Source = source };
            plan.Candidates.AddRange(tributes);
            plan.Fusions.AddRange(fusions);
            if (!engine.IsHumanControlled(who))
            {
                Tribute(engine, plan, AiPickTribute(tributes));
                return;
            }

            if (tributes.Count == 1)
            {
                Tribute(engine, plan, tributes[0]);
                return;
            }

            OpenPick(engine, plan, EffectTargetKind.YourMonster, plan.Candidates, 1,
                "Tribute 1 other monster you control (by this effect); then you Special Summon 1 Fusion Monster " +
                "from your Extra Deck.");
        }

        static List<CardInstance> TributeCandidates(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (ContinuousProtections.NeitherPlayerCanTribute(engine)) return list; // Mask of Restrict
            foreach (var m in who.MonstersOnField())
                if (m != null && m != source)
                    list.Add(m);
            return list;
        }

        /// <summary>One instance per distinct Fusion Monster in the Extra Deck that can be Special Summoned.</summary>
        static List<CardInstance> FusionCandidates(DuelEngine engine, DuelistState who)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.ExtraDeck == null) return list;
            foreach (var id in who.ExtraDeck.Distinct())
            {
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster || def.type == null ||
                    def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var inst = engine.CreateCardInstance(id);
                if (!ExtraDeckCopyCanBeSpecialSummoned(engine, who, inst)) continue;
                list.Add(inst);
            }

            return list;
        }

        // Summoning conditions of a Fusion Monster still in the Extra Deck ("Must be Fusion Summoned.", contact
        // procedures, Nomi): SpecialSummonLegality (EngineHygiene, v160).

        /// <remarks>
        /// RuleLocks batch (v159-lon-b audit): Jowgen the Spiritualist / Jam Breeding Machine also make
        /// IsCanBeSpecialSummoned false (EFFECT_CANNOT_SPECIAL_SUMMON), so under them nothing is offered or Tributed.
        /// </remarks>
        /// EngineHygiene batch (v160): the one engine-aware check (<see cref="SpecialSummonLegality.CanBeSpecialSummoned"/>).
        static bool ExtraDeckCopyCanBeSpecialSummoned(DuelEngine engine, DuelistState who, CardInstance c) =>
            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, c, null, SummonOrigin.ExtraDeck);

        static CardInstance AiPickTribute(List<CardInstance> tributes) =>
            tributes.OrderByDescending(t => t.IsToken)
                .ThenBy(t => t.FaceUp ? t.CurrentAtk : (t.Def?.atk ?? 0))
                .First();

        static CardInstance AiPickFusion(List<CardInstance> fusions) =>
            fusions.OrderByDescending(f => f.Def?.atk ?? 0).First();

        static void Tribute(DuelEngine engine, Plan plan, CardInstance pick)
        {
            var who = plan.Who;
            if (pick == null || !who.TryFindMonster(pick, out _))
            {
                engine.Log($"{plan.Source?.Name}: no monster was Tributed — no Special Summon.");
                engine.NotifyPublic();
                return;
            }

            var owner = engine.ControllerOf(pick) ?? who;
            ReleaseEvents.Record(engine, pick, who); // EngineHygiene: EVENT_RELEASE
            engine.SendCardToGrave(owner, pick, sentBy: plan.Source);
            engine.Log($"{plan.Source?.Name}: {who.Name} Tributes {pick.Name}.");
            if (engine.GameOver) return;
            if (who.TryFindMonster(pick, out _))
            {
                engine.Log($"{plan.Source?.Name}: {pick.Name} was not Tributed — no Special Summon.");
                engine.NotifyPublic();
                return;
            }

            // "and if you do": Special Summon 1 Fusion Monster (still in the Extra Deck now).
            plan.Fusions.RemoveAll(f => f == null || !who.ExtraDeck.Contains(f.CardId) ||
                                        !ExtraDeckCopyCanBeSpecialSummoned(engine, who, f));
            if (plan.Fusions.Count == 0 || engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{plan.Source?.Name}: no Fusion Monster can be Special Summoned.");
                engine.NotifyPublic();
                return;
            }

            // A choice the Tribute itself opened (a Tributed Sangan's search) is never displaced: the Fusion Monster is
            // then chosen automatically.
            if (!engine.IsHumanControlled(who) || engine.PendingActivation != null)
            {
                SummonFusion(engine, plan, AiPickFusion(plan.Fusions), BattlePosition.Attack, -1);
                return;
            }

            plan.Step = Step.SummonerFusion;
            OpenPick(engine, plan, EffectTargetKind.MonsterInYourDeckToSummon, plan.Fusions, 1,
                "Special Summon 1 Fusion Monster from your Extra Deck in face-up Attack or Defense Position " +
                "(destroyed during the End Phase).");
        }

        static void SummonFusion(DuelEngine engine, Plan plan, CardInstance fusion, BattlePosition pos, int zone)
        {
            var who = plan.Who;
            if (fusion == null || !who.ExtraDeck.Remove(fusion.CardId))
            {
                engine.Log($"{plan.Source?.Name}: that Fusion Monster is no longer in the Extra Deck.");
                engine.NotifyPublic();
                return;
            }

            if (!engine.SpecialSummonToField(who, fusion, pos, faceUp: true, SummonKind.SpecialSummon, zone))
            {
                who.ExtraDeck.Add(fusion.CardId);
                engine.Log($"{fusion.Name} could not be Special Summoned.");
                engine.NotifyPublic();
                return;
            }

            // Registered on that monster: it ends when it leaves the field (DuelEngine.ResetOnFieldEntry) or is
            // turned face-down (TextEffectRuntime.ClearFaceDownMarks).
            fusion.TempDestroyOnEndOfTurn = engine.TurnNumber;
            engine.Log($"{plan.Source?.Name}: {who.Name} Special Summons {fusion.Name} from the Extra Deck in face-up " +
                       $"{(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position; it is destroyed during the " +
                       "End Phase.");
            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        // ── De-Fusion ──

        /// <summary>
        /// De-Fusion: return the targeted face-up Fusion Monster to the Extra Deck; then, if it was Fusion Summoned and
        /// every material used for that Fusion Summon is in your GY as its material, can be Special Summoned and fits in
        /// your free Monster Zones, you can Special Summon all of them (each face-up Attack or Defense).
        /// </summary>
        public static void ResolveDeFusion(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            if (target == null || !target.FaceUp || !IsFusionMonster(target) || engine.ControllerOf(target) == null)
            {
                engine.Log($"{source?.Name}: the target is no longer a face-up Fusion Monster on the field — no effect.");
                return;
            }

            var materials = target.FusionMaterialsUsed != null
                ? new List<CardInstance>(target.FusionMaterialsUsed)
                : null;
            if (!engine.ReturnCardToHand(target, by: who)) return; // an Extra Deck monster goes to the Extra Deck
            if (materials == null || materials.Count == 0 || engine.GameOver) return; // not Fusion Summoned

            var free = who.MonsterZones.Count(z => z != null && z.IsEmpty);
            var ready = materials.All(m => m != null && who.Graveyard.Contains(m) &&
                                           m.SentAsFusionMaterialFor == target &&
                                           TextEffectRuntime.CanSpecialSummonFromGyTarget(m) &&
                                           FlipMassEffectsRuntime.CanBeSpecialSummonedByThis(who, m) &&
                                           // Jowgen / Jam Breeding Machine (RuleLocks): Lua mgfilter IsCanBeSpecialSummoned.
                                           !RuleLocksRuntime.SpecialSummonLocked(engine, who, m) &&
                                           !ContinuousProtections.GyMovementLocked(engine, source, m));
            if (!ready || materials.Count > free)
            {
                engine.Log($"{source?.Name}: not all of {target.Name}'s Fusion Materials can be Special Summoned " +
                           "from your GY.");
                return;
            }

            var plan = new Plan { Step = Step.DeFusionMaterials, Who = who, Source = source };
            plan.Candidates.AddRange(materials);
            if (!engine.IsHumanControlled(who))
            {
                // The AI always takes its materials back.
                foreach (var m in materials)
                    plan.Chosen.Add((m, AiPosition(m), -1));
                SummonMaterials(engine, plan);
                return;
            }

            plan.HoldsChain = engine.IsResolvingChain;
            OpenPick(engine, plan, EffectTargetKind.MonsterInEitherGy, plan.Candidates, plan.Candidates.Count,
                MaterialsPrompt(plan));
        }

        static BattlePosition AiPosition(CardInstance m) =>
            m?.Def != null && m.Def.def > m.Def.atk ? BattlePosition.Defense : BattlePosition.Attack;

        static string MaterialsPrompt(Plan plan) =>
            plan.Chosen.Count == 0
                ? $"you can Special Summon all {plan.Candidates.Count} Fusion Materials from your GY: tap each one " +
                  "to place it (face-up Attack or Defense), or CANCEL to not Summon them."
                : $"tap the next Fusion Material to place it ({plan.Candidates.Count} left), or CANCEL to Summon " +
                  "the rest in Attack Position.";

        static void SummonMaterials(DuelEngine engine, Plan plan)
        {
            var who = plan.Who;
            // Lua Duel.SpecialSummon(mg): the materials are Special Summoned together, one event (Card of Safe Return
            // draws once; NegationsAndGySummons batch, v159-lon-b audit).
            NegationsAndGySummonsRuntime.BeginGroupSpecialSummon(engine);
            try
            {
                foreach (var (m, pos, zone) in plan.Chosen)
                {
                    if (engine.GameOver) break;
                    if (!who.Graveyard.Contains(m))
                    {
                        engine.Log($"{plan.Source?.Name}: {m.Name} is no longer in the GY — it is not Special Summoned.");
                        continue;
                    }

                    who.Graveyard.Remove(m);
                    m.SentAsFusionMaterialFor = null;
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (engine.SpecialSummonToField(who, m, pos, faceUp: true, SummonKind.SpecialSummon, zone))
                        engine.Log($"{plan.Source?.Name}: {who.Name} Special Summons {m.Name} from the GY in face-up " +
                                   $"{(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                    else
                    {
                        engine.NextSpecialSummonFromGraveyard = false;
                        who.Graveyard.Add(m);
                        engine.Log($"{m.Name} could not be Special Summoned — it stays in the GY.");
                    }
                }
            }
            finally
            {
                NegationsAndGySummonsRuntime.EndGroupSpecialSummon(engine);
            }

            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        // ── the picker ──

        static void OpenPick(DuelEngine engine, Plan plan, EffectTargetKind kind, List<CardInstance> offer, int picks,
            string prompt)
        {
            var pending = new PendingActivation
            {
                Controller = plan.Who,
                Card = plan.Source,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = kind,
                IsMonsterEffect = plan.Step != Step.DeFusionMaterials,
                UsesTextProgram = true,
                TargetPicksRemaining = Math.Max(1, picks),
                HoldsChainResolution = plan.HoldsChain,
                PromptOverride = prompt
            };
            pending.LegalTargets.AddRange(offer);
            s_picks.Add(pending, plan);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
        }

        /// <summary>Close this batch's pick (forget it first so nothing treats it as displaced).</summary>
        static void ClosePick(DuelEngine engine, PendingActivation pending)
        {
            s_picks.Remove(pending);
            if (ReferenceEquals(engine.PendingActivation, pending))
                engine.ClearPendingActivation();
        }

        /// <summary>
        /// A tap on this batch's pick (SpellTrapEffects.TryResolveWithTarget). Returns false for any other pending
        /// activation; <paramref name="ok"/> is the tap's result.
        /// </summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var plan)) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            var chosenPosition = engine.PendingSpecialSummonPosition;
            var chosenZone = engine.PendingSpecialSummonZone;
            engine.ClearPendingSpecialSummonPlacement();
            if (pick == null || (!plan.Candidates.Contains(pick) && !plan.Fusions.Contains(pick)))
            {
                engine.Log("Not one of the offered cards.");
                return true;
            }

            ok = true;
            var pos = chosenPosition == BattlePosition.Defense ? BattlePosition.Defense : BattlePosition.Attack;
            switch (plan.Step)
            {
                case Step.SummonerTribute:
                    ClosePick(engine, pending);
                    Tribute(engine, plan, pick);
                    return true;
                case Step.SummonerFusion:
                    ClosePick(engine, pending);
                    SummonFusion(engine, plan, pick, pos, chosenZone);
                    return true;
                default:
                    plan.Candidates.Remove(pick);
                    pending.LegalTargets.Remove(pick);
                    plan.Chosen.Add((pick, pos, chosenZone));
                    if (plan.Candidates.Count > 0)
                    {
                        pending.TargetPicksRemaining = plan.Candidates.Count;
                        pending.PromptOverride = MaterialsPrompt(plan);
                        engine.Log(pending.Prompt);
                        engine.NotifyPublic();
                        return true;
                    }

                    ClosePick(engine, pending);
                    SummonMaterials(engine, plan);
                    ResumeChain(engine, plan);
                    return true;
            }
        }

        /// <summary>
        /// CANCEL on this batch's pick (SpellTrapEffects.CancelPending). Summoner's Tribute and Fusion choices are
        /// mandatory: CANCEL makes them as the AI would. De-Fusion's Summon is optional: CANCEL before the first tap
        /// declines it; after it, the rest are Summoned in Attack Position. Returns false for any other pending.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var plan)) return false;
            engine.ClearPendingSpecialSummonPlacement();
            ClosePick(engine, pending);
            switch (plan.Step)
            {
                case Step.SummonerTribute:
                    Tribute(engine, plan, AiPickTribute(plan.Candidates));
                    break;
                case Step.SummonerFusion:
                    SummonFusion(engine, plan, AiPickFusion(plan.Fusions), BattlePosition.Attack, -1);
                    break;
                default:
                    if (plan.Chosen.Count == 0)
                    {
                        engine.Log($"{plan.Source?.Name}: {plan.Who?.Name} does not Special Summon the Fusion Materials.");
                        engine.NotifyPublic();
                    }
                    else
                    {
                        foreach (var m in plan.Candidates)
                            plan.Chosen.Add((m, BattlePosition.Attack, -1));
                        plan.Candidates.Clear();
                        SummonMaterials(engine, plan);
                    }

                    ResumeChain(engine, plan);
                    break;
            }

            return true;
        }

        static void ResumeChain(DuelEngine engine, Plan plan)
        {
            if (plan.HoldsChain && (engine.PendingActivation == null || !engine.PendingActivation.HoldsChainResolution))
                engine.ResumeChainAfterChoice();
        }
    }
}
