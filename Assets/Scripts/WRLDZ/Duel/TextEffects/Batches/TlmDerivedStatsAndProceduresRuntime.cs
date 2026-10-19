using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmDerivedStatsAndProcedures" (TLM). Clauses: <see cref="TlmDerivedStatsAndProceduresAtoms"/>.
    /// <list type="bullet">
    /// <item>GainSelfAtkPerCount count sources read by FieldSpellEffects.ApplySelfScale (<see cref="Count"/>):
    /// <see cref="FaceUpNamedYouControlAllInAttack"/> / <see cref="FaceUpNamedYouControlAllInDefense"/> (Batteryman AA),
    /// <see cref="YourFaceUpBanishedRace"/> (Lost Guardian), <see cref="NamedOrAltInYourGy"/> (King of the Skull
    /// Servants).</item>
    /// <item>King of the Skull Servants' revival (<see cref="ResolveBanishNamedReviveThis"/>): an optional pick of the cost
    /// (Cancel = not used), then the cost banish and the Special Summon of this card from the GY.</item>
    /// <item>Megarock Dragon: EngineHygieneRuntime's banish procedure reports the monsters it banished
    /// (<see cref="NoteProcedureBanished"/>); <see cref="ApplyBoard"/> sets the original ATK/DEF on each board refresh while
    /// the monster stays face-up on that field presence with its effects not negated, and drops it for good once that ends
    /// (Lua RESETS_STANDARD_DISABLE without RESET_TOFIELD).</item>
    /// </list>
    /// State is kept per engine.
    /// </summary>
    public static class TlmDerivedStatsAndProceduresRuntime
    {
        /// <summary>
        /// GainSelfAtkPerCount <see cref="EffectClause.CountSource"/> (Batteryman AA, ATK half): the face-up
        /// <see cref="EffectClause.NamedCard"/> monsters you control, when every one of them is in Attack Position (else 0).
        /// Face-down copies are not counted and do not break the condition (Lua aux.FaceupFilter(Card.IsCode)).
        /// </summary>
        public const string FaceUpNamedYouControlAllInAttack = "TlmFaceUpNamedAllInAttack";

        /// <summary>The DEF half of <see cref="FaceUpNamedYouControlAllInAttack"/>: every one in Defense Position.</summary>
        public const string FaceUpNamedYouControlAllInDefense = "TlmFaceUpNamedAllInDefense";

        /// <summary>
        /// GainSelfAtkPerCount <see cref="EffectClause.CountSource"/> (Lost Guardian): the face-up banished monsters of
        /// <see cref="EffectClause.RaceFilter"/> in your banishment (Lua aux.FaceupFilter(Card.IsRace), LOCATION_REMOVED).
        /// </summary>
        public const string YourFaceUpBanishedRace = "TlmYourFaceUpBanishedRace";

        /// <summary>
        /// GainSelfAtkPerCount <see cref="EffectClause.CountSource"/> (King of the Skull Servants): the cards in your GY
        /// named <see cref="EffectClause.NamedCard"/> or <see cref="EffectClause.AltNamedCard"/> (Lua Card.IsCode).
        /// </summary>
        public const string NamedOrAltInYourGy = "TlmNamedOrAltInYourGy";

        sealed class Grant
        {
            public CardInstance Card;
            public int Atk;
            public int Def;
            public int FieldEntry;
            public int FaceDown;
        }

        sealed class State
        {
            public readonly List<Grant> Grants = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── count sources ─────────────────────────────

        /// <summary>FieldSpellEffects.ApplySelfScale: the count for this batch's count sources (0 for any other).</summary>
        public static int Count(DuelistState who, EffectClause clause)
        {
            if (who == null || clause == null) return 0;
            switch (clause.CountSource)
            {
                case FaceUpNamedYouControlAllInAttack:
                case FaceUpNamedYouControlAllInDefense:
                {
                    var named = who.MonstersOnField()
                        .Where(m => m != null && m.FaceUp && m.IsNamed(clause.NamedCard))
                        .ToList();
                    if (named.Count == 0) return 0;
                    var want = clause.CountSource == FaceUpNamedYouControlAllInAttack
                        ? BattlePosition.Attack
                        : BattlePosition.Defense;
                    return named.All(m => m.Position == want) ? named.Count : 0;
                }
                case YourFaceUpBanishedRace:
                    return who.Banished?.Count(b => b?.Def != null && b.FaceUp && b.Def.IsMonster &&
                                                    ClassicEraTemplates.RaceMatches(b.Def.race, clause.RaceFilter)) ?? 0;
                case NamedOrAltInYourGy:
                    return who.Graveyard?.Count(g => g?.Def != null && IsEitherName(g, clause)) ?? 0;
                default:
                    return 0;
            }
        }

        static bool IsEitherName(CardInstance c, EffectClause clause) =>
            c != null && clause != null &&
            ((!string.IsNullOrEmpty(clause.NamedCard) && c.IsNamed(clause.NamedCard)) ||
             (!string.IsNullOrEmpty(clause.AltNamedCard) && c.IsNamed(clause.AltNamedCard)));

        // ───────────────────────────── King of the Skull Servants: the revival ─────────────────────────────

        /// <summary>
        /// The cost candidates of <paramref name="source"/>'s revival: another card in your GY with either name that can be
        /// banished (Necrovalley, Kycoo, no-banish locks), or, under Spirit Elimination, a face-up monster with either name
        /// you control (Lua costfilter with aux.SpElimFilter(c,true) on LOCATION_MZONE|LOCATION_GRAVE). A GY candidate needs a
        /// free Monster Zone for the Summon; a field one frees its own (Lua GetLocationCount or a Main Monster Zone card).
        /// </summary>
        public static List<CardInstance> ReviveCostCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || clause == null) return list;
            var freeZone = engine.FirstEmpty(who.MonsterZones) >= 0;
            if (SpiritEliminationRuntime.Affects(engine, who))
            {
                foreach (var m in SpiritEliminationRuntime.Substitutes(engine, who, source, true, x => IsEitherName(x, clause)))
                    list.Add(m);
                return list;
            }

            if (!freeZone) return list;
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, who) || RuleLocksRuntime.BanishFromGyLocked(engine, who))
                return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || g == source || !IsEitherName(g, clause)) continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g)) continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.SpecialSummonThisFromGyByBanishingNamedFromGy"/> (the optional
        /// battle-destroyed trigger, TextEffectRuntime.TryResolveSentToGy): while this card is in its GY and can be Special
        /// Summoned from there, its controller may banish 1 cost candidate (a tap; Cancel = the effect is not used; the AI
        /// always uses it), then this card is Special Summoned in the face-up position its controller chooses.
        /// </summary>
        public static void ResolveBanishNamedReviveThis(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null || engine.GameOver) return;
            if (who.Graveyard == null || !who.Graveyard.Contains(source)) return;
            var stamp = source.GyEntryStamp;
            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, source, source, SummonOrigin.Graveyard))
            {
                engine.Log($"{source.Name} cannot be Special Summoned from the GY — its effect is not used.");
                return;
            }

            if (ReviveCostCandidates(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{source.Name}: no other \"{clause.NamedCard}\" or \"{clause.AltNamedCard}\" to banish — " +
                           "its effect is not used.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => ReviveCostCandidates(engine, who, source, clause),
                Kind = EffectTargetKind.BanishFromYourGy,
                Prompt = $"you can banish 1 other \"{clause.NamedCard}\" or 1 \"{clause.AltNamedCard}\" from your GY to " +
                         $"Special Summon {source.Name} (Cancel = no).",
                AiChoose = list => list.FirstOrDefault(),
                AskWithOneCandidate = true,
                Cancel = () => engine.Log($"{source.Name}: {who.Name} does not use its effect."),
                Finish = pick => Revive(engine, who, source, clause, pick, stamp)
            });
        }

        static void Revive(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance cost, int stamp)
        {
            if (cost == null)
            {
                engine.Log($"{source.Name}: {who.Name} does not use its effect.");
                return;
            }

            if (!who.Graveyard.Contains(source) || source.GyEntryStamp != stamp ||
                !ReviveCostCandidates(engine, who, source, clause).Contains(cost))
            {
                engine.Log($"{source.Name}: the effect can no longer be used.");
                return;
            }

            // Cost: banish it (BanishEvents: one banish event).
            using (BanishEvents.Group(engine))
                engine.BanishCard(engine.ControllerOf(cost) ?? who, cost, by: who);
            if (!who.Banished.Contains(cost) && !(engine.OpponentOf(who)?.Banished.Contains(cost) ?? false))
            {
                engine.Log($"{source.Name}: {cost.Name} could not be banished — no Special Summon.");
                return;
            }

            engine.Log($"Cost: banish {cost.Name}.");
            // Effect (Lua s.operation): a free Monster Zone and this card still in the GY; Duel.SpecialSummon with
            // POS_FACEUP — its controller chooses face-up Attack or Defense Position (and the zone): a tap on this card
            // with the placement prompt. The Summon is no longer optional once the cost is paid: Cancel keeps the AI's
            // position.
            if (!CanStillRevive(engine, who, source, stamp)) return;
            var human = engine.IsHumanControlled(who);
            var aiPosition = AiRevivePosition(who, source, clause);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => who.Graveyard.Contains(source) && source.GyEntryStamp == stamp
                    ? new List<CardInstance> { source }
                    : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"Special Summon {source.Name} from your GY: choose face-up Attack or Defense Position, then a " +
                         "Monster Zone.",
                AiChoose = list => list.FirstOrDefault(),
                AskWithOneCandidate = true,
                AsksSpecialSummonPlacement = true,
                Finish = pick =>
                {
                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : aiPosition;
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    SummonThis(engine, who, source, stamp, pos, zone);
                }
            });
        }

        static bool CanStillRevive(DuelEngine engine, DuelistState who, CardInstance source, int stamp)
        {
            if (!who.Graveyard.Contains(source) || source.GyEntryStamp != stamp)
            {
                engine.Log($"{source.Name} is no longer in the GY — it is not Special Summoned.");
                return false;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source.Name}: no free Monster Zone — it is not Special Summoned.");
                return false;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, source, source, SummonOrigin.Graveyard))
            {
                engine.Log($"{source.Name} cannot be Special Summoned from the GY.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The AI's position for the revival: face-up Defense when the ATK it will have on the field (its
        /// SET_BASE_ATTACK count clause without this card, else the printed ATK) is 0 or below its DEF, else Attack.
        /// </summary>
        static BattlePosition AiRevivePosition(DuelistState who, CardInstance source, EffectClause clause)
        {
            var def = Math.Max(0, source.Def?.def ?? 0);
            var atk = Math.Max(0, source.Def?.atk ?? 0);
            var program = CompiledEffectCache.GetOrCompile(source.Def);
            var scale = program?.ClauseList?.FirstOrDefault(c =>
                c != null && c.Action == EffectActionKind.GainSelfAtkPerCount && c.SelfStatIsSet &&
                c.CountSource == NamedOrAltInYourGy);
            if (scale != null)
                atk = scale.Amount * who.Graveyard.Count(g => g != null && g != source && g.Def != null &&
                                                              IsEitherName(g, scale));
            return atk <= 0 || atk < def ? BattlePosition.Defense : BattlePosition.Attack;
        }

        static void SummonThis(DuelEngine engine, DuelistState who, CardInstance source, int stamp, BattlePosition pos,
            int zone)
        {
            if (!CanStillRevive(engine, who, source, stamp)) return;
            who.Graveyard.Remove(source);
            engine.NextSpecialSummonFromGraveyard = true;
            if (!engine.SpecialSummonToField(who, source, pos, true, Rules.SummonKind.SpecialSummon, zone))
            {
                source.ControlOwner = null;
                if (!who.Graveyard.Contains(source)) who.Graveyard.Add(source);
                engine.Log($"{source.Name}: the Special Summon failed.");
            }
            else
            {
                engine.Log($"{who.Name} Special Summons {source.Name} from the GY in face-up " +
                           $"{(source.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── Megarock Dragon ─────────────────────────────

        /// <summary>
        /// EngineHygieneRuntime.BanishAndSummon, after its banish procedure Special Summoned <paramref name="card"/> by
        /// banishing <paramref name="banished"/> monsters: a card with
        /// <see cref="EffectActionKind.OriginalAtkDefBecomeProcedureBanishCountTimes"/> gets original ATK and DEF of
        /// <paramref name="banished"/> x Amount for this field presence.
        /// </summary>
        public static void NoteProcedureBanished(DuelEngine engine, CardInstance card, int banished)
        {
            if (engine == null || card?.Def == null) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.OriginalAtkDefBecomeProcedureBanishCountTimes);
            if (clause == null) return;
            var st = StateOf(engine);
            st.Grants.RemoveAll(g => ReferenceEquals(g.Card, card));
            var n = Math.Max(0, banished);
            st.Grants.Add(new Grant
            {
                Card = card,
                Atk = Math.Max(0, clause.Amount * n),
                Def = Math.Max(0, clause.DefAmount * n),
                FieldEntry = card.FieldEntryStamp,
                FaceDown = card.FaceDownStamp
            });
            engine.Log($"{card.Name}: {n} {clause.RaceFilter} monster(s) banished — its original ATK/DEF become " +
                       $"{clause.Amount * n}/{clause.DefAmount * n}.");
            engine.NotifyPublic();
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after DerivedAtkMonstersRuntime.ApplyBoard): every live grant is written to
        /// <see cref="CardInstance.SelfSetAtk"/> / <see cref="CardInstance.SelfSetDef"/>; one whose monster left the field,
        /// was turned face-down or had its effects negated is dropped for good (its printed "?" = 0 from then on).
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            for (var i = st.Grants.Count - 1; i >= 0; i--)
            {
                var g = st.Grants[i];
                var c = g.Card;
                var controller = c != null ? engine.ControllerOf(c) : null;
                var alive = c != null && c.FaceUp && controller != null && controller.TryFindMonster(c, out _) &&
                            c.FieldEntryStamp == g.FieldEntry && c.FaceDownStamp == g.FaceDown && !c.IsNegated;
                if (!alive)
                {
                    st.Grants.RemoveAt(i);
                    continue;
                }

                c.SelfSetAtk = g.Atk;
                c.SelfSetDef = g.Def;
            }
        }

        /// <summary>The original ATK/DEF a live grant gives <paramref name="card"/> (tests), or null.</summary>
        public static (int Atk, int Def)? GrantOf(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return null;
            var g = st.Grants.FirstOrDefault(x => ReferenceEquals(x.Card, card));
            return g == null ? null : (g.Atk, g.Def);
        }
    }
}
