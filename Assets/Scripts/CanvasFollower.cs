using UnityEngine;

public class CanvasFollower : MonoBehaviour
{
    [Tooltip("The camera the canvas should follow. Usually the CenterEyeAnchor.")]
    public Transform headCamera;
    
    [Tooltip("Distance in meters to keep the canvas away from the camera.")]
    public float distance = 1.0f;
    
    [Tooltip("Vertical offset. Negative values put it slightly below eye level.")]
    public float heightOffset = -0.2f;
    
    [Tooltip("How smoothly the canvas follows the head.")]
    public float smoothSpeed = 5.0f;

    private void Start()
    {
        if (headCamera == null)
        {
            // Fallback to Main Camera if not manually assigned
            if (Camera.main != null)
                headCamera = Camera.main.transform;
        }
    }

    private void Update()
    {
        if (headCamera == null) return;

        // Target position is a set distance in front of the head
        Vector3 targetPosition = headCamera.position + headCamera.forward * distance;
        
        // Apply vertical offset (e.g. to keep it out of the direct center of view)
        targetPosition.y += heightOffset;

        // Smoothly move the canvas
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);

        // Make the canvas face the camera
        // (UI Canvases face the opposite way by default, so we look away from camera)
        Vector3 lookDirection = transform.position - headCamera.position;
        if (lookDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(lookDirection);
        }
    }
}
