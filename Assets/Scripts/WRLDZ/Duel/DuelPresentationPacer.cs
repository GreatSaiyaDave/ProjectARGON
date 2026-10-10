using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Soft presentation holds so players can see flips / summons / damage before
    /// the AI continues. Does not pause rules resolution — only paces UI and AI steps.
    ///
    /// Spell/Trap activations are paced by <see cref="ActivationBeat"/>: <see cref="IsHolding"/> is the
    /// later of this pacer's own hold and the live beats' end, so a beat can only lengthen a hold.
    /// Result holds (<see cref="HoldCombatResult"/>, <see cref="HoldDraw"/>, <see cref="Extend"/> …) run
    /// from the beat's result gate — when the result is actually shown — not from the engine frame.
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
        /// Spell/Trap activate hologram (your card): the whole default beat — disk turn, rise, read,
        /// result, read, fade (<see cref="SpellActivationPresentation.PlayerBeatSeconds"/> ≈ 6.6 s).
        /// Was 5.5 s, which ended while the hologram still hovered.
        /// </summary>
        public const float ReadAfterSpellActivate = SpellActivationPresentation.PlayerBeatSeconds;

        /// <summary>
        /// Opponent Spell/Trap (Raigeki, etc.): the whole default opponent beat
        /// (<see cref="SpellActivationPresentation.OpponentBeatSeconds"/> ≈ 8.6 s).
        /// </summary>
        public const float ReadAfterOpponentSpellActivate = SpellActivationPresentation.OpponentBeatSeconds;

        /// <summary>Hold after battle damage / destruction lines.</summary>
        public const float ReadAfterCombatResult = 4.0f;

        /// <summary>Default AI step gap when nothing else is holding.</summary>
        public const float DefaultAiStep = 3.6f;

        static float _holdUntilUnscaled;
        static string _reason = "";

        static float BeatHoldUntil => ActivationBeat.HoldUntilUnscaled;

        static float HoldUntilUnscaled => Mathf.Max(_holdUntilUnscaled, BeatHoldUntil);

        /// <summary>True while a read / flip hold or a Spell/Trap beat is active.</summary>
        public static bool IsHolding => Time.unscaledTime < HoldUntilUnscaled;

        public static float SecondsRemaining =>
            Mathf.Max(0f, HoldUntilUnscaled - Time.unscaledTime);

        public static string Reason
        {
            get
            {
                if (!IsHolding) return "";
                if (BeatHoldUntil > _holdUntilUnscaled)
                {
                    var beat = ActivationBeat.HoldReason();
                    if (!string.IsNullOrEmpty(beat)) return beat;
                }

                return _reason;
            }
        }

        /// <summary>Extend the hold to at least <paramref name="seconds"/> from now.</summary>
        public static void Hold(float seconds, string reason = null)
        {
            if (seconds <= 0f) return;
            HoldUntil(Time.unscaledTime + seconds * PaceScale, reason);
        }

        /// <summary>Extend the hold to at least the absolute unscaled time <paramref name="until"/>.</summary>
        public static void HoldUntil(float until, string reason = null)
        {
            if (until > _holdUntilUnscaled)
            {
                _holdUntilUnscaled = until;
                if (!string.IsNullOrEmpty(reason))
                    _reason = reason;
            }
        }

        /// <summary>
        /// A result's read time, counted from when the result is actually presented: while a Spell/Trap
        /// beat still hides its results, that is the beat's result gate, not the engine frame.
        /// </summary>
        public static void HoldAfterResult(float seconds, string reason = null)
        {
            if (seconds <= 0f) return;
            var from = Mathf.Max(Time.unscaledTime, ActivationBeat.PendingResultGate);
            HoldUntil(from + seconds * PaceScale, reason);
        }

        /// <summary>
        /// Add time on top of an in-flight hold (Raigeki hologram, then monsters shatter).
        /// Starts a new hold if none is active. During a Spell/Trap beat the extra time runs from the
        /// beat's result gate instead (the beat already covers the hologram).
        /// </summary>
        public static void Extend(float seconds, string reason = null)
        {
            if (seconds <= 0f) return;
            if (ActivationBeat.AnyResultPending)
            {
                HoldAfterResult(seconds, reason);
                return;
            }

            var extra = seconds * PaceScale;
            if (!IsHolding)
            {
                Hold(seconds, reason);
                return;
            }

            _holdUntilUnscaled = Mathf.Max(_holdUntilUnscaled, HoldUntilUnscaled) + extra;
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
        /// Arena battle flip: toaster 0.70 + mid hold 0.12 + face beat 0.12 + settle as the Defense
        /// plate 0.147 + braced rise 0.45.
        /// Must stay in lockstep with <c>ArDiskMotion.BattleFlipArenaTotal</c>.
        /// </summary>
        public const float BattleFlipMotion = 1.54f;

        /// <summary>Face-down monster attacked: flip reveal, no ATK turn on the disk.</summary>
        public static void HoldBattleFlip(string cardName = null)
        {
            HoldAfterResult(BattleFlipMotion + ReadAfterFlip * 0.40f,
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
            HoldAfterResult(1.55f + n * 0.55f,
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

        /// <summary>What one activation's hold replaced, so a cancelled activation can give it back.</summary>
        struct SpellHold
        {
            public float PrevUntil;
            public string PrevReason;
            public float Until;
        }

        static readonly Dictionary<int, SpellHold> SpellHolds = new();

        /// <summary>Spell/Trap Solid Vision generation on the field.</summary>
        /// <param name="instanceId">
        /// The activating card; lets <see cref="ReleaseSpellActivateHold"/> undo this hold if the
        /// activation is cancelled. 0 = not releasable.
        /// </param>
        public static void HoldSpellActivate(string cardName = null, bool opponentCard = false,
            int instanceId = 0)
        {
            // The live ActivationBeat (opened by RegisterActivation just before this call) is the
            // authority; the constant is only the fallback when no beat exists.
            var seconds = opponentCard ? ReadAfterOpponentSpellActivate : ReadAfterSpellActivate;
            var beatEnd = ActivationBeat.HoldUntilUnscaled;
            if (beatEnd > Time.unscaledTime)
                seconds = Mathf.Min(seconds, beatEnd - Time.unscaledTime);
            var prevUntil = _holdUntilUnscaled;
            var prevReason = _reason;
            if (opponentCard)
            {
                Hold(seconds,
                    string.IsNullOrEmpty(cardName)
                        ? "Opponent activates a Spell/Trap — watch the field…"
                        : $"Opponent activates {cardName} — watch the field…");
            }
            else
            {
                Hold(seconds,
                    string.IsNullOrEmpty(cardName)
                        ? "Spell activates — hologram rising…"
                        : $"{cardName} — hologram rising…");
            }

            if (instanceId == 0) return;
            PruneSpellHolds();
            if (_holdUntilUnscaled > prevUntil)
                SpellHolds[instanceId] = new SpellHold
                {
                    PrevUntil = prevUntil,
                    PrevReason = prevReason,
                    Until = _holdUntilUnscaled
                };
        }

        /// <summary>
        /// A Spell/Trap activation was cancelled before it resolved (target pick cancelled, response
        /// undone): give back the read time <see cref="HoldSpellActivate"/> added for it, so the AI and
        /// the player's queued advance do not wait on a hologram that never plays. Holds added after it
        /// (a later activation, a battle result) are kept. Returns true when the hold was shortened.
        /// </summary>
        public static bool ReleaseSpellActivateHold(int instanceId)
        {
            if (instanceId == 0 || !SpellHolds.TryGetValue(instanceId, out var h)) return false;
            SpellHolds.Remove(instanceId);
            // Something extended the hold after this activation: that later hold still stands.
            if (!Mathf.Approximately(_holdUntilUnscaled, h.Until)) return false;
            _holdUntilUnscaled = h.PrevUntil;
            _reason = h.PrevReason ?? "";
            return true;
        }

        static void PruneSpellHolds()
        {
            if (SpellHolds.Count < 16) return;
            var now = Time.unscaledTime;
            var stale = new List<int>();
            foreach (var kv in SpellHolds)
                if (kv.Value.Until <= now)
                    stale.Add(kv.Key);
            foreach (var id in stale)
                SpellHolds.Remove(id);
        }

        public static void HoldCombatResult()
        {
            HoldAfterResult(ReadAfterCombatResult, "Battle result…");
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
            SpellHolds.Clear();
        }
    }
}
