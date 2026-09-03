using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Ingress-style SE motes on the map. Walk attracts/collects; ~1.8 SE/mile, daily cap 20.
    /// </summary>
    public class SetEnergyField : MonoBehaviour
    {
        const int MoteCount = 28;
        const float AttractR = 0.085f;
        const float CollectR = 0.028f;
        const float MetersPerSe = 900f;
        const int DailySeCap = 20;
        const float MapW = 280f; // sync with OverworldUI.MetersPerMapWidth
        const string PrefDay = "wrldz.seWalkDay";
        const string PrefToday = "wrldz.seWalkToday";
        const string PrefResidual = "wrldz.seWalkResidualM";

        struct Mote
        {
            public RectTransform rt;
            public Image img;
            public float nx, ny, phase, bobAmp, baseSize, attractT;
            public bool gold, alive;
        }

        Transform _burstHost;
        readonly List<Mote> _motes = new(MoteCount);
        float _lastEast, _lastNorth, _residualMeters, _respawnTimer;
        bool _haveLast;
        int _seToday;
        string _dayKey;
        System.Action<int, string> _onGathered;
        System.Action _onCurrencyChanged;
        Sprite _cyan, _gold, _burst;

        public int SeGatheredToday => _seToday;
        public int DailyCap => DailySeCap;

        public static SetEnergyField Attach(
            Transform mapContent,
            Transform burstHost,
            MapEnvironment env,
            System.Action<int, string> onGathered,
            System.Action onCurrencyChanged)
        {
            if (mapContent == null) return null;
            var existing = mapContent.GetComponentInChildren<SetEnergyField>(true);
            if (existing != null) Destroy(existing.gameObject);

            var go = new GameObject("SetEnergyField", typeof(RectTransform), typeof(SetEnergyField));
            go.transform.SetParent(mapContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var field = go.GetComponent<SetEnergyField>();
            field._burstHost = burstHost != null ? burstHost : mapContent;
            field._onGathered = onGathered;
            field._onCurrencyChanged = onCurrencyChanged;
            field.LoadPrefs();
            field._cyan = ImagineAssets.FxSeMoteCyan();
            field._gold = ImagineAssets.FxSeMoteGold();
            field._burst = ImagineAssets.FxSeGatherBurst();
            field.SpawnField();
            if (env != null)
            {
                field._lastEast = env.MetersEast;
                field._lastNorth = env.MetersNorth;
                field._haveLast = true;
            }

            return field;
        }

        void LoadPrefs()
        {
            _dayKey = System.DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (PlayerPrefs.GetString(PrefDay, "") != _dayKey)
            {
                _seToday = 0;
                _residualMeters = 0f;
                PlayerPrefs.SetString(PrefDay, _dayKey);
                PlayerPrefs.SetInt(PrefToday, 0);
                PlayerPrefs.SetFloat(PrefResidual, 0f);
                PlayerPrefs.Save();
            }
            else
            {
                _seToday = PlayerPrefs.GetInt(PrefToday, 0);
                _residualMeters = PlayerPrefs.GetFloat(PrefResidual, 0f);
            }
        }

        void SavePrefs()
        {
            PlayerPrefs.SetString(PrefDay, _dayKey);
            PlayerPrefs.SetInt(PrefToday, _seToday);
            PlayerPrefs.SetFloat(PrefResidual, _residualMeters);
            PlayerPrefs.Save();
        }

        void SpawnField()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            _motes.Clear();
            var rng = new System.Random(0x5E71CE);
            for (var i = 0; i < MoteCount; i++)
            {
                var nx = 0.08f + (float)rng.NextDouble() * 0.84f;
                var ny = 0.08f + (float)rng.NextDouble() * 0.84f;
                if (i % 3 == 0) nx = 0.42f + (float)(rng.NextDouble() - 0.5) * 0.2f;
                if (i % 5 == 0) ny = 0.48f + (float)(rng.NextDouble() - 0.5) * 0.18f;
                SpawnOne(nx, ny, i % 7 == 0, rng);
            }
        }

        void SpawnOne(float nx, float ny, bool gold, System.Random rng)
        {
            var go = new GameObject(gold ? "SeGold" : "SeCyan", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            var size = (gold ? 0.055f : 0.042f) * (0.85f + (float)rng.NextDouble() * 0.35f);
            PlaceNorm(rt, nx, ny, size);
            var img = go.GetComponent<Image>();
            var spr = gold ? (_gold ?? _cyan) : (_cyan ?? _gold);
            if (spr != null)
            {
                img.sprite = spr;
                img.color = Color.white;
                img.preserveAspect = true;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = gold
                    ? new Color(1f, 0.85f, 0.25f, 0.85f)
                    : new Color(0.3f, 0.95f, 1f, 0.8f);
            }

            img.raycastTarget = false;
            _motes.Add(new Mote
            {
                rt = rt, img = img, nx = nx, ny = ny,
                phase = (float)rng.NextDouble() * Mathf.PI * 2f,
                bobAmp = 0.004f + (float)rng.NextDouble() * 0.006f,
                baseSize = size, gold = gold, alive = true
            });
        }

        static void PlaceNorm(RectTransform rt, float nx, float ny, float size)
        {
            var h = size * 0.5f;
            rt.anchorMin = new Vector2(nx - h, ny - h);
            rt.anchorMax = new Vector2(nx + h, ny + h);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        public void NotifyMoved(float metersEast, float metersNorth)
        {
            if (!_haveLast)
            {
                _lastEast = metersEast;
                _lastNorth = metersNorth;
                _haveLast = true;
                return;
            }

            var de = metersEast - _lastEast;
            var dn = metersNorth - _lastNorth;
            _lastEast = metersEast;
            _lastNorth = metersNorth;
            var dist = Mathf.Sqrt(de * de + dn * dn);
            if (dist < 0.05f) return;

            var acc = AppSession.Ensure()?.Account;
            if (acc != null)
            {
                acc.pathKm += dist * 0.001f;
                acc.EnsureProgress();
            }

            _residualMeters += dist;
            TryGrantWalkSe(acc);
            AttractNear(0.5f + metersEast / MapW, 0.5f + metersNorth / MapW, dist);
        }

        void TryGrantWalkSe(LocalAccountStore.Account acc)
        {
            if (acc?.progress == null || _seToday >= DailySeCap) return;
            var gained = 0;
            while (_residualMeters >= MetersPerSe && _seToday + gained < DailySeCap)
            {
                _residualMeters -= MetersPerSe;
                gained++;
            }

            if (gained <= 0) { SavePrefs(); return; }
            acc.EnsureInventory();
            ArtifactService.GrantUntaggedSetEnergy(acc.progress, acc.inventory, gained);
            _seToday += gained;
            ProgressionService.Persist(acc);
            SavePrefs();
            _onCurrencyChanged?.Invoke();
            _onGathered?.Invoke(gained, $"+{gained} SE · {_seToday}/{DailySeCap}");
        }

        void AttractNear(float px, float py, float walkMeters)
        {
            var pull = Mathf.Clamp01(walkMeters / 4f);
            for (var i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                if (!m.alive) continue;
                var dx = m.nx - px;
                var dy = m.ny - py;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > AttractR) continue;
                m.attractT = Mathf.Clamp01(m.attractT + 0.35f + pull * 0.4f);
                var t = 0.12f + m.attractT * 0.45f;
                m.nx = Mathf.Lerp(m.nx, px, t);
                m.ny = Mathf.Lerp(m.ny, py, t);
                _motes[i] = m;
                if (d < CollectR || m.attractT >= 0.92f)
                    CollectMote(i);
            }
        }

        void CollectMote(int index)
        {
            if ((uint)index >= (uint)_motes.Count) return;
            var m = _motes[index];
            if (!m.alive) return;
            m.alive = false;
            _motes[index] = m;

            var acc = AppSession.Ensure()?.Account;
            if (m.gold && acc?.progress != null && _seToday < DailySeCap)
            {
                acc.EnsureInventory();
                ArtifactService.GrantUntaggedSetEnergy(acc.progress, acc.inventory, 1);
                _seToday++;
                ProgressionService.Persist(acc);
                SavePrefs();
                _onCurrencyChanged?.Invoke();
                _onGathered?.Invoke(1, $"+1 SE · {_seToday}/{DailySeCap}");
            }
            else if (!m.gold)
            {
                _residualMeters += MetersPerSe * 0.12f;
                TryGrantWalkSe(acc);
            }

            PlayBurst(m.nx, m.ny, m.gold);
            if (m.rt != null) Destroy(m.rt.gameObject);
        }

        void PlayBurst(float nx, float ny, bool gold)
        {
            var host = _burstHost != null ? _burstHost : transform;
            var go = new GameObject("SeBurst", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            if (host == transform)
                PlaceNorm(rt, nx, ny, gold ? 0.12f : 0.09f);
            else
            {
                rt.anchorMin = new Vector2(0.42f, 0.30f);
                rt.anchorMax = new Vector2(0.58f, 0.46f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            var img = go.GetComponent<Image>();
            img.sprite = _burst ?? UiFoundation.WhiteSprite();
            img.color = gold ? new Color(1f, 0.9f, 0.4f, 0.95f) : new Color(0.5f, 1f, 1f, 0.95f);
            img.preserveAspect = true;
            img.raycastTarget = false;
            go.AddComponent<SeBurstFade>();
        }

        void Update()
        {
            var t = Time.unscaledTime;
            for (var i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                if (!m.alive || m.rt == null) continue;
                var bob = Mathf.Sin(t * 2.1f + m.phase) * m.bobAmp;
                var pulse = 1f + 0.08f * Mathf.Sin(t * 3.2f + m.phase * 1.7f);
                PlaceNorm(m.rt, m.nx, m.ny + bob, m.baseSize * pulse * (1f - m.attractT * 0.35f));
                if (m.img == null) continue;
                var a = 0.75f + 0.25f * Mathf.Sin(t * 2.8f + m.phase);
                if (m.attractT > 0.01f) a = Mathf.Lerp(a, 1f, m.attractT);
                var c = m.img.color;
                c.a = a;
                m.img.color = c;
                m.rt.localScale = Vector3.one * (1f - m.attractT * 0.55f);
            }

            _respawnTimer += Time.unscaledDeltaTime;
            if (_respawnTimer > 4.5f)
            {
                _respawnTimer = 0f;
                RespawnIfNeeded();
            }
        }

        void RespawnIfNeeded()
        {
            var alive = 0;
            for (var i = 0; i < _motes.Count; i++)
                if (_motes[i].alive) alive++;
            if (alive >= MoteCount - 4) return;

            var rng = new System.Random(Random.Range(1, 99999));
            for (var i = 0; i < _motes.Count; i++)
            {
                if (_motes[i].alive) continue;
                var nx = 0.1f + (float)rng.NextDouble() * 0.8f;
                var ny = 0.1f + (float)rng.NextDouble() * 0.8f;
                if (_haveLast)
                {
                    var px = 0.5f + _lastEast / MapW;
                    var py = 0.5f + _lastNorth / MapW;
                    if (Mathf.Abs(nx - px) < 0.12f && Mathf.Abs(ny - py) < 0.12f)
                    {
                        nx = Mathf.Repeat(px + 0.25f, 0.9f) + 0.05f;
                        ny = Mathf.Repeat(py + 0.22f, 0.9f) + 0.05f;
                    }
                }

                _motes.RemoveAt(i);
                SpawnOne(nx, ny, rng.NextDouble() < 0.12, rng);
                break;
            }
        }
    }

    sealed class SeBurstFade : MonoBehaviour
    {
        float _t;
        Image _img;
        RectTransform _rt;

        void Awake()
        {
            _img = GetComponent<Image>();
            _rt = GetComponent<RectTransform>();
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            var u = Mathf.Clamp01(_t / 0.55f);
            if (_rt != null) _rt.localScale = Vector3.one * (0.6f + u * 1.4f);
            if (_img != null)
            {
                var c = _img.color;
                c.a = 1f - u;
                _img.color = c;
            }

            if (u >= 1f) Destroy(gameObject);
        }
    }
}
