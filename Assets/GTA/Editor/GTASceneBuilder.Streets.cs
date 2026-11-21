using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Ulice (część GTASceneBuilder):
    ///  • chodniki z płytami i krawężnikami, wnętrza kwartałów jako osobny obszar NavMesh (piesi trzymają się chodników)
    ///  • przejścia dla pieszych (zebry) na każdym wlocie skrzyżowania + linie zatrzymania
    ///  • sygnalizacja świetlna (TrafficSignal) – auta AI stają na czerwonym, piesi czekają na swoją kolej
    ///  • mała architektura: kosze, ławki, hydranty
    /// </summary>
    public static partial class GTASceneBuilder
    {
        const float CrosswalkOffset = 9.5f;   // środek zebry od środka skrzyżowania
        const float CrosswalkDepth = 3f;
        const float StopLineOffset = 11.6f;   // linia zatrzymania (za zebrą)
        const string TexDir = Root + "/Textures";

        // ---------------------------------------------------------------------
        //  Tekstury generowane w kodzie (bez zewnętrznych plików)
        // ---------------------------------------------------------------------

        static Texture2D SaveTexture(string name, Texture2D tex)
        {
            EnsureDir(TexDir);
            string path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Płyty chodnikowe: jedna tekstura = 2x2 płyty (2 m).</summary>
        static Texture2D PavingTexture()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGB24, true);
            var rnd = new System.Random(7);
            float[] tile = new float[4];
            for (int k = 0; k < 4; k++) tile[k] = 0.6f + (float)rnd.NextDouble() * 0.07f;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int tx = x / (S / 2), ty = y / (S / 2);
                int lx = x % (S / 2), ly = y % (S / 2);
                bool joint = lx < 2 || ly < 2;
                float v = joint ? 0.42f : tile[tx + ty * 2] + ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                t.SetPixel(x, y, new Color(v, v, v * 0.98f));
            }
            t.Apply();
            return SaveTexture("Paving", t);
        }

        /// <summary>Jeden pas zebry: połowa biała, połowa asfalt (powtarzane wzdłuż V).</summary>
        static Texture2D ZebraTexture()
        {
            const int S = 32;
            var t = new Texture2D(S, S, TextureFormat.RGB24, true);
            var white = new Color(0.92f, 0.92f, 0.88f);
            var asphalt = new Color(0.18f, 0.18f, 0.2f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                t.SetPixel(x, y, y < S / 2 ? white : asphalt);
            t.Apply();
            return SaveTexture("Zebra", t);
        }

        static Material TexturedMat(string name, Color tint, Texture2D tex, Vector2 tiling)
        {
            var m = Mat(name, tint);
            m.mainTexture = tex;
            m.mainTextureScale = tiling;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void NavArea(Transform parent, string name, Vector3 center, Vector3 size, int area)
        {
            var vol = Empty(name, parent, new Vector3(center.x, 0f, center.z)).gameObject.AddComponent<NavMeshModifierVolume>();
            vol.size = new Vector3(size.x, 4f, size.z);
            vol.center = Vector3.zero;
            vol.area = area;
        }

        // ---------------------------------------------------------------------
        //  Chodniki
        // ---------------------------------------------------------------------

        static Material _sidewalkMat;
        static Material SidewalkMat => _sidewalkMat != null ? _sidewalkMat
            : (_sidewalkMat = TexturedMat("Sidewalk", Color.white, PavingTexture(), new Vector2(BlockSize / 2f, BlockSize / 2f)));

        /// <summary>Płyta kwartału (chodnik) z krawężnikami. ring = szerokość chodnika widocznego dookoła.</summary>
        static void BuildSidewalk(Transform city, int bx, int bz, Vector3 c, float ring, bool lotArea)
        {
            Prim(PrimitiveType.Cube, $"Sidewalk_{bx}_{bz}", city, c + Vector3.up * 0.06f, new Vector3(BlockSize, 0.12f, BlockSize), SidewalkMat, true).isStatic = true;

            // krawężniki (tylko wizualne – kolizję daje płyta)
            var curb = Mat("Curb", new Color(0.78f, 0.78f, 0.75f));
            float e = BlockSize * 0.5f - 0.2f;
            Prim(PrimitiveType.Cube, "Curb", city, c + new Vector3(0, 0.08f, e), new Vector3(BlockSize, 0.17f, 0.4f), curb).isStatic = true;
            Prim(PrimitiveType.Cube, "Curb", city, c + new Vector3(0, 0.08f, -e), new Vector3(BlockSize, 0.17f, 0.4f), curb).isStatic = true;
            Prim(PrimitiveType.Cube, "Curb", city, c + new Vector3(e, 0.08f, 0), new Vector3(0.4f, 0.17f, BlockSize - 0.8f), curb).isStatic = true;
            Prim(PrimitiveType.Cube, "Curb", city, c + new Vector3(-e, 0.08f, 0), new Vector3(0.4f, 0.17f, BlockSize - 0.8f), curb).isStatic = true;

            // wnętrze kwartału: piesi mogą tam wejść (sklepy, park), ale spacerują chodnikiem
            if (lotArea)
            {
                float inner = BlockSize - ring * 2f;
                NavArea(city, $"Lot_{bx}_{bz}_NavArea", c, new Vector3(inner, 4f, inner), GameUtil.LotArea);
            }
        }

        /// <summary>Kosze, ławki i hydrant przy krawężniku.</summary>
        static void AddStreetFurniture(Transform parent, Vector3 c)
        {
            var binMat = Mat("TrashBin", new Color(0.18f, 0.35f, 0.22f));
            var concrete = Mat("BenchConcrete", new Color(0.62f, 0.6f, 0.57f));
            var wood = Mat("Wood", new Color(0.45f, 0.3f, 0.15f));
            var hydrant = Mat("Hydrant", new Color(0.8f, 0.12f, 0.1f));
            float edge = BlockSize * 0.5f;
            Vector3[] outward = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            int hydrantSide = Random.Range(0, 4);
            for (int s = 0; s < 4; s++)
            {
                Vector3 o = outward[s], t = Vector3.Cross(Vector3.up, o);
                float yaw = Quaternion.LookRotation(o).eulerAngles.y;
                // po jednej stronie środka (gdzie bywa latarnia) – trzymamy 3 m odstępu
                float along = Random.Range(4f, 14f) * (Random.value > 0.5f ? 1f : -1f);
                if (Random.value < 0.6f)
                {
                    Vector3 p = c + o * (edge - 0.7f) + t * along;
                    Prim(PrimitiveType.Cylinder, "TrashBin", parent, p + Vector3.up * 0.57f, new Vector3(0.55f, 0.45f, 0.55f), binMat, true).isStatic = true;
                }
                if (Random.value < 0.45f)
                {
                    Vector3 p = c + o * (edge - 1.0f) + t * (-along * 0.6f);
                    var packBench = PlacePack("bench_" + Random.Range(1, 3) + "_v_" + Random.Range(1, 4), parent, p, yaw, 1.0f, true);
                    if (packBench != null) { packBench.name = "Bench"; goto afterBench; }
                    var bench = new GameObject("Bench").transform;
                    bench.SetParent(parent, false);
                    bench.position = p;
                    bench.rotation = Quaternion.Euler(0, yaw, 0); // przodem do ulicy
                    Prim(PrimitiveType.Cube, "Base", bench, new Vector3(0, 0.33f, 0), new Vector3(1.9f, 0.42f, 0.55f), concrete, true).isStatic = true;
                    Prim(PrimitiveType.Cube, "Seat", bench, new Vector3(0, 0.57f, 0), new Vector3(2f, 0.07f, 0.6f), wood).isStatic = true;
                    Prim(PrimitiveType.Cube, "Back", bench, new Vector3(0, 0.88f, -0.27f), new Vector3(2f, 0.5f, 0.07f), wood).isStatic = true;
                }
                afterBench:;
                if (s == hydrantSide)
                {
                    Vector3 p = c + o * (edge - 0.6f) + t * (along > 0 ? -16f : 16f);
                    Prim(PrimitiveType.Cylinder, "Hydrant", parent, p + Vector3.up * 0.5f, new Vector3(0.32f, 0.38f, 0.32f), hydrant, true).isStatic = true;
                    Prim(PrimitiveType.Sphere, "HydrantCap", parent, p + Vector3.up * 0.9f, new Vector3(0.36f, 0.25f, 0.36f), hydrant).isStatic = true;
                }
            }
        }

        // ---------------------------------------------------------------------
        //  Zebry, linie zatrzymania, sygnalizacja
        // ---------------------------------------------------------------------

        /// <summary>Kierunki wylotów ze skrzyżowania (i,j) siatki: ulice miasta + zjazdy na obwodnicę.</summary>
        static List<(Vector3 dir, bool grid)> NodeExits(int i, int j)
        {
            var list = new List<(Vector3, bool)>();
            if (i > 0) list.Add((Vector3.left, true));
            if (i < Blocks) list.Add((Vector3.right, true));
            if (j > 0) list.Add((Vector3.back, true));
            if (j < Blocks) list.Add((Vector3.forward, true));
            bool con(int k) => System.Array.IndexOf(Connectors, k) >= 0;
            if (i == 0 && con(j)) list.Add((Vector3.left, false));
            if (i == Blocks && con(j)) list.Add((Vector3.right, false));
            if (j == 0 && con(i)) list.Add((Vector3.back, false));
            if (j == Blocks && con(i)) list.Add((Vector3.forward, false));
            return list;
        }

        static void BuildCrossings(Transform roads)
        {
            var zebraMat = TexturedMat("Crosswalk", Color.white, ZebraTexture(), new Vector2(1f, 11f));
            var white = Mat("RoadLine", new Color(0.9f, 0.9f, 0.85f));
            var signals = new GameObject("TrafficSignals").transform;
            signals.SetParent(roads.parent, false);

            for (int i = 0; i <= Blocks; i++)
            for (int j = 0; j <= Blocks; j++)
            {
                var center = new Vector3(RoadCenter(i), 0f, RoadCenter(j));
                var exits = NodeExits(i, j);
                foreach (var (n, grid) in exits)
                {
                    bool alongX = Mathf.Abs(n.x) > 0.5f;
                    Vector3 zp = center + n * CrosswalkOffset;
                    if (grid)
                    {
                        // zebra w poprzek ulicy (pasy równoległe do ruchu)
                        Prim(PrimitiveType.Cube, "Crosswalk", roads, zp + Vector3.up * 0.027f, new Vector3(CrosswalkDepth, 0.01f, RoadWidth - 1f), zebraMat,
                            false, new Vector3(0, alongX ? 0f : 90f, 0)).isStatic = true;
                        NavArea(roads, "Crosswalk_NavArea", zp, alongX ? new Vector3(CrosswalkDepth, 4f, RoadWidth) : new Vector3(RoadWidth, 4f, CrosswalkDepth), GameUtil.CrosswalkArea);
                    }
                    // linia zatrzymania na pasie wjeżdżającym (auta jadą w kierunku -n, prawy pas)
                    Vector3 d = -n, right = Vector3.Cross(Vector3.up, d);
                    Vector3 sl = center - d * StopLineOffset + right * (RoadWidth * 0.25f);
                    Prim(PrimitiveType.Cube, "StopLine", roads, sl + Vector3.up * 0.028f,
                        alongX ? new Vector3(0.5f, 0.01f, RoadWidth * 0.5f - 0.6f) : new Vector3(RoadWidth * 0.5f - 0.6f, 0.01f, 0.5f), white).isStatic = true;
                }
                if (exits.Count >= 3) BuildSignal(signals, center, exits, i, j);
            }
        }

        static void BuildSignal(Transform parent, Vector3 center, List<(Vector3 dir, bool grid)> exits, int i, int j)
        {
            var root = new GameObject($"Signal_{i}_{j}");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            var sig = root.AddComponent<TrafficSignal>();
            sig.offset = ((i + j) % 4) * 4f; // "zielona fala" – sąsiednie skrzyżowania przesunięte w fazie
            sig.redOn = SignalMat("SignalRedOn", new Color(1f, 0.12f, 0.08f));
            sig.yellowOn = SignalMat("SignalYellowOn", new Color(1f, 0.7f, 0.05f));
            sig.greenOn = SignalMat("SignalGreenOn", new Color(0.1f, 1f, 0.35f));
            sig.lampOff = Mat("SignalOff", new Color(0.24f, 0.24f, 0.22f));
            var pole = Mat("LampPole", new Color(0.2f, 0.2f, 0.22f));
            var head = Mat("SignalHead", new Color(0.12f, 0.12f, 0.1f));

            var xr = new List<Renderer>(); var xy = new List<Renderer>(); var xg = new List<Renderer>();
            var zr = new List<Renderer>(); var zy = new List<Renderer>(); var zg = new List<Renderer>();

            foreach (var (n, _) in exits)
            {
                // auta nadjeżdżające z kierunku n jadą w stronę d = -n; maszt stoi na ich prawym chodniku
                Vector3 d = -n, right = Vector3.Cross(Vector3.up, d);
                bool alongX = Mathf.Abs(d.x) > 0.5f;
                Vector3 polePos = center - d * StopLineOffset + right * (RoadWidth * 0.5f + 0.6f);
                var mast = new GameObject("Mast").transform;
                mast.SetParent(root.transform, false);
                mast.position = polePos;
                mast.rotation = Quaternion.LookRotation(-d); // przód głowicy patrzy na nadjeżdżające auta
                Prim(PrimitiveType.Cylinder, "Pole", mast, new Vector3(0, 2.8f, 0), new Vector3(0.22f, 2.8f, 0.22f), pole, true).isStatic = true;
                // ramię nad jezdnią (lokalnie: +x mastu = w lewo od kierunku jazdy = nad pas)
                float reach = RoadWidth * 0.5f + 0.6f - RoadWidth * 0.25f;
                Prim(PrimitiveType.Cube, "Arm", mast, new Vector3(reach * 0.5f, 5.5f, 0), new Vector3(reach + 0.2f, 0.14f, 0.14f), pole).isStatic = true;

                // głowica nad pasem + mała głowica na maszcie (widoczna z bliska)
                foreach (var (pos, scale) in new[] { (new Vector3(reach, 4.85f, 0), 1f), (new Vector3(0, 3.0f, 0.18f), 0.75f) })
                {
                    Prim(PrimitiveType.Cube, "Head", mast, pos, new Vector3(0.48f, 1.3f, 0.32f) * scale, head).isStatic = true;
                    var r = Lamp(mast, pos + new Vector3(0, 0.4f * scale, 0.17f * scale), scale, sig.lampOff);
                    var y = Lamp(mast, pos + new Vector3(0, 0f, 0.17f * scale), scale, sig.lampOff);
                    var g = Lamp(mast, pos + new Vector3(0, -0.4f * scale, 0.17f * scale), scale, sig.lampOff);
                    (alongX ? xr : zr).Add(r);
                    (alongX ? xy : zy).Add(y);
                    (alongX ? xg : zg).Add(g);
                }
            }
            sig.xRed = xr.ToArray(); sig.xYellow = xy.ToArray(); sig.xGreen = xg.ToArray();
            sig.zRed = zr.ToArray(); sig.zYellow = zy.ToArray(); sig.zGreen = zg.ToArray();
        }

        /// <summary>Świecąca soczewka – mocna emisja, widoczna także w dzień i w cieniu.</summary>
        static Material SignalMat(string name, Color c)
        {
            var m = Mat(name, c, true);
            m.SetColor("_EmissionColor", c * 4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetFloat("_Glossiness", 0.9f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Renderer Lamp(Transform parent, Vector3 localPos, float scale, Material off)
        {
            var go = Prim(PrimitiveType.Cylinder, "Lamp", parent, localPos, new Vector3(0.3f, 0.03f, 0.3f) * scale, off, false, new Vector3(90f, 0f, 0f));
            return go.GetComponent<Renderer>();
        }
    }
}
