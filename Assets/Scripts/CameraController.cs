/* This script controls the camera attached to the player */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform target; //The object the camera follows (usually the player).
    public Vector3 offset = new Vector3(0f, 0.5f, 0f); //Camera position relative to the target.
    public float mouseSensitivity = 200f; //How sensitive the camera is to mouse movement.
    public float minPitch = -70f; //Lowest angle the camera can look down.
    public float maxPitch = 80f;  //Highest angle the camera can look up.
    public bool lockCursorOnStart = true; //If true, lock and hide the cursor when the scene starts.

    private float yaw = 0f;   //Left/Right angle around the Y axis.
    private float pitch = 0f; //Up/Down angle around the X axis.

    private void Start() //Runs when the script first starts.
    {
        //Initialize yaw from the target (or from the camera if no target found yet).
        if (target != null)
        {
            yaw = target.eulerAngles.y; //Start yaw based on the player's current rotation.
        }
        else
        {
            yaw = transform.eulerAngles.y; //Fallback: use the camera's current rotation.
        }

        //Lock and hide the cursor.
        if (lockCursorOnStart == true)
        {
            Cursor.lockState = CursorLockMode.Locked; //Keep cursor centered.
            Cursor.visible = false; //Hide the cursor.
        }
    }

    private void LateUpdate() //Runs every frame after all Update() calls (good for following targets).
    {
        //Read mouse movement.
        float mouseX = Input.GetAxis("Mouse X"); //Horizontal mouse movement this frame.
        float mouseY = Input.GetAxis("Mouse Y"); //Vertical mouse movement this frame.

        //Update yaw (left/right) and pitch (up/down).
        yaw = yaw + mouseX * mouseSensitivity * Time.deltaTime;     //Add horizontal look to yaw.
        pitch = pitch - mouseY * mouseSensitivity * Time.deltaTime; //Subtract to make up look up by default.

        //Clamp pitch so the camera cannot flip over.
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch); //Keep pitch within limits.

        //If we have a target, rotate the player to match our yaw so WSAD always feels correct.
        if (target != null)
        {
            Quaternion playerRotation = Quaternion.Euler(0f, yaw, 0f); //Only rotate around Y for the player.
            target.rotation = playerRotation; //Apply rotation to the player.
        }

        //Build the camera's rotation using both pitch and yaw.
        Quaternion cameraRotation = Quaternion.Euler(pitch, yaw, 0f); //Turn X,Y,Z degrees into a rotation.
        transform.rotation = cameraRotation; //Rotate the camera.

        //Follow the target's position using the chosen offset.
        if (target != null)
        {
            transform.position = target.position + offset; //Place the camera at the target plus the offset.
        }
    }
}
