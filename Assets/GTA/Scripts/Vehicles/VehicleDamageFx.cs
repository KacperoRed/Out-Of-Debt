using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Widoczne uszkodzenia auta: poniżej 40% zdrowia z maski wali szary dym, poniżej 20% czarny, a tuż przed wybuchem (12%) pojawia się ogień.
    /// Tworzy się sam (Vehicle.Awake) – działa na każdym aucie bez zmian w prefabach.
    /// </summary>
    public class VehicleDamageFx : MonoBehaviour
    {
        ParticleSystem _smoke, _fire;
        Health _health;
        static Material _mat;

        static Material SoftMat()
        {
            if (_mat != null) return _mat;
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            tex.Apply();
            _mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
            return _mat;
        }

        void Start()
        {
            _health = GetComponent<Health>();
            var bc = GetComponent<BoxCollider>();
            Vector3 hood = bc != null ? bc.center + new Vector3(0f, bc.size.y * 0.45f, bc.size.z * 0.3f) : new Vector3(0f, 1.2f, 1.5f);
            _smoke = Make("DamageSmoke", hood, new Color(0.6f, 0.6f, 0.6f), 2.2f, 2.5f, 1.6f);
            _fire = Make("DamageFire", hood, new Color(1f, 0.55f, 0.1f), 1.1f, 0.7f, 2.2f);
        }

        ParticleSystem Make(string name, Vector3 localPos, Color c, float size, float life, float speed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = c;
            main.maxParticles = 80;
            main.playOnAwake = false;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 14f; sh.radius = 0.25f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.8f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = SoftMat();
            return ps;
        }

        void Update()
        {
            if (_health == null || _smoke == null) return;
            float p = _health.Percent;
            bool wreck = _health.Dead;

            float smokeRate = wreck ? 18f : p < 0.2f ? 22f : p < 0.4f ? 9f : 0f;
            var sm = _smoke.main;
            sm.startColor = p < 0.2f || wreck ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.6f, 0.6f, 0.6f);
            var se = _smoke.emission; se.rateOverTime = smokeRate;
            if (smokeRate > 0f && !_smoke.isPlaying) _smoke.Play();
            else if (smokeRate <= 0f && _smoke.isPlaying) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            float fireRate = wreck || p < 0.12f ? 25f : 0f;
            var fe = _fire.emission; fe.rateOverTime = fireRate;
            if (fireRate > 0f && !_fire.isPlaying) _fire.Play();
            else if (fireRate <= 0f && _fire.isPlaying) _fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
