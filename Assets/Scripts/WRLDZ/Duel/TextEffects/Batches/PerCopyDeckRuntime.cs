using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PerCopyDeck" (v163): per-copy identity of Deck and hand cards.
    /// <list type="bullet">
    /// <item><b>Deck copies.</b> <see cref="DuelistState.Deck"/> is a <see cref="DeckPile"/>: each copy may carry its
    /// owner and a face-up flag (<see cref="DeckCopyInfo"/>). <see cref="DuelEngine.ReturnCardToDeck"/> can put a card in
    /// another player's Deck or face-up; shuffles move the metadata with the card.</item>
    /// <item><b>Leaving the Deck.</b> The instance the engine creates for a copy just removed from a Deck takes its owner
    /// (<see cref="ClaimDeckCopy"/>, from <c>DuelEngine.CreateInstance</c>): a card held by a player who does not own it
    /// keeps its owner in <see cref="CardInstance.ControlOwner"/>, as a card on the field does after a control change.
    /// A path whose instance was made before the removal (a pick from a Deck list: Deck viruses, Race costs, searches
    /// and Summons from the Deck) takes the copy with <see cref="TakeFromDeck"/>, which also returns the metadata for a
    /// failed move to put back; looking at the top cards and putting them back carries each copy's metadata. A removal
    /// no instance claimed is forgotten at the next instance, never handed to an unrelated one. A banish from the Deck
    /// goes to the owner's pile. A draw also reports the face-up state and the copy's "when drawn" trigger
    /// (<see cref="NoteDrawn"/>).</item>
    /// <item><b>Owner's piles.</b> Such a card goes to its owner's GY from the hand too (<see cref="OwnersPile"/>, from
    /// <see cref="RedirectAndReplaceRuntime.PutInGraveyard"/>); from the field the existing <c>PileOwner</c> already
    /// routes it, and a banish goes through <c>BanishCard</c>'s <c>PileOwner</c>.</item>
    /// <item><b>Cards.</b> Parasite Paracide (FLIP into the opponent's Deck face-up; drawn: Special Summoned to the drawer's
    /// field, 1000 damage to them, their monsters become Insect), Pharaoh's Treasure (face-up into your Deck; drawn:
    /// send it to the GY, add a GY target to the hand), Graverobber (an opponent's Spell from their GY into your hand
    /// this turn; using it costs 2000 damage; at the End Phase it goes back to its owner's GY).</item>
    /// </list>
    /// Residual: a foreign card in a hand that is shuffled or returned into a Deck by another effect goes to the holder's
    /// Deck (those paths add plain ids).
    /// </summary>
    public static class PerCopyDeckRuntime
    {
        /// <summary>A Spell Graverobber put in a player's hand this turn.</summary>
        sealed class Stolen
        {
            public DuelistState Thief;
            public CardInstance Card;
            public CardInstance Source;
            public int Turn;
            public int Damage;
            public bool Used;
        }

        /// <summary>Parasite Paracide's Type change: its controller's monsters are <see cref="Race"/>.</summary>
        sealed class Aura
        {
            public CardInstance Source;
            public int EntryStamp;
            public int FaceDownStamp;
            public string Race;
            public long Stamp;
        }

        sealed class State
        {
            public readonly List<Stolen> Stolen = new();
            public readonly List<Aura> Auras = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        /// <summary>Duels with a live Type-change aura (read by <see cref="RaceOnField"/>, which has only the card).</summary>
        static readonly List<WeakReference<DuelEngine>> s_auraEngines = new();
        /// <summary>A Spell/Trap that its own resolution moved into the Deck instead of the GY (Pharaoh's Treasure).</summary>
        static readonly ConditionalWeakTable<CardInstance, object> s_wentToDeckInstead = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────── Deck copies ─────────────────────────

        /// <summary>
        /// <c>DuelEngine.CreateInstance</c>: <paramref name="card"/> is a new instance; if its id is the copy a Deck just
        /// gave up (<see cref="DeckPile.TakeDeparted"/>; the newest such removal across both Decks) and that copy belongs
        /// to another player, the instance keeps that owner (<see cref="CardInstance.ControlOwner"/>), so its later exits
        /// go to its owner's piles. Whatever it claims or not, no removal is remembered past this instance: a path that
        /// made its instance before the removal claims through <see cref="TakeFromDeck"/>, and an unclaimed record must
        /// never reach a later, unrelated instance of the same card.
        /// </summary>
        public static void ClaimDeckCopy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            DuelistState from = null;
            long newest = 0;
            foreach (var holder in new[] { engine.Player, engine.Opponent })
            {
                var stamp = holder?.Deck?.DepartedStamp(card.CardId) ?? 0;
                if (stamp <= newest) continue;
                newest = stamp;
                from = holder;
            }

            var info = from?.Deck.TakeDeparted(card.CardId);
            engine.Player?.Deck?.ForgetDepartedCopy();
            engine.Opponent?.Deck?.ForgetDepartedCopy();
            ApplyCopyOwner(from, card, info);
        }

        /// <summary>
        /// Take the copy at <paramref name="index"/> out of <paramref name="holder"/>'s Deck for <paramref name="card"/>,
        /// the instance that stands for it (a pick from a Deck list made before the removal, or one made for it): the
        /// instance gets the copy's owner, and the copy's metadata is returned, so a move that fails can put it back
        /// unchanged (<see cref="DeckPile.Insert(int,int,DeckCopyInfo)"/>).
        /// </summary>
        public static DeckCopyInfo TakeFromDeck(DuelistState holder, int index, CardInstance card)
        {
            if (holder?.Deck == null || index < 0 || index >= holder.Deck.Count) return null;
            var info = holder.Deck.TakeAt(index);
            ApplyCopyOwner(holder, card, info);
            return info;
        }

        /// <summary>A copy that belongs to a player other than the Deck's holder: its instance keeps that owner.</summary>
        static void ApplyCopyOwner(DuelistState holder, CardInstance card, DeckCopyInfo info)
        {
            if (card == null || info?.Owner == null || ReferenceEquals(info.Owner, holder)) return;
            card.ControlOwner = info.Owner;
            card.ReturnControlAtEndOfTurn = -1;
        }

        /// <summary>
        /// <c>DuelEngine.Draw</c>: <paramref name="who"/> drew <paramref name="card"/>, whose Deck copy had
        /// <paramref name="info"/>. A face-up copy is public (logged by name); a copy with its own "when drawn" trigger
        /// (<see cref="EffectTiming.DrawnFaceUpFromDeck"/>) queues it on the simultaneous-trigger Chain (a mandatory
        /// trigger of the card in the drawer's hand, controlled by the drawer).
        /// </summary>
        public static void NoteDrawn(DuelEngine engine, DuelistState who, CardInstance card, DeckCopyInfo info)
        {
            if (engine == null || who == null || card == null || info == null) return;
            if (info.FaceUp)
            {
                var owner = card.ControlOwner;
                engine.Log($"{who.Name} draws {card.Name}, which was face-up in the Deck" +
                           (owner != null && !ReferenceEquals(owner, who) ? $" ({owner.Name}'s card)." : "."));
            }

            if (!info.DrawTrigger) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return;
            var clause = prog.ClausesFor(EffectTiming.DrawnFaceUpFromDeck)?.FirstOrDefault(c => c != null);
            if (clause == null) return;
            engine.Log($"{card.Name} was drawn — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, who, card, optional: false,
                () => ResolveDrawTrigger(engine, who, card, clause));
        }

        static void ResolveDrawTrigger(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || engine.GameOver || clause == null) return;
            switch (clause.Action)
            {
                case EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace:
                    ResolveDrawnSummon(engine, who, card, clause);
                    break;
                case EffectActionKind.SendDrawnThisToGyThenAddGyTargetToHand:
                    ResolveDrawnRecovery(engine, who, card, clause);
                    break;
            }
        }

        /// <summary>
        /// <see cref="RedirectAndReplaceRuntime.PutInGraveyard"/>: the GY a card goes to. A card held by a player who does
        /// not own it (a drawn Parasite Paracide, a Spell taken by Graverobber; <see cref="CardInstance.ControlOwner"/> set
        /// while it is out of its owner's hands) goes to its owner's GY; the ownership bookkeeping ends there.
        /// </summary>
        public static DuelistState OwnersPile(DuelistState pile, CardInstance card)
        {
            var owner = card?.ControlOwner;
            if (owner == null) return pile;
            card.ControlOwner = null;
            card.ReturnControlAtEndOfTurn = -1;
            return owner;
        }

        /// <summary>
        /// <c>TextEffectRuntime.FinishSpellTrap</c>: true (once) when <paramref name="card"/>'s own resolution moved it into
        /// the Deck instead of the GY (Pharaoh's Treasure, ygopro CancelToGrave): nothing is left to send to the GY.
        /// </summary>
        public static bool ConsumeWentToDeckInstead(CardInstance card)
        {
            if (card == null || !s_wentToDeckInstead.TryGetValue(card, out _)) return false;
            s_wentToDeckInstead.Remove(card);
            return true;
        }

        // ───────────────────────── Parasite Paracide ─────────────────────────

        /// <summary>
        /// FLIP: if this card is still on its controller's field, it is put face-up in the opponent's Deck (it stays its
        /// owner's card) and that Deck is shuffled; the copy carries its "when drawn" trigger.
        /// </summary>
        public static void PutThisFaceUpInOpponentDeck(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            if (!who.TryFindMonster(source, out _))
            {
                engine.Log($"{source.Name} is no longer on the field — it is not put in {opp.Name}'s Deck.");
                return;
            }

            // Flipped by an attack that destroys it (ygopro STATUS_BATTLE_DESTROYED): the effect does nothing.
            if (ReferenceEquals(engine.FlipSourceBattleDestroyPending, source) || engine.IsBattleDestroyPending(source))
            {
                engine.Log($"{source.Name} is destroyed by battle — it is not put in {opp.Name}'s Deck.");
                return;
            }

            var info = new DeckCopyInfo { FaceUp = true, DrawTrigger = true };
            if (!engine.ReturnCardToDeck(source, onTop: false, intoDeckOf: opp, copyInfo: info)) return;
            engine.Rng.Shuffle(opp.Deck);
            engine.Log($"{opp.Name}'s Deck is shuffled with {source.Name} face-up in it.");
        }

        /// <summary>
        /// Drawn: if it is still in the drawer's hand, they Special Summon it to their field in face-up Defense Position;
        /// if it is Summoned, they take the damage, then every monster its controller controls has the clause's Type while
        /// it stays face-up on the field (ygopro EFFECT_CHANGE_RACE, RESETS_STANDARD).
        /// </summary>
        public static void ResolveDrawnSummon(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || who == null || card == null) return;
            if (!who.Hand.Contains(card))
            {
                engine.Log($"{card.Name} is no longer in {who.Name}'s hand — it is not Special Summoned.");
                return;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0 ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, card, SummonOrigin.Hand,
                    requireZone: true))
            {
                engine.Log($"{card.Name} cannot be Special Summoned to {who.Name}'s field.");
                return;
            }

            who.Hand.Remove(card);
            engine.MarkSpecialSummonFromHand(card);
            if (!engine.SpecialSummonToField(who, card, BattlePosition.Defense, true))
            {
                if (!who.TryFindMonster(card, out _) && !who.Hand.Contains(card) && !who.Graveyard.Contains(card) &&
                    !(card.ControlOwner?.Graveyard.Contains(card) ?? false))
                    who.Hand.Add(card);
                engine.Log($"{card.Name} could not be Special Summoned.");
                return;
            }

            // Negated as it was Summoned (Solemn Judgment): it was not Summoned, so nothing else happens.
            if (!who.TryFindMonster(card, out _)) return;
            engine.Log($"{card.Name} is Special Summoned to {who.Name}'s field in face-up Defense Position.");
            engine.ApplyEffectDamage(who, clause.Amount, card.Name, card);
            if (engine.GameOver || !who.TryFindMonster(card, out _) || string.IsNullOrEmpty(clause.RaceFilter)) return;
            StateOf(engine).Auras.Add(new Aura
            {
                Source = card,
                EntryStamp = card.FieldEntryStamp,
                FaceDownStamp = card.FaceDownStamp,
                Race = clause.RaceFilter,
                Stamp = TypeDeclarationRuntime.NextStamp()
            });
            RegisterAuraEngine(engine);
            engine.Log($"While {card.Name} is face-up on the field, every monster its controller controls is " +
                       $"{clause.RaceFilter}-Type.");
            engine.NotifyPublic();
        }

        static void RegisterAuraEngine(DuelEngine engine)
        {
            for (var i = s_auraEngines.Count - 1; i >= 0; i--)
            {
                if (!s_auraEngines[i].TryGetTarget(out var e) || e == null)
                {
                    s_auraEngines.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(e, engine)) return;
            }

            s_auraEngines.Add(new WeakReference<DuelEngine>(engine));
        }

        /// <summary>
        /// <see cref="TypeDeclarationRuntime.CurrentRace"/>: the Type a live aura gives <paramref name="card"/> (a monster
        /// on the field of the aura source's controller), the latest one applied, with its timestamp; null when none
        /// applies. An aura ends for good when its source leaves the field or is turned face-down; it does not apply
        /// while its source's effects are negated.
        /// </summary>
        public static string RaceOnField(CardInstance card, out long stamp)
        {
            stamp = long.MinValue;
            if (card == null || s_auraEngines.Count == 0) return null;
            string best = null;
            foreach (var wr in s_auraEngines)
            {
                if (!wr.TryGetTarget(out var engine) || engine == null) continue;
                if (!s_state.TryGetValue(engine, out var st) || st.Auras.Count == 0) continue;
                var side = MonsterSideOf(engine, card);
                if (side == null) continue;
                for (var i = st.Auras.Count - 1; i >= 0; i--)
                {
                    var a = st.Auras[i];
                    var src = a.Source;
                    var srcSide = MonsterSideOf(engine, src);
                    if (srcSide == null || !src.FaceUp || src.FieldEntryStamp != a.EntryStamp ||
                        src.FaceDownStamp != a.FaceDownStamp)
                    {
                        st.Auras.RemoveAt(i);
                        continue;
                    }

                    if (src.IsNegated || !ReferenceEquals(srcSide, side) || a.Stamp <= stamp) continue;
                    best = a.Race;
                    stamp = a.Stamp;
                }

                if (best != null) return best;
            }

            return best;
        }

        static DuelistState MonsterSideOf(DuelEngine engine, CardInstance card)
        {
            if (card == null) return null;
            if (engine.Player != null && engine.Player.TryFindMonster(card, out _)) return engine.Player;
            if (engine.Opponent != null && engine.Opponent.TryFindMonster(card, out _)) return engine.Opponent;
            return null;
        }

        // ───────────────────────── Pharaoh's Treasure ─────────────────────────

        /// <summary>
        /// Activation: if this card is still on the field, it is shuffled face-up into its controller's Deck instead of
        /// going to the GY; the copy carries its "when drawn" trigger.
        /// </summary>
        public static void ShuffleThisFaceUpIntoYourDeck(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            if (!who.TryFindSpellTrap(source, out _))
            {
                engine.Log($"{source.Name} is no longer on the field — it is not shuffled into the Deck.");
                return;
            }

            var info = new DeckCopyInfo { FaceUp = true, DrawTrigger = true };
            if (!engine.ReturnCardToDeck(source, onTop: false, intoDeckOf: who, copyInfo: info)) return;
            s_wentToDeckInstead.AddOrUpdate(source, null);
            engine.Rng.Shuffle(who.Deck);
            engine.Log($"{who.Name}'s Deck is shuffled with {source.Name} face-up in it.");
        }

        /// <summary>
        /// Drawn: target 1 card in the drawer's GY other than this card (none: the effect does not activate); send this
        /// card from the hand to the GY, then, if it is there, add the target to the hand. The target is chosen as the
        /// trigger resolves (nothing can answer a simultaneous-trigger link in between, so it is the same choice).
        /// </summary>
        public static void ResolveDrawnRecovery(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || who == null || card == null) return;
            List<CardInstance> Candidates() =>
                who.Graveyard.Where(g => g != null && !ReferenceEquals(g, card) &&
                                         !ContinuousProtections.CannotTargetCardInGy(engine, card, g)).ToList();

            if (!who.Hand.Contains(card) || Candidates().Count == 0)
            {
                engine.Log(who.Hand.Contains(card)
                    ? $"{card.Name}: there is no card in {who.Name}'s GY to target — its effect does not activate."
                    : $"{card.Name} is no longer in {who.Name}'s hand — its effect does not activate.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, Candidates,
                EffectTargetKind.CardInEitherGy,
                $"{card.Name}: target 1 card in your GY (except this card) to add to your hand; this card is sent " +
                "to the GY.",
                AiBestRecovery, target => FinishDrawnRecovery(engine, who, card, target));
        }

        static CardInstance AiBestRecovery(List<CardInstance> list) =>
            list?.Where(c => c?.Def != null && c.Def.IsMonster).OrderByDescending(c => c.Def.atk).FirstOrDefault() ??
            list?.LastOrDefault();

        static void FinishDrawnRecovery(DuelEngine engine, DuelistState who, CardInstance card, CardInstance target)
        {
            if (engine == null || engine.GameOver || target == null) return;
            engine.Log($"{card.Name} targets {target.Name} in {who.Name}'s GY.");
            if (!who.Graveyard.Contains(target) || !who.Hand.Contains(card))
            {
                engine.Log($"{card.Name}: the effect resolves with no effect.");
                return;
            }

            engine.SendCardToGrave(who, card, card);
            if (!who.Graveyard.Contains(card)) return;
            if (ContinuousProtections.GyMovementLocked(engine, card, target))
            {
                engine.Log("Necrovalley prevents the target from leaving the GY.");
                return;
            }

            who.Graveyard.Remove(target);
            GyEntryStamps.Left(target);
            who.Hand.Add(target);
            engine.Log($"{card.Name}: {target.Name} is added from the GY to {who.Name}'s hand.");
            engine.NotifyPublic();
        }

        // ───────────────────────── Graverobber ─────────────────────────

        /// <summary>
        /// Resolution: if the target is still a Spell in the opponent's GY, it is added to the activating player's hand,
        /// still the opponent's card. Its use this turn costs <see cref="EffectClause.Amount"/> damage
        /// (<see cref="OnSpellResolved"/>); at the End Phase it is sent to its owner's GY if they still hold or control it
        /// (<see cref="OnEndPhase"/>).
        /// </summary>
        public static void ResolveGraverobber(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null) return;
            var opp = engine.OpponentOf(who);
            if (target == null || opp == null || !opp.Graveyard.Contains(target) || target.Def == null ||
                !target.Def.IsSpell)
            {
                engine.Log($"{source?.Name}: the target is no longer in {opp?.Name}'s GY — no effect.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, target))
            {
                engine.Log("Necrovalley prevents the target from leaving the GY.");
                return;
            }

            opp.Graveyard.Remove(target);
            GyEntryStamps.Left(target);
            target.ControlOwner = opp;
            target.ReturnControlAtEndOfTurn = -1;
            who.Hand.Add(target);
            StateOf(engine).Stolen.Add(new Stolen
            {
                Thief = who,
                Card = target,
                Source = source,
                Turn = engine.TurnNumber,
                Damage = clause?.Amount > 0 ? clause.Amount : 2000
            });
            engine.Log($"{source?.Name}: {who.Name} adds {target.Name} from {opp.Name}'s GY to their hand until the " +
                       $"end of the turn (using it inflicts {(clause?.Amount > 0 ? clause.Amount : 2000)} damage to " +
                       $"{who.Name}).");
            engine.NotifyPublic();
        }

        /// <summary>
        /// <c>TextEffectRuntime.NotifySpellResolved</c>: <paramref name="who"/>'s activation of <paramref name="spell"/>
        /// resolved. A Spell Graverobber gave them this turn costs its damage, once (ygopro EVENT_CHAINING then
        /// EVENT_CHAIN_SOLVED: a negated activation costs nothing).
        /// </summary>
        public static void OnSpellResolved(DuelEngine engine, DuelistState who, CardInstance spell)
        {
            if (engine == null || who == null || spell == null || !s_state.TryGetValue(engine, out var st)) return;
            foreach (var s in st.Stolen)
            {
                if (s.Used || !ReferenceEquals(s.Card, spell) || !ReferenceEquals(s.Thief, who) ||
                    s.Turn != engine.TurnNumber)
                    continue;
                s.Used = true;
                engine.Log($"{who.Name} used {spell.Name} from {s.Source?.Name ?? "Graverobber"} — " +
                           $"{s.Damage} damage to {who.Name}.");
                engine.ApplyEffectDamage(who, s.Damage, s.Source?.Name ?? "Graverobber", s.Source);
                return;
            }
        }

        /// <summary>
        /// <c>DuelEngine</c> End Phase: each Spell Graverobber gave a player this turn that they still hold or control is
        /// sent to its owner's GY; the "use" damage ends with the turn.
        /// </summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Stolen.Count == 0) return;
            foreach (var s in st.Stolen.ToList())
            {
                if (s.Turn != engine.TurnNumber || engine.GameOver) continue;
                var c = s.Card;
                var thief = s.Thief;
                var held = thief.Hand.Contains(c) || thief.TryFindSpellTrap(c, out _) ||
                           thief.TryFindMonster(c, out _) ||
                           (thief.FieldSpellZone != null && ReferenceEquals(thief.FieldSpellZone.Occupant, c));
                if (!held || c.ControlOwner == null || ReferenceEquals(c.ControlOwner, thief)) continue;
                engine.Log($"{s.Source?.Name ?? "Graverobber"}: the turn ends — {c.Name} is sent to " +
                           $"{c.ControlOwner.Name}'s GY.");
                engine.SendCardToGrave(thief, c, s.Source);
            }

            st.Stolen.RemoveAll(s => s.Turn <= engine.TurnNumber);
        }
    }
}
