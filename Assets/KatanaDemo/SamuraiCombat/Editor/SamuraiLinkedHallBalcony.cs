using System;
using UnityEngine;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Two storeys at the west end of the linked hall and its one-way drop.</summary>
    public static class SamuraiLinkedHallBalcony
    {
        const float Ground = 1.3671f;
        const float Deck = 5.30f;
        const float CenterZ = 7.20f;

        public static void Build(Action<string, string, Vector3, Vector3, Quaternion, bool> box)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));

            void Add(string name, string palette, float x, float y, float z,
                float width, float height, float depth, bool collision = false)
            {
                box(name, palette, new Vector3(x, y, z), new Vector3(width, height, depth), Quaternion.identity, collision);
            }

            void Inlay(string name, Vector3 from, Vector3 to)
            {
                Vector3 direction = to - from;
                box(name, "Gold", (from + to) * .5f, new Vector3(.026f, .008f, direction.magnitude),
                    Quaternion.LookRotation(direction, Vector3.up), false);
            }

            Add("Drop balcony walking deck", "Timber", -16.225f, Deck - .11f, CenterZ, 1.15f, .22f, 2.70f, true);
            Add("West balcony edge fascia", "Dark", -16.775f, Deck - .20f, CenterZ, .05f, .17f, 2.68f);
            Add("West balcony brass edge pinstripe", "Gold", -16.796f, Deck - .14f, CenterZ, .012f, .018f, 2.64f);
            for (int i = -3; i <= 3; i++)
                Add("Balcony plank seam " + (i + 4), "Dark", -16.225f, Deck + .002f,
                    CenterZ + (i + .5f) * .34f, 1.11f, .004f, .009f);

            for (int i = 0; i < 2; i++)
            {
                float x = -16.24f - i * .26f;
                var tip = new Vector3(x - .18f, Deck + .005f, CenterZ);
                Inlay("Westward floor inlay " + (i + 1) + " south", new Vector3(x, tip.y, CenterZ - .19f), tip);
                Inlay("Westward floor inlay " + (i + 1) + " north", new Vector3(x, tip.y, CenterZ + .19f), tip);
            }

            void RailPost(string name, float x, float z)
            {
                Add(name, "Timber", x, Deck + .43f, z, .12f, .86f, .12f, true);
                Add(name + " stone socket", "Slate", x, Deck + .065f, z, .155f, .13f, .155f);
                Add(name + " dark cap", "Dark", x, Deck + .89f, z, .15f, .065f, .15f);
                Add(name + " warm inset", "Gold", x, Deck + .816f, z, .125f, .025f, .125f);
            }

            void Rails(string name, float x, float z, float width, float depth)
            {
                Add(name + " handrail", "Dark", x, Deck + .79f, z, width, .12f, depth, true);
                Add(name + " lower rail", "Timber", x, Deck + .40f, z, width, .10f, depth, true);
                Add(name + " foot curb", "Slate", x, Deck + .06f, z, width, .12f, depth);
            }

            foreach (float z in new[] { 5.93f, 8.47f })
            {
                string side = z < CenterZ ? "South" : "North";
                Rails(side + " balcony rail", -16.225f, z, 1.15f, .13f);
                RailPost(side + " east rail post", -15.72f, z);
                RailPost(side + " west corner post", -16.73f, z);
            }
            Rails("South short west return", -16.73f, 6.065f, .13f, .39f);
            Rails("North short west return", -16.73f, 8.335f, .13f, .39f);
            RailPost("South drop opening post", -16.73f, 6.215f);
            RailPost("North drop opening post", -16.73f, 8.185f);

            Add("Lower room flush stone floor", "Stone", -15.10f, Ground - .09f, CenterZ, 2.90f, .18f, 2.70f, true);
            Add("Lower room threshold inlay", "Slate", -16.405f, Ground + .003f, CenterZ, .28f, .006f, 1.80f);
            Add("Lower room timber ceiling", "Dark", -15.10f, 4.99f, CenterZ, 2.90f, .18f, 2.70f, true);

            float wallHeight = 4.91f - Ground;
            float wallCenter = Ground + wallHeight * .5f;
            Add("Lower room solid east backing", "Plaster", -13.74f, wallCenter, CenterZ, .18f, wallHeight, 2.70f, true);
            foreach (float z in new[] { 5.93f, 8.47f })
            {
                string side = z < CenterZ ? "South" : "North";
                Add(side + " lower plaster wall", "Plaster", -15.10f, wallCenter, z, 2.90f, wallHeight, .16f, true);
                Add(side + " sandstone plinth", "Stone", -15.10f, Ground + .14f, z, 2.90f, .28f, .17f);
                Add(side + " timber sill", "Timber", -15.10f, Ground + .32f, z, 2.89f, .09f, .17f);
                Add(side + " timber crown", "Dark", -15.10f, 4.78f, z, 2.89f, .22f, .18f);
            }

            // Two jambs and the lintel enclose the facade around a through opening.
            foreach (float z in new[] { 6.05f, 8.35f })
            {
                string side = z < CenterZ ? "South" : "North";
                Add(side + " west plaster cheek", "Plaster", -16.43f, wallCenter, z, .18f, wallHeight, .40f, true);
                Add(side + " west stone plinth", "Stone", -16.43f, Ground + .14f, z, .22f, .28f, .40f);
            }
            foreach (float z in new[] { 6.245f, 8.155f })
            {
                string side = z < CenterZ ? "South" : "North";
                Add(side + " doorway timber jamb", "Timber", -16.42f, 2.61f, z, .25f, 2.49f, .13f, true);
                Add(side + " doorway cut stone foot", "Slate", -16.42f, Ground + .10f, z, .255f, .20f, .15f);
                Add(side + " red jamb band", "Red", -16.42f, 3.68f, z, .252f, .09f, .133f);
            }
            Add("West door structural lintel", "Dark", -16.42f, 3.82f, CenterZ, .25f, .24f, 2.70f, true);
            Add("West timber and plaster transom backing", "Plaster", -16.43f, 4.425f, CenterZ, .18f, .97f, 2.70f, true);
            Add("West warm paper transom", "Paper", -16.532f, 4.33f, CenterZ, .027f, .38f, 1.76f);
            Add("West paper transom lower lintel", "Timber", -16.52f, 4.105f, CenterZ, .06f, .08f, 1.89f);
            Add("West paper transom upper lintel", "Timber", -16.52f, 4.555f, CenterZ, .06f, .08f, 1.89f);
            for (int i = -2; i <= 2; i++)
                Add("West paper transom muntin " + (i + 3), "Timber", -16.535f, 4.33f,
                    CenterZ + i * .36f, .027f, .41f, .048f);
            Add("West warm transom inlay", "Gold", -16.543f, 4.11f, CenterZ, .011f, .022f, 1.76f);

            foreach (float x in new[] { -16.41f, -13.79f })
                foreach (float z in new[] { 5.99f, 8.41f })
                {
                    string corner = (x < -15f ? "West " : "East ") + (z < CenterZ ? "south" : "north");
                    Add(corner + " timber pier", "Timber", x, 3.21f, z, .19f, 3.36f, .19f);
                    Add(corner + " pier sandstone foot", "Stone", x, Ground + .17f, z, .28f, .34f, .28f);
                    Add(corner + " pier capital", "Dark", x, 4.76f, z, .245f, .22f, .245f);
                }

            // Warm inset and horizontal framing give the open lower room a visible back wall.
            Add("Lower room east oak frame", "Timber", -13.865f, 2.89f, CenterZ, .07f, 1.41f, 1.80f);
            Add("Lower room east warm paper inset", "Paper", -13.91f, 2.89f, CenterZ, .024f, 1.21f, 1.60f);
            for (int i = -1; i <= 1; i++)
                Add("East panel timber divider " + (i + 2), "Timber", -13.93f, 2.89f,
                    CenterZ + i * .40f, .024f, 1.22f, .042f);
            Add("East panel brass strip", "Gold", -13.929f, 2.32f, CenterZ, .02f, .025f, 1.60f);
        }
    }
}
