using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Presentation
{
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private string worldScenePath = "Assets/CampusSim/Scenes/CampusWorld.unity";
        [SerializeField] private string roleScenePath;

        private bool isLoading;

        public string WorldScenePath => worldScenePath;
        public string RoleScenePath => roleScenePath;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (isLoading)
            {
                yield break;
            }

            isLoading = true;
            yield return LoadAdditiveSceneIfNeeded(worldScenePath);
            yield return LoadAdditiveSceneIfNeeded(roleScenePath);
        }

        public void Configure(string worldPath, string rolePath)
        {
            worldScenePath = worldPath;
            roleScenePath = rolePath;
        }

        private static IEnumerator LoadAdditiveSceneIfNeeded(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                throw new InvalidOperationException("A bootstrap scene path is not configured.");
            }

            if (SceneManager.GetSceneByPath(scenePath).isLoaded)
            {
                yield break;
            }

            var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Unable to load '{scenePath}'. Add the scene to the target build profile.");
            }

            yield return operation;
        }
    }
}
