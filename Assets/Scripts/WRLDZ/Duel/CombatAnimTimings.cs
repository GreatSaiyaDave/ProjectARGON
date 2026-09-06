using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Presentation timings for anime combat. Response windows are NOT menu pauses —
    /// they last until the action's visual "hit" frame while the animation plays out.
    ///
    /// When real 3D/AR clips exist, replace profile generation with clip length + impact event time.
    /// Until then, profiles approximate anime weight from level / ATK.
    /// </summary>
    public struct CombatAnimProfile
    {
        /// <summary>Full action length (charge + hit + recovery).</summary>
        public float TotalSeconds;

        /// <summary>
        /// Seconds from start until impact — player may react only before this.
        /// (~5s for a signature direct attack.)
        /// </summary>
        public float ImpactAtSeconds;

        /// <summary>Flavor line for log / UI ("charges a white blast…").</summary>
        public string MotionLine;

        public float ReactionWindow => Mathf.Max(0.5f, ImpactAtSeconds);
    }

    public enum CombatActionKind
    {
        None = 0,
        Attack,
        SummonAppear
    }

    /// <summary>In-flight presentation of an opponent (or player) action.</summary>
    public class ActiveCombatPresentation
    {
        public CombatActionKind Kind;
        public CardInstance SourceCard;
        public CardInstance TargetCard; // attack target; null = direct
        public DuelistState Actor;
        public CombatAnimProfile Profile;
        public float StartedUnscaledTime;
        public bool ImpactResolved;
        /// <summary>Whether the defender controlled a monster at attack declaration (replay rules).</summary>
        public bool HadMonstersAtAttackDeclaration;

        public float Elapsed => Time.unscaledTime - StartedUnscaledTime;
        public float SecondsToImpact => Mathf.Max(0f, Profile.ImpactAtSeconds - Elapsed);
        public float ImpactFraction =>
            Profile.ImpactAtSeconds <= 0.01f
                ? 1f
                : Mathf.Clamp01(Elapsed / Profile.ImpactAtSeconds);
        public bool PastImpact => Elapsed >= Profile.ImpactAtSeconds;
        public bool Finished => Elapsed >= Profile.TotalSeconds;
    }

    public static class CombatAnimTimings
    {
        /// <summary>
        /// Damage Calculation window — short but readable pause for hand QEs (Kuriboh).
        /// Official window is not a menu pause; this approximates reaction time on desktop/phone.
        /// </summary>
        public static CombatAnimProfile ForDamageCalculation() => new()
        {
            TotalSeconds = 7.2f,
            ImpactAtSeconds = 6.0f,
            MotionLine = "Damage calculation — activate Kuriboh now."
        };

        /// <summary>After LP damage — Numinous Healer / Attack and Receive family.</summary>
        public static CombatAnimProfile ForDamageTaken() => new()
        {
            TotalSeconds = 8.8f,
            ImpactAtSeconds = 8f,
            MotionLine = "You took damage — tap a blinking zone to activate."
        };

        /// <summary>
        /// Player-facing activate / response decision window. Letting this elapse is Pass.
        /// Independent of hologram / attack-impact cinematic length.
        /// </summary>
        public const float DefaultResponseSeconds = 5f;

        /// <summary>Baseline anime attack windup before impact (~5s for big monsters).</summary>
        public const float DefaultAttackImpact = 6.5f;

        public static CombatAnimProfile ForAttack(CardInstance attacker, bool direct)
        {
            if (attacker == null)
            {
                return new CombatAnimProfile
                {
                    ImpactAtSeconds = DefaultAttackImpact,
                    TotalSeconds = DefaultAttackImpact + 0.9f,
                    MotionLine = "attacks"
                };
            }

            // Weight: higher level / ATK = longer, heavier charge (Blue-Eyes feel)
            var level = Mathf.Max(1, attacker.Level);
            var atk = Mathf.Max(0, attacker.CurrentAtk);
            var impact = 5.4f
                         + Mathf.Clamp(level * 0.14f, 0f, 1.4f)
                         + (atk >= 3000 ? 0.9f : atk >= 2500 ? 0.55f : atk >= 1800 ? 0.25f : 0f);
            impact = Mathf.Clamp(impact, 5.0f, 8.0f);

            var motion = direct
                ? (atk >= 2500
                    ? $"rears back — a devastating direct assault with {attacker.Name}!"
                    : $"{attacker.Name} charges straight at your Life Points!")
                : $"{attacker.Name} lunges toward the enemy monster!";

            return new CombatAnimProfile
            {
                ImpactAtSeconds = impact,
                TotalSeconds = impact + 0.85f,
                MotionLine = motion
            };
        }

        /// <summary>
        /// Open-game-state window on the opponent's turn (Absolute End and same-shape traps).
        /// </summary>
        public static CombatAnimProfile ForOpenGameState() => new()
        {
            TotalSeconds = 8.8f,
            ImpactAtSeconds = 8f,
            MotionLine = "Open game state — tap a blinking zone to activate."
        };

        public static CombatAnimProfile ForSummon(CardInstance summoned)
        {
            var level = summoned != null ? Mathf.Max(1, summoned.Level) : 4;
            // Give Trap Hole / summon-response a real reaction window (was ~1.5–3.2s — too short).
            var impact = Mathf.Clamp(5.0f + level * 0.15f, 5.0f, 7.0f);
            var name = summoned?.Name ?? "A monster";
            return new CombatAnimProfile
            {
                ImpactAtSeconds = impact,
                TotalSeconds = impact + 0.6f,
                MotionLine = $"{name} materializes on the field… (Trap Hole window)"
            };
        }
    }
}
