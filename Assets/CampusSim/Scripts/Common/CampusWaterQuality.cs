using UnityEngine;
namespace CampusSim
{
    public sealed class CampusWaterQuality : MonoBehaviour
    {
        public Material highQuality;
        public Material mobileQuality;
        void Start() => SetHighQuality(!Application.isMobilePlatform);
        public void SetHighQuality(bool high)
        {
            var material=high?highQuality:mobileQuality;
            if(material)GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}
