using System.Collections;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
using InhaExpress.Client.Presentation;
using InhaExpress.Client.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class CozyOperatorWeatherTests
    {

        [UnityTest]
        public IEnumerator SelectionStaysFixedDespiteForecastAndOverlappingTransitions()
        {
            var prefab = Resources.Load<GameObject>("Cozy Prefabs/Cozy Weather Sphere");
            Assert.That(prefab, Is.Not.Null);
            GameObject weatherObject = null;
            var vehicleObject = new GameObject("cozy-test-vehicle");
            try
            {
                weatherObject = Object.Instantiate(prefab);
                var sphere = weatherObject.GetComponent<CozyWeather>();
                var source = PcLidarIntegration.Attach(vehicleObject.transform, "cozy-test",
                    new Bounds(new Vector3(0, .5f, 0), new Vector3(1, 1, 2)));
                yield return null;
                foreach (string weather in new[] { "rain", "snow", "fog", "clear" })
                {
                    var previousParticles = sphere.particleFXParent.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var particles in previousParticles) particles.Emit(3);
                    Assert.That(PhysicalLidarSource.ApplyEnvironment(weather, "wet"), Is.True);
                    foreach (var particles in previousParticles)
                        Assert.That(particles.particleCount, Is.Zero, "Previous weather must disappear on the selection frame.");
                    yield return null;
                    var bridge = weatherObject.GetComponent<CozyOperatorWeather>();
                    Assert.That(bridge, Is.Not.Null);
                    Assert.That(bridge.IsFixed, Is.True);
                    Assert.That(source.Capture(1).weather, Is.EqualTo(weather));
                    var ecosystem = sphere.weatherModule.ecosystem;
                    var selected = bridge.SelectedProfile;
                    Assert.That(selected, Is.Not.Null);
                    Assert.That(ecosystem.currentWeather, Is.SameAs(selected));
                    Assert.That(sphere.weatherModule.currentWeatherProfiles.Count, Is.EqualTo(1));
                    Assert.That(sphere.weatherModule.currentWeatherProfiles[0].weight, Is.EqualTo(1));
                    if (weather == "rain" || weather == "snow")
                    {
                        string effect = weather == "rain" ? "Light Rain Particle FX" : "Light Snow Particle FX";
                        var particles = sphere.GetFXRuntimeRef<CozyParticles>(effect);
                        Assert.That(particles, Is.Not.Null);
                        Assert.That(particles.GetComponentsInChildren<ParticleSystem>().Length, Is.GreaterThan(0));
                        if (weather == "snow")
                        {
                            foreach (var fx in selected.FX)
                            {
                                if (!(fx is ParticleFX snow)) continue;
                                var original = snow.particleSystem.GetComponentsInChildren<ParticleSystem>(true);
                                var enlarged = particles.GetComponentsInChildren<ParticleSystem>(true);
                                for (int i = 0; i < original.Length; i++)
                                    Assert.That(enlarged[i].main.startSizeMultiplier,
                                        Is.EqualTo(original[i].main.startSizeMultiplier * 1.8f).Within(.0001f));
                                float size = enlarged[0].main.startSizeMultiplier;
                                PhysicalLidarSource.ApplyEnvironment("snow", "wet");
                                Assert.That(enlarged[0].main.startSizeMultiplier, Is.EqualTo(size),
                                    "Repeated selections must not compound the snow size.");
                            }
                        }
                    }
                    var other = Resources.Load<WeatherProfile>("CampusWeather/" + (weather == "clear" ? "Rain" : "Clear"));
                    // Deliberately compete with the operator, including an already-running blend.
                    ecosystem.SetWeather(other, .05f);
                    ecosystem.weatherSelectionMode = CozyEcosystem.EcosystemStyle.random;
                    ecosystem.weatherTimer = -100;
                    yield return new WaitForSeconds(.2f);
                    Assert.That(ecosystem.currentWeather, Is.SameAs(selected));
                    Assert.That(ecosystem.weatherSelectionMode, Is.EqualTo(CozyEcosystem.EcosystemStyle.manual));
                    Assert.That(ecosystem.weightedWeatherProfiles.Count, Is.EqualTo(1));
                    Assert.That(ecosystem.currentForecast, Is.Empty);
                    Assert.That(bridge.IsFixed, Is.True);
                    Assert.That(PhysicalLidarSource.ReadEnvironment().visual_weather_status, Is.EqualTo("COZY 선택 날씨 고정"));
                }
            }
            finally
            {
                Object.DestroyImmediate(vehicleObject);
                if (weatherObject != null) Object.DestroyImmediate(weatherObject);
                PhysicalLidarSource.ApplyEnvironment("clear", "dry");
            }
            yield return null;
        }
    }
}
