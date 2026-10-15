SectorWorks.ManagedNtfs — isolated managed NTFS repair (Phase 2B)

This is a separately named, unsigned SectorWorks assembly, NOT DiscUtils.Ntfs
and NOT a replacement identity for any signed upstream assembly. Public
DiscUtils.Core/Streams 0.16.13 NuGet assemblies retain their original signatures.
FAT/VHD/VHDX packages and their assembly identities are not modified.

Imported NTFS implementation: DiscUtils/DiscUtils commit
59d7cadab839c6d8dfcf52f8be5efe6d2ced190f, MIT. Copyright/license headers
and LICENSE.upstream.txt are retained. No signing keys, binaries or upstream
build tooling are imported. Only required Core-internal helper source files
are copied, privately namespaced; public Core APIs still come from the package.

Namespace-only import: python3 src/SectorWorks.ManagedNtfs/import-upstream.py
WARNING: importing again overwrites repairs. Exact input hashes and baseline
transformations are in upstream-inputs.json. Local repair deltas and final
source hashes are in upstream-repairs.patch and repair-provenance.json.
After editing, record with record-repairs.py; --verify checks without changes.
Both scripts require the approved upstream checkout in the artifacts folder.
Imported and Internal sources are marked generated for formatter/analyzer
purposes to retain the auditable upstream layout rather than restyling the fork.

Security architecture:
- Legacy security attributes and shared $Secure use the sibling managed
  SectorWorks.NtfsSecurity.PortableSecurityDescriptor codec, not Windows ACL APIs.
- GetSecurityBytes/SetSecurityBytes expose self-relative bytes. NewFileOptions
  additionally accepts SecurityDescriptorBytes for explicit creation security.
- GetSecurity/SetSecurity and NewFileOptions.SecurityDescriptor remain Windows-
  annotated RawSecurityDescriptor compatibility bridges only; ordinary portable
  read/create/write paths never invoke them.
- Inheritance is computed/validated before allocating or indexing a new object.
  Directories use directory inheritance. Explicit descriptors bypass parent
  parsing. $Secure hashes deliberately wrap uint addition and use NTFS canonical
  DACL/SACL/owner/group ordering. Missing descriptors are errors, not defaults.
- This codec is metadata serialization/inheritance, not a host token/access-
  check or permission editor. Higher-level writer qualification remains required.

Intentional API/behavior deviations:
- All Format overloads immediately throw NotSupportedException without opening
  or modifying a volume. NtfsFormatter source is retained but not compiled.
- Quota presentation is explicitly reported unsupported by Dump; Quotas source
  is retained but not compiled. Quota bytes are not interpreted or changed.
- BuildClusterMap retains its public signed-Core return type but explicitly
  throws NotSupportedException: Core's ClusterMap constructor is friend-only.
  No reflection, duplicate public Core identity or signed-Core replacement
  is introduced to support this optional diagnostic presentation API.
- Diagnostic security output is canonical self-relative hexadecimal, not SDDL.
- The assembly and ordinary constructor are NOT marked Windows-only.
- Types live in SectorWorks.ManagedNtfs (including .Internals), not DiscUtils.Ntfs.
- SetShortName(longNamePath, null) removes a separate DOS alias (or converts a
  combined Win32/DOS name to Win32-only) without creating a replacement alias.
  Removing through an alias-only path is rejected before mutation.
- SetFileStandardInformation honors the supplied SI change time without an
  automatic current-time transaction stamp and does not fabricate a root '.'
  directory-index entry. Its ordinary selected-name refresh still copies SI
  dates into FILE_NAME and index metadata: preserve distinct original dates
  explicitly using the APIs below after all ordinary mutations.
- NtfsOptions.CreateObjectIds defaults true for upstream compatibility. Qualified
  ordinary writers must explicitly set it false to avoid optional new ObjectId
  attributes and $ObjId index insertions. The option never strips or changes
  existing object IDs; qualification must still reject unsupported existing IDs.

Exact supported FILE_NAME/index timestamp preservation:
GetFileNameTimestamps(path) captures 64 bytes for an ordinary selected name:
four little-endian Int64 FILETIMEs (creation, modification, MFT-change, access)
from its FILE_NAME attribute, followed by the independently captured index
timestamp quad. Root returns only the 32-byte attribute quad; no root index is
invented. Capture each primary name and DOS alias path independently.
SetFileNameTimestamps(path, bytes) validates the exact shape and all dates before
mutation, then restores those timestamps only, without deriving them from SI,
changing names/sizes/flags/security/IDs or stamping new SI times. Apply after
all operations, alias restoration and SI restoration. Metadata restoration is
rejected inside an active timestamping transaction. Unsupported or ambiguous
filename layouts fail closed. These APIs do not qualify unrelated opaque FN
fields; the higher-level qualification/preservation checks still govern them.

Maintaining this source fork carries an ongoing upstream merge/qualification
burden and duplicates private helpers. The application must keep it replaceable
behind its storage interface. Backend compilation or a creation smoke test is
not qualification of all staged writes, allocation metadata, containers or
platforms. Formatting, repair and direct commit are not enabled by this module.

Checked-build compatibility fixes are narrowly scoped: negative BPB record-size
encoding is decoded without a signed narrowing overflow; security hashes use
specified uint modular addition; bitmap clearing uses explicit unchecked
low-eight-bit complement masks. All addressing/count/allocation arithmetic
retains the project's checked policy.

Near-full allocator repair: all three progressive data-allocation fallback
zones accumulate newly found clusters rather than overwrite the prior count.
Free-run scans stop at the exclusive zone end, including extension probes.
AvailableSpace remains upstream bitmap accounting, not a promise that growing
a file by every reported free byte can succeed: bitmap padding can be counted
as used, BPB/stream bounds must agree, and MFT/data-run metadata may also need
clusters. Qualified writers must use independently validated bounds/counts and
conservative metadata headroom, retaining safe out-of-space failure behavior.

Runlist decoding distinguishes unsigned cluster counts from signed relative LCN
deltas. The latter alone deliberately reinterprets sign-extended bits with an
unchecked cast; physical address/VCN accumulation remains checked. Header widths
and source extents are bounded before decoding. Native high-bit unsigned run
lengths and backward fragmented runs do not require source mutation to parse.
The legal zero-byte mapping-pairs terminator is handled before nonterminal
width validation and consumes exactly one byte, matching the attribute reader.

MFT mirror writes cover max(four MFT records, one cluster), not always just four
records. At 64 KiB clusters with 1 KiB records this includes records 0 through63,
including new user records. Existing mirror length must match the geometry;
the backend does not resize or repair it to permit writes.
