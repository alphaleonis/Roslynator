// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslynator.CodeFixes;

namespace Roslynator.CSharp.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CoalesceExpressionCodeFixProvider))]
    [Shared]
    public class CoalesceExpressionCodeFixProvider : BaseCodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(DiagnosticIdentifiers.UseNullCoalescingAssignmentOperator); }
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = await context.GetSyntaxRootAsync().ConfigureAwait(false);

            if (!TryFindFirstAncestorOrSelf(root, context.Span, out BinaryExpressionSyntax coalesceExpression))
                return;

            Document document = context.Document;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                switch (diagnostic.Id)
                {
                    case DiagnosticIdentifiers.UseNullCoalescingAssignmentOperator:
                        {
                            CodeAction codeAction = CodeAction.Create(
                                "Use '??=' operator",
                                ct => UseNullCoalescingAssignmentOperatorAsync(document, coalesceExpression, ct),
                                GetEquivalenceKey(diagnostic));

                            context.RegisterCodeFix(codeAction, diagnostic);
                            break;
                        }
                }
            }
        }

        private static async Task<Document> UseNullCoalescingAssignmentOperatorAsync(
            Document document,
            BinaryExpressionSyntax coalesceExpression,
            CancellationToken cancellationToken)
        {
            var parenthesizedExpression = (ParenthesizedExpressionSyntax)coalesceExpression.Right;

            var simpleAssignment = (AssignmentExpressionSyntax)parenthesizedExpression.Expression;

            AssignmentExpressionSyntax assignmentExpression = CSharpFactory.CoalesceAssignmentExpression(
                coalesceExpression.Left,
                SyntaxFactory.Token(coalesceExpression.OperatorToken.LeadingTrivia, SyntaxKind.QuestionQuestionEqualsToken, coalesceExpression.OperatorToken.TrailingTrivia),
                simpleAssignment.Right.AppendToTrailingTrivia(parenthesizedExpression.GetTrailingTrivia()));

            return await document.ReplaceNodeAsync(coalesceExpression, assignmentExpression, cancellationToken).ConfigureAwait(false);
        }
    }
}
