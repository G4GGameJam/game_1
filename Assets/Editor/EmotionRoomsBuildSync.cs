/* EmotionRoomsBuildSync.cs  (place in Assets/Editor/)
 * 
 * Keeps Build Settings in sync with project scenes for your flow:
 *   Title_Screen -> Emotion_Room -> Emotion_Room_2 -> Emotion_Room_3 -> ...
 * 
 * What it does:
 * - Finds all scenes (t:Scene) in the project.
 * - Identifies Title_Screen and any Emotion_Room* scenes.
 * - Sorts Emotion_Room scenes so base comes first, then numeric order.
 * - Preserves any OTHER scenes already enabled in Build Settings.
 * - Writes the final ordered list back to EditorBuildSettings.scenes.
 * 
 * When it runs:
 * - Automatically on script reload (domain reload).
 * - Automatically whenever any scene asset is imported/deleted/moved/renamed.
 * - Manually via menu: Tools/Sync Emotion Rooms to Build Settings
 */

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

internal static class EmotionRoomsBuildSync
{
    private const string TitleSceneName = "Title_Screen";
    private static readonly Regex kRoomRegex = new Regex(@"^Emotion_Room(?:_(\d+))?$");

    // ---------- Manual trigger via menu ----------
    [MenuItem("Tools/Sync Emotion Rooms to Build Settings")]
    public static void SyncNow()
    {
        SyncBuildSettings();
    }

    // ---------- Auto-run on domain reload ----------
    [InitializeOnLoadMethod]
    private static void OnLoad()
    {
        // DelayCall ensures it runs after Unity finishes current reload cycle.
        EditorApplication.delayCall += SyncBuildSettings;
    }

    // ---------- Auto-run when scene assets change ----------
    private class RoomsAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool sceneChanged =
                importedAssets.Any(p => p.EndsWith(".unity")) ||
                deletedAssets.Any(p => p.EndsWith(".unity")) ||
                movedAssets.Any(p => p.EndsWith(".unity")) ||
                movedFromAssetPaths.Any(p => p.EndsWith(".unity"));

            if (sceneChanged)
                EditorApplication.delayCall += SyncBuildSettings;
        }
    }

    // ---------- Ordering helper: base room first, then _2, _3, ... ----------
    private static int ExtractRoomOrder(string sceneName)
    {
        var m = kRoomRegex.Match(sceneName);
        if (!m.Success) return int.MaxValue;
        if (!m.Groups[1].Success) return 1; // base room
        if (int.TryParse(m.Groups[1].Value, out int n)) return n;
        return int.MaxValue;
    }

    // ---------- Core sync logic ----------
    private static void SyncBuildSettings()
    {
        // Gather all scene assets in the project
        var guids = AssetDatabase.FindAssets("t:Scene");
        var allScenePaths = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".unity"))
            .ToList();

        // Partition: Title_Screen, Emotion_Room*, other scenes to preserve if already enabled
        var emotionRooms = new List<string>();
        string titlePath = null;
        var othersToPreserve = new HashSet<string>();

        foreach (var path in allScenePaths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name == TitleSceneName) { titlePath = path; continue; }
            if (kRoomRegex.IsMatch(name)) { emotionRooms.Add(path); continue; }
        }

        // Sort rooms by numeric suffix, base first
        emotionRooms = emotionRooms
            .OrderBy(p => ExtractRoomOrder(Path.GetFileNameWithoutExtension(p)))
            .ToList();

        // Preserve non-emotion, non-title scenes that are already enabled in Build Settings
        foreach (var ebs in EditorBuildSettings.scenes)
        {
            string name = Path.GetFileNameWithoutExtension(ebs.path);
            if (name != TitleSceneName && !kRoomRegex.IsMatch(name) && ebs.enabled)
                othersToPreserve.Add(ebs.path);
        }

        // Build final ordered list: Title (if present) -> Emotion rooms -> Preserved others
        var finalList = new List<EditorBuildSettingsScene>();

        if (!string.IsNullOrEmpty(titlePath))
            finalList.Add(new EditorBuildSettingsScene(titlePath, true));

        foreach (var p in emotionRooms)
            finalList.Add(new EditorBuildSettingsScene(p, true));

        foreach (var p in othersToPreserve)
            finalList.Add(new EditorBuildSettingsScene(p, true));

        // Only write back if something changed
        bool changed = !SequenceEqual(EditorBuildSettings.scenes, finalList);
        if (changed)
        {
            EditorBuildSettings.scenes = finalList.ToArray();
            Debug.Log($"[Build Sync] Build Settings updated. Total scenes: {finalList.Count}. Emotion rooms: {emotionRooms.Count}.");
        }
    }

    private static bool SequenceEqual(EditorBuildSettingsScene[] a, List<EditorBuildSettingsScene> b)
    {
        if (a.Length != b.Count) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].path != b[i].path) return false;
            if (a[i].enabled != b[i].enabled) return false;
        }
        return true;
    }
}
#endif
