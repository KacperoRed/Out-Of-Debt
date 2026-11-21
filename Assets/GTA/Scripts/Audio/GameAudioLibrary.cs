using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Lista dźwięków gry (strzały, przeładowanie, silnik). Tworzy ją narzędzie MiniGTA → BUDUJ WSZYSTKO (krok „Dźwięki”)
    /// i zapisuje w Assets/Resources/GameAudio.asset. Bez niej gra działa po cichu.
    /// </summary>
    public class GameAudioLibrary : ScriptableObject
    {
        [Header("Broń (Weapons of Choice FREE)")]
        public AudioClip[] gunShots;
        public AudioClip[] gunTails;
        public AudioClip[] ricochets;
        public AudioClip[] shells;
        public AudioClip reloadOut, reloadIn, arming;

        [Header("Silnik (i6 German Free)")]
        public AudioClip engineStart, engineIdle, engineLow, engineMed, engineHigh, engineMax;
        public AudioClip engineLowOff, engineMedOff, engineHighOff;
    }
}
