using System;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSteamworks;
using OpenSteamworks.Client.Config;
using OpenSteamworks.Client.Enums;
using OpenSteamworks.Client.Utils;
using OpenSteamworks.Helpers;
using OpenSteamworks.Data.Structs;
using OpenSteamworks.Data;
using OpenSteamClient.DI;
using OpenSteamworks.Data.Enums;

namespace OpenSteamClient.ViewModels.Downloads;

public partial class DownloadItemViewModel : AvaloniaCommon.ViewModelBase, IDisposable {
    private const ulong MaximumPlausibleTransferSize = 16UL * 1024 * 1024 * 1024 * 1024;

    public string Name => AvaloniaApp.Container.Get<AppsHelper>().GetAppLocalizedName(AppID);

    [ObservableProperty]
    private AppId_t _appID;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _currentDownloadProgress;

    public string ProgressText => $"{CurrentDownloadProgress:P0}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransferDetails))]
    private string _downloadSize = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransferDetails))]
    private string _diskSize = string.Empty;

    public bool HasTransferDetails => !string.IsNullOrEmpty(DownloadSize) || !string.IsNullOrEmpty(DiskSize);

    [ObservableProperty]
    private DateTime? _downloadStarted;

    [ObservableProperty]
    private DateTime? _downloadFinished;

    [ObservableProperty]
    private DateTime? _estimatedCompletion;

    [ObservableProperty]
    private DateTime? _scheduledFor;

    private readonly DownloadsHelper _downloadsHelper;
    private readonly AppManagerHelper _appManagerHelper;
    private readonly bool _listensForUpdates;
    public DownloadItemViewModel(DownloadsHelper downloadsHelper, AppManagerHelper appManagerHelper, AppId_t appid, DateTime? scheduledFor = null, bool listenForUpdates = true) {
        AppID = appid;
        ScheduledFor = scheduledFor;
        this._downloadsHelper = downloadsHelper;
        _appManagerHelper = appManagerHelper;
        _listensForUpdates = listenForUpdates;
        if (_listensForUpdates) {
            this._downloadsHelper.DownloadChanged += OnDownloadChanged;
        }
    }

    private void OnDownloadChanged(object? sender, DownloadsHelper.DownloadChangedEventArgs e)
    {
        if (e.DownloadingAppID != AppID) {
            return;
        }

        Update(e);
    }

    public void Update(DownloadsHelper.DownloadChangedEventArgs e)
    {
        if (e.DownloadFinished != DateTime.MinValue) {
            DownloadFinished = e.DownloadFinished;
            return;
        }

        var hasValidDownloadStats = IsValidProgress(e.TotalDownloaded, e.TotalToDownload);
        var hasValidDiskStats = IsValidProgress(e.TotalProcessed, e.TotalToProcess);

        if (hasValidDiskStats) {
            CurrentDownloadProgress = Math.Clamp((double)e.TotalProcessed / e.TotalToProcess, 0, 1);
        } else if (hasValidDownloadStats) {
            CurrentDownloadProgress = Math.Clamp((double)e.TotalDownloaded / e.TotalToDownload, 0, 1);
        } else {
            CurrentDownloadProgress = 0;
        }

        DownloadSize = hasValidDownloadStats
            ? DataUnitStrings.GetStringForSize(e.TotalToDownload, DataSizeUnit.Auto_GB_MB_KB_B)
            : "Calculating…";
        DiskSize = hasValidDiskStats
            ? DataUnitStrings.GetStringForSize(e.TotalToProcess, DataSizeUnit.Auto_GB_MB_KB_B)
            : "Calculating…";


        this.DownloadStarted = e.DownloadStarted;
        this.DownloadFinished = e.DownloadFinished;
        this.EstimatedCompletion = DateTime.Now + e.EstimatedTimeRemaining;
    }

    private static bool IsValidProgress(ulong completed, ulong total) =>
        total > 0 &&
        total <= MaximumPlausibleTransferSize &&
        completed <= total;

    [RelayCommand]
    private void Pause() =>
        _appManagerHelper.ChangeAppDownloadQueuePlacement(AppID, EAppDownloadQueuePlacement.PriorityPaused);

    [RelayCommand]
    private void Resume()
    {
        _appManagerHelper.ChangeAppDownloadQueuePlacement(AppID, EAppDownloadQueuePlacement.PriorityUserInitiated);
        _appManagerHelper.EnableDownloads = true;
    }

    public void Dispose()
    {
        if (_listensForUpdates) {
            _downloadsHelper.DownloadChanged -= OnDownloadChanged;
        }
        GC.SuppressFinalize(this);
    }
}
