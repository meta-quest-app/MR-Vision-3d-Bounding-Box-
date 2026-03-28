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
    [SerializeField] private BoundingBoxRenderer boundingBoxRenderer;
    [SerializeField] private DepthVisualizer depthVisualizer; // On-screen debugger
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
    private int[] _lastSegmentedIndices = null;
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

        // PHASE 6: Update the live visualizer texture
        if (depthVisualizer != null && _hasDepthData)
        {
            depthVisualizer.UpdateVisualization(
                _depthPixels, 
                _textureSize, 
                _selectedPixelX, 
                _selectedPixelY, 
                _lastSegmentedIndices
            );
        }
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

        if (_targetDot == null)
        {
            _targetDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _targetDot.transform.SetParent(this.transform);
            _targetDot.transform.localScale = Vector3.one * 0.03f; // 3cm sphere

            Material redMat = new Material(Shader.Find("Sprites/Default"));
            redMat.color = Color.red;
            _targetDot.GetComponent<MeshRenderer>().material = redMat;
            
            // Turn off physics collision for the dot
            Destroy(_targetDot.GetComponent<SphereCollider>());
            _targetDot.SetActive(false);
        }
    }

    private GameObject _targetDot;

    private void UpdateLaserPointer()
    {
        if (!_hasDepthData || _viewProjMatrices == null) return;
        
        Vector3 worldRayOrigin = OVRInput.GetLocalControllerPosition(controller);
        Quaternion worldRayRotation = OVRInput.GetLocalControllerRotation(controller);
        Vector3 worldRayDirection = worldRayRotation * Vector3.forward;

        Transform trackingSpace = Camera.main?.transform.parent;
        if (trackingSpace != null)
        {
            worldRayOrigin = trackingSpace.TransformPoint(worldRayOrigin);
            worldRayDirection = trackingSpace.TransformDirection(worldRayDirection);
        }

        laserPointer.positionCount = 2;
        laserPointer.SetPosition(0, worldRayOrigin);
        laserPointer.SetPosition(1, worldRayOrigin + worldRayDirection * laserMaxLength);
    }

    private void TrySelectPixel()
    {
        if (!_hasDepthData || _viewProjMatrices == null)
        {
            _lastError = "No depth data yet!";
            return;
        }

        // ** PHASE 12 BUGFIX **
        // Meta ViewProjectionMatrix operates in True Unity World Space.
        // Convert Controller Tracking location firmly to World Space.
        Vector3 worldRayOrigin = OVRInput.GetLocalControllerPosition(controller);
        Quaternion worldRayRotation = OVRInput.GetLocalControllerRotation(controller);
        Vector3 worldRayDirection = worldRayRotation * Vector3.forward;

        Transform trackingSpace = Camera.main?.transform.parent;
        if (trackingSpace != null)
        {
            worldRayOrigin = trackingSpace.TransformPoint(worldRayOrigin);
            worldRayDirection = trackingSpace.TransformDirection(worldRayDirection);
        }

        if (_viewProjMatrices == null || _viewProjMatrices.Length == 0) return;
        Matrix4x4 viewProj = _viewProjMatrices[0]; // Left eye Depth map matrix

        float estimatedDist = 1.0f; 
        int pixelX = -1;
        int pixelY = -1;
        float depth = 0f;

        // Converge onto the exact object surface recursively using World Space Geometry
        for (int i = 0; i < 4; i++)
        {
            Vector3 guessWorldPt = worldRayOrigin + worldRayDirection * estimatedDist;
            Vector4 clipPos = viewProj * new Vector4(guessWorldPt.x, guessWorldPt.y, guessWorldPt.z, 1.0f);

            if (Mathf.Abs(clipPos.w) < 0.0001f) {
                _lastError = "Clip W too small";
                return;
            }

            // NDC to UV
            float u = (clipPos.x / clipPos.w + 1.0f) * 0.5f;
            float v = (-clipPos.y / clipPos.w + 1.0f) * 0.5f; // Invert Y explicitly!

            pixelX = Mathf.Clamp(Mathf.FloorToInt(u * _textureSize), 0, _textureSize - 1);
            pixelY = Mathf.Clamp(Mathf.FloorToInt(v * _textureSize), 0, _textureSize - 1);
            
            int index = pixelY * _textureSize + pixelX;
            depth = _depthPixels[index];

            if (depth > 0.15f && depth < 8.0f) {
                estimatedDist = depth;
            } else {
                break; 
            }
        }

        _selectedPixelX = pixelX;
        _selectedPixelY = pixelY;
        _selectedDepth = depth;
        _hasSelection = true;
        _lastError = "";

        // Put the target dot perfectly on the exact world space hit!
        Vector3 finalWorldHit = Vector3.zero;
        if (depth > 0.1f && depth < 8.0f)
        {
            finalWorldHit = worldRayOrigin + worldRayDirection * depth;
            _targetDot.transform.position = finalWorldHit;
            _targetDot.SetActive(true);
        }
        else
        {
            _targetDot.SetActive(false);
        }

        Debug.Log($"[ObjectSelector] Selected pixel ({pixelX}, {pixelY}) depth: {depth:F3}m");

        // Only proceed with segmentation if depth is valid
        if (depth > 0.15f && depth < 8.0f)
        {
            if (depthSegmenter != null)
            {
                int[] segmentedIndices = depthSegmenter.Segment(_depthPixels, _textureSize, pixelX, pixelY, depth);
                _segmentedPointCount = segmentedIndices.Length;
                _lastSegmentedIndices = segmentedIndices;

                if (boundingBoxRenderer != null && finalWorldHit != Vector3.zero)
                {
                    boundingBoxRenderer.DrawSegmentedBox(segmentedIndices, _depthPixels, _textureSize, _viewProjMatrices, pixelX, pixelY, finalWorldHit);
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

        string info = "=== PHASE 13 ===\n";
        info += $"Depth Active: {_hasDepthData} | Triggers: {_triggerPressCount}\n";

        if (_hasSelection)
        {
            info += $"\nSelection: ({_selectedPixelX}, {_selectedPixelY}) | Depth: {_selectedDepth:F2}m\n";
            info += $"Object Segment Size: {_segmentedPointCount} points\n";
            
            if (boundingBoxRenderer != null && boundingBoxRenderer.HasBox)
            {
                var dim = boundingBoxRenderer.Dimensions;
                info += $"<b>Object Size:</b>\n W: {dim.x:F2}m, H: {dim.y:F2}m, D: {dim.z:F2}m";
            }
        }
        else
        {
            info += "\nAim at object + press TRIGGER\n";
        }

        if (!string.IsNullOrEmpty(_lastError))
            info += $"\n<color=red>{_lastError}</color>";

        statusText.text = $"<size=70%>{info}</size>";
    }
}
