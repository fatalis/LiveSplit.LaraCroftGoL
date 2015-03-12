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
        public delegate void LevelFinishedEventHandler(object sender, string level);
        public event LevelFinishedEventHandler OnLevelFinished;
        public event EventHandler OnFirstLevelStarted;
        public event EventHandler OnFirstLevelLoading;
        public event EventHandler OnLoadStart;
        public event EventHandler OnLoadFinish;
        public event EventHandler OnInvalidSettingsDetected;
        public event PersonalBestILDB.NewPersonalBestEventArgs OnNewILPersonalBest;

        private Task _thread;
        private CancellationTokenSource _cancelSource;
        private SynchronizationContext _uiThread;

        private DeepPointer _currentMapPtr;
        private DeepPointer _isOnEndScreenPtr;
        private DeepPointer _spLoadingPtr;
        private DeepPointer _mpLoadingPtr;
        private DeepPointer _mpLoading2Ptr;
        private DeepPointer _numPlayersPtr;
        private DeepPointer _gameTimePtr;
        private DeepPointer _refreshRatePtr;
        private DeepPointer _vsyncPtr;

        private PersonalBestILDB _pbDb;
        
        private const int D3DPRESENT_DONOTWAIT = 0x00000001;

        public GameMemory()
        {
            _currentMapPtr = new DeepPointer(0xCA8E1C);
            _isOnEndScreenPtr = new DeepPointer(0x7C0DD0);
            _spLoadingPtr = new DeepPointer(0xA84CAC);
            _mpLoadingPtr = new DeepPointer(0xCEB5F8);
            _mpLoading2Ptr = new DeepPointer(0xCA8D0B);
            _numPlayersPtr = new DeepPointer(0xD7F8EC, 0x10);
            _gameTimePtr = new DeepPointer(0xCA8EE4);
            _refreshRatePtr = new DeepPointer(0x884554, 0x228); // cdc::PCDeviceManager->D3DPRESENT_PARAMETERS
            _vsyncPtr = new DeepPointer(0x884554, 0x22C);

            _pbDb = new PersonalBestILDB();
            _pbDb.OnNewILPersonalBest += pbDb_OnNewILPersonalBest;
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
                    Process game = null;

                    // wait for game process
                    while (game == null)
                    {
                        game = Process.GetProcesses()
                            .FirstOrDefault(p => p.ProcessName.ToLower() == "lcgol" && !p.HasExited);

                        if (game != null)
                            break;

                        Thread.Sleep(250);

                        if (_cancelSource.IsCancellationRequested)
                            return;
                    }

                    bool prevIsOnEndScreen = false;
                    byte prevMpLoading = 0;
                    byte prevMpLoading2 = 0;
                    byte prevSpLoading = 0;
                    uint prevGameTime = 0;
                    bool first = true;
                    while (!game.HasExited)
                    {
                        string currentMap;
                        _currentMapPtr.Deref(game, out currentMap, 128);

                        bool isOnEndScreen;
                        _isOnEndScreenPtr.Deref(game, out isOnEndScreen);

                        byte numPlayers;
                        _numPlayersPtr.Deref(game, out numPlayers);

                        byte spLoading;
                        _spLoadingPtr.Deref(game, out spLoading);

                        byte mpLoading;
                        _mpLoadingPtr.Deref(game, out mpLoading);

                        byte mpLoading2;
                        _mpLoading2Ptr.Deref(game, out mpLoading2);

                        uint gameTime;
                        _gameTimePtr.Deref(game, out gameTime);

                        int refreshRate;
                        _refreshRatePtr.Deref(game, out refreshRate);

                        int vsyncPresentationInterval;
                        _vsyncPtr.Deref(game, out vsyncPresentationInterval);

                        _pbDb.Update(game);

                        if (first)
                        {
                            first = false;
                            goto next;
                        }

                        if (isOnEndScreen && !prevIsOnEndScreen)
                        {
                            _uiThread.Post(s => {
                                if (this.OnLevelFinished != null)
                                    this.OnLevelFinished(this, currentMap);
                            }, null);
                        }
                        else if ((numPlayers == 1 && spLoading == 1 && prevSpLoading != 1 && !isOnEndScreen)
                            || (numPlayers > 1 && mpLoading == 2 && prevMpLoading == 7) // new game
                            || (numPlayers > 1 && mpLoading2 == 1 && prevMpLoading2 == 0) // death
                            || (numPlayers > 1 && mpLoading == 2 && prevMpLoading == 1)) // change level
                        {
                            _uiThread.Post(s => {
                                if (this.OnLoadStart != null)
                                    this.OnLoadStart(this, EventArgs.Empty);
                            }, null);
                        }
                        else if ((numPlayers == 1 && spLoading != 1 && prevSpLoading == 1 && !isOnEndScreen)
                            || (numPlayers > 1 && mpLoading == 1 && prevMpLoading == 3))
                        {
                            _uiThread.Post(s => {
                                if (this.OnLoadFinish != null)
                                    this.OnLoadFinish(this, EventArgs.Empty);
                            }, null);
                        }
                        
                        if (gameTime == 0 && prevGameTime > 0 && currentMap == "alc_1_it_beginning")
                        {
                            _uiThread.Post(s => {
                                if (this.OnFirstLevelLoading != null)
                                    this.OnFirstLevelLoading(this, EventArgs.Empty);
                            }, null);
                        }
                        else if (gameTime > 0 && prevGameTime == 0 && currentMap == "alc_1_it_beginning")
                        {
                            _uiThread.Post(s => {
                                if (this.OnFirstLevelStarted != null)
                                    this.OnFirstLevelStarted(this, EventArgs.Empty);
                            }, null);
                        }

                        if (vsyncPresentationInterval != D3DPRESENT_DONOTWAIT ||
                            (refreshRate != 59 && refreshRate != 60))
                        {
                            if (gameTime > 0 && refreshRate != 0) // avoid false detection on game startup and zeroed memory on exit
                            {
                                _uiThread.Send(s => {
                                    if (this.OnInvalidSettingsDetected != null)
                                        this.OnInvalidSettingsDetected(this, EventArgs.Empty);
                                }, null);
                            }
                        }
                        
                    next:
                        prevIsOnEndScreen = isOnEndScreen;
                        prevSpLoading = spLoading;
                        prevMpLoading = mpLoading;
                        prevMpLoading2 = mpLoading2;
                        prevGameTime = gameTime;

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

        void pbDb_OnNewILPersonalBest(object sender, string level, TimeSpan time, TimeSpan oldTime)
        {
            if (this.OnNewILPersonalBest != null)
            {
                _uiThread.Post(s => {
                    if (this.OnNewILPersonalBest != null)
                        this.OnNewILPersonalBest(this, level, time, oldTime);
                }, null);
            }
        }
    }

    class DeepPointer
    {
        public string Name { get; set; }

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

    class PersonalBestILDB
    {
        public delegate void NewPersonalBestEventArgs(object sender, string level, TimeSpan time, TimeSpan oldTime);
        public event NewPersonalBestEventArgs OnNewILPersonalBest;

        private Dictionary<DeepPointer, uint> _db;

        private const int PR_TABLE_START = 0xD821A0;
        private const int STRUCT_SIZE = 0xF0;

        private string[] _levels = {
            "Temple Grounds",
            "Spider Tomb",
            "The Summoning",
            "Toxic Swamp",
            "Flooded Passage",
            "Temple of Light",
            "The Jaws of Death",
            "Forgotten Gate",
            "Twisting Bridge",
            "Belly of the Beast",
            "Xolotl's Stronghold",
            "The Mirror's Wake",
            "Fiery Depths",
            "Stronghold Passage"
        };

        public PersonalBestILDB()
        {
            _db = new Dictionary<DeepPointer, uint>();

            for (int i = 0; i < _levels.Length; i++)
            {
                int addr = PR_TABLE_START + (STRUCT_SIZE * i);
                var solo = new DeepPointer(addr) { Name= "Solo - " + _levels[i] };
                var coop = new DeepPointer(addr + 8) { Name = "Coop - " + _levels[i] };
                _db.Add(solo, 0);
                _db.Add(coop, 0);
            }
        }

        public void Update(Process game)
        {
            var keys = new List<DeepPointer>(_db.Keys);
            foreach (DeepPointer ptr in keys)
            {
                uint dbTime = _db[ptr];

                uint time;
                ptr.Deref(game, out time);

                if (time != dbTime)
                {
                    _db[ptr] = time;

                    if (time < dbTime && time > 0)
                    {
                        if (this.OnNewILPersonalBest != null)
                            this.OnNewILPersonalBest(this, ptr.Name, TimeSpan.FromMilliseconds(time), TimeSpan.FromMilliseconds(dbTime));
                    }
                }
            }
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
