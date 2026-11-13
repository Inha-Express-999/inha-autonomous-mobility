using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Fixed-step crossing along the fixture's sampled ground surface.</summary>
    public sealed class SyntheticPedestrianWalker : MonoBehaviour
    {
        private Rigidbody body;
        private Vector3 destination;
        private float speed;
        private float expires;
        private bool arrived;
        private float beginWalking;
        private bool walking;
        private Vector3[] path;
        private int waypoint;
        public bool IsWaiting => !walking && !arrived;

        public void Initialize(Rigidbody value, Vector3 end, float walkingSpeed, float waitSeconds = 0,
            Vector3[] walkingPath = null)
        {
            body = value;
            destination = end;
            speed = walkingSpeed;
            path = walkingPath;
            waypoint = path != null && path.Length > 1 ? 1 : 0;
            beginWalking = Time.time + Mathf.Max(0, waitSeconds);
            walking = waitSeconds <= 0;
            foreach (var animator in GetComponentsInChildren<Animator>())
            {
                animator.SetBool("walk", walking);
                // Initialize the waiting pose on the spawn frame, including prefab overrides.
                int idle = Animator.StringToHash("Base Layer.Idle 1");
                if (!walking && animator.HasState(0, idle)) animator.Play(idle, 0, 0);
            }
            Vector3 direction = end - body.position;
            direction.y = 0;
            if (direction.sqrMagnitude > .001f) body.rotation = Quaternion.LookRotation(direction);
            expires = beginWalking + direction.magnitude / speed + 10;
        }

        private void FixedUpdate()
        {
            if (body == null) return;
            if (Time.time >= expires) { Destroy(gameObject); return; }
            if (arrived) return;
            if (Time.time < beginWalking) return;
            if (!walking)
            {
                walking = true;
                foreach (var animator in GetComponentsInChildren<Animator>()) animator.SetBool("walk", true);
            }
            var position = body.position;
            float remaining = speed * Time.fixedDeltaTime;
            while (remaining > 0)
            {
                var target = path != null && waypoint < path.Length ? path[waypoint] : destination;
                float distance = Vector3.Distance(position, target);
                if (distance > remaining)
                {
                    position = Vector3.MoveTowards(position, target, remaining);
                    break;
                }
                position = target;
                remaining -= distance;
                if (path == null || ++waypoint >= path.Length) break;
            }
            body.MovePosition(position);
            if (Vector3.Distance(position, destination) > .05f) return;
            arrived = true;
            foreach (var animator in GetComponentsInChildren<Animator>()) animator.SetBool("walk", false);
            expires = Time.time + 2;
        }
    }
}
