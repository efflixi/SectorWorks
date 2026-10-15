using DiscUtils;
//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System.Security.AccessControl;
using System.Runtime.Versioning;
using SectorWorks.NtfsSecurity;

namespace SectorWorks.ManagedNtfs
{
    /// <summary>
    /// Options controlling how new NTFS files are created.
    /// </summary>
    public sealed class NewFileOptions
    {
        internal PortableSecurityDescriptor PortableDescriptor { get; private set; }

        /// <summary>
        /// Initializes a new instance of the NewFileOptions class.
        /// </summary>
        public NewFileOptions()
        {
            Compressed = null;
            CreateShortNames = null;
        }

        /// <summary>
        /// Gets or sets whether the new file should be compressed.
        /// </summary>
        /// <remarks>The default (<c>null</c>) value indicates the file system default behaviour applies.</remarks>
        public bool? Compressed { get; set; }

        /// <summary>
        /// Gets or sets whether a short name should be created for the file.
        /// </summary>
        /// <remarks>The default (<c>null</c>) value indicates the file system default behaviour applies.</remarks>
        public bool? CreateShortNames { get; set; }

        /// <summary>
        /// Gets or sets the security descriptor that to set for the new file.
        /// </summary>
        /// <remarks>The default (<c>null</c>) value indicates the security descriptor is inherited.</remarks>
        [SupportedOSPlatform("windows")]
        public RawSecurityDescriptor SecurityDescriptor
        {
            get { return PortableDescriptor == null ? null : new RawSecurityDescriptor(PortableDescriptor.ToBinary(), 0); }
            set
            {
                if (value == null)
                {
                    PortableDescriptor = null;
                    return;
                }

                byte[] bytes = new byte[value.BinaryLength];
                value.GetBinaryForm(bytes, 0);
                PortableDescriptor = PortableSecurityDescriptor.Parse(bytes);
            }
        }

        /// <summary>Gets or sets an explicit descriptor as portable self-relative bytes.</summary>
        public byte[] SecurityDescriptorBytes
        {
            get { return PortableDescriptor == null ? null : PortableDescriptor.ToBinary(); }
            set { PortableDescriptor = value == null ? null : PortableSecurityDescriptor.Parse(value); }
        }
    }
}