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
    
    private int minSize = 64;
    private int maxSize = 175;
    private int minSpeed = 2; //Speed that the smallest ones will go
    private int maxSpeed = 4; //Speed that the biggest ones will go
    
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
        ScreenHeight = SystemParameters.PrimaryScreenHeight;
        ScreenWidth = SystemParameters.PrimaryScreenWidth;
        InitializeComponent();
    }

    private static Random rnd = new Random();
    private void RandomizeCookieOrientation(Image cookie)
    {
        var angle = rnd.Next(0, 361);
        RotateTransform rotateTransform = new RotateTransform(angle);
        cookie.LayoutTransform = rotateTransform;

        if (rnd.Next(0, 100) < 50) //50% chance of flip
        {
            cookie.RenderTransformOrigin = new Point(0.5, 0.5);
            ScaleTransform flipTrans = new ScaleTransform
            {
                ScaleX = -1
            };
            cookie.RenderTransform = flipTrans;
        }
    }

    private void RandomizeHorizontalPosition(Image cookie)
    {
        var TopBound = (int)Math.Round(ScreenWidth - cookie.Width, 0);
        var newLeft = rnd.Next(0, TopBound+1);
        Canvas.SetLeft(cookie, newLeft);
    }
    
    private void RandomizeCookieSize(Image cookie)
    {
        cookie.Width = rnd.Next(minSize,maxSize);
        cookie.Height = cookie.Width;
    }

    private static double Map (double value, double fromSource, double toSource, double fromTarget, double toTarget)
    {
        return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
    }
    
    private void ApplyAnimation(Image cookie)
    {
        var currentTop = Canvas.GetTop(cookie);
        var easeExp = Map(cookie.Height, minSize, maxSize, minSpeed, maxSpeed);
        var animation = new DoubleAnimation(currentTop, ScreenHeight+(cookie.Height*3), new Duration(1.Seconds()))
        {
            EasingFunction =new ExponentialEase(){EasingMode = EasingMode.EaseIn, Exponent = easeExp}
            
        };
        animation.Completed += (sender, args) =>
        {
            CookieCanvas.Children.Remove(cookie);
        };
        cookie.BeginAnimation(Canvas.TopProperty, animation);
    }

    private void Cookie_OnLoaded(object sender, RoutedEventArgs e)
    {
        Width = ScreenWidth;
        Height = ScreenHeight;
        Top = 0;
        Left = 0;
    }

    private static Uri GetRandomCookieFromBaker() => new Uri($"pack://application:,,,/Resources/Egg/{Baker.SurpriseMe().FileName}.bmp");

    public void AddCookie()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var newCookie = new Image
            {
                Source = new CachedBitmap(new BitmapImage(GetRandomCookieFromBaker()), BitmapCreateOptions.None, BitmapCacheOption.Default)
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