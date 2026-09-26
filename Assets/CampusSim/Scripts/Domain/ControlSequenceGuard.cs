using System;
using System.Collections.Generic;

namespace InhaExpress.Client.Domain
{
    // Owned by the transport host; disposable actors share this monotonic history.
    public sealed class ControlSequenceGuard
    {
        private readonly Dictionary<(string Run, string Map, string Session, string Vehicle), long> latest =
            new Dictionary<(string Run, string Map, string Session, string Vehicle), long>();

        public bool TryAccept(string run, VehicleControlDto command)
        {
            if (string.IsNullOrWhiteSpace(run) || command == null) return false;
            var key = (run, command.MapVersion, command.SessionId, command.VehicleId);
            if (latest.TryGetValue(key, out long previous) && command.Sequence <= previous) return false;
            latest[key] = command.Sequence;
            return true;
        }
    }
}
