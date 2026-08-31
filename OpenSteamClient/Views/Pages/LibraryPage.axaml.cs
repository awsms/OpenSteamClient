using System;
using System.Collections.Generic;
using OpenSteamClient.Extensions;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using OpenSteamClient.Controls;

namespace OpenSteamClient.Views;

public partial class LibraryPage : BasePage
{
    public LibraryPage() : base()
    {
        InitializeComponent();
        this.TranslatableInit();
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
