using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using OpenSteamClient.Logging;
using OpenSteamworks.Callbacks;
using OpenSteamworks.Callbacks.Structs;
using OpenSteamworks.Data;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Data.Structs;

namespace OpenSteamworks.Client.Apps;

public sealed class AppsManager
{
	private readonly ConcurrentDictionary<CGameID, IApp> _appCache = new();
	private readonly ISteamClient _steamClient;
    private readonly ILogger _logger;

	public AppsManager(ISteamClient steamClient, ILoggerFactory loggerFactory)
    {
        this._logger = loggerFactory.CreateLogger("AppsManager");
		this._steamClient = steamClient;
        this._steamClient.CallbackManager.Register<AppInfoUpdateProgress_t>(OnAppInfoUpdate);
        this._steamClient.CallbackManager.Register<AppEventStateChange_t>(OnAppEventStateChange);
    }

    private void OnAppEventStateChange(ICallbackHandler handler, in AppEventStateChange_t cb)
    {
        try
        {
            if (!_appCache.TryGetValue(new CGameID(cb.AppID), out IApp? app))
                return;

            if (app is not IAppInfoUpdateInterface updateInterface)
                return;

            updateInterface.OnAppStateChanged();
        }
        catch (Exception e)
        {
            _logger.Error("Failed to update app wrapper from appinfo update:");
            _logger.Error(e);
        }
    }

    private void OnAppInfoUpdate(ICallbackHandler handler, in AppInfoUpdateProgress_t cb)
    {
        try
        {
            var appIdAsGameID = new CGameID(cb.AppID);
            if (_appCache.TryGetValue(appIdAsGameID, out IApp? app))
            {
                if (app is IAppInfoUpdateInterface updateInterface)
                    updateInterface.OnAppInfoUpdated(_steamClient.AppsHelper.GetAppInfo(cb.AppID, IAppInfoAccessInterface.Sections));

                return;
            }

            app = SteamApp.Create(_steamClient, this, appIdAsGameID);
            _appCache.TryAdd(app.ID, app);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to update app wrapper from appinfo update:");
            _logger.Error(e);
        }
    }

    public IApp GetApp(CGameID gameId)
	{
        if (_appCache.TryGetValue(gameId, out IApp? app))
            return app;

        if (_bannedApps.Contains(gameId))
            return BannedApp.Default;

		if (gameId.IsSteamApp())
		{
			app = SteamApp.Create(_steamClient, this, gameId);
		} else if (gameId.IsShortcut())
		{
			app = new ShortcutApp(_steamClient, gameId);
		}
		else
		{
			throw new ArgumentOutOfRangeException(nameof(gameId));
		}

        _appCache.TryAdd(gameId, app);
		return app;
	}

    public IList<IApp> GetApps(IEnumerable<CGameID> gameIds)
    {
        var listGameIDs = gameIds.ToList();

        List<IApp> apps = new(listGameIDs.Count);
        foreach (var gameid in listGameIDs)
        {
            if (_bannedApps.Contains(gameid))
                continue;

            Trace.Assert(gameid.IsValid());
            apps.Add(GetApp(gameid));
        }

        return apps;
    }

    private readonly ConcurrentBag<CGameID> _bannedApps = new() { CGameID.Zero };
    public async Task InitApps(IEnumerable<AppId_t> appIDs)
    {
        appIDs = appIDs.ToList();

        await _steamClient.AppsHelper.RequestAppInfo(appIDs);
        foreach (var appid in appIDs)
        {
            var gameid = new CGameID(appid);
            try
            {
                _appCache.GetOrAdd(gameid, id => SteamApp.Create(_steamClient, this, id));
            }
            catch (Exception e)
            {
                _bannedApps.Add(gameid);
                // We can just ignore errors here, it's not ideal but some stupid appids exist (such as 5)
            }
        }
    }

    public IEnumerable<IApp> GetLibraryApps()
        => GetApps(_steamClient.UserHelper.GetSubscribedApps().Select(a => new CGameID(a))).Where(a => a.Type is EAppType.Application or EAppType.Beta or EAppType.Game or EAppType.Shortcut
            or EAppType.Tool);
}
