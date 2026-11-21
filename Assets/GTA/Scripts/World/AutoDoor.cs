using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Rozsuwane drzwi (tylko wizualne, bez collidera – NPC i gracze przechodzą zawsze).
    /// Otwierają się, gdy ktoś (gracz, NPC, auto) jest w pobliżu. Każdy klient liczy to lokalnie.
    /// </summary>
    public class AutoDoor : MonoBehaviour
    {
        public Transform leftPanel;
        public Transform rightPanel;
        public float openDistance = 1.2f;   // o ile rozsuwa się każde skrzydło
        public float triggerRadius = 4f;
        public float speed = 4f;

        Vector3 _leftClosed, _rightClosed;
        float _open;
        float _nextCheck;
        bool _wantOpen;
        static readonly Collider[] _buffer = new Collider[16];

        void Start()
        {
            if (leftPanel != null) _leftClosed = leftPanel.localPosition;
            if (rightPanel != null) _rightClosed = rightPanel.localPosition;
        }

        void Update()
        {
            if (Time.time > _nextCheck)
            {
                _nextCheck = Time.time + 0.2f;
                _wantOpen = false;
                int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, triggerRadius, _buffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < n; i++)
                    if (_buffer[i].GetComponentInParent<Health>() != null || _buffer[i] is CharacterController) { _wantOpen = true; break; }
            }

            _open = Mathf.MoveTowards(_open, _wantOpen ? 1f : 0f, Time.deltaTime * speed);
            if (leftPanel != null) leftPanel.localPosition = _leftClosed - Vector3.right * openDistance * _open;
            if (rightPanel != null) rightPanel.localPosition = _rightClosed + Vector3.right * openDistance * _open;
        }
    }
}
