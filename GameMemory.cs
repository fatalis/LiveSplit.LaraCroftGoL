using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiveSplit.ComponentUtil;

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
                        string currentMap = _currentMapPtr.DerefString(game, 128);
                        var isOnEndScreen = _isOnEndScreenPtr.Deref<bool>(game);
                        var numPlayers = _numPlayersPtr.Deref<byte>(game);
                        var spLoading = _spLoadingPtr.Deref<byte>(game);
                        var mpLoading = _mpLoadingPtr.Deref<byte>(game);
                        var mpLoading2 = _mpLoading2Ptr.Deref<byte>(game);
                        var gameTime = _gameTimePtr.Deref<uint>(game);
                        var refreshRate = _refreshRatePtr.Deref<int>(game);
                        var vsyncPresentationInterval = _vsyncPtr.Deref<int>(game);

                        _pbDb.Update(game);

                        if (first)
                        {
                            first = false;
                            goto next;
                        }

                        if (isOnEndScreen && !prevIsOnEndScreen)
                        {
                            _uiThread.Post(s => this.OnLevelFinished?.Invoke(this, currentMap), null);
                        }
                        else if ((numPlayers == 1 && spLoading == 1 && prevSpLoading != 1 && !isOnEndScreen)
                            || (numPlayers > 1 && mpLoading == 2 && prevMpLoading == 7) // new game
                            || (numPlayers > 1 && mpLoading2 == 1 && prevMpLoading2 == 0) // death
                            || (numPlayers > 1 && mpLoading == 2 && prevMpLoading == 1)) // change level
                        {
                            _uiThread.Post(s => this.OnLoadStart?.Invoke(this, EventArgs.Empty), null);
                        }
                        else if ((numPlayers == 1 && spLoading != 1 && prevSpLoading == 1 && !isOnEndScreen)
                            || (numPlayers > 1 && mpLoading == 1 && prevMpLoading == 3))
                        {
                            _uiThread.Post(s => this.OnLoadFinish?.Invoke(this, EventArgs.Empty), null);
                        }
                        
                        if (gameTime == 0 && prevGameTime > 0 && currentMap == "alc_1_it_beginning")
                        {
                            _uiThread.Post(s => this.OnFirstLevelLoading?.Invoke(this, EventArgs.Empty), null);
                        }
                        else if (gameTime > 0 && prevGameTime == 0 && currentMap == "alc_1_it_beginning")
                        {
                            _uiThread.Post(s => this.OnFirstLevelStarted?.Invoke(this, EventArgs.Empty), null);
                        }

                        if (vsyncPresentationInterval != D3DPRESENT_DONOTWAIT ||
                            (refreshRate != 59 && refreshRate != 60))
                        {
                            if (gameTime > 0 && refreshRate != 0) // avoid false detection on game startup and zeroed memory on exit
                            {
                                _uiThread.Send(s => { this.OnInvalidSettingsDetected?.Invoke(this, EventArgs.Empty); }, null);
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
                _uiThread.Post(s => this.OnNewILPersonalBest?.Invoke(this, level, time, oldTime), null);
            }
        }
    }

    class PersonalBestILDB
    {
        public delegate void NewPersonalBestEventArgs(object sender, string level, TimeSpan time, TimeSpan oldTime);
        public event NewPersonalBestEventArgs OnNewILPersonalBest;

        private Dictionary<NamedDeepPointer, uint> _db;

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
            _db = new Dictionary<NamedDeepPointer, uint>();

            for (int i = 0; i < _levels.Length; i++)
            {
                int addr = PR_TABLE_START + (STRUCT_SIZE * i);
                var solo = new NamedDeepPointer(addr) { Name= "Solo - " + _levels[i] };
                var coop = new NamedDeepPointer(addr + 8) { Name = "Coop - " + _levels[i] };
                _db.Add(solo, 0);
                _db.Add(coop, 0);
            }
        }

        public void Update(Process game)
        {
            var keys = new List<NamedDeepPointer>(_db.Keys);
            foreach (NamedDeepPointer ptr in keys)
            {
                uint dbTime = _db[ptr];

                uint time;
                ptr.Deref(game, out time);

                if (time != dbTime)
                {
                    _db[ptr] = time;

                    if (time < dbTime && time > 0)
                    {
                        this.OnNewILPersonalBest?.Invoke(this, ptr.Name, TimeSpan.FromMilliseconds(time), TimeSpan.FromMilliseconds(dbTime));
                    }
                }
            }
        }

        class NamedDeepPointer : DeepPointer
        {
            public string Name { get; set; }
            public NamedDeepPointer(int base_, params int[] offsets) : base(base_, offsets) { }
            //public NamedDeepPointer(string module, int base_, params int[] offsets) : base(module, base_, offsets) { }
        }
    }

    public class Vector3f
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public int IX => (int)this.X;
        public int IY => (int)this.Y;
        public int IZ => (int)this.Z;

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
