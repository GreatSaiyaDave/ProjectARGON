using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TokensDeclarationsAndPicks" (atoms: <see cref="TokensDeclarationsAndPicksAtoms"/>):
    /// <list type="bullet">
    /// <item><b>Stray Lambs / Scapegoat</b>: a per-player record of this turn's Normal, Flip and Special Summons
    /// (<see cref="NoteSummon"/>, from the three DuelEngine summon funnels; ygopro ACTIVITY_SUMMON / FLIPSUMMON /
    /// SPSUMMON) gates the activation (<see cref="TokenSummonLegal"/>); the activation's oath
    /// (<see cref="ApplySummonOath"/>, EFFECT_FLAG_OATH) forbids its activator's Normal, Flip and Special Summons for the
    /// rest of the turn, except the Special Summon of that card's own resolution (<see cref="SummonTokens"/>), read by
    /// RuleLocksRuntime (<see cref="SummonOathLocked"/>).</item>
    /// <item><b>Multiplication of Ants</b> (and the two above): all Tokens or none (<see cref="EffectClause.TokensNeedAllZones"/>).</item>
    /// <item><b>DNA Transplant</b>: the Attribute declared at activation (<see cref="RecordDeclaredAttribute"/>) is every
    /// face-up monster's Attribute while the card applies (<see cref="AttributeOnField"/>, read by
    /// EquipFamilyRuntime.CurrentAttribute).</item>
    /// <item><b>Jade Insect Whistle</b>: the opponent's own Deck pick (<see cref="ResolveOpponentPlacesOnTop"/>).</item>
    /// <item><b>A Hero Emerges</b>: the random hand card (<see cref="ResolveRandomHandSummon"/>).</item>
    /// <item><b>Wild Nature's Release</b>: <see cref="ApplyDefToAtkThenDestroy"/>, target filter <see cref="FilterTargets"/>.</item>
    /// <item><b>Balloon Lizard</b>: <see cref="PlaceCounter"/> and <see cref="InflictCounterBurn"/>.</item>
    /// </list>
    /// </summary>
    public static class TokensDeclarationsAndPicksRuntime
    {
        /// <summary>Token activation check: N free Monster Zones (all-or-nothing Tokens) and, with the summon oath, no
        /// Normal / Flip / Special Summon by the activator this turn.</summary>
        public const string TokenSummonLegal = "TdpTokenSummonLegal";

        /// <summary>A Hero Emerges (s.target): a free Monster Zone and a hand monster that can be Special Summoned.</summary>
        public const string HandMonsterCanBeSpecialSummoned = "TdpHandMonsterCanBeSpecialSummoned";

        /// <summary>The Attributes a player may declare (ygopro ATTRIBUTE_ALL), A–Z.</summary>
        public static readonly string[] Attributes = { "DARK", "DIVINE", "EARTH", "FIRE", "LIGHT", "WATER", "WIND" };

        sealed class Oath
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Turn;
        }

        sealed class Declaration
        {
            public string Attribute;
            public long Stamp;
        }

        sealed class EngineState
        {
            /// <summary>The last turn each player Normal, Flip or Special Summoned.</summary>
            public readonly Dictionary<DuelistState, int> SummonedTurn = new();
            public readonly List<Oath> Oaths = new();
            /// <summary>The oath source whose own Special Summon is being performed (exempt from its own oath).</summary>
            public CardInstance SummoningFor;
            /// <summary>The summoning player when it is not the receiving field's controller (Tokens to the opponent's field).</summary>
            public DuelistState SummonerOverride;
            /// <summary>Cards with an Attribute-changing continuous clause that declared an Attribute in this duel.</summary>
            public readonly List<CardInstance> AttributeChangers = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();
        static readonly ConditionalWeakTable<CardInstance, Declaration> s_declared = new();
        static readonly List<WeakReference<DuelEngine>> s_attributeEngines = new();

        sealed class CounterStamp
        {
            public int Entry;
            public int FaceUp;
        }

        /// <summary>Balloon Lizard: the face-up field presence (FieldEntryStamp, FaceUpStamp) its counters were put on (a
        /// card that leaves the field or is turned face-down loses its counters).</summary>
        static readonly ConditionalWeakTable<CardInstance, CounterStamp> s_counterPresence = new();

        static EngineState StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new EngineState());

        // ───────────────────────────── summon activity + oath (Stray Lambs, Scapegoat) ─────────────────────────────

        /// <summary>
        /// DuelEngine: <paramref name="who"/> conducted a Normal (Tribute) Summon, a Flip Summon or a Special Summon (Sets do
        /// not count). A Token sent to the opponent's field counts for its summoner (<see cref="SummonTokens"/>).
        /// </summary>
        public static void NoteSummon(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            var st = StateOf(engine);
            st.SummonedTurn[st.SummonerOverride ?? who] = engine.TurnNumber;
        }

        /// <summary><paramref name="who"/> Normal, Flip or Special Summoned this turn.</summary>
        public static bool SummonedThisTurn(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && s_state.TryGetValue(engine, out var st) &&
            st.SummonedTurn.TryGetValue(who, out var turn) && turn == engine.TurnNumber;

        /// <summary>
        /// The activation's oath (Lua s.cost, EFFECT_FLAG_OATH, reset at the End Phase): <paramref name="who"/> cannot Normal,
        /// Flip or Special Summon for the rest of this turn, except <paramref name="source"/>'s own Special Summon.
        /// </summary>
        public static void ApplySummonOath(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            var st = StateOf(engine);
            st.Oaths.RemoveAll(o => o.Turn != engine.TurnNumber);
            if (st.Oaths.Exists(o => ReferenceEquals(o.Who, who) && ReferenceEquals(o.Source, source))) return;
            st.Oaths.Add(new Oath { Who = who, Source = source, Turn = engine.TurnNumber });
            engine.Log($"{source.Name}: {who.Name} cannot Summon other monsters this turn (Setting is still allowed).");
        }

        /// <summary>
        /// RuleLocksRuntime: an oath binds <paramref name="who"/> this turn. <paramref name="special"/>: a Special Summon,
        /// which the oath's own source may still perform while it resolves (Lua s.sumlimit: e:GetLabelObject() ~= se);
        /// <paramref name="exceptSource"/>: an oath of that card is ignored (its own activation's legality re-check).
        /// </summary>
        public static bool SummonOathLocked(DuelEngine engine, DuelistState who, bool special, CardInstance exceptSource,
            out string reason)
        {
            reason = null;
            if (engine == null || who == null || !s_state.TryGetValue(engine, out var st) || st.Oaths.Count == 0)
                return false;
            foreach (var o in st.Oaths)
            {
                if (o.Turn != engine.TurnNumber || !ReferenceEquals(o.Who, who)) continue;
                if (exceptSource != null && ReferenceEquals(o.Source, exceptSource)) continue;
                if (special && st.SummoningFor != null && ReferenceEquals(o.Source, st.SummoningFor)) continue;
                reason = $"You cannot Summon other monsters this turn ({o.Source?.Name ?? "oath"}); you can still Set.";
                return true;
            }

            return false;
        }

        /// <summary>
        /// The player who performs a Special Summon onto <paramref name="receiver"/>'s field: the Token clause's resolving
        /// player while it sends Tokens to the opponent's field (<see cref="SummonTokens"/>), else the receiving player.
        /// The oath binds the summoning player only (Lua EFFECT_CANNOT_SPECIAL_SUMMON, target range (1,0)): the opponent's
        /// Ojama Trio still Summons its Tokens onto the oath player's field.
        /// </summary>
        public static DuelistState SummoningPlayer(DuelEngine engine, DuelistState receiver) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.SummonerOverride != null
                ? st.SummonerOverride
                : receiver;

        /// <summary>
        /// The <see cref="EffectActionKind.SpecialSummonToken"/> arm: an all-or-nothing clause needs every Token's zone at
        /// resolution (Lua GetLocationCount); the Summon of an oath card's own Tokens is exempt from its oath; Tokens sent to
        /// the opponent's field count as <paramref name="who"/>'s Summon. An all-or-nothing clause with no printed position
        /// (Multiplication of Ants, Lua POS_FACEUP) asks the face-up position of each Token first (<see cref="AskTokenPositions"/>);
        /// <paramref name="summon"/> gets the chosen positions (null: the clause's own position).
        /// </summary>
        internal static void SummonTokens(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source,
            Action<IReadOnlyList<BattlePosition>> summon)
        {
            if (engine == null || who == null || clause == null || summon == null) return;
            if (clause.TokensNeedAllZones && !AllZonesFree(engine, who, clause, source)) return;
            if (AsksTokenPositions(clause))
            {
                AskTokenPositions(engine, who, clause, source, summon, new List<BattlePosition>());
                return;
            }

            PerformTokenSummon(engine, who, clause, source, summon, null);
        }

        static bool AllZonesFree(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source)
        {
            var dest = clause.TokenToOpponent ? engine.OpponentOf(who) : who;
            var need = Math.Max(1, clause.TokenCount);
            if (dest != null && FreeZonesPlacedBy(engine, who, dest) >= need) return true;
            engine.Log($"{source?.Name ?? "Effect"}: {need} free Monster Zones are needed — no Token is Summoned.");
            return false;
        }

        /// <summary>"Special Summon N Tokens" with no printed position, all or none, to its own field (Lua POS_FACEUP).</summary>
        static bool AsksTokenPositions(EffectClause c) =>
            c.TokensNeedAllZones && !c.SummonInDefense && !c.TokenToOpponent && !c.TokenFillRemainingZones;

        /// <summary>
        /// The summoner chooses face-up Attack or Defense Position for each Token (ygopro asks the position of every
        /// SpecialSummonStep with POS_FACEUP), through the option tokens of the generic card pick; the AI takes Defense
        /// when the Token's DEF is higher than its ATK.
        /// </summary>
        static void AskTokenPositions(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source,
            Action<IReadOnlyList<BattlePosition>> summon, List<BattlePosition> chosen)
        {
            var count = Math.Max(1, clause.TokenCount);
            if (chosen.Count >= count)
            {
                // The prompts may have waited: still all the zones, or none (s.activate's GetLocationCount).
                if (AllZonesFree(engine, who, clause, source))
                    PerformTokenSummon(engine, who, clause, source, summon, chosen);
                return;
            }

            var aiPos = clause.TokenDef > clause.TokenAtk ? BattlePosition.Defense : BattlePosition.Attack;
            var options = new List<CardInstance>
            {
                OptionToken("Face-up Attack Position", 0),
                OptionToken("Face-up Defense Position", 1)
            };
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => options,
                Kind = EffectTargetKind.None,
                Prompt = $"{source?.Name ?? "Effect"}: Special Summon {clause.TokenName} ({chosen.Count + 1} of {count}) — " +
                         "choose its position.",
                AiChoose = list => list.ElementAtOrDefault(aiPos == BattlePosition.Defense ? 1 : 0),
                Finish = pick =>
                {
                    chosen.Add(pick?.Def?.level == 1 ? BattlePosition.Defense
                        : pick != null ? BattlePosition.Attack : aiPos);
                    AskTokenPositions(engine, who, clause, source, summon, chosen);
                }
            });
        }

        static void PerformTokenSummon(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source,
            Action<IReadOnlyList<BattlePosition>> summon, IReadOnlyList<BattlePosition> positions)
        {
            var st = StateOf(engine);
            var prevFor = st.SummoningFor;
            var prevBy = st.SummonerOverride;
            st.SummoningFor = clause.ForbidsOtherSummonsThisTurn ? source : prevFor;
            st.SummonerOverride = clause.TokenToOpponent ? who : prevBy;
            try
            {
                summon(positions);
            }
            finally
            {
                st.SummoningFor = prevFor;
                st.SummonerOverride = prevBy;
            }
        }

        /// <summary>A declaration token (CallsDiceAndSetTriggersRuntime.OptionTokenKind) standing for one option.</summary>
        static CardInstance OptionToken(string name, int value) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new WRLDZ.Data.CardDef
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

        static int FreeZones(DuelistState side) => ZoneCapsRuntime.UsableMonsterZones(side); // ZoneCapsAndPhaseWindows: caps

        /// <summary>ZoneCapsAndPhaseWindows batch: <see cref="FreeZones"/> for Tokens <paramref name="who"/>'s effect places.</summary>
        static int FreeZonesPlacedBy(DuelEngine engine, DuelistState who, DuelistState dest)
        {
            using (ZoneCapsRuntime.PlacedBy(engine, who))
                return FreeZones(dest);
        }

        // ───────────────────────────── activation conditions ─────────────────────────────

        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null) return false;
            switch (c.ActivationCondition)
            {
                case TokenSummonLegal:
                    ok = TokenActivationLegal(engine, who, card, c, out reason);
                    return true;
                case HandMonsterCanBeSpecialSummoned:
                    ok = who != null && engine.FirstEmpty(who.MonsterZones) >= 0 &&
                         who.Hand.Any(h => h?.Def != null && h.Def.IsMonster &&
                                           SpecialSummonLegality.CanBeSpecialSummoned(engine, who, h, card,
                                               SummonOrigin.Hand, requireZone: true));
                    reason = "Needs a free Monster Zone and a monster in your hand that can be Special Summoned.";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// SpellTrapEffects.AttackWindowClauseLegal: the attack window offers A Hero Emerges to its responder only when its
        /// activation condition holds (the window lists Set Traps without asking CanActivate).
        /// </summary>
        public static bool AttackAnswerLegal(DuelEngine engine, EffectClause c)
        {
            if (c == null || c.ActivationCondition != HandMonsterCanBeSpecialSummoned) return true;
            var who = engine?.PendingResponse?.Responder;
            return who != null && TryActivationCondition(engine, who, null, c, out var ok, out _) && ok;
        }

        static bool TokenActivationLegal(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out string reason)
        {
            reason = null;
            if (engine == null || who == null) return false;
            if (c.ForbidsOtherSummonsThisTurn && SummonedThisTurn(engine, who))
            {
                reason = "You already Summoned a monster this turn.";
                return false;
            }

            if (!c.TokensNeedAllZones) return true;
            var dest = c.TokenToOpponent ? engine.OpponentOf(who) : who;
            var need = Math.Max(1, c.TokenCount);
            var free = dest == null ? 0 : FreeZonesPlacedBy(engine, who, dest);
            // Multiplication of Ants (s.cfilter: ft>1, or a Tribute in your own Main Monster Zone): the Tribute frees a zone.
            if (free < need && c.RequiresTributeCount > 0 && ReferenceEquals(dest, who) && free >= need - 1 &&
                TextEffectRuntime.CollectTributeCost(who, card, c, engine).Exists(m => who.TryFindMonster(m, out _)))
                return true;
            if (free >= need) return true;
            reason = $"Needs {need} free Monster Zones.";
            return false;
        }

        /// <summary>
        /// TextEffectRuntime.CollectTributeCost: with fewer free zones than an all-or-nothing Token clause needs, its Tribute
        /// must free one of your own Main Monster Zones (Multiplication of Ants s.cfilter).
        /// </summary>
        public static void FilterTributeCostCandidates(DuelEngine engine, DuelistState who, EffectClause c,
            List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null || !c.TokensNeedAllZones || c.TokenToOpponent)
                return;
            if (FreeZones(who) >= Math.Max(1, c.TokenCount)) return;
            list.RemoveAll(m => m == null || !who.TryFindMonster(m, out _));
        }

        // ───────────────────────────── DNA Transplant ─────────────────────────────

        /// <summary>One declaration token per Attribute (TypeDeclarationRuntime's Attribute tokens).</summary>
        public static List<CardInstance> AttributeDeclarationTokens() => TypeDeclarationRuntime.AttributeTokens(Attributes);

        /// <summary>
        /// Homunculus the Alchemic Being (c40410110): ignition ignition - the controller ANNOUNCES ANOTHER Attribute
        /// (one the card is not). The pick is the Declare Monster Attribute tokens minus the current one; when the
        /// card is later "targeted by a Spell" the live attribute flows through EquipFamilyRuntime.CurrentAttribute.
        /// </summary>
        public static void ChangeSelfAttribute(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            // Lua s.attop: c:IsFaceup() and c:IsRelateToEffect(e) — still face-up on its controller's field (v208 QC).
            if (!source.FaceUp || !who.TryFindMonster(source, out _))
            {
                engine.Log(source.Name + " is no longer face-up on the field — its Attribute does not change.");
                return;
            }

            var current = TextEffects.EquipFamilyRuntime.CurrentAttribute(source);
            var tokens = TypeDeclarationRuntime.AttributeTokens(Attributes)
                .Where(t => !string.Equals(t.Def?.attribute, current, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (tokens.Count == 0)
            {
                engine.Log(source.Name + ": there is no other Attribute to declare.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => tokens,
                Kind = EffectTargetKind.DiscardFromHand,   // any pick works; this just labels the prompt
                Prompt = source.Name + ": choose the new Attribute for this card (" + current + " is excluded).",
                AskWithOneCandidate = true,
                AiChoose = list => list.OrderBy(t => t.Def?.attribute).FirstOrDefault(),
                Finish = chosen =>
                {
                    if (chosen == null || !source.FaceUp || !who.TryFindMonster(source, out _)) return;
                    var attr = chosen.Def?.attribute;
                    if (string.IsNullOrEmpty(attr)) return;
                    TextEffects.EquipFamilyRuntime.SetSelfAttribute(engine, source, attr);
                    engine.Log(source.Name + " is now " + attr + " until it leaves the field.");
                    engine.NotifyPublic();
                }
            });
        }

        /// <summary>
        /// AI declaration: the Attribute most of its own face-up monsters already have (keeps its Attribute-based effects
        /// working); else the first Attribute.
        /// </summary>
        public static CardInstance AiPickAttribute(DuelEngine engine, DuelistState who, List<CardInstance> tokens)
        {
            if (tokens == null || tokens.Count == 0) return null;
            var own = who?.MonstersOnField().Where(m => m != null && m.FaceUp).Select(m => m.Def?.attribute)
                .Where(a => !string.IsNullOrEmpty(a)).ToList() ?? new List<string>();
            var best = tokens.OrderByDescending(t => own.Count(a =>
                    string.Equals(a, t.Def?.attribute, StringComparison.OrdinalIgnoreCase)))
                .First();
            return best;
        }

        /// <summary>
        /// <see cref="EffectActionKind.DeclareMonsterAttribute"/>: remember the declared Attribute on the card (it was
        /// announced at activation, so a negated resolution keeps it; it applies only while the card's effects do).
        /// </summary>
        public static void RecordDeclaredAttribute(DuelEngine engine, CardInstance source, CardInstance token)
        {
            if (engine == null || source == null) return;
            if (!TypeDeclarationRuntime.IsAttributeToken(token))
            {
                engine.Log($"{source.Name}: no Attribute was declared.");
                return;
            }

            s_declared.Remove(source);
            if (!OnSpellTrapField(engine, source))
            {
                engine.Log($"{source.Name} is no longer on the field — its declared Attribute applies to nothing.");
                return;
            }

            var attribute = token.Def.attribute;
            s_declared.Add(source, new Declaration { Attribute = attribute, Stamp = TypeDeclarationRuntime.NextStamp() });
            engine.Log($"{source.Name}: {attribute} is the declared Attribute — face-up monsters on the field become {attribute}.");
            var st = StateOf(engine);
            if (!st.AttributeChangers.Contains(source))
                st.AttributeChangers.Add(source);
            for (var i = s_attributeEngines.Count - 1; i >= 0; i--)
            {
                if (!s_attributeEngines[i].TryGetTarget(out var e) || e == null)
                {
                    s_attributeEngines.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(e, engine)) return;
            }

            s_attributeEngines.Add(new WeakReference<DuelEngine>(engine));
        }

        /// <summary>
        /// EquipFamilyRuntime.CurrentAttribute: the Attribute a face-up monster on the field has from a face-up, applying
        /// DNA Transplant (the latest declaration wins), with its timestamp; null when none applies.
        /// </summary>
        public static string AttributeOnField(CardInstance card, out long stamp)
        {
            stamp = long.MinValue;
            if (card == null || !card.FaceUp || s_attributeEngines.Count == 0) return null;
            foreach (var wr in s_attributeEngines)
            {
                if (!wr.TryGetTarget(out var engine) || engine == null) continue;
                if (!(engine.Player?.TryFindMonster(card, out _) ?? false) &&
                    !(engine.Opponent?.TryFindMonster(card, out _) ?? false))
                    continue;
                if (!s_state.TryGetValue(engine, out var st)) return null;
                string best = null;
                foreach (var src in st.AttributeChangers)
                {
                    if (src == null || !src.FaceUp || src.IsNegated || !OnSpellTrapField(engine, src)) continue;
                    if (!s_declared.TryGetValue(src, out var d) || string.IsNullOrEmpty(d.Attribute)) continue;
                    if (!ChangesAttributes(src) || d.Stamp <= stamp) continue;
                    stamp = d.Stamp;
                    best = d.Attribute;
                }

                return best;
            }

            return null;
        }

        static bool ChangesAttributes(CardInstance src)
        {
            var prog = CompiledEffectCache.GetOrCompile(src?.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                c.Action == EffectActionKind.AllFaceUpMonstersBecomeDeclaredAttribute);
        }

        static bool OnSpellTrapField(DuelEngine engine, CardInstance card) =>
            (engine.Player != null && engine.Player.TryFindSpellTrap(card, out _)) ||
            (engine.Opponent != null && engine.Opponent.TryFindSpellTrap(card, out _));

        // ───────────────────────────── Jade Insect Whistle ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.OpponentPlacesRaceMonsterFromTheirDeckOnTop"/> (Lua s.activate): <paramref name="who"/>'s
        /// opponent selects 1 monster of the clause's Type from their own Deck (mandatory when one exists; the human picks on
        /// the AI's card), shuffles that Deck and places the card on top, revealed. No such monster: nothing happens.
        /// </summary>
        public static void ResolveOpponentPlacesOnTop(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var opp = engine?.OpponentOf(who);
            if (opp?.Deck == null || clause == null) return;
            var name = source?.Name ?? "Effect";
            var race = clause.RaceFilter ?? "";
            var picks = new List<(int index, CardInstance card)>();
            for (var i = 0; i < opp.Deck.Count; i++)
            {
                if (engine.Database == null || !engine.Database.TryGet(opp.Deck[i], out var def) || def == null) continue;
                if (!def.IsMonster || !ClassicEraTemplates.RaceMatches(def.race, race)) continue;
                picks.Add((i, engine.CreateCardInstance(opp.Deck[i])));
            }

            if (picks.Count == 0)
            {
                engine.Log($"{name}: {opp.Name} has no {race} monster in the Deck — nothing happens.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, opp, source, new CardPickRequest
            {
                Candidates = () => picks.Select(p => p.card).ToList(),
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = $"your opponent's {name}: select 1 {race} monster from your Deck. Your Deck is shuffled and " +
                         "that card is placed on top of it.",
                // The AI gives up its weakest one: it is the card it draws next.
                AiChoose = list => list.OrderBy(c => c.Def?.atk ?? 0).FirstOrDefault(),
                Finish = chosen => PlaceOnTop(engine, opp, name, picks, chosen)
            });
        }

        static void PlaceOnTop(DuelEngine engine, DuelistState opp, string name,
            List<(int index, CardInstance card)> picks, CardInstance chosen)
        {
            if (chosen == null) return;
            var entry = picks.FirstOrDefault(p => ReferenceEquals(p.card, chosen));
            var idx = entry.card != null && entry.index < opp.Deck.Count && opp.Deck[entry.index] == chosen.CardId
                ? entry.index
                : opp.Deck.IndexOf(chosen.CardId);
            if (idx < 0)
            {
                engine.Log($"{name}: {chosen.Name} is no longer in {opp.Name}'s Deck.");
                return;
            }

            var info = opp.Deck.TakeAt(idx);
            engine.Rng.Shuffle(opp.Deck);
            opp.Deck.Insert(0, chosen.CardId, info);
            engine.Log($"{name}: {opp.Name} shuffles the Deck and places {chosen.Name} on top of it (revealed).");
            engine.NotifyPublic();
        }

        // ───────────────────────────── A Hero Emerges ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.RandomHandCardSpecialSummonOrSendToGy"/> (Lua s.activate): with a free Monster Zone, a
        /// random card of <paramref name="who"/>'s hand is chosen and revealed; a monster that can be Special Summoned is
        /// Special Summoned in the face-up position its controller chooses (POS_FACEUP), any other card is sent to the GY.
        /// </summary>
        public static void ResolveRandomHandSummon(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var name = source?.Name ?? "Effect";
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{name}: no free Monster Zone — nothing happens.");
                return;
            }

            var hand = who.Hand.Where(h => h != null).ToList();
            if (hand.Count == 0)
            {
                engine.Log($"{name}: {who.Name} has no card in hand.");
                return;
            }

            var pick = hand[engine.Rng.Next(hand.Count)];
            engine.Log($"{name}: {engine.OpponentOf(who)?.Name ?? "The opponent"} chooses a random card from " +
                       $"{who.Name}'s hand — {pick.Name} is revealed.");
            if (pick.Def != null && pick.Def.IsMonster &&
                SpecialSummonLegality.CanBeSpecialSummoned(engine, who, pick, source, SummonOrigin.Hand, requireZone: true))
            {
                // Lua POS_FACEUP: its controller chooses face-up Attack or Defense Position (the AI: Defense when its DEF
                // is higher than its ATK).
                var aiPos = (pick.Def.def) > (pick.Def.atk) ? BattlePosition.Defense : BattlePosition.Attack;
                var options = new List<CardInstance>
                {
                    OptionToken("Face-up Attack Position", 0),
                    OptionToken("Face-up Defense Position", 1)
                };
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                {
                    Candidates = () => options,
                    Kind = EffectTargetKind.None,
                    Prompt = $"{name}: Special Summon {pick.Name} — choose its position.",
                    AiChoose = list => list.ElementAtOrDefault(aiPos == BattlePosition.Defense ? 1 : 0),
                    Finish = chosen => SummonRevealed(engine, who, source, pick,
                        chosen?.Def?.level == 1 ? BattlePosition.Defense
                        : chosen != null ? BattlePosition.Attack : aiPos)
                });
                return;
            }

            MonsterEffects.SendFromHandToGraveByInstance(who, pick);
            engine.Log($"{name}: {pick.Name} cannot be Special Summoned — it is sent to the GY.");
        }

        static void SummonRevealed(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick,
            BattlePosition pos)
        {
            var name = source?.Name ?? "Effect";
            // The position prompt may have waited: still in the hand, still summonable, still a free zone.
            if (!who.Hand.Contains(pick) ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, pick, source, SummonOrigin.Hand, requireZone: true))
            {
                engine.Log($"{name}: {pick.Name} can no longer be Special Summoned — nothing happens.");
                return;
            }

            who.Hand.Remove(pick);
            engine.MarkSpecialSummonFromHand(pick); // Chain Energy toll (RuleLocks batch)
            if (engine.SpecialSummonToField(who, pick, pos, true))
            {
                engine.Log($"{name}: {who.Name} Special Summons {pick.Name} in face-up " +
                           $"{(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                return;
            }

            if (!who.Hand.Contains(pick)) who.Hand.Add(pick);
            engine.Log($"{name}: {pick.Name} could not be Special Summoned.");
        }

        // ───────────────────────────── Wild Nature's Release ─────────────────────────────

        /// <summary>TextEffectRuntime.CollectTargets: Wild Nature's Release needs a target with DEF above 0 (HasNonZeroDefense).</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null) return;
            if (c.Action == EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase)
                list.RemoveAll(t => t == null || t.CurrentDef <= 0);
        }

        /// <summary>
        /// <see cref="EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase"/>: still a face-up monster on the field, the
        /// target gains ATK equal to its current DEF until the end of the turn and is destroyed during this End Phase
        /// (both end if it is flipped face-down or leaves the field: DuelEngine.ClearFaceDownMarks / ResetOnFieldEntry).
        /// </summary>
        public static void ApplyDefToAtkThenDestroy(DuelEngine engine, CardInstance source, CardInstance target)
        {
            if (engine == null) return;
            var name = source?.Name ?? "Effect";
            var holder = target != null ? engine.ControllerOf(target) : null;
            if (target == null || holder == null || !holder.TryFindMonster(target, out _) || !target.FaceUp)
            {
                engine.Log($"{name}: the target is not a face-up monster on the field — no effect.");
                return;
            }

            var gain = Math.Max(0, target.CurrentDef);
            target.UntilEndOfTurnAtk += gain;
            target.TempDestroyOnEndOfTurn = engine.TurnNumber;
            engine.Log($"{name}: {target.Name} gains {gain} ATK (→ {target.CurrentAtk}); it is destroyed during the End Phase.");
        }

        // ───────────────────────────── Balloon Lizard ─────────────────────────────

        /// <summary><see cref="EffectActionKind.PlaceCounterOnThisCard"/>: put the counter(s) on this face-up monster.</summary>
        public static void PlaceCounter(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || !source.FaceUp || !who.TryFindMonster(source, out _))
                return;
            // Counters of an earlier presence are gone (a card that leaves the field or is turned face-down loses them).
            if (!SamePresence(source))
            {
                source.Counters = 0;
                s_counterPresence.Remove(source);
                s_counterPresence.Add(source,
                    new CounterStamp { Entry = source.FieldEntryStamp, FaceUp = source.FaceUpStamp });
            }

            source.Counters += Math.Max(1, clause?.Amount ?? 1);
            engine.Log($"{source.Name}: a counter is placed on it ({source.Counters} counter(s)).");
        }

        /// <summary>The counters <paramref name="card"/> had when it left the field (its last field presence only).</summary>
        public static int CountersOfLastPresence(CardInstance card) =>
            card != null && card.FaceUp && SamePresence(card) ? Math.Max(0, card.Counters) : 0;

        static bool SamePresence(CardInstance card) =>
            s_counterPresence.TryGetValue(card, out var p) && p.Entry == card.FieldEntryStamp &&
            p.FaceUp == card.FaceUpStamp;

        /// <summary>
        /// <see cref="EffectActionKind.InflictCountersTimesAmountToDestroyer"/> (Lua e2/e3): destroyed with 1 or more
        /// counters on it, inflict counters x Amount damage to the player whose card destroyed it (the reason player:
        /// EquipFamilyRuntime's destroy record — the other battler's controller, or the destroying effect's player).
        /// </summary>
        public static void InflictCounterBurn(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (engine == null || source == null || clause == null) return;
            var n = CountersOfLastPresence(source);
            if (n <= 0)
            {
                engine.Log($"{source.Name}: it had no counters — no damage.");
                return;
            }

            var rp = EquipFamilyRuntime.DestroyReasonPlayer(engine, source);
            if (rp == null)
            {
                engine.Log($"{source.Name}: no player's card destroyed it — no damage.");
                return;
            }

            engine.ApplyEffectDamage(rp, n * Math.Max(0, clause.Amount), source.Name, source);
        }
    }
}
