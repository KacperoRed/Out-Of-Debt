using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Zapisany wygląd bohatera (kolory + numer stroju). Trzymany w PlayerPrefs.</summary>
    public static class AppearanceSettings
    {
        // białe = oryginalne kolory części z paczki; suwaki w przebieralni barwią je na wybrany kolor
        public static Color Skin = Color.white;
        public static Color Hair = Color.white;
        public static Color Top = Color.white;
        public static Color Bottom = Color.white;
        public static Color Shoes = Color.white;
        public static int Outfit = 0;

        public static event System.Action Changed;

        /// <summary>Ziarno gry: od niego i od imienia zależy wygląd postaci niezależnych. Stałe przez całą grę, zmienia się tylko przy nowej grze.</summary>
        public static int GameSeed
        {
            get
            {
                int s = PlayerPrefs.GetInt("ap_seed", 0);
                if (s == 0) { s = Random.Range(1, int.MaxValue); PlayerPrefs.SetInt("ap_seed", s); PlayerPrefs.Save(); }
                return s;
            }
        }

        public static void NewSeed() { PlayerPrefs.SetInt("ap_seed", Random.Range(1, int.MaxValue)); PlayerPrefs.Save(); }

        public static int SeedFor(string key)
        {
            unchecked
            {
                int h = GameSeed;
                foreach (char c in key ?? "") h = h * 31 + c;
                return h == 0 ? 1 : h;
            }
        }

        static AppearanceSettings() { Load(); }

        /// <summary>Wybrana część postaci w kategorii (id z CharacterPartLibrary). −1 = brak; bez zapisu = wartość domyślna z biblioteki.</summary>
        public static int GetPart(CharacterPartLibrary.Category cat)
        {
            return PlayerPrefs.GetInt("ap_part_" + cat.id, cat.defaultIndex);
        }

        public static void SetPart(CharacterPartLibrary.Category cat, int index)
        {
            PlayerPrefs.SetInt("ap_part_" + cat.id, index);
        }

        static Color Get(string key, Color def)
        {
            string s = PlayerPrefs.GetString("ap_" + key, "");
            return ColorUtility.TryParseHtmlString("#" + s, out var c) && s.Length > 0 ? c : def;
        }

        static void Put(string key, Color c) => PlayerPrefs.SetString("ap_" + key, ColorUtility.ToHtmlStringRGB(c));

        public static void Load()
        {
            if (PlayerPrefs.GetInt("ap_ver", 0) < 2)
            {
                // poprzednia wersja zapisywała kolory dla modeli-kapsuł – kasujemy, żeby nie barwiły nowych postaci
                foreach (var k in new[] { "skin", "hair", "top", "bottom", "shoes" }) PlayerPrefs.DeleteKey("ap_" + k);
                PlayerPrefs.SetInt("ap_ver", 2);
            }
            Skin = Get("skin", Skin); Hair = Get("hair", Hair); Top = Get("top", Top);
            Bottom = Get("bottom", Bottom); Shoes = Get("shoes", Shoes);
            Outfit = PlayerPrefs.GetInt("ap_outfit", 0);
        }

        public static void Save()
        {
            Put("skin", Skin); Put("hair", Hair); Put("top", Top); Put("bottom", Bottom); Put("shoes", Shoes);
            PlayerPrefs.SetInt("ap_outfit", Outfit);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Ubiera i koloruje postać. Działa na dowolnym modelu:
    ///  • KOLORY: każdy materiał/renderer jest przypisywany do slotu (skóra, włosy, góra, dół, buty) po nazwie
    ///    (np. "skin", "hair", "shirt", "pants", "shoe"). Kolor nakładany jest przez MaterialPropertyBlock,
    ///    więc nie powstają kopie materiałów i paczka zostaje nietknięta.
    ///  • UBRANIA: dzieci o nazwach "Outfit_&lt;grupa&gt;_&lt;nr&gt;" (np. Outfit_Top_0, Outfit_Top_1, Hat_2) tworzą grupy.
    ///    W każdej grupie włączany jest jeden element (losowy dla NPC, wybrany w menu dla gracza).
    /// Gracz: wygląd z AppearanceSettings (menu postaci). NPC: losowy, w odcieniach "ludzkich".
    /// </summary>
    public class CharacterAppearance : MonoBehaviour
    {
        public enum Slot { None, Skin, Hair, Top, Bottom, Shoes }

        public bool isPlayer;
        public bool randomize = true;
        [Tooltip("Mundur: ogranicza losowe kolory (np. policja = granat)")]
        public bool useUniform;
        public Color uniformTop = new Color(0.1f, 0.15f, 0.4f);
        public Color uniformBottom = new Color(0.08f, 0.1f, 0.25f);

        static readonly Color[] SkinTones =
        {
            new Color(1f, 0.87f, 0.75f), new Color(0.95f, 0.8f, 0.65f), new Color(0.85f, 0.65f, 0.47f),
            new Color(0.65f, 0.45f, 0.3f), new Color(0.45f, 0.3f, 0.2f), new Color(0.32f, 0.2f, 0.14f),
        };
        static readonly Color[] HairTones =
        {
            new Color(0.08f, 0.06f, 0.05f), new Color(0.25f, 0.15f, 0.08f), new Color(0.5f, 0.35f, 0.15f),
            new Color(0.85f, 0.7f, 0.35f), new Color(0.6f, 0.6f, 0.6f), new Color(0.6f, 0.2f, 0.1f),
        };

        struct Entry { public Renderer r; public int matIndex; public Slot slot; public bool soft; }
        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, List<GameObject>> _groups = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<Slot, Color> _colors = new Dictionary<Slot, Color>();
        readonly Dictionary<string, int> _picked = new Dictionary<string, int>();
        bool _built;
        ModularCharacter _modular;
        int[] _npcSelection;

        /// <summary>Model składany z części (Creative Characters), jeśli prefab go ma.</summary>
        ModularCharacter Modular
        {
            get
            {
                if (_modular == null) _modular = GetComponentInChildren<ModularCharacter>(true);
                return _modular;
            }
        }

        // ---------- upadek po śmierci ----------
        bool _lying;
        float _lieT;

        /// <summary>Czy postać ma model z paczki (stroje Outfit_Body_N), a nie zastępczą kapsułę.</summary>
        public bool HasModel
        {
            get
            {
                if (Modular != null) return true;
                if (!_built) Scan();
                return _groups.ContainsKey("Outfit_Body");
            }
        }

        /// <summary>Kładzie model na plecach (śmierć) albo podnosi (odrodzenie). Obraca tylko stroje, nie korzeń postaci.</summary>
        public void SetLying(bool lying) { _lying = lying; }

        Health _health;
        bool _wasDead;

        void Update()
        {
            // stan wynika wprost ze zdrowia: martwy = leży, żywy (po odrodzeniu) = stoi
            if (_health == null) _health = GetComponent<Health>();
            if (_health != null && _health.IsSpawned)
            {
                bool dead = _health.Dead;
                _lying = dead;
                // odrodzenie: Animator potrafi zostać w stanie śmierci (brak przejścia powrotnego) – resetujemy go
                if (_wasDead && !dead)
                {
                    var an = Modular != null ? Modular.Animator : null;
                    if (an != null)
                    {
                        an.SetBool("Dead", false);
                        an.Rebind();
                        an.Update(0f);
                    }
                    _lieT = 1f; // niech wstanie płynnie z pozycji leżącej
                }
                _wasDead = dead;
            }
            float target = _lying ? 1f : 0f;
            if (Mathf.Approximately(_lieT, target)) return;
            _lieT = Mathf.MoveTowards(_lieT, target, Time.deltaTime * 2.5f);
            float eased = Mathf.SmoothStep(0f, 1f, _lieT);
            if (Modular != null) { Modular.transform.localRotation = Quaternion.Euler(90f * eased, 0f, 0f); return; }
            if (!_groups.TryGetValue("Outfit_Body", out var outfits)) return;
            foreach (var o in outfits)
                if (o != null) o.transform.localRotation = Quaternion.Euler(90f * eased, 0f, 0f);
        }

        public static CharacterAppearance Ensure(GameObject go, bool player = false, bool uniform = false)
        {
            var a = go.GetComponent<CharacterAppearance>();
            if (a == null) a = go.AddComponent<CharacterAppearance>();
            a.isPlayer = player;
            if (uniform) a.useUniform = true;
            return a;
        }

        void Start()
        {
            Rebuild();
            if (isPlayer) AppearanceSettings.Changed += Rebuild;
        }

        void OnDestroy() { if (isPlayer) AppearanceSettings.Changed -= Rebuild; }

        // ---------- rozpoznawanie slotów ----------

        static Slot SlotFor(string n)
        {
            n = n.ToLowerInvariant();
            // paczki ithappy: Part_01_Adult_Male_Body_02 = ciało (skóra), ..._Face_..., ..._Hairstyle_...
            if (n.Contains("_body") || n.Contains("face_") || n.Contains("_face") || n.Contains("_hands") || n.Contains("_arms")) return Slot.Skin;
            if (n.Contains("hairstyle") || n.Contains("_hat") || n.Contains("beard") || n.Contains("mustache")) return Slot.Hair;
            if (n.Contains("glasses") || n.Contains("headphones")) return Slot.None;
            if (n.Contains("skin") || n.Contains("face") || n.Contains("head") || n.Contains("body_skin") || n.Contains("hand") || n.Contains("arm_skin") || n.Contains("skóra")) return Slot.Skin;
            if (n.Contains("hair") || n.Contains("beard") || n.Contains("hat") || n.Contains("cap") || n.Contains("włos")) return Slot.Hair;
            if (n.Contains("shoe") || n.Contains("boot") || n.Contains("sneaker") || n.Contains("feet") || n.Contains("buty")) return Slot.Shoes;
            if (n.Contains("pant") || n.Contains("trouser") || n.Contains("jean") || n.Contains("short") || n.Contains("skirt") || n.Contains("bottom") || n.Contains("leg") || n.Contains("spodn")) return Slot.Bottom;
            if (n.Contains("shirt") || n.Contains("top") || n.Contains("jacket") || n.Contains("cloth") || n.Contains("coat") || n.Contains("hoodie") || n.Contains("dress") || n.Contains("body") || n.Contains("torso") || n.Contains("suit") || n.Contains("uniform")) return Slot.Top;
            return Slot.None;
        }

        /// <summary>Usuwa z modeli strojów wszystko, co fizyczne (stare prefaby mogły zawierać CharacterController i skrypty paczki).</summary>
        void StripPhysicsFromOutfits()
        {
            foreach (var kv in _groups)
                foreach (var outfit in kv.Value)
                {
                    if (outfit == null) continue;
                    foreach (var mb in outfit.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && !(mb is CharacterAppearance)) Destroy(mb);
                    foreach (var cc in outfit.GetComponentsInChildren<CharacterController>(true)) Destroy(cc);
                    foreach (var col in outfit.GetComponentsInChildren<Collider>(true)) Destroy(col);
                    foreach (var rb in outfit.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
                }
        }

        void Scan()
        {
            _entries.Clear();
            _groups.Clear();
            if (Modular != null && Modular.Ready)
            {
                // składana postać: slot koloru zna już każda część (twarz = skóra, fryzura = włosy, spodnie = dół…)
                foreach (var me in Modular.Entries)
                {
                    if (me.renderer == null || me.slot == Slot.None) continue;
                    for (int i = 0; i < me.renderer.sharedMaterials.Length; i++)
                        _entries.Add(new Entry { r = me.renderer, matIndex = i, slot = me.slot });
                }
                _built = true;
                return;
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string name = t.name;
                // grupy ubrań: Outfit_Top_3 / Hat_1 / Hair_2 / Glasses_0
                int us = name.LastIndexOf('_');
                if (us > 0 && int.TryParse(name.Substring(us + 1), out _) &&
                    (name.StartsWith("Outfit_") || name.StartsWith("Hat_") || name.StartsWith("Hair_") || name.StartsWith("Top_") || name.StartsWith("Bottom_") || name.StartsWith("Shoes_") || name.StartsWith("Glasses_")))
                {
                    string g = name.Substring(0, us);
                    if (!_groups.TryGetValue(g, out var list)) _groups[g] = list = new List<GameObject>();
                    list.Add(t.gameObject);
                }
            }
            StripPhysicsFromOutfits();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.sharedMaterials;
                Slot byObject = SlotFor(r.gameObject.name);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Slot s = SlotFor(mats[i].name);
                    if (s == Slot.None) s = byObject;
                    if (s != Slot.None) _entries.Add(new Entry { r = r, matIndex = i, slot = s });
                }
            }
            // model bez rozpoznawalnych nazw materiałów (np. jedna tekstura-paleta): delikatnie barwimy całość kolorem „góry”
            if (_entries.Count == 0)
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                        _entries.Add(new Entry { r = r, matIndex = i, slot = Slot.Top, soft = true });
                }
            _built = true;
        }

        /// <summary>Podpina Animator aktywnego stroju do pól „animator” skryptów postaci (gracz, przechodzień, policjant).</summary>
        void BindAnimator()
        {
            Animator anim = null;
            if (Modular != null && Modular.Animator != null) anim = Modular.Animator;
            else foreach (var a in GetComponentsInChildren<Animator>(false)) { anim = a; break; }
            if (anim == null) return;
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var f = mb.GetType().GetField("animator");
                if (f != null && f.FieldType == typeof(Animator)) f.SetValue(mb, anim);
            }

            // gniazdo na broń w prawej dłoni + poza „trzymam broń”
            var pose = GetComponent<ArmedPose>();
            if (pose == null) pose = gameObject.AddComponent<ArmedPose>();
            pose.Setup(anim.transform);
            if (pose.socket != null)
            {
                var wv = GetComponent<WeaponVisual>();
                if (wv != null) wv.hand = pose.socket;
                var npcGun = transform.Find("WeaponModel");
                if (npcGun == null) npcGun = pose.socket.Find("WeaponModel");
                if (npcGun != null) { npcGun.SetParent(pose.socket, false); npcGun.localPosition = Vector3.zero; npcGun.localRotation = Quaternion.identity; }
            }
        }

        // ---------- losowanie / zastosowanie ----------

        void PickColors()
        {
            _colors.Clear();
            if (isPlayer)
            {
                _colors[Slot.Skin] = AppearanceSettings.Skin;
                _colors[Slot.Hair] = AppearanceSettings.Hair;
                _colors[Slot.Top] = AppearanceSettings.Top;
                _colors[Slot.Bottom] = AppearanceSettings.Bottom;
                _colors[Slot.Shoes] = AppearanceSettings.Shoes;
                return;
            }
            _colors[Slot.Skin] = SkinTones[Random.Range(0, SkinTones.Length)];
            _colors[Slot.Hair] = HairTones[Random.Range(0, HairTones.Length)];
            if (useUniform)
            {
                var npc = GetComponent<CombatNpcAI>();
                var tier = npc != null ? npc.tier : PoliceTier.Police;
                if (tier == PoliceTier.Swat) { _colors[Slot.Top] = new Color(0.1f, 0.11f, 0.13f); _colors[Slot.Bottom] = new Color(0.08f, 0.09f, 0.1f); _colors[Slot.Hair] = new Color(0.1f, 0.11f, 0.13f); }
                else if (tier == PoliceTier.Army) { _colors[Slot.Top] = new Color(0.32f, 0.36f, 0.2f); _colors[Slot.Bottom] = new Color(0.28f, 0.32f, 0.18f); _colors[Slot.Hair] = new Color(0.3f, 0.34f, 0.18f); }
                else { _colors[Slot.Top] = new Color(0.12f, 0.28f, 0.62f); _colors[Slot.Bottom] = new Color(0.07f, 0.1f, 0.2f); _colors[Slot.Hair] = new Color(0.08f, 0.14f, 0.34f); }
                _colors[Slot.Shoes] = new Color(0.05f, 0.05f, 0.05f);
            }
            else
            {
                _colors[Slot.Top] = Color.HSVToRGB(Random.value, Random.Range(0.3f, 0.85f), Random.Range(0.45f, 0.95f));
                _colors[Slot.Bottom] = Color.HSVToRGB(Random.value, Random.Range(0.1f, 0.6f), Random.Range(0.2f, 0.6f));
                _colors[Slot.Shoes] = Color.HSVToRGB(Random.value, Random.Range(0f, 0.5f), Random.Range(0.1f, 0.5f));
            }
        }

        int[] PlayerSelection(CharacterPartLibrary lib)
        {
            var sel = new int[lib.categories.Count];
            for (int i = 0; i < sel.Length; i++)
            {
                var c = lib.categories[i];
                sel[i] = Mathf.Clamp(AppearanceSettings.GetPart(c), -1, c.parts.Count - 1);
            }
            return sel;
        }

        int[] NpcSelection(CharacterPartLibrary lib)
        {
            if (_npcSelection != null && _npcSelection.Length == lib.categories.Count) return _npcSelection;
            var sel = new int[lib.categories.Count];
            bool full = false;
            for (int i = 0; i < sel.Length; i++)
            {
                var c = lib.categories[i];
                bool take = c.parts.Count > 0 && Random.value < c.npcChance;
                if (c.id == "face" || c.id == "pants" || c.id == "shoes") take = c.parts.Count > 0; // zawsze twarz, spodnie i buty
                if (noCostumes && (c.isFull || c.id == "faceacc")) take = false;
                if (useUniform)
                {
                    // mundur: czapka + kurtka zawsze, żadnych strojów całościowych; SWAT/wojsko dodatkowo okulary i rękawice
                    var tier = GetComponent<CombatNpcAI>() != null ? GetComponent<CombatNpcAI>().tier : PoliceTier.Police;
                    if (c.isFull) take = false;
                    else if (c.id == "hat" || c.id == "outwear") take = c.parts.Count > 0;
                    else if (c.id == "glasses" || c.id == "gloves") take = c.parts.Count > 0 && tier != PoliceTier.Police;
                    else if (c.id == "faceacc") take = false;
                }
                sel[i] = take ? Random.Range(0, c.parts.Count) : -1;
                if (c.isFull && sel[i] >= 0) full = true;
            }
            _npcSelection = sel;
            return sel;
        }

        /// <summary>0 = losowo; inaczej wygląd NPC jest deterministyczny (ten sam dla tego samego ziarna).</summary>
        public int seed;
        [Tooltip("Bez strojów całościowych (kostiumy) – postacie fabularne")]
        public bool noCostumes;

        public void Rebuild()
        {
            if (seed != 0 && !isPlayer)
            {
                var st = Random.state;
                Random.InitState(seed);
                RebuildInner();
                Random.state = st;
            }
            else RebuildInner();
        }

        void RebuildInner()
        {
            var lib = CharacterPartLibrary.Instance;
            if (Modular != null && lib != null)
            {
                Modular.Build(lib, isPlayer ? PlayerSelection(lib) : NpcSelection(lib));
                _built = false; // renderery się zmieniły – przeskanuj sloty kolorów
            }
            if (!_built) Scan();
            PickColors();

            // ubrania: jeden element na grupę
            foreach (var kv in _groups)
            {
                int idx;
                if (isPlayer) idx = AppearanceSettings.Outfit;
                else if (!_picked.TryGetValue(kv.Key, out idx)) idx = _picked[kv.Key] = Random.Range(0, kv.Value.Count);
                idx %= kv.Value.Count;
                for (int i = 0; i < kv.Value.Count; i++) kv.Value[i].SetActive(i == idx);
            }

            BindAnimator();

            var mpb = new MaterialPropertyBlock();
            foreach (var e in _entries)
            {
                if (e.r == null || !_colors.TryGetValue(e.slot, out var c)) continue;
                if (e.soft) c = Color.Lerp(Color.white, c, 0.6f);
                e.r.GetPropertyBlock(mpb, e.matIndex);
                mpb.SetColor("_BaseColor", c); // URP Lit
                mpb.SetColor("_Color", c);     // Built-in / Standard
                e.r.SetPropertyBlock(mpb, e.matIndex);
            }
        }

        /// <summary>Liczba wariantów ubrań (największa grupa) – do suwaka w menu.</summary>
        public static int OutfitCount(GameObject prefabOrInstance)
        {
            int max = 1;
            var counts = new Dictionary<string, int>();
            foreach (var t in prefabOrInstance.GetComponentsInChildren<Transform>(true))
            {
                int us = t.name.LastIndexOf('_');
                if (us <= 0 || !int.TryParse(t.name.Substring(us + 1), out _)) continue;
                string g = t.name.Substring(0, us);
                counts[g] = counts.TryGetValue(g, out var c) ? c + 1 : 1;
                max = Mathf.Max(max, counts[g]);
            }
            return max;
        }
    }
}
