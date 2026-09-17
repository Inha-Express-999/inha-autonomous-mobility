// Stylized Water 3 by Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//    • Copying or referencing source code for the production of new asset store, or public, content is strictly prohibited!
//    • Uploading this file to a public repository will subject it to an automated DMCA takedown request.

using System;
using UnityEditor;
using UnityEngine;

namespace StylizedWater3
{
    public static class FogIntegration
    {
        public enum Assets
        {
            None,
            [InspectorName("Default Unity")]
            UnityFog,
            #if SWS_DEV
            [InspectorName("Colorful Sky")] //Unreleased
            Colorful,
            #endif
            [InspectorName("COZY: Stylized Weather v3")]
            COZY,
            AtmosphericHeightFog,
            [InspectorName("Enviro 3 - Sky & Weather")]
            Enviro3,
            [InspectorName("Azure[Sky] Dynamic Skybox")]
            Azure,
            [InspectorName("Buto (2022+)")]
            Buto,
            [InspectorName("SC Post Effects")]
            SCPostEffects
        }

        [Serializable]
        public class Integration
        {
            public string name;
            public Assets asset;
            public int id;
            public string libraryGUID;
            public string url;
            public bool underwaterCompatible;
            public Texture2D _thumbnail;
            public Texture2D thumbnail => _thumbnail ??= UI.Icons.DownloadAssetIcon(id);
            public bool installed;
            public bool includeWithPragmas;

            public Integration(string name, Assets asset, int id, bool includeWithPragmas, bool uwc, string guid, string url)
            {
                this.name = name;
                this.asset = asset;
                this.id = id;
                this.underwaterCompatible = uwc;
                this.includeWithPragmas = includeWithPragmas;
                this.libraryGUID = guid;
                this.url = url;
            }
        }

        private static Integration[] _Integrations;
        public static Integration[] Integrations
        {
            get
            {
                if (_Integrations == null) _Integrations = GetAvailableIntegrations();
                return _Integrations;
            }
        }

        private static Integration[] GetAvailableIntegrations()
        {
            Integration[] integrationsArray = new[]
            {
                new Integration("None", Assets.None, 0,true, true,"", ""),
                new Integration("Default Unity", Assets.UnityFog, 0,true, true, "", ""),
                #if SWS_DEV
                new Integration("Colorful Sky", Assets.Colorful, 0,true, true,"4a93911cc8864a3ba9f264d522fdfa3a", ""),
                #endif
                new Integration("SC Post Effects (Screen-Space Fog)", Assets.SCPostEffects, 377136, true, true, "a66e4b0e5c776404e9a091531ccac3f2", "https://assetstore.unity.com/packages/slug/377136"),
                new Integration("COZY: Stylized Weather v3", Assets.COZY, 271742, true, true,"e9bc566e199a97947a3e600f2563fe85", "https://assetstore.unity.com/packages/slug/271742"),
                new Integration("Atmospheric Height Fog", Assets.AtmosphericHeightFog, 143825,false, true, "8db8edf9bba0e9d48998019ca6c2f9ff", "https://assetstore.unity.com/packages/slug/143825"),
                new Integration("Enviro 3 - Sky & Weather", Assets.Enviro3, 236601, true, true, "8db9bd7b531f93d46ae2cb21180a00a8", "https://assetstore.unity.com/packages/slug/236601"),
                new Integration("Azure[Sky] Dynamic Skybox", Assets.Azure, 36050, true, true, "5da6209b193d6eb49b25c88c861e52fa", "https://assetstore.unity.com/packages/slug/36050"),
                new Integration("Buto - Volumetric Fog and Volumetric Lighting", Assets.Buto, 258881, true, true, "ada48e5159bb1a5469288f9c75ca4629", "https://assetstore.unity.com/packages/slug/258881"),
            };

            for (int i = 0; i < integrationsArray.Length; i++)
            {
                integrationsArray[i].installed = IsFogLibraryPresent(integrationsArray[i]);
            }

            return integrationsArray;
        }

        public static Integration GetIntegration(Assets asset)
        {
            for (int i = 0; i < Integrations.Length; i++)
            {
                if (Integrations[i].asset == asset) return Integrations[i];
            }

            return null;
        }

        public static bool IsFogLibraryPresent(Integration integration)
        {
            if (integration.asset == Assets.None || integration.asset == Assets.UnityFog) return true;

            string path = AssetDatabase.GUIDToAssetPath(integration.libraryGUID);
            
            return path != string.Empty && AssetDatabase.LoadAssetAtPath(path, typeof(TextAsset));
        }

        public static Integration GetFirstInstalled()
        {
            for (int i = 0; i < Integrations.Length; i++)
            {
                //Always installed anyway
                if (Integrations[i].asset == Assets.None || Integrations[i].asset == Assets.UnityFog) continue;
                
                #if SWS_DEV
                //Gets in the way of testing and using default Unity fog
                //if(Integrations[i].asset == Assets.SCPostEffects || Integrations[i].asset == Assets.Colorful) continue;
                #endif
                
                if (IsFogLibraryPresent(Integrations[i]))
                {
                    return Integrations[i];
                }
            }

            //No third-party assets installed, default to Unity fog
            return GetIntegration(Assets.UnityFog);
        }
    }
}