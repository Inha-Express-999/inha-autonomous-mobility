using System;

// WebSocketClientDataSource only needs Unity logging. This stub lets the same
// project source run under Unity's bundled Mono runtime outside the Editor.
namespace UnityEngine
{
    public static class Debug
    {
        public static void LogWarning(object message) => Console.Error.WriteLine(message);
    }
}
