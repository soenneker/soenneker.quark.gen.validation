using Soenneker.Extensions.Enumerable.String;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Soenneker.Quark.Gen.Validation.BuildTasks;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class ValidationParityTests
{
    private const string Contract = """
namespace Soenneker.Quark {
public interface IGeneratedQuarkValidation { void ValidateField(string memberName, System.Collections.Generic.ICollection<System.ComponentModel.DataAnnotations.ValidationResult> results); }
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
public sealed class GenerateQuarkValidationAttribute : System.Attribute { }
}
""";
    private static CSharpCompilation Compile(string source) => CSharpCompilation.Create("ValidationParity" + Guid.NewGuid().ToString("N"),
        new[] { CSharpSyntaxTree.ParseText(source + Contract, new CSharpParseOptions(LanguageVersion.Preview)) },
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Test]
    public void Generated_fields_match_data_annotations()
    {
        const string source = """
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Soenneker.Quark;
public static class Resources { public static string Label => "Localized label"; public static string Required => "{0} must be supplied"; }
public partial class BaseModel { [Required] public virtual string Inherited { get; set; } = ""; }
public sealed partial class Model : BaseModel, IValidatableObject {
 [Display(Name = nameof(Resources.Label), ResourceType = typeof(Resources))]
 [Required(ErrorMessageResourceType = typeof(Resources), ErrorMessageResourceName = nameof(Resources.Required))]
 [StringLength(6, MinimumLength = 3)] public string Name { get; set; } = "";
 [Range(1, 5)] public int Count { get; set; } = 8;
 [Range(typeof(decimal), "1.25", "3.75")] public decimal Amount { get; set; } = 4;
 [Range(typeof(decimal), "1.25", "3.75", MinimumIsExclusive = true, ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public string AmountText { get; set; } = "1.25";
 [Range(typeof(System.DateTime), "2020-01-01", "2021-01-01", ParseLimitsInInvariantCulture = true)] public System.DateTime Date { get; set; } = new(2022, 1, 1);
 [EmailAddress] public string Email { get; set; } = "invalid";
 [RegularExpression("^[A-Z]+$")] public string Code { get; set; } = "invalid";
 [MinLength(2), MaxLength(4)] public string[] Items { get; set; } = [];
 [Compare(nameof(Name))] public string Confirmation { get; set; } = "different";
 [CustomValidation(typeof(Model), nameof(Check))] public string Custom { get; set; } = "bad";
 [CustomValidation(typeof(Model), nameof(CheckNumber))] public string Convertible { get; set; } = "2";
 [CustomValidation(typeof(Model), nameof(Check)), CustomValidation(typeof(Model), nameof(CheckOther))] public string Multiple { get; set; } = "bad";
 [Required, Display(Name = "")] public string EmptyDisplay { get; set; } = "";
 [Odd] public int OddValue { get; set; } = 2;
 public override string Inherited { get; set; } = "";
 public string NoAttributes { get; set; } = "";
 public static ValidationResult? Check(string value, ValidationContext context) => value == "good" ? null : new ValidationResult(context.MemberName + " failed", new[] { "Custom" });
 public static ValidationResult? CheckNumber(int value) => value == 1 ? null : new ValidationResult(null);
 public static ValidationResult? CheckOther(object value) => new ValidationResult("second failure");
 public IEnumerable<ValidationResult> Validate(ValidationContext _) => throw new Exception("Object validation must not run");
}
public sealed class OddAttribute : ValidationAttribute { public override bool IsValid(object? value) => value is int number && number % 2 == 1; }
public static class Scenario {
 public static void Run() {
  var model = new Model();
  foreach (var name in new[] { "", "a", "abc", "toolongvalue" }) {
   model.Name = name;
   foreach (var property in typeof(Model).GetProperties()) {
    var expected = new List<ValidationResult>();
    Validator.TryValidateProperty(property.GetValue(model), new ValidationContext(model) { MemberName = property.Name }, expected);
    var actual = new List<ValidationResult>();
    ((IGeneratedQuarkValidation)model).ValidateField(property.Name, actual);
    string Format(IEnumerable<ValidationResult> results) => string.Join("|", results.Select(r => r.ErrorMessage + ":" + string.Join(",", r.MemberNames)));
    if (Format(actual) != Format(expected)) throw new Exception(property.Name + " expected " + Format(expected) + " got " + Format(actual));
   }
  }
 }
}
""";
        Run(source);
    }

    [Test]
    public void Nested_private_generic_record_and_unannotated_models_work()
    {
        Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Soenneker.Quark;
public partial class Scenario {
 private sealed partial record Model<T> where T : class { [Required] public T? Value { get; set; } }
 [GenerateQuarkValidation] private partial struct Plain { public int Value { get; set; } }
 public static void Run() {
  IGeneratedQuarkValidation model = new Model<string>();
  var results = new List<ValidationResult>(); model.ValidateField("Value", results);
  if (results.Count != 1) throw new Exception("Required did not run");
  results.Clear(); ((IGeneratedQuarkValidation)new Plain()).ValidateField("Value", results);
  if (results.Count != 0) throw new Exception("Unannotated field failed");
  try { model.ValidateField("Missing", results); throw new Exception("Unknown field accepted"); } catch (ArgumentException) { }
 }
}
""");
    }

    [Test]
    public void Nonpartial_models_fail_generation_instead_of_silently_skipping()
    {
        try { ValidationEmitter.Emit(Compile("public class Model { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } }")); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("partial")) { return; }
        throw new Exception("Expected actionable diagnostic");
    }

    [Test]
    public void Generated_output_is_deterministic()
    {
        // Emitter output must be stable independent of process-specific identities.
        var compilation = Compile("public partial class Model { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } }");
        if (ValidationEmitter.Emit(compilation) != ValidationEmitter.Emit(compilation)) throw new Exception("Nondeterministic output");
    }

    private static void Run(string source)
    {
        var compilation = Compile(source);
        var generated = ValidationEmitter.Emit(compilation);
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, new CSharpParseOptions(LanguageVersion.Preview)));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new Exception(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToSeparatedString('\n') + "\n" + generated);
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        try { assembly.GetType("Scenario")!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException exception) { throw exception.InnerException!; }
    }
}
