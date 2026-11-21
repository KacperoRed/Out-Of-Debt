using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine.AI;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Elementy z paczek ithappy (Cartoon City, Casino) wstawiane przez generator miasta.
    /// Jeśli paczki nie ma w projekcie, wszystkie funkcje po cichu nic nie robią i miasto buduje się z prymitywów.
    /// </summary>
    public static partial class GTASceneBuilder
    {
        static readonly Dictionary<string, GameObject> PackCache = new Dictionary<string, GameObject>();

        static GameObject PackPrefab(string name)
        {
            if (PackCache.TryGetValue(name, out var cached)) return cached;
            GameObject found = null;
            // prefaby (ithappy) albo same modele .fbx (np. paczka „Decorations”)
            var folders = new List<string>();
            if (System.IO.Directory.Exists("Assets/ithappy")) folders.Add("Assets/ithappy");
            if (System.IO.Directory.Exists("Assets/Decorations")) folders.Add("Assets/Decorations");
            if (folders.Count > 0)
                foreach (var filter in new[] { " t:Prefab", " t:Model" })
                {
                    foreach (var guid in AssetDatabase.FindAssets(name + filter, folders.ToArray()))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (System.IO.Path.GetFileNameWithoutExtension(p) == name) { found = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
                    }
                    if (found != null) break;
                }
            PackCache[name] = found;
            return found;
        }

        static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>
        /// Wstawia prefab z paczki. Skaluje do docelowej wysokości (targetHeight &gt; 0) i stawia dolną krawędzią na pozycji.
        /// colliders: dla budynków dodaje jeden BoxCollider z granic modelu, dla drobnicy usuwa wszystkie.
        /// </summary>
        static GameObject PlacePack(string prefabName, Transform parent, Vector3 worldPos, float yaw, float targetHeight = 0f, bool box = false, float maxFootprint = 0f)
        {
            var src = PackPrefab(prefabName);
            if (src == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetParent(parent, true);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.position = worldPos;

            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) if (mb != null) Object.DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
            foreach (var l in go.GetComponentsInChildren<Light>()) l.shadows = LightShadows.None;

            var b = RenderBounds(go);
            if (b.size.y > 0.001f)
            {
                float s = targetHeight > 0f ? targetHeight / b.size.y : 1f;
                if (maxFootprint > 0f)
                {
                    float fp = Mathf.Max(b.size.x, b.size.z) * s;
                    if (fp > maxFootprint) s *= maxFootprint / fp;
                }
                go.transform.localScale *= s;
                b = RenderBounds(go);
                go.transform.position += new Vector3(worldPos.x - b.center.x, worldPos.y - b.min.y, worldPos.z - b.center.z);
                b = RenderBounds(go);
            }
            if (box)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = go.transform.InverseTransformPoint(b.center);
                var inv = go.transform.lossyScale;
                col.size = new Vector3(b.size.x / Mathf.Abs(inv.x), b.size.y / Mathf.Abs(inv.y), b.size.z / Mathf.Abs(inv.z));
            }
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        // ---------------------------------------------------------------- budynki miasta

        static int _packBuildings, _fallbackBuildings, _nightBuildings;

        /// <summary>
        /// Wstawia budynek z Cartoon City dopasowany do działki w×d.
        /// stretch = rozciągnij dokładnie do w×d (magazyny); maxHeight &gt; 0 = ogranicz wysokość; targetHeight &gt; 0 = wymuś wysokość;
        /// collider = dodaj BoxCollider. Zwraca null, gdy paczki nie ma (wtedy zostaje prymityw).
        /// </summary>
        static GameObject TryPackBuilding(Transform parent, Vector3 lot, float w, float d, bool tall, float yBase = 0.12f,
            float maxHeight = 0f, float targetHeight = 0f, bool stretch = false, bool collider = true, float yaw = -1f)
        {
            string[] names = tall
                ? new[] { "Regular_Building_TwistedTower_Large", "Eco_Building_Grid", "Eco_Building_Terrace", "Eco_Building_Slope" }
                : new[] { "Eco_Building_Grid", "Eco_Building_Terrace", "Eco_Building_Slope" };
            string pick = names[Random.Range(0, names.Length)];
            if (PackPrefab(pick) == null) { _fallbackBuildings++; return null; }

            var go = PlacePack(pick, parent, lot, yaw >= 0f ? yaw : 90f * Random.Range(0, 4), 0f, false);
            if (go == null) { _fallbackBuildings++; return null; }
            var b = RenderBounds(go);
            float sx = w / Mathf.Max(0.1f, b.size.x), sz = d / Mathf.Max(0.1f, b.size.z);
            Vector3 mul;
            if (stretch) mul = new Vector3(sx, 1f, sz);
            else { float f = Mathf.Min(sx, sz); mul = new Vector3(f, f, f); }
            if (stretch && targetHeight <= 0f && maxHeight <= 0f) mul.y = Mathf.Min(sx, sz);
            go.transform.localScale = Vector3.Scale(go.transform.localScale, mul);
            b = RenderBounds(go);
            if (targetHeight > 0f) go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1f, targetHeight / Mathf.Max(0.1f, b.size.y), 1f));
            else if (maxHeight > 0f && b.size.y > maxHeight) go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1f, maxHeight / b.size.y, 1f));
            b = RenderBounds(go);
            go.transform.position += new Vector3(lot.x - b.center.x, yBase - b.min.y, lot.z - b.center.z);

            if (collider)
            {
                // dokładny kształt: MeshCollider na każdej bryle budynku (tarasy, skosy i przewieszenia nie mają niewidzialnych scian)
                int meshCols = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    meshCols++;
                }
                if (meshCols == 0)
                {
                    b = RenderBounds(go);
                    var col = go.AddComponent<BoxCollider>();
                    col.center = go.transform.InverseTransformPoint(b.center);
                    var ls = go.transform.lossyScale;
                    col.size = new Vector3(b.size.x / Mathf.Abs(ls.x), b.size.y / Mathf.Abs(ls.y), b.size.z / Mathf.Abs(ls.z));
                }
            }
            go.name = "Building_" + pick;
            _packBuildings++;

            // wariant z podświetlonymi oknami (…_NightLight): zapisujemy pary materiałów dzień/noc dla każdego renderera,
            // a w nocy NightVariant podmienia same materiały (geometria budynku się nie zmienia)
            var nightSrc = PackPrefab(pick + "_NightLight");
            if (nightSrc != null)
            {
                var n = (GameObject)PrefabUtility.InstantiatePrefab(nightSrc);
                var nightByPath = new Dictionary<string, Renderer>();
                foreach (var r in n.GetComponentsInChildren<Renderer>(true)) nightByPath[RelPath(n.transform, r.transform)] = r;

                var nv = go.AddComponent<NightVariant>();
                var dayList = go.GetComponentsInChildren<Renderer>(true);
                var nightList = n.GetComponentsInChildren<Renderer>(true);
                for (int ri = 0; ri < dayList.Length; ri++)
                {
                    var r = dayList[ri];
                    // dopasowanie po ścieżce; gdy nazwy obiektów w wariancie nocnym są inne – po kolejności (ta sama struktura modelu)
                    if (!nightByPath.TryGetValue(RelPath(go.transform, r.transform), out var nr))
                    {
                        if (dayList.Length != nightList.Length) continue;
                        nr = nightList[ri];
                    }
                    var dm = r.sharedMaterials;
                    var nm = nr.sharedMaterials;
                    if (dm.Length != nm.Length) continue;
                    bool differs = false;
                    for (int i = 0; i < dm.Length; i++) if (dm[i] != nm[i]) differs = true;
                    if (!differs) continue;
                    nv.slots.Add(new NightVariant.Slot { renderer = r, day = dm, night = nm });
                }
                Object.DestroyImmediate(n);
                if (nv.slots.Count == 0) Object.DestroyImmediate(nv);
                else
                {
                    // podmiana materiałów w czasie gry – nie łączymy tych obiektów w statyczne batche
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
                    _nightBuildings++;
                }
            }
            return go;
        }

        static string RelPath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Drobne rekwizyty z Cartoon City na chodnikach (bez colliderów, żeby nie blokować przechodniów).</summary>
        static void AddPackStreetProps(Transform parent, Vector3 c)
        {
            if (PackPrefab("Trash_Can_04") == null) return;
            float edge = BlockSize * 0.5f - 0.8f;
            string[] cans = { "Trash_Can_04", "Trash_Can_05", "Trash_Can_06", "Trash_Can_07", "Trash_Can_08" };
            string[] greens = { "Bush_06", "Bush_07", "Bush_10", "Palm_03" };
            for (int s = 0; s < 4; s++)
            {
                Vector3 o = s == 0 ? Vector3.forward : s == 1 ? Vector3.back : s == 2 ? Vector3.right : Vector3.left;
                Vector3 t = Vector3.Cross(Vector3.up, o);
                float yaw = Quaternion.LookRotation(o).eulerAngles.y;
                if (Random.value < 0.5f)
                    PlacePack(cans[Random.Range(0, cans.Length)], parent, c + o * edge + t * Random.Range(-18f, 18f), yaw, 1.0f);
            }
            if (Random.value < 0.18f)
                PlacePack("Bus_Stop_02", parent, c + Vector3.forward * edge + Vector3.right * 6f, 0f, 3f);
            if (Random.value < 0.12f)
                PlacePack("Graffiti_03", parent, c + Vector3.back * (edge + 0.4f), 0f, 2.5f);
        }

        // ---------------------------------------------------------------- wyciecie otworu w modelu budynku

        struct CutVert { public Vector3 p, n; public Vector2 uv; }

        static CutVert Lerp(CutVert a, CutVert b, float t) => new CutVert { p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t) };

        static void SplitPoly(List<CutVert> poly, int i, Vector4[] planes, List<List<CutVert>> keep)
        {
            if (poly.Count < 3 || i >= planes.Length) return; // reszta lezy wewnatrz prostopadloscianu – odrzucamy
            var pl = planes[i];
            var inside = new List<CutVert>();
            var outside = new List<CutVert>();
            for (int k = 0; k < poly.Count; k++)
            {
                var a = poly[k];
                var b = poly[(k + 1) % poly.Count];
                float da = pl.x * a.p.x + pl.y * a.p.y + pl.z * a.p.z + pl.w;
                float db = pl.x * b.p.x + pl.y * b.p.y + pl.z * b.p.z + pl.w;
                if (da >= 0f) inside.Add(a); else outside.Add(a);
                if ((da >= 0f) != (db >= 0f))
                {
                    var m = Lerp(a, b, da / (da - db));
                    inside.Add(m);
                    outside.Add(m);
                }
            }
            if (outside.Count >= 3) keep.Add(outside);
            SplitPoly(inside, i + 1, planes, keep);
        }

        /// <summary>
        /// Wycina z siatek budynku wszystko, co lezy wewnatrz prostopadloscianu (swiat) – np. otwor drzwiowy.
        /// Trojkaty przecinajace brzeg sa obcinane, nowa siatka trafia do Assets/GTA/Generated/Meshes.
        /// </summary>
        static void CutBox(GameObject building, Bounds box)
        {
            System.IO.Directory.CreateDirectory("Assets/GTA/Generated/Meshes");
            int n = 0;
            foreach (var mf in building.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var m = mf.transform.localToWorldMatrix.transpose;
                Vector4 PlaneLocal(Vector3 normal, Vector3 point) => m * new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point));
                var planes = new[]
                {
                    PlaneLocal(Vector3.right, box.min), PlaneLocal(Vector3.left, box.max),
                    PlaneLocal(Vector3.up, box.min), PlaneLocal(Vector3.down, box.max),
                    PlaneLocal(Vector3.forward, box.min), PlaneLocal(Vector3.back, box.max),
                };
                var vs = mesh.vertices; var ns = mesh.normals; var uvs = mesh.uv;
                bool hasN = ns.Length == vs.Length, hasUv = uvs.Length == vs.Length;
                var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUv = new List<Vector2>();
                var subTris = new List<int[]>();
                bool changed = false;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    var list = new List<int>();
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        var poly = new List<CutVert>(3);
                        for (int k = 0; k < 3; k++)
                        {
                            int vi = tris[t + k];
                            poly.Add(new CutVert { p = vs[vi], n = hasN ? ns[vi] : Vector3.up, uv = hasUv ? uvs[vi] : Vector2.zero });
                        }
                        var keep = new List<List<CutVert>>();
                        SplitPoly(poly, 0, planes, keep);
                        if (keep.Count != 1 || keep[0].Count != 3) changed = true;
                        foreach (var pg in keep)
                            for (int k = 1; k + 1 < pg.Count; k++)
                                foreach (var v in new[] { pg[0], pg[k], pg[k + 1] })
                                {
                                    list.Add(outV.Count);
                                    outV.Add(v.p); outN.Add(v.n); outUv.Add(v.uv);
                                }
                    }
                    subTris.Add(list.ToArray());
                }
                if (!changed) continue;
                var cut = new Mesh { name = mesh.name + "_cut", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                cut.SetVertices(outV); cut.SetNormals(outN); cut.SetUVs(0, outUv);
                cut.subMeshCount = subTris.Count;
                for (int sm = 0; sm < subTris.Count; sm++) cut.SetTriangles(subTris[sm], sm);
                cut.RecalculateBounds();
                string path = $"Assets/GTA/Generated/Meshes/{building.name}_{++_cutCounter}.asset";
                AssetDatabase.CreateAsset(cut, path);
                mf.sharedMesh = cut;
                n++;
            }
        }

        static int _cutCounter;

        /// <summary>
        /// Dopasowuje do działki PODSTAWĘ budynku (wierzchołki tuż nad ziemią), a nie cały obrys z balkonami i tarasami.
        /// Dzięki temu ściany parteru pokrywają się ze ścianami wnętrza i nie wystaje spod nich żadne „pudełko”.
        /// </summary>
        static void FitGroundFootprint(GameObject building, Vector3 center, float gw, float gd, float groundY)
        {
            bool Ground(out Vector2 min, out Vector2 max)
            {
                min = new Vector2(float.MaxValue, float.MaxValue); max = new Vector2(float.MinValue, float.MinValue);
                bool any = false;
                foreach (var mf in building.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var w = mf.transform.TransformPoint(v);
                        if (w.y > groundY + 1.2f) continue;
                        any = true;
                        min = Vector2.Min(min, new Vector2(w.x, w.z)); max = Vector2.Max(max, new Vector2(w.x, w.z));
                    }
                }
                return any;
            }
            if (!Ground(out var mn, out var mx)) return;
            float ex = mx.x - mn.x, ez = mx.y - mn.y;
            if (ex < 1f || ez < 1f) return;
            float mulX = Mathf.Clamp(gw / ex, 0.6f, 1.7f), mulZ = Mathf.Clamp(gd / ez, 0.6f, 1.7f);
            building.transform.localScale = Vector3.Scale(building.transform.localScale, new Vector3(mulX, 1f, mulZ));
            if (!Ground(out mn, out mx)) return;
            Vector2 c = (mn + mx) * 0.5f;
            building.transform.position += new Vector3(center.x - c.x, 0f, center.z - c.y);
        }

        // ---------------------------------------------------------------- meble (Cute Furniture)

        /// <summary>
        /// Wyposazenie wnetrz meblami z paczki Cute Furniture. Uklad lokalny jak w kasynie: +X to strona drzwi, tyl to -X.
        /// Zastepuje proste prymitywy (lozko, sofa, stolki...), gdy paczka jest w projekcie.
        /// </summary>
        static void FurnishInterior(Transform root, ShopType type, float w, float d, float floorY)
        {
            if (PackPrefab("Couch_08") == null && PackPrefab("Bed_02") == null) return;
            var deco = new GameObject("Furniture").transform;
            deco.SetParent(root, false);
            float ry = root.eulerAngles.y;
            float toDoor = ry + 90f, toBack = ry - 90f, toLeft = ry, toRight = ry + 180f; // kierunek, w ktory patrzy mebel
            Vector3 Wp(float lx, float lz, float ly = 0f) => root.TransformPoint(new Vector3(lx, floorY + 0.05f + ly, lz));
            void F(string n, float lx, float lz, float yaw, float h, bool col = true, float ly = 0f) => PlacePack(n, deco, Wp(lx, lz, ly), yaw, h, col);
            void Remove(params string[] names)
            {
                foreach (var n in names)
                    for (int i = root.childCount - 1; i >= 0; i--)
                        if (root.GetChild(i).name == n) Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
            float bx = -w / 2f;

            switch (type)
            {
                case ShopType.Safehouse:
                    Remove("Bed", "Pillow", "Sofa", "Table");
                    F("Bed_02", bx + 2.6f, -d / 2 + 3.2f, toDoor, 1.1f);
                    F("Nightstand_02", bx + 1.1f, -d / 2 + 5.6f, toDoor, 0.7f);
                    F("Closet_01", bx + 1.2f, d / 2 - 2.6f, toDoor, 2.5f);
                    F("Couch_08", 1f, d / 2 - 2.2f, toRight, 1.1f);
                    F("Coffee_Table_03", 1f, d / 2 - 4.7f, toDoor, 0.55f);
                    F("Armchair_18", 5.5f, d / 2 - 4f, toRight + 45f, 1.1f);
                    F("Fridge_01", 5f, -d / 2 + 1.2f, toLeft, 2.1f);
                    F("Kitchen_D_06", 7.4f, -d / 2 + 1.2f, toLeft, 1.05f);
                    F("Kitchen_D_09", 9.4f, -d / 2 + 1.2f, toLeft, 1.05f);
                    F("Plants_05", w / 2 - 1.5f, d / 2 - 1.5f, 0f, 1.5f, false);
                    F("Picture_08", bx + 0.5f, 0f, toDoor, 1.1f, false, 2.0f);
                    F("Clock_03", bx + 0.5f, 4f, toDoor, 0.6f, false, 3.4f);
                    break;

                case ShopType.Bar:
                    Remove("Stool", "TableTop", "TableLeg");
                    for (int i = 0; i < 5; i++) F("Chair_17", bx + 4.6f, -d / 2 + 4.5f + i * 2.2f, toBack, 0.95f);
                    for (int i = 0; i < 3; i++)
                    {
                        float tx = 2.5f + (i % 2) * 2.5f, tz = -5f + i * 5f;
                        F("Kitchen_Table_09", tx, tz, toDoor, 0.85f);
                        F("Chair_17", tx - 1.3f, tz, toDoor, 0.95f);
                        F("Chair_17", tx + 1.3f, tz, toBack, 0.95f);
                    }
                    F("Armchair_02", w / 2 - 2f, d / 2 - 2f, toRight - 45f, 1.1f);
                    F("Plants_19", w / 2 - 1.5f, -d / 2 + 1.5f, 0f, 1.6f, false);
                    F("Guitar_01", bx + 0.5f, -4f, toDoor, 1.0f, false, 1.8f);
                    F("Picture_17", bx + 0.5f, 5f, toDoor, 1.1f, false, 2.4f);
                    break;

                case ShopType.Store:
                    F("Plants_15", w / 2 - 1.5f, -d / 2 + 1.5f, 0f, 1.7f, false);
                    F("Plants_05", w / 2 - 1.5f, d / 2 - 1.5f, 0f, 1.4f, false);
                    F("Clock_03", bx + 0.5f, 5f, toDoor, 0.7f, false, 3.6f);
                    F("Picture_21", w / 2 - 6f, d / 2 - 0.5f, toRight, 1.2f, false, 2.4f);
                    F("Microwave_01", w / 2 - 4.5f, d / 2 - 3.2f, toRight, 0.45f, false, 1.1f);
                    F("Computer_01", w / 2 - 3.6f, d / 2 - 3.2f, toRight, 0.5f, false, 1.1f);
                    break;

                case ShopType.Weapons:
                    foreach (float cz in new[] { -d / 2 + 2f, -d / 2 + 4.5f, d / 2 - 2f, d / 2 - 4.5f })
                        F("Closet_02", cz < 0 ? 3f : 3f, cz < 0 ? -d / 2 + 1.1f : d / 2 - 1.1f, cz < 0 ? toLeft : toRight, 2.4f);
                    F("Work_Table_06", w / 2 - 4f, 0f, toDoor, 0.9f);
                    F("Chair_PC_04", w / 2 - 5.6f, 0f, toDoor, 1.0f);
                    F("Computer_01", w / 2 - 4f, 0f, toBack, 0.5f, false, 0.9f);
                    F("Plants_19", w / 2 - 1.5f, d / 2 - 1.5f, 0f, 1.6f, false);
                    F("Clock_03", bx + 0.5f, d / 2 - 3f, toDoor, 0.7f, false, 3.6f);
                    break;

                case ShopType.Repair:
                    F("Work_Table_06", bx + 3f, -d / 2 + 2f, toDoor, 0.95f);
                    F("Closet_02", bx + 1.2f, -d / 2 + 5.5f, toDoor, 2.3f);
                    F("Chair_PC_04", bx + 5f, -d / 2 + 2f, toBack, 1.0f);
                    F("Computer_01", bx + 3f, -d / 2 + 2f, toDoor, 0.5f, false, 0.95f);
                    F("Fridge_01", bx + 1.2f, d / 2 - 2f, toDoor, 2.0f);
                    F("Clock_03", bx + 0.5f, 0f, toDoor, 0.8f, false, 4.4f);
                    break;
            }
        }

        // ---------------------------------------------------------------- kasyno

        /// <summary>Wyposażenie kasyna z paczki Casino. Układ lokalny: oś X wskazuje drzwi, tył budynku to −X.</summary>
        /// <summary>Stół ruletki: sukno, drewniana podstawa i obracające się koło z kolorowymi polami.</summary>
        static void BuildRoulette(Transform parent, Vector3 c)
        {
            var rt = new GameObject("RouletteTable").transform;
            rt.SetParent(parent, false);
            rt.localPosition = c;
            var felt = Mat("RouletteFelt", new Color(0.05f, 0.45f, 0.2f));
            var wood = Mat("RouletteWood", new Color(0.3f, 0.15f, 0.07f));
            var gold = Mat("RouletteGold", new Color(0.92f, 0.72f, 0.2f));
            var red = Mat("RouletteRed", new Color(0.75f, 0.08f, 0.08f));
            var black = Mat("RouletteBlack", new Color(0.08f, 0.08f, 0.08f));
            Prim(PrimitiveType.Cube, "Base", rt, new Vector3(0f, 0.4f, 0f), new Vector3(3.4f, 0.8f, 1.7f), wood, true);
            Prim(PrimitiveType.Cube, "Felt", rt, new Vector3(0f, 0.83f, 0f), new Vector3(3.5f, 0.08f, 1.8f), felt, false);
            for (int i = 0; i < 6; i++)   // pola zakładów na suknie
                Prim(PrimitiveType.Cube, "Bet" + i, rt, new Vector3(0.1f + i * 0.28f, 0.88f, 0f), new Vector3(0.22f, 0.02f, 1.2f), i % 2 == 0 ? red : black, false);
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(rt, false);
            wheel.localPosition = new Vector3(-1.05f, 0.88f, 0f);
            Prim(PrimitiveType.Cylinder, "WheelBody", wheel, new Vector3(0f, 0.04f, 0f), new Vector3(1.3f, 0.04f, 1.3f), wood, false);
            Prim(PrimitiveType.Cylinder, "WheelRing", wheel, new Vector3(0f, 0.09f, 0f), new Vector3(1.05f, 0.02f, 1.05f), gold, false);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                var seg = Prim(PrimitiveType.Cube, "Pocket" + i, wheel, new Vector3(Mathf.Cos(a) * 0.38f, 0.12f, Mathf.Sin(a) * 0.38f), new Vector3(0.16f, 0.03f, 0.1f), i % 2 == 0 ? red : black, false);
                seg.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            Prim(PrimitiveType.Sphere, "Hub", wheel, new Vector3(0f, 0.16f, 0f), new Vector3(0.18f, 0.18f, 0.18f), gold, false);
            var tab = rt.gameObject.AddComponent<RouletteTable>();
            tab.wheel = wheel;
            tab.radius = 3.4f;
        }

        static void DecorateCasino(Transform root, float w, float d, float floorY)
        {
            if (PackPrefab("Slot_Machine_01") == null && PackPrefab("Table_01") == null) return;
            var deco = new GameObject("CasinoDecor").transform;
            deco.SetParent(root, false);
            float y = floorY + 0.05f;
            Vector3 W(float lx, float lz) => root.TransformPoint(new Vector3(lx, y, lz));
            float yawBack = root.eulerAngles.y + 90f;  // twarzą do wnętrza (+X lokalnie)

            // automaty pod tylną ścianą
            string[] slots = { "Slot_Machine_01", "Slot_Machine_02", "Slot_01", "Slot_02", "Slot_03", "Slot_04" };
            int n = Mathf.FloorToInt((d - 5f) / 1.6f);
            for (int i = 0; i < n; i++)
                PlacePack(slots[i % slots.Length], deco, W(-w / 2 + 1.5f, -d / 2 + 2.5f + i * 1.6f), yawBack, 2.1f);

            // stoły do gry (środek) + karty / żetony
            string[] tables = { "Table_01", "Table_06" };
            for (int i = 0; i < 4; i++)
            {
                float tx = -w / 2 + 5.5f + (i % 2) * 5f, tz = (i / 2 == 0 ? -1f : 1f) * 3.2f;
                var tb = PlacePack(tables[i % 2], deco, W(tx, tz), root.eulerAngles.y, 0.95f);
                PlacePack("Tablecloth_01", deco, W(tx, tz) + Vector3.up * 0.9f, root.eulerAngles.y, 0.03f);
                PlacePack("Card_" + (new[] { "39", "44", "48", "53" })[i], deco, W(tx, tz) + Vector3.up * 0.97f, Random.Range(0, 360), 0.02f);
            }

            BuildRoulette(deco, new Vector3(4.6f, floorY, 0f));

            // kolumny, neon, bankomaty
            foreach (float cz in new[] { -d / 2 + 1.5f, d / 2 - 1.5f })
                PlacePack("Column_17", deco, W(w / 2 - 3f, cz), root.eulerAngles.y, 4.6f);
            PlacePack("Neon_Sign_01", deco, W(-w / 2 + 0.6f, 0f), yawBack, 1.4f).transform.position += Vector3.up * 2.8f;
            PlacePack("ATM_01", deco, W(0f, d / 2 - 0.8f), root.eulerAngles.y + 180f, 2.0f);
            PlacePack("ATM_03", deco, W(2f, d / 2 - 0.8f), root.eulerAngles.y + 180f, 2.0f);
            PlacePack("Couch_05", deco, W(w / 2 - 2.5f, 0f), root.eulerAngles.y - 90f, 0.9f);
            PlacePack("Lamp_05", deco, W(0f, 0f), 0f, 0.5f);

            // skarbiec: sejf + złoto + gotówka w rogu (cel napadu)
            Vector3 vault = new Vector3(-w / 2 + 2f, 0, d / 2 - 2f);
            PlacePack("SafeBox_01", deco, W(vault.x, vault.z), yawBack, 1.7f);
            PlacePack("Gold_Ingot_01", deco, W(vault.x + 1.4f, vault.z), 0f, 0.15f);
            PlacePack("Gold_Ingot_02", deco, W(vault.x + 1.4f, vault.z - 0.4f), 20f, 0.15f);
            PlacePack("Cash_01", deco, W(vault.x + 1.4f, vault.z - 0.9f), 10f, 0.15f);
        }

        // ---------------------------------------------------------------- walidacja po zbudowaniu miasta

        /// <summary>
        /// Sprawdza, czy każdy budynek z wnętrzem jest dostępny (NavMesh przed drzwiami, droga do strefy sklepu, drzwi niezablokowane)
        /// oraz czy punkty startu misji leżą na chodzie/ulicy. Wynik w Console i w Assets/GTA/Generated/BuildReport.txt.
        /// </summary>
        static void ValidateCity(List<ShopZone> shops)
        {
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            int problems = 0;
            sb.AppendLine($"Budynki z paczki Cartoon City: {_packBuildings} (z nocnym podświetleniem okien: {_nightBuildings}), zastąpione prymitywami (brak prefabu): {_fallbackBuildings}");

            foreach (var z in shops)
            {
                if (z == null) continue;
                var root = z.transform.parent;
                var doorT = root.Find("Door");
                Vector3 doorPos = doorT != null ? doorT.position : (root.Find("Awning") != null ? root.Find("Awning").position : root.position);
                doorPos.y = root.position.y + 0.15f;   // markiza wisi wysoko – liczymy od poziomu chodnika
                Vector3 outward = root.TransformDirection(Vector3.right);
                Vector3 outside = doorPos + outward * 4f;
                Vector3 inside = z.transform.position;

                bool okOut = NavMesh.SamplePosition(outside, out var hOut, 4f, NavMesh.AllAreas);
                bool okIn = NavMesh.SamplePosition(inside, out var hIn, 4f, NavMesh.AllAreas);
                bool reach = false;
                if (okOut && okIn)
                {
                    var path = new NavMeshPath();
                    reach = NavMesh.CalculatePath(hOut.position, hIn.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                }
                // czy w progu drzwi coś stoi (np. budynek z paczki wchodzący na wejście)
                var blockers = Physics.OverlapBox(doorPos - outward * 0.5f + Vector3.up * 1.4f, new Vector3(0.4f, 1.2f, 1.0f), root.rotation);
                int blocked = 0;
                foreach (var c in blockers) if (c != null && !c.isTrigger && !c.transform.IsChildOf(root)) blocked++;
                foreach (var c in blockers) if (c != null && !c.isTrigger && c.transform.IsChildOf(root) && !c.name.StartsWith("Wall") && c.name != "Lintel" && c.name != "Door" && c.name != "Upper" && c.name != "Ceiling") { /* wyposażenie wnętrza w progu */ blocked++; }

                string status = okOut && okIn && reach && blocked == 0 ? "OK" : "PROBLEM";
                if (status != "OK") problems++;
                sb.AppendLine($"  [{status}] {z.shopName} ({z.type}): navmesh przed wejściem={okOut}, w strefie={okIn}, droga do strefy={reach}, blokady w progu={blocked}");
            }

            foreach (var m in Object.FindObjectsByType<Mission>(FindObjectsSortMode.None))
            {
                bool ok = NavMesh.SamplePosition(m.transform.position, out _, 4f, NavMesh.AllAreas);
                bool blockedStart = false;
                foreach (var c in Physics.OverlapSphere(m.transform.position + Vector3.up * 1f, 0.6f)) if (c != null && !c.isTrigger) blockedStart = true;
                if (!ok || blockedStart) { problems++; sb.AppendLine($"  [PROBLEM] misja '{m.missionName}': start na navmesh={ok}, zablokowany={blockedStart}"); }
                foreach (var s in m.steps)
                {
                    if (s.target == null || s.type == MissionStepType.DriveTo || s.type == MissionStepType.EnterVehicle) continue; // jazda: cel może leżeć na obwodnicy poza NavMeshem
                    if (!NavMesh.SamplePosition(s.target.position, out _, 6f, NavMesh.AllAreas))
                    { problems++; sb.AppendLine($"  [PROBLEM] misja '{m.missionName}': cel '{s.objective}' poza navmesh"); }
                }
            }

            sb.AppendLine(problems == 0 ? "WYNIK: wszystko działa (budynki dostępne, misje osiągalne)." : $"WYNIK: {problems} problemów – patrz wyżej.");
            string report = sb.ToString();
            System.IO.File.WriteAllText("Assets/GTA/Generated/BuildReport.txt", report);
            if (problems == 0) Debug.Log("[MiniGTA][BUILD] " + report); else Debug.LogWarning("[MiniGTA][BUILD] " + report);
        }
    }
}
