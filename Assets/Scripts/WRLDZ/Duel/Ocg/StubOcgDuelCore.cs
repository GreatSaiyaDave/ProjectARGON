using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Replays a recorded tape. No passcode branches — payloads come from JSON.</summary>
    public sealed class StubOcgDuelCore : IOcgDuelCore
    {
        readonly List<OcgTapeBatch> _batches = new();
        int _batch;
        bool _waiting;
        readonly List<byte[]> _responses = new();

        public bool IsWaiting => _waiting;
        public int WaitingPlayer { get; private set; }
        public OcgDuelStatus Status { get; private set; } = OcgDuelStatus.Continue;
        public OcgTapeBatch CurrentBatch =>
            _batch >= 0 && _batch < _batches.Count ? _batches[_batch] : null;
        public IReadOnlyList<byte[]> RecordedResponses => _responses;

        public void LoadTapeJson(string json)
        {
            var file = OcgTapeCodec.Parse(json);
            _batches.Clear();
            if (file.batches != null)
                _batches.AddRange(file.batches);
            _batch = 0;
            _waiting = false;
            Status = OcgDuelStatus.Continue;
        }

        public void CreateDuel(uint seed, OcgDuelStartInfo info)
        {
            _waiting = false;
            _batch = 0;
            Status = _batches.Count == 0 ? OcgDuelStatus.End : OcgDuelStatus.Continue;
        }

        public IReadOnlyList<OcgMessage> Process()
        {
            if (_waiting)
                return Array.Empty<OcgMessage>();
            if (_batch >= _batches.Count)
            {
                Status = OcgDuelStatus.End;
                return Array.Empty<OcgMessage>();
            }

            var batch = _batches[_batch];
            var msgs = OcgTapeCodec.BuildMessages(batch);
            var wait = batch.wait ?? "";
            if (wait == "idle" || wait == "chain" || wait == "battle" || wait == "place" || wait == "card")
            {
                _waiting = true;
                WaitingPlayer = batch.waitingPlayer;
                Status = OcgDuelStatus.Awaiting;
            }
            else
            {
                _waiting = false;
                Status = OcgDuelStatus.Continue;
                _batch++;
                if (_batch >= _batches.Count)
                    Status = OcgDuelStatus.End;
            }
            return msgs;
        }

        public void SetResponse(byte[] buf)
        {
            _responses.Add(buf ?? Array.Empty<byte>());
            if (!_waiting) return;
            _waiting = false;
            Status = OcgDuelStatus.Continue;
            _batch++;
            if (_batch >= _batches.Count)
                Status = OcgDuelStatus.End;
        }

        public byte[] QueryLocation(int player, int location, int queryFlag) => Array.Empty<byte>();

        public void Dispose() { }

        public static StubOcgDuelCore FromStreamingTape(string relativePath)
        {
            var stub = new StubOcgDuelCore();
            var path = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
            if (System.IO.File.Exists(path))
                stub.LoadTapeJson(System.IO.File.ReadAllText(path));
            else
                Debug.LogWarning("[WRLDZ OCG] Missing tape " + path);
            return stub;
        }
    }
}
