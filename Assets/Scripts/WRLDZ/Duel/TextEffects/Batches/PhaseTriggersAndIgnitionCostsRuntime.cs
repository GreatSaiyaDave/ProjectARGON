using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PhaseTriggersAndIgnitionCosts" runtime. Reads compiled programs only — no card-id branches. Every player
    /// choice goes through <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(DuelEngine, DuelistState, CardInstance, CardPickRequest)"/>
    /// (the AI and a lone candidate answer at once; a pick in the live Standby Phase holds it).
    /// <list type="bullet">
    /// <item>Amazoness Blowpiper (<see cref="ResolveStandbyAtkLoss"/>): in its controller's own Standby Phase, target 1 face-up
    /// monster the opponent controls (mandatory); it loses the ATK until the end of the turn.</item>
    /// <item>Helping Robo for Combat (<see cref="DrawThenBottom"/>): draw, then put 1 hand card on the bottom of the Deck.</item>
    /// <item>Mass Driver: a face-up Continuous Spell ignition (PerCountBurnsRuntime's face-up field ignition hooks:
    /// <see cref="IsTributeBurnIgnition"/>, <see cref="CanUseTributeBurn"/>, <see cref="ActivateTributeBurn"/>,
    /// <see cref="TryResolveTributeBurnLink"/>, <see cref="AiWantsTributeBurn"/>): Tribute 1 monster (cost), then the
    /// damage when its Chain Link resolves, while the card is still on the field.</item>
    /// <item>Senri Eye (<see cref="ResolveSenriEye"/>): in its controller's own Standby Phase, tap the card = pay the LP and
    /// look at the opponent's top card (it stays on top); CANCEL = not used.</item>
    /// <item>Magical Scientist (<see cref="ResolveMagicalScientist"/>): a Level-capped Fusion Monster from the Extra Deck; it
    /// cannot attack directly (<see cref="ForbidsDirectAttack"/>) and returns to the Extra Deck in this turn's End Phase
    /// (<see cref="OnEndPhase"/>).</item>
    /// <item>Roulette Barrel (<see cref="ResolveRouletteBarrel"/>): 2 dice (CoinDiceInterrupts redo cards apply), choose 1
    /// result, destroy 1 face-up monster of that Level (a pick, not a target).</item>
    /// <item>Physical Double (<see cref="ResolveCopyToken"/>): a Token copying the target's current Level, Type, Attribute,
    /// ATK and DEF, face-up Attack or Defense, destroyed in this turn's End Phase.</item>
    /// </list>
    /// </summary>
    public static class PhaseTriggersAndIgnitionCostsRuntime
    {
        /// <summary>ActivationCondition (Magical Scientist): a free Monster Zone and a Fusion Monster of Level Amount or
        /// lower in the Extra Deck that can be Special Summoned (Lua s.target chk).</summary>
        public const string CanSummonLevelCappedFusionFromExtra = "PhaseTriggers.CanSummonLevelCappedFusionFromExtra";

        /// <summary>ActivationCondition (Physical Double): a free Monster Zone and the player can Special Summon the Token.</summary>
        public const string CanSummonCopyToken = "PhaseTriggers.CanSummonCopyToken";

        /// <summary>The AI pays for Senri Eye only while it keeps at least this many LP afterwards.</summary>
        public const int AiSenriEyeFloorLp = 4000;

        // ───────────────────────────── shared ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet default chain: this batch's conditions. False for any other condition.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            switch (c.ActivationCondition)
            {
                case CanSummonLevelCappedFusionFromExtra:
                    ok = engine.FirstEmpty(who.MonsterZones) >= 0 && FusionCandidates(engine, who, card, c).Count > 0;
                    reason = $"Needs a free Monster Zone and a Level {Math.Max(1, c.Amount)} or lower Fusion Monster in " +
                             "your Extra Deck that can be Special Summoned.";
                    return true;
                case CanSummonCopyToken:
                    ok = engine.FirstEmpty(who.MonsterZones) >= 0 &&
                         !RuleLocksRuntime.AnySpecialSummonLocked(engine, who, c.TokenName, out _);
                    reason = $"Needs a free Monster Zone and the right to Special Summon a \"{c.TokenName}\".";
                    return true;
            }

            return false;
        }

        /// <summary>A declaration token (CallsDiceAndSetTriggersRuntime.OptionTokenKind) standing for one option.</summary>
        static CardInstance OptionToken(string name, int value) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = name,
                type = CallsDiceAndSetTriggersRuntime.OptionTokenKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = value,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        static bool OnField(DuelEngine engine, CardInstance c) =>
            c != null && engine?.ControllerOf(c) is { } side && side.TryFindMonster(c, out _);

        // ───────────────────────────── Amazoness Blowpiper ─────────────────────────────

        static List<CardInstance> OppFaceUpTargets(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var opp = engine.OpponentOf(who);
            return TextEffectRuntime.CollectChoiceTargets(engine, who, clause, source)
                .Where(t => t != null && t.FaceUp && ReferenceEquals(engine.ControllerOf(t), opp))
                // EquipLocksAndLeaveField batch: Metalsilver Armor — Blowpiper targets (Lua SelectTarget), and the clause
                // carries no RequiresTargetChoice, so CollectTargets' shield filter does not run for it.
                .Where(t => !EquipLocksAndLeaveFieldRuntime.ShieldedFromSource(engine, t, source))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// <see cref="EffectActionKind.TargetOppFaceUpLosesAtkAtYourStandby"/> (TextEffectRuntime.FirePhaseTriggersFor, the
        /// controller's own Standby Phase; the card is a face-up, not negated monster there). Mandatory: the AI, or a lone
        /// target, is chosen at once; a human picks among 2+ (Cancel makes the AI's choice) while the Standby Phase waits.
        /// No face-up monster the opponent controls: nothing happens.
        /// </summary>
        public static void ResolveStandbyAtkLoss(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance chosenTarget)
        {
            if (engine == null || who == null || clause == null) return;
            if (chosenTarget != null)
            {
                ApplyAtkLoss(engine, who, source, clause, chosenTarget);
                return;
            }

            if (engine.Phase != DuelPhase.Standby || !ReferenceEquals(engine.TurnPlayer, who)) return;
            if (source == null || !source.FaceUp || !who.TryFindMonster(source, out _)) return;
            if (OppFaceUpTargets(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{source.Name}: your opponent controls no face-up monster — no effect.");
                return;
            }

            var amount = Math.Max(0, clause.Amount);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => OppFaceUpTargets(engine, who, source, clause),
                Kind = EffectTargetKind.OppFaceUpMonster,
                Prompt = $"{source.Name}: target 1 face-up monster your opponent controls — it loses {amount} ATK until " +
                         "the end of this turn.",
                AiChoose = list => list.OrderByDescending(t => t.CurrentAtk).ThenBy(t => t.Name).FirstOrDefault(),
                Finish = t => ApplyAtkLoss(engine, who, source, clause, t)
            });
        }

        static void ApplyAtkLoss(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            if (target == null) return;
            var opp = engine.OpponentOf(who);
            if (!target.FaceUp || opp == null || !opp.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name}: the target is no longer a face-up monster your opponent controls — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            // ChainLinkAnswers batch: Chaos Command Magician negates a monster effect that targets only it.
            if (ChainLinkAnswersRuntime.NegatesRegisteredMonsterTarget(engine, source, target)) return;

            var amount = Math.Max(0, clause.Amount);
            target.UntilEndOfTurnAtk -= amount;
            engine.Log($"{source?.Name}: {target.Name} loses {amount} ATK until the end of this turn " +
                       $"(now {target.CurrentAtk}/{target.CurrentDef}).");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Helping Robo for Combat ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.DrawThenPlaceHandCardOnDeckBottom"/>: draw; if a card was drawn, its controller places 1
        /// card from the hand on the bottom of the Deck (a human picks among 2+; the pick is mandatory, Cancel makes the AI's
        /// choice: the least useful card).
        /// </summary>
        public static void DrawThenBottom(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null) return;
            var n = Math.Max(1, clause?.Amount ?? 1);
            var deckBefore = who.Deck.Count;
            engine.Draw(who, n);
            if (engine.GameOver) return;
            if (who.Deck.Count >= deckBefore)
            {
                engine.Log($"{source?.Name}: no card was drawn — nothing is returned to the Deck.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => who.Hand.Where(h => h != null).ToList(),
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"{source?.Name}: choose 1 card in your hand to place on the bottom of your Deck.",
                AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, null) ?? list.FirstOrDefault(),
                Finish = c => PlaceOnBottom(engine, who, source, c)
            });
        }

        static void PlaceOnBottom(DuelEngine engine, DuelistState who, CardInstance source, CardInstance card)
        {
            if (card == null || !who.Hand.Contains(card))
            {
                engine.Log($"{source?.Name}: no card in the hand to return.");
                return;
            }

            who.Hand.Remove(card);
            who.Deck.Add(card.CardId);
            engine.Log($"{source?.Name}: {who.Name} places a card from the hand on the bottom of the Deck.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Mass Driver ─────────────────────────────

        sealed class BurnHold
        {
            public DuelistState Who;
            public CardInstance Card;
            public EffectClause Clause;
            public int EntryStamp;
        }

        static readonly ConditionalWeakTable<ChainLink, BurnHold> s_burnLinks = new();

        /// <summary>A once-per-turn ignition's last use (turn, field stay); Mass Driver itself has no count limit.</summary>
        sealed class BurnUse
        {
            public int Turn;
            public int EntryStamp;
        }

        static readonly ConditionalWeakTable<CardInstance, BurnUse> s_burnUses = new();

        static EffectClause TributeBurnClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.FaceUpFieldIgnition)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.InflictDamageToOpponent &&
                                     c.RequiresTributeCount > 0 && !c.RequiresSendThisToGy);

        /// <summary>The card's face-up ignition Tributes a monster to inflict damage (Mass Driver).</summary>
        public static bool IsTributeBurnIgnition(CompiledCardProgram prog) => TributeBurnClause(prog) != null;

        static List<CardInstance> TributeCandidates(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c)
        {
            if (engine == null || who == null || c == null || ContinuousProtections.NeitherPlayerCanTribute(engine))
                return new List<CardInstance>();
            return TributeSummonsRuntime.RestrictToForced(who, TextEffectRuntime.CollectTributeCost(who, card, c, engine));
        }

        /// <summary>
        /// PerCountBurnsRuntime.CanActivateFaceUpFieldIgnition, after its open-state / own Main Phase / negation checks:
        /// a monster you control can be Tributed (Lua CheckReleaseGroupCost). No count limit.
        /// </summary>
        public static bool CanUseTributeBurn(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            var c = TributeBurnClause(prog);
            if (c == null || TributeCandidates(engine, who, card, c).Count == 0)
            {
                reason = "You have no monster you can Tribute.";
                return false;
            }

            if (c.OncePerTurn && card != null && s_burnUses.TryGetValue(card, out var used) &&
                used.Turn == engine.TurnNumber && used.EntryStamp == card.FieldEntryStamp)
            {
                reason = "This card's effect was already used this turn.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// PerCountBurnsRuntime.TryFaceUpFieldIgnition, on its seated Chain Link: pay the Tribute (a human picks among 2+;
        /// Cancel takes the activation back), then the opponent may respond; the damage is dealt when the link resolves.
        /// </summary>
        public static void ActivateTributeBurn(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, ChainLink link)
        {
            var c = TributeBurnClause(prog);
            if (engine == null || who == null || card == null || c == null || link == null) return;
            engine.Log($"Activate: {card.Name} (effect) — Chain Link {link.LinkNumber}.");
            List<CardInstance> Candidates() => TributeCandidates(engine, who, card, c);
            if (!engine.IsHumanControlled(who))
            {
                PayTributeThenOpen(engine, who, card, c, link, AiTribute(Candidates()));
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.TributeMonsterYouControl,
                Prompt = $"{card.Name}: Tribute 1 monster you control (cost), or CANCEL to not activate it.",
                AiChoose = AiTribute,
                DuringActivation = true,
                Finish = m => PayTributeThenOpen(engine, who, card, c, link, m),
                Cancel = () => Undo(engine, card, "nothing is Tributed")
            });
        }

        static CardInstance AiTribute(List<CardInstance> list) =>
            list?.OrderByDescending(m => m.IsToken)
                .ThenBy(m => m.FaceUp ? m.CurrentAtk : (m.Def?.atk ?? 0))
                .FirstOrDefault();

        static void Undo(DuelEngine engine, CardInstance card, string why)
        {
            engine.DropUnresolvedLink(card);
            engine.Log($"{card.Name}'s effect is not activated — {why}.");
            engine.NotifyPublic();
        }

        static void PayTributeThenOpen(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            ChainLink link, CardInstance tribute)
        {
            if (tribute == null || !TributeCandidates(engine, who, card, c).Contains(tribute))
            {
                Undo(engine, card, "no monster was Tributed");
                return;
            }

            var owner = engine.ControllerOf(tribute) ?? who;
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                ReleaseEvents.Record(engine, tribute, who); // EngineHygiene: EVENT_RELEASE
                engine.SendCardToGrave(owner, tribute);
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }

            engine.Log($"Cost: Tribute {tribute.Name}.");
            s_burnUses.Remove(card);
            s_burnUses.Add(card, new BurnUse { Turn = engine.TurnNumber, EntryStamp = card.FieldEntryStamp });
            if (engine.GameOver) return;
            s_burnLinks.Remove(link);
            s_burnLinks.Add(link, new BurnHold { Who = who, Card = card, Clause = c, EntryStamp = card.FieldEntryStamp });
            // The opponent may respond (Spell Speed 2+); with nothing to chain, the link resolves now.
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

        /// <summary>
        /// PerCountBurnsRuntime.TryResolveFaceUpFieldIgnitionLink: false for any other link. The damage needs this card still
        /// face-up in its zone (Lua IsRelateToEffect) and its effect not negated.
        /// </summary>
        public static bool TryResolveTributeBurnLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || !s_burnLinks.TryGetValue(link, out var hold)) return false;
            s_burnLinks.Remove(link);
            var card = hold.Card;
            var name = card?.Name ?? "effect";
            var onField = card != null && hold.Who != null && card.FaceUp && hold.Who.TryFindSpellTrap(card, out _) &&
                          card.FieldEntryStamp == hold.EntryStamp;
            var negatedBy = ContinuousNegationRuntime.NegatedBy(engine, card);
            if (negatedBy != null || (onField && card.IsNegated))
            {
                engine.Log($"{name}: its effect is negated{(negatedBy != null ? $" by {negatedBy.Name}" : "")}.");
                engine.NotifyPublic();
                return true;
            }

            if (!onField)
            {
                engine.Log($"{name} is no longer on the field — no damage.");
                engine.NotifyPublic();
                return true;
            }

            var amount = Math.Max(0, hold.Clause.Amount);
            engine.ApplyEffectDamage(engine.OpponentOf(hold.Who), amount, name, card);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>PerCountBurnsRuntime.AiWantsFaceUpFieldIgnition: the AI uses Mass Driver for lethal damage, or to Tribute
        /// a Token.</summary>
        public static bool AiWantsTributeBurn(DuelEngine engine, DuelistState ai, CardInstance card,
            CompiledCardProgram prog)
        {
            var c = TributeBurnClause(prog);
            if (c == null || !CanUseTributeBurn(engine, ai, card, prog, out _)) return false;
            var opp = engine.OpponentOf(ai);
            return (opp != null && c.Amount >= opp.LifePoints) ||
                   TributeCandidates(engine, ai, card, c).Exists(m => m.IsToken);
        }

        // ───────────────────────────── Senri Eye ─────────────────────────────

        static bool SenriEyeUsable(DuelEngine engine, DuelistState who, CardInstance card, int amount)
        {
            var opp = engine.OpponentOf(who);
            return card != null && card.FaceUp && who.TryFindSpellTrap(card, out _) && !card.IsNegated &&
                   opp?.Deck != null && opp.Deck.Count > 0 && who.LifePoints >= amount &&
                   engine.Phase == DuelPhase.Standby && ReferenceEquals(engine.TurnPlayer, who);
        }

        /// <summary>
        /// <see cref="EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby"/> (FirePhaseTriggersFor, the controller's
        /// own Standby Phase; the card is face-up and not negated there). Optional: the AI pays while it keeps
        /// <see cref="AiSenriEyeFloorLp"/> LP; a human taps the card to pay (CANCEL = not used) while the Standby Phase waits.
        /// Condition: the opponent's Deck is not empty and the LP can be paid.
        /// </summary>
        public static void ResolveSenriEye(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var amount = Math.Max(0, clause.Amount);
            if (!SenriEyeUsable(engine, who, source, amount)) return;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => SenriEyeUsable(engine, who, source, amount)
                    ? new List<CardInstance> { source }
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.AnyCardOnField,
                Prompt = $"{source.Name}: tap it to pay {amount} LP and look at the top card of your opponent's Deck, or " +
                         "CANCEL to not use it.",
                AskWithOneCandidate = true,
                AiChoose = list => who.LifePoints - amount >= AiSenriEyeFloorLp ? list.FirstOrDefault() : null,
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        engine.Log($"{source.Name}: {who.Name} does not use its effect.");
                        return;
                    }

                    LookAtTop(engine, who, source, amount);
                },
                Cancel = () => engine.Log($"{source.Name}: {who.Name} does not use its effect.")
            });
        }

        static void LookAtTop(DuelEngine engine, DuelistState who, CardInstance source, int amount)
        {
            if (!SenriEyeUsable(engine, who, source, amount))
            {
                engine.Log($"{source.Name}: it can no longer be used — nothing is paid.");
                return;
            }

            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP for {source.Name} → {who.Name} at {who.LifePoints - amount} LP.", isCost: true);
            var opp = engine.OpponentOf(who);
            if (engine.GameOver || opp?.Deck == null || opp.Deck.Count == 0) return;
            var top = engine.CreateCardInstance(opp.Deck[0]);
            FlipDownAndPeekRuntime.LogLook(engine, who, source, $"the top card of {opp.Name}'s Deck", new[] { top });
            engine.Log($"{source.Name}: the card stays on top of {opp.Name}'s Deck.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Magical Scientist ─────────────────────────────

        sealed class ExtraDeckReturnMark
        {
            public int Turn;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        static readonly ConditionalWeakTable<CardInstance, ExtraDeckReturnMark> s_returnMarks = new();

        static bool IsFusion(CardDef def) =>
            def != null && def.IsMonster && def.type != null &&
            def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>One instance per distinct Fusion Monster of Level Amount or lower in the Extra Deck that can be Special
        /// Summoned (Lua s.filter).</summary>
        static List<CardInstance> FusionCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.ExtraDeck == null || c == null) return list;
            var cap = Math.Max(1, c.Amount);
            foreach (var id in who.ExtraDeck.Distinct())
            {
                if (!engine.Database.TryGet(id, out var def) || !IsFusion(def) || def.level > cap) continue;
                var inst = engine.CreateCardInstance(id);
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.ExtraDeck))
                    continue;
                list.Add(inst);
            }

            return list;
        }

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase"/>: the controller chooses 1
        /// Fusion Monster of Level Amount or lower (a human through the Special Summon picker, which asks face-up Attack or
        /// Defense and a zone; the AI takes the highest ATK) and Special Summons it (not a Fusion Summon).
        /// </summary>
        public static void ResolveMagicalScientist(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            if (FusionCandidates(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{source?.Name}: no Fusion Monster in the Extra Deck can be Special Summoned.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => FusionCandidates(engine, who, source, clause),
                Kind = EffectTargetKind.MonsterInYourDeckToSummon,
                Prompt = $"{source?.Name}: Special Summon 1 Level {Math.Max(1, clause.Amount)} or lower Fusion Monster from " +
                         "your Extra Deck in face-up Attack or Defense Position (it cannot attack directly and returns to " +
                         "the Extra Deck at the end of the turn).",
                // A human chooses the position even for a lone Fusion Monster (the picker asks it).
                AskWithOneCandidate = engine.IsHumanControlled(who),
                AiChoose = list => list.OrderByDescending(f => f.Def?.atk ?? 0).ThenBy(f => f.Name).FirstOrDefault(),
                Finish = f => SummonFusion(engine, who, source, clause, f)
            });
        }

        static void SummonFusion(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance fusion)
        {
            if (fusion == null) return;
            if (!who.ExtraDeck.Contains(fusion.CardId) || !IsFusion(fusion.Def) ||
                fusion.Def.level > Math.Max(1, clause.Amount) ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, fusion, source, SummonOrigin.ExtraDeck) ||
                engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: {fusion.Name} cannot be Special Summoned now.");
                return;
            }

            // Lua POS_FACEUP: a human's position comes from the picker (DuelEngine's pending placement); otherwise Attack,
            // or Defense when the monster's DEF is higher.
            var pos = (fusion.Def?.def ?? 0) > (fusion.Def?.atk ?? 0) ? BattlePosition.Defense : BattlePosition.Attack;
            who.ExtraDeck.Remove(fusion.CardId);
            if (!engine.SpecialSummonToField(who, fusion, pos, faceUp: true, SummonKind.SpecialSummon, -1))
            {
                who.ExtraDeck.Add(fusion.CardId);
                engine.Log($"{source?.Name}: the Special Summon of {fusion.Name} failed.");
                return;
            }

            s_returnMarks.Remove(fusion);
            s_returnMarks.Add(fusion, new ExtraDeckReturnMark
            {
                Turn = engine.TurnNumber,
                EntryStamp = fusion.FieldEntryStamp,
                FaceDownStamp = fusion.FaceDownStamp
            });
            engine.Log($"{source?.Name}: {who.Name} Special Summons {fusion.Name} from the Extra Deck; it cannot attack " +
                       "directly and returns to the Extra Deck at the end of the turn.");
            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        /// <summary>The mark is still on this copy: same stay on the field, never turned face-down (Lua RESETS_STANDARD).</summary>
        static bool MarkLive(DuelEngine engine, CardInstance m, out ExtraDeckReturnMark mark)
        {
            mark = null;
            return m != null && s_returnMarks.TryGetValue(m, out mark) && OnField(engine, m) && m.FaceUp &&
                   m.FieldEntryStamp == mark.EntryStamp && m.FaceDownStamp == mark.FaceDownStamp;
        }

        /// <summary>
        /// SelfStatMonstersRuntime.ForbidsDirectAttack: a Fusion Monster Magical Scientist Special Summoned cannot attack
        /// directly (EFFECT_CANNOT_DIRECT_ATTACK, EFFECT_FLAG_IGNORE_IMMUNE, granted by another card: the monster's own
        /// negation does not remove it).
        /// </summary>
        public static bool ForbidsDirectAttack(DuelEngine engine, CardInstance attacker) =>
            MarkLive(engine, attacker, out _);

        /// <summary>
        /// DuelEngine.FinishEndTurnAfterTriggers (next to the temporary Special Summons' End Phase destruction): a Fusion
        /// Monster Magical Scientist Special Summoned this turn returns to the Extra Deck (Lua EVENT_PHASE+PHASE_END,
        /// EFFECT_TYPE_CONTINUOUS, RESETS_STANDARD_PHASE_END).
        /// </summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (!MarkLive(engine, m, out var mark) || mark.Turn != engine.TurnNumber) continue;
                    s_returnMarks.Remove(m);
                    engine.Log($"{m.Name} returns to the Extra Deck at the end of the turn.");
                    engine.ReturnCardToDeck(m);
                    if (engine.GameOver) return;
                }
            }
        }

        // ───────────────────────────── Roulette Barrel ─────────────────────────────

        static List<CardInstance> LevelVictims(DuelEngine engine, int level) =>
            new[] { engine.Player, engine.Opponent }
                .Where(s => s != null)
                .SelectMany(s => s.MonstersOnField())
                .Where(m => m != null && m.FaceUp && m.Level == level)
                .ToList();

        /// <summary>The AI's destroy pick: the opponent's strongest, else its own weakest.</summary>
        static CardInstance AiVictim(DuelEngine engine, DuelistState who, List<CardInstance> list)
        {
            var opp = engine.OpponentOf(who);
            return list.Where(m => ReferenceEquals(engine.ControllerOf(m), opp))
                       .OrderByDescending(m => m.CurrentAtk).FirstOrDefault()
                   ?? list.OrderBy(m => m.CurrentAtk).FirstOrDefault();
        }

        /// <summary>How good destroying a monster of <paramref name="level"/> is for <paramref name="who"/> (the AI's result
        /// choice): an opponent's monster scores its ATK, no victim scores 0, only its own monsters score below 0.</summary>
        static int AiLevelScore(DuelEngine engine, DuelistState who, int level)
        {
            var list = LevelVictims(engine, level);
            if (list.Count == 0) return 0;
            var pick = AiVictim(engine, who, list);
            return ReferenceEquals(engine.ControllerOf(pick), engine.OpponentOf(who))
                ? 1 + Math.Max(0, pick.CurrentAtk)
                : -1 - Math.Max(0, pick.CurrentAtk);
        }

        /// <summary>
        /// <see cref="EffectActionKind.RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel"/>: roll 2 dice (a Dice Re-Roll
        /// redo may change them), choose 1 result when they differ (a human taps one of two option cards; mandatory), then
        /// destroy 1 face-up monster on the field with that Level (a human picks among 2+; mandatory).
        /// </summary>
        public static void ResolveRouletteBarrel(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null) return;
            CoinDiceInterruptsRuntime.RollDice(engine, who, source, 2,
                dice => dice.Any(d => AiLevelScore(engine, who, d) > 0),
                dice => AfterRoll(engine, who, source, dice[0], dice[1]));
        }

        static void AfterRoll(DuelEngine engine, DuelistState who, CardInstance source, int d1, int d2)
        {
            engine.Log($"{source?.Name}: {who.Name} rolls {d1} and {d2}.");
            if (d1 == d2)
            {
                DestroyWithLevel(engine, who, source, d1);
                return;
            }

            var lo = Math.Min(d1, d2);
            var hi = Math.Max(d1, d2);
            var options = new List<CardInstance> { OptionToken($"Level {lo}", lo), OptionToken($"Level {hi}", hi) };
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => options,
                Kind = EffectTargetKind.None,
                Prompt = $"{source?.Name}: choose 1 result ({lo} or {hi}); then destroy 1 face-up monster on the field " +
                         "with that Level.",
                AiChoose = list => list.OrderByDescending(o => AiLevelScore(engine, who, o.Def.level)).FirstOrDefault(),
                Finish = chosen => DestroyWithLevel(engine, who, source,
                    chosen?.Def != null && (chosen.Def.level == lo || chosen.Def.level == hi) ? chosen.Def.level : hi)
            });
        }

        static void DestroyWithLevel(DuelEngine engine, DuelistState who, CardInstance source, int level)
        {
            engine.Log($"{source?.Name}: the chosen result is {level}.");
            if (LevelVictims(engine, level).Count == 0)
            {
                engine.Log($"{source?.Name}: no face-up Level {level} monster on the field — nothing is destroyed.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => LevelVictims(engine, level),
                Kind = EffectTargetKind.FaceUpMonsterOnField,
                Prompt = $"{source?.Name}: destroy 1 face-up Level {level} monster on the field.",
                AiChoose = list => AiVictim(engine, who, list),
                Finish = m =>
                {
                    if (m == null || !m.FaceUp || m.Level != level || !OnField(engine, m))
                    {
                        engine.Log($"{source?.Name}: nothing is destroyed.");
                        return;
                    }

                    TextEffectRuntime.DestroyCard(engine, m, source, byPlayer: who);
                    engine.NotifyPublic();
                }
            });
        }

        // ───────────────────────────── Physical Double ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.SpecialSummonTokenCopyingTargetDestroyAtEndPhase"/>: the target must still be a
        /// face-up monster the opponent controls (Lua IsRelateToEffect / IsFacedown); a free zone and the right to Special
        /// Summon it are checked again. The Token takes the target's current Level, Type, Attribute, ATK and DEF; a human
        /// chooses face-up Attack or Defense Position (the AI: Attack unless DEF is higher). It is destroyed in this turn's
        /// End Phase (<see cref="CardInstance.TempDestroyOnEndOfTurn"/>).
        /// </summary>
        public static void ResolveCopyToken(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            if (engine == null || who == null || clause == null) return;
            var opp = engine.OpponentOf(who);
            if (target == null || !target.FaceUp || opp == null || !opp.TryFindMonster(target, out _) || target.Level <= 0)
            {
                engine.Log($"{source?.Name}: the target is no longer a face-up monster your opponent controls — no effect.");
                return;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0 ||
                RuleLocksRuntime.AnySpecialSummonLocked(engine, who, clause.TokenName, out _))
            {
                engine.Log($"{source?.Name}: the Token cannot be Special Summoned — no effect.");
                return;
            }

            var level = target.Level;
            var race = target.Race;
            var attribute = target.Attribute;
            var atk = Math.Max(0, target.CurrentAtk);
            var def = Math.Max(0, target.CurrentDef);
            var aiPos = def > atk ? BattlePosition.Defense : BattlePosition.Attack;
            var name = string.IsNullOrEmpty(clause.TokenName) ? "Token" : clause.TokenName;
            void Summon(BattlePosition pos)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0) return;
                var tok = engine.CreateToken(name, race, attribute, level, atk, def);
                tok.Position = pos;
                if (!engine.SpecialSummonToField(who, tok, pos, true))
                {
                    engine.Log($"{source?.Name}: the Token could not be Special Summoned.");
                    return;
                }

                tok.TempDestroyOnEndOfTurn = engine.TurnNumber;
                engine.Log($"{source?.Name}: {name} Special Summoned — Level {level} {race} {attribute} " +
                           $"{tok.CurrentAtk}/{tok.CurrentDef}, destroyed during the End Phase.");
                engine.NotifyPublic();
            }

            if (!engine.IsHumanControlled(who))
            {
                Summon(aiPos);
                return;
            }

            var positions = new List<CardInstance>
            {
                OptionToken("Face-up Attack Position", 0),
                OptionToken("Face-up Defense Position", 1)
            };
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => positions,
                Kind = EffectTargetKind.None,
                Prompt = $"{source?.Name}: Special Summon {name} ({atk}/{def}) — choose its position.",
                AiChoose = list => list.ElementAtOrDefault(aiPos == BattlePosition.Defense ? 1 : 0),
                Finish = chosen => Summon(chosen?.Def?.level == 1 ? BattlePosition.Defense : BattlePosition.Attack)
            });
        }
    }
}
