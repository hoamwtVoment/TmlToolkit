using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using ReLogic.Content;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaPoint = Microsoft.Xna.Framework.Point;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaTmlToolkit
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

	internal sealed class RemotePlayerEntry
	{
		public int Index { get; set; }
		public string Name { get; set; }

		public override string ToString()
		{
			return Name + "  [P" + Index + "]";
		}
	}

	internal sealed class CheatSnapshot
	{
		public bool GodMode;
		public bool InfiniteLife;
		public bool InfiniteMana;
		public bool InfiniteBreath;
		public bool NoPotionCooldown;
		public bool ClearDebuffs;
		public bool NoKnockback;
		public bool NoFallDamage;
		public bool LavaImmune;
		public bool FastMovement;
		public bool HighJump;
		public bool InfiniteExtraJumps;
		public bool AdjustableGravity;
		public bool AdjustableMaxFallSpeed;
		public bool UnlimitedFallSpeed;
		public bool InstantRespawn;
		public bool AdjustableStep;
		public bool InfiniteFlight;
		public bool AdjustableMaxMinions;
		public bool AdjustableMaxBuffs;
		public bool FullBright;
		public bool UnrestrictedView;
		public bool UnrestrictedWorldBounds;
		public int WorldEdgeSafetyTiles = 10;
		public bool ImproveGameUnlimitedSelection;
		public bool UnlimitedPlacementInteractionRange;
		public bool ConcurrentAttack;
		public bool ShimmerPhaseCanOperate;
		public bool NoCalamityParryCooldown;
		public float MovementMultiplier = 3F;
		public float JumpMultiplier = 2.5F;
		public float GravityMultiplier = 1F;
		public float MaxFallSpeed = 10F;
		public int MaxMinions = 10;
		public int MaxBuffs = 100;
		public int StepBlocks = 3;
	}

	internal sealed class PrefixEntry { public int Id; public string Text; public override string ToString() { return Text ?? string.Empty; } }

	internal sealed class ItemAttributeSnapshot
	{
		public int Slot;
		public int MissingFields;
		public string ItemName;
		public string Error;
		public int Prefix;
		public readonly List<PrefixEntry> ApplicablePrefixes = new List<PrefixEntry>();
		public readonly Dictionary<string, object> Values =
			new Dictionary<string, object>(StringComparer.Ordinal);
	}

	public sealed class TmlTrainerForm : Form
	{
		private delegate void PlayerUpdateOrig(Player self, int i);
		private delegate void PlayerUpdateDetour(
			PlayerUpdateOrig orig, Player self, int i);
		private delegate void PlayerUpdateBuffsOrig(Player self, int i);
		private delegate void PlayerUpdateBuffsDetour(
			PlayerUpdateBuffsOrig orig, Player self, int i);
		private delegate void PreUpdateMovementOrig(Player player);
		private delegate void PreUpdateMovementDetour(
			PreUpdateMovementOrig orig, Player player);
		private delegate void PostUpdateEquipsOrig(Player player);
		private delegate void PostUpdateEquipsDetour(
			PostUpdateEquipsOrig orig, Player player);
		private delegate void FreeUpPetsAndMinionsOrig(
			Player self, Item item);
		private delegate void FreeUpPetsAndMinionsDetour(
			FreeUpPetsAndMinionsOrig orig, Player self, Item item);
		private delegate int PlayerMaxBuffsGetterOrig();
		private delegate int PlayerMaxBuffsGetterDetour(
			PlayerMaxBuffsGetterOrig orig);
		private delegate bool TileRangeCheckOrig(
			Player self, int tileX, int tileY, TileReachCheckSettings settings);
		private delegate bool TileRangeCheckDetour(
			TileRangeCheckOrig orig, Player self, int tileX, int tileY,
			TileReachCheckSettings settings);
		private delegate bool MultiTileRangeCheckOrig(
			Player self, int tileX, int tileY);
		private delegate bool MultiTileRangeCheckDetour(
			MultiTileRangeCheckOrig orig, Player self, int tileX, int tileY);
		private delegate bool TileTypeRangeCheckOrig(
			Player self, int tileType, TileReachCheckSettings settings);
		private delegate bool TileTypeRangeCheckDetour(
			TileTypeRangeCheckOrig orig, Player self, int tileType,
			TileReachCheckSettings settings);
		private delegate bool ItemRangeCheckOrig(Player self, Item item);
		private delegate bool ItemRangeCheckDetour(
			ItemRangeCheckOrig orig, Player self, Item item);
		private delegate void LimitReachPointOrig(
			Player self, ref XnaVector2 point);
		private delegate void LimitReachPointDetour(
			LimitReachPointOrig orig, Player self, ref XnaVector2 point);
		private delegate void PlaceThingOrig(
			Player self, ref Player.ItemCheckContext context);
		private delegate void PlaceThingDetour(
			PlaceThingOrig orig, Player self,
			ref Player.ItemCheckContext context);
		private delegate void ItemCheckReachOrig(Player self);
		private delegate void ItemCheckReachDetour(
			ItemCheckReachOrig orig, Player self);
		private delegate void ChestRangeOrig(Player self);
		private delegate void ChestRangeDetour(
			ChestRangeOrig orig, Player self);
		private delegate void ModifyTransformMatrixOrig(
			ref SpriteViewMatrix transform);
		private delegate void ModifyTransformMatrixDetour(
			ModifyTransformMatrixOrig orig,
			ref SpriteViewMatrix transform);
		private delegate void UpdateViewZoomKeysOrig(Main self);
		private delegate void UpdateViewZoomKeysDetour(
			UpdateViewZoomKeysOrig orig, Main self);
		private delegate XnaPoint GetScreenOverdrawOffsetOrig();
		private delegate XnaPoint GetScreenOverdrawOffsetDetour(
			GetScreenOverdrawOffsetOrig orig);
		private delegate void BordersMovementOrig(Player self);
		private delegate void BordersMovementDetour(BordersMovementOrig orig, Player self);
		private delegate void ClampScreenPositionOrig();
		private delegate void ClampScreenPositionDetour(ClampScreenPositionOrig orig);

		private const float MinimumUnrestrictedZoom = 0.30F;
		private const float MaximumUnrestrictedZoom = 10F;
		private const int VanillaRenderTargetPadding = 192;
		private const int MaximumExpandedRenderTargetDimension = 8192;

		private static readonly string[] ItemIntegerFields = {
			"type", "stack", "damage", "useTime", "useAnimation",
			"shoot", "useAmmo", "pick", "axe", "hammer", "defense", "crit",
			"mana", "healLife", "healMana", "fishingPole"
		};
		private static readonly string[] ItemFloatFields = {
			"knockBack", "scale", "shootSpeed"
		};

		private readonly List<ItemEntry> _allItems = new List<ItemEntry>();
		private readonly Dictionary<int, ItemEntry> _itemsById = new Dictionary<int, ItemEntry>();
		private readonly BindingList<ItemEntry> _filteredItems = new BindingList<ItemEntry>();
		private readonly BindingList<InventoryEntry> _inventory = new BindingList<InventoryEntry>();
		private readonly BindingList<HistoryEntry> _history = new BindingList<HistoryEntry>();
		private readonly Dictionary<int, int> _lastHeld = new Dictionary<int, int>();
		private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

		private DataGridView _itemGrid;
		private TmlInventorySlotGrid _inventoryGrid;
		private Label _itemResultLabel;
		private TmlInventorySlotGrid _itemEditorGrid;
		private Label _itemEditorSelectionLabel;
		private Label _itemEditorStatusLabel;
		private readonly Dictionary<string, NumericUpDown> _itemEditorNumbers =
			new Dictionary<string, NumericUpDown>(StringComparer.Ordinal);
		private CheckBox _itemEditorAutoReuse;
		private ComboBox _itemEditorPrefixBox;
		private readonly HashSet<string> _itemEditorChangedFields = new HashSet<string>(StringComparer.Ordinal);
		private bool _applyingItemEditorSnapshot;
		private DataGridView _historyGrid;
		private TabControl _tabs;
		private TabPage _itemsPage;
		private ComboBox _remotePlayerBox;
		private TmlInventorySlotGrid _remoteInventoryGrid;
		private Label _remoteInventoryStatus;
		private Label _remoteSelectionLabel;
		private NumericUpDown _remoteStackBox;
		private Button _remoteGiveButton;
		private Button _remoteFindButton;
		private Button _remoteCopyIdButton;
		private InventoryEntry _selectedRemoteItem;
		private TextBox _searchBox;
		private TextBox _historySearch;
		private NumericUpDown _stackBox;
		private Label _selectedLabel;
		private Label _statusLabel;
		private ItemEntry _selectedItem;
		private int _selectedSlot = -1;
		private int _selectedRemotePlayer = -1;
		private int _slowTick;
		private bool _refreshingRemotePlayers;
		private readonly NotifyIcon _trayIcon;
		private bool _allowClose;
		private volatile CheatSnapshot _cheatSnapshot = new CheatSnapshot();
		private volatile bool _gameHooksWanted;
		private bool _gameTickHooked;
		private int _gameTickAttachRetryCountdown;
		private int _mainThreadAttachProbePending;
		private bool _playerUpdateHookAttached;
		private Hook _playerUpdateDetour;
		private readonly object _hookAttachGate = new object();
		private volatile string _playerHookBackend = "未安装";
		private volatile string _playerHookError = string.Empty;
		private readonly ConcurrentQueue<Action> _pendingGameActions =
			new ConcurrentQueue<Action>();
		private bool _resetEffectsHookAttached;
		private bool _resetEffectsHookUnavailable;
		private bool _updateJumpHeightHookAttached;
		private bool _updateJumpHeightHookUnavailable;
		private bool _getRespawnTimeHookAttached;
		private bool _getRespawnTimeHookUnavailable;
		private Hook _updateBuffsDetour;
		private Hook _infiniteJumpMovementHook;
		private Hook _postUpdateEquipsHook;
		private bool _postUpdateEquipsHookUnavailable;
		private Hook _freeUpPetsAndMinionsHook;
		private bool _freeUpPetsAndMinionsHookUnavailable;
		private Hook _playerMaxBuffsGetterHook;
		private bool _buffCapacityHookUnavailable;
		private volatile bool _customBuffCapacityActive;
		private volatile bool _buffLoaderCapacityFieldModified;
		private volatile int _activeBuffCapacity;
		private int _baseBuffCapacity = 44;
		private int _baseExtraPlayerBuffCount;
		private int _lastAppliedBuffCapacity = -1;
		private FieldInfo _extraPlayerBuffCountField;
		private Hook _interactionRangeHook;
		private Hook _tileInteractionRangeHook;
		private Hook _multiTileInteractionRangeHook;
		private Hook _tileTypeInteractionRangeHook;
		private Hook _itemTargetRangeHook;
		private Hook _limitReachPointHook;
		private Hook _placeThingReachHook;
		private Hook _itemCheckReachHook;
		private Hook _chestRangeReachHook;
		private bool _unlimitedReachHookUnavailable;
		private bool _infiniteJumpHookUnavailable;
		private bool _updateBuffsHookUnavailable;
		private Type _calamityPlayerType;
		private MethodInfo _calamityGetModPlayer;
		private FieldInfo _calamityCooldownsField;
		private MethodInfo _calamitySyncCooldownRemoval;
		private int _calamityParryApiRetryCountdown;
		private bool _itemCheckCanUseHookAttached;
		private bool _itemCheckCanUseHookUnavailable;
		private bool _lightingHooksAttached;
		private bool _lightingHooksUnavailable;
		private Hook _viewTransformHook;
		private Hook _worldBorderHook;
		private Hook _cameraWorldClampHook;
		private long _worldBorderHookHits;
		private long _cameraClampHookHits;
		private bool _worldBoundsHookUnavailable;
		private Hook _viewInputHook;
		private ILHook _viewInitTargetsHook;
		private Hook _viewOverdrawHook;
		private bool _viewTransformHookUnavailable;
		private bool _viewRenderExpansionUnavailable;
		private bool _trainerViewZoomApplied;
		private float _gameZoomTargetBeforeTrainer;
		private float _unrestrictedGameZoomTarget;
		private uint _lastViewZoomUpdateCount = uint.MaxValue;
		private float _viewRenderTargetTier = 1F;
		private bool _viewTargetsNeedRebuild;
		private int _viewTargetRebuildDelay;
		private int _viewTargetRebuildFailures;
		private bool _viewTargetRebuildInProgress;
		private bool _viewVanillaRecoveryPending;
		private int _viewVanillaRenderTargetPadding = VanillaRenderTargetPadding;
		private MethodInfo _viewInitTargetsNoArgs;
		private FieldInfo _viewRenderTargetMaxSizeField;
		private FieldInfo _viewResizeBusyField;
		private int _viewVanillaRenderTargetMaxSize;
		private Hook _improveGameSelectionHook;
		private Assembly _improveGameHookAssembly;
		private bool _trainerGodModeApplied;
		private bool _godModeBeforeTrainer;
		private int _unloadedItemRepairCountdown;
		private bool _concurrentAttackInputWasDown;
		private bool _infiniteJumpInputWasDown;
		private volatile bool _forceConcurrentUseGate;
		private long _lastGameHeartbeatTicks;
		private long _lastLocalPlayerUpdateTicks;
		private volatile bool _gameWorldReady;
		private bool _worldSessionActive;
		private volatile string _lastGameThreadError = string.Empty;
		private readonly object _iconTaskLock = new object();
		private readonly Dictionary<int, Task<Bitmap>> _iconTasks =
			new Dictionary<int, Task<Bitmap>>();
		private readonly Dictionary<int, DateTime> _iconRetryAfter =
			new Dictionary<int, DateTime>();

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
		private CheckBox _highJump;
		private CheckBox _infiniteExtraJumps;
		private CheckBox _adjustableGravity;
		private CheckBox _adjustableMaxFallSpeed;
		private CheckBox _unlimitedFallSpeed;
		private CheckBox _instantRespawn;
		private CheckBox _adjustableStep;
		private CheckBox _infiniteFlight;
		private CheckBox _adjustableMaxMinions;
		private CheckBox _adjustableMaxBuffs;
		private CheckBox _fullBright;
		private CheckBox _unrestrictedView;
		private CheckBox _unrestrictedWorldBounds;
		private CheckBox _improveGameUnlimitedSelection;
		private CheckBox _unlimitedPlacementInteractionRange;
		private CheckBox _concurrentAttack;
		private CheckBox _shimmerPhaseCanOperate;
		private CheckBox _noCalamityParryCooldown;
		private NumericUpDown _movementMultiplier;
		private NumericUpDown _jumpMultiplier;
		private NumericUpDown _gravityMultiplier;
		private NumericUpDown _maxFallSpeed;
		private NumericUpDown _maxMinions;
		private NumericUpDown _maxBuffs;
		private NumericUpDown _stepBlocks;
		private Button _mapRevealButton;
		private Label _mapRevealStatusLabel;
		private volatile bool _mapRevealRequested;
		private volatile bool _mapRevealCancelRequested;
		private volatile bool _mapRevealActive;
		private volatile int _mapRevealDone;
		private volatile int _mapRevealTotal;
		private int _mapRevealX;
		private int _mapRevealY;
		private uint _lastMapRevealUpdateCount = uint.MaxValue;

		public TmlTrainerForm()
		{
			Text = "Terraria / tModLoader 全物品修改器";
			Size = new Size(1280, 760);
			MinimumSize = new Size(1100, 650);
			StartPosition = FormStartPosition.CenterScreen;
			Font = new Font("Microsoft YaHei UI", 9F);
			BackColor = Color.FromArgb(31, 33, 38);
			ForeColor = Color.WhiteSmoke;

			LoadCatalog();
			BuildUi();
			UpdateCheatSnapshot();
			AttachGameTick();

			_trayIcon = new NotifyIcon();
			_trayIcon.Icon = SystemIcons.Application;
			_trayIcon.Text = "Terraria / tModLoader 全物品修改器";
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
				DetachGameTick();
				DisposeIconSources();
			};

			_timer.Interval = 16;
			_timer.Tick += TimerTick;
			_timer.Start();
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
			_statusLabel.Text = "已注入 tModLoader；物品数据库：" + _allItems.Count + " 项";
			Controls.Add(_statusLabel);
		}

		private TabPage BuildBasicPage()
		{
			TabPage page = NewPage("基本修改");
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.AutoScroll = true;
			layout.Padding = new Padding(14, 12, 14, 16);
			layout.ColumnCount = 2;
			layout.RowCount = 5;
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			page.Controls.Add(layout);

			Label title = new Label();
			title.Text = "基础修改功能（tModLoader 游戏主线程 Hook）";
			title.Font = new Font(Font, FontStyle.Bold);
			title.Dock = DockStyle.Fill;
			title.TextAlign = ContentAlignment.MiddleLeft;
			title.Margin = new Padding(6, 0, 6, 6);
			layout.Controls.Add(title, 0, 0);
			layout.SetColumnSpan(title, 2);

			FlowLayoutPanel survival = AddFeatureSection(
				layout, "生存与防护", 0, 1);
			FlowLayoutPanel movement = AddFeatureSection(
				layout, "移动与物理", 1, 1);
			FlowLayoutPanel capacity = AddFeatureSection(
				layout, "容量与召唤", 0, 2);
			FlowLayoutPanel world = AddFeatureSection(
				layout, "世界、视野与建造", 1, 2);
			FlowLayoutPanel combat = AddFeatureSection(
				layout, "战斗与特殊状态", 0, 3, 2);

			_godMode = AddOption(survival, "无敌（上帝模式 + 持续免伤 + 生命/魔力/氧气回满）");
			_infiniteLife = AddOption(survival, "生命锁定（每帧回满）");
			_instantRespawn = AddOption(survival, "秒重生（死亡后下一帧复活）");
			_infiniteMana = AddOption(survival, "无限魔力");
			_infiniteBreath = AddOption(survival, "无限氧气");
			_noPotionCooldown = AddOption(survival, "无药水冷却");
			_clearDebuffs = AddOption(survival, "清除负面状态");
			_noKnockback = AddOption(survival, "免疫击退");
			_noFallDamage = AddOption(survival, "免疫摔落伤害");
			_lavaImmune = AddOption(survival, "熔岩免疫");

			_fastMovement = AddOption(movement, "高速移动");
			_movementMultiplier = AddMultiplierRow(
				movement, "移动倍率", 3M, delegate { UpdateCheatSnapshot(); });
			_highJump = AddOption(movement, "高跳");
			_infiniteExtraJumps = AddOption(movement, "无限段跳（空中可无限次触发二段跳）");
			_jumpMultiplier = AddMultiplierRow(
				movement, "跳跃高度倍率", 2.5M, delegate { UpdateCheatSnapshot(); });
			_adjustableGravity = AddOption(movement, "自定义自身重力倍率");
			_gravityMultiplier = AddDecimalRow(
				movement, "重力倍率", 1M, 0M, 100M, 0.05M,
				"x", delegate { UpdateCheatSnapshot(); });
			_adjustableMaxFallSpeed = AddOption(movement, "自定义最大下落速度");
			_maxFallSpeed = AddDecimalRow(
				movement, "最大下落速度", 10M, 0M, 10000M, 0.5M,
				"px/tick", delegate { UpdateCheatSnapshot(); });
			_unlimitedFallSpeed = AddOption(movement, "最大下落速度：无限制");
			_adjustableStep = AddOption(movement, "可调 Step（自动走上完整方块）");
			_stepBlocks = AddIntegerRow(
				movement, "最大整格数", 3M, delegate { UpdateCheatSnapshot(); });
			_infiniteFlight = AddOption(movement, "无限翅膀/火箭时间");

			_adjustableMaxMinions = AddOption(capacity, "自定义最大仆从容量");
			_maxMinions = AddIntegerValueRow(
				capacity, "最大仆从容量", 10M, 0M, 10000M, "个",
				delegate { UpdateCheatSnapshot(); });
			decimal originalBuffCapacity = 44M;
			try {
				originalBuffCapacity = Math.Max(1M, Player.MaxBuffs);
			}
			catch {
			}
			_adjustableMaxBuffs = AddOption(
				capacity,
				"自定义 Buff 栏上限（单人世界，最大同时存在数量）");
			_maxBuffs = AddIntegerValueRow(
				capacity, "最大 Buff 数量",
				Math.Max(100M, originalBuffCapacity),
				originalBuffCapacity,
				Math.Max(1000M, originalBuffCapacity),
				"个", delegate { UpdateCheatSnapshot(); });

			_fullBright = AddOption(world, "场景全亮（Fullbright，不再受地下黑暗影响）");
			_unrestrictedView = AddOption(
				world,
				"解除放大/缩小按键限制（使用游戏原有 ZoomIn / ZoomOut 键）");
			_unrestrictedWorldBounds = AddOption(world, "忽略世界边界（解除玩家和摄像边界）");
			_improveGameUnlimitedSelection = AddOption(
				world,
				"解除 ImproveGame 选择范围限制（液体魔杖、建筑/油漆等选择工具）");
			_unlimitedPlacementInteractionRange = AddOption(world, "无限放置、挖掘、交互距离");

			_concurrentAttack = AddOption(
				combat,
				"并发攻击（松开后再次按下可提前触发，已有弹射物不会清除）");
			_shimmerPhaseCanOperate = AddOption(
				combat,
				"微光穿墙可操作（保留微光相位/穿墙，但不再锁键和禁用物品）");

			_noCalamityParryCooldown = AddOption(
				combat,
				"灾厄 Parry 无冷却（移除通用格挡冷却；方舟系右键格挡结束后可立即再次触发）");

			_mapRevealButton = new Button();
			_mapRevealButton.Text = "点亮整个地图";
			StyleDarkButton(_mapRevealButton);
			_mapRevealButton.AutoSize = true;
			_mapRevealButton.MinimumSize = new Size(150, 32);
			_mapRevealButton.Margin = new Padding(3, 12, 3, 2);
			_mapRevealButton.Click += delegate {
				if (_mapRevealActive || _mapRevealRequested) {
					_mapRevealCancelRequested = true;
					_mapRevealButton.Text = "正在停止……";
				}
				else {
					_mapRevealCancelRequested = false;
					_mapRevealRequested = true;
					_mapRevealButton.Text = "取消点亮地图";
					_mapRevealStatusLabel.Text = "已提交到游戏主线程……";
				}
			};
			world.Controls.Add(_mapRevealButton);

			_mapRevealStatusLabel = new Label();
			_mapRevealStatusLabel.AutoSize = true;
			_mapRevealStatusLabel.MaximumSize = new Size(500, 0);
			_mapRevealStatusLabel.ForeColor = Color.Gainsboro;
			_mapRevealStatusLabel.Text = "地图点亮会分批扫描当前世界，不会卡住界面。";
			_mapRevealStatusLabel.Margin = new Padding(3, 2, 3, 3);
			world.Controls.Add(_mapRevealStatusLabel);

			foreach (CheckBox option in new[] {
				_godMode, _infiniteLife, _instantRespawn, _infiniteMana, _infiniteBreath,
				_noPotionCooldown, _clearDebuffs, _noKnockback,
				_noFallDamage, _lavaImmune, _fastMovement, _highJump, _infiniteExtraJumps, _adjustableStep,
				_adjustableGravity, _adjustableMaxFallSpeed, _unlimitedFallSpeed,
				_infiniteFlight, _adjustableMaxMinions, _adjustableMaxBuffs,
				_fullBright,
				_unrestrictedView, _unrestrictedWorldBounds,
				_improveGameUnlimitedSelection, _unlimitedPlacementInteractionRange, _concurrentAttack,
				_shimmerPhaseCanOperate, _noCalamityParryCooldown
			})
				option.CheckedChanged += delegate { UpdateCheatSnapshot(); };

			Label note = new Label();
			note.Dock = DockStyle.Fill;
			note.AutoSize = true;
			note.MaximumSize = new Size(1100, 0);
			note.Padding = new Padding(10, 9, 10, 9);
			note.Margin = new Padding(6, 10, 6, 4);
			note.ForeColor = Color.Gainsboro;
			note.BackColor = Color.FromArgb(38, 41, 48);
			note.Text = "效果由 Terraria 游戏主线程逐帧应用。移动/高跳倍率范围 1.0–10.0；Step 可调 1–10 个完整方块。并发攻击会正常消耗弹药/魔力；微光可操作只解除 frozen 锁键，保留相位穿墙。";
			layout.Controls.Add(note, 0, 4);
			layout.SetColumnSpan(note, 2);
			return page;
		}

		private TabPage BuildItemsPage()
		{
			TabPage page = NewPage("包裹 / 全物品");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(6);
			root.ColumnCount = 2;
			root.RowCount = 1;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(root);

			TableLayoutPanel left = new TableLayoutPanel();
			left.Dock = DockStyle.Fill;
			left.Margin = new Padding(0, 0, 5, 0);
			left.ColumnCount = 1;
			left.RowCount = 2;
			left.RowStyles.Add(new RowStyle(SizeType.Absolute, 98F));
			left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.Controls.Add(left, 0, 0);

			TableLayoutPanel searchPanel = new TableLayoutPanel();
			searchPanel.Dock = DockStyle.Fill;
			searchPanel.Margin = Padding.Empty;
			searchPanel.Padding = new Padding(3, 2, 3, 2);
			searchPanel.ColumnCount = 3;
			searchPanel.RowCount = 3;
			searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
			searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
			searchPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
			searchPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
			searchPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
			left.Controls.Add(searchPanel, 0, 0);

			Label searchLabel = new Label();
			searchLabel.Text = "搜索 ID / 中文名 / 英文名 / 内部名：";
			searchLabel.Dock = DockStyle.Fill;
			searchLabel.TextAlign = ContentAlignment.MiddleLeft;
			searchPanel.Controls.Add(searchLabel, 0, 0);
			searchPanel.SetColumnSpan(searchLabel, 3);

			_searchBox = new TextBox();
			_searchBox.Dock = DockStyle.Fill;
			_searchBox.Margin = new Padding(0, 3, 6, 3);
			_searchBox.TextChanged += delegate { ApplyItemFilter(); };
			searchPanel.Controls.Add(_searchBox, 0, 1);

			Button clearSearch = NewButton("清空搜索", 0, 0, 86);
			clearSearch.Dock = DockStyle.Fill;
			clearSearch.Margin = new Padding(0, 1, 6, 1);
			clearSearch.Click += delegate {
				_searchBox.Clear();
				_searchBox.Focus();
			};
			searchPanel.Controls.Add(clearSearch, 1, 1);

			_itemResultLabel = new Label();
			_itemResultLabel.Dock = DockStyle.Fill;
			_itemResultLabel.TextAlign = ContentAlignment.MiddleLeft;
			_itemResultLabel.ForeColor = Color.FromArgb(185, 215, 255);
			searchPanel.Controls.Add(_itemResultLabel, 2, 1);

			_selectedLabel = new Label();
			_selectedLabel.Text = "未选择物品";
			_selectedLabel.Dock = DockStyle.Fill;
			_selectedLabel.TextAlign = ContentAlignment.MiddleLeft;
			_selectedLabel.ForeColor = Color.Gold;
			searchPanel.Controls.Add(_selectedLabel, 0, 2);
			searchPanel.SetColumnSpan(_selectedLabel, 3);

			_itemGrid = CreateGrid();
			_itemGrid.Dock = DockStyle.Fill;
			_itemGrid.Margin = Padding.Empty;
			_itemGrid.DataSource = _filteredItems;
			AddTextColumn(_itemGrid, "ID", "Id", 62);
			AddFillTextColumn(_itemGrid, "中文名", "ChineseName", 105F, 120);
			AddFillTextColumn(_itemGrid, "英文名", "EnglishName", 95F, 110);
			AddFillTextColumn(_itemGrid, "内部名", "InternalName", 105F, 120);
			_itemGrid.SelectionChanged += ItemSelectionChanged;
			_itemGrid.CellDoubleClick += delegate { GiveSelectedItem(); };
			left.Controls.Add(_itemGrid, 0, 1);

			TableLayoutPanel right = new TableLayoutPanel();
			right.Dock = DockStyle.Fill;
			right.Margin = new Padding(5, 0, 0, 0);
			right.ColumnCount = 1;
			right.RowCount = 2;
			right.RowStyles.Add(new RowStyle(SizeType.Absolute, 158F));
			right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.Controls.Add(right, 1, 0);

			Panel rightTop = new Panel();
			rightTop.Dock = DockStyle.Top;
			rightTop.Height = 158;
			rightTop.Margin = Padding.Empty;
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
			rightTop.Controls.Add(NewLabel("右侧点击槽位后可替换；未选槽位时“获取”会正常加入背包。", 10, 128, 320));

			Panel inventoryHost = new Panel();
			inventoryHost.Dock = DockStyle.Fill;
			inventoryHost.Padding = new Padding(7);
			inventoryHost.BackColor = Color.FromArgb(25, 28, 36);
			inventoryHost.Margin = Padding.Empty;
			right.Controls.Add(inventoryHost, 0, 1);

			_inventoryGrid = new TmlInventorySlotGrid();
			_inventoryGrid.Dock = DockStyle.Fill;
			_inventoryGrid.ItemIconProvider = CreateItemIcon;
			_inventoryGrid.SelectedSlotChanged += InventorySelectionChanged;
			inventoryHost.Controls.Add(_inventoryGrid);

			ApplyItemFilter();
			RefreshInventory();
			return page;
		}

		protected override bool ShowWithoutActivation
		{
			get { return true; }
		}

		private TabPage BuildItemEditorPage()
		{
			TabPage page = NewPage("物品属性编辑");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(8);
			root.ColumnCount = 2;
			root.RowCount = 1;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(root);

			TableLayoutPanel inventorySide = new TableLayoutPanel();
			inventorySide.Dock = DockStyle.Fill;
			inventorySide.Margin = new Padding(0, 0, 6, 0);
			inventorySide.ColumnCount = 1;
			inventorySide.RowCount = 3;
			inventorySide.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
			inventorySide.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			inventorySide.RowStyles.Add(new RowStyle(SizeType.Absolute, 47F));
			root.Controls.Add(inventorySide, 0, 0);

			_itemEditorSelectionLabel = new Label();
			_itemEditorSelectionLabel.Dock = DockStyle.Fill;
			_itemEditorSelectionLabel.Padding = new Padding(6, 4, 6, 2);
			_itemEditorSelectionLabel.Text =
				"在下方图形背包中选择一个槽位，然后读取或写回属性。";
			_itemEditorSelectionLabel.ForeColor = Color.Gold;
			inventorySide.Controls.Add(_itemEditorSelectionLabel, 0, 0);

			Panel gridHost = new Panel();
			gridHost.Dock = DockStyle.Fill;
			gridHost.Margin = Padding.Empty;
			gridHost.Padding = new Padding(5);
			gridHost.BackColor = Color.FromArgb(25, 28, 36);
			inventorySide.Controls.Add(gridHost, 0, 1);

			_itemEditorGrid = new TmlInventorySlotGrid();
			_itemEditorGrid.Dock = DockStyle.Fill;
			_itemEditorGrid.ItemIconProvider = CreateItemIcon;
			_itemEditorGrid.SelectedSlotChanged += ItemEditorSelectionChanged;
			gridHost.Controls.Add(_itemEditorGrid);

			FlowLayoutPanel inventoryButtons = new FlowLayoutPanel();
			inventoryButtons.Dock = DockStyle.Fill;
			inventoryButtons.FlowDirection = FlowDirection.LeftToRight;
			inventoryButtons.WrapContents = false;
			inventoryButtons.Padding = new Padding(2, 6, 0, 0);
			inventorySide.Controls.Add(inventoryButtons, 0, 2);

			Button readButton = NewButton("读取选中槽位", 0, 0, 130);
			readButton.Click += delegate { QueueReadSelectedItemAttributes(); };
			inventoryButtons.Controls.Add(readButton);
			Button refreshButton = NewButton("刷新背包", 0, 0, 105);
			refreshButton.Click += delegate { RefreshInventory(); };
			inventoryButtons.Controls.Add(refreshButton);

			TableLayoutPanel editorSide = new TableLayoutPanel();
			editorSide.Dock = DockStyle.Fill;
			editorSide.Margin = new Padding(6, 0, 0, 0);
			editorSide.Padding = new Padding(8);
			editorSide.ColumnCount = 4;
			editorSide.RowCount = 15;
			editorSide.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
			editorSide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			editorSide.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
			editorSide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			editorSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
			for (int row = 1; row <= 12; row++)
				editorSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			editorSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
			editorSide.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.Controls.Add(editorSide, 1, 0);

			Label editorTitle = new Label();
			editorTitle.Text = "槽位物品字段（先读取，再修改数值并写回）";
			editorTitle.Dock = DockStyle.Fill;
			editorTitle.Font = new Font(Font, FontStyle.Bold);
			editorTitle.TextAlign = ContentAlignment.MiddleLeft;
			editorSide.Controls.Add(editorTitle, 0, 0);
			editorSide.SetColumnSpan(editorTitle, 4);

			string[] names = {
				"type", "stack", "damage", "knockBack",
				"useTime", "useAnimation", "scale", "shootSpeed", "shoot",
				"useAmmo", "pick", "axe", "hammer", "defense", "crit",
				"mana", "healLife", "healMana", "fishingPole"
			};
			string[] labels = {
				"物品 ID", "数量", "前缀 ID", "伤害", "击退",
				"使用间隔", "使用动画", "大小", "弹幕速度", "弹幕 ID",
				"弹药 ID", "镐力", "斧力", "锤力", "防御", "暴击",
				"魔力消耗", "生命恢复", "魔力恢复", "渔力"
			};

			for (int index = 0; index < names.Length; index++) {
				int row = 1 + index / 2;
				int pair = index % 2;
				AddItemEditorField(
					editorSide, row, pair * 2, labels[index], names[index]);
			}

			Label prefixLabel = new Label(); prefixLabel.Text = "前缀"; prefixLabel.Dock = DockStyle.Fill; prefixLabel.TextAlign = ContentAlignment.MiddleLeft;
			editorSide.Controls.Add(prefixLabel, 0, 11);
			FlowLayoutPanel prefixPanel = new FlowLayoutPanel(); prefixPanel.Dock = DockStyle.Fill; prefixPanel.FlowDirection = FlowDirection.LeftToRight; prefixPanel.WrapContents = false;
			_itemEditorPrefixBox = new ComboBox(); _itemEditorPrefixBox.DropDownStyle = ComboBoxStyle.DropDownList; _itemEditorPrefixBox.Width = 220; prefixPanel.Controls.Add(_itemEditorPrefixBox);
			Button applyPrefix = NewButton("应用前缀", 0, 0, 82); applyPrefix.Click += delegate { QueueApplySelectedItemPrefix(); }; prefixPanel.Controls.Add(applyPrefix);
			Button clearPrefix = NewButton("清除前缀", 0, 0, 82); clearPrefix.Click += delegate { QueuePrefixOperation(0, true, false); }; prefixPanel.Controls.Add(clearPrefix);
			Button randomPrefix = NewButton("随机重铸", 0, 0, 82); randomPrefix.Click += delegate { QueuePrefixOperation(-2, false, true); }; prefixPanel.Controls.Add(randomPrefix);
			editorSide.Controls.Add(prefixPanel, 1, 11); editorSide.SetColumnSpan(prefixPanel, 3);

			Label autoReuseLabel = new Label();
			autoReuseLabel.Text = "自动连用";
			autoReuseLabel.Dock = DockStyle.Fill;
			autoReuseLabel.TextAlign = ContentAlignment.MiddleLeft;
			editorSide.Controls.Add(autoReuseLabel, 0, 12);
			_itemEditorAutoReuse = new CheckBox();
			_itemEditorAutoReuse.Text = "按住持续使用";
			_itemEditorAutoReuse.AutoSize = true;
			_itemEditorAutoReuse.Anchor = AnchorStyles.Left;
			_itemEditorAutoReuse.CheckedChanged += delegate { if (!_applyingItemEditorSnapshot) _itemEditorChangedFields.Add("autoReuse"); };
			editorSide.Controls.Add(_itemEditorAutoReuse, 1, 12);
			editorSide.SetColumnSpan(_itemEditorAutoReuse, 3);

			FlowLayoutPanel writePanel = new FlowLayoutPanel();
			writePanel.Dock = DockStyle.Fill;
			writePanel.FlowDirection = FlowDirection.LeftToRight;
			writePanel.Padding = new Padding(0, 7, 0, 0);
			editorSide.Controls.Add(writePanel, 0, 13);
			editorSide.SetColumnSpan(writePanel, 4);

			Button readAgain = NewButton("从槽位读取", 0, 0, 125);
			readAgain.Click += delegate { QueueReadSelectedItemAttributes(); };
			writePanel.Controls.Add(readAgain);
			Button writeButton = NewButton("写回选中槽位", 0, 0, 145);
			writeButton.Click += delegate { QueueWriteSelectedItemAttributes(); };
			writePanel.Controls.Add(writeButton);

			_itemEditorStatusLabel = new Label();
			_itemEditorStatusLabel.Dock = DockStyle.Fill;
			_itemEditorStatusLabel.Padding = new Padding(2, 8, 2, 2);
			_itemEditorStatusLabel.ForeColor = Color.Gainsboro;
			_itemEditorStatusLabel.Text =
				"写入在 Terraria 主线程执行；个别版本缺少的字段会跳过，不会使游戏崩溃。";
			editorSide.Controls.Add(_itemEditorStatusLabel, 0, 14);
			editorSide.SetColumnSpan(_itemEditorStatusLabel, 4);

			RefreshInventory();
			return page;
		}

		private void AddItemEditorField(
			TableLayoutPanel layout, int row, int column, string labelText, string fieldName)
		{
			Label label = new Label();
			label.Text = labelText;
			label.Dock = DockStyle.Fill;
			label.TextAlign = ContentAlignment.MiddleLeft;
			layout.Controls.Add(label, column, row);

			NumericUpDown number = new NumericUpDown();
			number.Dock = DockStyle.Fill;
			number.Margin = new Padding(0, 5, 8, 5);
			if (Array.IndexOf(ItemFloatFields, fieldName) >= 0) {
				number.DecimalPlaces = 3;
				number.Increment = 0.05M;
				number.Minimum = -100000M;
				number.Maximum = 100000M;
			}
			else {
				number.DecimalPlaces = 0;
				number.Increment = 1M;
				number.Minimum = -1000000000M;
				number.Maximum = 1000000000M;
				if (fieldName == "type" || fieldName == "stack" ||
					fieldName == "prefix")
					number.Minimum = 0M;
				if (fieldName == "prefix")
					number.Maximum = 100000M;
			}
			number.ValueChanged += delegate { if (!_applyingItemEditorSnapshot) _itemEditorChangedFields.Add(fieldName); };
			layout.Controls.Add(number, column + 1, row);
			_itemEditorNumbers[fieldName] = number;
		}

		private void ItemEditorSelectionChanged(object sender, EventArgs e)
		{
			if (_itemEditorGrid == null)
				return;
			_selectedSlot = _itemEditorGrid.SelectedSlot;
			if (_inventoryGrid != null)
				_inventoryGrid.SelectedSlot = _selectedSlot;
			UpdateItemEditorSelectionText();
			QueueReadSelectedItemAttributes();
		}

		private void UpdateItemEditorSelectionText()
		{
			if (_itemEditorSelectionLabel == null)
				return;
			if (_selectedSlot < 0) {
				_itemEditorSelectionLabel.Text = "尚未选择槽位。";
				return;
			}
			InventoryEntry entry = _inventory.FirstOrDefault(
				delegate(InventoryEntry value) {
					return value != null && value.Slot == _selectedSlot;
				});
			_itemEditorSelectionLabel.Text = entry == null
				? "已选择槽位 " + _selectedSlot
				: string.Format(
					CultureInfo.InvariantCulture,
					"槽位 {0}  |  ID {1}  |  {2}  |  数量 {3}",
					entry.Slot, entry.Id, entry.Name, entry.Stack);
		}

		private void QueueReadSelectedItemAttributes()
		{
			int slot = _selectedSlot;
			if (slot < 0) {
				SetItemEditorStatus("请先在图形背包中选择一个槽位。", true);
				return;
			}
			SetItemEditorStatus("正在从槽位 " + slot + " 读取……", false);
			try {
				QueueGameThreadAction(delegate {
					ItemAttributeSnapshot snapshot =
						ReadItemAttributesOnGameThread(slot);
					PostToUi(delegate {
						ApplyItemAttributeSnapshot(snapshot);
					});
				});
			}
			catch (Exception ex) {
				SetItemEditorStatus("无法提交读取任务：" + ex.GetType().Name, true);
			}
		}

		private static ItemAttributeSnapshot ReadItemAttributesOnGameThread(int slot)
		{
			ItemAttributeSnapshot snapshot = new ItemAttributeSnapshot();
			snapshot.Slot = slot;
			try {
				Player player = Main.LocalPlayer;
				if (player == null || !player.active || player.inventory == null ||
					slot < 0 || slot >= player.inventory.Length) {
					snapshot.Error = "玩家或槽位当前不可用";
					return snapshot;
				}
				Item item = player.inventory[slot];
				if (item == null) {
					snapshot.Error = "槽位物品为空";
					return snapshot;
				}
				snapshot.ItemName = item.IsAir ? "(空)" : item.Name;
				snapshot.Prefix = item.prefix;
				snapshot.ApplicablePrefixes.Add(new PrefixEntry { Id = 0, Text = "[0 - 无前缀]" });
				for (int prefixId = 1; prefixId < PrefixLoader.PrefixCount; prefixId++) { if (!item.CanApplyPrefix(prefixId)) continue; Item probe = item.Clone(); probe.ResetPrefix(); if (probe.Prefix(prefixId)) snapshot.ApplicablePrefixes.Add(new PrefixEntry { Id = prefixId, Text = "[" + prefixId + " - " + probe.AffixName() + "]" }); }
				foreach (string fieldName in ItemIntegerFields) {
					object value;
					if (TryReadItemField(item, fieldName, out value))
						snapshot.Values[fieldName] = Convert.ToInt32(
							value, CultureInfo.InvariantCulture);
					else
						snapshot.MissingFields++;
				}
				foreach (string fieldName in ItemFloatFields) {
					object value;
					if (TryReadItemField(item, fieldName, out value))
						snapshot.Values[fieldName] = Convert.ToSingle(
							value, CultureInfo.InvariantCulture);
					else
						snapshot.MissingFields++;
				}
				object autoReuse;
				if (TryReadItemField(item, "autoReuse", out autoReuse))
					snapshot.Values["autoReuse"] = Convert.ToBoolean(
						autoReuse, CultureInfo.InvariantCulture);
				else
					snapshot.MissingFields++;
			}
			catch (Exception ex) {
				snapshot.Error = ex.GetType().Name + ": " + ex.Message;
			}
			return snapshot;
		}

		private void ApplyItemAttributeSnapshot(ItemAttributeSnapshot snapshot)
		{
			if (snapshot == null) {
				SetItemEditorStatus("读取失败：没有返回数据。", true);
				return;
			}
			if (!string.IsNullOrEmpty(snapshot.Error)) {
				SetItemEditorStatus("读取失败：" + snapshot.Error, true);
				return;
			}
			_applyingItemEditorSnapshot = true;
			foreach (KeyValuePair<string, NumericUpDown> pair in _itemEditorNumbers) {
				object value;
				if (!snapshot.Values.TryGetValue(pair.Key, out value))
					continue;
				decimal decimalValue;
				try {
					decimalValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
				}
				catch {
					continue;
				}
				pair.Value.Value = Math.Max(
					pair.Value.Minimum, Math.Min(pair.Value.Maximum, decimalValue));
			}
			object autoReuse;
			if (_itemEditorAutoReuse != null &&
				snapshot.Values.TryGetValue("autoReuse", out autoReuse)) {
				try {
					_itemEditorAutoReuse.Checked = Convert.ToBoolean(
						autoReuse, CultureInfo.InvariantCulture);
				}
				catch {
				}
			}
			if (_itemEditorPrefixBox != null) { _itemEditorPrefixBox.BeginUpdate(); try { _itemEditorPrefixBox.Items.Clear(); foreach (PrefixEntry entry in snapshot.ApplicablePrefixes) _itemEditorPrefixBox.Items.Add(entry); for (int i = 0; i < _itemEditorPrefixBox.Items.Count; i++) { PrefixEntry entry = _itemEditorPrefixBox.Items[i] as PrefixEntry; if (entry != null && entry.Id == snapshot.Prefix) { _itemEditorPrefixBox.SelectedIndex = i; break; } } } finally { _itemEditorPrefixBox.EndUpdate(); } }
			_applyingItemEditorSnapshot = false;
			_itemEditorChangedFields.Clear();
			SetItemEditorStatus(
				string.Format(
					CultureInfo.InvariantCulture,
					"已读取槽位 {0}：{1}{2}",
					snapshot.Slot,
					string.IsNullOrEmpty(snapshot.ItemName)
						? "(未知物品)"
						: snapshot.ItemName,
					snapshot.MissingFields > 0
						? "；跳过 " + snapshot.MissingFields + " 个不可用字段"
						: string.Empty),
				false);
		}

		private void QueueApplySelectedItemPrefix() { PrefixEntry entry = _itemEditorPrefixBox == null ? null : _itemEditorPrefixBox.SelectedItem as PrefixEntry; if (entry == null) { SetItemEditorStatus("请选择可用前缀", true); return; } QueuePrefixOperation(entry.Id, false, false); }
		private void QueuePrefixOperation(int prefixId, bool clear, bool random)
		{
			int slot = _selectedSlot; if (slot < 0) { SetItemEditorStatus("请先选择槽位", true); return; }
			QueueGameThreadAction(delegate { string error = null; try { Player player = Main.LocalPlayer; if (player == null || !player.active || player.inventory == null || slot >= player.inventory.Length) throw new InvalidOperationException("玩家或槽位当前不可用"); Item item = player.inventory[slot]; if (item == null || item.IsAir) throw new InvalidOperationException("槽位没有物品"); item.ResetPrefix(); if (!clear) { if (!random && !item.CanApplyPrefix(prefixId)) throw new InvalidOperationException("该前缀不适用于当前物品"); if (!item.Prefix(prefixId)) throw new InvalidOperationException("前缀应用失败"); } SyncSlot(player, slot); } catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; } PostToUi(delegate { if (!string.IsNullOrEmpty(error)) SetItemEditorStatus("前缀操作失败：" + error, true); else { SetItemEditorStatus(clear ? "已清除前缀。" : (random ? "已随机重铸前缀。" : "已应用前缀。"), false); RefreshInventory(); QueueReadSelectedItemAttributes(); } }); });
		}

		private void QueueWriteSelectedItemAttributes()
		{
			int slot = _selectedSlot;
			if (slot < 0) {
				SetItemEditorStatus("请先选择槽位。", true);
				return;
			}
			ItemAttributeSnapshot requested = new ItemAttributeSnapshot();
			requested.Slot = slot;
			foreach (KeyValuePair<string, NumericUpDown> pair in _itemEditorNumbers) {
				requested.Values[pair.Key] =
					Array.IndexOf(ItemFloatFields, pair.Key) >= 0
						? (object)Decimal.ToSingle(pair.Value.Value)
						: Decimal.ToInt32(pair.Value.Value);
			}
			requested.Values["autoReuse"] =
				_itemEditorAutoReuse != null && _itemEditorAutoReuse.Checked;
			SetItemEditorStatus("正在写回槽位 " + slot + "……", false);

			try {
				QueueGameThreadAction(delegate {
					int missing = 0;
					string error = null;
					try {
						Player player = Main.LocalPlayer;
						if (player == null || !player.active ||
							player.inventory == null || requested.Slot < 0 ||
							requested.Slot >= player.inventory.Length)
							throw new InvalidOperationException("玩家或槽位当前不可用");

						Item item = player.inventory[requested.Slot];
						if (item == null)
							throw new InvalidOperationException("槽位物品为空");

						object typeValue;
						int requestedType = requested.Values.TryGetValue(
							"type", out typeValue)
							? Convert.ToInt32(typeValue, CultureInfo.InvariantCulture)
							: item.type;
						if (requestedType < 0 ||
							requestedType >= ItemLoader.ItemCount)
							throw new ArgumentOutOfRangeException(
								"type",
								"物品 ID 必须在 0 到 " +
								(ItemLoader.ItemCount - 1) + " 之间");
						if (IsUnloadedItemType(requestedType))
							throw new InvalidOperationException(
								"该 ID 是 tModLoader 内部的失效模组占位物品，" +
								"直接生成会导致人物保存失败。");

						if (requestedType == 0) {
							item.TurnToAir();
						}
						else {
							if (requestedType != item.type)
								item.SetDefaults(requestedType);

							foreach (KeyValuePair<string, object> pair in requested.Values) {
								// type is validated above and is never raw-written.
								if (pair.Key == "type")
									continue;
								if (!TryWriteItemField(item, pair.Key, pair.Value))
									missing++;
							}
						}
						SyncSlot(player, requested.Slot);
					}
					catch (Exception ex) {
						error = ex.GetType().Name + ": " + ex.Message;
					}

					PostToUi(delegate {
						if (!string.IsNullOrEmpty(error))
							SetItemEditorStatus("写回失败：" + error, true);
						else {
							SetItemEditorStatus(
								"已写回槽位 " + requested.Slot +
								(missing > 0 ? "；跳过 " + missing + " 个不可用字段" : string.Empty),
								false);
							RefreshInventory();
							QueueReadSelectedItemAttributes();
						}
					});
				});
			}
			catch (Exception ex) {
				SetItemEditorStatus("无法提交写入任务：" + ex.GetType().Name, true);
			}
		}

		private static bool TryReadItemField(
			Item item, string fieldName, out object value)
		{
			value = null;
			try {
				FieldInfo field = typeof(Item).GetField(
					fieldName,
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance);
				if (field == null)
					return false;
				value = field.GetValue(item);
				return true;
			}
			catch {
				return false;
			}
		}

		private static bool TryWriteItemField(
			Item item, string fieldName, object value)
		{
			try {
				FieldInfo field = typeof(Item).GetField(
					fieldName,
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance);
				if (field == null || field.IsInitOnly)
					return false;
				Type targetType = Nullable.GetUnderlyingType(field.FieldType) ??
					field.FieldType;
				object converted = targetType.IsEnum
					? Enum.ToObject(targetType, Convert.ToInt32(
						value, CultureInfo.InvariantCulture))
					: Convert.ChangeType(
						value, targetType, CultureInfo.InvariantCulture);
				field.SetValue(item, converted);
				return true;
			}
			catch {
				return false;
			}
		}

		private void SetItemEditorStatus(string text, bool error)
		{
			if (_itemEditorStatusLabel == null)
				return;
			_itemEditorStatusLabel.Text = text;
			_itemEditorStatusLabel.ForeColor = error
				? Color.FromArgb(255, 145, 145)
				: Color.Gainsboro;
		}

		private void PostToUi(Action action)
		{
			try {
				if (IsDisposed || !IsHandleCreated)
					return;
				BeginInvoke(action);
			}
			catch {
			}
		}

		private TabPage BuildRemoteInventoryPage()
		{
			TabPage page = NewPage("别人背包");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.ColumnCount = 1;
			root.RowCount = 2;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(root);

			Panel top = new Panel();
			top.Dock = DockStyle.Fill;
			top.Margin = Padding.Empty;
			root.Controls.Add(top, 0, 0);

			top.Controls.Add(NewLabel("选择在线玩家：", 12, 11, 115));
			_remotePlayerBox = new ComboBox();
			_remotePlayerBox.DropDownStyle = ComboBoxStyle.DropDownList;
			_remotePlayerBox.SetBounds(126, 8, 300, 28);
			_remotePlayerBox.SelectedIndexChanged += RemotePlayerSelectionChanged;
			top.Controls.Add(_remotePlayerBox);

			_remoteInventoryStatus = NewLabel("正在等待其他在线玩家……", 442, 11, 650);
			_remoteInventoryStatus.ForeColor = Color.Gold;
			top.Controls.Add(_remoteInventoryStatus);

			_remoteSelectionLabel = NewLabel("尚未选择槽位", 12, 44, 1140);
			_remoteSelectionLabel.ForeColor = Color.FromArgb(185, 215, 255);
			top.Controls.Add(_remoteSelectionLabel);

			top.Controls.Add(NewLabel("给予数量：", 12, 79, 75));
			_remoteStackBox = new NumericUpDown();
			_remoteStackBox.SetBounds(88, 75, 90, 27);
			_remoteStackBox.Minimum = 1;
			_remoteStackBox.Maximum = 9999;
			_remoteStackBox.Value = 1;
			top.Controls.Add(_remoteStackBox);

			_remoteGiveButton = NewButton("给予自己", 190, 72, 120);
			_remoteGiveButton.Enabled = false;
			_remoteGiveButton.Click += delegate { GiveSelectedRemoteItem(); };
			top.Controls.Add(_remoteGiveButton);

			_remoteFindButton = NewButton("在全物品页查找", 320, 72, 160);
			_remoteFindButton.Enabled = false;
			_remoteFindButton.Click += delegate { FindSelectedRemoteItemInCatalog(); };
			top.Controls.Add(_remoteFindButton);

			_remoteCopyIdButton = NewButton("复制 ID", 490, 72, 110);
			_remoteCopyIdButton.Enabled = false;
			_remoteCopyIdButton.Click += delegate { CopySelectedRemoteItemId(); };
			top.Controls.Add(_remoteCopyIdButton);

			Label explanation = NewLabel(
				"点击槽位只会选中并读取；只读显示网络同步到本客户端的槽位 0–58：主背包、钱币、弹药和鼠标物品。" +
				"私有银行、保险箱、虚空袋及独立垃圾槽不会显示。",
				12, 48, 1120);
			explanation.AutoSize = false;
			explanation.Height = 44;
			explanation.Dock = DockStyle.Bottom;
			explanation.Padding = new Padding(12, 4, 12, 0);
			explanation.ForeColor = Color.Gainsboro;
			top.Controls.Add(explanation);

			Panel inventoryHost = new Panel();
			inventoryHost.Dock = DockStyle.Fill;
			inventoryHost.Padding = new Padding(12);
			inventoryHost.BackColor = Color.FromArgb(25, 28, 36);
			inventoryHost.Margin = Padding.Empty;
			root.Controls.Add(inventoryHost, 0, 1);

			_remoteInventoryGrid = new TmlInventorySlotGrid();
			_remoteInventoryGrid.Dock = DockStyle.Fill;
			_remoteInventoryGrid.ItemIconProvider = CreateItemIcon;
			_remoteInventoryGrid.SelectedSlotChanged += RemoteInventorySelectionChanged;
			inventoryHost.Controls.Add(_remoteInventoryGrid);

			RefreshRemoteInventory();
			return page;
		}

		private TabPage BuildHistoryPage()
		{
			TabPage page = NewPage("别人拿出记录");
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.ColumnCount = 1;
			root.RowCount = 2;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			page.Controls.Add(root);

			Panel top = new Panel();
			top.Dock = DockStyle.Fill;
			top.Margin = Padding.Empty;
			root.Controls.Add(top, 0, 0);
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
			_historyGrid.Margin = Padding.Empty;
			root.Controls.Add(_historyGrid, 0, 1);
			return page;
		}

		private void TimerTick(object sender, EventArgs e)
		{
			try {
				UpdateMapRevealUi();
				RestoreBuffCapacityOutsideSinglePlayerFromUiThread();
				bool worldReady = TryProbeCurrentWorld();
				long heartbeat = Interlocked.Read(
					ref _lastGameHeartbeatTicks);
				long heartbeatAge = heartbeat <= 0L
					? long.MaxValue
					: DateTime.UtcNow.Ticks - heartbeat;
				if (heartbeat <= 0L || heartbeatAge < 0L ||
					heartbeatAge > TimeSpan.TicksPerSecond * 3L) {
					if (_gameTickAttachRetryCountdown-- <= 0) {
						_gameTickAttachRetryCountdown = 60;
						TryAttachGameTick();
					}
				}
				else {
					_gameTickAttachRetryCountdown = 0;
				}
				if (!worldReady) {
					SetConnectionStatus(
						"已注入 tModLoader（PID " + Environment.ProcessId +
						"，Player Hook：" + _playerHookBackend +
						"）；等待进入世界。", true);
					return;
				}

				long age = heartbeat <= 0L
					? long.MaxValue
					: DateTime.UtcNow.Ticks - heartbeat;
				if (age < 0L || age > TimeSpan.TicksPerSecond * 3L) {
					SetConnectionStatus(
						"已读取当前世界（PID " + Environment.ProcessId +
						"），正在重建 Player Hook：" + _playerHookBackend +
						(string.IsNullOrEmpty(_playerHookError)
							? string.Empty
							: "；" + _playerHookError), false);
				}
				else {
					SetConnectionStatus(
						"已连接当前世界（PID " + Environment.ProcessId +
						"，Player Hook：" + _playerHookBackend + "）。", false);
				}
				TrackHeldItems();
				if (++_slowTick >= 30) {
					_slowTick = 0;
					RefreshInventory();
					RefreshRemoteInventory();
					UpdateStatus();
				}
			}
			catch (Exception ex) {
				_statusLabel.Text =
					"本帧游戏数据读取未完成：" +
					DescribeException(ex) + "；将自动重试。";
			}
		}

		private static string DescribeException(Exception exception)
		{
			Exception root = exception;
			while (root.InnerException != null)
				root = root.InnerException;
			string message = root.Message;
			if (string.IsNullOrWhiteSpace(message))
				return root.GetType().Name;
			if (message.Length > 100)
				message = message.Substring(0, 100);
			return root.GetType().Name + " - " + message;
		}

		private bool TryProbeCurrentWorld()
		{
			try {
				Player player = Main.LocalPlayer;
				bool ready = !Main.gameMenu && IsLocalPlayer(player);
				_gameWorldReady = ready;
				if (!ready)
					_worldSessionActive = false;
				return ready;
			}
			catch (Exception ex) {
				_gameWorldReady = false;
				_lastGameThreadError = DescribeException(ex);
				return false;
			}
		}

		private void SetConnectionStatus(string text, bool force)
		{
			if (_statusLabel == null)
				return;
			string current = _statusLabel.Text ?? string.Empty;
			if (force || current.Length == 0 ||
				current.StartsWith("等待", StringComparison.Ordinal) ||
				current.StartsWith("游戏主线程", StringComparison.Ordinal) ||
				current.StartsWith("本帧", StringComparison.Ordinal) ||
				current.StartsWith("已读取当前世界", StringComparison.Ordinal) ||
				current.StartsWith("已连接当前世界", StringComparison.Ordinal) ||
				current.StartsWith("已注入 tModLoader", StringComparison.Ordinal))
				_statusLabel.Text = text;
		}

		private void QueueGameThreadAction(Action action)
		{
			if (action == null)
				return;
			int executed = 0;
			Action guarded = delegate {
				if (Interlocked.Exchange(ref executed, 1) != 0)
					return;
				try { action(); }
				catch (Exception ex) {
					_lastGameThreadError =
						"游戏操作：" + DescribeException(ex);
				}
			};

			// Whichever path reaches the game thread first wins.  The guarded
			// action makes the official queue and Player.Update queue coexist
			// without ever applying an inventory operation twice.
			_pendingGameActions.Enqueue(guarded);
			try {
				Main.QueueMainThreadAction(guarded);
			}
			catch (Exception ex) {
				_lastGameThreadError =
					"官方主线程队列：" + DescribeException(ex);
			}
		}

		private void DrainPendingGameActions()
		{
			Action action;
			int count = 0;
			while (count++ < 128 &&
				_pendingGameActions.TryDequeue(out action)) {
				try { action(); }
				catch (Exception ex) {
					_lastGameThreadError =
						"Player Hook 队列：" + DescribeException(ex);
				}
			}
		}

		private void UpdateMapRevealUi()
		{
			if (_mapRevealButton == null || _mapRevealStatusLabel == null)
				return;
			if (_mapRevealActive || _mapRevealRequested) {
				int total = Math.Max(1, _mapRevealTotal);
				int done = Math.Max(0, Math.Min(total, _mapRevealDone));
				int percent = (int)((long)done * 100L / total);
				_mapRevealButton.Text = _mapRevealCancelRequested
					? "正在停止……"
					: "取消点亮地图";
				_mapRevealStatusLabel.Text =
					"正在点亮当前世界：" + percent + "%  (" +
					done.ToString("N0") + " / " + total.ToString("N0") + ")";
				return;
			}

			_mapRevealButton.Text = "点亮整个地图";
			if (_mapRevealTotal > 0 && _mapRevealDone >= _mapRevealTotal)
				_mapRevealStatusLabel.Text =
					"地图已全部点亮；打开小地图/全屏地图即可查看。";
			else if (_mapRevealCancelRequested)
				_mapRevealStatusLabel.Text = "地图点亮已停止。";
		}

		private void UpdateCheatSnapshot()
		{
			try {
				_cheatSnapshot = new CheatSnapshot {
					GodMode = _godMode != null && _godMode.Checked,
					InfiniteLife = _infiniteLife != null && _infiniteLife.Checked,
					InfiniteMana = _infiniteMana != null && _infiniteMana.Checked,
					InfiniteBreath = _infiniteBreath != null && _infiniteBreath.Checked,
					NoPotionCooldown = _noPotionCooldown != null &&
						_noPotionCooldown.Checked,
					ClearDebuffs = _clearDebuffs != null && _clearDebuffs.Checked,
					NoKnockback = _noKnockback != null && _noKnockback.Checked,
					NoFallDamage = _noFallDamage != null && _noFallDamage.Checked,
					LavaImmune = _lavaImmune != null && _lavaImmune.Checked,
					FastMovement = _fastMovement != null && _fastMovement.Checked,
					HighJump = _highJump != null && _highJump.Checked,
					InfiniteExtraJumps = _infiniteExtraJumps != null && _infiniteExtraJumps.Checked,
					AdjustableGravity = _adjustableGravity != null && _adjustableGravity.Checked,
					AdjustableMaxFallSpeed = _adjustableMaxFallSpeed != null && _adjustableMaxFallSpeed.Checked,
					UnlimitedFallSpeed = _unlimitedFallSpeed != null && _unlimitedFallSpeed.Checked,
					InstantRespawn = _instantRespawn != null && _instantRespawn.Checked,
					AdjustableStep = _adjustableStep != null && _adjustableStep.Checked,
					InfiniteFlight = _infiniteFlight != null &&
						_infiniteFlight.Checked,
					AdjustableMaxMinions = _adjustableMaxMinions != null &&
						_adjustableMaxMinions.Checked,
					AdjustableMaxBuffs = _adjustableMaxBuffs != null &&
						_adjustableMaxBuffs.Checked,
					FullBright = _fullBright != null && _fullBright.Checked,
					UnrestrictedView = _unrestrictedView != null &&
						_unrestrictedView.Checked,
					UnrestrictedWorldBounds = _unrestrictedWorldBounds != null && _unrestrictedWorldBounds.Checked,
					ImproveGameUnlimitedSelection =
						_improveGameUnlimitedSelection != null &&
						_improveGameUnlimitedSelection.Checked,
					UnlimitedPlacementInteractionRange =
						_unlimitedPlacementInteractionRange != null &&
						_unlimitedPlacementInteractionRange.Checked,
					ConcurrentAttack = _concurrentAttack != null &&
						_concurrentAttack.Checked,
					ShimmerPhaseCanOperate = _shimmerPhaseCanOperate != null &&
						_shimmerPhaseCanOperate.Checked,
					NoCalamityParryCooldown = _noCalamityParryCooldown != null &&
						_noCalamityParryCooldown.Checked,
					MovementMultiplier = _movementMultiplier == null
						? 3F
						: Decimal.ToSingle(_movementMultiplier.Value),
					JumpMultiplier = _jumpMultiplier == null
						? 2.5F
						: Decimal.ToSingle(_jumpMultiplier.Value),
					GravityMultiplier = _gravityMultiplier == null ? 1F : Decimal.ToSingle(_gravityMultiplier.Value),
					MaxFallSpeed = _maxFallSpeed == null ? 10F : Decimal.ToSingle(_maxFallSpeed.Value),
					MaxMinions = _maxMinions == null
						? 10
						: Decimal.ToInt32(_maxMinions.Value),
					MaxBuffs = _maxBuffs == null
						? 100
						: Decimal.ToInt32(_maxBuffs.Value),
					StepBlocks = _stepBlocks == null
						? 3
						: Decimal.ToInt32(_stepBlocks.Value)
				};
			}
			catch {
				// Keep the previous immutable snapshot if a control is already
				// being disposed.
			}
		}

		private void AttachGameTick()
		{
			_gameHooksWanted = true;
			TryAttachGameTick();
		}

		private void TryAttachGameTick()
		{
			if (!_gameHooksWanted)
				return;
			lock (_hookAttachGate) {
				AttachTickAndPlayerHooksDirectly();
			}

			// QueueMainThreadAction is only a single-flight backup probe.  It is
			// deliberately not the gate that installs the primary Player hook.
			if (Interlocked.CompareExchange(
				ref _mainThreadAttachProbePending, 1, 0) != 0)
				return;
			try {
				Main.QueueMainThreadAction(delegate {
					try { AttachGameHooksOnGameThread(); }
					finally {
						Interlocked.Exchange(
							ref _mainThreadAttachProbePending, 0);
					}
				});
			}
			catch (Exception ex) {
				Interlocked.Exchange(ref _mainThreadAttachProbePending, 0);
				_lastGameThreadError =
					"主线程队列探针：" + DescribeException(ex);
			}
		}

		private void AttachTickAndPlayerHooksDirectly()
		{
			if (!_gameTickHooked) {
				try {
					Main.OnTickForThirdPartySoftwareOnly += GameThreadTick;
					_gameTickHooked = true;
				}
				catch (Exception ex) {
					_gameTickHooked = false;
					_lastGameThreadError =
						"第三方 Tick：" + DescribeException(ex);
				}
			}
			AttachPlayerHooksOnGameThread();
		}

		private void AttachGameHooksOnGameThread()
		{
			if (!_gameHooksWanted)
				return;
			Interlocked.Exchange(
				ref _lastGameHeartbeatTicks,
				DateTime.UtcNow.Ticks);
			lock (_hookAttachGate) {
				AttachTickAndPlayerHooksDirectly();
			}
		}

		private void DetachGameTick()
		{
			_gameHooksWanted = false;
			_forceConcurrentUseGate = false;
			_concurrentAttackInputWasDown = false;
			_gameWorldReady = false;
			_worldSessionActive = false;
			Interlocked.Exchange(ref _lastLocalPlayerUpdateTicks, 0L);
			_mapRevealRequested = false;
			_mapRevealCancelRequested = true;
			_cheatSnapshot = new CheatSnapshot();
			if (_gameTickHooked) {
				try {
					Main.OnTickForThirdPartySoftwareOnly -= GameThreadTick;
				}
				catch {
				}
				_gameTickHooked = false;
			}
			try {
			QueueGameThreadAction(delegate {
					try {
						RestoreTrainerOwnedStateOnGameThread();
						DetachImproveGameSelectionHookOnGameThread();
						DetachPlayerHooksOnGameThread();
					}
					catch {
					}
				});
			}
			catch {
			}
		}

		private void MainUpdateHook(
			On_Main.orig_Update orig,
			Main self,
			Microsoft.Xna.Framework.GameTime gameTime)
		{
			orig(self, gameTime);
			GameThreadTick();
		}

		private void AttachPlayerHooksOnGameThread()
		{
			if (!_gameHooksWanted)
				return;
			if (!_playerUpdateHookAttached && _playerUpdateDetour == null) {
				// RuntimeDetour binds to the actual Player.Update MethodInfo in the
				// running process and therefore does not depend on HookGen ABI.
				TryAttachPlayerRuntimeDetour();
				if (_playerUpdateDetour == null) {
					try {
						On_Player.Update += PlayerUpdateHook;
						_playerUpdateHookAttached = true;
						_playerHookBackend = "HookGen 后备";
						_playerHookError = string.Empty;
					}
					catch (Exception ex) {
						_playerUpdateHookAttached = false;
						_playerHookBackend = "安装失败";
						_playerHookError =
							"HookGen：" + DescribeException(ex);
					}
				}
			}
			CheatSnapshot snapshot = _cheatSnapshot;
			if (!_resetEffectsHookAttached && !_resetEffectsHookUnavailable &&
				(snapshot.NoKnockback || snapshot.NoFallDamage ||
				snapshot.LavaImmune || snapshot.FastMovement ||
				snapshot.AdjustableMaxMinions)) {
				try {
					On_Player.ResetEffects += PlayerResetEffectsHook;
					_resetEffectsHookAttached = true;
				}
				catch (Exception ex) {
					_resetEffectsHookAttached = false;
					_resetEffectsHookUnavailable = true;
					_lastGameThreadError =
						"ResetEffects Hook：" + DescribeException(ex);
				}
			}
			if (!_updateJumpHeightHookAttached &&
				!_updateJumpHeightHookUnavailable && snapshot.HighJump) {
				try {
					On_Player.UpdateJumpHeight += PlayerUpdateJumpHeightHook;
					_updateJumpHeightHookAttached = true;
				}
				catch (Exception ex) {
					_updateJumpHeightHookAttached = false;
					_updateJumpHeightHookUnavailable = true;
					_lastGameThreadError =
						"跳跃 Hook：" + DescribeException(ex);
				}
			}
			if (!_getRespawnTimeHookAttached &&
				!_getRespawnTimeHookUnavailable && snapshot.InstantRespawn) {
				try {
					On_Player.GetRespawnTime += PlayerGetRespawnTimeHook;
					_getRespawnTimeHookAttached = true;
				}
				catch (Exception ex) {
					_getRespawnTimeHookAttached = false;
					_getRespawnTimeHookUnavailable = true;
					_lastGameThreadError =
						"重生 Hook：" + DescribeException(ex);
				}
			}
			if (_infiniteJumpMovementHook == null &&
				!_infiniteJumpHookUnavailable && snapshot.InfiniteExtraJumps)
				TryAttachInfiniteJumpMovementHook();
			if (_postUpdateEquipsHook == null &&
				!_postUpdateEquipsHookUnavailable &&
				snapshot.AdjustableMaxMinions)
				TryAttachPostUpdateEquipsHook();
			if (_freeUpPetsAndMinionsHook == null &&
				!_freeUpPetsAndMinionsHookUnavailable &&
				snapshot.AdjustableMaxMinions)
				TryAttachFreeUpPetsAndMinionsHook();
			if (_updateBuffsDetour == null &&
				!_updateBuffsHookUnavailable &&
				(snapshot.ShimmerPhaseCanOperate || snapshot.AdjustableGravity ||
				 snapshot.AdjustableMaxFallSpeed || snapshot.UnlimitedFallSpeed))
				TryAttachUpdateBuffsRuntimeDetour();
			if (!_itemCheckCanUseHookAttached &&
				!_itemCheckCanUseHookUnavailable && snapshot.ConcurrentAttack) {
				try {
					On_Player.ItemCheck_CheckCanUse +=
						PlayerItemCheckCanUseHook;
					_itemCheckCanUseHookAttached = true;
				}
				catch (Exception ex) {
					_itemCheckCanUseHookAttached = false;
					_itemCheckCanUseHookUnavailable = true;
					_lastGameThreadError =
						"攻击门控 Hook：" + DescribeException(ex);
				}
			}
			if (snapshot.FullBright && !_lightingHooksUnavailable)
				AttachLightingHooksOnGameThread();
		}

		private void TryAttachPlayerRuntimeDetour()
		{
			if (_playerUpdateDetour != null || !_gameHooksWanted)
				return;
			try {
				MethodInfo update = typeof(Player).GetMethod(
					"Update",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance,
					null,
					new Type[] { typeof(int) },
					null);
				if (update == null)
					throw new MissingMethodException(typeof(Player).FullName, "Update(int)");
				_playerUpdateDetour = new Hook(
					update,
					new PlayerUpdateDetour(PlayerUpdateRuntimeDetourHook));
				_playerHookBackend = "RuntimeDetour";
				_playerHookError = string.Empty;
			}
			catch (Exception ex) {
				_playerUpdateDetour = null;
				_playerHookBackend = "安装失败";
				_playerHookError =
					"RuntimeDetour：" + DescribeException(ex);
			}
		}

		private void TryAttachInfiniteJumpMovementHook()
		{
			if (_infiniteJumpMovementHook != null || !_gameHooksWanted)
				return;
			try {
				MethodInfo method = typeof(PlayerLoader).GetMethod(
					"PreUpdateMovement", BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.Static,
					null, new Type[] { typeof(Player) }, null);
				if (method == null)
					throw new MissingMethodException(
						typeof(PlayerLoader).FullName, "PreUpdateMovement(Player)");
				_infiniteJumpMovementHook = new Hook(
					method, new PreUpdateMovementDetour(
						PlayerLoaderPreUpdateMovementHook));
			}
			catch (Exception ex) {
				_infiniteJumpMovementHook = null;
				_infiniteJumpHookUnavailable = true;
				_lastGameThreadError =
					"Infinite extra jump hook: " + DescribeException(ex);
			}
		}

		private void PlayerLoaderPreUpdateMovementHook(
			PreUpdateMovementOrig orig, Player player)
		{
			orig(player);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (!IsLocalPlayer(player))
					return;
				if (snapshot == null || !snapshot.InfiniteExtraJumps) {
					_infiniteJumpInputWasDown = false;
					return;
				}
				bool inputDown = player.controlJump;
				bool newPress = inputDown && !_infiniteJumpInputWasDown;
				_infiniteJumpInputWasDown = inputDown;
				if (!newPress || player.dead ||
					player.jump > 0 || player.pulley || player.grapCount > 0 ||
					player.mount != null && player.mount.Active)
					return;
				float direction = player.gravDir >= 0F ? 1F : -1F;
				player.velocity.Y = -Player.jumpSpeed * direction;
				player.jump = Math.Max(1,
					(int)Math.Ceiling(Player.jumpHeight * 0.75F));
				player.releaseJump = false;
				player.fallStart = (int)(player.position.Y / 16F);
			}
			catch (Exception ex) {
				_lastGameThreadError =
					"Infinite extra jump: " + DescribeException(ex);
			}
		}

		private void TryAttachPostUpdateEquipsHook()
		{
			if (_postUpdateEquipsHook != null || !_gameHooksWanted)
				return;
			try {
				MethodInfo method = typeof(PlayerLoader).GetMethod(
					"PostUpdateEquips", BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.Static,
					null, new Type[] { typeof(Player) }, null);
				if (method == null || method.ReturnType != typeof(void))
					throw new MissingMethodException(
						typeof(PlayerLoader).FullName, "PostUpdateEquips(Player)");
				_postUpdateEquipsHook = new Hook(
					method, new PostUpdateEquipsDetour(
						PlayerLoaderPostUpdateEquipsHook));
			}
			catch (Exception ex) {
				_postUpdateEquipsHook = null;
				_postUpdateEquipsHookUnavailable = true;
				_lastGameThreadError =
					"最大仆从容量 Hook：" + DescribeException(ex);
			}
		}

		private void PlayerLoaderPostUpdateEquipsHook(
			PostUpdateEquipsOrig orig, Player player)
		{
			orig(player);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot != null && snapshot.AdjustableMaxMinions &&
					IsLocalPlayer(player))
					player.maxMinions = Math.Max(0, snapshot.MaxMinions);
			}
			catch (Exception ex) {
				_lastGameThreadError =
					"最大仆从容量：" + DescribeException(ex);
			}
		}

		private void TryAttachFreeUpPetsAndMinionsHook()
		{
			if (_freeUpPetsAndMinionsHook != null || !_gameHooksWanted)
				return;
			try {
				MethodInfo method = typeof(Player).GetMethod(
					"FreeUpPetsAndMinions",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance,
					null, new Type[] { typeof(Item) }, null);
				if (method == null || method.ReturnType != typeof(void))
					throw new MissingMethodException(
						typeof(Player).FullName,
						"FreeUpPetsAndMinions(Item)");
				_freeUpPetsAndMinionsHook = new Hook(
					method,
					new FreeUpPetsAndMinionsDetour(
						PlayerFreeUpPetsAndMinionsHook));
			}
			catch (Exception ex) {
				_freeUpPetsAndMinionsHook = null;
				_freeUpPetsAndMinionsHookUnavailable = true;
				_lastGameThreadError =
					"仆从召唤容量 Hook：" + DescribeException(ex);
			}
		}

		private void PlayerFreeUpPetsAndMinionsHook(
			FreeUpPetsAndMinionsOrig orig, Player player, Item item)
		{
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot != null && snapshot.AdjustableMaxMinions &&
					IsLocalPlayer(player))
					player.maxMinions = Math.Max(0, snapshot.MaxMinions);
			}
			catch (Exception ex) {
				_lastGameThreadError =
					"召唤前应用仆从容量：" + DescribeException(ex);
			}

			orig(player, item);

			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot != null && snapshot.AdjustableMaxMinions &&
					IsLocalPlayer(player))
					player.maxMinions = Math.Max(0, snapshot.MaxMinions);
			}
			catch {
			}
		}

		private void UpdateBuffCapacityOnGameThread(CheatSnapshot snapshot)
		{
			bool requested = snapshot != null && snapshot.AdjustableMaxBuffs;
			if (!requested || Main.netMode != 0 || Main.gameMenu) {
				if (_customBuffCapacityActive || _lastAppliedBuffCapacity >= 0)
					DeactivateBuffCapacityOnGameThread(clearExtraSlots: true);
				return;
			}
			if (_buffCapacityHookUnavailable)
				return;
			if (_playerMaxBuffsGetterHook == null)
				InstallBuffCapacityHookOnGameThread();
			if (_playerMaxBuffsGetterHook == null)
				return;

			int capacity = Math.Max(_baseBuffCapacity, snapshot.MaxBuffs);
			Player localPlayer = Main.LocalPlayer;
			bool storageReady = HasPlayerBuffStorage(localPlayer, capacity);
			if (!_customBuffCapacityActive ||
				_lastAppliedBuffCapacity != capacity || !storageReady) {
				EnsureAllPlayerBuffStorage(
					capacity, capacity, normalizeAndTrim: true);
				if (!HasPlayerBuffStorage(localPlayer, capacity))
					throw new InvalidOperationException(
						"无法扩展本地玩家 Buff 数组。");
				SetBuffLoaderCapacityField(capacity);
				_activeBuffCapacity = capacity;
				_lastAppliedBuffCapacity = capacity;
				_customBuffCapacityActive = true;
			}
		}

		private void InstallBuffCapacityHookOnGameThread()
		{
			try {
				_baseBuffCapacity = Math.Max(1, Player.MaxBuffs);
				_extraPlayerBuffCountField = typeof(BuffLoader).GetField(
					"extraPlayerBuffCount", BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.Static);
				if (_extraPlayerBuffCountField == null ||
					_extraPlayerBuffCountField.FieldType != typeof(int))
					throw new MissingFieldException(
						typeof(BuffLoader).FullName,
						"extraPlayerBuffCount");
				_baseExtraPlayerBuffCount = (int)
					_extraPlayerBuffCountField.GetValue(null);
				MethodInfo getter = typeof(Player).GetMethod(
					"get_maxBuffs", BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.Static,
					null, Type.EmptyTypes, null);
				if (getter == null || !getter.IsStatic ||
					getter.ReturnType != typeof(int))
					throw new MissingMethodException(
						typeof(Player).FullName, "get_maxBuffs()");
				_playerMaxBuffsGetterHook = new Hook(
					getter, new PlayerMaxBuffsGetterDetour(
						PlayerMaxBuffsGetterHook));
			}
			catch (Exception ex) {
				_playerMaxBuffsGetterHook = null;
				_buffCapacityHookUnavailable = true;
				_customBuffCapacityActive = false;
				_lastGameThreadError =
					"Buff 栏上限 Hook：" + DescribeException(ex);
			}
		}

		private int PlayerMaxBuffsGetterHook(PlayerMaxBuffsGetterOrig orig)
		{
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot == null || !snapshot.AdjustableMaxBuffs ||
					Main.gameMenu || Main.netMode != 0) {
					RestoreBuffLoaderCapacityField();
					return orig();
				}
				int original = orig();
				if (!_customBuffCapacityActive)
					return original;
				int capacity = Math.Max(original, _activeBuffCapacity);
				return HasPlayerBuffStorage(Main.LocalPlayer, capacity)
					? capacity
					: original;
			}
			catch {
				return orig();
			}
		}

		private void DeactivateBuffCapacityOnGameThread(bool clearExtraSlots)
		{
			int originalCapacity = Math.Max(1, _baseBuffCapacity);
			RestoreBuffLoaderCapacityField();
			_customBuffCapacityActive = false;
			_activeBuffCapacity = originalCapacity;
			_lastAppliedBuffCapacity = -1;
			if (clearExtraSlots)
				EnsureAllPlayerBuffStorage(
					originalCapacity, originalCapacity,
					normalizeAndTrim: true);
		}

		private void DisposeBuffCapacityHookOnGameThread()
		{
			try {
				DeactivateBuffCapacityOnGameThread(clearExtraSlots: true);
			}
			catch {
				_customBuffCapacityActive = false;
			}
			Hook hook = _playerMaxBuffsGetterHook;
			_playerMaxBuffsGetterHook = null;
			DisposeHookSilently(hook);
			_extraPlayerBuffCountField = null;
		}

		private void SetBuffLoaderCapacityField(int capacity)
		{
			if (_extraPlayerBuffCountField == null)
				throw new MissingFieldException(
					typeof(BuffLoader).FullName,
					"extraPlayerBuffCount");
			int extra = checked(
				_baseExtraPlayerBuffCount +
				(capacity - _baseBuffCapacity));
			_extraPlayerBuffCountField.SetValue(null, extra);
			_buffLoaderCapacityFieldModified = true;
		}

		private void RestoreBuffLoaderCapacityField()
		{
			if (!_buffLoaderCapacityFieldModified ||
				_extraPlayerBuffCountField == null)
				return;
			_extraPlayerBuffCountField.SetValue(
				null, _baseExtraPlayerBuffCount);
			_buffLoaderCapacityFieldModified = false;
		}

		private void RestoreBuffCapacityOutsideSinglePlayerFromUiThread()
		{
			if (!_buffLoaderCapacityFieldModified)
				return;
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot != null && snapshot.AdjustableMaxBuffs &&
					!Main.gameMenu && Main.netMode == 0)
					return;
				RestoreBuffLoaderCapacityField();
				_customBuffCapacityActive = false;
			}
			catch {
			}
		}

		private static bool HasPlayerBuffStorage(Player player, int capacity)
		{
			return player != null && player.buffType != null &&
				player.buffTime != null &&
				player.buffType.Length >= capacity &&
				player.buffTime.Length >= capacity;
		}

		private static void EnsureAllPlayerBuffStorage(
			int storageCapacity, int logicalCapacity, bool normalizeAndTrim)
		{
			if (Main.player != null) {
				for (int i = 0; i < Main.player.Length; i++)
					EnsurePlayerBuffStorage(
						Main.player[i], storageCapacity,
						logicalCapacity, normalizeAndTrim);
			}
			if (Main.playerVisualClone != null) {
				for (int i = 0; i < Main.playerVisualClone.Length; i++)
					EnsurePlayerBuffStorage(
						Main.playerVisualClone[i], storageCapacity,
						logicalCapacity, normalizeAndTrim);
			}
			EnsurePlayerBuffStorage(
				Main.PendingPlayer, storageCapacity,
				logicalCapacity, normalizeAndTrim);
			EnsurePlayerBuffStorage(
				Main.clientPlayer, storageCapacity,
				logicalCapacity, normalizeAndTrim);
			EnsurePlayerBuffStorage(
				Main.dresserInterfaceDummy, storageCapacity,
				logicalCapacity, normalizeAndTrim);
		}

		private static void EnsurePlayerBuffStorage(
			Player player, int storageCapacity, int logicalCapacity,
			bool normalizeAndTrim)
		{
			if (player == null)
				return;
			storageCapacity = Math.Max(1, storageCapacity);
			logicalCapacity = Math.Max(0,
				Math.Min(storageCapacity, logicalCapacity));
			int[] types = player.buffType ?? new int[storageCapacity];
			int[] times = player.buffTime ?? new int[storageCapacity];
			if (types.Length < storageCapacity)
				Array.Resize(ref types, storageCapacity);
			if (times.Length < storageCapacity)
				Array.Resize(ref times, storageCapacity);
			if (normalizeAndTrim) {
				int readLimit = Math.Min(types.Length, times.Length);
				int write = 0;
				for (int read = 0; read < readLimit &&
					write < logicalCapacity; read++) {
					if (types[read] <= 0 || times[read] <= 0)
						continue;
					if (write != read) {
						types[write] = types[read];
						times[write] = times[read];
					}
					write++;
				}
				for (int i = write; i < types.Length; i++)
					types[i] = 0;
				for (int i = write; i < times.Length; i++)
					times[i] = 0;
			}
			player.buffType = types;
			player.buffTime = times;
		}

		private void TryAttachUpdateBuffsRuntimeDetour()
		{
			if (_updateBuffsDetour != null || !_gameHooksWanted)
				return;
			try {
				MethodInfo updateBuffs = typeof(Player).GetMethod(
					"UpdateBuffs",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance,
					null,
					new Type[] { typeof(int) },
					null);
				if (updateBuffs == null)
					throw new MissingMethodException(
						typeof(Player).FullName, "UpdateBuffs(int)");
				_updateBuffsDetour = new Hook(
					updateBuffs,
					new PlayerUpdateBuffsDetour(
						PlayerUpdateBuffsRuntimeDetourHook));
			}
			catch (Exception ex) {
				_updateBuffsDetour = null;
				_updateBuffsHookUnavailable = true;
				_lastGameThreadError =
					"Shimmer operation Hook: " + DescribeException(ex);
			}
		}

		private void DetachPlayerHooksOnGameThread()
		{
			DetachLightingHooksOnGameThread();
			DisposeViewTransformHook();
			DisposeWorldBoundsHooksOnGameThread();
			DisposeUnlimitedReachHooksOnGameThread();
			DisposeBuffCapacityHookOnGameThread();
			if (_itemCheckCanUseHookAttached) {
				try {
					On_Player.ItemCheck_CheckCanUse -=
						PlayerItemCheckCanUseHook;
				}
				catch {
				}
				_itemCheckCanUseHookAttached = false;
			}
			if (_getRespawnTimeHookAttached) {
				try {
					On_Player.GetRespawnTime -= PlayerGetRespawnTimeHook;
				}
				catch {
				}
				_getRespawnTimeHookAttached = false;
			}
			if (_updateJumpHeightHookAttached) {
				try {
					On_Player.UpdateJumpHeight -= PlayerUpdateJumpHeightHook;
				}
				catch {
				}
				_updateJumpHeightHookAttached = false;
			}
			if (_resetEffectsHookAttached) {
				try {
					On_Player.ResetEffects -= PlayerResetEffectsHook;
				}
				catch {
				}
				_resetEffectsHookAttached = false;
			}
			Hook infiniteJumpHook = _infiniteJumpMovementHook;
			_infiniteJumpMovementHook = null;
			DisposeHookSilently(infiniteJumpHook);
			_infiniteJumpInputWasDown = false;
			Hook postUpdateEquipsHook = _postUpdateEquipsHook;
			_postUpdateEquipsHook = null;
			DisposeHookSilently(postUpdateEquipsHook);
			Hook freeUpPetsAndMinionsHook = _freeUpPetsAndMinionsHook;
			_freeUpPetsAndMinionsHook = null;
			DisposeHookSilently(freeUpPetsAndMinionsHook);
			Hook updateBuffsHook = _updateBuffsDetour;
			_updateBuffsDetour = null;
			if (updateBuffsHook != null) {
				try { updateBuffsHook.Dispose(); }
				catch { }
			}
			if (_playerUpdateHookAttached) {
				try {
					On_Player.Update -= PlayerUpdateHook;
				}
				catch {
				}
				_playerUpdateHookAttached = false;
			}
			Hook directPlayerHook = _playerUpdateDetour;
			_playerUpdateDetour = null;
			if (directPlayerHook != null) {
				try { directPlayerHook.Dispose(); }
				catch { }
			}
			_playerHookBackend = "已卸载";
		}

		private void AttachLightingHooksOnGameThread()
		{
			if (_lightingHooksAttached)
				return;
			try {
				On_Lighting.Brightness += LightingBrightnessHook;
				On_Lighting.GetSubLight += LightingGetSubLightHook;
				On_Lighting.GetColor_Point += LightingGetColorPointHook;
				On_Lighting.GetColor_Point_Color += LightingGetColorPointColorHook;
				On_Lighting.GetColor_int_int_Color += LightingGetColorIntIntColorHook;
				On_Lighting.GetColorClamped += LightingGetColorClampedHook;
				On_Lighting.GetColor_int_int += LightingGetColorIntIntHook;
				On_Lighting.GetColor9Slice_int_int_refColorArray +=
					LightingGetColor9SliceColorHook;
				On_Lighting.GetColor9Slice_int_int_refVector3Array +=
					LightingGetColor9SliceVectorHook;
				On_Lighting.GetCornerColors += LightingGetCornerColorsHook;
				On_Lighting.GetColor4Slice_int_int_refColorArray +=
					LightingGetColor4SliceColorHook;
				On_Lighting.GetColor4Slice_int_int_refVector3Array +=
					LightingGetColor4SliceVectorHook;
				_lightingHooksAttached = true;
			}
			catch (Exception ex) {
				// Unhook the successfully-added prefix if this Terraria build is
				// missing one of the optional lighting overloads.
				DetachLightingHooksOnGameThread();
				_lightingHooksUnavailable = true;
				_lastGameThreadError =
					"照明 Hook：" + DescribeException(ex);
			}
		}

		private void DetachLightingHooksOnGameThread()
		{
			try { On_Lighting.Brightness -= LightingBrightnessHook; } catch { }
			try { On_Lighting.GetSubLight -= LightingGetSubLightHook; } catch { }
			try { On_Lighting.GetColor_Point -= LightingGetColorPointHook; } catch { }
			try {
				On_Lighting.GetColor_Point_Color -= LightingGetColorPointColorHook;
			}
			catch { }
			try {
				On_Lighting.GetColor_int_int_Color -= LightingGetColorIntIntColorHook;
			}
			catch { }
			try { On_Lighting.GetColorClamped -= LightingGetColorClampedHook; } catch { }
			try {
				On_Lighting.GetColor_int_int -= LightingGetColorIntIntHook;
			}
			catch { }
			try {
				On_Lighting.GetColor9Slice_int_int_refColorArray -=
					LightingGetColor9SliceColorHook;
			}
			catch { }
			try {
				On_Lighting.GetColor9Slice_int_int_refVector3Array -=
					LightingGetColor9SliceVectorHook;
			}
			catch { }
			try { On_Lighting.GetCornerColors -= LightingGetCornerColorsHook; } catch { }
			try {
				On_Lighting.GetColor4Slice_int_int_refColorArray -=
					LightingGetColor4SliceColorHook;
			}
			catch { }
			try {
				On_Lighting.GetColor4Slice_int_int_refVector3Array -=
					LightingGetColor4SliceVectorHook;
			}
			catch { }
			_lightingHooksAttached = false;
		}

		private bool IsFullBrightEnabled()
		{
			CheatSnapshot snapshot = _cheatSnapshot;
			return snapshot != null && snapshot.FullBright;
		}

		private float LightingBrightnessHook(
			On_Lighting.orig_Brightness orig, int x, int y)
		{
			return IsFullBrightEnabled() ? 1F : orig(x, y);
		}

		private XnaVector3 LightingGetSubLightHook(
			On_Lighting.orig_GetSubLight orig, XnaVector2 position)
		{
			return IsFullBrightEnabled()
				? XnaVector3.One
				: orig(position);
		}

		private XnaColor LightingGetColorPointHook(
			On_Lighting.orig_GetColor_Point orig, XnaPoint point)
		{
			return IsFullBrightEnabled()
				? XnaColor.White
				: orig(point);
		}

		private XnaColor LightingGetColorPointColorHook(
			On_Lighting.orig_GetColor_Point_Color orig,
			XnaPoint point,
			XnaColor oldColor)
		{
			return IsFullBrightEnabled()
				? oldColor
				: orig(point, oldColor);
		}

		private XnaColor LightingGetColorIntIntColorHook(
			On_Lighting.orig_GetColor_int_int_Color orig,
			int x,
			int y,
			XnaColor oldColor)
		{
			return IsFullBrightEnabled()
				? oldColor
				: orig(x, y, oldColor);
		}

		private XnaColor LightingGetColorClampedHook(
			On_Lighting.orig_GetColorClamped orig,
			int x,
			int y,
			XnaColor oldColor)
		{
			return IsFullBrightEnabled()
				? oldColor
				: orig(x, y, oldColor);
		}

		private XnaColor LightingGetColorIntIntHook(
			On_Lighting.orig_GetColor_int_int orig, int x, int y)
		{
			return IsFullBrightEnabled()
				? XnaColor.White
				: orig(x, y);
		}

		private void LightingGetColor9SliceColorHook(
			On_Lighting.orig_GetColor9Slice_int_int_refColorArray orig,
			int x,
			int y,
			ref XnaColor[] slices)
		{
			if (!IsFullBrightEnabled()) {
				orig(x, y, ref slices);
				return;
			}
			if (slices == null || slices.Length < 9)
				slices = new XnaColor[9];
			for (int i = 0; i < slices.Length; i++)
				slices[i] = XnaColor.White;
		}

		private void LightingGetColor9SliceVectorHook(
			On_Lighting.orig_GetColor9Slice_int_int_refVector3Array orig,
			int x,
			int y,
			ref XnaVector3[] slices)
		{
			if (!IsFullBrightEnabled()) {
				orig(x, y, ref slices);
				return;
			}
			if (slices == null || slices.Length < 9)
				slices = new XnaVector3[9];
			for (int i = 0; i < slices.Length; i++)
				slices[i] = XnaVector3.One;
		}

		private void LightingGetCornerColorsHook(
			On_Lighting.orig_GetCornerColors orig,
			int centerX,
			int centerY,
			out VertexColors colors,
			float scale)
		{
			if (!IsFullBrightEnabled()) {
				orig(centerX, centerY, out colors, scale);
				return;
			}
			colors = new VertexColors(XnaColor.White);
		}

		private void LightingGetColor4SliceColorHook(
			On_Lighting.orig_GetColor4Slice_int_int_refColorArray orig,
			int x,
			int y,
			ref XnaColor[] slices)
		{
			if (!IsFullBrightEnabled()) {
				orig(x, y, ref slices);
				return;
			}
			if (slices == null || slices.Length < 4)
				slices = new XnaColor[4];
			for (int i = 0; i < slices.Length; i++)
				slices[i] = XnaColor.White;
		}

		private void LightingGetColor4SliceVectorHook(
			On_Lighting.orig_GetColor4Slice_int_int_refVector3Array orig,
			int x,
			int y,
			ref XnaVector3[] slices)
		{
			if (!IsFullBrightEnabled()) {
				orig(x, y, ref slices);
				return;
			}
			if (slices == null || slices.Length < 4)
				slices = new XnaVector3[4];
			for (int i = 0; i < slices.Length; i++)
				slices[i] = XnaVector3.One;
		}

		private void PlayerUpdateHook(
			On_Player.orig_Update orig, Player self, int i)
		{
			bool local;
			bool forceUseGate;
			CheatSnapshot snapshot;
			PreparePlayerUpdate(
				self, out local, out forceUseGate, out snapshot);

			// The original Terraria method must be called exactly once.
			try {
				orig(self, i);
			}
			finally {
				if (forceUseGate)
					_forceConcurrentUseGate = false;
			}

			FinishPlayerUpdate(self, local, snapshot);
		}

		private void PlayerUpdateRuntimeDetourHook(
			PlayerUpdateOrig orig, Player self, int i)
		{
			bool local;
			bool forceUseGate;
			CheatSnapshot snapshot;
			PreparePlayerUpdate(
				self, out local, out forceUseGate, out snapshot);
			try {
				orig(self, i);
			}
			finally {
				if (forceUseGate)
					_forceConcurrentUseGate = false;
			}
			FinishPlayerUpdate(self, local, snapshot);
		}

		private void PreparePlayerUpdate(
			Player self,
			out bool local,
			out bool forceUseGate,
			out CheatSnapshot snapshot)
		{
			local = false;
			forceUseGate = false;
			snapshot = _cheatSnapshot;
			try {
				local = IsLocalPlayer(self);
				if (!local)
					return;
				long now = DateTime.UtcNow.Ticks;
				Interlocked.Exchange(ref _lastGameHeartbeatTicks, now);
				Interlocked.Exchange(ref _lastLocalPlayerUpdateTicks, now);
				_gameWorldReady = true;
				_lastGameThreadError = string.Empty;
				_playerHookError = string.Empty;
				ApplyShimmerPhaseOperationUnlock(self, snapshot);
				DrainPendingGameActions();
				ApplySustainedCheats(self, snapshot);
				bool inputDown = snapshot.ConcurrentAttack &&
					self.controlUseItem;
				forceUseGate = inputDown &&
					!_concurrentAttackInputWasDown &&
					HasAttackStillRunning(self);
				_concurrentAttackInputWasDown = inputDown;
				if (forceUseGate) {
					PrepareConcurrentAttack(self);
					_forceConcurrentUseGate = true;
				}
			}
			catch (Exception ex) {
				_lastGameThreadError = DescribeException(ex);
			}
		}

		private void FinishPlayerUpdate(
			Player self, bool local, CheatSnapshot snapshot)
		{
			try {
				if (!local)
					return;
				GameThreadTick();
				ApplySustainedCheats(self, snapshot);
				if (snapshot.AdjustableStep)
					ApplyAdjustableStep(self, snapshot.StepBlocks);
				if (snapshot.UnrestrictedWorldBounds)
					ClampPlayerToRealWorld(self, snapshot.WorldEdgeSafetyTiles);
			}
			catch (Exception ex) {
				_lastGameThreadError = DescribeException(ex);
			}
		}

		private void PlayerUpdateBuffsRuntimeDetourHook(
			PlayerUpdateBuffsOrig orig, Player self, int i)
		{
			orig(self, i);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				ApplyShimmerPhaseOperationUnlock(self, snapshot);
				ApplyGravityAndFallSpeedFields(self, snapshot);
			}
			catch (Exception ex) {
				_lastGameThreadError =
					"UpdateBuffs trainer fields: " + DescribeException(ex);
			}
		}

		private static void ApplyGravityAndFallSpeedFields(
			Player player, CheatSnapshot snapshot)
		{
			if (player == null || snapshot == null || !IsLocalPlayer(player))
				return;
			if (snapshot.AdjustableGravity)
				player.gravity = Player.defaultGravity *
					Math.Max(0F, snapshot.GravityMultiplier);
			if (snapshot.UnlimitedFallSpeed)
				player.maxFallSpeed = float.MaxValue;
			else if (snapshot.AdjustableMaxFallSpeed)
				player.maxFallSpeed = Math.Max(0F, snapshot.MaxFallSpeed);
		}

		private static void ApplyShimmerPhaseOperationUnlock(
			Player player, CheatSnapshot snapshot)
		{
			if (snapshot == null || !snapshot.ShimmerPhaseCanOperate ||
				!IsLocalPlayer(player) || !player.shimmering)
				return;

			// Shimmer phase/wall-pass is keyed by player.shimmering.
			// The vanilla lockout comes from BuffID.Shimmer setting frozen=true;
			// clearing only frozen keeps the phase collision and transparency.
			player.frozen = false;
		}

		private void ApplyCalamityParryCooldownRemoval(
			Player player, CheatSnapshot snapshot)
		{
			if (snapshot == null || !snapshot.NoCalamityParryCooldown ||
				!IsLocalPlayer(player))
				return;

			try {
				RemoveCalamityParryCooldownEntry(player);
				ReleaseCalamityArkParryHoldouts(player);
			}
			catch (Exception ex) {
				ResetCalamityParryReflectionCache();
				_calamityParryApiRetryCountdown = 180;
				_lastGameThreadError =
					"Calamity Parry?" + DescribeException(ex);
			}
		}

		private void RemoveCalamityParryCooldownEntry(Player player)
		{
			if (!TryResolveCalamityParryApi())
				return;

			object calamityPlayer = _calamityGetModPlayer.Invoke(player, null);
			if (calamityPlayer == null)
				return;

			IDictionary cooldowns = _calamityCooldownsField.GetValue(
				calamityPlayer) as IDictionary;
			if (cooldowns == null || !cooldowns.Contains("ParryCooldown"))
				return;

			cooldowns.Remove("ParryCooldown");
			if (_calamitySyncCooldownRemoval != null) {
				IList<string> removedIds = new List<string>(1) {
					"ParryCooldown"
				};
				_calamitySyncCooldownRemoval.Invoke(calamityPlayer,
					new object[] { Main.dedServ, removedIds });
			}
		}

		private bool TryResolveCalamityParryApi()
		{
			if (_calamityGetModPlayer != null &&
				_calamityCooldownsField != null)
				return true;
			if (_calamityParryApiRetryCountdown > 0) {
				_calamityParryApiRetryCountdown--;
				return false;
			}

			Assembly calamityAssembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(delegate(Assembly assembly) {
					return string.Equals(assembly.GetName().Name, "CalamityMod",
						StringComparison.Ordinal);
				});
			if (calamityAssembly == null) {
				_calamityParryApiRetryCountdown = 180;
				return false;
			}

			Type calamityPlayerType = calamityAssembly.GetType(
				"CalamityMod.CalPlayer.CalamityPlayer", false);
			MethodInfo getModPlayerDefinition = typeof(Player).GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Instance).FirstOrDefault(delegate(MethodInfo method) {
					return string.Equals(method.Name, "GetModPlayer",
						StringComparison.Ordinal) &&
						method.IsGenericMethodDefinition &&
						method.GetGenericArguments().Length == 1 &&
						method.GetParameters().Length == 0;
				});
			if (calamityPlayerType == null || getModPlayerDefinition == null) {
				_calamityParryApiRetryCountdown = 180;
				return false;
			}

			MethodInfo getModPlayer = getModPlayerDefinition.MakeGenericMethod(
				calamityPlayerType);
			FieldInfo cooldownsField = calamityPlayerType.GetField("cooldowns",
				BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Instance);
			if (cooldownsField == null) {
				_calamityParryApiRetryCountdown = 180;
				return false;
			}

			MethodInfo syncRemoval = calamityPlayerType.GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Instance).FirstOrDefault(delegate(MethodInfo method) {
					if (!string.Equals(method.Name, "SyncCooldownRemoval",
						StringComparison.Ordinal))
						return false;
					ParameterInfo[] parameters = method.GetParameters();
					return parameters.Length == 2 &&
						parameters[0].ParameterType == typeof(bool) &&
						parameters[1].ParameterType.IsAssignableFrom(
							typeof(List<string>));
				});

			_calamityPlayerType = calamityPlayerType;
			_calamityGetModPlayer = getModPlayer;
			_calamityCooldownsField = cooldownsField;
			_calamitySyncCooldownRemoval = syncRemoval;
			return true;
		}

		private void ResetCalamityParryReflectionCache()
		{
			_calamityPlayerType = null;
			_calamityGetModPlayer = null;
			_calamityCooldownsField = null;
			_calamitySyncCooldownRemoval = null;
		}

		private static void ReleaseCalamityArkParryHoldouts(Player player)
		{
			const int parryWindowFrames = 15;
			const int arkParryLifetime = 340;
			for (int i = 0; i < Main.maxProjectiles; i++) {
				Projectile projectile = Main.projectile[i];
				if (projectile == null || !projectile.active ||
					projectile.owner != player.whoAmI ||
					!IsCalamityArkParryHoldout(projectile))
					continue;

				// The first 15 frames are the real parry window. The rest of
				// Calamity's 340-frame Ark holdout is only its recharge period.
				if (projectile.timeLeft <= arkParryLifetime - parryWindowFrames)
					projectile.active = false;
			}
		}

		private static bool IsCalamityArkParryHoldout(Projectile projectile)
		{
			ModProjectile modProjectile = projectile.ModProjectile;
			if (modProjectile == null || !string.Equals(
				modProjectile.GetType().Assembly.GetName().Name, "CalamityMod",
				StringComparison.Ordinal))
				return false;

			string typeName = modProjectile.GetType().Name;
			return string.Equals(typeName, "ArkoftheAncientsParryHoldout",
				StringComparison.Ordinal) ||
				string.Equals(typeName, "TrueArkoftheAncientsParryHoldout",
					StringComparison.Ordinal) ||
				string.Equals(typeName, "ArkoftheElementsParryHoldout",
					StringComparison.Ordinal) ||
				string.Equals(typeName, "ArkoftheCosmosParryHoldout",
					StringComparison.Ordinal);
		}

		private static void PrepareConcurrentAttack(Player player)
		{
			if (player == null || player.dead || !player.controlUseItem)
				return;
			Item held = player.HeldItem;
			if (held == null || held.IsAir ||
				held.damage <= 0 && held.shoot <= ProjectileID.None)
				return;

			// Terraria normally blocks a new ItemCheck until these counters reach
			// zero. Reset only the owner's cooldown gate; already-created
			// projectiles are separate entities and are intentionally untouched.
			player.itemAnimation = 0;
			player.itemTime = 0;
			player.reuseDelay = 0;
			player.releaseUseItem = true;
		}

		private static bool HasAttackStillRunning(Player player)
		{
			if (player.itemAnimation > 0 || player.itemTime > 0 ||
				player.reuseDelay > 0 || player.channel)
				return true;
			if (player.heldProj >= 0 && player.heldProj < Main.maxProjectiles) {
				Projectile projectile = Main.projectile[player.heldProj];
				if (projectile != null && projectile.active &&
					projectile.owner == player.whoAmI)
					return true;
			}
			Item held = player.HeldItem;
			return held != null && held.shoot > ProjectileID.None &&
				player.ownedProjectileCounts != null &&
				held.shoot < player.ownedProjectileCounts.Length &&
				player.ownedProjectileCounts[held.shoot] > 0;
		}

		private bool PlayerItemCheckCanUseHook(
			On_Player.orig_ItemCheck_CheckCanUse orig,
			Player self,
			Item item)
		{
			bool allowed = orig(self, item);
			if (allowed || !_forceConcurrentUseGate ||
				!_gameHooksWanted || !IsLocalPlayer(self))
				return allowed;
			return item != null && ReferenceEquals(item, self.HeldItem);
		}

		private void PlayerResetEffectsHook(
			On_Player.orig_ResetEffects orig, Player self)
		{
			// Always run Terraria first; trainer values are intentionally applied
			// after ResetEffects so they survive that frame's reset.
			orig(self);
			try {
				if (IsLocalPlayer(self)) {
					CheatSnapshot snapshot = _cheatSnapshot;
					ApplySustainedCheats(self, snapshot);
					ApplyResetSensitiveCheats(self, snapshot);
				}
			}
			catch {
			}
		}

		private void PlayerUpdateJumpHeightHook(
			On_Player.orig_UpdateJumpHeight orig, Player self)
		{
			orig(self);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (!IsLocalPlayer(self) || !snapshot.HighJump ||
					self.mount != null && self.mount.Active)
					return;

				// JumpMovement rewrites velocity.Y on every held-jump frame.
				// Scaling the values produced by UpdateJumpHeight keeps the
				// multiplier alive for the whole jump instead of only one frame.
				float factor = (float)Math.Sqrt(
					Math.Max(1F, snapshot.JumpMultiplier));
				Player.jumpSpeed *= factor;
				Player.jumpHeight = Math.Max(
					1, (int)Math.Ceiling(Player.jumpHeight * factor));
			}
			catch {
			}
		}

		private int PlayerGetRespawnTimeHook(
			On_Player.orig_GetRespawnTime orig, Player self, bool pvp)
		{
			int result = orig(self, pvp);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot.InstantRespawn && self != null &&
					self.whoAmI == Main.myPlayer)
					return 1;
			}
			catch {
			}
			return result;
		}

		private void GameThreadTick()
		{
			Interlocked.Exchange(
				ref _lastGameHeartbeatTicks,
				DateTime.UtcNow.Ticks);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				try {
					UpdateImproveGameSelectionHookOnGameThread(
						snapshot.ImproveGameUnlimitedSelection);
				}
				catch {
					DetachImproveGameSelectionHookOnGameThread();
				}
				UpdateBuffCapacityOnGameThread(snapshot);
				UpdateUnrestrictedViewOnGameThread(snapshot);
				UpdateWorldBoundsHooksOnGameThread(snapshot);
				UpdateUnlimitedReachHooksOnGameThread(snapshot);
				if (_gameHooksWanted) {
					lock (_hookAttachGate) {
						AttachPlayerHooksOnGameThread();
					}
				}
				Player player = Main.LocalPlayer;
				bool playerReady = !Main.gameMenu && IsLocalPlayer(player);
				if (!playerReady) {
					_gameWorldReady = false;
					_worldSessionActive = false;
					_concurrentAttackInputWasDown = false;
					_infiniteJumpInputWasDown = false;
					_forceConcurrentUseGate = false;
					return;
				}
				if (!_worldSessionActive) {
					// Hooks target Player methods, not a particular Player instance,
					// so they remain valid across save-and-reenter transitions.
					_concurrentAttackInputWasDown = false;
					_forceConcurrentUseGate = false;
					_worldSessionActive = true;
				}
				_gameWorldReady = true;
				_lastGameThreadError = string.Empty;
				DrainPendingGameActions();
				ProcessMapRevealOnGameThread();
				ApplySustainedCheats(player, snapshot);
				ApplyCalamityParryCooldownRemoval(player, snapshot);
				ApplyResetSensitiveCheats(player, snapshot);
				if (snapshot.InstantRespawn && player.dead &&
					player.respawnTimer > 1)
					player.respawnTimer = 1;
				if (snapshot.FastMovement)
					AssistHorizontalVelocity(
						player, Math.Max(1F, snapshot.MovementMultiplier));
				if (_unloadedItemRepairCountdown-- <= 0) {
					_unloadedItemRepairCountdown = 60;
					int repaired = RepairInvalidUnloadedItems(player);
					if (repaired > 0) {
						PostToUi(delegate {
							if (_statusLabel != null)
								_statusLabel.Text =
									"已清理 " + repaired +
									" 个不可保存的失效模组占位物品；现在可以正常保存。";
							RefreshInventory();
						});
					}
				}
			}
			catch (Exception ex) {
				_lastGameThreadError = DescribeException(ex);
				// A third-party tick callback must never escape into Terraria's
				// update loop.
			}
		}

		private void ProcessMapRevealOnGameThread()
		{
			if (_mapRevealCancelRequested) {
				_mapRevealRequested = false;
				_mapRevealActive = false;
				return;
			}
			uint updateCount = Main.GameUpdateCount;
			if (_lastMapRevealUpdateCount == updateCount)
				return;
			_lastMapRevealUpdateCount = updateCount;

			if (_mapRevealRequested && !_mapRevealActive) {
				if (Main.gameMenu || Main.Map == null ||
					Main.maxTilesX <= 2 || Main.maxTilesY <= 2)
					return;
				_mapRevealX = 1;
				_mapRevealY = 1;
				_mapRevealDone = 0;
				_mapRevealTotal =
					Math.Max(0, Main.maxTilesX - 2) * Math.Max(0, Main.maxTilesY - 2);
				_mapRevealRequested = false;
				_mapRevealActive = true;
			}
			if (!_mapRevealActive)
				return;
			if (Main.gameMenu || Main.Map == null) {
				_mapRevealActive = false;
				_mapRevealCancelRequested = true;
				return;
			}

			const int tilesPerTick = 18000;
			int processed = 0;
			int maxX = Main.maxTilesX - 1;
			int maxY = Main.maxTilesY - 1;
			while (processed < tilesPerTick && _mapRevealX < maxX) {
				Main.Map.Update(_mapRevealX, _mapRevealY, byte.MaxValue);
				processed++;
				_mapRevealDone++;
				_mapRevealY++;
				if (_mapRevealY >= maxY) {
					_mapRevealY = 1;
					_mapRevealX++;
				}
			}

			if (_mapRevealX < maxX)
				return;
			_mapRevealDone = _mapRevealTotal;
			_mapRevealActive = false;
			_mapRevealCancelRequested = false;
			Main.refreshMap = true;
			Main.resetMapFull = true;
		}

		private static bool IsLocalPlayer(Player player)
		{
			return player != null && player.active &&
				player.whoAmI == Main.myPlayer;
		}

		private void ApplySustainedCheats(
			Player player, CheatSnapshot snapshot)
		{
			if (snapshot.GodMode) {
				if (!_trainerGodModeApplied) {
					_godModeBeforeTrainer = player.creativeGodMode;
					_trainerGodModeApplied = true;
				}
				player.creativeGodMode = true;
				player.immune = true;
				player.immuneNoBlink = true;
				player.immuneTime = Math.Max(player.immuneTime, 60);
				player.statLife = player.statLifeMax2;
				player.statMana = player.statManaMax2;
				player.breath = player.breathMax;
			}
			else {
				if (_trainerGodModeApplied) {
					player.creativeGodMode = _godModeBeforeTrainer;
					_trainerGodModeApplied = false;
				}
			}

			if (snapshot.InfiniteLife)
				player.statLife = player.statLifeMax2;
			if (snapshot.InfiniteMana)
				player.statMana = player.statManaMax2;
			if (snapshot.InfiniteBreath)
				player.breath = player.breathMax;
			if (snapshot.NoPotionCooldown)
				player.potionDelay = 0;
			if (snapshot.InfiniteFlight) {
				player.wingTime = Math.Max(player.wingTime, player.wingTimeMax);
				player.rocketTime = Math.Max(
					player.rocketTime, player.rocketTimeMax);
			}
			if (snapshot.ClearDebuffs && player.buffType != null &&
				Main.debuff != null) {
				for (int slot = player.buffType.Length - 1; slot >= 0; slot--) {
					int type = player.buffType[slot];
					if (type > 0 && type < Main.debuff.Length &&
						Main.debuff[type])
						player.DelBuff(slot);
				}
			}
		}

		private static void ApplyResetSensitiveCheats(
			Player player, CheatSnapshot snapshot)
		{
			if (snapshot.NoKnockback)
				player.noKnockback = true;
			if (snapshot.NoFallDamage)
				player.noFallDmg = true;
			if (snapshot.LavaImmune)
				player.lavaImmune = true;
			if (snapshot.FastMovement)
				ApplyFastMovementFields(
					player, Math.Max(1F, snapshot.MovementMultiplier));
			if (snapshot.AdjustableMaxMinions)
				player.maxMinions = Math.Max(0, snapshot.MaxMinions);
		}

		private void RestoreTrainerOwnedStateOnGameThread()
		{
			try {
				Player player = Main.LocalPlayer;
				if (player != null && player.active && _trainerGodModeApplied)
					player.creativeGodMode = _godModeBeforeTrainer;
			}
			catch {
			}
			_trainerGodModeApplied = false;
			RestoreViewZoomStateOnGameThread();
		}

		private bool IsUnlimitedReachActive(Player player)
		{
			CheatSnapshot snapshot = _cheatSnapshot;
			return snapshot != null && snapshot.UnlimitedPlacementInteractionRange &&
				IsLocalPlayer(player);
		}

		private void UpdateUnlimitedReachHooksOnGameThread(CheatSnapshot snapshot)
		{
			if (_unlimitedReachHookUnavailable || snapshot == null ||
				!snapshot.UnlimitedPlacementInteractionRange ||
				(_interactionRangeHook != null && _tileInteractionRangeHook != null &&
				 _multiTileInteractionRangeHook != null &&
				 _tileTypeInteractionRangeHook != null &&
				 _itemTargetRangeHook != null && _limitReachPointHook != null &&
				 _placeThingReachHook != null && _itemCheckReachHook != null &&
				 _chestRangeReachHook != null))
				return;
			try {
				BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance;
				MethodInfo interaction = typeof(Player).GetMethod("InInteractionRange", flags,
					null, new Type[] { typeof(int), typeof(int), typeof(TileReachCheckSettings) }, null);
				MethodInfo tileInteraction = typeof(Player).GetMethod("IsInTileInteractionRange", flags,
					null, new Type[] { typeof(int), typeof(int), typeof(TileReachCheckSettings) }, null);
				MethodInfo multiTile = typeof(Player).GetMethod("IsInInteractionRangeToMultiTileHitbox", flags,
					null, new Type[] { typeof(int), typeof(int) }, null);
				MethodInfo tileType = typeof(Player).GetMethod("IsTileTypeInInteractionRange", flags,
					null, new Type[] { typeof(int), typeof(TileReachCheckSettings) }, null);
				MethodInfo itemRange = typeof(Player).GetMethod("IsTargetTileInItemRange", flags,
					null, new Type[] { typeof(Item) }, null);
				MethodInfo limitPoint = typeof(Player).GetMethod("LimitPointToPlayerReachableArea", flags,
					null, new Type[] { typeof(XnaVector2).MakeByRefType() }, null);
				MethodInfo placeThing = typeof(Player).GetMethod("PlaceThing", flags,
					null, new Type[] { typeof(Player.ItemCheckContext).MakeByRefType() }, null);
				MethodInfo itemCheck = typeof(Player).GetMethod("ItemCheck", flags,
					null, Type.EmptyTypes, null);
				MethodInfo chestRange = typeof(Player).GetMethod(
					"HandleBeingInChestRange", flags, null, Type.EmptyTypes, null);
				if (interaction == null || tileInteraction == null || multiTile == null ||
					tileType == null || itemRange == null || limitPoint == null ||
					placeThing == null || itemCheck == null || chestRange == null)
					throw new MissingMethodException("Unlimited reach hook target missing");
				_interactionRangeHook = new Hook(interaction,
					new TileRangeCheckDetour(PlayerTileRangeCheckHook));
				_tileInteractionRangeHook = new Hook(tileInteraction,
					new TileRangeCheckDetour(PlayerTileRangeCheckHook));
				_multiTileInteractionRangeHook = new Hook(multiTile,
					new MultiTileRangeCheckDetour(PlayerMultiTileRangeCheckHook));
				_tileTypeInteractionRangeHook = new Hook(tileType,
					new TileTypeRangeCheckDetour(PlayerTileTypeRangeCheckHook));
				_itemTargetRangeHook = new Hook(itemRange,
					new ItemRangeCheckDetour(PlayerItemRangeCheckHook));
				_limitReachPointHook = new Hook(limitPoint,
					new LimitReachPointDetour(PlayerLimitReachPointHook));
				_placeThingReachHook = new Hook(placeThing,
					new PlaceThingDetour(PlayerPlaceThingReachHook));
				_itemCheckReachHook = new Hook(itemCheck,
					new ItemCheckReachDetour(PlayerItemCheckReachHook));
				_chestRangeReachHook = new Hook(chestRange,
					new ChestRangeDetour(PlayerChestRangeReachHook));
			}
			catch (Exception ex) {
				DisposeUnlimitedReachHooksOnGameThread();
				_unlimitedReachHookUnavailable = true;
				_lastGameThreadError = "Unlimited reach hooks: " + DescribeException(ex);
			}
		}

		private void DisposeUnlimitedReachHooksOnGameThread()
		{
			DisposeHookSilently(_chestRangeReachHook); _chestRangeReachHook = null;
			DisposeHookSilently(_itemCheckReachHook); _itemCheckReachHook = null;
			DisposeHookSilently(_placeThingReachHook); _placeThingReachHook = null;
			DisposeHookSilently(_limitReachPointHook); _limitReachPointHook = null;
			DisposeHookSilently(_itemTargetRangeHook); _itemTargetRangeHook = null;
			DisposeHookSilently(_tileTypeInteractionRangeHook); _tileTypeInteractionRangeHook = null;
			DisposeHookSilently(_multiTileInteractionRangeHook); _multiTileInteractionRangeHook = null;
			DisposeHookSilently(_tileInteractionRangeHook); _tileInteractionRangeHook = null;
			DisposeHookSilently(_interactionRangeHook); _interactionRangeHook = null;
		}

		private bool PlayerTileRangeCheckHook(TileRangeCheckOrig orig, Player self,
			int tileX, int tileY, TileReachCheckSettings settings)
		{
			return IsUnlimitedReachActive(self) || orig(self, tileX, tileY, settings);
		}

		private bool PlayerMultiTileRangeCheckHook(MultiTileRangeCheckOrig orig,
			Player self, int tileX, int tileY)
		{
			return IsUnlimitedReachActive(self) || orig(self, tileX, tileY);
		}

		private bool PlayerTileTypeRangeCheckHook(TileTypeRangeCheckOrig orig,
			Player self, int tileType, TileReachCheckSettings settings)
		{
			return IsUnlimitedReachActive(self) || orig(self, tileType, settings);
		}

		private bool PlayerItemRangeCheckHook(ItemRangeCheckOrig orig,
			Player self, Item item)
		{
			return IsUnlimitedReachActive(self) || orig(self, item);
		}

		private void PlayerLimitReachPointHook(LimitReachPointOrig orig,
			Player self, ref XnaVector2 point)
		{
			if (!IsUnlimitedReachActive(self))
				orig(self, ref point);
		}

		private void PlayerPlaceThingReachHook(PlaceThingOrig orig, Player self,
			ref Player.ItemCheckContext context)
		{
			if (!IsUnlimitedReachActive(self)) {
				orig(self, ref context);
				return;
			}
			int oldRangeX = Player.tileRangeX;
			int oldRangeY = Player.tileRangeY;
			int oldBlockRange = self.blockRange;
			try {
				Player.tileRangeX = Math.Max(oldRangeX, Main.maxTilesX);
				Player.tileRangeY = Math.Max(oldRangeY, Main.maxTilesY);
				self.blockRange = 0;
				orig(self, ref context);
			}
			finally {
				Player.tileRangeX = oldRangeX;
				Player.tileRangeY = oldRangeY;
				self.blockRange = oldBlockRange;
			}
		}

		private void PlayerItemCheckReachHook(ItemCheckReachOrig orig, Player self)
		{
			if (!IsUnlimitedReachActive(self)) {
				orig(self);
				return;
			}
			int oldRangeX = Player.tileRangeX;
			int oldRangeY = Player.tileRangeY;
			int oldBlockRange = self.blockRange;
			try {
				// ItemCheck 的水桶、接线工具、割草机、释放小动物、
				// 人偶穿戴等分支会直接读取这些字段，不经过通用距离判断。
				Player.tileRangeX = Math.Max(oldRangeX, Main.maxTilesX);
				Player.tileRangeY = Math.Max(oldRangeY, Main.maxTilesY);
				self.blockRange = 0;
				orig(self);
			}
			finally {
				Player.tileRangeX = oldRangeX;
				Player.tileRangeY = oldRangeY;
				self.blockRange = oldBlockRange;
			}
		}

		private void PlayerChestRangeReachHook(ChestRangeOrig orig, Player self)
		{
			if (!IsUnlimitedReachActive(self)) {
				orig(self);
				return;
			}
			int oldRangeX = Player.tileRangeX;
			int oldRangeY = Player.tileRangeY;
			try {
				// 箱子距离检查独立于 ItemCheck；扩大后远程打开的箱子不会
				// 在下一次 Player.Update 中被原版距离检查立刻关闭。
				Player.tileRangeX = Math.Max(oldRangeX, Main.maxTilesX);
				Player.tileRangeY = Math.Max(oldRangeY, Main.maxTilesY);
				orig(self);
			}
			finally {
				Player.tileRangeX = oldRangeX;
				Player.tileRangeY = oldRangeY;
			}
		}

		private void UpdateWorldBoundsHooksOnGameThread(CheatSnapshot snapshot)
		{
			if (_worldBoundsHookUnavailable ||
				(_worldBorderHook != null && _cameraWorldClampHook != null))
				return;
			Hook border = null;
			Hook camera = null;
			try {
				MethodInfo bordersMovement = typeof(Player).GetMethod(
					"BordersMovement", BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance, null, Type.EmptyTypes, null);
				MethodInfo cameraClamp = typeof(Main).GetMethod(
					"ClampScreenPositionToWorld", BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.Static,
					null, Type.EmptyTypes, null);
				if (bordersMovement == null || cameraClamp == null || !cameraClamp.IsStatic)
					throw new MissingMethodException("World edge hook target missing");
				border = new Hook(bordersMovement, new BordersMovementDetour(PlayerBordersMovementHook));
				camera = new Hook(cameraClamp, new ClampScreenPositionDetour(MainClampScreenPositionToWorldHook));
				_worldBorderHook = border;
				_cameraWorldClampHook = camera;
			}
			catch (Exception ex) {
				DisposeHookSilently(camera);
				DisposeHookSilently(border);
				_worldBoundsHookUnavailable = true;
				_lastGameThreadError = "World edge hooks: " + DescribeException(ex);
			}
		}

		private void DisposeWorldBoundsHooksOnGameThread()
		{
			Hook camera = _cameraWorldClampHook; _cameraWorldClampHook = null;
			Hook border = _worldBorderHook; _worldBorderHook = null;
			DisposeHookSilently(camera);
			DisposeHookSilently(border);
		}

		private void PlayerBordersMovementHook(BordersMovementOrig orig, Player player)
		{
			Interlocked.Increment(ref _worldBorderHookHits);
			CheatSnapshot snapshot = _cheatSnapshot;
			if (snapshot == null || !snapshot.UnrestrictedWorldBounds || !IsLocalPlayer(player)) {
				orig(player);
				return;
			}
			ClampPlayerToRealWorld(player, snapshot.WorldEdgeSafetyTiles);
		}

		private static void ClampPlayerToRealWorld(Player player, int requestedSafetyTiles)
		{
			if (player == null) return;
			int safetyTiles = Math.Max(10, Math.Min(40, requestedSafetyTiles));
			float padding = safetyTiles * 16F;
			float minX = Main.leftWorld + padding;
			float maxX = Main.rightWorld - padding - player.width;
			float minY = Main.topWorld + padding;
			float maxY = Main.bottomWorld - padding - player.height;
			if (maxX < minX || maxY < minY) return;
			XnaVector2 position = player.position;
			bool changed = false;
			if (position.X < minX) { position.X = minX; if (player.velocity.X < 0F) player.velocity.X = 0F; changed = true; }
			else if (position.X > maxX) { position.X = maxX; if (player.velocity.X > 0F) player.velocity.X = 0F; changed = true; }
			if (position.Y < minY) { position.Y = minY; if (player.velocity.Y < 0F) player.velocity.Y = 0F; changed = true; }
			else if (position.Y > maxY) { position.Y = maxY; if (player.velocity.Y > 0F) player.velocity.Y = 0F; changed = true; }
			if (changed) { player.position = position; player.oldPosition = position; player.fallStart = (int)(position.Y / 16F); }
		}

		private void MainClampScreenPositionToWorldHook(ClampScreenPositionOrig orig)
		{
			CheatSnapshot snapshot = _cheatSnapshot;
			if (snapshot == null || !snapshot.UnrestrictedWorldBounds || Main.gameMenu || !IsLocalPlayer(Main.LocalPlayer)) { orig(); return; }
			Interlocked.Increment(ref _cameraClampHookHits);
			if (float.IsNaN(Main.screenPosition.X) || float.IsInfinity(Main.screenPosition.X) || float.IsNaN(Main.screenPosition.Y) || float.IsInfinity(Main.screenPosition.Y)) {
				Player player = Main.LocalPlayer;
				Main.screenPosition = player.Center - new XnaVector2(Main.screenWidth * .5F, Main.screenHeight * .5F);
			}
		}

		private void UpdateUnrestrictedViewOnGameThread(CheatSnapshot snapshot)
		{
			if (snapshot == null || !snapshot.UnrestrictedView) {
				RestoreViewZoomStateOnGameThread();
				ProcessViewTargetRebuildOnGameThread();
				return;
			}
			if (!_trainerViewZoomApplied) {
				_gameZoomTargetBeforeTrainer = Main.GameZoomTarget;
				_unrestrictedGameZoomTarget = Math.Max(
					MinimumUnrestrictedZoom,
					Math.Min(MaximumUnrestrictedZoom, Main.GameZoomTarget));
				InstallViewHooksOnGameThread();
				if (_viewTransformHook == null || _viewInputHook == null)
					return;
				_lastViewZoomUpdateCount = uint.MaxValue;
				_trainerViewZoomApplied = true;
				Main.GameZoomTarget = _unrestrictedGameZoomTarget;
				_viewRenderTargetTier = GetViewRenderTargetTier(
					_unrestrictedGameZoomTarget);
				_viewTargetsNeedRebuild =
					_viewInitTargetsHook != null &&
					!_viewRenderExpansionUnavailable &&
					_viewRenderTargetTier < 0.999F;
				_viewTargetRebuildDelay = 0;
			}
			if (Main.gameMenu) {
				if (_viewInitTargetsHook != null &&
					!_viewRenderExpansionUnavailable &&
					_viewRenderTargetTier < 0.999F)
					_viewTargetsNeedRebuild = true;
				return;
			}

			uint updateCount = Main.GameUpdateCount;
			if (_lastViewZoomUpdateCount == updateCount)
				return;
			_lastViewZoomUpdateCount = updateCount;
			Main.GameZoomTarget = _unrestrictedGameZoomTarget;

			float targetTier = GetViewRenderTargetTier(
				_unrestrictedGameZoomTarget);
			if (Math.Abs(targetTier - _viewRenderTargetTier) > 0.001F) {
				bool needsLargerTarget = targetTier < _viewRenderTargetTier;
				_viewRenderTargetTier = targetTier;
				_viewTargetsNeedRebuild =
					_viewInitTargetsHook != null &&
					!_viewRenderExpansionUnavailable;
				_viewTargetRebuildDelay = needsLargerTarget ? 0 : 12;
			}
			ProcessViewTargetRebuildOnGameThread();
		}

		private void InstallViewHooksOnGameThread()
		{
			if (_viewTransformHookUnavailable)
				return;

			if (_viewTransformHook == null || _viewInputHook == null) {
				Hook transformHook = null;
				Hook inputHook = null;
				try {
					MethodInfo modifyTransform = typeof(SystemLoader).GetMethod(
						"ModifyTransformMatrix",
						BindingFlags.Public | BindingFlags.NonPublic |
						BindingFlags.Static,
						null,
						new Type[] { typeof(SpriteViewMatrix).MakeByRefType() },
						null);
					if (modifyTransform == null)
						throw new MissingMethodException(
							typeof(SystemLoader).FullName,
							"ModifyTransformMatrix(ref SpriteViewMatrix)");

					MethodInfo updateZoomKeys = typeof(Main).GetMethod(
						"UpdateViewZoomKeys",
						BindingFlags.Public | BindingFlags.NonPublic |
						BindingFlags.Instance,
						null, Type.EmptyTypes, null);
					if (updateZoomKeys == null)
						throw new MissingMethodException(
							typeof(Main).FullName, "UpdateViewZoomKeys()");

					transformHook = new Hook(
						modifyTransform,
						new ModifyTransformMatrixDetour(
							SystemModifyTransformMatrixHook));
					inputHook = new Hook(
						updateZoomKeys,
						new UpdateViewZoomKeysDetour(
							MainUpdateViewZoomKeysHook));
					_viewTransformHook = transformHook;
					_viewInputHook = inputHook;
				}
				catch (Exception ex) {
					DisposeHookSilently(inputHook);
					DisposeHookSilently(transformHook);
					_viewTransformHook = null;
					_viewInputHook = null;
					_viewTransformHookUnavailable = true;
					_lastGameThreadError =
						"扩展视野缩放 Hook：" + DescribeException(ex);
					return;
				}
			}

			if ((_viewInitTargetsHook != null && _viewOverdrawHook != null) ||
				_viewRenderExpansionUnavailable)
				return;

			ILHook initTargetsHook = null;
			Hook overdrawHook = null;
			try {
				MethodInfo initTargets = typeof(Main).GetMethod(
					"InitTargets",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance,
					null,
					new Type[] { typeof(int), typeof(int) },
					null);
				MethodInfo getOverdraw = typeof(Main).GetMethod(
					"GetScreenOverdrawOffset",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Static,
					null, Type.EmptyTypes, null);
				_viewInitTargetsNoArgs = typeof(Main).GetMethod(
					"InitTargets",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance,
					null, Type.EmptyTypes, null);
				_viewRenderTargetMaxSizeField = typeof(Main).GetField(
					"_renderTargetMaxSize",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Static);
				_viewResizeBusyField = typeof(Main).GetField(
					"_isResizingAndRemakingTargets",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Static);
				if (initTargets == null || getOverdraw == null ||
					_viewInitTargetsNoArgs == null ||
					_viewRenderTargetMaxSizeField == null ||
					_viewResizeBusyField == null)
					throw new MissingMemberException(
						typeof(Main).FullName,
						"render target expansion members");

				_viewVanillaRenderTargetMaxSize =
					(int)_viewRenderTargetMaxSizeField.GetValue(null);
				initTargetsHook = new ILHook(
					initTargets,
					new ILContext.Manipulator(PatchViewInitTargets));
				overdrawHook = new Hook(
					getOverdraw,
					new GetScreenOverdrawOffsetDetour(
						MainGetScreenOverdrawOffsetHook));
				_viewInitTargetsHook = initTargetsHook;
				_viewOverdrawHook = overdrawHook;
			}
			catch (Exception ex) {
				DisposeHookSilently(overdrawHook);
				DisposeILHookSilently(initTargetsHook);
				_viewInitTargetsHook = null;
				_viewOverdrawHook = null;
				_viewRenderExpansionUnavailable = true;
				_lastGameThreadError =
					"扩展视野绘制 Hook：" + DescribeException(ex);
			}
		}

		private void MainUpdateViewZoomKeysHook(
			UpdateViewZoomKeysOrig orig, Main self)
		{
			if (!IsUnrestrictedViewActive()) {
				orig(self);
				return;
			}
			if (Main.inFancyUI)
				return;
			try {
				bool zoomIn = PlayerInput.Triggers.Current.ViewZoomIn;
				bool zoomOut = PlayerInput.Triggers.Current.ViewZoomOut;
				if (zoomIn != zoomOut) {
					const float zoomStepRatio = 1.02F;
					_unrestrictedGameZoomTarget = zoomIn
						? _unrestrictedGameZoomTarget * zoomStepRatio
						: _unrestrictedGameZoomTarget / zoomStepRatio;
					_unrestrictedGameZoomTarget = Math.Max(
						MinimumUnrestrictedZoom,
						Math.Min(
							MaximumUnrestrictedZoom,
							_unrestrictedGameZoomTarget));
				}
				Main.GameZoomTarget = _unrestrictedGameZoomTarget;
			}
			catch (Exception ex) {
				_lastGameThreadError =
					"扩展视野输入：" + DescribeException(ex);
			}
		}

		private void PatchViewInitTargets(ILContext il)
		{
			ILCursor cursor = new ILCursor(il);
			if (!cursor.TryGotoNext(
				MoveType.After,
				instruction => instruction.MatchStsfld<Main>(
					"offScreenRange")))
				throw new InvalidOperationException(
					"InitTargets offScreenRange anchor not found");
			cursor.Emit(OpCodes.Ldarg_1);
			cursor.Emit(OpCodes.Ldarg_2);
			cursor.EmitDelegate<Action<int, int>>(
				ApplyViewInitTargetsPatch);
		}

		private void ApplyViewInitTargetsPatch(int width, int height)
		{
			try {
				int vanillaPadding = Math.Max(0, Main.offScreenRange);
				_viewVanillaRenderTargetPadding = vanillaPadding;
				if (!IsUnrestrictedViewActive() || Main.gameMenu ||
					_viewRenderExpansionUnavailable ||
					_viewRenderTargetTier >= 0.999F ||
					width <= 0 || height <= 0) {
					_viewRenderTargetMaxSizeField.SetValue(
						null, _viewVanillaRenderTargetMaxSize);
					return;
				}

				int expandedPadding = ComputeExpandedRenderTargetPadding(
					width, height, _viewRenderTargetTier, vanillaPadding);
				int requiredMaximum = Math.Max(
					width + expandedPadding * 2,
					height + expandedPadding * 2);
				_viewRenderTargetMaxSizeField.SetValue(
					null,
					Math.Max(
						_viewVanillaRenderTargetMaxSize,
						requiredMaximum));
				Main.offScreenRange = expandedPadding;
			}
			catch (Exception ex) {
				_viewRenderExpansionUnavailable = true;
				try {
					_viewRenderTargetMaxSizeField.SetValue(
						null, _viewVanillaRenderTargetMaxSize);
				}
				catch {
				}
				_lastGameThreadError =
					"扩展视野目标准备：" + DescribeException(ex);
			}
		}

		private XnaPoint MainGetScreenOverdrawOffsetHook(
			GetScreenOverdrawOffsetOrig orig)
		{
			return IsUnrestrictedViewActive() &&
				!_viewRenderExpansionUnavailable &&
				_unrestrictedGameZoomTarget < 0.999F
				? new XnaPoint(0, 0)
				: orig();
		}

		private void SystemModifyTransformMatrixHook(
			ModifyTransformMatrixOrig orig,
			ref SpriteViewMatrix transform)
		{
			orig(ref transform);
			try {
				CheatSnapshot snapshot = _cheatSnapshot;
				if (snapshot != null && snapshot.UnrestrictedView &&
					_trainerViewZoomApplied)
					transform.Zoom = new XnaVector2(
						Math.Max(
							MinimumUnrestrictedZoom,
							_unrestrictedGameZoomTarget));
			}
			catch {
			}
		}

		private bool IsUnrestrictedViewActive()
		{
			CheatSnapshot snapshot = _cheatSnapshot;
			return snapshot != null && snapshot.UnrestrictedView &&
				_trainerViewZoomApplied;
		}

		private static float GetViewRenderTargetTier(float zoom)
		{
			if (zoom >= 0.875F)
				return 1F;
			if (zoom >= 0.625F)
				return 0.75F;
			if (zoom >= 0.45F)
				return 0.50F;
			if (zoom >= 0.35F)
				return 0.40F;
			return MinimumUnrestrictedZoom;
		}

		private static int ComputeExpandedRenderTargetPadding(
			int width, int height, float zoomTier, int vanillaPadding)
		{
			float boundedTier = Math.Max(
				MinimumUnrestrictedZoom, Math.Min(1F, zoomTier));
			double extraScale = 1.0 / boundedTier - 1.0;
			int desiredExtra = (int)Math.Ceiling(
				Math.Max(width, height) * extraScale * 0.5);
			int maximumPadding = Math.Min(
				(MaximumExpandedRenderTargetDimension - width) / 2,
				(MaximumExpandedRenderTargetDimension - height) / 2);
			maximumPadding = Math.Max(vanillaPadding, maximumPadding);
			return Math.Max(
				vanillaPadding,
				Math.Min(vanillaPadding + desiredExtra, maximumPadding));
		}

		private void ProcessViewTargetRebuildOnGameThread()
		{
			if (_viewVanillaRecoveryPending) {
				if (Main.dedServ || Main.instance == null || Main.gameMenu ||
					_viewTargetRebuildInProgress)
					return;
				RebuildVanillaViewTargetsOnGameThread();
				return;
			}
			if (!_viewTargetsNeedRebuild || _viewTargetRebuildInProgress)
				return;
			if (_viewInitTargetsHook == null ||
				_viewRenderExpansionUnavailable ||
				_viewInitTargetsNoArgs == null ||
				_viewResizeBusyField == null) {
				_viewTargetsNeedRebuild = false;
				return;
			}
			if (_viewTargetRebuildDelay > 0) {
				_viewTargetRebuildDelay--;
				return;
			}
			if (Main.dedServ || Main.instance == null || Main.gameMenu)
				return;

			bool wasBusy = false;
			try {
				wasBusy = (bool)_viewResizeBusyField.GetValue(null);
				if (wasBusy)
					return;
				_viewTargetRebuildInProgress = true;
				_viewResizeBusyField.SetValue(null, true);
				_viewInitTargetsNoArgs.Invoke(Main.instance, null);
				_viewTargetsNeedRebuild =
					!ViewRenderTargetsMatchCurrentTier();
				if (_viewTargetsNeedRebuild) {
					_viewTargetRebuildFailures++;
					_viewTargetRebuildDelay = 120;
					if (_viewTargetRebuildFailures >= 2)
						DisableViewRenderExpansionAfterFailure();
				}
				else {
					_viewTargetRebuildFailures = 0;
				}
			}
			catch (Exception ex) {
				_viewTargetRebuildFailures++;
				if (_viewTargetRebuildFailures >= 2)
					DisableViewRenderExpansionAfterFailure();
				else
					_viewTargetRebuildDelay = 120;
				_lastGameThreadError =
					"扩展视野目标重建：" + DescribeException(ex);
			}
			finally {
				try {
					if (!wasBusy && _viewResizeBusyField != null)
						_viewResizeBusyField.SetValue(null, false);
				}
				catch {
				}
				_viewTargetRebuildInProgress = false;
			}
		}

		private void DisableViewRenderExpansionAfterFailure()
		{
			_viewRenderExpansionUnavailable = true;
			_viewTargetsNeedRebuild = false;
			_viewVanillaRecoveryPending = true;
		}

		private void RebuildVanillaViewTargetsOnGameThread()
		{
			bool wasBusy = false;
			try {
				wasBusy = (bool)_viewResizeBusyField.GetValue(null);
				if (wasBusy)
					return;
				_viewTargetRebuildInProgress = true;
				_viewResizeBusyField.SetValue(null, true);
				_viewInitTargetsNoArgs.Invoke(Main.instance, null);
				_viewVanillaRecoveryPending = false;
			}
			catch {
				_viewVanillaRecoveryPending = false;
			}
			finally {
				try {
					if (!wasBusy && _viewResizeBusyField != null)
						_viewResizeBusyField.SetValue(null, false);
				}
				catch {
				}
				_viewTargetRebuildInProgress = false;
			}
		}

		private bool ViewRenderTargetsMatchCurrentTier()
		{
			try {
				if (Main.instance == null || Main.instance.tileTarget == null)
					return false;
				if (_viewRenderTargetTier >= 0.999F)
					return true;
				int padding = ComputeExpandedRenderTargetPadding(
					Main.screenWidth,
					Main.screenHeight,
					_viewRenderTargetTier,
					Math.Max(0, _viewVanillaRenderTargetPadding));
				return Main.instance.tileTarget.Width >=
					Main.screenWidth + padding * 2 &&
					Main.instance.tileTarget.Height >=
					Main.screenHeight + padding * 2;
			}
			catch {
				return false;
			}
		}

		private void DisposeViewTransformHook()
		{
			Hook transformHook = _viewTransformHook;
			Hook inputHook = _viewInputHook;
			ILHook initTargetsHook = _viewInitTargetsHook;
			Hook overdrawHook = _viewOverdrawHook;
			_viewTransformHook = null;
			_viewInputHook = null;
			_viewInitTargetsHook = null;
			_viewOverdrawHook = null;
			DisposeHookSilently(overdrawHook);
			DisposeILHookSilently(initTargetsHook);
			DisposeHookSilently(inputHook);
			DisposeHookSilently(transformHook);
		}

		private static void DisposeHookSilently(Hook hook)
		{
			if (hook == null)
				return;
			try { hook.Dispose(); }
			catch { }
		}

		private static void DisposeILHookSilently(ILHook hook)
		{
			if (hook == null)
				return;
			try { hook.Dispose(); }
			catch { }
		}

		private void RestoreViewZoomStateOnGameThread()
		{
			if (_trainerViewZoomApplied) {
				try {
					Main.GameZoomTarget = _gameZoomTargetBeforeTrainer;
				}
				catch {
				}
				_trainerViewZoomApplied = false;
				_viewRenderTargetTier = 1F;
				_viewTargetsNeedRebuild = true;
				_viewTargetRebuildDelay = 0;
			}
			_lastViewZoomUpdateCount = uint.MaxValue;
			ProcessViewTargetRebuildOnGameThread();
		}

		private delegate XnaPoint ImproveGameModifySizeOrig(
			XnaPoint start, XnaPoint end, int width, int height);

		private delegate XnaPoint ImproveGameModifySizeDetour(
			ImproveGameModifySizeOrig orig,
			XnaPoint start, XnaPoint end, int width, int height);

		private XnaPoint ImproveGameModifySizeHook(
			ImproveGameModifySizeOrig orig,
			XnaPoint start, XnaPoint end, int width, int height)
		{
			CheatSnapshot snapshot = _cheatSnapshot;
			if (snapshot == null || !snapshot.ImproveGameUnlimitedSelection)
				return orig(start, end, width, height);

			int maxX = Math.Max(1, Main.maxTilesX - 2);
			int maxY = Math.Max(1, Main.maxTilesY - 2);
			return new XnaPoint(
				Math.Max(1, Math.Min(maxX, end.X)),
				Math.Max(1, Math.Min(maxY, end.Y)));
		}

		private void UpdateImproveGameSelectionHookOnGameThread(bool enabled)
		{
			if (!enabled) {
				DetachImproveGameSelectionHookOnGameThread();
				return;
			}

			Assembly improveGame = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(delegate(Assembly candidate) {
					return string.Equals(
						candidate.GetName().Name,
						"ImproveGame",
						StringComparison.OrdinalIgnoreCase);
				});
			if (improveGame == null)
				return;
			if (_improveGameSelectionHook != null &&
				ReferenceEquals(improveGame, _improveGameHookAssembly))
				return;

			DetachImproveGameSelectionHookOnGameThread();
			Type utils = improveGame.GetType("ImproveGame.Helpers.MyUtils", false);
			if (utils == null)
				return;
			MethodInfo modifySize = utils.GetMethod(
				"ModifySize",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				null,
				new Type[] {
					typeof(XnaPoint), typeof(XnaPoint), typeof(int), typeof(int)
				},
				null);
			if (modifySize == null)
				return;

			_improveGameSelectionHook = new Hook(
				modifySize,
				new ImproveGameModifySizeDetour(ImproveGameModifySizeHook));
			_improveGameHookAssembly = improveGame;
		}

		private void DetachImproveGameSelectionHookOnGameThread()
		{
			Hook hook = _improveGameSelectionHook;
			_improveGameSelectionHook = null;
			_improveGameHookAssembly = null;
			if (hook == null)
				return;
			try { hook.Dispose(); }
			catch { }
		}

		private static void ApplyFastMovementFields(
			Player player, float multiplier)
		{
			float targetVelocity = 6F * multiplier;
			player.moveSpeed = Math.Max(player.moveSpeed, multiplier);
			player.maxRunSpeed = Math.Max(player.maxRunSpeed, targetVelocity);
			player.accRunSpeed = Math.Max(player.accRunSpeed, targetVelocity);
			player.runAcceleration = Math.Max(
				player.runAcceleration, 0.65F * multiplier);
		}

		private static void AssistHorizontalVelocity(
			Player player, float multiplier)
		{
			float targetVelocity = 6F * multiplier;
			if (player.mount != null && player.mount.Active ||
				player.controlLeft == player.controlRight)
				return;

			// Preserve a dash, mount boost, or a faster value supplied by gear.
			if (Math.Abs(player.velocity.X) >= targetVelocity)
				return;

			player.velocity.X = player.controlRight
				? targetVelocity
				: -targetVelocity;
		}

		private static void ApplyAdjustableStep(Player player, int maxBlocks)
		{
			if (player == null || player.dead ||
				player.mount != null && player.mount.Active ||
				player.controlLeft == player.controlRight)
				return;

			int direction = player.controlRight ? 1 : -1;
			float gravityDirection = player.gravDir >= 0F ? 1F : -1F;
			int blocks = Math.Max(1, Math.Min(10, maxBlocks));

			// Only climb while supported. This prevents Step from becoming
			// wall-climbing while the player is airborne.
			XnaVector2 supportProbe = player.position +
				new XnaVector2(0F, gravityDirection * 2F);
			if (!Collision.SolidCollision(
				supportProbe, player.width, player.height))
				return;

			XnaVector2 blockedProbe = player.position +
				new XnaVector2(direction * 3F, 0F);
			if (!Collision.SolidCollision(
				blockedProbe, player.width, player.height))
				return;

			int maximumLift = blocks * 16;
			for (int lift = 1; lift <= maximumLift; lift++) {
				XnaVector2 candidate = player.position +
					new XnaVector2(
						direction * 3F,
						-gravityDirection * lift);
				if (Collision.SolidCollision(
					candidate, player.width, player.height))
					continue;

				XnaVector2 candidateSupport = candidate +
					new XnaVector2(0F, gravityDirection * 2F);
				if (!Collision.SolidCollision(
					candidateSupport, player.width, player.height))
					continue;

				float horizontalSpeed = Math.Max(
					2F, Math.Abs(player.velocity.X));
				player.position = candidate;
				player.velocity.X = direction * horizontalSpeed;
				player.velocity.Y = 0F;
				player.fallStart = (int)(player.position.Y / 16F);
				return;
			}
		}

		private static bool IsUnloadedItemType(int type)
		{
			if (type <= 0)
				return false;
			try {
				Item sample;
				return ContentSamples.ItemsByType.TryGetValue(type, out sample) &&
					sample != null && sample.ModItem != null &&
					string.Equals(
						sample.ModItem.GetType().FullName,
						"Terraria.ModLoader.Default.UnloadedItem",
						StringComparison.Ordinal);
			}
			catch {
				return false;
			}
		}

		private static bool IsBrokenUnloadedItem(Item item)
		{
			if (item == null || item.IsAir || item.ModItem == null ||
				!string.Equals(
					item.ModItem.GetType().FullName,
					"Terraria.ModLoader.Default.UnloadedItem",
					StringComparison.Ordinal))
				return false;
			try {
				FieldInfo dataField = item.ModItem.GetType().GetField(
					"data",
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance);
				return dataField != null &&
					dataField.GetValue(item.ModItem) == null;
			}
			catch {
				return false;
			}
		}

		private static int RepairInvalidUnloadedItemArray(Item[] items)
		{
			if (items == null)
				return 0;
			int repaired = 0;
			foreach (Item item in items) {
				if (!IsBrokenUnloadedItem(item))
					continue;
				item.TurnToAir();
				repaired++;
			}
			return repaired;
		}

		private static int RepairInvalidUnloadedItems(Player player)
		{
			if (player == null)
				return 0;
			int repaired = 0;
			repaired += RepairInvalidUnloadedItemArray(player.inventory);
			repaired += RepairInvalidUnloadedItemArray(player.armor);
			repaired += RepairInvalidUnloadedItemArray(player.dye);
			repaired += RepairInvalidUnloadedItemArray(player.miscEquips);
			repaired += RepairInvalidUnloadedItemArray(player.miscDyes);
			if (player.bank != null)
				repaired += RepairInvalidUnloadedItemArray(player.bank.item);
			if (player.bank2 != null)
				repaired += RepairInvalidUnloadedItemArray(player.bank2.item);
			if (player.bank3 != null)
				repaired += RepairInvalidUnloadedItemArray(player.bank3.item);
			if (player.bank4 != null)
				repaired += RepairInvalidUnloadedItemArray(player.bank4.item);
			if (IsBrokenUnloadedItem(player.trashItem)) {
				player.trashItem.TurnToAir();
				repaired++;
			}
			if (IsBrokenUnloadedItem(Main.mouseItem)) {
				Main.mouseItem.TurnToAir();
				repaired++;
			}
			return repaired;
		}

		private void TrackHeldItems()
		{
			Player[] players = Main.player;
			if (players == null)
				return;

			for (int i = 0; i < players.Length; i++) {
				try {
					Player player = players[i];
					if (player == null || !player.active || i == Main.myPlayer)
						continue;

					Item held = player.HeldItem;
					int type = held == null ? 0 : held.type;
					int old;
					_lastHeld.TryGetValue(i, out old);
					if (old == type)
						continue;

					_lastHeld[i] = type;
					if (type <= 0 || held == null || held.IsAir)
						continue;

					ItemEntry info;
					_itemsById.TryGetValue(type, out info);
					HistoryEntry entry = new HistoryEntry();
					entry.Time = DateTime.Now.ToString("HH:mm:ss");
					entry.Player = string.IsNullOrWhiteSpace(player.name) ? "玩家 " + i : player.name;
					entry.Id = type;
					entry.Name = info == null
						? "物品 ID " + type
						: info.ChineseName;
					entry.InternalName = info == null ? "Item_" + type : info.InternalName;
					entry.Stack = held.stack;
					_history.Insert(0, entry);
					while (_history.Count > 2000)
						_history.RemoveAt(_history.Count - 1);
					if (_historySearch != null && _historySearch.TextLength > 0)
						ApplyHistoryFilter();
				}
				catch {
					// A broken mod item's display data must not stop the UI poller.
				}
			}
		}

		private void LoadCatalog()
		{
			for (int type = 1; type < ItemLoader.ItemCount; type++) {
				Item sample;
				if (!ContentSamples.ItemsByType.TryGetValue(type, out sample) || sample == null || sample.IsAir)
					continue;
				// UnloadedItem is an internal placeholder. Creating it with
				// QuickSpawnItem/SetDefaults leaves its saved TagCompound null,
				// which crashes PlayerIO.SaveInventory.
				if (IsUnloadedItemType(type))
					continue;
				string persistent;
				if (!ContentSamples.ItemPersistentIdsByNetIds.TryGetValue(type, out persistent))
					persistent = "Item_" + type;
				ItemEntry item = new ItemEntry {
					Id = type,
					InternalName = persistent,
					ChineseName = Lang.GetItemNameValue(type),
					EnglishName = persistent
				};
				_allItems.Add(item);
				_itemsById[type] = item;
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
				_itemResultLabel.Text = string.Format(
					CultureInfo.InvariantCulture,
					"结果 {0} / {1}",
					_filteredItems.Count,
					_allItems.Count);
			if (_filteredItems.Count == 0) {
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
			if (_inventoryGrid != null) {
				_selectedSlot = _inventoryGrid.SelectedSlot;
				if (_itemEditorGrid != null)
					_itemEditorGrid.SelectedSlot = _selectedSlot;
				UpdateItemEditorSelectionText();
			}
		}

		private void GiveSelectedItem()
		{
			if (_selectedItem == null)
				return;
			ItemEntry selected = _selectedItem;
			int amount = Decimal.ToInt32(_stackBox.Value);
			try {
				if (IsUnloadedItemType(selected.Id))
					throw new InvalidOperationException(
						"失效模组占位物品不能生成，否则会导致人物保存失败。");
				QueueGameThreadAction(delegate {
					string error = null;
					try {
						Player player = Main.LocalPlayer;
						if (player == null || !player.active)
							throw new InvalidOperationException("本地玩家尚未进入世界。");
						player.QuickSpawnItem(
							player.GetSource_Misc("TerrariaTmlToolkit"),
							selected.Id,
							amount);
					}
					catch (Exception ex) {
						error = ex.Message;
					}
					PostToUi(delegate {
						if (_statusLabel != null)
							_statusLabel.Text = error == null
								? "已获取：" + selected.ChineseName + " × " + amount
								: "获取失败：" + error;
						RefreshInventory();
					});
				});
			}
			catch (Exception ex) {
				_statusLabel.Text = "获取失败：" + ex.Message;
			}
		}

		private void ReplaceSelectedSlot()
		{
			if (_selectedItem == null || _selectedSlot < 0)
				return;
			ItemEntry selected = _selectedItem;
			int slot = _selectedSlot;
			int amount = Decimal.ToInt32(_stackBox.Value);
			try {
				if (IsUnloadedItemType(selected.Id))
					throw new InvalidOperationException(
						"失效模组占位物品不能写入槽位，否则会导致人物保存失败。");
				QueueGameThreadAction(delegate {
					string error = null;
					try {
						Player player = Main.LocalPlayer;
						if (player == null || !player.active ||
							player.inventory == null || slot >= player.inventory.Length)
							throw new InvalidOperationException("玩家或槽位当前不可用。");
						Item item = player.inventory[slot];
						item.SetDefaults(selected.Id);
						item.stack = Math.Min(amount, Math.Max(1, item.maxStack));
						SyncSlot(player, slot);
					}
					catch (Exception ex) {
						error = ex.Message;
					}
					PostToUi(delegate {
						if (_statusLabel != null)
							_statusLabel.Text = error == null
								? "已替换槽位 " + slot
								: "替换失败：" + error;
						RefreshInventory();
					});
				});
			}
			catch (Exception ex) {
				_statusLabel.Text = "替换失败：" + ex.Message;
			}
		}

		private void ClearSelectedSlot()
		{
			if (_selectedSlot < 0)
				return;
			int slot = _selectedSlot;
			try {
				QueueGameThreadAction(delegate {
					string error = null;
					try {
						Player player = Main.LocalPlayer;
						if (player == null || !player.active ||
							player.inventory == null || slot >= player.inventory.Length)
							throw new InvalidOperationException("玩家或槽位当前不可用。");
						player.inventory[slot].TurnToAir();
						SyncSlot(player, slot);
					}
					catch (Exception ex) {
						error = ex.Message;
					}
					PostToUi(delegate {
						if (_statusLabel != null)
							_statusLabel.Text = error == null
								? "已清空槽位 " + slot
								: "清空失败：" + error;
						RefreshInventory();
					});
				});
			}
			catch (Exception ex) {
				_statusLabel.Text = "清空失败：" + ex.Message;
			}
		}

		private static void SyncSlot(Player player, int slot)
		{
			if (Main.netMode == 1)
				NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot, player.inventory[slot].prefix);
		}

		private void RefreshInventory()
		{
			try {
				Player player = Main.LocalPlayer;
				if (player == null || player.inventory == null)
					return;
				int keepSlot = _selectedSlot;
				_inventory.RaiseListChangedEvents = false;
				_inventory.Clear();
				for (int i = 0; i < player.inventory.Length; i++) {
					Item item = player.inventory[i];
					_inventory.Add(new InventoryEntry {
						Slot = i,
						Id = item == null ? 0 : item.type,
						Name = item == null || item.IsAir ? "(空)" : item.Name,
						Stack = item == null ? 0 : item.stack,
						Prefix = item == null ? 0 : item.prefix
					});
				}
				_inventory.RaiseListChangedEvents = true;
				_inventory.ResetBindings();
				if (_inventoryGrid != null) {
					_inventoryGrid.SetItems(_inventory);
					_inventoryGrid.SelectedSlot = keepSlot;
				}
				if (_itemEditorGrid != null) {
					_itemEditorGrid.SetItems(_inventory);
					_itemEditorGrid.SelectedSlot = keepSlot;
					UpdateItemEditorSelectionText();
				}
			}
			catch {
			}
		}

		private void RemotePlayerSelectionChanged(object sender, EventArgs e)
		{
			if (_refreshingRemotePlayers || _remotePlayerBox == null)
				return;
			RemotePlayerEntry selected = _remotePlayerBox.SelectedItem as RemotePlayerEntry;
			int nextPlayer = selected == null ? -1 : selected.Index;
			if (nextPlayer != _selectedRemotePlayer && _remoteInventoryGrid != null)
				_remoteInventoryGrid.SelectedSlot = -1;
			_selectedRemotePlayer = nextPlayer;
			_selectedRemoteItem = null;
			RefreshRemoteInventory();
			UpdateRemoteSelectionDetails(true);
		}

		private void RemoteInventorySelectionChanged(object sender, EventArgs e)
		{
			UpdateRemoteSelectionDetails(true);
		}

		private void UpdateRemoteSelectionDetails(bool resetAmount)
		{
			if (_remoteInventoryGrid == null || _remoteSelectionLabel == null)
				return;

			int slot = _remoteInventoryGrid.SelectedSlot;
			Player[] players = Main.player;
			if (slot < 0) {
				_selectedRemoteItem = null;
				_remoteSelectionLabel.Text = "尚未选择槽位";
				SetRemoteActionsEnabled(false);
				return;
			}
			if (players == null || _selectedRemotePlayer < 0 ||
				_selectedRemotePlayer >= players.Length ||
				players[_selectedRemotePlayer] == null ||
				!players[_selectedRemotePlayer].active) {
				_selectedRemoteItem = null;
				_remoteSelectionLabel.Text = "所选玩家当前不在线";
				SetRemoteActionsEnabled(false);
				return;
			}

			Player remotePlayer = players[_selectedRemotePlayer];
			Item item = remotePlayer.inventory != null && slot < remotePlayer.inventory.Length
				? remotePlayer.inventory[slot]
				: null;
			int id = item == null ? 0 : item.type;
			int stack = item == null || item.IsAir ? 0 : item.stack;
			string itemName = item == null || item.IsAir ? "(空)" : item.Name;
			string playerName = string.IsNullOrWhiteSpace(remotePlayer.name)
				? "玩家 " + _selectedRemotePlayer
				: remotePlayer.name;

			_remoteSelectionLabel.Text = string.Format(
				CultureInfo.InvariantCulture,
				"玩家：{0}  |  槽位：{1}  |  ID：{2}  |  名称：{3}  |  堆叠：{4}",
				playerName, slot, id, itemName, stack);

			if (id <= 0 || stack <= 0) {
				_selectedRemoteItem = null;
				SetRemoteActionsEnabled(false);
				return;
			}

			_selectedRemoteItem = new InventoryEntry {
				Slot = slot,
				Id = id,
				Name = itemName,
				Stack = stack,
				Prefix = item == null ? 0 : item.prefix
			};
			SetRemoteActionsEnabled(true);
			if (resetAmount && _remoteStackBox != null) {
				decimal amount = Math.Max(1, stack);
				amount = Math.Min(_remoteStackBox.Maximum, amount);
				_remoteStackBox.Value = amount;
			}
		}

		private void SetRemoteActionsEnabled(bool enabled)
		{
			if (_remoteGiveButton != null)
				_remoteGiveButton.Enabled = enabled;
			if (_remoteFindButton != null)
				_remoteFindButton.Enabled = enabled;
			if (_remoteCopyIdButton != null)
				_remoteCopyIdButton.Enabled = enabled;
			if (_remoteStackBox != null)
				_remoteStackBox.Enabled = enabled;
		}

		private void GiveSelectedRemoteItem()
		{
			InventoryEntry selected = _selectedRemoteItem;
			if (selected == null || selected.Id <= 0 || _remoteStackBox == null)
				return;
			int amount = Decimal.ToInt32(_remoteStackBox.Value);
			try {
				if (IsUnloadedItemType(selected.Id))
					throw new InvalidOperationException(
						"对方物品是失效模组占位物品，不能复制到自己背包。");
				QueueGameThreadAction(delegate {
					string error = null;
					try {
						Player localPlayer = Main.LocalPlayer;
						if (localPlayer == null || !localPlayer.active)
							throw new InvalidOperationException("本地玩家尚未进入世界。");
						localPlayer.QuickSpawnItem(
							localPlayer.GetSource_Misc(
								"TerrariaTmlToolkit.RemoteInventory"),
							selected.Id,
							amount);
					}
					catch (Exception ex) {
						error = ex.Message;
					}
					PostToUi(delegate {
						if (_statusLabel != null)
							_statusLabel.Text = error == null
								? "已给予自己：[" + selected.Id + "] " +
									selected.Name + " × " + amount
								: "给予自己失败：" + error;
						RefreshInventory();
					});
				});
			}
			catch (Exception ex) {
				_statusLabel.Text = "给予自己失败：" + ex.Message;
			}
		}

		private void FindSelectedRemoteItemInCatalog()
		{
			InventoryEntry selected = _selectedRemoteItem;
			if (selected == null || selected.Id <= 0 ||
				_tabs == null || _itemsPage == null || _searchBox == null || _itemGrid == null)
				return;

			string idText = selected.Id.ToString(CultureInfo.InvariantCulture);
			_tabs.SelectedTab = _itemsPage;
			_searchBox.Text = idText;
			ApplyItemFilter();

			DataGridViewRow matchingRow = null;
			foreach (DataGridViewRow row in _itemGrid.Rows) {
				ItemEntry entry = row.DataBoundItem as ItemEntry;
				if (entry != null && entry.Id == selected.Id) {
					matchingRow = row;
					break;
				}
			}
			if (matchingRow == null) {
				_statusLabel.Text = "全物品列表中未找到 ID " + idText;
				return;
			}

			_itemGrid.ClearSelection();
			matchingRow.Selected = true;
			if (matchingRow.Cells.Count > 0)
				_itemGrid.CurrentCell = matchingRow.Cells[0];
			try {
				_itemGrid.FirstDisplayedScrollingRowIndex = matchingRow.Index;
			}
			catch {
			}
			_statusLabel.Text = "已在全物品页定位：[" + selected.Id + "] " + selected.Name;
		}

		private void CopySelectedRemoteItemId()
		{
			InventoryEntry selected = _selectedRemoteItem;
			if (selected == null || selected.Id <= 0)
				return;
			try {
				Clipboard.SetText(selected.Id.ToString(CultureInfo.InvariantCulture));
				_statusLabel.Text = "已复制物品 ID：" + selected.Id;
			}
			catch (Exception ex) {
				_statusLabel.Text = "复制 ID 失败：" + ex.Message;
			}
		}

		private void RefreshRemoteInventory()
		{
			if (_remotePlayerBox == null || _remoteInventoryGrid == null ||
				_remoteInventoryStatus == null)
				return;

			try {
				Player[] players = Main.player;
				List<RemotePlayerEntry> activePlayers = new List<RemotePlayerEntry>();
				if (players != null) {
					for (int i = 0; i < players.Length; i++) {
						Player player = players[i];
						if (i == Main.myPlayer || player == null || !player.active)
							continue;
						activePlayers.Add(new RemotePlayerEntry {
							Index = i,
							Name = string.IsNullOrWhiteSpace(player.name) ? "玩家 " + i : player.name
						});
					}
				}

				int keepPlayer = _selectedRemotePlayer;
				if (!activePlayers.Any(delegate(RemotePlayerEntry entry) {
					return entry.Index == keepPlayer;
				})) {
					keepPlayer = activePlayers.Count == 0 ? -1 : activePlayers[0].Index;
				}

				bool playerListChanged = _remotePlayerBox.Items.Count != activePlayers.Count;
				if (!playerListChanged) {
					for (int i = 0; i < activePlayers.Count; i++) {
						RemotePlayerEntry oldEntry = _remotePlayerBox.Items[i] as RemotePlayerEntry;
						RemotePlayerEntry newEntry = activePlayers[i];
						if (oldEntry == null || oldEntry.Index != newEntry.Index ||
							!string.Equals(oldEntry.Name, newEntry.Name, StringComparison.Ordinal)) {
							playerListChanged = true;
							break;
						}
					}
				}

				if (playerListChanged) {
					_refreshingRemotePlayers = true;
					_remotePlayerBox.BeginUpdate();
					try {
						_remotePlayerBox.Items.Clear();
						foreach (RemotePlayerEntry entry in activePlayers)
							_remotePlayerBox.Items.Add(entry);
						_remotePlayerBox.SelectedIndex = FindRemotePlayerComboIndex(keepPlayer);
					}
					finally {
						_remotePlayerBox.EndUpdate();
						_refreshingRemotePlayers = false;
					}
				}
				else if (FindRemotePlayerComboIndex(keepPlayer) != _remotePlayerBox.SelectedIndex) {
					_refreshingRemotePlayers = true;
					try {
						_remotePlayerBox.SelectedIndex = FindRemotePlayerComboIndex(keepPlayer);
					}
					finally {
						_refreshingRemotePlayers = false;
					}
				}

				_selectedRemotePlayer = keepPlayer;
				if (keepPlayer < 0 || players == null || keepPlayer >= players.Length ||
					players[keepPlayer] == null || !players[keepPlayer].active) {
					_remoteInventoryGrid.SetItems(Array.Empty<InventoryEntry>());
					_remoteInventoryGrid.SelectedSlot = -1;
					_remoteInventoryStatus.Text = "未发现其他在线玩家";
					UpdateRemoteSelectionDetails(false);
					return;
				}

				Player selectedPlayer = players[keepPlayer];
				List<InventoryEntry> entries = new List<InventoryEntry>(59);
				for (int slot = 0; slot <= 58; slot++) {
					Item item = selectedPlayer.inventory != null && slot < selectedPlayer.inventory.Length
						? selectedPlayer.inventory[slot]
						: null;
					entries.Add(new InventoryEntry {
						Slot = slot,
						Id = item == null ? 0 : item.type,
						Name = item == null || item.IsAir ? "(空)" : item.Name,
						Stack = item == null ? 0 : item.stack,
						Prefix = item == null ? 0 : item.prefix
					});
				}
				_remoteInventoryGrid.SetItems(entries);
				_remoteInventoryStatus.Text = "正在查看：" +
					(string.IsNullOrWhiteSpace(selectedPlayer.name)
						? "玩家 " + keepPlayer
						: selectedPlayer.name) +
					"（只读）";
				UpdateRemoteSelectionDetails(false);
			}
			catch (Exception ex) {
				_remoteInventoryStatus.Text = "暂时无法读取其他玩家背包：" + ex.GetType().Name;
				_selectedRemoteItem = null;
				SetRemoteActionsEnabled(false);
			}
		}

		private int FindRemotePlayerComboIndex(int playerIndex)
		{
			if (_remotePlayerBox == null || playerIndex < 0)
				return -1;
			for (int i = 0; i < _remotePlayerBox.Items.Count; i++) {
				RemotePlayerEntry entry = _remotePlayerBox.Items[i] as RemotePlayerEntry;
				if (entry != null && entry.Index == playerIndex)
					return i;
			}
			return -1;
		}

		private Bitmap CreateItemIcon(int itemType)
		{
			if (itemType <= 0)
				return null;

			Task<Bitmap> task;
			TaskCompletionSource<Bitmap> completion = null;
			lock (_iconTaskLock) {
				if (!_iconTasks.TryGetValue(itemType, out task)) {
					DateTime retryAfter;
					if (_iconRetryAfter.TryGetValue(itemType, out retryAfter) &&
						retryAfter > DateTime.UtcNow)
						return null;
					_iconRetryAfter.Remove(itemType);

					completion = new TaskCompletionSource<Bitmap>(
						TaskCreationOptions.RunContinuationsAsynchronously);
					task = completion.Task;
					_iconTasks[itemType] = task;
				}
			}

			if (completion != null) {
				try {
					// Texture2D.GetData touches FNA's graphics resources and
					// must not run on this separate WinForms thread.
					TaskCompletionSource<Bitmap> pendingCompletion = completion;
					QueueGameThreadAction(delegate {
						Bitmap bitmap = null;
						try {
							bitmap = ReadItemIconOnGameThread(itemType);
						}
						catch {
						}
						FinishItemIconLoad(itemType, pendingCompletion, bitmap);
					});
				}
				catch {
					FinishItemIconLoad(itemType, completion, null);
				}
			}

			if (!task.IsCompletedSuccessfully)
				return null;
			try {
				Bitmap source = task.Result;
				return source == null ? null : (Bitmap)source.Clone();
			}
			catch {
				return null;
			}
		}

		private void FinishItemIconLoad(
			int itemType,
			TaskCompletionSource<Bitmap> completion,
			Bitmap bitmap)
		{
			completion.TrySetResult(bitmap);
			bool retry = false;
			lock (_iconTaskLock) {
				Task<Bitmap> current;
				if (bitmap == null &&
					_iconTasks.TryGetValue(itemType, out current) &&
					ReferenceEquals(current, completion.Task)) {
					_iconTasks.Remove(itemType);
					_iconRetryAfter[itemType] = DateTime.UtcNow.AddMilliseconds(750);
					retry = true;
				}
				else if (bitmap != null) {
					_iconRetryAfter.Remove(itemType);
				}
			}
			RequestInventoryGridRepaint();
			if (retry) {
				Task.Delay(900).ContinueWith(delegate {
					RequestInventoryGridRepaint();
				});
			}
		}

		private static Bitmap ReadItemIconOnGameThread(int itemType)
		{
			if (itemType <= 0 || TextureAssets.Item == null || itemType >= TextureAssets.Item.Length)
				return null;

			try {
				Asset<Texture2D> asset = TextureAssets.Item[itemType];
				if (asset == null)
					return null;
				if (!asset.IsLoaded) {
					if (asset.State == AssetState.NotLoaded) {
						if (itemType < ItemID.Count)
							Main.Assets.Request<Texture2D>(asset.Name, AssetRequestMode.AsyncLoad);
						else
							ModContent.Request<Texture2D>(asset.Name, AssetRequestMode.AsyncLoad);
					}
					return null;
				}
				Texture2D texture = asset.Value;
				if (texture == null || texture.IsDisposed || texture.Width <= 0 || texture.Height <= 0)
					return null;
				if ((long)texture.Width * texture.Height > 4194304L)
					return null;

				Microsoft.Xna.Framework.Color[] source =
					new Microsoft.Xna.Framework.Color[texture.Width * texture.Height];
				texture.GetData(source);

				Bitmap bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
				BitmapData data = null;
				try {
					data = bitmap.LockBits(
						new Rectangle(0, 0, bitmap.Width, bitmap.Height),
						ImageLockMode.WriteOnly,
						PixelFormat.Format32bppArgb);
					int stride = Math.Abs(data.Stride);
					byte[] target = new byte[stride * bitmap.Height];
					for (int y = 0; y < bitmap.Height; y++) {
						int targetRow = y * stride;
						int sourceRow = y * bitmap.Width;
						for (int x = 0; x < bitmap.Width; x++) {
							Microsoft.Xna.Framework.Color pixel = source[sourceRow + x];
							int offset = targetRow + x * 4;
							target[offset] = pixel.B;
							target[offset + 1] = pixel.G;
							target[offset + 2] = pixel.R;
							target[offset + 3] = pixel.A;
						}
					}
					Marshal.Copy(target, 0, data.Scan0, target.Length);
					bitmap.UnlockBits(data);
					data = null;
					return bitmap;
				}
				catch {
					if (data != null)
						bitmap.UnlockBits(data);
					bitmap.Dispose();
					return null;
				}
			}
			catch {
				return null;
			}
		}

		private void RequestInventoryGridRepaint()
		{
			try {
				if (IsDisposed || !IsHandleCreated)
					return;
				BeginInvoke(new Action(delegate {
					if (_inventoryGrid != null && !_inventoryGrid.IsDisposed)
						_inventoryGrid.Invalidate();
					if (_itemEditorGrid != null && !_itemEditorGrid.IsDisposed)
						_itemEditorGrid.Invalidate();
					if (_remoteInventoryGrid != null && !_remoteInventoryGrid.IsDisposed)
						_remoteInventoryGrid.Invalidate();
				}));
			}
			catch {
			}
		}

		private void DisposeIconSources()
		{
			lock (_iconTaskLock) {
				foreach (Task<Bitmap> task in _iconTasks.Values) {
					if (task != null && task.Status == TaskStatus.RanToCompletion &&
						task.Result != null) {
						try {
							task.Result.Dispose();
						}
						catch {
						}
					}
				}
				_iconTasks.Clear();
				_iconRetryAfter.Clear();
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
				Player player = Main.LocalPlayer;
				if (player != null && player.active)
					Text = "Terraria / tModLoader 全物品修改器 - " + player.name;
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

		private static FlowLayoutPanel AddFeatureSection(
			TableLayoutPanel layout, string title, int column, int row)
		{
			return AddFeatureSection(layout, title, column, row, 1);
		}

		private static FlowLayoutPanel AddFeatureSection(
			TableLayoutPanel layout, string title, int column, int row, int columnSpan)
		{
			GroupBox section = new GroupBox();
			section.Text = title;
			section.Dock = DockStyle.Fill;
			section.AutoSize = true;
			section.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			section.Margin = new Padding(6);
			section.Padding = new Padding(10, 8, 10, 10);
			section.ForeColor = Color.WhiteSmoke;
			section.BackColor = Color.FromArgb(34, 36, 42);

			FlowLayoutPanel content = new FlowLayoutPanel();
			content.Dock = DockStyle.Top;
			content.AutoSize = true;
			content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			content.FlowDirection = FlowDirection.TopDown;
			content.WrapContents = false;
			content.Margin = Padding.Empty;
			content.Padding = new Padding(2, 5, 2, 2);
			content.BackColor = Color.Transparent;
			section.Controls.Add(content);

			layout.Controls.Add(section, column, row);
			if (columnSpan > 1)
				layout.SetColumnSpan(section, columnSpan);
			return content;
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

		private static NumericUpDown AddMultiplierRow(
			Control parent, string labelText, decimal defaultValue, Action changed)
		{
			FlowLayoutPanel row = new FlowLayoutPanel();
			row.FlowDirection = FlowDirection.LeftToRight;
			row.WrapContents = false;
			row.AutoSize = true;
			row.Margin = new Padding(24, 0, 3, 6);

			Label label = new Label();
			label.Text = labelText + "：";
			label.Width = 116;
			label.Height = 28;
			label.TextAlign = ContentAlignment.MiddleLeft;
			row.Controls.Add(label);

			NumericUpDown number = new NumericUpDown();
			number.Width = 100;
			number.Height = 28;
			number.DecimalPlaces = 2;
			number.Minimum = 1M;
			number.Maximum = 10M;
			number.Increment = 0.25M;
			number.Value = Math.Max(
				number.Minimum, Math.Min(number.Maximum, defaultValue));
			if (changed != null)
				number.ValueChanged += delegate { changed(); };
			row.Controls.Add(number);

			Label suffix = new Label();
			suffix.Text = "×";
			suffix.Width = 25;
			suffix.Height = 28;
			suffix.TextAlign = ContentAlignment.MiddleLeft;
			row.Controls.Add(suffix);
			parent.Controls.Add(row);
			return number;
		}

		private static NumericUpDown AddDecimalRow(Control parent, string labelText,
			decimal defaultValue, decimal minimum, decimal maximum,
			decimal increment, string suffixText, Action changed)
		{
			FlowLayoutPanel row = new FlowLayoutPanel();
			row.FlowDirection = FlowDirection.LeftToRight;
			row.WrapContents = false;
			row.AutoSize = true;
			row.Margin = new Padding(24, 0, 3, 6);
			Label label = new Label();
			label.Text = labelText + "\uFF1A";
			label.Width = 116; label.Height = 28;
			label.TextAlign = ContentAlignment.MiddleLeft;
			row.Controls.Add(label);
			NumericUpDown number = new NumericUpDown();
			number.Width = 100; number.Height = 28; number.DecimalPlaces = 2;
			number.Minimum = minimum; number.Maximum = maximum;
			number.Increment = increment;
			number.Value = Math.Max(minimum, Math.Min(maximum, defaultValue));
			if (changed != null) number.ValueChanged += delegate { changed(); };
			row.Controls.Add(number);
			Label suffix = new Label();
			suffix.Text = suffixText; suffix.Width = 62; suffix.Height = 28;
			suffix.TextAlign = ContentAlignment.MiddleLeft;
			row.Controls.Add(suffix);
			parent.Controls.Add(row);
			return number;
		}

		private static NumericUpDown AddIntegerRow(
			Control parent, string labelText, decimal defaultValue, Action changed)
		{
			return AddIntegerValueRow(
				parent, labelText, defaultValue, 1M, 10M, "格", changed);
		}

		private static NumericUpDown AddIntegerValueRow(
			Control parent, string labelText, decimal defaultValue,
			decimal minimum, decimal maximum, string suffixText, Action changed)
		{
			FlowLayoutPanel row = new FlowLayoutPanel();
			row.FlowDirection = FlowDirection.LeftToRight;
			row.WrapContents = false;
			row.AutoSize = true;
			row.Margin = new Padding(24, 0, 3, 6);

			Label label = new Label();
			label.Text = labelText + "：";
			label.Width = 116;
			label.Height = 28;
			label.TextAlign = ContentAlignment.MiddleLeft;
			row.Controls.Add(label);

			NumericUpDown number = new NumericUpDown();
			number.Width = 100;
			number.Height = 28;
			number.DecimalPlaces = 0;
			number.Minimum = minimum;
			number.Maximum = maximum;
			number.Increment = 1M;
			number.Value = Math.Max(
				number.Minimum, Math.Min(number.Maximum, defaultValue));
			if (changed != null)
				number.ValueChanged += delegate { changed(); };
			row.Controls.Add(number);

			Label suffix = new Label();
			suffix.Text = suffixText;
			suffix.Width = 62;
			suffix.Height = 28;
			suffix.TextAlign = ContentAlignment.MiddleLeft;
			row.Controls.Add(suffix);
			parent.Controls.Add(row);
			return number;
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
			StyleDarkButton(button);
			return button;
		}

		private static void StyleDarkButton(Button button)
		{
			if (button == null)
				return;
			button.UseVisualStyleBackColor = false;
			button.FlatStyle = FlatStyle.Flat;
			button.BackColor = Color.FromArgb(54, 61, 76);
			button.ForeColor = Color.White;
			button.FlatAppearance.BorderColor = Color.FromArgb(110, 125, 156);
			button.FlatAppearance.MouseOverBackColor = Color.FromArgb(69, 84, 112);
			button.FlatAppearance.MouseDownBackColor = Color.FromArgb(82, 100, 136);
			button.FlatAppearance.CheckedBackColor = Color.FromArgb(82, 100, 136);
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
			grid.ScrollBars = ScrollBars.Both;
			grid.RowTemplate.Height = 27;
			grid.ColumnHeadersHeight = 30;
			grid.ColumnHeadersHeightSizeMode =
				DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
			grid.BackgroundColor = Color.FromArgb(28, 30, 35);
			grid.GridColor = Color.FromArgb(64, 67, 74);
			grid.DefaultCellStyle.BackColor = Color.FromArgb(36, 38, 44);
			grid.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
			grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(67, 92, 132);
			grid.DefaultCellStyle.SelectionForeColor = Color.White;
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

		private static void AddFillTextColumn(
			DataGridView grid,
			string header,
			string property,
			float fillWeight,
			int minimumWidth)
		{
			DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
			column.HeaderText = header;
			column.DataPropertyName = property;
			column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
			column.FillWeight = fillWeight;
			column.MinimumWidth = minimumWidth;
			grid.Columns.Add(column);
		}
	}
}
