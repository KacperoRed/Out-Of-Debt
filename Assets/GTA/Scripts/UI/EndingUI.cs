using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Zakończenie gry po spłacie długu: katscena na czarnym ekranie, potem napis „WYGRAŁEŚ” i wybór –
    /// zakończ grę (zapis i powrót do menu) albo graj dalej (wolna gra).
    /// </summary>
    public class EndingUI : MonoBehaviour
    {
        static EndingUI _inst;
        bool _pending, _choice;

        /// <summary>Panel wyboru jest na ekranie – gra stoi.</summary>
        public static bool Active => _inst != null && (_inst._choice || _inst._pending);

        public static void Begin()
        {
            if (_inst == null)
            {
                var go = new GameObject("EndingUI");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<EndingUI>();
            }
            if (_inst._pending || _inst._choice) return;
            DialogueUI.Play(new[]
            {
                new DialogueLine("Rico", "Wszystko do ostatniego centa. Interesy z tobą to przyjemność. Jesteśmy kwita."),
                new DialogueLine("Narrator", "Dług spłacony. Ola wróciła do szkoły, a ty po raz pierwszy od miesięcy możesz spać spokojnie."),
                new DialogueLine("Narrator", "Miasto nigdy nie śpi. Ale teraz nie jesteś już jego dłużnikiem."),
                new DialogueLine("TYTUŁ", "WYGRAŁEŚ"),
            }, true);
            _inst._pending = true;
        }

        void Update()
        {
            if (_pending && !DialogueUI.Active)
            {
                _pending = false;
                _choice = true;
                GameInput.UIOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void Close()
        {
            _choice = false;
            GameInput.UIOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnGUI()
        {
            if (!_choice) return;
            GUI.depth = -3000;
            MenuSkin.Ensure();
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float k = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k, H = Screen.height / k;

            HudSkin.Outline(new Rect(0, H * 0.18f, W, 130), "WYGRAŁEŚ!", HudSkin.Text(110), HudSkin.Gold, 4f);
            HudSkin.Outline(new Rect(0, H * 0.18f + 130, W, 44), "Dług spłacony – jesteś wolny.", HudSkin.Text(30), Color.white, 2f);

            var p = PlayerController.Local;
            if (p != null)
                HudSkin.Outline(new Rect(0, H * 0.18f + 180, W, 34), $"Gotówka: ${p.Stats.Money.Value:N0}", HudSkin.Text(22), new Color(0.5f, 1f, 0.4f), 1.6f);

            float bw = 420, bh = 70, x = W / 2f - bw / 2f, y = H * 0.55f;
            var bs = new GUIStyle(MenuSkin.Button) { fontSize = 28 };
            if (GUI.Button(new Rect(x, y, bw, bh), "WOLNA GRA", bs)) Close();
            if (GUI.Button(new Rect(x, y + bh + 18, bw, bh), "ZAKOŃCZ GRĘ", bs))
            {
                if (p != null) SaveSystem.Save(p);
                Close();
                if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            }
            HudSkin.Outline(new Rect(0, y + bh * 2 + 50, W, 28), "Wolna gra: miasto, zlecenia i kasyno czekają dalej.", HudSkin.Text(16), new Color(1f, 1f, 1f, 0.7f), 1.2f);
            GUI.matrix = old;
        }
    }
}
