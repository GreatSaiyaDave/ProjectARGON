using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// "You can activate 1 of these effects: ● … ● …" on a monster trigger (Dark Scorpion - Cliff the Trap Remover
    /// c6967870, Dark Scorpion - Gorg the Strong c48768179: one optional trigger whose target function asks
    /// Duel.SelectEffect). The bullets compile as clauses with <see cref="EffectClause.EffectChoiceIndex"/>
    /// (<see cref="PartialCompilesAtoms"/>). When the trigger fires they are offered together, once, and only the
    /// chosen bullet resolves. A bullet that cannot resolve is not offered: no legal target, or fewer cards in the
    /// opponent's Deck than a mill sends (Duel.IsPlayerCanDiscardDeck). A monster the same battle destroys is not a
    /// target (<see cref="DuelEngine.IsBattleDestroyPending"/>).
    /// <para>
    /// A human picks in the existing target picker (<see cref="PendingActivation.EffectChoice"/>): its cards are the
    /// targeted bullet's legal targets plus this card itself, which stands for the untargeted bullet. CANCEL
    /// activates neither. The AI takes the targeted bullet when its pick is a card the opponent controls, else the
    /// untargeted bullet, else neither (it never spends the effect on its own card).
    /// </para>
    /// <para>
    /// BattleOutcomeTriggers batch (v162): Don Zaloog's two bullets are both untargeted ("● Discard 1 random card from their
    /// hand. ● Send the top 2 cards of their Deck to the GY."; ygopro c76922029 Duel.SelectEffect over the legal ones). This
    /// card stands for the first legal bullet; CANCEL then offers the other one (<see cref="Choice.UntargetedAlt"/>), and
    /// CANCEL again activates neither. The AI takes the first legal bullet.
    /// </para>
    /// </summary>
    public static class EffectChoiceRuntime
    {
        /// <summary>The bullets still open to the player: at most one targeted and one untargeted, plus optional alts.</summary>
        public sealed class Choice
        {
            /// <summary>The bullet that targets (its legal targets are in the picker), or null.</summary>
            public EffectClause Targeted;
            /// <summary>The bullet without a target (this card stands for it in the picker), or null.</summary>
            public EffectClause Untargeted;
            /// <summary>
            /// A second bullet without a target (Don Zaloog), offered in its turn when the player CANCELs
            /// <see cref="Untargeted"/>; or null.
            /// </summary>
            public EffectClause UntargetedAlt;
            /// <summary>
            /// ChoiceBullets batch (v173) — Cyber Raider: a second targeted bullet, offered in its turn when the
            /// player CANCELs <see cref="Targeted"/>. Both bullets share the same target pool (Equip Cards on the
            /// field); the alt may narrow it further (the Equip Card's own equip-target requirements).
            /// </summary>
            public EffectClause TargetedAlt;

            public string Prompt(string cardName)
            {
                if (Untargeted != null && UntargetedAlt != null)
                    return $"{cardName}: activate 1 effect. Tap {cardName} to {Describe(Untargeted)}, or CANCEL to " +
                           $"choose the other effect ({Describe(UntargetedAlt)}).";
                if (Targeted != null && TargetedAlt != null)
                    return $"{cardName}: activate 1 effect. Tap a target to {Describe(Targeted)}, or CANCEL to " +
                           $"choose the other effect ({Describe(TargetedAlt)}).";
                if (Targeted != null && Untargeted != null)
                    return $"{cardName}: activate 1 effect. Tap a target to {Describe(Targeted)}, or tap " +
                           $"{cardName} to {Describe(Untargeted)}. CANCEL activates neither.";
                if (Targeted != null)
                    return $"{cardName}: tap a target to {Describe(Targeted)}, or CANCEL.";
                return $"{cardName}: tap {cardName} to {Describe(Untargeted)}, or CANCEL.";
            }
        }

        public static bool IsChoiceBullet(EffectClause c) => c != null && c.EffectChoiceIndex > 0;

        /// <summary>
        /// The trigger fired: offer its bullets (the clauses of <paramref name="clauses"/> with an
        /// <see cref="EffectClause.EffectChoiceIndex"/>) to <paramref name="who"/>.
        /// </summary>
        public static void Offer(DuelEngine engine, DuelistState who, CardInstance source,
            IEnumerable<EffectClause> clauses)
        {
            if (engine == null || who == null || source == null || clauses == null) return;
            var bullets = clauses.Where(IsChoiceBullet).OrderBy(c => c.EffectChoiceIndex).ToList();
            var targeted = bullets.FirstOrDefault(c => c.RequiresTargetChoice);
            var untargetedAll = bullets.Where(c => !c.RequiresTargetChoice).ToList();
            var untargeted = untargetedAll.FirstOrDefault();
            // Don Zaloog: both bullets are untargeted; only the legal ones are offered (the first legal one first).
            var alt = untargetedAll.Count > 1 ? untargetedAll[1] : null;
            if (alt != null && !CanResolve(engine, who, alt)) alt = null;
            if (alt != null && untargeted != null && !CanResolve(engine, who, untargeted))
            {
                untargeted = alt;
                alt = null;
            }
            // ChoiceBullets batch (v173) — Cyber Raider: two targeted bullets sharing the same target pool.
            var targetedAll = bullets.Where(c => c.RequiresTargetChoice).ToList();
            var targetedAlt = targetedAll.Count > 1 ? targetedAll[1] : null;
            var targets = TextEffectRuntime.CollectChoiceTargets(engine, who, targeted, source);
            // A monster this battle destroyed is no longer a legal target (Gorg cannot return the monster it beat).
            targets.RemoveAll(engine.IsBattleDestroyPending);
            var canTarget = targeted != null && targets.Count > 0;
            var canOther = untargeted != null && CanResolve(engine, who, untargeted);
            // Cyber Raider b2: the equip bullet's pool is the subset of b1's targets that pass CheckEquipTarget.
            List<CardInstance> altTargets = null;
            var canTargetAlt = false;
            if (targetedAlt != null && canTarget)
            {
                altTargets = targets.Where(t =>
                    EquipFamilyRuntime.IsCorrectEquipTarget(engine, t, source)).ToList();
                canTargetAlt = altTargets.Count > 0;
            }
            if (!canTarget && !canOther && !canTargetAlt)
            {
                engine.Log($"{source.Name}: none of its effects can be activated.");
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                // Cyber Raider AI: prefer equipping an opponent's Equip Card (steals ATK), else destroy one.
                if (canTargetAlt)
                {
                    var steal = altTargets.FirstOrDefault(t => engine.ControllerOf(t) == engine.OpponentOf(who))
                                 ?? altTargets[0];
                    Resolve(engine, who, source, targetedAlt, steal);
                }
                else if (canTarget)
                {
                    var pick = TextEffectRuntime.AutoPickChoiceTarget(engine, who, targeted, targets);
                    if (pick != null && engine.ControllerOf(pick) == engine.OpponentOf(who))
                        Resolve(engine, who, source, targeted, pick);
                    else if (canOther)
                        Resolve(engine, who, source, untargeted, null);
                    else
                        engine.Log($"{source.Name}: {who.Name} does not activate its effect.");
                }
                else if (canOther)
                    Resolve(engine, who, source, untargeted, null);
                else
                    engine.Log($"{source.Name}: {who.Name} does not activate its effect.");
                engine.NotifyPublic();
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                FromHand = false,
                WasSetOnField = false,
                // The generic card picker (field cards tagged YOURS / OPP); this card is one of the picks.
                TargetKind = EffectTargetKind.AnyCardOnField,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                EffectChoice = new Choice
                {
                    Targeted = canTarget ? targeted : null,
                    Untargeted = canOther ? untargeted : null,
                    UntargetedAlt = canOther ? alt : null,
                    TargetedAlt = canTargetAlt ? targetedAlt : null
                }
            };
            if (canTarget)
                pending.LegalTargets.AddRange(targets);
            if (canOther && !pending.LegalTargets.Contains(source))
                pending.LegalTargets.Add(source);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
        }

        /// <summary>The human tapped a card in the picker: this card means the untargeted bullet.</summary>
        public static bool FinishPick(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            var choice = p?.EffectChoice;
            if (engine == null || choice == null || target == null) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            if (pick == null) return false;
            EffectClause bullet;
            if (choice.Untargeted != null && pick == p.Card)
            {
                bullet = choice.Untargeted;
                pick = null;
            }
            else if (choice.Targeted != null)
                bullet = choice.Targeted;
            else
                return false;

            engine.ClearPendingActivation();
            Resolve(engine, p.Controller, p.Card, bullet, pick);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// ChoiceBullets batch (v173) — Cyber Raider: the human tapped a target while the equip bullet (TargetedAlt)
        /// was offered. The pick is the Equip Card to equip to this card.
        /// </summary>
        public static bool FinishTargetedAltPick(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            var choice = p?.EffectChoice;
            if (engine == null || choice?.Targeted == null || target == null) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            if (pick == null) return false;
            engine.ClearPendingActivation();
            Resolve(engine, p.Controller, p.Card, choice.Targeted, pick);
            engine.NotifyPublic();
            return true;
        }

        static void Resolve(DuelEngine engine, DuelistState who, CardInstance source, EffectClause bullet,
            CardInstance target)
        {
            engine.Log(target != null
                ? $"{source.Name} activates its effect: {Describe(bullet)} ({target.Name})."
                : $"{source.Name} activates its effect: {Describe(bullet)}.");
            TextEffectRuntime.ApplyChosenClause(engine, who, source, bullet, target);
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook: CANCEL at the first of two untargeted bullets (Don Zaloog) offers the other
        /// one; CANCEL at the first of two targeted bullets (Cyber Raider) offers the other; false otherwise (CANCEL
        /// activates neither).
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            var choice = p?.EffectChoice;
            if (choice?.Untargeted != null && choice.UntargetedAlt != null)
            {
                p.EffectChoice = new Choice { Untargeted = choice.UntargetedAlt };
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }
            if (choice?.Targeted != null && choice.TargetedAlt != null)
            {
                var altTargets = p.LegalTargets.Where(t =>
                    EquipFamilyRuntime.IsCorrectEquipTarget(engine, t, p.Card)).ToList();
                p.EffectChoice = new Choice { Targeted = choice.TargetedAlt };
                p.LegalTargets.Clear();
                p.LegalTargets.AddRange(altTargets);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }
            return false;
        }

        /// <summary>An untargeted bullet can resolve (a mill needs that many cards: IsPlayerCanDiscardDeck).</summary>
        static bool CanResolve(DuelEngine engine, DuelistState who, EffectClause c)
        {
            // Don Zaloog: a random discard needs a card in the opponent's hand (Lua b1: GetFieldGroupCount(tp, 0, HAND) > 0).
            if (c.Action == EffectActionKind.DiscardRandomFromOpponentHand)
                return (engine.OpponentOf(who)?.Hand?.Count ?? 0) > 0;
            if (c.Action == EffectActionKind.SendFromTopOfDeckToGy)
            {
                var deckOwner = c.Side == EffectSide.Opponent ? engine.OpponentOf(who) : who;
                return deckOwner?.Deck != null && deckOwner.Deck.Count >= System.Math.Max(1, c.Amount);
            }

            return true;
        }

        static string Describe(EffectClause c)
        {
            switch (c?.Action)
            {
                case EffectActionKind.Destroy:
                    return "destroy it";
                case EffectActionKind.PlaceTargetOnTopOfDeck:
                    return "return it to the top of the Deck";
                case EffectActionKind.DiscardRandomFromOpponentHand:
                    return "discard 1 random card from their hand";
                case EffectActionKind.SendFromTopOfDeckToGy:
                    var n = System.Math.Max(1, c.Amount);
                    var whose = c.Side == EffectSide.Opponent ? "their" : "your";
                    return n == 1
                        ? $"send the top card of {whose} Deck to the GY"
                        : $"send the top {n} cards of {whose} Deck to the GY";
                case EffectActionKind.ReturnToHand:
                    return "return it to the hand";
                case EffectActionKind.LookAtOpponentTopCardThenTopOrBottom:
                    return "look at the top card of their Deck, then return it to the top or bottom";
                case EffectActionKind.AddFromDeckToHand:
                    return "add 1 card from your Deck to your hand";
                case EffectActionKind.AddFromGyToHand:
                    return "add that target to your hand";
                case EffectActionKind.EquipTargetToThis:
                    return "equip that target to this card";
                default:
                    return "apply that effect";
            }
        }
    }
}
