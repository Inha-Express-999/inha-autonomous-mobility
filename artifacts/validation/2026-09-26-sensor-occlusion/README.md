# Sensor actor hierarchy and occlusion

2026-09-26, Unity 6000.3.21f1 EditMode Physics: 17/17 passed.
Two added cases place ego and another actor below the same scene grouping parent.
The ego child collider is ignored, while the sibling collider is observed and
classified as PEDESTRIAN or VEHICLE from its configured layer. Adding an opaque
wall closer to the sensor replaces all those narrow-FOV returns with wall returns.
No hidden entity classification is emitted. Source hashes and XML are preserved.

The prior transform.root filter incorrectly excluded every sibling sharing the
scene group. It now excludes only descendants of the rig's own actor root.
The rig/reporter components must remain on that actor root as in supplied prefabs.

These are synthetic colliders with simulation-tag classification, not a pedestrian
movement model, learned recognition, 300-agent load test, full scene/Player run or
end-to-end transport test of these exact hierarchy cases. Earlier network evidence
has its own source manifests. No performance claim is made.
