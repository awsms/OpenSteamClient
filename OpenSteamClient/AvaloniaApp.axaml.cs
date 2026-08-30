using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using OpenSteamClient.Translation;
using OpenSteamClient.ViewModels;
using OpenSteamClient.Views;
using OpenSteamworks.Client.Utils;
using OpenSteamworks;
using OpenSteamworks.Client;
using OpenSteamworks.Client.Managers;
using System.Threading.Tasks;
using OpenSteamworks.Client.Config;
using OpenSteamworks.Client.CommonEventArgs;
using System;
using Avalonia.Controls;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Input;
using OpenSteamClient.DI;
using OpenSteamworks.Utils;
using Avalonia.Styling;
using OpenSteamworks.Client.Apps.Library;
using OpenSteamworks.Generated;
using System.Text;
using OpenSteamworks.Client.Login;
using OpenSteamworks.Data.Enums;
using Avalonia.Threading;
using OpenSteamworks.Client.Friends;
using OpenSteamClient.UIImpl;
using OpenSteamworks.Client.Startup;
using AvaloniaCommon;
using System.Diagnostics;
using OpenSteamClient.DI.Lifetime;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Client.DI;
using OpenSteamworks.Helpers;

namespace OpenSteamClient;

public class AvaloniaApp : Application
{
    public static readonly IContainer Container;
    public new static AvaloniaApp? Current;
    public static Theme? Theme;
    public new IClassicDesktopStyleApplicationLifetime ApplicationLifetime => (base.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!;
    public static bool DebugEnabled = false;
    public Window? MainWindow => ApplicationLifetime.MainWindow;
    static AvaloniaApp()
    {
        var installManager = new InstallManager();
		CApi.Initialize(installManager);

		var loggerFactory = new LoggerFactory(installManager);
		Container = new OpenSteamworks.Client.DI.Container(installManager, loggerFactory);
		Container.ConstructAndRegister<LifetimeManager>();
	}

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void InvokeOnUIThread(Action callback)
    {
        if (!Container.Get<LifetimeManager>().IsShuttingDown)
        {
            Avalonia.Threading.Dispatcher.UIThread.Invoke(callback);
        }
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        Theme = new Theme(this);

		Progress<OperationProgress> progress = new();
		var progVm = new ProgressWindowViewModel(progress, "Bootstrapper progress");
        progVm.OnClosed = (WindowClosingEventArgs e) =>
        {
            if (e.IsProgrammatic)
                return;

            //TODO: OSC really does not support graceful shutdown, so do this (evil!) instead
            Environment.Exit(0);
        };

        ForceProgressWindow(progVm);

        Container.ConstructAndRegister<ConfigManager>();
        var bootstrapper = Container.ConstructAndRegister<Bootstrapper>();

        bootstrapper.SetProgressObject(progress);
        await bootstrapper.RunBootstrap((title, msg) => RunOnUIThread(DispatcherPriority.Normal, () => MessageBox.Show(title, msg)));

        progVm.Title = "Login progress";

        Container.RegisterInstance(new Client(Container));
        Container.ConstructAndRegister<TranslationManager>();
        Container.RegisterInstance(this);
        await Container.Get<LifetimeManager>().RunClientStartup(progress);

        Container.Get<TranslationManager>().SetLanguage(ELanguage.English, false);

        Container.ConstructAndRegister<FriendsUI>();

        // This will stay for the lifetime of the application.
        Container.Get<LoginManager>().LoggedOn += (object sender, LoggedOnEventArgs e) =>
        {
            InvokeOnUIThread(() =>
            {
                (this.DataContext as AvaloniaAppViewModel)!.IsLoggedIn = true;
                ForceMainWindow();
            });
        };

        Container.Get<LoginManager>().LogOnFailed += (object sender, LogOnFailedEventArgs e) =>
        {
            InvokeOnUIThread(() =>
            {
                (this.DataContext as AvaloniaAppViewModel)!.IsLoggedIn = false;
                MessageBox.Show("Failed to log on", "Failed with result code: " + e.Error.ToString());
                ForceAccountPickerWindow();
            });
        };

        Container.Get<LoginManager>().LoggedOff += (object sender, LoggedOffEventArgs e) =>
        {
            InvokeOnUIThread(() =>
            {
                (this.DataContext as AvaloniaAppViewModel)!.IsLoggedIn = false;
                if (e.Error != EResult.OK)
                {
                    // What can cause a sudden log off?
                    MessageBox.Show("Session terminated", "You were forcibly logged off with an error code: " + e.Error.ToString());
                }
                ForceAccountPickerWindow();
            });
        };

        // This is kept for the lifetime of the application, which is fine
        Container.Get<LoginManager>().SetProgress(progress);
        Container.Get<LoginManager>().SetExceptionHandler(e => {
            if (Debugger.IsAttached)
            {
                throw e;
            }

            Program.FatalException(e);
        });

        Container.Get<LoginManager>().LogonStarted += (object sender, EventArgs e) =>
        {
            InvokeOnUIThread(() =>
            {
                this.ForceProgressWindow(new ProgressWindowViewModel(progress, "Login progress"));
            });
        };

        Container.Get<LoginManager>().LoggingOff += (object? sender, EventArgs e) =>
        {
            InvokeOnUIThread(() =>
            {
                this.ForceProgressWindow(new ProgressWindowViewModel(progress, "Logout progress"));
            });
        };

        var loginManager = Container.Get<LoginManager>();
        if (Container.Get<ISteamClient>().IsCrossProcess && loginManager.IsLoggedOn())
        {
            var clientUser = Container.Get<IClientUser>();
            StringBuilder username = new StringBuilder(256);
            clientUser.GetAccountName(username, username.Capacity);

            await Container.Get<AppsManager>().InitApps(Container.Get<UserHelper>().GetSubscribedApps());
            await Container.Get<LoginManager>().OnLoggedOn(new LoggedOnEventArgs(new LoginUser() { AccountName = username.ToString(), SteamID = clientUser.GetSteamID() }));
            ForceMainWindow();
        }
        else
        {
            if (!Container.Get<LoginManager>().TryAutologin())
            {
                ForceAccountPickerWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();

        var icons = TrayIcon.GetIcons(this);
        UtilityFunctions.AssertNotNull(icons);
        foreach (var icon in icons)
        {
            Container.Get<TranslationManager>().TranslateTrayIcon(icon);
        }
    }

    /// <summary>
    /// Closes the current MainWindow (if exists) and replaces it with the account picker
    /// </summary>
    public void ForceAccountPickerWindow()
    {
        ForceWindow(new AccountPickerWindow
        {
            DataContext = AvaloniaApp.Container.Construct<AccountPickerWindowViewModel>()
        });
    }

    /// <summary>
    /// Closes the current MainWindow (if exists) and replaces it with a new MainWindow
    /// </summary>
    public void ForceMainWindow()
    {
        var window = ForceWindow(new MainWindow());
        window.DataContext = AvaloniaApp.Container.Construct<MainWindowViewModel>((Action)OpenSettingsWindow, window);
    }

    private SettingsWindow? CurrentSettingsWindow;
    public void OpenSettingsWindow()
    {
        if (CurrentSettingsWindow != null)
        {
            if (CurrentSettingsWindow.PlatformImpl != null)
            {
                // Not closed but maybe hidden, maybe shown in background
                CurrentSettingsWindow.Show();
                CurrentSettingsWindow.Activate();
                return;
            }
        }

        CurrentSettingsWindow = new();
        CurrentSettingsWindow.DataContext = AvaloniaApp.Container.Construct<SettingsWindowViewModel>(CurrentSettingsWindow);

        CurrentSettingsWindow.Show();
        CurrentSettingsWindow.Closed += (object? sender, EventArgs e) =>
        {
            CurrentSettingsWindow = null;
        };
    }

    //private InterfaceList? CurrentInterfaceListWindow;
    // public void OpenInterfaceList()
    // {
    //     if (CurrentInterfaceListWindow != null)
    //     {
    //         if (CurrentInterfaceListWindow.PlatformImpl != null)
    //         {
    //             // Not closed but maybe hidden, maybe shown in background
    //             CurrentInterfaceListWindow.Show();
    //             CurrentInterfaceListWindow.Activate();
    //             return;
    //         }
    //     }

    //     CurrentInterfaceListWindow = new();

    //     CurrentInterfaceListWindow.Show();
    //     CurrentInterfaceListWindow.Closed += (object? sender, EventArgs e) =>
    //     {
    //         CurrentInterfaceListWindow = null;
    //     };
    // }

    public void OpenMainWindow()
    {
        if (ApplicationLifetime.MainWindow != null)
        {
            if (ApplicationLifetime.MainWindow.IsActive)
            {
                // Not closed but maybe hidden, maybe shown in background
                ApplicationLifetime.MainWindow.Show();
                ApplicationLifetime.MainWindow.Activate();
                return;
            }
        }

        ForceMainWindow();
    }

    public void ActivateMainWindow() {
        ApplicationLifetime.MainWindow?.Show();
        ApplicationLifetime.MainWindow?.Activate();
    }

    /// <summary>
    /// Tries to show the window as a dialog
    /// </summary>
    /// <param name="dialog"></param>
    public void TryShowDialog(Window dialog) {
        dialog.Show();
    }

    /// <summary>
    /// Tries to show the window as a dialog. If the window is already open, it focus on it.
    /// </summary>
    /// <param name="dialog"></param>
    public void TryShowDialogSingle<T>(Func<T> dialogFactory) where T: Window {
        RunOnUIThread(DispatcherPriority.Send, () =>
        {
            var existingDialog = TryGetDialogSingle<T>();
            if (existingDialog != null) {
                existingDialog.Show();
                existingDialog.Activate();
                return;
            }

            dialogFactory().Show();
        });
    }

    /// <summary>
    /// Runs the given action on the UI thread.
    /// If we're already on the UI thread, executes the function inline.
    /// Blocks until the function has been executed.
    /// </summary>
    /// <param name="func"></param>
    public void RunOnUIThread(DispatcherPriority priority, Action func) {
        if (Dispatcher.UIThread.CheckAccess()) {
            func();
            return;
        }

        Dispatcher.UIThread.Invoke(func, priority);
    }

    public T? TryGetDialogSingle<T>() where T: Window {
        var existingDialogs = ApplicationLifetime.Windows.Where(w => w.GetType() == typeof(T));
        if (existingDialogs.Any()) {
            return (T)existingDialogs.First();
        }

        return null;
    }

    /// <summary>
    /// Closes the current MainWindow (if exists) and replaces it with the login screen
    /// </summary>
    public void ForceLoginWindow(LoginUser? user)
    {
        LoginWindowViewModel vm = AvaloniaApp.Container.Construct<LoginWindowViewModel>();
		if (user != null)
		{
			vm.SetUser(user);
		}

		var window = new LoginWindow();
        //TODO: hacky
        vm.ShowSecondFactorDialog = window.ShowSecondFactorDialog;
        window.DataContext = vm;

        ForceWindow(window);
    }

    /// <summary>
    /// Closes the current MainWindow (if exists) and replaces it with a new Progress Window, with the user specified ProgressWindowViewModel
    /// </summary>
    public void ForceProgressWindow(ProgressWindowViewModel progVm)
    {
        ForceWindow(new ProgressWindow(progVm));
    }

    /// <summary>
    /// Closes the current MainWindow (if exists) and replaces it with a user specified window
    /// </summary>
    public T ForceWindow<T>(T window) where T : Window
    {
        if (!Container.Get<LifetimeManager>().IsShuttingDown)
        {
            if (ApplicationLifetime.MainWindow != window)
            {
                ApplicationLifetime.MainWindow?.Close();
                ApplicationLifetime.MainWindow = window;
            }

            ApplicationLifetime.MainWindow.Show();
        }

        return window;
    }

    public AvaloniaApp()
    {
        Current = this;
        Name = "OpenSteamClient";
        DataContext = new AvaloniaAppViewModel();
    }

    /// <summary>
    /// Async exit function. Will hang in certain cases for some unknown reason.
    /// </summary>
    public async Task Exit(int exitCode = 0)
    {
		Progress<OperationProgress> operation = new();
		var progVm = new ProgressWindowViewModel(operation);
        ForceProgressWindow(progVm);
        await Container.Get<LifetimeManager>().RunClientShutdown(operation);
        Console.WriteLine("Shutting down Avalonia");
        // At this point, all the other shutdown tasks should have finished so let's just kill
        Process.GetCurrentProcess().Kill();

        // Dispatcher.UIThread.Invoke(() => ApplicationLifetime.Shutdown(exitCode));
    }

    /// <summary>
    /// A synchronous exit function. Simply calls Task.Run.
    /// </summary>
    public void ExitEventually(int exitCode = 0)
    {
        Dispatcher.UIThread.InvokeAsync(async () => await this.Exit(exitCode));
    }
}
