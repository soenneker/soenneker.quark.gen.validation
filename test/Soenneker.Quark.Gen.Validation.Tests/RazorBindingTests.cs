using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Soenneker.Quark.Gen.Validation.BuildTasks;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class RazorBindingTests
{
    private const string Support = """
        namespace Microsoft.AspNetCore.Components.Forms {
            public readonly record struct FieldIdentifier(object Model, string FieldName);
        }
        namespace Soenneker.Quark {
            public interface IValidationFieldBinding { }
            public class Input<T> : IValidationFieldBinding { }
        }
        public class Builder {
            public void OpenComponent<T>(int sequence) { }
            public void AddComponentParameter(int sequence, string name, object value) { }
            public void CloseComponent() { }
        }
        public static class Helpers {
            public static object CreateInferredEventCallback<T>(object owner, System.Action<T> setter, T value) => setter;
        }
        public class Model {
            public Model Child => this;
            public Model[] Items => new[] { this };
            public string Name { get; set; } = "";
        }
        """;

    [Test]
    [Arguments("model.Name", "model")]
    [Arguments("model.Child.Name", "model.Child")]
    [Arguments("model.Items[0].Name", "model.Items[0]")]
    public void Ordinary_and_nested_bindings_emit_direct_field_identifiers(string binding, string owner)
    {
        string source = Support + $$"""
            public class Component {
                Model model = new();
                void Render(Builder builder) {
                    builder.OpenComponent<Soenneker.Quark.Input<string>>(0);
                    builder.AddComponentParameter(1, "ValueChanged", Helpers.CreateInferredEventCallback(this, v => {{binding}} = v, {{binding}}));
                    builder.CloseComponent();
                }
            }
            """;
        string generated = Rewrite(source);
        if (!generated.Contains("FieldIdentifier(" + owner + ", \"Name\")")) throw new Exception(generated);
    }

    [Test]
    public void Generic_inference_helpers_receive_the_callers_model_field()
    {
        string generated = Rewrite(Support + """
            public class Component {
                Model model = new();
                void Render(Builder builder) {
                    TypeInference.CreateInput<string>(builder, Helpers.CreateInferredEventCallback(this, v => model.Name = v, model.Name));
                }
            }
            public static class TypeInference {
                public static void CreateInput<T>(Builder builder, object callback) {
                    builder.OpenComponent<Soenneker.Quark.Input<T>>(0);
                    builder.AddComponentParameter(1, "ValueChanged", callback);
                    builder.CloseComponent();
                }
            }
            """);
        if (!generated.Contains("FieldIdentifier(model, \"Name\")") || !generated.Contains("__quarkValidationField.HasValue"))
            throw new Exception(generated);
    }

    [Test]
    public void Explicit_field_bypasses_unsupported_indexer_inference()
    {
        string generated = Rewrite(Support + """
            public class Component {
                string[] values = [""];
                void Render(Builder builder) {
                    builder.OpenComponent<Soenneker.Quark.Input<string>>(0);
                    builder.AddComponentParameter(1, "ValidationField", new Microsoft.AspNetCore.Components.Forms.FieldIdentifier(this, "Values"));
                    builder.AddComponentParameter(2, "ValueChanged", Helpers.CreateInferredEventCallback(this, v => values[0] = v, values[0]));
                    builder.CloseComponent();
                }
            }
            """);
        if (generated.Contains("int.MaxValue")) throw new Exception("Explicit field was overridden");
    }

    private static string Rewrite(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Bindings", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        SyntaxTree rewritten = RazorBindingRewriter.Rewrite(compilation, tree);
        var errors = compilation.ReplaceSyntaxTree(tree, rewritten).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new Exception(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        return rewritten.ToString();
    }

    [Test]
    public void Secondary_state_bindings_do_not_replace_the_value_field()
    {
        string generated = Rewrite(Support + """
            public class Component {
                Model model = new();
                bool open;
                void Render(Builder builder) {
                    builder.OpenComponent<Soenneker.Quark.Input<string>>(0);
                    builder.AddComponentParameter(1, "ValueChanged", Helpers.CreateInferredEventCallback(this, v => model.Name = v, model.Name));
                    builder.AddComponentParameter(2, "OpenChanged", Helpers.CreateInferredEventCallback(this, v => open = v, open));
                    builder.CloseComponent();
                }
            }
            """);
        if (!generated.Contains("FieldIdentifier(model, \"Name\")") || generated.Contains("FieldIdentifier(this, \"open\")"))
            throw new Exception(generated);
    }

    [Test]
    public void Unsupported_value_binding_reports_a_diagnostic_instead_of_guessing_a_field()
    {
        try
        {
            Rewrite(Support + """
                public class Component {
                    string[] values = [""];
                    void Render(Builder builder) {
                        builder.OpenComponent<Soenneker.Quark.Input<string>>(0);
                        builder.AddComponentParameter(1, "ValueChanged", Helpers.CreateInferredEventCallback(this, v => values[0] = v, values[0]));
                        builder.CloseComponent();
                    }
                }
                """);
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith("QV003:")) { return; }
        throw new Exception("Unsupported binding did not produce QV003");
    }
}
