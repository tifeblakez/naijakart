using NaijaKart.Core.Util;
using UnityEngine;
using ILogger = NaijaKart.Core.Util.ILogger;

namespace NaijaKart.Unity
{
    public sealed class UnityLogger : ILogger
    {
        public static readonly UnityLogger Instance = new UnityLogger();

        public void Log(LogLevel level, string category, string message)
        {
            string line = $"[NK:{category}] {message}";
            switch (level)
            {
                case LogLevel.Error: Debug.LogError(line); break;
                case LogLevel.Warning: Debug.LogWarning(line); break;
                default: Debug.Log(line); break;
            }
        }
    }
}
