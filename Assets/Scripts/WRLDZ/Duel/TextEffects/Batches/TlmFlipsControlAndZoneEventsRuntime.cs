using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmFlipsControlAndZoneEvents" runtime (<see cref="TlmFlipsControlAndZoneEventsAtoms"/>):
    /// <list type="bullet">
    /// <item>White Ninja: <see cref="EffectZoneFilter.FieldDefensePositionMonsters"/> (TextEffectRuntime.CollectTargets,
    /// the Destroy arm's Defense re-check) and the AI's pick <see cref="AutoPickDefender"/>.</item>
    /// <item>Dummy Golem: <see cref="ApplyOpponentSelectsSwitchWithThis"/> — the opponent selects at resolution
    /// (ContinuousTrapsAndSpiritSupportRuntime.AskCardPick with the opponent as chooser), then DuelEngine.TrySwapControl.</item>
    /// <item>Criosphinx: DuelEngine.ReturnCardToHand records each monster that goes from the field to its owner's hand
    /// (<see cref="NoteMonsterReturnedToHand"/>); TextEffectRuntime.NotifyMonstersReturnedToOwnerHand (once per return
    /// event, group returns batched) fires one mandatory trigger per face-up Criosphinx (<see cref="OnMonstersReturnedToHand"/>):
    /// after the current Chain (SimultaneousTriggersRuntime.TryDeferTriggerPastChain), as a Chain Link
    /// (TriggerChainResponsesRuntime), or inline.</item>
    /// <item>D.D. Survivor: DuelEngine's banish paths record a copy banished face-up from its owner's field
    /// (<see cref="NoteBanished"/>); BanishedReturnRuntime (D.D. Scout Plane's End Phase return) reads it through
    /// <see cref="EffectClause.RequiresBanishedFaceUpFromOwnField"/> and asks a human owner the face-up position
    /// (<see cref="TryAskSurvivorPosition"/>).</item>
    /// </list>
    /// </summary>
    public static class TlmFlipsControlAndZoneEventsRuntime
    {
        sealed class State
        {
            /// <summary>Hand owners who got a monster back from the field since the last return event closed.</summary>
            public readonly List<DuelistState> ReturnedTo = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>Turn in which a copy was last banished face-up from its owner's field (D.D. Survivor's flag).</summary>
        static readonly ConditionalWeakTable<CardInstance, StrongBox<int>> s_banishedFaceUpFromOwnField = new();

        // ───────────────────────────── White Ninja ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.AutoPick for <see cref="EffectZoneFilter.FieldDefensePositionMonsters"/>: the opponent's
        /// strongest defender first; with none, the AI's own least valuable one (the FLIP effect must take a target).
        /// </summary>
        public static CardInstance AutoPickDefender(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var opp = engine?.OpponentOf(who);
            var theirs = opp == null
                ? null
                : targets.Where(t => opp.TryFindMonster(t, out _))
                    .OrderByDescending(t => Math.Max(t.Def?.atk ?? 0, t.Def?.def ?? 0))
                    .FirstOrDefault();
            return theirs ?? targets.OrderBy(t => Math.Max(t.Def?.atk ?? 0, t.Def?.def ?? 0)).First();
        }

        // ───────────────────────────── Dummy Golem ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.OpponentSelectsOwnMonsterSwitchControlWithThis"/> (Lua s.operation): this card must
        /// still be on <paramref name="who"/>'s field, not about to be destroyed by the battle that flipped it, and able to
        /// change control; the opponent must control a monster that can. The opponent then selects 1 of their monsters (any
        /// position; not a target) and control of it and this card switch.
        /// </summary>
        public static void ApplyOpponentSelectsSwitchWithThis(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            var name = source.Name;
            if (!who.TryFindMonster(source, out _))
            {
                engine.Log($"{name}: no longer on the field — control does not switch.");
                return;
            }

            if (engine.FlipSourceBattleDestroyPending == source || engine.IsBattleDestroyPending(source))
            {
                engine.Log($"{name}: destroyed by this battle — control does not switch.");
                return;
            }

            var opp = engine.OpponentOf(who);
            if (opp == null || MonsterNegationAndControlLocksRuntime.ControlLocked(engine, source))
            {
                engine.Log($"{name}: its control cannot switch.");
                return;
            }

            List<CardInstance> Candidates() => who.TryFindMonster(source, out _)
                ? opp.MonstersOnField()
                    .Where(m => m != null && !MonsterNegationAndControlLocksRuntime.ControlLocked(engine, m))
                    .ToList()
                : new List<CardInstance>();

            if (Candidates().Count == 0)
            {
                engine.Log($"{name}: {opp.Name} controls no monster whose control can switch.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, opp, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.YourMonster,
                Prompt = $"your opponent's {name} — select 1 monster you control; you take control of {name} and " +
                         "they take control of the selected monster.",
                // The AI gives away its least valuable monster.
                AiChoose = list => list.OrderBy(m => m.FaceUp ? m.CurrentAtk : m.Def?.atk ?? 0)
                    .ThenBy(m => m.Def?.level ?? 0)
                    .FirstOrDefault(),
                Finish = chosen => FinishSwitch(engine, who, opp, source, chosen)
            });
        }

        static void FinishSwitch(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source,
            CardInstance chosen)
        {
            if (chosen == null) return;
            if (!who.TryFindMonster(source, out _) || !opp.TryFindMonster(chosen, out _))
            {
                engine.Log($"{source.Name}: a monster left its zone — control does not switch.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, chosen, source))
            {
                engine.Log($"{chosen.Name} is unaffected by {source.Name} — control does not switch.");
                return;
            }

            if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, chosen) ||
                MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, source))
                return;

            engine.Log($"{source.Name}: {opp.Name} selects {chosen.Name}.");
            if (!engine.TrySwapControl(source, chosen))
                engine.Log($"{source.Name}: switch control failed.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Criosphinx ─────────────────────────────

        /// <summary>
        /// DuelEngine.ReturnCardToHand: <paramref name="card"/> went from a Monster Zone to <paramref name="handOwner"/>'s
        /// hand (its owner; a Token or an Extra Deck monster never gets here). Read once the return event closes.
        /// </summary>
        public static void NoteMonsterReturnedToHand(DuelEngine engine, DuelistState handOwner, CardInstance card)
        {
            if (engine == null || handOwner == null || card?.Def == null || !card.Def.IsMonster) return;
            var st = StateOf(engine);
            if (!st.ReturnedTo.Contains(handOwner)) st.ReturnedTo.Add(handOwner);
        }

        static bool IsLiveCriosphinx(DuelEngine engine, CardInstance m)
        {
            if (m?.Def == null || !m.FaceUp || m.IsNegated) return false;
            if (ContinuousNegationRuntime.NegatesMonsterEffectOnField(engine, m)) return false;
            var prog = CompiledEffectCache.GetOrCompile(m);
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                       c != null && c.Action == EffectActionKind.OwnersOfMonstersReturnedToHandSendOneHandCardToGy);
        }

        /// <summary>
        /// TextEffectRuntime.NotifyMonstersReturnedToOwnerHand (once per return event): every face-up Criosphinx on the
        /// field raises one mandatory trigger for the players who got a monster back (Lua RaiseSingleEvent with ep = that
        /// player, or PLAYER_ALL). The triggers of one event go on one Chain (TriggerChainResponsesRuntime collect).
        /// </summary>
        public static void OnMonstersReturnedToHand(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.ReturnedTo.Count == 0) return;
            var turn = engine.TurnPlayer;
            var owners = st.ReturnedTo.OrderBy(p => ReferenceEquals(p, turn) ? 0 : 1).ToList();
            st.ReturnedTo.Clear();
            if (engine.GameOver) return;
            TriggerChainResponsesRuntime.BeginCollect(engine);
            try
            {
                foreach (var side in new[] { turn, engine.OpponentOf(turn) })
                {
                    if (side == null) continue;
                    foreach (var m in side.MonstersOnField().ToList())
                    {
                        if (engine.GameOver) return;
                        if (!IsLiveCriosphinx(engine, m)) continue;
                        var ctl = side;
                        var src = m;
                        var stamp = m.FieldEntryStamp;
                        Action run = () => ResolveCriosphinx(engine, ctl, src, stamp, owners);
                        if (SimultaneousTriggersRuntime.TryDeferTriggerPastChain(engine, ctl, src, run)) continue;
                        var seat = new TriggerChainResponsesRuntime.Seat
                        {
                            Controller = ctl,
                            Card = src,
                            From = Rules.CardLocation.MonsterZone,
                            Resolve = run
                        };
                        if (TriggerChainResponsesRuntime.TrySeatOrCollect(engine, seat, run)) continue;
                        run();
                    }
                }
            }
            finally
            {
                TriggerChainResponsesRuntime.EndCollect(engine);
            }
        }

        /// <summary>
        /// Lua s.hdop: only while this Criosphinx is still face-up on the field (the same copy; its effect not negated),
        /// each player in <paramref name="owners"/> sends 1 card of their choice from their hand to the GY, one after the
        /// other.
        /// </summary>
        static void ResolveCriosphinx(DuelEngine engine, DuelistState ctl, CardInstance src, int stamp,
            List<DuelistState> owners)
        {
            if (engine == null || engine.GameOver || src == null) return;
            if (engine.ControllerOf(src) == null || src.FieldEntryStamp != stamp || !src.FaceUp)
            {
                engine.Log($"{src.Name} is no longer face-up on the field — no card is sent.");
                return;
            }

            if (src.IsNegated || ContinuousNegationRuntime.NegatesMonsterEffectOnField(engine, src))
            {
                engine.Log($"{src.Name}'s effect is negated.");
                return;
            }

            var by = engine.ControllerOf(src) ?? ctl;
            SendNext(engine, src, by, owners, 0);
        }

        static void SendNext(DuelEngine engine, CardInstance src, DuelistState by, List<DuelistState> owners, int i)
        {
            if (engine.GameOver || i >= owners.Count) return;
            var who = owners[i];
            if (who?.Hand == null || who.Hand.Count(h => h != null) == 0)
            {
                engine.Log($"{src.Name}: {who?.Name} has no card in hand to send to the GY.");
                SendNext(engine, src, by, owners, i + 1);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, src, new CardPickRequest
            {
                Candidates = () => who.Hand.Where(h => h != null).ToList(),
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"{src.Name}: send 1 card from your hand to the GY.",
                AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, null) ?? list.FirstOrDefault(),
                Finish = pick =>
                {
                    SendFromHand(engine, who, pick, by, src);
                    SendNext(engine, src, by, owners, i + 1);
                }
            });
        }

        static void SendFromHand(DuelEngine engine, DuelistState who, CardInstance pick, DuelistState by,
            CardInstance src)
        {
            if (pick == null || !who.Hand.Contains(pick)) return;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                var gone = MonsterEffects.SendFromHandToGraveByInstance(who, pick);
                if (gone == null) return;
                engine.Log($"{src.Name}: {who.Name} sends {gone.Name} from the hand to the GY.");
                // A send by a card effect, not a discard (Lua REASON_EFFECT only): "sent from the hand to the GY by your
                // opponent's card effect" triggers read it; discard triggers do not.
                DiscardTriggersRuntime.NoteSentFromHand(engine, who, gone, by);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── D.D. Survivor ─────────────────────────────

        /// <summary>
        /// DuelEngine's banish paths (BanishCard, a destroyed monster banished instead, a GY send banished instead):
        /// <paramref name="card"/> was just put in <paramref name="pile"/>'s banished pile, leaving
        /// <paramref name="holder"/>'s field (a Monster Zone or, as an Equip Card, a Spell &amp; Trap Zone) face-up when <paramref name="faceUpFromField"/>. D.D. Survivor's flag
        /// (Lua rmop): banished face-up, from the field, face-up there, controlled by its owner. Any other banish clears it.
        /// </summary>
        public static void NoteBanished(DuelEngine engine, DuelistState holder, DuelistState pile, CardInstance card,
            bool faceUpFromField)
        {
            if (engine == null || card == null) return;
            if (faceUpFromField && card.FaceUp && holder != null && ReferenceEquals(holder, pile))
                s_banishedFaceUpFromOwnField.GetOrCreateValue(card).Value = engine.TurnNumber;
            else
                s_banishedFaceUpFromOwnField.Remove(card);
        }

        /// <summary>BanishedReturnRuntime.Qualifies: D.D. Survivor's flag is set this turn and the card is still banished.</summary>
        public static bool BanishedFaceUpFromOwnFieldThisTurn(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && card.FaceUp && card.BanishedTurnNumber == engine.TurnNumber &&
            s_banishedFaceUpFromOwnField.TryGetValue(card, out var turn) && turn.Value == engine.TurnNumber;

        /// <summary>
        /// BanishedReturnRuntime.Resolve for a POS_FACEUP return (D.D. Survivor), with a free Monster Zone: a human owner
        /// taps the card and chooses face-up Attack or Defense (and the zone). The summon is mandatory: Cancel summons it in
        /// Attack Position. False for the AI (the caller summons it in Attack Position).
        /// </summary>
        public static bool TryAskSurvivorPosition(DuelEngine engine, DuelistState owner, CardInstance card)
        {
            if (engine == null || owner == null || card == null || !engine.IsHumanControlled(owner)) return false;
            // The caller took it out of the banished pile: it stays there until the answer.
            if (!owner.Banished.Contains(card)) owner.Banished.Add(card);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, owner, card, new CardPickRequest
            {
                Candidates = () => owner.Banished.Contains(card)
                    ? new List<CardInstance> { card }
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInYourBanished,
                Prompt = $"{card.Name}: Special Summon it from the banished pile — tap it, then Summon ATK or Summon DEF " +
                         "(face-up).",
                AskWithOneCandidate = true,
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.FirstOrDefault(),
                Finish = pick =>
                {
                    var pos = engine.PendingSpecialSummonPosition ?? BattlePosition.Attack;
                    var zone = engine.PendingSpecialSummonZone;
                    engine.ClearPendingSpecialSummonPlacement();
                    SummonSurvivor(engine, owner, card, pos, zone);
                }
            });
            return true;
        }

        static void SummonSurvivor(DuelEngine engine, DuelistState owner, CardInstance card, BattlePosition pos,
            int zone)
        {
            if (!owner.Banished.Contains(card)) return;
            owner.Banished.Remove(card);
            if (engine.FirstEmpty(owner.MonsterZones) < 0)
            {
                engine.Log($"{card.Name}: no free Monster Zone — sent to the GY.");
                engine.SendCardToGrave(owner, card, sentBy: card);
                return;
            }

            DuelEngine.RecordOwnerOnFieldEntry(card, owner, owner);
            if (!engine.SpecialSummonToField(owner, card, pos, true, Rules.SummonKind.SpecialSummon, zone))
            {
                card.ControlOwner = null;
                card.FaceUp = true;
                if (!owner.Banished.Contains(card)) owner.Banished.Add(card);
                engine.Log($"{card.Name} cannot be Special Summoned — it stays banished.");
                return;
            }

            engine.Log($"{card.Name} returns from banishment: Special Summoned in face-up " +
                       (pos == BattlePosition.Defense ? "Defense" : "Attack") + " Position.");
            engine.NotifyPublic();
        }
    }
}
