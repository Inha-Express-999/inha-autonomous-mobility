using System;
using System.Collections.Generic;
using UnityEngine;

namespace InhaExpress.Simulation
{
    /// <summary>Reproducible parallel-lane crowd fixture; not campus crowd routing.</summary>
    public sealed class SyntheticPedestrianPopulation : MonoBehaviour
    {
        private readonly List<SyntheticPedestrianWalker> actors = new List<SyntheticPedestrianWalker>();
        public int Count => actors.Count;
        public IReadOnlyList<SyntheticPedestrianWalker> Actors => actors.AsReadOnly();

        public void SpawnLanes(SyntheticPedestrianWalker prefab, int count, Vector3 origin,
            int columns, float spacingM, float travelM, float speedMps)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (count < 0 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count));
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            if (!float.IsFinite(spacingM) || spacingM <= 0 ||
                !float.IsFinite(travelM) || travelM <= 0 ||
                !float.IsFinite(speedMps) || speedMps <= 0 ||
                !float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z))
                throw new ArgumentException("Fixture geometry and speed must be finite and positive.");
            // Validate the whole scenario before replacing a currently running population.
            var starts = new Vector3[count];
            var ends = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                starts[i] = origin + new Vector3((i % columns) * spacingM, 0, (i / columns) * spacingM);
                ends[i] = starts[i] + Vector3.forward * travelM;
                if (!float.IsFinite(ends[i].x) || !float.IsFinite(ends[i].z))
                    throw new ArgumentException("Fixture extent exceeds finite coordinates.");
            }
            Clear();
            for (int i = 0; i < count; i++)
            {
                var actor = Instantiate(prefab, starts[i], Quaternion.identity, transform);
                actor.name = "Synthetic pedestrian " + i.ToString("D4");
                actor.ConfigureSyntheticPath(new[] { ends[i] }, speedMps);
                actor.gameObject.SetActive(true);
                actors.Add(actor);
            }
        }

        public void SetPaused(bool value)
        {
            foreach (var actor in actors) if (actor != null) actor.SetPaused(value);
        }

        public void Clear()
        {
            foreach (var actor in actors)
            {
                if (actor == null) continue;
                actor.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(actor.gameObject);
                else DestroyImmediate(actor.gameObject);
            }
            actors.Clear();
        }

        private void OnDestroy() => Clear();
    }
}
