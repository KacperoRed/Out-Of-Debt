using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Wygląd menu jak na wzorze: pomarańczowy zaokrąglony panel, zakładki, jasne „pigułki” z białym tekstem szeryfowym.
    /// Wszystkie tekstury powstają w kodzie (zaokrąglone prostokąty), czcionka to systemowa szeryfowa (Georgia / Cambria).
    /// </summary>
    public static class MenuSkin
    {
        public static readonly Color PanelColor = new Color(0.985f, 0.71f, 0.33f);
        public static readonly Color PillColor = new Color(0.99f, 0.79f, 0.52f);
        public static readonly Color PillHover = new Color(1f, 0.86f, 0.64f);
        public static readonly Color PillDark = new Color(0.91f, 0.58f, 0.2f);
        public static readonly Color ShadowColor = new Color(0.8f, 0.5f, 0.15f, 0.55f);

        public static Font UIFont { get { Ensure(); return _font; } }

        static bool _built;
        static Font _font;
        public static GUIStyle Panel, Pill, PillLeft, Button, Tab, TabActive, Title, Label, SliderTrack, SliderThumb;

        static Texture2D RoundRect(int size, int radius, Color color)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x, radius, size - 1 - radius), cy = Mathf.Clamp(y, radius, size - 1 - radius);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * a));
                }
            tex.Apply();
            return tex;
        }

        static GUIStyle Make(Color bg, int radius, int fontSize, TextAnchor anchor, int pad = 10)
        {
            var st = new GUIStyle
            {
                font = _font,
                fontSize = fontSize,
                alignment = anchor,
                border = new RectOffset(radius, radius, radius, radius),
                padding = new RectOffset(pad, pad, 2, 2),
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            st.normal.background = RoundRect(radius * 2 + 2, radius, bg);
            st.normal.textColor = Color.white;
            st.hover.background = st.normal.background;
            st.hover.textColor = Color.white;
            st.active.background = st.normal.background;
            st.active.textColor = Color.white;
            return st;
        }

        static GUIStyle MakeTex(Texture2D bg, int border, int fontSize, TextAnchor anchor, int pad = 10)
        {
            var st = new GUIStyle
            {
                font = _font, fontSize = fontSize, alignment = anchor,
                border = new RectOffset(border, border, border, border),
                padding = new RectOffset(pad, pad, 2, 2),
                clipping = TextClipping.Clip, wordWrap = false,
            };
            st.normal.background = bg; st.hover.background = bg; st.active.background = bg;
            st.normal.textColor = Color.white; st.hover.textColor = Color.white; st.active.textColor = Color.white;
            return st;
        }

        static Texture2D Grad(int radius, Color top, Color bot)
        {
            var k = new Color(0.1f, 0.05f, 0.02f);
            return HudSkin.RoundedTex(radius, 2f, k, k, 60f, top, bot);
        }

        public static void Ensure()
        {
            if (_built) return;
            _built = true;
            HudSkin.Ensure();
            _font = HudSkin.Font;

            // jeden styl w całej grze: pomarańczowa ramka z gradientem, ciemne pigułki, pomarańczowe przyciski
            Panel = new GUIStyle(HudSkin.Frame) { font = _font, alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 14, 2, 2) };
            Pill = new GUIStyle(HudSkin.PillDark) { font = _font, fontSize = 20, alignment = TextAnchor.MiddleCenter };
            PillLeft = new GUIStyle(Pill) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(18, 10, 2, 2) };
            Title = new GUIStyle { font = _font, fontSize = 34, alignment = TextAnchor.MiddleCenter };
            Title.normal.textColor = Color.white;
            Label = new GUIStyle { font = _font, fontSize = 19, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            Label.normal.textColor = Color.white;

            Button = MakeTex(Grad(14, new Color(1f, 0.82f, 0.25f), new Color(0.96f, 0.45f, 0.08f)), 14, 20, TextAnchor.MiddleCenter);
            Button.hover.background = Grad(14, new Color(1f, 0.92f, 0.45f), new Color(1f, 0.58f, 0.16f));
            Button.active.background = Grad(14, new Color(0.85f, 0.42f, 0.06f), new Color(0.65f, 0.25f, 0.04f));

            Tab = MakeTex(Grad(12, new Color(0.85f, 0.45f, 0.1f), new Color(0.6f, 0.27f, 0.05f)), 12, 19, TextAnchor.MiddleCenter);
            Tab.hover.background = Grad(12, new Color(0.95f, 0.55f, 0.14f), new Color(0.72f, 0.33f, 0.07f));
            TabActive = MakeTex(Grad(12, new Color(1f, 0.88f, 0.35f), new Color(1f, 0.55f, 0.12f)), 12, 19, TextAnchor.MiddleCenter);

            SliderTrack = new GUIStyle(HudSkin.Track) { fixedHeight = 22, border = new RectOffset(10, 10, 10, 10) };
            SliderThumb = new GUIStyle { fixedWidth = 28, fixedHeight = 28 };
            SliderThumb.normal.background = HudSkin.Circle;
            SliderThumb.hover.background = HudSkin.Circle;
            SliderThumb.active.background = HudSkin.Circle;
        }

        /// <summary>Panel tła (ten sam styl w menu głównym i pauzie).</summary>
        public static void DrawPanel(Rect r)
        {
            Ensure();
            // miękki cień
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.Box(new Rect(r.x + 4, r.y + 8, r.width, r.height), GUIContent.none, Panel);
            GUI.color = Color.white;
            GUI.Box(r, GUIContent.none, Panel);
        }

        public static float Slider(Rect r, float value)
        {
            Ensure();
            return GUI.HorizontalSlider(r, value, 0f, 1f, SliderTrack, SliderThumb);
        }
    }
}
