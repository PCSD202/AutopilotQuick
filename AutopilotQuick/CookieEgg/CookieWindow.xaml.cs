using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutopilotQuick.Annotations;
using Humanizer;

namespace AutopilotQuick.CookieEgg;

public partial class CookieWindow : INotifyPropertyChanged
{
    public double ScreenWidth
    {
        get => _screenWidth;
        set
        {
            if (value.Equals(_screenWidth)) return;
            _screenWidth = value;
            OnPropertyChanged();
        }
    }

    public double ScreenHeight
    {
        get => _screenHeight;
        set
        {
            if (value.Equals(_screenHeight)) return;
            _screenHeight = value;
            OnPropertyChanged();
        }
    }

    private double _screenWidth;
    private double _screenHeight;

    public CookieWindow()
    {
        DataContext = this;
        ScreenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;
        ScreenWidth = SystemParameters.PrimaryScreenWidth;
        InitializeComponent();
    }

    private static Random rnd = new Random();
    private void RandomizeCookieOrientation(Image cookie)
    {
        var angle = rnd.Next(0, 361);
        RotateTransform rotateTransform = new RotateTransform(angle);
        cookie.LayoutTransform = rotateTransform;
    }

    private void RandomizeHorizontalPosition(Image cookie)
    {
        var TopBound = (int)Math.Round(ScreenWidth - cookie.Width, 0);
        var newLeft = rnd.Next(0, TopBound+1);
        Canvas.SetLeft(cookie, newLeft);
    }
    
    private void RandomizeCookieSize(Image cookie)
    {
        cookie.Width = rnd.Next(64,256);
        cookie.Height = cookie.Width;
    }
    
    private void ApplyAnimation(Image cookie)
    {
        var currentTop = Canvas.GetTop(cookie);
        var animation = new DoubleAnimation(currentTop, ScreenHeight+(cookie.Height*3), new Duration(1.Seconds()))
        {
            EasingFunction =new ExponentialEase(){EasingMode = EasingMode.EaseIn}
            
        };
        animation.Completed += (sender, args) =>
        {
            CookieCanvas.Children.Remove(cookie);
        };
        cookie.BeginAnimation(Canvas.TopProperty, animation);
    }

    private void Cookie_OnLoaded(object sender, RoutedEventArgs e)
    {
        //await Task.Delay(1000);
        //this.Dispatcher.Invoke(this.Close);
        Width = ScreenWidth;
        Height = ScreenHeight;
        Top = 0;
        Left = 0;
    }

    private BitmapImage? _cookieBitmap; 
    public void AddCookie()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _cookieBitmap ??= this.FindResource("PerfectCookie") as BitmapImage;
            var newCookie = new Image
            {
                Source = _cookieBitmap
            };
            RandomizeCookieSize(newCookie);
            RandomizeCookieOrientation(newCookie);
            RandomizeHorizontalPosition(newCookie);
            Canvas.SetTop(newCookie, -1.5*newCookie.Height);
            ApplyAnimation(newCookie);
            CookieCanvas.Children.Add(newCookie);
        }, DispatcherPriority.Background);

    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}