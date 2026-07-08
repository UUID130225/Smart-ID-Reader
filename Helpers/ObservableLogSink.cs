using System;
using System.Collections.Concurrent;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace SmartIDReader.Helpers
{
    public class ObservableLogSink : ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        public static readonly ConcurrentQueue<string> LogBuffer = new ConcurrentQueue<string>();
        private const int MaxLogLines = 200;

        public static event Action<string> LogAdded;

        public ObservableLogSink(ITextFormatter formatter)
        {
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        public void Emit(LogEvent logEvent)
        {
            try
            {
                using (var writer = new StringWriter())
                {
                    _formatter.Format(logEvent, writer);
                    string formattedMessage = writer.ToString().TrimEnd('\r', '\n');
                    
                    LogBuffer.Enqueue(formattedMessage);
                    while (LogBuffer.Count > MaxLogLines)
                    {
                        LogBuffer.TryDequeue(out _);
                    }

                    LogAdded?.Invoke(formattedMessage);
                }
            }
            catch
            {
                // Prevent application crashes due to logging formatting issues
            }
        }
    }
}
