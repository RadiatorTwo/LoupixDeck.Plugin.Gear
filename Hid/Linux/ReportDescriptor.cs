namespace LoupixDeck.Plugin.Gear.Hid.Linux;

/// <summary>Minimal HID report descriptor reader: top-level collections and their report sizes.</summary>
internal static class ReportDescriptor
{
    private const int MainInput = 0x8;
    private const int MainOutput = 0x9;
    private const int MainCollection = 0xA;
    private const int MainEndCollection = 0xC;

    private const int TypeMain = 0;
    private const int TypeGlobal = 1;
    private const int TypeLocal = 2;

    private const int GlobalUsagePage = 0x0;
    private const int GlobalReportSize = 0x7;
    private const int GlobalReportId = 0x8;
    private const int GlobalReportCount = 0x9;
    private const int LocalUsage = 0x0;

    private const ushort VendorPageStart = 0xFF00;

    /// <summary>One top-level collection; lengths are in bytes including the report id byte.</summary>
    /// <param name="UsagePage">Usage page of the collection.</param>
    /// <param name="Usage">Usage of the collection.</param>
    /// <param name="InputLength">Length of the largest input report.</param>
    /// <param name="OutputLength">Length of the largest output report.</param>
    /// <param name="Reports">Input/output length per report id.</param>
    internal sealed record Collection(
        ushort UsagePage,
        ushort Usage,
        int InputLength,
        int OutputLength,
        IReadOnlyDictionary<byte, (int Input, int Output)> Reports);

    /// <summary>
    /// The first vendor-defined collection (usage page 0xFF00 or above) that uses
    /// <paramref name="reportId"/>; all zeroes when there is none.
    /// </summary>
    public static (ushort UsagePage, ushort Usage, int InputLength, int OutputLength) FirstVendorCollection(
        ReadOnlySpan<byte> descriptor, byte reportId)
    {
        foreach (Collection collection in Collections(descriptor))
        {
            if (collection.UsagePage >= VendorPageStart && collection.Reports.TryGetValue(reportId, out (int Input, int Output) lengths))
            {
                return (collection.UsagePage, collection.Usage, lengths.Input, lengths.Output);
            }
        }

        return (0, 0, 0, 0);
    }

    /// <summary>All top-level collections of a descriptor; a malformed tail just ends the parse.</summary>
    public static IReadOnlyList<Collection> Collections(ReadOnlySpan<byte> descriptor)
    {
        List<Collection> result = [];
        Dictionary<byte, (int Input, int Output)>? reports = null;
        int depth = 0;
        ushort usagePage = 0;
        ushort collectionPage = 0;
        ushort collectionUsage = 0;
        ushort usage = 0;
        int reportSize = 0;
        int reportCount = 0;
        byte reportId = 0;

        int i = 0;
        while (i < descriptor.Length)
        {
            byte prefix = descriptor[i++];
            if (prefix == 0xFE)
            {
                // Long item: length byte, tag byte, data. Not used for anything we need.
                if (i >= descriptor.Length)
                {
                    break;
                }

                i += 2 + descriptor[i];
                continue;
            }

            int size = (prefix & 0x3) == 3 ? 4 : prefix & 0x3;
            if (i + size > descriptor.Length)
            {
                break;
            }

            uint value = 0;
            for (int n = 0; n < size; n++)
            {
                value |= (uint)descriptor[i + n] << (8 * n);
            }

            i += size;
            int tag = prefix >> 4;
            switch ((prefix >> 2) & 0x3)
            {
                case TypeGlobal:
                    switch (tag)
                    {
                        case GlobalUsagePage: usagePage = (ushort)value; break;
                        case GlobalReportSize: reportSize = (int)value; break;
                        case GlobalReportId: reportId = (byte)value; break;
                        case GlobalReportCount: reportCount = (int)value; break;
                    }

                    break;

                case TypeLocal:
                    if (tag == LocalUsage)
                    {
                        usage = (ushort)value;
                    }

                    break;

                case TypeMain:
                    switch (tag)
                    {
                        case MainCollection:
                            if (depth++ == 0)
                            {
                                collectionPage = usagePage;
                                collectionUsage = usage;
                                reports = [];
                            }

                            break;

                        case MainEndCollection:
                            if (depth > 0 && --depth == 0 && reports is not null)
                            {
                                result.Add(Build(collectionPage, collectionUsage, reports));
                                reports = null;
                            }

                            break;

                        case MainInput:
                        case MainOutput:
                            if (reports is not null)
                            {
                                AddReport(reports, reportId, tag == MainInput, reportSize * reportCount);
                            }

                            break;
                    }

                    usage = 0;
                    break;
            }
        }

        return result;
    }

    private static void AddReport(Dictionary<byte, (int Input, int Output)> reports, byte reportId, bool input, int bits)
    {
        // Bits are summed per report and direction; Build converts to bytes once.
        reports.TryGetValue(reportId, out (int Input, int Output) current);
        if (input)
        {
            current.Input += bits;
        }
        else
        {
            current.Output += bits;
        }

        reports[reportId] = current;
    }

    private static Collection Build(ushort page, ushort usage, Dictionary<byte, (int Input, int Output)> reportBits)
    {
        Dictionary<byte, (int Input, int Output)> reports = [];
        int input = 0;
        int output = 0;
        foreach ((byte reportId, (int inBits, int outBits)) in reportBits)
        {
            // A direction without items has length 0; otherwise whole bytes plus the report id byte.
            int idByte = reportId != 0 ? 1 : 0;
            int reportIn = inBits > 0 ? ((inBits + 7) / 8) + idByte : 0;
            int reportOut = outBits > 0 ? ((outBits + 7) / 8) + idByte : 0;
            reports[reportId] = (reportIn, reportOut);
            input = Math.Max(input, reportIn);
            output = Math.Max(output, reportOut);
        }

        return new Collection(page, usage, input, output, reports);
    }
}
