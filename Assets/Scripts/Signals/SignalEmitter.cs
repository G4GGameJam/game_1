/* This script tracks multiple SignalEmitters and triggers a target once all have signaled at least once.
 * It also shows cyan connection lines between emitters and the target in the Editor for visualization.
 */

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SignalEmitter : MonoBehaviour //Component that emits a signal to any linked SignalTarget(s).
{
    [Tooltip("Targets to receive this emitter's signal (SignalReceiver or SignalCounter).")] //Inspector help text for targets.
    public List<SignalTarget> targets = new List<SignalTarget>(); //List of SignalTargets that will be notified when this emitter fires.

    [Header("Visualization")] //Groups editor-visualization options in the Inspector.
    [SerializeField] private bool showLinesInEditor = true; //When true, draws cyan connection lines in the Editor (not in play mode).
    [SerializeField] private Color idleLineColor = Color.cyan; //Color used for editor connection lines.

    // Internal: editor line thickness in pixels
    private const float lineThicknessPixels = 3f; //Pixel thickness of the editor lines.

    // ---------- Interaction ----------
    public void OnInteracted() //Call this to emit a signal to all targets (e.g., from another script or a trigger).
    {
        // Just emit. No runtime visuals.
        for (int i = targets.Count - 1; i >= 0; i--) //Iterate backwards so we can remove null entries safely.
        {
            var t = targets[i]; //Grab the current target reference.
            if (!t) { targets.RemoveAt(i); continue; } //If target is missing/destroyed, remove it and continue.
            t.OnSignal(this); //Notify the target that this emitter has fired (passes self as the source).
        }

#if UNITY_EDITOR
        SceneView.RepaintAll(); //Ask the Scene view to redraw so editor lines update immediately.
#endif
    }

    // ---------- Edit-Mode Gizmos ----------
#if UNITY_EDITOR
    private void OnDrawGizmos() //Draws editor-only connection lines when not playing.
    {
        if (!showLinesInEditor || Application.isPlaying || targets == null) return; //Skip if hidden, in play mode, or no targets.

        using (new Handles.DrawingScope(idleLineColor)) //Set line color for the drawing scope.
        {
            foreach (var t in targets) //Loop through all targets.
            {
                if (!t) continue; //Skip null entries.
                if (t is SignalCounter) continue; //Let SignalCounter draw its own lines to avoid duplicates.
                Handles.DrawLine(transform.position, t.transform.position, Mathf.Max(1f, lineThicknessPixels)); //Draw a line from this emitter to the target.
            }
        }
    }
#endif

    // ---------- Inspector toggle API ----------
    public void SetShowLines(bool value) //Programmatically enable/disable editor connection lines.
    {
        showLinesInEditor = value; //Store the new on/off state.
#if UNITY_EDITOR
        SceneView.RepaintAll(); //Force a Scene view refresh to reflect the change.
#endif
    }

    public bool GetShowLines() => showLinesInEditor; //Returns whether editor lines are currently enabled.
}
