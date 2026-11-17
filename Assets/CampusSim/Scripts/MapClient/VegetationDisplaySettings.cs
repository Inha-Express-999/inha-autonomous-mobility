using System.Collections.Generic;
using UnityEngine;

namespace InhaExpress.Client.MapClient
{
    public static class VegetationDisplaySettings
    {
        private const string Key = "InhaMobil.Mobile.Vegetation";
        public static bool MobileVisible => PlayerPrefs.GetInt(Key, 0) != 0;
        /// <summary>Extra perspective viewpoints (PC all-vehicle view) that keep nearby vegetation drawn.</summary>
        public static readonly List<Transform> ExtraViewpoints = new List<Transform>();
        public static bool Visible => !(ClientServerSettings.MobileMode || Application.isMobilePlatform) || MobileVisible;
        public static void SetMobileVisible(bool visible)
        {
            PlayerPrefs.SetInt(Key, visible ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
