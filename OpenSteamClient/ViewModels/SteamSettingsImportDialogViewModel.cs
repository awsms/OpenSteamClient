using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaloniaCommon;
using OpenSteamClient.Services;
using OpenSteamClient.Views;

namespace OpenSteamClient.ViewModels;

public partial class SteamSettingsImportDialogViewModel : ViewModelBase
{
    private readonly SteamSettingsImportDialog _dialog;
    private readonly SteamSettingsImportService _service;
    private readonly SteamSettingsSnapshot? _snapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool importLaunchOptions = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool importOverlaySettings;

    [ObservableProperty]
    private bool overwriteExisting;

    [ObservableProperty]
    private string status = string.Empty;

    public int LaunchOptionsCount => _snapshot?.LaunchOptions.Count ?? 0;
    public int OverlaySettingsCount => _snapshot?.OverlaySettings.Count ?? 0;
    public string SourcePath => _snapshot?.SourcePath ?? string.Empty;
    public bool HasSource => _snapshot is not null;
    public bool CanImport => HasSource && (ImportLaunchOptions || ImportOverlaySettings);

    public SteamSettingsImportDialogViewModel(
        SteamSettingsImportDialog dialog,
        SteamSettingsImportService service)
    {
        _dialog = dialog;
        _service = service;

        try
        {
            _snapshot = service.ReadSteamSettings();
            Status = $"Found {LaunchOptionsCount} launch-option entries and {OverlaySettingsCount} overlay overrides.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        if (_snapshot is null)
            return;

        var result = _service.Import(
            _snapshot,
            ImportLaunchOptions,
            ImportOverlaySettings,
            OverwriteExisting);

        Status = $"Imported {result.Imported}; skipped {result.Skipped}; failed {result.Failed}.";
    }

    partial void OnImportLaunchOptionsChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();
    partial void OnImportOverlaySettingsChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Close() => _dialog.Close();
}
