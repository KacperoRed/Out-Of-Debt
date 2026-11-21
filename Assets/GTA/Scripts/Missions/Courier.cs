using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Zlecenia kurierskie: klawisz J losuje punkt dostawy gdzieś w mieście. Zdąż na czas (pieszo albo autem) – im dalej, tym większa wypłata.
    /// J w trakcie zlecenia je anuluje. Nie działa w trakcie misji fabularnej. Logika po stronie hosta; dodawany przez HUD.
    /// </summary>
    public class Courier : MonoBehaviour
    {
        public static bool Has { get; private set; }
        public static Vector3 Target { get; private set; }

        float _deadline, _reward;
        GameObject _pillar;
        int _streak;

        void Update()
        {
            var p = PlayerController.Local;
            if (p == null || !p.IsSpawned || !GameUtil.IsServerRunning) { Stop(); return; }

            bool blocked = GameInput.Blocked || GameInput.UIOpen;
            if (Input.GetKeyDown(KeyCode.J) && !blocked)
            {
                if (Has) { Stop(); HUD.Notify("Zlecenie kurierskie anulowane", new Color(1f, 0.5f, 0.4f)); _streak = 0; }
                else Begin(p);
            }
            if (!Has) return;

            if (p.IsDead) { Stop(); HUD.Notify("Zlecenie nieudane", new Color(1f, 0.4f, 0.4f)); _streak = 0; return; }
            if (p.Mission != null && p.Mission.InMission) { Stop(); return; }

            if (_pillar != null) _pillar.transform.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);

            Vector3 d = p.transform.position - Target; d.y = 0f;
            if (d.sqrMagnitude < 36f)
            {
                _streak++;
                int bonus = (_streak - 1) * 40;   // seria dostaw
                int pay = Mathf.RoundToInt(_reward) + bonus;
                p.Stats.AddMoney(pay);
                HUD.Notify($"Dostarczono! +${pay}" + (_streak > 1 ? $"  (seria x{_streak})" : ""), new Color(0.4f, 1f, 0.4f));
                Stop();
            }
            else if (Time.time > _deadline)
            {
                Stop();
                _streak = 0;
                HUD.Notify("Spóźniłeś się – klient zrezygnował", new Color(1f, 0.4f, 0.4f));
            }
        }

        void Begin(PlayerController p)
        {
            var rn = RoadNetwork.Instance;
            if (rn == null || rn.NodeCount == 0) return;
            if (p.Mission != null && p.Mission.InMission) { HUD.Notify("Najpierw dokończ misję", new Color(1f, 0.5f, 0.4f)); return; }

            Vector3 best = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 40 && !found; i++)
            {
                Vector3 n = rn.nodes[Random.Range(0, rn.NodeCount)];
                float dist = Vector3.Distance(new Vector3(n.x, 0f, n.z), new Vector3(p.transform.position.x, 0f, p.transform.position.z));
                if (dist > 180f && dist < 650f) { best = n; found = true; }
            }
            if (!found) { HUD.Notify("Brak zleceń w pobliżu", Color.white); return; }

            Target = best;
            float len = Vector3.Distance(new Vector3(best.x, 0f, best.z), new Vector3(p.transform.position.x, 0f, p.transform.position.z));
            _deadline = Time.time + 25f + len / 8f;
            _reward = 100f + len * 0.7f;
            Has = true;

            _pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _pillar.name = "KurierCel";
            Destroy(_pillar.GetComponent<Collider>());
            _pillar.transform.position = best + Vector3.up * 25f;
            _pillar.transform.localScale = new Vector3(4f, 25f, 4f);
            var r = _pillar.GetComponent<Renderer>();
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) r.sharedMaterial = new Material(sh) { color = new Color(1f, 0.6f, 0.1f, 0.35f) };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            HUD.Notify($"Zlecenie kurierskie: {len:0} m, ${_reward:0}. J = anuluj", new Color(1f, 0.8f, 0.3f));
        }

        void Stop()
        {
            Has = false;
            if (_pillar != null) Destroy(_pillar);
        }

        void OnGUI()
        {
            if (!Has) return;
            float k = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k;
            var p = PlayerController.Local;
            float left = Mathf.Max(0f, _deadline - Time.time);
            float dist = p != null ? Vector3.Distance(new Vector3(p.transform.position.x, 0f, p.transform.position.z), new Vector3(Target.x, 0f, Target.z)) : 0f;
            Color c = left < 15f ? new Color(1f, 0.4f, 0.3f) : Color.white;
            HudSkin.Outline(new Rect(W / 2f - 250f, 290f, 500f, 34f), $"KURIER  {left:0}s  •  {dist:0} m  •  ${_reward:0}", HudSkin.Text(24), c, 2f);
            GUI.matrix = old;
        }
    }
}
