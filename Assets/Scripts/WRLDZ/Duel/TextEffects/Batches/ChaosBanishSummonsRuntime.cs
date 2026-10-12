using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ChaosBanishSummons batch — the banish event (Lua EVENT_REMOVE). Every engine banish path records the card here
    /// (<see cref="DuelEngine.BanishCard"/>, a destroyed card banished instead of going to the GY, Banisher of the Light's
    /// redirect, Different Dimension Capsule's face-down banish, a top-of-Deck banish cost, Nobleman of Extermination's Deck
    /// purge). Cards banished inside one <see cref="Begin"/> / <see cref="End"/> group (one clause's application, one
    /// procedure or cost payment) are ONE event; a record outside any group is an event of its own. Soul Absorption
    /// (<see cref="EffectActionKind.GainLpPerCardBanished"/>) triggers on each event with the non-Token cards in it; its
    /// mandatory trigger activates once play is idle (DuelEngine.Notify → <see cref="FireQueuedTriggers"/>), as a Chain
    /// Link when the opponent could respond (TriggerChainResponsesRuntime), otherwise inline.
    /// </summary>
    public static class BanishEvents
    {
        sealed class Trigger
        {
            public CardInstance Source;
            public DuelistState Controller;
            public int Stamp;
            public int Count;
            public int Amount;
        }

        sealed class State
        {
            public int Depth;
            public readonly List<CardInstance> Open = new();
            public readonly List<Trigger> Queue = new();
            public bool Firing;
            /// <summary>Events closed so far (tests: "one event" vs "two events").</summary>
            public int EventCount;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>A group of banishes that happen at the same time (one event).</summary>
        public readonly struct Scope : IDisposable
        {
            readonly DuelEngine _engine;

            public Scope(DuelEngine engine)
            {
                _engine = engine;
                Begin(engine);
            }

            public void Dispose() => End(_engine);
        }

        /// <summary><c>using var _ = BanishEvents.Group(engine);</c>: every banish until the end of the block is one event.</summary>
        public static Scope Group(DuelEngine engine) => new Scope(engine);

        public static void Begin(DuelEngine engine)
        {
            if (engine != null) StateOf(engine).Depth++;
        }

        public static void End(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            if (st.Depth > 0) st.Depth--;
            if (st.Depth == 0) Close(engine, st);
        }

        /// <summary><paramref name="card"/> was just put in a banished pile (face-up or face-down, any reason).</summary>
        public static void Record(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            var st = StateOf(engine);
            if (!st.Open.Contains(card)) st.Open.Add(card);
            if (st.Depth == 0) Close(engine, st);
        }

        /// <summary>Banish events closed so far on <paramref name="engine"/> (tests).</summary>
        public static int EventCount(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) ? st.EventCount : 0;

        static void Close(DuelEngine engine, State st)
        {
            if (st.Open.Count == 0) return;
            var cards = st.Open.ToList();
            st.Open.Clear();
            st.EventCount++;
            // Lua s.filter: not c:IsType(TYPE_TOKEN).
            var n = cards.Count(c => c != null && !c.IsToken);
            if (n <= 0) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var sa in side.SpellTrapsOnField().ToList())
                {
                    var clause = AbsorptionClause(sa);
                    if (clause == null || !sa.FaceUp || sa.IsNegated) continue;
                    st.Queue.Add(new Trigger
                    {
                        Source = sa, Controller = side, Stamp = sa.FieldEntryStamp, Count = n,
                        Amount = clause.Amount > 0 ? clause.Amount : 500
                    });
                }
            }
        }

        static EffectClause AbsorptionClause(CardInstance card)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.GainLpPerCardBanished);
        }

        static bool StillApplies(DuelEngine engine, Trigger t) =>
            t.Source != null && t.Controller != null && t.Controller.TryFindSpellTrap(t.Source, out _) &&
            t.Source.FaceUp && t.Source.FieldEntryStamp == t.Stamp &&
            ReferenceEquals(engine.ControllerOf(t.Source), t.Controller);

        /// <summary>Queued Soul Absorption triggers (tests).</summary>
        public static int QueuedTriggers(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) ? st.Queue.Count : 0;

        /// <summary>
        /// DuelEngine.Notify once play is idle: each queued trigger whose card is still face-up on its controller's field
        /// (and not negated) activates; the triggers of one pass go on one Chain (TriggerChainResponsesRuntime collect).
        /// At resolution the card must still be on the field (Lua IsRelateToEffect); its controller gains the LP.
        /// </summary>
        public static void FireQueuedTriggers(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Firing || st.Queue.Count == 0) return;
            st.Firing = true;
            TriggerChainResponsesRuntime.BeginCollect(engine);
            try
            {
                while (st.Queue.Count > 0)
                {
                    if (engine.GameOver)
                    {
                        st.Queue.Clear();
                        return;
                    }

                    var t = st.Queue[0];
                    st.Queue.RemoveAt(0);
                    if (!StillApplies(engine, t) || t.Source.IsNegated) continue;
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, t.Source)) continue;
                    var gain = t.Count * t.Amount;
                    void Resolve()
                    {
                        if (!StillApplies(engine, t))
                        {
                            engine.Log($"{t.Source.Name} is no longer on the field — no LP gained.");
                            return;
                        }

                        if (ContinuousNegationRuntime.NegatedBy(engine, t.Source) != null || t.Source.IsNegated)
                        {
                            engine.Log($"{t.Source.Name}: its effect is negated.");
                            return;
                        }

                        engine.Log($"{t.Source.Name}: {t.Count} card(s) banished — {t.Controller.Name} gains {gain} LP.");
                        TextEffectRuntime.ApplyLifePointGain(engine, t.Controller, gain, t.Source.Name);
                    }

                    var seat = new TriggerChainResponsesRuntime.Seat
                    {
                        Controller = t.Controller,
                        Card = t.Source,
                        From = CardLocation.SpellTrapZone,
                        Resolve = Resolve
                    };
                    if (!TriggerChainResponsesRuntime.TrySeatOrCollect(engine, seat, Resolve))
                        Resolve();
                }
            }
            finally
            {
                TriggerChainResponsesRuntime.EndCollect(engine);
                st.Firing = false;
            }

            engine.NotifyPublic();
        }
    }

    /// <summary>
    /// ChaosBanishSummons batch runtime: Chaos Sorcerer / Black Luster Soldier - Envoy of the Beginning (the 1 LIGHT + 1 DARK
    /// procedure is paid by <see cref="EngineHygieneRuntime"/>; the ignition with its attack oath; the second attack in a
    /// row), Chaosrider Gustaph (banish up to 2 GY Spells at resolution, ATK until the end of the opponent's next turn),
    /// Primal Seed (condition and banished targets), Strike Ninja (Quick Effect: banish 2 DARK as the cost, banish itself
    /// until the End Phase). Soul Absorption's event is <see cref="BanishEvents"/>.
    /// </summary>
    public static class ChaosBanishSummonsRuntime
    {
        /// <summary>ActivationCondition: this card declared no attack this turn (Lua GetAttackAnnouncedCount()==0).</summary>
        public const string ThisDeclaredNoAttackThisTurn = "ChaosThisDeclaredNoAttackThisTurn";

        /// <summary>ActivationCondition: a face-up card named one of the '|'-separated <see cref="EffectClause.ConditionName"/>
        /// is on either field (Lua LOCATION_ONFIELD both sides, IsFaceup and IsCode).</summary>
        public const string NamedFaceUpOnEitherField = "ChaosNamedFaceUpOnEitherField";

        /// <summary>ActivationCondition (Chaosrider Gustaph): a Spell in your GY can be banished (Lua rmtg chk).</summary>
        public const string YourGySpellBanishable = "ChaosYourGySpellBanishable";

        /// <summary><see cref="ChainLink.EffectKey"/> of Strike Ninja's Quick Effect link.</summary>
        public const string NinjaLinkKey = "ChaosBanishSummons.MonsterQuickEffect";

        sealed class ChainAttack
        {
            public CardInstance Card;
            public int Stamp;
            public int Turn;
            public int CountAtGrant;
            public bool Lapsed;
        }

        sealed class AtkBoost
        {
            public CardInstance Card;
            public int Stamp;
            public int Amount;
            public int ExpireTurn;
        }

        sealed class State
        {
            public ChainAttack Chain;
            public readonly List<AtkBoost> Boosts = new();
            /// <summary>Strike Ninja's hard once per turn: (player, card name) used on this turn.</summary>
            public readonly HashSet<(DuelistState, string)> NameUsed = new();
            public int NameUsedTurn = -1;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet hook for this batch's conditions.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || c == null) return false;
            switch (c.ActivationCondition)
            {
                case ThisDeclaredNoAttackThisTurn:
                    ok = card != null && !card.AttackedThisTurn && card.AttacksDeclaredThisTurn == 0;
                    reason = "This card already declared an attack this turn.";
                    return true;
                case NamedFaceUpOnEitherField:
                    ok = NamedFaceUpOnField(engine, c.ConditionName);
                    if (ok && c.Action == EffectActionKind.AddBanishedTargetsToHand && c.TargetCount > 1)
                    {
                        // Primal Seed (Lua IsExistingTarget(..., LOCATION_REMOVED, 0, 2, nil)): 2 cards to target.
                        var banished = new List<CardInstance>();
                        CollectBanishedCards(who, banished);
                        if (banished.Count < c.TargetCount)
                        {
                            ok = false;
                            reason = $"Needs {c.TargetCount} of your banished cards to target.";
                            return true;
                        }
                    }

                    reason = $"Needs a face-up {string.Join(" or ", Names(c.ConditionName).Select(n => $"\"{n}\""))} " +
                             "on the field.";
                    return true;
                case YourGySpellBanishable:
                    ok = BanishableGySpells(engine, who, card).Count > 0;
                    reason = "No Spell Card in your GY can be banished.";
                    return true;
                default:
                    return false;
            }
        }

        static IEnumerable<string> Names(string list) =>
            (list ?? "").Split('|').Select(n => n.Trim()).Where(n => n.Length > 0);

        /// <summary>A face-up card named one of <paramref name="names"/> ('|'-separated) on either field.</summary>
        public static bool NamedFaceUpOnField(DuelEngine engine, string names)
        {
            var list = Names(names).ToList();
            if (engine == null || list.Count == 0) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                var cards = side.MonstersOnField().Concat(side.SpellTrapsOnField()).ToList();
                if (side.FieldSpellZone?.Occupant != null) cards.Add(side.FieldSpellZone.Occupant);
                if (cards.Any(x => x != null && x.FaceUp && list.Any(x.IsNamed))) return true;
            }

            return false;
        }

        // ───────────────────────────── the attack oath (Chaos Sorcerer / BLS) ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.ApplyClause prologue (before any negation): "This card cannot attack the turn you activate this
        /// effect" binds the activation (Lua rmcost: EFFECT_CANNOT_ATTACK, EFFECT_FLAG_OATH, reset at the End Phase).
        /// </summary>
        public static void ApplyAttackOath(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source == null) return;
            var ctrl = engine.ControllerOf(source);
            if (ctrl == null || !ctrl.TryFindMonster(source, out _)) return;
            source.CannotAttackDuringTurnNumber = engine.TurnNumber;
        }

        // ───────────────────────────── the ignition banish ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.BanishTargetedFieldMonster"/>: the target, still a monster in a Monster Zone (and
        /// still face-up when the clause asks it: Lua rmop tc:IsFaceup()), is banished face-up (REASON_EFFECT).
        /// </summary>
        public static void BanishTargetedFieldMonster(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null) return;
            var ctrl = target != null ? engine.ControllerOf(target) : null;
            if (ctrl == null || !ctrl.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name ?? "Effect"}: the target is no longer on the field — nothing is banished.");
                return;
            }

            if (clause != null && clause.RequiresTargetFaceUp && !target.FaceUp)
            {
                engine.Log($"{source?.Name ?? "Effect"}: the target is no longer face-up — nothing is banished.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            engine.BanishCard(ctrl, target, by: who);
        }

        /// <summary>AI pick for this batch's targeted clauses (TextEffectRuntime.AutoPick): the opponent's strongest monster.</summary>
        public static bool TryAutoPick(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> targets,
            out CardInstance pick)
        {
            pick = null;
            if (c == null || targets == null || targets.Count == 0) return false;
            switch (c.Action)
            {
                case EffectActionKind.BanishTargetedFieldMonster:
                    var opp = engine?.OpponentOf(who);
                    pick = targets.Where(t => opp != null && opp.TryFindMonster(t, out _))
                               .OrderByDescending(t => t.FaceUp ? t.CurrentAtk : 1500).FirstOrDefault()
                           ?? targets.OrderBy(t => t.CurrentAtk).First();
                    return true;
                case EffectActionKind.AddBanishedTargetsToHand:
                    pick = targets.OrderByDescending(t => t.Def != null && t.Def.IsMonster ? t.Def.atk : 1800).First();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// SimpleAi ignition loop: the AI does not banish its own monster (Chaos Sorcerer / BLS with no opponent's monster
        /// to target), and never uses Strike Ninja's Quick Effect as a Main Phase ignition.
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance monster)
        {
            if (engine == null || ai == null || monster?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(monster);
            if (prog == null || !prog.FullyCompiled) return false;
            if (prog.HasTiming(EffectTiming.MonsterQuickEffect) && NinjaClause(prog) != null) return true;
            var ign = prog.ClausesFor(EffectTiming.Activate)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.BanishTargetedFieldMonster);
            if (ign == null) return false;
            var opp = engine.OpponentOf(ai);
            return opp == null || !opp.MonstersOnField().Any(m => m != null && (m.FaceUp || !ign.RequiresTargetFaceUp));
        }

        // ───────────────────────────── a second attack in a row (BLS) ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.MakeSecondAttackInARow"/> (TextEffectRuntime.NotifyDestroyedOpponentByBattle → ApplyClause):
        /// only while this card is the attacker (DamageStepRulesRuntime.BattleAttacker), still face-up on its controller's field, and its "1 of these effects" was not
        /// used this turn (Lua atcon: Duel.GetAttacker()==c, bdocon, no flag, CanChainAttack). It grants the next attack.
        /// </summary>
        public static void GrantChainAttack(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null) return;
            var attacker = DamageStepRulesRuntime.BattleAttacker(engine) ?? engine.DeclaredAttacker;
            if (!ReferenceEquals(attacker, source) || !who.TryFindMonster(source, out _) || !source.FaceUp)
                return; // "If this attacking card": the attacker only (Lua Duel.GetAttacker()==c)
            if (clause != null && clause.OncePerTurn && source.EffectUsedThisTurn)
            {
                engine.Log($"{source.Name}: 1 of its effects was already activated this turn.");
                return;
            }

            source.EffectUsedThisTurn = true;
            StateOf(engine).Chain = new ChainAttack
            {
                Card = source,
                Stamp = source.FieldEntryStamp,
                Turn = engine.TurnNumber,
                CountAtGrant = source.AttacksDeclaredThisTurn
            };
            engine.Log($"{source.Name} can make a second attack in a row.");
        }

        static ChainAttack LiveChainAttack(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker == null || !s_state.TryGetValue(engine, out var st)) return null;
            var ca = st.Chain;
            if (ca == null || ca.Lapsed || !ReferenceEquals(ca.Card, attacker) || ca.Turn != engine.TurnNumber ||
                attacker.FieldEntryStamp != ca.Stamp)
                return null;
            return ca;
        }

        /// <summary>
        /// DuelEngine.MaxAttacksThisTurn: a granted second attack in a row lets this monster attack once more than it had
        /// when it was granted (Duel.ChainAttack ignores the attack count; the attack still counts).
        /// </summary>
        public static int AdjustMaxAttacks(DuelEngine engine, CardInstance attacker, int max)
        {
            var ca = LiveChainAttack(engine, attacker);
            return ca == null ? max : Math.Max(max, ca.CountAtGrant + 1);
        }

        /// <summary>The second attack in a row is still open for <paramref name="attacker"/> (tests).</summary>
        public static bool HasChainAttack(DuelEngine engine, CardInstance attacker)
        {
            var ca = LiveChainAttack(engine, attacker);
            return ca != null && attacker.AttacksDeclaredThisTurn <= ca.CountAtGrant;
        }

        /// <summary>
        /// DuelEngine.DeclareAttack: "in a row" — another monster's attack declaration ends the right to the second attack.
        /// </summary>
        public static void NoteAttackDeclared(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Chain == null) return;
            if (!ReferenceEquals(st.Chain.Card, attacker)) st.Chain.Lapsed = true;
        }

        // ───────────────────────────── Chaosrider Gustaph ─────────────────────────────

        /// <summary>Spells in <paramref name="who"/>'s GY that can be banished by an effect now (Necrovalley, Kycoo).</summary>
        static List<CardInstance> BanishableGySpells(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine == null || who?.Graveyard == null) return list;
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, who) || RuleLocksRuntime.BanishFromGyLocked(engine, who))
                return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsSpell) continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g)) continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>
        /// <see cref="EffectActionKind.BanishUpToSpellsFromYourGyGainAtkEach"/> (Lua rmop): choose 1 to
        /// <see cref="EffectClause.PickUpTo"/> Spells in your GY (no target; a human picks, the second one optional), banish
        /// them in one event, then — if any were banished and this card is still face-up on the field — it gains Amount ATK
        /// for each until the end of the opponent's next turn (RESET_OPPO_TURN; RESET_DISABLE: gone if negated).
        /// </summary>
        public static void ResolveGustaph(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var cap = Math.Max(1, clause.PickUpTo);
            var chosen = new List<CardInstance>();
            List<CardInstance> Left() => BanishableGySpells(engine, who, source).Where(s => !chosen.Contains(s)).ToList();
            if (Left().Count == 0)
            {
                engine.Log($"{source.Name}: no Spell Card in your GY — nothing is banished.");
                return;
            }

            var stamp = source.FieldEntryStamp;
            var inline = true; // false once a player's prompt made the rest run later

            void Finish()
            {
                var gone = 0;
                using (BanishEvents.Group(engine))
                {
                    foreach (var s in chosen)
                    {
                        if (!who.Graveyard.Contains(s)) continue;
                        engine.BanishCard(who, s, by: who);
                        if (who.Banished.Contains(s) || engine.OpponentOf(who).Banished.Contains(s)) gone++;
                    }
                }

                engine.Log($"{source.Name}: banishes {gone} Spell Card(s) from the GY.");
                if (gone > 0 && (!who.TryFindMonster(source, out _) || !source.FaceUp || source.FieldEntryStamp != stamp))
                    engine.Log($"{source.Name} is no longer face-up on the field — it gains no ATK.");
                else if (gone > 0)
                    GrantAtkUntilOpponentsTurnEnds(engine, who, source, gone * Math.Max(0, clause.Amount));
                // A player's pick finished the ignition after its activation returned: drop its idle link (no-op while a
                // Chain or another choice is open) so the banish event's triggers can activate.
                if (!inline) engine.ClearIdleChain();
                engine.NotifyPublic();
            }

            CardInstance AiPick(List<CardInstance> list) =>
                list == null || list.Count == 0 ? null : list.OrderBy(s => s.Def?.name ?? "").First();

            void PickNext()
            {
                var left = Left();
                if (chosen.Count >= cap || left.Count == 0)
                {
                    Finish();
                    return;
                }

                var optional = chosen.Count > 0;
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                {
                    Candidates = Left,
                    Kind = EffectTargetKind.SpellInYourGy,
                    Prompt = optional
                        ? $"{source.Name}: banish 1 more Spell Card from your GY (Cancel: only {chosen.Count})."
                        : $"{source.Name}: banish 1 Spell Card from your GY (up to {cap}).",
                    // The AI banishes as many as it may (more ATK).
                    AiChoose = AiPick,
                    AskWithOneCandidate = optional,
                    Cancel = optional ? Finish : null,
                    Finish = pick =>
                    {
                        if (pick == null)
                        {
                            Finish();
                            return;
                        }

                        chosen.Add(pick);
                        PickNext();
                    }
                });
            }

            PickNext();
            inline = false;
        }

        /// <summary>
        /// This card gains <paramref name="amount"/> ATK until the end of the opponent's next turn (a lingering modifier
        /// removed at that End Phase, when the card leaves the field, or as soon as its effects are negated).
        /// </summary>
        static void GrantAtkUntilOpponentsTurnEnds(DuelEngine engine, DuelistState who, CardInstance card, int amount)
        {
            if (amount <= 0) return;
            card.LingeringAtkModifier += amount;
            // The opponent's next turn: the first turn after this one that is not `who`'s (TurnNumber + 1 on your turn).
            var expire = ReferenceEquals(engine.TurnPlayer, who) ? engine.TurnNumber + 1 : engine.TurnNumber + 2;
            StateOf(engine).Boosts.Add(new AtkBoost
            {
                Card = card, Stamp = card.FieldEntryStamp, Amount = amount, ExpireTurn = expire
            });
            engine.Log($"{card.Name} gains {amount} ATK until the end of your opponent's next turn.");
        }

        /// <summary>
        /// DuelEngine.Notify (board settled): a boosted card that left the field, was turned face-down or is negated loses
        /// the boost (Lua RESETS_STANDARD_DISABLE: RESET_LEAVE / RESET_TURN_SET / RESET_DISABLE).
        /// </summary>
        public static void OnBoardSettled(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Boosts.Count == 0) return;
            foreach (var b in st.Boosts.ToList())
            {
                var ctrl = engine.ControllerOf(b.Card);
                var onField = ctrl != null && ctrl.TryFindMonster(b.Card, out _) && b.Card.FieldEntryStamp == b.Stamp;
                if (onField && b.Card.FaceUp && !b.Card.IsNegated) continue;
                st.Boosts.Remove(b);
                if (onField) b.Card.LingeringAtkModifier -= b.Amount; // RESET_TURN_SET / RESET_DISABLE
            }
        }

        /// <summary>DuelEngine End Phase (with DcrSpellTrapUtilityRuntime.OnEndPhase): boosts that end this turn expire.</summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Boosts.Count == 0) return;
            foreach (var b in st.Boosts.Where(x => x.ExpireTurn <= engine.TurnNumber).ToList())
            {
                st.Boosts.Remove(b);
                var ctrl = engine.ControllerOf(b.Card);
                if (ctrl == null || !ctrl.TryFindMonster(b.Card, out _) || b.Card.FieldEntryStamp != b.Stamp) continue;
                b.Card.LingeringAtkModifier -= b.Amount;
                engine.Log($"{b.Card.Name}: its {b.Amount} ATK gain ends.");
            }
        }

        // ───────────────────────────── Primal Seed ─────────────────────────────

        /// <summary>Targets for <see cref="EffectZoneFilter.ControllerBanishedCards"/>: every card in your banished pile.</summary>
        public static void CollectBanishedCards(DuelistState who, List<CardInstance> into)
        {
            if (who?.Banished == null || into == null) return;
            foreach (var c in who.Banished)
                if (c?.Def != null && !c.IsToken && !into.Contains(c))
                    into.Add(c);
        }

        /// <summary>
        /// <see cref="EffectActionKind.AddBanishedTargetsToHand"/> for one target: still in your banished pile → its owner's
        /// hand (Lua: the targets still related to the effect; a face-down card is revealed as it is added).
        /// </summary>
        public static void AddBanishedToHand(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target)
        {
            if (engine == null || who == null || target == null) return;
            if (who.Banished == null || !who.Banished.Contains(target))
            {
                engine.Log($"{source?.Name ?? "Effect"}: {target.Name} is no longer banished — not added.");
                return;
            }

            who.Banished.Remove(target);
            target.FaceUp = true;
            who.Hand.Add(target);
            engine.Log($"{source?.Name ?? "Effect"}: {target.Name} is added from the banished pile to {who.Name}'s hand.");
        }

        // ───────────────────────────── Strike Ninja ─────────────────────────────

        static EffectClause NinjaClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.MonsterQuickEffect)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.BanishThisUntilEndPhase);

        /// <summary>A face-up monster on <paramref name="who"/>'s field whose Quick Effect is this batch's (Strike Ninja).</summary>
        public static bool IsNinjaCard(DuelistState who, CardInstance card, CompiledCardProgram prog = null)
        {
            if (who == null || card?.Def == null || !card.Def.IsMonster || !card.FaceUp) return false;
            prog ??= CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled || NinjaClause(prog) == null) return false;
            return who.TryFindMonster(card, out _);
        }

        static bool NameUsed(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (!s_state.TryGetValue(engine, out var st) || st.NameUsedTurn != engine.TurnNumber) return false;
            return st.NameUsed.Contains((who, card.Def?.name ?? card.Name));
        }

        static void MarkNameUsed(DuelEngine engine, DuelistState who, CardInstance card, bool used)
        {
            var st = StateOf(engine);
            if (st.NameUsedTurn != engine.TurnNumber)
            {
                st.NameUsed.Clear();
                st.NameUsedTurn = engine.TurnNumber;
            }

            var key = (who, card.Def?.name ?? card.Name);
            if (used) st.NameUsed.Add(key);
            else st.NameUsed.Remove(key);
        }

        /// <summary>
        /// The cost's candidates (Lua s.cfilter, except this card): face-up monsters of the Attribute in your GY, or — under
        /// Spirit Elimination — face-up monsters of that Attribute you control (aux.SpElimFilter(c)).
        /// </summary>
        public static List<CardInstance> NinjaCostCandidates(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause clause)
        {
            var list = new List<CardInstance>();
            if (engine == null || who?.Graveyard == null || clause == null) return list;
            bool AttrOk(CardInstance m, bool live) =>
                string.IsNullOrEmpty(clause.AttributeFilter) ||
                string.Equals(live ? EquipFamilyRuntime.CurrentAttribute(m) : m.Def?.attribute, clause.AttributeFilter,
                    StringComparison.OrdinalIgnoreCase);
            if (SpiritEliminationRuntime.Affects(engine, who))
                return SpiritEliminationRuntime.Substitutes(engine, who, card, true, m => AttrOk(m, true));
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, who) || RuleLocksRuntime.BanishFromGyLocked(engine, who))
                return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster || g == card || !AttrOk(g, false)) continue;
                if (ContinuousProtections.GyMovementLocked(engine, card, g)) continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>
        /// Strike Ninja's Quick Effect can be activated now by <paramref name="who"/>: open play on either turn (Main Phases,
        /// the Battle Step, the End Phase; not during an attack or the Damage Step), the opponent's open-state window, an
        /// attack declaration, a Summon by the opponent, or as a Chain response; not negated; once per turn by name; 2 cost
        /// monsters available.
        /// </summary>
        public static bool CanActivateNinja(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog,
            out string reason)
        {
            reason = "Not a face-up monster with this Quick Effect.";
            if (engine == null || !IsNinjaCard(who, card, prog)) return false;
            var clause = NinjaClause(prog ?? CompiledEffectCache.GetOrCompile(card));
            if (engine.GameOver || clause == null)
            {
                reason = "Not now.";
                return false;
            }

            if (engine.PendingActivation != null)
            {
                reason = "Finish the current choice first.";
                return false;
            }

            if (engine.IsResolvingChain || engine.Chain.IsResolving)
            {
                reason = "A Chain is resolving.";
                return false;
            }

            if (card.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, card) != null)
            {
                reason = "This card's effects are negated.";
                return false;
            }

            if (ContinuousNegationRuntime.ActivationLocked(engine, who, card, out var lockReason))
            {
                reason = lockReason;
                return false;
            }

            var pr = engine.PendingResponse;
            if (pr == null)
            {
                if (engine.TurnPlayer != who)
                {
                    reason = "Respond in a window on your opponent's turn.";
                    return false;
                }

                if (engine.HasDeclaredAttack || engine.DamageSubStep != DamageSubStep.None)
                {
                    reason = "Finish the declared attack / response first.";
                    return false;
                }

                if (engine.Phase != DuelPhase.Main1 && engine.Phase != DuelPhase.Main2 &&
                    engine.Phase != DuelPhase.Battle && engine.Phase != DuelPhase.End)
                {
                    reason = "Not in an open game state.";
                    return false;
                }

                if (engine.Chain.HasLinks &&
                    !(engine.Chain.Count == 1 && engine.Chain.Last.Card == card && !engine.Chain.Last.Resolved))
                {
                    reason = "Respond in the Chain window.";
                    return false;
                }
            }
            else
            {
                if (pr.Responder != who)
                {
                    reason = "Not your response.";
                    return false;
                }

                switch (pr.Timing)
                {
                    case ResponseTiming.ChainResponse:
                        if (pr.ChainAnswersOnly || !engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick))
                        {
                            reason = "A Spell Speed 2 effect cannot be chained to this Chain Link.";
                            return false;
                        }

                        break;
                    case ResponseTiming.OpponentOpenState:
                    case ResponseTiming.AttackDeclared:
                        if (pr.AttackingPlayer == who && pr.Timing == ResponseTiming.AttackDeclared)
                        {
                            reason = "Not in your own attack's window.";
                            return false;
                        }

                        break;
                    case ResponseTiming.MonsterSummoned:
                        if (pr.Summoner == who)
                        {
                            reason = "Not legal for this response timing.";
                            return false;
                        }

                        break;
                    default:
                        reason = "Not legal for this response timing.";
                        return false;
                }
            }

            if (NameUsed(engine, who, card))
            {
                reason = $"\"{card.Name}\"'s effect was already used this turn.";
                return false;
            }

            if (NinjaCostCandidates(engine, who, card, clause).Count < Math.Max(1, clause.BanishFromGyCount))
            {
                reason = $"Needs {clause.BanishFromGyCount} {clause.AttributeFilter} monsters in your GY to banish.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>SpellTrapEffects.IsLegalResponseCard hook (before BattleBoosts): false when not Strike Ninja.</summary>
        public static bool TryIsLegalResponse(DuelEngine engine, DuelistState who, CardInstance card, out bool legal)
        {
            legal = false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (!IsNinjaCard(who, card, prog)) return false;
            legal = CanActivateNinja(engine, who, card, prog, out _);
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CollectLegalResponseCards: the response windows that do not list monsters themselves (the
        /// opponent's open state, an attack declaration, a Summon) offer Strike Ninja's Quick Effect.
        /// </summary>
        public static void CollectResponses(DuelEngine engine, DuelistState who, ResponseTiming timing,
            List<CardInstance> list)
        {
            if (engine == null || who == null || list == null) return;
            if (timing != ResponseTiming.OpponentOpenState && timing != ResponseTiming.AttackDeclared &&
                timing != ResponseTiming.MonsterSummoned)
                return;
            foreach (var m in who.MonstersOnField())
                if (m != null && !list.Contains(m) && IsNinjaCard(who, m) &&
                    CanActivateNinja(engine, who, m, null, out _))
                    list.Add(m);
        }

        /// <summary>
        /// SpellTrapEffects.BeginOrResolveManual hook (before BattleBoosts): Strike Ninja's Quick Effect seats a Spell
        /// Speed 2 Chain Link, the 2 banishes are paid (a player picks them; Cancel undoes the activation), then the
        /// opponent may respond; the effect applies when the link resolves (<see cref="TryResolveNinjaLink"/>).
        /// </summary>
        public static bool TryActivateNinja(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick, out bool ok)
        {
            ok = false;
            if (engine == null || who == null || card?.Def == null || fromHand) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (!IsNinjaCard(who, card, prog)) return false;
            if (!CanActivateNinja(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                return true;
            }

            var clause = NinjaClause(prog);
            var pr = engine.PendingResponse;
            var timing = pr != null && pr.Responder == who ? pr.Timing : ResponseTiming.None;
            var answered = pr?.ChainTarget;
            var link = engine.Chain.Last;
            if (timing == ResponseTiming.ChainResponse || link == null || link.Card != card || link.Resolved)
            {
                link = engine.Chain.AddLink(who, card, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.MonsterZone,
                    fromHand: false, wasSet: false, effectKey: NinjaLinkKey);
                if (link == null)
                {
                    engine.Log($"{card.Name}: its effect cannot be activated now.");
                    return true;
                }
            }

            link.EffectKey = NinjaLinkKey;
            link.Speed = SpellSpeed.Speed2;
            link.Class = EffectClass.Quick;
            link.WasSetOnField = false;
            link.FromHand = false;
            if (timing == ResponseTiming.ChainResponse)
                link.TargetLink = answered;
            MarkNameUsed(engine, who, card, true);
            ok = true;

            var need = Math.Max(1, clause.BanishFromGyCount);
            var chosen = new List<CardInstance>();
            List<CardInstance> Left() => NinjaCostCandidates(engine, who, card, clause).Where(c => !chosen.Contains(c)).ToList();
            CardInstance AiPick(List<CardInstance> l) =>
                l == null || l.Count == 0 ? null : l.OrderBy(c => c.Def?.atk ?? 0).ThenBy(c => c.Level).First();
            var human = engine.IsHumanControlled(who) && !autoPick;
            var stamp = card.FieldEntryStamp;

            void Undo()
            {
                if (!link.Resolved) engine.Chain.RemoveUnresolved(link);
                MarkNameUsed(engine, who, card, false);
                engine.Log($"{card.Name}: activation cancelled — nothing is banished.");
                engine.ClearIdleChain();
                engine.NotifyPublic();
            }

            void Pay()
            {
                BattleWindowsRuntime.BeginCost(engine);
                try
                {
                    using (BanishEvents.Group(engine))
                    {
                        foreach (var g in chosen)
                        {
                            var onField = who.TryFindMonster(g, out _);
                            if (!who.Graveyard.Contains(g) && !onField) continue;
                            engine.BanishCard(who, g, by: who);
                            engine.Log(onField
                                ? $"Cost: banish {g.Name} from the field (Spirit Elimination)."
                                : $"Cost: banish {g.Name} from the GY.");
                        }
                    }
                }
                finally
                {
                    BattleWindowsRuntime.EndCost(engine);
                }

                s_links.Remove(link);
                s_links.Add(link, new NinjaLink { Stamp = stamp });
                Seat(engine, who, card, link, timing);
            }

            if (!human || Left().Count <= need)
            {
                var left = Left();
                while (chosen.Count < need && left.Count > 0)
                {
                    var pick = AiPick(left);
                    chosen.Add(pick);
                    left.Remove(pick);
                }

                Pay();
                return true;
            }

            void PickNext()
            {
                if (chosen.Count >= need)
                {
                    Pay();
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
                {
                    Candidates = Left,
                    Kind = EffectTargetKind.BanishFromYourGy,
                    Prompt = $"banish {need - chosen.Count} more {clause.AttributeFilter} monster(s) from your GY to " +
                             $"activate {card.Name} (cost), or Cancel to not activate it.",
                    AiChoose = AiPick,
                    DuringActivation = true,
                    Cancel = Undo,
                    Finish = pick =>
                    {
                        if (pick == null)
                        {
                            Undo();
                            return;
                        }

                        chosen.Add(pick);
                        PickNext();
                    }
                });
            }

            PickNext();
            return true;
        }

        sealed class NinjaLink
        {
            public int Stamp;
        }

        static readonly ConditionalWeakTable<ChainLink, NinjaLink> s_links = new();

        static void Seat(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link, ResponseTiming timing)
        {
            engine.Log($"Activate: {card.Name} (Quick Effect) — Chain Link {link.LinkNumber}.");
            if (timing == ResponseTiming.ChainResponse)
            {
                engine.ClearPendingResponse();
                if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }

                return;
            }

            if (timing != ResponseTiming.None)
            {
                TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, timing);
                return;
            }

            if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
            {
                if (engine.Chain.HasLinks && !engine.Chain.IsResolving && !engine.IsResolvingChain)
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }
            }
            else
                engine.NotifyPublic();
        }

        /// <summary><paramref name="link"/> is Strike Ninja's Quick Effect link.</summary>
        public static bool IsNinjaLink(ChainLink link) => link != null && link.EffectKey == NinjaLinkKey;

        /// <summary>
        /// DuelEngine.ResolveChainStack hook. False for any other link. A negated link, a card no longer face-up on its
        /// controller's field (Lua c:IsFaceup() and c:IsRelateToEffect(e)) or with its effects negated does nothing;
        /// otherwise the card is banished until this turn's End Phase (DcrSpellTrapUtilityRuntime's temporary banishment).
        /// </summary>
        public static bool TryResolveNinjaLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsNinjaLink(link)) return false;
            var card = link.Card;
            var who = link.Controller;
            var name = card?.Name ?? "Effect";
            s_links.TryGetValue(link, out var state);
            s_links.Remove(link);
            if (link.Negated)
            {
                engine.Log($"{name}: its effect is negated.");
                return true;
            }

            var onField = card != null && who != null && who.TryFindMonster(card, out _) &&
                          (state == null || card.FieldEntryStamp == state.Stamp);
            if (!onField || !card.FaceUp)
            {
                engine.Log($"{name} is no longer face-up on the field — it is not banished.");
                return true;
            }

            if (ContinuousNegationRuntime.NegatedBy(engine, card) != null || card.IsNegated ||
                ContinuousNegationRuntime.NegatesMonsterEffectOnField(engine, card))
            {
                engine.Log($"{name}: its effect is negated.");
                return true;
            }

            using (BanishEvents.Group(engine))
                DcrSpellTrapUtilityRuntime.BanishUntilEndPhase(engine, who, card, card);
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond: the AI saves Strike Ninja from an opponent's Chain Link that would remove it
        /// (it targets Ninja or destroys / banishes / bounces every monster) or from an attack it would lose.
        /// </summary>
        public static bool AiWantsNinja(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || !IsNinjaCard(ai, card)) return false;
            var pr = engine.PendingResponse;
            if (pr == null || pr.Responder != ai) return false;
            if (pr.Timing == ResponseTiming.ChainResponse)
            {
                var last = engine.Chain.Last;
                if (last == null || ReferenceEquals(last.Controller, ai)) return false;
                if (last.Target == card || last.Targets.Contains(card)) return true;
                var prog = last.Card != null ? CompiledEffectCache.GetOrCompile(last.Card) : null;
                return prog != null && prog.ClausesFor(EffectTiming.Activate).Any(c => c != null && !c.RequiresTargetChoice &&
                    (c.Action == EffectActionKind.Destroy || c.Action == EffectActionKind.Banish ||
                     c.Action == EffectActionKind.ReturnToHand) &&
                    (c.Zone == EffectZoneFilter.FieldMonsters || c.Zone == EffectZoneFilter.AllOtherCardsOnField) &&
                    c.Side != EffectSide.Controller);
            }

            if (pr.Timing == ResponseTiming.AttackDeclared && ReferenceEquals(pr.AttackTarget, card))
            {
                var atk = pr.Attacker;
                if (atk == null) return false;
                var mine = card.Position == BattlePosition.Attack ? card.CurrentAtk : card.CurrentDef;
                return atk.CurrentAtk > mine;
            }

            return false;
        }
    }
}
