using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Defines the world-space coordinate convention used by the Swinging Paint Bucket Simulation.
    ///
    /// Coordinate system:
    /// - Y is height. Larger Y values are higher above the canvas.
    /// - X and Z form the horizontal canvas plane.
    /// - The starting canvas surface is a horizontal plane at CanvasY = 0.
    /// - The bucket swings above the canvas, so its world-space Y position should remain above CanvasY
    ///   until paint particles fall toward the surface.
    ///
    /// Later paint systems will use this convention for particle-to-texture conversion:
    /// - A paint particle moves in world space with position (x, y, z).
    /// - Manual collision checks detect when the particle crosses the canvas plane at y = CanvasY.
    /// - The impact point keeps its X and Z values while Y is projected onto CanvasY.
    /// - X maps to texture U, and Z maps to texture V.
    /// - The resulting UV coordinate is used to draw paint marks into a canvas texture.
    ///
    /// This helper does not use Rigidbody, Colliders, or Unity physics queries. It only centralizes
    /// the coordinate assumptions and small conversion functions used by custom simulation code.
    /// </summary>
    public static class SimulationCoordinateSystem
    {
        /// <summary>
        /// Initial height of the canvas plane in world units.
        /// </summary>
        public const float CanvasY = 0f;

        /// <summary>
        /// Converts a world-space impact point into normalized canvas UV coordinates.
        ///
        /// Assumptions:
        /// - canvasCenter is the world-space center of the rectangular canvas.
        /// - canvasSize.x is the canvas width along world X.
        /// - canvasSize.y is the canvas depth along world Z.
        /// - U = 0.5 means the impact is centered on X.
        /// - V = 0.5 means the impact is centered on Z.
        ///
        /// The Y component is intentionally ignored because collision with the canvas plane
        /// should already have projected the particle impact onto CanvasY.
        /// </summary>
        public static Vector2 WorldToCanvasUV(Vector3 worldPosition, Vector3 canvasCenter, Vector2 canvasSize)
        {
            if (canvasSize.x <= 0f || canvasSize.y <= 0f)
            {
                Debug.LogWarning("Canvas size must be positive when converting world position to UV.");
                return new Vector2(0.5f, 0.5f);
            }

            float u = ((worldPosition.x - canvasCenter.x) / canvasSize.x) + 0.5f;
            float v = ((worldPosition.z - canvasCenter.z) / canvasSize.y) + 0.5f;

            return new Vector2(u, v);
        }
    }
}
