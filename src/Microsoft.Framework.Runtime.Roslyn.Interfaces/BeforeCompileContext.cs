﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Framework.Runtime.Compilation;

namespace Microsoft.Framework.Runtime.Roslyn
{
    public class BeforeCompileContext
    {
        public CSharpCompilation Compilation { get; set; }

        public IProjectContext ProjectContext { get; set; }

        public IList<ResourceDescription> Resources { get; set; }

        public IList<Diagnostic> Diagnostics { get; set; }

        public IList<IMetadataReference> MetadataReferences { get; set; }
    }
}
