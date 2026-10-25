using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The deck's relic box in the hall's header (BUILD_PLAN §6 P4 step 2; deck.js:196-206, anime_box_v6.js:33-86): open at rest
    /// (the page's idle: lid .34, the deck's cards risen .2, light .5, the Wdjat dim), shut by SAVE (eIn over .2 s, the Wdjat
    /// lights 1.5 → .7, a gold flash at the mouth, "saved · the box is sealed"), and the lid lifting again on a deck select or
    /// the next edit. The golden-box set is local-only (licence unknown, D7) and drawn by the menu's BoxRig; a clone gets the
    /// page's own v5 rig as three tracked stills (idle / closed / sealed) crossfaded by the same state.
    /// </summary>
    public sealed class RelicBox
    {
        BoxRig _rig;
        Image _idle, _closed, _sealedImg, _flash;
        Rect _boxRect, _flashRect;
        double _shutAt = double.NegativeInfinity, _openAt = double.NegativeInfinity;

        /// <summary>True between a SAVE and the next edit / deck select.</summary>
        public bool Sealed { get; private set; }

        /// <summary>The page's status line for the box (the header's .st span).</summary>
        public string StatusLine => Sealed ? "saved · the box is sealed" : "editing · the box is open";

        public static RelicBox Build(DeckHallSpec spec, Transform host, Rect stageRect, IList<int> cards)
        {
            var b = spec.D("box");
            if (b == null || host == null) return null;
            var box = new RelicBox { _boxRect = RJ.R(b, "rect") };
            double cx = RJ.N(b, "cx", 45), cy = RJ.N(b, "cy", 104), s = RJ.N(b, "s", 70);
            box._flashRect = new Rect((float)(box._boxRect.x + cx - .7 * s), (float)(box._boxRect.y + cy - .5 * s - .7 * s),
                (float)(1.4 * s), (float)(1.4 * s));
            var local = RJ.D(b, "local");
            var box6 = RJ.D(b, "box6");
            if (local != null && box6 != null && MenuV6Art.HasLocal(RJ.S(local, "full")))
            {
                RectTransform rigNode = null;
                try
                {
                    var rigDef = new Dictionary<string, object>
                    {
                        ["canvasRect"] = new Dictionary<string, object>
                        {
                            ["x"] = (double)box._boxRect.x, ["y"] = (double)box._boxRect.y, ["w"] = (double)box._boxRect.width,
                            ["h"] = (double)box._boxRect.height
                        },
                        ["cx"] = cx, ["cy"] = cy, ["s"] = s,
                        ["bbox"] = RJ.L(box6, "bbox"), ["open"] = RJ.L(box6, "open"),
                        ["W1"] = 1.2887741, ["Y1"] = .21047555,   // the box6 frame (relics.js, the menu's rigs_<lay>.json box)
                        ["parts"] = new Dictionary<string, object>
                        {
                            ["full"] = new Dictionary<string, object> { ["file"] = RJ.S(local, "full") },
                            ["body"] = new Dictionary<string, object> { ["file"] = RJ.S(local, "body") },
                            ["lid"] = new Dictionary<string, object> { ["file"] = RJ.S(local, "lid") },
                            ["eye"] = new Dictionary<string, object> { ["file"] = RJ.S(local, "eye") }
                        }
                    };
                    var ctx = new RigContext
                    {
                        Kind = spec.Kind, Rigs = new Dictionary<string, object> { ["box"] = rigDef }, Gfx = new UguiGfx(),
                        Data = new MenuV6Snapshot { DeckCards = cards != null ? new List<int>(cards).ToArray() : null },
                        RealTime = () => Time.unscaledTimeAsDouble
                    };
                    rigNode = SheetDraw.Stretch(host, "RelicBox");
                    var rig = new BoxRig();
                    if (rig.Build(ctx, rigNode, stageRect))
                    {
                        rig.State = box.State;
                        box._rig = rig;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ MenuV6] deck box kept its stills: " + ex.Message);
                    box._rig = null;
                }

                // a rig that did not build leaves no half-drawn box over the stills (DestroyNow: edit mode too, MenuSmokeTest)
                if (box._rig == null && rigNode != null) WRLDZ.UI.Shell.FloatingPanel.DestroyNow(rigNode.gameObject);
            }

            if (box._rig == null)
            {
                var fb = RJ.D(b, "fallback");
                var node = SheetDraw.Stretch(host, "RelicBoxStills");
                box._idle = SheetDraw.Img(node, "Idle", MenuV6Art.Sprite(RJ.S(RJ.D(fb, "idle"), "file")), box._boxRect, stageRect);
                box._closed = SheetDraw.Img(node, "Closed", MenuV6Art.Sprite(RJ.S(RJ.D(fb, "closed"), "file")), box._boxRect, stageRect);
                box._sealedImg = SheetDraw.Img(node, "Sealed", MenuV6Art.Sprite(RJ.S(RJ.D(fb, "sealed"), "file")), box._boxRect, stageRect);
            }

            box._flash = SheetDraw.Img(host, "BoxFlash", MenuV6Art.Sprite("WRLDZ/MenuV6/Rigs/Common/spr_goldHot"), box._flashRect,
                stageRect);
            box.Tick(0);
            return box;
        }

        public void SetCards(IList<int> cards) => _rig?.SetCards(cards);

        /// <summary>SAVE shut the box at hall time <paramref name="t"/>.</summary>
        public void Shut(double t)
        {
            if (Sealed) return;
            Sealed = true;
            _shutAt = t;
        }

        /// <summary>The lid lifts again (a deck select, the next edit after a SAVE).</summary>
        public void Reopen(double t, bool fromShut = true)
        {
            if (!Sealed && !fromShut) return;
            Sealed = false;
            _openAt = t;
            if (double.IsNegativeInfinity(_shutAt) || fromShut) _shutAt = t - 10;   // closed before the lift
        }

        double _now;

        /// <summary>open (1 = the idle ajar lid), the Wdjat (eye), the flash alpha at hall time t.</summary>
        void Values(double t, out double open, out double eye, out double flash)
        {
            var us = t - _shutAt;
            var shut = double.IsNegativeInfinity(_shutAt) ? 0 : DeckHallMath.BoxShut(us);
            var eyeOn = double.IsNegativeInfinity(_shutAt) ? 0 : DeckHallMath.BoxEyeOn(us);
            open = 1 - shut;
            flash = double.IsNegativeInfinity(_shutAt) || us > 1 ? 0 : DeckHallMath.BoxFlash(us);
            if (!Sealed && !double.IsNegativeInfinity(_openAt))
            {
                var v = t - _openAt;
                open = DeckHallMath.BoxReopen(v);
                eyeOn *= 1 - MenuV6Math.InOut(MenuV6Math.Seg(v, 0, .3));
            }

            eye = DeckHallMath.BoxEye(us, open, eyeOn);
        }

        (double lid, double rise, double light, double eye) State(double _)
        {
            Values(_now, out var open, out var eye, out _);
            return (.34 * open, .2 * open, .5 * open, eye);
        }

        public void Tick(double t)
        {
            _now = t;
            Values(t, out var open, out var eye, out var flash);
            if (_rig != null) _rig.Tick(t);
            else
            {
                var sealedK = MenuV6Math.Clamp((eye - .3) / 1.2, 0, 1);
                SheetDraw.SetA(_idle, MenuV6Linear.Fade(open));
                SheetDraw.SetA(_closed, MenuV6Linear.Fade((1 - open) * (1 - sealedK)));
                SheetDraw.SetA(_sealedImg, MenuV6Linear.Fade((1 - open) * sealedK));
            }

            if (_flash != null) _flash.color = new Color(1f, 1f, 1f, MenuV6Linear.Alpha(flash));
        }
    }
}
