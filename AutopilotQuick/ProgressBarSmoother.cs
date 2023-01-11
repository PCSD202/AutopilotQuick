#region

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using Microsoft.Xaml.Behaviors;

#endregion

namespace AutopilotQuick
{
    public class ProgressBarSmoother
    {
        public static double GetSmoothValue(DependencyObject obj)
        {
            return (double)obj.GetValue(SmoothValueProperty);
        }

        public static void SetSmoothValue(DependencyObject obj, double value)
        {
            obj.SetValue(SmoothValueProperty, value);
        }

        public static readonly DependencyProperty SmoothValueProperty =
            DependencyProperty.RegisterAttached("SmoothValue", typeof(double), typeof(ProgressBarSmoother), new PropertyMetadata(0.0, changing));

        private static void changing(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var anim = new DoubleAnimation((double)e.OldValue, (double)e.NewValue, new TimeSpan(0, 0, 0, 0, 250));
            (d as ProgressBar).BeginAnimation(RangeBase.ValueProperty, anim);
        }
    }
    
    class ProgresBarAnimateBehavior : Behavior<ProgressBar>
    {
        bool _IsAnimating = false;

        protected override void OnAttached()
        {
            base.OnAttached();
            ProgressBar progressBar = this.AssociatedObject;
            progressBar.ValueChanged += ProgressBar_ValueChanged;
        }

        private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_IsAnimating)
                return;

            _IsAnimating = true;

            DoubleAnimation doubleAnimation = new DoubleAnimation
                (e.OldValue, e.NewValue, new Duration(TimeSpan.FromSeconds(0.3)), FillBehavior.Stop);
            doubleAnimation.Completed += Db_Completed;

            ((ProgressBar)sender).BeginAnimation(ProgressBar.ValueProperty, doubleAnimation);

            e.Handled = true;
        }

        private void Db_Completed(object sender, EventArgs e)
        {
            _IsAnimating = false;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            ProgressBar progressBar = this.AssociatedObject;
            progressBar.ValueChanged -= ProgressBar_ValueChanged;
        }
    }
}
