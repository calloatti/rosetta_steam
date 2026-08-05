using System;
using System.IO;

namespace TimberbornRosettaGenerator
{
  public static class LogService
  {
    private static readonly string _logFilePath;
    private static readonly object _fileLock = new();

    static LogService()
    {
      string exePath = AppDomain.CurrentDomain.BaseDirectory;
      _logFilePath = Path.Combine(exePath, "RosettaGenerator.log");

      // Start with a clean log file for each session
      try
      {
        if (File.Exists(_logFilePath))
        {
          File.Delete(_logFilePath);
        }
      }
      catch { }
    }

    public static void Log(string message)
    {
      string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

      // 1. Console output
      Console.WriteLine(logLine);

      // 2. Thread-safe file output
      try
      {
        lock (_fileLock)
        {
          File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
        }
      }
      catch { }
    }
  }
}