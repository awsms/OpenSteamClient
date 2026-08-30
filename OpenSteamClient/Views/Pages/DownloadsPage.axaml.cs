using System;
using OpenSteamClient.Extensions;
using OpenSteamClient.Controls;

namespace OpenSteamClient.Views;

public partial class DownloadsPage : BasePage
{
    public DownloadsPage() : base()
    {
        InitializeComponent();
        this.TranslatableInit();
    }

    public override void Free()
    {
        (DataContext as IDisposable)?.Dispose();
        base.Free();
    }
}
