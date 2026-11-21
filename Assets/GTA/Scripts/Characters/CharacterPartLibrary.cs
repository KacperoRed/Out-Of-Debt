using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Lista części postaci z paczki Creative Characters (twarze, fryzury, czapki, ubrania, buty…).
    /// Tworzy ją narzędzie MiniGTA → 6 i zapisuje w Assets/Resources/CharacterParts.asset, więc gra ładuje części przez Resources
    /// (bez żadnych ręcznych przypięć w scenie).
    /// </summary>
    public class CharacterPartLibrary : ScriptableObject
    {
        [Serializable]
        public class Category
        {
            public string id;
            public string label;
            public CharacterAppearance.Slot slot;
            [Tooltip("Kategoria jest ukrywana, gdy wybrano „strój cały” (kombinezon, kostium)")]
            public bool hiddenByFull;
            [Tooltip("Kategoria „strój cały” – zastępuje kurtkę, spodnie i skarpety")]
            public bool isFull;
            [Tooltip("Domyślny numer części dla gracza (−1 = brak)")]
            public int defaultIndex = -1;
            [Tooltip("Szansa (0–1), że losowy NPC ma tę część")]
            public float npcChance = 0.5f;
            public List<GameObject> parts = new List<GameObject>();
        }

        public GameObject bodyPrefab;
        public RuntimeAnimatorController controller;
        public Avatar avatar;
        [Tooltip("Skala dopasowująca model do ok. 1,8 m wzrostu")]
        public float scale = 1f;
        public float yOffset;
        public List<Category> categories = new List<Category>();

        static CharacterPartLibrary _instance;
        static bool _searched;

        /// <summary>Biblioteka z Resources albo null, gdy paczki postaci nie podłączono.</summary>
        public static CharacterPartLibrary Instance
        {
            get
            {
                if (!_searched) { _instance = Resources.Load<CharacterPartLibrary>("CharacterParts"); _searched = true; }
                return _instance;
            }
        }

        public Category Find(string id) => categories.Find(c => c.id == id);

        public static string PartName(GameObject part) => part == null ? "brak" : part.name.Replace('_', ' ');
    }
}
