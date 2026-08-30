using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenSteamClient.Logging;
using OpenSteamworks.Client.Apps.Assets;
using OpenSteamworks.Data;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Data.KeyValue;
using OpenSteamworks.Data.Structs;
using OpenSteamworks.Exceptions;
using OpenSteamworks.KeyValue.ObjectGraph;

namespace OpenSteamworks.Client.Apps;

internal sealed class SteamApp : ObservableObject, IApp, IAppInfoAccessInterface, IAppConfigInterface, IAppLaunchInterface, IAppAssetsInterface, IAppInstallInterface, IAppInfoUpdateInterface
{
	private string LaunchOptionsConfigKey => $"Software\\Valve\\Steam\\Apps\\{AppID}\\LaunchOptions";

	public AppDataCommonSection Common { get; private set; }
    public AppDataConfigSection Config { get; private set; }
    public AppDataExtendedSection Extended { get; private set; }
    public AppDataInstallSection Install { get; private set;}
    public AppDataDepotsSection Depots { get; private set;}
    public AppDataCommunitySection Community { get; private set; }
    public AppDataLocalizationSection Localization { get; private set; }

    [MemberNotNull(nameof(Common))]
    [MemberNotNull(nameof(Config))]
    [MemberNotNull(nameof(Extended))]
    [MemberNotNull(nameof(Install))]
    [MemberNotNull(nameof(Depots))]
    [MemberNotNull(nameof(Community))]
    [MemberNotNull(nameof(Localization))]
    [MemberNotNull(nameof(Assets))]
    public void OnAppInfoUpdated(IDictionary<EAppInfoSection, KVObject> appInfo)
    {
        Type = _steamClient.AppsHelper.GetAppType(AppID);
        Common = new AppDataCommonSection(appInfo[EAppInfoSection.Common]);
        Config = new AppDataConfigSection(appInfo[EAppInfoSection.Config]);
        Extended = new AppDataExtendedSection(appInfo[EAppInfoSection.Extended]);
        Install = new AppDataInstallSection(appInfo[EAppInfoSection.Install]);
        Depots = new AppDataDepotsSection(appInfo[EAppInfoSection.Depots]);
        Community = new AppDataCommunitySection(appInfo[EAppInfoSection.Community]);
        Localization = new AppDataLocalizationSection(appInfo[EAppInfoSection.Localization]);

        InitAssets();

        var newName = _steamClient.AppsHelper.GetAppLocalizedName(AppID);
        if (Name != newName)
        {
            Name = newName;
            OnPropertyChanged(nameof(Name));
        }
    }

    //TODO: Holdover from the old library system. Get rid of this terribleness
	public DateTime AssetsLastModified => (DateTime)RTime32.Parse(Common.StoreAssetModificationTime, CultureInfo.InvariantCulture.NumberFormat);

	public CGameID ID { get; }
	public AppId_t AppID => ID.AppID;
	public EAppType Type { get; private set; }

	public IApp? ParentApp
        => Common.ParentAppID != 0 ? _appsManager.GetApp(new CGameID(Common.ParentAppID)) : null;

    public EAppState State => _steamClient.AppManagerHelper.GetAppState(AppID);

    public string Name { get; private set; }

    private readonly ISteamClient _steamClient;
	private readonly AppsManager _appsManager;
	private SteamApp(ISteamClient steamClient, AppsManager appsManager, CGameID gameId, IDictionary<EAppInfoSection, KVObject> appInfo)
	{
		_steamClient = steamClient;
		_appsManager = appsManager;

		Trace.Assert(gameId.IsSteamApp());

		ID = gameId;

        OnAppInfoUpdated(appInfo);
    }

    public void OnAppStateChanged()
    {
        OnPropertyChanged(nameof(State));
    }

    public static IApp Create(ISteamClient steamClient, AppsManager appsManager, CGameID gameid)
    {
        Trace.Assert(gameid.IsValid());

        var appInfo = steamClient.AppsHelper.GetAppInfo(gameid.AppID, IAppInfoAccessInterface.Sections);
        return new SteamApp(steamClient, appsManager, gameid, appInfo);
    }

    public IEnumerable<IAppConfigInterface.ConfigKey> SupportedKeys =>
    [
        IAppConfigInterface.ConfigKey.LANGUAGE,
        IAppConfigInterface.ConfigKey.ACTIVE_BETA,
        IAppConfigInterface.ConfigKey.ENABLE_OVERLAY,
        IAppConfigInterface.ConfigKey.COMPAT_TOOL_NAME,
        IAppConfigInterface.ConfigKey.ENABLE_VR_THEATER,
        IAppConfigInterface.ConfigKey.ENABLE_STEAM_CLOUD,
        IAppConfigInterface.ConfigKey.ENABLE_STEAM_INPUT,
        IAppConfigInterface.ConfigKey.COMPAT_TOOL_CMDLINE,
        IAppConfigInterface.ConfigKey.LAUNCH_COMMAND_LINE,
        IAppConfigInterface.ConfigKey.DEFAULT_LAUNCH_OPTION
    ];

    public bool SetConfigValue(IAppConfigInterface.ConfigKey key, object? value)
	{
		switch (key)
		{
			case IAppConfigInterface.ConfigKey.LAUNCH_COMMAND_LINE:
				if (value is string launchOptStr)
				{
					if (!_steamClient.ConfigStoreHelper.Set(EConfigStore.UserLocal,
						    LaunchOptionsConfigKey, launchOptStr))
					{
						Logger.GeneralLogger.Error($"SteamApp {AppID}: Failed to set launch command line! (opt: \"{launchOptStr}\")");
						return false;
					}

					return true;
				}

				break;
			case IAppConfigInterface.ConfigKey.COMPAT_TOOL_NAME:
				if (value is string nameStr)
				{
					_steamClient.CompatHelper.SetAppCompatTool(AppID, nameStr);
				}

				break;
			case IAppConfigInterface.ConfigKey.COMPAT_TOOL_CMDLINE:
				if (value is string cmdlineStr)
				{
					_steamClient.CompatHelper.SetAppCompatTool(AppID, _steamClient.CompatHelper.GetAppCompatTool(AppID) ?? "", cmdlineStr);
				}

				break;
			case IAppConfigInterface.ConfigKey.ACTIVE_BETA:
				if (value is string betaStr)
				{
					if (!_steamClient.IClientAppManager.SetActiveBeta(AppID, betaStr))
					{
						Logger.GeneralLogger.Error($"SteamApp {AppID}: Failed to set beta! (beta: \"{betaStr}\")");
						return false;
					}
				}

				break;
			case IAppConfigInterface.ConfigKey.ENABLE_OVERLAY:
				if (value is bool bEnableOverlay)
				{
					if (!_steamClient.IClientUser.SetConfigInt(ERegistrySubTree.Apps, $"{AppID}\\OverlayAppEnable", bEnableOverlay ? 1 : 0))
					{
						Logger.GeneralLogger.Error($"SteamApp {AppID}: Failed to set overlay enable state!");
					}

					return true;
				}

				break;
            default:
                Logger.GeneralLogger.Error($"SteamApp {AppID}: Config option {key} not implemented");
                break;
		}

		return false;
	}

	public bool TryGetConfigValue(IAppConfigInterface.ConfigKey key, [NotNullWhen(true)] out object? value)
	{
		switch (key)
		{
			case IAppConfigInterface.ConfigKey.LAUNCH_COMMAND_LINE:
				value = _steamClient.ConfigStoreHelper.Get(EConfigStore.UserLocal,
					LaunchOptionsConfigKey, "");

				return true;
			case IAppConfigInterface.ConfigKey.COMPAT_TOOL_NAME:
				value = _steamClient.CompatHelper.GetAppCompatTool(AppID) ?? string.Empty;
				return true;
			case IAppConfigInterface.ConfigKey.ACTIVE_BETA:
				value = _steamClient.AppManagerHelper.GetActiveBeta(AppID);
				return true;
			case IAppConfigInterface.ConfigKey.ENABLE_OVERLAY:
				if (!_steamClient.IClientUser.GetConfigInt(ERegistrySubTree.Apps, $"{AppID}\\OverlayAppEnable",
					    out int val))
				{
					Logger.GeneralLogger.Error($"SteamApp {AppID}: Failed to get overlay enable state!");
					val = 1;
				}

				value = val;
				return true;
		}

		value = null;
		return false;
	}

    public IEnumerable<object?> GetAllowedValues(IAppConfigInterface.ConfigKey key)
    {
        if (key != IAppConfigInterface.ConfigKey.COMPAT_TOOL_NAME)
            return [];

        return _steamClient.CompatHelper.GetCompatToolsForApp(AppID);
    }

    private sealed class LaunchOption : ILaunchOption
	{
		public required uint ID { get; init; }
		public required string Title { get; init; }
		public required string CommandLine { get; init; }
	}

	//TODO: Results
	public event EventHandler<LaunchProgressEventArgs>? LaunchProgress;
	public void Launch(ILaunchOption launchOption, ELaunchSource source)
	{
		if (launchOption is not LaunchOption steamLaunchOption)
			throw new ArgumentException(nameof(launchOption));

        LaunchProgress?.Invoke(this, new LaunchProgressEventArgs() {IsLaunching = true, ShortForm = "Launching"});
		//TODO: No real progress...
        _steamClient.AppManagerHelper.LaunchApp(ID, steamLaunchOption.ID, source).ContinueWith(t =>
        {
            LaunchProgress?.Invoke(this, new LaunchProgressEventArgs() { IsLaunching = false, ShortForm = "Launching", FailureCode = t.IsFaulted ? EResult.Failure : EResult.OK });
        });
    }

    public bool Kill()
    {
        //TODO: Cancel launch
        return _steamClient.AppManagerHelper.KillApp(ID);
    }

	public IEnumerable<ILaunchOption> LaunchOptions
	{
		get
		{
			var optionIDs = _steamClient.AppsHelper.GetAvailableLaunchOptions(AppID);
			return Config.LaunchOptions.Where(opt => optionIDs.Contains(opt.ID)).Select(opt => new LaunchOption()
			{
				ID = (uint)opt.ID,
				Title = opt.Description,
				CommandLine = $"{opt.Executable} {opt.Arguments}".TrimEnd()
			});
		}
	}

    public ILaunchOption? DefaultOption
    {
        get
        {
            var opts = LaunchOptions.ToList();
            return opts.Count == 1 ? opts.First() : null;
        }
    }

    public event EventHandler<IAppAssetsInterface.AssetEventArgs>? AssetCached;
	public event EventHandler<IAppAssetsInterface.AssetEventArgs>? AssetUpdated;
	public IEnumerable<IAppAssetsInterface.ILibraryAsset> Assets { get; private set; }

	[MemberNotNull(nameof(Assets))]
	private void InitAssets()
	{
		var assets = new List<SteamLibraryAssets>();

        if (Common.LibraryAssetsFull != null)
        {
            foreach (var type in new[] {ELibraryAssetType.Hero, ELibraryAssetType.Logo, ELibraryAssetType.Portrait})
            {
                var kvType = type switch
                {
                    ELibraryAssetType.Hero => AppDataCommonSection.LibraryAssetsFullT.AssetType.Hero,
                    ELibraryAssetType.Logo => AppDataCommonSection.LibraryAssetsFullT.AssetType.Logo,
                    ELibraryAssetType.Portrait => AppDataCommonSection.LibraryAssetsFullT.AssetType.Portrait,
                    _ => throw new ArgumentOutOfRangeException(nameof(type))
                };

                var assetFilename = Common.LibraryAssetsFull.GetAssetFilename(kvType, true, ELanguage.English);
                if (!string.IsNullOrEmpty(assetFilename))
                {
                    object? properties = null;
                    if (type == ELibraryAssetType.Logo)
                    {
                        IAppAssetsInterface.LogoHAlign hAlign = IAppAssetsInterface.LogoHAlign.Center;
                        IAppAssetsInterface.LogoVAlign vAlign = IAppAssetsInterface.LogoVAlign.Center;

                        switch (Common.LibraryAssetsFull.LogoPinnedPosition)
                        {
                            case "BottomLeft":
                            {
                                hAlign = IAppAssetsInterface.LogoHAlign.Left;
                                vAlign = IAppAssetsInterface.LogoVAlign.Bottom;
                                break;
                            }

                            case "BottomCenter":
                            {
                                hAlign = IAppAssetsInterface.LogoHAlign.Center;
                                vAlign = IAppAssetsInterface.LogoVAlign.Bottom;
                                break;
                            }

                            case "UpperCenter":
                            {
                                hAlign = IAppAssetsInterface.LogoHAlign.Center;
                                vAlign = IAppAssetsInterface.LogoVAlign.Top;
                                break;
                            }

                            case "CenterCenter":
                            {
                                hAlign = IAppAssetsInterface.LogoHAlign.Center;
                                vAlign = IAppAssetsInterface.LogoVAlign.Center;
                                break;
                            }
                        }

                        properties = new IAppAssetsInterface.LogoPositionData(
                                Common.LibraryAssetsFull.LogoWidthPercentage,
                                Common.LibraryAssetsFull.LogoHeightPercentage, hAlign, vAlign);
                    }

                    assets.Add(new SteamLibraryAssets(this)
                    {
                        Type = type,
                        Properties = properties,
                        Uri = new Uri($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{AppID}/{assetFilename}") //TODO: Should this be hardcoded?
                    });
                }
            }
        }

        assets.Add(new SteamLibraryAssets(this) {Type = ELibraryAssetType.Icon, Uri = new Uri($"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{AppID}/{Common.Icon}.jpg")});
		Assets = assets;
	}

	private class SteamLibraryAssets(SteamApp app) : IAppAssetsInterface.ILibraryAsset
	{
		public required ELibraryAssetType Type { get; init; }
		public required Uri? Uri { get; init; }
		public bool NeedsUpdate { get; }
		public string? LocalPath { get; private set; }
		public object? Properties { get; init; }

		public void SetLocalPath(string? path)
		{
			app.AssetCached?.Invoke(this, new IAppAssetsInterface.AssetEventArgs(Type));
			LocalPath = path;
		}
	}

    public override string ToString() => $"SteamApp_{ID}";

    EAppError IAppInstallInterface.Install(LibraryFolder_t folder) =>
        _steamClient.IClientAppManager.InstallApp(AppID, folder, false);
    public EAppError Uninstall() => _steamClient.AppManagerHelper.UninstallApp(AppID);
    public bool IsInstalled => _steamClient.AppManagerHelper.IsAppInstalled(AppID);
    public void PauseInstall() => _steamClient.AppManagerHelper.EnableDownloads = false;

    public bool StartUpdate() =>
        _steamClient.AppManagerHelper.UpdateApp(AppID);
}
