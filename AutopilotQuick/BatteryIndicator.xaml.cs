using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AQ.DeviceInfo;
using AutopilotQuick.Annotations;
using Humanizer;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using Timer = System.Threading.Timer;

namespace AutopilotQuick;

public static class GradientStopCollectionExtensions
{
    public static Color GetRelativeColor(this GradientStopCollection gsc, double offset)
    {
        var point = gsc.SingleOrDefault(f => f.Offset == offset);
        if (point != null) return point.Color;

        GradientStop before = gsc.First(w => w.Offset == gsc.Min(m => m.Offset));
        GradientStop after = gsc.First(w => w.Offset == gsc.Max(m => m.Offset));

        foreach (var gs in gsc)
        {
            if (gs.Offset < offset && gs.Offset > before.Offset)
            {
                before = gs;
            }
            if (gs.Offset > offset && gs.Offset < after.Offset)
            {
                after = gs;
            }
        }

        var color = new Color();

        color.ScA = (float)((offset - before.Offset) * (after.Color.ScA - before.Color.ScA) / (after.Offset - before.Offset) + before.Color.ScA);
        color.ScR = (float)((offset - before.Offset) * (after.Color.ScR - before.Color.ScR) / (after.Offset - before.Offset) + before.Color.ScR);
        color.ScG = (float)((offset - before.Offset) * (after.Color.ScG - before.Color.ScG) / (after.Offset - before.Offset) + before.Color.ScG);
        color.ScB = (float)((offset - before.Offset) * (after.Color.ScB - before.Color.ScB) / (after.Offset - before.Offset) + before.Color.ScB);

        return color;
    }
}

public partial class BatteryIndicator : INotifyPropertyChanged
{
    private Timer _timer = null;
    private PackIconMaterialKind _iconKind = PackIconMaterialKind.Battery10;
    private Brush _batteryColor;

    public string TooltipText
    {
        get => _tooltipText;
        set
        {
            if (value == _tooltipText) return;
            _tooltipText = value;
            OnPropertyChanged();
        }
    }

    private bool IsAnimating = false;
    private string _tooltipText = "";


    public BatteryIndicator()
    {
        DataContext = this;
        CalculateNewIcon();
        InitializeComponent();
        _timer = new Timer(Run, null, 100, 100);
    }
    

    private void CalculateNewIcon()
    {
        if (BatteryConnected)
        {
            if (IsAnimating)
            {
                if (Icon is not null)
                {
                    Icon.Invoke(() =>
                    {
                        Icon.BeginAnimation(OpacityProperty, null);
                    });
                    IsAnimating = false;
                }
            }

            TooltipText = $"Battery health: {BatteryHealth}%";
            

            if (BatteryHealth > 50)
            {
                var percent = (int)Math.Round(BatteryPercent / 10d, 0, MidpointRounding.ToZero);
                IconKind = percent switch
                {
                    0 => IsCharging ? PackIconMaterialKind.BatteryChargingOutline : PackIconMaterialKind.BatteryOutline,
                    1 => IsCharging ? PackIconMaterialKind.BatteryCharging10 : PackIconMaterialKind.Battery10,
                    2 => IsCharging ? PackIconMaterialKind.BatteryCharging20 : PackIconMaterialKind.Battery20,
                    3 => IsCharging ? PackIconMaterialKind.BatteryCharging30 : PackIconMaterialKind.Battery30,
                    4 => IsCharging ? PackIconMaterialKind.BatteryCharging40 : PackIconMaterialKind.Battery40,
                    5 => IsCharging ? PackIconMaterialKind.BatteryCharging50 : PackIconMaterialKind.Battery50,
                    6 => IsCharging ? PackIconMaterialKind.BatteryCharging60 : PackIconMaterialKind.Battery60,
                    7 => IsCharging ? PackIconMaterialKind.BatteryCharging70 : PackIconMaterialKind.Battery70,
                    8 => IsCharging ? PackIconMaterialKind.BatteryCharging80 : PackIconMaterialKind.Battery80,
                    9 => IsCharging ? PackIconMaterialKind.BatteryCharging90 : PackIconMaterialKind.Battery90,
                    10 => IsCharging ? PackIconMaterialKind.BatteryCharging100 : PackIconMaterialKind.Battery,
                    _ => IconKind
                };
                GradientStopCollection grsc = new GradientStopCollection(3)
                {
                    new(Colors.Red, 0),
                    new(Colors.Yellow, .5),
                    new(Colors.LimeGreen, 1)
                };
                var b = new SolidColorBrush(grsc.GetRelativeColor(BatteryPercent / 100d));
                b.Freeze();
                Dispatcher.CurrentDispatcher.Invoke(() =>
                {
                    BatteryColor = b;
                });  
            }
            else
            {
                IconKind = PackIconMaterialKind.BatteryAlert;
                GradientStopCollection grsc = new GradientStopCollection(3)
                {
                    new(Colors.Red, 0),
                    new(Colors.Red, 0.25),
                    new(Colors.Orange, .5)
                };
                var b = new SolidColorBrush(grsc.GetRelativeColor(BatteryHealth/100d));
                b.Freeze();
                Dispatcher.CurrentDispatcher.Invoke(() =>
                {
                    BatteryColor = b;
                });
                TooltipText = $"Battery health: {BatteryHealth}% < 50%. Bad battery";
            }
            
        }
        else
        {
            if (!IsAnimating)
            {
                if (Icon is not null)
                {
                    Icon.Invoke(() =>
                    {
                        DoubleAnimation BlinkingAnimation =
                            new DoubleAnimation(1, 0, Duration.Forever, FillBehavior.Stop)
                            {
                                AutoReverse = true,
                                RepeatBehavior = RepeatBehavior.Forever,
                                Duration = new Duration(500.Milliseconds())
                            };
                        BlinkingAnimation.Freeze();
                        Icon.BeginAnimation(OpacityProperty, BlinkingAnimation);
                    });

                    IsAnimating = true;
                }
            }
            IconKind = PackIconMaterialKind.BatteryOffOutline;
            var b = new SolidColorBrush(Colors.Red);
            b.Freeze();
            Dispatcher.CurrentDispatcher.Invoke(() =>
            {
                BatteryColor = b;
            });
            TooltipText = $"Battery not detected. Bad battery or none installed";

        }
        
    }

    public Brush BatteryColor
    {
        get => _batteryColor;
        set
        {
            if (Equals(value, _batteryColor)) return;
            _batteryColor = value;
            OnPropertyChanged();
        }
    }

    public PackIconMaterialKind IconKind
    {
        get => _iconKind;
        set
        {
            if (value == _iconKind) return;
            _iconKind = value;
            OnPropertyChanged();
        }
    }

    public void Run(Object? o)
    {
        Dispatcher.CurrentDispatcher.Invoke(() =>
        {
            try
            {
                BatteryConnected = DeviceInfo.BatteryConnected;
                BatteryHealth = DeviceInfo.BatteryHealth;
                var pwr = SystemInformation.PowerStatus;
                BatteryPercent = (int)Math.Round(pwr.BatteryLifePercent * 100, 0);
                IsCharging = pwr.PowerLineStatus == PowerLineStatus.Online;
                CalculateNewIcon();
            }
            catch (Exception e)
            {
                BatteryConnected = false;
                BatteryHealth = 0;
                BatteryPercent = 0;
                IsCharging = false;
                CalculateNewIcon();
            }
            
        });
    }

    private BatteryData _status { get; set; } = new BatteryData(0, false, 0, false);

    public BatteryData Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IsCharging
    {
        get => Status.IsCharging;
        set
        {
            if(IsCharging == value) return;
            Status = Status with { IsCharging = value };
            OnPropertyChanged();
        }
    }

    public int BatteryPercent
    {
        get => Status.BatteryPercent;
        set
        {
            if(BatteryPercent == value) return;
            Status = Status with { BatteryPercent = value };
            OnPropertyChanged();
        }
    }
    
    public uint BatteryHealth
    {
        get => Status.BatteryHealth;
        set
        {
            if(BatteryHealth == value) return;
            Status = Status with { BatteryHealth = value };
            OnPropertyChanged();
        }
    }
    
    public bool BatteryConnected
    {
        get => Status.BatteryConnected;
        set
        {
            if(BatteryConnected == value) return;
            Status = Status with { BatteryConnected = value };
            OnPropertyChanged();
        }
    }

    public record BatteryData(int BatteryPercent, bool IsCharging, uint BatteryHealth, bool BatteryConnected);

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}