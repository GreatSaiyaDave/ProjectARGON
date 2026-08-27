using System.Collections;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Floating hologram card in the torso hand volume or locked on a disk zone.
    /// Hand pose uses a soft spring (not hard snap). Drag is free-roam on a plane.
    /// </summary>
    public class ArFloatingCard : MonoBehaviour
    {
        public CardInstance Card;
        public CardDatabase Db;
        public bool IsLockedToZone;
        public ArDiskZone LockedZone;
        public bool IsDragging;
        public int Layer = 28;

        MeshRenderer _mr;
        Material _mat;
        Vector3 _handRestLocal;
        Quaternion _handRestRot;
        Vector3 _handRestScale = Vector3.one * 0.12f;
        Coroutine _snapCo;
        /// <summary>True while a snap coroutine is easing into a lock/rest pose.</summary>
        public bool IsSnapAnimating => _snapCo != null;
        /// <summary>True while this card is flying out of the disk deck well.</summary>
        public bool IsDrawArriving { get; private set; }

        /// <summary>Opponent / hidden hand — never flip to the face during a draw.</summary>
        public bool KeepFaceHidden;

        public void MarkDrawArriving() => IsDrawArriving = true;
        ArDuelDiskRig _drawDeck;
        Vector3 _drawStartScale = Vector3.one * 0.04f;
        float _bobPhase;
        /// <summary>Soft offset from rest after free-roam — decays gently.</summary>
        Vector3 _freeOffsetLocal;
        bool _hasRest;

        /// <summary>How strongly idle cards spring home (higher = snappier, lower = freer).</summary>
        const float IdleSpring = 7.5f;
        /// <summary>Max free offset kept after a failed drop (stage units).</summary>
        const float FreeLeash = 0.08f;

        public static ArFloatingCard Spawn(Transform parent, CardInstance card, CardDatabase db,
            int layer, Color tint)
        {
            var go = new GameObject(card != null ? $"Float_{card.Name}" : "FloatCard");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var fc = go.AddComponent<ArFloatingCard>();
            fc.Card = card;
            fc.Db = db;
            fc.Layer = layer;
            fc.BuildVisual(tint);
            // Local-space size × CardScale → world pick volume
            var col = go.AddComponent<BoxCollider>();
            // Tight pick volume — a fat Z box made overlapping fan cards steal taps.
            col.size = new Vector3(0.80f, 1.18f, 0.08f);
            col.center = Vector3.zero;
            return fc;
        }

        void BuildVisual(Color tint)
        {
            // Anime physical card (2D art on 3D body) — default until per-monster meshes ship
            var art = Db != null && Card != null ? Db.GetArt(Card.CardId) : null;
            var faceTex = art != null && art.texture != null ? art.texture : Texture2D.whiteTexture;
            var backSpr = StreamingSprite.CardBack()
                          ?? ImagineAssets.CardBackWrldz()
                          ?? YgoCardFrames.CardBack();
            var backTex = backSpr != null ? backSpr.texture : Texture2D.grayTexture;

            var parts = ArPhysicalCardBuilder.Build(transform, Layer, faceTex, backTex, tint);
            if (parts.Root != null)
            {
                // Hand scale is applied on the floating root; keep mesh unit size
                parts.Root.localScale = Vector3.one;
            }

            _mr = parts.FaceRenderer;
            _mat = parts.FaceMaterial;
            if (_mr != null)
                ArAnimePresentation.ConfigureHoloRenderer(_mr);
            RefreshArtFace(showFace: true);
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        /// <summary>
        /// Update fan rest pose. Does not hard-teleport — idle spring eases toward it.
        /// </summary>
        public void SetHandRest(Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            _handRestLocal = localPos;
            _handRestRot = localRot;
            _handRestScale = localScale;
            if (!_hasRest && !IsLockedToZone && !IsDragging && !IsDrawArriving)
            {
                transform.localPosition = localPos;
                transform.localRotation = localRot;
                transform.localScale = localScale;
                _freeOffsetLocal = Vector3.zero;
            }

            _hasRest = true;
        }

        public void ReturnToHandRest()
        {
            IsDragging = false;
            if (IsLockedToZone) return;
            // Capture residual free offset from current pose (light freeroam leftover)
            var delta = transform.localPosition - _handRestLocal;
            if (delta.magnitude > FreeLeash)
                delta = delta.normalized * FreeLeash;
            _freeOffsetLocal = delta * 0.45f; // keep a little freeroam feel, then spring home
            AnimateSnap(_handRestLocal + _freeOffsetLocal, _handRestRot, _handRestScale, worldSpace: false);
        }

        public void AnimateSnap(Vector3 localPos, Quaternion localRot, Vector3 localScale,
            bool worldSpace = false, float duration = -1f)
        {
            if (_snapCo != null) StopCoroutine(_snapCo);
            _snapCo = StartCoroutine(SnapRoutine(localPos, localRot, localScale, worldSpace, duration));
        }

        IEnumerator SnapRoutine(Vector3 pos, Quaternion rot, Vector3 scale, bool worldSpace,
            float duration)
        {
            var startP = worldSpace ? transform.position : transform.localPosition;
            var startR = worldSpace ? transform.rotation : transform.localRotation;
            var startS = transform.localScale;
            // Solid physical pace (not instant) — default from ArDiskMotion
            var dur = duration > 0f ? duration : ArDiskMotion.PadSnap;
            var u = 0f;
            while (u < 1f)
            {
                u += Time.unscaledDeltaTime / dur;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                if (worldSpace)
                {
                    transform.position = Vector3.Lerp(startP, pos, e);
                    transform.rotation = Quaternion.Slerp(startR, rot, e);
                }
                else
                {
                    transform.localPosition = Vector3.Lerp(startP, pos, e);
                    transform.localRotation = Quaternion.Slerp(startR, rot, e);
                }

                transform.localScale = Vector3.Lerp(startS, scale, e);
                yield return null;
            }

            if (!worldSpace)
            {
                transform.localPosition = pos;
                transform.localRotation = rot;
            }
            else
            {
                transform.position = pos;
                transform.rotation = rot;
            }

            transform.localScale = scale;
            _snapCo = null;
        }

        /// <summary>
        /// Peel this card off the live disk magazine and fly it into the hand rest.
        /// The path re-samples the deck and hand every frame so a moving disk still
        /// reads as one card leaving the well.
        /// </summary>
        public void PlayDrawFromDeck(ArDuelDiskRig deck, Vector3 startLocalScale, float delay = 0f)
        {
            IsDrawArriving = true;
            _drawDeck = deck;
            _drawStartScale = startLocalScale.sqrMagnitude > 1e-6f ? startLocalScale : Vector3.one * 0.04f;
            if (delay > 0.001f)
            {
                transform.localPosition = _handRestLocal;
                transform.localRotation = _handRestRot;
                transform.localScale = Vector3.one * 0.001f;
            }
            else if (TrySampleDeck(out var p, out var r, out _))
            {
                transform.position = p;
                transform.rotation = r;
                transform.localScale = _drawStartScale;
            }

            RefreshArtFace(showFace: false);
            if (_snapCo != null) StopCoroutine(_snapCo);
            _snapCo = StartCoroutine(DrawFromDeckCo(delay));
        }

        /// <summary>Fallback: rest pose fade-in when a deck origin is not available.</summary>
        public void PlayFadeInAtRest(float delay = 0f)
        {
            IsDrawArriving = true;
            if (_snapCo != null) StopCoroutine(_snapCo);
            _snapCo = StartCoroutine(FadeInAtRestCo(delay));
        }

        IEnumerator DrawFromDeckCo(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            if (this == null) yield break;

            TrySampleDeck(out var startP, out var startR, out _);
            transform.position = startP;
            transform.rotation = startR;
            transform.localScale = _drawStartScale;
            RefreshArtFace(showFace: false);

            var parent = transform.parent;
            var startS = _drawStartScale;

            // 1) Peel: lift along the magazine's own up so a rotated disk still reads.
            var u = 0f;
            var liftDur = ArDiskMotion.DrawLift;
            while (u < 1f)
            {
                if (this == null) yield break;
                u += Time.unscaledDeltaTime / liftDur;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                TrySampleDeck(out var p, out var r, out _);
                var up = r * Vector3.up;
                var peel = p + up * 0.052f;
                transform.position = Vector3.Lerp(p, peel, e);
                transform.rotation = Quaternion.Slerp(r, r * Quaternion.Euler(-32f, 0f, 8f), e);
                transform.localScale = startS;
                yield return null;
            }

            // 2) Travel: quadratic arc from the live peel pose to the live hand rest.
            u = 0f;
            var travelDur = ArDiskMotion.DrawTravel;
            var flipped = false;
            while (u < 1f)
            {
                if (this == null) yield break;
                u += Time.unscaledDeltaTime / travelDur;
                var k = Mathf.Clamp01(u);
                var e = k * k * (3f - 2f * k);
                TrySampleDeck(out var p, out var r, out _);
                var from = p + r * Vector3.up * 0.052f;
                Vector3 to;
                Quaternion endR;
                if (parent != null)
                {
                    to = parent.TransformPoint(_handRestLocal);
                    endR = parent.rotation * _handRestRot;
                }
                else
                {
                    to = _handRestLocal;
                    endR = _handRestRot;
                }

                var span = to - from;
                var arcH = Mathf.Clamp(span.magnitude * 0.32f, 0.07f, 0.18f);
                var mid = from + span * 0.48f + Vector3.up * arcH;
                transform.position = QuadBezier(from, mid, to, e);
                transform.rotation = Quaternion.Slerp(r * Quaternion.Euler(-32f, 0f, 8f), endR, e);
                transform.localScale = Vector3.Lerp(startS, _handRestScale, e);

                if (!flipped && e >= 0.58f)
                {
                    flipped = true;
                    RefreshArtFace(showFace: !KeepFaceHidden);
                }

                yield return null;
            }

            if (parent != null)
            {
                transform.localPosition = _handRestLocal;
                transform.localRotation = _handRestRot;
            }

            transform.localScale = _handRestScale;
            RefreshArtFace(showFace: !KeepFaceHidden);
            _freeOffsetLocal = Vector3.zero;
            IsDrawArriving = false;
            _snapCo = null;
        }

        bool TrySampleDeck(out Vector3 pos, out Quaternion rot, out Vector3 worldScale)
        {
            if (_drawDeck != null && _drawDeck.TryGetDrawOriginWorldPose(out pos, out rot, out worldScale))
                return true;
            pos = transform.position;
            rot = transform.rotation;
            worldScale = _drawStartScale;
            return false;
        }

        static Vector3 QuadBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            var u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        IEnumerator FadeInAtRestCo(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            if (this == null) yield break;
            transform.localPosition = _handRestLocal;
            transform.localRotation = _handRestRot;
            transform.localScale = _handRestScale * 0.82f;
            RefreshArtFace(showFace: !KeepFaceHidden);
            var u = 0f;
            while (u < 1f)
            {
                if (this == null) yield break;
                u += Time.unscaledDeltaTime / 0.28f;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                transform.localScale = Vector3.Lerp(_handRestScale * 0.82f, _handRestScale, e);
                yield return null;
            }

            transform.localScale = _handRestScale;
            IsDrawArriving = false;
            _snapCo = null;
        }

        void OnDisable()
        {
            if (!IsDrawArriving) return;
            IsDrawArriving = false;
            if (_snapCo != null)
            {
                StopCoroutine(_snapCo);
                _snapCo = null;
            }

            if (_hasRest && !IsLockedToZone)
            {
                transform.localPosition = _handRestLocal;
                transform.localRotation = _handRestRot;
                transform.localScale = _handRestScale;
                RefreshArtFace(showFace: !KeepFaceHidden);
            }
        }

        void Update()
        {
            if (IsLockedToZone || IsDragging || !_hasRest || IsDrawArriving) return;
            if (_snapCo != null) return;

            // Soft spring toward rest + free offset (not glued)
            _freeOffsetLocal = Vector3.Lerp(_freeOffsetLocal, Vector3.zero, Time.deltaTime * 1.8f);
            var bob = Mathf.Sin(Time.time * 2.0f + _bobPhase) * 0.006f;
            var target = _handRestLocal + _freeOffsetLocal + new Vector3(0f, bob, 0f);
            var k = 1f - Mathf.Exp(-IdleSpring * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, k);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, _handRestRot, k);
            transform.localScale = Vector3.Lerp(transform.localScale, _handRestScale, k);
        }

        public void RefreshArtFace(bool showFace)
        {
            if (_mat == null) return;
            Texture tex;
            if (showFace && Card != null && Db != null)
            {
                var art = Db.GetArt(Card.CardId);
                tex = art != null ? art.texture : Texture2D.whiteTexture;
            }
            else
            {
                var back = WRLDZ.Presentation.StreamingSprite.CardBack()
                           ?? WRLDZ.Presentation.YgoCardFrames.CardBack()
                           ?? WRLDZ.Presentation.ImagineAssets.CardBackWrldz();
                tex = back != null ? back.texture : Texture2D.grayTexture;
            }

            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", tex);
            if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", tex);
            var col = ArAnimePresentation.Expose(Color.white);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", col);
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", col);
        }
    }
}
