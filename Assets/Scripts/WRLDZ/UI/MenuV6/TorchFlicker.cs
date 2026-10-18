using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The torch flicker of P1a (BUILD_PLAN §6 P1a step 3): the page's <c>flick</c> curve (menu.js:653, with the noise
    /// row of core.js NZF exported in schedules.json torch.NZF_row7) drives what schedules.json torch.drives lists:
    /// Stone B over Stone A at alpha flick, the keystone's lit state f1 over f0 at alpha flick, and the torch pool's b0
    /// weight a0 = .2 (.8 + .3 flick). sg (the dread ladder) is 0 until P5. The flame itself stays the baked frame
    /// until P1b's mesh.
    /// </summary>
    public sealed class TorchFlicker : MonoBehaviour
    {
        readonly List<Graphic> _flickAlpha = new();
        Graphic _poolB0;
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

        /// <summary>A layer whose alpha is the flick (Stone B, keystone f1).</summary>
        public void DriveAlpha(Graphic g)
        {
            if (g != null) _flickAlpha.Add(g);
        }

        /// <summary>The torch pool's b0 basis layer (its alpha is the weight a0).</summary>
        public void DrivePool(Graphic g) => _poolB0 = g;

        public void Apply(float t)
        {
            var flick = (float)MenuV6Math.Flick(t, _noise);
            Flick = flick;
            for (var i = 0; i < _flickAlpha.Count; i++)
                SetAlpha(_flickAlpha[i], flick);
            if (_poolB0 != null) SetAlpha(_poolB0, (float)MenuV6Math.TorchPoolA0(flick));
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
