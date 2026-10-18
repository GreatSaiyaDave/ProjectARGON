using System;
using System.Text.RegularExpressions;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Master spec §11: every card has an explicit effect status.
    /// Computed from registry + FullyCompiled text programs — not guessed in play.
    /// Lua / YGOPro catalogs are oracle/linter only; they do not mark Implemented.
    /// </summary>
    public enum CardEffectStatusKind
    {
        /// <summary>Normal Monster or effectless Extra Deck — no activation.</summary>
        Structural = 0,
        /// <summary>Registry script or FullyCompiled text program.</summary>
        Implemented = 1,
        /// <summary>Some clauses compiled; remainder refused.</summary>
        Stub = 2,
        /// <summary>Effect text with no program. Activation must fail loud.</summary>
        Unimplemented = 3
    }

    public static class CardEffectStatus
    {
        /// <summary>
        /// Spec §11 / ERAZ compile gate: stubs and unimplemented stay out of decks.
        /// Lab decks are 40/40 implemented; default on.
        /// </summary>
        public static bool ExcludeUnimplementedFromDecks = true;

        public static CardEffectStatusKind Classify(CardDef def)
        {
            if (def == null) return CardEffectStatusKind.Unimplemented;
            if (OfficialCardAuthority.HasNoActivatableEffect(def))
                return CardEffectStatusKind.Structural;

            if (OfficialEffectRegistry.HasActivatableScript(def.id))
                return CardEffectStatusKind.Implemented;

            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.FullyCompiled && prog.CanResolveAny)
            {
                // DeckSummonsAndSarcophagi batch: "This card cannot be Special Summoned except by the effect of "X"."
                // (Spirit of the Pharaoh) — a card nothing can summon is not Implemented until X compiles to that summon.
                if (!TextEffects.DeckSummonsAndSarcophagiRuntime.NamedEffectSummonIsLive(def))
                    return CardEffectStatusKind.Stub;
                return CardEffectStatusKind.Implemented;
            }
            // Summon-lock text with no effect, and a procedure the engine can already
            // perform (Cocoon moths, Elegant Egotist → Harpie Lady Sisters,
            // Gate Guardian's three face-up tributes).
            if (prog != null && prog.FullyCompiled && prog.ClauseList.Count == 0 &&
                HasLiveSummonPath(def))
                return CardEffectStatusKind.Structural;
            if (prog != null && prog.ClauseList.Count > 0)
                return CardEffectStatusKind.Stub;

            return CardEffectStatusKind.Unimplemented;
        }

        static readonly Regex RxMustFirstWithNamedSpell = new(
            @"Must first be Special Summoned with ""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True when the printed lock names a summon the engine already performs.
        /// A restriction-only compile with no path stays Unimplemented.
        /// </summary>
        static bool HasLiveSummonPath(CardDef def)
        {
            if (def == null) return false;
            if (OfficialEffectRegistry.CocoonTurnsNeeded(def.id) > 0) return true;
            if (OfficialEffectRegistry.HasInherentHandTribute(def.id)) return true;
            // DestinyBoard batch: "This card can only be placed on the field by the effect of "X"." (Spirit Message) is
            // live while X compiles to a placement that names it.
            if (DestinyBoardRuntime.HasLivePlacementPath(def)) return true;
            var named = RxMustFirstWithNamedSpell.Match(def.desc ?? "");
            if (!named.Success) return false;
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            if (db == null) return false;
            CardDef spell = null;
            foreach (var c in db.GetAllCards())
            {
                if (c != null && string.Equals(c.name, named.Groups[1].Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    spell = c;
                    break;
                }
            }

            if (spell == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(spell);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var clause in prog.ClauseList)
            {
                if (clause == null) continue;
                if (clause.Action != EffectActionKind.SpecialSummonNamed &&
                    clause.Action != EffectActionKind.SpecialSummonFromDeck &&
                    clause.Action != EffectActionKind.SpecialSummonFromHand)
                    continue;
                if (NamesThis(clause.NamedCard, def.name) ||
                    NamesThis(clause.AltNamedCard, def.name))
                    return true;
            }

            return false;
        }

        static bool NamesThis(string printed, string cardName) =>
            !string.IsNullOrEmpty(printed) && !string.IsNullOrEmpty(cardName) &&
            string.Equals(printed, cardName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Vocabulary view of a compiled program: shared kinds vs unique exceptions.
        /// </summary>
        public static string VocabularySummary(CardDef def)
        {
            if (def == null) return "none";
            if (OfficialCardAuthority.HasNoActivatableEffect(def))
                return "structural";
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.FullyCompiled && prog.ClauseList.Count == 0 &&
                HasLiveSummonPath(def))
                return "structural";
            if (prog == null || prog.ClauseList.Count == 0)
                return "unimplemented";
            var kinds = EffectVocabulary.KindsUsed(prog);
            var unique = 0;
            foreach (var c in prog.ClauseList)
                if (c != null && EffectVocabulary.ResolutionOf(c) ==
                    EffectResolutionKind.UniqueException)
                    unique++;
            var tag = unique > 0 ? "exception+" : "kinds:";
            return tag + string.Join(",", kinds);
        }

        public static bool MayIncludeInDeck(CardDef def)
        {
            if (def == null) return false;
            var s = Classify(def);
            if (!ExcludeUnimplementedFromDecks) return true;
            return s == CardEffectStatusKind.Structural
                || s == CardEffectStatusKind.Implemented;
        }

        /// <summary>Prefix for activation refusals (fail loud, never silent vanilla effect).</summary>
        public static string RefusalTag(CardDef def)
        {
            var s = Classify(def);
            return s switch
            {
                CardEffectStatusKind.Unimplemented => "[UNIMPLEMENTED]",
                CardEffectStatusKind.Stub => "[STUB]",
                _ => "[EFFECT]"
            };
        }

        public static void LogUnimplementedActivation(CardDef def, string detail)
        {
            var name = def?.name ?? "?";
            var id = def?.id ?? 0;
            Debug.LogWarning(
                $"[WRLDZ] {RefusalTag(def)} {name} ({id}) activation refused — {detail}");
        }
    }
}
