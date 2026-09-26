using System;
using System.Collections.Generic;
using UnityEngine;

namespace InhaExpress.Simulation
{
    /// <summary>
    /// Unity-owned synthetic pedestrian motion. No networking or planner reference.
    /// Paths/dimensions are scenario assumptions, not approved campus walk routes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class SyntheticPedestrianWalker : MonoBehaviour
    {
        private Rigidbody body;
        private Vector3[] path = Array.Empty<Vector3>();
        private int nextPoint;
        private float speedMps;
        private bool paused;

        public bool Completed => path.Length > 0 && nextPoint >= path.Length;
        public bool Paused => paused;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            int layer = LayerMask.NameToLayer("Pedestrian");
            if (layer >= 0) gameObject.layer = layer;
        }

        public void ConfigureSyntheticPath(IReadOnlyList<Vector3> waypoints, float metresPerSecond)
        {
            if (waypoints == null || waypoints.Count == 0)
                throw new ArgumentException("A pedestrian path is required.", nameof(waypoints));
            if (!float.IsFinite(metresPerSecond) || metresPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(metresPerSecond));
            var copy = new Vector3[waypoints.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                var point = waypoints[i];
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                    throw new ArgumentException("Path coordinates must be finite.", nameof(waypoints));
                copy[i] = point;
            }
            if (body == null) Awake();
            path = copy;
            speedMps = metresPerSecond;
            nextPoint = 0;
            paused = false;
        }

        public void SetPaused(bool value) => paused = value;

        private void FixedUpdate()
        {
            if (body == null || paused || nextPoint >= path.Length) return;
            var position = body.position;
            float remaining = speedMps * Time.fixedDeltaTime;
            // Finite one-way paths bound work even with repeated waypoints.
            while (nextPoint < path.Length)
            {
                var offset = path[nextPoint] - position;
                float distance = offset.magnitude;
                if (distance > remaining)
                {
                    position += offset * (remaining / distance);
                    break;
                }
                position = path[nextPoint++];
                remaining -= distance;
            }
            body.MovePosition(position);
        }
    }
}
