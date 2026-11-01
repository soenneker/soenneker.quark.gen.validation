using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

/// <summary>Adds statically resolved model fields to SDK-generated Quark input bindings.</summary>
public sealed class RazorBindingRewriter : CSharpSyntaxRewriter
{
    private const string FieldType = "global::Microsoft.AspNetCore.Components.Forms.FieldIdentifier";
    private readonly SemanticModel _model;
    private readonly Dictionary<IMethodSymbol, int[]> _helpers = new(SymbolEqualityComparer.Default);

    private static bool IsValueCallback(string? name) => name is "ValueChanged" or "CheckedChanged" or "SelectedValueChanged" or
        "DateChanged" or "DateOnlyChanged" or "SelectedDateChanged" or "SelectedDateTimeChanged" or
        "SliderValueChanged" or "ValuesChanged" or "TextChanged";

    private RazorBindingRewriter(SemanticModel model)
    {
        _model = model;
        foreach (var method in model.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            if (method.Parent is ClassDeclarationSyntax { Identifier.ValueText: "TypeInference" } &&
                method.Body?.Statements.OfType<ExpressionStatementSyntax>().Any(s => IsInputOpen(s.Expression)) == true &&
                model.GetDeclaredSymbol(method) is { } symbol)
            {
                var attributes = method.Body!.Statements.OfType<ExpressionStatementSyntax>()
                    .Select(s => s.Expression).OfType<InvocationExpressionSyntax>()
                    .Where(c => c.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddComponentParameter" or "AddAttribute" } && c.ArgumentList.Arguments.Count >= 3).ToArray();
                if (attributes.Any(a => AttributeName(a.ArgumentList.Arguments[1].Expression) == "ValidationField")) continue;
                _helpers.Add(symbol, attributes.Where(a => IsValueCallback(AttributeName(a.ArgumentList.Arguments[1].Expression)))
                    .SelectMany(a => a.ArgumentList.Arguments[2].Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                    .Select(n => model.GetSymbolInfo(n).Symbol).OfType<IParameterSymbol>().Select(p => p.Ordinal).Distinct().ToArray());
            }
    }

    /// <summary>Rewrites one generated Razor tree using the complete consumer compilation.</summary>
    public static SyntaxTree Rewrite(CSharpCompilation compilation, SyntaxTree tree)
    {
        var rewriter = new RazorBindingRewriter(compilation.GetSemanticModel(tree));
        return tree.WithRootAndOptions(rewriter.Visit(tree.GetRoot())!, tree.Options);
    }

    private bool IsInputOpen(ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax
            { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "OpenComponent" } name } })
            return false;
        return _model.GetTypeInfo(name.TypeArgumentList.Arguments[0]).Type is INamedTypeSymbol type &&
            type.AllInterfaces.Any(i => i.ToDisplayString() == "Soenneker.Quark.IValidationFieldBinding");
    }

    private ExpressionSyntax? BoundField(SyntaxNode node)
    {
        if (node is InvocationExpressionSyntax attribute && attribute.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddComponentParameter" or "AddAttribute" } &&
            (attribute.ArgumentList.Arguments.Count < 3 || !IsValueCallback(AttributeName(attribute.ArgumentList.Arguments[1].Expression)))) return null;
        var bindings = node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateInferredEventCallback" })
            .ToArray();
        if (bindings.Length == 0) return null;
        if (bindings.Length > 1)
            throw new InvalidOperationException("QV004: Multiple bindings on one Quark input require an explicit ValidationField.");
        ExpressionSyntax value = bindings[0].ArgumentList.Arguments[^1].Expression;
        while (value is ParenthesizedExpressionSyntax parentheses) value = parentheses.Expression;
        ISymbol? symbol = _model.GetSymbolInfo(value).Symbol;
        if (symbol is not (IPropertySymbol or IFieldSymbol) || symbol.IsStatic)
            throw new InvalidOperationException("QV003: Cannot infer a Quark validation field for '" + value.WithoutTrivia() +
                "' at " + value.GetLocation().GetMappedLineSpan() + " (" + (symbol?.Kind.ToString() ?? _model.GetSymbolInfo(value).CandidateReason.ToString()) + "). Supply ValidationField explicitly for this binding.");
        ExpressionSyntax receiver = value is MemberAccessExpressionSyntax member ? member.Expression : SyntaxFactory.ThisExpression();
        // Preserve the actual owner of nested properties and collection-item properties.
        return SyntaxFactory.ParseExpression("new " + FieldType + "(" + receiver.WithoutTrivia() + ", " +
            SymbolDisplay.FormatLiteral(symbol.Name, true) + ")");
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
        ExpressionSyntax? field = null;
        if (symbol is not null && _helpers.TryGetValue(symbol.OriginalDefinition, out var positions))
        {
            var fields = positions.Select(p => BoundField(node.ArgumentList.Arguments[p].Expression)).Where(f => f is not null).ToArray();
            if (fields.Length > 1) throw new InvalidOperationException("QV004: Multiple value bindings on one Quark input require an explicit ValidationField.");
            field = fields.SingleOrDefault();
        }
        var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        return field is null ? rewritten : rewritten.AddArgumentListArguments(SyntaxFactory.Argument(field));
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var rewritten = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        if (_model.GetDeclaredSymbol(node) is not { } symbol || !_helpers.ContainsKey(symbol)) return rewritten;
        return rewritten.AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("__quarkValidationField"))
            .WithType(SyntaxFactory.ParseTypeName(FieldType + "?"))
            .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
    }

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var rewritten = (BlockSyntax)base.VisitBlock(node)!;
        var statements = new List<StatementSyntax>();
        var stack = new Stack<(bool Input, string Builder, List<ExpressionSyntax> Attributes)>();
        bool helper = node.Parent is MethodDeclarationSyntax method && _model.GetDeclaredSymbol(method) is { } symbol && _helpers.ContainsKey(symbol);
        for (int i = 0; i < node.Statements.Count; i++)
        {
            if (node.Statements[i] is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call } &&
                call.Expression is MemberAccessExpressionSyntax access)
            {
                string name = access.Name.Identifier.ValueText;
                if (name == "OpenComponent") stack.Push((IsInputOpen(call), access.Expression.ToString(), []));
                else if (stack.TryPeek(out var current) && current.Builder == access.Expression.ToString())
                {
                    if (name is "AddComponentParameter" or "AddAttribute") current.Attributes.Add(call);
                    else if (name == "CloseComponent")
                    {
                        stack.Pop();
                        bool explicitField = current.Attributes.OfType<InvocationExpressionSyntax>().Any(a =>
                            a.ArgumentList.Arguments.Count > 1 && AttributeName(a.ArgumentList.Arguments[1].Expression) == "ValidationField");
                        if (current.Input && !explicitField)
                        {
                            var fields = helper ? [] : current.Attributes.Select(BoundField).Where(f => f is not null).ToArray();
                            if (fields.Length > 1) throw new InvalidOperationException("QV004: Multiple bindings on one Quark input require an explicit ValidationField.");
                            string? field = helper ? "__quarkValidationField" : fields.SingleOrDefault()?.ToString();
                            if (field is not null)
                            {
                                string statement = current.Builder + ".AddComponentParameter(int.MaxValue, \"ValidationField\", " + field + ");";
                                if (helper) statement = "if (__quarkValidationField.HasValue) " + statement;
                                statements.Add(SyntaxFactory.ParseStatement(statement).WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                                    .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));
                            }
                        }
                    }
                }
            }
            statements.Add(rewritten.Statements[i]);
        }
        return rewritten.WithStatements(SyntaxFactory.List(statements));
    }

    private static string? AttributeName(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal => literal.Token.ValueText,
        InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: var args }
            when args.Count == 1 && args[0].Expression is MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null
    };
}
