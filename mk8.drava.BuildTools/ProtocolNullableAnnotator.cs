using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mk8.Drava.BuildTools;

internal sealed class ProtocolNullableAnnotator(HashSet<string> messages) : CSharpSyntaxRewriter
{
    private static TypeSyntax Nullable(TypeSyntax type) => type is NullableTypeSyntax ? type : SyntaxFactory.NullableType(type.WithoutTrailingTrivia()).WithTrailingTrivia(type.GetTrailingTrivia());

    private bool IsMessage(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax name => messages.Contains(name.Identifier.ValueText),
        QualifiedNameSyntax qualified => IsMessage(qualified.Right),
        AliasQualifiedNameSyntax alias => IsMessage(alias.Name),
        _ => false,
    };

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node) => base.VisitPropertyDeclaration(IsMessage(node.Type) ? node.WithType(Nullable(node.Type)) : node);

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var type = node.Declaration.Type;
        return base.VisitFieldDeclaration(IsMessage(type) || type.ToString() is "object" or "pb::UnknownFieldSet"
            ? node.WithDeclaration(node.Declaration.WithType(Nullable(type))) : node);
    }

    public override SyntaxNode? VisitParameter(ParameterSyntax node)
    {
        if (node.Type is not { } type) return base.VisitParameter(node);
        var method = node.Parent?.Parent as MethodDeclarationSyntax;
        var nullable = node.Default?.Value.IsKind(SyntaxKind.NullLiteralExpression) == true
            || string.Equals(node.Identifier.ValueText, "serviceImpl", StringComparison.Ordinal)
            || string.Equals(node.Identifier.ValueText, "other", StringComparison.Ordinal) && method is not null
                && method.Identifier.ValueText is "Equals" or "MergeFrom" && (IsMessage(type) || type.ToString() is "object");
        return base.VisitParameter(nullable ? node.WithType(Nullable(type)) : node);
    }
}
