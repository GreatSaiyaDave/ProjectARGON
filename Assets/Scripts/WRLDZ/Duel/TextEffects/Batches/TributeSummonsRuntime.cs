using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TributeSummons batch runtime (Tools/CardCompile/README.md). Atoms: <see cref="TributeSummonsAtoms"/>.
    /// <list type="bullet">
    /// <item>Tribute Doll: Tribute 1 monster (cost, the shared ignition-cost pick), then Special Summon a hand
    /// monster whose current Level is exactly <see cref="EffectClause.RequiresExactLevel"/> and that can be
    /// Normal Summoned/Set (<see cref="IsNormalSummonableCard"/>, ygopro Card.IsSummonableCard); it cannot
    /// attack this turn (<see cref="CardInstance.CannotAttackDuringTurnNumber"/>).</item>
    /// <item>Insect Imitation (<see cref="EffectClause.MatchTributedLevel"/>): only a monster with a Level whose
    /// Level + offset monster of the printed Type is in the Deck may be Tributed; the cost records that Level
    /// (<see cref="TributeCostNumeric"/>) and the Deck pick must match it exactly.</item>
    /// <item>Magical Labyrinth (<see cref="EffectClause.RequiresTributeEquippedMonster"/>): Equip only to the
    /// named monster; ignition while you control the equipped monster — Tribute it (this card goes too) and
    /// Special Summon the named monster from the Deck. <see cref="PayTributeEquippedCost"/> is the only way to
    /// reach that summon: <see cref="TributeEquippedCostPaid"/> guards the clause everywhere else.</item>
    /// <item>Soul Exchange (<see cref="EffectActionKind.TributeTargetAsIfControlledThisTurn"/>): oath first
    /// (<see cref="DuelistState.BattlePhaseForbiddenTurn"/>), then the grant read by <see cref="ForcedTributes"/>.
    /// Every group Tribute of the controller this turn — Tribute Summon/Set (DuelEngine.ResolveTributes),
    /// Tribute costs and effect Tributes (TextEffectRuntime.CollectTributeCost), Ritual Tributes
    /// (RitualProcedures) — offers the target as if they controlled it and puts it first; one-at-a-time picks
    /// offer only it (<see cref="RestrictToForced"/>) until it is paid (ygopro EFFECT_EXTRA_RELEASE: the
    /// "must" Tributes are chosen first, and only where the target is itself an eligible Tribute). Each resolved
    /// copy adds its own grant (<see cref="DuelistState.MustTributeGrants"/>).</item>
    /// <item>Wall Shadow (made deckable by Magical Labyrinth) and Harpie Lady Sisters: "Must first be Special
    /// Summoned with "X"" is enforced (<see cref="SemiNomiSummonAllowed"/>): only X's effect summons them from the
    /// hand or Deck, and only a properly summoned copy (<see cref="CardInstance.ProcedureCompleted"/>) comes back
    /// from the GY or banishment.</item>
    /// </list>
    /// </summary>
    public static class TributeSummonsRuntime
    {
        // ───────────────────── Summonability (ygopro IsSummonableCard / IsCanBeSpecialSummoned) ─────────────────────

        /// <summary>Unconditional "Cannot be Normal Summoned/Set." (ygopro TYPE_SPSUMMON / EFFECT_UNSUMMONABLE_CARD).</summary>
        static readonly Regex RxUnsummonable = new(
            @"\bcannot be Normal Summoned(?:/Set| or Set)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Special Summon monster: "Must (first) be Special Summoned …" / "can only be Special Summoned …".</summary>
        static readonly Regex RxSpecialSummonMonster = new(
            @"\bmust (?:first )?be Special Summoned\b|\bcan only be Special Summoned\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// ygopro Card.IsSummonableCard: a Main Deck monster card (not a Token, not a Ritual Monster) that is not
        /// printed as a Special Summon monster.
        /// </summary>
        public static bool IsNormalSummonableCard(CardInstance m)
        {
            if (m?.Def == null || m.IsToken) return false;
            return IsNormalSummonableDef(m.Def);
        }

        internal static bool IsNormalSummonableDef(CardDef def)
        {
            if (def == null || !def.IsMonster || def.IsExtraDeck || def.IsRitualMonster || def.level < 1)
                return false;
            var text = OfficialCardAuthority.OfficialText(def);
            return !RxUnsummonable.IsMatch(text) && !RxSpecialSummonMonster.IsMatch(text);
        }

        /// <summary>
        /// ygopro IsCanBeSpecialSummoned(e,0,tp,false,false) for a monster in the hand or Deck: not a hard Nomi,
        /// not a Special Summon monster that has not been properly summoned, not an Extra Deck / Ritual monster.
        /// </summary>
        static bool CanBeSpecialSummonedByEffect(CardDef def)
        {
            if (def == null || !def.IsMonster || def.IsExtraDeck || def.IsRitualMonster) return false;
            var text = OfficialCardAuthority.OfficialText(def);
            return !PsctGrammar.BlocksSpecialSummon(text) && !RxUnsummonable.IsMatch(text) &&
                   !RxSpecialSummonMonster.IsMatch(text);
        }

        /// <summary>The engine would accept this Special Summon now (hard Nomi, "unless you control" gates).</summary>
        static bool SummonGateOpen(DuelistState who, CardInstance m)
        {
            if (m?.Def == null) return false;
            var text = OfficialCardAuthority.OfficialText(m);
            if (PsctGrammar.BlocksSpecialSummon(text)) return false;
            var gate = PsctGrammar.SummonGateNamed(text);
            return string.IsNullOrEmpty(gate) || FieldSpellEffects.ControlsFaceUpNamed(who, gate);
        }

        // ───────────────────── Named-card Semi-Nomi: "Must first be Special Summoned with "X"" ─────────────────────

        /// <summary>Wall Shadow (Magical Labyrinth), Harpie Lady Sisters (Elegant Egotist): ygopro EnableReviveLimit.</summary>
        static readonly Regex RxMustFirstWithNamed = new(
            @"Must first be Special Summoned with ""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// TargetNegationsAndHandQuickEffects batch (v163): Fushioh Richie (ygopro EnableReviveLimit; Great Dezard's
        /// IsCanBeSpecialSummoned(e,0,tp,true,false) + CompleteProcedure) — only that card's effect summons it from the hand
        /// or Deck, and a properly summoned copy may be revived.
        /// </summary>
        static readonly Regex RxOnlyByTributingNamed = new(
            @"This card can only be Special Summoned from your hand or your Deck by Tributing 1 ""([^""]+)"" that has " +
            @"fulfilled the condition\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// RevivalEquipsAndGearfried batch: the whole sentence "Must be Special Summoned with "X"." — the period right after
        /// the name, so "…, and cannot be Special Summoned by other ways" (a hard Nomi, NamedSummonPairs) never matches.
        /// </summary>
        static readonly Regex RxMustWithNamedSentence = new(
            @"(?:^|(?<=[.!?]\s))Must be Special Summoned with ""([^""]+)""\.(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The card whose effect is this monster's proper Special Summon, or null.</summary>
        public static string ProcedureCardName(CardDef def)
        {
            if (def == null || !def.IsMonster) return null;
            var text = OfficialCardAuthority.OfficialText(def) ?? "";
            var m = RxMustFirstWithNamed.Match(text);
            if (!m.Success) m = RxOnlyByTributingNamed.Match(text);
            // RevivalEquipsAndGearfried batch: Gearfried the Swordmaster's "Must be Special Summoned with "X"." with no
            // "cannot be Special Summoned by other ways" (Lua EnableReviveLimit + AddMustBeSpecialSummoned; Release
            // Restraint summons it nocheck + CompleteProcedure). Main Deck monsters only.
            if (!m.Success && !def.IsExtraDeck) m = RxMustWithNamedSentence.Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }

        [ThreadStatic] static CardInstance s_namedSummonSource;

        /// <summary>
        /// Scope of a named-card Special Summon (TextEffectRuntime.TrySpecialSummonNamed): the card whose effect is
        /// summoning. Returns the previous scope for <see cref="EndNamedSummon"/>.
        /// </summary>
        public static CardInstance BeginNamedSummon(CardInstance source)
        {
            var previous = s_namedSummonSource;
            s_namedSummonSource = source;
            return previous;
        }

        public static void EndNamedSummon(CardInstance previous) => s_namedSummonSource = previous;

        /// <summary>
        /// The card whose named Special Summon is in progress (TextEffectRuntime.TrySpecialSummonNamed), or null. Read by
        /// NamedSummonPairsRuntime.NamedSummonerIsSummoning (the Nomi "Must be Special Summoned with "X"" lock).
        /// </summary>
        public static CardInstance CurrentNamedSummonSource => s_namedSummonSource;

        /// <summary>
        /// DuelEngine.SpecialSummonToField gate for a named-card Semi-Nomi: the card its text names is doing the
        /// summon (<paramref name="completesProcedure"/>: it is now properly summoned), or it was properly summoned
        /// before and is coming back from the GY or banishment. Every other monster passes.
        /// </summary>
        public static bool SemiNomiSummonAllowed(CardInstance card, out bool completesProcedure)
        {
            completesProcedure = false;
            var named = ProcedureCardName(card?.Def);
            if (named == null) return true;
            var source = s_namedSummonSource;
            if (source?.Def != null && string.Equals(source.Def.name, named, StringComparison.OrdinalIgnoreCase))
            {
                completesProcedure = true;
                return true;
            }

            return card.ProcedureCompleted;
        }

        /// <summary>A GY / banished Special Summon target: a named-card Semi-Nomi only once properly summoned.</summary>
        public static bool SemiNomiMayBeRevived(CardInstance card) =>
            card?.Def == null || card.ProcedureCompleted || ProcedureCardName(card.Def) == null;

        /// <summary>
        /// A hand / Deck Special Summon filter: a named-card Semi-Nomi can only be summoned from there by the card
        /// that names it, so only a clause naming it exactly may pick it (the engine gate then checks the source).
        /// </summary>
        public static bool SemiNomiDefMatches(CardDef def, EffectClause clause)
        {
            if (ProcedureCardName(def) == null) return true;
            return clause != null && !clause.NamedCardIsSeries &&
                   (string.Equals(clause.NamedCard, def.name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clause.AltNamedCard, def.name, StringComparison.OrdinalIgnoreCase));
        }

        // ───────────────────── Target filters (end of TextEffectRuntime.CollectTargets) ─────────────────────

        /// <summary>
        /// Batch filters on a clause's pick list (activation legality, the prompt and the AI pick all read it).
        /// <paramref name="costNumeric"/> is the Tributed monster's Level once the cost is paid (0 before).
        /// </summary>
        public static void FilterTargets(DuelistState who, EffectClause c, List<CardInstance> list,
            int costNumeric)
        {
            if (c == null || list == null) return;
            if (c.RequiresExactLevel > 0)
                list.RemoveAll(t => t == null || t.Level != c.RequiresExactLevel);
            if (c.RequiresCanBeNormalSummonedOrSet)
                list.RemoveAll(t => !IsNormalSummonableCard(t) || !SummonGateOpen(who, t));
            if (c.MatchTributedLevel)
            {
                list.RemoveAll(t => t?.Def == null || !CanBeSpecialSummonedByEffect(t.Def) ||
                                    !SummonGateOpen(who, t));
                if (costNumeric > 0)
                    list.RemoveAll(t => t.Level != costNumeric + c.TributedLevelOffset);
            }
        }

        // ───────────────────── Tribute Doll: Special Summon from the hand ─────────────────────

        /// <summary>This hand Special Summon carries a batch filter or rider (Tribute Doll shape).</summary>
        public static bool HandlesHandSummon(EffectClause c) =>
            c != null && (c.RequiresExactLevel > 0 || c.RequiresCanBeNormalSummonedOrSet ||
                          c.SummonCannotAttackThisTurn);

        static bool HandPickLegal(DuelistState who, EffectClause c, CardInstance m)
        {
            if (m?.Def == null || !m.Def.IsMonster || m.Def.IsExtraDeck) return false;
            if (!who.Hand.Contains(m)) return false;
            var one = new List<CardInstance> { m };
            FilterTargets(who, c, one, 0);
            if (one.Count == 0) return false;
            return string.IsNullOrEmpty(c.RaceFilter) || ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter);
        }

        /// <summary>
        /// Resolution of a filtered hand Special Summon: the chosen monster (or, with no pick, the strongest
        /// legal one) if it is still in the hand and still legal; then the "cannot attack this turn" rider.
        /// </summary>
        public static void SpecialSummonFromHandFiltered(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, CardInstance chosen)
        {
            if (engine == null || who?.Hand == null || c == null) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            CardInstance pick;
            if (chosen != null)
                pick = HandPickLegal(who, c, chosen) ? chosen : null;
            else
                pick = who.Hand.Where(m => HandPickLegal(who, c, m))
                    .OrderByDescending(m => m.CurrentAtk).FirstOrDefault();
            if (pick == null)
            {
                engine.Log($"{source?.Name}: no legal monster in the hand — nothing is Special Summoned.");
                return;
            }

            who.Hand.Remove(pick);
            engine.MarkSpecialSummonFromHand(pick); // Chain Energy toll (RuleLocks batch)
            if (!engine.SpecialSummonToField(who, pick, BattlePosition.Attack, true))
            {
                who.Hand.Add(pick);
                engine.Log($"{source?.Name}: the Special Summon failed.");
                return;
            }

            engine.Log($"Special Summoned {pick.Name} from hand.");
            if (c.SummonCannotAttackThisTurn && who.TryFindMonster(pick, out _))
            {
                pick.CannotAttackDuringTurnNumber = engine.TurnNumber;
                engine.Log($"{pick.Name} cannot attack this turn.");
            }
        }

        // ───────────────────── Insect Imitation: Level-matched Deck summon ─────────────────────

        /// <summary>
        /// Tribute-cost candidates that need the engine (applied to a Soul Exchange target like any other
        /// candidate, before it becomes a "must"):
        /// <list type="bullet">
        /// <item>a Special Summon paid with a Tribute on a full field needs a Tribute from your own Main Monster
        /// Zone (ygopro ft&gt;0 or IsControler(tp));</item>
        /// <item>a Special Summon whose Tribute must be of a printed Type / kind, or of a Level with a Deck match
        /// (Ultra Evolution Pill, Insect Imitation), cannot Tribute an opponent's face-down monster: its
        /// properties are hidden (ygopro cfilter (IsControler(tp) or IsFaceup()));</item>
        /// <item>a Level-matched summon only offers monsters whose Level + offset has a summonable match in the
        /// Deck;</item>
        /// <item>"make your opponent Tribute 1 monster" (Share the Pain) cannot be paid with the last monster your
        /// opponent controls (ygopro cfilter CheckReleaseGroup(1-tp,nil,1,c)).</item>
        /// </list>
        /// </summary>
        public static void FilterTributeCostCandidates(DuelEngine engine, DuelistState who, EffectClause c,
            List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null) return;
            bool NotYours(CardInstance m) => m != null && !who.TryFindMonster(m, out _);
            var summons = IsSpecialSummonAction(c.Action);
            if (summons && engine.FirstEmpty(who.MonsterZones) < 0)
                list.RemoveAll(m => m == null || NotYours(m));
            if (c.MatchTributedLevel ||
                (summons && (!string.IsNullOrEmpty(c.TributeRaceFilter) || c.TributeNormalMonsterOnly)))
                list.RemoveAll(m => NotYours(m) && !m.FaceUp);
            if (c.MatchTributedLevel)
                list.RemoveAll(m => m == null || m.Level <= 0 ||
                                    !(c.Action == EffectActionKind.SpecialSummonFusionFromExtra
                                        // SummonTriggersAndTributes batch (v163): Metamorphosis — an Extra Deck Fusion match.
                                        ? SummonTriggersAndTributesRuntime.ExtraHasLevelMatch(engine, who,
                                            m.Level + c.TributedLevelOffset)
                                        : DeckHasLevelMatch(engine, who, c, m.Level + c.TributedLevelOffset)));
            if (c.Action == EffectActionKind.OpponentTributesOneMonster)
            {
                var opp = engine.OpponentOf(who);
                list.RemoveAll(m => NotYours(m) &&
                                    (opp == null || !opp.MonstersOnField().Any(x => x != null && x != m)));
            }
        }

        static bool IsSpecialSummonAction(EffectActionKind a) =>
            a == EffectActionKind.SpecialSummonFromHand || a == EffectActionKind.SpecialSummonFromDeck ||
            a == EffectActionKind.SpecialSummonNamed || a == EffectActionKind.SpecialSummonFromGy ||
            a == EffectActionKind.SpecialSummonChosenFromHandOrDeck ||
            a == EffectActionKind.SpecialSummonFusionFromExtra || // Metamorphosis (SummonTriggersAndTributes, v163)
            // Multiply (ygopro cfilter ft>0 or (GetSequence()<5 and IsControler(tp))).
            a == EffectActionKind.SpecialSummonToken;

        static bool DeckHasLevelMatch(DuelEngine engine, DuelistState who, EffectClause c, int level)
        {
            if (engine?.Database == null || who?.Deck == null) return false;
            foreach (var id in who.Deck)
            {
                if (!engine.Database.TryGet(id, out var def) || def == null) continue;
                if (def.level != level || !CanBeSpecialSummonedByEffect(def)) continue;
                if (!string.IsNullOrEmpty(c.RaceFilter) && !ClassicEraTemplates.RaceMatches(def.race, c.RaceFilter))
                    continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The numeric cost a Tribute records: its current Level for a Level-matched summon (ygopro
        /// e:SetLabel(rg:GetFirst():GetLevel()) before the release), otherwise the caller's ATK value.
        /// </summary>
        public static int TributeCostNumeric(EffectClause c, CardInstance tributed, int atkValue) =>
            c != null && (c.MatchTributedLevel ||
                          // DeckSummonsAndSarcophagi batch: Ninjitsu Art of Transformation (e:SetLabel(lv+3)).
                          DeckSummonsAndSarcophagiRuntime.RecordsTributedLevel(c)) && tributed != null
                ? tributed.Level
                : atkValue;

        /// <summary><see cref="TributeCostNumeric"/> for a pick whose clause is found from the paying card.</summary>
        public static int TributeCostNumeric(CardInstance source, CardInstance tributed, int atkValue)
        {
            var prog = source?.Def != null ? CompiledEffectCache.GetOrCompile(source) : null;
            var c = prog?.ClausesFor(EffectTiming.Activate).FirstOrDefault(x => x != null &&
                (x.MatchTributedLevel || DeckSummonsAndSarcophagiRuntime.RecordsTributedLevel(x)));
            return TributeCostNumeric(c, tributed, atkValue);
        }

        /// <summary>
        /// Resolution of a Level-matched Deck summon: the chosen Deck monster (or the first legal one) whose Level
        /// is the recorded cost Level + offset; the Deck is shuffled after the search.
        /// </summary>
        public static void SpecialSummonLevelMatchedFromDeck(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause c, CardInstance chosen, int costNumeric)
        {
            if (engine?.Database == null || who?.Deck == null || c == null) return;
            if (costNumeric <= 0)
            {
                engine.Log($"{source?.Name}: no Tributed Level recorded — nothing is Special Summoned.");
                return;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            var want = costNumeric + c.TributedLevelOffset;
            bool Legal(CardDef def) =>
                def != null && def.level == want && CanBeSpecialSummonedByEffect(def) &&
                (string.IsNullOrEmpty(c.RaceFilter) || ClassicEraTemplates.RaceMatches(def.race, c.RaceFilter));

            var index = -1;
            for (var i = 0; i < who.Deck.Count; i++)
            {
                if (!engine.Database.TryGet(who.Deck[i], out var def) || !Legal(def)) continue;
                if (chosen != null && who.Deck[i] != chosen.CardId) continue;
                index = i;
                break;
            }

            if (index < 0)
            {
                engine.Log($"{source?.Name}: no Level {want} {c.RaceFilter} monster in the Deck — nothing is Special Summoned.");
                return;
            }

            var id = who.Deck[index];
            var inst = chosen != null && chosen.CardId == id ? chosen : engine.CreateCardInstance(id);
            // PerCopyDeck batch (v163): the instance takes the copy's owner; a failed Summon puts the same copy back.
            var copy = PerCopyDeckRuntime.TakeFromDeck(who, index, inst);
            if (!SummonGateOpen(who, inst) ||
                !engine.SpecialSummonToField(who, inst, BattlePosition.Attack, true))
            {
                who.Deck.Insert(index, id, copy);
                engine.Log($"{source?.Name}: the Special Summon failed.");
                return;
            }

            engine.Log($"Special Summoned {inst.Name} from Deck.");
            engine.Rng.Shuffle(who.Deck);
        }

        // ───────────────────── Magical Labyrinth: Tribute the equipped monster ─────────────────────

        [ThreadStatic] static CardInstance s_tributeEquippedPaidFor;

        /// <summary>Activation legality of the batch's clause-level conditions (Soul Exchange oath, Labyrinth cost).</summary>
        public static bool ActivationLegal(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out string reason)
        {
            reason = "OK";
            if (engine == null || who == null || c == null) return true;
            if (c.ForbidsBattlePhaseThisTurn && engine.Phase != DuelPhase.Main1)
            {
                reason = "You cannot conduct your Battle Phase the turn you activate this card — Main Phase 1 only.";
                return false;
            }

            if (c.RequiresTributeEquippedMonster)
            {
                var host = card?.EquippedTo;
                if (host == null || !who.TryFindSpellTrap(card, out _) || !card.FaceUp)
                {
                    reason = "This card must be face-up and equipped.";
                    return false;
                }

                if (!who.TryFindMonster(host, out _))
                {
                    reason = "You must control the equipped monster.";
                    return false;
                }

                if (ContinuousProtections.NeitherPlayerCanTribute(engine))
                {
                    reason = "Neither player can Tribute cards (Mask of Restrict).";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Cost of "You can Tribute the equipped monster; …": this card and the monster it equips go to the GY
        /// together (ygopro Duel.Release(Group.FromCards(c,tc),REASON_COST)). Marks the cost paid for this card.
        /// </summary>
        public static bool PayTributeEquippedCost(DuelEngine engine, DuelistState who, CardInstance card)
        {
            s_tributeEquippedPaidFor = null;
            if (!ActivationLegal(engine, who, card,
                    new EffectClause { RequiresTributeEquippedMonster = true }, out var reason))
            {
                engine?.Log($"{card?.Name}: {reason}");
                return false;
            }

            var host = card.EquippedTo;
            engine.Log($"Activate: {card.Name} — Tribute {host.Name} (cost).");
            engine.SendCardToGrave(who, card, sentBy: card);
            ReleaseEvents.Record(engine, host, who); // EngineHygiene: EVENT_RELEASE
            engine.SendCardToGrave(engine.ControllerOf(host) ?? who, host, sentBy: card);
            s_tributeEquippedPaidFor = card;
            return true;
        }

        /// <summary>True only between <see cref="PayTributeEquippedCost"/> and <see cref="EndTributeEquipped"/>.</summary>
        public static bool TributeEquippedCostPaid(CardInstance source) =>
            source != null && ReferenceEquals(s_tributeEquippedPaidFor, source);

        public static void EndTributeEquipped() => s_tributeEquippedPaidFor = null;

        // ───────────────────── Soul Exchange: Tribute the target as if you controlled it ─────────────────────

        /// <summary>
        /// Every opponent's monster <paramref name="who"/> must Tribute this turn, one per live grant, in grant
        /// order (two copies of Soul Exchange on two monsters make both "musts"). A grant holds while the duelist
        /// who controlled the target at resolution still controls the same field presence
        /// (<see cref="CardInstance.FieldEntryStamp"/>) and it was not turned face-down since
        /// (<see cref="CardInstance.FaceDownStamp"/>; ygopro RESETS_STANDARD includes RESET_TURN_SET, so an ended
        /// grant stays ended if the monster is flipped face-up again). Cleared when a turn begins.
        /// </summary>
        public static List<CardInstance> ForcedTributes(DuelistState who)
        {
            var list = new List<CardInstance>();
            if (who?.MustTributeGrants == null) return list;
            foreach (var g in who.MustTributeGrants)
            {
                var t = g?.Target;
                if (t == null || list.Contains(t)) continue;
                var holder = g.Holder;
                if (holder == null || ReferenceEquals(holder, who) || !holder.TryFindMonster(t, out _)) continue;
                if (t.FieldEntryStamp != g.EntryStamp || t.FaceDownStamp != g.FaceDownStamp) continue;
                list.Add(t);
            }

            return list;
        }

        /// <summary><paramref name="card"/> is one of <paramref name="who"/>'s live forced Tributes.</summary>
        public static bool IsForcedTribute(DuelistState who, CardInstance card) =>
            card != null && ForcedTributes(who).Contains(card);

        /// <summary>The forced Tributes that can be Tributed for a Tribute Summon (face-up Summon locks).</summary>
        public static List<CardInstance> ForcedTributesForSummon(DuelistState who)
        {
            var list = ForcedTributes(who);
            list.RemoveAll(f => f.CannotBeTributedForSummon && f.FaceUp && !f.IsNegated);
            return list;
        }

        /// <summary>
        /// Move the forced Tributes that are in <paramref name="list"/> to its front, in grant order, keeping the
        /// order of the rest (the "must" Tributes are chosen first).
        /// </summary>
        public static void MoveForcedFirst(DuelistState who, List<CardInstance> list,
            IList<CardInstance> forced = null)
        {
            if (list == null || list.Count == 0) return;
            forced ??= ForcedTributes(who);
            var at = 0;
            foreach (var f in forced)
            {
                if (f == null || !list.Remove(f)) continue;
                list.Insert(at++, f);
            }
        }

        /// <summary>
        /// Put the forced Tributes into a candidate list built from <paramref name="who"/>'s own monsters — as if
        /// they controlled them — first, when they pass <paramref name="eligible"/> (the site's own filters).
        /// </summary>
        public static void IncludeForced(DuelistState who, List<CardInstance> list, Func<CardInstance, bool> eligible)
        {
            if (list == null) return;
            var at = 0;
            foreach (var f in ForcedTributes(who))
            {
                list.Remove(f);
                if (eligible != null && !eligible(f)) continue;
                list.Insert(at++, f);
            }
        }

        /// <summary>
        /// A one-at-a-time Tribute pick: while any forced Tribute is a candidate, only forced Tributes are offered
        /// (so they are paid first, until every one is paid or the Tribute count is reached).
        /// </summary>
        public static List<CardInstance> RestrictToForced(DuelistState who, List<CardInstance> list)
        {
            if (list == null || list.Count == 0) return list;
            var forced = ForcedTributes(who).Where(list.Contains).ToList();
            return forced.Count > 0 ? forced : list;
        }

        /// <summary>
        /// The Ritual Tribute choice (<c>TextEffectRuntime.BeginRitualTributeStep</c>) is not a free choice while a
        /// forced Tribute is in the pool: resolution picks the Tributes, forced ones first
        /// (<see cref="RitualTributesWithForced"/>, which also honours tap-marked picks).
        /// </summary>
        public static bool RitualPoolHasForced(DuelistState who, IList<CardInstance> pool) =>
            pool != null && pool.Count > 0 && ForcedTributes(who).Any(pool.Contains);

        /// <summary>
        /// Tribute Summon/Set on a full field: when the forced Tributes alone are worth the Tributes needed, the
        /// Summon Tributes only the opponent's monsters and has no zone to land in.
        /// </summary>
        public static bool ForcedTributeLeavesNoZone(DuelEngine engine, DuelistState who, CardInstance summoning,
            int needed)
        {
            if (engine == null || who == null || needed <= 0) return false;
            if (engine.FirstEmpty(who.MonsterZones) >= 0) return false;
            var forced = ForcedTributesForSummon(who);
            if (forced.Count == 0) return false;
            return forced.Sum(f => TextEffectRuntime.TributeValueFor(f, summoning)) >= needed;
        }

        /// <summary>
        /// Ritual Tributes with forced Tributes in the pool: the forced monsters are chosen first — a selection
        /// with any other Tribute includes every forced one; a selection of forced monsters only may be any of
        /// them. Requested or pending picks first, then the fewest / lowest other Tributes that make the Levels
        /// legal. Null when none is legal.
        /// </summary>
        public static List<CardInstance> RitualTributesWithForced(DuelEngine engine, IList<CardInstance> pool,
            IList<CardInstance> forced, IList<CardInstance> requested, int required, bool exact)
        {
            if (pool == null || forced == null) return null;
            var inPool = forced.Where(f => f != null && pool.Contains(f)).Distinct().ToList();
            if (inPool.Count == 0) return null;
            var rest = pool.Where(c => c != null && !inPool.Contains(c)).ToList();
            var picks = requested != null && requested.Count > 0
                ? requested
                : (IList<CardInstance>)engine?.PendingTributes;
            var pickedRest = picks == null
                ? new List<CardInstance>()
                : picks.Where(t => t != null && rest.Contains(t)).Distinct().ToList();

            var chosen = new List<CardInstance>(inPool);
            chosen.AddRange(pickedRest);
            if (RitualProcedures.LevelsLegal(chosen, required, exact)) return chosen;
            var have = chosen.Sum(c => c.Level);
            if (have < required)
            {
                var extra = RitualProcedures.AutoPickTributes(rest.Where(c => !chosen.Contains(c)).ToList(),
                    required - have, exact);
                if (extra != null)
                {
                    chosen.AddRange(extra);
                    if (RitualProcedures.LevelsLegal(chosen, required, exact)) return chosen;
                }

                return null;
            }

            // Every forced monster together is already over an exact Level: only forced monsters may go
            // (the forced ones come first), and only when no other Tribute was picked.
            if (pickedRest.Count > 0) return null;
            return RitualProcedures.AutoPickTributes(inPool, required, exact);
        }

        /// <summary>Soul Exchange resolution: the oath, then the grant on a target still on the opponent's field.</summary>
        public static void ResolveTributeTargetAsIfControlled(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, CardInstance target)
        {
            if (engine == null || who == null || c == null) return;
            if (c.ForbidsBattlePhaseThisTurn)
                ApplyBattlePhaseOath(engine, who, source);
            var opp = engine.OpponentOf(who);
            if (target == null || opp == null || !opp.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name}: the target is no longer controlled by the opponent — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            // One grant per resolved copy: a second Soul Exchange adds a second "must", it does not replace the first.
            who.MustTributeGrants.Add(new TributeGrant
            {
                Target = target,
                Holder = opp,
                EntryStamp = target.FieldEntryStamp,
                FaceDownStamp = target.FaceDownStamp
            });
            engine.Log($"{source?.Name}: this turn, if {who.Name} Tributes a monster, {who.Name} must Tribute " +
                       $"{(target.FaceUp ? target.Name : "the targeted face-down monster")}, as if they controlled it.");
        }

        /// <summary>"You cannot conduct your Battle Phase the turn you activate this card."</summary>
        public static void ApplyBattlePhaseOath(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || who.BattlePhaseForbiddenTurn == engine.TurnNumber) return;
            who.BattlePhaseForbiddenTurn = engine.TurnNumber;
            engine.Log($"{source?.Name}: {who.Name} cannot conduct their Battle Phase this turn.");
        }

        /// <summary>The Soul Exchange oath is in force for <paramref name="who"/> this turn.</summary>
        public static bool BattlePhaseForbidden(DuelEngine engine, DuelistState who) =>
            engine != null && who != null &&
            ((who.BattlePhaseForbiddenTurn != 0 && who.BattlePhaseForbiddenTurn == engine.TurnNumber) ||
             // ConditionalSpellsAndTraps batch: D.D. Borderline (EFFECT_CANNOT_BP for both players while no Spell is in
             // its controller's GY).
             ConditionalSpellsAndTrapsRuntime.BattlePhaseLocked(engine, who));

        /// <summary>A new turn: every "this turn" Tribute grant ends.</summary>
        public static void ClearTurnGrants(params DuelistState[] duelists)
        {
            if (duelists == null) return;
            foreach (var d in duelists)
            {
                d?.MustTributeGrants?.Clear();
            }
        }
    }
}
