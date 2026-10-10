using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Mr. Referobot referees from the sideline at midfield, on the M5 side (away from the
    /// Field zone, banished button and opponent glance by M1). His post is outside every
    /// spot a hologram can stand — both players' monsters, S/T, Field and Pendulum — so he
    /// never stands in the street. When the viewer walks up to him, or he would block the
    /// camera's view of a hologram, the opponent or midfield, he rolls to another post on
    /// the same sideline (never across the field) and returns once his home spot is clear.
    /// He faces the field the whole time — short moves are side-steps or back-pedals, only a long
    /// run turns him — and repositions in dead time, not while an attack or card is live.
    /// He rolls on one self-balancing wheel (<see cref="ReferobotWheelMotion"/>); where he looks and what he
    /// signals is <see cref="ReferobotOfficiating"/>. Mesh local +Z is his face.
    /// </summary>
    public class ReferobotWorldBody : MonoBehaviour
    {
        const float LineSeconds = 4.5f;

        /// <summary>Half his shoulder width — the body he keeps out of the way.</summary>
        const float BodyRadius = 0.35f;
        /// <summary>Gap between the widest hologram footprint and his shoulder.</summary>
        const float SidelineGap = 0.45f;
        /// <summary>How much further out the back-off posts are than the sideline posts.</summary>
        const float BackOffStep = 1.20f;
        /// <summary>Clearance kept from a camera sight line, on top of his body.</summary>
        const float SightGap = 0.20f;
        /// <summary>He steps away when the viewer comes this close (arena-local, flat).</summary>
        const float PersonalSpace = 1.10f;
        const float WalkSpeed = 1.05f;
        const float RunSpeed = 2.30f;
        const float RunWhenFartherThan = 1.40f;
        /// <summary>Speeding up and slowing down (m/s²), so starts and stops are not instant.</summary>
        const float Accel = 2.6f;
        const float Brake = 3.2f;
        /// <summary>Talk nod length when the rig has no talk clip (the wheel rig).</summary>
        const float TalkSeconds = 2.2f;
        const float TurnDegPerSecond = 360f;
        /// <summary>Farther than this he turns and runs; closer he side-steps facing the field.</summary>
        const float TurnAndFollowBeyond = 1.5f;
        /// <summary>Home has to stay clear this long before he walks back to it.</summary>
        const float ReturnHomeAfter = 1.25f;
        /// <summary>While the arena is still anchoring he lands on his post instead of walking.</summary>
        const float SnapForSeconds = 1.0f;

        /// <summary>
        /// Sideline posts as fractions of the S/T row depth, home (midfield) first. No post at
        /// the player's own end: that end of the M5 sideline carries the You LP plate.
        /// </summary>
        static readonly float[] PostDepths = { 0f, 0.5f, 1f, -0.5f };

        internal static int PostCount => PostDepths.Length * 2;

        /// <summary>Nearest a zone centre may be to a post: his body plus the widest standing art.</summary>
        internal static float ZoneClearance => BodyRadius + HoloHalfWidth;

        RectTransform _captionRoot;
        Text _caption;
        float _hideAt;
        ReferobotActor _actor;
        ArArenaHologramManager _arena;
        float _talkLeft;
        float _snapUntil;
        int _post;
        float _homeClearSince = -1f;
        ReferobotOfficiating _officiating;
        ReferobotWheelMotion _wheel;
        DuelEngine _engine;
        float _speed;

        public static ReferobotWorldBody Ensure(Transform arenaRoot, int layer)
        {
            if (arenaRoot == null) return null;
            var existing = arenaRoot.Find("MrReferobotBody");
            if (existing != null)
            {
                var have = existing.GetComponent<ReferobotWorldBody>();
                if (have != null) return have;
                return existing.gameObject.AddComponent<ReferobotWorldBody>();
            }

            var actor = ReferobotActor.TryCreate(arenaRoot, layer);
            if (actor != null)
                return actor.gameObject.AddComponent<ReferobotWorldBody>();

            var go = FreeModelCatalog.SpawnReferobot(arenaRoot, layer);
            if (go == null) return null;
            return go.AddComponent<ReferobotWorldBody>();
        }

        /// <summary>Follow <paramref name="engine"/>'s turns, attacks and verdict.</summary>
        public void Bind(DuelEngine engine)
        {
            _engine = engine;
            _officiating?.Bind(engine);
        }

        void OnEnable()
        {
            MrReferobot.LineSpoken += OnLine;
            MrReferobot.IllegalCalled += OnIllegal;
        }

        void OnDisable()
        {
            MrReferobot.LineSpoken -= OnLine;
            MrReferobot.IllegalCalled -= OnIllegal;
        }

        void Start()
        {
            _actor = GetComponent<ReferobotActor>();
            _arena = GetComponentInParent<ArArenaHologramManager>();
            _snapUntil = Time.time + SnapForSeconds;
            _actor?.Play("idle", true);
            _officiating = new ReferobotOfficiating(transform, _arena);
            _officiating.Bind(_engine);
            _wheel = ReferobotWheelMotion.TryCreate(transform);
            EnsureCaption();
        }

        void OnIllegal() => _officiating?.CallIllegal();

        void OnLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            EnsureCaption();
            if (_caption != null) _caption.text = line;
            if (_captionRoot != null) _captionRoot.gameObject.SetActive(true);
            _hideAt = Time.time + LineSeconds;
            if (_wheel != null)
            {
                _talkLeft = TalkSeconds;
            }
            else if (_actor != null)
            {
                _actor.Play("talk", false);
                _talkLeft = _actor.ClipLength("talk");
            }
        }

        void LateUpdate()
        {
            var cam = ArStageView.FindCamera(transform);
            var haveCam = cam != null && transform.parent != null;
            var camLocal = haveCam ? transform.parent.InverseTransformPoint(cam.transform.position) : Vector3.zero;

            ChoosePost(haveCam, camLocal);
            var speed = MoveToward(Post(_post));
            DriveClips(speed);
            _officiating?.Tick(Time.deltaTime);

            if (_captionRoot == null) return;
            if (Time.time > _hideAt)
            {
                _captionRoot.gameObject.SetActive(false);
                return;
            }

            if (cam == null) return;
            var toCam = _captionRoot.position - cam.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                _captionRoot.rotation = Quaternion.LookRotation(toCam, Vector3.up);
        }

        // ── Where to stand ──────────────────────────────────────────────────

        /// <summary>M5 side of the player's field: +1 = arena +X.</summary>
        static float HomeSide => ArPlaymatLayout.PlayerColumnSign;

        /// <summary>
        /// Arena-local post. 0..3 are the sideline (midfield, toward the opponent, the
        /// opponent's S/T end, toward the player); 4..7 are the same depths backed off.
        /// </summary>
        internal static Vector3 Post(int index)
        {
            var side = HomeSide;
            var backOff = index >= PostDepths.Length ? BackOffStep : 0f;
            var x = side * (FieldEdge(side) + BodyRadius + SidelineGap + backOff);
            var z = PostDepths[index % PostDepths.Length] * ArPlaymatLayout.LiveSpellTrapRowFromMid;
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// Outermost arena-local X (times <paramref name="side"/>) any hologram can reach on
        /// that side: every zone of both players plus half the widest standing art.
        /// </summary>
        static float FieldEdge(float side)
        {
            var edge = 0f;
            for (var zone = 0; zone < 5; zone++)
            {
                edge = Mathf.Max(edge, side * ArPlaymatLayout.MonsterArenaLocal(zone, true).x);
                edge = Mathf.Max(edge, side * ArPlaymatLayout.MonsterArenaLocal(zone, false).x);
                edge = Mathf.Max(edge, side * ArPlaymatLayout.SpellTrapArenaLocal(zone, true).x);
                edge = Mathf.Max(edge, side * ArPlaymatLayout.SpellTrapArenaLocal(zone, false).x);
            }

            edge = Mathf.Max(edge, side * ArPlaymatLayout.FieldSpellArenaLocal(true).x);
            edge = Mathf.Max(edge, side * ArPlaymatLayout.FieldSpellArenaLocal(false).x);
            edge = Mathf.Max(edge, side * ArPlaymatLayout.PendulumArenaLocal(0).x);
            edge = Mathf.Max(edge, side * ArPlaymatLayout.PendulumArenaLocal(1).x);
            return edge + HoloHalfWidth;
        }

        static float HoloHalfWidth =>
            Mathf.Max(CardArtFocus.MonsterArtworkScale.x, ArArenaCardVisual.CardPlateLong) * 0.5f *
            ArPlaymatLayout.LiveHoloScale;

        /// <summary>
        /// Stay on the current post unless something needs it; go home once home has been
        /// clear for <see cref="ReturnHomeAfter"/>.
        /// </summary>
        void ChoosePost(bool haveCam, Vector3 cam)
        {
            var here = transform.localPosition;
            if (_post != 0)
            {
                if (Obstructions(Post(0), haveCam, cam) == 0)
                {
                    if (_homeClearSince < 0f) _homeClearSince = Time.time;
                    // Reposition in dead time, not while an attack or card is live.
                    if (Time.time - _homeClearSince >= ReturnHomeAfter &&
                        (_officiating == null || !_officiating.PlayIsLive))
                    {
                        _post = 0;
                        _homeClearSince = -1f;
                    }
                }
                else
                {
                    _homeClearSince = -1f;
                }
            }

            if (Obstructions(Post(_post), haveCam, cam) == 0) return;

            var best = _post;
            var bestHits = int.MaxValue;
            var bestCost = float.MaxValue;
            for (var i = 0; i < PostCount; i++)
            {
                var post = Post(i);
                var hits = Obstructions(post, haveCam, cam);
                var cost = FlatDistance(here, post) +
                           0.6f * Mathf.Abs(PostDepths[i % PostDepths.Length]) +
                           (i >= PostDepths.Length ? 0.8f : 0f);
                // Do not walk past the viewer he is getting away from.
                if (haveCam &&
                    FlatSegmentDistance(here, post, cam) < Mathf.Min(FlatDistance(here, cam), PersonalSpace) - 0.05f)
                    cost += 2f;
                if (hits < bestHits || (hits == bestHits && cost < bestCost))
                {
                    best = i;
                    bestHits = hits;
                    bestCost = cost;
                }
            }

            _post = best;
            _homeClearSince = -1f;
        }

        /// <summary>How many things standing at <paramref name="post"/> would get in the way of.</summary>
        int Obstructions(Vector3 post, bool haveCam, Vector3 cam)
        {
            var hits = 0;
            if (haveCam)
            {
                if (FlatDistance(post, cam) < PersonalSpace) hits++;
                // Midfield and the opponent stay in view even before anything is summoned.
                if (BlocksSight(cam, Vector3.zero, 0f, post)) hits++;
                var opp = new Vector3(ArPlaymatLayout.OppHoloX, 0f,
                    ArPlaymatLayout.LiveSpellTrapRowFromMid + ArPlaymatLayout.LiveStandOff);
                if (BlocksSight(cam, opp, BodyRadius, post)) hits++;
            }

            if (_arena != null)
            {
                hits += HologramObstructions(_arena.MonsterPlane, post, haveCam, cam);
                hits += HologramObstructions(_arena.SpellTrapPlane, post, haveCam, cam);
            }

            return hits;
        }

        int HologramObstructions(Transform plane, Vector3 post, bool haveCam, Vector3 cam)
        {
            if (plane == null || transform.parent == null) return 0;
            var hits = 0;
            var half = HoloHalfWidth;
            for (var i = 0; i < plane.childCount; i++)
            {
                var holo = plane.GetChild(i);
                if (!holo.gameObject.activeInHierarchy) continue;
                var at = transform.parent.InverseTransformPoint(holo.position);
                // A hologram sliding past (presentations, removals) — give it room.
                if (FlatDistance(post, at) < BodyRadius + SightGap + half) hits++;
                if (haveCam && BlocksSight(cam, at, half, post)) hits++;
            }

            return hits;
        }

        /// <summary>
        /// True when a body at <paramref name="post"/> sits inside the view cone from
        /// <paramref name="eye"/> to a target of <paramref name="targetHalfWidth"/> (flat XZ).
        /// </summary>
        static bool BlocksSight(Vector3 eye, Vector3 target, float targetHalfWidth, Vector3 post)
        {
            var a = new Vector2(eye.x, eye.z);
            var ab = new Vector2(target.x, target.z) - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return false;
            var q = new Vector2(post.x, post.z);
            var t = Vector2.Dot(q - a, ab) / len2;
            // Behind the viewer, or behind what they are looking at.
            if (t <= 0f || t >= 1f) return false;
            var miss = (a + ab * t - q).magnitude;
            return miss < BodyRadius + SightGap + targetHalfWidth * t;
        }

        /// <summary>Closest flat distance from <paramref name="p"/> to the path a→b.</summary>
        static float FlatSegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            var a2 = new Vector2(a.x, a.z);
            var ab = new Vector2(b.x, b.z) - a2;
            var q = new Vector2(p.x, p.z);
            var len2 = ab.sqrMagnitude;
            var t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a2, ab) / len2);
            return (a2 + ab * t - q).magnitude;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ── Getting there ───────────────────────────────────────────────────

        /// <summary>Walk (or run) to the post; returns ground speed for the clips.</summary>
        float MoveToward(Vector3 target)
        {
            var from = transform.localPosition;
            var gap = FlatDistance(from, target);
            // Opening placement, or the disk moved to the other arm and flipped his sideline:
            // land directly instead of walking through the holograms.
            var snap = Time.time < _snapUntil || Mathf.Sign(from.x) != Mathf.Sign(target.x);
            float speed;
            Vector3 to;
            if (snap)
            {
                to = target;
                speed = 0f;
                _speed = 0f;
                // A landing is not a roll: no lean or wheel spin from the jump.
                _wheel?.Settle();
            }
            else
            {
                // Speed up and brake smoothly, arriving at rest on the post.
                var pace = gap > RunWhenFartherThan ? RunSpeed : WalkSpeed;
                var canStopFrom = Mathf.Sqrt(2f * Brake * gap);
                var wantSpeed = Mathf.Min(pace, canStopFrom);
                _speed = Mathf.MoveTowards(_speed, wantSpeed, (wantSpeed > _speed ? Accel : Brake) * Time.deltaTime);
                if (gap < 0.002f) _speed = 0f;
                to = Vector3.MoveTowards(from, target, _speed * Time.deltaTime);
                speed = Time.deltaTime > 0f ? FlatDistance(from, to) / Time.deltaTime : 0f;
            }

            transform.localPosition = to;

            // Never turn his back on play: short moves side-step / back-pedal with the chest on the
            // field; only a long run turns him toward travel (the head keeps watching either way).
            var field = FieldFacing(to);
            var travel = new Vector3(target.x - to.x, 0f, target.z - to.z);
            var face = field;
            if (!snap && travel.sqrMagnitude > 0.0004f)
            {
                var step = gap > TurnAndFollowBeyond ? travel.normalized : travel.normalized + field * 1.2f;
                if (step.sqrMagnitude > 1e-4f) face = step.normalized;
            }

            var want = Quaternion.LookRotation(face, Vector3.up);
            transform.localRotation = snap
                ? want
                : Quaternion.RotateTowards(transform.localRotation, want, TurnDegPerSecond * Time.deltaTime);
            return speed;
        }

        /// <summary>
        /// Square to the field so both duelists stay in view, turned part way toward the duelist whose
        /// turn it is. The head does the rest of the looking.
        /// </summary>
        Vector3 FieldFacing(Vector3 at)
        {
            var look = Vector3.zero;
            if (_officiating != null && _officiating.TryGetBodyFocus(out var focus, out var share))
                look = Vector3.Lerp(look, focus, share);
            var face = new Vector3(look.x - at.x, 0f, look.z - at.z);
            return face.sqrMagnitude > 1e-4f ? face.normalized : new Vector3(-Mathf.Sign(at.x), 0f, 0f);
        }

        void DriveClips(float speed)
        {
            if (_wheel != null)
            {
                // Rolling rig: no clips, the wheel, lean and balance are procedural.
                if (_talkLeft > 0f) _talkLeft -= Time.deltaTime;
                _wheel.Tick(Time.deltaTime, _talkLeft > 0f);
                return;
            }

            if (_actor == null) return;
            if (_talkLeft > 0f)
            {
                _talkLeft -= Time.deltaTime;
                _actor.Play("talk", false);
                return;
            }

            if (speed > (WalkSpeed + RunSpeed) * 0.5f) _actor.Play("run", true);
            else if (speed > 0.06f) _actor.Play("walk", true);
            else _actor.Play("idle", true);
        }

        // ── Caption ─────────────────────────────────────────────────────────

        void EnsureCaption()
        {
            if (_captionRoot != null) return;
            var go = new GameObject("Line", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _captionRoot = go.GetComponent<RectTransform>();
            _captionRoot.sizeDelta = new Vector2(920f, 150f);
            _captionRoot.localScale = Vector3.one * 0.00115f;
            _captionRoot.localPosition = new Vector3(0f, 1.68f, 0f);
            ApplyLayer(go);

            var textGo = new GameObject("Words", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            _caption = textGo.GetComponent<Text>();
            _caption.font = WrldzType.Body();
            _caption.fontSize = 64;
            _caption.fontStyle = FontStyle.Bold;
            _caption.alignment = TextAnchor.MiddleCenter;
            _caption.color = new Color(0.96f, 0.93f, 0.84f, 1f);
            _caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            _caption.verticalOverflow = VerticalWrapMode.Truncate;
            _caption.raycastTarget = false;
            _caption.text = "";
            ApplyLayer(textGo);
            go.SetActive(false);
        }

        void ApplyLayer(GameObject go)
        {
            go.layer = gameObject.layer;
            foreach (Transform t in go.transform)
                ApplyLayer(t.gameObject);
        }
    }
}
