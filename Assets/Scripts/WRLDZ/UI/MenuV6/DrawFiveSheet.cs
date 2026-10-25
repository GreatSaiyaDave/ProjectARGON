using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// DRAW 5 (BUILD_PLAN §6 P4 step 7, D13): a test draw of five cards at random from the Main Deck, UI-only — nothing is
    /// persisted, the deck is not shuffled or changed. No mockup page exists; the exports compose it from the deck page's
    /// own parts (deck_&lt;layout&gt;.json draw5): the inspect sheet's chrome and Puzzle-lock at the inspect rect, the .tabs2
    /// header ("DRAW 5" + "FIVE AT RANDOM FROM MAIN · NOT SAVED"), five cards in the House of Life frame at the collection
    /// size with their names, and one plate (DRAW AGAIN). Tapping outside the sheet (the back of the hall) closes it.
    /// </summary>
    public sealed class DrawFiveSheet
    {
        DeckSheet _sheet;
        Func<int, Sprite> _art;
        Func<int, string> _name;
        readonly List<Image> _cards = new();
        readonly List<Text> _names = new();
        readonly System.Random _rng = new();
        List<int> _main = new();
        Button _outside;

        public bool IsOpen => _sheet != null && _sheet.IsOpen;
        public GameObject Root => _sheet != null && _sheet.Root != null ? _sheet.Root.gameObject : null;

        /// <summary>The five picked (tests read them).</summary>
        public readonly List<int> Drawn = new();

        public static DrawFiveSheet Build(DeckHall hall, Transform host, Func<int, Sprite> art, Func<int, string> name)
        {
            if (hall == null || host == null) return null;
            var spec = hall.Spec;
            var d5 = spec.D("draw5");
            var geo = RJ.D(d5, "geo");
            var rect = RJ.R(geo, "rect");
            if (rect.width <= 0) rect = spec.Inspect;
            var f = new DrawFiveSheet { _art = art, _name = name };
            // a tap on the hall outside the sheet closes it (the inspect sheet's rule, deck.js)
            f._outside = DeckHall.Hit(host, "DrawFiveOutside", hall.StageRect, hall.StageRect, f.Close);
            f._outside.gameObject.SetActive(false);
            f._sheet = DeckSheet.Build(hall, host, "DrawFive", rect);
            if (f._sheet == null) return null;
            f._sheet.Closed = () =>
            {
                if (f._outside != null) f._outside.gameObject.SetActive(false);
            };
            var c = f._sheet.Content;
            var stage = hall.StageRect;
            var specs = RJ.L(RJ.D(d5, "specs"), "texts");
            Dictionary<string, object> Spec(string text)
            {
                if (specs == null) return null;
                foreach (var o in specs)
                    if (o is Dictionary<string, object> d && RJ.S(d, "text") == text)
                        return d;
                return null;
            }

            hall.DeckLabel(c, "Title", DeckHallSpec.Slot(Spec("DRAW 5")), "DRAW 5");
            hall.DeckLabel(c, "Sub", DeckHallSpec.Slot(Spec("FIVE AT RANDOM FROM MAIN · NOT SAVED")), "FIVE AT RANDOM FROM MAIN · NOT SAVED");
            var underline = spec.Chrome("insp_tab_on");
            var tSlot = DeckHallSpec.Slot(Spec("DRAW 5"));
            if (underline != null && tSlot != null)
            {
                var ue = spec.ChromeEl("insp_tab_on");
                var us = RJ.R(underline, "shotRect");
                var r = new Rect(tSlot.Rect.x + (us.x - ue.x), tSlot.Rect.y + (us.y - ue.y), tSlot.Rect.width + (us.width - ue.width), us.height);
                SheetDraw.Img(c, "TabOn", spec.Sprite(underline), r, stage);
            }

            var p5 = RJ.D(geo, "P5");
            float cw = (float)RJ.N(p5, "cw", spec.GridCw), ch = (float)RJ.N(p5, "ch", spec.GridCh);
            float gap = (float)RJ.N(p5, "gap", 24), top = (float)RJ.N(p5, "top", 126);
            var rowX = (float)RJ.N(geo, "rowX", rect.x + 94);
            var nmSpec = DeckHallSpec.Slot(Spec(null) ?? FirstNm(specs));
            for (var i = 0; i < 5; i++)
            {
                var cr = new Rect(rowX + i * (cw + gap), rect.y + top, cw, ch);
                spec.ChromeImg(c, "Frame" + i, "cc_frame", cr, stage);
                var img = SheetDraw.Img(c, "Card" + i, UiFoundation.WhiteSprite(), cr, stage);
                if (img != null)
                {
                    img.preserveAspect = true;
                    f._cards.Add(img);
                }

                if (nmSpec != null)
                {
                    var nr = new Rect(cr.x, cr.yMax + (nmSpec.Rect.y - (rect.y + top + ch)), cw, nmSpec.Rect.height);
                    var lab = hall.DeckLabel(c, "Name" + i, DeckHallSpec.At(nmSpec, nr), "");
                    if (lab != null)
                    {
                        lab.alignment = TextAnchor.MiddleCenter;
                        lab.horizontalOverflow = HorizontalWrapMode.Overflow;
                        f._names.Add(lab);
                    }
                }
            }

            var again = RJ.D(d5, "again");
            var aShot = RJ.R(again, "shotRect");
            var aSlot = DeckHallSpec.Slot(Spec("DRAW AGAIN"));
            var aEl = aSlot != null ? aSlot.Rect : aShot;
            var btn = DeckHall.Hit(c, "DrawAgain", aEl, stage, () =>
            {
                FreeUiKit.PlaySelect();
                f.Deal();
            });
            var plate = SheetDraw.Img(btn.transform, "Plate", MenuV6Art.Sprite(RJ.S(again, "file")),
                aShot, aEl, sliced: true);
            if (plate != null) plate.transform.SetAsFirstSibling();
            if (aSlot != null)
            {
                var lines = RJ.L(Spec("DRAW AGAIN"), "lines");
                var lr = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : aEl;
                hall.DeckLabel(btn.transform, "Label", DeckHallSpec.At(aSlot, lr), "DRAW AGAIN", aEl);
            }

            return f;
        }

        static Dictionary<string, object> FirstNm(List<object> specs)
        {
            if (specs == null) return null;
            foreach (var o in specs)
                if (o is Dictionary<string, object> d && RJ.S(d, "cls") == "nm")
                    return d;
            return null;
        }

        /// <summary>Open with five random cards of <paramref name="main"/> (a copy: the deck itself is never touched).</summary>
        public void Open(IList<int> main)
        {
            _main = main != null ? new List<int>(main) : new List<int>();
            Deal();
            if (_outside != null)
            {
                _outside.gameObject.SetActive(true);
                _outside.transform.SetAsLastSibling();
            }

            _sheet.Open();
            if (_sheet.Root != null) _sheet.Root.SetAsLastSibling();
        }

        public void Close()
        {
            _sheet?.Close();
            if (_outside != null) _outside.gameObject.SetActive(false);
        }

        public void Kill()
        {
            _sheet?.Kill();
            if (_outside != null) _outside.gameObject.SetActive(false);
        }

        void Deal()
        {
            Drawn.Clear();
            var pool = new List<int>(_main);
            for (var i = 0; i < 5 && pool.Count > 0; i++)
            {
                var k = _rng.Next(pool.Count);
                Drawn.Add(pool[k]);
                pool.RemoveAt(k);
            }

            for (var i = 0; i < _cards.Count; i++)
            {
                var has = i < Drawn.Count;
                var id = has ? Drawn[i] : 0;
                var sp = has && _art != null ? _art(id) : null;
                _cards[i].sprite = sp != null ? sp : UiFoundation.WhiteSprite();
                _cards[i].color = sp != null ? Color.white : new Color(1, 1, 1, has ? .1f : 0f);
                if (i < _names.Count)
                {
                    var nm = has && _name != null ? _name(id) ?? "" : "";
                    _names[i].text = nm;
                    TextFx.FitWidth(_names[i], _names[i].rectTransform.rect.width > 1 ? _names[i].rectTransform.rect.width : 132, 11);
                }
            }
        }
    }
}
