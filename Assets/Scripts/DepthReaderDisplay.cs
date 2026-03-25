using UnityEngine;
using TMPro; // For TMP_Text
using Meta.XR.EnvironmentDepth;
using Meta.XR.BuildingBlocks.AIBlocks;

public class DepthReaderDisplay : MonoBehaviour
{
    [SerializeField] private EnvironmentDepthManager environmentDepthManager;
    [SerializeField] private DepthTextureAccess depthTextureAccess;
    [SerializeField] private TMP_Text statusText;

    private float _centerDepth = 0f;

    private void OnEnable()
    {
        if (depthTextureAccess != null)
        {
            depthTextureAccess.OnDepthTextureUpdateCPU += OnDepthUpdated;
        }
    }

    private void OnDisable()
    {
        if (depthTextureAccess != null)
        {
            depthTextureAccess.OnDepthTextureUpdateCPU -= OnDepthUpdated;
        }
    }

    private void Update()
    {
        if (environmentDepthManager == null || depthTextureAccess == null || statusText == null)
            return;

        // Formulate debug string to show live status of the managers
        string info = $"EDM IsDepthAvailable: {environmentDepthManager.IsDepthAvailable}\n";
        info += $"DTA IsInitialized: {depthTextureAccess.IsInitialized}\n";

        // Depth becomes available a few frames AFTER permissions are accepted.
        if (environmentDepthManager.IsDepthAvailable)
        {
            // CRITICAL: We must explicitly trigger a sample to pull the depth pixels to CPU.
            // If we don't call this, IsInitialized stays false forever.
            depthTextureAccess.RequestDepthSample();
            
            if (depthTextureAccess.IsInitialized)
            {
                info += $"\nCenter Depth: {_centerDepth:F3} m";
            }
            else
            {
                info += "\nWaiting for GPU readback...";
            }
        }
        else
        {
            info += "\nWaiting for Depth (Allow Scene permission)...";
        }

        statusText.text = info;
    }

    private void OnDepthUpdated(DepthTextureAccess.DepthFrameData frameData)
    {
        if (!frameData.DepthTexturePixels.IsCreated) return;

        // Texture size dynamically scales, usually 320x320
        int textureSize = depthTextureAccess.TextureSize;
        
        // Find the center pixel of the LEFT eye
        // DepthTexturePixels contains raw data packed left-eye then right-eye
        int centerX = textureSize / 2;
        int centerY = textureSize / 2;
        int index = centerX + centerY * textureSize;

        if (index >= 0 && index < frameData.DepthTexturePixels.Length)
        {
            _centerDepth = frameData.DepthTexturePixels[index];
        }
    }
}
