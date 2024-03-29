﻿#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutopilotQuick.Annotations;
using AutopilotQuick.Steps;
using BooleanToVisibilityConverter = AutopilotQuick.Converters.BooleanToVisibilityConverter;

#endregion

namespace AutopilotQuick.StepDisplay;

public static class Extensions
{
    public static IEnumerable<(T item, int index)> WithIndex<T>(this IEnumerable<T> source)
    {
        return source.Select((item, index) => (item, index));
    }
}

public partial class StepProgressBar : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(StepProgressBar), new PropertyMetadata(default(double)));

    public static readonly DependencyProperty IndeterminateProperty = DependencyProperty.Register(
        "Indeterminate", typeof(bool), typeof(StepProgressBar), new PropertyMetadata(false, IndeterminatePropertyChanged));

    private ObservableList<StepBase> _steps = new ObservableList<StepBase>();


    public double Progress
    {
        get { return (double)GetValue(ProgressProperty); }
        set { SetValue(ProgressProperty, value); }
    }

    public ObservableList<StepBase> Steps
    {
        get => _steps;
        set
        {
            if (Equals(value, _steps)) return;
            _steps = value;
            OnPropertyChanged();
        }
    }

    public bool Indeterminate
    {
        get { return (bool)GetValue(IndeterminateProperty); }
        set
        {
            SetValue(IndeterminateProperty, value);
            OnPropertyChanged();
        }
    }
    public StepProgressBar()
    {
        InitializeComponent();
        LayoutRoot.DataContext = this;
        PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(Steps))
            {
                this.Dispatcher.Invoke(StepsUpdated);

            }
        };
        
        Steps = new ObservableList<StepBase>(TaskManager.GetInstance().Steps);
    }

    private void StepsUpdated()
    {
        LayoutRoot.ColumnDefinitions.Clear(); //Clear the columns out
        LayoutRoot.Children.RemoveRange(1, LayoutRoot.Children.Count);
        
        var binding = new Binding("Indeterminate")
        {
            Converter = new BooleanToVisibilityConverter() { True = Visibility.Hidden, False = Visibility.Visible}
        };
        
        foreach (var (step,idx) in _steps.WithIndex())
        {
            LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition(){Width = new GridLength(step.ProgressWeight(), GridUnitType.Star)});
            var sep = new Rectangle
            {
                Width = 1,
                Height = Bar.Height,
                IsHitTestVisible = false,
                Opacity = 0.5,
                Fill = (Brush)FindResource("MahApps.Brushes.Separator"),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            sep.SetBinding(Rectangle.VisibilityProperty, binding);
            Grid.SetZIndex(sep, 1);
            Grid.SetColumn(sep, idx);
            LayoutRoot.Children.Add(sep);
        }
    
        Grid.SetColumnSpan(Bar, LayoutRoot.ColumnDefinitions.Count);
        
    }


    private static void IndeterminatePropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}