using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 6: On-Screen Debugger for the Meta Quest Depth API.
/// Converts the raw float[] depth array into a 2D Texture and displays it on a UI RawImage.
/// Highly useful for casting to a laptop (via Meta Horizon Developer Hub) to prove
/// the algorithms are seeing the correct pixels.
/// </summary>
public class DepthVisualizer : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The UI Raw Image component that will display the depth map")]
    [SerializeField] private RawImage depthDisplay;

    [Header("Visualization Settings")]
    [SerializeField] private float minDepth = 0.1f;
    [SerializeField] private float maxDepth = 4.0f; // Limit to 4m for better contrast indoors

    private Texture2D _depthTex;
    private Color32[] _colorPixels;
    private int _lastTexSize = 0;

    /// <summary>
    /// Renders the raw depth map to the RawImage.
    /// Also overlays the selected pixel (Red) and the segmented object (Green).
    /// </summary>
    public void UpdateVisualization(float[] depthData, int texSize, int selectedX, int selectedY, int[] segmentedIndices)
    {
        if (depthDisplay == null || depthData == null || texSize <= 0) return;

        // Initialize or resize texture if size changes
        int totalPixels = texSize * texSize;
        if (_depthTex == null || _lastTexSize != texSize)
        {
            _depthTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            _depthTex.filterMode = FilterMode.Point; // Crisp pixels
            _colorPixels = new Color32[totalPixels];
            _lastTexSize = texSize;
            depthDisplay.texture = _depthTex;
        }

        float depthRange = maxDepth - minDepth;

        // 1. Convert all depth floats to grayscale pixels
        for (int i = 0; i < totalPixels; i++)
        {
            float d = depthData[i];
            byte gray = 0; // Black for invalid/out of bounds

            if (d >= minDepth && d <= maxDepth)
            {
                // Normalize depth to 0-1, then invert so closer is whiter, further is darker
                float normalized = 1.0f - Mathf.Clamp01((d - minDepth) / depthRange);
                gray = (byte)(normalized * 255f);
            }

            _colorPixels[i] = new Color32(gray, gray, gray, 255);
        }

        // 2. Overlay Segmented Indices (Green)
        if (segmentedIndices != null && segmentedIndices.Length > 0)
        {
            for (int i = 0; i < segmentedIndices.Length; i++)
            {
                int index = segmentedIndices[i];
                if (index >= 0 && index < totalPixels)
                {
                    // Tint the grayscale pixel green to show segmentation boundary
                    byte oldGray = _colorPixels[index].g;
                    _colorPixels[index] = new Color32(oldGray, 255, oldGray, 255);
                }
            }
        }

        // 3. Mark the exactly selected pixel as a prominent Red Dot
        if (selectedX >= 0 && selectedY >= 0 && selectedX < texSize && selectedY < texSize)
        {
            // Draw a small 5x5 crosshair/dot for visibility on a laptop screen
            int radius = 2;
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    int nx = Mathf.Clamp(selectedX + x, 0, texSize - 1);
                    int ny = Mathf.Clamp(selectedY + y, 0, texSize - 1);
                    int nIndex = ny * texSize + nx;
                    _colorPixels[nIndex] = new Color32(255, 0, 0, 255);
                }
            }
        }

        // Upload to GPU
        _depthTex.SetPixels32(_colorPixels);
        _depthTex.Apply();
    }
}
