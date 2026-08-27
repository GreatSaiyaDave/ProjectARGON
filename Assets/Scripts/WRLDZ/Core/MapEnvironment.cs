using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace WRLDZ.Core
{
    public enum DayPhase
    {
        Night,
        Dawn,
        Day,
        Dusk
    }

    public enum WeatherKind
    {
        Clear,
        Cloudy,
        Rain,
        Storm,
        Fog,
        Snow
    }

    /// <summary>
    /// Live map environment: day/night cycle from local clock + latitude,
    /// weather from GPS position (Open-Meteo, no API key). Falls back gracefully.
    /// </summary>
    public class MapEnvironment : MonoBehaviour
    {
        public static MapEnvironment Instance { get; private set; }

        [Header("Refresh")]
        public float weatherRefreshSeconds = 600f;
        public float dayNightTickSeconds = 30f;

        public DayPhase Phase { get; private set; } = DayPhase.Night;
        /// <summary>0 = midnight, 0.5 = noon, 1 = next midnight.</summary>
        public float DayCycle01 { get; private set; }
        /// <summary>0 = full night, 1 = full day (smooth).</summary>
        public float Daylight01 { get; private set; }
        public WeatherKind Weather { get; private set; } = WeatherKind.Clear;
        public string WeatherLabel { get; private set; } = "Clear";
        public float TemperatureC { get; private set; } = 20f;
        public int WeatherCode { get; private set; }
        public double Latitude { get; private set; } = 35.6762; // default Tokyo (Battle City energy)
        public double Longitude { get; private set; } = 139.6503;
        /// <summary>Session origin when GPS first locks (or default).</summary>
        public double OriginLatitude { get; private set; } = 35.6762;
        public double OriginLongitude { get; private set; } = 139.6503;
        /// <summary>Meters north of origin (real-world walk).</summary>
        public float MetersNorth { get; private set; }
        /// <summary>Meters east of origin.</summary>
        public float MetersEast { get; private set; }
        public bool GpsActive { get; private set; }
        public bool OriginSet { get; private set; }
        /// <summary>True when meters come from real GPS (not WASD/pad sim).</summary>
        public bool UsingRealGps { get; private set; }
        public float HorizontalAccuracyM { get; private set; } = 9999f;
        public string LocationStatus { get; private set; } = "Using default region";
        public string SummaryLine { get; private set; } = "Night · Clear";

        /// <summary>Max meters from avatar to interact with a Tear (GO-style zone entry).</summary>
        public const float TearInteractRadiusM = 55f;

        public event Action OnEnvironmentChanged;
        /// <summary>Fired when GPS position updates enough to move the avatar map.</summary>
        public event Action OnLocationMoved;

        float _weatherTimer;
        float _dayTimer;
        float _gpsPollTimer;
        bool _fetching;

        public static MapEnvironment Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MapEnvironment");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MapEnvironment>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            RecomputeDayNight();
            StartCoroutine(StartGpsAndWeather());
        }

        void Update()
        {
            _dayTimer += Time.unscaledDeltaTime;
            if (_dayTimer >= dayNightTickSeconds)
            {
                _dayTimer = 0f;
                RecomputeDayNight();
                RaiseChanged();
            }

            _weatherTimer += Time.unscaledDeltaTime;
            if (_weatherTimer >= weatherRefreshSeconds && !_fetching)
            {
                _weatherTimer = 0f;
                StartCoroutine(FetchWeather());
            }

            // Poll GPS for real-world walking (Pokémon GO–style movement)
            _gpsPollTimer += Time.unscaledDeltaTime;
            if (_gpsPollTimer >= 0.5f)
            {
                _gpsPollTimer = 0f;
                PollGpsPosition();
            }
        }

        /// <summary>
        /// Read live GPS and convert to meters N/E from session origin.
        /// Player avatar stays centered; overworld map pans under them.
        /// </summary>
        void PollGpsPosition()
        {
            try
            {
                if (!WrldzInput.TryGetLocation(out var lat, out var lon))
                    return;
                if (!OriginSet)
                {
                    OriginLatitude = lat;
                    OriginLongitude = lon;
                    OriginSet = true;
                }

                Latitude = lat;
                Longitude = lon;
                GpsActive = true;
                UsingRealGps = true;
                HorizontalAccuracyM = WrldzInput.LocationAccuracyMeters;
                LocationStatus = HorizontalAccuracyM < 80f
                    ? $"GPS ±{HorizontalAccuracyM:0}m"
                    : $"GPS weak ±{HorizontalAccuracyM:0}m · keep outdoors";

                // Equirectangular local approximation
                var dLat = (lat - OriginLatitude) * Mathf.Deg2Rad;
                var dLon = (lon - OriginLongitude) * Mathf.Deg2Rad;
                var meanLat = (float)((lat + OriginLatitude) * 0.5 * Mathf.Deg2Rad);
                const float R = 6371000f; // Earth radius m
                var north = (float)(dLat * R);
                var east = (float)(dLon * R * Mathf.Cos(meanLat));

                // Ignore huge jumps (tunnel / first bad fix)
                var dn = north - MetersNorth;
                var de = east - MetersEast;
                var jump2 = dn * dn + de * de;
                if (OriginSet && jump2 > 250000f) // >500m single hop
                {
                    Debug.LogWarning("[WRLDZ] GPS jump ignored (>500m). Tap Recenter if you moved far.");
                    return;
                }

                if (jump2 < 0.25f) // <0.5m noise filter
                    return;

                MetersNorth = north;
                MetersEast = east;
                OnLocationMoved?.Invoke();
            }
            catch
            {
                // ignore transient GPS errors
            }
        }

        /// <summary>Editor / indoor pad: simulate walking meters (WASD / on-screen pad).</summary>
        public void SimulateWalkMeters(float eastDelta, float northDelta)
        {
            MetersEast += eastDelta;
            MetersNorth += northDelta;
            if (!OriginSet)
            {
                OriginLatitude = Latitude;
                OriginLongitude = Longitude;
                OriginSet = true;
            }

            // Simulated walk does not claim real GPS
            if (!UsingRealGps)
                LocationStatus = Application.isEditor
                    ? "Editor walk (WASD) · phone GPS outdoors"
                    : "Walk pad · enable GPS outdoors for real position";

            OnLocationMoved?.Invoke();
        }

        /// <summary>Meters from current map origin to a pin at normalized map coords (0–1).</summary>
        public float DistanceMetersToMapPin(float pinX01, float pinY01, float metersPerMapWidth)
        {
            var east = MetersEast / Mathf.Max(1f, metersPerMapWidth);
            var north = MetersNorth / Mathf.Max(1f, metersPerMapWidth);
            var dx = (pinX01 - 0.5f) - east;
            var dy = (pinY01 - 0.5f) - north;
            return Mathf.Sqrt(dx * dx + dy * dy) * metersPerMapWidth;
        }

        public void RecenterOriginHere()
        {
            OriginLatitude = Latitude;
            OriginLongitude = Longitude;
            MetersNorth = 0f;
            MetersEast = 0f;
            OriginSet = true;
            OnLocationMoved?.Invoke();
            RaiseChanged();
        }

        IEnumerator StartGpsAndWeather()
        {
            // GPS — desktop editors often lack a provider; never break UI init.
            // (Cannot yield inside try/catch — CS1626)
            LocationStatus = "Starting location…";

            if (!WrldzLab.HasLocationPermission)
            {
                GpsActive = false;
                UsingRealGps = false;
                LocationStatus = "Location permission denied — walk pad / WASD";
                Debug.LogWarning("[WRLDZ] No location permission — map uses pad/WASD only.");
            }
            else if (!WrldzInput.LocationSupported && !Application.isEditor)
            {
                // System location toggle off (Android location services disabled)
                GpsActive = false;
                UsingRealGps = false;
                LocationStatus = "Enable Location in phone Settings · pad works indoors";
                Debug.LogWarning("[WRLDZ] System location disabled by user.");
            }
            else if (Application.isEditor)
            {
                // Editor: no real GPS — origin at default Battle City energy region
                GpsActive = false;
                UsingRealGps = false;
                OriginSet = true;
                LocationStatus = "Editor · WASD walks map · APK for real GPS";
            }
            else
            {
                // Mobile outdoor path
                WrldzInput.LocationStart(8f, 3f);
                var maxWait = 14f;
                while (maxWait > 0f)
                {
                    var st = WrldzInput.LocationStatus;
                    if (st == LocationServiceStatus.Failed)
                    {
                        LocationStatus = "GPS failed — check Settings → Location";
                        break;
                    }

                    if (WrldzInput.TryGetLocation(out var startLat, out var startLon))
                    {
                        Latitude = startLat;
                        Longitude = startLon;
                        OriginLatitude = Latitude;
                        OriginLongitude = Longitude;
                        OriginSet = true;
                        MetersNorth = 0f;
                        MetersEast = 0f;
                        GpsActive = true;
                        UsingRealGps = true;
                        HorizontalAccuracyM = WrldzInput.LocationAccuracyMeters;
                        LocationStatus = $"GPS locked ±{HorizontalAccuracyM:0}m · walk outdoors";
                        Debug.Log($"[WRLDZ] GPS origin {startLat:0.0000},{startLon:0.0000} ±{HorizontalAccuracyM:0}m");
                        OnLocationMoved?.Invoke();
                        break;
                    }

                    LocationStatus = st == LocationServiceStatus.Initializing
                        ? "GPS acquiring fix… step outdoors"
                        : "Waiting for GPS…";
                    yield return new WaitForSecondsRealtime(0.5f);
                    maxWait -= 0.5f;
                }

                if (!GpsActive)
                {
                    UsingRealGps = false;
                    if (string.IsNullOrEmpty(LocationStatus) || LocationStatus.StartsWith("Waiting") ||
                        LocationStatus.StartsWith("GPS acquiring"))
                        LocationStatus = "No fix yet — walk pad works · keep trying outdoors";
                }
            }

            RecomputeDayNight();
            yield return FetchWeather();
            RaiseChanged();
        }

        public void ForceRefresh()
        {
            RecomputeDayNight();
            if (!_fetching) StartCoroutine(FetchWeather());
            RaiseChanged();
        }

        void RecomputeDayNight()
        {
            var now = DateTime.Now;
            DayCycle01 = (float)(now.TimeOfDay.TotalHours / 24.0);

            // Approximate sunrise/sunset from latitude + day of year (no external lib)
            GetSunTimes(now, Latitude, out var sunriseH, out var sunsetH);
            var hour = (float)now.TimeOfDay.TotalHours;

            // Smooth daylight factor
            Daylight01 = SmoothDaylight(hour, sunriseH, sunsetH);

            if (hour < sunriseH - 0.5f || hour >= sunsetH + 0.5f)
                Phase = DayPhase.Night;
            else if (hour < sunriseH + 1.0f)
                Phase = DayPhase.Dawn;
            else if (hour < sunsetH - 1.0f)
                Phase = DayPhase.Day;
            else
                Phase = DayPhase.Dusk;

            RebuildSummary();
        }

        static float SmoothDaylight(float hour, float sunrise, float sunset)
        {
            // 0 at night, peaks mid-day
            if (sunset <= sunrise + 0.1f) return 0.5f;
            if (hour <= sunrise || hour >= sunset) return 0f;
            return Mathf.Clamp01(Mathf.Sin(Mathf.PI * (hour - sunrise) / (sunset - sunrise)));
        }

        /// <summary>Rough sunrise/sunset hours local (Cooper approximation-ish).</summary>
        public static void GetSunTimes(DateTime localDate, double latitudeDeg, out float sunriseHour, out float sunsetHour)
        {
            // Day of year
            var n = localDate.DayOfYear;
            var lat = (float)latitudeDeg * Mathf.Deg2Rad;
            // Solar declination
            var decl = 23.44f * Mathf.Deg2Rad * Mathf.Sin(Mathf.Deg2Rad * (360f / 365f * (n - 81)));
            var cosHa = -Mathf.Tan(lat) * Mathf.Tan(decl);
            cosHa = Mathf.Clamp(cosHa, -0.999f, 0.999f);
            var ha = Mathf.Acos(cosHa) * Mathf.Rad2Deg; // hour angle degrees
            var dayLen = 2f * ha / 15f; // hours
            sunriseHour = 12f - dayLen * 0.5f;
            sunsetHour = 12f + dayLen * 0.5f;
            // Clamp polar extremes
            if (float.IsNaN(sunriseHour))
            {
                sunriseHour = 6f;
                sunsetHour = 18f;
            }
        }

        IEnumerator FetchWeather()
        {
            _fetching = true;
            var url =
                $"https://api.open-meteo.com/v1/forecast?latitude={Latitude:0.#####}&longitude={Longitude:0.#####}" +
                "&current=temperature_2m,weather_code,precipitation,cloud_cover,is_day&timezone=auto";

            using var req = UnityWebRequest.Get(url);
            req.timeout = 12;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    ParseOpenMeteo(req.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] Weather parse failed: " + ex.Message);
                    ApplyFallbackWeather();
                }
            }
            else
            {
                Debug.LogWarning("[WRLDZ] Weather fetch failed: " + req.error);
                ApplyFallbackWeather();
            }

            // Prefer API is_day if present, else keep clock phase
            RebuildSummary();
            RaiseChanged();
            _fetching = false;
        }

        void ParseOpenMeteo(string json)
        {
            // Minimal parse without JSON package — look for "current":{...}
            // Open-Meteo format: "temperature_2m":21.2,"weather_code":3,...
            TemperatureC = ReadFloat(json, "temperature_2m", TemperatureC);
            WeatherCode = (int)ReadFloat(json, "weather_code", WeatherCode);
            var cloud = ReadFloat(json, "cloud_cover", 0f);
            var precip = ReadFloat(json, "precipitation", 0f);
            var isDay = ReadFloat(json, "is_day", -1f);

            Weather = MapWmoCode(WeatherCode, cloud, precip);
            WeatherLabel = LabelFor(Weather, TemperatureC);

            if (isDay >= 0f)
            {
                // Soft blend with clock — if API says night, force night phase when conflicting
                if (isDay < 0.5f && Phase == DayPhase.Day)
                    Phase = DayPhase.Night;
                else if (isDay >= 0.5f && Phase == DayPhase.Night)
                {
                    var hour = (float)DateTime.Now.TimeOfDay.TotalHours;
                    if (hour > 5f && hour < 21f) Phase = DayPhase.Day;
                }

                Daylight01 = isDay >= 0.5f ? Mathf.Max(Daylight01, 0.65f) : Mathf.Min(Daylight01, 0.2f);
            }
        }

        static WeatherKind MapWmoCode(int code, float cloud, float precip)
        {
            // WMO weather interpretation codes (Open-Meteo)
            if (code == 0) return WeatherKind.Clear;
            if (code is 1 or 2 or 3) return cloud > 70f ? WeatherKind.Cloudy : WeatherKind.Clear;
            if (code is 45 or 48) return WeatherKind.Fog;
            if (code is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82)
                return WeatherKind.Rain;
            if (code is 71 or 73 or 75 or 77 or 85 or 86) return WeatherKind.Snow;
            if (code is 95 or 96 or 99) return WeatherKind.Storm;
            if (precip > 0.2f) return WeatherKind.Rain;
            if (cloud > 75f) return WeatherKind.Cloudy;
            return WeatherKind.Clear;
        }

        static string LabelFor(WeatherKind w, float tempC)
        {
            var t = $"{tempC:0}°C";
            return w switch
            {
                WeatherKind.Clear => $"Clear · {t}",
                WeatherKind.Cloudy => $"Cloudy · {t}",
                WeatherKind.Rain => $"Rain · {t}",
                WeatherKind.Storm => $"Storm · {t}",
                WeatherKind.Fog => $"Fog · {t}",
                WeatherKind.Snow => $"Snow · {t}",
                _ => t
            };
        }

        void ApplyFallbackWeather()
        {
            // Time + season flavored fallback when network/GPS fails
            var month = DateTime.Now.Month;
            var hour = DateTime.Now.Hour;
            if (month is 12 or 1 or 2)
            {
                Weather = hour is >= 18 or < 7 ? WeatherKind.Cloudy : WeatherKind.Clear;
                TemperatureC = 5f;
            }
            else if (month is 6 or 7 or 8)
            {
                Weather = hour is >= 14 and <= 18 ? WeatherKind.Cloudy : WeatherKind.Clear;
                TemperatureC = 28f;
            }
            else
            {
                Weather = WeatherKind.Clear;
                TemperatureC = 18f;
            }

            WeatherLabel = LabelFor(Weather, TemperatureC) + " (offline)";
        }

        void RebuildSummary()
        {
            var phase = Phase switch
            {
                DayPhase.Dawn => "Dawn",
                DayPhase.Day => "Day",
                DayPhase.Dusk => "Dusk",
                _ => "Night"
            };
            SummaryLine = $"{phase} · {WeatherLabel}";
        }

        void RaiseChanged() => OnEnvironmentChanged?.Invoke();

        static float ReadFloat(string json, string key, float fallback)
        {
            var token = "\"" + key + "\":";
            var i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return fallback;
            i += token.Length;
            var end = i;
            while (end < json.Length && "0123456789+-.eE".IndexOf(json[end]) >= 0) end++;
            if (end == i) return fallback;
            return float.TryParse(json.Substring(i, end - i),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v)
                ? v
                : fallback;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            WrldzInput.LocationStop();
        }
    }
}
