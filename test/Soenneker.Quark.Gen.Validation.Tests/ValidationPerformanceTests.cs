using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Soenneker.Quark.Gen.Validation.BuildTasks;
using Soenneker.Utils.PooledStringBuilders;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class ValidationPerformanceTests
{
    [Test]
    public void Measure_generation_for_many_models()
    {
        var source = new PooledStringBuilder(32768);
        string text;
        try
        {
            source.AppendLine("using System.ComponentModel.DataAnnotations;");
            for (var i = 0; i < 200; i++)
            {
                source.Append("public partial class Model"); source.Append(i);
                source.AppendLine(" { [Required, StringLength(20)] public string Name { get; set; } = \"ok\"; [Range(1, 10)] public int Count { get; set; } = 5; [Compare(nameof(Name))] public string Copy { get; set; } = \"ok\"; }");
            }
            text = source.ToString();
        }
        finally { source.Dispose(); }
        CSharpCompilation compilation = ValidationParityTests.Compile(text);
        string expected = ValidationEmitter.Emit(compilation);
        for (var i = 0; i < 3; i++) ValidationEmitter.Emit(compilation);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 20; i++)
            if (ValidationEmitter.Emit(compilation) != expected) throw new Exception("Output changed");
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"Emitter (20 x 200 models): {bytes} bytes / {elapsed.TotalMilliseconds:F2} ms; output {expected.Length} chars");
    }

    [Test]
    public void Valid_fields_eliminate_steady_state_allocations()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Soenneker.Quark;
public partial class Model {
 [Required, StringLength(20), RegularExpression("^[A-Z]+$")]
 public string Name { get; set; } = "VALID";
 public void Previous(ICollection<ValidationResult> results) {
  var context = new ValidationContext(this, "Name", null, null) { MemberName = "Name" };
  var required = new RequiredAttribute().GetValidationResult(Name, context);
  if (required is not null) { results.Add(required); return; }
  var length = new StringLengthAttribute(20).GetValidationResult(Name, context);
  if (length is not null) results.Add(length);
  var regex = new RegularExpressionAttribute("^[A-Z]+$").GetValidationResult(Name, context);
  if (regex is not null) results.Add(regex);
 }
}
public static class Scenario {
 public static void Run() {
  var model = new Model();
  var generated = (IGeneratedQuarkValidation)model;
  var results = new List<ValidationResult>();
  for (int i = 0; i < 1000; i++) { model.Previous(results); generated.ValidateField("Name", results); }
  const int count = 10000;
  var before = GC.GetAllocatedBytesForCurrentThread();
  var start = Stopwatch.GetTimestamp();
  for (int i = 0; i < count; i++) model.Previous(results);
  var previousTime = Stopwatch.GetElapsedTime(start);
  var previousBytes = GC.GetAllocatedBytesForCurrentThread() - before;
  before = GC.GetAllocatedBytesForCurrentThread();
  start = Stopwatch.GetTimestamp();
  for (int i = 0; i < count; i++) generated.ValidateField("Name", results);
  var generatedTime = Stopwatch.GetElapsedTime(start);
  var generatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
  Console.WriteLine($"Validation ({count} calls): previous {previousBytes} bytes / {previousTime.TotalMilliseconds:F2} ms; generated {generatedBytes} bytes / {generatedTime.TotalMilliseconds:F2} ms");
  if (results.Count != 0 || generatedBytes > 1024 || previousBytes < count * 100) throw new Exception("Expected allocation-free warmed valid-field validation");
 }
}
""");
    }

    [Test]
    public void Custom_attributes_and_resources_remain_per_call_and_cache_is_concurrent()
    {
        ValidationParityTests.Run("""
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System.Globalization;
using Soenneker.Quark;
public static class Resources { public static string Message { get; set; } = "first {0}"; }
public sealed class StatefulAttribute : ValidationAttribute {
 private int calls;
 public override bool IsValid(object? value) => ++calls == 1;
}
public partial class Model {
 [Stateful] public string Custom { get; set; } = "value";
 [Required(ErrorMessageResourceType = typeof(Resources), ErrorMessageResourceName = "Message")]
 public string Localized { get; set; } = "";
 [Required, RegularExpression("^[A-Z]+$")] public string Code { get; set; } = "VALID";
 [RegularExpression(null!)] public string InvalidConfiguration { get; set; } = "";
 [RegularExpression("(?i)^i$")] public string CultureSensitive { get; set; } = "I";
}
public static class Scenario {
 public static void Run() {
  var model = (IGeneratedQuarkValidation)new Model();
  var results = new List<ValidationResult>();
  for (int i = 0; i < 3; i++) model.ValidateField("Custom", results);
  if (results.Count != 0) throw new Exception("Custom attribute was shared");
  model.ValidateField("Localized", results);
  Resources.Message = "second {0}";
  model.ValidateField("Localized", results);
  if (results[0].ErrorMessage != "first Localized" || results[1].ErrorMessage != "second Localized") throw new Exception("Resource was cached");
  Parallel.For(0, 1000, i => {
   var local = new List<ValidationResult>();
   model.ValidateField("Code", local);
   if (local.Count != 0) throw new Exception("Concurrent validation failed");
  });
  var culture = CultureInfo.CurrentCulture;
  try {
   foreach (var name in new[] { "en-US", "tr-TR", "en-US" }) {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
    results.Clear();
    model.ValidateField("CultureSensitive", results);
    var expected = new RegularExpressionAttribute("(?i)^i$").IsValid("I");
    if ((results.Count == 0) != expected) throw new Exception("Regex culture was cached incorrectly");
   }
  } finally { CultureInfo.CurrentCulture = culture; }
 }
}
""");
    }
}
