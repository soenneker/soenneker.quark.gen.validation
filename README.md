[![](https://img.shields.io/nuget/v/soenneker.quark.gen.validation.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.quark.gen.validation/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.quark.gen.validation/build-and-test.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.quark.gen.validation/actions/workflows/build-and-test.yml)
[![](https://img.shields.io/nuget/dt/soenneker.quark.gen.validation.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.quark.gen.validation/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.quark.gen.validation/codeql.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.quark.gen.validation/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Quark.Gen.Validation
### External build-time validation code generation for AOT-compatible Quark applications.

## Installation

```
dotnet add package Soenneker.Quark.Gen.Validation
```

MSBuild runs the external .NET executable before compilation. It discovers property validation attributes in C# and Razor models and emits implementations of Quark's `IGeneratedQuarkValidation`. No Roslyn analyzer or compiler dependency is added to the application runtime.

Models and their containing types must be `partial`. Models with validation attributes are discovered automatically; use `[GenerateQuarkValidation]` for models without attributes. Reference the generator in the project defining each model. Quark Suite forwards the build dependency to consumers.

```csharp
public sealed partial class Registration
{
    [Required]
    public string? Password { get; set; }

    [QuarkCompare(nameof(Password))]
    public string? Confirmation { get; set; }

    [QuarkRange(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true)]
    public decimal Amount { get; set; }
}
```

Generated validation reads the current model property and preserves required-first evaluation, display names, resource messages, inherited attributes, and custom validation attributes. It validates fields, not whole objects: model attributes and `IValidatableObject` are not invoked, matching Quark Suite. Delegates, async validators, pattern validation, and component state remain owned by Suite.

Standard `Compare` and typed `Range` are supported, but .NET annotates their constructors as trimming-unsafe, including their original attribute declarations. `QuarkCompare` and `QuarkRange` avoid those declarations for strict AOT builds. They are generator-only attributes, not inputs to the framework's reflection-based `Validator`. Custom attributes, validation methods, and converters must themselves be AOT-compatible. Runtime `TypeDescriptor` provider registration cannot change generated metadata.

Set `ValidationBuildEnabled=false` to disable generation. `ValidationBuildTasksDllPath` can point to a locally built executable assembly. Output lives under `obj/<configuration>/<framework>/QuarkValidation`; unchanged generated files are not rewritten. Referenced model assemblies must generate their own validators, or register a typed adapter through `GeneratedValidationRegistry.Register<T>`.
