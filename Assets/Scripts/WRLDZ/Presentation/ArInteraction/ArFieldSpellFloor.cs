using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Active Field Spell as two large illustration walls on the player's left
    /// and right of the street. Midfield stays empty air — no floor carpet, no
    /// far backdrop. Disk still holds the physical card.
    /// </summary>
    [DefaultExecutionOrder(750)]
    public class ArFieldSpellFloor : MonoBehaviour
    {
        const float WallHeight = 1.425f;
        const float WallAlpha = 0.52f;
        const float GroundLift = 0.03f;
        const int ArcSegs = 14;
        const int HeightSegs = 1;

        int _layer;
        MeshRenderer _leftMr;
        MeshRenderer _rightMr;
        Material _leftMat;
        Material _rightMat;
        int _artId = int.MinValue;
        bool _lit;

        public Transform Root => transform;

        public static ArFieldSpellFloor Create(Transform arenaRoot, int layer)
        {
            var go = new GameObject("FieldSpellFloor");
            go.transform.SetParent(arenaRoot, false);
            go.layer = layer;
            var f = go.AddComponent<ArFieldSpellFloor>();
            f._layer = layer;
            f.Build();
            return f;
        }

        void Build()
        {
            _leftMat = MakeArtMat("FieldWrapLeft");
            _rightMat = MakeArtMat("FieldWrapRight");
            _leftMr = MakeWall("WrapL", _leftMat, playerLeft: true);
            _rightMr = MakeWall("WrapR", _rightMat, playerLeft: false);
            SetLit(false);
        }

        static Material MakeArtMat(string name)
        {
            var mat = ArAnimePresentation.MakeFaceMaterial(Texture2D.whiteTexture, Color.white, name);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_DOUBLESIDED_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 2455;
            var c = new Color(1f, 1f, 1f, WallAlpha);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            return mat;
        }

        MeshRenderer MakeWall(string name, Material mat, bool playerLeft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = _layer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SideWrap(playerLeft);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            return mr;
        }

        public void Sync(CardInstance youField, CardInstance oppField, CardDatabase db)
        {
            var live = Live(youField) ?? Live(oppField);
            if (live == null)
            {
                SetLit(false);
                _artId = int.MinValue;
                return;
            }

            // Player faces +Z (opponent). Left = −X, right = +X of the street.
            var halfW = ArPlaymatLayout.MonsterColumnPitch * 2f + 0.72f;
            var halfZ = ArPlaymatLayout.LiveSpellTrapRowFromMid + 0.55f;
            var x = (ArPlaymatLayout.PlayerHoloX + ArPlaymatLayout.OppHoloX) * 0.5f;

            Place(_leftMr.transform, x, halfW, halfZ);
            Place(_rightMr.transform, x, halfW, halfZ);
            if (live.CardId != _artId)
            {
                _artId = live.CardId;
                CardArtFocus.ApplyMonsterArtwork(_leftMat, db, live.CardId);
                CardArtFocus.ApplyMonsterArtwork(_rightMat, db, live.CardId);
            }

            Tint(_leftMat);
            Tint(_rightMat);
            if (_leftMr != null) _leftMr.sharedMaterial = _leftMat;
            if (_rightMr != null) _rightMr.sharedMaterial = _rightMat;
            SetLit(true);
        }

        static void Place(Transform t, float x, float halfW, float halfZ)
        {
            t.localPosition = new Vector3(x, GroundLift, 0f);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(halfW, WallHeight, halfZ);
        }

        static CardInstance Live(CardInstance c)
        {
            if (c == null || !c.FaceUp || c.Def == null) return null;
            return c.Def.IsFieldSpell ? c : null;
        }

        static void Tint(Material mat)
        {
            if (mat == null) return;
            var c = new Color(1f, 1f, 1f, WallAlpha);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        void SetLit(bool on)
        {
            _lit = on;
            SetVisible(_leftMr, on);
            SetVisible(_rightMr, on);
        }

        static void SetVisible(MeshRenderer mr, bool on)
        {
            if (mr != null && mr.gameObject.activeSelf != on)
                mr.gameObject.SetActive(on);
        }

        /// <summary>
        /// Unit elliptical side wall. Player looks toward +Z; left is −X.
        /// Bows around the long edge so the picture wraps the street instead of
        /// sitting as a flat poster in the aisle.
        /// </summary>
        static Mesh SideWrap(bool playerLeft)
        {
            var sign = playerLeft ? -1f : 1f;
            // ±35° around the left/right axis — half the previous wrap so the
            // illustration is not stretched along the whole street.
            const float span = 35f * Mathf.Deg2Rad;
            var mid = playerLeft ? Mathf.PI : 0f;
            var a0 = mid - span;
            var a1 = mid + span;

            var nx = ArcSegs + 1;
            var ny = HeightSegs + 1;
            var v = new Vector3[nx * ny];
            var uv = new Vector2[nx * ny];
            var t = new int[ArcSegs * HeightSegs * 6];

            for (var iy = 0; iy < ny; iy++)
            {
                var vv = iy / (float)HeightSegs;
                for (var ix = 0; ix < nx; ix++)
                {
                    var u = ix / (float)ArcSegs;
                    var a = Mathf.Lerp(a0, a1, u);
                    var i = iy * nx + ix;
                    v[i] = new Vector3(Mathf.Cos(a), vv, Mathf.Sin(a));
                    // Keep the illustration upright and reading left→right on
                    // both walls when viewed from inside the street.
                    uv[i] = new Vector2(playerLeft ? 1f - u : u, vv);
                }
            }

            var ti = 0;
            for (var iy = 0; iy < HeightSegs; iy++)
            {
                for (var ix = 0; ix < ArcSegs; ix++)
                {
                    var i0 = iy * nx + ix;
                    var i1 = i0 + 1;
                    var i2 = i0 + nx;
                    var i3 = i2 + 1;
                    if (sign < 0f)
                    {
                        t[ti++] = i0;
                        t[ti++] = i2;
                        t[ti++] = i1;
                        t[ti++] = i1;
                        t[ti++] = i2;
                        t[ti++] = i3;
                    }
                    else
                    {
                        t[ti++] = i0;
                        t[ti++] = i1;
                        t[ti++] = i2;
                        t[ti++] = i1;
                        t[ti++] = i3;
                        t[ti++] = i2;
                    }
                }
            }

            var mesh = new Mesh { name = playerLeft ? "FieldWrapL" : "FieldWrapR" };
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
