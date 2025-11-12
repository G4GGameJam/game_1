/* This script auto-sizes a BoxCollider trigger to tightly wrap the renderers under a VisualSlot's anchor, accounting for rotation and scale, and includes an editor button to refit in the Inspector. */

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(BoxCollider))] //Forces a BoxCollider component to exist on this GameObject.
public class AutoFitTrigger : MonoBehaviour //Defines the AutoFitTrigger component that runs on a GameObject.
{
    public VisualSlot visualSlot; //Reference to the VisualSlot that owns/parents the visual meshes to fit around.
    [Tooltip("Extra space (world units) to add around the fitted collider.")] //Shows a help tip in the Inspector for padding.
    public Vector3 padding = new Vector3(0.02f, 0.02f, 0.02f); //Small extra space added beyond the exact fit.

    void Reset() //Unity callback that runs when the component is first added or reset in the Inspector.
    {
        var box = GetComponent<BoxCollider>(); //Gets the BoxCollider on this GameObject.
        box.isTrigger = true; //Ensures the BoxCollider works as a trigger (no physical collision).
    }

    void Awake() => Refit(); //On scene load, immediately compute and apply the fitted collider.

    [ContextMenu("Refit Now")] //Adds a right-click context menu item on the component to run Refit manually.
    public void Refit() //Calculates and applies a best-fit BoxCollider in local space that wraps the visuals.
    {
        var box = GetComponent<BoxCollider>(); //Gets the BoxCollider to modify.
        if (!box) return; //Exits if no collider exists (safety).
        if (visualSlot == null || visualSlot.anchor == null) return; //Exits if the VisualSlot or its anchor is missing.

        var renderers = visualSlot.anchor.GetComponentsInChildren<Renderer>(includeInactive: true); //Collects all Renderer components under the anchor, including inactive ones.
        if (renderers.Length == 0) return; //Exits if there are no renderers to fit around.

        // World-space AABB around all visuals
        Bounds worldB = renderers[0].bounds; //Starts an axis-aligned bounding box in world space using the first renderer.
        for (int i = 1; i < renderers.Length; i++) //Loops through remaining renderers.
            worldB.Encapsulate(renderers[i].bounds); //Expands the world AABB to include each renderer's bounds.

        // Convert world AABB -> local OBB aligned to this transform's local axes
        Bounds localAabb = WorldBoundsToLocalAabb(worldB, transform); //Converts world-space bounds to a local-space AABB aligned to this object.

        // Convert world padding to local-space padding using lossy scale magnitude per axis
        var s = transform.lossyScale; //Reads this transform's world scale to adjust padding correctly per axis.
        Vector3 padLocal = new Vector3( //Creates a local-space padding vector derived from world padding.
            Mathf.Approximately(Mathf.Abs(s.x), 0f) ? padding.x : padding.x / Mathf.Abs(s.x), //Avoids divide-by-zero; scales padding by X scale.
            Mathf.Approximately(Mathf.Abs(s.y), 0f) ? padding.y : padding.y / Mathf.Abs(s.y), //Avoids divide-by-zero; scales padding by Y scale.
            Mathf.Approximately(Mathf.Abs(s.z), 0f) ? padding.z : padding.z / Mathf.Abs(s.z)  //Avoids divide-by-zero; scales padding by Z scale.
        );

        // Apply padding by increasing size and keeping center
        Vector3 sizeLocal = localAabb.size + padLocal; //Adds local padding to the calculated local AABB size.
        box.center = localAabb.center; //Sets the BoxCollider center to the local AABB center.
        box.size = sizeLocal; //Sets the BoxCollider size to the padded local AABB size.
    }

    /// <summary>
    /// Take a world-space AABB and return the local-space AABB after transforming
    /// all 8 corners into 'localSpace'. This correctly accounts for rotation and scale.
    /// </summary>
    static Bounds WorldBoundsToLocalAabb(Bounds worldB, Transform localSpace) //Utility that converts a world AABB to a local-space AABB by transforming all corners.
    {
        // 8 corners of the world-space AABB
        Vector3 c = worldB.center; //Stores the world-space center of the bounds.
        Vector3 e = worldB.extents; //Stores half-sizes (extents) of the bounds along each axis.

        Vector3[] worldCorners = new Vector3[8] //Allocates an array for the 8 corner points of the world AABB.
        {
            new Vector3(c.x - e.x, c.y - e.y, c.z - e.z), //Corner 0: (-x, -y, -z) from center.
            new Vector3(c.x + e.x, c.y - e.y, c.z - e.z), //Corner 1: (+x, -y, -z).
            new Vector3(c.x - e.x, c.y + e.y, c.z - e.z), //Corner 2: (-x, +y, -z).
            new Vector3(c.x + e.x, c.y + e.y, c.z - e.z), //Corner 3: (+x, +y, -z).
            new Vector3(c.x - e.x, c.y - e.y, c.z + e.z), //Corner 4: (-x, -y, +z).
            new Vector3(c.x + e.x, c.y - e.y, c.z + e.z), //Corner 5: (+x, -y, +z).
            new Vector3(c.x - e.x, c.y + e.y, c.z + e.z), //Corner 6: (-x, +y, +z).
            new Vector3(c.x + e.x, c.y + e.y, c.z + e.z)  //Corner 7: (+x, +y, +z).
        };

        // Transform corners into local space and encapsulate
        Vector3 p0 = localSpace.InverseTransformPoint(worldCorners[0]); //Transforms corner 0 into the target local space.
        Bounds localB = new Bounds(p0, Vector3.zero); //Initializes a local-space bounds at the first point with zero size.
        for (int i = 1; i < 8; i++) //Loops through the remaining 7 corners.
        {
            Vector3 p = localSpace.InverseTransformPoint(worldCorners[i]); //Transforms the current corner into local space.
            localB.Encapsulate(p); //Expands the local bounds to include the current point.
        }

        return localB; //Returns the completed local-space AABB.
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(AutoFitTrigger))] //Registers a custom Inspector for AutoFitTrigger.
[CanEditMultipleObjects] //Allows editing multiple AutoFitTrigger instances at once.
public class AutoFitTriggerEditor : Editor //Defines the custom editor class.
{
    public override void OnInspectorGUI() //Overrides how the Inspector GUI is drawn.
    {
        DrawDefaultInspector(); //Draws the default Inspector fields for AutoFitTrigger.
        EditorGUILayout.Space(); //Adds spacing for visual separation.

        if (targets.Length > 1) //Checks if multiple objects are selected.
            EditorGUILayout.HelpBox($"Auto-fit {targets.Length} selected triggers.", MessageType.Info); //Displays an info box showing how many triggers can be auto-fit.

        if (GUILayout.Button("Auto-Fit Collider")) //Draws a button that triggers a refit on selection.
        {
            Undo.IncrementCurrentGroup(); //Starts a new Undo group for all changes from this click.
            int group = Undo.GetCurrentGroup(); //Stores the group ID so we can collapse it later.

            foreach (var obj in targets) //Iterates over all selected objects in the Inspector.
            {
                if (obj is AutoFitTrigger t) //Casts the object to AutoFitTrigger if possible.
                {
                    var box = t.GetComponent<BoxCollider>(); //Gets the BoxCollider on the selected object.
                    if (!box) continue; //Skips if there is no collider.

                    Undo.RecordObject(box, "Auto-Fit Collider"); //Records collider changes for Undo support.
                    t.Refit(); //Calls the runtime method to compute and apply the fitted collider.
                    EditorUtility.SetDirty(box); //Marks the collider as dirty so Unity saves the change.
                }
            }

            Undo.CollapseUndoOperations(group); //Collapses all recorded changes into a single Undo step.
        }
    }
}
#endif
