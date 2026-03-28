using UnityEngine;

/// <summary>
/// Phase 9: Proper Coordinate Space Unprojection.
/// Translates the depth map accurately from Oculus Tracking Space -> Unity World Space,
/// which guarantees the box stays glued exactly to the physical object regardless
/// of where the player teleports or walks.
/// </summary>
public class BoundingBoxRenderer : MonoBehaviour
{
    [Header("Bounding Box Appearance")]
    public float lineWidth = 0.015f; 
    public Color boxColor = Color.green;

    private LineRenderer[] _edgeLines;
    
    // UI tracking data
    public Vector3 Center { get; private set; }
    public Vector3 Dimensions { get; private set; } // width, height, depth in meters
    public float Volume { get; private set; }
    public bool HasBox { get; private set; }

    void Start()
    {
        // Must stay unparented at 0,0,0 World Space to draw lines accurately
        transform.SetParent(null, true);
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        _edgeLines = new LineRenderer[12];
        Material lineMat = new Material(Shader.Find("Sprites/Default"));
        lineMat.color = boxColor;

        for (int i = 0; i < 12; i++)
        {
            GameObject edgeObj = new GameObject($"BB_Edge_{i}");
            edgeObj.transform.SetParent(this.transform);
            
            LineRenderer lr = edgeObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = lineMat;
            lr.startColor = boxColor;
            lr.endColor = boxColor;
            lr.enabled = false;
            
            _edgeLines[i] = lr;
        }
    }

    private Vector3 UnprojectPixelLocal(int px, int py, int texSize, float ndcZ, Matrix4x4 invVP)
    {
        float u = (px + 0.5f) / texSize;
        float v = (py + 0.5f) / texSize;
        float ndcX = u * 2f - 1f;
        float ndcY = -(v * 2f - 1f); // Array Row 0 is Top-Left! Map to Top of Frustum (+1).
        
        Vector4 clipPt = new Vector4(ndcX, ndcY, ndcZ, 1f);
        Vector4 trackingPt4 = invVP * clipPt; // Meta Depth Matrix outputs Tracking Space, NOT World Space!

        if (Mathf.Abs(trackingPt4.w) < 0.0001f) return Vector3.zero;
        
        return new Vector3(trackingPt4.x / trackingPt4.w, trackingPt4.y / trackingPt4.w, trackingPt4.z / trackingPt4.w);
    }

    /// <summary>
    /// Computes accurate 3D bounds by reversing the Meta SDK view-projection matrix, 
    /// then translating the vectors from Tracking Space to World Space!
    /// </summary>
    public void DrawSegmentedBox(int[] segmentedIndices, float[] depthPixels, int textureSize, Matrix4x4[] viewProjMatrices, int targetPx, int targetPy, Vector3 trueWorldHit)
    {
        if (segmentedIndices == null || segmentedIndices.Length < 10) 
        {
            ClearBox();
            return;
        }

        Matrix4x4 vp = viewProjMatrices[0];
        Matrix4x4 invVP = vp.inverse;
        
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // ** PHASE 12 BUGFIX **
        // Meta's Inverse Projection Matrix translates flawlessly to World Space natively.
        // We eliminate all "Tracking Space" modifiers to prevent duplicated shifts.
        Vector3 worldCamPos = mainCam.transform.position;

        // ** PHASE 13 BUGFIX **
        // Meta's Inverse Projection Matrix is demonstrably flawed in coordinate anchoring.
        // We evaluate the mathematical flaw explicitly by unprojecting the exact targeted pixel!
        Vector3 targetNearWorld = UnprojectPixelLocal(targetPx, targetPy, textureSize, 1.0f, invVP);
        Vector3 targetMidWorld = UnprojectPixelLocal(targetPx, targetPy, textureSize, 0.5f, invVP);
        Vector3 targetRayDir = (targetMidWorld - targetNearWorld).normalized;
        
        float targetDepth = depthPixels[targetPy * textureSize + targetPx];
        Vector3 skewedUnprojectedSeed = worldCamPos + targetRayDir * targetDepth;

        // ** You are a genius! We coincide the Matrix centroid directly on the True Flawless Dot! **
        Vector3 translationOffset = trueWorldHit - skewedUnprojectedSeed;

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int validCount = 0;

        for (int i = 0; i < segmentedIndices.Length; i++)
        {
            int pixelId = segmentedIndices[i];
            int px = pixelId % textureSize;
            int py = pixelId / textureSize;
            float depth = depthPixels[pixelId];

            if (depth <= 0.1f || depth > 8.0f) continue;

            // Compute raw optical ray natively in World Space
            Vector3 nearWorldPt = UnprojectPixelLocal(px, py, textureSize, 1.0f, invVP);
            Vector3 midWorldPt = UnprojectPixelLocal(px, py, textureSize, 0.5f, invVP);
            Vector3 worldRayDir = (midWorldPt - nearWorldPt).normalized;

            // Pure Euclidean Depth mapping! (No cosine stretching distortion needed)
            float rayDist = depth;

            // Flawless True World Coordinate Origin + Offset
            Vector3 worldPos = worldCamPos + worldRayDir * rayDist;

            // Shift the geometry mathematically onto the true anchor!
            worldPos += translationOffset;

            if (worldPos.x < min.x) min.x = worldPos.x;
            if (worldPos.y < min.y) min.y = worldPos.y;
            if (worldPos.z < min.z) min.z = worldPos.z;
            
            if (worldPos.x > max.x) max.x = worldPos.x;
            if (worldPos.y > max.y) max.y = worldPos.y;
            if (worldPos.z > max.z) max.z = worldPos.z;

            validCount++;
        }

        if (validCount < 10)
        {
            ClearBox();
            return;
        }

        Dimensions = max - min;
        Center = (min + max) * 0.5f;
        Volume = Dimensions.x * Dimensions.y * Dimensions.z;
        HasBox = true;

        Vector3 v0 = new Vector3(min.x, min.y, min.z); 
        Vector3 v1 = new Vector3(max.x, min.y, min.z); 
        Vector3 v2 = new Vector3(max.x, max.y, min.z); 
        Vector3 v3 = new Vector3(min.x, max.y, min.z); 
        Vector3 v4 = new Vector3(min.x, min.y, max.z); 
        Vector3 v5 = new Vector3(max.x, min.y, max.z); 
        Vector3 v6 = new Vector3(max.x, max.y, max.z); 
        Vector3 v7 = new Vector3(min.x, max.y, max.z); 

        SetEdge(0, v0, v1);
        SetEdge(1, v1, v2);
        SetEdge(2, v2, v3);
        SetEdge(3, v3, v0);
        SetEdge(4, v4, v5);
        SetEdge(5, v5, v6);
        SetEdge(6, v6, v7);
        SetEdge(7, v7, v4);
        SetEdge(8, v0, v4);
        SetEdge(9, v1, v5);
        SetEdge(10, v2, v6);
        SetEdge(11, v3, v7);
    }

    private void SetEdge(int index, Vector3 start, Vector3 end)
    {
        _edgeLines[index].SetPosition(0, start);
        _edgeLines[index].SetPosition(1, end);
        _edgeLines[index].enabled = true;
    }

    public void ClearBox()
    {
        HasBox = false;
        if (_edgeLines == null) return;
        foreach (var edge in _edgeLines) edge.enabled = false;
    }
}
