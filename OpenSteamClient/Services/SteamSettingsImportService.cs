using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenSteamworks;
using OpenSteamworks.Client.Managers;
using OpenSteamworks.Data.Enums;

namespace OpenSteamClient.Services;

public sealed class SteamSettingsImportService
{
    private const string LaunchOptionsRoot = "Software\\Valve\\Steam\\Apps";

    private readonly ISteamClient _client;
    private readonly InstallManager _installManager;

    public SteamSettingsImportService(ISteamClient client, InstallManager installManager)
    {
        _client = client;
        _installManager = installManager;
    }

    public SteamSettingsSnapshot ReadSteamSettings()
    {
        if (_installManager.ValveSteamInstallDir is null)
            throw new InvalidOperationException("A Valve Steam installation could not be found.");

        var accountId = _client.IClientUser.GetSteamID().AccountID;
        var sourcePath = Path.Combine(
            _installManager.ValveSteamInstallDir,
            "userdata",
            accountId.ToString(),
            "config",
            "localconfig.vdf");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Steam's localconfig.vdf was not found for the logged-in account.", sourcePath);

        return ReadSteamSettingsFile(sourcePath);
    }

    internal static SteamSettingsSnapshot ReadSteamSettingsFile(string sourcePath)
    {
        VdfNode root;
        try
        {
            root = VdfParser.Parse(File.ReadAllText(sourcePath));
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Steam's localconfig.vdf could not be parsed.", exception);
        }

        var launchOptions = new Dictionary<uint, string>();
        var steamApps = GetChildPath(root, "Software", "Valve", "Steam", "apps");
        if (steamApps is not null)
        {
            foreach (var app in steamApps.Children)
            {
                if (!uint.TryParse(app.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
                    app.Value.GetChild("LaunchOptions") is not { Value: { } launchOption })
                {
                    continue;
                }

                launchOptions[appId] = launchOption;
            }
        }

        var overlaySettings = new Dictionary<uint, int>();
        var registryApps = root.GetChild("apps");
        if (registryApps is not null)
        {
            foreach (var app in registryApps.Children)
            {
                if (!uint.TryParse(app.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
                    app.Value.GetChild("OverlayAppEnable") is not { Value: { } overlayValue } ||
                    !int.TryParse(overlayValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enabled))
                {
                    continue;
                }

                overlaySettings[appId] = enabled;
            }
        }

        return new SteamSettingsSnapshot(sourcePath, launchOptions, overlaySettings);
    }

    public SteamSettingsImportResult Import(
        SteamSettingsSnapshot snapshot,
        bool importLaunchOptions,
        bool importOverlaySettings,
        bool overwriteExisting)
    {
        var imported = 0;
        var skipped = 0;
        var failed = 0;

        if (importLaunchOptions)
        {
            foreach (var (appId, value) in snapshot.LaunchOptions)
            {
                var key = $"{LaunchOptionsRoot}\\{appId}\\LaunchOptions";
                if (!overwriteExisting && _client.ConfigStoreHelper.IsSet(EConfigStore.UserLocal, key))
                {
                    skipped++;
                    continue;
                }

                if (_client.ConfigStoreHelper.Set(EConfigStore.UserLocal, key, value))
                    imported++;
                else
                    failed++;
            }
        }

        if (importOverlaySettings)
        {
            foreach (var (appId, value) in snapshot.OverlaySettings)
            {
                var key = $"Apps\\{appId}\\OverlayAppEnable";
                if (!overwriteExisting && _client.ConfigStoreHelper.IsSet(EConfigStore.UserLocal, key))
                {
                    skipped++;
                    continue;
                }

                if (_client.IClientUser.SetConfigInt(ERegistrySubTree.Apps, $"{appId}\\OverlayAppEnable", value))
                    imported++;
                else
                    failed++;
            }
        }

        return new SteamSettingsImportResult(imported, skipped, failed);
    }

    private static VdfNode? GetChildPath(VdfNode root, params string[] path)
    {
        VdfNode? current = root;
        foreach (var component in path)
        {
            current = current?.GetChild(component);
            if (current is null)
                return null;
        }

        return current;
    }

    private sealed class VdfNode
    {
        public string? Value { get; init; }
        public List<KeyValuePair<string, VdfNode>> Children { get; } = [];

        public VdfNode? GetChild(string name)
        {
            foreach (var child in Children)
            {
                if (string.Equals(child.Key, name, StringComparison.Ordinal))
                    return child.Value;
            }

            return null;
        }
    }

    private sealed class VdfParser
    {
        private readonly string _text;
        private int _position;

        private VdfParser(string text) => _text = text;

        public static VdfNode Parse(string text)
        {
            var parser = new VdfParser(text);
            var rootName = parser.ReadToken() ?? throw new InvalidDataException("The VDF root key is missing.");
            parser.Expect('{');
            return parser.ReadObject(rootName);
        }

        private VdfNode ReadObject(string name)
        {
            var node = new VdfNode();
            while (true)
            {
                SkipTrivia();
                if (_position >= _text.Length)
                    throw new InvalidDataException($"Unexpected end of VDF object '{name}'.");

                if (_text[_position] == '}')
                {
                    _position++;
                    return node;
                }

                var key = ReadToken() ?? throw new InvalidDataException($"Missing key in VDF object '{name}'.");
                SkipTrivia();
                if (_position < _text.Length && _text[_position] == '{')
                {
                    _position++;
                    node.Children.Add(new(key, ReadObject(key)));
                    continue;
                }

                var value = ReadToken() ?? throw new InvalidDataException($"Missing value for VDF key '{key}'.");
                node.Children.Add(new(key, new VdfNode { Value = value }));
            }
        }

        private string? ReadToken()
        {
            SkipTrivia();
            if (_position >= _text.Length || _text[_position] == '}')
                return null;

            if (_text[_position] != '"')
            {
                var start = _position;
                while (_position < _text.Length &&
                       !char.IsWhiteSpace(_text[_position]) &&
                       _text[_position] is not '{' and not '}')
                {
                    _position++;
                }

                return _text[start.._position];
            }

            _position++;
            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                var character = _text[_position++];
                if (character == '"')
                    return builder.ToString();

                if (character == '\\' && _position < _text.Length)
                {
                    var escaped = _text[_position++];
                    if (escaped is '"' or '\\')
                        builder.Append(escaped);
                    else
                    {
                        builder.Append('\\');
                        builder.Append(escaped);
                    }

                    continue;
                }

                builder.Append(character);
            }

            throw new InvalidDataException("Unterminated quoted VDF string.");
        }

        private void Expect(char expected)
        {
            SkipTrivia();
            if (_position >= _text.Length || _text[_position] != expected)
                throw new InvalidDataException($"Expected '{expected}' in VDF input.");
            _position++;
        }

        private void SkipTrivia()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    _position++;
                    continue;
                }

                if (_position + 1 < _text.Length && _text[_position] == '/' && _text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < _text.Length && _text[_position] != '\n')
                        _position++;
                    continue;
                }

                return;
            }
        }
    }
}

public sealed record SteamSettingsSnapshot(
    string SourcePath,
    IReadOnlyDictionary<uint, string> LaunchOptions,
    IReadOnlyDictionary<uint, int> OverlaySettings);

public sealed record SteamSettingsImportResult(int Imported, int Skipped, int Failed);
