// AutoClicker - Windows auto clicker (left/right mouse button)
// Toggle with the SPACE bar (global hook): press once to start the loop,
// press again to stop. Written in C# 5 so it compiles with the csc.exe that
// ships with .NET Framework 4.x on every Windows install.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int VK_SPACE = 0x20;
        public const uint LLKHF_INJECTED = 0x10;

        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // INPUT union: MOUSEINPUT is the largest member, so this layout is
        // correct on both 32-bit and 64-bit.
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uPeriod);
    }

    public class MainForm : Form
    {
        private readonly RadioButton rbLeft;
        private readonly RadioButton rbRight;
        private readonly RadioButton rbBoth;
        private readonly RadioButton rbSimul;
        private readonly NumericUpDown numInterval;
        private readonly CheckBox chkDouble;
        private readonly CheckBox chkBlockSpace;
        private readonly Label lblStatus;
        private readonly Label lblCount;
        private readonly Button btnToggle;

        private Native.LowLevelKeyboardProc hookProc; // keep a reference so the GC doesn't collect it
        private IntPtr hookHandle = IntPtr.Zero;
        private bool spaceHeld; // ignore key auto-repeat while space is held down

        private Thread worker;
        private volatile bool running;
        private long clickCount;

        // Settings copied for the worker thread
        private volatile int intervalMs;
        private volatile int buttonMode; // 0 = left, 1 = right, 2 = alternate, 3 = both each cycle
        private volatile bool doubleClick;

        public MainForm()
        {
            Text = "Auto Clicker";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 300);
            Font = new Font("Arial", 9F);
            TopMost = true;

            GroupBox grpButton = new GroupBox();
            grpButton.Text = "Botão do mouse";
            grpButton.SetBounds(12, 10, 376, 50);
            rbLeft = new RadioButton();
            rbLeft.Text = "Esquerdo";
            rbLeft.Checked = true;
            rbLeft.SetBounds(10, 20, 85, 22);
            rbRight = new RadioButton();
            rbRight.Text = "Direito";
            rbRight.SetBounds(95, 20, 75, 22);
            rbBoth = new RadioButton();
            rbBoth.Text = "Alternar";
            rbBoth.SetBounds(170, 20, 85, 22);
            rbSimul = new RadioButton();
            rbSimul.Text = "Ambos";
            rbSimul.SetBounds(260, 20, 100, 22);
            grpButton.Controls.Add(rbLeft);
            grpButton.Controls.Add(rbRight);
            grpButton.Controls.Add(rbBoth);
            grpButton.Controls.Add(rbSimul);

            Label lblInterval = new Label();
            lblInterval.Text = "Intervalo entre cliques (ms):";
            lblInterval.SetBounds(12, 74, 180, 20);
            numInterval = new NumericUpDown();
            numInterval.Minimum = 1;
            numInterval.Maximum = 600000;
            numInterval.Value = 100;
            numInterval.SetBounds(280, 72, 108, 22);

            chkDouble = new CheckBox();
            chkDouble.Text = "Clique duplo";
            chkDouble.SetBounds(12, 102, 376, 22);

            chkBlockSpace = new CheckBox();
            chkBlockSpace.Text = "Não repassar a barra de espaço a outros programas";
            chkBlockSpace.SetBounds(12, 126, 376, 22);

            btnToggle = new Button();
            btnToggle.Text = "Iniciar (Espaço)";
            btnToggle.SetBounds(12, 160, 376, 40);
            btnToggle.Click += delegate { Toggle(); };
            // Avoid the button reacting to the space bar itself (would double-toggle)
            btnToggle.TabStop = false;

            lblStatus = new Label();
            lblStatus.Text = "PARADO";
            lblStatus.Font = new Font("Arial", 14F, FontStyle.Bold);
            lblStatus.ForeColor = Color.Firebrick;
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.SetBounds(12, 208, 376, 30);

            lblCount = new Label();
            lblCount.Text = "Cliques: 0";
            lblCount.TextAlign = ContentAlignment.MiddleCenter;
            lblCount.SetBounds(12, 240, 376, 20);

            Label lblHelp = new Label();
            lblHelp.Text = "Pressione ESPAÇO para iniciar / parar o loop.";
            lblHelp.ForeColor = Color.DimGray;
            lblHelp.TextAlign = ContentAlignment.MiddleCenter;
            lblHelp.SetBounds(12, 266, 376, 20);

            Controls.Add(grpButton);
            Controls.Add(lblInterval);
            Controls.Add(numInterval);
            Controls.Add(chkDouble);
            Controls.Add(chkBlockSpace);
            Controls.Add(btnToggle);
            Controls.Add(lblStatus);
            Controls.Add(lblCount);
            Controls.Add(lblHelp);

            System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 200;
            uiTimer.Tick += delegate { lblCount.Text = "Cliques: " + Interlocked.Read(ref clickCount); };
            uiTimer.Start();

            Load += delegate { InstallHook(); };
            FormClosing += delegate { StopClicking(); RemoveHook(); };
        }

        // Keep the focused control (checkbox/radio) from reacting to SPACE;
        // the global hook already handles it.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void InstallHook()
        {
            hookProc = HookCallback;
            using (Process proc = Process.GetCurrentProcess())
            using (ProcessModule mod = proc.MainModule)
            {
                hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc,
                    Native.GetModuleHandle(mod.ModuleName), 0);
            }
            if (hookHandle == IntPtr.Zero)
            {
                MessageBox.Show("Não foi possível registrar o atalho global da barra de espaço.\n" +
                                "Use o botão Iniciar/Parar.", "Auto Clicker",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RemoveHook()
        {
            if (hookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.KBDLLHOOKSTRUCT kb = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool injected = (kb.flags & Native.LLKHF_INJECTED) != 0;
                if (kb.vkCode == Native.VK_SPACE && !injected)
                {
                    int msg = wParam.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        if (!spaceHeld)
                        {
                            spaceHeld = true;
                            BeginInvoke(new MethodInvoker(Toggle));
                        }
                    }
                    else if (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP)
                    {
                        spaceHeld = false;
                    }

                    if (chkBlockSpace.Checked)
                        return (IntPtr)1; // swallow the key
                }
            }
            return Native.CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        private void Toggle()
        {
            if (running) StopClicking();
            else StartClicking();
        }

        private void StartClicking()
        {
            if (running) return;
            intervalMs = (int)numInterval.Value;
            buttonMode = rbLeft.Checked ? 0 : rbRight.Checked ? 1 : rbBoth.Checked ? 2 : 3;
            doubleClick = chkDouble.Checked;
            Interlocked.Exchange(ref clickCount, 0);

            running = true;
            worker = new Thread(ClickLoop);
            worker.IsBackground = true;
            worker.Start();

            lblStatus.Text = "RODANDO";
            lblStatus.ForeColor = Color.ForestGreen;
            btnToggle.Text = "Parar (Espaço)";
            SetOptionsEnabled(false);
        }

        private void StopClicking()
        {
            if (!running) return;
            running = false;
            if (worker != null)
            {
                worker.Join(1000);
                worker = null;
            }
            lblStatus.Text = "PARADO";
            lblStatus.ForeColor = Color.Firebrick;
            btnToggle.Text = "Iniciar (Espaço)";
            SetOptionsEnabled(true);
        }

        private void SetOptionsEnabled(bool enabled)
        {
            rbLeft.Enabled = enabled;
            rbRight.Enabled = enabled;
            rbBoth.Enabled = enabled;
            rbSimul.Enabled = enabled;
            numInterval.Enabled = enabled;
            chkDouble.Enabled = enabled;
        }

        private void ClickLoop()
        {
            Native.timeBeginPeriod(1); // finer Sleep() resolution for short intervals
            try
            {
                bool nextRight = false;
                Stopwatch sw = Stopwatch.StartNew();
                long nextTick = 0;
                while (running)
                {
                    if (buttonMode == 3)
                    {
                        // Both buttons in the same cycle: left, then right
                        SendClick(false);
                        if (doubleClick) SendClick(false);
                        SendClick(true);
                        if (doubleClick) SendClick(true);
                    }
                    else
                    {
                        bool right = buttonMode == 1 || (buttonMode == 2 && nextRight);
                        nextRight = !nextRight;
                        SendClick(right);
                        if (doubleClick) SendClick(right);
                    }
                    Interlocked.Increment(ref clickCount);

                    // Fixed-rate scheduling; sleep in small slices so stop is responsive
                    nextTick += intervalMs;
                    while (running)
                    {
                        long remaining = nextTick - sw.ElapsedMilliseconds;
                        if (remaining <= 0) break;
                        Thread.Sleep((int)Math.Min(remaining, 50));
                    }
                    if (sw.ElapsedMilliseconds - nextTick > 1000) nextTick = sw.ElapsedMilliseconds; // resync after lag
                }
            }
            finally
            {
                Native.timeEndPeriod(1);
            }
        }

        private static void SendClick(bool right)
        {
            Native.INPUT[] inputs = new Native.INPUT[2];
            inputs[0].type = Native.INPUT_MOUSE;
            inputs[0].mi.dwFlags = right ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_LEFTDOWN;
            inputs[1].type = Native.INPUT_MOUSE;
            inputs[1].mi.dwFlags = right ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_LEFTUP;
            Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
