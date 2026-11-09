using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai.Editor
{
    /// <summary>
    /// An eroded ridge behind the lodge, relative to (-8.05, 0, 36).
    /// The visible surface is a series of irregular cliff and scree sections.
    /// Its lower skirt bends into the original western crag.
    /// </summary>
    public static class SamuraiRidgeBackdropMesh
    {
        // X, crest height, crest depth, toe height, toe depth, gully depth, exposed rock.
        // Four broken crests frame the deep saddle behind the middle of the lodge.
        static readonly Section[] Sections =
        {
            new Section(-23.4f, 1.30f, 4.05f, -6.40f, -3.80f, .35f, .16f),
            new Section(-21.9f, 4.60f, 2.53f, -3.40f, -6.70f, .40f, .35f),
            new Section(-20.4f, 8.70f, 1.73f, -2.55f, -8.55f, .22f, .46f),
            new Section(-18.8f, 11.40f, 2.39f, -2.03f, -8.26f, .10f, .65f),
            new Section(-17.1f, 15.10f, 2.97f, -1.72f, -7.68f, .24f, .82f),
            new Section(-15.6f, 15.50f, 3.51f, -1.26f, -6.86f, .64f, .62f),
            new Section(-14.2f, 12.90f, 5.08f, -.71f, -7.86f, 2.12f, .24f),
            new Section(-12.8f, 16.90f, 5.72f, -.32f, -8.48f, 1.32f, .67f),
            new Section(-11.4f, 21.70f, 6.39f, .61f, -8.10f, .61f, .91f),
            new Section(-9.9f, 25.80f, 6.34f, 1.24f, -7.67f, .20f, .99f),
            new Section(-8.0f, 27.90f, 7.09f, 1.55f, -7.35f, .31f, .94f),
            new Section(-6.5f, 26.10f, 7.61f, 1.18f, -7.55f, .95f, .88f),
            new Section(-5.0f, 23.00f, 8.23f, 1.71f, -7.09f, 2.11f, .82f),
            new Section(-3.7f, 19.40f, 8.82f, 1.12f, -6.79f, 3.47f, .43f),
            new Section(-2.1f, 16.50f, 10.24f, .49f, -6.18f, 4.36f, .22f),
            new Section(-.7f, 17.15f, 11.17f, -.58f, -6.62f, 3.77f, .33f),
            new Section(1.1f, 18.65f, 10.53f, -2.88f, -6.42f, 2.64f, .57f),
            new Section(2.8f, 21.30f, 9.70f, -7.81f, -6.81f, 1.38f, .86f),
            new Section(4.6f, 22.95f, 8.89f, -10.56f, -7.25f, .44f, .98f),
            new Section(6.2f, 23.60f, 9.81f, -12.95f, -7.51f, .22f, .91f),
            new Section(8.1f, 21.65f, 10.17f, -14.58f, -7.23f, .80f, .77f),
            new Section(9.9f, 18.15f, 11.67f, -15.42f, -6.73f, 1.61f, .52f),
            new Section(11.5f, 15.95f, 12.63f, -14.13f, -5.96f, 2.75f, .26f),
            new Section(13.2f, 17.25f, 12.01f, -15.37f, -5.52f, 1.72f, .69f),
            new Section(14.8f, 15.20f, 11.49f, -16.28f, -5.17f, 1.08f, .71f),
            new Section(16.5f, 11.80f, 12.38f, -17.52f, -4.06f, .82f, .49f),
            new Section(18.2f, 8.70f, 13.16f, -18.29f, -2.48f, .53f, .54f),
            new Section(20.0f, 4.65f, 13.65f, -19.48f, -.98f, .42f, .38f),
            new Section(21.6f, 1.20f, 14.19f, -20.15f, 1.05f, .26f, .26f),
            new Section(23.0f, -3.60f, 14.97f, -21.00f, 3.52f, .12f, .17f)
        };

        // Close depth pairs expose cliff faces. Longer intervals leave eroded
        // shelves. Each cross section blends a different amount of loose slope.
        static readonly float[] FrontDepth = { .07f, .12f, .24f, .285f, .38f, .42f, .56f, .61f, .71f, .82f, .92f, 1f };
        static readonly float[] CliffRise = { .15f, .31f, .35f, .52f, .55f, .68f, .70f, .81f, .85f, .93f, .96f, 1f };
        static readonly float[] RearDepth = { .18f, .36f, .54f, .71f, .86f, 1f };
        static readonly float[] RearFall = { .09f, .25f, .48f, .69f, .89f, 1f };

        public static Mesh Build()
        {
            int rows = 2 + FrontDepth.Length + RearDepth.Length;
            var vertices = new List<Vector3>(Sections.Length * rows * 2 + 200);
            var indices = new List<int>(9000);
            foreach (Section section in Sections) AddVisibleSection(vertices, section);

            for (int column = 0; column < Sections.Length - 1; column++)
                for (int row = 0; row < rows - 1; row++)
                {
                    int a = column * rows + row;
                    int b = a + rows;
                    int c = b + 1;
                    int d = a + 1;
                    if ((column + row) % 2 == 0)
                        AddQuad(indices, a, d, c, b);
                    else
                    {
                        AddTriangle(indices, a, d, b);
                        AddTriangle(indices, b, d, c);
                    }
                }

            int topVertexCount = vertices.Count;
            int topIndexCount = indices.Count;
            int[] boundary = Outline(Sections.Length, rows);
            int firstSkirt = vertices.Count;
            foreach (int id in boundary) vertices.Add(SkirtPoint(vertices[id], false));
            int secondSkirt = vertices.Count;
            foreach (int id in boundary) vertices.Add(SkirtPoint(vertices[id], true));

            int bottom = vertices.Count;
            for (int i = 0; i < topVertexCount; i++)
            {
                Vector3 surface = vertices[i];
                float x = Mathf.Lerp(-11.6f, surface.x, .49f);
                float z = Mathf.Lerp(8.9f, surface.z, .55f);
                vertices.Add(new Vector3(x, -52.5f + .05f * x + .02f * z, z));
            }

            // Close the base with the same footprint triangulation as the top.
            // This retains the irregular perimeter without a fan across gullies.
            for (int i = 0; i < topIndexCount; i += 3)
                AddTriangle(indices, bottom + indices[i], bottom + indices[i + 2], bottom + indices[i + 1]);

            for (int i = 0; i < boundary.Length; i++)
            {
                int next = (i + 1) % boundary.Length;
                AddQuad(indices, boundary[i], boundary[next], firstSkirt + next, firstSkirt + i);
                AddQuad(indices, firstSkirt + i, firstSkirt + next, secondSkirt + next, secondSkirt + i);
                AddQuad(indices, secondSkirt + i, secondSkirt + next, bottom + boundary[next], bottom + boundary[i]);
            }

            var uv = new List<Vector2>(vertices.Count);
            foreach (Vector3 point in vertices) uv.Add(new Vector2(point.x, point.z) * .05f);
            var mesh = new Mesh { name = "RidgeLodge_BackdropMountain" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddVisibleSection(List<Vector3> vertices, Section section)
        {
            float x = section.X;
            float cliffBase = Mathf.Max(-6.75f, section.ToeZ + .72f) + section.Gully * .55f;
            float talusY = Mathf.Min(2.4f, Mathf.Lerp(section.ToeY, section.CrestY, .11f));
            vertices.Add(new Vector3(x, section.ToeY, section.ToeZ));
            vertices.Add(new Vector3(x, talusY, cliffBase));

            float stratumTilt = .17f * Mathf.Sin(x * .27f + .3f) + .09f * Mathf.Cos(x * .61f);
            float depthTilt = .13f * Mathf.Sin(x * .47f - .8f);
            for (int i = 0; i < FrontDepth.Length; i++)
            {
                float depth = FrontDepth[i];
                float rise = Mathf.Lerp(depth, CliffRise[i], section.Rock);
                rise += stratumTilt * rise * (1f - rise);
                float z = Mathf.Lerp(cliffBase, section.CrestZ, depth)
                    + (depthTilt * (section.CrestZ - cliffBase) + section.Gully * .44f) * depth * (1f - depth);
                vertices.Add(new Vector3(x, Mathf.Lerp(talusY, section.CrestY, rise), z));
            }

            float center = 1f - Mathf.Clamp01(Mathf.Abs(x + 2f) / 26f);
            float rearZ = Mathf.Max(section.CrestZ + 3.6f,
                Mathf.Lerp(18.2f, 22.7f, center) + .56f * Mathf.Sin(x * .57f) + .29f * Mathf.Sin(x * .99f));
            float rearY = Mathf.Min(section.ToeY - 2f, -4f - 10f * EastWeight(x));
            for (int i = 0; i < RearDepth.Length; i++)
            {
                float depth = RearDepth[i];
                float y = Mathf.Lerp(section.CrestY, rearY, RearFall[i])
                    + 1.45f * Mathf.Sin(x * .31f + 1.4f) * Mathf.Sin(depth * Mathf.PI);
                vertices.Add(new Vector3(x, y, Mathf.Lerp(section.CrestZ, rearZ, depth)));
            }
        }

        static float EastWeight(float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-4f, 10f, x));

        static Vector3 SkirtPoint(Vector3 top, bool deep)
        {
            float east = EastWeight(top.x);
            float firstY = Mathf.Min(top.y - 3.2f, Mathf.Lerp(-7f, -26.5f, east));
            if (deep)
                return new Vector3(Mathf.Lerp(-11.6f, top.x, .72f),
                    Mathf.Min(firstY - 2f, Mathf.Lerp(-23.5f, -37.5f, east)),
                    Mathf.Lerp(8.9f, top.z, .73f));

            var radial = new Vector2(top.x + 3.8f, top.z - 8f).normalized;
            float spread = .72f + .45f * Mathf.Sin(top.x * .51f + top.z * .73f);
            return new Vector3(top.x + radial.x * spread, firstY, top.z + radial.y * spread);
        }

        static int[] Outline(int columns, int rows)
        {
            var ids = new List<int>(2 * (columns + rows) - 4);
            for (int column = 0; column < columns; column++) ids.Add(column * rows);
            for (int row = 1; row < rows; row++) ids.Add((columns - 1) * rows + row);
            for (int column = columns - 2; column >= 0; column--) ids.Add(column * rows + rows - 1);
            for (int row = rows - 2; row > 0; row--) ids.Add(row);
            return ids.ToArray();
        }

        static void AddQuad(List<int> indices, int a, int b, int c, int d)
        {
            AddTriangle(indices, a, b, c);
            AddTriangle(indices, a, c, d);
        }

        static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        readonly struct Section
        {
            public readonly float X;
            public readonly float CrestY;
            public readonly float CrestZ;
            public readonly float ToeY;
            public readonly float ToeZ;
            public readonly float Gully;
            public readonly float Rock;

            public Section(float x, float crestY, float crestZ, float toeY, float toeZ, float gully, float rock)
            {
                X = x;
                CrestY = crestY;
                CrestZ = crestZ;
                ToeY = toeY;
                ToeZ = toeZ;
                Gully = gully;
                Rock = rock;
            }
        }
    }
}
