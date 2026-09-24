using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Terraria1456Toolkit
{
	/// <summary>
	/// Terraria 风格的玩家背包槽位视图。
	/// 0-49：10 x 5 主背包；50-53：钱币；54-57：弹药；
	/// 58：InventoryMouseItem（鼠标物品）。垃圾物品是 Player.trashItem，
	/// 不在 inventory[58] 中。
	/// </summary>
	internal sealed class InventorySlotGrid : Control
	{
		private const int MainColumnCount = 10;
		private const int MainRowCount = 5;
		private const int SlotGap = 3;
		private const int MinimumCellSize = 27;
		private const int MaximumCellSize = 48;

		private readonly Dictionary<int, InventoryEntry> _items =
			new Dictionary<int, InventoryEntry>();
		private readonly ToolTip _toolTip;
		private readonly Font _hotkeyFont;
		private readonly Font _stackFont;
		private readonly Font _captionFont;
		private readonly Font _idFont;
		private readonly Font _compactIdFont;
		private int _selectedSlot = -1;
		private int _hoveredSlot = -1;

		public event EventHandler SelectedSlotChanged;

		/// <summary>
		/// 可选的物品图标提供器。任何异常都会被控件吞掉并退回到 ID 文本。
		/// 返回的 Bitmap 由提供器持有，控件不会 Dispose。
		/// </summary>
		public Func<int, Bitmap> IconProvider { get; set; }

		/// <summary>
		/// 关闭后仍可查看图标和悬停提示，但鼠标/键盘不会改变选中槽位。
		/// 供“别人背包”等只读视图使用。
		/// </summary>
		public bool SelectionEnabled { get; set; }

		public int SelectedSlot
		{
			get { return _selectedSlot; }
			set
			{
				int next = value >= 0 && value <= 58 ? value : -1;
				if (_selectedSlot == next)
					return;
				_selectedSlot = next;
				Invalidate();
			}
		}

		public InventorySlotGrid()
		{
			SetStyle(
				ControlStyles.AllPaintingInWmPaint |
				ControlStyles.OptimizedDoubleBuffer |
				ControlStyles.ResizeRedraw |
				ControlStyles.UserPaint |
				ControlStyles.Selectable,
				true);
			TabStop = true;
			BackColor = Color.FromArgb(28, 30, 35);
			ForeColor = Color.White;
			MinimumSize = new Size(340, 210);
			_hotkeyFont = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
			_stackFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
			_captionFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
			_idFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
			_compactIdFont = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold);
			_toolTip = new ToolTip();
			_toolTip.AutoPopDelay = 10000;
			_toolTip.InitialDelay = 250;
			_toolTip.ReshowDelay = 80;
			_toolTip.ShowAlways = true;
			AccessibleName = "Terraria 背包槽位";
			SelectionEnabled = true;
		}

		public void SetItems(IEnumerable<InventoryEntry> items)
		{
			_items.Clear();
			if (items != null) {
				foreach (InventoryEntry item in items) {
					if (item != null && item.Slot >= 0 && item.Slot <= 58)
						_items[item.Slot] = item;
				}
			}
			Invalidate();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				_toolTip.Dispose();
				_hotkeyFont.Dispose();
				_stackFont.Dispose();
				_captionFont.Dispose();
				_idFont.Dispose();
				_compactIdFont.Dispose();
			}
			base.Dispose(disposing);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
			e.Graphics.TextRenderingHint =
				System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

			LayoutMetrics layout = GetLayoutMetrics();
			DrawSideCaption(e.Graphics, "钱币", 50, layout);
			DrawSideCaption(e.Graphics, "弹药", 54, layout);

			for (int slot = 0; slot <= 58; slot++) {
				Rectangle bounds = GetSlotBounds(slot, layout);
				if (!bounds.IsEmpty)
					DrawSlot(e.Graphics, slot, bounds);
			}

			if (ClientSize.Height > layout.GridBottom + 8) {
				using (Brush hintBrush = new SolidBrush(Color.FromArgb(185, 215, 225, 245))) {
					e.Graphics.DrawString(
						"点击槽位选择；金色边框表示当前槽位",
						_captionFont,
						hintBrush,
						new PointF(layout.OriginX, layout.GridBottom + 7));
				}
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (!SelectionEnabled)
				return;
			if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
				return;

			int slot = HitTest(e.Location);
			if (slot < 0)
				return;

			Focus();
			if (_selectedSlot != slot)
				SelectSlotFromInput(slot);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			int slot = HitTest(e.Location);
			if (slot == _hoveredSlot)
				return;

			_hoveredSlot = slot;
			Invalidate();
			if (slot < 0) {
				_toolTip.SetToolTip(this, null);
				return;
			}

			InventoryEntry item;
			_items.TryGetValue(slot, out item);
			string slotName = GetSlotName(slot);
			string text;
			if (item == null || item.Id <= 0 || item.Stack <= 0) {
				text = slotName + "（槽位 " + slot + "）\r\n空";
			}
			else {
				text = slotName + "（槽位 " + slot + "）\r\n" +
					"ID " + item.Id + "  " + (item.Name ?? "Item_" + item.Id) +
					" × " + item.Stack;
				if (item.Prefix > 0)
					text += "\r\n前缀 ID " + item.Prefix;
			}
			_toolTip.SetToolTip(this, text);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			_hoveredSlot = -1;
			_toolTip.SetToolTip(this, null);
			Invalidate();
		}

		protected override bool IsInputKey(Keys keyData)
		{
			Keys key = keyData & Keys.KeyCode;
			if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
				return true;
			return base.IsInputKey(keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!SelectionEnabled)
				return;
			if (_selectedSlot < 0)
				return;

			int next = MoveSelection(_selectedSlot, e.KeyCode);
			if (next == _selectedSlot)
				return;

			SelectSlotFromInput(next);
			e.Handled = true;
		}

		// Unlike the SelectedSlot setter, a user-driven change raises
		// SelectedSlotChanged.
		private void SelectSlotFromInput(int slot)
		{
			_selectedSlot = slot;
			Invalidate();
			EventHandler handler = SelectedSlotChanged;
			if (handler != null)
				handler(this, EventArgs.Empty);
		}

		private void DrawSideCaption(Graphics graphics, string text, int firstSlot, LayoutMetrics layout)
		{
			Rectangle slot = GetSlotBounds(firstSlot, layout);
			if (slot.IsEmpty)
				return;
			Rectangle caption = new Rectangle(slot.X - 4, slot.Y - 23, slot.Width + 8, 20);
			TextRenderer.DrawText(
				graphics,
				text,
				_captionFont,
				caption,
				Color.FromArgb(238, 241, 255),
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
				TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
		}

		private void DrawSlot(Graphics graphics, int slot, Rectangle bounds)
		{
			bool hotbar = slot >= 0 && slot < MainColumnCount;
			bool selected = slot == _selectedSlot;
			bool hovered = slot == _hoveredSlot;

			using (GraphicsPath path = RoundedRectangle(bounds, Math.Max(4, bounds.Width / 8)))
			using (LinearGradientBrush fill = new LinearGradientBrush(
				bounds,
				hotbar ? Color.FromArgb(222, 75, 105, 205) : Color.FromArgb(205, 60, 82, 164),
				hotbar ? Color.FromArgb(224, 44, 64, 139) : Color.FromArgb(215, 41, 54, 118),
				LinearGradientMode.Vertical))
			using (Pen outer = new Pen(
				selected ? Color.Gold : hovered ? Color.White : Color.FromArgb(218, 111, 147, 224),
				selected ? 3F : hovered ? 2F : 1.5F)) {
				graphics.FillPath(fill, path);
				graphics.DrawPath(outer, path);
			}

			Rectangle inner = Rectangle.Inflate(bounds, -3, -3);
			using (GraphicsPath innerPath = RoundedRectangle(inner, Math.Max(3, inner.Width / 9)))
			using (Pen innerPen = new Pen(Color.FromArgb(130, 15, 26, 83), 1F))
				graphics.DrawPath(innerPen, innerPath);

			InventoryEntry item;
			_items.TryGetValue(slot, out item);
			bool hasItem = item != null && item.Id > 0 && item.Stack > 0;
			if (hasItem)
				DrawItem(graphics, item, bounds);
			else if (slot == 58)
				DrawMouseGlyph(graphics, bounds);

			if (hotbar) {
				string key = slot == 9 ? "0" : (slot + 1).ToString();
				Rectangle keyBounds = new Rectangle(bounds.X + 3, bounds.Y + 2, bounds.Width - 6, 14);
				DrawOutlinedText(graphics, key, _hotkeyFont, keyBounds,
					ContentAlignment.TopLeft, Color.White, Color.FromArgb(205, 18, 27, 65));
			}
		}

		private void DrawItem(Graphics graphics, InventoryEntry item, Rectangle bounds)
		{
			Bitmap icon = null;
			if (IconProvider != null) {
				try {
					icon = IconProvider(item.Id);
				}
				catch {
					icon = null;
				}
			}

			Rectangle iconBounds = Rectangle.Inflate(bounds, -6, -6);
			if (item.Slot >= 0 && item.Slot < MainColumnCount)
				iconBounds.Y += 4;

			if (icon != null && icon.Width > 0 && icon.Height > 0) {
				Rectangle destination = FitInside(icon.Size, iconBounds);
				try {
					graphics.DrawImage(icon, destination);
				}
				catch {
					DrawItemId(graphics, item.Id, iconBounds);
				}
			}
			else {
				DrawItemId(graphics, item.Id, iconBounds);
			}

			if (item.Stack > 1) {
				string amount = item.Stack.ToString();
				Rectangle stackBounds = new Rectangle(
					bounds.X + 2,
					bounds.Bottom - 17,
					bounds.Width - 5,
					15);
				DrawOutlinedText(graphics, amount, _stackFont, stackBounds,
					ContentAlignment.BottomRight, Color.White, Color.FromArgb(225, 12, 17, 45));
			}
		}

		private void DrawItemId(Graphics graphics, int id, Rectangle bounds)
		{
			string text = id.ToString();
			TextRenderer.DrawText(
				graphics,
				text,
				bounds.Width < 35 ? _compactIdFont : _idFont,
				bounds,
				Color.FromArgb(230, 236, 246, 255),
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
				TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
		}

		private static void DrawMouseGlyph(Graphics graphics, Rectangle bounds)
		{
			int width = Math.Max(10, bounds.Width / 3);
			int height = Math.Max(15, bounds.Height / 2);
			int x = bounds.X + (bounds.Width - width) / 2;
			int y = bounds.Y + (bounds.Height - height) / 2;
			using (Pen pen = new Pen(Color.FromArgb(180, 210, 221, 245), Math.Max(1F, bounds.Width / 28F))) {
				graphics.DrawArc(pen, x, y, width, height, 180, 180);
				graphics.DrawLine(pen, x, y + height / 2, x, y + height - width / 2);
				graphics.DrawLine(pen, x + width, y + height / 2, x + width, y + height - width / 2);
				graphics.DrawArc(pen, x, y + height - width, width, width, 0, 180);
				graphics.DrawLine(pen, x + width / 2, y, x + width / 2, y + height / 3);
				graphics.DrawLine(pen, x + 2, y + height / 3, x + width - 2, y + height / 3);
			}
		}

		private static void DrawOutlinedText(
			Graphics graphics,
			string text,
			Font font,
			Rectangle bounds,
			ContentAlignment alignment,
			Color foreground,
			Color outline)
		{
			TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
			if (alignment == ContentAlignment.TopLeft)
				flags |= TextFormatFlags.Left | TextFormatFlags.Top;
			else
				flags |= TextFormatFlags.Right | TextFormatFlags.Bottom;

			Rectangle shadow = bounds;
			shadow.Offset(1, 1);
			TextRenderer.DrawText(graphics, text, font, shadow, outline, flags);
			TextRenderer.DrawText(graphics, text, font, bounds, foreground, flags);
		}

		private int HitTest(Point point)
		{
			LayoutMetrics layout = GetLayoutMetrics();
			for (int slot = 0; slot <= 58; slot++) {
				if (GetSlotBounds(slot, layout).Contains(point))
					return slot;
			}
			return -1;
		}

		private LayoutMetrics GetLayoutMetrics()
		{
			int usableWidth = Math.Max(1, ClientSize.Width - 20 - SlotGap * 11);
			int cellSize = usableWidth / 12;
			cellSize = Math.Max(MinimumCellSize, Math.Min(MaximumCellSize, cellSize));
			int totalWidth = cellSize * 12 + SlotGap * 11;
			int gridHeight = cellSize * MainRowCount + SlotGap * (MainRowCount - 1);
			int hintHeight = ClientSize.Height >= gridHeight + 70 ? 25 : 0;
			int usableHeight = Math.Max(gridHeight, ClientSize.Height - 27 - hintHeight);
			int originY = 27 + Math.Max(0, (usableHeight - gridHeight) / 2);

			return new LayoutMetrics {
				CellSize = cellSize,
				OriginX = Math.Max(5, (ClientSize.Width - totalWidth) / 2),
				OriginY = originY,
				GridBottom = originY + gridHeight
			};
		}

		private static Rectangle GetSlotBounds(int slot, LayoutMetrics layout)
		{
			int column;
			int row;
			if (slot >= 0 && slot < 50) {
				column = slot % MainColumnCount;
				row = slot / MainColumnCount;
			}
			else if (slot >= 50 && slot <= 53) {
				column = 10;
				row = slot - 50;
			}
			else if (slot >= 54 && slot <= 57) {
				column = 11;
				row = slot - 54;
			}
			else if (slot == 58) {
				column = 10;
				row = 4;
			}
			else {
				return Rectangle.Empty;
			}

			return new Rectangle(
				layout.OriginX + column * (layout.CellSize + SlotGap),
				layout.OriginY + row * (layout.CellSize + SlotGap),
				layout.CellSize,
				layout.CellSize);
		}

		private static int MoveSelection(int slot, Keys key)
		{
			if (slot < 50) {
				int row = slot / 10;
				int column = slot % 10;
				if (key == Keys.Left)
					return column > 0 ? slot - 1 : slot;
				if (key == Keys.Right)
					return column < 9 ? slot + 1 : (row < 4 ? 50 + Math.Min(row, 3) : 58);
				if (key == Keys.Up)
					return row > 0 ? slot - 10 : slot;
				if (key == Keys.Down)
					return row < 4 ? slot + 10 : slot;
			}
			else {
				int column = slot >= 54 && slot <= 57 ? 11 : 10;
				int row = slot == 58 ? 4 : slot >= 54 ? slot - 54 : slot - 50;
				if (key == Keys.Left)
					return row * 10 + 9;
				if (key == Keys.Right && column == 10 && row < 4)
					return 54 + row;
				if (key == Keys.Up) {
					if (slot == 58)
						return 53;
					return row > 0 ? slot - 1 : slot;
				}
				if (key == Keys.Down) {
					if (column == 10)
						return row < 3 ? slot + 1 : 58;
					return row < 3 ? slot + 1 : slot;
				}
			}
			return slot;
		}

		private static Rectangle FitInside(Size source, Rectangle target)
		{
			if (source.Width <= 0 || source.Height <= 0)
				return target;
			float scale = Math.Min(
				target.Width / (float)source.Width,
				target.Height / (float)source.Height);
			int width = Math.Max(1, (int)Math.Round(source.Width * scale));
			int height = Math.Max(1, (int)Math.Round(source.Height * scale));
			return new Rectangle(
				target.X + (target.Width - width) / 2,
				target.Y + (target.Height - height) / 2,
				width,
				height);
		}

		private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
		{
			int diameter = Math.Max(2, radius * 2);
			GraphicsPath path = new GraphicsPath();
			path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
			path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
			path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
			path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
			path.CloseFigure();
			return path;
		}

		private static string GetSlotName(int slot)
		{
			if (slot < 50)
				return "主背包 " + (slot + 1);
			if (slot < 54)
				return "钱币 " + (slot - 49);
			if (slot < 58)
				return "弹药 " + (slot - 53);
			return "鼠标物品";
		}

		private struct LayoutMetrics
		{
			public int CellSize;
			public int OriginX;
			public int OriginY;
			public int GridBottom;
		}
	}
}
