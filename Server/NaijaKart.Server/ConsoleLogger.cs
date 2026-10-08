using System;
using NaijaKart.Core.Util;

namespace NaijaKart.Server
{
    public sealed class ConsoleLogger : ILogger
    {
        private readonly LogLevel _min;

        public ConsoleLogger(LogLevel min = LogLevel.Info)
        {
            _min = min;
        }

        public void Log(LogLevel level, string category, string message)
        {
            if (level < _min) return;
            Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [{level}] {category}: {message}");
        }
    }
}
