using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiveSplit.GoLSplit
{
    class GameMemory
    {
        public delegate void LevelFinishedEventHandler(object sender, string level, uint time);
        public event LevelFinishedEventHandler OnLevelFinished;
        public event EventHandler OnFirstLevelStarted;
        public event EventHandler OnFirstLevelLoading;

        private Task _thread;
        private CancellationTokenSource _cancelSource;
        private SynchronizationContext _uiThread;

        private DeepPointer _currentMapPtr;
        private DeepPointer _gameTimePtr;
        private DeepPointer _isOnEndScreenPtr;

        private uint _prevGameTime;
        private bool _levelLoaded;

        private Dictionary<string, string> _zoneToLevel = new Dictionary<string, string> {
            { "alc_1_",                  "Temple of Light" },
            { "alc_2_",                  "Temple Grounds" },
            { "alc_3_",                  "Spider Tomb" },
            { "alc_bossfight_trex",      "The Summoning" },
            { "alc_4_",                  "Forgotten Gate" },
            { "alc_6_",                  "Toxic Swamp" },
            { "alc_5_lt_arrow_shrine",   "The Jaws of Death" }, // special case
            { "alc_5_",                  "Flooded Passage" },
            { "alc_10_",                 "Twisting Bridge" },
            { "alc_11_",                 "Fiery Depths" },
            { "alc_bossfight_lava_trex", "Belly of the Beast" },
            { "alc_13_",                 "Stronghold Passage" },
            { "alc_14_",                 "The Mirror's Wake" },
            { "alc_bossfight_xolotl",    "Xolotl's Stronghold" }
        };

        public uint GameTime
        {
            get { return _levelLoaded ? _prevGameTime : 0; }
        }

        public GameMemory()
        {
            _currentMapPtr = new DeepPointer(0xCA8E1C);
            _gameTimePtr = new DeepPointer(0xCA8EE4);
            _isOnEndScreenPtr = new DeepPointer(0x7C0DD0);
        }

        public void StartReading()
        {
            if (_thread != null && _thread.Status == TaskStatus.Running)
                throw new InvalidOperationException();
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                throw new InvalidOperationException("SynchronizationContext.Current is not a UI thread.");

            _uiThread = SynchronizationContext.Current;
            _cancelSource = new CancellationTokenSource();
            _thread = Task.Factory.StartNew(MemoryReadThread);
        }

        public void Stop()
        {
            if (_cancelSource == null || _thread == null || _thread.Status != TaskStatus.Running)
                return;

            _cancelSource.Cancel();
            _thread.Wait();
        }

        void MemoryReadThread()
        {
            while (!_cancelSource.IsCancellationRequested)
            {
                try
                {
                    Process gameProcess = null;

                    // wait for game process
                    while (gameProcess == null)
                    {
                        gameProcess = Process.GetProcesses()
                            .FirstOrDefault(p => p.ProcessName.ToLower() == "lcgol" && !p.HasExited);

                        if (gameProcess != null)
                            break;

                        Thread.Sleep(250);

                        if (_cancelSource.IsCancellationRequested)
                            return;
                    }

                    bool prevIsOnEndScreen = false;
                    while (!gameProcess.HasExited)
                    {
                        string currentMap;
                        _currentMapPtr.Deref(gameProcess, out currentMap, 128);

                        bool isOnEndScreen;
                        _isOnEndScreenPtr.Deref(gameProcess, out isOnEndScreen);

                        uint gameTime;
                        _gameTimePtr.Deref(gameProcess, out gameTime);

                        if (isOnEndScreen != prevIsOnEndScreen && isOnEndScreen)
                        {
                            _levelLoaded = false;

                            string level = currentMap;
                            foreach (string zone in _zoneToLevel.Keys)
                            {
                                if (level.StartsWith(zone))
                                    level = _zoneToLevel[zone];
                            }

                            _uiThread.Send(s => {
                                if (this.OnLevelFinished != null)
                                    this.OnLevelFinished(this, level, gameTime);
                            }, null);
                        }
                        else if (gameTime == 0 && _prevGameTime > 0 && currentMap == "alc_1_it_beginning")
                        {
                            _uiThread.Send(s => {
                                if (this.OnFirstLevelLoading != null)
                                    this.OnFirstLevelLoading(this, EventArgs.Empty);
                            }, null);
                        }
                        else if (_prevGameTime == 0 && gameTime > 0)
                        {
                            _levelLoaded = true;

                            if (currentMap == "alc_1_it_beginning")
                            {
                                _uiThread.Send(s => {
                                    if (this.OnFirstLevelStarted != null)
                                        this.OnFirstLevelStarted(this, EventArgs.Empty);
                                }, null);
                            }
                        }

                        _prevGameTime = gameTime;
                        prevIsOnEndScreen = isOnEndScreen;

                        Thread.Sleep(15);

                        if (_cancelSource.IsCancellationRequested)
                            return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                    Thread.Sleep(1000);
                }
            }
        }
    }

    class DeepPointer
    {
        private List<int> _offsets;
        private int _base;

        public DeepPointer(int base_, params int[] offsets)
        {
            _base = base_;
            _offsets = new List<int>();
            _offsets.Add(0); // deref base first
            _offsets.AddRange(offsets);
        }

        public bool Deref<T>(Process process, out T value) where T : struct
        {
            int offset = _offsets[_offsets.Count - 1];
            IntPtr ptr;
            if (!this.DerefOffsets(process, out ptr)
                || !ReadProcessValue(process, ptr + offset, out value))
            {
                value = default(T);
                return false;
            }

            return true;
        }

        public bool Deref(Process process, out Vector3f value)
        {
            int offset = _offsets[_offsets.Count - 1];
            IntPtr ptr;
            float x, y, z;
            if (!this.DerefOffsets(process, out ptr)
                || !ReadProcessValue(process, ptr + offset + 0, out x)
                || !ReadProcessValue(process, ptr + offset + 4, out y)
                || !ReadProcessValue(process, ptr + offset + 8, out z))
            {
                value = new Vector3f();
                return false;
            }

            value = new Vector3f(x, y, z);
            return true;
        }

        public bool Deref(Process process, out string str, int max)
        {
            var sb = new StringBuilder(max);

            IntPtr ptr;
            if (!this.DerefOffsets(process, out ptr)
                || !ReadProcessASCIIString(process, ptr, sb))
            {
                str = String.Empty;
                return false;
            }

            str = sb.ToString();
            return true;
        }

        bool DerefOffsets(Process process, out IntPtr ptr)
        {
            ptr = process.MainModule.BaseAddress + _base;
            for (int i = 0; i < _offsets.Count - 1; i++)
            {
                if (!ReadProcessPtr32(process, ptr + _offsets[i], out ptr)
                    || ptr == IntPtr.Zero)
                {
                    return false;
                }
            }

            return true;
        }

        static bool ReadProcessValue<T>(Process process, IntPtr addr, out T val) where T : struct
        {
            Type type = typeof(T);

            var bytes = new byte[Marshal.SizeOf(type)];

            int read;
            val = default(T);
            if (!SafeNativeMethods.ReadProcessMemory(process.Handle, addr, bytes, bytes.Length, out read) || read != bytes.Length)
                return false;

            if (type == typeof(int))
            {
                val = (T)(object)BitConverter.ToInt32(bytes, 0);
            }
            else if (type == typeof(uint))
            {
                val = (T)(object)BitConverter.ToUInt32(bytes, 0);
            }
            else if (type == typeof(float))
            {
                val = (T)(object)BitConverter.ToSingle(bytes, 0);
            }
            else if (type == typeof(byte))
            {
                val = (T)(object)bytes[0];
            }
            else if (type == typeof(bool))
            {
                val = (T)(object)BitConverter.ToBoolean(bytes, 0);
            }
            else
            {
                throw new Exception("Type not supported.");
            }

            return true;
        }

        static bool ReadProcessPtr32(Process process, IntPtr addr, out IntPtr val)
        {
            byte[] bytes = new byte[4];
            int read;
            val = IntPtr.Zero;
            if (!SafeNativeMethods.ReadProcessMemory(process.Handle, addr, bytes, bytes.Length, out read) || read != bytes.Length)
                return false;
            val = (IntPtr)BitConverter.ToInt32(bytes, 0);
            return true;
        }

        static bool ReadProcessASCIIString(Process process, IntPtr addr, StringBuilder sb)
        {
            byte[] bytes = new byte[sb.Capacity];
            int read;
            if (!SafeNativeMethods.ReadProcessMemory(process.Handle, addr, bytes, bytes.Length, out read) || read != bytes.Length)
                return false;
            sb.Append(Encoding.ASCII.GetString(bytes));

            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\0')
                {
                    sb.Remove(i, sb.Length - i);
                    break;
                }
            }

            return true;
        }
    }

    public class Vector3f
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public int IX { get { return (int)this.X; } }
        public int IY { get { return (int)this.Y; } }
        public int IZ { get { return (int)this.Z; } }

        public Vector3f() { }

        public Vector3f(float x, float y, float z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public float Distance(Vector3f other)
        {
            float result = (this.X - other.X) * (this.X - other.X) +
                (this.Y - other.Y) * (this.Y - other.Y) +
                (this.Z - other.Z) * (this.Z - other.Z);
            return (float)Math.Sqrt(result);
        }

        public float DistanceXY(Vector3f other)
        {
            float result = (this.X - other.X) * (this.X - other.X) +
                (this.Y - other.Y) * (this.Y - other.Y);
            return (float)Math.Sqrt(result);
        }

        public override string ToString()
        {
            return this.X + " " + this.Y + " " + this.Z;
        }
    }
}
