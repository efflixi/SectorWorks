using DiscUtils;
using System.IO;

namespace SectorWorks.ManagedNtfs
{
    internal interface INtfsContext
    {
        AllocateFileFn AllocateFile { get; }

        AttributeDefinitions AttributeDefinitions { get; }

        BiosParameterBlock BiosParameterBlock { get; }

        ClusterBitmap ClusterBitmap { get; }

        ForgetFileFn ForgetFile { get; }

        GetDirectoryByIndexFn GetDirectoryByIndex { get; }

        GetDirectoryByRefFn GetDirectoryByRef { get; }

        GetFileByIndexFn GetFileByIndex { get; }

        GetFileByRefFn GetFileByRef { get; }

        MasterFileTable Mft { get; }

        ObjectIds ObjectIds { get; }

        NtfsOptions Options { get; }

        Stream RawStream { get; }

        bool ReadOnly { get; }

        int VolumeVersion { get; }

        ReparsePoints ReparsePoints { get; }

        SecurityDescriptors SecurityDescriptors { get; }

        UpperCase UpperCase { get; }
    }
}
