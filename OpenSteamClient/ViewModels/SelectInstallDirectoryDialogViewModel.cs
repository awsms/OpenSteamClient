using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using AvaloniaCommon;
using OpenSteamClient.Extensions;
using OpenSteamClient.Translation;
using OpenSteamClient.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Helpers;

namespace OpenSteamClient.ViewModels;

public partial class SelectInstallDirectoryDialogViewModel : AvaloniaCommon.ViewModelBase {
    public ObservableCollection<LibraryFolderViewModel> LibraryFolders { get; init; }

    [ObservableProperty]
    private LibraryFolderViewModel? selectedLibraryFolder;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? textBlockText;

    private readonly IApp app;
    private readonly SelectInstallDirectoryDialog dialog;
    private readonly AppManagerHelper appManagerHelper;

    public SelectInstallDirectoryDialogViewModel(AppManagerHelper appManagerHelper, TranslationManager tm, SelectInstallDirectoryDialog dialog, IApp app) {
        this.appManagerHelper = appManagerHelper;
        this.app = app;
        this.dialog = dialog;
        Title = string.Format(tm.GetTranslationForKey("#SelectInstallDirectoryDialog_Title"), app.Name);
        TextBlockText = string.Format(tm.GetTranslationForKey("#SelectInstallDirectoryDialog_SelectLibraryFolder"), app.Name);

        LibraryFolders = new(LibraryFolderViewModel.GetLibraryFolders(appManagerHelper));
        SelectedLibraryFolder = LibraryFolders.FirstOrDefault();
    }

    public void OnCancelClicked() {
        dialog.Close();
    }

    public void OnInstallClicked() {
        if (SelectedLibraryFolder is null)
        {
            MessageBox.Error("Installation failed", "No mounted Steam library folder is available.");
            return;
        }

        if (app is IAppInstallInterface installInterface)
        {
            Console.WriteLine("Installing " + app.Name + " to " + SelectedLibraryFolder.Path);
            var error = installInterface.Install(SelectedLibraryFolder.ID);
            if (error != OpenSteamworks.Data.Enums.EAppError.NoError)
            {
                MessageBox.Error("Installation failed", $"Failed to install {app.Name}: {error}");
                return;
            }
        }
        else
        {
            MessageBox.Error("Installation failed", $"{app.Name} does not support installation.");
            return;
        }

        dialog.Close();
    }
}
