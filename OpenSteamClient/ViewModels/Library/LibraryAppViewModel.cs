using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenSteamClient.ViewModels.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Data.Structs;
using OpenSteamClient.DI;
using OpenSteamworks.Client.Apps.Assets;
using OpenSteamworks.Data.Enums;

namespace OpenSteamClient.ViewModels.Library;

public partial class LibraryAppViewModel : Node
{
    public IApp App { get; init; }
    public bool IsInstalled => App is IAppInstallInterface { IsInstalled: true };

    public LibraryAppViewModel(CGameID gameid)
    {
        this.HasIcon = true;
        this.IsApp = true;

        App = AvaloniaApp.Container.Get<AppsManager>().GetApp(gameid);
        this.GameID = App.ID;

        SetLibraryAssets();
        SetStatusIcon();
        CalculateName();

        if (App is IAppAssetsInterface assetsInterface)
        {
            assetsInterface.AssetUpdated += OnLibraryAssetUpdated;
        }

        App.PropertyChanged += App_OnPropertyChanged;
    }

    private void App_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(App.Name) or nameof(App.State))
        {
            CalculateName();
            OnPropertyChanged(nameof(IsInstalled));
        }
    }

    private void CalculateName()
    {
        StringBuilder name = new StringBuilder(72);
        name.Append(App.Name);
        bool isColored = true;

        if (App is IAppConfigInterface configInterface && configInterface.TryGetConfigValue(IAppConfigInterface.ConfigKey.ACTIVE_BETA, out object? val) && val is string beta)
        {
            if (!string.IsNullOrEmpty(beta) && beta != "public")
            {
                name.Append($" [{beta}]");
            }
        }

        if (App.State.HasFlag(EAppState.Uninstalling))
        {
            name.Append(" - Uninstalling");
        } else if (App.State.HasFlag(EAppState.MovingFolder))
        {
            name.Append(" - Moving");
        } else if (App.State.HasFlag(EAppState.Terminating))
        {
            name.Append(" - Stopping");
        } else if (App.State.HasFlag(EAppState.AppRunning))
        {
            name.Append(" - Running");
        } else if (App.State.HasFlag(EAppState.UpdateRunning))
        {
            //TODO: This should be in the format of "- {updateProgressPct}%"
            name.Append(" - Update Running");
        } else if (App.State.HasFlag(EAppState.UpdatePaused))
        {
            name.Append(" - Update Paused");
        } else if (App.State.HasFlag(EAppState.UpdateQueued))
        {
            name.Append(" - Update Queued");
        } else if (App.State.HasFlag(EAppState.UpdateRequired))
        {
            name.Append(" - Update Required");
        } else if (App.State.HasFlag(EAppState.UpdatePaused))
        {
            name.Append(" - Update Paused");
        }
        else
        {
            isColored = false;
        }

        Name = name.ToString();

        //TODO: Not ideal, we can't use theme default
        if (!isColored && App is IAppInstallInterface installInterface)
        {
            Foreground = installInterface.IsInstalled ? Brushes.White : Brushes.DarkGray;
        }
        else
        {
            Foreground = isColored ? Brushes.Aquamarine : Brushes.White;
        }
    }

    private void SetLibraryAssets()
    {
        string? localIconPath = null;
        if (App is IAppAssetsInterface assetsInterface)
        {
            localIconPath = assetsInterface.Assets.FirstOrDefault(a => a.Type == ELibraryAssetType.Icon)?.LocalPath;
        }

        // Constructing an ImageBrush needs to happen on the main thread (strange design but sure, whatever)
        AvaloniaApp.Current?.RunOnUIThread(DispatcherPriority.Send, () =>
        {
            if (!string.IsNullOrEmpty(localIconPath))
            {
                this.Icon = new ImageBrush()
                {
                    Source = new Bitmap(localIconPath),
                };
            }
            else
            {
                this.Icon = Brushes.DarkGray;
            }
        });
    }

    private void SetStatusIcon()
    {
        StatusIcon = Brushes.Transparent;
        //TODO: Cloud save icon
    }

    public void OnLibraryAssetUpdated(object? sender, IAppAssetsInterface.AssetEventArgs assetEventArgs)
    {
        Dispatcher.UIThread.Invoke(SetLibraryAssets);
    }
}
