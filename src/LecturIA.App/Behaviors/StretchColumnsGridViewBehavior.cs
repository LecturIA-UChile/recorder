using System.Windows;
using System.Windows.Controls;

using Microsoft.Xaml.Behaviors;

namespace LecturIA.App.Behaviors;

/// <summary>
/// Distributes available width across all <see cref="GridViewColumn"/> instances
/// proportionally to their initial <see cref="GridViewColumn.Width"/> values,
/// so that the columns always fill the entire <see cref="ListView"/>.
/// </summary>
public sealed class StretchColumnsGridViewBehavior : Behavior<ListView>
{
    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SizeChanged += OnSizeChanged;
        AssociatedObject.Loaded += OnLoaded;
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        AssociatedObject.SizeChanged -= OnSizeChanged;
        AssociatedObject.Loaded -= OnLoaded;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Redistribute();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redistribute();

    private void Redistribute()
    {
        if (AssociatedObject.View is not GridView gridView)
        {
            return;
        }

        // Reserve space for the vertical scrollbar so columns never overflow.
        const double scrollBarWidth = 18;
        var available = AssociatedObject.ActualWidth - scrollBarWidth;

        if (available <= 0 || gridView.Columns.Count == 0)
        {
            return;
        }

        var share = available / gridView.Columns.Count;
        foreach (var column in gridView.Columns)
        {
            column.Width = share;
        }
    }
}
