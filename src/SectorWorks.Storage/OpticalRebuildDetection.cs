using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.OpticalBinary;
using static SectorWorks.Storage.OpticalLayout;

namespace SectorWorks.Storage;

internal static class OpticalRebuildDetection
{
    internal static bool HasUdfStart(IReadOnlyBlockDevice device) =>
        device.Length >= Offset(17) && Sector(device, 16).AsSpan(1, 5).SequenceEqual("BEA01"u8);

    internal static int FindUdfRecognition(IReadOnlyBlockDevice device, CancellationToken token)
    {
        for (int block = 16; block < 32 && Offset(block + 3) <= device.Length; block++)
        {
            token.ThrowIfCancellationRequested();
            if (Sector(device, block).AsSpan(1, 5).SequenceEqual("BEA01"u8)
                && (Sector(device, block + 1).AsSpan(1, 5).SequenceEqual("NSR02"u8)
                    || Sector(device, block + 1).AsSpan(1, 5).SequenceEqual("NSR03"u8)))
            { return block; }
        }
        return 0;
    }

    internal static OpticalFileSystem? TryOpen(IReadOnlyBlockDevice device, bool hasIso, int recognition,
        Dictionary<string, string> properties, List<string> warnings, CancellationToken token, bool standaloneCompatibility, bool allowRebuild = true)
    {
        try
        {
            if (device.Length % SectorSize != 0 || device.Length > MaximumBytes)
            { throw new NotSupportedException("Optical medium is not a bounded 2048-byte sector image."); }
            var coverage = new OpticalCoverage(device);
            IsoImage? iso = hasIso ? IsoImage.Read(device, recognition != 0, standaloneCompatibility, coverage, token) : null;
            UdfImage? udf = recognition != 0 ? UdfImage.Read(device, recognition, coverage, token) : null;
            if (iso is null && udf is null) { return null; }
            if (iso is not null && udf is not null)
            {
                if (recognition != iso.Terminator + 1) { throw new NotSupportedException("Nonstandard ISO/UDF descriptor order."); }
                OpticalNamespace.Match(iso.Root, udf.Root, (target, alternate) =>
                {
                    if (target.Name != alternate.Name || target.Hidden != alternate.Hidden)
                    { throw new NotSupportedException("ISO/Joliet and UDF namespaces disagree."); }
                    target.Udf = alternate.Udf;
                }, token);
            }
            OpticalNode root = iso?.Root ?? udf!.Root;
            var label = iso?.Label ?? udf!.Label;
            if (iso is not null)
            {
                properties["Active naming variant"] = iso.ActiveNaming;
                properties["ISO naming namespaces"] = iso.Naming;
                if (iso.TrailingBlocks > 0)
                { properties["ISO trailing padding"] = $"{Offset(iso.TrailingBlocks)} bytes; only verified zero padding can be preserved."; }
                if (iso.Boot is not null)
                {
                    properties["Boot preservation"] = "Catalog and full boot-image files retained at original LBAs. Actual bootability is not guaranteed.";
                }
            }
            if (udf is not null) { properties["UDF"] = "1.02; single read-only physical partition"; }
            properties["Optical logical sector size"] = "2048 bytes";
            if (!allowRebuild)
            {
                var readOnly = new Capability(false, "The container or track layout is qualified for reading only.");
                return new OpticalFileSystem(device, root, iso, udf, label, properties, readOnly);
            }
            Capability capability;
            try
            {
                coverage.Validate(token);
                capability = new(true, "Rebuild required: write a new image preserving the qualified filesystem namespaces and metadata. The source stays unchanged.");
                var candidate = new OpticalFileSystem(device, root, iso, udf, label, properties, capability);
                _ = candidate.PlanRebuild([], token);
                return candidate;
            }
            catch (Exception error) when (IsQualificationFailure(error))
            {
                capability = new(false, "Read-only optical layout: " + error.Message);
                warnings.Add(capability.Reason);
                return new OpticalFileSystem(device, root, iso, udf, label, properties, capability);
            }
        }
        catch (Exception error) when (IsQualificationFailure(error))
        {
            const string explanation = "Optical rebuild unavailable";
            properties["Rebuild qualification"] = explanation + ": " + error.Message;
            warnings.Add(properties["Rebuild qualification"]);
            return null;
        }
    }

    private static bool IsQualificationFailure(Exception error) => error is IOException or NotSupportedException
        or OverflowException or ArgumentException;
}
