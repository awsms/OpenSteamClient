using System;
using Avalonia.Controls;
using Avalonia.Input;
using OpenSteamClient.Extensions;
using OpenSteamClient.Controls;
using OpenSteamClient.ViewModels.Downloads;

namespace OpenSteamClient.Views;

public partial class DownloadsPage : BasePage
{
    private static readonly DataFormat<string> QueueItemDataFormat =
        DataFormat.CreateStringApplicationFormat("OpenSteamClient.DownloadQueueItem");

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

    private async void QueueDragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DownloadItemViewModel item }) {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(QueueItemDataFormat, item.AppID.ToString()));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void QueueItem_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(QueueItemDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void QueueItem_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is DownloadsPageViewModel viewModel &&
            sender is Control { DataContext: DownloadItemViewModel target } &&
            e.DataTransfer.TryGetValue(QueueItemDataFormat) is { } sourceAppID) {
            viewModel.MoveDownloadBefore(sourceAppID, target);
        }
    }
}
