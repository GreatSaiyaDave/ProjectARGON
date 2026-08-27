using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.UI;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Mr. Referobot — physical NPC referee + announcer present in every duel.
    /// Phone: portrait + speech bubble. AR later: world-anchored body mesh.
    /// </summary>
    public class MrReferobot : MonoBehaviour
    {
        Text _bubble;
        Text _nameplate;
        Image _portrait;
        Image _bubbleBg;
        float _bubbleT;
        string _lastLine;
        readonly Queue<string> _queue = new();
        DuelEngine _engine;

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
            _bubble.text = Openers[0];
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
            if (_engine == null) return;
            _engine.OnLog += OnLog;
            _engine.OnStateChanged += OnState;
            _engine.OnGameOver += OnGameOver;
            Say(Openers[UnityEngine.Random.Range(0, Openers.Length)]);
        }

        void OnDestroy()
        {
            if (_engine == null) return;
            _engine.OnLog -= OnLog;
            _engine.OnStateChanged -= OnState;
            _engine.OnGameOver -= OnGameOver;
        }

        void OnLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var l = line.ToLowerInvariant();
            if (l.Contains("illegal") || l.Contains("cannot") || l.Contains("failed"))
                Say(Illegal[UnityEngine.Random.Range(0, Illegal.Length)]);
            else if (l.Contains("battle phase"))
                Say(Battle[UnityEngine.Random.Range(0, Battle.Length)]);
            else if (l.Contains("response") || l.Contains("attack declared") || l.Contains("impact"))
                Say(TrapWindow[UnityEngine.Random.Range(0, TrapWindow.Length)]);
            else if (l.Contains("normal summon") || l.Contains("summoned"))
                Say("Monster on the field! Arena hologram online!");
            else if (l.Contains("set a") || l.Contains("set "))
                Say("Face-down set. Keep your bluff sharp.");
            else if (l.Contains("activate"))
                Say("Card activated! Effect resolving!");
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
            var youWin = _engine.Player != null && _engine.Player.LifePoints > 0 &&
                         (_engine.Opponent == null || _engine.Opponent.LifePoints <= 0);
            // Also deck-out etc. — StatusLine often enough; use LP as proxy
            if (_engine.Player != null && _engine.Opponent != null)
            {
                if (_engine.Player.LifePoints > 0 && _engine.Opponent.LifePoints <= 0)
                    Say(Win[UnityEngine.Random.Range(0, Win.Length)]);
                else if (_engine.Player.LifePoints <= 0)
                    Say(Lose[UnityEngine.Random.Range(0, Lose.Length)]);
                else
                    Say(youWin ? Win[0] : Lose[0]);
            }
        }

        public void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line == _lastLine) return;
            _queue.Enqueue(line);
        }

        public void SayIllegal() =>
            Say(Illegal[UnityEngine.Random.Range(0, Illegal.Length)]);

        void Update()
        {
            if (_queue.Count > 0 && _bubbleT <= 0.15f)
            {
                _lastLine = _queue.Dequeue();
                if (_bubble != null) _bubble.text = _lastLine;
                _bubbleT = 4.5f;
                if (_bubbleBg != null)
                    _bubbleBg.color = new Color(1f, 0.98f, 0.85f, 0.96f);
            }

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

        static void Stretch(RectTransform r, float padX = 0, float padY = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
