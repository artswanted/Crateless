namespace GHelper.UI.Nebula
{
    /// <summary>
    /// A page of the Nebula shell. Pages paint from already-read state, collect hit regions
    /// through the canvas and translate clicks into calls on the existing controllers.
    /// </summary>
    public abstract class NebulaPage
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Subtitle { get; }

        /// <summary>Called on the UI thread after each sensor tick and when the page opens.</summary>
        public virtual void Refresh(bool opened) { }

        /// <summary>Bottom edge of the page content in design px; the shell scrolls when it exceeds the window.</summary>
        public virtual float ContentHeight => 1013;

        public abstract void Paint(NebulaCanvas c);

        /// <summary>Returns true when the click was handled. `at` is the client point (for menus).</summary>
        public virtual bool Click(string id, Point at, NebulaForm form) => false;

        /// <summary>Slider drag: t in 0..1; done = mouse released.</summary>
        public virtual void Drag(string id, float t, bool done) { }

        // -- shared helpers for pages --------------------------------------------------------------
        protected static void Menu(NebulaForm form, Point at, IEnumerable<(string text, bool isChecked, Action action)> items)
            => Menu(form, at, items.Select(i => (i.text, i.isChecked, (Image?)null, i.action)));

        /// <summary>Same menu with a small picture in front of each entry (language flags).</summary>
        protected static void Menu(NebulaForm form, Point at, IEnumerable<(string text, bool isChecked, Image? image, Action action)> items)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = true, ImageScalingSize = new Size(20, 14) };
            foreach (var (text, isChecked, image, action) in items)
            {
                var item = new ToolStripMenuItem(text) { Checked = isChecked, Image = image, ImageScaling = ToolStripItemImageScaling.None };
                // a picture replaces the tick mark, so the current entry is marked by its weight instead
                if (isChecked && image is not null) item.Font = new Font(item.Font, FontStyle.Bold);
                item.Click += (_, _) => { try { action(); } catch (Exception ex) { Logger.WriteLine("Nebula menu: " + ex.Message); } form.Invalidate(); };
                menu.Items.Add(item);
            }
            menu.Show(form, at);
        }
    }
}
