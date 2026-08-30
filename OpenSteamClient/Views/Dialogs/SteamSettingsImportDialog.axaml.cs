using Avalonia.Controls;
using OpenSteamClient.Extensions;

namespace OpenSteamClient.Views;

public partial class SteamSettingsImportDialog : Window
{
    public SteamSettingsImportDialog()
    {
        InitializeComponent();
        this.TranslatableInit();
    }
}
