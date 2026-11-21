using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Własna grafika menu startowego rysowana w kodzie: nocne miasto o zachodzie, słońce, trzy warstwy wieżowców z oknami,
    /// droga z linią przerywaną i gwiazdy. Tekstura 960×540 budowana raz (deterministycznie).
    /// </summary>
    public static class MenuArt
    {
        const int W = 960, H = 540;
        static Texture2D _tex;

        public static Texture2D Get()
        {
            if (_tex != null) return _tex;
            var pix = new Color32[W * H];
            var old = Random.state;
            Random.InitState(2024);

            // niebo: granat → fiolet → pomarańcz przy horyzoncie
            int horizon = 175;   // wiersz od dołu
            Color top = new Color(0.03f, 0.04f, 0.12f), mid = new Color(0.30f, 0.10f, 0.32f), low = new Color(1f, 0.52f, 0.20f);
            for (int y = 0; y < H; y++)
            {
                float t = Mathf.Clamp01((y - horizon) / (float)(H - horizon));
                Color c = t < 0.45f ? Color.Lerp(low, mid, t / 0.45f) : Color.Lerp(mid, top, (t - 0.45f) / 0.55f);
                for (int x = 0; x < W; x++) pix[y * W + x] = c;
            }

            // gwiazdy
            for (int i = 0; i < 160; i++)
            {
                int x = Random.Range(0, W), y = Random.Range(horizon + 120, H);
                float b = Random.Range(0.4f, 1f);
                pix[y * W + x] = new Color(b, b, b * 0.9f);
            }

            // słońce
            Vector2 sun = new Vector2(690f, horizon + 55f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), sun);
                    if (d < 170f)
                    {
                        float glow = Mathf.Pow(1f - d / 170f, 2f) * 0.55f;
                        pix[y * W + x] = Color.Lerp(pix[y * W + x], new Color(1f, 0.7f, 0.3f), glow);
                    }
                    if (d < 62f)
                    {
                        // pasy na tarczy słońca (styl retro)
                        bool stripe = y < sun.y - 4 && ((int)(y - sun.y) % 11 + 11) % 11 < 4;
                        if (!stripe) pix[y * W + x] = Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.25f), Mathf.InverseLerp(sun.y + 62f, sun.y - 62f, y));
                    }
                }

            // wieżowce: tył (jasne), środek, przód (ciemne, z oknami)
            Skyline(pix, horizon, 60, 22, 70, 30, 130, new Color(0.35f, 0.17f, 0.38f), false);
            Skyline(pix, horizon, 40, 26, 90, 30, 160, new Color(0.17f, 0.08f, 0.24f), false);
            Skyline(pix, horizon, 20, 34, 110, 20, 190, new Color(0.06f, 0.03f, 0.10f), true);

            // ulica
            for (int y = 0; y < horizon; y++)
            {
                float t = y / (float)horizon;
                Color c = Color.Lerp(new Color(0.03f, 0.03f, 0.05f), new Color(0.10f, 0.09f, 0.12f), t);
                for (int x = 0; x < W; x++) pix[y * W + x] = c;
            }
            // chodnik + linia przerywana
            for (int x = 0; x < W; x++)
                for (int y = horizon - 14; y < horizon; y++) pix[y * W + x] = new Color(0.16f, 0.14f, 0.18f);
            for (int x = 0; x < W; x++)
                if ((x / 36) % 2 == 0)
                    for (int y = 60; y < 66; y++) pix[y * W + x] = new Color(0.95f, 0.78f, 0.25f);
            // odbicie zachodu na asfalcie
            for (int y = horizon - 14; y > 0; y--)
            {
                float k = (y / (float)horizon);
                for (int x = 520; x < 860; x++)
                {
                    float f = Mathf.Clamp01(1f - Mathf.Abs(x - 690f) / 170f) * k * 0.35f;
                    pix[y * W + x] = Color.Lerp(pix[y * W + x], new Color(1f, 0.5f, 0.2f), f);
                }
            }

            // winieta
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = (x / (float)W - 0.5f) * 2f, dy = (y / (float)H - 0.5f) * 2f;
                    float v = 1f - Mathf.Clamp01((dx * dx + dy * dy) * 0.28f);
                    Color c = pix[y * W + x];
                    pix[y * W + x] = new Color(c.r * v, c.g * v, c.b * v, 1f);
                }
            Random.state = old;

            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "MenuArt" };
            _tex.SetPixels32(pix);
            _tex.Apply(false, true);
            return _tex;
        }

        static void Skyline(Color32[] pix, int baseY, int extra, int minW, int maxW, int minH, int maxH, Color col, bool windows)
        {
            int x = -10;
            // extra: bazowa wysokość warstwy nad horyzontem; minH/maxH: zakres dodatkowej wysokości
            while (x < W)
            {
                int bw = Random.Range(minW, maxW);
                int bh = Mathf.Min(extra / 2 + Random.Range(minH, maxH), H - baseY - 60);   // wysokość budynku nad linią chodnika
                int antenna = Random.value > 0.7f ? Random.Range(10, 34) : 0;
                for (int xx = Mathf.Max(0, x); xx < Mathf.Min(W, x + bw); xx++)
                {
                    for (int y = baseY - 14; y < baseY - 14 + bh && y < H; y++)
                    {
                        Color c = col;
                        if (windows)
                        {
                            int lx = xx - x, ly = y - (baseY - 14);
                            if (lx > 4 && lx < bw - 5 && ly > 6 && lx % 8 < 4 && ly % 12 < 6 && Hash(xx / 8, y / 12) > 0.55f)
                                c = Hash(xx / 8 + 7, y / 12) > 0.8f ? new Color(0.5f, 0.85f, 1f) : new Color(1f, 0.82f, 0.4f);
                        }
                        pix[y * W + xx] = c;
                    }
                    if (antenna > 0 && xx == x + bw / 2)
                    {
                        int top = Mathf.Min(H - 1, baseY - 14 + bh);
                        for (int y = top; y < Mathf.Min(H, top + antenna); y++) pix[y * W + xx] = col;
                    }
                }
                x += bw + Random.Range(0, 4);
            }
        }

        static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }
}
