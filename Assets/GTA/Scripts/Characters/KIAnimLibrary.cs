using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Zestaw modeli i animacji z paczki Kevin Iglesias „Human Animations”. Asset Resources/KIAnims.asset
    /// tworzy automatycznie skrypt edytora KIAnimLinker (nic nie trzeba podpinać ręcznie).
    /// </summary>
    public class KIAnimLibrary : ScriptableObject
    {
        public GameObject maleModel, femaleModel;
        public Avatar maleAvatar, femaleAvatar;
        public AnimationClip maleIdle, maleIdle2, maleTalk, maleWalk, maleRun;
        public AnimationClip femaleIdle, femaleIdle2, femaleTalk, femaleWalk, femaleRun;

        static KIAnimLibrary _inst; static bool _searched;
        public static KIAnimLibrary Get()
        {
            if (!_searched) { _inst = Resources.Load<KIAnimLibrary>("KIAnims"); _searched = true; }
            return _inst;
        }

        public bool Ready => maleModel != null && maleIdle != null && maleTalk != null;
    }
}
