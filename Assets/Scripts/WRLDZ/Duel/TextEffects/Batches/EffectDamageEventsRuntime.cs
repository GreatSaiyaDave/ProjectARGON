using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>One effect-damage event: who took how much effect damage, from which card (when the caller knew it).</summary>
    public sealed class EffectDamageRecord
    {
        public DuelistState Victim;
        public int Amount;
        public CardInstance Source;
        public string SourceName;
        public int Turn;
    }

    /// <summary>
    /// Card batch "EffectDamageEvents" (v162) runtime — the central effect-damage event (atoms:
    /// <see cref="EffectDamageEventsAtoms"/>). Every effect damage goes through DuelEngine.ApplyEffectDamage /
    /// ApplyEffectDamageBoth, which call, in ygopro <c>field::damage</c> order:
    /// <list type="number">
    /// <item><see cref="ModifyEffectDamage"/> before the LP change: EFFECT_REFLECT_DAMAGE (the damage goes to the other
    /// player), then EFFECT_CHANGE_DAMAGE → 0 on the player who takes it; then DuelEngine's Crush Card Virus guard. A modifier
    /// is keyed to ONE Chain Link (the link a Counter Trap answered) and applies only while that link resolves and only to
    /// damage from that link's card; every modifier is cleared when the Chain ends (<see cref="OnChainEnded"/>).</item>
    /// <item><see cref="AfterEffectDamage"/> after the LP change: the event is recorded (<see cref="LastEffectDamage"/>)
    /// and "each time your opponent takes damage from a card effect" triggers are queued on the simultaneous-trigger Chain
    /// (Dark Room of Nightmare, not for damage from a card with its own name — no loop).</item>
    /// </list>
    /// <b>Would-damage knowledge</b> (<see cref="LinkWouldDamage"/>, ygopro CATEGORY_DAMAGE operation info): derived from the
    /// compiled clauses of the link's card activation (burn actions and the player each one hits; an LP gain that Bad Reaction
    /// to Simochi turns into damage), never per card. A face-up Spell/Trap's effect link is not a card activation.
    /// <b>Counter Traps</b> (Barrel Behind the Door, Trap of Board Eraser): <see cref="CanAnswerDamageLink"/>
    /// (TextEffectRuntime.CanActivateChainResponse and DuelEngine.TryActivateChainResponse) and <see cref="TryResolveLink"/>
    /// (DuelEngine.ResolveChainStack; Board Eraser's discard pick holds the Chain for a human victim).
    /// <b>Battle damage as effect damage</b> (Gravekeeper's Vassal, ygopro EFFECT_BATTLE_DAMAGE_TO_EFFECT):
    /// <see cref="BattleDamageAsEffect"/> in DuelEngine's damage calculation — the damage the card inflicts (not piercing)
    /// skips every battle-damage change / prevention and is dealt as effect damage; no battle-damage event follows.
    /// <b>Sent to the opponent's GY</b> (Coffin Seller): <see cref="GySendCheckpoint"/> (from the Skull Invitation checkpoint:
    /// each board refresh, before each Chain Link resolves and before each clause applies) — one trigger per send event.
    /// State is kept per engine.
    /// </summary>
    public static class EffectDamageEventsRuntime
    {
        enum ModifierKind
        {
            Reflect,
            Negate
        }

        sealed class Modifier
        {
            public ModifierKind Kind;
            public ChainLink Link;
            public DuelistState Player;
            public CardInstance Source;
        }

        sealed class Watcher
        {
            public DuelistState Side;
            public CardInstance Card;
            public int Stamp;
        }

        sealed class State
        {
            public ChainLink Resolving;
            public readonly List<Modifier> Modifiers = new();
            public readonly Dictionary<DuelistState, HashSet<CardInstance>> SeenMonsters = new();
            public bool SeenValid;
            public readonly List<Watcher> Watchers = new();
            public EffectDamageRecord Last;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>The ChainLinkActivated clause of the two Counter Traps.</summary>
        public static bool IsDamageAnswer(EffectClause clause) =>
            clause != null &&
            (clause.Action == EffectActionKind.ReflectEffectDamageOfAnsweredLink ||
             clause.Action == EffectActionKind.NegateEffectDamageOfAnsweredLinkThenOpponentDiscards);

        /// <summary>The last effect damage taken in this duel (null before any).</summary>
        public static EffectDamageRecord LastEffectDamage(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) ? st.Last : null;

        // ───────────────────────────── would-damage knowledge ─────────────────────────────

        /// <summary>
        /// True when resolving <paramref name="link"/> would inflict effect damage to <paramref name="player"/> (ygopro
        /// GetOperationInfo CATEGORY_DAMAGE with cp == player or PLAYER_ALL, or CATEGORY_RECOVER under EFFECT_REVERSE_RECOVER).
        /// </summary>
        public static bool LinkWouldDamage(DuelEngine engine, ChainLink link, DuelistState player)
        {
            if (engine == null || player == null || link?.Card?.Def == null || link.Negated) return false;
            var def = link.Card.Def;
            // An effect of a face-up Spell/Trap is not a card activation (Lua: re:IsSpellTrapEffect() and not ACTIVATE).
            if ((def.IsSpell || def.IsTrap) &&
                (PerCountBurnsRuntime.IsCardEffectLink(link) || FaceUpQuickEffectsRuntime.IsQuickEffectLink(link)))
                return false;
            var prog = CompiledEffectCache.GetOrCompile(link.Card);
            if (prog == null || !prog.FullyCompiled) return false;
            var controller = link.Controller ?? engine.ControllerOf(link.Card);
            if (controller == null) return false;
            var opp = engine.OpponentOf(controller);
            foreach (var c in ActivationClauses(prog, link))
            foreach (var victim in Victims(engine, c, controller, opp, link))
                if (ReferenceEquals(victim, player))
                    return true;
            return false;
        }

        /// <summary>The clauses the link's activation resolves: Activate, plus the window clauses of a seated response.</summary>
        static List<EffectClause> ActivationClauses(CompiledCardProgram prog, ChainLink link)
        {
            var list = new List<EffectClause>(prog.ClausesFor(EffectTiming.Activate));
            switch (link.SeatedFrom)
            {
                case ResponseTiming.AttackDeclared:
                    list.AddRange(prog.ClausesFor(EffectTiming.AttackDeclared));
                    break;
                case ResponseTiming.MonsterSummoned:
                    list.AddRange(prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon));
                    break;
                case ResponseTiming.DamageCalculation:
                    list.AddRange(prog.ClausesFor(EffectTiming.DamageCalculation));
                    break;
                case ResponseTiming.YouTakeDamage:
                    list.AddRange(prog.ClausesFor(EffectTiming.YouTakeLifePointDamage));
                    break;
            }

            return list;
        }

        /// <summary>Who each burn action hits (TextEffectRuntime.ApplyClause: <c>who</c> = controller, <c>opp</c>).</summary>
        static IEnumerable<DuelistState> Victims(DuelEngine engine, EffectClause c, DuelistState controller,
            DuelistState opp, ChainLink link)
        {
            if (c == null) yield break;
            switch (c.Action)
            {
                case EffectActionKind.InflictDamageToOpponent:
                case EffectActionKind.InflictDamageHalfTributedAtk:
                case EffectActionKind.InflictDamageEqualToAtk:
                case EffectActionKind.DestroyOppAttackThenDamage:
                    yield return opp;
                    break;
                case EffectActionKind.DestroyTokensInflictPer:
                    if (!c.TokenDestroyGainsLp) yield return opp;
                    break;
                case EffectActionKind.TakeEffectDamage:
                    yield return controller;
                    break;
                case EffectActionKind.EffectDamageBothFromOriginalAtk:
                    yield return controller;
                    yield return opp;
                    break;
                case EffectActionKind.Destroy:
                    // "… and if you do, inflict N damage to its controller": the controller of the chosen target.
                    if (c.DamageItsController <= 0) break;
                    foreach (var t in LinkTargets(link))
                    {
                        var tc = engine.ControllerOf(t);
                        if (tc != null) yield return tc;
                    }

                    break;
                case EffectActionKind.GainLifePoints:
                case EffectActionKind.GainLpEqualToAtk:
                {
                    // EFFECT_REVERSE_RECOVER (Bad Reaction to Simochi): the gain is damage instead.
                    var gainers = c.Action == EffectActionKind.GainLpEqualToAtk
                        ? new[] { controller }
                        : c.Side == EffectSide.Both
                            ? new[] { controller, opp }
                            : new[] { c.Side == EffectSide.Opponent || c.OpponentIsSubject ? opp : controller };
                    foreach (var g in gainers)
                        if (g != null && TextEffectRuntime.OpponentLpGainIsConverted(engine, g))
                            yield return g;
                    break;
                }
            }
        }

        static IEnumerable<CardInstance> LinkTargets(ChainLink link)
        {
            if (link.Targets != null && link.Targets.Count > 0)
            {
                foreach (var t in link.Targets)
                    if (t != null)
                        yield return t;
            }
            else if (link.Target != null)
                yield return link.Target;
        }

        // ───────────────────────────── Counter Trap answers ─────────────────────────────

        /// <summary>
        /// Barrel Behind the Door / Trap of Board Eraser may answer <paramref name="target"/> (the last Chain Link): a Set,
        /// not-Set-this-turn card on <paramref name="who"/>'s field, a link that would inflict effect damage to them, and (Board
        /// Eraser's target step) an opponent holding a card.
        /// </summary>
        public static bool CanAnswerDamageLink(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause clause, ChainLink target, out string reason)
        {
            reason = "OK";
            if (engine == null || who == null || card?.Def == null || target?.Card?.Def == null || clause == null)
            {
                reason = "Not a chain-activation response.";
                return false;
            }

            if (!who.TryFindSpellTrap(card, out _))
            {
                reason = "Counter Trap must be Set on the field.";
                return false;
            }

            if (card.FaceUp)
            {
                reason = "Already face-up.";
                return false;
            }

            if (card.SetThisTurn)
            {
                reason = "Cannot activate the turn it was Set.";
                return false;
            }

            if (!LinkWouldDamage(engine, target, who))
            {
                reason = "That activation would not inflict damage to you.";
                return false;
            }

            if (clause.Action == EffectActionKind.NegateEffectDamageOfAnsweredLinkThenOpponentDiscards)
            {
                var opp = engine.OpponentOf(who);
                if (opp?.Hand == null || !opp.Hand.Any(h => h != null))
                {
                    reason = "Your opponent has no card in hand to discard.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack: the link of Barrel Behind the Door / Trap of Board Eraser resolves — the damage
        /// modifier for the link it answered is registered (and Board Eraser's opponent discards). True when handled.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link?.Card?.Def == null || link.TargetLink == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(link.Card);
            if (prog == null || !prog.FullyCompiled) return false;
            var clause = prog.ClausesFor(EffectTiming.ChainLinkActivated).FirstOrDefault(IsDamageAnswer);
            if (clause == null) return false;
            // Royal Decree / Imperial Order: the Counter Trap's effect is negated on the field.
            if (ContinuousNegationRuntime.ChainLinkEffectNegated(engine, link)) return true;
            var who = link.Controller ?? engine.ControllerOf(link.Card);
            if (who == null) return true;
            var opp = engine.OpponentOf(who);
            var answered = link.TargetLink;
            var st = StateOf(engine);
            if (clause.Action == EffectActionKind.ReflectEffectDamageOfAnsweredLink)
            {
                st.Modifiers.Add(new Modifier
                    { Kind = ModifierKind.Reflect, Link = answered, Player = who, Source = link.Card });
                engine.Log($"{link.Card.Name}: the effect damage {answered.Card?.Name ?? "that effect"} would inflict to " +
                           $"{who.Name} goes to {opp?.Name ?? "the opponent"} instead.");
                return true;
            }

            st.Modifiers.Add(new Modifier
                { Kind = ModifierKind.Negate, Link = answered, Player = who, Source = link.Card });
            engine.Log($"{link.Card.Name}: the effect damage {answered.Card?.Name ?? "that effect"} would inflict to " +
                       $"{who.Name} is negated.");
            OpponentDiscardsOneOfTheirChoice(engine, who, link.Card);
            return true;
        }

        /// <summary>
        /// "your opponent then selects 1 card from his/her hand and discards it": the AI (or a lone card) discards at once; a
        /// human with 2+ cards picks through the Delinquent Duo prompt (<see cref="PendingActivation.AwaitingForcedDiscardChoice"/>),
        /// which holds the resolving Chain (DiscardTriggersRuntime.FinishForcedDiscardChoice resumes it).
        /// </summary>
        static void OpponentDiscardsOneOfTheirChoice(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var victim = engine.OpponentOf(who);
            var hand = victim?.Hand?.Where(h => h != null).ToList();
            if (hand == null || hand.Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: {victim?.Name ?? "the opponent"} has no card in hand to discard.");
                return;
            }

            if (hand.Count > 1 && engine.IsHumanControlled(victim))
            {
                var pending = new PendingActivation
                {
                    Controller = victim,
                    Card = source,
                    FromHand = false,
                    TargetKind = EffectTargetKind.DiscardFromHand,
                    UsesTextProgram = true,
                    AwaitingForcedDiscardChoice = true,
                    HoldsChainResolution = engine.IsResolvingChain
                };
                pending.LegalTargets.AddRange(hand);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            var pick = hand.Count == 1 ? hand[0] : DiscardTriggersRuntime.PickOwnDiscard(hand);
            var gone = MonsterEffects.DiscardFromHandByInstance(victim, pick);
            if (gone == null) return;
            engine.Log($"{source?.Name ?? "Effect"}: {victim.Name} discards {gone.Name}.");
            DiscardTriggersRuntime.NoteDiscard(engine, victim, gone, who);
        }

        // ───────────────────────────── the damage event ─────────────────────────────

        /// <summary>DuelEngine.ResolveChainStack: <paramref name="link"/> starts resolving.</summary>
        public static void NoteResolving(DuelEngine engine, ChainLink link)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st))
                st.Resolving = link;
        }

        /// <summary>DuelEngine.ResolveChainStack: the Chain is over — every per-link modifier ends (RESET_CHAIN).</summary>
        public static void OnChainEnded(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Resolving = null;
            st.Modifiers.Clear();
        }

        /// <summary>
        /// Before the LP change of an effect damage: a reflect modifier of the resolving link sends the damage to the other
        /// player, then a negate modifier for the player who takes it makes it 0. Only damage from the resolving link's own
        /// card (a damage whose source is known to be another card — Fire Princess, Skull Invitation — is not that link's).
        /// </summary>
        public static void ModifyEffectDamage(DuelEngine engine, ref DuelistState target, ref int amount,
            string sourceName, CardInstance source)
        {
            if (engine == null || target == null || amount <= 0) return;
            if (!s_state.TryGetValue(engine, out var st) || st.Modifiers.Count == 0) return;
            var link = st.Resolving;
            if (link == null || !engine.IsResolvingChain || !FromLink(link, sourceName, source)) return;
            var victim = target;
            var reflect = st.Modifiers.Find(m =>
                m.Kind == ModifierKind.Reflect && ReferenceEquals(m.Link, link) && ReferenceEquals(m.Player, victim));
            if (reflect != null)
            {
                var other = engine.OpponentOf(target);
                if (other != null)
                {
                    engine.Log($"{reflect.Source?.Name ?? "Effect"}: {other.Name} takes the {amount} damage instead of " +
                               $"{target.Name}.");
                    target = other;
                }
            }

            var taker = target;
            var negate = st.Modifiers.Find(m =>
                m.Kind == ModifierKind.Negate && ReferenceEquals(m.Link, link) && ReferenceEquals(m.Player, taker));
            if (negate != null)
            {
                engine.Log($"{negate.Source?.Name ?? "Effect"}: the {amount} effect damage to {taker.Name} is negated.");
                amount = 0;
            }
        }

        static bool FromLink(ChainLink link, string sourceName, CardInstance source)
        {
            if (link.Card == null) return false;
            if (source != null) return ReferenceEquals(source, link.Card);
            if (string.IsNullOrEmpty(sourceName) ||
                string.Equals(sourceName, "effect", StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(sourceName, link.Card.Name, StringComparison.Ordinal);
        }

        /// <summary>
        /// After the LP change of an effect damage of <paramref name="amount"/> to <paramref name="victim"/>: record it and
        /// queue "each time your opponent takes damage from a card effect" triggers (Dark Room of Nightmare) of the victim's
        /// opponent — not for damage from a card with the trigger card's own name.
        /// </summary>
        public static void AfterEffectDamage(DuelEngine engine, DuelistState victim, int amount, string sourceName,
            CardInstance source)
        {
            if (engine == null || victim == null || amount <= 0) return;
            var st = StateOf(engine);
            st.Last = new EffectDamageRecord
            {
                Victim = victim, Amount = amount, Source = source, SourceName = sourceName, Turn = engine.TurnNumber
            };
            if (engine.GameOver) return;
            var controller = engine.OpponentOf(victim);
            foreach (var (side, card, clause) in LiveCards(engine,
                         EffectActionKind.InflictDamageEachTimeOpponentTakesEffectDamage))
            {
                if (!ReferenceEquals(side, controller)) continue;
                if (SameName(card, sourceName, source)) continue;
                QueueBurn(engine, side, card, victim, clause.Amount > 0 ? clause.Amount : 300,
                    EffectActionKind.InflictDamageEachTimeOpponentTakesEffectDamage,
                    $"{victim.Name} took damage from a card effect");
            }
        }

        static bool SameName(CardInstance card, string sourceName, CardInstance source)
        {
            var name = card?.Def?.name;
            if (string.IsNullOrEmpty(name)) return false;
            if (source?.Def != null) return string.Equals(source.Def.name, name, StringComparison.Ordinal);
            return string.Equals(sourceName, name, StringComparison.Ordinal);
        }

        /// <summary>A mandatory trigger on the simultaneous-trigger Chain: Amount to the victim if the card still applies.</summary>
        static void QueueBurn(DuelEngine engine, DuelistState side, CardInstance card, DuelistState victim, int amount,
            EffectActionKind action, string why)
        {
            var stamp = card.FieldEntryStamp;
            SimultaneousTriggersRuntime.QueueTrigger(engine, side, card, optional: false, () =>
            {
                if (engine.GameOver) return;
                if (card.FieldEntryStamp != stamp || LiveClause(engine, side, card, action) == null)
                {
                    engine.Log($"{card.Name} no longer applies on the field — its effect does not resolve.");
                    return;
                }

                engine.Log($"{card.Name}: {why} — {amount} damage to {victim.Name}.");
                engine.ApplyEffectDamage(victim, amount, card.Name, card);
            });
        }

        // ───────────────────────────── live face-up cards ─────────────────────────────

        static EffectClause LiveClause(DuelEngine engine, DuelistState side, CardInstance st, EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || side == null) return null;
            if (!side.TryFindSpellTrap(st, out _)) return null;
            var prog = CompiledEffectCache.GetOrCompile(st);
            if (prog == null || !prog.FullyCompiled) return null;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
            if (clause == null) return null;
            // Its own activation is still an unresolved Chain Link: its effects do not apply yet.
            if (engine.Chain != null &&
                engine.Chain.Links.Any(l => l != null && ReferenceEquals(l.Card, st) && !l.Resolved))
                return null;
            return clause;
        }

        static List<(DuelistState side, CardInstance card, EffectClause clause)> LiveCards(DuelEngine engine,
            EffectActionKind action)
        {
            var list = new List<(DuelistState, CardInstance, EffectClause)>();
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField().ToList())
                {
                    var c = LiveClause(engine, side, st, action);
                    if (c != null) list.Add((side, st, c));
                }
            }

            return list;
        }

        // ───────────────────────────── sent to the opponent's GY ─────────────────────────────

        /// <summary>
        /// StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint: monster cards (not Tokens) that are in a GY now and were not at
        /// the last checkpoint were sent there since. Each Coffin Seller active at the last checkpoint and still active queues
        /// ONE trigger when its controller's opponent's GY received one or more of them (a mass send is one event). A newly
        /// active one starts watching from here. Does nothing while no Coffin Seller is active.
        /// </summary>
        public static void GySendCheckpoint(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var live = LiveCards(engine, EffectActionKind.InflictDamageEachTimeMonsterSentToOpponentGy);
            s_state.TryGetValue(engine, out var st);
            if (live.Count == 0 && (st == null || st.Watchers.Count == 0))
            {
                if (st != null) st.SeenValid = false;
                return;
            }

            st ??= StateOf(engine);
            var now = new Dictionary<DuelistState, HashSet<CardInstance>>
            {
                [engine.Player] = MonstersInGy(engine.Player),
                [engine.Opponent] = MonstersInGy(engine.Opponent)
            };
            if (st.SeenValid)
            {
                foreach (var w in st.Watchers)
                {
                    var still = live.Find(l => ReferenceEquals(l.card, w.Card) && ReferenceEquals(l.side, w.Side));
                    if (still.card == null || w.Card.FieldEntryStamp != w.Stamp) continue;
                    var opp = engine.OpponentOf(w.Side);
                    if (opp == null || !now.TryGetValue(opp, out var current)) continue;
                    st.SeenMonsters.TryGetValue(opp, out var seen);
                    if (!current.Any(c => seen == null || !seen.Contains(c))) continue;
                    QueueBurn(engine, w.Side, w.Card, opp, still.clause.Amount > 0 ? still.clause.Amount : 300,
                        EffectActionKind.InflictDamageEachTimeMonsterSentToOpponentGy,
                        $"a monster was sent to {opp.Name}'s GY");
                }
            }

            st.SeenMonsters.Clear();
            foreach (var kv in now)
                st.SeenMonsters[kv.Key] = kv.Value;
            st.SeenValid = true;
            st.Watchers.Clear();
            foreach (var (side, card, _) in live)
                st.Watchers.Add(new Watcher { Side = side, Card = card, Stamp = card.FieldEntryStamp });
        }

        static HashSet<CardInstance> MonstersInGy(DuelistState side)
        {
            var set = new HashSet<CardInstance>();
            if (side?.Graveyard == null) return set;
            foreach (var c in side.Graveyard)
                if (c?.Def != null && !c.IsToken && c.Def.IsMonster)
                    set.Add(c);
            return set;
        }

        // ───────────────────────────── battle damage as effect damage ─────────────────────────────

        /// <summary>The monster's own "battle damage it inflicts is effect damage" applies now (face-up, not negated).</summary>
        static bool ConvertsBattleDamage(CardInstance monster)
        {
            if (monster?.Def == null || !monster.FaceUp || monster.IsNegated) return false;
            var prog = CompiledEffectCache.GetOrCompile(monster);
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                       .Exists(c => c != null && c.Action == EffectActionKind.BattleDamageThisInflictsIsEffectDamage);
        }

        /// <summary>
        /// DuelEngine damage calculation, before the battle damage is computed (ygopro calculate_battle_damage): the card that
        /// inflicts the battle damage (the attacker for damage to the defending player unless it is piercing damage; the attack
        /// target for damage to the attacking player, including a Defense Position target's DEF − ATK) is affected by
        /// EFFECT_BATTLE_DAMAGE_TO_EFFECT → that damage is effect damage instead: no battle-damage change or prevention applies
        /// to it (the matching no-damage flag is cleared) and the caller deals it with ApplyEffectDamage (no battle-damage
        /// event). Out: the card that inflicts each converted damage, or null.
        /// </summary>
        public static void BattleDamageAsEffect(DuelEngine engine, CardInstance attacker, CardInstance target,
            bool piercing, ref bool noDamageToAttacker, ref bool noDamageToDefender,
            out CardInstance toDefendingPlayerBy, out CardInstance toAttackingPlayerBy)
        {
            toDefendingPlayerBy = null;
            toAttackingPlayerBy = null;
            if (engine == null || attacker == null) return;
            var attackerConverts = ConvertsBattleDamage(attacker);
            var targetConverts = target != null && ConvertsBattleDamage(target);
            if (!attackerConverts && !targetConverts) return;
            var raw = BattleMechanics.Calculate(attacker, target, piercing, false, false, false, false);
            BattleMechanics.Sanitize(ref raw, attacker, target);
            if (attackerConverts && raw.DamageToDefendingPlayer > 0 && !raw.PiercingApplied)
            {
                toDefendingPlayerBy = attacker;
                noDamageToDefender = false;
            }

            if (targetConverts && raw.DamageToAttackingPlayer > 0)
            {
                toAttackingPlayerBy = target;
                noDamageToAttacker = false;
            }
        }
    }
}
