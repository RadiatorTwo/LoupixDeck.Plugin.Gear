namespace LoupixDeck.Plugin.Gear.Devices;

/// <summary>
/// Median filter over the last three battery percentages, to suppress single noisy readings.
/// </summary>
/// <remarks>
/// With fewer than three values the median of the values present is used. With two values the
/// lower one is returned, so a single high spike cannot be shown before a second reading confirms it.
/// </remarks>
internal sealed class BatteryFilter
{
    private const int WindowSize = 3;

    private readonly int[] _window = new int[WindowSize];
    private int _count;
    private int _next;

    /// <summary>Adds a reading and returns the filtered percentage.</summary>
    public int Add(int percent)
    {
        _window[_next] = percent;
        _next = (_next + 1) % WindowSize;
        if (_count < WindowSize)
        {
            _count++;
        }

        return Median();
    }

    /// <summary>Clears the history so the next reading is returned unfiltered.</summary>
    public void Reset()
    {
        _count = 0;
        _next = 0;
    }

    private int Median()
    {
        int[] values = new int[_count];
        Array.Copy(_window, values, _count);
        Array.Sort(values);

        return _count == 2 ? values[0] : values[_count / 2];
    }
}
