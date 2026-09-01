using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BinDiff.Core.Model;

namespace BinDiff.Core.Analyzers;

/// <summary>Compares managed metadata directly from PE bytes without loading either assembly.</summary>
public sealed class DotNetAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.DotNet;

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        var section = new DotNetSection();
        try
        {
            var limit = Math.Clamp(options.MaxMetadataItems, 1, 1_000_000);
            var reportLimit = Math.Clamp(options.MaxReportedMetadataItems, 0, 100_000);
            section.A = ReadProfile(a.Data, limit);
            section.B = ReadProfile(b.Data, limit);
            section.Applicable = section.A is not null || section.B is not null;
            if (!section.Applicable) return section;
            if (section.A is null || section.B is null)
            {
                section.SimilarityPercent = 0;
                PopulateComparisons(section, reportLimit);
                return section;
            }

            section.SimilarityPercent = Score(section.A, section.B);
            PopulateComparisons(section, reportLimit);
            return section;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            return new DotNetSection { Error = ex.Message };
        }
    }

    private static DotNetProfile? ReadProfile(byte[] data, int limit)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) return null;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) return null;
        var assembly = reader.GetAssemblyDefinition();
        var profile = new DotNetProfile
        {
            AssemblyName = reader.GetString(assembly.Name),
            AssemblyVersion = assembly.Version.ToString(),
            TargetFramework = ReadTargetFramework(reader, assembly)
        };

        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            Add(profile.AssemblyReferences,
                $"{reader.GetString(reference.Name)}, Version={reference.Version}", limit, profile);
        }

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var typeName = QualifiedName(reader.GetString(type.Namespace), reader.GetString(type.Name));
            if (typeName == "<Module>") continue;
            Add(profile.Types, typeName, limit, profile);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var parameterCount = method.GetParameters()
                    .Count(parameter => reader.GetParameter(parameter).SequenceNumber > 0);
                var genericCount = method.GetGenericParameters().Count;
                var methodName = $"{typeName}::{reader.GetString(method.Name)}(params {parameterCount}, generic {genericCount})";
                Add(profile.Methods, methodName, limit, profile);
                if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
                var import = method.GetImport();
                var module = reader.GetModuleReference(import.Module);
                Add(profile.PInvokes,
                    $"{reader.GetString(module.Name)}!{reader.GetString(import.Name)} ({methodName})", limit, profile);
            }
        }

        SortDistinct(profile.AssemblyReferences);
        SortDistinct(profile.Types);
        SortDistinct(profile.Methods);
        SortDistinct(profile.PInvokes);
        return profile;
    }

    private static string ReadTargetFramework(MetadataReader reader, AssemblyDefinition assembly)
    {
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (ConstructorTypeName(reader, attribute.Constructor) !=
                "System.Runtime.Versioning.TargetFrameworkAttribute") continue;
            var value = reader.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1) return "";
            return value.ReadSerializedString() ?? "";
        }
        return "";
    }

    private static string ConstructorTypeName(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle parent = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        return parent.Kind switch
        {
            HandleKind.TypeReference => TypeName(reader, reader.GetTypeReference((TypeReferenceHandle)parent)),
            HandleKind.TypeDefinition => TypeName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)parent)),
            _ => ""
        };
    }

    private static string TypeName(MetadataReader reader, TypeReference type) =>
        QualifiedName(reader.GetString(type.Namespace), reader.GetString(type.Name));

    private static string TypeName(MetadataReader reader, TypeDefinition type) =>
        QualifiedName(reader.GetString(type.Namespace), reader.GetString(type.Name));

    private static string QualifiedName(string ns, string name) => string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

    private static void Add(List<string> values, string value, int limit, DotNetProfile profile)
    {
        if (values.Count < limit) values.Add(value);
        else profile.Truncated = true;
    }

    private static void SortDistinct(List<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        values.Clear();
        values.AddRange(distinct);
    }

    private static double Score(DotNetProfile a, DotNetProfile b)
    {
        var identity = string.Equals(a.AssemblyName, b.AssemblyName, StringComparison.Ordinal) ? 1.0 : 0.0;
        var score = 100 * (0.1 * identity +
                           0.2 * Jaccard(a.AssemblyReferences, b.AssemblyReferences) +
                           0.3 * Jaccard(a.Types, b.Types) +
                           0.3 * Jaccard(a.Methods, b.Methods) +
                           0.1 * Jaccard(a.PInvokes, b.PInvokes));
        return Math.Clamp(score, 0, 100);
    }

    private static double Jaccard(IEnumerable<string> a, IEnumerable<string> b)
    {
        var left = a.ToHashSet(StringComparer.Ordinal);
        var right = b.ToHashSet(StringComparer.Ordinal);
        var union = left.Union(right).Count();
        return union == 0 ? 1.0 : (double)left.Intersect(right).Count() / union;
    }

    private static void PopulateComparisons(DotNetSection section, int limit)
    {
        Compare(section.A?.AssemblyReferences, section.B?.AssemblyReferences, limit,
            section.ReferencesCommon, section.ReferencesOnlyA, section.ReferencesOnlyB);
        Compare(section.A?.Types, section.B?.Types, limit,
            section.TypesCommon, section.TypesOnlyA, section.TypesOnlyB);
        Compare(section.A?.Methods, section.B?.Methods, limit,
            section.MethodsCommon, section.MethodsOnlyA, section.MethodsOnlyB);
        Compare(section.A?.PInvokes, section.B?.PInvokes, limit,
            section.PInvokesCommon, section.PInvokesOnlyA, section.PInvokesOnlyB);
    }

    private static void Compare(
        IEnumerable<string>? a, IEnumerable<string>? b, int limit,
        List<string> common, List<string> onlyA, List<string> onlyB)
    {
        var left = (a ?? []).ToHashSet(StringComparer.Ordinal);
        var right = (b ?? []).ToHashSet(StringComparer.Ordinal);
        common.AddRange(left.Intersect(right).Order(StringComparer.Ordinal).Take(limit));
        onlyA.AddRange(left.Except(right).Order(StringComparer.Ordinal).Take(limit));
        onlyB.AddRange(right.Except(left).Order(StringComparer.Ordinal).Take(limit));
    }
}
