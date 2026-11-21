using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Trzymanie broni na animowanej postaci. Paczka nie ma pozy „celowania”, więc po animacji (LateUpdate)
    /// unosimy prawą rękę do przodu, a broń (GunSocket w dłoni) ustawiamy zawsze lufą w kierunku patrzenia postaci.
    /// Dzięki temu broń nie wisi krzywo ani nie wystaje z ciała, niezależnie od animacji biegu/chodzenia.
    /// Uzbrojeni: gracz z bronią w ręku (nie w pojeździe) oraz policja/gangsterzy (CombatNpcAI).
    /// </summary>
    public class ArmedPose : MonoBehaviour
    {
        public Transform socket;
        Transform _arm, _fore, _hand, _larm, _lfore, _lhand;
        WeaponVisual _visual;
        CombatNpcAI _npc;
        Health _health;
        PlayerController _player;
        float _blend;
        float _swing = -1f;                 // 0..1 w trakcie zamachu, -1 = brak
        const float SwingTime = 0.45f;

        /// <summary>Zamach kijem: uniesienie, szybkie uderzenie po skosie i powrót.</summary>
        public void Swing() { if (_swing < 0f) _swing = 0f; }

        static Vector3 Mix(Vector3 a, Vector3 b, float t) => Vector3.Slerp(a, b, t);

        /// <summary>Kierunek w trakcie zamachu: rest → wind (zamach) → hit (uderzenie) → rest.</summary>
        Vector3 SwingDir(Vector3 rest, Vector3 wind, Vector3 hit)
        {
            float p = _swing;
            if (p < 0.3f) { float t = p / 0.3f; return Mix(rest, wind, t * t * (3f - 2f * t)); }
            if (p < 0.5f) { float q = (p - 0.3f) / 0.2f; return Mix(wind, hit, q * q); }
            float r = (p - 0.5f) / 0.5f;
            return Mix(hit, rest, r * r * (3f - 2f * r));
        }

        public bool Armed
        {
            get
            {
                if (_health != null && _health.Dead) return false;
                if (_npc != null) return true;
                return _visual != null && _visual.Showing && (_player == null || !_player.InVehicle);
            }
        }

        void Awake()
        {
            _visual = GetComponent<WeaponVisual>();
            _npc = GetComponent<CombatNpcAI>();
            _health = GetComponent<Health>();
            _player = GetComponent<PlayerController>();
        }

        /// <summary>Szuka kości prawej ręki w aktywnym stroju i przenosi tam gniazdo na broń.</summary>
        public void Setup(Transform modelRoot)
        {
            _arm = FindDeep(modelRoot, "RightArm");
            _fore = FindDeep(modelRoot, "RightForeArm");
            _hand = FindDeep(modelRoot, "RightHand");
            _larm = FindDeep(modelRoot, "LeftArm");
            _lfore = FindDeep(modelRoot, "LeftForeArm");
            _lhand = FindDeep(modelRoot, "LeftHand");
            if (_hand == null) return;
            if (socket == null) socket = new GameObject("GunSocket").transform;
            socket.SetParent(_hand, false);
            socket.localPosition = Vector3.zero;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        void LateUpdate()
        {
            if (socket == null || _hand == null) return;
            _blend = Mathf.MoveTowards(_blend, Armed ? 1f : 0f, Time.deltaTime * 6f);

            Vector3 fwd = transform.forward, right = transform.right;
            bool melee = _visual != null && _visual.ShowingMelee && _npc == null;
            if (_swing >= 0f)
            {
                _swing += Time.deltaTime / SwingTime;
                if (_swing >= 1f || !melee) _swing = -1f;
            }
            bool swinging = _swing >= 0f && melee;
            if (!melee)
            {
                // strzelba: ramię do przodu i lekko w dół, przedramię prosto przed siebie (pozycja sprzed przeróbek)
                if (_blend > 0.01f && _arm != null && _fore != null)
                {
                    Aim(_arm, _fore.position - _arm.position, (fwd * 0.95f + Vector3.down * 0.25f).normalized);
                    Aim(_fore, _hand.position - _fore.position, (fwd + Vector3.up * 0.05f).normalized);
                }
                socket.position = _hand.position + fwd * 0.06f;
                socket.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                return;
            }

            if (_blend > 0.01f && _arm != null && _fore != null)
            {
                // kij: łokieć zgięty, przedramię w górę, kij oparty o ramię (głowica do góry i trochę do przodu)
                Vector3 upperDir = (fwd * 0.55f + Vector3.down * 0.65f).normalized;
                Vector3 foreDir = (fwd * 0.6f + Vector3.up * 0.8f).normalized;
                if (swinging)
                {
                    // zamach znad prawego ramienia na ukos w dół i do przodu
                    upperDir = SwingDir(upperDir, (Vector3.up * 0.85f - fwd * 0.15f + right * 0.5f).normalized, (fwd * 0.8f + Vector3.down * 0.35f - right * 0.25f).normalized);
                    foreDir = SwingDir(foreDir, (Vector3.up * 0.9f - fwd * 0.3f + right * 0.3f).normalized, (fwd * 0.95f + Vector3.down * 0.15f - right * 0.3f).normalized);
                }
                Aim(_arm, _fore.position - _arm.position, upperDir);
                Aim(_fore, _hand.position - _fore.position, foreDir);
            }
            socket.position = _hand.position + fwd * 0.06f;
            Vector3 aim = (Vector3.up * 0.95f + fwd * 0.45f).normalized;
            if (swinging)
                aim = SwingDir(aim, (Vector3.up * 0.7f - fwd * 0.6f + right * 0.2f).normalized, (fwd * 0.85f + Vector3.down * 0.5f - right * 0.2f).normalized);
            socket.rotation = Quaternion.LookRotation(aim, -fwd);
        }

        /// <summary>
        /// Chwyt broni palnej: obie ręce dociągane dwukościowym IK do punktów przed klatką piersiową.
        /// Prawa trzyma chwyt, lewa podpiera (pistolet – pod chwytem, karabin / strzelba – z przodu lufy). Łokcie na zewnątrz i w dół.
        /// </summary>
        void GunPose(Vector3 aimDir, Vector3 right)
        {
            bool longGun = false;
            var wc = GetComponent<WeaponController>();
            if (wc != null && _npc == null)
            {
                var w = wc.Current;
                longGun = w.automatic || w.pellets > 1 || w.aimFov > 0f || w.magazineSize > 20 || w.kind == WeaponKind.Rocket;
            }
            else if (_npc != null) longGun = false;

            Vector3 shoulder = _arm.position;
            Vector3 rTarget = shoulder + aimDir * 0.5f - right * 0.1f + Vector3.down * 0.17f;
            SolveTwoBone(_arm, _fore, _hand, rTarget, shoulder + right * 0.6f + Vector3.down * 0.8f, _blend);

            if (_larm != null && _lfore != null && _lhand != null)
            {
                Vector3 lTarget = longGun
                    ? rTarget + aimDir * 0.34f - right * 0.04f + Vector3.down * 0.05f
                    : rTarget - right * 0.07f + aimDir * 0.02f + Vector3.down * 0.05f;
                SolveTwoBone(_larm, _lfore, _lhand, lTarget, _larm.position - right * 0.6f + Vector3.down * 0.8f, _blend);
            }
        }

        /// <summary>Dwukościowe IK: ustawia ramię i przedramię tak, by dłoń trafiła w target; łokieć ucieka w stronę pole.</summary>
        static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float weight)
        {
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
            if (l1 < 1e-4f || l2 < 1e-4f) return;
            Vector3 toT = target - a;
            float d = Mathf.Clamp(toT.magnitude, 0.05f, (l1 + l2) * 0.999f);
            Vector3 dir = toT.normalized;
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float angA = Mathf.Acos(cosA);
            Vector3 side = Vector3.ProjectOnPlane(pole - a, dir);
            side = side.sqrMagnitude < 1e-6f ? Vector3.up : side.normalized;
            Vector3 elbow = a + (dir * Mathf.Cos(angA) + side * Mathf.Sin(angA)) * l1;

            RotateBone(upper, b - a, elbow - a, weight);
            RotateBone(lower, end.position - lower.position, (a + dir * d) - lower.position, weight);
        }

        static void RotateBone(Transform bone, Vector3 current, Vector3 desired, float weight)
        {
            if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f) return;
            var delta = Quaternion.FromToRotation(current, desired);
            bone.rotation = Quaternion.Slerp(bone.rotation, delta * bone.rotation, weight);
        }

        void Aim(Transform bone, Vector3 current, Vector3 desired)
        {
            if (current.sqrMagnitude < 1e-6f) return;
            var delta = Quaternion.FromToRotation(current, desired);
            bone.rotation = Quaternion.Slerp(bone.rotation, delta * bone.rotation, _blend);
        }
    }
}
