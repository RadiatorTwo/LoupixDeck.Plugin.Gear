using LoupixDeck.Plugin.Gear.Hid.Linux;

namespace LoupixDeck.Plugin.Gear.Tests.Hid;

public sealed class ReportDescriptorTests
{
    // Vendor collection: usage page 0xFF02, usage 0x02, report id 8, 16 data bytes in and out.
    internal static readonly byte[] VendorCollection =
    [
        0x06, 0x02, 0xFF, 0x09, 0x02, 0xA1, 0x01,
        0x85, 0x08, 0x75, 0x08, 0x95, 0x10,
        0x09, 0x02, 0x81, 0x02,
        0x09, 0x02, 0x91, 0x02,
        0xC0,
    ];

    // A plain mouse collection (usage page 1) in front of the vendor one, as on a composite interface.
    internal static readonly byte[] MousePlusVendor =
    [
        0x05, 0x01, 0x09, 0x02, 0xA1, 0x01,
        0x09, 0x01, 0xA1, 0x00,
        0x05, 0x09, 0x19, 0x01, 0x29, 0x05, 0x15, 0x00, 0x25, 0x01, 0x95, 0x05, 0x75, 0x01, 0x81, 0x02,
        0xC0, 0xC0,
        .. VendorCollection,
    ];

    [Fact]
    public void Vendor_collection_lengths_include_the_report_id()
    {
        Assert.Equal(((ushort)0xFF02, (ushort)0x02, 17, 17), ReportDescriptor.FirstVendorCollection(VendorCollection, 8));
    }

    [Fact]
    public void Vendor_collection_is_found_behind_a_standard_collection()
    {
        Assert.Equal(((ushort)0xFF02, (ushort)0x02, 17, 17), ReportDescriptor.FirstVendorCollection(MousePlusVendor, 8));
    }

    [Fact]
    public void Unknown_report_id_yields_nothing()
    {
        Assert.Equal(((ushort)0, (ushort)0, 0, 0), ReportDescriptor.FirstVendorCollection(VendorCollection, 7));
    }

    [Fact]
    public void Collections_lists_every_top_level_collection()
    {
        IReadOnlyList<ReportDescriptor.Collection> collections = ReportDescriptor.Collections(MousePlusVendor);
        Assert.Equal(2, collections.Count);
        Assert.Equal((ushort)0x0001, collections[0].UsagePage);
        Assert.Equal((ushort)0xFF02, collections[1].UsagePage);
        Assert.Equal(17, collections[1].InputLength);
    }

    [Fact]
    public void Bit_packed_items_are_summed_before_rounding_to_bytes()
    {
        // Five one-bit buttons plus three constant padding bits: one byte, no report id.
        byte[] descriptor =
        [
            0x05, 0x01, 0x09, 0x02, 0xA1, 0x01,
            0x95, 0x05, 0x75, 0x01, 0x81, 0x02,
            0x95, 0x01, 0x75, 0x03, 0x81, 0x01,
            0xC0,
        ];

        Assert.Equal(1, ReportDescriptor.Collections(descriptor)[0].InputLength);
    }

    [Fact]
    public void Truncated_descriptor_does_not_throw()
    {
        Assert.Empty(ReportDescriptor.Collections([0x06, 0x02]));
    }
}
