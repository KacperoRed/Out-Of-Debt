using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Tło nieba: pierścień gór na horyzoncie i dryfujące chmury. Tworzy się samo po załadowaniu sceny
    /// (nie trzeba nic przebudowywać), podąża za kamerą, więc góry są zawsze „daleko”.
    /// Siatki powstają proceduralnie, bez żadnych plików. Kolory gór ciemnieją w nocy razem ze światłem sceny.
    /// </summary>
    public class SkyBackdrop : MonoBehaviour
    {
        const float FarRadius = 640f, NearRadius = 560f;

        Transform _clouds;
        Material _far, _near, _cloud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<SkyBackdrop>() != null) return;
            new GameObject("SkyBackdrop").AddComponent<SkyBackdrop>();
        }

        static Material MakeMat(Color c, float emission)
        {
            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
            if (emission > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emission);
            }
            return m;
        }

        void Awake()
        {
            _far = MakeMat(new Color(0.50f, 0.60f, 0.76f), 0.25f);
            _near = MakeMat(new Color(0.36f, 0.52f, 0.46f), 0.12f);
            _cloud = MakeMat(Color.white, 0.55f);

            BuildRing("MountainsFar", _far, FarRadius, 150f, 330f, 36, 11);
            BuildRing("MountainsNear", _near, NearRadius, 60f, 150f, 50, 23);
            // chmury usunięte na życzenie – zostały same góry
        }

        void BuildRing(string name, Material mat, float radius, float minH, float maxH, int segments, int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            // wysokości szczytów – kilka warstw szumu daje naturalną sylwetkę
            var h = new float[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float n = 0.5f + 0.25f * Mathf.Sin(a * 3f + seed) + 0.15f * Mathf.Sin(a * 7f + seed * 2f) + 0.1f * (float)rnd.NextDouble();
                if (i % 2 == 1) n *= 0.55f + 0.3f * (float)rnd.NextDouble(); // doliny między szczytami
                h[i] = Mathf.Lerp(minH, maxH, Mathf.Clamp01(n)) * (i % 2 == 0 ? 1f : 0.6f);
            }
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i - 1f) / segments * Mathf.PI * 2f, a1 = (i + 1f) / segments * Mathf.PI * 2f, am = i / (float)segments * Mathf.PI * 2f;
                var b0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * (radius + 70f);
                var b1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * (radius + 70f);
                var peak = new Vector3(Mathf.Cos(am), 0, Mathf.Sin(am)) * radius + Vector3.up * h[i];
                b0.y = b1.y = -20f;
                int k = verts.Count;
                verts.Add(b0); verts.Add(b1); verts.Add(peak);
                tris.Add(k); tris.Add(k + 2); tris.Add(k + 1); // ściany zwrócone do środka
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2); // i na zewnątrz (dwustronnie)
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            // normalne skierowane do środka i lekko w górę: góry są oświetlone jak ściana widziana z miasta (a nie czarne od spodu)
            var nrm = new Vector3[verts.Count];
            for (int i = 0; i < nrm.Length; i++) nrm[i] = (new Vector3(-verts[i].x, 0f, -verts[i].z).normalized + Vector3.up * 0.6f).normalized;
            mesh.SetNormals(nrm);
            mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void BuildClouds()
        {
            _clouds = new GameObject("Clouds").transform;
            _clouds.SetParent(transform, false);
            var rnd = new System.Random(5);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float dist = 180f + (float)rnd.NextDouble() * 420f;
                var center = new Vector3(Mathf.Cos(a) * dist, 190f + (float)rnd.NextDouble() * 110f, Mathf.Sin(a) * dist);
                var cloud = new GameObject("Cloud").transform;
                cloud.SetParent(_clouds, false);
                cloud.localPosition = center;
                int puffs = 4 + rnd.Next(0, 4);
                for (int p = 0; p < puffs; p++)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(s.GetComponent<Collider>());
                    s.transform.SetParent(cloud, false);
                    float sx = 30f + (float)rnd.NextDouble() * 35f;
                    s.transform.localPosition = new Vector3((p - puffs / 2f) * sx * 0.7f, (float)rnd.NextDouble() * 8f, ((float)rnd.NextDouble() - 0.5f) * 16f);
                    s.transform.localScale = new Vector3(sx, sx * 0.38f, sx * 0.7f);
                    var r = s.GetComponent<MeshRenderer>();
                    r.sharedMaterial = _cloud;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.position = new Vector3(cam.transform.position.x, 0f, cam.transform.position.z);
            if (_clouds != null) _clouds.Rotate(0f, 0.15f * Time.deltaTime, 0f); // powolny dryf
        }
    }
}
