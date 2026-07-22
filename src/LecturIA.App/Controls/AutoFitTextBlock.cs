using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LecturIA.App.Controls;

/// <summary>
/// Displays wrapped text at the largest font size that fits the control's
/// available area without scrolling. Unlike a <see cref="Viewbox"/>, the
/// text wraps to the full available width and only the font size is
/// adjusted, so long passages stay as large and legible as possible and no
/// horizontal space is wasted.
/// </summary>
/// <remarks>
/// Intended for the on-screen reading passage that children read aloud: it
/// must always be fully visible (no scrollbar) and as big as the panel
/// allows. The best font size is found by a binary search that measures
/// candidate sizes with <see cref="FormattedText"/>. That measurement is a
/// pure calculation: it never mutates the live visual tree during a layout
/// pass, so it does not trigger the re-entrant layout invalidation that a
/// naive "measure the real TextBlock while arranging" approach causes. The
/// resolved size is cached and only recomputed when the available size or
/// the text actually changes.
/// </remarks>
public sealed class AutoFitTextBlock : Decorator
{
    private const int SearchIterations = 12;

    private readonly TextBlock _textBlock;

    private double _cachedWidth = -1;
    private double _cachedHeight = -1;
    private string? _cachedText;
    private double _cachedFontSize = double.NaN;

    /// <summary>Initializes the control and its inner wrapped text block.</summary>
    public AutoFitTextBlock()
    {
        _textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
        };
        Child = _textBlock;
    }

    /// <summary>Text to display.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(AutoFitTextBlock),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnTextChanged));

    /// <summary>Brush used to paint the text.</summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground),
            typeof(Brush),
            typeof(AutoFitTextBlock),
            new FrameworkPropertyMetadata(Brushes.Black, OnForegroundChanged));

    /// <summary>Largest font size the text may use.</summary>
    public static readonly DependencyProperty MaxFontSizeProperty =
        DependencyProperty.Register(
            nameof(MaxFontSize),
            typeof(double),
            typeof(AutoFitTextBlock),
            new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Smallest font size the text may shrink to.</summary>
    public static readonly DependencyProperty MinFontSizeProperty =
        DependencyProperty.Register(
            nameof(MinFontSize),
            typeof(double),
            typeof(AutoFitTextBlock),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Line height as a multiple of the resolved font size.</summary>
    public static readonly DependencyProperty LineHeightRatioProperty =
        DependencyProperty.Register(
            nameof(LineHeightRatio),
            typeof(double),
            typeof(AutoFitTextBlock),
            new FrameworkPropertyMetadata(1.35, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Text to display.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Brush used to paint the text.</summary>
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Largest font size the text may use.</summary>
    public double MaxFontSize
    {
        get => (double)GetValue(MaxFontSizeProperty);
        set => SetValue(MaxFontSizeProperty, value);
    }

    /// <summary>Smallest font size the text may shrink to.</summary>
    public double MinFontSize
    {
        get => (double)GetValue(MinFontSizeProperty);
        set => SetValue(MinFontSizeProperty, value);
    }

    /// <summary>Line height as a multiple of the resolved font size.</summary>
    public double LineHeightRatio
    {
        get => (double)GetValue(LineHeightRatioProperty);
        set => SetValue(LineHeightRatioProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        var availableWidth = double.IsInfinity(constraint.Width) ? 0 : constraint.Width;
        var availableHeight = double.IsInfinity(constraint.Height) ? 0 : constraint.Height;

        if (availableWidth > 0 && availableHeight > 0)
        {
            var fontSize = ResolveFontSize(availableWidth, availableHeight);

            // Only assign when the value actually changes. Assigning the
            // same value is a no-op in WPF, so once the size converges the
            // control stops invalidating itself and layout goes idle.
            if (double.IsNaN(_textBlock.FontSize) || Math.Abs(_textBlock.FontSize - fontSize) > 0.05)
            {
                _textBlock.FontSize = fontSize;
                _textBlock.LineHeight = fontSize * LineHeightRatio;
            }
        }

        _textBlock.Measure(constraint);
        return new Size(availableWidth, availableHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        _textBlock.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    private double ResolveFontSize(double width, double height)
    {
        var text = Text ?? string.Empty;

        if (!double.IsNaN(_cachedFontSize)
            && string.Equals(text, _cachedText, StringComparison.Ordinal)
            && Math.Abs(width - _cachedWidth) < 0.5
            && Math.Abs(height - _cachedHeight) < 0.5)
        {
            return _cachedFontSize;
        }

        var min = Math.Max(1.0, Math.Min(MinFontSize, MaxFontSize));
        var max = Math.Max(min, MaxFontSize);

        double best;
        if (text.Length == 0)
        {
            best = min;
        }
        else
        {
            best = min;
            var lo = min;
            var hi = max;
            for (var i = 0; i < SearchIterations; i++)
            {
                var mid = (lo + hi) / 2.0;
                if (FitsAt(text, mid, width, height))
                {
                    best = mid;
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
        }

        _cachedWidth = width;
        _cachedHeight = height;
        _cachedText = text;
        _cachedFontSize = best;
        return best;
    }

    private bool FitsAt(string text, double fontSize, double width, double height)
    {
        var typeface = new Typeface(
            _textBlock.FontFamily,
            _textBlock.FontStyle,
            _textBlock.FontWeight,
            _textBlock.FontStretch);

        // Measurement in DIPs; pixelsPerDip only affects glyph pixel
        // snapping, not the wrapped height, so a fixed 1.0 is fine here and
        // avoids requiring the control to be connected to a presentation
        // source.
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip: 1.0)
        {
            MaxTextWidth = width,
            LineHeight = fontSize * LineHeightRatio,
            Trimming = TextTrimming.None,
        };

        return formatted.Height <= height
            && formatted.WidthIncludingTrailingWhitespace <= width + 0.5;
    }

    private void InvalidateFit()
    {
        _cachedFontSize = double.NaN;
        InvalidateMeasure();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AutoFitTextBlock)d;
        control._textBlock.Text = (string)e.NewValue;
        control.InvalidateFit();
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AutoFitTextBlock)d;
        control._textBlock.Foreground = (Brush)e.NewValue;
    }
}
