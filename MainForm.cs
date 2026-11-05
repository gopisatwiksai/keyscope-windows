using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyScope
{
    public sealed class MainForm : Form
    {
        private readonly AudioLoopbackListener _listener = new AudioLoopbackListener();
        private Label _keyValue;
        private Label _keyCaption;
        private Label _confidenceValue;
        private Label _bpmValue;
        private Label _status;
        private Button _startButton;
        private Button _stopButton;

        public MainForm()
        {
            BuildInterface();
            _listener.AnalysisUpdated += ListenerAnalysisUpdated;
            _listener.Failed += ListenerFailed;
        }

        private void BuildInterface()
        {
            Text = "KeyScope — song key detector";
            ClientSize = new Size(620, 390);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(19, 22, 28);
            ForeColor = Color.FromArgb(242, 244, 247);
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            Label title = LabelAt("KEYSCOPE", 34, 26, 300, 24, 10f, FontStyle.Bold, Color.FromArgb(119, 205, 166));
            title.LetterSpacingSafe();
            LabelAt("Song / section key detector", 34, 50, 390, 29, 19f, FontStyle.Bold, Color.White);
            LabelAt("Captures your default Windows playback device — local analysis, no files or uploads.",
                34, 80, 550, 22, 9.5f, FontStyle.Regular, Color.FromArgb(175, 184, 196));

            Panel card = new Panel();
            card.Bounds = new Rectangle(30, 122, 560, 170);
            card.BackColor = Color.FromArgb(29, 34, 43);
            Controls.Add(card);

            _keyValue = InPanel(card, "—", 24, 20, 300, 64, 44f, FontStyle.Bold, Color.White);
            _keyCaption = InPanel(card, "Press Start to listen", 27, 87, 290, 30, 14f, FontStyle.Regular, Color.FromArgb(185, 193, 204));
            InPanel(card, "EVIDENCE", 358, 28, 160, 19, 8.5f, FontStyle.Bold, Color.FromArgb(144, 158, 174));
            _confidenceValue = InPanel(card, "—", 358, 46, 160, 36, 22f, FontStyle.Bold, Color.White);
            InPanel(card, "ROUGH BPM", 358, 103, 160, 19, 8.5f, FontStyle.Bold, Color.FromArgb(144, 158, 174));
            _bpmValue = InPanel(card, "—", 358, 121, 170, 31, 18f, FontStyle.Bold, Color.White);

            _status = LabelAt("Ready — Start, then let a tonal section play for about 20 seconds.",
                34, 310, 552, 34, 9.5f, FontStyle.Regular, Color.FromArgb(192, 202, 215));

            _startButton = CreateButton("Start listening", 34, 348, 166, 34,
                Color.FromArgb(61, 166, 113), Color.White);
            _stopButton = CreateButton("Stop", 211, 348, 94, 34,
                Color.FromArgb(55, 62, 74), Color.FromArgb(187, 196, 207));
            _stopButton.Enabled = false;
            _startButton.Click += StartButtonClick;
            _stopButton.Click += StopButtonClick;

            LabelAt("Song/section mode: gathers context first; it does not follow each chord.",
                320, 355, 270, 26, 8.25f, FontStyle.Regular, Color.FromArgb(132, 143, 158));
        }

        private Label LabelAt(string text, int x, int y, int width, int height, float size,
            FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Bounds = new Rectangle(x, y, width, height);
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoEllipsis = true;
            Controls.Add(label);
            return label;
        }

        private static Label InPanel(Panel panel, string text, int x, int y, int width, int height,
            float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Bounds = new Rectangle(x, y, width, height);
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoEllipsis = true;
            panel.Controls.Add(label);
            return label;
        }

        private Button CreateButton(string text, int x, int y, int width, int height, Color backColor, Color foreColor)
        {
            Button button = new Button();
            button.Text = text;
            button.Bounds = new Rectangle(x, y, width, height);
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;
            Controls.Add(button);
            return button;
        }

        private void StartButtonClick(object sender, EventArgs eventArgs)
        {
            _status.Text = "Starting WASAPI loopback on the default Windows output…";
            _status.ForeColor = Color.FromArgb(187, 211, 248);
            _keyValue.Text = "…";
            _keyCaption.Text = "Waiting for enough audio";
            _confidenceValue.Text = "—";
            _bpmValue.Text = "—";
            _listener.Start();
            if (_listener.IsRunning)
            {
                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _status.Text = "Listening locally — gathering whole-track evidence for about 20 seconds.";
                _status.ForeColor = Color.FromArgb(149, 218, 181);
            }
        }

        private void StopButtonClick(object sender, EventArgs eventArgs)
        {
            _listener.Stop();
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _keyValue.Text = "—";
            _keyCaption.Text = "Stopped";
            _confidenceValue.Text = "—";
            _bpmValue.Text = "—";
            _status.Text = "Stopped — no audio is being captured.";
            _status.ForeColor = Color.FromArgb(192, 202, 215);
        }

        private void ListenerAnalysisUpdated(KeyEstimate estimate, double? bpm)
        {
            RunOnUiThread(delegate
            {
                if (!_listener.IsRunning) return;
                if (estimate.State == EstimateState.Key)
                {
                    _keyValue.Text = estimate.Key;
                    _keyCaption.Text = estimate.Mode + " • stable estimate";
                    _confidenceValue.Text = estimate.Confidence + " sec";
                    _confidenceValue.ForeColor = Color.FromArgb(119, 220, 165);
                    _status.Text = "Listening locally — " + estimate.Detail + ".";
                    _status.ForeColor = Color.FromArgb(149, 218, 181);
                }
                else
                {
                    _keyValue.Text = "—";
                    _keyCaption.Text = estimate.State == EstimateState.Silence ? "No key detected" : "No reliable key yet";
                    _confidenceValue.Text = "—";
                    _confidenceValue.ForeColor = Color.White;
                    _status.Text = estimate.Detail;
                    _status.ForeColor = Color.FromArgb(220, 188, 125);
                }
                _bpmValue.Text = bpm.HasValue ? "~" + Math.Round(bpm.Value).ToString() : "—";
            });
        }

        private void ListenerFailed(string message)
        {
            RunOnUiThread(delegate
            {
                _listener.Stop();
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _keyValue.Text = "—";
                _keyCaption.Text = "Capture unavailable";
                _status.Text = message;
                _status.ForeColor = Color.FromArgb(244, 154, 154);
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); } catch (InvalidOperationException) { }
            }
            else action();
        }

        private static Color ConfidenceColor(int confidence)
        {
            return Color.FromArgb(119, 220, 165);
        }

        protected override void OnFormClosed(FormClosedEventArgs eventArgs)
        {
            _listener.Dispose();
            base.OnFormClosed(eventArgs);
        }
    }

    internal static class LabelExtensions
    {
        // WinForms has no reliable portable letter-spacing API. Kept as a no-op so the header stays simple.
        public static void LetterSpacingSafe(this Label label) { }
    }
}
