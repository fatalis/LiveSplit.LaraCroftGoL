using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace LiveSplit.GoLSplit
{
    public partial class LogForm : Form
    {
        public LogForm()
        {
            InitializeComponent();
        }

        public void AddMessage(string message)
        {
            this.tbLog.AppendText(Environment.NewLine + DateTime.Now.ToShortTimeString() + "\t" + message);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
        }
    }
}
