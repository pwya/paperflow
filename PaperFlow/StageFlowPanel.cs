using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PaperFlow;

// Keep all stages and dates visible. An optional next-action label only uses spare
// space in the final row; the full action is always available on the progress tooltip.
public sealed class StageFlowPanel : Panel
{
    public UIElement? OptionalTail { get; set; }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Layout(availableSize.Width, false);
    }
    protected override Size ArrangeOverride(Size finalSize) { Layout(finalSize.Width, true); return finalSize; }
    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var row = new List<(UIElement Child, double X, Size Size)>();
        void FinishRow()
        {
            if (arrange) foreach (var item in row) item.Child.Arrange(new Rect(item.X, y, item.Size.Width, rowHeight));
            usedWidth = Math.Max(usedWidth, x); y += rowHeight; x = 0; rowHeight = 0; row.Clear();
        }
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            if (child == OptionalTail)
            {
                bool fits = x + size.Width <= width;
                child.Visibility = fits ? Visibility.Visible : Visibility.Hidden;
                if (!fits) continue;
            }
            else if (x > 0 && x + size.Width > width) FinishRow();
            row.Add((child, x, size)); x += size.Width; rowHeight = Math.Max(rowHeight, size.Height);
        }
        FinishRow(); return new Size(usedWidth, y);
    }
}
