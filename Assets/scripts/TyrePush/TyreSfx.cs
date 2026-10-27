using UnityEngine;

/// <summary>
/// Sound effects synthesised at start-up, so the prototype has contact audio without any
/// audio files. Replace with real recordings later by assigning clips on the feedback components.
/// </summary>
public static class TyreSfx
{
    const int Rate = 44100;

    static AudioClip rolling, scrape, thud, thwack;

    /// <summary>Low rumble loop: filtered noise with a little tread "hum".</summary>
    public static AudioClip RollingLoop()
    {
        if (rolling != null) return rolling;

        System.Random rng = new System.Random(11);
        int n = Rate * 2;
        int xf = Rate / 8;
        float[] buf = new float[n + xf];

        float lp1 = 0f, lp2 = 0f;
        for (int i = 0; i < buf.Length; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp1 += (white - lp1) * 0.06f;   // ~400 Hz low-pass
            lp2 += (lp1 - lp2) * 0.10f;
            float hum = Mathf.Sin(2f * Mathf.PI * 62f * i / Rate) * 0.10f;
            buf[i] = lp2 * 2.6f + hum;
        }

        rolling = MakeLoop("TyreRolling", buf, n, xf, 0.8f);
        return rolling;
    }

    /// <summary>Higher, rougher noise loop for sliding / scraping.</summary>
    public static AudioClip ScrapeLoop()
    {
        if (scrape != null) return scrape;

        System.Random rng = new System.Random(23);
        int n = Rate * 2;
        int xf = Rate / 8;
        float[] buf = new float[n + xf];

        float lp = 0f, prev = 0f;
        for (int i = 0; i < buf.Length; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.35f;
            float hp = lp - prev;           // crude high-pass: rubbery squeal band
            prev = lp;
            float flutter = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 17f * i / Rate);
            buf[i] = hp * 1.8f * flutter;
        }

        scrape = MakeLoop("TyreScrape", buf, n, xf, 0.7f);
        return scrape;
    }

    /// <summary>Dull rubber thud for tyre impacts.</summary>
    public static AudioClip Thud()
    {
        if (thud != null) return thud;
        thud = MakeHit("TyreThud", 0.35f, 68f, 0.55f, 0.35f, 28f, 5);
        return thud;
    }

    /// <summary>Sharper knock for the stick hitting the tyre.</summary>
    public static AudioClip Thwack()
    {
        if (thwack != null) return thwack;
        thwack = MakeHit("StickThwack", 0.22f, 190f, 0.35f, 0.65f, 40f, 9);
        return thwack;
    }

    // ------------------------------------------------------------------
    static AudioClip MakeHit(string name, float seconds, float freq, float toneAmount, float noiseAmount, float decay, int seed)
    {
        System.Random rng = new System.Random(seed);
        int n = Mathf.RoundToInt(Rate * seconds);
        float[] data = new float[n];

        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Exp(-decay * t);
            float tone = Mathf.Sin(2f * Mathf.PI * freq * (1f - 0.25f * t) * t);
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.25f;
            data[i] = (tone * toneAmount + lp * noiseAmount) * env;
        }

        AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Builds a seamless loop: the tail (xf samples) is cross-faded into the head.
    static AudioClip MakeLoop(string name, float[] buf, int n, int xf, float gain)
    {
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float v = buf[i];
            if (i < xf)
            {
                float t = i / (float)xf;
                v = buf[i] * t + buf[i + n] * (1f - t);
            }
            data[i] = Mathf.Clamp(v * gain, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
