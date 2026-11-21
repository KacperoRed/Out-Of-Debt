using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>Po każdym przeładowaniu skryptów dba o Resources/KIAnims.asset (modele i klipy z paczki Kevin Iglesias).</summary>
    [InitializeOnLoad]
    public static class KIAnimLinker
    {
        const string Root = "Assets/Kevin Iglesias/Human Animations/";
        const string AssetPath = "Assets/Resources/KIAnims.asset";

        static KIAnimLinker() { EditorApplication.delayCall += Run; }

        static AnimationClip Clip(string rel)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Root + rel))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
            return null;
        }

        static Avatar Av(string rel)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Root + rel)) if (o is Avatar a) return a;
            return null;
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Directory.Exists(Root.TrimEnd('/'))) return;
            var lib = AssetDatabase.LoadAssetAtPath<KIAnimLibrary>(AssetPath);
            bool isNew = lib == null;
            if (isNew) lib = ScriptableObject.CreateInstance<KIAnimLibrary>();

            var n = ScriptableObject.CreateInstance<KIAnimLibrary>();
            n.maleModel = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/HumanM_Model.fbx");
            n.femaleModel = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/HumanF_Model.fbx");
            n.maleAvatar = Av("Models/HumanM_Model.fbx");
            n.femaleAvatar = Av("Models/HumanF_Model.fbx");
            n.maleIdle = Clip("Animations/Male/Idles/HumanM@Idle01.fbx");
            n.maleIdle2 = Clip("Animations/Male/Idles/HumanM@Idle02.fbx");
            n.maleTalk = Clip("Animations/Male/Social/Conversation/HumanM@Talk01.fbx");
            n.maleWalk = Clip("Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx");
            n.maleRun = Clip("Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx");
            n.femaleIdle = Clip("Animations/Female/Idles/HumanF@Idle01.fbx");
            n.femaleIdle2 = Clip("Animations/Female/Idles/HumanF@Idle02.fbx");
            n.femaleTalk = Clip("Animations/Female/Social/Conversation/HumanF@Talk01.fbx");
            n.femaleWalk = Clip("Animations/Female/Movement/Walk/HumanF@Walk01_Forward.fbx");
            n.femaleRun = Clip("Animations/Female/Movement/Run/HumanF@Run01_Forward.fbx");

            bool changed = isNew;
            foreach (var f in typeof(KIAnimLibrary).GetFields())
            {
                var v = f.GetValue(n);
                if (!Equals(f.GetValue(lib), v)) { f.SetValue(lib, v); changed = true; }
            }
            Object.DestroyImmediate(n);
            if (!changed) return;

            Directory.CreateDirectory("Assets/Resources");
            if (isNew) AssetDatabase.CreateAsset(lib, AssetPath); else EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[MiniGTA] KIAnims.asset zaktualizowany (modele i animacje Kevin Iglesias).");
        }
    }
}
