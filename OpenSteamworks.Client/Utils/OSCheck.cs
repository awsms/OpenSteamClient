namespace OpenSteamworks.Client.Utils;

public static class OSCheck {
    public static bool IsWindows11() {
        if (!OperatingSystem.IsWindows()) {
            return false;
        }

        return Environment.OSVersion.Version.Build >= 22000;
    }

    public static bool IsArchLinux() {
        if (!OperatingSystem.IsLinux()) {
            return false;
        }

        try
        {
            var osReleasePath = File.Exists("/etc/os-release")
                ? "/etc/os-release"
                : "/usr/lib/os-release";

            foreach (var line in File.ReadLines(osReleasePath))
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
                if (key == "ID" && value.Equals("arch", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (key == "ID_LIKE" && value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("arch", StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
}
