using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TimberbornRosettaGenerator
{
  class MainClass
  {
    private static bool _requiresPause = false;

    static async Task Main(string[] Richmond)
    {
      LogService.Log("=== TIMBERBORN ROSETTA GENERATOR STARTING ===");

      try
      {
        var crawler = new SteamCrawlerService();

        // 1. Validate configuration files before initialization steps
        if (!crawler.VerifyConfiguration())
        {
          _requiresPause = true;
          return;
        }

        try
        {
          // 2. Execute the crawling pipeline
          List<ulong> modIds = await crawler.GetAllPublicModIdsAsync();

          if (modIds.Count > 0)
          {
            // 3. Download and extract manifest data anonymously
            var downloader = new ModDownloaderService();
            await downloader.ProcessModsAsync(modIds);

            // 4. Build, sanitize, and analyze the rosetta files (Passing our single memory lookup structure)
            var processor = new ManifestProcessorService();
            processor.RunExport(modIds, crawler.SteamModLookup);
          }
          else
          {
            LogService.Log("[Warning] No mods were fetched from the Workshop crawl. Export skipped.");
          }
        }
        catch (Exception ex)
        {
          LogService.Log($"[Fatal Error] Pipeline execution crashed: {ex.Message}");
          _requiresPause = true;
        }
      }
      catch (Exception criticalEx)
      {
        LogService.Log($"[Fatal Error] Core initialization failed: {criticalEx.Message}");
        _requiresPause = true;
      }
      finally
      {
        LogService.Log("=== TIMBERBORN ROSETTA GENERATOR FINISHED ===");

        if (_requiresPause)
        {
          Console.WriteLine("\n[Notice] The application encountered a configuration or execution error.");
          Console.WriteLine("Press [ENTER] to exit the application...");
          Console.ReadLine();
        }
      }
    }
  }
}