using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Media;
using AvaloniaCommon;
using OpenSteamClient.Controls;
using OpenSteamClient.Translation;
using OpenSteamClient.ViewModels.Downloads;
using OpenSteamClient.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenSteamworks;
using OpenSteamworks.Callbacks;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Client.Managers;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Generated;
using OpenSteamworks.Protobuf;
using OpenSteamClient.DI;
using OpenSteamClient.Logging;
using OpenSteamClient.DI.Lifetime;

namespace OpenSteamClient.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _showGoOffline = false;

    [ObservableProperty]
    private bool _showGoOnline = false;

    [ObservableProperty]
    private BasePage _currentPage;

    private PageHeaderViewModel? _currentPageHeader;
    private readonly Dictionary<Type, BasePage> _loadedPages = new();
    public ObservableCollection<PageHeaderViewModel> PageList { get; } = new() { };
    public bool IsDebug => AvaloniaApp.DebugEnabled;

    public bool CanLogonOffline => _client.IClientUser.CanLogonOffline() == 1;
    public bool IsOfflineMode => _client.IClientUtils.GetOfflineMode();
    private readonly Action _openSettingsWindow;
    private readonly TranslationManager _tm;
    private readonly ISteamClient _client;
    private readonly LoginManager _loginManager;
    private readonly AppsManager _appsManager;
    private readonly MainWindow _mainWindow;
	private readonly ILogger _logger;

    public MainWindowViewModel(MainWindow mainWindow, ISteamClient client, AppsManager appsManager, TranslationManager tm, LoginManager loginManager, ILoggerFactory loggerFactory, Action openSettingsWindowAction)
    {
		_logger = loggerFactory.CreateLogger("MainWindowViewModel");
        _mainWindow = mainWindow;
        _client = client;
        _tm = tm;
        _loginManager = loginManager;
        ShowGoOffline = CanLogonOffline && !IsOfflineMode;
        ShowGoOnline = CanLogonOffline && IsOfflineMode;
        _openSettingsWindow = openSettingsWindowAction;
        _appsManager = appsManager;

        _client.CallbackManager.Register(1210004, OnCGameNetworkingUI_AppSummary);
        _client.CallbackManager.Register(1210001, OnClientNetworking_ConnectionStateChanged);

        //TODO: Ideally we'd embed CEF ourselves (steamwebhelper does not seem very good for that purpose)
        PageList.Add(new(this, "Store", "#Tab_Store", typeof(StorePage), typeof(ViewModelBase)));
        PageList.Add(new(this, "Library", "#Library", typeof(LibraryPage), typeof(LibraryPageViewModel)));
        //TODO: this isn't final, we might move downloads to the bottom still
        PageList.Add(new(this, "Downloads", "#Tab_Downloads", typeof(DownloadsPage), typeof(DownloadsPageViewModel)));
        PageList.Add(new(this, "Community", "#Tab_Community", typeof(CommunityPage), typeof(ViewModelBase)));
        PageList.Add(new(this, "Console", "#Tab_Console", typeof(ConsolePage), typeof(ConsolePageViewModel)));

        SwitchToPage(typeof(LibraryPage));
    }

#pragma warning disable MVVMTK0034
    [MemberNotNull(nameof(_currentPage))]
    [MemberNotNull(nameof(CurrentPage))]
#pragma warning restore MVVMTK0034
    internal void SwitchToPage(Type pageType)
    {
        PageHeaderViewModel model = PageList.First(item => item.PageType == pageType);
        var (type, page) = _loadedPages.FirstOrDefault(item => item.Key == model.PageType);
        if (page == null)
        {
            page = model.PageCtor();
            page.DataContext = model.ViewModelCtor(page);
            _loadedPages.Add(model.PageType, page);
        }

        // Set selected button
        model.ButtonBackground = Brushes.Green;
        if (_currentPageHeader != null)
        {
            _currentPageHeader.ButtonBackground = AvaloniaApp.Theme!.ButtonBackground;
            if (_currentPageHeader.IsWebPage)
            {
                _currentPageHeader.ButtonBackground = AvaloniaApp.Theme!.AccentButtonBackground;
            }
        }

        CurrentPage = page;
        _currentPageHeader = model;
    }

    internal void UnloadPage(Type pageType)
    {
        if (!_loadedPages.ContainsKey(pageType))
        {
            return;
        }

        var loadedPage = _loadedPages[pageType];
        if (ReferenceEquals(CurrentPage, loadedPage))
        {
            // Detach the page from the visual tree before freeing its native web
            // browser and render resources.
            SwitchToPage(typeof(LibraryPage));
        }

        loadedPage.Free();
        loadedPage.DataContext = null;
        _loadedPages.Remove(pageType);
    }

    public void DBG_Crash() => throw new Exception("test");

    private void OnCGameNetworkingUI_AppSummary(ICallbackHandler handler, ReadOnlySpan<byte> data)
    {
        try
        {
            var dataoffset = data[8..];
            var parsed = CGameNetworkingUI_AppSummary.Parser.ParseFrom(dataoffset);
            Console.WriteLine("appid: " + parsed.Appid);
            Console.WriteLine("connections: " + parsed.ActiveConnections);
            Console.WriteLine("loss: " + parsed.MainCxn.PacketLoss);
            Console.WriteLine("ping: " + parsed.MainCxn.PingMs);
        }
        catch (Exception e)
        {
            _logger.Error(e);
        }
    }

    private void OnClientNetworking_ConnectionStateChanged(ICallbackHandler handler, ReadOnlySpan<byte> data)
    {
        try
        {
            var dataoffset = data[4..];

            var state = CGameNetworkingUI_ConnectionState.Parser.ParseFrom(dataoffset);
            _logger.Info("AddressRemote: " + state.AddressRemote);
            _logger.Info("state: " + state.ConnectionState);
            _logger.Info("appid: " + state.Appid);
            _logger.Info("relay: " + state.SdrpopidLocal);
            _logger.Info("datacenter: " + state.SdrpopidRemote);
            _logger.Info("statustoken: " + state.StatusLocToken);
            _logger.Info("server identity: " + state.IdentityRemote);
            _logger.Info("local identity: " + state.IdentityLocal);
            _logger.Info("ping: " + state.PingDefaultInternetRoute);
            _logger.Info("connected for: " + state.E2EQualityLocal.Lifetime.ConnectedSeconds);
        }
        catch (Exception e)
        {
            _logger.Error(e);
        }
    }

    // public void DBG_OpenInterfaceList() => AvaloniaApp.Current?.OpenInterfaceList();
    public void DBG_ChangeLanguage()
    {
        // Very simple logic, just switches between english and finnish.
        var tm = AvaloniaApp.Container.Get<TranslationManager>();

        ELanguage lang = tm.CurrentTranslation.Language;
        Console.WriteLine(string.Format(tm.GetTranslationForKey("#SettingsWindow_YourCurrentLanguage"), tm.GetTranslationForKey("#LanguageNameTranslated"), tm.CurrentTranslation.LanguageFriendlyName));
        tm.SetLanguage(lang == ELanguage.English ? ELanguage.Finnish : ELanguage.English);
    }

    public async void DBG_TestHTMLSurface()
    {
        HTMLSurfaceTest testWnd = new();
        testWnd.Show();
        await testWnd.Init("Valve Steam Client", "https://google.com/");
    }

    public void Quit() => AvaloniaApp.Current?.ExitEventually();

    public void OpenSettings() => _openSettingsWindow?.Invoke();

    public void GoOffline()
    {
        _client.IClientUtils.SetOfflineMode(true);
        ShowGoOffline = CanLogonOffline && !IsOfflineMode;
        ShowGoOnline = CanLogonOffline && IsOfflineMode;
    }
    public void GoOnline()
    {
        _client.IClientUtils.SetOfflineMode(false);
        ShowGoOffline = CanLogonOffline && !IsOfflineMode;
        ShowGoOnline = CanLogonOffline && IsOfflineMode;
    }

    public async void SignOut()
    {
        Progress<OperationProgress> operation = new();

        AvaloniaApp.Current?.ForceProgressWindow(new ProgressWindowViewModel(operation, "Logging off"));
        await _loginManager.LogoutAsync(operation, true);
    }

    public async void ChangeAccount() => await _loginManager.LogoutAsync();

    public void OpenFriendsList()
    {
        if (AvaloniaApp.Container.TryGet(out IClientFriends? friends)) {
            friends.OpenFriendsDialog();
        }
    }

    public void SetPersonaOnline() {
        if (AvaloniaApp.Container.TryGet(out IClientUser? user)) {
            user.SetSelfAsChatDestination(true);
        }

        if (AvaloniaApp.Container.TryGet(out IClientFriends? friends)) {
            friends.SetPersonaState(EPersonaState.Online);
        }
    }

    public void SetPersonaAway() {
        if (AvaloniaApp.Container.TryGet(out IClientFriends? friends)) {
            friends.SetPersonaState(EPersonaState.Away);
        }
    }

    public void SetPersonaInvisible() {
        if (AvaloniaApp.Container.TryGet(out IClientFriends? friends)) {
            friends.SetPersonaState(EPersonaState.Invisible);
        }
    }

    public void SetPersonaOffline() {
        if (AvaloniaApp.Container.TryGet(out IClientUser? user)) {
            user.SetSelfAsChatDestination(false);
        }

        if (AvaloniaApp.Container.TryGet(out IClientFriends? friends)) {
            friends.SetPersonaState(EPersonaState.Offline);
        }
    }
}
