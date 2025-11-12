#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class SignalEditorUtilities
{
    [MenuItem("Signals/Clear Editor Lines")]
    static void ClearEditorLines()
    {
        int n = 0;
        var all = Resources.FindObjectsOfTypeAll<LineRenderer>();
        foreach (var lr in all)
        {
            if (lr && lr.gameObject && lr.gameObject.name == "~SignalLine(EditorOnly)")
            {
                Object.DestroyImmediate(lr.gameObject);
                n++;
            }
        }

        Debug.Log($"[Signals] Cleared {n} editor-only line(s).");
    }
}
#endif
