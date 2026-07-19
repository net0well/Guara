// Polyfills para compilar records com init/required em netstandard2.0
// (o generator roda dentro do compilador; estes tipos nunca chegam ao consumidor).
#pragma warning disable SA1403, SA1649
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
    {
        public string FeatureName { get; } = featureName;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute;
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute;
}
#pragma warning restore SA1403, SA1649
