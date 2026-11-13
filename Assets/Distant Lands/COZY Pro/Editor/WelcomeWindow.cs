using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
public class WelcomeWindow : EditorWindow
{
    private const string CozyWelcomeWindowKey = "COZY3_WelcomeWindowShown";

    VisualElement root;
    VisualElement ModuleGrid => root.Q<VisualElement>("module-grid");

    // Static constructor to run on editor load
    static WelcomeWindow()
    {
        // Only show if not already shown
        if (!EditorPrefs.GetBool(CozyWelcomeWindowKey, false))
        {
            // Delay call to ensure Unity is ready
            EditorApplication.update += ShowOnFirstImport;
        }
    }

    private static void ShowOnFirstImport()
    {
        EditorApplication.update -= ShowOnFirstImport;
        ShowExample();
        EditorPrefs.SetBool(CozyWelcomeWindowKey, true);
    }
    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;

    [MenuItem("Tools/Cozy: Stylized Weather 3/Welcome to COZY Pro")]
    public static void ShowExample()
    {
        WelcomeWindow wnd = GetWindow<WelcomeWindow>();
        wnd.titleContent = new GUIContent("Welcome to COZY Pro");
        wnd.Show();
    }

    public void CreateGUI()
    {
        root = rootVisualElement;
        m_VisualTreeAsset.CloneTree(root);

        ModuleGrid.Add(ModuleRow("COZY Core", "Get started in seconds with the base COZY package. Required for all other modules.", "https://assetstore.unity.com/packages/tools/utilities/cozy-stylized-weather-3-271742?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.core"));
        ModuleGrid.Add(ModuleRow("Plume", "Add billowing volumetric clouds to your scene.", "https://assetstore.unity.com/packages/tools/particles-effects/cozy-plume-volumetric-clouds-module-243905?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.plume"));
        ModuleGrid.Add(ModuleRow("Blocks", "Add additional control options for atmospheres.", "https://assetstore.unity.com/packages/tools/utilities/cozy-blocks-preset-based-atmosphere-module-238051?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.blocks"));
        ModuleGrid.Add(ModuleRow("Habits", "Extend calendar and event control with a visual calendar.", "https://assetstore.unity.com/packages/tools/utilities/cozy-habits-extended-calendar-module-259983?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.habits"));
        ModuleGrid.Add(ModuleRow("ReSound", "Control a dynamic soundtrack engine.", "https://assetstore.unity.com/packages/tools/audio/cozy-resound-adaptive-soundtrack-module-278334?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.resound"));
        ModuleGrid.Add(ModuleRow("Eclipse", "Add dynamic solar eclipses to your world.", "https://assetstore.unity.com/packages/vfx/shaders/cozy-eclipse-sun-occlusion-module-277785?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.eclipse"));
        ModuleGrid.Add(ModuleRow("Horizon", "Textural skybox layers that dynamically adjust to your atmosphere.", "https://assetstore.unity.com/packages/vfx/shaders/cozy-horizon-sky-layers-module-307494?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.horizon"));
        ModuleGrid.Add(ModuleRow("Link", "Control COZY across multiplayer instances.", "https://assetstore.unity.com/packages/tools/network/cozy-link-multiplayer-module-238669?aid=1011luj9X", "https://github.com/DistantLandsProductions/com.distantlands.cozy.link"));
    }


    public VisualElement ModuleRow(string extensionTitle, string description, string downloadURL, string githubURL)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("module-row");

        VisualElement icon = new VisualElement();
        icon.AddToClassList("icon");
        icon.style.backgroundImage = Resources.Load(extensionTitle) as Texture2D;
        row.Add(icon);

        VisualElement content = new VisualElement();
        row.Add(content);

        Label title = new Label(extensionTitle);
        title.name = "title";
        content.Add(title);

        Label desc = new Label(description);
        desc.name = "desc";
        content.Add(desc);

        VisualElement buttonRow = new VisualElement();
        buttonRow.name = "btn-row";
        content.Add(buttonRow);

        Button UAS = new Button(() => Application.OpenURL(downloadURL));
        UAS.text = "Download";
        UAS.AddToClassList("btn");
        UAS.AddToClassList("primary");
        buttonRow.Add(UAS);

        Button GitHub = new Button(() => Application.OpenURL(githubURL));
        GitHub.text = "GitHub";
        GitHub.AddToClassList("btn");
        buttonRow.Add(GitHub);



        return row;
    }
}
