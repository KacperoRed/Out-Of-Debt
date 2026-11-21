using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Podgląd postaci w przebieralni: osobna scenka daleko pod mapą, kamera renderuje do tekstury, którą rysuje menu.
    /// Postać składa się z tych samych części i kolorów co bohater w grze (CharacterAppearance z isPlayer = true),
    /// więc każda zmiana w przebieralni widać od razu. Kamera jest włączona tylko, gdy przebieralnia jest otwarta.
    /// </summary>
    public class CharacterPreview : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }

        Camera _cam;
        Transform _stage, _character;
        float _yaw = 160f;
        bool _active;

        void Awake()
        {
            _stage = new GameObject("PreviewStage").transform;
            _stage.SetParent(transform, false);
            _stage.position = new Vector3(0f, -3000f, 0f);

            Texture = new RenderTexture(420, 620, 24) { name = "CharacterPreview" };

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localPosition = new Vector3(0f, 1.0f, 5.0f);   // dalej, żeby w kadrze była cała postać z głową i czapką
            camGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = Texture;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.16f, 0.5f, 0.82f, 1f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 14f; // z daleka nie widać miasta, tylko scenkę
            _cam.enabled = false;

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(_stage, false);
            lightGo.transform.localRotation = Quaternion.Euler(30f, 200f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.shadows = LightShadows.None;

            // postać: korzeń z CharacterAppearance (gracz) + dziecko ModularModel, tak jak w prefabie gracza
            var go = new GameObject("PreviewCharacter");
            go.transform.SetParent(_stage, false);
            go.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            var model = new GameObject("ModularModel");
            model.transform.SetParent(go.transform, false);
            model.AddComponent<ModularCharacter>();
            CharacterAppearance.Ensure(go, true);
            _character = go.transform;
            _stage.gameObject.SetActive(false);
        }

        /// <summary>Włącz/wyłącz podgląd (kamera renderuje tylko, gdy przebieralnia jest otwarta).</summary>
        public void SetActive(bool on)
        {
            if (_active == on) return;
            _active = on;
            _stage.gameObject.SetActive(on);
            if (_cam != null) _cam.enabled = on;
        }

        /// <summary>Obrót postaci myszą (przeciąganie nad podglądem).</summary>
        public void Rotate(float deltaYaw) { _yaw += deltaYaw; }

        void Update()
        {
            if (!_active || _character == null) return;
            _yaw += 12f * Time.unscaledDeltaTime; // powolny obrót
            _character.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            // gra bywa spauzowana (Time.timeScale = 0) – animacja postaci w podglądzie ma iść dalej
            var an = _character.GetComponentInChildren<Animator>();
            if (an != null && an.updateMode != AnimatorUpdateMode.UnscaledTime) an.updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
