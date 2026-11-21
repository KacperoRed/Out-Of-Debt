using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Postać fabularna stojąca w świecie (zleceniodawca, rozmówca): animowana (Kevin Iglesias), z imieniem nad głową,
    /// patrzy na gracza, a gdy trwa jej kwestia – gestykuluje. Składana dopiero, gdy gracz jest blisko (wydajność).
    /// To obiekt lokalny (bez sieci) tworzony przez każdą kopię gry z tych samych danych misji.
    /// </summary>
    public class MissionActor : MonoBehaviour
    {
        public string actorName;
        const float ShowDist = 70f, HideDist = 100f;

        GameObject _root;
        KIActor _kia;
        KIAnimLibrary _lib;
        float _nextCheck;
        float _yaw;
        bool _talking;

        public static MissionActor Create(Transform parent, string name, Vector3 worldPos, float yaw)
        {
            var go = new GameObject("Actor_" + name);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            var a = go.AddComponent<MissionActor>();
            a.actorName = name;
            a._yaw = yaw;
            return a;
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                float d = NearestPlayerDistance(out _);
                if (_root == null && d < ShowDist) Build();
                else if (_root != null && _root.activeSelf && d > HideDist) _root.SetActive(false);
                else if (_root != null && !_root.activeSelf && d < ShowDist) _root.SetActive(true);
            }
            if (_root == null || !_root.activeSelf) return;

            // patrz na najbliższego gracza
            float dist = NearestPlayerDistance(out var pp);
            if (pp != null && dist < 14f)
            {
                var dir = pp.transform.position - transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) _yaw = Mathf.MoveTowardsAngle(_yaw, Quaternion.LookRotation(dir).eulerAngles.y, 160f * Time.deltaTime);
            }
            _root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // kto mówi, ten gestykuluje
            bool talk = DialogueUI.Active && DialogueUI.CurrentSpeaker == actorName;
            if (_kia != null && _lib != null && talk != _talking)
            {
                _talking = talk;
                _kia.Play(talk ? _lib.maleTalk : _lib.maleIdle);
            }
        }

        Vector3 _self;
        float NearestPlayerDistance(out PlayerController who)
        {
            who = null;
            _self = transform.position;
            float best = float.MaxValue;
            foreach (var p in PlayerController.All)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, _self);
                if (d < best) { best = d; who = p; }
            }
            return best;
        }

        void Build()
        {
            _lib = KIAnimLibrary.Get();
            if (_lib == null || !_lib.Ready) return;
            _kia = ActorFactory.Make(actorName, false, transform, Vector3.zero, _yaw);
            _root = ActorFactory.RootOf(_kia, transform);
            NameTag.Add(_root, actorName);
            _kia.Play(_lib.maleIdle);
        }
    }
}
