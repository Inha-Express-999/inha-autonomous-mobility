using System;
using System.Collections;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.MapClient;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InhaExpress.Client.Presentation
{
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private string worldScenePath = "Assets/CampusSim/Scenes/CampusWorld.unity";
        [SerializeField] private string roleScenePath;
        [SerializeField] private ClientRole clientRole;
        [SerializeField] private bool useFixture = true;
        [SerializeField] private bool autoStartLocalServer = true;

        private static ClientBootstrap instance;
        private bool isLoading;
        public ClientRuntimeHost Runtime { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        public string WorldScenePath => worldScenePath;
        public string RoleScenePath => roleScenePath;

        private void Awake()
        {
#if UNITY_EDITOR
            // MCP queries and Editor tools must not stall the mobile bootstrap between frames.
            Application.runInBackground = true;
#elif UNITY_STANDALONE
            // The shared world and transport must keep updating while the web dashboard has focus.
            if (clientRole == ClientRole.PC_Operator) Application.runInBackground = true;
#endif
            if (instance != null && instance != this)
            {
                enabled = false;
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (isLoading)
            {
                yield break;
            }

            isLoading = true;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--mobile-preview") >= 0)
            {
                clientRole = ClientRole.Mobile_Passenger;
                roleScenePath = "Assets/CampusSim/Scenes/Mobile_Passenger.unity";
                useFixture = false;
            }
            if (!useFixture && autoStartLocalServer && clientRole == ClientRole.PC_Operator)
            {
                var launcher = gameObject.AddComponent<DesktopServerLauncher>();
                yield return launcher.EnsureRunning();
            }
            yield return LoadAdditiveSceneIfNeeded(worldScenePath);
            yield return LoadAdditiveSceneIfNeeded(roleScenePath);
            if (!useFixture)
            {
                var hud = gameObject.AddComponent<WebClientHud>();
                hud.Initialize(clientRole == ClientRole.Mobile_Passenger);
                yield break;
            } // No implicit mock fallback after a real transport failure.
            Runtime = gameObject.AddComponent<ClientRuntimeHost>();
            string subscriber = clientRole == ClientRole.Mobile_Passenger ? FixtureScenario.PassengerId : null;
            Runtime.Initialize(clientRole, new FixtureClientDataSource(clientRole, Application.version, subscriber), subscriber);
            int boundViews = 0;
            foreach (var root in SceneManager.GetSceneByPath(roleScenePath).GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is IClientView view)
                    {
                        view.Bind(Runtime);
                        boundViews++;
                    }
            if (boundViews == 0) throw new InvalidOperationException("The role scene has no client view. Configure Fixture HUD first.");
            Runtime.StartSource();
        }

        private void OnDestroy() { if (instance == this) instance = null; }

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

#if UNITY_EDITOR
            var operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
            var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
#endif
            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Unable to load '{scenePath}'. Add the scene to the target build profile.");
            }

            yield return operation;
        }
    }
}
