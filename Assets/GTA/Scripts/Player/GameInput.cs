using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// JEDNO miejsce z całym sterowaniem. Chcesz zmienić klawisze albo przejść na nowy
    /// Input System / pad? Zmieniasz tylko ten plik.
    /// Wymaga: Project Settings > Player > Active Input Handling = "Both" (albo "Input Manager (Old)").
    /// </summary>
    public static class GameInput
    {
        public static bool Blocked => ConsoleOpen || Cursor.lockState != CursorLockMode.Locked;

        /// <summary>Konsola deweloperska (klawisz `) jest otwarta – sterowanie postaci stoi.</summary>
        public static bool ConsoleOpen;

        /// <summary>Otwarte okno (np. sklep) – klik myszą nie blokuje wtedy kursora.</summary>
        public static bool UIOpen;

        /// <summary>Wymuszony ruch (testy automatyczne, przerywniki filmowe). null = normalne sterowanie.</summary>
        public static Vector2? OverrideMove;

        public static Vector2 Move => OverrideMove ?? (Blocked ? Vector2.zero : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")));
        public static Vector2 Look => Blocked ? Vector2.zero : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        public static float Zoom => Blocked ? 0f : Input.mouseScrollDelta.y;

        public static bool Sprint => !Blocked && Input.GetKey(KeyCode.LeftShift);
        public static bool Jump => !Blocked && Input.GetKeyDown(KeyCode.Space);
        public static bool Handbrake => !Blocked && Input.GetKey(KeyCode.Space);
        public static bool Interact => !Blocked && Input.GetKeyDown(KeyCode.F);          // wsiądź / wysiądź
        public static bool FireHeld => !Blocked && Input.GetMouseButton(0);
        public static bool FirePressed => !Blocked && Input.GetMouseButtonDown(0);
        public static bool Aim => !Blocked && Input.GetMouseButton(1);
        public static bool Reload => !Blocked && Input.GetKeyDown(KeyCode.R);
        public static bool FlipVehicle => !Blocked && Input.GetKeyDown(KeyCode.R);
        public static bool Horn => !Blocked && Input.GetKey(KeyCode.H);

        /// <summary>0 – schowaj broń / wyjmij ostatnią.</summary>
        public static bool HolsterPressed => !Blocked && Input.GetKeyDown(KeyCode.Alpha0);

        public static int WeaponSlotPressed()
        {
            if (Blocked) return -1;
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
            return -1;
        }

        public static float WeaponScroll => Blocked ? 0f : Input.mouseScrollDelta.y;

        /// <summary>Esc odblokowuje kursor, klik myszą blokuje go z powrotem.</summary>
        /// <summary>Menu pauzy (Esc) jest otwarte – kliknięcia w menu nie mogą blokować kursora.</summary>
        public static bool PauseOpen;

        public static void UpdateCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Esc: otwiera pauzę, a przy otwartej pauzie – wznawia grę
                bool resume = PauseOpen && Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = resume ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !resume;
            }
            else if (!UIOpen && !PauseOpen && Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && GUIUtility.hotControl == 0)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
