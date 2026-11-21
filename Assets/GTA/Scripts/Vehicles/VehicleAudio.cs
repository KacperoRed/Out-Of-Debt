using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Silnik auta z paczki „i6 German Free”. Cztery pętle (jałowy, niskie, średnie, wysokie obroty) są mieszane
    /// w zależności od obrotów (prędkość + gaz), a wysokość dźwięku rośnie z obrotami. Auta dalej niż 75 m od kamery
    /// milczą (oszczędność). Auto bez kierowcy stoi cicho (wyłączony silnik), auto AI i gracza brzmi tak samo.
    /// Dodaje się samo w Vehicle.Awake; bez GameAudio.asset nic nie robi.
    /// </summary>
    public class VehicleAudio : MonoBehaviour
    {
        const float AudibleDistance = 75f;

        Vehicle _v;
        AudioSource _idle, _low, _med, _high;
        float _rpm;
        bool _built, _wasRunning;

        void Start()
        {
            _v = GetComponent<Vehicle>();
        }

        void Build(GameAudioLibrary lib)
        {
            _built = true;
            if (lib.engineIdle == null && lib.engineLow == null) return;
            _idle = GameAudio.Make3D(transform, lib.engineIdle, true, 70f);
            _low = GameAudio.Make3D(transform, lib.engineLow, true, 70f);
            _med = GameAudio.Make3D(transform, lib.engineMed, true, 70f);
            _high = GameAudio.Make3D(transform, lib.engineHigh, true, 70f);
            foreach (var s in All()) if (s != null) { s.volume = 0f; s.Play(); }
        }

        AudioSource[] All() => new[] { _idle, _low, _med, _high };

        static float Tri(float x, float a, float peak, float b)
        {
            if (x <= a || x >= b) return 0f;
            return x < peak ? (x - a) / (peak - a) : (b - x) / (b - peak);
        }

        void Update()
        {
            if (_v == null) { _v = GetComponent<Vehicle>(); return; }
            var lib = GameAudio.Library;
            var cam = Camera.main;
            if (lib == null || cam == null) return;

            bool near = (cam.transform.position - transform.position).sqrMagnitude < AudibleDistance * AudibleDistance;
            bool running = near && _v.IsSpawned && (_v.HasPlayerDriver || _v.IsAIDriven.Value) && !_v.Health.Dead;

            if (!_built)
            {
                if (!running) return;
                Build(lib);
            }
            if (_idle == null && _low == null) return;

            // start silnika, gdy kierowca wsiada
            if (running && !_wasRunning && lib.engineStart != null && (_v.HasPlayerDriver))
                GameAudio.PlayAt(lib.engineStart, transform.position, 0.7f);
            _wasRunning = running;

            float speedRatio = Mathf.Clamp01(Mathf.Abs(_v.ForwardSpeed) / Mathf.Max(1f, _v.maxSpeed));
            float target = running ? Mathf.Clamp01(0.08f + speedRatio * 0.82f + Mathf.Abs(_v.throttleInput) * 0.12f) : 0f;
            _rpm = Mathf.MoveTowards(_rpm, target, Time.deltaTime * (target > _rpm ? 1.2f : 0.7f));

            float master = running ? GameSettings.EngineVolume : 0f;
            if (!running && _rpm < 0.01f)
            {
                foreach (var s in All()) if (s != null && s.isPlaying) s.volume = 0f;
                return;
            }

            float idleW = Mathf.Clamp01(1f - _rpm * 4.5f);
            float lowW = Tri(_rpm, 0.05f, 0.28f, 0.55f);
            float medW = Tri(_rpm, 0.3f, 0.55f, 0.82f);
            float highW = Mathf.Clamp01((_rpm - 0.58f) * 2.6f);

            Set(_idle, idleW * master, 0.85f + _rpm * 0.3f);
            Set(_low, lowW * master, 0.85f + _rpm * 0.55f);
            Set(_med, medW * master, 0.8f + _rpm * 0.5f);
            Set(_high, highW * master, 0.85f + _rpm * 0.4f);
        }

        static void Set(AudioSource s, float volume, float pitch)
        {
            if (s == null) return;
            s.volume = Mathf.Clamp01(volume);
            s.pitch = pitch;
            if (!s.isPlaying && volume > 0.001f) s.Play();
        }
    }
}
