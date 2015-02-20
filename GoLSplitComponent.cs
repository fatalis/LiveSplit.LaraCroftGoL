using System.Drawing;
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
        private List<string> _completedLevels;

        public GoLSplitComponent(LiveSplitState state)
        {
            this.ContextMenuControls = new Dictionary<String, Action>();

            this.InternalComponent = new InfoTimeComponent(null, null, new RegularTimeFormatter(TimeAccuracy.Hundredths));

            _completedLevels = new List<string>();
            _timer = new TimerModel { CurrentState = state };

            _gameMemory = new GameMemory();
            _gameMemory.OnFirstLevelLoading += gameMemory_OnFirstLevelLoading;
            _gameMemory.OnFirstLevelStarted += gameMemory_OnFirstLevelStarted;
            _gameMemory.OnLevelFinished += gameMemory_OnLevelFinished;
            _gameMemory.OnLoadStart += gameMemory_OnLoadStart;
            _gameMemory.OnLoadFinish += gameMemory_OnLoadFinish;
            _gameMemory.OnInvalidSettingsDetected += gameMemory_OnInvalidSettingsDetected;
            _gameMemory.StartReading();
        }

        public void Dispose()
        {
            if (_gameMemory != null)
                _gameMemory.Stop();
        }

        void gameMemory_OnLevelFinished(object sender, string level)
        {
            // hack to hopefully fix an issue one person has where this is called many times on the level end screen
            if (_completedLevels.Contains(level))
                return;

            _completedLevels.Add(level);
            _timer.Split();
        }

        void gameMemory_OnFirstLevelStarted(object sender, EventArgs e)
        {
            _timer.Start();
        }

        void gameMemory_OnFirstLevelLoading(object sender, EventArgs e)
        {
            _timer.Reset();
            _completedLevels.Clear();
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
