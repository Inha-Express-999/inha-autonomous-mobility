using System;
using InhaExpress.Client.Presentation;
class CheckConnectionObservationBuffer
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Main()
    {
        var buffer = new ConnectionObservationBuffer();
        buffer.Publish(0, "offline pose");
        int first = buffer.BeginConnection();
        Check(buffer.Read(first) == null, "initial connection waits for new capture");
        Check(!buffer.Publish(0, "late offline capture"), "late preconnection capture rejected");
        Check(buffer.Publish(first, "position A") && buffer.Read(first) == "position A", "current capture accepted");
        int second = buffer.BeginConnection();
        Check(buffer.Read(second) == null, "reconnect clears prior pose");
        Check(!buffer.Publish(first, "late A") && buffer.Read(second) == null, "capture spanning reconnect rejected");
        Check(buffer.Publish(second, "position B") && buffer.Read(second) == "position B", "fresh reconnect capture accepted");
        Check(buffer.Read(first) == null, "retired socket cannot consume new pose");
    }
}
