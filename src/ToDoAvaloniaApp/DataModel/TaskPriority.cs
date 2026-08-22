using System.ComponentModel.DataAnnotations;

namespace ToDoAvaloniaApp.DataModel;

/// <summary>
/// Priority of a <see cref="Task"/>.
///
/// Exists to give the showcase app a real enum property: Kapok's column generation has a
/// dedicated enum branch (a combo box column in WPF, a template column with a ComboBox editor in
/// Avalonia - Avalonia ships no DataGridComboBoxColumn), and localized enum captions via
/// [Display] are part of what that branch does. Without an enum anywhere in ToDoAvaloniaApp that
/// branch could only ever be verified by reading the code.
/// </summary>
public enum TaskPriority
{
    [Display(Name = "Low")]
    Low = 0,

    [Display(Name = "Normal")]
    Normal = 1,

    [Display(Name = "High")]
    High = 2,

    [Display(Name = "Urgent")]
    Urgent = 3
}
