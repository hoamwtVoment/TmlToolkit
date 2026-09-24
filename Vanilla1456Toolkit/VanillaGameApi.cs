using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Terraria1456Toolkit
{
	internal sealed class RemoteHeldSnapshot
	{
		public int PlayerIndex;
		public string PlayerName;
		public int ItemType;
		public string ItemName;
		public int Stack;
	}

	internal sealed class RemoteInventorySnapshot
	{
		public int PlayerIndex { get; set; }
		public string PlayerName { get; set; }
		public List<InventoryEntry> Items { get; set; }
	}

	internal sealed class ItemAttributeSnapshot
	{
		public int Slot { get; set; }
		public Dictionary<string, object> Values { get; set; }
		public HashSet<string> SupportedFields { get; set; }
	}

	internal sealed class VanillaGameApi
	{
		private sealed class CheatState
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
			public float MovementMultiplier = 3f;
			public bool HighJump;
			public float HighJumpMultiplier = 2.5f;
			public bool InstantRespawn;
			public bool AdjustableStep;
			public int StepBlocks = 3;
			public bool InfiniteFlight;
			public bool FullBright;
			public bool UnrestrictedView;
			public bool ConcurrentAttack;
		}

		private readonly Assembly _terrariaAssembly;
		private readonly string _terrariaDirectory;
		private readonly FieldInfo _mainInstance;
		private readonly FieldInfo _gameMenu;
		private readonly FieldInfo _players;
		private readonly FieldInfo _myPlayer;
		private readonly FieldInfo _netMode;
		private readonly FieldInfo _debuff;
		private readonly PropertyInfo _gameUpdateCount;
		private readonly FieldInfo _screenPosition;
		private readonly FieldInfo _screenWidth;
		private readonly FieldInfo _screenHeight;
		private readonly FieldInfo _maxTilesX;
		private readonly FieldInfo _maxTilesY;
		private readonly FieldInfo _mainMap;
		private readonly FieldInfo _refreshMap;
		private readonly FieldInfo _resetMapFull;
		private readonly MethodInfo _worldMapUpdate;
		private readonly Action<int, int, float, float, float> _addLight;
		private readonly FieldInfo _devLightTilesCheat;
		private readonly FieldInfo _forcedMinimumZoom;
		private readonly FieldInfo _gameZoomTarget;
		private readonly FieldInfo _inputTriggers;
		private readonly FieldInfo _inputCurrent;
		private readonly PropertyInfo _viewZoomIn;
		private readonly PropertyInfo _viewZoomOut;

		private readonly FieldInfo _playerActive;
		private readonly FieldInfo _playerName;
		private readonly FieldInfo _playerWhoAmI;
		private readonly FieldInfo _inventory;
		private readonly PropertyInfo _heldItem;
		private readonly MethodInfo _delBuff;
		private readonly MethodInfo _getItemSource;
		private readonly MethodInfo _quickSpawn;

		private readonly FieldInfo _itemType;
		private readonly FieldInfo _itemStack;
		private readonly FieldInfo _itemPrefix;
		private readonly FieldInfo _itemMaxStack;
		private readonly PropertyInfo _itemName;
		private readonly PropertyInfo _itemIsAir;
		private readonly MethodInfo _setDefaults;
		private readonly MethodInfo _turnToAir;
		private readonly int _itemTypeCount;
		private readonly MethodInfo _solidCollision;
		private readonly FieldInfo _vectorX;
		private readonly FieldInfo _vectorY;
		private readonly PropertyInfo _mountActive;
		private readonly Dictionary<string, FieldInfo> _itemAttributeFields =
			new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

		private readonly Dictionary<string, FieldInfo> _playerFields = new Dictionary<string, FieldInfo>();
		private readonly MethodInfo _sendData;
		private readonly object _itemIconSync = new object();
		private readonly Dictionary<int, Bitmap> _itemIconCache = new Dictionary<int, Bitmap>();
		private readonly Dictionary<int, DateTime> _itemIconRetryAfter = new Dictionary<int, DateTime>();
		private readonly Queue<int> _pendingItemIcons = new Queue<int>();
		private readonly HashSet<int> _pendingItemIconSet = new HashSet<int>();
		private readonly HashSet<int> _loggedItemIconFailures =
			new HashSet<int>();
		private int _itemIconGeneration;
		private int _itemIconWorkerRunning;
		private volatile bool _itemIconWorkerStopping;
		private EventInfo _gameTickEvent;
		private Action _gameTickHandler;
		private volatile CheatState _cheatState = new CheatState();
		private bool _gameTickHooked;
		private readonly object _gameActionSync = new object();
		private readonly Queue<Action> _pendingGameActions = new Queue<Action>();
		private volatile string _lastGameActionError;
		private bool _hasGameUpdateCountSample;
		private uint _lastGameUpdateCount;
		private readonly object _godPowerSync = new object();
		private readonly Dictionary<int, bool> _originalGodPowerStates =
			new Dictionary<int, bool>();
		private object _creativeGodPower;
		private MethodInfo _creativeGodSetEnabled;
		private MethodInfo _creativeGodIsEnabled;
		private DateTime _nextCreativeGodLookupUtc = DateTime.MinValue;
		private volatile bool _mapRevealRequested;
		private volatile bool _mapRevealCancelRequested;
		private volatile bool _mapRevealActive;
		private volatile int _mapRevealDone;
		private volatile int _mapRevealTotal;
		private int _mapRevealX;
		private int _mapRevealY;
		private object _mapRevealMap;
		private Action<int, int, byte> _boundMapUpdate;
		private bool _trainerFullBrightApplied;
		private bool _fullBrightBeforeTrainer;
		private bool _trainerViewZoomApplied;
		private float _forcedMinimumZoomBeforeTrainer;
		private float _unrestrictedGameZoomTarget;
		private bool _concurrentAttackInputWasDown;
		private long _lastGameHeartbeatTicks;
		private volatile bool _gameWorldReady;
		private volatile string _lastGameThreadError = string.Empty;

		private static readonly string[] EditableItemAttributeNames = {
			"type", "stack", "prefix", "damage", "knockBack", "useTime",
			"useAnimation", "scale", "shootSpeed", "shoot", "useAmmo", "pick",
			"axe", "hammer", "fishingPole", "defense", "crit", "mana", "healLife",
			"healMana", "autoReuse"
		};

		public string Version { get; private set; }
		public string LastGameActionError
		{
			get { return _lastGameActionError; }
		}
		public bool IsGameTickHooked
		{
			get { return _gameTickHooked; }
		}
		public int MaxItemType
		{
			get { return Math.Max(0, _itemTypeCount - 1); }
		}
		public bool IsMapRevealActive
		{
			get { return _mapRevealActive || _mapRevealRequested; }
		}
		public bool IsMapRevealCancelling
		{
			get { return _mapRevealCancelRequested && IsMapRevealActive; }
		}
		public int MapRevealDone
		{
			get { return _mapRevealDone; }
		}
		public int MapRevealTotal
		{
			get { return _mapRevealTotal; }
		}
		public bool IsGameThreadResponsive
		{
			get {
				long heartbeat = Interlocked.Read(
					ref _lastGameHeartbeatTicks);
				if (heartbeat <= 0L)
					return false;
				long age = DateTime.UtcNow.Ticks - heartbeat;
				return age >= 0L &&
					age <= TimeSpan.TicksPerSecond * 3L;
			}
		}
		public bool IsWorldReady
		{
			get { return _gameWorldReady; }
		}
		public string LastGameThreadError
		{
			get { return _lastGameThreadError; }
		}
		public int ItemIconGeneration
		{
			get { return Volatile.Read(ref _itemIconGeneration); }
		}

		private VanillaGameApi(Assembly assembly)
		{
			_terrariaAssembly = assembly;
			Version = assembly.GetName().Version == null ? "未知" : assembly.GetName().Version.ToString();
			string assemblyLocation = assembly.Location;
			string assemblyDirectory = string.IsNullOrEmpty(assemblyLocation)
				? null
				: Path.GetDirectoryName(assemblyLocation);
			_terrariaDirectory = string.IsNullOrEmpty(assemblyDirectory)
				? AppDomain.CurrentDomain.BaseDirectory
				: assemblyDirectory;

			Type main = RequireType("Terraria.Main");
			Type player = RequireType("Terraria.Player");
			Type item = RequireType("Terraria.Item");

			_mainInstance = RequireField(main, "instance", true);
			_gameMenu = RequireField(main, "gameMenu", true);
			_players = RequireField(main, "player", true);
			_myPlayer = RequireField(main, "myPlayer", true);
			_netMode = RequireField(main, "netMode", true);
			_debuff = RequireField(main, "debuff", true);
			_gameUpdateCount = main.GetProperty(
				"GameUpdateCount",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			_screenPosition = RequireField(main, "screenPosition", true);
			_screenWidth = RequireField(main, "screenWidth", true);
			_screenHeight = RequireField(main, "screenHeight", true);
			_forcedMinimumZoom = RequireField(main, "ForcedMinimumZoom", true);
			_gameZoomTarget = RequireField(main, "GameZoomTarget", true);
			Type playerInput = RequireType("Terraria.GameInput.PlayerInput");
			_inputTriggers = RequireField(playerInput, "Triggers", true);
			_inputCurrent = RequireField(_inputTriggers.FieldType, "Current", false);
			_viewZoomIn = RequireProperty(_inputCurrent.FieldType, "ViewZoomIn");
			_viewZoomOut = RequireProperty(_inputCurrent.FieldType, "ViewZoomOut");
			_maxTilesX = RequireField(main, "maxTilesX", true);
			_maxTilesY = RequireField(main, "maxTilesY", true);
			_mainMap = RequireField(main, "Map", true);
			_refreshMap = RequireField(main, "refreshMap", true);
			_resetMapFull = RequireField(main, "resetMapFull", true);
			_worldMapUpdate = _mainMap.FieldType.GetMethod(
				"Update",
				BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance,
				null,
				new Type[] { typeof(int), typeof(int), typeof(byte) },
				null);
			if (_worldMapUpdate == null)
				throw new MissingMethodException(_mainMap.FieldType.FullName, "Update");

			Type lighting = RequireType("Terraria.Lighting");
			MethodInfo addLight = lighting.GetMethod(
				"AddLight",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				null,
				new Type[] {
					typeof(int), typeof(int), typeof(float), typeof(float), typeof(float)
				},
				null);
			if (addLight == null)
				throw new MissingMethodException(lighting.FullName, "AddLight");
			_addLight = (Action<int, int, float, float, float>)
				Delegate.CreateDelegate(
					typeof(Action<int, int, float, float, float>),
					addLight);
			Type debugOptions = RequireType("Terraria.Testing.DebugOptions");
			_devLightTilesCheat = RequireField(
				debugOptions, "devLightTilesCheat", true);

			_playerActive = RequireField(player, "active", false);
			_playerName = RequireField(player, "name", false);
			_playerWhoAmI = RequireField(player, "whoAmI", false);
			_inventory = RequireField(player, "inventory", false);
			_heldItem = RequireProperty(player, "HeldItem");
			_delBuff = RequireMethod(player, "DelBuff", 1);
			_getItemSource = RequireMethod(player, "GetItemSource_Misc", 1);
			_quickSpawn = player.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.First(method => method.Name == "QuickSpawnItem" && method.GetParameters().Length == 3 &&
					method.GetParameters()[1].ParameterType == typeof(int) && method.GetParameters()[2].ParameterType == typeof(int));

			_itemType = RequireField(item, "type", false);
			_itemStack = RequireField(item, "stack", false);
			_itemPrefix = RequireField(item, "prefix", false);
			_itemMaxStack = RequireField(item, "maxStack", false);
			_itemName = RequireProperty(item, "Name");
			_itemIsAir = RequireProperty(item, "IsAir");
			_setDefaults = item.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.First(method => method.Name == "SetDefaults" && method.GetParameters().Length >= 1 &&
					method.GetParameters()[0].ParameterType == typeof(int));
			// Terraria 1.4.5.6 changed TurnToAir() to
			// TurnToAir(bool fullReset = false).  Accept both forms so the
			// original backend keeps working across the 1.4.4/1.4.5 boundary.
			_turnToAir = item.GetMethods(
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance)
				.Where(method => method.Name == "TurnToAir")
				.OrderBy(method => method.GetParameters().Length)
				.FirstOrDefault(method => method.GetParameters().All(
					parameter => parameter.IsOptional ||
						parameter.HasDefaultValue));
			if (_turnToAir == null)
				throw new MissingMethodException(item.FullName, "TurnToAir");
			Type itemId = RequireType("Terraria.ID.ItemID");
			FieldInfo itemCount = itemId.GetField(
				"Count",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			if (itemCount == null)
				throw new MissingFieldException(itemId.FullName, "Count");
			_itemTypeCount = Convert.ToInt32(itemCount.GetValue(null));
			if (_itemTypeCount <= 1)
				throw new InvalidOperationException("Terraria.ID.ItemID.Count 无效。");
			foreach (string name in EditableItemAttributeNames) {
				FieldInfo field = item.GetField(
					name,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (field != null && !field.IsInitOnly)
					_itemAttributeFields[name] = field;
			}

			string[] fields = {
				"creativeGodMode", "immune", "immuneNoBlink", "immuneTime",
				"statLife", "statLifeMax2", "statMana", "statManaMax2",
				"breath", "breathMax", "potionDelay", "noKnockback",
				"noFallDmg", "lavaImmune", "moveSpeed", "maxRunSpeed",
				"accRunSpeed", "runAcceleration", "jumpSpeedBoost",
				"wingTime", "wingTimeMax", "rocketTime", "rocketTimeMax",
				"buffType", "controlLeft", "controlRight", "controlJump",
				"velocity", "position", "width", "height", "mount",
				"dead", "respawnTimer", "jump", "controlUseItem",
				"releaseUseItem", "itemAnimation", "itemTime", "reuseDelay"
			};
			foreach (string name in fields)
				_playerFields[name] = RequireField(player, name, false);
			AddOptionalInstanceField(player, "justJumped");
			AddOptionalInstanceField(player, "gravDir");
			// Main.screenPosition, Player.position and Player.velocity are all
			// XNA Vector2 values, and Player.mount is a Terraria.Mount.  Resolve
			// their members once here instead of on every game tick.
			BindingFlags instanceMember = BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.Instance;
			_vectorX = _screenPosition.FieldType.GetField("X", instanceMember);
			_vectorY = _screenPosition.FieldType.GetField("Y", instanceMember);
			_mountActive = _playerFields["mount"].FieldType.GetProperty(
				"Active", instanceMember);
			Type collision = RequireType("Terraria.Collision");
			_solidCollision = collision.GetMethods(
					BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Static)
				.First(method => method.Name == "SolidCollision" &&
					method.GetParameters().Length == 3 &&
					method.GetParameters()[1].ParameterType == typeof(int) &&
					method.GetParameters()[2].ParameterType == typeof(int));

			Type netMessage = RequireType("Terraria.NetMessage");
			_sendData = netMessage.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(method => method.Name == "SendData")
				.OrderByDescending(method => method.GetParameters().Length)
				.First();

			TryInitializeCreativeGodPower();
		}

		public static VanillaGameApi Connect()
		{
			Assembly entry = Assembly.GetEntryAssembly();
			Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(candidate => ReferenceEquals(candidate, entry) &&
					string.Equals(candidate.GetName().Name, "Terraria", StringComparison.OrdinalIgnoreCase));
			if (assembly == null) {
				assembly = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "Terraria", StringComparison.OrdinalIgnoreCase));
			}
			if (assembly == null)
				throw new InvalidOperationException("没有在当前 AppDomain 找到已经运行的 Terraria 程序集。");
			return new VanillaGameApi(assembly);
		}

		public void AttachHooks()
		{
			if (_gameTickHooked)
				return;
			AttachFastMovementGameTick(RequireType("Terraria.Main"));
		}

		public object GetLocalPlayer()
		{
			Array players = (Array)_players.GetValue(null);
			int index = (int)_myPlayer.GetValue(null);
			if (players == null || index < 0 || index >= players.Length)
				return null;
			return players.GetValue(index);
		}

		public bool IsPlayerActive(object player)
		{
			return player != null && (bool)_playerActive.GetValue(player);
		}

		public string GetPlayerName(object player)
		{
			return Convert.ToString(_playerName.GetValue(player)) ?? string.Empty;
		}

		public void ApplyCheats(
			bool godMode,
			bool infiniteLife,
			bool infiniteMana,
			bool infiniteBreath,
			bool noPotionCooldown,
			bool clearDebuffs,
			bool noKnockback,
			bool noFallDamage,
			bool lavaImmune,
			bool fastMovement,
			float movementMultiplier,
			bool highJump,
			float highJumpMultiplier,
			bool instantRespawn,
			bool adjustableStep,
			int stepBlocks,
			bool infiniteFlight,
			bool fullBright,
			bool unrestrictedView,
			bool concurrentAttack)
		{
			_cheatState = new CheatState {
				GodMode = godMode,
				InfiniteLife = infiniteLife,
				InfiniteMana = infiniteMana,
				InfiniteBreath = infiniteBreath,
				NoPotionCooldown = noPotionCooldown,
				ClearDebuffs = clearDebuffs,
				NoKnockback = noKnockback,
				NoFallDamage = noFallDamage,
				LavaImmune = lavaImmune,
				FastMovement = fastMovement,
				MovementMultiplier = ClampMultiplier(movementMultiplier, 3f),
				HighJump = highJump,
				HighJumpMultiplier = ClampMultiplier(highJumpMultiplier, 2.5f),
				InstantRespawn = instantRespawn,
				AdjustableStep = adjustableStep,
				StepBlocks = Math.Max(1, Math.Min(10, stepBlocks)),
				InfiniteFlight = infiniteFlight,
				FullBright = fullBright,
				UnrestrictedView = unrestrictedView,
				ConcurrentAttack = concurrentAttack
			};
		}

		private void ApplyCheatStateOnGameThread(
			CheatState state,
			bool afterPlayerUpdate)
		{
			object player = GetLocalPlayer();
			if (!IsPlayerActive(player)) {
				_concurrentAttackInputWasDown = false;
				RestoreCreativeGodPowerStates();
				return;
			}

			if (state.InstantRespawn && GetBool(player, "dead") &&
				GetInt(player, "respawnTimer") > 1)
				Set(player, "respawnTimer", 1);

			if (state.GodMode) {
				int playerIndex = Convert.ToInt32(_playerWhoAmI.GetValue(player));
				EnableCreativeGodPower(playerIndex);
				Set(player, "creativeGodMode", true);
				Set(player, "immune", true);
				Set(player, "immuneNoBlink", true);
				Set(player, "immuneTime", Math.Max(GetInt(player, "immuneTime"), 60));
				Set(player, "statLife", GetInt(player, "statLifeMax2"));
				Set(player, "statMana", GetInt(player, "statManaMax2"));
				Set(player, "breath", GetInt(player, "breathMax"));
				if (afterPlayerUpdate)
					RestoreCreativeGodPowerStates();
			}
			else
				RestoreCreativeGodPowerStates();

			if (state.InfiniteLife)
				Set(player, "statLife", GetInt(player, "statLifeMax2"));
			if (state.InfiniteMana)
				Set(player, "statMana", GetInt(player, "statManaMax2"));
			if (state.InfiniteBreath)
				Set(player, "breath", GetInt(player, "breathMax"));
			if (state.NoPotionCooldown)
				Set(player, "potionDelay", 0);
			if (state.NoKnockback)
				Set(player, "noKnockback", true);
			if (state.NoFallDamage)
				Set(player, "noFallDmg", true);
			if (state.LavaImmune)
				Set(player, "lavaImmune", true);
			if (state.FastMovement)
				ApplyFastMovement(player, state.MovementMultiplier);
			if (state.HighJump && afterPlayerUpdate)
				ApplyHighJump(player, state.HighJumpMultiplier);
			if (state.AdjustableStep && afterPlayerUpdate)
				ApplyAdjustableStep(player, state.StepBlocks);
			if (!afterPlayerUpdate) {
				bool inputDown = state.ConcurrentAttack &&
					GetBool(player, "controlUseItem");
				if (inputDown && !_concurrentAttackInputWasDown)
					PrepareConcurrentAttack(player);
				_concurrentAttackInputWasDown = inputDown;
			}
			if (state.InfiniteFlight) {
				Set(player, "wingTime", Math.Max(GetFloat(player, "wingTime"), GetInt(player, "wingTimeMax")));
				Set(player, "rocketTime", Math.Max(GetInt(player, "rocketTime"), GetInt(player, "rocketTimeMax")));
			}
			if (state.ClearDebuffs) {
				int[] buffTypes = (int[])_playerFields["buffType"].GetValue(player);
				bool[] debuffs = (bool[])_debuff.GetValue(null);
				for (int slot = buffTypes.Length - 1; slot >= 0; slot--) {
					int type = buffTypes[slot];
					if (type > 0 && type < debuffs.Length && debuffs[type])
						_delBuff.Invoke(player, new object[] { slot });
				}
			}
		}

		private void PrepareConcurrentAttack(object player)
		{
			if (GetBool(player, "dead") ||
				!GetBool(player, "controlUseItem"))
				return;
			object held = _heldItem.GetValue(player, null);
			if (held == null ||
				Convert.ToBoolean(_itemIsAir.GetValue(held, null)))
				return;

			FieldInfo damageField;
			FieldInfo shootField;
			if (!_itemAttributeFields.TryGetValue("damage", out damageField) ||
				!_itemAttributeFields.TryGetValue("shoot", out shootField))
				return;
			int damage = Convert.ToInt32(damageField.GetValue(held));
			int shoot = Convert.ToInt32(shootField.GetValue(held));
			if (damage <= 0 && shoot <= 0)
				return;

			Set(player, "itemAnimation", 0);
			Set(player, "itemTime", 0);
			Set(player, "reuseDelay", 0);
			Set(player, "releaseUseItem", true);
		}

		private void TryInitializeCreativeGodPower()
		{
			if (_creativeGodPower != null ||
				DateTime.UtcNow < _nextCreativeGodLookupUtc)
				return;
			_nextCreativeGodLookupUtc = DateTime.UtcNow.AddSeconds(2);
			try {
				Type managerType = _terrariaAssembly.GetType(
					"Terraria.GameContent.Creative.CreativePowerManager",
					false);
				Type godPowerType = _terrariaAssembly.GetType(
					"Terraria.GameContent.Creative.CreativePowers+GodmodePower",
					false);
				if (managerType == null || godPowerType == null)
					return;

				FieldInfo instanceField = managerType.GetField(
					"Instance",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				object manager = instanceField == null
					? null
					: instanceField.GetValue(null);
				if (manager == null)
					return;

				MethodInfo getPower = managerType.GetMethods(
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					.FirstOrDefault(delegate(MethodInfo method) {
						return method.Name == "GetPower" &&
							method.IsGenericMethodDefinition &&
							method.GetParameters().Length == 0;
					});
				if (getPower == null)
					return;

				object power = getPower.MakeGenericMethod(godPowerType)
					.Invoke(manager, null);
				if (power == null)
					return;

				Type powerType = power.GetType();
				MethodInfo setEnabled = powerType.GetMethod(
					"SetEnabledState",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
					null,
					new Type[] { typeof(int), typeof(bool) },
					null);
				MethodInfo isEnabled = powerType.GetMethod(
					"IsEnabledForPlayer",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
					null,
					new Type[] { typeof(int) },
					null);
				if (setEnabled == null || isEnabled == null)
					return;

				_creativeGodPower = power;
				_creativeGodSetEnabled = setEnabled;
				_creativeGodIsEnabled = isEnabled;
			}
			catch {
				_creativeGodPower = null;
				_creativeGodSetEnabled = null;
				_creativeGodIsEnabled = null;
			}
		}

		private void EnableCreativeGodPower(int playerIndex)
		{
			if (playerIndex < 0)
				return;
			if (_creativeGodPower == null)
				TryInitializeCreativeGodPower();
			if (_creativeGodPower == null ||
				_creativeGodSetEnabled == null ||
				_creativeGodIsEnabled == null)
				return;

			lock (_godPowerSync) {
				if (!_originalGodPowerStates.ContainsKey(playerIndex)) {
					bool original = Convert.ToBoolean(
						_creativeGodIsEnabled.Invoke(
							_creativeGodPower,
							new object[] { playerIndex }));
					_originalGodPowerStates[playerIndex] = original;
				}
				_creativeGodSetEnabled.Invoke(
					_creativeGodPower,
					new object[] { playerIndex, true });
			}
		}

		private void RestoreCreativeGodPowerStates()
		{
			if (_creativeGodPower == null || _creativeGodSetEnabled == null)
				return;
			lock (_godPowerSync) {
				foreach (KeyValuePair<int, bool> pair in _originalGodPowerStates) {
					try {
						_creativeGodSetEnabled.Invoke(
							_creativeGodPower,
							new object[] { pair.Key, pair.Value });
					}
					catch {
					}
				}
				_originalGodPowerStates.Clear();
			}
		}

		private static float ClampMultiplier(float value, float fallback)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
				value = fallback;
			return Math.Max(1f, Math.Min(10f, value));
		}

		// Player.ResetEffects() rewrites the movement multipliers on every Terraria
		// update.  A WinForms timer can consistently run on the wrong side of that
		// reset, which made the old switch appear to do nothing.  Terraria exposes
		// this game-thread event specifically for injected/third-party software, so
		// keep the values alive there as well.  The velocity assist is deliberately
		// input-driven: it gives an immediate run speed without moving the player
		// while no direction key is held.
		private void AttachFastMovementGameTick(Type mainType)
		{
			try {
				_gameTickEvent = mainType.GetEvent(
					"OnTickForThirdPartySoftwareOnly",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				if (_gameTickEvent == null)
					return;

				_gameTickHandler = GameTick;
				_gameTickEvent.AddEventHandler(null, _gameTickHandler);
				_gameTickHooked = true;
			}
			catch {
				// Inventory writes retain a guarded compatibility fallback.
				// Cheat fields are never written from the WinForms thread.
				_gameTickEvent = null;
				_gameTickHandler = null;
				_gameTickHooked = false;
			}
		}

		private void GameTick()
		{
			Interlocked.Exchange(
				ref _lastGameHeartbeatTicks,
				DateTime.UtcNow.Ticks);
			_gameWorldReady = false;
			try {
				bool afterPlayerUpdate = ObserveGameUpdateCountChanged();
				ProcessPendingGameActions(24);
				object localPlayer = GetLocalPlayer();
				// OnTickForThirdPartySoftwareOnly is called once near the start
				// and once at the end of Main.DoUpdate.  During world transitions
				// Player.active can briefly lag behind gameMenu, so accept either
				// signal while still requiring a real local-player object.
				_gameWorldReady = localPlayer != null &&
					(IsPlayerActive(localPlayer) ||
					 !Convert.ToBoolean(_gameMenu.GetValue(null)));
				if (afterPlayerUpdate && _gameWorldReady)
					ProcessMapRevealOnGameThread();

				CheatState state = _cheatState;
				UpdateFullBrightOnGameThread(state.FullBright);
				UpdateUnrestrictedViewOnGameThread(state);
				ApplyCheatStateOnGameThread(state, afterPlayerUpdate);
				_lastGameThreadError = string.Empty;
			}
			catch (Exception ex) {
				_lastGameThreadError = DescribeException(ex);
				// Never let a trainer callback abort Terraria's update loop.
			}
		}

		private static string DescribeException(Exception exception)
		{
			Exception root = exception;
			while (root.InnerException != null)
				root = root.InnerException;
			string message = root.Message;
			if (string.IsNullOrEmpty(message))
				return root.GetType().Name;
			if (message.Length > 100)
				message = message.Substring(0, 100);
			return root.GetType().Name + " - " + message;
		}

		public void RequestMapReveal()
		{
			if (_mapRevealActive || _mapRevealRequested)
				return;
			_mapRevealCancelRequested = false;
			_mapRevealRequested = true;
		}

		public void CancelMapReveal()
		{
			_mapRevealCancelRequested = true;
		}

		private void ProcessMapRevealOnGameThread()
		{
			if (_mapRevealCancelRequested) {
				_mapRevealRequested = false;
				_mapRevealActive = false;
				_boundMapUpdate = null;
				_mapRevealMap = null;
				return;
			}

			if (_mapRevealRequested && !_mapRevealActive) {
				object map = _mainMap.GetValue(null);
				int width = Convert.ToInt32(_maxTilesX.GetValue(null));
				int height = Convert.ToInt32(_maxTilesY.GetValue(null));
				if (map == null || width <= 80 || height <= 80)
					return;
				_mapRevealMap = map;
				_boundMapUpdate = (Action<int, int, byte>)
					Delegate.CreateDelegate(
						typeof(Action<int, int, byte>),
						map,
						_worldMapUpdate);
				_mapRevealX = 40;
				_mapRevealY = 40;
				_mapRevealDone = 0;
				_mapRevealTotal = (width - 80) * (height - 80);
				_mapRevealRequested = false;
				_mapRevealActive = true;
			}
			if (!_mapRevealActive || _boundMapUpdate == null)
				return;

			object currentMap = _mainMap.GetValue(null);
			if (currentMap == null ||
				!ReferenceEquals(currentMap, _mapRevealMap)) {
				_mapRevealActive = false;
				_mapRevealCancelRequested = true;
				_boundMapUpdate = null;
				_mapRevealMap = null;
				return;
			}

			int maxX = Convert.ToInt32(_maxTilesX.GetValue(null)) - 40;
			int maxY = Convert.ToInt32(_maxTilesY.GetValue(null)) - 40;
			const int tilesPerTick = 18000;
			int processed = 0;
			while (processed < tilesPerTick && _mapRevealX < maxX) {
				_boundMapUpdate(_mapRevealX, _mapRevealY, byte.MaxValue);
				processed++;
				_mapRevealDone++;
				_mapRevealY++;
				if (_mapRevealY >= maxY) {
					_mapRevealY = 40;
					_mapRevealX++;
				}
			}

			if (_mapRevealX < maxX)
				return;
			_mapRevealDone = _mapRevealTotal;
			_mapRevealActive = false;
			_mapRevealCancelRequested = false;
			_boundMapUpdate = null;
			_mapRevealMap = null;
			_refreshMap.SetValue(null, true);
			_resetMapFull.SetValue(null, true);
		}

		private void UpdateFullBrightOnGameThread(bool enabled)
		{
			if (!enabled) {
				RestoreFullBrightState();
				return;
			}
			if (!_trainerFullBrightApplied) {
				_fullBrightBeforeTrainer = Convert.ToBoolean(
					_devLightTilesCheat.GetValue(null));
				_trainerFullBrightApplied = true;
			}
			_devLightTilesCheat.SetValue(null, true);

			object screen = _screenPosition.GetValue(null);
			if (screen == null || _vectorX == null || _vectorY == null)
				return;

			float screenX = Convert.ToSingle(_vectorX.GetValue(screen));
			float screenY = Convert.ToSingle(_vectorY.GetValue(screen));
			int pixelWidth = Convert.ToInt32(_screenWidth.GetValue(null));
			int pixelHeight = Convert.ToInt32(_screenHeight.GetValue(null));
			int worldWidth = Convert.ToInt32(_maxTilesX.GetValue(null));
			int worldHeight = Convert.ToInt32(_maxTilesY.GetValue(null));
			if (pixelWidth <= 0 || pixelHeight <= 0 ||
				worldWidth <= 2 || worldHeight <= 2)
				return;

			int left = Math.Max(1, (int)Math.Floor(screenX / 16F) - 16);
			int top = Math.Max(1, (int)Math.Floor(screenY / 16F) - 16);
			int right = Math.Min(
				worldWidth - 2,
				(int)Math.Ceiling((screenX + pixelWidth) / 16F) + 16);
			int bottom = Math.Min(
				worldHeight - 2,
				(int)Math.Ceiling((screenY + pixelHeight) / 16F) + 16);

			// A bright source every eight tiles keeps the whole visible rectangle
			// at full intensity while remaining far below LegacyLighting's 2000
			// per-frame light-source limit.
			const int spacing = 8;
			const float intensity = 100F;
			for (int x = left; x <= right; x += spacing) {
				for (int y = top; y <= bottom; y += spacing)
					_addLight(x, y, intensity, intensity, intensity);
			}
		}

		private void RestoreFullBrightState()
		{
			if (!_trainerFullBrightApplied)
				return;
			try {
				_devLightTilesCheat.SetValue(
					null, _fullBrightBeforeTrainer);
			}
			catch {
			}
			_trainerFullBrightApplied = false;
		}

		private void UpdateUnrestrictedViewOnGameThread(CheatState state)
		{
			if (state == null || !state.UnrestrictedView) {
				RestoreViewZoomState();
				return;
			}
			if (!_trainerViewZoomApplied) {
				_forcedMinimumZoomBeforeTrainer = Convert.ToSingle(
					_forcedMinimumZoom.GetValue(null));
				_unrestrictedGameZoomTarget = Math.Max(
					0.01f,
					Convert.ToSingle(_gameZoomTarget.GetValue(null)) *
					Math.Max(0.01f, _forcedMinimumZoomBeforeTrainer));
				_trainerViewZoomApplied = true;
			}

			object triggers = _inputTriggers.GetValue(null);
			object current = triggers == null
				? null
				: _inputCurrent.GetValue(triggers);
			bool zoomIn = current != null && Convert.ToBoolean(
				_viewZoomIn.GetValue(current, null));
			bool zoomOut = current != null && Convert.ToBoolean(
				_viewZoomOut.GetValue(current, null));
			if (zoomIn != zoomOut) {
				_unrestrictedGameZoomTarget += zoomIn ? 0.01f : -0.01f;
				_unrestrictedGameZoomTarget = Math.Max(
					0.01f, _unrestrictedGameZoomTarget);
			}
			_forcedMinimumZoom.SetValue(null, 1f);
			_gameZoomTarget.SetValue(null, _unrestrictedGameZoomTarget);
		}

		private void RestoreViewZoomState()
		{
			if (!_trainerViewZoomApplied)
				return;
			try {
				_forcedMinimumZoom.SetValue(
					null, _forcedMinimumZoomBeforeTrainer);
			}
			catch {
			}
			_trainerViewZoomApplied = false;
		}

		private void ApplyFastMovement(object player, float multiplier)
		{
			float safeMultiplier = ClampMultiplier(multiplier, 3f);
			float targetSpeed = 6f * safeMultiplier;
			Set(player, "moveSpeed", Math.Max(GetFloat(player, "moveSpeed"), safeMultiplier));
			Set(player, "maxRunSpeed", Math.Max(GetFloat(player, "maxRunSpeed"), targetSpeed));
			Set(player, "accRunSpeed", Math.Max(GetFloat(player, "accRunSpeed"), targetSpeed));
			Set(player, "runAcceleration", Math.Max(GetFloat(player, "runAcceleration"), 0.65f * safeMultiplier));

			bool left = GetBool(player, "controlLeft");
			bool right = GetBool(player, "controlRight");
			if (left == right || IsMounted(player))
				return;

			FieldInfo velocityField = _playerFields["velocity"];
			object velocity = velocityField.GetValue(player);
			if (velocity == null || _vectorX == null)
				return;

			float current = Convert.ToSingle(_vectorX.GetValue(velocity));
			// Preserve dashes, mounts and any faster velocity supplied by gear.
			if (Math.Abs(current) >= targetSpeed)
				return;

			_vectorX.SetValue(velocity, right ? targetSpeed : -targetSpeed);
			velocityField.SetValue(player, velocity);
		}

		private void ApplyHighJump(object player, float multiplier)
		{
			FieldInfo justJumpedField;
			FieldInfo gravDirField;
			FieldInfo jumpField;
			FieldInfo controlJumpField;
			if (!_playerFields.TryGetValue("justJumped", out justJumpedField) ||
				!_playerFields.TryGetValue("gravDir", out gravDirField) ||
				!_playerFields.TryGetValue("jump", out jumpField) ||
				!_playerFields.TryGetValue("controlJump", out controlJumpField) ||
				justJumpedField == null ||
				gravDirField == null ||
				jumpField == null ||
				controlJumpField == null)
				return;
			bool justJumped = Convert.ToBoolean(
				justJumpedField.GetValue(player));
			int remainingJump = Convert.ToInt32(jumpField.GetValue(player));
			bool holdingJump = Convert.ToBoolean(
				controlJumpField.GetValue(player));
			if (remainingJump <= 0 ||
				!justJumped && !holdingJump ||
				IsMounted(player))
				return;

			FieldInfo velocityField = _playerFields["velocity"];
			object velocity = velocityField.GetValue(player);
			if (velocity == null || _vectorY == null)
				return;

			float currentY = Convert.ToSingle(_vectorY.GetValue(velocity));
			float gravityDirection = Convert.ToSingle(gravDirField.GetValue(player));
			if (currentY * gravityDirection >= 0f)
				return;

			float factor = (float)Math.Sqrt(
				ClampMultiplier(multiplier, 2.5f));
			// Terraria rewrites velocity.Y to the base jump speed on every
			// held-jump frame. Apply the factor on every such post-update,
			// and extend the newly-created jump once so the visible height
			// follows the requested multiplier instead of lasting one frame.
			if (justJumped) {
				int extendedJump = Math.Max(
					1, (int)Math.Ceiling(remainingJump * factor));
				jumpField.SetValue(
					player,
					ConvertValue(extendedJump, jumpField.FieldType));
			}
			currentY *= factor;
			_vectorY.SetValue(velocity, ConvertValue(currentY, _vectorY.FieldType));
			velocityField.SetValue(player, velocity);
		}

		private void ApplyAdjustableStep(object player, int maxBlocks)
		{
			if (GetBool(player, "dead") || IsMounted(player))
				return;
			bool left = GetBool(player, "controlLeft");
			bool right = GetBool(player, "controlRight");
			if (left == right)
				return;

			FieldInfo positionField = _playerFields["position"];
			FieldInfo velocityField = _playerFields["velocity"];
			object position = positionField.GetValue(player);
			object velocity = velocityField.GetValue(player);
			if (position == null || velocity == null ||
				_vectorX == null || _vectorY == null)
				return;

			int direction = right ? 1 : -1;
			FieldInfo gravDirField;
			float gravityDirection =
				_playerFields.TryGetValue("gravDir", out gravDirField) &&
				gravDirField != null
					? Convert.ToSingle(gravDirField.GetValue(player))
					: 1f;
			gravityDirection = gravityDirection >= 0f ? 1f : -1f;
			int width = GetInt(player, "width");
			int height = GetInt(player, "height");

			object supportProbe = ShiftVector(
				position, 0f, gravityDirection * 2f);
			if (!HasSolidCollision(supportProbe, width, height))
				return;
			object blockedProbe = ShiftVector(
				position, direction * 3f, 0f);
			if (!HasSolidCollision(blockedProbe, width, height))
				return;

			int maximumLift = Math.Max(1, Math.Min(10, maxBlocks)) * 16;
			for (int lift = 1; lift <= maximumLift; lift++) {
				object candidate = ShiftVector(
					position, direction * 3f, -gravityDirection * lift);
				if (HasSolidCollision(candidate, width, height))
					continue;
				object candidateSupport = ShiftVector(
					candidate, 0f, gravityDirection * 2f);
				if (!HasSolidCollision(candidateSupport, width, height))
					continue;

				float horizontalSpeed = Math.Max(
					2f, Math.Abs(Convert.ToSingle(
						_vectorX.GetValue(velocity))));
				_vectorX.SetValue(
					velocity,
					ConvertValue(
						direction * horizontalSpeed,
						_vectorX.FieldType));
				_vectorY.SetValue(
					velocity,
					ConvertValue(0f, _vectorY.FieldType));
				positionField.SetValue(player, candidate);
				velocityField.SetValue(player, velocity);
				return;
			}
		}

		private bool HasSolidCollision(
			object position, int width, int height)
		{
			return Convert.ToBoolean(_solidCollision.Invoke(
				null,
				new object[] { position, width, height }));
		}

		private object ShiftVector(object vector, float deltaX, float deltaY)
		{
			object shifted = Activator.CreateInstance(vector.GetType());
			float x = Convert.ToSingle(_vectorX.GetValue(vector));
			float y = Convert.ToSingle(_vectorY.GetValue(vector));
			_vectorX.SetValue(
				shifted,
				ConvertValue(x + deltaX, _vectorX.FieldType));
			_vectorY.SetValue(
				shifted,
				ConvertValue(y + deltaY, _vectorY.FieldType));
			return shifted;
		}

		private bool IsMounted(object player)
		{
			object mount = _playerFields["mount"].GetValue(player);
			return mount != null && _mountActive != null &&
				Convert.ToBoolean(_mountActive.GetValue(mount, null));
		}

		private bool ObserveGameUpdateCountChanged()
		{
			if (_gameUpdateCount == null)
				return false;
			uint current = Convert.ToUInt32(_gameUpdateCount.GetValue(null, null));
			if (!_hasGameUpdateCountSample) {
				_lastGameUpdateCount = current;
				_hasGameUpdateCountSample = true;
				return false;
			}
			if (current == _lastGameUpdateCount)
				return false;
			_lastGameUpdateCount = current;
			return true;
		}

		public void DetachHooks()
		{
			_cheatState = new CheatState();
			_concurrentAttackInputWasDown = false;
			_gameWorldReady = false;
			_mapRevealRequested = false;
			_mapRevealCancelRequested = true;
			_mapRevealActive = false;
			_boundMapUpdate = null;
			_mapRevealMap = null;
			_hasGameUpdateCountSample = false;
			_lastGameUpdateCount = 0U;
			RestoreCreativeGodPowerStates();
			RestoreFullBrightState();
			RestoreViewZoomState();
			lock (_gameActionSync)
				_pendingGameActions.Clear();
			lock (_itemIconSync) {
				_itemIconWorkerStopping = true;
				_pendingItemIcons.Clear();
				_pendingItemIconSet.Clear();
			}
			if (!_gameTickHooked || _gameTickEvent == null || _gameTickHandler == null)
				return;

			try {
				_gameTickEvent.RemoveEventHandler(null, _gameTickHandler);
			}
			catch {
			}
			_gameTickHooked = false;
			_gameTickEvent = null;
			_gameTickHandler = null;
		}

		public List<RemoteHeldSnapshot> GetRemoteHeldItems()
		{
			List<RemoteHeldSnapshot> result = new List<RemoteHeldSnapshot>();
			Array players = (Array)_players.GetValue(null);
			int localIndex = (int)_myPlayer.GetValue(null);
			for (int i = 0; i < players.Length; i++) {
				object player = players.GetValue(i);
				if (i == localIndex || !IsPlayerActive(player))
					continue;
				object item = _heldItem.GetValue(player, null);
				if (item == null || (bool)_itemIsAir.GetValue(item, null))
					continue;
				result.Add(new RemoteHeldSnapshot {
					PlayerIndex = i,
					PlayerName = GetPlayerName(player),
					ItemType = (int)_itemType.GetValue(item),
					ItemName = Convert.ToString(_itemName.GetValue(item, null)) ?? string.Empty,
					Stack = (int)_itemStack.GetValue(item)
				});
			}
			return result;
		}

		public List<InventoryEntry> ReadInventory()
		{
			object player = GetLocalPlayer();
			if (!IsPlayerActive(player))
				return new List<InventoryEntry>();
			return ReadPlayerInventory(player);
		}

		/// <summary>
		/// 返回其他联机玩家已经由 Terraria 同步到本客户端的 inventory[0..58]。
		/// 这里不包含服务端私有存储、猪猪储蓄罐、保险箱、护卫熔炉、
		/// 虚空袋或独立的 trashItem。
		/// </summary>
		public List<RemoteInventorySnapshot> GetRemoteInventories()
		{
			List<RemoteInventorySnapshot> result = new List<RemoteInventorySnapshot>();
			Array players = (Array)_players.GetValue(null);
			int localIndex = (int)_myPlayer.GetValue(null);
			if (players == null)
				return result;

			for (int i = 0; i < players.Length; i++) {
				object player = players.GetValue(i);
				if (i == localIndex || !IsPlayerActive(player))
					continue;

				result.Add(new RemoteInventorySnapshot {
					PlayerIndex = i,
					PlayerName = GetPlayerName(player),
					Items = ReadPlayerInventory(player)
				});
			}
			return result;
		}

		private List<InventoryEntry> ReadPlayerInventory(object player)
		{
			List<InventoryEntry> result = new List<InventoryEntry>();
			Array inventory = (Array)_inventory.GetValue(player);
			if (inventory == null)
				return result;

			// Terraria 的联机 ItemOwner/SyncEquipment 数据只覆盖玩家 inventory
			// 的 0..58。58 是鼠标物品，并不是 Player.trashItem。
			int slotCount = Math.Min(inventory.Length, 59);
			for (int slot = 0; slot < slotCount; slot++) {
				object item = inventory.GetValue(slot);
				bool air = item == null || (bool)_itemIsAir.GetValue(item, null);
				result.Add(new InventoryEntry {
					Slot = slot,
					Id = air ? 0 : (int)_itemType.GetValue(item),
					Name = air ? "(空)" : Convert.ToString(_itemName.GetValue(item, null)),
					Stack = air ? 0 : (int)_itemStack.GetValue(item),
					Prefix = air ? 0 : Convert.ToInt32(_itemPrefix.GetValue(item))
				});
			}
			return result;
		}

		public Bitmap GetItemIcon(int type)
		{
			if (type <= 0 || type >= _itemTypeCount)
				return null;

			lock (_itemIconSync) {
				Bitmap cached;
				if (_itemIconCache.TryGetValue(type, out cached))
					return cached;
				if (_itemIconWorkerStopping)
					return null;

				DateTime retryAfter;
				if (_itemIconRetryAfter.TryGetValue(type, out retryAfter) &&
					retryAfter > DateTime.UtcNow)
					return null;
				_itemIconRetryAfter.Remove(type);

				if (_pendingItemIconSet.Add(type)) {
					_pendingItemIcons.Enqueue(type);
				}
			}
			EnsureItemIconWorker();
			// Painting only reads completed Bitmaps. The serial CPU worker
			// decodes XNB files without touching Terraria's GraphicsDevice.
			return null;
		}

		private void EnsureItemIconWorker()
		{
			if (Interlocked.CompareExchange(
					ref _itemIconWorkerRunning, 1, 0) != 0)
				return;
			bool queued = false;
			try {
				queued = ThreadPool.QueueUserWorkItem(delegate {
					ProcessItemIconQueue();
				});
			}
			catch {
			}
			if (!queued)
				Interlocked.Exchange(ref _itemIconWorkerRunning, 0);
		}

		private void ProcessItemIconQueue()
		{
			int currentType = 0;
			try {
				while (true) {
					int type;
					lock (_itemIconSync) {
						if (_itemIconWorkerStopping ||
							_pendingItemIcons.Count == 0)
							return;
						type = _pendingItemIcons.Dequeue();
						currentType = type;
					}

					Bitmap bitmap = null;
					Exception failure = null;
					try {
						bitmap = VanillaXnbTextureDecoder.DecodeItemTexture(
							_terrariaDirectory, type);
					}
					catch (Exception ex) {
						failure = ex;
					}

					bool keepBitmap = false;
					bool recordFailure = false;
					lock (_itemIconSync) {
						_pendingItemIconSet.Remove(type);
						if (_itemIconWorkerStopping) {
							// The form has closed while this file was decoding.
						}
						else if (bitmap != null &&
							!_itemIconCache.ContainsKey(type)) {
							_itemIconCache[type] = bitmap;
							_itemIconRetryAfter.Remove(type);
							Interlocked.Increment(ref _itemIconGeneration);
							keepBitmap = true;
						}
						else if (bitmap == null) {
							_itemIconRetryAfter[type] =
								DateTime.UtcNow.AddSeconds(30);
							recordFailure = true;
						}
					}

					if (!keepBitmap && bitmap != null) {
						try {
							bitmap.Dispose();
						}
						catch {
						}
					}
					if (recordFailure)
						RecordItemIconFailure(type, failure);
					currentType = 0;
				}
			}
			catch {
				// Never let a decoding failure escape a thread-pool thread; that
				// would terminate Terraria. The icon is retried after a delay.
				if (currentType > 0) {
					lock (_itemIconSync) {
						_pendingItemIconSet.Remove(currentType);
						if (!_itemIconWorkerStopping) {
							_itemIconRetryAfter[currentType] =
								DateTime.UtcNow.AddSeconds(30);
						}
					}
				}
			}
			finally {
				Interlocked.Exchange(ref _itemIconWorkerRunning, 0);
				bool restart;
				lock (_itemIconSync) {
					restart = !_itemIconWorkerStopping &&
						_pendingItemIcons.Count > 0;
				}
				if (restart)
					EnsureItemIconWorker();
			}
		}

		private void RecordItemIconFailure(
			int type,
			Exception exception)
		{
			bool shouldLog;
			lock (_itemIconSync)
				shouldLog = _loggedItemIconFailures.Add(type);
			if (!shouldLog)
				return;

			try {
				string directory = Path.Combine(
					Path.GetTempPath(), "HoamTerrariaToolkit");
				Directory.CreateDirectory(directory);
				string path = Path.Combine(
					directory, "vanilla_item_icons.log");
				File.AppendAllText(
					path,
					DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
					" [ID " + type + "] " +
					(exception == null
						? "贴图读取返回空。"
						: exception.ToString()) +
					Environment.NewLine,
					new System.Text.UTF8Encoding(true));
			}
			catch {
			}
		}

		public void GiveItem(int type, int stack)
		{
			QueueGameAction(delegate {
				object player = GetLocalPlayer();
				if (!IsPlayerActive(player))
					throw new InvalidOperationException("本地玩家尚未进入世界。");
				object source = _getItemSource.Invoke(player, new object[] { 0 });
				_quickSpawn.Invoke(player, new object[] { source, type, stack });
			});
		}

		public void ReplaceSlot(int slot, int type, int stack)
		{
			ValidateInventorySlot(slot);
			QueueGameAction(delegate {
				object player = GetLocalPlayer();
				if (!IsPlayerActive(player))
					throw new InvalidOperationException("本地玩家尚未进入世界。");
				Array inventory = (Array)_inventory.GetValue(player);
				object item = inventory.GetValue(slot);
				InvokeWithDefaults(_setDefaults, item, type);
				int maxStack = (int)_itemMaxStack.GetValue(item);
				_itemStack.SetValue(item, Math.Min(stack, Math.Max(1, maxStack)));
				SyncSlot(player, slot, item);
			});
		}

		public void ClearSlot(int slot)
		{
			ValidateInventorySlot(slot);
			QueueGameAction(delegate {
				object player = GetLocalPlayer();
				if (!IsPlayerActive(player))
					throw new InvalidOperationException("本地玩家尚未进入世界。");
				Array inventory = (Array)_inventory.GetValue(player);
				object item = inventory.GetValue(slot);
				InvokeWithOptionalDefaults(_turnToAir, item);
				SyncSlot(player, slot, item);
			});
		}

		public ItemAttributeSnapshot ReadItemAttributes(int slot)
		{
			ValidateInventorySlot(slot);
			object player = GetLocalPlayer();
			if (!IsPlayerActive(player))
				throw new InvalidOperationException("本地玩家尚未进入世界。");
			Array inventory = (Array)_inventory.GetValue(player);
			object item = inventory.GetValue(slot);
			if (item == null)
				throw new InvalidOperationException("所选槽位没有 Item 实例。");

			ItemAttributeSnapshot snapshot = new ItemAttributeSnapshot {
				Slot = slot,
				Values = new Dictionary<string, object>(StringComparer.Ordinal),
				SupportedFields = new HashSet<string>(
					_itemAttributeFields.Keys,
					StringComparer.Ordinal)
			};
			foreach (KeyValuePair<string, FieldInfo> pair in _itemAttributeFields) {
				try {
					snapshot.Values[pair.Key] = pair.Value.GetValue(item);
				}
				catch {
					snapshot.SupportedFields.Remove(pair.Key);
				}
			}
			return snapshot;
		}

		public void RequestItemAttributes(
			int slot,
			Action<ItemAttributeSnapshot, string> completed)
		{
			ValidateInventorySlot(slot);
			QueueGameAction(delegate {
				ItemAttributeSnapshot snapshot = null;
				string error = null;
				try {
					snapshot = ReadItemAttributes(slot);
				}
				catch (Exception ex) {
					error = RootExceptionMessage(ex);
				}
				try {
					if (completed != null)
						completed(snapshot, error);
				}
				catch {
					// UI callbacks are best-effort and must never escape into the
					// Terraria update loop.
				}
			});
		}

		public void WriteItemAttributes(int slot, IDictionary<string, object> values)
		{
			ValidateInventorySlot(slot);
			if (values == null)
				throw new ArgumentNullException("values");

			Dictionary<string, object> copy =
				new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, object> pair in values) {
				if (_itemAttributeFields.ContainsKey(pair.Key))
					copy[pair.Key] = pair.Value;
			}

			QueueGameAction(delegate {
				object player = GetLocalPlayer();
				if (!IsPlayerActive(player))
					throw new InvalidOperationException("本地玩家尚未进入世界。");
				Array inventory = (Array)_inventory.GetValue(player);
				object item = inventory.GetValue(slot);
				if (item == null)
					throw new InvalidOperationException("所选槽位没有 Item 实例。");

				object requestedType;
				int currentType = Convert.ToInt32(_itemType.GetValue(item));
				if (copy.TryGetValue("type", out requestedType)) {
					int nextType = Convert.ToInt32(requestedType);
					if (nextType < 0 || nextType >= _itemTypeCount)
						throw new ArgumentOutOfRangeException(
							"type",
							"物品 ID 必须在 0 到 " + MaxItemType + " 之间。");
					if (nextType == 0) {
						InvokeWithOptionalDefaults(_turnToAir, item);
						SyncSlot(player, slot, item);
						return;
					}
					if (nextType != currentType)
						InvokeWithDefaults(_setDefaults, item, nextType);
				}

				// SetDefaults above produces a coherent Item first.  The explicit
				// editor values are then applied, so changing type and custom
				// attributes in one operation behaves like the classic editor.
				foreach (string name in EditableItemAttributeNames) {
					if (string.Equals(name, "type", StringComparison.Ordinal))
						continue;
					object value;
					FieldInfo field;
					if (!copy.TryGetValue(name, out value) ||
						!_itemAttributeFields.TryGetValue(name, out field))
						continue;
					try {
						field.SetValue(item, ConvertValue(value, field.FieldType));
					}
					catch {
						// A single field changing type between Terraria builds
						// must not cancel the other compatible attributes.
					}
				}
				SyncSlot(player, slot, item);
			});
		}

		private void ValidateInventorySlot(int slot)
		{
			if (slot < 0 || slot > 58)
				throw new ArgumentOutOfRangeException("slot", "背包槽位必须在 0 到 58 之间。");
		}

		private void QueueGameAction(Action action)
		{
			if (action == null)
				return;
			if (!_gameTickHooked) {
				try {
					action();
					_lastGameActionError = null;
				}
				catch (Exception ex) {
					_lastGameActionError = RootExceptionMessage(ex);
				}
				return;
			}
			lock (_gameActionSync)
				_pendingGameActions.Enqueue(action);
		}

		private void ProcessPendingGameActions(int maximumCount)
		{
			for (int i = 0; i < maximumCount; i++) {
				Action action;
				lock (_gameActionSync) {
					if (_pendingGameActions.Count == 0)
						return;
					action = _pendingGameActions.Dequeue();
				}
				try {
					action();
					_lastGameActionError = null;
				}
				catch (Exception ex) {
					_lastGameActionError = RootExceptionMessage(ex);
				}
			}
		}

		private static string RootExceptionMessage(Exception exception)
		{
			Exception root = exception;
			while (root != null && root.InnerException != null)
				root = root.InnerException;
			return root == null
				? "未知错误"
				: root.GetType().Name + " - " + root.Message;
		}

		private void SyncSlot(object player, int slot, object item)
		{
			if ((int)_netMode.GetValue(null) != 1)
				return;
			ParameterInfo[] parameters = _sendData.GetParameters();
			object[] args = new object[parameters.Length];
			for (int i = 0; i < parameters.Length; i++)
				args[i] = DefaultValue(parameters[i].ParameterType);
			args[0] = 5;
			if (args.Length > 1) args[1] = -1;
			if (args.Length > 2) args[2] = -1;
			if (args.Length > 4) args[4] = Convert.ToInt32(_playerWhoAmI.GetValue(player));
			if (args.Length > 5) args[5] = Convert.ChangeType(slot, parameters[5].ParameterType);
			if (args.Length > 6) args[6] = Convert.ChangeType(_itemPrefix.GetValue(item), parameters[6].ParameterType);
			_sendData.Invoke(null, args);
		}

		private static void InvokeWithDefaults(MethodInfo method, object target, int firstArgument)
		{
			object[] args = DefaultArguments(method);
			args[0] = firstArgument;
			method.Invoke(target, args);
		}

		private static void InvokeWithOptionalDefaults(
			MethodInfo method, object target)
		{
			method.Invoke(target, DefaultArguments(method));
		}

		private static object[] DefaultArguments(MethodInfo method)
		{
			ParameterInfo[] parameters = method.GetParameters();
			object[] args = new object[parameters.Length];
			for (int i = 0; i < args.Length; i++) {
				args[i] = parameters[i].HasDefaultValue
					? parameters[i].DefaultValue
					: DefaultValue(parameters[i].ParameterType);
			}
			return args;
		}

		private static object DefaultValue(Type type)
		{
			return type.IsValueType ? Activator.CreateInstance(type) : null;
		}

		private static object ConvertValue(object value, Type targetType)
		{
			if (targetType == null)
				return value;
			if (value == null)
				return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
			if (targetType.IsInstanceOfType(value))
				return value;
			if (targetType.IsEnum)
				return Enum.ToObject(targetType, Convert.ToInt32(value));
			return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
		}

		private Type RequireType(string name)
		{
			Type type = _terrariaAssembly.GetType(name, false);
			if (type == null)
				throw new TypeLoadException(name);
			return type;
		}

		private static FieldInfo RequireField(Type type, string name, bool isStatic)
		{
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
			FieldInfo field = type.GetField(name, flags);
			if (field == null)
				throw new MissingFieldException(type.FullName, name);
			return field;
		}

		private void AddOptionalInstanceField(Type type, string name)
		{
			FieldInfo field = type.GetField(
				name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (field != null)
				_playerFields[name] = field;
		}

		private static PropertyInfo RequireProperty(Type type, string name)
		{
			PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (property == null)
				throw new MissingMemberException(type.FullName, name);
			return property;
		}

		private static MethodInfo RequireMethod(Type type, string name, int parameterCount)
		{
			MethodInfo method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				.FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == parameterCount);
			if (method == null)
				throw new MissingMethodException(type.FullName, name);
			return method;
		}

		private void Set(object player, string name, object value)
		{
			_playerFields[name].SetValue(player, value);
		}

		private int GetInt(object player, string name)
		{
			return Convert.ToInt32(_playerFields[name].GetValue(player));
		}

		private float GetFloat(object player, string name)
		{
			return Convert.ToSingle(_playerFields[name].GetValue(player));
		}

		private bool GetBool(object player, string name)
		{
			return Convert.ToBoolean(_playerFields[name].GetValue(player));
		}
	}
}
