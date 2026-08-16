using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;

namespace Kapok.View.Avalonia.Behavior;

/// <summary>
/// Which dimension(s) a <see cref="PopupThumbResizeBehavior"/> resizes.
/// </summary>
public enum PopupResizeDirection
{
    Horizontal,
    Vertical,
    Both
}

/// <summary>
/// Port of Kapok.View.Wpf's PopupThumbResizeBehavior: dragging the attached Thumb resizes an
/// ancestor Popup's content. WPF's version infers the resize direction from the Thumb's own
/// Cursor property (comparing against Cursors.SizeWE/SizeNS/SizeNWSE); Avalonia's StandardCursorType
/// has no diagonal resize cursor to match against (confirmed via reflection - only SizeWestEast/
/// SizeNorthSouth/SizeAll exist), so direction is an explicit Direction property here instead of
/// inferred from the cursor - more robust anyway, and decouples resize logic from a purely
/// cosmetic property.
///
/// WPF's version resizes the Popup object itself (Width/Height) and finds it by walking up the
/// *logical* tree (Popup.Child stays logically parented to the Popup even though - both in WPF and
/// in Avalonia, confirmed by reading Popup.cs's Child setter - the popup's content is presented via
/// a separate visual root once open, which is why FindLogicalParent/FindLogicalAncestorOfType is
/// used here rather than a visual-tree walk). This resizes the Popup's Child directly instead of
/// the Popup itself: Avalonia's Popup doesn't drive its presented size from its own Width/Height as
/// authoritatively as WPF's does, whereas setting the actual content's Width/Height reliably
/// resizes what's on screen.
/// </summary>
public class PopupThumbResizeBehavior : Behavior<Thumb>
{
    public static readonly StyledProperty<PopupResizeDirection> DirectionProperty =
        AvaloniaProperty.Register<PopupThumbResizeBehavior, PopupResizeDirection>(nameof(Direction));

    public PopupResizeDirection Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.DragDelta += OnDragDelta;
            AssociatedObject.DragStarted += OnDragStarted;
            AssociatedObject.DragCompleted += OnDragCompleted;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.DragDelta -= OnDragDelta;
            AssociatedObject.DragStarted -= OnDragStarted;
            AssociatedObject.DragCompleted -= OnDragCompleted;
        }

        base.OnDetaching();
    }

    protected virtual void OnDragDelta(object? sender, VectorEventArgs e)
    {
        var thumb = AssociatedObject;
        if (thumb == null)
            return;

        if (thumb.FindLogicalAncestorOfType<Popup>()?.Child is not Control content)
            return;

        if (Direction is PopupResizeDirection.Horizontal or PopupResizeDirection.Both)
        {
            var currentWidth = double.IsNaN(content.Width) ? content.Bounds.Width : content.Width;
            content.Width = Math.Clamp(currentWidth + e.Vector.X, content.MinWidth, content.MaxWidth);
        }

        if (Direction is PopupResizeDirection.Vertical or PopupResizeDirection.Both)
        {
            var currentHeight = double.IsNaN(content.Height) ? content.Bounds.Height : content.Height;
            content.Height = Math.Clamp(currentHeight + e.Vector.Y, content.MinHeight, content.MaxHeight);
        }
    }

    protected virtual void OnDragStarted(object? sender, VectorEventArgs e)
    {
        // This is called when the user starts dragging the thumb
    }

    protected virtual void OnDragCompleted(object? sender, VectorEventArgs e)
    {
        // This is called when the user finishes dragging the thumb
    }
}
