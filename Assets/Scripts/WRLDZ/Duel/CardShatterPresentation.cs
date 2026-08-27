using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Spec for how a card shatters when destroyed.
    /// Battle: piece count scales with overwhelm difference (ATK margin / damage),
    /// capped at <see cref="MaxOverwhelmDiff"/> (8000).
    /// Non-battle (effect, tribute-as-destroy, Raigeki, etc.): default shatter.
    /// </summary>
    public struct ShatterSpec
    {
        public bool IsBattle;
        /// <summary>0–8000 “how hard they lost” (damage or stat gap).</summary>
        public float OverwhelmDiff;
        public string CardName;

        public static ShatterSpec DefaultEffect(string name = null) => new()
        {
            IsBattle = false,
            OverwhelmDiff = 0f,
            CardName = name
        };

        public static ShatterSpec Battle(float overwhelmDiff, string name = null) => new()
        {
            IsBattle = true,
            OverwhelmDiff = Mathf.Clamp(overwhelmDiff, 0f, CardShatterPresentation.MaxOverwhelmDiff),
            CardName = name
        };
    }

    /// <summary>
    /// Queues per-card shatter specs from the rules engine; presentation consumes on visual purge.
    /// </summary>
    public static class CardShatterPresentation
    {
        /// <summary>Cap for overwhelm scaling (user canon: 8000 difference).</summary>
        public const float MaxOverwhelmDiff = 8000f;

        /// <summary>Pieces for Raigeki / Dark Hole / tribute-destroy / non-battle.</summary>
        public const int DefaultEffectPieces = 12;

        /// <summary>Barely destroyed by battle (equal-ish stats).</summary>
        public const int MinBattlePieces = 8;

        /// <summary>At full 8000 overwhelm — dense glass shatter.</summary>
        public const int MaxBattlePieces = 56;

        static readonly Dictionary<int, ShatterSpec> ByInstance = new();

        public static void Clear() => ByInstance.Clear();

        /// <summary>Queue battle shatter for a card about to leave the field.</summary>
        public static void QueueBattle(int instanceId, float overwhelmDiff, string cardName = null)
        {
            if (instanceId <= 0) return;
            ByInstance[instanceId] = ShatterSpec.Battle(overwhelmDiff, cardName);
        }

        /// <summary>Queue default effect shatter (or leave existing battle queue alone).</summary>
        public static void QueueEffect(int instanceId, string cardName = null)
        {
            if (instanceId <= 0) return;
            if (ByInstance.ContainsKey(instanceId)) return; // battle already set
            ByInstance[instanceId] = ShatterSpec.DefaultEffect(cardName);
        }

        /// <summary>Take and remove queued spec (default effect if none).</summary>
        public static ShatterSpec Take(int instanceId, string fallbackName = null)
        {
            if (TryTake(instanceId, out var s))
                return s;
            return ShatterSpec.DefaultEffect(fallbackName);
        }

        /// <summary>True if a shatter is queued (does not consume).</summary>
        public static bool HasQueued(int instanceId) =>
            instanceId > 0 && ByInstance.ContainsKey(instanceId);

        /// <summary>
        /// True only when the rules engine queued a destroy for this instance.
        /// Presentation should quiet-destroy visuals when false (refresh/rebuild), not fake-shatter.
        /// </summary>
        public static bool TryTake(int instanceId, out ShatterSpec spec)
        {
            if (instanceId > 0 && ByInstance.TryGetValue(instanceId, out spec))
            {
                ByInstance.Remove(instanceId);
                return true;
            }

            spec = default;
            return false;
        }

        /// <summary>Piece count from a spec.</summary>
        public static int PieceCount(in ShatterSpec spec)
        {
            if (!spec.IsBattle)
                return DefaultEffectPieces;

            var t = Mathf.Clamp01(spec.OverwhelmDiff / MaxOverwhelmDiff);
            // Ease-in so mid overkills jump, max at 8000
            t = t * t * (3f - 2f * t);
            return Mathf.RoundToInt(Mathf.Lerp(MinBattlePieces, MaxBattlePieces, t));
        }

        /// <summary>
        /// Battle overwhelm: prefer LP damage already applied; else ATK vs DEF/ATK gap.
        /// </summary>
        public static float ComputeBattleOverwhelm(
            CardInstance attacker,
            CardInstance defender,
            bool destroyDefender,
            bool destroyAttacker,
            int damageToDefendingPlayer,
            int damageToAttackingPlayer)
        {
            if (destroyDefender && damageToDefendingPlayer > 0)
                return Mathf.Min(MaxOverwhelmDiff, damageToDefendingPlayer);
            if (destroyAttacker && damageToAttackingPlayer > 0)
                return Mathf.Min(MaxOverwhelmDiff, damageToAttackingPlayer);

            if (attacker == null) return 0f;

            if (destroyDefender && defender != null)
            {
                var atk = BattleMechanics.AttackValue(attacker);
                var defStat = BattleMechanics.UsesDefenseStat(defender)
                    ? BattleMechanics.DefenseValue(defender)
                    : BattleMechanics.AttackValue(defender);
                return Mathf.Clamp(atk - defStat, 0, MaxOverwhelmDiff);
            }

            if (destroyAttacker && defender != null)
            {
                var a = BattleMechanics.AttackValue(attacker);
                var d = BattleMechanics.UsesDefenseStat(defender)
                    ? BattleMechanics.DefenseValue(defender)
                    : BattleMechanics.AttackValue(defender);
                // How hard the attacker was outmatched
                return Mathf.Clamp(d - a, 0, MaxOverwhelmDiff);
            }

            // Mutual destruction (equal ATK) — moderate shatter
            if (destroyAttacker && destroyDefender)
                return 1500f;

            return 500f;
        }

        /// <summary>Presentation hold so the shatter can play before AI continues.</summary>
        public static void HoldForShatter(int pieces)
        {
            // Base read + extra for denser shatters
            var extra = Mathf.Lerp(0f, 0.55f, Mathf.Clamp01((pieces - MinBattlePieces) /
                                                            (float)(MaxBattlePieces - MinBattlePieces)));
            DuelPresentationPacer.Hold(DuelPresentationPacer.ReadAfterCombatResult + extra,
                pieces >= MaxBattlePieces * 0.75f
                    ? "Overwhelming destruction…"
                    : "Card shattered…");
        }
    }
}
