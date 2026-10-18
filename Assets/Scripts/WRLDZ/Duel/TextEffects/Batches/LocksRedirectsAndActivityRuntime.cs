using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LocksRedirectsAndActivity batch. One helper per concept, asked by every site of that concept:
    /// <list type="bullet">
    /// <item><b>Discard-cost lock</b> (Goblin of Greed, ygopro EFFECT_CANNOT_DISCARD_HAND; action
    /// <see cref="EffectActionKind.ContinuousCannotDiscardAsCost"/>, Side = whose costs):
    /// <see cref="DiscardCostLocked(DuelEngine, DuelistState, EffectClause, out string)"/> — every activation-legality site
    /// asks it for the clause it would activate (Spell/Trap activation, monster ignitions and their structured costs, chain
    /// answers, free-chain cards on a Chain, response windows, hand and face-up Quick Effects, Counter Traps, Kuriboh's
    /// damage-calculation discard, the registered Abyss Soldier ignition). A "send from your hand to the GY" cost
    /// (<see cref="EffectClause.DiscardCostIsSend"/>, Spell Reproduction's Spell send) is not a discard, and discards made
    /// by an effect's resolution are not costs: neither is locked.</item>
    /// <item><b>Leave-field redirect funnel</b> (Dark Magician of Chaos, EFFECT_LEAVE_FIELD_REDIRECT):
    /// <see cref="LeavesFieldBanishedInstead"/> — asked by every monster field exit in DuelEngine (DestroyMonster,
    /// SendCardToGrave, ReturnCardToHand, ReturnCardToDeck; BanishCard already banishes). Grave Protector's battle GY
    /// redirect (<see cref="ShufflesBattleDestroyedIntoDeck"/>) sits after it, after Lesser Fiend's battle-destroy redirect
    /// and Banisher of the Light, in DestroyMonster.</item>
    /// <item><b>Attack activity record</b> (ygopro ACTIVITY_ATTACK custom counter): <see cref="NoteAttackDeclared"/> from
    /// DuelEngine.DeclareAttack records, per player and turn, the names of the monsters whose attacks were declared;
    /// <see cref="DeclaredAttackWithNamedThisTurn"/> reads it (Burst Stream of Destruction; Inferno Fire Blast can reuse
    /// it). The oath (<see cref="ApplyNamedAttackOath"/>) is read by DuelEngine.ContinuousCannotAttackBlocks.</item>
    /// <item><b>EVENT_BATTLED triggers after the FLIP</b> (Dark Magician of Chaos's battle banish):
    /// <see cref="QueueBattledTrigger"/> / <see cref="ResolveQueuedBattledTriggers"/>. The FLIP effect of a monster flipped
    /// by the battle resolves first (it is Chain Link 2 in ocgcore), then the queued trigger, on its battle target if that
    /// is still on the field (IsRelateToBattle).</item>
    /// <item><b>Player-wide activation lock</b> (<see cref="ActivationLocked"/>): a partial reader kept from the 46269f39d
    /// draft — only the Spell/Trap and response hub asks it, so no atom may emit its actions
    /// (<see cref="EffectVocabulary.HasNoRuntime"/>; Chaos Emperor Dragon - Envoy of the End stays fail-closed). The
    /// draft's ACTIVITY_CHAIN counter (it counted CanActivate legality queries, not activations) and its return-to-hand
    /// leave-field redirect (Dark Magician of Chaos banishes) were removed.</item>
    /// </list>
    /// </summary>
    public static class LocksRedirectsAndActivityRuntime
    {
        /// <summary>
        /// ActivationCondition: no monster named <see cref="EffectClause.NamedCannotAttackTurnActivated"/> declared an attack
        /// for the activating player this turn (Burst Stream of Destruction, Lua GetCustomActivityCount(id,tp,ACTIVITY_ATTACK)==0).
        /// </summary>
        public const string NoNamedAttackThisTurn = "LraNoNamedAttackThisTurn";

        sealed class AttackRecord
        {
            public DuelistState Who;
            public int Turn;
            public string Name;
            public string RulesName;
        }

        sealed class NamedAttackOath
        {
            public DuelistState Who;
            public int Turn;
            public string Name;
            public CardInstance Source;
        }

        sealed class NameUse
        {
            public DuelistState Who;
            public int Turn;
            public string Name;
        }

        sealed class State
        {
            public readonly List<AttackRecord> Attacks = new();
            public readonly List<NamedAttackOath> Oaths = new();
            public readonly List<NameUse> NameUses = new();
        }

        /// <summary>A Normal or Special Summon of a card: the turn and the field presence (aux.sumreg flag).</summary>
        sealed class SummonFlag
        {
            public int Turn;
            public int FieldEntry;
            public int FaceUp;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<CardInstance, SummonFlag> s_summoned = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static bool HasClause(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            return prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static IEnumerable<CardInstance> FieldMonsters(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) yield break;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var m in side.MonstersOnField())
                if (m != null)
                    yield return m;
        }

        /// <summary>A face-up monster whose continuous effect applies now (Summon done, not negated).</summary>
        static bool AppliesNow(DuelEngine engine, CardInstance m) =>
            m != null && m.FaceUp && !m.IsNegated && !engine.IsSummonPending(m);

        // ───────────────────────────── discard-cost lock (Goblin of Greed) ─────────────────────────────

        /// <summary>
        /// The live <see cref="EffectActionKind.ContinuousCannotDiscardAsCost"/> source (face-up, un-negated, Summon done)
        /// whose Side covers <paramref name="who"/>'s costs, or null. Goblin of Greed is Side = Both.
        /// </summary>
        public static CardInstance DiscardLockSource(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return null;
            foreach (var src in LockSources(engine))
            foreach (var c in ClausesOn(src, EffectActionKind.ContinuousCannotDiscardAsCost))
                if (ConditionMet(engine, c) && CoversSide(c.Side, engine, src, who))
                    return src;
            return null;
        }

        /// <summary><paramref name="who"/> cannot discard from the hand as a cost now (a live lock source covers them).</summary>
        public static bool DiscardAsCostLocked(DuelEngine engine, DuelistState who) => DiscardLockSource(engine, who) != null;

        /// <summary>
        /// <paramref name="c"/>'s cost discards from the hand: "discard N card(s)" (<see cref="EffectClause.RequiresDiscardCost"/>
        /// unless it is a send, <see cref="EffectClause.DiscardCostCount"/> unless it sends Spells), "discard this card"
        /// (<see cref="EffectClause.RequiresDiscardSelf"/>), "discard your entire hand" (<see cref="EffectClause.DiscardEntireHandCost"/>,
        /// Rope of Life), or Kuriboh's "You can discard this card".
        /// </summary>
        public static bool DiscardsAsCost(EffectClause c) =>
            c != null &&
            ((c.RequiresDiscardCost && !c.DiscardCostIsSend) ||
             (c.DiscardCostCount > 0 && !c.RequiresDiscardCost && !DcrSpellTrapUtilityRuntime.IsSpellSendCost(c)) ||
             c.RequiresDiscardSelf || c.DiscardEntireHandCost ||
             c.Action == EffectActionKind.DiscardSelfNoBattleDamageThisBattle) &&
            !PrintedAsSendFromHand(c);

        static readonly System.Text.RegularExpressions.Regex RxSendFromHandToGy = new(
            @"\bsend\b[^.;]*?\bfrom\s+(?:your|the)\s+hand\s+to\s+the\s+(?:GY|Graveyard)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// The clause's own sentence prints its hand cost as a "send … from your hand to the GY" (a send, not a discard:
        /// EFFECT_CANNOT_DISCARD_HAND does not forbid it), for a send-cost compile that did not set
        /// <see cref="EffectClause.DiscardCostIsSend"/>. A positive match only: a "Discard 1 card." sentence merged into the
        /// next effect sentence (Spell Purification, Malice Dispersion) leaves only that effect sentence in
        /// <see cref="EffectClause.SourceSnippet"/>, and keeps its structural answer (a discard).
        /// </summary>
        static bool PrintedAsSendFromHand(EffectClause c) =>
            !string.IsNullOrWhiteSpace(c.SourceSnippet) && RxSendFromHandToGy.IsMatch(c.SourceSnippet);

        /// <summary><paramref name="who"/>'s discard cost cannot be paid now (no clause needed: Kuriboh, Abyss Soldier).</summary>
        public static bool DiscardCostLocked(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = null;
            var src = DiscardLockSource(engine, who);
            if (src == null) return false;
            reason = $"{src.Name}: neither player can activate an effect by discarding.";
            return true;
        }

        /// <summary>
        /// The one check every activation-legality site asks: <paramref name="c"/>, activated by <paramref name="who"/>,
        /// discards as its cost while a discard-cost lock covers them.
        /// </summary>
        public static bool DiscardCostLocked(DuelEngine engine, DuelistState who, EffectClause c, out string reason)
        {
            reason = null;
            return DiscardsAsCost(c) && DiscardCostLocked(engine, who, out reason);
        }

        /// <summary><see cref="DiscardCostLocked(DuelEngine, DuelistState, EffectClause, out string)"/> for any of <paramref name="clauses"/>.</summary>
        public static bool DiscardCostLocked(DuelEngine engine, DuelistState who, IEnumerable<EffectClause> clauses,
            out string reason)
        {
            reason = null;
            if (clauses == null) return false;
            foreach (var c in clauses)
                if (DiscardCostLocked(engine, who, c, out reason))
                    return true;
            return false;
        }

        // ───────────────────────────── leave-field redirect funnel (Dark Magician of Chaos) ─────────────────────────────

        /// <summary>
        /// <paramref name="card"/>, a face-up monster about to leave the field for the GY, the hand or the Deck, is banished
        /// instead (EFFECT_LEAVE_FIELD_REDIRECT, condition IsFaceup, CANNOT_DISABLE: applies while negated). Read while it
        /// is still in its Monster Zone. A monster whose Summon is still in its negation window has no effect yet.
        /// </summary>
        public static bool LeavesFieldBanishedInstead(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || card.IsToken || !card.FaceUp) return false;
            var ctrl = engine.ControllerOf(card);
            if (ctrl == null || !ctrl.TryFindMonster(card, out _)) return false;
            if (engine.IsSummonPending(card)) return false;
            return HasClause(card, EffectActionKind.BanishThisInsteadOfLeavingField);
        }

        /// <summary>
        /// DuelEngine.ReturnCardToHand / ReturnCardToDeck: banish <paramref name="card"/> instead when
        /// <see cref="LeavesFieldBanishedInstead"/>. True when it was banished (the caller stops: the card did not return).
        /// </summary>
        public static bool TryBanishInsteadOfLeaving(DuelEngine engine, CardInstance card, DuelistState by)
        {
            if (!LeavesFieldBanishedInstead(engine, card)) return false;
            var ctrl = engine.ControllerOf(card);
            engine.Log($"{card.Name} would leave the field: it is banished instead.");
            engine.BanishCard(ctrl, card, destroyed: false, by: by);
            return true;
        }

        // ───────────────────────────── battle GY redirect (Grave Protector) ─────────────────────────────

        /// <summary>
        /// <paramref name="card"/>, destroyed by battle, is shuffled into the Deck instead of going to the GY: a face-up,
        /// un-negated Grave Protector is on either field and is not itself destroyed by the battle being applied (a monster
        /// destroyed by battle has no continuous effect while its destruction applies: STATUS_BATTLE_DESTROYED). A Token
        /// cannot go to the Deck (it leaves play as usual).
        /// </summary>
        public static bool ShufflesBattleDestroyedIntoDeck(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || card.IsToken) return false;
            foreach (var m in FieldMonsters(engine))
                if (AppliesNow(engine, m) && !RedirectAndReplaceRuntime.DestroyedByCurrentBattle(engine, m) &&
                    HasClause(m, EffectActionKind.BattleDestroyedMonstersShuffleIntoDeckInstead))
                    return true;
            return false;
        }

        /// <summary>
        /// DuelEngine.DestroyMonster: put <paramref name="card"/> (already off the field) into <paramref name="pile"/>'s Deck
        /// and shuffle it (LOCATION_DECKSHF), or into the Extra Deck. It never reaches the GY: no "sent to the GY" event; it
        /// is a "returned from the field to the Deck" event (Serpentine Princess, EVENT_TO_DECK).
        /// </summary>
        public static void ShuffleIntoDeckInstead(DuelEngine engine, DuelistState pile, CardInstance card, bool wasFaceUp)
        {
            if (engine == null || pile == null || card == null) return;
            card.FaceUp = true;
            if (card.Def != null && card.Def.IsExtraDeck)
            {
                pile.ExtraDeck.Add(card.CardId);
                engine.Log($"Destroyed: {card.Name} (returned to the Extra Deck instead of the GY).");
            }
            else
            {
                pile.Deck.Add(card.CardId);
                engine.Rng.Shuffle(pile.Deck);
                engine.Log($"Destroyed: {card.Name} (shuffled into {pile.Name}'s Deck instead of the GY).");
            }

            DeckReturnsAndMassEffectsRuntime.NoteReturnedToDeck(engine, pile, card, wasFaceUp);
        }

        // ───────────────────────────── attack activity record + named attack oath (Burst Stream) ─────────────────────────────

        /// <summary>DuelEngine.DeclareAttack: <paramref name="who"/> declared an attack with <paramref name="attacker"/>.</summary>
        public static void NoteAttackDeclared(DuelEngine engine, DuelistState who, CardInstance attacker)
        {
            if (engine == null || who == null || attacker == null) return;
            var st = StateOf(engine);
            st.Attacks.RemoveAll(r => r.Turn != engine.TurnNumber);
            st.Attacks.Add(new AttackRecord
            {
                Who = who, Turn = engine.TurnNumber, Name = attacker.Name, RulesName = attacker.RulesName
            });
        }

        static bool SameName(string a, string b) =>
            !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary><paramref name="who"/> declared an attack with a monster named <paramref name="name"/> this turn.</summary>
        public static bool DeclaredAttackWithNamedThisTurn(DuelEngine engine, DuelistState who, string name) =>
            engine != null && who != null && !string.IsNullOrEmpty(name) && s_state.TryGetValue(engine, out var st) &&
            st.Attacks.Exists(r => ReferenceEquals(r.Who, who) && r.Turn == engine.TurnNumber &&
                                   (SameName(r.Name, name) || SameName(r.RulesName, name)));

        /// <summary>
        /// The activation's oath (EFFECT_CANNOT_ATTACK_ANNOUNCE, EFFECT_FLAG_OATH, both Monster Zones, reset at the End
        /// Phase): monsters named <paramref name="name"/> cannot declare an attack for the rest of this turn.
        /// </summary>
        public static void ApplyNamedAttackOath(DuelEngine engine, DuelistState who, string name, CardInstance source)
        {
            if (engine == null || who == null || string.IsNullOrEmpty(name)) return;
            var st = StateOf(engine);
            st.Oaths.RemoveAll(o => o.Turn != engine.TurnNumber);
            if (st.Oaths.Exists(o => ReferenceEquals(o.Who, who) && SameName(o.Name, name))) return;
            st.Oaths.Add(new NamedAttackOath { Who = who, Turn = engine.TurnNumber, Name = name, Source = source });
            engine.Log($"{source?.Name ?? "Oath"}: \"{name}\" cannot attack this turn.");
        }

        /// <summary>DuelEngine.ContinuousCannotAttackBlocks: an oath this turn forbids <paramref name="attacker"/>'s attack.</summary>
        public static bool AttackForbidden(DuelEngine engine, DuelistState controller, CardInstance attacker)
        {
            if (engine == null || attacker == null || !s_state.TryGetValue(engine, out var st)) return false;
            foreach (var o in st.Oaths)
                if (o.Turn == engine.TurnNumber && attacker.IsNamed(o.Name))
                    return true;
            return false;
        }

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            switch (c.ActivationCondition)
            {
                case NoNamedAttackThisTurn:
                {
                    var name = !string.IsNullOrEmpty(c.NamedCannotAttackTurnActivated)
                        ? c.NamedCannotAttackTurnActivated
                        : c.RequiresFaceUpName;
                    ok = !DeclaredAttackWithNamedThisTurn(engine, who, name);
                    reason = $"\"{name}\" you control already declared an attack this turn.";
                    return true;
                }
                default:
                    return false;
            }
        }

        // ───────────────────────────── Dark Magician of Chaos: End Phase GY Spell ─────────────────────────────

        /// <summary>DuelEngine: <paramref name="card"/> was Normal (incl. Tribute) or Special Summoned (aux.sumreg).</summary>
        public static void NoteNormalOrSpecialSummoned(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            s_summoned.Remove(card);
            s_summoned.Add(card, new SummonFlag
            {
                Turn = engine.TurnNumber, FieldEntry = card.FieldEntryStamp, FaceUp = card.FaceUpStamp
            });
        }

        /// <summary>
        /// The summon flag is still on the card: Normal or Special Summoned this turn, still the same face-up field presence
        /// (RESETS_STANDARD: leaving the field or being turned face-down resets it), and not spent by the cost.
        /// </summary>
        static bool SummonFlagLive(DuelEngine engine, CardInstance card) =>
            s_summoned.TryGetValue(card, out var f) && f.Turn == engine.TurnNumber &&
            f.FieldEntry == card.FieldEntryStamp && f.FaceUp == card.FaceUpStamp;

        static bool NameUsedThisTurn(DuelEngine engine, DuelistState who, string name) =>
            s_state.TryGetValue(engine, out var st) &&
            st.NameUses.Exists(u => ReferenceEquals(u.Who, who) && u.Turn == engine.TurnNumber && SameName(u.Name, name));

        static void MarkNameUsed(DuelEngine engine, DuelistState who, string name)
        {
            var st = StateOf(engine);
            st.NameUses.RemoveAll(u => u.Turn != engine.TurnNumber);
            st.NameUses.Add(new NameUse { Who = who, Turn = engine.TurnNumber, Name = name });
        }

        static List<CardInstance> GySpellTargets(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (who?.Graveyard == null) return list;
            foreach (var g in who.Graveyard)
                if (g?.Def != null && g.Def.IsSpell && !ContinuousProtections.GyMovementLocked(engine, source, g))
                    list.Add(g);
            return list;
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor (either player's End Phase): Dark Magician of Chaos's optional trigger
        /// (<see cref="EffectActionKind.AddGySpellToHandAtEndPhaseIfSummonedThisTurn"/>). Its controller may activate it
        /// while it is face-up in their Monster Zone, un-negated, with its summon flag, the name unused this turn and a Spell
        /// in their GY it can target; the cost spends the flag, the name's once per turn is used, and the targeted Spell
        /// still in the GY is added to the hand. A human picks the target (CANCEL = do not activate); the AI takes the
        /// Spell it values most. False for any other clause.
        /// </summary>
        public static bool TryFirePhaseClause(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            EffectTiming timing)
        {
            if (engine == null || who == null || card == null || c == null ||
                c.Action != EffectActionKind.AddGySpellToHandAtEndPhaseIfSummonedThisTurn)
                return false;
            if (timing != EffectTiming.EndPhase) return true;
            var name = !string.IsNullOrEmpty(c.NamedCard) ? c.NamedCard : card.Name;
            bool Live() => card.FaceUp && !card.IsNegated && who.TryFindMonster(card, out _) &&
                           SummonFlagLive(engine, card) && !NameUsedThisTurn(engine, who, name);
            if (!Live() || GySpellTargets(engine, who, card).Count == 0) return true;

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => Live() ? GySpellTargets(engine, who, card) : new List<CardInstance>(),
                Kind = EffectTargetKind.SpellInYourGy,
                Prompt = $"{card.Name}: target 1 Spell in your GY to add to your hand (CANCEL = do not activate).",
                AskWithOneCandidate = true,
                AiChoose = list => list.OrderByDescending(AiHeuristicPolicy.DiscardPriority).FirstOrDefault(),
                Cancel = () => engine.Log($"{card.Name}: its End Phase effect is not activated."),
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{card.Name}: its End Phase effect is not activated.");
                        return;
                    }

                    // Cost (Lua thcost: ResetFlagEffect) and the hard once per turn (SetCountLimit(1,id)).
                    s_summoned.Remove(card);
                    MarkNameUsed(engine, who, name);
                    engine.Log($"{card.Name} activates: target {chosen.Name} in the GY.");
                    // Resolution (Lua thop: tc:IsRelateToEffect(e)).
                    if (!who.Graveyard.Contains(chosen) ||
                        ContinuousProtections.GyMovementLocked(engine, card, chosen))
                    {
                        engine.Log($"{card.Name}: {chosen.Name} is no longer a legal target — no effect.");
                        return;
                    }

                    who.Graveyard.Remove(chosen);
                    who.Hand.Add(chosen);
                    engine.Log($"{card.Name}: add {chosen.Name} from the GY to the hand.");
                }
            });
            return true;
        }

        // ───────────────────────────── EVENT_BATTLED triggers after the FLIP (Dark Magician of Chaos) ─────────────────────────────

        sealed class QueuedBattled
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public CardInstance Target;
            public int Turn;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<QueuedBattled>> s_battled = new();

        static List<QueuedBattled> BattledQueue(DuelEngine engine) => s_battled.GetValue(engine, _ => new List<QueuedBattled>());

        /// <summary>TextEffectRuntime.NotifyAfterDamageCalculation: a new battle's EVENT_BATTLED starts with an empty queue.</summary>
        public static void ClearBattledQueue(DuelEngine engine)
        {
            if (engine != null && s_battled.TryGetValue(engine, out var q)) q.Clear();
        }

        /// <summary>
        /// TextEffectRuntime.FireAfterDamageFor: <paramref name="self"/>'s <see cref="EffectClause.RequiresBattledMonsterDestroyed"/>
        /// trigger activated at EVENT_BATTLED (Lua rmcon was checked there). It resolves in
        /// <see cref="ResolveQueuedBattledTriggers"/>, after the FLIP effect of a monster flipped face-up by this battle
        /// (ocgcore: the turn player's trigger is Chain Link 1, the FLIP Chain Link 2, so the FLIP resolves first).
        /// </summary>
        public static void QueueBattledTrigger(DuelEngine engine, DuelistState who, CardInstance self, EffectClause c,
            CardInstance target)
        {
            if (engine == null || who == null || self == null || c == null || target == null) return;
            BattledQueue(engine).Add(new QueuedBattled
            {
                Who = who, Source = self, Clause = c, Target = target, Turn = engine.TurnNumber
            });
        }

        /// <summary>
        /// DuelEngine.ApplyBattleDestructions (both the direct path and the resume after a paused FLIP target pick): resolve
        /// the queued EVENT_BATTLED triggers of this battle (<paramref name="attacker"/> vs <paramref name="defender"/>).
        /// A target no longer on the field is no longer related to the battle (Lua rmop: bc:IsRelateToBattle()): no effect.
        /// </summary>
        public static void ResolveQueuedBattledTriggers(DuelEngine engine, CardInstance attacker, CardInstance defender)
        {
            if (engine == null || !s_battled.TryGetValue(engine, out var q) || q.Count == 0) return;
            var due = q.FindAll(x => x.Turn == engine.TurnNumber &&
                                     ((ReferenceEquals(x.Source, attacker) && ReferenceEquals(x.Target, defender)) ||
                                      (ReferenceEquals(x.Source, defender) && ReferenceEquals(x.Target, attacker))));
            q.Clear();
            foreach (var x in due)
            {
                var holder = engine.ControllerOf(x.Target);
                if (holder == null || !holder.TryFindMonster(x.Target, out _))
                {
                    engine.Log($"{x.Source.Name}: {x.Target.Name} is no longer on the field — no effect.");
                    continue;
                }

                var dummy = false;
                TextEffectRuntime.ApplyClause(engine, x.Who, x.Source, x.Clause, x.Target, ref dummy, ref dummy);
            }
        }

        // ───────────────────── Player-wide activation lock ─────────────────────

        /// <summary>
        /// True when a live <see cref="EffectActionKind.ContinuousThisPlayerCannotActivate"/> or
        /// <see cref="EffectActionKind.ContinuousNeitherPlayerCanActivate"/> source forbids
        /// <paramref name="who"/> from activating cards or effects at all — the player-wide half of the
        /// activation-lock row (Chaos Emperor Dragon - Envoy of the End).
        /// Checked at every activation site; GY effects of another card are untouched (a lock bars
        /// activating, not applying).
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null) return false;
            foreach (var src in LockSources(engine))
            {
                if (src == card) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousThisPlayerCannotActivate))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (CoversSide(c.Side, engine, src, who))
                    {
                        engine.Log($"{who.Name} cannot activate cards or effects ({src.Name}).");
                        return true;
                    }
                }
                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousNeitherPlayerCanActivate))
                {
                    if (!ConditionMet(engine, c)) continue;
                    // "Neither player can activate cards or effects" — every activation site, both sides.
                    engine.Log($"{who.Name} cannot activate cards or effects ({src.Name}).");
                    return true;
                }
            }
            return false;
        }

        static DuelistState ControllerOf(DuelEngine engine, CardInstance c)
        {
            if (engine?.Player != null && engine.Player.TryFindMonster(c, out _)) return engine.Player;
            if (engine?.Player != null && engine.Player.TryFindSpellTrap(c, out _)) return engine.Player;
            if (engine?.Opponent != null && engine.Opponent.TryFindMonster(c, out _)) return engine.Opponent;
            if (engine?.Opponent != null && engine.Opponent.TryFindSpellTrap(c, out _)) return engine.Opponent;
            return null;
        }

        // ───────────────────── Shared source scan ─────────────────────

        /// <summary>
        /// Every face-up, non-negated monster or Spell/Trap on either field that can carry a lock clause. A monster
        /// whose Summon is still in its summon-negation window is not on the field yet.
        /// </summary>
        static IEnumerable<CardInstance> LockSources(DuelEngine engine)
        {
            if (engine == null) yield break;
            foreach (var p in new[] { engine.Player, engine.Opponent })
            {
                if (p == null) continue;
                foreach (var m in p.MonstersOnField())
                    if (m != null && m.FaceUp && !m.IsNegated && !engine.IsSummonPending(m))
                        yield return m;
                foreach (var st in p.SpellTrapsOnField())
                    if (st != null && st.FaceUp && !st.IsNegated)
                        yield return st;
            }
        }

        /// <summary>
        /// Does a clause on <paramref name="src"/> (controlled by one duelist) apply to
        /// <paramref name="target"/>? Side Controller = the source's controller only, Opponent = the other
        /// duelist, Both / Either = either. Blank Side is treated as Controller.
        /// </summary>
        static bool CoversSide(EffectSide side, DuelEngine engine, CardInstance src, DuelistState target)
        {
            var controller = ControllerOf(engine, src);
            switch (side)
            {
                case EffectSide.Opponent:
                    return target != controller;
                case EffectSide.Both:
                case EffectSide.Either:
                    return true;
                case EffectSide.Controller:
                default:
                    return target == controller;
            }
        }

        static IEnumerable<EffectClause> ClausesOn(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Action == action &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    yield return c;
            }
        }

        static bool ConditionMet(DuelEngine engine, EffectClause c) =>
            ContinuousProtections.ConditionMet(engine, c);
    }
}
