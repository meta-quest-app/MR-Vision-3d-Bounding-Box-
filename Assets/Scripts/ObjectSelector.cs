using UnityEngine;
using TMPro;
using Meta.XR.EnvironmentDepth;
using Meta.XR.BuildingBlocks.AIBlocks;

/// <summary>
/// Handles controller-based pixel selection on the depth map.
/// When the user presses the trigger, it maps the controller ray to a depth texture pixel,
/// reads the depth value, and invokes segmentation + point cloud rendering.
/// </summary>
public class ObjectSelector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnvironmentDepthManager environmentDepthManager;
    [SerializeField] private DepthTextureAccess depthTextureAccess;
    [SerializeField] private DepthSegmenter depthSegmenter;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;
    [SerializeField] private TMP_Text statusText;

    [Header("Controller Settings")]
    [Tooltip("Use right hand controller by default")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Header("Visual Feedback")]
    [SerializeField] private LineRenderer laserPointer;
    [SerializeField] private float laserMaxLength = 10f;

    // Cached depth frame data
    private float[] _depthPixels;
    private int _textureSize;
    private Matrix4x4[] _viewProjMatrices;
    private bool _hasDepthData = false;

    // Selection state
    private int _selectedPixelX = -1;
    private int _selectedPixelY = -1;
    private float _selectedDepth = 0f;
    private bool _hasSelection = false;

    // Debug state
    private int _triggerPressCount = 0;
    private int _segmentedPointCount = 0;
    private string _lastError = "";

    private void OnEnable()
    {
        if (depthTextureAccess != null)
            depthTextureAccess.OnDepthTextureUpdateCPU += OnDepthUpdated;
    }

    private void OnDisable()
    {
        if (depthTextureAccess != null)
            depthTextureAccess.OnDepthTextureUpdateCPU -= OnDepthUpdated;
    }

    private void OnDepthUpdated(DepthTextureAccess.DepthFrameData frameData)
    {
        if (!frameData.DepthTexturePixels.IsCreated) return;

        _textureSize = depthTextureAccess.TextureSize;
        int numPoints = _textureSize * _textureSize;

        // Cache the depth pixels as a regular array for segmentation use
        if (_depthPixels == null || _depthPixels.Length != numPoints)
            _depthPixels = new float[numPoints];

        // Copy depth data to managed array
        Unity.Collections.NativeArray<float>.Copy(frameData.DepthTexturePixels, _depthPixels, numPoints);
        
        // Copy the view projection matrices (store our own copy so they don't get invalidated)
        if (frameData.ViewProjectionMatrix != null)
        {
            _viewProjMatrices = new Matrix4x4[frameData.ViewProjectionMatrix.Length];
            System.Array.Copy(frameData.ViewProjectionMatrix, _viewProjMatrices, frameData.ViewProjectionMatrix.Length);
        }
        
        _hasDepthData = true;
    }

    private void Update()
    {
        // CRITICAL: Must call RequestDepthSample to trigger CPU readback.
        // Without this, OnDepthTextureUpdateCPU never fires.
        if (environmentDepthManager != null && environmentDepthManager.IsDepthAvailable && depthTextureAccess != null)
        {
            depthTextureAccess.RequestDepthSample();
        }

        UpdateLaserPointer();

        // Check for trigger press on BOTH controllers as fallback
        bool rightTrigger = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool leftTrigger = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        
        if (rightTrigger || leftTrigger)
        {
            _triggerPressCount++;
            TrySelectPixel();
        }

        UpdateStatusUI();
    }

    private void Start()
    {
        // Auto-create a laser pointer if none was assigned in the Inspector
        if (laserPointer == null)
        {
            GameObject laserObj = new GameObject("LaserPointer");
            laserObj.transform.SetParent(this.transform);
            laserPointer = laserObj.AddComponent<LineRenderer>();
            laserPointer.positionCount = 2;
            laserPointer.startWidth = 0.005f;
            laserPointer.endWidth = 0.002f;
            
            // Create a simple bright unlit material for the laser
            Material laserMat = new Material(Shader.Find("Sprites/Default"));
            laserMat.color = Color.cyan;
            laserPointer.material = laserMat;
            laserPointer.startColor = Color.cyan;
            laserPointer.endColor = new Color(0f, 1f, 1f, 0.3f);
        }
    }

    private void UpdateLaserPointer()
    {

        Vector3 rayOrigin = OVRInput.GetLocalControllerPosition(controller);
        Quaternion rayRotation = OVRInput.GetLocalControllerRotation(controller);
        Vector3 rayDirection = rayRotation * Vector3.forward;

        // Transform to world space if needed (controller positions are in tracking space)
        Transform trackingSpace = Camera.main?.transform.parent;
        if (trackingSpace != null)
        {
            rayOrigin = trackingSpace.TransformPoint(rayOrigin);
            rayDirection = trackingSpace.TransformDirection(rayDirection);
        }

        laserPointer.positionCount = 2;
        laserPointer.SetPosition(0, rayOrigin);
        laserPointer.SetPosition(1, rayOrigin + rayDirection * laserMaxLength);
    }

    private void TrySelectPixel()
    {
        if (!_hasDepthData || _viewProjMatrices == null)
        {
            _lastError = "No depth data yet!";
            return;
        }

        // Get controller ray in world space
        Vector3 rayOrigin = OVRInput.GetLocalControllerPosition(controller);
        Quaternion rayRotation = OVRInput.GetLocalControllerRotation(controller);
        Vector3 rayDirection = rayRotation * Vector3.forward;

        Transform trackingSpace = Camera.main?.transform.parent;
        if (trackingSpace != null)
        {
            rayOrigin = trackingSpace.TransformPoint(rayOrigin);
            rayDirection = trackingSpace.TransformDirection(rayDirection);
        }

        // Project a point along the ray into screen space using the depth camera's ViewProj matrix
        Matrix4x4 viewProj = _viewProjMatrices[0]; // Left eye

        // Project a world-space point along the ray onto the depth camera's image plane
        Vector3 worldPoint = rayOrigin + rayDirection * 1.0f;
        Vector4 clipPos = viewProj * new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1.0f);

        // Perspective divide to get NDC (-1 to 1)
        if (Mathf.Abs(clipPos.w) < 0.0001f)
        {
            _lastError = "Clip W too small";
            return;
        }
        float ndcX = clipPos.x / clipPos.w;
        float ndcY = clipPos.y / clipPos.w;

        // NDC to UV (0 to 1)
        float u = (ndcX + 1.0f) * 0.5f;
        float v = (-ndcY + 1.0f) * 0.5f; // Invert Y (depth texture origin is top-left)

        // UV to pixel coordinates
        int pixelX = Mathf.Clamp(Mathf.FloorToInt(u * _textureSize), 0, _textureSize - 1);
        int pixelY = Mathf.Clamp(Mathf.FloorToInt(v * _textureSize), 0, _textureSize - 1);

        int index = pixelY * _textureSize + pixelX;
        float depth = _depthPixels[index];

        _selectedPixelX = pixelX;
        _selectedPixelY = pixelY;
        _selectedDepth = depth;
        _hasSelection = true;
        _lastError = "";

        Debug.Log($"[ObjectSelector] Selected pixel ({pixelX}, {pixelY}) depth: {depth:F3}m");

        // Only proceed with segmentation if depth is valid
        if (depth > 0.15f && depth < 8.0f)
        {
            if (depthSegmenter != null)
            {
                int[] segmentedIndices = depthSegmenter.Segment(_depthPixels, _textureSize, pixelX, pixelY, depth);
                _segmentedPointCount = segmentedIndices.Length;

                Debug.Log($"[ObjectSelector] Segmented {segmentedIndices.Length} points");

                if (pointCloudRenderer != null)
                {
                    pointCloudRenderer.RenderSegmentedPoints(segmentedIndices, _depthPixels, _textureSize, _viewProjMatrices);
                }
            }
            else
            {
                _lastError = "DepthSegmenter is NULL!";
            }
        }
        else
        {
            _lastError = $"Invalid depth: {depth:F3}m";
        }
    }

    private void UpdateStatusUI()
    {
        if (statusText == null) return;

        string info = "=== PHASE 5 DEBUG ===\n";
        
        // Show connection status
        info += $"DepthMgr: {(environmentDepthManager != null ? "OK" : "MISSING!")}\n";
        info += $"DepthAccess: {(depthTextureAccess != null ? "OK" : "MISSING!")}\n";
        info += $"Segmenter: {(depthSegmenter != null ? "OK" : "MISSING!")}\n";
        info += $"PointCloud: {(pointCloudRenderer != null ? "OK" : "MISSING!")}\n";
        info += $"Depth Data: {_hasDepthData} | Size: {_textureSize}\n";
        info += $"Triggers: {_triggerPressCount}\n";

        if (_hasSelection)
        {
            info += $"\nPixel: ({_selectedPixelX}, {_selectedPixelY})\n";
            info += $"Depth: {_selectedDepth:F3} m\n";
            info += $"Segmented: {_segmentedPointCount} pts\n";
            if (pointCloudRenderer != null)
                info += $"{pointCloudRenderer.DebugStatus}\n";
        }
        else
        {
            info += "\nAim at object + press TRIGGER\n";
        }

        if (!string.IsNullOrEmpty(_lastError))
            info += $"\n<color=red>{_lastError}</color>";

        statusText.text = info;
    }
}
