using System.ComponentModel;
using System.Text;
using RightClickDoctor.Models;
using RightClickDoctor.Probing;
using RightClickDoctor.Registry;
using RightClickDoctor.Reports;

namespace RightClickDoctor.UI;

public sealed class MainForm : Form
{
    private readonly ShellEntryScanner _scanner = new();
    private readonly ShellEntryManager _manager = new();
    private readonly ProbeRunner _probeRunner = new();
    private readonly BindingSource _bindingSource = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _detailsBox = new();
    private readonly TextBox _samplePathBox = new();
    private readonly NumericUpDown _timeoutBox = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripProgressBar _progressBar = new();
    private readonly List<Button> _buttons = new();
    private List<ShellEntry> _entries = new();
    private CancellationTokenSource? _probeCancellation;

    public MainForm()
    {
        Text = "RightClickDoctor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 680);
        Size = new Size(1360, 820);
        BuildLayout();
        Shown += (_, _) => ScanEntries();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(8),
            WrapContents = true
        };
        root.Controls.Add(toolbar, 0, 0);

        toolbar.Controls.Add(MakeButton("Scan", (_, _) => ScanEntries()));
        toolbar.Controls.Add(MakeButton("Probe timings", async (_, _) => await ProbeEntriesAsync()));
        toolbar.Controls.Add(MakeButton("Disable selected", (_, _) => DisableSelected()));
        toolbar.Controls.Add(MakeButton("Enable selected", (_, _) => EnableSelected()));
        toolbar.Controls.Add(MakeButton("Restart Explorer", (_, _) => RestartExplorer()));
        toolbar.Controls.Add(MakeButton("Export report", (_, _) => ExportReport()));

        toolbar.Controls.Add(MakeLabel("Sample path"));
        _samplePathBox.Width = 330;
        _samplePathBox.Text = Path.Combine(Path.GetTempPath(), "RightClickDoctor sample.txt");
        toolbar.Controls.Add(_samplePathBox);
        toolbar.Controls.Add(MakeButton("Browse", (_, _) => BrowseSamplePath(), trackBusy: false));

        toolbar.Controls.Add(MakeLabel("Timeout"));
        _timeoutBox.Minimum = 1;
        _timeoutBox.Maximum = 60;
        _timeoutBox.Value = 8;
        _timeoutBox.Width = 60;
        toolbar.Controls.Add(_timeoutBox);
        toolbar.Controls.Add(MakeLabel("sec"));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 500
        };
        root.Controls.Add(split, 0, 1);

        ConfigureGrid();
        split.Panel1.Controls.Add(_grid);

        _detailsBox.Dock = DockStyle.Fill;
        _detailsBox.Multiline = true;
        _detailsBox.ReadOnly = true;
        _detailsBox.ScrollBars = ScrollBars.Vertical;
        _detailsBox.Font = new Font("Consolas", 10f);
        split.Panel2.Controls.Add(_detailsBox);

        var status = new StatusStrip();
        _statusLabel.Text = "Ready";
        _progressBar.Visible = false;
        _progressBar.Width = 220;
        status.Items.Add(_statusLabel);
        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        status.Items.Add(_progressBar);
        root.Controls.Add(status, 0, 2);
    }

    private Button MakeButton(string text, EventHandler handler, bool trackBusy = true)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Margin = new Padding(4)
        };
        button.Click += handler;
        if (trackBusy)
        {
            _buttons.Add(button);
        }

        return button;
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 7, 0, 0)
        };
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += (_, _) => UpdateDetails();
        _grid.CellFormatting += GridCellFormatting;

        AddColumn(nameof(ShellEntry.Risk), "Risk", 76);
        AddColumn(nameof(ShellEntry.StateText), "State", 96);
        AddColumn(nameof(ShellEntry.KindText), "Kind", 126);
        AddColumn(nameof(ShellEntry.DisplayName), "Name", 210);
        AddColumn(nameof(ShellEntry.Context), "Context", 132);
        AddColumn(nameof(ShellEntry.TotalMsText), "Total ms", 76);
        AddColumn(nameof(ShellEntry.InitializeMsText), "Init ms", 72);
        AddColumn(nameof(ShellEntry.QueryContextMenuMsText), "Query ms", 78);
        AddColumn(nameof(ShellEntry.Publisher), "Publisher", 160);
        AddColumn(nameof(ShellEntry.CompanyName), "Company", 150);
        AddColumn(nameof(ShellEntry.TargetPath), "DLL / command", 360);
        AddColumn(nameof(ShellEntry.ClsidText), "CLSID", 260);
        AddColumn(nameof(ShellEntry.RegistryPath), "Registry", 380);
    }

    private void AddColumn(string property, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }

    private void ScanEntries()
    {
        try
        {
            SetBusy(true, "Scanning registry...");
            _entries = OrderEntries(_scanner.Scan()).ToList();
            _bindingSource.DataSource = new BindingList<ShellEntry>(_entries);
            _grid.ClearSelection();
            SetStatus($"Found {_entries.Count} context menu registrations. Probe timings to identify slow COM handlers.");
        }
        catch (Exception ex)
        {
            ShowError("Scan failed", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ProbeEntriesAsync()
    {
        var selected = SelectedEntries().Where(entry => entry.CanProbe).ToList();
        var candidates = selected.Count > 0
            ? selected
            : _entries.Where(entry => entry.CanProbe).ToList();

        if (candidates.Count == 0)
        {
            MessageBox.Show("No enabled 64-bit COM context menu handlers are available to probe.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (selected.Count == 0)
        {
            var confirm = MessageBox.Show(
                $"Probe all {candidates.Count} enabled 64-bit COM handlers? Each one runs in an isolated child process with a timeout.",
                Text,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.OK)
            {
                return;
            }
        }

        try
        {
            var samplePath = EnsureSamplePath();
            _probeCancellation = new CancellationTokenSource();
            _progressBar.Minimum = 0;
            _progressBar.Maximum = candidates.Count;
            _progressBar.Value = 0;
            _progressBar.Visible = true;
            SetBusy(true, "Probing handlers...");

            var progress = new Progress<ProbeProgress>(probeProgress =>
            {
                _progressBar.Value = Math.Min(probeProgress.Completed, _progressBar.Maximum);
                _statusLabel.Text = $"Probed {probeProgress.Completed}/{probeProgress.Total}: {probeProgress.Entry.DisplayName}";
                _grid.Refresh();
                UpdateDetails();
            });

            await _probeRunner.ProbeAsync(
                candidates,
                new[] { samplePath },
                TimeSpan.FromSeconds((double)_timeoutBox.Value),
                progress,
                _probeCancellation.Token);

            _entries = OrderEntries(_entries).ToList();
            _bindingSource.DataSource = new BindingList<ShellEntry>(_entries);
            SetStatus("Probe complete. Sort by Risk or Total ms, then disable obvious third-party offenders.");
        }
        catch (Exception ex)
        {
            ShowError("Probe failed", ex);
        }
        finally
        {
            _probeCancellation?.Dispose();
            _probeCancellation = null;
            _progressBar.Visible = false;
            SetBusy(false);
        }
    }

    private void DisableSelected()
    {
        ChangeSelected(disable: true);
    }

    private void EnableSelected()
    {
        ChangeSelected(disable: false);
    }

    private void ChangeSelected(bool disable)
    {
        var selected = SelectedEntries().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more rows first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (disable && selected.Any(entry => entry.IsMicrosoft))
        {
            var systemConfirm = MessageBox.Show(
                "The selection includes Microsoft-signed or Windows-system entries. Disable them only if you know exactly why.",
                Text,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (systemConfirm != DialogResult.OK)
            {
                return;
            }
        }

        var action = disable ? "disable" : "enable";
        var confirm = MessageBox.Show(
            $"Really {action} {selected.Count} selected item(s)? This writes reversible registry values and then notifies Explorer.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var entry in selected)
        {
            try
            {
                if (disable)
                {
                    _manager.Disable(entry);
                }
                else
                {
                    _manager.Enable(entry);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.DisplayName}: {ex.Message}");
            }
        }

        _grid.Refresh();
        UpdateDetails();

        if (failures.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, failures), $"{action} failed for some entries", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            SetStatus($"{selected.Count} item(s) updated. Restart Explorer if the menu still shows old entries.");
        }
    }

    private void RestartExplorer()
    {
        var confirm = MessageBox.Show(
            "Restart Windows Explorer now? Open File Explorer windows and taskbar will briefly disappear and come back.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        try
        {
            SetBusy(true, "Restarting Explorer...");
            _manager.RestartExplorer();
            SetStatus("Explorer restart requested.");
        }
        catch (Exception ex)
        {
            ShowError("Failed to restart Explorer", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportReport()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export RightClickDoctor report",
            Filter = "JSON report (*.json)|*.json|CSV report (*.csv)|*.csv",
            FileName = $"RightClickDoctor-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ReportWriter.Write(dialog.FileName, _entries);
            SetStatus($"Report exported: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowError("Export failed", ex);
        }
    }

    private void BrowseSamplePath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a sample file to right-click during probes",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _samplePathBox.Text = dialog.FileName;
        }
    }

    private string EnsureSamplePath()
    {
        var path = _samplePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(Path.GetTempPath(), "RightClickDoctor sample.txt");
            _samplePathBox.Text = path;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Path.GetTempPath());
            File.WriteAllText(path, "RightClickDoctor context menu probe sample.");
        }

        return Path.GetFullPath(path);
    }

    private IEnumerable<ShellEntry> SelectedEntries()
    {
        return _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem as ShellEntry)
            .Where(entry => entry is not null)
            .Cast<ShellEntry>();
    }

    private void UpdateDetails()
    {
        var entry = SelectedEntries().FirstOrDefault();
        if (entry is null)
        {
            _detailsBox.Clear();
            return;
        }

        var builder = new StringBuilder();
        Append(builder, "Name", entry.DisplayName);
        Append(builder, "Kind", entry.KindText);
        Append(builder, "Risk", entry.Risk);
        Append(builder, "State", entry.StateText);
        Append(builder, "Context", entry.Context);
        Append(builder, "Total ms", entry.TotalMsText);
        Append(builder, "CreateInstance ms", entry.CreateInstanceMsText);
        Append(builder, "Initialize ms", entry.InitializeMsText);
        Append(builder, "QueryContextMenu ms", entry.QueryContextMenuMsText);
        Append(builder, "Menu items", entry.MenuItems?.ToString() ?? string.Empty);
        Append(builder, "CLSID", entry.ClsidText);
        Append(builder, "Threading model", entry.ThreadingModel);
        Append(builder, "Target", entry.TargetPath);
        Append(builder, "Publisher", entry.Publisher);
        Append(builder, "Company", entry.CompanyName);
        Append(builder, "Registry", entry.RegistryPath);
        Append(builder, "Registry view", entry.ViewText);
        Append(builder, "Last error", entry.LastError);
        _detailsBox.Text = builder.ToString();
    }

    private static void Append(StringBuilder builder, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(label).Append(": ").AppendLine(value);
        }
    }

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(ShellEntry.Risk) || e.Value is not string risk)
        {
            return;
        }

        var style = e.CellStyle;
        if (style is null)
        {
            return;
        }

        style.ForeColor = Color.Black;
        style.BackColor = risk switch
        {
            "Timeout" or "Severe" => Color.FromArgb(255, 190, 190),
            "Slow" => Color.FromArgb(255, 223, 171),
            "Watch" => Color.FromArgb(255, 244, 181),
            "OK" => Color.FromArgb(210, 242, 214),
            "Disabled" => Color.FromArgb(220, 220, 220),
            _ => Color.White
        };
    }

    private static IEnumerable<ShellEntry> OrderEntries(IEnumerable<ShellEntry> entries)
    {
        return entries
            .OrderByDescending(RiskWeight)
            .ThenByDescending(entry => entry.TotalMs ?? -1)
            .ThenByDescending(entry => entry.Kind == ShellEntryKind.ContextMenuHandler)
            .ThenBy(entry => entry.IsMicrosoft)
            .ThenBy(entry => entry.Context)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private static int RiskWeight(ShellEntry entry)
    {
        return entry.Risk switch
        {
            "Timeout" => 6,
            "Severe" => 5,
            "Slow" => 4,
            "Watch" => 3,
            "Error" => 2,
            "Unknown" => 1,
            _ => 0
        };
    }

    private void SetBusy(bool busy, string? status = null)
    {
        UseWaitCursor = busy;
        foreach (var button in _buttons)
        {
            button.Enabled = !busy;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _statusLabel.Text = status;
        }
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
    }

    private void ShowError(string caption, Exception ex)
    {
        MessageBox.Show(ex.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        SetStatus($"{caption}: {ex.Message}");
    }
}
