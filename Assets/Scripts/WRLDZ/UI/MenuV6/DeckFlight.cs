using System;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The deck hall's motion beats (BUILD_PLAN §6 P4 step 6; deck.js:157-172): the +1 flight (the inspect card arcs .65 s
    /// eIO to its altar slot, lifted 160 px, rolled −8°, in its constant blue glow: the page's inline eased glow loses to
    /// anime_v6.css), the landing burst (the ring at the slot, 36 motes of the page's formula) and the tap ripple. Every beat
    /// draws in the hall's Fx layer in stage px and removes itself; nothing here touches deck data.
    /// </summary>
    public static class DeckFlight
    {
        /// <summary>A stage-px rect of a RectTransform anywhere under the hall (its world corners into the hall's stage).</summary>
        public static Rect StageRectOf(DeckHall hall, RectTransform rt)
        {
            if (hall == null || rt == null || hall.Fx == null) return Rect.zero;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var a = hall.Fx.InverseTransformPoint(c[1]);   // top-left
            var b = hall.Fx.InverseTransformPoint(c[3]);   // bottom-right
            var size = hall.Fx.rect.size;
            float x0 = a.x + size.x * .5f, y0 = size.y * .5f - a.y, x1 = b.x + size.x * .5f, y1 = size.y * .5f - b.y;
            return new Rect(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        }

        /// <summary>Fly <paramref name="art"/> from stage rect <paramref name="from"/> to the slot at <paramref name="to"/>;
        /// <paramref name="target"/> (the new slot) stays hidden until the card lands, then <paramref name="landed"/> runs.</summary>
        public static void Plus1(DeckHall hall, Sprite art, Rect from, Rect to, GameObject target, Action landed)
        {
            if (hall == null || hall.Fx == null || from.width <= 0 || to.width <= 0)
            {
                landed?.Invoke();
                return;
            }

            var spec = hall.Spec;
            var stage = hall.StageRect;
            var t0 = hall.Now;
            var node = DeckHall.Node(hall.Fx, "Plus1Flight", stage, stage);
            var group = SheetDraw.Group(target);
            if (group != null) group.alpha = 0f;
            var card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(node, false);
            card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(.5f, .5f);
            var glowEl = spec.ChromeEl("fly_glow");
            Image glow = null;
            if (glowEl.width > 0)
            {
                var shot = spec.ShotFor("fly_glow", glowEl);
                glow = spec.ChromeImg(card, "Glow", "fly_glow", glowEl, glowEl);
                if (glow != null)
                {
                    // stretch the glow with the card (its margins kept in card units)
                    var rt = glow.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = new Vector2(shot.x - glowEl.x, glowEl.yMax - shot.yMax);
                    rt.offsetMax = new Vector2(shot.xMax - glowEl.xMax, glowEl.y - shot.y);
                }
            }

            var face = new GameObject("Face", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            face.transform.SetParent(card, false);
            UiTheme.Stretch(face.rectTransform);
            face.sprite = art != null ? art : UiFoundation.WhiteSprite();
            face.preserveAspect = true;
            face.raycastTarget = false;

            var ring = SheetDraw.Img(node, "Burst", spec.ChromeSprite("burst"), new Rect(0, 0, 1, 1), stage);
            var blue = MenuV6Art.Sprite("WRLDZ/MenuV6/Rigs/Common/spr_blue");
            var hot = MenuV6Art.Sprite("WRLDZ/MenuV6/Rigs/Common/spr_goldHot");
            var motes = new Image[36];
            for (var i = 0; i < motes.Length; i++)
                motes[i] = SheetDraw.Img(node, "Mote", i % 3 != 0 ? blue : hot, new Rect(0, 0, 12, 12), stage);
            if (ring != null) ring.enabled = false;
            foreach (var m in motes)
                if (m != null)
                    m.enabled = false;

            var done = false;
            var burstEl = spec.ChromeEl("burst");
            var burstW = burstEl.width > 0 ? burstEl.width : 120f;
            hall.OnTick(t =>
            {
                if (done || node == null) return;
                var u = t - t0;
                DeckHallMath.Flight(u, from.center.x, from.center.y, from.width, to.center.x, to.center.y, to.width,
                    out var x, out var y, out var w, out var rot, out var e);
                var h = w * 614.0 / 421.0;
                var flying = u < .7;
                card.gameObject.SetActive(flying);
                if (flying)
                {
                    card.anchoredPosition = new Vector2((float)x, (float)-y);
                    card.sizeDelta = new Vector2((float)w, (float)h);
                    card.localRotation = Quaternion.Euler(0f, 0f, (float)-rot);
                }
                else if (landed != null)
                {
                    if (group != null) group.alpha = 1f;
                    var fire = landed;
                    landed = null;
                    fire();
                }

                var bk = DeckHallMath.Burst(u);
                var burstOn = u >= .68 && bk < 1;
                if (ring != null)
                {
                    ring.enabled = burstOn;
                    if (burstOn)
                    {
                        var s = (float)((.3 + 2.4 * bk) * burstW);
                        MenuV6Layout.Place(ring.rectTransform, new Rect(to.center.x - s / 2f, to.center.y - s / 2f, s, s), stage);
                        ring.color = new Color(1f, 1f, 1f, MenuV6Linear.Fade(1 - bk));
                    }
                }

                for (var i = 0; i < motes.Length; i++)
                {
                    var m = motes[i];
                    if (m == null) continue;
                    m.enabled = burstOn;
                    if (!burstOn) continue;
                    DeckHallMath.Mote(i, bk, out var dx, out var dy, out var a);
                    MenuV6Layout.Place(m.rectTransform, new Rect((float)(to.center.x + dx - 6), (float)(to.center.y + dy - 6), 12, 12), stage);
                    m.color = new Color(1f, 1f, 1f, MenuV6Linear.Alpha(a));
                }

                if (u >= 1.45)
                {
                    done = true;
                    if (group != null) group.alpha = 1f;
                    landed?.Invoke();
                    landed = null;
                    UnityEngine.Object.Destroy(node.gameObject);
                }
            });
            if (!Application.isPlaying)
            {
                // edit mode (tests): no clock runs; land at once
                if (group != null) group.alpha = 1f;
                landed?.Invoke();
                UnityEngine.Object.DestroyImmediate(node.gameObject);
                done = true;
            }
        }

        /// <summary>The tap ripple at a stage point (fx/tapr, .5 s).</summary>
        public static void Ripple(DeckHall hall, Vector2 at)
        {
            if (hall == null || hall.Fx == null || !Application.isPlaying) return;
            var spec = hall.Spec;
            var sp = spec.ChromeSprite("tapr");
            if (sp == null) return;
            var el = spec.ChromeEl("tapr");
            var baseW = el.width > 0 ? spec.ShotFor("tapr", el).width : 120f;
            var stage = hall.StageRect;
            var img = SheetDraw.Img(hall.Fx, "Ripple", sp, new Rect(at.x, at.y, 1, 1), stage);
            if (img == null) return;
            var t0 = hall.Now;
            var gone = false;
            hall.OnTick(t =>
            {
                if (gone || img == null) return;
                DeckHallMath.Ripple(t - t0, out var a, out var s);
                if (a <= 0)
                {
                    gone = true;
                    UnityEngine.Object.Destroy(img.gameObject);
                    return;
                }

                var w = (float)(baseW * s);
                MenuV6Layout.Place(img.rectTransform, new Rect(at.x - w / 2f, at.y - w / 2f, w, w), stage);
                img.color = new Color(1f, 1f, 1f, MenuV6Linear.Fade(a));
            });
        }
    }
}
