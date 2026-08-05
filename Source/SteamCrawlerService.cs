using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;

namespace TimberbornRosettaGenerator
{
  // Clean, dedicated data record to hold the Workshop metadata properties together
  public class SteamModMetadata
  {
    public string Title { get; set; } = "";
    // Numeric SteamID64 of the Workshop item creator (anonymous CM queries do not resolve profile names)
    public string Creator { get; set; } = "";
  }

  public class SteamCrawlerService
  {
    private string appId = string.Empty;
    private readonly int itemsPerPage = 100;
    private readonly string baseDirectory;

    // Single unified collection mapping a PublishedFileId straight to its Metadata payload
    public Dictionary<string, SteamModMetadata> SteamModLookup { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SteamCrawlerService()
    {
      baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
    }

    public bool VerifyConfiguration()
    {
      string appIdFile = Path.Combine(baseDirectory, "steam_appid.txt");

      if (!File.Exists(appIdFile)) File.WriteAllText(appIdFile, "1062090");

      appId = File.ReadAllText(appIdFile).Trim();

      if (string.IsNullOrEmpty(appId) || !uint.TryParse(appId, out _))
      {
        LogService.Log("[Error] steam_appid.txt does not contain a valid numeric app id.");
        return false;
      }

      return true;
    }

    public async Task<List<ulong>> GetAllPublicModIdsAsync()
    {
      var modIds = new List<ulong>();
      if (!VerifyConfiguration()) return modIds;
      if (!uint.TryParse(appId, out uint appIdValue)) return modIds;

      SteamModLookup.Clear(); // Flush previous memory allocations safely

      SteamClient? client = null;
      try
      {
        client = await CreateAnonymousSessionAsync();
        if (client == null) return modIds;

        var unifiedMessages = client.GetHandler<SteamUnifiedMessages>();
        unifiedMessages!.CreateService<PublishedFileService>();

        LogService.Log("[Network] Crawling Workshop via anonymous PublishedFile.QueryFiles...");
        int page = 1;
        while (true)
        {
          var request = new CPublishedFile_QueryFiles_Request
          {
            query_type = 21,
            appid = appIdValue,
            numperpage = (uint)itemsPerPage,
            return_details = true,
            page = (uint)page
          };
          request.requiredtags.Add("Mod");
          request.excludedtags.Add("BuildingBlueprints");

          var job = unifiedMessages.SendMessage<CPublishedFile_QueryFiles_Request, CPublishedFile_QueryFiles_Response>("PublishedFile.QueryFiles#1", request);
          job.Timeout = TimeSpan.FromSeconds(30);
          var response = await job;

          if (response.Result != EResult.OK)
          {
            LogService.Log($"[Warning] QueryFiles page {page} rejected: {response.Result}.");
            break;
          }

          var details = response.Body.publishedfiledetails;
          if (details.Count == 0) break;

          foreach (var detail in details)
          {
            if (detail.publishedfileid == 0) continue;

            modIds.Add(detail.publishedfileid);
            SteamModLookup[detail.publishedfileid.ToString()] = new SteamModMetadata
            {
              Title = detail.title ?? "",
              Creator = detail.creator.ToString()
            };
          }

          LogService.Log($"[Network] Crawled page {page}: {details.Count} mods (total {response.Body.total}).");
          if (modIds.Count >= (int)response.Body.total || page >= 200) break;
          page++;
        }

        LogService.Log($"[Summary] Found {modIds.Count} total mods via anonymous Workshop crawl.");
      }
      catch (Exception ex)
      {
        LogService.Log($"[Fatal Error] Workshop crawl failed: {ex.Message}");
      }
      finally
      {
        try { client?.Disconnect(); } catch { }
      }

      return modIds;
    }

    private static async Task<SteamClient?> CreateAnonymousSessionAsync()
    {
      var config = SteamConfiguration.Create(b => b.WithProtocolTypes(ProtocolTypes.Tcp));
      var client = new SteamClient(config);

      var server = await client.Servers.GetNextServerCandidateAsync(ProtocolTypes.Tcp);
      if (server == null)
      {
        LogService.Log("[Fatal Error] No Steam CM server candidates available.");
        try { client.Disconnect(); } catch { }
        return null;
      }
      LogService.Log($"[Network] Connecting to CM server {server.GetHost()}:{server.GetPort()} ...");
      client.Connect(server);

      var connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var loggedOnTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

      var pump = Task.Run(async () =>
      {
        while (true)
        {
          var msg = client.WaitForCallback(TimeSpan.FromMilliseconds(100));
          if (msg == null) continue;

          switch (msg)
          {
            case SteamClient.ConnectedCallback:
              LogService.Log("[Network] CM connection established.");
              connectedTcs.TrySetResult(true);
              break;
            case SteamClient.DisconnectedCallback d:
              LogService.Log($"[Warning] CM disconnected (UserInitiated={d.UserInitiated}).");
              if (!connectedTcs.Task.IsCompleted) connectedTcs.TrySetException(new Exception("CM disconnected before connect completed."));
              if (!loggedOnTcs.Task.IsCompleted) loggedOnTcs.TrySetException(new Exception("CM disconnected during logon."));
              return;
            case SteamUser.LoggedOnCallback l:
              if (l.Result != EResult.OK)
              {
                loggedOnTcs.TrySetException(new Exception($"Anonymous logon failed: {l.Result}."));
                return;
              }
              LogService.Log("[Network] Anonymous logon OK.");
              loggedOnTcs.TrySetResult(true);
              return;
            case SteamUser.LoggedOffCallback:
              LogService.Log("[Warning] Anonymous session logged off.");
              loggedOnTcs.TrySetException(new Exception("Anonymous session logged off unexpectedly."));
              return;
          }
        }
      });

      try
      {
        await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(45));
        client.GetHandler<SteamUser>()!.LogOnAnonymous();
        await loggedOnTcs.Task.WaitAsync(TimeSpan.FromSeconds(45));
        await pump.WaitAsync(TimeSpan.FromSeconds(5));
        return client;
      }
      catch (Exception ex)
      {
        LogService.Log($"[Fatal Error] Anonymous CM session setup failed: {ex.Message}");
        try { client.Disconnect(); } catch { }
        return null;
      }
    }

    // Registers the "PublishedFile" unified-message service so SteamKit2 routes QueryFiles responses back to the job
    private sealed class PublishedFileService : SteamUnifiedMessages.UnifiedService
    {
      public override string ServiceName => "PublishedFile";

      public override void HandleResponseMsg(string methodName, PacketClientMsgProtobuf packetMsg)
      {
        if (methodName == "QueryFiles")
          PostResponseMsg<CPublishedFile_QueryFiles_Response>(packetMsg);
      }

      public override void HandleNotificationMsg(string methodName, PacketClientMsgProtobuf packetMsg) { }
    }
  }
}
