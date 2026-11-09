using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace InhaExpress.Client.MapClient
{
    public static class ClientServerSettings
    {
        public const string AddressKey = "InhaMobil.Mobile.ServerUrl";
        public static bool MobileMode { get; set; }
        public static string MobileUrl => PlayerPrefs.GetString(AddressKey, "");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => MobileMode = false;

        public static string NormalizeUrl(string value)
        {
            value = (value ?? "").Trim().TrimEnd('/');
            if (value.Length == 0) throw new ArgumentException("PC의 서버 주소를 입력하세요.");
            bool bare = !value.Contains("://");
            if (bare) value = "http://" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                string.IsNullOrEmpty(uri.Host) || uri.Host == "0.0.0.0" ||
                uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                throw new ArgumentException("PC 주소를 입력하세요. 예: 192.168.0.10:8765");
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (uri.IsLoopback) throw new ArgumentException("127.0.0.1은 휴대폰 자신입니다. PC의 LAN IP를 입력하세요.");
#endif
            var builder = new UriBuilder(uri);
            if (bare && uri.IsDefaultPort && !value.Substring("http://".Length).Contains(":")) builder.Port = 8765;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        public static string PassengerId
        {
            get
            {
                const string key = "InhaMobil.Passenger.Id";
                string id = PlayerPrefs.GetString(key, "");
                if (id.Length == 0) { id = "mobile-" + Guid.NewGuid().ToString("N"); PlayerPrefs.SetString(key, id); PlayerPrefs.Save(); }
                return id;
            }
        }

        public static string[] LanUrls(int port)
        {
            var urls = new List<string>();
            var preferred = new List<string>();
#if UNITY_EDITOR || UNITY_STANDALONE
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                var properties = adapter.GetIPProperties();
                bool hasGateway = false;
                foreach (var gateway in properties.GatewayAddresses)
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any))
                        hasGateway = true;
                foreach (var entry in properties.UnicastAddresses)
                {
                    var ip = entry.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || ip.ToString().StartsWith("169.254.")) continue;
                    var url = "http://" + ip + ":" + port;
                    var candidates = hasGateway ? preferred : urls;
                    if (!candidates.Contains(url)) candidates.Add(url);
                }
            }
#endif
            preferred.Sort(StringComparer.Ordinal);
            urls.Sort(StringComparer.Ordinal);
            foreach (var url in urls) if (!preferred.Contains(url)) preferred.Add(url);
            return preferred.ToArray();
        }
    }
}
