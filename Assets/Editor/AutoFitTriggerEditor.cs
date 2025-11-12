#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AutoFitTrigger))]
[CanEditMultipleObjects]
public class AutoFitTriggerEditor : Editor
{
    SerializedProperty propVisualSlot;
    SerializedProperty propPadding; // we won't draw this

    void OnEnable()
    {
        propVisualSlot = serializedObject.FindProperty("visualSlot");
        propPadding = serializedObject.FindProperty("padding");
    }

    public override void OnInspectorGUI()
    {
        // Top help box
        EditorGUILayout.HelpBox(
            "Automatically size your object's BoxCollider to the bounds of the VisualSlot's renderer.\n" +
            "Use this after you select or change your Visual Slot object.",
            MessageType.Info
        );

        serializedObject.Update();

        // Draw ONLY the fields we want students to edit (padding intentionally hidden)
        EditorGUILayout.PropertyField(propVisualSlot);

        EditorGUILayout.Space();

        // Action buttons
        using (new EditorGUI.DisabledScope(targets == null || targets.Length == 0))
        {
            if (GUILayout.Button("Auto-Fit Collider"))
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();

                foreach (var obj in targets)
                {
                    if (obj is AutoFitTrigger t)
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (!box) continue;

                        Undo.RecordObject(box, "Auto-Fit Collider");
                        t.Refit();
                        EditorUtility.SetDirty(box);
                    }
                }

                Undo.CollapseUndoOperations(group);
            }
        }

        serializedObject.ApplyModifiedProperties();

        // Optional: gentle reminder if padding was changed via script/other tools
        if (propPadding != null && propPadding.hasMultipleDifferentValues)
            EditorGUILayout.HelpBox("Multiple different padding values exist across selections (hidden).", MessageType.None);
    }
}
#endif
