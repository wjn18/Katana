using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OCDesign.LookDev.Editor
{
    /// <summary>
    /// Bakes the existing, unscaled cloud SDF into a linear RGBA volume. This class
    /// only returns a Texture3D; the caller owns saving, materials and box placement.
    /// </summary>
    public static class ComicCloudDensityFactory
    {
        public static Vector3 DomainMin => new Vector3(-26f, -14f, -22f);
        public static Vector3 DomainMax => new Vector3(26f, 65f, 22f);
        public static Vector3 DomainSize => DomainMax - DomainMin;
        public static Vector3 DomainCenter => (DomainMin + DomainMax) * 0.5f;
        public static Bounds DomainBounds => new Bounds(DomainCenter, DomainSize);

        /// <summary>
        /// R = cloud density; G = fixed object-space paint-block value;
        /// B = normalized domain height; A = 1. All channels are linear data.
        /// Texel centres sample DomainMin + ((index + .5) / size) * DomainSize.
        /// A unit box [-.5,.5] therefore uses UVW = positionOS + .5; scale the box
        /// by DomainSize (52,79,44) for the original SDF aspect ratio. Its origin
        /// represents DomainCenter (0,25.5,0), not the old mesh bounds centre.
        /// </summary>
        public static Texture3D Create(int seed, int width = 128, int height = 192, int depth = 112)
        {
            if (width < 8 || height < 8 || depth < 8 || width > 256 || height > 256 || depth > 256)
                throw new ArgumentOutOfRangeException(nameof(width), "Volume dimensions must each be between 8 and 256.");

            var field = new ComicCloudMeshFactory.CloudField(seed);
            var pixels = new Color32[checked(width * height * depth)];
            Vector3 origin = DomainMin;
            Vector3 size = DomainSize;
            Vector3 step = new Vector3(size.x / width, size.y / height, size.z / depth);
            var random = new System.Random(unchecked(seed * 486187739 + 713));
            Vector3 offset = new Vector3((float)random.NextDouble() * 71f + 3f,
                (float)random.NextDouble() * 67f + 7f, (float)random.NextDouble() * 79f + 11f);

            // All worker work is numerical and reads one immutable field. Texture
            // construction/upload stays on the caller (Unity main) thread.
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1)) };
            Parallel.For(0, depth, options, z =>
            {
                float pz = origin.z + (z + 0.5f) * step.z;
                for (int y = 0; y < height; y++)
                {
                    float py = origin.y + (y + 0.5f) * step.y;
                    byte normalizedHeight = ToByte((y + 0.5f) / height);
                    int row = (z * height + y) * width;
                    bool clearRow = z < 2 || z >= depth - 2 || y < 2 || y >= height - 2;
                    for (int x = 0; x < width; x++)
                    {
                        // Two empty texels on every face also keep clamp/trilinear
                        // sampling from revealing the volume's rectangular border.
                        pixels[row + x] = new Color32(0, 0, normalizedHeight, 255);
                        if (clearRow || x < 2 || x >= width - 2) continue;

                        Vector3 p = new Vector3(origin.x + (x + 0.5f) * step.x, py, pz);
                        float sdf = field.Evaluate(p);
                        if (sdf >= 0.2f) continue;

                        float middle = Noise01(Vector3.Scale(p, new Vector3(0.24f, 0.20f, 0.27f)) + offset);
                        float shell = Smooth01((sdf + 4.0f) / 4.2f);
                        // Work through a four-unit skin with coherent medium folds,
                        // then a lighter fine layer. The deeper core is untouched;
                        // this adds readable rolled edges, not uniform grain or
                        // low-frequency thinning of the whole cloud.
                        float folds = 0f, detail = 0f;
                        if (shell > 0f)
                        {
                            folds = Noise01(Vector3.Scale(p, new Vector3(0.56f, 0.46f, 0.62f)) + offset * 1.73f);
                            folds = Smooth01((folds - 0.25f) / 0.5f);
                            detail = Noise01(Vector3.Scale(p, new Vector3(1.03f, 0.88f, 1.12f)) + offset * 2.17f);
                        }
                        float erodedSdf = sdf + shell * (1.85f * folds + 0.50f * detail);
                        // A narrower -.55..+.20 field-unit transition preserves a
                        // clear organic edge after the large world-space box scale.
                        float density = 1f - Smooth01((erodedSdf + 0.55f) / 0.75f);
                        if (density <= 0.002f) continue;
                        density *= 0.86f + middle * 0.14f;

                        Vector3 stroke = new Vector3((p.x + p.y * 0.42f) * 0.32f,
                            (p.y - p.z * 0.12f) * 0.75f, (p.z - p.x * 0.22f) * 0.46f);
                        float brush = Noise01(stroke * 1.8f + offset * 1.31f);
                        brush = Mathf.Round(brush * 5f) * 0.2f;
                        pixels[row + x] = new Color32(ToByte(density), ToByte(brush), normalizedHeight, 255);
                    }
                }
            });

            var texture = new Texture3D(width, height, depth,
                GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "SculptedCloudDensity_" + seed,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float Noise01(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = Smooth01(p.x - x), fy = Smooth01(p.y - y), fz = Smooth01(p.z - z);
            float lower = Mathf.Lerp(Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx),
                Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx), fy);
            float upper = Mathf.Lerp(Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx),
                Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx), fy);
            return Mathf.Lerp(lower, upper, fz);
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
