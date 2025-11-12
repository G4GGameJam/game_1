/* This script applies a highlight effect to an object when activated. */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent] //Prevents multiple HighlightObject scripts from being added to the same GameObject.
public class HighlightObject : MonoBehaviour
{
    public Color highlightColor = Color.yellow; //Defines the color shown when the object is highlighted.
    public float outlineWidth = 2f; //Optional: used if switching to outline shaders later.

    Renderer[] _renderers; //Stores all renderers attached to this object and its children.
    Material[] _originalMats; //Stores copies of the original materials.
    Material[] _highlightMats; //Stores highlight materials created at runtime.

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(); //Finds all mesh renderers on the object and its children.
        _originalMats = new Material[_renderers.Length]; //Prepares an array to store the original materials.
        _highlightMats = new Material[_renderers.Length]; //Prepares an array to store highlight materials.

        for (int i = 0; i < _renderers.Length; i++) //Loops through each renderer.
        {
            _originalMats[i] = _renderers[i].material; //Saves the current material of each renderer.
            _highlightMats[i] = new Material(_originalMats[i]); //Creates a duplicate so it can be modified safely.
            _highlightMats[i].color = highlightColor; //Changes the duplicate’s color to the highlight color.
        }
    }

    public void SetHighlight(bool state)
    {
        if (_renderers == null) return; //Prevents errors if renderers are missing.
        for (int i = 0; i < _renderers.Length; i++) //Loops through all renderers.
        {
            _renderers[i].material = state ? _highlightMats[i] : _originalMats[i]; //Switches materials based on highlight state.
        }
    }
}
