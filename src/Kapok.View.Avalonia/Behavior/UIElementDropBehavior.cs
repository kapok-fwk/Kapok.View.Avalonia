using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Xaml.Interactivity;
using Kapok.View;

namespace Kapok.View.Avalonia.Behavior;

/// <summary>
/// Rewrite (not a port - see the porting plan's own note on this file) of Kapok.View.Wpf's
/// UIElementDropBehavior/AdornerManager/UIElementDropAdorner: shows a "Drop files here" overlay
/// while a file is dragged over the attached control, and forwards dropped/pasted files to the
/// control's DataContext if it implements <see cref="IDropTargetOnPage"/>.
///
/// Scoped to file drops only. WPF's version also supported a generic in-app "IDropTarget"
/// (drag one UI element onto another via a magic "DragSource" data key) - that interface was
/// defined locally in WPF's own behavior file, not part of Kapok.View's shared contracts (checked:
/// only IDropTargetOnPage is), and nothing in this port ever originates such a drag, so it isn't
/// reusable framework surface to port - left out rather than built speculatively.
///
/// Real API differences from WPF, not just a syntax swap:
/// - Avalonia's drag-drop data model (confirmed via reflection against the installed
///   Avalonia.Controls.dll - this is a genuinely new API shape, not the older IDataObject one
///   still described in a lot of older Avalonia docs/samples) is IDataTransfer/IDataTransferItem,
///   list-of-formats based rather than WPF's string-keyed IDataObject.GetData/GetFormats. Getting
///   dropped files is `DragEventArgs.DataTransfer.TryGetFiles()` (DataTransferExtensions),
///   returning IStorageItem[] instead of WPF's string[] - IStorageItem.Path.LocalPath is the
///   file path.
/// - Avalonia's clipboard (TopLevel.Clipboard) is fully async
///   (Task&lt;IAsyncDataTransfer&gt; TryGetDataAsync()) where WPF's Clipboard.GetDataObject() is
///   synchronous - the Ctrl+V handler here is `async void` accordingly (standard, accepted
///   pattern for a UI event handler that can't itself be awaited by a caller).
/// - No WPF-style Adorner/AdornerLayer.OnRender custom drawing here; the overlay is a plain
///   Border+StackPanel control tree added to Avalonia's own AdornerLayer via
///   AdornerLayer.SetAdornedElement, matching how every other visual in this project is built
///   (in C#, not a markup+code-behind split) rather than reintroducing WPF's manual
///   DrawingContext icon/text layout for a purely cosmetic overlay.
/// </summary>
public class UIElementDropBehavior : Behavior<Control>
{
    private Control? _dropOverlay;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject == null)
            return;

        DragDrop.SetAllowDrop(AssociatedObject, true);
        AssociatedObject.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AssociatedObject.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
        AssociatedObject.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            DragDrop.SetAllowDrop(AssociatedObject, false);
            AssociatedObject.RemoveHandler(DragDrop.DragEnterEvent, OnDragEnter);
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
            AssociatedObject.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        }

        RemoveOverlay();

        base.OnDetaching();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (CanAccept(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.Copy;
            ShowOverlay();
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        RemoveOverlay();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (CanAccept(e.DataTransfer))
            Drop(e.DataTransfer);

        RemoveOverlay();
        e.Handled = true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        if (AssociatedObject == null)
            return;

        var clipboard = TopLevel.GetTopLevel(AssociatedObject)?.Clipboard;
        if (clipboard == null)
            return;

        // Clipboard is fully async (IAsyncDataTransfer), unlike drag-drop's synchronous
        // IDataTransfer - a separate small path rather than forcing CanAccept/Drop to handle
        // both, since AsyncDataTransferExtensions' shape (TryGetFilesAsync returning a Task) is
        // genuinely different from DataTransferExtensions' synchronous TryGetFiles.
        if (AssociatedObject?.DataContext is not IDropTargetOnPage dropTargetOnPage)
            return;

        var dataTransfer = await clipboard.TryGetDataAsync();
        if (dataTransfer == null || !dataTransfer.Contains(DataFormat.File))
            return;

        var files = await dataTransfer.TryGetFilesAsync();
        if (files == null || files.Length == 0)
            return;

        var paths = GetFilePaths(files);
        if (dropTargetOnPage.CanDropFile(paths))
            dropTargetOnPage.DropFile(paths);
    }

    private bool CanAccept(IDataTransfer? data)
    {
        if (AssociatedObject?.DataContext is not IDropTargetOnPage dropTargetOnPage)
            return false;

        if (data == null || !data.Contains(DataFormat.File))
            return false;

        var files = data.TryGetFiles();
        if (files == null || files.Length == 0)
            return false;

        return dropTargetOnPage.CanDropFile(GetFilePaths(files));
    }

    private void Drop(IDataTransfer data)
    {
        if (AssociatedObject?.DataContext is not IDropTargetOnPage dropTargetOnPage)
            return;

        var files = data.TryGetFiles();
        if (files == null || files.Length == 0)
            return;

        dropTargetOnPage.DropFile(GetFilePaths(files));
    }

    private static string[] GetFilePaths(IStorageItem[] files)
    {
        var paths = new string[files.Length];
        for (var i = 0; i < files.Length; i++)
            paths[i] = files[i].Path.LocalPath;
        return paths;
    }

    private void ShowOverlay()
    {
        if (_dropOverlay != null || AssociatedObject == null)
            return;

        var adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
        if (adornerLayer == null)
            return;

        _dropOverlay = new Border
        {
            Background = new SolidColorBrush(Colors.White, 0.5),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Drop files here",
                        FontSize = 18,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        AdornerLayer.SetAdornedElement(_dropOverlay, AssociatedObject);
        adornerLayer.Children.Add(_dropOverlay);
    }

    private void RemoveOverlay()
    {
        if (_dropOverlay == null || AssociatedObject == null)
            return;

        AdornerLayer.GetAdornerLayer(AssociatedObject)?.Children.Remove(_dropOverlay);
        _dropOverlay = null;
    }
}
