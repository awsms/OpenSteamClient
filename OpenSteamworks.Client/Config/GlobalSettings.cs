using OpenSteamworks.Data.Enums;
using OpenSteamworks.Client.Config.Attributes;
using OpenSteamworks.Client.Managers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenSteamworks.Client.Config;

public class GlobalSettings: IConfigFile<GlobalSettings> {
	static JsonTypeInfo<GlobalSettings> IConfigFile<GlobalSettings>.JsonTypeInfo => ConfigJsonContext.Default.GlobalSettings;
    static string IConfigFile<GlobalSettings>.ConfigName => "GlobalSettings_001";
    static bool IConfigFile<GlobalSettings>.PerUser => false;
    static bool IConfigFile<GlobalSettings>.AlwaysSave => false;

    public static GlobalSettings LoadForStartup(InstallManager installManager)
    {
        var path = Path.Combine(installManager.ConfigDir, "GlobalSettings_001.json");
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            return new();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.GlobalSettings) ?? new();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Console.Error.WriteLine($"Failed to load startup rendering settings: {exception.Message}");
            return new();
        }
    }

    [ConfigName("Enable client hardware acceleration", "#GlobalSettings_ClientHardwareAcceleration")]
    [ConfigDescription("Renders the OpenSteamClient interface using the GPU. Requires a restart.", "#GlobalSettings_ClientHardwareAccelerationDesc")]
    [ConfigCategory("OpenSteamClient", "#GlobalSettings_Category_OpenSteamClient")]
    public bool ClientHardwareAcceleration { get; set; } = true;
    
    [ConfigName("Enable Webhelper", "#GlobalSettings_EnableWebHelper")]
    [ConfigDescription("Enables/disables Webhelper. Required for some games and for browsing the store and community pages in-client. 100% functionality is not guaranteed.", "#GlobalSettings_EnableWebHelperDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool EnableWebHelper { get; set; } = true;

    [ConfigName("Webhelper Always on", "#GlobalSettings_AlwaysWebhelper")]
    [ConfigDescription("Enables/disables Webhelper launching at client startup, and always running in the background.", "#GlobalSettings_AlwaysWebhelperDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool WebhelperAlwaysOn { get; set; } = true;

    [ConfigName("Enable Webhelper GPU Acceleration", "#GlobalSettings_WebhelperGPUAcceleration")]
    [ConfigDescription("Enables/disables GPU hardware rendering in Webhelper.", "#GlobalSettings_WebhelperGPUAccelerationDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool WebhelperGPUAcceleration { get; set; } = true;

    [ConfigName("Enable Webhelper smooth scrolling", "#GlobalSettings_WebhelperSmoothScrolling")]
    [ConfigDescription("Enables/disables smooth scrolling in Webhelper.", "#GlobalSettings_WebhelperSmoothScrollingDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool WebhelperSmoothScrolling { get; set; } = true;

    [ConfigName("Enable Webhelper GPU video decoding", "#GlobalSettings_WebhelperGPUVideoDecode")]
    [ConfigDescription("Enables/disables GPU video decoding in Webhelper.", "#GlobalSettings_WebhelperGPUVideoDecodeDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool WebhelperGPUVideoDecode { get; set; } = true;

    [ConfigName("Enable HighDPI support for Webhelper", "#GlobalSettings_WebhelperHighDPI")]
    [ConfigDescription("Enables/disables HighDPI support in Webhelper.", "#GlobalSettings_WebhelperHighDPIDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool WebhelperHighDPI { get; set; } = true;

    [ConfigName("Webhelper proxy URL", "#GlobalSettings_WebhelperProxy")]
    [ConfigDescription("Sets a proxy for Webhelper to use. Does not affect connections outside Webhelper. Leave empty for no proxy.", "#GlobalSettings_WebhelperProxyDesc")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public string WebhelperProxy { get; set; } = "";

    [ConfigName("Webhelper ignore proxy for localhost", "#GlobalSettings_WebhelperIgnoreProxyForLocalhost")]
    [ConfigDescription("If the proxy should be ignored for localhost connections.", "#GlobalSettings_WebhelperIgnoreProxyForLocalhost")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    public bool WebhelperIgnoreProxyForLocalhost { get; set; } = true;
    
    [ConfigAdvanced]
    [ConfigDescription("We don't actually know what this does. Doesn't seem to be a vanilla CEF thing.", "")]
    public int WebhelperComposerMode { get; set; } = 0;

    [ConfigName("Webhelper ignore GPU blocklist", "#GlobalSettings_WebhelperIgnoreGPUBlocklist")]
    [ConfigDescription("If CEFs internal GPU blocklist should be disabled. ", "#GlobalSettings_WebhelperIgnoreGPUBlocklist")]
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    [ConfigAdvanced]
    public bool WebhelperIgnoreGPUBlocklist { get; set; } = true;
    
    [ConfigCategory("Webhelper", "#GlobalSettings_Category_Webhelper")]
    [ConfigAdvanced]
    public bool WebhelperAllowWorkarounds { get; set; } = true;
}
