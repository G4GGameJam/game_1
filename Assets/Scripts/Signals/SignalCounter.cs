/* This script tracks multiple SignalEmitters and triggers a target once all have signaled at least once.
 * In the Editor, it can draw cyan lines between emitters and the target; no runtime visuals.
 */

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SignalCounter : SignalTarget //Counts signals from specific emitters and forwards once all have fired.
{
    [Header("Emitters to Watch")] //Groups the list of emitters in the Inspector.
    public List<SignalEmitter> emitters = new List<SignalEmitter>(); //Emitters this counter is waiting on.

    [Tooltip("Target to notify once all watched emitters have signaled at least once.")] //Explains when the target is called.
    public SignalTarget target; //The receiver to notify after all emitters have signaled.

    [Header("Visualization")] //Groups editor-only visualization settings.
    [SerializeField] private bool showLinesInEditor = true; //If true, draw cyan lines in the Scene view (Editor only).
    [SerializeField] private Color idleLineColor = Color.cyan; //Color used for editor connection lines.

    // Internal: editor line thickness in pixels
    private const float lineThicknessPixels = 3f; //Thickness of the editor lines in pixels.

    // Track which emitters have signaled this session
    private readonly HashSet<SignalEmitter> _triggered = new HashSet<SignalEmitter>(); //Set of emitters that have already signaled.

    public override void OnSignal(SignalEmitter from) //Called when any emitter sends a signal to this counter.
    {
        if (!from) return; //Ignore null senders (safety).
        if (emitters.Count > 0 && !emitters.Contains(from)) return; //If a watch list exists, ignore unknown emitters.

        bool firstFromThis = _triggered.Add(from); //Adds emitter; returns true only the first time it signals.

        if (firstFromThis && emitters.Count > 0 && _triggered.Count == emitters.Count) //If this completed the set…
        {
            if (target) target.OnSignal(null); //Notify the target that all emitters have fired (source not needed).
            else Debug.LogWarning($"[SignalCounter] All emitters met, but no target set on {name}."); //Warn if no target.
        }

#if UNITY_EDITOR
        SceneView.RepaintAll(); //Refresh Scene view so editor lines update immediately.
#endif
    }

    // ---------- Edit-Mode Gizmos ----------
#if UNITY_EDITOR
    private void OnDrawGizmos() //Draws editor-only lines between emitters and the target.
    {
        if (!showLinesInEditor || Application.isPlaying || emitters == null || !target) return; //Only draw in Editor, not play mode, and with valid data.

        using (new Handles.DrawingScope(idleLineColor)) //Set the drawing color for the scope.
        {
            foreach (var emitter in emitters) //Loop through all watched emitters.
            {
                if (!emitter) continue; //Skip missing references.
                Handles.DrawLine(emitter.transform.position, target.transform.position, Mathf.Max(1f, lineThicknessPixels)); //Draw a line from emitter to target.
            }
        }
    }
#endif

    // ---------- Inspector toggle API ----------
    public void SetShowLines(bool value) //Programmatic toggle for editor line visibility.
    {
        showLinesInEditor = value; //Store the new visibility.
#if UNITY_EDITOR
        SceneView.RepaintAll(); //Force Scene view to refresh.
#endif
    }

    public bool GetShowLines() => showLinesInEditor; //Returns whether editor lines are enabled.
}
