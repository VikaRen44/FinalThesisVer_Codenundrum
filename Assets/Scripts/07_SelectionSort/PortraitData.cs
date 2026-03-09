using UnityEngine;

public enum FileSizeUnit
{
    Bit,
    Byte,
    KB,
    MB,
    GB,
    TB
}

[CreateAssetMenu(menuName = "HauntedGallery/Portrait")]
public class PortraitData : ScriptableObject
{
    [Header("Display")]
    public string displayName;
    public Sprite portrait;

    [Header("Stats (base values - can be overridden at runtime)")]
    [Tooltip("Numeric value for file size (example: 500)")]
    public float fileSizeValue = 500;

    public FileSizeUnit fileSizeUnit = FileSizeUnit.KB;

    [Tooltip("Time stored as ISO (optional). Runtime can override.")]
    public string timeIso = "2026-01-01T00:00:00";
}
