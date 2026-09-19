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
        {
            var menu = new ContextMenuStrip();
            foreach (var (text, isChecked, action) in items)
            {
                var item = new ToolStripMenuItem(text) { Checked = isChecked };
                item.Click += (_, _) => { try { action(); } catch (Exception ex) { Logger.WriteLine("Nebula menu: " + ex.Message); } form.Invalidate(); };
                menu.Items.Add(item);
            }
            menu.Show(form, at);
        }
    }
}
