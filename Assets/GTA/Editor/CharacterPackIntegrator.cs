using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// MiniGTA → 6. Podłącz postacie z paczki (ubrania + kolory)
    /// Bierze modele postaci z paczki „Creative Characters” (albo z zaznaczonych w oknie Project prefabów)
    /// i wstawia je jako STROJE do gracza, przechodniów, policji, SWAT-u, wojska i gangsterów:
    ///   • każdy NPC przy spawnie dostaje losowy strój z paczki (CharacterAppearance włącza jeden z modeli),
    ///   • kolory skóry/włosów/ubrań są losowane przez MaterialPropertyBlock (paczka nie jest modyfikowana),
    ///   • gracz wybiera strój i kolory w przebieralni (F2),
    ///   • tworzy Animator Controller (Speed / Dead / Shoot) z klipów znalezionych w paczce.
    /// Można uruchamiać wielokrotnie.
    /// </summary>
    public static class CharacterPackIntegrator
    {
        const string GeneratedPrefabs = "Assets/GTA/Generated/Prefabs";
        const string ControllerPath = "Assets/GTA/Generated/GTA_Humanoid.controller";
        const float TargetHeight = 1.8f;
        const int MaxModels = 16;

        static readonly string[] TargetPrefixes = { "Player", "Pedestrian_", "PoliceOfficer", "PoliceSwat", "ArmySoldier", "Gangster" };

        [MenuItem("MiniGTA/6. Podłącz postacie z paczki (ubrania + kolory)", priority = 45)]
        public static void Run()
        {
            var report = new StringBuilder("Postacie z paczki:\n");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PartsRoot + "/Body/Body_010.prefab") != null)
            {
                RunModular(report);
                return;
            }
            var sources = FindSources(report);
            if (sources.Count == 0)
            {
                BuildDialog.Show("MiniGTA",
                    "Nie znaleziono modeli postaci.\n\nZaimportuj paczkę „Creative Characters” (Package Manager → My Assets → Import) " +
                    "albo zaznacz w oknie Project prefaby postaci i uruchom to polecenie ponownie.", "OK");
                Debug.LogWarning(report.ToString());
                return;
            }

            var controller = BuildController(report);
            int done = 0;
            foreach (var path in Directory.GetFiles(GeneratedPrefabs, "*.prefab"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!TargetPrefixes.Any(p => name.StartsWith(p))) continue;
                if (Dress(path.Replace('\\', '/'), sources, controller, report)) done++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.AppendLine($"Gotowe: ubrano {done} prefabów w {sources.Count} strojów.");
            Debug.Log(report.ToString());
            BuildDialog.Show("MiniGTA", $"Podłączono {sources.Count} modeli postaci do {done} prefabów.\nSzczegóły w Console.", "OK");
        }

        // ---------- źródła ----------

        static List<GameObject> FindSources(StringBuilder report)
        {
            var result = new List<GameObject>();
            foreach (var o in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
                if (IsCharacter(o)) result.Add(o);
            if (result.Count > 0) { report.AppendLine($"  użyto {result.Count} zaznaczonych prefabów"); return result.Take(MaxModels).ToList(); }

            // gotowe, animowane postacie z paczek ithappy (Casino: Ready_Characters) mają pierwszeństwo
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ithappy" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.Contains("/Ready_Characters/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && IsCharacter(go)) result.Add(go);
            }
            if (result.Count > 0) { report.AppendLine($"  użyto {result.Count} gotowych postaci (Ready_Characters)"); return result.Take(MaxModels).ToList(); }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith("Assets/GTA/")) continue;
                string low = p.ToLowerInvariant();
                if (!(low.Contains("creative") || low.Contains("character") || low.Contains("humanoid"))) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && IsCharacter(go)) result.Add(go);
            }
            report.AppendLine($"  znaleziono {result.Count} prefabów postaci w paczce");
            return result.Take(MaxModels).ToList();
        }

        static bool IsCharacter(GameObject go) => go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        // ---------- animacje ----------

        static AnimationClip FindClip(params string[] keys)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip").OrderBy(g => AssetDatabase.GUIDToAssetPath(g).Contains("Creative_Characters") ? 0 : 1))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith("Assets/GTA/")) continue;
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    if (!(a is AnimationClip c) || c.name.StartsWith("__preview")) continue;
                    string n = c.name.ToLowerInvariant();
                    if (keys.Any(k => n.Contains(k))) return c;
                }
            }
            return null;
        }

        static AnimatorController BuildController(StringBuilder report)
        {
            var idle = FindClip("idle");
            var walk = FindClip("walk");
            var run = FindClip("run", "sprint");
            var death = FindClip("death", "die", "dead");
            var shoot = FindClip("shoot", "fire", "attack", "aim");
            report.AppendLine($"  animacje: idle={idle?.name ?? "-"} walk={walk?.name ?? "-"} run={run?.name ?? "-"} death={death?.name ?? "-"} shoot={shoot?.name ?? "-"}");

            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            c.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            var sm = c.layers[0].stateMachine;

            var move = c.CreateBlendTreeInController("Move", out BlendTree tree);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, 1.5f);
            if (run != null) tree.AddChild(run, 5.5f);
            sm.defaultState = sm.states.First(s => s.state.name == "Move").state;

            if (death != null)
            {
                var d = sm.AddState("Death");
                d.motion = death;
                var t = sm.AddAnyStateTransition(d);
                t.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                t.hasExitTime = false; t.duration = 0.1f; t.canTransitionToSelf = false;
                // po odrodzeniu (Dead = false) wracamy do chodzenia – bez tego postać zostawała w pozie śmierci
                var back = d.AddTransition(sm.defaultState);
                back.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                back.hasExitTime = false; back.duration = 0.1f;
            }
            if (shoot != null)
            {
                var s = sm.AddState("Shoot");
                s.motion = shoot;
                var t = sm.AddAnyStateTransition(s);
                t.AddCondition(AnimatorConditionMode.If, 0, "Shoot");
                t.hasExitTime = false; t.duration = 0.05f;
                var back = s.AddTransition(sm.defaultState);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;
            }
            return c;
        }

        // ---------- ubieranie prefabów ----------

        // ---------- tryb modułowy: Creative Characters (części na wspólnym szkielecie) ----------

        const string PartsRoot = "Assets/ithappy/Creative_Characters_FREE/Prefabs";
        const string LibraryPath = "Assets/Resources/CharacterParts.asset";

        static void RunModular(StringBuilder report)
        {
            var controller = BuildController(report);
            var lib = BuildLibrary(controller, report);
            if (lib == null)
            {
                BuildDialog.Show("MiniGTA", "Nie udało się zbudować listy części postaci. Szczegóły w Console.", "OK");
                Debug.LogWarning(report.ToString());
                return;
            }

            int done = 0;
            foreach (var path in Directory.GetFiles(GeneratedPrefabs, "*.prefab"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!TargetPrefixes.Any(pfx => name.StartsWith(pfx))) continue;
                if (DressModular(path.Replace('\\', '/'), report)) done++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            int parts = lib.categories.Sum(c => c.parts.Count);
            report.AppendLine($"Gotowe: {lib.categories.Count} kategorii, {parts} części, ubrano {done} prefabów.");
            Debug.Log(report.ToString());
            BuildDialog.Show("MiniGTA", $"Postacie modułowe: {lib.categories.Count} kategorii, {parts} części.\nUbrano {done} prefabów (gracz, przechodnie, policja, gangsterzy).\nSzczegóły w Console.", "OK");
        }

        static CharacterPartLibrary BuildLibrary(RuntimeAnimatorController controller, StringBuilder report)
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(PartsRoot + "/Body/Body_010.prefab");
            if (body == null) return null;

            Directory.CreateDirectory("Assets/Resources");
            if (File.Exists(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var lib = ScriptableObject.CreateInstance<CharacterPartLibrary>();
            lib.bodyPrefab = body;
            lib.controller = controller;

            // wzrost i avatar mierzone na tymczasowej instancji ciała
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(body);
            inst.transform.position = Vector3.zero;
            var rends = inst.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                if (b.size.y > 0.01f)
                {
                    lib.scale = TargetHeight / b.size.y;
                    lib.yOffset = -b.min.y * lib.scale;
                }
                report.AppendLine($"  ciało: wysokość {b.size.y:0.00} m → skala {lib.scale:0.00}");
            }
            var anim = inst.GetComponentInChildren<Animator>(true);
            if (anim != null) lib.avatar = anim.avatar;
            Object.DestroyImmediate(inst);
            if (lib.avatar == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Avatar", new[] { "Assets/ithappy/Creative_Characters_FREE" }))
                {
                    lib.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AssetDatabase.GUIDToAssetPath(guid));
                    if (lib.avatar != null) break;
                }
            }

            // id, etykieta, slot koloru, hiddenByFull, isFull, domyślny nr, szansa u NPC, foldery
            void Add(string id, string label, CharacterAppearance.Slot slot, bool hiddenByFull, bool isFull, int def, float chance, params string[] folders)
            {
                var cat = new CharacterPartLibrary.Category
                {
                    id = id, label = label, slot = slot, hiddenByFull = hiddenByFull, isFull = isFull, defaultIndex = def, npcChance = chance,
                };
                foreach (var folder in folders)
                {
                    string dir = PartsRoot + "/" + folder;
                    if (!Directory.Exists(dir)) continue;
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }).OrderBy(AssetDatabase.GUIDToAssetPath))
                    {
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                        if (go != null && !cat.parts.Contains(go)) cat.parts.Add(go);
                    }
                }
                report.AppendLine($"  {label}: {cat.parts.Count} części");
                if (cat.parts.Count > 0) lib.categories.Add(cat);
            }

            var S = CharacterAppearance.Slot.None;
            Add("face", "Twarz", CharacterAppearance.Slot.Skin, false, false, 0, 1f, "Faces");
            Add("hair", "Fryzura", CharacterAppearance.Slot.Hair, false, false, 0, 0.85f, "Hairstyle", "Hairstyle Single");
            Add("hat", "Czapka / kapelusz", CharacterAppearance.Slot.Hair, false, false, -1, 0.2f, "Hat", "Hat Single");
            Add("glasses", "Okulary", S, false, false, -1, 0.15f, "Glasses");
            Add("faceacc", "Dodatki twarzy", CharacterAppearance.Slot.Hair, false, false, -1, 0.1f, "Face Accessories");
            Add("gloves", "Rękawiczki", CharacterAppearance.Slot.Bottom, false, false, -1, 0.06f, "Gloves");
            Add("full", "Strój cały", CharacterAppearance.Slot.Top, false, true, -1, 0.2f, "Costumes", "Outfit", "Mascots");
            Add("outwear", "Kurtka / góra", CharacterAppearance.Slot.Top, true, false, 0, 0.85f, "Outwear");
            Add("pants", "Spodnie / szorty", CharacterAppearance.Slot.Bottom, true, false, 0, 1f, "Pants", "Shorts");
            Add("socks", "Skarpety", CharacterAppearance.Slot.Bottom, true, false, -1, 0.3f, "Socks");
            Add("shoes", "Buty", CharacterAppearance.Slot.Shoes, false, false, 0, 1f, "Shoes");

            AssetDatabase.CreateAsset(lib, LibraryPath);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        static bool DressModular(string path, StringBuilder report)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return false;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var n in new[] { "Body", "Face", "Hat", "ModularModel" })
                {
                    var t = root.transform.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    if (root.transform.GetChild(i).name.StartsWith("Outfit_Body_")) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                var model = new GameObject("ModularModel");
                model.transform.SetParent(root.transform, false);
                model.AddComponent<ModularCharacter>();

                // pola „animator” skryptów postaci wiąże w grze CharacterAppearance (model powstaje dopiero w trakcie gry)
                foreach (var mb in root.GetComponents<MonoBehaviour>())
                {
                    var f = mb != null ? mb.GetType().GetField("animator") : null;
                    if (f != null && f.FieldType == typeof(Animator)) f.SetValue(mb, null);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine($"  ✔ {Path.GetFileNameWithoutExtension(path)}: postać składana z części");
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static bool Dress(string path, List<GameObject> sources, RuntimeAnimatorController controller, StringBuilder report)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return false;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // usuń stare prymitywy i poprzednie stroje
                foreach (var n in new[] { "Body", "Face", "Hat" })
                {
                    var t = root.transform.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    if (root.transform.GetChild(i).name.StartsWith("Outfit_Body_")) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                // kolejność strojów: dla każdego prefabu inna, żeby gracz i NPC nie mieli tego samego numeru 0
                bool isPlayer = root.name.StartsWith("Player");
                for (int i = 0; i < sources.Count; i++)
                {
                    var src = sources[i];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    go.name = "Outfit_Body_" + i;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;

                    // KOLEJNOŚĆ MA ZNACZENIE: skrypty paczki mają [RequireComponent(CharacterController)] – dopóki istnieją, Unity nie pozwoli
                    // usunąć CharacterControllera, a ten zostawał na każdej postaci i auta zderzały się z „niewidzialnymi” kapsułami.
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
                    foreach (var cc in go.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(cc);
                    foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);

                    Fit(go);
                    var anim = go.GetComponentInChildren<Animator>(true);
                    if (anim == null) anim = go.AddComponent<Animator>();
                    anim.runtimeAnimatorController = controller;
                    anim.applyRootMotion = false;
                    go.SetActive(i == 0);
                }

                // domyślny animator = pierwszy strój (CharacterAppearance podmienia go po wylosowaniu)
                var first = root.transform.Find("Outfit_Body_0")?.GetComponentInChildren<Animator>(true);
                foreach (var mb in root.GetComponents<MonoBehaviour>())
                {
                    var f = mb != null ? mb.GetType().GetField("animator") : null;
                    if (f != null && f.FieldType == typeof(Animator)) f.SetValue(mb, first);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine($"  ✔ {Path.GetFileNameWithoutExtension(path)}: {sources.Count} strojów{(isPlayer ? " (wybór w przebieralni F2)" : " (losowo)")}");
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Skaluje model do wzrostu 1,8 m i stawia stopami na y = 0.</summary>
        static void Fit(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y < 0.01f) return;
            float scale = TargetHeight / b.size.y;
            go.transform.localScale = Vector3.one * scale;
            // po skalowaniu przelicz granice i oprzyj o ziemię
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            go.transform.localPosition = new Vector3(0f, -b.min.y, 0f);
        }
    }
}
