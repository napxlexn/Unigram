//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Telegram.Host
{
    /// <summary>
    /// Keeps the process' kernel handle count from drifting up between garbage collections.
    ///
    /// Events, semaphores and the like behind WinRT and .NET wrappers are released only when the
    /// wrapper is finalized, and the managed heap here is small enough that a full collection
    /// can be minutes apart while the window keeps animating - a couple of handles a second pile
    /// up in the meantime, which is the "many handles" the process shows. Measured: a forced
    /// collection takes the count straight back to its baseline, so nothing is leaked, only late.
    ///
    /// The UWP host has GarbageCollectionMonitor for the same job, but that hooks the .NET Native
    /// runtime and does nothing on CoreCLR. This asks for a background collection only once the
    /// count has grown by <see cref="Threshold"/> since the last one, so an idle process with no
    /// growth is never touched.
    /// </summary>
    internal static partial class HandleCollector
    {
        private const uint Threshold = 300;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        private static Timer _timer;
        private static uint _baseline;

        public static void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _baseline = Count();
            _timer = new Timer(OnTick, null, Interval, Interval);
        }

        private static void OnTick(object state)
        {
            var count = Count();
            if (count < _baseline + Threshold)
            {
                // Handles freed by a collection the runtime ran on its own move the baseline
                // down, so the next threshold is measured from where things actually are.
                _baseline = Math.Min(_baseline, count);
                return;
            }

            // Background and non-blocking: the UI thread keeps running while it happens, and
            // the finalizer thread releases the handles once the wrappers are found dead.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);

            _baseline = count;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                GC.WaitForPendingFinalizers();
                _baseline = Count();
            });
        }

        private static uint Count()
        {
            return GetProcessHandleCount(GetCurrentProcess(), out var count) ? count : 0;
        }

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetProcessHandleCount(IntPtr process, out uint count);

        [LibraryImport("kernel32.dll")]
        private static partial IntPtr GetCurrentProcess();
    }
}
