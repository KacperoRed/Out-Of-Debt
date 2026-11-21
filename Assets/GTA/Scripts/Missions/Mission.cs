using System;
using UnityEngine;

namespace MiniGTA
{
    public enum MissionStepType
    {
        GoTo,           // dojdź/dojedź do punktu
        DriveTo,        // dojedź do punktu SIEDZĄC w aucie
        EnterVehicle,   // wsiądź do dowolnego auta (albo do auta zespawnowanego w tym kroku)
        KillEnemies,    // zabij wrogów zespawnowanych przy punkcie
        LoseWanted,     // zgub policję (0 gwiazdek)
        Survive,        // przeżyj X sekund
        Checkpoints,    // odwiedź wszystkie punkty z listy `points` (pieszo lub autem, w dowolnej kolejności)
        Talk,           // podejdź do rozmówcy (target + contact) – po podejściu startuje rozmowa (katscena)
        Dialogue,       // sama katscena w środku misji (bez chodzenia)
    }

    [Serializable]
    public class MissionStep
    {
        public MissionStepType type = MissionStepType.GoTo;
        [Tooltip("Tekst celu wyświetlany na HUD")]
        public string objective = "Jedź do punktu";
        [Tooltip("Punkt docelowy / miejsce spawnu wrogów lub auta")]
        public Transform target;
        public float radius = 6f;

        [Header("KillEnemies")]
        public int enemyCount = 3;

        [Tooltip("KillEnemies: ile fal wrogów (kolejna pojawia się po zabiciu poprzedniej)")]
        public int waves = 1;

        [Header("Survive")]
        public float duration = 30f;

        [Header("Checkpoints / Talk / Dialogue")]
        public Transform[] points;
        [Tooltip("Rozmowa grana w kroku Talk / Dialogue")]
        public DialogueLine[] lines;
        [Tooltip("Imię postaci stojącej w target (animowana, z imieniem nad głową)")]
        public string contact;

        [Header("Opcje")]
        [Tooltip("0 = bez limitu czasu. Po przekroczeniu misja przegrana.")]
        public float timeLimit = 0f;
        [Tooltip("Ustaw tyle gwiazdek na starcie kroku (np. po napadzie)")]
        public int setWantedOnStart = 0;
        [Tooltip("Zespawnuj auto w punkcie target na starcie kroku (np. do kradzieży)")]
        public bool spawnVehicleAtTarget = false;
        [Tooltip("Jakie auto zespawnować (puste = domyślne z MissionManager)")]
        public GameObject vehicleOverride;
    }

    /// <summary>
    /// Definicja misji. Postaw obiekt na mapie (to jest punkt startu – żółty znacznik),
    /// wypełnij kroki w Inspectorze. MissionManager sam ją znajdzie.
    /// Wszyscy gracze stojący blisko startu biorą udział razem (CO-OP).
    /// </summary>
    public class Mission : MonoBehaviour
    {
        public string missionName = "Nowa misja";
        [TextArea] public string description;
        public int moneyReward = 1000;
        public bool repeatable = true;
        public float startRadius = 3f;
        [Tooltip("Gracze w tym promieniu od startu dołączają do misji (co-op)")]
        public float coopJoinRadius = 12f;
        public MissionStep[] steps = new MissionStep[0];

        [Header("Fabuła")]
        [Tooltip("Akt / rozdział (tylko opis)")]
        public string chapter;
        [Tooltip("Misja startuje dopiero po ustawieniu tej flagi fabuły (puste = zawsze)")]
        public string requiresFlag;
        [Tooltip("Flaga ustawiana po ukończeniu misji")]
        public string setsFlag;
        public DialogueLine[] intro = new DialogueLine[0];
        public DialogueLine[] outro = new DialogueLine[0];
        [Tooltip("Postać zlecająca misję – stoi przy znaczniku startu")]
        public string contact;
        [Tooltip("Misja główna fabuły – znacznik pulsuje i pokazuje się na mapie")]
        public bool isStory;
        [Tooltip("Ile długu spłaca ukończenie tej misji")]
        public int debtPayment;

        /// <summary>Czy misja jest teraz dostępna (spełnione wymagania fabularne, nie ukończona jednorazowa).</summary>
        public bool Available => Story.Has(requiresFlag) && (repeatable || !Story.Has("done:" + missionName));

        [Header("Wygląd znacznika")]
        public Color markerColor = new Color(1f, 0.85f, 0.1f, 0.6f);

        GameObject _marker;
        bool _hidden;

        void Start()
        {
            // prosty świecący walec jako znacznik startu (widoczny lokalnie u każdego)
            _marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _marker.name = "MissionMarker";
            Destroy(_marker.GetComponent<Collider>());
            _marker.transform.SetParent(transform, false);
            _marker.transform.localPosition = new Vector3(0f, 1f, 0f);
            _marker.transform.localScale = new Vector3(startRadius * 2f, 1f, startRadius * 2f);
            var mat = new Material(Shader.Find("Sprites/Default")) { color = markerColor };
            _marker.GetComponent<Renderer>().sharedMaterial = mat;

            // słup światła – widać go z daleka między budynkami
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(_marker.transform, false);
            beam.transform.localPosition = new Vector3(0f, 20f, 0f);
            beam.transform.localScale = new Vector3(0.14f, 20f, 0.14f);
            var beamMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(markerColor.r, markerColor.g, markerColor.b, 0.28f) };
            beam.GetComponent<Renderer>().sharedMaterial = beamMat;
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // postaci: zleceniodawca przy starcie + rozmówcy z kroków Talk
            if (!string.IsNullOrEmpty(contact)) MissionActor.Create(transform, contact, transform.position + new Vector3(2.2f, 0f, 1.2f), 200f);
            if (steps != null)
                foreach (var st in steps)
                    if (st != null && !string.IsNullOrEmpty(st.contact) && st.target != null)
                        MissionActor.Create(transform, st.contact, st.target.position, st.target.eulerAngles.y);
        }

        void Update()
        {
            bool show = Available && !_hidden;
            if (_marker != null && _marker.activeSelf != show) _marker.SetActive(show);
        }

        public void SetMarkerVisible(bool visible)
        {
            _hidden = !visible;
            if (_marker != null) _marker.SetActive(visible && Available);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, startRadius);
            if (steps == null) return;
            Vector3 prev = transform.position;
            foreach (var s in steps)
            {
                if (s == null || s.target == null) continue;
                Gizmos.DrawLine(prev, s.target.position);
                Gizmos.DrawWireSphere(s.target.position, s.radius);
                prev = s.target.position;
            }
        }
    }
}
