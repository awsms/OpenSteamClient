using System;
using OpenSteamClient.Translation;
using OpenSteamClient.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenSteamworks;
using OpenSteamworks.Client;
using OpenSteamworks.Client.Managers;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Generated;
using OpenSteamworks.Data.Structs;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpenSteamworks.Helpers;
using System.Linq;
using AvaloniaCommon;
using OpenSteamworks.Utils;
using OpenSteamworks.Callbacks;
using OpenSteamworks.Callbacks.Structs;
using System.ComponentModel;
using System.Collections.Generic;
using OpenSteamworks.Client.Config;
using OpenSteamworks.Data;
using OpenSteamClient.Services;

namespace OpenSteamClient.ViewModels;

public partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly TranslationManager _tm;
    private readonly ISteamClient _client;
    private readonly LoginManager _loginManager;
    private readonly SettingsWindow _settingsWindow;
    private readonly AppsHelper _clientApps;
    private readonly AppManagerHelper _appManagerHelper;
    private readonly CompatHelper _compatManager;
    private readonly ConfigManager _configManager;
    private readonly InstallManager _installManager;

    public SettingsWindowViewModel(ConfigManager configManager, AppsHelper clientApps, AppManagerHelper appManagerHelper, CallbackManager callbackManager, SettingsWindow settingsWindow, CompatHelper compatManager, ISteamClient client, TranslationManager tm, LoginManager loginManager, InstallManager installManager)
    {
        callbackManager.Register<LibraryFoldersChanged_t>(OnLibraryFoldersChanged);
        _configManager = configManager;
        _compatManager = compatManager;
        _clientApps = clientApps;
        _appManagerHelper = appManagerHelper;
        _settingsWindow = settingsWindow;
        _client = client;
        _tm = tm;
        _loginManager = loginManager;
        _installManager = installManager;
        PropertyChanged += SelfOnPropertyChanged;
        RefreshLibraryFolders();
        RefreshCompatTools();
        RefreshLanguages();
    }

    private void SelfOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SelectedLibraryFolder):
                RefreshGamesList();
                break;
            case nameof(SelectedCompatTool):
                SelectedCompatToolChanged();
                break;
            case nameof(SelectedLanguage):
                SelectedLanguageChanged();
                break;
        }
    }

    // Library folders window
    private void OnLibraryFoldersChanged(ICallbackHandler handler, in LibraryFoldersChanged_t folder)
        => AvaloniaApp.Current?.RunOnUIThread(DispatcherPriority.Background, RefreshLibraryFolders);


    public ObservableCollectionEx<LibraryFolderViewModel> LibraryFolders { get; } = new();
    public ObservableCollectionEx<string> AppsInCurrentLibraryFolder { get; } = new();

    [ObservableProperty]
    private LibraryFolderViewModel? _selectedLibraryFolder;

    [ObservableProperty]
    private int _selectedLibraryFolderIdx;

    public void LibraryFolders_OnAddClicked() {
        AvaloniaApp.Current?.RunOnUIThread(DispatcherPriority.Send, async () =>
        {
            var files = await _settingsWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = _tm.GetTranslationForKey("#LibraryFolders_PathToNewLibraryFolder"),
                AllowMultiple = false
            });

            if (files.Count >= 1)
            {
                var newFolder = _appManagerHelper.AddLibraryFolder(files[0].Path.AbsolutePath);
                Console.WriteLine("Got new folder " + newFolder);

                if (newFolder > 0) {
                    RefreshLibraryFolders();
                } else {
                    MessageBox.Show(_tm.GetTranslationForKey("#LibraryFolders_FailedToAddNewFolderTitle"), _tm.GetTranslationForKey("#LibraryFolders_FailedToAddNewFolder"));
                }
            }
        });
    }

    public void LibraryFolders_OnRemoveClicked() {
        if (SelectedLibraryFolder == null) {
            return;
        }

        if (!_appManagerHelper.RemoveLibraryFolder(SelectedLibraryFolder.ID, out AppId_t? inUseByApp)) {
            MessageBox.Show(_tm.GetTranslationForKey("#LibraryFolders_FailedToRemoveFolderTitle"), string.Empty, string.Format(_tm.GetTranslationForKey("#LibraryFolders_FailedToRemoveFolder"), _clientApps.GetAppLocalizedName(inUseByApp.Value)), AvaloniaCommon.Enums.MessageBoxIcon.ERROR);
        }
    }

    private void RefreshLibraryFolders() {
        LibraryFolders.BlockUpdates = true;

        LibraryFolders.Clear();
        LibraryFolders.AddRange(LibraryFolderViewModel.GetLibraryFolders(_appManagerHelper));

        LibraryFolders.BlockUpdates = false;
        LibraryFolders.FireReset();


        SelectedLibraryFolder = LibraryFolders.FirstOrDefault();
        SelectedLibraryFolderIdx = 0;

        RefreshGamesList();
    }

    private void RefreshGamesList() {
        AppsInCurrentLibraryFolder.BlockUpdates = true;
        AppsInCurrentLibraryFolder.Clear();
        if (SelectedLibraryFolder != null) {
            AppsInCurrentLibraryFolder.AddRange(_appManagerHelper.GetAppsInFolder(SelectedLibraryFolder.ID).Select(appid => _clientApps.GetAppLocalizedName(appid)));
        }

        AppsInCurrentLibraryFolder.BlockUpdates = false;
        AppsInCurrentLibraryFolder.FireReset();
    }

    // Compat tools

    public ObservableCollectionEx<IDNameViewModel> CompatTools { get; } = new();

    [ObservableProperty]
    private IDNameViewModel? _selectedCompatTool;

    private void SelectedCompatToolChanged()
    {
	    if (SelectedCompatTool == null)
		    return;

	    _compatManager.SetCompatToolForWindowsTitles(SelectedCompatTool.ID, string.Empty);
    }

    private void RefreshCompatTools() {
        CompatTools.Clear();
        CompatTools.AddRange(_compatManager.GetCompatTools(ERemoteStoragePlatform.PlatformWindows).Select(id => new IDNameViewModel(id, _compatManager.GetCompatToolDisplayName(id))));

        SelectedCompatTool = CompatTools.Find(t => t.ID == _compatManager.GetAppCompatTool(0));
    }

    // Friends

    public bool AutologinToFriendsNetwork {
        get => _configManager.Get<UserSettings>().LoginToFriendsNetworkAutomatically;
        set => _configManager.Get<UserSettings>().LoginToFriendsNetworkAutomatically = value;
    }

    // OpenSteamClient

    public bool ClientHardwareAcceleration
    {
        get => _configManager.Get<GlobalSettings>().ClientHardwareAcceleration;
        set
        {
            var settings = _configManager.Get<GlobalSettings>();
            if (settings.ClientHardwareAcceleration == value)
            {
                return;
            }

            settings.ClientHardwareAcceleration = value;
            _configManager.Save(settings);
            OnPropertyChanged();
        }
    }

    public bool WebhelperHardwareAcceleration
    {
        get => _configManager.Get<GlobalSettings>().WebhelperGPUAcceleration;
        set
        {
            var settings = _configManager.Get<GlobalSettings>();
            if (settings.WebhelperGPUAcceleration == value)
            {
                return;
            }

            settings.WebhelperGPUAcceleration = value;
            _configManager.Save(settings);
            OnPropertyChanged();
        }
    }

    public void ImportSteamSettings()
    {
        var dialog = new SteamSettingsImportDialog();
        dialog.DataContext = new SteamSettingsImportDialogViewModel(
            dialog,
            new SteamSettingsImportService(_client, _installManager));
        _ = dialog.ShowDialog(_settingsWindow);
    }

    // Localization
    public ObservableCollectionEx<IDNameViewModel> Languages { get; } = new();

    [ObservableProperty]
    private IDNameViewModel? _selectedLanguage;

    private void SelectedLanguageChanged() {
        if (SelectedLanguage == null) {
            return;
        }

        _tm.SetLanguage(ELanguageConversion.ELanguageFromAPIName(SelectedLanguage.ID));
    }

    private void RefreshLanguages() {
        Languages.Clear();
        foreach (var item in Enum.GetValues<ELanguage>())
        {
            bool hasUITranslation = _tm.HasUITranslation(item, out string? translationName);
            if (!hasUITranslation) {
                translationName = item.ToString();
            } else {
                translationName += " (UI)";
            }

            string key = item.ToAPIName();
            Languages.Add(new IDNameViewModel(key, translationName));
        }

        SelectedLanguage = Languages.Find(l => l.ID == _tm.CurrentTranslation.Language.ToAPIName());
    }
}
