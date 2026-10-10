using System;
using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Time as the engine sees it (R4). Rules never branch on it: it stamps presentation windows, the
    /// review log and snapshots. One per <see cref="DuelEngine"/> (<see cref="DuelEngine.Clock"/>), so a
    /// server can run every match on its own clock and a replay on a fixed one.
    /// </summary>
    public interface IClock
    {
        /// <summary>Seconds on a monotonic clock (Unity: <c>Time.unscaledTime</c>).</summary>
        float UnscaledTime { get; }

        /// <summary>Wall-clock UTC (review log stamps, snapshot <c>TimestampMs</c>).</summary>
        DateTime UtcNow { get; }
    }

    /// <summary>Diagnostic output of one engine (R4). Not the duel log: that is <see cref="DuelEngine.OnLog"/>.</summary>
    public interface IDuelLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    /// <summary>
    /// Structured consumer of one duel's events (R4): the review log is one; a server's per-seat event
    /// stream or match audit can be another (<see cref="DuelEngine.AddEventSink"/>). Bound on every
    /// <see cref="DuelEngine.StartDuel(WRLDZ.Data.CardDatabase, WRLDZ.Data.DeckFile, WRLDZ.Data.DeckFile)"/>;
    /// listens through <see cref="DuelEngine.OnLog"/>, <see cref="DuelEngine.OnStateChanged"/> and
    /// <see cref="DuelEngine.OnGameOver"/>. Must never draw from <see cref="DuelEngine.Rng"/>.
    /// </summary>
    public interface IDuelEventSink
    {
        void Bind(DuelEngine engine);
        void Unbind();
    }

    /// <summary>Unity default clock: <c>Time.unscaledTime</c> + <see cref="DateTime.UtcNow"/> (today's behaviour).</summary>
    public sealed class UnityDuelClock : IClock
    {
        public static readonly UnityDuelClock Instance = new UnityDuelClock();
        public float UnscaledTime => Time.unscaledTime;
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Unity default diagnostics: the Unity console.</summary>
    public sealed class UnityDuelLog : IDuelLog
    {
        public static readonly UnityDuelLog Instance = new UnityDuelLog();
        public void Info(string message) => Debug.Log(message);
        public void Warning(string message) => Debug.LogWarning(message);
        public void Error(string message) => Debug.LogError(message);
    }

    /// <summary>
    /// The match whose rules the CURRENT THREAD is running. Card-effect compilation is a process-wide
    /// cache reached from ~110 static call sites that do not carry the engine, so the engine publishes
    /// its <see cref="DuelMatchConfig"/> here: <see cref="DuelEngine.StartDuel(WRLDZ.Data.CardDatabase, WRLDZ.Data.DeckFile, WRLDZ.Data.DeckFile, bool)"/>
    /// sets it when the engine was given a config and game over clears it; <see cref="DuelCommandService"/>
    /// re-enters it per command. A host that interleaves several matches on one thread wraps each
    /// engine call in <see cref="Enter"/> (or <see cref="DuelEngine.EnterMatchScope"/>).
    /// Read by <c>CompiledEffectCache.ResolveEraId</c> (era) and its AI-compile gate.
    /// </summary>
    public static class DuelMatchContext
    {
        [ThreadStatic] static DuelMatchConfig _current;

        /// <summary>Config of the match on this thread, or null outside a configured duel.</summary>
        public static DuelMatchConfig Current => _current;

        /// <summary>Make <paramref name="config"/> current until the returned scope is disposed.</summary>
        public static Scope Enter(DuelMatchConfig config)
        {
            var prev = _current;
            _current = config;
            return new Scope(prev);
        }

        /// <summary>Replace the current match without a scope (engine start / game over, scene teardown).</summary>
        public static void Set(DuelMatchConfig config) => _current = config;

        public static void Clear() => _current = null;

        public readonly struct Scope : IDisposable
        {
            readonly DuelMatchConfig _previous;
            internal Scope(DuelMatchConfig previous) => _previous = previous;
            public void Dispose() => _current = _previous;
        }
    }
}
