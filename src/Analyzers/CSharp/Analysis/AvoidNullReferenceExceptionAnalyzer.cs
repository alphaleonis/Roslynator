// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Roslynator.CSharp.Syntax;

namespace Roslynator.CSharp.Analysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AvoidNullReferenceExceptionAnalyzer : BaseDiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(DiagnosticDescriptors.AvoidNullReferenceException); }
        }

        public override void Initialize(AnalysisContext context)
        {
            base.Initialize(context);

            context.RegisterSyntaxNodeAction(AnalyzeAsExpression, SyntaxKind.AsExpression);
        }

        public static void Analyze(SyntaxNodeAnalysisContext context, in SimpleMemberInvocationExpressionInfo invocationInfo)
        {
            InvocationExpressionSyntax invocationExpression = invocationInfo.InvocationExpression;

            ExpressionSyntax expression = invocationExpression.WalkUpParentheses();

            if (GetAccessExpression(expression) == null)
                return;

            IMethodSymbol methodSymbol = context.SemanticModel.GetMethodSymbol(invocationExpression, context.CancellationToken);

            if (methodSymbol?.ReturnType.IsReferenceTypeOrNullableType() != true)
                return;

            methodSymbol = methodSymbol.ReducedFromOrSelf();

            INamedTypeSymbol containingType = methodSymbol.ContainingType;

            if (containingType == null)
                return;

            string methodName = methodSymbol.Name.Remove(methodSymbol.Name.Length - "OrDefault".Length);

            if (!ContainsComplementMethod(methodSymbol, containingType.GetMembers(methodName)))
                return;

            ReportDiagnostic(context, expression);

            static bool ContainsComplementMethod(IMethodSymbol methodSymbol, ImmutableArray<ISymbol> symbols)
            {
                foreach (ISymbol symbol in symbols)
                {
                    if (symbol.Kind != SymbolKind.Method)
                        continue;

                    var methodSymbol2 = (IMethodSymbol)symbol;

                    if (methodSymbol.TypeParameters.Length != methodSymbol2.TypeParameters.Length)
                        continue;

                    if (!SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, methodSymbol2.ReturnType))
                        continue;

                    ImmutableArray<IParameterSymbol> parameters = methodSymbol.Parameters;
                    ImmutableArray<IParameterSymbol> parameters2 = methodSymbol2.Parameters;

                    if (parameters.Length != parameters2.Length)
                        continue;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(parameters[i], parameters2[i]))
                            continue;
                    }

                    return true;
                }

                return false;
            }
        }

        private static void AnalyzeAsExpression(SyntaxNodeAnalysisContext context)
        {
            var asExpression = (BinaryExpressionSyntax)context.Node;

            if (asExpression.ContainsDiagnostics)
                return;

            ExpressionSyntax expression = asExpression.WalkUpParentheses();

            if (asExpression == expression)
                return;

            SemanticModel semanticModel = context.SemanticModel;
            CancellationToken cancellationToken = context.CancellationToken;

            if (asExpression.Left.IsKind(SyntaxKind.ThisExpression))
            {
                var interfaceSymbol = semanticModel.GetTypeSymbol(asExpression.Right, cancellationToken) as INamedTypeSymbol;

                if (interfaceSymbol?.TypeKind == TypeKind.Interface)
                {
                    ITypeSymbol thisTypeSymbol = semanticModel.GetTypeSymbol(asExpression.Left, cancellationToken);

                    if (thisTypeSymbol.Implements(interfaceSymbol, allInterfaces: true))
                        return;
                }
            }

            SyntaxNode topExpression = GetAccessExpression(expression)?.WalkUp(f => f.IsKind(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxKind.ElementAccessExpression,
                SyntaxKind.InvocationExpression,
                SyntaxKind.ParenthesizedExpression));

            if (topExpression == null)
                return;

            if (semanticModel
                .GetTypeSymbol(asExpression, cancellationToken)?
                .IsReferenceType != true)
            {
                return;
            }

            ITypeSymbol typeSymbol = semanticModel.GetTypeSymbol(topExpression, cancellationToken);

            if (typeSymbol == null)
                return;

            if (!typeSymbol.IsReferenceType && !typeSymbol.IsValueType)
                return;

            if (semanticModel.GetSymbol(topExpression, cancellationToken) is IMethodSymbol methodSymbol
                && methodSymbol.IsExtensionMethod)
            {
                return;
            }

            ReportDiagnostic(context, expression);
        }

        private static ExpressionSyntax GetAccessExpression(ExpressionSyntax expression)
        {
            SyntaxNode parent = expression.Parent;

            switch (parent?.Kind())
            {
                case SyntaxKind.SimpleMemberAccessExpression:
                    {
                        var memberAccessExpression = (MemberAccessExpressionSyntax)parent;

                        if (expression == memberAccessExpression.Expression)
                            return memberAccessExpression;

                        break;
                    }
                case SyntaxKind.ElementAccessExpression:
                    {
                        var elementAccess = (ElementAccessExpressionSyntax)parent;

                        if (expression == elementAccess.Expression)
                            return elementAccess;

                        break;
                    }
            }

            return null;
        }

        private static void ReportDiagnostic(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            DiagnosticHelpers.ReportDiagnostic(context,
                DiagnosticDescriptors.AvoidNullReferenceException,
                Location.Create(expression.SyntaxTree, new TextSpan(expression.Span.End, 1)));
        }
    }
}
