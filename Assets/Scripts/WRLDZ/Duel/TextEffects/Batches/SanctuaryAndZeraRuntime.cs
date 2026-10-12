using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SanctuaryAndZera batch (Ancient Sanctuary) — runtime for <see cref="SanctuaryAndZeraAtoms"/>:
    /// <list type="bullet">
    /// <item>the named-Tribute hand procedure (Archlord Zerato, Mazera DeVille): ToonsRuntime hands it over through
    /// <see cref="IsNamedTributeProcedure"/>; the engine picks the face-up named monster (a human with 2+ chooses one);</item>
    /// <item>"If "X" is not on (your side of) the field, this effect is not applied" (<see cref="NotApplied"/>);</item>
    /// <item>activation conditions (<see cref="TryActivationCondition"/>): your LP lead (Saturn), an opponent's monster
    /// (Zerato), a card in the opponent's hand or on their field (Light of Judgment);</item>
    /// <item>Mars' ATK/DEF = your LP lead while you control the named card (<see cref="LpLeadWhileNamedYouControl"/>, the
    /// FieldSpellEffects.ApplySelfScale count source);</item>
    /// <item>Saturn's damage (<see cref="ApplyLpLeadDamage"/>) and Light of Judgment's choice (<see cref="ResolveSendOrDiscard"/>).</item>
    /// </list>
    /// </summary>
    public static class SanctuaryAndZeraRuntime
    {
        /// <summary>ActivationCondition: your opponent controls a monster (Archlord Zerato's destg).</summary>
        public const string OppControlsMonster = "SazOppControlsMonster";
        /// <summary>ActivationCondition: your LP are higher than your opponent's (The Agent of Judgment - Saturn).</summary>
        public const string YourLpHigher = "SazYourLpHigher";
        /// <summary>ActivationCondition: your opponent has a card on the field or in the hand (Light of Judgment's target chk).</summary>
        public const string OppHandOrFieldCard = "SazOppHandOrFieldCard";
        /// <summary>
        /// GainSelfAtkPerCount <see cref="EffectClause.CountSource"/> (The Agent of Force - Mars): your LP minus your
        /// opponent's while you control a face-up <see cref="EffectClause.NamedCard"/>, else 0 (never negative).
        /// </summary>
        public const string LpLeadWhileNamedYouControl = "SazLpLeadWhileNamedYouControl";

        // ───────────────────────────── shared checks ─────────────────────────────

        /// <summary>Your LP minus your opponent's, or 0 when that is not positive.</summary>
        public static int LpLead(DuelEngine engine, DuelistState who)
        {
            var opp = engine?.OpponentOf(who);
            if (who == null || opp == null) return 0;
            var lead = who.LifePoints - opp.LifePoints;
            return lead > 0 ? lead : 0;
        }

        /// <summary>
        /// A face-up card named <paramref name="name"/> is on <paramref name="who"/>'s side of the field
        /// (<paramref name="yourSide"/>) or on either field (FieldSpellEffects.NamedCardIsFaceUpOnField: an activation still
        /// on the Chain does not count yet).
        /// </summary>
        public static bool NamedFaceUp(DuelEngine engine, DuelistState who, string name, bool yourSide) =>
            yourSide
                ? FieldSpellEffects.ControlsFaceUpNamed(who, name)
                : FieldSpellEffects.NamedCardIsFaceUpOnField(engine, name);

        /// <summary>
        /// TextEffectRuntime.ApplyClause: "If "X" is not on (your side of) the field, this effect is not applied." The clause
        /// resolves with no effect when the named card is gone (its costs and oaths were already paid).
        /// </summary>
        public static bool NotApplied(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || c == null || string.IsNullOrEmpty(c.NotAppliedUnlessNamedFaceUp)) return false;
            if (NamedFaceUp(engine, who, c.NotAppliedUnlessNamedFaceUp, c.RequiresControllerNamedCard)) return false;
            engine.Log($"{source?.Name ?? "Effect"}: \"{c.NotAppliedUnlessNamedFaceUp}\" is not " +
                       (c.RequiresControllerNamedCard ? "on your side of the field" : "on the field") +
                       " — this effect is not applied.");
            return true;
        }

        /// <summary>TextEffectRuntime.ActivationConditionsMet: this batch's condition strings.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            var opp = engine.OpponentOf(who);
            switch (c.ActivationCondition)
            {
                case OppControlsMonster:
                    ok = opp != null && opp.MonsterCount > 0;
                    reason = "Your opponent controls no monster.";
                    return true;
                case YourLpHigher:
                    ok = LpLead(engine, who) > 0;
                    reason = "Only while your LP are higher than your opponent's.";
                    return true;
                case OppHandOrFieldCard:
                    ok = opp != null && (OppFieldCards(engine, opp).Count > 0 || opp.Hand.Any(h => h != null));
                    reason = "Your opponent has no card in the hand or on the field.";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>FieldSpellEffects.ApplySelfScale (Mars): the LP lead while its controller controls the named card.</summary>
        public static int LpLeadCount(DuelEngine engine, DuelistState who, EffectClause c)
        {
            if (engine == null || who == null || c == null || string.IsNullOrEmpty(c.NamedCard)) return 0;
            return FieldSpellEffects.ControlsFaceUpNamed(who, c.NamedCard) ? LpLead(engine, who) : 0;
        }

        // ───────────────────────────── The Agent of Judgment - Saturn ─────────────────────────────

        /// <summary>Inflict damage to the opponent equal to your LP lead, read now (nothing when it is not positive).</summary>
        public static void ApplyLpLeadDamage(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var lead = LpLead(engine, who);
            if (lead <= 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: your LP are not higher than your opponent's — no damage.");
                return;
            }

            engine.ApplyEffectDamage(opp, lead, source?.Name, source);
        }

        // ───────────────────────────── Light of Judgment ─────────────────────────────

        /// <summary>Every card <paramref name="opp"/> controls: Monster, Spell &amp; Trap, Field and Pendulum Zones.</summary>
        static List<CardInstance> OppFieldCards(DuelEngine engine, DuelistState opp)
        {
            var list = new List<CardInstance>();
            if (opp == null) return list;
            foreach (var m in opp.MonstersOnField())
                if (m != null) list.Add(m);
            foreach (var st in opp.SpellTrapsOnField())
                if (st != null && !list.Contains(st)) list.Add(st);
            var field = opp.FieldSpellZone?.Occupant;
            if (field != null && !list.Contains(field)) list.Add(field);
            if (opp.PendulumZones != null)
                foreach (var z in opp.PendulumZones)
                    if (z?.Occupant != null && !list.Contains(z.Occupant))
                        list.Add(z.Occupant);
            return list;
        }

        static List<CardInstance> HandCards(DuelistState opp) =>
            opp?.Hand?.Where(h => h != null).ToList() ?? new List<CardInstance>();

        /// <summary>The AI's field pick: the opponent's strongest monster, else any other card they control.</summary>
        static CardInstance AiFieldPick(List<CardInstance> list) =>
            list?.Where(c => c != null)
                .OrderByDescending(c => c.Def != null && c.Def.IsMonster && c.FaceUp ? 2 : c.Def != null && c.Def.IsMonster ? 1 : 0)
                .ThenByDescending(c => c.CurrentAtk)
                .FirstOrDefault();

        /// <summary>The AI's hand pick: the most valuable card.</summary>
        static CardInstance AiHandPick(List<CardInstance> list) =>
            list?.Where(c => c != null).OrderByDescending(AiHeuristicPolicy.DiscardPriority).FirstOrDefault();

        /// <summary>
        /// Light of Judgment's resolution (Lua s.activate): only the options that still have cards are offered. A human taps
        /// a card the opponent controls to send it to the GY, or taps this card (or CANCEL) to look at the opponent's hand and
        /// discard 1 card from it; the AI sends the opponent's strongest monster, else discards from the hand, else sends
        /// another card. Neither choice targets.
        /// </summary>
        public static void ResolveSendOrDiscard(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null || engine.GameOver) return;
            var field = OppFieldCards(engine, opp);
            var hand = HandCards(opp);
            var name = source?.Name ?? "Light of Judgment";
            if (field.Count == 0 && hand.Count == 0)
            {
                engine.Log($"{name}: your opponent has no card on the field or in the hand — no effect.");
                return;
            }

            if (field.Count == 0)
            {
                DiscardFromHand(engine, who, opp, source);
                return;
            }

            if (hand.Count == 0)
            {
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                {
                    Candidates = () => OppFieldCards(engine, opp),
                    Kind = EffectTargetKind.AnyCardOnField,
                    Prompt = $"{name}: choose 1 card your opponent controls to send to the GY.",
                    AiChoose = AiFieldPick,
                    Finish = picked => SendToGy(engine, who, opp, source, picked)
                });
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                var best = AiFieldPick(field);
                if (best != null && best.Def != null && best.Def.IsMonster)
                    SendToGy(engine, who, opp, source, best);
                else
                    DiscardFromHand(engine, who, opp, source);
                return;
            }

            // Both options: this card (when it is still on the field) or CANCEL stands for the hand option.
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () =>
                {
                    var list = OppFieldCards(engine, opp);
                    if (source != null && engine.ControllerOf(source) == who && !list.Contains(source))
                        list.Add(source);
                    return list;
                },
                Kind = EffectTargetKind.AnyCardOnField,
                Prompt = $"{name}: tap a card your opponent controls to send it to the GY, or tap {name} (or CANCEL) " +
                         "to look at your opponent's hand and discard 1 card from it.",
                AiChoose = AiFieldPick,
                AskWithOneCandidate = true,
                Finish = picked =>
                {
                    if (picked == null || picked == source || engine.ControllerOf(picked) != opp)
                        DiscardFromHand(engine, who, opp, source);
                    else
                        SendToGy(engine, who, opp, source, picked);
                },
                Cancel = () => DiscardFromHand(engine, who, opp, source)
            });
        }

        static void SendToGy(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source, CardInstance card)
        {
            var name = source?.Name ?? "Effect";
            if (card == null || engine.ControllerOf(card) != opp)
            {
                engine.Log($"{name}: that card is no longer on your opponent's field — nothing is sent.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, card, source))
            {
                engine.Log($"{name}: {card.Name} is unaffected — it stays on the field.");
                return;
            }

            var shown = card.FaceUp ? card.Name : $"a face-down card ({card.Name})";
            engine.SendCardToGrave(opp, card, sentBy: source);
            engine.Log($"{name}: {shown} is sent to {opp.Name}'s GY.");
            engine.NotifyPublic();
        }

        static void DiscardFromHand(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source)
        {
            var name = source?.Name ?? "Effect";
            var hand = HandCards(opp);
            if (hand.Count == 0)
            {
                engine.Log($"{name}: {opp.Name} has no card in hand — nothing is discarded.");
                return;
            }

            FlipDownAndPeekRuntime.LogLook(engine, who, source, $"{opp.Name}'s hand", hand);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => HandCards(opp),
                Kind = EffectTargetKind.CardInOppHand,
                Prompt = $"{name}: choose 1 card in your opponent's hand to discard.",
                AiChoose = AiHandPick,
                Finish = picked =>
                {
                    if (picked == null || !opp.Hand.Contains(picked)) return;
                    DiscardTriggersRuntime.BeginScope(engine);
                    try
                    {
                        var gone = MonsterEffects.DiscardFromHandByInstance(opp, picked, DiscardCause.Effect, who);
                        if (gone == null) return;
                        engine.Log($"{opp.Name} discards {gone.Name} ({name}).");
                        DiscardTriggersRuntime.NoteDiscard(engine, opp, gone, who);
                    }
                    finally
                    {
                        DiscardTriggersRuntime.EndScope(engine);
                    }

                    engine.Rng.Shuffle(opp.Hand);
                    engine.NotifyPublic();
                }
            });
        }

        // ───────────────────────────── named-Tribute hand procedure ─────────────────────────────

        /// <summary>The hand procedure Tributes 1 named monster (Archlord Zerato, Mazera DeVille).</summary>
        public static bool IsNamedTributeProcedure(EffectClause proc) =>
            proc != null && proc.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
            !string.IsNullOrEmpty(proc.TributeNamedCard);

        /// <summary>
        /// The monsters <paramref name="who"/> may Tribute: a face-up monster with the name on their own side of the field
        /// that can be Tributed (not one that "cannot be Tributed" at all, Archfiend's Roar).
        /// </summary>
        static List<CardInstance> NamedTributes(DuelistState who, EffectClause proc) =>
            who?.MonstersOnField()
                .Where(m => m != null && (m.FaceUp || !proc.TributeFaceUpOnly) &&
                            (m.IsNamed(proc.TributeNamedCard) ||
                             string.Equals(m.Def?.name, proc.TributeNamedCard, System.StringComparison.OrdinalIgnoreCase)) &&
                            !(m.CannotBeTributed && m.FaceUp))
                .ToList() ?? new List<CardInstance>();

        /// <summary>
        /// ToonsRuntime.CanSummonByHandProcedure, after its hand / Main Phase / open-Chain checks: the named card the
        /// procedure needs is face-up on the field (either field, or yours with RequiresControllerNamedCard), Tributing is
        /// allowed, and you control a face-up monster with the Tribute's name (Tributing it frees its Monster Zone).
        /// </summary>
        public static bool CanSummonByNamedTribute(DuelEngine engine, DuelistState who, CardInstance card, EffectClause proc,
            out string reason)
        {
            if (!string.IsNullOrEmpty(proc.RequiresFaceUpName) &&
                !NamedFaceUp(engine, who, proc.RequiresFaceUpName, proc.RequiresControllerNamedCard))
            {
                reason = proc.RequiresControllerNamedCard
                    ? $"Requires a face-up \"{proc.RequiresFaceUpName}\" you control."
                    : $"Requires \"{proc.RequiresFaceUpName}\" on the field.";
                return false;
            }

            if (ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                reason = "Neither player can Tribute cards.";
                return false;
            }

            if (NamedTributes(who, proc).Count == 0)
            {
                reason = $"Tribute 1 face-up \"{proc.TributeNamedCard}\" on your side of the field.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// ToonsRuntime.TrySummonByHandProcedure (legality already checked): Tribute the named monster — the lone one, the
        /// AI's (lowest ATK), or the one a human with 2+ taps (CANCEL: no Summon) — then Special Summon the card from the
        /// hand (DuelEngine.SpecialSummonByOwnHandProcedure: the Tribute is the cost, the card is properly summoned).
        /// </summary>
        public static bool SummonByNamedTribute(DuelEngine engine, DuelistState who, CardInstance card, EffectClause proc)
        {
            var candidates = NamedTributes(who, proc);
            if (candidates.Count == 0) return false;
            if (candidates.Count == 1 || !engine.IsHumanControlled(who))
                return SummonWith(engine, who, card, proc, candidates.OrderBy(m => m.CurrentAtk).First());

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => NamedTributes(who, proc),
                Kind = EffectTargetKind.TributeMonsterYouControl,
                Prompt = $"{card.Name}: choose 1 face-up \"{proc.TributeNamedCard}\" to Tribute, or CANCEL.",
                AiChoose = list => list.OrderBy(m => m.CurrentAtk).FirstOrDefault(),
                DuringActivation = true,
                HoldsChain = false,
                Finish = picked => SummonWith(engine, who, card, proc, picked),
                Cancel = () => engine.Log($"{card.Name} is not Special Summoned.")
            });
            return true;
        }

        static bool SummonWith(DuelEngine engine, DuelistState who, CardInstance card, EffectClause proc,
            CardInstance tribute)
        {
            if (tribute == null || !who.Hand.Contains(card) || !NamedTributes(who, proc).Contains(tribute))
            {
                engine.Log($"Cannot Special Summon {card?.Name}: the Tribute is no longer available.");
                return false;
            }

            engine.Log($"{who.Name} Special Summons {card.Name} from the hand by Tributing {tribute.Name} " +
                       $"(\"{proc.RequiresFaceUpName}\" is on the field).");
            return engine.SpecialSummonByOwnHandProcedure(who, card, new List<CardInstance> { tribute });
        }
    }
}
