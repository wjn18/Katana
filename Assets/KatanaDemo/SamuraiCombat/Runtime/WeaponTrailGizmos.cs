using System.Diagnostics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OCDesign.Samurai
{
    /// <summary>可移除的武器判定观察组件；仅绘制Scene视图，不参与检测或战斗结算。</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponTrailGizmos : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] Transform weaponBase;
        [SerializeField] Transform weaponTip;
        [SerializeField, Min(.01f)] float bladeRadius = .13f;
        [Header("武器判定可视化 · 仅 Scene")]
        [Tooltip("只在Scene场景视图绘制，不显示在Game视图，也不进入游戏构建。")]
        [SerializeField] bool showWeaponTrailGizmos = true;
        [Tooltip("保留最近有效窗口的查询轨迹；暂停Play时保留画面。黄色是查询范围，不代表全部产生伤害。")]
        [SerializeField, Range(.1f, 3f)] float weaponTrailGizmoSeconds = .8f;

        struct TrailGizmo
        {
            public WeaponTrailSweep sweep;
            public float time;
        }
        struct ContactGizmo
        {
            public Vector3 point;
            public float time;
            public SamuraiHitResult result;
        }
        readonly TrailGizmo[] trailGizmos = new TrailGizmo[256];
        readonly ContactGizmo[] contactGizmos = new ContactGizmo[32];
        int trailGizmoCount, trailGizmoNext, contactGizmoCount, contactGizmoNext;
#endif

        public void Configure(Transform bladeBase, Transform bladeTip, float radius)
        {
#if UNITY_EDITOR
            weaponBase = bladeBase;
            weaponTip = bladeTip;
            bladeRadius = Mathf.Max(.01f, radius);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public void RecordSweep(WeaponTrailSweep sweep)
        {
#if UNITY_EDITOR
            if (!showWeaponTrailGizmos || !isActiveAndEnabled) return;
            sweep.Radius = Mathf.Max(.01f, sweep.Radius);
            trailGizmos[trailGizmoNext] = new TrailGizmo { sweep = sweep, time = Time.time };
            trailGizmoNext = (trailGizmoNext + 1) % trailGizmos.Length;
            trailGizmoCount = Mathf.Min(trailGizmoCount + 1, trailGizmos.Length);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public void RecordContact(Vector3 point, SamuraiHitResult result)
        {
#if UNITY_EDITOR
            if (!showWeaponTrailGizmos || !isActiveAndEnabled || result == SamuraiHitResult.Ignored) return;
            contactGizmos[contactGizmoNext] = new ContactGizmo { point = point, result = result, time = Time.time };
            contactGizmoNext = (contactGizmoNext + 1) % contactGizmos.Length;
            contactGizmoCount = Mathf.Min(contactGizmoCount + 1, contactGizmos.Length);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public void ClearHistory()
        {
#if UNITY_EDITOR
            trailGizmoCount = trailGizmoNext = contactGizmoCount = contactGizmoNext = 0;
#endif
        }

#if UNITY_EDITOR
        void OnDisable() => ClearHistory();

        void OnDrawGizmos()
        {
            // Game视图即使打开Gizmos也不画；只接受SceneView的绘制相机。
            if (Camera.current == null || Camera.current.cameraType != CameraType.SceneView
                || !showWeaponTrailGizmos || !isActiveAndEnabled
                || weaponBase == null || weaponTip == null) return;
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousHandleColor = Handles.color;
            try
            {
                Gizmos.matrix = Matrix4x4.identity;
                float radius = Mathf.Max(.01f, bladeRadius);
                Vector3 currentBase = weaponBase.position, currentTip = weaponTip.position;
                Gizmos.color = new Color(0f, 1f, 1f, .85f);
                DrawTrailCapsule(currentBase, currentTip, radius);
                Gizmos.DrawSphere(currentBase, .025f);
                Handles.color = Color.cyan;
                Handles.Label(currentBase + Vector3.up * .06f, "Base");
                Gizmos.color = new Color(.3f, .55f, 1f);
                Gizmos.DrawSphere(currentTip, .03f);
                Handles.color = new Color(.45f, .7f, 1f);
                Handles.Label(currentTip + Vector3.up * .06f, "Tip");

                float lifetime = Mathf.Max(.1f, weaponTrailGizmoSeconds);
                for (int i = 0; i < trailGizmoCount; i++)
                {
                    int index = (trailGizmoNext - 1 - i + trailGizmos.Length) % trailGizmos.Length;
                    TrailGizmo trail = trailGizmos[index];
                    float age = Time.time - trail.time;
                    if (!Application.isPlaying || age > lifetime) continue;
                    WeaponTrailSweep sweep = trail.sweep;
                    float fade = 1f - Mathf.Clamp01(age / lifetime);
                    Gizmos.color = new Color(1f, .78f, .08f, .15f + .6f * fade);
                    Gizmos.DrawLine(sweep.FromBase, sweep.FromTip);
                    Gizmos.DrawLine(sweep.ToBase, sweep.ToTip);
                    float length = Mathf.Max(Vector3.Distance(sweep.FromBase, sweep.FromTip),
                        Vector3.Distance(sweep.ToBase, sweep.ToTip));
                    int lanes = Mathf.Max(1, Mathf.CeilToInt(length / sweep.Radius));
                    for (int lane = 0; lane <= lanes; lane++)
                    {
                        float t = (float)lane / lanes;
                        Vector3 start = Vector3.Lerp(sweep.FromBase, sweep.FromTip, t);
                        Vector3 finish = Vector3.Lerp(sweep.ToBase, sweep.ToTip, t);
                        Gizmos.DrawLine(start, finish);
                        // 最近一次查询显示完整厚度，历史仅画中心轨迹。
                        if (i == 0) DrawTrailCapsule(start, finish, sweep.Radius);
                    }
                }
                for (int i = 0; i < contactGizmoCount; i++)
                {
                    int index = (contactGizmoNext - 1 - i + contactGizmos.Length) % contactGizmos.Length;
                    ContactGizmo hit = contactGizmos[index];
                    if (!Application.isPlaying || Time.time - hit.time > lifetime) continue;
                    bool damage = hit.result == SamuraiHitResult.Hit || hit.result == SamuraiHitResult.Killed
                        || hit.result == SamuraiHitResult.GuardBroken;
                    Gizmos.color = damage ? new Color(1f, .15f, .1f) : new Color(.25f, 1f, .35f);
                    Gizmos.DrawSphere(hit.point, .045f);
                    Gizmos.DrawWireSphere(hit.point, .09f);
                    Handles.color = Gizmos.color;
                    Handles.Label(hit.point + Vector3.up * .12f, hit.result.ToString());
                }
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
                Handles.color = previousHandleColor;
            }
        }

        static void DrawTrailCapsule(Vector3 start, Vector3 end, float radius)
        {
            Gizmos.DrawWireSphere(start, radius);
            Gizmos.DrawWireSphere(end, radius);
            Vector3 direction = end - start;
            if (direction.sqrMagnitude < .000001f) return;
            direction.Normalize();
            Vector3 right = Vector3.Cross(direction, Mathf.Abs(direction.y) > .9f ? Vector3.right : Vector3.up).normalized * radius;
            Vector3 up = Vector3.Cross(direction, right).normalized * radius;
            Gizmos.DrawLine(start + right, end + right);
            Gizmos.DrawLine(start - right, end - right);
            Gizmos.DrawLine(start + up, end + up);
            Gizmos.DrawLine(start - up, end - up);
        }
#endif
    }
}
