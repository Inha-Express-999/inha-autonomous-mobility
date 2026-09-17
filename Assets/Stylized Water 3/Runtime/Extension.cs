// Stylized Water 3 by Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//    • Copying or referencing source code for the production of new asset store, or public, content is strictly prohibited!
//    • Uploading this file to a public repository will subject it to an automated DMCA takedown request.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace StylizedWater3
{
    public class Extension
    {
        private static readonly Extension[] catalogue = new Extension[]
        {
            new Extension(ID.DynamicEffects, "Dynamic Effects", "Enables advanced effects to be projected onto the water surface. Such as boat wakes, ripples and shoreline waves.", 299321), 
            new Extension(ID.UnderwaterRendering, "Underwater Rendering", "Extends the shader with underwater rendering, by seamlessly blending the water with post processing effects.", 322081),
            new Extension(ID.RiverModeler, "River Modeler", "Create rivers using splines with foam FX and 3D audio.", 400836),
            //new Extension(ID.Physics, "Physics", "Buoyancy physics to make Rigidbodies float naturally.", 322081),
        };
        
        protected Extension(){}

        protected Extension(ID id, string name, string description, int assetStoreID)
        {
            this.id = id;
            this.name = name;
            this.description = description;
            this.assetStoreID = assetStoreID;
        }
        
        public enum ID
        {
            Unknown,
            DynamicEffects,
            UnderwaterRendering,
            RiverModeler,
            Physics,
            Simulations,
            OceanWaves
        }
        
        public struct DemoScene
        {
            public string guid;
            public string name;
            public string description;
            
            public DemoScene(string guid, string name, string description)
            {
                this.guid = guid;
                this.name = name;
                this.description = description;
            }
        }

        public ID id = ID.Unknown;
        public string name;
        public string description;
        public int assetStoreID;
        public string docUrl;

        public string[] packageDependencies = Array.Empty<string>();
        public DemoScene[] demoScenes = Array.Empty<DemoScene>();

        public Texture2D _icon;
        public Texture2D icon
        {
            get
            {
                if (!_icon) _icon = DownloadAssetIcon(assetStoreID);
                
                return _icon;
            }
            set => _icon = value;
        }

        public string version;
        public string minBaseVersion;
        
        private static Extension[] _installed;
        private static Extension[] _available;

        public static Extension[] installed
        {
            get
            {
                if (_installed == null) GetInstalled();
                return _installed;
            }
        }

        public static Extension[] available
        {
            get
            {
                if (_available == null) GetInstalled();
                return _available;
            }
        }

        //Deprecated since 3.2.9
        public void CreateIcon(string data) { }

        private static readonly Dictionary<int, Texture2D> DownloadedAssetIcons = new();
        private static readonly HashSet<int> AssetIconsBeingDownloaded = new();
        private static Texture2D TemporaryAssetIcon;
        
        public static Texture2D DownloadAssetIcon(int id)
        {
            #if UNITY_EDITOR
            if(id == 0) return TemporaryAssetIcon;
            
            if(Application.internetReachability == NetworkReachability.NotReachable) return TemporaryAssetIcon;
            
            if (DownloadedAssetIcons.TryGetValue(id, out Texture2D cachedIcon) && cachedIcon)
            {
                return cachedIcon;
            }

            if (!AssetIconsBeingDownloaded.Contains(id))
            {
                AssetIconsBeingDownloaded.Add(id);
                DownloadAssetIconAsync(id);
            }
            #endif

            return TemporaryAssetIcon;
        }
        
        #if UNITY_EDITOR
        private static async void DownloadAssetIconAsync(int id)
        {
            try
            {
                string url = $"https://api.assetstore.unity3d.com/affiliate/embed/package/{id}/icon";

                using UnityWebRequest request = UnityWebRequest.Get(url);

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }

                AssetIconsBeingDownloaded.Remove(id);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    //Debug.LogWarning($"Failed to download Asset Store icon for package {id}: {request.error}");
                    DownloadedAssetIcons[id] = TemporaryAssetIcon;
                    InternalEditorUtility.RepaintAllViews();
                    
                    return;
                }

                byte[] imageBytes = request.downloadHandler.data;
                Texture2D icon = new Texture2D(160, 160, TextureFormat.RGBA32, false, false);
                icon.LoadImage(imageBytes, true);

                if (icon)
                {
                    DownloadedAssetIcons[id] = icon;
                    InternalEditorUtility.RepaintAllViews();
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to download Asset Store icon for package {id}: {e.Message}");
            }
        }
        #endif
        
        #if UNITY_EDITOR
        [InitializeOnLoadMethod]
        #endif
        private static void GetInstalled()
        {
            var allTypes = new List<System.Type>();
#if UNITY_6000_4_OR_NEWER
            var assemblies = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
#endif
                
            foreach (var assembly in assemblies)
            {
                Type[] types = assembly.GetTypes();
                foreach (Type type in types)
                {
                    if (type.IsAbstract) continue;

                    if (type.IsSubclassOf(typeof(Extension)))
                        allTypes.Add(type);
                }
            }

            _installed = new Extension[allTypes.Count];
            for (int i = 0; i < allTypes.Count; i++)
            {
                installed[i] = Activator.CreateInstance(allTypes[i]) as Extension;
                //installed[i] = Convert.ChangeType(typeof(Extension), allTypes[i]) as Extension;
                
                //Debug.Log($"Found installed extension: {allTypes[i]}");
            }

            for (int i = 0; i < installed.Length; i++)
            {
                installed[i].Load();
            }
            
            List<Extension> notInstalledList = new List<Extension>();

            for (int i = 0; i < catalogue.Length; i++)
            {
                //Get installed
                Extension extension = IsInstalled(catalogue[i].id);

                //Not installed
                if (extension == null)
                {
                    notInstalledList.Add(catalogue[i]);
                }
            }
            
            _available = notInstalledList.ToArray();
            //Debug.Log($"{available.Length} extensions available");
        }
        
        public virtual void Load(){}

        public static Extension IsInstalled(ID id)
        {
            return installed.FirstOrDefault(e => e.id == id);
        }
    }
}