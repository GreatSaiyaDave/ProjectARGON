using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Soft presentation holds so players can see flips / summons / damage before
    /// the AI continues. Does not pause rules resolution — only paces UI and AI steps.
    /// </summary>
    public static class DuelPresentationPacer
    {
        /// <summary>
        /// Arena readability scale. 1 = original; &gt;1 holds longer so LP, traps,
        /// and holograms can be followed.
        /// </summary>
        public const float PaceScale = 1f;

        /// <summary>UI / AR card flip spin duration.</summary>
        public const float FlipDuration = 0.85f;

        /// <summary>Extra time after a flip so the face can be read.</summary>
        public const float ReadAfterFlip = 2.6f;

        /// <summary>Brief hold after a face-up summon or set activation.</summary>
        public const float ReadAfterSummonOrActivate = 3.6f;

        /// <summary>S/T toaster insert (present → mouth → pocket).</summary>
        public const float ReadAfterSlotInsert = 3.5f;

        /// <summary>
        /// Spell/Trap activate hologram: face-down → rise → face opponent.
        /// Long enough for the arena sequence to read before AI continues.
        /// </summary>
        public const float ReadAfterSpellActivate = 5.5f;

        /// <summary>
        /// Opponent Spell/Trap (Raigeki, etc.): hold long enough to read the card
        /// and watch the field change before the next AI action.
        /// </summary>
        public const float ReadAfterOpponentSpellActivate = 8.0f;

        /// <summary>Hold after battle damage / destruction lines.</summary>
        public const float ReadAfterCombatResult = 4.0f;

        /// <summary>Default AI step gap when nothing else is holding.</summary>
        public const float DefaultAiStep = 3.6f;

        static float _holdUntilUnscaled;
        static string _reason = "";

        /// <summary>True while a read / flip hold is active.</summary>
        public static bool IsHolding => Time.unscaledTime < _holdUntilUnscaled;

        public static float SecondsRemaining =>
            Mathf.Max(0f, _holdUntilUnscaled - Time.unscaledTime);

        public static string Reason => IsHolding ? _reason : "";

        /// <summary>Extend the hold to at least <paramref name="seconds"/> from now.</summary>
        public static void Hold(float seconds, string reason = null)
        {
            if (seconds <= 0f) return;
            var until = Time.unscaledTime + seconds * PaceScale;
            if (until > _holdUntilUnscaled)
            {
                _holdUntilUnscaled = until;
                if (!string.IsNullOrEmpty(reason))
                    _reason = reason;
            }
        }

        /// <summary>
        /// Add time on top of an in-flight hold (Raigeki hologram, then monsters shatter).
        /// Starts a new hold if none is active.
        /// </summary>
        public static void Extend(float seconds, string reason = null)
        {
            if (seconds <= 0f) return;
            var extra = seconds * PaceScale;
            if (!IsHolding)
            {
                Hold(seconds, reason);
                return;
            }

            _holdUntilUnscaled += extra;
            if (!string.IsNullOrEmpty(reason))
                _reason = reason;
        }

        /// <summary>
        /// Lift 0.22 + toaster flip 0.70 + mid hold 0.12 + face beat 0.12 + turn 0.42.
        /// Must stay in lockstep with <c>ArDiskMotion.FlipSummonTotal</c>.
        /// </summary>
        public const float FlipSummonMotion = 1.58f;

        /// <summary>Flip Summon: lift + reveal + turn to ATK + a beat to read the face.</summary>
        public static void HoldFlipSummon(string cardName = null)
        {
            Hold(FlipSummonMotion + ReadAfterFlip * 0.55f,
                string.IsNullOrEmpty(cardName)
                    ? "Flip Summon…"
                    : $"Flip Summon {cardName}…");
        }

        /// <summary>
        /// Arena battle flip: toaster 0.70 + mid hold 0.12 + stand-up 0.42.
        /// Must stay in lockstep with <c>ArDiskMotion.BattleFlipArenaTotal</c>.
        /// </summary>
        public const float BattleFlipMotion = 1.24f;

        /// <summary>Face-down monster attacked: flip reveal, no ATK turn on the disk.</summary>
        public static void HoldBattleFlip(string cardName = null)
        {
            Hold(BattleFlipMotion + ReadAfterFlip * 0.40f,
                string.IsNullOrEmpty(cardName)
                    ? "Flipped face-up…"
                    : $"{cardName} flipped face-up…");
        }

        /// <summary>Flip animation + reading time (face-down → face-up).</summary>
        public static void HoldCardFlip(string cardName = null)
        {
            Hold(FlipDuration + ReadAfterFlip,
                string.IsNullOrEmpty(cardName)
                    ? "Card revealed — read the face…"
                    : $"{cardName} revealed — read the face…");
        }

        public static void HoldSummonOrActivate(string cardName = null)
        {
            Hold(ReadAfterSummonOrActivate,
                string.IsNullOrEmpty(cardName)
                    ? "Resolving…"
                    : $"{cardName}…");
        }

        /// <summary>Cards leaving the disk deck well into the hand.</summary>
        public static void HoldDraw(int count = 1)
        {
            var n = Mathf.Max(1, count);
            Hold(1.55f + n * 0.55f,
                n == 1 ? "Drawing a card…" : $"Drawing {n} cards…");
        }

        /// <summary>Physical card sliding into a Spell/Trap slot.</summary>
        public static void HoldSlotInsert(string cardName = null)
        {
            Hold(ReadAfterSlotInsert,
                string.IsNullOrEmpty(cardName)
                    ? "Card sliding into the slot…"
                    : $"{cardName} sliding into the slot…");
        }

        /// <summary>Spell/Trap Solid Vision generation on the field.</summary>
        public static void HoldSpellActivate(string cardName = null, bool opponentCard = false)
        {
            var seconds = opponentCard ? ReadAfterOpponentSpellActivate : ReadAfterSpellActivate;
            if (opponentCard)
            {
                Hold(seconds,
                    string.IsNullOrEmpty(cardName)
                        ? "Opponent activates a Spell/Trap — watch the field…"
                        : $"Opponent activates {cardName} — watch the field…");
                return;
            }

            Hold(seconds,
                string.IsNullOrEmpty(cardName)
                    ? "Spell activates — hologram rising…"
                    : $"{cardName} — hologram rising…");
        }

        public static void HoldCombatResult()
        {
            Hold(ReadAfterCombatResult, "Battle result…");
        }

        public static void HoldOpponentSummon(string cardName = null, bool asSet = false)
        {
            if (asSet)
            {
                Hold(ReadAfterSummonOrActivate, "Opponent Sets a monster…");
                return;
            }

            Hold(ReadAfterSummonOrActivate,
                string.IsNullOrEmpty(cardName)
                    ? "Opponent Summons a monster…"
                    : $"Opponent Summons {cardName}…");
        }

        public static void HoldOpponentSetSpellTrap()
        {
            Hold(ReadAfterSlotInsert, "Opponent Sets a Spell/Trap…");
        }

        public static void Clear()
        {
            _holdUntilUnscaled = 0f;
            _reason = "";
        }
    }
}
