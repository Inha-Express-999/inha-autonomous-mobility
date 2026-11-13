using System;
namespace InhaExpress.Client.Presentation
{
    // The capture caller reads Generation BEFORE sampling the physical vehicle.
    // A delayed capture from an old socket must never enter a newer socket's queue.
    public sealed class ConnectionObservationBuffer
    {
        private readonly object sync = new object();
        private int generation;
        private string json;
        public int Generation { get { lock (sync) return generation; } }
        public int BeginConnection()
        {
            lock (sync) { generation = checked(generation + 1); json = null; return generation; }
        }
        public bool Publish(int capturedGeneration, string observation)
        {
            if (string.IsNullOrEmpty(observation)) throw new ArgumentException("Observation required", nameof(observation));
            lock (sync)
            {
                if (capturedGeneration != generation) return false;
                json = observation; return true;
            }
        }
        public string Read(int socketGeneration)
        {
            lock (sync) return socketGeneration == generation ? json : null;
        }
    }
}
