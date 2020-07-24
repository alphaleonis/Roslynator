// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslynator.CSharp.Syntax;

namespace Roslynator.CSharp.Analysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UseNullCoalescingAssignmentOperatorAnalyzer : BaseDiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(
                    DiagnosticDescriptors.UseNullCoalescingAssignmentOperator,
                    DiagnosticDescriptors.UseNullCoalescingAssignmentOperatorFadeOut);
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            base.Initialize(context);

            context.RegisterCompilationStartAction(startContext =>
            {
                if (startContext.IsAnalyzerSuppressed(DiagnosticDescriptors.UseNullCoalescingAssignmentOperator))
                    return;

                if (((CSharpCompilation)startContext.Compilation).LanguageVersion < LanguageVersion.CSharp8)
                    return;

                startContext.RegisterSyntaxNodeAction(AnalyzeCoalesceExpression, SyntaxKind.CoalesceExpression);
            });
        }

        private static void AnalyzeCoalesceExpression(SyntaxNodeAnalysisContext context)
        {
            var coalesceExpression = (BinaryExpressionSyntax)context.Node;

            BinaryExpressionInfo binaryExpressionInfo = SyntaxInfo.BinaryExpressionInfo(coalesceExpression, walkDownParentheses: false);

            if (!binaryExpressionInfo.Success)
                return;

            ExpressionSyntax right = binaryExpressionInfo.Right;

            if (!right.IsKind(SyntaxKind.ParenthesizedExpression))
                return;

            var parenthesizedExpression = (ParenthesizedExpressionSyntax)right;

            ExpressionSyntax expression = parenthesizedExpression.Expression;

            if (!expression.IsKind(SyntaxKind.SimpleAssignmentExpression))
                return;

            SimpleAssignmentExpressionInfo assignmentInfo = SyntaxInfo.SimpleAssignmentExpressionInfo((AssignmentExpressionSyntax)expression);

            if (!assignmentInfo.Success)
                return;

            if (!CSharpFactory.AreEquivalent(binaryExpressionInfo.Left, assignmentInfo.Left))
                return;

            DiagnosticHelpers.ReportDiagnostic(context, DiagnosticDescriptors.UseNullCoalescingAssignmentOperator, coalesceExpression);

            DiagnosticHelpers.ReportToken(context, DiagnosticDescriptors.UseNullCoalescingAssignmentOperatorFadeOut, parenthesizedExpression.OpenParenToken);
            DiagnosticHelpers.ReportNode(context, DiagnosticDescriptors.UseNullCoalescingAssignmentOperatorFadeOut, assignmentInfo.Left);
            DiagnosticHelpers.ReportToken(context, DiagnosticDescriptors.UseNullCoalescingAssignmentOperatorFadeOut, parenthesizedExpression.CloseParenToken);
        }
    }
}
