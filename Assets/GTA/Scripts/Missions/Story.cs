using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiniGTA
{
    [Serializable]
    public class DialogueLine
    {
        public string speaker;
        [TextArea] public string text;
        [NonSerialized] public bool cine;   // linia katsceny (pełny czarny ekran)
        public DialogueLine() { }
        public DialogueLine(string speaker, string text) { this.speaker = speaker; this.text = text; }
    }

    /// <summary>Flagi fabuły ("a1_1" = ukończona misja 1 aktu I). Zapisywane na dysku osobno dla każdego nicka.</summary>
    public static class Story
    {
        public const int StartDebt = 20000;
        [Serializable] class Data { public List<string> flags = new List<string>(); public int debt = StartDebt; }

        /// <summary>Ile jeszcze zostało do spłaty gangowi Rica.</summary>
        public static int Debt { get; private set; } = StartDebt;

        /// <summary>Odsetki: co minutę gry dług rośnie o tyle procent (do dwukrotności początkowego długu).</summary>
        public const float InterestPerMinute = 0.01f;
        static float _interestTimer;

        /// <summary>Wywołuj co klatkę z czasem gry. Zwraca kwotę naliczonych odsetek (0, gdy jeszcze nie ten moment).</summary>
        public static int TickInterest(float dt)
        {
            if (Debt <= 0) { _interestTimer = 0f; return 0; }
            _interestTimer += dt;
            if (_interestTimer < 60f) return 0;
            _interestTimer -= 60f;
            int add = Mathf.Max(10, Mathf.RoundToInt(Debt * InterestPerMinute / 10f) * 10);
            add = Mathf.Min(add, StartDebt * 2 - Debt);
            if (add <= 0) return 0;
            Debt += add;
            Save();
            return add;
        }

        static readonly HashSet<string> Flags = new HashSet<string>();
        static string _nick;

        static string PathFor(string nick)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) nick = nick.Replace(c, '_');
            return Path.Combine(Application.persistentDataPath, "saves", "story_" + nick.ToLowerInvariant() + ".json");
        }

        public static void Load(string nick)
        {
            _nick = nick;
            Flags.Clear();
            Debt = StartDebt;
            try
            {
                string p = PathFor(nick);
                if (!File.Exists(p)) return;
                var d = JsonUtility.FromJson<Data>(File.ReadAllText(p));
                if (d != null) { foreach (var f in d.flags) Flags.Add(f); Debt = Mathf.Max(0, d.debt); }
            }
            catch (Exception e) { Debug.LogWarning("[MiniGTA] Story.Load: " + e.Message); }
        }

        public static void Reset(string nick)
        {
            Flags.Clear();
            Debt = StartDebt;
            _nick = nick;
            try { string p = PathFor(nick); if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
        }

        public static bool Has(string flag) => string.IsNullOrEmpty(flag) || Flags.Contains(flag);
        public static bool HasFlagStrict(string flag) => Flags.Contains(flag);

        public static void Set(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !Flags.Add(flag)) return;
            Save();
        }

        /// <summary>Dopóki nie wykonasz wszystkich zadań fabularnych, dług nie spadnie poniżej tej kwoty.</summary>
        public const int DebtFloor = 1000;
        public static bool AllTasksDone => Level >= MaxLevel;
        /// <summary>Najniższy możliwy teraz dług (0 dopiero po wszystkich zadaniach).</summary>
        public static int CurrentFloor => AllTasksDone ? 0 : DebtFloor;
        /// <summary>Ile można jeszcze spłacić w tej chwili.</summary>
        public static int Payable => Mathf.Max(0, Debt - CurrentFloor);

        public static void PayDebt(int amount)
        {
            if (amount <= 0) return;
            Debt = Mathf.Max(CurrentFloor, Debt - amount);
            Save();
        }

        public static void ClearDebt() { Debt = 0; Save(); }

        static void Save()
        {
            if (string.IsNullOrEmpty(_nick)) return;
            try
            {
                string p = PathFor(_nick);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonUtility.ToJson(new Data { flags = new List<string>(Flags), debt = Debt }));
            }
            catch (Exception e) { Debug.LogWarning("[MiniGTA] Story.Save: " + e.Message); }
        }

        static readonly string[] LevelFlags = { "a1_1", "a1_2", "a1_3", "a2_1", "a2_2", "a2_3", "a2_4", "a3_1", "a3_2", "a3_3", "a3_4" };
        static readonly int[] LevelDebt = { 2000, 2500, 3000, 1500, 3000, 3000, 3000, 0, 0, 0, 0 };
        /// <summary>Ile poziomów (ukończonych misji fabularnych) ma kampania.</summary>
        public static int MaxLevel => LevelFlags.Length;

        /// <summary>Ile misji fabularnych ukończono po kolei (0 = nowa gra, 11 = koniec).</summary>
        public static int Level
        {
            get
            {
                int n = 0;
                while (n < LevelFlags.Length && Flags.Contains(LevelFlags[n])) n++;
                return n;
            }
        }

        /// <summary>Ustawia postęp fabuły: ukończone są misje 1..level (konsola: „poziom N”). Dług spada tak, jakby je spłacono.</summary>
        public static void SetLevel(int level)
        {
            level = Mathf.Clamp(level, 0, LevelFlags.Length);
            Flags.Clear();
            int paid = 0;
            for (int i = 0; i < level; i++) { Flags.Add(LevelFlags[i]); paid += LevelDebt[i]; }
            Debt = level >= LevelFlags.Length ? 0 : Mathf.Max(0, StartDebt - paid);
            Save();
        }

        public static int ActReached
        {
            get
            {
                if (Flags.Contains("a2_4")) return 3;
                if (Flags.Contains("a1_3")) return 2;
                return 1;
            }
        }

        public static bool GameFinished => Flags.Contains("a3_4");
    }

    /// <summary>Okno dialogowe na dole ekranu. Enter / klik = dalej, inaczej samo przechodzi po kilku sekundach.</summary>
    public class DialogueUI : MonoBehaviour
    {
        static DialogueUI _inst;
        readonly Queue<DialogueLine> _queue = new Queue<DialogueLine>();
        DialogueLine _current;
        float _until;
        GUIStyle _name, _text, _hint;

        float _start;

        public static bool Active => _inst != null && _inst._current != null;
        /// <summary>Kto aktualnie mówi (do animacji postaci w świecie).</summary>
        public static string CurrentSpeaker => _inst != null && _inst._current != null ? _inst._current.speaker : null;
        /// <summary>Trwa katscena na czarnym ekranie – gra stoi.</summary>
        public static bool Cinematic => _inst != null && _inst._current != null && _inst._current.cine;

        public static void Play(IEnumerable<DialogueLine> lines, bool cinematic = false)
        {
            if (lines == null) return;
            if (cinematic) foreach (var l in lines) if (l != null) l.cine = true;
            if (_inst == null)
            {
                var go = new GameObject("DialogueUI");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<DialogueUI>();
            }
            foreach (var l in lines) if (l != null && !string.IsNullOrEmpty(l.text)) _inst._queue.Enqueue(l);
            if (_inst._current == null) _inst.Next();
        }

        void Next()
        {
            _current = _queue.Count > 0 ? _queue.Dequeue() : null;
            _start = Time.unscaledTime;
            bool titleCard = _current != null && (string.IsNullOrEmpty(_current.speaker) || _current.speaker == "TYTUŁ");
            if (_current != null && _current.cine && !titleCard) CutsceneStage.Show(_current.speaker);
            else CutsceneStage.Hide();
            if (_current != null)
                _until = Time.unscaledTime + (_current.cine ? Mathf.Clamp(3.5f + _current.text.Length * 0.06f, 4.5f, 13f) : Mathf.Clamp(2.5f + _current.text.Length * 0.045f, 3f, 9f));
        }

        void Update()
        {
            if (_current == null) return;
            if (_current.cine && Input.GetKeyDown(KeyCode.Escape)) // Esc pomija całą katscenę
            {
                while (_current != null && _current.cine) Next();
                return;
            }
            if (Time.unscaledTime > _until || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Next();
        }

        void OnGUI()
        {
            if (_current == null) return;
            if (_name == null)
            {
                MenuSkin.Ensure();
                _name = new GUIStyle(MenuSkin.Label) { fontSize = 22, fontStyle = FontStyle.Bold };
                _name.normal.textColor = new Color(0.4f, 0.17f, 0.02f);
                _text = new GUIStyle(MenuSkin.Label) { fontSize = 21, wordWrap = true };
                _text.normal.textColor = Color.white;
                _hint = new GUIStyle(MenuSkin.Label) { fontSize = 14, alignment = TextAnchor.LowerRight };
                _hint.normal.textColor = new Color(1, 1, 1, 0.8f);
            }
            if (_current.cine) { DrawCinematic(); return; }
            float w = Mathf.Min(Screen.width - 40, 900), h = 130;
            var r = new Rect((Screen.width - w) / 2, Screen.height - h - 30, w, h);
            MenuSkin.DrawPanel(r);
            GUI.Label(new Rect(r.x + 24, r.y + 10, w - 48, 30), _current.speaker, _name);
            GUI.Label(new Rect(r.x + 24, r.y + 42, w - 48, h - 56), _current.text, _text);
            GUI.Label(new Rect(r.x, r.y, w - 10, h - 4), "[Enter] dalej", _hint);
        }

        void DrawCinematic()
        {
            GUI.depth = -2000;
            MenuSkin.Ensure();
            float a = Mathf.Clamp01((Time.unscaledTime - _start) / 0.9f);          // płynne wejście tekstu
            float outA = Mathf.Clamp01((_until - Time.unscaledTime) / 0.6f);         // i wyjście
            float alpha = Mathf.Min(a, outA);
            bool stage = CutsceneStage.Active;
            if (stage)
            {
                // kino: czarne pasy u góry i u dołu, między nimi scena 3D
                GUI.color = Color.black;
                float bar = Screen.height * 0.14f;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, Screen.height - bar * 1.9f, Screen.width, bar * 1.9f), Texture2D.whiteTexture);
                float fade = 1f - Mathf.Clamp01((Time.unscaledTime - _start) / 0.5f);
                if (fade > 0f) { GUI.color = new Color(0, 0, 0, fade * 0.5f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); }
            }
            else
            {
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            float k = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k, H = Screen.height / k;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            bool title = string.IsNullOrEmpty(_current.speaker) || _current.speaker == "TYTUŁ";
            float cy = stage ? H * 0.855f : H / 2f;   // środek bloku tekstu (w trybie sceny – w dolnym pasie)
            if (!title && _current.speaker != "Narrator")
                HudSkin.Outline(new Rect(W / 2 - 450, cy - (stage ? 112f : 150f), 900, 40), _current.speaker.ToUpper(), HudSkin.Text(26), new Color(1f, 0.6f, 0.15f, alpha), 1.5f);
            var st = HudSkin.Text(title ? 54 : (stage ? 26 : 30));
            st.wordWrap = true; st.alignment = TextAnchor.MiddleCenter; st.clipping = TextClipping.Clip;
            HudSkin.Outline(new Rect(W / 2 - 480, cy - (stage ? 80f : 120f), 960, stage ? 150f : 240f), _current.text, st, new Color(1f, 1f, 1f, alpha), 1.4f);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            HudSkin.Outline(new Rect(0, H - 56, W, 28), "[Enter] dalej     [Esc] pomiń", HudSkin.Text(16), new Color(1f, 1f, 1f, 0.6f), 1.2f);
            GUI.color = Color.white;
            GUI.matrix = old;
        }
    }
}
