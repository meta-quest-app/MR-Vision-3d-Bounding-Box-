using Meta.XR.BuildingBlocks.AIBlocks;
using Meta.XR.EnvironmentDepth;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86;

public class Phase2DepthReader : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI statusText;

    [Header("Depth")]
    public DepthTextureAccess depthTextureAccess;

    // Latest depth data received
    private float _centerDepth = -1f;
    private bool _hasData = false;
    private float _timer = 0f;

    void Start()
    {
        if (statusText != null)
            statusText.text = "Phase 2 starting...\nWaiting for depth...";

        // Find EnvironmentDepthManager and force enable it
        var edm = depthTextureAccess.GetComponent<EnvironmentDepthManager>();
        if (edm != null)
        {
            edm.enabled = true;
            Debug.Log("[Phase2] EnvironmentDepthManager force enabled.");
        }
        else
        {
            Debug.LogError("[Phase2] EnvironmentDepthManager not found on same GameObject!");
        }

        if (depthTextureAccess == null)
        {
            Debug.LogError("[Phase2] DepthTextureAccess not assigned!");
            return;
        }

        depthTextureAccess.OnDepthTextureUpdateCPU += OnDepthReceived;
        Debug.Log("[Phase2] Subscribed to OnDepthTextureUpdateCPU.");
    }
    void OnDestroy()
    {
        // Always unsubscribe to avoid memory leaks
        if (depthTextureAccess != null)
            depthTextureAccess.OnDepthTextureUpdateCPU -= OnDepthReceived;
    }

    void OnDepthReceived(DepthTextureAccess.DepthFrameData frameData)
    {
        // Depth texture is 320x320, packed Left eye then Right eye
        // Total array length = 320 * 320 * 2
        // Left eye = index 0 to (320*320  1)
        // Right eye = index (320*320) to end

        int size = depthTextureAccess.TextureSize; // 320

        // Get center pixel of LEFT eye
        int cx = size / 2;
        int cy = size / 2;

        // Index formula for 2D  1D: index = cy * width + cx
        int centerIndex = cy * size + cx;

        if (centerIndex < frameData.DepthTexturePixels.Length)
        {
            _centerDepth = frameData.DepthTexturePixels[centerIndex];
            _hasData = true;
        }

        Debug.Log($"[Phase2] Depth frame received. Center: {_centerDepth:F3}m");
    }

    void Update()
    {
        // Request a new depth sample every 0.5 seconds
        _timer += Time.deltaTime;
        if (_timer >= 0.5f)
        {
            _timer = 0f;

            // Add these debug lines
            Debug.Log($"[Phase2] EDM enabled: {depthTextureAccess.enabled}");
            Debug.Log($"[Phase2] IsInitialized: {depthTextureAccess.IsInitialized}");
            if (depthTextureAccess != null && depthTextureAccess.IsInitialized)
            {
                depthTextureAccess.RequestDepthSample();
            }
            else if (depthTextureAccess != null && !depthTextureAccess.IsInitialized)
            {
                if (statusText != null)
                    statusText.text = "DepthTextureAccess\nnot initialized yet...\nWaiting...";
                return;
            }
        }

        // Update UI with latest depth value
        if (_hasData)
        {
            string msg = $"Phase 2 — Depth OK\n" +
                         $"Center depth: {_centerDepth:F3} m\n" +
                         $"Tex size: {depthTextureAccess.TextureSize}x{depthTextureAccess.TextureSize}\n" +
                         $"FPS: {(1f / Time.deltaTime):F1}";

            if (statusText != null)
                statusText.text = msg;
        }
    }
}
 