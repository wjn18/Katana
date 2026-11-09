using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Exterior temple tower, authored in local metres from the stone base.</summary>
    public static class SamuraiHallLandmarkTower
    {
        static readonly string[] Palette = { "Dark", "Timber", "Slate", "Stone", "Plaster", "Red", "Gold", "Roof", "Paper" };
        static readonly Stage[] Stages =
        {
            new Stage(2.37f, .66f, 4.46f, 3.55f, 1.94f, 4.40f, 5.69f, .30f),
            new Stage(2.02f, 5.44f, 8.58f, 3.04f, 1.58f, 8.50f, 9.85f, .26f),
            new Stage(1.66f, 9.62f, 12.40f, 2.57f, 1.25f, 12.31f, 13.56f, .23f),
            new Stage(1.33f, 13.33f, 16.05f, 2.16f, .23f, 15.96f, 17.56f, .20f)
        };
        static readonly float[] RoofRun = { 0f, .26f, .50f, .71f, .88f, 1f };
        static readonly float[] RoofRise = { 1f, .65f, .34f, .12f, -.022f, .025f };

        public static void Build(Action<string, Mesh, string> emit)
        {
            if (emit == null) throw new ArgumentNullException(nameof(emit));
            var groups = new Dictionary<string, Geometry>();
            foreach (string key in Palette) groups.Add(key, new Geometry());

            groups["Slate"].StoneCourse(2.90f, 0f, .23f, .20f);
            groups["Stone"].StoneCourse(2.79f, .23f, .50f, .17f);
            groups["Slate"].StoneCourse(2.68f, .50f, .66f, .15f);
            for (int i = 0; i < Stages.Length; i++)
            {
                BuildStage(groups, Stages[i], i);
                BuildRoof(groups["Roof"], Stages[i], i == Stages.Length - 1);
            }
            BuildSpire(groups);

            foreach (string key in Palette)
            {
                string name = "HallLandmarkTower_" + key;
                emit(name, groups[key].ToMesh(name), key);
            }
        }

        static void BuildStage(Dictionary<string, Geometry> groups, Stage stage, int index)
        {
            float half = stage.Half;
            float height = stage.Top - stage.Bottom;
            float post = index == 0 ? .29f : .23f;
            groups["Dark"].Box(new Vector3(0, stage.Bottom + .12f, 0), new Vector3(2f * half + .24f, .24f, 2f * half + .24f));
            groups["Timber"].Box(new Vector3(0, stage.Top - .26f, 0), new Vector3(2f * half + .32f, .22f, 2f * half + .32f));
            groups["Red"].Box(new Vector3(0, stage.Top - .10f, 0), new Vector3(2f * half + .21f, .19f, 2f * half + .21f));
            foreach (int x in new[] { -1, 1 })
                foreach (int z in new[] { -1, 1 })
                {
                    var corner = new Vector3(x * (half - .025f), 0, z * (half - .025f));
                    groups["Red"].Box(corner + Vector3.up * (stage.Bottom + height * .52f), new Vector3(post, height * .92f, post));
                    groups["Slate"].Box(corner + Vector3.up * (stage.Bottom + .22f), new Vector3(post + .10f, .19f, post + .10f));
                    groups["Timber"].Box(corner + Vector3.up * (stage.Top - .44f), new Vector3(post + .22f, .15f, post + .22f));
                }

            for (int face = 0; face < 4; face++)
            {
                Quaternion rotation = Quaternion.Euler(0, face * 90f, 0);
                BuildBay(groups, stage, index, face == 0 && index == 0, rotation);
                // Heavy bracket tails disappear into the timber frieze and project under the eave.
                for (int i = -2; i <= 2; i++)
                {
                    float x = i * half * .34f;
                    FaceBox(groups["Timber"], rotation, half, x, stage.EaveY - .23f, .27f, .13f, .16f, .66f);
                }
            }

            if (index != 0 && index != 2) return;
            // Small suspended corner fittings catch light without banding every roof edge in gold.
            foreach (int x in new[] { -1, 1 })
                foreach (int z in new[] { -1, 1 })
                {
                    float offset = stage.Outer - .19f;
                    float y = stage.EaveY + stage.Upturn - .40f;
                    var center = new Vector3(x * offset, y, z * offset);
                    groups["Dark"].Box(center + Vector3.up * .23f, new Vector3(.028f, .31f, .028f));
                    groups["Gold"].Turn(center, new[] { -.08f, -.04f, .065f }, new[] { .105f, .095f, .054f }, 8);
                }
        }

        static void BuildBay(Dictionary<string, Geometry> groups, Stage stage, int index, bool entrance, Quaternion rotation)
        {
            float half = stage.Half;
            float stageHeight = stage.Top - stage.Bottom;
            float opening = half * (entrance ? 1.01f : 1.10f);
            float lower = stage.Bottom + (entrance ? .20f : stageHeight * .29f);
            float upper = stage.Bottom + stageHeight * .79f;
            float centerY = (lower + upper) * .5f;
            float wallEnd = stage.Top - .29f;
            float sideWidth = half - opening * .5f;
            foreach (int sign in new[] { -1, 1 })
                FaceBox(groups["Plaster"], rotation, half, sign * (opening * .5f + sideWidth * .5f),
                    (stage.Bottom + wallEnd) * .5f, -.075f, sideWidth, wallEnd - stage.Bottom, .17f);
            FaceBox(groups["Plaster"], rotation, half, 0, (upper + wallEnd) * .5f, -.075f,
                opening, wallEnd - upper, .17f);
            FaceBox(groups["Timber"], rotation, half, 0, (stage.Bottom + lower) * .5f, -.065f,
                opening, lower - stage.Bottom, .18f);

            FaceBox(groups["Dark"], rotation, half, 0, centerY, -.20f, opening + .08f, upper - lower + .08f, .07f);
            float paperLow = entrance ? Mathf.Lerp(lower, upper, .51f) : lower;
            FaceBox(groups["Paper"], rotation, half, 0, (paperLow + upper) * .5f, -.103f,
                opening - .10f, upper - paperLow - .08f, .035f);

            foreach (int sign in new[] { -1, 1 })
            {
                FaceBox(groups["Red"], rotation, half, sign * opening * .5f, centerY, .012f, .13f, upper - lower + .15f, .13f);
                FaceBox(groups["Timber"], rotation, half, 0, sign < 0 ? lower : upper, .038f, opening + .17f, .11f, .16f);
            }
            int mullions = index < 2 ? 3 : 2;
            for (int i = 1; i <= mullions; i++)
            {
                float x = opening * (i / (mullions + 1f) - .5f);
                FaceBox(groups["Timber"], rotation, half, x, (paperLow + upper) * .5f, -.057f,
                    .043f, upper - paperLow, .06f);
            }
            FaceBox(groups["Timber"], rotation, half, 0, Mathf.Lerp(paperLow, upper, .48f), -.051f,
                opening, .049f, .063f);

            if (!entrance) return;
            FaceBox(groups["Stone"], rotation, half, 0, lower - .05f, .115f, opening + .30f, .14f, .39f);
            foreach (int sign in new[] { -1, 1 })
            {
                FaceBox(groups["Red"], rotation, half, sign * opening * .25f, (lower + paperLow) * .5f, -.055f,
                    opening * .5f - .023f, paperLow - lower, .10f);
                FaceBox(groups["Gold"], rotation, half, sign * .16f, Mathf.Lerp(lower, paperLow, .62f), .035f,
                    .085f, .14f, .073f);
            }
            FaceBox(groups["Timber"], rotation, half, 0, paperLow, .025f, opening, .10f, .14f);
        }

        static void FaceBox(Geometry geometry, Quaternion rotation, float half, float x, float y, float offset,
            float width, float height, float depth)
        {
            geometry.Box(rotation * new Vector3(x, y, -half - offset), new Vector3(width, height, depth), rotation);
        }

        static void BuildRoof(Geometry roof, Stage stage, bool cap)
        {
            const int perSide = 6;
            const int perimeter = perSide * 4;
            int start = roof.Vertices.Count;
            int skin = perimeter * RoofRun.Length;
            for (int layer = 0; layer < 2; layer++)
                for (int ring = 0; ring < RoofRun.Length; ring++)
                {
                    float run = RoofRun[ring];
                    float radius = Mathf.Lerp(stage.Inner, stage.Outer, run);
                    for (int i = 0; i < perimeter; i++)
                    {
                        float t = (i % perSide) / (float)perSide;
                        float y = stage.EaveY + (stage.RidgeY - stage.EaveY) * RoofRise[ring]
                            + stage.Upturn * run * run * Mathf.Pow(Mathf.Abs(2f * t - 1f), 2.6f)
                            - .19f * layer;
                        roof.Vertices.Add(SquarePoint(radius, y, i / perSide, t));
                    }
                }

            for (int ring = 0; ring < RoofRun.Length - 1; ring++)
                for (int i = 0; i < perimeter; i++)
                {
                    int j = (i + 1) % perimeter;
                    int a = start + ring * perimeter + i;
                    int b = start + ring * perimeter + j;
                    int c = b + perimeter;
                    int d = a + perimeter;
                    roof.Quad(a, b, c, d);
                    roof.Quad(a + skin, d + skin, c + skin, b + skin);
                }
            int rim = start + (RoofRun.Length - 1) * perimeter;
            for (int i = 0; i < perimeter; i++)
            {
                int j = (i + 1) % perimeter;
                roof.Quad(rim + i, rim + j, rim + j + skin, rim + i + skin);
                if (!cap) roof.Quad(start + j, start + i, start + i + skin, start + j + skin);
            }
            if (!cap) return;

            int topCenter = roof.Vertices.Count;
            roof.Vertices.Add(new Vector3(0, stage.RidgeY, 0));
            int bottomCenter = roof.Vertices.Count;
            roof.Vertices.Add(new Vector3(0, stage.RidgeY - .19f, 0));
            for (int i = 0; i < perimeter; i++)
            {
                int j = (i + 1) % perimeter;
                roof.Triangle(topCenter, start + j, start + i);
                roof.Triangle(bottomCenter, start + skin + i, start + skin + j);
            }
        }

        static Vector3 SquarePoint(float radius, float y, int side, float t)
        {
            float along = Mathf.Lerp(-radius, radius, t);
            switch (side)
            {
                case 0: return new Vector3(along, y, -radius);
                case 1: return new Vector3(radius, y, along);
                case 2: return new Vector3(-along, y, radius);
                default: return new Vector3(-radius, y, -along);
            }
        }

        static void BuildSpire(Dictionary<string, Geometry> groups)
        {
            groups["Slate"].Turn(Vector3.zero, new[] { 17.48f, 17.58f, 17.72f }, new[] { .32f, .32f, .21f }, 12);
            groups["Dark"].Turn(Vector3.zero, new[] { 17.65f, 20.62f }, new[] { .083f, .047f }, 10);
            groups["Gold"].Turn(Vector3.zero, new[] { 17.66f, 17.76f, 17.89f, 17.97f }, new[] { .22f, .25f, .16f, .15f }, 12);
            for (int i = 0; i < 7; i++)
            {
                float y = 18.02f + i * .33f;
                float radius = Mathf.Lerp(.45f, .24f, i / 6f);
                groups["Gold"].Turn(Vector3.up * y, new[] { 0, .028f, .075f, .099f },
                    new[] { radius * .82f, radius, radius, radius * .79f }, 12);
            }
            groups["Gold"].Turn(Vector3.zero, new[] { 20.20f, 20.30f, 20.48f, 20.69f, 20.91f, 21.08f },
                new[] { .11f, .17f, .27f, .18f, .09f, .019f }, 12);
        }

        readonly struct Stage
        {
            public readonly float Half, Bottom, Top, Outer, Inner, EaveY, RidgeY, Upturn;

            public Stage(float half, float bottom, float top, float outer, float inner, float eaveY, float ridgeY, float upturn)
            {
                Half = half;
                Bottom = bottom;
                Top = top;
                Outer = outer;
                Inner = inner;
                EaveY = eaveY;
                RidgeY = ridgeY;
                Upturn = upturn;
            }
        }

        sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            readonly List<int> indices = new List<int>();

            public void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                int a = Vertices.Count;
                Vector3 h = size * .5f;
                foreach (float y in new[] { -h.y, h.y })
                {
                    Vertices.Add(center + rotation * new Vector3(-h.x, y, -h.z));
                    Vertices.Add(center + rotation * new Vector3(h.x, y, -h.z));
                    Vertices.Add(center + rotation * new Vector3(h.x, y, h.z));
                    Vertices.Add(center + rotation * new Vector3(-h.x, y, h.z));
                }
                Quad(a, a + 1, a + 2, a + 3);
                Quad(a + 4, a + 7, a + 6, a + 5);
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Quad(a + 4 + i, a + 4 + j, a + j, a + i);
                }
            }

            public void StoneCourse(float half, float bottom, float top, float cut)
            {
                int start = Vertices.Count;
                foreach (float y in new[] { bottom, top })
                {
                    Vertices.Add(new Vector3(-half + cut, y, -half));
                    Vertices.Add(new Vector3(half - cut, y, -half));
                    Vertices.Add(new Vector3(half, y, -half + cut));
                    Vertices.Add(new Vector3(half, y, half - cut));
                    Vertices.Add(new Vector3(half - cut, y, half));
                    Vertices.Add(new Vector3(-half + cut, y, half));
                    Vertices.Add(new Vector3(-half, y, half - cut));
                    Vertices.Add(new Vector3(-half, y, -half + cut));
                }
                CloseTurns(start, 8, 2, new Vector3(0, bottom, 0), new Vector3(0, top, 0));
            }

            public void Turn(Vector3 center, float[] heights, float[] radii, int segments)
            {
                int start = Vertices.Count;
                for (int row = 0; row < heights.Length; row++)
                    for (int i = 0; i < segments; i++)
                    {
                        float angle = i * (2f * Mathf.PI / segments);
                        Vertices.Add(center + new Vector3(radii[row] * Mathf.Cos(angle), heights[row], radii[row] * Mathf.Sin(angle)));
                    }
                CloseTurns(start, segments, heights.Length, center + Vector3.up * heights[0], center + Vector3.up * heights[heights.Length - 1]);
            }

            void CloseTurns(int start, int segments, int rows, Vector3 lower, Vector3 upper)
            {
                for (int row = 0; row < rows - 1; row++)
                    for (int i = 0; i < segments; i++)
                    {
                        int a = start + row * segments + i;
                        int b = start + row * segments + (i + 1) % segments;
                        Quad(a, a + segments, b + segments, b);
                    }
                int bottom = Vertices.Count;
                Vertices.Add(lower);
                int top = Vertices.Count;
                Vertices.Add(upper);
                int last = start + (rows - 1) * segments;
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    Triangle(bottom, start + i, start + j);
                    Triangle(top, last + j, last + i);
                }
            }

            public void Quad(int a, int b, int c, int d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public void Triangle(int a, int b, int c)
            {
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Vertices);
                mesh.SetTriangles(indices, 0);
                var uv = new List<Vector2>(Vertices.Count);
                foreach (Vector3 vertex in Vertices) uv.Add(new Vector2(vertex.x + vertex.z, vertex.y));
                mesh.SetUVs(0, uv);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
