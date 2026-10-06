using System.Globalization;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class AddressMapper : IAddressMapper
{
    public static ulong Parse(BinaryAddress address)
    {
        if (address.Value is null || !address.Value.StartsWith("0x", StringComparison.Ordinal) || !ulong.TryParse(address.Value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) { throw new InvalidDataException("Address must be unsigned hexadecimal with a 0x prefix."); }
        return value;
    }

    public AddressMappingResult Map(BinaryInspection binary, BinaryAddress address, AddressSpace destination, ulong? loadBias = null)
    {
        var value = Parse(address);
        if (!Enum.IsDefined(address.AddressSpace) || !Enum.IsDefined(destination)) { throw new InvalidDataException("Unrecognized address space."); }
        AddressMappingResult Unknown(string why) => new(1, address, null, "Unknown", [new Evidence("ELF PT_LOAD mapping", why)]);
        if (binary.Status is BinaryInspectionStatus.Malformed or BinaryInspectionStatus.Unsupported) { return Unknown("No trustworthy mappings available."); }
        if (address.AddressSpace == AddressSpace.RelativeAddress || destination == AddressSpace.RelativeAddress) { return Unknown("Relative addresses require a section/function base and are not mapped implicitly."); }
        if ((address.AddressSpace == AddressSpace.RuntimeAddress || destination == AddressSpace.RuntimeAddress) && loadBias is null) { return Unknown("Runtime conversion requires an explicitly observed load bias."); }
        if (address.AddressSpace == destination) { return new(1, address, address, "Mapped", [new Evidence("address space", "Identity mapping; this does not validate a patch target.")]); }
        if (address.AddressSpace == AddressSpace.RuntimeAddress)
        {
            if (value < loadBias!.Value) { return Unknown("Runtime address precedes load bias."); }
            value -= loadBias.Value;
        }
        var mappings = binary.LoadMappings;
        var candidates = new List<ulong>();
        foreach (var mapping in mappings)
        {
            var fileOffset = Parse(mapping.Offset);
            var virtualAddress = Parse(mapping.Address);
            if (mapping.Offset.AddressSpace != AddressSpace.FileOffset || mapping.Address.AddressSpace != AddressSpace.VirtualAddress || mapping.FileSize < 0 || mapping.MemorySize < mapping.FileSize
                || (ulong)mapping.FileSize > ulong.MaxValue - fileOffset || (ulong)mapping.MemorySize > ulong.MaxValue - virtualAddress) { return Unknown("Malformed mapping data."); }
            if (address.AddressSpace == AddressSpace.FileOffset)
            {
                if (value >= fileOffset && value - fileOffset < (ulong)mapping.FileSize) { candidates.Add(virtualAddress + (value - fileOffset)); }
            }
            else if (value >= virtualAddress && value - virtualAddress < (ulong)mapping.MemorySize)
            {
                var relative = value - virtualAddress;
                if (destination == AddressSpace.FileOffset && relative >= (ulong)mapping.FileSize) { continue; }
                candidates.Add(destination == AddressSpace.FileOffset ? fileOffset + relative : value);
            }
        }
        if (candidates.Count != 1) { return Unknown(candidates.Count == 0 ? "Address has no file-backed mapping (or lies only in zero-filled memory)." : "Overlapping PT_LOAD mappings are ambiguous."); }
        var mapped = candidates[0];
        if (destination == AddressSpace.RuntimeAddress)
        {
            if (mapped > ulong.MaxValue - loadBias!.Value) { return Unknown("Runtime address would overflow."); }
            mapped += loadBias.Value;
        }
        return new(1, address, new BinaryAddress(destination, $"0x{mapped:x}"), "Mapped", [new Evidence("ELF PT_LOAD mapping", "Unique bounded segment mapping; runtime bias, if supplied, is user-provided evidence.")]);
    }
}
