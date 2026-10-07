using UnityEngine;

namespace KingdomRuler.Systems.Ledger
{
    /// <summary>
    /// Designer-editable coefficients for the characteristic leveling curve (GDD.md §6).
    /// One asset in Systems/Ledger/ScriptableObjects/ — this is Ledger-owned and shared by
    /// every mechanic that awards characteristic points, not per-module tuning.
    ///
    /// There is deliberately no per-level value array: a formula keeps every level defined,
    /// including ones no designer has reached yet.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelingConfig", menuName = "Kingdom Ruler/Systems/Leveling Config")]
    public sealed class LevelingConfig : ScriptableObject
    {
        [Header("Curve — required(N) = round(base × growth^(N-1) / roundTo) × roundTo")]
        [Tooltip("Points required to clear level 1.")]
        [Min(1f)]
        [SerializeField] private float _basePoints = LevelingCurve.DefaultBasePoints;

        [Tooltip("Multiplier per level. Above 1 means each level costs more than the last.")]
        [Min(0.01f)]
        [SerializeField] private float _growthFactor = LevelingCurve.DefaultGrowthFactor;

        [Tooltip("Round the result to the nearest multiple of this, so players see legible numbers.")]
        [Min(1f)]
        [SerializeField] private float _roundToNearest = LevelingCurve.DefaultRoundToNearest;

        /// <summary>The plain-C# curve the Ledger actually uses.</summary>
        public LevelingCurve ToCurve() => new LevelingCurve(_basePoints, _growthFactor, _roundToNearest);

        private void OnValidate()
        {
            // A non-positive coefficient would make the Ledger's level-up loop
            // non-terminating. LevelingCurve.PointsRequired defends against this too,
            // but catching it here tells the designer immediately instead of silently
            // substituting a different curve than the inspector shows.
            if (_basePoints     < 1f)    _basePoints     = 1f;
            if (_growthFactor   < 0.01f) _growthFactor   = 0.01f;
            if (_roundToNearest < 1f)    _roundToNearest = 1f;
        }
    }
}
