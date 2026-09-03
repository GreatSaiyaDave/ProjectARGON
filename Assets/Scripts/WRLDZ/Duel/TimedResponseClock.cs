using System;
using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Reaction clock until the active action's deadline (attack impact or summon appear).
    /// Does not re-log the open prompt — <see cref="DuelEngine.OpenResponseWindow"/> owns that.
    /// </summary>
    public class TimedResponseClock : MonoBehaviour
    {
        public DuelEngine Engine { get; private set; }

        public bool IsRunning { get; private set; }
        public float SecondsRemaining { get; private set; }
        public float FractionToImpact { get; private set; }

        /// <summary>0 at windup start, 1 at impact (for UI bars).</summary>
        public float FractionRemaining => 1f - FractionToImpact;

        public event Action<float> OnTick; // seconds to impact
        public event Action OnExpired;    // deadline — too late
        public event Action OnStarted;
        public event Action OnStopped;

        bool _wasAwaiting;
        string _motionLine = "";

        public string MotionLine => _motionLine;

        public void Bind(DuelEngine engine, float unusedLegacyDefault = 5f)
        {
            Engine = engine;
            if (engine != null)
            {
                engine.OnStateChanged -= OnEngineState;
                engine.OnStateChanged += OnEngineState;
            }

            SyncFromEngine();
        }

        public void Unbind()
        {
            if (Engine != null)
                Engine.OnStateChanged -= OnEngineState;
            StopClock();
            Engine = null;
        }

        void OnDestroy() => Unbind();

        void OnEngineState() => SyncFromEngine();

        void SyncFromEngine()
        {
            bool awaitingNow;
            if (Ocg.OcgLabDuelHost.IsActive)
            {
                var host = Ocg.OcgLabDuelHost.Current;
                awaitingNow = host != null && host.IsAwaitingChain;
                if (awaitingNow && !_wasAwaiting)
                    StartOcgChainClock(host);
                else if (!awaitingNow && _wasAwaiting)
                    StopClock();
                _wasAwaiting = awaitingNow;
                return;
            }

            if (Engine == null) return;
            awaitingNow = Engine.IsAwaitingPlayerResponse;
            if (awaitingNow && !_wasAwaiting)
                StartClock();
            else if (!awaitingNow && _wasAwaiting)
                StopClock();
            _wasAwaiting = awaitingNow;
        }

        void StartOcgChainClock(Ocg.OcgLabDuelHost host)
        {
            IsRunning = true;
            SecondsRemaining = host != null ? host.ChainWindowSeconds : CombatAnimTimings.DefaultAttackImpact;
            FractionToImpact = 0f;
            _motionLine = "Chain — activate or Pass.";
            OnStarted?.Invoke();
            OnTick?.Invoke(SecondsRemaining);
        }

        void StartClock()
        {
            IsRunning = true;
            var pres = Engine?.ActivePresentation;
            var impact = Engine?.PendingResponse?.ReactionSeconds
                         ?? pres?.Profile.ImpactAtSeconds
                         ?? CombatAnimTimings.DefaultAttackImpact;
            SecondsRemaining = impact;
            FractionToImpact = 0f;
            _motionLine = Engine?.PendingResponse?.MotionLine
                          ?? pres?.Profile.MotionLine
                          ?? "Action in progress…";
            // No extra log — OpenResponseWindow already announced the window.
            OnStarted?.Invoke();
            OnTick?.Invoke(SecondsRemaining);
        }

        void StopClock()
        {
            IsRunning = false;
            SecondsRemaining = 0f;
            FractionToImpact = 0f;
            OnStopped?.Invoke();
        }

        void Update()
        {
            if (!IsRunning) return;

            if (Ocg.OcgLabDuelHost.IsActive)
            {
                var host = Ocg.OcgLabDuelHost.Current;
                if (host == null || !host.IsAwaitingChain)
                {
                    StopClock();
                    _wasAwaiting = false;
                    return;
                }
                var dur = Mathf.Max(0.5f, host.ChainWindowSeconds);
                var elapsed = Time.unscaledTime - host.ChainOpenedUnscaled;
                SecondsRemaining = Mathf.Max(0f, dur - elapsed);
                FractionToImpact = Mathf.Clamp01(elapsed / dur);
                OnTick?.Invoke(SecondsRemaining);
                if (SecondsRemaining <= 0.001f)
                {
                    IsRunning = false;
                    host.PassChainIfExpired();
                    OnExpired?.Invoke();
                    _wasAwaiting = false;
                }
                return;
            }

            if (Engine == null) return;

            if (!Engine.IsAwaitingPlayerResponse)
            {
                StopClock();
                _wasAwaiting = false;
                return;
            }

            var pres = Engine.ActivePresentation;
            if (pres != null && Engine.PendingResponse != null)
            {
                SecondsRemaining = pres.SecondsToImpact;
                FractionToImpact = pres.ImpactFraction;
            }
            else if (Engine.PendingResponse != null)
            {
                var open = Engine.PendingResponse.OpenedUnscaledTime;
                var dur = Mathf.Max(0.5f, Engine.PendingResponse.ReactionSeconds);
                var elapsed = Time.unscaledTime - open;
                SecondsRemaining = Mathf.Max(0f, dur - elapsed);
                FractionToImpact = Mathf.Clamp01(elapsed / dur);
            }

            OnTick?.Invoke(SecondsRemaining);

            if (Engine.IsAwaitingEffectTarget)
                return;

            if (SecondsRemaining <= 0.001f || (pres != null && pres.PastImpact))
            {
                IsRunning = false;
                // PassResponse logs a single clear line for attack vs summon.
                Engine.PassResponse();
                OnExpired?.Invoke();
                _wasAwaiting = false;
            }
        }
    }
}
