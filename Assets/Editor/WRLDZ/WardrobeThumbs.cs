using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Presentation;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Renders the pictures of the CUSTOMIZE cards with the game's own body, pieces and cel shaders (2026-10-18 AAA pass):
    /// every wardrobe piece worn on each body it fits, every hairstyle, every signature look and both bodies, framed per
    /// slot, on a transparent background, into Assets/Resources/WRLDZ/Avatar3D/Thumbs/ (item_/hair_/look_/body_*.png).
    /// The models are only filmed: nothing about a piece, its mesh or its textures changes.
    /// Run it in Play mode for the idle pose (edit mode films the FBX rest pose, arms out):
    ///   WRLDZ.EditorTools.WardrobeThumbs.Start()            every card
    ///   WRLDZ.EditorTools.WardrobeThumbs.Start("_3d")      only ids containing "_3d"
    ///   WRLDZ.EditorTools.WardrobeThumbs.Status()          progress / log
    /// One job per editor update (Apply, two frames for the pose and the sculpt shape follower, render, write).
    /// </summary>
    public static class WardrobeThumbs
    {
        public const string OutDir = "Assets/Resources/WRLDZ/Avatar3D/Thumbs";
        const int Layer = 29;
        const int Size = 320;
        static readonly Vector3 StagePos = new(0f, -8000f, 0f);

        enum Frame { Full, Head, Face, Bust, Torso, Hands, Legs, Feet }

        sealed class Job
        {
            public string File;
            public AvatarAppearance Look;
            public string RendererName;   // frame this renderer's bounds (ArgonCloth_<id>, ArgonHair_<id>)
            public Frame Frame;
            public float Yaw = -28f;
        }

        static List<Job> _jobs;
        static int _i, _wait;
        static GameObject _root;
        static OverworldAvatar3d _av;
        static bool _avFemale;
        static Camera _cam;
        static RenderTexture _rtBig, _rtSmall;
        static readonly List<string> Log = new();
        static Stopwatch _sw;
        static bool _running;

        public static string Status() =>
            _jobs == null ? "idle" : $"{(_running ? "running" : "done")} {_i}/{_jobs.Count} in {(_sw?.Elapsed.TotalSeconds ?? 0):0}s\n" +
                                       string.Join("\n", Log.GetRange(Math.Max(0, Log.Count - 12), Math.Min(12, Log.Count)));

        public static string Start(string filter = null)
        {
            if (_running) return "already running: " + Status();
            Directory.CreateDirectory(OutDir);
            _jobs = BuildJobs(filter);
            _i = 0; _wait = 0;
            Log.Clear();
            _sw = Stopwatch.StartNew();
            _running = true;
            EditorApplication.update += Tick;
            return $"queued {_jobs.Count} cards ({(Application.isPlaying ? "play mode: idle pose" : "edit mode: rest pose")})";
        }

        public static void Cancel()
        {
            Finish("cancelled");
        }

        static AvatarAppearance Base(bool female)
        {
            var a = AvatarAppearance.Default();
            a.bodyType = female ? 1 : 0;
            a.ClampToCatalog();
            return a;
        }

        static List<Job> BuildJobs(string filter)
        {
            var jobs = new List<Job>();
            bool Want(string file) => string.IsNullOrEmpty(filter) || file.Contains(filter);
            foreach (var female in new[] { false, true })
            {
                var sex = female ? "female" : "male";
                if (Want("body_" + sex)) jobs.Add(new Job { File = "body_" + sex, Look = Base(female), Frame = Frame.Full, Yaw = -18f });
                foreach (var it in ClothingCatalog.All)
                {
                    if (!it.FitsBody(female)) continue;
                    var file = "item_" + it.Id + "_" + sex;
                    if (!Want(file)) continue;
                    var look = Base(female);
                    look.SetSlot(it.Slot, it.Id);
                    var frame = it.Slot switch
                    {
                        ClothingSlot.Hat => Frame.Head,
                        ClothingSlot.Facial => Frame.Face,
                        ClothingSlot.Neck => Frame.Bust,
                        ClothingSlot.Shirt => Frame.Torso,
                        ClothingSlot.Hands => Frame.Hands,
                        ClothingSlot.Bottoms => Frame.Legs,
                        _ => Frame.Feet
                    };
                    jobs.Add(new Job { File = file, Look = look, RendererName = it.EmptySlot ? null : "ArgonCloth_" + it.Id, Frame = frame, Yaw = it.Slot == ClothingSlot.Shoes ? -38f : -28f });
                }
                foreach (var h in AvatarCatalog.HairStyles)
                {
                    var file = "hair_" + h.id + "_" + sex;
                    if (!Want(file)) continue;
                    var look = Base(female);
                    look.hairStyleId = h.id;
                    jobs.Add(new Job { File = file, Look = look, RendererName = "ArgonHair_" + h.id, Frame = Frame.Head, Yaw = -24f });
                }
            }
            foreach (var l in CharacterLooks.All)
            {
                var file = "look_" + l.Id;
                if (!Want(file)) continue;
                var look = AvatarAppearance.Default();
                l.ApplyTo(look);
                look.characterId = "";
                look.silhouette = false;
                look.ClampToCatalog();
                jobs.Add(new Job { File = file, Look = look, Frame = Frame.Full, Yaw = -18f });
            }
            return jobs;
        }

        static void Tick()
        {
            if (!_running) return;
            try
            {
                if (_i >= _jobs.Count) { Finish("done"); return; }
                var job = _jobs[_i];
                if (_wait == 0)
                {
                    Stage(job.Look);
                    _wait = 1;
                    if (!Application.isPlaying) { _av?.EvaluatePreview(0.5f); _wait = 3; }
                    return;
                }
                if (_wait < 3) { _wait++; return; }   // two player frames: the idle pose and the sculpt shape follower
                Render(job);
                _i++;
                _wait = 0;
                // the sculpts are heavy (up to 1.9M triangles, 4096² maps): let go of the ones already filmed
                if (_i % 8 == 0) Resources.UnloadUnusedAssets();
            }
            catch (Exception e)
            {
                Log.Add(_jobs != null && _i < _jobs.Count ? _jobs[_i].File + " FAILED " + e.Message : e.Message);
                Debug.LogWarning("[WardrobeThumbs] " + e);
                _i++;
                _wait = 0;
            }
        }

        static void Stage(AvatarAppearance look)
        {
            if (_root == null)
            {
                _root = new GameObject("WardrobeThumbsStage") { hideFlags = HideFlags.DontSave };
                _root.transform.position = StagePos;
                var camGo = new GameObject("ThumbCam") { hideFlags = HideFlags.DontSave };
                camGo.transform.SetParent(_root.transform, false);
                _cam = camGo.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.orthographic = true;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _cam.cullingMask = 1 << Layer;
                _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 30f;
                _cam.allowHDR = false; _cam.allowMSAA = true;
                _rtBig = new RenderTexture(Size * 2, Size * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                _rtSmall = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
            }
            if (_av == null || look.IsFemale != _avFemale)
            {
                if (_av != null) Object.DestroyImmediate(_av.gameObject);
                _av = OverworldAvatar3d.AttachWorld(_root.transform, look, Layer);
                _avFemale = look.IsFemale;
                if (_av == null) throw new Exception("AttachWorld failed (no base body)");
                _av.Speed = 0f;
                _av.ProceduralBlink = false;   // never film a half-closed eye
                _av.IgnoreBodyHide = true;
                _av.SetPresence(1f);
                var blob = _av.transform.Find("ShadowBlob");
                if (blob != null) blob.gameObject.SetActive(false);
            }
            else _av.Apply(look);
        }

        static bool BoneY(Animator a, HumanBodyBones b, out Vector3 p)
        {
            p = default;
            if (a == null || !a.isHuman) return false;
            var t = a.GetBoneTransform(b);
            if (t == null) return false;
            p = t.position;
            return true;
        }

        static void Render(Job job)
        {
            // Framed from the bones, per slot: a skinned piece's bounds are useless here (the rig root is scaled x100, so
            // the grafted renderers' bounds come out metres wide). The renderer is only checked for being worn.
            if (!string.IsNullOrEmpty(job.RendererName) && FindRenderer(job.RendererName) == null)
            {
                Log.Add(job.File + " skipped: " + job.RendererName + " not grafted");
                return;
            }
            var anim = _av.GetComponent<Animator>();
            var s = Mathf.Max(0.3f, _av.transform.lossyScale.y);
            var root = _root.transform.position;
            Vector3 Bone(HumanBodyBones b, Vector3 fallback) => BoneY(anim, b, out var p) ? p : fallback;
            var head = Bone(HumanBodyBones.Head, root + Vector3.up * 1.55f * s);
            var hips = Bone(HumanBodyBones.Hips, root + Vector3.up * 1.0f * s);
            var chest = Bone(HumanBodyBones.UpperChest, Bone(HumanBodyBones.Chest, root + Vector3.up * 1.3f * s));
            var lf = Bone(HumanBodyBones.LeftFoot, root); var rf = Bone(HumanBodyBones.RightFoot, root);
            // the lowest of the ankles and the balls of the feet: the bases stand on tiptoe in the humanoid idle (their rest
            // pose has the ankle ~15 cm up), so the toes reach lower than the ankles and, a little, under the floor
            var lt = Bone(HumanBodyBones.LeftToes, lf); var rt = Bone(HumanBodyBones.RightToes, rf);
            var foot = Mathf.Min(Mathf.Min(lf.y, rf.y), Mathf.Min(lt.y, rt.y));
            var hand = Bone(HumanBodyBones.LeftHand, hips);   // the hand nearer the three-quarter camera
            float y0, y1; var cx = hips;
            switch (job.Frame)
            {
                case Frame.Head: y0 = head.y - 0.17f * s; y1 = head.y + 0.36f * s; cx = head; break;
                case Frame.Face: y0 = head.y - 0.07f * s; y1 = head.y + 0.21f * s; cx = head; break;
                case Frame.Bust: y0 = chest.y - 0.17f * s; y1 = head.y + 0.08f * s; cx = chest; break;
                case Frame.Torso: y0 = hips.y - 0.30f * s; y1 = head.y + 0.03f * s; cx = chest; break;
                case Frame.Hands: y0 = hand.y - 0.17f * s; y1 = hand.y + 0.17f * s; cx = hand; break;
                case Frame.Legs: y0 = Mathf.Min(foot, root.y) - 0.04f * s; y1 = hips.y + 0.16f * s; cx = hips; break;
                case Frame.Feet:   // from the floor: the foot bone is the ankle, ~15 cm above the sole
                    y0 = Mathf.Min(foot, root.y) - 0.08f * s; y1 = y0 + 0.42f * s; cx = (lf + rf) * 0.5f; break;
                default: y0 = Mathf.Min(foot, root.y) - 0.05f * s; y1 = head.y + 0.36f * s; cx = hips; break;
            }
            var frame = new Bounds(new Vector3(cx.x, (y0 + y1) * 0.5f, cx.z), new Vector3(y1 - y0, y1 - y0, y1 - y0));

            var dir = Quaternion.Euler(0f, job.Yaw, 0f) * Vector3.forward;
            var c = frame.center;
            _cam.transform.position = c + dir * 6f + Vector3.up * 0.25f;
            _cam.transform.LookAt(c);
            // ortho size: the frame's largest screen extent with a margin (the AABB is a little loose at a three-quarter turn)
            _cam.orthographicSize = frame.size.y * 0.5f * 1.04f;
            _cam.aspect = 1f;
            _cam.targetTexture = _rtBig;
            _cam.Render();
            _cam.targetTexture = null;
            Graphics.Blit(_rtBig, _rtSmall);
            var prev = RenderTexture.active;
            RenderTexture.active = _rtSmall;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, job.File + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            if (_i % 20 == 0) Log.Add($"{_i}/{_jobs.Count} {job.File}");
        }

        static Renderer FindRenderer(string name)
        {
            foreach (var r in _av.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name == name && r.gameObject.activeInHierarchy) return r;
            return null;
        }

        static void Finish(string why)
        {
            _running = false;
            EditorApplication.update -= Tick;
            if (_av != null) Object.DestroyImmediate(_av.gameObject);
            if (_root != null) Object.DestroyImmediate(_root);
            if (_rtBig != null) { _rtBig.Release(); Object.DestroyImmediate(_rtBig); }
            if (_rtSmall != null) { _rtSmall.Release(); Object.DestroyImmediate(_rtSmall); }
            _av = null; _root = null; _cam = null; _rtBig = _rtSmall = null;
            Log.Add(why + $" after {(_sw?.Elapsed.TotalSeconds ?? 0):0}s");
            AssetDatabase.Refresh();
        }
    }
}
