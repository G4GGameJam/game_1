/* SpawnObjectEditor.cs
 * Custom inspector for SpawnObject.
 * - One big button that shows only the current mode text
 * - Switching the mode updates which fields are visible
 * - Shows the usual interaction + mode-specific fields
 * - Audio section shows ONLY the AudioClip field (no other audio controls)
 */

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SpawnObject))]
public class SpawnObjectEditor : Editor
{
    // Serialized properties
    SerializedProperty modeProp;
    SerializedProperty interactKeyProp, interactDistanceProp;
    SerializedProperty objectToActivateProp, activateOnlyOnceProp;
    SerializedProperty prefabToSpawnProp, spawnAtProp, maxSpawnsProp;

    // Audio
    SerializedProperty interactSfxProp;

    void OnEnable()
    {
        modeProp = SafeFind(nameof(SpawnObject.mode));

        interactKeyProp = SafeFind(nameof(SpawnObject.interactKey));
        interactDistanceProp = SafeFind(nameof(SpawnObject.interactDistance));

        objectToActivateProp = SafeFind(nameof(SpawnObject.objectToActivate));
        activateOnlyOnceProp = SafeFind(nameof(SpawnObject.activateOnlyOnce));

        prefabToSpawnProp = SafeFind(nameof(SpawnObject.prefabToSpawn));
        spawnAtProp = SafeFind(nameof(SpawnObject.spawnAt));
        maxSpawnsProp = SafeFind(nameof(SpawnObject.maxSpawns));

        interactSfxProp = SafeFind(nameof(SpawnObject.interactSfx));
    }

    SerializedProperty SafeFind(string propertyName)
    {
        var p = serializedObject.FindProperty(propertyName);
        if (p == null)
        {
            Debug.LogError($"[SpawnObjectEditor] Missing serialized field '{propertyName}'. " +
                           $"Make sure SpawnObject has a matching public or [SerializeField] member.");
        }
        return p;
    }

    bool DrawIfValid(SerializedProperty prop, GUIContent label = null)
    {
        if (prop == null) return false;
        if (label != null) EditorGUILayout.PropertyField(prop, label);
        else EditorGUILayout.PropertyField(prop);
        return true;
    }

    public override void OnInspectorGUI()
    {
        if (serializedObject == null)
        {
            base.OnInspectorGUI();
            return;
        }

        serializedObject.Update();

        // Short help text at the top
        EditorGUILayout.HelpBox(
            "Use the button to switch modes.\n" +
            "- Activate Existing turns on a scene object.\n" +
            "- Spawn Prefab instantiates a prefab at a chosen spawn point.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        // Mode toggle button with exact wording you wanted
        var currentMode = (SpawnObject.ActionMode)modeProp.enumValueIndex;
        string label = "Current Action Mode: " +
                       (currentMode == SpawnObject.ActionMode.ActivateExisting
                            ? "Activate Existing"
                            : "Spawn Prefab");

        if (GUILayout.Button(label, GUILayout.Height(30)))
        {
            Undo.RecordObject(target, "Toggle Action Mode");
            var next = currentMode == SpawnObject.ActionMode.ActivateExisting
                ? SpawnObject.ActionMode.SpawnPrefab
                : SpawnObject.ActionMode.ActivateExisting;
            modeProp.enumValueIndex = (int)next;
            currentMode = next;
        }

        EditorGUILayout.Space(8);

        // Interaction
        EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
        DrawIfValid(interactKeyProp);
        DrawIfValid(interactDistanceProp);

        EditorGUILayout.Space(6);

        // Mode-specific fields
        if (currentMode == SpawnObject.ActionMode.ActivateExisting)
        {
            EditorGUILayout.LabelField("Activate", EditorStyles.boldLabel);
            DrawIfValid(objectToActivateProp);
            DrawIfValid(activateOnlyOnceProp);
        }
        else
        {
            EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
            DrawIfValid(prefabToSpawnProp);
            DrawIfValid(spawnAtProp);
            DrawIfValid(maxSpawnsProp);
        }

        EditorGUILayout.Space(10);

        // Audio (only the AudioClip)
        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        DrawIfValid(interactSfxProp, new GUIContent("Interact SFX"));

        serializedObject.ApplyModifiedProperties();
    }
}
