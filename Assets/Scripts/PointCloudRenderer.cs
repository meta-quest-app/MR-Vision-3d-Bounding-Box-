using UnityEngine;
using Meta.XR.EnvironmentDepth;
using Meta.XR.BuildingBlocks.AIBlocks;

/// <summary>
/// Renders a point cloud for ONLY the segmented object pixels.
/// Phase 6: Perfects the 3D projection math using explicit Near/Far plane raycasting,
/// eliminating the flat plane clipping issues and the reliance on extracting the camera origin.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PointCloudRenderer : MonoBehaviour
{
    public Material pointCloudMaterial;

    [Header("Point Settings")]
    [Tooltip("Size of each rendered point in world units (meters)")]
    public float pointSize = 0.008f;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private int _lastMeshVertCount = 0;
    private string _lastDebugDetail = "";

    /// <summary>Returns a debug status string for on-screen display.</summary>
    public string DebugStatus
    {
        get
        {
            if (_meshRenderer == null) return "MeshRenderer: NULL";
            return $"Renderer: {(_meshRenderer.enabled ? "ON" : "OFF")} | Verts: {_lastMeshVertCount} | Mat: {(_meshRenderer.sharedMaterial != null ? "OK" : "NONE")}\n{_lastDebugDetail}";
        }
    }

    void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        if (pointCloudMaterial != null)
            _meshRenderer.material = pointCloudMaterial;

        _mesh = new Mesh();
        _mesh.name = "SegmentedPointCloud";
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _meshFilter.mesh = _mesh;

        _meshRenderer.enabled = false;

        // CRITICAL PHASE 6: Detach from ALL parents (especially Canvas) so its
        // World Position and Scale are 100% untainted by UI RectTransforms!
        transform.SetParent(null, true);
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private Vector3 UnprojectPixel(int px, int py, int texSize, float ndcZ, Matrix4x4 invVP)
    {
        float u = (px + 0.5f) / texSize;
        float v = (py + 0.5f) / texSize;
        float ndcX = u * 2f - 1f;
        float ndcY = -(v * 2f - 1f); // Depth map textures usually have top-left origin, NDC is bottom-left
        
        Vector4 clipPt = new Vector4(ndcX, ndcY, ndcZ, 1f);
        Vector4 worldPt4 = invVP * clipPt;
        
        // Perspective divide
        return new Vector3(worldPt4.x / worldPt4.w, worldPt4.y / worldPt4.w, worldPt4.z / worldPt4.w);
    }

    /// <summary>
    /// Computes accurate 3D positions by unprojecting rays natively on CPU.
    /// </summary>
    public void RenderSegmentedPoints(int[] segmentedIndices, float[] depthPixels, int textureSize, Matrix4x4[] viewProjMatrices)
    {
        if (segmentedIndices == null || segmentedIndices.Length == 0)
        {
            _meshRenderer.enabled = false;
            return;
        }

        Matrix4x4 vp = viewProjMatrices[0]; // Left eye
        Matrix4x4 invVP = vp.inverse;

        // Use the main tracking camera as a highly reliable reference for the optical origin
        // (The left eye depth camera is nearly identical in position to the main tracked head)
        Camera mainCam = Camera.main;
        if (mainCam == null) return;
        
        Vector3 camPos = mainCam.transform.position;

        // Calculate the depth camera's forward vector by unprojecting the dead center pixel
        Vector3 centerNear = UnprojectPixel(textureSize / 2, textureSize / 2, textureSize, 1.0f, invVP);
        Vector3 centerFar = UnprojectPixel(textureSize / 2, textureSize / 2, textureSize, 0.0f, invVP);
        Vector3 camForward = (centerFar - centerNear).normalized;

        // Billboard orientation
        Vector3 billboardRight = mainCam.transform.right;
        Vector3 billboardUp = mainCam.transform.up;

        int numQuads = segmentedIndices.Length;
        int numVerts = numQuads * 4;
        int numIndices = numQuads * 6;

        _mesh.Clear();
        _mesh.indexFormat = numVerts > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        Vector3[] verts = new Vector3[numVerts];
        int[] indices = new int[numIndices];

        int vIdx = 0;
        int iIdx = 0;
        float halfSize = pointSize * 0.5f;
        int validCount = 0;

        for (int i = 0; i < numQuads; i++)
        {
            int pixelId = segmentedIndices[i];
            int px = pixelId % textureSize;
            int py = pixelId / textureSize;
            float depth = depthPixels[pixelId];

            if (depth <= 0.1f || depth > 10.0f)
                continue;

            // Phase 6 Math Fix: Near/Far Unprojection
            // Unproject both the near clip plane (1.0) and far clip plane (0.0) for this pixel
            // (Note: Unity/Meta usually uses Reversed-Z, so 1.0 is near, 0.0 is far)
            Vector3 nearPt = UnprojectPixel(px, py, textureSize, 1.0f, invVP);
            Vector3 farPt = UnprojectPixel(px, py, textureSize, 0.0f, invVP);
            
            // Ray direction connecting near and far planes
            Vector3 rayDir = (farPt - nearPt).normalized;

            // Depth provided by the API is typically Linear Depth (Z-axis distance, not ray distance).
            // To find the along-ray distance: distance = linearDepth / cos(angle to principal axis)
            float cosAngle = Vector3.Dot(rayDir, camForward);
            float rayDist = (Mathf.Abs(cosAngle) > 0.001f) ? depth / cosAngle : depth;

            // Place the point physically in the world
            Vector3 worldPos = camPos + rayDir * rayDist;

            // ── Build a camera-facing billboard quad ──
            verts[vIdx + 0] = worldPos + (-billboardRight - billboardUp) * halfSize;
            verts[vIdx + 1] = worldPos + (-billboardRight + billboardUp) * halfSize;
            verts[vIdx + 2] = worldPos + ( billboardRight + billboardUp) * halfSize;
            verts[vIdx + 3] = worldPos + ( billboardRight - billboardUp) * halfSize;

            indices[iIdx + 0] = vIdx + 0;
            indices[iIdx + 1] = vIdx + 1;
            indices[iIdx + 2] = vIdx + 2;
            indices[iIdx + 3] = vIdx + 0;
            indices[iIdx + 4] = vIdx + 2;
            indices[iIdx + 5] = vIdx + 3;

            vIdx += 4;
            iIdx += 6;
            validCount++;
        }

        // Trim arrays
        if (vIdx < numVerts)
        {
            System.Array.Resize(ref verts, vIdx);
            System.Array.Resize(ref indices, iIdx);
        }

        _mesh.vertices = verts;
        _mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        _meshFilter.mesh = _mesh;

        _meshRenderer.enabled = true;
        _lastMeshVertCount = vIdx;

        _lastDebugDetail = $"Phase 6 Unproj | Valid: {validCount}\nCam: ({camPos.x:F2}, {camPos.y:F2}, {camPos.z:F2})";
        Debug.Log($"[PointCloudRenderer] Phase 6: {validCount} points.");
    }

    public void ClearPointCloud()
    {
        if (_mesh != null) _mesh.Clear();
        if (_meshRenderer != null) _meshRenderer.enabled = false;
    }
}
