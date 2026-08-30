using System.Diagnostics;
using System.Runtime.Versioning;
using OpenSteamworks.Client.Managers;
using OpenSteamClient.DI;
using OpenSteamworks.Client.Config;
using System.Text;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Utils;
using OpenSteamClient.DI.Lifetime;
using OpenSteamClient.Logging;

namespace OpenSteamworks.Client.Startup;

public class SteamHTML : IClientLifetime
{
    public bool ShouldStop = false;
    public object CurrentHTMLHostLock = new();
    public Process? CurrentHTMLHost;
    public Thread? WatcherThread;
    private readonly ISteamClient steamClient;
    private readonly InstallManager installManager;
    private readonly GlobalSettings globalSettings;
    private readonly ILogger logger;
    private static RefCount initCount = new();

    public SteamHTML(ISteamClient steamClient, InstallManager installManager, ILoggerFactory loggerFactory, GlobalSettings globalSettings)
    {
        this.logger = loggerFactory.CreateLogger("SteamHTMLManager");
        this.steamClient = steamClient;
        this.installManager = installManager;
        this.globalSettings = globalSettings;
    }

    [SupportedOSPlatform("linux")]
    private void StartHTMLHost()
    {
        lock (CurrentHTMLHostLock)
        {
            logger.Info("Creating steamwebhelper process");

            var htmlHost = new Process();
            htmlHost.StartInfo.WorkingDirectory = Path.Combine(installManager.InstallDir, "ubuntu12_64");
            htmlHost.StartInfo.FileName = Path.Combine(installManager.InstallDir, "ubuntu12_64", "steamwebhelper");
            htmlHost.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = $".:{Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")}";
            htmlHost.StartInfo.ArgumentList.Add("--disable-seccomp-filter-sandbox");
            htmlHost.StartInfo.ArgumentList.Add("-lang=fi_FI");
            htmlHost.StartInfo.ArgumentList.Add($"-cachedir={Path.Combine(installManager.CacheDir, "htmlcache")}");
            // This could technically be improved by reading from the steam.pid file, but no need since this code always assumes we're the master
            htmlHost.StartInfo.ArgumentList.Add($"-steampid={Environment.ProcessId}");

            htmlHost.StartInfo.ArgumentList.Add($"-buildid={Generated.VersionInfo.STEAM_MANIFEST_VERSION}");

            // Don't know our SteamID at this point.
            htmlHost.StartInfo.ArgumentList.Add("-steamid=0");
            htmlHost.StartInfo.ArgumentList.Add($"-logdir={installManager.LogsDir}");
            htmlHost.StartInfo.ArgumentList.Add($"-uimode={(int)steamClient.IClientUtils.GetCurrentUIMode()}");

            // We don't track this.
            htmlHost.StartInfo.ArgumentList.Add($"-startcount=0");

            htmlHost.StartInfo.ArgumentList.Add($"-steamuniverse={steamClient.IClientUtils.GetConnectedUniverse()}");
            htmlHost.StartInfo.ArgumentList.Add($"-realm={steamClient.IClientUtils.GetSteamRealm()}");

            // Doesn't exist, but we pass it anyway.
            htmlHost.StartInfo.ArgumentList.Add($"-clientui={Path.Combine(installManager.InstallDir, "clientui")}");

            string steampath;
            if (OperatingSystem.IsLinux())
            {
                steampath = Directory.ResolveLinkTarget("/proc/self/exe", false)!.FullName;
            }
            else
            {
                steampath = Environment.ProcessPath!;
            }

            htmlHost.StartInfo.ArgumentList.Add($"-steampath={steampath}");

            // No idea what this means or does.
            htmlHost.StartInfo.ArgumentList.Add($"-launcher=0");

            // This should only be passed if we're in debug mode, but that info isn't really passed through to us in any way.
            htmlHost.StartInfo.ArgumentList.Add($"-dev");

            htmlHost.StartInfo.ArgumentList.Add($"-no-restart-on-ui-mode-change");
            htmlHost.StartInfo.ArgumentList.Add($"--enable-media-stream");
            htmlHost.StartInfo.ArgumentList.Add($"--enable-smooth-scrolling");
            htmlHost.StartInfo.ArgumentList.Add($"--password-store=basic");
            htmlHost.StartInfo.ArgumentList.Add($"--log-file={Path.Combine(installManager.LogsDir, "cef_log.txt")}");
            htmlHost.StartInfo.ArgumentList.Add($"--disable-quick-menu");
            htmlHost.StartInfo.ArgumentList.Add($"--disable-features=SameSiteByDefaultCookies");
            htmlHost.StartInfo.ArgumentList.Add($"--enable-blink-features=ResizeObserver,Worklet,AudioWorklet");
            htmlHost.StartInfo.ArgumentList.Add($"--disable-blink-features=Badging");

            if (!globalSettings.WebhelperGPUAcceleration)
            {
                htmlHost.StartInfo.ArgumentList.Add("--disable-gpu");
                htmlHost.StartInfo.ArgumentList.Add("--disable-gpu-compositing");
            }

            if (!globalSettings.WebhelperGPUAcceleration || !globalSettings.WebhelperGPUVideoDecode)
            {
                htmlHost.StartInfo.ArgumentList.Add("--disable-accelerated-video-decode");
            }

            // // Necessary for hooking some funcs (to get it to connect to master steam process)
            // CurrentHTMLHost.StartInfo.Environment.Add("OPENSTEAM_EXE_PATH", steampath);
            // CurrentHTMLHost.StartInfo.Environment.Add("OPENSTEAM_PID", Environment.ProcessId.ToString());

            // if (OperatingSystem.IsLinux())
            // {
            //     CurrentHTMLHost.StartInfo.Environment.Add("LD_LIBRARY_PATH", $".:{Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")}");
            //     CurrentHTMLHost.StartInfo.Environment.Add("LD_PRELOAD", $"/tmp/libhtmlhost_fakepid.so:/tmp/libbootstrappershim32.so:{Environment.GetEnvironmentVariable("LD_PRELOAD")}");

            //     // We don't use steam-runtime-heavy
            //     CurrentHTMLHost.StartInfo.Environment.Add("STEAM_RUNTIME", "0");
            // }

            // [cachedir, steampath, universe, realm, language, uimode, enablegpuacceleration, enablesmoothscrolling, enablegpuvideodecode, enablehighdpi, proxyserver, bypassproxyforlocalhost, composermode, ignoregpublocklist, allowworkarounds]
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(cacheDir);
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(steampath);
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(((int)this.steamClient.IClientUtils.GetConnectedUniverse()).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(((int)this.steamClient.IClientUtils.GetSteamRealm()).ToString());
            // StringBuilder builder = new(128);
            // this.steamClient.IClientUser.GetLanguage(builder, 128);
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(((int)ELanguageConversion.ELanguageFromAPIName(builder.ToString())).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(((int)this.steamClient.IClientUtils.GetCurrentUIMode()).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperGPUAcceleration).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperSmoothScrolling).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperGPUVideoDecode).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperHighDPI).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(this.globalSettings.WebhelperProxy);
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperIgnoreProxyForLocalhost).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(this.globalSettings.WebhelperComposerMode.ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperIgnoreGPUBlocklist).ToString());
            // CurrentHTMLHost.StartInfo.ArgumentList.Add(Convert.ToInt32(this.globalSettings.WebhelperAllowWorkarounds).ToString());

            // // Corresponds to --v=4 in CEF terms
            // CurrentHTMLHost.StartInfo.ArgumentList.Add("-cef-verbose-logging 4");

            logger.Info("Starting steamwebhelper process");
            htmlHost.Start();
            CurrentHTMLHost = htmlHost;

            if (WatcherThread == null)
            {
                logger.Info("Creating watcher thread");
                WatcherThread = new Thread(() =>
                {
                    do
                    {
                        Process? currentHost;
                        lock (CurrentHTMLHostLock)
                        {
                            currentHost = CurrentHTMLHost;
                        }

                        if (currentHost?.HasExited == true)
                        {
                            logger.Error($"steamwebhelper crashed (exit code {currentHost.ExitCode})! Restarting in 1s.");
                            Thread.Sleep(1000);
                            StartHTMLHost();
                        }
                        Thread.Sleep(50);
                    } while (!ShouldStop);
                    lock (CurrentHTMLHostLock)
                    {
                        if (CurrentHTMLHost is { HasExited: false })
                            CurrentHTMLHost.Kill();
                    }
                    WatcherThread = null;
                });

                logger.Info("Starting watcher thread");
                WatcherThread.Start();
            }
        }
    }

    private void StopThread()
    {
        ShouldStop = true;
        while (WatcherThread != null) { Thread.Sleep(1); }
    }

    public void Stop()
    {
        if (CurrentHTMLHost == null || CurrentHTMLHost.HasExited)
        {
            return;
        }

        using var d = initCount.Decrement(out bool deconstruct);
        if (deconstruct)
        {
            logger.Info("Freeing IClientHTMLSurface, no surfaces left");
            this.steamClient.IClientHTMLSurface.Shutdown();

            logger.Info("Killing HTMLHost");
            StopThread();
            CurrentHTMLHost.Kill(true);

            logger.Info("Killing remaining steamwebhelper processes");
            foreach (var process in Process.GetProcessesByName("steamwebhelper"))
            {
                process.Kill();
            }
        }
    }

    public bool CanRun()
    {
        if (!globalSettings.EnableWebHelper)
        {
            logger.Info("Not running SteamHTML due to user preference");
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            //TODO: windows
            logger.Warning("Not running SteamHTML due to unsupported OS");
            return false;
        }

        return true;
    }

    public async Task Start()
    {
        if (!CanRun())
        {
            throw new InvalidOperationException("SteamHTML cannot run on this installation.");
        }

        using var d = initCount.Increment(out bool construct);
        if (construct)
        {
            logger.Info("Initializing SteamHTML");

            if (CurrentHTMLHost != null && !CurrentHTMLHost.HasExited)
            {
                logger.Info("Not running SteamHTML due to it already running");
            }
            else if (steamClient.IsCrossProcess)
            {
                //TODO: check for existing steamwebhelper here
                logger.Info("Not rerunning SteamHTML due to existing client connection");
            }
            else
            {
                if (OperatingSystem.IsLinux()) {
                    this.StartHTMLHost();
                } else {
                    logger.Info("Not running SteamHTML due to unsupported OS");
                    return;
                }

                logger.Info("Waiting a bit for init");
                await Task.Delay(1500);
            }

            logger.Info("Initializing IClientHTMLSurface");
            await Task.Run(() =>
            {
                while (!this.steamClient.IClientHTMLSurface.Init())
                {
                    logger.Warning("Init failed. Retrying");
                    Thread.Sleep(1000);
                }
            });
        }
    }

    public async Task RunShutdown(IProgress<OperationProgress> operation)
    {
        ShouldStop = true;
        await Task.CompletedTask;
    }

    public async Task RunStartup(IProgress<OperationProgress> operation)
    {
        if (CanRun() && globalSettings.WebhelperAlwaysOn)
        {
            // Start in background
            _ = Task.Run(Start);
        }

        await Task.CompletedTask;
    }
}
