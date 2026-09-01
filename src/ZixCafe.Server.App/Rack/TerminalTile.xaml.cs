using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZixCafe.Server.App.Rack;

public partial class TerminalTile : UserControl
{
    private Storyboard? _railPulse;

    public TerminalTile()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (e.NewValue is TileViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            RenderState(vm);
        }
    }

    private void Detach()
    {
        if (DataContext is TileViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        StopPulse();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not TileViewModel vm)
        {
            return;
        }
        if (e.PropertyName is nameof(TileViewModel.IsRunning) or nameof(TileViewModel.IsSelected))
        {
            RenderState(vm);
        }
    }

    private void RenderState(TileViewModel vm)
    {
        if (vm.IsRunning)
        {
            Rail.Fill = FindResource("GoldDeepBrush") as Brush ?? Brushes.Orange;
            StartPulse();
        }
        else
        {
            StopPulse();
            Rail.Fill = Brushes.Transparent;
        }

        Card.BorderBrush = vm.IsSelected
            ? FindResource("GoldDeepBrush") as Brush ?? Brushes.Orange
            : FindResource("LineBrush") as Brush ?? Brushes.Gray;
    }

    private void StartPulse()
    {
        if (_railPulse is not null)
        {
            return;
        }
        _railPulse = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var fade = new DoubleAnimation(1d, 0.35d, new Duration(TimeSpan.FromSeconds(1)))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fade, Rail);
        Storyboard.SetTargetProperty(fade, new PropertyPath(nameof(Rail.Opacity)));
        _railPulse.Children.Add(fade);
        _railPulse.Begin(Rail, isControllable: true);
    }

    private void StopPulse()
    {
        _railPulse?.Remove(Rail);
        _railPulse = null;
        Rail.Opacity = 1d;
    }
}
