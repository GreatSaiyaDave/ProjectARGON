using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// BATTLE / MAIN 2 / END as floating glass chips on the blade interior,
    /// lined up with the wrist hub and deck magazine. Taps go through
    /// <see cref="ArCardDragSystem"/> (AR RawImage ray), not a GraphicRaycaster.
    /// </summary>
    public class ArDiskPhaseButtons : MonoBehaviour
    {
        const int CanvasW = 256;
        const int CanvasH = 80;

        GameObject[] _chips;
        Text[] _labels;
        MeshRenderer[] _faces;
        int _layer;
        bool _deployed;

        public Transform Root => transform;

        public static ArDiskPhaseButtons Create(Transform parent, int layer)
        {
            var go = new GameObject("DiskPhaseRail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = layer;
            var rail = go.AddComponent<ArDiskPhaseButtons>();
            rail._layer = layer;
            rail.Build();
            rail.SetDeployed(false);
            return rail;
        }

        void Build()
        {
            _chips = new GameObject[3];
            _labels = new Text[3];
            _faces = new MeshRenderer[3];
            BuildChip(0, ArDiskPhaseKind.Battle, "BATTLE",
                new Color(0.92f, 0.78f, 0.32f, 0.92f));
            BuildChip(1, ArDiskPhaseKind.Main2, "MAIN 2",
                new Color(0.35f, 0.86f, 0.95f, 0.92f));
            BuildChip(2, ArDiskPhaseKind.End, "END",
                new Color(0.92f, 0.38f, 0.42f, 0.92f));
        }

        void BuildChip(int index, ArDiskPhaseKind kind, string title, Color accent)
        {
            var size = ArPlaymatLayout.DiskPhaseChipSize;
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = ArPlaymatLayout.DiskPhaseRailLocal(index);
            go.transform.localRotation = Quaternion.Euler(-90f, ArPlaymatLayout.DiskPhaseRailYawDeg(index), 0f);
            go.layer = _layer;

            var hit = go.AddComponent<ArDiskPhaseHit>();
            hit.Kind = kind;

            var colGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            colGo.name = "Hit";
            colGo.transform.SetParent(go.transform, false);
            colGo.transform.localPosition = Vector3.zero;
            colGo.transform.localRotation = Quaternion.identity;
            colGo.transform.localScale = new Vector3(size.x * 1.08f, size.y * 1.15f, 0.008f);
            colGo.layer = _layer;
            ArObjectUtil.Destroy(colGo.GetComponent<MeshRenderer>());
            var box = colGo.GetComponent<BoxCollider>();
            box.isTrigger = true;

            var faceMat = ArAnimePresentation.MakeSolid(new Color(0.04f, 0.07f, 0.10f, 0.72f),
                "PhaseChipFace");
            var rimMat = ArAnimePresentation.MakeSolid(accent, "PhaseChipRim");
            _faces[index] = Quad(go.transform, "Face", new Vector3(size.x, size.y, 1f),
                new Vector3(0f, 0f, -0.0004f), faceMat);
            var rim = Quad(go.transform, "Rim", new Vector3(size.x * 1.10f, size.y * 1.18f, 1f),
                new Vector3(0f, 0f, -0.0008f), rimMat);
            rim.transform.SetAsFirstSibling();

            var lcd = new GameObject("Lcd", typeof(RectTransform), typeof(Canvas));
            lcd.transform.SetParent(go.transform, false);
            lcd.layer = _layer;
            lcd.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            lcd.transform.localPosition = new Vector3(0f, 0f, 0.0006f);
            var scale = size.x * 0.92f / CanvasW;
            lcd.transform.localScale = new Vector3(scale, scale, scale);
            var rt = lcd.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            var canvas = lcd.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (Application.isPlaying)
                canvas.worldCamera = ArStageView.FindCamera(transform);
            canvas.overrideSorting = true;
            canvas.sortingOrder = 42;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(lcd.transform, false);
            labelGo.layer = _layer;
            var label = labelGo.GetComponent<Text>();
            WrldzType.Style(label, 22, display: false, heavyOutline: true);
            label.text = title;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.fontSize = 28;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 32;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _chips[index] = go;
            _labels[index] = label;
        }

        MeshRenderer Quad(Transform parent, string name, Vector3 scale, Vector3 pos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            go.layer = _layer;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            return mr;
        }

        public void SetDeployed(bool on)
        {
            _deployed = on;
            if (!on)
            {
                SetEnabled(false, false, false);
                return;
            }

            if (gameObject.activeSelf != on)
                gameObject.SetActive(on);
        }

        public void SetEnabled(bool battle, bool main2, bool end)
        {
            if (!_deployed)
                battle = main2 = end = false;
            var any = battle || main2 || end;
            if (gameObject.activeSelf != any)
                gameObject.SetActive(any);
            if (!any) return;
            SetChip(0, battle);
            SetChip(1, main2);
            SetChip(2, end);
        }

        void SetChip(int i, bool on)
        {
            if (_chips == null || i < 0 || i >= _chips.Length || _chips[i] == null) return;
            if (_chips[i].activeSelf != on)
                _chips[i].SetActive(on);
            if (_labels[i] != null)
                _labels[i].color = on ? Color.white : new Color(0.72f, 0.76f, 0.82f, 0.55f);
        }
    }
}
