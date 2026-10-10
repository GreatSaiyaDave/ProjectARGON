using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Mr. Referobot — physical NPC referee + announcer present in every duel.
    /// Phone: portrait + speech bubble. The duel street uses ReferobotWorldBody.
    /// </summary>
    public class MrReferobot : MonoBehaviour
    {
        Text _bubble;
        Text _nameplate;
        Image _portrait;
        Image _bubbleBg;
        float _bubbleT;
        string _lastLine;

        /// <summary>Last spoken line, including the world body above his head.</summary>
        public static event System.Action<string> LineSpoken;
        /// <summary>He called an illegal play (the world body raises a stop hand).</summary>
        public static event System.Action IllegalCalled;
        readonly Queue<string> _queue = new();
        DuelEngine _engine;

        /// <summary>Most lines waiting behind the current one; older commentary is dropped, not played late.</summary>
        const int MaxQueued = 2;
        /// <summary>A line stays up this long (seconds); with a backlog the next one may cut in after
        /// <see cref="BacklogMinShow"/>.</summary>
        const float LineShowSeconds = 4.5f;
        const float BacklogMinShow = 2.2f;

        // Spell/Trap name call-outs ride the ActivationBeat clock (announced once each, at the art reveal).
        readonly HashSet<int> _announcedBeats = new();
        static readonly List<ActivationBeat.Beat> BeatScratch = new();
        /// <summary>Unscaled time of an "activate" log line still waiting to see whether a Spell/Trap beat
        /// names it; -1 = none.</summary>
        float _activateLogAt = -1f;
        /// <summary>Runs the beat polling + the game-over wait even while this chip is hidden (Salvage).</summary>
        ReferobotBeatWatcher _watcher;

        static readonly string[] Openers =
        {
            "Mr. Referobot online! Legal play only — make it dramatic!",
            "Arena sealed. Disks linked. IT'S TIME TO DUEL!",
            "I am the referee of this Shadow Duel. Play fair. Play loud.",
        };

        static readonly string[] Illegal =
        {
            "Illegal! That move is not allowed right now!",
            "Denied! Check the rules — and try a legal declaration!",
            "Whistle! Not a legal activation. Pass or pick another play!",
        };

        static readonly string[] Battle =
        {
            "Battle Phase! Declare your attacks!",
            "Monsters ready — choose your target!",
            "Combat window open. React before IMPACT!",
        };

        static readonly string[] TrapWindow =
        {
            "Response window! Trap now or eat the blow!",
            "The attack is charging — Speeds 2 only until IMPACT!",
            "Mirror Force? Negate Attack? Speak now!",
        };

        static readonly string[] Win =
        {
            "Victory! LP zero — duel over!",
            "Winner confirmed! The arena stands down.",
        };

        static readonly string[] Lose =
        {
            "Defeat recorded. Retrain and return, Spirit Dueler.",
            "LP zero on your side. Next time — trap earlier!",
        };

        public static MrReferobot CreateInUi(Transform parent, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("MrReferobot", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var refb = go.AddComponent<MrReferobot>();
            refb.Build(go.transform);
            return refb;
        }

        /// <summary>Small duel chip: portrait plus the current call. Does not block the stage.</summary>
        public static MrReferobot CreateCallout(Transform parent)
        {
            var go = new GameObject("MrReferobot", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.012f, 0.748f);
            rt.anchorMax = new Vector2(0.328f, 0.838f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var refb = go.AddComponent<MrReferobot>();
            refb.BuildCallout(go.transform);
            return refb;
        }

        void Build(Transform root)
        {
            // Soft GO-style plate
            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(root, false);
            Stretch(plate.GetComponent<RectTransform>());
            var pimg = plate.GetComponent<Image>();
            pimg.sprite = ImagineAssets.HudChip() ?? StreamingSprite.GoOrbMenu() ?? UiFoundation.WhiteSprite();
            pimg.type = pimg.sprite != null && pimg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            pimg.color = Color.white;
            pimg.raycastTarget = false;

            // Portrait
            var port = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            port.transform.SetParent(root, false);
            var prt = port.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.02f, 0.08f);
            prt.anchorMax = new Vector2(0.22f, 0.92f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _portrait = port.GetComponent<Image>();
            _portrait.sprite = StreamingSprite.ReferobotPortrait() ?? UiFoundation.WhiteSprite();
            _portrait.preserveAspect = true;
            _portrait.color = Color.white;
            _portrait.raycastTarget = false;

            // Name
            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameGo.transform.SetParent(root, false);
            var nrt = nameGo.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0.24f, 0.62f);
            nrt.anchorMax = new Vector2(0.98f, 0.95f);
            nrt.offsetMin = Vector2.zero;
            nrt.offsetMax = Vector2.zero;
            _nameplate = nameGo.GetComponent<Text>();
            WrldzType.Style(_nameplate, 16, display: true);
            _nameplate.text = "MR. REFEROBOT  ·  REFEREE";
            _nameplate.color = WrldzTheme.GoldHot;
            _nameplate.alignment = TextAnchor.MiddleLeft;
            _nameplate.raycastTarget = false;

            // Speech bubble
            var bub = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
            bub.transform.SetParent(root, false);
            var brt = bub.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.24f, 0.08f);
            brt.anchorMax = new Vector2(0.98f, 0.60f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            _bubbleBg = bub.GetComponent<Image>();
            var bubble = ImagineAssets.DialogueBubble() ?? DuelystUi.DialogueBubble();
            if (bubble != null)
            {
                _bubbleBg.sprite = bubble;
                _bubbleBg.type = bubble.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                _bubbleBg.color = Color.white;
            }
            else
            {
                _bubbleBg.sprite = UiFoundation.WhiteSprite();
                _bubbleBg.color = new Color(0.95f, 0.96f, 1f, 0.94f);
            }
            _bubbleBg.raycastTarget = false;

            var btxt = new GameObject("Line", typeof(RectTransform), typeof(Text));
            btxt.transform.SetParent(bub.transform, false);
            Stretch(btxt.GetComponent<RectTransform>(), 6, 4);
            _bubble = btxt.GetComponent<Text>();
            WrldzType.Style(_bubble, 14, display: false);
            // Imagine dialogue bubble is dark glass → cream text; legacy white plate → dark ink
            _bubble.color = bubble != null
                ? DuelystUi.TextCream
                : new Color(0.1f, 0.12f, 0.2f, 1f);
            _bubble.alignment = TextAnchor.MiddleLeft;
            _bubble.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bubble.verticalOverflow = VerticalWrapMode.Truncate;
            _bubble.raycastTarget = false;
            _bubble.text = "";
        }

        void BuildCallout(Transform root)
        {
            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(root, false);
            Stretch(plate.GetComponent<RectTransform>());
            var pimg = plate.GetComponent<Image>();
            pimg.sprite = UiFoundation.WhiteSprite();
            pimg.color = new Color(0.04f, 0.06f, 0.10f, 0.78f);
            pimg.raycastTarget = false;

            var port = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            port.transform.SetParent(root, false);
            var prt = port.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.02f, 0.08f);
            prt.anchorMax = new Vector2(0.22f, 0.92f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _portrait = port.GetComponent<Image>();
            _portrait.sprite = StreamingSprite.ReferobotPortrait() ?? UiFoundation.WhiteSprite();
            _portrait.preserveAspect = true;
            _portrait.color = Color.white;
            _portrait.raycastTarget = false;

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameGo.transform.SetParent(root, false);
            var nrt = nameGo.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0.24f, 0.58f);
            nrt.anchorMax = new Vector2(0.98f, 0.94f);
            nrt.offsetMin = Vector2.zero;
            nrt.offsetMax = Vector2.zero;
            _nameplate = nameGo.GetComponent<Text>();
            WrldzType.Style(_nameplate, 13, display: true);
            _nameplate.text = "MR. REFEROBOT";
            _nameplate.color = WrldzTheme.GoldHot;
            _nameplate.alignment = TextAnchor.MiddleLeft;
            _nameplate.raycastTarget = false;

            var btxt = new GameObject("Line", typeof(RectTransform), typeof(Text));
            btxt.transform.SetParent(root, false);
            var brt = btxt.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.24f, 0.06f);
            brt.anchorMax = new Vector2(0.98f, 0.58f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            _bubble = btxt.GetComponent<Text>();
            WrldzType.Style(_bubble, 12, display: false);
            _bubble.color = DuelystUi.TextCream;
            _bubble.alignment = TextAnchor.UpperLeft;
            _bubble.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bubble.verticalOverflow = VerticalWrapMode.Truncate;
            _bubble.raycastTarget = false;
            _bubble.text = "";
        }

        public void Bind(DuelEngine engine)
        {
            if (_engine != null)
            {
                _engine.OnLog -= OnLog;
                _engine.OnStateChanged -= OnState;
                _engine.OnGameOver -= OnGameOver;
            }

            _engine = engine;
            _announcedBeats.Clear();
            _activateLogAt = -1f;
            if (_watcher != null) _watcher.CancelGameOver();
            if (_engine == null) return;
            _engine.OnLog += OnLog;
            _engine.OnStateChanged += OnState;
            _engine.OnGameOver += OnGameOver;
            EnsureWatcher();
            Say(Openers[UnityEngine.Random.Range(0, Openers.Length)]);
        }

        void OnDestroy()
        {
            if (_watcher != null) Destroy(_watcher.gameObject);
            _watcher = null;
            if (_engine == null) return;
            _engine.OnLog -= OnLog;
            _engine.OnStateChanged -= OnState;
            _engine.OnGameOver -= OnGameOver;
        }

        void EnsureWatcher()
        {
            if (_watcher != null || !Application.isPlaying) return;
            var go = new GameObject("MrReferobotBeatWatcher");
            _watcher = go.AddComponent<ReferobotBeatWatcher>();
            _watcher.Owner = this;
        }

        void OnLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.Contains("TURN (game turn #"))
            {
                // Chair-umpire call: whose move it is, not the raw engine banner.
                Say(TurnCall(_engine));
                return;
            }

            var l = line.ToLowerInvariant();
            // Rule notes on legal plays also say "cannot" ("Traps/Quick-Play cannot activate the turn they are
            // Set", "cannot attack a monster" this turn) — those are not illegal moves.
            var ruleNote = l.Contains("cannot activate the turn") || l.Contains("cannot attack a monster");
            if (l.Contains("illegal") || l.Contains("failed") || (l.Contains("cannot") && !ruleNote))
                SayIllegal();
            else if (l.Contains("battle phase"))
                Say(Battle[UnityEngine.Random.Range(0, Battle.Length)]);
            else if (l.Contains("response") || l.Contains("attack declared") || l.Contains("impact"))
                Say(TrapWindow[UnityEngine.Random.Range(0, TrapWindow.Length)]);
            else if (l.Contains("normal summon") || l.Contains("summoned"))
                Say("Monster on the field! Arena hologram online!");
            else if (l.Contains("set a") || l.Contains("set ") || l.Contains("sets "))
                Say("Face-down set. Keep your bluff sharp."); // "Opponent Sets a …" too (its pop-up is gone)
            else if (l.Contains("activate"))
            {
                // A Spell/Trap activation is called out BY NAME when its art is revealed
                // (PollActivationCallouts). Only an activation with no Spell/Trap beat (a monster effect)
                // gets the generic line, a moment later once that is known.
                if (_activateLogAt < 0f) _activateLogAt = Time.unscaledTime;
            }
            else if (l.Contains("direct attack"))
                Say("Direct attack declared — defend or take it!");
            else if (line.Length > 8 && line.Length < 90 && !line.StartsWith("["))
            {
                // Echo short dramatic engine lines
                if (UnityEngine.Random.value < 0.35f)
                    Say(line);
            }
        }

        void OnState()
        {
            if (_engine == null) return;
            if (_engine.IsAwaitingPlayerResponse)
                Say(TrapWindow[UnityEngine.Random.Range(0, TrapWindow.Length)]);
        }

        void OnGameOver()
        {
            if (_engine == null) return;
            var line = GameOverLine(_engine);
            if (string.IsNullOrEmpty(line)) return;
            // The engine ends the duel in the frame the lethal card resolves, before its reveal / result beat has
            // played. Hold the verdict for the same wait as DuelUI.GameOverAfterBeatCo (ActivationBeat.
            // BlocksAdvance, hard-capped), so "Victory!" never lands before the card that won it is shown.
            EnsureWatcher();
            if (_watcher != null && Application.isPlaying)
                _watcher.SayAfterBeat(_engine, line);
            else
                SayNow(line);
        }

        internal static string TurnCall(DuelEngine engine)
        {
            var who = engine?.TurnPlayer;
            if (who == null) return null;
            if (who.IsPlayer) return $"Turn {engine.TurnNumber} — your move!";
            var name = string.IsNullOrEmpty(who.Name) ? "Opponent" : who.Name;
            return $"Turn {engine.TurnNumber} — {name} to play!";
        }

        static string GameOverLine(DuelEngine engine)
        {
            if (engine.Player == null || engine.Opponent == null) return null;
            var youWin = engine.Player.LifePoints > 0 && engine.Opponent.LifePoints <= 0;
            if (youWin)
                return Win[UnityEngine.Random.Range(0, Win.Length)];
            if (engine.Player.LifePoints <= 0)
                return Lose[UnityEngine.Random.Range(0, Lose.Length)];
            // Deck-out etc.: the engine's winner decides.
            return engine.Winner != null && engine.Winner.IsPlayer ? Win[0] : Lose[0];
        }

        /// <summary>
        /// Name each Spell/Trap activation once, when its art becomes readable on the arena ("Trap card, Mirror
        /// Force!"), in beat order. Without an arena visual (2D) the call comes at the estimated reveal instead.
        /// Also resolves a pending generic "activate" log line that turned out to have no Spell/Trap beat.
        /// </summary>
        internal void PollActivationCallouts()
        {
            if (_engine == null) return;
            var now = Time.unscaledTime;
            ActivationBeat.CollectLive(BeatScratch);
            ActivationBeat.Beat next = null;
            for (var i = 0; i < BeatScratch.Count; i++)
            {
                var b = BeatScratch[i];
                if (b == null || _announcedBeats.Contains(b.Serial)) continue;
                var fallbackAt = Mathf.Max(b.RegisteredAt, ActivationBeat.RevealStartFloor(b.InstanceId)) +
                                 SpellActivationPresentation.ArtReadableDelaySeconds;
                if (b.ArtRevealedAt < 0f && now < fallbackAt) continue;
                if (next == null || b.Serial < next.Serial) next = b;
            }

            var anyBeat = BeatScratch.Count > 0;
            BeatScratch.Clear();
            if (next != null)
            {
                _announcedBeats.Add(next.Serial);
                _activateLogAt = -1f;
                SayNow(NameCall(next));
                return;
            }

            if (_activateLogAt >= 0f && now - _activateLogAt > 0.35f)
            {
                _activateLogAt = -1f;
                if (!anyBeat) Say("Effect activated! Resolving!");
            }
        }

        static string NameCall(ActivationBeat.Beat b)
        {
            var kind = b.IsTrap ? "Trap card" : "Spell card";
            return string.IsNullOrEmpty(b.CardName) ? kind + " activated!" : kind + ", " + b.CardName + "!";
        }

        /// <summary>Show <paramref name="line"/> right away (timed call-outs): pending commentary is dropped.</summary>
        public void SayNow(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _queue.Clear();
            ShowLine(line);
        }

        public void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line == _lastLine) return;
            _queue.Enqueue(line);
            while (_queue.Count > MaxQueued) _queue.Dequeue();
            // Optical salvage hides the chip, so the street caption has to hear the line now.
            if (!isActiveAndEnabled)
                ShowLine(_queue.Dequeue());
        }

        public void SayIllegal()
        {
            Say(Illegal[UnityEngine.Random.Range(0, Illegal.Length)]);
            IllegalCalled?.Invoke();
        }

        void Update()
        {
            // Next line after the current one has been up its full time, or sooner with a backlog.
            if (_queue.Count > 0 &&
                (_bubbleT <= 0.15f || (_queue.Count > 1 && _bubbleT <= LineShowSeconds - BacklogMinShow)))
                ShowLine(_queue.Dequeue());

            if (_bubbleT > 0f)
            {
                _bubbleT -= Time.deltaTime;
                if (_bubbleT < 0.5f && _bubbleBg != null)
                    _bubbleBg.color = Color.Lerp(
                        new Color(0.95f, 0.96f, 1f, 0.94f),
                        new Color(1f, 0.98f, 0.85f, 0.96f),
                        _bubbleT * 2f);
            }
        }

        void ShowLine(string line)
        {
            _lastLine = line;
            if (_bubble != null) _bubble.text = line;
            LineSpoken?.Invoke(line);
            _bubbleT = LineShowSeconds;
            if (_bubbleBg != null)
                _bubbleBg.color = new Color(1f, 0.98f, 0.85f, 0.96f);
        }

        static void Stretch(RectTransform r, float padX = 0, float padY = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }
    }

    /// <summary>
    /// Always-active helper for <see cref="MrReferobot"/> (the chip itself may be hidden, e.g. optical Salvage):
    /// polls Spell/Trap name call-outs and holds the game-over verdict until the lethal beat has played.
    /// </summary>
    public sealed class ReferobotBeatWatcher : MonoBehaviour
    {
        internal MrReferobot Owner;
        Coroutine _gameOverCo;

        void Update()
        {
            if (Owner == null)
            {
                Destroy(gameObject);
                return;
            }

            Owner.PollActivationCallouts();
        }

        internal void CancelGameOver()
        {
            if (_gameOverCo != null) StopCoroutine(_gameOverCo);
            _gameOverCo = null;
        }

        internal void SayAfterBeat(DuelEngine engine, string line)
        {
            CancelGameOver();
            _gameOverCo = StartCoroutine(SayAfterBeatCo(engine, line));
        }

        System.Collections.IEnumerator SayAfterBeatCo(DuelEngine engine, string line)
        {
            // Same wait as DuelUI.GameOverAfterBeatCo.
            var cap = Time.unscaledTime + SpellActivationPresentation.BeatHardCapSeconds;
            while (ActivationBeat.BlocksAdvance(out _, out _) && Time.unscaledTime < cap)
                yield return null;
            _gameOverCo = null;
            if (Owner == null || engine == null || !engine.GameOver) yield break;
            // Let the lethal card's own name call land first if it is still pending.
            Owner.PollActivationCallouts();
            Owner.SayNow(line);
        }
    }
}
