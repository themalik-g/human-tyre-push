using UnityEngine;

public enum TyreDifficulty { Easy, Normal, Hard }

/// <summary>What a difficulty changes. Physics stay real; only tolerances and the track change.</summary>
public struct DifficultyProfile
{
    public string name;
    public string description;
    public float trackWidth;      // multiplier on the path width
    public int barriers;          // 0 none, 1 normal, 2 extra
    public float stability;       // TyrePhysics.stability (self-righting of a moving tyre)
    public float wobble;          // TyrePhysics.wobbleStrength (N m)
    public float rolling;         // TyrePhysics.rollingResistance
    public float headingGain;     // StickTool.headingGain (how strongly the push steers)
    public float steerMaxAngle;   // StickTool.steerMaxAngle (deg)
    public float fallLean;        // |axle.y| at which the tyre counts as fallen
}

/// <summary>The player's menu choices. Saved between runs.</summary>
public static class TyreGameSettings
{
    public static int Character;
    public static int Path;
    public static TyreDifficulty Difficulty = TyreDifficulty.Normal;

    public static readonly string[] PathNames = { "Training Yard", "Hill Climb", "S-Curves", "Mountain Pass" };
    public static readonly string[] PathInfo =
    {
        "Wide, flat practice ground. Learn pushing and steering.",
        "A straight track, then a long gentle ramp to the summit.",
        "A narrow track with a right and a left turn before the climb.",
        "Very narrow track with two steep ramps and barriers."
    };

    public static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };

    public static int PathCount { get { return PathNames.Length; } }

    public static DifficultyProfile Profile(TyreDifficulty d)
    {
        switch (d)
        {
            case TyreDifficulty.Easy:
                return new DifficultyProfile
                {
                    name = "Easy", description = "Wide track, no barriers. The tyre stays up on its own and steers eagerly.",
                    trackWidth = 1.35f, barriers = 0, stability = 2.4f, wobble = 1.5f, rolling = 0.04f,
                    headingGain = 4.5f, steerMaxAngle = 45f, fallLean = 0.8f
                };
            case TyreDifficulty.Hard:
                return new DifficultyProfile
                {
                    name = "Hard", description = "Narrow track, extra barriers. The tyre wobbles, tips sooner and turns less.",
                    trackWidth = 0.8f, barriers = 2, stability = 1.0f, wobble = 5f, rolling = 0.06f,
                    headingGain = 2.0f, steerMaxAngle = 35f, fallLean = 0.55f
                };
            default:
                return new DifficultyProfile
                {
                    name = "Normal", description = "The standard track and tyre.",
                    trackWidth = 1.0f, barriers = 1, stability = 1.6f, wobble = 3.5f, rolling = 0.05f,
                    headingGain = 3.0f, steerMaxAngle = 40f, fallLean = 0.65f
                };
        }
    }

    public static void Load()
    {
        Character = PlayerPrefs.GetInt("tp_character", 0);
        Path = Mathf.Clamp(PlayerPrefs.GetInt("tp_path", 0), 0, PathCount - 1);
        Difficulty = (TyreDifficulty)Mathf.Clamp(PlayerPrefs.GetInt("tp_difficulty", 1), 0, 2);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt("tp_character", Character);
        PlayerPrefs.SetInt("tp_path", Path);
        PlayerPrefs.SetInt("tp_difficulty", (int)Difficulty);
        PlayerPrefs.Save();
    }
}
