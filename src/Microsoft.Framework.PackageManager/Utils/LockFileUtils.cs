using System;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using NuGet;
using NuGet.ContentModel;
using Microsoft.Framework.Runtime.DependencyManagement;

namespace Microsoft.Framework.PackageManager.Utils
{
    internal static class LockFileUtils
    {
        public static LockFileLibrary CreateLockFileLibraryForProject2(IPackage package, SHA512 sha512, string correctedPackageName = null)
        {
            var lockFileLib = new LockFileLibrary();

            // package.Id is read from nuspec and it might be in wrong casing.
            // correctedPackageName should be the package name used by dependency graph and
            // it has the correct casing that runtime needs during dependency resolution.
            lockFileLib.Name = correctedPackageName ?? package.Id;
            lockFileLib.Version = package.Version;

            using (var nupkgStream = package.GetStream())
            {
                lockFileLib.Sha512 = Convert.ToBase64String(sha512.ComputeHash(nupkgStream));
            }

            lockFileLib.Files = package.GetFiles().Select(p => p.Path).ToList();

            return lockFileLib;
        }

        public static LockFileTargetLibrary CreateLockFileTargetLibrary(IPackage package, RestoreContext context, DefaultPackagePathResolver defaultPackagePathResolver, string correctedPackageName)
        {
            var lockFileLib = new LockFileTargetLibrary();

            var framework = context.FrameworkName;
            var runtimeIdentifier = context.RuntimeName;

            // package.Id is read from nuspec and it might be in wrong casing.
            // correctedPackageName should be the package name used by dependency graph and
            // it has the correct casing that runtime needs during dependency resolution.
            lockFileLib.Name = correctedPackageName ?? package.Id;
            lockFileLib.Version = package.Version;

            var files = package.GetFiles().Select(p => p.Path.Replace(Path.DirectorySeparatorChar, '/')).ToList();

            var contentItems = new ContentItemCollection();
            contentItems.Load(files);

            IEnumerable<PackageDependencySet> dependencySet;
            if (VersionUtility.TryGetCompatibleItems(framework, package.DependencySets, out dependencySet))
            {
                var set = dependencySet.FirstOrDefault()?.Dependencies?.ToList();

                if (set != null)
                {
                    lockFileLib.Dependencies = set;
                }
            }

            // TODO: Remove this when we do #596
            // ASP.NET Core isn't compatible with generic PCL profiles
            if (!string.Equals(framework.Identifier, VersionUtility.AspNetCoreFrameworkIdentifier, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(framework.Identifier, VersionUtility.DnxCoreFrameworkIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                IEnumerable<FrameworkAssemblyReference> frameworkAssemblies;
                if (VersionUtility.TryGetCompatibleItems(framework, package.FrameworkAssemblies, out frameworkAssemblies))
                {
                    foreach (var assemblyReference in frameworkAssemblies)
                    {
                        if (!assemblyReference.SupportedFrameworks.Any() &&
                            !VersionUtility.IsDesktop(framework))
                        {
                            // REVIEW: This isn't 100% correct since none *can* mean 
                            // any in theory, but in practice it means .NET full reference assembly
                            // If there's no supported target frameworks and we're not targeting
                            // the desktop framework then skip it.

                            // To do this properly we'll need all reference assemblies supported
                            // by each supported target framework which isn't always available.
                            continue;
                        }

                        lockFileLib.FrameworkAssemblies.Add(assemblyReference);
                    }
                }
            }

            var patterns = new PatternDefinitions();

            var criteriaBuilderWithTfm = new SelectionCriteriaBuilder(patterns.Properties.Definitions);
            var criteriaBuilderWithoutTfm = new SelectionCriteriaBuilder(patterns.Properties.Definitions);

            if (context.RuntimeSpecs != null)
            {
                foreach (var runtimeSpec in context.RuntimeSpecs)
                {
                    criteriaBuilderWithTfm = criteriaBuilderWithTfm
                    .Add["tfm", framework]["rid", runtimeSpec.Name];

                    criteriaBuilderWithoutTfm = criteriaBuilderWithoutTfm
                        .Add["rid", runtimeSpec.Name];
                }
            }

            criteriaBuilderWithTfm = criteriaBuilderWithTfm
                .Add["tfm", framework];

            var criteria = criteriaBuilderWithTfm.Criteria;

            var compileGroup = contentItems.FindBestItemGroup(criteria, patterns.CompileTimeAssemblies, patterns.ManagedAssemblies);

            if (compileGroup != null)
            {
                lockFileLib.CompileTimeAssemblies = compileGroup.Items.Select(t => t.Path).ToList();
            }

            var runtimeGroup = contentItems.FindBestItemGroup(criteria, patterns.ManagedAssemblies);
            if (runtimeGroup != null)
            {
                lockFileLib.RuntimeAssemblies = runtimeGroup.Items.Select(p => p.Path).ToList();
            }

            var nativeGroup = contentItems.FindBestItemGroup(criteriaBuilderWithoutTfm.Criteria, patterns.NativeLibraries);
            if (nativeGroup != null)
            {
                lockFileLib.NativeLibraries = nativeGroup.Items.Select(p => p.Path).ToList();
            }

            // COMPAT: Support lib/contract so older packages can be consumed
            string contractPath = "lib/contract/" + package.Id + ".dll";
            var hasContract = files.Any(path => path == contractPath);
            var hasLib = lockFileLib.RuntimeAssemblies.Any();

            if (hasContract && hasLib && !VersionUtility.IsDesktop(framework))
            {
                lockFileLib.CompileTimeAssemblies.Clear();
                lockFileLib.CompileTimeAssemblies.Add(contractPath);
            }

            // TODO: figure out servicable
            //var installPath = resolver.GetInstallPath(package.Id, package.Version);
            //foreach (var assembly in lockFileLib.FrameworkGroups.SelectMany(f => f.RuntimeAssemblies))
            //{
            //    var assemblyPath = Path.Combine(installPath, assembly);
            //    if (IsAssemblyServiceable(assemblyPath))
            //    {
            //        lockFileLib.IsServiceable = true;
            //        break;
            //    }
            //}

            return lockFileLib;
        }

        public static LockFileLibrary CreateLockFileLibraryForProject(
            Runtime.Project project,
            IPackage package,
            SHA512 sha512,
            IEnumerable<FrameworkName> frameworks,
            IPackagePathResolver resolver,
            string correctedPackageName = null)
        {
            var lockFileLib = new LockFileLibrary();

            // package.Id is read from nuspec and it might be in wrong casing.
            // correctedPackageName should be the package name used by dependency graph and
            // it has the correct casing that runtime needs during dependency resolution.
            lockFileLib.Name = correctedPackageName ?? package.Id;
            lockFileLib.Version = package.Version;

            using (var nupkgStream = package.GetStream())
            {
                lockFileLib.Sha512 = Convert.ToBase64String(sha512.ComputeHash(nupkgStream));
            }

            lockFileLib.Files = package.GetFiles().Select(p => p.Path).ToList();

            foreach (var framework in frameworks)
            {
                var group = new LockFileFrameworkGroup();
                group.TargetFramework = framework;

                IEnumerable<PackageDependencySet> dependencySet;
                if (VersionUtility.TryGetCompatibleItems(framework, package.DependencySets, out dependencySet))
                {
                    var set = dependencySet.FirstOrDefault()?.Dependencies?.ToList();

                    if (set != null)
                    {
                        group.Dependencies = set;
                    }
                }

                // TODO: Remove this when we do #596
                // ASP.NET Core isn't compatible with generic PCL profiles
                if (!string.Equals(framework.Identifier, VersionUtility.AspNetCoreFrameworkIdentifier, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(framework.Identifier, VersionUtility.DnxCoreFrameworkIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    IEnumerable<FrameworkAssemblyReference> frameworkAssemblies;
                    if (VersionUtility.TryGetCompatibleItems(framework, package.FrameworkAssemblies, out frameworkAssemblies))
                    {
                        foreach (var assemblyReference in frameworkAssemblies)
                        {
                            if (!assemblyReference.SupportedFrameworks.Any() &&
                                !VersionUtility.IsDesktop(framework))
                            {
                                // REVIEW: This isn't 100% correct since none *can* mean 
                                // any in theory, but in practice it means .NET full reference assembly
                                // If there's no supported target frameworks and we're not targeting
                                // the desktop framework then skip it.

                                // To do this properly we'll need all reference assemblies supported
                                // by each supported target framework which isn't always available.
                                continue;
                            }

                            group.FrameworkAssemblies.Add(assemblyReference);
                        }
                    }
                }

                group.RuntimeAssemblies = GetPackageAssemblies(package, framework);

                string contractPath = Path.Combine("lib", "contract", package.Id + ".dll");
                var hasContract = lockFileLib.Files.Any(path => path == contractPath);
                var hasLib = group.RuntimeAssemblies.Any();

                if (hasContract && hasLib && !VersionUtility.IsDesktop(framework))
                {
                    group.CompileTimeAssemblies.Add(contractPath);
                }
                else if (hasLib)
                {
                    group.CompileTimeAssemblies.AddRange(group.RuntimeAssemblies);
                }

                lockFileLib.FrameworkGroups.Add(group);
            }

            var installPath = resolver.GetInstallPath(package.Id, package.Version);
            foreach (var assembly in lockFileLib.FrameworkGroups.SelectMany(f => f.RuntimeAssemblies))
            {
                var assemblyPath = Path.Combine(installPath, assembly);
                if (IsAssemblyServiceable(assemblyPath))
                {
                    lockFileLib.IsServiceable = true;
                    break;
                }
            }

            return lockFileLib;
        }

        private static List<string> GetPackageAssemblies(IPackage package, FrameworkName targetFramework)
        {
            var results = new List<string>();

            IEnumerable<IPackageAssemblyReference> compatibleReferences;
            if (VersionUtility.TryGetCompatibleItems(targetFramework, package.AssemblyReferences, out compatibleReferences))
            {
                // Get the list of references for this target framework
                var references = compatibleReferences.ToList();

                // See if there's a list of specific references defined for this target framework
                IEnumerable<PackageReferenceSet> referenceSets;
                if (VersionUtility.TryGetCompatibleItems(targetFramework, package.PackageAssemblyReferences, out referenceSets))
                {
                    // Get the first compatible reference set
                    var referenceSet = referenceSets.FirstOrDefault();

                    if (referenceSet != null)
                    {
                        // Remove all assemblies of which names do not appear in the References list
                        references.RemoveAll(r => !referenceSet.References.Contains(r.Name, StringComparer.OrdinalIgnoreCase));
                    }
                }

                foreach (var reference in references)
                {
                    // Skip anything that isn't a dll. Unfortunately some packages put random stuff
                    // in the lib folder and they surface as assembly references
                    if (!Path.GetExtension(reference.Path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(reference.Path);
                }
            }

            return results;
        }

        internal static bool IsAssemblyServiceable(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
            {
                return false;
            }

            using (var stream = File.OpenRead(assemblyPath))
            using (var peReader = new PEReader(stream))
            {
                var mdReader = peReader.GetMetadataReader();
                var attrs = mdReader.GetAssemblyDefinition().GetCustomAttributes()
                    .Select(ah => mdReader.GetCustomAttribute(ah));

                foreach (var attr in attrs)
                {
                    var ctorHandle = attr.Constructor;
                    if (ctorHandle.Kind != HandleKind.MemberReference)
                    {
                        continue;
                    }

                    var container = mdReader.GetMemberReference((MemberReferenceHandle)ctorHandle).Parent;
                    var name = mdReader.GetTypeReference((TypeReferenceHandle)container).Name;
                    if (!string.Equals(mdReader.GetString(name), "AssemblyMetadataAttribute"))
                    {
                        continue;
                    }

                    var arguments = GetFixedStringArguments(mdReader, attr);
                    if (arguments.Count == 2 &&
                        string.Equals(arguments[0], "Serviceable", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(arguments[1], "True", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the fixed (required) string arguments of a custom attribute.
        /// Only attributes that have only fixed string arguments.
        /// </summary>
        private static List<string> GetFixedStringArguments(MetadataReader reader, CustomAttribute attribute)
        {
            // TODO: Nick Guerrera (Nick.Guerrera@microsoft.com) hacked this method for temporary use.
            // There is a blob decoder feature in progress but it won't ship in time for our milestone.
            // Replace this method with the blob decoder feature when later it is availale.

            var signature = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Signature;
            var signatureReader = reader.GetBlobReader(signature);
            var valueReader = reader.GetBlobReader(attribute.Value);
            var arguments = new List<string>();

            var prolog = valueReader.ReadUInt16();
            if (prolog != 1)
            {
                // Invalid custom attribute prolog
                return arguments;
            }

            var header = signatureReader.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method || header.IsGeneric)
            {
                // Invalid custom attribute constructor signature
                return arguments;
            }

            int parameterCount;
            if (!signatureReader.TryReadCompressedInteger(out parameterCount))
            {
                // Invalid custom attribute constructor signature
                return arguments;
            }

            var returnType = signatureReader.ReadSignatureTypeCode();
            if (returnType != SignatureTypeCode.Void)
            {
                // Invalid custom attribute constructor signature
                return arguments;
            }

            for (int i = 0; i < parameterCount; i++)
            {
                var signatureTypeCode = signatureReader.ReadSignatureTypeCode();
                if (signatureTypeCode == SignatureTypeCode.String)
                {
                    // Custom attribute constructor must take only strings
                    arguments.Add(valueReader.ReadSerializedString());
                }
            }

            return arguments;
        }

        public class PropertyDefinitions
        {
            public PropertyDefinitions()
            {
                Definitions = new Dictionary<string, ContentPropertyDefinition>
                {
                    { "language", _language },
                    { "tfm", _targetFramework },
                    { "rid", _rid },
                    { "assembly", _assembly },
                    { "dynamicLibrary", _dynamicLibrary },
                    { "resources", _resources },
                    { "locale", _locale },
                    { "any", _any },
                };
            }

            public IDictionary<string, ContentPropertyDefinition> Definitions { get; }

            ContentPropertyDefinition _language = new ContentPropertyDefinition
            {
                Table =
                {
                    { "cs", "CSharp" },
                    { "vb", "Visual Basic" },
                    { "fs", "FSharp" },
                }
            };

            ContentPropertyDefinition _targetFramework = new ContentPropertyDefinition
            {
                Table =
                {
                    { "any", new FrameworkName("Core", new Version(5, 0)) }
                },
                Parser = TargetFrameworkName_Parser,
                OnIsCriteriaSatisfied = TargetFrameworkName_IsCriteriaSatisfied
            };

            ContentPropertyDefinition _rid = new ContentPropertyDefinition
            {
                Parser = name => name
            };

            ContentPropertyDefinition _assembly = new ContentPropertyDefinition
            {
                FileExtensions = { ".dll" }
            };

            ContentPropertyDefinition _dynamicLibrary = new ContentPropertyDefinition
            {
                FileExtensions = { ".dll", ".dylib", ".so" }
            };

            ContentPropertyDefinition _resources = new ContentPropertyDefinition
            {
                FileExtensions = { ".resources.dll" }
            };

            ContentPropertyDefinition _locale = new ContentPropertyDefinition
            {
                Parser = Locale_Parser,
            };

            ContentPropertyDefinition _any = new ContentPropertyDefinition
            {
                Parser = name => name
            };


            internal static object Locale_Parser(string name)
            {
                if (name.Length == 2)
                {
                    return name;
                }
                else if (name.Length >= 4 && name[2] == '-')
                {
                    return name;
                }

                return null;
            }

            internal static object TargetFrameworkName_Parser(string name)
            {
                if (name.Contains('.') || name.Contains('/'))
                {
                    return null;
                }

                if (name == "contract")
                {
                    return null;
                }

                var result = VersionUtility.ParseFrameworkName(name);

                if (result != VersionUtility.UnsupportedFrameworkName)
                {
                    return result;
                }

                return new FrameworkName(name, new Version(0, 0));
            }

            internal static bool TargetFrameworkName_IsCriteriaSatisfied(object criteria, object available)
            {
                var criteriaFrameworkName = criteria as FrameworkName;
                var availableFrameworkName = available as FrameworkName;

                if (criteriaFrameworkName != null && availableFrameworkName != null)
                {
                    return VersionUtility.IsCompatible(criteriaFrameworkName, availableFrameworkName);
                }

                return false;
            }

            internal static bool TargetPlatformName_IsCriteriaSatisfied(object criteria, object available)
            {
                var criteriaFrameworkName = criteria as FrameworkName;
                var availableFrameworkName = available as FrameworkName;

                if (criteriaFrameworkName != null && availableFrameworkName != null)
                {
                    if (!String.Equals(criteriaFrameworkName.Identifier, availableFrameworkName.Identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (NormalizeVersion(criteriaFrameworkName.Version) < NormalizeVersion(availableFrameworkName.Version))
                    {
                        return false;
                    }

                    return true;
                }
                return false;
            }

            internal static Version NormalizeVersion(Version version)
            {
                return new Version(version.Major,
                                   version.Minor,
                                   Math.Max(version.Build, 0),
                                   Math.Max(version.Revision, 0));
            }
        }

        public class NetPortableProfileWithToString
        {
            public NetPortableProfileWithToString(NetPortableProfile profile)
            {
                Profile = profile;
            }
            public NetPortableProfile Profile { get; }
            public override string ToString()
            {
                return "portable-" + Profile.CustomProfileString;
            }
            public override int GetHashCode()
            {
                return Profile.CustomProfileString?.GetHashCode() ?? 0;
            }
            public override bool Equals(object obj)
            {
                return Profile.CustomProfileString.Equals((obj as NetPortableProfileWithToString)?.Profile?.CustomProfileString);
            }
        }

        public class PatternDefinitions
        {
            public PropertyDefinitions Properties { get; }

            public ContentPatternDefinition CompileTimeAssemblies { get; }
            public ContentPatternDefinition ManagedAssemblies { get; }
            public ContentPatternDefinition NativeLibraries { get; }

            public PatternDefinitions()
            {
                Properties = new PropertyDefinitions();

                ManagedAssemblies = new ContentPatternDefinition
                {
                    GroupPatterns =
                    {
                        "runtimes/{rid}/lib/{tfm}/{any?}",
                        "lib/{tfm}/{any?}",
                    },
                    PathPatterns =
                    {
                        "runtimes/{rid}/lib/{tfm}/{assembly}",
                        "lib/{tfm}/{assembly}",
                    },
                    PropertyDefinitions = Properties.Definitions,
                };

                CompileTimeAssemblies = new ContentPatternDefinition
                {
                    GroupPatterns =
                    {
                        "ref/{tfm}/{any?}",
                    },
                    PathPatterns =
                    {
                        "ref/{tfm}/{assembly}",
                    },
                    PropertyDefinitions = Properties.Definitions,
                };

                NativeLibraries = new ContentPatternDefinition
                {
                    GroupPatterns =
                    {
                        "runtimes/{rid}/native/{any?}",
                        "native/{any?}",
                    },
                    PathPatterns =
                    {
                        "runtimes/{rid}/native/{any}",
                        "native/{any}",
                    },
                    PropertyDefinitions = Properties.Definitions,
                };
            }
        }

        private class SelectionCriteriaBuilder
        {
            private IDictionary<string, ContentPropertyDefinition> propertyDefinitions;

            public SelectionCriteriaBuilder(IDictionary<string, ContentPropertyDefinition> propertyDefinitions)
            {
                this.propertyDefinitions = propertyDefinitions;
            }

            public virtual SelectionCriteria Criteria { get; } = new SelectionCriteria();

            internal virtual SelectionCriteriaEntryBuilder Add
            {
                get
                {
                    var entry = new SelectionCriteriaEntry();
                    Criteria.Entries.Add(entry);
                    return new SelectionCriteriaEntryBuilder(this, entry);
                }
            }

            internal class SelectionCriteriaEntryBuilder : SelectionCriteriaBuilder
            {
                public SelectionCriteriaEntry Entry { get; }
                public SelectionCriteriaBuilder Builder { get; }

                public SelectionCriteriaEntryBuilder(SelectionCriteriaBuilder builder, SelectionCriteriaEntry entry) : base(builder.propertyDefinitions)
                {
                    Builder = builder;
                    Entry = entry;
                }
                public SelectionCriteriaEntryBuilder this[string key, string value]
                {
                    get
                    {
                        ContentPropertyDefinition propertyDefinition;
                        if (!propertyDefinitions.TryGetValue(key, out propertyDefinition))
                        {
                            throw new Exception("Undefined property used for criteria");
                        }
                        if (value == null)
                        {
                            Entry.Properties[key] = null;
                        }
                        else
                        {
                            object valueLookup;
                            if (propertyDefinition.TryLookup(value, out valueLookup))
                            {
                                Entry.Properties[key] = valueLookup;
                            }
                            else
                            {
                                throw new Exception("Undefined value used for criteria");
                            }
                        }
                        return this;
                    }
                }
                public SelectionCriteriaEntryBuilder this[string key, object value]
                {
                    get
                    {
                        ContentPropertyDefinition propertyDefinition;
                        if (!propertyDefinitions.TryGetValue(key, out propertyDefinition))
                        {
                            throw new Exception("Undefined property used for criteria");
                        }
                        Entry.Properties[key] = value;
                        return this;
                    }
                }
                internal override SelectionCriteriaEntryBuilder Add
                {
                    get
                    {
                        return Builder.Add;
                    }
                }
                public override SelectionCriteria Criteria
                {
                    get
                    {
                        return Builder.Criteria;
                    }
                }
            }
        }
    }
}