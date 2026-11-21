using System.Collections;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Odtwarzanie dźwięków 3D w świecie (krótkie, z puli). Głośność = Master × Efekty z menu.</summary>
    public class GameAudio : MonoBehaviour
    {
        static GameAudioLibrary _lib;
        static bool _searched;
        static GameAudio _inst;
        static float _lastShot;

        public static GameAudioLibrary Library
        {
            get
            {
                if (!_searched) { _lib = Resources.Load<GameAudioLibrary>("GameAudio"); _searched = true; }
                return _lib;
            }
        }

        static GameAudio Inst
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("[GameAudio]");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<GameAudio>();
                }
                return _inst;
            }
        }

        public static AudioSource Make3D(Transform parent, AudioClip clip, bool loop, float maxDistance)
        {
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0.3f;
            return src;
        }

        public static void PlayAt(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("Sfx");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 5f;
            src.maxDistance = 120f;
            src.volume = Mathf.Clamp01(volume * GameSettings.SfxVolume);
            src.pitch = pitch;
            src.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
        }

        static AudioClip Pick(AudioClip[] clips) => clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;

        /// <summary>Strzał (jedna salwa, także śrutówki z wieloma kulami, nie gra N razy).</summary>
        public static void Shot(Vector3 pos)
        {
            var lib = Library;
            if (lib == null || Time.time - _lastShot < 0.02f) return;
            _lastShot = Time.time;
            PlayAt(Pick(lib.gunShots), pos, 1f, Random.Range(0.94f, 1.06f));
            if (lib.gunTails != null && lib.gunTails.Length > 0) PlayAt(Pick(lib.gunTails), pos, 0.45f, 1f);
        }

        public static void Ricochet(Vector3 pos)
        {
            var lib = Library;
            if (lib != null) PlayAt(Pick(lib.ricochets), pos, 0.5f, Random.Range(0.9f, 1.1f));
        }

        /// <summary>Przeładowanie: wyjęcie magazynka, po chwili włożenie.</summary>
        public static void Reload(Vector3 pos, float duration)
        {
            var lib = Library;
            if (lib == null) return;
            PlayAt(lib.reloadOut, pos, 0.8f);
            Inst.StartCoroutine(DelayedPlay(lib.reloadIn, pos, Mathf.Max(0.3f, duration * 0.6f)));
        }

        static IEnumerator DelayedPlay(AudioClip clip, Vector3 pos, float delay)
        {
            yield return new WaitForSeconds(delay);
            PlayAt(clip, pos, 0.8f);
        }
    }
}
