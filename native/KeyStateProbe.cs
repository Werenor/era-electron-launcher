using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class KeyStateProbe {
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint scan, uint kind, IntPtr layout);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
    static readonly int[] LetterScans = { 0x1e,0x30,0x2e,0x20,0x12,0x21,0x22,0x23,0x17,0x24,0x25,0x26,0x32,0x31,0x18,0x19,0x10,0x13,0x1f,0x14,0x16,0x2f,0x11,0x2d,0x15,0x2c };
    static int VirtualKey(string code, IntPtr layout) {
        if (code.Length == 4 && code.StartsWith("Key") && code[3] >= 'A' && code[3] <= 'Z') return (int)MapVirtualKeyEx((uint)LetterScans[code[3] - 'A'], 3, layout);
        if (code.Length == 6 && code.StartsWith("Digit") && code[5] >= '0' && code[5] <= '9') return (int)MapVirtualKeyEx((uint)(code[5] == '0' ? 0x0b : 0x02 + code[5] - '1'), 3, layout);
        switch (code) {
            case "ArrowLeft": return 0x25; case "ArrowUp": return 0x26; case "ArrowRight": return 0x27; case "ArrowDown": return 0x28;
            case "Space": return 0x20; case "Enter": return 0x0d; case "Escape": return 0x1b; case "Tab": return 0x09;
            case "Backspace": return 0x08; case "ShiftLeft": return 0xa0; case "ShiftRight": return 0xa1;
            default: throw new ArgumentException("Unsupported key code");
        }
    }
    static int Main(string[] args) {
        try {
            if (args.Length != 2) return 2;
            string[] keys = args[1].Split(','); if (keys.Length > 64) return 2;
            using (var parent = Process.GetProcessById(Int32.Parse(args[0]))) {
                IntPtr lifetime = parent.Handle; // Retain a handle to this process instance, not a recycled PID.
                string previous = null; long heartbeat = 0;
                while (!parent.HasExited) {
                    IntPtr layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
                    var held = new List<string>();
                    foreach (string code in keys) if ((GetAsyncKeyState(VirtualKey(code, layout)) & 0x8000) != 0) held.Add(code);
                    string value = String.Join(",", held.ToArray());
                    long now = (DateTime.UtcNow.Ticks - 621355968000000000L) / 10000;
                    if (value != previous || now - heartbeat >= 16) {
                        Console.WriteLine(now + "|" + value); Console.Out.Flush(); previous = value; heartbeat = now;
                    }
                    Thread.Sleep(8);
                }
            }
            return 0;
        } catch (System.IO.IOException) { return 0; } catch { return 1; }
    }
}
