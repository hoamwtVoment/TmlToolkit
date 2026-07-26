using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Terraria1456Toolkit
{
	internal sealed class ItemEntry
	{
		public int Id { get; set; }
		public string InternalName { get; set; }
		public string ChineseName { get; set; }
		public string EnglishName { get; set; }
	}

	internal sealed class InventoryEntry
	{
		public int Slot { get; set; }
		public int Id { get; set; }
		public string Name { get; set; }
		public int Stack { get; set; }
		public int Prefix { get; set; }
	}

	internal sealed class HistoryEntry
	{
		public string Time { get; set; }
		public string Player { get; set; }
		public int Id { get; set; }
		public string Name { get; set; }
		public string InternalName { get; set; }
		public int Stack { get; set; }
	}

	public sealed class TrainerForm : Form
	{
		private readonly List<ItemEntry> _allItems = new List<ItemEntry>();
		private readonly Dictionary<int, ItemEntry> _itemsById = new Dictionary<int, ItemEntry>();
		private readonly BindingList<ItemEntry> _filteredItems = new BindingList<ItemEntry>();
		private readonly BindingList<InventoryEntry> _inventory = new BindingList<InventoryEntry>();
		private readonly BindingList<HistoryEntry> _history = new BindingList<HistoryEntry>();
		private readonly Dictionary<int, int> _lastHeld = new Dictionary<int, int>();
		private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
		private readonly VanillaGameApi _game;

		private DataGridView _itemGrid;
		private Label _itemResultLabel;
		private InventorySlotGrid _inventoryGrid;
		private InventorySlotGrid _editorInventoryGrid;
		private DataGridView _historyGrid;
		private TabControl _tabs;
		private TabPage _itemsPage;
		private ComboBox _remotePlayerBox;
		private InventorySlotGrid _remoteInventoryGrid;
		private Label _remoteInventoryInfo;
		private Label _remoteSelectedInfo;
		private NumericUpDown _remoteGiveAmount;
		private Button _remoteGiveButton;
		private Button _remoteFindButton;
		private Button _remoteCopyButton;
		private TextBox _searchBox;
		private TextBox _historySearch;
		private NumericUpDown _stackBox;
		private Label _selectedLabel;
		private Label _statusLabel;
		private Label _itemEditorSelectionLabel;
		private Label _itemEditorCompatibilityLabel;
		private readonly Dictionary<string, Control> _itemAttributeEditors =
			new Dictionary<string, Control>(StringComparer.Ordinal);
		private ItemEntry _selectedItem;
		private int _selectedSlot = -1;
		private int _editorSelectedSlot = -1;
		private int _selectedRemotePlayerIndex = -1;
		private int _selectedRemoteSlot = -1;
		private int _slowTick;
		private bool _refreshingRemotePlayers;
		private readonly List<RemoteInventorySnapshot> _remoteInventories =
			new List<RemoteInventorySnapshot>();
		private readonly NotifyIcon _trayIcon;
		private bool _allowClose;

		private CheckBox _godMode;
		private CheckBox _infiniteLife;
		private CheckBox _infiniteMana;
		private CheckBox _infiniteBreath;
		private CheckBox _noPotionCooldown;
		private CheckBox _clearDebuffs;
		private CheckBox _noKnockback;
		private CheckBox _noFallDamage;
		private CheckBox _lavaImmune;
		private CheckBox _fastMovement;
		private NumericUpDown _fastMovementMultiplier;
		private CheckBox _highJump;
		private NumericUpDown _highJumpMultiplier;
		private CheckBox _instantRespawn;
		private CheckBox _adjustableStep;
		private NumericUpDown _stepBlocks;
		private CheckBox _infiniteFlight;
		private CheckBox _fullBright;
		private CheckBox _unrestrictedView;
		private CheckBox _concurrentAttack;
		private Button _mapRevealButton;
		private Label _mapRevealStatusLabel;

		public TrainerForm()
		{
			Text = "Terraria 1.4.5.6 全物品修改器";
			Size = new Size(1280, 760);
			MinimumSize = new Size(1100, 650);
			StartPosition = FormStartPosition.CenterScreen;
			Font = new Font("Microsoft YaHei UI", 9F);
			BackColor = Color.FromArgb(31, 33, 38);
			ForeColor = Color.WhiteSmoke;

			_game = VanillaGameApi.Connect();
			LoadCatalog();
			BuildUi();

			_trayIcon = new NotifyIcon();
			_trayIcon.Icon = SystemIcons.Application;
			_trayIcon.Text = "Terraria 1.4.5.6 全物品修改器";
			_trayIcon.Visible = true;
			_trayIcon.DoubleClick += delegate { ShowFromTray(); };
			ContextMenuStrip trayMenu = new ContextMenuStrip();
			trayMenu.Items.Add("显示修改器", null, delegate { ShowFromTray(); });
			trayMenu.Items.Add("退出界面（重新打开需重启游戏）", null, delegate {
				_allowClose = true;
				_trayIcon.Visible = false;
				Close();
			});
			_trayIcon.ContextMenuStrip = trayMenu;
			FormClosing += delegate(object sender, FormClosingEventArgs args) {
				if (!_allowClose && args.CloseReason == CloseReason.UserClosing) {
					args.Cancel = true;
					Hide();
					_trayIcon.ShowBalloonTip(1500, "修改器仍在运行", "双击托盘图标可以重新显示。", ToolTipIcon.Info);
				}
			};
			FormClosed += delegate {
				_timer.Stop();
				_game.DetachHooks();
			};

			// Do not subscribe to a Terraria game-loop event until every control
			// has been built successfully.  A constructor failure must not leave
			// a callback rooted inside the game process.
			_game.AttachHooks();
			_timer.Interval = 16;
			_timer.Tick += TimerTick;
			_timer.Start();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				try {
					_timer.Stop();
					_timer.Dispose();
				}
				catch {
				}
				try {
					if (_game != null)
						_game.DetachHooks();
				}
				catch {
				}
				try {
					if (_trayIcon != null) {
						_trayIcon.Visible = false;
						_trayIcon.Dispose();
					}
				}
				catch {
				}
			}
			base.Dispose(disposing);
		}

		internal void RestoreFromExternalLaunch()
		{
			ShowFromTray();
		}

		private void ShowFromTray()
		{
			Show();
			WindowState = FormWindowState.Normal;
			Activate();
		}

		private void BuildUi()
		{
			_tabs = new TabControl();
			_tabs.Dock = DockStyle.Fill;
			_tabs.Controls.Add(BuildBasicPage());
			_itemsPage = BuildItemsPage();
			_tabs.Controls.Add(_itemsPage);
			_tabs.Controls.Add(BuildItemEditorPage());
			_tabs.Controls.Add(BuildRemoteInventoryPage());
			_tabs.Controls.Add(BuildHistoryPage());
			Controls.Add(_tabs);

			_statusLabel = new Label();
			_statusLabel.Dock = DockStyle.Bottom;
			_statusLabel.Height = 28;
			_statusLabel.Padding = new Padding(8, 5, 0, 0);
			_statusLabel.Text = "已连接进程内 Terraria " + _game.Version + "；物品数据库：" + _allItems.Count + " 项";
			Controls.Add(_statusLabel);
		}

		private TabPage BuildBasicPage()
		{
			TabPage page = NewPage("基本修改");
			FlowLayoutPanel flow = new FlowLayoutPanel();
			flow.Dock = DockStyle.Fill;
			flow.FlowDirection = FlowDirection.TopDown;
			flow.WrapContents = false;
			flow.AutoScroll = true;
			flow.Padding = new Padding(22);
			page.Controls.Add(flow);

			Label title = new Label();
			title.Text = "基础修改功能（直接适配 1.4.5.6 玩家字段）";
			title.Font = new Font(Font, FontStyle.Bold);
			title.AutoSize = true;
			title.Margin = new Padding(3, 3, 3, 15);
			flow.Controls.Add(title);

			_godMode = AddOption(flow, "无敌（旅途上帝模式 + 免伤 + 自动补满）");
			_infiniteLife = AddOption(flow, "生命锁定（每帧回满）");
			_instantRespawn = AddOption(flow, "秒重生（死亡后下一帧复活）");
			_infiniteMana = AddOption(flow, "无限魔力");
			_infiniteBreath = AddOption(flow, "无限氧气");
			_noPotionCooldown = AddOption(flow, "无药水冷却");
			_clearDebuffs = AddOption(flow, "清除负面状态");
			_noKnockback = AddOption(flow, "免疫击退");
			_noFallDamage = AddOption(flow, "免疫摔落伤害");
			_lavaImmune = AddOption(flow, "熔岩免疫");
			_fastMovement = AddAdjustableOption(
				flow,
				"高速移动",
				"移动倍率",
				3.0M,
				out _fastMovementMultiplier);
			_highJump = AddAdjustableOption(
				flow,
				"高跳",
				"高度倍率",
				2.5M,
				out _highJumpMultiplier);
			_adjustableStep = AddStepOption(
				flow,
				"可调 Step",
				"最大整格数",
				3M,
				out _stepBlocks);
			_infiniteFlight = AddOption(flow, "无限翅膀/火箭时间");
			_fullBright = AddOption(
				flow,
				"场景全亮（Fullbright，不再受地下黑暗影响）");
			_unrestrictedView = AddOption(
				flow,
				"解除放大/缩小按键限制（使用游戏原有 ZoomIn / ZoomOut 键）");
			_concurrentAttack = AddOption(
				flow,
				"并发攻击（松开后再次按下可提前触发，已有弹射物不会清除）");

			_mapRevealButton = new Button();
			_mapRevealButton.Text = "点亮整个地图";
			_mapRevealButton.AutoSize = true;
			_mapRevealButton.MinimumSize = new Size(150, 32);
			_mapRevealButton.Margin = new Padding(3, 12, 3, 2);
			_mapRevealButton.Click += delegate {
				if (_game.IsMapRevealActive) {
					_game.CancelMapReveal();
					_mapRevealButton.Text = "正在停止……";
				}
				else {
					_game.RequestMapReveal();
					_mapRevealButton.Text = "取消点亮地图";
					_mapRevealStatusLabel.Text = "已提交到游戏主线程……";
				}
			};
			flow.Controls.Add(_mapRevealButton);

			_mapRevealStatusLabel = new Label();
			_mapRevealStatusLabel.AutoSize = true;
			_mapRevealStatusLabel.ForeColor = Color.Gainsboro;
			_mapRevealStatusLabel.Text =
				"地图点亮会分批扫描当前世界，不会卡住界面。";
			_mapRevealStatusLabel.Margin = new Padding(3, 2, 3, 3);
			flow.Controls.Add(_mapRevealStatusLabel);

			Label note = new Label();
			note.AutoSize = true;
			note.MaximumSize = new Size(780, 0);
			note.Margin = new Padding(3, 18, 3, 3);
			note.ForeColor = Color.Gainsboro;
			note.Text = "这些选项不再使用旧修改器的代码特征扫描，因此不会因为 1.4.5.6 更新后指令地址变化而自动失效。并发攻击会正常消耗弹药/魔力。";
			flow.Controls.Add(note);
			return page;
		}

		private TabPage BuildItemsPage()
		{
			TabPage page = NewPage("包裹 / 全物品");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Margin = Padding.Empty;
			root.Padding = new Padding(4);
			root.ColumnCount = 2;
			root.RowCount = 1;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.BackColor = Color.FromArgb(45, 47, 54);
			page.Controls.Add(root);

			TableLayoutPanel left = new TableLayoutPanel();
			left.Dock = DockStyle.Fill;
			left.Margin = new Padding(0, 0, 4, 0);
			left.ColumnCount = 1;
			left.RowCount = 2;
			left.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
			left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.Controls.Add(left, 0, 0);

			TableLayoutPanel searchHeader = new TableLayoutPanel();
			searchHeader.Dock = DockStyle.Fill;
			searchHeader.Margin = Padding.Empty;
			searchHeader.Padding = new Padding(8, 5, 8, 4);
			searchHeader.ColumnCount = 2;
			searchHeader.RowCount = 3;
			searchHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			searchHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
			searchHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
			searchHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
			searchHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
			left.Controls.Add(searchHeader, 0, 0);

			Label searchLabel = new Label();
			searchLabel.Text = "搜索 ID / 中文名 / 英文名 / 内部名：";
			searchLabel.Dock = DockStyle.Fill;
			searchLabel.TextAlign = ContentAlignment.MiddleLeft;
			searchHeader.Controls.Add(searchLabel, 0, 0);
			searchHeader.SetColumnSpan(searchLabel, 2);

			_searchBox = new TextBox();
			_searchBox.Dock = DockStyle.Fill;
			_searchBox.Margin = new Padding(2, 2, 8, 4);
			_searchBox.TextChanged += delegate { ApplyItemFilter(); };
			searchHeader.Controls.Add(_searchBox, 0, 1);

			Button clearSearch = NewButton("清空搜索", 0, 0, 96);
			clearSearch.Dock = DockStyle.Fill;
			clearSearch.Margin = new Padding(0, 0, 0, 3);
			clearSearch.Click += delegate {
				_searchBox.Clear();
				_searchBox.Focus();
			};
			searchHeader.Controls.Add(clearSearch, 1, 1);

			_selectedLabel = new Label();
			_selectedLabel.Text = "未选择物品";
			_selectedLabel.Dock = DockStyle.Fill;
			_selectedLabel.AutoEllipsis = true;
			_selectedLabel.TextAlign = ContentAlignment.MiddleLeft;
			_selectedLabel.Margin = new Padding(2, 1, 6, 0);
			_selectedLabel.ForeColor = Color.Gold;
			searchHeader.Controls.Add(_selectedLabel, 0, 2);

			_itemResultLabel = new Label();
			_itemResultLabel.Dock = DockStyle.Fill;
			_itemResultLabel.TextAlign = ContentAlignment.MiddleRight;
			_itemResultLabel.ForeColor = Color.Gainsboro;
			_itemResultLabel.Margin = Padding.Empty;
			searchHeader.Controls.Add(_itemResultLabel, 1, 2);

			_itemGrid = CreateGrid();
			_itemGrid.Dock = DockStyle.Fill;
			_itemGrid.Margin = Padding.Empty;
			_itemGrid.DataSource = _filteredItems;
			AddTextColumn(_itemGrid, "ID", "Id", 70);
			AddTextColumn(_itemGrid, "中文名", "ChineseName", 210);
			AddTextColumn(_itemGrid, "英文名", "EnglishName", 185);
			AddTextColumn(_itemGrid, "内部名", "InternalName", 185);
			_itemGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
			_itemGrid.Columns[1].MinimumWidth = 150;
			_itemGrid.SelectionChanged += ItemSelectionChanged;
			_itemGrid.CellDoubleClick += delegate { GiveSelectedItem(); };
			left.Controls.Add(_itemGrid, 0, 1);

			TableLayoutPanel right = new TableLayoutPanel();
			right.Dock = DockStyle.Fill;
			right.Margin = new Padding(4, 0, 0, 0);
			right.ColumnCount = 1;
			right.RowCount = 2;
			right.RowStyles.Add(new RowStyle(SizeType.Absolute, 166F));
			right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.Controls.Add(right, 1, 0);

			Panel rightTop = new Panel();
			rightTop.Dock = DockStyle.Fill;
			right.Controls.Add(rightTop, 0, 0);
			rightTop.Controls.Add(NewLabel("数量：", 10, 12, 60));
			_stackBox = new NumericUpDown();
			_stackBox.SetBounds(70, 9, 100, 27);
			_stackBox.Minimum = 1;
			_stackBox.Maximum = 9999;
			_stackBox.Value = 1;
			rightTop.Controls.Add(_stackBox);

			Button give = NewButton("获取到背包", 10, 48, 145);
			give.Click += delegate { GiveSelectedItem(); };
			rightTop.Controls.Add(give);
			Button replace = NewButton("替换选中槽位", 165, 48, 145);
			replace.Click += delegate { ReplaceSelectedSlot(); };
			rightTop.Controls.Add(replace);
			Button clear = NewButton("清空选中槽位", 10, 87, 145);
			clear.Click += delegate { ClearSelectedSlot(); };
			rightTop.Controls.Add(clear);
			Button refresh = NewButton("刷新包裹", 165, 87, 145);
			refresh.Click += delegate { RefreshInventory(); };
			rightTop.Controls.Add(refresh);
			Label slotHint = NewLabel("点击下方槽位选择；金框为当前槽位。“获取”仍会自动加入背包。", 10, 128, 520);
			slotHint.AutoEllipsis = true;
			rightTop.Controls.Add(slotHint);

			_inventoryGrid = new InventorySlotGrid();
			_inventoryGrid.Dock = DockStyle.Fill;
			_inventoryGrid.Margin = new Padding(6);
			_inventoryGrid.IconProvider = _game.GetItemIcon;
			_inventoryGrid.SelectedSlotChanged += InventorySelectionChanged;
			right.Controls.Add(_inventoryGrid, 0, 1);

			ApplyItemFilter();
			RefreshInventory();
			return page;
		}

		private TabPage BuildItemEditorPage()
		{
			TabPage page = NewPage("物品属性编辑");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(6);
			root.ColumnCount = 2;
			root.RowCount = 1;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(root);

			TableLayoutPanel inventoryPanel = new TableLayoutPanel();
			inventoryPanel.Dock = DockStyle.Fill;
			inventoryPanel.Margin = new Padding(0, 0, 6, 0);
			inventoryPanel.ColumnCount = 1;
			inventoryPanel.RowCount = 2;
			inventoryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
			inventoryPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.Controls.Add(inventoryPanel, 0, 0);

			Panel inventoryHeader = new Panel();
			inventoryHeader.Dock = DockStyle.Fill;
			inventoryPanel.Controls.Add(inventoryHeader, 0, 0);
			Label editorTitle = NewLabel(
				"选择下方背包槽位，再读取属性。写回操作会在 Terraria 游戏线程执行。",
				8,
				7,
				510);
			editorTitle.AutoEllipsis = true;
			inventoryHeader.Controls.Add(editorTitle);
			_itemEditorSelectionLabel = NewLabel("未选择槽位", 8, 40, 510);
			_itemEditorSelectionLabel.ForeColor = Color.Gold;
			_itemEditorSelectionLabel.AutoEllipsis = true;
			inventoryHeader.Controls.Add(_itemEditorSelectionLabel);

			_editorInventoryGrid = new InventorySlotGrid();
			_editorInventoryGrid.Dock = DockStyle.Fill;
			_editorInventoryGrid.Margin = Padding.Empty;
			_editorInventoryGrid.IconProvider = _game.GetItemIcon;
			_editorInventoryGrid.SelectedSlotChanged += EditorInventorySelectionChanged;
			_editorInventoryGrid.SetItems(_inventory);
			inventoryPanel.Controls.Add(_editorInventoryGrid, 0, 1);

			TableLayoutPanel editorPanel = new TableLayoutPanel();
			editorPanel.Dock = DockStyle.Fill;
			editorPanel.Margin = new Padding(6, 0, 0, 0);
			editorPanel.ColumnCount = 1;
			editorPanel.RowCount = 3;
			editorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
			editorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			editorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
			root.Controls.Add(editorPanel, 1, 0);

			Label editorHelp = new Label();
			editorHelp.Dock = DockStyle.Fill;
			editorHelp.Padding = new Padding(6, 4, 6, 0);
			editorHelp.Text =
				"先读取，再修改并写回。缺失字段会自动禁用；修改 type 时先生成该物品默认值，再覆盖下面的自定义属性。";
			editorHelp.AutoEllipsis = true;
			editorPanel.Controls.Add(editorHelp, 0, 0);

			Panel scrollHost = new Panel();
			scrollHost.Dock = DockStyle.Fill;
			scrollHost.AutoScroll = true;
			editorPanel.Controls.Add(scrollHost, 0, 1);

			TableLayoutPanel fields = new TableLayoutPanel();
			fields.Dock = DockStyle.Top;
			fields.AutoSize = true;
			fields.Padding = new Padding(5, 2, 5, 5);
			fields.ColumnCount = 4;
			fields.RowCount = 11;
			fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19F));
			fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
			fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19F));
			fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
			for (int row = 0; row < 11; row++)
				fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			scrollHost.Controls.Add(fields);

			AddNumericItemAttribute(
				fields,
				0,
				0,
				"type",
				"物品 ID",
				0M,
				Math.Max(0, _game.MaxItemType),
				0,
				1M);
			AddNumericItemAttribute(fields, 0, 2, "stack", "数量", 0M, 999999M, 0, 1M);
			AddNumericItemAttribute(fields, 1, 0, "prefix", "前缀 ID", 0M, 255M, 0, 1M);
			AddNumericItemAttribute(fields, 1, 2, "damage", "伤害", -100000M, 1000000M, 0, 1M);
			AddNumericItemAttribute(fields, 2, 0, "knockBack", "击退", -10000M, 10000M, 2, 0.1M);
			AddNumericItemAttribute(fields, 2, 2, "useTime", "使用时间", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 3, 0, "useAnimation", "使用动画", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 3, 2, "scale", "大小", 0M, 100M, 3, 0.05M);
			AddNumericItemAttribute(fields, 4, 0, "shootSpeed", "弹速", -10000M, 10000M, 2, 0.25M);
			AddNumericItemAttribute(fields, 4, 2, "shoot", "弹射物 ID", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 5, 0, "useAmmo", "弹药 ID", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 5, 2, "pick", "镐力", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 6, 0, "axe", "斧力", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 6, 2, "hammer", "锤力", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 7, 0, "fishingPole", "渔力", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 7, 2, "defense", "防御", -100000M, 1000000M, 0, 1M);
			AddNumericItemAttribute(fields, 8, 0, "crit", "暴击", -100000M, 1000000M, 0, 1M);
			AddNumericItemAttribute(fields, 8, 2, "mana", "耗魔", 0M, 100000M, 0, 1M);
			AddNumericItemAttribute(fields, 9, 0, "healLife", "生命回复", -100000M, 1000000M, 0, 1M);
			AddNumericItemAttribute(fields, 9, 2, "healMana", "魔力回复", -100000M, 1000000M, 0, 1M);
			AddBooleanItemAttribute(fields, 10, 0, "autoReuse", "自动连用");

			Panel actions = new Panel();
			actions.Dock = DockStyle.Fill;
			editorPanel.Controls.Add(actions, 0, 2);
			Button read = NewButton("读取选中槽位", 8, 7, 145);
			read.Click += delegate { ReadSelectedItemAttributes(); };
			actions.Controls.Add(read);
			Button write = NewButton("写回选中槽位", 164, 7, 145);
			write.Click += delegate { WriteSelectedItemAttributes(); };
			actions.Controls.Add(write);
			Button refresh = NewButton("刷新背包", 320, 7, 110);
			refresh.Click += delegate { RefreshInventory(); };
			actions.Controls.Add(refresh);
			_itemEditorCompatibilityLabel = NewLabel(
				"兼容字段：等待读取",
				8,
				48,
				540);
			_itemEditorCompatibilityLabel.AutoEllipsis = true;
			_itemEditorCompatibilityLabel.ForeColor = Color.Gainsboro;
			actions.Controls.Add(_itemEditorCompatibilityLabel);
			return page;
		}

		private void AddNumericItemAttribute(
			TableLayoutPanel table,
			int row,
			int column,
			string name,
			string caption,
			decimal minimum,
			decimal maximum,
			int decimalPlaces,
			decimal increment)
		{
			Label label = new Label();
			label.Text = caption;
			label.Dock = DockStyle.Fill;
			label.TextAlign = ContentAlignment.MiddleRight;
			label.Margin = new Padding(2, 5, 5, 4);
			table.Controls.Add(label, column, row);

			NumericUpDown editor = new NumericUpDown();
			editor.Name = "itemAttribute_" + name;
			editor.Dock = DockStyle.Fill;
			editor.Margin = new Padding(0, 5, 8, 4);
			editor.Minimum = minimum;
			editor.Maximum = maximum;
			editor.DecimalPlaces = decimalPlaces;
			editor.Increment = increment;
			editor.ThousandsSeparator = true;
			table.Controls.Add(editor, column + 1, row);
			_itemAttributeEditors[name] = editor;
		}

		private void AddBooleanItemAttribute(
			TableLayoutPanel table,
			int row,
			int column,
			string name,
			string caption)
		{
			Label label = new Label();
			label.Text = caption;
			label.Dock = DockStyle.Fill;
			label.TextAlign = ContentAlignment.MiddleRight;
			label.Margin = new Padding(2, 5, 5, 4);
			table.Controls.Add(label, column, row);

			CheckBox editor = new CheckBox();
			editor.Name = "itemAttribute_" + name;
			editor.Text = "启用";
			editor.AutoSize = true;
			editor.Anchor = AnchorStyles.Left;
			editor.Margin = new Padding(0, 5, 8, 4);
			table.Controls.Add(editor, column + 1, row);
			_itemAttributeEditors[name] = editor;
		}

		private TabPage BuildRemoteInventoryPage()
		{
			TabPage page = NewPage("别人背包");
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.Margin = Padding.Empty;
			layout.ColumnCount = 1;
			layout.RowCount = 2;
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168F));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(layout);

			Panel top = new Panel();
			top.Dock = DockStyle.Fill;
			layout.Controls.Add(top, 0, 0);

			Label explanation = NewLabel(
				"只显示 Terraria 联机协议已经同步到本客户端的主背包、钱币、弹药和鼠标物品槽（0–58）。" +
				"\r\n不包含银行、保险箱、护卫熔炉、虚空袋、服务端私有存储或独立垃圾槽；每约 0.5 秒自动刷新。",
				10,
				8,
				1040);
			explanation.Height = 42;
			top.Controls.Add(explanation);

			top.Controls.Add(NewLabel("玩家：", 10, 63, 52));
			_remotePlayerBox = new ComboBox();
			_remotePlayerBox.DropDownStyle = ComboBoxStyle.DropDownList;
			_remotePlayerBox.SetBounds(62, 59, 255, 27);
			_remotePlayerBox.SelectedIndexChanged += RemotePlayerSelectionChanged;
			top.Controls.Add(_remotePlayerBox);

			Button refresh = NewButton("立即刷新", 330, 57, 110);
			refresh.Click += delegate { RefreshRemoteInventories(); };
			top.Controls.Add(refresh);

			_remoteInventoryInfo = NewLabel("正在读取联机玩家……", 455, 63, 575);
			_remoteInventoryInfo.AutoEllipsis = true;
			top.Controls.Add(_remoteInventoryInfo);

			_remoteSelectedInfo = NewLabel("未选择槽位。", 10, 95, 1030);
			_remoteSelectedInfo.AutoEllipsis = true;
			_remoteSelectedInfo.ForeColor = Color.Gold;
			top.Controls.Add(_remoteSelectedInfo);

			top.Controls.Add(NewLabel("数量：", 10, 132, 52));
			_remoteGiveAmount = new NumericUpDown();
			_remoteGiveAmount.SetBounds(62, 128, 90, 27);
			_remoteGiveAmount.Minimum = 1;
			_remoteGiveAmount.Maximum = 9999;
			_remoteGiveAmount.Value = 1;
			top.Controls.Add(_remoteGiveAmount);

			_remoteGiveButton = NewButton("给予自己", 165, 126, 110);
			_remoteGiveButton.Enabled = false;
			_remoteGiveButton.Click += delegate { GiveSelectedRemoteItemToSelf(); };
			top.Controls.Add(_remoteGiveButton);

			_remoteFindButton = NewButton("在全物品页查找", 288, 126, 155);
			_remoteFindButton.Enabled = false;
			_remoteFindButton.Click += delegate { FindSelectedRemoteItemInCatalog(); };
			top.Controls.Add(_remoteFindButton);

			_remoteCopyButton = NewButton("复制 ID", 456, 126, 100);
			_remoteCopyButton.Enabled = false;
			_remoteCopyButton.Click += delegate { CopySelectedRemoteItemId(); };
			top.Controls.Add(_remoteCopyButton);

			Label actionNote = NewLabel("这些操作只读取对方槽位；不会修改对方背包。", 570, 132, 460);
			actionNote.ForeColor = Color.Gainsboro;
			top.Controls.Add(actionNote);

			_remoteInventoryGrid = new InventorySlotGrid();
			_remoteInventoryGrid.Dock = DockStyle.Fill;
			_remoteInventoryGrid.IconProvider = _game.GetItemIcon;
			_remoteInventoryGrid.SelectionEnabled = true;
			_remoteInventoryGrid.SelectedSlotChanged += RemoteInventorySlotSelectionChanged;
			layout.Controls.Add(_remoteInventoryGrid, 0, 1);

			RefreshRemoteInventories();
			return page;
		}

		private void RemotePlayerSelectionChanged(object sender, EventArgs e)
		{
			if (_refreshingRemotePlayers)
				return;

			int previousPlayerIndex = _selectedRemotePlayerIndex;
			int selectedIndex = _remotePlayerBox == null ? -1 : _remotePlayerBox.SelectedIndex;
			if (selectedIndex >= 0 && selectedIndex < _remoteInventories.Count)
				_selectedRemotePlayerIndex = _remoteInventories[selectedIndex].PlayerIndex;
			else
				_selectedRemotePlayerIndex = -1;
			if (_selectedRemotePlayerIndex != previousPlayerIndex)
				_selectedRemoteSlot = -1;
			ShowSelectedRemoteInventory();
		}

		private void RefreshRemoteInventories()
		{
			if (_remotePlayerBox == null || _remoteInventoryGrid == null)
				return;

			try {
				int keepPlayerIndex = _selectedRemotePlayerIndex;
				int oldSelectedIndex = _remotePlayerBox.SelectedIndex;
				if (keepPlayerIndex < 0 &&
					oldSelectedIndex >= 0 && oldSelectedIndex < _remoteInventories.Count)
					keepPlayerIndex = _remoteInventories[oldSelectedIndex].PlayerIndex;

				List<RemoteInventorySnapshot> latest = _game.GetRemoteInventories()
					.OrderBy(snapshot => snapshot.PlayerIndex)
					.ToList();
				bool rosterChanged = latest.Count != _remoteInventories.Count;
				if (!rosterChanged) {
					for (int i = 0; i < latest.Count; i++) {
						if (latest[i].PlayerIndex != _remoteInventories[i].PlayerIndex ||
							!string.Equals(
								latest[i].PlayerName,
								_remoteInventories[i].PlayerName,
								StringComparison.Ordinal)) {
							rosterChanged = true;
							break;
						}
					}
				}

				_remoteInventories.Clear();
				_remoteInventories.AddRange(latest);

				int selectedIndex = latest.FindIndex(
					delegate(RemoteInventorySnapshot snapshot) {
						return snapshot.PlayerIndex == keepPlayerIndex;
					});
				if (selectedIndex < 0 && latest.Count > 0)
					selectedIndex = 0;

				_refreshingRemotePlayers = true;
				try {
					if (rosterChanged) {
						_remotePlayerBox.BeginUpdate();
						try {
							_remotePlayerBox.Items.Clear();
							foreach (RemoteInventorySnapshot snapshot in latest)
								_remotePlayerBox.Items.Add(RemotePlayerCaption(snapshot));
						}
						finally {
							_remotePlayerBox.EndUpdate();
						}
					}
					if (_remotePlayerBox.SelectedIndex != selectedIndex)
						_remotePlayerBox.SelectedIndex = selectedIndex;
				}
				finally {
					_refreshingRemotePlayers = false;
				}

				int nextPlayerIndex = selectedIndex >= 0
					? latest[selectedIndex].PlayerIndex
					: -1;
				if (nextPlayerIndex != keepPlayerIndex)
					_selectedRemoteSlot = -1;
				_selectedRemotePlayerIndex = nextPlayerIndex;
				ShowSelectedRemoteInventory();
			}
			catch (Exception ex) {
				Exception root = ex;
				while (root.InnerException != null)
					root = root.InnerException;
				_remoteInventoryInfo.Text = "别人背包刷新失败：" +
					root.GetType().Name + " - " + root.Message;
			}
		}

		private void ShowSelectedRemoteInventory()
		{
			if (_remoteInventoryGrid == null || _remoteInventoryInfo == null)
				return;

			int selectedIndex = _remotePlayerBox == null ? -1 : _remotePlayerBox.SelectedIndex;
			if (selectedIndex < 0 || selectedIndex >= _remoteInventories.Count) {
				_remoteInventoryGrid.SetItems(Enumerable.Empty<InventoryEntry>());
				_selectedRemoteSlot = -1;
				_remoteInventoryGrid.SelectedSlot = -1;
				_remoteInventoryInfo.Text = "当前没有检测到其他联机玩家。";
				UpdateRemoteSelectedItemDetails();
				return;
			}

			RemoteInventorySnapshot snapshot = _remoteInventories[selectedIndex];
			_selectedRemotePlayerIndex = snapshot.PlayerIndex;
			_remoteInventoryGrid.SetItems(snapshot.Items);
			bool selectedSlotExists = _selectedRemoteSlot >= 0 &&
				_selectedRemoteSlot <= 58 &&
				snapshot.Items != null &&
				snapshot.Items.Any(item => item.Slot == _selectedRemoteSlot);
			if (!selectedSlotExists)
				_selectedRemoteSlot = -1;
			_remoteInventoryGrid.SelectedSlot = _selectedRemoteSlot;
			int nonEmpty = snapshot.Items == null
				? 0
				: snapshot.Items.Count(item => item.Id > 0 && item.Stack > 0);
			_remoteInventoryInfo.Text = RemotePlayerCaption(snapshot) +
				"：同步槽位 0–58，非空 " + nonEmpty + " 个";
			UpdateRemoteSelectedItemDetails();
		}

		private void RemoteInventorySlotSelectionChanged(object sender, EventArgs e)
		{
			if (_remoteInventoryGrid == null)
				return;
			_selectedRemoteSlot = _remoteInventoryGrid.SelectedSlot;

			RemoteInventorySnapshot snapshot;
			InventoryEntry item;
			if (TryGetSelectedRemoteItem(out snapshot, out item) &&
				item.Id > 0 && item.Stack > 0 && _remoteGiveAmount != null) {
				decimal amount = Math.Max(1, item.Stack);
				amount = Math.Min(_remoteGiveAmount.Maximum, amount);
				_remoteGiveAmount.Value = amount;
			}
			UpdateRemoteSelectedItemDetails();
		}

		private void UpdateRemoteSelectedItemDetails()
		{
			if (_remoteSelectedInfo == null)
				return;

			RemoteInventorySnapshot snapshot;
			InventoryEntry item;
			bool hasSelection = TryGetSelectedRemoteItem(out snapshot, out item);
			if (!hasSelection) {
				_remoteSelectedInfo.Text = "未选择槽位。";
				SetRemoteActionButtonsEnabled(false);
				return;
			}

			string itemName = item.Id <= 0 || item.Stack <= 0
				? "(空)"
				: string.IsNullOrWhiteSpace(item.Name) ? "Item_" + item.Id : item.Name;
			_remoteSelectedInfo.Text =
				"已选：" + RemotePlayerCaption(snapshot) +
				" | 槽位 " + item.Slot + "（" + RemoteSlotKind(item.Slot) + "）" +
				" | ID " + item.Id +
				" | " + itemName +
				" | 堆叠 " + item.Stack;
			SetRemoteActionButtonsEnabled(item.Id > 0 && item.Stack > 0);
		}

		private bool TryGetSelectedRemoteItem(
			out RemoteInventorySnapshot snapshot,
			out InventoryEntry item)
		{
			snapshot = null;
			item = null;
			if (_remotePlayerBox == null || _selectedRemoteSlot < 0)
				return false;

			int selectedIndex = _remotePlayerBox.SelectedIndex;
			if (selectedIndex < 0 || selectedIndex >= _remoteInventories.Count)
				return false;

			snapshot = _remoteInventories[selectedIndex];
			if (snapshot.Items == null)
				return false;
			item = snapshot.Items.FirstOrDefault(
				delegate(InventoryEntry candidate) {
					return candidate.Slot == _selectedRemoteSlot;
				});
			return item != null;
		}

		private void SetRemoteActionButtonsEnabled(bool enabled)
		{
			if (_remoteGiveButton != null)
				_remoteGiveButton.Enabled = enabled;
			if (_remoteFindButton != null)
				_remoteFindButton.Enabled = enabled;
			if (_remoteCopyButton != null)
				_remoteCopyButton.Enabled = enabled;
		}

		private void GiveSelectedRemoteItemToSelf()
		{
			RemoteInventorySnapshot snapshot;
			InventoryEntry item;
			if (!TryGetSelectedRemoteItem(out snapshot, out item) ||
				item.Id <= 0 || item.Stack <= 0)
				return;

			try {
				int amount = Decimal.ToInt32(_remoteGiveAmount.Value);
				_game.GiveItem(item.Id, amount);
				_statusLabel.Text = "已参考 " + RemotePlayerCaption(snapshot) +
					" 的槽位 " + item.Slot + " 给予自己：" +
					item.Name + " × " + amount;
				RefreshInventory();
			}
			catch (Exception ex) {
				_statusLabel.Text = "给予自己失败：" + ex.Message;
			}
		}

		private void FindSelectedRemoteItemInCatalog()
		{
			RemoteInventorySnapshot snapshot;
			InventoryEntry item;
			if (!TryGetSelectedRemoteItem(out snapshot, out item) ||
				item.Id <= 0 || item.Stack <= 0)
				return;

			string id = item.Id.ToString(CultureInfo.InvariantCulture);
			_searchBox.Text = id;
			ApplyItemFilter();
			if (_tabs != null && _itemsPage != null)
				_tabs.SelectedTab = _itemsPage;

			if (_itemGrid != null && _itemGrid.Rows.Count > 0) {
				_itemGrid.ClearSelection();
				DataGridViewRow row = _itemGrid.Rows[0];
				row.Selected = true;
				if (row.Cells.Count > 0)
					_itemGrid.CurrentCell = row.Cells[0];
				_itemGrid.FirstDisplayedScrollingRowIndex = row.Index;
				ItemSelectionChanged(_itemGrid, EventArgs.Empty);
			}
			_statusLabel.Text = "已在全物品页查找 ID " + id + "。";
		}

		private void CopySelectedRemoteItemId()
		{
			RemoteInventorySnapshot snapshot;
			InventoryEntry item;
			if (!TryGetSelectedRemoteItem(out snapshot, out item) ||
				item.Id <= 0 || item.Stack <= 0)
				return;

			try {
				Clipboard.SetText(item.Id.ToString(CultureInfo.InvariantCulture));
				_statusLabel.Text = "已复制物品 ID：" + item.Id;
			}
			catch (Exception ex) {
				_statusLabel.Text = "复制 ID 失败：" + ex.Message;
			}
		}

		private static string RemoteSlotKind(int slot)
		{
			if (slot >= 0 && slot < 50)
				return "主背包";
			if (slot >= 50 && slot < 54)
				return "钱币";
			if (slot >= 54 && slot < 58)
				return "弹药";
			if (slot == 58)
				return "鼠标物品";
			return "未知";
		}

		private static string RemotePlayerCaption(RemoteInventorySnapshot snapshot)
		{
			string name = snapshot == null ? string.Empty : snapshot.PlayerName;
			int index = snapshot == null ? -1 : snapshot.PlayerIndex;
			if (string.IsNullOrWhiteSpace(name))
				name = "玩家 " + index;
			return name + "  [P" + index + "]";
		}

		private TabPage BuildHistoryPage()
		{
			TabPage page = NewPage("别人拿出记录");
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.Margin = Padding.Empty;
			layout.ColumnCount = 1;
			layout.RowCount = 2;
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(layout);

			Panel top = new Panel();
			top.Dock = DockStyle.Fill;
			layout.Controls.Add(top, 0, 0);
			top.Controls.Add(NewLabel("记录本客户端运行期间，其他玩家切换到手上的物品：", 10, 8, 520));
			_historySearch = new TextBox();
			_historySearch.SetBounds(10, 37, 320, 27);
			_historySearch.TextChanged += delegate { ApplyHistoryFilter(); };
			top.Controls.Add(_historySearch);
			Button clear = NewButton("清空记录", 342, 35, 110);
			clear.Click += delegate { _history.Clear(); _lastHeld.Clear(); };
			top.Controls.Add(clear);
			Button export = NewButton("导出 CSV", 462, 35, 110);
			export.Click += delegate { ExportHistory(); };
			top.Controls.Add(export);

			_historyGrid = CreateGrid();
			_historyGrid.Dock = DockStyle.Fill;
			_historyGrid.DataSource = _history;
			AddTextColumn(_historyGrid, "时间", "Time", 85);
			AddTextColumn(_historyGrid, "玩家", "Player", 135);
			AddTextColumn(_historyGrid, "ID", "Id", 65);
			AddTextColumn(_historyGrid, "名称", "Name", 240);
			AddTextColumn(_historyGrid, "内部名", "InternalName", 220);
			AddTextColumn(_historyGrid, "数量", "Stack", 60);
			layout.Controls.Add(_historyGrid, 0, 1);
			return page;
		}

		private void TimerTick(object sender, EventArgs e)
		{
			try {
				ApplyCheats();
				UpdateMapRevealUi();
				if (!_game.IsGameThreadResponsive) {
					_statusLabel.Text =
						"游戏主线程已暂停（单人失去焦点时正常）；切回游戏后会自动恢复。";
					return;
				}
				if (!_game.IsWorldReady) {
					_statusLabel.Text =
						string.IsNullOrEmpty(_game.LastGameThreadError)
							? "游戏线程已连接，等待进入世界……"
							: "等待世界初始化：" +
								_game.LastGameThreadError;
					return;
				}
				TrackHeldItems();
				if (++_slowTick >= 30) {
					_slowTick = 0;
					RefreshInventory();
					RefreshRemoteInventories();
					UpdateStatus();
				}
			}
			catch (Exception ex) {
				Exception root = ex;
				while (root.InnerException != null)
					root = root.InnerException;
				_statusLabel.Text =
					"本帧游戏数据读取未完成：" +
					root.GetType().Name + " - " + root.Message +
					"；将自动重试。";
			}
		}

		private void UpdateMapRevealUi()
		{
			if (_mapRevealButton == null || _mapRevealStatusLabel == null)
				return;
			if (_game.IsMapRevealActive) {
				int total = Math.Max(1, _game.MapRevealTotal);
				int done = Math.Max(
					0, Math.Min(total, _game.MapRevealDone));
				int percent = (int)((long)done * 100L / total);
				_mapRevealButton.Text = _game.IsMapRevealCancelling
					? "正在停止……"
					: "取消点亮地图";
				_mapRevealStatusLabel.Text =
					"正在点亮当前世界：" + percent + "%  (" +
					done.ToString("N0") + " / " +
					total.ToString("N0") + ")";
				return;
			}

			_mapRevealButton.Text = "点亮整个地图";
			if (_game.MapRevealTotal > 0 &&
				_game.MapRevealDone >= _game.MapRevealTotal)
				_mapRevealStatusLabel.Text =
					"地图已全部点亮；打开小地图/全屏地图即可查看。";
			else if (_game.MapRevealTotal > 0)
				_mapRevealStatusLabel.Text = "地图点亮已停止。";
		}

		private void ApplyCheats()
		{
			_game.ApplyCheats(
				_godMode.Checked,
				_infiniteLife.Checked,
				_infiniteMana.Checked,
				_infiniteBreath.Checked,
				_noPotionCooldown.Checked,
				_clearDebuffs.Checked,
				_noKnockback.Checked,
				_noFallDamage.Checked,
				_lavaImmune.Checked,
				_fastMovement.Checked,
				(float)_fastMovementMultiplier.Value,
				_highJump.Checked,
				(float)_highJumpMultiplier.Value,
				_instantRespawn.Checked,
				_adjustableStep.Checked,
				Decimal.ToInt32(_stepBlocks.Value),
				_infiniteFlight.Checked,
				_fullBright.Checked,
				_unrestrictedView.Checked,
				_concurrentAttack.Checked);
		}

		private void TrackHeldItems()
		{
			foreach (RemoteHeldSnapshot held in _game.GetRemoteHeldItems()) {
				int i = held.PlayerIndex;
				int type = held.ItemType;
				int old;
				_lastHeld.TryGetValue(i, out old);
				if (old == type)
					continue;

				_lastHeld[i] = type;
				if (type <= 0)
					continue;

				ItemEntry info;
				_itemsById.TryGetValue(type, out info);
				HistoryEntry entry = new HistoryEntry();
				entry.Time = DateTime.Now.ToString("HH:mm:ss");
				entry.Player = string.IsNullOrWhiteSpace(held.PlayerName) ? "玩家 " + i : held.PlayerName;
				entry.Id = type;
				entry.Name = info == null ? held.ItemName : info.ChineseName;
				entry.InternalName = info == null ? "Item_" + type : info.InternalName;
				entry.Stack = held.Stack;
				_history.Insert(0, entry);
				while (_history.Count > 2000)
					_history.RemoveAt(_history.Count - 1);
				if (_historySearch != null && _historySearch.TextLength > 0)
					ApplyHistoryFilter();
			}
		}

		private void LoadCatalog()
		{
			Assembly assembly = Assembly.GetExecutingAssembly();
			using (Stream stream = assembly.GetManifestResourceStream("Terraria1456Toolkit.items.tsv"))
			using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					string[] parts = line.Split('\t');
					int id;
					if (parts.Length < 4 || !int.TryParse(parts[0], out id))
						continue;
					ItemEntry item = new ItemEntry {
						Id = id,
						InternalName = parts[1],
						ChineseName = parts[2],
						EnglishName = parts[3]
					};
					_allItems.Add(item);
					_itemsById[id] = item;
				}
			}
		}

		private void ApplyItemFilter()
		{
			if (_searchBox == null)
				return;
			string query = _searchBox.Text.Trim();
			IEnumerable<ItemEntry> result = _allItems;
			int exactId;
			if (int.TryParse(query, out exactId))
				result = result.Where(delegate(ItemEntry x) { return x.Id == exactId; });
			else if (query.Length > 0)
				result = result.Where(delegate(ItemEntry x) {
					return Contains(x.ChineseName, query) || Contains(x.EnglishName, query) || Contains(x.InternalName, query);
				});

			_filteredItems.RaiseListChangedEvents = false;
			_filteredItems.Clear();
			foreach (ItemEntry item in result)
				_filteredItems.Add(item);
			_filteredItems.RaiseListChangedEvents = true;
			_filteredItems.ResetBindings();
			if (_itemResultLabel != null)
				_itemResultLabel.Text = _filteredItems.Count + " / " + _allItems.Count + " 项";
			if (_itemGrid != null && _filteredItems.Count == 0) {
				_selectedItem = null;
				if (_selectedLabel != null)
					_selectedLabel.Text = "没有匹配的物品";
			}
		}

		private static bool Contains(string value, string query)
		{
			return value != null && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
		}

		private void ItemSelectionChanged(object sender, EventArgs e)
		{
			if (_itemGrid.CurrentRow == null)
				return;
			_selectedItem = _itemGrid.CurrentRow.DataBoundItem as ItemEntry;
			if (_selectedItem != null)
				_selectedLabel.Text = string.Format(CultureInfo.InvariantCulture, "已选：[{0}] {1} / {2}", _selectedItem.Id, _selectedItem.ChineseName, _selectedItem.InternalName);
		}

		private void InventorySelectionChanged(object sender, EventArgs e)
		{
			_selectedSlot = _inventoryGrid.SelectedSlot;
			_editorSelectedSlot = _selectedSlot;
			if (_editorInventoryGrid != null)
				_editorInventoryGrid.SelectedSlot = _selectedSlot;
			UpdateItemEditorSelectionLabel();
		}

		private void EditorInventorySelectionChanged(object sender, EventArgs e)
		{
			if (_editorInventoryGrid == null)
				return;
			_editorSelectedSlot = _editorInventoryGrid.SelectedSlot;
			_selectedSlot = _editorSelectedSlot;
			if (_inventoryGrid != null)
				_inventoryGrid.SelectedSlot = _selectedSlot;
			UpdateItemEditorSelectionLabel();
			ReadSelectedItemAttributes();
		}

		private void UpdateItemEditorSelectionLabel()
		{
			if (_itemEditorSelectionLabel == null)
				return;
			if (_editorSelectedSlot < 0) {
				_itemEditorSelectionLabel.Text = "未选择槽位";
				return;
			}
			InventoryEntry entry = _inventory.FirstOrDefault(
				delegate(InventoryEntry item) {
					return item.Slot == _editorSelectedSlot;
				});
			if (entry == null || entry.Id <= 0 || entry.Stack <= 0) {
				_itemEditorSelectionLabel.Text =
					"已选槽位 " + _editorSelectedSlot + "（空）";
				return;
			}
			_itemEditorSelectionLabel.Text =
				"已选槽位 " + _editorSelectedSlot + "：" +
				(entry.Name ?? "Item_" + entry.Id) +
				"  ID " + entry.Id + " × " + entry.Stack;
		}

		private void ReadSelectedItemAttributes()
		{
			if (_editorSelectedSlot < 0) {
				if (_statusLabel != null)
					_statusLabel.Text = "请先选择要读取的背包槽位。";
				return;
			}

			int requestedSlot = _editorSelectedSlot;
			if (_itemEditorCompatibilityLabel != null)
				_itemEditorCompatibilityLabel.Text = "正在游戏线程读取槽位 " + requestedSlot + "…";
			try {
				_game.RequestItemAttributes(
					requestedSlot,
					delegate(ItemAttributeSnapshot snapshot, string error) {
						try {
							BeginInvoke((MethodInvoker)delegate {
								ApplyItemAttributeSnapshot(requestedSlot, snapshot, error);
							});
						}
						catch {
						}
					});
				if (!_game.IsGameTickHooked && _statusLabel != null)
					_statusLabel.Text = "游戏 Tick 钩子不可用，已使用兼容读取路径。";
			}
			catch (Exception ex) {
				if (_itemEditorCompatibilityLabel != null)
					_itemEditorCompatibilityLabel.Text = "读取失败：" + ex.Message;
			}
		}

		private void ApplyItemAttributeSnapshot(
			int requestedSlot,
			ItemAttributeSnapshot snapshot,
			string error)
		{
			if (IsDisposed)
				return;
			if (!string.IsNullOrEmpty(error) || snapshot == null) {
				if (_itemEditorCompatibilityLabel != null)
					_itemEditorCompatibilityLabel.Text =
						"读取失败：" + (error ?? "没有返回数据");
				return;
			}

			int supportedCount = 0;
			List<string> missing = new List<string>();
			foreach (KeyValuePair<string, Control> pair in _itemAttributeEditors) {
				bool supported = snapshot.SupportedFields != null &&
					snapshot.SupportedFields.Contains(pair.Key);
				pair.Value.Enabled = supported;
				if (!supported) {
					missing.Add(pair.Key);
					continue;
				}
				supportedCount++;
				object value;
				if (!snapshot.Values.TryGetValue(pair.Key, out value))
					continue;
				NumericUpDown number = pair.Value as NumericUpDown;
				if (number != null) {
					try {
						decimal converted = Convert.ToDecimal(
							value,
							CultureInfo.InvariantCulture);
						number.Value = Math.Max(
							number.Minimum,
							Math.Min(number.Maximum, converted));
					}
					catch {
					}
					continue;
				}
				CheckBox check = pair.Value as CheckBox;
				if (check != null) {
					try {
						check.Checked = Convert.ToBoolean(value);
					}
					catch {
					}
				}
			}

			if (_itemEditorCompatibilityLabel != null) {
				_itemEditorCompatibilityLabel.Text =
					"已读取槽位 " + requestedSlot + "；兼容 " +
					supportedCount + "/" + _itemAttributeEditors.Count + " 字段" +
					(missing.Count == 0
						? "（全部支持）"
						: "；缺失：" + string.Join(", ", missing.ToArray()));
			}
			if (_statusLabel != null)
				_statusLabel.Text = "已从游戏线程读取槽位 " + requestedSlot + " 的物品属性。";
		}

		private void WriteSelectedItemAttributes()
		{
			if (_editorSelectedSlot < 0) {
				if (_statusLabel != null)
					_statusLabel.Text = "请先选择要写回的背包槽位。";
				return;
			}

			Dictionary<string, object> values =
				new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, Control> pair in _itemAttributeEditors) {
				if (!pair.Value.Enabled)
					continue;
				NumericUpDown number = pair.Value as NumericUpDown;
				if (number != null) {
					values[pair.Key] = number.DecimalPlaces == 0
						? (object)Decimal.ToInt32(number.Value)
						: (object)number.Value;
					continue;
				}
				CheckBox check = pair.Value as CheckBox;
				if (check != null)
					values[pair.Key] = check.Checked;
			}

			try {
				int slot = _editorSelectedSlot;
				_game.WriteItemAttributes(slot, values);
				if (_statusLabel != null) {
					_statusLabel.Text = _game.IsGameTickHooked
						? "已提交槽位 " + slot + " 的属性写回，将在下一游戏 Tick 生效。"
						: "游戏 Tick 钩子不可用，已使用兼容写回路径。";
				}
			}
			catch (Exception ex) {
				if (_statusLabel != null)
					_statusLabel.Text = "属性写回失败：" + ex.Message;
			}
		}

		private void GiveSelectedItem()
		{
			if (_selectedItem == null)
				return;
			try {
				int amount = Decimal.ToInt32(_stackBox.Value);
				_game.GiveItem(_selectedItem.Id, amount);
				_statusLabel.Text = "已获取：" + _selectedItem.ChineseName + " × " + amount;
				RefreshInventory();
			}
			catch (Exception ex) {
				_statusLabel.Text = "获取失败：" + ex.Message;
			}
		}

		private void ReplaceSelectedSlot()
		{
			if (_selectedItem == null || _selectedSlot < 0)
				return;
			try {
				_game.ReplaceSlot(_selectedSlot, _selectedItem.Id, Decimal.ToInt32(_stackBox.Value));
				_statusLabel.Text = "已替换槽位 " + _selectedSlot;
				RefreshInventory();
			}
			catch (Exception ex) {
				_statusLabel.Text = "替换失败：" + ex.Message;
			}
		}

		private void ClearSelectedSlot()
		{
			if (_selectedSlot < 0)
				return;
			try {
				_game.ClearSlot(_selectedSlot);
				_statusLabel.Text = "已清空槽位 " + _selectedSlot;
				RefreshInventory();
			}
			catch (Exception ex) {
				_statusLabel.Text = "清空失败：" + ex.Message;
			}
		}

		private void RefreshInventory()
		{
			try {
				int keepSlot = _selectedSlot;
				_inventory.RaiseListChangedEvents = false;
				_inventory.Clear();
				foreach (InventoryEntry item in _game.ReadInventory())
					_inventory.Add(item);
				_inventory.RaiseListChangedEvents = true;
				_inventory.ResetBindings();
				_inventoryGrid.SetItems(_inventory);
				_inventoryGrid.SelectedSlot = keepSlot;
				if (_editorInventoryGrid != null) {
					_editorInventoryGrid.SetItems(_inventory);
					_editorInventoryGrid.SelectedSlot = _editorSelectedSlot;
				}
				UpdateItemEditorSelectionLabel();
			}
			catch {
			}
		}

		private void ApplyHistoryFilter()
		{
			if (_historySearch == null || _historyGrid == null)
				return;
			string query = _historySearch.Text.Trim();
			_historyGrid.CurrentCell = null;
			foreach (DataGridViewRow row in _historyGrid.Rows) {
				HistoryEntry entry = row.DataBoundItem as HistoryEntry;
				row.Visible = entry == null || query.Length == 0 || Contains(entry.Player, query) ||
					Contains(entry.Name, query) || Contains(entry.InternalName, query) || entry.Id.ToString() == query;
			}
		}

		private void ExportHistory()
		{
			try {
				string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				string path = Path.Combine(desktop, "Terraria_拿出记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
				using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true))) {
					writer.WriteLine("时间,玩家,ID,名称,内部名,数量");
					foreach (HistoryEntry row in _history)
						writer.WriteLine(string.Join(",", Csv(row.Time), Csv(row.Player), row.Id.ToString(), Csv(row.Name), Csv(row.InternalName), row.Stack.ToString()));
				}
				_statusLabel.Text = "已导出：" + path;
			}
			catch (Exception ex) {
				_statusLabel.Text = "导出失败：" + ex.Message;
			}
		}

		private static string Csv(string value)
		{
			return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
		}

		private void UpdateStatus()
		{
			try {
				if (!string.IsNullOrEmpty(_game.LastGameActionError) &&
					_statusLabel != null)
					_statusLabel.Text =
						"游戏线程操作失败：" + _game.LastGameActionError;
				object player = _game.GetLocalPlayer();
				if (_game.IsPlayerActive(player))
					Text = "Terraria 1.4.5.6 全物品修改器 - " + _game.GetPlayerName(player);
			}
			catch {
			}
		}

		private TabPage NewPage(string text)
		{
			TabPage page = new TabPage(text);
			page.BackColor = BackColor;
			page.ForeColor = ForeColor;
			return page;
		}

		private static CheckBox AddOption(Control parent, string text)
		{
			CheckBox option = new CheckBox();
			option.Text = text;
			option.AutoSize = true;
			option.Margin = new Padding(3, 7, 3, 7);
			parent.Controls.Add(option);
			return option;
		}

		private static CheckBox AddAdjustableOption(
			Control parent,
			string text,
			string multiplierCaption,
			decimal defaultValue,
			out NumericUpDown multiplier)
		{
			FlowLayoutPanel row = new FlowLayoutPanel();
			row.AutoSize = true;
			row.WrapContents = false;
			row.Margin = new Padding(0, 2, 0, 2);

			CheckBox option = new CheckBox();
			option.Text = text;
			option.AutoSize = true;
			option.Margin = new Padding(3, 7, 14, 7);
			row.Controls.Add(option);

			Label caption = new Label();
			caption.Text = multiplierCaption;
			caption.AutoSize = true;
			caption.Margin = new Padding(0, 9, 5, 0);
			row.Controls.Add(caption);

			multiplier = new NumericUpDown();
			multiplier.Minimum = 1.0M;
			multiplier.Maximum = 10.0M;
			multiplier.Increment = 0.25M;
			multiplier.DecimalPlaces = 2;
			multiplier.Value = Math.Max(
				multiplier.Minimum,
				Math.Min(multiplier.Maximum, defaultValue));
			multiplier.Width = 82;
			multiplier.Margin = new Padding(0, 5, 4, 0);
			row.Controls.Add(multiplier);

			Label suffix = new Label();
			suffix.Text = "×";
			suffix.AutoSize = true;
			suffix.Margin = new Padding(0, 9, 0, 0);
			row.Controls.Add(suffix);
			parent.Controls.Add(row);
			return option;
		}

		private static CheckBox AddStepOption(
			Control parent,
			string text,
			string stepCaption,
			decimal defaultValue,
			out NumericUpDown blocks)
		{
			FlowLayoutPanel row = new FlowLayoutPanel();
			row.AutoSize = true;
			row.WrapContents = false;
			row.Margin = new Padding(0, 2, 0, 2);

			CheckBox option = new CheckBox();
			option.Text = text;
			option.AutoSize = true;
			option.Margin = new Padding(3, 7, 14, 7);
			row.Controls.Add(option);

			Label caption = new Label();
			caption.Text = stepCaption;
			caption.AutoSize = true;
			caption.Margin = new Padding(0, 9, 5, 0);
			row.Controls.Add(caption);

			blocks = new NumericUpDown();
			blocks.Minimum = 1M;
			blocks.Maximum = 10M;
			blocks.Increment = 1M;
			blocks.DecimalPlaces = 0;
			blocks.Value = Math.Max(
				blocks.Minimum,
				Math.Min(blocks.Maximum, defaultValue));
			blocks.Width = 82;
			blocks.Margin = new Padding(0, 5, 4, 0);
			row.Controls.Add(blocks);

			Label suffix = new Label();
			suffix.Text = "格";
			suffix.AutoSize = true;
			suffix.Margin = new Padding(0, 9, 0, 0);
			row.Controls.Add(suffix);
			parent.Controls.Add(row);
			return option;
		}

		private static Label NewLabel(string text, int x, int y, int width)
		{
			Label label = new Label();
			label.Text = text;
			label.SetBounds(x, y, width, 24);
			return label;
		}

		private static Button NewButton(string text, int x, int y, int width)
		{
			Button button = new Button();
			button.Text = text;
			button.SetBounds(x, y, width, 32);
			button.FlatStyle = FlatStyle.Flat;
			return button;
		}

		private static DataGridView CreateGrid()
		{
			DataGridView grid = new DataGridView();
			grid.AutoGenerateColumns = false;
			grid.AllowUserToAddRows = false;
			grid.AllowUserToDeleteRows = false;
			grid.AllowUserToResizeRows = false;
			grid.ReadOnly = true;
			grid.MultiSelect = false;
			grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
			grid.RowHeadersVisible = false;
			grid.ColumnHeadersVisible = true;
			grid.ColumnHeadersHeight = 30;
			grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
			grid.RowTemplate.Height = 27;
			grid.ScrollBars = ScrollBars.Both;
			grid.BorderStyle = BorderStyle.FixedSingle;
			grid.BackgroundColor = Color.FromArgb(28, 30, 35);
			grid.GridColor = Color.FromArgb(64, 67, 74);
			grid.DefaultCellStyle.BackColor = Color.FromArgb(36, 38, 44);
			grid.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
			grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(67, 92, 132);
			grid.DefaultCellStyle.SelectionForeColor = Color.White;
			grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 34, 40);
			grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(49, 52, 60);
			grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
			grid.EnableHeadersVisualStyles = false;
			return grid;
		}

		private static void AddTextColumn(DataGridView grid, string header, string property, int width)
		{
			DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
			column.HeaderText = header;
			column.DataPropertyName = property;
			column.Width = width;
			grid.Columns.Add(column);
		}
	}
}
