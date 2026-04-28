using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace FitTrackr.MAUI.Models
{
    public enum ProgressTimeRange
    {
        OneMonth,
        ThreeMonths,
        AllTime
    }

    public enum ProgressMetricType
    {
        Weight,
        Volume,
        EstimatedOneRm
    }

    public sealed partial class ProgressTimeRangeOption : ObservableObject
    {
        public ProgressTimeRangeOption(string title, ProgressTimeRange range)
        {
            Title = title;
            Range = range;
        }

        public string Title { get; }
        public ProgressTimeRange Range { get; }

        [ObservableProperty]
        private bool isSelected;

        public override bool Equals(object? obj)
        {
            return obj is ProgressTimeRangeOption other && Range == other.Range;
        }

        public override int GetHashCode()
        {
            return Range.GetHashCode();
        }
    }

    public sealed partial class ProgressExerciseOption : ObservableObject
    {
        public ProgressExerciseOption(string title, string subtitle)
        {
            Title = title;
            Subtitle = subtitle;
        }

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string subtitle = string.Empty;

        [ObservableProperty]
        private bool isSelected;

        public override bool Equals(object? obj)
        {
            return obj is ProgressExerciseOption other && string.Equals(Title, other.Title, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Title ?? string.Empty);
        }
    }

    public sealed class ProgressMetricOption : ObservableObject
    {
        private bool _isSelected;

        public ProgressMetricOption(string title, ProgressMetricType type)
        {
            Title = title;
            Type = type;
        }

        public string Title { get; }
        public ProgressMetricType Type { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public override bool Equals(object? obj)
        {
            return obj is ProgressMetricOption other && Type == other.Type;
        }

        public override int GetHashCode()
        {
            return Type.GetHashCode();
        }
    }

    public sealed class ProgressSummaryStat
    {
        public ProgressSummaryStat(string title, string value, string subtitle, Color accentColor)
        {
            Title = title;
            Value = value;
            Subtitle = subtitle;
            AccentColor = accentColor;
        }

        public string Title { get; }
        public string Value { get; }
        public string Subtitle { get; }
        public Color AccentColor { get; }
    }

    public sealed class ProgressChartPoint
    {
        public ProgressChartPoint(string label, string valueLabel, double value, bool isLastPoint, bool isPeakPoint)
        {
            Label = label;
            ValueLabel = valueLabel;
            Value = value;
            IsLastPoint = isLastPoint;
            IsPeakPoint = isPeakPoint;
        }

        public string Label { get; }
        public string ValueLabel { get; }
        public double Value { get; }
        public bool IsLastPoint { get; }
        public bool IsPeakPoint { get; }
    }

    public sealed class ProgressInsightItem
    {
        public ProgressInsightItem(string text, Color accentColor)
        {
            Text = text;
            AccentColor = accentColor;
        }

        public string Text { get; }
        public Color AccentColor { get; }
    }

    public sealed class ProgressLineChartDrawable : IDrawable
    {
        private readonly IReadOnlyList<double> _values;
        private readonly int _lastIndex;
        private readonly int _peakIndex;

        public ProgressLineChartDrawable(IReadOnlyList<double> values, int lastIndex, int peakIndex)
        {
            _values = values;
            _lastIndex = lastIndex;
            _peakIndex = peakIndex;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.Antialias = true;

            var chartLeft = 10f;
            var chartRight = dirtyRect.Width - 10f;
            var chartTop = 12f;
            var chartBottom = dirtyRect.Height - 14f;
            var chartHeight = chartBottom - chartTop;

            canvas.StrokeColor = Color.FromArgb("#2E2E2E");
            canvas.StrokeSize = 1;

            for (var i = 0; i < 3; i++)
            {
                var y = chartTop + (chartHeight / 2f) * i;
                canvas.DrawLine(chartLeft, y, chartRight, y);
            }

            if (_values.Count == 0)
            {
                return;
            }

            var maxValue = _values.Max();
            var minValue = _values.Min();
            var range = maxValue - minValue;
            if (Math.Abs(range) < 0.001)
            {
                range = maxValue == 0 ? 1 : Math.Abs(maxValue);
            }

            var points = new List<PointF>(_values.Count);
            var step = _values.Count == 1 ? 0 : (chartRight - chartLeft) / (_values.Count - 1);

            for (var i = 0; i < _values.Count; i++)
            {
                var normalized = _values.Count == 1
                    ? 0.5f
                    : (float)((_values[i] - minValue) / range);
                var x = chartLeft + (step * i);
                var y = chartBottom - (normalized * chartHeight);
                points.Add(new PointF(x, y));
            }

            var areaPath = new PathF();
            areaPath.MoveTo(points[0].X, chartBottom);
            foreach (var point in points)
            {
                areaPath.LineTo(point.X, point.Y);
            }
            areaPath.LineTo(points[^1].X, chartBottom);
            areaPath.Close();

            canvas.FillColor = Color.FromArgb("#33FF8A65");
            canvas.FillPath(areaPath);

            var linePath = new PathF();
            linePath.MoveTo(points[0].X, points[0].Y);
            foreach (var point in points.Skip(1))
            {
                linePath.LineTo(point.X, point.Y);
            }

            canvas.StrokeColor = Color.FromArgb("#FF8A65");
            canvas.StrokeSize = 2.5f;
            canvas.DrawPath(linePath);

            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var isPeak = i == _peakIndex;
                var isLast = i == _lastIndex;

                canvas.FillColor = Color.FromArgb("#0F0F0F");
                canvas.FillCircle(point.X, point.Y, isLast ? 5f : 4f);

                canvas.StrokeColor = isPeak ? Color.FromArgb("#FFD180") : Color.FromArgb("#FF8A65");
                canvas.StrokeSize = isLast ? 3f : 2f;
                canvas.DrawCircle(point.X, point.Y, isLast ? 5f : 4f);
            }
        }
    }
}