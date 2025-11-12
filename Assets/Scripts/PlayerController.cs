/* This script controls the player's movement. */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f; //How fast the player moves.
    private CharacterController cc; //Referencing the CharacterController component.

    private void Awake() //Function that happens before the Start() function.
    {
        cc = GetComponent<CharacterController>(); //Gets the properties of the player's Character Controller.
    }

    private void Update() //Function that happens every frame.
    {
        float h = Input.GetAxisRaw("Horizontal"); //Creating a float for A/D or Left/Right movement.
        float v = Input.GetAxisRaw("Vertical");   //Creating a float for W/S or Up/Down movement.

        Vector3 input = new Vector3(h, 0f, v).normalized; //Creating a new Vector3(x,y,z position) to determine player input while moving.
        Vector3 worldMove = transform.TransformDirection(input); //Creating a new Vector3(x,y,z position) to determine where the player will move using the transform direction and input Vector3 value.

        cc.SimpleMove(worldMove * moveSpeed); //Uses Character Controller's Simple Move Function to move the player multiplying the direction by the speed.
    }
}

