using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SampleViewer.Controls
{
    // つまみが2つある、範囲指定用のスライダー
    // ドラッグ中は RangeChanged、つまみを離したときとダブルクリック（全範囲に戻す）で RangeCommitted
    public class RangeSlider : Canvas
    {
        private const double ThumbSize = 14;
        private const double TrackHeight = 4;

        private readonly Rectangle _track;
        private readonly Rectangle _selection;
        private readonly Thumb _lower;
        private readonly Thumb _upper;

        public double Minimum { get; private set; }
        public double Maximum { get; private set; } = 1;
        public double LowerValue { get; private set; }
        public double UpperValue { get; private set; } = 1;

        // つまみが止まる刻み
        public double Step { get; set; } = 1;

        public bool IsLowerAtMinimum => LowerValue <= Minimum;
        public bool IsUpperAtMaximum => UpperValue >= Maximum;

        public event EventHandler? RangeChanged;
        public event EventHandler? RangeCommitted;

        public RangeSlider()
        {
            Height = 20;
            Background = Brushes.Transparent;

            var brand = new SolidColorBrush(Color.FromRgb(0x0D, 0x2E, 0xA0));

            _track = new Rectangle
            {
                Height = TrackHeight,
                RadiusX = TrackHeight / 2,
                RadiusY = TrackHeight / 2,
                Fill = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xEA))
            };
            _selection = new Rectangle
            {
                Height = TrackHeight,
                RadiusX = TrackHeight / 2,
                RadiusY = TrackHeight / 2,
                Fill = brand
            };

            _lower = CreateThumb(brand);
            _upper = CreateThumb(brand);

            Children.Add(_track);
            Children.Add(_selection);
            Children.Add(_upper);
            Children.Add(_lower);

            _lower.DragDelta += (_, e) => Drag(_lower, e.HorizontalChange);
            _upper.DragDelta += (_, e) => Drag(_upper, e.HorizontalChange);
            _lower.DragCompleted += (_, _) => RangeCommitted?.Invoke(this, EventArgs.Empty);
            _upper.DragCompleted += (_, _) => RangeCommitted?.Invoke(this, EventArgs.Empty);

            SizeChanged += (_, _) => Arrange();
            IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
        }

        private static Thumb CreateThumb(Brush stroke)
        {
            var ellipse = new FrameworkElementFactory(typeof(Ellipse));
            ellipse.SetValue(Shape.FillProperty, Brushes.White);
            ellipse.SetValue(Shape.StrokeProperty, stroke);
            ellipse.SetValue(Shape.StrokeThicknessProperty, 2.0);

            return new Thumb
            {
                Width = ThumbSize,
                Height = ThumbSize,
                Cursor = Cursors.SizeWE,
                Template = new ControlTemplate(typeof(Thumb)) { VisualTree = ellipse }
            };
        }

        // 両端の値を決める。範囲が無い（最小＝最大）ときは操作できないようにする
        public void SetRange(double minimum, double maximum)
        {
            Minimum = minimum;
            Maximum = Math.Max(minimum, maximum);
            IsEnabled = Maximum > Minimum;
            SetValues(LowerValue, UpperValue);
        }

        public void SetValues(double lower, double upper)
        {
            LowerValue = Math.Clamp(lower, Minimum, Maximum);
            UpperValue = Math.Clamp(Math.Max(upper, LowerValue), Minimum, Maximum);
            Arrange();
        }

        private double UsableWidth => Math.Max(1, ActualWidth - ThumbSize);

        private double ToX(double value)
        {
            return Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) * UsableWidth : 0;
        }

        private void Drag(Thumb thumb, double change)
        {
            if (Maximum <= Minimum)
                return;

            double x = Math.Clamp(GetLeft(thumb) + change, 0, UsableWidth);
            double value = Minimum + x / UsableWidth * (Maximum - Minimum);

            // 刻みに丸める。両端はちょうど最小・最大にする
            if (Step > 0)
                value = Math.Round(value / Step) * Step;

            if (x <= 0) value = Minimum;
            if (x >= UsableWidth) value = Maximum;

            value = Math.Clamp(value, Minimum, Maximum);

            if (thumb == _lower)
                LowerValue = Math.Min(value, UpperValue);
            else
                UpperValue = Math.Max(value, LowerValue);

            Arrange();
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Arrange()
        {
            if (ActualWidth <= 0)
                return;

            double top = (ActualHeight - ThumbSize) / 2;
            double trackTop = (ActualHeight - TrackHeight) / 2;
            double lowerX = ToX(LowerValue);
            double upperX = ToX(UpperValue);

            _track.Width = UsableWidth;
            SetLeft(_track, ThumbSize / 2);
            SetTop(_track, trackTop);

            _selection.Width = Math.Max(0, upperX - lowerX);
            SetLeft(_selection, lowerX + ThumbSize / 2);
            SetTop(_selection, trackTop);

            SetLeft(_lower, lowerX);
            SetTop(_lower, top);
            SetLeft(_upper, upperX);
            SetTop(_upper, top);

            // 2つが重なったとき、動かせる側のつまみを上にする（右端で重なったら下限側）
            SetZIndex(_lower, LowerValue > (Minimum + Maximum) / 2 ? 2 : 0);
            SetZIndex(_upper, 1);
        }

        // ダブルクリックで全範囲に戻す
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (e.ClickCount != 2 || !IsEnabled)
                return;

            ResetToFullRange();
            e.Handled = true;
        }

        public void ResetToFullRange()
        {
            SetValues(Minimum, Maximum);
            RangeChanged?.Invoke(this, EventArgs.Empty);
            RangeCommitted?.Invoke(this, EventArgs.Empty);
        }
    }
}
