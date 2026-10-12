using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "IgnitionCostsAndSimpleSpells" runtime (atoms: <see cref="IgnitionCostsAndSimpleSpellsAtoms"/>):
    /// <list type="bullet">
    /// <item>activation conditions: N face-up copies of a named monster you control and enough Deck to draw (3-Hump Lacooda),
    /// a Spell/Trap the opponent controls (Lady Ninja Yae), a face-up monster of an Attribute you control (Solar Ray);</item>
    /// <item>a named hand card as a send cost (<see cref="NamedHandCard"/>, Avatar of The Pot; read by
    /// TextEffectRuntime.CollectDiscardCost);</item>
    /// <item>the per-count burn source "face-up monsters of an Attribute you control" (<see cref="BurnCount"/>, Solar Ray);</item>
    /// <item>Sand Gambler's 3-coin toss through the coin bus (<see cref="ResolveCoinGamble"/>);</item>
    /// <item>Mystik Wok: the Tribute candidates (ATK or DEF above 0), the Tributed monster's ATK/DEF noted as the cost is paid,
    /// the ATK-or-DEF choice made as the card is activated, and the LP gain at resolution.</item>
    /// </list>
    /// </summary>
    public static class IgnitionCostsAndSimpleSpellsRuntime
    {
        // ── EffectClause.ActivationCondition values ──

        /// <summary>
        /// At least ConditionAmount face-up monsters named ConditionName in your Monster Zone (3-Hump Lacooda: Lua
        /// IsExistingMatchingCard(faceup + IsCode, LOCATION_MZONE, 0, 3)); a Draw clause also needs that many Deck cards
        /// (Lua IsPlayerCanDraw(tp, Amount) in s.target).
        /// </summary>
        public const string FaceUpNamedYouControlAtLeast = "IcssFaceUpNamedYouControlAtLeast";

        /// <summary>The opponent controls a Spell/Trap Card (Spell &amp; Trap Zones or Field Zone, face-up or Set).</summary>
        public const string OpponentControlsSpellTrap = "IcssOpponentControlsSpellTrap";

        /// <summary>You control at least ConditionAmount face-up monsters whose current Attribute is ConditionName.</summary>
        public const string YouControlFaceUpAttribute = "IcssYouControlFaceUpAttribute";

        /// <summary>
        /// Mystik Wok: activated outside a response window, where its own activation path (StartIgnition) pays the Tribute.
        /// A response-window seat (TextEffectRuntime.SeatResponseOnChain) pays no generic Tribute cost, and a chain answer
        /// refuses a cost that needs a pick, so the card is not offered there (fail closed).
        /// </summary>
        public const string TributeCostPaidOnActivation = "IcssTributeCostPaidOnActivation";

        // ── EffectClause.CountSource values (InflictDamageToOpponent; PerCountBurnsRuntime.BurnCount) ──

        /// <summary>Face-up monsters you control whose current Attribute is ConditionName, counted at resolution (Solar Ray).</summary>
        public const string YourFaceUpMonstersOfConditionAttribute = "IcssYourFaceUpMonstersOfConditionAttribute";

        // ── A named hand card as the cost (EffectClause.DiscardCostAttribute) ──

        /// <summary>
        /// DiscardCostAttribute for "1 "X" from your hand": the card name in double quotes. CollectDiscardCost then offers only
        /// hand cards with that name (Spells included); with DiscardCostIsSend the card is sent, not discarded.
        /// </summary>
        public static string NamedHandCard(string name) => "\"" + name + "\"";

        /// <summary>The card name of a <see cref="NamedHandCard"/> attribute, or null.</summary>
        public static string NamedHandCardName(string attribute) =>
            attribute != null && attribute.Length > 2 && attribute[0] == '"' && attribute[attribute.Length - 1] == '"'
                ? attribute.Substring(1, attribute.Length - 2)
                : null;

        /// <summary>TextEffectRuntime.CollectDiscardCost: <paramref name="card"/> matches a named-card cost attribute.</summary>
        public static bool MatchesNamedHandCard(CardInstance card, string name) =>
            card?.Def != null && !string.IsNullOrEmpty(name) &&
            (card.IsNamed(name) || string.Equals(card.Def.name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The prompt of a human's send-cost pick (StartIgnition): "send 1 "Pot of Greed" from your hand to the GY (cost)".
        /// Null for any other cost.
        /// </summary>
        public static string SendCostPrompt(EffectClause c)
        {
            if (c == null || !c.RequiresDiscardCost || !c.DiscardCostIsSend) return null;
            var name = NamedHandCardName(c.DiscardCostAttribute);
            return name != null
                ? $"send 1 \"{name}\" from your hand to the GY (cost), or Cancel to not activate it."
                : "send 1 card from your hand to the GY (cost), or Cancel to not activate it.";
        }

        // ── Activation conditions ──

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet default chain: the conditions this batch owns. False when the clause's
        /// condition is not one of them.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            var min = c.ConditionAmount > 0 ? c.ConditionAmount : 1;
            switch (c.ActivationCondition)
            {
                case FaceUpNamedYouControlAtLeast:
                {
                    var n = who.MonstersOnField().Count(m =>
                        m != null && m.FaceUp && MatchesNamedHandCard(m, c.ConditionName));
                    if (n < min)
                    {
                        reason = $"Needs {min} face-up \"{c.ConditionName}\" on your side of the field.";
                        return true;
                    }

                    if (c.Action == EffectActionKind.Draw && !DrawThenDiscardRuntime.CanDraw(engine, who, c.Amount))
                    {
                        reason = $"You must be able to draw {Math.Max(1, c.Amount)} card(s).";
                        return true;
                    }

                    ok = true;
                    return true;
                }
                case OpponentControlsSpellTrap:
                {
                    var opp = engine.OpponentOf(who);
                    ok = opp != null && opp.SpellTrapsOnField().Any(st => st != null);
                    reason = "Your opponent must control a Spell or Trap Card.";
                    return true;
                }
                case YouControlFaceUpAttribute:
                    ok = CountFaceUpOfAttribute(who, c.ConditionName) >= min;
                    reason = $"You must control a face-up {c.ConditionName} monster.";
                    return true;
                case TributeCostPaidOnActivation:
                    ok = engine.PendingResponse == null || !ReferenceEquals(engine.PendingResponse.Responder, who);
                    reason = "Its Tribute cost cannot be paid in this response window.";
                    return true;
                default:
                    return false;
            }
        }

        // ── Per-count burn (Solar Ray) ──

        /// <summary>
        /// PerCountBurnsRuntime.BurnCount default: the count for a CountSource this batch owns, or -1.
        /// </summary>
        public static int BurnCount(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            if (clause == null || clause.CountSource != YourFaceUpMonstersOfConditionAttribute) return -1;
            return CountFaceUpOfAttribute(who, clause.ConditionName);
        }

        static int CountFaceUpOfAttribute(DuelistState who, string attribute)
        {
            if (who == null || string.IsNullOrEmpty(attribute)) return 0;
            return who.MonstersOnField().Count(m =>
                m != null && m.FaceUp &&
                string.Equals(EquipFamilyRuntime.CurrentAttribute(m), attribute, StringComparison.OrdinalIgnoreCase));
        }

        // ── Sand Gambler ──

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours"/>: toss the
        /// coins through the coin bus (Second Coin Toss may redo them first); all Heads destroys every monster the opponent
        /// controls, all Tails every monster <paramref name="who"/> controls, together.
        /// </summary>
        public static void ResolveCoinGamble(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var n = clause.CoinCount > 0 ? clause.CoinCount : 3;
            // A result is good for the tosser unless every coin is Tails (its own monsters are destroyed).
            CoinDiceInterruptsRuntime.TossCoins(engine, who, source, n, r => r.Any(h => h), r =>
            {
                using var redoScope = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                var heads = r.Count(h => h);
                engine.Log($"{source?.Name}: {heads}/{n} Heads.");
                var victim = heads == n ? engine.OpponentOf(who) : heads == 0 ? who : null;
                if (victim == null) return;
                var doomed = victim.MonstersOnField().Where(m => m != null).ToList();
                if (doomed.Count == 0) return;
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var m in doomed)
                        TextEffectRuntime.DestroyCard(engine, m, source, false, who);
                });
            });
        }

        /// <summary>
        /// SimpleAi: the AI does not gamble Sand Gambler's effect when it cannot gain from all Heads (the opponent controls no
        /// monster), since all Tails would still destroy its own.
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance m)
        {
            var prog = m?.Def != null ? CompiledEffectCache.GetOrCompile(m.Def) : null;
            if (prog == null || !prog.FullyCompiled) return false;
            if (!prog.ClausesFor(EffectTiming.Activate).Exists(c =>
                    c != null && c.Action == EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours))
                return false;
            var opp = engine?.OpponentOf(ai);
            return opp == null || opp.MonsterCount == 0;
        }

        // ── Mystik Wok ──

        sealed class TributeNote
        {
            public CardInstance Monster;
            public int Atk;
            public int Def;
        }

        sealed class GainChoice
        {
            public bool Chosen;
            public int Value;
        }

        /// <summary>The monster each card last Tributed as its cost, with its ATK/DEF as it was Tributed.</summary>
        static readonly ConditionalWeakTable<CardInstance, TributeNote> s_tributes = new();

        /// <summary>Mystik Wok's ATK-or-DEF choice made on activation, read when its Chain Link resolves.</summary>
        static readonly ConditionalWeakTable<CardInstance, GainChoice> s_choices = new();

        static bool IsTributedStatGain(EffectClause c) =>
            c != null && c.Action == EffectActionKind.GainLpEqualToTributedAtkOrDef;

        /// <summary>
        /// TextEffectRuntime.CollectTributeCost: Mystik Wok Tributes only a monster whose ATK or DEF is above 0 (Lua
        /// s.costfilter, GetAttack() &gt; 0 or GetDefense() &gt; 0).
        /// </summary>
        public static void FilterTributeCostCandidates(EffectClause c, List<CardInstance> list)
        {
            if (!IsTributedStatGain(c) || list == null) return;
            list.RemoveAll(m => m == null || (m.CurrentAtk <= 0 && m.CurrentDef <= 0));
        }

        /// <summary>
        /// TextEffectRuntime.PayOneIgnitionCostCore / PayAutoIgnitionCostsCore, before a Tributed cost monster leaves the
        /// field: note its current ATK and DEF for <paramref name="source"/>.
        /// </summary>
        public static void NoteTributeCost(CardInstance source, CardInstance monster)
        {
            if (source == null || monster == null) return;
            s_tributes.Remove(source);
            s_tributes.Add(source, new TributeNote
            {
                Monster = monster,
                Atk = Math.Max(0, monster.CurrentAtk),
                Def = Math.Max(0, monster.CurrentDef)
            });
        }

        static TributeNote NoteOf(CardInstance source) =>
            source != null && s_tributes.TryGetValue(source, out var n) ? n : null;

        static int AiValue(TributeNote n) => n == null ? 0 : Math.Max(n.Atk, n.Def);

        static void SetChoice(CardInstance source, int value)
        {
            if (source == null) return;
            var c = s_choices.GetOrCreateValue(source);
            c.Chosen = true;
            c.Value = Math.Max(0, value);
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause, before a compiled activation is recorded on its Chain Link: Mystik Wok's
        /// ATK-or-DEF choice is made now, as the card is activated (Lua SelectEffect in s.target). Only the stat above 0 is
        /// offered when the other is 0; the AI takes the higher one. A human taps the Tributed monster (in the GY) for its
        /// ATK, or CANCEL for its DEF. A Tributed Token (no card left to tap) takes the higher value.
        /// </summary>
        public static void ChooseOnActivation(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (!IsTributedStatGain(clause) || engine == null || who == null || source == null) return;
            if (!CompiledActivationChainRuntime.IsRecording(source)) return;
            s_choices.Remove(source);
            var note = NoteOf(source);
            if (note == null)
            {
                SetChoice(source, 0);
                return;
            }

            if (note.Atk <= 0 || note.Def <= 0 || !engine.IsHumanControlled(who))
            {
                SetChoice(source, AiValue(note));
                return;
            }

            AskAtkOrDef(engine, who, source, note, duringActivation: true, then: null);
        }

        static void AskAtkOrDef(DuelEngine engine, DuelistState who, CardInstance source, TributeNote note,
            bool duringActivation, Action<int> then)
        {
            void Done(int value)
            {
                if (duringActivation) SetChoice(source, value);
                then?.Invoke(value);
            }

            var monster = note.Monster;
            List<CardInstance> Candidates()
            {
                var list = new List<CardInstance>();
                if (monster != null && !monster.IsToken &&
                    (engine.Player.Graveyard.Contains(monster) || engine.Opponent.Graveyard.Contains(monster)))
                    list.Add(monster);
                return list;
            }

            if (Candidates().Count == 0)
            {
                Done(AiValue(note));
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{source.Name}: tap {monster.Name} to gain its ATK ({note.Atk} LP), or CANCEL to gain its DEF " +
                         $"({note.Def} LP).",
                AskWithOneCandidate = true,
                AiChoose = list => note.Atk >= note.Def ? list.FirstOrDefault() : null,
                DuringActivation = duringActivation,
                HoldsChain = !duringActivation,
                Finish = m => Done(m != null ? note.Atk : note.Def),
                Cancel = () => Done(note.Def)
            });
        }

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.GainLpEqualToTributedAtkOrDef"/> (the Chain Link resolves): gain
        /// the value chosen on activation. An activation that was not recorded on a Chain Link chooses now (the AI or a lone
        /// stat at once; a human through the same pick).
        /// </summary>
        public static void ResolveTributedStatGain(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            var note = NoteOf(source);
            s_tributes.Remove(source); // a Tribute is used by one resolution only
            if (s_choices.TryGetValue(source, out var choice) && choice.Chosen)
            {
                s_choices.Remove(source);
                Gain(engine, who, source, choice.Value);
                return;
            }

            if (note == null)
            {
                engine.Log($"{source.Name}: no Tributed monster recorded — no LP gained.");
                return;
            }

            if (note.Atk <= 0 || note.Def <= 0 || !engine.IsHumanControlled(who))
            {
                Gain(engine, who, source, AiValue(note));
                return;
            }

            AskAtkOrDef(engine, who, source, note, duringActivation: false, then: v => Gain(engine, who, source, v));
        }

        static void Gain(DuelEngine engine, DuelistState who, CardInstance source, int value)
        {
            if (value <= 0)
            {
                engine.Log($"{source?.Name}: 0 LP to gain.");
                return;
            }

            TextEffectRuntime.ApplyLifePointGain(engine, who, value, source?.Name);
        }
    }
}
