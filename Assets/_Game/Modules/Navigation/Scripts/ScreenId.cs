namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// The bottom-navigation screens from <c>GDD.md</c> §13.
    /// </summary>
    /// <remarks>
    /// <para>All five values exist from the start even though only Laws and Trade are built —
    /// the unbuilt three are authored as tabs with <c>Is Available</c> unticked.</para>
    ///
    /// <para><b>Declaration order is not display order.</b> The nav bar renders whichever
    /// buttons a designer authored, in the order they sit in the prefab, and binds each one by
    /// the <c>ScreenId</c> it declares. Reordering this enum is therefore harmless — see
    /// <see cref="BottomNavBarView"/> for why binding by index instead would fail silently.</para>
    ///
    /// <para>This lives in <c>Shared</c> rather than any module because it is the vocabulary the
    /// navigator and every screen share. <c>KingdomRuler.Shared</c> is referenced *by* the
    /// modules, so nothing here may name a module type — which is why the nav bar binds screens
    /// as plain <c>GameObject</c>s tagged with one of these values, rather than knowing about
    /// <c>LawsView</c>.</para>
    /// </remarks>
    public enum ScreenId
    {
        Laws     = 0,
        Trade    = 1,
        Economy  = 2,
        Kingdom  = 3,
        Shop     = 4,
    }
}
