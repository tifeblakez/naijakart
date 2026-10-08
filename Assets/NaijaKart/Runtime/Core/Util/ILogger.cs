namespace NaijaKart.Core.Util
{
    public enum LogLevel { Debug, Info, Warning, Error }

    /// <summary>Engine-agnostic logging sink. Unity binds this to Debug.Log; the server to Console.</summary>
    public interface ILogger
    {
        void Log(LogLevel level, string category, string message);
    }

    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();
        public void Log(LogLevel level, string category, string message) { }
    }

    public static class LoggerExtensions
    {
        public static void Debug(this ILogger l, string category, string message) => l.Log(LogLevel.Debug, category, message);
        public static void Info(this ILogger l, string category, string message) => l.Log(LogLevel.Info, category, message);
        public static void Warn(this ILogger l, string category, string message) => l.Log(LogLevel.Warning, category, message);
        public static void Error(this ILogger l, string category, string message) => l.Log(LogLevel.Error, category, message);
    }
}
