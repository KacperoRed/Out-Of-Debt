using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Cykl dnia i nocy. Pora dnia liczona z czasu serwera (NetworkManager.ServerTime),
    /// więc wszyscy gracze widzą to samo – bez żadnej dodatkowej synchronizacji.
    /// W nocy włącza latarnie (StreetLamp) i reflektory aut – tylko najbliższe kamerze (wydajność).
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        public Light sun;
        [Tooltip("Długość całej doby w minutach")]
        public float dayLengthMinutes = 12f;
        [Range(0f, 1f), Tooltip("Pora startowa: 0 = północ, 0.25 = 6:00, 0.5 = południe")]
        public float startTime = 0.3f;

        [Header("Kolory")]
        public Gradient ambient;
        public Gradient fog;
        public AnimationCurve sunIntensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Światła nocne")]
        public int maxLampLights = 24;
        public int maxHeadlights = 10;
        public float lightRange = 140f;

        /// <summary>0..1 (0.5 = południe).</summary>
        public float TimeOfDay { get; private set; }
        public bool IsNight => TimeOfDay < 0.26f || TimeOfDay > 0.76f;
        public string ClockText
        {
            get
            {
                int minutes = Mathf.FloorToInt(TimeOfDay * 24f * 60f);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }

        readonly List<Vehicle> _lit = new List<Vehicle>();
        float _nextLightUpdate;

        void Awake()
        {
            Instance = this;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            if (ambient == null || ambient.colorKeys.Length <= 2) ambient = DefaultAmbient();
            if (fog == null || fog.colorKeys.Length <= 2) fog = DefaultFog();
            SetupGraphics();
        }

        Material _sky;

        /// <summary>Lepsza grafika bez nowych assetów: jakość cieni i krawędzi, trójkolorowe światło otoczenia, niebo proceduralne, ciepłe słońce.</summary>
        void SetupGraphics()
        {
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 160f;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = 1.6f;
            QualitySettings.pixelLightCount = 6;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 200f;
            RenderSettings.fogEndDistance = 850f;
            RenderSettings.sun = sun;

            var sh = Shader.Find("Skybox/Procedural");
            if (sh != null)
            {
                _sky = new Material(sh) { name = "RuntimeSky" };
                RenderSettings.skybox = _sky;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                cam.allowHDR = true;
                cam.allowMSAA = true;
                if (_sky != null) cam.clearFlags = CameraClearFlags.Skybox;
            }
        }

        void Update()
        {
            double seconds = Time.timeAsDouble;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) seconds = nm.ServerTime.Time;
            TimeOfDay = (float)((startTime + seconds / (dayLengthMinutes * 60.0)) % 1.0);

            // słońce: wschód 6:00, zachód 18:00
            float sunAngle = (TimeOfDay - 0.25f) * 360f;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(sunAngle, 30f, 0f);
                float day = Mathf.Clamp01(Mathf.Sin(TimeOfDay * Mathf.PI * 2f - Mathf.PI * 0.5f) * 0.5f + 0.5f);
                sun.intensity = Mathf.Lerp(0.05f, 1.1f, sunIntensity.Evaluate(Mathf.Clamp01((day - 0.35f) * 2.5f)));
                sun.shadows = sun.intensity > 0.2f ? LightShadows.Soft : LightShadows.None;
            }
            Color amb = ambient.Evaluate(TimeOfDay);
            RenderSettings.ambientLight = amb;
            RenderSettings.ambientSkyColor = Color.Lerp(amb, new Color(0.55f, 0.7f, 1f), 0.35f) * 1.15f;       // niebo – chłodniejsze, jaśniejsze
            RenderSettings.ambientEquatorColor = amb;
            RenderSettings.ambientGroundColor = Color.Lerp(amb, new Color(0.35f, 0.3f, 0.25f), 0.35f) * 0.85f;   // od ziemi – ciemniejsze, ciepłe
            if (sun != null)
            {
                float elev = Mathf.Sin((TimeOfDay - 0.25f) * Mathf.PI * 2f);   // 1 = południe, <0 = noc
                sun.color = Color.Lerp(new Color(1f, 0.58f, 0.34f), new Color(1f, 0.96f, 0.88f), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elev * 2.2f)));
                sun.shadowStrength = 0.85f;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.5f;
            }
            if (_sky != null)
            {
                float day = Mathf.Clamp01(Mathf.Sin((TimeOfDay - 0.25f) * Mathf.PI * 2f) * 0.5f + 0.5f);
                _sky.SetFloat("_Exposure", Mathf.Lerp(0.12f, 1.25f, Mathf.SmoothStep(0f, 1f, day)));
                _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.6f, 1.1f, day));
                _sky.SetFloat("_SunSize", 0.04f);
                _sky.SetColor("_SkyTint", Color.Lerp(new Color(0.35f, 0.4f, 0.55f), new Color(0.5f, 0.55f, 0.65f), day));
            }
            RenderSettings.fogColor = fog.Evaluate(TimeOfDay);
            var cam = Camera.main;
            if (cam != null)
            {
                if (_sky != null) cam.clearFlags = CameraClearFlags.Skybox;   // kamera mogła powstać po Awake – ustawiaj co klatkę
                else cam.backgroundColor = RenderSettings.fogColor;
            }

            if (Time.time > _nextLightUpdate)
            {
                _nextLightUpdate = Time.time + 0.5f;
                UpdateNightLights(cam);
            }
        }

        void UpdateNightLights(Camera cam)
        {
            Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
            bool night = IsNight;

            // latarnie: włącz Light tylko w najbliższych, reszta świeci samą emisją
            StreetLamp.UpdateLights(pos, night, maxLampLights, lightRange);

            // reflektory aut
            foreach (var v in _lit) if (v != null && v.Headlight != null) v.Headlight.enabled = false;
            _lit.Clear();
            if (!night) return;
            var sorted = new List<Vehicle>(Vehicle.All);
            sorted.RemoveAll(v => v == null || v.Health.Dead || (!v.IsAIDriven.Value && !v.HasPlayerDriver));
            sorted.Sort((a, b) => (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
            for (int i = 0; i < sorted.Count && i < maxHeadlights; i++)
            {
                if ((sorted[i].transform.position - pos).sqrMagnitude > lightRange * lightRange) break;
                sorted[i].EnsureHeadlight().enabled = true;
                _lit.Add(sorted[i]);
            }
        }

        static Gradient DefaultAmbient()
        {
            var g = new Gradient();
            g.SetKeys(new[]
            {
                new GradientColorKey(new Color(0.10f, 0.12f, 0.22f), 0.0f),
                new GradientColorKey(new Color(0.12f, 0.14f, 0.25f), 0.22f),
                new GradientColorKey(new Color(0.55f, 0.45f, 0.40f), 0.28f),
                new GradientColorKey(new Color(0.55f, 0.58f, 0.62f), 0.5f),
                new GradientColorKey(new Color(0.55f, 0.42f, 0.38f), 0.74f),
                new GradientColorKey(new Color(0.12f, 0.14f, 0.25f), 0.8f),
                new GradientColorKey(new Color(0.10f, 0.12f, 0.22f), 1.0f),
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static Gradient DefaultFog()
        {
            var g = new Gradient();
            g.SetKeys(new[]
            {
                new GradientColorKey(new Color(0.04f, 0.05f, 0.10f), 0.0f),
                new GradientColorKey(new Color(0.05f, 0.06f, 0.12f), 0.22f),
                new GradientColorKey(new Color(0.95f, 0.65f, 0.45f), 0.27f),
                new GradientColorKey(new Color(0.70f, 0.78f, 0.85f), 0.35f),
                new GradientColorKey(new Color(0.70f, 0.78f, 0.85f), 0.68f),
                new GradientColorKey(new Color(0.95f, 0.55f, 0.35f), 0.75f),
                new GradientColorKey(new Color(0.05f, 0.06f, 0.12f), 0.8f),
                new GradientColorKey(new Color(0.04f, 0.05f, 0.10f), 1.0f),
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
