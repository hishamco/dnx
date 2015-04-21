// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Framework.Runtime.Compilation;

namespace Microsoft.Framework.Runtime.Roslyn
{
    public class CompilationContext
    {
        private BeforeCompileContext _beforeCompileContext;

        public CompilationContext(CSharpCompilation compilation,
                                  ICompilationProject project,
                                  FrameworkName targetFramework,
                                  string configuration,
                                  IEnumerable<IMetadataReference> incomingReferences,
                                  Func<IList<ResourceDescription>> resourcesResolver)
        {
            Project = project;
            Modules = new List<ICompileModule>();

            var lazyResourceResolver = new Lazy<IList<ResourceDescription>>(() =>
            {
                var sw = Stopwatch.StartNew();
                Logger.TraceInformation("[{0}]: Generating resources for {1}", nameof(BeforeCompileContext), project);

                var resources = resourcesResolver();

                sw.Stop();
                Logger.TraceInformation("[{0}]: Generated resources for {1} in {2}ms", nameof(BeforeCompileContext),
                                                                                       project.Name,
                                                                                       sw.ElapsedMilliseconds);

                return resources;
            });

            var projectContext = new ProjectContext(project, targetFramework, configuration);
            _beforeCompileContext = new BeforeCompileContext(compilation, projectContext, lazyResourceResolver, incomingReferences);
        }

        public ICompilationProject Project { get; }

        public IList<ICompileModule> Modules { get; }

        public CSharpCompilation Compilation
        {
            get { return _beforeCompileContext.Compilation; }
            set { _beforeCompileContext.Compilation = value; }
        }

        public IList<Diagnostic> Diagnostics
        {
            get { return _beforeCompileContext.Diagnostics; }
        }

        public IList<ResourceDescription> Resources
        {
            get { return _beforeCompileContext.Resources; }
        }

        public IProjectContext ProjectContext
        {
            get { return _beforeCompileContext.ProjectContext; }
        }

        public BeforeCompileContext BeforeCompileContext
        {
            get { return _beforeCompileContext; }
        }
    }
}
