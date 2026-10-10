using System;
using System.Collections.Generic;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// The effect of a compiled Spell/Trap activation, recorded while the card was activated and applied when its
    /// Chain Link resolves (<see cref="CompiledActivationChainRuntime"/>, <see cref="ChainLink.DeferredEffect"/>).
    /// </summary>
    public sealed class DeferredActivationEffect
    {
        internal readonly List<Step> Steps = new();

        /// <summary>Costs are paid and every target is chosen: the link waits for responses, then resolves.</summary>
        public bool Complete { get; internal set; }

        /// <summary>Effect steps recorded (clauses, second targets, the card's own finish).</summary>
        public int StepCount => Steps.Count;

        internal sealed class Step
        {
            /// <summary>The card the step applies to (null: the step has no target).</summary>
            public CardInstance Target;
            /// <summary>
            /// A chosen card, listed on the Chain Link as its target when it is public (field, GY, banished), like the
            /// pick of a legacy chain activation (Monster Reborn). Not a choice that does not target (Fissure).
            /// </summary>
            public bool IsTarget;
            /// <summary>An equip: an Equip Spell that equipped nothing at resolution is sent to the GY.</summary>
            public bool Equips;
            /// <summary>The target must still be face-up (Equip Spells, "face-up monster" targets).</summary>
            public bool NeedsFaceUp;
            public Where At;
            /// <summary>
            /// A choice that does not target (Fissure: the face-up monster with the lowest ATK): made again from the
            /// cards that qualify at resolution, keeping the activation's pick while it still qualifies.
            /// </summary>
            public Func<CardInstance, CardInstance> Rechoose;
            public Action<CardInstance> Apply;
        }

        internal enum Pile { Untracked, Monster, SpellTrap, FieldSpell, Hand, Graveyard, Banished }

        internal struct Where
        {
            public Pile Pile;
            public DuelistState Side;
            public int EntryStamp;
            public int FaceDownStamp;
            public bool FaceUp;
        }
    }

    /// <summary>
    /// Compiled Spell/Trap activations become a real Chain Link. A card whose Activate clauses need the compiled
    /// cost / target flow (<c>SpellTrapEffects.UsesCompiledActivationPending</c>: discard and Tribute costs, second
    /// targets, lowest/highest ATK, take control, every Equip Spell, Magical Hats, Crush Card Virus, Soul Exchange,
    /// multi-target and opponent-GY clauses) used to run that flow through
    /// <see cref="TextEffectRuntime.TryResolveActivation"/> at activation and resolve on the spot, so the opponent
    /// never got a chain window (Magic Jammer never answered Brain Control). Now:
    /// <list type="bullet">
    /// <item><see cref="Activate"/> runs the same flow (<c>SpellTrapEffects.BeginDeferredChainActivation</c>), so costs
    /// are paid and targets chosen exactly as before: the player's cost / target prompts, Cancel and its refunds, the
    /// AI's auto-picks. While it runs for the seated link, the effect calls for that card (<c>ApplyClause</c>,
    /// <c>ApplySecondTarget</c>, <c>FinishSpellTrap</c>, the targeting notifications, Magical Hats) are recorded on
    /// the link instead of applied. <see cref="Continue"/> does the same for each of the player's picks
    /// (<see cref="DuelEngine.TrySelectEffectTarget"/>, <see cref="DuelEngine.CancelEffectTargeting"/>).</item>
    /// <item>Once nothing is left to choose, the card sits face-up on the field as the Chain Link, its targets are on
    /// the link, and the opponent's chain window opens (or the chain resolves when nobody can respond).</item>
    /// <item><see cref="TryResolveLink"/> (<c>DuelEngine.ResolveChainStack</c>) applies the recorded steps when the link
    /// resolves. A negated link never gets there; its costs stay paid. A target that left its zone (or, for a
    /// face-up target, was turned face-down) since it was chosen is not affected, and an Equip Spell with nothing to
    /// equip goes to the GY. The LP cost Brain Control pays in <c>StartIgnition</c> is paid once, at activation.</item>
    /// </list>
    /// Face-up ignitions (Magical Labyrinth's Tribute) and response-window activations keep their old paths.
    /// </summary>
    public static class CompiledActivationChainRuntime
    {
        /// <summary>The link whose activation flow is running right now (recording its effect).</summary>
        [ThreadStatic] static ChainLink s_recording;

        /// <summary>The activation of <paramref name="card"/> is being recorded: its effect waits for its link.</summary>
        public static bool IsRecording(CardInstance card) =>
            card != null && s_recording != null && ReferenceEquals(s_recording.Card, card);

        /// <summary>An activation is being recorded (callers without a source card: the targeting notifications).</summary>
        public static bool IsRecordingAny => s_recording != null;

        /// <summary>
        /// Activates <paramref name="card"/> through the compiled flow as the Chain Link that
        /// <see cref="DuelEngine.TryActivateSpellTrap"/> seated for it. Returns what
        /// <see cref="TextEffectRuntime.TryResolveActivation"/> returns.
        /// </summary>
        public static bool Activate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            CompiledCardProgram prog, bool autoPickTarget)
        {
            var link = engine?.Chain?.Last;
            if (!CanRecord(engine, who, card, fromHand, link))
                return TextEffectRuntime.TryResolveActivation(engine, who, card, fromHand, prog, autoPickTarget);

            link.DeferredEffect = new DeferredActivationEffect();
            bool ok;
            s_recording = link;
            try
            {
                ok = TextEffectRuntime.TryResolveActivation(engine, who, card, fromHand, prog, autoPickTarget);
            }
            finally
            {
                s_recording = null;
            }

            if (!ok)
            {
                link.DeferredEffect = null;
                return false;
            }

            AfterStep(engine, link);
            return true;
        }

        /// <summary>
        /// Runs one of the player's choices (<paramref name="step"/>: a cost or target pick, or Cancel) for the
        /// activation still being made on the last Chain Link, recording its effect; once nothing is left to
        /// choose, the opponent may respond. Any other choice just runs.
        /// </summary>
        public static bool Continue(DuelEngine engine, Func<bool> step)
        {
            var link = engine?.Chain?.Last;
            var d = link?.DeferredEffect;
            if (d == null || d.Complete || s_recording != null || engine.IsResolvingChain)
                return step();
            bool ok;
            s_recording = link;
            try
            {
                ok = step();
            }
            finally
            {
                s_recording = null;
            }

            AfterStep(engine, link);
            return ok;
        }

        /// <summary>
        /// Records one effect step of the activation being made (see <see cref="IsRecording"/>). The target's place
        /// is noted now; at resolution a target that moved is not affected.
        /// </summary>
        internal static void Record(DuelEngine engine, CardInstance target, bool isTarget, bool equips,
            bool needsFaceUp, Func<CardInstance, CardInstance> rechoose, Action<CardInstance> apply)
        {
            var d = s_recording?.DeferredEffect;
            if (d == null || apply == null) return;
            d.Steps.Add(new DeferredActivationEffect.Step
            {
                Target = target,
                IsTarget = isTarget,
                Equips = equips,
                NeedsFaceUp = needsFaceUp,
                At = Locate(engine, target),
                Rechoose = rechoose,
                Apply = apply
            });
        }

        /// <summary>
        /// EngineResiduals (v163): a redirect (Fairy's Hand Mirror, Shift: RedirectAndReplaceRuntime.ResolveRedirect) moved
        /// <paramref name="link"/>'s single target. The recorded steps that targeted <paramref name="oldTarget"/> now apply to
        /// <paramref name="newTarget"/>, whose place is noted now (it must still be there, face-up if required, when the
        /// link resolves).
        /// </summary>
        internal static void Retarget(DuelEngine engine, ChainLink link, CardInstance oldTarget, CardInstance newTarget)
        {
            var d = link?.DeferredEffect;
            if (engine == null || d == null || oldTarget == null || newTarget == null) return;
            foreach (var s in d.Steps)
            {
                if (s == null || !s.IsTarget || !ReferenceEquals(s.Target, oldTarget)) continue;
                s.Target = newTarget;
                s.At = Locate(engine, newTarget);
            }
        }

        /// <summary>
        /// Resolves <paramref name="link"/> when its compiled activation was recorded: applies the steps in order.
        /// Every target is checked first, against the board as the link starts resolving. Returns false for any
        /// other link.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            var d = link?.DeferredEffect;
            if (engine == null || d == null || !d.Complete) return false;
            link.DeferredEffect = null;
            var card = link.Card;
            var who = link.Controller;
            var targets = new CardInstance[d.Steps.Count];
            for (var i = 0; i < d.Steps.Count; i++)
            {
                var s = d.Steps[i];
                if (s.Target == null) continue;
                var still = StillThere(engine, s);
                var t = s.Rechoose != null ? s.Rechoose(still ? s.Target : null) : still ? s.Target : null;
                if (t == null)
                    engine.Log($"{card?.Name}: {s.Target.Name} is no longer a legal target — not affected.");

                targets[i] = t;
            }

            for (var i = 0; i < d.Steps.Count; i++)
            {
                var s = d.Steps[i];
                if (s.Target != null && targets[i] == null) continue;
                s.Apply(targets[i]);
            }

            // An Equip Spell that equipped nothing does not stay on the field. (A Continuous Trap that links to its
            // target, such as Battle-Scarred, stays face-up without one, as in ygopro's persistent procedure.)
            // EquipAndSpellProtection batch: nor does a Normal Trap that equips itself (Blast with Chain, Lua
            // c:CancelToGrave(false) when its target is gone).
            if (card?.Def != null && who != null &&
                (card.Def.IsEquipSpell ||
                 (card.Def.IsTrap && !card.Def.IsContinuousSpellOrTrap && TextEffectRuntime.IsRealEquipCard(card))) &&
                HasEquipStep(d) &&
                card.EquippedTo == null && who.TryFindSpellTrap(card, out _))
            {
                engine.Log($"{card.Name} has no monster to equip — sent to the GY.");
                engine.SendCardToGrave(who, card);
            }

            return true;
        }

        // ───────────────────────────── internals ─────────────────────────────

        /// <summary>
        /// Only the card's own activation from the hand or a Set, seated by <see cref="DuelEngine.TryActivateSpellTrap"/>
        /// as the last link and not resolving. A face-up card's ignition (Magical Labyrinth) keeps its old path.
        /// </summary>
        static bool CanRecord(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand, ChainLink link)
        {
            if (engine == null || who == null || card?.Def == null || link == null) return false;
            if (!card.Def.IsSpell && !card.Def.IsTrap) return false;
            if (!ReferenceEquals(link.Card, card) || link.Resolved || link.DeferredEffect != null) return false;
            if (s_recording != null || engine.IsResolvingChain || engine.Chain.IsResolving) return false;
            if (engine.PendingResponse != null) return false;
            var faceUpOnField = card.FaceUp &&
                                (who.TryFindSpellTrap(card, out _) ||
                                 (who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card));
            return fromHand || !faceUpOnField;
        }

        /// <summary>
        /// After the activation flow ran (<see cref="Activate"/> or a <see cref="Continue"/> step): wait while a cost /
        /// target prompt is open; drop the record when the activation was undone; otherwise seat the activation and
        /// let the opponent respond.
        /// </summary>
        static void AfterStep(DuelEngine engine, ChainLink link)
        {
            var d = link?.DeferredEffect;
            if (d == null || d.Complete) return;
            if (!ContainsLink(engine.Chain, link))
            {
                link.DeferredEffect = null; // cancelled: DropUnresolvedLink took the link back
                return;
            }

            if (engine.PendingActivation != null) return; // still choosing a cost or target
            if (d.Steps.Count == 0 || engine.GameOver)
            {
                // Nothing to apply later (the flow found nothing to do, or the cost ended the duel): as before.
                link.DeferredEffect = null;
                return;
            }

            d.Complete = true;
            var who = link.Controller;
            var card = link.Card;
            if (card != null && who != null)
            {
                // A Continuous card's effects wait for this link, as on the untargeted path.
                TurnNegationTrapsRuntime.MarkActivationOnChain(card, fromHand: true);
                // The card is face-up on the field as the Chain Link while the opponent may respond. A cost flow
                // (Brain Control's LP in StartIgnition, Crush Card Virus's Tribute) reaches here with the card still
                // in the hand or Set.
                var inHand = who.Hand != null && who.Hand.Contains(card);
                var setOnField = !card.FaceUp && who.TryFindSpellTrap(card, out _);
                if (inHand || setOnField)
                    SpellTrapEffects.PlaceFaceUpForActivation(engine, who, card, fromHand: inHand);
            }

            link.Target = null;
            link.Targets.Clear();
            foreach (var s in d.Steps)
            {
                if (!s.IsTarget || s.Target == null || link.Targets.Contains(s.Target)) continue;
                if (s.At.Pile == DeferredActivationEffect.Pile.Untracked ||
                    s.At.Pile == DeferredActivationEffect.Pile.Hand)
                    continue; // a Deck or hand pick stays hidden from the opponent
                link.Targets.Add(s.Target);
            }

            if (link.Targets.Count > 0)
                link.Target = link.Targets[0];

            if (engine.OpenChainResponseWindow(engine.OpponentOf(who)))
            {
                engine.NotifyPublic();
                return;
            }

            engine.Chain.StartResolution();
            engine.ResolveChainStackForActivation();
        }

        static bool ContainsLink(ChainStack chain, ChainLink link)
        {
            if (chain == null) return false;
            foreach (var l in chain.Links)
                if (ReferenceEquals(l, link)) return true;
            return false;
        }

        static bool HasEquipStep(DeferredActivationEffect d)
        {
            foreach (var s in d.Steps)
                if (s.Equips) return true;
            return false;
        }

        static DeferredActivationEffect.Where Locate(DuelEngine engine, CardInstance card)
        {
            var w = new DeferredActivationEffect.Where();
            if (engine == null || card == null) return w;
            w.EntryStamp = card.FieldEntryStamp;
            w.FaceDownStamp = card.FaceDownStamp;
            w.FaceUp = card.FaceUp;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                w.Side = side;
                if (side.TryFindMonster(card, out _)) { w.Pile = DeferredActivationEffect.Pile.Monster; return w; }
                if (side.TryFindSpellTrap(card, out _)) { w.Pile = DeferredActivationEffect.Pile.SpellTrap; return w; }
                if (side.FieldSpellZone != null && side.FieldSpellZone.Occupant == card)
                {
                    w.Pile = DeferredActivationEffect.Pile.FieldSpell;
                    return w;
                }

                if (side.Hand != null && side.Hand.Contains(card)) { w.Pile = DeferredActivationEffect.Pile.Hand; return w; }
                if (side.Graveyard != null && side.Graveyard.Contains(card))
                {
                    w.Pile = DeferredActivationEffect.Pile.Graveyard;
                    return w;
                }

                if (side.Banished != null && side.Banished.Contains(card))
                {
                    w.Pile = DeferredActivationEffect.Pile.Banished;
                    return w;
                }
            }

            // A Deck card (the Deck holds ids; a pick is a fresh instance) or anything else: not tracked.
            w.Side = null;
            w.Pile = DeferredActivationEffect.Pile.Untracked;
            return w;
        }

        /// <summary>The step's target is where it was chosen: same zone, same side, same stay on the field.</summary>
        static bool StillThere(DuelEngine engine, DeferredActivationEffect.Step s)
        {
            var was = s.At;
            if (was.Pile == DeferredActivationEffect.Pile.Untracked) return true;
            var now = Locate(engine, s.Target);
            if (now.Pile != was.Pile || !ReferenceEquals(now.Side, was.Side)) return false;
            switch (was.Pile)
            {
                case DeferredActivationEffect.Pile.Monster:
                case DeferredActivationEffect.Pile.SpellTrap:
                case DeferredActivationEffect.Pile.FieldSpell:
                    if (now.EntryStamp != was.EntryStamp) return false;
                    if (s.NeedsFaceUp && was.FaceUp && (!now.FaceUp || now.FaceDownStamp != was.FaceDownStamp))
                        return false;
                    return true;
                default:
                    return true;
            }
        }
    }
}
