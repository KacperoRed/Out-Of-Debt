using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Łapówka: klawisz K zdejmuje poszukiwanie za gotówkę (400 zł za gwiazdkę), ale tylko gdy żaden policjant Cię akurat nie widzi.
    /// Logika po stronie hosta; dodawany przez HUD.
    /// </summary>
    public class Bribe : MonoBehaviour
    {
        public const int PricePerStar = 400;

        void Update()
        {
            var p = PlayerController.Local;
            if (p == null || !p.IsSpawned || p.IsDead || !GameUtil.IsServerRunning) return;
            if (!Input.GetKeyDown(KeyCode.K) || GameInput.Blocked || GameInput.UIOpen || p.Wanted == null) return;

            int stars = p.Wanted.Level;
            if (stars <= 0) { HUD.Notify("Nikt Cię nie szuka", Color.white); return; }
            if (p.Wanted.SeenByPolice.Value) { HUD.Notify("Policja Cię widzi – najpierw im uciekaj", new Color(1f, 0.4f, 0.4f)); return; }
            int cost = stars * PricePerStar;
            if (!p.Stats.TrySpend(cost)) { HUD.Notify($"Łapówka kosztuje ${cost} – masz za mało gotówki", new Color(1f, 0.4f, 0.4f)); return; }
            p.Wanted.ClearWanted();
            HUD.Notify($"Łapówka -${cost}: policja odpuszcza", new Color(0.4f, 1f, 0.4f));
        }
    }
}
