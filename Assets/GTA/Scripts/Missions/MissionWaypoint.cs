using UnityEngine;

namespace MiniGTA
{
    /// <summary>Znacznik postawiony przez gracza na dużej mapie (M → klik). Prawy klik albo ponowny klik w to samo miejsce go usuwa.</summary>
    public static class MapMarker
    {
        public static bool Has;
        public static Vector3 Pos;

        public static void Set(Vector3 p) { Has = true; Pos = p; }
        public static void Clear() { Has = false; }
    }

    /// <summary>
    /// Strzałki prowadzące gracza: podczas misji – do celu bieżącego kroku (z opisem i odległością), poza misją – do najbliższej
    /// dostępnej misji (fabularne przed pobocznymi), a do tego do znacznika postawionego na mapie.
    /// Gdy cel jest poza kadrem, strzałka przykleja się do krawędzi ekranu i wskazuje kierunek.
    /// Tworzy się sam po wczytaniu sceny.
    /// </summary>
    public static class MissionWaypoint
    {
        /// <summary>Misja, do której prowadzi strzałka poza misją (HUD pokazuje ją jako „NOWA MISJA”).</summary>
        public static Mission Suggested { get; private set; }
        static Mission[] _missions = new Mission[0];
        static float _nextScan;
        static GUIStyle _label, _icon;

        static Mission Pick(Vector3 from)
        {
            Mission best = null;
            float bestScore = float.MaxValue;
            foreach (var m in _missions)
            {
                if (m == null || !m.Available || m.steps == null || m.steps.Length == 0) continue;
                float d = Vector3.Distance(from, m.transform.position);
                float score = m.isStory ? d : d + 100000f; // fabuła przed zleceniami pobocznymi
                if (score < bestScore) { bestScore = score; best = m; }
            }
            return best;
        }

        /// <summary>Wywoływane z HUD.OnGUI.</summary>
        public static void Draw(PlayerController p)
        {
            Suggested = null;
            if (!GameSettings.ShowWaypoint) return;
            var cam = Camera.main;
            if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
            if (p == null || cam == null || p.IsDead) return;
            if (DialogueUI.Active) return;

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _icon = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            bool inMission = p.Mission != null && p.Mission.InMission;
            float hudY = 196f * Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f); // strzałki w interfejsie: pod panelem misji, jedna pod drugą
            if (inMission)
            {
                // cel bieżącego kroku misji
                if (p.Mission.HasTarget.Value)
                {
                    var t = p.Mission.TargetPos.Value; var col = new Color(1f, 0.85f, 0.2f); string title = p.Mission.Objective.Value.ToString();
                    DrawTarget(cam, p, t, title, col, 4f);
                    DrawHudArrow(cam, p, t, title, col, ref hudY);
                }
            }
            else
            {
                if (Time.unscaledTime > _nextScan)
                {
                    _nextScan = Time.unscaledTime + 2f;
                    _missions = Object.FindObjectsByType<Mission>(FindObjectsSortMode.None);
                }
                var m = Pick(p.transform.position);
                Suggested = m;
                if (m != null)
                {
                    var col = m.isStory ? new Color(1f, 0.38f, 0.2f) : new Color(1f, 0.9f, 0.2f);
                    DrawTarget(cam, p, m.transform.position, m.missionName, col, 9f);
                    DrawHudArrow(cam, p, m.transform.position, m.missionName, col, ref hudY);
                }
            }

            if (MapMarker.Has)
            {
                Vector3 d = MapMarker.Pos - p.transform.position; d.y = 0f;
                if (d.magnitude < 10f) { MapMarker.Clear(); HUD.Notify("Dotarłeś do znacznika", Color.cyan); }
                else
                {
                    var col = new Color(0.3f, 0.9f, 1f);
                    DrawTarget(cam, p, MapMarker.Pos, "Znacznik", col, 9f);
                    DrawHudArrow(cam, p, MapMarker.Pos, "Znacznik", col, ref hudY);
                }
            }
        }

        static Texture2D _arrowTex;

        static Texture2D ArrowTex()
        {
            if (_arrowTex != null) return _arrowTex;
            const int n = 64;
            _arrowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // grot skierowany w górę: trójkąt z wcięciem u dołu
                    float fy = y / (n - 1f);              // 0 = dół
                    float w = (1f - fy) * 0.48f;           // szerokość maleje ku górze
                    float cx = Mathf.Abs(x / (n - 1f) - 0.5f);
                    bool inside = cx <= w && fy > 0.05f && fy < 0.97f;
                    // wcięcie u podstawy
                    if (inside && fy < 0.3f && cx < (0.3f - fy) * 0.8f) inside = false;
                    _arrowTex.SetPixel(x, y, inside ? Color.white : new Color(1, 1, 1, 0));
                }
            _arrowTex.Apply();
            return _arrowTex;
        }

        /// <summary>Strzałka w interfejsie (pod panelem misji): wskazuje kierunek do celu względem kamery, z opisem i odległością.</summary>
        static void DrawHudArrow(Camera cam, PlayerController p, Vector3 world, string title, Color col, ref float y)
        {
            Vector3 to = world - p.transform.position; to.y = 0f;
            Vector3 fwd = cam != null ? cam.transform.forward : p.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            float ang = Vector3.SignedAngle(fwd, to, Vector3.up); // + = cel po prawej
            float cx = Screen.width * 0.5f, size = 46f;
            var bg = new Rect(cx - 150, y, 300, 56);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var old = GUI.matrix;
            var pivot = new Vector2(bg.x + 30, bg.y + 28);
            GUIUtility.RotateAroundPivot(ang, pivot);
            GUI.color = col;
            GUI.DrawTexture(new Rect(pivot.x - size / 2, pivot.y - size / 2, size, size), ArrowTex());
            GUI.color = Color.white;
            GUI.matrix = old;
            if (_label == null) _label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            string text = string.IsNullOrEmpty(title) ? $"{to.magnitude:0} m" : $"{title}  ·  {to.magnitude:0} m";
            _label.alignment = TextAnchor.MiddleLeft;
            Draw(new Rect(bg.x + 62, bg.y, bg.width - 66, bg.height), text, _label, col);
            _label.alignment = TextAnchor.MiddleCenter;
            y += 62f;
        }

        static void DrawTarget(Camera cam, PlayerController p, Vector3 world, string title, Color col, float lift)
        {
            float dist = Vector3.Distance(p.transform.position, world);
            string text = string.IsNullOrEmpty(title) ? $"{dist:0} m" : $"{title}\n{dist:0} m";

            Vector3 sp = cam.WorldToScreenPoint(world + Vector3.up * lift);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            Vector2 pos = new Vector2(sp.x, Screen.height - sp.y);
            const float margin = 60f;
            bool off = behind || pos.x < margin || pos.x > Screen.width - margin || pos.y < margin || pos.y > Screen.height - margin;
            if (off)
            {
                Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 dir = pos - center;
                if (dir.sqrMagnitude < 1f) dir = Vector2.up;
                float k = Mathf.Min((Screen.width * 0.5f - margin) / Mathf.Max(0.01f, Mathf.Abs(dir.x)), (Screen.height * 0.5f - margin) / Mathf.Max(0.01f, Mathf.Abs(dir.y)));
                pos = center + dir * k;
                float angle = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, pos);
                Draw(new Rect(pos.x - 30, pos.y - 30, 60, 60), "▲", _icon, col);
                GUI.matrix = old;
            }
            else
            {
                Draw(new Rect(pos.x - 30, pos.y - 30, 60, 60), "◆", _icon, col);
            }
            Draw(new Rect(pos.x - 130, pos.y + (off ? 26 : 22), 260, 44), text, _label, col);
        }

        static void Draw(Rect r, string t, GUIStyle style, Color c)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, style);
            style.normal.textColor = c;
            GUI.Label(r, t, style);
            style.normal.textColor = old;
        }
    }
}
