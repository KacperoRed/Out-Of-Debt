using System.Collections;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Proste efekty wizualne bez żadnych assetów (smugi pocisków, wybuchy).
    /// Wywoływane lokalnie na każdym kliencie przez ClientRpc.
    /// Tu podmień na własne Particle Systemy / dźwięki.
    /// </summary>
    public class Effects : MonoBehaviour
    {
        static Effects _instance;
        static Material _lineMat, _fireMat;

        static Effects Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[Effects]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Effects>();
                }
                return _instance;
            }
        }

        static Material LineMat
        {
            get
            {
                if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
                return _lineMat;
            }
        }

        static Material FireMat
        {
            get
            {
                if (_fireMat == null)
                {
                    _fireMat = new Material(Shader.Find("Sprites/Default"));
                    _fireMat.color = new Color(1f, 0.55f, 0.1f, 0.8f);
                }
                return _fireMat;
            }
        }

        static Material _bulletMat;
        static Material BulletMat
        {
            get
            {
                if (_bulletMat == null) { _bulletMat = new Material(Shader.Find("Sprites/Default")); _bulletMat.color = new Color(1f, 0.92f, 0.5f, 1f); }
                return _bulletMat;
            }
        }

        public static void Tracer(Vector3 from, Vector3 to, Color color)
        {
            GameAudio.Shot(from); // dźwięk strzału (jedna salwa, także przy śrutówce)
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = LineMat;
            lr.startColor = color;
            lr.endColor = new Color(color.r, color.g, color.b, 0.1f);
            lr.startWidth = 0.05f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            Destroy(go, 0.06f);

            // widoczny pocisk: leci prosto (bez grawitacji) z lufy do punktu trafienia
            if ((to - from).sqrMagnitude > 1f)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.name = "Bullet";
                var br = b.GetComponent<Renderer>();
                br.sharedMaterial = BulletMat;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.transform.localScale = new Vector3(0.1f, 0.1f, 1.1f);
                var mover = b.AddComponent<BulletMover>();
                mover.from = from; mover.to = to; mover.speed = 100f;
            }

            // błysk z lufy
            var light = new GameObject("Flash").AddComponent<Light>();
            light.transform.position = from;
            light.color = new Color(1f, 0.8f, 0.4f);
            light.range = 6f;
            light.intensity = 3f;
            Destroy(light.gameObject, 0.05f);
        }

        static Material _bloodMat;

        /// <summary>Fontanna krwi (cząsteczki) + plama na podłożu. Bez assetów – tworzone w kodzie.</summary>
        public static void Blood(Vector3 pos, Vector3 dir)
        {
            if (_bloodMat == null) _bloodMat = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject("Blood");
            go.transform.position = pos;
            go.transform.rotation = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.duration = 0.3f; main.loop = false;
            main.startLifetime = 0.55f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0f, 0f), new Color(0.85f, 0.05f, 0.05f));
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 22) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.04f;
            go.GetComponent<ParticleSystemRenderer>().material = _bloodMat;
            ps.Play();
            Destroy(go, 1.2f);

            // plama na ziemi pod trafionym
            if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.GetComponent<Renderer>().sharedMaterial = _bloodMat;
                quad.GetComponent<Renderer>().material.color = new Color(0.45f, 0f, 0f, 0.85f);
                quad.transform.position = hit.point + hit.normal * 0.02f;
                quad.transform.rotation = Quaternion.FromToRotation(Vector3.forward, -hit.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                quad.transform.localScale = Vector3.one * Random.Range(0.35f, 0.7f);
                Destroy(quad, 12f);
            }
        }

        public static void Explosion(Vector3 pos, float radius)
        {
            Instance.StartCoroutine(ExplosionRoutine(pos, radius));
        }

        static IEnumerator ExplosionRoutine(Vector3 pos, float radius)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.GetComponent<Renderer>().sharedMaterial = FireMat;
            sphere.transform.position = pos;
            var light = sphere.AddComponent<Light>();
            light.color = new Color(1f, 0.5f, 0.1f);
            light.range = radius * 4f;
            light.intensity = 8f;

            float t = 0f;
            const float duration = 0.6f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                sphere.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, radius * 2f, k);
                light.intensity = Mathf.Lerp(8f, 0f, k);
                yield return null;
            }
            Destroy(sphere);
        }
    }
}
