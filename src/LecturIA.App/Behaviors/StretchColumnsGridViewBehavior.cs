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
    private double[]? _columnWeights;

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
        _columnWeights = null;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CaptureColumnWeights();
        Redistribute();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_columnWeights is not null)
        {
            Redistribute();
        }
    }

    private void CaptureColumnWeights()
    {
        if (AssociatedObject.View is not GridView gridView)
        {
            return;
        }

        _columnWeights = gridView.Columns
            .Select(column => double.IsNaN(column.Width) || column.Width <= 0 ? 1d : column.Width)
            .ToArray();
    }

    private void Redistribute()
    {
        if (AssociatedObject.View is not GridView gridView ||
            _columnWeights is null ||
            _columnWeights.Length != gridView.Columns.Count)
        {
            return;
        }

        // Reserve space for the vertical scrollbar so columns never overflow.
        const double scrollBarWidth = 18;
        var available = AssociatedObject.ActualWidth - scrollBarWidth;
        var totalWeight = _columnWeights.Sum();

        if (available <= 0 || totalWeight <= 0)
        {
            return;
        }

        for (var index = 0; index < gridView.Columns.Count; index++)
        {
            gridView.Columns[index].Width = available * _columnWeights[index] / totalWeight;
        }
    }
}
