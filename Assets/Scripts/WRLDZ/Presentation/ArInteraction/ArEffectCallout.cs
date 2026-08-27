using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Unobtrusive effect-text readout on arena holos.
    /// Opponent monsters: dot + leader line to a glass box beside/above the art.
    /// One-shot Spells/Traps: glass box in front of (below) the card, art left clear.
    /// </summary>
    [DefaultExecutionOrder(800)]
    public class ArEffectCallout : MonoBehaviour
    {
        const float CanvasWorld = 0.00085f;
        const int CanvasW = 200;
        const int CanvasH = 44;
        public const float BoxWorldWidth = CanvasW * CanvasWorld;
        const float NameHold = 2.6f;

        ArArenaCardVisual _host;
        Transform _dot;
        Transform _box;
        LineRenderer _line;
        Text _body;
        bool _leader;
        Camera _cam;
        int _shownId = int.MinValue;
        float _hideAt;

        public static ArEffectCallout Ensure(ArArenaCardVisual host)
        {
            if (host == null) return null;
            var existing = host.GetComponentInChildren<ArEffectCallout>(true);
            if (existing != null)
            {
                existing._host = host;
                existing.RefreshText();
                return existing;
            }

            var go = new GameObject("EffectCallout");
            go.transform.SetParent(host.transform, false);
            go.layer = host.gameObject.layer;
            var c = go.AddComponent<ArEffectCallout>();
            c._host = host;
            c.Build();
            c.RefreshText();
            return c;
        }

        void Build()
        {
            var layer = gameObject.layer;
            var accent = _host != null && _host.PlayerSide
                ? new Color(0.25f, 0.92f, 1f, 0.95f)
                : new Color(1f, 0.55f, 0.7f, 0.95f);

            var dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dotGo.name = "Dot";
            dotGo.transform.SetParent(transform, false);
            dotGo.layer = layer;
            ArObjectUtil.Destroy(dotGo.GetComponent<Collider>());
            dotGo.transform.localScale = Vector3.one * 0.010f;
            var dotMr = dotGo.GetComponent<MeshRenderer>();
            dotMr.sharedMaterial = ArFieldMaterials.Get(accent);
            dotMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dotMr.receiveShadows = false;
            _dot = dotGo.transform;

            var boxGo = new GameObject("TextBox", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            boxGo.transform.SetParent(transform, false);
            boxGo.layer = layer;
            var rt = boxGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            boxGo.transform.localScale = Vector3.one * CanvasWorld;
            var canvas = boxGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 85;
            canvas.worldCamera = ArStageView.FindCamera(transform);
            _box = boxGo.transform;

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(boxGo.transform, false);
            bgGo.layer = layer;
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bg = bgGo.GetComponent<Image>();
            var glass = ImagineAssets.HudCalloutGlass();
            bg.sprite = glass != null ? glass : UiFoundation.WhiteSprite();
            bg.color = glass != null ? Color.white : new Color(0.03f, 0.05f, 0.08f, 0.22f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Body", typeof(RectTransform), typeof(Text), typeof(Outline));
            textGo.transform.SetParent(boxGo.transform, false);
            textGo.layer = layer;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6f, 4f);
            textRt.offsetMax = new Vector2(-6f, -4f);
            _body = textGo.GetComponent<Text>();
            _body.font = UiFoundation.BuiltinFont();
            _body.fontSize = 13;
            _body.fontStyle = FontStyle.Bold;
            _body.alignment = TextAnchor.MiddleCenter;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;
            _body.color = DuelystUi.TextCream;
            _body.raycastTarget = false;
            var outline = textGo.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.1f, -1.1f);

            var lineGo = new GameObject("Leader");
            lineGo.transform.SetParent(transform, false);
            lineGo.layer = layer;
            _line = lineGo.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.widthMultiplier = 0.004f;
            _line.numCapVertices = 3;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sharedMaterial = ArFieldMaterials.GetTransparent(accent);
            _line.startColor = accent;
            _line.endColor = new Color(accent.r, accent.g, accent.b, 0.35f);
        }

        public void RefreshText()
        {
            if (_body == null || _host == null) return;
            var def = _host.Card?.Def;
            if (def == null)
            {
                gameObject.SetActive(false);
                return;
            }

            var name = string.IsNullOrEmpty(def.name) ? "" : def.name;
            _body.text = name;
        }

        void LateUpdate()
        {
            if (_host == null)
            {
                gameObject.SetActive(false);
                return;
            }

            if (!ShouldShow())
            {
                _shownId = int.MinValue;
                SetVisible(false);
                return;
            }

            var id = _host.Card.CardId;
            if (id != _shownId)
            {
                _shownId = id;
                _hideAt = Time.unscaledTime + NameHold;
            }

            if (Time.unscaledTime > _hideAt)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _leader = _host.IsMonster;
            Layout();
            UpdateLine();
            BillboardBox();
        }

        bool ShouldShow()
        {
            if (_host.Card?.Def == null) return false;
            if (!_host.FaceUp) return false;
            if (_host.IsFadingOut) return false;
            if (_host.PlayerSide) return false;
            if (_host.transform.localScale.x < ArPlaymatLayout.LiveHoloScale * 0.45f) return false;
            return !string.IsNullOrWhiteSpace(_host.Card.Def.name);
        }

        void Layout()
        {
            if (_dot == null || _box == null) return;
            var boxHalf = CanvasW * CanvasWorld * 0.5f;
            if (_leader)
            {
                var lift = CardArtFocus.MonsterArtLift;
                var half = CardArtFocus.MonsterArtworkScale.x * 0.5f;
                var side = _host != null && !_host.PlayerSide ? -1f : 1f;
                _dot.localPosition = new Vector3(side * half * 0.92f, lift + 0.08f, 0.06f);
                // Fully off the standing art, slightly toward the camera (+Z after billboard).
                _box.localPosition = new Vector3(
                    side * (half + boxHalf + 0.16f),
                    lift + 0.12f,
                    0.22f);
            }
            else
            {
                var h = CardArtFocus.SpellTrapArtworkScale.y;
                _dot.localPosition = new Vector3(0f, -h * 0.08f, 0.06f);
                _box.localPosition = new Vector3(0f, -(h * 0.5f + boxHalf + 0.10f), 0.18f);
            }
        }

        void UpdateLine()
        {
            if (_line == null || _dot == null || _box == null) return;
            _line.SetPosition(0, _dot.position);
            _line.SetPosition(1, _box.position);
        }

        void BillboardBox()
        {
            if (_box == null) return;
            _cam = ArStageView.FindCamera(transform);
            _box.rotation = ArStageView.UiFacing(_box.position, _cam);
        }

        void SetVisible(bool on)
        {
            if (_dot != null && _dot.gameObject.activeSelf != on)
                _dot.gameObject.SetActive(on);
            if (_box != null && _box.gameObject.activeSelf != on)
                _box.gameObject.SetActive(on);
            if (_line != null)
                _line.enabled = on;
        }
    }
}
