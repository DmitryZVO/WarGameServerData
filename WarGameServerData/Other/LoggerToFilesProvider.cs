using Microsoft.Extensions.Logging;

namespace WarGameServerData.Other;

public class LoggerToFilesProvider : ILoggerProvider
{
    private const int MessagesMaxCount = 500;
    public static List<string> Messages { get; } = new();
    public static DateTime TimeStamp { get; set; } = DateTime.MinValue;

    private class LoggerToFiles : ILogger
    {
        private readonly string _filesPath = AppDomain.CurrentDomain.BaseDirectory + "Logs";
        private static readonly object Lock = new();

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return default;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                lock (Messages)
                {
                    while (Messages.Count > MessagesMaxCount)
                    {
                        Messages.RemoveAt(0);
                    }
                }

                if (!Directory.Exists(_filesPath))
                {
                    Directory.CreateDirectory(_filesPath);
                }

                var path = Path.Combine(_filesPath, logLevel.ToString());
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                var exc = "";
                var fullFilePath = Path.Combine(_filesPath, logLevel.ToString(),
                    DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                if (exception != null)
                    exc = "***" + exception.GetType() + ": " + exception.Message + " {" + exception.StackTrace +
                          "} *** ";

                lock (Lock)
                {
                    var message = "[*" + logLevel.ToString()[0] + "* " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + exc +
                                  formatter(state, exception);
                    File.AppendAllText(fullFilePath, message + Environment.NewLine);

                    lock (Messages)
                    {
                        Messages.Add(message + Environment.NewLine);
                        TimeStamp = DateTime.Now;
                    }
                }
            }
            catch
            {
                //
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new LoggerToFiles();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
