using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AvaloniaCommon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Primitives;
using OpenSteamworks;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Data.Enums;
using OpenSteamworks.Helpers;

namespace OpenSteamClient.ViewModels;

// Fuck it. This just entirely bypasses the whole new IApp system. Don't care. Lost cause.
public partial class AppSettingsWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string launchOptions = string.Empty;

    [ObservableProperty]
    private string launchOptionsStatus = string.Empty;

    public bool SupportsLaunchOptions =>
        _configInterface?.SupportedKeys.Contains(IAppConfigInterface.ConfigKey.LAUNCH_COMMAND_LINE) == true;

    public ObservableCollection<string> CompatTools { get; } = new();

    public string SelectedCompatTool
    {
        get
        {
            if (!_compatHelper.BIsCompatToolUserOverride(app.ID.AppID))
                return NO_TOOL;

            return _compatHelper.GetAppCompatTool(app.ID.AppID) ?? NO_TOOL;
        }

        set
        {
            if (value == NO_TOOL)
            {
                _compatHelper.RemoveAppCompatTool(app.ID.AppID);
                return;
            }

            _compatHelper.SetAppCompatTool(app.ID.AppID, value);
            OnPropertyChanged(nameof(SelectedCompatTool));
        }
    }

    public string EffectiveCompatTool
    {
        get
        {
            if (!_compatHelper.AppCompatEnabled(app.ID.AppID))
                return "Compat is disabled for this app.";

            StringBuilder builder = new();
            var appCompatTool = _compatHelper.GetAppCompatTool(app.ID.AppID);
            if (appCompatTool == null)
                return $"Compat enabled but no compat tool is selected. (This shouldn't happen, file a bug, gameid {app.ID})";

            builder.Append($"Effective compat tool is {_compatHelper.GetCompatToolDisplayName(appCompatTool)} selected by ");
            builder.Append(_compatHelper.BIsCompatToolUserOverride(app.ID.AppID) ? "user override" : "valve testing");
            return builder.ToString();
        }
    }

    public bool IsInstalled => _installInterface?.IsInstalled ?? false;

    public void OnUninstall()
    {
        if (_installInterface == null)
            return;

        var err = _installInterface.Uninstall();
        if (err != EAppError.NoError)
            MessageBox.Error("Failed to uninstall", $"Failed to uninstall {Name}: {err}");
    }

    public record Beta(string Name, string Display);
    public ObservableCollection<Beta> Betas { get; } = new();

    public Beta SelectedBeta
    {
        get
        {
            //TODO: AppManagerHelper should return empty string in case of "public"
            var beta = _steamClient.AppManagerHelper.GetActiveBeta(app.ID.AppID);
            if (string.IsNullOrEmpty(beta) || beta == "public")
                return NO_BETA;

            return Betas.First(b => b.Name == beta);
        }

        set
        {
            _steamClient.IClientAppManager.SetActiveBeta(app.ID.AppID, value.Name);
            OnPropertyChanged(nameof(SelectedBeta));
        }
    }

    [ObservableProperty]
    private string betaPassword = string.Empty;

    public void OnCheckBetaPassword()
    {
        _steamClient.IClientAppManager.CheckBetaPassword(app.ID.AppID, BetaPassword);
    }

    private readonly IApp app;
    private readonly ISteamClient _steamClient;
    private readonly IAppConfigInterface? _configInterface;
    private readonly IAppInstallInterface? _installInterface;
    private readonly CompatHelper _compatHelper;

    private const string NO_TOOL = "< no compat tool override >";
    private readonly Beta NO_BETA = new(Name: "public", Display: "< no beta selected >");

    public AppSettingsWindowViewModel(ISteamClient steamClient, IApp app)
    {
        this._steamClient = steamClient;
        this._compatHelper = steamClient.CompatHelper;
        this.app = app;
        this.app.PropertyChanged += App_PropertyChanged;
        this._configInterface = app as IAppConfigInterface;
        this._installInterface = app as IAppInstallInterface;

        this.Name = this.app.Name;

        if (SupportsLaunchOptions &&
            _configInterface!.TryGetConfigValue(IAppConfigInterface.ConfigKey.LAUNCH_COMMAND_LINE, out var launchOptions) &&
            launchOptions is string launchOptionsString)
        {
            LaunchOptions = launchOptionsString;
        }

        CompatTools.Add(NO_TOOL);
        Betas.Add(NO_BETA);

        if (app.ID.IsSteamApp())
        {
            foreach (var tool in _compatHelper.GetCompatToolsForApp(app.ID.AppID))
            {
                CompatTools.Add(tool);
            }

            var numBetas = _steamClient.IClientAppManager.GetNumBetas(app.ID.AppID, out _, out _);
            StringBuilder betaName = new(64);
            StringBuilder betaDesc = new(256);
            StringBuilder display = new(64 + 256 + 3);

            for (int i = 0; i < numBetas; i++)
            {
                betaName.Length = 0;
                betaDesc.Length = 0;
                display.Length = 0;
                if (!_steamClient.IClientAppManager.GetBetaInfo(app.ID.AppID, i, out var flags, out _, betaName, betaName.Capacity, betaDesc, betaDesc.Capacity))
                    continue;

                // Exclude default "public" branch
                if (flags.HasFlag(EBetaBranchFlags.Default))
                    continue;

                display.Append(betaName);
                if (betaDesc.Length != 0)
                {
                    display.Append(" - ");
                    display.Append(betaDesc);
                }

                Betas.Add(new(Name: betaName.ToString(), Display: display.ToString()));
            }
        }
        else
        {
            foreach (var tool in _compatHelper.GetCompatTools())
            {
                CompatTools.Add(tool);
            }
        }
    }

    [RelayCommand]
    private void SaveLaunchOptions()
    {
        if (!SupportsLaunchOptions || _configInterface is null)
        {
            LaunchOptionsStatus = "Launch options are not supported for this item.";
            return;
        }

        LaunchOptionsStatus = _configInterface.SetConfigValue(
            IAppConfigInterface.ConfigKey.LAUNCH_COMMAND_LINE,
            LaunchOptions)
            ? "Launch options saved."
            : "Failed to save launch options.";
    }

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAppInstallInterface.IsInstalled))
        {
            OnPropertyChanged(nameof(IsInstalled));
        }
    }
}
