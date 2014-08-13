using System.Drawing;
using System.Globalization;
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
        private uint _totalGameTime;
        private GraphicsCache _cache;
        private MapTimesForm _mapTimesForm;

        public GoLSplitComponent(LiveSplitState state)
        {
            this.ContextMenuControls = new Dictionary<String, Action>();
            this.ContextMenuControls.Add("GoLSplit: Level Times", () =>
            {
                if (_mapTimesForm.Visible)
                    _mapTimesForm.Hide();
                else
                    _mapTimesForm.Show();
            });

            this.InternalComponent = new InfoTimeComponent(null, null, new RegularTimeFormatter(TimeAccuracy.Hundredths));

            _mapTimesForm = new MapTimesForm();
            _cache = new GraphicsCache();
            _timer = new TimerModel { CurrentState = state };

            _gameMemory = new GameMemory();
            _gameMemory.OnFirstLevelLoading += gameMemory_OnFirstLevelLoading;
            _gameMemory.OnFirstLevelStarted += gameMemory_OnFirstLevelStarted;
            _gameMemory.OnLevelFinished += gameMemory_OnLevelFinished;
            _gameMemory.StartReading();
        }

        public void Dispose()
        {
            if (_gameMemory != null)
                _gameMemory.Stop();
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            state.IsGameTimePaused = true; // prevent flicker, doesn't actually pause anything.
            state.SetGameTime(TimeSpan.FromMilliseconds(_totalGameTime + _gameMemory.GameTime));

            this.InternalComponent.TimeValue =
                state.CurrentTime[state.CurrentTimingMethod == TimingMethod.GameTime
                    ? TimingMethod.RealTime : TimingMethod.GameTime];
            this.InternalComponent.InformationName = state.CurrentTimingMethod == TimingMethod.GameTime
                ? "Real Time" : "Game Time";

            _cache.Restart();
            _cache["TimeValue"] = this.InternalComponent.ValueLabel.Text;
            _cache["TimingMethod"] = state.CurrentTimingMethod;
            if (invalidator != null && _cache.HasChanged)
                invalidator.Invalidate(0f, 0f, width, height);
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region region)
        {
            this.PrepareDraw(state);
            this.InternalComponent.DrawVertical(g, state, width, region);
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region region)
        {
            this.PrepareDraw(state);
            this.InternalComponent.DrawHorizontal(g, state, height, region);
        }

        void PrepareDraw(LiveSplitState state)
        {
            this.InternalComponent.NameLabel.ForeColor = state.LayoutSettings.TextColor;
            this.InternalComponent.ValueLabel.ForeColor = state.LayoutSettings.TextColor;
            this.InternalComponent.NameLabel.HasShadow = this.InternalComponent.ValueLabel.HasShadow = state.LayoutSettings.DropShadows;
        }

        void gameMemory_OnLevelFinished(object sender, string level, uint time)
        {
            _mapTimesForm.AddMapTime(level, new ThousandthsTimeFormatter().Format(TimeSpan.FromMilliseconds(time)));
            _totalGameTime += time;
            _timer.Split();
        }

        void gameMemory_OnFirstLevelStarted(object sender, EventArgs e)
        {
            _timer.Start();
        }

        void gameMemory_OnFirstLevelLoading(object sender, EventArgs e)
        {
            _totalGameTime = 0;
            _timer.Reset();
            _mapTimesForm.Reset();
        }

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

    class ThousandthsTimeFormatter : ITimeFormatter
    {
        public string Format(TimeSpan? time)
        {
            if (!time.HasValue)
                return "0.000";

            if (time.Value.TotalDays >= 1.0)
                return (int)time.Value.TotalHours + time.Value.ToString("\\:mm\\:ss\\.fff", CultureInfo.InvariantCulture);
            if (time.Value.TotalHours >= 1.0)
                return time.Value.ToString("h\\:mm\\:ss\\.fff", CultureInfo.InvariantCulture);
            else
                return time.Value.ToString("m\\:ss\\.fff", CultureInfo.InvariantCulture);
        }
    }
}
