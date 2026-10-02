namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class ValidationOptimizationTests
{
    [Test]
    public void Mutable_cultures_and_localized_default_messages_are_not_frozen()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Soenneker.Quark;
public partial class Model {
 [Range(typeof(decimal), "1,5", "3,5")] public decimal Amount { get; set; } = 2;
 [Range(typeof(decimal), "broken", "9")] public decimal? OptionalInvalid { get; set; }
 [Required] public string Name { get; set; } = "";
 [StringLength(1)] public string Length { get; set; } = "long";
}
public static class Scenario {
 public static void Run() {
  var model = new Model(); var generated = (IGeneratedQuarkValidation)model;
  var results = new List<ValidationResult>();
  var previous = CultureInfo.CurrentCulture; var previousUI = CultureInfo.CurrentUICulture;
  try {
   var mutable = new CultureInfo("en-US"); CultureInfo.CurrentCulture = mutable;
   foreach (var separator in new[] { ",", ".", "," }) {
    mutable.NumberFormat.NumberDecimalSeparator = separator;
    mutable.NumberFormat.NumberGroupSeparator = separator == "." ? "," : ".";
    results.Clear(); Exception? actualError = null, expectedError = null; bool expected = false;
    try { generated.ValidateField("Amount", results); } catch (Exception e) { actualError = e; }
    try { expected = new RangeAttribute(typeof(decimal), "1,5", "3,5").IsValid(model.Amount); } catch (Exception e) { expectedError = e; }
    if (actualError?.GetType() != expectedError?.GetType() || (actualError is null && (results.Count == 0) != expected)) throw new Exception("Mutable culture limits were cached");
   }
   Exception? actualInvalid = null, expectedInvalid = null;
   try { generated.ValidateField("OptionalInvalid", results); } catch (Exception e) { actualInvalid = e; }
   try { new RangeAttribute(typeof(decimal), "broken", "9").IsValid(null); } catch (Exception e) { expectedInvalid = e; }
   if (actualInvalid is null || actualInvalid.GetType() != expectedInvalid?.GetType()) throw new Exception("Null value bypassed invalid range limits");
   foreach (var language in new[] { "en-US", "fr-FR", "tr-TR", "en-US" }) {
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
    results.Clear(); generated.ValidateField("Name", results); generated.ValidateField("Length", results);
    if (results[0].ErrorMessage != new RequiredAttribute().FormatErrorMessage("Name") ||
        results[1].ErrorMessage != new StringLengthAttribute(1).FormatErrorMessage("Length")) throw new Exception("Default message culture was cached");
   }
  } finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUI; }
 }
}
""");
    }

    [Test]
    public void Candidate_pruning_keeps_partial_inherited_and_record_properties()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Soenneker.Quark;
public class Unrelated { public string Name { get; set; } = ""; }
public partial class Split { public string Plain { get; set; } = ""; }
public partial class Split { [Required] public string Value { get; set; } = ""; }
public partial class Parent { [Required] public string Value { get; set; } = ""; }
public partial class Child : Parent { }
public partial record Positional([property: Required] string Value);
public static class Scenario {
 public static void Run() {
  foreach (var model in new IGeneratedQuarkValidation[] { new Split(), new Child(), new Positional("") }) {
   var results = new List<ValidationResult>(); model.ValidateField("Value", results);
   if (results.Count != 1) throw new Exception("Candidate pruning missed " + model.GetType().Name);
  }
 }
}
""");
    }

    [Test]
    public void Fast_paths_preserve_boundaries_nulls_exceptions_and_cultures()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Soenneker.Quark;
public partial class Model {
 [Required, Range(1, 5, MinimumIsExclusive = true)] public int? Count { get; set; }
 [Range(1.0, 5.0, MaximumIsExclusive = true)] public double? Floating { get; set; }
 [Range(double.NaN, 5.0)] public double Special { get; set; }
 [Range(typeof(decimal), "1.25", "3.75", ParseLimitsInInvariantCulture = true, MinimumIsExclusive = true)] public decimal? Amount { get; set; }
 [Range(typeof(decimal), "1,5", "3,5")] public decimal CultureAmount { get; set; } = 2;
 [Range(typeof(string), "b", "d")] public string? TextRange { get; set; }
 [Required, StringLength(4, MinimumLength = 2), MinLength(2), MaxLength(4)] public string? Text { get; set; }
 [MinLength(1), MaxLength(3)] public int[]? Items { get; set; }
 [StringLength(-1)] public string InvalidLength { get; set; } = "";
 [Range(2, 1)] public int InvalidRange { get; set; }
}
public static class Scenario {
 public static void Run() {
  var model = new Model();
  void Check(string name, object? value) {
   var property = typeof(Model).GetProperty(name)!;
   property.SetValue(model, value);
   var expected = new List<ValidationResult>(); var actual = new List<ValidationResult>();
   Exception? expectedError = null, actualError = null;
   try {
    foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>().OrderBy(a => a is RequiredAttribute ? 0 : 1)) {
     var result = attribute.GetValidationResult(value, new ValidationContext(model) { MemberName = name });
     if (result is not null) { expected.Add(result); if (attribute is RequiredAttribute) break; }
    }
   } catch (Exception e) { expectedError = e; }
   try { ((IGeneratedQuarkValidation)model).ValidateField(name, actual); } catch (Exception e) { actualError = e; }
   string Format(List<ValidationResult> values) => string.Join("|", values.Select(r => r.ErrorMessage + ":" + string.Join(",", r.MemberNames)));
   if (expectedError?.GetType() != actualError?.GetType() || Format(expected) != Format(actual))
    throw new Exception(name + " value " + value + " expected " + Format(expected) + " / " + expectedError + " got " + Format(actual) + " / " + actualError);
  }
  var culture = CultureInfo.CurrentCulture;
  try {
   foreach (var name in new[] { "en-US", "fr-FR", "en-US" }) {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
    foreach (var value in new int?[] { null, 0, 1, 2, 5, 6 }) Check("Count", value);
    foreach (var value in new double?[] { null, double.NaN, double.NegativeInfinity, 0, 1, 4.99, 5, double.PositiveInfinity }) Check("Floating", value);
    foreach (var value in new[] { double.NaN, 0, 6 }) Check("Special", value);
    foreach (var value in new decimal?[] { null, 0, 1.25m, 2, 3.75m, 4 }) Check("Amount", value);
    Check("CultureAmount", 2m);
    foreach (var value in new string?[] { null, "", " ", "a", "b", "c", "d", "longer" }) { Check("Text", value); Check("TextRange", value); }
    foreach (var value in new int[]?[] { null, [], [1], [1,2,3], [1,2,3,4] }) Check("Items", value);
    Check("InvalidLength", "valid"); Check("InvalidRange", 0);
   }
  } finally { CultureInfo.CurrentCulture = culture; }
 }
}
""");
    }

    [Test]
    public void Numeric_and_typed_range_success_paths_do_not_box()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading.Tasks;
using Soenneker.Quark;
public partial class Model {
 [Required, Range(1, 10)] public int Number { get; set; } = 5;
 [Range(1.0, 10.0)] public double? Floating { get; set; } = 5;
 [Range(typeof(decimal), "1.25", "9.75", ParseLimitsInInvariantCulture = true)] public decimal? Amount { get; set; } = 5;
 [Compare(nameof(Number))] public int Copy { get; set; } = 5;
}
public static class Scenario {
 public static void Run() {
  var model = (IGeneratedQuarkValidation)new Model();
  Parallel.For(0, 1000, i => {
   var results = new List<ValidationResult>(); model.ValidateField("Amount", results);
   if (results.Count != 0) throw new Exception("Concurrent typed range initialization failed");
  });
  var results = new List<ValidationResult>();
  for (int i = 0; i < 1000; i++) { model.ValidateField("Number", results); model.ValidateField("Floating", results); model.ValidateField("Amount", results); model.ValidateField("Copy", results); }
  var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
  for (int i = 0; i < 10000; i++) { model.ValidateField("Number", results); model.ValidateField("Floating", results); model.ValidateField("Amount", results); model.ValidateField("Copy", results); }
  var elapsed = Stopwatch.GetElapsedTime(start); var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
  Console.WriteLine($"Numeric validation (40000 calls): {allocated} bytes / {elapsed.TotalMilliseconds:F2} ms");
  if (results.Count != 0 || allocated > 1024) throw new Exception("Numeric validation allocated " + allocated);
 }
}
""");
    }

    [Test]
    public void Framework_failures_preserve_custom_context_mutations()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Soenneker.Quark;
public sealed class MutateAttribute : ValidationAttribute {
 protected override ValidationResult? IsValid(object? value, ValidationContext context) { context.DisplayName = "Changed"; context.MemberName = "Other"; return null; }
}
public partial class Model { [Mutate, StringLength(1)] public string Value { get; set; } = "invalid"; }
public static class Scenario {
 public static void Run() {
  var results = new List<ValidationResult>();
  ((IGeneratedQuarkValidation)new Model()).ValidateField("Value", results);
  if (results.Count != 1 || !results[0].ErrorMessage!.Contains("Changed") || results[0].MemberNames.Single() != "Other") throw new Exception("Context mutation was lost");
 }
}
""");
    }
}
