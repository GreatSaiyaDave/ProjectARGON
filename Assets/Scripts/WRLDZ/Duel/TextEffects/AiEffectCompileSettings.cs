using System;
using System.IO;
using UnityEngine;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SpaceXAI / xAI settings for offline + first-play effect compilation.
    /// Key resolution order: environment XAI_API_KEY → PlayerPrefs → optional local config file.
    /// Never ship a real key in the repo; use env or a gitignored config.
    /// </summary>
    public static class AiEffectCompileSettings
    {
        public const string EnvKeyName = "XAI_API_KEY";
        public const string PrefsKeyName = "wrldz_xai_api_key";
        public const string DefaultBaseUrl = "https://api.x.ai/v1";
        public const string DefaultModel = "grok-4.5";

        /// <summary>When true, cache miss + incomplete regex may call xAI (can hitch on main thread).</summary>
        public static bool AllowRuntimeAi
        {
            get => PlayerPrefs.GetInt("wrldz_ai_fx_runtime", 0) != 0;
            set
            {
                PlayerPrefs.SetInt("wrldz_ai_fx_runtime", value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static string Model
        {
            get
            {
                var m = PlayerPrefs.GetString("wrldz_ai_fx_model", DefaultModel);
                return string.IsNullOrEmpty(m) ? DefaultModel : m;
            }
            set
            {
                PlayerPrefs.SetString("wrldz_ai_fx_model", value ?? DefaultModel);
                PlayerPrefs.Save();
            }
        }

        public static string BaseUrl => DefaultBaseUrl;

        public static int TimeoutSeconds
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("wrldz_ai_fx_timeout", 45), 10, 300);
            set
            {
                PlayerPrefs.SetInt("wrldz_ai_fx_timeout", Mathf.Clamp(value, 10, 300));
                PlayerPrefs.Save();
            }
        }

        public static bool HasApiKey => !string.IsNullOrEmpty(ResolveApiKey());

        public static string ResolveApiKey()
        {
            var env = Environment.GetEnvironmentVariable(EnvKeyName);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

            var prefs = PlayerPrefs.GetString(PrefsKeyName, "");
            if (!string.IsNullOrWhiteSpace(prefs)) return prefs.Trim();

            // Optional local config (gitignored path under persistentDataPath)
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "WRLDZ", "ai_config.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonUtility.FromJson<AiConfigFile>(json);
                    if (!string.IsNullOrWhiteSpace(cfg?.apiKey)) return cfg.apiKey.Trim();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        public static void SetApiKeyInPrefs(string key)
        {
            if (string.IsNullOrEmpty(key))
                PlayerPrefs.DeleteKey(PrefsKeyName);
            else
                PlayerPrefs.SetString(PrefsKeyName, key.Trim());
            PlayerPrefs.Save();
        }

        [Serializable]
        class AiConfigFile
        {
            public string apiKey;
            public string model;
        }
    }
}
