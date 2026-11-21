using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Mała scena 3D dla katscenek: bohater i rozmówca (modele i animacje Kevin Iglesias) stoją w oświetlonym studio
    /// daleko pod mapą, a osobna kamera z wyższym depth filmuje je powoli najeżdżając. Mówiący aktor gra animację rozmowy.
    /// Wywoływane przez DialogueUI przy liniach katscen; bez biblioteki KIAnims zostaje czarny ekran.
    /// </summary>
    public class CutsceneStage : MonoBehaviour
    {
        static CutsceneStage _inst;
        public static bool Active => _inst != null && _inst.gameObject.activeSelf;

        Camera _cam;
        KIActor _hero, _other;
        KIAnimLibrary _lib;
        string _speaker;
        float _since;
        bool _closeUp;

        public static void Show(string speaker)
        {
            var lib = KIAnimLibrary.Get();
            if (lib == null || !lib.Ready) return;
            if (_inst == null)
            {
                var go = new GameObject("[CutsceneStage]");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<CutsceneStage>();
                _inst.Build(lib);
            }
            _inst.gameObject.SetActive(true);
            _inst.Setup(speaker);
        }

        /// <summary>Usuwa scenę (nowa gra = nowe ziarno wyglądu postaci).</summary>
        public static void Reset()
        {
            if (_inst != null) { Destroy(_inst.gameObject); _inst = null; }
        }

        public static void Hide()
        {
            if (_inst != null) _inst.gameObject.SetActive(false);
        }

        static bool IsFemale(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("ola") || n.Contains("mara") || n.Contains("siostr") || n.Contains("mama") || n.Contains("kobiet") || n.Contains("pani");
        }

        void Build(KIAnimLibrary lib)
        {
            _lib = lib;
            transform.position = new Vector3(0f, -900f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(14f, 0.05f, 14f);
            Destroy(floor.GetComponent<Collider>());
            var fm = new Material(Shader.Find("Standard")) { color = new Color(0.16f, 0.1f, 0.07f) };
            fm.SetFloat("_Glossiness", 0.15f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;

            // tło: duża ciepła ściana za aktorami
            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = new Vector3(0f, 3f, 5f);
            wall.transform.localScale = new Vector3(24f, 10f, 1f);
            Destroy(wall.GetComponent<Collider>());
            var wm = new Material(Shader.Find("Standard")) { color = new Color(0.32f, 0.17f, 0.08f) };
            wm.EnableKeyword("_EMISSION"); wm.SetColor("_EmissionColor", new Color(0.10f, 0.045f, 0.015f));
            wall.GetComponent<Renderer>().sharedMaterial = wm;

            MakeLight(new Vector3(-2.5f, 3.2f, -2.5f), new Color(1f, 0.82f, 0.6f), 2.6f, 14f);
            MakeLight(new Vector3(3f, 2.2f, 1.5f), new Color(1f, 0.45f, 0.2f), 2.2f, 12f);
            MakeLight(new Vector3(0f, 1.5f, -4f), new Color(0.8f, 0.85f, 1f), 1.2f, 12f);

            var camGo = new GameObject("StageCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.03f, 0.02f);
            _cam.depth = 100f;
            _cam.fieldOfView = 34f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.allowHDR = false;
            var al = camGo.GetComponent<AudioListener>(); if (al != null) Destroy(al);
        }

        void MakeLight(Vector3 lp, Color c, float intensity, float range)
        {
            var l = new GameObject("StageLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = lp;
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
        }

        string _otherName;

        /// <summary>Postać ze składanych części (jak w grze). player = wygląd wybrany w przebieralni; inaczej stały wygląd z ziarna gry.</summary>
        KIActor MakeCharacter(string label, bool player, Vector3 pos, float yaw)
        {
            return ActorFactory.Make(label, player, transform, pos, yaw);
        }

        void Setup(string speaker)
        {
            _speaker = speaker;
            _since = Time.unscaledTime;
            bool narrator = string.IsNullOrEmpty(speaker) || speaker == "Narrator";
            // kwestie bohatera ("Ty" / imię gracza) mówi sam bohater – nie tworzymy dla nich drugiej osoby
            bool heroSpeaks = speaker == "Ty" || (!string.IsNullOrEmpty(speaker) && speaker == NetworkUI.LocalPlayerName);

            if (_hero == null) _hero = MakeCharacter("Hero", true, new Vector3(-0.9f, 0f, 0f), 145f);

            if (heroSpeaks)
            {
                SetActorActive(_other, _other != null);
                _closeUp = false;
                _hero.Play(_lib.maleTalk);
                if (_other != null) _other.Play(_lib.maleIdle);
                return;
            }

            // druga osoba: inna, losowa postać; zostaje ta sama, dopóki mówi ten sam rozmówca
            if (!narrator && (_other == null || _otherName != speaker))
            {
                if (_other != null) Destroy(RootOf(_other));
                _otherFemale = false; // manekin HumanF nie ma ubrań – wszyscy są składani z części
                _other = MakeCharacter(speaker, false, new Vector3(0.9f, 0f, 0f), 215f);
                _otherName = speaker;
            }

            SetActorActive(_other, !narrator);
            _closeUp = narrator;

            // kto mówi, ten gestykuluje; drugi stoi w pozie spoczynkowej
            if (narrator) _hero.Play(_lib.maleIdle2 != null ? _lib.maleIdle2 : _lib.maleIdle);
            else
            {
                _hero.Play(_lib.maleIdle);
                _other.Play(_otherFemale && _lib.femaleTalk != null ? _lib.femaleTalk : _lib.maleTalk);
            }
        }

        bool _otherFemale;
        GUIStyle _tagStyle;

        void DrawTag(KIActor a, string text, Color col)
        {
            if (a == null || !RootOf(a).activeSelf || string.IsNullOrEmpty(text)) return;
            Vector3 sp = _cam.WorldToScreenPoint(RootOf(a).transform.position + Vector3.up * 2.05f);
            if (sp.z <= 0f) return;
            var r = new Rect(sp.x - 120f, Screen.height - sp.y - 14f, 240f, 28f);
            _tagStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, _tagStyle);
            _tagStyle.normal.textColor = col;
            GUI.Label(r, text, _tagStyle);
        }

        void OnGUI()
        {
            if (_cam == null || Event.current.type != EventType.Repaint) return;
            if (_tagStyle == null)
                _tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(16, Screen.height / 40), fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            DrawTag(_hero, NetworkUI.LocalPlayerName, new Color(1f, 0.85f, 0.3f));
            DrawTag(_other, _otherName, Color.white);
        }

        // korzeń postaci = dziecko sceny (Hero / Other_*), a KIActor siedzi na obiekcie z Animatorem głębiej
        GameObject RootOf(KIActor a)
        {
            var t = a.transform;
            while (t.parent != null && t.parent != transform) t = t.parent;
            return t.gameObject;
        }

        void SetActorActive(KIActor a, bool on)
        {
            if (a != null) RootOf(a).SetActive(on);
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            float t = Time.unscaledTime - _since;
            // powolny najazd + lekki ruch na boki
            Vector3 center = _closeUp ? new Vector3(-0.9f, 1.25f, 0f) : new Vector3(0f, 1.2f, 0f);
            float dist = (_closeUp ? 3.8f : 4.6f) - Mathf.Min(t, 12f) * 0.06f;
            float side = Mathf.Sin(Time.unscaledTime * 0.35f) * 0.45f;
            _cam.transform.localPosition = center + new Vector3(side, 0.25f, -dist);
            _cam.transform.LookAt(transform.TransformPoint(center + new Vector3(0f, -0.05f, 0f)));
        }
    }
}
