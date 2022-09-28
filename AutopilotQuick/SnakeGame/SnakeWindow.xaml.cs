using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Serialization;
using AutopilotQuick.Annotations;
using MahApps.Metro.Controls;
using NHotkey.Wpf;

namespace AutopilotQuick.SnakeGame;

public partial class SnakeWindow : Window
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

    const int SnakeSquareSize = 40;
    const int SnakeStartLength = 2;
    const int SnakeStartSpeed = 200;
    const int SnakeSpeedThreshold = 100;
    private int currentScore = 0;  

    private SolidColorBrush snakeBodyBrush = Brushes.Green;
    private SolidColorBrush snakeHeadBrush = Brushes.YellowGreen;
    private List<SnakePart> snakeParts = new List<SnakePart>();
    private UIElement snakeFood = null;  
    private SolidColorBrush foodBrush = Brushes.Red;
    private System.Windows.Threading.DispatcherTimer gameTickTimer = new System.Windows.Threading.DispatcherTimer();
    public ObservableCollection<SnakeHighscore> HighscoreList { get; set; } = new ObservableCollection<SnakeHighscore>();
    
    const int MaxHighscoreListEntryCount = 5;

    public enum SnakeDirection
    {
        Left,
        Right,
        Up,
        Down
    };

    private SnakeDirection snakeDirection = SnakeDirection.Right;
    private int snakeLength;

    public SnakeWindow()
    {
        DataContext = this;
        ScreenHeight = SystemParameters.PrimaryScreenHeight;
        ScreenWidth = SystemParameters.PrimaryScreenWidth;

        InitializeComponent();
        Width = ScreenWidth;
        Height = ScreenHeight;
        Top = 0;
        Left = 0;

        gameTickTimer.Tick += GameTickTimer_Tick;
        LoadHighscoreList();
        Canvas.SetLeft(bdrWelcomeMessage, SnakeCanvas.Width/2-(bdrWelcomeMessage.Width/2));
        Canvas.SetTop(bdrWelcomeMessage, SnakeCanvas.Height/2-(bdrWelcomeMessage.Height/2));
        Canvas.SetLeft(bdrHighscoreList, SnakeCanvas.Width/2-(bdrHighscoreList.Width/2));
        Canvas.SetTop(bdrHighscoreList, SnakeCanvas.Height/2-(bdrHighscoreList.Height/2));
        Canvas.SetLeft(bdrNewHighscore, SnakeCanvas.Width/2-(bdrNewHighscore.Width/2));
        Canvas.SetTop(bdrNewHighscore, SnakeCanvas.Height/2-(bdrNewHighscore.Height/2));
        Canvas.SetLeft(bdrEndOfGame, SnakeCanvas.Width/2-(bdrEndOfGame.Width/2));
        Canvas.SetTop(bdrEndOfGame, SnakeCanvas.Height/2-(bdrEndOfGame.Height/2));
    }

    private void GameTickTimer_Tick(object sender, EventArgs e)
    {
        MoveSnake();
    }

    private void StartNewGame()  
    { 
        bdrWelcomeMessage.Visibility = Visibility.Collapsed;
        bdrHighscoreList.Visibility = Visibility.Collapsed;
        bdrEndOfGame.Visibility = Visibility.Collapsed;
        
        // Remove potential dead snake parts and leftover food...
        foreach(SnakePart snakeBodyPart in snakeParts)
        {
            if(snakeBodyPart.UiElement != null)
                SnakeCanvas.Children.Remove(snakeBodyPart.UiElement);
        }
        snakeParts.Clear();
        if(snakeFood != null)
            SnakeCanvas.Children.Remove(snakeFood);

        // Reset stuff
        currentScore = 0;
        snakeLength = SnakeStartLength;
        snakeDirection = SnakeDirection.Right;
        snakeParts.Add(new SnakePart() { Position = new Point(SnakeSquareSize * 5, SnakeSquareSize * 5) });
        gameTickTimer.Interval = TimeSpan.FromMilliseconds(SnakeStartSpeed);

        // Draw the snake again and some new food...
        DrawSnake();
        DrawSnakeFood();

        // Update status
        UpdateGameStatus();

        // Go!        
        gameTickTimer.IsEnabled = true;
    }

    private void DrawSnake()
    {
        foreach (SnakePart snakePart in snakeParts)
        {
            if (snakePart.UiElement == null)
            {
                snakePart.UiElement = new Rectangle()
                {
                    Width = SnakeSquareSize,
                    Height = SnakeSquareSize,
                    Fill = (snakePart.IsHead ? snakeHeadBrush : snakeBodyBrush)
                };
                SnakeCanvas.Children.Add(snakePart.UiElement);
                Canvas.SetTop(snakePart.UiElement, snakePart.Position.Y);
                Canvas.SetLeft(snakePart.UiElement, snakePart.Position.X);
            }
        }
    }
    private void DrawSnakeFood()
    {
        Point foodPosition = GetNextFoodPosition();
        snakeFood = new Ellipse()
        {
            Width = SnakeSquareSize,
            Height = SnakeSquareSize,
            Fill = foodBrush
        };
        SnakeCanvas.Children.Add(snakeFood);
        Canvas.SetTop(snakeFood, foodPosition.Y);
        Canvas.SetLeft(snakeFood, foodPosition.X);
    }

    private void MoveSnake()
    {
        // Remove the last part of the snake, in preparation of the new part added below  
        while (snakeParts.Count >= snakeLength)
        {
            SnakeCanvas.Children.Remove(snakeParts[0].UiElement);
            snakeParts.RemoveAt(0);
        }

        // Next up, we'll add a new element to the snake, which will be the (new) head  
        // Therefore, we mark all existing parts as non-head (body) elements and then  
        // we make sure that they use the body brush  
        foreach (SnakePart snakePart in snakeParts)
        {
            (snakePart.UiElement as Rectangle).Fill = snakeBodyBrush;
            snakePart.IsHead = false;
        }

        // Determine in which direction to expand the snake, based on the current direction  
        SnakePart snakeHead = snakeParts[snakeParts.Count - 1];
        double nextX = snakeHead.Position.X;
        double nextY = snakeHead.Position.Y;
        switch (snakeDirection)
        {
            case SnakeDirection.Left:
                nextX -= SnakeSquareSize;
                break;
            case SnakeDirection.Right:
                nextX += SnakeSquareSize;
                break;
            case SnakeDirection.Up:
                nextY -= SnakeSquareSize;
                break;
            case SnakeDirection.Down:
                nextY += SnakeSquareSize;
                break;
        }

        // Now add the new head part to our list of snake parts...  
        snakeParts.Add(new SnakePart()
        {
            Position = new Point(nextX, nextY),
            IsHead = true
        });
        //... and then have it drawn!
        DrawSnake();
        // Finally: Check if it just hit something!
        DoCollisionCheck();                
    }
    private Random rnd = new Random();
    
    private Point GetNextFoodPosition()
    {
        int maxX = (int)(SnakeCanvas.ActualWidth / SnakeSquareSize);
        int maxY = (int)(SnakeCanvas.ActualHeight / SnakeSquareSize);
        int foodX = rnd.Next(0, maxX) * SnakeSquareSize;
        int foodY = rnd.Next(0, maxY) * SnakeSquareSize;

        foreach(SnakePart snakePart in snakeParts)
        {
            if((snakePart.Position.X == foodX) && (snakePart.Position.Y == foodY))
                return GetNextFoodPosition();
        }

        return new Point(foodX, foodY);
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void HandleKey(Key key)
    {
        SnakeDirection originalSnakeDirection = snakeDirection;
        switch(key)
        {            
            case Key.Up:
                if(snakeDirection != SnakeDirection.Down)
                    snakeDirection = SnakeDirection.Up;
                break;
            case Key.Down:
                if(snakeDirection != SnakeDirection.Up)
                    snakeDirection = SnakeDirection.Down;
                break;
            case Key.Left:
                if(snakeDirection != SnakeDirection.Right)
                    snakeDirection = SnakeDirection.Left;
                break;
            case Key.Right:
                if(snakeDirection != SnakeDirection.Left)
                    snakeDirection = SnakeDirection.Right;
                break;
            case Key.Space:
                StartNewGame();
                break;
        }

        if (gameTickTimer.IsEnabled)
        {
            MoveSnake(); 
        }
        

    }

    private void EatSnakeFood()
    {
        if (snakeLength == 2)
        {
            snakeLength += 3;
        }
        snakeLength++;
        currentScore++;
        int timerInterval = Math.Max(SnakeSpeedThreshold, (int)gameTickTimer.Interval.TotalMilliseconds - (currentScore * 2));
        gameTickTimer.Interval = TimeSpan.FromMilliseconds(timerInterval);        
        SnakeCanvas.Children.Remove(snakeFood);
        DrawSnakeFood();
        UpdateGameStatus();
    }
    private void UpdateGameStatus()
    {        
        this.tbStatusScore.Text = currentScore.ToString();
        this.tbStatusSpeed.Text = gameTickTimer.Interval.TotalMilliseconds.ToString();
    }
    
    private void EndGame()
    {
        bool isNewHighscore = false;
        if(currentScore > 0)
        {
            int lowestHighscore = (this.HighscoreList.Count > 0 ? this.HighscoreList.Min(x => x.Score) : 0);
            if((currentScore > lowestHighscore) || (this.HighscoreList.Count < MaxHighscoreListEntryCount))
            {
                bdrNewHighscore.Visibility = Visibility.Visible;
                txtPlayerName.Focus();
                isNewHighscore = true;
            }
        }
        if(!isNewHighscore)
        {
            tbFinalScore.Text = currentScore.ToString();
            bdrEndOfGame.Visibility = Visibility.Visible;
        }
        gameTickTimer.IsEnabled = false;
    }
    
    private void DoCollisionCheck()
    {
        SnakePart snakeHead = snakeParts[snakeParts.Count - 1];
    
        if((snakeHead.Position.X == Canvas.GetLeft(snakeFood)) && (snakeHead.Position.Y == Canvas.GetTop(snakeFood)))
        {            
            EatSnakeFood();
            return;
        }

        if((snakeHead.Position.Y < 0) || (snakeHead.Position.Y >= SnakeCanvas.Height) ||
           (snakeHead.Position.X < 0) || (snakeHead.Position.X >= SnakeCanvas.Width))
        {
            EndGame();
        }

        foreach(SnakePart snakeBodyPart in snakeParts.Take(snakeParts.Count - 1))
        {
            if((snakeHead.Position.X == snakeBodyPart.Position.X) && (snakeHead.Position.Y == snakeBodyPart.Position.Y))
                EndGame();
        }
    }
    
    private void SnakeWindow_OnContentRendered(object? sender, EventArgs e)
    {
        HotkeyManager.Current.AddOrReplace("Exit", Key.Escape, ModifierKeys.None, true,
            (o, args) => { this.Dispatcher.Invoke(() => { this.Close(); }); });
        HotkeyManager.Current.AddOrReplace("Up", Key.Up, ModifierKeys.None, true, (o, args) => { this.Dispatcher.Invoke(() => { HandleKey(Key.Up); }); });
        HotkeyManager.Current.AddOrReplace("Down", Key.Down, ModifierKeys.None, true, (o, args) => { this.Dispatcher.Invoke(() => { HandleKey(Key.Down); }); });
        HotkeyManager.Current.AddOrReplace("Left", Key.Left, ModifierKeys.None, true, (o, args) => { this.Dispatcher.Invoke(() => { HandleKey(Key.Left); }); });
        HotkeyManager.Current.AddOrReplace("Right", Key.Right, ModifierKeys.None, true, (o, args) => { this.Dispatcher.Invoke(() => { HandleKey(Key.Right); }); });
        HotkeyManager.Current.AddOrReplace("Space", Key.Space, ModifierKeys.None, true, (o, args) => { this.Dispatcher.Invoke(() => { HandleKey(Key.Space); }); });
    }
    private void SaveHighscoreList()
    {
        XmlSerializer serializer = new XmlSerializer(typeof(ObservableCollection<SnakeHighscore>));
        using(Stream writer = new FileStream("snake_highscorelist.xml", FileMode.Create))
        {
            serializer.Serialize(writer, this.HighscoreList);
        }
    }
    
    private void LoadHighscoreList()
    {
        if(File.Exists("snake_highscorelist.xml"))
        {
            XmlSerializer serializer = new XmlSerializer(typeof(List<SnakeHighscore>));
            using(Stream reader = new FileStream("snake_highscorelist.xml", FileMode.Open))
            {            
                List<SnakeHighscore> tempList = (List<SnakeHighscore>)serializer.Deserialize(reader);
                this.HighscoreList.Clear();
                foreach(var item in tempList.OrderByDescending(x => x.Score))
                    this.HighscoreList.Add(item);
            }
        }
    }

    private void BtnShowHighscoreList_OnClick(object sender, RoutedEventArgs e)
    {
        bdrWelcomeMessage.Visibility = Visibility.Collapsed;    
        bdrHighscoreList.Visibility = Visibility.Visible; 
    }

    private void BtnAddToHighscoreList_OnClick(object sender, RoutedEventArgs e)
    {
        int newIndex = 0;
        // Where should the new entry be inserted?
        if((this.HighscoreList.Count > 0) && (currentScore < this.HighscoreList.Max(x => x.Score)))
        {
            SnakeHighscore justAbove = this.HighscoreList.OrderByDescending(x => x.Score).First(x => x.Score >= currentScore);
            if(justAbove != null)
                newIndex = this.HighscoreList.IndexOf(justAbove) + 1;
        }
        // Create & insert the new entry
        this.HighscoreList.Insert(newIndex, new SnakeHighscore()
        {
            PlayerName = txtPlayerName.Text,
            Score = currentScore
        });
        // Make sure that the amount of entries does not exceed the maximum
        while(this.HighscoreList.Count > MaxHighscoreListEntryCount)
            this.HighscoreList.RemoveAt(MaxHighscoreListEntryCount);

        SaveHighscoreList();

        bdrNewHighscore.Visibility = Visibility.Collapsed;
        bdrHighscoreList.Visibility = Visibility.Visible;
    }
}