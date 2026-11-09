using System.Collections.Generic;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Runtime adapter: the operator owns weather selection, COZY owns visual FX.</summary>
    [DisallowMultipleComponent]
    public sealed class CozyOperatorWeather : MonoBehaviour
    {
        private static readonly List<CozyOperatorWeather> bindings = new List<CozyOperatorWeather>();
        private static string selectedWeather;
        private CozyWeather sphere;
        private WeatherProfile selectedProfile;
        private string appliedWeather;
        private bool warned;
        private readonly HashSet<ParticleSystem> enlargedSnow = new HashSet<ParticleSystem>();

        public WeatherProfile SelectedProfile => selectedProfile;
        public bool IsFixed => isActiveAndEnabled && sphere != null && sphere.isActiveAndEnabled &&
            sphere.weatherModule != null && Matches(sphere.weatherModule.ecosystem);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBindings()
        {
            bindings.Clear(); selectedWeather = null;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (selectedWeather != null) BindExisting();
        }

        public static void Apply(string weather)
        {
            selectedWeather = weather;
            BindExisting();
            foreach (var binding in bindings) binding.Select(weather);
        }

        private static void BindExisting()
        {
            bindings.RemoveAll(binding => binding == null);
            foreach (var weather in FindObjectsByType<CozyWeather>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var binding = weather.GetComponent<CozyOperatorWeather>();
                if (binding == null) binding = weather.gameObject.AddComponent<CozyOperatorWeather>();
                if (!bindings.Contains(binding)) bindings.Add(binding);
                binding.sphere = weather;
                binding.Select(selectedWeather);
            }
        }

        public static string Status
        {
            get
            {
                bindings.RemoveAll(binding => binding == null);
                int active = 0, fixedCount = 0;
                foreach (var binding in bindings)
                {
                    if (!binding.isActiveAndEnabled) continue;
                    active++;
                    if (binding.IsFixed) fixedCount++;
                }
                return active == 0 ? "COZY 미연결" : fixedCount == active ? "COZY 선택 날씨 고정" : "COZY 적용 확인 필요";
            }
        }

        private void OnEnable()
        {
            sphere = GetComponent<CozyWeather>();
            // Runs before COZY's weather weights and FX phase, even when time advances.
            CozyWeather.OnFrameReset += Enforce;
        }

        private void OnDisable() => CozyWeather.OnFrameReset -= Enforce;
        private void OnDestroy() => bindings.Remove(this);

        private void Select(string weather)
        {
            if (appliedWeather == weather && selectedProfile != null) { Enforce(); return; }
            string name = weather switch { "clear" => "Clear", "rain" => "Rain", "snow" => "Snow", "fog" => "Fog", _ => null };
            var profile = name == null ? null : Resources.Load<WeatherProfile>("CampusWeather/" + name);
            if (profile == null)
            {
                if (!warned) Debug.LogWarning("COZY operator weather profile is missing: " + weather, this);
                warned = true; selectedProfile = null; return;
            }
            if (selectedProfile != null && Application.isPlaying) selectedProfile.SetWeatherWeight(0);
            // COZY Stop() stops emission but lets living particles expire naturally.
            // Clear only this sphere's weather particles, before starting the new FX.
            if (Application.isPlaying && sphere != null && sphere.particleFXParent != null)
                foreach (var particles in sphere.particleFXParent.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            selectedProfile = profile; appliedWeather = weather; warned = false;
            if (Application.isPlaying && sphere != null)
            {
                foreach (var fx in selectedProfile.FX) if (fx != null) fx.InitializeEffect(sphere);
                if (weather == "snow")
                    foreach (var fx in selectedProfile.FX)
                    {
                        if (!(fx is ParticleFX)) continue;
                        var runtime = sphere.GetFXRuntimeRef<CozyParticles>(fx.name);
                        if (runtime == null) continue;
                        foreach (var particles in runtime.GetComponentsInChildren<ParticleSystem>(true))
                            if (enlargedSnow.Add(particles))
                            {
                                var main = particles.main;
                                main.startSizeMultiplier *= 1.8f;
                            }
                    }
            }
            Enforce();
        }

        private bool Matches(CozyEcosystem ecosystem)
        {
            if (selectedProfile == null || ecosystem == null) return false;
            var weights = ecosystem.weightedWeatherProfiles;
            return ecosystem.weatherSelectionMode == CozyEcosystem.EcosystemStyle.manual &&
                ecosystem.currentWeather == selectedProfile && ecosystem.weatherChangeCheck == selectedProfile &&
                weights != null && weights.Count == 1 && weights[0].profile == selectedProfile &&
                weights[0].weight == 1 && !weights[0].transitioning && ecosystem.currentForecast.Count == 0;
        }

        private void Lock(CozyEcosystem ecosystem)
        {
            if (ecosystem == null || Matches(ecosystem)) return;
            // Detach old blend relations rather than stopping unrelated COZY coroutines.
            if (Application.isPlaying)
                foreach (var previous in ecosystem.weightedWeatherProfiles)
                    if (previous.profile != null && previous.profile != selectedProfile) previous.profile.SetWeatherWeight(0);
            ecosystem.weatherSelectionMode = CozyEcosystem.EcosystemStyle.manual;
            ecosystem.currentForecast.Clear();
            ecosystem.weatherTimer = 0;
            ecosystem.currentWeather = ecosystem.weatherChangeCheck = selectedProfile;
            ecosystem.weightedWeatherProfiles = new List<WeatherRelation>
            {
                new WeatherRelation { profile = selectedProfile, weight = 1, transitioning = false }
            };
        }

        private void Enforce()
        {
            if (!isActiveAndEnabled || selectedProfile == null || sphere == null || sphere.weatherModule == null) return;
            Lock(sphere.weatherModule.ecosystem);
            // Biomes must not blend a different forecast into the globally selected weather.
            foreach (var biome in sphere.weatherModule.biomes)
                if (biome is CozyWeatherModule weatherBiome) Lock(weatherBiome.ecosystem);
        }
    }
}
