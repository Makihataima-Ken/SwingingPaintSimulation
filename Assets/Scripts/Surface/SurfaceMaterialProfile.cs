using UnityEngine;

namespace SwingingPaint.Surface
{
    [CreateAssetMenu(fileName = "SurfaceMaterialProfile", menuName = "SwingingPaint/Surface Material Profile")]
    public class SurfaceMaterialProfile : ScriptableObject
    {
        public Color baseColor = new Color(0.94f, 0.92f, 0.86f, 1f);

        [Range(0f, 1f)]
        public float absorption = 0.18f;

        [Range(0.05f, 3f)]
        public float spread = 1.2f;

        [Range(0f, 1f)]
        public float roughness = 0.45f;

        [Range(0.05f, 4f)]
        public float dryingSpeed = 0.45f;

        [Range(0f, 2f)]
        public float mixingRate = 0.35f;

        [Range(0f, 1f)]
        public float edgeNoise = 0.45f;

        [Range(0f, 3f)]
        public float splatter = 0.65f;

        [Range(0f, 3f)]
        public float sliding = 0.35f;

        [Range(0f, 1f)]
        public float wetGloss = 0.5f;

        [Min(0.01f)]
        public float noiseScale = 1f;

        public Vector2 grainDirection = Vector2.right;

        [Range(0f, 1f)]
        public float beading = 0f;

        [Tooltip("Multiplier for wet pigment speed along a tilted surface.")]
        [Range(0.1f, 4f)]
        public float downhillFlowSpeed = 1f;

        [Tooltip("How strongly wet paint gathers into narrow downhill rivulets.")]
        [Range(0f, 2f)]
        public float rivuletStrength = 0f;

        [Tooltip("How much thin glossy wet residue remains behind sliding paint.")]
        [Range(0f, 1f)]
        public float wetTrailRetention = 0f;

        private void OnValidate()
        {
            baseColor.r = Mathf.Clamp01(baseColor.r);
            baseColor.g = Mathf.Clamp01(baseColor.g);
            baseColor.b = Mathf.Clamp01(baseColor.b);
            baseColor.a = Mathf.Clamp01(baseColor.a);
            absorption = Mathf.Clamp01(absorption);
            spread = Mathf.Clamp(spread, 0.05f, 3f);
            roughness = Mathf.Clamp01(roughness);
            dryingSpeed = Mathf.Clamp(dryingSpeed, 0.05f, 4f);
            mixingRate = Mathf.Clamp(mixingRate, 0f, 2f);
            edgeNoise = Mathf.Clamp01(edgeNoise);
            splatter = Mathf.Clamp(splatter, 0f, 3f);
            sliding = Mathf.Clamp(sliding, 0f, 3f);
            wetGloss = Mathf.Clamp01(wetGloss);
            noiseScale = Mathf.Max(0.01f, noiseScale);
            beading = Mathf.Clamp01(beading);
            downhillFlowSpeed = Mathf.Clamp(downhillFlowSpeed, 0.1f, 4f);
            rivuletStrength = Mathf.Clamp(rivuletStrength, 0f, 2f);
            wetTrailRetention = Mathf.Clamp01(wetTrailRetention);

            if (grainDirection.sqrMagnitude <= 0.0001f)
            {
                grainDirection = Vector2.right;
            }
            else
            {
                grainDirection.Normalize();
            }
        }
    }
}
