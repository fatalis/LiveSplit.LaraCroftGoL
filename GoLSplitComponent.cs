using System.Diagnostics;
using System.Media;
using LiveSplit.Model;
using LiveSplit.UI.Components;
using LiveSplit.UI;
using System;
using System.Collections.Generic;
using System.Xml;
using System.Windows.Forms;

namespace LiveSplit.GoLSplit
{
    class GoLSplitComponent : LogicComponent
    {
        public override string ComponentName => "Lara Croft: GoL";

        private TimerModel _timer;
        private GameMemory _gameMemory;
        private LogForm _logForm;
        private DateTime _lastSplit;
        private Timer _updateTimer;

        public GoLSplitComponent(LiveSplitState state)
        {
            _timer = new TimerModel { CurrentState = state };
            _timer.CurrentState.OnStart += timer_OnStart;

            _updateTimer = new Timer() { Interval = 15, Enabled = true };
            _updateTimer.Tick += updateTimer_Tick;

            _logForm = new LogForm();
            _lastSplit = DateTime.MinValue;

            _gameMemory = new GameMemory();
            _gameMemory.OnFirstLevelLoading += gameMemory_OnFirstLevelLoading;
            _gameMemory.OnFirstLevelStarted += gameMemory_OnFirstLevelStarted;
            _gameMemory.OnLevelFinished += gameMemory_OnLevelFinished;
            _gameMemory.OnLoadStart += gameMemory_OnLoadStart;
            _gameMemory.OnLoadFinish += gameMemory_OnLoadFinish;
            _gameMemory.OnInvalidSettingsDetected += gameMemory_OnInvalidSettingsDetected;
            _gameMemory.OnNewILPersonalBest += gameMemory_OnNewILPersonalBest;

            this.ContextMenuControls = new Dictionary<String, Action>();
            this.ContextMenuControls.Add("Lara Croft: GoL - IL PB Log", () => _logForm.Show());
        }

        public override void Dispose()
        {
            _timer.CurrentState.OnStart -= timer_OnStart;
            _logForm?.Dispose();
            _updateTimer?.Dispose();
        }

        void updateTimer_Tick(object sender, EventArgs eventArgs)
        {
            try
            {
                _gameMemory.Update();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }

        void timer_OnStart(object sender, EventArgs e)
        {
            _timer.InitializeGameTime();
        }

        void gameMemory_OnLevelFinished(object sender, string level)
        {
            // hack to hopefully fix an issue one person has where this is called many times on the level end screen
            if (DateTime.Now - _lastSplit < TimeSpan.FromSeconds(10))
                return;
            _lastSplit = DateTime.Now;

            _timer.Split();
        }

        void gameMemory_OnFirstLevelStarted(object sender, EventArgs e)
        {
            _timer.Start();
        }

        void gameMemory_OnFirstLevelLoading(object sender, EventArgs e)
        {
            _timer.Reset();
        }

        void gameMemory_OnLoadStart(object sender, EventArgs e)
        {
            _timer.CurrentState.IsGameTimePaused = true;
        }

        void gameMemory_OnLoadFinish(object sender, EventArgs e)
        {
            _timer.CurrentState.IsGameTimePaused = false;
        }

        void gameMemory_OnInvalidSettingsDetected(object sender, EventArgs e)
        {
            if (_timer.CurrentState.CurrentPhase == TimerPhase.Running)
            {
                MessageBox.Show(
                    "Invalid settings detected. VSync must be ON and refresh rate must be set to 60hz. Stopping timer.",
                    "LiveSplit.LaraCroftGoL", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                _timer.Reset(false);
            }
        }

        void gameMemory_OnNewILPersonalBest(object sender, string level, TimeSpan time, TimeSpan oldTime)
        {
            TimeSpan improve = oldTime - time;
            _logForm.AddMessage($"{level}: {time:m\\:ss\\.fff} - {improve:m\\:ss\\.fff} improvement");

            try
            {
                new SoundPlayer(Properties.Resources.UI_reward_b_05_left).Play();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }

        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) { }
        public override XmlNode GetSettings(XmlDocument document) { return document.CreateElement("Settings"); }
        public override Control GetSettingsControl(LayoutMode mode) { return null; }
        public override void SetSettings(XmlNode settings) { }
    }
}
