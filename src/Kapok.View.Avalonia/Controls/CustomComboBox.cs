using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// Direct port of Kapok.View.Wpf's CustomComboBox: when the box gained focus via the keyboard
/// before its editable text box existed (e.g. tabbing to it while its template hadn't applied
/// yet), give the now-realized text box real focus and put the caret at the end, once the
/// template is actually applied.
///
/// Avalonia's ComboBox already has the same template contract WPF's did (confirmed via
/// reflection against the installed Avalonia.Controls.dll and the real Fluent theme XAML on
/// GitHub): a `PART_EditableTextBox` template part, and OnApplyTemplate(TemplateAppliedEventArgs)
/// is overridable just like WPF's parameterless OnApplyTemplate() was. The one real difference:
/// WPF splits "focus" into a separate keyboard-focus concept (OnGotKeyboardFocus/
/// OnLostKeyboardFocus, which WPF actually raises for pointer clicks too, not just keyboard nav)
/// - Avalonia has a single unified focus model (OnGotFocus/OnLostFocus), so those are the direct
/// equivalent here, not a NavigationMethod-filtered subset of them.
/// </summary>
public class CustomComboBox : ComboBox
{
    private bool _hasKeyboardFocus;
    private TextBox? _textBox;

    // Avalonia's implicit ControlTheme lookup keys strictly off StyleKeyOverride (GetType() by
    // default - confirmed by reading StyledElement.GetEffectiveTheme()/TryFindResource in the
    // Avalonia repo), with no fallback walk up the base-type chain the way a plain CSS-style
    // selector match would do. Without this override, a CustomComboBox instance would look for a
    // ControlTheme keyed to typeof(CustomComboBox), never find one (FluentTheme only registers
    // one for typeof(ComboBox)), and silently render with no template at all - confirmed the hard
    // way via a real headless screenshot (a LookupComboBox with no border, no popup, and a
    // squashed few-pixel-tall Bounds) before finding this. This is the standard Avalonia idiom
    // for "inherit a base control's default theme in a subclass", equivalent to WPF's
    // DefaultStyleKeyProperty.OverrideMetadata pattern.
    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _textBox = e.NameScope.Find<TextBox>("PART_EditableTextBox");
        if (_hasKeyboardFocus && _textBox != null)
        {
            var textBox = _textBox;
            // Matches WPF's Dispatcher.BeginInvoke - focusing synchronously here doesn't stick
            // because the template's visual tree isn't fully attached/measured yet at this point.
            Dispatcher.UIThread.Post(() =>
            {
                textBox.Focus();
                textBox.CaretIndex = textBox.Text?.Length ?? 0;
            });
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _hasKeyboardFocus = false;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _hasKeyboardFocus = true;
    }
}
