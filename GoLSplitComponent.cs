using System.Diagnostics;
using System.Drawing;
using System.Media;
using LiveSplit.Model;
using LiveSplit.TimeFormatters;
using LiveSplit.UI.Components;
using LiveSplit.UI;
using System;
using System.Collections.Generic;
using System.Xml;
using System.Windows.Forms;

namespace LiveSplit.GoLSplit
{
    class GoLSplitComponent : IComponent
    {
        public string ComponentName
        {
            get { return "Lara Croft: GoL"; }
        }

        public IDictionary<string, Action> ContextMenuControls { get; protected set; }
        protected InfoTimeComponent InternalComponent { get; set; }

        private TimerModel _timer;
        private GameMemory _gameMemory;
        private LogForm _logForm;
        private DateTime _lastSplit;

        public GoLSplitComponent(LiveSplitState state)
        {
            this.InternalComponent = new InfoTimeComponent(null, null, new RegularTimeFormatter(TimeAccuracy.Hundredths));

            _timer = new TimerModel { CurrentState = state };
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
            _gameMemory.StartReading();

            this.ContextMenuControls = new Dictionary<String, Action>();
            this.ContextMenuControls.Add("Lara Croft: GoL - IL PB Log", () => _logForm.Show());
        }

        public void Dispose()
        {
            if (_gameMemory != null)
                _gameMemory.Stop();
            if (_logForm != null)
                _logForm.Dispose();
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
            _logForm.AddMessage(String.Format("{0}: {1:m\\:ss\\.fff} - {2:m\\:ss\\.fff} improvement", level, time, improve));

            try
            {
                new SoundPlayer(Properties.Resources.UI_reward_b_05_left).Play();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) { }
        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region region) { }
        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region region) { }
        public XmlNode GetSettings(XmlDocument document) { return document.CreateElement("Settings"); }
        public Control GetSettingsControl(LayoutMode mode) { return null; }
        public void SetSettings(XmlNode settings) { }
        public void RenameComparison(string oldName, string newName) { }
        public float VerticalHeight  { get { return this.InternalComponent.VerticalHeight; } }
        public float MinimumWidth    { get { return this.InternalComponent.MinimumWidth; } }
        public float HorizontalWidth { get { return this.InternalComponent.HorizontalWidth; } }
        public float MinimumHeight   { get { return this.InternalComponent.MinimumHeight; } }
        public float PaddingLeft     { get { return this.InternalComponent.PaddingLeft; } }
        public float PaddingRight    { get { return this.InternalComponent.PaddingRight; } }
        public float PaddingTop      { get { return this.InternalComponent.PaddingTop; } }
        public float PaddingBottom   { get { return this.InternalComponent.PaddingBottom; } }
    }
}
