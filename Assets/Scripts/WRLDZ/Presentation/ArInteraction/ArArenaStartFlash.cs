using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// White light on the Solid Vision arena border during the duel-start
    /// hologram-projection sting. Brightness follows the clip RMS envelope
    /// (crescendo ~81%, then fade).
    /// </summary>
    public class ArArenaStartFlash : MonoBehaviour
    {
        LineRenderer _line;
        Material _mat;
        int _layer;
        bool _lit;

        public static ArArenaStartFlash Ensure(Transform arenaRoot, int layer)
        {
            if (arenaRoot == null) return null;
            var existing = arenaRoot.GetComponent<ArArenaStartFlash>();
            if (existing != null) return existing;
            var fx = arenaRoot.gameObject.AddComponent<ArArenaStartFlash>();
            fx._layer = layer;
            fx.Build();
            return fx;
        }

        void Build()
        {
            var go = new GameObject("ArenaStartBorder");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.layer = _layer;
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.positionCount = 4;
            _line.numCapVertices = 2;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.textureMode = LineTextureMode.Stretch;
            _line.alignment = LineAlignment.View;
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
            _mat = new Material(sh) { name = "ArenaStartBorderMat" };
            _mat.SetOverrideTag("RenderType", "Transparent");
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _mat.SetInt("_ZWrite", 0);
            _mat.renderQueue = 3100;
            if (_mat.HasProperty("_Cull")) _mat.SetFloat("_Cull", 0f);
            _line.sharedMaterial = _mat;
            _line.enabled = false;
            Layout();
            Paint(0f);
        }

        void Layout()
        {
            if (_line == null) return;
            var halfZ = ArPlaymatLayout.LiveSpellTrapRowFromMid + 0.42f;
            var halfX = ArPlaymatLayout.MonsterColumnPitch * 2.45f + 0.35f;
            _line.SetPosition(0, new Vector3(-halfX, 0f, -halfZ));
            _line.SetPosition(1, new Vector3(halfX, 0f, -halfZ));
            _line.SetPosition(2, new Vector3(halfX, 0f, halfZ));
            _line.SetPosition(3, new Vector3(-halfX, 0f, halfZ));
        }

        void LateUpdate()
        {
            if (_line == null) return;
            if (!WrldzAudio.TryDuelBeginGlow(out _, out var env))
            {
                if (_lit)
                {
                    Paint(0f);
                    _line.enabled = false;
                    _lit = false;
                }

                return;
            }

            Layout();
            _line.enabled = true;
            _lit = true;
            Paint(env);
        }

        void Paint(float envelope)
        {
            var e = Mathf.Clamp01(envelope);
            var width = Mathf.Lerp(0.045f, 0.22f, e) * Mathf.Max(0.45f, ArPlaymatLayout.LiveHoloScale);
            _line.startWidth = width;
            _line.endWidth = width;
            var a = Mathf.Clamp01(e * 1.15f);
            var col = new Color(1f, 1f, 1f, a);
            if (_mat != null)
            {
                if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", col);
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", col);
            }

            _line.startColor = col;
            _line.endColor = col;
        }
    }
}
