using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using LiveSplit.ComponentUtil;

namespace LiveSplit.GoLSplit
{
    class GameData : MemoryWatcherList
    {
        public StringWatcher       CurrentMap                { get; } = new StringWatcher      (new DeepPointer(0xCA8E1C), 128);
        public MemoryWatcher<bool> IsOnEndScreen             { get; } = new MemoryWatcher<bool>(new DeepPointer(0x7C0DD0));
        public MemoryWatcher<byte> NumPlayers                { get; } = new MemoryWatcher<byte>(new DeepPointer(0xD7F8EC, 0x10));
        public MemoryWatcher<byte> SpLoading                 { get; } = new MemoryWatcher<byte>(new DeepPointer(0xA84CAC));
        public MemoryWatcher<byte> MpLoading                 { get; } = new MemoryWatcher<byte>(new DeepPointer(0xCEB5F8));
        public MemoryWatcher<byte> MpLoading2                { get; } = new MemoryWatcher<byte>(new DeepPointer(0xCA8D0B));
        public MemoryWatcher<uint> GameTime                  { get; } = new MemoryWatcher<uint>(new DeepPointer(0xCA8EE4));
        // cdc::PCDeviceManager->D3DPRESENT_PARAMETERS
        public MemoryWatcher<int>  RefreshRate               { get; } = new MemoryWatcher<int> (new DeepPointer(0x0884554, 0x228));
        public MemoryWatcher<int>  VSyncPresentationInterval { get; } = new MemoryWatcher<int> (new DeepPointer(0x0884554, 0x22C));

        public GameData()
        {
            this.AddRange(this.GetType().GetProperties()
                .Where(p => !p.GetIndexParameters().Any())
                .Select(p => p.GetValue(this, null) as MemoryWatcher)
                .Where(p => p != null));
        }
    }

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

        private Process _process;
        private GameData _data;

        private PersonalBestILDB _pbDb;
        
        private const int D3DPRESENT_DONOTWAIT = 0x00000001;

        public GameMemory()
        {
            _data = new GameData();
            _pbDb = new PersonalBestILDB();
            _pbDb.OnNewILPersonalBest += pbDb_OnNewILPersonalBest;
        }

        public void Update()
        {
            if (_process == null || _process.HasExited)
            {
                _process = null;
                if (!this.TryGetGameProcess())
                    return;
                _data.ResetAll();
            }

            _data.UpdateAll(_process);
            _pbDb.Update(_process);

            int numPlayers = _data.NumPlayers.Current;

            if (_data.IsOnEndScreen.Changed)
                this.OnLevelFinished?.Invoke(this, _data.CurrentMap.Current);
            else if ((numPlayers == 1 && _data.SpLoading.Current == 1 && _data.SpLoading.Old != 1 && !_data.IsOnEndScreen.Current)
                || (numPlayers > 1 && _data.MpLoading.Current == 2 && _data.MpLoading.Old == 7)   // new game
                || (numPlayers > 1 && _data.MpLoading2.Current == 1 && _data.MpLoading2.Old == 0) // death
                || (numPlayers > 1 && _data.MpLoading.Current == 2 && _data.MpLoading.Old == 1))  // change level
            {
                this.OnLoadStart?.Invoke(this, EventArgs.Empty);
            }
            else if ((numPlayers == 1 && _data.SpLoading.Current != 1 && _data.SpLoading.Old == 1 && !_data.IsOnEndScreen.Current)
                || (numPlayers > 1 && _data.MpLoading.Current == 1 && _data.MpLoading.Old == 3))
            {
                this.OnLoadFinish?.Invoke(this, EventArgs.Empty);
            }

            if (_data.GameTime.Current == 0 && _data.GameTime.Old > 0 && _data.CurrentMap.Current == "alc_1_it_beginning")
                this.OnFirstLevelLoading?.Invoke(this, EventArgs.Empty);
            else if (_data.GameTime.Current > 0 && _data.GameTime.Old == 0 && _data.CurrentMap.Current == "alc_1_it_beginning")
                this.OnFirstLevelStarted?.Invoke(this, EventArgs.Empty);

            if (_data.VSyncPresentationInterval.Current != D3DPRESENT_DONOTWAIT ||
                (_data.RefreshRate.Current != 59 && _data.RefreshRate.Current != 60))
            {
                if (_data.GameTime.Current > 0 && _data.RefreshRate.Current != 0) // avoid false detection on game startup and zeroed memory on exit
                    this.OnInvalidSettingsDetected?.Invoke(this, EventArgs.Empty);
            }
        }

        bool TryGetGameProcess()
        {
            Process game = Process.GetProcesses()
                .FirstOrDefault(p => p.ProcessName.ToLower() == "lcgol" && !p.HasExited);
            if (game == null)
                return false;

            _process = game;
            return true;
        }

        void pbDb_OnNewILPersonalBest(object sender, string level, TimeSpan time, TimeSpan oldTime)
        {
            this.OnNewILPersonalBest?.Invoke(this, level, time, oldTime);
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
                uint time = ptr.Deref<uint>(game);

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
}
