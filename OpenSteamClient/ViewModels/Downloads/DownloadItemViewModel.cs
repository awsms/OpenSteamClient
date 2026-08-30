using System;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private readonly bool _listensForUpdates;
    public DownloadItemViewModel(DownloadsHelper downloadsHelper, AppId_t appid, DateTime? scheduledFor = null, bool listenForUpdates = true) {
        AppID = appid;
        ScheduledFor = scheduledFor;
        this._downloadsHelper = downloadsHelper;
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

        //Console.WriteLine($"{AppID}: " + updateInfo.ToString());
        if (e.TotalToProcess != 0 && e.TotalToProcess != e.TotalProcessed) {
            this.CurrentDownloadProgress = (double)e.TotalProcessed / e.TotalToProcess;
        } else if (e.TotalToDownload != 0 && e.TotalToDownload != e.TotalDownloaded) {
            this.CurrentDownloadProgress = (double)e.TotalDownloaded / e.TotalToDownload;
        }

        this.DownloadSize = DataUnitStrings.GetStringForSize(e.TotalToDownload, DataSizeUnit.Auto_GB_MB_KB_B);
        this.DiskSize = DataUnitStrings.GetStringForSize(e.TotalToProcess, DataSizeUnit.Auto_GB_MB_KB_B);


        this.DownloadStarted = e.DownloadStarted;
        this.DownloadFinished = e.DownloadFinished;
        this.EstimatedCompletion = DateTime.Now + e.EstimatedTimeRemaining;
    }

    public void Dispose()
    {
        if (_listensForUpdates) {
            _downloadsHelper.DownloadChanged -= OnDownloadChanged;
        }
        GC.SuppressFinalize(this);
    }
}
