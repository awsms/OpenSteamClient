using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using OpenSteamClient.ViewModels.Library;
using OpenSteamClient.Views.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenSteamworks.Client;
using OpenSteamworks.Client.Apps;
using OpenSteamworks.Client.Apps.Library;
using OpenSteamworks.Client.Managers;
using OpenSteamworks.Utils;
using OpenSteamClient.DI;

namespace OpenSteamClient.ViewModels;

public partial class LibraryPageViewModel : AvaloniaCommon.ViewModelBase
{
    public ObservableCollectionEx<CollectionItemViewModel> Nodes { get; init; } = new();
    public ObservableCollectionEx<Node> SelectedNodes { get; } = new();


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSideContent))]
    [NotifyPropertyChangedFor(nameof(ListColumnSpan))]
    private Control? sideContent;

    public bool HasSideContent => sideContent != null;
    public int ListColumnSpan => HasSideContent ? 1 : 3;

    private string searchText = string.Empty;
    public string SearchText {
        get => searchText;
        set {
            searchText = value;
            UpdateGamesList();
        }
    }

    [ObservableProperty]
    private bool showOnlyReadyToPlay;

    partial void OnShowOnlyReadyToPlayChanged(bool value) => UpdateGamesList();

    private void UpdateGamesList()
    {
        foreach (var coll in Nodes)
        {
            if (searchText == string.Empty && !ShowOnlyReadyToPlay) {
                coll.Children.ClearFilter();
            } else {
                coll.Children.SetFilter(node =>
                    node.GetSortableName().Contains(searchText, StringComparison.InvariantCultureIgnoreCase) &&
                    (!ShowOnlyReadyToPlay || node is LibraryAppViewModel { IsInstalled: true }));
                coll.Children.Sort();
            }
        }
    }

    private readonly OpenSteamworks.Client.Apps.Library.Library library;

    public LibraryPageViewModel(AppsManager appsManager, LibraryManager libraryManager)
    {
        library = libraryManager.GetLibrary();
        library.LibraryUpdated += OnLibraryUpdated;
        OnLibraryUpdated(this, EventArgs.Empty);

        this.SelectedNodes.CollectionChanged += SelectionChanged;
    }

    public void HideSidePane()
    {
        SideContent = null;
    }

    private void OnLibraryUpdated(object? sender, EventArgs e)
    {
        foreach (var collection in library.Collections)
        {
            var appids = library.GetAppsInCollection(collection.ID);
            if (appids.Count == 0) {
                // Don't show empty collections
                continue;
            }

            var collectionviewmodel = this.GetOrCreateCategory(library, collection);
            int removeCount = collectionviewmodel.Children.RemoveAll(i => !appids.Contains(i.GameID));
            Console.WriteLine("Removed " + removeCount + " apps");

            int addCount = 0;
            foreach (var app in appids)
            {
                var existingApp = collectionviewmodel.Children.Where(c => c.GameID == app).FirstOrDefault();
                if (existingApp != null)
                {
                    // Already in collection, no need to readd
                    continue;
                }

                var appViewModel = new LibraryAppViewModel(app);
                appViewModel.PropertyChanged += LibraryAppOnPropertyChanged;
                collectionviewmodel.Children.AddUnique(appViewModel);
                addCount++;
            }

            collectionviewmodel.Children.Sort();
            Console.WriteLine("Added " + addCount + " apps");
        }

        // Delete collections that no longer exist
        foreach (var item in Nodes.ToList())
        {
            if (item is CollectionItemViewModel cvm)
            {
                if (library.Collections.Find(c => c.ID == cvm.ID) == null)
                {
                    Nodes.Remove(item);
                }
            }
        }

        UpdateGamesList();
    }

    private void LibraryAppOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ShowOnlyReadyToPlay && e.PropertyName == nameof(LibraryAppViewModel.IsInstalled))
            AvaloniaApp.Current?.RunOnUIThread(Avalonia.Threading.DispatcherPriority.Background, UpdateGamesList);
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        if (!SelectedNodes[0].IsApp)
        {
            return;
        }

        var pane = new FocusedAppPane();
        pane.DataContext = AvaloniaApp.Container.Construct<FocusedAppPaneViewModel>(pane, SelectedNodes[0].GameID);
        SideContent = pane;
    }

    private CollectionItemViewModel GetOrCreateCategory(OpenSteamworks.Client.Apps.Library.Library library, Collection collection)
    {
        foreach (var item in Nodes)
        {
            if (item is CollectionItemViewModel cvm)
            {
                if (cvm.ID == collection.ID)
                {
                    return cvm;
                }
            }
        }

        CollectionItemViewModel vm = new(collection);
        Nodes.Add(vm);
        Nodes.Sort();

        return vm;
    }
}
