// Pomocnik do budowania z linii poleceń: generuje miasto i buduje .exe jednym poleceniem.
// Unity.exe -batchmode -projectPath . -executeMethod MiniGTA.EditorTools.BatchAll.Run -quit
namespace MiniGTA.EditorTools
{
    public static class BatchAll
    {
        public static void Run()
        {
            GTASceneBuilder.BuildAllBatch();
            GTASceneBuilder.BuildWindows();
        }
    }
}
