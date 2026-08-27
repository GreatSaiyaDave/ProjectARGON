using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Soul image on player badges — remaining fractures as a count over the gem.
    /// Intact glow when whole; cracked gem when any Shadow Game loss has landed.
    /// </summary>
    public class SoulBadgeView : MonoBehaviour
    {
        Image _gem;
        Text _count;

        public static SoulBadgeView Create(Transform parent)
        {
            var go = new GameObject("SoulBadge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SoulBadgeView>();
            v.Build();
            return v;
        }

        void Build()
        {
            var gemGo = new GameObject("Gem", typeof(RectTransform), typeof(Image));
            gemGo.transform.SetParent(transform, false);
            var grt = gemGo.GetComponent<RectTransform>();
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;
            _gem = gemGo.GetComponent<Image>();
            _gem.sprite = ImagineAssets.IconSoul() ?? ImagineAssets.NaviSpirit();
            _gem.preserveAspect = true;
            _gem.raycastTarget = false;
            _gem.color = Color.white;

            var tGo = new GameObject("Count", typeof(RectTransform), typeof(Text), typeof(Outline));
            tGo.transform.SetParent(transform, false);
            var trt = tGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.05f, -0.08f);
            trt.anchorMax = new Vector2(0.95f, 0.42f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            _count = tGo.GetComponent<Text>();
            WrldzType.Style(_count, 12, display: true, heavyOutline: true);
            _count.alignment = TextAnchor.MiddleCenter;
            _count.color = DuelystUi.GoldHot;
            _count.raycastTarget = false;
            var ol = tGo.GetComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.9f);
            ol.effectDistance = new Vector2(1.1f, -1.1f);
        }

        public void Bind(PlayerProgress progress)
        {
            if (progress != null)
            {
                if (SoulFractureService.TickRegen(progress))
                {
                    var acc = AppSession.Ensure()?.Account;
                    if (acc != null && acc.progress == progress)
                        ProgressionService.Persist(acc);
                }
            }
            var cap = progress != null
                ? progress.soulFractureCapacity
                : PlayerProgress.DefaultSoulFractureCapacity;
            var remain = progress != null
                ? progress.SoulRemaining
                : PlayerProgress.DefaultSoulFractureCapacity;
            var cracked = progress != null && progress.soulFractures > 0;
            if (_gem != null)
            {
                var spr = cracked
                    ? (ImagineAssets.IconSoulFractured() ?? ImagineAssets.IconSoul())
                    : ImagineAssets.IconSoul();
                if (spr != null) _gem.sprite = spr;
                _gem.color = remain <= 0
                    ? new Color(0.45f, 0.22f, 0.35f, 1f)
                    : Color.white;
            }

            if (_count != null)
                _count.text = remain.ToString();
        }
    }
}
