using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// The shipped compiled-effects seed is what the game runs, so it must say what the compiler says (v207).
    ///
    /// On 2026-10-18 the v205 seed had gone stale: EffectActionKind / EffectZoneFilter members had been inserted
    /// mid-enum and atoms changed without a Version bump, and every --export-seed wrote the old same-Version entries
    /// back unchanged. Magical Labyrinth's seeded Special Summon then decoded as a coin toss + damage clause, and Wall
    /// Shadow, Harpie Lady Sisters and Spirit Message "A"/"I"/"L"/"N" fell from Structural to Unimplemented (their
    /// summon-path check reads the summoning card's seeded program). Fixed by SeedEnumLayout (a file written under
    /// another enum layout is skipped), a fresh compile in --export-seed, and this check.
    ///
    /// Runs only when the seed is the one in use (same Version): a scratch tree has no seed, and mid-batch the
    /// Version is already bumped above the seed's.
    /// </summary>
    public static class SeedFreshnessRegressionTests
    {
        const int WallShadow = 63162310;
        const int HarpieLadySisters = 12206212;
        const int SpiritMessageA = 94772232;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("SeedFreshness: " + n, ok, d ?? "");

            SummonPathPins(db, Check);

            if (!StreamingFiles.TryReadAllText(CompiledEffectCache.SeedRelative, out var json)) return;
            var seed = JsonUtility.FromJson<SeedFile>(json);
            if (seed?.programs == null || seed.version != CardTextEffectCompiler.Version) return;

            Check("seed enum layout matches the code",
                SeedEnumLayout.Compatible(seed.enumLayout, out var why), why);

            var stale = new List<string>();
            var compared = 0;
            foreach (var p in seed.programs)
            {
                if (p == null || p.CompilerVersion != CardTextEffectCompiler.Version ||
                    !string.IsNullOrEmpty(p.EraId))
                    continue;
                var def = db.Get(p.CardId);
                if (def == null || p.TextHash != OfficialCardAuthority.TextHash(def)) continue;
                compared++;
                if (!Same(p, CardTextEffectCompiler.Compile(def)))
                    stale.Add($"{def.name} ({def.id})");
            }

            Check($"every seeded program equals a fresh compile ({compared} compared)", stale.Count == 0,
                stale.Count == 0
                    ? null
                    : $"{stale.Count} stale, e.g. {string.Join("; ", stale.GetRange(0, Math.Min(5, stale.Count)))}. " +
                      "Bump CardTextEffectCompiler.Version above every number used, then headless.sh . --export-seed.");
        }

        /// <summary>The three summon paths the stale seed broke, read through the cache like the game does.</summary>
        static void SummonPathPins(CardDatabase db, Action<string, bool, string> Check)
        {
            void Structural(string name, int id)
            {
                var def = db.Get(id);
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check($"{name}: Structural (its summoning card's program names it)",
                    st == CardEffectStatusKind.Structural, $"status={st}");
            }

            Structural("Wall Shadow (Magical Labyrinth)", WallShadow);
            Structural("Harpie Lady Sisters (Elegant Egotist)", HarpieLadySisters);
            Structural("Spirit Message \"A\" (Destiny Board)", SpiritMessageA);
        }

        static bool Same(CompiledCardProgram seeded, CompiledCardProgram fresh)
        {
            if (fresh == null || seeded.FullyCompiled != fresh.FullyCompiled) return false;
            return JsonUtility.ToJson(new Shape { Clauses = seeded.Clauses, Unparsed = seeded.UnparsedFragments }) ==
                   JsonUtility.ToJson(new Shape { Clauses = fresh.Clauses, Unparsed = fresh.UnparsedFragments });
        }

        [Serializable]
        class SeedFile
        {
            public int version;
            public string[] enumLayout;
            public CompiledCardProgram[] programs;
        }

        [Serializable]
        class Shape
        {
            public EffectClause[] Clauses;
            public string[] Unparsed;
        }
    }
}
