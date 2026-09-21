using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Presentation
{
    public sealed class AdditiveSceneLoader : MonoBehaviour
    {
        [SerializeField] private string scenePath;

        public void Configure(string path)
        {
            scenePath = path;
        }

        private IEnumerator Start()
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                throw new InvalidOperationException("An additive scene path is not configured.");
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
