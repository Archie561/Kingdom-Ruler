namespace KingdomRuler.Modules.Navigation
{
    /// <summary>
    /// The bottom-navigation screens from <c>GDD.md</c> §13.
    /// </summary>
    /// <remarks>
    /// <para>All five exist although only some screens are built; a tab whose screen is not yet
    /// assigned is shown dimmed (<see cref="NavTabButton.IsAvailable"/>).</para>
    ///
    /// <para><b>Declaration order is not display order:</b> tabs are laid out by the prefab and
    /// bound by the id each declares, so reordering this enum is harmless.</para>
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
