using System;
using System.Windows;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Interop;
using System.IO;
using System.Windows.Input;
using System.Text;

namespace Topmost
{
    public partial class MainWindow : Window
    {
        public Version version = new Version("1.0");

        private const int WM_HOTKEY = 0x0312;
        private const long WS_EX_TOPMOST = 0x00000008L;
        private const int GWL_EXSTYLE = -20;
        private const int SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern long GetWindowLongA(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern uint GetWindowModuleFileNameA(IntPtr hWnd, StringBuilder pszFileName, uint cchFileNameMax);
        [DllImport("user32.dll")]
        private static extern long RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern long UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("Kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("Kernel32.dll")]
        private static extern int GetLastError();

        private readonly string saveDir = @"C:\Users\" + Environment.UserName + @"\AppData\Roaming\Topmost";
        private readonly string saveFile;

        private System.Windows.Forms.NotifyIcon icon;

        private readonly IntPtr ownHWnd;
        private const int hotkeyId = 0x420911;
        private readonly uint tid = GetCurrentThreadId();

        private Thread setKeyThread;
        private bool setKey = false;
        private uint newKey;
        private uint newMod;

        private uint key = (uint)VirtualKey.Space;
        private uint mod = (uint)ModifierKey.CTRL | (uint)ModifierKey.ALT;

        private Thread checkStateThread;
        private bool checkState = false;

        public MainWindow()
        {
            InitializeComponent();

            saveFile = Path.Combine(saveDir, "key.txt");

            if (!Directory.Exists(saveDir))
                Directory.CreateDirectory(saveDir);

            load();

            icon = new System.Windows.Forms.NotifyIcon();
            icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            icon.Text = "Topmost";

            System.Windows.Forms.MenuItem exit = new System.Windows.Forms.MenuItem();
            exit.Text = "Exit";
            exit.Click += cmExit_Click;
            icon.ContextMenu = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[] { exit });

            icon.Visible = true;

            icon.DoubleClick += Icon_DoubleClick;
            Visibility = Visibility.Hidden;

            ownHWnd = new WindowInteropHelper(this).EnsureHandle();
            HwndSource source = HwndSource.FromHwnd(ownHWnd);
            source.AddHook(HwndHook);

            RegisterHotKey(ownHWnd, hotkeyId, mod, key);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && (int)wParam == hotkeyId)
            {
                uint mod = (uint)lParam & 0xFFFF;
                uint key = ((uint)lParam >> 16) & 0xFFFF;

                if (mod == this.mod && key == this.key)
                {
                    IntPtr target = GetForegroundWindow();
                    long state = GetWindowLongA(target, GWL_EXSTYLE);
                    if ((state & WS_EX_TOPMOST) != 0)
                        SetWindowPos(target, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    else
                        SetWindowPos(target, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void load()
        {
            if (!File.Exists(saveFile))
            {
                key = (uint)VirtualKey.Space;
                mod = (uint)ModifierKey.CTRL | (uint)ModifierKey.ALT;
                save();
                updateUI();
                return;
            }

            using (StreamReader f = new StreamReader(saveFile))
            {
                try
                {
                    mod = Convert.ToUInt32(f.ReadLine());
                    key = Convert.ToUInt32(f.ReadLine());
                }
                catch
                {
                    File.Delete(saveFile);
                    load();
                }
            }
            updateUI();
        }

        private void save()
        {
            using (StreamWriter f = new StreamWriter(saveFile, false))
            {
                f.WriteLine(mod);
                f.WriteLine(key);
            }
        }

        private void updateUI()
        {
            Dispatcher.Invoke(() =>
            {
                string keyText = "";
                uint keyBeingDisplayed;
                uint modBeingDisplayed;
                if (setKey)
                {
                    btKey.Background = Brushes.LightGoldenrodYellow;
                    keyBeingDisplayed = newKey;
                    modBeingDisplayed = newMod;
                }
                else
                {
                    btKey.ClearValue(BackgroundProperty);
                    keyBeingDisplayed = key;
                    modBeingDisplayed = mod;
                }

                if ((modBeingDisplayed & (uint)ModifierKey.CTRL) > 0)
                {
                    keyText += "CTRL+";
                }
                if ((modBeingDisplayed & (uint)ModifierKey.ALT) > 0)
                {
                    keyText += "ALT+";
                }
                if ((modBeingDisplayed & (uint)ModifierKey.SHIFT) > 0)
                {
                    keyText += "SHIFT+";
                }
                if ((modBeingDisplayed & (uint)ModifierKey.WIN) > 0)
                {
                    keyText += "WIN+";
                }

                if (keyBeingDisplayed != 0)
                    keyText += ((VirtualKey)keyBeingDisplayed).ToString();

                btKey.Content = keyText;

                if (checkState)
                {
                    btCheck.Background = Brushes.LightGoldenrodYellow;
                    btCheck.Content = "Select Window";
                }
                else
                {
                    btCheck.ClearValue(BackgroundProperty);
                    btCheck.Content = "Check State";
                }
            });
        }

        private void Icon_DoubleClick(object sender, EventArgs e)
        {
            if (Visibility == Visibility.Visible)
            {
                Visibility = Visibility.Hidden;
            }
            else
            {
                Visibility = Visibility.Visible;
            }
        }

        private void cmExit_Click(object sender, EventArgs e)
        {
            btExit_Click(sender, null);
        }

        private void btAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(string.Format("Topmost version {0}\nBenjamin Goisser 2022\nhttps://github.com/Begus001", version), "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btExit_Click(object sender, RoutedEventArgs e)
        {
            setKey = false;
            icon.Visible = false;
            Application.Current.Shutdown(0);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            setKey = false;
            Visibility = Visibility.Hidden;
        }

        private void btKey_Click(object sender, RoutedEventArgs e)
        {
            if (setKey)
            {
                setKey = false;
                updateUI();
                return;
            }
            if (checkState)
                return;

            UnregisterHotKey(ownHWnd, hotkeyId);
            setKey = true;
            newKey = 0;
            newMod = 0;
            updateUI();
            setKeyThread = new Thread(() =>
            {
                AttachThreadInput(GetCurrentThreadId(), tid, true);
                while (setKey && !checkState)
                {
                    var keys = new byte[256];
                    GetKeyboardState(keys);

                    if (keys[(uint)VirtualKey.Escape] >> 7 != 0)
                    {
                        setKey = false;
                        break;
                    }

                    if (keys[(uint)VirtualKey.Control] >> 7 != 0 || keys[(uint)VirtualKey.LeftControl] >> 7 != 0 || keys[(uint)VirtualKey.RightControl] >> 7 != 0)
                        newMod |= (uint)ModifierKey.CTRL;
                    else
                        newMod &= ~(uint)ModifierKey.CTRL;

                    if (keys[(uint)VirtualKey.Shift] >> 7 != 0 || keys[(uint)VirtualKey.LeftShift] >> 7 != 0 || keys[(uint)VirtualKey.RightShift] >> 7 != 0)
                        newMod |= (uint)ModifierKey.SHIFT;
                    else
                        newMod &= ~(uint)ModifierKey.SHIFT;

                    if (keys[(uint)VirtualKey.Menu] >> 7 != 0 || keys[(uint)VirtualKey.LeftMenu] >> 7 != 0 || keys[(uint)VirtualKey.RightMenu] >> 7 != 0)
                        newMod |= (uint)ModifierKey.ALT;
                    else
                        newMod &= ~(uint)ModifierKey.ALT;

                    if (keys[(uint)VirtualKey.LeftWindows] >> 7 != 0 || keys[(uint)VirtualKey.RightWindows] >> 7 != 0)
                        newMod |= (uint)ModifierKey.WIN;
                    else
                        newMod &= ~(uint)ModifierKey.WIN;

                    for (uint i = 0; i < 256; i++)
                    {
                        switch ((VirtualKey)i)
                        {
                            case VirtualKey.Control:
                            case VirtualKey.LeftControl:
                            case VirtualKey.RightControl:
                            case VirtualKey.Shift:
                            case VirtualKey.LeftShift:
                            case VirtualKey.RightShift:
                            case VirtualKey.Menu:
                            case VirtualKey.LeftMenu:
                            case VirtualKey.RightMenu:
                            case VirtualKey.LeftWindows:
                            case VirtualKey.RightWindows:
                                continue;
                        }
                        if (keys[i] >> 7 != 0)
                        {
                            newKey = i;
                            key = newKey;
                            mod = newMod;
                            setKey = false;
                        }
                    }
                    updateUI();
                    Thread.Sleep(10);
                }
                Dispatcher.Invoke(() =>
                {
                    RegisterHotKey(ownHWnd, hotkeyId, mod, key);
                    save();
                });
                updateUI();
            });
            setKeyThread.Start();
        }

        private void btCheck_Click(object sender, RoutedEventArgs e)
        {
            if (checkState)
            {
                checkState = false;
                updateUI();
                return;
            }

            checkState = true;
            updateUI();
            checkStateThread = new Thread(() =>
            {
                IntPtr target = IntPtr.Zero;
                while (checkState)
                {
                    target = GetForegroundWindow();
                    if (target != ownHWnd && target != IntPtr.Zero)
                    {
                        checkState = false;
                        updateUI();
                        long state = GetWindowLongA(target, GWL_EXSTYLE);
                        if ((state & WS_EX_TOPMOST) != 0)
                        {
                            Dispatcher.Invoke(() => MessageBox.Show("The selected window is always on top", "State", MessageBoxButton.OK, MessageBoxImage.Information));
                        }
                        else
                        {
                            Dispatcher.Invoke(() => MessageBox.Show("The selected window is NOT always on top", "State", MessageBoxButton.OK, MessageBoxImage.Information));
                        }
                    }
                    Thread.Sleep(10);
                }
                updateUI();
            });
            checkStateThread.Start();
        }

        public enum ModifierKey : uint
        {
            ALT = 0x0001,
            CTRL = 0x0002,
            SHIFT = 0x0004,
            WIN = 0x0008
        }

        public enum VirtualKey : uint
        {
            LeftButton = 0x01,
            RightButton = 0x02,
            Cancel = 0x03,
            MiddleButton = 0x04,
            ExtraButton1 = 0x05,
            ExtraButton2 = 0x06,
            Back = 0x08,
            Tab = 0x09,
            Clear = 0x0C,
            Return = 0x0D,
            Shift = 0x10,
            Control = 0x11,
            Menu = 0x12,
            Pause = 0x13,
            CapsLock = 0x14,
            Kana = 0x15,
            Hangeul = 0x15,
            Hangul = 0x15,
            Junja = 0x17,
            Final = 0x18,
            Hanja = 0x19,
            Kanji = 0x19,
            Escape = 0x1B,
            Convert = 0x1C,
            NonConvert = 0x1D,
            Accept = 0x1E,
            ModeChange = 0x1F,
            Space = 0x20,
            Prior = 0x21,
            Next = 0x22,
            End = 0x23,
            Home = 0x24,
            Left = 0x25,
            Up = 0x26,
            Right = 0x27,
            Down = 0x28,
            Select = 0x29,
            Print = 0x2A,
            Execute = 0x2B,
            Snapshot = 0x2C,
            Insert = 0x2D,
            Delete = 0x2E,
            Help = 0x2F,
            N0 = 0x30,
            N1 = 0x31,
            N2 = 0x32,
            N3 = 0x33,
            N4 = 0x34,
            N5 = 0x35,
            N6 = 0x36,
            N7 = 0x37,
            N8 = 0x38,
            N9 = 0x39,
            A = 0x41,
            B = 0x42,
            C = 0x43,
            D = 0x44,
            E = 0x45,
            F = 0x46,
            G = 0x47,
            H = 0x48,
            I = 0x49,
            J = 0x4A,
            K = 0x4B,
            L = 0x4C,
            M = 0x4D,
            N = 0x4E,
            O = 0x4F,
            P = 0x50,
            Q = 0x51,
            R = 0x52,
            S = 0x53,
            T = 0x54,
            U = 0x55,
            V = 0x56,
            W = 0x57,
            X = 0x58,
            Y = 0x59,
            Z = 0x5A,
            LeftWindows = 0x5B,
            RightWindows = 0x5C,
            Application = 0x5D,
            Sleep = 0x5F,
            Numpad0 = 0x60,
            Numpad1 = 0x61,
            Numpad2 = 0x62,
            Numpad3 = 0x63,
            Numpad4 = 0x64,
            Numpad5 = 0x65,
            Numpad6 = 0x66,
            Numpad7 = 0x67,
            Numpad8 = 0x68,
            Numpad9 = 0x69,
            Multiply = 0x6A,
            Add = 0x6B,
            Separator = 0x6C,
            Subtract = 0x6D,
            Decimal = 0x6E,
            Divide = 0x6F,
            F1 = 0x70,
            F2 = 0x71,
            F3 = 0x72,
            F4 = 0x73,
            F5 = 0x74,
            F6 = 0x75,
            F7 = 0x76,
            F8 = 0x77,
            F9 = 0x78,
            F10 = 0x79,
            F11 = 0x7A,
            F12 = 0x7B,
            F13 = 0x7C,
            F14 = 0x7D,
            F15 = 0x7E,
            F16 = 0x7F,
            F17 = 0x80,
            F18 = 0x81,
            F19 = 0x82,
            F20 = 0x83,
            F21 = 0x84,
            F22 = 0x85,
            F23 = 0x86,
            F24 = 0x87,
            NumLock = 0x90,
            ScrollLock = 0x91,
            NEC_Equal = 0x92,
            Fujitsu_Jisho = 0x92,
            Fujitsu_Masshou = 0x93,
            Fujitsu_Touroku = 0x94,
            Fujitsu_Loya = 0x95,
            Fujitsu_Roya = 0x96,
            LeftShift = 0xA0,
            RightShift = 0xA1,
            LeftControl = 0xA2,
            RightControl = 0xA3,
            LeftMenu = 0xA4,
            RightMenu = 0xA5,
            BrowserBack = 0xA6,
            BrowserForward = 0xA7,
            BrowserRefresh = 0xA8,
            BrowserStop = 0xA9,
            BrowserSearch = 0xAA,
            BrowserFavorites = 0xAB,
            BrowserHome = 0xAC,
            VolumeMute = 0xAD,
            VolumeDown = 0xAE,
            VolumeUp = 0xAF,
            MediaNextTrack = 0xB0,
            MediaPrevTrack = 0xB1,
            MediaStop = 0xB2,
            MediaPlayPause = 0xB3,
            LaunchMail = 0xB4,
            LaunchMediaSelect = 0xB5,
            LaunchApplication1 = 0xB6,
            LaunchApplication2 = 0xB7,
            OEM1 = 0xBA,
            OEMPlus = 0xBB,
            OEMComma = 0xBC,
            OEMMinus = 0xBD,
            OEMPeriod = 0xBE,
            OEM2 = 0xBF,
            OEM3 = 0xC0,
            OEM4 = 0xDB,
            OEM5 = 0xDC,
            OEM6 = 0xDD,
            OEM7 = 0xDE,
            OEM8 = 0xDF,
            OEMAX = 0xE1,
            OEM102 = 0xE2,
            ICOHelp = 0xE3,
            ICO00 = 0xE4,
            ProcessKey = 0xE5,
            ICOClear = 0xE6,
            Packet = 0xE7,
            OEMReset = 0xE9,
            OEMJump = 0xEA,
            OEMPA1 = 0xEB,
            OEMPA2 = 0xEC,
            OEMPA3 = 0xED,
            OEMWSCtrl = 0xEE,
            OEMCUSel = 0xEF,
            OEMATTN = 0xF0,
            OEMFinish = 0xF1,
            OEMCopy = 0xF2,
            OEMAuto = 0xF3,
            OEMENLW = 0xF4,
            OEMBackTab = 0xF5,
            ATTN = 0xF6,
            CRSel = 0xF7,
            EXSel = 0xF8,
            EREOF = 0xF9,
            Play = 0xFA,
            Zoom = 0xFB,
            Noname = 0xFC,
            PA1 = 0xFD,
            OEMClear = 0xFE
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                checkState = false;

            updateUI();
        }
    }
}
