using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The torch flicker of P1a (BUILD_PLAN §6 P1a step 3): the page's <c>flick</c> curve (menu.js:653, with the noise
    /// row of core.js NZF exported in schedules.json torch.NZF_row7) drives what schedules.json torch.drives lists:
    /// Stone B over Stone A at alpha flick, the keystone's lit state f1 over f0 at alpha flick, and the torch pool's b0
    /// weight a0 = .2 (.8 + .3 flick). sg (the dread ladder) is 0 until P5. P1b: the flame's tongues are the FlameRig
    /// (the page's visible flicker is mostly their sway), and each driven layer carries its Linear-space gain
    /// (MenuV6Linear.LayerGain: the pool's warm light was too strong in the P1a capture).
    /// </summary>
    public sealed class TorchFlicker : MonoBehaviour
    {
        readonly List<(Graphic g, float gain)> _flickAlpha = new();
        Graphic _poolB0;
        float _poolGain = 1f, _poolSwing = float.NaN;
        double[] _noise;
        float _last = -1f;

        public float Flick { get; private set; }

        public static TorchFlicker Attach(GameObject host, double[] noiseRow)
        {
            if (host == null) return null;
            var f = host.GetComponent<TorchFlicker>();
            if (f == null) f = host.AddComponent<TorchFlicker>();
            f._noise = noiseRow;
            return f;
        }

        /// <summary>A layer whose alpha is the flick (Stone B, keystone f1), times its gain.</summary>
        public void DriveAlpha(Graphic g, float gain = 1f)
        {
            if (g != null) _flickAlpha.Add((g, gain));
        }

        /// <summary>The torch pool's b0 basis layer (its alpha is the weight a0), times its gain.</summary>
        public void DrivePool(Graphic g, float gain = 1f)
        {
            _poolB0 = g;
            _poolGain = gain;
        }

        /// <summary>P2 (the P1b landing review: the torch's light swing on the wall was too small to see): the pool keeps the
        /// P1b fit's MEAN (gain × .2 × .95) but swings with its own gain about it, a0 = gain .19 + swing .06 (flick − .5).
        /// unity_build/p2_code/torch_fit.py (Linear model, flick 0 → 1 on the torch wall): the page 3.39 levels, P1b 2.36,
        /// swing .6 → 3.54 (p95 8.5 vs the page's 7.4); a page render at flick .00 / .95 measures 2.39 levels.</summary>
        public void DrivePool(Graphic g, float gain, float swing)
        {
            _poolB0 = g;
            _poolGain = gain;
            _poolSwing = swing;
        }

        public void Apply(float t)
        {
            var flick = (float)MenuV6Math.Flick(t, _noise);
            Flick = flick;
            for (var i = 0; i < _flickAlpha.Count; i++)
                SetAlpha(_flickAlpha[i].g, Mathf.Clamp01(flick * _flickAlpha[i].gain));
            if (_poolB0 != null)
            {
                var a0 = float.IsNaN(_poolSwing)
                    ? (float)MenuV6Math.TorchPoolA0(flick) * _poolGain
                    : _poolGain * .2f * .95f + _poolSwing * .2f * .3f * (flick - .5f);
                SetAlpha(_poolB0, Mathf.Clamp01(a0));
            }
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            if (Mathf.Approximately(c.a, a)) return;
            c.a = a;
            g.color = c;
        }

        void Update()
        {
            var t = MenuV6Clock.Now;
            if (Mathf.Approximately(t, _last)) return;
            _last = t;
            Apply(t);
        }
    }
}
