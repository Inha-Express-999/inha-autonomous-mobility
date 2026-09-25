using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    public enum RoadGraphVerificationStatus
    {
        Synthetic,
        Unverified,
        Validated,
        Closed
    }

    public abstract class RoadGraphMarkerView : MonoBehaviour
    {
        [SerializeField] private RoadGraphVerificationStatus verificationStatus;
        [SerializeField] private Renderer[] statusRenderers;
        private MaterialPropertyBlock propertyBlock;

        public RoadGraphVerificationStatus VerificationStatus => verificationStatus;

        public virtual void SetVerificationStatus(RoadGraphVerificationStatus status)
        {
            verificationStatus = status;
            if (statusRenderers == null) return;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            Color color = StatusColor(status);
            foreach (Renderer item in statusRenderers)
            {
                if (!item) continue;
                item.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_BaseColor", color);
                propertyBlock.SetColor("_Color", color);
                item.SetPropertyBlock(propertyBlock);
            }
        }

        protected void SetStatusRenderers(Renderer[] renderers) => statusRenderers = renderers;

        protected static Color StatusColor(RoadGraphVerificationStatus status)
        {
            switch (status)
            {
                case RoadGraphVerificationStatus.Validated: return new Color(0.20f, 0.78f, 0.42f);
                case RoadGraphVerificationStatus.Closed: return new Color(0.86f, 0.22f, 0.20f);
                case RoadGraphVerificationStatus.Unverified: return new Color(0.52f, 0.56f, 0.62f);
                default: return new Color(1.00f, 0.68f, 0.16f);
            }
        }
    }
}
