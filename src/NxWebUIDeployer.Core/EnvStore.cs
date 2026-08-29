using System;
using System.Collections.Generic;

namespace NxWebUIDeployer
{
    public interface IEnvStore
    {
        Dictionary<string, string> Read(IEnumerable<string> names);
        void Write(Dictionary<string, string> updates);
    }

    public sealed class MemoryEnvStore : IEnvStore
    {
        public readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Read(IEnumerable<string> names)
        {
            var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                output[name] = Values.TryGetValue(name, out var value) ? value : null;
            }
            return output;
        }

        public void Write(Dictionary<string, string> updates)
        {
            foreach (var pair in updates)
            {
                if (string.IsNullOrEmpty(pair.Value)) Values.Remove(pair.Key);
                else Values[pair.Key] = pair.Value;
            }
        }
    }

    public sealed class WindowsUserEnvStore : IEnvStore
    {
        public Dictionary<string, string> Read(IEnumerable<string> names)
        {
            var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                output[name] = string.IsNullOrEmpty(value) ? null : value;
            }
            return output;
        }

        public void Write(Dictionary<string, string> updates)
        {
            foreach (var pair in updates)
            {
                Environment.SetEnvironmentVariable(
                    pair.Key,
                    string.IsNullOrEmpty(pair.Value) ? null : pair.Value,
                    EnvironmentVariableTarget.User);
            }
            Native.BroadcastSettingChange();
        }
    }

    static class Native
    {
        const int HwndBroadcast = 0xffff;
        const int WmSettingChange = 0x001A;
        const int SmtoAbortIfHung = 0x0002;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

        public static void BroadcastSettingChange()
        {
            SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero, "Environment",
                SmtoAbortIfHung, 2000, out _);
        }
    }
}
