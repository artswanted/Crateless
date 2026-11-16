namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// Descriptor of a painted UI element used consistently for hit testing,
    /// keyboard focus navigation, focus rings, tooltips, and Windows UI Automation.
    /// </summary>
    public sealed class UiElement
    {
        public string Id { get; init; } = "";
        public RectangleF BoundsPx { get; set; }
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
        public string Tooltip { get; init; } = "";
        public AccessibleRole Role { get; init; } = AccessibleRole.PushButton;
        public AccessibleStates States { get; init; } = AccessibleStates.Focusable;
        public bool Focusable { get; init; } = true;
        public bool Enabled { get; init; } = true;
        public bool Selected { get; init; }
        public bool IsSlider { get; init; }
        public RectangleF SliderTrackPx { get; init; }
        public float RadiusDip { get; init; } = SignalTheme.RadiusControl;
        public Action<Point>? OnActivate { get; init; }
        public Action<int>? OnStep { get; init; }
    }

    /// <summary>
    /// Exposes Signal Glass owner-drawn controls as child AccessibleObjects for Windows UI Automation.
    /// </summary>
    public sealed class SignalAccessibleObject : Control.ControlAccessibleObject
    {
        private readonly Func<IReadOnlyList<UiElement>> getElements;
        private readonly Func<string> getFocusedId;
        private readonly Action<string> setFocusId;

        public SignalAccessibleObject(
            Control owner,
            Func<IReadOnlyList<UiElement>> getElements,
            Func<string> getFocusedId,
            Action<string> setFocusId) : base(owner)
        {
            this.getElements = getElements;
            this.getFocusedId = getFocusedId;
            this.setFocusId = setFocusId;
        }

        public override AccessibleRole Role => AccessibleRole.Client;

        public override int GetChildCount() => getElements().Count;

        public override AccessibleObject? GetChild(int index)
        {
            var list = getElements();
            if (index < 0 || index >= list.Count) return null;
            return new SignalElementAccessibleObject(Owner, this, list[index], getFocusedId, setFocusId);
        }

        public override AccessibleObject? HitTest(int x, int y)
        {
            if (!Owner.IsHandleCreated) return null;
            var pt = Owner.PointToClient(new Point(x, y));
            var list = getElements();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].BoundsPx.Contains(pt))
                    return new SignalElementAccessibleObject(Owner, this, list[i], getFocusedId, setFocusId);
            }
            return base.HitTest(x, y);
        }

        public override AccessibleObject? GetFocused()
        {
            string focused = getFocusedId();
            if (!string.IsNullOrEmpty(focused))
            {
                var list = getElements();
                var match = list.FirstOrDefault(e => e.Id == focused);
                if (match is not null)
                    return new SignalElementAccessibleObject(Owner, this, match, getFocusedId, setFocusId);
            }
            return base.GetFocused();
        }
    }

    public sealed class SignalElementAccessibleObject : AccessibleObject
    {
        private readonly Control owner;
        private readonly AccessibleObject parent;
        private readonly UiElement element;
        private readonly Func<string> getFocusedId;
        private readonly Action<string> setFocusId;

        public SignalElementAccessibleObject(
            Control owner,
            AccessibleObject parent,
            UiElement element,
            Func<string> getFocusedId,
            Action<string> setFocusId)
        {
            this.owner = owner;
            this.parent = parent;
            this.element = element;
            this.getFocusedId = getFocusedId;
            this.setFocusId = setFocusId;
        }

        public override AccessibleObject Parent => parent;
        public override string Name => element.Name;
        public override string Value => element.Value;
        public override string Description => element.Tooltip;
        public override AccessibleRole Role => element.Role;

        public override AccessibleStates State
        {
            get
            {
                var st = element.States;
                if (!element.Enabled) st |= AccessibleStates.Unavailable;
                if (element.Focusable && element.Enabled) st |= AccessibleStates.Focusable;
                if (getFocusedId() == element.Id) st |= AccessibleStates.Focused;
                return st;
            }
        }

        public override Rectangle Bounds
        {
            get
            {
                if (!owner.IsHandleCreated) return Rectangle.Empty;
                var clientRect = Rectangle.Round(element.BoundsPx);
                return owner.RectangleToScreen(clientRect);
            }
        }

        public override void DoDefaultAction()
        {
            if (!element.Enabled) return;
            setFocusId(element.Id);
            var pt = new Point((int)(element.BoundsPx.X + element.BoundsPx.Width / 2), (int)(element.BoundsPx.Y + element.BoundsPx.Height / 2));
            element.OnActivate?.Invoke(pt);
        }

        public override void Select(AccessibleSelection flags)
        {
            if ((flags & AccessibleSelection.TakeFocus) != 0 && element.Focusable && element.Enabled)
            {
                setFocusId(element.Id);
            }
        }
    }
}
