using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ArchfiendRiders" runtime (<see cref="ArchfiendRidersAtoms"/>):
    /// <list type="bullet">
    /// <item>Terrorking Archfiend: <see cref="NormalOrFlipSummonForbidden"/> (SummonProcedures.CheckNormalOrTribute,
    /// DuelEngine.CanFlipSummon).</item>
    /// <item>Pandemonium: <see cref="StandbyLpCostWaived"/> (the Standby maintenance arms of TextEffectRuntime) and the
    /// search (<see cref="NoteHeldSend"/> / <see cref="OnSentToGy"/> from MonsterEffects.OnSentFromFieldToGy; an optional
    /// trigger on the simultaneous-trigger Chain: <see cref="TakeHeldSearches"/>, SimultaneousTriggersRuntime.QueueTrigger).</item>
    /// <item>Pandemonium Watchbear: <see cref="ProtectsSpellTrapFromEffectDestruction"/>
    /// (EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction).</item>
    /// <item>Infernalqueen Archfiend: <see cref="TryStandbyTargetBoost"/> (TextEffectRuntime.FirePhaseTriggersFor).</item>
    /// <item>Checkmate: <see cref="FilterTributeCostCandidates"/> (TextEffectRuntime.CollectTributeCost).</item>
    /// <item>Desrook Archfiend: a hand Quick Effect answering the destruction of a named monster
    /// (<see cref="CollectHandResponses"/>, <see cref="CanActivateHandEvent"/>, <see cref="TryActivate"/>,
    /// <see cref="TryResolveLink"/>, <see cref="AiWantsHandEvent"/>).</item>
    /// </list>
    /// The "Archfiend" filter is <see cref="TextEffectRuntime.CardMatchesSeries"/> (archetype, name, or the "always
    /// treated as an "Archfiend" card" text of Summoned Skull and its kin), never a card id.
    /// </summary>
    public static class ArchfiendRidersRuntime
    {
        // ───────────────────────────── shared ─────────────────────────────

        /// <summary>The compiled clauses of <paramref name="card"/> with that action, FullyCompiled programs only (a Stub's
        /// fragment never applies a rule its card does not fully print).</summary>
        static IEnumerable<EffectClause> Clauses(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action)
                    yield return c;
        }

        /// <summary>A monster card of that series (ygopro IsSetCard + IsMonster).</summary>
        public static bool IsSeriesMonster(CardDef def, string series) =>
            def != null && def.IsMonster && !string.IsNullOrEmpty(series) &&
            TextEffectRuntime.CardMatchesSeries(def, series);

        /// <summary>The face-up Field Spells in either Field Zone whose effects apply now (resolved, not negated).</summary>
        static IEnumerable<CardInstance> LiveFieldSpells(DuelEngine engine)
        {
            if (engine == null) yield break;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                var f = side?.FieldSpellZone?.Occupant;
                if (f?.Def == null || !f.FaceUp || f.IsNegated || f.ActivationUnresolvedOnChain) continue;
                yield return f;
            }
        }

        /// <summary>The player a card belongs to right now: its controller on the field, else the holder of the GY, hand or
        /// banished pile it is in.</summary>
        static DuelistState PlayerOf(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            var field = engine.ControllerOf(card);
            if (field != null) return field;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if ((side.Graveyard != null && side.Graveyard.Contains(card)) ||
                    (side.Hand != null && side.Hand.Contains(card)) ||
                    (side.Banished != null && side.Banished.Contains(card)))
                    return side;
            }

            return null;
        }

        // ───────────────────────────── Terrorking Archfiend ─────────────────────────────

        /// <summary>
        /// SummonProcedures.CheckNormalOrTribute (not a Set) and DuelEngine.CanFlipSummon: "This card cannot be Normal
        /// Summoned or Flip Summoned unless you have an Archfiend Monster Card on your side of the field." — Lua s.excon:
        /// <paramref name="who"/> controls no face-up monster of the series.
        /// </summary>
        public static bool NormalOrFlipSummonForbidden(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = null;
            if (who == null || card?.Def == null || !card.Def.IsMonster) return false;
            var c = Clauses(card, EffectActionKind.CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries)
                .FirstOrDefault();
            if (c == null) return false;
            if (who.MonstersOnField().Any(m => m != null && m != card && m.FaceUp && IsSeriesMonster(m.Def, c.NamedCard)))
                return false;
            reason = $"{card.Name} cannot be Normal Summoned or Flip Summoned unless you control a face-up " +
                     $"\"{c.NamedCard}\" monster (it can still be Set).";
            return true;
        }

        // ───────────────────────────── Pandemonium: the Standby LP waiver ─────────────────────────────

        /// <summary>
        /// The Standby Phase LP cost of <paramref name="payer"/>'s effect is not paid (Pandemonium, Lua EFFECT_LPCOST_REPLACE
        /// with s.lrcon): a live Field Spell in either Field Zone waives it for monsters of its series, for both players.
        /// </summary>
        public static bool StandbyLpCostWaived(DuelEngine engine, CardInstance payer, out CardInstance waiver)
        {
            waiver = null;
            if (engine == null || payer?.Def == null || !payer.Def.IsMonster) return false;
            foreach (var f in LiveFieldSpells(engine))
            {
                foreach (var c in Clauses(f, EffectActionKind.WaiveSeriesMonsterStandbyLpCosts))
                {
                    if (!IsSeriesMonster(payer.Def, c.NamedCard)) continue;
                    waiver = f;
                    return true;
                }
            }

            return false;
        }

        // ───────────────────────────── Pandemonium: the search ─────────────────────────────

        /// <summary>The highest Level of each player's qualifying monsters a simultaneous event destroyed (Lua s.regop), until
        /// that event's held triggers go on their Chain (<see cref="TakeHeldSearches"/>).</summary>
        sealed class SearchState
        {
            public int Turn;
            public readonly Dictionary<DuelistState, int> MaxLevel = new Dictionary<DuelistState, int>();
        }

        static readonly ConditionalWeakTable<DuelEngine, SearchState> s_search = new ConditionalWeakTable<DuelEngine, SearchState>();

        /// <summary>The series a live search clause names when <paramref name="card"/> qualifies for it: a monster of that
        /// series, with a Level, destroyed and sent to the GY, not by battle.</summary>
        static bool Qualifies(DuelEngine engine, CardInstance card, bool destroyed, bool byBattle)
        {
            if (!destroyed || byBattle || card?.Def == null || card.Def.level <= 0) return false;
            foreach (var f in LiveFieldSpells(engine))
                foreach (var c in Clauses(f, EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle))
                    if (IsSeriesMonster(card.Def, c.NamedCard))
                        return true;
            return false;
        }

        /// <summary>
        /// MonsterEffects.OnSentFromFieldToGy, when a simultaneous event (FlipMassEffectsRuntime.Simultaneously: Dark Hole,
        /// Raigeki, …) holds the send: the destroyed monster is counted now, so the event makes one search per player for
        /// the highest Level destroyed (Lua s.regop raises one event per player). The searches go on the held triggers'
        /// Chain (<see cref="TakeHeldSearches"/>, SimultaneousTriggersRuntime.RunHeld).
        /// </summary>
        public static void NoteHeldSend(DuelEngine engine, DuelistState owner, CardInstance card, bool destroyed,
            bool byBattle)
        {
            NoteDestroyed(card, owner, destroyed);
            if (engine == null || owner == null || !Qualifies(engine, card, destroyed, byBattle)) return;
            var st = s_search.GetOrCreateValue(engine);
            if (st.Turn != engine.TurnNumber)
            {
                st.MaxLevel.Clear(); // a count left from an event whose triggers never went on a Chain
                st.Turn = engine.TurnNumber;
            }

            var lv = card.Def.level;
            if (!st.MaxLevel.TryGetValue(owner, out var had) || lv > had)
                st.MaxLevel[owner] = lv;
        }

        /// <summary>
        /// SimultaneousTriggersRuntime.RunHeld: the searches the held event earned, one per player and live search clause
        /// (an optional trigger of the destroyed monsters' player, EFFECT_FLAG_EVENT_PLAYER), for the same Chain as the
        /// held "sent to the GY" triggers (SEGOC). A search opening its prompt while a held trigger resolved would be
        /// replaced by that trigger's own prompt (Sangan destroyed with an Archfiend).
        /// </summary>
        public static List<(DuelistState Who, CardInstance Card, System.Action Run)> TakeHeldSearches(DuelEngine engine)
        {
            var list = new List<(DuelistState, CardInstance, System.Action)>();
            if (engine == null || !s_search.TryGetValue(engine, out var st) || st.MaxLevel.Count == 0) return list;
            var counted = st.Turn == engine.TurnNumber ? new Dictionary<DuelistState, int>(st.MaxLevel) : null;
            st.MaxLevel.Clear();
            if (counted == null) return list;
            foreach (var who in new[] { engine.TurnPlayer, engine.OpponentOf(engine.TurnPlayer) })
            {
                if (who == null || !counted.TryGetValue(who, out var level)) continue;
                foreach (var (f, c) in SearchesOffered(engine, who, level))
                    list.Add((who, f, () => OfferSearch(engine, who, f, c, level)));
            }

            return list;
        }

        /// <summary>
        /// MonsterEffects.OnSentFromFieldToGy (a send nothing held): a qualifying destruction queues the search of each live
        /// search clause on the next simultaneous-trigger Chain (SimultaneousTriggersRuntime.QueueTrigger), with the
        /// destroyed monster's own "sent to the GY" trigger when it has one. A held send replaying was counted when it
        /// happened (<see cref="NoteHeldSend"/>): its search is already on that Chain.
        /// </summary>
        public static void OnSentToGy(DuelEngine engine, DuelistState owner, CardInstance card, bool destroyed,
            bool byBattle)
        {
            if (engine == null || owner == null || card == null) return;
            NoteDestroyed(card, owner, destroyed);
            if (SimultaneousTriggersRuntime.IsReplaying(engine, card)) return;
            if (!Qualifies(engine, card, destroyed, byBattle)) return;
            var level = card.Def.level;
            foreach (var (f, c) in SearchesOffered(engine, owner, level))
                SimultaneousTriggersRuntime.QueueTrigger(engine, owner, f, optional: true,
                    () => OfferSearch(engine, owner, f, c, level));
        }

        /// <summary>
        /// The live search clauses whose effect <paramref name="who"/> can activate for a Level <paramref name="level"/>
        /// destruction: a lower-Level series monster is in the Deck (Lua s.target chk) and Spell/Trap activations are
        /// not locked (Cold Wave).
        /// </summary>
        static List<(CardInstance Source, EffectClause Clause)> SearchesOffered(DuelEngine engine, DuelistState who,
            int level)
        {
            var list = new List<(CardInstance, EffectClause)>();
            foreach (var f in LiveFieldSpells(engine).ToList())
                foreach (var c in Clauses(f, EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle))
                {
                    if (SearchCandidates(engine, who, c.NamedCard, level).Count == 0) continue;
                    // Cold Wave or Sasuke Samurai #2 (re:IsSpellTrapEffect()): the trigger cannot be activated.
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, f)) continue;

                    list.Add((f, c));
                }

            return list;
        }

        /// <summary>One copy per distinct card in <paramref name="who"/>'s Deck: a monster of the series with a lower Level.</summary>
        static List<CardInstance> SearchCandidates(DuelEngine engine, DuelistState who, string series, int level)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!IsSeriesMonster(def, series) || def.level <= 0 || def.level >= level) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        /// <summary>
        /// The resolution of the search <paramref name="who"/> activated (<see cref="SearchesOffered"/> checked the
        /// activation; EFFECT_FLAG_EVENT_PLAYER: the player whose monster was destroyed, whoever controls the Field Spell).
        /// The player picks the card or declines (CANCEL); the AI takes the highest Level. A Deck emptied of candidates
        /// since then adds nothing (Lua s.operation).
        /// </summary>
        static void OfferSearch(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c, int level)
        {
            if (SearchCandidates(engine, who, c.NamedCard, level).Count == 0)
            {
                engine.Log($"{source.Name}: {who.Name} has no \"{c.NamedCard}\" monster lower than Level {level} in the " +
                           "Deck — nothing is added.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => SearchCandidates(engine, who, c.NamedCard, level),
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = $"{source.Name}: add 1 \"{c.NamedCard}\" monster lower than Level {level} from your Deck to " +
                         "your hand, or CANCEL.",
                AskWithOneCandidate = true,
                AiChoose = list => list.OrderByDescending(x => x.Def?.level ?? 0).FirstOrDefault(),
                Finish = chosen => AddFromDeck(engine, who, source, chosen),
                Cancel = () => engine.Log($"{who.Name} does not use {source.Name}'s effect.")
            });
        }

        static void AddFromDeck(DuelEngine engine, DuelistState who, CardInstance source, CardInstance chosen)
        {
            if (chosen?.Def == null)
            {
                engine.Log($"{who.Name} does not use {source.Name}'s effect.");
                return;
            }

            var idx = who.Deck.IndexOf(chosen.CardId);
            if (idx < 0) return;
            PerCopyDeckRuntime.TakeFromDeck(who, idx, chosen);
            who.Hand.Add(chosen);
            engine.Rng.Shuffle(who.Deck);
            engine.Log($"{source.Name}: {who.Name} adds {chosen.Name} (Lv{chosen.Def.level}) from the Deck to the hand.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Pandemonium Watchbear ─────────────────────────────

        /// <summary>
        /// EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction: a face-up Spell/Trap named by a live
        /// face-up monster its controller controls cannot be destroyed by the opponent's card effects (Lua
        /// EFFECT_INDESTRUCTABLE_EFFECT, s.indval: the effect's player is not the protector's controller).
        /// </summary>
        public static bool ProtectsSpellTrapFromEffectDestruction(DuelEngine engine, CardInstance card,
            CardInstance source)
        {
            if (engine == null || card?.Def == null || card.Def.IsMonster || !card.FaceUp) return false;
            var holder = engine.ControllerOf(card);
            var destroyer = PlayerOf(engine, source);
            if (holder == null || destroyer == null || destroyer == holder) return false;
            foreach (var m in holder.MonstersOnField())
            {
                if (m == null || !m.FaceUp || m.IsNegated) continue;
                foreach (var c in Clauses(m, EffectActionKind.NamedSpellTrapCannotBeDestroyedByOpponentEffects))
                {
                    if (string.IsNullOrEmpty(c.NamedCard) || !card.IsNamed(c.NamedCard)) continue;
                    engine.Log($"{m.Name}: {card.Name} is not destroyed by the opponent's card effect.");
                    return true;
                }
            }

            return false;
        }

        // ───────────────────────────── Infernalqueen Archfiend ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor, each Standby Phase: a face-up monster's targeted Standby boost
        /// (<see cref="EffectActionKind.GainAtkDefUntilEndOfTurn"/> + RequiresTargetChoice). Its controller targets 1
        /// face-up monster of the series they control (mandatory; the AI and a lone target answer at once); it gains
        /// Amount ATK until the End Phase. Nothing happens without a target. True when the clause is this shape.
        /// </summary>
        public static bool TryStandbyTargetBoost(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (c == null || c.Timing != EffectTiming.StandbyPhase || !c.RequiresTargetChoice ||
                c.Action != EffectActionKind.GainAtkDefUntilEndOfTurn || string.IsNullOrEmpty(c.TargetSeriesName))
                return false;
            if (engine == null || who == null || card?.Def == null) return true;
            if (!card.FaceUp || !who.TryFindMonster(card, out _)) return true;
            if (!Clauses(card, EffectActionKind.GainAtkDefUntilEndOfTurn).Contains(c)) return true;

            List<CardInstance> Targets() => who.MonstersOnField()
                .Where(m => m != null && m.FaceUp && IsSeriesMonster(m.Def, c.TargetSeriesName))
                .ToList();

            if (Targets().Count == 0)
            {
                engine.Log($"{card.Name}: no face-up \"{c.TargetSeriesName}\" monster to target.");
                return true;
            }

            engine.Log($"{card.Name}: Standby Phase effect.");
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = Targets,
                Kind = EffectTargetKind.YourMonster,
                Prompt = $"{card.Name}: target 1 face-up \"{c.TargetSeriesName}\" monster you control; it gains " +
                         $"{c.Amount} ATK until the End Phase.",
                AiChoose = list => list.OrderByDescending(m => m.CurrentAtk).FirstOrDefault(),
                HoldsChain = false,
                HoldsStandby = engine.Phase == DuelPhase.Standby,
                Finish = target =>
                {
                    if (target == null || !target.FaceUp || !who.TryFindMonster(target, out _)) return;
                    target.UntilEndOfTurnAtk += c.Amount;
                    engine.Log($"{card.Name}: {target.Name} gains {c.Amount} ATK until the End Phase " +
                               $"({target.CurrentAtk}).");
                    engine.NotifyPublic();
                }
            });
            return true;
        }

        // ───────────────────────────── Checkmate ─────────────────────────────

        /// <summary>
        /// The end of TextEffectRuntime.CollectTributeCost, for a clause with <see cref="EffectClause.TributeSeriesName"/>:
        /// only monsters of that series, and none whose Tribute would leave no legal target (Lua s.rfilter:
        /// IsExistingTarget(s.filter, tp, LOCATION_MZONE, 0, 1, c)).
        /// </summary>
        public static void FilterTributeCostCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || string.IsNullOrEmpty(c.TributeSeriesName)) return;
            list.RemoveAll(m => m?.Def == null || !IsSeriesMonster(m.Def, c.TributeSeriesName));
            if (engine == null || who == null || !c.RequiresTargetChoice) return;
            var targets = TextEffectRuntime.CollectTargets(engine, who, c, source);
            list.RemoveAll(m => !targets.Any(t => t != null && t != m));
        }

        // ───────────────────────────── Desrook Archfiend ─────────────────────────────

        /// <summary><see cref="ChainLink.EffectKey"/> of a hand Quick Effect that answers a named monster's destruction.</summary>
        public const string HandEventLinkKey = "ArchfiendRiders.HandEventQuick";

        /// <summary>The player who controlled the monster when it was destroyed and sent from the field to the GY (Lua
        /// IsPreviousControler + IsReason(REASON_DESTROY)); dropped when the card is sent again without destruction.</summary>
        sealed class DestroyedFrom
        {
            public DuelistState Controller;
        }

        static readonly ConditionalWeakTable<CardInstance, DestroyedFrom> s_destroyed =
            new ConditionalWeakTable<CardInstance, DestroyedFrom>();

        static void NoteDestroyed(CardInstance card, DuelistState controller, bool destroyed)
        {
            if (card == null) return;
            s_destroyed.Remove(card);
            if (destroyed && controller != null)
                s_destroyed.Add(card, new DestroyedFrom { Controller = controller });
        }

        static EffectClause HandEventClause(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled
                ? prog.ClausesFor(EffectTiming.HandQuickEffectWhenNamedDestroyed).FirstOrDefault(c => c != null)
                : null;

        /// <summary><paramref name="card"/> is a monster in <paramref name="who"/>'s hand with this kind of Quick Effect.</summary>
        public static bool IsHandEventCard(DuelistState who, CardInstance card, bool fromHand, CompiledCardProgram prog) =>
            fromHand && who?.Hand != null && card?.Def != null && card.Def.IsMonster && who.Hand.Contains(card) &&
            HandEventClause(prog) != null;

        static bool InAnyGraveyard(DuelEngine engine, CardInstance card) =>
            (engine.Player?.Graveyard?.Contains(card) ?? false) || (engine.Opponent?.Graveyard?.Contains(card) ?? false);

        /// <summary>
        /// The legal targets now: monsters named NamedCard that <paramref name="who"/>'s open window answers (sent from the
        /// field to the GY: <see cref="BattleWindowsRuntime.AnsweredSends"/>), that <paramref name="who"/> controlled when
        /// they were destroyed, still in a GY and able to be Special Summoned from it (Lua s.spfilter).
        /// </summary>
        static List<CardInstance> HandEventTargets(DuelEngine engine, DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || c == null || string.IsNullOrEmpty(c.NamedCard)) return list;
            foreach (var t in BattleWindowsRuntime.AnsweredSends(engine, who))
            {
                if (t?.Def == null || !t.IsNamed(c.NamedCard) || !InAnyGraveyard(engine, t)) continue;
                if (!s_destroyed.TryGetValue(t, out var d) || d.Controller != who) continue;
                if (!TextEffectRuntime.CanSpecialSummonFromGyTarget(t) ||
                    !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, t, null, SummonOrigin.Graveyard))
                    continue;
                list.Add(t);
            }

            return list;
        }

        /// <summary>
        /// TextEffectRuntime.CanActivate: the Quick Effect can be activated from the hand now — only in
        /// <paramref name="who"/>'s window right after the named monster's destruction (the idle "monster sent to your GY"
        /// window, or the end of the Damage Step: EFFECT_FLAG_DAMAGE_STEP), with a free Monster Zone and a legal target.
        /// </summary>
        public static bool CanActivateHandEvent(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            reason = "Not this kind of Quick Effect.";
            var c = HandEventClause(prog);
            if (engine == null || engine.GameOver || c == null || !IsHandEventCard(who, card, true, prog)) return false;
            // LocksRedirectsAndActivity batch: Goblin of Greed — no discard cost.
            if (LocksRedirectsAndActivityRuntime.DiscardCostLocked(engine, who, c, out var discardLock))
            {
                reason = discardLock;
                return false;
            }
            if (engine.PendingActivation != null)
            {
                reason = "Finish the current choice first.";
                return false;
            }

            var pr = engine.PendingResponse;
            if (pr == null || pr.Responder != who ||
                (pr.Timing != ResponseTiming.MonsterSentFromFieldToYourGy && pr.Timing != ResponseTiming.EndOfDamageStep))
            {
                reason = $"Only right after a \"{c.NamedCard}\" you control is destroyed and sent to the GY.";
                return false;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                reason = "No free Monster Zone.";
                return false;
            }

            if (HandEventTargets(engine, who, c).Count == 0)
            {
                reason = $"No destroyed \"{c.NamedCard}\" to Special Summon.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>SpellTrapEffects.CollectLegalResponseCards: the hand monsters whose Quick Effect answers this window.</summary>
        public static void CollectHandResponses(DuelEngine engine, DuelistState who, ResponseTiming timing,
            List<CardInstance> list)
        {
            if (engine == null || who?.Hand == null || list == null) return;
            if (timing != ResponseTiming.MonsterSentFromFieldToYourGy && timing != ResponseTiming.EndOfDamageStep) return;
            foreach (var h in who.Hand.ToList())
            {
                if (h?.Def == null || list.Contains(h)) continue;
                var prog = CompiledEffectCache.GetOrCompile(h.Def);
                if (IsHandEventCard(who, h, true, prog) && CanActivateHandEvent(engine, who, h, prog, out _))
                    list.Add(h);
            }
        }

        /// <summary>SpellTrapEffects.AiAutoRespond: the AI always brings its destroyed monster back.</summary>
        public static bool AiWantsHandEvent(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return IsHandEventCard(ai, card, true, prog) && CanActivateHandEvent(engine, ai, card, prog, out _);
        }

        /// <summary>
        /// SpellTrapEffects.BeginOrResolveManual hook. False for anything else. For this Quick Effect: its Spell Speed 2
        /// Chain Link (the link DuelEngine.TryActivateSpellTrap opened for it, or a new one), the cost (this card from the
        /// hand to the GY, Lua Cost.SelfToGrave: a send, not a discard), then the target (a lone one, or the player's pick
        /// among simultaneous ones), then the opponent may respond; the Special Summon happens when the link resolves.
        /// </summary>
        public static bool TryActivate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick, out bool ok)
        {
            ok = false;
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsHandEventCard(who, card, fromHand, prog)) return false;
            var link = engine.Chain.Last;
            var ownManualLink = link != null && link.Card == card && !link.Resolved;
            if (!CanActivateHandEvent(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                if (ownManualLink) engine.Chain.RemoveUnresolved(link);
                return true;
            }

            var clause = HandEventClause(prog);
            var timing = engine.PendingResponse.Timing;
            var targets = HandEventTargets(engine, who, clause);
            if (!ownManualLink)
            {
                link = engine.Chain.AddLink(who, card, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.Hand,
                    fromHand: true, wasSet: false, effectKey: HandEventLinkKey);
                if (link == null)
                {
                    engine.Log($"{card.Name}: its effect cannot be activated now.");
                    return true;
                }
            }

            link.EffectKey = HandEventLinkKey;
            link.Speed = SpellSpeed.Speed2;
            link.Class = EffectClass.Quick;
            link.FromLocation = CardLocation.Hand;
            link.FromHand = true;
            link.WasSetOnField = false;

            if (MonsterEffects.SendFromHandToGraveByInstance(who, card) == null)
            {
                engine.Chain.RemoveUnresolved(link);
                engine.Log($"{card.Name}: it could not be sent to the GY.");
                return true;
            }

            engine.Log($"Cost: send {card.Name} from the hand to the GY.");
            ok = true;

            void Seat(CardInstance target)
            {
                link.Target = target;
                engine.Log($"Activate: {card.Name} (Quick Effect) targeting {target?.Name ?? "nothing"} — Chain Link " +
                           $"{link.LinkNumber}.");
                TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, timing);
            }

            if (targets.Count == 1 || autoPick || !engine.IsHumanControlled(who))
            {
                Seat(targets.OrderByDescending(t => t.Def?.atk ?? 0).First());
                return true;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => targets.Where(t => InAnyGraveyard(engine, t)).ToList(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{card.Name}: target the \"{clause.NamedCard}\" to Special Summon.",
                AiChoose = list => list.OrderByDescending(t => t.Def?.atk ?? 0).FirstOrDefault(),
                DuringActivation = true,
                Finish = Seat
            });
            return true;
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack hook: the link Special Summons its target if it is still in the GY and a Monster
        /// Zone is free (Lua s.spop); negated: nothing. POS_FACEUP: a human chooses face-up Attack or Defense Position
        /// (the pick holds the Chain); the AI takes Attack unless the target's DEF is higher.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || link.EffectKey != HandEventLinkKey) return false;
            var name = link.Card?.Name ?? "Effect";
            if (link.Negated)
            {
                engine.Log($"{name}: its effect is negated.");
                return true;
            }

            var who = link.Controller;
            var source = link.Card;
            var target = link.Target;
            if (!CanSummonFromGy(engine, who, source, target, name, out _)) return true;
            var aiPos = (target.Def?.atk ?? 0) >= (target.Def?.def ?? 0) ? BattlePosition.Attack : BattlePosition.Defense;
            if (!engine.IsHumanControlled(who))
            {
                SummonFromGy(engine, who, source, target, name, aiPos);
                return true;
            }

            var positions = new List<CardInstance>
            {
                PositionOption("Face-up Attack Position", 0),
                PositionOption("Face-up Defense Position", 1)
            };
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => positions,
                Kind = EffectTargetKind.None,
                Prompt = $"{name}: Special Summon {target.Name} — choose its position.",
                AiChoose = list => list.ElementAtOrDefault(aiPos == BattlePosition.Defense ? 1 : 0),
                Finish = chosen => SummonFromGy(engine, who, source, target, name,
                    chosen?.Def?.level == 1 ? BattlePosition.Defense : BattlePosition.Attack)
            });
            return true;
        }

        static CardInstance PositionOption(string label, int value) => new CardInstance
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = label,
                type = CallsDiceAndSetTriggersRuntime.OptionTokenKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = value,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        /// <summary>The target is still in a GY, <paramref name="who"/> has a free Monster Zone, it can be Special Summoned
        /// and nothing locks the GY (Necrovalley); otherwise the reason is logged.</summary>
        static bool CanSummonFromGy(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target,
            string name, out DuelistState gyOwner)
        {
            gyOwner = null;
            if (target != null && engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(target))
                gyOwner = engine.Player;
            else if (target != null && engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(target))
                gyOwner = engine.Opponent;
            if (who == null || gyOwner == null)
            {
                engine.Log($"{name}: the target is no longer in the GY — nothing is Special Summoned.");
                return false;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0 ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, target, null, SummonOrigin.Graveyard))
            {
                engine.Log($"{name}: {target.Name} cannot be Special Summoned.");
                return false;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, target))
            {
                engine.Log("Necrovalley prevents the target from leaving the GY.");
                return false;
            }

            return true;
        }

        static void SummonFromGy(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target,
            string name, BattlePosition pos)
        {
            if (!CanSummonFromGy(engine, who, source, target, name, out var gyOwner)) return;
            gyOwner.Graveyard.Remove(target);
            DuelEngine.RecordOwnerOnFieldEntry(target, gyOwner, who);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(who, target, pos, true))
            {
                engine.Log($"{name}: {who.Name} Special Summons {target.Name} from the GY in face-up " +
                           $"{(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                return;
            }

            engine.NextSpecialSummonFromGraveyard = false;
            target.ControlOwner = null;
            if (!gyOwner.Graveyard.Contains(target)) gyOwner.Graveyard.Add(target);
            engine.Log($"{name}: {target.Name} cannot be Special Summoned — it stays in the GY.");
        }
    }
}
