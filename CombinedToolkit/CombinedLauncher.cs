using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class CombinedLauncher
{
	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(
		IntPtr window, out uint processId);

	private sealed class Payload
	{
		public string Resource;
		public string FileName;

		public Payload(string resource, string fileName)
		{
			Resource = resource;
			FileName = fileName;
		}
	}

	private sealed class TargetChoice
	{
		public int ProcessId;
		public bool UseTml;
		public int Score;
		public string Title;

		public string Key
		{
			get { return (UseTml ? "tml:" : "vanilla:") + ProcessId; }
		}

		public override string ToString()
		{
			string backend = UseTml ? "tModLoader" : "Terraria 原版";
			string title = string.IsNullOrWhiteSpace(Title)
				? "无窗口标题"
				: Title.Trim();
			return backend + "  |  PID " + ProcessId + "  |  " + title;
		}
	}

	private sealed class InjectionResult
	{
		public bool Success;
		public string Message;
	}

	private sealed class LauncherForm : Form
	{
		private readonly Label _connectionLabel;
		private readonly Label _statusLabel;
		private readonly Button _retryButton;
		private readonly System.Windows.Forms.Timer _scanTimer;
		private bool _injecting;
		private bool _scanInProgress;
		private string _lastAttemptKey = string.Empty;

		public LauncherForm()
		{
			Text = "Terraria / tModLoader 全物品修改器";
			StartPosition = FormStartPosition.CenterScreen;
			Size = new Size(1280, 760);
			MinimumSize = new Size(1100, 650);
			BackColor = Color.FromArgb(31, 33, 38);
			ForeColor = Color.WhiteSmoke;
			Font = new Font("Microsoft YaHei UI", 9F);

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.ColumnCount = 1;
			root.RowCount = 3;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			header.BackColor = Color.FromArgb(38, 41, 48);
			header.Padding = new Padding(16, 8, 16, 8);
			root.Controls.Add(header, 0, 0);

			TableLayoutPanel headerLayout = new TableLayoutPanel();
			headerLayout.Dock = DockStyle.Fill;
			headerLayout.ColumnCount = 2;
			headerLayout.RowCount = 2;
			headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
			headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
			headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
			header.Controls.Add(headerLayout);

			Label title = new Label();
			title.Text = "Terraria / tModLoader 全物品修改器";
			title.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
			title.Dock = DockStyle.Fill;
			title.TextAlign = ContentAlignment.MiddleLeft;
			headerLayout.Controls.Add(title, 0, 0);

			_connectionLabel = new Label();
			_connectionLabel.Text = "● 预览模式：等待游戏";
			_connectionLabel.ForeColor = Color.Silver;
			_connectionLabel.Dock = DockStyle.Fill;
			_connectionLabel.TextAlign = ContentAlignment.MiddleLeft;
			headerLayout.Controls.Add(_connectionLabel, 0, 1);

			_retryButton = NewLauncherButton("重新扫描");
			_retryButton.Dock = DockStyle.Fill;
			_retryButton.Margin = new Padding(8, 6, 0, 6);
			_retryButton.Click += delegate {
				_lastAttemptKey = string.Empty;
				QueueGameScan(true);
			};
			headerLayout.Controls.Add(_retryButton, 1, 0);
			headerLayout.SetRowSpan(_retryButton, 2);

			TabControl tabs = BuildPreviewTabs();
			root.Controls.Add(tabs, 0, 1);

			_statusLabel = new Label();
			_statusLabel.Dock = DockStyle.Fill;
			_statusLabel.Padding = new Padding(10, 6, 0, 0);
			_statusLabel.ForeColor = Color.Gainsboro;
			_statusLabel.Text =
				"当前可预览全部功能；检测到游戏后会自动注入并打开可操作界面。";
			root.Controls.Add(_statusLabel, 0, 2);

			_scanTimer = new System.Windows.Forms.Timer();
			_scanTimer.Interval = 2000;
			_scanTimer.Tick += delegate {
				QueueGameScan(false);
			};

			Shown += delegate {
				_scanTimer.Start();
				QueueGameScan(true);
			};
			FormClosing += delegate(object sender, FormClosingEventArgs args) {
				if (_injecting) {
					args.Cancel = true;
					_statusLabel.Text = "自动注入正在进行，请等待本次操作完成。";
				}
			};
			FormClosed += delegate {
				_scanTimer.Stop();
				_scanTimer.Dispose();
			};
		}

		private TabControl BuildPreviewTabs()
		{
			TabControl tabs = new TabControl();
			tabs.Dock = DockStyle.Fill;
			tabs.Controls.Add(BuildBasicPreviewPage());
			tabs.Controls.Add(BuildItemsPreviewPage());
			tabs.Controls.Add(BuildItemEditorPreviewPage());
			tabs.Controls.Add(BuildRemoteInventoryPreviewPage());
			tabs.Controls.Add(BuildHistoryPreviewPage());
			return tabs;
		}

		private TabPage BuildBasicPreviewPage()
		{
			TabPage page = NewPreviewPage("基本修改");
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.AutoScroll = true;
			layout.Padding = new Padding(14, 10, 14, 14);
			layout.ColumnCount = 2;
			layout.RowCount = 4;
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			page.Controls.Add(layout);

			FlowLayoutPanel survival = AddPreviewSection(layout, "生存与防护", 0, 0, 1);
			FlowLayoutPanel movement = AddPreviewSection(layout, "移动与物理", 1, 0, 1);
			FlowLayoutPanel capacity = AddPreviewSection(layout, "容量与召唤", 0, 1, 1);
			FlowLayoutPanel world = AddPreviewSection(layout, "世界、视野与建造", 1, 1, 1);
			FlowLayoutPanel combat = AddPreviewSection(layout, "战斗与特殊状态", 0, 2, 2);

			AddPreviewOptions(survival, new[] {
				"无敌（上帝模式 + 持续免伤 + 状态回满）",
				"生命锁定（每帧回满）", "秒重生", "无限魔力", "无限氧气",
				"无药水冷却", "清除负面状态", "免疫击退", "免疫摔落伤害", "熔岩免疫"
			});

			AddPreviewOption(movement, "高速移动");
			AddPreviewValue(movement, "移动倍率", "3.00 ×");
			AddPreviewOption(movement, "高跳");
			AddPreviewOption(movement, "无限段跳（空中无限触发二段跳）");
			AddPreviewValue(movement, "跳跃高度倍率", "2.50 ×");
			AddPreviewOption(movement, "自定义自身重力倍率");
			AddPreviewValue(movement, "重力倍率", "1.00 ×");
			AddPreviewOption(movement, "自定义最大下落速度");
			AddPreviewValue(movement, "最大下落速度", "10.00 px/tick");
			AddPreviewOption(movement, "最大下落速度：无限制");
			AddPreviewOption(movement, "可调 Step（自动走上完整方块）");
			AddPreviewValue(movement, "最大整格数", "3 格");
			AddPreviewOption(movement, "无限翅膀/火箭时间");

			AddPreviewOption(capacity, "自定义最大仆从容量");
			AddPreviewValue(capacity, "最大仆从容量", "10 个");
			AddPreviewOption(capacity, "自定义 Buff 栏上限（单人世界）");
			AddPreviewValue(capacity, "最大 Buff 数量", "100 个");

			AddPreviewOptions(world, new[] {
				"场景全亮（Fullbright）",
				"解除放大/缩小按键限制"
			});
			AddPreviewValue(world, "最远缩放倍率", "0.50 ×");
			AddPreviewOptions(world, new[] {
				"忽略世界边界（玩家与摄像机）",
				"解除 ImproveGame 选择范围限制",
				"无限放置、挖掘、交互距离"
			});
			Button reveal = NewPreviewButton("点亮整个地图");
			reveal.Margin = new Padding(3, 10, 3, 3);
			world.Controls.Add(reveal);

			AddPreviewOptions(combat, new[] {
				"并发攻击（提前触发新攻击，不清除已有弹射物）",
				"微光穿墙可操作（保留相位穿墙）",
				"灾厄 Parry 无冷却（包含方舟系右键格挡）"
			});

			Label note = NewPreviewNotice(
				"预览模式：可以提前查看功能与布局。游戏出现后会自动注入，随后打开真正可操作的修改器界面。");
			layout.Controls.Add(note, 0, 3);
			layout.SetColumnSpan(note, 2);
			return page;
		}

		private TabPage BuildItemsPreviewPage()
		{
			TabPage page = NewPreviewPage("包裹 / 全物品");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(10);
			root.ColumnCount = 2;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
			page.Controls.Add(root);

			TableLayoutPanel left = new TableLayoutPanel();
			left.Dock = DockStyle.Fill;
			left.RowCount = 3;
			left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
			left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			left.Margin = new Padding(0, 0, 7, 0);
			root.Controls.Add(left, 0, 0);
			Label searchTitle = NewPreviewLabel("搜索 ID / 中文名 / 英文名 / 内部名：");
			left.Controls.Add(searchTitle, 0, 0);
			TextBox search = new TextBox();
			search.Dock = DockStyle.Fill;
			search.Text = "注入后可搜索全部物品";
			left.Controls.Add(search, 0, 1);
			ListView items = NewPreviewList();
			items.Columns.Add("ID", 80);
			items.Columns.Add("物品名称", 220);
			items.Columns.Add("内部名", 220);
			items.Items.Add(new ListViewItem(new[] { "—", "注入后载入全部物品数据库", "—" }));
			left.Controls.Add(items, 0, 2);

			GroupBox inventory = new GroupBox();
			inventory.Text = "玩家包裹（槽位预览）";
			inventory.Dock = DockStyle.Fill;
			inventory.ForeColor = Color.WhiteSmoke;
			root.Controls.Add(inventory, 1, 0);
			inventory.Controls.Add(BuildSlotPreview(10, 5));
			return page;
		}

		private TabPage BuildItemEditorPreviewPage()
		{
			TabPage page = NewPreviewPage("物品属性编辑");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(18);
			root.ColumnCount = 2;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
			page.Controls.Add(root);

			GroupBox fields = new GroupBox();
			fields.Text = "选中背包物品的属性";
			fields.Dock = DockStyle.Fill;
			fields.ForeColor = Color.WhiteSmoke;
			fields.Padding = new Padding(14);
			root.Controls.Add(fields, 0, 0);
			FlowLayoutPanel form = new FlowLayoutPanel();
			form.Dock = DockStyle.Fill;
			form.FlowDirection = FlowDirection.TopDown;
			form.WrapContents = false;
			form.AutoScroll = true;
			fields.Controls.Add(form);
			foreach (string field in new[] {
				"数量", "伤害", "暴击", "击退", "使用时间", "使用动画", "射弹速度", "尺寸倍率"
			})
				AddPreviewValue(form, field, "0");
			AddPreviewOption(form, "自动挥舞 / 自动使用");
			Button apply = NewPreviewButton("应用修改后的属性");
			apply.Margin = new Padding(24, 14, 3, 3);
			form.Controls.Add(apply);

			GroupBox prefix = new GroupBox();
			prefix.Text = "可用前缀";
			prefix.Dock = DockStyle.Fill;
			prefix.ForeColor = Color.WhiteSmoke;
			prefix.Padding = new Padding(14);
			prefix.Margin = new Padding(10, 0, 0, 0);
			root.Controls.Add(prefix, 1, 0);
			FlowLayoutPanel prefixFlow = new FlowLayoutPanel();
			prefixFlow.Dock = DockStyle.Fill;
			prefixFlow.FlowDirection = FlowDirection.TopDown;
			prefixFlow.WrapContents = false;
			prefix.Controls.Add(prefixFlow);
			Label prefixHint = NewPreviewLabel("选择物品后，仅显示该物品实际可以使用的原版/模组前缀。");
			prefixHint.MaximumSize = new Size(420, 0);
			prefixFlow.Controls.Add(prefixHint);
			ComboBox prefixes = new ComboBox();
			prefixes.Width = 380;
			prefixes.DropDownStyle = ComboBoxStyle.DropDownList;
			prefixes.Items.Add("[0 - 无前缀]");
			prefixes.SelectedIndex = 0;
			prefixes.Margin = new Padding(3, 12, 3, 8);
			prefixFlow.Controls.Add(prefixes);
			FlowLayoutPanel buttons = new FlowLayoutPanel();
			buttons.AutoSize = true;
			buttons.Controls.Add(NewPreviewButton("应用前缀"));
			buttons.Controls.Add(NewPreviewButton("清除前缀"));
			buttons.Controls.Add(NewPreviewButton("随机重铸"));
			prefixFlow.Controls.Add(buttons);
			return page;
		}

		private TabPage BuildRemoteInventoryPreviewPage()
		{
			TabPage page = NewPreviewPage("查看别人背包");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(14);
			root.RowCount = 3;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
			page.Controls.Add(root);
			ComboBox players = new ComboBox();
			players.Dock = DockStyle.Fill;
			players.DropDownStyle = ComboBoxStyle.DropDownList;
			players.Items.Add("注入并进入多人世界后读取在线玩家");
			players.SelectedIndex = 0;
			players.Margin = new Padding(0, 6, 0, 8);
			root.Controls.Add(players, 0, 0);
			root.Controls.Add(BuildSlotPreview(10, 5), 0, 1);
			FlowLayoutPanel actions = new FlowLayoutPanel();
			actions.Dock = DockStyle.Fill;
			actions.Controls.Add(NewPreviewButton("刷新玩家背包"));
			actions.Controls.Add(NewPreviewButton("给予自己"));
			actions.Controls.Add(NewPreviewButton("在全物品中查找"));
			root.Controls.Add(actions, 0, 2);
			return page;
		}

		private TabPage BuildHistoryPreviewPage()
		{
			TabPage page = NewPreviewPage("拿出历史");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(12);
			root.RowCount = 2;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(root);
			FlowLayoutPanel toolbar = new FlowLayoutPanel();
			toolbar.Dock = DockStyle.Fill;
			toolbar.WrapContents = false;
			toolbar.Controls.Add(NewPreviewLabel("搜索记录："));
			TextBox search = new TextBox();
			search.Width = 360;
			toolbar.Controls.Add(search);
			toolbar.Controls.Add(NewPreviewButton("清除记录"));
			root.Controls.Add(toolbar, 0, 0);
			ListView history = NewPreviewList();
			history.Columns.Add("时间", 150);
			history.Columns.Add("玩家", 150);
			history.Columns.Add("ID", 90);
			history.Columns.Add("物品", 260);
			history.Columns.Add("数量", 90);
			history.Items.Add(new ListViewItem(new[] {
				"—", "预览模式", "—", "注入后记录玩家曾拿出的物品", "—"
			}));
			root.Controls.Add(history, 0, 1);
			return page;
		}

		private TabPage NewPreviewPage(string text)
		{
			TabPage page = new TabPage(text);
			page.BackColor = BackColor;
			page.ForeColor = ForeColor;
			return page;
		}

		private FlowLayoutPanel AddPreviewSection(
			TableLayoutPanel layout, string title, int column, int row, int span)
		{
			GroupBox box = new GroupBox();
			box.Text = title;
			box.Dock = DockStyle.Fill;
			box.AutoSize = true;
			box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			box.Margin = new Padding(6);
			box.Padding = new Padding(10, 8, 10, 10);
			box.ForeColor = Color.WhiteSmoke;
			box.BackColor = Color.FromArgb(34, 36, 42);
			FlowLayoutPanel content = new FlowLayoutPanel();
			content.Dock = DockStyle.Top;
			content.AutoSize = true;
			content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			content.FlowDirection = FlowDirection.TopDown;
			content.WrapContents = false;
			content.Padding = new Padding(2, 5, 2, 2);
			box.Controls.Add(content);
			layout.Controls.Add(box, column, row);
			if (span > 1)
				layout.SetColumnSpan(box, span);
			return content;
		}

		private void AddPreviewOptions(Control parent, string[] options)
		{
			foreach (string option in options)
				AddPreviewOption(parent, option);
		}

		private void AddPreviewOption(Control parent, string text)
		{
			CheckBox option = new CheckBox();
			option.Text = text;
			option.AutoSize = true;
			option.AutoCheck = false;
			option.Margin = new Padding(3, 6, 3, 6);
			option.Click += delegate {
				ShowPreviewOnlyMessage();
			};
			parent.Controls.Add(option);
		}

		private void AddPreviewValue(Control parent, string label, string value)
		{
			FlowLayoutPanel row = new FlowLayoutPanel();
			row.AutoSize = true;
			row.WrapContents = false;
			row.Margin = new Padding(24, 0, 3, 5);
			Label name = NewPreviewLabel(label + "：");
			name.Width = 130;
			TextBox input = new TextBox();
			input.Width = 130;
			input.Text = value;
			input.ReadOnly = true;
			input.BackColor = Color.FromArgb(44, 47, 54);
			input.ForeColor = Color.Gainsboro;
			row.Controls.Add(name);
			row.Controls.Add(input);
			parent.Controls.Add(row);
		}

		private Label NewPreviewNotice(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.Dock = DockStyle.Fill;
			label.AutoSize = true;
			label.Padding = new Padding(10, 9, 10, 9);
			label.Margin = new Padding(6, 10, 6, 4);
			label.BackColor = Color.FromArgb(38, 41, 48);
			label.ForeColor = Color.Gainsboro;
			return label;
		}

		private Label NewPreviewLabel(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.AutoSize = true;
			label.Height = 26;
			label.TextAlign = ContentAlignment.MiddleLeft;
			label.ForeColor = Color.Gainsboro;
			return label;
		}

		private Button NewPreviewButton(string text)
		{
			Button button = NewLauncherButton(text);
			button.AutoSize = true;
			button.MinimumSize = new Size(110, 31);
			button.Click += delegate {
				ShowPreviewOnlyMessage();
			};
			return button;
		}

		private ListView NewPreviewList()
		{
			ListView list = new ListView();
			list.Dock = DockStyle.Fill;
			list.View = View.Details;
			list.FullRowSelect = true;
			list.GridLines = true;
			list.BackColor = Color.FromArgb(36, 38, 44);
			list.ForeColor = Color.Gainsboro;
			return list;
		}

		private Control BuildSlotPreview(int columns, int rows)
		{
			TableLayoutPanel grid = new TableLayoutPanel();
			grid.Dock = DockStyle.Fill;
			grid.Padding = new Padding(10);
			grid.ColumnCount = columns;
			grid.RowCount = rows;
			for (int x = 0; x < columns; x++)
				grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
			for (int y = 0; y < rows; y++)
				grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
			for (int index = 0; index < columns * rows; index++) {
				Label slot = new Label();
				slot.Text = (index + 1).ToString();
				slot.Dock = DockStyle.Fill;
				slot.Margin = new Padding(2);
				slot.TextAlign = ContentAlignment.MiddleCenter;
				slot.BackColor = Color.FromArgb(45, 48, 56);
				slot.ForeColor = Color.Silver;
				slot.BorderStyle = BorderStyle.FixedSingle;
				grid.Controls.Add(slot, index % columns, index / columns);
			}
			return grid;
		}

		private static Button NewLauncherButton(string text)
		{
			Button button = new Button();
			button.Text = text;
			button.UseVisualStyleBackColor = false;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderColor = Color.FromArgb(110, 125, 156);
			button.FlatAppearance.MouseOverBackColor = Color.FromArgb(69, 84, 112);
			button.FlatAppearance.MouseDownBackColor = Color.FromArgb(82, 100, 136);
			button.FlatAppearance.CheckedBackColor = Color.FromArgb(82, 100, 136);
			button.BackColor = Color.FromArgb(54, 61, 76);
			button.ForeColor = Color.White;
			return button;
		}

		private void ShowPreviewOnlyMessage()
		{
			_statusLabel.Text =
				"当前是离线预览；检测到游戏后会自动注入，功能即可在游戏内操作。";
		}

		private void QueueGameScan(bool report)
		{
			if (_injecting || _scanInProgress || IsDisposed)
				return;
			_scanInProgress = true;
			ThreadPool.QueueUserWorkItem(delegate {
				TargetChoice[] targets = null;
				Exception scanError = null;
				try {
					targets = FindTargets();
				}
				catch (Exception ex) {
					scanError = ex;
				}
				try {
					BeginInvoke(new Action(delegate {
						ApplyGameScanResult(targets, scanError, report);
					}));
				}
				catch {
				}
			});
		}

		private void ApplyGameScanResult(
			TargetChoice[] targets, Exception scanError, bool report)
		{
			_scanInProgress = false;
			if (IsDisposed || _injecting)
				return;

			if (scanError != null) {
				_connectionLabel.Text = "● 游戏扫描发生错误";
				_connectionLabel.ForeColor = Color.OrangeRed;
				_statusLabel.Text = scanError.Message;
				_retryButton.Enabled = true;
				WriteStableLog("scan-error: " + scanError);
				return;
			}

			if (targets == null || targets.Length == 0) {
				_connectionLabel.Text = "● 预览模式：等待 Terraria / tModLoader";
				_connectionLabel.ForeColor = Color.Silver;
				_retryButton.Enabled = true;
				if (report)
					_statusLabel.Text = "未检测到游戏；功能页保持可见，游戏启动后将自动注入。";
				return;
			}

			TargetChoice target = targets[0];
			_connectionLabel.Text =
				"● 已检测到 " + (target.UseTml ? "tModLoader" : "Terraria 原版") +
				"（PID " + target.ProcessId + "）";
			_connectionLabel.ForeColor = Color.LightGreen;
			if (string.Equals(
				target.Key, _lastAttemptKey, StringComparison.Ordinal)) {
				_retryButton.Enabled = true;
				return;
			}
			BeginAutomaticInjection(target);
		}

		private void BeginAutomaticInjection(TargetChoice target)
		{
			if (target == null || _injecting)
				return;
			_lastAttemptKey = target.Key;
			_injecting = true;
			_retryButton.Enabled = false;
			_connectionLabel.Text =
				"● 正在自动注入 " + (target.UseTml ? "tModLoader" : "Terraria 原版") +
				"（PID " + target.ProcessId + "）";
			_connectionLabel.ForeColor = Color.LightSkyBlue;
			_statusLabel.Text = "检测到游戏，正在自动加载修改器，请稍候……";

			ThreadPool.QueueUserWorkItem(delegate {
				InjectionResult result;
				try {
					result = InjectTarget(target);
				}
				catch (Exception ex) {
					WriteStableLog("inject-error: " + ex);
					result = new InjectionResult {
						Success = false,
						Message = ex.Message
					};
				}
				try {
					BeginInvoke(new Action(delegate {
						CompleteAutomaticInjection(result);
					}));
				}
				catch {
				}
			});
		}

		private void CompleteAutomaticInjection(InjectionResult result)
		{
			_injecting = false;
			if (result != null && result.Success) {
				_connectionLabel.Text = "● 自动注入成功";
				_connectionLabel.ForeColor = Color.LightGreen;
				_statusLabel.Text = result.Message;
				Close();
				return;
			}
			_connectionLabel.Text = "● 自动注入失败，可点击“重新扫描”重试";
			_connectionLabel.ForeColor = Color.OrangeRed;
			_statusLabel.Text = result == null
				? "注入后端没有返回结果。"
				: result.Message;
			_retryButton.Enabled = true;
		}
	}

	private static readonly Payload[] Payloads = {
		new Payload("payload.vanilla.injector", "VanillaInjector.x86.exe"),
		new Payload("payload.vanilla.native", "Terraria1456Toolkit.Bootstrap.dll"),
		new Payload("payload.vanilla.managed", "Terraria1456Toolkit.Managed.dll"),
		new Payload("payload.tml.injector", "TmlInjector.x64.exe"),
		new Payload("payload.tml.native", "TerrariaTmlToolkit.Native.dll"),
		new Payload("payload.tml.bootstrap", "TerrariaTmlToolkit.Bootstrap.dll"),
		new Payload("payload.tml.ui", "TerrariaTmlToolkit.UI.dll"),
		new Payload("payload.tml.config", "TerrariaTmlToolkit.runtimeconfig.json"),
		new Payload("payload.tml.hostfxr", "hostfxr.dll")
	};

	[STAThread]
	private static void Main()
	{
		bool ownsLauncherMutex = false;
		using (Mutex launcherMutex = new Mutex(false, @"Local\HoamTerrariaToolkit.CombinedLauncher")) {
			try {
				try {
					ownsLauncherMutex = launcherMutex.WaitOne(0, false);
				}
				catch (AbandonedMutexException) {
					ownsLauncherMutex = true;
				}
				if (!ownsLauncherMutex) {
					MessageBox.Show(
						"另一个修改器启动实例正在运行，请等待它完成。",
						"Terraria 全物品修改器",
						MessageBoxButtons.OK,
						MessageBoxIcon.Information);
					return;
				}

				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new LauncherForm());
			}
			catch (Exception ex) {
				WriteStableLog("unhandled: " + ex);
				MessageBox.Show(ex.Message, "修改器启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally {
				if (ownsLauncherMutex) {
					try { launcherMutex.ReleaseMutex(); }
					catch { }
				}
			}
		}
	}

	private static InjectionResult InjectTarget(TargetChoice selection)
	{
		if (selection == null)
			throw new ArgumentNullException("selection");

		using (Process target = Process.GetProcessById(selection.ProcessId)) {
			if (target.HasExited)
				throw new InvalidOperationException("目标游戏进程已经退出。");
			if (selection.UseTml) {
				if (!IsTmlProcess(target) || IsLikelyTmlServer(target))
					throw new InvalidOperationException(
						"所选进程已经不再是可用的 tModLoader 客户端，请刷新目标列表。");
			}
			else {
				string processName;
				try { processName = target.ProcessName; }
				catch { processName = string.Empty; }
				if (!string.Equals(
					processName, "Terraria", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException(
						"所选进程已经不再是 Terraria 原版客户端，请刷新目标列表。");
			}

			string runtime = ExtractPayloads();
			string backend = selection.UseTml ? "tml" : "vanilla";
			string helper = Path.Combine(
				runtime,
				selection.UseTml ? "TmlInjector.x64.exe" : "VanillaInjector.x86.exe");
			string helperLog = Path.Combine(
				runtime, backend + "_injector_" + target.Id + ".log");
			string arguments =
				"--silent --pid " + target.Id +
				" --log " + QuoteArgument(helperLog);

			WriteStableLog(
				"launch backend=" + backend +
				" target=" + target.Id +
				" runtime=" + runtime);
			ProcessStartInfo start = new ProcessStartInfo(helper) {
				Arguments = arguments,
				WorkingDirectory = runtime,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			int exitCode;
			using (Process child = Process.Start(start)) {
				if (child == null)
					throw new InvalidOperationException("无法启动注入后端。");
				if (!child.WaitForExit(40000)) {
					WriteStableLog(
						"helper-timeout backend=" + backend +
						" target=" + target.Id +
						" (no retry, helper left running)");
					throw new TimeoutException(
						"注入后端在 40 秒内没有退出。为保护游戏，本次不会重试注入。\n\n日志：" +
						helperLog);
				}
				exitCode = child.ExitCode;
			}

			WriteStableLog(
				"helper-exit backend=" + backend +
					" target=" + target.Id +
					" code=" + exitCode);
			if (exitCode == 0) {
				return new InjectionResult {
					Success = true,
					Message = "注入成功，游戏内修改器界面已经启动。"
				};
			}
			if (exitCode == 11) {
				return new InjectionResult {
					Success = false,
					Message =
						"修改器已经加载到该游戏进程，本次已阻止重复注入。" +
						"如果界面没有出现，请重启游戏后重试。\n日志：" + helperLog
				};
			}
			if (exitCode == 12) {
				return new InjectionResult {
					Success = false,
					Message = "另一个实例正在向这个游戏进程加载修改器，请等待。"
				};
			}

			string lastLog = ReadLogTail(helperLog, 2200);
			throw new InvalidOperationException(
				"加载后端失败（代码 " + exitCode + "）。\n\n" +
				(string.IsNullOrWhiteSpace(lastLog)
					? string.Empty
					: lastLog + "\n\n") +
				"完整日志：" + helperLog);
		}
	}

	private static TargetChoice[] FindTargets()
	{
		List<TargetChoice> targets = new List<TargetChoice>();
		uint foregroundPid = 0;
		try {
			IntPtr foreground = GetForegroundWindow();
			if (foreground != IntPtr.Zero)
				GetWindowThreadProcessId(foreground, out foregroundPid);
		}
		catch {
		}

		foreach (Process process in Process.GetProcesses()) {
			try {
				if (process.HasExited)
					continue;

				string processName;
				try { processName = process.ProcessName; }
				catch { processName = string.Empty; }

				if (string.Equals(
					processName, "Terraria", StringComparison.OrdinalIgnoreCase)) {
					targets.Add(new TargetChoice {
						ProcessId = process.Id,
						UseTml = false,
						Score = (process.Id == foregroundPid ? 200000 : 0) +
							(process.MainWindowHandle != IntPtr.Zero ? 100000 : 0),
						Title = SafeWindowTitle(process)
					});
					continue;
				}

				bool possibleTml =
					string.Equals(
						processName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
					processName.IndexOf(
						"tModLoader", StringComparison.OrdinalIgnoreCase) >= 0;
				if (possibleTml &&
					IsTmlProcess(process) &&
					!IsLikelyTmlServer(process)) {
					targets.Add(new TargetChoice {
						ProcessId = process.Id,
						UseTml = true,
						Score = ScoreTmlClient(process, foregroundPid),
						Title = SafeWindowTitle(process)
					});
				}
			}
			catch {
			}
			finally {
				process.Dispose();
			}
		}

		return targets
			.OrderByDescending(delegate(TargetChoice target) {
				return target.Score;
			})
			.ThenByDescending(delegate(TargetChoice target) {
				return target.ProcessId;
			})
			.ToArray();
	}

	private static bool IsTmlProcess(Process process)
	{
		if (process == null)
			return false;
		try { if (process.HasExited) return false; }
		catch { return false; }
		try {
			foreach (ProcessModule module in process.Modules) {
				try {
					string name = Path.GetFileName(module.FileName);
					if (string.Equals(name, "tModLoader.dll",
						StringComparison.OrdinalIgnoreCase) ||
						string.Equals(name, "tModLoader.exe",
						StringComparison.OrdinalIgnoreCase))
						return true;
				}
				catch { }
			}
		}
		catch { }
		string title = SafeWindowTitle(process);
		return title.IndexOf(
			"tModLoader", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsLikelyTmlServer(Process process)
	{
		string name;
		try { name = process.ProcessName; }
		catch { name = string.Empty; }
		string title = SafeWindowTitle(process);
		if (name.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0 ||
			title.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0 ||
			title.IndexOf("dedicated", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		try {
			foreach (ProcessModule module in process.Modules) {
				string moduleName;
				try { moduleName = Path.GetFileName(module.FileName); }
				catch { continue; }
				if (moduleName.IndexOf(
					"tModLoaderServer", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}
		}
		catch { }
		return false;
	}

	private static int ScoreTmlClient(Process process, uint foregroundPid)
	{
		int score = 0;
		try {
			if (process.MainWindowHandle != IntPtr.Zero)
				score += 100000;
		}
		catch { }
		string title = SafeWindowTitle(process);
		if (title.Length > 0)
			score += 10000;
		if (title.IndexOf("tModLoader", StringComparison.OrdinalIgnoreCase) >= 0)
			score += 5000;
		if (title.IndexOf("Terraria", StringComparison.OrdinalIgnoreCase) >= 0)
			score += 2500;
		if (foregroundPid != 0 && process.Id == foregroundPid)
			score += 200000;
		return score;
	}

	private static string SafeWindowTitle(Process process)
	{
		try { return process.MainWindowTitle ?? string.Empty; }
		catch { return string.Empty; }
	}

	private static string ExtractPayloads()
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		// The MVID changes on every launcher rebuild.  A new build therefore gets
		// an immutable payload directory instead of silently reusing stale DLLs.
		string buildId = assembly.ManifestModule.ModuleVersionId.ToString("N");
		string directory = Path.Combine(Path.GetTempPath(), "HoamTerrariaToolkit", buildId);
		Directory.CreateDirectory(directory);

		using (Mutex extractMutex = new Mutex(false, @"Local\HoamTerrariaToolkit.Extract." + buildId)) {
			bool acquired = false;
			try {
				try {
					acquired = extractMutex.WaitOne(30000, false);
				}
				catch (AbandonedMutexException) {
					acquired = true;
				}
				if (!acquired)
					throw new TimeoutException("等待修改器组件解压超时。");

				foreach (Payload payload in Payloads)
					ExtractPayload(assembly, directory, payload);
			}
			finally {
				if (acquired) {
					try { extractMutex.ReleaseMutex(); }
					catch { }
				}
			}
		}
		return directory;
	}

	private static void ExtractPayload(Assembly assembly, string directory, Payload payload)
	{
		byte[] expected;
		using (Stream source = assembly.GetManifestResourceStream(payload.Resource)) {
			if (source == null)
				throw new InvalidDataException("内置组件缺失：" + payload.Resource);
			using (MemoryStream memory = new MemoryStream()) {
				source.CopyTo(memory);
				expected = memory.ToArray();
			}
		}

		string destination = Path.Combine(directory, payload.FileName);
		if (File.Exists(destination) && FilesEqual(destination, expected))
			return;

		string temporary = destination + ".tmp." + Process.GetCurrentProcess().Id;
		using (FileStream target = new FileStream(
			temporary,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			81920,
			FileOptions.WriteThrough)) {
			target.Write(expected, 0, expected.Length);
			target.Flush();
		}

		if (File.Exists(destination))
			File.Delete(destination);
		File.Move(temporary, destination);
	}

	private static bool FilesEqual(string path, byte[] expected)
	{
		try {
			byte[] actual = File.ReadAllBytes(path);
			if (actual.Length != expected.Length)
				return false;
			for (int i = 0; i < actual.Length; i++) {
				if (actual[i] != expected[i])
					return false;
			}
			return true;
		}
		catch {
			return false;
		}
	}

	private static string QuoteArgument(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
	}

	private static void WriteStableLog(string message)
	{
		try {
			string root = Path.Combine(Path.GetTempPath(), "HoamTerrariaToolkit");
			Directory.CreateDirectory(root);
			File.AppendAllText(
				Path.Combine(root, "launcher.log"),
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
				" [pid " + Process.GetCurrentProcess().Id + "] " + message + Environment.NewLine,
				Encoding.UTF8);
		}
		catch {
		}
	}

	private static string ReadLogTail(string path, int maxCharacters)
	{
		try {
			if (!File.Exists(path))
				return string.Empty;
			string text = File.ReadAllText(path);
			return text.Length <= maxCharacters
				? text
				: text.Substring(text.Length - maxCharacters);
		}
		catch {
			return string.Empty;
		}
	}
}
