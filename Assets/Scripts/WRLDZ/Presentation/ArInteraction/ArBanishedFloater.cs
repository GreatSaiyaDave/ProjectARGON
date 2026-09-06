using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// M1-adjacent Banished/RFG button + world floater shell.
    /// Visible only when <see cref="Sync"/> count &gt; 0. Opens nothing by itself —
    /// Dilbot connects OnBanishedTapped → BanishedBrowser.Show.
    /// Placement: in front of the player, off the disk edge near M1
    /// (mirrors <see cref="ArOppFieldGlance.PlaceLeftOfM1"/> pattern).
    /// </summary>
    [DefaultExecutionOrder(811)]
    public class ArBanishedFloater : MonoBehaviour
    {
        const int CanvasW = 220;
        const int CanvasH = 72;
        public const float WorldWidth = 0.28f;

        public Transform Root { get; private set; }
        public ArDuelDiskRig PlayerDisk;
        public Transform ArenaRoot;
        public bool IsPlayerSide = true;

        int _layer;
        Canvas _canvas;
        CanvasGroup _cg;
        Text _label;
        BoxCollider _hit;
        int _count;

        public static ArBanishedFloater Create(Transform parent, int layer, bool playerSide = true)
        {
            var go = new GameObject("BanishedFloater");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var g = go.AddComponent<ArBanishedFloater>();
            g._layer = layer;
            g.IsPlayerSide = playerSide;
            g.Build();
            go.SetActive(false);
            return g;
        }

        void Build()
        {
            FreeUiKit.EnsureLoaded();
            Root = transform;

            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup));
            plate.transform.SetParent(transform, false);
            plate.layer = _layer;
            var rt = plate.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            var canvas = plate.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 89;
            canvas.worldCamera = ArStageView.FindCamera(transform);
            _canvas = canvas;
            _cg = plate.GetComponent<CanvasGroup>();
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 1f;

            _hit = plate.AddComponent<BoxCollider>();
            _hit.size = new Vector3(CanvasW, CanvasH, 24f);
            _hit.center = Vector3.zero;
            var marker = plate.AddComponent<ArBanishedHit>();
            marker.Floater = this;
            marker.IsPlayerSide = IsPlayerSide;

            var bg = plate.gameObject.AddComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = new Color(0.06f, 0.04f, 0.12f, 0.55f);
            bg.raycastTarget = false;
            var glass = ImagineAssets.HudIslandGlass();
            if (glass != null)
            {
                var frame = new GameObject("MatFrame", typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(plate.transform, false);
                frame.layer = _layer;
                var frt = frame.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero;
                frt.offsetMax = Vector2.zero;
                var fi = frame.GetComponent<Image>();
                fi.sprite = glass;
                fi.color = Color.white;
                fi.raycastTarget = false;
            }

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(plate.transform, false);
            labelGo.layer = _layer;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 6f);
            lrt.offsetMax = new Vector2(-8f, -6f);
            _label = labelGo.GetComponent<Text>();
            WrldzType.Style(_label, 18, display: true, heavyOutline: true);
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = new Color(0.78f, 0.62f, 1f, 1f);
            _label.raycastTarget = false;
            _label.text = "BANISHED";

            transform.localScale = Vector3.one * (WorldWidth / CanvasW);
        }

        /// <summary>Show button only when banished cards exist.</summary>
        public void Sync(int banishedCount)
        {
            _count = Mathf.Max(0, banishedCount);
            var show = _count > 0;
            if (gameObject.activeSelf != show)
                gameObject.SetActive(show);
            if (_label != null)
                _label.text = _count <= 0 ? "BANISHED" : $"BANISHED  {_count}";
            if (_cg != null)
                _cg.alpha = show ? 1f : 0f;
        }

        void LateUpdate()
        {
            if (!isActiveAndEnabled) return;
            if (_canvas != null)
            {
                var cam = ArStageView.FindCamera(transform);
                if (cam != null) _canvas.worldCamera = cam;
            }

            PlaceNearM1OffDisk();
        }

        /// <summary>
        /// In front of the player, off the disk edge by M1 (not overlapping opp glance).
        /// Opp glance sits left of M1; this sits forward toward the player.
        /// </summary>
        void PlaceNearM1OffDisk()
        {
            Vector3 pos;
            if (ArenaRoot != null)
            {
                var m1 = ArenaRoot.TransformPoint(ArPlaymatLayout.MonsterArenaLocal(0, IsPlayerSide));
                // Toward player (negative arena forward for player side) + slight left of M1.
                var towardPlayer = IsPlayerSide ? -ArenaRoot.forward : ArenaRoot.forward;
                var left = IsPlayerSide ? -ArenaRoot.right : ArenaRoot.right;
                pos = m1 + towardPlayer * 0.38f + left * 0.12f + Vector3.up * 0.28f;
            }
            else if (PlayerDisk != null && PlayerDisk.DiskRoot != null)
            {
                // Disk-local: past M1 tip, in front of plate edge.
                pos = PlayerDisk.DiskRoot.TransformPoint(new Vector3(-0.28f, 0.18f, 0.36f));
            }
            else
                return;

            transform.position = pos;
            var cam = ArStageView.FindCamera(transform);
            transform.rotation = ArStageView.UiFacing(pos, cam);
        }
    }
}
