using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Categories for duel review events (AI training / post-duel analysis).
    /// </summary>
    public enum DuelLogKind
    {
        Info = 0,
        Phase,
        Draw,
        Summon,
        Set,
        Activate,
        Attack,
        Response,
        Damage,
        Effect,
        Ai,
        Illegal,
        Rules,
        Outcome,
        Board,
        Voice
    }

    /// <summary>One structured duel event — human-readable and JSONL-exportable.</summary>
    [Serializable]
    public class DuelLogEvent
    {
        public int seq;
        public string utc;
        public float duelTime;
        public int turn;
        public string phase;
        public string actor;
        public string kind;
        public string message;
        public int lpYou;
        public int lpOpp;
        public int handYou;
        public int handOpp;
        public int monYou;
        public int monOpp;
        public string cardId;
        public string cardName;
        public string extra;
    }

    /// <summary>
    /// In-duel review log: captures every engine line + board snapshots, keeps a
    /// visible ring buffer, and exports JSONL for offline AI review/learning.
    /// </summary>
    public class DuelReviewLog
    {
        public const int DefaultCapacity = 400;
        public const int VisibleLinesDefault = 14;

        readonly List<DuelLogEvent> _events = new();
        readonly List<string> _visible = new();
        DuelEngine _engine;
        float _startedUnscaled;
        int _seq;
        string _sessionId;
        string _lastBoardFingerprint;
        int _eventsSinceBoard;

        public IReadOnlyList<DuelLogEvent> Events => _events;
        public IReadOnlyList<string> VisibleLines => _visible;
        public string SessionId => _sessionId;
        public string LastExportPath { get; private set; }
        public int Count => _events.Count;
        public event Action OnChanged;

        public void Bind(DuelEngine engine)
        {
            Unbind();
            _engine = engine;
            if (_engine == null) return;
            _startedUnscaled = Time.unscaledTime;
            _sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" +
                         UnityEngine.Random.Range(1000, 9999);
            _seq = 0;
            _events.Clear();
            _visible.Clear();
            _lastBoardFingerprint = null;
            _eventsSinceBoard = 0;
            LastExportPath = null;

            _engine.OnLog += OnEngineLog;
            _engine.OnStateChanged += OnEngineState;
            _engine.OnGameOver += OnEngineGameOver;

            Record(DuelLogKind.Info, "System",
                $"Duel review session {_sessionId} started (visible log + JSONL export).");
        }

        public void Unbind()
        {
            if (_engine == null) return;
            _engine.OnLog -= OnEngineLog;
            _engine.OnStateChanged -= OnEngineState;
            _engine.OnGameOver -= OnEngineGameOver;
            _engine = null;
        }

        void OnEngineLog(string msg) => Ingest(msg);

        void OnEngineState()
        {
            // Periodic board snapshots so AI can correlate moves with board shape
            _eventsSinceBoard++;
            if (_eventsSinceBoard < 4) return;
            TryRecordBoard(force: false);
        }

        void OnEngineGameOver()
        {
            TryRecordBoard(force: true);
            var winner = _engine?.Winner;
            var outcome = winner == null
                ? "Draw / no winner"
                : winner.IsPlayer
                    ? "OUTCOME: YOU WIN"
                    : "OUTCOME: AI WINS";
            Record(DuelLogKind.Outcome, "System", outcome);
            LastExportPath = ExportToDisk();
            if (!string.IsNullOrEmpty(LastExportPath))
                Record(DuelLogKind.Info, "System", "Review export → " + LastExportPath);
        }

        public void Ingest(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var msg = raw.Trim();
            if (IsNoise(msg)) return;
            var kind = Classify(msg);
            var actor = InferActor(msg, kind);
            Record(kind, actor, msg);
        }

        /// <summary>
        /// Drop leftover fluff that should not appear in review / visible log
        /// (cinematic doubles, meta pipeline talk, empty process lines).
        /// </summary>
        public static bool IsNoise(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return true;
            // Cinematic presentation leftovers
            if (msg.IndexOf("materializes", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("lunges toward", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("charges straight", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("COMBAT —", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("IMPACT —", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("hit lands", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("React before IMPACT", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Animation does not pause", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Meta / pipeline
            if (msg.IndexOf("learned text", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("official text applied via registry", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (msg.IndexOf("Policy loaded=", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf(">>> ", StringComparison.Ordinal) >= 0) return true;
            if (msg.IndexOf("takes their turn", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Your turn again", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Opponent turn starting", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Empty process
            if (msg.IndexOf("has no more Main Phase actions", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (msg.IndexOf("has no more attackers", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("finds no beneficial attack target", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (msg.IndexOf("no more Main Phase", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Micro phase steps (covered by single Battle Phase line)
            if (msg.IndexOf("Battle Phase — Start Step", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Battle Phase — Battle Step", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Battle Phase — End Step", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Standby Phase", StringComparison.OrdinalIgnoreCase) >= 0 &&
                msg.IndexOf("[", StringComparison.Ordinal) >= 0) return true;
            if (msg.IndexOf("Draw Phase", StringComparison.OrdinalIgnoreCase) >= 0 &&
                msg.IndexOf("skipped", StringComparison.OrdinalIgnoreCase) < 0 &&
                !msg.StartsWith("════", StringComparison.Ordinal))
            {
                // Drop bare "[Name] Draw Phase" — draw line is enough
                if (msg.IndexOf("draws", StringComparison.OrdinalIgnoreCase) < 0)
                    return true;
            }

            return false;
        }

        public void Record(DuelLogKind kind, string actor, string message,
            string cardId = null, string cardName = null, string extra = null)
        {
            var e = Capture(kind, actor, message, cardId, cardName, extra);
            _events.Add(e);
            while (_events.Count > DefaultCapacity)
                _events.RemoveAt(0);

            var line = FormatVisibleLine(e);
            _visible.Add(line);
            while (_visible.Count > DefaultCapacity)
                _visible.RemoveAt(0);

            OnChanged?.Invoke();
        }

        DuelLogEvent Capture(DuelLogKind kind, string actor, string message,
            string cardId, string cardName, string extra)
        {
            var eng = _engine;
            return new DuelLogEvent
            {
                seq = ++_seq,
                utc = DateTime.UtcNow.ToString("o"),
                duelTime = Time.unscaledTime - _startedUnscaled,
                turn = eng?.TurnNumber ?? 0,
                phase = eng?.Phase.ToString() ?? "?",
                actor = actor ?? "System",
                kind = kind.ToString(),
                message = message ?? "",
                lpYou = eng?.Player?.LifePoints ?? 0,
                lpOpp = eng?.Opponent?.LifePoints ?? 0,
                handYou = eng?.Player?.HandCount ?? 0,
                handOpp = eng?.Opponent?.HandCount ?? 0,
                monYou = eng?.Player?.MonsterCount ?? 0,
                monOpp = eng?.Opponent?.MonsterCount ?? 0,
                cardId = cardId ?? "",
                cardName = cardName ?? "",
                extra = extra ?? ""
            };
        }

        void TryRecordBoard(bool force)
        {
            if (_engine?.Player == null || _engine.Opponent == null) return;
            var fp = BuildBoardFingerprint();
            if (!force && fp == _lastBoardFingerprint) return;
            _lastBoardFingerprint = fp;
            _eventsSinceBoard = 0;
            Record(DuelLogKind.Board, "System", "BOARD  " + CompactBoardLine(), extra: fp);
        }

        string CompactBoardLine()
        {
            var p = _engine.Player;
            var o = _engine.Opponent;
            return
                $"T{_engine.TurnNumber} {_engine.Phase} | " +
                $"You LP{p.LifePoints} H{p.HandCount} M[{MonsterNames(p)}] ST{p.SpellTrapCount()} | " +
                $"AI LP{o.LifePoints} H{o.HandCount} M[{MonsterNames(o)}] ST{o.SpellTrapCount()}";
        }

        string BuildBoardFingerprint()
        {
            var sb = new StringBuilder(128);
            sb.Append(_engine.TurnNumber).Append('|').Append(_engine.Phase).Append('|');
            sb.Append(_engine.Player.LifePoints).Append('/').Append(_engine.Opponent.LifePoints).Append('|');
            foreach (var m in _engine.Player.MonstersOnField())
                sb.Append('P').Append(m.CardId).Append(m.FaceUp ? 'U' : 'D').Append(m.Position == BattlePosition.Attack ? 'A' : 'F');
            foreach (var m in _engine.Opponent.MonstersOnField())
                sb.Append('O').Append(m.CardId).Append(m.FaceUp ? 'U' : 'D').Append(m.Position == BattlePosition.Attack ? 'A' : 'F');
            sb.Append('|').Append(_engine.Player.HandCount).Append('/').Append(_engine.Opponent.HandCount);
            return sb.ToString();
        }

        static string MonsterNames(DuelistState who)
        {
            if (who == null) return "";
            var parts = new List<string>();
            foreach (var m in who.MonstersOnField())
            {
                if (m == null) continue;
                if (!m.FaceUp)
                    parts.Add("SET");
                else
                    parts.Add($"{m.Name}({m.CurrentAtk}/{(m.Position == BattlePosition.Attack ? "ATK" : "DEF")})");
            }

            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }

        public static string FormatVisibleLine(DuelLogEvent e)
        {
            if (e == null) return "";
            var tag = KindTag(e.kind);
            var t = e.turn > 0 ? $"T{e.turn}" : "T-";
            var actor = string.IsNullOrEmpty(e.actor) ? "?" : e.actor;
            // Keep one line tight for UI
            var msg = e.message ?? "";
            if (msg.Length > 140) msg = msg.Substring(0, 137) + "…";
            return $"{tag} {t} {e.phase} [{actor}] {msg}";
        }

        static string KindTag(string kind) => kind switch
        {
            nameof(DuelLogKind.Phase) => "PHASE",
            nameof(DuelLogKind.Draw) => "DRAW ",
            nameof(DuelLogKind.Summon) => "SUMMN",
            nameof(DuelLogKind.Set) => "SET  ",
            nameof(DuelLogKind.Activate) => "ACT  ",
            nameof(DuelLogKind.Attack) => "ATK  ",
            nameof(DuelLogKind.Response) => "REACT",
            nameof(DuelLogKind.Damage) => "DMG  ",
            nameof(DuelLogKind.Effect) => "FX   ",
            nameof(DuelLogKind.Ai) => "AI   ",
            nameof(DuelLogKind.Illegal) => "DENY ",
            nameof(DuelLogKind.Rules) => "RULE ",
            nameof(DuelLogKind.Outcome) => "END  ",
            nameof(DuelLogKind.Board) => "BOARD",
            nameof(DuelLogKind.Voice) => "VOICE",
            _ => "INFO "
        };

        public static DuelLogKind Classify(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return DuelLogKind.Info;
            var m = msg;

            if (m.IndexOf("illegal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Activation illegal", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Illegal;

            if (m.StartsWith("════", StringComparison.Ordinal) ||
                m.IndexOf("Main Phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Draw Phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Standby Phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Battle Phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("End Phase", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Phase;

            if (m.IndexOf("draws", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Draw;

            if (m.IndexOf("Normal Summon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Tribute Summon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Flip Summon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Special Summon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("materializes", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Summon;

            if (m.IndexOf("Set ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Sets ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("face-down", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Set;

            if (m.IndexOf("Activate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("activates", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Activate;

            if (m.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("IMPACT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("charges", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("lunges", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Attack;

            if (m.IndexOf("REACT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("response", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Trap Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Mirror Force", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("passes", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Response;

            if (m.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("💥", StringComparison.Ordinal) >= 0 ||
                m.IndexOf(" LP", StringComparison.Ordinal) >= 0 &&
                m.IndexOf("now at", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Damage;

            if (m.IndexOf("[AI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf(">>> ", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("takes their turn", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Ai;

            if (m.IndexOf("YOU WIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("YOU LOSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("★", StringComparison.Ordinal) >= 0)
                return DuelLogKind.Outcome;

            if (m.IndexOf("🎙", StringComparison.Ordinal) >= 0)
                return DuelLogKind.Voice;

            if (m.IndexOf("destroys", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("learned text", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Effect;

            if (m.IndexOf("Rule", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("TCG", StringComparison.OrdinalIgnoreCase) >= 0)
                return DuelLogKind.Rules;

            return DuelLogKind.Info;
        }

        public static string InferActor(string msg, DuelLogKind kind)
        {
            if (string.IsNullOrEmpty(msg)) return "System";
            if (msg.IndexOf("Opponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("[AI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf(">>> ", StringComparison.Ordinal) >= 0)
                return "AI";
            if (msg.IndexOf("You ", StringComparison.Ordinal) >= 0 ||
                msg.IndexOf("YOUR", StringComparison.Ordinal) >= 0 ||
                msg.IndexOf("[You]", StringComparison.OrdinalIgnoreCase) >= 0)
                return "You";
            if (kind == DuelLogKind.Ai) return "AI";
            if (kind == DuelLogKind.Board || kind == DuelLogKind.Rules || kind == DuelLogKind.Info)
                return "System";
            return "Duel";
        }

        /// <summary>Last N lines for on-screen panel (newest at bottom).</summary>
        public string FormatVisibleBlock(int lastN = VisibleLinesDefault)
        {
            if (_visible.Count == 0) return "(duel log empty — actions will appear here for AI review)";
            var start = Mathf.Max(0, _visible.Count - Mathf.Max(1, lastN));
            var sb = new StringBuilder();
            for (var i = start; i < _visible.Count; i++)
            {
                if (i > start) sb.Append('\n');
                sb.Append(_visible[i]);
            }

            return sb.ToString();
        }

        /// <summary>Full human transcript for clipboard / file.</summary>
        public string FormatTranscript()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# WRLDZ Duel Review  session={_sessionId}");
            sb.AppendLine($"# events={_events.Count}  exportedUtc={DateTime.UtcNow:o}");
            sb.AppendLine("# columns: SEQ | TURN | PHASE | ACTOR | KIND | LP_YOU/LP_AI | MESSAGE");
            foreach (var e in _events)
            {
                sb.Append(e.seq).Append(" | T").Append(e.turn).Append(" | ").Append(e.phase)
                    .Append(" | ").Append(e.actor).Append(" | ").Append(e.kind)
                    .Append(" | ").Append(e.lpYou).Append('/').Append(e.lpOpp)
                    .Append(" | ").Append(e.message).AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>JSONL — one event per line (best for AI fine-tune / analysis).</summary>
        public string ToJsonl()
        {
            var sb = new StringBuilder(_events.Count * 180);
            foreach (var e in _events)
                sb.AppendLine(JsonUtility.ToJson(e));
            return sb.ToString();
        }

        /// <summary>
        /// Write JSONL + plain transcript under persistentDataPath/WRLDZ/duel_reviews/.
        /// Returns primary JSONL path.
        /// </summary>
        public string ExportToDisk()
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "WRLDZ", "duel_reviews");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var baseName = "duel_" + (_sessionId ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                var jsonlPath = Path.Combine(dir, baseName + ".jsonl");
                var txtPath = Path.Combine(dir, baseName + ".txt");
                File.WriteAllText(jsonlPath, ToJsonl(), Encoding.UTF8);
                File.WriteAllText(txtPath, FormatTranscript(), Encoding.UTF8);
                LastExportPath = jsonlPath;
                Debug.Log("[WRLDZ ReviewLog] Exported " + jsonlPath);
                return jsonlPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ ReviewLog] Export failed: " + ex.Message);
                return null;
            }
        }
    }

    // DuelistState.SpellTrapCount helper — avoid missing API
    static class DuelistStateReviewExt
    {
        public static int SpellTrapCount(this DuelistState who)
        {
            if (who == null) return 0;
            var n = 0;
            foreach (var _ in who.SpellTrapsOnField()) n++;
            return n;
        }
    }
}
