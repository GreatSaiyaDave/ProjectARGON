using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleAndPositionRecords" runtime (atoms: <see cref="BattleAndPositionRecordsAtoms"/>). Reads compiled
    /// programs only — no card-id branches; only a FullyCompiled program's clauses apply.
    /// <list type="bullet">
    /// <item><b>Atomic Firefly</b>: a "destroyed by battle" trigger (TextEffectRuntime.TryResolveSentToGy, or
    /// RedirectAndReplaceRuntime's outside-GY path when banished instead) — the reason player takes the damage
    /// (<see cref="InflictDamageToDestroyer"/>).</item>
    /// <item><b>Disc Fighter</b>: EVENT_BATTLE_CONFIRM in DuelEngine.ContinueBeforeDamageCalculation, after the target was
    /// flipped (<see cref="FireBattleConfirm"/>). The destroyed target leaves the field, so the attack stops before damage
    /// calculation. A battle trigger resolving in the Damage Step, inline (the known residual).</item>
    /// <item><b>Ghost Knight of Jackal / Legacy Hunter</b>: EVENT_BATTLE_DESTROYING clauses of the monster that destroyed,
    /// from TextEffectRuntime.NotifyDestroyedOpponentByBattle (<see cref="TryFireBattleDestroying"/>).</item>
    /// <item><b>Vampire Lady / Vampire Lord</b>: the battle-damage declaration (Monster / Spell / Trap) and the opponent's pick
    /// from their Deck, both through ContinuousTrapsAndSpiritSupportRuntime.AskCardPick
    /// (<see cref="DeclareCardTypeThenOpponentSendsFromDeck"/>).</item>
    /// <item><b>The Unhappy Girl</b>: battle protection read at damage calculation (<see cref="BattleIndestructible"/>, from
    /// DamageStepRulesRuntime.BattleIndestructible); its EVENT_BATTLED record (<see cref="NoteBattled"/>), cleared when it
    /// changes position (<see cref="NotePositionChanged"/>) or stops being face-up Attack Position; the locks
    /// (<see cref="AttackForbidden"/>, <see cref="ManualPositionChangeLocked"/>).</item>
    /// <item><b>Judgment of the Desert</b>: its activation starts a flip record (<see cref="StartFlipRecord"/>); every
    /// monster whose <see cref="CardInstance.FaceUpStamp"/> grows on the same field presence afterwards (Flip Summon, flipped
    /// by an attack, flipped by an effect: ygopro EVENT_CHANGE_POS from face-down) is recorded while the card is face-up and
    /// not negated (<see cref="Scan"/>, run on each board refresh and before every lock check).</item>
    /// <item><b>Curse of Anubis</b>: the mass change, original DEF 0 until the end of the turn (reverted at the End Phase and
    /// when the monster leaves the field or is turned face-down) and PositionLocksRuntime's per-card rest-of-turn lock
    /// (<see cref="ChangeToDefenseZeroDefLock"/>).</item>
    /// </list>
    /// State is kept per engine (ConditionalWeakTable), so a restarted Duel starts clean.
    /// </summary>
    public static class BattleAndPositionRecordsRuntime
    {
        /// <summary><see cref="EffectClause.ActivationCondition"/> (Curse of Anubis): a face-up Attack Position Effect Monster
        /// is on the field (Lua target chk, IsExistingMatchingCard on both Monster Zones).</summary>
        public const string FaceUpAttackEffectMonsterOnField = "BprFaceUpAttackEffectMonsterOnField";

        sealed class GirlRecord
        {
            public int Entry;
            public int FaceDownStamp;
            public readonly List<(CardInstance Monster, int Entry, int FaceDownStamp)> Battled = new();
        }

        sealed class FlipRecord
        {
            public int Entry;
            public int FaceDownStamp;
            /// <summary>Each monster's field presence and face-up stamp when last looked at.</summary>
            public readonly Dictionary<CardInstance, (int Entry, int FaceUpStamp)> Seen = new();
            /// <summary>The recorded monsters (ygopro card targets), with the field presence they were flipped on.</summary>
            public readonly Dictionary<CardInstance, int> Recorded = new();
        }

        sealed class BaseDefZero
        {
            public CardInstance Card;
            public bool AtkField;
            public int Prior;
            public int Entry;
            public int FaceDownStamp;
            public int Turn;
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, GirlRecord> Girls = new();
            public readonly Dictionary<CardInstance, FlipRecord> Flips = new();
            public readonly List<BaseDefZero> DefZero = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The clause of <paramref name="card"/>'s FullyCompiled program with <paramref name="action"/>, or null.</summary>
        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClauseList.FirstOrDefault(c => c != null && c.Action == action);
        }

        /// <summary>A face-up, un-negated card whose FullyCompiled program carries <paramref name="action"/>.</summary>
        static EffectClause LiveClause(CardInstance card, EffectActionKind action) =>
            card == null || !card.FaceUp || card.IsNegated ? null : ClauseOf(card, action);

        static bool MonsterOnField(DuelEngine engine, CardInstance card)
        {
            var ctrl = engine?.ControllerOf(card);
            return ctrl != null && ctrl.TryFindMonster(card, out _);
        }

        static bool SpellTrapOnField(DuelEngine engine, CardInstance card)
        {
            var ctrl = engine?.ControllerOf(card);
            return ctrl != null && ctrl.TryFindSpellTrap(card, out _);
        }

        static IEnumerable<CardInstance> FieldMonsters(DuelEngine engine)
        {
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null)
                        yield return m;
            }
        }

        static bool IsFaceUpAttackEffectMonster(CardInstance m) =>
            m?.Def != null && m.FaceUp && m.Position == BattlePosition.Attack && m.Def.IsEffectMonster && !m.IsToken;

        // ───────────────────────────── Atomic Firefly ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.InflictDamageToPlayerWhoDestroyedThis"/>: the player who destroyed
        /// <paramref name="source"/> (EquipFamilyRuntime's destroy record: the other battler's controller; when it was banished
        /// instead, the controller of <paramref name="destroyer"/>, the monster that destroyed it, or else the opponent of
        /// its controller — every battle is between the two players) takes <see cref="EffectClause.Amount"/> damage.
        /// </summary>
        public static void InflictDamageToDestroyer(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance destroyer)
        {
            if (engine == null || source == null || clause == null || engine.GameOver) return;
            var rp = EquipFamilyRuntime.DestroyReasonPlayer(engine, source) ??
                     (destroyer != null ? engine.ControllerOf(destroyer) : null) ??
                     engine.OpponentOf(who);
            if (rp == null) return;
            engine.Log($"{source.Name}: destroyed by battle — {rp.Name}, who destroyed it, takes {clause.Amount} damage.");
            engine.ApplyEffectDamage(rp, Math.Max(0, clause.Amount), source.Name, source);
        }

        // ───────────────────────────── Disc Fighter ─────────────────────────────

        /// <summary>
        /// DuelEngine.ContinueBeforeDamageCalculation (EVENT_BATTLE_CONFIRM: the face-down target is already flipped,
        /// damage has not been calculated): an attacker with
        /// <see cref="EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation"/> attacking a Defense
        /// Position monster whose DEF is at least the clause's Amount destroys it with its effect (by its controller). The
        /// target has left the field, so the caller stops the attack before damage calculation.
        /// </summary>
        public static void FireBattleConfirm(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance attacker,
            CardInstance target)
        {
            if (engine == null || engine.GameOver || who == null || opp == null || attacker == null || target == null)
                return;
            if (!who.TryFindMonster(attacker, out _) || !opp.TryFindMonster(target, out _)) return;
            var clause = LiveClause(attacker, EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation);
            if (clause == null) return;
            // Lua chk: the attack target IsDefensePos and IsDefenseAbove(N); op: still related to the battle, not in Attack
            // Position (the trigger resolves at once, so both hold).
            if (target.Position != BattlePosition.Defense) return;
            var def = target.CurrentDef;
            if (def < clause.Amount) return;
            engine.Log($"{attacker.Name}: it attacks {target.Name} (Defense Position, DEF {def}) — its effect destroys that " +
                       "monster without applying damage calculation.");
            TextEffectRuntime.DestroyCard(engine, target, attacker, byPlayer: who);
        }

        // ───────────────────────────── Ghost Knight of Jackal / Legacy Hunter ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle: <paramref name="source"/> (still on the field) destroyed
        /// <paramref name="destroyed"/> by battle. True when <paramref name="clause"/> is one of this batch's
        /// EVENT_BATTLE_DESTROYING clauses (handled here, applied or not).
        /// </summary>
        public static bool TryFireBattleDestroying(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance destroyed)
        {
            if (clause == null) return false;
            switch (clause.Action)
            {
                case EffectActionKind.SpecialSummonBattleDestroyedMonsterToYourFieldInDefense:
                    if (engine == null || who == null || source == null || engine.GameOver) return true;
                    if (!source.FaceUp || source.IsNegated) return true;
                    OfferSummonDestroyed(engine, who, source, destroyed);
                    return true;
                case EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle:
                    if (engine == null || who == null || source == null || engine.GameOver) return true;
                    if (!source.FaceUp || source.IsNegated) return true;
                    if (!FlipDownAndPeekRuntime.AttackedFaceDownDefense(engine, source, destroyed))
                    {
                        engine.Log($"{source.Name}: the monster it destroyed was not in face-down Defense Position — no effect.");
                        return true;
                    }

                    ReturnRandomHandCardToDeck(engine, who, source);
                    return true;
                default:
                    return false;
            }
        }

        static DuelistState GyHolding(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side?.Graveyard != null && side.Graveyard.Contains(card))
                    return side;
            return null;
        }

        static bool CanSummonDestroyed(DuelEngine engine, DuelistState who, CardInstance source, CardInstance card) =>
            card?.Def != null && card.Def.IsMonster && GyHolding(engine, card) != null &&
            engine.FirstEmpty(who.MonsterZones) >= 0 &&
            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, source, SummonOrigin.Graveyard,
                requireZone: true) &&
            !ContinuousProtections.GyMovementLocked(engine, source, card);

        /// <summary>
        /// Ghost Knight of Jackal (aux.bdogcon): the destroyed monster must be a monster in a GY. Optional (TRIGGER_O): a
        /// human answers yes (tap the monster) or no (Cancel); the AI always summons it. It is a target (SetTargetCard), so
        /// it must still be in the GY when the effect resolves.
        /// </summary>
        static void OfferSummonDestroyed(DuelEngine engine, DuelistState who, CardInstance source, CardInstance destroyed)
        {
            if (destroyed?.Def == null || GyHolding(engine, destroyed) == null)
            {
                engine.Log($"{source.Name}: the destroyed monster is not in the GY — its effect cannot be activated.");
                return;
            }

            if (!CanSummonDestroyed(engine, who, source, destroyed))
            {
                engine.Log($"{source.Name}: {destroyed.Name} cannot be Special Summoned to {who.Name}'s field.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => CanSummonDestroyed(engine, who, source, destroyed)
                    ? new List<CardInstance> { destroyed }
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{source.Name}: you can Special Summon {destroyed.Name} to your field in face-up Defense Position " +
                         "(Cancel = no).",
                AiChoose = list => list.FirstOrDefault(),
                AskWithOneCandidate = true,
                Cancel = () => engine.Log($"{source.Name}: {who.Name} does not use its effect."),
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{source.Name}: {who.Name} does not use its effect.");
                        return;
                    }

                    SummonToYourField(engine, who, source, pick);
                }
            });
        }

        static void SummonToYourField(DuelEngine engine, DuelistState who, CardInstance source, CardInstance card)
        {
            var gyOwner = GyHolding(engine, card);
            if (gyOwner == null)
            {
                engine.Log($"{source.Name}: {card.Name} is no longer in the GY — it is not Special Summoned.");
                return;
            }

            if (!CanSummonDestroyed(engine, who, source, card))
            {
                engine.Log($"{source.Name}: {card.Name} cannot be Special Summoned now.");
                return;
            }

            gyOwner.Graveyard.Remove(card);
            DuelEngine.RecordOwnerOnFieldEntry(card, gyOwner, who);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(who, card, BattlePosition.Defense, true))
            {
                engine.Log($"{source.Name}: {who.Name} Special Summons {card.Name} from the GY to their field in face-up " +
                           "Defense Position.");
                return;
            }

            engine.NextSpecialSummonFromGraveyard = false;
            card.ControlOwner = null;
            if (!gyOwner.Graveyard.Contains(card)) gyOwner.Graveyard.Add(card);
            engine.Log($"{source.Name}: {card.Name} could not be Special Summoned — it stays in the GY.");
        }

        /// <summary>
        /// <see cref="EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle"/>: the opponent's hand is empty — nothing;
        /// otherwise 1 card picked at random goes to its owner's Deck, which is shuffled.
        /// </summary>
        static void ReturnRandomHandCardToDeck(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var victim = engine.OpponentOf(who);
            if (victim?.Hand == null || victim.Hand.Count == 0)
            {
                engine.Log($"{source.Name}: {victim?.Name ?? "the opponent"} has no card in hand — nothing is returned.");
                return;
            }

            var i = engine.Rng.PickIndex(victim.Hand.Count);
            if (i < 0 || i >= victim.Hand.Count) i = 0;
            var card = victim.Hand[i];
            victim.Hand.Remove(card);
            var pile = PerCopyDeckRuntime.OwnersPile(victim, card) ?? victim;
            pile.Deck.Add(card.CardId);
            engine.Rng.Shuffle(pile.Deck);
            engine.Log($"{source.Name}: {card.Name}, picked at random from {victim.Name}'s hand, returns to the Deck, which " +
                       "is shuffled.");
        }

        // ───────────────────────────── Vampire Lady / Vampire Lord ─────────────────────────────

        /// <summary>
        /// <summary>
        /// Abyssal Designator (c89801755) at resolution. The Lua declares in its target step — AnnounceAttribute first,
        /// then AnnounceRace — and at resolution the opponent selects 1 monster with both from hand or Deck and sends it
        /// to the GY. Both declarations are made by the activator, so they go through AskCardPick one after the other;
        /// the final pick is the only one in the engine that may come from either pile.
        /// </summary>
        public static void DeclareTypeAndAttributeThenOpponentSends(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            var victim = engine.OpponentOf(who);
            if (victim == null) return;

            // 1. the Attribute (any of the seven — Lua ATTRIBUTE_ALL).
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () =>
                    TypeDeclarationRuntime.AttributeTokens(TypeDeclarationRuntime.AllAttributes).ToList(),
                Kind = EffectTargetKind.DeclareAttribute,
                Prompt = $"{source.Name}: declare 1 Attribute (DARK, DIVINE, EARTH, FIRE, LIGHT, WATER or WIND).",
                AiChoose = list => list.FirstOrDefault(),
                Finish = attrToken => DeclareTypeThenSend(engine, who, source, attrToken?.Def?.name ?? "", victim)
            });
        }

        /// <summary>The Monster Type declaration and the opponent's send, once the Attribute is known.</summary>
        static void DeclareTypeThenSend(DuelEngine engine, DuelistState who, CardInstance source, string attribute,
            DuelistState victim)
        {
            if (string.IsNullOrEmpty(attribute))
            {
                engine.Log($"{source.Name}: no Attribute declared — no effect.");
                return;
            }

            engine.Log($"{source.Name}: {who.Name} declares {attribute}.");

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => TypeDeclarationRuntime.MonsterTypeTokens(engine),
                Kind = EffectTargetKind.None,
                Prompt = $"{source.Name}: declare 1 Monster Type. Your opponent then sends 1 monster with both the " +
                         "declared Type and Attribute from their hand or Deck to the GY.",
                AiChoose = list => list.FirstOrDefault(),
                Finish = typeToken => OpponentSendsOfTypeAndAttribute(engine, source, victim,
                    typeToken?.Def?.name ?? "", attribute)
            });
        }

        /// <summary>
        /// The opponent's pick: 1 monster with both the declared Type and Attribute, from hand OR Deck — the engine's
        /// only pick that spans both piles. A hidden hand card is still a legal candidate: the declaration is exactly
        /// what makes it a legal guess.
        /// </summary>
        static void OpponentSendsOfTypeAndAttribute(DuelEngine engine, CardInstance source, DuelistState victim,
            string type, string attribute)
        {
            if (string.IsNullOrEmpty(type))
            {
                engine.Log($"{source.Name}: no Monster Type declared — no effect.");
                return;
            }

            var activator = engine.OpponentOf(victim);
            engine.Log($"{source.Name}: {activator?.Name ?? "The activator"} declared {type}/{attribute}.");

            List<CardInstance> Matching()
            {
                var list = new List<CardInstance>();
                var seen = new HashSet<int>();
                if (victim?.Hand != null)
                    foreach (var c in victim.Hand)
                    {
                        if (c?.Def == null) continue;
                        if (!string.Equals(c.Def.race, type, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(c.Def.attribute, attribute, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (seen.Add(c.CardId)) list.Add(c);
                    }

                if (victim?.Deck != null && engine.Database != null)
                    foreach (var id in victim.Deck)
                    {
                        if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null) continue;
                        if (!string.Equals(def.race, type, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(def.attribute, attribute, System.StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(engine.CreateCardInstance(id));
                    }

                return list;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, victim, source, new CardPickRequest
            {
                Candidates = Matching,
                Kind = EffectTargetKind.CardInOppHand,
                Prompt = $"{source.Name}: your opponent declared {type}/{attribute}. Select 1 monster of that Type and " +
                         "Attribute in your hand or Deck to send to the GY.",
                AiChoose = list => list.Count > 0 ? list[0] : null,
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{source.Name}: {victim.Name} controls no {type}/{attribute} monster in hand or Deck.");
                        return;
                    }

                    engine.SendCardToGrave(victim, pick);
                    engine.Log($"{source.Name}: {victim.Name} sends {pick.Name} to the GY.");
                }
            });
        }

        /// <see cref="EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck"/> (MfcLabCards3Runtime delegates here):
        /// <paramref name="who"/> declares Monster, Spell or Trap (Lua SelectOption; the AI declares the type most common in
        /// the opponent's Deck), then the opponent selects 1 card of that type in their Deck (a human through the Deck
        /// sheet) and sends it to the GY; none of that type: nothing happens.
        /// </summary>
        public static void DeclareCardTypeThenOpponentSendsFromDeck(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            var victim = engine.OpponentOf(who);
            if (victim == null) return;
            var names = ContinuousTrapsAndSpiritSupportRuntime.TypeNames;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => names.Select(ContinuousTrapsAndSpiritSupportRuntime.TypeToken).ToList(),
                Kind = EffectTargetKind.None,
                Prompt = $"{source.Name}: declare 1 card type (Monster, Spell or Trap) — your opponent sends 1 card of " +
                         "that type from their Deck to the GY.",
                AiChoose = list => list.ElementAtOrDefault(AiDeclare(engine, victim)) ?? list.FirstOrDefault(),
                Finish = token =>
                {
                    var kind = Array.IndexOf(names, token?.Def?.name ?? "");
                    if (kind < 0) kind = AiDeclare(engine, victim);
                    engine.Log($"{source.Name}: {who.Name} declares {names[kind]}.");
                    OpponentSendsOfKind(engine, who, victim, source, kind);
                }
            });
        }

        static int KindOf(CardDef def) =>
            def == null ? -1 : def.IsMonster ? 0 : def.IsSpell ? 1 : def.IsTrap ? 2 : -1;

        /// <summary>The AI declares the card type its opponent's Deck holds most of (Monster on a tie).</summary>
        static int AiDeclare(DuelEngine engine, DuelistState victim)
        {
            var counts = new int[3];
            if (engine?.Database != null && victim?.Deck != null)
                foreach (var id in victim.Deck)
                {
                    var k = engine.Database.TryGet(id, out var def) ? KindOf(def) : -1;
                    if (k >= 0) counts[k]++;
                }

            var best = 0;
            for (var k = 1; k < 3; k++)
                if (counts[k] > counts[best])
                    best = k;
            return best;
        }

        /// <summary>One card per distinct card of <paramref name="kind"/> in <paramref name="owner"/>'s Deck.</summary>
        static List<CardInstance> DeckCardsOfKind(DuelEngine engine, DuelistState owner, int kind)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in owner.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || KindOf(def) != kind) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        static void OpponentSendsOfKind(DuelEngine engine, DuelistState who, DuelistState victim, CardInstance source,
            int kind)
        {
            var what = ContinuousTrapsAndSpiritSupportRuntime.TypeNames[kind];
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, victim, source, new CardPickRequest
            {
                Candidates = () => DeckCardsOfKind(engine, victim, kind),
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = $"{source.Name}: select 1 {what} Card in your Deck to send to the GY.",
                AiChoose = list => list == null || list.Count == 0
                    ? null
                    : BattleOutcomeTriggersRuntime.AiSpellToSend(victim, list),
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{source.Name}: {victim.Name} has no {what} Card in their Deck.");
                        return;
                    }

                    BattleOutcomeTriggersRuntime.SendSpell(engine, who, victim, source, pick);
                }
            });
        }

        // ───────────────────────────── The Unhappy Girl ─────────────────────────────

        /// <summary>
        /// DamageStepRulesRuntime.BattleIndestructible (damage calculation): <paramref name="mine"/> is in face-up Attack
        /// Position with an un-negated <see cref="EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition"/>
        /// — it is not destroyed by this battle (damage is calculated normally).
        /// </summary>
        public static bool BattleIndestructible(DuelEngine engine, DuelistState side, CardInstance mine, CardInstance other)
        {
            if (engine == null || mine == null || !mine.FaceUp || mine.Position != BattlePosition.Attack) return false;
            var shield = LiveClause(mine, EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition);
            if (shield == null) return false;
            // CrvEmptyHandAndAuras batch: Reborn Zombie — only while its controller has no cards in hand.
            if (!AttackShieldsAndDamageZeroRuntime.WhileConditionMet(engine, mine, shield)) return false;
            engine.Log($"{mine.Name}: in face-up Attack Position, it is not destroyed by battle.");
            return true;
        }

        static bool GirlRecordLive(DuelEngine engine, CardInstance girl, GirlRecord rec) =>
            rec != null && MonsterOnField(engine, girl) && girl.FaceUp && girl.Position == BattlePosition.Attack &&
            girl.FieldEntryStamp == rec.Entry && girl.FaceDownStamp == rec.FaceDownStamp;

        /// <summary>
        /// DuelEngine, after damage calculation (ygopro EVENT_BATTLED): a battler with an un-negated
        /// <see cref="EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition"/> in face-up Attack
        /// Position records its battle target. A record that ended (it changed position, left the field) starts again.
        /// </summary>
        public static void NoteBattled(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return;
            foreach (var (girl, other) in new[] { (attacker, target), (target, attacker) })
            {
                if (!MonsterOnField(engine, girl) || !girl.FaceUp || girl.Position != BattlePosition.Attack) continue;
                if (LiveClause(girl, EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition) == null)
                    continue;
                var st = StateOf(engine);
                if (!st.Girls.TryGetValue(girl, out var rec) || !GirlRecordLive(engine, girl, rec))
                {
                    rec = new GirlRecord { Entry = girl.FieldEntryStamp, FaceDownStamp = girl.FaceDownStamp };
                    st.Girls[girl] = rec;
                }

                rec.Battled.RemoveAll(x => ReferenceEquals(x.Monster, other));
                rec.Battled.Add((other, other.FieldEntryStamp, other.FaceDownStamp));
                engine.Log($"{girl.Name}: {other.Name} battled it — it cannot attack or change its battle position while " +
                           $"{girl.Name} stays in face-up Attack Position.");
            }
        }

        /// <summary>
        /// TextEffectRuntime.NotifyPositionChanged (manual change, card effect, Flip Summon; ygopro EVENT_CHANGE_POS): a
        /// recording monster whose battle position changed loses its record.
        /// </summary>
        public static void NotePositionChanged(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Girls.Remove(card);
        }

        /// <summary>The Unhappy Girl's lock on <paramref name="monster"/>: battled a live, un-negated recording monster of its
        /// controller's opponent, on this field presence and never turned face-down since.</summary>
        static bool GirlLocks(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null || !s_state.TryGetValue(engine, out var st) || st.Girls.Count == 0)
                return false;
            var mc = engine.ControllerOf(monster);
            if (mc == null) return false;
            foreach (var kv in st.Girls.ToList())
            {
                var girl = kv.Key;
                if (!GirlRecordLive(engine, girl, kv.Value))
                {
                    st.Girls.Remove(girl);
                    continue;
                }

                if (LiveClause(girl, EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition) == null)
                    continue;
                if (ReferenceEquals(engine.ControllerOf(girl), mc)) continue;
                if (kv.Value.Battled.Any(x => ReferenceEquals(x.Monster, monster) &&
                                              x.Entry == monster.FieldEntryStamp &&
                                              x.FaceDownStamp == monster.FaceDownStamp))
                    return true;
            }

            return false;
        }

        /// <summary>DuelEngine.ContinuousCannotAttackBlocks: <paramref name="attacker"/> battled a face-up Attack Position
        /// The Unhappy Girl of its opponent and cannot attack while it stays so.</summary>
        public static bool AttackForbidden(DuelEngine engine, DuelistState attackerController, CardInstance attacker) =>
            GirlLocks(engine, attacker);

        // ───────────────────────────── Judgment of the Desert ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition"/>, the activation resolves:
        /// the record starts now — every monster on the field is looked at once, so only a later flip records it.
        /// </summary>
        public static void StartFlipRecord(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || source == null) return;
            if (!SpellTrapOnField(engine, source) || !source.FaceUp)
            {
                engine.Log($"{source.Name} is no longer face-up on the field — nothing is recorded.");
                return;
            }

            var rec = new FlipRecord { Entry = source.FieldEntryStamp, FaceDownStamp = source.FaceDownStamp };
            foreach (var m in FieldMonsters(engine))
                rec.Seen[m] = (m.FieldEntryStamp, m.FaceUpStamp);
            StateOf(engine).Flips[source] = rec;
            engine.Log($"{source.Name}: from now on, the battle positions of monsters flipped face-up cannot be changed " +
                       "except by a card effect.");
        }

        static bool FlipRecordLive(DuelEngine engine, CardInstance card, FlipRecord rec) =>
            rec != null && SpellTrapOnField(engine, card) && card.FaceUp && card.FieldEntryStamp == rec.Entry &&
            card.FaceDownStamp == rec.FaceDownStamp;

        /// <summary>
        /// Look at every monster on the field for each live record: one whose face-up stamp grew on the same field presence
        /// was flipped face-up since the last look and is recorded while the card applies (face-up, not negated). A new
        /// field presence is only noted (a Set monster is seen at the board refresh after it is Set, before it can flip).
        /// </summary>
        static void Scan(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Flips.Count == 0) return;
            foreach (var kv in st.Flips.ToList())
            {
                var card = kv.Key;
                var rec = kv.Value;
                if (!FlipRecordLive(engine, card, rec))
                {
                    st.Flips.Remove(card);
                    continue;
                }

                var applies = LiveClause(card, EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition) != null;
                var onField = new HashSet<CardInstance>();
                foreach (var m in FieldMonsters(engine))
                {
                    onField.Add(m);
                    if (applies && m.FaceUp && rec.Seen.TryGetValue(m, out var seen) && seen.Entry == m.FieldEntryStamp &&
                        m.FaceUpStamp != seen.FaceUpStamp && !rec.Recorded.TryGetValue(m, out _))
                    {
                        rec.Recorded[m] = m.FieldEntryStamp;
                        engine.Log($"{card.Name}: {m.Name} was flipped face-up — its battle position cannot be changed " +
                                   "except by a card effect.");
                    }

                    rec.Seen[m] = (m.FieldEntryStamp, m.FaceUpStamp);
                }

                foreach (var gone in rec.Seen.Keys.Where(m => !onField.Contains(m)).ToList())
                    rec.Seen.Remove(gone);
                foreach (var gone in rec.Recorded.Where(x => !onField.Contains(x.Key) || x.Key.FieldEntryStamp != x.Value)
                             .Select(x => x.Key).ToList())
                    rec.Recorded.Remove(gone);
            }
        }

        static bool FlipLocks(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null || !s_state.TryGetValue(engine, out var st) || st.Flips.Count == 0)
                return false;
            Scan(engine);
            foreach (var kv in st.Flips)
            {
                if (!kv.Value.Recorded.TryGetValue(monster, out var entry) || entry != monster.FieldEntryStamp) continue;
                if (LiveClause(kv.Key, EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition) == null)
                    continue;
                return true;
            }

            return false;
        }

        // ───────────────────────────── Curse of Anubis ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock"/>: every face-up Attack
        /// Position Effect Monster on the field changes to face-up Defense Position at the same time (its "position changed"
        /// triggers wait for one Chain, as Curse of Fiend's); each of them unaffected by nothing here gets original DEF 0 and
        /// the rest-of-turn manual-position lock (PositionLocksRuntime.LockRestOfTurn), until the end of the turn.
        /// </summary>
        public static void ChangeToDefenseZeroDefLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var plan = new List<(DuelistState Side, CardInstance Monster)>();
            foreach (var side in new[] { who, engine.OpponentOf(who) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                    if (IsFaceUpAttackEffectMonster(m) && !ContinuousProtections.IsUnaffectedBy(engine, m, source))
                        plan.Add((side, m));
            }

            if (plan.Count == 0)
            {
                engine.Log($"{source?.Name}: no face-up Attack Position Effect Monster on the field.");
                return;
            }

            var changed = new List<CardInstance>();
            SimultaneousTriggersRuntime.Capture(engine, () =>
            {
                foreach (var (side, m) in plan)
                {
                    if (!ReferenceEquals(engine.ControllerOf(m), side)) continue;
                    if (engine.ApplyEffectPositionChange(m, BattlePosition.Defense, faceUp: true))
                        changed.Add(m);
                }
            });

            foreach (var (_, m) in plan)
            {
                if (!MonsterOnField(engine, m) || !m.FaceUp) continue;
                SetOriginalDefZero(engine, m);
                PositionLocksRuntime.LockRestOfTurn(engine, m);
            }

            engine.Log($"{source?.Name}: " + string.Join(", ", plan.Select(x => x.Monster.Name)) +
                       " — Defense Position, original DEF 0 and battle positions locked until the end of this turn.");
            foreach (var m in changed)
                if (m.FaceUp && engine.ControllerOf(m) != null)
                    SimultaneousTriggersRuntime.QueueThisCardPositionChanged(engine, m, true);
        }

        /// <summary>ygopro EFFECT_SET_BASE_DEFENSE 0 (RESETS_STANDARD_PHASE_END). While its original ATK/DEF are switched this
        /// turn, the shown original DEF comes from the ATK override (SummonLocksAndStatRules' convention).</summary>
        static void SetOriginalDefZero(DuelEngine engine, CardInstance m)
        {
            var atkField = m.SwapOriginalAtkDefThisTurn;
            StateOf(engine).DefZero.Add(new BaseDefZero
            {
                Card = m,
                AtkField = atkField,
                Prior = atkField ? m.OriginalAtkOverride : m.OriginalDefOverride,
                Entry = m.FieldEntryStamp,
                FaceDownStamp = m.FaceDownStamp,
                Turn = engine.TurnNumber
            });
            if (atkField) m.OriginalAtkOverride = 0;
            else m.OriginalDefOverride = 0;
        }

        static void Revert(BaseDefZero z)
        {
            if (z.AtkField) z.Card.OriginalAtkOverride = z.Prior;
            else z.Card.OriginalDefOverride = z.Prior;
        }

        /// <summary>DuelEngine.ClearUntilEndOfTurnStatMods (End Phase): every original DEF set to 0 this turn returns.</summary>
        public static void EndOfTurn(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.DefZero.Count == 0) return;
            for (var i = st.DefZero.Count - 1; i >= 0; i--) Revert(st.DefZero[i]);
            st.DefZero.Clear();
        }

        // ───────────────────────────── shared hooks ─────────────────────────────

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: the flip records look at the board; an original DEF 0 whose monster left the
        /// field, was turned face-down (RESETS_STANDARD) or outlived its turn returns; ended Unhappy Girl records go.
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            Scan(engine);
            for (var i = st.DefZero.Count - 1; i >= 0; i--)
            {
                var z = st.DefZero[i];
                if (MonsterOnField(engine, z.Card) && z.Card.FaceUp && z.Card.FieldEntryStamp == z.Entry &&
                    z.Card.FaceDownStamp == z.FaceDownStamp && z.Turn == engine.TurnNumber)
                    continue;
                Revert(z);
                st.DefZero.RemoveAt(i);
            }

            foreach (var kv in st.Girls.ToList())
                if (!GirlRecordLive(engine, kv.Key, kv.Value))
                    st.Girls.Remove(kv.Key);
        }

        /// <summary>
        /// PositionLocksRuntime.ManualPositionChangeLocked (DuelEngine.CanChangePosition / CanFlipSummon /
        /// IsBattlePositionLocked): ygopro EFFECT_CANNOT_CHANGE_POSITION from The Unhappy Girl or Judgment of the Desert —
        /// no manual change and no Flip Summon; a card effect still changes it.
        /// </summary>
        public static bool ManualPositionChangeLocked(DuelEngine engine, CardInstance card) =>
            GirlLocks(engine, card) || FlipLocks(engine, card);

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain (Curse of Anubis).</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || c == null) return false;
            switch (c.ActivationCondition)
            {
                case FaceUpAttackEffectMonsterOnField:
                    ok = FieldMonsters(engine).Any(IsFaceUpAttackEffectMonster);
                    reason = "Needs a face-up Attack Position Effect Monster on the field.";
                    return true;
                default:
                    return false;
            }
        }
    }
}
