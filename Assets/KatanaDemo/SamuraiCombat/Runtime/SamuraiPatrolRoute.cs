using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    public enum SamuraiPatrolMode { Loop, PingPong }

    [Serializable]
    public sealed class SamuraiPatrolPoint
    {
        public Vector3 localPosition;
        [Min(0f)] public float waitSeconds;
        public bool useFacing;
        public float localYaw;
    }

    /// <summary>场景巡逻路线。节点相对独立路线根存储，不跟随小兵移动。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("OC Design/Samurai/Patrol Route")]
    public sealed class SamuraiPatrolRoute : MonoBehaviour
    {
        public List<SamuraiPatrolPoint> points = new List<SamuraiPatrolPoint>();
        public SamuraiPatrolMode mode = SamuraiPatrolMode.PingPong;
        public Color routeColor = new Color(.2f, .85f, 1f, 1f);

        public Vector3 GetWorldPosition(int index)
        {
            return points != null && index >= 0 && index < points.Count && points[index] != null
                ? transform.TransformPoint(points[index].localPosition) : transform.position;
        }

        public Vector3 GetWorldFacing(int index)
        {
            if (points == null || index < 0 || index >= points.Count || points[index] == null)
                return transform.forward;
            Vector3 facing = transform.rotation * Quaternion.Euler(0f, points[index].localYaw, 0f) * Vector3.forward;
            facing.y = 0f;
            return facing.sqrMagnitude > .0001f ? facing.normalized : Vector3.forward;
        }
    }
}
