using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Performs depth-based object segmentation using BFS (Breadth-First Search) flood-fill.
/// Starting from a seed pixel, expands to neighboring pixels that have similar depth values
/// to isolate the selected object from the rest of the scene.
/// </summary>
public class DepthSegmenter : MonoBehaviour
{
    [Header("Segmentation Settings")]
    [Tooltip("Maximum depth difference (meters) for a neighbor to be considered part of the same object.")]
    [Range(0.05f, 0.5f)]
    public float depthThreshold = 0.08f;

    [Tooltip("Maximum number of points to include in segmentation (performance safety limit).")]
    public int maxSegmentSize = 10000;

    /// <summary>
    /// Segments an object from the depth map starting at the given seed pixel.
    /// Uses BFS flood-fill with depth similarity as the grouping condition.
    /// </summary>
    /// <param name="depthPixels">1D array of depth values (left eye, size = texSize*texSize)</param>
    /// <param name="texSize">Width/height of the square depth texture (e.g., 320)</param>
    /// <param name="seedX">X coordinate of the selected seed pixel</param>
    /// <param name="seedY">Y coordinate of the selected seed pixel</param>
    /// <param name="seedDepth">Depth value at the seed pixel</param>
    /// <returns>Array of pixel indices belonging to the segmented object</returns>
    public int[] Segment(float[] depthPixels, int texSize, int seedX, int seedY, float seedDepth)
    {
        int totalPixels = texSize * texSize;
        bool[] visited = new bool[totalPixels];
        List<int> segmentedIndices = new List<int>();

        // BFS queue stores pixel coordinates as (x, y) pairs
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        
        int seedIndex = seedY * texSize + seedX;
        queue.Enqueue(new Vector2Int(seedX, seedY));
        visited[seedIndex] = true;

        // 4-connected neighbors (up, down, left, right)
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };

        while (queue.Count > 0 && segmentedIndices.Count < maxSegmentSize)
        {
            Vector2Int current = queue.Dequeue();
            int currentIndex = current.y * texSize + current.x;
            float currentDepth = depthPixels[currentIndex];

            // Add this pixel to the segmented set
            segmentedIndices.Add(currentIndex);

            // Check all 4 neighbors
            for (int d = 0; d < 4; d++)
            {
                int nx = current.x + dx[d];
                int ny = current.y + dy[d];

                // Bounds check
                if (nx < 0 || nx >= texSize || ny < 0 || ny >= texSize)
                    continue;

                int neighborIndex = ny * texSize + nx;

                // Already visited
                if (visited[neighborIndex])
                    continue;

                visited[neighborIndex] = true;

                float neighborDepth = depthPixels[neighborIndex];

                // Skip invalid depths
                if (neighborDepth <= 0.15f || neighborDepth > 8.0f)
                    continue;

                // Check depth similarity against the SEED depth (not current pixel)
                // This prevents gradual drift where each pixel is close to its neighbor
                // but the overall segment drifts far from the original object depth.
                float depthDiff = Mathf.Abs(neighborDepth - seedDepth);
                if (depthDiff <= depthThreshold)
                {
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        Debug.Log($"[DepthSegmenter] Segmented {segmentedIndices.Count} pixels " +
                  $"(seed depth: {seedDepth:F3}m, threshold: {depthThreshold:F3}m)");

        return segmentedIndices.ToArray();
    }
}
