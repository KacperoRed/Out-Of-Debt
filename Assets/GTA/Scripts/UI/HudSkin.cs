using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Wspólny styl interfejsu: pomarańczowe ramki z gradientem, ciemne panele z pomarańczowym obramowaniem, pigułki, pasek zdrowia,
    /// ikony (serce, tarcza, gwiazda, broń, słońce) i gruby tekst z czarnym konturem. Wszystkie tekstury powstają w kodzie.
    /// </summary>
    public static class HudSkin
    {
        public static readonly Color Black = new Color(0.1f, 0.05f, 0.02f);
        public static readonly Color Orange = new Color(0.97f, 0.5f, 0.08f);
        public static readonly Color Yellow = new Color(1f, 0.85f, 0.2f);
        public static readonly Color Gold = new Color(1f, 0.78f, 0.1f);

        static bool _built;
        public static Font Font;
        /// <summary>Ramka pomarańczowa (minimapa, baner misji, panele menu).</summary>
        public static GUIStyle Frame, Panel, PillDark, Track, Strip, Debt, FillGreen, FillBlue, Slot;
        public static Texture2D PlayerArrow, Circle, Heart, Shield, StarOn, StarOff, Sun, Pistol, Rifle, Shotgun, Launcher, Knife, Grenade;

        // ---------- generowanie tekstur ----------

        struct Layer { public float depth; public Color top, bot; public Layer(float d, Color t, Color b) { depth = d; top = t; bot = b; } }

        static Texture2D Rounded(int w, int h, int radius, params Layer[] layers)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Clamp(x, radius, w - 1 - radius), cy = Mathf.Clamp(y, radius, h - 1 - radius);
                    float t = radius - Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)); // głębokość od krawędzi
                    float a = Mathf.Clamp01(t + 0.5f);
                    float acc = 0f; Color c = layers[layers.Length - 1].top; float gy = y / (h - 1f);
                    for (int i = 0; i < layers.Length; i++)
                    {
                        acc += layers[i].depth;
                        if (t < acc || i == layers.Length - 1) { c = Color.Lerp(layers[i].bot, layers[i].top, gy); break; }
                    }
                    px[y * w + x] = new Color(c.r, c.g, c.b, c.a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static GUIStyle Box(Texture2D bg, int border, int fontSize = 20, TextAnchor anchor = TextAnchor.MiddleCenter, int pad = 10)
        {
            var st = new GUIStyle
            {
                font = Font,
                fontSize = fontSize,
                alignment = anchor,
                border = new RectOffset(border, border, border, border),
                padding = new RectOffset(pad, pad, 2, 2),
                clipping = TextClipping.Clip,
            };
            st.normal.background = bg; st.hover.background = bg; st.active.background = bg;
            st.normal.textColor = Color.white; st.hover.textColor = Color.white; st.active.textColor = Color.white;
            return st;
        }

        public static Texture2D RoundedTex(int radius, params object[] spec)
        {
            // pomocnik dla MenuSkin: spec = (głębokość, górny, dolny)...
            var layers = new List<Layer>();
            for (int i = 0; i + 2 < spec.Length; i += 3) layers.Add(new Layer((float)spec[i], (Color)spec[i + 1], (Color)spec[i + 2]));
            return Rounded(radius * 2 + 2, radius * 2 + 2, radius, layers.ToArray());
        }

        // wielokąty (współrzędne 0..1, y w górę) z ciemnym konturem na zewnątrz
        static Texture2D Poly(int w, int h, List<Vector2[]> polys, Color top, Color bot, float outline)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2((x + 0.5f) / w * w, (y + 0.5f) / h * h); // piksele
                    bool inside = false; float dmin = 1e9f;
                    foreach (var poly in polys)
                    {
                        int n = poly.Length; bool ins = false;
                        for (int i = 0, j = n - 1; i < n; j = i++)
                        {
                            Vector2 a = new Vector2(poly[i].x * w, poly[i].y * h), b = new Vector2(poly[j].x * w, poly[j].y * h);
                            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) ins = !ins;
                            dmin = Mathf.Min(dmin, DistSeg(p, a, b));
                        }
                        if (ins) inside = true;
                    }
                    Color c;
                    if (inside)
                    {
                        float k = y / (h - 1f);
                        c = Color.Lerp(bot, top, k);
                        c.a = Mathf.Clamp01(dmin + 0.5f) < 1f ? Mathf.Lerp(1f, 1f, 1f) : 1f; // wnętrze pełne
                        // jasna krawędź wewnętrzna
                        if (dmin < outline * 1.6f) c = Color.Lerp(c, Color.white, 0.18f);
                    }
                    else if (dmin < outline) c = new Color(Black.r, Black.g, Black.b, Mathf.Clamp01(outline - dmin + 0.5f));
                    else c = new Color(0, 0, 0, 0);
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        static Vector2[] R(float x0, float y0, float x1, float y1) => new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

        static Texture2D MakeCircle()
        {
            // pierścień: czarny kontur, pomarańczowa obwódka, ciemne wnętrze
            return Rounded(64, 64, 32, new Layer(2f, Black, Black), new Layer(3.5f, Orange, Orange), new Layer(40f, new Color(0.2f, 0.12f, 0.07f), new Color(0.12f, 0.07f, 0.04f)));
        }

        static void BuildIcons()
        {
            Circle = MakeCircle();

            // serce
            var heart = new List<Vector2>();
            for (int i = 0; i < 48; i++)
            {
                float t = i / 48f * Mathf.PI * 2f;
                float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2 * t) - 2f * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                heart.Add(new Vector2(0.5f + x / 16f * 0.4f, 0.5f + (y + 2.5f) / 14.5f * 0.4f));
            }
            Heart = Poly(64, 64, new List<Vector2[]> { heart.ToArray() }, new Color(1f, 0.45f, 0.45f), new Color(0.85f, 0.1f, 0.15f), 2.2f);

            // tarcza
            Shield = Poly(64, 64, new List<Vector2[]> { new[] {
                new Vector2(0.2f, 0.86f), new Vector2(0.5f, 0.94f), new Vector2(0.8f, 0.86f), new Vector2(0.82f, 0.55f), new Vector2(0.74f, 0.32f),
                new Vector2(0.5f, 0.08f), new Vector2(0.26f, 0.32f), new Vector2(0.18f, 0.55f) } }, new Color(0.55f, 0.8f, 1f), new Color(0.15f, 0.45f, 0.9f), 2.2f);

            // gwiazda
            var star = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f; float rr = i % 2 == 0 ? 0.44f : 0.19f;
                star[i] = new Vector2(0.5f + Mathf.Cos(a) * rr, 0.5f + Mathf.Sin(a) * rr - 0.02f);
            }
            StarOn = Poly(64, 64, new List<Vector2[]> { star }, new Color(1f, 0.93f, 0.4f), new Color(1f, 0.62f, 0.05f), 2.4f);
            StarOff = Poly(64, 64, new List<Vector2[]> { star }, new Color(0.38f, 0.34f, 0.33f), new Color(0.2f, 0.17f, 0.16f), 2.4f);

            // strzałka gracza (na minimapie i mapie): grot skierowany w górę, obracany wokół środka
            PlayerArrow = Poly(64, 64, new List<Vector2[]> { new[] { new Vector2(0.5f, 0.96f), new Vector2(0.9f, 0.06f), new Vector2(0.5f, 0.28f), new Vector2(0.1f, 0.06f) } },
                new Color(1f, 0.97f, 0.85f), new Color(1f, 0.62f, 0.1f), 3.2f);

            // słońce
            var sun = new Vector2[24];
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f; float rr = i % 2 == 0 ? 0.46f : 0.32f;
                sun[i] = new Vector2(0.5f + Mathf.Cos(a) * rr, 0.5f + Mathf.Sin(a) * rr);
            }
            Sun = Poly(64, 64, new List<Vector2[]> { sun }, new Color(1f, 0.92f, 0.4f), new Color(1f, 0.6f, 0.1f), 2.2f);

            Color gt = new Color(0.95f, 0.95f, 0.97f), gb = new Color(0.62f, 0.64f, 0.7f);
            Pistol = Poly(128, 64, new List<Vector2[]> {
                R(0.08f, 0.56f, 0.92f, 0.76f),
                new[] { new Vector2(0.12f, 0.56f), new Vector2(0.44f, 0.56f), new Vector2(0.38f, 0.14f), new Vector2(0.14f, 0.14f) },
                new[] { new Vector2(0.44f, 0.56f), new Vector2(0.66f, 0.56f), new Vector2(0.54f, 0.4f), new Vector2(0.44f, 0.4f) } }, gt, gb, 2.6f);
            Rifle = Poly(128, 64, new List<Vector2[]> {
                new[] { new Vector2(0.03f, 0.64f), new Vector2(0.26f, 0.68f), new Vector2(0.26f, 0.42f), new Vector2(0.03f, 0.38f) },
                R(0.24f, 0.46f, 0.74f, 0.7f), R(0.72f, 0.56f, 0.98f, 0.64f),
                new[] { new Vector2(0.44f, 0.46f), new Vector2(0.58f, 0.46f), new Vector2(0.54f, 0.16f), new Vector2(0.42f, 0.16f) },
                new[] { new Vector2(0.3f, 0.46f), new Vector2(0.38f, 0.46f), new Vector2(0.34f, 0.2f), new Vector2(0.27f, 0.2f) } }, gt, gb, 2.6f);
            Shotgun = Poly(128, 64, new List<Vector2[]> {
                new[] { new Vector2(0.03f, 0.62f), new Vector2(0.3f, 0.66f), new Vector2(0.3f, 0.42f), new Vector2(0.04f, 0.34f) },
                R(0.28f, 0.5f, 0.55f, 0.68f), R(0.5f, 0.54f, 0.98f, 0.64f), R(0.5f, 0.42f, 0.9f, 0.5f) }, gt, gb, 2.6f);
            Launcher = Poly(128, 64, new List<Vector2[]> {
                R(0.1f, 0.5f, 0.88f, 0.7f), R(0.86f, 0.46f, 0.98f, 0.74f),
                new[] { new Vector2(0.4f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.46f, 0.2f), new Vector2(0.38f, 0.2f) } }, gt, gb, 2.6f);
            Knife = Poly(128, 64, new List<Vector2[]> {
                new[] { new Vector2(0.28f, 0.7f), new Vector2(0.94f, 0.52f), new Vector2(0.3f, 0.44f) }, R(0.06f, 0.44f, 0.3f, 0.6f) }, gt, gb, 2.6f);
            var gren = new Vector2[20];
            for (int i = 0; i < 20; i++) { float a = i * Mathf.PI * 2f / 20f; gren[i] = new Vector2(0.5f + Mathf.Cos(a) * 0.2f, 0.42f + Mathf.Sin(a) * 0.32f); }
            Grenade = Poly(128, 64, new List<Vector2[]> { gren, R(0.44f, 0.7f, 0.58f, 0.86f) }, new Color(0.6f, 0.8f, 0.5f), new Color(0.25f, 0.45f, 0.2f), 2.6f);
        }

        public static void Ensure()
        {
            if (_built) return;
            _built = true;
            // Unity 6 używa tylko pierwszej nazwy z listy, więc wybieramy pierwszą, która naprawdę jest w systemie
#if UNITY_WEBGL && !UNITY_EDITOR
            // w przeglądarce nie ma czcionek systemowych – bierzemy czcionkę wbudowaną w Unity (zawiera polskie znaki)
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            string pick = null;
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in new[] { "Arial Rounded MT Bold", "Segoe UI Black", "Arial Black", "Verdana", "Segoe UI", "Arial" })
                if (installed.Contains(n)) { pick = n; break; }
            Font = Font.CreateDynamicFontFromOSFont(pick ?? "Arial", 22);
#endif

            var yTop = new Color(1f, 0.83f, 0.2f); var yBot = new Color(0.96f, 0.42f, 0.07f);
            Frame = Box(Rounded(64, 64, 22, new Layer(2.2f, Black, Black), new Layer(3f, Orange, Orange), new Layer(60f, yTop, yBot)), 22);
            Panel = Frame;
            PillDark = Box(Rounded(48, 48, 22, new Layer(2f, Black, Black), new Layer(2.6f, Orange, Orange), new Layer(60f, new Color(0.22f, 0.14f, 0.09f), new Color(0.12f, 0.07f, 0.04f))), 22, 22, TextAnchor.MiddleCenter, 14);
            Track = Box(Rounded(32, 32, 14, new Layer(2f, Black, Black), new Layer(1.6f, new Color(0.62f, 0.3f, 0.08f), new Color(0.62f, 0.3f, 0.08f)), new Layer(60f, new Color(0.15f, 0.1f, 0.08f), new Color(0.08f, 0.05f, 0.04f))), 14);
            Strip = Box(Rounded(32, 32, 14, new Layer(1.6f, new Color(0.35f, 0.17f, 0.04f), new Color(0.35f, 0.17f, 0.04f)), new Layer(60f, new Color(0.08f, 0.04f, 0.02f), new Color(0.04f, 0.02f, 0.01f))), 14);
            Debt = Box(Rounded(32, 32, 14, new Layer(2f, Black, Black), new Layer(1.8f, new Color(0.75f, 0.18f, 0.12f), new Color(0.75f, 0.18f, 0.12f)), new Layer(60f, new Color(0.62f, 0.1f, 0.1f), new Color(0.28f, 0.03f, 0.03f))), 14);
            FillGreen = Box(Rounded(24, 24, 10, new Layer(1f, new Color(0.15f, 0.5f, 0.12f), new Color(0.15f, 0.5f, 0.12f)), new Layer(60f, new Color(0.6f, 0.98f, 0.45f), new Color(0.18f, 0.65f, 0.14f))), 10);
            FillBlue = Box(Rounded(24, 24, 10, new Layer(1f, new Color(0.1f, 0.3f, 0.7f), new Color(0.1f, 0.3f, 0.7f)), new Layer(60f, new Color(0.5f, 0.78f, 1f), new Color(0.15f, 0.42f, 0.9f))), 10);
            Slot = Box(Rounded(32, 32, 14, new Layer(2f, Black, Black), new Layer(2f, Orange, Orange), new Layer(60f, new Color(0.2f, 0.12f, 0.08f), new Color(0.1f, 0.06f, 0.04f))), 14);
            // panel ciemny (pieniądze, broń)
            Dark = Box(Rounded(64, 64, 22, new Layer(2.2f, Black, Black), new Layer(3f, new Color(0.9f, 0.45f, 0.1f), new Color(0.9f, 0.45f, 0.1f)), new Layer(60f, new Color(0.36f, 0.32f, 0.31f), new Color(0.19f, 0.16f, 0.15f))), 22);
            BuildIcons();
        }

        public static GUIStyle Dark;

        static Dictionary<string, Texture2D> _mapIcons;

        /// <summary>Ikona mapy dla znaku (B, W, S, D, K, $, !, ?, P, X, @ = gracz) z Resources/Icons; null, gdy nie ma.</summary>
        public static Texture2D MapIcon(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return null;
            if (_mapIcons == null)
            {
                _mapIcons = new Dictionary<string, Texture2D>();
                void L(string g, string file) { var t = Resources.Load<Texture2D>("Icons/" + file); if (t != null) _mapIcons[g] = t; }
                L("B", "icon_bron"); L("W", "icon_warsztat"); L("S", "icon_sklep_24_7"); L("D", "icon_bar"); L("K", "icon_kryjowka");
                L("$", "icon_kasyno"); L("!", "icon_misja_fabularna"); L("?", "icon_zlecenie"); L("P", "icon_policja");
                L("X", "icon_znacznik"); L("@", "icon_gracz");
            }
            return _mapIcons.TryGetValue(glyph, out var tex) ? tex : null;
        }

        /// <summary>Rysuje ikonę mapy wyśrodkowaną w punkcie (pinezka znacznika kończy się ostrzem w punkcie), opcjonalnie obróconą.</summary>
        public static void MapIconAt(Texture2D icon, float x, float y, float size, bool pin, float angle = 0f, float pivotY = 0.5f)
        {
            GUI.color = Color.white;
            var old = GUI.matrix;
            if (Mathf.Abs(angle) > 0.01f) GUIUtility.RotateAroundPivot(angle, new Vector2(x, y));
            // pivotY = gdzie w ikonie (0 = góra, 1 = dół) leży punkt, wokół którego ikona się obraca / który wskazuje pozycję
            var r = pin ? new Rect(x - size * 0.5f, y - size, size, size) : new Rect(x - size * 0.5f, y - size * pivotY, size, size);
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit, true);
            GUI.matrix = old;
        }

        /// <summary>Rysuje strzałkę gracza wyśrodkowaną w (x, y), obróconą o angle stopni (zgodnie z ruchem wskazówek zegara).</summary>
        public static void DrawPlayerArrow(float x, float y, float size, float angle)
        {
            Ensure();
            if (PlayerArrow == null) return;
            var old = GUI.matrix;
            GUI.color = Color.white;
            // obrót wokół (x, y) liczony w bieżącej (przeskalowanej) przestrzeni GUI: RotateAroundPivot traktuje pivot jako punkt ekranu,
            // więc przy skalowanym GUI.matrix obracałby strzałkę wokół złego punktu (rogu)
            GUI.matrix = old * Matrix4x4.Translate(new Vector3(x, y, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)) * Matrix4x4.Translate(new Vector3(-x, -y, 0f));
            // środek ciężkości strzałki (ok. 57% wysokości od góry) trafia dokładnie w (x, y)
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.57f, size, size), PlayerArrow);
            GUI.matrix = old;
        }

        // ---------- rysowanie ----------

        public static void Draw(GUIStyle s, Rect r) { Ensure(); GUI.Box(r, GUIContent.none, s); }

        public static void Icon(Texture2D t, Rect r) { GUI.color = Color.white; GUI.DrawTexture(r, t, ScaleMode.ScaleToFit); }

        /// <summary>Okrągła plakietka z literą (np. N nad mapą, E w podpowiedzi).</summary>
        public static void Badge(Rect r, string letter, Color letterColor, int size = 22)
        {
            Ensure();
            GUI.color = Color.white;
            GUI.DrawTexture(r, Circle);
            var st = new GUIStyle { font = Font, fontSize = size, alignment = TextAnchor.MiddleCenter };
            Outline(r, letter, st, letterColor, 1.5f);
        }

        /// <summary>Tekst z czarnym konturem (jak na wzorze).</summary>
        public static void Outline(Rect r, string text, GUIStyle st, Color color, float w = 2f)
        {
            Ensure();
            if (st.font == null) st.font = Font;
            var old = st.normal.textColor;
            st.normal.textColor = Black;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * w, r.y + Mathf.Sin(a) * w, r.width, r.height), text, st);
            }
            st.normal.textColor = color;
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }

        public static GUIStyle Text(int size, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            Ensure();
            return new GUIStyle { font = Font, fontSize = size, alignment = anchor, clipping = TextClipping.Overflow };
        }

        /// <summary>Podpowiedź przy obiekcie: ciemna pigułka z okrągłym klawiszem i napisem (np. [E] WEJDŹ DO KRYJÓWKI).</summary>
        public static void Prompt(Rect center, string key, string text)
        {
            Ensure();
            var st = Text(24, TextAnchor.MiddleLeft);
            float tw = st.CalcSize(new GUIContent(text)).x;
            float w = Mathf.Max(260f, tw + 100f), h = 62f;
            var r = new Rect(center.center.x - w / 2f, center.y, w, h);
            Draw(PillDark, r);
            Badge(new Rect(r.x + 8, r.y + 7, 48, 48), key, Yellow, 26);
            Outline(new Rect(r.x + 68, r.y, w - 76, h), text, st, Color.white, 1.5f);
        }

        /// <summary>Ikona broni do panelu broni.</summary>
        public static Texture2D WeaponIcon(WeaponData w)
        {
            Ensure();
            if (w == null) return Pistol;
            switch (w.kind)
            {
                case WeaponKind.Melee: return Knife;
                case WeaponKind.Throwable: return Grenade;
                case WeaponKind.Rocket: return Launcher;
            }
            if (w.pellets > 1) return Shotgun;
            if (w.automatic || w.magazineSize > 20 || w.aimFov > 0f) return Rifle;
            return Pistol;
        }
    }
}
