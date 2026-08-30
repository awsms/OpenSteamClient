using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSteamworks;
using OpenSteamworks.Data;
using OpenSteamworks.Client.Config;
using OpenSteamworks.Client.Enums;
using OpenSteamworks.Client.Utils;
using OpenSteamworks.Helpers;
using OpenSteamworks.Generated;

namespace OpenSteamClient.ViewModels.Downloads;

public partial class DownloadsPageViewModel : AvaloniaCommon.ViewModelBase, IDisposable {
    public ObservableCollection<DownloadItemViewModel> DownloadQueue { get; init; } = new();
    public ObservableCollection<DownloadItemViewModel> ScheduledDownloads { get; init; } = new();
    public ObservableCollection<DownloadItemViewModel> UnscheduledDownloads { get; init; } = new();


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private DownloadItemViewModel? _currentDownload;

    public bool IsDownloading => CurrentDownload is not null;
    public bool IsIdle => CurrentDownload is null;

    public bool HasQueuedDownloads => DownloadQueue.Count > 0;
    public bool HasScheduledDownloads => ScheduledDownloads.Count > 0;
    public bool HasUnscheduledDownloads => UnscheduledDownloads.Count > 0;

    [ObservableProperty]
    private ulong _peakDownloadRateNum;

    [ObservableProperty]
    private ulong _peakDiskRateNum;

    [ObservableProperty]
    private string _currentDownloadRate;

    [ObservableProperty]
    private string _currentDiskRate;

    [ObservableProperty]
    private string _peakDownloadRate;

    [ObservableProperty]
    private string _peakDiskRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalPauseButtonText))]
    private bool _downloadsPaused;

    public string GlobalPauseButtonText => DownloadsPaused ? "Resume all" : "Pause all";

    private readonly DownloadsHelper _downloadManager;
    private readonly AppManagerHelper _appManagerHelper;
    private readonly IClientAppManager _clientAppManager;
    private readonly UserSettings _userSettings;
    public DownloadsPageViewModel(DownloadsHelper downloadManager, AppManagerHelper appManagerHelper, IClientAppManager clientAppManager, UserSettings userSettings) {
        this._userSettings = userSettings;
        this._downloadManager = downloadManager;
        _appManagerHelper = appManagerHelper;
        _clientAppManager = clientAppManager;
        DownloadsPaused = !appManagerHelper.EnableDownloads;
        downloadManager.DownloadChanged += OnDownloadChanged;
        downloadManager.DownloadScheduleChanged += OnDownloadQueueChanged;
        UpdateRates(new());
    }

    private void OnDownloadChanged(object? sender, DownloadsHelper.DownloadChangedEventArgs e)
    {
        UpdateRates(e);
    }

    private void OnDownloadQueueChanged(object? sender, DownloadsHelper.DownloadScheduleChangedEventArgs e)
    {
        // Update download queue
        DisposeItems(DownloadQueue);
        this.DownloadQueue.Clear();
        foreach (var newitem in e.QueuedApps)
        {
            this.DownloadQueue.Add(CreateDownloadItem(newitem));
        }
        OnPropertyChanged(nameof(HasQueuedDownloads));

        // Update scheduled downloads
        DisposeItems(ScheduledDownloads);
        this.ScheduledDownloads.Clear();
        foreach (var newitem in e.ScheduledApps)
        {
            this.ScheduledDownloads.Add(CreateDownloadItem(newitem.Key, newitem.Value));
        }
        OnPropertyChanged(nameof(HasScheduledDownloads));

        // Update unscheduled downloads
        DisposeItems(UnscheduledDownloads);
        this.UnscheduledDownloads.Clear();
        foreach (var newitem in e.UnscheduledApps)
        {
            this.UnscheduledDownloads.Add(CreateDownloadItem(newitem));
        }
        OnPropertyChanged(nameof(HasUnscheduledDownloads));
    }

#pragma warning disable MVVMTK0034
    [MemberNotNull(nameof(_currentDownloadRate))]
    [MemberNotNull(nameof(_currentDiskRate))]
    [MemberNotNull(nameof(_peakDownloadRate))]
    [MemberNotNull(nameof(_peakDiskRate))]
#pragma warning restore MVVMTK0034
    private void UpdateRates(DownloadsHelper.DownloadChangedEventArgs downloadStats) {
        if (downloadStats.DownloadingAppID != 0) {
            if (CurrentDownload?.AppID != downloadStats.DownloadingAppID) {
                CurrentDownload?.Dispose();
                CurrentDownload = CreateDownloadItem(downloadStats.DownloadingAppID, listenForUpdates: false);
            }

            CurrentDownload.Update(downloadStats);
        } else {
            CurrentDownload?.Dispose();
            this.CurrentDownload = null;
        }

        if (downloadStats.DownloadRate > PeakDownloadRateNum) {
            PeakDownloadRateNum = downloadStats.DownloadRate;
        }

        if (downloadStats.DiskRate > PeakDiskRateNum) {
            PeakDiskRateNum = downloadStats.DiskRate;
        }

        CurrentDownloadRate = DataUnitStrings.GetStringForDownloadSpeed(downloadStats.DownloadRate, _userSettings.DownloadDataRateUnit);
        CurrentDiskRate = DataUnitStrings.GetStringForDownloadSpeed(downloadStats.DiskRate, _userSettings.DownloadDataRateUnit);
        PeakDownloadRate = DataUnitStrings.GetStringForDownloadSpeed(PeakDownloadRateNum, _userSettings.DownloadDataRateUnit);
        PeakDiskRate = DataUnitStrings.GetStringForDownloadSpeed(PeakDiskRateNum, _userSettings.DownloadDataRateUnit);
        DownloadsPaused = downloadStats.Paused || !_appManagerHelper.EnableDownloads;
    }

    [RelayCommand]
    private void ToggleAllDownloads()
    {
        _appManagerHelper.EnableDownloads = DownloadsPaused;
        DownloadsPaused = !_appManagerHelper.EnableDownloads;
    }

    public void MoveDownloadBefore(DownloadItemViewModel source, DownloadItemViewModel target)
    {
        if (source.AppID == target.AppID) {
            return;
        }

        var targetIndex = _clientAppManager.GetAppDownloadQueueIndex(target.AppID);
        if (targetIndex >= 0) {
            _clientAppManager.SetAppDownloadQueueIndex(source.AppID, targetIndex);
        }
    }

    public void MoveDownloadBefore(string sourceAppID, DownloadItemViewModel target)
    {
        var source = DownloadQueue.FirstOrDefault(item => item.AppID.ToString() == sourceAppID);
        if (source is not null) {
            MoveDownloadBefore(source, target);
        }
    }

    private DownloadItemViewModel CreateDownloadItem(AppId_t appID, DateTime? scheduledFor = null, bool listenForUpdates = true) =>
        new(_downloadManager, _appManagerHelper, _clientAppManager, appID, scheduledFor, listenForUpdates);

    private static void DisposeItems(IEnumerable<DownloadItemViewModel> items)
    {
        foreach (var item in items) {
            item.Dispose();
        }
    }

    public void Dispose()
    {
        _downloadManager.DownloadChanged -= OnDownloadChanged;
        _downloadManager.DownloadScheduleChanged -= OnDownloadQueueChanged;
        CurrentDownload?.Dispose();
        DisposeItems(DownloadQueue);
        DisposeItems(ScheduledDownloads);
        DisposeItems(UnscheduledDownloads);
        GC.SuppressFinalize(this);
    }
}
