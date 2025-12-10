using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Ports;
using System.Text;

namespace WindKeyClient
{
    class Program
    {
        // --- Configuration ---
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        
        private const int WM_INPUT = 0x00FF;

        // --- Structs ---

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWMOUSE
        {
            public ushort usFlags;
            public uint ulButtons;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct RAWINPUT
        {
            [FieldOffset(0)]
            public RAWINPUTHEADER header;
            [FieldOffset(24)]
            public RAWMOUSE mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        // --- Fields ---

        private static LowLevelKeyboardProc _procKeyboard = HookCallbackKeyboard;
        private static LowLevelMouseProc _procMouse = HookCallbackMouse;
        private static IntPtr _hookIDKeyboard = IntPtr.Zero;
        private static IntPtr _hookIDMouse = IntPtr.Zero;
        
        private static SerialPort _serialPort;
        private static bool _bridgeEnabled = false;
        private static bool _ctrlDown = false;
        
        private static IntPtr _hwndMessage;
        private static WndProcDelegate _wndProc;

        private static IntPtr _hTransparentCursor = IntPtr.Zero;

        // --- Main ---

        static void Main(string[] args)
        {
            Console.WriteLine("=== WindKey Client (Raw Input + Hook) ===");
            Console.WriteLine("Commands:");
            Console.WriteLine("  [Ctrl + M] : Toggle Bridge Mode (ON/OFF)");
            Console.WriteLine("  [Ctrl + C] : Exit App");
            Console.WriteLine("----------------------------------------");

            // Prepare transparent cursor
            byte[] andPlane = new byte[128];
            for (int i = 0; i < 128; i++) andPlane[i] = 0xFF;
            byte[] xorPlane = new byte[128];
            _hTransparentCursor = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);

            // Ensure cursor is restored on exit
            AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreCursor();
            Console.CancelKeyPress += (s, e) => { RestoreCursor(); };

            if (!SetupSerial())
            {
                Console.WriteLine("Failed to setup serial port. Press any key to exit.");
                Console.ReadKey();
                return;
            }

            // Create Message Window for Raw Input
            CreateMessageWindow();
            RegisterRawInput(_hwndMessage);

            // Install Hooks
            _hookIDKeyboard = SetHook(_procKeyboard, WH_KEYBOARD_LL);
            _hookIDMouse = SetHook(_procMouse, WH_MOUSE_LL);

            Console.WriteLine("\nReady! Press 'Ctrl + M' to toggle iPad control.");
            
            ApplicationRun();

            RestoreCursor();
            UnhookWindowsHookEx(_hookIDKeyboard);
            UnhookWindowsHookEx(_hookIDMouse);
            if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close();
        }

        private static void HideCursorGlobal()
        {
            if (_hTransparentCursor != IntPtr.Zero)
            {
                SetSystemCursor(CopyIcon(_hTransparentCursor), OCR_NORMAL);
                SetSystemCursor(CopyIcon(_hTransparentCursor), OCR_IBEAM);
                SetSystemCursor(CopyIcon(_hTransparentCursor), OCR_HAND);
            }
        }

        private static void RestoreCursor()
        {
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        }

        private static bool SetupSerial()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                Console.WriteLine("No COM ports found.");
                return false;
            }

            Console.WriteLine("Available Ports:");
            for (int i = 0; i < ports.Length; i++)
            {
                Console.WriteLine(string.Format("{0}: {1}", i, ports[i]));
            }

            Console.Write("Select Port Index: ");
            string input = Console.ReadLine();
            int index = 0;
            int.TryParse(input, out index);
            
            if (index < 0 || index >= ports.Length) index = 0;
            
            try
            {
                _serialPort = new SerialPort(ports[index], 115200);
                _serialPort.Open();
                Console.WriteLine("Connected to " + ports[index]);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return false;
            }
        }

        // --- Hook Callbacks ---

        private static IntPtr HookCallbackKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                // Track Ctrl State
                if (vkCode == 0xA2 || vkCode == 0xA3 || vkCode == 0x11) // LCtrl, RCtrl, Ctrl
                {
                    _ctrlDown = isDown;
                }

                // Toggle Logic: Ctrl + M (VK_M = 0x4D)
                if (isDown && vkCode == 0x4D) 
                {
                    if (_ctrlDown) // Use tracked state
                    {
                        _bridgeEnabled = !_bridgeEnabled;
                        Console.WriteLine(_bridgeEnabled ? ">>> BRIDGE ON (iPad) <<<" : "<<< BRIDGE OFF (Windows) >>>");
                        
                        // Toggle Cursor Visibility
                        if (_bridgeEnabled)
                        {
                            HideCursorGlobal();
                        }
                        else
                        {
                            RestoreCursor();
                        }

                        return (IntPtr)1; 
                    }
                }

                if (_bridgeEnabled)
                {
                    int espCode = MapVkToEspCode(vkCode);
                    if (espCode != 0)
                    {
                        if (isDown) SendKey(espCode, "D");
                        else if (isUp) SendKey(espCode, "U");
                    }
                    return (IntPtr)1; // Block input
                }
            }
            return CallNextHookEx(_hookIDKeyboard, nCode, wParam, lParam);
        }

        private static IntPtr HookCallbackMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _bridgeEnabled)
            {
                // Block Windows mouse movement so it doesn't hit edges or click things
                if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    return (IntPtr)1; 
                }
                else if (wParam == (IntPtr)WM_LBUTTONDOWN) { SendCommand("M,CLICK,LEFT"); return (IntPtr)1; }
                else if (wParam == (IntPtr)WM_RBUTTONDOWN) { SendCommand("M,CLICK,RIGHT"); return (IntPtr)1; }
                else if (wParam == (IntPtr)WM_MBUTTONDOWN) { SendCommand("M,CLICK,MIDDLE"); return (IntPtr)1; }
                // Block UP events too to prevent Windows clicks
                else if (wParam == (IntPtr)WM_LBUTTONUP || wParam == (IntPtr)WM_RBUTTONUP || wParam == (IntPtr)WM_MBUTTONUP)
                {
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookIDMouse, nCode, wParam, lParam);
        }

        // --- Raw Input Logic ---

        private static void RegisterRawInput(IntPtr hwnd)
        {
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01; // Generic Desktop Controls
            rid[0].usUsage = 0x02;     // Mouse
            rid[0].dwFlags = 0x00000100; // RIDEV_INPUTSINK
            rid[0].hwndTarget = hwnd;

            if (!RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                Console.WriteLine("Failed to register Raw Input.");
            }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_INPUT && _bridgeEnabled)
            {
                uint dwSize = 0;
                GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));

                if (dwSize > 0)
                {
                    IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                    try
                    {
                        if (GetRawInputData(lParam, 0x10000003, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
                        {
                            RAWINPUT raw = (RAWINPUT)Marshal.PtrToStructure(buffer, typeof(RAWINPUT));
                            if (raw.header.dwType == 0) // RIM_TYPEMOUSE
                            {
                                int dx = raw.mouse.lLastX;
                                int dy = raw.mouse.lLastY;

                                if (dx != 0 || dy != 0)
                                {
                                    int sendDx = Math.Max(-127, Math.Min(127, dx));
                                    int sendDy = Math.Max(-127, Math.Min(127, dy));
                                    SendCommand(string.Format("M,MOVE,{0},{1}", sendDx, sendDy));
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static void CreateMessageWindow()
        {
            _wndProc = new WndProcDelegate(WndProc);
            
            WNDCLASSEX wndClass = new WNDCLASSEX();
            wndClass.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc);
            wndClass.hInstance = GetModuleHandle(null);
            wndClass.lpszClassName = "WindKeyMessageWindow";

            RegisterClassEx(ref wndClass);

            _hwndMessage = CreateWindowEx(0, "WindKeyMessageWindow", "WindKey", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        }

        // --- Helpers ---

        private static int MapVkToEspCode(int vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return vk; 
            if (vk >= 0x41 && vk <= 0x5A) return vk + 32; 
            if (vk >= 0x70 && vk <= 0x7B) return 194 + (vk - 0x70); 

            switch (vk)
            {
                case 0x08: return 178; // Backspace
                case 0x09: return 179; // Tab
                case 0x0D: return 176; // Enter
                case 0x1B: return 177; // Esc
                case 0x20: return 32;  // Space
                case 0x25: return 216; // Left
                case 0x26: return 218; // Up
                case 0x27: return 215; // Right
                case 0x28: return 217; // Down
                case 0xA0: return 129; // L Shift
                case 0xA1: return 133; // R Shift
                case 0xA2: return 128; // L Ctrl
                case 0xA3: return 132; // R Ctrl
                case 0xA4: return 130; // L Alt
                case 0xA5: return 134; // R Alt
                case 0x5B: return 131; // L GUI
                case 0x5C: return 135; // R GUI
                case 0xBA: return 59;  // ;
                case 0xBB: return 61;  // =
                case 0xBC: return 44;  // ,
                case 0xBD: return 45;  // -
                case 0xBE: return 46;  // .
                case 0xBF: return 47;  // /
                case 0xC0: return 96;  // `
                case 0xDB: return 91;  // [
                case 0xDC: return 92;  // \
                case 0xDD: return 93;  // ]
                case 0xDE: return 39;  // '
            }
            return 0; 
        }

        private static void SendKey(int code, string action)
        {
            SendCommand(string.Format("K,{0},{1}", action, code));
        }

        private static void SendCommand(string cmd)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try 
                { 
                    _serialPort.WriteLine(cmd); 
                } 
                catch (Exception ex) 
                {
                    Console.WriteLine("TX Error: " + ex.Message);
                }
            }
            else
            {
                Console.WriteLine("TX Fail: Port Closed");
            }
        }

        private static void ApplicationRun()
        {
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static IntPtr SetHook(Delegate proc, int hookId)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookId, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        // --- Win32 API Imports ---

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwc);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

        [DllImport("user32.dll")]
        private static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        private const uint OCR_NORMAL = 32512;
        private const uint OCR_IBEAM = 32513;
        private const uint OCR_HAND = 32649;
        private const uint SPI_SETCURSORS = 0x0057;
    }
}
