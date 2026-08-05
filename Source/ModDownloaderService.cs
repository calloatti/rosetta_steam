using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamDepotDownload.Steam.Shared.Auth;
using SteamDepotDownload.Steam.Shared.Depot;
using SteamDepotDownload.Steam.Shared.Session;

namespace TimberbornRosettaGenerator
{
  public class ModDownloaderService
  {
    private const int WorkerCount = 4;
    private const int MaxAttempts = 3;

    private readonly string _targetBaseFolder;
    private readonly string _accountsFolder;
    private int _totalMods;
    private int _completed;
    private int _failedSessions;

    public ModDownloaderService()
    {
      string rootPath = AppDomain.CurrentDomain.BaseDirectory;
      _targetBaseFolder = Path.Combine(rootPath, "data");
      _accountsFolder = Path.Combine(rootPath, "accounts");
      Directory.CreateDirectory(_targetBaseFolder);
    }

    public async Task ProcessModsAsync(List<ulong> modIds)
    {
      LogService.Log("\n=== STARTING MANIFEST DOWNLOAD PIPELINE ===");
      Directory.CreateDirectory(_accountsFolder);

      _totalMods = modIds.Count;
      _completed = 0;
      _failedSessions = 0;

      if (_totalMods == 0) return;

      LogService.Log($"[Pipeline] Spawning {WorkerCount} anonymous sessions for {_totalMods} mods...");

      var queue = new ConcurrentQueue<ulong>(modIds);
      var workers = Enumerable.Range(0, WorkerCount).Select(i => RunWorkerAsync(i, queue)).ToArray();
      await Task.WhenAll(workers);

      LogService.Log($"[Pipeline] Completed {_completed}/{_totalMods} mods.");

      if (_failedSessions > 0)
      {
        LogService.Log($"[Warning] {_failedSessions} worker session(s) failed to connect.");
      }
      if (_failedSessions == WorkerCount && _completed == 0)
      {
        throw new InvalidOperationException("All anonymous Steam sessions failed to connect. Is the network reachable?");
      }
    }

    private async Task RunWorkerAsync(int workerIndex, ConcurrentQueue<ulong> queue)
    {
      var credentials = new SteamCredentials { LoginId = (uint)(1000 + workerIndex) };
      var options = new SteamSessionOptions
      {
        AccountStore = AccountSettingsStore.CreateAt(Path.Combine(_accountsFolder, $"worker-{workerIndex}.config")),
      };

      try
      {
        await using var session = await SteamClientFactory.Create().ConnectAsync(credentials, options);
        LogService.Log($"[Worker {workerIndex}] Session connected (SteamId {session.SteamId}, CellId {session.CellId}).");

        while (queue.TryDequeue(out ulong modId))
        {
          int idx = Interlocked.Increment(ref _completed);
          await ProcessModAsync(session, workerIndex, modId, idx, _totalMods);
        }
      }
      catch (Exception ex)
      {
        LogService.Log($"[Worker {workerIndex}] Failed to establish session: {ex.Message}");
        Interlocked.Increment(ref _failedSessions);
      }
    }

    private async Task ProcessModAsync(ISteamSession session, int workerIndex, ulong modId, int idx, int total)
    {
      string modIdStr = modId.ToString();
      string localDataPath = Path.Combine(_targetBaseFolder, modIdStr);
      Directory.CreateDirectory(localDataPath);

      LogService.Log($"[{idx}/{total}] Downloading manifest for {modId}...");

      for (int attempt = 1; attempt <= MaxAttempts; attempt++)
      {
        try
        {
          var downloader = session.CreateDownloader(new DownloadConfig
          {
            FileFilter = FileFilter.FromLines(new[] { "regex:.*manifest\\.json" }),
            InstallDirectory = localDataPath,
            VerifyAll = true,
          });

          string lastStage = string.Empty;
          var progress = new Progress<DownloadProgress>(p =>
          {
            if (!string.IsNullOrEmpty(p.Stage) && p.Stage != lastStage)
            {
              LogService.Log($"[{idx}/{total}] [{workerIndex}] {p.Stage}: {p.BytesDownloaded}/{p.BytesTotal} bytes");
              lastStage = p.Stage;
            }
          });

          DownloadResult result = await downloader.DownloadPubfileAsync(modId, progress);
          var summary = result.Depots.FirstOrDefault();

          bool unchanged = result.BytesDownloaded == 0 && summary != null && summary.FilesDownloaded == 0;
          LogService.Log(unchanged
            ? $"[{idx}/{total}] OK {modId} (unchanged, manifestId {summary!.ManifestId})"
            : $"[{idx}/{total}] OK {modId} ({result.BytesDownloaded} bytes, {summary?.FilesDownloaded ?? 0} file(s), manifestId {summary?.ManifestId ?? 0})");
          return;
        }
        catch (Exception ex)
        {
          LogService.Log($"[{idx}/{total}] Attempt {attempt}/{MaxAttempts} failed for {modId}: {ex.Message}");
          if (attempt == MaxAttempts)
          {
            LogService.Log($"[Warning] {modId} could not be downloaded. Keeping cached copy if present.");
          }
        }

        await Task.Delay(attempt * 500);
      }
    }
  }
}
