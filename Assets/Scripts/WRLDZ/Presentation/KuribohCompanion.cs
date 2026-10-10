using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// The chosen Kuriboh stays with the duelist after onboarding. Silent: no lines.
    /// Uses the existing navi portraits, not a new model.
    /// </summary>
    public static class KuribohCompanion
    {
        /// <summary>
        /// The spirit companion to show (<see cref="SpiritCompanions"/> id): the account's choice when allowed (the lab admin may
        /// pick any Kuriboh), otherwise its Kuriboh team's. Empty = none.
        /// </summary>
        public static string CurrentId()
        {
            var acc = AppSession.Ensure()?.Account;
            if (acc?.progress == null) return "";
            return SpiritCompanions.Resolve(acc.progress, LabAdminService.IsAdmin(acc));
        }

        public static void AttachMap(RectTransform avatar)
        {
            if (avatar == null) return;
            var old = avatar.Find("Navi");
            if (old != null) Object.Destroy(old.gameObject);

            // 3D spirit companion beside the avatar (same UI-slot contract as OverworldAvatar3d.Attach)
            var cid = CurrentId();
            if (SpiritCompanionModel.Exists(cid))
            {
                var holder = new GameObject("Navi", typeof(RectTransform));
                holder.transform.SetParent(avatar, false);
                holder.layer = avatar.gameObject.layer;
                var hrt = holder.GetComponent<RectTransform>();
                hrt.anchorMin = new Vector2(0.62f, 0.42f);
                hrt.anchorMax = new Vector2(1.08f, 0.92f);
                hrt.offsetMin = Vector2.zero;
                hrt.offsetMax = Vector2.zero;
                var m = SpiritCompanionModel.Spawn(cid, holder.transform, avatar.gameObject.layer, 48f);
                if (m != null)
                {
                    m.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the canvas camera like the avatar
                    m.transform.localPosition = new Vector3(0f, 0f, -30f);
                    return;
                }
                Object.Destroy(holder);
            }

            if (!TrySprite(out var spr)) return;

            var go = new GameObject("Navi", typeof(RectTransform), typeof(Image), typeof(NaviBob));
            go.transform.SetParent(avatar, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.62f, 0.42f);
            rt.anchorMax = new Vector2(1.08f, 0.92f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = spr;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;
            go.GetComponent<NaviBob>().Bind(rt);
        }

        /// <summary>
        /// AR duel / Zone Mode companion. Called from <see cref="ArDuelInteractionSystem"/> build.
        /// Routes to <see cref="KuribohArBehaviour"/> (left-side hologram with timer + behaviours)
        /// whenever the owning interaction system can be found; the old fixed arena quad below is
        /// only a fallback for stages without one.
        /// </summary>
        public static void EnsureDuel(Transform arenaRoot, int layer)
        {
            if (arenaRoot == null) return;
            if (TryEnsureArBehaviour(arenaRoot, layer)) return;
            EnsureLegacyQuad(arenaRoot, layer);
        }

        /// <summary>
        /// Direct mount used by <see cref="ArDuelInteractionSystem"/>: the caller already knows its own system, stage
        /// root and stage camera, so no parent walk is needed. The walk cannot reach the system on ARCore / Lenses /
        /// Salvage, where the stage hangs under the session's DontDestroyOnLoad FloorAnchor while the interaction host
        /// lives under the ArDuelSpace object. The legacy fixed quad is only used when no AR companion can be built.
        /// </summary>
        public static void EnsureDuel(ArDuelInteractionSystem sys, Transform arenaRoot, Transform stageRoot,
            Camera stageCamera, int layer)
        {
            if (arenaRoot == null) return;
            if (sys == null || stageRoot == null)
            {
                EnsureDuel(arenaRoot, layer);
                return;
            }

            var kb = KuribohArBehaviour.Ensure(sys, stageRoot, stageCamera, layer);
            if (kb != null)
            {
                var legacy = arenaRoot.Find("KuribohCompanion");
                if (legacy != null) Object.Destroy(legacy.gameObject);
                return;
            }

            EnsureLegacyQuad(arenaRoot, layer);
        }

        static void EnsureLegacyQuad(Transform arenaRoot, int layer)
        {
            var existing = arenaRoot.Find("KuribohCompanion");
            if (existing != null) return;
            if (!TrySprite(out var spr) || spr.texture == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "KuribohCompanion";
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(arenaRoot, false);
            go.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Texture");
            if (sh != null)
            {
                var mat = new Material(sh);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", spr.texture);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", spr.texture);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                mat.color = Color.white;
                mr.sharedMaterial = mat;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SetLayer(go, layer);
            go.AddComponent<NaviFloat>();
        }

        /// <summary>
        /// Find the interaction system that owns <paramref name="arenaRoot"/> and mount the AR
        /// companion beside its hand volume (stage root, not the arena — Zone Mode hides the arena).
        /// </summary>
        static bool TryEnsureArBehaviour(Transform arenaRoot, int layer)
        {
            ArDuelInteractionSystem sys = null;
            var a = arenaRoot.parent;
            for (var depth = 0; a != null && depth < 4 && sys == null; depth++, a = a.parent)
            {
                foreach (var cand in a.GetComponentsInChildren<ArDuelInteractionSystem>(true))
                {
                    if (cand != null && cand.Arena != null && cand.Arena.ArenaRoot == arenaRoot)
                    {
                        sys = cand;
                        break;
                    }
                }
            }

            if (sys == null) return false;
            // Stage root = the arena layer's parent (the interaction host itself may sit under a
            // separate lifetime host, e.g. the ArDuelSpace object, so its parent is not the stage).
            var stageRoot = sys.Arena.transform.parent != null ? sys.Arena.transform.parent : arenaRoot.parent;
            var space = stageRoot != null ? stageRoot.GetComponentInParent<ArDuelSpace>() : null;
            var cam = space != null ? space.StageCamera : null;
            var kb = KuribohArBehaviour.Ensure(sys, stageRoot, cam, layer);
            if (kb == null) return false;

            // Retire the legacy fixed arena quad if an earlier build left one.
            var legacy = arenaRoot.Find("KuribohCompanion");
            if (legacy != null) Object.Destroy(legacy.gameObject);
            return true;
        }

        /// <summary>The chosen team's navi portrait with its flat backdrop keyed out (cached).</summary>
        public static bool TryDuelSprite(out Sprite spr) => TrySprite(out spr);

        static readonly Dictionary<int, Sprite> CutCache = new();

        static bool TrySprite(out Sprite spr)
        {
            spr = null;
            var p = AppSession.Ensure()?.Account?.progress;
            if (p == null) return false;
            var team = (KuribohTeam)p.kuribohTeam;
            if (team == KuribohTeam.None) return false;
            var id = (int)team;
            if (CutCache.TryGetValue(id, out spr) && spr != null) return true;
            var raw = WrldzPresentation.NaviForTeam(id);
            if (raw == null || raw.texture == null) return false;
            spr = CutBackdrop(raw);
            CutCache[id] = spr;
            return spr != null;
        }

        static Sprite CutBackdrop(Sprite raw)
        {
            Texture2D src;
            try { src = raw.texture; }
            catch { return raw; }
            Color[] px;
            try { px = src.GetPixels(); }
            catch { return raw; }

            var w = src.width;
            var h = src.height;
            var key = px[0];
            var seen = new bool[px.Length];
            var stack = new Stack<int>();
            void Push(int x, int y)
            {
                if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
                var i = y * w + x;
                if (seen[i]) return;
                var c = px[i];
                if (Mathf.Abs(c.r - key.r) > 0.16f || Mathf.Abs(c.g - key.g) > 0.16f || Mathf.Abs(c.b - key.b) > 0.18f)
                    return;
                if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.45f) return;
                seen[i] = true;
                c.a = 0f;
                px[i] = c;
                stack.Push(i);
            }

            Push(0, 0);
            Push(w - 1, 0);
            Push(0, h - 1);
            Push(w - 1, h - 1);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                var x = i % w;
                var y = i / w;
                Push(x - 1, y);
                Push(x + 1, y);
                Push(x, y - 1);
                Push(x, y + 1);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = raw.name + "_cut",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject, layer);
        }

        sealed class NaviBob : MonoBehaviour
        {
            RectTransform _rt;
            Vector2 _base;

            public void Bind(RectTransform rt)
            {
                _rt = rt;
                _base = rt.anchoredPosition;
            }

            void Update()
            {
                if (_rt == null) return;
                var y = Mathf.Sin(Time.unscaledTime * 1.7f) * 8f;
                _rt.anchoredPosition = _base + new Vector2(0f, y);
            }
        }

        sealed class NaviFloat : MonoBehaviour
        {
            void LateUpdate()
            {
                var sign = ArPlaymatLayout.PlayerColumnSign;
                var x = ArPlaymatLayout.PlayerHoloX - sign * 0.48f;
                var z = -Mathf.Lerp(
                    ArPlaymatLayout.LiveMonsterRowFromMid,
                    ArPlaymatLayout.LiveSpellTrapRowFromMid,
                    0.7f);
                var bob = Mathf.Sin(Time.time * 1.7f) * 0.04f;
                transform.localPosition = new Vector3(x, 0.92f + bob, z);

                var cam = StageCamera();
                if (cam == null) return;
                var to = transform.position - cam.transform.position;
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(to, Vector3.up);
            }

            Camera StageCamera()
            {
                var mask = 1 << gameObject.layer;
                var cams = Camera.allCameras;
                for (var i = 0; i < cams.Length; i++)
                {
                    var c = cams[i];
                    if (c != null && c.enabled && (c.cullingMask & mask) != 0)
                        return c;
                }
                return Camera.main;
            }
        }
    }
}
