using System;

namespace InhaExpress.Client.Presentation
{
    // Editor/simulation actions only. No pedestrian pose or control decision crosses this bridge.
    public static class PedestrianScenarioActions
    {
        public static Func<IOperatorVehicle, string> SpawnAhead;
        public static Func<string> ReadStatus;
    }
}
