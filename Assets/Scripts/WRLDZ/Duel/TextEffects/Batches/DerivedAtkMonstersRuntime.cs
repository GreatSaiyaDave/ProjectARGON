using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DerivedAtkMonsters" (v176). Clauses: <see cref="DerivedAtkMonstersAtoms"/>.
    /// <list type="bullet">
    /// <item>The Tribute record: <see cref="NoteTributeSummon"/> (DuelEngine, at every Tribute Summon / Tribute Set) stores
    /// the Tributes on the summoned card (<see cref="CardInstance.TributeMaterials"/>, ygopro GetMaterial).</item>
    /// <item>Great Maju Garzett / Maju Garzett: on a Tribute Summon (not a Set) the ATK becomes Amount × the Tributes'
    /// printed ATK ("?" = 0; Lua GetTextAttack). It lasts while the monster stays face-up on the field and its effects
    /// are not negated (RESETS_STANDARD_DISABLE without RESET_TOFIELD): <see cref="ApplyBoard"/> writes
    /// <see cref="CardInstance.SelfSetAtk"/> on each board refresh and drops the grant for good once it ends.</item>
    /// <item>Guardian Tryce: <see cref="SpecialSummonTributeMaterials"/> — the Tributes still in the GY they were sent to
    /// by its Tribute Summon (REASON_SUMMON, GetReasonCard), all of them or none (GetLocationCount &gt;= count).</item>
    /// <item>Gren Maju Da Eiza: the GainSelfAtkPerCount source <see cref="YourBanishedCards"/>
    /// (FieldSpellEffects.ApplySelfScale).</item>
    /// </list>
    /// State is kept per engine.
    /// </summary>
    public static class DerivedAtkMonstersRuntime
    {
        /// <summary>GainSelfAtkPerCount <see cref="EffectClause.CountSource"/>: every card in your banishment (face-down too).</summary>
        public const string YourBanishedCards = "YourBanishedCards";

        /// <summary><see cref="EffectActionKind.ThisAtkBecomesTributedOriginalAtk"/>: the first Tribute only (Lua GetFirst).</summary>
        public const string FirstTribute = "FirstTribute";

        /// <summary><see cref="EffectActionKind.ThisAtkBecomesTributedOriginalAtk"/>: every Tribute, summed.</summary>
        public const string AllTributes = "AllTributes";

        sealed class Grant
        {
            public CardInstance Card;
            public int Atk;
            public int FieldEntry;
            public int FaceDown;
        }

        sealed class State
        {
            public readonly List<Grant> Grants = new();
            /// <summary>Cards holding a Tribute record, swept by <see cref="ApplyBoard"/>.</summary>
            public readonly List<CardInstance> Recorded = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>
        /// DuelEngine (Normal Summon path), right after <paramref name="card"/> entered the field: record its Tributes and,
        /// for a Tribute Summon (not a Set) of a card with <see cref="EffectActionKind.ThisAtkBecomesTributedOriginalAtk"/>,
        /// set its ATK from them.
        /// </summary>
        public static void NoteTributeSummon(DuelEngine engine, CardInstance card, List<CardInstance> tributes, bool asSet)
        {
            if (engine == null || card == null) return;
            card.TributeMaterials = null;
            if (tributes == null || tributes.Count == 0) return;
            var record = new List<(CardInstance Card, int GyStamp)>();
            foreach (var t in tributes)
                if (t != null && InAnyGy(engine, t))
                    record.Add((t, t.GyEntryStamp));
            card.TributeMaterials = record;
            card.TributeMaterialsEntryStamp = card.FieldEntryStamp;
            var st = StateOf(engine);
            if (record.Count > 0 && !st.Recorded.Contains(card)) st.Recorded.Add(card);

            if (asSet || card.Def == null) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.ThisAtkBecomesTributedOriginalAtk);
            if (clause == null) return;
            var used = clause.CountSource == FirstTribute ? tributes.Where(t => t != null).Take(1) : tributes.Where(t => t != null);
            var sum = used.Sum(t => Math.Max(0, t.Def?.atk ?? 0));
            var atk = Math.Max(0, sum * Math.Max(1, clause.Amount));
            StateOf(engine).Grants.Add(new Grant
            {
                Card = card,
                Atk = atk,
                FieldEntry = card.FieldEntryStamp,
                FaceDown = card.FaceDownStamp
            });
            engine.Log($"{card.Name}: its ATK becomes {atk} (from the monster(s) Tributed for its Tribute Summon).");
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after the auras): every live Tribute-derived ATK is written to
        /// <see cref="CardInstance.SelfSetAtk"/>; an ended one (left the field, turned face-down, negated) is dropped for
        /// good. A self-set ATK/DEF left on a card that is no longer a monster on the field is cleared.
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null) return;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                ClearOffField(who.Hand);
                ClearOffField(who.Graveyard);
                ClearOffField(who.Banished);
                foreach (var st in who.SpellTrapsOnField()) ClearStale(st);
            }

            if (!s_state.TryGetValue(engine, out var state)) return;
            // A Tribute that left the GY it was sent to (to the hand, the Deck, the field, banished) is no longer the
            // monster "used for its Tribute Summon" (ygopro: its reason is replaced), even if it comes back later.
            for (var i = state.Recorded.Count - 1; i >= 0; i--)
            {
                var r = state.Recorded[i];
                if (r?.TributeMaterials == null || r.TributeMaterialsEntryStamp != r.FieldEntryStamp)
                {
                    state.Recorded.RemoveAt(i);
                    continue;
                }

                r.TributeMaterials.RemoveAll(t => t.Card == null || t.Card.GyEntryStamp != t.GyStamp ||
                                                  !InAnyGy(engine, t.Card));
                if (r.TributeMaterials.Count == 0) state.Recorded.RemoveAt(i);
            }

            for (var i = state.Grants.Count - 1; i >= 0; i--)
            {
                var g = state.Grants[i];
                var c = g.Card;
                var controller = c != null ? engine.ControllerOf(c) : null;
                var alive = c != null && c.FaceUp && controller != null && controller.TryFindMonster(c, out _) &&
                            c.FieldEntryStamp == g.FieldEntry && c.FaceDownStamp == g.FaceDown && !c.IsNegated;
                if (!alive)
                {
                    state.Grants.RemoveAt(i);
                    continue;
                }

                c.SelfSetAtk = g.Atk;
            }
        }

        static void ClearOffField(List<CardInstance> pile)
        {
            if (pile == null) return;
            foreach (var c in pile) ClearStale(c);
        }

        static void ClearStale(CardInstance c)
        {
            if (c == null) return;
            c.SelfSetAtk = int.MinValue;
            c.SelfSetDef = int.MinValue;
        }

        /// <summary>
        /// Guardian Tryce, on resolution: Special Summon (face-up) the monsters Tributed for <paramref name="source"/>'s
        /// Tribute Summon that are still in the GY they were sent to then, to <paramref name="who"/>'s field — all of them,
        /// or none when there are not enough free Monster Zones (Lua s.spop). Necrovalley and the Special Summon gates
        /// apply to each.
        /// </summary>
        internal static void SpecialSummonTributeMaterials(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            var eligible = new List<(CardInstance Card, DuelistState GyOwner)>();
            if (source.TributeMaterials != null && source.TributeMaterialsEntryStamp == source.FieldEntryStamp)
            {
                foreach (var (m, stamp) in source.TributeMaterials)
                {
                    if (m?.Def == null || !m.Def.IsMonster || m.GyEntryStamp != stamp) continue;
                    var gyOwner = engine.Player?.Graveyard?.Contains(m) == true ? engine.Player
                        : engine.Opponent?.Graveyard?.Contains(m) == true ? engine.Opponent
                        : null;
                    if (gyOwner == null) continue;
                    if (!TextEffectRuntime.CanSpecialSummonFromGyTarget(m)) continue;
                    if (ContinuousProtections.GyMovementLocked(engine, source, m)) continue;
                    eligible.Add((m, gyOwner));
                }
            }

            if (eligible.Count == 0)
            {
                engine.Log($"{source.Name}: no monster used for its Tribute Summon is in the GY — nothing is Special Summoned.");
                return;
            }

            var free = who.MonsterZones.Count(z => z.IsEmpty);
            if (free < eligible.Count)
            {
                engine.Log($"{source.Name}: not enough free Monster Zones — nothing is Special Summoned.");
                return;
            }

            foreach (var (m, gyOwner) in eligible)
            {
                if (!gyOwner.Graveyard.Contains(m)) continue;
                gyOwner.Graveyard.Remove(m);
                DuelEngine.RecordOwnerOnFieldEntry(m, gyOwner, who);
                engine.NextSpecialSummonFromGraveyard = true;
                if (!engine.SpecialSummonToField(who, m, BattlePosition.Attack, true))
                {
                    m.ControlOwner = null;
                    gyOwner.Graveyard.Add(m);
                    engine.Log($"{source.Name}: {m.Name} cannot be Special Summoned.");
                    continue;
                }

                engine.Log($"{source.Name}: Special Summoned {m.Name} from the GY (used for its Tribute Summon).");
            }
        }

        static bool InAnyGy(DuelEngine engine, CardInstance c) =>
            engine.Player?.Graveyard?.Contains(c) == true || engine.Opponent?.Graveyard?.Contains(c) == true;
    }
}
