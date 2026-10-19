using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using System.Collections.Generic;

namespace OCDesign.Combat
{
    /// <summary>重击预计接地前向前蔓延的地裂与低尘，只观察战斗和碰撞。</summary>
    [DefaultExecutionOrder(210), DisallowMultipleComponent]
    public sealed class CombatLandingImpact : MonoBehaviour
    {
        [SerializeField] private CombatAnimationDriver driver;
        [SerializeField] private CombatCharacterMotor motor;
        [SerializeField] private CharacterController capsule;
        [SerializeField] private CombatAttackDefinition heavyAttack;
        [SerializeField, FormerlySerializedAs("ringMaterial")] private Material fractureMaterial;
        [SerializeField] private Material dustMaterial;
        [SerializeField, Range(1f, 5f)] private float fractureLength = 3.2f;
        [SerializeField, Range(0.1f, 0.6f)] private float propagationDuration = 0.28f;
        [SerializeField, Range(0.6f, 1.2f)] private float fractureDuration = 1.08f;
        [SerializeField, FormerlySerializedAs("landingDelaySeconds"), Range(0.01f, 0.25f)]
        [Tooltip("预计实际接地前的提前量；60fps下提前6帧。")]
        private float landingLeadSeconds = 6f / 60f;
        [SerializeField, Range(0.2f, 1.2f)] private float dustDuration = 0.70f;
        [SerializeField] private LayerMask groundMask = ~0;

        private const int DustCount = 10;
        private readonly Vector3[] dustVertices = new Vector3[DustCount * 4];
        private readonly Vector2[] dustUV = new Vector2[DustCount * 4];
        private readonly Color[] dustColors = new Color[DustCount * 4];
        private GameObject fractureObject, dustObject;
        private Mesh fractureMesh, dustMesh;
        private MeshRenderer fractureRenderer, dustRenderer;
        private readonly List<Vector3> fractureVertices = new List<Vector3>(256);
        private readonly List<Vector2> fractureUV = new List<Vector2>(256);
        private readonly List<Color> fractureColors = new List<Color>(256);
        private readonly List<int> fractureTriangles = new List<int>(384);
        private Camera viewCamera;
        private uint watchedSerial;
        private bool watching, seenAir, consumed, previousGrounded;
        private float previousProgress;
        private float age = -1f;
        private Vector3 impactNormal = Vector3.up, impactRight = Vector3.right, impactForward = Vector3.forward;

        public int TriggerCount { get; private set; }
        public uint LastImpactSerial { get; private set; }
        public float LastImpactNormalizedTime { get; private set; }
        public float LastGroundContactNormalizedTime { get; private set; }
        public float LandingLeadSeconds => landingLeadSeconds;
        public Vector3 LastImpactPoint { get; private set; }
        public Vector3 LastImpactForward => impactForward;
        public float PropagationDistance { get; private set; }
        public float FractureLength => fractureLength;
        public bool CrackVisible => fractureRenderer && fractureRenderer.enabled;
        public bool RingVisible => false; // 兼容旧观察工具；当前效果不再绘制圆环。
        public bool DustVisible => dustRenderer && dustRenderer.enabled;
        public bool IsVisible => CrackVisible || DustVisible;

        public void Configure(CombatAnimationDriver animationDriver, CombatCharacterMotor characterMotor,
            CharacterController controller, CombatAttackDefinition definition, Material fracture, Material dust)
        {
            ClearEffect(); watching = false;
            driver = animationDriver; motor = characterMotor; capsule = controller;
            heavyAttack = definition; fractureMaterial = fracture; dustMaterial = dust;
        }

        private void LateUpdate()
        {
            bool active = heavyAttack && driver && driver.isActiveAndEnabled && driver.Target
                && driver.Target.isActiveAndEnabled && driver.CurrentAttack == heavyAttack
                && motor && motor.isActiveAndEnabled && capsule && capsule.enabled;
            if (!active) { watching = false; ClearEffect(); return; }
            bool grounded = capsule.isGrounded;
            float progress = driver.CurrentAttackNormalizedTime;
            if (!watching || watchedSerial != driver.AttackSerial)
            {
                ClearEffect(); watching = true; watchedSerial = driver.AttackSerial;
                seenAir = false; consumed = false; previousGrounded = grounded;
                previousProgress = progress; LastGroundContactNormalizedTime = 0f;
            }
            float normalizedRate = Mathf.Max(0f, (progress - previousProgress) / Mathf.Max(.00001f, Time.deltaTime));
            previousProgress = progress;
            // 顶碰取消本次资格；即使之后在落斩时间落地，也不误作一次成功重砸。
            if ((motor.LastCollisionFlags & CollisionFlags.Above) != 0) { consumed = true; ClearEffect(); }
            if (!consumed && !grounded && motor.IsAttackAirborne) seenAir = true;
            if (!consumed && seenAir && !grounded && progress >= heavyAttack.DamageWindowStartNormalized
                && motor.TryPredictAttackLanding(landingLeadSeconds, normalizedRate, groundMask,
                    out RaycastHit predictedGround, out float predictedSeconds))
            {
                consumed = true;
                Emit(predictedGround);
            }
            if (!previousGrounded && grounded && seenAir)
            {
                // 实际接地仅补记验证时刻，不能再补发第二次或接地后等待。
                LastGroundContactNormalizedTime = progress;
                consumed = true;
            }
            previousGrounded = grounded;
            if (age < 0f) return;
            DrawImpact();
            age += Time.deltaTime;
            if (age > Mathf.Max(fractureDuration, dustDuration)) ClearEffect();
        }

        private void Emit(RaycastHit ground)
        {
            if (!fractureMaterial || !dustMaterial) return;
            LastImpactPoint = ground.point;
            impactNormal = ground.normal;
            impactForward = Vector3.ProjectOnPlane(transform.forward, impactNormal).normalized;
            if (impactForward.sqrMagnitude < 0.1f) impactForward = Vector3.ProjectOnPlane(Vector3.forward, impactNormal).normalized;
            impactRight = Vector3.Cross(impactNormal, impactForward).normalized;
            LastImpactSerial = watchedSerial; LastImpactNormalizedTime = driver.CurrentAttackNormalizedTime;
            // 发射帧就生成短裂缝，避免零长度网格把实际可见时刻再推迟一帧。
            TriggerCount++; age = Mathf.Min(Time.deltaTime, 1f / 60f); EnsureMeshes();
        }

        private MeshRenderer CreateRenderer(string name, Mesh mesh, Material material, out GameObject obj)
        {
            obj = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            obj.transform.SetParent(transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false; return renderer;
        }

        private void EnsureMeshes()
        {
            if (fractureMesh) return;
            fractureMesh = new Mesh { name = "Heavy forward ground fracture", hideFlags = HideFlags.DontSave };
            fractureMesh.MarkDynamic();
            fractureRenderer = CreateRenderer("Heavy forward ground fracture (runtime)", fractureMesh, fractureMaterial, out fractureObject);
            dustMesh = new Mesh { name = "Heavy landing dust", hideFlags = HideFlags.DontSave }; dustMesh.MarkDynamic();
            int[] triangles = new int[DustCount * 6];
            for (int i = 0; i < DustCount; i++)
            {
                int k = i*4,j=i*6; dustUV[k]=Vector2.zero; dustUV[k+1]=Vector2.right; dustUV[k+2]=Vector2.one; dustUV[k+3]=Vector2.up;
                triangles[j]=k;triangles[j+1]=k+1;triangles[j+2]=k+2;triangles[j+3]=k;triangles[j+4]=k+2;triangles[j+5]=k+3;
            }
            dustMesh.vertices=dustVertices;dustMesh.uv=dustUV;dustMesh.colors=dustColors;dustMesh.triangles=triangles;
            dustRenderer = CreateRenderer("Heavy landing low dust (runtime)",dustMesh,dustMaterial,out dustObject);
            viewCamera = Camera.main;
        }

        // 固定折线按距离揭示，避免整张贴花缩放造成裂缝滑动。所有分叉都朝前延伸。
        private static readonly Vector2[][] Strokes =
        {
            new[] { new Vector2(0,.12f),new Vector2(-.06f,.28f),new Vector2(.11f,.43f),new Vector2(.02f,.63f),new Vector2(.18f,.82f),new Vector2(-.04f,1.04f),new Vector2(.08f,1.25f),new Vector2(-.14f,1.52f),new Vector2(.10f,1.73f),new Vector2(-.04f,1.98f),new Vector2(.09f,2.20f),new Vector2(-.09f,2.45f),new Vector2(.04f,2.67f),new Vector2(-.04f,2.94f),new Vector2(.03f,3.20f) },
            new[] { new Vector2(.02f,.63f),new Vector2(-.17f,.76f),new Vector2(-.38f,.82f),new Vector2(-.44f,1.03f),new Vector2(-.62f,1.13f),new Vector2(-.72f,1.40f) },
            new[] { new Vector2(-.38f,.82f),new Vector2(-.62f,.90f),new Vector2(-.76f,1.02f) },
            new[] { new Vector2(.11f,.43f),new Vector2(.34f,.57f),new Vector2(.45f,.76f),new Vector2(.60f,.84f) },
            new[] { new Vector2(-.14f,1.52f),new Vector2(-.37f,1.67f),new Vector2(-.50f,1.90f),new Vector2(-.72f,1.98f),new Vector2(-.94f,2.23f) },
            new[] { new Vector2(.10f,1.73f),new Vector2(.35f,1.91f),new Vector2(.47f,2.04f),new Vector2(.72f,2.22f),new Vector2(.88f,2.34f) },
            new[] { new Vector2(-.09f,2.45f),new Vector2(-.32f,2.59f),new Vector2(-.42f,2.86f) },
            new[] { new Vector2(.04f,2.67f),new Vector2(.24f,2.84f),new Vector2(.39f,3.08f) },
            new[] { new Vector2(-.06f,.28f),new Vector2(-.24f,.34f),new Vector2(-.35f,.55f) }
        };

        private Vector2 StrokePoint(Vector2 point) => new Vector2(point.x * 1.25f, point.y * fractureLength / 3.2f);

        private Vector2 StrokeEdge(Vector2[] stroke, int index, float width)
        {
            Vector2 p = StrokePoint(stroke[index]);
            Vector2 before = index > 0 ? (p - StrokePoint(stroke[index - 1])).normalized : (StrokePoint(stroke[1]) - p).normalized;
            Vector2 after = index < stroke.Length - 1 ? (StrokePoint(stroke[index + 1]) - p).normalized : before;
            Vector2 normal = new Vector2(-before.y, before.x), other = new Vector2(-after.y, after.x);
            Vector2 miter = (normal + other).normalized;
            float taper = Mathf.Lerp(1f, .06f, Mathf.Pow((float)index / (stroke.Length - 1), 2f));
            float irregular = .83f + .17f * Mathf.Sin(index * 4.7f + .4f);
            return miter * (width * .5f * taper * irregular / Mathf.Max(.45f, Vector2.Dot(miter, normal)));
        }

        private void DrawFracture()
        {
            PropagationDistance = fractureLength * Mathf.Clamp01(age / propagationDuration);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fractureDuration * .676f, fractureDuration, age));
            Matrix4x4 local = fractureObject.transform.worldToLocalMatrix;
            fractureVertices.Clear(); fractureUV.Clear(); fractureColors.Clear(); fractureTriangles.Clear();
            for (int s = 0; s < Strokes.Length; s++)
            {
                Vector2[] stroke = Strokes[s];
                float width = s == 0 ? .26f : (s == 2 || s == 8 ? .07f : .14f);
                for (int i = 0; i < stroke.Length - 1; i++)
                {
                    Vector2 a = StrokePoint(stroke[i]), b = StrokePoint(stroke[i + 1]);
                    if (a.y >= PropagationDistance) break;
                    Vector2 edgeA = StrokeEdge(stroke, i, width), edgeB = StrokeEdge(stroke, i + 1, width);
                    if (b.y > PropagationDistance)
                    {
                        b = Vector2.Lerp(a, b, Mathf.InverseLerp(a.y, b.y, PropagationDistance));
                        edgeB = Vector2.zero;
                    }
                    int first = fractureVertices.Count;
                    AddFractureVertex(a - edgeA, a.y, 0f, fade, local);
                    AddFractureVertex(a + edgeA, a.y, 1f, fade, local);
                    AddFractureVertex(b + edgeB, b.y, 1f, fade, local);
                    AddFractureVertex(b - edgeB, b.y, 0f, fade, local);
                    fractureTriangles.Add(first); fractureTriangles.Add(first + 1); fractureTriangles.Add(first + 2);
                    fractureTriangles.Add(first); fractureTriangles.Add(first + 2); fractureTriangles.Add(first + 3);
                }
            }
            fractureMesh.Clear(); fractureMesh.SetVertices(fractureVertices); fractureMesh.SetUVs(0, fractureUV);
            fractureMesh.SetColors(fractureColors); fractureMesh.SetTriangles(fractureTriangles, 0);
            fractureMesh.RecalculateBounds(); fractureRenderer.enabled = age < fractureDuration;
        }

        private void AddFractureVertex(Vector2 point, float distance, float across, float alpha, Matrix4x4 local)
        {
            Vector3 world = LastImpactPoint + impactRight * point.x + impactForward * point.y + impactNormal * .018f;
            fractureVertices.Add(local.MultiplyPoint3x4(world));
            fractureUV.Add(new Vector2(distance * 2.7f, across));
            fractureColors.Add(new Color(1f, 1f, 1f, alpha));
        }

        private void DrawImpact()
        {
            DrawFracture();
            if (!viewCamera) viewCamera = Camera.main;
            Vector3 right = viewCamera ? viewCamera.transform.right : impactRight;
            Vector3 up = viewCamera ? viewCamera.transform.up : Vector3.up;
            // 顶点每帧从固定世界落点换算，角色随后位移不会拖着尘土走。
            Matrix4x4 local = dustObject.transform.worldToLocalMatrix;
            for (int i = 0; i < DustCount; i++)
            {
                float seed = Mathf.Repeat(Mathf.Sin(i * 12.9898f + 2.73f) * 43758.5453f, 1f);
                float distance = Mathf.Lerp(.22f, fractureLength * .94f, (float)i / (DustCount - 1));
                float birth = i == 0 ? 0f : propagationDuration * distance / fractureLength;
                float d = Mathf.Clamp01((age - birth) / Mathf.Max(.2f, dustDuration - birth));
                float side = (i % 2 == 0 ? -1f : 1f) * (.08f + seed * .16f + d * .18f);
                float size = (.13f + seed * .08f) * (1f + d * .7f);
                float height = size * .35f + Mathf.Sin(d * Mathf.PI) * (.04f + seed * .05f);
                Vector3 center = LastImpactPoint + impactForward * distance + impactRight * side + impactNormal * height;
                Vector3 x = right * size, y = up * size * .65f;
                int k=i*4;
                dustVertices[k]=local.MultiplyPoint3x4(center-x-y);dustVertices[k+1]=local.MultiplyPoint3x4(center+x-y);
                dustVertices[k+2]=local.MultiplyPoint3x4(center+x+y);dustVertices[k+3]=local.MultiplyPoint3x4(center-x+y);
                float alpha = age < birth ? 0f : Mathf.Pow(1f - d, 1.7f) * (.45f + seed * .25f);
                for(int v=0;v<4;v++)dustColors[k+v]=new Color(1f,1f,1f,alpha);
            }
            dustMesh.vertices=dustVertices;dustMesh.colors=dustColors;dustMesh.RecalculateBounds();
            dustRenderer.enabled = age < dustDuration;
        }

        public void ClearEffect()
        {
            age = -1f; PropagationDistance = 0f;
            if (fractureRenderer) fractureRenderer.enabled = false;
            if (dustRenderer) dustRenderer.enabled = false;
        }
        private void OnDisable() { watching = false; ClearEffect(); }
        private void OnDestroy()
        {
            foreach (var obj in new Object[] { fractureObject,dustObject,fractureMesh,dustMesh })
                if(obj){if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
        }
    }
}
