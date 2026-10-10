using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleOutcomeTriggers" (v162). Atoms: <see cref="BattleOutcomeTriggersAtoms"/>.
    /// <list type="bullet">
    /// <item>Zombyra the Dark: when it destroys a monster by battle (TextEffectRuntime.NotifyDestroyedOpponentByBattle, both
    /// the attacker and the defender path) it loses 200 ATK for as long as it stays face-up on the field; negating its effects
    /// ends the loss for good (RESETS_STANDARD_DISABLE, <see cref="SpecialSummonEffectsRuntime.AddLastingStatChange"/>).</item>
    /// <item>Timeater: its opponent skips their next Main Phase 1 (<see cref="MainPhase1SkipPending"/>, read by
    /// DuelEngine.EnterMain1): from the Standby Phase the turn goes to the Battle Phase, or to the End Phase when no Battle
    /// Phase can be conducted.</item>
    /// <item>Kryuel: sent to the GY by battle, its controller calls a coin toss (a human through
    /// <see cref="PendingActivation.AwaitingCoinCall"/>; the toss is a CoinDiceInterrupts coin-toss event). A right call lets
    /// them select 1 monster the opponent controls (not a target, a Set one included) and destroy it.</item>
    /// <item>Mystical Knight of Jackal / Winged Sage Falcos: optional; the monster this card destroyed by battle, if it is in
    /// the opponent's GY (and, for Falcos, was in face-up Attack Position), goes on top of its owner's Deck. A human taps
    /// that card or CANCELs; the effect targets it (Necrovalley / "cannot be targeted" stop it).</item>
    /// <item>Dark Scorpion Burglars: battle damage to the opponent makes the opponent select 1 Spell in their Deck and send
    /// it to the GY (a human opponent picks in the target picker, the Deck is shuffled afterwards; a Deck send by an
    /// opponent's effect for DiscardTriggers). Picked while the battle's destruction waits (IsMonsterEffect).</item>
    /// <item>Mucus Yolk: battle damage flags it; during its controller's next Standby Phase it gains 1000 ATK once (however
    /// many times it inflicted damage), kept while it stays face-up and not negated (<see cref="OnStandbyPhase"/>).</item>
    /// </list>
    /// Every human pick goes through <see cref="PendingActivation"/> and the hooks <see cref="TryResolvePick"/>,
    /// <see cref="TryCancelPick"/> and <see cref="TryFinishCoinCall"/>; the AI's choices are made here at once. A trigger
    /// that needs a human pick while another prompt is open waits on the simultaneous-trigger queue.
    /// </summary>
    public static class BattleOutcomeTriggersRuntime
    {
        // ───────────────────────────── state ─────────────────────────────

        enum PickKind
        {
            KryuelCall,
            KryuelSelect,
            ReturnToDeckTop,
            SpellFromDeck
        }

        sealed class Pick
        {
            public PickKind Kind;
            /// <summary>The controller of the trigger.</summary>
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            /// <summary>Dark Scorpion Burglars: the player who sends the Spell (the pending's controller).</summary>
            public DuelistState Victim;
        }

        /// <summary>Mucus Yolk's flag on one field presence of the monster.</summary>
        sealed class StandbyFlag
        {
            public CardInstance Card;
            public int Entry;
            public int FaceDown;
            public int Amount;
        }

        sealed class State
        {
            /// <summary>Players who skip their next Main Phase 1 (Timeater).</summary>
            public readonly List<DuelistState> SkipMain1 = new();
            public readonly List<StandbyFlag> StandbyFlags = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Pick> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static string Shown(CardInstance c) => c == null ? "nothing" : c.FaceUp ? c.Name : "a Set monster";

        static void OpenPick(DuelEngine engine, PendingActivation p, Pick pick)
        {
            s_picks.Add(p, pick);
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
        }

        // ───────────────────────────── "If this card destroys a monster by battle" ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle: fire one of this batch's clauses of <paramref name="source"/>,
        /// which just destroyed <paramref name="destroyed"/> by battle and is still on the field. False when the clause is not
        /// this batch's. A face-down or negated monster does not activate its trigger.
        /// </summary>
        public static bool TryFireBattleDestroying(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, CardInstance destroyed)
        {
            if (engine == null || who == null || source == null || c == null) return false;
            var mine = c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp ||
                       c.Action == EffectActionKind.OpponentSkipsNextMainPhase1 ||
                       c.TargetIsMonsterDestroyedByThisBattle;
            if (!mine) return false;
            if (!source.FaceUp || source.IsNegated)
            {
                engine.Log($"{source.Name}'s effect is negated — it does not activate.");
                return true;
            }

            if (c.TargetIsMonsterDestroyedByThisBattle)
            {
                OfferReturnToDeckTop(engine, who, source, c, destroyed);
                return true;
            }

            var d = false;
            TextEffectRuntime.ApplyClause(engine, who, source, c, null, ref d, ref d);
            return true;
        }

        /// <summary>ApplyClause arm (Zombyra the Dark): this card gains / loses <see cref="EffectClause.Amount"/> ATK.</summary>
        internal static void ApplyThisCardAtkChange(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || source == null || clause == null) return;
            var controller = engine.ControllerOf(source);
            if (controller == null || !source.FaceUp || !controller.TryFindMonster(source, out _))
            {
                engine.Log($"{source.Name} is no longer face-up on the field — its ATK does not change.");
                return;
            }

            SpecialSummonEffectsRuntime.AddLastingStatChange(engine, source, clause.Amount, 0, endsOnNegation: true);
            engine.NotifyPublic();
            engine.Log(clause.Amount < 0
                ? $"{source.Name} loses {-clause.Amount} ATK (now {source.CurrentAtk})."
                : $"{source.Name} gains {clause.Amount} ATK (now {source.CurrentAtk}).");
        }

        // ───────────────────────────── Timeater ─────────────────────────────

        /// <summary>ApplyClause arm: <paramref name="victim"/> skips their next Main Phase 1.</summary>
        internal static void ScheduleMainPhase1Skip(DuelEngine engine, DuelistState victim, CardInstance source)
        {
            if (engine == null || victim == null) return;
            var st = StateOf(engine);
            if (!st.SkipMain1.Contains(victim)) st.SkipMain1.Add(victim);
            if (source != null)
                engine.Log($"{source.Name}: {victim.Name} skips their next Main Phase 1.");
        }

        /// <summary>DuelEngine.EnterMain1: <paramref name="who"/> skips this Main Phase 1 (ygopro EFFECT_SKIP_M1).</summary>
        public static bool MainPhase1SkipPending(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && s_state.TryGetValue(engine, out var st) && st.SkipMain1.Contains(who);

        /// <summary>DuelEngine.EnterMain1: the skip happened (the effect resets at that Main Phase 1).</summary>
        public static void ConsumeMainPhase1Skip(DuelEngine engine, DuelistState who)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st)) st.SkipMain1.Remove(who);
        }

        // ───────────────────────────── Kryuel ─────────────────────────────

        /// <summary>ApplyClause arm: Kryuel was sent to the GY by battle — its controller tosses a coin and calls it.</summary>
        internal static void StartKryuelCall(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            if (engine.IsHumanControlled(who))
            {
                if (engine.PendingActivation != null)
                {
                    // Another prompt is open: the call is asked once play allows (a mandatory trigger).
                    SimultaneousTriggersRuntime.QueueTrigger(engine, who, source, optional: false,
                        () => StartKryuelCall(engine, who, source));
                    return;
                }

                engine.Log($"{source.Name} was sent to the GY as a result of battle — {who.Name} tosses a coin and calls it.");
                OpenPick(engine, new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    AwaitingCoinCall = true,
                    PromptOverride = $"{source.Name}: call the coin toss — if you call it right, destroy 1 monster your " +
                                     "opponent controls."
                }, new Pick { Kind = PickKind.KryuelCall, Who = who, Source = source });
                return;
            }

            engine.Log($"{source.Name} was sent to the GY as a result of battle — {who.Name} tosses a coin and calls it.");
            TossKryuel(engine, who, source, callHeads: true);
        }

        static void TossKryuel(DuelEngine engine, DuelistState who, CardInstance source, bool callHeads)
        {
            CoinDiceInterruptsRuntime.TossCoin(engine, who, source, toss => toss == callHeads, toss =>
            {
                engine.Log($"{who.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(toss ? "Heads" : "Tails")}.");
                if (toss != callHeads)
                {
                    engine.Log($"{source.Name}: wrong call — no effect.");
                    engine.NotifyPublic();
                    return;
                }

                SelectKryuelVictim(engine, who, source);
            });
        }

        static void SelectKryuelVictim(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine.OpponentOf(who);
            var legal = opp?.MonstersOnField().Where(m => m != null).ToList() ?? new List<CardInstance>();
            if (legal.Count == 0)
            {
                engine.Log($"{source.Name}: right call, but {opp?.Name ?? "the opponent"} controls no monster.");
                engine.NotifyPublic();
                return;
            }

            if (legal.Count > 1 && engine.IsHumanControlled(who) && engine.PendingActivation == null)
            {
                var p = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    TargetKind = EffectTargetKind.OppMonster,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"{source.Name}: right call — select 1 monster your opponent controls to destroy " +
                                     "(it is not targeted)."
                };
                p.LegalTargets.AddRange(legal);
                OpenPick(engine, p, new Pick { Kind = PickKind.KryuelSelect, Who = who, Source = source });
                return;
            }

            DestroySelected(engine, who, source, legal.Count == 1 ? legal[0] : AiDestroyPick(legal));
        }

        static void DestroySelected(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            if (pick == null) return;
            var controller = engine.ControllerOf(pick);
            if (controller == null || !controller.TryFindMonster(pick, out _))
            {
                engine.Log($"{source.Name}: the selected monster is no longer on the field.");
                return;
            }

            engine.Log($"{source.Name}: {who.Name} selects {Shown(pick)} to destroy.");
            TextEffectRuntime.DestroyCard(engine, pick, source, false, who);
            engine.NotifyPublic();
        }

        /// <summary>The AI's selection: the strongest face-up monster; a Set one counts as 1000 ATK.</summary>
        static CardInstance AiDestroyPick(List<CardInstance> legal) =>
            legal.OrderByDescending(m => m.FaceUp ? m.CurrentAtk : 1000).First();

        // ───────────────────────────── Mystical Knight of Jackal / Winged Sage Falcos ─────────────────────────────

        static bool InOpponentGy(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var opp = engine.OpponentOf(who);
            return card?.Def != null && card.Def.IsMonster && opp?.Graveyard != null && opp.Graveyard.Contains(card);
        }

        static void OfferReturnToDeckTop(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance destroyed)
        {
            // "… and sends it to your opponent's Graveyard" (Lua bc:IsLocation(LOCATION_GRAVE) and bc:IsControler(1-tp)).
            if (!InOpponentGy(engine, who, destroyed)) return;
            // Falcos: bc:IsPreviousPosition(POS_FACEUP_ATTACK) — a monster in Attack Position is face-up.
            if (clause.RequiresDestroyedWasFaceUpAttack && destroyed.Position != BattlePosition.Attack) return;
            // The target must be able to go to the Deck (Necrovalley: it cannot be activated).
            if (ContinuousProtections.GyMovementLocked(engine, source, destroyed))
            {
                engine.Log($"{source.Name}: {destroyed.Name} cannot leave the GY — the effect cannot be activated.");
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                ReturnToDeckTop(engine, who, source, clause, destroyed);
                return;
            }

            if (engine.PendingActivation != null)
            {
                SimultaneousTriggersRuntime.QueueTrigger(engine, who, source, optional: true,
                    () => OfferReturnToHuman(engine, who, source, clause, destroyed));
                return;
            }

            OfferReturnToHuman(engine, who, source, clause, destroyed);
        }

        static void OfferReturnToHuman(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance destroyed)
        {
            if (!InOpponentGy(engine, who, destroyed))
            {
                engine.Log($"{source.Name}: {destroyed?.Name} is no longer in the GY.");
                return;
            }

            var p = new PendingActivation
            {
                Controller = who,
                Card = source,
                TargetKind = EffectTargetKind.MonsterInEitherGy,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                PromptOverride = $"{source.Name}: tap {destroyed.Name} to place it on top of your opponent's Deck, or CANCEL."
            };
            p.LegalTargets.Add(destroyed);
            OpenPick(engine, p, new Pick { Kind = PickKind.ReturnToDeckTop, Who = who, Source = source, Clause = clause });
        }

        static void ReturnToDeckTop(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance destroyed)
        {
            if (!InOpponentGy(engine, who, destroyed))
            {
                engine.Log($"{source.Name}: {destroyed?.Name} is no longer in the GY — no effect.");
                return;
            }

            engine.Log($"{source.Name} activates its effect: {destroyed.Name} goes on top of its owner's Deck.");
            TextEffectRuntime.ApplyChosenClause(engine, who, source, clause, destroyed);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Dark Scorpion Burglars ─────────────────────────────

        /// <summary>ApplyClause arm: the opponent of <paramref name="who"/> sends 1 Spell from their Deck to the GY.</summary>
        internal static void StartOpponentSpellSend(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            var victim = engine.OpponentOf(who);
            var legal = SpellsInDeck(engine, victim);
            if (legal.Count == 0)
            {
                engine.Log($"{source.Name}: {victim?.Name ?? "the opponent"} has no Spell Card in their Deck.");
                return;
            }

            if (legal.Count > 1 && engine.IsHumanControlled(victim))
            {
                if (engine.PendingActivation != null)
                {
                    SimultaneousTriggersRuntime.QueueTrigger(engine, who, source, optional: false,
                        () => StartOpponentSpellSend(engine, who, source));
                    return;
                }

                var p = new PendingActivation
                {
                    Controller = victim,
                    Card = source,
                    TargetKind = EffectTargetKind.CardInYourDeck,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"{source.Name}: select 1 Spell Card in your Deck to send to the GY (then your Deck " +
                                     "is shuffled)."
                };
                p.LegalTargets.AddRange(legal);
                OpenPick(engine, p, new Pick { Kind = PickKind.SpellFromDeck, Who = who, Source = source, Victim = victim });
                return;
            }

            SendSpell(engine, who, victim, source, legal.Count == 1 ? legal[0] : AiSpellToSend(victim, legal));
        }

        /// <summary>One card per distinct Spell in <paramref name="owner"/>'s Deck.</summary>
        static List<CardInstance> SpellsInDeck(DuelEngine engine, DuelistState owner)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in owner.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null || !def.IsSpell) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        /// <summary>
        /// The AI sends its least useful Spell: one it cannot play (not Implemented) first, then the one with the most copies
        /// left in its Deck.
        /// </summary>
        static CardInstance AiSpellToSend(DuelistState owner, List<CardInstance> legal) =>
            legal.OrderBy(c => CardEffectStatus.Classify(c.Def) == CardEffectStatusKind.Implemented ? 1 : 0)
                .ThenByDescending(c => owner.Deck.Count(id => id == c.CardId))
                .First();

        static void SendSpell(DuelEngine engine, DuelistState who, DuelistState victim, CardInstance source,
            CardInstance pick)
        {
            if (pick == null || victim?.Deck == null) return;
            var idx = victim.Deck.IndexOf(pick.CardId);
            if (idx < 0)
            {
                engine.Log($"{source.Name}: {pick.Name} is no longer in the Deck.");
                return;
            }

            // PerCopyDeck batch (v163): the pick was made before the removal; it takes the copy's owner.
            PerCopyDeckRuntime.TakeFromDeck(victim, idx, pick);
            // DiscardTriggers: a card sent from the Deck to the GY by the effect of a card the victim's opponent controls.
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                if (RedirectAndReplaceRuntime.PutInGraveyard(engine, victim, pick))
                    engine.Log($"{source.Name}: {victim.Name} sends {pick.Name} from the Deck to the GY.");
                DiscardTriggersRuntime.NoteSentFromDeck(engine, victim, pick, who);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            engine.Rng.Shuffle(victim.Deck);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Mucus Yolk ─────────────────────────────

        /// <summary>ApplyClause arm: flag this card for its controller's next Standby Phase (one flag however many hits).</summary>
        internal static void FlagNextStandbyGain(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || source == null || clause == null) return;
            var st = StateOf(engine);
            if (st.StandbyFlags.Any(f => ReferenceEquals(f.Card, source) && f.Entry == source.FieldEntryStamp &&
                                         f.FaceDown == source.FaceDownStamp))
                return;
            st.StandbyFlags.Add(new StandbyFlag
            {
                Card = source,
                Entry = source.FieldEntryStamp,
                FaceDown = source.FaceDownStamp,
                Amount = clause.Amount
            });
            engine.Log($"{source.Name}: it gains {clause.Amount} ATK during its controller's next Standby Phase.");
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggers (Standby Phase of <paramref name="turnPlayer"/>): each flagged monster they
        /// control that is still the same face-up presence gains its ATK once (mandatory; not while negated); the flag ends
        /// with this Standby Phase (RESET_PHASE+PHASE_STANDBY+RESET_SELF_TURN). A flag whose monster left the field or was
        /// turned face-down is dropped (RESETS_STANDARD).
        /// </summary>
        public static void OnStandbyPhase(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null || !s_state.TryGetValue(engine, out var st)) return;
            for (var i = st.StandbyFlags.Count - 1; i >= 0; i--)
            {
                var f = st.StandbyFlags[i];
                var c = f.Card;
                var controller = c != null ? engine.ControllerOf(c) : null;
                var present = controller != null && controller.TryFindMonster(c, out _) &&
                              c.FieldEntryStamp == f.Entry && c.FaceDownStamp == f.FaceDown;
                if (!present)
                {
                    st.StandbyFlags.RemoveAt(i);
                    continue;
                }

                if (!ReferenceEquals(controller, turnPlayer)) continue;
                st.StandbyFlags.RemoveAt(i);
                if (!c.FaceUp || c.IsNegated)
                {
                    engine.Log($"{c.Name}'s effect is negated — it gains no ATK this Standby Phase.");
                    continue;
                }

                SpecialSummonEffectsRuntime.AddLastingStatChange(engine, c, f.Amount, 0, endsOnNegation: true);
                engine.Log($"{c.Name} gains {f.Amount} ATK while it stays face-up on the field.");
            }
        }

        // ───────────────────────────── pick hooks ─────────────────────────────

        /// <summary>TextEffectRuntime.TryFinishCoinCall hook: Kryuel's call. False when the open call is not this batch's.</summary>
        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick) || pick.Kind != PickKind.KryuelCall) return false;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            TossKryuel(engine, pick.Who, pick.Source, callHeads);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>SpellTrapEffects.TryResolveWithTarget hook. False when the open pick is not this batch's.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick)) return false;
            if (pick.Kind == PickKind.KryuelCall) return false; // a coin call, answered by DuelEngine.TrySelectCoinCall
            var chosen = engine.ResolveLegalEffectTarget(target);
            if (chosen == null || !p.LegalTargets.Contains(chosen))
            {
                engine.Log("Choose one of the highlighted cards.");
                return true;
            }

            s_picks.Remove(p);
            engine.ClearPendingActivation();
            ok = true;
            switch (pick.Kind)
            {
                case PickKind.KryuelSelect:
                    DestroySelected(engine, pick.Who, pick.Source, chosen);
                    break;
                case PickKind.ReturnToDeckTop:
                    ReturnToDeckTop(engine, pick.Who, pick.Source, pick.Clause, chosen);
                    break;
                case PickKind.SpellFromDeck:
                    SendSpell(engine, pick.Who, pick.Victim, pick.Source, chosen);
                    break;
            }

            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook: Kryuel's call and selection and Burglars' Spell are mandatory (made for the
        /// player: Heads, the strongest monster, the least useful Spell); CANCEL declines Jackal's / Falcos' effect.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick)) return false;
            switch (pick.Kind)
            {
                case PickKind.KryuelCall:
                    engine.Log($"{pick.Source?.Name}: the call is mandatory — Heads is called.");
                    return TryFinishCoinCall(engine, callHeads: true);
                case PickKind.KryuelSelect:
                {
                    var auto = AiDestroyPick(p.LegalTargets.ToList());
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    engine.Log($"{pick.Source?.Name}: the destruction is mandatory — {Shown(auto)} is selected.");
                    DestroySelected(engine, pick.Who, pick.Source, auto);
                    return true;
                }
                case PickKind.SpellFromDeck:
                {
                    var auto = AiSpellToSend(pick.Victim, p.LegalTargets.ToList());
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    engine.Log($"{pick.Source?.Name}: the send is mandatory — {auto.Name} is selected.");
                    SendSpell(engine, pick.Who, pick.Victim, pick.Source, auto);
                    return true;
                }
                default:
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    engine.Log($"{pick.Source?.Name}: {pick.Who?.Name} does not use its effect.");
                    engine.NotifyPublic();
                    return true;
            }
        }
    }
}
