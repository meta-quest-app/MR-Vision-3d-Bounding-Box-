using UnityEngine;
using Meta.XR.EnvironmentDepth;
using Meta.XR.BuildingBlocks.AIBlocks;

/// <summary>
/// Renders a point cloud for ONLY the segmented object pixels.
/// Phase 5: Computes 3D world positions on the CPU using inverse VP ray-casting.
/// No longer relies on _EnvironmentDepthZBufferParams (which was likely unset).
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

        // CRITICAL: Keep transform at identity so vertex positions = world positions
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Extracts the camera (eye) position from a ViewProjection matrix.
    /// Camera position is the null-point of the VP matrix: VP * [c,1]^T has w=0.
    /// Computed as: c = -(VP_3x3)^-1 * VP_col3
    /// </summary>
    private Vector3 ExtractCameraPosition(Matrix4x4 vp)
    {
        // Build the upper-left 3x3 submatrix
        Matrix4x4 m = Matrix4x4.identity;
        m[0, 0] = vp[0, 0]; m[0, 1] = vp[0, 1]; m[0, 2] = vp[0, 2];
        m[1, 0] = vp[1, 0]; m[1, 1] = vp[1, 1]; m[1, 2] = vp[1, 2];
        m[2, 0] = vp[2, 0]; m[2, 1] = vp[2, 1]; m[2, 2] = vp[2, 2];

        Matrix4x4 inv = m.inverse;

        // The 4th column (translation part) of the VP matrix
        Vector3 t = new Vector3(vp[0, 3], vp[1, 3], vp[2, 3]);

        // Camera position = -inv(VP_3x3) * VP_col3
        Vector3 camPos;
        camPos.x = -(inv[0, 0] * t.x + inv[0, 1] * t.y + inv[0, 2] * t.z);
        camPos.y = -(inv[1, 0] * t.x + inv[1, 1] * t.y + inv[1, 2] * t.z);
        camPos.z = -(inv[2, 0] * t.x + inv[2, 1] * t.y + inv[2, 2] * t.z);

        return camPos;
    }

    /// <summary>
    /// Called by ObjectSelector after segmentation is complete.
    /// Computes world positions on CPU using inverse VP ray-casting,
    /// then builds a mesh with billboarded quads at those world positions.
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

        // Extract depth camera position from the VP matrix
        Vector3 camPos = ExtractCameraPosition(vp);

        // Compute the camera forward direction by unprojecting the center pixel
        Vector4 centerClip = new Vector4(0f, 0f, 0.5f, 1f);
        Vector4 centerW4 = invVP * centerClip;
        Vector3 centerWorld = new Vector3(centerW4.x / centerW4.w, centerW4.y / centerW4.w, centerW4.z / centerW4.w);
        Vector3 camForward = (centerWorld - camPos).normalized;

        // Get the camera's right and up vectors for billboard orientation
        // Use the main camera for billboard facing (it represents where the user is looking)
        Camera mainCam = Camera.main;
        Vector3 billboardRight = mainCam != null ? mainCam.transform.right : Vector3.right;
        Vector3 billboardUp = mainCam != null ? mainCam.transform.up : Vector3.up;

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

            // ── Pixel → NDC ──
            float u = (px + 0.5f) / textureSize;
            float v = (py + 0.5f) / textureSize;
            float ndcX = u * 2f - 1f;
            float ndcY = -(v * 2f - 1f); // Flip Y: depth texture top-left origin → NDC bottom-left origin

            // ── Unproject to get the ray direction ──
            // Use any ndcZ (0.5) to get a world-space point on the ray through this pixel
            Vector4 clipPt = new Vector4(ndcX, ndcY, 0.5f, 1f);
            Vector4 worldPt4 = invVP * clipPt;
            Vector3 worldPt = new Vector3(worldPt4.x / worldPt4.w, worldPt4.y / worldPt4.w, worldPt4.z / worldPt4.w);

            // Ray from depth camera through this pixel
            Vector3 rayDir = (worldPt - camPos).normalized;

            // ── Place point at correct depth along the ray ──
            // Linear depth = distance along camera's forward axis (not along the ray).
            // So rayDistance = linearDepth / cos(angle between ray and forward)
            float cosAngle = Vector3.Dot(rayDir, camForward);
            float rayDist = (Mathf.Abs(cosAngle) > 0.001f) ? depth / cosAngle : depth;

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

        // Trim arrays if some points were skipped
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

        // Store debug detail for UI
        Vector4 zParamsCheck = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
        _lastDebugDetail = $"CamPos: ({camPos.x:F2},{camPos.y:F2},{camPos.z:F2}) | Valid: {validCount}\nZParams: ({zParamsCheck.x:F2},{zParamsCheck.y:F2},{zParamsCheck.z:F2},{zParamsCheck.w:F2})";

        Debug.Log($"[PointCloudRenderer] Phase 5: {validCount} world-positioned points, camPos={camPos}");
    }

    public void ClearPointCloud()
    {
        if (_mesh != null) _mesh.Clear();
        if (_meshRenderer != null) _meshRenderer.enabled = false;
    }
}
