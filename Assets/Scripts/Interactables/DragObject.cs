/* This script allows the player to click and drag physics objects using the E key.
 * Includes improved raycasting, collision-safe dragging, and an Interact Audio Clip
 * that plays on the Player's AudioSource when starting/stopping drag.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1000)] //Forces this script to run after most others so cached references are ready.
[RequireComponent(typeof(Collider))] //Ensures the object has a Collider so it can be targeted by raycasts.
public class DragObject : MonoBehaviour //Defines a component that lets the player grab and drag this object.
{
    [Header("Interaction")] //Inspector header for interaction settings.
    public KeyCode interactKey = KeyCode.E; //Sets which key will start and stop dragging.
    public float interactDistance = 3f;     //Sets how close the player must be to interact.

    [Header("Audio")] //Inspector header for audio settings.
    [Tooltip("Interact Audio Clip played on the Player's AudioSource when E is pressed to start/stop dragging.")] //Hover help text for the clip.
    public AudioClip interactAudioClip; //Clip to play on drag start/stop via the Player's AudioSource.

    const float kDragSmoothing = 15f; //Controls how quickly the object lerps toward the target position while dragging.
    const float kCastPadding = 0.02f; //Extra radius added to collision probes to avoid clipping.
    const float kSkin = 0.01f; //Small inset to keep the object slightly away from hit surfaces.
    const int kResolveIters = 3; //How many times to iterate penetration resolution when intersecting colliders.
    const int kMaxSteps = 4; //How many sub-steps to take when moving toward the target to avoid tunneling.
    const float kGroundSnap = 0.001f; //Optional downward snap distance to rest flush on surfaces.

    // NEW: internal aim radius for improved ray
    const float kAimRadius = 0.15f; //Sphere cast radius to make targeting more forgiving than a thin ray.

    int _collisionMask; //Layer mask used for collision tests during dragging.

    Transform _cam; //Reference to the player camera for aiming and reach calculations.
    HighlightObject _highlight; //Optional outline/highlight component to show interactability.
    Rigidbody _rb; //Optional rigidbody for physics state changes during drag.
    RigidbodyConstraints _rbPrevConstraints; //Stores rigidbody constraints to restore after drag ends.
    bool _rbPrevKinematic; //Stores whether the rigidbody was kinematic before dragging.

    bool _isDragging; //Tracks whether the object is currently being dragged.
    bool _inSight; //Tracks whether the object is currently targeted by the player's aim.

    float _pickupDistanceFromCam; //Distance from the camera to maintain while dragging.
    Quaternion _rotAtStart; //Rotation captured at drag start to keep orientation fixed.

    Collider[] _selfColliders; //All colliders on this object and children (used to temporarily disable during probes).
    Collider _mainCol; //Primary collider used for penetration resolution sizing.

    static DragObject s_currentDrag; //Global reference to the currently dragged object (so only one can be dragged at a time).

    // audio cache
    AudioSource _playerAudio; //Cached Player AudioSource for playing interact sounds.
    bool _lookedForPlayerAudio; //Prevents repeated lookups for the Player AudioSource.
    bool _warnedNoPlayerAudio; //Prevents spamming the console with missing-audio warnings.

    void Awake() //Called when the object is initialized in the scene.
    {
        _cam = Camera.main ? Camera.main.transform : null; //Caches the main camera transform if it exists.
        _rb = GetComponent<Rigidbody>(); //Gets the rigidbody on this object (if any) to manage physics during drag.
        _mainCol = GetComponent<Collider>(); //Caches the main collider for bounds and penetration checks.
        _selfColliders = GetComponentsInChildren<Collider>(includeInactive: true); //Collects all child colliders for temporary disabling.
        _collisionMask = LayerMask.GetMask("Default"); //Sets the collision mask to the Default layer for environment checks.

        var rbs = GetComponentsInChildren<Rigidbody>(includeInactive: true); //Finds all child rigidbodies (including inactive).
        var prevKin = new bool[rbs.Length]; //Temp array to remember each child rigidbody's kinematic state.

        for (int i = 0; i < rbs.Length; i++) //Iterates all child rigidbodies.
        {
            prevKin[i] = rbs[i].isKinematic; //Stores original kinematic state.
            rbs[i].isKinematic = true; //Temporarily sets them kinematic to safely modify mesh colliders.
        }

        FixMeshColliders(); //Ensures any MeshColliders are convex so physics queries are safe.

        for (int i = 0; i < rbs.Length; i++) //Iterates again after adjustments.
            rbs[i].isKinematic = prevKin[i]; //Restores each child rigidbody's original kinematic state.
    }

    void Start() //Called on the first frame after Awake.
    {
        _highlight = GetComponentInChildren<HighlightObject>(includeInactive: true); //Caches optional highlight component if present.
        TryCachePlayerAudio(); //Attempts to find and cache the Player's AudioSource.
    }

    void TryCachePlayerAudio() //Locates the Player GameObject and caches its AudioSource.
    {
        if (_lookedForPlayerAudio) return; //Skips if we've already attempted to find it.
        _lookedForPlayerAudio = true; //Marks that we tried to find the audio source.

        var player = GameObject.FindGameObjectWithTag("Player"); //Searches the scene for an object tagged "Player".
        if (player) _playerAudio = player.GetComponent<AudioSource>(); //Caches its AudioSource if present.
    }

    void PlayInteractAudio() //Plays the interactAudioClip once on the Player's AudioSource (if available).
    {
        if (!interactAudioClip) return; //Does nothing if no clip is assigned.
        if (!_playerAudio) TryCachePlayerAudio(); //If we haven't cached the audio source, try again now.

        if (_playerAudio) //If we have a valid Player AudioSource…
        {
            _playerAudio.PlayOneShot(interactAudioClip); //Play the clip once without interrupting other sounds.
        }
        else if (!_warnedNoPlayerAudio) //If still no audio source and we haven't warned yet…
        {
            _warnedNoPlayerAudio = true; //Ensure we only warn once.
            Debug.LogWarning("[DragObject] Interact Audio Clip is set, but no 'Player' AudioSource was found (tag 'Player')."); //Useful warning for setup issues.
        }
    }

    void Update() //Runs every frame to handle aiming, highlighting, and drag input/state.
    {
        if (_cam == null) return; //If no camera is available, exit early.

        if (!_isDragging) //If we are not currently dragging this object…
        {
            if (s_currentDrag != null && s_currentDrag != this) //If another object is being dragged…
            {
                if (_highlight) _highlight.SetHighlight(false); //Ensure this object isn't highlighted.
                return; //Skip any interaction while another drag is active.
            }

            Ray ray = new Ray(_cam.position, _cam.forward); //Creates a ray from the camera forward to represent the player's aim.
            _inSight = false; //Reset sight flag each frame.

            // --- Improved raycasting: SphereCastAll + nearest hit that belongs to this object ---
            RaycastHit bestHit = default; //Stores the closest valid hit on this object.
            float bestDist = float.PositiveInfinity; //Tracks the nearest distance encountered.
            var hits = Physics.SphereCastAll(ray, kAimRadius, interactDistance, ~0, QueryTriggerInteraction.Collide); //Sphere cast to find nearby hits within reach.
            for (int i = 0; i < hits.Length; i++) //Loop through all hits.
            {
                var h = hits[i]; //Current hit data.
                if (!h.collider) continue; //Skip if no collider on this hit.
                var t = h.collider.transform; //Transform of the hit collider.
                bool hitThis = (t == transform) || t.IsChildOf(transform); //Check if the hit belongs to this object or its children.
                if (!hitThis) continue; //Ignore hits on other objects.
                if (h.distance < bestDist) //If this hit is closer than our current best…
                {
                    bestDist = h.distance; //Update the nearest distance.
                    bestHit = h; //Store this hit as the best candidate.
                }
            }

            if (bestDist < float.PositiveInfinity) //If we found a valid hit on this object within range…
            {
                _inSight = true; //Mark as currently targeted.
                if (_highlight) _highlight.SetHighlight(true); //Turn on highlight/outline.

                if (Input.GetKeyDown(interactKey)) //If the player pressed the interact key this frame…
                    BeginDrag(bestHit); //Begin dragging using the best hit information.
            }

            if (!_inSight && _highlight) _highlight.SetHighlight(false); //If not targeted, ensure highlight is off.
        }
        else //If we are currently dragging this object…
        {
            Vector3 desiredPos = _cam.position + _cam.forward * _pickupDistanceFromCam; //Compute the target position in front of the camera at pickup distance.
            Vector3 from = transform.position; //Current position before moving.
            Vector3 to = StepwiseConstrainedMove(from, desiredPos); //Compute a collision-safe target using stepwise movement.

            transform.position = Vector3.Lerp(from, to, 1f - Mathf.Exp(-kDragSmoothing * Time.deltaTime)); //Smoothly approach the safe target position.
            transform.rotation = _rotAtStart; //Lock rotation to the orientation captured at drag start.

            if (Input.GetKeyDown(interactKey)) //If the player pressed interact while dragging…
                EndDrag(); //Release the object.
        }
    }

    void BeginDrag(RaycastHit hit) //Starts dragging the object and sets up physics state.
    {
        if (s_currentDrag != null && s_currentDrag != this) return; //If something else is already dragged, ignore.
        s_currentDrag = this; //Claim the global drag slot.

        _isDragging = true; //Mark as dragging.

        _pickupDistanceFromCam = Vector3.Distance(_cam.position, transform.position); //Remember the distance to maintain from camera.
        _rotAtStart = transform.rotation; //Cache current rotation so we can keep it fixed.

        if (_rb) //If the object has a rigidbody…
        {
            _rbPrevKinematic = _rb.isKinematic; //Store previous kinematic state.
            _rbPrevConstraints = _rb.constraints; //Store previous constraint flags.

            if (!_rbPrevKinematic) //If it was dynamic…
            {
                _rb.velocity = Vector3.zero; //Stop linear motion.
                _rb.angularVelocity = Vector3.zero; //Stop rotational motion.
            }

            _rb.isKinematic = true; //Make kinematic so we can drive position manually.
            _rb.constraints = RigidbodyConstraints.FreezeRotation; //Prevent rotation changes while held.
        }

        if (_highlight) _highlight.SetHighlight(false); //Turn off highlight while dragging.

        // play on start
        PlayInteractAudio(); //Play the interact sound to signal drag begin.
    }

    void EndDrag() //Stops dragging and restores physics state.
    {
        _isDragging = false; //No longer dragging.

        if (_rb) //If a rigidbody exists…
        {
            _rb.isKinematic = _rbPrevKinematic; //Restore previous kinematic state.
            _rb.constraints = _rbPrevConstraints; //Restore previous constraints.

            if (!_rb.isKinematic) //If returned to dynamic…
            {
                _rb.velocity = Vector3.zero; //Clear residual linear velocity.
                _rb.angularVelocity = Vector3.zero; //Clear residual angular velocity.
            }
        }

        if (s_currentDrag == this) s_currentDrag = null; //Release the global drag slot if we own it.

        // play on release
        PlayInteractAudio(); //Play the interact sound to signal drag end.
    }

    Vector3 StepwiseConstrainedMove(Vector3 start, Vector3 target) //Moves toward target in small steps while avoiding collisions.
    {
        if (_mainCol == null) return target; //If no main collider, just move directly to target.

        Vector3 current = start; //Begin from the current position.
        Bounds b = _mainCol.bounds; //Get collider world-space bounds.
        float radius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) + kCastPadding; //Use the largest extent as a probe radius plus padding.

        Vector3 totalDelta = target - start; //Overall movement vector.
        float totalDist = totalDelta.magnitude; //Distance to move.
        if (totalDist <= Mathf.Epsilon) return start; //If negligible movement, keep current position.

        int steps = Mathf.Clamp(kMaxSteps, 1, 16); //Clamp number of sub-steps for stability.
        for (int s = 1; s <= steps; s++) //Iterate sub-steps from near to full distance.
        {
            float t = (float)s / steps; //Fractional progress for this step.
            Vector3 stepTarget = start + totalDelta * t; //Desired position at this sub-step.
            Vector3 dir = stepTarget - current; //Direction from current to desired sub-step.
            float dist = dir.magnitude; //Distance for this sub-step.
            if (dist <= Mathf.Epsilon) continue; //Skip if no distance to travel.
            dir /= dist; //Normalize direction.

            ToggleSelfColliders(false); //Disable own colliders to avoid self-hits.
            bool blocked = Physics.SphereCast(current, radius, dir, out RaycastHit hit, dist, _collisionMask, QueryTriggerInteraction.Ignore); //Probe along the step to see if something blocks us.
            ToggleSelfColliders(true); //Re-enable own colliders.

            if (blocked) //If movement is blocked this sub-step…
            {
                float allowed = Mathf.Max(0f, hit.distance - kSkin); //Compute how far we can move before the obstacle, minus skin.
                current += dir * allowed; //Advance to the allowed point.
                current = ResolvePenetration(current, transform.rotation, radius); //Resolve any small overlaps.
                break; //Stop further stepping once blocked.
            }
            else //If not blocked…
            {
                current = stepTarget; //Advance to the full sub-step target.
                current = ResolvePenetration(current, transform.rotation, radius); //Resolve any small overlaps at the new position.
            }
        }

        if (kGroundSnap > 0f) //Optionally snap down slightly to rest flush on the surface.
        {
            ToggleSelfColliders(false); //Disable own colliders for the probe.
            if (Physics.SphereCast(current + Vector3.up * kGroundSnap, radius, Vector3.down, out RaycastHit groundHit, 2f * kGroundSnap, _collisionMask, QueryTriggerInteraction.Ignore)) //Probe just below to find the supporting surface.
            {
                current = groundHit.point + groundHit.normal * (radius + kSkin); //Place the object flush above the ground with skin offset.
            }
            ToggleSelfColliders(true); //Re-enable own colliders.
        }

        return current; //Return the collision-safe position.
    }

    Vector3 ResolvePenetration(Vector3 candidatePosition, Quaternion rot, float radiusForProbe) //Pushes the object out of overlapping colliders.
    {
        ToggleSelfColliders(false); //Disable own colliders to avoid detecting self.
        Collider[] hits = Physics.OverlapSphere(candidatePosition, radiusForProbe, _collisionMask, QueryTriggerInteraction.Ignore); //Find colliders overlapping our probe sphere.
        ToggleSelfColliders(true); //Re-enable own colliders.

        if (hits == null || hits.Length == 0) return candidatePosition; //If nothing overlaps, return as-is.

        Vector3 resolved = candidatePosition; //Start from the candidate position.

        for (int iter = 0; iter < kResolveIters; iter++) //Iteratively resolve overlaps for robustness.
        {
            bool pushed = false; //Tracks if we moved during this iteration.

            for (int i = 0; i < hits.Length; i++) //Check each overlapping collider.
            {
                var other = hits[i]; //The other collider.
                if (!other || other.isTrigger) continue; //Ignore null or trigger colliders.

                Vector3 dir; //Direction to push out of overlap.
                float distance; //Penetration depth along that direction.

                if (Physics.ComputePenetration(
                    _mainCol, resolved, rot, //Our collider at the current resolved pose.
                    other, other.transform.position, other.transform.rotation, //Other collider's pose.
                    out dir, out distance)) //Outputs push direction and overlap distance.
                {
                    if (distance > 0f) //If we are actually intersecting…
                    {
                        resolved += dir * (distance + kSkin); //Move out along the push direction plus skin.
                        pushed = true; //Mark that we changed position.
                    }
                }
            }

            if (!pushed) break; //Stop early if no more penetration was found.
        }

        return resolved; //Return the adjusted non-overlapping position.
    }

    void ToggleSelfColliders(bool enable) //Enables or disables all colliders on this object and its children.
    {
        if (_selfColliders == null) return; //Nothing to toggle if the list is missing.
        for (int i = 0; i < _selfColliders.Length; i++) //Loop through collected colliders.
            if (_selfColliders[i]) _selfColliders[i].enabled = enable; //Set each collider's enabled state.
    }

    void FixMeshColliders() //Ensures all MeshColliders are convex to support proper physics queries.
    {
        var meshCols = GetComponentsInChildren<MeshCollider>(includeInactive: true); //Find all MeshColliders in children.
        for (int i = 0; i < meshCols.Length; i++) //Iterate each mesh collider.
        {
            var mc = meshCols[i]; //Current MeshCollider.
            if (!mc) continue; //Skip null entries (safety).
            mc.convex = true; //Force convex so sphere casts and penetration tests are supported.
        }
    }
}
