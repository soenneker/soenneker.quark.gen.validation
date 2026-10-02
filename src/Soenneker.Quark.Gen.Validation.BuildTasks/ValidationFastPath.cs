using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class ValidationFastPath
{
    internal static bool CanCompareWithoutBoxing(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return CanCompareWithoutBoxing(nullable.TypeArguments[0]);
        if (type.TypeKind == TypeKind.Enum) return true;
        return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Decimal or SpecialType.System_Single or
            SpecialType.System_Double or SpecialType.System_String or SpecialType.System_DateTime or SpecialType.System_IntPtr or SpecialType.System_UIntPtr ||
            (type.ContainingNamespace.ToDisplayString() == "System" && type.Name is "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid");
    }

    internal static string? Get(AttributeData attribute, string name, ITypeSymbol propertyType)
    {
        const string annotations = "System.ComponentModel.DataAnnotations.";
        bool nullable = propertyType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        ITypeSymbol type = nullable ? ((INamedTypeSymbol)propertyType).TypeArguments[0] : propertyType;
        string value = nullable ? "value.GetValueOrDefault()" : "value";
        if (name == annotations + "RequiredAttribute")
        {
            if (type.IsValueType) return nullable ? "value.HasValue" : "true";
            if (type.SpecialType == SpecialType.System_String)
                return Flag(attribute, "AllowEmptyStrings") ? "value is not null" : "!global::System.String.IsNullOrWhiteSpace(value)";
            // Object/interface properties can contain strings and require the framework's runtime check.
            return null;
        }

        bool length = type.SpecialType == SpecialType.System_String || type is IArrayTypeSymbol;
        if (length && attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Primitive, Value: int limit }])
        {
            if (name == annotations + "StringLengthAttribute" && type.SpecialType == SpecialType.System_String)
            {
                var minimum = 0;
                foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
                    if (argument.Key == "MinimumLength" && argument.Value.Value is int min) minimum = min;
                if (minimum >= 0 && limit >= minimum)
                    return "value is null || (value.Length >= " + Number(minimum) + " && value.Length <= " + Number(limit) + ")";
            }
            if (name == annotations + "MinLengthAttribute" && limit >= 0)
                return "value is null || value.Length >= " + Number(limit);
            if (name == annotations + "MaxLengthAttribute" && limit >= -1)
                return limit == -1 ? "true" : "value is null || value.Length <= " + Number(limit);
        }

        if (name != annotations + "RangeAttribute" || attribute.ConstructorArguments.Length != 2) return null;
        TypedConstant lower = attribute.ConstructorArguments[0];
        TypedConstant upper = attribute.ConstructorArguments[1];
        bool minimumExclusive = Flag(attribute, "MinimumIsExclusive");
        bool maximumExclusive = Flag(attribute, "MaximumIsExclusive");
        if (type.SpecialType == SpecialType.System_Int32 && lower.Value is int minInt && upper.Value is int maxInt)
        {
            if (minInt > maxInt || (minInt == maxInt && (minimumExclusive || maximumExclusive))) return null;
        }
        else if (type.SpecialType == SpecialType.System_Double && lower.Value is double minDouble && upper.Value is double maxDouble)
        {
            if (!double.IsFinite(minDouble) || !double.IsFinite(maxDouble) || minDouble > maxDouble ||
                (minDouble == maxDouble && (minimumExclusive || maximumExclusive))) return null;
        }
        else return null;

        return (nullable ? "!value.HasValue || (" : "(") + value + (minimumExclusive ? " > " : " >= ") + lower.ToCSharpString() +
            " && " + value + (maximumExclusive ? " < " : " <= ") + upper.ToCSharpString() + ")";
    }

    private static bool Flag(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            if (argument.Key == name) return argument.Value.Value is true;
        return false;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
