// Minimal UnityEngine compatibility shim for running the WRLDZ rules engine
// headlessly (outside the Unity Editor). This file lives OUTSIDE the Unity
// Assets/ tree, so Unity never compiles it and there is no conflict with the
// real UnityEngine assemblies. Only the tiny API surface the engine + tests
// actually use is implemented; graphics types are inert stand-ins used solely
// so the (never-exercised) card-art code paths compile.
using System;
using System.Diagnostics;
using System.Text.Json;

namespace UnityEngine
{
    public static class Debug
    {
        public static bool Silence = false;
        public static void Log(object m) { if (!Silence) Console.WriteLine("[Log] " + m); }
        public static void LogWarning(object m) { if (!Silence) Console.WriteLine("[Warn] " + m); }
        public static void LogError(object m) { if (!Silence) Console.Error.WriteLine("[Error] " + m); }
        public static void LogFormat(string f, params object[] a) { if (!Silence) Console.WriteLine("[Log] " + string.Format(f, a)); }
        public static void Assert(bool c, object m = null) { if (!c && !Silence) Console.Error.WriteLine("[Assert] " + m); }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Epsilon = 1.401298E-45f;

        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static float Round(float v) => (float)Math.Round(v, MidpointRounding.AwayFromZero);
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Sign(float v) => v < 0f ? -1f : 1f;
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f * Math.Max(1f, Math.Max(Math.Abs(a), Math.Abs(b)));
        public static float Repeat(float t, float length) { var r = t - (float)Math.Floor(t / length) * length; return Clamp(r, 0f, length); }
    }

    public static class Application
    {
        // Set by the headless entrypoint (Program.Main) to the repo's Assets/StreamingAssets.
        public static string streamingAssetsPath = "";
        public static string persistentDataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wrldz-headless");
        public static string dataPath = "";
        public static string temporaryCachePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wrldz-headless-cache");
        public static bool isBatchMode = true;
        public static bool isEditor = false;
        public static bool isPlaying = true;
    }

    public static class JsonUtility
    {
        // Unity's JsonUtility serializes FIELDS only (not properties) and does not
        // aggressively escape text. Mirror that so generated files (e.g. the compiled
        // effects seed) match the Editor-produced format and stay clean.
        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IgnoreReadOnlyProperties = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers = { FieldsOnly },
            },
        };

        // Drop property-backed members so only public fields are (de)serialized, matching
        // UnityEngine.JsonUtility semantics.
        static void FieldsOnly(System.Text.Json.Serialization.Metadata.JsonTypeInfo ti)
        {
            if (ti.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object) return;
            for (var i = ti.Properties.Count - 1; i >= 0; i--)
            {
                if (ti.Properties[i].AttributeProvider is System.Reflection.PropertyInfo)
                    ti.Properties.RemoveAt(i);
            }
        }

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            try { return JsonSerializer.Deserialize<T>(json, Opts); }
            catch (Exception e) { Debug.LogWarning("[JsonUtility] FromJson<" + typeof(T).Name + "> failed: " + e.Message); return default; }
        }

        public static object FromJson(string json, Type t)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonSerializer.Deserialize(json, t, Opts); }
            catch (Exception e) { Debug.LogWarning("[JsonUtility] FromJson(" + t.Name + ") failed: " + e.Message); return null; }
        }

        public static string ToJson(object obj) => obj == null ? "{}" : JsonSerializer.Serialize(obj, obj.GetType(), Opts);
        public static string ToJson(object obj, bool pretty)
        {
            if (obj == null) return "{}";
            var o = new JsonSerializerOptions(Opts) { WriteIndented = pretty };
            return JsonSerializer.Serialize(obj, obj.GetType(), o);
        }
    }

    public static class Random
    {
        static System.Random _rng = new System.Random();
        public static void InitState(int seed) => _rng = new System.Random(seed);
        public static int seed { set { _rng = new System.Random(value); } }
        // Unity int overload: maxExclusive. float overload: maxInclusive.
        public static int Range(int minInclusive, int maxExclusive) =>
            maxExclusive <= minInclusive ? minInclusive : _rng.Next(minInclusive, maxExclusive);
        public static float Range(float minInclusive, float maxInclusive) =>
            minInclusive + (float)_rng.NextDouble() * (maxInclusive - minInclusive);
        public static float value => (float)_rng.NextDouble();
    }

    public static class Time
    {
        static readonly Stopwatch _sw = Stopwatch.StartNew();
        public static float unscaledTime => (float)_sw.Elapsed.TotalSeconds;
        public static float time => (float)_sw.Elapsed.TotalSeconds;
        public static float realtimeSinceStartup => (float)_sw.Elapsed.TotalSeconds;
        public static float deltaTime => 0.016f;
        public static float unscaledDeltaTime => 0.016f;
    }

    [Serializable]
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
        public override string ToString() => $"({x}, {y})";
    }

    [Serializable]
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new Vector2Int(0, 0);
        public static Vector2Int one => new Vector2Int(1, 1);
        public override string ToString() => $"({x}, {y})";
        public override bool Equals(object o) => o is Vector2Int v && v.x == x && v.y == y;
        public override int GetHashCode() => (x * 397) ^ y;
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
    }

    [Serializable]
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public override string ToString() => $"({x}, {y}, {z})";
    }

    [Serializable]
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color red => new Color(1, 0, 0, 1);
        public static Color green => new Color(0, 1, 0, 1);
        public static Color blue => new Color(0, 0, 1, 1);
        public static Color yellow => new Color(1, 0.92f, 0.016f, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1);
        public static Color cyan => new Color(0, 1, 1, 1);
        public static Color magenta => new Color(1, 0, 1, 1);
        public static Color Lerp(Color x, Color y, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t);
        }
        public static Color operator *(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a * s);
        public override string ToString() => $"RGBA({r}, {g}, {b}, {a})";
    }

    [Serializable]
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
    }

    [Serializable]
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
    }

    public enum TextureFormat { RGBA32 = 4, RGB24 = 3, ARGB32 = 5 }

    // Inert graphics stand-ins. Only referenced by CardDatabase art helpers,
    // which the rules engine + tests never call in headless mode.
    public class Object
    {
        public string name = "";
        public static void Destroy(Object o) { }
        public static void Destroy(Object o, float t) { }
        public static void DestroyImmediate(Object o) { }
        public override string ToString() => name ?? base.ToString();
    }

    public class Texture2D : Object
    {
        public int width, height;
        public Texture2D(int w, int h) { width = w; height = h; }
        public Texture2D(int w, int h, TextureFormat fmt, bool mipmaps) { width = w; height = h; }
        public bool LoadImage(byte[] data) => false;
        public byte[] EncodeToPNG() => Array.Empty<byte>();
    }

    public class Sprite : Object
    {
        public Texture2D texture;
        public Rect rect;
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot, float pixelsPerUnit)
            => new Sprite { texture = tex, rect = rect };
    }

    // SimpleAi derives from MonoBehaviour; an empty base is enough headlessly.
    public class MonoBehaviour
    {
        public string name = "";
        public bool enabled = true;
        public GameObject gameObject { get; set; }
    }

    public class GameObject
    {
        public string name = "";
        public GameObject() { }
        public GameObject(string n) { name = n; }
        public T AddComponent<T>() where T : new() => new T();
    }

    // In-memory PlayerPrefs (persists only for the process). Enough for the
    // AI compile-settings toggles and test setup that read/write prefs.
    public static class PlayerPrefs
    {
        static readonly System.Collections.Generic.Dictionary<string, object> _p = new();
        public static void SetInt(string k, int v) => _p[k] = v;
        public static int GetInt(string k, int d = 0) => _p.TryGetValue(k, out var v) ? Convert.ToInt32(v) : d;
        public static void SetFloat(string k, float v) => _p[k] = v;
        public static float GetFloat(string k, float d = 0) => _p.TryGetValue(k, out var v) ? Convert.ToSingle(v) : d;
        public static void SetString(string k, string v) => _p[k] = v;
        public static string GetString(string k, string d = "") => _p.TryGetValue(k, out var v) ? (string)v : d;
        public static bool HasKey(string k) => _p.ContainsKey(k);
        public static void DeleteKey(string k) => _p.Remove(k);
        public static void DeleteAll() => _p.Clear();
        public static void Save() { }
    }

    // Coroutine yield instructions — inert; the headless harness never pumps
    // coroutines, it calls the synchronous rules APIs directly.
    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public class WaitForSecondsRealtime : YieldInstruction { public WaitForSecondsRealtime(float s) { } }
    public class WaitForEndOfFrame : YieldInstruction { }
    public class WaitForFixedUpdate : YieldInstruction { }
    public abstract class CustomYieldInstruction : System.Collections.IEnumerator
    {
        public abstract bool keepWaiting { get; }
        public object Current => null;
        public bool MoveNext() => keepWaiting;
        public void Reset() { }
    }
}
