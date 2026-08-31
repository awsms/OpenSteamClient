using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Reactive;
using Avalonia.Threading;
using OpenSteamClient.Extensions;
using OpenSteamClient.ViewModels.Library;
using OpenSteamClient.Views;
using OpenSteamClient.Views.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSteamworks.Callbacks;
using OpenSteamworks.Callbacks.Structs;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Data.Structs;
using OpenSteamworks.Utils;
using SkiaSharp;
using AvaloniaCommon;
using OpenSteamClient.DI;
using OpenSteamClient.Translation;
using OpenSteamworks.Client.Apps.Assets;
using OpenSteamworks.Data.KeyValue;
using OpenSteamworks.Helpers;

namespace OpenSteamClient.ViewModels.Library;

public partial class FocusedAppPaneViewModel : AvaloniaCommon.ViewModelBase
{
    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private IBrush hero;

    [ObservableProperty]
    private IImage? logo;

    [NotifyPropertyChangedFor(nameof(HasLogoAsText))]
    [ObservableProperty]
    private string logoAsText;

    [ObservableProperty]
    private string playButtonLocalizationToken;

    [ObservableProperty]
    private ICommand playButtonAction;

    [ObservableProperty]
    private bool playButtonEnabled;

    [ObservableProperty]
    private string updateButtonLocalizationToken;

    [ObservableProperty]
    private ICommand updateButtonAction;

    [ObservableProperty]
    private bool updateButtonVisible;

    [ObservableProperty]
    private double logoHeight;

    [ObservableProperty]
    private double logoWidth;

    [ObservableProperty]
    private double logoLeft;

    [ObservableProperty]
    private double logoTop;

    [ObservableProperty]
    private double logoRight;

    [ObservableProperty]
    private double logoBottom;

    [ObservableProperty]
    private VerticalAlignment logoVerticalAlignment;

    [ObservableProperty]
    private HorizontalAlignment logoHorizontalAlignment;

    private double HeroHeight => heroContainer.Bounds.Height;
    private double HeroWidth => heroContainer.Bounds.Width;

    public double LogoContainerHeight => HeroHeight - 32;
    public double LogoContainerWidth => HeroWidth - 64;
    public bool HasLogoAsText => !string.IsNullOrEmpty(LogoAsText);

    public IApp App { get; }
    private readonly FocusedAppPane pane;
    private readonly Grid heroContainer;

    public FocusedAppPaneViewModel(FocusedAppPane pane, CGameID gameid)
    {
        this.pane = pane;

        this.heroContainer = pane.Hero;

        LogoAsText = "";
        App = AvaloniaApp.Container.Get<AppsManager>().GetApp(gameid);

        AvaloniaApp.Container.Get<CallbackManager>().Register<AppEventStateChange_t>(OnAppEventStateChange);
        AvaloniaApp.Container.Get<CallbackManager>().Register<AppLaunchResult_t>(OnAppLaunchResult);
        this.Name = App.Name;
        this.heroContainer.GetObservable(Visual.BoundsProperty).Subscribe(new AnonymousObserver<Rect>(OnHeroBoundsChanged));
        SetLibraryAssets();

        if (App is IAppAssetsInterface assetsInterface)
        {
            assetsInterface.AssetUpdated += OnLibraryAssetsUpdated;
        }

        PlayButtonLocalizationToken = "Initial state";
        PlayButtonAction = new RelayCommand(InvalidAction);
        UpdateButtonLocalizationToken = "#App_UpdateApp";
        UpdateButtonAction = new RelayCommand(InvalidAction);
        UpdatePlayButton(App.State);
    }

    private void OnLibraryAssetsUpdated(object? sender, IAppAssetsInterface.AssetEventArgs e)
    {
        AvaloniaApp.Current?.RunOnUIThread(DispatcherPriority.Send, SetLibraryAssets);
    }

    private void OnAppLaunchResult(ICallbackHandler handler, in AppLaunchResult_t t)
    {
        if (t.m_GameID != App.ID)
            return;

        var error = t.m_eAppError;
        AvaloniaApp.Current?.RunOnUIThread(DispatcherPriority.Send, () =>
        {
            if (error != EAppError.NoError)
                MessageBox.Show("Launch failed", $"Launch failed with EResult: {error}");
            else
                UpdatePlayButton(EAppState.AppRunning);
        });
    }

    private void OnAppEventStateChange(ICallbackHandler handler, in AppEventStateChange_t change)
    {
        if (change.AppID != App.ID.AppID)
            return;

        var state = change.NewState;
        AvaloniaApp.Current?.RunOnUIThread(
            DispatcherPriority.Send,
            () => UpdatePlayButton(state));
    }

#pragma warning disable MVVMTK0034
    [MemberNotNull(nameof(playButtonAction))]
    [MemberNotNull(nameof(PlayButtonAction))]
    [MemberNotNull(nameof(playButtonLocalizationToken))]
    [MemberNotNull(nameof(PlayButtonLocalizationToken))]
#pragma warning restore MVVMTK0034
    private void UpdatePlayButton(EAppState state)
    {
        bool isAppPlayable = false;
        if (App is IAppLaunchInterface launchInterface)
        {
            isAppPlayable = launchInterface.LaunchOptions.Any();
        }

        PlayButtonEnabled = true;
        UpdateButtonVisible = false;
        UpdateButtonLocalizationToken = "#App_UpdateApp";
        UpdateButtonAction = new RelayCommand(Update);

        if (state.HasFlag(EAppState.AppRunning))
        {
            PlayButtonLocalizationToken = "#App_StopApp";
            PlayButtonAction = new RelayCommand(KillApp);
        }
        else if (state.HasFlag(EAppState.Terminating))
        {
            PlayButtonLocalizationToken = "#App_StoppingApp";
            PlayButtonAction = new RelayCommand(InvalidAction);
            PlayButtonEnabled = false;
        }
        else if (state.HasFlag(EAppState.UpdateRunning))
        {
            PlayButtonLocalizationToken = App.Type == EAppType.Game ? "#App_PlayApp" : "#App_LaunchApp";
            PlayButtonAction = new RelayCommand(InvalidAction);
            PlayButtonEnabled = false;
            UpdateButtonVisible = true;
            UpdateButtonLocalizationToken = "#App_PauseAppUpdate";
            UpdateButtonAction = new RelayCommand(PauseUpdate);
        }
        else if (isAppPlayable && state.HasFlag(EAppState.FullyInstalled))
        {
            if (App.Type == EAppType.Game)
            {
                PlayButtonLocalizationToken = "#App_PlayApp";
                PlayButtonAction = new RelayCommand(Launch);
            }
            else
            {
                PlayButtonLocalizationToken = "#App_LaunchApp";
                PlayButtonAction = new RelayCommand(Launch);
            }
        } else if (state.HasFlag(EAppState.Uninstalled) || state.HasFlag(EAppState.SharedOnly)) {
            PlayButtonLocalizationToken = "#App_InstallApp";
            PlayButtonAction = new RelayCommand(RequestInstall);
        } else if ((!isAppPlayable) && state.HasFlag(EAppState.FullyInstalled)) {
            PlayButtonLocalizationToken = "#App_UninstallApp";
            PlayButtonAction = new RelayCommand(Uninstall);
        }
        else
        {
            PlayButtonLocalizationToken = "Unknown state: " + state.ToString();
            PlayButtonAction = new RelayCommand(InvalidAction);
            PlayButtonEnabled = false;
        }

        if (!state.HasFlag(EAppState.AppRunning) &&
            !state.HasFlag(EAppState.Terminating) &&
            !state.HasFlag(EAppState.UpdateRunning) &&
            (state.HasFlag(EAppState.UpdateRequired) ||
             state.HasFlag(EAppState.UpdatePaused) ||
             state.HasFlag(EAppState.UpdateQueued)))
        {
            UpdateButtonVisible = true;
            if (state.HasFlag(EAppState.UpdatePaused))
            {
                UpdateButtonLocalizationToken = "#App_ResumeAppUpdate";
            }
        }
    }

#pragma warning disable MVVMTK0034
    [MemberNotNull(nameof(hero))]
    [MemberNotNull(nameof(Hero))]
#pragma warning restore MVVMTK0034
    private void SetLibraryAssets()
    {
        string? localLogoPath = null;
        string? localHeroPath = null;
        if (App is IAppAssetsInterface assetsInterface)
        {
            localLogoPath = assetsInterface.Assets.FirstOrDefault(a => a.Type == ELibraryAssetType.Logo)?.LocalPath;
            localHeroPath = assetsInterface.Assets.FirstOrDefault(a => a.Type == ELibraryAssetType.Hero)?.LocalPath;
        }

        if (string.IsNullOrEmpty(localHeroPath) && string.IsNullOrEmpty(localLogoPath))
            LogoAsText = App.Name;

        if (!string.IsNullOrEmpty(localHeroPath))
        {
            this.Hero = new ImageBrush()
            {
                Source = new Bitmap(localHeroPath)
            };
        } else {
            this.Hero = Brushes.Transparent;
        }

        AppDataCommonSection.LibraryAssetsT? assetAlignment = null;
        if (App is IAppInfoAccessInterface appInfoAccessInterface)
        {
            assetAlignment = appInfoAccessInterface.Common.LibraryAssets;
        }



        if (localLogoPath != null)
        {
            this.Logo = new Bitmap(localLogoPath);

            if (assetAlignment != null) {
                this.LogoHeight = (this.HeroHeight / 100) * assetAlignment.LogoHeightPercentage;
                this.LogoWidth = (this.HeroWidth / 100) * assetAlignment.LogoWidthPercentage;

                if (assetAlignment.LogoPinnedPosition == "CenterCenter") {
                    LogoHorizontalAlignment = HorizontalAlignment.Center;
                    LogoVerticalAlignment = VerticalAlignment.Center;
                } else if (assetAlignment.LogoPinnedPosition == "UpperCenter") {
                    LogoHorizontalAlignment = HorizontalAlignment.Center;
                    LogoVerticalAlignment = VerticalAlignment.Top;
                } else if (assetAlignment.LogoPinnedPosition == "BottomCenter") {
                    LogoHorizontalAlignment = HorizontalAlignment.Center;
                    LogoVerticalAlignment = VerticalAlignment.Bottom;
                } else if (assetAlignment.LogoPinnedPosition == "BottomLeft") {
                    LogoHorizontalAlignment = HorizontalAlignment.Left;
                    LogoVerticalAlignment = VerticalAlignment.Bottom;
                } else {
                    throw new InvalidOperationException("Unhandled logo alignment: " + assetAlignment.LogoPinnedPosition);
                }
            }
        }
        else
        {
            this.Logo = null;
        }
    }

    public void OnHeroBoundsChanged(Rect newBounds)
    {
        SetLibraryAssets();
    }

    private void InvalidAction()
    {
        throw new InvalidOperationException("Nothing to do");
    }

    private void PauseUpdate()
    {
        if (App is IAppInstallInterface installInterface)
        {
            installInterface.PauseInstall();
        }
    }

    private void Update()
    {
        if (App is IAppInstallInterface installInterface)
        {
            installInterface.StartUpdate();
        }
    }

    private void KillApp()
    {
        if (App is IAppLaunchInterface launchInterface)
        {
            launchInterface.Kill();
        }
    }

    private void Uninstall() {
        //this.app.Uninstall();
    }

    private void RequestInstall() {
        if (App is IAppInstallInterface installInterface)
        {
            SelectInstallDirectoryDialog dialog = new();
            dialog.DataContext = new SelectInstallDirectoryDialogViewModel(
                AvaloniaApp.Container.Get<AppManagerHelper>(),
                AvaloniaApp.Container.Get<TranslationManager>(),
                dialog,
                installInterface);

            AvaloniaApp.Current?.TryShowDialog(dialog);
        }


    }


    private async void Launch()
    {
        try
        {
            if (App is IAppLaunchInterface launchInterface)
            {
                if (launchInterface.DefaultOption != null)
                {
                    launchInterface.Launch(launchInterface.DefaultOption, ELaunchSource._2ftLibraryDetails);
                } else {
                    AvaloniaApp.Current?.RunOnUIThread(DispatcherPriority.Normal, () =>
                    {
                        var dialog = new PickLaunchOptionDialog();
                        var vm = new PickLaunchOptionDialogViewModel(dialog, App);
                        dialog.DataContext = vm;
                        vm.OptionSelected += (_, selectedOption) =>
                        {
                            launchInterface.Launch(selectedOption, ELaunchSource._2ftLibraryDetails);
                        };

                        dialog.Show();
                    });
                }
            }

        }
        catch (Exception e)
        {
            Console.WriteLine("Error launching app " + e);
            try
            {
                MessageBox.Error("Error launching app " + Name, "Exception message: " + e.Message, e.ToString());
            }
            catch (Exception)
            {

            }
        }
    }
}
