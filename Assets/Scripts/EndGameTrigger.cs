/* This script ends the game when the Player enters a trigger volume, with extra reliability:
 * it listens to OnTriggerEnter and OnTriggerStay and also runs a FixedUpdate overlap
 * check to catch fast players or thin colliders. It can auto-add a kinematic Rigidbody
 * to improve trigger messaging and supports layer-filtered fallback checks.
 */

using UnityEngine;

[RequireComponent(typeof(Collider))] //Ensures a Collider component exists on the same GameObject.
public class EndGameTrigger : MonoBehaviour //Defines a trigger that signals Game Over when the player enters.
{
    [Header("Detection")] //Groups detection-related fields in the Inspector.
    [Tooltip("Tag used to detect the player object.")] //Explains how the player is identified.
    public string playerTag = "Player"; //Name of the tag used to identify the Player.

    [Tooltip("Add a kinematic Rigidbody to this trigger for more reliable physics messaging.")] //Describes why adding an RB helps.
    public bool addKinematicRigidbody = true; //If true, ensures a kinematic Rigidbody is present for stable trigger callbacks.

    [Tooltip("Run a physics overlap check in FixedUpdate as a safety net.")] //Explains the fallback overlap behavior.
    public bool enableFallbackOverlapCheck = true; //If true, performs an overlap probe each FixedUpdate to catch missed entries.

    [Tooltip("Layers to test in the fallback overlap. Leave empty to check all.")] //Explains the layer mask usage.
    public LayerMask fallbackLayerMask = ~0; // everything by default //Layer mask for the fallback overlap queries (default: all layers).

    Collider _col; //Cached reference to this object's Collider.
    GameManager _gm; //Cached reference to the GameManager in the scene.
    bool _fired; //Tracks whether we already triggered game over to avoid duplicates.

    void Awake() //Called when the component is initialized (before Start).
    {
        _col = GetComponent<Collider>(); //Fetch the Collider component required for trigger behavior.
        if (_col == null) //If somehow missing a collider (shouldn't happen due to RequireComponent)…
        {
            Debug.LogError("[EndGameTrigger] No Collider found."); //Log an error for debugging.
            enabled = false; //Disable this script since it can't function without a collider.
            return; //Exit early.
        }

        // Ensure it's a trigger.
        if (!_col.isTrigger) //If the collider is not set to trigger mode…
            _col.isTrigger = true; //Force it to be a trigger so it receives trigger events.

        // Optional kinematic rigidbody improves trigger reliability in some edge cases.
        if (addKinematicRigidbody) //If we want to ensure a kinematic Rigidbody is present…
        {
            var rb = GetComponent<Rigidbody>(); //Try to get an existing Rigidbody.
            if (!rb) //If none exists…
            {
                rb = gameObject.AddComponent<Rigidbody>(); //Add a new Rigidbody.
                rb.isKinematic = true; //Set it to kinematic so it doesn't simulate physically.
                rb.useGravity = false; //Disable gravity so it doesn't fall.
            }
            else //If one already exists…
            {
                rb.isKinematic = true; //Ensure it is kinematic for stable triggers.
                rb.useGravity = false; //Ensure gravity is off.
            }
        }

        _gm = FindObjectOfType<GameManager>(); //Find the GameManager in the scene to call TriggerGameOver.
        if (!_gm) //If the GameManager wasn't found…
        {
            Debug.LogWarning("[EndGameTrigger] No GameManager found in scene."); //Warn so the user can fix the scene setup.
        }
    }

    void OnEnable() //Called when the component is enabled.
    {
        _fired = false; //Reset the fired flag so this trigger can fire once per enable/scene load.
    }

    // Primary path
    void OnTriggerEnter(Collider other) //Called by Unity when another collider enters this trigger.
    {
        if (_fired) return; //Do nothing if we've already fired once.
        if (!other.CompareTag(playerTag)) return; //Ignore anything not tagged as the Player.
        Trigger(); //Trigger the end game sequence.
    }

    // Secondary path: if the engine missed the exact enter moment,
    // we'll still fire as soon as the player is reported inside.
    void OnTriggerStay(Collider other) //Called once per physics step for colliders staying inside this trigger.
    {
        if (_fired) return; //Skip if we've already fired.
        if (!other.CompareTag(playerTag)) return; //Only care about the Player.
        Trigger(); //Trigger the end game sequence (fallback if OnTriggerEnter was missed).
    }

    // Fallback: physics overlap check in FixedUpdate
    void FixedUpdate() //Called on a fixed timestep; good for physics overlap fallback checks.
    {
        if (_fired || !enableFallbackOverlapCheck) return; //Abort if already fired or fallback is disabled.
        if (!_col) return; //Safety check if collider reference is missing.

        // Perform an overlap that matches the collider type.
        Collider[] hits = null; //Array to receive the colliders overlapping our volume.

        if (_col is BoxCollider box) //If our trigger is a BoxCollider…
        {
            Vector3 worldCenter = box.transform.TransformPoint(box.center); //Convert the local center to world space.
            Vector3 worldHalfExtents = Vector3.Scale(box.size * 0.5f, box.transform.lossyScale); //Get half extents scaled by world scale.
            hits = Physics.OverlapBox(worldCenter, worldHalfExtents, box.transform.rotation, fallbackLayerMask, QueryTriggerInteraction.Collide); //Query overlapping colliders with rotation and mask.
        }
        else if (_col is SphereCollider sphere) //If our trigger is a SphereCollider…
        {
            Vector3 worldCenter = sphere.transform.TransformPoint(sphere.center); //Convert local center to world space.
            float maxScale = Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x), Mathf.Abs(sphere.transform.lossyScale.y), Mathf.Abs(sphere.transform.lossyScale.z)); //Find the largest scale axis.
            float worldRadius = sphere.radius * maxScale; //Compute world-space radius accounting for scale.
            hits = Physics.OverlapSphere(worldCenter, worldRadius, fallbackLayerMask, QueryTriggerInteraction.Collide); //Query overlapping colliders by sphere.
        }
        else if (_col is CapsuleCollider capsule) //If our trigger is a CapsuleCollider…
        {
            // Compute capsule endpoints in world space
            capsule.GetWorldCapsule(out Vector3 p0, out Vector3 p1, out float radius); //Use helper to get world endpoints and radius.
            hits = Physics.OverlapCapsule(p0, p1, radius, fallbackLayerMask, QueryTriggerInteraction.Collide); //Query overlapping colliders by capsule.
        }
        else if (_col is MeshCollider meshCol && meshCol.convex) //If it's a convex MeshCollider…
        {
            // Approximate convex mesh with its bounds (box overlap)
            var b = meshCol.bounds; //Get world-space AABB bounds as an approximation.
            hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity, fallbackLayerMask, QueryTriggerInteraction.Collide); //Query as a box overlap using bounds.
        }
        else //Generic fallback for other collider types.
        {
            // As a generic fallback, use bounds box
            var b = _col.bounds; //Use the collider's world-space AABB.
            hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity, fallbackLayerMask, QueryTriggerInteraction.Collide); //Query with a box overlap.
        }

        if (hits != null) //If we got any overlaps…
        {
            for (int i = 0; i < hits.Length; i++) //Iterate through all overlapping colliders.
            {
                if (hits[i] && hits[i].CompareTag(playerTag)) //If any overlapping collider is tagged Player…
                {
                    Trigger(); //Trigger the end game sequence.
                    break; //Stop checking further hits.
                }
            }
        }
    }

    void Trigger() //Centralized method to mark as fired and notify the GameManager.
    {
        if (_fired) return; //Avoid double-firing.
        _fired = true; //Mark as fired so subsequent entries do nothing.

        if (_gm) //If we found a GameManager…
        {
            _gm.TriggerGameOver(); //Show the End Game UI and handle next-room logic.
        }
        else //If no GameManager is present…
        {
            Debug.LogWarning("[EndGameTrigger] Triggered, but no GameManager found to handle Game Over."); //Warn so the user can fix the scene.
        }
    }
}

// -------- Helper extension for CapsuleCollider world endpoints --------
static class CapsuleColliderExtensions //Provides a utility to compute world-space capsule endpoints and radius.
{
    public static void GetWorldCapsule(this CapsuleCollider c, out Vector3 p0, out Vector3 p1, out float radius) //Extension method to get capsule endpoints/radius in world space.
    {
        var t = c.transform; //Cache transform for repeated use.
        Vector3 center = t.TransformPoint(c.center); //Convert local center to world space.

        // Determine the capsule axis in world space
        Vector3 axis; //Will hold the capsule's main axis in world space.
        switch (c.direction) //Choose axis based on the collider's direction setting.
        {
            case 0: axis = t.right; break; // X //Capsule aligned along local X axis.
            case 1: axis = t.up; break; // Y //Capsule aligned along local Y axis.
            default: axis = t.forward; break; // Z //Capsule aligned along local Z axis.
        }

        // Scale-aware radius & height
        Vector3 lossy = t.lossyScale; //Fetch world scaling factors of the transform.
        float maxAxisScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)); //Largest axis scale influences height.
        float radiusScale; //Will store the radius scale (largest of the two axes perpendicular to direction).
        if (c.direction == 0) radiusScale = Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)); //For X, radius uses Y/Z scales.
        else if (c.direction == 1) radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z)); //For Y, radius uses X/Z scales.
        else radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y)); //For Z, radius uses X/Y scales.

        radius = c.radius * radiusScale; //Compute world-space radius from local radius and scale.
        float height = Mathf.Max(c.height * maxAxisScale, radius * 2f); // ensure valid //Ensure height is at least diameter.

        float halfLine = (height * 0.5f) - radius; //Compute half of the straight middle segment (excluding hemispheres).
        halfLine = Mathf.Max(0f, halfLine); //Clamp to non-negative.

        p0 = center + axis * halfLine; //Compute one endpoint along the axis.
        p1 = center - axis * halfLine; //Compute the opposite endpoint along the axis.
    }
}
