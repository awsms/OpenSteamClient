using System;
using System.Collections.Generic;
using OpenSteamClient.Extensions;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenSteamClient.Controls;

namespace OpenSteamClient.Views;

public partial class LibraryPage : BasePage
{
    private TopLevel? keyBindingTopLevel;

    public LibraryPage() : base()
    {
        InitializeComponent();
        this.TranslatableInit();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        keyBindingTopLevel = TopLevel.GetTopLevel(this);
        keyBindingTopLevel?.AddHandler(
            InputElement.KeyDownEvent,
            LibraryPage_OnKeyDown,
            RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        keyBindingTopLevel?.RemoveHandler(InputElement.KeyDownEvent, LibraryPage_OnKeyDown);
        keyBindingTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void LibraryPage_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        SearchBar.Focus();
        SearchBar.SelectAll();
        e.Handled = true;
    }
}
