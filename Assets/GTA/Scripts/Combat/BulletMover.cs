using UnityEngine;

namespace MiniGTA
{
    /// <summary>Wizualny pocisk: leci po prostej od lufy do celu ze stałą prędkością (bez grawitacji) i znika.</summary>
    public class BulletMover : MonoBehaviour
    {
        public Vector3 from, to;
        public float speed = 220f;
        float _t, _dur;

        void Start()
        {
            _dur = Mathf.Max(0.25f, Vector3.Distance(from, to) / speed);
            transform.position = from;
            transform.rotation = Quaternion.LookRotation((to - from).normalized);
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _dur);
            transform.position = Vector3.Lerp(from, to, k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
