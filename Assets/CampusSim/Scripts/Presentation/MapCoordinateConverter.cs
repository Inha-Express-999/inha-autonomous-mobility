using System;
using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    public static class MapCoordinateConverter
    {
        public static Vector3 ToUnity(MapPositionDto position) =>
            new Vector3(ToFloat(position.X), ToFloat(position.Z), ToFloat(position.Y));

        public static MapPositionDto FromUnity(Vector3 position) =>
            new MapPositionDto(position.x, position.z, position.y);

        public static float ToUnityYaw(double headingRad)
        {
            if (double.IsNaN(headingRad) || double.IsInfinity(headingRad))
                throw new ArgumentOutOfRangeException(nameof(headingRad));
            return (float)((headingRad % (2 * Math.PI)) * 180.0 / Math.PI);
        }

        private static float ToFloat(double value)
        {
            var result = (float)value;
            if (float.IsInfinity(result) || Math.Abs((double)result - value) > 0.01)
                throw new ArgumentOutOfRangeException(nameof(value), "Use a local origin to preserve centimetre precision.");
            return result;
        }
    }
}
