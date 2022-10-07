using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AutopilotQuick.KeyboardTester;

public partial class KeyboardWindow : Window
{
    public KeyboardWindow()
        {
            InitializeComponent();
        }
        private void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
        {
            var keyControl = GetKeyControl(e);
            if (keyControl == null)
                return;

            keyControl.ViewModel.Pressed = true;
            keyControl.ViewModel.PreviouslyPressed = true;

            e.Handled = true;
        }
        
        private void MainWindow_OnKeyUp(object sender, KeyEventArgs e)
        {
            var keyControl = GetKeyControl(e);
            if (keyControl == null)
                return;

            keyControl.ViewModel.Pressed = false;

            if (keyControl == Snapshot)
                Snapshot.ViewModel.PreviouslyPressed = true;

            e.Handled = true;
        }

        private KeyboardKey GetKeyControl(KeyEventArgs keyEvent)
        {
            if (keyEvent.Key == Key.LeftCtrl && IsModifier(ModifierKeys.Control) && IsNotModifier(ModifierKeys.Alt))
            {
                if (keyEvent.IsRepeat)
                    return null;
                return LeftCtrl;
            }

            if (keyEvent.Key == Key.Enter)
            {
                if (ReadIsExteneded(keyEvent))
                    return NumPadEnter;
                return Return;
            }

            if (keyEvent.Key == Key.System)
            {
                if (keyEvent.SystemKey == Key.F10)
                    return F10;
                if (keyEvent.SystemKey == Key.F11)
                    return F11;
                if (keyEvent.SystemKey == Key.F12)
                    return F12;
                if (keyEvent.SystemKey == Key.LeftAlt)
                {
                    return LeftAlt;
                }
                if (keyEvent.SystemKey == Key.RightAlt)
                    return RightAlt;
            }

            var keyControl = GetKeyControlNonStandard(keyEvent);

            if (keyControl == null)
                keyControl = FindName(keyEvent.Key.ToString()) as KeyboardKey;

            return keyControl;
        }

        private static bool ReadIsExteneded(KeyEventArgs keyEvent)
        {
            return (bool)typeof(KeyEventArgs).InvokeMember("IsExtendedKey", BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.Instance, null, keyEvent, null); 
        }

        private static Key ReadRealKey(KeyEventArgs keyEvent)
        {
            return (Key)typeof(KeyEventArgs).InvokeMember("RealKey", BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.Instance, null, keyEvent, null);
        }

        private static bool IsModifier(ModifierKeys key)
        {
            return (Keyboard.Modifiers & key) == key;
        }

        private static bool IsNotModifier(ModifierKeys key)
        {
            return (Keyboard.Modifiers & key) != key;
        }

        private KeyboardKey GetKeyControlNonStandard(KeyEventArgs keyEvent)
        {
            switch (keyEvent.Key)
            {
                case Key.Oem1:
                    return OemSemicolon;
                case Key.Oem4:
                    return OemOpenBrackets;
                case Key.Oem5:
                    return OemPipe;
                case Key.Oem6:
                    return OemCloseBrackets;
                case Key.Oem7:
                    return OemQuotes;
                default:
                    return null;
            }
        }
}