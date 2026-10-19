using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DeckSummonsAndSarcophagi" (AST) runtime. Reads compiled clauses only — no card-id branches.
    /// Atoms: <see cref="DeckSummonsAndSarcophagiAtoms"/>.
    /// <list type="bullet">
    /// <item><b>Enchanting Fitting Room</b> (<see cref="EffectActionKind.ExcavateTopSpecialSummonLowLevelNormalsShuffleRest"/>,
    /// Lua c30531525): the top N cards are excavated; the Level-N-or-lower Normal Monsters among them that can be Special
    /// Summoned are Special Summoned face-up (Attack or Defense, the player's choice); with fewer free zones the player
    /// chooses that many and the other qualifying ones go to the GY (the FlipMassEffects plan); the rest stay in the Deck,
    /// which is shuffled after the Summons.</item>
    /// <item><b>Monster Gate</b> (<see cref="EffectActionKind.ExcavateUntilNormalSummonableSpecialSummonItSendRestToGy"/>,
    /// Lua c43040603): cost Tribute 1 monster that leaves a usable zone (<see cref="FilterTributeCostCandidates"/>); the
    /// cards above the top-most monster that can be Normal Summoned/Set (TributeSummonsRuntime.IsNormalSummonableDef,
    /// ygopro IsSummonableCard) go to the GY and that monster is Special Summoned face-up — or goes to the GY too when no
    /// zone is free or it cannot be Special Summoned. No such monster: the Deck is revealed and shuffled.</item>
    /// <item><b>Ninjitsu Art of Transformation</b>
    /// (<see cref="EffectActionKind.SpecialSummonRaceLevelUpToTributedPlusFromHandOrDeckLinked"/>, Lua c70861343): the
    /// cost Tributes 1 face-up series monster with a Level that has a match in the hand or Deck and records that Level
    /// (<see cref="RecordsTributedLevel"/>, read by TributeSummonsRuntime.TributeCostNumeric); at resolution, only while
    /// this card is still face-up on the field, 1 monster of a listed Type with Level &lt;= that Level + offset is chosen
    /// from the hand or Deck (not a target) and Special Summoned face-up, then linked (EquippedTo, so
    /// DuelEngine.UnlinkCardFromHostOnLeave destroys it when this card leaves); when the monster leaves first, this card
    /// stays (<see cref="StaysWhenLinkedMonsterLeaves"/>, read by DuelEngine.UnlinkEquips).</item>
    /// <item><b>Human-Wave Tactics</b>
    /// (<see cref="EffectActionKind.SpecialSummonLowLevelNormalsFromDeckPerOwnBattleDestroyedAtEndPhase"/>, Lua c30353551):
    /// <see cref="NoteBattle"/> (DuelEngine.ApplyBattleDestructions, Lua EVENT_BATTLED + STATUS_BATTLE_DESTROYED) counts,
    /// per player and per turn, the Level-N-or-lower Normal Monsters that battle is destroying, as they are on the field;
    /// at every End Phase a face-up copy selects exactly that many such monsters from its controller's Deck (all of them
    /// when fewer exist), Special Summons them face-up (fewer zones: the player chooses which; the others selected go to
    /// the GY) and the Deck is shuffled. A count of 0 does nothing.</item>
    /// <item><b>The First Sarcophagus</b>: the placement and the leave-field send are DestinyBoardRuntime's; after each
    /// placement <see cref="AfterNamedPlacement"/> (Lua plop after BreakEffect) sends this card and every placed card to
    /// the GY once all are face-up on your field and, only if all of them went there and a zone is free, Special Summons
    /// the named monster from the hand or Deck ignoring its Summoning conditions (TributeSummonsRuntime.BeginNamedSummon,
    /// CompleteProcedure).</item>
    /// <item><b>"This card cannot be Special Summoned except by the effect of "X"."</b> (Spirit of the Pharaoh; Lua
    /// EFFECT_SPSUMMON_CONDITION with no value): every Special Summon is refused except X's named one
    /// (<see cref="NomiBlocksSpecialSummon"/>, DuelEngine.SpecialSummonToField and SpecialSummonLegality), properly summoned
    /// before or not; the card is Implemented only while X compiles to that summon (<see cref="NamedEffectSummonIsLive"/>,
    /// CardEffectStatus.Classify).</item>
    /// <item><b>Spirit of the Pharaoh</b> (<see cref="EffectActionKind.SpecialSummonUpToTargetedLowLevelRaceNormalsFromGy"/>):
    /// "you can" — the controller targets 1 to min(N, free zones) qualifying monsters in their GY (a human one at a time:
    /// CANCEL first declines, CANCEL later stops), then they are Special Summoned face-up together, or none if fewer free
    /// zones than targets still in the GY remain (Lua s.operation).</item>
    /// </list>
    /// </summary>
    public static class DeckSummonsAndSarcophagiRuntime
    {
        // ── ActivationCondition values ──
        /// <summary>Enchanting Fitting Room: a free Monster Zone and at least <see cref="EffectClause.ConditionAmount"/> cards in the Deck.</summary>
        public const string FreeZoneAndDeckHoldsExcavation = "DssFreeZoneAndDeckHoldsExcavation";
        /// <summary>Monster Gate: a monster in the Deck that can be Normal Summoned/Set, and Special Summons are not locked.</summary>
        public const string NormalSummonableMonsterInDeck = "DssNormalSummonableMonsterInDeck";

        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null) return false;
            switch (c.ActivationCondition)
            {
                case FreeZoneAndDeckHoldsExcavation:
                    ok = engine != null && who?.Deck != null && engine.FirstEmpty(who.MonsterZones) >= 0 &&
                         who.Deck.Count >= Math.Max(1, c.ConditionAmount);
                    reason = $"Needs a free Monster Zone and at least {Math.Max(1, c.ConditionAmount)} cards in your Deck.";
                    return true;
                case NormalSummonableMonsterInDeck:
                    ok = engine?.Database != null && who?.Deck != null && who.Deck.Count > 0 &&
                         !RuleLocksRuntime.AnySpecialSummonLocked(engine, who, null, out _) &&
                         who.Deck.Any(id => engine.Database.TryGet(id, out var def) &&
                                            TributeSummonsRuntime.IsNormalSummonableDef(def));
                    reason = "Needs a monster in your Deck that can be Normal Summoned/Set.";
                    return true;
                default:
                    return false;
            }
        }

        static string NameOf(CardInstance source, string fallback) => source?.Name ?? fallback;

        static bool IsLowLevelNormal(CardDef def, int levelMax) =>
            def != null && def.IsMonster && def.IsNormalMonster && !def.IsExtraDeck && def.level >= 1 &&
            (levelMax <= 0 || def.level <= levelMax);

        // ───────────────────────────── Enchanting Fitting Room ─────────────────────────────

        /// <summary>The <see cref="EffectActionKind.ExcavateTopSpecialSummonLowLevelNormalsShuffleRest"/> arm.</summary>
        public static void ResolveFittingRoom(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine?.Database == null || who?.Deck == null || clause == null) return;
            var name = NameOf(source, "Enchanting Fitting Room");
            var n = Math.Min(Math.Max(1, clause.Amount), who.Deck.Count);
            if (n == 0)
            {
                engine.Log($"{name}: {who.Name}'s Deck is empty — nothing is excavated.");
                return;
            }

            var top = new List<string>();
            for (var i = 0; i < n; i++)
                top.Add(engine.Database.Get(who.Deck[i])?.name ?? "?");
            engine.Log($"{name}: {who.Name} excavates {string.Join(", ", top)}.");
            // The qualifying monsters leave the Deck (each instance takes its copy); the rest stay where they are.
            var candidates = new List<CardInstance>();
            for (var i = n - 1; i >= 0; i--)
            {
                if (!engine.Database.TryGet(who.Deck[i], out var def) || !IsLowLevelNormal(def, clause.SummonLevelMax))
                    continue;
                var inst = engine.CreateCardInstance(who.Deck[i]);
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck)) continue;
                PerCopyDeckRuntime.TakeFromDeck(who, i, inst);
                candidates.Insert(0, inst);
            }

            if (candidates.Count == 0)
                engine.Log($"{name}: no excavated Level {clause.SummonLevelMax} or lower Normal Monster can be Special Summoned.");
            FlipMassEffectsRuntime.RunFaceUpSummonOrGravePlan(engine, who, source, clause, candidates, () =>
            {
                engine.Rng.Shuffle(who.Deck);
                engine.Log($"{name}: {who.Name} shuffles the rest back into the Deck.");
            });
        }

        // ───────────────────────────── Monster Gate ─────────────────────────────

        /// <summary>The <see cref="EffectActionKind.ExcavateUntilNormalSummonableSpecialSummonItSendRestToGy"/> arm.</summary>
        public static void ResolveMonsterGate(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine?.Database == null || who?.Deck == null || clause == null) return;
            var name = NameOf(source, "Monster Gate");
            var idx = -1;
            for (var i = 0; i < who.Deck.Count; i++)
            {
                if (!engine.Database.TryGet(who.Deck[i], out var def) || !TributeSummonsRuntime.IsNormalSummonableDef(def))
                    continue;
                idx = i;
                break;
            }

            string NameAt(int id) => engine.Database.Get(id)?.name ?? "?";
            if (idx < 0)
            {
                engine.Log($"{name}: no monster that can be Normal Summoned/Set — {who.Name} reveals the Deck " +
                           $"({string.Join(", ", who.Deck.Select(NameAt))}) and shuffles it.");
                engine.Rng.Shuffle(who.Deck);
                engine.NotifyPublic();
                return;
            }

            engine.Log($"{name}: {who.Name} excavates {idx + 1} card(s): " +
                       $"{string.Join(", ", who.Deck.Take(idx + 1).Select(NameAt))}.");
            var inst = engine.CreateCardInstance(who.Deck[idx]);
            var canSummon = engine.FirstEmpty(who.MonsterZones) >= 0 &&
                            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck);
            if (!canSummon)
            {
                engine.Log($"{name}: {inst.Name} cannot be Special Summoned — every excavated card goes to the GY.");
                MillTop(engine, who, idx + 1);
                engine.NotifyPublic();
                return;
            }

            PerCopyDeckRuntime.TakeFromDeck(who, idx, inst);
            MillTop(engine, who, idx);
            FlipMassEffectsRuntime.RunFaceUpSummonOrGravePlan(engine, who, source, clause,
                new List<CardInstance> { inst }, null);
        }

        static void MillTop(DuelEngine engine, DuelistState who, int n)
        {
            if (n <= 0) return;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                TextEffectRuntime.SendTopOfDeckToGy(engine, who, n, byEffectOf: who);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }
        }

        // ───────────────────────────── Tribute costs (end of TextEffectRuntime.CollectTributeCost) ─────────────────────────────

        /// <summary>Ninjitsu Art of Transformation: the Tribute cost records the Level the monster had on the field.</summary>
        public static bool RecordsTributedLevel(EffectClause c) =>
            c != null && c.Action == EffectActionKind.SpecialSummonRaceLevelUpToTributedPlusFromHandOrDeckLinked;

        /// <summary>
        /// This batch's Tribute-cost candidates. Monster Gate (Lua cfilter GetMZoneCount(tp,c)&gt;0) and Ninjitsu Art (ft&gt;0
        /// or IsControler(tp)): on a full field only a monster in your own Monster Zone frees one. Ninjitsu Art: a monster
        /// with a Level whose Level + offset has a summonable match of a listed Type in the hand or Deck.
        /// </summary>
        public static void FilterTributeCostCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null) return;
            var gate = c.Action == EffectActionKind.ExcavateUntilNormalSummonableSpecialSummonItSendRestToGy;
            var ninjitsu = RecordsTributedLevel(c);
            if (!gate && !ninjitsu) return;
            // ZoneCapsAndPhaseWindows: GetMZoneCount(tp,c) — the Tribute must leave a usable zone (caps included).
            if (engine.FirstEmpty(who.MonsterZones) < 0)
                list.RemoveAll(m => m == null || !who.TryFindMonster(m, out _) ||
                                    !engine.MonsterZoneAfterLeaving(who, new List<CardInstance> { m }));
            if (!ninjitsu) return;
            list.RemoveAll(m => m == null || !m.FaceUp || m.Level <= 0 ||
                                NinjitsuCandidates(engine, who, source, c, m.Level + c.TributedLevelOffset).Count == 0);
        }

        // ───────────────────────────── Ninjitsu Art of Transformation ─────────────────────────────

        /// <summary>
        /// Hand and Deck monsters of a listed Type with a Level from 1 to <paramref name="levelCap"/> that can be Special
        /// Summoned face-up (Lua s.filter: IsCanBeSpecialSummoned(e,0,tp,false,false)). One Deck instance per card name.
        /// </summary>
        static List<CardInstance> NinjitsuCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, int levelCap)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who == null || c == null || levelCap <= 0) return list;
            bool Fits(CardDef d) =>
                d != null && d.IsMonster && !d.IsExtraDeck && d.level >= 1 && d.level <= levelCap &&
                ClassicEraTemplates.RaceMatches(d.race, c.RaceFilter);
            foreach (var h in who.Hand)
                if (h?.Def != null && Fits(h.Def) && ClassicEraTemplates.RaceMatches(h.Race, c.RaceFilter) &&
                    SpecialSummonLegality.CanBeSpecialSummoned(engine, who, h, source, SummonOrigin.Hand))
                    list.Add(h);
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || !Fits(def)) continue;
                var inst = engine.CreateCardInstance(id);
                if (SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck))
                    list.Add(inst);
            }

            return list;
        }

        /// <summary>
        /// The <see cref="EffectActionKind.SpecialSummonRaceLevelUpToTributedPlusFromHandOrDeckLinked"/> arm
        /// (<paramref name="tributedLevel"/> = the cost numeric).
        /// </summary>
        public static void ResolveNinjitsu(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            int tributedLevel)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var name = source.Name;
            if (!source.FaceUp || !who.TryFindSpellTrap(source, out _))
            {
                engine.Log($"{name} is no longer on the field — nothing is Special Summoned.");
                return;
            }

            if (tributedLevel <= 0)
            {
                engine.Log($"{name}: no Tributed Level recorded — nothing is Special Summoned.");
                return;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            var cap = tributedLevel + clause.TributedLevelOffset;
            var races = (clause.RaceFilter ?? "").Replace("|", ", ");
            if (NinjitsuCandidates(engine, who, source, clause, cap).Count == 0)
            {
                engine.Log($"{name}: no Level {cap} or lower {races} monster in the hand or Deck can be Special Summoned.");
                return;
            }

            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => NinjitsuCandidates(engine, who, source, clause, cap),
                Kind = EffectTargetKind.MonsterInHandOrDeck,
                Prompt = $"Special Summon 1 Level {cap} or lower {races} monster from your hand or Deck (face-up).",
                AiChoose = l => l.OrderByDescending(m => m.Def?.atk ?? 0).FirstOrDefault(),
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                Finish = pick =>
                {
                    if (pick == null) return;
                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : AiPosition(pick);
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    if (!source.FaceUp || !who.TryFindSpellTrap(source, out _))
                    {
                        engine.Log($"{name} is no longer on the field — nothing is Special Summoned.");
                        return;
                    }

                    if (!SummonFromHandOrDeck(engine, who, source, pick, pos, zone)) return;
                    source.EquippedTo = pick;
                    if (!pick.Equips.Contains(source)) pick.Equips.Add(source);
                    engine.Log($"{name} is linked to {pick.Name}: when {name} leaves the field, {pick.Name} is destroyed.");
                }
            });
        }

        internal static BattlePosition AiPosition(CardInstance c) =>
            c?.Def != null && c.Def.def > c.Def.atk ? BattlePosition.Defense : BattlePosition.Attack;

        /// <summary>
        /// Special Summon <paramref name="pick"/> face-up from its controller's hand or Deck (a Deck instance takes its copy
        /// and the Deck is shuffled). False when it is no longer there or the Summon is refused (the card stays).
        /// </summary>
        internal static bool SummonFromHandOrDeck(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick,
            BattlePosition pos, int zone)
        {
            var name = NameOf(source, "Effect");
            if (who.Hand.Contains(pick))
            {
                who.Hand.Remove(pick);
                engine.MarkSpecialSummonFromHand(pick); // Chain Energy toll (RuleLocks batch)
                if (engine.SpecialSummonToField(who, pick, pos, true, SummonKind.SpecialSummon, zone))
                {
                    engine.Log($"{name}: {who.Name} Special Summons {pick.Name} from the hand.");
                    return true;
                }

                who.Hand.Add(pick);
                engine.Log($"{name}: {pick.Name} could not be Special Summoned — it stays in the hand.");
                return false;
            }

            var at = who.Deck.IndexOf(pick.CardId);
            if (at < 0)
            {
                engine.Log($"{name}: {pick.Name} is no longer in the Deck — nothing is Special Summoned.");
                return false;
            }

            var copy = PerCopyDeckRuntime.TakeFromDeck(who, at, pick);
            if (!engine.SpecialSummonToField(who, pick, pos, true, SummonKind.SpecialSummon, zone))
            {
                who.Deck.Insert(at, pick.CardId, copy);
                pick.ControlOwner = null;
                engine.Log($"{name}: {pick.Name} could not be Special Summoned — it stays in the Deck.");
                return false;
            }

            engine.Log($"{name}: {who.Name} Special Summons {pick.Name} from the Deck.");
            engine.Rng.Shuffle(who.Deck);
            return true;
        }

        /// <summary>
        /// DuelEngine.UnlinkEquips: a link card whose "When this card leaves the field, destroy that monster" is its only
        /// tie (Ninjitsu Art of Transformation) is not sent anywhere when the monster leaves first; it only loses the link.
        /// </summary>
        public static bool StaysWhenLinkedMonsterLeaves(CardInstance linkCard)
        {
            if (linkCard?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(linkCard.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.SpecialSummonRaceLevelUpToTributedPlusFromHandOrDeckLinked);
        }

        // ───────────────────────────── Human-Wave Tactics ─────────────────────────────

        sealed class BattleRecord
        {
            public int Turn = -1;
            public readonly List<(DuelistState Controller, int Level)> Destroyed = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, BattleRecord> s_battles = new();

        static BattleRecord RecordOf(DuelEngine engine)
        {
            var rec = s_battles.GetValue(engine, _ => new BattleRecord());
            if (rec.Turn != engine.TurnNumber)
            {
                rec.Turn = engine.TurnNumber;
                rec.Destroyed.Clear();
            }

            return rec;
        }

        /// <summary>
        /// DuelEngine.ApplyBattleDestructions, once the battle's destructions are decided (Lua EVENT_BATTLED with
        /// STATUS_BATTLE_DESTROYED): each Normal Monster on the field that this battle is destroying is recorded with its
        /// controller and its Level as it is now, whatever happens to it next (shuffled into the Deck by Grave Protector,
        /// banished …).
        /// </summary>
        public static void NoteBattle(DuelEngine engine, CardInstance destroyedAttacker, CardInstance destroyedDefender)
        {
            if (engine == null) return;
            foreach (var m in new[] { destroyedAttacker, destroyedDefender })
            {
                if (m?.Def == null || m.IsToken || !m.Def.IsNormalMonster) continue;
                var ctrl = engine.ControllerOf(m);
                if (ctrl == null || !ctrl.TryFindMonster(m, out _)) continue;
                RecordOf(engine).Destroyed.Add((ctrl, m.Level));
            }
        }

        /// <summary>Your Level <paramref name="levelMax"/> or lower Normal Monsters destroyed by battle this turn.</summary>
        public static int BattleDestroyedThisTurn(DuelEngine engine, DuelistState who, int levelMax)
        {
            if (engine == null || who == null) return 0;
            return RecordOf(engine).Destroyed.Count(r => ReferenceEquals(r.Controller, who) &&
                                                         r.Level >= 1 && r.Level <= levelMax);
        }

        /// <summary>The <see cref="EffectActionKind.SpecialSummonLowLevelNormalsFromDeckPerOwnBattleDestroyedAtEndPhase"/> arm.</summary>
        public static void ResolveHumanWave(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine?.Database == null || who?.Deck == null || source == null || clause == null) return;
            var name = source.Name;
            if (!source.FaceUp || !who.TryFindSpellTrap(source, out _)) return;
            var ct = BattleDestroyedThisTurn(engine, who, clause.SummonLevelMax);
            if (ct <= 0) return;
            // One instance per Deck copy that can be Special Summoned (selected, not targeted).
            var pool = new List<CardInstance>();
            foreach (var id in who.Deck)
            {
                if (!engine.Database.TryGet(id, out var def) || !IsLowLevelNormal(def, clause.SummonLevelMax)) continue;
                var inst = engine.CreateCardInstance(id);
                if (SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck))
                    pool.Add(inst);
            }

            var need = Math.Min(ct, pool.Count);
            engine.Log($"{name}: {ct} of {who.Name}'s Level {clause.SummonLevelMax} or lower Normal Monster(s) were " +
                       $"destroyed by battle this turn — {need} selected from the Deck.");
            if (need == 0) return;
            var chosen = new List<CardInstance>();
            SelectNext(engine, who, source, clause, pool, chosen, need);
        }

        static void SelectNext(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            List<CardInstance> pool, List<CardInstance> chosen, int need)
        {
            List<CardInstance> Left() => pool.Where(p => !chosen.Contains(p)).ToList();
            if (chosen.Count >= need || Left().Count == 0)
            {
                FinishHumanWave(engine, who, source, clause, chosen);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Left,
                Kind = EffectTargetKind.MonsterInHandOrDeck,
                Prompt = $"select a Level {clause.SummonLevelMax} or lower Normal Monster from your Deck " +
                         $"({chosen.Count + 1} of {need}).",
                AiChoose = l => l.OrderByDescending(m => Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0)).FirstOrDefault(),
                Finish = pick =>
                {
                    if (pick != null) chosen.Add(pick);
                    else
                    {
                        // Mandatory: take the AI's choice.
                        var auto = Left().OrderByDescending(m => Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0)).FirstOrDefault();
                        if (auto == null)
                        {
                            FinishHumanWave(engine, who, source, clause, chosen);
                            return;
                        }

                        chosen.Add(auto);
                    }

                    SelectNext(engine, who, source, clause, pool, chosen, need);
                }
            });
        }

        static void FinishHumanWave(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            List<CardInstance> chosen)
        {
            var taken = new List<CardInstance>();
            foreach (var c in chosen)
            {
                var at = who.Deck.IndexOf(c.CardId);
                if (at < 0) continue;
                PerCopyDeckRuntime.TakeFromDeck(who, at, c);
                taken.Add(c);
            }

            if (taken.Count == 0) return;
            engine.Log($"{source.Name}: {who.Name} selects {string.Join(", ", taken.Select(t => t.Name))}.");
            FlipMassEffectsRuntime.RunFaceUpSummonOrGravePlan(engine, who, source, clause, taken, () =>
            {
                engine.Rng.Shuffle(who.Deck);
                engine.Log($"{source.Name}: {who.Name}'s Deck is shuffled.");
            });
        }

        // ───────────────────────────── The First Sarcophagus ─────────────────────────────

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClauseList.FirstOrDefault(c => c != null && c.Action == action);
        }

        static List<string> Names(string named) =>
            string.IsNullOrEmpty(named)
                ? new List<string>()
                : named.Split('|').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

        static IEnumerable<CardInstance> FieldCards(DuelistState side)
        {
            foreach (var m in side.MonstersOnField())
                if (m != null) yield return m;
            foreach (var s in side.SpellTrapsOnField())
                if (s != null) yield return s;
            if (side.FieldSpellZone?.Occupant != null)
                yield return side.FieldSpellZone.Occupant;
        }

        /// <summary>
        /// DestinyBoardRuntime.Place, after a named card was placed (Lua plop after the placement): when this card and every
        /// card it places are face-up on your field, send every face-up card with one of those names to the GY; only if all
        /// of them went there and a Monster Zone is free, Special Summon 1 named monster from the hand or Deck, ignoring its
        /// Summoning conditions.
        /// </summary>
        public static void AfterNamedPlacement(DuelEngine engine, DuelistState who, CardInstance board,
            EffectClause placement)
        {
            if (engine == null || who == null || board?.Def == null || placement == null) return;
            var rider = ClauseOf(board, EffectActionKind.SendThisAndPlacedToGyThenSpecialSummonNamedFromHandOrDeck);
            if (rider == null || string.IsNullOrEmpty(rider.NamedCard)) return;
            var set = new List<string> { board.Def.name };
            set.AddRange(Names(placement.NamedCard));
            if (set.Count != rider.Amount) return;
            bool FaceUpNamed(CardInstance c, string n) => c.FaceUp && (c.IsNamed(n) ||
                string.Equals(c.Def?.name, n, StringComparison.OrdinalIgnoreCase));
            foreach (var n in set)
                if (!FieldCards(who).Any(c => FaceUpNamed(c, n)))
                    return;
            var send = FieldCards(who).Where(c => set.Any(n => FaceUpNamed(c, n))).ToList();
            engine.Log($"{board.Name}: all {set.Count} cards are face-up — {who.Name} sends " +
                       $"{string.Join(", ", send.Select(c => c.Name))} to the GY.");
            foreach (var c in send)
                if (FieldCards(who).Contains(c))
                    engine.SendCardToGrave(who, c, sentBy: board);
            var allSent = send.All(c => PileOf(engine, c)?.Graveyard.Contains(c) == true);
            if (!allSent)
            {
                engine.Log($"{board.Name}: not every card was sent to the GY — {rider.NamedCard} is not Special Summoned.");
                engine.NotifyPublic();
                return;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{board.Name}: no free Monster Zone — {rider.NamedCard} is not Special Summoned.");
                engine.NotifyPublic();
                return;
            }

            var candidates = NamedSummonCandidates(engine, who, rider.NamedCard);
            if (candidates.Count == 0)
            {
                engine.Log($"{board.Name}: no {rider.NamedCard} in the hand or Deck can be Special Summoned.");
                engine.NotifyPublic();
                return;
            }

            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, board, new CardPickRequest
            {
                Candidates = () => NamedSummonCandidates(engine, who, rider.NamedCard),
                Kind = EffectTargetKind.MonsterInHandOrDeck,
                Prompt = $"Special Summon 1 \"{rider.NamedCard}\" from your hand or Deck (face-up).",
                // The hand copy first (the AI and a prompt-less answer).
                AiChoose = l => l.FirstOrDefault(c => who.Hand.Contains(c)) ?? l.FirstOrDefault(),
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                Finish = pick =>
                {
                    if (pick == null) return;
                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : BattlePosition.Attack;
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    var previous = TributeSummonsRuntime.BeginNamedSummon(board);
                    bool done;
                    try
                    {
                        done = SummonFromHandOrDeck(engine, who, board, pick, pos, zone);
                    }
                    finally
                    {
                        TributeSummonsRuntime.EndNamedSummon(previous);
                    }

                    // Lua sc:CompleteProcedure().
                    if (done) pick.ProcedureCompleted = true;
                    engine.NotifyPublic();
                }
            });
        }

        static DuelistState PileOf(DuelEngine engine, CardInstance c)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side?.Graveyard != null && side.Graveyard.Contains(c))
                    return side;
            return null;
        }

        /// <summary>
        /// The named monster in the hand or Deck (one Deck instance) that can be Special Summoned face-up while its
        /// Summoning conditions are ignored (Lua IsCanBeSpecialSummoned(e,0,tp,true,false)): only the rule locks, the
        /// face-up rules and summon costs apply.
        /// </summary>
        static List<CardInstance> NamedSummonCandidates(DuelEngine engine, DuelistState who, string named)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who == null || string.IsNullOrEmpty(named)) return list;
            bool Ok(CardInstance c, SummonOrigin from) =>
                c?.Def != null && c.Def.IsMonster && !RuleLocksRuntime.SpecialSummonLocked(engine, who, c) &&
                !SummonTriggersAndTributesRuntime.UniqueBlocksFaceUp(engine, who, c, out _) &&
                !SummonLocksAndStatRulesRuntime.BlocksFaceUpSummon(engine, who, c, out _) &&
                (from != SummonOrigin.Hand || !RuleLocksRuntime.HandSpecialSummonUnaffordable(engine, who)) &&
                SpecialSummonLegality.CanPaySummonCosts(engine, who, c, from);
            foreach (var h in who.Hand)
                if (h?.Def != null && string.Equals(h.Def.name, named, StringComparison.OrdinalIgnoreCase) &&
                    Ok(h, SummonOrigin.Hand))
                    list.Add(h);
            foreach (var id in who.Deck)
            {
                if (!engine.Database.TryGet(id, out var def) ||
                    !string.Equals(def?.name, named, StringComparison.OrdinalIgnoreCase))
                    continue;
                var inst = engine.CreateCardInstance(id);
                if (Ok(inst, SummonOrigin.Deck)) list.Add(inst);
                break;
            }

            return list;
        }

        // ───────────────────────────── "cannot be Special Summoned except by the effect of "X"" ─────────────────────────────

        static readonly Regex RxNomiExceptByEffect = new(
            @"(?:^|(?<=[.!?)]\s))This card cannot be Special Summoned except by the effect of ""([^""]+)""\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>X of "This card cannot be Special Summoned except by the effect of "X"." in <paramref name="text"/>, or null.</summary>
        public static string NomiSummonerOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = RxNomiExceptByEffect.Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// DuelEngine.SpecialSummonToField and SpecialSummonLegality: such a monster may only be Special Summoned while X's
        /// own named Special Summon is in progress (TributeSummonsRuntime.CurrentNamedSummonSource) — never by another
        /// effect, properly summoned before or not (Lua EFFECT_SPSUMMON_CONDITION with no value).
        /// </summary>
        public static bool NomiBlocksSpecialSummon(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsMonster) return false;
            var named = NomiSummonerOf(OfficialCardAuthority.OfficialText(card));
            if (named == null) return false;
            var source = TributeSummonsRuntime.CurrentNamedSummonSource;
            return source?.Def == null ||
                   !(string.Equals(source.Def.name, named, StringComparison.OrdinalIgnoreCase) || source.IsNamed(named));
        }

        /// <summary>
        /// CardEffectStatus.Classify: a monster with the "except by the effect of "X"" rule is Implemented only while X
        /// compiles in full to a summon that names it. True for every other card.
        /// </summary>
        public static bool NamedEffectSummonIsLive(CardDef def)
        {
            if (def == null || !def.IsMonster) return true;
            var named = NomiSummonerOf(OfficialCardAuthority.OfficialText(def));
            if (named == null) return true;
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            if (db == null) return false;
            foreach (var x in db.GetAllCards())
            {
                if (x == null || !string.Equals(x.name, named, StringComparison.OrdinalIgnoreCase)) continue;
                var prog = CompiledEffectCache.GetOrCompile(x);
                if (prog == null || !prog.FullyCompiled) continue;
                if (prog.ClauseList.Exists(c => c != null &&
                                                c.Action == EffectActionKind.SendThisAndPlacedToGyThenSpecialSummonNamedFromHandOrDeck &&
                                                string.Equals(c.NamedCard, def.name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        // ───────────────────────────── Spirit of the Pharaoh ─────────────────────────────

        static List<CardInstance> RevivalCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, ICollection<CardInstance> taken)
        {
            var list = new List<CardInstance>();
            if (engine == null || who?.Graveyard == null || clause == null) return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || g == source || (taken != null && taken.Contains(g))) continue;
                if (!IsLowLevelNormal(g.Def, clause.SummonLevelMax) ||
                    !ClassicEraTemplates.RaceMatches(g.Def.race, clause.RaceFilter))
                    continue;
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, g, source, SummonOrigin.Graveyard)) continue;
                list.Add(g);
            }

            return list;
        }

        sealed class RevivalPick
        {
            public CardInstance Card;
            public BattlePosition Position;
            public int Zone = -1;
        }

        /// <summary>The <see cref="EffectActionKind.SpecialSummonUpToTargetedLowLevelRaceNormalsFromGy"/> arm.</summary>
        public static void ResolvePharaohRevival(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var free = ZoneCapsRuntime.UsableMonsterZones(who); // ZoneCapsAndPhaseWindows: caps
            var max = Math.Min(Math.Max(1, clause.Amount), free);
            if (max <= 0 || RevivalCandidates(engine, who, source, clause, null).Count == 0) return;
            var picks = new List<RevivalPick>();
            NextRevivalTarget(engine, who, source, clause, picks, max);
        }

        static void NextRevivalTarget(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            List<RevivalPick> picks, int max)
        {
            var name = NameOf(source, "Effect");
            var taken = picks.Select(p => p.Card).ToList();
            if (picks.Count >= max || RevivalCandidates(engine, who, source, clause, taken).Count == 0)
            {
                FinishRevival(engine, who, source, picks);
                return;
            }

            var human = engine.IsHumanControlled(who);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => RevivalCandidates(engine, who, source, clause, taken),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = picks.Count == 0
                    ? $"you can target up to {max} Level {clause.SummonLevelMax} or lower {clause.RaceFilter} Normal " +
                      "Monster(s) in your GY to Special Summon (CANCEL = no)."
                    : $"target another monster ({picks.Count + 1} of up to {max}); CANCEL Special Summons the " +
                      $"{picks.Count} targeted.",
                AiChoose = l => l.OrderByDescending(m => Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0)).FirstOrDefault(),
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                Cancel = () =>
                {
                    if (picks.Count == 0) engine.Log($"{name}: {who.Name} does not use the effect.");
                    FinishRevival(engine, who, source, picks);
                },
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        FinishRevival(engine, who, source, picks);
                        return;
                    }

                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : AiPosition(pick);
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    engine.ClearPendingSpecialSummonPlacement();
                    picks.Add(new RevivalPick { Card = pick, Position = pos, Zone = zone });
                    NextRevivalTarget(engine, who, source, clause, picks, max);
                }
            });
        }

        static void FinishRevival(DuelEngine engine, DuelistState who, CardInstance source, List<RevivalPick> picks)
        {
            if (picks.Count == 0) return;
            var name = NameOf(source, "Effect");
            engine.Log($"{name}: {who.Name} targets {string.Join(", ", picks.Select(p => p.Card.Name))}.");
            var still = picks.Where(p => who.Graveyard.Contains(p.Card)).ToList();
            var free = ZoneCapsRuntime.UsableMonsterZones(who); // ZoneCapsAndPhaseWindows: caps
            if (free < still.Count)
            {
                engine.Log($"{name}: fewer free Monster Zones than targets — nothing is Special Summoned.");
                return;
            }

            foreach (var p in still)
            {
                if (engine.GameOver) return;
                var card = p.Card;
                if (!who.Graveyard.Contains(card)) continue;
                who.Graveyard.Remove(card);
                DuelEngine.RecordOwnerOnFieldEntry(card, who, who);
                engine.NextSpecialSummonFromGraveyard = true;
                if (engine.SpecialSummonToField(who, card, p.Position, true, SummonKind.SpecialSummon, p.Zone))
                {
                    engine.Log($"{name}: {who.Name} Special Summons {card.Name} from the GY in face-up " +
                               $"{(card.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                    continue;
                }

                engine.NextSpecialSummonFromGraveyard = false;
                card.ControlOwner = null;
                if (!who.Graveyard.Contains(card)) who.Graveyard.Add(card);
                engine.Log($"{name}: {card.Name} could not be Special Summoned — it stays in the GY.");
            }

            engine.NotifyPublic();
        }
    }

    /// <summary>DeckSummonsAndSarcophagi batch: the face-up summon-or-GY plan shared by its Deck summons.</summary>
    public static partial class FlipMassEffectsRuntime
    {
        /// <summary>
        /// <paramref name="candidates"/> (already taken out of <paramref name="who"/>'s Deck) are Special Summoned face-up
        /// (Attack or Defense, the player's choice); with fewer free Monster Zones the player chooses that many and the
        /// others go to the GY; CANCEL takes them in order in face-up Attack Position. <paramref name="after"/> runs once
        /// every Summon and send is done (the Deck shuffle).
        /// </summary>
        internal static void RunFaceUpSummonOrGravePlan(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, List<CardInstance> candidates, Action after)
        {
            if (engine == null || who == null) return;
            var plan = new Plan
            {
                Kind = PlanKind.ExcavateToGrave,
                Source = source,
                PositionChoice = true,
                FaceUpOnly = true,
                Clause = clause,
                ResumesHeldPlay = true
            };
            var seat = plan.AddSeat(who);
            if (candidates != null) seat.Candidates.AddRange(candidates.Where(c => c != null));
            if (after != null) plan.After.Add(after);
            DecideAndRun(engine, plan);
        }
    }
}
