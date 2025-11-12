/* This script manages a "VisualSlot" that owns a child anchor named "Visual" and ensures exactly one visual child is spawned there.
 * It safely handles play mode vs. edit mode, prefab stage protections, optional Inspector locking, and selection bouncing in the Editor.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement; // for PrefabStageUtility
#endif

[SelectionBase] //Makes this GameObject the preferred selection when clicking any of its children in the Scene view.
public class VisualSlot : MonoBehaviour //Defines the VisualSlot component that controls a spawned visual child.
{
    [HideInInspector] public Transform anchor;   //Hidden reference to the "Visual" anchor Transform used as the parent for the visual.
    public GameObject visualPrefab;              //Prefab to spawn under the anchor as the visual child.

    [Tooltip("When ON (Editor only), the Visual anchor and its spawned child are non-editable in the Inspector. Selection of them bounces to this parent.")] //Explains the lock behavior in the Inspector.
    public bool lockEditorSelection = true; //When true, the anchor and instance are not editable and selection bounces to this object.

    [SerializeField, HideInInspector] private GameObject _instance; //Stored reference to the currently spawned/kept visual instance.

#if UNITY_EDITOR
    bool _needsApply; //Editor-only flag to defer Apply execution safely on the next delay call.
#endif

    void Reset() //Called when the component is first added or reset via the Inspector context menu.
    {
        EnsureAnchor(); //Creates or finds the "Visual" anchor under this object.
#if UNITY_EDITOR
        ApplyEditorLockState(); //Applies Inspector lock flags to anchor/instance in the Editor.
#endif
    }

    void OnEnable() //Called when the component or GameObject becomes enabled/active.
    {
        EnsureAnchor(); //Make sure the anchor exists whenever we enable.
#if UNITY_EDITOR
        EditorApplication.delayCall -= EditorSafeApply; //Avoid duplicate scheduled calls by removing first.
        EditorApplication.delayCall -= ApplyEditorLockState; //Avoid duplicate scheduled calls for lock state.

        ApplyEditorLockState(); //Apply the current lock state immediately.
        VisualSlotEditorBridge.Register(this); //Register with the editor bridge so selection bouncing can work.
#endif
    }

    void OnDisable() //Called when the component or GameObject becomes disabled/inactive.
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= EditorSafeApply; //Remove any pending deferred apply.
        EditorApplication.delayCall -= ApplyEditorLockState; //Remove any pending deferred lock application.
        VisualSlotEditorBridge.Unregister(this); //Unregister from the editor bridge on disable.
#endif
    }

    void OnDestroy() //Called when the component or GameObject is about to be destroyed.
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= EditorSafeApply; //Clean up pending calls to avoid touching destroyed objects.
        EditorApplication.delayCall -= ApplyEditorLockState; //Clean up pending calls for lock state.
        VisualSlotEditorBridge.Unregister(this); //Unregister from the editor bridge on destroy.
#endif
    }

    void Awake() //Called when the script instance is being loaded.
    {
        EnsureAnchor(); //Ensure the "Visual" anchor exists as early as possible.

        // Adopt existing child if present (prevents dupes on Play)
        if (_instance == null && anchor != null && anchor.childCount > 0) //If we don't have an instance but anchor already has children…
        {
            for (int i = 0; i < anchor.childCount; i++) //Scan all anchor children for an active one to adopt.
            {
                var c = anchor.GetChild(i); //Get the i-th child Transform.
                if (c && c.gameObject.activeSelf) //If the child exists and is active…
                {
                    _instance = c.gameObject; //Adopt it as our current instance.
                    break; //Stop after adopting the first active child.
                }
            }
        }

        Apply(); //Apply the prefab/instance rules depending on play/edit mode.
#if UNITY_EDITOR
        ApplyEditorLockState(); //Ensure editor lock flags match current setting.
#endif
    }

#if UNITY_EDITOR
    void OnValidate() //Called in the Editor when values change in the Inspector.
    {
        if (Application.isPlaying) return; //Ignore validation while in play mode.
        if (IsEditingPrefabAssetOrInPrefabStage(gameObject)) return; //Do not modify prefab assets or contents in Prefab Mode.

        _needsApply = true; //Mark that we need to Apply on the next safe tick.

        EditorApplication.delayCall -= EditorSafeApply; //Avoid duplicate scheduled apply.
        EditorApplication.delayCall += EditorSafeApply; //Defer Apply to a safe time.

        EditorApplication.delayCall -= ApplyEditorLockState; //Avoid duplicate lock scheduling.
        EditorApplication.delayCall += ApplyEditorLockState; //Defer lock state application to a safe time.
    }

    void EditorSafeApply() //Deferred apply that runs safely after current editor operations complete.
    {
        if (!SafeInstanceExists(this)) return; //Ensure the object still exists.
        if (IsEditingPrefabAssetOrInPrefabStage(gameObject)) return; //Skip in prefab asset/Prefab Mode.
        if (!_needsApply) return; //Only act if we were marked for apply.

        _needsApply = false; //Clear the pending flag.
        Apply(); //Apply instance/prefab rules.
        ApplyEditorLockState(); //Re-apply lock flags after changes.
    }
#endif

    [ContextMenu("Apply Visual Now")] //Adds a context menu item to run Apply() immediately.
    public void ApplyVisualNow() //Public method to apply visuals from the context menu.
    {
        Apply(); //Apply prefab/instance logic.
#if UNITY_EDITOR
        ApplyEditorLockState(); //Refresh editor lock flags afterward.
#endif
    }

    public void Apply() //Core method that ensures exactly one visual child and correct parenting/transform.
    {
        EnsureAnchor(); //Create or cache the anchor if needed.

#if UNITY_EDITOR
        // Never spawn/reparent when editing a prefab asset or in Prefab Mode contents
        if (!Application.isPlaying && IsEditingPrefabAssetOrInPrefabStage(gameObject)) //Protect against editing prefab assets or Prefab Mode contents.
            return; //Exit early in those cases.
#endif

        // Split runtime vs editor safely so editor-only methods are not referenced in player builds
        if (Application.isPlaying) //If running in play mode…
        {
            AdoptIfMissingRuntime(); //Adopt an existing anchor child as instance if missing.
        }
#if UNITY_EDITOR
        else //If in the Editor (not playing)…
        {
            EnsureSingleChildEditor(); //Ensure only one child exists and store it as _instance.
        }
#endif

        if (visualPrefab == null || anchor == null) //If no prefab or no anchor is available…
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) //Only in the Editor (not during play)…
                DestroyInstanceIfAny(); //Destroy any existing instance so we don't keep stale visuals.
#endif
            return; //Nothing else to do without both a prefab and an anchor.
        }

        if (Application.isPlaying) //Runtime path for spawning/parenting.
        {
            if (_instance == null) //If no instance has been created yet…
                CreateInstanceRuntime(); //Instantiate the visual at runtime.

            if (_instance != null) //If we have an instance…
            {
                var t = _instance.transform; //Cache the instance Transform.
                if (t.parent != anchor) t.SetParent(anchor, false); //Ensure it's parented to anchor without changing local transform.
                t.localPosition = Vector3.zero; //Reset local position.
                t.localRotation = Quaternion.identity; //Reset local rotation.
                t.localScale = Vector3.one; //Reset local scale.
            }
            return; //Done with runtime handling.
        }

#if UNITY_EDITOR
        if (NeedsReplaceEditor()) //If the existing instance doesn't match the assigned prefab…
        {
            DestroyInstanceIfAny(); //Remove the current instance.
            CreateInstanceEditor(); //Instantiate a new instance from the prefab under the anchor.
        }

        if (_instance != null) //If we have an instance in the Editor…
        {
            var t = _instance.transform; //Cache the instance Transform.
            if (t.parent != anchor) //If it is not already parented to the anchor…
            {
                if (!IsEditingPrefabAssetOrInPrefabStage(gameObject)) //Extra safety against touching prefab contents.
                    t.SetParent(anchor, false); //Parent to anchor while preserving local values.
            }
            t.localPosition = Vector3.zero; //Ensure zeroed local position.
            t.localRotation = Quaternion.identity; //Ensure identity local rotation.
            t.localScale = Vector3.one; //Ensure unit local scale.
        }
#endif
    }

    public GameObject GetInstance() => _instance; //Accessor to retrieve the current spawned/kept visual instance.

    // ---------------- internals ----------------

    void EnsureAnchor() //Creates or finds a child named "Visual" and stores its Transform as the anchor.
    {
        if (anchor != null) return; //If we already have an anchor, do nothing.

        var vis = transform.Find("Visual"); //Try to find an existing "Visual" child.
        if (vis == null) //If there isn't one…
        {
            var go = new GameObject("Visual"); //Create a new child GameObject named "Visual".
            go.transform.SetParent(transform, false); //Parent it directly under this object without changing its local transform.
            anchor = go.transform; //Store its Transform as the anchor.
        }
        else //If found…
        {
            anchor = vis as Transform; //Cache the existing "Visual" child's Transform as the anchor.
        }
    }

#if UNITY_EDITOR
    void EnsureSingleChildEditor() //Editor-only: keep at most one child under the anchor and set _instance to it.
    {
        if (anchor == null) return; //Nothing to do without an anchor.

        GameObject keep = null; //The child we will keep (first encountered or current _instance).

        if (_instance != null && _instance.transform && _instance.transform.parent == anchor) //If _instance is already a direct child…
            keep = _instance; //Prefer keeping that.

        for (int i = 0; i < anchor.childCount; i++) //Scan all anchor children.
        {
            var child = anchor.GetChild(i)?.gameObject; //Get the child GameObject.
            if (!child) continue; //Skip null entries (safety).

            if (keep == null) //If we haven't selected a keeper yet…
            {
                keep = child; //Keep the first valid child encountered.
                continue; //Move on to next child.
            }

            SafeDestroyEditor(child); //Destroy any extra children beyond the one we keep.
        }

        _instance = keep; //Record the kept child as the current instance.
    }
#endif

    void AdoptIfMissingRuntime() //Runtime: if no _instance is recorded, adopt the first active child under anchor.
    {
        if (_instance != null || anchor == null) return; //Only adopt when missing and we have an anchor.

        for (int i = 0; i < anchor.childCount; i++) //Scan all anchor children.
        {
            var c = anchor.GetChild(i); //Get the i-th child.
            if (c && c.gameObject.activeSelf) //If it's a valid active GameObject…
            {
                _instance = c.gameObject; //Adopt it as our instance.
                break; //Stop after adopting one.
            }
        }
    }

#if UNITY_EDITOR
    void DestroyInstanceIfAny() //Editor-only: destroys the current instance via Undo so it can be undone.
    {
        if (_instance == null) return; //Nothing to destroy if we don't have an instance.
        SafeDestroyEditor(_instance); //Destroy with Undo support.
        _instance = null; //Clear the stored reference.
    }

    void CreateInstanceEditor() //Editor-only: instantiate the prefab under the anchor with Undo support.
    {
        if (visualPrefab == null || anchor == null) return; //Need both prefab and anchor to instantiate.
        if (IsEditingPrefabAssetOrInPrefabStage(gameObject)) return; //Do not instantiate inside prefab assets or Prefab Mode.

        _instance = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, anchor); //Instantiate prefab as an instance parented to anchor.
        if (_instance != null) //If instantiation succeeded…
        {
            var t = _instance.transform; //Cache its Transform.
            t.localPosition = Vector3.zero; //Zero local position.
            t.localRotation = Quaternion.identity; //Identity local rotation.
            t.localScale = Vector3.one; //Unit local scale.
            Undo.RegisterCreatedObjectUndo(_instance, "Instantiate Visual"); //Register for undo operations.
        }
    }
#endif

    void CreateInstanceRuntime() //Runtime: instantiate the prefab as a child of the anchor.
    {
        if (visualPrefab == null || anchor == null) return; //Require both prefab and anchor.

#if UNITY_EDITOR
        // If somehow running in editor with a persistent anchor (Prefab Mode), skip.
        if (IsEditingPrefabAssetOrInPrefabStage(gameObject)) return; //Protect against Prefab Mode content modifications at runtime in Editor.
#endif

        _instance = Instantiate(visualPrefab, anchor, false); //Instantiate prefab as a child of anchor (preserve local transform).
        var tr = _instance.transform; //Cache Transform for quick setup.
        tr.localPosition = Vector3.zero; //Zero local position.
        tr.localRotation = Quaternion.identity; //Identity local rotation.
        tr.localScale = Vector3.one; //Unit local scale.
    }

#if UNITY_EDITOR
    bool NeedsReplaceEditor() //Editor-only: determines if the current instance doesn't match the assigned prefab.
    {
        if (_instance == null) return true; //If there is no instance, we need one.
        if (visualPrefab == null) return false; //If no prefab is assigned, we keep whatever exists.

        var src = PrefabUtility.GetCorrespondingObjectFromSource(_instance); //Get the source prefab of the instance if any.
        if (src != null) return src != visualPrefab; //If it has a source, require it to match the assigned prefab.

        var a = _instance.name; //Fallback: compare by name when not a prefab instance.
        if (a.EndsWith("(Clone)")) a = a.Substring(0, a.Length - "(Clone)".Length); //Strip "(Clone)" from name.
        return a != visualPrefab.name; //Replace if the names don't match.
    }

    void SafeDestroyEditor(GameObject go) //Editor-only: destroys an object immediately with Undo.
    {
        if (!go) return; //Safety check for null.
        Undo.DestroyObjectImmediate(go); //Destroy with Undo support so it can be reverted.
    }

    // --------- Editor lock helpers ---------
    void SetHideFlagsRecursive(GameObject go, HideFlags flags) //Applies HideFlags recursively down a hierarchy.
    {
        if (!SafeInstanceExists(this)) return; //Ensure this component still exists.
        if (!go) return; //Skip if the target object is null.

        go.hideFlags = flags; //Apply the requested hide flags to this object.
        foreach (Transform c in go.transform) //Iterate over all children recursively.
            SetHideFlagsRecursive(c.gameObject, flags); //Apply the same flags to each child.
        EditorUtility.SetDirty(go); //Mark the object dirty so Unity saves the modified flags.
    }

    void ApplyEditorLockState() //Sets Inspector/selection lock behavior on anchor and instance based on lockEditorSelection.
    {
        if (!SafeInstanceExists(this)) return; //Do nothing if the component is gone.
        if (EditorApplication.isCompiling) return; //Skip while scripts are compiling.
        if (Application.isPlaying) return; //Skip in play mode (runtime never needs these flags).
        if (!gameObject.scene.IsValid()) return; // don’t touch prefab assets //Skip if this is a prefab asset (not in a scene).

        // Visible & pickable; just non-editable in Inspector when locked.
        var lockFlags = lockEditorSelection
            ? (HideFlags.NotEditable | HideFlags.HideInInspector) //When locked, hide in Inspector and make not editable.
            : HideFlags.None; //When unlocked, show normally.

        if (anchor)
            SetHideFlagsRecursive(anchor.gameObject, lockFlags); //Apply flags recursively to the anchor hierarchy.

        if (_instance)
            SetHideFlagsRecursive(_instance, lockFlags); //Apply flags recursively to the instance hierarchy.
    }

    static bool SafeInstanceExists(VisualSlot slot) //Robust null/Destroyed check for Unity objects.
    {
        return !(slot == null) && (object)slot != null && slot && slot.gameObject != null; //Verifies C# and Unity null semantics.
    }

    static bool IsEditingPrefabAssetOrInPrefabStage(GameObject go) //Detects prefab asset editing or Prefab Mode contents.
    {
        // Not in a scene? It's a prefab asset.
        if (!go.scene.IsValid()) return true; //If not part of a valid scene, it's a prefab asset.

        // In Prefab Mode (editing contents)?
#if UNITY_2018_3_OR_NEWER
        var stage = PrefabStageUtility.GetCurrentPrefabStage(); //Get the current prefab stage (if any).
        if (stage != null && stage.IsPartOfPrefabContents(go)) return true; //Return true when editing prefab contents.

        // Experimental fallback for some editor versions/projects
        var expStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage(); //Fallback stage retrieval.
        if (expStage != null && expStage.IsPartOfPrefabContents(go)) return true; //Also treat as prefab contents.
#endif
        return false; //Otherwise, this is a regular scene object.
    }
#endif
}

#if UNITY_EDITOR
// --------- INLINE EDITOR BRIDGE (Editor-only, same file) ---------
static class VisualSlotEditorBridge //Static helper used only in the Editor to manage selection bouncing and cleanup.
{
    static readonly HashSet<VisualSlot> Slots = new HashSet<VisualSlot>(); //Tracks all active VisualSlot instances.

    static VisualSlotEditorBridge() //Static constructor registers global editor callbacks.
    {
        Selection.selectionChanged += OnSelectionChanged; //Listen for selection changes to bounce selection if needed.

        EditorApplication.update += () => //Each editor update, prune destroyed entries.
        {
            Slots.RemoveWhere(s => s == null || (object)s == null || !s || s.gameObject == null); //Remove invalid/destroyed slots.
        };
    }

    public static void Register(VisualSlot slot) //Adds a slot to the tracked set.
    {
        if (slot) Slots.Add(slot); //Only add non-null references.
    }

    public static void Unregister(VisualSlot slot) //Removes a slot from the tracked set.
    {
        if (slot) Slots.Remove(slot); //Remove if present.
    }

    static void OnSelectionChanged() //Called whenever the editor selection changes.
    {
        var t = Selection.activeTransform; //Current active selection Transform.
        if (!t) return; //If nothing is selected, do nothing.

        foreach (var slot in Slots) //Iterate through all tracked slots.
        {
            if (slot == null || (object)slot == null || !slot || slot.gameObject == null) continue; //Skip invalid entries.
            if (!slot.lockEditorSelection) continue; //Skip if the slot doesn't want to lock selection.

            var anchor = slot.anchor; //Cache the slot's anchor.
            var inst = slot.GetInstance() ? slot.GetInstance().transform : null; //Cache the instance Transform if it exists.

            // If selection is the anchor/instance or any of their descendants, bounce to the parent.
            if ((anchor && (t == anchor || IsDescendantOf(t, anchor))) || //Selection under anchor?
                (inst && (t == inst || IsDescendantOf(t, inst)))) //Or under instance?
            {
                Selection.activeTransform = slot.transform; //Bounce selection to the slot's parent object.
                EditorGUIUtility.PingObject(slot.gameObject); //Ping the object in the Hierarchy for user feedback.
                break; //Stop after first handled case.
            }
        }
    }

    static bool IsDescendantOf(Transform child, Transform potentialParent) //Returns true if child is under potentialParent.
    {
        if (!child || !potentialParent) return false; //Invalid inputs cannot be descendants.
        var p = child.parent; //Start with the child's parent.
        while (p) //Climb up the hierarchy.
        {
            if (p == potentialParent) return true; //Found the ancestor—return true.
            p = p.parent; //Continue climbing.
        }
        return false; //No match found—return false.
    }
}
#endif
