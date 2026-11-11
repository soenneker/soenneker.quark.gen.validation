using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Soenneker.Quark.Gen.Validation.BuildTasks;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class AsyncValidationTests
{
    [Test]
    public async Task Generated_async_annotations_receive_services_and_cancellation()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.Threading;
            using System.Threading.Tasks;
            using Soenneker.Quark;
            public sealed class RemoteAttribute : AsyncValidationAttribute {
                protected override ValidationResult? IsValid(object? value, ValidationContext context) => throw new InvalidOperationException();
                protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext context, CancellationToken token) {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    return new ValidationResult((string?)context.GetService(typeof(string)) ?? "missing service");
                }
            }
            public partial class Model { [Required, Remote] public string Name { get; set; } = "value"; }
            public sealed class Services : IServiceProvider { public object? GetService(Type type) => type == typeof(string) ? "remote failure" : null; }
            public static class Run {
                public static async Task Verify() {
                    IGeneratedQuarkValidation model = new Model();
                    if (!model.HasAsyncValidation("Name")) throw new Exception("Missing async marker");
                    var results = new List<ValidationResult>();
                    await model.ValidateFieldAsync("Name", results, new Services(), default);
                    if (results.Count != 1 || results[0].ErrorMessage != "remote failure") throw new Exception("Invalid async result");
                    try { model.ValidateField("Name", results); throw new Exception("Sync validation must fail"); } catch (InvalidOperationException) { }
                    try { await model.ValidateFieldAsync("Name", results, null, new CancellationToken(true)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
                    ((Model)model).Name = "";
                    results.Clear();
                    await model.ValidateFieldAsync("Name", results, new Services(), default);
                    if (results.Count != 1 || results[0].ErrorMessage == "remote failure") throw new Exception("Required must short circuit");
                }
            }
            """;
        var compilation = ValidationParityTests.Compile(source);
        string generated = ValidationEmitter.Emit(compilation);
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, new CSharpParseOptions(LanguageVersion.Preview)));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(output.ToArray());
        await (Task)assembly.GetType("Run")!.GetMethod("Verify")!.Invoke(null, null)!;
    }
}

